"""Catalogue coverage by region: counts, ore tonnage and contained metal per commodity."""
import csv, sys
from collections import defaultdict
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import COMPACT_ORDER, REGIONS

HERE = Path(__file__).resolve().parent
rows = list(csv.DictReader(open(HERE / 'catalogue-flat.csv', encoding='utf8')))

def f(v):
    try: return float(v)
    except (TypeError, ValueError): return 0.0

# commodity -> (selector, value) ; value in tonnes metal unless noted
COMMS = [
    ('Cu', 'contained Cu (Mt)', lambda r: f(r.get('c_Cu')) / 1e6),
    ('Au', 'contained Au (kt), primary+co-product', lambda r: f(r.get('c_Au')) / 1e3),
    ('Au_primary', 'contained Au (kt) in Au-layer bodies', lambda r: f(r.get('c_Au')) / 1e3 if r['layer'] == 'Au' else 0),
    ('Ag', 'contained Ag (kt)', lambda r: f(r.get('c_Ag')) / 1e3),
    ('Zn', 'contained Zn (Mt)', lambda r: f(r.get('c_Zn')) / 1e6),
    ('Pb', 'contained Pb (Mt)', lambda r: f(r.get('c_Pb')) / 1e6),
    ('Sn', 'contained Sn (kt)', lambda r: f(r.get('c_Sn')) / 1e3),
    ('W', 'contained WO3 (kt)', lambda r: f(r.get('c_W')) / 1e3),
    ('Mo', 'contained Mo (kt)', lambda r: f(r.get('c_Mo')) / 1e3),
    ('Fe', 'contained Fe (Gt)', lambda r: f(r.get('c_Fe')) / 1e9 if r['layer'] == 'Fe' else 0),
    ('Fe_ore', 'iron ore (Gt)', lambda r: f(r['mt']) / 1e3 if r['layer'] == 'Fe' else 0),
    ('Mn', 'contained Mn (Mt)', lambda r: f(r.get('c_Mn')) / 1e6 if r['layer'] == 'Mn' else 0),
    ('Cr', 'contained Cr2O3 (Mt)', lambda r: f(r.get('c_Cr')) / 1e6 if r['layer'] == 'Cr' else 0),
    ('Ni', 'contained Ni (Mt)', lambda r: f(r.get('c_Ni')) / 1e6 if r['layer'] == 'Ni' else 0),
    ('Pt', 'contained PGE (t)', lambda r: f(r.get('c_Pt')) if r['layer'] == 'Pt' else 0),
    ('Al', 'bauxite ore (Gt)', lambda r: f(r['mt']) / 1e3 if r['layer'] == 'Al' else 0),
    ('U', 'contained U (kt)', lambda r: f(r.get('c_U')) / 1e3 if r['layer'] == 'U' else 0),
    ('diamond', 'diamonds (Mct)', lambda r: f(r.get('c_diamond')) / 1e6 if r['layer'] == 'diamond' else 0),
    ('C_coal', 'coal (Gt, basin resource)', lambda r: f(r['mt']) / 1e3 if r['layer'] == 'C_coal' else 0),
    ('K', 'potash basin salt (Gt, mt field)', lambda r: f(r['mt']) / 1e3 if r['layer'] == 'K' else 0),
    ('NaCl', 'salt (Gt)', lambda r: f(r['mt']) / 1e3 if r['layer'] == 'NaCl' else 0),
    ('P', 'phosphate rock (Gt)', lambda r: f(r['mt']) / 1e3 if r['layer'] == 'P' else 0),
]
LAYERCOUNT = {'Au': 'Au', 'Au_primary': 'Au', 'Fe_ore': 'Fe'}

def table(order, key):
    out = {}
    for code, label, fn in COMMS:
        lay = LAYERCOUNT.get(code, code)
        tot = defaultdict(float); cnt = defaultdict(int)
        for r in rows:
            v = fn(r)
            if v: tot[r[key]] += v
            if r['layer'] == lay: cnt[r[key]] += 1
        out[code] = (label, tot, cnt)
    return out

def write(order, key, fname):
    t = table(order, key)
    with open(HERE / fname, 'w', newline='', encoding='utf8') as fh:
        w = csv.writer(fh)
        w.writerow(['commodity', 'measure', 'total'] + [f'{o} share%' for o in order] + [f'{o} n' for o in order])
        for code, (label, tot, cnt) in t.items():
            T = sum(tot.values())
            w.writerow([code, label, round(T, 2)] + [round(100 * tot[o] / T, 1) if T else '' for o in order] + [cnt[o] for o in order])
    return t

t = write(COMPACT_ORDER, 'compact', 'coverage-compact.csv')
write(REGIONS, 'region', 'coverage-regions.csv')
# console markdown
print('| commodity | total | ' + ' | '.join(COMPACT_ORDER[:9]) + ' |')
for code, (label, tot, cnt) in t.items():
    T = sum(tot.values())
    print(f'| {code} ({label}) | {T:,.1f} | ' + ' | '.join(f"{100*tot[o]/T:.0f}% ({cnt[o]})" if T else '-' for o in COMPACT_ORDER[:9]) + ' |')

# counts per layer
print()
lc = defaultdict(lambda: defaultdict(int))
for r in rows: lc[r['layer']][r['compact']] += 1
print('| layer | n | ' + ' | '.join(COMPACT_ORDER[:9]) + ' |')
for lay in sorted(lc, key=lambda k: -sum(lc[k].values())):
    n = sum(lc[lay].values())
    print(f'| {lay} | {n} | ' + ' | '.join(str(lc[lay][o]) for o in COMPACT_ORDER[:9]) + ' |')
