"""How rain comes at the airports of tools/iem_metar_precip.py, month by month: the share of the hours with precipitation, the
share of the days with any, how long the rain lasts once it starts and when in the day it falls, from the same routine METAR
reports (the Iowa Environmental Mesonet's archive, 2017-2023; downloaded by tools/iem_metar_precip.py --download).

Per airport and calendar month: reports, the share of them with precipitation falling at the station (iem_metar_precip.
precipitating), the share of local solar days (by the GHCN-Daily place's longitude) with 18 or more reports that have one
or more such reports, the mean length of a rain event (a run of such reports an hour apart or less: hours), and the share of
the precipitation reports in each quarter of the local day (0-6, 6-12, 12-18, 18-24 h). WeatherTests events reads the table
and compares the model's hours, days and event lengths with it.

Only these monthly numbers are used; the reports are not shipped.

Usage: python -I tools/iem_metar_events.py --ghcnd <ghcnd-dryspells.tsv> [--data <reports>] [--out tests/out/weather/metar-events.tsv]
"""
import argparse
import csv
import datetime as dt
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from iem_metar_precip import AIRPORTS, precipitating  # noqa: E402


def stats(path, lon):
    """Per month (0..11): [reports, precip reports, days, wet days, events, event hours, quarter counts x4]."""
    acc = [[0] * 10 for _ in range(12)]
    days = {}           # local day -> [month, reports, wet]
    run_last = None     # time of the last precipitation report in the current run
    run_month = None
    run_start = None
    shift = dt.timedelta(hours=lon / 15)

    def close():
        nonlocal run_start
        if run_start is not None:
            acc[run_month][4] += 1
            acc[run_month][5] += (run_last - run_start).total_seconds() / 3600 + 1
        run_start = None

    with open(path, encoding='utf8', errors='replace', newline='') as f:
        r = csv.reader(f)
        head = next(r, None)
        if not head or 'wxcodes' not in head:
            return None
        iv, iw = head.index('valid'), head.index('wxcodes')
        last_t = None
        for rec in r:
            if len(rec) <= max(iv, iw):
                continue
            try:
                t = dt.datetime.strptime(rec[iv], '%Y-%m-%d %H:%M')
            except ValueError:
                continue
            if last_t is not None and t <= last_t:
                continue   # duplicates
            last_t = t
            loc = t + shift
            m = loc.month - 1
            p = precipitating(rec[iw])
            a = acc[m]
            a[0] += 1
            key = loc.date()
            d = days.setdefault(key, [m, 0, 0])
            d[1] += 1
            if p:
                a[1] += 1
                d[2] = 1
                a[6 + loc.hour // 6] += 1
                if run_last is None or (t - run_last).total_seconds() > 1.6 * 3600:
                    close()
                    run_start = t
                    run_month = m
                run_last = t
            elif run_start is not None and (t - run_last).total_seconds() > 0.5 * 3600:
                close()
                run_last = None
        close()
    for m, n, wet in days.values():
        if n >= 18:
            acc[m][2] += 1
            acc[m][3] += wet
    return acc


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--ghcnd', required=True, help='the table tools/ghcnd_dryspells.py writes (for the places\' longitudes)')
    ap.add_argument('--data', default='rawdata/metar-iem')
    ap.add_argument('--out', default='tests/out/weather/metar-events.tsv')
    a = ap.parse_args()
    place = {}
    with open(a.ghcnd, encoding='utf8') as f:
        for row in csv.DictReader(f, delimiter='\t'):
            place[row['name']] = (float(row['lat']), float(row['lon']))
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, 'w', encoding='utf8', newline='') as f:
        w = csv.writer(f, delimiter='\t')
        w.writerow(['name', 'id', 'lat', 'lon', 'month', 'reports', 'hours', 'days', 'wetdays', 'eventh', 'q0', 'q1', 'q2', 'q3'])
        for name, sid in AIRPORTS:
            path = os.path.join(a.data, sid + '.csv')
            if name not in place or not os.path.exists(path):
                print('skipped', name, sid)
                continue
            lat, lon = place[name]
            acc = stats(path, lon)
            if acc is None:
                print('no reports for', name, sid)
                continue
            for m, v in enumerate(acc):
                n, p, nd, wd, ev, evh = v[:6]
                q = [c / p if p else 0 for c in v[6:]]
                w.writerow([name, sid, lat, lon, m + 1, n, f'{p / n if n else float("nan"):.4f}', nd,
                            f'{wd / nd if nd else float("nan"):.4f}', f'{evh / ev if ev else float("nan"):.2f}'] + [f'{x:.3f}' for x in q])
            print(f'{name:16} {sid:5} days ' + ' '.join(f'{100 * v[3] / max(1, v[2]):3.0f}' for v in acc)
                  + '  event h ' + ' '.join(f'{v[5] / max(1, v[4]):4.1f}' for v in acc))
            sys.stdout.flush()
    print('wrote', a.out)


if __name__ == '__main__':
    sys.exit(main())
