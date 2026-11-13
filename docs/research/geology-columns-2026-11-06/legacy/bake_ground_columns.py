"""Bake the Baltic ground-column grid (prototype, 6 November 2026).

One 0.01-degree grid over 20-29 E, 53.5-60 N (900 x 650 cells, row 0 at the north edge). Per cell:

  qthick   uint16  Quaternary cover thickness, decimetres (Latvia: Popovs et al. 2022; elsewhere a rule by GUM type)
  basement uint16  depth to crystalline basement, metres (CRUST1.0 bilinear + published control points)
  cover    uint8   cover class (see COVER below; from GUM 0.5 degree)
  subcrop  uint8   age band of the first bedrock under the cover (IGME 5000; see AGES)
  soil     uint8   WRB reference soil group index (SoilGrids 2.0 most probable, implausible Baltic groups replaced)
  clay     uint8   clay %, 15-30 cm (SoilGrids)
  ph       uint8   pH x 10, 15-30 cm (SoilGrids)
  sublith  uint8   IGME 5000 first lithology word of the subcrop unit (see LITHS; 0 = undifferentiated)
  seabed   uint8   EMODnet Folk-7 class (0 none, 1 mud, 2 sandy mud, 3 muddy sand, 4 sand, 5 coarse, 6 mixed, 7 rock & boulders)

File: b"EGCB", uint16 version=1, uint16 nx, uint16 ny, float64 west, float64 north, float64 step, then the nine fields as
separate planes in the order above, zlib-compressed as one block. Sources and licences: docs/research/geology-columns-2026-11-06.

Usage: python tools/bake_ground_columns.py <data dir> <out .bin>
  <data dir> holds: soilgrids/{wrb,clay_15-30cm,phh2o_15-30cm}.tif, crust1/x/crust1.bnds, igme5000/igme_baltic_idx.npy +
  igme_baltic_recs.json (0.05-degree rasterisation by scripts/igme_baltic.py), lvq/Latvia_bedrock_surface.tif,
  seabed/baltic250k.json (EMODnet WFS GeoJSON, EPSG:3857), and the GUM grid path is read from the repository.
"""
import json, math, struct, sys, zlib
import numpy as np, tifffile

W, N, STEP, NX, NY = 20.0, 60.0, 0.01, 900, 650
lat = N - (np.arange(NY) + 0.5) * STEP
lon = W + (np.arange(NX) + 0.5) * STEP
LON, LAT = np.meshgrid(lon, lat)

# ---------------------------------------------------------------- cover classes (from GUM)
COVER = {"thin": 0, "till": 1, "lakeclay": 2, "outwash": 3, "alluvium": 4, "peat": 5, "dune": 6, "marine": 7, "loess": 8}
GUM_TO_COVER = {"Gt": "till", "Gu": "till", "Gm": "till", "Gp": "till", "Gl": "lakeclay", "Gf": "outwash",
                "Au": "alluvium", "Al": "alluvium", "Ap": "alluvium", "Ae": "alluvium", "Af": "outwash", "At": "alluvium",
                "Op": "peat", "Ou": "peat", "Pu": "peat", "Ed": "dune", "El": "dune", "Eu": "dune", "Er": "dune", "Ea": "dune",
                "Mu": "marine", "Lu": "loess", "Ca": "thin", "Cu": "thin", "Du": "thin", "Us": "thin"}
# typical Quaternary thickness (m) where no national grid exists, by cover class (Baltic literature ranges)
FALLBACK_Q = {"thin": 3.0, "till": 25.0, "lakeclay": 18.0, "outwash": 20.0, "alluvium": 12.0, "peat": 15.0, "dune": 15.0,
              "marine": 12.0, "loess": 10.0}

# ---------------------------------------------------------------- bedrock age bands (from IGME 5000)
AGES = ["unknown", "quaternary", "neogene", "palaeogene", "cretaceous", "jurassic", "triassic", "permian", "carboniferous",
        "devonian-upper", "devonian-middle", "devonian-lower", "silurian", "ordovician", "cambrian", "precambrian"]

def age_band(name):
    n = (name or "").lower()
    for key, band in [("quaternary", "quaternary"), ("pleistocene", "quaternary"), ("holocene", "quaternary"),
                      ("miocene", "neogene"), ("pliocene", "neogene"), ("neogene", "neogene"),
                      ("eocene", "palaeogene"), ("oligocene", "palaeogene"), ("palaeocene", "palaeogene"), ("paleocene", "palaeogene"),
                      ("cretaceous", "cretaceous"), ("jurassic", "jurassic"), ("triassic", "triassic"), ("permian", "permian"),
                      ("carboniferous", "carboniferous"), ("late devonian", "devonian-upper"), ("middle devonian", "devonian-middle"),
                      ("early devonian", "devonian-lower"), ("devonian", "devonian-upper"), ("silurian", "silurian"),
                      ("ordovician", "ordovician"), ("cambrian", "cambrian"), ("proterozoic", "precambrian"),
                      ("archaean", "precambrian"), ("ediacaran", "precambrian"), ("vendian", "precambrian"), ("precambrian", "precambrian")]:
        if key in n:
            return AGES.index(band)
    return 0

# ---------------------------------------------------------------- soil groups
WRB = ["Acrisols", "Albeluvisols", "Alisols", "Andosols", "Arenosols", "Calcisols", "Cambisols", "Chernozems", "Cryosols", "Durisols",
       "Ferralsols", "Fluvisols", "Gleysols", "Gypsisols", "Histosols", "Kastanozems", "Leptosols", "Lixisols", "Luvisols", "Nitisols",
       "Phaeozems", "Planosols", "Plinthosols", "Podzols", "Regosols", "Solonchaks", "Solonetz", "Stagnosols", "Umbrisols", "Vertisols"]
# groups that do not occur in the cool humid Baltic climate (tropical, arid, permafrost); a later global version gates by climate
IMPLAUSIBLE = {"Acrisols", "Alisols", "Andosols", "Calcisols", "Cryosols", "Durisols", "Ferralsols", "Gypsisols", "Kastanozems",
               "Lixisols", "Nitisols", "Plinthosols", "Solonchaks", "Solonetz", "Vertisols"}

LITHS = ["", "sandstone", "limestone", "dolomite", "clay", "marl", "chalk", "sand", "shale"]

def lith_code(rec):
    words = " ".join(str(rec.get(k) or "") for k in ("Portr_Petr", "Portr_Pe_1")).lower().replace(",", " ").split()
    for w in words:
        for i, key in enumerate(LITHS):
            if i and (w == key or w.startswith(key)):
                return i
    return 0

def tm_baltic93(lat_deg, lon_deg):
    """EPSG:25884 ETRS89 / TM Baltic93 forward (GRS80, lon0 24, k0 0.9996, FE 500000, FN 0), vectorised."""
    a = 6378137.0; f = 1 / 298.257222101; e2 = 2 * f - f * f; ep2 = e2 / (1 - e2); k0 = 0.9996
    p = np.radians(lat_deg); l = np.radians(lon_deg) - math.radians(24)
    M = a * ((1 - e2 / 4 - 3 * e2 ** 2 / 64 - 5 * e2 ** 3 / 256) * p - (3 * e2 / 8 + 3 * e2 ** 2 / 32 + 45 * e2 ** 3 / 1024) * np.sin(2 * p)
             + (15 * e2 ** 2 / 256 + 45 * e2 ** 3 / 1024) * np.sin(4 * p) - (35 * e2 ** 3 / 3072) * np.sin(6 * p))
    Nn = a / np.sqrt(1 - e2 * np.sin(p) ** 2); T = np.tan(p) ** 2; C = ep2 * np.cos(p) ** 2; A = np.cos(p) * l
    x = k0 * Nn * (A + (1 - T + C) * A ** 3 / 6 + (5 - 18 * T + T * T + 72 * C - 58 * ep2) * A ** 5 / 120)
    y = k0 * (M + Nn * np.tan(p) * (A * A / 2 + (5 - T + 9 * C + 4 * C * C) * A ** 4 / 24 + (61 - 58 * T + T * T + 600 * C - 330 * ep2) * A ** 6 / 720))
    return x + 500000, y

def mode_fill(values, bad, good, radius=4):
    """Replace cells flagged bad by the most common good value within the window (else keep)."""
    out = values.copy()
    ys, xs = np.nonzero(bad)
    for y, x in zip(ys, xs):
        sl = (slice(max(0, y - radius), y + radius + 1), slice(max(0, x - radius), x + radius + 1))
        g = good[sl]
        if g.any():
            out[y, x] = np.bincount(values[sl][g].ravel()).argmax()
    return out

def main(data, out, repo="."):
    # GUM 0.5 degree
    gum = np.loadtxt(f"{repo}/docs/research/regional-geology-2026-09-29/gum_v1.0_0point5deg.txt.asc", skiprows=6)
    names = {int(l.split(';')[1]): l.split(';')[3].strip().strip('"')
             for l in open(f"{repo}/docs/research/regional-geology-2026-09-29/classnames.txt").read().splitlines()[1:]}
    yll = -89.985256795408
    gr = (gum.shape[0] - 1 - ((LAT - yll) / 0.5).astype(int)); gc = ((LON + 179.99998074964) / 0.5).astype(int)
    gcode = gum[gr, gc].astype(int)
    cover_name = np.vectorize(lambda v: GUM_TO_COVER.get(names.get(v, "-"), "thin"))(gcode)
    cover = np.vectorize(COVER.get)(cover_name).astype(np.uint8)

    # Quaternary thickness: Latvian grid where it has data, else fallback by cover class
    q = np.vectorize(FALLBACK_Q.get)(cover_name).astype(float)
    lv = tifffile.imread(f"{data}/lvq/Latvia_bedrock_surface.tif")[..., 1].astype(float)
    x, y = tm_baltic93(LAT, LON)
    ii = ((x - 312750.0) / 250.0).astype(int); jj = ((6439000.0 - y) / 250.0).astype(int)
    inside = (ii >= 0) & (ii < lv.shape[1]) & (jj >= 0) & (jj < lv.shape[0])
    v = np.full(LAT.shape, np.nan); v[inside] = lv[jj[inside], ii[inside]]
    v[v == -32768] = np.nan
    have = np.isfinite(v)
    q[have] = np.maximum(0.0, v[have])
    qd = np.clip(np.round(q * 10), 0, 65000).astype(np.uint16)

    # Basement: CRUST1.0 bilinear (cell centres at .5 degrees) + inverse-distance residuals at control points
    b = np.loadtxt(f"{data}/crust1/x/crust1.bnds").reshape(180, 360, 9)
    sed = (b[:, :, 2] - b[:, :, 5]) * 1000.0
    fy = 89.5 - LAT; fx = LON + 179.5
    y0 = np.floor(fy).astype(int); x0 = np.floor(fx).astype(int); ty = fy - y0; tx = fx - x0
    base = (sed[y0, x0] * (1 - tx) * (1 - ty) + sed[y0, x0 + 1] * tx * (1 - ty) + sed[y0 + 1, x0] * (1 - tx) * ty + sed[y0 + 1, x0 + 1] * tx * ty)
    control = [(57.83, 24.75, 688), (57.39, 21.56, 1170), (57.80, 23.25, 784), (56.97, 21.97, 1100), (55.87, 26.53, 800)]
    num = np.zeros_like(base); den = np.zeros_like(base)
    for cla, clo, depth in control:
        cy = int(round(89.5 - cla)); cx = int(round(clo + 179.5))
        fyc = 89.5 - cla; fxc = clo + 179.5; yy = int(np.floor(fyc)); xx = int(np.floor(fxc)); tyc = fyc - yy; txc = fxc - xx
        model = (sed[yy, xx] * (1 - txc) * (1 - tyc) + sed[yy, xx + 1] * txc * (1 - tyc) + sed[yy + 1, xx] * (1 - txc) * tyc + sed[yy + 1, xx + 1] * txc * tyc)
        dkm = np.hypot((LAT - cla) * 111.2, (LON - clo) * 111.2 * np.cos(np.radians(cla)))
        w = 1.0 / np.maximum(dkm, 5.0) ** 2 * np.exp(-(dkm / 120.0) ** 2)
        num += w * (depth - model); den += w
    base = base + np.where(den > 0, num / np.maximum(den, 1e-12), 0) * np.minimum(1.0, den / (den + 1e-4))
    base = np.maximum(base, q + 10)
    bd = np.clip(np.round(base), 0, 65000).astype(np.uint16)

    # Subcrop age from IGME 5000 (0.05 degree raster)
    idx = np.load(f"{data}/igme5000/igme_baltic_idx.npy"); recs = json.load(open(f"{data}/igme5000/igme_baltic_recs.json"))
    bands = np.array([age_band(r["AgeName"]) for r in recs] + [0], dtype=np.uint8)
    ri = np.clip(((N - LAT) / 0.05).astype(int), 0, idx.shape[0] - 1); ci = np.clip(((LON - W) / 0.05).astype(int), 0, idx.shape[1] - 1)
    sub = bands[np.where(idx[ri, ci] >= 0, idx[ri, ci], len(recs))]
    liths = np.array([lith_code(r) for r in recs] + [0], dtype=np.uint8)
    sublith = liths[np.where(idx[ri, ci] >= 0, idx[ri, ci], len(recs))]

    # Soil
    wrb = tifffile.imread(f"{data}/soilgrids/wrb.tif").astype(np.int32)
    clay = tifffile.imread(f"{data}/soilgrids/clay_15-30cm.tif").astype(float) / 10
    ph = tifffile.imread(f"{data}/soilgrids/phh2o_15-30cm.tif").astype(float)
    nodata = (wrb >= len(WRB)) | (clay <= 0)            # SoilGrids masks water and towns
    implaus = np.isin(wrb, [WRB.index(g) for g in IMPLAUSIBLE]) & ~nodata
    soil = mode_fill(np.where(nodata, 0, wrb), implaus, ~nodata & ~implaus).astype(np.uint8)
    # towns and lakes are masked in SoilGrids: take the nearest mapped cell within ~3 km; open sea stays 255
    from scipy.ndimage import distance_transform_edt
    dist, (ny_, nx_) = distance_transform_edt(nodata, return_indices=True)
    near = nodata & (dist <= 30)
    soil[near] = soil[ny_[near], nx_[near]]; clay[near] = clay[ny_[near], nx_[near]]; ph[near] = ph[ny_[near], nx_[near]]
    soil[nodata & ~near] = 255
    clay8 = np.clip(np.round(clay), 0, 100).astype(np.uint8); ph8 = np.clip(np.round(ph), 0, 140).astype(np.uint8)

    # Seabed (EMODnet Folk 7), rasterised at cell centres
    from shapely.geometry import shape
    from shapely.strtree import STRtree
    from shapely import points as shp_points
    from shapely.ops import transform
    R = 6378137.0
    def unmerc(xs, ys, zs=None):
        return np.degrees(np.asarray(xs) / R), np.degrees(np.arctan(np.sinh(np.asarray(ys) / R)))
    gj = json.load(open(f"{data}/seabed/baltic250k.json", encoding="utf-8"))
    geoms, cls = [], []
    folk = {"11": 1, "12": 2, "13": 3, "2.": 4, "3.": 5, "4.": 6, "5.": 7}
    for f in gj["features"]:
        t = (f["properties"].get("folk_7cl_txt") or "")
        c = next((v for k, v in folk.items() if t.startswith(k)), 0)
        if not c:
            continue
        g = transform(unmerc, shape(f["geometry"]))
        if not g.is_valid:
            g = g.buffer(0)
        geoms.append(g); cls.append(c)
    tree = STRtree(geoms)
    pts = shp_points(LON.ravel(), LAT.ravel())
    pi, gi = tree.query(pts, predicate="within")
    sea = np.zeros(LAT.size, np.uint8); sea[pi] = np.array(cls, np.uint8)[gi]
    sea = sea.reshape(LAT.shape)

    planes = [qd, bd, cover, sub, soil, clay8, ph8, sublith, sea]
    body = b"".join(np.ascontiguousarray(p).astype("<u2" if p.dtype == np.uint16 else "u1").tobytes() for p in planes)
    head = b"EGCB" + struct.pack("<HHHddd", 1, NX, NY, W, N, STEP)
    with open(out, "wb") as fh:
        fh.write(head + zlib.compress(body, 9))
    print(f"wrote {out}: {len(head) + len(zlib.compress(body, 9)):,} bytes")
    print(f"  Quaternary from the Latvian grid in {have.mean() * 100:.1f}% of cells; basement {bd.min()}-{bd.max()} m; seabed classified in {(sea > 0).mean() * 100:.1f}%")
    for nm, la, lo in [("Riga", 56.95, 24.10), ("Jelgava", 56.65, 23.72), ("Staicele", 57.83, 24.75), ("Liepaja", 56.51, 21.05),
                       ("Daugavpils", 55.87, 26.53), ("Ventspils", 57.39, 21.56), ("Tallinn", 59.40, 24.75), ("Kaunas", 54.90, 23.90)]:
        j = int((N - la) / STEP); i = int((lo - W) / STEP)
        print(f"  {nm:10s} q={qd[j, i] / 10:5.1f} m  basement={bd[j, i]:5d} m  cover={list(COVER)[cover[j, i]]:8s} subcrop={AGES[sub[j, i]]:15s} lith={LITHS[sublith[j, i]] or '-':9s} soil={WRB[soil[j, i]] if soil[j, i] < len(WRB) else '-':12s} clay={clay8[j, i]}% pH={ph8[j, i] / 10}")

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else ".")
