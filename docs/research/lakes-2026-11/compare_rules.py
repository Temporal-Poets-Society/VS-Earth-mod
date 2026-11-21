"""EA-0183: lake rules against a control, column by column, in the probe's lake windows.

For each place and scale, the windows <place>_<scale>_<a>.bin and <place>_<scale>_<b>.bin (default base and base-lake0):
lake columns whose bed differs (the control's deeper: a course's channel cut into the bed), and how much; land columns
that differ (a course's bank or valley on the shore); and the lake's depth in blocks under each.

    python compare_rules.py <window dir> [a] [b]
"""
import os, sys, glob
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "ea-0148-offset-2026-11", "probe"))
from analyse import load


def main():
    folder = sys.argv[1]
    va = sys.argv[2] if len(sys.argv) > 2 else "base"
    vb = sys.argv[3] if len(sys.argv) > 3 else "base-lake0"
    print(f"{'lake':10s} {'scale':>5s} {'lake cols':>9s} {'beds differ':>11s} {'deeper in ' + vb:>18s} {'mean +bl':>8s} {'max +bl':>7s} "
          f"{'land differs':>12s} {'depth ' + va:>14s} {'depth ' + vb:>18s}")
    for pa in sorted(glob.glob(os.path.join(folder, f"*_{va}.bin"))):
        stem = os.path.basename(pa)[: -len(f"_{va}.bin")]
        pb = os.path.join(folder, f"{stem}_{vb}.bin")
        if not os.path.exists(pb):
            continue
        place, scale = stem.rsplit("_", 1)
        _, A = load(pa)
        _, B = load(pb)
        fa, fb = A["flags"].astype(np.int64), B["flags"].astype(np.int64)
        lake = ((fb & (4 | 8)) != 0) & ((fb & 2) == 0) & ((fb & 16) == 0) & ((fb & 1) != 0)
        land_a, land_b = (fa & 1) == 0, (fb & 1) == 0
        ta, tb = A["top"].astype(np.int64), B["top"].astype(np.int64)
        da, db = A["wtop"].astype(np.int64) - ta, B["wtop"].astype(np.int64) - tb
        differ = lake & (ta != tb)
        deeper = lake & (tb < ta)
        land = (land_a | land_b) & (ta != tb)
        extra = (ta - tb)[deeper]
        print(f"{place:10s} {scale:>5s} {int(lake.sum()):9d} {int(differ.sum()):11d} {int(deeper.sum()):18d} "
              f"{(extra.mean() if extra.size else 0):8.1f} {(extra.max() if extra.size else 0):7d} {int(land.sum()):12d} "
              f"{da[lake].mean():14.2f} {db[lake].mean():18.2f}")


if __name__ == "__main__":
    main()
