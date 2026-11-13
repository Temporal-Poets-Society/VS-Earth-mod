"""Bake lakes-glo90.bin: the lakes of the Copernicus GLO-90 water body mask (class 2) as outlines with their levels, for
worlds on GLO-90 heights (Earth 0053 item 17, measuring pass: docs/research/lake-outlines-2026-11-07.md).

The author: "Take all lakes. We will increase size anyway and can put large data into same repo as height map in git."

Every lake of the mask north of 60°S is drawn, whatever its size, except
- lakes Earth's sea has (half or more under Earth's sea);
- reservoirs (no modern dams in 1318): half or more inside a Natural Earth reservoir (featurecla "Reservoir", which the
  bake leaves out) or a lake the bake dropped as a reservoir, or a HydroLAKES reservoir (type 2) whose dam is at the lake
  and whose lake is at least 0.3 times its size;
- lakes on Earth's salt lakes and the terminal lakes it fills on purpose (decision 0005: the Aral Sea, Lake Chad,
  Urmia, the Dead Sea, ...): 30 % or more inside such grid cells (or the cells next to them). Earth's grid keeps those,
  at their 1318 extent, which the mask (2011-2015) has shrunk;
- lakes on glaciers (half or more under RGI glaciers).
Salt flats and pans in dry climates (under 250 mm a year, flat, and arid by de Martonne's index: rain / (mean
temperature at the lake's level + 10) under 15, so not the cold, dry Arctic, whose lakes are fresh) are drawn as salt
lakes, filled as in wet years as decision 0005 has Earth's own salt pans. A lake's level is the median GLO-90 height of its largest piece.

Pieces of one lake in neighbouring 1° tiles are joined (pass 1) before they are judged. Outlines are marching squares at
the pixel edges, simplified by half a pixel; vertices on a tile's edge stay where they are, so neighbours meet exactly.

File (little-endian): "EMLO", version 1, unit (vertex units per degree), tile size (degrees), tiles across, tiles down,
then one state byte per tile (row 0 at 90°S, column 0 at 180°W: 255 no data, the grid decides; 0 the mask has no
drawn lake here; 1 lakes), then (offset, length) int32 per tile with lakes, then each such tile's brotli stream:
varint lake count; per lake zigzag varint level (decimetres), varint flags (1 salt, 2 raised by a dam, 4 missing in
Earth's grid), varint ring count; per ring varint vertex count and zigzag varint deltas (x, y in units from the tile's
south-west corner; from (0, 0) at each lake's first vertex, then from the previous vertex). Rings are closed (the last
vertex joins the first); an edge with both ends on the tile's edge is that edge, not a shore.

Usage: python -I tools/bake_lake_outlines.py OUTDIR [workers] [tile-name-filter]
Needs what tools/measure_lake_outlines.py needs, plus rawdata/ne_lakes and earthdata/terrain.bin and lakes.bin.
"""
import glob, hashlib, json, math, os, struct, sys, time
from multiprocessing import Pool

import numpy as np
import tifffile
import brotli
import contourpy
import shapely
from scipy import ndimage as ndi

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RAW = os.environ.get("EARTH_RAW", os.path.join(ROOT, "rawdata"))
DEM = os.path.join(RAW, "copernicus-glo90")
WBM = os.path.join(RAW, "copernicus-glo90-wbm")
CACHE = os.environ.get("ATLAS_CACHE", os.path.join(ROOT, "preview", "water-atlas", "cache"))
TOOLS = os.path.dirname(os.path.abspath(__file__))
EARTHDATA = os.path.join(ROOT, "earthdata")
SOUTH = -60
UNIT = 2400                         # vertex units per degree (half a 3" pixel north-south)
TOL = 0.5                           # simplification tolerance, pixels
VERSION = 2 if os.environ.get("EARTH_GDW", "0") == "1" else 1   # 2: with Global Dam Watch (0069 rule 9)
FLAG_SALT, FLAG_RAISED, FLAG_MISSING = 1, 2, 4
CLASSES = ("drawn", "sea", "reservoir", "kept", "ice")
T0 = time.time()
G = {}


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


# ------------------------------------------------------------------------------------------- inputs (global, cached)

def terminal_cells(size):
    """Flat 2' grid indices that lakes.bin makes lake (as tools/water_atlas.py terminal_lake_cells)."""
    with open(os.path.join(EARTHDATA, "lakes.bin"), "rb") as f:
        b = f.read()
    assert b[:4] == b"EMLK"
    _, w, npatch, _ = struct.unpack_from("<iiii", b, 4)
    buf = brotli.decompress(b[20:])
    a = np.frombuffer(buf, np.uint8)
    o = int(np.flatnonzero((a & 0x80) == 0)[npatch - 1]) + 1
    v = a[:o]
    end = (v & 0x80) == 0
    start = np.concatenate([[0], np.flatnonzero(end)[:-1] + 1])
    grp = np.concatenate([[0], np.cumsum(end)[:-1]])
    vals = (v & 0x7F).astype(np.int64) << (7 * (np.arange(o) - start[grp]))
    idx = np.cumsum(np.add.reduceat(vals, start))
    flags = np.frombuffer(buf, np.uint8, npatch, o + 4 * npatch)
    idx = idx[(flags & 2) > 0]
    assert idx.max() < size
    return idx


def prepare(out):
    sys.path.insert(0, TOOLS)
    import bake_earth as be
    import shapefile
    from PIL import Image, ImageDraw
    p = os.path.join(out, "p_year.npy")
    if not os.path.exists(p):
        np.save(p, np.array(be.read_climate_planes()[3]))
    p = os.path.join(out, "t_mean.npy")
    if not os.path.exists(p):
        np.save(p, (be.read_climate_planes()[0] / 100).astype(np.float32))
    p = os.path.join(out, "dams.npy")
    if not os.path.exists(p):
        # HydroLAKES' reservoirs and lake controls; with EARTH_GDW=1 (version 2, decision 0069 rule 9) also Global Dam
        # Watch's reservoirs HydroLAKES lacks, as reservoirs (tools/reservoir_list.py)
        sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
        from reservoir_list import reservoirs
        dams = reservoirs(RAW, (2, 3), -90.0, log)[:, [1, 2, 3, 5]]
        np.save(p, dams)
        log(f"{len(dams)} HydroLAKES dams")
    p = os.path.join(out, "kept2m.npy")
    if not os.path.exists(p):
        # Earth's salt lakes and terminal lakes on the 2' grid, and the cells next to them
        flags = be.read_terrain_flags()
        kept = ((flags & 2) > 0) & ((flags & 4) > 0)
        kept.ravel()[terminal_cells(kept.size)] = True
        kept = ndi.binary_dilation(kept, structure=np.ones((3, 3), bool))
        np.save(p, kept)
        log(f"kept grid lakes: {int(kept.sum())} 2' cells with their ring")
    p = os.path.join(out, "neres1m.npy")
    if not os.path.exists(p):
        # Natural Earth reservoirs (featurecla "Reservoir"), which the bake leaves out, at 1'
        img = Image.new("1", (360 * 60, 180 * 60), 0)
        dr = ImageDraw.Draw(img)
        n = 0
        for f in ("ne_10m_lakes", "ne_10m_lakes_europe", "ne_10m_lakes_north_america"):
            r = shapefile.Reader(os.path.join(RAW, "ne_lakes", f), encoding="utf-8")
            for sr in r.iterShapeRecords():
                if (sr.record.as_dict().get("featurecla") or "") != "Reservoir" or not sr.shape.points:
                    continue
                parts = list(sr.shape.parts) + [len(sr.shape.points)]
                for i in range(len(parts) - 1):
                    pts = sr.shape.points[parts[i]:parts[i + 1]]
                    if len(pts) >= 3:
                        dr.polygon([((x + 180) * 60, (90 - y) * 60) for x, y in pts], fill=1)
                n += 1
        np.save(p, np.array(img, bool))
        log(f"Natural Earth reservoirs: {n}")


def init_worker(out, ov=None):
    G["sea"] = np.load(os.path.join(CACHE, "sea.npy"), mmap_mode="r")
    G["lakes"] = np.load(os.path.join(CACHE, "lakes1m.npy"), mmap_mode="r")
    G["grid"] = np.load(os.path.join(CACHE, "gridlakes2m-terminal.npy"), mmap_mode="r")
    G["res"] = np.load(os.path.join(CACHE, "reservoirs1m.npy"), mmap_mode="r")
    G["glac"] = np.load(os.path.join(CACHE, "glaciers.npy"), mmap_mode="r")
    G["kept"] = np.load(os.path.join(out, "kept2m.npy"), mmap_mode="r")
    G["neres"] = np.load(os.path.join(out, "neres1m.npy"), mmap_mode="r")
    G["p"] = np.load(os.path.join(out, "p_year.npy"))
    G["t"] = np.load(os.path.join(out, "t_mean.npy"))
    G["dams"] = np.load(os.path.join(out, "dams.npy"))
    if ov:
        z = np.load(ov)
        G["ov"] = {k: z[k] for k in ("draw", "flags", "level_dm")}
        G["ov_off"] = dict(zip(z["names"].tolist(), z["offs"].tolist()))


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


# ------------------------------------------------------------------------------------------- per tile

def tile_lakes(path):
    """The tile's lake labels and each label's stats, or None without lakes."""
    lat0, lon0 = tile_origin(path)
    if lat0 < SOUTH:
        return None
    wbm = tifffile.imread(path)
    lake = wbm == 2
    if not lake.any():
        return None
    hp, wp = wbm.shape
    dem = tifffile.imread(os.path.join(DEM, os.path.basename(path).replace("_WBM.tif", "_DEM.tif"))).astype(np.float64)
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
    st = {"km2": km2, "sh_sea": share(sea), "sh_lake": share(el & ~sea), "sh_res": share(sample(G["res"], 60, lat0, lon0, hp, wp)),
          "sh_neres": share(sample(G["neres"], 60, lat0, lon0, hp, wp)), "sh_kept": share(sample(G["kept"], 30, lat0, lon0, hp, wp)),
          "sh_glac": share(sample(G["glac"], 120, lat0, lon0, hp, wp) >= 128)}
    d = dem.ravel()
    mean = np.bincount(lf, weights=d, minlength=k + 1) / np.maximum(npx, 1)
    st["rough"] = np.sqrt(np.maximum(np.bincount(lf, weights=d * d, minlength=k + 1) / np.maximum(npx, 1) - mean * mean, 0))
    m = lf > 0
    order = np.lexsort((d[m], lf[m]))
    ds = d[m][order]
    starts = np.concatenate([[0], np.cumsum(npx[1:])[:-1]])
    level = np.zeros(k + 1)
    level[1:] = ds[starts + npx[1:] // 2]
    st["level"] = level
    yy, xx = np.indices((hp, wp))
    cy = np.bincount(lf, weights=yy.ravel(), minlength=k + 1) / np.maximum(npx, 1)
    cx = np.bincount(lf, weights=xx.ravel(), minlength=k + 1) / np.maximum(npx, 1)
    st["clat"] = lat0 + 1 - (cy + 0.5) / hp
    st["clon"] = lon0 + (cx + 0.5) / wp
    objs = ndi.find_objects(lab)
    box = np.vstack([[0, 0, 0, 0], np.array([[lat0 + 1 - s[0].stop / hp, lon0 + s[1].start / wp, lat0 + 1 - s[0].start / hp,
                                               lon0 + s[1].stop / wp] for s in objs]).reshape(-1, 4)])
    # HydroLAKES dams (pour points) at each lake: its box plus 3 pixels and a twentieth of its own size
    dams = G["dams"]
    near_t = (dams[:, 1] > lat0 - 0.1) & (dams[:, 1] < lat0 + 1.1) & (dams[:, 0] > lon0 - 0.2) & (dams[:, 0] < lon0 + 1.2)
    st["d2"], st["d3"] = np.zeros(k + 1), np.zeros(k + 1)
    coslat = max(math.cos(math.radians(lat0 + 0.5)), 0.05)
    mm = 3.0 / hp + 0.05 * np.sqrt(km2) / 111.32
    mk = 3.0 / wp + 0.05 * np.sqrt(km2) / 111.32 / coslat
    for dx, dy, da, dt in dams[near_t]:
        hit = (dy >= box[:, 0] - mm) & (dy <= box[:, 2] + mm) & (dx >= box[:, 1] - mk) & (dx <= box[:, 3] + mk)
        hit[0] = False
        tgt = st["d2"] if dt == 2 else st["d3"]
        tgt[hit] = np.maximum(tgt[hit], da)
    P = G["p"]
    CH, CW = P.shape
    pr = np.clip(((90 - st["clat"]) / 180 * CH).astype(int), 0, CH - 1)
    pc = np.clip(((st["clon"] + 180) / 360 * CW).astype(int), 0, CW - 1)
    st["p"] = P[pr, pc].astype(np.float64)
    st["t"] = G["t"][pr, pc].astype(np.float64)
    return lat0, lon0, hp, wp, lab, k, st


def edges(lab):
    out = {}
    for side, line in (("top", lab[0]), ("bottom", lab[-1]), ("left", lab[:, 0]), ("right", lab[:, -1])):
        i = np.nonzero(line)[0]
        out[side] = (i.astype(np.int32), line[i].astype(np.int32), len(line))
    return out


def pass1(path):
    r = tile_lakes(path)
    if r is None:
        return None
    lat0, lon0, hp, wp, lab, k, st = r
    return os.path.basename(path), k, {kk: v.astype(np.float32 if kk != "level" else np.float64) for kk, v in st.items()}, edges(lab)


def classify(M):
    """Whole lakes: class (index into CLASSES) and flags."""
    km2 = M["km2"]
    cls = np.zeros(len(km2), np.int8)
    cls[M["sh_glac"] >= 0.5] = 4
    cls[(M["sh_res"] >= 0.3) | (M["sh_neres"] >= 0.3) | (M["d2"] >= 0.3 * km2)] = 2
    cls[M["sh_kept"] >= 0.3] = 3
    cls[M["sh_sea"] >= 0.5] = 1
    # salt: dry (under 250 mm a year), flat (GLO-90 flattens water, so a pan's spread is small) and arid by de Martonne's
    # index, rain / (mean temperature + 10) under 15: in the cold, dry Arctic little evaporates and the lakes are fresh
    # (Great Bear Lake, Taymyr, the Mackenzie delta came out salt on rain alone). climate.bin's temperatures are reduced to
    # sea level (Uyuni read 32.7 °C), so the lake's own is taken down by 6.5 °C per km of its level.
    t = M["t"] - 0.0065 * np.maximum(M["level"], 0)
    arid = (t + 10 > 0) & (M["p"] < 15 * (t + 10))
    flags = (((M["p"] < 250) & (M["rough"] < 1) & arid) * FLAG_SALT | (M["d3"] >= 0.3 * km2) * FLAG_RAISED
             | ((M["sh_sea"] + M["sh_lake"]) < 0.5) * FLAG_MISSING).astype(np.uint8)
    return cls, flags


def simplify_ring(c, wp, hp, tol=TOL):
    """A closed ring (z-index coordinates, first == last) simplified by tol (pixels), with its vertices on the tile's edge
    kept (and those in the middle of a straight run along the edge dropped). None when it collapses."""
    c = c[:-1]
    e = 1e-9

    def sides(p):
        return np.column_stack([np.abs(p[:, 0] - 0.5) < e, np.abs(p[:, 0] - (wp + 0.5)) < e,
                                np.abs(p[:, 1] - 0.5) < e, np.abs(p[:, 1] - (hp + 0.5)) < e])
    onb = sides(c).any(axis=1)
    if not onb.any():
        g = shapely.simplify(shapely.LinearRing(c), tol)
        out = np.asarray(g.coords)[:-1] if not g.is_empty else np.zeros((0, 2))
    else:
        s0 = int(np.nonzero(onb)[0][0])
        c = np.roll(c, -s0, axis=0)
        onb = np.roll(onb, -s0)
        cc = np.vstack([c, c[:1]])
        fixed = np.append(np.nonzero(onb)[0], len(c))
        pts = [cc[0]]
        for a, b in zip(fixed[:-1], fixed[1:]):
            if b - a > 1:
                seg = np.asarray(shapely.simplify(shapely.LineString(cc[a:b + 1]), tol).coords)
                pts.extend(seg[1:])
            else:
                pts.append(cc[b])
        out = np.array(pts[:-1])
        # drop edge vertices in the middle of a straight run along the same tile edge
        s = sides(out)
        out = out[~(np.roll(s, 1, axis=0) & s & np.roll(s, -1, axis=0)).any(axis=1)]
    return out if len(out) >= 3 else None


def zz(a):
    a = np.asarray(a, np.int64)
    return ((a << 1) ^ (a >> 63)).astype(np.uint64)


def varints(v):
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


def lake_bytes(level_dm, flags, rings):
    vals = [zz([level_dm]), np.array([flags, len(rings)], np.uint64)]
    px = py = 0
    for r in rings:
        vals.append(np.array([len(r)], np.uint64))
        d = np.diff(np.vstack([[px, py], r]), axis=0)
        px, py = int(r[-1, 0]), int(r[-1, 1])
        vals.append(zz(d.ravel()))
    return varints(np.concatenate(vals))


def pass2(path):
    name = os.path.basename(path)
    lat0, lon0 = tile_origin(path)
    if lat0 < SOUTH:
        return None
    if name not in G["ov_off"]:
        return name, 0, b"", 0, 0
    r = tile_lakes(path)
    lat0, lon0, hp, wp, lab, k, st = r
    o = G["ov_off"][name]
    draw = G["ov"]["draw"][o:o + k + 1].astype(bool)
    flags = G["ov"]["flags"][o:o + k + 1]
    level_dm = G["ov"]["level_dm"][o:o + k + 1]
    draw[0] = False
    if not draw.any():
        return name, 0, b"", 0, 0
    keep = draw[lab]
    z = np.zeros((hp + 2, wp + 2), np.float32)
    z[1:-1, 1:-1] = keep
    polys, offs = contourpy.contour_generator(z=z, name="serial", fill_type="OuterOffset", corner_mask=False).filled(0.5, 1.5)
    lakes, nv = [], 0
    for pts, off in zip(polys, offs):
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
        if owner == 0:
            continue
        rings = []
        for i in range(len(off) - 1):
            ring = simplify_ring(pts[off[i]:off[i + 1]], wp, hp)
            if ring is None:
                if i == 0:
                    break
                continue
            X = np.round((ring[:, 0] - 0.5) * UNIT / wp).astype(np.int64)
            Y = np.round((hp - (ring[:, 1] - 0.5)) * UNIT / hp).astype(np.int64)
            rings.append(np.column_stack([X, Y]))
        if not rings:
            continue
        nv += sum(len(r_) for r_ in rings)
        lakes.append(lake_bytes(int(level_dm[owner]), int(flags[owner]), rings))
    if not lakes:
        return name, 0, b"", 0, 0
    raw = varints([len(lakes)]) + b"".join(lakes)
    return name, len(lakes), brotli.compress(raw, quality=11, lgwin=22), nv, len(raw)


# ------------------------------------------------------------------------------------------- whole lakes

def join(out, workers, tiles):
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components
    res = {}
    with Pool(workers, initializer=init_worker, initargs=(out,)) as pool:
        for i, r in enumerate(pool.imap_unordered(pass1, tiles, chunksize=4), 1):
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
    st["km2"][[offs[nm] for nm in names]] = 0          # label 0: the tile's land
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
    nc, comp = connected_components(coo_matrix((np.ones(len(A), np.int8), (A, B)), shape=(N, N)), directed=False)
    w = st["km2"]
    km2 = np.bincount(comp, weights=w, minlength=nc)
    M = {"km2": km2}
    for kk in ("sh_sea", "sh_lake", "sh_res", "sh_neres", "sh_kept", "sh_glac", "rough", "p", "t", "clat", "clon"):
        M[kk] = np.bincount(comp, weights=w * st[kk], minlength=nc) / np.maximum(km2, 1e-12)
    for kk in ("d2", "d3"):
        M[kk] = np.zeros(nc)
        np.maximum.at(M[kk], comp, st[kk])
    # the level of the largest piece
    order = np.lexsort((-w, comp))
    first = np.ones(N, bool)
    first[1:] = comp[order][1:] != comp[order][:-1]
    level = np.zeros(nc)
    level[comp[order][first]] = st["level"][order][first]
    M["level"] = level
    cls, flags = classify(M)
    level_dm = np.round(level * 10).astype(np.int32)
    np.savez(os.path.join(out, "merge.npz"), draw=(cls == 0)[comp], flags=flags[comp], level_dm=level_dm[comp],
             names=np.array(names), offs=np.array([offs[nm] for nm in names], np.int64))
    lakes = np.nonzero(km2 > 0)[0]
    lines = [f"{N - len(names)} lake pieces in {len(names)} tiles joined into {len(lakes)} lakes", ""]
    for c, cn in enumerate(CLASSES):
        s = cls[lakes] == c
        lines.append(f"{cn:10s} {int(s.sum()):9d} lakes {km2[lakes][s].sum():12.0f} km²")
    d = lakes[cls[lakes] == 0]
    for f, fn in ((FLAG_SALT, "salt"), (FLAG_RAISED, "raised"), (FLAG_MISSING, "missing in Earth's grid")):
        s = (flags[d] & f) > 0
        lines.append(f"  drawn, {fn}: {int(s.sum())} lakes, {km2[d][s].sum():.0f} km²")

    def top(sel, title, n=25):
        idx = lakes[sel[lakes]]
        idx = idx[np.argsort(-km2[idx])][:n]
        lines.append(f"\n{title}: {int(sel[lakes].sum())} lakes, {km2[lakes][sel[lakes]].sum():.0f} km²")
        for i in idx:
            lines.append(f"  {M['clat'][i]:8.3f} {M['clon'][i]:9.3f} {km2[i]:9.1f} km²  level {level[i]:7.1f} m  flags {flags[i]}  "
                         f"sea {M['sh_sea'][i]:.2f} lake {M['sh_lake'][i]:.2f} res {M['sh_res'][i]:.2f}/{M['sh_neres'][i]:.2f} "
                         f"kept {M['sh_kept'][i]:.2f}  dams {M['d2'][i]:.0f}/{M['d3'][i]:.0f}  rain {M['p'][i]:.0f} mm  "
                         f"{M['t'][i] - 0.0065 * max(level[i], 0):.1f} °C")
    top((cls == 0) & ((flags & FLAG_MISSING) > 0), "drawn, missing in Earth's grid", 40)
    top((cls == 0) & ((flags & FLAG_SALT) > 0), "drawn as salt lakes", 25)
    top(cls == 2, "reservoirs (left out)", 25)
    top(cls == 3, "on Earth's salt and terminal lakes (left to the grid)", 25)
    txt = "\n".join(lines)
    with open(os.path.join(out, "lakes-joined.txt"), "w", encoding="utf8") as f:
        f.write(txt + "\n")
    print(txt, flush=True)


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
        join(out, workers, tiles)
    TX, TY = 360, 180
    states = np.full(TX * TY, 255, np.uint8)
    blobs = {}
    nl = nv = nraw = 0
    with Pool(workers, initializer=init_worker, initargs=(out, ov)) as pool:
        for i, r in enumerate(pool.imap_unordered(pass2, tiles, chunksize=4), 1):
            if i % 5000 == 0:
                log(f"pass 2: {i}/{len(tiles)} tiles")
            if r is None:
                continue
            name, n, blob, v, raw = r
            lat0, lon0 = tile_origin(name)
            t = (lat0 + 90) * TX + (lon0 + 180)
            states[t] = 1 if n else 0
            if n:
                blobs[t] = blob
                nl += n; nv += v; nraw += raw
    head = struct.pack("<4siiiii", b"EMLO", VERSION, UNIT, 1, TX, TY)
    index = bytearray()
    o = len(head) + TX * TY + 8 * len(blobs)
    body = bytearray()
    for t in sorted(blobs):
        index += struct.pack("<ii", o + len(body), len(blobs[t]))
        body += blobs[t]
    data = head + states.tobytes() + bytes(index) + bytes(body)
    path = os.path.join(out, "lakes-glo90.bin")
    with open(path, "wb") as f:
        f.write(data)
    sha = hashlib.sha256(data).hexdigest()
    info = {"file": "lakes-glo90.bin", "bytes": len(data), "sha256": sha, "lakes": nl, "vertices": nv, "raw_bytes": nraw,
            "tiles_with_lakes": len(blobs), "tiles_covered": int((states != 255).sum())}
    with open(os.path.join(out, "lakes-glo90.json"), "w", encoding="utf8") as f:
        json.dump(info, f, indent=1)
    log(json.dumps(info))


if __name__ == "__main__":
    main()
