"""Normalize the LVGMC (Latvia) INSPIRE licensed mineral-occurrence subset for peat and clay.

Source: LVGMC GeoServer WFS (INSPIRE GE/MR), retrieved 2026-09-29 into corrections/gypsum/raw/,
Latvia open-data catalogue licence CC0-1.0 (see latvia-data-catalog.json). Each occurrence polygon is joined
to its commodity row and mine/licence row by IFCID. Only commodities that have a runtime layer are exported:
peat -> 'peat', clay -> 'clay'. Gypsum is already in gypsum-reviewed.geojson. Sand/gravel, frac sand and
limestone have no runtime ore layer and are counted but not exported.

These are licensed extraction subareas (mostly operating peat bogs), not a complete map of Latvian peat or clay.
Coordinates are EPSG:4258 (ETRS89) longitude/latitude, numerically equivalent to WGS 84 at this scale.
"""
from pathlib import Path
from collections import Counter
import json
from shapely.geometry import shape, mapping

ROOT = Path(__file__).resolve().parents[1]
RAW = ROOT / 'gypsum' / 'raw'
OUT = ROOT / 'nonmetals' / 'normalized' / 'latvia-lvgmc-commodities.geojson'
LAYER = {'peat': 'peat', 'clay': 'clay'}


def load(name):
    return json.loads((RAW / name).read_text(encoding='utf-8-sig'))['features']


def main():
    commodity = {f['properties']['IFCID']: f['properties'] for f in load('latvia-commodity.geojson')}
    mines = {f['properties']['IFCID']: f['properties'] for f in load('latvia-mines-native.geojson')}
    features = []
    seen = Counter()
    for f in load('latvia-occurrence-polygons-native.geojson'):
        a = f['properties']
        c = commodity.get(a['IFCID'])
        if c is None:
            seen['no_commodity_row'] += 1
            continue
        seen[c['commodity_code']] += 1
        code = LAYER.get(c['commodity_code'])
        if code is None or not f.get('geometry'):
            continue
        g = shape(f['geometry'])
        m = mines.get(a['IFCID'], {})
        features.append({'type': 'Feature', 'id': 'lvgmc:' + str(a['IFCID']), 'geometry': mapping(g), 'properties': {
            'record_id': 'lvgmc_inspire:' + str(a['IFCID']), 'source_id': 'lvgmc_inspire_licensed_occurrence',
            'source_feature_id': a['IFCID'], 'name': m.get('minename_1') or None, 'country': 'Latvia',
            'commodities': [code], 'source_commodity_code': c['commodity_code'],
            'commodity_importance': c.get('commodityImportance_code'),
            'current_operating_status': m.get('status_code'), 'licence_reference': m.get('sourceReference'),
            'evidence_class': 'licensed_extraction_subarea', 'area_deg2': round(g.area, 8),
            'source_url': 'https://geoserver.lvgmc.lv/geoserver/mr/ows',
            'license_note': 'Latvia data catalogue CC0-1.0; WFS access constraints NONE',
            'coverage_warning': 'Licensed extraction subset only; absence elsewhere is not evidence of absence',
            'spawn_probability': None, 'early_accessibility': None}})
    OUT.write_text(json.dumps({'type': 'FeatureCollection', 'features': features}, ensure_ascii=False,
                              separators=(',', ':')), encoding='utf-8')
    summary = {'exported': dict(Counter(x['properties']['commodities'][0] for x in features)),
               'all_polygons_by_commodity': dict(seen)}
    print(json.dumps(summary))
    return summary


if __name__ == '__main__':
    main()
