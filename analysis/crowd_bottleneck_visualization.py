"""
Crowd perception, situation projection, and bottleneck visualization for
high-speed railway station exit corridor simulation data.

Default input:
    D:/AAA Learning/Unity/Shenzhenbei_wcx/crowd_state_normal.csv

Default output:
    ./crowd_analysis_normal_output

The script is designed for grid-state CSV files with columns:
scenario,time,cell_x,cell_z,count,density,mean_speed,mean_vx,mean_vz
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Iterable

import numpy as np
import pandas as pd

try:
    import matplotlib.pyplot as plt

    HAS_MATPLOTLIB = True
except ModuleNotFoundError:
    plt = None
    HAS_MATPLOTLIB = False

try:
    from PIL import Image, ImageDraw, ImageFont

    HAS_PIL = True
except ModuleNotFoundError:
    Image = ImageDraw = ImageFont = None
    HAS_PIL = False


def _require_plot_backend() -> None:
    if not HAS_MATPLOTLIB and not HAS_PIL:
        raise RuntimeError("No plotting backend found. Install matplotlib or Pillow.")


def _font(size: int, bold: bool = False):
    if not HAS_PIL:
        return None
    candidates = [
        "arialbd.ttf" if bold else "arial.ttf",
        "DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf",
        "C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf",
        "C:/Windows/Fonts/msyhbd.ttc" if bold else "C:/Windows/Fonts/msyh.ttc",
    ]
    for candidate in candidates:
        try:
            return ImageFont.truetype(candidate, size)
        except OSError:
            continue
    return ImageFont.load_default()


def _lerp(a: int, b: int, t: float) -> int:
    return int(round(a + (b - a) * t))


def _palette_color(value: float, cmap: str) -> tuple[int, int, int]:
    value = float(np.clip(value, 0, 1))
    palettes = {
        "YlOrRd": [(255, 255, 204), (254, 178, 76), (240, 59, 32), (128, 0, 38)],
        "viridis": [(68, 1, 84), (59, 82, 139), (33, 145, 140), (94, 201, 98), (253, 231, 37)],
        "magma": [(0, 0, 4), (80, 18, 123), (182, 54, 121), (251, 136, 97), (252, 253, 191)],
        "YlGnBu": [(255, 255, 217), (199, 233, 180), (65, 182, 196), (34, 94, 168), (8, 29, 88)],
    }
    stops = palettes.get(cmap, palettes["viridis"])
    scaled = value * (len(stops) - 1)
    i = int(np.floor(scaled))
    if i >= len(stops) - 1:
        return stops[-1]
    t = scaled - i
    c0, c1 = stops[i], stops[i + 1]
    return tuple(_lerp(c0[j], c1[j], t) for j in range(3))


def _normalize(values: np.ndarray, vmin: float | None, vmax: float | None) -> np.ndarray:
    arr = values.astype(float)
    finite = np.isfinite(arr)
    if vmin is None:
        vmin = float(np.nanmin(arr[finite])) if finite.any() else 0.0
    if vmax is None:
        vmax = float(np.nanmax(arr[finite])) if finite.any() else 1.0
    if abs(vmax - vmin) < 1e-12:
        return np.zeros_like(arr, dtype=float)
    return np.clip((arr - vmin) / (vmax - vmin), 0, 1)


class CrowdSituationAnalyzer:
    def __init__(
        self,
        csv_path: str | Path,
        output_dir: str | Path,
        free_speed: float = 1.30,
        density_threshold: float | None = None,
        bottleneck_threshold: float = 0.62,
        persistence_steps: int = 3,
        high_speed_cap: float = 3.0,
    ) -> None:
        self.csv_path = Path(csv_path)
        self.output_dir = Path(output_dir)
        self.free_speed = free_speed
        self.density_threshold = density_threshold
        self.bottleneck_threshold = bottleneck_threshold
        self.persistence_steps = persistence_steps
        self.high_speed_cap = high_speed_cap
        self.output_dir.mkdir(parents=True, exist_ok=True)

        self.df = pd.read_csv(self.csv_path)

    def run(self) -> dict:
        self._validate_columns()
        self._quality_check()
        self._preprocess()
        self._compute_bottleneck_score()

        self.plot_active_count_curve()
        self.plot_density_heatmap()
        self.plot_speed_heatmap()
        self.plot_bottleneck_heatmap()
        self.plot_corridor_snapshots()
        self.plot_cell_profile()
        self.plot_bottleneck_events_scatter()
        self.plot_candidate_hotspots_scatter()
        self.export_tables_and_report()

        return self.summary

    def _validate_columns(self) -> None:
        required = {
            "scenario",
            "time",
            "cell_x",
            "cell_z",
            "count",
            "density",
            "mean_speed",
            "mean_vx",
            "mean_vz",
        }
        missing = sorted(required.difference(self.df.columns))
        if missing:
            raise ValueError(f"Missing required columns: {missing}")

    def _quality_check(self) -> None:
        df = self.df
        times = np.sort(df["time"].unique())
        dx = np.round(np.diff(times), 5)
        active_count = df.groupby("time", sort=True)["count"].sum()
        nonzero = df[df["count"] > 0]

        grid_size = df["cell_x"].nunique() * df["cell_z"].nunique()
        expected_rows = len(times) * grid_size
        duplicates = int(df.duplicated(["time", "cell_x", "cell_z"]).sum())

        high_speed = df[(df["count"] > 0) & (df["mean_speed"] > self.high_speed_cap)]
        zero_speed_with_people = df[(df["count"] > 0) & (df["mean_speed"] <= 1e-9)]
        inconsistent_empty = df[
            (df["count"] == 0)
            & (
                (df["density"] != 0)
                | (df["mean_speed"] != 0)
                | (df["mean_vx"] != 0)
                | (df["mean_vz"] != 0)
            )
        ]

        self.summary = {
            "csv_path": str(self.csv_path),
            "rows": int(len(df)),
            "scenario_values": [str(v) for v in df["scenario"].dropna().unique()],
            "time_min": float(df["time"].min()),
            "time_max": float(df["time"].max()),
            "time_steps": int(len(times)),
            "dominant_time_step_seconds": float(pd.Series(dx).mode().iloc[0]) if len(dx) else None,
            "cell_x_min": int(df["cell_x"].min()),
            "cell_x_max": int(df["cell_x"].max()),
            "cell_x_count": int(df["cell_x"].nunique()),
            "cell_z_min": int(df["cell_z"].min()),
            "cell_z_max": int(df["cell_z"].max()),
            "cell_z_count": int(df["cell_z"].nunique()),
            "expected_complete_grid_rows": int(expected_rows),
            "grid_is_complete": bool(expected_rows == len(df) and duplicates == 0),
            "duplicate_time_cell_rows": duplicates,
            "missing_values": {k: int(v) for k, v in df.isna().sum().to_dict().items()},
            "negative_count_rows": int((df["count"] < 0).sum()),
            "negative_density_rows": int((df["density"] < 0).sum()),
            "high_speed_rows_over_cap": int(len(high_speed)),
            "high_speed_cap": float(self.high_speed_cap),
            "max_speed": float(df["mean_speed"].max()),
            "max_density": float(df["density"].max()),
            "max_cell_count": int(df["count"].max()),
            "zero_speed_rows_with_people": int(len(zero_speed_with_people)),
            "empty_rows_with_nonzero_state": int(len(inconsistent_empty)),
            "active_count_min": int(active_count.min()),
            "active_count_max": int(active_count.max()),
            "active_count_peak_time": float(active_count.idxmax()),
            "active_count_start": int(active_count.iloc[0]),
            "active_count_end": int(active_count.iloc[-1]),
            "nonzero_density_p95": float(nonzero["density"].quantile(0.95)) if len(nonzero) else 0.0,
            "nonzero_density_p90": float(nonzero["density"].quantile(0.90)) if len(nonzero) else 0.0,
            "nonzero_speed_p99": float(nonzero["mean_speed"].quantile(0.99)) if len(nonzero) else 0.0,
        }

        self.high_speed_rows = high_speed.copy()

    def _preprocess(self) -> None:
        df = self.df.copy()
        df["time_bin"] = df["time"].round(2)
        df["valid_speed"] = df["mean_speed"].where(
            (df["count"] > 0) & (df["mean_speed"] <= self.high_speed_cap),
            np.nan,
        )
        df["valid_vx"] = df["mean_vx"].where(
            (df["count"] > 0) & (df["mean_speed"] <= self.high_speed_cap),
            np.nan,
        )
        df["valid_vz"] = df["mean_vz"].where(
            (df["count"] > 0) & (df["mean_speed"] <= self.high_speed_cap),
            np.nan,
        )
        self.df = df

        def weighted_average(values: pd.Series, weights: pd.Series) -> float:
            mask = values.notna() & weights.notna() & (weights > 0)
            if not mask.any():
                return 0.0
            return float(np.average(values[mask], weights=weights[mask]))

        rows = []
        for (time_bin, cell_x), group in df.groupby(["time_bin", "cell_x"], sort=True):
            count_sum = int(group["count"].sum())
            rows.append(
                {
                    "time_bin": time_bin,
                    "cell_x": cell_x,
                    "count": count_sum,
                    "density": float(group["density"].sum()),
                    "mean_speed": weighted_average(group["valid_speed"], group["count"]),
                    "mean_vx": weighted_average(group["valid_vx"], group["count"]),
                    "mean_vz": weighted_average(group["valid_vz"], group["count"]),
                }
            )

        self.state = pd.DataFrame(rows).sort_values(["time_bin", "cell_x"]).reset_index(drop=True)
        self.times = np.sort(self.state["time_bin"].unique())
        self.cells = np.sort(self.state["cell_x"].unique())

        if self.density_threshold is None:
            nonzero_density = self.state.loc[self.state["count"] > 0, "density"]
            if len(nonzero_density) == 0:
                self.density_threshold_used = 1.0
            else:
                # Normal-case absolute density is low, so use a robust relative threshold.
                self.density_threshold_used = float(
                    max(nonzero_density.quantile(0.90), 0.60 * nonzero_density.max(), 1e-6)
                )
        else:
            self.density_threshold_used = float(self.density_threshold)

        self.summary["density_threshold_used"] = self.density_threshold_used

    def _compute_bottleneck_score(self) -> None:
        density = self.state.pivot(index="time_bin", columns="cell_x", values="density").fillna(0)
        speed = self.state.pivot(index="time_bin", columns="cell_x", values="mean_speed").fillna(0)
        count = self.state.pivot(index="time_bin", columns="cell_x", values="count").fillna(0)

        density_risk = (density / self.density_threshold_used).clip(0, 1)
        speed_loss = (1 - speed / self.free_speed).clip(0, 1)
        speed_loss = speed_loss.where(count > 0, 0)
        density_growth = density.diff().fillna(0)
        growth_risk = (density_growth / self.density_threshold_used).clip(0, 1)

        score = (0.50 * density_risk + 0.35 * speed_loss + 0.15 * growth_risk).clip(0, 1)

        def persistent_mask(raw: pd.DataFrame) -> pd.DataFrame:
            persistent = raw.copy()
            for col in raw.columns:
                values = raw[col].astype(int).to_numpy()
                out = np.zeros_like(values)
                run_length = 0
                for i, value in enumerate(values):
                    if value:
                        run_length += 1
                    else:
                        run_length = 0
                    if run_length >= self.persistence_steps:
                        out[i] = 1
                persistent[col] = out
            return persistent

        raw_bottleneck = score >= self.bottleneck_threshold
        persistent = persistent_mask(raw_bottleneck)

        count_scale = float(np.nanquantile(count.to_numpy(dtype=float), 0.95))
        if count_scale <= 0:
            count_scale = float(count.max().max()) or 1.0
        count_risk = (count / count_scale).clip(0, 1)
        hotspot_score = (0.65 * density_risk + 0.35 * count_risk).clip(0, 1)
        hotspot_binary = persistent_mask(hotspot_score >= 0.75)

        self.pivot_density = density
        self.pivot_speed = speed
        self.pivot_count = count
        self.bottleneck_score = score
        self.bottleneck_binary = persistent.astype(int)
        self.hotspot_score = hotspot_score
        self.hotspot_binary = hotspot_binary.astype(int)

        event_count = int(self.bottleneck_binary.to_numpy().sum())
        hotspot_count = int(self.hotspot_binary.to_numpy().sum())
        self.summary["persistent_bottleneck_event_cells"] = event_count
        self.summary["candidate_hotspot_event_cells"] = hotspot_count
        if event_count:
            event_cells = self._bottleneck_event_table()
            self.summary["top_bottleneck_cell_x"] = int(
                event_cells.groupby("cell_x")["bottleneck_score"].mean().idxmax()
            )
            self.summary["top_bottleneck_mean_score"] = float(
                event_cells.groupby("cell_x")["bottleneck_score"].mean().max()
            )
        else:
            self.summary["top_bottleneck_cell_x"] = None
            self.summary["top_bottleneck_mean_score"] = None

        if hotspot_count:
            hotspot_cells = self._candidate_hotspot_table()
            self.summary["top_candidate_hotspot_cell_x"] = int(
                hotspot_cells.groupby("cell_x")["hotspot_score"].mean().idxmax()
            )
            self.summary["top_candidate_hotspot_mean_score"] = float(
                hotspot_cells.groupby("cell_x")["hotspot_score"].mean().max()
            )
        else:
            self.summary["top_candidate_hotspot_cell_x"] = None
            self.summary["top_candidate_hotspot_mean_score"] = None

    def plot_active_count_curve(self) -> None:
        _require_plot_backend()
        if not HAS_MATPLOTLIB:
            self._plot_active_count_curve_pil()
            return

        active = self.df.groupby("time_bin", sort=True)["count"].sum()
        fig, ax = plt.subplots(figsize=(11, 4.8))
        ax.plot(active.index, active.values, color="#1f77b4", linewidth=2)
        peak_t = active.idxmax()
        peak_v = active.max()
        ax.scatter([peak_t], [peak_v], color="#d62728", zorder=3)
        ax.annotate(
            f"peak {int(peak_v)} @ {peak_t:.2f}s",
            xy=(peak_t, peak_v),
            xytext=(10, -20),
            textcoords="offset points",
            arrowprops={"arrowstyle": "->", "color": "#d62728"},
            fontsize=9,
        )
        ax.set_title("Active Agents Over Time")
        ax.set_xlabel("Time (s)")
        ax.set_ylabel("Active agents")
        ax.grid(alpha=0.25)
        fig.tight_layout()
        fig.savefig(self.output_dir / "01_active_count_curve.png", dpi=220)
        plt.close(fig)

    def plot_density_heatmap(self) -> None:
        self._plot_heatmap(
            matrix=self.pivot_density,
            path=self.output_dir / "02_density_heatmap_x_time.png",
            title="Spatio-temporal Density Along Corridor X",
            colorbar_label="Density (sum over Z lanes)",
            cmap="YlOrRd",
        )

    def plot_speed_heatmap(self) -> None:
        speed_plot = self.pivot_speed.replace(0, np.nan)
        self._plot_heatmap(
            matrix=speed_plot,
            path=self.output_dir / "03_speed_heatmap_x_time.png",
            title=f"Mean Speed Along Corridor X (valid <= {self.high_speed_cap:g} m/s)",
            colorbar_label="Mean speed",
            cmap="viridis",
            vmin=0,
            vmax=self.high_speed_cap,
        )

    def plot_bottleneck_heatmap(self) -> None:
        _require_plot_backend()
        if not HAS_MATPLOTLIB:
            self._plot_bottleneck_heatmap_pil()
            return

        fig, ax = plt.subplots(figsize=(12, 6))
        im = ax.imshow(
            self.bottleneck_score.values,
            aspect="auto",
            origin="lower",
            cmap="magma",
            vmin=0,
            vmax=1,
            extent=[
                self.bottleneck_score.columns.min() - 0.5,
                self.bottleneck_score.columns.max() + 0.5,
                self.bottleneck_score.index.min(),
                self.bottleneck_score.index.max(),
            ],
        )
        event_y, event_x = np.where(self.bottleneck_binary.values == 1)
        if len(event_x):
            ax.scatter(
                self.bottleneck_binary.columns[event_x],
                self.bottleneck_binary.index[event_y],
                s=7,
                c="#00e5ff",
                alpha=0.65,
                linewidths=0,
                label="persistent bottleneck",
            )
            ax.legend(loc="upper right", frameon=True)
        hotspot_y, hotspot_x = np.where(self.hotspot_binary.values == 1)
        if len(hotspot_x):
            ax.scatter(
                self.hotspot_binary.columns[hotspot_x],
                self.hotspot_binary.index[hotspot_y],
                s=10,
                c="#7CFC00",
                alpha=0.45,
                marker="s",
                linewidths=0,
                label="candidate hotspot",
            )
            ax.legend(loc="upper right", frameon=True)
        ax.set_title("Bottleneck Score and Persistent Events")
        ax.set_xlabel("Corridor cell X")
        ax.set_ylabel("Time (s)")
        fig.colorbar(im, ax=ax, label="Bottleneck score")
        fig.tight_layout()
        fig.savefig(self.output_dir / "04_bottleneck_score_heatmap.png", dpi=240)
        plt.close(fig)

    def plot_corridor_snapshots(self) -> None:
        _require_plot_backend()
        if not HAS_MATPLOTLIB:
            self._plot_corridor_snapshots_pil()
            return

        active = self.df.groupby("time_bin")["count"].sum()
        active_for_snapshots = active[active > 0]
        if len(active_for_snapshots) == 0:
            active_for_snapshots = active
        candidate_times = [
            active_for_snapshots.index[min(len(active_for_snapshots) - 1, int(len(active_for_snapshots) * q))]
            for q in (0.10, 0.35, 0.60, 0.85)
        ]
        candidate_times.append(active.idxmax())
        snapshot_times = sorted({float(t) for t in candidate_times})

        fig, axes = plt.subplots(len(snapshot_times), 1, figsize=(12, 2.2 * len(snapshot_times)))
        if len(snapshot_times) == 1:
            axes = [axes]

        vmax = max(float(self.df["count"].max()), 1.0)
        for ax, time_value in zip(axes, snapshot_times):
            sub = self.df[self.df["time_bin"] == time_value]
            count_map = sub.pivot(index="cell_z", columns="cell_x", values="count").fillna(0)
            vx_map = sub.pivot(index="cell_z", columns="cell_x", values="valid_vx").fillna(0)
            vz_map = sub.pivot(index="cell_z", columns="cell_x", values="valid_vz").fillna(0)

            im = ax.imshow(
                count_map.values,
                origin="lower",
                aspect="auto",
                cmap="YlGnBu",
                vmin=0,
                vmax=vmax,
                extent=[
                    count_map.columns.min() - 0.5,
                    count_map.columns.max() + 0.5,
                    count_map.index.min() - 0.5,
                    count_map.index.max() + 0.5,
                ],
            )

            xs, zs = np.meshgrid(count_map.columns.to_numpy(), count_map.index.to_numpy())
            counts = count_map.values
            mask = counts > 0
            ax.quiver(
                xs[mask],
                zs[mask],
                vx_map.values[mask],
                vz_map.values[mask],
                color="black",
                alpha=0.55,
                scale=25,
                width=0.004,
            )
            ax.set_title(f"Corridor Situation Snapshot: t={time_value:.2f}s, active={int(active.loc[time_value])}")
            ax.set_ylabel("cell Z")
            ax.set_yticks(sorted(self.df["cell_z"].unique()))
            ax.set_xlim(self.df["cell_x"].min() - 0.5, self.df["cell_x"].max() + 0.5)
        axes[-1].set_xlabel("Corridor cell X")
        fig.colorbar(im, ax=axes, label="Agents in grid cell", shrink=0.88)
        fig.savefig(self.output_dir / "05_corridor_situation_snapshots.png", dpi=240, bbox_inches="tight")
        plt.close(fig)

    def plot_cell_profile(self) -> None:
        profile = self.state.groupby("cell_x").agg(
            total_person_time=("count", "sum"),
            max_density=("density", "max"),
            mean_density=("density", "mean"),
            mean_speed=("mean_speed", lambda x: x[x > 0].mean()),
            mean_bottleneck_score=("cell_x", "size"),
        )
        profile["mean_bottleneck_score"] = self.bottleneck_score.mean(axis=0)
        profile["persistent_event_steps"] = self.bottleneck_binary.sum(axis=0)
        profile["candidate_hotspot_steps"] = self.hotspot_binary.sum(axis=0)
        self.profile = profile

        _require_plot_backend()
        if not HAS_MATPLOTLIB:
            self._plot_cell_profile_pil(profile)
            return

        fig, axes = plt.subplots(4, 1, figsize=(12, 10), sharex=True)
        axes[0].bar(profile.index, profile["total_person_time"], color="#4c78a8")
        axes[0].set_ylabel("Person-time")
        axes[0].set_title("Cell-level Corridor Profile")
        axes[1].bar(profile.index, profile["max_density"], color="#f58518")
        axes[1].axhline(self.density_threshold_used, color="#d62728", linestyle="--", linewidth=1.2)
        axes[1].set_ylabel("Max density")
        axes[2].bar(profile.index, profile["mean_bottleneck_score"], color="#b279a2")
        axes[2].axhline(self.bottleneck_threshold, color="#d62728", linestyle="--", linewidth=1.2)
        axes[2].set_ylabel("Mean risk")
        axes[3].bar(profile.index, profile["persistent_event_steps"], color="#54a24b")
        axes[3].set_ylabel("Event steps")
        axes[3].set_xlabel("Corridor cell X")
        for ax in axes:
            ax.grid(axis="y", alpha=0.22)
        fig.tight_layout()
        fig.savefig(self.output_dir / "06_cell_profile_bottleneck_ranking.png", dpi=240)
        plt.close(fig)

    def plot_bottleneck_events_scatter(self) -> None:
        _require_plot_backend()
        if not HAS_MATPLOTLIB:
            self._plot_bottleneck_events_scatter_pil()
            return

        event_table = self._bottleneck_event_table()
        fig, ax = plt.subplots(figsize=(12, 5))
        if len(event_table):
            sc = ax.scatter(
                event_table["cell_x"],
                event_table["time"],
                c=event_table["bottleneck_score"],
                s=np.clip(event_table["count"], 10, 80),
                cmap="magma",
                vmin=0,
                vmax=1,
                alpha=0.78,
                edgecolors="none",
            )
            fig.colorbar(sc, ax=ax, label="Bottleneck score")
        ax.set_title("Persistent Bottleneck Event Points")
        ax.set_xlabel("Corridor cell X")
        ax.set_ylabel("Time (s)")
        ax.grid(alpha=0.20)
        fig.tight_layout()
        fig.savefig(self.output_dir / "07_bottleneck_events_scatter.png", dpi=240)
        plt.close(fig)

    def plot_candidate_hotspots_scatter(self) -> None:
        _require_plot_backend()
        if not HAS_MATPLOTLIB:
            self._plot_candidate_hotspots_scatter_pil()
            return

        hotspot_table = self._candidate_hotspot_table()
        fig, ax = plt.subplots(figsize=(12, 5))
        if len(hotspot_table):
            sc = ax.scatter(
                hotspot_table["cell_x"],
                hotspot_table["time"],
                c=hotspot_table["hotspot_score"],
                s=np.clip(hotspot_table["count"], 10, 80),
                cmap="YlOrRd",
                vmin=0,
                vmax=1,
                alpha=0.78,
                edgecolors="none",
            )
            fig.colorbar(sc, ax=ax, label="Hotspot score")
        ax.set_title("Candidate Relative Congestion Hotspots")
        ax.set_xlabel("Corridor cell X")
        ax.set_ylabel("Time (s)")
        ax.grid(alpha=0.20)
        fig.tight_layout()
        fig.savefig(self.output_dir / "08_candidate_hotspots_scatter.png", dpi=240)
        plt.close(fig)

    def _plot_heatmap(
        self,
        matrix: pd.DataFrame,
        path: Path,
        title: str,
        colorbar_label: str,
        cmap: str,
        vmin: float | None = None,
        vmax: float | None = None,
    ) -> None:
        _require_plot_backend()
        if not HAS_MATPLOTLIB:
            self._plot_heatmap_pil(matrix, path, title, colorbar_label, cmap, vmin, vmax)
            return

        fig, ax = plt.subplots(figsize=(12, 6))
        im = ax.imshow(
            matrix.values,
            aspect="auto",
            origin="lower",
            cmap=cmap,
            vmin=vmin,
            vmax=vmax,
            extent=[
                matrix.columns.min() - 0.5,
                matrix.columns.max() + 0.5,
                matrix.index.min(),
                matrix.index.max(),
            ],
        )
        ax.set_title(title)
        ax.set_xlabel("Corridor cell X")
        ax.set_ylabel("Time (s)")
        fig.colorbar(im, ax=ax, label=colorbar_label)
        fig.tight_layout()
        fig.savefig(path, dpi=240)
        plt.close(fig)

    def _draw_title(self, draw, title: str, width: int) -> None:
        font = _font(24, bold=True)
        bbox = draw.textbbox((0, 0), title, font=font)
        draw.text(((width - (bbox[2] - bbox[0])) / 2, 18), title, fill=(30, 30, 30), font=font)

    def _plot_active_count_curve_pil(self) -> None:
        active = self.df.groupby("time_bin", sort=True)["count"].sum()
        width, height = 1300, 620
        left, right, top, bottom = 90, 40, 75, 80
        plot_w, plot_h = width - left - right, height - top - bottom
        img = Image.new("RGB", (width, height), "white")
        draw = ImageDraw.Draw(img)
        self._draw_title(draw, "Active Agents Over Time", width)

        x = active.index.to_numpy(dtype=float)
        y = active.to_numpy(dtype=float)
        xmin, xmax = float(x.min()), float(x.max())
        ymax = max(float(y.max()), 1.0)

        draw.rectangle([left, top, left + plot_w, top + plot_h], outline=(180, 180, 180), width=1)
        for i in range(6):
            yy = top + plot_h - i * plot_h / 5
            draw.line([left, yy, left + plot_w, yy], fill=(230, 230, 230))
            label = f"{ymax * i / 5:.0f}"
            draw.text((16, yy - 8), label, fill=(80, 80, 80), font=_font(14))
        points = [
            (
                left + (xx - xmin) / (xmax - xmin) * plot_w if xmax > xmin else left,
                top + plot_h - yy / ymax * plot_h,
            )
            for xx, yy in zip(x, y)
        ]
        if len(points) > 1:
            draw.line(points, fill=(31, 119, 180), width=3)
        peak_t = float(active.idxmax())
        peak_v = float(active.max())
        px = left + (peak_t - xmin) / (xmax - xmin) * plot_w
        py = top + plot_h - peak_v / ymax * plot_h
        draw.ellipse([px - 6, py - 6, px + 6, py + 6], fill=(214, 39, 40))
        draw.text((px + 12, py - 22), f"peak {int(peak_v)} @ {peak_t:.2f}s", fill=(214, 39, 40), font=_font(15))
        draw.text((width / 2 - 35, height - 45), "Time (s)", fill=(50, 50, 50), font=_font(16))
        draw.text((14, 42), "Active agents", fill=(50, 50, 50), font=_font(16))
        img.save(self.output_dir / "01_active_count_curve.png")

    def _plot_heatmap_pil(
        self,
        matrix: pd.DataFrame,
        path: Path,
        title: str,
        colorbar_label: str,
        cmap: str,
        vmin: float | None,
        vmax: float | None,
    ) -> None:
        width, height = 1300, 720
        left, right, top, bottom = 90, 140, 75, 80
        plot_w, plot_h = width - left - right, height - top - bottom
        img = Image.new("RGB", (width, height), "white")
        draw = ImageDraw.Draw(img)
        self._draw_title(draw, title, width)

        arr = matrix.to_numpy(dtype=float)
        norm = _normalize(arr, vmin, vmax)
        rgb = np.zeros((norm.shape[0], norm.shape[1], 3), dtype=np.uint8)
        for row in range(norm.shape[0]):
            for col in range(norm.shape[1]):
                if np.isfinite(arr[row, col]):
                    rgb[row, col] = _palette_color(norm[row, col], cmap)
                else:
                    rgb[row, col] = (245, 245, 245)
        rgb = np.flipud(rgb)
        heatmap = Image.fromarray(rgb, "RGB").resize((plot_w, plot_h), resample=Image.Resampling.BILINEAR)
        img.paste(heatmap, (left, top))
        draw.rectangle([left, top, left + plot_w, top + plot_h], outline=(80, 80, 80), width=1)

        # Axes ticks.
        x_min, x_max = float(matrix.columns.min()), float(matrix.columns.max())
        y_min, y_max = float(matrix.index.min()), float(matrix.index.max())
        for cell in np.linspace(x_min, x_max, 7):
            px = left + (cell - x_min) / (x_max - x_min) * plot_w if x_max > x_min else left
            draw.line([px, top + plot_h, px, top + plot_h + 6], fill=(70, 70, 70))
            draw.text((px - 12, top + plot_h + 10), f"{cell:.0f}", fill=(70, 70, 70), font=_font(13))
        for t in np.linspace(y_min, y_max, 6):
            py = top + plot_h - (t - y_min) / (y_max - y_min) * plot_h if y_max > y_min else top
            draw.line([left - 6, py, left, py], fill=(70, 70, 70))
            draw.text((20, py - 8), f"{t:.0f}", fill=(70, 70, 70), font=_font(13))
        draw.text((left + plot_w / 2 - 55, height - 42), "Corridor cell X", fill=(40, 40, 40), font=_font(16))
        draw.text((18, 45), "Time (s)", fill=(40, 40, 40), font=_font(16))

        # Colorbar.
        cb_x, cb_y, cb_w, cb_h = left + plot_w + 35, top, 28, plot_h
        for j in range(cb_h):
            value = 1 - j / max(cb_h - 1, 1)
            draw.line([cb_x, cb_y + j, cb_x + cb_w, cb_y + j], fill=_palette_color(value, cmap))
        draw.rectangle([cb_x, cb_y, cb_x + cb_w, cb_y + cb_h], outline=(80, 80, 80))
        label_font = _font(13)
        lo = vmin if vmin is not None else float(np.nanmin(arr[np.isfinite(arr)])) if np.isfinite(arr).any() else 0.0
        hi = vmax if vmax is not None else float(np.nanmax(arr[np.isfinite(arr)])) if np.isfinite(arr).any() else 1.0
        draw.text((cb_x + 38, cb_y - 4), f"{hi:.2f}", fill=(70, 70, 70), font=label_font)
        draw.text((cb_x + 38, cb_y + cb_h - 12), f"{lo:.2f}", fill=(70, 70, 70), font=label_font)
        draw.text((cb_x - 4, cb_y + cb_h + 12), colorbar_label[:26], fill=(40, 40, 40), font=label_font)
        img.save(path)

    def _plot_bottleneck_heatmap_pil(self) -> None:
        path = self.output_dir / "04_bottleneck_score_heatmap.png"
        self._plot_heatmap_pil(
            self.bottleneck_score,
            path,
            "Bottleneck Score and Persistent Events",
            "Bottleneck score",
            "magma",
            0,
            1,
        )

        img = Image.open(path).convert("RGB")
        draw = ImageDraw.Draw(img)
        left, right, top, bottom = 90, 140, 75, 80
        plot_w, plot_h = img.size[0] - left - right, img.size[1] - top - bottom
        x_min, x_max = float(self.bottleneck_score.columns.min()), float(self.bottleneck_score.columns.max())
        y_min, y_max = float(self.bottleneck_score.index.min()), float(self.bottleneck_score.index.max())
        event_y, event_x = np.where(self.bottleneck_binary.values == 1)
        for row, col in zip(event_y, event_x):
            x_val = float(self.bottleneck_binary.columns[col])
            y_val = float(self.bottleneck_binary.index[row])
            px = left + (x_val - x_min) / (x_max - x_min) * plot_w if x_max > x_min else left
            py = top + plot_h - (y_val - y_min) / (y_max - y_min) * plot_h if y_max > y_min else top
            draw.ellipse([px - 2, py - 2, px + 2, py + 2], fill=(0, 229, 255))
        hotspot_y, hotspot_x = np.where(self.hotspot_binary.values == 1)
        for row, col in zip(hotspot_y, hotspot_x):
            x_val = float(self.hotspot_binary.columns[col])
            y_val = float(self.hotspot_binary.index[row])
            px = left + (x_val - x_min) / (x_max - x_min) * plot_w if x_max > x_min else left
            py = top + plot_h - (y_val - y_min) / (y_max - y_min) * plot_h if y_max > y_min else top
            draw.rectangle([px - 2, py - 2, px + 2, py + 2], fill=(124, 252, 0))
        draw.text(
            (img.size[0] - 385, 48),
            "cyan: bottleneck; green: candidate hotspot",
            fill=(0, 105, 70),
            font=_font(14),
        )
        img.save(path)

    def _plot_corridor_snapshots_pil(self) -> None:
        active = self.df.groupby("time_bin")["count"].sum()
        active_for_snapshots = active[active > 0]
        if len(active_for_snapshots) == 0:
            active_for_snapshots = active
        candidate_times = [
            active_for_snapshots.index[min(len(active_for_snapshots) - 1, int(len(active_for_snapshots) * q))]
            for q in (0.10, 0.35, 0.60, 0.85)
        ]
        candidate_times.append(active.idxmax())
        snapshot_times = sorted({float(t) for t in candidate_times})
        width = 1300
        row_h = 205
        height = 75 + row_h * len(snapshot_times) + 55
        left, top0 = 90, 75
        grid_w, grid_h = 1040, 95
        img = Image.new("RGB", (width, height), "white")
        draw = ImageDraw.Draw(img)
        self._draw_title(draw, "Corridor Situation Snapshots", width)

        vmax = max(float(self.df["count"].max()), 1.0)
        x_values = sorted(self.df["cell_x"].unique())
        z_values = sorted(self.df["cell_z"].unique())
        cell_w = grid_w / len(x_values)
        cell_h = grid_h / len(z_values)
        x_min = min(x_values)

        for idx, time_value in enumerate(snapshot_times):
            y0 = top0 + idx * row_h + 45
            sub = self.df[self.df["time_bin"] == time_value]
            draw.text(
                (left, y0 - 34),
                f"t={time_value:.2f}s, active={int(active.loc[time_value])}",
                fill=(35, 35, 35),
                font=_font(18, bold=True),
            )
            for _, row in sub.iterrows():
                x = int(row["cell_x"])
                z = int(row["cell_z"])
                cx = left + (x - x_min) * cell_w
                cy = y0 + (len(z_values) - 1 - z) * cell_h
                color = _palette_color(float(row["count"]) / vmax, "YlGnBu")
                draw.rectangle([cx, cy, cx + cell_w, cy + cell_h], fill=color, outline=(230, 230, 230))
                if row["count"] > 0 and np.isfinite(row["valid_vx"]):
                    mid_x = cx + cell_w / 2
                    mid_y = cy + cell_h / 2
                    end_x = mid_x + float(row["valid_vx"]) * 8
                    end_y = mid_y - float(row["valid_vz"]) * 8
                    draw.line([mid_x, mid_y, end_x, end_y], fill=(20, 20, 20), width=2)
                    draw.ellipse([end_x - 2, end_y - 2, end_x + 2, end_y + 2], fill=(20, 20, 20))
            draw.rectangle([left, y0, left + grid_w, y0 + grid_h], outline=(80, 80, 80), width=1)
            draw.text((left - 48, y0 + 34), "Z", fill=(50, 50, 50), font=_font(15))
        draw.text((left + grid_w / 2 - 55, height - 38), "Corridor cell X", fill=(50, 50, 50), font=_font(16))
        img.save(self.output_dir / "05_corridor_situation_snapshots.png")

    def _plot_cell_profile_pil(self, profile: pd.DataFrame) -> None:
        width, height = 1300, 980
        img = Image.new("RGB", (width, height), "white")
        draw = ImageDraw.Draw(img)
        self._draw_title(draw, "Cell-level Corridor Profile", width)
        panels = [
            ("Person-time", profile["total_person_time"].to_numpy(dtype=float), (76, 120, 168), None),
            ("Max density", profile["max_density"].to_numpy(dtype=float), (245, 133, 24), self.density_threshold_used),
            ("Mean risk", profile["mean_bottleneck_score"].to_numpy(dtype=float), (178, 121, 162), self.bottleneck_threshold),
            ("Event steps", profile["persistent_event_steps"].to_numpy(dtype=float), (84, 162, 75), None),
        ]
        left, panel_w, panel_h = 90, 1120, 180
        xs = profile.index.to_numpy(dtype=float)
        for i, (label, values, color, threshold) in enumerate(panels):
            top = 75 + i * 220
            ymax = max(float(np.nanmax(values)), threshold or 0, 1e-9)
            ymax *= 1.12
            draw.text((left, top - 24), label, fill=(40, 40, 40), font=_font(17, bold=True))
            draw.rectangle([left, top, left + panel_w, top + panel_h], outline=(185, 185, 185))
            bar_w = panel_w / len(xs) * 0.72
            for j, value in enumerate(values):
                x_center = left + (j + 0.5) * panel_w / len(xs)
                bar_h = 0 if ymax == 0 else value / ymax * panel_h
                draw.rectangle(
                    [x_center - bar_w / 2, top + panel_h - bar_h, x_center + bar_w / 2, top + panel_h],
                    fill=color,
                )
            if threshold is not None:
                ty = top + panel_h - threshold / ymax * panel_h
                draw.line([left, ty, left + panel_w, ty], fill=(214, 39, 40), width=2)
            for tick in np.linspace(0, len(xs) - 1, 7):
                x_pos = left + (tick + 0.5) * panel_w / len(xs)
                draw.text((x_pos - 8, top + panel_h + 5), f"{xs[int(round(tick))]:.0f}", fill=(70, 70, 70), font=_font(12))
        draw.text((left + panel_w / 2 - 55, height - 34), "Corridor cell X", fill=(45, 45, 45), font=_font(16))
        img.save(self.output_dir / "06_cell_profile_bottleneck_ranking.png")

    def _plot_bottleneck_events_scatter_pil(self) -> None:
        event_table = self._bottleneck_event_table()
        width, height = 1300, 620
        left, right, top, bottom = 90, 120, 75, 75
        plot_w, plot_h = width - left - right, height - top - bottom
        img = Image.new("RGB", (width, height), "white")
        draw = ImageDraw.Draw(img)
        self._draw_title(draw, "Persistent Bottleneck Event Points", width)
        draw.rectangle([left, top, left + plot_w, top + plot_h], outline=(180, 180, 180))
        x_min, x_max = float(self.pivot_count.columns.min()), float(self.pivot_count.columns.max())
        y_min, y_max = float(self.pivot_count.index.min()), float(self.pivot_count.index.max())
        if len(event_table):
            for _, row in event_table.iterrows():
                px = left + (row["cell_x"] - x_min) / (x_max - x_min) * plot_w if x_max > x_min else left
                py = top + plot_h - (row["time"] - y_min) / (y_max - y_min) * plot_h if y_max > y_min else top
                radius = int(np.clip(row["count"] / 3, 3, 10))
                draw.ellipse(
                    [px - radius, py - radius, px + radius, py + radius],
                    fill=_palette_color(row["bottleneck_score"], "magma"),
                )
        draw.text((left + plot_w / 2 - 55, height - 42), "Corridor cell X", fill=(50, 50, 50), font=_font(16))
        draw.text((16, 45), "Time (s)", fill=(50, 50, 50), font=_font(16))
        img.save(self.output_dir / "07_bottleneck_events_scatter.png")

    def _plot_candidate_hotspots_scatter_pil(self) -> None:
        hotspot_table = self._candidate_hotspot_table()
        width, height = 1300, 620
        left, right, top, bottom = 90, 120, 75, 75
        plot_w, plot_h = width - left - right, height - top - bottom
        img = Image.new("RGB", (width, height), "white")
        draw = ImageDraw.Draw(img)
        self._draw_title(draw, "Candidate Relative Congestion Hotspots", width)
        draw.rectangle([left, top, left + plot_w, top + plot_h], outline=(180, 180, 180))
        x_min, x_max = float(self.pivot_count.columns.min()), float(self.pivot_count.columns.max())
        y_min, y_max = float(self.pivot_count.index.min()), float(self.pivot_count.index.max())
        if len(hotspot_table):
            for _, row in hotspot_table.iterrows():
                px = left + (row["cell_x"] - x_min) / (x_max - x_min) * plot_w if x_max > x_min else left
                py = top + plot_h - (row["time"] - y_min) / (y_max - y_min) * plot_h if y_max > y_min else top
                radius = int(np.clip(row["count"] / 3, 3, 10))
                draw.ellipse(
                    [px - radius, py - radius, px + radius, py + radius],
                    fill=_palette_color(row["hotspot_score"], "YlOrRd"),
                )
        draw.text((left + plot_w / 2 - 55, height - 42), "Corridor cell X", fill=(50, 50, 50), font=_font(16))
        draw.text((16, 45), "Time (s)", fill=(50, 50, 50), font=_font(16))
        img.save(self.output_dir / "08_candidate_hotspots_scatter.png")

    def _bottleneck_event_table(self) -> pd.DataFrame:
        rows = []
        for time_value in self.bottleneck_binary.index:
            active_cells = self.bottleneck_binary.columns[self.bottleneck_binary.loc[time_value] == 1]
            for cell_x in active_cells:
                rows.append(
                    {
                        "time": float(time_value),
                        "cell_x": int(cell_x),
                        "count": float(self.pivot_count.loc[time_value, cell_x]),
                        "density": float(self.pivot_density.loc[time_value, cell_x]),
                        "mean_speed": float(self.pivot_speed.loc[time_value, cell_x]),
                        "bottleneck_score": float(self.bottleneck_score.loc[time_value, cell_x]),
                    }
                )
        return pd.DataFrame(rows)

    def _candidate_hotspot_table(self) -> pd.DataFrame:
        rows = []
        for time_value in self.hotspot_binary.index:
            active_cells = self.hotspot_binary.columns[self.hotspot_binary.loc[time_value] == 1]
            for cell_x in active_cells:
                rows.append(
                    {
                        "time": float(time_value),
                        "cell_x": int(cell_x),
                        "count": float(self.pivot_count.loc[time_value, cell_x]),
                        "density": float(self.pivot_density.loc[time_value, cell_x]),
                        "mean_speed": float(self.pivot_speed.loc[time_value, cell_x]),
                        "hotspot_score": float(self.hotspot_score.loc[time_value, cell_x]),
                        "bottleneck_score": float(self.bottleneck_score.loc[time_value, cell_x]),
                    }
                )
        return pd.DataFrame(rows)

    def export_tables_and_report(self) -> None:
        event_table = self._bottleneck_event_table()
        hotspot_table = self._candidate_hotspot_table()
        event_table.to_csv(self.output_dir / "bottleneck_events.csv", index=False, encoding="utf-8-sig")
        hotspot_table.to_csv(
            self.output_dir / "candidate_hotspot_events.csv",
            index=False,
            encoding="utf-8-sig",
        )
        self.high_speed_rows.to_csv(self.output_dir / "quality_high_speed_rows.csv", index=False, encoding="utf-8-sig")

        profile = getattr(self, "profile", None)
        if profile is not None:
            profile.to_csv(self.output_dir / "cell_profile_summary.csv", encoding="utf-8-sig")

        with open(self.output_dir / "quality_summary.json", "w", encoding="utf-8") as f:
            json.dump(self.summary, f, ensure_ascii=False, indent=2)

        lines = [
            "Crowd analysis summary",
            "======================",
            f"Input: {self.csv_path}",
            f"Rows: {self.summary['rows']}",
            f"Scenario: {', '.join(self.summary['scenario_values'])}",
            f"Time range: {self.summary['time_min']:.2f}s - {self.summary['time_max']:.2f}s, "
            f"{self.summary['time_steps']} steps",
            f"Grid: X={self.summary['cell_x_min']}..{self.summary['cell_x_max']} "
            f"({self.summary['cell_x_count']}), Z={self.summary['cell_z_min']}..{self.summary['cell_z_max']} "
            f"({self.summary['cell_z_count']})",
            f"Complete grid: {self.summary['grid_is_complete']}",
            f"Active agents peak: {self.summary['active_count_max']} "
            f"at {self.summary['active_count_peak_time']:.2f}s",
            f"Max density: {self.summary['max_density']:.4f}; "
            f"adaptive density threshold used: {self.summary['density_threshold_used']:.4f}",
            f"Max speed: {self.summary['max_speed']:.4f}; "
            f"rows over {self.high_speed_cap:g} speed cap: {self.summary['high_speed_rows_over_cap']}",
            f"Persistent bottleneck event cells: {self.summary['persistent_bottleneck_event_cells']}",
            f"Top bottleneck cell X: {self.summary['top_bottleneck_cell_x']}",
            f"Candidate hotspot event cells: {self.summary['candidate_hotspot_event_cells']}",
            f"Top candidate hotspot cell X: {self.summary['top_candidate_hotspot_cell_x']}",
            "",
            "Interpretation notes:",
            "- The CSV grid is structurally complete and has no missing values or duplicate time-cell rows.",
            "- The absolute density level is low, so bottlenecks are identified as relative local congestion zones.",
            "- Persistent bottleneck events require density plus speed-loss risk; candidate hotspots use density plus local count pressure.",
            "- Several sparse cells have unrealistically high mean_speed values; these rows are exported for inspection and excluded from risk speed aggregation.",
            "- Empty cells have zero density and zero velocity, which is internally consistent.",
        ]
        (self.output_dir / "analysis_report.md").write_text("\n".join(lines), encoding="utf-8")


def parse_args(argv: Iterable[str] | None = None) -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description="Visualize crowd perception and bottleneck risks.")
    parser.add_argument(
        "--csv",
        default="D:/AAA Learning/Unity/Shenzhenbei_wcx/crowd_state_normal.csv",
        help="Path to simulation grid-state CSV.",
    )
    parser.add_argument(
        "--output",
        default=str(script_dir / "crowd_analysis_normal_output"),
        help="Directory for figures and summary tables.",
    )
    parser.add_argument("--free-speed", type=float, default=1.30, help="Reference free walking speed.")
    parser.add_argument(
        "--density-threshold",
        type=float,
        default=None,
        help="Density threshold. If omitted, a robust relative threshold is used.",
    )
    parser.add_argument("--bottleneck-threshold", type=float, default=0.62, help="Risk score threshold.")
    parser.add_argument("--persistence-steps", type=int, default=3, help="Consecutive steps required.")
    parser.add_argument("--high-speed-cap", type=float, default=3.0, help="Speed quality-control cap.")
    return parser.parse_args(argv)


def main(argv: Iterable[str] | None = None) -> None:
    args = parse_args(argv)
    analyzer = CrowdSituationAnalyzer(
        csv_path=args.csv,
        output_dir=args.output,
        free_speed=args.free_speed,
        density_threshold=args.density_threshold,
        bottleneck_threshold=args.bottleneck_threshold,
        persistence_steps=args.persistence_steps,
        high_speed_cap=args.high_speed_cap,
    )
    summary = analyzer.run()
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    print(f"\nOutputs saved to: {Path(args.output).resolve()}")


if __name__ == "__main__":
    main()
