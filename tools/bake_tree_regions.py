"""Bake offline tree ranges from Kew WCVP and TDWG WGSRPD level 3 (regional tree rules version 2).

Inputs in --sources: wcvp.zip, tdwg-level3.geojson (see docs/research/tree-range-provenance.json).
The inspected supplier inventory is checked in at docs/research/tree-generator-inventory.json.
Requires shapely (only this build tool; never required by the mod).

Scientific identities, analogue rules, WCVP error exclusions and explicit habitat corrections
live in tools/tree-taxa.json.

Earth climate envelopes: every species rule that is not a vanilla forest-type species ("group")
gets an envelope of real yearly mean temperature (°C at the surface) and VS rainfall, taken
from its native botanical countries in tools/tree-region-climate.json (written from Earth's
baked climate by `dotnet run --project tests/RegionalTreesTests -c Release -- . --region-climate`).
Envelope = weighted percentiles (settings in tree-taxa.json "envelope") with margins and a
minimum width. Explicit values in tree-taxa.json win.
"""
import argparse, csv, hashlib, io, json, math, re, sys, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser()
p.add_argument('--sources', type=Path, default=ROOT/'obj/ecology-inspect')
p.add_argument('--climate', type=Path, default=ROOT/'tools/tree-region-climate.json')
p.add_argument('--no-envelopes', action='store_true', help='bootstrap: write rules without Earth climate envelopes')
p.add_argument('--chorology', type=Path, default=ROOT.parents[2]/'rawdata/caudullo-chorology/chorological_maps_dataset' if ROOT.parent.name == 'worktrees' else ROOT/'rawdata/caudullo-chorology/chorological_maps_dataset',
               help='Caudullo et al. 2017 chorological maps (species folders); missing: no range masks')
p.add_argument('--eive', type=Path, default=ROOT.parents[2]/'rawdata/eive/EIVE_Paper_1.0_SM_08.xlsx' if ROOT.parent.name == 'worktrees' else ROOT/'rawdata/eive/EIVE_Paper_1.0_SM_08.xlsx',
               help='EIVE 1.0 supplementary material 8 (niche position and width of M, N, R); missing: no soil indicators')
args = p.parse_args()
sys.path.insert(0, str(args.sources/'pylibs'))
from shapely.geometry import shape, mapping

RULES_VERSION = 2
def compact(s): return re.sub('[^a-z]', '', s.lower())
def write(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, separators=(',', ':'), ensure_ascii=False)+'\n', encoding='utf-8')
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()

manual = json.loads((ROOT/'tools/tree-taxa.json').read_text(encoding='utf-8-sig'))
inventory_path = ROOT/'docs/research/tree-generator-inventory.json'
inventory = json.loads(inventory_path.read_text())
RULE_FIELDS = ('note', 'cultivated', 'water', 'group', 'forestType', 'weightScale', 'minTemperature', 'maxTemperature', 'minRain', 'maxRain',
               'minColdMonth', 'maxColdMonth', 'minWarmMonth', 'maxWarmMonth', 'minElevation', 'maxElevation', 'envelopes', 'ignoreHeight')
entries = []
for provider in ('vanilla', 'wildcrafttree'):
    for item in manual[provider]:
        entries.append({'provider':provider, **item, 'generators':item['generators'].split()})
for provider, generator in sorted({(r['provider'],r['generator']) for r in inventory if r['provider'].startswith('floralzones')}):
    name = manual['floralAliases'].get(generator, generator)
    entries.append({'provider':provider,'generators':[generator], 'taxa':[name], **manual['floralOverrides'].get(generator, {})})
wanted = {compact(n) for e in entries for n in e['taxa']} | {compact(x['taxon']) for x in manual['exclusions']}
found = {}
archive = zipfile.ZipFile(args.sources/'wcvp.zip')
def rows(filename):
    with io.TextIOWrapper(archive.open(filename), encoding='utf-8', errors='replace', newline='') as stream:
        yield from csv.DictReader(stream, delimiter='|')
for r in rows('wcvp_names.csv'):
    key = compact(r['taxon_name'])
    if key in wanted:
        old = found.get(key)
        if old is None or r['taxon_status'] == 'Accepted': found[key] = r
missing = wanted - found.keys()
if missing: raise ValueError('Unresolved taxa: '+', '.join(sorted(missing)))
accepted_ids = {r['accepted_plant_name_id'] or r['plant_name_id'] for r in found.values()}
accepted = {}
for r in rows('wcvp_names.csv'):
    if r['plant_name_id'] in accepted_ids: accepted[r['plant_name_id']] = r
ranges = {k:set() for k in accepted_ids}
for r in rows('wcvp_distribution.csv'):
    if r['plant_name_id'] in ranges and r['introduced'] == '0' and r['location_doubtful'] == '0':
        ranges[r['plant_name_id']].add(r['area_code_l3'])
# Explicit corrections of WCVP native records that are introductions or errors (see the notes)
excluded = {}
for x in manual['exclusions']:
    ident = found[compact(x['taxon'])]
    ident = ident['accepted_plant_name_id'] or ident['plant_name_id']
    absent = set(x['regions']) - ranges[ident]
    if absent: raise ValueError(f"Exclusion for {x['taxon']} names regions that are not native records: {sorted(absent)}")
    ranges[ident] -= set(x['regions'])
    excluded.setdefault(ident, []).append(x)
taxa = {}
for key, original in found.items():
    ident = original['accepted_plant_name_id'] or original['plant_name_id']
    a = accepted[ident]
    if not ranges[ident]: raise ValueError('No native range: '+a['taxon_name'])
    taxa[key] = {'name':a['taxon_name'], 'id':ident, 'regions':sorted(ranges[ident])}
for entry in entries:
    source_names = entry['taxa']
    t = [taxa[compact(n)] for n in source_names]
    entry['taxa'] = sorted({n['name'] for n in t})
    entry['taxonIds'] = sorted({n['id'] for n in t})
    entry['regions'] = sorted({r for n in t for r in n['regions']})
    entry['generators'] = ['game:'+g for g in entry['generators']]
    if any(compact(n) in {compact(x) for x in manual['coastalFlora']} for n in source_names): entry['water'] = 'coastal'
    assert entry.get('water') in (None, 'fresh', 'coastal', 'riparian', 'shore'), entry
expected = {(x['provider'], 'game:'+x['generator']) for x in inventory}
covered = {(e['provider'], g) for e in entries for g in e['generators']}
assert expected == covered, (expected-covered, covered-expected)

features = json.loads((args.sources/'tdwg-level3.geojson').read_text())['features']
regions = []
level = {}
for feature in features:
    props = feature['properties']
    level[props['LEVEL3_COD']] = (props['LEVEL1_COD'], props['LEVEL2_COD'])
    geom = shape(feature['geometry']).simplify(.015, preserve_topology=True)
    coordinates = mapping(geom)['coordinates']
    if geom.geom_type == 'Polygon': coordinates = [coordinates]
    def rounded(v):
        return round(v,5) if isinstance(v,(int,float)) else [rounded(x) for x in v]
    regions.append({'code':props['LEVEL3_COD'], 'name':props['LEVEL3_NAM'], 'polygons':rounded(coordinates)})
codes = {r['code'] for r in regions}
assert all(set(e['regions']) <= codes for e in entries)

# ---------------------------------------------------------------- Earth climate envelopes
settings = manual['envelope']
climate = None if args.no_envelopes else json.loads(args.climate.read_text())
def histogram(region_codes, key):
    total = {}
    for code in region_codes:
        c = climate['regions'].get(code)
        if not c: continue
        for b, rel in c[key]:
            total[b] = total.get(b, 0) + rel * 1e-6 * c['w']
    return total
def percentile(hist, q, step, origin):
    items = sorted(hist.items())
    s = sum(w for _, w in items)
    if s <= 0: return None
    acc = 0
    for b, w in items:
        if acc + w >= q * s:
            return origin + (b + (q * s - acc) / w) * step
        acc += w
    return origin + (items[-1][0] + 1) * step
envelopes_made = 0
for entry in entries:
    if climate is None or entry.get('group'): continue
    th = histogram(entry['regions'], 't'); rh = histogram(entry['regions'], 'r')
    if not th: raise ValueError('No climate samples for '+' '.join(entry['generators']))
    lo, hi = settings['lowerPercentile'] / 100, settings['upperPercentile'] / 100
    if 'minTemperature' not in entry or 'maxTemperature' not in entry:
        t0 = percentile(th, lo, climate['tempStep'], climate['tempMin']) - settings['temperatureMargin']
        t1 = percentile(th, hi, climate['tempStep'], climate['tempMin']) + settings['temperatureMargin']
        width = settings['minTemperatureWidth']
        if t1 - t0 < width:
            mid = percentile(th, .5, climate['tempStep'], climate['tempMin'])
            t0, t1 = mid - width / 2, mid + width / 2
        manual_part = 'minTemperature' in entry or 'maxTemperature' in entry
        entry.setdefault('minTemperature', round(math.floor(t0 * 2) / 2, 1))
        entry.setdefault('maxTemperature', round(math.ceil(t1 * 2) / 2, 1))
        entry['climate'] = 'native-range, manual limit' if manual_part else 'native-range'
    else:
        entry['climate'] = 'manual'
    if 'minRain' not in entry:
        r0 = percentile(rh, lo, climate['rainStep'], 0) - settings['rainMargin']
        r1 = percentile(rh, hi, climate['rainStep'], 0) + settings['rainMargin']
        width = settings['minRainWidth']
        if r1 - r0 < width:
            mid = percentile(rh, .5, climate['rainStep'], 0)
            r0, r1 = mid - width / 2, mid + width / 2
        entry['minRain'], entry['maxRain'] = max(0, math.floor(r0)), (255 if r1 >= 250 else math.ceil(r1))
    envelopes_made += 1

# ---------------------------------------------------------------- regional analogues
generators_by_provider = {(e['provider'], g) for e in entries for g in e['generators']}
analogues = []
for a in manual['analogues']:
    selected = set()
    for code, (l1, l2) in level.items():
        if l1 in a.get('level1', []) or l2 in a.get('level2', []) or code in a.get('regions', []): selected.add(code)
    selected -= set(a.get('excludeRegions', []))
    if not selected: raise ValueError('Analogue rule without regions: '+a['name'])
    members = []
    for m in a['members']:
        gens = ['game:'+g for g in m['generators'].split()]
        for g in gens:
            if (m['provider'], g) not in generators_by_provider: raise ValueError(f"Analogue member {m['provider']} {g} is not a mapped generator")
        members.append({'provider':m['provider'], 'generators':gens, 'weight':m['weight']})
    out = {'name':a['name'], 'note':a['note'], 'regions':sorted(selected),
           'minTemperature':a['minTemperature'], 'maxTemperature':a['maxTemperature'],
           'minRain':a.get('minRain', 0), 'maxRain':a.get('maxRain', 255), 'members':members}
    for k in ('minColdMonth', 'maxColdMonth', 'minDrySummer', 'maxDrySummer', 'minAbsLatitude', 'maxAbsLatitude', 'forestTypes'):
        if k in a: out[k] = a[k]
    analogues.append(out)

# ---------------------------------------------------------------- finer natural ranges (chorological maps)
range_grid = None
chorology = manual.get('chorology')
if chorology and args.chorology and args.chorology.exists():
    import numpy as np
    import shapefile
    from PIL import Image, ImageDraw
    from scipy.ndimage import binary_dilation
    step, north, south = chorology['step'], chorology['north'], chorology['south']
    cols, rows_n = int(round(360 / step)), int(round((north - south) / step))
    def px(lon, lat): return ((lon + 180) / step, (north - lat) / step)
    def signed_area(ring): return sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(ring, ring[1:] + ring[:1])) / 2
    # botanical region of each cell (0 none), larger regions first so enclaves win
    label = Image.new('I', (cols, rows_n), 0)
    draw = ImageDraw.Draw(label)
    index = {r['code']: i + 1 for i, r in enumerate(regions)}
    by_area = sorted(regions, key=lambda r: -sum(abs(signed_area([tuple(p) for p in poly[0]])) for poly in r['polygons']))
    for r in by_area:
        for poly in r['polygons']:
            draw.polygon([px(*p) for p in poly[0]], fill=index[r['code']])
            for hole in poly[1:]: draw.polygon([px(*p) for p in hole], fill=0)
    label = np.array(label, dtype=np.int32)
    def species_map(folder):
        img = Image.new('L', (cols, rows_n), 0)
        d = ImageDraw.Draw(img)
        shp = sorted(p for p in (args.chorology/folder/'shapefiles').glob('*.shp') if '_syn_' not in p.name and '_clip' not in p.name)
        if not any(p.name.endswith('_plg.shp') for p in shp): raise ValueError('No native range polygon for '+folder)
        for path in shp:
            reader = shapefile.Reader(str(path))
            for s in reader.shapes():
                if s.shapeTypeName.startswith('POINT'):
                    for lon, lat in s.points:
                        x, y = px(lon, lat); d.rectangle([x - 1, y - 1, x + 1, y + 1], fill=1)
                    continue
                parts = list(s.parts) + [len(s.points)]
                for a, b in zip(parts, parts[1:]):
                    ring = s.points[a:b]
                    # shapefile rings: outer clockwise (negative area in lon/lat), holes counter-clockwise
                    d.polygon([px(*p) for p in ring], fill=1 if signed_area(ring) <= 0 else 0)
        return binary_dilation(np.array(img, dtype=bool), iterations=1)
    available = {p.name for p in args.chorology.iterdir() if p.is_dir()}
    by_name = {v['name']: v for v in taxa.values()}
    maps = {}
    for entry in entries:
        if entry.get('cultivated'): continue
        names = entry['taxa']
        mapped = [n for n in names if n not in chorology['exclude'] and any(f in available for f in chorology['aliases'].get(n, [n]))]
        if not mapped: continue
        allow = np.zeros((rows_n, cols), dtype=bool)
        holders = np.zeros((rows_n, cols), dtype=bool)
        for n in names:
            t = by_name[n]
            own = np.isin(label, [index[c] for c in t['regions']])
            holders |= own
            if n in mapped:
                key = n
                if key not in maps:
                    m = np.zeros((rows_n, cols), dtype=bool)
                    for folder in chorology['aliases'].get(n, [n]):
                        if folder in available: m |= species_map(folder)
                    # where the range of 1318 was wider than today's map (Caledonian pine), the box joins it
                    for s_, n_, w_, e_ in chorology.get('historic', {}).get(n, {}).get('boxes', []):
                        m[int(round((north - n_) / step)):int(round((north - s_) / step)), int(round((w_ + 180) / step)):int(round((e_ + 180) / step))] = True
                    maps[key] = m
                allow |= own & maps[key]
            else:
                allow |= own
        bits = ~holders | allow
        if bits.all(): continue
        raw = np.packbits(bits.ravel(), bitorder='little').tobytes()
        import base64, zlib
        entry['rangeMask'] = base64.b64encode(zlib.compress(raw, 9)).decode('ascii')
        entry['rangeSource'] = 'Caudullo et al. 2017: ' + ', '.join(f for n in mapped for f in chorology['aliases'].get(n, [n]) if f in available)
    range_grid = {'west':-180, 'north':north, 'step':step, 'cols':cols, 'rows':rows_n,
                  'source':'Caudullo, G., Welk, E., San-Miguel-Ayanz, J., 2017. Chorological maps for the main European woody species. Data in Brief 12, 662-666 (CC BY 4.0)'}
    print(f"Range masks for {sum(1 for e in entries if 'rangeMask' in e)} rules from {len(maps)} chorological maps.")

# Soil indicators: EIVE 1.0 (Dengler et al. 2023, CC BY 4.0) niche position and width for moisture (M), nutrients (N) and
# soil reaction (R) on its 0-10 scale, for the first of an entry's taxa EIVE lists (European flora only; others get none
# and keep their weight on every soil).
def xlsx_rows(path, sheet_name):
    """The rows of one sheet of an .xlsx file (cell values as str/float/None), with the standard library only."""
    import xml.etree.ElementTree as ET
    ns = {'m': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main',
          'r': 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'}
    with zipfile.ZipFile(path) as z:
        shared = []
        if 'xl/sharedStrings.xml' in z.namelist():
            for si in ET.fromstring(z.read('xl/sharedStrings.xml')).findall('m:si', ns):
                shared.append(''.join(t.text or '' for t in si.iter('{%s}t' % ns['m'])))
        book = ET.fromstring(z.read('xl/workbook.xml'))
        rid = next(s.get('{%s}id' % ns['r']) for s in book.find('m:sheets', ns) if s.get('name') == sheet_name)
        rels = ET.fromstring(z.read('xl/_rels/workbook.xml.rels'))
        target = next(r.get('Target') for r in rels if r.get('Id') == rid).lstrip('/')
        if not target.startswith('xl/'): target = 'xl/' + target
        def col_index(ref):
            n = 0
            for ch in ref:
                if not ch.isalpha(): break
                n = n * 26 + ord(ch.upper()) - 64
            return n - 1
        for row in ET.fromstring(z.read(target)).find('m:sheetData', ns):
            values = {}
            for c in row:
                v = c.find('m:v', ns)
                if c.get('t') == 's' and v is not None: val = shared[int(v.text)]
                elif c.get('t') == 'inlineStr': val = ''.join(t.text or '' for t in c.iter('{%s}t' % ns['m']))
                elif v is not None:
                    try: val = float(v.text)
                    except ValueError: val = v.text
                else: val = None
                values[col_index(c.get('r'))] = val
            yield tuple(values.get(i) for i in range(max(values) + 1)) if values else ()

if args.eive.exists():
    rows_iter = xlsx_rows(args.eive, 'mainTable')
    head = next(rows_iter)
    col = {h: i for i, h in enumerate(head)}
    eive = {}
    for row in rows_iter:
        if len(row) <= col["EIVEres-R.nw3"]: continue
        name = row[col["TaxonConcept"]]
        if not name: continue
        values = {}
        for k in ('M', 'N', 'R'):
            pos, width, n = row[col['EIVEres-' + k]], row[col['EIVEres-' + k + '.nw3']], row[col['EIVEres-' + k + '.n']]
            if pos is not None and width is not None: values[k.lower()] = (float(pos), float(width), float(n or 1))
        if len(values) == 3: eive[name.strip()] = values
    # A generator standing for several species (vanilla's beech: Fagus sylvatica, F. orientalis, F. crenata ...) gets their
    # niches combined, each weighted by how many indicator systems EIVE drew on for it (well-known natives outweigh the
    # odd introduced species): the mean position, and a width covering each species' niche about that position.
    found = 0
    for entry in entries:
        hits = []
        for name in entry['taxa']:
            v = eive.get(name) or eive.get(re.sub(r' (subsp|var)\. .*', '', name))
            if v and v not in hits: hits.append(v)
        if not hits: continue
        combined = {}
        for k in ('m', 'n', 'r'):
            total = sum(h[k][2] for h in hits)
            pos = sum(h[k][0] * h[k][2] for h in hits) / total
            width = sum((h[k][1] + 2 * abs(h[k][0] - pos)) * h[k][2] for h in hits) / total
            combined[k] = [round(pos, 2), round(width, 2)]
        entry['eive'] = combined
        found += 1
    print(f"EIVE soil indicators for {found} of {len(entries)} rules.")

ORDER = ('provider', 'generators', 'taxa', 'taxonIds', 'regions') + RULE_FIELDS + ('climate', 'eive', 'rangeSource', 'rangeMask')
entries = [{k:e[k] for k in ORDER if k in e} for e in entries]
output = ROOT/'assets/earth/config'
write(output/'treeregions.json', {'version':1, 'regions':regions})
tree_file = {'version':RULES_VERSION, 'entries':entries, 'analogues':analogues}
if range_grid: tree_file['rangeGrid'] = range_grid
write(output/'regionaltrees.json', tree_file)
write(ROOT/'docs/research/tree-range-provenance.json', {
    'wcvp':{'url':'https://sftp.kew.org/pub/data-repositories/WCVP/wcvp.zip','snapshot':'2026-06-04','sha256':sha(args.sources/'wcvp.zip'), 'license':'CC BY 3.0'},
    'boundaries':{'url':'https://raw.githubusercontent.com/tdwg/wgsrpd/master/geojson/level3.geojson','sha256':sha(args.sources/'tdwg-level3.geojson'), 'simplificationDegrees':0.015,
                  'license':'open question: no explicit licence on the files (TDWG website content is CC BY 4.0; file metadata retains an ESRI All Rights Reserved credit); see TREE-DATA-ATTRIBUTION.txt'},
    'regionClimate':None if climate is None else {'file':'tools/tree-region-climate.json','sha256':sha(args.climate),
                    'climateSha256':climate['climateSha256'],'terrainSha256':climate['terrainSha256'],'envelope':settings},
    'inventorySha256':sha(inventory_path),
    'exclusions':[{'taxon':x['taxon'],'regions':x['regions'],'note':x['note']} for x in manual['exclusions']],
    'rulesVersion':RULES_VERSION, 'generatorCount':len(covered), 'analogueRules':len(analogues),
    'taxa':sorted({v['name']:v for v in taxa.values()}.values(),key=lambda x:x['name'])})
print(f'Baked {len(covered)} generators, {len(entries)} rules ({envelopes_made} with Earth climate envelopes), '
      f'{len(analogues)} analogue rules, {len(regions)} botanical regions.')
