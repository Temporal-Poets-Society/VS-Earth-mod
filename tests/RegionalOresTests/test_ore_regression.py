"""Famous-deposit and regional-coverage regression for the shipped ore heat maps.

Run:  .venv\\Scripts\\python.exe tests/RegionalOresTests/test_ore_regression.py [--grid PATH] [--json OUT]
Exit code 0 = every assertion passed. Use --grid docs/research/ore-heatmaps-v1/baseline-v1/ore-heatmaps-v1.json
to confirm the v1 bake fails the same thresholds (it does: see docs/research/ore-heatmaps-v1/regression-v1-vs-v2.json).

Thresholds were set from documented review findings (2026-10-02), not tuned to the exact v2 numbers:
famous deposits must reach a "present" weight (>=0.5, a mapped deposit) within one cell of the textbook location
(Gorleben salt dome >=0.3, Irish midland bogs >=0.3); the five v2 data gaps (Tavan Tolgoi, Moatize, Nord-Pas-de-Calais,
Inkai, Sicily) must be present since v3 (authored best-guess layer), whose peaks must stay below the survey tiers;
continents must have >=50% of ice-free land with some metal heat (observed or prior);
US-to-rest-of-world saturation (share of land at >=0.5, averaged over 19 metals) <=3x; the US (7% of ice-free
land) may hold at most 20% of total metal heat; sylvite (K) never exceeds its parent halite (NaCl).
"""
from pathlib import Path
import argparse, json, sys
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import ore_regression as R  # noqa: E402

V1_CODES = None


def run(grid_path):
    data, g = R.load_grid(grid_path)
    m = R.masks()
    failures = []; report = {'grid': str(grid_path), 'datasetId': data.get('datasetId')}

    def check(ok, msg):
        if not ok: failures.append(msg)

    # --- format: same layer set/order and binding as the v1 runtime contract
    base, _ = R.load_grid(R.BASELINE_V1)
    check([l['code'] for l in data['layers']] == [l['code'] for l in base['layers']], 'layer codes/order differ from v1 contract')
    check(all(a['deposits'] == b['deposits'] for a, b in zip(data['layers'], base['layers'])), 'deposit bindings differ from v1')
    check(data['version'] == 1 and data['width'] == 720 and data['height'] == 360, 'file header changed')

    # --- famous deposits
    spot = []
    for name, lat, lon, code, minimum in R.FAMOUS:
        v0 = R.value(g, code, lat, lon, 0); v1 = R.value(g, code, lat, lon, 1)
        spot.append({'name': name, 'code': code, 'cell': round(v0, 2), 'ring1': round(v1, 2), 'min': minimum, 'status': R.classify(v1)})
        check(v1 >= minimum - 1e-6, f'{name} {code}: {v1:.2f} < {minimum}')
    report['famous'] = spot
    tracts = []
    for name, lat, lon, code in R.REPAIRED_TRACTS:
        y, x = R.cell(lat, lon); v = g[code][0][1, y, x] / 255
        tracts.append({'name': name, 'potential': round(float(v), 2)})
        check(v >= .2, f'{name}: potential {v:.2f} < 0.2')
    report['repairedTracts'] = tracts
    former = []
    for name, lat, lon, code, minimum, better in R.FORMER_GAPS:  # v2 gaps, filled by the authored best-guess layer in v3
        v0 = R.value(g, code, lat, lon, 0); v1 = R.value(g, code, lat, lon, 1)
        former.append({'name': name, 'code': code, 'cell': round(v0, 2), 'ring1': round(v1, 2), 'min': minimum, 'status': R.classify(v1), 'betterSource': better})
        check(v1 >= minimum - 1e-6, f'former gap {name} {code}: {v1:.2f} < {minimum} (authored best-guess layer missing?)')
    report['formerGaps'] = former
    report['knownGaps'] = [{'name': n, 'code': c, 'ring1': round(R.value(g, c, la, lo, 1), 2), 'fix': why} for n, la, lo, c, why in R.KNOWN_GAPS]
    report['extra'] = [{'name': n, 'code': c, 'cell': round(R.value(g, c, la, lo, 0), 2), 'ring1': round(R.value(g, c, la, lo, 1), 2)} for n, la, lo, c in R.EXTRA]
    report['bestGuessSpots'] = [{'name': n, 'code': c, 'cell': round(R.value(g, c, la, lo, 0), 2), 'ring1': round(R.value(g, c, la, lo, 1), 2)} for n, la, lo, c in R.BEST_GUESS_SPOTS]

    # --- the authored best-guess layer may never outrank survey evidence tiers
    cfg = json.loads((R.ROOT / 'tools/ore-heatmap-settings.json').read_text(encoding='utf8'))
    ab = cfg['authoredBestGuess']; peaks = [v for row in ab['peak'].values() for v in row.values()]
    check(max(peaks) <= ab['maxPeak'] <= cfg['tiers']['deposit_large']['peak'] < cfg['tiers']['major']['peak'],
          'authored best-guess peaks exceed the deposit_large tier or reach the Major Deposits tier')

    # --- regional coverage and survey-bias metrics
    mt = R.metrics(g, m); report['metrics'] = mt
    for cont, v in mt['continents'].items():
        check(v['anyMetalHeatShare'] >= .5, f'{cont}: only {v["anyMetalHeatShare"]:.0%} of ice-free land has any metal heat (<50%)')
    check(mt['usToRestSaturationRatio'] <= 3.0, f'US/rest-of-world saturation ratio {mt["usToRestSaturationRatio"]} > 3')
    check(mt['usHeatMassShare'] <= .2, f'US holds {mt["usHeatMassShare"]:.0%} of all metal heat (> 20%)')

    # --- commodity-specific regressions from the review
    k = np.stack([g['K'][0][i] for i in range(3)]); na = np.stack([g['NaCl'][0][i] for i in range(3)])
    check(bool((na >= k).all()), 'some cells have potash (K) stronger than parent halite (NaCl)')
    s_lit = int(((g['S'][0].max(0) > 0) & m['land']).sum()); report['sulfurLitLandCells'] = s_lit
    check(s_lit <= 3000, f'native sulfur lit on {s_lit} land cells; pyrite-only records leaking in?')
    peat_lit = int(((g['peat'][0].max(0) > 0) & m['free']).sum()); report['peatLitCells'] = peat_lit
    check(peat_lit >= 3000, f'peat lit on only {peat_lit} cells')
    lv = R.region_counts(g, m, 'Latvia'); report['latvia'] = lv
    check(lv.get('peat', {}).get('atLeast', 0) >= 20, 'Latvia: fewer than 20 land cells with peat >= 0.5')
    check(lv.get('clay', {}).get('max', 0) >= .7, 'Latvia: no clay deposit-strength (>=0.7) cell')
    def box_share(code, a0, a1, o0, o1, t=.5):
        ys = slice(int((90 - a1) * 2), int((90 - a0) * 2)); xs = slice(int((o0 + 180) * 2), int((o1 + 180) * 2))
        a = g[code][0].max(0)[ys, xs][m['land'][ys, xs]]
        return float((a >= t * 255).mean())
    bw = box_share('gypsum', 47.5, 49.8, 7.5, 10.5); report['gypsumBadenWuerttembergShareAtLeast0.5'] = round(bw, 3)
    check(bw <= .2, f'gypsum >= 0.5 on {bw:.0%} of Baden-Wuerttemberg (formation polygons over-weighted)')
    ir = box_share('gypsum', 25.5, 39.5, 45, 61, t=.01); report['gypsumIranLitShare'] = round(ir, 3)
    check(ir >= .1, f'gypsum lit on only {ir:.0%} of Iran (evaporite priors missing)')
    report['failures'] = failures
    return report


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--grid', default=str(R.SHIPPED))
    ap.add_argument('--json')
    a = ap.parse_args()
    rep = run(Path(a.grid))
    if a.json: Path(a.json).write_text(json.dumps(rep, indent=2), encoding='utf8')
    for f in rep['failures']: print('FAIL:', f)
    gaps = ', '.join(f"{x['name']}={x['ring1']}" for x in rep['knownGaps'])
    print(f"famous deposits: {sum(x['ring1'] >= x['min'] for x in rep['famous'])}/{len(rep['famous'])} pass; "
          f"former gaps (best-guess layer): {sum(x['ring1'] >= x['min'] for x in rep['formerGaps'])}/{len(rep['formerGaps'])} pass; known data gaps (not asserted): {gaps}")
    print('PASS' if not rep['failures'] else f"FAILED ({len(rep['failures'])} checks)")
    sys.exit(1 if rep['failures'] else 0)


if __name__ == '__main__':
    main()
