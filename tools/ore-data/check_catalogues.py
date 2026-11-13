"""Checks the ore deposit catalogues together (decision 0065): schema, group ownership of layer codes, deposit codes,
layer classes, and duplicates across groups. Writes docs/research/ore-deposits-2026-11-08/catalogue-check.json and
prints a summary. Exit code 1 on errors.

Run: C:\\VS mods\\Earth\\.venv\\Scripts\\python.exe -I tools/ore-data/check_catalogues.py
"""
import csv, json, math, re, sys
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CAT = ROOT / 'assets/earth/config/ore-deposits-v1'
OUT = ROOT / 'docs/research/ore-deposits-2026-11-08/catalogue-check.json'
GROUPS = {
    'coal-evaporites': {'C_coal', 'NaCl', 'K', 'gypsum', 'B', 'S', 'P', 'N', 'alum'},
    'base-metals': {'Cu', 'Sn', 'Pb', 'Zn', 'Ag', 'Bi', 'Hg', 'W', 'Mo', 'Sb'},
    'iron-ferro': {'Fe', 'Mn', 'Cr', 'Ni', 'Ti', 'V', 'Pt', 'Al'},
    'precious-gems': {'Au', 'diamond', 'corundum', 'emerald', 'olivine', 'garnet', 'lapis', 'topaz', 'tourmaline', 'beryl',
                      'spinel', 'C_graphite', 'F', 'SiO2', 'U'},
}
SHAPES = {'stock', 'lens', 'veins', 'blanket', 'dome'}
CLASSES = {'alluvial', 'outwash', 'peat', 'laterite', 'saprolite', 'playa', 'deep-cover', 'cover', 'bed', 'basement'}
UNITS = {'%', 'g/t', 'ct/t', 'kg/t', None}


def deposit_codes():
    codes = set()
    with open(ROOT / 'docs/research/remaining-ores-2026-09-29/game-mineral-scope.csv', encoding='utf-8') as f:
        for r in csv.DictReader(f): codes.add(r['deposit_code'])
    codes.update({'earth-bauxite', 'earth-gypsum', 'gold', 'silver', 'quartz', 'nativeplatinum', 'sperrylite', 'vanadinite'})
    for name in ('earth-gems.json', 'earth-ores.json', 'earth-layers.json'):
        text = (ROOT / 'assets/earth/worldgen/deposits' / name).read_text(encoding='utf-8')
        codes.update(re.findall(r'"code"\s*:\s*"([a-z0-9-]+)"', text))
    return codes


def km(a, b):
    la1, lo1, la2, lo2 = map(math.radians, (a[0], a[1], b[0], b[1]))
    h = math.sin((la2 - la1) / 2) ** 2 + math.cos(la1) * math.cos(la2) * math.sin((lo2 - lo1) / 2) ** 2
    return 12742 * math.asin(min(1, math.sqrt(h)))


def norm(name):
    return re.sub(r'[^a-z0-9]', '', (name or '').casefold().replace('mine', '').replace('deposit', ''))


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    known = deposit_codes()
    layer_of = {}
    with open(ROOT / 'docs/research/remaining-ores-2026-09-29/game-mineral-scope.csv', encoding='utf-8') as f:
        for r in csv.DictReader(f): layer_of.setdefault(r['deposit_code'], r['commodity_layer'])
    errors, warnings = [], []
    bodies = []  # (group, kind, entry)
    totals = defaultdict(lambda: {'deposits': 0, 'areas': 0, 'mt': 0.0, 'contained_t': 0.0})
    seen_bands = {}
    for path in sorted(CAT.glob('*.json')):
        group = path.stem
        doc = json.loads(path.read_text(encoding='utf-8'))
        own = GROUPS.get(group)
        if own is None: errors.append(f'{path.name}: unknown group'); continue
        if doc.get('version') != 1: errors.append(f'{path.name}: version {doc.get("version")}')
        srcs = {s['id'] for s in doc.get('sources', [])}
        for s in doc.get('sources', []):
            if not s.get('licence'): errors.append(f'{group}: source {s.get("id")} has no licence')
        bands = doc.get('gradeBands', {})
        for code, b in bands.items():
            if not (b.get('medium', 0) <= b.get('rich', 0) <= b.get('bountiful', 0)): errors.append(f'{group}: gradeBands {code} not ascending')
            # the runtime keeps the first file's band (files load in name order); a repeated band must be the same
            key = tuple(b.get(k) for k in ('unit', 'medium', 'rich', 'bountiful'))
            if seen_bands.setdefault(code, (group, key))[1] != key:
                errors.append(f'{group}: gradeBands {code} differs from {seen_bands[code][0]}')
        ids = Counter()
        for kind in ('deposits', 'areas'):
            for e in doc.get(kind, []):
                ids[e.get('id')] += 1
                where = f'{group}:{e.get("id")}'
                layer = e.get('layer')
                if layer not in own: errors.append(f'{where}: layer {layer} not owned by {group}')
                if e.get('src') not in srcs: errors.append(f'{where}: src {e.get("src")} not in sources')
                for c in e.get('layers') or []:
                    if c not in CLASSES: errors.append(f'{where}: layer class {c}')
                mt = e.get('mt')
                t = totals[layer]; t[kind] += 1
                if kind == 'deposits':
                    if not (isinstance(mt, (int, float)) and mt > 0): errors.append(f'{where}: mt {mt}'); continue
                    if e.get('shape') not in SHAPES: errors.append(f'{where}: shape {e.get("shape")}')
                    lat, lon = e.get('lat'), e.get('lon')
                    if not (isinstance(lat, (int, float)) and -90 <= lat <= 90 and isinstance(lon, (int, float)) and -180 <= lon <= 180):
                        errors.append(f'{where}: position {lat},{lon}'); continue
                    ores = e.get('ores') or []
                    share = sum(o.get('share', 0) for o in ores)
                    if not ores or abs(share - 1) > .02: errors.append(f'{where}: ore shares sum {share:.3f}')
                    for o in ores:
                        if o.get('code') not in known: errors.append(f'{where}: deposit code {o.get("code")}')
                        if o.get('zone') not in (None, 'top', 'bottom'): errors.append(f'{where}: zone {o.get("zone")}')
                    top, base = e.get('topM'), e.get('baseM')
                    if top is not None and base is not None and not (0 <= top < base): errors.append(f'{where}: topM {top} baseM {base}')
                    if e.get('unit') not in UNITS: errors.append(f'{where}: unit {e.get("unit")}')
                    t['mt'] += mt
                    g = e.get('grade')
                    if isinstance(g, (int, float)) and g > 0:
                        unit = e.get('unit')
                        # the grade is the whole body's, unless the layer's own ores are a minor share (a salt dome's
                        # sulfur cap): then it is that share's grade
                        own_share = sum(o.get('share', 0) for o in ores if layer_of.get(o.get('code')) == layer)
                        f = own_share if 0 < own_share < .5 else 1
                        t['contained_t'] += f * mt * 1e6 * (g / 100 if unit == '%' else g / 1e6 if unit == 'g/t' else 0)
                    bodies.append((group, e))
                else:
                    geom = e.get('geometry') or {}
                    if geom.get('type') not in ('Polygon', 'MultiPolygon'): errors.append(f'{where}: geometry {geom.get("type")}')
                    beds = e.get('beds') or []
                    if not beds: errors.append(f'{where}: no beds')
                    for b in beds:
                        if b.get('code') not in known: errors.append(f'{where}: bed code {b.get("code")}')
                        if not (0 <= (b.get('cover') or 0) <= 1) or not (b.get('thickM') or 0) > 0: errors.append(f'{where}: bed cover/thickness {b}')
                    if isinstance(mt, (int, float)): t['mt'] += mt
        for i, n in ids.items():
            if n > 1: errors.append(f'{group}: id {i} used {n} times')
    # duplicates across groups: same normalised name within 50 km, or any two bodies within 3 km
    dups = []
    by_cell = defaultdict(list)
    for g, e in bodies: by_cell[(round(e['lat']), round(e['lon']))].append((g, e))
    for (cy, cx), items in by_cell.items():
        near = [x for dy in (-1, 0, 1) for dx in (-1, 0, 1) for x in by_cell.get((cy + dy, cx + dx), [])]
        for g, e in items:
            for g2, e2 in near:
                if g2 <= g or g2 == g: continue
                d = km((e['lat'], e['lon']), (e2['lat'], e2['lon']))
                same_name = norm(e.get('name')) and norm(e.get('name')) == norm(e2.get('name'))
                if (same_name and d < 50) or d < 3:
                    dups.append({'a': f'{g}:{e["id"]}', 'aName': e.get('name'), 'aLayer': e['layer'], 'aMt': e['mt'],
                                 'b': f'{g2}:{e2["id"]}', 'bName': e2.get('name'), 'bLayer': e2['layer'], 'bMt': e2['mt'], 'km': round(d, 1)})
    report = {'errors': errors, 'warnings': warnings, 'crossGroupPairs': dups,
              'totals': {k: {**v, 'mt': round(v['mt'], 1), 'contained_t': round(v['contained_t'])} for k, v in sorted(totals.items())}}
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(report, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
    print(f'{len(errors)} errors, {len(dups)} cross-group pairs (same name < 50 km or any < 3 km)')
    for x in errors[:40]: print('  E', x)
    for p in dups[:60]: print('  D', p['a'], p['aName'], p['aLayer'], p['aMt'], '|', p['b'], p['bName'], p['bLayer'], p['bMt'], '|', p['km'], 'km')
    for k, v in report['totals'].items(): print(f'  {k:10s} deposits {v["deposits"]:5d} areas {v["areas"]:3d}  Mt {v["mt"]:>12,.0f}  contained t {v["contained_t"]:>14,.0f}')
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main())
