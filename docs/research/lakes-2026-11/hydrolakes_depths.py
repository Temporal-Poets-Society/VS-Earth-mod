"""EA-0184: how many lakes Earth draws much too deep or too shallow, from HydroLAKES' mean depths (Depth_avg; Vol_src 1
measured, else modelled). Earth's outline lakes come out 25-45 m deep on average whatever the lake (the probe: Geneva 30,
Constance 28, Pielinen 27, Peipus 40, Ladoga 60), so a lake's real mean depth against that band says which way it is off.

    python hydrolakes_depths.py [HydroLAKES_points_v10.shp]
"""
import sys
import numpy as np
import shapefile

path = sys.argv[1] if len(sys.argv) > 1 else r"C:\VS mods\data\earth-raw\hydrolakes\HydroLAKES_points_v10_shp\HydroLAKES_points_v10.shp"
r = shapefile.Reader(path, encoding="latin-1")
F = [f[0] for f in r.fields[1:]]
ia, idp, isrc = F.index("Lake_area"), F.index("Depth_avg"), F.index("Vol_src")
area, depth, src = [], [], []
for rec in r.iterRecords():
    area.append(rec[ia]); depth.append(rec[idp]); src.append(rec[isrc])
area, depth, src = np.array(area), np.array(depth), np.array(src)
print(f"{'lakes from':>12s} {'count':>8s} {'mean < 10 m':>12s} {'10-25 m':>9s} {'25-45 m':>9s} {'45-100 m':>9s} {'> 100 m':>8s}  "
      f"{'too deep in Earth (< 10 m)':>27s}  {'too shallow (> 100 m)':>22s}")
for lo in (1, 10, 100, 1000):
    m = area >= lo
    d = depth[m]
    bins = [np.sum(d < 10), np.sum((d >= 10) & (d < 25)), np.sum((d >= 25) & (d < 45)), np.sum((d >= 45) & (d < 100)), np.sum(d >= 100)]
    n = m.sum()
    print(f"{lo:>9} km2 {n:8d} " + " ".join(f"{b:9d}" for b in bins) +
          f"   {bins[0] / n * 100:24.0f} %  {bins[4] / n * 100:19.1f} %")
