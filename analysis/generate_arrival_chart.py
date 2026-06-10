import json
import math
import re
from pathlib import Path

import pandas as pd


SOURCE = Path(
    r"F:/深圳北数据/集团需要调研数据（综控）/2.2每趟车次的旅客上下车客票信息/2.2每趟车次的旅客上下车客票信息/20260301/2 到达客流统计.xlsx"
)
OUTPUT_DIR = Path(r"D:/AAA Learning/AAA本科毕业设计/代码")
OUTPUT_HTML = OUTPUT_DIR / "shenzhenbei_arrival_flow_20260301.html"
TARGET_STATION = "深圳北"
SHEET_NAME = "页面1_1"


def clean(value):
    if pd.isna(value):
        return ""
    return str(value).replace("\u00a0", " ").strip()


def parse_time(value):
    text = clean(value)
    if not re.match(r"^\d{1,2}:\d{2}$", text):
        return None
    hour, minute = map(int, text.split(":"))
    if not (0 <= hour <= 23 and 0 <= minute <= 59):
        return None
    return hour * 60 + minute


def time_label(minutes):
    if minutes >= 24 * 60:
        return "24:00"
    return f"{minutes // 60:02d}:{minutes % 60:02d}"


def locate_station_total_column(raw):
    station_row = raw.iloc[4]
    seat_row = raw.iloc[6]
    station_col = None
    for col, value in station_row.items():
        if clean(value) == TARGET_STATION:
            station_col = int(col)
            break
    if station_col is None:
        raise ValueError(f"未找到站点列：{TARGET_STATION}")

    next_station_col = raw.shape[1]
    for col in range(station_col + 1, raw.shape[1]):
        if clean(station_row.iat[col]):
            next_station_col = col
            break

    total_col = None
    for col in range(station_col, next_station_col):
        if clean(seat_row.iat[col]) == "小计":
            total_col = col
            break
    if total_col is None:
        raise ValueError(f"未找到 {TARGET_STATION} 分组下的“小计”列")
    return station_col, total_col


def build_records(raw, total_col):
    records = []
    for idx in range(7, len(raw)):
        train = clean(raw.iat[idx, 2])
        route = clean(raw.iat[idx, 3])
        arrival = clean(raw.iat[idx, 5])
        minutes = parse_time(arrival)
        pax = pd.to_numeric(pd.Series([raw.iat[idx, total_col]]), errors="coerce").iloc[0]

        if not train or not route or minutes is None or pd.isna(pax) or pax <= 0:
            continue

        bin_start = minutes - minutes % 15
        records.append(
            {
                "row": int(idx + 1),
                "train": train,
                "route": route,
                "arrival": time_label(minutes),
                "pax": int(round(float(pax))),
                "binStart": bin_start,
            }
        )
    return records


def aggregate(records):
    bins = []
    by_bin = {m: [] for m in range(0, 24 * 60, 15)}
    for record in records:
        by_bin[record["binStart"]].append(record)

    for start in range(0, 24 * 60, 15):
        trains = sorted(by_bin[start], key=lambda r: (r["arrival"], r["train"]))
        total = sum(r["pax"] for r in trains)
        bins.append(
            {
                "start": start,
                "end": start + 15,
                "label": f"{time_label(start)}-{time_label(start + 15)}",
                "total": total,
                "trains": trains,
            }
        )
    return bins


def make_html(bins, records, station_col, total_col):
    total_pax = sum(r["pax"] for r in records)
    peak = max(bins, key=lambda b: b["total"])
    active_bins = sum(1 for b in bins if b["total"] > 0)
    max_total = max([b["total"] for b in bins] or [0])
    payload = {
        "bins": bins,
        "summary": {
            "date": "2026-03-01",
            "station": TARGET_STATION,
            "totalPax": total_pax,
            "trainCount": len(records),
            "activeBins": active_bins,
            "peakLabel": peak["label"],
            "peakTotal": peak["total"],
            "maxTotal": max_total,
            "stationCol": station_col + 1,
            "totalCol": total_col + 1,
        },
    }
    data_json = json.dumps(payload, ensure_ascii=False)

    return f"""<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>深圳北到达客流 15 分钟粒度</title>
  <style>
    :root {{
      color-scheme: light;
      --bg: #f7f8fb;
      --panel: #ffffff;
      --ink: #17202a;
      --muted: #637083;
      --grid: #dce3ea;
      --line: #0f766e;
      --line-soft: rgba(15, 118, 110, 0.14);
      --accent: #c2410c;
      --shadow: rgba(17, 24, 39, 0.12);
    }}
    * {{ box-sizing: border-box; }}
    body {{
      margin: 0;
      min-height: 100vh;
      font-family: "Microsoft YaHei", "Segoe UI", Arial, sans-serif;
      background: var(--bg);
      color: var(--ink);
    }}
    main {{
      width: min(1440px, 100%);
      margin: 0 auto;
      padding: 24px;
    }}
    .topbar {{
      display: flex;
      align-items: end;
      justify-content: space-between;
      gap: 18px;
      margin-bottom: 16px;
    }}
    h1 {{
      margin: 0;
      font-size: clamp(22px, 2.4vw, 34px);
      font-weight: 760;
      letter-spacing: 0;
    }}
    .subtitle {{
      margin-top: 6px;
      color: var(--muted);
      font-size: 14px;
    }}
    .stats {{
      display: grid;
      grid-template-columns: repeat(4, minmax(116px, 1fr));
      gap: 10px;
      min-width: min(640px, 100%);
    }}
    .stat {{
      background: var(--panel);
      border: 1px solid #e5eaf0;
      border-radius: 8px;
      padding: 12px 14px;
      box-shadow: 0 8px 22px var(--shadow);
    }}
    .stat span {{
      display: block;
      color: var(--muted);
      font-size: 12px;
      white-space: nowrap;
    }}
    .stat strong {{
      display: block;
      margin-top: 6px;
      font-size: 20px;
      letter-spacing: 0;
    }}
    .chart-wrap {{
      position: relative;
      background: var(--panel);
      border: 1px solid #e1e7ee;
      border-radius: 8px;
      padding: 16px 16px 8px;
      box-shadow: 0 12px 30px var(--shadow);
      overflow: hidden;
    }}
    svg {{
      display: block;
      width: 100%;
      height: min(68vh, 680px);
      min-height: 430px;
    }}
    .axis text {{
      fill: var(--muted);
      font-size: 13px;
    }}
    .grid line {{
      stroke: var(--grid);
      stroke-width: 1;
    }}
    .axis-line {{
      stroke: #9aa8b7;
      stroke-width: 1;
    }}
    .flow-area {{
      fill: var(--line-soft);
    }}
    .flow-line {{
      fill: none;
      stroke: var(--line);
      stroke-width: 3;
      stroke-linejoin: round;
      stroke-linecap: round;
    }}
    .hover-band {{
      fill: transparent;
      cursor: crosshair;
    }}
    .hover-band:hover {{
      fill: rgba(15, 118, 110, 0.045);
    }}
    .focus-line {{
      stroke: var(--accent);
      stroke-width: 1.5;
      stroke-dasharray: 5 5;
      pointer-events: none;
      opacity: 0;
    }}
    .focus-dot {{
      fill: var(--accent);
      stroke: #fff;
      stroke-width: 3;
      pointer-events: none;
      opacity: 0;
    }}
    .tooltip {{
      position: fixed;
      z-index: 10;
      min-width: 280px;
      max-width: min(420px, calc(100vw - 32px));
      max-height: min(520px, calc(100vh - 32px));
      overflow: auto;
      padding: 12px 14px;
      border-radius: 8px;
      background: rgba(255, 255, 255, 0.98);
      border: 1px solid #d6dee7;
      box-shadow: 0 18px 42px rgba(15, 23, 42, 0.18);
      opacity: 0;
      pointer-events: none;
      transform: translate(-50%, -105%);
    }}
    .tooltip h2 {{
      margin: 0 0 8px;
      font-size: 16px;
    }}
    .tooltip .total {{
      color: var(--line);
      font-weight: 760;
      margin-bottom: 8px;
    }}
    .tooltip table {{
      width: 100%;
      border-collapse: collapse;
      font-size: 12px;
    }}
    .tooltip th, .tooltip td {{
      padding: 5px 4px;
      border-bottom: 1px solid #edf1f5;
      text-align: left;
      vertical-align: top;
    }}
    .tooltip th:last-child,
    .tooltip td:last-child {{
      text-align: right;
      white-space: nowrap;
    }}
    .empty {{
      color: var(--muted);
      font-size: 13px;
    }}
    .footnote {{
      color: var(--muted);
      font-size: 12px;
      margin: 10px 4px 0;
    }}
    @media (max-width: 900px) {{
      main {{ padding: 14px; }}
      .topbar {{ display: block; }}
      .stats {{ grid-template-columns: repeat(2, minmax(0, 1fr)); margin-top: 14px; }}
      svg {{ min-height: 480px; }}
    }}
  </style>
</head>
<body>
  <main>
    <section class="topbar">
      <div>
        <h1>深圳北到达客流曲线</h1>
        <div class="subtitle">2026-03-01，每 15 分钟聚合一次，纵轴为到达深圳北站客流量</div>
      </div>
      <div class="stats" aria-label="统计摘要">
        <div class="stat"><span>总到达客流</span><strong id="totalPax"></strong></div>
        <div class="stat"><span>涉及车次</span><strong id="trainCount"></strong></div>
        <div class="stat"><span>峰值时间段</span><strong id="peakLabel"></strong></div>
        <div class="stat"><span>峰值客流</span><strong id="peakTotal"></strong></div>
      </div>
    </section>

    <section class="chart-wrap">
      <svg id="chart" viewBox="0 0 1200 640" role="img" aria-label="深圳北站 15 分钟到达客流折线图"></svg>
      <div id="tooltip" class="tooltip" aria-hidden="true"></div>
      <div class="footnote">数据来源：2 到达客流统计.xlsx；使用“深圳北”分组下“小计”列，按“下车时间”分箱。</div>
    </section>
  </main>

  <script>
    const data = {data_json};
    const bins = data.bins;
    const summary = data.summary;
    const fmt = new Intl.NumberFormat("zh-CN");

    document.getElementById("totalPax").textContent = fmt.format(summary.totalPax);
    document.getElementById("trainCount").textContent = fmt.format(summary.trainCount);
    document.getElementById("peakLabel").textContent = summary.peakLabel;
    document.getElementById("peakTotal").textContent = fmt.format(summary.peakTotal);

    const svg = document.getElementById("chart");
    const tip = document.getElementById("tooltip");
    const NS = "http://www.w3.org/2000/svg";
    const width = 1200;
    const height = 640;
    const margin = {{ left: 76, right: 34, top: 36, bottom: 78 }};
    const plotW = width - margin.left - margin.right;
    const plotH = height - margin.top - margin.bottom;
    const yMax = niceMax(Math.max(10, summary.maxTotal));
    const stepX = plotW / (bins.length - 1);

    function el(name, attrs = {{}}, parent = svg) {{
      const node = document.createElementNS(NS, name);
      for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, value);
      parent.appendChild(node);
      return node;
    }}
    function x(i) {{ return margin.left + i * stepX; }}
    function y(value) {{ return margin.top + plotH - (value / yMax) * plotH; }}
    function niceMax(value) {{
      const exp = Math.floor(Math.log10(value));
      const base = Math.pow(10, exp);
      const n = value / base;
      const nice = n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10;
      return nice * base;
    }}
    function pathLine() {{
      return bins.map((b, i) => `${{i === 0 ? "M" : "L"}} ${{x(i).toFixed(2)}} ${{y(b.total).toFixed(2)}}`).join(" ");
    }}
    function pathArea() {{
      const baseY = y(0);
      return `M ${{x(0)}} ${{baseY}} ` + bins.map((b, i) => `L ${{x(i).toFixed(2)}} ${{y(b.total).toFixed(2)}}`).join(" ") + ` L ${{x(bins.length - 1)}} ${{baseY}} Z`;
    }}

    const grid = el("g", {{ class: "grid" }});
    const axis = el("g", {{ class: "axis" }});

    const yTicks = 5;
    for (let t = 0; t <= yTicks; t++) {{
      const value = (yMax / yTicks) * t;
      const yy = y(value);
      el("line", {{ x1: margin.left, y1: yy, x2: width - margin.right, y2: yy }}, grid);
      const label = el("text", {{ x: margin.left - 12, y: yy + 4, "text-anchor": "end" }}, axis);
      label.textContent = fmt.format(Math.round(value));
    }}
    el("line", {{ class: "axis-line", x1: margin.left, y1: y(0), x2: width - margin.right, y2: y(0) }});
    el("line", {{ class: "axis-line", x1: margin.left, y1: margin.top, x2: margin.left, y2: y(0) }});

    for (let hour = 0; hour <= 24; hour += 2) {{
      const i = Math.min(bins.length - 1, hour * 4);
      const xx = margin.left + Math.min(hour * 60, 23 * 60 + 45) / (23 * 60 + 45) * plotW;
      el("line", {{ x1: xx, y1: y(0), x2: xx, y2: y(0) + 6, stroke: "#9aa8b7" }});
      const label = el("text", {{ x: xx, y: y(0) + 28, "text-anchor": hour === 0 ? "start" : hour === 24 ? "end" : "middle" }}, axis);
      label.textContent = hour === 24 ? "24:00" : `${{String(hour).padStart(2, "0")}}:00`;
    }}

    const yTitle = el("text", {{ x: 20, y: margin.top + 12, transform: `rotate(-90 20 ${{margin.top + 12}})`, "text-anchor": "end", fill: "#637083", "font-size": 13 }});
    yTitle.textContent = "客流量";
    const xTitle = el("text", {{ x: width / 2, y: height - 20, "text-anchor": "middle", fill: "#637083", "font-size": 13 }});
    xTitle.textContent = "到达时间段";

    el("path", {{ class: "flow-area", d: pathArea() }});
    el("path", {{ class: "flow-line", d: pathLine() }});

    bins.forEach((b, i) => {{
      if (b.total > 0) {{
        el("circle", {{ cx: x(i), cy: y(b.total), r: 3.2, fill: "#0f766e", opacity: 0.82 }});
      }}
    }});

    const focusLine = el("line", {{ class: "focus-line", y1: margin.top, y2: y(0) }});
    const focusDot = el("circle", {{ class: "focus-dot", r: 6 }});

    const overlays = el("g");
    bins.forEach((b, i) => {{
      const bandX = i === 0 ? margin.left - stepX / 2 : x(i) - stepX / 2;
      const bandW = i === bins.length - 1 ? stepX : stepX;
      const rect = el("rect", {{
        class: "hover-band",
        x: bandX,
        y: margin.top,
        width: bandW,
        height: plotH,
        "data-index": i
      }}, overlays);
      rect.addEventListener("mousemove", (event) => showTooltip(event, b, i));
      rect.addEventListener("mouseleave", hideTooltip);
    }});

    function trainRows(trains) {{
      if (!trains.length) return '<div class="empty">该时间段无到达客流记录</div>';
      return `<table>
        <thead><tr><th>到达</th><th>车次 / 运行区间</th><th>客流</th></tr></thead>
        <tbody>${{trains.map(r => `<tr><td>${{r.arrival}}</td><td><strong>${{r.train}}</strong><br>${{r.route}}</td><td>${{fmt.format(r.pax)}}</td></tr>`).join("")}}</tbody>
      </table>`;
    }}
    function showTooltip(event, b, i) {{
      focusLine.setAttribute("x1", x(i));
      focusLine.setAttribute("x2", x(i));
      focusLine.style.opacity = 1;
      focusDot.setAttribute("cx", x(i));
      focusDot.setAttribute("cy", y(b.total));
      focusDot.style.opacity = 1;

      tip.innerHTML = `<h2>${{b.label}}</h2>
        <div class="total">到达客流：${{fmt.format(b.total)}} 人，车次：${{b.trains.length}} 趟</div>
        ${{trainRows(b.trains)}}`;
      tip.style.opacity = 1;
      tip.setAttribute("aria-hidden", "false");

      const pad = 14;
      let left = event.clientX;
      let top = event.clientY - 12;
      tip.style.left = `${{left}}px`;
      tip.style.top = `${{top}}px`;

      requestAnimationFrame(() => {{
        const box = tip.getBoundingClientRect();
        if (box.left < pad) left += pad - box.left;
        if (box.right > window.innerWidth - pad) left -= box.right - (window.innerWidth - pad);
        if (box.top < pad) top = event.clientY + 22;
        tip.style.left = `${{left}}px`;
        tip.style.top = `${{top}}px`;
      }});
    }}
    function hideTooltip() {{
      tip.style.opacity = 0;
      tip.setAttribute("aria-hidden", "true");
      focusLine.style.opacity = 0;
      focusDot.style.opacity = 0;
    }}
  </script>
</body>
</html>
"""


def main():
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    raw = pd.read_excel(SOURCE, sheet_name=SHEET_NAME, header=None, dtype=object)
    station_col, total_col = locate_station_total_column(raw)
    records = build_records(raw, total_col)
    bins = aggregate(records)
    html = make_html(bins, records, station_col, total_col)
    OUTPUT_HTML.write_text(html, encoding="utf-8")

    summary = {
        "output": str(OUTPUT_HTML),
        "records": len(records),
        "totalPax": sum(r["pax"] for r in records),
        "nonZeroBins": sum(1 for b in bins if b["total"] > 0),
        "peak": max(bins, key=lambda b: b["total"]),
        "stationCol1Based": station_col + 1,
        "totalCol1Based": total_col + 1,
    }
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
