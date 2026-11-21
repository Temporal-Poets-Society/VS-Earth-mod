"""EA-0148 question 4: the world's water against the mask along whole-degree tile edges that cross a window, next to the
same measure away from them.

For each whole meridian (or parallel) in a window: the share of samples within half a mask cell of the line where the
world's water and the mask's (nearest grid point) disagree, and the share over the whole window; and how many rows (or
columns) change water between the two sample columns (rows) on either side of the line, against the same count for
every other pair of neighbouring columns (rows), as EA-0147 measured the 27° E seam.

Usage: python -I seams.py <probe out dir> place[,place...]
"""
import math, os, sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import glo90  # noqa: E402
from analyse import load  # noqa: E402


def main():
    src, places = sys.argv[1], sys.argv[2].split(",")
    for place in places:
        for scale in (10,):
            for variant in ("base", "shift"):
                path = os.path.join(src, f"{place}_{scale}_{variant}.bin")
                if not os.path.exists(path):
                    continue
                h, d = load(path)
                lat, lon = d["lat"], d["lon"]
                M = glo90.nearest(lat[:, None], lon[None, :], "WBM") > 0
                W = (d["flags"] & 1) > 0
                bad = M ^ W
                lat_s = math.ceil(float(lat.mean())) - 1
                cols = glo90.cols_for(lat_s)
                half_x = 0.5 / cols
                half_z = 0.5 / 1200
                out = [f"{place:14s} {variant:6s} window disagree {bad.mean():.3f}"]
                for m in range(math.ceil(lon.min()), math.floor(lon.max()) + 1):
                    near = np.abs(lon - m) <= half_x
                    i = int(np.searchsorted(lon, m))   # first column east of the line
                    steps = (W[:, 1:] != W[:, :-1]).sum(0)
                    out.append(f"meridian {m}: strip disagree {bad[:, near].mean():.3f}; rows changing across it {steps[i - 1]}"
                               f" (median other column pair {int(np.median(steps))})")
                for p in range(math.ceil(lat.min()), math.floor(lat.max()) + 1):
                    near = np.abs(lat - p) <= half_z
                    j = int(np.argmax(lat < p))         # first row south of the line (rows run north to south)
                    steps = (W[1:, :] != W[:-1, :]).sum(1)
                    out.append(f"parallel {p}: strip disagree {bad[near, :].mean():.3f}; columns changing across it {steps[j - 1]}"
                               f" (median other row pair {int(np.median(steps))})")
                print("  ".join(out))


if __name__ == "__main__":
    main()
