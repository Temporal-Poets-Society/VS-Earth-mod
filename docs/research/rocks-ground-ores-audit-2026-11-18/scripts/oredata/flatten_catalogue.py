"""Flatten the four ore-deposit catalogues into one CSV with Natural Earth country/region and contained metal."""
import csv, json, math, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import numpy as np
import shapely
from shapely.geometry import shape
from common import Countries, load_catalogue, metal_tonnes, COMPACT

OUT = Path(__file__).resolve().parent

def decimals(v):
    s = repr(float(v))
    if 'e' in s: return 6
    return len(s.split('.')[1].rstrip('0')) if '.' in s else 0

def main():
    cats = load_catalogue()
    C = Countries()
    rows = []
    for g, d in cats.items():
        units = {k: v.get('unit') for k, v in (d.get('gradeBands') or {}).items()}
        for x in d['deposits']:
            reg, crow, dist = C.region(x['lat'], x['lon'])
            r = {'group': g, 'kind': 'deposit', 'id': x['id'], 'name': x['name'], 'layer': x['layer'],
                 'type': x.get('type', ''), 'catCountry': x.get('country', ''),
                 'neCountry': crow['admin'] if crow else '', 'neDistDeg': round(dist, 3) if dist else 0,
                 'region': reg, 'compact': COMPACT[reg], 'lat': x['lat'], 'lon': x['lon'],
                 'latDec': decimals(x['lat']), 'lonDec': decimals(x['lon']),
                 'shape': x.get('shape'), 'mt': x.get('mt'), 'grade': x.get('grade'), 'unit': x.get('unit'),
                 'topM': x.get('topM'), 'baseM': x.get('baseM'), 'lenKm': x.get('lenKm'), 'widKm': x.get('widKm'),
                 'q': x.get('q'), 'est': ';'.join(x.get('est') or []), 'src': x.get('src'),
                 'district': x.get('district', ''), 'srcFootprint': x.get('srcFootprint', ''),
                 'ores': ';'.join(o['code'] for o in x.get('ores', [])), 'areaKm2': ''}
            r['contained_main'] = metal_tonnes(x.get('mt'), x.get('grade'), x.get('unit'))
            gr = dict(x.get('grades') or {})
            gr.setdefault(x['layer'], x.get('grade'))
            for m, v in gr.items():
                u = units.get(m) or ('%' if m in ('Cu', 'Zn', 'Pb', 'Sn', 'Mo', 'W', 'Sb', 'Bi', 'Hg', 'Ni', 'U') else 'g/t' if m in ('Au', 'Ag', 'Pt') else None)
                if m == x['layer']: u = x.get('unit') or u
                r['c_' + m] = metal_tonnes(x.get('mt'), v, u)
            rows.append(r)
        for a in d.get('areas', []):
            geom = shape(a['geometry'])
            if not geom.is_valid: geom = shapely.make_valid(geom)
            pt = geom.representative_point()
            lat0 = pt.y
            area_km2 = geom.area * (111.195 ** 2) * math.cos(math.radians(lat0))
            reg, crow, dist = C.region(pt.y, pt.x)
            mt = a.get('mt')
            beds = a.get('beds') or []
            if mt is None and any('mt' in b for b in beds): mt = sum(b.get('mt') or 0 for b in beds)
            cont = None
            if any(b.get('mt') and b.get('grade') for b in beds):
                cont = sum(metal_tonnes(b['mt'], b['grade'], a.get('unit') or '%') or 0 for b in beds if b.get('mt') and b.get('grade'))
            else:
                gr = a.get('grade')
                if gr is None:
                    gs = [b.get('grade') for b in beds if b.get('grade')]
                    gr = gs[0] if gs else None
                unit = a.get('unit') or (units.get(a['layer']))
                cont = metal_tonnes(mt, gr, unit)
            r = {'group': g, 'kind': 'area', 'id': a['id'], 'name': a['name'], 'layer': a['layer'], 'type': a.get('type', ''),
                 'catCountry': a.get('country', ''), 'neCountry': crow['admin'] if crow else '',
                 'neDistDeg': round(dist, 3) if dist else 0, 'region': reg, 'compact': COMPACT[reg],
                 'lat': round(pt.y, 4), 'lon': round(pt.x, 4), 'latDec': '', 'lonDec': '', 'shape': 'area', 'mt': mt,
                 'grade': a.get('grade'), 'unit': a.get('unit'), 'topM': min((b.get('topM') or 0) for b in beds) if beds else '',
                 'baseM': '', 'lenKm': '', 'widKm': '', 'q': a.get('q'), 'est': ';'.join(a.get('est') or []), 'src': a.get('src'),
                 'district': '', 'srcFootprint': '', 'ores': ';'.join(b['code'] for b in beds), 'areaKm2': round(area_km2, 1),
                 'contained_main': cont}
            if a.get('grades'):
                for m, v in a['grades'].items(): r['c_' + m] = metal_tonnes(mt, v, units.get(m) or '%')
            else:
                r['c_' + a['layer']] = cont
            rows.append(r)
    keys = []
    for r in rows:
        for k in r:
            if k not in keys: keys.append(k)
    with open(OUT / 'catalogue-flat.csv', 'w', newline='', encoding='utf8') as f:
        w = csv.DictWriter(f, fieldnames=keys); w.writeheader(); w.writerows(rows)
    print(len(rows), 'rows ->', OUT / 'catalogue-flat.csv')

if __name__ == '__main__':
    main()
