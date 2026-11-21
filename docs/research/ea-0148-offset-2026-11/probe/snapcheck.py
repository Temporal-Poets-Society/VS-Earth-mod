"""EA-0148: where a snap's moved courses lie across the mask's water (pixel-is-point), as courses.py measures the world's.

Reads an EMRV file from tools/snap_rivers_channel.py (or river_arms.py) and its source; for the reaches whose vertices
moved, the inner vertices' offset from the middle of the mask water run through them (cells, + south, + east).

Usage: python -I snapcheck.py <source rivers-glo90.bin> <snapped .bin> [lat0,lon0,lat1,lon1]
"""
import math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "..", "tools"))
import glo90  # noqa: E402
from courses import cross  # noqa: E402
import snap_rivers_wbm as v1  # noqa: E402


def main():
    src, out = v1.read_full(sys.argv[1]), v1.read_full(sys.argv[2])
    box = [float(v) for v in sys.argv[3].split(",")] if len(sys.argv) > 3 else None
    ns, ew, moved = [], [], 0
    for i, (q, nxt, a) in enumerate(out):
        b = src[i][2] if i < len(src) else None
        if b is not None and len(a) == len(b) and np.array_equal(a["lat"], b["lat"]) and np.array_equal(a["lon"], b["lon"]):
            continue
        if box and not (box[0] <= float(a["lat"].mean()) <= box[2] and box[1] <= float(a["lon"].mean()) <= box[3]):
            continue
        moved += 1
        for k in range(1, len(a) - 1):
            lat, lon = float(a["lat"][k]), float(a["lon"][k])
            gr = (90 - lat) * 1200
            rr = int(math.floor(gr + 0.5))
            lat_s = 89 - rr // 1200
            cols = glo90.cols_for(lat_s)
            gc = (lon + 180) * cols
            cc = int(math.floor(gc + 0.5))
            lon_w = cc // cols - 180
            t = glo90.tile(lat_s, lon_w, "WBM")
            fr, fc = gr - (89 - lat_s) * 1200, gc - (lon_w + 180) * cols
            r, c = rr % 1200, cc % cols
            for lst, line, pos in ((ns, t[:, c] > 0, fr), (ew, t[r, :] > 0, fc)):
                run = cross(line, pos)
                if run:
                    lst.append(pos - (run[0] + run[1]) / 2)
    ns, ew = np.array(ns), np.array(ew)
    print(f"{moved} moved reaches; inner vertices across the water: south {ns.mean():+.3f} (median {np.median(ns):+.3f}, n {len(ns)}),"
          f" east {ew.mean():+.3f} (median {np.median(ew):+.3f}, n {len(ew)}) cells")


if __name__ == "__main__":
    main()
