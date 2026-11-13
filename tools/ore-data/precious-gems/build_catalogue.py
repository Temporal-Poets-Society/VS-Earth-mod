"""Build the precious-gems deposit catalogue, its heat-map evidence and its validation report (decision 0065).

Inputs (this folder, all authored or normalised from cited sources; see ATTRIBUTION.txt):
  deposits-*.csv   one row per deposit (columns: see README in tools/ore-data and docs/research/ore-deposits-2026-11-08/precious-gems.md)
  areas.json       Witwatersrand goldfields (areas with reef beds)
  sources*.json    the sources[] of the catalogue
  extra-evidence.json  optional heat-map-only evidence points (districts without a catalogue body)

Outputs:
  assets/earth/config/ore-deposits-v1/precious-gems.json   the catalogue (minified)
  tools/ore-data/precious-gems/evidence.geojson            group evidence for the heat-map bake
  tools/ore-data/precious-gems/validation.json             checks and totals (read by the research doc)

Run: C:\\VS mods\\Earth\\.venv\\Scripts\\python.exe -I tools/ore-data/precious-gems/build_catalogue.py
Fails (exit 1) on any hard error: unknown source, code or shape, shares not summing to 1, a body larger than its
footprint, a point not on land or outside its country.
"""
from pathlib import Path
from collections import defaultdict, Counter
import csv, json, math, sys, importlib.util

import shapely
from shapely.geometry import Polygon, Point

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / 'assets/earth/config/ore-deposits-v1/precious-gems.json'
EVIDENCE = HERE / 'evidence.geojson'
VALIDATION = HERE / 'validation.json'
ATLAS = ROOT / 'docs/research/remaining-ores-2026-09-29'

LAYERS = ['Au', 'diamond', 'corundum', 'emerald', 'olivine', 'garnet', 'lapis', 'topaz', 'tourmaline', 'beryl', 'spinel',
          'C_graphite', 'F', 'SiO2', 'U']
SHAPES = {'stock', 'lens', 'veins', 'blanket', 'dome'}
AGES = {'Cenozoic', 'Mesozoic', 'Paleozoic', 'Devonian', 'Carboniferous', 'Permian', 'Precambrian'}
EST_FIELDS = {'mt', 'grade', 'density', 'topM', 'baseM', 'lenKm', 'widKm', 'strikeDeg', 'dipDeg', 'lat', 'lon'}

# Grade bands per resource (layer code). Below "medium" is poor. Units match every catalogue grade of that layer.
GRADE_BANDS = {
    'Au': {'unit': 'g/t', 'medium': 1.0, 'rich': 4.0, 'bountiful': 10.0},
    'U': {'unit': '%', 'medium': 0.1, 'rich': 0.5, 'bountiful': 2.0},
    'diamond': {'unit': 'ct/t', 'medium': 0.3, 'rich': 1.0, 'bountiful': 3.0},
    'emerald': {'unit': 'ct/t', 'medium': 1.0, 'rich': 5.0, 'bountiful': 50.0},
    'corundum': {'unit': 'ct/t', 'medium': 0.1, 'rich': 1.0, 'bountiful': 10.0},
    'spinel': {'unit': 'ct/t', 'medium': 0.5, 'rich': 2.0, 'bountiful': 10.0},
    'olivine': {'unit': 'ct/t', 'medium': 0.5, 'rich': 2.0, 'bountiful': 10.0},
    'garnet': {'unit': 'ct/t', 'medium': 1.0, 'rich': 5.0, 'bountiful': 20.0},
    'topaz': {'unit': 'ct/t', 'medium': 0.5, 'rich': 2.0, 'bountiful': 10.0},
    'tourmaline': {'unit': 'ct/t', 'medium': 0.5, 'rich': 2.0, 'bountiful': 10.0},
    'beryl': {'unit': 'ct/t', 'medium': 0.5, 'rich': 2.0, 'bountiful': 10.0},
    'lapis': {'unit': 'ct/t', 'medium': 1.0, 'rich': 5.0, 'bountiful': 20.0},
    'C_graphite': {'unit': '%', 'medium': 5.0, 'rich': 10.0, 'bountiful': 20.0},
    'F': {'unit': '%', 'medium': 20.0, 'rich': 40.0, 'bountiful': 60.0},
    'SiO2': {'unit': '%', 'medium': 95.0, 'rich': 99.0, 'bountiful': 99.9},
}
CU_BAND = {'unit': '%', 'medium': 0.4, 'rich': 1.2, 'bountiful': 3.0}  # base-metals' Cu band, for by-product chalcopyrite
V_BAND = {'unit': '%', 'basis': 'V2O5', 'medium': 0.3, 'rich': 0.8, 'bountiful': 1.4}  # iron-ferro's V band (Uravan)

# Ore code -> layer code, for heat-map evidence of the other gems a deposit carries (foreign codes are skipped).
CODE_LAYER = {'gold': 'Au', 'earth-placergold': 'Au', 'diamond': 'diamond', 'emerald': 'emerald', 'lapis': 'lapis',
              'corundum': 'corundum', 'corundumruby': 'corundum', 'corundumsapphire': 'corundum', 'spinelred': 'spinel',
              'peridot': 'olivine', 'olivine': 'olivine', 'graphite': 'C_graphite', 'fluorite': 'F', 'quartz': 'SiO2',
              'uranium': 'U'}
for _c in ['garnetalmandine', 'garnetandradite', 'garnetgrossular', 'garnetpyrope', 'garnetspessartine', 'garnetuvarovite']:
    CODE_LAYER[_c] = 'garnet'
for _c in ['topazamber', 'topazblue', 'topazpink']: CODE_LAYER[_c] = 'topaz'
for _c in ['tourmalinerubellite', 'tourmalineschorl', 'tourmalineverdelite', 'tourmalinewatermelon']: CODE_LAYER[_c] = 'tourmaline'
for _c in ['berylaquamarine', 'berylbixbite']: CODE_LAYER[_c] = 'beryl'

# Decision 0060 layer classes of each ore code (assets/earth/config/ore-layers-v1.json).
BED_ONLY = {'fluorite'}
# Small islands missing from Natural Earth 1:10m admin polygons (Earth's own heights have them): checked by hand.
ISLANDS = {'pg:ol-zabargad': "Zabargad (St John's Island), 23.60 N 36.20 E, Egyptian Red Sea, about 4.5 km2"}
COVER_CODES = {'earth-placergold'}
# 0060 layer classes per body (`layers`, contract addition of 8 Nov, claude/ore-rework 50420af). Every blanket here is a
# gravel: placer gold, alluvial diamonds and gem gravels take alluvium and outwash (not laterite or saprolite, the
# blanket default). Beach and deflation diamonds of the Namibian and Namaqualand coasts and Nome's beach placers are
# also in beach and dune sand (`cover`). Other shapes keep 0060's table of their ore codes (hard rock: bed, basement).
BLANKET_LAYERS = ['alluvial', 'outwash']
BEACH = {'pg:dia-namibia-coast', 'pg:dia-elizabeth-bay', 'pg:dia-namaqualand', 'pg:pl-nome'}
# By-product copper in gold bodies (each deposit once, owned by its main commodity by value): chalcopyrite takes its
# tier from the copper grade via the runtime's `oreMetal` + `grades` and base-metals' Cu band. % Cu of the body's ore.
# Uravan's vanadium (carnotite-roscoelite, about 5 V2O5 : 1 U3O8) is a `vanadinite` share (Geology Additions code;
# iron-ferro gave the body to this group, uranium being the main value), tiered by % V2O5 with iron-ferro's V band.
ORE_METAL = {'chalcopyrite': 'Cu', 'vanadinite': 'V'}
METAL_GRADE = {'Cu': {'pg:au-telfer': 0.12, 'pg:au-mount-morgan': 0.72, 'pg:au-zijinshan': 0.45},
               'V': {'pg:u-uravan': 1.5}}

# Contained amount (mt x grade in the layer's unit, so t Au, Mct, kt U3O8 ...) at which a deposit counts as a major
# or large district in the heat-map evidence.
def contained(layer, mt, grade):
    if grade is None: return None
    if layer == 'Au': return mt * grade  # t Au
    if layer in ('U', 'C_graphite', 'F', 'SiO2'): return mt * grade / 100  # Mt of U3O8, graphite, CaF2, SiO2
    return mt * grade  # Mct (ct/t x Mt)

TIERS = {  # layer: (major, large)
    'Au': (500, 50), 'U': (0.1, 0.01), 'diamond': (100, 10), 'C_graphite': (10, 1), 'F': (10, 1), 'SiO2': (1, 0.01),
}
GEM_TIERS = (5, 0.5)  # Mct


def load_country_check():
    spec = importlib.util.spec_from_file_location('bake', ROOT / 'tools/bake_ore_heatmaps.py')
    argv = sys.argv; sys.argv = [argv[0]]
    try:
        bake = importlib.util.module_from_spec(spec); spec.loader.exec_module(bake)
    finally:
        sys.argv = argv
    cfg = json.loads((ROOT / 'tools/ore-heatmap-settings.json').read_text())['coordinateCheck']
    cc = bake.CountryCheck(ROOT / cfg['table'], cfg['marginDegrees'], ROOT / cfg['naturalEarth'], cfg['polygonToleranceDegrees'])
    from pyogrio.raw import read
    _, _, wkb, _ = read('/vsizip/' + (ROOT / cfg['naturalEarth']).as_posix() + '/ne_10m_admin_0_countries.shp', columns=['ADMIN'])
    land = shapely.union_all(shapely.from_wkb(wkb)); shapely.prepare(land)
    return cc, land


def known_codes():
    codes = set(['earth-placergold', 'chalcopyrite'])
    with (ATLAS / 'game-mineral-scope.csv').open(encoding='utf-8') as f:
        for r in csv.DictReader(f): codes.add(r['deposit_code'])
    for e in json.loads((ROOT / 'assets/earth/worldgen/deposits/earth-gems.json').read_text(encoding='utf-8')): codes.add(e['code'])
    return codes


def num(v):
    v = (v or '').strip()
    return float(v) if v else None


def area_km2(ring):
    lat0 = sum(p[1] for p in ring) / len(ring)
    k = math.cos(math.radians(lat0))
    return Polygon([(lo * 111.32 * k, la * 110.57) for lo, la in ring]).area


def main():
    errors, warnings = [], []
    sources = []
    for p in sorted(HERE.glob('sources*.json')):
        sources += json.loads(p.read_text(encoding='utf-8'))
    src_ids = Counter(s['id'] for s in sources)
    for k, n in src_ids.items():
        if n > 1: errors.append('duplicate source id ' + k)
    codes = known_codes()
    cc, land = load_country_check()

    rows = []
    for p in sorted(HERE.glob('deposits-*.csv')):
        with p.open(encoding='utf-8') as f:
            for r in csv.DictReader(f): r['_file'] = p.name; rows.append(r)
    ids = Counter(r['id'] for r in rows)
    for k, n in ids.items():
        if n > 1: errors.append('duplicate deposit id ' + k)

    deposits, report = [], []
    used_src = set()
    for r in rows:
        did = r['id']; lat, lon = float(r['lat']), float(r['lon'])
        layer = r['layer']; shape = r['shape']; mt = float(r['mt']); grade = num(r['grade']); unit = r['unit'].strip() or None
        dens = num(r['density']); top, base = float(r['topM']), float(r['baseM'])
        ln, wd = float(r['lenKm']), float(r['widKm'])
        e = lambda m: errors.append(did + ': ' + m)
        if layer not in LAYERS: e('layer ' + layer)
        if shape not in SHAPES: e('shape ' + shape)
        if r['age'] not in AGES: e('age ' + r['age'])
        if r['src'] not in src_ids: e('unknown src ' + r['src'])
        used_src.add(r['src'])
        if r['q'] not in ('A', 'B', 'C'): e('q ' + r['q'])
        est = [x for x in (r['est'] or '').split(';') if x]
        for x in est:
            if x not in EST_FIELDS: e('est field ' + x)
        ores = []
        for part in r['ores'].split(';'):
            bits = part.split(':')
            o = {'code': bits[0], 'share': float(bits[1])}
            if len(bits) > 2: o['zone'] = bits[2]
            if o['code'] not in codes: e('unknown ore code ' + o['code'])
            ores.append(o)
        if abs(sum(o['share'] for o in ores) - 1) > 1e-6: e('shares sum %.3f' % sum(o['share'] for o in ores))
        if grade is not None:
            band = GRADE_BANDS[layer]['unit']
            if unit != band: e('unit %s, band unit %s' % (unit, band))
        if not (base > top >= 0): e('depths %s..%s' % (top, base))
        if mt <= 0 or ln <= 0 or wd <= 0: e('size')
        d = dens or 2.7
        vol = mt * 1e6 / d
        fill = vol / (ln * wd * 1e6 * (base - top))
        if fill > 1: e('body larger than its footprint (fill %.2f)' % fill)
        # 0060 layers
        for o in ores:
            if o['code'] in COVER_CODES and (shape != 'blanket' or r['age'] != 'Cenozoic' or top > 30):
                e('placer %s must be a Cenozoic blanket near the surface' % o['code'])
            if o['code'] in BED_ONLY and r['age'] == 'Precambrian':
                warnings.append(did + ': fluorite (bed-only in 0060) with a Precambrian host; the runtime uses a similar bed host or drops it')
        layers = BLANKET_LAYERS + (['cover'] if did in BEACH else []) if shape == 'blanket' else None
        if did in BEACH and shape != 'blanket': e('beach body must be a blanket')
        metals = {}
        for o in ores:
            m = ORE_METAL.get(o['code'])
            if m:
                if did not in METAL_GRADE[m]: e('%s share without a %s grade in METAL_GRADE' % (o['code'], m))
                else: metals[m] = METAL_GRADE[m][did]
        if metals and grade is not None: metals = {layer: grade, **metals}
        # coordinates
        on_land = bool(shapely.intersects(land, Point(lon, lat)))
        near_land = on_land or bool(shapely.dwithin(land, Point(lon, lat), 0.05))
        inside = cc.inside(r['country'], lat, lon, margin=0.1)
        if did in ISLANDS: inside, near_land, on_land = True, True, False
        if inside is None: e('country label not in table: ' + r['country'])
        elif inside is False: e('outside %s' % r['country'])
        if not near_land: e('not on land')
        elif not on_land: warnings.append(did + ': within 0.05 deg of the coast but not inside a land polygon (island or coast)')
        dep = {'id': did, 'name': r['name'], 'lat': lat, 'lon': lon, 'layer': layer, 'ores': ores, 'shape': shape, 'mt': mt,
               'grade': grade, 'unit': unit if grade is not None else None}
        if dens: dep['density'] = dens
        dep.update({'topM': top, 'baseM': base, 'lenKm': ln, 'widKm': wd, 'strikeDeg': float(r['strikeDeg']), 'dipDeg': float(r['dipDeg']),
                    'age': r['age'], 'src': r['src'], 'q': r['q']})
        if layers: dep['layers'] = layers
        if metals: dep['grades'] = metals
        if est: dep['est'] = est
        deposits.append(dep)
        report.append({'id': did, 'name': r['name'], 'country': r['country'], 'layer': layer, 'shape': shape, 'mt': mt, 'grade': grade,
                       'unit': unit, 'contained': contained(layer, mt, grade), 'fill': round(fill, 4), 'q': r['q'], 'age': r['age'],
                       'lat': lat, 'lon': lon, 'onLand': on_land, 'lenKm': ln, 'widKm': wd, 'ores': ores, 'file': r['_file']})

    areas, area_report = [], []
    adoc = json.loads((HERE / 'areas.json').read_text(encoding='utf-8'))
    for a in adoc['areas']:
        ring = a['ring']; A = area_km2(ring) * 1e6
        dens = a['density']
        denom = A * sum(b['thickM'] * dens * b['grade'] for b in a['beds'])
        cover = a['goldT'] * 1e6 / denom
        if cover > 1: errors.append(a['id'] + ': reef cover %.2f > 1' % cover)
        mt = cover * A * sum(b['thickM'] * dens for b in a['beds']) / 1e6
        poly = Polygon(ring)
        if not poly.is_valid: errors.append(a['id'] + ': invalid polygon')
        c = poly.representative_point()
        if cc.inside(a['country'], c.y, c.x, margin=0.1) is not True: errors.append(a['id'] + ': outside ' + a['country'])
        used_src.add(a['src'])
        if a['src'] not in src_ids: errors.append(a['id'] + ': unknown src')
        beds = [{'code': b['code'], 'topM': b['topM'], 'thickM': b['thickM'], 'cover': round(cover, 3), 'grade': b['grade']} for b in a['beds']]
        areas.append({'id': a['id'], 'name': a['name'], 'layer': a['layer'], 'age': a['age'], 'src': a['src'], 'q': a['q'],
                      'geometry': {'type': 'Polygon', 'coordinates': [ring]}, 'beds': beds, 'mt': round(mt, 1)})
        area_report.append({'id': a['id'], 'name': a['name'], 'country': a['country'], 'areaKm2': round(A / 1e6, 1), 'cover': round(cover, 3),
                            'mt': round(mt, 1), 'goldT': a['goldT'], 'lon': c.x, 'lat': c.y})

    unused = sorted(set(src_ids) - used_src - {'lit-wgc-2025', 'usgs-mcs-2025', 'lit-kp-2023'})
    if unused: warnings.append('sources not used by any entry: ' + ', '.join(unused))
    catalogue = {'version': 1, 'group': 'precious-gems',
                 'sources': [s for s in sources],
                 'gradeBands': {**GRADE_BANDS, 'Cu': CU_BAND, 'V': V_BAND}, 'oreMetal': ORE_METAL, 'deposits': deposits, 'areas': areas}
    OUT.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(catalogue, separators=(',', ':'), ensure_ascii=False)
    assert len(text.encode('utf-8')) < 3_000_000
    OUT.write_text(text + '\n', encoding='utf-8')

    write_evidence(report, area_report)
    totals = summarise(report, area_report)
    VALIDATION.write_text(json.dumps({'errors': errors, 'warnings': warnings, 'counts': totals['counts'], 'totals': totals,
                                      'reference': reference_check(totals),
                                      'deposits': report, 'areas': area_report}, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
    print(json.dumps({'deposits': len(deposits), 'areas': len(areas), 'errors': len(errors), 'warnings': len(warnings),
                      'bytes': len(text)}, indent=None))
    for x in errors: print('ERROR', x)
    for x in warnings: print('warn ', x)
    if errors: sys.exit(1)


def tier_for(layer, amount):
    major, large = TIERS.get(layer, GEM_TIERS)
    if amount is None: return 'deposit_large'
    return 'major' if amount >= major else 'deposit_large' if amount >= large else 'deposit'


PEAK = {'major': (0.95, 75), 'deposit_large': (0.85, 60), 'deposit': (0.75, 45)}


def write_evidence(report, area_report):
    """One point per deposit and layer it carries (peak by contained amount; radius grows with the footprint), and the
    Witwatersrand goldfields as polygons. Points keep their country so the bake's envelope check applies."""
    feats = []
    for r in report:
        layers = defaultdict(float)
        for o in r['ores']:
            lay = CODE_LAYER.get(o['code'])
            if lay: layers[lay] += o['share']
        for lay, share in layers.items():
            amount = r['contained'] if lay == r['layer'] else (r['contained'] or 0) * share or None
            t = tier_for(lay, amount)
            peak, radius = PEAK[t]
            if lay != r['layer']: peak = round(peak * 0.9, 3)  # carried gem of a multi-gem deposit
            radius = min(150, max(radius, round(r['lenKm'] / 2 + 25)))
            feats.append({'type': 'Feature', 'geometry': {'type': 'Point', 'coordinates': [r['lon'], r['lat']]},
                          'properties': {'code': lay, 'source': 'precious-gems-catalogue', 'peak': peak, 'radiusKm': radius,
                                         'id': r['id'], 'name': r['name'], 'country': r['country']}})
            if r['id'] in ISLANDS: del feats[-1]['properties']['country']  # not in Natural Earth: skip the envelope check
    adoc = json.loads((HERE / 'areas.json').read_text(encoding='utf-8'))
    for a in adoc['areas']:
        feats.append({'type': 'Feature', 'geometry': {'type': 'Polygon', 'coordinates': [a['ring']]},
                      'properties': {'code': a['layer'], 'source': 'precious-gems-catalogue', 'weight': 0.95, 'id': a['id'],
                                     'name': a['name'], 'group': a['id']}})
        lon = sum(p[0] for p in a['ring'][:-1]) / (len(a['ring']) - 1); lat = sum(p[1] for p in a['ring'][:-1]) / (len(a['ring']) - 1)
        feats.append({'type': 'Feature', 'geometry': {'type': 'Point', 'coordinates': [round(lon, 3), round(lat, 3)]},
                      'properties': {'code': a['layer'], 'source': 'precious-gems-catalogue', 'peak': 0.95, 'radiusKm': 75,
                                     'id': a['id'] + ':district', 'name': a['name'], 'country': a['country']}})
    extra = HERE / 'extra-evidence.json'
    if extra.exists():
        for x in json.loads(extra.read_text(encoding='utf-8'))['points']:
            feats.append({'type': 'Feature', 'geometry': {'type': 'Point', 'coordinates': [x['lon'], x['lat']]},
                          'properties': {k: v for k, v in x.items() if k not in ('lat', 'lon', 'note')}})
    EVIDENCE.write_text(json.dumps({'type': 'FeatureCollection', 'features': feats}, separators=(',', ':'), ensure_ascii=False) + '\n', encoding='utf-8')


def summarise(report, area_report):
    counts = Counter(r['layer'] for r in report)
    counts['areas'] = len(area_report)
    gold = defaultdict(float); gold_kind = defaultdict(float)
    for r in report:
        if r['layer'] == 'Au' and r['contained']:
            gold[r['country']] += r['contained']
            gold_kind['placer' if r['shape'] == 'blanket' else 'lode'] += r['contained']
    for a in area_report:
        gold[a['country']] += a['goldT']; gold_kind['Witwatersrand'] += a['goldT']
    dia = defaultdict(float)
    for r in report:
        if r['layer'] == 'diamond' and r['contained']: dia[r['country']] += r['contained']
    u = defaultdict(float)
    for r in report:
        if r['layer'] == 'U' and r['contained']: u[r['country']] += r['contained'] * 1000  # kt U3O8
    top_au = sorted([(r['contained'], r['name']) for r in report if r['layer'] == 'Au' and r['contained']] +
                    [(a['goldT'], a['name']) for a in area_report], reverse=True)[:20]
    top_mt = sorted([(r['mt'], r['name'], r['layer']) for r in report] + [(a['mt'], a['name'], 'Au') for a in area_report], reverse=True)[:20]
    top_dia = sorted([(r['contained'], r['name']) for r in report if r['layer'] == 'diamond' and r['contained']], reverse=True)[:20]
    return {'counts': dict(counts), 'goldTonnesByCountry': dict(sorted(gold.items(), key=lambda x: -x[1])),
            'goldTonnesByKind': dict(gold_kind), 'goldTonnesTotal': round(sum(gold.values())),
            'diamondMctByCountry': dict(sorted(dia.items(), key=lambda x: -x[1])), 'diamondMctTotal': round(sum(dia.values())),
            'uraniumKtU3O8ByCountry': dict(sorted(u.items(), key=lambda x: -x[1])), 'uraniumKtU3O8Total': round(sum(u.values())),
            'top20GoldTonnes': top_au, 'top20OreMt': top_mt, 'top20DiamondMct': top_dia}


# Published reference figures (facts, cited in the research doc). Cumulative mine production to date by country is the
# rounded value most often quoted by the national survey or the World Gold Council; the catalogue holds endowment
# (mined + reserves + resources of the bodies it has), so catalogue / (cumulative + reserves) is its coverage.
WGC_ABOVE_GROUND_T = 216_265  # World Gold Council, end of 2024 (lit-wgc-2025)
# Rounded to the nearest 500-1000 t; good to about +-20 %, enough to judge coverage, not to rank countries closely.
GOLD_CUMULATIVE_T = {'South Africa': 53_000, 'United States': 24_000, 'Russia': 18_000, 'Australia': 15_000,
                     'Canada': 12_000, 'China': 10_000, 'Uzbekistan': 5_000, 'Ghana': 5_000, 'Brazil': 3_500,
                     'Peru': 5_000, 'Indonesia': 3_500, 'Kazakhstan': 1_500, 'Mexico': 3_500}
KP_WORLD_ROUGH_MCT_2023 = 111.5  # Kimberley Process, all rough diamonds (gem and industrial), 2023 (lit-kp-2023)


def reference_check(totals):
    ref = HERE / 'reference-mcs2026.json'
    if not ref.exists(): return None
    tables = json.loads(ref.read_text(encoding='utf-8'))['tables']
    gold = {r['country']: r for r in tables['gold']['rows']}
    out = {'source': 'USGS Mineral Commodity Summaries 2026 (reference-mcs2026.json); cumulative production figures in GOLD_CUMULATIVE_T'}
    rows = []
    for c, cum in GOLD_CUMULATIVE_T.items():
        res = (gold.get(c) or {}).get('reserves') or 0
        have = totals['goldTonnesByCountry'].get(c, 0)
        rows.append({'country': c, 'catalogueT': round(have), 'cumulativeT': cum, 'reservesT': res,
                     'coverage': round(have / (cum + res), 2)})
    world_ref = WGC_ABOVE_GROUND_T + gold['World total (rounded)']['reserves']
    out['gold'] = {'byCountry': rows, 'world': {'catalogueT': totals['goldTonnesTotal'], 'wgcAboveGroundT': WGC_ABOVE_GROUND_T,
                   'mcsReservesT': gold['World total (rounded)']['reserves'], 'coverage': round(totals['goldTonnesTotal'] / world_ref, 2)}}
    dia = {r['country']: r for r in tables['gemDiamond']['rows']}
    drows = []
    for c, r in dia.items():
        if c.startswith(('World', 'Other', 'United States')): continue
        drows.append({'country': c, 'catalogueMct': round(totals['diamondMctByCountry'].get(c, 0)),
                      'gemProduction2024Mct': r['production2024'] and r['production2024'] / 1000,
                      'gemReservesMct': r['reserves'] and r['reserves'] / 1000})
    for c, v in totals['diamondMctByCountry'].items():
        if c not in dia: drows.append({'country': c, 'catalogueMct': round(v), 'gemProduction2024Mct': None, 'gemReservesMct': None})
    out['diamond'] = {'byCountry': drows, 'world': {'catalogueMct': totals['diamondMctTotal'],
                      'gemProduction2024Mct': dia['World total (rounded)']['production2024'] / 1000,
                      'gemReservesMct': dia['World total (rounded)']['reserves'] / 1000, 'kpAllRough2023Mct': KP_WORLD_ROUGH_MCT_2023}}
    return out


if __name__ == '__main__':
    main()
