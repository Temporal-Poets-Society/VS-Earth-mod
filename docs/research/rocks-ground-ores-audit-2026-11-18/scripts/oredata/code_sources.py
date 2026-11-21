"""Per heat-map code: source mix from the v2 bake manifest merged with heat statistics."""
import csv, json, sys
from pathlib import Path
HERE = Path(__file__).resolve().parent
m = json.load(open(r'C:\VS mods\Earth\docs\research\ore-heatmaps-v2\bake-manifest.json', encoding='utf8'))
stats = {r['code']: r for r in csv.DictReader(open(HERE / 'heat-stats-v2.csv', encoding='utf8'))}

def kind(src):
    s = src.lower()
    if 'occurrences.csv' in s or 'mrds' in s: return 'MRDS'
    if 'best-guess' in s: return 'best-guess'
    if 'catalogue' in s or 'districts' in s or 'provinces' in s or 'coal-evaporites evidence' in s: return 'group catalogue/evidence'
    if '(cells' in s: return 'grid'
    if 'polygon' in s or 'tract' in s or 'formation' in s or 'peatmap' in s: return 'polygons'
    return 'USGS/national specialist'

rows = []
for r in m['resources']:
    c = r['code']; acc = r['acceptedSourceAssociations']
    tot = sum(v for k, v in acc.items())
    by = {}
    for k, v in acc.items(): by[kind(k)] = by.get(kind(k), 0) + v
    s = stats[c]
    rows.append({'code': c, 'records': tot, **{f'{k} %': round(100 * v / tot) for k, v in sorted(by.items())},
                 'pointUS%': round(100 * r['pointRecordUsShare']), 'lit_land_%': s['lit_land_%'],
                 'pot_only_%_of_lit': s['pot_only_share_of_lit_%'], 'obs>=0.5 cells': s['strong_obs_cells_ge_0.5'],
                 'USA+Can obs heat %': s['obs_mass_share_USA+Canada_%'], 'Africa obs heat %': s['obs_mass_share_Africa_%']})
keys = []
for r in rows:
    for k in r:
        if k not in keys: keys.append(k)
with open(HERE / 'code-sources.csv', 'w', newline='', encoding='utf8') as f:
    w = csv.DictWriter(f, fieldnames=keys); w.writeheader(); w.writerows(rows)
for r in rows:
    print(r['code'], {k: v for k, v in r.items() if k != 'code'})
