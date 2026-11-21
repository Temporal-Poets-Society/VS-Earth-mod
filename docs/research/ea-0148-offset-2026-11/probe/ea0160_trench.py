"""EA-0160: where the world's ground is cut far below GLO-90's surface outside the sea, and which course runs there."""
import csv, os, sys, json, math
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import glo90
from analyse import load
from PIL import Image

path, courses_csv, out_png = sys.argv[1], sys.argv[2], sys.argv[3]
h, d = load(path)
lat, lon = d["lat"], d["lon"]
k = (h["y100"] - h["y0"]) / 100.0
fine = d["fine"].astype(np.float64)
want = h["y0"] + fine * k            # (linear above sea level: the play styles at 1:10 and 1:25)
top = d["top"].astype(np.float64)
cut = want - top
f = d["flags"]
sea = (f & 16) > 0
wet = (f & 1) > 0
river = (f & 2) > 0
course = (f & 64) > 0
M = glo90.nearest(lat[:, None], lon[None, :], "WBM")
trench = (cut >= 8) & ~sea & (M == 0) & (fine > 2)
print(f"{h['place']} 1:{h['scale']}: blocks per metre {k:.3f}; columns cut 8+ blocks below GLO-90 on the mask's land: {int(trench.sum())}"
      f" (with river water {int((trench & river).sum())}, where a course draws {int((trench & course).sum())})")
if trench.any():
    ii, jj = np.nonzero(trench)
    print(f"  extent lat {lat[ii].min():.4f}..{lat[ii].max():.4f}, lon {lon[jj].min():.4f}..{lon[jj].max():.4f}; cut median {np.median(cut[trench]):.0f}, max {cut[trench].max():.0f} blocks")
# the courses through those columns
rows = list(csv.DictReader(open(courses_csv)))
by = {}
for r in rows:
    by.setdefault(int(r["reach"]), []).append(r)
dlat = abs(lat[1] - lat[0]); dlon = abs(lon[1] - lon[0])
hits = {}
for reach, vs in by.items():
    vs.sort(key=lambda r: int(r["vertex"]))
    la = np.array([float(r["lat"]) for r in vs]); lo = np.array([float(r["lon"]) for r in vs])
    # dense points along the course
    n = 0; land = 0; mask_sea = 0
    for a in range(len(la) - 1):
        m = max(2, int(max(abs(la[a + 1] - la[a]) / dlat, abs(lo[a + 1] - lo[a]) / dlon)) + 1)
        for t in np.linspace(0, 1, m):
            y, x = la[a] + t * (la[a + 1] - la[a]), lo[a] + t * (lo[a + 1] - lo[a])
            i = int(round((lat[0] - y) / dlat)); j = int(round((x - lon[0]) / dlon))
            if 0 <= i < len(lat) and 0 <= j < len(lon):
                n += 1
                if trench[i, j]:
                    land += 1
    if land:
        hits[reach] = (land, n, float(vs[0]["q"]), len(vs), (la[0], lo[0]), (la[-1], lo[-1]))
for reach, (land, n, q, nv, a, b) in sorted(hits.items(), key=lambda kv: -kv[1][0]):
    print(f"  reach {reach}: Q {q:.1f} m3/s, {nv} vertices, from {a[0]:.4f},{a[1]:.4f} to {b[0]:.4f},{b[1]:.4f}; {land} of its {n} window points in the trench")
# picture: trench red, sea blue, mask water light blue, courses black
rgb = np.zeros(trench.shape + (3,), np.uint8)
rgb[:] = (230, 225, 210)
rgb[M > 0] = (150, 190, 230)
rgb[sea] = (60, 110, 180)
rgb[trench] = (210, 40, 40)
rgb[course & ~trench] = (20, 20, 20)
step = 4
Image.fromarray(rgb[::step, ::step]).save(out_png)
print("picture", out_png)
