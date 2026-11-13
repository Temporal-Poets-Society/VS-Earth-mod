"""Bake the world-wide ground tile sets at 2' (1/30 degree, about 3.7 km), in 10-degree tiles the game unpacks on demand.

Usage: python -P tools/ground/bake_global.py <data dir> [<out dir>] [set names...]
<data dir> (downloads kept outside the repository; sources and licences in
docs/research/geology-columns-2026-11-06/global-sources.md):
  soilgrids-2min/{wrb,clay,ph}/<w>_<s>.tif   tools/ground/fetch_soilgrids_2min.py (SoilGrids 2.0, CC BY 4.0)
  BDTICM_M_1km_ll.tif                        Shangguan et al. 2017 depth to bedrock, 30" (CC BY 4.0)
  batchelor2019/{LGM,MIS6,MIS12,MIS16}_best_estimate.*   Batchelor et al. 2019 ice-sheet extents (OSF 7jen3)
  permafrost/UiO_PEX_PERPROB_*.tif.ovr       Obu et al. 2019 permafrost probability, overview level 1 (CC BY 3.0)
  hasterok/global_gprv.*                     Hasterok et al. 2022 geological provinces (CC BY 4.0)
  chorlton/gen.*                             Chorlton 2007, GSC OF 5529, era and bedrock class (OGL-Canada)
Needs pyshp (shapefile), Pillow, numpy, scipy, tifffile + imagecodecs. Writes <out dir>/global-<set>.egts.
"""
import math, os, sys
import numpy as np, tifffile
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(__file__))
import tiles
from bake_baltic import WRB

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
STEP = 1 / 30
TILE = 300                                 # cells per 10-degree tile side
NX, NY = 360 * 30, 180 * 30                # the whole world at 2'


def centres_lat():
    return 90 - (np.arange(NY) + 0.5) * STEP


def centres_lon():
    return -180 + (np.arange(NX) + 0.5) * STEP


def split(name, source, licence, fields, keep=None):
    """Cut world-size fields into 10-degree tiles; tiles where every field is nodata are left out."""
    blobs = []
    for ty in range(NY // TILE):
        for tx in range(NX // TILE):
            sl = (slice(ty * TILE, (ty + 1) * TILE), slice(tx * TILE, (tx + 1) * TILE))
            part = [dict(f, data=f["data"][sl]) for f in fields]
            if all((p["data"] == p["nodata"]).all() for p in part):
                continue
            west, north = -180 + tx * 10, 90 - ty * 10
            blobs.append(tiles.pack(f"{name} {west:+d} {north:+d}", source, licence, west, north, STEP, part))
    return blobs


# ------------------------------------------------------------------ soil (SoilGrids)

def soil(data):
    d = os.path.join(data, "soilgrids-2min")
    wrb = np.full((NY, NX), 255, np.uint8); clay = np.full((NY, NX), 255, np.uint8); ph = np.full((NY, NX), 255, np.uint8)
    from scipy.ndimage import distance_transform_edt
    for fn in os.listdir(os.path.join(d, "wrb")):
        w, s = (int(v) for v in fn[:-4].split("_"))
        g = tifffile.imread(os.path.join(d, "wrb", fn)).astype(np.int32)
        c = tifffile.imread(os.path.join(d, "clay", fn)).astype(float) / 10
        p = tifffile.imread(os.path.join(d, "ph", fn)).astype(float)
        nodata = (g < 0) | (g >= len(WRB)) | (c <= 0)          # SoilGrids masks water, towns, ice and rock
        if nodata.all():
            continue
        dist, (iy, ix) = distance_transform_edt(nodata, return_indices=True)
        near = nodata & (dist <= 3)                           # towns and lakes: the nearest mapped cell within ~11 km
        g[near] = g[iy[near], ix[near]]; c[near] = c[iy[near], ix[near]]; p[near] = p[iy[near], ix[near]]
        far = nodata & ~near
        r0, c0 = int(round((90 - (s + 10)) * 30)), int(round((w + 180) * 30))
        sl = (slice(r0, r0 + TILE), slice(c0, c0 + TILE))
        wrb[sl] = np.where(far, 255, g).astype(np.uint8)
        clay[sl] = np.where(far, 255, np.clip(np.round(c), 0, 100)).astype(np.uint8)
        ph[sl] = np.where(far, 255, np.clip(np.round(p), 0, 140)).astype(np.uint8)
    source = "SoilGrids 2.0 (ISRIC, Poggio et al. 2021): WRB most probable group, clay and pH at 15-30 cm, WCS at 2'"
    if os.path.isdir(os.path.join(data, "soilgrids-30s", "wrb")):
        # The WCS averages class numbers when it scales (fetch_soilgrids_30s.py), so the groups come from the 30" classes:
        # the most common in each 2' cell, gaps of up to 3 cells filled from the nearest.
        import bake_30s
        wrb = bake_30s.soilgroup_2min(data)
        gap = wrb == 255
        dist, (iy, ix) = distance_transform_edt(gap, return_indices=True)
        near = gap & (dist <= 3)
        wrb[near] = wrb[iy[near], ix[near]]
        source = source.replace("WRB most probable group", "WRB group (most common full-resolution class)")
    return split("global-soilgrids", source,
                 "CC BY 4.0", [tiles.field("soil_group", wrb, legend=WRB), tiles.field("clay_pct", clay), tiles.field("ph", ph, scale=0.1)])


# ------------------------------------------------------------------ cover thickness (Shangguan)

def shangguan(data):
    """Depth to bedrock (cm, 30") averaged over each 2' cell (4x4), stored in decimetres."""
    tif = tifffile.TiffFile(os.path.join(data, "BDTICM_M_1km_ll.tif"))
    page = tif.pages[0]
    h, w = page.shape
    top = int(round((90 - page.tags[33922].value[4]) * 120))     # 30" rows above the file's first row (84 N: 720)
    assert w == 43200 and top % 4 == 0
    out = np.full((NY, NX), 65535, np.uint16)
    acc = np.zeros((NX,), float); cnt = np.zeros((NX,), float); row = 0
    fh = tif.filehandle
    for off, n in zip(page.dataoffsets, page.databytecounts):  # one row per strip
        fh.seek(off)
        a = page.decode(fh.read(n), 0)[0].reshape(-1)[:w].astype(float)
        valid = a >= 0
        acc += np.where(valid, a, 0).reshape(NX, 4).sum(1); cnt += valid.reshape(NX, 4).sum(1)
        row += 1
        if row % 4 == 0:
            r = (top + row) // 4 - 1
            v = np.where(cnt >= 4, acc / np.maximum(cnt, 1) / 100, np.nan)  # cm -> m; at least a quarter of the cell
            v = (np.exp(np.round(40 * np.log1p(v)) / 40) - 1) * 10           # about 2.5 % steps (finer near the top), in dm
            out[r] = np.where(np.isnan(v), 65535, np.clip(np.round(v), 0, 65534)).astype(np.uint16)
            acc[:] = 0; cnt[:] = 0
    return split("global-shangguan", "Shangguan et al. 2017, depth to bedrock (BDTICM, SoilGrids 2017), mean over 2'",
                 "CC BY 4.0", [tiles.field("quaternary_m", out, type="u16", scale=0.1)])


# ------------------------------------------------------------------ polygons

def rings(shape):
    pts = shape.points; parts = list(shape.parts) + [len(pts)]
    for a, b in zip(parts[:-1], parts[1:]):
        ring = pts[a:b]
        area = sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(ring, ring[1:] + ring[:1])) / 2
        yield ring, area < 0                                        # shapefile outer rings are clockwise


def raster_lonlat(reader, value_of, scale=1):
    """Polygons in degrees onto the 2' world grid (at scale x finer, then the centre sample); value 0 = none."""
    img = Image.new("I", (NX * scale, NY * scale), 0)
    dr = ImageDraw.Draw(img)
    k = 30 * scale
    for sr in reader.iterShapeRecords():
        v = value_of(sr.record)
        if not v:
            continue
        for ring, outer in rings(sr.shape):
            if len(ring) >= 3:
                dr.polygon([((x + 180) * k, (90 - y) * k) for x, y in ring], fill=v if outer else 0)
    a = np.asarray(img, np.int32)
    return a[scale // 2::scale, scale // 2::scale]


def laea_north(lat, lon):
    """North polar Lambert azimuthal equal-area on WGS84 (Snyder 1987), metres."""
    a, f = 6378137.0, 1 / 298.257223563; e = math.sqrt(2 * f - f * f)
    def q(phi):
        s = np.sin(phi)
        return (1 - e * e) * (s / (1 - e * e * s * s) - 1 / (2 * e) * np.log((1 - e * s) / (1 + e * s)))
    rho = a * np.sqrt(np.maximum(q(np.pi / 2) - q(np.radians(lat)), 0))
    lam = np.radians(lon)
    return rho * np.sin(lam), -rho * np.cos(lam)


def stereo_north(lat, lon, lat_ts=71.0):
    """EPSG:3995 polar stereographic (WGS84, latitude of true scale 71 N), metres."""
    a, f = 6378137.0, 1 / 298.257223563; e = math.sqrt(2 * f - f * f)
    def t(phi):
        s = np.sin(phi)
        return np.tan(np.pi / 4 - phi / 2) / ((1 - e * s) / (1 + e * s)) ** (e / 2)
    pc = math.radians(lat_ts)
    mc = math.cos(pc) / math.sqrt(1 - e * e * math.sin(pc) ** 2)
    rho = a * mc * t(np.radians(lat)) / t(pc)
    lam = np.radians(lon)
    return rho * np.sin(lam), -rho * np.cos(lam)


def glaciation(data):
    """0 never glaciated, 1 by an older ice sheet only (MIS 6, 12 or 16), 2 at the last glacial maximum. Northern
    Hemisphere only (Batchelor et al. 2019); southern ice (Patagonia, New Zealand, Antarctica) is not in it."""
    import shapefile
    d = os.path.join(data, "batchelor2019")
    out = np.zeros((NY, NX), np.uint8)
    south = int(60 * 30)                                   # rows north of 30 N
    lat = centres_lat()[:south][:, None]; lon = centres_lon()[None, :]
    x, y = laea_north(np.broadcast_to(lat, (south, NX)), np.broadcast_to(lon, (south, NX)))
    px = 2000.0                                            # 2 km raster in the projection
    for stage in ("MIS16", "MIS12", "MIS6"):
        r = shapefile.Reader(os.path.join(d, f"{stage}_best_estimate"))
        x0, y0, x1, y1 = r.bbox
        w, h = int((x1 - x0) / px) + 2, int((y1 - y0) / px) + 2
        img = Image.new("L", (w, h), 0); dr = ImageDraw.Draw(img)
        for shp in r.iterShapes():
            for ring, outer in rings(shp):
                if len(ring) >= 3:
                    dr.polygon([((xx - x0) / px, (y1 - yy) / px) for xx, yy in ring], fill=1 if outer else 0)
        a = np.asarray(img)
        ix = np.clip(((x - x0) / px).astype(int), 0, w - 1); iy = np.clip(((y1 - y) / px).astype(int), 0, h - 1)
        inside = (x >= x0) & (x <= x1) & (y >= y0) & (y <= y1) & (a[iy, ix] > 0)
        out[:south][inside] = 1
    lgm = raster_lonlat(shapefile.Reader(os.path.join(d, "LGM_best_estimate")), lambda rec: 1, scale=1)
    out[lgm > 0] = 2
    out = np.where(out == 0, 255, out - 1).astype(np.uint8)  # store only glaciated ground
    return split("global-glaciation", "Batchelor et al. 2019, Northern Hemisphere ice-sheet best estimates (LGM, MIS 6, 12, 16)",
                 "no licence stated on OSF 7jen3 (the article is CC BY 4.0); confirm before release", [tiles.field("glaciation", out, legend=["older", "lgm"])])


def permafrost(data):
    """Permafrost probability (0-100 %) from Obu et al. 2019, overview level 1 (about 3.7 km), sampled at cell centres."""
    d = os.path.join(data, "permafrost")
    name = [f for f in os.listdir(d) if f.endswith(".tif.ovr")][0]
    tfw = [float(v) for v in open(os.path.join(d, name.replace(".tif.ovr", ".tfw"))).read().split()]
    page = tifffile.TiffFile(os.path.join(d, name)).pages[1]
    a = page.asarray()
    h, w = a.shape
    full_w = round(-2 * tfw[4] / tfw[0])                  # the grid is centred on the pole
    px = tfw[0] * full_w / w
    ox, oy = tfw[4] - tfw[0] / 2, tfw[5] + tfw[0] / 2      # outer corner of the first pixel
    south = int(65 * 30)                                   # rows north of 25 N
    lat = np.broadcast_to(centres_lat()[:south][:, None], (south, NX)); lon = np.broadcast_to(centres_lon()[None, :], (south, NX))
    x, y = stereo_north(lat, lon)
    ix = ((x - ox) / px).astype(int); iy = ((oy - y) / px).astype(int)
    ok = (ix >= 0) & (ix < w) & (iy >= 0) & (iy < h)
    v = np.full((south, NX), np.nan)
    v[ok] = a[iy[ok], ix[ok]]
    v[(v < 0) | (v > 1)] = np.nan
    out = np.full((NY, NX), 255, np.uint8)
    pct = np.where(np.isnan(v) | (v < 0.01), 255, np.clip(np.round(v * 100), 1, 100))
    out[:south] = pct.astype(np.uint8)
    return split("global-permafrost", "Obu et al. 2019, permafrost probability 2000-2016 (ESA GlobPermafrost), overview at about 3.7 km",
                 "CC BY 3.0", [tiles.field("permafrost_pct", out)])


def legend_raster(reader, key):
    legend = sorted({str(rec[key]).strip() for rec in reader.iterRecords() if str(rec[key]).strip()})
    a = raster_lonlat(reader, lambda rec: legend.index(str(rec[key]).strip()) + 1 if str(rec[key]).strip() else 0, scale=1)
    return np.where(a == 0, 255, a - 1).astype(np.uint8), legend


def provinces(data):
    import shapefile
    r = shapefile.Reader(os.path.join(data, "hasterok", "global_gprv"))
    fields = []
    for key, name in (("prov_type", "province"), ("lastorogen", "last_orogen"), ("crust_type", "crust_type")):
        a, legend = legend_raster(r, key)
        fields.append(tiles.field(name, a, legend=legend))
    return split("global-provinces", "Hasterok et al. 2022, global geological provinces (Zenodo 5093930)", "CC BY 4.0", fields)


def ages(data):
    import shapefile
    r = shapefile.Reader(os.path.join(data, "chorlton", "gen"))
    era, el = legend_raster(r, "ERA")
    rx, rl = legend_raster(r, "RXTYP")
    return split("global-chorlton", "Chorlton 2007, Generalized geology of the world (GSC Open File 5529): era and bedrock class",
                 "Open Government Licence - Canada", [tiles.field("bedrock_era", era, legend=el), tiles.field("bedrock_class", rx, legend=rl)])


SETS = {"soilgrids": soil, "shangguan": shangguan, "glaciation": glaciation, "permafrost": permafrost,
        "provinces": provinces, "chorlton": ages}


def main(data, out, only):
    os.makedirs(out, exist_ok=True)
    total = 0
    for name, fn in SETS.items():
        if only and name not in only:
            continue
        blobs = fn(data)
        n = tiles.write_set(os.path.join(out, f"global-{name}.egts"), blobs)
        total += n
        print(f"  global-{name:12s} {len(blobs):4d} tiles {n:>12,} bytes", flush=True)
    print(f"  total {total:,} bytes")


if __name__ == "__main__":
    out = sys.argv[2] if len(sys.argv) > 2 and sys.argv[2] != "-" else os.path.join(REPO, "assets", "earth", "config", "ground")
    main(sys.argv[1], out, set(sys.argv[3:]))
