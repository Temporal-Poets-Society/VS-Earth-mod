"""Validate research exports and write provenance inventories; no runtime changes."""
from __future__ import annotations

import collections
import csv
import gc
import gzip
import hashlib
import json
import math
import pathlib
import re
import struct
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent
NORM = ROOT / 'normalized'


def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(4 * 1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def readjson(path):
    op = gzip.open if str(path).endswith('.gz') else open
    with op(path, 'rt', encoding='utf-8') as stream:
        return json.load(stream)


def rows(path):
    op = gzip.open if str(path).endswith('.gz') else open
    with op(path, 'rt', encoding='utf-8-sig', newline='') as stream:
        yield from csv.DictReader(stream)


def writejson(name, value):
    (ROOT / name).write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def coord(x, y):
    x, y = float(x), float(y)
    assert math.isfinite(x) and math.isfinite(y) and -180 <= x <= 180 and -90 <= y <= 90
    return x, y


def inventories():
    sources = readjson(ROOT / 'specialist-source-inventory.json')
    for item in sources:
        item.update(acquisition_state='acquired', role='observed mineral source records and available grades',
                    crs='EPSG:4326', precision='Source points retained; heterogeneous original precision',
                    limitations='Selective legacy compilation; source records overlap other databases and are not unique deposit counts')
    sources.extend([
        dict(source_id='usgs_mrds_digest', title='Mineral Resources Data System digest',
             source_url='https://mrdata.usgs.gov/mrds/', publication_year=None,
             acquisition_state='reused existing download without modification',
             raw_archive='../copper-tin-global-2026-09-29/raw/mrds-csv.zip',
             role='Observed commodity/mineral source records and source chronology', crs='EPSG:4326',
             precision='Original digest coordinates retained; per-record uncertainty usually unspecified',
             reuse_terms='USGS public data; cite USGS and original record references. No USGS endorsement.',
             limitations='Strong US reporting bias; incomplete and dated outside US; a commodity is not a mineral species or mineable ore'),
        dict(source_id='usgs_global_potash_2014', title='Global assessment of undiscovered potash resources',
             source_url='https://pubs.usgs.gov/sir/2010/5090/s/', publication_year=2014,
             acquisition_state='acquired', raw_archive='raw/potash-PotashGIS.zip',
             role='Expert permissive tracts plus typed mineral/structural/administrative site records', crs='EPSG:4326',
             precision='Map-scale dependent; commonly 1:1,000,000 or coarser. Source maps range 1:50,000 to 1:18,000,000; boundaries may be displaced kilometres.',
             reuse_terms='Use constraints and accuracy caveats retained in raw/Potash_Tracts_metadata.xml and raw/Potash_Deposits_metadata.xml; attribution and no endorsement.',
             limitations='84 tracts, seven invalid source geometries preserved but not plotted. Assessment depth commonly 3 km; brines and buried potash do not establish accessible halite.'),
        dict(source_id='usgs_fsu_coal_2001', title='Coal resources of the former Soviet Union',
             source_url='https://pubs.usgs.gov/of/2001/ofr-01-104/', publication_year=2001,
             acquisition_state='two ASCII point layers acquired; partial basin shapefile withheld',
             raw_archive='raw/fsu-coal_dep.gen; raw/fsu-coal_pt.gen',
             role='Regional coal source points, including coal-quality sampling locations',
             crs='Original EPSG:4284 transformed to EPSG:4326; source coordinates and per-point operation retained',
             precision='Coal-layer metadata state coordinates specified to nearest degree; displayed decimals are not an accuracy claim',
             reuse_terms='Coal-layer metadata Use_Constraints=None. Separately restricted ESRI coastline/country layers in parent project were not acquired or used.',
             limitations='446 records from overlapping layers, not unique deposits; coal rank/name joins withheld pending documented attribute schema; regional and dated.'),
        dict(source_id='usgs_deposit_models', title='Mineral deposit models, USGS Bulletin 1693',
             source_url='https://pubs.usgs.gov/bul/b1693/html/bull7tkj.htm', publication_year=1986,
             acquisition_state='model index acquired; individual primary model references catalogued',
             raw_archive='raw/deposit-models-toc.html', role='Narrative geological permissiveness models',
             crs=None, precision=None, reuse_terms='USGS publication; attribution retained.',
             limitations='Narrative models do not define eligible geographic polygons. Eighteen commodity model reviews remain pending.'),
        dict(source_id='usgs_pp1802_candidate', title='Critical mineral resources, PP1802 shapefiles',
             source_url='https://mrdata.usgs.gov/pp1802/', publication_year=2017,
             acquisition_state='NOT imported: ScienceBase archive request returned HTTP 403',
             raw_archive='raw/pp1802-page.html', role='Candidate future complementary evidence', crs=None, precision=None,
             reuse_terms='Archive not acquired; verify per-layer terms before import.', limitations='No records counted or drawn.'),
        dict(source_id='gem_coal_candidate', title='Global Coal Mine Tracker',
             source_url='https://globalenergymonitor.org/projects/global-coal-mine-tracker', publication_year=None,
             acquisition_state='NOT imported: discovered download form; no form submitted',
             raw_archive='raw/gem-coal-download.html', role='Candidate future modern coal mine evidence', crs=None, precision=None,
             reuse_terms='Verify release-specific terms before acquisition or redistribution.',
             limitations='Modern mine tracker alone will not establish historical workings or coal-basin permissiveness. No records counted or drawn.'),
        dict(source_id='vanilla_local_assets', title='Installed Vintage Story 1.22.7 asset audit',
             source_url=None, publication_year=None, acquisition_state='read only; hashes and selected declarations exported',
             raw_archive=None, role='Actual game mineral/deposit scope and setting patches', crs=None, precision=None,
             reuse_terms='Proprietary installed game assets are not redistributed; only audit metadata included.',
             limitations='Asset declarations do not prove all variants have natural deposit generators.'),
        dict(source_id='geoaddons_optional_archive', title='Geology Additions 1.4.8 archive audit',
             source_url='https://mods.vintagestory.at/show/mod/8054', publication_year=None,
             acquisition_state='existing uninstalled archive read only', raw_archive='../regions-2026-09-29/geoaddons.zip',
             role='Optional provider mineral scope', crs=None, precision=None,
             reuse_terms='Archive/assets not copied into this package or runtime; upstream mod terms apply.',
             limitations='No guarantee of current installed compatibility; source mineral names do not identify world occurrence ranges.'),
        dict(source_id='vintage_story_official_code', title='Official deposit generator and world-setting source',
             source_url='https://github.com/anegostudios/vsessentialsmod/tree/06673b67318e002332b6aa8b3d0308f11d2aba99', publication_year=None,
             acquisition_state='pinned source files acquired for inspection',
             raw_archive='raw/GenDeposits.cs; raw/DepositVariant.cs; raw/DiscGenerator.cs; raw/Disc-ChildDeposit.cs; raw/Deposits.cs; raw/AssemblyInfo.cs',
             role='Frequency-versus-grade semantics; future Earth integration requirement', crs=None, precision=None,
             reuse_terms='Upstream repository terms apply to copied code. Research excerpts must remain outside runtime releases.',
             limitations='vsessentials commit 06673b67318e002332b6aa8b3d0308f11d2aba99; vssurvival commit 849fa8cad9e392368566efc7474e73db6404a145. Current source not asserted binary-identical to installed 1.22.7.'),
        dict(source_id='earth_preview_basemap', title='Existing Earth preview land/water mask',
             source_url=None, publication_year=None, acquisition_state='existing file read without modification',
             raw_archive='../../../preview/grids.npz', role='Map display background only', crs='Geographic display grid',
             precision='Terrain preview mask; not an ore evidence source', reuse_terms='Existing Earth project research dependency; upstream terrain terms remain applicable.',
             limitations='No geological abundance or survey-completeness information inferred from this mask.')
    ])
    for case in readjson(ROOT / 'historical-evidence.json'):
        sources.append(dict(source_id='history:' + case['id'], title=case['place'],
                            source_url='; '.join(case['source_urls']), publication_year=None,
                            acquisition_state='reviewed and cited; protected text/maps not reproduced',
                            raw_archive=None, role='Historical extraction/processing context', crs=None, precision=None,
                            reuse_terms='Brief attributed paraphrase; source publisher rights apply; no heritage geometry copied.',
                            limitations=case['spatial_status']))
    for item in sources:
        item['snapshot_date'] = '2026-09-29'
        item['global_complete'] = False
        item['generation_ready'] = False
    writejson('source-inventory.json', sources)
    fields = sorted({k for s in sources for k in s})
    with (ROOT / 'source-inventory.csv').open('w', encoding='utf-8', newline='') as stream:
        writer = csv.DictWriter(stream, fieldnames=fields)
        writer.writeheader()
        writer.writerows(sources)
    return sources


def main():
    sources = inventories()
    audit = {'snapshot_date': '2026-09-29', 'status': 'passed', 'runtime_changes': False,
             'confirmed_unique_geological_deposits': None, 'generation_ready': False}
    mrds = readjson(ROOT / 'validation-summary.json')
    inputpath = ROOT.parent / mrds['mrds_input_file']
    assert digest(inputpath) == mrds['mrds_input_sha256']
    audit['reused_mrds_sha256_verified'] = True
    base = {}
    for r in rows(NORM / 'mrds-records.csv.gz'):
        assert r['record_id'] not in base
        assert r['source_status'].lower() != 'plant'
        assert not r['grade_value'] and not r['economic_status'] and not r['early_accessibility']
        base[r['record_id']] = coord(r['longitude'], r['latitude'])
    assert len(base) == mrds['retained_unique_source_records'] == 185733
    audit['mrds_source_records'] = len(base)
    audit['mrds_zero_longitude_latitude_records'] = sum(v == (0., 0.) for v in base.values())
    links = collections.defaultdict(set)
    nlinks = 0
    for r in rows(NORM / 'record-commodities.csv.gz'):
        assert r['record_id'] in base and r['record_id'] not in links[r['commodity']]
        links[r['commodity']].add(r['record_id']); nlinks += 1
    assert nlinks == mrds['commodity_evidence_links'] == 321772
    audit['mrds_commodity_links'] = nlinks
    nminerals = 0
    for r in rows(NORM / 'record-explicit-minerals.csv.gz'):
        assert r['record_id'] in base and r['evidence_basis'] == 'explicit_ore_field_mention'
        nminerals += 1
    assert nminerals == mrds['explicit_mineral_links'] == 52862
    audit['explicit_ore_mineral_links'] = nminerals
    coverage = list(rows(NORM / 'commodity-coverage.csv'))
    assert len(coverage) == len(links) == 40
    for c in coverage:
        code = c['commodity']; ids = set()
        for r in rows(NORM / (code + '-occurrences.csv.gz')):
            assert coord(r['longitude'], r['latitude']) == base[r['record_id']]
            assert r['record_id'] not in ids
            ids.add(r['record_id'])
        assert ids == links[code] and len(ids) == int(c['source_records'])
        data = readjson(NORM / (code + '-occurrences.geojson.gz'))
        assert {f['properties']['record_id'] for f in data['features']} == ids
        for f in data['features']:
            assert tuple(f['geometry']['coordinates']) == base[f['properties']['record_id']]
            # Compact MRDS geometry joins to the full CSV by record_id.
            assert f['properties'].get('grade_value') is None
        del data; gc.collect()
    audit['mrds_commodity_csv_geojson_layers_verified'] = 40
    specialist = {}
    for r in rows(NORM / 'specialist-records.csv'):
        assert r['record_id'] not in specialist
        assert r['source_status'] not in ('Cross Reference', 'HANDLING')
        specialist[r['record_id']] = coord(r['longitude'], r['latitude'])
    assert len(specialist) == 24757
    slinks = collections.defaultdict(set)
    for r in rows(NORM / 'specialist-commodity-links.csv'):
        assert r['record_id'] in specialist and r['record_id'] not in slinks[r['commodity']]
        slinks[r['commodity']].add(r['record_id'])
    assert sum(map(len, slinks.values())) == 41294
    for c in rows(NORM / 'specialist-coverage.csv'):
        data = readjson(NORM / (c['commodity'] + '-specialist.geojson'))
        assert {f['properties']['record_id'] for f in data['features']} == slinks[c['commodity']]
        assert len(data['features']) == int(c['specialist_source_records'])
        for f in data['features']:
            assert tuple(f['geometry']['coordinates']) == specialist[f['properties']['record_id']]
    grades = list(rows(NORM / 'specialist-grades.csv'))
    assert len(grades) == 3838
    for g in grades:
        assert g['record_id'] in specialist and float(g['grade_value']) > 0
        assert g['grade_unit'] and g['grade_basis'] and not g['economic_viability']
        if g['grade_unit'].startswith('wt%'):
            assert float(g['grade_value']) <= 100
    audit.update(specialist_source_records=len(specialist), specialist_commodity_links=41294,
                 positive_grade_rows=len(grades), specialist_commodity_geojson_layers_verified=40)
    tracts = readjson(NORM / 'PotashTracts.geojson')['features']
    assert len(tracts) == 84 and sum(not f['properties']['geometry_valid'] for f in tracts) == 7
    deps = readjson(NORM / 'PotashDeps.geojson')['features']
    assert len(deps) == 981
    for f in deps:
        coord(*f['geometry']['coordinates'])
    coal = readjson(NORM / 'fsu-coal-points.geojson')['features']
    assert len(coal) == 446
    for f in coal:
        coord(*f['geometry']['coordinates'])
        p = f['properties']
        coord(p['source_longitude'], p['source_latitude'])
        assert p['source_crs'] == 'EPSG:4284' and p['output_crs'] == 'EPSG:4326'
        assert p['coal_rank'] is None and p['early_accessibility'] is None
    audit.update(potash_source_tracts=84, invalid_source_tracts_preserved_withheld_from_map=7,
                 potash_typed_point_records=981, coal_source_points=446)
    historical = readjson(ROOT / 'historical-evidence.json')
    assert len(historical) == 8 and all(c['geometry'] is None for c in historical)
    assets = readjson(ROOT / 'game-asset-audit.json')
    assert not assets['parse_errors']
    scope = list(rows(ROOT / 'game-mineral-scope.csv'))
    assert len(scope) == 82
    potential = readjson(ROOT / 'permissiveness-review-catalogue.json')
    assert len(potential) == 40 and all(not c['low_chance_background_allowed_now'] for c in potential)
    audit.update(asset_files_audited=len(assets['assets']), asset_parse_errors=0,
                 scope_rows=len(scope), historical_cases_without_invented_geometry=8,
                 commodities_with_review_checklist=40)
    downloads = []
    failures = []
    for manifest in ['acquisition-manifest.json', 'coal-acquisition-manifest.json']:
        for item in readjson(ROOT / manifest):
            if item.get('sha256'):
                p = ROOT / item['file']
                assert p.is_file() and p.stat().st_size == item['bytes'] and digest(p) == item['sha256']
                downloads.append(item['file'])
            else:
                failures.append(item)
    audit['download_hashes_verified'] = len(downloads)
    audit['documented_acquisition_failures'] = failures
    archives = list((ROOT / 'raw').glob('*.zip'))
    for path in archives:
        with zipfile.ZipFile(path) as z:
            assert z.testzip() is None
    audit['new_archive_crc_checks'] = len(archives)
    pngs = []
    for path in sorted((ROOT / 'maps').glob('*.png')):
        raw = path.read_bytes()
        assert raw[:8] == b'\x89PNG\r\n\x1a\n'
        width, height = struct.unpack('>II', raw[16:24])
        assert width >= 1500 and height >= 900 and raw[-8:-4] == b'IEND'
        pngs.append(dict(file=path.relative_to(ROOT).as_posix(), width=width, height=height))
    assert len(pngs) == 49
    audit['png_headers_and_completion_verified'] = pngs
    html = (ROOT / 'index.html').read_text(encoding='utf-8')
    for link in re.findall(r'(?:href|src)="([^"]+)"', html):
        if not link.startswith(('https:', 'http:', '#')):
            assert (ROOT / link).exists(), link
    audit['atlas_local_links_verified'] = True
    for path in ROOT.glob('*.py'):
        compile(path.read_text(encoding='utf-8'), str(path), 'exec')
    audit['research_script_syntax_verified'] = True
    audit['source_inventory_entries'] = len(sources)
    audit['remaining_gaps'] = ['No globally complete evidence claim', 'Cross-source deposit entity matching incomplete',
                              'Most commodities lack spatial permissive geology', 'Historical geometry and accessibility incomplete',
                              'Economic status cannot be inferred from source grade', 'Coal ranks/name joins withheld',
                              'PP1802 and GEM coal datasets not imported']
    files = []
    for path in sorted(ROOT.rglob('*')):
        if path.is_file() and '__pycache__' not in path.parts and path.name not in ('package-manifest.json', 'final-validation.json'):
            files.append(dict(file=path.relative_to(ROOT).as_posix(), bytes=path.stat().st_size, sha256=digest(path)))
    report = ROOT.parents[1] / 'remaining-ore-evidence.md'
    manifest = dict(snapshot_date='2026-09-29', hash_algorithm='sha256', files=files,
                    report=dict(file='../../remaining-ore-evidence.md', bytes=report.stat().st_size, sha256=digest(report)),
                    exclusions=['package-manifest.json and final-validation.json avoid recursive hashes', '__pycache__ is not research output'])
    writejson('package-manifest.json', manifest)
    audit['package_hashed_files'] = len(files)
    audit['package_bytes_excluding_manifests_and_report'] = sum(f['bytes'] for f in files)
    writejson('final-validation.json', audit)
    print(json.dumps({k: v for k, v in audit.items() if k not in ('png_headers_and_completion_verified', 'documented_acquisition_failures', 'remaining_gaps')}, indent=2))


if __name__ == '__main__':
    main()
