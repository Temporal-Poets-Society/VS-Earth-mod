"""
Checks the monthly temperature layer (decision 0086, EA-0144) against the GHCN-Daily stations Earth has
(earth-raw/ghcnd/access), and compares it with today's live temperature (seasons rules 0).

Per station and calendar month, 1981-2010 (CHELSA's period), days with TMAX and TMIN passing GHCN's checks:
  mean    the monthly mean of the daily mean, TAVG where the station has it on 80 % of the days, else (TMAX + TMIN) / 2
  range   the mean of TMAX - TMIN (the day-night range)
  spread  the standard deviation of the daily mean's departure from its smoothed seasonal cycle (31-day running mean of
          the calendar-day climatology), as tools/fit_weather_temperature.py
The layer at the station: its 2' cell's planes (nearest cell), the means at the station's height (6.5 C/km).
Rules 0 at the station: the month's average of today's curve, (warm + cold) / 2 + (warm - cold) (s - 1/2) with vanilla's
smootherstep s (coldest mid-January, warmest mid-July, mirrored south), from the same cell's coldest and warmest month.

    .venv/Scripts/python tools/check_seasons.py [--access folder] [--layer folder] [--out tests/out/seasons]
Prints per-station lines and the summary; writes stations.tsv (one row per station-month).
"""
import argparse, csv, datetime, math, os, sys
import numpy as np
import season_curve as sc

RES = 30
LAPSE = 0.0065
EARTH_RAW = os.environ.get("EARTH_RAW", r"C:\VS mods\data\earth-raw")
MONTH_DAYS = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]


def smootherstep(x):
    x = min(max(x, 0.0), 1.0)
    return x * x * x * (x * (x * 6 - 15) + 10)


def rules0_month(t_cold, t_warm, lat, m):
    """Today's live seasonal curve averaged over month m (0..11): EarthClimateHooks.SeasonCorrection on vanilla's
    ModTemperature, without the day-night term and the noise (both average out over a month)."""
    peak = 0.5 if lat > 0 else 6.5
    acc = 0.0
    n = 60
    for i in range(n):
        month_pos = m + (i + 0.5) / n
        d = abs(((month_pos - peak) + 6) % 12 - 6) / 6
        acc += smootherstep(d)
    s = acc / n
    return (t_warm + t_cold) / 2 + (t_warm - t_cold) * (s - 0.5)


def rules0_day(t_cold, t_warm, lat, t):
    peak = 0.5 if lat > 0 else 6.5
    d = abs(((t - peak) + 6) % 12 - 6) / 6
    return (t_warm + t_cold) / 2 + (t_warm - t_cold) * (smootherstep(d) - 0.5)


def rules0_spread(yearly_range, month_mean):
    """Weather rules 6's spell size (WeatherModel.TemperatureAnomaly), the station fit, with the station's own yearly range
    and month's mean as its inputs."""
    x = min(max(month_mean / 20.0, 0.0), 1.0)
    return 0.356 + 5.436 * (1 - math.exp(-yearly_range / 14)) * (1 - 0.40 * x * x * (3 - 2 * x))


def read_station(path):
    days = {}
    meta = None
    with open(path, encoding="utf8", errors="replace", newline="") as f:
        for r in csv.DictReader(f):
            d = r["DATE"]
            y = int(d[:4])
            if y < 1981 or y > 2010:
                continue
            try:
                tx, tn = float(r["TMAX"]) / 10, float(r["TMIN"]) / 10
            except (ValueError, KeyError):
                continue
            if r.get("TMAX_ATTRIBUTES", ",,").split(",")[1:2] not in ([""], []) or r.get("TMIN_ATTRIBUTES", ",,").split(",")[1:2] not in ([""], []):
                continue
            try:
                ta = float(r["TAVG"]) / 10
            except (ValueError, KeyError):
                ta = float("nan")
            m, dd = int(d[5:7]), int(d[8:10])
            if m == 2 and dd == 29:
                continue
            meta = (r["NAME"].strip(), float(r["LATITUDE"]), float(r["LONGITUDE"]), float(r["ELEVATION"]))
            days[(y, m, dd)] = (tx, tn, ta)
    return meta, days


def station_months(days):
    doy = lambda m, dd: datetime.date(2001, m, dd).timetuple().tm_yday - 1
    have_avg = np.mean([not math.isnan(v[2]) for v in days.values()]) >= 0.8
    daily = {k: (v[2] if have_avg and not math.isnan(v[2]) else (v[0] + v[1]) / 2) for k, v in days.items()}
    clim = [[] for _ in range(365)]
    for (y, m, dd), t in daily.items():
        clim[doy(m, dd)].append(t)
    c = np.array([np.mean(v) if v else np.nan for v in clim])
    idx = np.arange(365)
    ok = ~np.isnan(c)
    c = np.interp(idx, idx[ok], c[ok], period=365)
    sm = np.array([np.mean(np.take(c, range(k - 15, k + 16), mode="wrap")) for k in range(365)])
    out = []
    for m in range(1, 13):
        sel = [(k, t) for k, t in daily.items() if k[1] == m]
        if len(sel) < 200:
            out.append(None)
            continue
        mean = float(np.mean([t for _, t in sel]))
        rng = float(np.mean([days[k][0] - days[k][1] for k, _ in sel]))
        spread = float(np.std([t - sm[doy(k[1], k[2])] for k, t in sel]))
        out.append((mean, rng, spread, len(sel)))
    return out, have_avg, sm


def peak_month(vals, warmest):
    v = np.array(vals, float)
    i = int(np.argmax(v) if warmest else np.argmin(v))
    nb = np.delete(v, [i])
    clear = (v[i] - np.max(nb) if warmest else np.min(nb) - v[i]) >= 0.0
    return i, clear


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--access", default=os.path.join(EARTH_RAW, "ghcnd", "access"))
    ap.add_argument("--layer", default=r"C:\VS mods\work\seasons")
    ap.add_argument("--out", default=os.path.join("tests", "out", "seasons"))
    a = ap.parse_args()
    # the modern means (seasons-means.npz, before the 1318 correction of decision 0087 is added in the final stage): the
    # stations measured 1981-2010, so the check is against the layer with the shift taken off
    means = np.load(os.path.join(a.layer, "seasons-means.npz"))["means"]
    rng_path = os.path.join(a.layer, "seasons-range.npz")
    ranges = np.load(rng_path)["range"] if os.path.exists(rng_path) else None
    sp_path = os.path.join(a.layer, "seasons-spread.npz")
    spreads = np.load(sp_path)["spread"] if os.path.exists(sp_path) else None
    os.makedirs(a.out, exist_ok=True)
    rows = []
    err0, err1, peaks = [], [], {"warm0": 0, "warm1": 0, "cold0": 0, "cold1": 0, "n": 0}
    rerr = []
    serr1, serr0 = [], []
    derr0, derr1 = [], []
    kern = sc.kernel()
    for name in sorted(os.listdir(a.access)):
        meta, days = read_station(os.path.join(a.access, name))
        if meta is None or len(days) < 3000:
            continue
        sname, lat, lon, elev = meta
        obs, have_avg, sm = station_months(days)
        if any(o is None for o in obs):
            print(f"  {sname[:28]:28s} skipped: a month with under 200 days in 1981-2010")
            continue
        r, c = min(int((90 - lat) * RES), 5399), int((lon + 180) * RES) % (360 * RES)
        cell = means[:, r, c].astype(float) / 10 - LAPSE * elev
        t_cold, t_warm = cell.min(), cell.max()
        r0 = [rules0_month(t_cold, t_warm, lat, m) for m in range(12)]
        om = [o[0] for o in obs]
        e0 = np.array(r0) - om
        e1 = cell - om
        err0 += list(e0)
        err1 += list(e1)
        ow, oc = peak_month(om, True)[0], peak_month(om, False)[0]
        peaks["n"] += 1
        peaks["warm0"] += int(np.argmax(r0)) == ow or abs(np.max(r0) - r0[ow]) < 1e-9
        peaks["cold0"] += int(np.argmin(r0)) == oc or abs(np.min(r0) - r0[oc]) < 1e-9
        peaks["warm1"] += int(np.argmax(cell)) == ow
        peaks["cold1"] += int(np.argmin(cell)) == oc
        # day by day against the station's smoothed calendar-day cycle (real months spread over the game's 12 equal ones)
        tday = (np.arange(365) + 0.5) / 365 * 12
        lay_day = sc.curve(cell, tday, kern)
        r0_day = np.array([rules0_day(t_cold, t_warm, lat, t) for t in tday])
        derr1 += list(lay_day - sm); derr0 += list(r0_day - sm)
        lay_rng = ranges[:, r, c].astype(float) * 0.2 if ranges is not None else None
        lay_sp = spreads[:, r, c].astype(float) * 0.1 if spreads is not None else None
        yearly_range = max(om) - min(om)
        for m in range(12):
            rows.append((sname, lat, lon, elev, m + 1, om[m], r0[m], cell[m], obs[m][1],
                         lay_rng[m] if lay_rng is not None else float("nan"), obs[m][2], "TAVG" if have_avg else "(TX+TN)/2"))
            if lay_rng is not None:
                rerr.append(lay_rng[m] - obs[m][1])
            if lay_sp is not None:
                serr1.append(lay_sp[m] - obs[m][2])
                serr0.append(rules0_spread(yearly_range, om[m]) - obs[m][2])
        print(f"  {sname[:28]:28s} {lat:6.1f} {lon:7.1f} {elev:5.0f} m  warmest obs {ow + 1:2d} layer {int(np.argmax(cell)) + 1:2d} "
              f"rules0 {int(np.argmax(r0)) + 1:2d} | coldest obs {oc + 1:2d} layer {int(np.argmin(cell)) + 1:2d} rules0 {int(np.argmin(r0)) + 1:2d} | "
              f"rms layer {np.sqrt(np.mean(e1 ** 2)):4.1f} rules0 {np.sqrt(np.mean(e0 ** 2)):4.1f} | "
              f"range obs {np.mean([o[1] for o in obs]):4.1f} spread obs {np.mean([o[2] for o in obs]):3.1f}")
    e0, e1 = np.array(err0), np.array(err1)
    n = peaks["n"]
    print(f"\n{n} stations, {len(e1)} station-months")
    print(f"monthly means: rms layer {np.sqrt(np.mean(e1 ** 2)):.2f} C (bias {e1.mean():+.2f}), rules 0 {np.sqrt(np.mean(e0 ** 2)):.2f} C (bias {e0.mean():+.2f})")
    print(f"warmest month right: layer {peaks['warm1']}/{n}, rules 0 {peaks['warm0']}/{n}; coldest: layer {peaks['cold1']}/{n}, rules 0 {peaks['cold0']}/{n}")
    d0, d1 = np.array(derr0), np.array(derr1)
    print(f"day by day against the stations' smoothed cycle: rms layer {np.sqrt(np.mean(d1 ** 2)):.2f} C, rules 0 {np.sqrt(np.mean(d0 ** 2)):.2f} C")
    if rerr:
        rerr = np.array(rerr)
        print(f"day-night range: rms layer {np.sqrt(np.mean(rerr ** 2)):.2f} C (bias {rerr.mean():+.2f})")
    if serr1:
        s1, s0 = np.array(serr1), np.array(serr0)
        print(f"day-to-day spread: rms layer {np.sqrt(np.mean(s1 ** 2)):.2f} C (bias {s1.mean():+.2f}), "
              f"rules 0's station fit {np.sqrt(np.mean(s0 ** 2)):.2f} C (bias {s0.mean():+.2f})")
    with open(os.path.join(a.out, "stations.tsv"), "w", encoding="utf8") as f:
        f.write("station\tlat\tlon\telev\tmonth\tobs_mean\trules0_mean\tlayer_mean\tobs_range\tlayer_range\tobs_spread\tobs_daily_mean\n")
        for row in rows:
            f.write("\t".join(f"{v:.2f}" if isinstance(v, float) else str(v) for v in row) + "\n")


if __name__ == "__main__":
    main()
