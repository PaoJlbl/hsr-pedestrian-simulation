"""
Visualize hierarchical control experiment exports for Burst crowd simulation.

The script produces two kinds of outputs:
1. actual_*: strict visualizations based on the exported CSV files.
2. illustrative_*: clearly labeled assumption-based presentation views for metrics
   that were exported as zero or placeholders. These are not simulation facts.
"""

from __future__ import annotations

import csv
import json
from pathlib import Path
from typing import Any

import pandas as pd
import numpy as np

try:
    from PIL import Image, ImageDraw, ImageFont
except ModuleNotFoundError as exc:
    raise RuntimeError("Pillow is required for plotting in this environment.") from exc


INPUT_DIR = Path("D:/AAA Learning/Unity/Shenzhenbei_wcx/ControlExport")
OUTPUT_DIR = Path("D:/AAA Learning/AAA本科毕业设计/代码/control_analysis_output")

EXPERIMENTS = [
    {
        "level": "Level 0",
        "short": "L0",
        "name": "No control",
        "summary": "Burst_Base_20260527_135610_summary.csv",
        "zones": "Burst_Base_20260527_135610_zone_metrics.csv",
    },
    {
        "level": "Level 1",
        "short": "L1",
        "name": "Exit metering",
        "summary": "Burst_MeteringOnly_20260527_115243_summary.csv",
        "zones": "Burst_MeteringOnly_20260527_115243_zone_metrics.csv",
    },
    {
        "level": "Level 2",
        "short": "L2",
        "name": "Metering + gate guidance",
        "summary": "Burst_Metering_GateGuidance_20260526_213739_summary.csv",
        "zones": "Burst_Metering_GateGuidance_20260526_213739_zone_metrics.csv",
    },
]

CORE_METRICS = ["T_total", "AvgTravelTime", "T_core", "T_belt", "rho_peak_core", "Imbalance"]

METRIC_LABELS = {
    "T_total": "Total clearance time",
    "AvgTravelTime": "Average travel time",
    "T_core": "Core bottleneck duration",
    "T_belt": "Main belt duration",
    "rho_peak_core": "Core peak density",
    "Imbalance": "Gate imbalance",
    "TotalArrived": "Arrived agents",
    "TotalFailed": "Failed agents",
    "PeakActiveAgents": "Peak active agents",
    "WrongExitCount": "Wrong exits",
    "OutOfBoundsCount": "Out of bounds",
    "WallCollisionCount": "Wall collisions",
    "ObstacleCollisionCount": "Obstacle collisions",
    "PedestrianCollisionCount": "Pedestrian collisions",
}

COLORS = {
    "L0": (102, 126, 153),
    "L1": (72, 145, 179),
    "L2": (58, 158, 117),
}


def font(size: int, bold: bool = False):
    candidates = [
        "C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf",
        "C:/Windows/Fonts/msyhbd.ttc" if bold else "C:/Windows/Fonts/msyh.ttc",
        "arialbd.ttf" if bold else "arial.ttf",
    ]
    for candidate in candidates:
        try:
            return ImageFont.truetype(candidate, size)
        except OSError:
            continue
    return ImageFont.load_default()


def read_summary(path: Path) -> dict[str, Any]:
    df = pd.read_csv(path)
    out: dict[str, Any] = {}
    for _, row in df.iterrows():
        value = row["Value"]
        try:
            if isinstance(value, str) and value.strip() != "":
                number = float(value)
                if number.is_integer():
                    number = int(number)
                out[row["Metric"]] = number
            else:
                out[row["Metric"]] = value
        except ValueError:
            out[row["Metric"]] = value
    return out


def load_data() -> tuple[pd.DataFrame, pd.DataFrame]:
    summary_rows = []
    zone_rows = []
    for exp in EXPERIMENTS:
        summary = read_summary(INPUT_DIR / exp["summary"])
        row = {"level": exp["level"], "short": exp["short"], "control_name": exp["name"]}
        row.update(summary)
        summary_rows.append(row)

        zones = pd.read_csv(INPUT_DIR / exp["zones"])
        zones.insert(0, "level", exp["level"])
        zones.insert(1, "short", exp["short"])
        zones.insert(2, "control_name", exp["name"])
        zone_rows.append(zones)

    return pd.DataFrame(summary_rows), pd.concat(zone_rows, ignore_index=True)


def draw_title(draw: ImageDraw.ImageDraw, width: int, title: str, subtitle: str | None = None) -> None:
    title_font = font(27, bold=True)
    bbox = draw.textbbox((0, 0), title, font=title_font)
    draw.text(((width - (bbox[2] - bbox[0])) / 2, 20), title, fill=(28, 35, 42), font=title_font)
    if subtitle:
        sub_font = font(15)
        bbox = draw.textbbox((0, 0), subtitle, font=sub_font)
        draw.text(((width - (bbox[2] - bbox[0])) / 2, 58), subtitle, fill=(88, 96, 105), font=sub_font)


def bar_chart(
    data: pd.DataFrame,
    metrics: list[str],
    path: Path,
    title: str,
    subtitle: str | None = None,
    lower_is_better: set[str] | None = None,
) -> None:
    lower_is_better = lower_is_better or set()
    width, height = 1450, 260 + 210 * len(metrics)
    img = Image.new("RGB", (width, height), "white")
    draw = ImageDraw.Draw(img)
    draw_title(draw, width, title, subtitle)

    left, right = 285, 95
    panel_top = 105
    panel_h = 150
    row_gap = 58
    label_font = font(17, bold=True)
    small_font = font(14)
    value_font = font(14, bold=True)

    for i, metric in enumerate(metrics):
        top = panel_top + i * (panel_h + row_gap)
        values = data[metric].astype(float).to_numpy()
        max_value = max(float(np.nanmax(values)), 1e-9)
        if metric in {"rho_peak_core", "Imbalance"}:
            max_value *= 1.25
        else:
            max_value *= 1.12
        plot_w = width - left - right

        draw.text((35, top + 52), METRIC_LABELS.get(metric, metric), fill=(35, 40, 48), font=label_font)
        draw.rectangle([left, top, left + plot_w, top + panel_h], outline=(220, 225, 230))
        for tick in np.linspace(0, max_value, 5):
            x = left + tick / max_value * plot_w if max_value else left
            draw.line([x, top, x, top + panel_h], fill=(240, 242, 245))
            draw.text((x - 12, top + panel_h + 6), f"{tick:.0f}" if tick >= 1 else f"{tick:.2f}", fill=(105, 112, 120), font=small_font)

        bar_h = 26
        for j, (_, row) in enumerate(data.iterrows()):
            y = top + 22 + j * 40
            value = float(row[metric])
            bar_w = value / max_value * plot_w if max_value else 0
            color = COLORS[row["short"]]
            draw.rectangle([left, y, left + bar_w, y + bar_h], fill=color)
            draw.text((left - 58, y + 2), row["short"], fill=color, font=value_font)
            draw.text((left + bar_w + 8, y + 2), f"{value:.3g}", fill=(35, 40, 48), font=value_font)

        base = float(data.iloc[0][metric])
        final = float(data.iloc[-1][metric])
        if abs(base) > 1e-12:
            change = (final - base) / base * 100
            if metric in lower_is_better:
                text = f"L2 vs L0: {-change:+.1f}% improvement" if change <= 0 else f"L2 vs L0: {change:+.1f}% worse"
            else:
                text = f"L2 vs L0: {change:+.1f}%"
            draw.text((width - 335, top + 3), text, fill=(80, 92, 100), font=small_font)

    img.save(path)


def grouped_zone_chart(zone_df: pd.DataFrame, metric: str, path: Path, title: str) -> None:
    zones = list(zone_df["ZoneName"].drop_duplicates())
    width, height = 1450, 760
    img = Image.new("RGB", (width, height), "white")
    draw = ImageDraw.Draw(img)
    draw_title(draw, width, title)

    left, right, top, bottom = 160, 70, 110, 145
    plot_w, plot_h = width - left - right, height - top - bottom
    values = zone_df[metric].astype(float)
    max_value = max(float(values.max()) * 1.15, 1e-9)
    draw.rectangle([left, top, left + plot_w, top + plot_h], outline=(220, 225, 230))
    for tick in np.linspace(0, max_value, 6):
        y = top + plot_h - tick / max_value * plot_h
        draw.line([left, y, left + plot_w, y], fill=(240, 242, 245))
        draw.text((42, y - 8), f"{tick:.3g}", fill=(105, 112, 120), font=font(14))

    group_w = plot_w / len(zones)
    bar_w = group_w / 5
    for i, zone in enumerate(zones):
        sub = zone_df[zone_df["ZoneName"] == zone]
        group_x = left + i * group_w
        for j, (_, row) in enumerate(sub.iterrows()):
            x0 = group_x + group_w * 0.24 + j * bar_w
            value = float(row[metric])
            y0 = top + plot_h - value / max_value * plot_h
            draw.rectangle([x0, y0, x0 + bar_w * 0.82, top + plot_h], fill=COLORS[row["short"]])
        label = zone.replace("Zone_", "").replace("_", " ")
        draw.text((group_x + 12, top + plot_h + 12), label, fill=(60, 68, 76), font=font(14))

    lx = width - 355
    for j, exp in enumerate(EXPERIMENTS):
        y = 62 + j * 24
        draw.rectangle([lx, y, lx + 16, y + 16], fill=COLORS[exp["short"]])
        draw.text((lx + 24, y - 1), f"{exp['short']} {exp['name']}", fill=(60, 68, 76), font=font(14))
    img.save(path)


def build_illustrative(summary_df: pd.DataFrame) -> pd.DataFrame:
    """
    Create a transparent presentation-only dataset for zero placeholder metrics.
    Values are monotonic and plausible for explaining the control mechanism, but are
    not exported simulation measurements.
    """
    out = summary_df[["level", "short", "control_name"]].copy()
    out["T_total"] = [848.09, 805.00, 760.00]
    out["AvgTravelTime"] = summary_df["AvgTravelTime"].astype(float).to_numpy()
    out["T_core"] = [580.0, 430.0, 315.0]
    out["T_belt"] = [610.0, 455.0, 330.0]
    out["rho_peak_core"] = [0.2869, 0.2450, 0.2150]
    out["Imbalance"] = [0.38, 0.27, 0.14]
    out["data_type"] = "illustrative_assumption_not_simulation_output"
    return out


def write_report(summary_df: pd.DataFrame, zone_df: pd.DataFrame, illustrative_df: pd.DataFrame) -> None:
    actual_core = summary_df[["level", "control_name", *CORE_METRICS]].copy()
    actual_core.to_csv(OUTPUT_DIR / "actual_core_metrics.csv", index=False, encoding="utf-8-sig")
    zone_df.to_csv(OUTPUT_DIR / "actual_zone_metrics_merged.csv", index=False, encoding="utf-8-sig")
    illustrative_df.to_csv(OUTPUT_DIR / "illustrative_core_metrics_assumption.csv", index=False, encoding="utf-8-sig")

    def pct(old: float, new: float, lower_better: bool = True) -> float:
        if old == 0:
            return float("nan")
        raw = (new - old) / old * 100
        return -raw if lower_better else raw

    l0, l1, l2 = summary_df.iloc[0], summary_df.iloc[1], summary_df.iloc[2]
    zone_belt = zone_df[zone_df["ZoneName"] == "Zone_Belt_X6_8"].copy()
    zone_core = zone_df[zone_df["ZoneName"] == "Zone_Core_X8"].copy()
    lines = [
        "# Burst Control Analysis",
        "",
        "## Data Quality",
        "- Three experiments were found: Level 0 no control, Level 1 exit metering, Level 2 metering + gate guidance.",
        "- The first six core metrics contain zero placeholders: T_total, T_core, T_belt, and Imbalance are all 0 in the export.",
        "- Control trigger fields are also 0, and gate pass counts are 0, so the exported control-state logger likely did not capture dynamic intervention states.",
        "- The actual visualizations therefore separate measured outputs from presentation-only assumptions.",
        "",
        "## Actual Export Findings",
        f"- Average travel time decreases from {float(l0['AvgTravelTime']):.3f}s to {float(l1['AvgTravelTime']):.3f}s and {float(l2['AvgTravelTime']):.3f}s.",
        f"- Level 2 improves average travel time by {pct(float(l0['AvgTravelTime']), float(l2['AvgTravelTime'])):.2f}% relative to Level 0.",
        f"- Total failed agents change from {int(l0['TotalFailed'])} to {int(l1['TotalFailed'])} and {int(l2['TotalFailed'])}.",
        f"- Wrong exits decrease from {int(l0['WrongExitCount'])} to {int(l2['WrongExitCount'])}, an improvement of {pct(float(l0['WrongExitCount']), float(l2['WrongExitCount'])):.2f}%.",
        f"- Main belt peak density changes from {float(zone_belt.iloc[0]['PeakObservedDensity']):.4f} to {float(zone_belt.iloc[2]['PeakObservedDensity']):.4f}.",
        f"- Core X8 peak density changes from {float(zone_core.iloc[0]['PeakObservedDensity']):.4f} to {float(zone_core.iloc[2]['PeakObservedDensity']):.4f}.",
        "",
        "## Interpretation",
        "- Level 1 mainly reduces average travel time and failed-agent count slightly, consistent with exit metering smoothing inflow.",
        "- Level 2 further reduces average travel time and wrong-exit behavior, consistent with gate guidance improving route choice.",
        "- The actual exported core bottleneck durations cannot support a claim about reduced T_core/T_belt because they are all zero.",
        "- The illustrative charts show how the expected control effect can be presented, but those values must be described as assumptions unless rerun/export logic provides measured values.",
    ]
    (OUTPUT_DIR / "control_analysis_report.md").write_text("\n".join(lines), encoding="utf-8")


def main() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    summary_df, zone_df = load_data()
    illustrative_df = build_illustrative(summary_df)

    numeric_cols = [
        *CORE_METRICS,
        "TotalArrived",
        "TotalFailed",
        "PeakActiveAgents",
        "WrongExitCount",
        "OutOfBoundsCount",
        "WallCollisionCount",
        "ObstacleCollisionCount",
        "PedestrianCollisionCount",
    ]
    for col in numeric_cols:
        if col in summary_df.columns:
            summary_df[col] = pd.to_numeric(summary_df[col], errors="coerce")

    bar_chart(
        summary_df,
        CORE_METRICS,
        OUTPUT_DIR / "actual_first_six_core_metrics.png",
        "Actual Export: First Six Core Metrics",
        "Zeros are preserved from the simulation export.",
        lower_is_better={"T_total", "AvgTravelTime", "T_core", "T_belt", "rho_peak_core", "Imbalance"},
    )
    bar_chart(
        summary_df,
        ["AvgTravelTime", "TotalArrived", "TotalFailed", "PeakActiveAgents", "WrongExitCount", "PedestrianCollisionCount"],
        OUTPUT_DIR / "actual_operational_indicators.png",
        "Actual Export: Operational Indicators",
        "Useful measured indicators beyond the zero-placeholder core fields.",
        lower_is_better={"AvgTravelTime", "TotalFailed", "PeakActiveAgents", "WrongExitCount", "PedestrianCollisionCount"},
    )
    grouped_zone_chart(
        zone_df,
        "PeakObservedDensity",
        OUTPUT_DIR / "actual_zone_peak_density.png",
        "Actual Export: Zone Peak Observed Density",
    )
    grouped_zone_chart(
        zone_df,
        "MaxObservedCount",
        OUTPUT_DIR / "actual_zone_max_count.png",
        "Actual Export: Zone Maximum Observed Count",
    )
    bar_chart(
        illustrative_df,
        CORE_METRICS,
        OUTPUT_DIR / "illustrative_first_six_core_metrics_assumption.png",
        "Illustrative Control Effect: First Six Core Metrics",
        "Presentation-only assumptions for zero-placeholder metrics; not simulation output.",
        lower_is_better={"T_total", "AvgTravelTime", "T_core", "T_belt", "rho_peak_core", "Imbalance"},
    )

    write_report(summary_df, zone_df, illustrative_df)
    print(json.dumps({"output_dir": str(OUTPUT_DIR), "files": sorted(p.name for p in OUTPUT_DIR.iterdir())}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
