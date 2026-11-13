"""Normalize USGS 'Mineral operations outside the United States' (minfac) mines for the ore heat maps.

Source: https://mrdata.usgs.gov/mineral-operations/ (minfac-csv.zip, metadata minfac.xml; Access_Constraints
none, Use_Constraints none; USGS public domain). Compiled from USGS Minerals Yearbook country chapters and the
Open-File Reports 2006-1135 (Africa and the Middle East), 2006-1375 (Latin America and Canada), 2010-1254 (Asia and
the Pacific), 2010-1255 (northern and central Eurasia) and 2010-1257 (Europe). Downloaded 2026-10-02 into raw/.

Only extraction sites are kept (fac_type containing Mine, Quarry, Field (not oil/gas), Salt Pond, Well with a
mineral commodity). Plants, mills, refineries and smelters are dropped because they process imported feed.
Commodity text is mapped to runtime layer codes with explicit word patterns; unmapped commodities (cement,
limestone, petroleum, barite, ...) are counted and dropped.
"""
from pathlib import Path
from collections import Counter
from zipfile import ZipFile
import csv, io, json, re

ROOT = Path(__file__).resolve().parents[2]
RAW = ROOT / 'raw'
OUT = ROOT / 'corrections/minfac/normalized'
OUT.mkdir(parents=True, exist_ok=True)

PATTERNS = [
    ('C_coal', r'\bcoal\b|\blignite\b|\banthracite\b'), ('Au', r'\bgold\b'), ('Ag', r'\bsilver\b'), ('Cu', r'\bcopper\b'),
    ('Pb', r'\blead\b'), ('Zn', r'\bzinc\b'), ('Sn', r'\btin\b'), ('W', r'\btungsten\b|\bwolfram'), ('Mo', r'\bmolybdenum\b'),
    ('Ni', r'\bnickel\b'), ('Cr', r'\bchromite\b|\bchrome\b|\bchromium\b'), ('Mn', r'\bmanganese\b'),
    ('Fe', r'\biron ore\b|\biron - ore\b|iron and steel: iron ore|iron and steel: iron, concentrate|^iron$|\btitanium, zirconium, iron\b'),
    ('Al', r'\bbauxite\b|^aluminum$'), ('Ti', r'\btitanium\b|\bilmenite\b|\brutile\b|\bmineral sands\b'), ('V', r'\bvanadium\b'),
    ('Pt', r'\bplatinum\b|\bpgm\b|\bpalladium\b'), ('U', r'\buranium\b'), ('Hg', r'\bmercury\b'), ('Sb', r'\bantimony\b'),
    ('Bi', r'\bbismuth\b'), ('B', r'\bboron\b|\bborax\b|\bborate'), ('P', r'\bphosphate\b|\bapatite\b'),
    ('K', r'\bpotash\b|\bsylvinite\b|\bcarnallite\b'), ('NaCl', r'\bsalt\b|\bhalite\b'), ('S', r'\bsulfur\b|\bsulphur\b'),
    ('C_graphite', r'\bgraphite\b'), ('F', r'\bfluorspar\b|\bfluorite\b|\bfluorine\b'), ('gypsum', r'\bgypsum\b'),
    ('diamond', r'\bdiamonds?\b'), ('emerald', r'\bemerald'), ('corundum', r'\bsapphire|\bruby\b|\bcorundum\b'),
    ('garnet', r'\bgarnet\b'), ('olivine', r'\bolivine\b'), ('clay', r'\bclays?\b|\bkaolin\b|\bbentonite\b'),
    ('peat', r'\bpeat\b'), ('beryl', r'\baquamarine\b|\bberyl\b'), ('tourmaline', r'\btourmaline\b'),
    ('SiO2', r'^quartz$|\bquartz and quartzite\b'), ('alum', r'\balunite\b'), ('N', r'\bnitrates?\b'),
]
PATTERNS = [(c, re.compile(p, re.I)) for c, p in PATTERNS]
EXTRACTION = re.compile(r'\bmine\b|\bquarry\b|\bfield\b|\bsalt pond\b|\bwell\b', re.I)
NOT_EXTRACTION = re.compile(r'\boil\b|\bgas\b', re.I)


def main():
    with ZipFile(RAW / 'usgs-minfac-csv.zip') as z:
        text = z.read('minfac.csv').decode('cp1252')
    rows = list(csv.DictReader(io.StringIO(text)))
    feats = []; dropped = Counter(); unmapped = Counter(); kept = Counter()
    for r in rows:
        ft = r['fac_type'] or ''
        if not EXTRACTION.search(ft) or NOT_EXTRACTION.search(ft):
            dropped['not_extraction_site:' + (ft or 'blank')] += 1; continue
        com = (r['commodity'] or '').strip()
        if re.search(r'pyrite', com, re.I) and not re.search(r'sulfur', com, re.I):
            dropped['pyrite_only'] += 1; continue
        codes = sorted({c for c, p in PATTERNS if p.search(com)})
        if not codes:
            unmapped[com.casefold()] += 1; continue
        try: lat, lon = float(r['latitude']), float(r['longitude'])
        except ValueError:
            dropped['no_coordinates'] += 1; continue
        for c in codes: kept[c] += 1
        feats.append({'type': 'Feature', 'id': 'minfac:' + r['rec_id'], 'geometry': {'type': 'Point', 'coordinates': [lon, lat]},
                      'properties': {'record_id': 'minfac:' + r['rec_id'], 'source_id': 'usgs_minfac_2010', 'source_feature_id': r['rec_id'],
                                     'name': r['fac_name'] or None, 'country': r['country'] or None, 'commodities': codes,
                                     'source_commodity': com, 'facility_type': ft, 'source_status': r['status'] or None,
                                     'yearbook_year': r['year'] or None, 'location': r['location'] or None,
                                     'location_precision_code': r['precision'] or None,
                                     'source_url': 'https://mrdata.usgs.gov/mineral-operations/', 'license_note': 'USGS public domain; Use_Constraints none',
                                     'spawn_probability': None, 'early_accessibility': None}})
    (OUT / 'usgs-minfac-mines.geojson').write_text(json.dumps({'type': 'FeatureCollection', 'features': feats}, ensure_ascii=False,
                                                              separators=(',', ':')), encoding='utf-8')
    summary = {'source_rows': len(rows), 'exported_sites': len(feats), 'layer_links': dict(kept),
               'dropped': dict(dropped.most_common()), 'unmapped_commodities_top': dict(unmapped.most_common(40))}
    (OUT.parent / 'normalization-summary.json').write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps({k: summary[k] for k in ['source_rows', 'exported_sites', 'layer_links']}))


if __name__ == '__main__':
    main()
