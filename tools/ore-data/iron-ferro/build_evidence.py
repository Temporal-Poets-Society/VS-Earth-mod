"""Heat-map evidence and overrides for the iron-ferro layer codes (decision 0065).

Writes tools/ore-data/iron-ferro/evidence.geojson and heatmap-overrides.json, read by tools/bake_ore_heatmaps.py.

Why (see docs/research/ore-deposits-2026-11-08/iron-ferro.md):
* The USGS Ni-Cr-PGE compilation (nicrpge, OFR 2009-1045) lists every ultramafic occurrence with any Ni, Cr or PGE
  mention: 5,112 Pt, 8,710 Ni and 7,072 Cr records, heavily North American. Together with MRDS (2,817 Pt records, many
  by-product PGE in gold placers; 5,543 Cr records, 75 % small US pods) they light Pt, Ni and Cr across the western US
  and Canada. nicrpge is itself an index of MRDS and other databases, so for Pt, Ni and Cr both the specialist files
  (major deposits + nicrpge + laterite + podiform chromite) and MRDS are dropped and re-added here:
  - USGS Major Mineral Deposits records: tier 'major';
  - nicrpge records only where the status says the site was worked or is a deposit (peaks per code in TIERS) and the
    record's commodities all belong to the code's deposit family (FAMILY). The order of nicrpge's source commodity
    labels is not "main commodity first" (Esterley Chrome Mine reads Pd;Cr;Ir;Rh;Ru;Pt;PGE), so it cannot be used.
    Pt keeps only sites whose sole commodity is PGE (Ural, Japanese and Australian placers, reef mines): PGE as a
    by-product of gold placers, chromite pods and Ni-Cu sulphides is not a platinum deposit (the Ni-Cu-PGE sulphides
    carry a sperrylite share in layer Ni). Ni and Cr drop polymetallic sites (Pb, Zn, U, Mo...) where they are by-products;
  - the catalogue (ore-deposits-v1/iron-ferro.json): every deposit as a point with a peak from its ore tonnage and a
    radius from its footprint, every area as a polygon (observed plane, weight 0.8).
* Fe, Mn, Ti, V and Al keep all existing sources and gain the catalogue evidence.

Run:  C:\\VS mods\\Earth\\.venv\\Scripts\\python.exe -I tools/ore-data/iron-ferro/build_evidence.py
"""
from pathlib import Path
import json, re

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
ATLAS = ROOT / 'docs/research/remaining-ores-2026-09-29/normalized'
CATALOGUE = ROOT / 'assets/earth/config/ore-deposits-v1/iron-ferro.json'

FAMILY = {'Pt': {'Pt'}, 'Ni': {'Ni', 'Cu', 'Co', 'Pt', 'Au', 'Ag', 'Fe', 'Cr', 'S'}, 'Cr': {'Cr', 'Fe', 'Ni', 'Pt', 'SiO2', 'Au'}}
WORKED = re.compile(r'^(producer|worked deposit|deposit|deposit \(undivided\)|mine|dep=sito|small deposit)$', re.I)
PAST = re.compile(r'^past producer$', re.I)
# (worked or deposit, past producer) -> (peak, radius km). Pt: mostly small placer and by-product sites, so below the
# deposit tier; the PGE centres come from the catalogue. Cr: past-producing pods come back with their tonnage from the
# USGS podiform-chromite catalogue entries, so only producers and deposits are re-added here.
TIERS = {'Pt': ((.5, 40), (.4, 35)), 'Ni': ((.75, 60), (.55, 45)), 'Cr': ((.6, 45), None)}


def size_peak(mt):
    """Peak and kernel radius (km) by ore tonnage."""
    for lim, peak, rad in [(1000, .95, 75), (100, .85, 70), (10, .75, 60), (1, .6, 45), (.1, .45, 35), (.01, .3, 25)]:
        if mt >= lim: return peak, rad
    return .2, 20


def point(code, lon, lat, peak, radius, source, rid, name, country=None):
    p = {'code': code, 'source': source, 'peak': round(peak, 3), 'radiusKm': radius, 'id': rid, 'name': name}
    if country: p['country'] = country
    return {'type': 'Feature', 'properties': p, 'geometry': {'type': 'Point', 'coordinates': [round(lon, 4), round(lat, 4)]}}


def main():
    feats = []
    cat = json.loads(CATALOGUE.read_text(encoding='utf-8'))
    for d in cat['deposits']:
        peak, rad = size_peak(d['mt'])
        rad = int(min(75, max(rad, 20 + d['lenKm'] / 2)))
        feats.append(point(d['layer'], d['lon'], d['lat'], peak, rad, 'iron-ferro catalogue', d['id'], d['name'], d.get('country')))
    for a in cat['areas']:
        feats.append({'type': 'Feature', 'properties': {'code': a['layer'], 'source': 'iron-ferro catalogue areas', 'weight': 0.8,
                                                         'id': a['id'], 'name': a['name'], 'group': a['id']}, 'geometry': a['geometry']})
    kept = {}
    for code in ['Pt', 'Ni', 'Cr']:
        n = 0
        with (ATLAS / (code + '-specialist.geojson')).open(encoding='utf-8-sig') as f:
            data = json.load(f)
        for ft in data['features']:
            p = ft['properties']; g = ft.get('geometry')
            if not g: continue
            lon, lat = g['coordinates'][:2]
            sid = p.get('source_id')
            if sid == 'usgs_major-deposits':
                feats.append(point(code, lon, lat, .95, 75, 'usgs major deposits (iron-ferro re-add)', p['record_id'], p.get('name'), p.get('country'))); n += 1
            elif sid == 'usgs_nicrpge':
                labels = set((p.get('commodity_labels') or '').split(';'))  # normalised: PGE -> Pt
                st = (p.get('source_status') or '').strip()
                if code not in labels or not labels <= FAMILY[code]: continue
                if WORKED.match(st): peak, rad = TIERS[code][0]
                elif PAST.match(st) and TIERS[code][1]: peak, rad = TIERS[code][1]
                else: continue
                feats.append(point(code, lon, lat, peak, rad, 'usgs nicrpge primary (iron-ferro re-add)', p['record_id'], p.get('name'), p.get('country'))); n += 1
            # usgs_laterite and usgs_podchrome come back through the catalogue (with tonnage); usgs_vms Ni is by-product
        kept[code] = n
    (HERE / 'evidence.geojson').write_text(json.dumps({'type': 'FeatureCollection', 'name': 'iron-ferro evidence', 'features': feats},
                                                      separators=(',', ':'), ensure_ascii=False) + '\n', encoding='utf-8')
    overrides = {'comment': 'Dropped for the iron-ferro codes and re-added selectively in evidence.geojson (see build_evidence.py).',
                 # MRDS records reach the bake as '<code>-occurrences.csv.gz' (the 'mrds' alias does not match them yet)
                 'drop': {c: [c + '-specialist.geojson', c + '-occurrences.csv.gz', 'mrds'] for c in ['Pt', 'Ni', 'Cr']}}
    (HERE / 'heatmap-overrides.json').write_text(json.dumps(overrides, indent=1) + '\n', encoding='utf-8')
    print('features', len(feats), 're-added', kept)


if __name__ == '__main__':
    main()
