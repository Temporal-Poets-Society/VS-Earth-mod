"""Compare OSM natural=coastline (Overpass JSON) with raw GSHHG full L1 inside a bounding box.
Prints statistics and writes an overview PNG and a zoom PNG with both lines.
usage: coast_compare.py <name> <osm.json> <GSHHS_f_L1 path> <S> <W> <N> <E> <outdir> [<zoom lat> <zoom lon> <zoom tag>]
Without a zoom centre the zoom goes to the 1.5 km square with the most OSM shore more than 50 m from GSHHG."""
import json, math, os, sys
import numpy as np
import shapefile
from scipy.spatial import cKDTree
from PIL import Image, ImageDraw

name, osm_path, shp = sys.argv[1:4]
S, W, N, E = map(float, sys.argv[4:8])
outdir = sys.argv[8]
lat0 = (S + N) / 2
KX, KY = 111320 * math.cos(math.radians(lat0)), 110574   # metres per degree


def to_m(p):
    return np.column_stack(((p[:, 0] - W) * KX, (p[:, 1] - S) * KY))


def clip_runs(p):
    """Split a polyline (lon, lat) into runs of consecutive vertices inside the box."""
    inside = (p[:, 0] >= W) & (p[:, 0] <= E) & (p[:, 1] >= S) & (p[:, 1] <= N)
    runs, cur = [], []
    for ok, v in zip(inside, p):
        if ok:
            cur.append(v)
        elif cur:
            runs.append(np.array(cur)); cur = []
    if cur:
        runs.append(np.array(cur))
    return [r for r in runs if len(r) >= 2]


def load_osm():
    els = json.load(open(osm_path, encoding="utf-8"))["elements"]
    lines = []
    for e in els:
        if e.get("type") == "way" and "geometry" in e:
            lines += clip_runs(np.array([[g["lon"], g["lat"]] for g in e["geometry"]]))
    return lines


def load_gshhg():
    r = shapefile.Reader(shp)
    lines = []
    for s in r.iterShapes():
        b = s.bbox
        if b[2] < W or b[0] > E or b[3] < S or b[1] > N:
            continue
        p = np.asarray(s.points)
        parts = list(s.parts) + [len(p)]
        for a, z in zip(parts[:-1], parts[1:]):
            lines += clip_runs(p[a:z])
    return lines


def stats(lines, deg_lines):
    segs = np.concatenate([np.diff(l, axis=0) for l in lines])
    dsegs = np.concatenate([np.diff(l, axis=0) for l in deg_lines])
    lens = np.hypot(segs[:, 0], segs[:, 1])
    keep = lens > 0
    axis = ((dsegs[keep, 0] == 0) | (dsegs[keep, 1] == 0)).mean()
    verts = sum(len(l) for l in lines)
    total = lens.sum() / 1000
    return dict(vertices=verts, km=total, per_km=verts / total, median_edge=np.median(lens[keep]),
                p90_edge=np.percentile(lens[keep], 90), axis=axis)


def densify(lines, step=5.0):
    pts = []
    for l in lines:
        for a, b in zip(l[:-1], l[1:]):
            n = max(1, int(math.ceil(np.hypot(*(b - a)) / step)))
            t = np.arange(n)[:, None] / n
            pts.append(a + (b - a) * t)
        pts.append(l[-1:])
    return np.concatenate(pts)


osm_deg, gsh_deg = load_osm(), load_gshhg()
osm, gsh = [to_m(l) for l in osm_deg], [to_m(l) for l in gsh_deg]
so, sg = stats(osm, osm_deg), stats(gsh, gsh_deg)
po, pg = densify(osm), densify(gsh)
d_o = cKDTree(pg).query(po)[0]   # every 5 m of OSM shore: distance to GSHHG's line
d_g = cKDTree(po).query(pg)[0]   # every 5 m of GSHHG shore: distance to OSM's line

print(f"== {name}  box {S},{W} .. {N},{E}  ({(E-W)*KX/1000:.1f} x {(N-S)*KY/1000:.1f} km)")
print(f"{'':10}{'vertices':>10}{'shore km':>10}{'pts/km':>8}{'median edge m':>15}{'p90 edge m':>12}{'N-S/E-W edges':>15}")
for k, s in (("OSM", so), ("GSHHG", sg)):
    print(f"{k:10}{s['vertices']:>10}{s['km']:>10.1f}{s['per_km']:>8.1f}{s['median_edge']:>15.1f}{s['p90_edge']:>12.0f}{s['axis']:>15.1%}")
print(f"OSM shore -> nearest GSHHG line, m: median {np.median(d_o):.0f}, p90 {np.percentile(d_o, 90):.0f}, p99 {np.percentile(d_o, 99):.0f}")
print(f"  share of OSM shore length farther than 25 m: {(d_o > 25).mean():.0%}, 50 m: {(d_o > 50).mean():.0%}, 100 m: {(d_o > 100).mean():.0%}, 250 m: {(d_o > 250).mean():.0%}")
print(f"GSHHG shore -> nearest OSM line, m: median {np.median(d_g):.0f}, p90 {np.percentile(d_g, 90):.0f}, p99 {np.percentile(d_g, 99):.0f}")

# zoom window: the 1.5 km square holding the most OSM shore that is more than 50 m from GSHHG
cell = 1500.0
far = po[d_o > 50]
ij = np.floor(far / cell).astype(int)
if len(ij):
    u, c = np.unique(ij, axis=0, return_counts=True)
    zi = u[np.argmax(c)]
else:
    zi = np.array([0, 0])
zx0, zy0 = zi * cell
if len(sys.argv) > 10:   # optional zoom centre: lat lon
    zlat, zlon = float(sys.argv[9]), float(sys.argv[10])
    zx0, zy0 = (zlon - W) * KX - cell / 2, (zlat - S) * KY - cell / 2
    zoomtag = sys.argv[11] if len(sys.argv) > 11 else "zoom-b"
else:
    zoomtag = "zoom"


def render(path, x0, y0, wm, hm, mpp, grid=None, title=""):
    w, h = int(wm / mpp), int(hm / mpp)
    img = Image.new("RGB", (w, h + 34), "white")
    dr = ImageDraw.Draw(img)

    def px(p):
        return [((x - x0) / mpp, h - (y - y0) / mpp) for x, y in p]
    if grid:
        for gx in np.arange(math.ceil(x0 / grid) * grid, x0 + wm, grid):
            dr.line([((gx - x0) / mpp, 0), ((gx - x0) / mpp, h)], fill=(232, 232, 232))
        for gy in np.arange(math.ceil(y0 / grid) * grid, y0 + hm, grid):
            dr.line([(0, h - (gy - y0) / mpp), (w, h - (gy - y0) / mpp)], fill=(232, 232, 232))
    for l in gsh:
        dr.line(px(l), fill=(230, 120, 0), width=3)
    for l in osm:
        dr.line(px(l), fill=(20, 80, 200), width=1)
    bar = 1000 if wm > 5000 else 100
    dr.rectangle([10, h + 8, 10 + bar / mpp, h + 12], fill="black")
    dr.text((14 + bar / mpp, h + 4), f"{bar} m", fill="black")
    dr.text((140 + bar / mpp, h + 4), f"{title}   orange: GSHHG full (raw)   blue: OSM coastline (© OpenStreetMap contributors, ODbL)" +("   grid: 10 m blocks" if grid else ""), fill="black")
    img.save(path)


W_m, H_m = (E - W) * KX, (N - S) * KY
render(os.path.join(outdir, f"{name}-overview.png"), 0, 0, W_m, H_m, max(W_m, H_m) / 1600, title=name)
render(os.path.join(outdir, f"{name}-{zoomtag}.png"), zx0, zy0, cell, cell, 1.25, grid=10,
       title=f"{name} zoom at {S + (zy0 + cell/2)/KY:.4f}N {W + (zx0 + cell/2)/KX:.4f}E")
print(f"zoom centre {S + (zy0 + cell/2)/KY:.4f}N {W + (zx0 + cell/2)/KX:.4f}E")
