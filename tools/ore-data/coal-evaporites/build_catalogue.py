"""Build the coal and evaporites catalogue (decision 0065) and the group's heat-map evidence.

Inputs (all on disk; see fetch_sources.py for what it downloads and ATTRIBUTION.txt for licences):
  tables.py                                   the authored catalogue: basins, beds, deposits, published tonnages, sources
  docs/research/remaining-ores-2026-09-29/corrections/coal/normalized/coal-polygons.geojson   (USGS, BGS, PGI, NRW, Saxony, NSW, Victoria)
  docs/research/remaining-ores-2026-09-29/corrections/coal/normalized/coal-context-polygons.geojson (GA basins)
  docs/research/remaining-ores-2026-09-29/normalized/PotashTracts.geojson  (USGS global potash tracts, SIR 2010-5090-S)
  docs/research/remaining-ores-2026-09-29/raw/naturalearth/ne_10m_admin_0_countries.zip (land and country check)

Outputs:
  assets/earth/config/ore-deposits-v1/coal-evaporites.json   catalogue (schema: tools/ore-data/README.md), minified
  tools/ore-data/coal-evaporites/evidence.geojson            group evidence for the heat-map bake
  tools/ore-data/coal-evaporites/validation.json             checks and tables used by the research note

Run: C:\\VS mods\\Earth\\.venv\\Scripts\\python.exe -I tools/ore-data/coal-evaporites/build_catalogue.py
"""
from pathlib import Path
from collections import defaultdict, Counter
import json, math, sys

import shapely
from shapely.geometry import shape, Polygon, MultiPolygon, Point, box, mapping
from shapely.ops import unary_union
from pyproj import Geod

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
sys.path.insert(0, str(HERE))
import tables  # noqa: E402

COAL = ROOT / 'docs/research/remaining-ores-2026-09-29/corrections/coal/normalized'
POTASH = ROOT / 'docs/research/remaining-ores-2026-09-29/normalized/PotashTracts.geojson'
NE = ROOT / 'docs/research/remaining-ores-2026-09-29/raw/naturalearth/ne_10m_admin_0_countries.zip'
OUT = ROOT / 'assets/earth/config/ore-deposits-v1/coal-evaporites.json'
EVIDENCE = HERE / 'evidence.geojson'
VALIDATION = HERE / 'validation.json'
GEOD = Geod(ellps='WGS84')
SIMPLIFY = 0.01  # degrees, about 1 km
# 0060 layer classes a body may name in `layers` (tools/ore-data/README.md, from assets/earth/config/ore-layers-v1.json)
LAYER_CLASSES = {'alluvial', 'outwash', 'peat', 'laterite', 'saprolite', 'playa', 'deep-cover', 'cover', 'bed', 'basement'}
AGES = {'Cenozoic', 'Mesozoic', 'Paleozoic', 'Precambrian', 'Cambrian', 'Ordovician', 'Silurian', 'Devonian',
        'Carboniferous', 'Permian', 'Triassic', 'Jurassic', 'Cretaceous', 'Paleogene', 'Neogene', 'Quaternary'}
ERA = {'Cenozoic': 'Cenozoic', 'Paleogene': 'Cenozoic', 'Neogene': 'Cenozoic', 'Quaternary': 'Cenozoic',
       'Mesozoic': 'Mesozoic', 'Triassic': 'Mesozoic', 'Jurassic': 'Mesozoic', 'Cretaceous': 'Mesozoic',
       'Paleozoic': 'Paleozoic', 'Cambrian': 'Paleozoic', 'Ordovician': 'Paleozoic', 'Silurian': 'Paleozoic',
       'Devonian': 'Paleozoic', 'Carboniferous': 'Paleozoic', 'Permian': 'Paleozoic', 'Precambrian': 'Precambrian'}
DEVONIAN_OR_LATER = {'Devonian', 'Carboniferous', 'Permian'}


# --------------------------------------------------------------------------------------------- geometry sources
class Sources:
    def __init__(self):
        self._coal = None; self._ctx = None; self._potash = None

    def coal(self):
        if self._coal is None:
            self._coal = []
            for f in json.loads((COAL / 'coal-polygons.geojson').read_text(encoding='utf-8'))['features']:
                p = f['properties']
                if p.get('geometry_valid') is False or not f.get('geometry'): continue
                g = shape(f['geometry'])
                if g.is_empty: continue
                self._coal.append((p['source_id'], p, g))
        return self._coal

    def ctx(self):
        if self._ctx is None:
            self._ctx = {}
            for f in json.loads((COAL / 'coal-context-polygons.geojson').read_text(encoding='utf-8'))['features']:
                p = f['properties']
                if p['source_id'] == 'ga_basins_2013' and f.get('geometry'):
                    self._ctx[p['name']] = shapely.make_valid(shape(f['geometry']))
        return self._ctx

    def potash(self):
        if self._potash is None:
            self._potash = {}
            for f in json.loads(POTASH.read_text(encoding='utf-8'))['features']:
                a = f['properties']['source_attributes']
                self._potash[a['Coded_ID']] = (a, shapely.make_valid(shape(f['geometry'])))
        return self._potash


SRC = Sources()


def match(p, where):
    if not where: return True
    attrs = p.get('source_attributes') or {}
    for k, want in where.items():
        v = p.get(k, attrs.get(k))
        v = '' if v is None else str(v)
        wants = want if isinstance(want, (list, tuple)) else [want]
        if not any(w.casefold() in v.casefold() for w in wants): return False
    return True


def resolve(recipe):
    """Turn a geometry recipe of tables.py into a shapely geometry in degrees."""
    kind = recipe['kind']
    if kind == 'ring':
        g = Polygon(recipe['ring'])
    elif kind == 'union':
        g = unary_union([resolve(r) for r in recipe['parts']])
    elif kind == 'coal':
        bb = box(*recipe['bbox']); nb = [box(*b) for b in recipe.get('notbox', [])]
        parts = []
        for sid, p, geom in SRC.coal():
            if sid not in recipe['src'] or not match(p, recipe.get('where')): continue
            if not geom.intersects(bb): continue
            parts.append(shapely.make_valid(geom).intersection(bb))
        if not parts: raise ValueError('no coal polygons for recipe %r' % recipe)
        g = unary_union(parts)
        for b in nb: g = g.difference(b)
    elif kind == 'ga':
        g = SRC.ctx()[recipe['name']]
    elif kind == 'potash':
        g = unary_union([SRC.potash()[i][1] for i in recipe['ids']])
    else:
        raise ValueError(kind)
    if recipe.get('clip'): g = g.intersection(box(*recipe['clip']))
    if recipe.get('minus'): g = g.difference(resolve(recipe['minus']))
    return g


def tidy(g, close=0.0):
    g = shapely.make_valid(g)
    if close: g = g.buffer(close, join_style='mitre').buffer(-close, join_style='mitre')
    g = g.simplify(SIMPLIFY, preserve_topology=True)
    polys = [p for p in getattr(g, 'geoms', [g]) if isinstance(p, Polygon) and not p.is_empty]
    polys += [q for p in getattr(g, 'geoms', []) if isinstance(p, MultiPolygon) for q in p.geoms]
    # drop slivers below about 1 km² and holes below about 4 km²
    keep = []
    for p in polys:
        if abs(GEOD.geometry_area_perimeter(p)[0]) < 1e6: continue
        holes = [h for h in p.interiors if abs(GEOD.geometry_area_perimeter(Polygon(h))[0]) >= 4e6]
        keep.append(Polygon(p.exterior, holes))
    if not keep: raise ValueError('geometry vanished after tidy')
    g = MultiPolygon(keep) if len(keep) > 1 else keep[0]
    return shapely.set_precision(g, 1e-4)


def area_km2(g):
    return abs(GEOD.geometry_area_perimeter(g)[0]) / 1e6


def geojson(g):
    m = mapping(g)
    def rnd(c):
        return [rnd(x) for x in c] if isinstance(c[0], (list, tuple)) else [round(c[0], 4), round(c[1], 4)]
    return {'type': m['type'], 'coordinates': rnd(m['coordinates'])}


# --------------------------------------------------------------------------------------------- land and countries
class Countries:
    def __init__(self):
        from pyogrio.raw import read
        meta, fids, geoms, fields = read('/vsizip/' + str(NE).replace('\\', '/'), columns=['ADMIN'])
        self.names = list(fields[0])
        self.geoms = [shapely.make_valid(shapely.from_wkb(w)) for w in geoms]
        self.tree = shapely.STRtree(self.geoms)
        self.land = None

    def at(self, lon, lat, tol=0.0):
        pt = Point(lon, lat)
        hits = [i for i in self.tree.query(pt.buffer(tol) if tol else pt) if self.geoms[i].intersects(pt.buffer(tol) if tol else pt)]
        if not hits: return None
        if len(hits) == 1: return self.names[hits[0]]
        return min(hits, key=lambda i: self.geoms[i].distance(pt)) and self.names[min(hits, key=lambda i: self.geoms[i].distance(pt))]

    def overlap(self, g):
        """Countries a polygon overlaps, by share of its area, and the share on land."""
        out = Counter(); total = area_km2(g)
        for i in self.tree.query(g):
            if not self.geoms[i].intersects(g): continue
            inter = self.geoms[i].intersection(g)
            if inter.is_empty: continue
            a = area_km2(inter)
            if a > 0: out[self.names[i]] += a / total
        return out


# --------------------------------------------------------------------------------------------- build
def era(age):
    return ERA.get(age.split('-')[0].split('/')[0], None)


def age_check(layer, codes, age):
    """0060's layer rules for the entry's ores. Returns '' when fine, else the conflict."""
    e = era(age); first = age.split('-')[0]
    msgs = []
    for c in codes:
        if c == 'lignite' and e not in ('Cenozoic', 'Mesozoic'): msgs.append('lignite must be Cenozoic or Mesozoic')
        if c == 'bituminouscoal' and not (e in ('Cenozoic', 'Mesozoic') or first in DEVONIAN_OR_LATER): msgs.append('bituminous coal must be Devonian or later')
        if c == 'anthracite' and not (e == 'Mesozoic' or first in DEVONIAN_OR_LATER): msgs.append('anthracite must be Devonian or later Paleozoic, or Mesozoic')
        if c in ('borax', 'kernite') and e != 'Cenozoic': msgs.append('borates must be Cenozoic evaporites')
        if c in ('halite', 'sylvite', 'earth-gypsum') and e == 'Precambrian': msgs.append(c + ': evaporite or sedimentary bed must not be Precambrian')
    return '; '.join(sorted(set(msgs)))


def main():
    countries = Countries()
    srcids = {s['id'] for s in tables.SOURCES}
    areas_out, deposits_out, evidence, checks = [], [], [], []
    by_country = defaultdict(lambda: defaultdict(float))  # layer -> country -> Mt
    implied_rows = []
    errors = []

    geoms = {}
    for a in tables.AREAS:
        try:
            geoms[a['id']] = tidy(resolve(a['geom']), a['geom'].get('close', 0.0))
        except Exception as ex:
            errors.append((a['id'], repr(ex)[:300]))
    for keep, cut in tables.OVERLAP_CUTS:
        assert keep in geoms and cut in geoms, (keep, cut)
        if geoms[cut].intersects(geoms[keep]):
            geoms[cut] = tidy(geoms[cut].difference(geoms[keep]))

    for a in tables.AREAS:
        for s in a['src']: assert s in srcids, (a['id'], s)
        assert a['age'].split('-')[0] in AGES, (a['id'], a['age'])
        if a['id'] not in geoms: continue
        g = geoms[a['id']]
        km2 = area_km2(g)
        beds = [dict(b) for b in a['beds']]
        dens = tables.DENSITY
        est = set(a.get('est', []))
        derived = None
        if a.get('mt'):
            main = [b for b in beds if b['code'] in a.get('mtCodes', tables.MT_CODES[a['layer']])]
            # tonnage the beds give at their authored cover; scale the cover so the beds hold the published tonnage
            base = sum(km2 * 1e6 * b['thickM'] * (b.get('cover') or 1.0) * dens[b['code']] for b in main) / 1e6
            s = a['mt'] / base if base else 0
            for b in main:
                c0 = b.get('cover') or 1.0
                b['cover'] = round(min(1.0, max(0.0005, c0 * s)), 4)
            est.add('cover')
            got = sum(km2 * 1e6 * b['thickM'] * b['cover'] * dens[b['code']] for b in main) / 1e6
            derived = {'scale': round(s, 3), 'impliedMt': round(got), 'publishedMt': a['mt'], 'ratio': round(got / a['mt'], 2)}
            implied_rows.append({'id': a['id'], 'name': a['name'], 'areaKm2': round(km2), **derived})
        for b in beds:
            b.setdefault('cover', 1.0)
            b.setdefault('grade', None)
        codes = [b['code'] for b in beds]
        conflict = age_check(a['layer'], codes, a['age'])
        ov = countries.overlap(g)
        land = sum(ov.values())
        top = ov.most_common(1)[0][0] if ov else None
        out = {'id': a['id'], 'name': a['name'], 'layer': a['layer'], 'age': a['age'], 'src': a['src'][0],
               'geometry': geojson(g), 'beds': [{k: b[k] for k in ('code', 'topM', 'thickM', 'cover', 'grade')} for b in beds]}
        if a.get('mt'): out['mt'] = a['mt']
        if a.get('grade') is not None: out['grade'] = a['grade']; out['unit'] = a['unit']
        out['q'] = a['q']
        if est: out['est'] = sorted(est)
        if len(a['src']) > 1: out['srcAlso'] = a['src'][1:]
        if a.get('worked'): out['worked'] = a['worked']
        out['country'] = a['country']
        areas_out.append(out)
        if a.get('mt'): by_country[a['layer']][a['country']] += a['mt']
        checks.append({'kind': 'area', 'id': a['id'], 'name': a['name'], 'layer': a['layer'], 'age': a['age'], 'codes': codes,
                       'country': a['country'], 'largestOverlap': top, 'landShare': round(land, 3), 'areaKm2': round(km2),
                       'vertices': len(shapely.get_coordinates(g)), 'ageConflict': conflict, 'mt': a.get('mt'), 'q': a['q'],
                       'countryOk': (top == a['country'] or a['country'] in ov), 'derived': derived})
        if not a.get('noEvidence'):
            # Buried basins (top of the shallowest bed at 300 m or more) go to the deep plane with their depth, so the
            # background does not put surface deposits over a basin whose beds are deep.
            main = [b for b in beds if b['code'] in tables.MT_CODES[a['layer']]] or beds
            top = min(b['topM'] for b in main)
            deep = {'depthM': top} if top >= 300 else {}
            # Richness: a giant thin basin (Surat, Gulf lignite, Amazonas potash) must not be as hot as the Ruhr.
            # Coal and potash weights scale with published Mt per km2: 3+ Mt/km2 full weight, 1 -> 0.8, 0.1 -> 0.45,
            # 0.03 or less -> 0.25. Other layers keep the confidence weight (their areas are worked districts).
            rich = 1.0
            if a.get('mt') and a['layer'] in ('C_coal', 'K'):
                rich = min(1.0, max(0.25, 0.8 + 0.35 * math.log10(a['mt'] / km2)))
            w = round(tables.AREA_WEIGHT[a['q']] * rich, 3)
            checks[-1]['evidenceWeight'] = w
            evidence.append({'type': 'Feature', 'geometry': geojson(g), 'properties': {
                'code': a['layer'], 'source': 'coal-evaporites catalogue', 'plane': 'obs', 'weight': w,
                'id': a['id'], 'name': a['name'], 'group': a['id'], **deep}})
            for extra in a.get('alsoCodes', []):
                xb = [b for b in beds if b['code'] in tables.MT_CODES[extra]] or beds
                xt = min(b['topM'] for b in xb)
                evidence.append({'type': 'Feature', 'geometry': geojson(g), 'properties': {
                    'code': extra, 'source': 'coal-evaporites catalogue', 'plane': 'obs', 'weight': tables.AREA_WEIGHT[a['q']] * .8,
                    'id': a['id'] + ':' + extra, 'name': a['name'], 'group': a['id'] + ':' + extra, **({'depthM': xt} if xt >= 300 else {})}})

    for d in tables.DEPOSITS:
        for s in d['src']: assert s in srcids, (d['id'], s)
        assert d['age'].split('-')[0] in AGES, (d['id'], d['age'])
        assert abs(sum(o['share'] for o in d['ores']) - 1) < 1e-6, d['id']
        codes = [o['code'] for o in d['ores']]
        conflict = age_check(d['layer'], codes, d['age'])
        found = countries.at(d['lon'], d['lat'])
        near = found or countries.at(d['lon'], d['lat'], 0.1)
        out = {k: d[k] for k in ('id', 'name', 'lat', 'lon', 'layer', 'ores', 'shape', 'mt') if k in d}
        out['grade'] = d.get('grade'); out['unit'] = d.get('unit')
        for k in ('density', 'topM', 'baseM', 'lenKm', 'widKm', 'strikeDeg', 'dipDeg'):
            if d.get(k) is not None: out[k] = d[k]
        if d.get('layers'):
            assert set(d['layers']) <= LAYER_CLASSES, (d['id'], d['layers'])
            out['layers'] = d['layers']
        out['age'] = d['age']; out['src'] = d['src'][0]; out['q'] = d['q']
        if d.get('est'): out['est'] = d['est']
        if len(d['src']) > 1: out['srcAlso'] = d['src'][1:]
        if d.get('worked'): out['worked'] = d['worked']
        out['country'] = d['country']
        deposits_out.append(out)
        main_mt = d.get('mainMt') or d['mt']
        by_country[d['layer']][d['country']] += main_mt
        checks.append({'kind': 'deposit', 'id': d['id'], 'name': d['name'], 'layer': d['layer'], 'age': d['age'], 'codes': codes,
                       'country': d['country'], 'foundCountry': near, 'onLand': found is not None, 'ageConflict': conflict,
                       'mt': main_mt, 'bodyMt': d['mt'], 'q': d['q'], 'countryOk': near == d['country'], 'layers': d.get('layers')})
        if not d.get('noEvidence'):
            tier = tables.DEPOSIT_TIER(d)
            for code in [d['layer']] + d.get('alsoCodes', []):
                evidence.append({'type': 'Feature', 'geometry': {'type': 'Point', 'coordinates': [d['lon'], d['lat']]}, 'properties': {
                    'code': code, 'source': 'coal-evaporites catalogue', 'plane': 'obs', 'tier': tier,
                    'id': d['id'] + ('' if code == d['layer'] else ':' + code), 'name': d['name'], 'country': d['country'],
                    **({'depthM': d['topM']} if (d.get('topM') or 0) >= 300 else {})}})

    for e in tables.EVIDENCE_ONLY:
        props = {k: v for k, v in e.items() if k not in ('geometry', 'lon', 'lat')}
        props.setdefault('source', 'coal-evaporites evidence')
        if 'ring' in e:
            geom = geojson(tidy(Polygon(e['ring']))); props.setdefault('weight', 0.6)
        else:
            geom = {'type': 'Point', 'coordinates': [e['lon'], e['lat']]}
        props.pop('ring', None)
        evidence.append({'type': 'Feature', 'geometry': geom, 'properties': props})
        if 'lon' in e:
            found = countries.at(e['lon'], e['lat'], 0.1)
            checks.append({'kind': 'evidence', 'id': e.get('id'), 'name': e.get('name'), 'layer': e['code'], 'country': e.get('country'),
                           'foundCountry': found, 'countryOk': found == e.get('country') if e.get('country') else None})

    if errors:
        for e in errors: print('ERROR', *e)
        sys.exit(1)
    import evidence_fixes
    fixes = evidence_fixes.build(tidy, geojson)
    evidence.extend(fixes['features'])
    ids = [x['id'] for x in areas_out + deposits_out]
    dup = [k for k, v in Counter(ids).items() if v > 1]
    assert not dup, dup
    cat = {'version': 1, 'group': 'coal-evaporites', 'sources': tables.SOURCES, 'gradeBands': tables.GRADE_BANDS,
           'deposits': deposits_out, 'areas': areas_out}
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(cat, separators=(',', ':'), ensure_ascii=False) + '\n', encoding='utf-8')
    EVIDENCE.write_text(json.dumps({'type': 'FeatureCollection', 'features': evidence}, separators=(',', ':'), ensure_ascii=False) + '\n', encoding='utf-8')
    size = OUT.stat().st_size
    assert size <= 3 * 1024 * 1024, size
    val = {'catalogueBytes': size, 'areas': len(areas_out), 'deposits': len(deposits_out), 'evidenceFeatures': len(evidence),
           'byLayer': Counter(x['layer'] for x in areas_out + deposits_out),
           'mtByLayerCountry': {k: dict(sorted(v.items(), key=lambda kv: -kv[1])) for k, v in by_country.items()},
           'impliedTonnage': implied_rows, 'evidenceFixes': fixes['summary'], 'checks': checks,
           'ageConflicts': [c for c in checks if c.get('ageConflict')],
           'countryProblems': [c for c in checks if c.get('countryOk') is False],
           'offLand': [c for c in checks if c.get('onLand') is False or (c.get('landShare') is not None and c['landShare'] < .5)]}
    VALIDATION.write_text(json.dumps(val, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
    print(json.dumps({'bytes': size, 'areas': len(areas_out), 'deposits': len(deposits_out), 'evidence': len(evidence),
                      'ageConflicts': len(val['ageConflicts']), 'countryProblems': len(val['countryProblems']),
                      'offLand': len(val['offLand'])}))
    for c in val['countryProblems'] + val['offLand']:
        print('CHECK', c['kind'], c['id'], c.get('country'), c.get('foundCountry') or c.get('largestOverlap'), c.get('landShare'))
    for r in implied_rows:
        if r['ratio'] < .8 or r['ratio'] > 1.25: print('TONNAGE', r)


if __name__ == '__main__':
    main()
