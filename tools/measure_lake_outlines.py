"""Measuring pass for lake outlines from the GLO-90 water body mask (class 2 = lake), worldwide north of 60°S.

Per 1° tile: label the mask's lake pixels (8-connected), give each lake its tags as the water atlas does (Earth already has
it as sea or lake, reservoir, natural lake raised by a dam, salt flat in a dry climate, polar, glacier), its level (median
GLO-90 height of its pixels) and its outline (marching squares at the pixel edges, simplified), then encode the tile's
outlines like coasts.bin (zigzag varint deltas, brotli per tile) and measure the bytes for each selection and size cut.

Pass 1 joins the pieces of each lake across tile edges and tags whole lakes (merge.npz, lakes-joined.txt); pass 2
outlines and measures with those tags (summary.txt, sizes.json, big.json, window.pkl for the Patagonia window).
Measured on 7 November 2026: docs/research/lake-outlines-2026-11-07.md.

Usage: python -I tools/measure_lake_outlines.py OUTDIR [workers] [tile-name-filter]
Needs rawdata/copernicus-glo90 and copernicus-glo90-wbm, rawdata/hydrolakes, earthdata/climate.bin and the water
atlas caches (tools/water_atlas.py: sea.npy, lakes1m.npy, reservoirs1m.npy, gridlakes2m-terminal.npy, glaciers.npy);
EARTH_RAW and ATLAS_CACHE point elsewhere (a worktree without rawdata).
"""
import glob, json, math, os, pickle, struct, sys, time
from multiprocessing import Pool

import numpy as np
import tifffile
import brotli
import contourpy
import shapely
import shapely.affinity
from scipy import ndimage as ndi

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RAW = os.environ.get("EARTH_RAW", os.path.join(ROOT, "rawdata"))
DEM = os.path.join(RAW, "copernicus-glo90")
WBM = os.path.join(RAW, "copernicus-glo90-wbm")
CACHE = os.environ.get("ATLAS_CACHE", os.path.join(ROOT, "preview", "water-atlas", "cache"))
TOOLS = os.path.dirname(os.path.abspath(__file__))
SOUTH = -60
UNIT = 2400                         # vertex units per degree (half a 3" pixel north-south)
TOLS = (0.5, 1.0)                   # simplification tolerance, pixels
CUTS = (0.0, 0.05, 0.1, 0.5, 1.0, 10.0)
SELS = ("real-all", "real-missing")
BINS = (0.0, 0.05, 0.1, 0.5, 1.0, 10.0, 100.0, 1e12)
CLASSES = ("real", "raised", "sea", "polar", "reservoir", "saltflat", "ice")
WINDOW = (-53.0, -75.0, -50.0, -71.0)   # tiles whose outlines are kept (Patagonia window)
T0 = time.time()
G = {}


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


def polar(lat, lon):
    return (lat > 75) | ((lat > 59) & (lon > -75) & (lon < -10))


def init_worker(scratch, pass1=False, ov=None):
    G["pass1"] = pass1
    if ov:
        z = np.load(ov)
        G["ov"] = {"cls": z["cls"], "missing": z["missing"], "km2": z["km2"]}
        G["ov_off"] = dict(zip(z["names"].tolist(), z["offs"].tolist()))
    G["sea"] = np.load(os.path.join(CACHE, "sea.npy"), mmap_mode="r")
    G["lakes"] = np.load(os.path.join(CACHE, "lakes1m.npy"), mmap_mode="r")
    G["grid"] = np.load(os.path.join(CACHE, "gridlakes2m-terminal.npy"), mmap_mode="r")
    G["res"] = np.load(os.path.join(CACHE, "reservoirs1m.npy"), mmap_mode="r")
    G["glac"] = np.load(os.path.join(CACHE, "glaciers.npy"), mmap_mode="r")
    G["p"] = np.load(os.path.join(scratch, "p_year.npy"))
    G["dams"] = np.load(os.path.join(scratch, "dams.npy"))


def tile_origin(name):
    parts = os.path.basename(name).split("_")
    lat = int(parts[4][1:]) * (1 if parts[4][0] == "N" else -1)
    lon = int(parts[6][1:]) * (1 if parts[6][0] == "E" else -1)
    return lat, lon


def sample(global_arr, per_deg, lat0, lon0, hp, wp):
    """A global north-up raster (per_deg cells per degree) at each pixel of the tile (nearest cell)."""
    r0 = int(round((90 - (lat0 + 1)) * per_deg))
    c0 = int(round((lon0 + 180) * per_deg))
    block = np.asarray(global_arr[r0:r0 + per_deg, c0:c0 + per_deg])
    return block[(np.arange(hp) * per_deg // hp)[:, None], (np.arange(wp) * per_deg // wp)[None, :]]


def varints(v):
    """LEB128 varints of non-negative ints, vectorised."""
    v = np.asarray(v, np.uint64)
    n = np.ones(len(v), np.int64)
    t = v >> np.uint64(7)
    while t.any():
        n += t > 0
        t >>= np.uint64(7)
    out = np.empty(int(n.sum()), np.uint8)
    pos = np.concatenate([[0], np.cumsum(n)[:-1]])
    for i in range(int(n.max()) if len(n) else 0):
        s = n > i
        byte = ((v[s] >> np.uint64(7 * i)) & np.uint64(0x7F)).astype(np.uint8)
        out[pos[s] + i] = byte | ((n[s] > i + 1).astype(np.uint8) << 7)
    return out.tobytes()


def zz(a):
    a = np.asarray(a, np.int64)
    return ((a << 1) ^ (a >> 63)).astype(np.uint64)


def lake_bytes(level, flags, rings):
    """One lake as coasts.bin-style varints: level (zigzag), flags, ring count, then per ring its vertex count and the
    zigzag deltas of its vertices (the first from the tile's south-west corner, then from the previous vertex)."""
    vals = [zz([level]), np.array([flags, len(rings)], np.uint64)]
    px = py = 0
    for r in rings:
        vals.append(np.array([len(r)], np.uint64))
        d = np.diff(np.vstack([[px, py], r]), axis=0)
        px, py = int(r[-1, 0]), int(r[-1, 1])
        vals.append(zz(d.ravel()))
    return varints(np.concatenate(vals))


def classify(st):
    """Class and missing flag of each lake (or each joined lake) from its stats, by the atlas's rules."""
    km2 = st["km2"]
    cls = np.zeros(len(km2), np.int8)                        # real
    cls[st["d3"] >= 0.3 * km2] = 1                           # raised
    cls[(st["p"] < 250) & (st["rough"] < 1)] = 5             # salt flat
    cls[(st["sh_res"] >= 0.3) | (st["d2"] >= 0.3 * km2)] = 4  # reservoir
    cls[st["sh_glac"] >= 0.5] = 6
    cls[polar(st["clat"], st["clon"])] = 3
    cls[st["sh_sea"] >= 0.5] = 2
    return cls, (st["sh_sea"] + st["sh_lake"]) < 0.5


def edges(lab):
    """The labels along the tile's four edges, as (positions, labels) of the lake pixels."""
    out = {}
    for side, line in (("top", lab[0]), ("bottom", lab[-1]), ("left", lab[:, 0]), ("right", lab[:, -1])):
        i = np.nonzero(line)[0]
        out[side] = (i.astype(np.int32), line[i].astype(np.int32), len(line))
    return out


def do_tile(path):
    lat0, lon0 = tile_origin(path)
    if lat0 < SOUTH:
        return None
    wbm = tifffile.imread(path)
    lake = wbm == 2
    if not lake.any():
        return None
    hp, wp = wbm.shape
    dem = tifffile.imread(os.path.join(DEM, os.path.basename(path).replace("_WBM.tif", "_DEM.tif"))).astype(np.float32)
    lab, k = ndi.label(lake, structure=np.ones((3, 3), bool))
    lf = lab.ravel()
    rows_lat = lat0 + 1 - (np.arange(hp) + 0.5) / hp
    px_km2 = (111.32 / hp) * (111.32 / wp) * np.cos(np.radians(rows_lat))
    km2 = np.bincount(lf, weights=np.broadcast_to(px_km2[:, None], (hp, wp)).ravel(), minlength=k + 1)
    npx = np.bincount(lf, minlength=k + 1)

    def share(m):
        return np.bincount(lf, weights=m.ravel().astype(np.float64), minlength=k + 1) / np.maximum(npx, 1)

    sea = sample(G["sea"], 120, lat0, lon0, hp, wp) > 0
    el = (sample(G["lakes"], 60, lat0, lon0, hp, wp) > 0) | sample(G["grid"], 30, lat0, lon0, hp, wp)
    sh_sea = share(sea)
    sh_lake = share(el & ~sea)
    sh_res = share(sample(G["res"], 60, lat0, lon0, hp, wp))
    sh_glac = share(sample(G["glac"], 120, lat0, lon0, hp, wp) >= 128)
    d = dem.ravel().astype(np.float64)
    mean = np.bincount(lf, weights=d, minlength=k + 1) / np.maximum(npx, 1)
    rough = np.sqrt(np.maximum(np.bincount(lf, weights=d * d, minlength=k + 1) / np.maximum(npx, 1) - mean * mean, 0))
    m = lf > 0
    order = np.lexsort((d[m], lf[m]))
    ds = d[m][order]
    starts = np.concatenate([[0], np.cumsum(npx[1:])[:-1]])
    level = np.zeros(k + 1)
    level[1:] = ds[starts + npx[1:] // 2]
    objs = ndi.find_objects(lab)
    yy, xx = np.indices((hp, wp))
    cy = np.bincount(lf, weights=yy.ravel(), minlength=k + 1) / np.maximum(npx, 1)
    cx = np.bincount(lf, weights=xx.ravel(), minlength=k + 1) / np.maximum(npx, 1)
    clat = lat0 + 1 - (cy + 0.5) / hp
    clon = lon0 + (cx + 0.5) / wp
    box = np.array([[lat0 + 1 - s[0].stop / hp, lon0 + s[1].start / wp, lat0 + 1 - s[0].start / hp, lon0 + s[1].stop / wp]
                    for s in objs]).reshape(-1, 4)
    box = np.vstack([[0, 0, 0, 0], box])
    # HydroLAKES dams (pour points) at each lake: type 2 reservoir, 3 natural lake raised by a dam. The atlas's margin
    # (√ of the dam's lake) was for sites that are a part of the reservoir; here each lake is whole (but for tile edges),
    # so the pour point must lie at the lake: its box plus 3 pixels and a twentieth of its own size
    dams = G["dams"]
    near_t = (dams[:, 1] > lat0 - 0.1) & (dams[:, 1] < lat0 + 1.1) & (dams[:, 0] > lon0 - 0.2) & (dams[:, 0] < lon0 + 1.2)
    d2 = np.zeros(k + 1)                                   # largest dam lake (km2) of each type whose pour point is here
    d3 = np.zeros(k + 1)
    coslat = max(math.cos(math.radians(lat0 + 0.5)), 0.05)
    mm = 3.0 / hp + 0.05 * np.sqrt(km2) / 111.32
    mk = 3.0 / wp + 0.05 * np.sqrt(km2) / 111.32 / coslat
    for dx, dy, da, dt in dams[near_t]:
        hit = (dy >= box[:, 0] - mm) & (dy <= box[:, 2] + mm) & (dx >= box[:, 1] - mk) & (dx <= box[:, 3] + mk)
        hit[0] = False
        tgt = d2 if dt == 2 else d3
        tgt[hit] = np.maximum(tgt[hit], da)
    P = G["p"]
    CH, CW = P.shape
    pr = np.clip(((90 - clat) / 180 * CH).astype(int), 0, CH - 1)
    pc = np.clip(((clon + 180) / 360 * CW).astype(int), 0, CW - 1)
    p_year = P[pr, pc].astype(np.float64)
    st = dict(km2=km2, sh_sea=sh_sea, sh_lake=sh_lake, sh_res=sh_res, sh_glac=sh_glac, rough=rough, p=p_year,
              clat=clat, clon=clon, d2=d2, d3=d3)
    if "ov" in G and os.path.basename(path) in G["ov_off"]:
        o = G["ov_off"][os.path.basename(path)]
        cls = G["ov"]["cls"][o:o + k + 1].astype(np.int8)
        missing = G["ov"]["missing"][o:o + k + 1].astype(bool)
        km2_lake = G["ov"]["km2"][o:o + k + 1].astype(np.float64)
        dam = np.where(cls == 4, 2, np.where(cls == 1, 3, 0)).astype(np.int8)
    else:
        cls, missing = classify(st)
        km2_lake = km2
        dam = np.where(d2 >= 0.3 * km2, 2, np.where(d3 >= 0.3 * km2, 3, 0)).astype(np.int8)
    if G.get("pass1"):
        return os.path.basename(path), k, {kk: v.astype(np.float32) for kk, v in st.items()}, edges(lab)

    # tallies: class x missing x size bin -> count, km2 (pieces in this tile, binned by their whole lake's size)
    b = np.clip(np.searchsorted(BINS, km2_lake, side="right") - 1, 0, len(BINS) - 2)
    tally = np.zeros((len(CLASSES), 2, len(BINS) - 1, 2))
    sel = np.arange(1, k + 1)
    np.add.at(tally, (cls[sel], missing[sel].astype(int), b[sel], 0), 1)
    np.add.at(tally, (cls[sel], missing[sel].astype(int), b[sel], 1), km2[sel])

    # outlines of the lakes that would be drawn (real or raised): one marching-squares pass over the tile
    drawn = np.zeros(k + 1, bool)
    drawn[1:] = cls[1:] <= 1
    keep = drawn[lab]
    z = np.zeros((hp + 2, wp + 2), np.float32)
    z[1:-1, 1:-1] = keep
    gen = contourpy.contour_generator(z=z, name="serial", fill_type="OuterOffset", corner_mask=False)
    polys, offs = gen.filled(0.5, 1.5)
    owners, geoms = [], []
    for pts, off in zip(polys, offs):
        # the lake pixel next to the first vertex (one coordinate whole, the other half)
        x, y = pts[0]
        if abs(x - round(x)) < 1e-6:
            cands = [(int(round(x)), int(math.floor(y))), (int(round(x)), int(math.ceil(y)))]
        else:
            cands = [(int(math.floor(x)), int(round(y))), (int(math.ceil(x)), int(round(y)))]
        owner = 0
        for cx_, cy_ in cands:
            if 1 <= cy_ <= hp and 1 <= cx_ <= wp and keep[cy_ - 1, cx_ - 1]:
                owner = lab[cy_ - 1, cx_ - 1]
                break
        rings = [pts[off[i]:off[i + 1]] for i in range(len(off) - 1)]
        geoms.append(shapely.Polygon(rings[0], rings[1:]))
        owners.append(owner)
    owners = np.array(owners, np.int64)
    geoms = np.array(geoms, dtype=object)
    sizes, verts, window = {}, {}, None
    in_window = WINDOW[0] <= lat0 < WINDOW[2] and WINDOW[1] <= lon0 < WINDOW[3]
    for tol in TOLS:
        simp = shapely.simplify(geoms, tol, preserve_topology=True) if len(geoms) else geoms
        lakes = []
        for g, o in zip(simp, owners):
            if o == 0 or g is None or g.is_empty:
                continue
            parts = list(g.geoms) if g.geom_type == "MultiPolygon" else [g]
            rings = []
            for pg in parts:
                for ring in [pg.exterior] + list(pg.interiors):
                    c = np.asarray(ring.coords)[:-1]
                    if len(c) < 3:
                        continue
                    X = np.round((c[:, 0] - 0.5) * UNIT / wp).astype(np.int64)
                    Y = np.round((hp - (c[:, 1] - 0.5)) * UNIT / hp).astype(np.int64)
                    rings.append(np.column_stack([X, Y]))
            if rings:
                lakes.append((o, sum(len(r) for r in rings),
                              lake_bytes(int(round(level[o])), int(missing[o]) | (int(cls[o] == 1) << 1), rings)))
        for s in SELS:
            for cut in CUTS:
                chosen = [(nv, bb) for o, nv, bb in lakes if km2_lake[o] >= cut and (s == "real-all" or missing[o])]
                raw = varints([len(chosen)]) + b"".join(bb for _, bb in chosen) if chosen else b""
                sizes[(s, cut, tol)] = len(brotli.compress(raw, quality=11, lgwin=22)) if chosen else 0
                verts[(s, cut, tol)] = (len(chosen), sum(nv for nv, _ in chosen), len(raw))
        if in_window and tol == TOLS[0]:
            window = [(int(o), int(cls[o]), bool(missing[o]), float(level[o]), float(km2[o]),
                       shapely.affinity.affine_transform(g, [1 / wp, 0, 0, -1 / hp, lon0 - 0.5 / wp, lat0 + 1 + 0.5 / hp]))
                      for g, o in zip(simp, owners) if o]
    # lakes worth listing: 1 km2 or more
    big = [(float(clat[i]), float(clon[i]), float(km2[i]), int(cls[i]), bool(missing[i]), float(level[i]), float(rough[i]),
            float(p_year[i]), float(sh_sea[i]), float(sh_lake[i]), int(dam[i])) for i in np.nonzero(km2 >= 1.0)[0] if i]
    return os.path.basename(path), tally, sizes, verts, big, window


def prepare(scratch):
    sys.path.insert(0, TOOLS)
    import bake_earth as be
    import shapefile
    if not os.path.exists(os.path.join(scratch, "p_year.npy")):
        np.save(os.path.join(scratch, "p_year.npy"), np.array(be.read_climate_planes()[3]))
    if not os.path.exists(os.path.join(scratch, "dams.npy")):
        hl = shapefile.Reader(os.path.join(RAW, "hydrolakes", "HydroLAKES_points_v10_shp", "HydroLAKES_points_v10.shp"),
                              encoding="latin1", encodingErrors="replace")
        dams = np.array([(r["Pour_long"], r["Pour_lat"], r["Lake_area"], r["Lake_type"]) for r in
                         hl.iterRecords(fields=["Lake_type", "Lake_area", "Pour_long", "Pour_lat"]) if r["Lake_type"] in (2, 3)])
        np.save(os.path.join(scratch, "dams.npy"), dams)
        log(f"{len(dams)} HydroLAKES dams")


def join_tiles(out, workers, tiles):
    """Pass 1: each tile's lake stats and edge pixels; lakes joined across tile edges (8-connected, east-west and
    north-south; corners not), tagged as whole lakes; writes merge.npz (per tile label: class, missing, whole lake km2)
    and the whole-lake tallies and lists."""
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components
    res = {}
    with Pool(workers, initializer=init_worker, initargs=(out, True, None)) as pool:
        for i, r in enumerate(pool.imap_unordered(do_tile, tiles, chunksize=4), 1):
            if i % 5000 == 0:
                log(f"pass 1: {i}/{len(tiles)} tiles")
            if r is not None:
                res[r[0]] = r[1:]
    names = sorted(res)
    offs, o = {}, 0
    for nm in names:
        offs[nm] = o
        o += res[nm][0] + 1
    N = o
    keys = list(res[names[0]][1].keys())
    st = {kk: np.concatenate([res[nm][1][kk] for nm in names]).astype(np.float64) for kk in keys}
    st["km2"][[offs[nm] for nm in names]] = 0          # label 0: the tile's land, not a lake
    at = {tile_origin(nm): nm for nm in names}
    A, B = [], []

    def link(off_a, la, off_b, lb):
        m = (la > 0) & (lb > 0)
        A.append(off_a + la[m].astype(np.int64)); B.append(off_b + lb[m].astype(np.int64))
    for nm in names:
        lat0, lon0 = tile_origin(nm)
        e = res[nm][2]
        east = at.get((lat0, lon0 + 1 if lon0 < 179 else -180))
        if east:
            ri, rl, n = e["right"]
            li, ll, n2 = res[east][2]["left"]
            full = np.zeros(n2, np.int64); full[li] = ll
            for d in (-1, 0, 1):
                link(offs[nm], rl, offs[east], full[np.clip(ri + d, 0, n2 - 1)])
        north = at.get((lat0 + 1, lon0))
        if north:
            ti, tl, wt = e["top"]
            bi, bl, wn = res[north][2]["bottom"]
            fn = np.zeros(wn, np.int64); fn[bi] = bl
            ft = np.zeros(wt, np.int64); ft[ti] = tl
            for d in (-1, 0, 1):
                link(offs[nm], tl, offs[north], fn[np.clip(((ti + 0.5) * wn / wt).astype(int) + d, 0, wn - 1)])
                link(offs[nm], ft[np.clip(((bi + 0.5) * wt / wn).astype(int) + d, 0, wt - 1)], offs[north], bl)
    A = np.concatenate(A) if A else np.zeros(0, np.int64)
    B = np.concatenate(B) if B else np.zeros(0, np.int64)
    g = coo_matrix((np.ones(len(A), np.int8), (A, B)), shape=(N, N))
    nc, comp = connected_components(g, directed=False)
    w = st["km2"]
    km2 = np.bincount(comp, weights=w, minlength=nc)
    pieces = np.bincount(comp, weights=(w > 0).astype(float), minlength=nc)
    M = {"km2": km2}
    for kk in ("sh_sea", "sh_lake", "sh_res", "sh_glac", "rough", "p", "clat", "clon"):
        M[kk] = np.bincount(comp, weights=w * st[kk], minlength=nc) / np.maximum(km2, 1e-12)
    for kk in ("d2", "d3"):
        M[kk] = np.zeros(nc)
        np.maximum.at(M[kk], comp, st[kk])
    cls, missing = classify(M)
    np.savez(os.path.join(out, "merge.npz"), cls=cls[comp].astype(np.int8), missing=missing[comp],
             km2=km2[comp].astype(np.float32), names=np.array(names), offs=np.array([offs[nm] for nm in names], np.int64))
    log(f"pass 1: {N - len(names)} lake pieces in {len(names)} tiles joined into {int((km2 > 0).sum())} lakes "
        f"({int((pieces > 1).sum())} span tiles)")
    lakes = np.nonzero(km2 > 0)[0]
    lines = ["whole lakes (joined across tile edges)", "class      missing  size bin (km2)        lakes        km2"]
    b = np.clip(np.searchsorted(BINS, km2[lakes], side="right") - 1, 0, len(BINS) - 2)
    for c, cn in enumerate(CLASSES):
        for mi in (0, 1):
            for bi in range(len(BINS) - 1):
                s = (cls[lakes] == c) & (missing[lakes] == bool(mi)) & (b == bi)
                if s.any():
                    lines.append(f"{cn:10s} {'yes' if mi else 'no ':7s}  {BINS[bi]:>7g} - {BINS[bi + 1]:<9g} {int(s.sum()):10d} "
                                 f"{km2[lakes][s].sum():12.1f}")

    def top(sel, title, n=25):
        idx = lakes[sel[lakes]]
        idx = idx[np.argsort(-km2[idx])][:n]
        lines.append(f"\n{title}: {int(sel[lakes].sum())} lakes, {km2[lakes][sel[lakes]].sum():.0f} km²")
        for i in idx:
            lines.append(f"  {M['clat'][i]:8.3f} {M['clon'][i]:9.3f} {km2[i]:9.1f} km² in {int(pieces[i])} pieces  "
                         f"rough {M['rough'][i]:5.1f}  p {M['p'][i]:5.0f} mm  earth sea {M['sh_sea'][i]:.2f} lake {M['sh_lake'][i]:.2f}  "
                         f"reservoir share {M['sh_res'][i]:.2f}  dam lakes {M['d2'][i]:.0f}/{M['d3'][i]:.0f} km²")
    top((cls <= 1) & missing, "missing real lakes (incl. raised)", 40)
    top((cls == 1) & missing, "of them raised by a dam", 10)
    top((cls == 4) & missing, "reservoir (missing)", 20)
    top((cls == 5) & missing, "salt flat (missing)", 30)
    top((cls <= 1) & ~missing, "real lakes Earth has", 10)
    txt = "\n".join(lines)
    with open(os.path.join(out, "lakes-joined.txt"), "w", encoding="utf8") as f:
        f.write(txt + "\n")
    print(txt)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    out = sys.argv[1]
    workers = int(sys.argv[2]) if len(sys.argv) > 2 else 16
    filt = sys.argv[3] if len(sys.argv) > 3 else ""
    os.makedirs(out, exist_ok=True)
    prepare(out)
    tiles = sorted(p for p in glob.glob(os.path.join(WBM, "*_WBM.tif")) if filt in os.path.basename(p))
    log(f"{len(tiles)} tiles, {workers} workers")
    ov = os.path.join(out, "merge.npz")
    if not os.path.exists(ov):
        join_tiles(out, workers, tiles)
    tally = np.zeros((len(CLASSES), 2, len(BINS) - 1, 2))
    sizes, verts, big, window = {}, {}, [], []
    done = 0
    with Pool(workers, initializer=init_worker, initargs=(out, False, ov)) as pool:
        for r in pool.imap_unordered(do_tile, tiles, chunksize=4):
            done += 1
            if done % 1000 == 0:
                log(f"{done}/{len(tiles)} tiles")
            if r is None:
                continue
            name, t, s, v, b, w = r
            tally += t
            for key, val in s.items():
                sizes[key] = sizes.get(key, 0) + val
            for key, val in v.items():
                a = verts.get(key, (0, 0, 0))
                verts[key] = tuple(x + y for x, y in zip(a, val))
            big.extend(b)
            if w:
                window.extend(w)
    np.save(os.path.join(out, "tally.npy"), tally)
    with open(os.path.join(out, "sizes.json"), "w", encoding="utf8") as f:
        json.dump({"|".join(map(str, k)): [sizes[k], *verts[k]] for k in sizes}, f, indent=0)
    with open(os.path.join(out, "big.json"), "w", encoding="utf8") as f:
        json.dump(big, f)
    with open(os.path.join(out, "window.pkl"), "wb") as f:
        pickle.dump(window, f)
    lines = ["class      missing  size bin (km2)        lakes        km2"]
    for c, cn in enumerate(CLASSES):
        for mi in (0, 1):
            for bi in range(len(BINS) - 1):
                n, a = tally[c, mi, bi]
                if n:
                    lines.append(f"{cn:10s} {'yes' if mi else 'no ':7s}  {BINS[bi]:>7g} - {BINS[bi + 1]:<9g} {int(n):10d} {a:12.1f}")
    lines.append("")
    lines.append("selection     cut km2  tol px   lakes   vertices    raw bytes   brotli bytes")
    for k in sorted(sizes):
        n, nv, raw = verts[k]
        lines.append(f"{k[0]:13s} {k[1]:7g} {k[2]:7g} {n:8d} {nv:10d} {raw:12d} {sizes[k]:14d}")
    txt = "\n".join(lines)
    print(txt)
    with open(os.path.join(out, "summary.txt"), "w", encoding="utf8") as f:
        f.write(txt + "\n")
    log("done")


if __name__ == "__main__":
    main()
