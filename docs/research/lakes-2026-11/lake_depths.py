"""EA-0183/EA-0184: lake depths and course channels in the probe's lake windows.

For each window (place_scale_variant.bin from the EA-0148 probe, sampled every EA0148_STRIDE blocks of the 1:10 world):
the main lake's columns (lake water, not river or sea, at the window's most common lake level), their depth below the
water in blocks (water top - bed top, as generated) and in metres (the lake's level - the column's Elev, the lake's own
bed), and the columns where a course's channel is cut deeper than the lake's own bed (a course draws there and the bed
lies more than a block below the lake's own).

    python lake_depths.py <window dir> [place,...]
"""
import os, sys, glob
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "ea-0148-offset-2026-11", "probe"))
from analyse import load

REAL = {  # HydroLAKES Depth_avg (m; Vol_src 1: measured) and the literature's maximum depth (m)
    "geneva": (155.7, 310), "constance": (93.9, 251), "baikal": (738.7, 1642), "ladoga": (48.0, 230),
    "pielinen": (9.9, 61), "peipus": (7.1, 15),
}


def stats(path):
    h, d = load(path)
    f = d["flags"].astype(np.int64)
    k = (h["y100"] - h["y0"]) / 100.0
    lake = ((f & (4 | 8)) != 0) & ((f & 2) == 0) & ((f & 16) == 0) & ((f & 1) != 0)
    # the lake level of the outline layer where it has one (flag 4096 sets it), else none
    lvl = d["level"]
    ok = lake & np.isfinite(lvl)
    if not ok.any():
        return h, None
    vals, counts = np.unique(np.round(lvl[ok], 1), return_counts=True)
    main = float(vals[np.argmax(counts)])
    m = ok & (np.abs(lvl - main) <= 1.0)
    depth_b = (d["wtop"].astype(np.int64) - d["top"].astype(np.int64))[m]
    depth_m = (lvl - d["elev"])[m]
    own_b = np.round(depth_m * k)
    course = ((f & 64) != 0)[m]
    cut = course & (depth_b > own_b + 1)
    return h, dict(level=main, n=int(m.sum()), k=k, mean_b=float(depth_b.mean()), p50_b=float(np.median(depth_b)),
                   p90_b=float(np.percentile(depth_b, 90)), max_b=int(depth_b.max()), mean_m=float(depth_m.mean()),
                   max_m=float(depth_m.max()), course=int(course.sum()), cut=int(cut.sum()),
                   cut_extra=float((depth_b - own_b)[cut].mean()) if cut.any() else 0.0)


def main():
    folder = sys.argv[1]
    want = sys.argv[2].split(",") if len(sys.argv) > 2 else None
    print(f"{'lake':10s} {'scale':>5s} {'variant':10s} {'level m':>8s} {'columns':>8s} {'bl/m':>6s} {'depth blocks mean/p50/p90/max':>31s} "
          f"{'bed m mean/max':>15s} {'real m mean/max':>16s} {'course cols':>11s} {'cut cols':>8s} {'cut +bl':>7s}")
    for path in sorted(glob.glob(os.path.join(folder, "*.bin"))):
        name = os.path.basename(path)[:-4]
        place, scale, variant = name.split("_", 2)
        if want and place not in want:
            continue
        h, s = stats(path)
        if s is None:
            print(f"{place:10s} {scale:>5s} {variant:10s} no lake")
            continue
        real = REAL.get(place, (float("nan"), float("nan")))
        print(f"{place:10s} {scale:>5s} {variant:10s} {s['level']:8.1f} {s['n']:8d} {s['k']:6.3f} "
              f"{s['mean_b']:7.1f}/{s['p50_b']:5.1f}/{s['p90_b']:5.1f}/{s['max_b']:5d}       "
              f"{s['mean_m']:6.0f}/{s['max_m']:6.0f}   {real[0]:6.0f}/{real[1]:6.0f}   {s['course']:9d} {s['cut']:8d} {s['cut_extra']:7.1f}")


if __name__ == "__main__":
    main()
