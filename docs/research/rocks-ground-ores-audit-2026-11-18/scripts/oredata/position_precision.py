"""Coordinate precision of catalogue point bodies: decimals, estimated coordinates, district-level names, q, by source."""
import csv, re, sys
from collections import Counter, defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
rows = [r for r in csv.DictReader(open(HERE / 'catalogue-flat.csv', encoding='utf8')) if r['kind'] == 'deposit']
DIST = re.compile(r'district|field|belt|province|placers|gravels|camp|trend|range|region|basin|goldfield|orefield|\(.*,.*\)|-[A-Z][a-z]+-', re.I)

def prec(r):
    d = min(int(r['latDec']), int(r['lonDec']))
    return d

def f(v):
    try: return float(v)
    except Exception: return None

tot = len(rows)
by = defaultdict(Counter)
for r in rows:
    d = prec(r)
    est = set(filter(None, r['est'].split(';')))
    coord_est = bool(est & {'lat', 'lon'})
    distname = bool(DIST.search(r['name']))
    cls = ('<=1 dec (>=5 km)' if d <= 1 else '2 dec (~0.5-1 km)' if d == 2 else '>=3 dec (<=100 m)')
    by['all'][cls] += 1
    by['src:' + r['src']][cls] += 1
    by['group:' + r['group']][cls] += 1
    by['q:' + r['q']][cls] += 1
    if coord_est: by['flags']['coordinates marked est'] += 1
    if distname: by['flags']['district/field-type name'] += 1
    L, Wd = f(r['lenKm']), f(r['widKm'])
    if L is not None and Wd is not None:
        small = max(L, Wd) < 2.0
        if small and d <= 2: by['flags']['footprint <2 km AND coords <=2 dec'] += 1
        if small and d <= 1: by['flags']['footprint <2 km AND coords <=1 dec'] += 1
        if small: by['flags']['footprint <2 km'] += 1
    by['flags']['q=' + r['q']] += 1
print('point bodies:', tot)
for k in sorted(by, key=lambda s: (not s.startswith('all'), s)):
    c = by[k]; n = sum(v for kk, v in c.items() if not kk.startswith('q=')) if k != 'flags' else tot
    print(f'{k:40} ' + ', '.join(f'{kk}: {v} ({100*v/max(1,n):.0f}%)' for kk, v in sorted(c.items())))

# precision vs size: share of contained metal in bodies with <=1 decimal
print()
for lay in ['Cu', 'Zn', 'Au', 'Fe', 'Sn', 'Ni', 'Cr', 'Al', 'diamond', 'U', 'NaCl']:
    sel = [r for r in rows if r['layer'] == lay]
    if not sel: continue
    tot_mt = sum(f(r['mt']) or 0 for r in sel)
    low = sum(f(r['mt']) or 0 for r in sel if prec(r) <= 1)
    two = sum(f(r['mt']) or 0 for r in sel if prec(r) == 2)
    est = sum(f(r['mt']) or 0 for r in sel if 'lat' in r['est'].split(';') or 'lon' in r['est'].split(';'))
    qc = sum(f(r['mt']) or 0 for r in sel if r['q'] == 'C')
    estsize = sum(f(r['mt']) or 0 for r in sel if set(r['est'].split(';')) & {'lenKm', 'widKm'})
    print(f'{lay:8} n={len(sel):5} ore Mt share: <=1 dec {100*low/tot_mt:5.1f}%  2 dec {100*two/tot_mt:5.1f}%  coord-est {100*est/tot_mt:5.1f}%  q=C {100*qc/tot_mt:5.1f}%  footprint estimated {100*estsize/tot_mt:5.1f}%')
