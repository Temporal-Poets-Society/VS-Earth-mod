"""EA-0025 (weather rules 6, WeatherModel.TemperatureAnomaly): day-to-day temperature spells and rain cooling at the GHCN-Daily stations Earth already has (rawdata/ghcnd/access).

Per station and calendar month, 1991-2020, days with TMAX, TMIN and PRCP:
  sd      standard deviation of the daily mean's departure from its smoothed seasonal cycle (31-day running mean of the
          calendar-day climatology), i.e. how strong the warm and cold spells are
  range   warmest minus coldest monthly mean (continentality)
  wetdT   mean TMAX on days with >= 1 mm minus on dry days (departures, so the season is out), the rain's cooling by day
  wetdN   the same for TMIN (clouds keep wet nights warmer)
Usage: python -I tools/fit_weather_temperature.py [rawdata/ghcnd/access] [tests/out/weather/temperature-spells.tsv]
(the station files come from tools/ghcnd_dryspells.py --download). Prints a table and fits sd = a + b * (1 - exp(-range / c)) * (1 - s * smoothstep(0, 20, monthly mean)).
"""
import csv, math, os, sys
import numpy as np

ACCESS = sys.argv[1] if len(sys.argv) > 1 else "rawdata/ghcnd/access"
rows = []
for name in sorted(os.listdir(ACCESS)):
    path = os.path.join(ACCESS, name)
    days = {}
    lat = lon = None
    sname = name
    with open(path, encoding="utf8", errors="replace", newline="") as f:
        for r in csv.DictReader(f):
            d = r["DATE"]
            y = int(d[:4])
            if y < 1991 or y > 2020: continue
            try:
                tx, tn = float(r["TMAX"]) / 10, float(r["TMIN"]) / 10
            except (ValueError, KeyError):
                continue
            if r.get("TMAX_ATTRIBUTES", ",,").split(",")[1:2] not in ([""], []) or r.get("TMIN_ATTRIBUTES", ",,").split(",")[1:2] not in ([""], []):
                continue  # failed a quality check
            try:
                p = float(r["PRCP"]) / 10
            except (ValueError, KeyError):
                p = float("nan")
            lat, lon, sname = float(r["LATITUDE"]), float(r["LONGITUDE"]), r["NAME"]
            m, dd = int(d[5:7]), int(d[8:10])
            if m == 2 and dd == 29: continue
            days[(y, m, dd)] = (tx, tn, p)
    if len(days) < 3000: continue
    # calendar-day climatology of the daily mean, smoothed
    import datetime
    doy = {}
    for (y, m, dd), (tx, tn, p) in days.items():
        k = datetime.date(2001, m, dd).timetuple().tm_yday - 1
        doy.setdefault(k, []).append((tx + tn) / 2)
    clim = np.full(365, np.nan)
    for k, v in doy.items(): clim[k] = np.mean(v)
    if np.isnan(clim).any():
        idx = np.arange(365); ok = ~np.isnan(clim)
        clim = np.interp(idx, idx[ok], clim[ok], period=365)
    sm = np.array([np.mean(np.take(clim, range(k - 15, k + 16), mode="wrap")) for k in range(365)])
    climx = {}; climn = {}
    for (y, m, dd), (tx, tn, p) in days.items():
        k = datetime.date(2001, m, dd).timetuple().tm_yday - 1
        climx.setdefault(k, []).append(tx); climn.setdefault(k, []).append(tn)
    smx = np.array([np.mean([x for j in range(k - 15, k + 16) for x in climx.get(j % 365, [])]) for k in range(365)])
    smn = np.array([np.mean([x for j in range(k - 15, k + 16) for x in climn.get(j % 365, [])]) for k in range(365)])
    month_mean = np.zeros(12); per = [[] for _ in range(12)]; wx = [[] for _ in range(12)]; dx = [[] for _ in range(12)]
    wn = [[] for _ in range(12)]; dn = [[] for _ in range(12)]
    for (y, m, dd), (tx, tn, p) in days.items():
        k = datetime.date(2001, m, dd).timetuple().tm_yday - 1
        per[m - 1].append((tx + tn) / 2 - sm[k])
        if not math.isnan(p):
            (wx if p >= 1 else dx)[m - 1].append(tx - smx[k])
            (wn if p >= 1 else dn)[m - 1].append(tn - smn[k])
    for m in range(12):
        ks = [datetime.date(2001, m + 1, 15).timetuple().tm_yday - 1]
        month_mean[m] = sm[ks[0]]
    rng = month_mean.max() - month_mean.min()
    for m in range(12):
        if len(per[m]) < 200: continue
        wetdT = np.mean(wx[m]) - np.mean(dx[m]) if len(wx[m]) > 30 and len(dx[m]) > 30 else float("nan")
        wetdN = np.mean(wn[m]) - np.mean(dn[m]) if len(wn[m]) > 30 and len(dn[m]) > 30 else float("nan")
        rows.append((sname.strip()[:22], lat, lon, m + 1, month_mean[m], rng, float(np.std(per[m])), wetdT, wetdN))

out = sys.argv[2] if len(sys.argv) > 2 else "tests/out/weather/temperature-spells.tsv"
os.makedirs(os.path.dirname(out), exist_ok=True)
with open(out, "w", encoding="utf8") as f:
    f.write("station\tlat\tlon\tmonth\tmean\trange\tsd\twetdTmax\twetdTmin\n")
    for r in rows: f.write("\t".join(str(round(v, 3)) if isinstance(v, float) else str(v) for v in r) + "\n")

R = np.array([[r[4], r[5], r[6], r[7], r[8], abs(r[1])] for r in rows])
mean, rng, sd = R[:, 0], R[:, 1], R[:, 2]
def ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)
best = None
for c in np.arange(6, 40, 1.0):
    for s in np.arange(0, 0.8, 0.05):
        X = np.column_stack([np.ones_like(rng), (1 - np.exp(-rng / c)) * (1 - s * ss(0, 20, mean))])
        coef, res, *_ = np.linalg.lstsq(X, sd, rcond=None)
        err = np.sqrt(np.mean((X @ coef - sd) ** 2))
        if best is None or err < best[0]: best = (err, c, s, coef)
err, c, s, (a, b) = best
pred = a + b * (1 - np.exp(-rng / c)) * (1 - s * ss(0, 20, mean))
print(f"stations-months {len(rows)}; sd = {a:.3f} + {b:.3f} (1 - exp(-range/{c:.0f})) (1 - {s:.2f} smoothstep(0, 20, T)); rms {err:.2f} C, r {np.corrcoef(pred, sd)[0,1]:.2f}")
for name in ["YAKUTSK", "MOSCOW", "LONDON", "KANSAS", "SINGAPORE", "MIAMI", "PERTH", "RIGA", "DENVER", "MUMBAI", "PHOENIX", "TOKYO"]:
    sel = [i for i, r in enumerate(rows) if name in r[0].upper()]
    if not sel: continue
    j = [i for i in sel if rows[i][3] in (1, 7)]
    print("  " + name.title() + ": " + ", ".join(f"m{rows[i][3]} sd {rows[i][6]:.1f} fit {pred[i]:.1f} wet-dry Tmax {rows[i][7]:+.1f} Tmin {rows[i][8]:+.1f}" for i in j))
wT, wN = R[:, 3], R[:, 4]
ok = ~np.isnan(wT)
print(f"wet minus dry days: Tmax median {np.median(wT[ok]):+.2f} C (quartiles {np.percentile(wT[ok],25):+.2f} {np.percentile(wT[ok],75):+.2f}); Tmin median {np.median(wN[ok]):+.2f} C (quartiles {np.percentile(wN[ok],25):+.2f} {np.percentile(wN[ok],75):+.2f})")
for lo, hi in [(-40, 0), (0, 10), (10, 20), (20, 35)]:
    sel = ok & (mean >= lo) & (mean < hi)
    print(f"  months {lo}..{hi} C: Tmax {np.median(wT[sel]):+.2f}, Tmin {np.median(wN[sel]):+.2f} (n {sel.sum()})")
print("table:", out)
