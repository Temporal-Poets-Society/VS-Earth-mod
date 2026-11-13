"""Read the raw GSHHG full-resolution L1 shapefile and measure, inside a bounding box, how many edges are
axis-aligned or 45 degrees, and how long the straight edges are. Prints a sample run of vertices."""
import math, sys
import numpy as np
import shapefile

shp, lon0, lat0, lon1, lat1 = sys.argv[1], *map(float, sys.argv[2:6])
r = shapefile.Reader(shp)
lens, kinds, sample = [], {"axis": 0, "diag45": 0, "other": 0}, None
for s in r.iterShapes():
    bx = s.bbox
    if bx[2] < lon0 or bx[0] > lon1 or bx[3] < lat0 or bx[1] > lat1:
        continue
    p = np.asarray(s.points)
    inside = (p[:, 0] >= lon0) & (p[:, 0] <= lon1) & (p[:, 1] >= lat0) & (p[:, 1] <= lat1)
    idx = np.nonzero(inside[:-1] & inside[1:])[0]
    if len(idx) == 0:
        continue
    if sample is None:
        sample = p[idx[0]:idx[0] + 12]
    d = p[idx + 1] - p[idx]
    for dx, dy in d:
        if dx == 0 and dy == 0:
            continue
        # metres, with longitude shrunk by cos(latitude) at the box centre
        mx = dx * 111320 * math.cos(math.radians((lat0 + lat1) / 2)); my = dy * 110574
        lens.append(math.hypot(mx, my))
        if dx == 0 or dy == 0:
            kinds["axis"] += 1
        elif abs(abs(dx) - abs(dy)) < 1e-9:
            kinds["diag45"] += 1   # 45 degrees in lon/lat degrees, i.e. a raster cell's diagonal
        else:
            kinds["other"] += 1
n = sum(kinds.values())
lens = np.array(lens)
print(f"edges {n}: axis-aligned {kinds['axis']/n:.1%}, exact 45-degree (in degrees) {kinds['diag45']/n:.1%}, other {kinds['other']/n:.1%}")
print(f"edge length m: median {np.median(lens):.0f}, p90 {np.percentile(lens, 90):.0f}, max {lens.max():.0f}")
print("sample vertices (lon, lat):")
for x, y in sample:
    print(f"  {x:.6f} {y:.6f}")
