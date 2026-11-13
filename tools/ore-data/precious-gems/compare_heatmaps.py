"""Compare two test bakes of the ore heat maps for the precious-gems layer codes and render before/after maps.

  python -I tools/ore-data/precious-gems/compare_heatmaps.py <before ore-heatmaps.json> <after ore-heatmaps.json> <png folder>

Writes <png folder>/<code>.png (before above, after below: max of the observed and potential planes), and
tools/ore-data/precious-gems/heatmap-comparison.json: per code the evidence cells, the share of observed weight in the
USA, and every catalogue deposit's observed value before and after (deposits whose cell was below 0.5 before are the
places where the old maps were wrong about a known deposit).
"""
from pathlib import Path
import base64, gzip, json, sys

import numpy as np
import shapely
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.colors import ListedColormap

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
CODES = ['Au', 'diamond', 'corundum', 'emerald', 'olivine', 'garnet', 'lapis', 'topaz', 'tourmaline', 'beryl', 'spinel',
         'C_graphite', 'F', 'SiO2', 'U']
W, H = 720, 360


def planes(path):
    d = json.loads(Path(path).read_text())
    out = {}
    for l in d['layers']:
        if l['code'] not in CODES: continue
        raw = gzip.decompress(base64.b64decode(l['data']))
        a = np.frombuffer(raw[:W * H * 3], np.uint8).reshape(3, H, W).astype(np.float32) / 255
        out[l['code']] = a
    return out


def masks():
    from pyogrio.raw import read
    ne = ROOT / 'docs/research/remaining-ores-2026-09-29/raw/naturalearth/ne_10m_admin_0_countries.zip'
    _, _, wkb, cols = read('/vsizip/' + ne.as_posix() + '/ne_10m_admin_0_countries.shp', columns=['SOVEREIGNT'])
    geoms = shapely.from_wkb(wkb)
    lon = (np.arange(W) + .5) / 2 - 180; lat = 90 - (np.arange(H) + .5) / 2
    LON, LAT = np.meshgrid(lon, lat)
    pts = shapely.points(LON.ravel(), LAT.ravel())
    land = shapely.union_all(geoms); shapely.prepare(land)
    us = shapely.union_all([g for g, s in zip(geoms, cols[0]) if s == 'United States of America']); shapely.prepare(us)
    return shapely.contains(land, pts).reshape(H, W), shapely.contains(us, pts).reshape(H, W)


def cell(lat, lon):
    return min(H - 1, int((90 - lat) * 2)), min(W - 1, int((lon + 180) * 2))


def main():
    before, after, png = planes(sys.argv[1]), planes(sys.argv[2]), Path(sys.argv[3])
    png.mkdir(parents=True, exist_ok=True)
    land, us = masks()
    cat = json.loads((ROOT / 'assets/earth/config/ore-deposits-v1/precious-gems.json').read_text(encoding='utf-8'))
    val = json.loads((HERE / 'validation.json').read_text(encoding='utf-8'))
    country = {d['id']: d['country'] for d in val['deposits']}
    cmap = plt.get_cmap('magma_r')
    result = {'codes': {}, 'deposits': []}
    for c in CODES:
        row = {}
        for name, p in (('before', before[c]), ('after', after[c])):
            obs, pot = p[0], p[1]
            w = obs.sum()
            row[name] = {'observedCells': int((obs > 0).sum()), 'potentialCells': int((pot > 0).sum()),
                         'observedWeight': round(float(w), 1), 'usShareOfObservedWeight': round(float(obs[us].sum() / w), 3) if w else None,
                         'cellsAbove05': int((obs >= .5).sum())}
        result['codes'][c] = row
        fig, axes = plt.subplots(2, 1, figsize=(9, 9.2), dpi=64)
        for ax, (name, p) in zip(axes, (('before', before[c]), ('after', after[c]))):
            v = np.maximum(p[0], p[1])
            base = np.where(land, .93, 1.0)
            ax.imshow(base, cmap='gray', vmin=0, vmax=1, extent=[-180, 180, -90, 90], interpolation='nearest')
            ax.imshow(np.ma.masked_where(v <= 0, v), cmap=cmap, vmin=0, vmax=1, extent=[-180, 180, -90, 90], interpolation='nearest')
            pts = [(d['lon'], d['lat']) for d in cat['deposits'] if d['layer'] == c]
            if name == 'after' and pts:
                ax.scatter([x for x, _ in pts], [y for _, y in pts], s=6, c='#1f77b4', marker='o', linewidths=0)
            r = result['codes'][c][name]
            ax.set_title('%s %s: %d observed cells (%d at 0.5+), US share of observed weight %s' %
                         (c, name, r['observedCells'], r['cellsAbove05'], r['usShareOfObservedWeight']), fontsize=9)
            ax.set_xlim(-180, 180); ax.set_ylim(-60, 85); ax.tick_params(labelsize=7)
        fig.text(.01, .005, 'max(observed, potential); blue dots: catalogue deposits (after). Test bakes of tools/bake_ore_heatmaps.py, 0.5 degree cells.', fontsize=7)
        fig.tight_layout(); fig.savefig(png / (c + '.png')); plt.close(fig)
    for d in cat['deposits']:
        y, x = cell(d['lat'], d['lon'])
        result['deposits'].append({'id': d['id'], 'name': d['name'], 'layer': d['layer'], 'country': country.get(d['id']),
                                   'before': round(float(before[d['layer']][0, y, x]), 3), 'after': round(float(after[d['layer']][0, y, x]), 3)})
    for a in cat['areas']:
        ring = a['geometry']['coordinates'][0]
        lon = sum(p[0] for p in ring[:-1]) / (len(ring) - 1); lat = sum(p[1] for p in ring[:-1]) / (len(ring) - 1)
        y, x = cell(lat, lon)
        result['deposits'].append({'id': a['id'], 'name': a['name'], 'layer': a['layer'], 'country': 'South Africa',
                                   'before': round(float(before['Au'][0, y, x]), 3), 'after': round(float(after['Au'][0, y, x]), 3)})
    missed = [d for d in result['deposits'] if d['before'] < .5]
    result['missedBefore'] = len(missed); result['missedAfter'] = sum(d['after'] < .5 for d in result['deposits'])
    (HERE / 'heatmap-comparison.json').write_text(json.dumps(result, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
    for c, r in result['codes'].items(): print(c, r['before'], '->', r['after'])
    print('catalogue deposits below 0.5 observed: before', result['missedBefore'], 'after', result['missedAfter'])


if __name__ == '__main__':
    main()
