"""
Bakes Earth's 1318 temperature correction (decision 0087): for each month and each 2-degree cell, how much colder (or
warmer) the years 1300-1330 were than 1981-2010, the period of the layer's normals (CHELSA). Written as
an intermediate paleo1318.bin in C:/VS mods/work/seasons (not shipped: tools/bake_chelsa.py --paleo and tools/bake_seasons.py final
bake it into temperature-1318.bin and the monthly layer's means) and checked against what is known of the 1300s.

    correction(cell, month) = level(cell) + season(cell, month)
    level  = LMR v2.1's annual anomaly for 1300-1330 (relative to 1951-1980; the ensemble mean over its 20 Monte Carlo
             runs) less GISTEMP v4's annual anomaly for 1981-2010 (also relative to 1951-1980): 1300-1330 against
             1981-2010, from a reanalysis that assimilates real proxies (tree rings, ice cores, corals).
    season = the CMIP6/PMIP4 past1000 models' split of their own 1300-1330 against 1981-2010 (historical) difference over
             the months: each month's difference less the year's, averaged over the five models. Only the split, never the
             models' level (their internal variability is not the real one's, and their 1981-2010 has their own warming).

Inputs (earth-raw/paleo-1318, tools/fetch_paleo_1318.py): air_MCruns_ensemble_mean_LMRv2.1.nc,
gistemp1200_GHCNv4_ERSSTv5.nc.gz, cmip6/tas_Amon_<model>_{past1000,historical}_*.nc.

paleo1318.bin: "EP13", int32 version 1, int32 rows 91, int32 columns 180 (2-degree cell centres from 90 S and from 0 E,
LMR's grid), then 12 x 91 x 180 int16 in 0.01 C, January first, rows from the south.

    .venv/Scripts/python -I tools/bake_1318.py [paleo folder] [--out C:/VS mods/work/seasons/paleo1318.bin]
"""
import argparse, gzip, os, sys, time

import numpy as np
import netCDF4

EARTH_RAW = os.environ.get("EARTH_RAW", r"C:\VS mods\data\earth-raw")
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
Y0, Y1 = 1300, 1330
M0, M1 = 1981, 2010
BASE0, BASE1 = 1951, 1980
MODELS = ["ACCESS-ESM1-5", "MIROC-ES2L", "MRI-ESM2-0", "INM-CM4-8", "EC-Earth3-Veg-LR"]
T0 = time.time()


def log(msg):
    print(f"[{time.time() - T0:6.1f}s] {msg}", flush=True)


def area_mean(field, lat, mask=None):
    w = np.cos(np.radians(lat))[:, None] * np.ones_like(field)
    ok = np.isfinite(field) if mask is None else (np.isfinite(field) & mask)
    return float((field * w)[ok].sum() / w[ok].sum())


def regrid(field, lat, lon, to_lat, to_lon):
    """Bilinear, longitudes wrapping, latitudes clamped; field[lat, lon] with lat ascending or descending."""
    if lat[0] > lat[-1]:
        lat, field = lat[::-1], field[::-1]
    lon = np.asarray(lon) % 360
    order = np.argsort(lon)
    lon, field = lon[order], field[:, order]
    lon_ext = np.concatenate([lon[-1:] - 360, lon, lon[:1] + 360])
    f_ext = np.concatenate([field[:, -1:], field, field[:, :1]], axis=1)
    yi = np.clip(np.interp(to_lat, lat, np.arange(len(lat))), 0, len(lat) - 1)
    xi = np.interp(np.asarray(to_lon) % 360, lon_ext, np.arange(len(lon_ext)))
    y0 = np.clip(np.floor(yi).astype(int), 0, len(lat) - 2); wy = (yi - y0)[:, None]
    x0 = np.clip(np.floor(xi).astype(int), 0, len(lon_ext) - 2); wx = (xi - x0)[None, :]
    a = f_ext[y0][:, x0]; b = f_ext[y0][:, x0 + 1]; c = f_ext[y0 + 1][:, x0]; d = f_ext[y0 + 1][:, x0 + 1]
    return (a * (1 - wx) + b * wx) * (1 - wy) + (c * (1 - wx) + d * wx) * wy


def fill_nan(field):
    """NaN cells take their latitude row's mean (then the field's)."""
    out = field.copy()
    for r in range(out.shape[0]):
        row = out[r]
        if np.isnan(row).any():
            row[np.isnan(row)] = np.nanmean(row) if np.isfinite(row).any() else np.nan
    out[np.isnan(out)] = np.nanmean(out)
    return out


def lmr_level(folder):
    with netCDF4.Dataset(os.path.join(folder, "air_MCruns_ensemble_mean_LMRv2.1.nc")) as d:
        lat = np.asarray(d.variables["lat"][:]); lon = np.asarray(d.variables["lon"][:])
        air = d.variables["air"]
        past = np.asarray(air[Y0:Y1 + 1], np.float64).mean(axis=(0, 1))
        base = np.asarray(air[BASE0:BASE1 + 1], np.float64).mean(axis=(0, 1))
    log(f"LMR {Y0}-{Y1}: global {area_mean(past, lat):+.2f} C against 1951-1980; its own 1951-1980: {area_mean(base, lat):+.3f} "
        "(the reference period: about 0)")
    return past, lat, lon


def gistemp_modern(folder):
    raw = gzip.open(os.path.join(folder, "gistemp1200_GHCNv4_ERSSTv5.nc.gz")).read()
    with netCDF4.Dataset("gistemp", memory=raw) as d:
        lat = np.asarray(d.variables["lat"][:]); lon = np.asarray(d.variables["lon"][:])
        t = d.variables["time"]
        dates = netCDF4.num2date(t[:], t.units, only_use_cftime_datetimes=False, only_use_python_datetimes=True)
        sel = [i for i, x in enumerate(dates) if M0 <= x.year <= M1]
        a = d.variables["tempanomaly"][sel]
        a = np.asarray(a.filled(np.nan) if hasattr(a, "filled") else a, np.float64)
    annual = np.nanmean(a, axis=0)
    log(f"GISTEMP {M0}-{M1} against 1951-1980: global {area_mean(annual, lat):+.2f} C, {np.isnan(annual).mean() * 100:.1f}% cells empty")
    return fill_nan(annual), lat, lon


def model_months(folder, model, exp, y0, y1):
    files = sorted(f for f in os.listdir(folder) if f"_{model}_{exp}_" in f and f.endswith(".nc"))
    sums = np.zeros(12); out = None; counts = np.zeros(12)
    for f in files:
        with netCDF4.Dataset(os.path.join(folder, f)) as d:
            t = d.variables["time"]
            dates = netCDF4.num2date(t[:], t.units, calendar=getattr(t, "calendar", "standard"))
            sel = [i for i, x in enumerate(dates) if y0 <= x.year <= y1]
            if not sel:
                continue
            lat = np.asarray(d.variables["lat"][:]); lon = np.asarray(d.variables["lon"][:])
            tas = d.variables["tas"]
            if out is None:
                out = np.zeros((12, len(lat), len(lon)))
            for i in sel:
                m = dates[i].month - 1
                out[m] += np.asarray(tas[i], np.float64)
                counts[m] += 1
    if out is None or counts.min() == 0:
        raise SystemExit(f"{model} {exp}: no months in {y0}-{y1}")
    log(f"  {model} {exp} {y0}-{y1}: {int(counts.sum())} months")
    return out / counts[:, None, None], lat, lon


def season_split(folder, to_lat, to_lon):
    splits = []
    for model in MODELS:
        past, lat, lon = model_months(folder, model, "past1000", Y0, Y1)
        hist, lat2, lon2 = model_months(folder, model, "historical", M0, M1)
        diff = past - hist
        split = diff - diff.mean(axis=0)
        log(f"  {model}: {Y0}-{Y1} against {M0}-{M1} {area_mean(diff.mean(axis=0), lat):+.2f} C (not used: the level is LMR's); "
            f"its months' split ranges {split.min():+.1f}..{split.max():+.1f} C")
        splits.append(np.stack([regrid(split[m], lat, lon, to_lat, to_lon) for m in range(12)]))
    return np.mean(splits, axis=0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("folder", nargs="?", default=os.path.join(EARTH_RAW, "paleo-1318"))
    ap.add_argument("--out", default="C:/VS mods/work/seasons/paleo1318.bin")
    a = ap.parse_args()
    past, lat, lon = lmr_level(a.folder)
    modern, glat, glon = gistemp_modern(a.folder)
    level = past - regrid(modern, glat, glon, lat, lon)
    season = season_split(os.path.join(a.folder, "cmip6"), lat, lon)
    corr = level[None] + season
    q = np.clip(np.rint(corr * 100), -32000, 32000).astype("<i2")
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "wb") as f:
        f.write(b"EP13"); f.write(np.array([1, len(lat), len(lon)], "<i4").tobytes()); f.write(q.tobytes())
    np.savez(os.path.join(os.path.dirname(os.path.abspath(a.out)), "paleo1318.npz"), corr=corr, lat=lat, lon=lon)
    log(f"{a.out}: {os.path.getsize(a.out)} bytes")
    report(corr, lat, lon)


def report(corr, lat, lon):
    ann = corr.mean(axis=0)
    print(f"\n1300-1330 against 1981-2010 (the correction), area-weighted:")
    print(f"  globe, year {area_mean(ann, lat):+.2f} C")
    lat2 = np.repeat(lat[:, None], len(lon), axis=1)
    for name, sel in (("north of 60 N", lat2 >= 60), ("30-60 N", (lat2 >= 30) & (lat2 < 60)), ("tropics", abs(lat2) < 30),
                      ("south of 30 S", lat2 <= -30)):
        print(f"  {name:14s} year {area_mean(ann, lat, sel):+.2f}, Jan {area_mean(corr[0], lat, sel):+.2f}, "
              f"Jul {area_mean(corr[6], lat, sel):+.2f} C")
    def at(la, lo):
        r = int(round((la + 90) / 2)); c = int(round((lo % 360) / 2)) % len(lon)
        return corr[:, r, c]
    for name, la, lo in (("Western Europe (Paris)", 48.9, 2.3), ("the Alps", 46.5, 9.0), ("Greenland, Eastern Settlement", 61.0, -45.5),
                         ("Iceland", 64.5, -19.0), ("Scandinavia", 63.0, 15.0), ("Moscow", 55.8, 37.6), ("China (Beijing)", 39.9, 116.4),
                         ("Arctic Ocean", 85.0, 0.0), ("Sahara", 23.0, 5.0), ("Amazon", -5.0, -60.0)):
        v = at(la, lo)
        print(f"  {name:30s} year {v.mean():+.2f}, Jan {v[0]:+.2f}, Jul {v[6]:+.2f} C")


if __name__ == "__main__":
    main()
