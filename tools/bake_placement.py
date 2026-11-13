"""
Bakes Earth's placement catalogue (decisions 0049, 0047): assets/earth/config/placement.json.

Input: tools/placement-taxa.json (curated taxa, their 1318 introductions with sources, and the mappings of provider
assets to taxa). Each taxon's 1318 range is its WCVP native range (accepted name or synonym resolved to the accepted
taxon; native, non-doubtful level-3 records) plus its introductions, minus its exclusions. L1:n and L2:n expand
through the TDWG hierarchy. Every region must exist in assets/earth/config/treeregions.json.

Climate envelopes follow the regional-tree method (tools/bake_tree_regions.py): weighted percentiles of real yearly
mean surface temperature and VS rainfall over the taxon's 1318 regions in tools/tree-region-climate.json, with margins.
Manual 'climate' limits are kept with their reason.

  python tools/bake_placement.py --sources obj/ecology-inspect [--check]

--sources holds wcvp.zip and tdwg-level3.geojson (as for bake_plant_regions.py). --check compares with the existing
output instead of writing it.
"""
import argparse, csv, hashlib, io, json, math, re, sys, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
p = argparse.ArgumentParser()
p.add_argument('--sources', type=Path, default=ROOT/'obj/ecology-inspect')
p.add_argument('--climate', type=Path, default=ROOT/'tools/tree-region-climate.json')
p.add_argument('--out', type=Path, default=ROOT/'assets/earth/config/placement.json')
p.add_argument('--check', action='store_true')
args = p.parse_args()

def fail(msg): raise SystemExit('ERROR: ' + msg)
def compact(s): return re.sub('[^a-z]', '', s.lower())
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()

taxa_path = ROOT/'tools/placement-taxa.json'
manual = json.loads(taxa_path.read_text(encoding='utf-8'))
if manual.get('version') != 1: fail('placement-taxa.json: unsupported version')
region_codes = {r['code'] for r in json.loads((ROOT/'assets/earth/config/treeregions.json').read_text(encoding='utf-8'))['regions']}

# ---------------------------------------------------------------- TDWG hierarchy
geo_path = args.sources/'tdwg-level3.geojson'
level = {}
for f in json.loads(geo_path.read_text(encoding='utf-8'))['features']:
    pr = f['properties']
    level[pr['LEVEL3_COD']] = (int(pr['LEVEL1_COD']), int(pr['LEVEL2_COD']))
def expand(specs, where):
    out = set()
    for s in specs:
        if s.startswith('L1:') or s.startswith('L2:'):
            n = int(s[3:])
            members = {c for c, (l1, l2) in level.items() if (l1 if s.startswith('L1:') else l2) == n}
            if not members: fail(f'{where}: {s} matches no level-3 region')
            out |= members
        elif s in level: out.add(s)
        else: fail(f'{where}: unknown region {s}')
    unknown = out - region_codes
    if unknown: print(f'WARNING: {where}: regions without boundaries in treeregions.json dropped: {sorted(unknown)}')
    return out & region_codes

# ---------------------------------------------------------------- WCVP native ranges
wanted = {compact(n) for t in manual['taxa'].values() for n in t.get('wcvp', [])}
archive = zipfile.ZipFile(args.sources/'wcvp.zip')
def rows(filename):
    with io.TextIOWrapper(archive.open(filename), encoding='utf-8', errors='replace', newline='') as stream:
        yield from csv.DictReader(stream, delimiter='|')
STATUS = {'Accepted': 0, 'Synonym': 1}
found = {}
for r in rows('wcvp_names.csv'):
    key = compact(r['taxon_name'])
    if key in wanted and r['taxon_status'] in STATUS:
        old = found.get(key)
        if old is None or STATUS[r['taxon_status']] < STATUS[old['taxon_status']]: found[key] = r
missing = wanted - found.keys()
if missing: fail('names without an accepted or synonym WCVP entry: ' + ', '.join(sorted(missing)))
accepted_ids = {r['accepted_plant_name_id'] or r['plant_name_id'] for r in found.values()}
accepted = {r['plant_name_id']: r for r in rows('wcvp_names.csv') if r['plant_name_id'] in accepted_ids}
native = {k: set() for k in accepted_ids}
for r in rows('wcvp_distribution.csv'):
    if r['plant_name_id'] in native and r['introduced'] == '0' and r['location_doubtful'] == '0':
        native[r['plant_name_id']].add(r['area_code_l3'])

# ---------------------------------------------------------------- climate envelopes
climate = json.loads(args.climate.read_text(encoding='utf-8'))
settings = manual['envelope']
def histogram(codes, key):
    total = {}
    for code in codes:
        c = climate['regions'].get(code)
        if not c: continue
        for b, rel in c[key]:
            total[b] = total.get(b, 0) + rel * 1e-6 * c['w']
    return total
def percentile(hist, q, step, origin):
    items = sorted(hist.items())
    s = sum(w for _, w in items)
    acc = 0
    for b, w in items:
        if acc + w >= q * s: return origin + (b + (q * s - acc) / w) * step
        acc += w
    return origin + (items[-1][0] + 1) * step
def envelope(codes, where):
    th, rh = histogram(codes, 't'), histogram(codes, 'r')
    if not th: fail(f'{where}: no climate samples in its regions')
    lo, hi = settings['lowerPercentile'] / 100, settings['upperPercentile'] / 100
    t0 = percentile(th, lo, climate['tempStep'], climate['tempMin']) - settings['temperatureMargin']
    t1 = percentile(th, hi, climate['tempStep'], climate['tempMin']) + settings['temperatureMargin']
    if t1 - t0 < settings['minTemperatureWidth']:
        mid = percentile(th, .5, climate['tempStep'], climate['tempMin'])
        t0, t1 = mid - settings['minTemperatureWidth'] / 2, mid + settings['minTemperatureWidth'] / 2
    r0 = percentile(rh, lo, climate['rainStep'], 0) - settings['rainMargin']
    r1 = percentile(rh, hi, climate['rainStep'], 0) + settings['rainMargin']
    if r1 - r0 < settings['minRainWidth']:
        mid = percentile(rh, .5, climate['rainStep'], 0)
        r0, r1 = mid - settings['minRainWidth'] / 2, mid + settings['minRainWidth'] / 2
    return {'minTemp': round(math.floor(t0 * 2) / 2, 1), 'maxTemp': round(math.ceil(t1 * 2) / 2, 1),
            'minRain': max(0, math.floor(r0)), 'maxRain': 255 if r1 >= 250 else math.ceil(r1)}

# ---------------------------------------------------------------- taxa
KINDS = {'fruitTree', 'crop', 'berry', 'wild'}
taxa = []
for tid, t in manual['taxa'].items():
    if t.get('kind') not in KINDS: fail(f'{tid}: unknown kind {t.get("kind")}')
    names, ids, nat = [], [], set()
    for n in t.get('wcvp', []):
        r = found[compact(n)]
        ident = r['accepted_plant_name_id'] or r['plant_name_id']
        names.append(accepted[ident]['taxon_name']); ids.append(ident)
        regions = native[ident] & region_codes
        if not regions: fail(f'{tid}: {n} has no native range in WCVP')
        nat |= regions
    intro = []
    for a in t.get('add', []):
        if a.get('source') not in manual['sources']: fail(f'{tid}: introduction without a listed source')
        if not a.get('period'): fail(f'{tid}: introduction without a period')
        intro.append({'regions': sorted(expand(a['regions'], tid)), 'period': a['period'], 'source': a['source']})
    regions = nat | {c for a in intro for c in a['regions']}
    regions -= expand(t.get('exclude', []), tid)
    if not regions: fail(f'{tid}: empty 1318 range')
    if not t.get('wcvp') and not t.get('add'): fail(f'{tid}: neither a WCVP name nor introductions')
    env = envelope(regions, tid)
    manual_climate = t.get('climate')
    if manual_climate:
        if not manual_climate.get('reason'): fail(f'{tid}: manual climate limit without a reason')
        env.update({k: v for k, v in manual_climate.items()})
    env['basis'] = '1318-range' + (', manual limit' if manual_climate else '')
    entry = {'id': tid, 'name': t['name'], 'kind': t['kind'], 'scientific': names, 'wcvpIds': ids,
             'native': sorted(nat), 'regions': sorted(regions), 'introductions': intro, 'climate': env}
    if t.get('note'): entry['note'] = t['note']
    # ranges step 5c: annuals that bloom on dry land after rain and die back when it dries
    if t.get('ephemeral'): entry['ephemeral'] = True
    taxa.append(entry)

ids = {t['id'] for t in taxa}
seen = set()
for m in manual['mappings']:
    if m['taxon'] not in ids: fail(f'mapping to unknown taxon {m["taxon"]}')
    if m['path'] not in ('fruitTree', 'blockPatch'): fail(f'unknown mapping path {m["path"]}')
    key = (m['provider'], m['path'], m.get('key') or m.get('prefix'))
    if None in key or key in seen: fail(f'duplicate or incomplete mapping {key}')
    seen.add(key)
unused = ids - {m['taxon'] for m in manual['mappings']}
if unused: fail('taxa without a mapping: ' + ', '.join(sorted(unused)))

out = {
    'version': 1, 'profile': manual['profile'],
    'inputs': {'taxa': sha(taxa_path), 'wcvp': sha(args.sources/'wcvp.zip'), 'tdwg': sha(geo_path),
               'regionClimate': sha(args.climate), 'climateSha256': climate['climateSha256'], 'envelope': settings},
    'sources': manual['sources'], 'taxa': taxa, 'mappings': manual['mappings'],
    'notOrganisms': manual['notOrganisms'], 'controlledElsewhere': manual['controlledElsewhere'], 'fauna': manual['fauna'],
}
text = json.dumps(out, ensure_ascii=False, indent=1) + '\n'
if args.check:
    if not args.out.exists() or args.out.read_text(encoding='utf-8') != text: fail(f'{args.out} is out of date')
    print('placement.json is up to date')
else:
    args.out.write_text(text, encoding='utf-8', newline='\n')
    print(f'Baked {len(taxa)} taxa, {len(manual["mappings"])} mappings into {args.out.relative_to(ROOT)}')
for t in taxa:
    c = t['climate']
    print(f"  {t['id']:24} {len(t['native']):3} native + intro -> {len(t['regions']):3} regions; T {c['minTemp']}..{c['maxTemp']}, rain {c['minRain']}..{c['maxRain']}"
          + (f", coldest month >= {c['minColdMonth']}" if 'minColdMonth' in c else ''))
