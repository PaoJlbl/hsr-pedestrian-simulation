import json
import re
from pathlib import Path


SOURCE_DIR = Path(r"D:/AAA Learning/Unity/Shenzhenbei_wcx/Assets/results/crowd_stable_60agents_v1")
LOG_PATH = SOURCE_DIR / "LOG.txt"
OUTPUT_DIR = Path(r"D:/AAA Learning/CODEX")
OUTPUT_HTML = OUTPUT_DIR / "crowd_stable_60agents_v1_training_curve.html"


LOG_PATTERN = re.compile(
    r"\[INFO\]\s+(?P<behavior>[^.]+)\.\s+Step:\s+(?P<step>\d+)\.\s+"
    r"Time Elapsed:\s+(?P<elapsed>[-\d.]+)\s+s\.\s+"
    r"Mean Reward:\s+(?P<mean>[-\d.]+)\.\s+"
    r"Std of Reward:\s+(?P<std>[-\d.]+)\."
)


def parse_log():
    text = LOG_PATH.read_text(encoding="utf-8", errors="replace")
    points = []
    for match in LOG_PATTERN.finditer(text):
        step = int(match.group("step"))
        elapsed = float(match.group("elapsed"))
        points.append(
            {
                "behavior": match.group("behavior"),
                "step": step,
                "stepLabel": f"{step:,}",
                "elapsed": elapsed,
                "elapsedLabel": format_elapsed(elapsed),
                "meanReward": float(match.group("mean")),
                "stdReward": float(match.group("std")),
            }
        )
    if not points:
        raise ValueError(f"没有从日志中解析到训练曲线数据：{LOG_PATH}")
    return points


def parse_checkpoints():
    checkpoints = []
    for path in sorted((SOURCE_DIR / "PedestrianCrowd").glob("PedestrianCrowd-*.onnx")):
        match = re.search(r"-(\d+)\.onnx$", path.name)
        if match:
            checkpoints.append({"step": int(match.group(1)), "file": path.name})
    return checkpoints


def format_elapsed(seconds):
    total = int(round(seconds))
    minutes, sec = divmod(total, 60)
    hours, minute = divmod(minutes, 60)
    if hours:
        return f"{hours}h {minute}m {sec}s"
    return f"{minute}m {sec}s"


def make_html(points, checkpoints):
    first = points[0]
    last = points[-1]
    best = max(points, key=lambda p: p["meanReward"])
    worst = min(points, key=lambda p: p["meanReward"])
    summary = {
        "runName": SOURCE_DIR.name,
        "behavior": first["behavior"],
        "pointCount": len(points),
        "firstStep": first["step"],
        "lastStep": last["step"],
        "lastMeanReward": last["meanReward"],
        "lastStdReward": last["stdReward"],
        "bestStep": best["step"],
        "bestMeanReward": best["meanReward"],
        "worstStep": worst["step"],
        "worstMeanReward": worst["meanReward"],
        "totalElapsed": last["elapsedLabel"],
        "checkpointCount": len(checkpoints),
    }
    payload = json.dumps({"points": points, "checkpoints": checkpoints, "summary": summary}, ensure_ascii=False)

    return f"""<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>crowd_stable_60agents_v1 训练曲线</title>
  <style>
    :root {{
      --bg: #f7f8fb;
      --panel: #ffffff;
      --ink: #17202a;
      --muted: #657386;
      --grid: #dce3ea;
      --mean: #0f766e;
      --std: #b45309;
      --checkpoint: #64748b;
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
      grid-template-columns: repeat(4, minmax(128px, 1fr));
      gap: 10px;
      min-width: min(680px, 100%);
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
      padding: 16px 16px 10px;
      box-shadow: 0 12px 30px var(--shadow);
      overflow: hidden;
    }}
    .legend {{
      display: flex;
      gap: 18px;
      align-items: center;
      color: var(--muted);
      font-size: 13px;
      margin: 0 0 8px 60px;
    }}
    .legend span::before {{
      content: "";
      display: inline-block;
      width: 24px;
      height: 3px;
      margin-right: 8px;
      vertical-align: middle;
      border-radius: 999px;
      background: var(--mean);
    }}
    .legend span:nth-child(2)::before {{ background: var(--std); }}
    .legend span:nth-child(3)::before {{
      width: 12px;
      height: 12px;
      background: transparent;
      border-left: 2px dashed var(--checkpoint);
      border-radius: 0;
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
    .mean-line, .std-line {{
      fill: none;
      stroke-width: 3;
      stroke-linejoin: round;
      stroke-linecap: round;
    }}
    .mean-line {{ stroke: var(--mean); }}
    .std-line {{ stroke: var(--std); opacity: 0.86; }}
    .mean-area {{
      fill: rgba(15, 118, 110, 0.12);
    }}
    .checkpoint-line {{
      stroke: var(--checkpoint);
      stroke-dasharray: 4 5;
      opacity: 0.35;
    }}
    .hover-band {{
      fill: transparent;
      cursor: crosshair;
    }}
    .hover-band:hover {{
      fill: rgba(15, 118, 110, 0.045);
    }}
    .focus-line {{
      stroke: #9f1239;
      stroke-width: 1.5;
      stroke-dasharray: 5 5;
      pointer-events: none;
      opacity: 0;
    }}
    .focus-dot {{
      fill: #9f1239;
      stroke: #fff;
      stroke-width: 3;
      pointer-events: none;
      opacity: 0;
    }}
    .tooltip {{
      position: fixed;
      z-index: 10;
      min-width: 280px;
      max-width: min(430px, calc(100vw - 32px));
      padding: 12px 14px;
      border-radius: 8px;
      background: rgba(255, 255, 255, 0.98);
      border: 1px solid #d6dee7;
      box-shadow: 0 18px 42px rgba(15, 23, 42, 0.18);
      opacity: 0;
      pointer-events: none;
      transform: translate(-50%, -105%);
      font-size: 13px;
    }}
    .tooltip h2 {{
      margin: 0 0 8px;
      font-size: 16px;
    }}
    .tooltip dl {{
      display: grid;
      grid-template-columns: auto 1fr;
      gap: 6px 14px;
      margin: 0;
    }}
    .tooltip dt {{ color: var(--muted); }}
    .tooltip dd {{
      margin: 0;
      text-align: right;
      font-weight: 650;
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
      .legend {{ margin-left: 0; flex-wrap: wrap; }}
      svg {{ min-height: 480px; }}
    }}
  </style>
</head>
<body>
  <main>
    <section class="topbar">
      <div>
        <h1>crowd_stable_60agents_v1 训练曲线</h1>
        <div class="subtitle">行为：PedestrianCrowd；横轴为训练步数，纵轴为奖励值；鼠标悬停查看具体 step 信息</div>
      </div>
      <div class="stats" aria-label="训练摘要">
        <div class="stat"><span>最终 Step</span><strong id="lastStep"></strong></div>
        <div class="stat"><span>最终 Mean Reward</span><strong id="lastMean"></strong></div>
        <div class="stat"><span>最佳 Mean Reward</span><strong id="bestMean"></strong></div>
        <div class="stat"><span>训练耗时</span><strong id="elapsed"></strong></div>
      </div>
    </section>

    <section class="chart-wrap">
      <div class="legend"><span>Mean Reward</span><span>Std of Reward</span><span>Checkpoint</span></div>
      <svg id="chart" viewBox="0 0 1200 640" role="img" aria-label="ML-Agents 训练奖励曲线"></svg>
      <div id="tooltip" class="tooltip" aria-hidden="true"></div>
      <div class="footnote">数据来源：LOG.txt；采样频率为每 50,000 step 一次，checkpoint 由 PedestrianCrowd-*.onnx 文件名识别。</div>
    </section>
  </main>

  <script>
    const data = {payload};
    const points = data.points;
    const checkpoints = data.checkpoints;
    const summary = data.summary;
    const fmt = new Intl.NumberFormat("zh-CN");
    const fmtNum = new Intl.NumberFormat("zh-CN", {{ maximumFractionDigits: 3, minimumFractionDigits: 3 }});

    document.getElementById("lastStep").textContent = fmt.format(summary.lastStep);
    document.getElementById("lastMean").textContent = fmtNum.format(summary.lastMeanReward);
    document.getElementById("bestMean").textContent = `${{fmtNum.format(summary.bestMeanReward)}} @ ${{fmt.format(summary.bestStep)}}`;
    document.getElementById("elapsed").textContent = summary.totalElapsed;

    const svg = document.getElementById("chart");
    const tip = document.getElementById("tooltip");
    const NS = "http://www.w3.org/2000/svg";
    const width = 1200;
    const height = 640;
    const margin = {{ left: 78, right: 34, top: 28, bottom: 78 }};
    const plotW = width - margin.left - margin.right;
    const plotH = height - margin.top - margin.bottom;
    const minStep = points[0].step;
    const maxStep = points[points.length - 1].step;
    const allValues = points.flatMap(p => [p.meanReward, p.stdReward]);
    const minY = niceFloor(Math.min(...allValues));
    const maxY = niceCeil(Math.max(...allValues));

    function el(name, attrs = {{}}, parent = svg) {{
      const node = document.createElementNS(NS, name);
      for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, value);
      parent.appendChild(node);
      return node;
    }}
    function x(step) {{
      return margin.left + ((step - minStep) / (maxStep - minStep)) * plotW;
    }}
    function y(value) {{
      return margin.top + plotH - ((value - minY) / (maxY - minY)) * plotH;
    }}
    function niceFloor(value) {{
      return Math.floor(value * 2) / 2;
    }}
    function niceCeil(value) {{
      return Math.ceil(value * 2) / 2;
    }}
    function linePath(key) {{
      return points.map((p, i) => `${{i === 0 ? "M" : "L"}} ${{x(p.step).toFixed(2)}} ${{y(p[key]).toFixed(2)}}`).join(" ");
    }}
    function areaPath(key) {{
      const baseY = y(0);
      return `M ${{x(points[0].step)}} ${{baseY}} ` + points.map(p => `L ${{x(p.step).toFixed(2)}} ${{y(p[key]).toFixed(2)}}`).join(" ") + ` L ${{x(points[points.length - 1].step)}} ${{baseY}} Z`;
    }}

    const grid = el("g", {{ class: "grid" }});
    const axis = el("g", {{ class: "axis" }});
    const yTicks = 6;
    for (let t = 0; t <= yTicks; t++) {{
      const value = minY + ((maxY - minY) / yTicks) * t;
      const yy = y(value);
      el("line", {{ x1: margin.left, y1: yy, x2: width - margin.right, y2: yy }}, grid);
      const label = el("text", {{ x: margin.left - 12, y: yy + 4, "text-anchor": "end" }}, axis);
      label.textContent = fmtNum.format(value);
    }}
    el("line", {{ class: "axis-line", x1: margin.left, y1: y(0), x2: width - margin.right, y2: y(0) }});
    el("line", {{ class: "axis-line", x1: margin.left, y1: margin.top, x2: margin.left, y2: margin.top + plotH }});

    const stepTicks = 5;
    for (let i = 0; i <= stepTicks; i++) {{
      const value = minStep + ((maxStep - minStep) / stepTicks) * i;
      const xx = x(value);
      el("line", {{ x1: xx, y1: margin.top + plotH, x2: xx, y2: margin.top + plotH + 6, stroke: "#9aa8b7" }});
      const label = el("text", {{ x: xx, y: margin.top + plotH + 30, "text-anchor": i === 0 ? "start" : i === stepTicks ? "end" : "middle" }}, axis);
      label.textContent = `${{Math.round(value / 10000) / 100}}M`;
    }}

    const yTitle = el("text", {{ x: 20, y: margin.top + 12, transform: `rotate(-90 20 ${{margin.top + 12}})`, "text-anchor": "end", fill: "#657386", "font-size": 13 }});
    yTitle.textContent = "Reward";
    const xTitle = el("text", {{ x: width / 2, y: height - 20, "text-anchor": "middle", fill: "#657386", "font-size": 13 }});
    xTitle.textContent = "Training Step";

    checkpoints.forEach(cp => {{
      if (cp.step >= minStep && cp.step <= maxStep) {{
        const xx = x(cp.step);
        el("line", {{ class: "checkpoint-line", x1: xx, y1: margin.top, x2: xx, y2: margin.top + plotH }});
      }}
    }});

    el("path", {{ class: "mean-area", d: areaPath("meanReward") }});
    el("path", {{ class: "mean-line", d: linePath("meanReward") }});
    el("path", {{ class: "std-line", d: linePath("stdReward") }});

    points.forEach(p => {{
      el("circle", {{ cx: x(p.step), cy: y(p.meanReward), r: 3.5, fill: "#0f766e", opacity: 0.86 }});
      el("circle", {{ cx: x(p.step), cy: y(p.stdReward), r: 3, fill: "#b45309", opacity: 0.76 }});
    }});

    const focusLine = el("line", {{ class: "focus-line", y1: margin.top, y2: margin.top + plotH }});
    const focusDot = el("circle", {{ class: "focus-dot", r: 6 }});
    const overlays = el("g");
    points.forEach((p, i) => {{
      const prevX = i === 0 ? margin.left : (x(points[i - 1].step) + x(p.step)) / 2;
      const nextX = i === points.length - 1 ? width - margin.right : (x(points[i + 1].step) + x(p.step)) / 2;
      const rect = el("rect", {{
        class: "hover-band",
        x: prevX,
        y: margin.top,
        width: Math.max(1, nextX - prevX),
        height: plotH
      }}, overlays);
      rect.addEventListener("mousemove", event => showTooltip(event, p));
      rect.addEventListener("mouseleave", hideTooltip);
    }});

    function checkpointAt(step) {{
      return checkpoints.filter(cp => Math.abs(cp.step - step) <= 30000).map(cp => cp.file).join("<br>");
    }}
    function showTooltip(event, p) {{
      const xx = x(p.step);
      focusLine.setAttribute("x1", xx);
      focusLine.setAttribute("x2", xx);
      focusLine.style.opacity = 1;
      focusDot.setAttribute("cx", xx);
      focusDot.setAttribute("cy", y(p.meanReward));
      focusDot.style.opacity = 1;

      const checkpoint = checkpointAt(p.step);
      tip.innerHTML = `<h2>Step ${{fmt.format(p.step)}}</h2>
        <dl>
          <dt>Mean Reward</dt><dd style="color:#0f766e">${{fmtNum.format(p.meanReward)}}</dd>
          <dt>Std of Reward</dt><dd style="color:#b45309">${{fmtNum.format(p.stdReward)}}</dd>
          <dt>Time Elapsed</dt><dd>${{p.elapsedLabel}}</dd>
          <dt>Behavior</dt><dd>${{p.behavior}}</dd>
          ${{checkpoint ? `<dt>Checkpoint</dt><dd>${{checkpoint}}</dd>` : ""}}
        </dl>`;
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
    points = parse_log()
    checkpoints = parse_checkpoints()
    OUTPUT_HTML.write_text(make_html(points, checkpoints), encoding="utf-8")
    summary = {
        "output": str(OUTPUT_HTML),
        "points": len(points),
        "firstStep": points[0]["step"],
        "lastStep": points[-1]["step"],
        "lastMeanReward": points[-1]["meanReward"],
        "best": max(points, key=lambda p: p["meanReward"]),
        "checkpoints": len(checkpoints),
    }
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
