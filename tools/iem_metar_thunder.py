"""Thunder days at airports from their routine METAR reports (the Iowa Environmental Mesonet's archive, 2017-2023, the
same reports as tools/iem_metar_precip.py), month by month: the share of the local days, among those with 18 or more
reports, on which a report has thunder (TS, at the station or in its vicinity, VCTS: thunder heard, as a thunderstorm day
is defined). Also how evenly the reports cover the day (the share of the day's hours reported), since a station that
reports only by day misses night storms.

Checked against GHCN-Daily's thunder days (WT03) at the US first-order stations (tools/ghcnd_weathertypes.py), then used
for tropical Africa, where the WWLLN lightning climatology the thunder density is fitted to sees few strokes (EA-0074).
Only these monthly numbers are used; the reports are not shipped.

Usage: python -I tools/iem_metar_thunder.py [--data <reports>] [--out tests/out/weather/metar-thunder.tsv] [--download]
  --download fetches the stations below that aren't there yet (about 1.4 MB each; tools/iem_metar_precip.py has the rest).
"""
import argparse
import csv
import datetime as dt
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from iem_metar_precip import AIRPORTS, fetch  # noqa: E402

# station, name, latitude, longitude (the airport's, to 0.01°); first the stations of iem_metar_precip.py used here
STATIONS = [
    # tropical Africa (EA-0074)
    ("HUEN", "Entebbe (Kampala)", 0.04, 32.44), ("HKJK", "Nairobi", -1.32, 36.93), ("HRYR", "Kigali", -1.97, 30.14),
    ("HBBA", "Bujumbura", -3.32, 29.32), ("HTDA", "Dar es Salaam", -6.88, 39.20), ("FZAA", "Kinshasa", -4.39, 15.44),
    ("FCBB", "Brazzaville", -4.25, 15.25), ("FOOL", "Libreville", 0.46, 9.41), ("FKKD", "Douala", 4.01, 9.72),
    ("FKYS", "Yaounde", 3.72, 11.55), ("FEFF", "Bangui", 4.40, 18.52), ("DNMM", "Lagos", 6.58, 3.32),
    ("DNAA", "Abuja", 9.01, 7.26), ("DGAA", "Accra", 5.61, -0.17), ("DIAP", "Abidjan", 5.26, -3.93),
    ("DFFD", "Ouagadougou", 12.35, -1.51), ("GOOY", "Dakar", 14.74, -17.49), ("FTTJ", "N'Djamena", 12.13, 15.03),
    ("HAAB", "Addis Ababa", 8.98, 38.80), ("FZQA", "Lubumbashi", -11.59, 27.53), ("FWKI", "Lilongwe", -13.79, 33.78),
    ("FAOR", "Johannesburg", -26.14, 28.25), ("DNKN", "Kano", 12.05, 8.52), ("DRRN", "Niamey", 13.48, 2.18),
    ("GABS", "Bamako", 12.53, -7.95), ("FVRG", "Harare", -17.93, 31.09), ("FLKK", "Lusaka", -15.33, 28.45),
    # the other tropics, to compare
    ("SBEG", "Manaus", -3.04, -60.05), ("SBBE", "Belem", -1.38, -48.48), ("SKBO", "Bogota", 4.70, -74.15),
    ("WSSS", "Singapore", 1.36, 103.99), ("WMKK", "Kuala Lumpur", 2.75, 101.71), ("VTBS", "Bangkok", 13.69, 100.75),
    ("YPDN", "Darwin", -12.41, 130.88), ("MIA", "Miami", 25.79, -80.29), ("TPA", "Tampa", 27.98, -82.53),
    # US stations with GHCN-Daily thunder days (WT03), to check the method
    ("OKC", "Oklahoma City", 35.39, -97.60), ("MCI", "Kansas City", 39.30, -94.71), ("DEN", "Denver", 39.86, -104.67),
    ("PHX", "Phoenix", 33.43, -112.01), ("ABQ", "Albuquerque", 35.04, -106.61), ("ELP", "El Paso", 31.81, -106.38),
    ("SEA", "Seattle", 47.45, -122.31), ("DDC", "Dodge City", 37.76, -99.97), ("SLC", "Salt Lake City", 40.79, -111.98),
    ("LAS", "Las Vegas", 36.08, -115.15), ("BOI", "Boise", 43.56, -116.22), ("SAC", "Sacramento", 38.51, -121.49),
]


def thunder(wx):
    """True when a report has thunder at the station or in its vicinity (TS, VCTS; not recent RETS)."""
    if not wx or wx == 'M':
        return False
    for tok in wx.split():
        t = tok.lstrip('+-')
        if t.startswith('RE'):
            continue
        if 'TS' in t:
            return True
    return False


def stats(path, lon):
    days = {}   # local day -> [month, reports, thunder, hours set]
    shift = dt.timedelta(hours=lon / 15)
    with open(path, encoding='utf8', errors='replace', newline='') as f:
        r = csv.reader(f)
        head = next(r, None)
        if not head or 'wxcodes' not in head:
            return None
        iv, iw = head.index('valid'), head.index('wxcodes')
        for rec in r:
            if len(rec) <= max(iv, iw):
                continue
            try:
                loc = dt.datetime.strptime(rec[iv], '%Y-%m-%d %H:%M') + shift
            except ValueError:
                continue
            d = days.setdefault(loc.date(), [loc.month - 1, 0, 0, set()])
            d[1] += 1
            d[3].add(loc.hour)
            if thunder(rec[iw]):
                d[2] = 1
    acc = [[0, 0, 0.0, 0] for _ in range(12)]   # days, thunder days, hours covered, all days seen
    for m, n, t, hours in days.values():
        acc[m][3] += 1
        if n >= 18:
            acc[m][0] += 1
            acc[m][1] += t
            acc[m][2] += len(hours) / 24
    return acc


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--data', default='rawdata/metar-iem')
    ap.add_argument('--out', default='tests/out/weather/metar-thunder.tsv')
    ap.add_argument('--download', action='store_true')
    a = ap.parse_args()
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, 'w', encoding='utf8', newline='') as f:
        w = csv.writer(f, delimiter='\t')
        w.writerow(['id', 'name', 'lat', 'lon', 'month', 'days', 'thunder', 'coverage', 'alldays'])
        for sid, name, lat, lon in STATIONS:
            path = os.path.join(a.data, sid + '.csv')
            if a.download and not (os.path.exists(path) and os.path.getsize(path) > 100):
                fetch(sid, path)
                time.sleep(3)
            if not os.path.exists(path):
                print('missing', sid, name)
                continue
            acc = stats(path, lon)
            if acc is None:
                print('no reports', sid, name)
                continue
            for m, (n, t, cov, alld) in enumerate(acc):
                w.writerow([sid, name, lat, lon, m + 1, n, f'{t / n if n else float("nan"):.4f}', f'{cov / n if n else float("nan"):.3f}', alld])
            yearly = sum((t / n if n else 0) * md for (n, t, _, _), md in zip(acc, (31, 28.25, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31)))
            full = sum(n for n, _, _, _ in acc) / max(1, sum(x[3] for x in acc))
            cov = sum(c for _, _, c, _ in acc) / max(1, sum(n for n, _, _, _ in acc))
            print(f'{sid:5} {name:20} thunder days {yearly:5.0f}/yr   days with 18+ reports {100 * full:3.0f} %, hours covered {100 * cov:3.0f} %')
            sys.stdout.flush()
    print('wrote', a.out)


if __name__ == '__main__':
    sys.exit(main())
