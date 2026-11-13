"""Wet days and ten-day dry spells at real weather stations, to fit and check how Earth's rain is spread over the days.

GHCN-Daily (NOAA NCEI; Menne et al. 2012, doi:10.1175/JTECH-D-11-00103.1, dataset doi:10.7289/V5D21VHZ): for each place
below, the station within 60 km with daily precipitation from 1991 or earlier to 2020 or later, the longest record first.
Per calendar month over 1971-2020 (values that failed a quality check left out): the mean mm a day; the share of days with
1 mm or more; wet spells (runs of such days) starting per day; the share of days whose ten days up to and including them
had less than 1, 3 and 5 mm (only windows with all ten days known). 'full' marks stations with at least 10 years and 200
ten-day windows in every month.

Only these monthly numbers are used (WeatherTests dryspells reads the table; CheckDrySpells has a few of them written in);
the station records are not shipped.

Usage: python -I tools/ghcnd_dryspells.py [--data rawdata/ghcnd] [--out tests/out/weather/ghcnd-dryspells.tsv] [--download]
  --download fetches ghcnd-stations.txt, ghcnd-inventory.txt and the picked stations' files (about 350 MB) into --data.
Then: dotnet run -c Release --project tests/WeatherTests -- <root> dryspells <out> [detail]
"""
import argparse
import csv
import datetime as dt
import math
import os
import sys
import urllib.request

BASE = 'https://www.ncei.noaa.gov/pub/data/ghcn/daily/'
ACCESS = 'https://www.ncei.noaa.gov/data/global-historical-climatology-network-daily/access/'
Y0, Y1 = 1971, 2020
THRESHOLDS = (1.0, 3.0, 5.0)

PLACES = [
    # dry summers (Mediterranean climates)
    ("Madrid", 40.42, -3.70), ("Seville", 37.39, -5.98), ("Lisbon", 38.72, -9.14), ("Rome", 41.90, 12.50),
    ("Athens", 37.98, 23.73), ("Izmir", 38.42, 27.14), ("Los Angeles", 34.05, -118.24), ("Sacramento", 38.58, -121.49),
    ("Fresno", 36.74, -119.79), ("Perth", -31.95, 115.86), ("Adelaide", -34.93, 138.60), ("Santiago", -33.45, -70.67),
    ("Cape Town", -33.92, 18.42),
    # semi-arid and desert
    ("Phoenix", 33.45, -112.07), ("Las Vegas", 36.17, -115.14), ("Elko", 40.83, -115.76), ("Denver", 39.74, -104.99),
    ("Salt Lake City", 40.76, -111.89), ("Albuquerque", 35.08, -106.65), ("El Paso", 31.76, -106.49),
    ("Alice Springs", -23.70, 133.88), ("Tamanrasset", 22.79, 5.52), ("Niamey", 13.51, 2.11), ("Kano", 12.00, 8.52),
    ("Khartoum", 15.50, 32.56), ("Riyadh", 24.71, 46.68), ("Tehran", 35.69, 51.39), ("Tashkent", 41.30, 69.24),
    ("Ulaanbaatar", 47.92, 106.92), ("Karachi", 24.86, 67.01), ("Boise", 43.62, -116.20),
    # monsoon and savanna dry seasons
    ("Darwin", -12.46, 130.84), ("Mumbai", 19.08, 72.88), ("Nagpur", 21.15, 79.09), ("New Delhi", 28.61, 77.21),
    ("Bamako", 12.64, -8.00), ("Brasilia", -15.79, -47.88), ("Cuiaba", -15.60, -56.10), ("Harare", -17.83, 31.05),
    ("Lusaka", -15.39, 28.32), ("Chiang Mai", 18.79, 98.98), ("Mandalay", 21.95, 96.08), ("Townsville", -19.26, 146.82),
    # continental and wet
    ("Yakutsk", 62.03, 129.73), ("Irkutsk", 52.29, 104.28), ("Dodge City", 37.75, -100.02), ("Winnipeg", 49.90, -97.14),
    ("Calgary", 51.05, -114.07), ("London", 51.51, -0.13), ("Vienna", 48.21, 16.37), ("Moscow", 55.76, 37.62),
    ("Riga", 56.95, 24.11), ("Singapore", 1.35, 103.82), ("Manaus", -3.12, -60.02), ("Tampa", 27.95, -82.46),
    ("Oklahoma City", 35.47, -97.52), ("Paris", 48.86, 2.35), ("Berlin", 52.52, 13.40), ("Tokyo", 35.68, 139.69),
    ("Seattle", 47.61, -122.33), ("Kansas City", 39.10, -94.58), ("Miami", 25.76, -80.19),
]


def km(a, b, c, e):
    p1, p2 = math.radians(a), math.radians(c)
    x = math.sin((p2 - p1) / 2) ** 2 + math.cos(p1) * math.cos(p2) * math.sin(math.radians(e - b) / 2) ** 2
    return 12742 * math.asin(math.sqrt(min(1, x)))


def fetch(url, path):
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    req = urllib.request.Request(url, headers={'User-Agent': 'earth-mod-ghcnd-dryspells'})
    with urllib.request.urlopen(req, timeout=300) as r, open(path + '.part', 'wb') as f:
        while True:
            b = r.read(1 << 20)
            if not b:
                break
            f.write(b)
    os.replace(path + '.part', path)


def pick(data):
    inv = {}
    with open(os.path.join(data, 'ghcnd-inventory.txt'), encoding='ascii', errors='replace') as f:
        for line in f:
            if line[31:35] == 'PRCP':
                inv[line[0:11]] = (float(line[12:20]), float(line[21:30]), int(line[36:40]), int(line[41:45]))
    out = []
    for name, lat, lon in PLACES:
        best = None
        for sid, (a, b, y0, y1) in inv.items():
            if abs(a - lat) > 0.7 or y0 > 1991 or y1 < 2020:
                continue
            dist = km(lat, lon, a, b)
            if dist > 60:
                continue
            score = (min(y1, 2024) - max(y0, 1951)) - dist / 10
            if best is None or score > best[0]:
                best = (score, sid, a, b)
        if best is None:
            print('no station for', name)
        else:
            out.append((name, best[1], best[2], best[3]))
    return out


def stats(path):
    rain = {}
    with open(path, encoding='utf8', errors='replace', newline='') as f:
        r = csv.reader(f)
        head = next(r)
        ip, ia, idate = head.index('PRCP'), head.index('PRCP_ATTRIBUTES'), head.index('DATE')
        for rec in r:
            y = int(rec[idate][:4])
            v = rec[ip].strip()
            if y < Y0 - 1 or y > Y1 or not v:
                continue
            attr = rec[ia].split(',') if rec[ia] else ['', '', '']
            if len(attr) > 1 and attr[1].strip():
                continue
            rain[dt.date.fromisoformat(rec[idate])] = int(v) / 10.0
    n = [0] * 12; tot = [0.0] * 12; wet = [0] * 12; starts = [0] * 12; dn = [0] * 12
    dry = [[0] * 12 for _ in THRESHOLDS]
    years = [set() for _ in range(12)]
    one = dt.timedelta(days=1)
    day = dt.date(Y0, 1, 1)
    while day <= dt.date(Y1, 12, 31):
        v = rain.get(day)
        m = day.month - 1
        if v is not None:
            n[m] += 1; tot[m] += v; years[m].add(day.year)
            if v >= 1:
                wet[m] += 1
                pv = rain.get(day - one)
                if pv is not None and pv < 1:
                    starts[m] += 1
            win = [rain.get(day - k * one) for k in range(10)]
            if all(w is not None for w in win):
                s = sum(win)
                dn[m] += 1
                for i, t in enumerate(THRESHOLDS):
                    if s < t:
                        dry[i][m] += 1
        day += one
    full = all(len(years[m]) >= 10 and dn[m] >= 200 for m in range(12))
    rows = []
    for m in range(12):
        if n[m] and dn[m]:
            rows.append([m + 1, len(years[m]), round(tot[m] / n[m], 3), round(wet[m] / n[m], 4), round(starts[m] / n[m], 4)]
                        + [round(dry[i][m] / dn[m], 4) for i in range(len(THRESHOLDS))] + [int(full)])
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--data', default='rawdata/ghcnd')
    ap.add_argument('--out', default='tests/out/weather/ghcnd-dryspells.tsv')
    ap.add_argument('--download', action='store_true')
    a = ap.parse_args()
    if a.download:
        for f in ('ghcnd-stations.txt', 'ghcnd-inventory.txt', 'readme.txt'):
            fetch(BASE + f, os.path.join(a.data, f))
    picks = pick(a.data)
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, 'w', encoding='utf8', newline='') as f:
        w = csv.writer(f, delimiter='\t')
        w.writerow(['name', 'id', 'lat', 'lon', 'month', 'years', 'mm_day', 'wet1', 'spells_day', 'dry10_1', 'dry10_3', 'dry10_5', 'full'])
        for name, sid, lat, lon in picks:
            path = os.path.join(a.data, 'access', sid + '.csv')
            if a.download:
                fetch(ACCESS + sid + '.csv', path)
            if not os.path.exists(path):
                print('missing', path, '(--download)')
                continue
            rows = stats(path)
            for r in rows:
                w.writerow([name, sid, lat, lon] + r)
            print(f'{name:16} {sid}  ' + ' '.join(f'{100 * r[7]:3.0f}' for r in rows) + ('' if rows and rows[0][-1] else '   (thin)'))
    print('wrote', a.out)


if __name__ == '__main__':
    sys.exit(main())
