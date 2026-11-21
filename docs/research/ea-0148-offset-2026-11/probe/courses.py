"""EA-0148: where the world's river courses lie across the water mask's rivers (the probe's "courses" mode dumps the 1:10
world's course vertices in chosen 1° squares).

Per vertex: the mask's water run through its nearest grid point along the column (north-south) and along the row
(east-west), on the pixel-is-point grid; where a run is a cross-section (3 to 40 cells), the vertex's offset from the
run's middle in cells (+ south, + east). Vertices are grouped by where they sit on the grids:
- hydrorivers: on a 15" cell's centre in both directions (HydroRIVERS' own vertices, (k + 0.5)/240°), not moved;
- snapped: half way between two 3" grid points in both directions but not a 15" centre (the snap tools put moved
  vertices at (pixel + 0.5)/1200°);
- other: anywhere else (smoothed or straightened, or rerouted).
A course in the heights' frame lies on the water's middle on average (offset 0); one half a cell south-east, +0.5.

Usage: python -I courses.py <courses-squares.csv> [out.json]
"""
import csv, json, math, sys, os
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import glo90  # noqa: E402


def cross(line, pos):
    """The run of water in a 1-D mask nearest pos (grid units) within a cell either way, as (first, last), if it is a
    cross-section: 3 to 40 cells, not reaching the array's ends."""
    lo, hi = int(math.floor(pos - 1)), int(math.ceil(pos + 1))
    idx = [i for i in range(max(lo, 0), min(hi, len(line) - 1) + 1) if line[i] and abs(i - pos) <= 1]
    if not idx:
        return None
    i = min(idx, key=lambda k: abs(k - pos))
    a = b = i
    while a > 0 and line[a - 1]:
        a -= 1
    while b < len(line) - 1 and line[b + 1]:
        b += 1
    if a == 0 or b == len(line) - 1 or not 3 <= b - a + 1 <= 40:
        return None
    return a, b


def near(x, k):
    return abs(x - k) < 0.02


def main():
    rows = list(csv.DictReader(open(sys.argv[1])))
    groups = {}
    for row in rows:
        v, last = int(row["vertex"]), int(row["last"])
        lat, lon = float(row["lat"]), float(row["lon"])
        end = v == 0 or v == last
        p15r, p15c = ((90 - lat) * 240) % 1, ((lon + 180) * 240) % 1
        p3r, p3c = ((90 - lat) * 1200) % 1, ((lon + 180) * 1200) % 1
        if "kind" in row:
            g = row["kind"] + ("" if row.get("prepared") == "1" else "-reprepared")
        elif near(p15r, 0.5) and near(p15c, 0.5):
            g = "hydrorivers"
        elif near(p3r, 0.5) and near(p3c, 0.5):
            g = "snapped"
        else:
            g = "other"
        if end:
            g += "-end"
        gr = (90 - lat) * 1200
        rr = int(math.floor(gr + 0.5))
        lat_s = 89 - rr // 1200
        cols = glo90.cols_for(lat_s)
        gc = (lon + 180) * cols
        cc = int(math.floor(gc + 0.5))
        lon_w = cc // cols - 180
        t = glo90.tile(lat_s, lon_w, "WBM")
        fr = gr - (89 - lat_s) * 1200
        fc = gc - (lon_w + 180) * cols
        r, c = rr % 1200, cc % cols
        rec = groups.setdefault(g, {"n": 0, "dry": 0, "ns": [], "ew": []})
        rec["n"] += 1
        if t[r, c] == 0:
            rec["dry"] += 1
        # the water run nearest the vertex within a cell either way (the same on both sides: a vertex half a cell
        # beyond a run's south end counts as one half a cell beyond its north end does)
        for key, line, pos in (("ns", t[:, c] > 0, fr), ("ew", t[r, :] > 0, fc)):
            run = cross(line, pos)
            if run is not None:
                a, b = run
                rec[key].append((pos - (a + b) / 2, b - a + 1))
    out = {}
    for g, rec in sorted(groups.items()):
        nsl = np.array(rec["ns"]).reshape(-1, 2)
        ewl = np.array(rec["ew"]).reshape(-1, 2)
        ns, ew = nsl[:, 0], ewl[:, 0]
        bands = {}
        for lo_, hi_ in ((3, 5), (6, 12), (13, 40)):
            a_ = nsl[(nsl[:, 1] >= lo_) & (nsl[:, 1] <= hi_), 0]
            b_ = ewl[(ewl[:, 1] >= lo_) & (ewl[:, 1] <= hi_), 0]
            bands[f"{lo_}-{hi_}"] = {"n_ns": len(a_), "south": float(a_.mean()) if len(a_) else None,
                                    "n_ew": len(b_), "east": float(b_.mean()) if len(b_) else None}
        out[g] = {"by_width_cells": bands, "vertices": rec["n"], "on_mask_land": rec["dry"], "n_ns": len(ns), "n_ew": len(ew),
                  "mean_south_cells": float(ns.mean()) if len(ns) else None, "median_south_cells": float(np.median(ns)) if len(ns) else None,
                  "mean_east_cells": float(ew.mean()) if len(ew) else None, "median_east_cells": float(np.median(ew)) if len(ew) else None,
                  "sem_south": float(ns.std() / np.sqrt(len(ns))) if len(ns) > 1 else None,
                  "sem_east": float(ew.std() / np.sqrt(len(ew))) if len(ew) > 1 else None}
        print(g, json.dumps(out[g]))
    if len(sys.argv) > 2:
        json.dump(out, open(sys.argv[2], "w"), indent=1)


if __name__ == "__main__":
    main()
