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
    data = open(path, 'rb').read()
    assert data[:4] == b'EMWX'
    version, w, h, months = struct.unpack_from('<iiii', data, 4)
    raw = brotli.decompress(data[20:])
    n = w * h
    planes = np.frombuffer(raw, dtype=np.uint8)
    lut = np.exp(np.arange(256) / 32.0) - 1
    precip = lut[planes[:12 * n].reshape(12, h, w)]
    temp = planes[12 * n:24 * n].reshape(12, h, w) / 2.0 - 50
    land = planes[24 * n:25 * n].reshape(h, w) / 255.0
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


if __name__ == '__main__':
    main()
