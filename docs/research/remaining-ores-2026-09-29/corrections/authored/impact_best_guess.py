"""Per-entry impact of the authored best-guess layer on the ore grid.

Re-stamps every exported best-guess entry with the bake's own kernel (tools/bake_ore_heatmaps.kernel_offsets, kernel
floor, minimum stamp value, authoredBestGuess peak table) and compares it with a grid baked WITHOUT the layer
(default: docs/research/ore-heatmaps-v1/baseline-v2/ore-heatmaps-v1.json, the v2 grid). An entry "changes" a cell when
its quantised stamp exceeds the baseline observed plane there (the plane it is written to; max-combination), and
"adds heat" when it exceeds the baseline max(observed, potential, deep). With --after (default: the shipped grid) it
also checks that the shipped observed plane equals max(baseline, stamps) wherever a stamp lands.

Writes best-guess-impact.csv and best-guess-impact.json next to this script.
Run: .venv\\Scripts\\python.exe docs/research/remaining-ores-2026-09-29/corrections/authored/impact_best_guess.py [--before PATH] [--after PATH]
"""
from pathlib import Path
from collections import defaultdict
import argparse, csv, json, sys
import numpy as np

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[4]
sys.path.insert(0, str(ROOT / 'tools'))
sys.path.insert(0, str(ROOT / 'tests/RegionalOresTests'))
import bake_ore_heatmaps as B  # noqa: E402
import ore_regression as R  # noqa: E402


def stamps(feat, cfg):
    p = feat['properties']; lon, lat = feat['geometry']['coordinates']
    ab = cfg['authoredBestGuess']
    peak = min(ab['peak'][p['size']][p['confidence']], ab['maxPeak'])
    radius = min(float(p['radius_km']), ab['maxRadiusKm'])
    _, ys, xs, close = B.kernel_offsets(np.array([lat]), np.array([lon]), radius)
    vals = peak * (cfg['kernelFloor'] + (1 - cfg['kernelFloor']) * close)
    keep = vals >= cfg['minimumStampValue']
    return peak, ys[keep], xs[keep], B.quantize(vals[keep].astype(np.float32))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--before', default=str(ROOT / 'docs/research/ore-heatmaps-v1/baseline-v2/ore-heatmaps-v1.json'))
    ap.add_argument('--after', default=str(R.SHIPPED))
    ap.add_argument('--no-after', action='store_true')
    a = ap.parse_args()
    cfg = json.loads(B.SETTINGS.read_text())
    feats = json.loads((HERE / 'normalized/best-guess-districts.geojson').read_text(encoding='utf-8'))['features']
    _, before = R.load_grid(a.before)
    after = None if a.no_after else R.load_grid(a.after)[1]
    union = defaultdict(lambda: np.zeros((B.H, B.W), np.uint8))
    rows = []
    for f in feats:
        p = f['properties']; lon, lat = f['geometry']['coordinates']
        peak, ys, xs, q = stamps(f, cfg)
        row = {'name': p['name'], 'country': p['country'], 'layers': ';'.join(p['commodities']), 'size': p['size'],
               'confidence': p['confidence'], 'peak': peak, 'radius_km': p['radius_km']}
        changed = 0; added = 0; base_at = []
        for c in p['commodities']:
            planes = before[c][0]
            changed += int((q > planes[0][ys, xs]).sum()); added += int((q > planes.max(0)[ys, xs]).sum())
            y0, x0 = R.cell(lat, lon); base_at.append(f'{c}={planes[:, y0, x0].max() / 255:.2f}')
            np.maximum.at(union[c], (ys, xs), q)
        row.update(baseline_at_point=' '.join(base_at), cells_raised_observed=changed, cells_raised_any=added,
                   status='changes cells' if changed else 'no-op (grid already as strong)')
        rows.append(row)
    summary = {'entries': len(feats), 'entriesChangingCells': sum(r['cells_raised_observed'] > 0 for r in rows),
               'entriesAddingHeat': sum(r['cells_raised_any'] > 0 for r in rows), 'noOpEntries': [r['name'] for r in rows if not r['cells_raised_observed']],
               'perLayer': {}}
    mism = 0
    for c, u in union.items():
        planes = before[c][0]
        raised = u > planes[0]; added = u > planes.max(0)
        summary['perLayer'][c] = {'cellsRaisedObserved': int(raised.sum()), 'cellsAddingHeat': int(added.sum()),
                                  'newlyLitCells': int(((u > 0) & (planes.max(0) == 0)).sum())}
        if after is not None:
            exp = np.maximum(planes[0], u); m = u > 0
            if c == 'NaCl' and 'K' in union:  # every K stamp is mirrored into NaCl at the same weight
                exp = np.maximum(exp, union['K']); m |= union['K'] > 0
            mism += int((after[c][0][0][m] != exp[m]).sum())
    if after is not None: summary['shippedGridMismatchCells'] = mism
    summary['perLayer'] = dict(sorted(summary['perLayer'].items()))
    with (HERE / 'best-guess-impact.csv').open('w', newline='', encoding='utf-8') as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
    (HERE / 'best-guess-impact.json').write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps({k: v for k, v in summary.items() if k not in ('noOpEntries', 'perLayer')}))
    print('no-op entries:', len(summary['noOpEntries']))


if __name__ == '__main__':
    main()
