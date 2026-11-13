"""Read and render ore heat-map files (ore-heatmaps-v1 format) for the iron-ferro group's layer codes.

Usage (project venv, -I):
  python -I tools/ore-data/iron-ferro/heatmap_tools.py render <heatmaps.json> <out folder> [tag]
      one PNG per group code (observed | potential | combined max) plus a contact sheet
  python -I tools/ore-data/iron-ferro/heatmap_tools.py probe <heatmaps.json> <lat> <lon> [codes...]
      the three planes and deep depth of each code at a point
  python -I tools/ore-data/iron-ferro/heatmap_tools.py diff <before.json> <after.json> <out folder>
      cell counts and an after-minus-before PNG per code
"""
from pathlib import Path
import base64, gzip, json, sys

import numpy as np

W, H = 720, 360
GROUP_CODES = ['Fe', 'Mn', 'Cr', 'Ni', 'Ti', 'V', 'Pt', 'Al']
ROOT = Path(__file__).resolve().parents[3]


def load(path):
    d = json.loads(Path(path).read_text(encoding='utf-8'))
    out = {}
    for layer in d['layers']:
        raw = gzip.decompress(base64.b64decode(layer['data']))
        planes = np.frombuffer(raw[:W * H * 3], np.uint8).reshape(3, H, W)
        depth = np.frombuffer(raw[W * H * 3:], '<u2').reshape(H, W)
        out[layer['code']] = (planes, depth, layer)
    return out


def cell(lat, lon):
    return int(np.clip(np.floor((90 - lat) * 2), 0, H - 1)), int(np.floor((lon + 180) * 2)) % W


def land_mask():
    """0.5-degree land share from preview/grids.npz (the bake's own land mask)."""
    with np.load(ROOT / 'preview/grids.npz') as p:
        flags = p['flags']
    a = ((flags & 1) == 0).astype(np.float32)
    h, w = a.shape
    return a.reshape(h // 15, 15, w // 15, 15).mean(axis=(1, 3))


def render(path, outdir, tag=''):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    maps = load(path); outdir = Path(outdir); outdir.mkdir(parents=True, exist_ok=True)
    land = land_mask()
    fig, axes = plt.subplots(4, 2, figsize=(16, 16))
    for ax, code in zip(axes.ravel(), GROUP_CODES):
        planes, depth, layer = maps[code]
        obs = planes[0].astype(np.float32) / 255; pot = planes[1].astype(np.float32) / 255
        img = np.zeros((H, W, 3), np.float32)
        img[...] = np.where(land[..., None] >= .5, [.86, .86, .84], [.97, .98, 1.0])
        # observed in red-orange, potential in blue; observed wins where both
        p = np.clip(pot / .5, 0, 1)[..., None]
        img = img * (1 - .6 * p) + np.array([.2, .35, .8]) * .6 * p
        o = np.clip(obs, 0, 1)[..., None]
        img = img * (1 - o) + np.array([.75, .1, .05]) * o
        ax.imshow(img[20:330], extent=[-180, 180, -75, 80], interpolation='nearest')
        ax.set_title('%s %s  obs>0: %d cells, pot>0: %d' % (code, tag, int((obs > 0).sum()), int((pot > 0).sum())), fontsize=10)
        ax.set_xticks([]); ax.set_yticks([])
    fig.suptitle('Ore heat maps %s (red: observed plane, blue: potential plane) — %s' % (tag, Path(path).name))
    fig.tight_layout()
    fig.savefig(outdir / ('heatmaps-%s.png' % (tag or 'map')), dpi=70)
    plt.close(fig)


def probe(path, lat, lon, codes):
    maps = load(path); y, x = cell(lat, lon)
    for c in codes or GROUP_CODES:
        planes, depth, _ = maps[c]
        print(c, 'obs %.2f pot %.2f deep %.2f depth %d' % (planes[0, y, x] / 255, planes[1, y, x] / 255, planes[2, y, x] / 255, depth[y, x]))


def diff(before, after, outdir):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    a = load(before); b = load(after); outdir = Path(outdir); outdir.mkdir(parents=True, exist_ok=True)
    rows = []
    fig, axes = plt.subplots(4, 2, figsize=(16, 16))
    for ax, code in zip(axes.ravel(), GROUP_CODES):
        oa = a[code][0][0].astype(np.int16); ob = b[code][0][0].astype(np.int16)
        pa = a[code][0][1].astype(np.int16); pb = b[code][0][1].astype(np.int16)
        d = (ob - oa) / 255
        rows.append({'code': code, 'obsCellsBefore': int((oa > 0).sum()), 'obsCellsAfter': int((ob > 0).sum()),
                     'obsSumBefore': round(float(oa.sum()) / 255, 1), 'obsSumAfter': round(float(ob.sum()) / 255, 1),
                     'potCellsBefore': int((pa > 0).sum()), 'potCellsAfter': int((pb > 0).sum()),
                     'cellsRaised': int((d > .02).sum()), 'cellsLowered': int((d < -.02).sum())})
        ax.imshow(d[20:330], extent=[-180, 180, -75, 80], cmap='RdBu_r', vmin=-1, vmax=1, interpolation='nearest')
        ax.set_title('%s observed after - before (red raised, blue lowered)' % code, fontsize=10)
        ax.set_xticks([]); ax.set_yticks([])
    fig.tight_layout(); fig.savefig(outdir / 'heatmaps-diff.png', dpi=70); plt.close(fig)
    (outdir / 'heatmaps-diff.json').write_text(json.dumps(rows, indent=1))
    for r in rows: print(r)


if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'render': render(sys.argv[2], sys.argv[3], sys.argv[4] if len(sys.argv) > 4 else '')
    elif cmd == 'probe': probe(sys.argv[2], float(sys.argv[3]), float(sys.argv[4]), sys.argv[5:])
    elif cmd == 'diff': diff(sys.argv[2], sys.argv[3], sys.argv[4])
