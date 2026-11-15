"""
Bakes free global datasets into the compact files the Earth Map mod reads at runtime.

Inputs (rawdata/, see tools/download_data.sh):
  - ETOPO 2022 60 arc-second surface + bed elevation (NOAA NCEI, public domain)
  - University of Delaware air temperature & precipitation v5.01 monthly normals (NOAA PSL)
  - NCEP/NCAR reanalysis 2 m air temperature monthly normals 1991-2020 (NOAA PSL), used over the oceans
  - GPCP v2.3 precipitation monthly normals 1991-2020 (NOAA PSL), used over the oceans
  - Natural Earth 10m lakes (+ Europe and North America supplements), public domain
  - GSHHG 2.3.7 full-resolution shorelines (Wessel & Smith, LGPL), for the islands

Outputs (earthdata/):
  - terrain.bin   2 arc-minute grid in 6° tiles: elevation, water level, flags, ice thickness, roughness
  - climate.bin   0.25° grid: sea-level-equivalent temperature (mean, coldest, warmest month), precipitation, VS rain
                  (from a cut-short UDel file, kept for existing worlds); climatefix.bin (v1), climatefix2.bin (v2,
                  the complete UDel normals) and climatefix3.bin (v3, UDel faded in over a 1° coast band) hold the
                  cells newer worlds change (`--climate-fix-only [1 2 3 4]`, decision 0018); climatefix4.bin (v4, the
                  GPCP share at desert coasts at CHELSA's level, EA-0060)
  - weatherground.bin  0.5° grid: the height weather.bin's sea-level temperatures were reduced from (weather rules 7,
                  EA-0021; `--weather-ground-only`)
  - earth.json    grid description, seam path (longitude of the cut by latitude), attribution
  - islands.bin   islands smaller than BIG_LAND_KM2 as outlines (built in game where they are wide enough in blocks), and
                  the grid cells that change when their land is taken out of the grid
  - lakes.bin     the terminal lakes filled (worlds with river ends): the grid cells that change
  - shores.bin    the land around lakes eased below the lake's level instead of raised to it (worlds with LakeShores):
                  the grid cells that change
  - outlets.bin   the lakes' tongues past their outlet down the outflow river as land (worlds with LakeOutlets; Lake
                  Erie's upper Niagara): the grid cells that change. `--outlets-report` lists them without baking

Everything is stored in geographic coordinates (equirectangular lat/lon), so any world size can use it.
"""
import json, math, os, struct, sys, time, zlib

import brotli
from collections import defaultdict, deque
import numpy as np
import netCDF4
import scipy.ndimage as ndi
import shapefile
import tifffile
from PIL import Image, ImageDraw
from scipy.interpolate import RegularGridInterpolator

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import planecoding as pc
RAW = os.path.join(ROOT, "rawdata")
OUT = os.path.join(ROOT, "earthdata")
PREVIEW = os.path.join(ROOT, "preview")

SRC_RES = 60          # ETOPO cells per degree (1')
RES = 30              # baked terrain cells per degree (2')
TILE = 180            # cells per tile side (6°)
CLIM_RES = 4          # climate cells per degree (0.25°)

T0 = time.time()


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


# ---------------------------------------------------------------------------------------------- elevation

def load_etopo():
    s = tifffile.imread(os.path.join(RAW, "ETOPO_2022_v1_60s_N90W180_surface.tif")).astype(np.float32)
    b = tifffile.imread(os.path.join(RAW, "ETOPO_2022_v1_60s_N90W180_bed.tif")).astype(np.float32)
    assert s.shape == (180 * SRC_RES, 360 * SRC_RES), s.shape
    return s, b


def rc(lat, lon, res):
    """Row/col of the cell containing (lat, lon) in a global grid with `res` cells per degree, row 0 at 90°N."""
    r = int((90 - lat) * res)
    c = int(((lon + 180) % 360) * res)
    return min(max(r, 0), 180 * res - 1), c % (360 * res)


def label_wrapped(mask):
    """8-connected components with the east and west edges joined."""
    lab, n = ndi.label(mask, structure=np.ones((3, 3), bool))
    parent = np.arange(n + 1)

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    h = mask.shape[0]
    for r in range(h):
        a = lab[r, 0]
        if a == 0:
            continue
        for dr in (-1, 0, 1):
            rr = r + dr
            if 0 <= rr < h:
                b = lab[rr, -1]
                if b:
                    ra, rb = find(a), find(b)
                    if ra != rb:
                        parent[max(ra, rb)] = min(ra, rb)
    roots = np.array([find(i) for i in range(n + 1)])
    return roots[lab], n


def rasterize_lakes(shape_hw):
    """Natural Earth lakes -> (id raster at 1', list of lake dicts). Natural lakes only (no reservoirs)."""
    h, w = shape_hw
    img = Image.new("I", (w, h), 0)
    draw = ImageDraw.Draw(img)
    lakes = [None]
    files = ["ne_10m_lakes", "ne_10m_lakes_europe", "ne_10m_lakes_north_america"]
    for f in files:
        r = shapefile.Reader(os.path.join(RAW, "ne_lakes", f), encoding="utf-8")
        for sr in r.iterShapeRecords():
            rec = sr.record.as_dict()
            cla = rec.get("featurecla") or ""
            name = rec.get("name") or ""
            shp = sr.shape
            if not shp.points:
                continue
            xs = [p[0] for p in shp.points]
            ys = [p[1] for p in shp.points]
            cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
            if cy < -60:
                continue  # subglacial Antarctic lakes
            salt = False
            if cla == "Reservoir":
                continue
            if cla == "Alkaline Lake":
                # mostly dry salt pans in Australia; the Salton Sea only exists since 1905
                if (110 < cx < 155 and -45 < cy < -10) or "Salton" in name:
                    continue
                salt = True
            if any(k in name for k in ("Dead Sea", "Great Salt", "Urmia", "Van", "Aral", "Balkhash", "Qinghai", "Issyk")):
                salt = True
            lid = len(lakes)
            lakes.append({"name": name, "salt": salt, "src": f, "wikidata": rec.get("wikidataid") or ""})
            parts = list(shp.parts) + [len(shp.points)]
            rings = []
            for i in range(len(parts) - 1):
                pts = shp.points[parts[i]:parts[i + 1]]
                # signed area: shapefile outer rings are clockwise (negative in lon/lat axes)
                a = 0.0
                for j in range(len(pts)):
                    x1, y1 = pts[j]
                    x2, y2 = pts[(j + 1) % len(pts)]
                    a += x1 * y2 - x2 * y1
                rings.append((a < 0, pts))
            for outer, pts in rings:
                if outer:
                    draw.polygon([((x + 180) * SRC_RES, (90 - y) * SRC_RES) for x, y in pts], fill=lid)
            for outer, pts in rings:
                if not outer:
                    draw.polygon([((x + 180) * SRC_RES, (90 - y) * SRC_RES) for x, y in pts], fill=0)
    return np.array(img, dtype=np.int32), lakes


GSHHG_L1 = os.path.join(RAW, "gshhg", "GSHHS_shp", "f", "GSHHS_f_L1")   # GSHHG 2.3.7 full resolution, land/ocean
BIG_LAND_KM2 = 1000.0   # land this big stays in the 2' grid; smaller islands go to islands.bin as outlines
MARGIN_ROWS = 2         # 1' rows (1.85 km each) around big land where ETOPO's land stays: one baked 2' cell


def read_gshhg():
    """GSHHG full-resolution shorelines between land and sea: [(area km², points as an (n, 2) lon/lat array)]."""
    out = []
    r = shapefile.Reader(GSHHG_L1)
    for sr in r.iterShapeRecords():
        pts = np.asarray(sr.shape.points, np.float64)
        if len(pts) >= 3:
            out.append((float(sr.record.as_dict()["area"]), pts))
    log(f"GSHHG: {len(out)} land polygons, {sum(1 for a, _ in out if a >= BIG_LAND_KM2)} of them big")
    return out


def big_land_allowed(shape_hw, polys):
    """Where ETOPO's land may stay in the grid: the big land (BIG_LAND_KM2) and ~3.7 km around it, as wide east-west as
    north-south at every latitude (in 1' columns: far north a column is only 0.3 km wide), as a bool raster at 1'."""
    h, w = shape_hw
    img = Image.new("L", (w, h), 0)
    draw = ImageDraw.Draw(img)
    for area, pts in polys:
        if area >= BIG_LAND_KM2:
            draw.polygon([((x + 180) * SRC_RES, (90 - y) * SRC_RES) for x, y in pts], fill=1, outline=1)
    near = ndi.maximum_filter1d(np.array(img, dtype=np.uint8), 2 * MARGIN_ROWS + 1, axis=0)
    lat = 90 - (np.arange(h) + 0.5) / SRC_RES
    k = np.ceil(MARGIN_ROWS / np.maximum(np.cos(np.radians(lat)), 0.02)).astype(int)
    for kk in np.unique(k):
        rows = k == kk
        near[rows] = ndi.maximum_filter1d(near[rows], min(2 * kk + 1, w), axis=1, mode="wrap")
    return near > 0


NATURAL_LAKE_CLASSES = {
    "lake", "lake water body", "lake or pond", "salt lake", "glacial lake", "endorheic lake", "volcanic crater lake",
    "rift lake", "finger lake", "soda lake", "impact crater lake", "hypersaline lake", "liman", "monomictic lake",
    "bifurcation lake", "freshwater lake", "meromictic lake", "tectonic lake", "moraine-dammed lake", "ancient lake",
    "mountain lake", "lagoon", "pluvial lake", "oxbow lake", "crater lake", "group of lakes", "intermittent lake",
}
# Origin evidence differs from salinity, location and mixing regime. A salt lake can be artificial.
# Preserve demonstrably natural basins later regulated by dams; generic/physical lake tags cannot veto
# an explicit artificial-lake classification (Razzaza's {salt lake, artificial lake} exposed this bug).
NATURAL_ORIGIN_CLASSES = {
    "glacial lake", "volcanic crater lake", "rift lake", "finger lake", "impact crater lake", "liman",
    "tectonic lake", "moraine-dammed lake", "ancient lake", "lagoon", "pluvial lake", "oxbow lake", "crater lake",
}
# natural lakes that were dammed later, which HydroLAKES lists as reservoirs under names that don't say "lake"
# (names can't tell them apart from reservoirs like Kikuletwa = Nyumba ya Mungu, 1965)
DAMMED_NATURAL_LAKES = {"hungtze", "skagern", "red indian", "kempt"}
NATURAL_WORDS = ("lake", "lac ", "lac-", "lago", "laguna", "see", "järvi", "jarvi", "vatn", "sjø", "sjön", "sjon",
                 "ozero", "göl", "loch", "lough", "nuur", "tso", " hu")
RESERVOIR_WORDS = ("reservoir", "res.", "vdkhr", "vodokhran", "embalse", "represa", "barragem", "barrage", "stausee",
                   "talsperre", "shuiku", "dam", "pool", "reservoar", "magasin", "krātuve")


def drop_reservoir_lakes(lake_id, lakes, S):
    """
    Natural Earth tags many reservoirs as lakes (Pļaviņas on the Daugava, most US "... Lake" reservoirs). Modern dams
    are left out of the world, so these are removed (the ground stays at ETOPO's flat reservoir surface; the rivers
    through them are de-dammed in bake_rivers.py). Natural lakes that are only regulated by a dam (Mjøsa, Inari, Cayuga
    Lake, Utah Lake) stay.
     - With a Wikidata id (rawdata/lake_types.json from tools/fetch_lake_types.py): removed when Wikidata calls it a
       reservoir or artificial lake and not also some kind of natural lake.
     - Without: removed when a HydroLAKES reservoir (Lake_type 2; its pour point is the dam) lies inside or right next
       to it, the lake is not much larger than that reservoir, and HydroLAKES doesn't give it a lake's name
       (HydroLAKES also counts dammed natural lakes as reservoirs; those keep names like "Moosehead Lake").
    Dammed sea arms (the IJsselmeer, the Lauwerszee) become sea again, as before their dikes.
    """
    types_path = os.path.join(RAW, "lake_types.json")
    types = json.load(open(types_path, encoding="utf-8")) if os.path.exists(types_path) else {}
    dropped = {}
    for lid in range(1, len(lakes)):
        q = lakes[lid]["wikidata"] if lakes[lid] else ""
        cl = set(types.get(q, []))
        if ("artificial lake" in cl and not cl & NATURAL_ORIGIN_CLASSES
                or "reservoir" in cl and not cl & NATURAL_LAKE_CLASSES):
            dropped[lid] = lakes[lid]["name"]
    n_wiki = len(dropped)
    shp = os.path.join(RAW, "hydrolakes", "HydroLAKES_points_v10_shp", "HydroLAKES_points_v10.shp")
    if os.path.exists(shp):
        r = shapefile.Reader(shp, encoding="latin1", encodingErrors="replace")
        H1, W1 = lake_id.shape
        counts = np.bincount(lake_id.ravel(), minlength=len(lakes))
        for rec in r.iterRecords(fields=["Lake_name", "Lake_type", "Lake_area", "Pour_long", "Pour_lat"]):
            d = rec.as_dict()
            if d["Lake_type"] != 2 or d["Lake_area"] < 0.5:
                continue
            hl_name = (d["Lake_name"] or "").strip().lower()
            # a dammed natural lake keeps a lake's name (Moosehead Lake); a reservoir often has only a river's or
            # place's (Kikuletwa: Nyumba ya Mungu, 1965)
            if (any(w in hl_name for w in NATURAL_WORDS) and not any(w in hl_name for w in RESERVOIR_WORDS)) or hl_name in DAMMED_NATURAL_LAKES:
                continue
            rr, cc = rc(d["Pour_lat"], d["Pour_long"], SRC_RES)
            cols = [(cc + k) % W1 for k in range(-2, 3)]
            ids = np.unique(lake_id[max(rr - 2, 0):rr + 3][:, cols])
            for lid in ids[ids > 0]:
                lid = int(lid)
                if lid in dropped or not lakes[lid] or types.get(lakes[lid]["wikidata"]):
                    continue  # Wikidata already decided
                # 1' cell area in km2 at this latitude
                cell_km2 = 1.853 * 1.853 * max(0.05, np.cos(np.radians(d["Pour_lat"])))
                if counts[lid] * cell_km2 <= 5 * d["Lake_area"] + 50:
                    dropped[lid] = lakes[lid]["name"]
    else:
        log("HydroLAKES not found, only Wikidata decides which lakes are reservoirs")
    if dropped:
        # lakes dammed off from the sea (the IJsselmeer and Lauwerszee behind their dikes) go back to being sea:
        # below sea level and next to it, they join the sea when the ocean is labelled
        lake_id[np.isin(lake_id, np.fromiter(dropped.keys(), np.int32))] = 0
    names = sorted(n for n in dropped.values() if n)
    log(f"dropped {len(dropped)} reservoir lakes ({n_wiki} by Wikidata), e.g. {names[:12]}")


# Outlet tongues (outlets.bin, worlds with EarthParams.LakeOutlets). Some Natural Earth lakes run on down their outflow
# river past the lake's outlet: Lake Erie's polygon holds the whole upper Niagara River, Grand Island as a hole, and ends
# 3 km below the falls, over the gorge. The grid then has a lake at the upper lake's level kilometres wide where the river
# is, the river's fall stands at the lake's edge and the lower river runs along the raised lake. HydroLAKES marks each
# lake's outlet (its pour point) and HydroRIVERS the river out of it.
OUTLET_GATES_KM = (2.0, 3.0, 4.5, 6.0, 8.0)   # gate radii tried, on the course this far past the pour point
OUTLET_MIN_COURSE_KM = 3.0                    # course past the outlet still in the lake before a tongue counts
OUTLET_COURSE_GAP_KM = 5.0                    # the course is followed across this much land between the lake's pieces
OUTLET_NEAR_KM = 4.0                          # pieces the raster cut off within this of the tongue, nearer it than the lake, join it
OUTLET_LAKE_SHARE = 0.25                      # a tongue with HydroLAKES lakes over this share of it is a lake chain
OUTLET_MAX_WIDTH_KM = 3.5                     # a tongue wider on average (area / course by it) is lake, not river
                                              # (the upper Niagara 3.3 km, both channels round Grand Island; Päijänne's
                                              # 9.3 km holds Ruotsalainen and Konnivesi)
OUTLET_PIECE_INNER_KM = 2.5                   # a detached piece joins the tongue only if no cell of it is further from its
                                              # shore than this: a channel, not a lake beside the river


def outlet_tongues(lake_id, lakes):
    """
    The 1' lake cells of Natural Earth lakes that lie past the lake's outlet along its outflow river (a boolean raster),
    and a report per tongue.
     - The outlet is the pour point of the largest natural HydroLAKES lake (types 1 and 3) whose pour point is in the NE
       lake; the outflow is the biggest HydroRIVERS reach (5 m³/s or more) starting within 0.03° of it, followed downstream
       while it is in the lake (gaps of up to OUTLET_COURSE_GAP_KM).
     - A gate disk on that course, as far past the pour point as its radius (2 km, then wider up to 8 km), cuts the lake.
       The lake parts it cuts off from the lake's main body that hold the course further on are the tongue, with the
       gate's cells nearer to them than to the main body. Detached channel pieces (none wider inside than
       OUTLET_PIECE_INNER_KM) within OUTLET_NEAR_KM, nearer the tongue than the main body, go with it.
     - A tongue holding HydroLAKES lakes (pour points in it or within two cells) over OUTLET_LAKE_SHARE of its area is a
       chain of lakes on the river (Playgreen Lake below Lake Winnipeg, Clearwater Lake BC split in two) and stays, as
       does one wider than OUTLET_MAX_WIDTH_KM on average.
    """
    H1, W1 = lake_id.shape
    out = np.zeros(lake_id.shape, bool)
    shp = os.path.join(RAW, "hydrolakes", "HydroLAKES_points_v10_shp", "HydroLAKES_points_v10.shp")
    rshp = os.path.join(RAW, "hydrorivers", "HydroRIVERS_v10_shp", "HydroRIVERS_v10.shp")
    if not (os.path.exists(shp) and os.path.exists(rshp)):
        log("HydroLAKES or HydroRIVERS not found: no outlet tongues")
        return out, []
    hl = shapefile.Reader(shp, encoding="latin1", encodingErrors="replace")
    best = {}
    pts_all, area_all = [], []
    for rec in hl.iterRecords(fields=["Hylak_id", "Lake_name", "Lake_type", "Lake_area", "Pour_long", "Pour_lat"]):
        d = rec.as_dict()
        if d["Lake_area"] >= 1:
            pts_all.append((d["Pour_long"], d["Pour_lat"]))
            area_all.append(d["Lake_area"])
        if d["Lake_area"] < 5 or d["Lake_type"] == 2:
            continue
        rr, cc = rc(d["Pour_lat"], d["Pour_long"], SRC_RES)
        win = lake_id[max(rr - 1, 0):rr + 2][:, [(cc + k) % W1 for k in (-1, 0, 1)]]
        ids, cnt = np.unique(win[win > 0], return_counts=True)
        if len(ids) == 0:
            continue
        lid = int(ids[np.argmax(cnt)])
        if lid not in best or d["Lake_area"] > best[lid]["Lake_area"]:
            best[lid] = d
    from scipy.spatial import cKDTree
    hl_pts = np.array(pts_all)
    hl_area = np.array(area_all)
    hl_tree = cKDTree(hl_pts)

    rv = shapefile.Reader(rshp)
    reach = {}
    for i, rec in enumerate(rv.iterRecords(fields=["HYRIV_ID", "NEXT_DOWN", "DIS_AV_CMS"])):
        if rec[2] >= 5:
            reach[rec[0]] = (i, rec[1], rec[2])
    ids = np.fromiter(reach.keys(), np.int64)
    first = np.array([rv.shape(reach[int(rid)][0]).points[0] for rid in ids])
    tree = cKDTree(first)
    log(f"outlet tongues: {len(best)} lakes with an outlet, {len(reach)} river reaches")

    def km(a, b, c, d, kx):
        return math.hypot((c - a) * 111.2 * kx, (d - b) * 111.2)

    objs = ndi.find_objects(lake_id)
    report = []
    for lid, p in sorted(best.items()):
        plat, plon = p["Pour_lat"], p["Pour_long"]
        kx = max(0.05, math.cos(math.radians(plat)))
        near = tree.query_ball_point([plon, plat], 0.03)
        if not near:
            continue
        rid = int(max((ids[n] for n in near), key=lambda r: reach[int(r)][2]))
        course, outside = [], 0.0
        while rid in reach and outside < OUTLET_COURSE_GAP_KM and len(course) < 20000:
            P = rv.shape(reach[rid][0]).points
            for lon, lat in P:
                rr, cc = rc(lat, lon, SRC_RES)
                d = km(course[-1][0], course[-1][1], lon, lat, kx) if course else 0.0
                outside = 0.0 if lake_id[rr, cc % W1] == lid else outside + d
                if outside >= OUTLET_COURSE_GAP_KM:
                    break
                course.append((lon, lat))
            rid = reach[rid][1]
        if len(course) < 2 or sum(km(*a, *b, kx) for a, b in zip(course, course[1:])) < OUTLET_MIN_COURSE_KM:
            continue
        sl = objs[lid - 1]
        if sl is None:
            continue
        lons = [lo for lo, _ in course] + [plon]
        lats = [la for _, la in course] + [plat]
        if max(lons) - min(lons) > 90:
            continue
        r0 = max(min(sl[0].start, rc(max(lats), 0, SRC_RES)[0]) - 12, 0)
        r1 = min(max(sl[0].stop, rc(min(lats), 0, SRC_RES)[0] + 1) + 12, H1)
        c0 = min(sl[1].start, rc(0, min(lons), SRC_RES)[1]) - 12
        c1 = max(sl[1].stop, rc(0, max(lons), SRC_RES)[1] + 1) + 12
        if c0 < 0 or c1 > W1 or c1 - c0 > W1 // 4:
            continue  # across the date line or huge: none of the lakes concerned
        sub = lake_id[r0:r1, c0:c1] == lid
        rr_, cc_ = np.ogrid[:sub.shape[0], :sub.shape[1]]
        cell_km2 = 1.853 * 1.853 * kx
        for rad in OUTLET_GATES_KM:
            acc, gate_at = 0.0, None
            for a, b in zip(course, course[1:]):
                acc += km(*a, *b, kx)
                if acc >= rad:
                    gate_at = b
                    break
            if gate_at is None:
                break
            gr, gc = rc(gate_at[1], gate_at[0], SRC_RES)
            gate = ((rr_ - (gr - r0)) * 1.853) ** 2 + ((cc_ - (gc - c0)) * 1.853 * kx) ** 2 <= rad * rad
            # 8-connected: at 1' a narrow river-lake is a chain of cells touching at their corners
            lab, n = ndi.label(sub & ~gate, structure=np.ones((3, 3), bool))
            if n < 2:
                continue
            sizes = np.bincount(lab.ravel(), minlength=n + 1)
            sizes[0] = 0
            main = int(np.argmax(sizes))
            pr, pc = rc(plat, plon, SRC_RES)
            if lab[pr - r0, pc - c0] not in (0, main):
                continue  # the outlet isn't on the main body's side of the gate
            # the lake's parts cut off from its main body that the course past the gate runs through
            parts, acc = set(), 0.0
            for a_, b_ in zip(course, course[1:]):
                acc += km(*a_, *b_, kx)
                if acc > 2 * rad:
                    cr, cc2 = rc(b_[1], b_[0], SRC_RES)
                    if 0 <= cr - r0 < lab.shape[0] and 0 <= cc2 - c0 < lab.shape[1]:
                        k = lab[cr - r0, cc2 - c0]
                        if k and k != main:
                            parts.add(int(k))
            if not parts:
                continue
            tongue = np.isin(lab, list(parts))
            # the gate's cells on the tongue's side
            g_in = gate & sub
            samp = (1.853, 1.853 * kx)
            d_t = ndi.distance_transform_edt(~tongue, sampling=samp)
            d_m = ndi.distance_transform_edt(lab != main, sampling=samp)
            tongue |= g_in & (d_t < d_m)
            # pieces the coarse raster cut off at the river's narrows (the Tonawanda channel east of Grand Island): nearer
            # the tongue than the lake's main body, and within OUTLET_NEAR_KM of it. Kept apart for the width.
            extra = np.zeros_like(tongue)
            d_t = ndi.distance_transform_edt(~tongue, sampling=samp)
            idx = np.arange(1, n + 1)
            dt = np.asarray(ndi.minimum(d_t, lab, idx))
            dm = np.asarray(ndi.minimum(d_m, lab, idx))
            d_in = ndi.distance_transform_edt(lab > 0, sampling=samp)
            inner = np.asarray(ndi.maximum(d_in, lab, idx))
            near_pieces = [(int(k), float(i_)) for k, a_, b_, i_ in zip(idx, dt, dm, inner) if k != main and k not in parts and a_ <= OUTLET_NEAR_KM and a_ < b_]
            add = [k for k, i_ in near_pieces if i_ <= OUTLET_PIECE_INNER_KM]
            if add:
                extra = np.isin(lab, add)
            # HydroLAKES lakes in it: a chain of lakes, not a river
            # (all eight ways: Clearwater Lake's own pour point is two cells down and one across from the polygon's end)
            big = ndi.binary_dilation(tongue, structure=np.ones((3, 3), bool), iterations=2)
            tr, tc = np.nonzero(big)
            lo0, lo1 = (c0 + tc.min()) / SRC_RES - 180, (c0 + tc.max() + 1) / SRC_RES - 180
            la1, la0 = 90 - (r0 + tr.min()) / SRC_RES, 90 - (r0 + tr.max() + 1) / SRC_RES
            cand = hl_tree.query_ball_point([(lo0 + lo1) / 2, (la0 + la1) / 2], math.hypot(lo1 - lo0, la1 - la0) / 2 + 0.02)
            in_lakes = 0.0
            for j in cand:
                hr, hc = rc(hl_pts[j][1], hl_pts[j][0], SRC_RES)
                if 0 <= hr - r0 < big.shape[0] and 0 <= hc - c0 < big.shape[1] and big[hr - r0, hc - c0] \
                        and not (abs(hl_pts[j][0] - plon) < 1e-6 and abs(hl_pts[j][1] - plat) < 1e-6):
                    in_lakes += hl_area[j]
            t_km2 = float(tongue.sum()) * cell_km2
            # the course's length in the tongue, for its mean width
            t_len = 0.0
            # (points within a cell of it count: at 1' a river-lake is a chain of cells the course runs beside)
            by_t = ndi.distance_transform_edt(~tongue, sampling=samp) <= 1.9
            for a, b in zip(course, course[1:]):
                cr, cc2 = rc(b[1], b[0], SRC_RES)
                if 0 <= cr - r0 < tongue.shape[0] and 0 <= cc2 - c0 < tongue.shape[1] and by_t[cr - r0, cc2 - c0]:
                    t_len += km(*a, *b, kx)
            width = t_km2 / max(t_len, 1.853)
            kept = in_lakes > OUTLET_LAKE_SHARE * t_km2 or width > OUTLET_MAX_WIDTH_KM
            report.append({"lake": lakes[lid]["name"], "id": lid, "hydrolakes": p["Hylak_id"], "pour": [round(plat, 4), round(plon, 4)],
                           "gate_km": rad, "tongue_km2": round(t_km2, 1), "course_km": round(t_len, 1), "width_km": round(width, 2),
                           "lakes_in_km2": round(in_lakes, 1), "pieces_km2": round(float(extra.sum()) * cell_km2, 1),
                           "pieces_inner_km": [round(i_, 2) for _, i_ in near_pieces], "box": [r0, r1, c0, c1],
                           "trimmed": not kept})
            if not kept:
                out[r0:r1, c0:c1] |= tongue | extra
            break
    n_trim = sum(1 for x in report if x["trimmed"])
    log(f"outlet tongues: {n_trim} trimmed ({int(out.sum())} 1' cells), {len(report) - n_trim} lake chains kept")
    return out, report


def order_lakes_along_rivers(lake_id, level):
    """
    A lake downstream on a river can't be higher than the lake upstream of it. Separate Natural Earth polygons of one
    lake system get their levels from ETOPO independently (Lake Constance's Untersee came out 16 m above the
    Obersee it is fed from), so walking the large rivers (HydroRIVERS, discharge >= 150 m3/s) downstream, a lake
    higher than the last lake upstream is lowered to it (by at most 30 m: more is a river merely passing near a lake).
    """
    shp = os.path.join(RAW, "hydrorivers", "HydroRIVERS_v10_shp", "HydroRIVERS_v10.shp")
    if not os.path.exists(shp):
        log("HydroRIVERS not found, lake levels along rivers not ordered")
        return
    r = shapefile.Reader(shp)
    names = [f[0] for f in r.fields[1:]]
    iid, inext, iq = names.index("HYRIV_ID"), names.index("NEXT_DOWN"), names.index("DIS_AV_CMS")
    H1, W1 = lake_id.shape
    seqs, nexts = {}, {}
    for i, rec in enumerate(r.iterRecords()):
        if rec[iq] < 150:
            continue
        pts = r.shape(i).points
        seq = []
        for lon, lat in pts:
            rr = min(max(int((90 - lat) * SRC_RES), 0), H1 - 1)
            cc = int((lon + 180) * SRC_RES) % W1
            lid = int(lake_id[rr, cc])
            if lid > 0 and np.isfinite(level[lid]) and (not seq or seq[-1] != lid):
                seq.append(lid)
        seqs[rec[iid]] = seq
        nexts[rec[iid]] = rec[inext]
    indeg = defaultdict(int)
    for rid, nx in nexts.items():
        if nx in seqs:
            indeg[nx] += 1
    queue = deque(rid for rid in seqs if indeg[rid] == 0)
    cap_in = {}
    lowered = {}
    while queue:
        rid = queue.popleft()
        cap = cap_in.get(rid, np.inf)
        for lid in seqs[rid]:
            if level[lid] > cap and level[lid] - cap <= 30:
                lowered[lid] = (float(level[lid]), float(cap))
                level[lid] = cap
            cap = float(level[lid])
        nx = nexts[rid]
        if nx in seqs:
            cap_in[nx] = min(cap_in.get(nx, np.inf), cap)
            indeg[nx] -= 1
            if indeg[nx] == 0:
                queue.append(nx)
    log(f"lowered {len(lowered)} lakes to the level of the lake upstream on their river, e.g. "
        f"{[(k, round(a), round(b)) for k, (a, b) in list(lowered.items())[:8]]}")


# Terminal lakes for lakes.bin (worlds with EarthParams.RiverEnds): where rivers without an outlet end. "revived": lakes
# irrigation and dams dried up in the 20th century, at their level before (the rivers carry HydroRIVERS' natural flow,
# without the withdrawals); "natural": salt lakes and pans that fill in wet years, at a filled level. Each fills the
# basin around its seed (the lowest 1' cell within 0.1°) below its level, inside its box (lat0, lat1, lon0, lon1).
# Levels in ETOPO 2022 metres, tuned to the known areas (1' cells, before the 2' grid). Shore: how far (km) the land
# around a lake in a dry climate is greener (reed marsh, meadow, tugai woods in the deltas, as along the Aral before
# 1960), easing out into the steppe and the desert; 0 for salt pans and brines with barren shores.
TERMINAL_LAKES = [
    # name, kind, salt, level m, seed lat, lon, box, shore km; real area for reference
    ("Aral Sea", "revived", True, 53.0, 45.0, 59.5, (43.0, 47.6, 57.8, 62.5), 40),               # 1960: 68 000 km², 53 m
    ("Hamun lakes", "revived", False, 475.0, 31.3, 61.5, (30.3, 31.9, 60.8, 62.3), 15),          # Sistan, wet years 2-5 000 km²
    ("Lop Nur", "revived", True, 788.0, 40.4, 90.5, (39.4, 41.2, 89.4, 91.4), 10),               # 1930s ~3 000 km², dry since 1972
    ("Tashk", "revived", True, 1562.0, 29.7, 53.6, (29.0, 30.0, 53.2, 54.6), 0),                 # with Bakhtegan, the Kor's end
    ("Bakhtegan", "revived", True, 1562.0, 29.35, 53.95, (29.0, 30.0, 53.2, 54.6), 0),
    ("Gavkhouni", "revived", True, 1452.0, 32.25, 52.85, (31.9, 32.6, 52.5, 53.2), 5),           # the Zayandeh Rud's end, ~470 km²
    ("Manas Lake", "revived", True, 256.0, 45.75, 85.9, (45.4, 46.2, 85.4, 86.4), 8),            # 1950s ~550 km²
    ("Lake Chad", "revived", False, 283.0, 13.3, 14.0, (11.5, 15.0, 12.3, 15.5), 25),            # 1963 ~25 000 km²
    ("Owens Lake", "revived", True, 1096.0, 36.43, -117.95, (36.2, 36.65, -118.15, -117.8), 0),  # 1913, 280 km²
    ("Tulare Lake", "revived", False, 61.0, 36.05, -119.75, (35.7, 36.35, -120.1, -119.35), 12), # 1870s ~1 800 km²
    ("Dead Sea", "revived", True, -392.0, 31.5, 35.5, (30.9, 31.85, 35.3, 35.65), 0),            # 1960s, both basins
    ("Kati Thanda-Lake Eyre", "natural", True, -14.0, -28.4, 137.3, (-29.7, -27.8, 136.4, 138.3), 0),  # full 9 500 km²
    ("Paraku (Lake Gregory)", "natural", False, 276.0, -20.15, 127.5, (-20.5, -19.8, 127.1, 127.8), 5),
    ("Salar de Coipasa", "natural", True, 3656.8, -19.3, -68.2, (-19.7, -18.9, -68.6, -67.7), 0),
    ("Makgadikgadi", "natural", True, 902.0, -20.7, 25.9, (-21.3, -20.0, 24.6, 26.6), 0),
    ("Namak Lake", "natural", True, 789.0, 34.75, 51.85, (34.0, 35.5, 50.9, 52.9), 0),
    ("Salinas Grandes", "natural", True, 178.0, -30.0, -64.8, (-30.8, -29.6, -65.6, -64.2), 0),
    ("Etosha Pan", "natural", True, 1081.0, -18.8, 16.3, (-19.6, -18.0, 15.4, 17.4), 0),
]


def add_terminal_lakes(S, lake_id, lakes):
    """Floods each TERMINAL_LAKES basin into lake_id (over the Natural Earth lakes in it) with its own level."""
    for name, kind, salt, lv, la, lo, (a0, a1, o0, o1), _shore in TERMINAL_LAKES:
        r0, c0 = rc(a1, o0, SRC_RES)
        r1, c1 = rc(a0, o1, SRC_RES)
        sub = S[r0:r1, c0:c1]
        sr, sc = rc(la, lo, SRC_RES)
        sr -= r0
        sc -= c0
        w = 6
        win = sub[max(sr - w, 0):sr + w + 1, max(sc - w, 0):sc + w + 1]
        i = np.unravel_index(np.argmin(win), win.shape)
        br, bc = max(sr - w, 0) + i[0], max(sc - w, 0) + i[1]
        lab, _ = ndi.label(sub < lv)
        if lab[br, bc] == 0:
            log(f"terminal lake {name}: its seed is not below {lv} m, left out")
            continue
        m = lab == lab[br, bc]
        edge = m[0].any() or m[-1].any() or m[:, 0].any() or m[:, -1].any()
        lid = len(lakes)
        lakes.append({"name": name, "salt": salt, "src": "terminal-" + kind, "level": lv})
        lake_id[r0:r1, c0:c1][m] = lid
        lat = a1 - (np.arange(sub.shape[0]) + 0.5) / SRC_RES
        km2 = float(((111.32 / SRC_RES) ** 2 * np.cos(np.radians(lat))[:, None] * m).sum())
        log(f"terminal lake {name} ({kind}) at {lv} m: {km2:.0f} km²{' (cut at its box)' if edge else ''}")


def build_terrain(allowed=None, terminal_lakes=False, outlets=False):
    """The 2' grid from ETOPO; with `allowed` (1' bool raster) only the land in it, for islands.bin; with terminal_lakes
    the TERMINAL_LAKES filled, for lakes.bin."""
    log("loading ETOPO")
    S, B = load_etopo()
    H1, W1 = S.shape

    log("rasterizing lakes")
    lake_id, lakes = rasterize_lakes(S.shape)
    drop_reservoir_lakes(lake_id, lakes, S)
    if outlets:
        # outlets.bin: the lakes' tongues past their outlet become land (ETOPO's surface there), the river runs through
        tongue, _ = outlet_tongues(lake_id, lakes)
        lake_id[tongue] = 0
        del tongue
    if terminal_lakes:
        add_terminal_lakes(S, lake_id, lakes)

    # --- islands.bin only (allowed given): ETOPO's land away from the big land goes under water as a shallow reef. The
    # small islands come back from their GSHHG outlines, built in game where they are big enough in blocks; the rest
    # of that land is false: ETOPO has islands 10-95 m high on reef rims where there is only open reef (the Great
    # Bahama Bank's southern rim, the Tongue of the Ocean). Not under the ice sheets, south of 60°S or in lakes.
    fake = None
    if allowed is not None:
        fake = (S >= 0) & ~allowed & (lake_id == 0) & (S - B <= 0)
        fake[(90 + 60) * SRC_RES:] = False
        fake_elev = S[fake].copy()
        S[fake] = -2.0

    # --- ocean: everything below sea level that is connected to one of the seas below
    log("labelling water below sea level")
    below = S < 0
    lab, _ = label_wrapped(below & (lake_id == 0))
    sea_seeds = [(0, -150), (0, -30), (-30, 80), (85, 0), (43.0, 34.0), (46.0, 36.5), (40.8, 28.0), (35, 18)]
    ocean_labels = set()
    for lat, lon in sea_seeds:
        r, c = rc(lat, lon, SRC_RES)
        if lab[r, c]:
            ocean_labels.add(int(lab[r, c]))
    ocean = np.isin(lab, list(ocean_labels))
    # The Caspian is a lake below sea level (Natural Earth keeps it with the oceans, so it has no lake polygon)
    # Its water surface is 27 m below sea level; the rest of the Caspian Depression (-27..0 m) is dry land.
    r, c = rc(42.0, 51.0, SRC_RES)
    casp = (lab == lab[r, c]) & (S < -27.0)
    casp_id = len(lakes)
    lakes.append({"name": "Caspian Sea", "salt": True, "src": "etopo", "level": -27.0})
    lake_id[casp & (lake_id == 0)] = casp_id
    ocean &= ~(lab == lab[r, c])
    del lab, below
    if fake is not None:
        # masked land that didn't become sea (inland, away from any coast) keeps its ETOPO height
        keep = fake & ~ocean
        S[keep] = fake_elev[keep[fake]]
        log(f"masked {int((fake & ocean).sum())} 1' land cells into the sea, kept {int(keep.sum())} inland")
        del fake, fake_elev, keep

    # Carve the Bosporus and Dardanelles: at 1' they are too narrow to connect the Black Sea
    straits = [[(40.05, 26.20), (40.20, 26.40), (40.38, 26.68), (40.45, 26.75)],
               [(40.98, 28.98), (41.08, 29.05), (41.20, 29.12)]]
    for line in straits:
        for (la1, lo1), (la2, lo2) in zip(line, line[1:]):
            n = 40
            for t in np.linspace(0, 1, n):
                r, c = rc(la1 + (la2 - la1) * t, lo1 + (lo2 - lo1) * t, SRC_RES)
                ocean[r - 1:r + 2, c - 1:c + 2] = True
                S[r - 1:r + 2, c - 1:c + 2] = np.minimum(S[r - 1:r + 2, c - 1:c + 2], -30)

    # --- lakes: level from the shore, bed from bathymetry where ETOPO has it, else a depth profile
    log(f"processing {len(lakes) - 1} lakes")
    lake_mask = lake_id > 0
    lake_mask &= ~ocean
    lake_id[~lake_mask] = 0
    objs = ndi.find_objects(lake_id)
    level = np.full(len(lakes), np.nan, np.float32)
    dist_shore = ndi.distance_transform_edt(lake_mask).astype(np.float32)  # in 1' cells
    bed = S.copy()
    merged_into_sea = 0
    for lid, sl in enumerate(objs, start=1):
        if sl is None:
            continue
        r0, r1 = max(sl[0].start - 3, 0), min(sl[0].stop + 3, H1)
        c0, c1 = max(sl[1].start - 3, 0), min(sl[1].stop + 3, W1)
        sub = lake_id[r0:r1, c0:c1] == lid
        if sub.sum() == 0:
            continue
        ring = ndi.binary_dilation(sub, iterations=2) & ~sub & ~lake_mask[r0:r1, c0:c1] & ~ocean[r0:r1, c0:c1]
        sv = S[r0:r1, c0:c1]
        interior = sv[sub]
        # ETOPO stores most lakes as a flat water surface; some (Great Lakes, Baikal, Caspian) with their bathymetry.
        vals, counts = np.unique(np.round(interior), return_counts=True)
        mode = float(vals[np.argmax(counts)])
        flat_share = float(np.mean(np.abs(interior - mode) <= 1.0))
        if lid < len(lakes) and lakes[lid] and "level" in lakes[lid]:
            lv = float(lakes[lid]["level"])
        elif flat_share >= 0.3 or ring.sum() == 0:
            lv = mode
        else:
            lv = float(np.percentile(sv[ring], 10))   # lowest shores
        level[lid] = lv
        # a lake at sea level with the sea all round it (Frisian meres amid the flooded polders) is part of the sea
        if lv <= 1.0:
            around = ndi.binary_dilation(sub, iterations=2) & ~sub
            if around.sum() > 0 and ocean[r0:r1, c0:c1][around].mean() >= 0.6:
                tmp = ocean[r0:r1, c0:c1]
                tmp[sub] = True
                lake_mask[r0:r1, c0:c1][sub] = False
                lake_id[r0:r1, c0:c1][sub] = 0
                level[lid] = np.nan
                merged_into_sea += 1
    log(f"merged {merged_into_sea} sea-level lakes surrounded by sea into it")
    order_lakes_along_rivers(lake_id, level)
    for lid, sl in enumerate(objs, start=1):
        if sl is None or not np.isfinite(level[lid]):
            continue
        r0, r1 = max(sl[0].start - 3, 0), min(sl[0].stop + 3, H1)
        c0, c1 = max(sl[1].start - 3, 0), min(sl[1].stop + 3, W1)
        sub = lake_id[r0:r1, c0:c1] == lid
        if sub.sum() == 0:
            continue
        interior = S[r0:r1, c0:c1][sub]
        lv = float(level[lid])
        # bed: bathymetry if clearly below the level, else a profile from the distance to shore
        km = dist_shore[r0:r1, c0:c1][sub] * 1.5
        profile = lv - np.minimum(6 + 14 * np.power(km, 0.7), 400)
        bsub = np.where(interior < lv - 1, interior, profile)
        bsub = np.minimum(bsub, lv - 2)
        tmp = bed[r0:r1, c0:c1]
        tmp[sub] = bsub
    lakes_out = []
    for lid in range(1, len(lakes)):
        if np.isfinite(level[lid]):
            lakes_out.append({"id": lid, "name": lakes[lid]["name"], "salt": lakes[lid]["salt"], "level": round(float(level[lid]), 1)})
    del dist_shore

    # --- ice: ETOPO bed differs from the surface only under the Greenland and Antarctic ice sheets
    ice = np.clip(S - B, 0, 6000)
    ice[ocean] = np.where(ice[ocean] > 0, ice[ocean], 0)
    del B

    # terrain surface: the lake beds, everything else as is
    T = np.where(lake_mask, bed, S)
    del bed

    # roughness: standard deviation of the 1' terrain in a 4x4 window
    log("roughness")
    m = ndi.uniform_filter(T.astype(np.float64), 4, mode="wrap")
    m2 = ndi.uniform_filter(T.astype(np.float64) ** 2, 4, mode="wrap")
    rough1 = np.sqrt(np.maximum(m2 - m * m, 0)).astype(np.float32)
    del m, m2

    # --- downsample to 2'
    log("downsampling to 2'")
    k = SRC_RES // RES
    H, W = 180 * RES, 360 * RES

    def blockmean(a):
        return a.reshape(H, k, W, k).mean(axis=(1, 3))

    def blockmax(a):
        return a.reshape(H, k, W, k).max(axis=(1, 3))

    elev = blockmean(T)
    peak = blockmax(T)
    # the dry land's own mean per 2' cell (for shores.bin): a cell that is land by majority but holds part of a narrow, deep
    # lake (Loch Ness, the fjord lakes) has the lake's bed in its mean, up to hundreds of metres below the land around.
    # The 1' cells next to a lake are left out too: where the lake's outline misses some of a narrow lake, ETOPO's
    # bathymetry stands there as land (Loch Ness: -65 m)
    dry = ~(ndi.binary_dilation(lake_mask, iterations=2) | ocean)
    n_dry = dry.reshape(H, k, W, k).sum(axis=(1, 3))
    dry_mean = np.where(dry, T, 0).reshape(H, k, W, k).sum(axis=(1, 3)) / np.maximum(n_dry, 1)
    dry_mean = np.where(n_dry > 0, dry_mean, np.nan).astype(np.float32)
    del dry, n_dry
    rough = np.maximum(blockmean(rough1), (peak - elev) * 0.5)
    ocean2 = blockmean(ocean.astype(np.float32)) >= 0.5
    # Narrow sea channels (fjords, sounds): a 2' cell is sea by majority of its 1' cells, and its elevation is the
    # mean, so a fjord one or two 1' cells wide lost its sea cells and its floor (the walls averaged in) and fell
    # apart into separate pools. Cells holding the centre line of water less than ~3 cells wide stay sea, with the
    # mean depth of their water; diagonal steps get an orthogonal link so the channel stays connected.
    dsea = ndi.distance_transform_edt(ocean).astype(np.float32)
    axis = ocean & (dsea <= 4) & (dsea >= ndi.maximum_filter(dsea, size=3) - 1e-3)
    del dsea
    axis2 = blockmax(axis.astype(np.uint8)) > 0
    del axis
    oc_f = ocean.astype(np.float32)
    n_sea = oc_f.reshape(H, k, W, k).sum(axis=(1, 3))
    sea_mean = (np.where(ocean, T, 0).reshape(H, k, W, k).sum(axis=(1, 3))) / np.maximum(n_sea, 1)
    del oc_f
    add = axis2 & ~ocean2
    # diagonal-only steps between channel cells: link through the orthogonal neighbour with more water
    chan = ocean2 | add
    for dr, dc in ((1, 1), (1, -1)):
        a = chan & np.roll(np.roll(chan, -dr, 0), -dc, 1)            # cell and its diagonal neighbour are water
        o1 = np.roll(chan, -dr, 0)                                    # the two orthogonal cells between them
        o2 = np.roll(chan, -dc, 1)
        gap = a & ~o1 & ~o2
        n1 = np.roll(n_sea, -dr, 0)
        n2 = np.roll(n_sea, -dc, 1)
        pick1 = gap & (n1 >= n2) & (n1 > 0)
        pick2 = gap & ~pick1 & (n2 > 0)
        add |= np.roll(pick1, dr, 0) | np.roll(pick2, dc, 1)
    add &= n_sea > 0
    ocean2 |= add
    # every cell on a channel's centre line (also those that were sea by majority but got their walls averaged into
    # their elevation, e.g. along the Sognefjord) takes the depth of its water
    deep = (add | axis2) & (n_sea > 0)
    elev[deep] = np.minimum(elev[deep], np.minimum(sea_mean[deep], -3.0))
    log(f"kept {int(add.sum())} narrow sea channel cells (fjords, sounds), deepened {int(deep.sum())}")
    del axis2, n_sea, sea_mean, add, chan
    lake2f = blockmean(lake_mask.astype(np.float32))
    lake2 = (lake2f >= 0.5) & ~ocean2
    # majority lake id per 2' cell
    lid2 = lake_id.reshape(H, k, W, k).max(axis=(1, 3))
    lid2[~lake2] = 0
    ice2 = blockmean(ice)
    del T, ice, rough1, peak

    # connected lake cells (two basins of one lake, a lake and its marsh) share one level and salinity: the largest's
    comp, ncomp = ndi.label(lake2, structure=np.ones((3, 3), bool))
    for k, sl in enumerate(ndi.find_objects(comp), start=1):
        if sl is None:
            continue
        m = comp[sl] == k
        ids = lid2[sl][m]
        ids = ids[ids > 0]
        if ids.size == 0:
            continue
        major = np.bincount(ids).argmax()
        sub = lid2[sl]
        sub[m] = major
    # lakes within a few metres of sea level sit at sea level (ETOPO's flat lake surfaces near coasts are often -2..+2 m)
    level = np.where(np.isfinite(level) & (np.abs(level) < 3), 0, level)

    # water level: 0 for the ocean, the lake's level for lakes, nearest water level for land within 3 cells
    lvl = np.full((H, W), np.nan, np.float32)
    lvl[ocean2] = 0
    lake_levels = np.where(np.isfinite(level), level, np.nan)
    lvl[lake2] = lake_levels[lid2[lake2]]
    water = np.isfinite(lvl)
    dist, (ir, ic) = ndi.distance_transform_edt(~water, return_indices=True)
    near = (~water) & (dist <= 3)
    lvl[near] = lvl[ir[near], ic[near]]

    # a lake cell whose level is unknown (tiny lakes) becomes land
    lake2 &= np.isfinite(lvl)

    # keep dry land out of the sea: coastal land at least at sea level, lake shores at least at the lake level
    land = ~ocean2 & ~lake2
    elev = np.where(ocean2, np.minimum(elev, -1.0), elev)
    elev = np.where(lake2, np.minimum(elev, lvl - 2), elev)
    shore = land & np.isfinite(lvl)
    raised = np.where(shore, np.maximum(elev, lvl + 1), elev)
    # shores.bin (worlds with EarthParams.LakeShores): around a lake (not the sea, not a lake at sea level) only the land
    # touching it is raised to the lake's level; further out the raise eases off to nothing at the edge of the 3-cell
    # ring, so ground lower than the lake (its outlet valley) slopes down instead of standing as a flat plateau with a
    # cliff where the ring ends (Lake Bolsena, Lake Bracciano: up to 10 blocks)
    from_lake = shore & near & lake2[ir, ic] & (lvl != 0)
    t = np.clip((dist - 1.5) / 2.0, 0, 1)
    ease = 1 - t * t * (3 - 2 * t)
    # (a cell with no dry 1' cell at all, half sea loch and half lake like the north end of Loch Ness, keeps the raise)
    ground = np.where(np.isfinite(dry_mean), np.minimum(dry_mean, raised), raised)
    ground = np.maximum(ground, np.minimum(raised, 1.0))   # never eased below sea level
    shores = np.where(from_lake, ground + (raised - ground) * ease, raised)
    # nearness to the lake (1 at the cells touching it, easing to 0 where the ring ends): in game the land is held at the
    # lake's level and kept from the noise's dips in step with it, so nothing ends in a step at the ring's edge
    near_lake = np.where(from_lake, ease, 1.0).astype(np.float32)
    log(f"lake shores: {int((from_lake & (shores < raised)).sum())} ring cells eased below the lake level")
    elev = raised
    del raised, t, ease, from_lake, ground, dry_mean

    salt_ids = np.array([False] + [bool(l and l["salt"]) for l in lakes[1:]])
    flags = np.zeros((H, W), np.uint8)
    flags[ocean2] |= 1
    flags[lake2] |= 2
    flags[lake2 & salt_ids[lid2]] |= 4
    flags[ice2 > 15] |= 8

    log("terrain grid done")
    return {
        "elev": elev.astype(np.float32),
        "level": lvl,
        "flags": flags,
        "ice": ice2.astype(np.float32),
        "shores": shores.astype(np.float32),
        "near_lake": near_lake,
        "rough": rough.astype(np.float32),
        "ocean1": ocean,  # 1' ocean mask, used for the seam path
        "S1": S,
    }, lakes_out


LAND_Q, SEA_Q = 700.0, 150.0   # elevation code: land 50*expm1(q/700) m, sea -50*expm1(-q/150) m


def encode_elev(e):
    e = e.astype(np.float64)
    return np.where(e >= 0, np.round(np.log1p(np.maximum(e, 0) / 50) * LAND_Q),
                    -np.round(np.log1p(np.maximum(-e, 0) / 50) * SEA_Q)).astype(np.int32)


def paeth_residual(q):
    """Residual against the planar predictor left + up - upleft (zeros outside the tile)."""
    a = np.zeros_like(q); b = np.zeros_like(q); c = np.zeros_like(q)
    a[:, 1:] = q[:, :-1]; b[1:, :] = q[:-1, :]; c[1:, 1:] = q[:-1, :-1]
    return q - (a + b - c)


def terrain_planes(g):
    """The 2' grid as stored in terrain.bin: elevation code, level (m), flags, ice (4 m steps)."""
    e = g["elev"].astype(np.float64)
    # the deep ocean floor is never seen up close: smooth it so it compresses. Only over sea cells, and never above
    # -300 m: blurred together with the land beside them, deep fjords (Sognefjord 1300 m) came out above sea level.
    ocean = (g["flags"] & 1) > 0
    oc = ocean.astype(np.float64)
    sea_blur = ndi.gaussian_filter(np.where(ocean, e, 0.0), 1.5, mode="wrap") / np.maximum(ndi.gaussian_filter(oc, 1.5, mode="wrap"), 1e-6)
    e = np.where(ocean & (e < -300), np.minimum(sea_blur, -300.0), e)
    del oc, sea_blur
    elev = encode_elev(e)
    lvl = g["level"]
    lvl = np.where(np.isfinite(lvl), np.clip(np.round(lvl), -1000, 9000), -32768).astype("<i2")
    ice = np.clip(np.round(g["ice"] / 4), 0, 2000).astype(np.int32)   # 4 m steps
    return elev, lvl, g["flags"], ice


def write_terrain(g):
    H, W = g["elev"].shape
    tiles_y, tiles_x = H // TILE, W // TILE
    elev, lvl, flags, ice = terrain_planes(g)
    blobs = []
    for ty in range(tiles_y):
        for tx in range(tiles_x):
            sl = (slice(ty * TILE, (ty + 1) * TILE), slice(tx * TILE, (tx + 1) * TILE))
            raw = (paeth_residual(elev[sl]).astype("<i2").tobytes() + lvl[sl].tobytes() + flags[sl].tobytes()
                   + paeth_residual(ice[sl]).astype("<i2").tobytes())
            blobs.append(brotli.compress(raw, quality=11, lgwin=24))
    path = os.path.join(OUT, "terrain.bin")
    with open(path, "wb") as f:
        f.write(b"EMTR")
        f.write(struct.pack("<iiiii", 2, W, H, TILE, len(blobs)))
        off = 4 + 20 + 8 * len(blobs)
        for b in blobs:
            f.write(struct.pack("<ii", off, len(b)))
            off += len(b)
        for b in blobs:
            f.write(b)
    log(f"terrain.bin {os.path.getsize(path) / 1e6:.1f} MB, {len(blobs)} tiles")


# ---------------------------------------------------------------------------------------------- climate

def netcdf3_size(path):
    """The size a classic / 64-bit-offset netCDF file must have (from its header), or None for other formats (netCDF-4 /
    HDF5 fails loudly when cut short). netCDF-C reads a cut-short classic file without an error: what is past the end
    comes back as zeros or fill, which is how a truncated UDel download lost December (see UDEL_LEGACY_PRECIP)."""
    with open(path, "rb") as f:
        b = f.read(1 << 20)
    if b[:3] != b"CDF" or b[3] not in (1, 2):
        return None
    off_size = 8 if b[3] == 2 else 4
    o = 4
    numrecs = struct.unpack(">i", b[o:o + 4])[0]; o += 4

    def name(o):
        n = struct.unpack(">i", b[o:o + 4])[0]
        return o + 4 + (n + 3) // 4 * 4

    def attrs(o):
        _, n = struct.unpack(">ii", b[o:o + 8]); o += 8
        for _ in range(n):
            o = name(o)
            t, c = struct.unpack(">ii", b[o:o + 8]); o += 8
            o += (c * {1: 1, 2: 1, 3: 2, 4: 4, 5: 4, 6: 8}[t] + 3) // 4 * 4
        return o

    _, n = struct.unpack(">ii", b[o:o + 8]); o += 8
    dims = []
    for _ in range(n):
        o = name(o)
        dims.append(struct.unpack(">i", b[o:o + 4])[0]); o += 4
    o = attrs(o)
    _, n = struct.unpack(">ii", b[o:o + 8]); o += 8
    end, rec_begin, rec_size = 0, None, 0
    for _ in range(n):
        o = name(o)
        nd = struct.unpack(">i", b[o:o + 4])[0]
        ids = struct.unpack(f">{nd}i", b[o + 4:o + 4 + 4 * nd]); o += 4 + 4 * nd
        o = attrs(o)
        _, vsize = struct.unpack(">ii", b[o:o + 8]); o += 8
        begin = struct.unpack(">q" if off_size == 8 else ">i", b[o:o + off_size])[0]; o += off_size
        if nd and dims[ids[0]] == 0:                                   # a record variable
            rec_begin = begin if rec_begin is None else min(rec_begin, begin)
            rec_size += vsize
        else:
            end = max(end, begin + vsize)
    if rec_begin is not None:
        end = max(end, rec_begin + numrecs * rec_size)
    return end


def nc(name, var, allow_truncated=False):
    path = os.path.join(RAW, name)
    need = netcdf3_size(path)
    if need is not None and os.path.getsize(path) < need and not allow_truncated:
        raise SystemExit(f"{path} is cut short ({os.path.getsize(path)} of {need} bytes): download it again "
                         f"(tools/download_data.sh)")
    d = netCDF4.Dataset(path)
    v = d.variables[var][:]
    lat = np.array(d.variables["lat"][:], np.float64)
    lon = np.array(d.variables["lon"][:], np.float64)
    return np.ma.filled(v.astype(np.float64), np.nan), lat, lon


def regrid(field, lat, lon, tlat, tlon):
    """Bilinear from a (lat, lon 0..360) grid to target cell centres, wrapping in longitude."""
    order = np.argsort(lat)
    lat = lat[order]
    field = field[..., order, :]
    lon_ext = np.concatenate([lon[-1:] - 360, lon, lon[:1] + 360])
    f_ext = np.concatenate([field[..., -1:], field, field[..., :1]], axis=-1)
    lat_ext = np.concatenate([[-90.0], lat, [90.0]])
    f_ext = np.concatenate([f_ext[..., :1, :], f_ext, f_ext[..., -1:, :]], axis=-2)
    LA, LO = np.meshgrid(tlat, tlon % 360, indexing="ij")
    pts = np.stack([LA.ravel(), LO.ravel()], -1)
    out = []
    for m in range(f_ext.shape[0]):
        it = RegularGridInterpolator((lat_ext, lon_ext), f_ext[m], bounds_error=False, fill_value=None)
        out.append(it(pts).reshape(LA.shape))
    return np.array(out)


def fill_nan(a):
    """Fill NaNs with the nearest valid value (2D)."""
    m = np.isnan(a)
    if not m.any():
        return a
    _, (ir, ic) = ndi.distance_transform_edt(m, return_indices=True)
    return a[ir, ic]


# UDel v5.01 monthly normals, complete (tools/download_data.sh: 18 669 612 and 18 669 608 bytes)
UDEL_PRECIP = os.path.join("udel-v501", "precip.mon.v501.ltm.nc")
UDEL_AIR = os.path.join("udel-v501", "air.mon.v501.ltm.nc")
# The precipitation file climate.bin (0.14) and climatefix.bin (0.15.1) were baked from was a download cut short at
# 11 151 009 bytes: the precip variable ends three quarters into November, so 9 929 land cells read November or December
# as missing (the "missing months" of decision 0018) and the other ~75 600 read December, and ~21 600 November, as 0 mm.
# Its bytes are the complete file's first 11 151 009. Kept so these two files stay reproducible for existing worlds
# (decision 0002); weather.bin and climatefix2.bin (new worlds) read the complete file (decision 0018, 2026-11-07).
UDEL_LEGACY_PRECIP = "udel.airt.precip_precip.mon.v501.ltm.nc"
UDEL_LEGACY_BYTES = 11151009


def udel_precip(legacy):
    """UDel precipitation (cm / month), lat, lon: the complete normals, or (legacy) those climate.bin was baked from."""
    if not legacy:
        return nc(UDEL_PRECIP, "precip")
    path = os.path.join(RAW, UDEL_LEGACY_PRECIP)
    if not (os.path.exists(path) and os.path.getsize(path) == UDEL_LEGACY_BYTES):
        # rebuild the cut-short file from the complete one (same bytes)
        name = os.path.join("udel-v501", f"precip.mon.v501.ltm.first-{UDEL_LEGACY_BYTES}-bytes.nc")
        path = os.path.join(RAW, name)
        if not (os.path.exists(path) and os.path.getsize(path) == UDEL_LEGACY_BYTES):
            with open(os.path.join(RAW, UDEL_PRECIP), "rb") as f:
                head = f.read(UDEL_LEGACY_BYTES)
            with open(path, "wb") as f:
                f.write(head)
        return nc(name, "precip", allow_truncated=True)
    return nc(UDEL_LEGACY_PRECIP, "precip", allow_truncated=True)


COAST_BAND = 4        # climatefix3: UDel fades in over this many 0.25° cells from the edge of its coverage (1°)


def coast_band_weight(wU, tlat, band):
    """UDel's weight over a band of `band` cells from the edge of its coverage (a smoothstep of the distance, in cells, to
    the nearest cell without UDel nodes near it, wU < 0.25), instead of over the one row where wU falls (decision 0018,
    climate fix v3, EA-0016). Longitude wraps. South of 60°S the old weight stays (Antarctica is left as it was)."""
    have = wU >= 0.25
    pad = 4 * band
    padded = np.concatenate([have[:, -pad:], have, have[:, :pad]], axis=1)
    dist = ndi.distance_transform_edt(padded)[:, pad:-pad]
    x = np.clip(dist / band, 0, 1)
    w = np.where(have, x * x * (3 - 2 * x), 0.0)
    old = np.clip((wU - 0.25) / 0.5, 0, 1)
    return np.where((tlat > -60)[:, None], w, old)


# climatefix4 (EA-0060): GPCP's share is brought to CHELSA's level where GPCP is over DESERT_RATIO times as wet as CHELSA (fully
# from twice that) and CHELSA has under DESERT_MM (fully under half of it); a smooth weight in the logarithm in between
DESERT_RATIO, DESERT_MM = 2.0, 300.0
CHELSA_BIO12 = os.path.join("chelsa-bio-v2.1", "CHELSA_bio12_1981-2010_V.2.1.tif")


def chelsa_bio12_quarter():
    """CHELSA v2.1's yearly precipitation (bio12, mm, 1981-2010) as means of its 30" pixels over each 0.25° cell of the
    climate grid (rows from 90°N); NaN north of 84°N, where CHELSA ends. The pixels at the format's top value 65535
    (6553.5 mm, a few very wet places) are left out of their cell's mean; no dry cell has one."""
    import bake_rain
    r = bake_rain.Raster(os.path.join(RAW, CHELSA_BIO12))
    out = np.full((180 * CLIM_RES, 360 * CLIM_RES), np.nan)
    k = int(round(1 / (r.px[1] * CLIM_RES)))                  # pixels per cell side (30)
    row0 = int(round((90 - r.tie[4]) * CLIM_RES))
    step = k * 16
    for a in range(0, r.h, step):
        b = min(a + step, r.h)
        x = r.rows(a, b).astype(np.float64)
        if r.nodata is not None:
            x[x == r.nodata] = np.nan
        x = x * r.scale + r.offset
        n = (b - a) // k
        with np.errstate(invalid="ignore"):
            out[row0 + a // k: row0 + a // k + n] = np.nanmean(x[:n * k].reshape(n, k, out.shape[1], k), axis=(1, 3))
    log(f"CHELSA bio12 on the 0.25° grid: {np.isfinite(out).mean() * 100:.1f}% of the cells")
    return out


def desert_coast_factor(p_gpcp_year):
    """The factor on GPCP's monthly precipitation that brings its year toward CHELSA's on dry coasts (climatefix4): GPCP^(1-k)
    CHELSA^k for the year, k = smooth((ln(GPCP/CHELSA) - ln DESERT_RATIO) / ln DESERT_RATIO) smooth((DESERT_MM - CHELSA) /
    (DESERT_MM / 2)), 1 where CHELSA has no data. Only GPCP's share of a cell changes: inland, where the climate is UDel's
    alone, the Sahel's northern edge is wetter in UDel than in CHELSA and stations there don't settle which is right."""
    c = chelsa_bio12_quarter()
    def smooth(x):
        x = np.clip(x, 0, 1)
        return x * x * (3 - 2 * x)
    with np.errstate(invalid="ignore", divide="ignore"):
        ratio = p_gpcp_year / np.maximum(c, 1)
        k = smooth((np.log(ratio) - math.log(DESERT_RATIO)) / math.log(DESERT_RATIO)) * smooth((DESERT_MM - c) / (DESERT_MM / 2))
        k = np.where(np.isfinite(k) & (p_gpcp_year > 0), k, 0)
        f = np.where(k > 0, np.exp(-k * np.log(np.maximum(ratio, 1e-9))), 1.0)
    log(f"climate fix v4: GPCP brought toward CHELSA in {int((k > 0.01).sum())} cells ({int((k > 0.99).sum())} fully)")
    return f


def build_climate(S1, fill_gaps=False, legacy_udel=True, coast_band=0, desert_coasts=False):
    """fill_gaps: UDel's missing precipitation months filled (fill_month_gaps), which climate.bin (worlds before the climate
    fix) doesn't have; climatefix.bin holds the cells that changes (decision 0018).
    legacy_udel: UDel's precipitation as climate.bin and climatefix.bin were baked from (cut short, December 0 mm over most
    land, see UDEL_LEGACY_PRECIP); False reads the complete normals (climatefix2.bin).
    coast_band: UDel's precipitation fades into GPCP's over this many cells from the edge of its coverage (climatefix3.bin,
    COAST_BAND); 0 switches within the row where its nodes end (climate.bin to v2). The one-row switch drew rain stripes
    along coasts wherever the two differ in level (GPCP corrects for the gauges' undercatch: about 1.4 times UDel on
    Arctic coasts), EA-0016. The temperatures keep the one-row switch: worlds take theirs from CHELSA (temperature.bin),
    and climatefix3.bin then holds only the precipitation planes (size).
    desert_coasts: GPCP's share of the blend (the coast band and the sea) at CHELSA's yearly level where GPCP is much wetter
    than CHELSA on a dry coast (climatefix4.bin, desert_coast_factor, EA-0060): GPCP's 2.5° cells mix the Humboldt and
    Benguela coasts with the Andes and the tropics (Lima 497 mm against 24)."""
    log("climate" + (" with UDel's missing months filled" if fill_gaps else "") + ("" if legacy_udel else " (complete UDel)")
        + (f" blended over {coast_band} cells at the coast" if coast_band else ""))
    # UDel land normals on a 0.5° grid
    tU, latU, lonU = nc(UDEL_AIR, "air")                                          # degC
    pU, _, _ = udel_precip(legacy_udel)                                           # cm / month
    pU_raw = pU
    if fill_gaps:
        # not in Antarctica: its climate comes from NCEP and GPCP (UDel's few stations there stay out, as before)
        pU = fill_month_gaps(pU, lat=latU, south=-60)
    tN, latN, lonN = nc("ncep.reanalysis.derived_surface_gauss_air.2m.mon.ltm.1991-2020.nc", "air")
    tN = tN - 273.15
    pG, latG, lonG = nc("gpcp_precip.mon.ltm.1991-2020.nc", "precip")            # mm / day
    days = np.array([31, 28.25, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31])

    # mean land elevation per UDel cell (0.5° = 30 x 30 source cells), UDel lon runs 0..360
    land_h = np.maximum(S1, 0).astype(np.float32)
    hU = land_h.reshape(360, 30, 720, 30).mean(axis=(1, 3))       # lat 89.75.., lon -179.75..
    hU = np.roll(hU, 360, axis=1)                                  # to lon 0.25..359.75
    # UDel temperatures to sea-level equivalents with a 6.5 °C/km lapse rate
    tU_sl = tU + 0.0065 * hU[None]

    # target 0.25° grid
    CH, CW = 180 * CLIM_RES, 360 * CLIM_RES
    tlat = 90 - (np.arange(CH) + 0.5) / CLIM_RES
    tlon = -180 + (np.arange(CW) + 0.5) / CLIM_RES

    validU = np.isfinite(tU_sl).all(axis=0) & np.isfinite(pU).all(axis=0)
    wU = regrid(validU[None].astype(np.float64), latU, lonU, tlat, tlon)[0]
    tU_f = np.where(np.isfinite(tU_sl), tU_sl, 0)
    pU_f = np.where(np.isfinite(pU), pU, 0)
    tUg = regrid(np.where(validU[None], tU_f, 0), latU, lonU, tlat, tlon)
    pUg = regrid(np.where(validU[None], pU_f, 0), latU, lonU, tlat, tlon)
    with np.errstate(invalid="ignore", divide="ignore"):
        tUg = tUg / wU[None]
        pUg = pUg / wU[None]
    tNg = regrid(tN, latN, lonN, tlat, tlon)
    pGg = regrid(pG, latG, lonG, tlat, tlon)
    if desert_coasts:
        pGg = pGg * desert_coast_factor((pGg * days[:, None, None]).sum(axis=0))[None]

    # land cells use UDel where it has data nearby, the ocean uses NCEP / GPCP; blend over the coast
    wt = np.clip((wU - 0.25) / 0.5, 0, 1)
    # the precipitation's blend weight (the temperatures and the ice sheets below still go by wt, UDel's own coverage)
    wb = coast_band_weight(wU, tlat, coast_band) if coast_band else wt
    # elevation of each 0.25° cell (for NCEP over land we have no model orography; treat as sea level)
    hC = land_h.reshape(CH, 15, CW, 15).mean(axis=(1, 3))
    t_sl = np.where(wt[None] > 0, wt[None] * np.nan_to_num(tUg) + (1 - wt[None]) * tNg, tNg)
    p_mm = np.where(wb[None] > 0, wb[None] * np.nan_to_num(pUg) * 10 + (1 - wb[None]) * pGg * days[:, None, None], pGg * days[:, None, None])
    p_mm = np.maximum(p_mm, 0)

    # Greenland / Antarctic interior: UDel has no stations there; NCEP 2 m temperatures are at the model's
    # own (smooth) ice surface, which is close to the real one. Convert them with the real surface elevation.
    # (the ice sheets are judged by UDel's own coverage, without the filled months: its sparse stations there stay out)
    if fill_gaps:
        valid_raw = np.isfinite(tU_sl).all(axis=0) & np.isfinite(pU_raw).all(axis=0)
        wt_raw = np.clip((regrid(valid_raw[None].astype(np.float64), latU, lonU, tlat, tlon)[0] - 0.25) / 0.5, 0, 1)
    else:
        wt_raw = wt
    ice_region = (wt_raw < 0.5) & (hC > 500)
    t_sl = np.where(ice_region[None], tNg + 0.0065 * hC[None], t_sl)

    t_mean = t_sl.mean(axis=0)
    t_cold = t_sl.min(axis=0)
    t_warm = t_sl.max(axis=0)
    p_year = p_mm.sum(axis=0)

    # VS rainfall (0..255) from the Holdridge aridity: precipitation over potential evapotranspiration
    t_surf = t_sl - 0.0065 * hC[None]
    biotemp = np.clip(t_surf, 0, 30).mean(axis=0)
    # Holdridge PET (58.93 x biotemperature) is too low for cool climates; keep a floor that grows slowly with warmth
    pet = np.maximum(58.93 * biotemp, 450 + 25 * biotemp)
    ai = p_year / pet
    # Calibrated to VS's tree ranges: temperate Europe ~120-140 (oak, pine, spruce, birch), deserts < 30
    xs = np.log([0.01, 0.05, 0.2, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 5.0])
    ys = np.array([0, 15, 50, 95, 115, 130, 150, 170, 200, 235], np.float64)
    rain = np.interp(np.log(np.maximum(ai, 1e-4)), xs, ys)
    # humid tropics: VS's rainforest trees (kapok, fern trees, purpleheart) need 185+, savanna trees (acacia) 80-140
    def smooth(x):
        x = np.clip(x, 0, 1)
        return x * x * (3 - 2 * x)
    rain = rain + 60 * smooth((biotemp - 20) / 5) * smooth((ai - 0.6) / 0.5)

    # Summer drought (Mediterranean climates): aridity of the three warmest months. Where summers are dry but the year
    # is not a desert, vegetation is sparser and drier than the yearly totals suggest: lower VS rainfall by up to 25 %.
    tm = np.clip(t_surf, 0, 30)
    pet_m = np.maximum(58.93 * tm, 450 + 25 * tm) / 12
    warm3 = np.argsort(t_surf, axis=0)[-3:]
    p3 = np.take_along_axis(p_mm, warm3, axis=0).sum(axis=0)
    pet3 = np.take_along_axis(pet_m, warm3, axis=0).sum(axis=0)
    ai_summer = p3 / np.maximum(pet3, 1)
    dry_summer = smooth((0.45 - ai_summer) / 0.35) * smooth((ai - 0.15) / 0.3) * smooth((biotemp - 8) / 4)
    rain = rain * (1 - 0.25 * dry_summer)

    log(f"climate: t_mean {np.nanmin(t_mean):.1f}..{np.nanmax(t_mean):.1f}, rain 0..{rain.max():.0f}, summer-dry cells {np.mean(dry_summer > 0.5) * 100:.1f}%")
    return {"t_mean": t_mean, "t_cold": t_cold, "t_warm": t_warm, "p_year": p_year, "rain": rain, "hC": hC, "dry": dry_summer}


def climate_planes(c):
    """climate.bin's planes as stored (quantised)."""
    return [
        np.clip(np.round(c["t_mean"] * 100), -32000, 32000).astype("<i2"),
        np.clip(np.round(c["t_cold"] * 100), -32000, 32000).astype("<i2"),
        np.clip(np.round(c["t_warm"] * 100), -32000, 32000).astype("<i2"),
        np.clip(np.round(c["p_year"]), 0, 65000).astype("<u2"),
        np.clip(np.round(c["rain"]), 0, 255).astype(np.uint8),
        np.clip(np.round(c["dry"] * 255), 0, 255).astype(np.uint8),
    ]


def write_climate(c):
    """climate.bin version 3 as predicted planes (tools/planecoding.py, EA-0077; plain brotli of the planes before
    2.0.0-rc.14)."""
    path = os.path.join(OUT, "climate.bin")
    pc.write_climate(path, climate_planes(c))
    log(f"climate.bin {os.path.getsize(path) / 1e6:.1f} MB")


def read_climate_planes():
    """climate.bin's planes as stored in earthdata/ (no rebuild), either coding, in their stored types."""
    planes, version = pc.read_climate(os.path.join(OUT, "climate.bin"))
    return [p.astype(pc.DTYPE[t]) for p, t in zip(planes, pc.CLIMATE_TYPES)]


def write_climate_fix(fixed, base, version=1):
    """climatefix.bin (version 1: UDel's filled months, decision 0018), climatefix2.bin (version 2: the complete UDel
    normals, 0018 Correction), climatefix3.bin (version 3: v2's precipitation blended over a coast band, EA-0016) or
    climatefix4.bin (version 4: v3 with GPCP's share at CHELSA's level on desert coasts, EA-0060): the
    cells of climate.bin that change. climate.bin stays as old worlds have it; worlds with EarthParams.ClimateData =
    version apply that file's values on top.
    Versions 1 and 2, each against climate.bin itself: header EMCF, version, width, height, count, then brotli of the cell
    indices (i32) and, plane by plane as in climate.bin, their new values.
    Version 3 and later, against the version before (base is that version's climate; the reader applies the fixes in
    turn), so a new version ships only what it changes (the mod zip's 40 MB limit): header EMCF, version, width, height,
    count, plane mask (i32, bit p for plane p), then brotli of the cell indices as differences from the one before (i32,
    the first from 0) and the new values of the planes in the mask only (the others are the version before's).
    From 2.0.0-rc.14 versions 1 and 2 are written predicted (tools/planecoding.py write_climate_fix, EA-0077): a bitmap of
    the cells and the new values as differences from climate.bin's."""
    # the cells the filled months change (against the same build without them, so rounding noise between library versions
    # in climate.bin itself stays out)
    old = climate_planes(base)
    new = climate_planes(fixed)
    CH, CW = old[0].shape
    changed = np.zeros((CH, CW), bool)
    for a, b in zip(old, new):
        changed |= a != b
    idx = np.flatnonzero(changed).astype("<i4")
    name = "climatefix.bin" if version == 1 else f"climatefix{version}.bin"
    path = os.path.join(OUT, name)
    if version >= 3:
        mask = sum(1 << p for p, (a, b) in enumerate(zip(old, new)) if (a != b).any())
        pc.write_climate_fix(path, version, CW, CH, idx, [p.ravel()[idx] for p in new], None, mask)
    else:
        # against climate.bin as written (read back: the stored planes, which old's rebuild matches but for roundings)
        stored = read_climate_planes()
        pc.write_climate_fix(path, version, CW, CH, idx, [p.ravel()[idx] for p in new], [p.ravel()[idx] for p in stored], climate=stored)
    log(f"{name} {os.path.getsize(path) / 1e6:.2f} MB: {len(idx)} of {CH * CW} cells ({len(idx) / (CH * CW) * 100:.1f}%)")


def check_climate_reproduced(c):
    """The unfilled climate must be the stored climate.bin (but for a few one-step roundings), or the bake has drifted."""
    old = read_climate_planes()
    new = climate_planes(c)
    diff = sum(int((a != b).sum()) for a, b in zip(old, new))
    worst = max(int(np.abs(a.astype(np.int64) - b.astype(np.int64)).max()) for a, b in zip(old, new))
    if diff > 100 or worst > 5:
        raise SystemExit(f"build_climate no longer reproduces climate.bin ({diff} values differ, by up to {worst}); fix that first")
    log(f"climate.bin reproduced ({diff} values differ, by up to {worst} steps: rounding between library versions)")


# ---------------------------------------------------------------------------------------------- weather

WX_RES = 2            # weather.bin cells per degree (0.5°)


def fill_month_gaps(a, min_months=10, lat=None, south=None):
    """The cut-short UDel precipitation (UDEL_LEGACY_PRECIP) lacks November or December in 12 642 land cells (15 % of the
    land, in whole runs of rows). A cell with a missing month counts as no land data, so the coarse ocean data takes over
    and draws one-row stripes across the continents. Fill a cell's missing months (with at least min_months present) from
    the nearest months around them. Only climatefix.bin (decision 0018) needs this: the complete file has no gaps."""
    orig = a
    a = a.copy()
    ok = np.isfinite(orig)
    cells = ok.sum(axis=0) >= min_months
    if south is not None:
        cells &= (np.asarray(lat) > south)[:, None]
    M = orig.shape[0]
    # linear in time between the nearest observed month before and after (around the year), read from the original data
    # only, so the result doesn't depend on which month is filled first
    for m in range(M):
        miss = cells & ~ok[m]
        if not miss.any():
            continue
        prev_v = np.full(miss.shape, np.nan); prev_d = np.zeros(miss.shape)
        next_v = np.full(miss.shape, np.nan); next_d = np.zeros(miss.shape)
        for d in range(1, M):
            pm, nm = (m - d) % M, (m + d) % M
            take = miss & np.isnan(prev_v) & ok[pm]
            prev_v[take] = orig[pm][take]; prev_d[take] = d
            take = miss & np.isnan(next_v) & ok[nm]
            next_v[take] = orig[nm][take]; next_d[take] = d
        both = miss & np.isfinite(prev_v) & np.isfinite(next_v)
        a[m][both] = (prev_v[both] * next_d[both] + next_v[both] * prev_d[both]) / (prev_d[both] + next_d[both])
    return a


def build_weather(S1):
    """
    The monthly normals the weather (Core.Weather) is made from, on a 0.5° grid: precipitation (mm/day, UDel over land,
    GPCP over the sea, blended over the coast like climate.bin), sea-level-equivalent temperature (°C, UDel / NCEP 2 m)
    and the land fraction of each cell from ETOPO. The weather's rain chance, wind belts (the rain band of the tropics
    marks the ITCZ) and where tropical storms form all come from these.
    """
    log("weather")
    tU, latU, lonU = nc(UDEL_AIR, "air")
    pU, _, _ = udel_precip(legacy=False)                                          # cm / month
    # every UDel land cell has all twelve months (weather.bin before 2026-11-07 read a cut-short file: December 0 mm)
    have = np.isfinite(pU).sum(axis=0)
    if ((have > 0) & (have < 12)).any():
        raise SystemExit(f"UDel precipitation lacks months in {int(((have > 0) & (have < 12)).sum())} cells: is {UDEL_PRECIP} complete?")
    tN, latN, lonN = nc("ncep.reanalysis.derived_surface_gauss_air.2m.mon.ltm.1991-2020.nc", "air")
    tN = tN - 273.15
    pG, latG, lonG = nc("gpcp_precip.mon.ltm.1991-2020.nc", "precip")            # mm / day
    days = np.array([31, 28.25, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31])

    land = (S1 > 0).astype(np.float32)
    land_h = np.maximum(S1, 0).astype(np.float32)
    hU = land_h.reshape(360, 30, 720, 30).mean(axis=(1, 3))
    hU = np.roll(hU, 360, axis=1)
    tU_sl = tU + 0.0065 * hU[None]

    H, W = 180 * WX_RES, 360 * WX_RES
    tlat = 90 - (np.arange(H) + 0.5) / WX_RES
    tlon = -180 + (np.arange(W) + 0.5) / WX_RES
    k = 60 // WX_RES
    landfrac = land.reshape(H, k, W, k).mean(axis=(1, 3))

    validU = np.isfinite(tU_sl).all(axis=0) & np.isfinite(pU).all(axis=0)
    wU = regrid(validU[None].astype(np.float64), latU, lonU, tlat, tlon)[0]
    tUg = regrid(np.where(validU[None], np.where(np.isfinite(tU_sl), tU_sl, 0), 0), latU, lonU, tlat, tlon)
    pUg = regrid(np.where(validU[None], np.where(np.isfinite(pU), pU, 0), 0), latU, lonU, tlat, tlon)
    with np.errstate(invalid="ignore", divide="ignore"):
        tUg = tUg / wU[None]
        pUg = pUg / wU[None]
    tNg = regrid(tN, latN, lonN, tlat, tlon)
    pGg = regrid(pG, latG, lonG, tlat, tlon)
    wt = np.clip((wU - 0.25) / 0.5, 0, 1)
    t_sl = np.where(wt[None] > 0, wt[None] * np.nan_to_num(tUg) + (1 - wt[None]) * tNg, tNg)
    # the height each cell's sea-level temperature was reduced from: UDel's share, regridded as its temperatures were
    # (NCEP's 2 m air, where UDel has no stations, is at its own surface and was not reduced)
    hUg = regrid(np.where(validU, hU, 0)[None], latU, lonU, tlat, tlon)[0]
    with np.errstate(invalid="ignore", divide="ignore"):
        h_red = np.where(wt > 0, wt * np.nan_to_num(hUg / wU), 0.0)
    # mm per day: UDel cm/month over land, GPCP mm/day over the sea
    p_day = np.where(wt[None] > 0, wt[None] * np.nan_to_num(pUg) * 10 / days[:, None, None] + (1 - wt[None]) * pGg, pGg)
    p_day = np.maximum(p_day, 0)
    # monthly 10 m winds (m/s, east and north) and total cloud cover (%) from the NCEP/NCAR reanalysis
    uN, latW, lonW = nc("ncep-reanalysis/uwnd.10m.mon.ltm.1991-2020.nc", "uwnd")
    vN, _, _ = nc("ncep-reanalysis/vwnd.10m.mon.ltm.1991-2020.nc", "vwnd")
    cN, latC, lonC = nc("ncep-reanalysis/tcdc.eatm.mon.ltm.1991-2020.nc", "tcdc")
    u = regrid(uN, latW, lonW, tlat, tlon)
    v = regrid(vN, latW, lonW, tlat, tlon)
    cloud = np.clip(regrid(cN, latC, lonC, tlat, tlon), 0, 100)
    log(f"weather: precipitation 0..{p_day.max():.1f} mm/day, temperature {t_sl.min():.1f}..{t_sl.max():.1f} °C, land {landfrac.mean() * 100:.1f}%, "
        f"wind {np.hypot(u, v).max():.1f} m/s at most, cloud cover {cloud.mean():.0f}% on average")
    return {"p": p_day, "t": t_sl, "land": landfrac, "u": u, "v": v, "cloud": cloud, "h": h_red}


# at most this share of the land north of 60°S may store 0 mm (u8 0: under 0.016 mm/day) in any one month: dry seasons in
# the Sahara and Sahel, 2.6 % at worst (August) with the complete UDel normals; the cut-short file gave 87 % in December.
# Antarctica stays out: UDel's few stations there read about 0 mm. tests/WeatherTests checks the stored file the same way.
WX_ZERO_LAND_MAX = 0.04


def check_weather_months(precip_planes, land):
    """Stop before writing a weather.bin in which a month is (almost) dry over much of the land."""
    H = land.shape[0]
    lat = 90 - (np.arange(H) + 0.5) * 180 / H
    on_land = (land >= 0.5) & (lat > -60)[:, None]
    shares = [float(((pl == 0) & on_land).sum()) / on_land.sum() for pl in precip_planes]
    log("weather: land north of 60°S at 0 mm by month " + " ".join(f"{s * 100:.1f}%" for s in shares))
    bad = [m + 1 for m, s in enumerate(shares) if s > WX_ZERO_LAND_MAX]
    if bad:
        raise SystemExit(f"weather: months {bad} are 0 mm over more than {WX_ZERO_LAND_MAX * 100:.0f} % of the land: check the inputs")


def write_weather(w):
    """weather.bin v2: header EMWX, version, width, height, months; brotli of 12 precipitation planes (u8, 32·ln(1+mm/day)),
    12 temperature planes (u8, 2·(°C + 50)), the land fraction (u8, 0..255), then 12 planes each of the 10 m wind east and
    north (i8, 8·m/s) and the total cloud cover (u8, 2.5·%)."""
    p, t, land = w["p"], w["t"], w["land"]
    M, H, W = p.shape
    planes = [np.clip(np.round(32 * np.log1p(p[m])), 0, 255).astype(np.uint8) for m in range(M)]
    check_weather_months(planes[:M], land)
    planes += [np.clip(np.round(2 * (t[m] + 50)), 0, 255).astype(np.uint8) for m in range(M)]
    planes.append(np.clip(np.round(land * 255), 0, 255).astype(np.uint8))
    planes += [np.clip(np.round(8 * w["u"][m]), -127, 127).astype(np.int8) for m in range(M)]
    planes += [np.clip(np.round(8 * w["v"][m]), -127, 127).astype(np.int8) for m in range(M)]
    planes += [np.clip(np.round(2.5 * w["cloud"][m]), 0, 250).astype(np.uint8) for m in range(M)]
    path = os.path.join(OUT, "weather.bin")
    # predicted planes (EA-0077) carrying the signature of the plain file of these planes (worlds freeze it)
    plain = os.path.join(OUT, "weather.plain.bin")
    pc.write_weather(plain, planes, coding=pc.PLAIN)
    signature = pc.fnv1a(open(plain, "rb").read())
    os.remove(plain)
    pc.write_weather(path, planes, signature=signature)
    log(f"weather.bin {os.path.getsize(path) / 1e6:.2f} MB, signature {signature:016x}")


WX_GROUND_STEP = 50   # weatherground.bin's unit, metres


def write_weather_ground(w):
    """weatherground.bin (weather rules 7, EA-0021): for each cell of weather.bin's 0.5° grid the height (m) its sea-level
    temperatures were reduced from with 6.5 °C/km, so the weather knows the ground's temperature for the rain rate (fitted
    at low airports; the High Plains' and the Great Basin's winters snow for longer at their real temperatures). Header
    EMWG, version 1, width, height, metres per unit, then brotli of the u16 heights row by row from the north-west, each
    row as differences from the cell before (i16, the first from 0). 0 at sea and where NCEP's surface air stands in."""
    h = w["h"]
    H, W = h.shape
    q = np.clip(np.round(h / WX_GROUND_STEP), 0, 65535).astype(np.int32)
    steps = np.diff(q, axis=1, prepend=0).astype("<i2")
    path = os.path.join(OUT, "weatherground.bin")
    with open(path, "wb") as f:
        f.write(b"EMWG")
        f.write(struct.pack("<iiii", 1, W, H, WX_GROUND_STEP))
        f.write(brotli.compress(steps.tobytes(), quality=11, lgwin=24))
    log(f"weatherground.bin {os.path.getsize(path) / 1e3:.1f} kB, heights up to {q.max() * WX_GROUND_STEP} m")


# ---------------------------------------------------------------------------------------------- seam path

def seam_path(g, lon_min=150.0, lon_max=215.0):
    """
    The line where the world is cut (it becomes the jump line). One longitude per latitude, found by dynamic
    programming through water, staying as far from land as it can and changing slowly with latitude.
    Floating ice shelves count as water. Returns longitudes (-180..180) for latitudes 90..-90 in 0.1° steps.
    """
    log("seam path")
    H, W = g["elev"].shape
    ocean = (g["flags"] & 1) > 0
    shelf = (g["ice"] > 0) & (g["elev"] - g["ice"] < -50) & (g["elev"] < 200)
    passable = ocean | shelf
    c0 = int((lon_min + 180) * RES)
    c1 = int((lon_max + 180) * RES)
    cols = np.arange(c0, c1) % W
    P = passable[:, cols]
    d = ndi.distance_transform_edt(P)  # cells to the nearest land, inside the window
    lat = 90 - (np.arange(H) + 0.5) / RES
    cost = np.where(P, 1.0 + 60.0 / (1.0 + d), 5000.0)
    cost[np.abs(lat) >= 85] = 1.0
    n = cost.shape[1]
    lam = 3.0
    acc = cost[0].copy()
    back = np.zeros(cost.shape, np.int8)
    for r in range(1, H):
        best = acc.copy()
        arg = np.zeros(n, np.int8)
        for s in (-1, 1):
            shifted = np.full(n, np.inf)
            if s < 0:
                shifted[:-1] = acc[1:] + lam
            else:
                shifted[1:] = acc[:-1] + lam
            better = shifted < best
            best[better] = shifted[better]
            arg[better] = -s  # came from column j - (-s)... stored as the offset to the previous column
        acc = best + cost[r]
        back[r] = arg
    path = np.zeros(H, np.int64)
    path[-1] = int(np.argmin(acc))
    for r in range(H - 1, 0, -1):
        path[r - 1] = path[r] + back[r, path[r]]
    lon = lon_min + (path + 0.5) / RES
    lon = ndi.gaussian_filter1d(lon, 12, mode="nearest")
    # report where the smoothed path touches land
    touched = []
    for r in range(H):
        cc = int((lon[r] - lon_min) * RES)
        if 0 <= cc < n and not P[r, cc] and abs(lat[r]) < 85:
            touched.append(round(float(lat[r]), 2))
    log(f"seam path: lon range {lon.min():.2f}..{lon.max():.2f}, touches land at {len(touched)} rows {touched[:20]}")
    # resample to 0.1° latitude steps
    tl = np.linspace(90, -90, 1801)
    out = np.interp(-tl, -lat, lon)
    out = ((out + 180) % 360) - 180
    return out, touched


# ---------------------------------------------------------------------------------------------- islands

ISLAND_UNIT = 1e5        # outline vertices in 1e-5 degree steps (~1 m)


def island_records(polys, S):
    """The small islands: area (km²), height (m), width (km: twice the distance from the coast to the innermost point)
    and outline. The height is the highest ETOPO 1' cell inside, at most 3 + 100 sqrt(area) m: on reef rims ETOPO has
    50 m hills where a cay a few metres high is, and a 1' cell can't resolve a small island's top anyway."""
    from matplotlib.path import Path
    H1, W1 = S.shape
    out = []
    for area, pts in polys:
        if area >= BIG_LAND_KM2 or pts[:, 1].mean() < -60:
            continue
        lon0, lon1 = pts[:, 0].min(), pts[:, 0].max()
        lat0, lat1 = pts[:, 1].min(), pts[:, 1].max()
        kx = 111.32 * math.cos(math.radians((lat0 + lat1) / 2))
        wkm, hkm = max((lon1 - lon0) * kx, 1e-3), max((lat1 - lat0) * 111.32, 1e-3)
        px = max(wkm, hkm) / 96
        nx, ny = int(wkm / px) + 5, int(hkm / px) + 5
        img = Image.new("L", (nx, ny), 0)
        ImageDraw.Draw(img).polygon([(2 + (x - lon0) * kx / px, 2 + (lat1 - y) * 111.32 / px) for x, y in pts], fill=1)
        m = np.array(img, dtype=bool)
        if m.any():
            d = ndi.distance_transform_edt(m)
            iy, ix = np.unravel_index(np.argmax(d), d.shape)
            width = 2 * float(d[iy, ix]) * px
            clon, clat = lon0 + (ix - 2) * px / kx, lat1 - (iy - 2) * px / 111.32
        else:
            width, clon, clat = 0.5 * min(wkm, hkm), (lon0 + lon1) / 2, (lat0 + lat1) / 2
        r0, r1 = int((90 - lat1) * SRC_RES), int((90 - lat0) * SRC_RES) + 1
        c0, c1 = int((lon0 + 180) * SRC_RES), int((lon1 + 180) * SRC_RES) + 1
        rr, cc = np.mgrid[r0:r1, c0:c1]
        inside = Path(pts).contains_points(np.c_[(cc.ravel() + 0.5) / SRC_RES - 180, 90 - (rr.ravel() + 0.5) / SRC_RES])
        if inside.any():
            top = float(S[np.clip(rr.ravel()[inside], 0, H1 - 1), cc.ravel()[inside] % W1].max())
        else:
            r, c = rc(clat, clon, SRC_RES)
            top = float(S[r, c])
        height = float(np.clip(top, 2.0, 3 + 100 * math.sqrt(area)))
        out.append((area, height, width, pts))
    log(f"{len(out)} small islands, {sum(len(p) for *_, p in out)} outline points")
    return out


def varint(v):
    """Unsigned LEB128 bytes for a uint64 array."""
    v = v.astype(np.uint64)
    n = np.ones(len(v), np.int64)
    for k in range(1, 10):
        n += v >= (np.uint64(1) << np.uint64(7 * k))
    out = np.zeros((len(v), 10), np.uint8)
    for k in range(10):
        out[:, k] = ((v >> np.uint64(7 * k)) & np.uint64(0x7F)).astype(np.uint8) | np.where(n > k + 1, 0x80, 0).astype(np.uint8)
    return out[np.arange(10)[None, :] < n[:, None]].tobytes()


def zigzag(v):
    v = v.astype(np.int64)
    return ((v << 1) ^ (v >> 63)).astype(np.uint64)


def write_islands(planes_a, planes_b, islands):
    """islands.bin: header EMIS, version, grid width, patch count, island count, then brotli of
      patches (sorted by cell): cell index deltas (varint), elevation codes (i16), levels (i16), flags (u8)
      islands: area km² (f32), height m (f32), width km (f32), point count (i32), then all outlines as
               zigzag varint deltas of lon, lat in 1e-5 degrees (each outline starts from the previous one's last point)"""
    ea, la, fa, _ = planes_a
    eb, lb, fb, _ = planes_b
    diff = (ea != eb) | (la != lb) | (fa != fb)
    idx = np.flatnonzero(diff)
    W = ea.shape[1]
    land_a = (fa & 3) == 0
    log(f"islands overlay: {len(idx)} grid cells change, {int((land_a & ~((fb & 3) == 0)).sum())} of them from land to water")
    buf = bytearray()
    buf += varint(np.diff(idx, prepend=0))
    buf += eb.ravel()[idx].astype("<i2").tobytes()
    buf += lb.ravel()[idx].astype("<i2").tobytes()
    buf += fb.ravel()[idx].astype(np.uint8).tobytes()
    buf += np.array([i[0] for i in islands], "<f4").tobytes()
    buf += np.array([i[1] for i in islands], "<f4").tobytes()
    buf += np.array([i[2] for i in islands], "<f4").tobytes()
    buf += np.array([len(i[3]) for i in islands], "<i4").tobytes()
    q = np.round(np.concatenate([i[3] for i in islands]) * ISLAND_UNIT).astype(np.int64)
    d = np.diff(q, axis=0, prepend=np.zeros((1, 2), np.int64))
    buf += varint(zigzag(d.ravel()))
    path = os.path.join(OUT, "islands.bin")
    with open(path, "wb") as f:
        f.write(b"EMIS")
        f.write(struct.pack("<iiii", 1, W, len(idx), len(islands)))
        f.write(brotli.compress(bytes(buf), quality=11, lgwin=24))
    log(f"islands.bin {os.path.getsize(path) / 1e6:.1f} MB: {len(islands)} islands, {len(idx)} grid patches")
    return idx


def bake_islands(g):
    """islands.bin from the grid `g` as stored in terrain.bin: the small islands, and the grid without their land."""
    polys = read_gshhg()
    islands = island_records(polys, g["S1"])
    allowed = big_land_allowed(g["S1"].shape, polys)
    del polys
    planes_a = terrain_planes(g)
    log("the grid again, with only the big land")
    gb, _ = build_terrain(allowed)
    del allowed
    write_islands(planes_a, terrain_planes(gb), islands)


def write_lakes(planes_a, planes_b):
    """lakes.bin: header EMLK, version, grid width, patch count, lake count, then brotli of
      patches as in islands.bin (cell index deltas, elevation codes, levels, flags)
      lakes: level m (f32), seed lat, lon (f32), shore km (f32), kind (u8: 1 revived, 2 natural), salt (u8),
             name (u8 length + utf-8)"""
    ea, la, fa, _ = planes_a
    eb, lb, fb, _ = planes_b
    diff = (ea != eb) | (la != lb) | (fa != fb)
    idx = np.flatnonzero(diff)
    W = ea.shape[1]
    lake_b = (fb & 2) > 0
    log(f"lakes overlay: {len(idx)} grid cells change, {int((lake_b & ~((fa & 2) > 0)).sum())} of them become lake")
    buf = bytearray()
    buf += varint(np.diff(idx, prepend=0))
    buf += eb.ravel()[idx].astype("<i2").tobytes()
    buf += lb.ravel()[idx].astype("<i2").tobytes()
    buf += fb.ravel()[idx].astype(np.uint8).tobytes()
    for name, kind, salt, lv, la_, lo, _box, shore in TERMINAL_LAKES:
        nb = name.encode("utf-8")
        buf += struct.pack("<ffffBBB", lv, la_, lo, shore, 1 if kind == "revived" else 2, 1 if salt else 0, len(nb)) + nb
    path = os.path.join(OUT, "lakes.bin")
    with open(path, "wb") as f:
        f.write(b"EMLK")
        f.write(struct.pack("<iiii", 1, W, len(idx), len(TERMINAL_LAKES)))
        f.write(brotli.compress(bytes(buf), quality=11, lgwin=24))
    log(f"lakes.bin {os.path.getsize(path) / 1e3:.0f} kB: {len(TERMINAL_LAKES)} lakes, {len(idx)} grid patches")


def bake_lakes(g):
    """lakes.bin from the grid `g` as stored in terrain.bin: the grid with the terminal lakes filled."""
    planes_a = terrain_planes(g)
    log("the grid again, with the terminal lakes")
    gb, _ = build_terrain(terminal_lakes=True)
    write_lakes(planes_a, terrain_planes(gb))


OUTLETS_VERSION = 1


def write_outlets(planes_a, planes_b, report):
    """outlets.bin: header EMOT, version, grid width, patch count, then brotli of the patches as in islands.bin (cell
    index deltas, elevation codes, levels, flags): the grid cells that change when the lakes' tongues past their outlet
    are land (outlet_tongues)."""
    ea, la, fa, _ = planes_a
    eb, lb, fb, _ = planes_b
    diff = (ea != eb) | (la != lb) | (fa != fb)
    idx = np.flatnonzero(diff)
    W = ea.shape[1]
    gone = ((fa & 2) > 0) & ~((fb & 2) > 0)
    log(f"outlets overlay: {len(idx)} grid cells change, {int(gone.sum())} of them lake no more")
    buf = bytearray()
    buf += varint(np.diff(idx, prepend=0))
    buf += eb.ravel()[idx].astype("<i2").tobytes()
    buf += lb.ravel()[idx].astype("<i2").tobytes()
    buf += fb.ravel()[idx].astype(np.uint8).tobytes()
    path = os.path.join(OUT, "outlets.bin")
    with open(path, "wb") as f:
        f.write(b"EMOT")
        f.write(struct.pack("<iii", OUTLETS_VERSION, W, len(idx)))
        f.write(brotli.compress(bytes(buf), quality=11, lgwin=24))
    with open(os.path.join(PREVIEW, "outlets-report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=1, ensure_ascii=False)
    log(f"outlets.bin {os.path.getsize(path) / 1e3:.1f} kB: {sum(1 for x in report if x['trimmed'])} tongues, {len(idx)} grid patches")


def bake_outlets(g):
    """outlets.bin from the grid `g` as stored in terrain.bin: the grid with the lakes' tongues past their outlet as land."""
    planes_a = terrain_planes(g)
    log("the grid again, with the outlet tongues as land")
    gb, _ = build_terrain(outlets=True)
    S, _ = load_etopo()
    lake_id, lakes = rasterize_lakes(S.shape)
    drop_reservoir_lakes(lake_id, lakes, S)
    del S
    _, report = outlet_tongues(lake_id, lakes)
    write_outlets(planes_a, terrain_planes(gb), report)


SHORES_VERSION = 1


def bake_shores(g):
    """shores.bin: header EMSH, version, grid width, patch count, near count, then brotli of
      patches as in islands.bin (cell index deltas, elevation codes, levels, flags): the grid cells around lakes whose
               land is eased below the lake's level instead of raised to it (build_terrain's "shores"); only elevations differ
      near:    the cells of the lakes' rings less than fully near a lake: cell index deltas (varint), nearness 0..254 (u8,
               of 255); every other cell counts as 255 (the cells touching a lake, and everything the old rule covers)"""
    ea, la, fa, _ = terrain_planes(g)
    eb, _, _, _ = terrain_planes({**g, "elev": g["shores"]})
    idx = np.flatnonzero(ea != eb)
    W = ea.shape[1]
    drop = (ea.ravel()[idx] - eb.ravel()[idx])
    log(f"shores overlay: {len(idx)} grid cells lower, by up to {int(drop.max()) if len(idx) else 0} elevation steps")
    buf = bytearray()
    buf += varint(np.diff(idx, prepend=0))
    buf += eb.ravel()[idx].astype("<i2").tobytes()
    buf += la.ravel()[idx].astype("<i2").tobytes()
    buf += fa.ravel()[idx].astype(np.uint8).tobytes()
    nq = np.round(g["near_lake"] * 255).astype(np.int32).ravel()
    nidx = np.flatnonzero(nq < 255)
    buf += varint(np.diff(nidx, prepend=0))
    buf += nq[nidx].astype(np.uint8).tobytes()
    log(f"shores nearness: {len(nidx)} ring cells less than fully near a lake")
    path = os.path.join(OUT, "shores.bin")
    with open(path, "wb") as f:
        f.write(b"EMSH")
        f.write(struct.pack("<iiii", SHORES_VERSION, W, len(idx), len(nidx)))
        f.write(brotli.compress(bytes(buf), quality=11, lgwin=24))
    log(f"shores.bin {os.path.getsize(path) / 1e3:.0f} kB: {len(idx)} grid patches")


# ---------------------------------------------------------------------------------------------- coasts

COAST_UNIT = 2e4         # outline vertices in 1/20000 degree steps (~5.6 m: half a block with a 4 million block ring)
COAST_TILE = 1           # degrees per tile side
COAST_MARGIN = 0.05      # degrees: a tile also holds the edges this close to it (distances near its sides)
COAST_SOUTH = -60        # no GSHHG L1 land further south (Antarctica is L5/L6): tiles there have no data


def read_terrain_flags():
    """The 2' flags plane as stored in earthdata/terrain.bin (no rebuild of the grid)."""
    with open(os.path.join(OUT, "terrain.bin"), "rb") as f:
        b = f.read()
    _, w, h, t, count = struct.unpack_from("<iiiii", b, 4)
    flags = np.zeros((h, w), np.uint8)
    tx = w // t
    for k in range(count):
        off, ln = struct.unpack_from("<ii", b, 24 + 8 * k)
        raw = brotli.decompress(b[off:off + ln])
        r, c = divmod(k, tx)
        flags[r * t:(r + 1) * t, c * t:(c + 1) * t] = np.frombuffer(raw[4 * t * t:5 * t * t], np.uint8).reshape(t, t)
    return flags


CANAL_GAPS = [          # GSHHG L1 splits the continents along the canals: boxes (lon0, lat0, lon1, lat1) where that is closed
    ("suez", (32.10, 29.80, 32.75, 31.40)),
    ("panama", (-80.15, 8.80, -79.40, 9.50)),
]
CANAL_NARROW_KM = 0.8   # water narrower than twice this in a gap box (the cut itself) can be closed


def canal_gap_fill(polys, small, bbox):
    """The water in a canal box that the land closes over, as it was before the canal (shapely geometry, lon/lat): the
    water no longer reaching the open sea (the box's sides) once channels narrower than 2·CANAL_NARROW_KM are shut
    (Gatun Lake, the Bitter Lakes, Lake Timsah), and those narrow channels where they lead into such water or join two
    seas (the cut). Narrow inlets with open sea on one side only stay. `polys` the big land (shapely polygons) there;
    `small` the smaller islands, land too for the channels left beside them where they touch what closes (the canal's
    two arms either side of the islands at Port Said; the lagoons around them stay)."""
    from shapely import affinity
    from shapely.geometry import box as sbox
    from shapely.ops import unary_union
    x0, y0, x1, y1 = bbox
    kx, ky = 111.32 * math.cos(math.radians(0.5 * (y0 + y1))), 110.57
    km = lambda g: affinity.scale(g, kx, ky, origin=(0, 0))
    deg = lambda g: affinity.scale(g, 1 / kx, 1 / ky, origin=(0, 0))
    frame = km(sbox(*bbox))
    parts = lambda g: [p for p in getattr(g, "geoms", [g]) if p.geom_type == "Polygon" and not p.is_empty and p.area > 1e-6]
    side = frame.exterior.buffer(1e-3)
    r = CANAL_NARROW_KM

    def split(land):
        water = frame.difference(land)
        wide = water.buffer(-r).buffer(r).intersection(water)
        return wide, water.difference(wide)

    land = unary_union([km(p.buffer(0)) for p in polys]).intersection(frame)
    wide, narrow = split(land)
    seas = [p for p in parts(wide) if p.intersects(side)]
    enclosed = [p for p in parts(wide) if not p.intersects(side)]
    fill = list(enclosed)
    for p in parts(narrow):
        touch = p.buffer(1e-3)
        if any(touch.intersects(e) for e in enclosed) or sum(touch.intersects(s) for s in seas) >= 2:
            fill.append(p)
    if fill and small:
        closed = unary_union(fill).buffer(1e-3)
        islands = [km(p.buffer(0)) for p in small]
        _, narrow = split(unary_union([land] + islands).intersection(frame))
        fill += [p for p in parts(narrow) if p.intersects(closed)]
        # islands the closed water held become part of the land (no hole in the joined outline around them)
        closed = unary_union(fill).buffer(1e-3)
        fill += [p for p in islands if p.intersects(closed)]
    return deg(unary_union(fill)) if fill else deg(frame.difference(frame))


def close_canal_gaps(rings, small):
    """The big land's rings with the canal gaps (CANAL_GAPS) closed: the continents GSHHG splits along the Suez and
    Panama canals joined over the canal and its lakes, as before the canals (the canals are a world option of their own,
    SeaChannels). Rings are (n, 2) lon/lat arrays; `small` the smaller islands (islands.bin) in the boxes, land for what
    is narrow (the channels either side of the islands at Port Said) but not joined here."""
    from shapely.geometry import Polygon
    from shapely.ops import unary_union
    for name, (x0, y0, x1, y1) in CANAL_GAPS:
        inside = lambda p: p[:, 0].min() <= x1 and p[:, 0].max() >= x0 and p[:, 1].min() <= y1 and p[:, 1].max() >= y0
        near = [i for i, p in enumerate(rings) if inside(p)]
        polys = [Polygon(rings[i]) for i in near]
        fill = canal_gap_fill(polys, [Polygon(p) for p in small if inside(p)], (x0, y0, x1, y1))
        if fill.is_empty:
            log(f"coasts: {name} canal: nothing to close")
            continue
        merged = unary_union([p if p.is_valid else p.buffer(0) for p in polys] + [fill])
        out = []
        for g in getattr(merged, "geoms", [merged]):
            out.append(np.asarray(g.exterior.coords, np.float64))
            # (slivers the joins leave between the outlines, a few m² at most, are not water)
            out += [np.asarray(h.coords, np.float64) for h in g.interiors if Polygon(h).area > 1e-6]
        rings = [p for i, p in enumerate(rings) if i not in near] + out
        log(f"coasts: {name} canal closed: {len(near)} outlines joined into {len(out)} rings, "
            f"{fill.area * 111.32 * 110.57 * math.cos(math.radians(0.5 * (y0 + y1))):.0f} km² of water made land")
    return rings


def coast_rings(flags):
    """The outlines coasts.bin holds: the big land (GSHHG L1, BIG_LAND_KM2 and more, north of COAST_SOUTH), and the GSHHG
    lakes (L2) inside it that the grid has as sea for the most part (the IJsselmeer, coastal lagoons): with them the
    outline leaves that water as the grid has it instead of filling it with land. [(points (n, 2) lon/lat, closed)]."""
    rings, big_ids, small = [], set(), []
    r = shapefile.Reader(GSHHG_L1)
    for sr in r.iterShapeRecords():
        d = sr.record.as_dict()
        pts = np.asarray(sr.shape.points, np.float64)
        if d["area"] >= BIG_LAND_KM2 and len(pts) >= 3 and pts[:, 1].max() > COAST_SOUTH:
            rings.append(pts)
            big_ids.add(str(d["id"]).split("-")[0])
        elif len(pts) >= 3 and any(sr.shape.bbox[0] <= x1 and sr.shape.bbox[2] >= x0 and sr.shape.bbox[1] <= y1 and sr.shape.bbox[3] >= y0
                                   for _, (x0, y0, x1, y1) in CANAL_GAPS):
            small.append(pts)
    rings = close_canal_gaps(rings, small)
    n_big = len(rings)
    r = shapefile.Reader(GSHHG_L1.replace("_L1", "_L2"))
    lakes = [(np.asarray(sr.shape.points, np.float64), str(sr.record.as_dict()["parent_id"])) for sr in r.iterShapeRecords()]
    lakes = [(p, pid) for p, pid in lakes if len(p) >= 3 and pid in big_ids]
    H, W = flags.shape
    img = Image.new("I", (W, H), 0)
    draw = ImageDraw.Draw(img)
    for i, (pts, _) in enumerate(lakes, start=1):
        draw.polygon([((x + 180) * RES, (90 - y) * RES) for x, y in pts], fill=i)
    ids = np.array(img).ravel()
    ocean = (flags.ravel() & 1) > 0
    cells = np.bincount(ids, minlength=len(lakes) + 1)
    sea = np.bincount(ids[ocean], minlength=len(lakes) + 1)
    keep = [i for i in range(1, len(lakes) + 1) if cells[i] > 0 and sea[i] >= 0.5 * cells[i]]
    rings += [lakes[i - 1][0] for i in keep]
    log(f"coasts: {n_big} big land outlines, {len(keep)} of {len(lakes)} lakes in them that the grid has as sea, "
        f"{sum(len(p) for p in rings)} points")
    out = []
    for p in rings:
        if (p[0] != p[-1]).any():
            p = np.vstack([p, p[:1]])
        out.append(p)
    return out


COAST_CUT = 0.0015       # degrees (north-south ~170 m): the most a corner is cut per smoothing pass
COAST_PASSES = 2


def smooth_ring(p):
    """GSHHG's full-resolution shorelines are often traced from a raster: straight runs of ~550 m with 45° cuts (the fjords
    of western Norway, the mangrove coasts of Indonesia), staircases ~50 blocks a step in a world with a 4 million block
    ring. The game rounds them off when it decodes a tile (CoastLayer.Smooth), the same as this, which the tile centres'
    land/sea is worked out with: corner cutting (Chaikin: each edge's points a quarter from its ends, at most COAST_CUT;
    the data has no detail below ~200 m to lose), COAST_PASSES times, repeated points dropped first; points on 180°
    stay (GSHHG's cut there). `p` is a ring (degrees) without its closing point."""
    for _ in range(COAST_PASSES):
        p = p[np.r_[(np.diff(p, axis=0) != 0).any(axis=1), (p[-1] != p[0]).any()]]
        q = np.roll(p, -1, axis=0)
        k = np.cos(np.radians(p[:, 1]))
        L = np.hypot((q[:, 0] - p[:, 0]) * k, q[:, 1] - p[:, 1])
        f = np.where(L > 0, np.minimum(0.25, COAST_CUT / np.maximum(L, 1e-300)), 0.25)
        f[(np.abs(p[:, 0]) >= 180) | (np.abs(q[:, 0]) >= 180)] = 0
        a = p + f[:, None] * (q - p)
        b = q - f[:, None] * (q - p)
        p = np.stack([a, b], axis=1).reshape(-1, 2)
    return p


def coast_reference(x0, y0, x1, y1, cx, cy):
    """Inside (odd crossings) of each point (cx, cy) on a grid of rows, by a ray from 180°W: x0..y1 the edges, cx the
    column positions, cy the row positions (same units)."""
    inside = np.zeros((len(cy), len(cx)), bool)
    for j, y in enumerate(cy):
        m = (y0 > y) != (y1 > y)
        xs = np.sort(x0[m] + (y - y0[m]) / (y1[m] - y0[m]) * (x1[m] - x0[m]))
        inside[j] = np.searchsorted(xs, cx) % 2 == 1
    return inside


def write_coasts(rings):
    """coasts.bin: header EMCO, version, unit (vertices per degree), tile size (deg), tiles across, tiles down, margin
    (in units), then a state byte per tile (row 0 at 90°S): bit 0 the tile centre is land, bit 1 the tile has outline
    edges, 255 no data; then (offset, length) of each tile with edges, in tile order; then those tiles, each brotli of
      run count (varint), vertex count per run (varint), then the runs' vertices as zigzag varints (lon, lat): the first
      relative to the tile's south-west corner, the others to the vertex before.
    A tile holds every edge whose bounding box comes within COAST_MARGIN of it (across 180° as well), as runs of
    consecutive edges; the edges along 180° where GSHHG cuts Eurasia and the Chukchi peninsula are left out."""
    unit, m = int(COAST_UNIT), int(round(COAST_MARGIN * COAST_UNIT))
    q = [np.round(p * COAST_UNIT).astype(np.int64) for p in rings]
    start = np.cumsum([0] + [len(p) for p in q])
    V = np.concatenate(q)
    e = np.concatenate([np.arange(start[i], start[i + 1] - 1) for i in range(len(q))])
    x0, y0, x1, y1 = V[e, 0], V[e, 1], V[e + 1, 0], V[e + 1, 1]
    half = 180 * unit
    cut = (np.abs(x0) == half) & (np.abs(x1) == half)
    tiles_x, tiles_y = 360 // COAST_TILE, 180 // COAST_TILE
    tu = COAST_TILE * unit
    log(f"coasts: {len(e)} edges, {int(cut.sum())} of them along 180°")
    # the tile centres' land/sea, from all edges of the outlines as the game smooths them (the cuts along 180° too: a ray
    # from 180°W enters land through them)
    sm = [smooth_ring(p[:-1] / unit) for p in q]
    sm = [np.vstack([p, p[:1]]) for p in sm]
    sv = np.concatenate(sm)
    s0 = np.cumsum([0] + [len(p) for p in sm])
    se = np.concatenate([np.arange(s0[i], s0[i + 1] - 1) for i in range(len(sm))])
    ref = coast_reference(sv[se, 0], sv[se, 1], sv[se + 1, 0], sv[se + 1, 1],
                          (np.arange(tiles_x) + 0.5) * COAST_TILE - 180, (np.arange(tiles_y) + 0.5) * COAST_TILE - 90)
    del sm, sv, se
    keep = ~cut
    ex, x0, y0, x1, y1 = e[keep], x0[keep], y0[keep], x1[keep], y1[keep]
    # edges near 180° also go to the tiles on the other side, shifted by 360°
    shift = np.zeros(len(ex), np.int64)
    lo, hi = np.minimum(x0, x1), np.maximum(x0, x1)
    west, east = lo - m < -half, hi + m >= half
    ex = np.concatenate([ex, ex[west], ex[east]])
    shift = np.concatenate([shift, np.full(int(west.sum()), 360 * unit), np.full(int(east.sum()), -360 * unit)])
    lo, hi = np.concatenate([lo, lo[west], lo[east]]) + shift, np.concatenate([hi, hi[west], hi[east]]) + shift
    blo = np.minimum(V[ex, 1], V[ex + 1, 1])
    bhi = np.maximum(V[ex, 1], V[ex + 1, 1])
    c0 = np.clip((lo - m + half) // tu, 0, tiles_x - 1)
    c1 = np.clip((hi + m + half) // tu, 0, tiles_x - 1)
    r0 = np.clip((blo - m + 90 * unit) // tu, 0, tiles_y - 1)
    r1 = np.clip((bhi + m + 90 * unit) // tu, 0, tiles_y - 1)
    nx, ny = c1 - c0 + 1, r1 - r0 + 1
    rep = nx * ny
    idx = np.repeat(np.arange(len(ex)), rep)
    k = np.arange(len(idx)) - np.repeat(np.cumsum(rep) - rep, rep)
    tile = (r0[idx] + k // nx[idx]) * tiles_x + c0[idx] + k % nx[idx]
    order = np.lexsort((ex[idx], shift[idx], tile))
    tile, edge, sh = tile[order], ex[idx][order], shift[idx][order]
    south = (np.arange(tiles_y) + 0.5) * COAST_TILE - 90 < COAST_SOUTH
    states = np.where(ref, 1, 0).astype(np.uint8)
    states[np.repeat(south, tiles_x).reshape(tiles_y, tiles_x)] = 255
    states = states.ravel()
    bounds = np.flatnonzero(np.diff(tile, prepend=-1, append=len(states) + 1) != 0)
    blobs, total_runs = [], 0
    for a, b in zip(bounds[:-1], bounds[1:]):
        t = int(tile[a])
        if states[t] == 255:
            continue
        ed, s = edge[a:b], sh[a:b]
        brk = np.flatnonzero((np.diff(ed) != 1) | (np.diff(s) != 0)) + 1
        runs = np.split(np.arange(len(ed)), brk)
        ox, oy = (t % tiles_x) * tu - half, (t // tiles_x) * tu - 90 * unit
        counts, pts = [], []
        for run in runs:
            v = V[ed[run[0]]:ed[run[-1]] + 2].copy()
            v[:, 0] += s[run[0]]
            counts.append(len(v))
            d = np.diff(v, axis=0, prepend=np.array([[ox, oy]], np.int64))
            pts.append(d)
        buf = varint(np.array([len(runs)] + counts, np.uint64)) + varint(zigzag(np.concatenate(pts).ravel()))
        blobs.append((t, brotli.compress(buf, quality=11, lgwin=22)))
        states[t] |= 2
        total_runs += len(runs)
    path = os.path.join(OUT, "coasts.bin")
    with open(path, "wb") as f:
        f.write(b"EMCO")
        f.write(struct.pack("<iiiiii", 1, unit, COAST_TILE, tiles_x, tiles_y, m))
        f.write(states.tobytes())
        off = 28 + len(states) + 8 * len(blobs)
        for _, blob in blobs:
            f.write(struct.pack("<ii", off, len(blob)))
            off += len(blob)
        for _, blob in blobs:
            f.write(blob)
    log(f"coasts.bin {os.path.getsize(path) / 1e6:.1f} MB: {len(blobs)} tiles with edges, {total_runs} runs, "
        f"{int(((states & 3) == 1).sum())} land and {int((states == 0).sum())} sea tiles without")


def bake_coasts():
    """coasts.bin: the outlines of the big land, for worlds whose blocks are much smaller than the grid's 2' cells."""
    write_coasts(coast_rings(read_terrain_flags()))


# ---------------------------------------------------------------------------------------------- main

def main():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(PREVIEW, exist_ok=True)
    if "--climate-only" in sys.argv:
        # quick path when only the climate mapping changed: terrain.bin and earth.json stay as they are
        S = tifffile.imread(os.path.join(RAW, "ETOPO_2022_v1_60s_N90W180_surface.tif")).astype(np.float32)
        write_climate(build_climate(S))
        log("done (climate only)")
        return
    if "--climate-fix-only" in sys.argv:
        # climatefix.bin, climatefix2.bin and climatefix3.bin (decision 0018): climate.bin stays; the cells UDel's filled
        # months (v1), the complete UDel normals (v2) and those blended over a coast band (v3) change. Versions to write may
        # follow the flag (default all): a shipped file never changes under its name, so rebuild old ones only to compare.
        args = sys.argv[sys.argv.index("--climate-fix-only") + 1:]
        versions = [int(a) for a in args if a.isdigit()] or [1, 2, 3, 4]
        S = tifffile.imread(os.path.join(RAW, "ETOPO_2022_v1_60s_N90W180_surface.tif")).astype(np.float32)
        base = build_climate(S)
        check_climate_reproduced(base)
        if 1 in versions:
            write_climate_fix(build_climate(S, fill_gaps=True), base)
        if 2 in versions:
            write_climate_fix(build_climate(S, legacy_udel=False), base, version=2)
        if 3 in versions:
            v2 = build_climate(S, legacy_udel=False)
            write_climate_fix(build_climate(S, legacy_udel=False, coast_band=COAST_BAND), v2, version=3)
        if 4 in versions:
            v3 = build_climate(S, legacy_udel=False, coast_band=COAST_BAND)
            write_climate_fix(build_climate(S, legacy_udel=False, coast_band=COAST_BAND, desert_coasts=True), v3, version=4)
        log("done (climate fix only)")
        return
    if "--weather-ground-only" in sys.argv:
        # weatherground.bin only (weather rules 7): weather.bin itself is pinned and stays as it is
        S = tifffile.imread(os.path.join(RAW, "ETOPO_2022_v1_60s_N90W180_surface.tif")).astype(np.float32)
        write_weather_ground(build_weather(S))
        log("done (weather ground only)")
        return
    if "--weather-only" in sys.argv:
        # quick path for the weather's monthly normals: nothing else changes
        S = tifffile.imread(os.path.join(RAW, "ETOPO_2022_v1_60s_N90W180_surface.tif")).astype(np.float32)
        write_weather(build_weather(S))
        log("done (weather only)")
        return
    if "--islands-only" in sys.argv:
        # quick path when only the islands changed: terrain.bin, climate.bin and earth.json stay as they are
        g, _ = build_terrain()
        bake_islands(g)
        log("done (islands only)")
        return
    if "--lakes-only" in sys.argv:
        # quick path when only the terminal lakes changed: terrain.bin, climate.bin and earth.json stay as they are
        g, _ = build_terrain()
        bake_lakes(g)
        log("done (lakes only)")
        return
    if "--shores-only" in sys.argv:
        # quick path when only the lake shores changed: terrain.bin, climate.bin and earth.json stay as they are
        g, _ = build_terrain()
        bake_shores(g)
        log("done (shores only)")
        return
    if "--outlets-report" in sys.argv:
        # which lakes have a tongue past their outlet, and which of those outlets.bin trims (writes nothing in earthdata/)
        S, _ = load_etopo()
        lake_id, lakes = rasterize_lakes(S.shape)
        drop_reservoir_lakes(lake_id, lakes, S)
        del S
        tongue, report = outlet_tongues(lake_id, lakes)
        path = os.path.join(PREVIEW, "outlets-report.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=1, ensure_ascii=False)
        for x in sorted(report, key=lambda x: -x["tongue_km2"]):
            print(f"{'TRIM' if x['trimmed'] else 'keep'} {x['lake'][:28]:28} HL {x['hydrolakes']:>7} pour {x['pour']}  "
                  f"tongue {x['tongue_km2']:7.1f} km2 (gate {x['gate_km']} km), course {x['course_km']:5.1f} km, "
                  f"width {x['width_km']:5.2f} km, HydroLAKES lakes in it {x['lakes_in_km2']:7.1f} km2")
        log(f"report in {path}")
        return
    if "--outlets-only" in sys.argv:
        # outlets.bin: terrain.bin, climate.bin and earth.json stay as they are
        g, _ = build_terrain()
        bake_outlets(g)
        log("done (outlets only)")
        return
    if "--coasts-only" in sys.argv:
        # quick path when only the coasts changed: reads the flags from terrain.bin, everything else stays as it is
        bake_coasts()
        log("done (coasts only)")
        return
    g, lakes = build_terrain()
    write_terrain(g)
    bake_shores(g)
    clim = build_climate(g["S1"])
    write_climate(clim)
    write_climate_fix(build_climate(g["S1"], fill_gaps=True), clim)
    write_weather(build_weather(g["S1"]))
    path, touched = seam_path(g)

    # save small arrays for the previews
    np.savez_compressed(os.path.join(PREVIEW, "grids.npz"), elev=g["elev"], flags=g["flags"], ice=g["ice"],
                        level=g["level"], rough=g["rough"], t_mean=clim["t_mean"], rain=clim["rain"], seam=path)

    meta = {
        "version": 1,
        "terrain": {"file": "terrain.bin", "cellsPerDegree": RES, "width": 360 * RES, "height": 180 * RES, "tile": TILE,
                    "compression": "brotli per tile",
                    "planes": ["elevCode:i16 paeth residual (land 50*expm1(q/700) m, sea -50*expm1(-q/150) m)",
                               "level:i16 m (-32768 none)", "flags:u8 (1 ocean, 2 lake, 4 salt, 8 ice)",
                               "ice:i16 paeth residual, 4 m units"],
                    "landQ": LAND_Q, "seaQ": SEA_Q},
        "climate": {"file": "climate.bin", "cellsPerDegree": CLIM_RES, "width": 360 * CLIM_RES, "height": 180 * CLIM_RES,
                    "compression": "brotli",
                    "planes": ["tMean100:i16", "tCold100:i16", "tWarm100:i16", "precipMm:u16", "vsRain:u8", "drySummer:u8 (0..255)"]},
        "seam": {"latStep": 0.1, "lon": [round(float(v), 3) for v in path], "touchesLand": touched},
        "lakes": len(lakes),
        "attribution": [
            "ETOPO 2022 15/30/60 Arc-Second Global Relief Model, NOAA National Centers for Environmental Information (public domain), doi:10.25921/fd45-gt74",
            "University of Delaware Air Temperature & Precipitation v5.01, provided by NOAA PSL, Boulder, Colorado, USA",
            "NCEP/NCAR Reanalysis 1 long term means, provided by NOAA PSL",
            "GPCP Version 2.3 Combined Precipitation Data Set, provided by NOAA PSL",
            "Natural Earth 1:10m lakes (public domain), naturalearthdata.com",
            "GSHHG 2.3.7 full-resolution shorelines, Wessel, P., and W. H. F. Smith (1996), J. Geophys. Res., 101(B4), 8741-8743 (LGPL)",
        ],
    }
    with open(os.path.join(OUT, "earth.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=1)
    with open(os.path.join(PREVIEW, "lakes.json"), "w", encoding="utf-8") as f:
        json.dump(lakes, f, indent=0, ensure_ascii=False)
    bake_islands(g)
    bake_lakes(g)
    bake_coasts()
    log("done")


if __name__ == "__main__":
    main()
