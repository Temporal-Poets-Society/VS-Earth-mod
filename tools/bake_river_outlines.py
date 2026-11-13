"""Bake rivers-outline-glo90-VERSION.bin: the rivers of the Copernicus GLO-90 water body mask (class 3) as outlines, for
worlds on GLO-90 heights (Earth 0053 items 18 and 19, 0063 rule 2).

The author, on Riga in a 1:10 world: "river looks to be wrong. straight lines and not in the correct path". The rivers
are drawn from HydroRIVERS' courses, moved onto GLO-90 (rivers-glo90.bin and its snap and arms files): a course is a
line of straight pieces about 500 m long, and the water a channel of one width along it (at 1:10, 50-block straight
lines, 20 blocks wide where the Daugava is 600-700 m). The mask has the river's own water, and its shores.

Every river pixel of the mask north of 60°S is drawn, with a pixel of the lakes and the sea it touches (the lakes'
outlines and the rivers' are rounded off apart, and left land between them where they meet), except reservoirs (no
modern dams in 1318, as lakes-glo90-1.bin): the mask draws many as rivers (above the Riga dam, 1974, a 4 km wide
"river"). From each HydroLAKES reservoir's dam (type 2, its pour point) the water the mask has there (lake or river) is
followed at the reservoir's level: the highest flat water by the dam (the water below a dam is lower) of a tenth of the
reservoir's area or 1 km², then up to 3 m higher in half-metre steps until it has the reservoir's area, never more than
twice it (flat water by a dam that is already wider is more than the reservoir, as a forebay's in a delta at sea level,
and is left as it is). HydroLAKES' own
elevations are often from before the dam filled (Three Gorges 62 m; the reservoir is at 145-175 m). Its river pixels are
left out; Earth's own river courses run there, as before.
The mod decides where the rest count: on
dry land only (Earth's sea and lakes stay as they are), not on ice, and their water takes the level of the nearest
river course (so a river's water is one surface with its channel). Inside the mask's rivers GLO-90 is the water's own
surface, flattened in steps of half a metre (Riga 0 m, above the Riga dam 14.5 m, above Ķegums 31 m): each piece's
median height is kept as its level, for water the mod finds no course for.

Outlines are marching squares at the pixel edges, simplified by half a pixel (tools/bake_lake_outlines.py); vertices on
a tile's edge stay where they are, so neighbours meet exactly.

File (little-endian): the layout of lakes-glo90-1.bin (tools/bake_lake_outlines.py) with "EMRO" for "EMLO": version 1,
unit, tile size, tiles across and down, a state byte per tile (255 no data, 0 no river, 1 rivers), (offset, length) per
tile with rivers, then each such tile's brotli stream of pieces (each a "lake": level in decimetres, flags 0 or in
version 3 16 for a narrow piece, rings).

Version 3 (Earth 0063 rule 2, rivers-outline-glo90-3.bin). The mask's rivers are one or two pixels wide in most places
(a pixel is 9.3 blocks at 1:10), and marching squares split an outline wherever two pixels touch only at a corner (Rome:
25 rings among 83 river pixels; the game rounds them off but can't join them). So:
1. Corner-only contacts are joined first: in each 2 x 2 window with river on one diagonal only, of the two pixels on the
   other the lower by GLO-90 becomes river (a lake's or the sea's pixel first: it is water already). Repeated until none
   are left. Joined pixels and bridge decks don't count towards a piece's level.
2. The water is split into wide water (what survives an opening and then a closing by a WIDE x WIDE square: three pixels
   and more across) and narrow pieces (the rest). A narrow fringe all of whose pixels touch the wide water (a bank's
   pixel) goes with the wide water; a wide piece under WIDE_MIN pixels that runs on as narrow water (a pool of 3 x 3 to
   3 x 6 pixels in a narrow river: the Tigris below Amarah, the Gauja) is narrow, unless it reaches the tile's edge,
   beyond which it may go on. Narrow pieces are written as before (marching squares, simplified by half a pixel)
   with flag 16 (RiverOutlineData.FlagNarrow): the courses draw those rivers at fine scales, coarse worlds draw them. A
   narrow piece reaches a pixel into the wide water it runs into, cut back to OVERLAP inside the wide water's outline,
   so the two meet with no land between them.
3. Wide water is contoured smooth: the 0.5 contour of its pixels (with a pixel of the lakes and sea it touches) blurred by
   a Gaussian of SIGMA pixels, simplified by WIDE_TOL. The blur fades in over EDGE_TAPER pixels from a tile's edge, so
   on the edge an outline is the pixels' own as before and neighbouring tiles meet as they did; vertices on the edge stay
   where they are. Vertices are in UNIT3 units a degree (a twentieth of a pixel north-south; half a pixel is a whole
   number of units at every tile width), so the smooth outlines keep their shape.
Levels, bridges, reservoirs and dam corridors as version 2.

Usage: python -I tools/bake_river_outlines.py OUTDIR [workers] [tile-name-filter[,filter...]] [--reservoirs NPZ]
       [--corridors NPZ] [--version 1|2|3]
The reservoirs are found first and kept in OUTDIR/reservoirs.npz (delete it to find them again); a filter still finds
them all. tools/bake_dam_removal.py writes the river pixels of its own reservoirs (reservoirs-dams.npz) in that layout.
With its corridors.npz beside them (0053 item 19), the corridors are river at their levels (version 2: for worlds with
the dams taken out). --reservoirs and --corridors take the dam bake's files from where they are (then OUTDIR's
reservoirs.npz is not used). The version is 3 with corridors and 1 without, unless --version says otherwise. Writes
OUTDIR/rivers-outline-glo90-VERSION.bin and .json.
"""
import glob, hashlib, json, math, os, struct, sys, time
from multiprocessing import Pool

import numpy as np
import tifffile
import brotli
import contourpy
import shapely
import shapely.geometry
from scipy import ndimage as ndi

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bake_lake_outlines import RAW, DEM, WBM, SOUTH, UNIT, TOL, tile_origin, simplify_ring, lake_bytes, varints  # noqa: E402

VERSION = 3                         # the newest file (0063 rule 2); 1 without the dam bake's corridors
UNIT3 = 24000                       # version 3: vertex units per degree (a twentieth of a pixel north-south)
WIDE = 3                            # version 3: wide water survives an opening and a closing by a WIDE x WIDE square
WIDE_MIN = 20                       # a wide piece under this many pixels that runs on as narrow water is narrow (a pool)
SIGMA = 0.7                         # its pixels blurred by a Gaussian of this many pixels before the 0.5 contour
WIDE_TOL = 0.1                      # its outlines simplified by this many pixels
EDGE_TAPER = 3                      # the blur fades in over this many pixels from a tile's edge
OVERLAP = 0.3                       # narrow pieces reach this many pixels inside the wide water's outline
JOIN_PASSES = 8                     # corner contacts joined in up to this many passes
FLAG_NARROW = 16                    # RiverOutlineData.FlagNarrow
EIGHT = np.ones((3, 3), bool)
BRIDGE_GAP = 1                      # version 2: a closing by this many pixels finds bridges (decks up to 2 pixels wide)
BRIDGE_LINE = 3                     # in lines at least this many pixels long
BRIDGE_REACH = 3                    # whose ends reach land within this many pixels (end spans the mask has as water),
BRIDGE_OPEN = 4                     # with water on for this many pixels (up to the deck's length) on both sides across it
BRIDGE_BANK = 2                     # where the water beside it is at most this much wider than the deck and its reach
BRIDGE_DIRS = ((0, 1), (1, 0), (1, 1), (1, -1))
RES_RISE = 6                        # half-metre steps a reservoir's water may rise above its level by the dam (3 m)
RES_SEED = 0.1                      # the flat water by the dam must be this share of the reservoir's area or more,
RES_SEED_KM2 = 1.0                  # or this many km² (most of a long reservoir may lie beyond the mosaic)
RES_WIDE = 2.0                      # and at most this many times its area; the water followed stops there too
PX = 1200                           # mosaic pixels per degree, both ways (the mask's rows; columns resampled)
T0 = time.time()
G = {}


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


# ------------------------------------------------------------------------------------------- reservoirs

def tile_name(lat0, lon0):
    lon0 = (lon0 + 180) % 360 - 180
    return f"Copernicus_DSM_COG_30_{'N' if lat0 >= 0 else 'S'}{abs(lat0):02d}_00_{'E' if lon0 >= 0 else 'W'}{abs(lon0):03d}_00_WBM.tif"


def mosaic(lat0, lon0, r):
    """The mask's water (lake or river) and GLO-90 in half metres for tiles lat0-r..lat0+r, lon0-r..lon0+r, north up, at
    PX columns a degree, and the tiles in it: (name, row, column, width)."""
    n = 2 * r + 1
    water = np.zeros((n * PX, n * PX), bool)
    q = np.zeros((n * PX, n * PX), np.int16)
    tiles = []
    for i in range(n):
        lat = lat0 + r - i
        if lat < SOUTH or lat > 89:
            continue
        for j in range(n):
            name = tile_name(lat, lon0 - r + j)
            path = os.path.join(WBM, name)
            if not os.path.exists(path):
                continue
            w = tifffile.imread(path)
            hp, wp = w.shape
            cols = np.arange(PX) * wp // PX
            d = tifffile.imread(os.path.join(DEM, name.replace("_WBM.tif", "_DEM.tif")))
            blk = (slice(i * PX, (i + 1) * PX), slice(j * PX, (j + 1) * PX))
            water[blk] = ((w == 2) | (w == 3))[:, cols]
            q[blk] = np.round(d[:, cols].astype(np.float64) * 2).astype(np.int16)
            tiles.append((name, i, j, wp))
    return water, q, tiles


def reservoir(dam, cache):
    """The river pixels of one HydroLAKES reservoir, per tile: (stats, {tile name: flat indices})."""
    hid, lon, lat, area, elev = dam
    lat0, lon0 = int(math.floor(lat)), int(math.floor(lon))
    coslat = max(math.cos(math.radians(lat)), 0.05)
    for r in (1, 2, 3):
        key = (lat0, lon0, r)
        if cache.get("key") != key:
            cache.clear()
            cache["key"] = key
            cache["m"] = mosaic(lat0, lon0, r)
        water, q, tiles = cache["m"]
        n = water.shape[0]
        rows_lat = lat0 + r + 1 - (np.arange(n) + 0.5) / PX
        px_km2 = (111.32 / PX) ** 2 * np.cos(np.radians(rows_lat))
        pr = int((lat0 + r + 1 - lat) * PX)
        pc = int((lon - lon0 + r) * PX)
        hr = int(3 + 0.05 * math.sqrt(area) / 111.32 * PX)
        hc = int(3 + 0.05 * math.sqrt(area) / (111.32 * coslat) * PX)
        win = (slice(max(pr - hr, 0), pr + hr + 1), slice(max(pc - hc, 0), pc + hc + 1))
        lv = None
        for level in np.unique(q[win][water[win]])[::-1]:
            lab, _ = ndi.label(water & (q == level), structure=np.ones((3, 3), bool))
            ids = np.unique(lab[win])
            seed = np.isin(lab, ids[ids > 0])
            km2 = float(seed.sum(axis=1) @ px_km2)
            if km2 >= min(RES_SEED * area, RES_SEED_KM2):
                lv = int(level)
                break
        if lv is None:
            return (hid, lat, lon, area, elev, None, 0.0, 0, r, ""), {}
        if km2 > RES_WIDE * area:
            # the flat water there is more than this reservoir (a forebay in a delta at sea level): left as it is
            return (hid, lat, lon, area, elev, lv / 2, km2, 0, r, "wide"), {}
        region = seed
        for step in range(0, RES_RISE + 1):
            if km2 >= area:
                break
            lab, _ = ndi.label(water & (q >= lv - 1) & (q <= lv + step), structure=np.ones((3, 3), bool))
            ids = np.unique(lab[seed])
            grown = np.isin(lab, ids[ids > 0])
            g_km2 = float(grown.sum(axis=1) @ px_km2)
            if g_km2 > RES_WIDE * area:
                break
            region, km2 = grown, g_km2
        edge = region[0].any() or region[-1].any() or region[:, 0].any() or region[:, -1].any()
        if not edge or km2 >= area or r == 3:
            break
    out = {}
    npx = 0
    for name, i, j, wp in tiles:
        blk = region[i * PX:(i + 1) * PX, j * PX:(j + 1) * PX]
        if not blk.any():
            continue
        starts = np.searchsorted(np.arange(PX) * wp // PX, np.arange(wp))
        orig = np.add.reduceat(blk.astype(np.uint8), starts, axis=1) > 0
        w = tifffile.imread(os.path.join(WBM, name))
        idx = np.flatnonzero(orig & (w == 3)).astype(np.int32)
        if len(idx):
            out[name] = idx
            npx += len(idx)
    return (hid, lat, lon, area, elev, lv / 2, km2, npx, r, "found"), out


def reservoir_group(dams):
    cache = {}
    return [reservoir(d, cache) for d in dams]


def find_reservoirs(out, workers):
    """Every HydroLAKES reservoir's river pixels, kept in OUTDIR/reservoirs.npz."""
    path = os.path.join(out, "reservoirs.npz")
    if os.path.exists(path):
        log(f"reservoirs from {path}")
        return path
    import shapefile
    hl = shapefile.Reader(os.path.join(RAW, "hydrolakes", "HydroLAKES_points_v10_shp", "HydroLAKES_points_v10.shp"),
                          encoding="latin1", encodingErrors="replace")
    dams = [(r["Hylak_id"], r["Pour_long"], r["Pour_lat"], r["Lake_area"], r["Elevation"]) for r in
            hl.iterRecords(fields=["Hylak_id", "Lake_type", "Lake_area", "Elevation", "Pour_long", "Pour_lat"])
            if r["Lake_type"] == 2 and r["Pour_lat"] >= SOUTH]
    groups = {}
    for d in dams:
        groups.setdefault((math.floor(d[2]), math.floor(d[1])), []).append(d)
    log(f"{len(dams)} HydroLAKES reservoirs in {len(groups)} tiles")
    px = {}
    stats = []
    with Pool(workers) as pool:
        for i, res in enumerate(pool.imap_unordered(reservoir_group, list(groups.values())), 1):
            if i % 500 == 0:
                log(f"{i}/{len(groups)} tiles of reservoirs")
            for st, o in res:
                stats.append(st)
                for name, idx in o.items():
                    px.setdefault(name, []).append(idx)
    np.savez(path, **{name: np.unique(np.concatenate(v)) for name, v in px.items()})
    stats.sort(key=lambda s: -s[7])
    with open(os.path.join(out, "reservoirs.tsv"), "w", encoding="utf8") as f:
        f.write("hylak_id\tlat\tlon\tkm2\televation\tlevel\tfound_km2\triver_px\tmosaic_r\tresult\n")
        for s in stats:
            f.write("\t".join(str(v) for v in s) + "\n")
    found = sum(1 for s in stats if s[9] == "found")
    wide = sum(1 for s in stats if s[9] == "wide")
    log(f"reservoirs: {found} of {len(stats)} found ({wide} more by water too wide to be theirs), "
        f"{sum(s[7] for s in stats)} river pixels in {len(px)} tiles")
    return path


def init_worker(res, cor, version):
    G["res"] = np.load(res)
    G["res_names"] = set(G["res"].files)
    # 0053 item 19: river corridors through the reservoirs left out (tools/bake_dam_removal.py), river at their levels
    G["cor"] = np.load(cor) if cor else None
    G["cor_names"] = set(G["cor"].files) if G["cor"] is not None else set()
    G["version"] = version
    G["v2"] = version >= 2
    # version 4 (0069 rule 5): the courses, to trim the river water's short bulges (EARTH_COURSES_NPZ: the flow bake's
    # courses.npz, tools/bake_river_flow.py)
    G["v4"] = version >= 4
    if G["v4"]:
        z = np.load(os.environ["EARTH_COURSES_NPZ"])
        for k in ("reach", "lat", "lon", "order", "tsorted"):
            G["c_" + k] = z[k]


def bridges(wbm, river):
    """Version 2: the bridges across the rivers (none in 1318). The mask stops at a deck: 1-2 pixels of land across the
    water, GLO-90 the deck's height (Riga's Southern Bridge 13 m over the Daugava at 0). Land that a closing of the water
    by BRIDGE_GAP fills and that has the river on one side and water of any class on the opposite one, in pieces no
    shorter along some direction than across it, whose two ends reach the land beyond within BRIDGE_REACH pixels along
    it (the mask has a deck's end spans as water: Riga's Southern Bridge touches neither bank; pieces shorter than
    BRIDGE_LINE only where land is next to both ends), with water on across them both ways for as far as they are long
    (BRIDGE_OPEN to twice that), and where the water beside them on both sides is no wider than they reach (BRIDGE_BANK
    more): a channel they span. An islet or a bar along the river ends in water or is longer along the river than
    across; a strip between two channels (Vienna's Danube Island, Riga's Zakusala) has land a channel's width away; a
    levee between a river and a floodplain lake (the Amazon's) has the water running on past its ends.
    Most decks are water in the mask already (Riga's Stone and railway bridges); these are the ones it has as land."""
    wet = wbm > 0
    p_ = BRIDGE_GAP + 1
    gap = ndi.binary_closing(np.pad(wet, p_), structure=np.ones((3, 3), bool), iterations=BRIDGE_GAP)[p_:-p_, p_:-p_] & ~wet
    if not gap.any():
        return gap
    H_, W_ = river.shape
    o_ = 2 * BRIDGE_GAP + 1
    rp = np.pad(river, o_)
    wp = np.pad(wet, o_)
    across = np.zeros(river.shape, bool)
    for dy_, dx_ in BRIDGE_DIRS:
        f_r, f_w, b_r, b_w = (np.zeros(river.shape, bool) for _ in range(4))
        for k_ in range(1, o_ + 1):
            fs = (slice(o_ + k_ * dy_, o_ + k_ * dy_ + H_), slice(o_ + k_ * dx_, o_ + k_ * dx_ + W_))
            bs = (slice(o_ - k_ * dy_, o_ - k_ * dy_ + H_), slice(o_ - k_ * dx_, o_ - k_ * dx_ + W_))
            f_r |= rp[fs]
            f_w |= wp[fs]
            b_r |= rp[bs]
            b_w |= wp[bs]
        # the river on one side, any water on the other (a tidal reach or a lake the mask has as such)
        across |= (f_r & b_w) | (f_w & b_r)
    cand = gap & across
    if not cand.any():
        return cand
    land = ~wet & ~cand
    lab, n = ndi.label(cand, structure=np.ones((3, 3), bool))
    out = np.zeros(river.shape, bool)
    for i, sl in enumerate(ndi.find_objects(lab), 1):
        ys, xs = np.nonzero(lab[sl] == i)
        ys, xs = ys + sl[0].start, xs + sl[1].start
        # a deck is a line at most two pixels thick across its main axis (a plug of land is a blob)
        if len(ys) > 2:
            lam = np.linalg.eigvalsh(np.cov(np.stack((ys, xs)).astype(np.float64)))[0]
            if math.sqrt(12.0 * max(lam, 0.0) + 1.0) > 2 * BRIDGE_GAP + 0.5:
                continue
        # the piece's extent in steps along each direction
        ext = {d: int(np.ptp(ys * d[0] + xs * d[1])) // (d[0] * d[0] + d[1] * d[1]) + 1 for d in BRIDGE_DIRS}
        for (dy_, dx_), perp in zip(BRIDGE_DIRS, ((1, 0), (0, 1), (1, -1), (1, 1))):
            # a deck runs across the water: no shorter along its direction than across it (an island along the river is)
            span = ext[(dy_, dx_)]
            if span < ext[perp]:
                continue
            pr = ys * dy_ + xs * dx_
            # a piece shorter than BRIDGE_LINE (an islet) only where it all but touches land at both ends (a gap across
            # a narrow channel)
            reach = BRIDGE_REACH if span >= BRIDGE_LINE else 1
            ends = 0
            for sgn, j in ((1, int(np.argmax(pr))), (-1, int(np.argmin(pr)))):
                for k_ in range(1, reach + 1):
                    yy, xx = ys[j] + sgn * k_ * dy_, xs[j] + sgn * k_ * dx_
                    if not (0 <= yy < H_ and 0 <= xx < W_):
                        break
                    if land[yy, xx]:
                        ends += 1
                        break
            if ends < 2:
                continue
            # across the deck the water runs on (upstream and downstream) for as far as the deck is long, from
            # BRIDGE_OPEN to twice that, not just a channel's width to land: a strip between two channels (Vienna's
            # Danube Island) is land. Steps leave the piece first; other decks count as water.
            need = min(max(span, BRIDGE_OPEN), 2 * BRIDGE_OPEN)
            m_ = int(np.argsort(pr)[len(pr) // 2])
            open_ = 0
            for sgn in (1, -1):
                run_ = 0
                for k_ in range(1, need + 2 * BRIDGE_GAP + 2):
                    yy, xx = ys[m_] + sgn * k_ * dx_, xs[m_] - sgn * k_ * dy_
                    if not (0 <= yy < H_ and 0 <= xx < W_):
                        break
                    if lab[yy, xx] == i and run_ == 0 and k_ <= 2 * BRIDGE_GAP:
                        continue
                    if not (wet[yy, xx] or cand[yy, xx]) or lab[yy, xx] == i:
                        break
                    run_ += 1
                    if run_ >= need:
                        break
                if run_ >= need:
                    open_ += 1
            if open_ < 2:
                continue
            # a deck spans a channel: beside it on both sides the banks are near its ends. A levee between a river and
            # a floodplain lake (the Amazon's), or a strip along the river, has the water running on past them.
            most = span + 2 * BRIDGE_REACH + BRIDGE_BANK
            narrow = 0
            for sgn in (1, -1):
                py_, px_ = ys[m_] + sgn * o_ * dx_, xs[m_] - sgn * o_ * dy_
                width = 1
                for s2 in (1, -1):
                    for k_ in range(1, most + 1):
                        yy, xx = py_ + s2 * k_ * dy_, px_ + s2 * k_ * dx_
                        if not (0 <= yy < H_ and 0 <= xx < W_) or not (wet[yy, xx] or cand[yy, xx]) or lab[yy, xx] == i:
                            break
                        width += 1
                narrow += width <= most
            if narrow == 2:
                out[ys, xs] = True
                break
    # only decks that touch the river: a piece's level is its river pixels' median, and a deck with only a lake or the sea
    # next to it would have none
    if out.any():
        lab, n = ndi.label(out, structure=np.ones((3, 3), bool))
        touch = np.unique(lab[out & ndi.binary_dilation(river, structure=np.ones((3, 3), bool))])
        out = np.isin(lab, touch[touch > 0])
    return out


# ------------------------------------------------------------------------------------------- outlines

def tile(path):
    """One mask tile's outlines: a dict with name, n (pieces), blob (brotli), nv (vertices), raw (bytes before brotli),
    px (river pixels), br (bridge pixels, pieces) and, for version 3, the wide and narrow water's counts (stats)."""
    return tile_v3(path) if G["version"] >= 3 else tile_v2(path)


def tile_v2(path):
    """Versions 1 and 2: every river piece contoured at the pixels' edges."""
    name = os.path.basename(path)
    lat0, lon0 = tile_origin(path)
    if lat0 < SOUTH:
        return None
    wbm = tifffile.imread(path)
    river = wbm == 3
    if name in G["res_names"]:
        river.ravel()[G["res"][name]] = False
    cor = name in G["cor_names"]
    if cor:
        river.ravel()[G["cor"][name]] = True
    if not river.any():
        return dict(name=name, n=0, blob=b"", nv=0, raw=0, px=0, br=(0, 0))
    br = bridges(wbm, river) if G.get("v2") else np.zeros(wbm.shape, bool)
    nbr = (int(br.sum()), int(ndi.label(br, structure=np.ones((3, 3), bool))[1])) if br.any() else (0, 0)
    river |= br
    # a pixel into the lakes and the sea a river touches: the lake's and the river's outlines are rounded off apart, and
    # left a sliver of land between them where they meet (Ķīšezers' channel at Riga); the mod draws rivers on dry land only
    keep = river | (ndi.binary_dilation(river, structure=np.ones((3, 3), bool)) & ((wbm == 1) | (wbm == 2)))
    hp, wp = wbm.shape
    dem = tifffile.imread(os.path.join(DEM, name.replace("_WBM.tif", "_DEM.tif"))).astype(np.float64)
    if cor:
        dem.ravel()[G["cor"][name]] = G["cor"][name + "|lv"]
    lab, k = ndi.label(keep, structure=np.ones((3, 3), bool))
    lf = np.where(river & ~br, lab, 0).ravel()    # each piece's level from its river pixels (not a bridge's deck)
    d = dem.ravel()
    m = lf > 0
    npx = np.bincount(lf, minlength=k + 1)
    order = np.lexsort((d[m], lf[m]))
    ds = d[m][order]
    starts = np.concatenate([[0], np.cumsum(npx[1:])[:-1]])
    level = np.zeros(k + 1)
    level[1:] = ds[starts + npx[1:] // 2]
    level_dm = np.round(level * 10).astype(np.int64)
    z = np.zeros((hp + 2, wp + 2), np.float32)
    z[1:-1, 1:-1] = keep
    polys, offs = contourpy.contour_generator(z=z, name="serial", fill_type="OuterOffset", corner_mask=False).filled(0.5, 1.5)
    pieces, nv = [], 0
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
        pieces.append(lake_bytes(int(level_dm[owner]), 0, rings))
    if not pieces:
        return dict(name=name, n=0, blob=b"", nv=0, raw=0, px=int(river.sum()), br=nbr)
    raw = varints([len(pieces)]) + b"".join(pieces)
    return dict(name=name, n=len(pieces), blob=brotli.compress(raw, quality=11, lgwin=22), nv=nv, raw=len(raw),
                px=int(river.sum()), br=nbr)


# ------------------------------------------------------------------------------------------- version 3

def join_corners(m, cost):
    """0063 rule 2.1: corner-only contacts joined. In each 2 x 2 window with m on one diagonal only, the pixel of the other
    diagonal with the lower cost is set; repeated until none are left (JOIN_PASSES at most). (mask, pixels set)."""
    m = m.copy()
    W = m.shape[1]
    done = set()
    for _ in range(JOIN_PASSES):
        a, b, c, d = m[:-1, :-1], m[:-1, 1:], m[1:, :-1], m[1:, 1:]
        r1, q1 = np.nonzero(a & d & ~b & ~c)      # set (r, q + 1) or (r + 1, q)
        r2, q2 = np.nonzero(b & c & ~a & ~d)      # set (r, q) or (r + 1, q + 1)
        if not len(r1) and not len(r2):
            break
        p1 = cost[r1, q1 + 1] <= cost[r1 + 1, q1]
        p2 = cost[r2, q2] <= cost[r2 + 1, q2 + 1]
        ys = np.concatenate([np.where(p1, r1, r1 + 1), np.where(p2, r2, r2 + 1)])
        xs = np.concatenate([np.where(p1, q1 + 1, q1), np.where(p2, q2, q2 + 1)])
        m[ys, xs] = True
        done.update((ys * W + xs).tolist())
    return m, len(done)


def split_wide(r):
    """0063 rule 2.2: (wide, narrow, fringe pixels, pool pixels) of the river water r. Wide water survives an opening and a
    closing by a WIDE x WIDE square (the tile's edge pixels repeated beyond it); a narrow piece all of whose pixels touch
    the wide water is a fringe of its bank and goes with it; a wide piece under WIDE_MIN pixels that runs on as narrow
    water (a pool in a narrow river) is narrow, unless it reaches the tile's edge (it may go on beyond)."""
    p = WIDE + 1
    st = np.ones((WIDE, WIDE), bool)
    rp = np.pad(r, p, mode="edge")
    w = ndi.binary_closing(ndi.binary_opening(rp, structure=st), structure=st)[p:-p, p:-p] & r
    nar = r & ~w
    nf = 0
    if nar.any() and w.any():
        lab, k = ndi.label(nar, structure=EIGHT)
        far = np.bincount(lab[nar & ~ndi.binary_dilation(w, structure=EIGHT)], minlength=k + 1)
        fringe = far == 0
        fringe[0] = False
        if fringe.any():
            f = fringe[lab]
            nf = int(f.sum())
            w |= f
            nar &= ~f
    npool = 0
    if nar.any() and w.any():
        lab, k = ndi.label(w, structure=EIGHT)
        size = np.bincount(lab.ravel(), minlength=k + 1)
        touch = np.bincount(lab[ndi.binary_dilation(nar, structure=EIGHT) & w], minlength=k + 1) > 0
        edge = np.zeros(k + 1, bool)
        edge[np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]])] = True
        pool = (size < WIDE_MIN) & touch & ~edge
        pool[0] = False
        if pool.any():
            p_ = pool[lab]
            npool = int(p_.sum())
            w &= ~p_
            nar |= p_
    return w, nar, nf, npool


TRIM_FACTOR = 1.25     # version 4: river water further from its course than this many times the river's half width
TRIM_LENGTH = 3        # (its running median over TRIM_WINDOW_M) is land, unless the widening is longer than this
TRIM_WINDOW_M = 2000   # many river widths (a real widening), or touches a lake or the sea


def course_paths(lat0, lon0, hp, wp):
    """Version 4: the courses through the tile as pixel paths (rows, columns inside it), every quarter pixel."""
    key = (lat0 + 90) * 360 + (lon0 + 180)
    lo, hi = np.searchsorted(G["c_tsorted"], [key, key + 1])
    if lo == hi:
        return []
    st = np.sort(G["c_order"][lo:hi])
    reach = G["c_reach"]
    out = []
    for run in np.split(st, np.flatnonzero((np.diff(st) != 1) | (np.diff(reach[st]) != 0)) + 1):
        if len(run) < 2:
            continue
        pr = (lat0 + 1 - G["c_lat"][run]) * hp
        pc = (G["c_lon"][run] - lon0) * wp
        pts = []
        for k in range(len(run) - 1):
            n = max(1, int(math.ceil(4 * max(abs(pr[k + 1] - pr[k]), abs(pc[k + 1] - pc[k])))))
            f = np.arange(n) / n
            pts.append(np.column_stack([pr[k] + (pr[k + 1] - pr[k]) * f, pc[k] + (pc[k + 1] - pc[k]) * f]))
        q = np.floor(np.vstack(pts)).astype(np.int64)
        q = q[(q[:, 0] >= 0) & (q[:, 0] < hp) & (q[:, 1] >= 0) & (q[:, 1] < wp)]
        if len(q) > 1:
            keep = np.ones(len(q), bool)
            keep[1:] = np.any(np.diff(q, axis=0) != 0, axis=1)
            out.append(q[keep])
    return out


def trim_bulges(river, lakesea, lat0, lon0):
    """Version 4 (decision 0069 rule 5): the river water's short bulges as land. Along each course in the water, the
    river's half width (the distance from the course to the shore) is taken as a running median over TRIM_WINDOW_M;
    water further from the course than TRIM_FACTOR times it is a bulge, kept where it is longer along the course than
    TRIM_LENGTH river widths (a real widening, a side arm) or touches a lake or the sea. The mask marks a river unevenly
    (the Lielupe 511 m across at 23.74 E, 265 m at 23.77 E): its wider pieces read as ponds along the river."""
    from scipy.ndimage import median_filter
    hp, wp = river.shape
    dy = 111320.0 / hp
    dx = 111320.0 * max(math.cos(math.radians(lat0 + 0.5)), 0.05) / wp
    paths = course_paths(lat0, lon0, hp, wp)
    if not paths:
        return river, 0
    shore = ndi.distance_transform_edt(river, sampling=(dy, dx))
    cp = np.zeros(river.shape, bool)
    hwmed = np.zeros(river.shape)
    for q in paths:
        hw = shore[q[:, 0], q[:, 1]]
        step = max(1.0, float(np.mean(np.hypot(np.diff(q[:, 0]) * dy, np.diff(q[:, 1]) * dx)))) if len(q) > 1 else dy
        k = max(3, int(TRIM_WINDOW_M / step) | 1)
        med = median_filter(hw, size=k, mode="nearest")
        wet = hw > 0
        cp[q[wet, 0], q[wet, 1]] = True
        hwmed[q[wet, 0], q[wet, 1]] = np.maximum(hwmed[q[wet, 0], q[wet, 1]], med[wet])
    if not cp.any():
        return river, 0
    dist, (ir, ic) = ndi.distance_transform_edt(~cp, sampling=(dy, dx), return_indices=True)
    hwn = hwmed[ir, ic]
    lab, nb = ndi.label(river, structure=EIGHT)
    with_course = np.zeros(nb + 1, bool)
    with_course[np.unique(lab[cp])] = True
    with_course[0] = False
    cand = river & with_course[lab] & (hwn > 0) & (dist > TRIM_FACTOR * hwn)
    if not cand.any():
        return river, 0
    near_ls = ndi.binary_dilation(lakesea, structure=EIGHT)
    cl, nc = ndi.label(cand, structure=EIGHT)
    out = river.copy()
    trimmed = 0
    objs = ndi.find_objects(cl)
    for k, sl in enumerate(objs, 1):
        m = cl[sl] == k
        if (near_ls[sl] & m).any():
            continue
        cps = np.unique((ir[sl][m] * wp + ic[sl][m]))
        length = len(cps) * (dy + dx) / 2
        width = 2 * float(np.median(hwn[sl][m]))
        if length < TRIM_LENGTH * width:
            out[sl][m] = False
            trimmed += int(m.sum())
    return out, trimmed


def masks_v3(path):
    """A tile's water as version 3 takes it (None without rivers): wbm, dem (with the corridors' levels), own (the mask's
    river pixels, which give the levels), br (bridges), river (with bridges and joined corners), joined (pixels),
    wide, nar, fringe (pixels), lakesea."""
    name = os.path.basename(path)
    wbm = tifffile.imread(path)
    river = wbm == 3
    if name in G["res_names"]:
        river.ravel()[G["res"][name]] = False
    cor = name in G["cor_names"]
    if cor:
        river.ravel()[G["cor"][name]] = True
    if not river.any():
        return None
    br = bridges(wbm, river)
    dem = tifffile.imread(os.path.join(DEM, name.replace("_WBM.tif", "_DEM.tif"))).astype(np.float64)
    if cor:
        dem.ravel()[G["cor"][name]] = G["cor"][name + "|lv"]
    lakesea = (wbm == 1) | (wbm == 2)
    own = river.copy()
    # rule 2.1: of the two pixels across a corner contact, a lake's or the sea's first (water already), else the lower
    joined, nj = join_corners(river | br, np.where(lakesea, -1e9, dem))
    # (0069 rule 5, not built: at the mask's 90 m it either barely touches the ponds or notches the far bank where a
    # course swings; EARTH_TRIM_BULGES=1 runs it, for study)
    if G.get("v4") and os.environ.get("EARTH_TRIM_BULGES") == "1":
        lat0, lon0 = tile_origin(name)
        joined, _ = trim_bulges(joined, lakesea, lat0, lon0)
    wide, nar, nf, npool = split_wide(joined)
    return dict(name=name, wbm=wbm, dem=dem, own=own, br=br, river=joined, joined=nj, wide=wide, nar=nar, fringe=nf,
                pool=npool, lakesea=lakesea)


def medians(lab, k, sel, d):
    """Each label's median (upper, as version 2) of d over its pixels in sel; NaN for a label with none."""
    out = np.full(k + 1, np.nan)
    lf = np.where(sel, lab, 0).ravel()
    m = lf > 0
    if m.any():
        n = np.bincount(lf, minlength=k + 1)[1:]
        ds = d.ravel()[m][np.lexsort((d.ravel()[m], lf[m]))]
        starts = np.concatenate([[0], np.cumsum(n)[:-1]])
        has = n > 0
        out[1:][has] = ds[starts[has] + n[has] // 2]
    return out


def levels(lab, k, own, d):
    """Each piece's level in decimetres: the median GLO-90 of its own river pixels (any of its pixels if it has none)."""
    lv = medians(lab, k, own, d)
    miss = np.isnan(lv[1:])
    if miss.any():
        lv[1:][miss] = medians(lab, k, lab > 0, d)[1:][miss]
    return np.round(np.nan_to_num(lv) * 10).astype(np.int64)


def wide_field(wk):
    """The wide water's pixels blurred by SIGMA (the tile's edge pixels repeated beyond it), the blur fading in over
    EDGE_TAPER pixels from the tile's edge: on the edge the field is the pixels' own (0 or 1)."""
    hp, wp = wk.shape
    f = wk.astype(np.float32)
    p = int(4 * SIGMA) + 2
    b = ndi.gaussian_filter(np.pad(f, p, mode="edge"), SIGMA, truncate=4.0)[p:-p, p:-p]
    iy = np.minimum(np.arange(hp), np.arange(hp)[::-1])
    ix = np.minimum(np.arange(wp), np.arange(wp)[::-1])
    t = np.clip(np.minimum(iy[:, None], ix[None, :]) / EDGE_TAPER, 0, 1).astype(np.float32)
    return f + t * (b - f)


def contour_polys(field, wp, hp, tol):
    """The 0.5 contour of a tile's field (zero beyond the tile), in contour coordinates (pixel centres at 1..wp, 1..hp;
    the tile's edges at 0.5 and wp + 0.5): a list of polygons, each [outer ring, holes...], simplified by tol."""
    z = np.zeros((hp + 2, wp + 2), np.float32)
    z[1:-1, 1:-1] = field
    polys, offs = contourpy.contour_generator(z=z, name="serial", fill_type="OuterOffset", corner_mask=False).filled(0.5, 1.5)
    out = []
    for pts, off in zip(polys, offs):
        rings = []
        for i in range(len(off) - 1):
            ring = simplify_ring(pts[off[i]:off[i + 1]], wp, hp, tol)
            if ring is None:
                if i == 0:
                    break
                continue
            rings.append(ring)
        if rings:
            out.append(rings)
    return out


def nearest_label(lab):
    """Each pixel's nearest labelled pixel's label."""
    if lab.all():
        return lab
    idx = ndi.distance_transform_edt(lab == 0, return_distances=False, return_indices=True)
    return lab[idx[0], idx[1]]


def owners(polys, near, wp, hp):
    """The label under each polygon's representative point (nearest_label map)."""
    out = []
    for g in polys:
        pt = shapely.point_on_surface(g if g.is_valid else shapely.make_valid(g))
        if pt.is_empty:
            out.append(0)
            continue
        cx = min(max(int(round(pt.x)) - 1, 0), wp - 1)
        cy = min(max(int(round(pt.y)) - 1, 0), hp - 1)
        out.append(int(near[cy, cx]))
    return out


def to_units(ring, wp, hp):
    """A ring in contour coordinates as UNIT3 units from the tile's south-west corner (repeats dropped), or None."""
    X = np.round((ring[:, 0] - 0.5) * UNIT3 / wp).astype(np.int64)
    Y = np.round((hp - (ring[:, 1] - 0.5)) * UNIT3 / hp).astype(np.int64)
    q = np.column_stack([X, Y])
    q = q[np.any(q != np.roll(q, 1, axis=0), axis=1)]
    return q if len(q) >= 3 else None


def poly_rings(g):
    """A shapely polygon's rings (outer first), open, exterior oriented as contourpy's."""
    g = shapely.geometry.polygon.orient(g, 1.0)
    return [np.asarray(g.exterior.coords)[:-1]] + [np.asarray(h.coords)[:-1] for h in g.interiors]


def tile_v3(path):
    """Version 3 (0063 rule 2): wide water contoured smooth, narrow pieces as before with FLAG_NARROW."""
    name = os.path.basename(path)
    lat0, lon0 = tile_origin(path)
    if lat0 < SOUTH:
        return None
    m = masks_v3(path)
    if m is None:
        return dict(name=name, n=0, blob=b"", nv=0, raw=0, px=0, br=(0, 0), stats={})
    wbm, dem, own, br, river, wide, nar, lakesea = (m[k] for k in ("wbm", "dem", "own", "br", "river", "wide", "nar", "lakesea"))
    hp, wp = wbm.shape
    nbr = (int(br.sum()), int(ndi.label(br, structure=EIGHT)[1])) if br.any() else (0, 0)
    rows_lat = lat0 + 1 - (np.arange(hp) + 0.5) / hp
    px_km2 = (111.32 / hp) * (111.32 / wp) * np.cos(np.radians(rows_lat))
    water = np.where(river | lakesea, -1e9, dem)      # corner joins of the outlines' masks: water first, else the lower
    pieces, st = [], dict(wide_px=int(wide.sum()), narrow_px=int(nar.sum()), joined_px=m["joined"], fringe_px=m["fringe"],
                          pool_px=m["pool"],
                          wide_km2=float(wide.sum(axis=1) @ px_km2), narrow_km2=float(nar.sum(axis=1) @ px_km2),
                          wide_pieces=0, narrow_pieces=0, wide_polys=0, narrow_polys=0, wide_nv=0, narrow_nv=0)
    inner = None
    if wide.any():
        # wide water with a pixel of the lakes and sea it touches, blurred and contoured smooth
        wk, _ = join_corners(wide | (ndi.binary_dilation(wide, structure=EIGHT) & lakesea), water)
        wl, nw = ndi.label(wk, structure=EIGHT)
        lv = levels(wl, nw, own & wide, dem)
        polys = [shapely.Polygon(r[0], r[1:]) for r in contour_polys(wide_field(wk), wp, hp, WIDE_TOL)]
        own_ = owners(polys, nearest_label(wl), wp, hp)
        st["wide_pieces"] = nw
        for g, o in zip(polys, own_):
            rings = [q for q in (to_units(r, wp, hp) for r in poly_rings(g)) if q is not None]
            if not rings or o == 0:
                continue
            st["wide_polys"] += 1
            st["wide_nv"] += sum(len(r) for r in rings)
            pieces.append(lake_bytes(int(lv[o]), 0, rings))
        if polys:
            u = shapely.union_all(shapely.make_valid(np.array(polys, dtype=object)))
            inner = u.buffer(-OVERLAP)
            if inner.is_empty:
                inner = None
            else:
                shapely.prepare(inner)
    if nar.any():
        # narrow pieces with a pixel of the lakes, the sea and the wide water they touch, at the pixels' edges as before,
        # cut back to OVERLAP inside the wide water's outline
        nk_, _ = join_corners(nar | (ndi.binary_dilation(nar, structure=EIGHT) & (lakesea | wide)), water)
        nl, nn = ndi.label(nar, structure=EIGHT)
        lv = levels(nl, nn, own & nar, dem)
        near = nearest_label(nl)
        st["narrow_pieces"] = nn
        out = []
        for r in contour_polys(nk_, wp, hp, TOL):
            g = shapely.Polygon(r[0], r[1:])
            gv = g if g.is_valid else shapely.make_valid(g)
            if inner is not None and inner.intersects(gv):
                d = gv.difference(inner)
                out.extend(p for p in getattr(d, "geoms", [d]) if p.geom_type == "Polygon" and p.area > 0.02)
            else:
                out.append(g)
        for g, o in zip(out, owners(out, near, wp, hp)):
            rings = [q for q in (to_units(r, wp, hp) for r in poly_rings(g)) if q is not None]
            if not rings or o == 0:
                continue
            st["narrow_polys"] += 1
            st["narrow_nv"] += sum(len(r) for r in rings)
            pieces.append(lake_bytes(int(lv[o]), FLAG_NARROW, rings))
    nv = st["wide_nv"] + st["narrow_nv"]
    if not pieces:
        return dict(name=name, n=0, blob=b"", nv=0, raw=0, px=int(river.sum()), br=nbr, stats=st)
    raw = varints([len(pieces)]) + b"".join(pieces)
    return dict(name=name, n=len(pieces), blob=brotli.compress(raw, quality=11, lgwin=22), nv=nv, raw=len(raw),
                px=int(river.sum()), br=nbr, stats=st)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    pos, opt = [], {}
    args = sys.argv[1:]
    while args:
        a = args.pop(0)
        if a.startswith("--"):
            opt[a[2:]] = args.pop(0)
        else:
            pos.append(a)
    out = pos[0]
    workers = int(pos[1]) if len(pos) > 1 else 16
    filt = pos[2] if len(pos) > 2 else ""
    os.makedirs(out, exist_ok=True)
    res = opt.get("reservoirs") or find_reservoirs(out, workers)
    cor = opt.get("corridors") or os.path.join(os.path.dirname(res), "corridors.npz")
    if not os.path.exists(cor):
        if "corridors" in opt:
            raise SystemExit(f"no corridors at {cor}")
        cor = None
    # version 2 (0053 item 19): the dam bake's reservoirs, with its river corridors through them; 3 (0063 rule 2) the same
    # with corner contacts joined, wide water smooth and narrow pieces flagged
    version = int(opt.get("version", VERSION if cor else 1))
    unit = UNIT3 if version >= 3 else UNIT
    tiles = sorted(p for p in glob.glob(os.path.join(WBM, "*_WBM.tif")) if any(f in os.path.basename(p) for f in filt.split(",")))
    log(f"version {version}: {len(tiles)} tiles, {workers} workers; reservoirs {res}; corridors {cor}")
    TX, TY = 360, 180
    states = np.full(TX * TY, 255, np.uint8)
    blobs = {}
    npc = nv = nraw = npx = nbp = nbn = 0
    tot = {}
    with Pool(workers, initializer=init_worker, initargs=(res, cor, version)) as pool:
        for i, r in enumerate(pool.imap_unordered(tile, tiles, chunksize=4), 1):
            if i % 2000 == 0:
                log(f"{i}/{len(tiles)} tiles")
            if r is None:
                continue
            lat0, lon0 = tile_origin(r["name"])
            t = (lat0 + 90) * TX + (lon0 + 180)
            states[t] = 1 if r["n"] else 0
            npx += r["px"]
            nbp += r["br"][0]
            nbn += r["br"][1]
            for k, v in r.get("stats", {}).items():
                tot[k] = tot.get(k, 0) + v
            if r["n"]:
                blobs[t] = r["blob"]
                npc += r["n"]; nv += r["nv"]; nraw += r["raw"]
    head = struct.pack("<4siiiii", b"EMRO", version, unit, 1, TX, TY)
    index = bytearray()
    o = len(head) + TX * TY + 8 * len(blobs)
    body = bytearray()
    for t in sorted(blobs):
        index += struct.pack("<ii", o + len(body), len(blobs[t]))
        body += blobs[t]
    data = head + states.tobytes() + bytes(index) + bytes(body)
    fname = f"rivers-outline-glo90-{version}.bin"
    with open(os.path.join(out, fname), "wb") as f:
        f.write(data)
    sha = hashlib.sha256(data).hexdigest()
    rz = np.load(res)
    info = {"file": fname, "version": version, "unit": unit, "bytes": len(data), "sha256": sha, "pieces": npc,
            "vertices": nv, "raw_bytes": nraw, "river_pixels": npx,
            "reservoir_river_pixels_left_out": int(sum(len(rz[f]) for f in rz.files)), "tiles_with_rivers": len(blobs),
            "tiles_covered": int((states != 255).sum()), "bridge_pixels": nbp, "bridge_pieces": nbn,
            "reservoirs": os.path.abspath(res), "corridors": os.path.abspath(cor) if cor else None,
            "seconds": round(time.time() - T0, 1)}
    if version >= 3:
        info.update({k: round(v, 1) if isinstance(v, float) else v for k, v in tot.items()})
    with open(os.path.join(out, fname.replace(".bin", ".json")), "w", encoding="utf8") as f:
        json.dump(info, f, indent=1)
    log(json.dumps(info))


if __name__ == "__main__":
    main()
