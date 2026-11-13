"""Offline habitat prototype for a Latvia box (proposal research, not game code).

Water table, soil moisture, wetlands and forest site types from:
  GLO-90 DSM tiles + water-body masks (Earth rawdata), SoilGrids 2.0 texture/pH clips (ground-column pilot),
  Earth climate.bin (rain). OSM natural=wetland polygons are used only for comparison, never as an input.

usage: python -I habitat_proto.py <earth rawdata dir> <soilgrids dir> <climate.bin> <osm wetlands json> <out dir>
"""
import sys, os, glob, heapq, json, math, zlib
import numpy as np, tifffile

RAW, SG, CLIM, OSM, OUT = sys.argv[1:6]
os.makedirs(OUT, exist_ok=True)
LAT0, LAT1, LON0, LON1 = 55.65, 58.10, 20.95, 28.25
F = 2                                   # GLO-90 cells per prototype cell (3" x 4.5" -> 6" x 9", ~185 x 155 m)
DLAT, DLON = F / 1200, F * 0.00125
H = round((LAT1 - LAT0) / DLAT); W = round((LON1 - LON0) / DLON)
lat_c = LAT1 - (np.arange(H) + 0.5) * DLAT
lon_c = LON0 + (np.arange(W) + 0.5) * DLON
DY = DLAT * 111320.0
DX = DLON * 111320.0 * math.cos(math.radians((LAT0 + LAT1) / 2))
CELL_KM2 = DX * DY / 1e6

# ------------------------------------------------------------------ inputs
def mosaic(kind):
    """kind DEM or WBM; returns full-res array covering the box (NaN / 255 where no tile = open sea)."""
    h = round((LAT1 - LAT0) * 1200); w = round((LON1 - LON0) * 800)
    out = np.full((h, w), np.nan if kind == 'DEM' else 1, np.float32 if kind == 'DEM' else np.uint8)
    sub = 'copernicus-glo90' if kind == 'DEM' else 'copernicus-glo90-wbm'
    for la in range(int(math.floor(LAT0)), int(math.ceil(LAT1))):
        for lo in range(int(math.floor(LON0)), int(math.ceil(LON1))):
            f = os.path.join(RAW, sub, f'Copernicus_DSM_COG_30_N{la:02d}_00_E{lo:03d}_00_{kind}.tif')
            if not os.path.exists(f): continue
            t = tifffile.imread(f)               # 1200 x 800, top-left at (lo, la+1)
            r0 = round((LAT1 - (la + 1)) * 1200); c0 = round((lo - LON0) * 800)
            rs, cs = max(r0, 0), max(c0, 0)
            re, ce = min(r0 + 1200, h), min(c0 + 800, w)
            if rs >= re or cs >= ce: continue
            out[rs:re, cs:ce] = t[rs - r0:re - r0, cs - c0:ce - c0]
    return out

dem_f = mosaic('DEM'); wbm_f = mosaic('WBM')
def block(a, fn):
    return fn(a[:H * F, :W * F].reshape(H, F, W, F), axis=(1, 3))
z = block(dem_f, np.nanmean).astype(np.float64)
ocean = block((wbm_f == 1).astype(np.float32), np.mean) >= 0.5
lake = block((wbm_f == 2).astype(np.float32), np.mean) >= 0.5
river = block((wbm_f == 3).astype(np.float32), np.max) > 0
ocean |= np.isnan(z)
z[np.isnan(z)] = 0.0
if os.environ.get('GROUND_OPEN'):
    # crude canopy removal test: grey opening removes raised patches narrower than the window (forest blocks, towns)
    from scipy import ndimage as _nd
    z = _nd.grey_opening(z, size=int(os.environ['GROUND_OPEN']))
del dem_f, wbm_f

def sg(name):
    """SoilGrids clip (0.01 deg, top-left 20E 60N) sampled at the prototype cells (nearest)."""
    a = tifffile.imread(os.path.join(SG, name)).astype(np.float64)
    r = np.clip(((60.0 - lat_c) / 0.01).astype(int), 0, a.shape[0] - 1)
    c = np.clip(((lon_c - 20.0) / 0.01).astype(int), 0, a.shape[1] - 1)
    return a[np.ix_(r, c)]
sand_top, clay_top = sg('sand_15-30cm.tif') / 10, sg('clay_15-30cm.tif') / 10
sand_sub = (sg('sand_60-100cm.tif') + sg('sand_100-200cm.tif')) / 20
clay_sub = (sg('clay_60-100cm.tif') + sg('clay_100-200cm.tif')) / 20
ph = sg('phh2o_15-30cm.tif') / 10
nodata_soil = (sand_top <= 0) & (clay_top <= 0)            # towns and water read 0 in the clips

# Ground columns (Earth/src/Core/GroundColumns.cs, ground-baltic-v1.bin): WRB soil group, GUM cover class, clay and pH at
# 0.01 degrees. GROUND=<path> replaces the SoilGrids stand-ins for permeability, dryness and richness.
GROUND = os.environ.get('GROUND')
if GROUND:
    import struct, zlib
    gb = open(GROUND, 'rb').read()
    assert gb[:4] == b'EGCB'
    gnx, gny = struct.unpack('<HH', gb[6:10]); gw, gn, gs = struct.unpack('<3d', gb[10:34])
    body = zlib.decompress(gb[34:]); gnn = gnx * gny
    def _u8(k): return np.frombuffer(body, np.uint8, gnn, 4 * gnn + k * gnn).reshape(gny, gnx)
    g_cover, g_soil, g_clay, g_ph = _u8(0), _u8(2), _u8(3), _u8(4)
    gr = np.clip(((gn - lat_c) / gs).astype(int), 0, gny - 1); gc = np.clip(((lon_c - gw) / gs).astype(int), 0, gnx - 1)
    def _g(a): return a[np.ix_(gr, gc)]
    cover, soilg, gclay, gph = _g(g_cover), _g(g_soil), _g(g_clay).astype(float), _g(g_ph) / 10.0
    SG_NAMES = ["Acrisols", "Albeluvisols", "Alisols", "Andosols", "Arenosols", "Calcisols", "Cambisols", "Chernozems", "Cryosols", "Durisols",
        "Ferralsols", "Fluvisols", "Gleysols", "Gypsisols", "Histosols", "Kastanozems", "Leptosols", "Lixisols", "Luvisols", "Nitisols",
        "Phaeozems", "Planosols", "Plinthosols", "Podzols", "Regosols", "Solonchaks", "Solonetz", "Stagnosols", "Umbrisols", "Vertisols"]
    SGI = {n: i for i, n in enumerate(SG_NAMES)}
    def is_soil(*names): return np.isin(soilg, [SGI[n] for n in names])
    COV = ["thin", "till", "lakeclay", "outwash", "alluvium", "peat", "dune", "marine", "loess"]
    def is_cover(*names): return np.isin(cover, [COV.index(n) for n in names])
    print('ground soil groups in box:', {SG_NAMES[k]: int(v) for k, v in zip(*np.unique(soilg, return_counts=True)) if k < len(SG_NAMES)})

try:
    import subprocess
    # node has brotli; decode the rain plane once (climate.bin layout: see EarthData.ReadClimate)
    js = ("const f=require('fs').readFileSync(process.argv[1]);const z=require('zlib');const W=f.readInt32LE(8),H=f.readInt32LE(12),n=W*H;"
          "const r=z.brotliDecompressSync(f.subarray(16));process.stdout.write(JSON.stringify({W,H,rain:Array.from(r.subarray(8*n,9*n))}));")
    cj = json.loads(subprocess.run(['node', '-e', js, CLIM], capture_output=True, text=True, check=True).stdout)
    cw, chh, rainp = cj['W'], cj['H'], np.array(cj['rain'], np.float64).reshape(cj['H'], cj['W'])
    rr = np.clip(((90 - lat_c) * 4).astype(int), 0, chh - 1); cc = np.clip(((lon_c + 180) * 4).astype(int), 0, cw - 1)
    rain = rainp[np.ix_(rr, cc)]
except Exception as e:
    print('climate.bin not read, using rain 125:', e); rain = np.full((H, W), 125.0)

# ------------------------------------------------------------------ drainage: priority flood, D8 routing over the filled surface
water = ocean | lake | river
CACHE = os.environ.get("DRAIN_CACHE", os.path.join(OUT, "drainage-cache.npz"))   # drainage depends only on heights and water masks
STREAM_KM2 = 3.0
if os.path.exists(CACHE):
    _c = np.load(CACHE); acc, base, xdown, xup, zf = (_c[k] for k in ("acc", "base", "xdown", "xup", "zf"))
    print("drainage from cache")
else:
    Hp, Wp = H + 2, W + 2
    zp = np.full((Hp, Wp), -1e9); zp[1:-1, 1:-1] = z
    visited = np.zeros((Hp, Wp), bool); visited[0, :] = visited[-1, :] = visited[:, 0] = visited[:, -1] = True
    zf = zp.copy()
    down = np.full(Hp * Wp, -1, np.int64)
    zl = zp.ravel().tolist(); zfl = zf.ravel().tolist(); vis = visited.ravel().tolist(); dl = [-1] * (Hp * Wp)
    heap = []
    oceanp = np.zeros((Hp, Wp), bool); oceanp[1:-1, 1:-1] = ocean
    edge = np.zeros((Hp, Wp), bool); edge[1, 1:-1] = edge[-2, 1:-1] = edge[1:-1, 1] = edge[1:-1, -2] = True
    for i in np.flatnonzero((oceanp | edge).ravel()).tolist():
        vis[i] = True; heapq.heappush(heap, (zl[i], i))
    offs = [-Wp - 1, -Wp, -Wp + 1, -1, 1, Wp - 1, Wp, Wp + 1]
    order = []
    EPS = 1e-4
    while heap:
        zc, c = heapq.heappop(heap)
        order.append(c)
        for o in offs:
            n = c + o
            if vis[n]: continue
            vis[n] = True
            zn = zl[n]
            if zn <= zc: zn = zc + EPS
            zfl[n] = zn; dl[n] = c
            heapq.heappush(heap, (zn, n))
    zf = np.array(zfl).reshape(Hp, Wp)[1:-1, 1:-1]
    print('flood done', len(order))

    acc = [1.0] * (Hp * Wp)
    for c in reversed(order):
        d = dl[c]
        if d >= 0: acc[d] += acc[c]
    acc = np.array(acc).reshape(Hp, Wp)[1:-1, 1:-1] * CELL_KM2      # upslope area km2

    streamp = np.zeros((Hp, Wp), bool); streamp[1:-1, 1:-1] = water | (acc >= STREAM_KM2)
    st = streamp.ravel().tolist()
    base = [0.0] * (Hp * Wp)
    for c in order:
        d = dl[c]
        base[c] = zl[c] if (st[c] or d < 0) else base[d]
    base = np.array(base).reshape(Hp, Wp)[1:-1, 1:-1]
    # flow distance down to the drainage (x) and longest flow path up to the divide; L = x + up = hillslope length
    DIAG = math.hypot(DX, DY)
    def step(o):
        return DX if o in (1, -1) else DY if o in (Wp, -Wp) else DIAG
    dd = [0.0] * (Hp * Wp)
    for c in order:
        d = dl[c]
        if not (st[c] or d < 0): dd[c] = dd[d] + step(d - c)
    ul = [0.0] * (Hp * Wp)
    for c in reversed(order):
        d = dl[c]
        if d >= 0 and not st[c]:
            v = ul[c] + step(d - c)
            if v > ul[d]: ul[d] = v
    xdown = np.array(dd).reshape(Hp, Wp)[1:-1, 1:-1]
    xup = np.array(ul).reshape(Hp, Wp)[1:-1, 1:-1]
    del dd, ul
    np.savez_compressed(CACHE, acc=acc, base=base, xdown=xdown, xup=xup, zf=zf)
hand = np.clip(z - base, 0, None)
fill = np.clip(zf - z - 0.05, 0, None)                         # depth of closed hollows (m)

gy, gx = np.gradient(z, DY, DX)
slope = np.hypot(gx, gy)
twi = np.log((acc * 1e6 / math.sqrt(DX * DY)) / np.maximum(slope, 1e-3))

# ------------------------------------------------------------------ OSM wetlands: comparison, or with MAPPED=1 the stand-in for mapped wetlands (GLWD/PEATMAP later)
from PIL import Image, ImageDraw
osm_img = Image.new('L', (W, H), 0)
dr = ImageDraw.Draw(osm_img)
KIND = {'bog': 1, 'string_bog': 1, 'swamp': 2, 'marsh': 3, 'reedbed': 3, 'fen': 4, 'wet_meadow': 5}
osm_counts = {}
def px(pt):
    return ((pt['lon'] - LON0) / DLON, (LAT1 - pt['lat']) / DLAT)
def lonlat_px(x, y):
    return ((x - LON0) / DLON, (LAT1 - y) / DLAT)
def ring_area(pts):
    return 0.5 * sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]))
srcs = OSM.split(',')
country = None
# 1) extent: Geofabrik shapefile water polygons with fclass "wetland" (outer rings clockwise, holes counter-clockwise)
for f in [x for x in srcs if x.endswith('.shp')]:
    sys.path.insert(0, os.environ['PYSHP_DIR']); import shapefile
    r = shapefile.Reader(f)
    fi = [fld[0] for fld in r.fields[1:]].index('fclass')
    n = 0
    for sr in r.iterShapeRecords():
        fc = sr.record[fi]
        if not fc.startswith('wetland'): continue
        n += 1
        kind = fc[8:] if fc.startswith('wetland_') else 'unspecified'
        val = KIND.get(kind, 6); osm_counts[kind] = osm_counts.get(kind, 0) + 1
        pts, parts = sr.shape.points, list(sr.shape.parts) + [len(sr.shape.points)]
        rings = [pts[a:b] for a, b in zip(parts, parts[1:])]
        for ring in rings:
            if len(ring) >= 3 and ring_area(ring) < 0: dr.polygon([lonlat_px(x, y) for x, y in ring], fill=val)
        for ring in rings:
            if len(ring) >= 3 and ring_area(ring) > 0: dr.polygon([lonlat_px(x, y) for x, y in ring], fill=0)
    osm_counts['shapefile_wetland_polygons'] = n
# the extract's boundary (Geofabrik .poly): statistics only inside it
for f in [x for x in srcs if x.endswith('.poly')]:
    cimg = Image.new('L', (W, H), 0); cd = ImageDraw.Draw(cimg); ring = []; inner = False
    for line in open(f, encoding='utf-8').read().splitlines()[1:]:
        t = line.split()
        if not t: continue
        if t[0] == 'END':
            if ring: cd.polygon(ring, fill=0 if inner else 1)
            ring = []
            continue
        if len(t) == 1: inner = t[0].startswith('!'); continue
        ring.append(lonlat_px(float(t[0]), float(t[1])))
    country = np.array(cimg) > 0
# 2) wetland kinds where tagged: Overpass ways (bog, swamp, marsh, fen, wet meadow) drawn over the extent
try:
    elements = []
    for f in [x for x in srcs if x.endswith('.json')]:
        elements += [e for e in json.load(open(f, encoding='utf-8')).get('elements', [])
                     if e['type'] == 'way' or any('geometry' in m for m in e.get('members', []))]
    for el in elements:
        kind = el.get('tags', {}).get('wetland', 'unspecified')
        val = KIND.get(kind, 6)
        osm_counts[kind] = osm_counts.get(kind, 0) + 1
        rings = []
        if el['type'] == 'way' and 'geometry' in el:
            rings = [(el['geometry'], 'outer')]
        elif el['type'] == 'relation':
            rings = [(m['geometry'], m.get('role', 'outer')) for m in el.get('members', []) if m.get('type') == 'way' and 'geometry' in m]
        for g, role in rings:
            if len(g) >= 3 and role != 'inner':
                dr.polygon([px(p) for p in g], fill=val)
except Exception as e:
    print('OSM json not read:', e)
osm = np.array(osm_img)
land = ~water
if country is not None: land &= country

# ------------------------------------------------------------------ water table
def smooth(x):
    x = np.clip(x, 0, 1); return x * x * (3 - 2 * x)
# SoilGrids is smooth in Latvia (topsoil sand 41-58 % between the 10th and 90th percentile), so the classes are relative
# stand-ins until the ground column's Quaternary map gives real outwash sand, till and clay
perm = np.where(sand_sub >= 57, 2, np.where(clay_sub >= 22, 0, 1))
perm = np.where(nodata_soil, 1, perm)
if GROUND:
    # sandy soils and outwash/dune/marine cover drain fast; clay-rich, stagnic and lake-clay ones hold water up
    sandy_g = is_soil('Arenosols', 'Podzols') | is_cover('outwash', 'dune', 'marine')
    tight_g = is_soil('Stagnosols', 'Planosols', 'Vertisols') | is_cover('lakeclay') | (gclay >= 30)
    perm = np.where(sandy_g, 2, np.where(tight_g, 0, 1))
MODEL = os.environ.get('WT_MODEL', 'mound')
MAPPED = os.environ.get('MAPPED') == '1'
mapped = (osm > 0) & ~water if MAPPED else np.zeros((H, W), bool)
KMUL = float(os.environ.get('K_MUL', '1'))
if MODEL == 'hand':
    # v1: the table sinks a fixed fraction of the height above drainage
    K = np.array([0.30, 0.55, 0.90])[perm]
    dry = smooth((120 - rain) / 60)
    K = K + (1 - K) * dry
    WT_DEPTH = 0.25 + K * hand
    perched = (perm == 0) & (slope < 0.004) & (rain > 110)
    WT_DEPTH = np.where(perched, np.minimum(WT_DEPTH, 0.6), WT_DEPTH)
    WT_DEPTH = WT_DEPTH - fill
    mound = np.zeros_like(hand)
else:
    # v2: steady groundwater mound between drainages (Dupuit): h^2 = R/K (2 L x - x^2), x from the drainage, L to the divide.
    # Recharge R (m/yr) from the climate's water surplus; K (m/yr, effective) from the subsoil class.
    R = 0.15 * smooth((rain - 60) / 80)
    Kc = np.array([30.0, 300.0, 5000.0])[perm] * KMUL          # clay/till-clay, loam/till, sand/gravel
    if MAPPED: Kc = np.where(mapped & np.isin(osm, [1, 4, 6]), 3.0 * KMUL, Kc)   # mapped peat: very low lateral flow
    L = xdown + xup
    mound = np.sqrt(np.maximum(R / Kc * (2 * L * xdown - xdown ** 2), 0))
    WT_DEPTH = hand - mound                                     # < 0: groundwater reaches the surface (seepage)
    WT_DEPTH = np.maximum(WT_DEPTH, 0) + 0.1 - np.minimum(fill, 0.5 if MAPPED else 2.0)
WT_DEPTH = np.where(water, -1.0, WT_DEPTH)
recharge_side = (xdown > 0.5 * (xdown + xup)) | (xup < 200)     # upper half of the hillslope: rain-fed

# ------------------------------------------------------------------ moisture and richness
sandy_top = sand_top >= 55
very_dry = (sand_top >= 58) & (hand > 8)
if GROUND:
    sandy_top = is_soil('Arenosols', 'Podzols', 'Regosols') | is_cover('dune', 'marine')
    very_dry = (is_soil('Arenosols') | is_cover('dune')) & (hand > 3) | is_soil('Podzols') & (hand > 20)
moist = np.full((H, W), 2, np.int8)                             # 2 fresh
moist[(WT_DEPTH >= 2) & sandy_top] = 1                          # 1 dry
moist[(WT_DEPTH >= 2) & very_dry] = 0                           # 0 very dry: dunes, eskers
moist[WT_DEPTH < 2.0] = 3                                       # moist
moist[WT_DEPTH < 1.0] = 4                                       # wet
moist[WT_DEPTH < 0.3] = 5                                       # waterlogged
moist[WT_DEPTH < 0.0] = 6                                       # flooded (hollow)
if MAPPED:
    from scipy import ndimage as _nd
    slope_1km = _nd.uniform_filter(np.minimum(slope, 0.05), size=7)
    riparian = (xdown < 320) & (hand < 0.5)
    small_mire = (moist >= 5) & (slope_1km < 0.0025) & ~riparian       # kettle and valley mires picked from the table
    moist[(moist >= 5) & ~mapped & ~small_mire] = 4                    # elsewhere wet, not a mire
    moist[mapped] = np.maximum(moist[mapped], 5)
moist[water] = 7

rich = np.full((H, W), 2, np.int8)                               # 0 A poor .. 3 D rich
rich[sand_top >= 55] = 0
rich[(sand_top >= 51) & (sand_top < 55)] = 1
rich[(clay_top >= 20) | ((ph >= 6.5) & (sand_top < 50))] = 3
if GROUND:
    # richness by WRB group: podzols and arenosols poor (A), albeluvisols/stagnic soils and regosols B, cambisols and
    # luvisols C, rising to D with clay, base-rich (pH >= 6.3) parent material or humus-rich groups
    rich = np.full((H, W), 2, np.int8)
    rich[is_soil('Arenosols', 'Podzols')] = 0
    rich[is_soil('Regosols', 'Planosols', 'Stagnosols', 'Umbrisols')] = 1
    # Albeluvisols span sandy loam to loam: the lighter 40 % by clay are B (pine-spruce, blueberry), the rest C
    alb = is_soil('Albeluvisols')
    if alb.any(): rich[alb & (gclay <= np.quantile(gclay[alb], .4))] = 1
    rich[is_soil('Phaeozems', 'Chernozems', 'Fluvisols', 'Vertisols') | is_soil('Luvisols', 'Cambisols', 'Gleysols') & ((gph >= 6.3) | (gclay >= 27))] = 3
    # Gleysols: groundwater soils, at least moist (wet where the table is within 1.5 m); Histosols: peat (a mire unless the map says otherwise)
    gley = is_soil('Gleysols') & ~water
    moist[gley] = np.maximum(moist[gley], np.where(WT_DEPTH[gley] < 1.5, 4, 3))
    histo = (is_soil('Histosols') | is_cover('peat')) & ~water
    moist[histo] = np.maximum(moist[histo], 5)

# ------------------------------------------------------------------ habitats
HAB = [  # code, label, colour
    ('water', 'open water', '#2f6db5'),
    ('sils', 'lichen pine (sils)', '#e8dc9a'),
    ('metrajs', 'pine, cowberry/heather (mētrājs)', '#cfc070'),
    ('lans', 'pine-spruce, blueberry (lāns)', '#9fb36a'),
    ('veris', 'spruce, oxalis (vēris, damaksnis)', '#5c8f4a'),
    ('garsa', 'oak-ash-lime-hazel (gārša)', '#2f6b2b'),
    ('wetpoor', 'wet pine-birch, sphagnum (slapjais mētrājs)', '#9db8a0'),
    ('wetrich', 'wet birch-spruce-alder (slapjais vēris/gārša)', '#5f9a86'),
    ('bog', 'raised bog (purvājs)', '#b5527a'),
    ('fen', 'fen / transition mire', '#d58bb0'),
    ('carr', 'black alder swamp (liekņa)', '#6b3f69'),
    ('marsh', 'marsh / reedbed', '#7fc6c2'),
    ('swampforest', 'birch-pine swamp forest', '#8a6d8f'),
    ('meadow', 'floodplain meadow', '#c8e07a'),
    ('dunepine', 'coastal dune pine (kapu priežu mežs)', '#f2d36b'),
    ('foredune', 'beach and foredune', '#fff3c4'),
]
HI = {h[0]: i for i, h in enumerate(HAB)}
hab = np.full((H, W), HI['veris'], np.int8)
hab[(moist == 0) & (rich == 0)] = HI['sils']
hab[(moist == 1) & (rich == 0)] = HI['metrajs']
hab[(moist <= 1) & (rich == 1)] = HI['metrajs']
hab[(moist <= 1) & (rich >= 2)] = HI['veris']
hab[(moist == 2) & (rich == 0)] = HI['metrajs']
hab[(moist == 2) & (rich == 1)] = HI['lans']
hab[(moist == 2) & (rich == 2)] = HI['veris']
hab[(moist == 2) & (rich == 3)] = HI['garsa']
hab[(moist == 3) & (rich <= 1)] = HI['lans']
hab[(moist == 3) & (rich == 2)] = HI['veris']
hab[(moist == 3) & (rich == 3)] = HI['garsa']
hab[(moist == 4) & (rich <= 1)] = HI['wetpoor']
hab[(moist == 4) & (rich >= 2)] = HI['wetrich']
wl = moist >= 5
# rain-fed bog away from the drainage on flat ground; groundwater-fed fen or swamp in valleys and hollows
bogsite = wl & (slope < 0.006) & ((hand >= 1.5) if MODEL == 'hand' else (recharge_side & (xdown > 400)))
hab[wl & ~bogsite & (rich <= 1)] = HI['fen']
hab[wl & ~bogsite & (rich >= 2)] = HI['carr']
hab[bogsite] = HI['bog']
from scipy import ndimage
near_lake = ndimage.binary_dilation(lake, iterations=2)
hab[wl & near_lake] = HI['marsh']
big_river = river | (acc >= 300)
near_big = ndimage.binary_dilation(big_river, iterations=4)
hab[(moist >= 3) & (moist <= 5) & near_big & (hand < 1.5) & ~bogsite] = HI['meadow']
if MAPPED:
    # mapped kinds decide the wetland type; unspecified ones follow the model rule above
    hab[mapped & (osm == 1)] = HI['bog']
    hab[mapped & (osm == 2) & (rich >= 2)] = HI['carr']
    hab[mapped & (osm == 2) & (rich <= 1)] = HI['swampforest']
    hab[mapped & (osm == 3)] = HI['marsh']
    hab[mapped & (osm == 4)] = HI['fen']
    hab[mapped & (osm == 5)] = HI['meadow']
# coast: the beach and white foredune within ~250 m of the sea, then grey-dune pine on sand within ~2 km
from scipy import ndimage as _ndc
sea_d = _ndc.distance_transform_edt(~ocean, sampling=(DY, DX))
coastal_sand = (sea_d < 2000) & (moist <= 3) & (sandy_top | (rich == 0))
hab[coastal_sand & (sea_d < 2000)] = HI['dunepine']
hab[(sea_d < 250) & ~water & (moist <= 4)] = HI['foredune']
hab[water] = HI['water']


# ------------------------------------------------------------------ statistics
stats = {'grid': [H, W], 'cell_m': [round(DY), round(DX)], 'stream_km2': STREAM_KM2,
         'land_cells': int(land.sum()), 'osm_elements_by_wetland_tag': osm_counts}
def frac(m): return round(float((m & land).sum()) / land.sum() * 100, 2)
stats['habitat_percent_of_land'] = {HAB[i][0]: frac(hab == i) for i in range(1, len(HAB))}
stats['moisture_percent_of_land'] = {str(k): frac(moist == k) for k in range(0, 7)}
pred_wl = land & np.isin(hab, [HI['bog'], HI['fen'], HI['carr'], HI['marsh'], HI['swampforest']])
osm_wl = land & (osm > 0)
osm_mire = land & np.isin(osm, [1, 2, 3, 4])
stats['predicted_wetland_percent'] = frac(pred_wl)
stats['config'] = {'model': MODEL, 'k_mul': KMUL, 'mapped_wetlands_input': MAPPED}
if MAPPED:
    stats['unmapped_small_mire_percent'] = frac(pred_wl & ~mapped)
    stats['moisture_percent_outside_mapped'] = {str(k): round(float(((moist == k) & land & ~mapped).sum()) / (land & ~mapped).sum() * 100, 2) for k in range(0, 7)}
stats['osm_wetland_percent'] = frac(osm_wl)
tp = (pred_wl & osm_wl).sum()
stats['wetland_precision'] = round(float(tp / max(pred_wl.sum(), 1)), 3)
stats['wetland_recall'] = round(float(tp / max(osm_wl.sum(), 1)), 3)
# a tolerant match: within ~500 m
osm_near = ndimage.binary_dilation(osm_wl, iterations=3)
pred_near = ndimage.binary_dilation(pred_wl, iterations=3)
stats['wetland_precision_500m'] = round(float((pred_wl & osm_near).sum() / max(pred_wl.sum(), 1)), 3)
stats['wetland_recall_500m'] = round(float((osm_wl & pred_near).sum() / max(osm_wl.sum(), 1)), 3)
conf = {}
for name, v in [('osm_bog', 1), ('osm_swamp', 2), ('osm_marsh', 3), ('osm_fen', 4), ('osm_wet_meadow', 5), ('osm_other', 6)]:
    m = land & (osm == v)
    if m.sum() == 0: continue
    conf[name] = {'cells': int(m.sum()), **{HAB[i][0]: round(float((hab[m] == i).mean() * 100), 1) for i in range(1, len(HAB)) if (hab[m] == i).mean() >= 0.02}}
stats['what_the_model_says_inside_osm_wetlands_percent'] = conf
stats['wt_depth_m_percentiles_land'] = {p: round(float(np.percentile(WT_DEPTH[land], p)), 2) for p in (5, 25, 50, 75, 95)}
stats['hand_m_percentiles_land'] = {p: round(float(np.percentile(hand[land], p)), 2) for p in (5, 25, 50, 75, 95)}
json.dump(stats, open(os.path.join(OUT, 'stats.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
print(json.dumps(stats, indent=1))

# ------------------------------------------------------------------ maps
import matplotlib; matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.colors import ListedColormap, BoundaryNorm
from matplotlib.patches import Patch
ext = [LON0, LON1, LAT0, LAT1]
asp = 1 / math.cos(math.radians(56.9))
SITES = {'Ķemeri bog': (56.93, 23.48), 'Cena bog': (56.86, 23.87), 'Teiči bog': (56.62, 26.30), 'Riga': (56.95, 24.10),
         'Jelgava': (56.65, 23.72), 'Gauja valley': (57.30, 25.05)}
def mark(ax):
    for n, (la, lo) in SITES.items():
        ax.plot(lo, la, 'k+', ms=6); ax.text(lo + 0.03, la + 0.02, n, fontsize=7)

def save(arr, name, title, cmap, norm=None, legend=None, box=None, vmin=None, vmax=None):
    fig, ax = plt.subplots(figsize=(13, 6.2), dpi=110)
    a = arr
    e = ext
    if box:
        la0, la1, lo0, lo1 = box
        r0, r1 = int((LAT1 - la1) / DLAT), int((LAT1 - la0) / DLAT)
        c0, c1 = int((lo0 - LON0) / DLON), int((lo1 - LON0) / DLON)
        a = arr[r0:r1, c0:c1]; e = [lo0, lo1, la0, la1]
    im = ax.imshow(a, extent=e, cmap=cmap, norm=norm, vmin=vmin, vmax=vmax, interpolation='nearest', aspect=asp)
    if legend: ax.legend(handles=legend, loc='lower left', fontsize=7, framealpha=0.85, ncol=2)
    else: fig.colorbar(im, ax=ax, shrink=0.7)
    if not box: mark(ax)
    ax.set_title(title, fontsize=10)
    fig.tight_layout(); fig.savefig(os.path.join(OUT, name)); plt.close(fig)

save(np.where(water, np.nan, z), '01-elevation.png', 'GLO-90 surface height (m), 6"x9" mean - a surface model: forest canopy is included', 'terrain', vmin=0, vmax=250)
save(np.where(water, np.nan, np.minimum(hand, 30)), '02-hand.png', f'Height above nearest drainage (m), streams from {STREAM_KM2} km2', 'viridis_r', vmin=0, vmax=30)
save(np.where(water, np.nan, np.clip(WT_DEPTH, -0.5, 8)), '03-water-table-depth.png', 'Modelled water-table depth (m below ground; <0 = water stands in a closed hollow)', 'Blues_r', vmin=-0.5, vmax=8)
mcols = ['#d9b36c', '#efd99a', '#cfe6a8', '#8fcf8f', '#4fa3a0', '#2f6f8f', '#1f3f6f', '#2f6db5']
mlab = ['0 very dry', '1 dry', '2 fresh', '3 moist', '4 wet', '5 waterlogged', '6 flooded hollow', 'open water']
save(moist, '04-moisture.png', 'Soil moisture regime from water table + texture', ListedColormap(mcols), BoundaryNorm(np.arange(-0.5, 8), 8),
     [Patch(color=c, label=l) for c, l in zip(mcols, mlab)])
hcm = ListedColormap([h[2] for h in HAB]); hnorm = BoundaryNorm(np.arange(-0.5, len(HAB)), len(HAB))
hleg = [Patch(color=h[2], label=h[1]) for h in HAB]
save(hab, '05-habitats.png', 'Prototype habitats (hemiboreal zone; site = moisture x soil richness)', hcm, hnorm, hleg)
cmp_ = np.zeros((H, W), np.int8)
cmp_[pred_wl & osm_wl] = 1; cmp_[pred_wl & ~osm_wl] = 2; cmp_[~pred_wl & osm_wl] = 3; cmp_[water] = 4
ccols = ['#f4f1e8', '#2a9d3a', '#e05a2b', '#3366cc', '#9fbbdd']
save(cmp_, '06-wetlands-vs-osm.png', 'Wetlands: model vs OpenStreetMap natural=wetland', ListedColormap(ccols), BoundaryNorm(np.arange(-0.5, 5), 5),
     [Patch(color=c, label=l) for c, l in zip(ccols, ['neither', 'both', 'model only', 'OSM only', 'water'])])
ZOOM = (56.70, 57.05, 23.25, 24.15)
save(hab, '07-habitats-kemeri-cena.png', 'Habitats, Ķemeri-Cena-Jelgava zoom', hcm, hnorm, hleg, box=ZOOM)
save(cmp_, '08-wetlands-vs-osm-kemeri-cena.png', 'Model vs OSM wetlands, Ķemeri-Cena-Jelgava zoom', ListedColormap(ccols), BoundaryNorm(np.arange(-0.5, 5), 5),
     [Patch(color=c, label=l) for c, l in zip(ccols, ['neither', 'both', 'model only', 'OSM only', 'water'])], box=ZOOM)
ZOOM2 = (56.45, 56.80, 25.90, 26.70)
save(hab, '09-habitats-teici.png', 'Habitats, Teiči zoom', hcm, hnorm, hleg, box=ZOOM2)
save(cmp_, '10-wetlands-vs-osm-teici.png', 'Model vs OSM wetlands, Teiči zoom', ListedColormap(ccols), BoundaryNorm(np.arange(-0.5, 5), 5),
     [Patch(color=c, label=l) for c, l in zip(ccols, ['neither', 'both', 'model only', 'OSM only', 'water'])], box=ZOOM2)
print('maps written to', OUT)

if os.environ.get('DUMP'):
    np.savez_compressed(os.environ['DUMP'], hand=hand, mound=mound, twi=twi, slope=slope, fill=fill, perm=perm, rich=rich,
                        xdown=xdown, xup=xup, acc=acc, osm=osm, land=land, water=water, z=z, rain=rain)

if os.environ.get('HAB_BIN'):
    # dev overlay for the in-game test build: habitat class per cell inside Latvia (255 = no overlay), gzip after a header
    import gzip, struct
    cls = np.where(land, hab, 255).astype(np.uint8)
    with open(os.environ['HAB_BIN'], 'wb') as f:
        f.write(b'EHAB' + struct.pack('<i4d2i', 1, LAT1, LON0, DLAT, DLON, H, W))
        f.write(gzip.compress(cls.tobytes(), 9))
    print('habitat overlay written', os.environ['HAB_BIN'], [h[0] for h in HAB])
