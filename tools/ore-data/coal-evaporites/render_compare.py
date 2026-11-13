"""Before/after maps of the group's heat-map layers from two test bakes, with the catalogue on the after map.

Run: python -I tools/ore-data/coal-evaporites/render_compare.py <before ore-heatmaps.json> <after ore-heatmaps.json> <out folder>
"""
from pathlib import Path
import base64, gzip, json, sys
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.colors import ListedColormap

ROOT = Path(__file__).resolve().parents[3]
CODES = ['C_coal', 'NaCl', 'K', 'gypsum', 'B', 'S', 'P', 'N', 'alum']


def load(path):
    d = json.loads(Path(path).read_text(encoding='utf-8'))
    W, H = d['width'], d['height']; N = W * H; out = {}
    for l in d['layers']:
        if l['code'] not in CODES: continue
        raw = gzip.decompress(base64.b64decode(l['data']))
        pl = np.frombuffer(raw[:3 * N], np.uint8).reshape(3, H, W) / 255
        out[l['code']] = (l['label'], pl)
    return out, W, H


def main():
    before, W, H = load(sys.argv[1]); after, _, _ = load(sys.argv[2]); out = Path(sys.argv[3]); out.mkdir(parents=True, exist_ok=True)
    with np.load(ROOT / 'preview/grids.npz') as p:
        f = p['flags']
        rows = np.minimum(f.shape[0] - 1, ((np.arange(H) + .5) * f.shape[0] / H).astype(int))
        cols = np.minimum(f.shape[1] - 1, ((np.arange(W) + .5) * f.shape[1] / W).astype(int))
        ocean = ((f[np.ix_(rows, cols)] & 1) > 0).astype(np.uint8)
    cat = json.loads((ROOT / 'assets/earth/config/ore-deposits-v1/coal-evaporites.json').read_text(encoding='utf-8'))
    cmap = plt.get_cmap('magma')
    stats = {}
    for code in CODES:
        fig, axes = plt.subplots(2, 1, figsize=(10, 10.4), layout='constrained')
        for ax, (title, data) in zip(axes, [('before (ore-heatmaps-v1 sources)', before), ('after (with this group\'s evidence and overrides)', after)]):
            label, pl = data[code]
            obs, pot, deep = pl
            heat = np.maximum(np.maximum(obs, pot), deep)
            ax.imshow(ocean, extent=(-180, 180, -90, 90), cmap=ListedColormap(['#e7e8e2', '#f7fafc']), vmin=0, vmax=1, interpolation='nearest')
            im = ax.imshow(np.ma.masked_where(heat == 0, heat), extent=(-180, 180, -90, 90), vmin=0, vmax=1, cmap=cmap, interpolation='nearest')
            ax.set(xlim=(-180, 180), ylim=(-60, 85), title=f'{label} ({code}): {title}')
            ax.tick_params(labelsize=8)
            stats.setdefault(code, {})['before' if data is before else 'after'] = {
                'obsCells': int((obs > 0).sum()), 'potCells': int((pot > 0).sum()), 'deepCells': int((deep > 0).sum()),
                'anyCells': int((heat > 0).sum()), 'cellsAtLeast0.5': int((heat >= .5).sum())}
            if data is after:
                for a in cat['areas']:
                    if a['layer'] != code and not (code == 'NaCl' and a['layer'] == 'K'): continue
                    g = a['geometry']
                    polys = [g['coordinates']] if g['type'] == 'Polygon' else g['coordinates']
                    for poly in polys:
                        xy = np.array(poly[0]); ax.plot(xy[:, 0], xy[:, 1], color='#19a7ce', lw=.6)
                pts = [(d['lon'], d['lat']) for d in cat['deposits'] if d['layer'] == code]
                if pts: ax.scatter(*zip(*pts), s=9, facecolors='none', edgecolors='#19a7ce', linewidths=.8)
        fig.colorbar(im, ax=axes, shrink=.6, label='max(observed, potential, deep) weight')
        fig.text(.01, .002, 'Blue: catalogue areas (outlines) and deposits (circles). 0.5-degree cells; test bakes of 8 Nov 2026.', fontsize=8)
        fig.savefig(out / f'{code}.png', dpi=72); plt.close(fig)
    (out / 'heatmap-cell-counts.json').write_text(json.dumps(stats, indent=1) + '\n', encoding='utf-8')
    print(json.dumps(stats))


if __name__ == '__main__':
    main()
