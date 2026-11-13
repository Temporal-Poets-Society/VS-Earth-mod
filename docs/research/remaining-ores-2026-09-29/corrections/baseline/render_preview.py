"""Render an evidence preview, not an ore probability or record-density heatmap.

Uses matplotlib/numpy from a temporary research environment. Inputs are the
normalized source data and Earth's existing unwarped geographic terrain flags.
"""
from pathlib import Path
import argparse
import csv
import hashlib
import json
import sys
import tempfile

import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.colors import ListedColormap
from matplotlib.lines import Line2D
from matplotlib.patches import Patch, PathPatch
from matplotlib.path import Path as PlotPath
from matplotlib.ticker import FuncFormatter

HERE = Path(__file__).resolve().parent
EARTH = HERE.parents[2]
DATA = HERE.parent / "copper-tin-global-2026-09-29" / "normalized"


def read_points():
    points = {"Cu": [], "Sn": []}
    counts = {}
    for name in ["mrds-copper-tin-occurrences.csv", "global-copper-assessment-occurrences.csv"]:
        count = 0
        with (DATA / name).open(encoding="utf-8", newline="") as f:
            for row in csv.DictReader(f):
                if row["heatmap_eligible"].lower() != "true":
                    continue
                lon, lat = float(row["longitude"]), float(row["latitude"])
                assert np.isfinite(lon) and np.isfinite(lat)
                assert -180.000001 <= lon <= 180.000001 and -90 <= lat <= 90
                points[row["commodity"]].append((lon, lat))
                count += 1
        counts[name] = count
    return {k: np.asarray(v) for k, v in points.items()}, counts


def plot_polygon(ax, rings, color, hatch):
    # Unwrap each ring and align holes to its shell, then clip repeated copies
    # at map edges. This avoids drawing a false band across the Pacific seam.
    vertices, codes = [], []
    centre = None
    for ring in rings:
        coords = np.asarray(ring, dtype=float)[:, :2].copy()
        assert len(coords) >= 4
        coords[:, 0] = np.rad2deg(np.unwrap(np.deg2rad(coords[:, 0])))
        if centre is None:
            centre = np.mean(coords[:, 0])
        else:
            coords[:, 0] += 360 * round((centre - np.mean(coords[:, 0])) / 360)
        vertices.extend(coords.tolist())
        codes.extend([PlotPath.MOVETO] + [PlotPath.LINETO] * (len(coords) - 2) + [PlotPath.CLOSEPOLY])
    vertices = np.asarray(vertices)
    # Bring shell centre into map range before considering adjacent copies.
    vertices[:, 0] -= 360 * np.floor((centre + 180) / 360)
    for shift in [-360, 0, 360]:
        shifted = vertices + np.array([shift, 0])
        if shifted[:, 0].max() < -180 or shifted[:, 0].min() > 180:
            continue
        ax.add_patch(PathPatch(PlotPath(shifted, codes), facecolor=color,
                              edgecolor=color, linewidth=0.25, alpha=0.38,
                              hatch=hatch, zorder=2, clip_on=True))


def plot_tracts(ax, path, color, hatch):
    obj = json.loads(path.read_text(encoding="utf-8"))
    count = 0
    excluded = 0
    for feature in obj["features"]:
        if feature["properties"].get("geometry_valid") is False:
            excluded += 1
            continue
        geom = feature["geometry"]
        if geom is None:
            continue
        polygons = [geom["coordinates"]] if geom["type"] == "Polygon" else geom["coordinates"]
        assert geom["type"] in ["Polygon", "MultiPolygon"]
        for rings in polygons:
            plot_polygon(ax, rings, color, hatch)
        count += 1
    return {"shown": count, "invalid_source_geometry_withheld": excluded}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--porphyry", type=Path, required=True)
    ap.add_argument("--sedimentary", type=Path, required=True)
    args = ap.parse_args()
    points, input_counts = read_points()
    # Equal-strength presence cells: repeated reports never darken the same cell.
    # This reduces visual sampling-density bias without pretending countries were surveyed equally.
    cells = {}
    for commodity, coords in points.items():
        indices = np.floor(coords + np.array([180, 90])).astype(int)
        indices[:, 0] = np.clip(indices[:, 0], 0, 359)
        indices[:, 1] = np.clip(indices[:, 1], 0, 179)
        cells[commodity] = np.unique(indices, axis=0) + np.array([-179.5, -89.5])
    with np.load(EARTH / "preview" / "grids.npz") as grid:
        flags = grid["flags"]
        step = max(1, flags.shape[1] // 1800)
        ocean = ((flags[::step, ::step] & 1) > 0).astype(np.uint8)

    plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 11,
                         "axes.titlesize": 14, "axes.titleweight": "bold"})
    fig, axes = plt.subplots(2, 1, figsize=(16, 11.8))
    fig.subplots_adjust(left=0.065, right=0.965, bottom=0.17, top=0.89, hspace=0.34)
    fig.suptitle("Global copper and tin: evidence for regional generation", x=0.065,
                 ha="left", y=0.972, fontsize=21, weight="bold")
    fig.text(0.065, 0.935, "Fixed-strength 1° evidence cells and assessed potential  |  Research snapshot: 29 September 2026",
             fontsize=12, color="#48535c")
    fmt_lon = FuncFormatter(lambda n, _: f"{abs(n):g}\N{DEGREE SIGN}" + ("W" if n < 0 else "E" if n > 0 else ""))
    fmt_lat = FuncFormatter(lambda n, _: f"{abs(n):g}\N{DEGREE SIGN}" + ("S" if n < 0 else "N" if n > 0 else ""))
    for ax in axes:
        ax.imshow(ocean, extent=(-180, 180, -90, 90), origin="upper",
                  cmap=ListedColormap(["#e9e9e3", "#f7fafc"]), interpolation="nearest", zorder=0)
        ax.set(xlim=(-180, 180), ylim=(-90, 90), aspect="equal")
        ax.set_xticks(np.arange(-180, 181, 60)); ax.set_yticks(np.arange(-60, 61, 30))
        ax.xaxis.set_major_formatter(fmt_lon); ax.yaxis.set_major_formatter(fmt_lat)
        ax.grid(color="#d7dfe4", linewidth=0.45, zorder=1)
        ax.tick_params(length=0, labelsize=10, pad=5)
        for spine in ax.spines.values(): spine.set_edgecolor("#c8d0d6")

    counts = {
        "porphyry_tracts": plot_tracts(axes[0], args.porphyry, "#5594bb", None),
        "sediment_hosted_tracts": plot_tracts(axes[0], args.sedimentary, "#476d68", "///")
    }
    axes[0].scatter(*cells["Cu"].T, s=5, marker="s", color="#b65c17", linewidths=0, zorder=3, rasterized=True)
    axes[0].set_title(f"COPPER  |  47,798 source records in {len(cells['Cu']):,} occupied cells", loc="left", pad=14)
    axes[0].legend(handles=[
        Line2D([], [], marker="s", linestyle="", color="#b65c17", markersize=5, label="Cell with recorded copper"),
        Patch(facecolor="#5594bb", alpha=.55, label="Porphyry potential"),
        Patch(facecolor="#476d68", alpha=.55, hatch="///", label="Sediment-hosted potential")
    ], loc="upper center", bbox_to_anchor=(0.5, -0.11), ncol=3, frameon=False, fontsize=10)
    axes[1].scatter(*cells["Sn"].T, s=5, marker="s", color="#334fa4", linewidths=0, zorder=3, rasterized=True)
    axes[1].set_title(f"TIN  |  2,749 records in {len(cells['Sn']):,} cells; global potential layer missing", loc="left", pad=14)
    axes[1].legend(handles=[Line2D([], [], marker="s", linestyle="", color="#334fa4", markersize=5,
                                 label="Cell with recorded tin")], loc="upper center",
                   bbox_to_anchor=(0.5, -0.11), frameon=False, fontsize=10)

    footer = [
        "One mark per occupied 1° cell. Survey coverage remains uneven; neither marks nor blank areas measure abundance.",
        "Potential regions can include deep mineralization. They do not guarantee surface ore. Historical cases are not plotted.",
        "229 of 240 source tracts shown; 11 invalid polygons withheld. Display geometry is simplified; processing plants excluded.",
        "Sources: USGS MRDS; USGS SIR 2010-5090-Z spatial database. Land outline: Earth's existing terrain-data preview.",
        "Geographic view in longitude/latitude. This is an evidence preview, not a calibrated ore-generation heatmap."
    ]
    for i, line in enumerate(footer):
        fig.text(0.065, 0.107 - i * 0.02, line, va="top", fontsize=11 if i == 0 else 10.5,
                 weight="bold" if i == 0 else "normal", color="#18232b" if i == 0 else "#59666f")
    output = HERE / "copper-tin-evidence-preview.png"
    fig.savefig(output, dpi=180, facecolor="white")
    plt.close(fig)
    inputs = [args.porphyry, args.sedimentary, DATA / "mrds-copper-tin-occurrences.csv",
              DATA / "global-copper-assessment-occurrences.csv"]
    audit = {"input_counts": input_counts, "source_point_counts": {k: len(v) for k, v in points.items()},
             "plotted_presence_cells": {k: len(v) for k, v in cells.items()}, "presence_cell_degrees": 1,
             "plotted_tracts": counts, "matplotlib_version": matplotlib.__version__,
             "numpy_version": np.__version__, "map_extent": [-180, -90, 180, 90],
             "semantics": "fixed-strength cell evidence; not unique deposits, country-normalized abundance or spawn probabilities",
             "inputs": [{"file": str(p), "sha256": hashlib.sha256(p.read_bytes()).hexdigest()} for p in inputs],
             "output": str(output)}
    (HERE / "preview-audit.json").write_text(json.dumps(audit, indent=2), encoding="utf-8")
    print(json.dumps(audit, indent=2))


if __name__ == "__main__":
    main()
