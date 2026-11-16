"""
Fits the day's temperature shape of the monthly temperature layer (decision 0086) at the 22 IEM METAR stations with
hourly temperatures (earth-raw/metar-iem-fronts, 2017-2023), against vanilla's fixed shape.

Per station and month: the mean hourly cycle by local solar time (UTC + longitude / 15), scaled to its own range
(0 at the coldest hour, 1 at the warmest). The model (Parton and Logan 1981): from sunrise + d the temperature rises as
sin(pi (t - rise - d) / (D + 2 c)), D the day length, to its top about c hours after the solar noon, and after sunset it
falls exponentially, exp(-b (t - set) / N), toward the next morning's minimum (N the night's length), both scaled so
that the minimum is 0 and the maximum 1. The global d, c and b are fitted to all station-months; the fit and vanilla's
shape (smoothstep with its minimum at 04:00 and its maximum at 16:00, whatever the sun) are scored by the rms of the
scaled cycle and by the hours of the minimum and the maximum.

    .venv/Scripts/python tools/fit_day_shape.py [folder]
"""
import csv, math, os, sys
from datetime import datetime
import numpy as np
from scipy.optimize import minimize

STATIONS = {  # IEM ids and airport coordinates (degrees)
    "BOS": (42.36, -71.01), "CYWG": (49.91, -97.24), "DDC": (37.76, -99.97), "EDDB": (52.37, 13.52), "EGLL": (51.47, -0.45),
    "EVRA": (56.92, 23.97), "FACT": (-33.97, 18.60), "LEMD": (40.47, -3.56), "LFPG": (49.01, 2.55), "LIRF": (41.80, 12.24),
    "LOWW": (48.11, 16.57), "MCI": (39.30, -94.71), "NZAA": (-37.01, 174.79), "OKC": (35.39, -97.60), "ORD": (41.98, -87.90),
    "RJTT": (35.55, 139.78), "SCEL": (-33.39, -70.79), "SEA": (47.45, -122.31), "UUEE": (55.97, 37.41), "YMML": (-37.67, 144.84),
    "YPAD": (-34.95, 138.53), "YPPH": (-31.94, 115.97),
}
FOLDER = sys.argv[1] if len(sys.argv) > 1 else r"C:\VS mods\data\earth-raw\metar-iem-fronts"


def day_length(lat, month):
    """Hours of daylight at the month's middle (declination from the day of the year; sun's centre at the horizon)."""
    doy = [15, 46, 74, 105, 135, 166, 196, 227, 258, 288, 319, 349][month - 1]
    decl = math.radians(23.44) * math.sin(2 * math.pi * (284 + doy) / 365)
    x = -math.tan(math.radians(lat)) * math.tan(decl)
    return 24 if x <= -1 else 0 if x >= 1 else 24 * math.acos(x) / math.pi


def pl_shape(D, d, c, b, hours=np.arange(24) + 0.5):
    """Parton-Logan scaled to 0..1 at the given local solar hours."""
    rise, sset = 12 - D / 2, 12 + D / 2
    N = 24 - D
    t = np.asarray(hours, float)
    out = np.empty_like(t)
    day = (t >= rise + d) & (t <= sset)
    out[day] = np.sin(np.pi * (t[day] - rise - d) / (D + 2 * c))
    t_set = np.sin(np.pi * (sset - rise - d) / (D + 2 * c))
    night = ~day
    since = np.where(t[night] > sset, t[night] - sset, t[night] + 24 - sset)
    span = N + d
    out[night] = t_set * (np.exp(-b * since / max(span, 1e-6)) - np.exp(-b)) / (1 - np.exp(-b))
    lo, hi = out.min(), out.max()
    return (out - lo) / (hi - lo) if hi > lo else out * 0


def vanilla_shape(hours=np.arange(24) + 0.5):
    x = np.abs(((hours - 4.0) + 12) % 24 - 12) / 12
    s = x * x * (3 - 2 * x)
    return (s - s.min()) / (s.max() - s.min())


def load():
    cycles = []
    for sid, (lat, lon) in STATIONS.items():
        path = os.path.join(FOLDER, sid + ".csv")
        if not os.path.exists(path):
            continue
        acc = np.zeros((12, 24)); cnt = np.zeros((12, 24))
        with open(path, newline="") as f:
            for r in csv.DictReader(f):
                try:
                    tf = float(r["tmpf"])
                except ValueError:
                    continue
                v = datetime.strptime(r["valid"], "%Y-%m-%d %H:%M")
                solar = (v.hour + v.minute / 60 + lon / 15) % 24
                acc[v.month - 1, int(solar)] += (tf - 32) / 1.8
                cnt[v.month - 1, int(solar)] += 1
        for m in range(12):
            if cnt[m].min() < 30:
                continue
            cyc = acc[m] / cnt[m]
            rngc = cyc.max() - cyc.min()
            cycles.append((sid, lat, m + 1, day_length(lat, m + 1), (cyc - cyc.min()) / rngc, rngc))
    return cycles


def score(cycles, shape_of):
    err, dmin, dmax = [], [], []
    for sid, lat, m, D, obs, rng in cycles:
        s = shape_of(D)
        err.append(np.mean((s - obs) ** 2))
        dmin.append(abs(((np.argmin(s) - np.argmin(obs)) + 12) % 24 - 12))
        dmax.append(abs(((np.argmax(s) - np.argmax(obs)) + 12) % 24 - 12))
    return math.sqrt(np.mean(err)), np.mean(dmin), np.mean(dmax)


def main():
    cycles = load()
    print(f"{len(cycles)} station-months at {len(set(c[0] for c in cycles))} stations")
    f = lambda p: score(cycles, lambda D: pl_shape(D, p[0], p[1], p[2]))[0]
    best = minimize(f, [0.5, 1.5, 2.2], method="Nelder-Mead", options={"xatol": 0.01, "fatol": 1e-6})
    d, c, b = best.x
    print(f"Parton-Logan fit: minimum {d:.2f} h after sunrise, top {c:.2f} h after solar noon + ..., night decay b {b:.2f}")
    for name, sh in (("Parton-Logan", lambda D: pl_shape(D, d, c, b)), ("vanilla 04:00/16:00", lambda D: vanilla_shape())):
        rms, emin, emax = score(cycles, sh)
        print(f"  {name:20s} rms of the scaled cycle {rms:.3f}; hour of the minimum off by {emin:.2f} h, of the maximum by {emax:.2f} h")
    # the extremes' timing by season (winter's short day against summer's long one)
    for lo, hi, label in ((0, 10, "days under 10 h"), (10, 14, "10-14 h"), (14, 25, "over 14 h")):
        sel = [x for x in cycles if lo <= x[3] < hi]
        if sel:
            obs_min = np.mean([np.argmin(x[4]) + 0.5 - (12 - x[3] / 2) for x in sel])
            obs_max = np.mean([np.argmax(x[4]) + 0.5 - 12 for x in sel])
            print(f"  {label:16s} ({len(sel):3d}): observed minimum {obs_min:+.1f} h from sunrise, maximum {obs_max:+.1f} h from noon")


if __name__ == "__main__":
    main()
