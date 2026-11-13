"""
Water disagreement atlas: where Earth's water sources disagree with the Copernicus DEM GLO-90 water body mask (WBM).

The WBM (tools/fetch_glo90_wbm.py) is on GLO-90's own 3" grid and was used by Copernicus to edit the heights over water,
so it agrees with the heights the world is built on. Earth's water comes from other sources: the sea from GSHHG's
shorelines (coasts.bin, islands.bin), lakes from Natural Earth (terrain.bin, lakes.bin, outlets.bin), rivers from
HydroRIVERS lines with a width, glaciers from RGI 6.2. Where they disagree with the WBM the world has water against
its heights (sea cut into mountains: the fjords near the Balmaceda glacier, Patagonia) or misses water.

Checked per 30" cell (~0.9 km; the WBM's 3" pixels counted in it):
  sea_on_land       Earth sea (outside GSHHG land), WBM land in 80% or more of the cell
  land_on_sea       Earth land (inside GSHHG land, not an Earth lake), WBM ocean in 80% or more
  lake_on_land      Earth lake (Natural Earth, as baked: reservoirs dropped, outlet tongues trimmed), WBM land 80%+
  lake_missing      Earth land, WBM lake in 80% or more of the cell
  glacier_in_water  RGI glacier over half the cell (glaciers.bin) where Earth has sea or lake
and per WBM river or lake island (land enclosed by river or lake water in the WBM, 0.25 km² or more, inside one tile):
  island_lost       Earth water (a river's width along its HydroRIVERS line, a lake, the sea) over half the island

Neighbouring flagged cells are one site. Each site has its area, how much of it is "core" (all eight neighbours flagged:
not a shoreline offset), the mean GLO-90 height of its WBM land (how tall the walls are), the source that decided it,
and a score to rank by. South of 60°S (Antarctica, no GSHHG land) is skipped.

    .venv/Scripts/python -I tools/water_atlas.py [--workers 16] [--only lat0,lon0,lat1,lon1]

Writes preview/water-atlas/: sites.json (all sites, ranked), islands.json, summary.txt, overview.png, and a cache of the
global layers (cache/*.npy, reused when present).
"""
import argparse, json, math, os, struct, sys, time
from multiprocessing import Pool

import brotli
import numpy as np
import scipy.ndimage as ndi
import shapefile
import tifffile
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RAW = os.environ.get("EARTH_RAWDATA") or os.path.join(ROOT, "rawdata")  # a worktree reads the main checkout's rawdata
DEM = os.path.join(RAW, "copernicus-glo90")
WBM = os.path.join(RAW, "copernicus-glo90-wbm")
OUTDIR = os.path.join(ROOT, "preview", "water-atlas")
CACHE = os.path.join(OUTDIR, "cache")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

RES = 120                 # atlas cells per degree (30")
W, H = 360 * RES, 180 * RES
SOUTH = -60.0             # no GSHHG land south of this (Antarctica is L5/L6): skipped
SHARE = 0.8               # a cell disagrees when the WBM has the other thing in this share of it or more
GLACIER = 0.5             # glacier cover that counts
RIVER_MIN_Q = 100.0       # m³/s: the game's default RiverMinDischarge
ISLAND_MIN_KM2 = 0.25
ISLAND_LOST = 0.5         # share of an island under Earth water
CLASSES = ["sea_on_land", "land_on_sea", "lake_on_land", "lake_missing", "glacier_in_water"]
T0 = time.time()


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


def cell_km2(lat):
    return (111.32 / RES) ** 2 * np.cos(np.radians(lat))


# ------------------------------------------------------------------------------------------- Earth's layers (global)

def build_sea():
    """uint8 30" raster: 1 where Earth has sea (outside every GSHHG L1 land polygon), north of SOUTH."""
    path = os.path.join(CACHE, "sea.npy")
    if os.path.exists(path):
        return np.load(path, mmap_mode="r")
    import bake_earth as be
    img = Image.new("1", (W, H), 0)
    draw = ImageDraw.Draw(img)
    r = shapefile.Reader(be.GSHHG_L1)
    n = 0
    for sr in r.iterShapes():
        pts = sr.points
        if len(pts) >= 3:
            draw.polygon([((x + 180) * RES, (90 - y) * RES) for x, y in pts], fill=1)
            n += 1
    # GSHHG lakes (L2) that the 2' grid has as sea for the most part are sea in the game (coast_rings): the Caspian, the
    # IJsselmeer, coastal lagoons
    flags = be.read_terrain_flags()
    h2, w2 = flags.shape
    l2 = shapefile.Reader(be.GSHHG_L1.replace("_L1", "_L2"))
    rings = [sr.points for sr in l2.iterShapes() if len(sr.points) >= 3]
    ids = Image.new("I", (w2, h2), 0)
    d2 = ImageDraw.Draw(ids)
    k2 = w2 / 360
    for i, pts in enumerate(rings, start=1):
        d2.polygon([((x + 180) * k2, (90 - y) * k2) for x, y in pts], fill=i)
    ids = np.array(ids).ravel()
    ocean = (flags.ravel() & 1) > 0
    cells = np.bincount(ids, minlength=len(rings) + 1)
    seac = np.bincount(ids[ocean], minlength=len(rings) + 1)
    kept = [i for i in range(1, len(rings) + 1) if cells[i] > 0 and seac[i] >= 0.5 * cells[i]]
    for i in kept:
        draw.polygon([((x + 180) * RES, (90 - y) * RES) for x, y in rings[i - 1]], fill=0)
    log(f"sea: {len(kept)} GSHHG lakes the grid has as sea")
    land = np.array(img, dtype=bool)
    sea = (~land).astype(np.uint8)
    sea[int((90 - SOUTH) * RES):] = 0
    np.save(path, sea)
    log(f"sea: {n} GSHHG land polygons rasterised")
    return np.load(path, mmap_mode="r")


def build_lakes():
    """int32 1' raster of Natural Earth lake ids as the bake keeps them (reservoirs dropped, outlet tongues trimmed),
    and the lake names."""
    path, names_path = os.path.join(CACHE, "lakes1m.npy"), os.path.join(CACHE, "lake-names.json")
    if os.path.exists(path) and os.path.exists(names_path):
        return np.load(path, mmap_mode="r"), json.load(open(names_path, encoding="utf8"))
    import bake_earth as be
    S, _ = be.load_etopo()
    lake_id, lakes = be.rasterize_lakes(S.shape)
    before = lake_id > 0
    be.drop_reservoir_lakes(lake_id, lakes, S)
    # the reservoirs the bake leaves out (a world as of 1318): lake_missing there is on purpose
    np.save(os.path.join(CACHE, "reservoirs1m.npy"), before & (lake_id == 0))
    del S
    tongue, _ = be.outlet_tongues(lake_id, lakes)
    lake_id[tongue] = 0
    np.save(path, lake_id.astype(np.int32))
    names = [None] + [(l or {}).get("name") or "" for l in lakes[1:]]
    json.dump(names, open(names_path, "w", encoding="utf8"))
    log(f"lakes: {int((lake_id > 0).sum())} 1' cells")
    return np.load(path, mmap_mode="r"), names


def terminal_lake_cells(size):
    """Flat 2' grid indices that lakes.bin makes lake: the terminal lakes Earth fills (decision 0005: the Aral Sea at
    53 m, Lop Nur, the Hamun lakes, Lake Chad as in 1963, and natural salt lakes filled as in wet years)."""
    import brotli
    import struct
    with open(os.path.join(ROOT, "earthdata", "lakes.bin"), "rb") as f:
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
    log(f"lakes.bin: {len(idx)} grid cells of terminal lakes")
    return idx


def build_grid_lakes():
    """bool 2' raster: the game grid's own lake flag (terrain.bin): the Caspian and other lakes not from Natural Earth's
    polygons as the bake rasterised them."""
    path = os.path.join(CACHE, "gridlakes2m-terminal.npy")
    if not os.path.exists(path):
        import bake_earth as be
        lakes = (be.read_terrain_flags() & 2) > 0
        lakes.ravel()[terminal_lake_cells(lakes.size)] = True
        np.save(path, lakes)
    return np.load(path, mmap_mode="r")


def build_glaciers():
    """uint8 30" raster of RGI glacier cover (0..255) from earthdata/glaciers.bin."""
    path = os.path.join(CACHE, "glaciers.npy")
    if os.path.exists(path):
        return np.load(path, mmap_mode="r")
    with open(os.path.join(ROOT, "earthdata", "glaciers.bin"), "rb") as f:
        b = f.read()
    assert b[:4] == b"EMGL"
    _, w, h, t, count = struct.unpack_from("<iiiii", b, 4)
    assert (w, h) == (W, H)
    g = np.zeros((H, W), np.uint8)
    tx = w // t
    for k in range(count):
        off, ln = struct.unpack_from("<ii", b, 24 + 8 * k)
        if ln:
            r, c = divmod(k, tx)
            g[r * t:(r + 1) * t, c * t:(c + 1) * t] = np.frombuffer(brotli.decompress(b[off:off + ln]), np.uint8).reshape(t, t)
    np.save(path, g)
    return np.load(path, mmap_mode="r")


def build_rivers():
    """HydroRIVERS reaches of RIVER_MIN_Q or more, as segments per 1° tile: {(lat0, lon0): [(x0, y0, x1, y1, q), ...]}."""
    path = os.path.join(CACHE, "rivers.npy")
    if os.path.exists(path):
        return np.load(path)
    rshp = os.path.join(RAW, "hydrorivers", "HydroRIVERS_v10_shp", "HydroRIVERS_v10.shp")
    r = shapefile.Reader(rshp)
    segs = []
    for sr, rec in zip(r.iterShapes(), r.iterRecords(fields=["DIS_AV_CMS"])):
        q = rec[0]
        if q < RIVER_MIN_Q:
            continue
        p = np.asarray(sr.points, np.float64)
        if len(p) >= 2:
            segs.append(np.column_stack([p[:-1], p[1:], np.full(len(p) - 1, q)]))
    a = np.concatenate(segs).astype(np.float64)
    np.save(path, a)
    log(f"rivers: {len(a)} segments of reaches {RIVER_MIN_Q:.0f} m³/s and more")
    return a


# ------------------------------------------------------------------------------------------- per tile

G = {}


def init_worker():
    G["sea"] = np.load(os.path.join(CACHE, "sea.npy"), mmap_mode="r")
    G["lakes"] = np.load(os.path.join(CACHE, "lakes1m.npy"), mmap_mode="r")
    G["glaciers"] = np.load(os.path.join(CACHE, "glaciers.npy"), mmap_mode="r")
    G["gridlakes"] = np.load(os.path.join(CACHE, "gridlakes2m-terminal.npy"), mmap_mode="r")
    G["rivers"] = np.load(os.path.join(CACHE, "rivers.npy"))
    rv = G["rivers"]
    G["rbox"] = (np.minimum(rv[:, 0], rv[:, 2]), np.maximum(rv[:, 0], rv[:, 2]), np.minimum(rv[:, 1], rv[:, 3]), np.maximum(rv[:, 1], rv[:, 3]))


def tile_origin(name):
    # Copernicus_DSM_COG_30_N60_00_E006_00_DEM: the tile's south-west corner
    parts = name.split("_")
    lat = int(parts[4][1:]) * (1 if parts[4][0] == "N" else -1)
    lon = int(parts[6][1:]) * (1 if parts[6][0] == "E" else -1)
    return lat, lon


def block_sum(a, ry, rx):
    h, w = a.shape
    return a.reshape(h // ry, ry, w // rx, rx).sum(axis=(1, 3))


def do_tile(name):
    lat0, lon0 = tile_origin(name)
    if lat0 < SOUTH:
        return None
    wbm_path = os.path.join(WBM, name[:-4] + "_WBM.tif")
    if not os.path.exists(wbm_path):
        return None
    wbm = tifffile.imread(wbm_path)
    dem = tifffile.imread(os.path.join(DEM, name + ".tif"))
    hp, wp = wbm.shape                      # 1200 rows; 1200, 800, 600, 400, 240 or 120 columns by latitude
    land = wbm == 0
    # columns repeated up to a multiple of the cells (800 -> 2400: 20 per cell)
    f = math.lcm(wp, RES) // wp
    ry, rx = hp // RES, wp * f // RES
    rep = (lambda a: np.repeat(a, f, axis=1)) if f > 1 else (lambda a: a)
    n_land = block_sum(rep(land.astype(np.int32)), ry, rx)
    n_ocean = block_sum(rep((wbm == 1).astype(np.int32)), ry, rx)
    n_lake = block_sum(rep((wbm == 2).astype(np.int32)), ry, rx)
    n = ry * rx
    hsum = block_sum(rep(np.where(land, dem, 0).astype(np.float64)), ry, rx)
    hmean = np.where(n_land > 0, hsum / np.maximum(n_land, 1), 0)
    d64 = rep(dem.astype(np.float64))
    m1 = block_sum(d64, ry, rx) / n
    hstd = np.sqrt(np.maximum(block_sum(d64 * d64, ry, rx) / n - m1 * m1, 0))

    r0, c0 = int(round((90 - (lat0 + 1)) * RES)), int(round((lon0 + 180) * RES)) % W
    sea = np.asarray(G["sea"][r0:r0 + RES, c0:c0 + RES], bool)
    lid1 = np.asarray(G["lakes"][r0 // 2:r0 // 2 + RES // 2, c0 // 2:c0 // 2 + RES // 2])
    lid = np.repeat(np.repeat(lid1, 2, 0), 2, 1)
    g2 = np.asarray(G["gridlakes"][r0 // 4:r0 // 4 + RES // 4, c0 // 4:c0 // 4 + RES // 4])
    lake = (lid > 0) | np.repeat(np.repeat(g2, 4, 0), 4, 1)
    glac = np.asarray(G["glaciers"][r0:r0 + RES, c0:c0 + RES]) >= GLACIER * 255

    bits = np.zeros((RES, RES), np.uint8)
    bits |= (sea & (n_land >= SHARE * n)).astype(np.uint8) << 0
    bits |= (~sea & ~lake & (n_ocean >= SHARE * n)).astype(np.uint8) << 1
    bits |= (lake & (n_land >= SHARE * n)).astype(np.uint8) << 2
    bits |= (~sea & ~lake & (n_lake >= SHARE * n)).astype(np.uint8) << 3
    bits |= (glac & (sea | lake)).astype(np.uint8) << 4

    # islands in rivers and lakes (WBM land enclosed by river or lake water, not touching the tile's edge)
    islands = []
    lab, k = ndi.label(land, structure=np.ones((3, 3), bool))
    if k:
        edge = np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))
        sizes = np.bincount(lab.ravel(), minlength=k + 1)
        km_y = 1.0 / 1200 * 111.32
        km_x = 1.0 / wp * 111.32 * math.cos(math.radians(lat0 + 0.5))
        px_km2 = km_y * km_x
        cand = [i for i in np.nonzero(sizes * px_km2 >= ISLAND_MIN_KM2)[0] if i and i not in set(edge.tolist())]
        if cand:
            # Earth water at 3": the sea and lakes (their cells), the rivers (width along the line)
            ew = (sea | lake)[(np.arange(hp) * RES // hp)[:, None], (np.arange(wp) * RES // wp)[None, :]]
            img = Image.new("1", (wp, hp), 0)
            dr = ImageDraw.Draw(img)
            rv = G["rivers"]
            x0b, x1b, y0b, y1b = G["rbox"]
            sel = np.nonzero((x1b >= lon0 - 0.05) & (x0b <= lon0 + 1.05) & (y1b >= lat0 - 0.05) & (y0b <= lat0 + 1.05))[0]
            for s in sel:
                x0, y0, x1, y1, q = rv[s]
                wpx = max(1, int(round(8 * math.sqrt(q) / (km_x * 1000))))
                dr.line([((x0 - lon0) * wp, (lat0 + 1 - y0) * 1200), ((x1 - lon0) * wp, (lat0 + 1 - y1) * 1200)], fill=1, width=wpx)
            ew |= np.array(img, bool)
            objs = ndi.find_objects(lab)
            for i in cand:
                sl = objs[i - 1]
                pad = (slice(max(sl[0].start - 1, 0), sl[0].stop + 1), slice(max(sl[1].start - 1, 0), sl[1].stop + 1))
                m = lab[pad] == i
                ring = ndi.binary_dilation(m, structure=np.ones((3, 3), bool)) & ~m
                rw = wbm[pad][ring]
                inland = np.count_nonzero((rw == 2) | (rw == 3))
                if inland < 0.5 * len(rw):
                    continue  # a sea island: GSHHG's job, counted by the cell classes
                cov = float(ew[pad][m].mean())
                yy, xx = np.nonzero(m)
                islands.append({"lat": round(lat0 + 1 - (pad[0].start + yy.mean() + 0.5) / 1200, 4),
                                "lon": round(lon0 + (pad[1].start + xx.mean() + 0.5) / wp, 4),
                                "km2": round(float(m.sum()) * px_km2, 2), "earth_water": round(cov, 2),
                                "in": "river" if np.count_nonzero(rw == 3) >= np.count_nonzero(rw == 2) else "lake",
                                "height_m": round(float(dem[pad][m].mean()), 1)})
    return r0, c0, bits, hmean.astype(np.float32), hstd.astype(np.float16), islands


# ------------------------------------------------------------------------------------------- sites

def polar(lat, lon):
    """Greenland and the high Arctic: ice shelves and floating glacier tongues, which the game's ice rules handle."""
    return lat > 75 or (lat > 59 and -75 < lon < -10)


def sites_of(bits, hmean, hstd, lakes1m, names, bit):
    m = (bits & (1 << bit)) > 0
    lab, k = ndi.label(m, structure=np.ones((3, 3), bool))
    if not k:
        return []
    core = ndi.binary_erosion(m, structure=np.ones((3, 3), bool), border_value=0)
    sizes = np.bincount(lab.ravel(), minlength=k + 1)
    objs = ndi.find_objects(lab)
    out = []
    for i, sl in enumerate(objs, start=1):
        if sizes[i] < 2:
            continue  # one cell: under ~1 km², a shoreline offset
        mm = lab[sl] == i
        rows = np.arange(sl[0].start, sl[0].stop)
        lat_r = 90 - (rows + 0.5) / RES
        ka = cell_km2(lat_r)[:, None] * mm
        area = float(ka.sum())
        if area < 1.0:
            continue
        core_km2 = float((ka * core[sl]).sum())
        yy, xx = np.nonzero(mm)
        clat = 90 - (sl[0].start + yy.mean() + 0.5) / RES
        clon = (sl[1].start + xx.mean() + 0.5) / RES - 180
        h = float(hmean[sl][mm].mean())
        rough = float(np.asarray(hstd[sl], np.float32)[mm].mean())
        s = {"class": CLASSES[bit], "lat": round(clat, 4), "lon": round(clon, 4), "km2": round(area, 1),
             "core_km2": round(core_km2, 1), "height_m": round(h, 1), "rough_m": round(rough, 1),
             "polar": bool(polar(clat, clon)),
             "box": [round(90 - sl[0].stop / RES, 4), round(sl[1].start / RES - 180, 4), round(90 - sl[0].start / RES, 4), round(sl[1].stop / RES - 180, 4)]}
        if bit in (0, 1):
            s["source"] = "GSHHG shoreline"
        elif bit == 2:
            ids = np.asarray(lakes1m[sl[0].start // 2:(sl[0].stop + 1) // 2, sl[1].start // 2:(sl[1].stop + 1) // 2]).ravel()
            ids = ids[ids > 0]
            lid = int(np.bincount(ids).argmax()) if len(ids) else 0
            s["source"] = "Natural Earth lake " + (names[lid] or f"#{lid}") if lid else "Natural Earth lake"
        elif bit == 3:
            rv = np.asarray(RESERVOIRS[sl[0].start // 2:(sl[0].stop + 1) // 2, sl[1].start // 2:(sl[1].stop + 1) // 2])
            la0, lo0, la1, lo1 = s["box"]
            hl = (HL_RES[:, 1] >= la0) & (HL_RES[:, 1] <= la1) & (HL_RES[:, 0] >= lo0) & (HL_RES[:, 0] <= lo1)
            res_km2 = float(HL_RES[hl, 2].sum())
            s["source"] = ("reservoir, left out on purpose (1318)" if rv.mean() >= 0.3 or res_km2 >= 0.3 * area
                           else "no Earth lake (WBM lake)")
        else:
            s["source"] = "GSHHG/Natural Earth water over an RGI glacier"
        # rank: area that is not shoreline offset, weighted by how far the water is from the heights
        if s["source"].startswith("reservoir"):
            core_km2 = 0.0
        s["score"] = round(core_km2 * (1 + max(h, 0) / 50) if bit in (0, 2, 4) else core_km2, 1)
        out.append(s)
    out.sort(key=lambda s: -s["score"])
    return out


def overview(bits, path):
    """A 0.1° overview: grey land/sea from nothing, coloured where sites are."""
    f = 12
    small = np.zeros((H // f, W // f, 3), np.uint8) + 245
    colours = [(220, 40, 40), (40, 90, 220), (240, 140, 0), (0, 160, 160), (150, 0, 200)]
    for bit, col in enumerate(colours):
        m = ((bits & (1 << bit)) > 0).reshape(H // f, f, W // f, f).any(axis=(1, 3))
        small[m] = col
    Image.fromarray(small).save(path)


# ------------------------------------------------------------------------------------------- courses (game level)

STEP_KM = 0.1             # sampling along a course
COURSE_MIN_Q = 500.0      # rivers checked: the WBM shows rivers from about 2 pixels (~180 m) wide; 8·√500 = 179 m
SLACK_M = 90.0            # one WBM pixel: a bed edge within this of water counts as on it
DRY_KM = 1.0              # a stretch with no WBM water within the tolerance this long or longer is reported


def read_reaches(path):
    """rivers.bin's format (EMRV): [(q, next, lon array, lat array)]."""
    with open(path, "rb") as f:
        b = f.read()
    assert b[:4] == b"EMRV", path
    version, count = struct.unpack_from("<ii", b, 4)
    raw = brotli.decompress(b[12:])
    out, o = [], 0
    for _ in range(count):
        q = struct.unpack_from("<f", raw, o)[0]
        o += 4
        nxt = -1
        if version >= 2:
            nxt = struct.unpack_from("<i", raw, o)[0]
            o += 4
        n = struct.unpack_from("<i", raw, o)[0]
        o += 4
        rec = 11 if version >= 2 else 10
        a = np.frombuffer(raw, np.uint8, n * rec, o).reshape(n, rec)
        o += n * rec
        lonlat = a[:, :8].copy().view(np.float32).reshape(n, 2)
        out.append((q, nxt, lonlat[:, 0].astype(np.float64), lonlat[:, 1].astype(np.float64)))
    return out


def densify(lat, lon):
    """Points every STEP_KM along a polyline, with their distance along it (km)."""
    kx = np.cos(np.radians(lat))
    d = np.hypot(np.diff(lat) * 111.32, np.diff(lon) * 111.32 * kx[:-1])
    pts_lat, pts_lon, along = [lat[:1]], [lon[:1]], [np.zeros(1)]
    s0 = 0.0
    for i, L in enumerate(d):
        k = max(int(L / STEP_KM), 1)
        t = np.arange(1, k + 1) / k
        pts_lat.append(lat[i] + t * (lat[i + 1] - lat[i]))
        pts_lon.append(lon[i] + t * (lon[i + 1] - lon[i]))
        along.append(s0 + t * L)
        s0 += L
    return np.concatenate(pts_lat), np.concatenate(pts_lon), np.concatenate(along)


def water_distance(lats, lons):
    """For each point: the distance (m) to the nearest WBM water (ocean, lake, river). Points without a WBM tile (open sea)
    are 0, points in a tile without water inf."""
    out = np.zeros(len(lats))
    tl = np.floor(lats).astype(int)
    tc = np.floor(lons).astype(int)
    key = tl * 1000 + tc
    order = np.argsort(key, kind="stable")
    ks = key[order]
    starts = np.r_[0, np.nonzero(np.diff(ks))[0] + 1]
    ends_ = np.r_[starts[1:], len(ks)]
    for s, e in zip(starts, ends_):
        idx = order[s:e]
        la0, lo0 = int(tl[idx[0]]), int(tc[idx[0]])
        name = f"Copernicus_DSM_COG_30_{'N' if la0 >= 0 else 'S'}{abs(la0):02d}_00_{'E' if lo0 >= 0 else 'W'}{abs(lo0):03d}_00_WBM.tif"
        p = os.path.join(WBM, name)
        if not os.path.exists(p):
            continue
        wbm = tifffile.imread(p)
        hp, wp = wbm.shape
        water = wbm > 0
        rr = np.clip(((la0 + 1 - lats[idx]) * hp).astype(int), 0, hp - 1)
        cc = np.clip(((lons[idx] - lo0) * wp).astype(int), 0, wp - 1)
        px_m = 111320.0 / hp
        # distance (m) to the nearest water pixel, with the pixels' aspect
        dist = ndi.distance_transform_edt(~water, sampling=(px_m, 111320.0 * math.cos(math.radians(la0 + 0.5)) / wp)) if water.any() else None
        out[idx] = np.inf if dist is None else dist[rr, cc]
    return out


def dry_stretches(along, wet):
    """(start km, end km) of the runs without water nearby, DRY_KM or longer."""
    runs, start = [], None
    for i, w in enumerate(wet):
        if not w and start is None:
            start = i
        if (w or i == len(wet) - 1) and start is not None:
            end = i if w else i
            if along[end] - along[start] >= DRY_KM:
                runs.append((float(along[start]), float(along[end])))
            start = None
    return runs


def check_courses(items, label, mpb=None):
    """items: [(id, q, half_width_m, lat array, lon array, extra dict)]. Returns the reports, worst first."""
    lats, lons, tols, owner, alongs = [], [], [], [], []
    for n, (_, q, hw, la, lo, _) in enumerate(items):
        pl, po, al = densify(la, lo)
        lats.append(pl); lons.append(po); alongs.append(al)
        tols.append(np.full(len(pl), hw + SLACK_M))
        owner.append(np.full(len(pl), n))
    if not items:
        return []
    L, O = np.concatenate(lats), np.concatenate(lons)
    dist = water_distance(L, O)
    wet = dist <= np.concatenate(tols)
    centre_wet = dist <= SLACK_M
    log(f"{label}: {len(items)} courses, {len(L)} points every {STEP_KM * 1000:.0f} m: {100 * (1 - wet.mean()):.2f}% with the bed off WBM water, "
        f"{100 * (1 - centre_wet.mean()):.2f}% with the centre line on land")
    ow = np.concatenate(owner)
    out = []
    bounds = np.r_[0, np.cumsum([len(x) for x in lats])]
    for n, (cid, q, hw, la, lo, extra) in enumerate(items):
        w = wet[bounds[n]:bounds[n + 1]]
        cw = centre_wet[bounds[n]:bounds[n + 1]]
        al = alongs[n]
        runs = dry_stretches(al, w)
        centre_land_km = round(float((~cw).sum()) * STEP_KM, 2)
        if not runs:
            if centre_land_km >= DRY_KM:
                i = int(np.argmin(cw))
                out.append(dict(id=cid, q=round(float(q), 1), length_km=round(float(al[-1]), 2), dry_km=0.0, longest_dry_km=0.0,
                                dry_share=0.0, centre_land_km=centre_land_km,
                                at=[round(float(lats[n][i]), 4), round(float(lons[n][i]), 4)],
                                start=[round(float(la[0]), 4), round(float(lo[0]), 4)], **extra))
            continue
        worst = max(runs, key=lambda r: r[1] - r[0])
        i0 = int(np.searchsorted(al, worst[0]))
        i1 = int(np.searchsorted(al, worst[1]))
        pl, po = lats[n], lons[n]
        out.append(dict(id=cid, q=round(float(q), 1), length_km=round(float(al[-1]), 2),
                        dry_km=round(sum(r[1] - r[0] for r in runs), 2), longest_dry_km=round(worst[1] - worst[0], 2),
                        dry_share=round(1 - float(w.mean()), 3), centre_land_km=centre_land_km,
                        at=[round(float(pl[(i0 + i1) // 2]), 4), round(float(po[(i0 + i1) // 2]), 4)],
                        start=[round(float(la[0]), 4), round(float(lo[0]), 4)], **extra))
    out.sort(key=lambda r: (-r["dry_km"], -r["centre_land_km"]))
    return out


def tag_weaknesses():
    """Where the mask itself is the weak side (decision of 6 November 2026): each site in sites.json gets a "mask_note",
    and summary-tags.txt counts them. Salt flats and playas in dry climates are lakes in the mask; salt lakes there were
    lower when GLO-90 was flown (2011-2015) than their outlines; flat ground at sea level the mask has as sea is tidal
    flat or marsh; Greenland and the high Arctic have floating ice. Climate: earthdata/climate.bin's annual precipitation."""
    import bake_earth as be
    planes = be.read_climate_planes()
    p_year = planes[3]
    CH, CW = p_year.shape
    path = os.path.join(OUTDIR, "sites.json")
    sites = json.load(open(path, encoding="utf8"))
    counts = {}
    flags = be.read_terrain_flags()
    revived = np.zeros(flags.size, bool)
    revived[terminal_lake_cells(flags.size)] = True
    revived = revived.reshape(flags.shape)
    k2 = flags.shape[1] / 360
    # HydroLAKES dams (Lake_type 2 reservoir, 3 natural lake raised by a dam): a long reservoir's dam can lie well past
    # the part the mask flags, so the dam may be up to one reservoir length (√area) beyond the site's box
    hl = shapefile.Reader(os.path.join(RAW, "hydrolakes", "HydroLAKES_points_v10_shp", "HydroLAKES_points_v10.shp"),
                          encoding="latin1", encodingErrors="replace")
    dams = np.array([(r["Pour_long"], r["Pour_lat"], r["Lake_area"], r["Lake_type"]) for r in
                     hl.iterRecords(fields=["Lake_type", "Lake_area", "Pour_long", "Pour_lat"]) if r["Lake_type"] in (2, 3)])

    def dammed(s_):
        la0, lo0, la1, lo1 = s_["box"]
        m = np.sqrt(dams[:, 2]) / 111.32
        mk = m / max(math.cos(math.radians(s_["lat"])), 0.05)
        near = ((dams[:, 1] >= la0 - m) & (dams[:, 1] <= la1 + m) & (dams[:, 0] >= lo0 - mk) & (dams[:, 0] <= lo1 + mk)
                & (dams[:, 2] >= 0.3 * s_["km2"]))
        if not near.any():
            return 0
        return 2 if (dams[near, 3] == 2).any() else 3
    for s_ in sites:
        r = min(max(int((90 - s_["lat"]) / 180 * CH), 0), CH - 1)
        c = min(max(int((s_["lon"] + 180) / 360 * CW), 0), CW - 1)
        p = int(p_year[r, c])
        s_["p_year_mm"] = p
        note = ""
        if s_["polar"]:
            note = "high Arctic: floating ice, the mask is unreliable"
        elif s_["source"].startswith("reservoir"):
            note = "reservoir: left out on purpose (no modern dams)"
        elif s_["class"] == "lake_on_land" and revived[
                int((90 - s_["box"][2]) * k2):int((90 - s_["box"][0]) * k2) + 1,
                int((s_["box"][1] + 180) * k2):int((s_["box"][3] + 180) * k2) + 1].mean() >= 0.3:
            note = "terminal lake Earth fills on purpose (lakes.bin: revived as before 1960, or filled as in wet years; decision 0005)"
        elif s_["class"] == "lake_missing" and dammed(s_) == 2:
            note = "reservoir: left out on purpose (no modern dams)"
        elif s_["class"] == "lake_missing" and dammed(s_) == 3:
            note = "natural lake raised by a dam: its raised part left out on purpose"
        elif s_["class"] == "lake_missing" and p < 250 and s_["rough_m"] < 1:
            note = "salt flat or playa in a dry climate: the mask counts it as a lake"
        elif s_["class"] == "lake_on_land" and p < 400 and s_["rough_m"] < 2:
            note = "salt or shallow lake in a dry climate: lower in 2011-2015 than its outline"
        elif s_["class"] in ("land_on_sea", "sea_on_land") and abs(s_["height_m"]) < 2 and s_["rough_m"] < 1:
            note = "tidal flat, marsh or lagoon at sea level"
        s_["mask_note"] = note
        if note:
            k = (s_["class"], note)
            n, km = counts.get(k, (0, 0.0))
            counts[k] = (n + 1, km + s_["km2"])
    json.dump(sites, open(path, "w", encoding="utf8"), ensure_ascii=False, indent=0)
    with open(os.path.join(OUTDIR, "summary-tags.txt"), "w", encoding="utf8") as f:
        for c in CLASSES:
            tot = [s_ for s_ in sites if s_["class"] == c]
            f.write(f"{c}: {len(tot)} sites, {sum(s_['km2'] for s_ in tot):.0f} km²\n")
            for (cc, note), (n, km) in sorted(counts.items()):
                if cc == c:
                    f.write(f"  {n:6d} sites, {km:9.0f} km²  {note}\n")
    log("tagged: " + os.path.join(OUTDIR, "summary-tags.txt"))


def run_courses(a):
    reports = {}
    if a.channels:
        d = json.load(open(a.channels, encoding="utf8"))
        mpb = d["metres_per_block"]
        items = []
        for i, ch in enumerate(d["channels"]):
            c = np.asarray(ch["course"], np.float64)
            if len(c) < 2:
                continue
            items.append((i, ch["q"], ch["half_width"] * mpb, c[:, 0], c[:, 1],
                          {"name": ch["name"], "ship": ch["ship"], "canal": ch["canal"], "half_width_m": round(ch["half_width"] * mpb)}))
        reports["channels"] = check_courses(items, "sea channels", mpb)
        # the pieces that may cut land (channel rules 3: bars only; files without "land": every piece, as rules 1-2 cut)
        cut = []
        for i, ch in enumerate(d["channels"]):
            c = np.asarray(ch["course"], np.float64)
            if len(c) < 2 or ch["canal"]:
                continue
            land = ch.get("land") or [1] * (len(c) - 1)
            k = 0
            while k < len(land):
                if not land[k]:
                    k += 1
                    continue
                j = k
                while j < len(land) and land[j]:
                    j += 1
                part = c[k:j + 1]
                cut.append((i, ch["q"], ch["half_width"] * mpb, part[:, 0], part[:, 1], {"name": ch["name"], "ship": ch["ship"]}))
                k = j
        reports["channel_cuts"] = check_courses(cut, "sea channel pieces that may cut land", mpb)
    for label, path in (("rivers", a.rivers), ("rivers_base", a.rivers_base)):
        if not path:
            continue
        reaches = read_reaches(path)
        led = None
        if label == "rivers" and a.ends and os.path.exists(a.ends):
            led = np.frombuffer(open(a.ends, "rb").read(), np.uint8)
        items = []
        for i, (q, nxt, lo, la) in enumerate(reaches):
            if q < COURSE_MIN_Q or len(lo) < 2:
                continue
            extra = {}
            if led is not None and i < len(led) and led[i] != 255:
                extra["end"] = ["water", "into_lake", "led", "faded", "antimeridian"][led[i]] if led[i] < 5 else str(led[i])
            items.append((i, q, 4 * math.sqrt(q), la, lo, extra))
        reports[label] = check_courses(items, f"{label} ({os.path.basename(path)}, {COURSE_MIN_Q:.0f} m³/s and more)")
    os.makedirs(OUTDIR, exist_ok=True)
    json.dump(reports, open(os.path.join(OUTDIR, "courses.json"), "w", encoding="utf8"), ensure_ascii=False, indent=0)
    with open(os.path.join(OUTDIR, "courses.txt"), "w", encoding="utf8") as f:
        for k, rs in reports.items():
            dry = [r for r in rs if r["dry_km"] > 0]
            f.write(f"{k}: {len(dry)} whose bed (half width + {SLACK_M:.0f} m) leaves WBM water for {DRY_KM} km or more, "
                    f"{sum(r['dry_km'] for r in dry):.0f} km in all; {len(rs)} with {DRY_KM} km or more of centre line on land "
                    f"or a dry stretch, {sum(r['centre_land_km'] for r in rs):.0f} km of centre line on land\n")
            for r in rs[:60]:
                f.write(f"  {r['dry_km']:7.1f} km dry (longest {r['longest_dry_km']:.1f}), centre on land {r['centre_land_km']:6.1f} km, of {r['length_km']:.0f}  Q {r['q']:8.0f}  at {r['at'][0]:.4f},{r['at'][1]:.4f}"
                        f"  {r.get('name', '')} {r.get('end', '')} {'ship' if r.get('ship') else ''} #{r['id']}\n")
    log("done: " + os.path.join(OUTDIR, "courses.txt"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--workers", type=int, default=16)
    ap.add_argument("--only", help="lat0,lon0,lat1,lon1: only tiles in this box (a quick look)")
    ap.add_argument("--courses", action="store_true", help="check courses instead: --channels, --rivers, --rivers-base")
    ap.add_argument("--channels", help="channels.json from EarthMapTests' channels mode")
    ap.add_argument("--rivers", help="the world's river courses (game-rivers.bin from the channels mode, or a rivers.bin)")
    ap.add_argument("--ends", help="game-river-ends.bin from the channels mode")
    ap.add_argument("--rivers-base", help="another reach file to compare (rivers.bin: HydroRIVERS' courses)")
    ap.add_argument("--tag", action="store_true", help="tag the sites where the mask is the weak side (sites.json, summary-tags.txt)")
    a = ap.parse_args()
    if a.tag:
        return tag_weaknesses()
    if a.courses:
        return run_courses(a)
    os.makedirs(CACHE, exist_ok=True)
    build_sea()
    lakes1m, names = build_lakes()
    global RESERVOIRS
    RESERVOIRS = np.load(os.path.join(CACHE, "reservoirs1m.npy"), mmap_mode="r")
    # HydroLAKES reservoirs (type 2): pour point and area
    global HL_RES
    hl = shapefile.Reader(os.path.join(RAW, "hydrolakes", "HydroLAKES_points_v10_shp", "HydroLAKES_points_v10.shp"),
                          encoding="latin1", encodingErrors="replace")
    HL_RES = np.array([(r["Pour_long"], r["Pour_lat"], r["Lake_area"]) for r in
                       hl.iterRecords(fields=["Lake_type", "Lake_area", "Pour_long", "Pour_lat"]) if r["Lake_type"] == 2])
    build_glaciers()
    build_grid_lakes()
    build_rivers()
    tiles = sorted(f[:-4] for f in os.listdir(DEM) if f.endswith("_DEM.tif"))
    if a.only:
        la0, lo0, la1, lo1 = map(float, a.only.split(","))
        tiles = [t for t in tiles if la0 - 1 < tile_origin(t)[0] <= la1 and lo0 - 1 < tile_origin(t)[1] <= lo1]
    log(f"{len(tiles)} tiles")
    bits = np.zeros((H, W), np.uint8)
    hmean = np.zeros((H, W), np.float32)
    hstd = np.zeros((H, W), np.float16)
    islands, done, no_wbm = [], 0, 0
    with Pool(a.workers, initializer=init_worker) as pool:
        for res in pool.imap_unordered(do_tile, tiles, chunksize=8):
            done += 1
            if res is None:
                no_wbm += 1
            else:
                r0, c0, b, hm, hs, isl = res
                bits[r0:r0 + RES, c0:c0 + RES] = b
                hmean[r0:r0 + RES, c0:c0 + RES] = hm
                hstd[r0:r0 + RES, c0:c0 + RES] = hs
                islands += isl
            if done % 2000 == 0:
                log(f"{done}/{len(tiles)} tiles")
    log(f"tiles done ({no_wbm} without a mask or south of {SOUTH}°)")
    sites = []
    for bit in range(len(CLASSES)):
        s = sites_of(bits, hmean, hstd, lakes1m, names, bit)
        log(f"{CLASSES[bit]}: {len(s)} sites, {sum(x['km2'] for x in s):.0f} km² ({sum(x['core_km2'] for x in s):.0f} core)")
        sites += s
    lost = sorted([i for i in islands if i["earth_water"] >= ISLAND_LOST], key=lambda i: -i["km2"])
    tag = "" if not a.only else "-" + a.only.replace(",", "_")
    json.dump(sites, open(os.path.join(OUTDIR, f"sites{tag}.json"), "w", encoding="utf8"), ensure_ascii=False, indent=0)
    json.dump({"all": len(islands), "lost": lost}, open(os.path.join(OUTDIR, f"islands{tag}.json"), "w", encoding="utf8"), ensure_ascii=False, indent=0)
    with open(os.path.join(OUTDIR, f"summary{tag}.txt"), "w", encoding="utf8") as f:
        for c in CLASSES:
            cs = [s for s in sites if s["class"] == c and not s["polar"]]
            ps = [s for s in sites if s["class"] == c and s["polar"]]
            f.write(f"{c}: {len(cs)} sites, {sum(s['km2'] for s in cs):.0f} km², {sum(s['core_km2'] for s in cs):.0f} km² core "
                    f"(and {len(ps)} in Greenland and the high Arctic, {sum(s['km2'] for s in ps):.0f} km²)\n")
            for s in cs[:40]:
                f.write(f"  {s['score']:9.1f}  {s['lat']:9.4f},{s['lon']:10.4f}  {s['km2']:8.1f} km² ({s['core_km2']:.1f} core)  "
                        f"{s['height_m']:7.1f} m, rough {s['rough_m']:5.1f} m  {s['source']}\n")
        f.write(f"island_lost: {len(lost)} of {len(islands)} river and lake islands of {ISLAND_MIN_KM2} km² or more under Earth water\n")
        for i in lost[:40]:
            f.write(f"  {i['lat']:9.4f},{i['lon']:10.4f}  {i['km2']:8.2f} km²  in {i['in']}, {int(i['earth_water'] * 100)}% under Earth water\n")
    if not a.only:
        overview(bits, os.path.join(OUTDIR, "overview.png"))
    log("done: " + os.path.join(OUTDIR, f"summary{tag}.txt"))


if __name__ == "__main__":
    main()
