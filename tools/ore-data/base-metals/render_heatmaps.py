"""Render the base-metals heat maps of one or two bakes (before / after) as PNGs, and print cell statistics.

Usage:
  python -I render_heatmaps.py --before <ore-heatmaps.json> [--after <ore-heatmaps.json>] --out <folder> [--codes Cu,Sn,...]
Each PNG shows heat = max(observed, potential) (what the runtime reads near the surface; deep evidence as contours) per
0.5-degree cell; before on top, after below. Statistics go to <out>/heatmap-stats.json.
"""
import argparse, base64, gzip, json, sys
from pathlib import Path

import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.colors import ListedColormap

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import CODES, NATURAL_EARTH  # noqa: E402

W, H = 720, 360


def load(path, codes):
    j = json.loads(Path(path).read_text(encoding='utf-8'))
    out = {}
    for layer in j['layers']:
        if layer['code'] not in codes: continue
        raw = gzip.decompress(base64.b64decode(layer['data']))
        planes = np.frombuffer(raw[:3 * W * H], np.uint8).reshape(3, H, W).astype(np.float32) / 255
        depth = np.frombuffer(raw[3 * W * H:], '<u2').reshape(H, W)
        out[layer['code']] = (planes[0], planes[1], planes[2], depth)
    return out


def land_outline():
    import shapely
    from pyogrio.raw import read
    _, _, wkb, _ = read('/vsizip/' + NATURAL_EARTH.as_posix() + '/ne_10m_admin_0_countries.shp', columns=['ADMIN'])
    geoms = shapely.from_wkb(wkb)
    segs = []
    for g in shapely.get_parts(shapely.simplify(shapely.union_all(geoms), .2)):
        for ring in [g.exterior] + list(g.interiors):
            c = np.asarray(ring.coords)
            if len(c) > 8: segs.append(c)
    return segs


def stats(obs, pot, deep):
    heat = np.maximum(obs, pot)
    return {'obsCells': int((obs > 0).sum()), 'potCells': int((pot > 0).sum()), 'deepCells': int((deep > 0).sum()),
            'heat>=0.25': int((heat >= .25).sum()), 'heat>=0.5': int((heat >= .5).sum()), 'heat>=0.75': int((heat >= .75).sum()),
            'heatSum': round(float(heat.sum()), 1)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--before', required=True); ap.add_argument('--after'); ap.add_argument('--out', required=True)
    ap.add_argument('--codes', default=','.join(CODES))
    a = ap.parse_args()
    codes = a.codes.split(',')
    out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    bake = [('before', load(a.before, codes))] + ([('after', load(a.after, codes))] if a.after else [])
    segs = land_outline()
    cmap = ListedColormap(plt.get_cmap('magma_r')(np.linspace(.05, .95, 256)))
    cmap.set_under('#f4f4f2')
    allstats = {}
    for code in codes:
        n = len(bake)
        fig, axes = plt.subplots(n, 1, figsize=(12, 6.2 * n / 1.0 * .5 + .4), dpi=90, squeeze=False)
        allstats[code] = {}
        for ax, (label, maps) in zip(axes[:, 0], bake):
            obs, pot, deep, depth = maps[code]
            heat = np.maximum(obs, pot)
            allstats[code][label] = stats(obs, pot, deep)
            ax.imshow(np.where(heat > 0, heat, -1), extent=(-180, 180, -90, 90), cmap=cmap, vmin=.001, vmax=1, interpolation='nearest')
            if deep.any():
                ax.contour(np.linspace(-179.75, 179.75, W), np.linspace(89.75, -89.75, H), deep, levels=[.3], colors='#1a7f8e', linewidths=.6)
            for c in segs: ax.plot(c[:, 0], c[:, 1], color='#7a7a7a', lw=.25)
            ax.set_xlim(-180, 180); ax.set_ylim(-60, 84); ax.set_xticks([]); ax.set_yticks([])
            s = allstats[code][label]
            ax.set_title(f"{code} {label}: heat = max(observed, potential); cells >= 0.5: {s['heat>=0.5']}, >= 0.25: {s['heat>=0.25']}; deep (teal) {s['deepCells']}",
                         fontsize=9, loc='left')
        fig.tight_layout()
        fig.savefig(out / f'heat-{code}.png', dpi=90)
        plt.close(fig)
    (out / 'heatmap-stats.json').write_text(json.dumps(allstats, indent=1), encoding='utf-8')
    print(json.dumps(allstats))


if __name__ == '__main__':
    main()
