"""Heat-map evidence for the base-metals layer codes (decision 0065): evidence.geojson and heatmap-overrides.json.

* Every catalogue deposit stamps its own code and every by-product code it carries at or above half that metal's medium
  grade band. Peak and kernel radius follow its contained metal (size classes below), so the background between the
  catalogue's bodies is strongest in real districts. Bodies whose top is 300 m or deeper stamp the deep plane at that
  depth (Lubin, Olympic Dam, Resolution...).
* Catalogue areas (Kupferschiefer, Copperbelt) stamp their polygons (observed plane, or deep plane when the bed is deep).
* Tin and tungsten provinces (potential plane): the granite-related Sn-W belts of the world, drawn coarsely. They carry
  the geological potential for Sn and W where the generic granite prior is proposed to be dropped (see the research
  note): tin is not found in most granite.
* The authored best-guess districts (docs/research/remaining-ores-2026-09-29/corrections/authored) are dropped for this
  group's codes by heatmap-overrides.json; the ones the catalogue does not yet cover are carried over unchanged here, so
  nothing is lost and their provenance stays visible.
Run after build_catalogue.py.
"""
import csv, json, math, sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *  # noqa: F401,F403

SRC = 'base-metals catalogue'
# Contained metal (Mt; Ag in Mt too) of the size classes: (world-class, large, deposit). Below: small.
SIZE = {'Cu': (5, .5, .05), 'Zn': (3, .5, .05), 'Pb': (2, .3, .03), 'Ag': (.005, .001, .0001), 'Sn': (.3, .05, .005),
        'W': (.3, .05, .005), 'Mo': (.5, .05, .005), 'Sb': (.3, .05, .005), 'Bi': (.03, .005, .0005), 'Hg': (.03, .003, .0003)}
CLASS = [('world-class', .95, 60), ('large', .85, 50), ('deposit', .75, 40), ('small', .6, 30)]
SETTINGS = json.loads((ROOT / 'tools/ore-heatmap-settings.json').read_text())
BEST_GUESS = ATLAS / 'corrections/authored/best-guess-districts.csv'

# Granite-related tin and tungsten provinces (lon/lat ellipses: centre, semi-axes km, strike). Coarse outlines of the belts
# in standard reviews (e.g. Lehmann 1990 Metallogeny of Tin; Schwarz-Schampera 2014 for tin provinces; Werner et al. 2014 for
# tungsten). Potential plane, weight 0.3: permissive ground, not deposits.
PROVINCES = [
    ('Cornubian batholith', 50.4, -4.6, 120, 35, 70, 'Sn,W'),
    ('Iberian Sn-W belt (Galicia, Tras-os-Montes, Beira)', 41.3, -7.6, 280, 90, 150, 'Sn,W'),
    ('Massif Central and Brittany Sn-W', 46.5, 0.5, 300, 120, 120, 'Sn,W'),
    ('Erzgebirge-Slavkovsky les', 50.5, 13.0, 110, 35, 65, 'Sn,W'),
    ('Southeast Asian tin belt (Myanmar-Thailand)', 13.5, 98.8, 900, 120, 170, 'Sn,W'),
    ('Southeast Asian tin belt (Malaysia-Bangka-Belitung)', 1.5, 104.0, 950, 160, 140, 'Sn,W'),
    ('Yunnan-Guangxi tin (Gejiu, Dachang, Dulong)', 23.8, 105.5, 330, 90, 80, 'Sn,W'),
    ('Nanling W-Sn belt (Hunan, Jiangxi, Guangdong)', 25.5, 113.5, 420, 150, 80, 'Sn,W,Bi,Mo'),
    ('Jiangnan W belt (Zhuxi, Dahutang)', 29.0, 116.5, 300, 70, 70, 'W,Mo'),
    ('Central Andean tin belt (Bolivia, southern Peru)', -18.5, -66.8, 650, 110, 160, 'Sn,W,Bi,Ag'),
    ('Rondonia tin province', -10.0, -63.0, 300, 150, 100, 'Sn'),
    ('Pitinga (Amazonas)', -0.8, -60.1, 80, 60, 0, 'Sn'),
    ('Jos Plateau tin fields', 9.6, 8.9, 140, 80, 0, 'Sn'),
    ('Kibaran tin-tungsten belt (Rwanda, Kivu, Maniema, Manono)', -3.5, 28.0, 650, 120, 160, 'Sn,W'),
    ('Erongo tin (Uis)', -21.2, 14.9, 120, 60, 40, 'Sn'),
    ('Bushveld granite tin (Zaaiplaats)', -24.6, 28.8, 150, 70, 90, 'Sn'),
    ('Tasmania Sn-W (Renison, Mount Bischoff, King Island)', -41.6, 145.5, 200, 90, 160, 'Sn,W'),
    ('Herberton and New England tin (eastern Australia)', -23.0, 148.5, 1200, 130, 160, 'Sn,W'),
    ('Russian Far East tin (Komsomolsk, Badzhal, Kavalerovo)', 47.5, 136.5, 600, 140, 30, 'Sn,W'),
    ('Yana-Kolyma tin (Deputatsky, Ege-Khaya, Pevek)', 68.5, 145.0, 900, 200, 110, 'Sn,W'),
    ('Transbaikalia Sn-W-Mo (Sherlovaya Gora, Dzhida, Khapcheranga)', 50.5, 112.0, 650, 180, 60, 'Sn,W,Mo'),
    ('Seward Peninsula tin (Lost River)', 65.4, -167.0, 120, 50, 90, 'Sn,W'),
    ('Korean tungsten (Sangdong)', 37.1, 128.8, 120, 60, 40, 'W,Mo'),
    ('Northern Cordillera tungsten belt (Cantung, Mactung)', 62.5, -129.5, 300, 70, 140, 'W'),
    ('Eastern Alps tungsten (Felbertal)', 47.2, 12.4, 120, 40, 90, 'W'),
    ('Kazakhstan and Tian Shan W-Mo (Verkhne-Kairaktinsk, Ustarasai)', 45.5, 72.0, 700, 150, 70, 'W,Mo'),
    ('Kalba-Narym tin-tantalum (East Kazakhstan)', 49.3, 83.5, 220, 70, 120, 'Sn'),
    ('Mongolian Altai and Khentii Sn-W', 48.5, 108.5, 400, 120, 60, 'Sn,W'),
]


# Minor metals whose MRDS records are mostly small US occurrences (US share of point records 49-88 %): MRDS is dropped for
# them and re-stamped here per 0.5-degree cell with a cap below the district level (0.5), so MRDS alone never makes a
# district; producers keep more weight than occurrences. Catalogue deposits, specialist compilations and provinces stay higher.
MINOR = ['Sn', 'W', 'Mo', 'Bi', 'Hg', 'Sb']
MRDS_CAP = {'producers3': .45, 'producer': .35, 'other': .25, 'radiusKm': 25}


def mrds_minor():
    import gzip
    feats = []; stats = Counter()
    for code in MINOR:
        recs = []
        if code == 'Sn':
            with open(CUTIN / 'mrds-copper-tin-occurrences.csv', encoding='utf-8-sig') as f:
                for r in csv.DictReader(f):
                    if r['commodity'] == 'Sn' and r['heatmap_eligible'] == 'True':
                        recs.append((r['source_feature_id'], float(r['latitude']), float(r['longitude']), r['source_status'], r['country'], r['name'], False))
        else:
            with gzip.open(ATLAS / 'normalized' / (code + '-occurrences.csv.gz'), 'rt', encoding='utf-8') as f:
                for r in csv.DictReader(f):
                    recs.append((r['source_feature_id'], float(r['latitude']), float(r['longitude']), r['source_status'], r['country'], r['name'],
                                 r['evidence_basis'] == 'explicit_ore_mineral_only'))
        names_at = {}
        for fid, la, lo, st, c, nm, _ in recs: names_at.setdefault((la, lo), set()).add((nm or '').strip().casefold())
        cells = {}
        seen = set()
        for fid, la, lo, st, c, nm, mention in recs:
            if fid in seen: continue
            seen.add(fid)
            if len(names_at[(la, lo)]) >= 5: stats[code + ' placeholder stack'] += 1; continue
            k = (int((90 - la) * 2), int((lo + 180) * 2))
            cell = cells.setdefault(k, {'n': 0, 'prod': 0, 'lat': 0.0, 'lon': 0.0, 'country': Counter()})
            cell['n'] += 1; cell['lat'] += la; cell['lon'] += lo; cell['country'][c] += 1
            if st in ('Producer', 'Past Producer') and not mention: cell['prod'] += 1
        for k, cell in cells.items():
            peak = MRDS_CAP['producers3'] if cell['prod'] >= 3 else MRDS_CAP['producer'] if cell['prod'] else MRDS_CAP['other']
            p = {'code': code, 'source': 'base-metals mrds capped', 'peak': peak, 'radiusKm': MRDS_CAP['radiusKm'],
                 'records': cell['n'], 'producers': cell['prod']}
            country = cell['country'].most_common(1)[0][0]
            if country: p['country'] = country
            feats.append({'type': 'Feature', 'geometry': {'type': 'Point', 'coordinates': [round(cell['lon'] / cell['n'], 3), round(cell['lat'] / cell['n'], 3)]},
                          'properties': p})
            stats[code + ' mrds cells'] += 1
    return feats, stats


def size_class(code, c):
    t = SIZE[code]
    i = 0 if c >= t[0] else 1 if c >= t[1] else 2 if c >= t[2] else 3
    return CLASS[i]


def contained(mt, code, g):
    return mt * g / (1e6 if UNIT.get(code) == 'g/t' else 100)


def main():
    cat = json.loads(CATALOGUE.read_text(encoding='utf-8'))
    feats = []; stats = Counter(); covered = []
    for d in cat['deposits']:
        codes = {d['layer']}
        for m, g in d.get('grades', {}).items():
            if m in CODES and g >= GRADE_BANDS[m]['medium'] / 2: codes.add(m)  # gold's heat map is the precious group's
        for code in sorted(codes):
            g = d['grades'].get(code)
            if not g: continue
            c = contained(d['mt'] - d.get('gossanMt', 0), code, g)  # the gossan part carries no base metals
            name, peak, radius = size_class(code, c)
            if code != d['layer']: peak = max(.5, peak - .1)  # by-products: one step weaker
            p = {'code': code, 'source': SRC, 'peak': peak, 'radiusKm': radius, 'id': d['id'], 'name': d['name'],
                 'size': name, 'containedMt': round(c, 6)}
            if d.get('country'): p['country'] = d['country']
            if d.get('topM', 0) >= 300: p['depthM'] = round(d['topM'])
            feats.append({'type': 'Feature', 'geometry': {'type': 'Point', 'coordinates': [d['lon'], d['lat']]}, 'properties': p})
            stats[code + ' ' + name] += 1
            covered.append((code, d['lat'], d['lon']))
    for a in cat['areas']:
        top = min(b['topM'] for b in a['beds'])
        for code in [m for m in a.get('grades', {}) if m in CODES]:
            p = {'code': code, 'source': SRC, 'weight': .8 if code == a['layer'] else .6, 'id': a['id'], 'name': a['name']}
            if top >= 300: p['depthM'] = round(top)
            feats.append({'type': 'Feature', 'geometry': a['geometry'], 'properties': p})
            stats[code + ' area'] += 1
            from shapely.geometry import shape
            c = shape(a['geometry']).centroid
            covered.append((code, c.y, c.x))
    for name, lat, lon, a_km, b_km, strike, codes in PROVINCES:
        ring = ellipse(lat, lon, a_km, b_km, strike, 36)
        for code in codes.split(','):
            feats.append({'type': 'Feature', 'geometry': {'type': 'Polygon', 'coordinates': [ring]},
                          'properties': {'code': code, 'source': 'base-metals Sn-W provinces', 'plane': 'pot', 'weight': .3, 'name': name}})
            stats[code + ' province'] += 1
    # best-guess districts the catalogue does not cover yet: carried over (the source is dropped for these codes)
    ab = SETTINGS['authoredBestGuess']
    carried = []
    with open(BEST_GUESS, encoding='utf-8') as f:
        for r in csv.DictReader(f):
            for code in [c for c in r['commodities'].split(';') if c in CODES]:
                lat, lon, rad = float(r['lat']), float(r['lon']), float(r['radius_km'])
                near = any(cc == code and haversine_km(lat, lon, la, lo) <= max(40, rad) for cc, la, lo in covered)
                if near:
                    stats[code + ' best-guess superseded'] += 1; continue
                peak = min(ab['peak'][r['size']][r['confidence']], ab['maxPeak'])
                feats.append({'type': 'Feature', 'geometry': {'type': 'Point', 'coordinates': [lon, lat]},
                              'properties': {'code': code, 'source': 'best-guess carried (base-metals)', 'peak': peak,
                                             'radiusKm': min(rad, ab['maxRadiusKm']), 'name': r['name'], 'country': r['country']}})
                carried.append({'code': code, 'name': r['name']})
                stats[code + ' best-guess carried'] += 1
    mf, ms = mrds_minor()
    feats += mf; stats.update(ms)
    EVIDENCE.write_text(json.dumps({'type': 'FeatureCollection', 'name': 'base-metals evidence (decision 0065)', 'features': feats},
                                   separators=(',', ':'), ensure_ascii=False) + '\n', encoding='utf-8')
    # the bake names MRDS sources by file: 'mrds' matches only names starting with mrds, so the per-commodity files are listed too
    drop = {c: ['best-guess-districts.geojson'] + (['mrds', c + '-occurrences.csv.gz'] if c in MINOR else []) for c in CODES}
    OVERRIDES.write_text(json.dumps({'drop': drop,
                                     'why': 'Authored best-guess districts are replaced for these codes by catalogue evidence with real tonnage '
                                            '(evidence.geojson, source "base-metals catalogue"); districts the catalogue does not cover are carried '
                                            'over unchanged as source "best-guess carried (base-metals)". For the minor metals (' + ', '.join(MINOR) +
                                            ') MRDS records are re-stamped per cell with a cap below the district level (source "base-metals mrds capped").'},
                                    indent=1) + '\n', encoding='utf-8')
    rep = json.loads(REPORT.read_text(encoding='utf-8')) if REPORT.exists() else {}
    rep['evidence'] = {'features': len(feats), 'stats': dict(sorted(stats.items())), 'bestGuessCarried': carried}
    REPORT.write_text(json.dumps(rep, indent=1, ensure_ascii=False), encoding='utf-8')
    print(len(feats), dict(sorted(stats.items())))
    print('carried', carried)


if __name__ == '__main__':
    main()
