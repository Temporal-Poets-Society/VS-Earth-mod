"""Fit Earth's thunderstorm climate (Core.Thunder) against the WWLLN Global Lightning Climatology.

Earth's weather knows each month's normal temperature (sea-level equivalent), precipitation and land share
(earthdata/weather.bin). This fits how often lightning strikes from those three, so thunder follows the real
lightning map without shipping it: WGLC (Kaplan and Lau, Zenodo 10.5281/zenodo.20277101) is used for the fit only.

  monthly stroke density D = exp(c0 + cl * land^g) * sigmoid((T - Tm) / Ts) * (P / (P + Ph))^k     strokes km^-2 day^-1

(land^g with g < 1: coasts are nearly as stormy as inland, sea breezes set off storms there)

Then the share of days with thunder in a month, from the density, is fitted to known yearly thunder-day counts:

  tau = 1 - exp(-(D / Dt)^b)

Usage: python -I tools/fit_thunder.py [--weather earthdata/weather.bin] [--wglc <rawdata>/wglc/wglc_climatology_30m_monthly.nc]
Prints the constants for src/Core/Thunder.cs and the checks.

Weather rules 7 (EA-0022): the density fit gives the desert Southwest about half its real summer thunder (WGLC's own strokes
give about the real days there), because a month of 1 mm/day weighs as little rain. The hot dry months' boost

  D' = D * (1 + c * smoothstep(T: t0 -> t0 + 8) * exp(-P / pd))

is fitted with --dry <table> to the monthly share of thunder days (WT03) at US first-order stations (tools/
ghcnd_weathertypes.py), with the yearly stations above in the fit as well and Dt, b and the density constants kept, so the
rest of the world keeps its thunder:

  python -I tools/fit_thunder.py --dry tests/out/weather/ghcnd-weathertypes.tsv
"""
import argparse
import math
import os
import struct
import sys

import brotli
import netCDF4
import numpy as np
from scipy.optimize import least_squares

MONTH_DAYS = np.array([31, 28.25, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31])

# yearly thunderstorm days at stations (national weather services' long-term means, rounded; the fit tolerates
# a factor of about 1.5). Kampala is shown but not fitted: WWLLN sees few of Africa's strokes (0.7 strokes/km2/yr
# there against 37 at Lake Maracaibo), so its density says little about its 240 thunder days.
STATIONS = [
    ("Kampala", 0.31, 32.58, 240), ("Singapore", 1.35, 103.82, 170), ("Tampa", 27.98, -82.53, 85),
    ("Darwin", -12.46, 130.84, 85), ("Oklahoma City", 35.47, -97.52, 50), ("Denver", 39.74, -104.99, 40),
    ("Vienna", 48.21, 16.37, 26), ("Moscow", 55.75, 37.62, 23), ("Paris", 48.86, 2.35, 19),
    ("London", 51.50, -0.12, 13), ("Cairo", 30.04, 31.24, 3), ("Reykjavik", 64.13, -21.90, 1.5),
]


def read_weather(path):
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import planecoding as pc
    planes, _ = pc.read_weather(path)        # plain or predicted (EA-0077)
    h, w = planes[0].shape
    lut = np.exp(np.arange(256) / 32.0) - 1
    precip = lut[np.stack(planes[:12])]
    temp = np.stack(planes[12:24]) / 2.0 - 50
    land = planes[24] / 255.0
    return precip, temp, land, w // 360


def to_half_degree(a, res):
    """Mean over each 0.5° cell (north first, from -180), the weather grid's rows run north to south."""
    f = res // 2 if res >= 2 else 1
    if res == 2:
        return a
    if res > 2:
        s = a.shape
        return a.reshape(*s[:-2], s[-2] // f, f, s[-1] // f, f).mean(axis=(-3, -1))
    # res 1: repeat
    return np.repeat(np.repeat(a, 2, axis=-2), 2, axis=-1)


def model(params, T, P, L):
    c0, cl, tm, ts, ph, k, g = params
    return np.exp(c0 + cl * np.power(np.clip(L, 0, 1), g)) / (1 + np.exp(-(T - tm) / ts)) * (P / (P + ph)) ** k


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--weather', default='earthdata/weather.bin')
    ap.add_argument('--wglc', default='C:/VS mods/Earth/rawdata/wglc/wglc_climatology_30m_monthly.nc')
    a = ap.parse_args()
    precip, temp, land, res = read_weather(a.weather)
    P = to_half_degree(precip, res)
    T = to_half_degree(temp, res)
    L = to_half_degree(land, res)
    d = netCDF4.Dataset(a.wglc)
    lat = np.array(d.variables['lat'][:])
    lon = np.array(d.variables['lon'][:])
    D = np.ma.filled(d.variables['density'][:], np.nan).astype(float)
    if lat[0] < lat[-1]:
        D = D[:, ::-1, :]
        lat = lat[::-1]
    if lon[0] > 0:
        raise SystemExit('unexpected longitude order')
    assert D.shape == T.shape, (D.shape, T.shape)
    w = np.cos(np.radians(lat))[None, :, None] * np.ones_like(D)
    Lb = np.broadcast_to(L, D.shape)
    ok = np.isfinite(D) & np.isfinite(T) & (np.abs(lat)[None, :, None] < 70)
    eps = 1e-4
    y = np.log(D[ok] + eps)
    x = (T[ok], P[ok], Lb[ok])
    sw = np.sqrt(w[ok])

    def resid(p):
        return sw * (np.log(model(p, *x) + eps) - y)

    fit = least_squares(resid, [-3.0, 2.0, 22.0, 3.0, 3.0, 1.0, 1.0],
                        bounds=([-12, -5, 0, 0.5, 0.05, 0.1, 0.1], [5, 8, 35, 15, 40, 4, 3]))
    p = fit.x
    r = resid(p) / sw
    ss = np.sum(w[ok] * (y - np.average(y, weights=w[ok])) ** 2)
    r2 = 1 - np.sum(w[ok] * r ** 2) / ss
    print('density fit: c0 %.4f  cl %.4f  Tm %.3f  Ts %.3f  Ph %.4f  k %.4f  g %.4f   R2(log) %.3f' % (*p, r2))
    # yearly totals: modelled vs WGLC, by land and sea
    yearly_obs = np.nansum(D * MONTH_DAYS[:, None, None], axis=0)
    yearly_mod = np.sum(model(p, T, P, Lb) * MONTH_DAYS[:, None, None], axis=0)
    good = np.isfinite(yearly_obs) & (np.abs(lat)[:, None] < 70)
    cw = np.cos(np.radians(lat))[:, None] * np.ones_like(yearly_obs)
    for name, m in (('land', L > 0.5), ('sea', L < 0.1)):
        mm = good & m
        print('  %s: mean strokes/km2/yr observed %.2f, modelled %.2f' % (
            name, np.average(yearly_obs[mm], weights=cw[mm]), np.average(yearly_mod[mm], weights=cw[mm])))
    lo, hi = np.log(yearly_obs[good] + 0.01), np.log(yearly_mod[good] + 0.01)
    print('  yearly log correlation %.3f' % np.corrcoef(lo, hi)[0, 1])

    def cell(la, lo_):
        i = int((90 - la) * 2)
        j = int((lo_ + 180) * 2) % 720
        return i, j

    # thunder days from the modelled density at the stations: the game turns the model's density into thunder
    obs = []
    for name, la, lo_, td in STATIONS:
        i, j = cell(la, lo_)
        dens = D[:, i, j]
        obs.append((name, dens, td, model(p, T[:, i, j], P[:, i, j], L[i, j])))

    def days(q, dens):
        dt, b = q
        return np.sum(MONTH_DAYS * (1 - np.exp(-(np.maximum(dens, 0) / dt) ** b)))

    def resid2(q):
        return [math.log(days(q, mod) + 0.5) - math.log(td + 0.5) for name, _, td, mod in obs if name != 'Kampala']

    f2 = least_squares(resid2, [0.5, 0.7], bounds=([1e-4, 0.2], [50, 2]))
    q = f2.x
    print('thunder days: Dt %.4f  b %.4f' % tuple(q))
    print('  %-14s %8s %8s %8s %8s' % ('station', 'real', 'WGLC', 'model', 'dens/yr'))
    for name, dens, td, mod in obs:
        print('  %-14s %8.0f %8.1f %8.1f %8.2f' % (name, td, days(q, dens), days(q, mod), np.sum(dens * MONTH_DAYS)))


# the constants in src/Core/Thunder.cs (the density and the thunder days), which --dry keeps
DENSITY = [-5.6707, 1.7736, 18.541, 3.772, 7.6689, 1.3957, 0.1827]
DAYS = (0.0100, 0.8822)
WEST = ('PHOENIX', 'EL PASO', 'ALBUQUERQUE', 'HARRY REID', 'ELKO', 'SALT LAKE', 'DENVER', 'BOISE')


def smoothstep(x, a, b):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def fit_dry(weather, table):
    """Rules 7's hot dry months' boost (Core.Thunder.DryFactor), fitted to the US stations' monthly thunder days."""
    import csv
    P, T, L, res = read_weather(weather)
    P, T, L = to_half_degree(P, res), to_half_degree(T, res), to_half_degree(L, res)

    def cell(la, lo_):
        return int((90 - la) * 2), int((lo_ + 180) * 2) % 720

    us = {}
    with open(table) as f:
        for row in csv.DictReader(f, delimiter='	'):
            us.setdefault(row['name'].split(',')[0], [float(row['lat']), float(row['lon']), np.zeros(12)])[2][int(row['month']) - 1] = float(row['thunder'])
    obs = []
    for name, la, lo_, td in STATIONS:
        i, j = cell(la, lo_)
        obs.append((name, td, T[:, i, j], P[:, i, j], L[i, j], None))
    for name, (la, lo_, m) in us.items():
        if m.sum() == 0 or name.startswith('SEATTLE'):   # Seattle: six years of reports
            continue
        i, j = cell(la, lo_)
        obs.append((name, float(np.sum(m * MONTH_DAYS)), T[:, i, j], P[:, i, j], L[i, j], m))

    def share(q, t, pr, l):
        c, t0, pd = q
        dens = model(DENSITY, t, pr, l) * (1 + c * smoothstep(t, t0, t0 + 8) * np.exp(-pr / pd))
        return 1 - np.exp(-(dens / DAYS[0]) ** DAYS[1])

    def resid(q):
        out = []
        for name, td, t, pr, l, m in obs:
            if name == 'Kampala':
                continue
            if m is None:
                out.append(math.log(np.sum(MONTH_DAYS * share(q, t, pr, l)) + 0.5) - math.log(td + 0.5))
            else:
                out += list(2 * (np.log(share(q, t, pr, l) + 0.02) - np.log(m + 0.02)) / math.sqrt(12))
        return out

    def report(q, label):
        mae, west = [], []
        print('%s: DryBoost %.4f  DryWarm %.4f  DryRain %.4f' % (label, *q))
        for name, td, t, pr, l, m in obs:
            s = share(q, t, pr, l)
            line = '  %-22s real %5.0f model %5.0f' % (name[:22], td, np.sum(MONTH_DAYS * s))
            if m is not None:
                mae.append(np.abs(s - m).mean())
                if name.startswith(WEST):
                    west.append(np.abs(s - m)[5:9].mean())
                line += '  July real %3.0f %% model %3.0f %%' % (m[6] * 100, s[6] * 100)
            print(line)
        print('  US monthly mean error %.3f, the West June-September %.3f' % (np.mean(mae), np.mean(west)))

    report([0, 25, 1], 'rules 6 (no boost)')
    q = least_squares(resid, [2, 24, 0.7], bounds=([0, 15, .1], [60, 34, 5])).x
    report(q, 'rules 7')


if __name__ == '__main__':
    if '--dry' in sys.argv:
        k = sys.argv.index('--dry')
        w = sys.argv[sys.argv.index('--weather') + 1] if '--weather' in sys.argv else 'earthdata/weather.bin'
        fit_dry(w, sys.argv[k + 1])
    else:
        main()
