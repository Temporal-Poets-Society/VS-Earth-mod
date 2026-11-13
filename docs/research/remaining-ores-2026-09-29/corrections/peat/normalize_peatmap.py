"""PEATMAP -> 0.5-degree peatland area-fraction grid for the ore heat-map bake.

Source: Xu, J., Morris, P.J., Liu, J., Holden, J. (2017/2018) PEATMAP: Refining estimates of global peatland
distribution based on a meta-analysis. University of Leeds dataset, doi:10.5518/252, CC BY 4.0
(https://archive.researchdata.leeds.ac.uk/251/), downloaded 2026-10-02 into raw/peatmap/ (six regional zips).
Polygons are stored in World Cylindrical Equal Area (WGS 84, standard parallel 0): x = R*lon, y = R*sin(lat).
They are inverse-projected exactly, repaired with make_valid, weighted by the polygon's peat-cover percentage where
the layer provides one (Canada: PEAT_PER), rasterised with the same covered-area routine as
tools/bake_ore_heatmaps.py and summed per cell within each layer (capped at 1); layers are combined by maximum because
regional/national layers can overlap.

Output: corrections/peat/normalized/peatmap-fraction-0p5deg.npz with 'fraction' float32 (360, 720), north-up.
"""
from pathlib import Path
import hashlib, json, math, sys
import numpy as np
import shapely
from pyogrio.raw import read

ROOT = Path(__file__).resolve().parents[2]
EARTH = ROOT.parents[2]
sys.path.insert(0, str(EARTH / 'tools'))
import bake_ore_heatmaps as B  # noqa: E402

RAW = ROOT / 'raw/peatmap'
OUT = ROOT / 'corrections/peat/normalized'
OUT.mkdir(parents=True, exist_ok=True)
R = 6378137.0
LAYERS = {
    'Africa.zip': ['Africa/AF_Peatland.shp'],
    'Asia.zip': ['Asia/EA_Peatland.shp', 'Asia/Histosols_Hokkaido_Mongolia_North Korea.shp', 'Asia/NEA_Peatland.shp', 'Asia/SEA_Peatland.shp', 'Asia/SIB_Peatland.shp'],
    'Europe.zip': ['Europe/British Isles Peatland.shp', 'Europe/Finland_Peatland.shp', 'Europe/Norway_Peatland.shp', 'Europe/Other_European_Peatland.shp', 'Europe/Sweden_Peatland.shp'],
    'North_America.zip': ['North America/Canada_Peatland.shp', 'North America/Other_North_American_Peatlands.shp', 'North America/USA_Peatland.shp'],
    'Oceania.zip': ['Oceania/Oceania_Peatland.shp'],
    'South_America.zip': ['South America/SA_Peatland.shp'],
}


def unproject(coords):
    x = coords[:, 0] / R; y = np.clip(coords[:, 1] / R, -1, 1)
    return np.column_stack([np.degrees(x), np.degrees(np.arcsin(y))])


def main():
    frac = np.zeros((360, 720), np.float64); frac_all = np.zeros((360, 720), np.float64); stats = {}; hashes = {}
    for zname, layers in LAYERS.items():
        path = RAW / zname
        hashes[zname] = hashlib.sha256(path.read_bytes()).hexdigest()
        for layer in layers:
            info = __import__('pyogrio').read_info('/vsizip/' + path.as_posix() + '/' + layer)
            # Canada's layer (Tarnocai et al. peatland map) gives the peat-covered percentage of each polygon.
            pct_field = 'PEAT_PER' if 'PEAT_PER' in list(info['fields']) else None
            meta, _, wkb, cols = read('/vsizip/' + path.as_posix() + '/' + layer, read_geometry=True, columns=[pct_field] if pct_field else [])
            geoms = shapely.from_wkb(wkb)
            share = np.clip(cols[0].astype(float) / 100, 0, 1) if pct_field else np.ones(len(geoms))
            keep = ~shapely.is_missing(geoms); n = int(keep.sum())
            # Explode multipolygons first: repairing one continent-sized multipolygon at once is very slow.
            parts, idx = shapely.get_parts(geoms[keep], return_index=True); w = share[keep][idx]
            parts = shapely.transform(parts, unproject)
            bad = ~shapely.is_valid(parts); repaired = int(bad.sum())
            if repaired:
                fixed, fidx = shapely.get_parts(shapely.make_valid(parts[bad]), return_index=True)
                parts = np.concatenate([parts[~bad], fixed]); w = np.concatenate([w[~bad], w[bad][fidx]])
            ok = (shapely.get_type_id(parts) == 3) & ~shapely.is_empty(parts)
            parts = parts[ok]; w = w[ok]
            b = shapely.bounds(parts)
            x0 = np.floor((b[:, 0] + 180) * 2).astype(int); x1 = np.floor((b[:, 2] + 180) * 2).astype(int)
            y0 = np.floor((90 - b[:, 3]) * 2).astype(int); y1 = np.floor((90 - b[:, 1]) * 2).astype(int)
            single = (x0 == x1) & (y0 == y1) & (y0 >= 0) & (y0 < 360)
            np.add.at(frac, (y0[single], np.mod(x0[single], 720)), w[single] * shapely.area(parts[single]) / .25)
            for g, wi in zip(parts[~single], w[~single]):
                for part, rep, unw in B.polygon_parts(g):
                    ys, xs, fr = B.polygon_fractions(part)
                    np.add.at(frac, (ys, xs), wi * fr)
            # Layers from different national/regional sources can overlap: combine layers by maximum, not sum.
            np.maximum(frac_all, np.minimum(frac, 1), out=frac_all); frac[:] = 0
            stats[layer] = {'features': n, 'repaired_parts': int(repaired)}
            print(layer, stats[layer], flush=True)
    frac = frac_all.astype(np.float32)
    np.savez_compressed(OUT / 'peatmap-fraction-0p5deg.npz', fraction=frac)
    lat = 89.75 - np.arange(360) / 2
    cell_km2 = (111.195 * .5) ** 2 * np.cos(np.radians(lat))[:, None]
    summary = {'source': 'PEATMAP (Xu et al. 2018), doi:10.5518/252, CC BY 4.0', 'zip_sha256': hashes, 'layers': stats,
               'cells_with_peat': int((frac > 0).sum()), 'cells_ge_10pct': int((frac >= .1).sum()),
               'approx_peat_area_million_km2': round(float((frac * cell_km2).sum()) / 1e6, 2),
               'method': 'exact inverse cylindrical-equal-area projection; make_valid; per-cell covered fraction summed and capped at 1'}
    (OUT.parent / 'peatmap-summary.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps({k: summary[k] for k in ['cells_with_peat', 'cells_ge_10pct', 'approx_peat_area_million_km2']}))


if __name__ == '__main__':
    main()
