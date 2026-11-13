"""Where a bake's base-metals heat maps disagree with the catalogue.

  python -I check_heatmaps.py <ore-heatmaps.json> [<label>]
For each code: (a) catalogue deposits of the world-class and large size classes whose own cell has heat < 0.5 (the map
misses a known deposit); (b) cells with heat >= 0.5 farther than 150 km from every point of this group's evidence for
that code (catalogue deposits, carried districts; not the capped MRDS cells) and outside its areas and provinces: heat
that no catalogued deposit supports, counted by country. Writes check-<label>.json next to the bake.
"""
import json, sys
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np
from scipy.ndimage import binary_dilation

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import CODES, EVIDENCE, NATURAL_EARTH  # noqa: E402
from render_heatmaps import load, W, H  # noqa: E402


def main():
    bake = Path(sys.argv[1]); label = sys.argv[2] if len(sys.argv) > 2 else bake.stem
    maps = load(bake, CODES)
    ev = json.loads(EVIDENCE.read_text(encoding='utf-8'))['features']
    import shapely
    from pyogrio.raw import read
    _, _, wkb, cols = read('/vsizip/' + NATURAL_EARTH.as_posix() + '/ne_10m_admin_0_countries.shp', columns=['ADMIN'])
    geoms = shapely.from_wkb(wkb); names = [str(x) for x in cols[0]]
    tree = shapely.STRtree(geoms)
    yy, xx = np.mgrid[0:H, 0:W]
    out = {}
    for code in CODES:
        obs, pot, deep, depth = maps[code]
        heat = np.maximum(obs, pot)
        support = np.zeros((H, W), bool)
        missed = []
        for f in ev:
            p = f['properties']
            if p['code'] != code or p.get('source') == 'base-metals mrds capped': continue  # background, not support
            g = f['geometry']
            if g['type'] == 'Point':
                lon, lat = g['coordinates']
                y, x = int((90 - lat) * 2), int((lon + 180) * 2) % W
                r = 150 / 55.6
                rx = r / max(.2, np.cos(np.radians(lat)))
                m = ((yy - y) ** 2 / r ** 2 + (((xx - x + W / 2) % W - W / 2) ** 2) / rx ** 2) <= 1
                support |= m
                if p.get('size') in ('world-class', 'large') and p.get('source') == 'base-metals catalogue':
                    h = float(max(heat[min(H - 1, y), x], deep[min(H - 1, y), x]))
                    if h < .5: missed.append({'name': p['name'], 'size': p['size'], 'heat': round(h, 2)})
            else:
                poly = shapely.geometry.shape(g)
                b = poly.bounds
                x0, x1 = int((b[0] + 180) * 2), int((b[2] + 180) * 2); y0, y1 = int((90 - b[3]) * 2), int((90 - b[1]) * 2)
                for y in range(max(0, y0), min(H, y1 + 1)):
                    for x in range(x0, x1 + 1):
                        if poly.intersects(shapely.box(x / 2 - 180, 89.5 - y / 2, x / 2 - 179.5, 90 - y / 2)): support[y, x % W] = True
        support = binary_dilation(support, iterations=1)
        bad = (heat >= .5) & ~support
        by = Counter()
        for y, x in zip(*np.nonzero(bad)):
            idx = tree.query(shapely.Point(x / 2 - 179.75, 89.75 - y / 2), predicate='intersects')
            by[names[idx[0]] if len(idx) else 'sea/none'] += 1
        out[code] = {'hotCells': int((heat >= .5).sum()), 'hotUnsupported': int(bad.sum()), 'hotUnsupportedByCountry': dict(by.most_common(12)),
                     'bigDepositsMissed': len(missed), 'bigMissedExamples': sorted(missed, key=lambda m: m['heat'])[:15]}
    path = bake.parent / f'check-{label}.json'
    path.write_text(json.dumps(out, indent=1, ensure_ascii=False), encoding='utf-8')
    for c, v in out.items():
        print(c, v['hotCells'], 'unsupported', v['hotUnsupported'], list(v['hotUnsupportedByCountry'].items())[:6], 'missed', v['bigDepositsMissed'], [m['name'] for m in v['bigMissedExamples'][:6]])


if __name__ == '__main__':
    main()
