"""Inventory the fauna and flora pack releases Earth's catalogues are reviewed against (a dated research snapshot).

Writes, in the formats of docs/research/ecology-2026-09-28 (which build-fauna-catalog.py and RegionalFaunaTests read):
  release-inventory.json   one row per pack: id, name, version, game, released, url, archive, changelog (Mod DB API)
  archive-audit.json       manifest, entity/biomes files, DLL names and SHA-256 of each release zip
  entity-spawn-audit.json  code, variant groups and spawn conditions of every entity JSON in the packs
and, with --previous, changes.json: per pack the entity files, entity codes (all variant cross-products), variant states,
worldgen block-patch codes and tree generators added or removed since that snapshot.

The zips are read as data only (nothing is run or installed). Each pack's newest Mod DB release for game 1.22 is used.
  --zips DIR       folder(s) holding the release zips (searched by the Mod DB file name, then by modinfo id/version)
  --api FILE       cached Mod DB API answers ({modid: mod}); --fetch fills it, and downloads missing zips into the first --zips
  --packs LIST     comma-separated mod ids (default: the ids of --previous's release-inventory.json, plus --add)
  --previous DIR   an older snapshot to compare with; its zips are found with --previous-zips (by <modid>.zip or id/version)
Run with the project venv and -I; JSON5 assets need the json5 package (--pylibs DIR adds a folder holding it).
"""
import argparse, hashlib, io, itertools, json, re, sys, time, urllib.request, zipfile
from pathlib import Path

ap = argparse.ArgumentParser()
ap.add_argument('--zips', type=Path, nargs='+', required=True)
ap.add_argument('--api', type=Path, required=True)
ap.add_argument('--out', type=Path, required=True)
ap.add_argument('--packs')
ap.add_argument('--add', default='')
ap.add_argument('--previous', type=Path)
ap.add_argument('--previous-zips', type=Path, nargs='*', default=[])
ap.add_argument('--fetch', action='store_true')
ap.add_argument('--pylibs', type=Path)
args = ap.parse_args()
if args.pylibs: sys.path.insert(0, str(args.pylibs))
import json5

UA = {'User-Agent': 'earth-pack-inventory/1.0'}
def vkey(v):
    m = re.match(r'v?(\d+(?:\.\d+)*)(.*)', v or '')
    if not m: return ((0,) * 4, (0, v or ''))
    nums = (tuple(int(x) for x in m.group(1).split('.')) + (0,) * 4)[:4]
    return (nums, (1, '') if not m.group(2) else (0, m.group(2)))
def text(html): return re.sub(r'\s+', ' ', re.sub(r'<[^>]+>', ' ', html or '')).strip()

previous_rows = json.loads((args.previous/'release-inventory.json').read_text(encoding='utf-8-sig')) if args.previous else []
packs = args.packs.split(',') if args.packs else [r['id'] for r in previous_rows]
packs += [p for p in args.add.split(',') if p and p not in packs]

api = json.loads(args.api.read_text(encoding='utf-8')) if args.api.exists() else {}
for mid in packs:
    if mid in api: continue
    if not args.fetch: raise SystemExit(f'{mid}: not in {args.api}; rerun with --fetch')
    with urllib.request.urlopen(urllib.request.Request(f'https://mods.vintagestory.at/api/mod/{mid}', headers=UA), timeout=60) as r:
        api[mid] = json.loads(r.read().decode('utf-8'))['mod']
    time.sleep(1)
if args.fetch: args.api.write_text(json.dumps(api, indent=1, ensure_ascii=False), encoding='utf-8')

def ci(d, key, default=None):
    """A JSON property as the game reads it (Newtonsoft: case-insensitive names)."""
    if not isinstance(d, dict): return default
    return next((v for k, v in d.items() if k.lower() == key.lower()), default)
def manifest(z):
    return json5.loads(z.read('modinfo.json').decode('utf-8-sig'))
def find_zip(folders, mid, version, filename=None):
    for folder in folders:
        if filename and (folder/filename).exists(): return folder/filename
        if (folder/f'{mid}.zip').exists(): return folder/f'{mid}.zip'
    for folder in folders:
        for p in sorted(folder.glob('*.zip')):
            try:
                with zipfile.ZipFile(p) as z: m = manifest(z)
            except Exception: continue
            if (ci(m, 'modid') or '').lower() == mid and (ci(m, 'version') or '') == version: return p
    return None

def audit(path, mid):
    """archive-audit row and entity-spawn-audit rows of one release zip (the 2026-09-28 formats)."""
    data = path.read_bytes()
    z = zipfile.ZipFile(io.BytesIO(data))
    m = manifest(z)
    names = sorted(n for n in z.namelist() if not n.endswith('/'))
    entity_files = [n for n in names if re.match(r'assets/[^/]+/entities/.+\.json$', n)]
    row = {'id': mid, 'version': ci(m, 'version'), 'type': ci(m, 'type'), 'side': ci(m, 'side'), 'dependencies': ci(m, 'dependencies'),
           'biomesFiles': [n for n in names if n.endswith('biomes.json') and ('/config/' in n or '/patches/' in n)],
           'entityFiles': entity_files, 'dlls': [n.split('/')[-1] for n in names if n.lower().endswith('.dll')],
           'sha256': hashlib.sha256(data).hexdigest().upper()}
    entities = []
    for f in entity_files:
        e = json5.loads(z.read(f).decode('utf-8-sig'))
        server = ci(e, 'server') or {}
        entities.append({'modid': mid, 'file': f, 'code': ci(e, 'code'), 'variantgroups': ci(e, 'variantgroups'),
                         'spawnConditions': ci(server, 'spawnConditions'), 'spawnConditionsByType': ci(server, 'spawnConditionsByType')})
    patches = {}
    for f in (n for n in names if '/patches/worldgen/' in n and n.endswith('.json')):
        for op in json5.loads(z.read(f).decode('utf-8-sig')):
            for c in ((op.get('value') or {}).get('blockCodes') or []) if isinstance(op.get('value'), dict) else []:
                patches.setdefault(c, set()).add(f.split('/')[-1])
    trees = sorted(n.split('/')[-1] for n in names if re.match(r'assets/[^/]+/worldgen/treegen/.+\.json$', n))
    return row, entities, patches, trees, m

def codes(entity):
    out = [entity['code']]
    for g in entity.get('variantgroups') or []:
        if g.get('states'): out = [c + '-' + s for c in out for s in g['states']]
    return set(out)

rows, archive, spawn, changes = [], [], [], {}
for mid in packs:
    mod = api[mid]
    rels = [r for r in mod.get('releases') or [] if any(str(t).lstrip('v').startswith('1.22') for t in r.get('tags') or [])]
    rel = max(rels, key=lambda r: vkey(r.get('modversion')))
    path = find_zip(args.zips, mid, rel['modversion'], rel.get('filename'))
    if path is None:
        if not args.fetch: raise SystemExit(f'{mid} {rel["modversion"]}: no zip in {args.zips}; rerun with --fetch')
        path = args.zips[0]/rel['filename']
        with urllib.request.urlopen(urllib.request.Request(rel['mainfile'], headers=UA), timeout=300) as r: path.write_bytes(r.read())
    row, entities, patches, trees, m = audit(path, mid)
    if row['version'] != rel['modversion'] or ci(m, 'modid') != mid:
        raise SystemExit(f'{path}: modinfo says {ci(m, "modid")} {row["version"]}, Mod DB {mid} {rel["modversion"]}')
    rows.append({'id': mid, 'name': mod.get('name'), 'version': rel['modversion'], 'game': max(rel.get('tags') or [''], key=vkey),
                 'released': rel.get('created'), 'url': f'https://mods.vintagestory.at/{mid}', 'archive': rel.get('mainfile'),
                 'changelog': text(rel.get('changelog'))})
    archive.append(row); spawn += entities
    if not args.previous: continue
    old_row = next((r for r in previous_rows if r['id'] == mid), None)
    old_path = find_zip(args.previous_zips, mid, old_row['version']) if old_row else None
    c = {'old': old_row and old_row['version'], 'new': row['version'], 'zip': path.name, 'sha256': row['sha256']}
    if old_row and old_path is None: raise SystemExit(f'{mid} {old_row["version"]}: previous zip not found')
    if old_path:
        _, old_entities, old_patches, old_trees, _ = audit(old_path, mid)
        of, nf = {e['file'] for e in old_entities}, {e['file'] for e in entities}
        oc = set().union(*[codes(e) for e in old_entities]) if old_entities else set()
        nc = set().union(*[codes(e) for e in entities]) if entities else set()
        states = {}
        for e in entities:
            old = next((o for o in old_entities if o['file'] == e['file']), None)
            for g in e.get('variantgroups') or []:
                og = next((x for x in (old or {}).get('variantgroups') or [] if x.get('code') == g.get('code')), {}) if old else {}
                add = [s for s in g.get('states') or [] if s not in (og.get('states') or [])] if old else []
                rem = [s for s in og.get('states') or [] if s not in (g.get('states') or [])]
                if add or rem: states[e['file'].split('/')[-1] + ' ' + g['code']] = {'added': add, 'removed': rem}
        c.update({'entityFilesAdded': sorted(nf - of), 'entityFilesRemoved': sorted(of - nf), 'variantStates': states,
                  'entityCodesAdded': sorted(nc - oc), 'entityCodesRemoved': sorted(oc - nc),
                  'patchCodesAdded': sorted(set(patches) - set(old_patches)), 'patchCodesRemoved': sorted(set(old_patches) - set(patches)),
                  'treegenAdded': sorted(set(trees) - set(old_trees)), 'treegenRemoved': sorted(set(old_trees) - set(trees))})
    changes[mid] = c
    print(mid, c['old'], '->', c['new'], {k: len(v) for k, v in c.items() if isinstance(v, (list, dict)) and v})

args.out.mkdir(parents=True, exist_ok=True)
(args.out/'release-inventory.json').write_text(json.dumps(rows, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
(args.out/'archive-audit.json').write_text(json.dumps(archive, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
(args.out/'entity-spawn-audit.json').write_text(json.dumps(spawn, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
if args.previous: (args.out/'changes.json').write_text(json.dumps(changes, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
print(f'{len(rows)} packs, {len(spawn)} entity files')
