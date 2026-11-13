"""How often precipitation falls (the share of the time), month by month, at the airports next to the GHCN-Daily places of
tools/ghcnd_dryspells.py, from the present weather of their routine METAR reports.

Source: the Iowa Environmental Mesonet's METAR archive (Iowa State University; https://mesonet.agron.iastate.edu/,
ASOS/AWOS/METAR download service, cgi-bin/request/asos.py), routine reports only (report_type 3: specials come with the
weather and would count it twice), 2017-2023, the present-weather column alone. A report counts as precipitation when it
has rain, drizzle, snow, snow grains, ice pellets, hail, graupel, ice crystals or unknown precipitation falling at the
station (not in the vicinity, not blowing or drifting snow); thunder alone does not count. The share of such reports is
the share of the time it rains or snows: WeatherModel.RainRate is fitted to it, and the full WeatherTests run checks the
rain chance against it at eight places.

Only these monthly numbers are used; the reports are not shipped.

Usage: python -I tools/iem_metar_precip.py [--data <folder for the downloaded reports>] [--out tests/out/weather/metar-precip.tsv]
  [--download]  (about 100 MB; one request per airport, a few seconds apart as the service asks)
"""
import argparse
import csv
import os
import sys
import time
import urllib.error
import urllib.request

URL = ('https://mesonet.agron.iastate.edu/cgi-bin/request/asos.py?station={id}&data=wxcodes&year1={y0}&month1=1&day1=1'
       '&year2={y1}&month2=1&day2=1&tz=UTC&format=onlycomma&report_type=3&missing=M')
Y0, Y1 = 2017, 2024   # 1 January 2017 up to 1 January 2024

# the GHCN-Daily place (tools/ghcnd_dryspells.py) and the METAR station next to it (IEM ids: three letters in the US)
AIRPORTS = [
    ("Madrid", "LEMD"), ("Seville", "LEZL"), ("Lisbon", "LPPT"), ("Rome", "LIRF"), ("Athens", "LGAV"), ("Izmir", "LTBJ"),
    ("Los Angeles", "LAX"), ("Sacramento", "SAC"), ("Fresno", "FAT"), ("Perth", "YPPH"), ("Adelaide", "YPAD"),
    ("Santiago", "SCEL"), ("Cape Town", "FACT"), ("Phoenix", "PHX"), ("Las Vegas", "LAS"), ("Elko", "EKO"),
    ("Denver", "DEN"), ("Salt Lake City", "SLC"), ("Albuquerque", "ABQ"), ("El Paso", "ELP"), ("Alice Springs", "YBAS"),
    ("Niamey", "DRRN"), ("Kano", "DNKN"), ("Khartoum", "HSSS"), ("Riyadh", "OERK"), ("Tehran", "OIII"),
    ("Tashkent", "UTTT"), ("Ulaanbaatar", "ZMCK"), ("Karachi", "OPKC"), ("Boise", "BOI"), ("Darwin", "YPDN"),
    ("Mumbai", "VABB"), ("Nagpur", "VANP"), ("New Delhi", "VIDP"), ("Bamako", "GABS"), ("Brasilia", "SBBR"),
    ("Cuiaba", "SBCY"), ("Harare", "FVRG"), ("Lusaka", "FLKK"), ("Chiang Mai", "VTCC"), ("Mandalay", "VYMD"),
    ("Townsville", "YBTL"), ("Yakutsk", "UEEE"), ("Irkutsk", "UIII"), ("Dodge City", "DDC"), ("Winnipeg", "CYWG"),
    ("Calgary", "CYYC"), ("London", "EGLL"), ("Vienna", "LOWW"), ("Moscow", "UUEE"), ("Riga", "EVRA"),
    ("Singapore", "WSSS"), ("Manaus", "SBEG"), ("Tampa", "TPA"), ("Oklahoma City", "OKC"), ("Paris", "LFPG"),
    ("Berlin", "EDDB"), ("Tokyo", "RJTT"), ("Seattle", "SEA"), ("Kansas City", "MCI"), ("Miami", "MIA"),
]

PRECIP = ('RA', 'DZ', 'SN', 'SG', 'PL', 'GR', 'GS', 'IC', 'UP')


def precipitating(wx):
    """True when the present weather has precipitation falling at the station."""
    if not wx or wx == 'M':
        return False
    for tok in wx.split():
        t = tok.lstrip('+-')
        if t.startswith('VC') or t.startswith('BL') or t.startswith('DR') or t.startswith('RE'):
            continue
        # strip the descriptors (MI BC PR SH TS FZ) two letters at a time
        while t[:2] in ('MI', 'BC', 'PR', 'SH', 'TS', 'FZ'):
            t = t[2:]
        if any(t[i:i + 2] in PRECIP for i in range(0, len(t) - 1, 2)):
            return True
    return False


def fetch(sid, path):
    if os.path.exists(path) and os.path.getsize(path) > 100:
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    req = urllib.request.Request(URL.format(id=sid, y0=Y0, y1=Y1), headers={'User-Agent': 'earth-mod-metar-precip'})
    for attempt in range(6):
        try:
            with urllib.request.urlopen(req, timeout=600) as r, open(path + '.part', 'wb') as f:
                while True:
                    b = r.read(1 << 20)
                    if not b:
                        break
                    f.write(b)
            os.replace(path + '.part', path)
            return
        except urllib.error.HTTPError as e:
            if e.code in (429, 503):
                time.sleep(10 * (attempt + 1))
                continue
            raise


def stats(path):
    n = [0] * 12
    p = [0] * 12
    years = [set() for _ in range(12)]
    with open(path, encoding='utf8', errors='replace', newline='') as f:
        r = csv.reader(f)
        head = next(r, None)
        if not head or 'wxcodes' not in head:
            return None
        iv, iw = head.index('valid'), head.index('wxcodes')
        for rec in r:
            if len(rec) <= max(iv, iw):
                continue
            m = int(rec[iv][5:7]) - 1
            n[m] += 1
            years[m].add(rec[iv][:4])
            if precipitating(rec[iw]):
                p[m] += 1
    return [(m + 1, len(years[m]), n[m], p[m] / n[m] if n[m] else float('nan')) for m in range(12)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--data', default='rawdata/metar-iem')
    ap.add_argument('--out', default='tests/out/weather/metar-precip.tsv')
    ap.add_argument('--download', action='store_true')
    a = ap.parse_args()
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, 'w', encoding='utf8', newline='') as f:
        w = csv.writer(f, delimiter='\t')
        w.writerow(['name', 'id', 'month', 'years', 'reports', 'precip'])
        for name, sid in AIRPORTS:
            path = os.path.join(a.data, sid + '.csv')
            if a.download and not (os.path.exists(path) and os.path.getsize(path) > 100):
                fetch(sid, path)
                time.sleep(3)
            if not os.path.exists(path):
                print('missing', path, '(--download)')
                continue
            rows = stats(path)
            if rows is None:
                print('no reports for', name, sid)
                continue
            for m, y, n, fr in rows:
                w.writerow([name, sid, m, y, n, f'{fr:.4f}'])
            print(f'{name:16} {sid:5} ' + ' '.join(f'{100 * r[3]:4.1f}' for r in rows) + f'   ({sum(r[2] for r in rows)} reports)')
            sys.stdout.flush()
    print('wrote', a.out)


if __name__ == '__main__':
    sys.exit(main())
