"""EA-0184: in each window, the lake's lowest bed block against the deepest sea floor's block (lake rules 1's clamp) and
the world's bottom (y 0, the mantle); and how much of the lake vanilla's underwater plants and the fish can live in.

Plants (vanilla's underwater patches, unchanged): water 2 to 15 blocks deep (vanilla places a patch only where the block
two below the surface is water; hornwort and muskgrass grow to 15 blocks, the milfoils and crowfoot to 8). Fish (decision
0096): freshwater fish spawn on the bed within their band below the surface, the top 21 blocks (and in light 3 or more,
not measured here).

    python floor_and_life.py <window dir>
"""
import os, sys, glob
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "ea-0148-offset-2026-11", "probe"))
from analyse import load

print(f"{'lake':11s} {'scale':>5s} {'height':>6s} {'sea floor y':>11s} {'lowest bed y':>12s} {'above floor':>11s} "
      f"{'depth mean/max':>14s} {'plants 2-15':>11s} {'crowfoot 2-8':>12s} {'fish <= 21':>10s} {'bare > 15':>9s}")
for path in sorted(glob.glob(os.path.join(sys.argv[1], "*_base.bin"))):
    place, scale, _ = os.path.basename(path)[:-4].split("_")
    h, d = load(path)
    f = d["flags"].astype(np.int64)
    lake = ((f & (4 | 8)) != 0) & ((f & 2) == 0) & ((f & 16) == 0) & ((f & 1) != 0)
    lvl = d["level"]
    ok = lake & np.isfinite(lvl)
    vals, counts = np.unique(np.round(lvl[ok], 1), return_counts=True)
    main = float(vals[np.argmax(counts)])
    m = ok & (np.abs(lvl - main) <= 1.0)
    depth = (d["wtop"].astype(np.int64) - d["top"].astype(np.int64))[m]
    tops = d["top"][m].astype(np.int64)
    floor = h.get("yTrench", float("nan"))
    n = len(depth)
    print(f"{place:11s} {scale:>5s} {h.get('mapHeight', 0):6d} {floor:11.1f} {int(tops.min()):12d} {int(tops.min()) - floor:11.1f} "
          f"{depth.mean():7.1f}/{depth.max():5d}  {np.mean((depth >= 2) & (depth <= 15)) * 100:9.0f} % {np.mean((depth >= 2) & (depth <= 8)) * 100:10.0f} % "
          f"{np.mean(depth <= 21) * 100:8.0f} % {np.mean(depth > 15) * 100:7.0f} %")
