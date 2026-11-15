"""
Bakes CHELSA v2.1 monthly mean air temperatures (tas, 1981-2010, 30 arc-seconds) into earthdata/temperature.bin: the
yearly mean, coldest and warmest month on Earth's 2' terrain grid (decision 0009, climate rules v2).

CHELSA's temperatures already follow the terrain (its downscaling includes the lapse rate and effects such as the trade-
wind inversion over Hawaii's summits). Each 2' cell averages its 4 x 4 CHELSA pixels together with CHELSA's own elevation
for the same pixels, and is stored reduced to sea level with 6.5 C/km from that reference height (the same convention as
climate.bin). The game subtracts 6.5 C/km x the drawn ground's elevation, so only the difference between the drawn ground
and CHELSA's height is extrapolated: T(ground) = T(reference) - 0.0065 (ground - reference). Storing the sea-level value
is exactly that (it is linear in the height, also under bilinear interpolation) without a fourth plane.

Inputs (rawdata/chelsa-v2.1/, see tools/download_data.sh, about 3.7 GB):
  - CHELSA_tas_MM_1981-2010_V.2.1.tif, MM = 01..12 (K x 10, scale 0.1, offset -273.15)
  - dem_latlong.sdat/.sgrd: CHELSA's orography (SAGA grid, int16 m, rows bottom to top)
Karger, D.N., Conrad, O., Böhner, J., Kawohl, T., Kreft, H., Soria-Auza, R.W., Zimmermann, N.E., Linder, H.P., Kessler, M.
(2017): Climatologies at high resolution for the earth's land surface areas. Scientific Data 4, 170122. CC-BY 4.0.

temperature.bin: header EMTP, version (1; with predicted planes 1 | 1 << 16), width, height, tile, tile count, then (offset,
length) per tile and the tiles, predicted planes (tools/planecoding.py, EA-0077) of the yearly mean, coldest and warmest
month (int16 in 0.1 C units, publication precision 0.5 C so a step of 5, sea level, the filled planes; plain files before
2.0.0-rc.14 held brotli of their Paeth residuals) and a data
mask (1 byte per cell; 0 where CHELSA has no data, which is only north of 84 N: CHELSA covers the sea too, with its
orography 0 there; the game keeps climate.bin where the mask is 0).

    .venv/Scripts/python tools/bake_chelsa.py [chelsa folder] [--check]
"""
import argparse, os, struct, sys, time

import numpy as np
import tifffile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import planecoding as pc

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "earthdata")
RES = 30              # Earth's terrain cells per degree (2')
TILE = 180            # cells per tile side (6°), as terrain.bin
SUB = 4               # CHELSA pixels per cell side (30" -> 2')
MIN_PIXELS = 4        # a cell needs a quarter of its pixels on land, else the coarse climate stays
LAPSE = 0.0065
DEFAULT_TEMPERATURE_STEP = 0.5
T0 = time.time()


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


def read_sgrd(path):
    h = {}
    with open(path, encoding="latin-1") as f:
        for line in f:
            if "=" in line:
                k, v = line.split("=", 1)
                h[k.strip()] = v.strip()
    return h


def load_dem(folder):
    """CHELSA's orography, top row first (lat 84 - 1/240), as float32 m with NaN where there is none."""
    h = read_sgrd(os.path.join(folder, "dem_latlong.sgrd"))
    w, n = int(h["CELLCOUNT_X"]), int(h["CELLCOUNT_Y"])
    if h["DATAFORMAT"] != "SHORTINT" or h["BYTEORDER_BIG"] != "FALSE" or abs(float(h["CELLSIZE"]) - 1 / 120) > 1e-9:
        raise SystemExit("dem_latlong.sgrd: unexpected layout " + str(h))
    dem = np.fromfile(os.path.join(folder, "dem_latlong.sdat"), "<i2", count=w * n).reshape(n, w)
    if h["TOPTOBOTTOM"] == "FALSE":
        dem = dem[::-1]
    top = float(h["POSITION_YMIN"]) + (n - 1) * float(h["CELLSIZE"]) + float(h["CELLSIZE"]) / 2
    nodata = float(h["NODATA_VALUE"].split(";")[0])
    out = dem.astype(np.float32)
    out[dem == nodata] = np.nan
    return out, top, float(h["POSITION_XMIN"]) - float(h["CELLSIZE"]) / 2


def read_tas(folder, month):
    path = os.path.join(folder, f"CHELSA_tas_{month:02d}_1981-2010_V.2.1.tif")
    with tifffile.TiffFile(path) as t:
        page = t.pages[0]
        tags = {tag.name: tag.value for tag in page.tags.values()}
        a = page.asarray()
    nodata = tags.get("GDAL_NODATA")
    v = a.astype(np.float32) * 0.1 - 273.15
    if nodata is not None:
        v[a == int(float(nodata))] = np.nan
    return v, tags


def bake(folder):
    dem, top_lat, west = load_dem(folder)
    log(f"dem {dem.shape}, top edge {top_lat:.4f}, west edge {west:.4f}, {np.isfinite(dem).mean() * 100:.1f}% with data")
    n, w = dem.shape
    if w != 360 * 120:
        raise SystemExit("dem: not global")
    ch, cw = n // SUB, w // SUB
    sums = np.zeros((12, ch, cw), np.float64)
    count = None
    for m in range(1, 13):
        tas, tags = read_tas(folder, m)
        if tas.shape != dem.shape:
            raise SystemExit(f"tas {m}: shape {tas.shape}, dem {dem.shape}")
        if m == 1:
            log(f"tas tags: tiepoint {tags.get('ModelTiepointTag')}, scale {tags.get('ModelPixelScaleTag')}, nodata {tags.get('GDAL_NODATA')}")
            tie = tags.get("ModelTiepointTag")
            if tie is not None and (abs(tie[3] - west) > 0.01 or abs(tie[4] - top_lat) > 0.01):
                raise SystemExit(f"tas grid ({tie[3]}, {tie[4]}) is not the dem's ({west}, {top_lat})")
        # sea-level equivalents at CHELSA's own pixel heights
        sl = tas + LAPSE * dem
        valid = np.isfinite(sl)
        c = valid.reshape(ch, SUB, cw, SUB).sum(axis=(1, 3))
        count = c if count is None else np.minimum(count, c)
        sums[m - 1] = np.where(valid, sl, 0).reshape(ch, SUB, cw, SUB).sum(axis=(1, 3), dtype=np.float64) / np.maximum(c, 1)
        log(f"month {m}: {np.nanmin(tas):.1f}..{np.nanmax(tas):.1f} C")
        del tas, sl, valid
    ok = count >= MIN_PIXELS
    t_mean = sums.mean(axis=0)
    t_cold = sums.min(axis=0)
    t_warm = sums.max(axis=0)
    href = np.where(np.isfinite(dem), dem, 0).reshape(ch, SUB, cw, SUB).mean(axis=(1, 3))
    # into Earth's grid: rows from 90 N; CHELSA starts at top_lat
    H, W = 180 * RES, 360 * RES
    r0 = int(round((90 - top_lat) * RES))
    def place(a, fill):
        g = np.full((H, W), fill, a.dtype)
        g[r0:r0 + ch] = a
        return g
    land = place(ok, False)
    planes = [place(t, np.nan) for t in (t_mean, t_cold, t_warm)]
    log(f"cells with CHELSA: {land.mean() * 100:.1f}%; yearly mean (sea level) {np.nanmin(np.where(land, planes[0], np.nan)):.1f}..{np.nanmax(np.where(land, planes[0], np.nan)):.1f} C")
    return land, planes, place(href, np.float64(0))


def paeth_residual(q):
    """Residual against the planar predictor left + up - upleft (zeros outside the tile), as terrain.bin."""
    a = np.zeros_like(q); b = np.zeros_like(q); c = np.zeros_like(q)
    a[:, 1:] = q[:, :-1]; b[1:, :] = q[:-1, :]; c[1:, 1:] = q[:-1, :-1]
    return q - (a + b - c)


def filled(tile, mask):
    """No-data cells take the nearest data cell along the row (then the column), so the residuals stay small."""
    t = tile.copy()
    if mask.all() or not mask.any():
        return np.where(mask, t, 0)
    idx = np.where(mask, np.arange(t.shape[1])[None, :], -1)
    np.maximum.accumulate(idx, axis=1, out=idx)
    rows = np.arange(t.shape[0])[:, None]
    t = np.where(idx >= 0, t[rows, np.maximum(idx, 0)], np.nan)
    rev = np.where(mask[:, ::-1], np.arange(t.shape[1])[None, :], -1)
    np.maximum.accumulate(rev, axis=1, out=rev)
    back = tile[:, ::-1][rows, np.maximum(rev, 0)][:, ::-1]
    t = np.where(np.isnan(t), np.where(rev[:, ::-1] >= 0, back, np.nan), t)
    # whole rows without data: from the row above or below
    col = np.where(np.isfinite(t).any(axis=1))[0]
    if len(col):
        near = col[np.clip(np.searchsorted(col, np.arange(t.shape[0])), 0, len(col) - 1)]
        t = np.where(np.isfinite(t), t, t[near])
    return np.nan_to_num(t)


def quantize_temperature(q, step=DEFAULT_TEMPERATURE_STEP):
    """Keep the existing tenths-of-degree encoding, with less numeric precision, at the same spatial resolution."""
    units = int(round(step * 10))
    if units < 1 or abs(units / 10 - step) > 1e-9:
        raise ValueError("temperature step must be a positive multiple of 0.1 C")
    return (np.rint(q.astype(np.float64) / units) * units).astype('<i2')


def write(land, planes, step=DEFAULT_TEMPERATURE_STEP):
    H, W = land.shape
    q = [quantize_temperature(np.clip(np.round(p * 10), -32000, 32000), step) for p in planes]
    tiles = []
    for ty in range(H // TILE):
        for tx in range(W // TILE):
            sl = (slice(ty * TILE, (ty + 1) * TILE), slice(tx * TILE, (tx + 1) * TILE))
            m = land[sl]
            # the values the plain file's Paeth residuals decoded to (int16, wrapped as the reader wraps them)
            tiles.append([filled(p[sl], m).astype(np.int32).astype("<i2") for p in q] + [m.astype(np.uint8)])
    path = os.path.join(OUT, "temperature.bin")
    pc.write_tiled(path, "EMTP", 1, W, H, TILE, tiles, [pc.I16, pc.I16, pc.I16, pc.U8], steps=["auto", "auto", "auto", 1])
    log(f"temperature.bin {os.path.getsize(path) / 1e6:.1f} MB, {len(tiles)} tiles")


# a quick look while baking: CHELSA at the stations (the in-game check is tests/EarthMapTests CheckFineTemperature)
STATIONS = [
    ("Mauna Loa Observatory", 19.536, -155.576, 3397), ("Mauna Kea summit", 19.821, -155.468, 4205), ("Hilo", 19.721, -155.048, 11),
    ("Quito", -0.178, -78.467, 2850), ("La Paz El Alto", -16.513, -68.192, 4061), ("Kilimanjaro summit", -3.076, 37.353, 5895),
    ("Sonnblick", 47.054, 12.958, 3106), ("Säntis", 47.250, 9.343, 2502), ("Geneva", 46.248, 6.128, 411), ("Lhasa", 29.65, 91.13, 3650),
]


def report(land, planes, href):
    for name, lat, lon, elev in STATIONS:
        r, c = int((90 - lat) * RES), int((lon + 180) * RES)
        if not land[r, c]:
            print(f"  {name}: no CHELSA cell")
            continue
        tm, tc, tw = (p[r, c] - LAPSE * elev for p in planes)
        print(f"  {name} ({elev} m; cell reference {href[r, c]:.0f} m): mean {tm:.1f}, coldest {tc:.1f}, warmest {tw:.1f} C")


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('folder', nargs='?', default=os.path.join(ROOT, 'rawdata', 'chelsa-v2.1'))
    ap.add_argument('--temperature-step', type=float, default=DEFAULT_TEMPERATURE_STEP)
    ap.add_argument('--check', action='store_true')
    args = ap.parse_args()
    quantize_temperature(np.zeros((1, 1)), args.temperature_step)
    land, planes, href = bake(args.folder)
    report(land, planes, href)
    if not args.check:
        write(land, planes, args.temperature_step)
