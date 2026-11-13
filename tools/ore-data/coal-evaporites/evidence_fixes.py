"""Heat-map evidence that replaces sources dropped in heatmap-overrides.json (coal and evaporites group, 0065).

What was wrong in ore-heatmaps-v1 (baseline test bake of 8 November 2026) and what replaces it:

* S (native sulfur). MRDS 'Sulfur' records are mostly sulfide mines that sold by-product sulfur or pyrite
  (Pyhasalmi, Palabora, Saxberget, the Witwatersrand gold mines); USGS mineral facilities add refinery and gas-plant
  sulfur; the Afghan additional evidence is sulfide showings. VS sulfur is a native-sulfur deposit in sedimentary
  beds. Dropped: S-occurrences.csv.gz (MRDS), usgs-minfac-mines.geojson, additional-evidence.geojson (S only). Re-added: MRDS records whose
  own text names native sulfur, Frasch, salt-dome cap rock, solfataras or fumaroles and that list no sulfide ore.
* P (phosphorite). P-specialist.geojson mixes the USGS phosphate compilation with Ni-Cr-PGE deposits (usgs_nicrpge,
  platinum read as P) and magmatic apatite (Khibiny, Kovdor, Siilinjarvi, Palabora carbonatites). Dropped
  P-specialist.geojson; re-added only sedimentary, guano-replacement and residual phosphate records and the
  sedimentary major deposits, with the bake's own specialist tiers.
* K (potash). PotashTracts.geojson gave every USGS permissive potash tract the full tract weight (0.5) on the
  potential plane, so sylvite background covered all of East Siberia (1.8 million km2) and the Sahara, where no
  potash is known. Dropped for K; re-added per tract: 0.5 where potash is mined or under development, 0.35 where
  the tract has potash sites or resources, 0.15 otherwise and for any tract over 300,000 km2 without mining. Halite
  keeps the tract weight 0.5 everywhere (all tracts are salt basins), as before through the K->NaCl mirror.
"""
from pathlib import Path
import csv, gzip, json, re

import shapely
from shapely.geometry import shape

ROOT = Path(__file__).resolve().parents[3]
ATLAS = ROOT / 'docs/research/remaining-ores-2026-09-29'

SULFIDE = re.compile(r'pyrite|sphalerite|galena|chalcopyrite|pyrrhotite|arsenopyrite|bornite|chalcocite|marcasite|stibnite|cinnabar', re.I)
NATIVE = re.compile(r'native sulfur|sulfur,? native|native sulphur|frasch|salt dome|cap ?rock|solfatar|fumarol|crater|volcanic sulfur|elemental sulfur|sulfur bed', re.I)
MAGMATIC = re.compile(r'magmatic|carbonatite|alkalic|igneous', re.I)


def tier_of_status(st):
    st = (st or '').casefold()
    if 'producer' in st or 'mine' in st: return 'deposit'
    if 'prospect' in st: return 'prospect'
    return 'occurrence'


def build(tidy, geojson):
    feats, summary = [], {}

    # ---- S: native sulfur from MRDS
    n = 0
    with gzip.open(ATLAS / 'normalized/S-occurrences.csv.gz', 'rt', encoding='utf-8') as f:
        for r in csv.DictReader(f):
            if 'Sulfur' not in (r['matching_source_commodity_labels'] or '').split(';'): continue
            text = ' '.join(r[k] or '' for k in ('name', 'deposit_style', 'host_geology', 'ore_text', 'occurrence_form', 'commodity_labels'))
            if SULFIDE.search(r['explicit_ore_minerals'] or '') or SULFIDE.search(r['ore_text'] or ''): continue
            if not NATIVE.search(text): continue
            feats.append({'type': 'Feature', 'geometry': {'type': 'Point', 'coordinates': [float(r['longitude']), float(r['latitude'])]},
                          'properties': {'code': 'S', 'source': 'native-sulfur-from-mrds', 'plane': 'obs', 'tier': tier_of_status(r['source_status']),
                                         'id': r['source_feature_id'], 'name': r['name'], 'country': r['country']}})
            n += 1
    summary['S native-sulfur MRDS records kept'] = n

    # ---- P: sedimentary phosphate only
    kept, dropped = 0, 0
    fc = json.loads((ATLAS / 'normalized/P-specialist.geojson').read_text(encoding='utf-8'))
    for f in fc['features']:
        p = f['properties']; g = f.get('geometry')
        sid = p.get('source_id'); style = str(p.get('deposit_style') or '')
        ok = (sid == 'usgs_phosphate' and not MAGMATIC.search(style) and style.strip() != 'Guano' and style != 'None') or \
             (sid == 'usgs_major-deposits' and style.startswith('Sedimentary'))
        if not ok or not g: dropped += 1; continue
        if sid == 'usgs_major-deposits': tier = 'major'
        else:
            prod = (p.get('production_status') or '').casefold()
            tier = 'deposit_large' if re.search(r'produc|active|mine', prod) and 'not' not in prod else 'deposit'
        lon, lat = g['coordinates'][:2]
        feats.append({'type': 'Feature', 'geometry': {'type': 'Point', 'coordinates': [lon, lat]},
                      'properties': {'code': 'P', 'source': 'usgs-phosphate-sedimentary', 'plane': 'obs', 'tier': tier,
                                     'id': p.get('source_feature_id'), 'name': p.get('name'), 'country': p.get('country')}})
        kept += 1
    summary['P specialist records kept (sedimentary)'] = kept
    summary['P specialist records left out (Ni-Cr-PGE, magmatic apatite, pure guano, unclassified)'] = dropped

    # ---- K and NaCl: USGS potash tracts ranked
    fc = json.loads((ATLAS / 'normalized/PotashTracts.geojson').read_text(encoding='utf-8'))
    ranks = {}
    for f in fc['features']:
        a = f['properties']['source_attributes']
        try:
            g = tidy(shapely.make_valid(shape(f['geometry'])))
        except Exception:
            continue
        mining = str(a.get('Mining_act') or '').startswith(('Yes', 'Under development'))
        known = a.get('Resources') == 'Yes' or a.get('K_sites') == 'Yes'
        w = 0.5 if mining else 0.35 if known else 0.15
        if (a.get('Area_km2') or 0) > 300000 and not mining: w = 0.15
        ranks[a['Coded_ID']] = w
        gj = geojson(g)
        feats.append({'type': 'Feature', 'geometry': gj, 'properties': {
            'code': 'K', 'source': 'usgs-potash-tracts-ranked', 'plane': 'pot', 'weight': w, 'id': a['Coded_ID'],
            'name': a['Tract_name'], 'group': 'potash-tract-%s' % w}})
        feats.append({'type': 'Feature', 'geometry': gj, 'properties': {
            'code': 'NaCl', 'source': 'usgs-potash-tracts-ranked', 'plane': 'pot', 'weight': 0.5, 'id': a['Coded_ID'] + ':NaCl',
            'name': a['Tract_name'], 'group': 'salt-tract'}})
    summary['potash tracts by K weight'] = {str(w): sum(1 for v in ranks.values() if v == w) for w in (0.5, 0.35, 0.15)}
    return {'features': feats, 'summary': summary}
