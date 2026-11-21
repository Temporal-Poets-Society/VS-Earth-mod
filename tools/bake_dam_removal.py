"""Bake dams-glo90-2.bin: modern dams taken out of GLO-90 for worlds on full GLO-90 heights (Earth 0053 item 19, dam
rules 2 of 0063 rule 3; docs/research/dam-removal-2026-11-07.md). Dam rules 1 (dams-glo90-1.bin) are this tool at
75fae1f.

Earth's worlds are set around 1318. Items 17 and 18 leave the reservoirs' water out, but GLO-90 keeps what the dams made:
the dam walls, the reservoirs' flat water surfaces as land, and the river courses (rivers-glo90.bin, levels from GLO-90)
stepping down at the dams (Riga HPP 14 -> 0 m within 1.4 km, Plavinas 66 -> 32 m within 1.6 km). For each HydroLAKES
reservoir (type 2) found from its dam (its pour point) as tools/bake_river_outlines.py finds it, but rising up to RISE_MAX
above its level by the dam while it has less than its HydroLAKES area (Three Gorges' upper reaches), this bake

1. finds the dam wall: ground within DAM_REACH of the pour point that is within DAM_WIDTH of both the reservoir and the
   water or ground below the dam (at least TAIL_DROP lower than the reservoir), and no more than DAM_CREST above the
   reservoir's level (higher ground there is the valley's side), and takes it down to the valley's cross-section a short
   way below it: per wall pixel the highest ground at VALLEY_BELOW downstream, square to the wall's line, plus BANK (the
   riverbed across the river, the valley's sides at its ends; where that is another reservoir, a cascade, its height
   above that reservoir's level set on the course's foot at the dam). Fixed in the fill where a course runs, a cap
   elsewhere. The second pass of 8 November filled the walls with the reservoirs' floors instead, and twice as many walls
   (122) stayed more than 100 m above the river below;
2. sets the river courses through the reservoirs (runs of course stations in a reservoir or on a dam) falling evenly by
   their length between knots: the level where the run comes in (the station above it, or where another run left it),
   at each dam the lowest level of the measured river within FOOT_REACH below it (course stations outside every
   reservoir and wall; a run that leaves its reservoir short of the dam is followed on to the station nearest the pour
   point; never below the ground just below the dam, less TAIL_DROP, where the river falls on steeply), and where a
   tributary's run ends the main river's new level. In a cascade (the next reservoir starts at the dam) the dam has no
   knot of its own: the course's old level there is the next reservoir's surface (Kakhovka's 26 m at DniproHES's foot
   inside its 14.5 m flat), so the run falls on to the next measured river below. No station stands above the ground
   GLO-90 has beside it (within the corridor's half width: old water, walls and land, not water outside the zone) less
   LEVEL_BANK: where the even fall would, the level there is a knot too (the fall bends under the old reservoirs' flats).
   Never higher than before or than another run set it, never rising downstream. Stations on the step's ramp below a
   dam (within FOOT_REACH, above the foot) take the foot's level;
3. (off while FACE_REACH is 0) adds to each wall the dam's downstream face: ground below the reservoir's level but more
   than FACE_RISE above the water below the dam, within FACE_REACH downstream of the wall's line and FACE_SIDE beyond its
   ends, joined to it;
4. rebuilds the ground under the reservoirs and dams (the zone, and the corridors' ground below the walls): the courses
   and their corridors (rule 5) at their new level plus BANK and the walls fixed, between them and the ground just
   outside (shores, islands, the water below the dam) a smooth (harmonic) surface; where no course runs in a zone, the
   shore's height falling SIDE_SLOPE per metre, not below the foot, its walls capped as in 1. Never higher than GLO-90;
5. lays a river corridor along the course runs through each zone that reach a dam or carry at least CORRIDOR_MAIN of
   the largest flow of a run in their reservoir (the river through it: Jebel Aulia's White Nile, where no wall is found,
   leaves its old reservoir and comes back in many runs), not the tributaries, which keep their courses' channels. Its
   half width is the smaller of the river's going in and coming out: the median over WIDTH_REACH of the course of the
   mask's river water's half width (not lakes or other reservoirs), above the zone where the course comes in, and below
   it beyond FOOT_REACH (the tailwater basin below a dam: Kakhovka's 1.3 km), at most WIDTH_MAX_BY_FLOW times the root
   of the flow or WIDTH_MAX_FLOOR (where neither end has river water: WIDTH_BY_FLOW times it). In a zone and further than
   WALL_NEAR from a wall it is no wider than CORRIDOR_SHARE of the old water's width there (smoothed, a gorge's
   reservoir); within WALL_NEAR of a wall it keeps its full width (easing to the zone's cap by WALL_FAR). It lies on the
   mask's old water,
   the walls and the ground below the dam within WALL_NEAR of the wall (the tailrace's banks), and that ground comes
   down to it with the zone's (OUTDIR/corridors.npz): tools/bake_river_outlines.py adds it to the river outlines at the
   course's level, so the river keeps its width through a former reservoir and across its dam.

Usage: python -I tools/bake_dam_removal.py OUTDIR COURSES.bin --global [workers]
       python -I tools/bake_dam_removal.py OUTDIR COURSES.bin AREA [AREA ...]
  COURSES.bin: the GLO-90 course list a world gets, whole and in the mod's order (EarthData.RiverReaches: dam rules 2
  is for courses rules 4, DamRemovalData.CourseRules2; developed on courses rules 3's), either exported by the probe
  harness ("EMCX": key, reaches with flow, next reach, stations, levels, fixed) or an EMRV file (record version 2 or 3);
  the levels are keyed to it (DamRemovalData.KeyOf, worked out here for an EMRV file; DAMS_CHECK_KEY=1 checks an
  export's).
  --global: every reservoir. Pass 1 finds each one's region on a mosaic of 3 x 3 squares round its dam (5, 7, 9 while
  it reaches the edge), kept in OUTDIR/regions.pkl (delete it to find them again); pass 2 does the reservoirs in groups
  whose regions and dams lie within 2 x CLUSTER_GAP of each other (a cascade together), each on one mosaic of its
  squares; a square two groups touch keeps the lower height. About 12 minutes on 16 workers. DAMS_BOX=latS:latN:lonW:
  lonE[,...] keeps only the dams in those boxes (a test; it rewrites OUTDIR/hl_reservoirs.npy). DAMS_BIG_WORKERS sets
  how many groups on more than BIG_GROUP squares are done at a time (BIG_WORKERS; each takes up to ~8 GB).
  AREA: name:latS:latN:lonW:lonE in whole degrees; the reservoirs whose pour points lie inside are done together on one
  mosaic of those squares (a cascade must lie in one area), with a preview (before/after relief).
Writes OUTDIR/dams-glo90-2.bin, dams.tsv (per reservoir), corridor-runs.tsv (per corridor: flow, widths in and out,
levels) and reservoirs-dams.npz (the regions' river pixels per tile: copied as reservoirs.npz into
tools/bake_river_outlines.py's OUTDIR, it leaves the same reservoirs out) with corridors.npz beside it.
"""
import hashlib, math, os, struct, sys, time

import numpy as np
import tifffile
import brotli
from scipy import ndimage as ndi

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bake_lake_outlines import RAW, DEM, WBM, SOUTH, varints  # noqa: E402
from bake_river_outlines import tile_name, RES_SEED, RES_SEED_KM2, RES_WIDE  # noqa: E402
import glo90grid as gg  # noqa: E402

# dam rules 2 (0063 rule 3); 3 (0069 rule 9, EARTH_GDW=1): the same rules with Global Dam Watch's reservoirs HydroLAKES
# lacks; 4 (decision 0092, EA-0148): rules 3 on GLO-90's pixel-is-point grid (tools/glo90grid.py) and on courses rules 5's
# list. The file's header and name (dams-glo90-2.bin, dams-glo90-3.bin, dams-glo90-4.bin)
VERSION = (4 if gg.POINT else 3) if os.environ.get("EARTH_GDW", "0") == "1" else 2
PX = 1200                          # mosaic pixels per degree, both ways (columns resampled), as bake_river_outlines
RES_RISE = 6                        # half-metre steps of bake_river_outlines (3 m)
RISE_MAX = 60                       # and on, while the reservoir has less than its HydroLAKES area (30 m)
RISE_SHORT = 0.8                    # "less than its area": under this share of it
DAM_REACH = 2000.0                  # m from the pour point a dam wall is looked for
DAM_WIDTH = 300.0                   # m: a dam wall lies within this of both the reservoir and the water or ground below
VALLEY_BELOW = (300.0, 400.0, 500.0, 600.0)   # m below a wall where the valley's cross-section is taken for it
FACE_RISE = 5.0                     # m: a dam's downstream face is ground at least this far above the water below the dam,
FACE_REACH = 0.0                    # within this far downstream of the wall's line (0: rule 3 off. At 1000 m, 8 November's
                                    # global bake had 3,792 walls higher than without: where no course runs, a face's
                                    # nearest ground is the hillside beside it, and neither came down)
FACE_SIDE = 100.0                   # and this far beyond the wall's ends along its line
CORRIDOR_BEYOND = 3                 # pixels: a river corridor reaches this far out of the zone (to meet the river below)
WIDTH_REACH = 5000.0                # m of course beside a zone over which the river's width for its corridor is measured
                                    # (below a dam from FOOT_REACH on: the tailwater basin is not the river)
WIDTH_BY_FLOW = 3.0                 # m of half width per root of the flow (m3/s) where there is none to measure
WIDTH_MAX_BY_FLOW = 10.0            # and at most this many (Rīgas HES' run 7.6, Kakhovka's 6.4: a cap for backwater the
WIDTH_MAX_FLOOR = 186.0             # mask has as river: 532 m at 347 m3/s by Jupiá), but not below two of the mask's
                                    # pixels (a small river's mask is a pixel or two wide whatever its flow)
CORRIDOR_SHARE = 0.5                # in a zone, a corridor's half width is at most this share of the old water's there,
CORRIDOR_SMOOTH = 1000.0            # m: smoothed along the river over this length
WALL_NEAR = 1000.0                  # m: within this of a wall a corridor keeps its full width (no share of the old
WALL_FAR = 2000.0                   # water's), easing to the zone's cap by this; the ground below the dam within
                                    # WALL_NEAR of the wall (the tailrace's banks) may be corridor
LEVEL_BANK = 1.0                    # m: a course's level through a zone is at least this below the ground GLO-90 has
                                    # beside it (the reservoir's flat holds water to half a metre below its level by the
                                    # dam; at least BANK, so the corridor's floor is never above GLO-90)
CORRIDOR_MAIN = 0.5                 # a run with at least this share of the largest flow of a run in its reservoir has one
ZONE_GAP = 1                        # pixels: a reservoir's zone closes gaps (bridges) up to twice this wide
DAM_CREST = 30.0                   # m: a wall stands no higher above the water GLO-90 has by it (Three Gorges: 185 m on 155 m)
TAIL_DROP = 1.0                     # m: the water or ground below a dam is at least this much below the reservoir
FOOT_REACH = 2000.0                 # m below a dam (along the course) the foot of the river's step is looked for
JOIN_REACH = 5000.0                 # m a tributary's run that leaves the zone is followed to its main river's new level
BANK = 0.5                          # m: the rebuilt ground along a course stands this much above its water
SIDE_SLOPE = 0.1                    # where no course runs: the floor falls this much per metre from the shore
T0 = time.time()


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


# ------------------------------------------------------------------------------------------- inputs

EMRV_VERTEX = {2: np.dtype([("lon", "<f4"), ("lat", "<f4"), ("lev", "<i2"), ("fixed", "u1")]),
               3: np.dtype([("lon", "<f4"), ("lat", "<f4"), ("lev", "<i2"), ("fixed", "u1"), ("width", "u1")])}


def course_key(q, nxt, lon, lat, lev, start):
    """DamRemovalData.KeyOf: FNV-1a over the reach count, then per reach its flow's bits, next reach and station count,
    and per station its longitude's and latitude's bits and its level (as an unsigned 16-bit value), each as 4 bytes."""
    n = len(q)
    head = np.zeros((n, 3), "<u4")
    head[:, 0] = np.asarray(q, "<f4").view("<u4")
    head[:, 1] = np.asarray(nxt, np.int64).astype("<i4").view("<u4")
    head[:, 2] = np.diff(start).astype("<u4")
    st = np.zeros((len(lon), 3), "<u4")
    st[:, 0] = np.asarray(lon, "<f4").view("<u4")
    st[:, 1] = np.asarray(lat, "<f4").view("<u4")
    st[:, 2] = np.asarray(lev, np.int64).astype("<i2").view("<u2")
    parts = [np.array([n], "<u4").tobytes()]
    for i in range(n):
        parts.append(head[i].tobytes())
        parts.append(st[start[i]:start[i + 1]].tobytes())
    h, P, M = 14695981039346656037, 1099511628211, (1 << 64) - 1
    for x in b"".join(parts):
        h = ((h ^ x) * P) & M
    return h


def read_courses(path):
    """The course list the levels are keyed to: an export of the probe harness ("EMCX": the key, then per reach flow,
    next reach, stations, levels, fixed), or a full course list as an EMRV file (record version 2, or 3 with widths:
    float32 flow, int32 next reach, int32 count, per vertex float32 lon, lat, int16 level, uint8 fixed[, uint8 width],
    brotli-compressed), whose key is worked out here as the mod works it out (DamRemovalData.KeyOf)."""
    b = open(path, "rb").read()
    lons, lats, levs, fixs = [], [], [], []
    if b[:4] == b"EMRV":
        version, n = struct.unpack_from("<ii", b, 4)
        assert version in EMRV_VERTEX, f"{path}: EMRV record version {version}"
        raw, o, dt = brotli.decompress(b[12:]), 0, EMRV_VERTEX[version]
        key = None
    else:
        assert b[:4] == b"EMCX", f"{path}: neither EMCX nor EMRV"
        key, n = struct.unpack_from("<Qi", b, 4)
        raw, o, version = b, 16, 0
    q = np.zeros(n, np.float32)
    nxt = np.zeros(n, np.int64)
    start = np.zeros(n + 1, np.int64)
    for i in range(n):
        q[i], nxt[i], m = struct.unpack_from("<fii", raw, o)
        o += 12
        if version:
            a = np.frombuffer(raw, dt, m, o); o += m * dt.itemsize
            lons.append(a["lon"]); lats.append(a["lat"]); levs.append(a["lev"]); fixs.append(a["fixed"])
        else:
            lons.append(np.frombuffer(raw, "<f4", m, o)); o += 4 * m
            lats.append(np.frombuffer(raw, "<f4", m, o)); o += 4 * m
            levs.append(np.frombuffer(raw, "<i2", m, o)); o += 2 * m
            fixs.append(np.frombuffer(raw, "u1", m, o)); o += m
        start[i + 1] = start[i] + m
    lon, lat, lev = np.concatenate(lons), np.concatenate(lats), np.concatenate(levs)
    if key is None:
        key = course_key(q, nxt, lon, lat, lev, start)
    elif os.environ.get("DAMS_CHECK_KEY"):
        k2 = course_key(q, nxt, lon, lat, lev, start)
        log(f"courses: key worked out {k2:016x}, " + ("as exported" if k2 == key else f"NOT the exported {key:016x}"))
    C = {"key": key, "q": q, "next": nxt, "start": start, "lon": lon.astype(np.float64), "lat": lat.astype(np.float64),
         "lev": lev.astype(np.float64), "fixed": np.concatenate(fixs) > 0}
    C["reach"] = np.repeat(np.arange(n), np.diff(start))
    log(f"courses ({'EMRV record version %d' % version if version else 'EMCX'}): {n} reaches, {len(C['lon'])} stations, "
        f"key {key:016x}")
    return C


def read_dams(out):
    """The reservoirs (id, lon, lat, km2, elevation): HydroLAKES' (type 2), and with EARTH_GDW=1 (dam rules 3, decision
    0069 rule 9) Global Dam Watch's that HydroLAKES lacks (tools/reservoir_list.py)."""
    path = os.path.join(out, "hl_reservoirs.npy")
    if not os.path.exists(path):
        sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
        from reservoir_list import reservoirs
        np.save(path, reservoirs(RAW, (2,), SOUTH, log)[:, :5])
    return np.load(path)


def mosaic(lat_s, lat_n, lon_w, lon_e, rivers=False):
    """The mask's water (lake or river) and GLO-90 (m), north up, PX columns a degree, for the squares lat_s..lat_n-1,
    lon_w..lon_e-1; and the squares: (name, row, column, native width). With rivers, also the mask's river water alone
    and what is not ground (the sea, and squares without data)."""
    nr, nc = (lat_n - lat_s) * PX, (lon_e - lon_w) * PX
    water = np.zeros((nr, nc), bool)
    river = np.zeros((nr, nc), bool) if rivers else None
    notg = np.ones((nr, nc), bool) if rivers else None
    h = np.zeros((nr, nc), np.float32)
    tiles = []
    for i, lat in enumerate(range(lat_n - 1, lat_s - 1, -1)):
        for j, lon in enumerate(range(lon_w, lon_e)):
            name = tile_name(lat, lon)
            p = os.path.join(WBM, name)
            if not os.path.exists(p):
                continue
            w = tifffile.imread(p)
            hp, wp = w.shape
            d = tifffile.imread(os.path.join(DEM, name.replace("_WBM.tif", "_DEM.tif")))
            # on PX columns a degree (glo90grid.columns: on the pixel-is-point grid each takes the nearest cell, a tile's
            # last column or two the next square's first)
            we = de = None
            if (gg.column_index(PX, wp) >= wp).any():
                en = tile_name(lat, ((lon + 1 + 180) % 360) - 180)
                if os.path.exists(os.path.join(WBM, en)):
                    we = tifffile.imread(os.path.join(WBM, en))
                    de = tifffile.imread(os.path.join(DEM, en.replace("_WBM.tif", "_DEM.tif")))
                else:
                    we, de = np.ones((hp, 1), w.dtype), np.zeros((hp, 1), d.dtype)
            wc, dc = gg.columns(w, PX, we), gg.columns(d, PX, de)
            blk = (slice(i * PX, (i + 1) * PX), slice(j * PX, (j + 1) * PX))
            water[blk] = (wc == 2) | (wc == 3)
            if rivers:
                river[blk] = wc == 3
                notg[blk] = wc == 1
            h[blk] = dc
            tiles.append((name, i, j, wp, lat, lon))
    return (water, h, tiles, river, notg) if rivers else (water, h, tiles)


# ------------------------------------------------------------------------------------------- reservoirs

def grow(dam, water, q, px_km2, lat_n, lon_w):
    """bake_river_outlines.reservoir's region on this mosaic, rising on to RISE_MAX while short of the area: (stats, region).
    lat_n, lon_w: the north-west corner of pixel (0, 0)'s area (glo90grid: half a pixel north-west of the squares' corner
    on the pixel-is-point grid)."""
    hid, lon, lat, area, elev = dam
    n_r, n_c = water.shape
    coslat = max(math.cos(math.radians(lat)), 0.05)
    pr, pc = int((lat_n - lat) * PX), int((lon - lon_w) * PX)
    hr = int(3 + 0.05 * math.sqrt(area) / 111.32 * PX)
    hc = int(3 + 0.05 * math.sqrt(area) / (111.32 * coslat) * PX)
    win = (slice(max(pr - hr, 0), pr + hr + 1), slice(max(pc - hc, 0), pc + hc + 1))
    # the seed's labels on a box around the dam (a long reservoir's seed may reach beyond it: then on the whole mosaic)
    lv = seed = None
    for level in np.unique(q[win][water[win]])[::-1]:
        lab, _ = ndi.label(water & (q == level), structure=np.ones((3, 3), bool))
        ids = np.unique(lab[win])
        s = np.isin(lab, ids[ids > 0])
        km2 = float(s.sum(axis=1) @ px_km2)
        if km2 >= min(RES_SEED * area, RES_SEED_KM2):
            lv, seed = int(level), s
            break
    if lv is None:
        return {"result": "none"}, None
    if km2 > RES_WIDE * area:
        return {"result": "wide", "level": lv / 2, "km2": km2}, None
    region, step = seed, 0
    for step in range(0, RISE_MAX + 1):
        if km2 >= area or (step > RES_RISE and km2 >= RISE_SHORT * area):
            break
        lab, _ = ndi.label(water & (q >= lv - 1) & (q <= lv + step), structure=np.ones((3, 3), bool))
        ids = np.unique(lab[seed])
        g = np.isin(lab, ids[ids > 0])
        g_km2 = float(g.sum(axis=1) @ px_km2)
        if g_km2 > RES_WIDE * area:
            break
        region, km2 = g, g_km2
    edge = region[0].any() or region[-1].any() or region[:, 0].any() or region[:, -1].any()
    return {"result": "found", "level": lv / 2, "km2": km2, "rise": step / 2, "edge": edge, "pr": pr, "pc": pc}, region


# ------------------------------------------------------------------------------------------- one area

def reg_view(reg, shape):
    """A compact region (row, column of its box's corner, its box's pixels) on an array of shape: (the slices, the part
    of its pixels inside), or (None, None) when it lies outside."""
    r0, c0, sub = reg
    a0, a1 = max(r0, 0), min(r0 + sub.shape[0], shape[0])
    b0, b1 = max(c0, 0), min(c0 + sub.shape[1], shape[1])
    if a0 >= a1 or b0 >= b1:
        return None, None
    return (slice(a0, a1), slice(b0, b1)), sub[a0 - r0:a1 - r0, b0 - c0:b1 - c0]


def reg_in(reg, box, shape):
    """A compact region as a mask over box (two slices with starts and stops, of an array of shape)."""
    out = np.zeros((box[0].stop - box[0].start, box[1].stop - box[1].start), bool)
    sl, s = reg_view(reg, shape)
    if sl is None:
        return out
    a0, a1 = max(sl[0].start, box[0].start), min(sl[0].stop, box[0].stop)
    b0, b1 = max(sl[1].start, box[1].start), min(sl[1].stop, box[1].stop)
    if a0 < a1 and b0 < b1:
        out[a0 - box[0].start:a1 - box[0].start, b0 - box[1].start:b1 - box[1].start] = \
            s[a0 - sl[0].start:a1 - sl[0].start, b0 - sl[1].start:b1 - sl[1].start]
    return out


def grow_local(d, water, q, px_km2, lat_n, lon_w):
    """grow on a box round the dam (most reservoirs are small), twice as wide while the region reaches its edge, up to the
    whole mosaic: (stats with the dam's pixel, compact region or None)."""
    n_r, n_c = water.shape
    half = max(0.25, 3 * math.sqrt(d[3]) / 111.32)
    cl = max(math.cos(math.radians(d[2])), 0.05)
    while True:
        r0 = max(int((lat_n - d[2] - half) * PX), 0)
        r1 = min(int((lat_n - d[2] + half) * PX), n_r)
        c0 = max(int((d[1] - lon_w - half / cl) * PX), 0)
        c1 = min(int((d[1] - lon_w + half / cl) * PX), n_c)
        sb = (slice(r0, r1), slice(c0, c1))
        st, reg = grow(tuple(d), water[sb], q[sb], px_km2[sb[0]], lat_n - r0 / PX, lon_w + c0 / PX)
        if reg is None or not st["edge"] or (r0, r1, c0, c1) == (0, n_r, 0, n_c):
            break
        half *= 2
    if reg is None:
        return st, None
    st["pr"] += r0
    st["pc"] += c0
    ys = np.flatnonzero(reg.any(axis=1))
    xs = np.flatnonzero(reg.any(axis=0))
    return st, (r0 + int(ys[0]), c0 + int(xs[0]), reg[ys[0]:ys[-1] + 1, xs[0]:xs[-1] + 1].copy())


def area(name, lat_s, lat_n, lon_w, lon_e, dams, C, out, only=None, regions=None, previews=True):
    """One area: the reservoirs whose pour points lie in it (or those of only), on one mosaic of its squares. regions:
    hid -> (stats, compact region in world pixels or None), found beforehand (the global bake)."""
    log(f"{name}: squares {lat_s}..{lat_n} N, {lon_w}..{lon_e} E")
    water, h, tiles, river_m, notg = mosaic(lat_s, lat_n, lon_w, lon_e, rivers=True)
    h0 = h.copy()
    n_r, n_c = water.shape
    # pixel (r, c)'s area runs from (top_ - r/PX, left_ + c/PX): half a pixel north-west of the squares' corner on GLO-90's
    # pixel-is-point grid (glo90grid), where a pixel's centre is its cell's
    top_, left_ = gg.top(lat_n, PX), gg.left(lon_w, PX)
    rows_lat = top_ - (np.arange(n_r) + 0.5) / PX
    px_km2 = (111.32 / PX) ** 2 * np.cos(np.radians(rows_lat))
    lat_mid = (lat_s + lat_n) / 2
    dy = 111320.0 / PX
    dx = dy * math.cos(math.radians(lat_mid))
    sel = (dams[:, 2] >= lat_s) & (dams[:, 2] < lat_n) & (dams[:, 1] >= lon_w) & (dams[:, 1] < lon_e)
    if only is not None:
        sel &= np.isin(dams[:, 0].astype(np.int64), np.array(sorted(only), np.int64))
    q = np.round(h.astype(np.float64) * 2).astype(np.int32) if regions is None else None
    res = []
    for d in dams[sel]:
        if regions is None:
            st, reg = grow_local(d, water, q, px_km2, top_, left_)
        else:
            st, greg = regions[int(d[0])]
            st = dict(st)
            reg = None
            if greg is not None:
                reg = (greg[0] - round((90 - lat_n) * PX), greg[1] - round((lon_w + 180) * PX), greg[2])
                st["pr"], st["pc"] = int((top_ - d[2]) * PX), int((d[1] - left_) * PX)
        st.update(hid=int(d[0]), lon=d[1], lat=d[2], area=d[3], elev=d[4])
        res.append((st, reg))
    del q
    found = [(st, reg) for st, reg in res if reg is not None]
    log(f"{name}: {len(res)} reservoirs, {len(found)} found")
    if not found:
        return res, {}, {}, {}, {}, []
    # every reservoir's id on the mosaic (lower levels first, so a cascade's lower reservoir keeps the shared pixels)
    rid = np.full((n_r, n_c), -1, np.int32)
    found.sort(key=lambda t: -t[0]["level"])
    for i, (st, reg) in enumerate(found):
        sl, s = reg_view(reg, (n_r, n_c))
        if sl is not None:
            rid[sl][s] = i
    # bridges and causeways across a reservoir (GLO-90 is a surface model: a deck is a wall across the old valley, and the
    # mask's water stops at it): gaps of up to 2 x ZONE_GAP pixels in a reservoir are its zone too
    for i, (st, reg) in enumerate(found):
        sl, s = reg_view(reg, (n_r, n_c))
        if sl is None:
            continue
        bb = (slice(max(sl[0].start - 3, 0), min(sl[0].stop + 3, n_r)), slice(max(sl[1].start - 3, 0), min(sl[1].stop + 3, n_c)))
        rb = rid[bb]
        cl = ndi.binary_closing(np.pad(rb == i, 3), structure=np.ones((3, 3), bool), iterations=ZONE_GAP)[3:-3, 3:-3]
        rb[cl & (rb < 0)] = i
    zone = rid >= 0
    yy, xx = np.ogrid[:n_r, :n_c]
    dam_px = np.zeros((n_r, n_c), bool)
    tail_px = np.zeros((n_r, n_c), bool)          # the ground and water below a dam within WALL_NEAR of its wall
    for i, (st, reg) in enumerate(found):
        L = st["level"]
        pr, pc = st["pr"], st["pc"]
        rr = int(DAM_REACH / dy) + 2
        cr = int(DAM_REACH / dx) + 2
        box = (slice(max(pr - rr, 0), min(pr + rr + 1, n_r)), slice(max(pc - cr, 0), min(pc + cr + 1, n_c)))
        near = (((yy[box[0]] - pr) * dy) ** 2 + ((xx[:, box[1]] - pc) * dx) ** 2) <= DAM_REACH ** 2
        R = reg_in(reg, box, (n_r, n_c))
        # below the dam: water or ground lower than the reservoir (another reservoir's water too: a cascade)
        T = near & ~R & (h[box] <= L - TAIL_DROP)
        st["foot_px"] = int(T.sum())
        if not T.any() or not R.any():
            st["dam_px"] = 0
            continue
        dR = ndi.distance_transform_edt(~R, sampling=(dy, dx))
        dT = ndi.distance_transform_edt(~T, sampling=(dy, dx))
        D = near & ~zone[box] & ~T & (dR <= DAM_WIDTH) & (dT <= DAM_WIDTH) & (h[box] <= L + DAM_CREST)
        st["dam_px"] = int(D.sum())
        st["dam_top"] = float(h[box][D].max()) if D.any() else float("nan")
        st["dam_mean"] = float(h[box][D].mean()) if D.any() else float("nan")
        st["_D"] = (box, D)
        close = T & (dR <= DAM_WIDTH * 2)
        st["below"] = float(np.percentile(h[box][close if close.any() else T], 10))
        st["face_px"] = 0
        if D.sum() >= 2:
            # the wall's line (its longest axis) and 400 m beyond each end, for probes across it
            ry, rx = np.nonzero(D)
            my = (ry + box[0].start + 0.5) * -dy
            mx = (rx + box[1].start + 0.5) * dx
            cy, cx = my.mean(), mx.mean()
            w_, v_ = np.linalg.eigh(np.cov(np.vstack([mx - cx, my - cy])))
            ax_ = v_[:, -1]
            ty_, tx_ = np.nonzero(T)
            nrm = np.array([-ax_[1], ax_[0]])          # square to the wall, towards the water below
            if ((tx_ + box[1].start + 0.5) * dx - cx).mean() * nrm[0] + ((ty_ + box[0].start + 0.5) * -dy - cy).mean() * nrm[1] < 0:
                nrm = -nrm
            st["_ax"] = (cy, cx, nrm, ax_)
            # the wall takes the valley's cross-section from a short way below it (across the river the riverbed, at its
            # ends the valley sides): the ground VALLEY_BELOW downstream, square to its line, towards the water below
            r_abs, c_abs = ry + box[0].start, rx + box[1].start
            samples = []                  # per distance: the ground there, and the reservoir it lies in (-1: none)
            for s_ in VALLEY_BELOW:
                rr_ = np.clip(np.round(r_abs - s_ * nrm[1] / dy).astype(np.int64), 0, n_r - 1)
                cc_ = np.clip(np.round(c_abs + s_ * nrm[0] / dx).astype(np.int64), 0, n_c - 1)
                samples.append((h[rr_, cc_].astype(np.float32), rid[rr_, cc_]))
            st["_W"] = (r_abs, c_abs, samples)       # resolved once the courses' levels at the dams are known
            # the dam's downstream face: ground below the reservoir's level (so not wall by the rule above) that still stands
            # above the water below the dam, from the wall to FACE_REACH downstream, across the wall's span, joined to the
            # wall (an embankment's face is hundreds of metres wide: Nurek's falls 914 -> 776 m over 400 m below its crest)
            bmy = (np.arange(box[0].start, box[0].stop)[:, None] + 0.5) * -dy - cy
            bmx = (np.arange(box[1].start, box[1].stop)[None, :] + 0.5) * dx - cx
            along = bmx * nrm[0] + bmy * nrm[1]
            across = bmx * ax_[0] + bmy * ax_[1]
            ua = (mx - cx) * ax_[0] + (my - cy) * ax_[1]
            cand = (T & ~water[box] & (h[box] > st["below"] + FACE_RISE) & (along >= -DAM_WIDTH) & (along <= FACE_REACH)
                    & (across >= ua.min() - FACE_SIDE) & (across <= ua.max() + FACE_SIDE))
            if FACE_REACH > 0 and cand.any():
                lab_, _ = ndi.label(cand | D, structure=np.ones((3, 3), bool))
                keep_ = np.unique(lab_[D])
                face = np.isin(lab_, keep_[keep_ > 0]) & cand
                st["face_px"] = int(face.sum())
                st["_F"] = (box, face)
                st["face_top"] = float(h[box][face].max()) if face.any() else float("nan")
                D = D | face
            ext = np.abs(ua).max() + 400
            for k_, sgn in (("0", -1), ("1", 1)):
                ex, ey = cx + sgn * ext * ax_[0], cy + sgn * ext * ax_[1]
                st["dam_lat" + k_] = top_ + ey / dy / PX
                st["dam_lon" + k_] = left_ + ex / dx / PX
        dam_px[box] |= D
        if D.any():
            # dam rules 2: the tailrace's banks (ground below the dam by its wall) may be the river's corridor
            tail_px[box] |= T & (ndi.distance_transform_edt(~D, sampling=(dy, dx)) <= WALL_NEAR)
    U = zone | dam_px
    log(f"{name}: zone {int(zone.sum())} px, dam walls {int(dam_px.sum())} px")

    # --- course runs through the zone
    lon, lat = C["lon"], C["lat"]
    inbox = (lat >= lat_s) & (lat < lat_n) & (lon >= lon_w) & (lon < lon_e)
    sidx = np.flatnonzero(inbox)
    srow = np.clip(np.floor((top_ - lat[sidx]) * PX).astype(np.int64), 0, n_r - 1)
    scol = np.clip(np.floor((lon[sidx] - left_) * PX).astype(np.int64), 0, n_c - 1)
    in_u = np.zeros(len(lon), bool)
    in_u[sidx] = U[srow, scol]
    lab_of = np.full(len(lon), -2, np.int64)          # -2 outside, -1 a dam wall, else the reservoir
    lab_of[sidx] = np.where(zone[srow, scol], rid[srow, scol], np.where(dam_px[srow, scol], -1, -2))
    start, nxt, reach = C["start"], C["next"], C["reach"]
    lev = C["lev"]
    new = lev.copy()

    def down(s):
        """The station after s along the course (the next reach's first station, the same place as a reach's last)."""
        r = reach[s]
        if s + 1 < start[r + 1]:
            return s + 1
        nr_ = nxt[r]
        return -1 if nr_ < 0 else start[nr_]

    def up_main(s):
        """The station before s along its own reach, or -1 at the reach's head."""
        return s - 1 if s > start[reach[s]] else -1

    def dist(a, b):
        return math.hypot((lon[b] - lon[a]) * 111320 * math.cos(math.radians(lat[a])), (lat[b] - lat[a]) * 111320)

    # runs: maximal chains of stations in the zone, from a station whose predecessor (in its reach) is outside; the
    # largest rivers first, so a tributary's run ends where it meets its main river's
    zs = np.flatnonzero(in_u)
    fed = np.zeros(len(lon), bool)
    for s in zs:
        u = down(s)
        if u >= 0 and in_u[u] and u != s:
            fed[u] = True
    heads = [s for s in zs if not fed[s]]
    heads.sort(key=lambda s: (-C["q"][reach[s]], s))
    pour = [(st["lat"], st["lon"]) for st, _ in found]

    def near_dam(s, i):
        la, lo = pour[i]
        return math.hypot((lon[s] - lo) * 111320 * math.cos(math.radians(la)), (lat[s] - la) * 111320) <= 1.5 * DAM_REACH

    def no_lower(f, i):
        """A foot no lower than the ground just below dam i (where the river below falls on steeply, as below many dams
        in mountains, the lowest level within FOOT_REACH is far below the dam's own foot: Guatapé's 333 m)."""
        b_ = found[i][0].get("below")
        return f if b_ is None or not np.isfinite(b_) or f == math.inf else max(f, round(b_ - TAIL_DROP))
    runs = []
    seen = set()
    for hd in heads:
        if hd in seen:
            continue
        chain = []
        s = hd
        while s >= 0 and in_u[s] and s not in seen:
            seen.add(s)
            chain.append(s)
            s = down(s)
        runs.append((chain, s))           # s: the station after the run (-1 at a mouth), or one already in another run
    done = np.zeros(len(lon), bool)
    order = []
    pending = list(range(len(runs)))
    while pending:
        rest = []
        for k in pending:
            chain, after = runs[k]
            if after < 0 or not in_u[after] or done[after] or after in chain:
                order.append(k)
                for s in chain:
                    done[s] = True
            else:
                rest.append(k)
        if len(rest) == len(pending):
            order += rest
            break
        pending = rest

    # --- each run's river width (its corridor's half width, rule 5) and the ground GLO-90 has beside its stations
    riv_out = river_m & ~zone

    def half_width(s_, step, skip=0.0):
        """The mask's river's half width (m) beside a zone: the median over the course's stations from s_ on (step: down or
        up_main) while outside the zones, from skip to skip + WIDTH_REACH along it, of the river water's half width by each
        (lakes and other reservoirs are not the river's width; below a dam, the tailwater basin within FOOT_REACH is not
        either: Kakhovka's 1.3 km); None where the river has no water there."""
        vals, run_ = [], 0.0
        while s_ >= 0 and not in_u[s_] and run_ <= skip + WIDTH_REACH:
            r_, c_ = int((top_ - lat[s_]) * PX), int((lon[s_] - left_) * PX)
            if not (0 <= r_ < n_r and 0 <= c_ < n_c):
                break
            if run_ >= skip:
                a0, a1, b0, b1 = max(r_ - 20, 0), min(r_ + 21, n_r), max(c_ - 20, 0), min(c_ + 21, n_c)
                win = riv_out[a0:a1, b0:b1]
                near = (slice(max(r_ - 3 - a0, 0), r_ + 4 - a0), slice(max(c_ - 3 - b0, 0), c_ + 4 - b0))
                if win[near].any():
                    e = ndi.distance_transform_edt(np.pad(win, 1), sampling=(dy, dx))[1:-1, 1:-1]
                    vals.append(float(e[near].max()))
            nx = step(s_)
            if nx < 0 or nx == s_:
                break
            run_ += dist(s_, nx)
            s_ = nx
        return float(np.median(vals)) if vals else None
    run_w = []                            # per run: (half width m, flow, width in, width out, "mask" or "flow")
    for chain, after in runs:
        q_ = float(C["q"][reach[np.asarray(chain)]].max())
        w_in, w_out = half_width(up_main(chain[0]), up_main), half_width(after, down, FOOT_REACH)
        if w_in is None and w_out is None:
            w_, src = WIDTH_BY_FLOW * math.sqrt(max(q_, 0.0)), "flow"
        else:
            w_, src = min(x for x in (w_in, w_out) if x is not None), "mask"
        run_w.append((min(w_, max(WIDTH_MAX_BY_FLOW * math.sqrt(max(q_, 0.0)), WIDTH_MAX_FLOOR)), q_, w_in, w_out, src))
    # the ground beside each station (within its run's half width and a pixel): land, the old water and the walls, not water
    # outside the zone (the river below the dam is not its bank) nor the sea; its level stays LEVEL_BANK below the lowest
    ground = ~notg & ~(water & ~U)
    cap_of = {}
    for k, (chain, after) in enumerate(runs):
        R_ = max(run_w[k][0], 2 * min(dy, dx)) + max(dy, dx)
        ry, rx = int(math.ceil(R_ / dy)), int(math.ceil(R_ / dx))
        oy, ox = np.ogrid[-ry:ry + 1, -rx:rx + 1]
        disc = (oy * dy) ** 2 + (ox * dx) ** 2 <= R_ ** 2
        for s in chain:
            r_, c_ = int((top_ - lat[s]) * PX), int((lon[s] - left_) * PX)
            a0, a1, b0, b1 = max(r_ - ry, 0), min(r_ + ry + 1, n_r), max(c_ - rx, 0), min(c_ + rx + 1, n_c)
            m_ = disc[a0 - r_ + ry:a1 - r_ + ry, b0 - c_ + rx:b1 - c_ + rx] & ground[a0:a1, b0:b1]
            if m_.any():
                cap_of[s] = float(h0[a0:a1, b0:b1][m_].min()) - LEVEL_BANK
    del ground

    lowered_ramps = tails_joined = dams_ahead = capped_runs = 0
    knots_log = []
    run_lv = {}                           # run -> (level where it comes in, at its end, knots from the ground beside)
    dam_runs = set()                      # the runs that reach a dam (the rivers through the dams; rule 5's corridors)
    foot_of = {}                          # reservoir -> the course's new level at the foot of its dam
    for k in order:
        chain, after = runs[k]
        cum = [0.0]
        for a, b in zip(chain, chain[1:]):
            cum.append(cum[-1] + dist(a, b))
        p_in = up_main(chain[0])
        e_in = p_in if p_in >= 0 else chain[0]
        top = new[e_in] if done[e_in] else lev[e_in]     # a run below another's end starts where that one left it
        knots = [(0.0, top)]
        foot_end = None
        extra = []                        # stations beyond the zone on the way to a tributary's main river
        crossed = []                      # (station index, reservoir) of each dam the run crosses
        cur = -2                          # the reservoir this run is in (a wall keeps the one above it)
        for j, s in enumerate(chain):
            here = lab_of[s]
            if here >= 0:
                cur = here
            if cur < 0:
                continue
            nxt_s = chain[j + 1] if j + 1 < len(chain) else after
            nxt_lab = lab_of[nxt_s] if nxt_s >= 0 else -2
            if nxt_lab == cur or nxt_lab == -1:
                continue
            # leaving reservoir cur downstream (into the water below its dam, or the next reservoir): a dam if it is there
            # (at the run's end the dam is looked for along the course below, as "ahead")
            if j + 1 == len(chain) or not near_dam(s, cur):
                continue
            # the foot: the lowest level of the measured river within FOOT_REACH below (stations outside every reservoir and
            # wall). In a cascade there is none (the next reservoir starts at the dam, and the course's old level there is
            # that reservoir's surface: Kakhovka's 26 m at DniproHES's foot): the run falls on to the next measured river
            f, walked, t = math.inf, 0.0, s
            ramp = []
            while True:
                u = down(t)
                if u < 0:
                    break
                walked += dist(t, u)
                if walked > FOOT_REACH:
                    break
                if not in_u[u]:
                    f = min(f, new[u] if done[u] else lev[u])
                    ramp.append(u)
                t = u
            f = no_lower(f, cur)
            dam_runs.add(k)
            crossed.append((j, cur))
            if f < math.inf:
                knots.append((cum[j], f))
                foot_of[cur] = min(foot_of.get(cur, math.inf), f)
                if j + 1 == len(chain):
                    foot_end = (f, ramp)
        ahead = None
        if foot_end is None and cur >= 0 and after >= 0 and not in_u[after]:
            # the run's end: the dam is the station nearest the pour point, here or on along the course below (the run
            # may leave its reservoir before the dam: water by the dam at other levels, locks, islands)
            la, lo = pour[cur]

            def to_pour(s_):
                return math.hypot((lon[s_] - lo) * 111320 * math.cos(math.radians(la)), (lat[s_] - la) * 111320)
            walked, t, u, way, best = 0.0, chain[-1], after, [], (to_pour(chain[-1]), 0)
            while u >= 0 and walked <= 2 * DAM_REACH and (not in_u[u] or lab_of[u] in (cur, -1)):
                walked += dist(t, u)
                way.append((u, cum[-1] + walked))
                dd = to_pour(u)
                if dd < best[0]:
                    best = (dd, len(way))
                t, u = u, down(u)
            if best[0] <= 1.5 * DAM_REACH:
                ds_ = way[best[1] - 1][0] if best[1] > 0 else chain[-1]
                f, walked2, t, ramp = math.inf, 0.0, ds_, []
                while True:
                    u = down(t)
                    if u < 0:
                        break
                    walked2 += dist(t, u)
                    if walked2 > FOOT_REACH:
                        break
                    if not in_u[u]:               # the measured river (not another reservoir's surface)
                        f = min(f, new[u] if done[u] else lev[u])
                        ramp.append(u)
                    t = u
                f = no_lower(f, cur)
                if os.environ.get("DAMS_DEBUG") and len(chain) >= 20:
                    log(f"    ahead: dam {best[0]:.0f} m from the pour point at {lat[ds_]:.4f},{lon[ds_]:.4f} "
                        f"({best[1]} stations on), foot {f}; below: "
                        + ", ".join(f"{lat[u]:.4f},{lon[u]:.4f} {lev[u]:.0f}{'/' + str(new[u]) if done[u] else ''}"
                                    for u in [down(ds_)] + ramp[:8] if u >= 0))
                if f < math.inf:
                    ahead = (way[:best[1]], (way[best[1] - 1][1] if best[1] > 0 else cum[-1], f), ramp)
                    foot_of[cur] = min(foot_of.get(cur, math.inf), f)
        if ahead is not None:
            extra, kn, ramp = ahead
            knots.append(kn)
            foot_end = (kn[1], ramp)
            dams_ahead += len(extra) > 0
            dam_runs.add(k)
        elif after >= 0 and in_u[after] and done[after] and after not in chain:
            knots.append((cum[-1] + dist(chain[-1], after), new[after]))
        elif foot_end is None and after >= 0:
            # out of the zone, not at a dam (a tributary's course leaving its arm of the reservoir before it meets the
            # main river): on to the next station already set within JOIN_REACH, if any; else the level where it leaves
            walked, t, u, hit, way = 0.0, chain[-1], after, None, []
            while u >= 0 and walked <= JOIN_REACH:
                walked += dist(t, u)
                if done[u] and u not in chain:
                    hit = (cum[-1] + walked, new[u])
                    break
                way.append((u, cum[-1] + walked))
                t, u = u, down(u)
            knots.append(hit if hit is not None else (cum[-1] + dist(chain[-1], after), lev[after]))
            if hit is not None:
                extra = way
                tails_joined += 1
        # levels between the knots, by length; never above the level before, never rising downstream; and never above the
        # ground GLO-90 has beside a station less LEVEL_BANK: where the even fall would be, that is a knot too (pulled under
        # the old reservoir's flat, the fall bends there: Kakhovka's 13.5 m below DniproHES)
        ks = sorted((float(x), float(y)) for x, y in knots)
        stations = chain + [u for u, _ in extra]
        xs_st = np.array(cum + [x for _, x in extra])
        capv = np.array([cap_of.get(s, math.inf) for s in chain] + [math.inf] * len(extra))
        n_capk = 0
        while True:
            xs_ = np.array([x for x, _ in ks])
            ys_ = np.minimum.accumulate(np.array([y for _, y in ks]))
            vals = np.interp(xs_st, xs_, ys_)
            over = vals - capv
            j_ = int(np.argmax(over))
            if not over[j_] > 0.05 or n_capk >= 200:
                break
            x_, y_ = float(xs_st[j_]), float(capv[j_])
            ks = sorted([kn for kn in ks if kn[0] != x_] + [(x_, min([y_] + [y for x, y in ks if x == x_]))])
            n_capk += 1
        capped_runs += n_capk > 0
        prev = math.inf
        for s, v, c_ in zip(stations, vals, capv):
            if not C["fixed"][s]:
                v = min(v, lev[s], prev, new[s] if done[s] else math.inf)   # never above another run's level here
                new[s] = min(round(v), math.floor(c_)) if c_ < math.inf else round(v)   # whole metres, under the cap
            prev = new[s]
            done[s] = True
        if foot_end is not None:
            f, ramp = foot_end
            for u in ramp:
                if lev[u] > f and not C["fixed"][u]:
                    new[u] = min(new[u], f)
                    lowered_ramps += 1
        for j, i in crossed:
            # a cascade's dam: the foot is where the run's fall leaves it (the walls' cross-section rule reads it)
            foot_of[i] = min(foot_of.get(i, math.inf), float(new[chain[min(j + 1, len(chain) - 1)]]))
        run_lv[k] = (float(new[chain[0]]), float(new[chain[-1]]), n_capk)
        knots_log.append((len(chain), [(round(x / 1000, 1), round(y, 1)) for x, y in zip(xs_, ys_)]))
        if os.environ.get("DAMS_DEBUG") and len(chain) >= 20:
            e = chain[-1]
            log(f"    run {len(chain)}: Q {C['q'][reach[chain[0]]]:.0f}, {lat[chain[0]]:.4f},{lon[chain[0]]:.4f} -> {lat[e]:.4f},{lon[e]:.4f}; "
                f"after {after} lab {lab_of[after] if after >= 0 else None} in_u {in_u[after] if after >= 0 else None} "
                f"done {done[after] if after >= 0 else None}; levels {lev[chain[0]]:.0f}..{lev[e]:.0f} -> {new[chain[0]]:.0f}..{new[e]:.0f}")
    changed = np.flatnonzero(new < lev)
    log(f"{name}: {len(runs)} course runs in the zone ({tails_joined} joined to their main river beyond it, "
        f"{dams_ahead} reaching their dam beyond it), "
        f"{len(changed)} stations lowered ({lowered_ramps} on ramps below dams)")
    for nst, kn in sorted(knots_log, key=lambda t: -t[0])[:6]:
        log(f"  run of {nst} stations, knots (km, m): {kn}")

    # --- the ground: pins along the courses in the zone, blended to the ground outside
    pin = np.full((n_r, n_c), np.nan, np.float32)
    for s in sidx:
        if not in_u[s]:
            continue
        u = down(s)
        b = u if u >= 0 else s
        r0, c0 = (top_ - lat[s]) * PX, (lon[s] - left_) * PX
        r1, c1 = (top_ - lat[b]) * PX, (lon[b] - left_) * PX
        m = max(2, int(2 * max(abs(r1 - r0), abs(c1 - c0))) + 1)
        t = np.linspace(0, 1, m)
        rr = np.clip((r0 + (r1 - r0) * t).astype(np.int64), 0, n_r - 1)
        cc = np.clip((c0 + (c1 - c0) * t).astype(np.int64), 0, n_c - 1)
        v = (new[s] + (new[b] - new[s]) * t + BANK).astype(np.float32)
        ok = U[rr, cc]
        rr, cc, v = rr[ok], cc[ok], v[ok]
        cur = pin[rr, cc]
        pin[rr, cc] = np.where(np.isnan(cur), v, np.minimum(cur, v))

    # --- rule 5: a river corridor along each run, as wide as the mask's river where the run comes in and goes out (the
    # smaller), within the mask's old water (the river ran under the reservoir), the dams and the tailrace's banks
    near_u = ndi.binary_dilation(U, structure=np.ones((3, 3), bool), iterations=CORRIDOR_BEYOND)
    may = ((water | U) & near_u) | tail_px     # not beyond the old water (a pixel more notched the gorges' sides)
    cor = np.zeros((n_r, n_c), bool)
    corl = np.full((n_r, n_c), np.inf, np.float32)
    # the rivers through the reservoirs: the runs that reach a dam, and in each reservoir those with at least CORRIDOR_MAIN of
    # the largest flow of a run in it (where no wall is found, or the river leaves the old reservoir and comes back)
    q_run = [float(C["q"][reach[chain[0]]]) for chain, _ in runs]
    res_of = [set(int(x) for x in lab_of[chain] if x >= 0) for chain, _ in runs]
    q_top = {}
    for q_, rs_ in zip(q_run, res_of):
        for r_ in rs_:
            q_top[r_] = max(q_top.get(r_, 0.0), q_)
    river_runs = dam_runs | {k for k, (q_, rs_) in enumerate(zip(q_run, res_of)) if any(q_ >= CORRIDOR_MAIN * q_top[r_] for r_ in rs_)}
    log(f"{name}: {len(river_runs)} of {len(runs)} runs are rivers through the reservoirs ({len(dam_runs)} reach a dam)")
    if os.environ.get("DAMS_DEBUG"):
        for k in range(len(runs)):
            ch_ = runs[k][0]
            log(f"    run {len(ch_)} st: Q {q_run[k]:.0f} of {max(q_top[r_] for r_ in res_of[k]) if res_of[k] else 0:.0f}, "
                f"{lat[ch_[0]]:.4f},{lon[ch_[0]]:.4f} -> {lat[ch_[-1]]:.4f},{lon[ch_[-1]]:.4f}; "
                f"{'dam' if k in dam_runs else 'flow' if k in river_runs else 'tributary'}")
    n_cor = 0
    run_rows = []                         # per corridor, for corridor-runs.tsv
    for k_run, (chain, after) in enumerate(runs):
        if k_run not in river_runs:
            # a tributary keeps its course's channel: the model draws an outline's water at its course's pools, and a
            # steep tributary's pools step two blocks at a time across a corridor as wide as its arm of the reservoir
            # (Kariba, 1:10: 120 two-block steps)
            continue
        w_, q_, w_in, w_out, src = run_w[k_run]
        if os.environ.get("DAMS_DEBUG") and len(chain) >= 20:
            log(f"    corridor run {len(chain)}: half widths in {w_in}, out {w_out}, by flow {WIDTH_BY_FLOW * math.sqrt(q_):.0f}, "
                f"at most {WIDTH_MAX_BY_FLOW * math.sqrt(q_):.0f}: {w_:.0f} ({src})")
        if src == "flow" and w_ < min(dy, dx) / 2:
            # no river water beside the zone to measure (another reservoir, a lake, the mosaic's edge), and a stream
            continue
        p_in = up_main(chain[0])
        sts = ([p_in] if p_in >= 0 else []) + chain + ([after] if after >= 0 else [])
        rr_l, cc_l, lv_l = [], [], []
        for j in range(len(sts) - 1):
            a, b = sts[j], sts[j + 1]
            r0, c0 = (top_ - lat[a]) * PX, (lon[a] - left_) * PX
            r1, c1 = (top_ - lat[b]) * PX, (lon[b] - left_) * PX
            m = max(2, int(2 * max(abs(r1 - r0), abs(c1 - c0))) + 1)
            t = np.linspace(0, 1, m)
            rr_l.append(np.clip((r0 + (r1 - r0) * t).astype(np.int64), 0, n_r - 1))
            cc_l.append(np.clip((c0 + (c1 - c0) * t).astype(np.int64), 0, n_c - 1))
            lv_l.append(new[a] + (new[b] - new[a]) * t)
        rr_, cc_, lv_ = np.concatenate(rr_l), np.concatenate(cc_l), np.concatenate(lv_l)
        hw_ = np.full(len(rr_), w_)
        # in the zone and away from the walls, no more than CORRIDOR_SHARE of the old water's (and dams') half width at the
        # course: the river ran in the valley's bottom, and its banks rise from its edges to the old shore (a corridor as
        # wide as a gorge's reservoir leaves the shore standing over it as a wall). Within WALL_NEAR of a wall its full
        # width: the old water narrows to the wall (and the wall's own width is not the river's), and a corridor capped
        # there was a neck across the dam (Rīgas HES: 137 m against 490-690 m above and 340-550 m below)
        mh = int(math.ceil(w_ / min(dy, dx))) + 2
        bxe = (slice(max(rr_.min() - mh - 40, 0), min(rr_.max() + mh + 41, n_r)), slice(max(cc_.min() - mh - 40, 0), min(cc_.max() + mh + 41, n_c)))
        wv = ndi.distance_transform_edt(np.pad(water[bxe] | U[bxe], 1), sampling=(dy, dx))[1:-1, 1:-1]
        ddam = ndi.distance_transform_edt(~dam_px[bxe], sampling=(dy, dx)) if dam_px[bxe].any() else None
        inz = zone[rr_, cc_]
        dd = ddam[rr_ - bxe[0].start, cc_ - bxe[1].start] if ddam is not None else np.full(len(rr_), np.inf)
        far = inz & (dd > WALL_NEAR)
        cap = np.where(far, CORRIDOR_SHARE * wv[rr_ - bxe[0].start, cc_ - bxe[1].start], np.inf)
        del wv, ddam
        # smoothed along the run (the narrowest within CORRIDOR_SMOOTH, then averaged over it), so the shores don't
        # follow every side bay and island of the old water; eased to the full width from WALL_FAR to WALL_NEAR
        k_ = max(3, int(CORRIDOR_SMOOTH / (min(dy, dx) / 2)) | 1)
        cm = np.where(far, ndi.minimum_filter1d(np.where(far, cap, 1e9), k_, mode="nearest"), 0.0)
        wsum = ndi.uniform_filter1d(far.astype(np.float64), k_, mode="nearest")
        avg = ndi.uniform_filter1d(cm, k_, mode="nearest") / np.maximum(wsum, 1e-9)
        ease = np.clip((WALL_FAR - dd) / (WALL_FAR - WALL_NEAR), 0.0, 1.0)
        cap = np.where(inz & (wsum > 0), avg + ease * np.maximum(hw_ - avg, 0.0), np.inf)
        hw_ = np.minimum(hw_, cap)
        mh = int(math.ceil(hw_.max() / min(dy, dx))) + 2
        bx = (slice(max(rr_.min() - mh, 0), min(rr_.max() + mh + 1, n_r)), slice(max(cc_.min() - mh, 0), min(cc_.max() + mh + 1, n_c)))
        centre = np.zeros((bx[0].stop - bx[0].start, bx[1].stop - bx[1].start), bool)
        hwb = np.zeros(centre.shape, np.float32)
        lvb = np.full(centre.shape, np.inf, np.float32)
        rl, cl_ = rr_ - bx[0].start, cc_ - bx[1].start
        centre[rl, cl_] = True
        np.maximum.at(hwb, (rl, cl_), hw_.astype(np.float32))
        np.minimum.at(lvb, (rl, cl_), lv_.astype(np.float32))
        dC, (ci_, cj_) = ndi.distance_transform_edt(~centre, sampling=(dy, dx), return_indices=True)
        run_cor = (dC <= hwb[ci_, cj_]) & may[bx]
        cor[bx] |= run_cor
        corl[bx] = np.where(run_cor, np.minimum(corl[bx], lvb[ci_, cj_]), corl[bx])
        n_cor += 1
        del dC, ci_, cj_
        # the half width it has (to its nearest edge from the course's line) in the zone and within WALL_NEAR of a wall
        ec = ndi.distance_transform_edt(np.pad(run_cor, 1), sampling=(dy, dx))[1:-1, 1:-1][rl, cl_]
        nw = dd <= WALL_NEAR
        run_rows.append(dict(n=len(chain), q=q_, w_in=w_in, w_out=w_out, w=w_, src=src,
                             hw_zone=float(np.median(ec[inz])) if inz.any() else math.nan,
                             hw_wall=float(np.percentile(ec[nw], 10)) if nw.any() else math.nan,
                             lv_in=run_lv[k_run][0], lv_end=run_lv[k_run][1], cap_knots=run_lv[k_run][2],
                             lat0=float(lat[chain[0]]), lon0=float(lon[chain[0]]), lat1=float(lat[chain[-1]]),
                             lon1=float(lon[chain[-1]]), dam=int(k_run in dam_runs),
                             res=",".join(str(found[x][0]["hid"]) for x in sorted(res_of[k_run]))))
        del ec
    # the corridors are the river's bottom: fixed at their level plus BANK as the courses' lines are, so the banks rise
    # from the corridor's edges (fixed on the line alone, the floor stood up to 20 m over the corridor's edges, and the
    # river cut down into it between walls); on the walls and the tailrace's banks too, which come down to it with the
    # zone (Uf: the ground the fill rebuilds)
    cpin = cor & (U | tail_px)
    pin[cpin] = np.fmin(pin[cpin], corl[cpin] + BANK)
    Uf = U | cpin
    log(f"{name}: river corridors along {n_cor} runs, {int(cor.sum())} px ({int((cpin & ~U).sum())} on the ground below a "
        f"wall); {capped_runs} runs bent under the ground beside them")
    del cpin, tail_px, may

    # the zone's pieces (each is filled on its own box and two pixels round it)
    comp, ncomp = ndi.label(Uf, structure=np.ones((3, 3), bool))
    pinned = np.zeros(ncomp + 1, bool)          # the zone pieces a course runs through
    pinned[np.unique(comp[~np.isnan(pin)])] = True
    pinned[0] = False
    # the walls, at the valley's height just below them (fixed in the fill where a course runs, a cap elsewhere); where
    # that is another reservoir (a cascade), its height above that reservoir's level set on the course's foot at the dam.
    # (Filled with the reservoirs' floors instead, as the second pass first did, twice as many walls stayed high: a wall's
    # end on the valley's side then only meets the hillside beside it)
    wall = pin
    res_level = np.array([st["level"] for st, _ in found], np.float32)
    for i, (st, _) in enumerate(found):
        if "_W" not in st:
            continue
        wr, wc, samples = st.pop("_W")
        ft = foot_of.get(i)
        vals = []
        for hs, zs in samples:
            inz = zs >= 0
            if ft is not None:
                v = np.where(inz, ft + BANK + np.maximum(0.0, hs - res_level[np.maximum(zs, 0)]), hs + BANK)
            else:
                v = np.where(inz, np.nan, hs + BANK)
            vals.append(np.where(zs == i, np.nan, v))              # never its own reservoir (the wrong side)
        # the highest of them: a wall comes down only where the whole stretch below it is low (a wall's end on the
        # valley's side whose line reaches the river below would otherwise leave a pit)
        vs = np.stack(vals)
        wv = np.where(np.isnan(vs).all(axis=0), np.nan, np.where(np.isnan(vs), -np.inf, vs).max(axis=0))
        ok_ = np.isfinite(wv) & (wv < h0[wr, wc])
        wall[wr[ok_], wc[ok_]] = np.fmin(wall[wr[ok_], wc[ok_]], wv[ok_])
        if os.environ.get("DAMS_DEBUG"):
            own_ = sum(int((zs == i).sum()) for _, zs in samples)
            other_ = sum(int(((zs >= 0) & (zs != i)).sum()) for _, zs in samples)
            log(f"    wall {st['hid']}: {len(wr)} px, foot {ft}, {int(ok_.sum())} taken down; samples in own reservoir {own_}, "
                f"in another {other_}, of {len(wr) * len(samples)}")
    del wall
    # zones without a course: the shore's height falling SIDE_SLOPE per metre, not below the foot of their dam
    foot = np.full(ncomp + 1, -1e9)
    for i, (st, reg) in enumerate(found):
        if "below" in st and np.isfinite(st["below"]):
            sl, s = reg_view(reg, (n_r, n_c))
            if sl is not None:
                c_ = np.unique(comp[sl][s])
                c_ = c_[c_ > 0]
                foot[c_] = np.maximum(foot[c_], st["below"])
    for ci, ob in enumerate(ndi.find_objects(comp), 1):
        if ob is None:
            continue
        bb = (slice(max(ob[0].start - 2, 0), min(ob[0].stop + 2, n_r)), slice(max(ob[1].start - 2, 0), min(ob[1].stop + 2, n_c)))
        Ub, hb, pinb = comp[bb] == ci, h0[bb], pin[bb]
        has_pin = Ub & ~np.isnan(pinb)
        edge = ndi.binary_dilation(Ub, structure=np.ones((3, 3), bool)) & ~Ub
        dB, (bi, bj) = ndi.distance_transform_edt(~edge, sampling=(dy, dx), return_indices=True)
        Bv = hb[bi, bj]
        del bi, bj
        F = hb.astype(np.float64)
        if pinned[ci]:
            dP, (pi_, pj) = ndi.distance_transform_edt(~has_pin, sampling=(dy, dx), return_indices=True)
            Pv = pinb[pi_, pj]
            del pi_, pj
            blend = Pv + (Bv - Pv) * (dP / np.maximum(dP + dB, 1e-6))
            F[Ub] = blend[Ub]
            del dP, Pv, blend
            # smooth: the harmonic surface between the courses (fixed) and the ground round the zone (fixed), from the blend
            known = np.where(has_pin, pinb, hb).astype(np.float64)
            F[Ub] = harmonic(Ub & ~has_pin, known, F, dy, dx)[Ub]
            F[has_pin] = pinb[has_pin]
        else:
            lo = np.maximum(Bv - SIDE_SLOPE * dB, foot[ci] if foot[ci] > -1e8 else hb - 5.0)
            F[Ub] = lo[Ub]
            F[has_pin] = np.minimum(F[has_pin], pinb[has_pin])     # their walls
        F = np.minimum(F, hb)
        hv = h[bb]
        hv[Ub] = F[Ub]
    drop_ = h0[Uf] - h[Uf]
    log(f"{name}: ground under the zone lowered by {float(drop_.mean()):.1f} m on average, at most {float(drop_.max()):.1f} m; "
        f"{int(pinned[1:].sum())} of {ncomp} zone pieces have a course")
    del drop_, comp, pin

    # --- per reservoir stats
    for i, (st, reg) in enumerate(found):
        ax = st.pop("_ax", None)
        if ax is not None and os.environ.get("DAMS_DEBUG"):
            # the ground before and after along the line square to the wall through its middle, from the reservoir's side
            cy, cx, nrm, _ = ax
            ss = np.arange(-600, 1601, 100)
            rr_ = np.clip(np.round(-(cy + ss * nrm[1]) / dy - 0.5).astype(np.int64), 0, n_r - 1)
            cc_ = np.clip(np.round((cx + ss * nrm[0]) / dx - 0.5).astype(np.int64), 0, n_c - 1)
            log(f"    across {st['hid']} (m below the wall: before/after; * wall, ~ zone): " + " ".join(
                f"{s_}:{h0[a, b]:.0f}/{h[a, b]:.0f}{'*' if dam_px[a, b] else '~' if zone[a, b] else ''}" for s_, a, b in zip(ss, rr_, cc_)))
        st["foot"] = float(foot_of.get(i, math.nan))
        sl, s = reg_view(reg, (n_r, n_c))
        st["region_px"] = int(s.sum()) if sl is not None else 0
        if st["region_px"]:
            st["floor_mean_drop"] = float(np.mean((h0[sl] - h[sl])[s]))
            st["floor_max_drop"] = float(np.max((h0[sl] - h[sl])[s]))
        if "_D" in st:
            box, D = st.pop("_D")
            st["dam_top_after"] = float(h[box][D].max()) if D.any() else float("nan")
            st["dam_mean_after"] = float(h[box][D].mean()) if D.any() else float("nan")
            st["dam_max_drop"] = float((h0[box] - h[box])[D].max()) if D.any() else float("nan")
        if "_F" in st:
            box, Fc = st.pop("_F")
            st["face_top_after"] = float(h[box][Fc].max()) if Fc.any() else float("nan")
    # --- back to the squares' own pixels (only lowered ones)
    patches = {}
    river_px = {}
    corridors = {}
    for tname, i, j, wp, tlat, tlon in tiles:
        blk = (slice(i * PX, (i + 1) * PX), slice(j * PX, (j + 1) * PX))
        u = Uf[blk]
        if not u.any():
            continue
        colmap = gg.column_index(PX, wp)
        u = u & (colmap < wp)[None, :]
        if not u.any():
            continue
        r_, c_ = np.nonzero(u)
        nat = r_ * wp + colmap[c_]
        sums = np.bincount(nat, weights=h[blk][u].astype(np.float64), minlength=1200 * wp)
        cnt = np.bincount(nat, minlength=1200 * wp)
        dem = tifffile.imread(os.path.join(DEM, tname.replace("_WBM.tif", "_DEM.tif")))
        orig = np.round(dem.astype(np.float64)).astype(np.int64).ravel()
        idx = np.flatnonzero(cnt)
        newh = np.round(sums[idx] / cnt[idx]).astype(np.int64)
        lowered = newh < orig[idx]
        if lowered.any():
            patches[(tlat, (tlon + 180) % 360 - 180)] = (wp, idx[lowered], newh[lowered])
        # the regions' river pixels, for the river outlines (as bake_river_outlines' reservoirs.npz), and the corridors
        w = tifffile.imread(os.path.join(WBM, tname))
        z = zone[blk]
        own_ = colmap < wp
        starts_ = np.searchsorted(colmap[own_], np.arange(wp))
        z = z[:, own_]
        if z.any():
            orig_z = np.add.reduceat(z.astype(np.uint8), starts_, axis=1) > 0
            rp = np.flatnonzero(orig_z & (w == 3)).astype(np.int32)
            if len(rp):
                river_px[tname] = rp
        cz = cor[blk][:, own_]
        if cz.any():
            lvz = np.minimum.reduceat(np.where(cz, corl[blk][:, own_], np.inf), starts_, axis=1).ravel()
            ci = np.flatnonzero(np.isfinite(lvz) & (w.ravel() != 1)).astype(np.int32)
            if len(ci):
                corridors[tname] = (ci, np.floor(lvz[ci]).astype(np.int16))
    # previews: relief before and after
    if previews:
        try:
            preview(os.path.join(out, f"preview-{name}"), h0, h, Uf, water, dy, dx)
        except Exception as e:  # noqa: BLE001
            log(f"preview failed: {e}")
    return res, patches, river_px, {int(s): int(new[s]) for s in changed}, corridors, run_rows


# ------------------------------------------------------------------------------------------- the whole world

MOSAIC_R = (1, 2, 3, 4, 7)          # pass 1's mosaics: 2r+1 squares each way round the dam's (Jebel Aulia needs 15)
CLUSTER_GAP = 6000.0               # m: reservoirs whose regions (and dams) lie within twice this of each other are done
                                    # together (a cascade; the courses' ramps, joins and walls reach a few km out)
BIG_GROUP = 24                      # squares: a group on more is done with BIG_WORKERS at a time (about 25 bytes a pixel,
BIG_WORKERS = 2                     # 1.4 million pixels a square: the Volga's cascade on ~230 squares takes ~8 GB)
G = {}


def find_group(dams_):
    """Pass 1, the dams of one square: each one's region on mosaics of 3, 5, 7 and 9 squares round its square (larger
    while it reaches the edge), in world pixels (rows from 90° N, columns from 180° W, PX a degree), packed."""
    out_ = []
    cache = {}                            # r -> (water, q); the smallest kept, one larger at a time
    for d in dams_:
        lat0, lon0 = int(math.floor(d[2])), int(math.floor(d[1]))
        for r in MOSAIC_R:
            if r not in cache:
                for k_ in [k_ for k_ in cache if k_ > 1]:
                    del cache[k_]
                water, h, _ = mosaic(lat0 - r, lat0 + r + 1, lon0 - r, lon0 + r + 1)
                q = np.round(h.astype(np.float64) * 2).astype(np.int32)
                del h
                cache[r] = (water, q)
            water, q = cache[r]
            lat_n, lon_w = lat0 + r + 1, lon0 - r
            top_, left_ = gg.top(lat_n, PX), gg.left(lon_w, PX)
            rows_lat = top_ - (np.arange(water.shape[0]) + 0.5) / PX
            px_km2 = (111.32 / PX) ** 2 * np.cos(np.radians(rows_lat))
            st, reg = grow_local(d, water, q, px_km2, top_, left_)
            if reg is None or not st["edge"] or r == MOSAIC_R[-1]:
                break
        st = {k: v for k, v in st.items() if k not in ("pr", "pc")}
        st.update(hid=int(d[0]), lon=float(d[1]), lat=float(d[2]), area=float(d[3]), elev=float(d[4]), mosaic_r=r)
        if reg is not None:
            reg = (reg[0] + round((90 - lat_n) * PX), reg[1] + round((lon_w + 180) * PX), np.packbits(reg[2]), reg[2].shape)
        out_.append((int(d[0]), st, reg))
    return out_


def init_bake(courses, dams_path):
    G["C"] = read_courses(courses)
    G["dams"] = np.load(dams_path)


def bake_cluster(task):
    """Pass 2, one group of reservoirs on one mosaic, with the regions of pass 1."""
    name, (lat_s, lat_n, lon_w, lon_e), regs = task
    regions = {}
    for hid, (st, reg) in regs.items():
        if reg is not None:
            n_ = reg[3][0] * reg[3][1]
            reg = (reg[0], reg[1], np.unpackbits(reg[2], count=n_).reshape(reg[3]).astype(bool))
        regions[hid] = (st, reg)
    res, p, rp, lv, cr, rr = area(name, lat_s, lat_n, lon_w, lon_e, G["dams"], G["C"], None, only=set(regs), regions=regions,
                                  previews=False)
    stats = [{k: v for k, v in st.items() if not k.startswith("_")} for st, _ in res]
    for r_ in rr:
        r_["area_name"] = name
    return name, stats, p, rp, lv, cr, rr


def merge_min(idx_a, v_a, idx_b, v_b):
    """Two sets of (index, value): every index once, with its lower value."""
    idx = np.concatenate([idx_a, idx_b])
    vv = np.concatenate([v_a, v_b])
    o = np.lexsort((vv, idx))
    idx, vv = idx[o], vv[o]
    keep = np.r_[True, idx[1:] != idx[:-1]]
    return idx[keep], vv[keep]


def merge_patch(a, b):
    """Two patches of one square (columns, indices, heights): every index once, at the lower height."""
    assert a[0] == b[0]
    return (a[0],) + merge_min(a[1], a[2], b[1], b[2])


def merge_results(acc, p, rp, lv, cr):
    """Adds one area's or group's patches, reservoir river pixels, course levels and corridors to acc."""
    patches, rivers, levels, corridors = acc
    for k, v in p.items():
        patches[k] = merge_patch(patches[k], v) if k in patches else v
    for k, v in rp.items():
        rivers[k] = np.union1d(rivers[k], v) if k in rivers else v
    for s, v in lv.items():
        levels[s] = min(levels.get(s, v), v)
    for k, v in cr.items():
        corridors[k] = merge_min(*corridors[k], *v) if k in corridors else v


def save_corridors(out, corridors):
    np.savez(os.path.join(out, "corridors.npz"), **{k_: v for k, (i_, l_) in corridors.items() for k_, v in ((k, i_), (k + "|lv", l_))})


def run_global(out, courses, workers):
    import pickle
    from multiprocessing import Pool
    dams = read_dams(out)
    dams_path = os.path.join(out, "hl_reservoirs.npy")
    log(f"{len(dams)} HydroLAKES reservoirs")
    # --- pass 1: the regions
    rpath = os.path.join(out, "regions.pkl")
    if os.path.exists(rpath):
        with open(rpath, "rb") as f:
            regs = pickle.load(f)
        log(f"regions from {rpath}")
    else:
        groups = {}
        for d in dams:
            groups.setdefault((math.floor(d[2]), math.floor(d[1])), []).append(d)
        tasks = sorted(groups.values(), key=lambda g: -max(x[3] for x in g))
        regs = {}
        with Pool(workers) as pool:
            for i, res in enumerate(pool.imap_unordered(find_group, tasks), 1):
                for hid, st, reg in res:
                    regs[hid] = (st, reg)
                if i % 200 == 0:
                    log(f"regions: {i}/{len(tasks)} squares, {len(regs)} reservoirs")
        with open(rpath, "wb") as f:
            pickle.dump(regs, f, protocol=pickle.HIGHEST_PROTOCOL)
    # regions found before the largest mosaic was added, still at their mosaic's edge: again
    redo = [d for d in dams if regs.get(int(d[0]), ({}, None))[1] is not None and regs[int(d[0])][0]["edge"]
            and regs[int(d[0])][0].get("mosaic_r", 0) < MOSAIC_R[-1]]
    if redo:
        for d in redo:
            for hid, st, reg in find_group([d]):
                regs[hid] = (st, reg)
                log(f"region of {hid} again: {st.get('result')}, {st.get('km2', 0):.0f} km2, mosaic {st.get('mosaic_r')}, "
                    f"edge {st.get('edge')}")
        with open(rpath, "wb") as f:
            pickle.dump(regs, f, protocol=pickle.HIGHEST_PROTOCOL)
    res_ = [st.get("result") for st, _ in regs.values()]
    log(f"regions: {res_.count('found')} found, {res_.count('wide')} wide, {res_.count('none')} none; "
        f"{sum(1 for st, r in regs.values() if r is not None and st['edge'])} still at their mosaic's edge")
    # --- groups: regions (with their dams) within 2 x CLUSTER_GAP of each other, as union-find on boxes
    by_hid = {int(d[0]): d for d in dams}
    hids = [h_ for h_, (st, reg) in regs.items() if reg is not None]
    B = np.zeros((len(hids), 4))
    for k, h_ in enumerate(hids):
        st, reg = regs[h_]
        d = by_hid[h_]
        pr, pc = (90 - d[2]) * PX, (d[1] + 180) * PX
        r0, r1 = min(reg[0], pr), max(reg[0] + reg[3][0], pr)
        c0, c1 = min(reg[1], pc), max(reg[1] + reg[3][1], pc)
        mr = CLUSTER_GAP / (111320.0 / PX)
        mc = mr / max(math.cos(math.radians(90 - (r0 + r1) / 2 / PX)), 0.05)
        B[k] = (r0 - mr, r1 + mr, c0 - mc, c1 + mc)
    parent = list(range(len(hids)))

    def root(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    for k in range(len(hids)):
        ov = np.flatnonzero((B[:, 0] <= B[k, 1]) & (B[:, 1] >= B[k, 0]) & (B[:, 2] <= B[k, 3]) & (B[:, 3] >= B[k, 2]))
        for j in ov:
            a, b = root(k), root(int(j))
            if a != b:
                parent[a] = b
    members = {}
    for k in range(len(hids)):
        members.setdefault(root(k), []).append(k)
    tasks = []
    for ci, ks in enumerate(members.values()):
        bb_ = B[ks]
        lat_n = int(math.ceil(90 - bb_[:, 0].min() / PX))
        lat_s = int(math.floor(90 - bb_[:, 1].max() / PX))
        lon_w = int(math.floor(bb_[:, 2].min() / PX - 180))
        lon_e = int(math.ceil(bb_[:, 3].max() / PX - 180))
        tasks.append((f"g{ci}", (max(lat_s, SOUTH), min(lat_n, 90), lon_w, lon_e), {hids[k]: regs[hids[k]] for k in ks}))
    sq = np.array([(t[1][1] - t[1][0]) * (t[1][3] - t[1][2]) for t in tasks])
    log(f"{len(tasks)} groups; squares per group: median {np.median(sq):.0f}, 90th {np.percentile(sq, 90):.0f}, "
        f"largest {sorted(sq)[-8:]}; reservoirs in the largest groups {sorted(len(t[2]) for t in tasks)[-8:]}")
    if os.environ.get("DAMS_GROUPS_ONLY"):
        for t in sorted(tasks, key=lambda t: -(t[1][1] - t[1][0]) * (t[1][3] - t[1][2]))[:12]:
            log(f"  {t[0]}: {t[1]}, {len(t[2])} reservoirs")
        return
    # --- pass 2: the groups, the largest first and fewer at a time
    tasks.sort(key=lambda t: -(t[1][1] - t[1][0]) * (t[1][3] - t[1][2]))
    big = [t for t in tasks if (t[1][1] - t[1][0]) * (t[1][3] - t[1][2]) > BIG_GROUP]
    small = [t for t in tasks if (t[1][1] - t[1][0]) * (t[1][3] - t[1][2]) <= BIG_GROUP]
    C = read_courses(courses)
    allres, acc, allruns = [], ({}, {}, {}, {}), []
    for st, reg in regs.values():
        if reg is None:
            allres.append(dict(st, area_name=""))
    done_ = 0
    big_workers = min(int(os.environ.get("DAMS_BIG_WORKERS", BIG_WORKERS)), workers)
    for batch, nw in ((big, big_workers), (small, workers)):
        if not batch:
            continue
        with Pool(nw, initializer=init_bake, initargs=(courses, dams_path)) as pool:
            for name, stats, p, rp, lv, cr, rr in pool.imap_unordered(bake_cluster, batch):
                done_ += 1
                for st in stats:
                    st["area_name"] = name
                    allres.append(st)
                allruns += rr
                merge_results(acc, p, rp, lv, cr)
                if done_ % 100 == 0:
                    log(f"groups: {done_}/{len(tasks)}, {len(acc[0])} squares, {len(acc[2])} course levels")
    return C, allres, acc, allruns


def harmonic(unk, known, init, dy, dx):
    """Laplace's equation on the unknown pixels (4 neighbours, weighted 1/dy², 1/dx²), the others fixed at known; a
    neighbour beyond the array is left out (no flow). Conjugate gradients from init; returns the whole array."""
    from scipy.sparse import coo_matrix, diags
    from scipy.sparse.linalg import cg
    H, W = unk.shape
    n = int(unk.sum())
    out = init.copy()
    if n == 0:
        return out
    idx = np.full(unk.shape, -1, np.int64)
    idx[unk] = np.arange(n)
    ys, xs = np.nonzero(unk)
    me = idx[ys, xs]
    diag = np.zeros(n)
    rhs = np.zeros(n)
    rows, cols, vals = [], [], []
    for oy, ox, w in ((-1, 0, 1 / dy ** 2), (1, 0, 1 / dy ** 2), (0, -1, 1 / dx ** 2), (0, 1, 1 / dx ** 2)):
        ny, nx = ys + oy, xs + ox
        ok = (ny >= 0) & (ny < H) & (nx >= 0) & (nx < W)
        nyc, nxc = np.clip(ny, 0, H - 1), np.clip(nx, 0, W - 1)
        diag[ok] += w
        u = ok & unk[nyc, nxc]
        rows.append(me[u]); cols.append(idx[nyc[u], nxc[u]]); vals.append(np.full(int(u.sum()), -w))
        k = ok & ~unk[nyc, nxc]
        np.add.at(rhs, me[k], w * known[nyc[k], nxc[k]])
    A = coo_matrix((np.concatenate(vals), (np.concatenate(rows), np.concatenate(cols))), shape=(n, n)).tocsr() + diags(diag)
    M = diags(1 / diag)
    x, info = cg(A, rhs, x0=init[unk], rtol=1e-7, maxiter=20000, M=M)
    if info != 0:
        log(f"  harmonic floor: conjugate gradients stopped ({info}), {n} pixels")
    out[unk] = x
    return out


def preview(path, h0, h1, U, water, dy, dx):
    from PIL import Image
    ys, xs = np.nonzero(U)
    if not len(ys):
        return
    pad = 60
    r0, r1 = max(ys.min() - pad, 0), min(ys.max() + pad, U.shape[0])
    c0, c1 = max(xs.min() - pad, 0), min(xs.max() + pad, U.shape[1])
    step = max(1, int(max(r1 - r0, c1 - c0) / 1600))
    sl = (slice(r0, r1, step), slice(c0, c1, step))
    lo, hi = np.percentile(h0[sl], 1), np.percentile(h0[sl], 99)
    for tag, hh in (("before", h0), ("after", h1)):
        a = hh[sl].astype(np.float64)
        gy, gx = np.gradient(a, dy * step, dx * step)
        shade = np.clip(0.75 + 2.5 * (-gx * 0.7 - gy * 0.7), 0.25, 1.3)
        t = np.clip((a - lo) / max(hi - lo, 1), 0, 1)
        rgb = np.stack([90 + 140 * t, 130 + 80 * t, 70 + 120 * t], -1) * shade[..., None]
        if tag == "before":
            wmask = water[sl] & ~U[sl]
        else:
            wmask = water[sl] & ~U[sl]
        rgb[wmask] = (40, 90, 170)
        Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8)).save(f"{path}-{tag}.png")


# ------------------------------------------------------------------------------------------- file

def write(out, key, patches, levels, C):
    sq = sorted(patches.items(), key=lambda kv: (kv[0][0] + 90) * 360 + (kv[0][1] + 180))
    blobs = []
    for (lat, lon), (wp, idx, newh) in sq:
        gaps = np.diff(np.concatenate([[-1], idx])) - 1
        dh = np.diff(np.concatenate([[0], newh]))
        zz = (dh << 1) ^ (dh >> 63)
        raw = varints([len(idx)]) + varints(gaps.tolist()) + varints(zz.tolist())
        blobs.append(brotli.compress(raw, quality=11, lgwin=22))
    # as DamRemovalData.Levels reads them: from reach 0, station -1; a reach gap > 0 then the station, else 0 then the
    # station's gap within the reach
    st = sorted(levels.items())
    raw = bytearray()
    pr, pk = 0, -1
    for s, v in st:
        r = int(C["reach"][s])
        k = int(s - C["start"][r])
        raw += varints([r - pr, k]) if r != pr else varints([0, k - pk - 1])
        raw += varints([(v << 1) ^ (v >> 63)])
        pr, pk = r, k
    cblob = brotli.compress(bytes(raw), quality=11, lgwin=22) if st else b""
    head = struct.pack("<4siiQi", b"EMDM", VERSION, len(sq), key, len(st))
    o = len(head) + 16 * len(sq)
    index = bytearray()
    for ((lat, lon), (wp, idx, newh)), blob in zip(sq, blobs):
        index += struct.pack("<hhiii", lat, lon, wp, o, len(blob))
        o += len(blob)
    data = head + bytes(index) + b"".join(blobs) + cblob
    with open(os.path.join(out, FILE), "wb") as f:
        f.write(data)
    return len(data), sum(len(p[2]) for p in patches.values()), hashlib.sha256(data).hexdigest()


FILE = f"dams-glo90-{VERSION}.bin"


def main():
    """bake_dam_removal.py OUT COURSES name:lat_s:lat_n:lon_w:lon_e ...   areas, each on one mosaic (the prototype)
       bake_dam_removal.py OUT COURSES --global [workers]                  every reservoir, in groups (DAMS_BOX=
       lat_s:lat_n:lon_w:lon_e[,...] keeps only the dams in those boxes, for a test)"""
    sys.stdout.reconfigure(encoding="utf-8")
    out, courses = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    if sys.argv[3] == "--global":
        if os.environ.get("DAMS_BOX"):
            dams = read_dams(out)
            keep = np.zeros(len(dams), bool)
            for b_ in os.environ["DAMS_BOX"].split(","):
                a, b, c, d = map(float, b_.split(":"))
                keep |= (dams[:, 2] >= a) & (dams[:, 2] < b) & (dams[:, 1] >= c) & (dams[:, 1] < d)
            np.save(os.path.join(out, "hl_reservoirs.npy"), dams[keep])
        r_ = run_global(out, courses, int(sys.argv[4]) if len(sys.argv) > 4 else 12)
        if r_ is None:
            return
        C, allres, acc, allruns = r_
    else:
        C = read_courses(courses)
        dams = read_dams(out)
        log(f"{len(dams)} HydroLAKES reservoirs")
        allres, acc, allruns = [], ({}, {}, {}, {}), []
        for spec in sys.argv[3:]:
            name, a, b, c, d = spec.split(":")
            res, p, rp, lv, cr, rr = area(name, int(a), int(b), int(c), int(d), dams, C, out)
            for st, _ in res:
                st["area_name"] = name
                allres.append(st)
            for r_ in rr:
                r_["area_name"] = name
            allruns += rr
            merge_results(acc, p, rp, lv, cr)
    patches, rivers, levels, corridors = acc
    nbytes, npx, sha = write(out, C["key"], patches, levels, C)
    np.savez(os.path.join(out, "reservoirs-dams.npz"), **rivers)
    save_corridors(out, corridors)
    rcols = ["area_name", "res", "dam", "n", "q", "src", "w_in", "w_out", "w", "hw_zone", "hw_wall", "lv_in", "lv_end", "cap_knots",
             "lat0", "lon0", "lat1", "lon1"]
    with open(os.path.join(out, "corridor-runs.tsv"), "w", encoding="utf8") as f:
        f.write("\t".join(rcols) + "\n")
        for r_ in sorted(allruns, key=lambda r: -r["q"]):
            f.write("\t".join(f"{r_[c]:.4f}" if isinstance(r_.get(c), float) else str(r_.get(c, "")) for c in rcols) + "\n")
    cols = ["area_name", "hid", "lat", "lon", "area", "elev", "result", "level", "km2", "rise", "edge", "mosaic_r", "region_px", "foot_px",
            "dam_px", "dam_top", "dam_top_after", "dam_mean", "dam_mean_after", "dam_max_drop", "face_px", "face_top", "face_top_after",
            "below", "foot", "floor_mean_drop", "floor_max_drop", "dam_lat0", "dam_lon0", "dam_lat1", "dam_lon1"]
    with open(os.path.join(out, "dams.tsv"), "w", encoding="utf8") as f:
        f.write("\t".join(cols) + "\n")
        for st in sorted(allres, key=lambda s: -s.get("km2", 0)):
            f.write("\t".join(f"{st[c]:.3f}" if isinstance(st.get(c), float) else str(st.get(c, "")) for c in cols) + "\n")
    log(f"{FILE}: {nbytes} bytes, sha256 {sha}, {len(patches)} squares, {npx} points lowered, {len(levels)} course levels; "
        f"corridors in {len(corridors)} squares, {sum(len(v[0]) for v in corridors.values())} pixels")


if __name__ == "__main__":
    main()
