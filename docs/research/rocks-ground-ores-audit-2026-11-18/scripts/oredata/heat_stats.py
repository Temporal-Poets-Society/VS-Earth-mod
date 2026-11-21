"""Per-code heat-map statistics (v2): lit land cells by plane, prior-only share, regional share vs land share."""
import csv, json, sys
from collections import defaultdict
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import numpy as np
import shapely
from common import load_heat, land_ice, cell_area_km2, Countries, region_of, COMPACT, COMPACT_ORDER, W, H

HERE = Path(__file__).resolve().parent

def region_grid(C):
    lats = 89.75 - np.arange(H) / 2.0; lons = np.arange(W) / 2.0 - 179.75
    LON, LAT = np.meshgrid(lons, lats)
    idx = C.lookup_many(LAT.ravel(), LON.ravel())
    reg = np.empty(W * H, dtype=object)
    for i, (k, lo) in enumerate(zip(idx, LON.ravel())):
        if k < 0: reg[i] = 'Ocean/none'
        else:
            r = C.rows[k]; reg[i] = COMPACT[region_of(r['a3'], r['continent'], r['subregion'], lo)]
    cty = np.array([C.rows[k]['admin'] if k >= 0 else '' for k in idx], dtype=object)
    return reg.reshape(H, W), cty.reshape(H, W)

def main():
    heat, meta = load_heat(2)
    land, ice = land_ice()
    area = cell_area_km2()
    C = Countries()
    reg, cty = region_grid(C)
    np.savez_compressed(HERE / 'grids-cache.npz', land=land, ice=ice, reg=reg.astype(str), cty=cty.astype(str))
    landok = (land >= .5) & (ice < .5)
    land_area = area * landok
    tot_land = land_area.sum()
    reg_land = {o: land_area[(reg == o)].sum() / tot_land for o in COMPACT_ORDER}
    rows = []
    for code, L in heat.items():
        obs, pot, deep = L['obs'], L['pot'], L['deep']
        lit = (np.maximum(np.maximum(obs, pot), deep) > 0) & landok
        obsd = ((obs > 0) | (deep > 0)) & landok
        potonly = (pot > 0) & ~((obs > 0) | (deep > 0)) & landok
        strong = (np.maximum(obs, deep) >= .5) & landok
        best = np.maximum(np.maximum(obs, pot), deep) * landok
        mass = best * area
        obsmass = np.maximum(obs, deep) * landok * area
        r = {'code': code, 'label': L['label'],
             'lit_land_cells': int(lit.sum()), 'lit_land_%': round(100 * (area * lit).sum() / tot_land, 1),
             'obs_or_deep_cells': int(obsd.sum()), 'pot_only_cells': int(potonly.sum()),
             'pot_only_share_of_lit_%': round(100 * potonly.sum() / max(1, lit.sum()), 1),
             'heat_mass_from_pot_only_%': round(100 * (mass * potonly).sum() / max(1e-9, mass.sum()), 1),
             'strong_obs_cells_ge_0.5': int(strong.sum()), 'deep_cells': int(((deep > 0) & landok).sum())}
        om = obsmass.sum()
        for o in COMPACT_ORDER[:9]:
            r[f'obs_mass_share_{o}_%'] = round(100 * obsmass[reg == o].sum() / max(om, 1e-9), 1)
        rows.append(r)
    with open(HERE / 'heat-stats-v2.csv', 'w', newline='', encoding='utf8') as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
    print('land share by region %:', {k: round(100 * v, 1) for k, v in reg_land.items()})
    print('| code | lit land % | obs/deep cells | pot-only cells | pot-only % of lit | heat mass pot-only % | obs>=0.5 cells | ' + ' | '.join(COMPACT_ORDER[:9]) + ' |')
    for r in rows:
        print(f"| {r['code']} | {r['lit_land_%']} | {r['obs_or_deep_cells']} | {r['pot_only_cells']} | {r['pot_only_share_of_lit_%']} | {r['heat_mass_from_pot_only_%']} | {r['strong_obs_cells_ge_0.5']} | " +
              ' | '.join(str(r[f'obs_mass_share_{o}_%']) for o in COMPACT_ORDER[:9]) + ' |')

if __name__ == '__main__':
    main()
