"""Empty 5x5-degree tiles per commodity: >=90 % of the tile's ice-free land has no heat in any plane and no catalogue
body within 100 km, while >=30 % of it is in permissive GLiM rock. Also writes per-region shares of empty land."""
import base64, csv, json, math, sys
from collections import Counter
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import numpy as np
import shapely
from shapely.geometry import shape
from common import load_heat, load_catalogue, cell_area_km2, W, H, EARTH
from empty_blocks import commodity_layers, near_mask

HERE = Path(__file__).resolve().parent
PERM = {'Cu': {2, 4, 7, 8, 9, 10, 13}, 'Sn': {8, 9}, 'Fe': {2, 4, 8}, 'Au': {1, 2, 7, 8, 9, 10, 13}, 'C_coal': {1, 3, 5}}
T = 10  # cells per tile side (5 degrees)

def main():
    heat, _ = load_heat(2)
    cache = np.load(HERE / 'grids-cache.npz', allow_pickle=True)
    land, ice, cty = cache['land'], cache['ice'], cache['cty']
    landok = (land >= .5) & (ice < .5)
    area = cell_area_km2()
    geo = json.loads((EARTH / 'assets/earth/config/geology-grids-v1.json').read_text())
    lith = np.frombuffer(base64.b64decode(geo['lithology']), dtype=np.uint8).reshape(H, W)
    cats = load_catalogue()
    out = []
    for code, perm in PERM.items():
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
        hm = np.maximum(np.maximum(L['obs'], L['pot']), L['deep']) > 0
        empty = landok & ~hm & ~near
        pm = np.isin(lith, list(perm)) & landok
        for ty in range(0, H, T):
            for tx in range(0, W, T):
                sl = (slice(ty, ty + T), slice(tx, tx + T))
                la = (area[sl] * landok[sl]).sum()
                if la < 100000: continue
                ea = (area[sl] * empty[sl]).sum()
                pa = (area[sl] * pm[sl]).sum()
                pe = (area[sl] * (pm[sl] & empty[sl])).sum()
                if ea / la >= .9 and pa / la >= .3:
                    cc = Counter(c for c in cty[sl][landok[sl]].tolist() if c)
                    out.append({'code': code, 'tile_lat_n': 90 - ty / 2, 'tile_lon_w': tx / 2 - 180,
                                'land_km2': round(la), 'empty_%': round(100 * ea / la), 'permissive_%': round(100 * pa / la),
                                'empty_permissive_km2': round(pe),
                                'countries': '; '.join(f'{c}' for c, _ in cc.most_common(3))})
    with open(HERE / 'empty-tiles.csv', 'w', newline='', encoding='utf8') as f:
        w = csv.DictWriter(f, fieldnames=list(out[0])); w.writeheader(); w.writerows(out)
    for code in PERM:
        sel = [o for o in out if o['code'] == code]
        print(f'== {code}: {len(sel)} empty permissive 5-degree tiles, {sum(o["empty_permissive_km2"] for o in sel)/1e6:.2f} Mkm2 of empty permissive land')
        byc = Counter()
        for o in sel:
            for c in o['countries'].split('; ')[:1]: byc[c] += o['empty_permissive_km2']
        print('   by main country (Mkm2):', ', '.join(f'{c} {v/1e6:.2f}' for c, v in byc.most_common(14)))

if __name__ == '__main__':
    main()
