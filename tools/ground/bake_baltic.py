"""Bake the ground tiles for the Baltic prototype (and the two small global tiles it falls back on).

Usage: python tools/ground/bake_baltic.py <data dir> [<out dir>]
  <data dir>: soilgrids/{wrb,clay_15-30cm,phh2o_15-30cm}.tif (0.01-degree WCS clips over 20-29 E, 53.5-60 N),
  crust1/x/crust1.bnds, igme5000/igme_baltic_idx.npy + igme_baltic_recs.json (0.05-degree rasterisation of IGME 5000),
  lvq/Latvia_bedrock_surface.tif (Popovs et al. 2022, EPSG:25884), seabed/baltic250k.json (EMODnet WFS, EPSG:3857).
  GUM is read from docs/research/regional-geology-2026-09-29 in this repository.
<out dir> defaults to assets/earth/config/ground. Each tile keeps its source's own vocabulary (GUM codes, IGME age names,
WRB group names, EMODnet Folk classes); the rules map those names. Design: docs/research/geology-columns-2026-11-06/global-design.md.
"""
import csv, json, math, os, sys
import numpy as np, tifffile
sys.path.insert(0, os.path.dirname(__file__))
import tiles

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
W, N, STEP, NX, NY = 20.0, 60.0, 0.01, 900, 650           # the SoilGrids clips' grid


def centres(west, north, step, nx, ny):
    lat = north - (np.arange(ny) + 0.5) * step
    lon = west + (np.arange(nx) + 0.5) * step
    return np.meshgrid(lon, lat)


def tm_baltic93(lat_deg, lon_deg):
    """EPSG:25884 ETRS89 / TM Baltic93 forward (GRS80, lon0 24, k0 0.9996, FE 500000, FN 0)."""
    a = 6378137.0; f = 1 / 298.257222101; e2 = 2 * f - f * f; ep2 = e2 / (1 - e2); k0 = 0.9996
    p = np.radians(lat_deg); l = np.radians(lon_deg) - math.radians(24)
    M = a * ((1 - e2 / 4 - 3 * e2 ** 2 / 64 - 5 * e2 ** 3 / 256) * p - (3 * e2 / 8 + 3 * e2 ** 2 / 32 + 45 * e2 ** 3 / 1024) * np.sin(2 * p)
             + (15 * e2 ** 2 / 256 + 45 * e2 ** 3 / 1024) * np.sin(4 * p) - (35 * e2 ** 3 / 3072) * np.sin(6 * p))
    Nn = a / np.sqrt(1 - e2 * np.sin(p) ** 2); T = np.tan(p) ** 2; C = ep2 * np.cos(p) ** 2; A = np.cos(p) * l
    x = k0 * Nn * (A + (1 - T + C) * A ** 3 / 6 + (5 - 18 * T + T * T + 72 * C - 58 * ep2) * A ** 5 / 120)
    y = k0 * (M + Nn * np.tan(p) * (A * A / 2 + (5 - T + 9 * C + 4 * C * C) * A ** 4 / 24 + (61 - 58 * T + T * T + 600 * C - 330 * ep2) * A ** 6 / 720))
    return x + 500000, y


# GUM leaves a cell empty both at sea and where no loose sediment is mapped (bare or thinly covered rock: the Sahara's
# hamadas, the shields, the high mountains). GLiM's 0.5-degree grid, on the same cells, tells the two apart: on land the
# empty cell gets a rock-surface code by GLiM class (R*, plus GUM's own pyroclastic, evaporite, ice and water codes);
# GLiM's unconsolidated cells take the nearest GUM unit; sea cells within COAST_CELLS of land take the nearest land code,
# so coastal columns (Carnikava, 6 November) get their real neighbour instead of the default.
GLIM_EMPTY = {1: None, 2: "Rm", 3: "Rx", 4: "Rm", 5: "Rx", 6: "Rk", 7: "Rx", 8: "Rx", 9: "Rx", 10: "Rm", 11: "Wu",
              12: "Iy", 13: "Rx", 14: "Pu", 15: None, 16: "Du"}
COAST_CELLS = 3


def gum_global(out):
    """GUM v1.0 0.5-degree grid on GLiM's whole-globe cells, empty cells filled as above; legend = GUM codes + R*."""
    from scipy import ndimage
    import zipfile
    src = f"{REPO}/docs/research/regional-geology-2026-09-29"
    head = {}
    with open(f"{src}/gum_v1.0_0point5deg.txt.asc") as fh:
        for _ in range(6):
            k, v = fh.readline().split()
            head[k.lower()] = float(v)
    grid = np.loadtxt(f"{src}/gum_v1.0_0point5deg.txt.asc", skiprows=6).astype(int)
    with zipfile.ZipFile(f"{src}/glim-download.zip") as z:
        glim = np.loadtxt(z.open("glim_wgs84_0point5deg.txt.asc"), skiprows=6).astype(int)
    assert glim.shape == (360, 720)
    names = {int(l.split(';')[1]): l.split(';')[3].strip().strip('"')
             for l in open(f"{src}/classnames.txt").read().splitlines()[1:]}
    legend = sorted(set(names.values()) | {c for c in GLIM_EMPTY.values() if c})
    code = np.full((360, 720), 255, np.uint8)
    top = int(round((90 - (head["yllcorner"] + head["nrows"] * 0.5)) / 0.5))   # GUM's first row is 83.5 N: 13 rows down
    lut = {k: legend.index(v) for k, v in names.items()}
    code[top:top + grid.shape[0]] = np.vectorize(lambda v: lut.get(v, 255))(grid).astype(np.uint8)
    empty = code == 255
    loose = empty & (glim == 1)
    for g, c in GLIM_EMPTY.items():
        if c: code[empty & (glim == g)] = legend.index(c)
    def nearest(mask_src, mask_dst, cells):
        d, (iy, ix) = ndimage.distance_transform_edt(~mask_src, return_indices=True)
        sel = mask_dst & (d <= cells)
        code[sel] = code[iy[sel], ix[sel]]
    gum = np.zeros_like(empty); gum[top:top + grid.shape[0]] = grid != int(head["nodata_value"])
    nearest(gum, loose, 1e9)
    nearest(code != 255, code == 255, COAST_CELLS)
    return tiles.write(out, "global-gum", "GUM v1.0 0.5-degree grid (Boerker et al. 2018); empty cells by GLiM (Hartmann & Moosdorf 2012)",
                       "CC BY 3.0", -180.0, 90.0, 0.5, [tiles.field("cover_unit", code, legend=legend)])


def crust1_global(data_dir, out):
    """CRUST1.0 sediment thickness (top of sediments to top of crystalline crust), 1 degree, cell centres at .5."""
    b = np.loadtxt(f"{data_dir}/crust1/x/crust1.bnds").reshape(180, 360, 9)
    sed = np.clip(np.round((b[:, :, 2] - b[:, :, 5]) * 1000.0), 0, 65000).astype(np.uint16)
    return tiles.write(out, "global-crust1", "CRUST1.0 (Laske et al. 2013), sediment thickness", "free for research and education (IGPP); attribution",
                       -180.0, 90.0, 1.0, [tiles.field("basement_m", sed, type="u16")]), sed


def basement_baltic(sed, out):
    """CRUST1.0 corrected to Baltic borehole control points (tools/ground/basement-control-baltic.csv), 0.05 degree."""
    step = 0.05; nx = int(round(9 / step)); ny = int(round(6.5 / step))
    LON, LAT = centres(W, N, step, nx, ny)
    def bil(la, lo):
        fy = 89.5 - la; fx = lo + 179.5
        y0 = np.floor(fy).astype(int); x0 = np.floor(fx).astype(int); ty = fy - y0; tx = fx - x0
        return (sed[y0, x0] * (1 - tx) * (1 - ty) + sed[y0, x0 + 1] * tx * (1 - ty) + sed[y0 + 1, x0] * (1 - tx) * ty + sed[y0 + 1, x0 + 1] * tx * ty)
    base = bil(LAT, LON).astype(float)
    num = np.zeros_like(base); den = np.zeros_like(base)
    for row in csv.DictReader(open(os.path.join(os.path.dirname(__file__), "basement-control-baltic.csv"), encoding="utf-8")):
        la, lo, depth = float(row["lat"]), float(row["lon"]), float(row["basement_m"])
        model = float(bil(np.array(la), np.array(lo)))
        dkm = np.hypot((LAT - la) * 111.2, (LON - lo) * 111.2 * np.cos(np.radians(la)))
        w = 1.0 / np.maximum(dkm, 5.0) ** 2 * np.exp(-(dkm / 120.0) ** 2)
        num += w * (depth - model); den += w
    base = base + np.where(den > 0, num / np.maximum(den, 1e-12), 0) * np.minimum(1.0, den / (den + 1e-4))
    data = np.clip(np.round(base), 0, 65000).astype(np.uint16)
    return tiles.write(out, "baltic-basement", "CRUST1.0 corrected to Baltic boreholes (basement-control-baltic.csv)", "derived; sources in the CSV",
                       W, N, step, [tiles.field("basement_m", data, type="u16")])


def latvia_quaternary(data_dir, out):
    """Quaternary thickness of Latvia (Popovs et al. 2022, 250 m, CC BY 4.0), sampled at 0.01 degree; nodata outside Latvia."""
    west, north, nx, ny = 20.8, 58.2, 760, 270
    LON, LAT = centres(west, north, STEP, nx, ny)
    lv = tifffile.imread(f"{data_dir}/lvq/Latvia_bedrock_surface.tif")[..., 1].astype(float)
    x, y = tm_baltic93(LAT, LON)
    ii = ((x - 312750.0) / 250.0).astype(int); jj = ((6439000.0 - y) / 250.0).astype(int)
    inside = (ii >= 0) & (ii < lv.shape[1]) & (jj >= 0) & (jj < lv.shape[0])
    v = np.full(LAT.shape, np.nan); v[inside] = lv[jj[inside], ii[inside]]
    v[v == -32768] = np.nan
    data = np.where(np.isfinite(v), np.clip(np.round(np.maximum(v, 0) * 10), 0, 65000), 65535).astype(np.uint16)
    return tiles.write(out, "latvia-quaternary", "Popovs et al. 2022, Latvian bedrock surface and Quaternary thickness (Zenodo 7053754)", "CC BY 4.0",
                       west, north, STEP, [tiles.field("quaternary_m", data, type="u16", scale=0.1)])


def igme_subcrop(data_dir, out):
    """IGME 5000 pre-Quaternary map (BGR): age name and first lithology word, 0.05 degree. Terms to check before shipping."""
    idx = np.load(f"{data_dir}/igme5000/igme_baltic_idx.npy"); recs = json.load(open(f"{data_dir}/igme5000/igme_baltic_recs.json"))
    ages = sorted({r["AgeName"] for r in recs if r.get("AgeName")})
    words = ["sandstone", "limestone", "dolomite", "clay", "marl", "chalk", "sand", "shale"]
    def lith(r):
        for w in " ".join(str(r.get(k) or "") for k in ("Portr_Petr", "Portr_Pe_1")).lower().replace(",", " ").split():
            for i, key in enumerate(words):
                if w == key or w.startswith(key):
                    return i
        return 255
    age_lut = np.array([ages.index(r["AgeName"]) if r.get("AgeName") else 255 for r in recs] + [255], np.uint8)
    lith_lut = np.array([lith(r) for r in recs] + [255], np.uint8)
    k = np.where(idx >= 0, idx, len(recs))
    return tiles.write(out, "europe-igme5000", "IGME 5000, International Geological Map of Europe 1:5M (BGR)", "BGR terms to check before publication",
                       W, N, 0.05, [tiles.field("subcrop", age_lut[k], legend=ages), tiles.field("subcrop_lith", lith_lut[k], legend=words)])


WRB = ["Acrisols", "Albeluvisols", "Alisols", "Andosols", "Arenosols", "Calcisols", "Cambisols", "Chernozems", "Cryosols", "Durisols",
       "Ferralsols", "Fluvisols", "Gleysols", "Gypsisols", "Histosols", "Kastanozems", "Leptosols", "Lixisols", "Luvisols", "Nitisols",
       "Phaeozems", "Planosols", "Plinthosols", "Podzols", "Regosols", "Solonchaks", "Solonetz", "Stagnosols", "Umbrisols", "Vertisols"]
# Data cleaning for this clip only: groups that cannot occur in the cool humid Baltic (tropical, arid, permafrost) are
# replaced by the most common plausible group nearby. A global bake gates by climate instead.
IMPLAUSIBLE = {"Acrisols", "Alisols", "Andosols", "Calcisols", "Cryosols", "Durisols", "Ferralsols", "Gypsisols", "Kastanozems",
               "Lixisols", "Nitisols", "Plinthosols", "Solonchaks", "Solonetz", "Vertisols"}


def soilgrids(data_dir, out):
    from scipy.ndimage import distance_transform_edt
    wrb = tifffile.imread(f"{data_dir}/soilgrids/wrb.tif").astype(np.int32)
    clay = tifffile.imread(f"{data_dir}/soilgrids/clay_15-30cm.tif").astype(float) / 10
    ph = tifffile.imread(f"{data_dir}/soilgrids/phh2o_15-30cm.tif").astype(float)
    nodata = (wrb >= len(WRB)) | (clay <= 0)                      # SoilGrids masks water and towns
    implaus = np.isin(wrb, [WRB.index(g) for g in IMPLAUSIBLE]) & ~nodata
    soil = np.where(nodata, 0, wrb)
    good = ~nodata & ~implaus
    for y, x in zip(*np.nonzero(implaus)):
        sl = (slice(max(0, y - 4), y + 5), slice(max(0, x - 4), x + 5))
        if good[sl].any():
            soil[y, x] = np.bincount(soil[sl][good[sl]].ravel()).argmax()
    soil = soil.astype(np.uint8)
    dist, (ny_, nx_) = distance_transform_edt(nodata, return_indices=True)
    near = nodata & (dist <= 30)                                  # towns and lakes: nearest mapped cell within ~3 km
    soil[near] = soil[ny_[near], nx_[near]]; clay[near] = clay[ny_[near], nx_[near]]; ph[near] = ph[ny_[near], nx_[near]]
    far = nodata & ~near
    soil[far] = 255
    clay8 = np.where(far, 255, np.clip(np.round(clay), 0, 100)).astype(np.uint8)
    ph8 = np.where(far, 255, np.clip(np.round(ph), 0, 140)).astype(np.uint8)
    return tiles.write(out, "baltic-soilgrids", "SoilGrids 2.0 (ISRIC): WRB most probable group, clay and pH at 15-30 cm", "CC BY 4.0",
                       W, N, STEP, [tiles.field("soil_group", soil, legend=WRB), tiles.field("clay_pct", clay8),
                                    tiles.field("ph", ph8, scale=0.1)])


def seabed(data_dir, out):
    from shapely.geometry import shape
    from shapely.strtree import STRtree
    from shapely import points as shp_points
    from shapely.ops import transform
    R = 6378137.0
    def unmerc(xs, ys, zs=None):
        return np.degrees(np.asarray(xs) / R), np.degrees(np.arctan(np.sinh(np.asarray(ys) / R)))
    gj = json.load(open(f"{data_dir}/seabed/baltic250k.json", encoding="utf-8"))
    legend, geoms, cls = [], [], []
    for f in gj["features"]:
        t = (f["properties"].get("folk_7cl_txt") or "").strip()
        if not t or t.startswith(("6.", "9.")):                    # "No data at this level", "Restricted data"
            continue
        if t not in legend:
            legend.append(t)
        g = transform(unmerc, shape(f["geometry"]))
        geoms.append(g if g.is_valid else g.buffer(0)); cls.append(legend.index(t))
    LON, LAT = centres(W, N, STEP, NX, NY)
    tree = STRtree(geoms)
    pi, gi = tree.query(shp_points(LON.ravel(), LAT.ravel()), predicate="within")
    sea = np.full(LAT.size, 255, np.uint8); sea[pi] = np.array(cls, np.uint8)[gi]
    return tiles.write(out, "baltic-emodnet-seabed", "EMODnet Geology seabed substrate 1:250k, Folk 7 classes", "CC BY 4.0",
                       W, N, STEP, [tiles.field("seabed", sea.reshape(LAT.shape), legend=legend)])


def main(data_dir, out_dir, only=None):
    os.makedirs(out_dir, exist_ok=True)
    p = lambda n: os.path.join(out_dir, n + ".egt")
    want = lambda n: not only or n in only
    sizes = {}
    if want("global-gum"): sizes["global-gum"] = gum_global(p("global-gum"))
    if want("global-crust1") or want("baltic-basement"):
        size, sed = crust1_global(data_dir, p("global-crust1")); sizes["global-crust1"] = size
        if want("baltic-basement"): sizes["baltic-basement"] = basement_baltic(sed, p("baltic-basement"))
    if want("latvia-quaternary"): sizes["latvia-quaternary"] = latvia_quaternary(data_dir, p("latvia-quaternary"))
    if want("europe-igme5000"): sizes["europe-igme5000"] = igme_subcrop(data_dir, p("europe-igme5000"))
    if want("baltic-soilgrids"): sizes["baltic-soilgrids"] = soilgrids(data_dir, p("baltic-soilgrids"))
    if want("baltic-emodnet-seabed"): sizes["baltic-emodnet-seabed"] = seabed(data_dir, p("baltic-emodnet-seabed"))
    for k, v in sizes.items():
        print(f"  {k:24s} {v:>10,} bytes")
    print(f"  total {sum(sizes.values()):,} bytes")


if __name__ == "__main__":
    # usage: bake_baltic.py DATA_DIR [OUT_DIR|-] [tile names to bake only those]
    out = sys.argv[2] if len(sys.argv) > 2 and sys.argv[2] != "-" else os.path.join(REPO, "assets", "earth", "config", "ground")
    main(sys.argv[1], out, set(sys.argv[3:]))
