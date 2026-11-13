"""Validate the base-metals catalogue against USGS Mineral Commodity Summaries 2024 (world and country reserves, 2023 mine
production, world identified resources). Reads the MCS text extracted by fetch_base_metals.py; writes the 'validation'
section of build-report.json and prints markdown tables for the research note.
Run after build_catalogue.py.
"""
import json, re, sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *  # noqa: F401,F403

MCS = RAW / 'usgs-mcs-2024'
FILE = {'Cu': 'copper', 'Zn': 'zinc', 'Pb': 'lead', 'Ag': 'silver', 'Sn': 'tin', 'W': 'tungsten', 'Mo': 'molybdenum',
        'Sb': 'antimony', 'Bi': 'bismuth', 'Hg': 'mercury'}
# MCS table units -> tonnes of metal: production and reserves columns
UNITS = {'Cu': (1e3, 1e3), 'Zn': (1e3, 1e3), 'Pb': (1e3, 1e3), 'Ag': (1, 1), 'Sn': (1, 1), 'W': (1, 1), 'Mo': (1, 1e3),
         'Sb': (1, 1), 'Bi': (1, 1), 'Hg': (1, 1)}
# World identified resources stated in MCS 2024 "World Resources" (tonnes of metal); None where MCS gives no number
RESOURCES = {'Cu': 2.1e9, 'Zn': 1.9e9, 'Pb': 2.0e9, 'Mo': 25.4e6, 'Hg': 6.0e5}
NAMES = {'Congo (Kinshasa)': 'Democratic Republic of the Congo', 'Burma': 'Myanmar', 'Korea, Republic of': 'South Korea',
         'Korea, North': 'North Korea', 'United States': 'United States', 'Kyrgyzstan': 'Kyrgyzstan'}
CAT_NAMES = {'Russia (Buryatia Republic)': 'Russia', 'Kirghistan': 'Kyrgyzstan', 'Congo (Kinshasa)': 'Democratic Republic of the Congo',
             'USA': 'United States', 'Burma': 'Myanmar', 'Korea, South': 'South Korea', 'Republic of Korea': 'South Korea'}


def number(tok):
    tok = tok.replace('e', '').replace('*', '').replace('>', '').strip()
    if not re.fullmatch(r'\d{1,4}(,\d{3})*(\.\d+)?', tok): return None
    first = tok.split(',')[0]
    if len(first) == 4 and ',' in tok: tok = tok[1:]  # a footnote digit glued to the number (MCS "6100,000")
    return float(tok.replace(',', ''))


def mcs_table(code):
    t = (MCS / f'mcs2024-{FILE[code]}.txt').read_text(encoding='utf-8')
    i = t.find('World Mine'); j = t.find('World total', i)
    rows = {}
    for line in t[i:t.find('\n', j) + 1].splitlines()[1:]:
        m = re.match(r'^([A-Z][A-Za-z ,.()\'-]+?)(?:\d{1,2})?\s+((?:[*>]?[\d,e.]+|—|NA|W|XX|\(\d\))(?:\s+(?:[*>]?[\d,e.]+|—|NA|W|XX|\(\d\)))*)\s*$', line.strip())
        if not m: continue
        name = m.group(1).strip()
        toks = m.group(2).split()
        vals = [number(x) for x in toks]
        rows[NAMES.get(name, name)] = vals
    return rows


def main():
    cat = json.loads(CATALOGUE.read_text(encoding='utf-8'))
    by = defaultdict(lambda: defaultdict(float)); world = defaultdict(float)
    for d in cat['deposits'] + cat['areas']:
        c = CAT_NAMES.get(d.get('country'), d.get('country'))
        for m, g in d.get('grades', {}).items():
            if m not in CODES: continue
            t = (d['mt'] - d.get('gossanMt', 0)) * 1e6 * g /(1e6 if UNIT.get(m) == 'g/t' else 100)
            if m == 'W': t *= .793  # WO3 -> W
            by[m][c] += t; world[m] += t
    out = {}; md = []
    for code in CODES:
        rows = mcs_table(code)
        pu, ru = UNITS[code]
        wt = rows.get('World total (rounded)') or next((v for k, v in rows.items() if k.startswith('World total')), [])
        def col(vals, idx):
            return vals[idx] if len(vals) > (idx if idx >= 0 else -idx - 1) else None
        has_res = code not in ('Bi', 'Hg')
        wprod = (col(wt, 1) or 0) * pu
        wres = (col(wt, -1) or 0) * ru if has_res else None
        top = sorted(by[code].items(), key=lambda kv: -kv[1])[:10]
        trows = []
        for c, t in top:
            v = rows.get(c) or rows.get({'United States': 'United States'}.get(c, c)) or []
            prod = (col(v, 1) or 0) * pu if v else None
            res = col(v, -1) if v and has_res and len(v) >= 3 else None
            if res is not None and wres and res * ru > wres:  # footnote digit glued to the reserves (MCS "835,000" = note 8, 35,000)
                res = float(str(int(res))[1:])
            res = res * ru if res is not None else None
            trows.append({'country': c, 'catalogueT': round(t), 'mcsReservesT': res, 'mcsMine2023T': prod,
                          'mineShare2023': round(prod / wprod, 3) if prod and wprod else None})
        out[code] = {'catalogueWorldT': round(world[code]), 'mcsWorldReservesT': wres, 'mcsWorldMine2023T': wprod,
                     'mcsIdentifiedResourcesT': RESOURCES.get(code), 'catalogueOverReserves': round(world[code] / wres, 2) if wres else None,
                     'catalogueYearsOfMine2023': round(world[code] / wprod) if wprod else None, 'topCountries': trows}
        md.append(f"\n**{code}**: catalogue {world[code] / 1e6:,.2f} Mt; MCS reserves {wres / 1e6 if wres else float('nan'):,.2f} Mt; "
                  f"2023 mine output {wprod / 1e6:,.3f} Mt; identified resources {RESOURCES[code] / 1e6:,.0f} Mt" if code in RESOURCES else
                  f"\n**{code}**: catalogue {world[code] / 1e6:,.3f} Mt; MCS reserves {(wres or 0) / 1e6:,.3f} Mt; 2023 mine output {wprod / 1e6:,.4f} Mt")
        md.append('\n| Country | Catalogue (t) | MCS reserves (t) | MCS 2023 mine (t) |\n|---|---:|---:|---:|')
        for r in trows:
            md.append(f"| {r['country']} | {r['catalogueT']:,} | {'' if r['mcsReservesT'] is None else format(round(r['mcsReservesT']), ',')} | "
                      f"{'' if r['mcsMine2023T'] is None else format(round(r['mcsMine2023T']), ',')} |")
    rep = json.loads(REPORT.read_text(encoding='utf-8'))
    rep['validation'] = out
    REPORT.write_text(json.dumps(rep, indent=1, ensure_ascii=False), encoding='utf-8')
    (HERE / '__pycache__').mkdir(exist_ok=True)
    (HERE / '__pycache__' / 'validation.md').write_text('\n'.join(md), encoding='utf-8')
    print('\n'.join(md))


if __name__ == '__main__':
    main()
