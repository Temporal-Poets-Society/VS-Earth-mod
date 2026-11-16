"""
Bakes the planes of Earth's monthly temperature layer (decision 0086, EA-0144) on the 2' terrain grid, into an
intermediate .npz for the data store's writer (the file format, its coding and shipping are the data side's).

For each month m = 1..12 and each 2' cell:
  - mean:   the month's mean air temperature (CHELSA V2.1 tas 1981-2010), reduced to sea level at 6.5 C/km from CHELSA's
            own height, the 4 x 4 CHELSA pixels of the cell averaged as tools/bake_chelsa.py does (int16, C x 10);
  - range:  the month's mean daily maximum minus mean daily minimum (CHELSA V2.1 tasmax - tasmin), not reduced (uint8,
            0.2 C steps);
  - spread: the standard deviation of the daily mean around the place's smooth seasonal cycle over the days of that
            month (ERA5 daily statistics, 0.25 deg, bilinear to 2') (uint8, 0.1 C steps).
Then one provenance byte: bit 0 the mean is CHELSA's, bit 1 the range is CHELSA's, bit 2 the spread is measured; 0 bits
are filled (north of 84 N: the means from climate.bin's yearly cycle, as the game reads it today).

Stages, each writing its own .npz in the output folder (default C:\\VS mods\\work\\seasons):
    .venv/Scripts/python tools/bake_seasons.py means  [chelsa folder] [--out folder]
    .venv/Scripts/python tools/bake_seasons.py range  [chelsa folder] [--out folder]     (needs tasmax and tasmin)
    .venv/Scripts/python tools/bake_seasons.py spread [era5 folder]   [--out folder]     (ERA5's daily means, one .nc a year)
    .venv/Scripts/python tools/bake_seasons.py final  [--out folder]                     (seasons-layer.npz for the data side's writer)

Sources: Karger, D.N. et al. (2017): Climatologies at high resolution for the earth's land surface areas. Scientific Data
4, 170122 (CHELSA V2.1, CC BY 4.0). Hersbach, H. et al. (2020): The ERA5 global reanalysis (Copernicus C3S, CC BY 4.0).
"""
import argparse, os, sys, time

import numpy as np
import tifffile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bake_chelsa as bc

RES = bc.RES                   # cells per degree (2')
H, W = 180 * RES, 360 * RES    # Earth's 2' grid, rows from 90 N, columns from 180 W
SUB = bc.SUB
MEAN_STEP = 5                  # tenths: means published at 0.5 C, as temperature.bin
RANGE_SCALE = 1.10             # CHELSA's range to the stations' (least squares over 564 station-months: 1.099)
T0 = time.time()
DEFAULT_OUT = r"C:\VS mods\work\seasons"
EARTH_RAW = os.environ.get("EARTH_RAW", os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "rawdata"))


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


def read_var(folder, var, month):
    """A CHELSA V2.1 monthly climatology (K x 10 as uint16) as float32 C, NaN where it has none."""
    path = os.path.join(folder, f"CHELSA_{var}_{month:02d}_1981-2010_V.2.1.tif")
    with tifffile.TiffFile(path) as t:
        page = t.pages[0]
        tags = {tag.name: tag.value for tag in page.tags.values()}
        a = page.asarray()
    v = a.astype(np.float32) * np.float32(0.1) - np.float32(273.15)
    nodata = tags.get("GDAL_NODATA")
    if nodata is not None:
        v[a == int(float(nodata))] = np.nan
    return v, tags


def to_cells(v, dem):
    """CHELSA pixels to 2' cells: the mean of the cell's 4 x 4 pixels with data, and how many had data."""
    n, w = v.shape
    ch, cw = n // SUB, w // SUB
    valid = np.isfinite(v)
    c = valid.reshape(ch, SUB, cw, SUB).sum(axis=(1, 3))
    s = np.where(valid, v, 0).reshape(ch, SUB, cw, SUB).sum(axis=(1, 3), dtype=np.float64)
    return (s / np.maximum(c, 1)).astype(np.float32), c


def place(a, r0, fill):
    g = np.full((H, W), fill, a.dtype)
    g[r0:r0 + a.shape[0]] = a
    return g


def fill_holes(planes, ok):
    """Cells without the source's data take the nearest cell with data in their column (north of 84 N: the
    northernmost row CHELSA has), then in their row; marked as filled in the provenance bits."""
    out = planes.copy()
    rows = np.arange(planes.shape[1])[:, None]
    # down the column from the north: the nearest valid row at or below; then from the south for what is left
    idx = np.where(ok, rows, planes.shape[1])
    nxt = np.minimum.accumulate(idx[::-1], axis=0)[::-1]
    has = nxt < planes.shape[1]
    cols = np.arange(planes.shape[2])[None, :]
    src = np.where(has, nxt, 0)
    for m in range(planes.shape[0]):
        out[m] = np.where(ok, planes[m], np.where(has, planes[m][src, cols], planes[m]))
    left = ~ok & ~has
    if left.any():
        prv = np.maximum.accumulate(np.where(ok, rows, -1), axis=0)
        for m in range(planes.shape[0]):
            out[m] = np.where(left, planes[m][np.maximum(prv, 0), cols], out[m])
    return out


def stage_means(folder, out):
    dem, top_lat, west = bc.load_dem(folder)
    r0 = int(round((90 - top_lat) * RES))
    lapse = np.float32(bc.LAPSE)
    planes, count = [], None
    for m in range(1, 13):
        tas, tags = read_var(folder, "tas", m)
        if tas.shape != dem.shape:
            raise SystemExit(f"tas {m}: shape {tas.shape}, dem {dem.shape}")
        cells, c = to_cells(tas + lapse * dem, dem)
        count = c if count is None else np.minimum(count, c)
        planes.append(cells)
        log(f"tas {m:02d}: sea level {np.nanmin(cells):.1f}..{np.nanmax(cells):.1f} C")
        del tas
    ok = count >= bc.MIN_PIXELS
    means = np.stack([place(np.where(ok, p, np.nan), r0, np.float32(np.nan)) for p in planes])
    measured = place(ok, r0, False)
    means = fill_holes(means, measured)
    q = np.where(np.isfinite(means), np.rint(means * 10 / MEAN_STEP) * MEAN_STEP, 0).astype(np.int16)
    path = os.path.join(out, "seasons-means.npz")
    np.savez(path, means=q, measured=measured)
    log(f"{path}: 12 x {H} x {W} int16 (C x 10, step {MEAN_STEP}), CHELSA in {measured.mean() * 100:.1f}% of cells; "
        f"{os.path.getsize(path) / 1e9:.2f} GB")


def stage_range(folder, out):
    dem, top_lat, west = bc.load_dem(folder)
    r0 = int(round((90 - top_lat) * RES))
    planes, count = [], None
    for m in range(1, 13):
        hi, _ = read_var(folder, "tasmax", m)
        lo, _ = read_var(folder, "tasmin", m)
        # (decision 0086) CHELSA's day-night range is narrower than the stations' by about a tenth (its daily extremes come
        # from a smoothed reanalysis): scaled by RANGE_SCALE, fitted at the GHCN-Daily stations (tools/check_seasons.py)
        cells, c = to_cells((hi - lo) * np.float32(RANGE_SCALE), dem)
        del hi, lo
        count = c if count is None else np.minimum(count, c)
        planes.append(cells)
        log(f"range {m:02d}: {np.nanmin(cells):.1f}..{np.nanmax(cells):.1f} C")
    ok = count >= bc.MIN_PIXELS
    ranges = np.stack([place(np.where(ok, p, np.nan), r0, np.float32(np.nan)) for p in planes])
    measured = place(ok, r0, False)
    ranges = fill_holes(ranges, measured)
    q = np.where(np.isfinite(ranges), np.clip(np.rint(ranges / 0.2), 0, 255), 0).astype(np.uint8)
    path = os.path.join(out, "seasons-range.npz")
    np.savez(path, range=q, measured=measured)
    log(f"{path}: range {q.min() * 0.2:.1f}..{q.max() * 0.2:.1f} C, CHELSA in {measured.mean() * 100:.1f}% of cells")


# ------------------------------------------------------------------------------------------ the day-to-day spread (ERA5)

# The years of seasons-1.bin's spread: the ten in hand when the author decided, 16 November 2026, to release on them
# ("Use 10 years"; decision 0086). A later 20-year rebake (1991-2010) changes this list and writes a new file name.
ERA5_YEAR_LIST = (1991, 1992, 1993, 1994, 1995, 1996, 1997, 1998, 2000, 2001)


def era5_files(folder):
    def year(f):
        digits = "".join(ch for ch in f[:-3].rsplit("-", 1)[-1] if ch.isdigit())
        return int(digits) if digits else -1
    files = sorted(f for f in os.listdir(folder) if f.endswith(".nc") and year(f) in ERA5_YEAR_LIST)
    if not files:
        raise SystemExit(f"no ERA5 NetCDF files in {folder}")
    return [os.path.join(folder, f) for f in files]


def era5_days(path):
    """The daily means (C) and their dates in one ERA5 daily-statistics file: rows from 90 N, columns from 0 E."""
    import netCDF4
    with netCDF4.Dataset(path) as d:
        var = next(v for v in ("t2m", "2m_temperature", "T2M") if v in d.variables)
        tname = next(v for v in ("valid_time", "time") if v in d.variables)
        t = d.variables[tname]
        dates = netCDF4.num2date(t[:], t.units, only_use_cftime_datetimes=False, only_use_python_datetimes=True)
        lat = np.asarray(d.variables["latitude"][:])
        a = d.variables[var][:]
        a = np.asarray(a.filled(np.nan) if hasattr(a, "filled") else a, np.float32) - np.float32(273.15)
    if lat[0] < lat[-1]:
        a = a[:, ::-1]
    return a, dates


def doy365(d):
    """Day of a 365-day year (29 February returns -1: left out, as tools/check_seasons.py does at the stations)."""
    if d.month == 2 and d.day == 29:
        return -1
    import datetime
    return datetime.date(2001, d.month, d.day).timetuple().tm_yday - 1


def stage_spread(folder, out):
    """The standard deviation of the daily mean around its smoothed calendar-day cycle (31-day running mean of the
    calendar-day climatology), per month, as the stations' spread in tools/check_seasons.py; on ERA5's 0.25 deg grid,
    then bilinear onto the 2' grid (columns wrap)."""
    files = era5_files(folder)
    clim = cnt = None
    for f in files:                                       # pass 1: the calendar-day climatology
        a, dates = era5_days(f)
        if clim is None:
            clim = np.zeros((365,) + a.shape[1:], np.float64)
            cnt = np.zeros(365, np.int64)
        for i, d in enumerate(dates):
            k = doy365(d)
            if k >= 0:
                clim[k] += a[i]
                cnt[k] += 1
        log(f"climatology: {os.path.basename(f)} ({len(dates)} days)")
    clim /= np.maximum(cnt, 1)[:, None, None]
    sm = np.zeros_like(clim, np.float32)                  # 31-day running mean, circular
    cs = np.concatenate([clim[-15:], clim, clim[:15]]).cumsum(axis=0)
    cs = np.concatenate([np.zeros((1,) + clim.shape[1:]), cs])
    for k in range(365):
        sm[k] = (cs[k + 31] - cs[k]) / 31
    del clim, cs
    sq = np.zeros((12,) + sm.shape[1:], np.float64)
    n = np.zeros(12, np.int64)
    for f in files:                                       # pass 2: the departures' variance per month
        a, dates = era5_days(f)
        for i, d in enumerate(dates):
            k = doy365(d)
            if k >= 0:
                dev = a[i] - sm[k]
                sq[d.month - 1] += dev.astype(np.float64) ** 2
                n[d.month - 1] += 1
        log(f"departures: {os.path.basename(f)}")
    sd = np.sqrt(sq / np.maximum(n, 1)[:, None, None]).astype(np.float32)
    del sq, sm
    # onto the 2' grid: ERA5 rows from 90 N in 0.25 deg steps, columns from 0 E
    ny, nx = sd.shape[1:]
    step = 180.0 / (ny - 1)
    lat = 90 - (np.arange(H) + 0.5) / RES
    lon = -180 + (np.arange(W) + 0.5) / RES
    fy = (90 - lat) / step
    fx = (lon % 360) / (360.0 / nx)
    y0 = np.clip(np.floor(fy).astype(int), 0, ny - 2); wy = (fy - y0).astype(np.float32)
    x0 = np.floor(fx).astype(int) % nx; x1 = (x0 + 1) % nx; wx = (fx - np.floor(fx)).astype(np.float32)
    spread = np.zeros((12, H, W), np.uint8)
    for m in range(12):
        g = sd[m]
        top = g[y0][:, x0] * (1 - wx) + g[y0][:, x1] * wx
        bot = g[y0 + 1][:, x0] * (1 - wx) + g[y0 + 1][:, x1] * wx
        v = top * (1 - wy)[:, None] + bot * wy[:, None]
        spread[m] = np.clip(np.rint(v / 0.1), 0, 255).astype(np.uint8)
        log(f"spread {m + 1:02d}: {v.min():.1f}..{v.max():.1f} C")
    path = os.path.join(out, "seasons-spread.npz")
    np.savez(path, spread=spread, years=np.array([os.path.basename(f) for f in files]))
    log(f"{path}: written from {len(files)} files")


SOURCES = ("Monthly mean, maximum and minimum air temperature 1981-2010: CHELSA V2.1 (Karger, D.N. et al. 2017, Climatologies "
           "at high resolution for the earth's land surface areas, Scientific Data 4, 170122; CC BY 4.0; www.chelsa-climate.org). "
           "Day-to-day spread: ERA5 post-processed daily statistics on single levels, 2 m temperature daily mean, 1991-1998, 2000 and 2001 "
           "(Hersbach, H. et al. 2020, The ERA5 global reanalysis; Copernicus Climate Change Service, Climate Data Store; "
           "CC BY 4.0). The means moved to around 1318 (decision 0087): Last Millennium Reanalysis v2.1 (Tardif et al. 2019, "
           "NOAA NCEI, public domain), GISTEMP v4 (NASA GISS, public domain), and the CMIP6 PMIP4 past1000 and historical "
           "simulations of ACCESS-ESM1-5, MIROC-ES2L, MRI-ESM2-0, INM-CM4-8 and EC-Earth3-Veg-LR (CC BY 4.0, via ESGF). "
           "Baked by Earth's tools/bake_seasons.py (decision 0086).")


def paleo1318_on_grid(path, month):
    """(Decision 0087) earthdata/paleo1318.bin's correction for one month (0 = January) on the 2' grid, bilinear as
    Core.Paleo1318.At reads it (rows from 90 S, columns from 0 E, 2 degrees apart, columns wrapping)."""
    with open(path, "rb") as f:
        if f.read(4) != b"EP13":
            raise SystemExit(f"{path}: not a 1318 correction")
        version, rows, cols = np.frombuffer(f.read(12), "<i4")
        corr = np.frombuffer(f.read(), "<i2").reshape(12, rows, cols).astype(np.float32) * np.float32(0.01)
    lat = 90 - (np.arange(H) + 0.5) / RES
    lon = -180 + (np.arange(W) + 0.5) / RES
    fr = (lat + 90) / 180 * (rows - 1)
    r0 = np.clip(np.floor(fr).astype(int), 0, rows - 2); tr = np.clip(fr - r0, 0, 1).astype(np.float32)[:, None]
    fc = (lon % 360) / 360 * cols
    c0 = np.floor(fc).astype(int) % cols; c1 = (c0 + 1) % cols; tc = (fc - np.floor(fc)).astype(np.float32)[None, :]
    g = corr[month]
    top = g[r0][:, c0] * (1 - tc) + g[r0][:, c1] * tc
    bot = g[r0 + 1][:, c0] * (1 - tc) + g[r0 + 1][:, c1] * tc
    return top * (1 - tr) + bot * tr


PALEO = os.environ.get("EARTH_PALEO1318", "C:/VS mods/work/seasons/paleo1318.bin")


def stage_final(out):
    """The data side's input: mean, range, spread, provenance (bit 0 the mean measured, bit 1 the range, bit 2 the
    spread), sources."""
    m = np.load(os.path.join(out, "seasons-means.npz"))
    r = np.load(os.path.join(out, "seasons-range.npz"))
    sp = np.load(os.path.join(out, "seasons-spread.npz"))
    prov = (m["measured"].astype(np.uint8) | (r["measured"].astype(np.uint8) << 1) | np.uint8(4))
    # (decision 0087) the means as around 1318: the correction added month by month, then rounded to the means' step again
    means = m["means"].astype(np.float32) / 10
    for month in range(12):
        means[month] += paleo1318_on_grid(PALEO, month)
    mean_q = (np.rint(means * 10 / MEAN_STEP) * MEAN_STEP).astype(np.int16)
    log(f"the means moved to 1318: by {(mean_q.astype(np.float32) - m['means']).mean() / 10:+.2f} C on average over the cells")
    path = os.path.join(out, "seasons-layer.npz")
    # (decision 0086) the published steps, chosen with the data side for the file's size (about 51 MB instead of 72): the
    # range in 0.4 C, the spread in 0.2 C (rms rounding 0.12 and 0.06 C, against the stations' 1.83 and 0.38 C)
    range_q = np.clip(np.rint(r["range"].astype(np.float32) / 2) * 2, 0, 254).astype(np.uint8)
    spread_q = np.clip(np.rint(sp["spread"].astype(np.float32) / 2) * 2, 0, 254).astype(np.uint8)
    np.savez(path, mean=mean_q, range=range_q, spread=spread_q, provenance=prov, sources=np.array(SOURCES))
    log(f"{path}: written")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("stage", choices=["means", "range", "spread", "final"])
    ap.add_argument("folder", nargs="?")
    ap.add_argument("--out", default=DEFAULT_OUT)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    if a.stage == "means":
        stage_means(a.folder or os.path.join(EARTH_RAW, "chelsa-v2.1"), a.out)
    elif a.stage == "range":
        stage_range(a.folder or os.path.join(EARTH_RAW, "chelsa-v2.1"), a.out)
    elif a.stage == "spread":
        stage_spread(a.folder or os.path.join(EARTH_RAW, "era5-daily-t2m"), a.out)
    else:
        stage_final(a.out)


if __name__ == "__main__":
    main()
