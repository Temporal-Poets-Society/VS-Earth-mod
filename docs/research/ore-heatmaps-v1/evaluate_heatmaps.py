"""Before/after evaluation of the ore heat maps.

Default: v2 (baseline-v2/, archived 2026-10-02 before the authored best-guess layer) versus the shipped grid (v3),
writing regression-v2-vs-v3.json, spotcheck-v2-vs-v3.csv, commodities-v2-vs-v3.csv and coverage-v2-vs-v3.json.
The earlier step is reproduced with --old baseline-v1/ore-heatmaps-v1.json --new baseline-v2/ore-heatmaps-v1.json
--tag v1-vs-v2 (the v1-vs-v2 files committed with dataset v2 predate the FORMER_GAPS checks).
A grid's bake manifest is read from its own folder when present, else from this folder. Run from the Earth workspace
with the project venv.
"""
from pathlib import Path
import argparse, csv, json, sys
import numpy as np

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
sys.path.insert(0, str(ROOT / 'tests/RegionalOresTests'))
import ore_regression as R  # noqa: E402
import test_ore_regression as T  # noqa: E402


def manifest_for(grid):
    own = Path(grid).parent / 'bake-manifest.json'
    path = own if own.exists() and Path(grid).resolve() != R.SHIPPED.resolve() else HERE / 'bake-manifest.json'
    return {r['code']: r for r in json.loads(path.read_text(encoding='utf8'))['resources']}


def coverage(g, m, codes):
    """Per continent: lit ice-free land cells and cells >= 0.5 (observed/deep) for each layer."""
    out = {}
    for code in codes:
        p = g[code][0]; a = p.max(0); o = np.maximum(p[0], p[2])
        out[code] = {name: {'lit': int(((a > 0) & mask).sum()), 'atLeast0.5': int(((o >= 128) & mask).sum())}
                     for name, mask in list(m['continents'].items()) + [('World', m['free'])]}
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--old', default=str(HERE / 'baseline-v2/ore-heatmaps-v1.json'))
    ap.add_argument('--new', default=str(R.SHIPPED))
    ap.add_argument('--tag', default='v2-vs-v3')
    a = ap.parse_args()
    o_tag, n_tag = a.tag.split('-vs-')
    old = T.run(Path(a.old)); new = T.run(Path(a.new))
    (HERE / f'regression-{a.tag}.json').write_text(json.dumps({o_tag: old, n_tag: new}, indent=2), encoding='utf8')
    rows = []
    groups = [('famous', 'asserted'), ('formerGaps', 'former gap (asserted since v3)'), ('extra', 'reported'),
              ('bestGuessSpots', 'best-guess spot (reported)'), ('knownGaps', 'known gap')]
    for key, label in groups:
        for x, y in zip(old[key], new[key]):
            rows.append({'name': x['name'], 'code': x['code'], f'{o_tag}_ring1': x['ring1'], f'{o_tag}_status': R.classify(x['ring1']),
                         f'{n_tag}_ring1': y['ring1'], f'{n_tag}_status': R.classify(y['ring1']), 'group': label})
    with (HERE / f'spotcheck-{a.tag}.csv').open('w', newline='', encoding='utf8') as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
    m = R.masks(); _, g1 = R.load_grid(a.old); _, g2 = R.load_grid(a.new)
    man1, man2 = manifest_for(a.old), manifest_for(a.new)
    crow = []
    for code in g2:
        def stats(g):
            p = g[code][0]; mx = p.max(0)
            lit = (mx > 0) & m['land']
            return int(lit.sum()), int(((p[0] > 0) | (p[2] > 0)).sum()), int((p[1] > 0).sum()), int((p[2] > 0).sum()), \
                round(float((lit & m['us']).sum() / max(1, lit.sum())), 3), int(((mx >= 128) & m['land']).sum())
        s1, s2 = stats(g1), stats(g2)
        crow.append({'code': code, f'{o_tag}_lit_land': s1[0], f'{n_tag}_lit_land': s2[0], f'{o_tag}_observed_or_deep': s1[1], f'{n_tag}_observed_or_deep': s2[1],
                     f'{o_tag}_potential': s1[2], f'{n_tag}_potential': s2[2], f'{o_tag}_deep': s1[3], f'{n_tag}_deep': s2[3],
                     f'{o_tag}_us_share_lit_land': s1[4], f'{n_tag}_us_share_lit_land': s2[4], f'{o_tag}_cells_ge_0.5': s1[5], f'{n_tag}_cells_ge_0.5': s2[5],
                     f'{n_tag}_point_records': man2[code].get('pointRecords'), f'{n_tag}_point_record_us_share': man2[code].get('pointRecordUsShare')})
    with (HERE / f'commodities-{a.tag}.csv').open('w', newline='', encoding='utf8') as f:
        w = csv.DictWriter(f, fieldnames=list(crow[0])); w.writeheader(); w.writerows(crow)
    cov = {o_tag: coverage(g1, m, list(g2)), n_tag: coverage(g2, m, list(g2)),
           'metrics': {o_tag: {k: v for k, v in old['metrics'].items()}, n_tag: {k: v for k, v in new['metrics'].items()}}}
    (HERE / f'coverage-{a.tag}.json').write_text(json.dumps(cov, indent=1), encoding='utf8')
    keys = ['usToRestSaturationRatio', 'usHeatMassShare', 'iceFreeLandObservedMetalShare', 'iceFreeLandAnyMetalHeatShare']
    print(json.dumps({f'{o_tag}_failures': len(old['failures']), f'{n_tag}_failures': len(new['failures']),
                      f'{o_tag}_metrics': {k: old['metrics'][k] for k in keys}, f'{n_tag}_metrics': {k: new['metrics'][k] for k in keys},
                      'continents': {c: {o_tag: old['metrics']['continents'][c], n_tag: new['metrics']['continents'][c]} for c in new['metrics']['continents']}}, indent=1))


if __name__ == '__main__':
    main()
