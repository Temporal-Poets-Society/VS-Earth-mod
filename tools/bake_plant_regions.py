"""Bake regional ground-plant rules (assets/earth/config/regionalplants.json).

Inputs:
  --mods     folder with the installed regional plant pack zips (read only; default %APPDATA%/VintagestoryData/Mods)
  --sources  folder with wcvp.zip and pylibs/json5 (default obj/ecology-inspect, as for bake_tree_regions.py)
  tools/plant-taxa.json                    manual species identities, pack floristic regions and exceptions
  assets/earth/config/treeregions.json  WGSRPD level-3 boundaries baked by bake_tree_regions.py (codes only)

Every block patch the packs add (patches/worldgen/*.json, "addmerge" into game:worldgen/blockpatches/*.json) is
inventoried. Each block code is mapped to exactly one species token of plant-taxa.json; the token's taxon is resolved
in WCVP (accepted names first, synonyms to their accepted taxon) and its native, non-doubtful level-3 regions are
used, mirroring bake_tree_regions.py. Tokens marked range=packRegion use the pack's floristic region; range=climateOnly
(and every patch from a climateOnlyFiles file) keeps the pack's own climate rules. Nothing here is needed at runtime
except the output JSON. Run with the project venv:  .venv\\Scripts\\python.exe tools\\bake_plant_regions.py
--ranges-from FILE takes the WCVP ranges from an earlier output (assets/earth/config/regionalplants.json) instead of
wcvp.zip, for a pack update that adds no species: every WCVP-ranged species must be in that file, and its WCVP source and
hash are carried over, so the ranges stay those of the reviewed WCVP (EA-0156).
"""
import argparse, csv, glob, hashlib, io, json, os, re, sys, zipfile
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser()
p.add_argument('--sources', type=Path, default=ROOT/'obj/ecology-inspect')
p.add_argument('--mods', type=Path, default=Path(os.environ.get('APPDATA', ''))/'VintagestoryData/Mods')
p.add_argument('--check', action='store_true', help='verify only; do not write the output')
p.add_argument('--ranges-from', type=Path, help='take the WCVP ranges from an earlier regionalplants.json instead of wcvp.zip')
args = p.parse_args()
sys.path.insert(0, str(args.sources/'pylibs'))
import json5  # provider assets use JSON5 features (comments, trailing commas)

WATER_BLOCKS = {'waterlily', 'aquatic', 'aquaticplant'}

def compact(s): return re.sub('[^a-z]', '', s.lower())
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def fail(msg): raise SystemExit('ERROR: ' + msg)

manual = json.loads((ROOT/'tools/plant-taxa.json').read_text(encoding='utf-8'))
region_codes = {r['code'] for r in json.loads((ROOT/'assets/earth/config/treeregions.json').read_text(encoding='utf-8'))['regions']}
for name, pack in manual['packs'].items():
    if pack['regions'] is not None and not set(pack['regions']) <= region_codes:
        fail(f'{name}: unknown region codes {set(pack["regions"]) - region_codes}')
for name, s in manual['regionSets'].items():
    if not set(s['regions']) <= region_codes: fail(f'region set {name}: unknown codes {set(s["regions"]) - region_codes}')

species = {}
for s in manual['species']:
    key = (s['provider'], s['token'])
    if key in species: fail(f'duplicate species token {key}')
    if s.get('range', 'wcvp') not in ('wcvp', 'packRegion', 'climateOnly'): fail(f'{key}: unknown range {s.get("range")}')
    species[key] = s

# ---------------------------------------------------------------- provider inventory
providers, inventory = [], []
for name, pack in manual['packs'].items():
    zips = sorted(glob.glob(str(args.mods/f'{name}-*.zip')))
    if not zips: fail(f'{name}: no zip in {args.mods}')
    path = zips[-1]
    z = zipfile.ZipFile(path)
    info = json5.loads(z.read('modinfo.json').decode('utf-8-sig'))
    version = info.get('version') or info.get('Version')
    digest = sha(path)
    if version != pack['version'] or digest != pack['sha256']:
        print(f'WARNING: {name} {version} ({digest[:12]}) differs from the reviewed {pack["version"]} ({pack["sha256"][:12]})')
    count = 0
    for f in sorted(n for n in z.namelist() if '/patches/worldgen/' in n and n.endswith('.json')):
        for i, op in enumerate(json5.loads(z.read(f).decode('utf-8-sig'))):
            target = op.get('file', '')
            if op.get('op') != 'addmerge' or op.get('path') != '/-' or not target.startswith('game:worldgen/blockpatches/'):
                fail(f'{name} {f}#{i}: unexpected patch operation {op.get("op")} {op.get("path")} {target}')
            v = op['value']
            codes = v.get('blockCodes') or []
            if not codes: fail(f'{name} {f}#{i}: no block codes')
            for c in codes:
                if ':' not in c or c.split(':')[0] != name or '*' in c or '{' in c:
                    fail(f'{name} {f}#{i}: unsupported block code {c} (expected an exact {name}: code)')
            inventory.append({'provider': name, 'file': f.split('/')[-1], 'index': i, 'target': target.split('/')[-1],
                              'comment': (v.get('comment') or '').strip(), 'placement': v.get('placement', 'OnSurface'),
                              'codes': codes})
            count += 1
    providers.append({'id': name, 'version': version, 'sha256': digest, 'patches': count,
                      'floristicRegion': pack['floristicRegion']})

# ---------------------------------------------------------------- block code -> species token
code_token = {}
token_codes, token_files, token_water = {}, {}, {}
for e in inventory:
    for c in e['codes']:
        provider, path = c.split(':')
        parts = path.split('-')
        hits = [t for t in parts[1:] if (provider, t) in species]
        if len(hits) != 1: fail(f'{c}: expected exactly one species token of plant-taxa.json, found {hits}')
        key = (provider, hits[0])
        code_token[c] = key
        token_codes.setdefault(key, set()).add(c)
        token_files.setdefault(key, set()).add(e['file'])
        # Aquatic blocks may be placed in open water outside the botanical polygons (lakes, sea surface), so their
        # runtime region lookup falls back to the nearest botanical country. Water placements of other blocks are
        # detected from the live patch at runtime.
        if parts[0] in WATER_BLOCKS: token_water[key] = True
unused = set(species) - set(token_codes)
if unused: fail(f'plant-taxa.json tokens not used by any installed patch: {sorted(unused)}')
for key, files in token_files.items():
    crop = files & set(manual['climateOnlyFiles'])
    if crop and species[key].get('range') != 'climateOnly':
        fail(f'{key}: used in {crop} (wild crops) but not marked climateOnly')
    if files - set(manual['climateOnlyFiles']) and species[key].get('range') == 'climateOnly' and 'not in WCVP' not in species[key].get('note', ''):
        fail(f'{key}: climateOnly outside crop files needs an explanatory note')
for t, s in species.items():
    if compact(s['taxon']) != t[1] and 'note' not in s: fail(f'{t}: taxon "{s["taxon"]}" differs from the code token without a note')

# ---------------------------------------------------------------- WCVP native ranges
wanted = {compact(s['taxon']) for s in species.values() if s.get('range', 'wcvp') == 'wcvp'}
taxa = {}
if args.ranges_from:
    earlier = json.loads(args.ranges_from.read_text(encoding='utf-8'))
    wcvp_source = earlier['sources']['wcvp']
    for r in earlier['rules']:
        if r['basis'] == 'species':
            taxa[compact(r['packTaxon'])] = {'name': r['taxa'][0], 'id': r['taxonIds'][0], 'status': None, 'area': r['wcvpArea'],
                                             'regions': r['regions']}
    missing = wanted - taxa.keys()
    if missing: fail(f'species without a WCVP range in {args.ranges_from} (bake with wcvp.zip): ' + ', '.join(sorted(missing)))
    taxa = {k: v for k, v in taxa.items() if k in wanted}
else:
    wcvp_source = {'url': 'https://sftp.kew.org/pub/data-repositories/WCVP/wcvp.zip', 'sha256': sha(args.sources/'wcvp.zip'), 'license': 'CC BY 3.0'}
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
    if missing: fail('taxa without an accepted or synonym WCVP name: ' + ', '.join(sorted(missing)))
    accepted_ids = {r['accepted_plant_name_id'] or r['plant_name_id'] for r in found.values()}
    accepted = {}
    for r in rows('wcvp_names.csv'):
        if r['plant_name_id'] in accepted_ids: accepted[r['plant_name_id']] = r
    ranges = {k: set() for k in accepted_ids}
    for r in rows('wcvp_distribution.csv'):
        if r['plant_name_id'] in ranges and r['introduced'] == '0' and r['location_doubtful'] == '0':
            ranges[r['plant_name_id']].add(r['area_code_l3'])
    for key, original in found.items():
        ident = original['accepted_plant_name_id'] or original['plant_name_id']
        regions = ranges[ident] & region_codes
        if not regions: fail('no native range: ' + accepted[ident]['taxon_name'])
        if ranges[ident] - region_codes: print('WARNING: dropped unknown region codes', ranges[ident] - region_codes)
        taxa[key] = {'name': accepted[ident]['taxon_name'], 'id': ident, 'status': original['taxon_status'],
                     'area': accepted[ident]['geographic_area'], 'regions': sorted(regions)}

# ---------------------------------------------------------------- rules
rules = []
rule_of = {}
for key in sorted(token_codes):
    s = species[key]
    pack = manual['packs'][key[0]]
    kind = s.get('range', 'wcvp')
    rule = {'id': f'{key[0]}/{key[1]}', 'provider': key[0], 'basis': {'wcvp': 'species', 'packRegion': 'packRegion', 'climateOnly': 'climateOnly'}[kind],
            'name': s['name'], 'packTaxon': s['taxon']}
    if kind == 'wcvp':
        t = taxa[compact(s['taxon'])]
        rule.update({'taxa': [t['name']], 'taxonIds': [t['id']], 'wcvpArea': t['area'], 'regions': t['regions']})
    elif kind == 'packRegion':
        if pack['regions'] is None: fail(f'{key}: pack has no floristic region')
        regions = set(pack['regions'])
        if s.get('restrictTo'): regions &= set(manual['regionSets'][s['restrictTo']]['regions'])
        rule.update({'taxa': [s['taxon']], 'taxonIds': [], 'regions': sorted(regions), 'packRegion': pack['floristicRegion']})
        if s.get('restrictTo'): rule['restrictTo'] = s['restrictTo']
    else:
        rule.update({'taxa': [s['taxon']], 'taxonIds': [], 'regions': [],
                     'reason': 'crop' if token_files[key] <= set(manual['climateOnlyFiles']) else 'noRange'})
    rule['water'] = bool(token_water.get(key))
    rule['codes'] = sorted(token_codes[key])
    if s.get('note'): rule['note'] = s['note']
    rules.append(rule)
    rule_of[key] = rule
for e in inventory:
    ids = sorted({rule_of[code_token[c]]['id'] for c in e['codes']})
    bases = {rule_of[code_token[c]]['basis'] for c in e['codes']}
    e['rules'] = ids
    e['basis'] = 'climateOnly' if 'climateOnly' in bases else ('species' if bases == {'species'} else 'packRegion')

stats = Counter(e['basis'] for e in inventory)
summary = {'patches': len(inventory), 'bySpecies': stats['species'], 'byPackRegion': stats['packRegion'],
           'climateOnly': stats['climateOnly'], 'rules': len(rules),
           'climateOnlyCrop': sum(1 for e in inventory if e['basis'] == 'climateOnly' and e['file'] in manual['climateOnlyFiles'])}
output = {
    'version': 1,
    'about': 'Earth regional ground plants: native ranges for block patches of regional plant packs. Generated by tools/bake_plant_regions.py from tools/plant-taxa.json; do not edit.',
    'sources': {'wcvp': wcvp_source,
                'boundaries': 'treeregions.json (WGSRPD level 3, see TREE-DATA-ATTRIBUTION.txt)',
                'mapping': 'tools/plant-taxa.json', 'mappingSha256': sha(ROOT/'tools/plant-taxa.json')},
    'summary': summary,
    'providers': providers,
    'packRegions': {k: v['regions'] for k, v in manual['packs'].items() if v['regions'] is not None},
    'rules': rules,
    'inventory': inventory,
}
if not args.check:
    out = ROOT/'assets/earth/config/regionalplants.json'
    lines = ['{']
    keys = list(output)
    for i, k in enumerate(keys):
        end = ',' if i < len(keys)-1 else ''
        v = output[k]
        if isinstance(v, list) and v and isinstance(v[0], dict):
            lines.append(f' "{k}": [')
            lines += ['  ' + json.dumps(x, ensure_ascii=False, separators=(',', ':')) + (',' if j < len(v)-1 else '') for j, x in enumerate(v)]
            lines.append(' ]' + end)
        else:
            lines.append(f' "{k}": ' + json.dumps(v, ensure_ascii=False, separators=(',', ':')) + end)
    lines.append('}')
    out.write_text('\n'.join(lines) + '\n', encoding='utf-8', newline='\n')
    json.loads(out.read_text(encoding='utf-8'))
print(f'{summary["patches"]} pack patches: {summary["bySpecies"]} by species range, {summary["byPackRegion"]} by pack region, '
      f'{summary["climateOnly"]} climate-only ({summary["climateOnlyCrop"]} wild crops); {len(rules)} rules.')
for prov in providers:
    c = Counter(e['basis'] for e in inventory if e['provider'] == prov['id'])
    print(f'  {prov["id"]} {prov["version"]}: {prov["patches"]} patches ({dict(c)})')
for key, t in sorted(taxa.items()):
    if t['status'] not in ('Accepted', None): print(f'  synonym resolved: {key} -> {t["name"]}')
