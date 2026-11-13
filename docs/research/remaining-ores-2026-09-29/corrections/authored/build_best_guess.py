"""Convert the authored best-guess district table to GeoJSON for the ore heat-map bake.

Input : best-guess-districts.csv (this folder; authored for the Earth project from general knowledge of world mining
        geography, no third-party data). Columns: name, country, lat, lon, commodities (';'-separated runtime layer
        codes), kind (point | district | basin), radius_km, size (world-class | major | medium),
        confidence (high | medium | low), note.
Output: normalized/best-guess-districts.geojson (points; the bake reads per-feature size, confidence and radius_km and
        looks the peak up in tools/ore-heatmap-settings.json -> authoredBestGuess) and best-guess-summary.json.

Every row must name one of the 44 runtime layer codes and must lie INSIDE the Natural Earth 1:10m admin-0 polygon of
its country (no tolerance; stricter than the bake's 0.5 degree check). Rows that fail are dropped and listed in the
summary, and the script exits with status 1 so they get fixed rather than silently lost.

Run: .venv\\Scripts\\python.exe docs/research/remaining-ores-2026-09-29/corrections/authored/build_best_guess.py
"""
from pathlib import Path
from collections import Counter
import csv, json, re, sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[4]
sys.path.insert(0, str(ROOT / 'tools'))
import bake_ore_heatmaps as B  # noqa: E402

SOURCE_ID = 'authored-best-guess-2026-10-02'
LICENCE = 'authored for this project (no third-party data)'
KINDS = {'point': (40, 75), 'district': (45, 120), 'basin': (50, 150)}  # km; >= MRDS producer radius (positions +-10-30 km)
SIZES = ['world-class', 'major', 'medium']
CONFIDENCE = ['high', 'medium', 'low']


def slug(s):
    return re.sub(r'[^a-z0-9]+', '-', s.casefold()).strip('-')


def main():
    cfg = json.loads(B.SETTINGS.read_text())
    ab = cfg['authoredBestGuess']
    cc = cfg['coordinateCheck']
    check = B.CountryCheck(ROOT / cc['table'], cc['marginDegrees'], ROOT / cc['naturalEarth'], cc['polygonToleranceDegrees'])
    rows = list(csv.DictReader((HERE / 'best-guess-districts.csv').open(encoding='utf-8')))
    feats, problems, seen = [], [], set()
    by_code, by_conf, by_size, by_kind, by_country = Counter(), Counter(), Counter(), Counter(), Counter()
    for i, r in enumerate(rows, start=2):
        name = r['name'].strip(); why = []
        codes = [c.strip() for c in r['commodities'].split(';') if c.strip()]
        bad = [c for c in codes if c not in B.CODES]
        if not codes or bad: why.append('unknown layer code(s) ' + ','.join(bad))
        kind, size, conf = r['kind'].strip(), r['size'].strip(), r['confidence'].strip()
        if kind not in KINDS: why.append('kind ' + kind)
        if size not in SIZES: why.append('size ' + size)
        if conf not in CONFIDENCE: why.append('confidence ' + conf)
        try:
            lat, lon, radius = float(r['lat']), float(r['lon']), float(r['radius_km'])
        except ValueError:
            problems.append({'line': i, 'name': name, 'problem': 'non-numeric lat/lon/radius'}); continue
        if kind in KINDS and not (KINDS[kind][0] <= radius <= KINDS[kind][1]): why.append(f'radius {radius} outside {KINDS[kind]} for {kind}')
        if radius > ab['maxRadiusKm']: why.append('radius above maxRadiusKm')
        fid = slug(name)
        if fid in seen: why.append('duplicate name')
        seen.add(fid)
        country = r['country'].strip()
        inside = check.inside(country, lat, lon, margin=0)
        if inside is None: why.append(f'country label {country!r} unknown to the coordinate check')
        elif not inside:
            near = check.inside(country, lat, lon)  # the bake's own 0.5-degree tolerance, for the report only
            why.append(f'outside {country} (Natural Earth, no tolerance; within bake tolerance: {near})')
        if why:
            problems.append({'line': i, 'name': name, 'country': country, 'lat': lat, 'lon': lon, 'problem': '; '.join(why)}); continue
        peak = ab['peak'][size][conf]
        feats.append({'type': 'Feature', 'id': SOURCE_ID + ':' + fid, 'geometry': {'type': 'Point', 'coordinates': [lon, lat]},
                      'properties': {'record_id': SOURCE_ID + ':' + fid, 'source_id': SOURCE_ID, 'source_feature_id': fid,
                                     'name': name, 'country': country, 'commodities': codes, 'kind': kind, 'radius_km': radius,
                                     'size': size, 'confidence': conf, 'peak_at_build': peak, 'note': r['note'].strip() or None,
                                     'evidence_class': 'authored_best_guess_district', 'licence': LICENCE,
                                     'status': 'best-guess, authored from general knowledge, not survey data'}})
        for c in codes: by_code[c] += 1
        by_conf[conf] += 1; by_size[size] += 1; by_kind[kind] += 1; by_country[check.canonical(country)] += 1
    out = HERE / 'normalized'; out.mkdir(exist_ok=True)
    (out / 'best-guess-districts.geojson').write_text(json.dumps({'type': 'FeatureCollection', 'features': feats},
                                                                 ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    summary = {'source_id': SOURCE_ID, 'licence': LICENCE, 'rows': len(rows), 'exported': len(feats), 'dropped': problems,
               'peakTable': ab['peak'], 'byKind': dict(by_kind), 'bySize': dict(by_size), 'byConfidence': dict(by_conf),
               'byLayer': dict(sorted(by_code.items())), 'countries': len(by_country), 'byCountry': dict(by_country.most_common())}
    (HERE / 'best-guess-summary.json').write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding='utf-8')
    for p in problems: print('DROP line', p['line'], p['name'], '-', p['problem'])
    print(json.dumps({k: summary[k] for k in ['rows', 'exported', 'byKind', 'bySize', 'byConfidence', 'countries']}))
    sys.exit(1 if problems else 0)


if __name__ == '__main__':
    main()
