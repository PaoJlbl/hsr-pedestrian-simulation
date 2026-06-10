"""
Visualize control experiment results exported from Unity.

Input directory:
    D:/AAA Learning/Unity/Shenzhenbei_wcx/ControlExport

Output directory:
    ./control_analysis_output

The script compares three Burst control configurations:
Level 0: no control
Level 1: spawn/exit metering only
Level 2: metering + dynamic gate guidance
"""

from __future__ import annotations

import argparse
import csv
import json
from pathlib import Path
from typing import Iterable

import numpy as np
import pandas as pd
from PIL import Image, ImageDraw, ImageFont


LEVELS = {
    "Burst_Baseline": {
        "level": "Level 0",
        "label": "Level 0 No control",
        "short": "L0",
        "order": 0,
        "color": (76, 120, 168),
    },
    "Burst_MeteringOnly": {
        "level": "Level 1",
        "label": "Level 1 Metering",
        "short": "L1",
        "order": 1,
        "color": (245, 133, 24),
    },
    "Burst_Metering_GateGuidance": {
        "level": "Level 2",
        "label": "Level 2 Metering + Gate guidance",
        "short": "L2",
        "order": 2,
        "color": (84, 162, 75),
    },
}

NUMERIC_METRICS = {
    "InterventionCount",
    "LevelChangeCount",
    "TotalControlActiveTime",
    "TotalTarget",
    "TotalGenerated",
    "TotalArrived",
    "TotalFailed",
    "TotalEvacuationTime",
    "AverageTravelTime",
    "PeakActiveAgents",
    "WestPassed",
    "EastPassed",
    "GateImbalance",
    "WrongExitCount",
    "OutOfBoundsCount",
    "WallCollisionCount",
    "ObstacleCollisionCount",
    "PedestrianCollisionCount",
}


def font(size: int, bold: bool = False):
    candidates = [
        "C:/Windows/Fonts/msyhbd.ttc" if bold else "C:/Windows/Fonts/msyh.ttc",
        "C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf",
        "DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf",
    ]
    for candidate in candidates:
        try:
            return ImageFont.truetype(candidate, size)
        except OSError:
            continue
    return ImageFont.load_default()


def text_center(draw, xy, text: str, fill, fnt) -> None:
    x, y = xy
    box = draw.textbbox((0, 0), text, font=fnt)
    draw.text((x - (box[2] - box[0]) / 2, y), text, fill=fill, font=fnt)


def metric_value(row: pd.Series, metric: str) -> float:
    value = row.get(metric, 0)
    if pd.isna(value):
        return 0.0
    return float(value)


def percent_change(value: float, baseline: float, lower_is_better: bool = True) -> float:
    if baseline == 0:
        return 0.0
    delta = (value - baseline) / baseline * 100.0
    return -delta if lower_is_better else delta


def read_key_value_csv(path: Path) -> dict:
    out = {}
    with open(path, "r", encoding="utf-8-sig", newline="") as f:
        for row in csv.DictReader(f):
            key = row["Metric"]
            value = row["Value"]
            if key in NUMERIC_METRICS:
                try:
                    out[key] = float(value)
                except ValueError:
                    out[key] = np.nan
            else:
                out[key] = value
    return out


def discover_results(input_dir: Path) -> tuple[pd.DataFrame, pd.DataFrame]:
    summaries = []
    zones = []
    for summary_path in sorted(input_dir.glob("*_summary.csv")):
        stem = summary_path.name.replace("_summary.csv", "")
        experiment_name = "_".join(stem.split("_")[:-2])
        if experiment_name not in LEVELS:
            continue
        row = read_key_value_csv(summary_path)
        meta = LEVELS[experiment_name]
        row.update(
            {
                "ExperimentKey": experiment_name,
                "Level": meta["level"],
                "Label": meta["label"],
                "Short": meta["short"],
                "Order": meta["order"],
                "SummaryFile": str(summary_path),
            }
        )
        summaries.append(row)

        zone_path = summary_path.with_name(stem + "_zone_metrics.csv")
        if zone_path.exists():
            zone_df = pd.read_csv(zone_path)
            zone_df["ExperimentKey"] = experiment_name
            zone_df["Level"] = meta["level"]
            zone_df["Label"] = meta["label"]
            zone_df["Short"] = meta["short"]
            zone_df["Order"] = meta["order"]
            zones.append(zone_df)

    if not summaries:
        raise FileNotFoundError(f"No recognized control summary CSV files found in {input_dir}")

    summary_df = pd.DataFrame(summaries).sort_values("Order").reset_index(drop=True)
    zone_df = pd.concat(zones, ignore_index=True).sort_values(["Order", "ZoneName"]).reset_index(drop=True)
    return summary_df, zone_df


def draw_grouped_bars(
    path: Path,
    title: str,
    categories: list[str],
    series: list[tuple[str, list[float], tuple[int, int, int]]],
    y_label: str = "",
    value_fmt: str = "{:.0f}",
    width: int = 1400,
    height: int = 760,
) -> None:
    img = Image.new("RGB", (width, height), "white")
    draw = ImageDraw.Draw(img)
    title_font = font(28, True)
    label_font = font(16)
    small_font = font(13)
    text_center(draw, (width / 2, 24), title, (30, 30, 30), title_font)

    left, right, top, bottom = 100, 60, 95, 105
    plot_w, plot_h = width - left - right, height - top - bottom
    max_value = max(max(values) for _, values, _ in series)
    max_value = max(max_value, 1.0) * 1.12

    draw.rectangle([left, top, left + plot_w, top + plot_h], outline=(185, 185, 185))
    for i in range(6):
        y = top + plot_h - i * plot_h / 5
        value = max_value * i / 5
        draw.line([left, y, left + plot_w, y], fill=(232, 232, 232))
        draw.text((18, y - 8), value_fmt.format(value), fill=(80, 80, 80), font=small_font)

    group_w = plot_w / len(categories)
    bar_gap = 8
    bar_w = max(12, (group_w * 0.72 - bar_gap * (len(series) - 1)) / len(series))
    for cat_idx, category in enumerate(categories):
        group_x = left + cat_idx * group_w
        center = group_x + group_w / 2
        for s_idx, (name, values, color) in enumerate(series):
            value = values[cat_idx]
            bar_h = value / max_value * plot_h
            x0 = center - (len(series) * bar_w + (len(series) - 1) * bar_gap) / 2 + s_idx * (bar_w + bar_gap)
            y0 = top + plot_h - bar_h
            draw.rectangle([x0, y0, x0 + bar_w, top + plot_h], fill=color)
            draw.text((x0 - 4, y0 - 18), value_fmt.format(value), fill=(40, 40, 40), font=small_font)
        text_center(draw, (center, top + plot_h + 14), category, (60, 60, 60), label_font)

    legend_x = left
    legend_y = height - 48
    for name, _, color in series:
        draw.rectangle([legend_x, legend_y, legend_x + 18, legend_y + 18], fill=color)
        draw.text((legend_x + 25, legend_y - 1), name, fill=(50, 50, 50), font=label_font)
        legend_x += 300
    if y_label:
        draw.text((16, 58), y_label, fill=(50, 50, 50), font=label_font)
    img.save(path)


def draw_metric_cards(summary_df: pd.DataFrame, output_dir: Path) -> None:
    metrics = [
        ("TotalArrived", "Arrived", False),
        ("TotalFailed", "Failed", True),
        ("AverageTravelTime", "Avg travel time", True),
        ("PeakActiveAgents", "Peak active", True),
        ("WrongExitCount", "Wrong exits", True),
        ("OutOfBoundsCount", "Out of bounds", True),
    ]
    width, height = 1500, 900
    img = Image.new("RGB", (width, height), "white")
    draw = ImageDraw.Draw(img)
    text_center(draw, (width / 2, 24), "Burst Control KPI Comparison", (30, 30, 30), font(30, True))

    baseline = summary_df.iloc[0]
    card_w, card_h = 420, 210
    start_x, start_y = 80, 110
    gap_x, gap_y = 45, 50
    for idx, (metric, label, lower_is_better) in enumerate(metrics):
        col = idx % 3
        row = idx // 3
        x = start_x + col * (card_w + gap_x)
        y = start_y + row * (card_h + gap_y)
        draw.rounded_rectangle([x, y, x + card_w, y + card_h], radius=8, fill=(248, 249, 250), outline=(210, 210, 210))
        draw.text((x + 20, y + 16), label, fill=(35, 35, 35), font=font(20, True))
        base_value = metric_value(baseline, metric)
        for i, (_, level_row) in enumerate(summary_df.iterrows()):
            color = LEVELS[level_row["ExperimentKey"]]["color"]
            value = metric_value(level_row, metric)
            yy = y + 58 + i * 42
            draw.rectangle([x + 20, yy + 6, x + 36, yy + 22], fill=color)
            draw.text((x + 45, yy), level_row["Short"], fill=(40, 40, 40), font=font(16, True))
            draw.text((x + 92, yy), f"{value:,.3f}" if metric == "AverageTravelTime" else f"{value:,.0f}", fill=(40, 40, 40), font=font(16))
            if i > 0:
                change = percent_change(value, base_value, lower_is_better)
                good = change >= 0
                sign = "+" if change >= 0 else ""
                draw.text(
                    (x + 250, yy),
                    f"{sign}{change:.1f}% vs L0",
                    fill=(18, 135, 70) if good else (190, 50, 45),
                    font=font(15, True),
                )

    audit_y = 650
    draw.text((80, audit_y), "Execution audit", fill=(35, 35, 35), font=font(22, True))
    headers = ["Level", "CurrentLevel", "Interventions", "Level changes", "Active time"]
    xs = [80, 240, 610, 800, 1010]
    for x, header in zip(xs, headers):
        draw.text((x, audit_y + 42), header, fill=(80, 80, 80), font=font(15, True))
    for i, (_, row) in enumerate(summary_df.iterrows()):
        yy = audit_y + 78 + i * 42
        values = [
            row["Short"],
            str(row["CurrentLevel"]),
            f"{metric_value(row, 'InterventionCount'):.0f}",
            f"{metric_value(row, 'LevelChangeCount'):.0f}",
            f"{metric_value(row, 'TotalControlActiveTime'):.3f}",
        ]
        for x, value in zip(xs, values):
            draw.text((x, yy), value, fill=(45, 45, 45), font=font(16))
    img.save(output_dir / "01_kpi_dashboard.png")


def draw_collisions(summary_df: pd.DataFrame, output_dir: Path) -> None:
    categories = ["Wall", "Obstacle", "Pedestrian"]
    metrics = ["WallCollisionCount", "ObstacleCollisionCount", "PedestrianCollisionCount"]
    series = []
    for _, row in summary_df.iterrows():
        color = LEVELS[row["ExperimentKey"]]["color"]
        series.append((row["Short"], [metric_value(row, metric) for metric in metrics], color))
    draw_grouped_bars(
        output_dir / "02_collision_comparison.png",
        "Safety / Interaction Event Comparison",
        categories,
        series,
        y_label="Event count",
        value_fmt="{:.0f}",
    )


def draw_zone_pressure(zone_df: pd.DataFrame, output_dir: Path) -> None:
    zones = ["Zone_Core_X8", "Zone_Belt_X6_8", "Zone_Spill_X9_10", "Zone_Secondary_X19"]
    labels = ["Core X8", "Belt X6-8", "Spill X9-10", "Secondary X19"]
    for metric, title, filename, fmt in [
        ("PeakObservedDensity", "Zone Peak Density Comparison", "03_zone_peak_density.png", "{:.3f}"),
        ("MaxObservedCount", "Zone Max Count Comparison", "04_zone_max_count.png", "{:.0f}"),
    ]:
        series = []
        for key, meta in sorted(LEVELS.items(), key=lambda item: item[1]["order"]):
            sub = zone_df[zone_df["ExperimentKey"] == key].set_index("ZoneName")
            values = [float(sub.loc[zone, metric]) if zone in sub.index else 0.0 for zone in zones]
            series.append((meta["short"], values, meta["color"]))
        draw_grouped_bars(output_dir / filename, title, labels, series, y_label=metric, value_fmt=fmt)


def export_summary(summary_df: pd.DataFrame, zone_df: pd.DataFrame, output_dir: Path) -> dict:
    baseline = summary_df.iloc[0]
    comparison_rows = []
    for _, row in summary_df.iterrows():
        comparison_rows.append(
            {
                "Level": row["Level"],
                "Label": row["Label"],
                "ActualCurrentLevel": row["CurrentLevel"],
                "InterventionCount": metric_value(row, "InterventionCount"),
                "TotalControlActiveTime": metric_value(row, "TotalControlActiveTime"),
                "TotalArrived": metric_value(row, "TotalArrived"),
                "ArrivedChangeVsL0Pct": percent_change(metric_value(row, "TotalArrived"), metric_value(baseline, "TotalArrived"), False),
                "TotalFailed": metric_value(row, "TotalFailed"),
                "FailedReductionVsL0Pct": percent_change(metric_value(row, "TotalFailed"), metric_value(baseline, "TotalFailed"), True),
                "AverageTravelTime": metric_value(row, "AverageTravelTime"),
                "TravelTimeReductionVsL0Pct": percent_change(metric_value(row, "AverageTravelTime"), metric_value(baseline, "AverageTravelTime"), True),
                "PeakActiveAgents": metric_value(row, "PeakActiveAgents"),
                "WrongExitCount": metric_value(row, "WrongExitCount"),
                "OutOfBoundsCount": metric_value(row, "OutOfBoundsCount"),
                "WallCollisionCount": metric_value(row, "WallCollisionCount"),
                "PedestrianCollisionCount": metric_value(row, "PedestrianCollisionCount"),
            }
        )
    comparison_df = pd.DataFrame(comparison_rows)
    comparison_df.to_csv(output_dir / "control_summary_comparison.csv", index=False, encoding="utf-8-sig")
    zone_df.to_csv(output_dir / "control_zone_metrics_combined.csv", index=False, encoding="utf-8-sig")

    level1 = comparison_df[comparison_df["Level"] == "Level 1"].iloc[0]
    level2 = comparison_df[comparison_df["Level"] == "Level 2"].iloc[0]
    audit_ok = bool((summary_df["InterventionCount"].astype(float) > 0).any())
    result = {
        "audit_control_triggered": audit_ok,
        "rows": comparison_rows,
        "level1_failed_reduction_pct": float(level1["FailedReductionVsL0Pct"]),
        "level1_ped_collision_reduction_pct": percent_change(
            metric_value(summary_df.iloc[1], "PedestrianCollisionCount"),
            metric_value(baseline, "PedestrianCollisionCount"),
            True,
        ),
        "level2_failed_reduction_pct": float(level2["FailedReductionVsL0Pct"]),
        "level2_wall_collision_change_pct_lower_is_better": percent_change(
            metric_value(summary_df.iloc[2], "WallCollisionCount"),
            metric_value(baseline, "WallCollisionCount"),
            True,
        ),
    }
    with open(output_dir / "control_analysis_summary.json", "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)

    def markdown_table(df: pd.DataFrame) -> str:
        df = df.copy()
        for col in df.columns:
            if pd.api.types.is_numeric_dtype(df[col]):
                df[col] = df[col].map(lambda value: f"{float(value):.4f}".rstrip("0").rstrip("."))
            else:
                df[col] = df[col].astype(str)
        headers = list(df.columns)
        lines = [
            "| " + " | ".join(headers) + " |",
            "| " + " | ".join(["---"] * len(headers)) + " |",
        ]
        for _, row in df.iterrows():
            lines.append("| " + " | ".join(str(row[col]) for col in headers) + " |")
        return "\n".join(lines)

    zone_report_df = zone_df[
        ["Level", "ZoneName", "PeakObservedDensity", "MaxObservedCount", "TotalCriticalTime", "CriticalEpisodeCount"]
    ]

    report = [
        "# Burst control analysis",
        "",
        "## Execution audit",
        "",
        "All three exported summaries report `CurrentLevel=Level0_NoControl`, `InterventionCount=0`, "
        "and `TotalControlActiveTime=0.000`. Therefore the files compare configured experiment variants, "
        "but they do not prove that adaptive level control was actually triggered during simulation.",
        "",
        "## Main comparison",
        "",
        markdown_table(comparison_df.round(4)),
        "",
        "## Zone pressure",
        "",
        markdown_table(zone_report_df.round(4)),
        "",
    ]
    (output_dir / "control_analysis_report.md").write_text("\n".join(report), encoding="utf-8")
    return result


def parse_args(argv: Iterable[str] | None = None) -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description="Visualize Burst control experiment results.")
    parser.add_argument(
        "--input",
        default="D:/AAA Learning/Unity/Shenzhenbei_wcx/ControlExport",
        help="Directory containing *_summary.csv and *_zone_metrics.csv files.",
    )
    parser.add_argument(
        "--output",
        default=str(script_dir / "control_analysis_output"),
        help="Output directory for charts and summary tables.",
    )
    return parser.parse_args(argv)


def main(argv: Iterable[str] | None = None) -> None:
    args = parse_args(argv)
    input_dir = Path(args.input)
    output_dir = Path(args.output)
    output_dir.mkdir(parents=True, exist_ok=True)
    summary_df, zone_df = discover_results(input_dir)
    draw_metric_cards(summary_df, output_dir)
    draw_collisions(summary_df, output_dir)
    draw_zone_pressure(zone_df, output_dir)
    result = export_summary(summary_df, zone_df, output_dir)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    print(f"Outputs saved to: {output_dir.resolve()}")


if __name__ == "__main__":
    main()
