"""Empty blocks: ice-free land with no heat (any plane) and no catalogue body within 100 km, for Cu, Sn, Fe, Au, coal.
Connected regions (8-neighbour, wrapping in longitude) are listed with area, centroid, main countries and the GLiM
lithology mix (to tell expected emptiness, e.g. coal on Precambrian shields, from suspicious gaps)."""
import base64, csv, json, math, sys
from collections import Counter
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import numpy as np
import shapely
from shapely.geometry import shape
from scipy import ndimage
from common import load_heat, load_catalogue, cell_area_km2, W, H, EARTH

HERE = Path(__file__).resolve().parent
RKM = 100.0
LITH = {1: 'su', 2: 'vb', 3: 'ss', 4: 'pb', 5: 'sm', 6: 'sc', 7: 'va', 8: 'mt', 9: 'pa', 10: 'vi', 11: 'wb', 12: 'py', 13: 'pi', 14: 'ev', 15: 'nd', 16: 'ig'}
SED = {1, 3, 5, 6, 14}; CRYST = {2, 4, 7, 8, 9, 10, 12, 13}

def commodity_layers(x):
    s = {x['layer']}
    s.update((x.get('grades') or {}).keys())
    for o in x.get('ores', []):
        c = o['code']
        if c in ('chalcopyrite', 'malachite', 'azurite', 'nativecopper', 'chalcocite'): s.add('Cu')
        if c in ('gold', 'earth-placergold'): s.add('Au')
        if c in ('cassiterite', 'earth-placertin'): s.add('Sn')
        if c in ('hematite', 'magnetite', 'limonite'): s.add('Fe')
    for b in x.get('beds', []) or []:
        if b['code'] in ('bituminouscoal', 'lignite', 'anthracite'): s.add('C_coal')
        if b['code'] in ('hematite', 'magnetite', 'limonite'): s.add('Fe')
        if b['code'] in ('chalcopyrite', 'malachite'): s.add('Cu')
        if b['code'] == 'gold': s.add('Au')
    return s

def near_mask(points, polys):
    m = np.zeros((H, W), bool)
    lat_c = 89.75 - np.arange(H) / 2.0
    for lat, lon in points:
        cy = int(math.floor((90 - lat) * 2)); ky = int(math.ceil(RKM / 55.6)) + 1
        for y in range(max(0, cy - ky), min(H, cy + ky + 1)):
            cl = math.cos(math.radians(lat_c[y])); kx = min(W // 2, int(math.ceil(RKM / (55.6 * max(cl, .05)))) + 1)
            cx = int(math.floor((lon + 180) * 2))
            for x in range(cx - kx, cx + kx + 1):
                xx = x % W; clon = xx / 2.0 - 179.75
                dl = (clon - lon + 180) % 360 - 180
                d = math.hypot(dl * 111.195 * math.cos(math.radians((lat + lat_c[y]) / 2)), (lat_c[y] - lat) * 111.195)
                if d <= RKM: m[y, xx] = True
    if polys:
        lats = 89.75 - np.arange(H) / 2.0; lons = np.arange(W) / 2.0 - 179.75
        LON, LAT = np.meshgrid(lons, lats)
        pts = shapely.points(LON.ravel(), LAT.ravel())
        tree = shapely.STRtree(pts)
        for g in polys:
            for i in tree.query(g, predicate='dwithin', distance=RKM / 111.195):
                m.flat[int(i)] = True
    return m

def main():
    heat, _ = load_heat(2)
    cache = np.load(HERE / 'grids-cache.npz', allow_pickle=True)
    land, ice, reg, cty = cache['land'], cache['ice'], cache['reg'], cache['cty']
    landok = (land >= .5) & (ice < .5)
    area = cell_area_km2()
    geo = json.loads((EARTH / 'assets/earth/config/geology-grids-v1.json').read_text())
    lith = np.frombuffer(base64.b64decode(geo['lithology']), dtype=np.uint8).reshape(H, W)
    cats = load_catalogue()
    report = []; summary = []
    for code in ['Cu', 'Sn', 'Fe', 'Au', 'C_coal']:
        pts, polys = [], []
        for g, d in cats.items():
            for x in d['deposits']:
                if code in commodity_layers(x): pts.append((x['lat'], x['lon']))
            for a in d.get('areas', []):
                if code in commodity_layers(a):
                    geom = shape(a['geometry'])
                    if not geom.is_valid: geom = shapely.make_valid(geom)
                    polys.append(geom)
        near = near_mask(pts, polys)
        L = heat[code]
        anyheat = (np.maximum(np.maximum(L['obs'], L['pot']), L['deep']) > 0)
        obsheat = (np.maximum(L['obs'], L['deep']) > 0)
        for variant, hm in [('no heat at all', anyheat), ('no observed/deep heat', obsheat)]:
            empty = landok & ~hm & ~near
            tot = (area * landok).sum()
            ea = (area * empty).sum()
            # per compact region share of land that is empty
            regs = {}
            for rname in np.unique(reg):
                if rname in ('Ocean/none', 'Antarctica'): continue
                rl = (area * (landok & (reg == rname))).sum()
                if rl > 0: regs[rname] = round(100 * (area * (empty & (reg == rname))).sum() / rl, 1)
            summary.append({'code': code, 'variant': variant, 'catalogue_points': len(pts), 'catalogue_areas': len(polys),
                            'empty_land_Mkm2': round(ea / 1e6, 2), 'empty_land_%': round(100 * ea / tot, 1), **{f'{k} %': v for k, v in regs.items()}})
            if variant != 'no heat at all':
                continue
            # components with longitude wrap: label on a doubled-width grid
            dbl = np.concatenate([empty, empty], axis=1)
            lab, n = ndimage.label(dbl, structure=np.ones((3, 3)))
            seen = set(); comps = []
            flat = lab.ravel(); nz = np.nonzero(flat)[0]
            order = nz[np.argsort(flat[nz], kind='stable')]
            labs = flat[order]
            bounds = np.flatnonzero(np.diff(labs)) + 1
            groups = np.split(order, bounds)
            for grp in groups:
                ys, xs = np.divmod(grp, 2 * W)
                if len(grp) * 2500 < 50000 * 0.2: continue
                key = frozenset(zip(ys.tolist(), (xs % W).tolist()))
                sig = (min(key), len(key))
                if sig in seen: continue
                seen.add(sig)
                cells = list(key)
                yy = np.array([c[0] for c in cells]); xx = np.array([c[1] for c in cells])
                a_km = float(area[yy, xx].sum())
                if a_km < 50000: continue
                cc = Counter(cty[yy, xx].tolist())
                lc = Counter(int(v) for v in lith[yy, xx])
                totc = sum(lc.values())
                sed = sum(v for kk, v in lc.items() if kk in SED) / totc
                cry = sum(v for kk, v in lc.items() if kk in CRYST) / totc
                lat_c = float(np.average(89.75 - yy / 2.0, weights=area[yy, xx]))
                ang = np.radians(xx / 2.0 - 179.75)
                lon_c = math.degrees(math.atan2(np.sin(ang).mean(), np.cos(ang).mean()))
                comps.append({'code': code, 'area_km2': round(a_km), 'cells': len(cells), 'lat': round(lat_c, 1), 'lon': round(lon_c, 1),
                              'countries': '; '.join(f'{c or "?"} {100*v/len(cells):.0f}%' for c, v in cc.most_common(4)),
                              'sedimentary_%': round(100 * sed), 'crystalline_%': round(100 * cry),
                              'lith_top': ' '.join(f'{LITH.get(k, k)}:{100*v/totc:.0f}%' for k, v in lc.most_common(3))})
            comps.sort(key=lambda c: -c['area_km2'])
            report.extend(comps[:15])
    with open(HERE / 'empty-blocks.csv', 'w', newline='', encoding='utf8') as f:
        w = csv.DictWriter(f, fieldnames=list(report[0])); w.writeheader(); w.writerows(report)
    keys = []
    for s in summary:
        for k in s:
            if k not in keys: keys.append(k)
    with open(HERE / 'empty-summary.csv', 'w', newline='', encoding='utf8') as f:
        w = csv.DictWriter(f, fieldnames=keys); w.writeheader(); w.writerows(summary)
    for s in summary: print(s)
    print()
    for c in report: print(c)

if __name__ == '__main__':
    main()
