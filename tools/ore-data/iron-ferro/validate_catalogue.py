"""Validate assets/earth/config/ore-deposits-v1/iron-ferro.json.

* totals by country and layer (ore and contained metal) against USGS Mineral Commodity Summaries 2025 reserves;
* the 20 largest entries;
* coordinates on land and inside their country (Natural Earth 1:10m admin-0, already on disk for the heat-map bake);
* ages, shapes and ore codes against decision 0060's layer rules.

Writes tools/ore-data/iron-ferro/validation.json and validation.md (tables used in the research note).
Run:  C:\\VS mods\\Earth\\.venv\\Scripts\\python.exe -I tools/ore-data/iron-ferro/validate_catalogue.py
"""
from pathlib import Path
from collections import defaultdict
import json

import shapely
from shapely.geometry import shape, Point

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
CAT = ROOT / 'assets/earth/config/ore-deposits-v1/iron-ferro.json'
NE = ROOT / 'docs/research/remaining-ores-2026-09-29/raw/naturalearth/ne_10m_admin_0_countries.zip'

# USGS MCS 2025 reserves (footnote markers removed). Units: Fe crude ore Mt; Mn kt Mn content; Cr kt shipping-grade
# chromite (45 % Cr2O3; Finland 26 %, US 7 %); Ni t Ni; Ti kt TiO2 (ilmenite + rutile); V kt V; Pt kg PGM; Al kt bauxite.
MCS = {
    'Fe': {'United States': 3600, 'Australia': 58000, 'Brazil': 34000, 'Canada': 6000, 'China': 20000, 'India': 5500, 'Iran': 3800,
           'Kazakhstan': 2500, 'Peru': 2600, 'Russia': 35000, 'South Africa': 930, 'Sweden': 1300, 'Turkey': 150, 'Ukraine': 6500, 'World': 200000},
    'Mn': {'Australia': 500000, 'Brazil': 270000, 'China': 280000, 'Gabon': 61000, 'Ghana': 13000, 'India': 34000, 'South Africa': 560000, 'World': 1700000},
    'Cr': {'United States': 630, 'Brazil': 6600, 'Finland': 8300, 'India': 79000, 'Kazakhstan': 320000, 'South Africa': 200000, 'Turkey': 27000,
           'Zimbabwe': 540000, 'World': 1200000},
    'Ni': {'United States': 310000, 'Australia': 24000000, 'Brazil': 16000000, 'Canada': 2200000, 'China': 4400000, 'Indonesia': 55000000,
           'New Caledonia': 7100000, 'Philippines': 4800000, 'Russia': 8300000, 'World': 130000000},
    'Ti': {'United States': 2000, 'Australia': 215000, 'Canada': 51000, 'China': 110000, 'India': 15670, 'Madagascar': 30000, 'Norway': 37000,
           'South Africa': 34100, 'Ukraine': 5900, 'Sierra Leone': 2900, 'Mozambique': 720, 'World': 556000},
    'V': {'United States': 45, 'Australia': 8500, 'Brazil': 120, 'China': 4100, 'Russia': 5000, 'South Africa': 430, 'World': 18000},
    'Pt': {'United States': 820000, 'Canada': 310000, 'Russia': 16000000, 'South Africa': 63000000, 'Zimbabwe': 1200000, 'World': 81000000},
    'Al': {'United States': 20000, 'Australia': 3500000, 'Brazil': 2700000, 'China': 680000, 'Guinea': 7400000, 'India': 650000, 'Indonesia': 2800000,
           'Jamaica': 2000000, 'Kazakhstan': 280000, 'Russia': 480000, 'Saudi Arabia': 180000, 'Turkey': 69000, 'Vietnam': 3100000, 'World': 29000000},
}
MCS_UNIT = {'Fe': 'Mt crude ore', 'Mn': 'kt Mn', 'Cr': 'kt chromite at 45 % Cr2O3', 'Ni': 't Ni', 'Ti': 'kt TiO2', 'V': 'kt V', 'Pt': 'kg PGM', 'Al': 'kt bauxite'}
WORLD_RESOURCES = {'Fe': '>800 Gt ore (>230 Gt Fe)', 'Mn': 'large; South Africa about 70 %', 'Cr': '>12 Gt shipping-grade chromite', 'Ni': '>350 Mt Ni',
                   'Ti': '>2 Gt of Ti minerals', 'V': '>63 Mt V', 'Pt': '>100,000 t PGM', 'Al': '55-75 Gt bauxite'}


def in_mcs_units(layer, mt, grade):
    """Catalogue ore Mt + grade -> the MCS reserve unit."""
    if layer == 'Fe': return mt
    if layer == 'Mn': return mt * grade / 100 * 1000
    if layer == 'Cr': return mt * grade / 45 * 1000
    if layer == 'Ni': return mt * grade / 100 * 1e6
    if layer == 'Ti': return mt * grade / 100 * 1000
    if layer == 'V': return mt * grade / 100 * 0.56 * 1000
    if layer == 'Pt': return mt * grade * 1000  # Mt x g/t = t; x1000 = kg
    if layer == 'Al': return mt * 1000


COUNTRY_ALIASES = {'Kosovo (Serbia)': 'Kosovo', 'Territory of New Caledonia and Dependencies': 'New Caledonia', 'Burma': 'Myanmar',
                   'Macedonia': 'North Macedonia', 'Congo (Kinshasa)': 'Democratic Republic of the Congo', 'USA': 'United States',
                   'Ivory Coast': "Cote d'Ivoire"}
NE_TO_NAME = {'United States of America': 'United States', 'Republic of Serbia': 'Serbia', 'United Republic of Tanzania': 'Tanzania',
              'Czechia': 'Czech Republic', 'eSwatini': 'Swaziland', 'Republic of the Congo': 'Congo', "Côte d'Ivoire": "Cote d'Ivoire",
              'Ivory Coast': "Cote d'Ivoire"}


def canon(name):
    name = (name or '').strip()
    return COUNTRY_ALIASES.get(name, name)


def main():
    cat = json.loads(CAT.read_text(encoding='utf-8'))
    from pyogrio.raw import read
    _, _, wkb, cols = read('/vsizip/' + NE.as_posix() + '/ne_10m_admin_0_countries.shp', columns=['ADMIN', 'SOVEREIGNT', 'NAME'])
    geoms = shapely.from_wkb(wkb); tree = shapely.STRtree(geoms)
    names = [(str(cols[0][i]), str(cols[1][i]), str(cols[2][i])) for i in range(len(geoms))]

    def where(lon, lat):
        p = Point(lon, lat)
        hit = tree.query(p, predicate='intersects')
        if len(hit): return names[hit[0]], 0.0
        i = tree.nearest(p); return names[i], float(shapely.distance(geoms[i], p))

    totals = defaultdict(lambda: defaultdict(lambda: [0.0, 0.0, 0]))  # layer -> country -> [ore Mt, mcs units, entries]
    land, entries, ages = [], [], []
    hard = {'hematite', 'magnetite', 'limonite', 'chromite', 'ilmenite', 'pentlandite', 'rhodochrosite', 'sperrylite', 'vanadinite', 'chalcopyrite',
            'fluorite'}
    cover_classes = {'alluvial', 'outwash', 'peat', 'laterite', 'saprolite', 'playa', 'deep-cover', 'cover'}  # 0060 cover layers
    for d in cat['deposits']:
        c = canon(d.get('country'))
        t = totals[d['layer']][c]; t[0] += d['mt']; t[1] += in_mcs_units(d['layer'], d['mt'], d['grade'] or 0); t[2] += 1
        entries.append((d['mt'], d['layer'], d['name'], c, d['grade'], d['unit'], d['shape'], d['src'], d['q']))
        (adm, sov, nm), dist = where(d['lon'], d['lat'])
        ok_country = c in (adm, sov, nm, NE_TO_NAME.get(adm), NE_TO_NAME.get(sov)) or c.split(' (')[0] in (adm, sov, nm)
        if dist > 0 or not ok_country:
            land.append({'id': d['id'], 'name': d['name'], 'country': c, 'lat': d['lat'], 'lon': d['lon'], 'ne': adm, 'offshoreKm': round(dist * 111, 1)})
        # 0060 layer rules as 0065 applies them to catalogue bodies: 'layers' replaces the per-code table; blankets default to cover
        codes = {o['code'] for o in d['ores']}
        layers = d.get('layers')
        if d['shape'] == 'blanket':
            bad = set(layers or ['alluvial', 'outwash', 'laterite', 'saprolite']) - cover_classes
            if bad: ages.append({'id': d['id'], 'issue': 'blanket in non-cover layers ' + ','.join(sorted(bad))})
        else:
            if 'earth-bauxite' in codes and not layers:
                ages.append({'id': d['id'], 'issue': 'buried bauxite (shape %s, %s) without layers: 0060 allows bauxite only in laterite' % (d['shape'], d['age'])})
            if layers and set(layers) - {'bed', 'basement'}:
                ages.append({'id': d['id'], 'issue': 'rock body in layers ' + ','.join(layers)})
            bad = [x for x in codes if x not in hard and x != 'earth-bauxite']
            if bad: ages.append({'id': d['id'], 'issue': 'rock body with code ' + ','.join(bad)})
        if d['age'] not in ('Cenozoic', 'Mesozoic', 'Paleozoic', 'Devonian', 'Carboniferous', 'Permian', 'Precambrian'):
            ages.append({'id': d['id'], 'issue': 'unknown age ' + d['age']})
        if d['shape'] == 'blanket' and d['age'] not in ('Cenozoic',) and 'earth-bauxite' not in codes:
            ages.append({'id': d['id'], 'issue': 'cover blanket with pre-Cenozoic weathering age %s (kept: old laterite still at the surface)' % d['age']})
    area_land = []
    for a in cat['areas']:
        c = canon(a.get('country'))
        for b in a['beds']:
            t = totals[a['layer']][c]; t[0] += b['mt']; t[1] += in_mcs_units(a['layer'], b['mt'], b['grade'] or 0)
        totals[a['layer']][c][2] += 1
        entries.append((a['mt'], a['layer'], a['name'] + ' (area)', c, a['beds'][0]['grade'], a['unit'], 'area', a['src'], a['q']))
        g = shape(a['geometry']); rp = g.representative_point()
        (adm, sov, nm), dist = where(rp.x, rp.y)
        hits = tree.query(g, predicate='intersects')
        landfrac = float(shapely.area(shapely.intersection(g, shapely.union_all(geoms[hits]))) / g.area) if len(hits) else 0.0
        area_land.append({'id': a['id'], 'country': c, 'ne': adm, 'landShare': round(landfrac, 3)})
    rows = []
    for layer in ['Fe', 'Mn', 'Cr', 'Ni', 'Ti', 'V', 'Pt', 'Al']:
        world = [0.0, 0.0, 0]
        for c, (mt, units, n) in sorted(totals[layer].items(), key=lambda kv: -kv[1][1]):
            world[0] += mt; world[1] += units; world[2] += n
            res = MCS[layer].get(c)
            if res or units >= 0.02 * max(1, sum(v[1] for v in totals[layer].values())):
                rows.append({'layer': layer, 'country': c, 'entries': n, 'oreMt': round(mt, 1), 'catalogue': round(units, 1), 'unit': MCS_UNIT[layer],
                             'mcsReserves': res, 'ratio': round(units / res, 2) if res else None})
        rows.append({'layer': layer, 'country': 'World', 'entries': world[2], 'oreMt': round(world[0], 1), 'catalogue': round(world[1], 1), 'unit': MCS_UNIT[layer],
                     'mcsReserves': MCS[layer]['World'], 'ratio': round(world[1] / MCS[layer]['World'], 2), 'worldResources': WORLD_RESOURCES[layer]})
    entries.sort(key=lambda e: -e[0])
    top = [{'oreMt': e[0], 'layer': e[1], 'name': e[2], 'country': e[3], 'grade': e[4], 'unit': e[5], 'shape': e[6], 'src': e[7], 'q': e[8]} for e in entries[:20]]
    out = {'deposits': len(cat['deposits']), 'areas': len(cat['areas']), 'countryTotals': rows, 'largest20': top,
           'pointsOffLandOrOutsideCountry': land, 'areasLand': area_land, 'layerRuleNotes': ages}
    (HERE / 'validation.json').write_text(json.dumps(out, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
    md = ['| Layer | Country | Entries | Ore Mt | Catalogue | MCS 2025 reserves | Catalogue / reserves |', '|---|---|---:|---:|---:|---:|---:|']
    for r_ in rows:
        md.append('| %s | %s | %d | %s | %s %s | %s | %s |' % (r_['layer'], r_['country'], r_['entries'], f"{r_['oreMt']:,.0f}", f"{r_['catalogue']:,.0f}", r_['unit'],
                                                         f"{r_['mcsReserves']:,}" if r_['mcsReserves'] else '-', r_['ratio'] if r_['ratio'] is not None else '-'))
    md += ['', '| # | Entry | Layer | Country | Ore Mt | Grade | Shape | Source, q |', '|---:|---|---|---|---:|---|---|---|']
    for i, t_ in enumerate(top, 1):
        md.append('| %d | %s | %s | %s | %s | %s %s | %s | %s, %s |' % (i, t_['name'], t_['layer'], t_['country'], f"{t_['oreMt']:,.0f}", t_['grade'], t_['unit'], t_['shape'], t_['src'], t_['q']))
    (HERE / 'validation.md').write_text('\n'.join(md) + '\n', encoding='utf-8')
    print('off land / outside country:', len(land)); print(json.dumps(land[:40], ensure_ascii=False))
    print('areas:', area_land); print('layer notes:', len(ages)); print(json.dumps(ages[:15]))


if __name__ == '__main__':
    main()
