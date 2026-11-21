"""EA-0160: river courses that reach the sea and then cross land again (worldwide, on a course list EMRV).

Per reach, points every ~90 m along its course; each on the GLO-90 mask's nearest cell (ocean = class 1 or no tile;
land = class 0) and GLO-90's height there. After the reach's first ocean point (0 for a reach that starts in the ocean),
the land points whose ground stands 5 m or more above the course's level there: where the course would cut a trench.
Reports reaches with at least MIN_M of that, their total length, and the biggest.
"""
import math, os, sys, json
from collections import OrderedDict
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "..", "tools"))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import snap_rivers_wbm as v1
import glo90

STEP_M, MIN_M, RISE_M = 90.0, 500.0, 5.0
R = v1.read_full(sys.argv[1])
print(len(R), "reaches", flush=True)
# densify every reach
lat_l, lon_l, lev_l, rid_l = [], [], [], []
for i, (q, nxt, a) in enumerate(R):
    la, lo, lv = a["lat"].astype(np.float64), a["lon"].astype(np.float64), a["lev"].astype(np.float64)
    if len(la) < 2:
        continue
    seg = np.hypot(np.diff(la) * 111320, np.diff(lo) * 111320 * np.cos(np.radians(la[:-1])))
    s = np.r_[0, np.cumsum(seg)]
    t = np.r_[np.arange(0, s[-1], STEP_M), s[-1]]
    lat_l.append(np.interp(t, s, la)); lon_l.append(np.interp(t, s, lo)); lev_l.append(np.interp(t, s, lv))
    rid_l.append(np.full(len(t), i, np.int32))
lat, lon, lev, rid = (np.concatenate(x) for x in (lat_l, lon_l, lev_l, rid_l))
del lat_l, lon_l, lev_l, rid_l
print(len(lat), "points", flush=True)
# mask class and height at each point's nearest grid point, tile by tile
g = np.rint((90 - lat) * 1200).astype(np.int64)
lat_s = 89 - g // 1200
row = g % 1200
cls = np.zeros(len(lat), np.int16)
dem = np.zeros(len(lat), np.float32)
order = np.lexsort((np.floor(lon).astype(np.int64), lat_s))
keys = lat_s[order] * 1000 + np.floor(lon[order]).astype(np.int64)
bounds = np.flatnonzero(np.r_[True, keys[1:] != keys[:-1], True])
done = 0
for b0, b1 in zip(bounds[:-1], bounds[1:]):
    idx = order[b0:b1]
    ls = int(lat_s[idx[0]])
    cols = glo90.cols_for(ls)
    gc = np.rint((lon[idx] + 180) * cols).astype(np.int64)
    lw = gc // cols - 180
    cc = gc % cols
    for w in np.unique(lw):
        m = lw == w
        ii = idx[m]
        wt = glo90.tile(ls, int((w + 180) % 360 - 180), "WBM")
        dt = glo90.tile(ls, int((w + 180) % 360 - 180), "DEM")
        cls[ii] = wt[row[ii], cc[m]]
        dem[ii] = dt[row[ii], cc[m]]
    done += 1
    if done % 2000 == 0:
        print(done, "tiles", flush=True)
glo90._cache.clear()
ocean = cls == 1
trench = (cls == 0) & (dem >= lev + RISE_M)
# per reach: after its first ocean point
starts = np.flatnonzero(np.r_[True, rid[1:] != rid[:-1]])
ends = np.r_[starts[1:], len(rid)]
res = []
for a, b in zip(starts, ends):
    oc = np.flatnonzero(ocean[a:b])
    if not len(oc):
        continue
    n = int(trench[a + oc[0]:b].sum())
    if n * STEP_M >= MIN_M:
        i = int(rid[a])
        res.append((n * STEP_M / 1000, i, float(R[i][0]), float(lat[a]), float(lon[a]), float(lat[b - 1]), float(lon[b - 1]),
                    float((dem[a:b] - lev[a:b])[trench[a:b]].max())))
res.sort(reverse=True)
tot = sum(r[0] for r in res)
print(json.dumps({"reaches": len(res), "km_of_trench": round(tot, 1), "reaches_with_ocean": int(sum(1 for a, b in zip(starts, ends) if ocean[a:b].any()))}))
for r in res[:25]:
    print(f"  {r[0]:6.1f} km  reach {r[1]}  Q {r[2]:8.1f}  from {r[3]:.3f},{r[4]:.3f} to {r[5]:.3f},{r[6]:.3f}  up to {r[7]:.0f} m above the course")
by = {}
for r in res:
    k = (round(r[3] / 5) * 5, round(r[4] / 5) * 5)
    by[k] = by.get(k, 0) + 1
print("by 5-degree box (most):", sorted(by.items(), key=lambda kv: -kv[1])[:15])
json.dump([list(r) for r in res], open(sys.argv[2], "w"))
