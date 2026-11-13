"""Validate the coal correction and generate a non-spatial known-region audit.

Counts are source records, never independent deposits, tonnage or abundance.
No geometry is fabricated for audit rows with unresolved spatial coverage.
"""
from pathlib import Path
from collections import Counter
from zipfile import ZipFile
import csv, gzip, hashlib, json, math, sys, tempfile
from shapely.geometry import shape
from shapely.validation import explain_validity

ROOT = Path(__file__).resolve().parent
PACKAGE = ROOT.parent.parent
OUT = ROOT / 'normalized'
DATE = '2026-09-29'

def read(path):
    return json.loads(path.read_text(encoding='utf-8'))

def write(name, obj):
    (ROOT / name).write_text(json.dumps(obj, ensure_ascii=False, indent=2), encoding='utf-8')

files = {name: read(OUT / name)['features'] for name in [
    'coal-polygons.geojson', 'coal-context-polygons.geojson',
    'coal-supplement-points.geojson', 'nrw-halite.geojson']}
coal, context, points = [files[n] for n in list(files)[:3]]
errors, invalid, transforms, file_checks = [], [], Counter(), []

for name, features in files.items():
    if len(features) != len({f['id'] for f in features}):
        errors.append('Duplicate IDs: ' + name)
    bounds = [180., 90., -180., -90.]
    for feature in features:
        p = feature['properties']
        geom = shape(feature['geometry'])
        if geom.is_empty or not all(math.isfinite(v) for v in geom.bounds):
            errors.append('Empty/nonfinite geometry: ' + feature['id'])
        b = geom.bounds
        if b[0] < -180 or b[1] < -90 or b[2] > 180 or b[3] > 90:
            errors.append('Out-of-range geometry: ' + feature['id'])
        bounds = [min(bounds[0], b[0]), min(bounds[1], b[1]), max(bounds[2], b[2]), max(bounds[3], b[3])]
        if bool(geom.is_valid) != p['geometry_valid']:
            errors.append('Incorrect validity flag: ' + feature['id'])
        if not geom.is_valid:
            invalid.append(dict(file=name, id=feature['id'], reason=explain_validity(geom)))
        if p['game_generation_ready'] is not False or p['grade_value'] is not None:
            errors.append('Invented generation/grade: ' + feature['id'])
        if not p['source_id'] or not p['source_attributes'] or not p['source_url']:
            errors.append('Missing provenance: ' + feature['id'])
        transforms[p['coordinate_transform']] += 1
    file_checks.append(dict(file=name, features=len(features),
        valid=sum(f['properties']['geometry_valid'] for f in features), bounds=bounds,
        sha256=hashlib.sha256((OUT / name).read_bytes()).hexdigest()))

for mode in ['current', 'archive']:
    expected = read(ROOT / 'raw' / f'pgi-{mode}-coal-count.json')['count']
    raw = read(ROOT / 'raw' / f'pgi-{mode}-coal.geojson')['features']
    native = read(ROOT / 'raw' / f'pgi-{mode}-coal.json')['features']
    assert expected == len(raw) == len(native)
    assert {f['properties']['OBJECTID'] for f in raw} == {f['attributes']['OBJECTID'] for f in native}
assert len(coal) == 3918 and len(context) == 729 and len(points) == 520
assert len(files['nrw-halite.geojson']) == 4
assert not any(f['properties']['source_id'] == 'ga_basins_2013' for f in coal)
assert not any(f['properties']['coal_rank_source'] is not None for f in coal if f['properties']['source_id'] == 'usgs_india_2011')
assert all(f['properties']['source_crs'] == 'EPSG:4240' for f in coal if f['properties']['source_id'] == 'usgs_india_2011')
assert all(f['properties']['coal_rank_source'] in ['Brown', 'Black'] for f in context if f['properties']['source_id'] == 'ga_basins_2013')

hash_checks, archive_checks, response_checks = [], [], []
for entry in read(ROOT / 'acquisition-manifest.json'):
    path = ROOT / entry['file']
    if not path.exists():
        response_checks.append(dict(file=entry['file'], result='not acquired', source_status=entry.get('status')))
        continue
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    ok = digest == entry.get('sha256') and path.stat().st_size == entry.get('bytes')
    hash_checks.append(dict(file=entry['file'], matches=ok))
    if not ok:
        errors.append('Manifest hash/length mismatch: ' + entry['file'])
    if path.suffix == '.zip':
        with ZipFile(path) as archive:
            failed = archive.testzip()
            archive_checks.append(dict(file=entry['file'], members=len(archive.namelist()), crc_ok=failed is None))
            if failed:
                errors.append('ZIP CRC failure: ' + entry['file'])
    if path.suffix in ['.html', '.xml']:
        txt = path.read_text(encoding='utf-8', errors='replace')
        if '_Incapsula_Resource' in txt or 'Request Rejected' in txt:
            response_checks.append(dict(file=entry['file'], result='HTTP response is a WAF/block page, not usable source content'))

# Baseline source rows, not deduplicated deposits. Existing point imports are unchanged.
with gzip.open(PACKAGE / 'normalized/C_coal-occurrences.csv.gz', 'rt', encoding='utf-8', newline='') as handle:
    baseline_mrds = Counter(row['country'] for row in csv.DictReader(handle))
specialists = read(PACKAGE / 'normalized/C_coal-specialist.geojson')['features']
baseline_specialist = Counter(f['properties'].get('country') for f in specialists)
country_polygons = Counter(f['properties']['country'] for f in coal)
country_points = Counter(f['properties']['country'] for f in points)
source_polygons = Counter(f['properties']['source_id'] for f in coal)
sources = read(ROOT / 'source-inventory.json')
rows = []

def region(key, name, countries, status, sources_used, remaining, urls=None, evidence=None):
    rows.append(dict(region_id=key, region=name, country_labels=countries, coverage_status=status,
        evidence_summary=evidence, baseline_mrds_source_rows=sum(baseline_mrds[c] for c in countries),
        baseline_specialist_source_rows=sum(baseline_specialist[c] for c in countries),
        correction_polygon_records=sum(country_polygons[c] for c in countries),
        correction_point_records=sum(country_points[c] for c in countries),
        count_scope='Exact source country labels across whole listed countries, not the named subregion; overlaps and invalid geometries included.',
        imported_sources=sources_used, primary_urls=urls or [sources[s]['url'] for s in sources_used],
        unresolved=remaining, geometry=None, early_accessibility=None, geological_deposit_count=None,
        blank_means_absent=False))

region('britain', 'Britain and Northern Ireland', ['United Kingdom'], 'legacy national polygons acquired', ['bgs_coal_2004'],
    'Source is 1:1 million; no individual seam continuity, coal quality or universal shallow-access claim. Republic of Ireland is outside this layer.',
    evidence='638 strata polygons include surface coal-bearing strata, lignite and concealed coal, including a greater-than-1200 m depth category.')
region('poland', 'Poland: documented hard coal and brown coal', ['Poland'], 'national register plus selected archive polygons acquired', ['pgi_midas'],
    'Selected archives are incomplete; some deposit bounds reconstructed from mining areas. Current register does not imply operating mine.',
    evidence='315 current deposit boundaries and 28 archived deposit boundaries; no operating-status or reserve-size filter.')
region('germany', 'Germany: NRW and Saxony acquired; national coverage incomplete', ['Germany'], 'partial state polygons acquired', ['nrw_rk500','saxony_ruk400','saxony_lignite50'],
    'No claim of full German coverage: Saar hard coal, Brandenburg and other state lignite fields still require spatial acquisition. Vertically overlapping Saxon seam layers are not separate abundance increments.',
    evidence='NRW hard-coal resource areas extend to 2000 m in actual source layer; also NRW lignite and Saxon coal seams/fields.')
region('france_saar', 'Nord–Pas-de-Calais, Lorraine and Saar', ['France'], 'historically verified; polygon gap remains', [],
    'No reviewed coal-body GIS imported. Do not construct a bounding rectangle from narrative dimensions or fill the entire Saar–Lorraine sedimentary basin.',
    ['https://www.brgm.fr/fr/reference-projet-acheve/nord-pas-calais-surveillance-travaux-urgence-deux-anciens-puits-charbon','https://sigesrm.brgm.fr/Reservoir-minier-du-bassin-houiller-lorrain','https://dpsm.brgm.fr/actualite/liens-franco-allemands-lapres-mine-en-lorraine-en-sarre'],
    'BRGM documents historical coal mining in Nord–Pas-de-Calais and Lorraine, including Lorraine–Saar continuity. Closure is not geological absence.')
region('czech', 'Czech hard coal and lignite districts', ['Czech Republic'], 'primary national review found; polygon gap remains', [],
    'No Czech national polygons imported; Saxon data do not establish national Czech coverage. Retain exhausted districts in a future acquisition.',
    ['https://cgs.gov.cz/sites/geology/files/extranet-eng/publications/online/mineral-commodity-summaries/MINERAL-COMMODITY-SUMMARIES-OF-THE-CZECH-REPUBLIC-2011.pdf'],
    'Czech Geological Survey commodity summary distinguishes registered, other and exhausted coal/lignite resources.')
region('australia', 'Australia, including Queensland and western/southern basins', ['Australia'], 'national basin context and sites; partial true coalfield polygons', ['ga_basins_2013','nsw_coalfields','victoria_coalfields','ga_aimr_2025'],
    'Only NSW and Victoria have coalfield polygons here. Queensland, Western Australia and other states have broad basin context plus resource sites, not a demonstrated continuous coal area.',
    evidence='51 coalfield polygons, 29 full-basin context polygons and 421 resource points. NSW has Western, Southern, Newcastle, Hunter and Gunnedah fields; Victoria includes historical fields.')
region('india', 'India', ['India'], 'legacy coal-bearing-area polygons acquired', ['usgs_india_2011'],
    'Coal rank absent in feature attributes; no inferred bituminous/lignite assignment. Some mapped contacts interpreted beneath alluvium; local depth and early access unknown.',
    evidence='305 coal-bearing formation polygons from USGS compilation of GSI and cited sources; not restricted to modern production.')
region('bangladesh', 'Bangladesh', ['Bangladesh'], 'legacy coal-bearing-area polygons acquired', ['usgs_india_2011'],
    'Eight mapped areas do not establish national completeness or shallow access; source coal rank absent.')
region('china', 'China', ['China'], 'historical atlas coal-area polygons acquired', ['usgs_china_2014'],
    '906 areas represent the source atlas coal-mining areas as of 2001, not all coal-bearing formations. Some datum conversions are approximate. Older 2000 coalfield shapefile withheld because its CRS is not declared.',
    evidence='550 bituminous, 249 anthracite and 107 lignite polygons with source rank. No present-operation filter.')
region('indonesia', 'Indonesia: Sumatra and Kalimantan', ['Indonesia'], 'major verified gap; source map acquired without reusable polygons', [],
    '2021 government coal-distribution PDF retained; no georeferenced coalfield vectors normalized. Do not replace coal formations with island/basin-wide eligibility.',
    ['https://geologi.esdm.go.id/media-center/indonesia-s-mineral-and-coal-resources-and-reserves-2025','https://geologi.esdm.go.id/storage/publikasi/gIY50hqnqkYEtdjToQsEpJrOVBVnNDZa8k0rOUXz.pdf'],
    'Geological Agency 2025 inventory explicitly identifies Kalimantan and Sumatra as major coal regions and reports coal by basin, calorific value and quality.')
region('south_africa', 'South Africa coalfields', ['South Africa'], 'major verified gap; national survey map located', [],
    'Existing point records do not represent coalfield coverage. CGS provides a coalfield map, but no reusable georeferenced coalfield polygons imported in this pass.',
    ['https://www.geoscience.org.za/cgs/systems/publications/downloadable-material/','https://researchspace.csir.co.za/server/api/core/bitstreams/2f13ac0c-d434-4fe7-ade2-cbb62baf495a/content'],
    'CGS national coalfield map and CSIR coalfield geology chapter include economic and subeconomic coal districts; no current producer filter is appropriate.')
region('southern_africa', 'Botswana, Zimbabwe and Zambia: Kalahari and Zambezi coal-bearing successions', ['Botswana','Zimbabwe','Zambia'], 'primary geological evidence; coal-body polygons unresolved', [],
    'USGS coalbed-gas assessment provinces are not coal-body boundaries and have not been imported as coalfields.',
    ['https://www.usgs.gov/publications/assessment-coalbed-gas-resources-kalahari-basin-province-botswana-zimbabwe-and-zambia','https://pubs.usgs.gov/fs/2017/3003/fs20173003.pdf'],
    'USGS assessment documents Permian coal-bearing successions in the regional geological setting; remaining geometry gap is explicit.')
region('usa', 'United States', ['United States'], 'overlapping generalized national compilations acquired', ['usgs_americas_2008','usgs_us_2012'],
    '602 updated conterminous coalfields overlap 496 Americas polygons. Do not add feature density; deep NCRA thickness/overburden layers remain raw rather than generalized into shallow availability.',
    evidence='Source counts can exceed other countries because of segmentation, source overlap and surveying history, not inherently greater coal abundance.')
region('canada', 'Canada', ['Canada'], 'generalized coal-bearing-area polygons acquired', ['usgs_americas_2008'],
    '141 small-scale polygons; source uses surficial geology and coal references, so concealed coal is underrepresented. Not a reserve or seam-volume map.')
region('mexico', 'Mexico', ['Mexico'], 'generalized coal-bearing-area polygons acquired', ['usgs_americas_2008'],
    'Thirteen country-labelled polygons; local seam, quality, depth and historical access require regional evidence.')
region('colombia', 'Colombia and border coal areas', ['Colombia','Colombia Venezuela'], 'generalized coal-bearing-area polygons acquired', ['usgs_americas_2008'],
    '24 Colombia and three cross-border-labelled polygons; not a national deposit census. Preserve cross-border labels and source rank/age.')
region('brazil', 'Brazil and border coal areas', ['Brazil','Uraguay Brazil','Brazil French Guiana'], 'generalized coal-bearing-area polygons acquired', ['usgs_americas_2008'],
    'Six Brazil and two cross-border-labelled polygons; original misspelt source labels retained. No concealed-coal completeness claim.')
region('southern_americas', 'Chile, Argentina, Peru and neighbouring coal areas', ['Chile','Argentina','Peru','Argentina Chile','Ecuador Peru','Peru Ecuador'], 'generalized coal-bearing-area polygons acquired', ['usgs_americas_2008'],
    'Some cross-border labels represent single geometry records. Generalized surface geology does not capture all buried coal, low-grade resources or historical small workings.')
region('central_america', 'Central America and Caribbean', ['El Salvador','Guatemala','Costa Rica','Costa Rica Panama','Panama','Nicaragua','Honduras','Cuba','Haiti','Jamaica','Dominican Republic'], 'mixed generalized polygons and literature points acquired', ['usgs_americas_2008'],
    '99 supplementary point records across the source study region remain points. No buffers invented where source could not delineate coal-bearing area.')
region('former_soviet', 'Former Soviet Union', ['Russia','Kazakhstan','Ukraine','Russian Federation'], 'existing regional point layer only; polygons unresolved', [],
    'Earlier package contains 446 source points across the former Soviet region; country attribution is not joined here. No coalfield polygon completeness claim or country-specific point count inferred.',
    ['https://pubs.usgs.gov/of/2001/ofr-01-104/'],
    'Legacy USGS regional coal data exist separately at ../../normalized/fsu-coal-points.geojson; this correction does not alter them.')
region('global_unassessed', 'All other regions', [], 'unassessed or spatially incomplete', [],
    'No global completeness claim: additional European states, Mongolia, Pakistan, Turkey, Southeast Asia, Africa beyond the reviewed sources, New Zealand and other regions require their own primary evidence checks. This row does not assert occurrence in every listed location.',
    [], 'Not importing a source is not evidence that a region lacks coal.')

audit = dict(snapshot_date=DATE, scope='Coal evidence coverage correction; global review is incomplete',
    counts_are='Source features, not unique deposits; overlaps, segmentation and differing map purposes prohibit abundance inference.',
    baseline_mrds_by_country=dict(baseline_mrds), baseline_specialist_by_country=dict(baseline_specialist),
    correction_polygons_by_country_label=dict(country_polygons), correction_polygons_by_source=dict(source_polygons),
    unique_deposit_count=None, audit_geometry_policy='No coordinates or buffers generated for narrative unresolved records.', regions=rows)
write('known-region-audit.json', audit)
with (ROOT / 'known-region-audit.csv').open('w', newline='', encoding='utf-8-sig') as handle:
    writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
    writer.writeheader()
    for row in rows:
        writer.writerow({key: json.dumps(value, ensure_ascii=False) if isinstance(value,list) else value for key,value in row.items()})
write('validation.json', dict(snapshot_date=DATE, errors=errors, passed=not errors,
    files=file_checks, invalid_geometries=invalid, invalid_geometry_policy='Preserved and flagged; consumer must withhold from map/spawn geometry until reviewed.',
    coordinate_operations=dict(transforms), coordinate_precision_note='Ballpark operations are explicitly retained; no survey-level precision claimed.',
    archive_crc_checks=archive_checks, acquisition_hash_checks=hash_checks,
    response_content_warnings=response_checks, known_region_rows=len(rows),
    api_count_checks={'PGI_current':315,'PGI_archive':28}, unknown_grade_and_game_generation_checks_passed=True))
print(json.dumps(dict(passed=not errors, errors=errors, invalid_geometry_records=len(invalid), audited_regions=len(rows),
    checked_downloads=len(hash_checks), checked_archives=len(archive_checks), response_warnings=response_checks), indent=2))
if errors:
    raise SystemExit(1)
