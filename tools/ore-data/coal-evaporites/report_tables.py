"""Validation tables for docs/research/ore-deposits-2026-11-08/coal-evaporites.md (coal and evaporites group, 0065).

Reads validation.json and the catalogue (run build_catalogue.py first) and writes
docs/research/ore-deposits-2026-11-08/coal-evaporites/validation-tables.md and entries.csv (every entry with its age,
codes, 0060 verdict and location checks).

Published totals below are facts cited for comparison only. Coal: BGR Energy Study 2023 (data status 2022; reserves
plus resources, hard coal plus lignite, Gt), national surveys where BGR was not reachable or is narrower; BGR's site
refused automated download on 8 Nov 2026, so the BGR values are the published headline figures as recalled and are
approximate (about +-20 %); they are marked q C. Other layers: USGS Mineral Commodity Summaries 2025 reserves, which are
economic reserves and so are expected to be far smaller than the catalogue's geological resources.

Run: python -I tools/ore-data/coal-evaporites/report_tables.py
"""
from pathlib import Path
import csv, json

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / 'docs/research/ore-deposits-2026-11-08/coal-evaporites'

# (published Gt, basis, source)
COAL_PUBLISHED = {
    'United States of America': (8000, 'BGR: hard coal ~6,500 Gt resources + lignite ~1,400 Gt + reserves ~250 Gt', 'bgr-2023'),
    'China': (5800, 'BGR: hard coal ~5,300 Gt resources + lignite ~300 Gt + reserves ~140 Gt', 'bgr-2023'),
    'Russia': (3300, 'BGR: hard coal ~2,600 Gt resources + lignite ~540 Gt + reserves ~160 Gt', 'bgr-2023'),
    'Australia': (2000, 'BGR: hard coal ~1,500 Gt + lignite ~400 Gt resources + reserves ~150 Gt', 'bgr-2023'),
    'India': (407, 'Geological Survey of India inventory 1 April 2022: coal 361 Gt + lignite 46 Gt', 'gsi-2022'),
    'Canada': (310, 'BGR: hard coal ~180 Gt + lignite ~120 Gt resources + reserves ~6 Gt; Alberta alone ~620 Gt ultimate potential (AER ST98)', 'bgr-2023'),
    'Germany': (155, 'BGR: hard coal ~83 Gt resources + lignite ~36 Gt resources + ~35 Gt reserves', 'bgr-2023'),
    'Poland': (88, 'PGI 2022 balance resources: hard coal ~64 Gt + lignite ~23 Gt', 'pgi-2022'),
    'United Kingdom': (187, 'BGR: hard coal resources ~187 Gt (includes deep and offshore coal)', 'bgr-2023'),
    'Ukraine': (90, 'BGR: hard coal ~32 Gt reserves + ~50 Gt resources + lignite ~8 Gt', 'bgr-2023'),
    'Kazakhstan': (170, 'BGR: hard coal ~26 Gt reserves + ~123 Gt resources + lignite ~20 Gt', 'bgr-2023'),
    'South Africa': (215, 'BGR: hard coal ~10 Gt reserves + ~205 Gt resources', 'bgr-2023'),
    'Botswana': (212, 'BGR: hard coal resources ~212 Gt', 'bgr-2023'),
    'Pakistan': (186, 'BGR: lignite resources ~176 Gt (Thar 175 Gt) + reserves ~3 Gt + hard coal ~7 Gt', 'bgr-2023'),
    'Indonesia': (99, 'Indonesian MEMR 2021: coal resources ~99 Gt (reserves ~35 Gt are part of them)', 'memr-2021'),
    'Mongolia': (173, 'Mongolian government: geological resources ~173 Gt (BGR ~40 Gt hard coal + ~120 Gt lignite)', 'bgr-2023'),
    'Colombia': (15, 'BGR: hard coal ~4.5 Gt reserves + ~11 Gt resources', 'bgr-2023'),
    'Turkey': (21, 'MTA: lignite ~19 Gt + hard coal ~1.5 Gt (Zonguldak)', 'mta-2021'),
    'Czechia': (23, 'BGR: hard coal ~16 Gt + lignite ~7 Gt resources', 'bgr-2023'),
    'Brazil': (32, 'BGR: lignite and subbituminous ~6.6 Gt reserves + ~25 Gt resources (Rio Grande do Sul)', 'bgr-2023'),
}

# USGS MCS 2025 reserves (Mt; phosphate rock as rock, potash as K2O). Catalogue figures are geological resources in
# place (phosphorite rock; sylvite for potash), so the expected ratio is well above 1; the check is rank and presence.
MCS_PUBLISHED = {
    'P': {'Morocco': 50000, 'China': 3700, 'Egypt': 2800, 'Tunisia': 2500, 'Algeria': 2200, 'Brazil': 1600,
          'Saudi Arabia': 1400, 'Australia': 1100, 'Jordan': 1000, 'United States of America': 1000, 'Russia': 2400,
          'Kazakhstan': 260, 'Peru': 210, 'Israel': 60, 'Senegal': 50, 'Togo': 30, 'Vietnam': 30, 'Iraq': 0,
          'Mexico': 30, 'Estonia': 0},
    'K': {'Canada': 1100, 'Belarus': 750, 'Russia': 400, 'China': 180, 'Germany': 150, 'United States of America': 220,
          'Israel': 0, 'Laos': 75, 'Thailand': 0, 'Ethiopia': 0, 'Republic of the Congo': 0, 'Brazil': 2, 'Spain': 44,
          'Chile': 100, 'United Kingdom': 0, 'Turkmenistan': 0, 'Ukraine': 0, 'Argentina': 0, 'Morocco': 0, 'France': 0},
    'B': {'Turkey': 1100, 'United States of America': 40, 'Russia': 40, 'Chile': 35, 'Argentina': 0, 'China': 24,
          'Bolivia': 0, 'Peru': 4},
}


def fmt(x):
    return '{:,.0f}'.format(x) if x is not None else ''


def main():
    v = json.loads((HERE / 'validation.json').read_text(encoding='utf-8'))
    evid = [c for c in v['checks'] if c['kind'] == 'evidence']
    v['checks'] = [c for c in v['checks'] if c['kind'] != 'evidence']
    cat = json.loads((ROOT / 'assets/earth/config/ore-deposits-v1/coal-evaporites.json').read_text(encoding='utf-8'))
    OUT.mkdir(parents=True, exist_ok=True)
    md = []

    # ---- coal by country
    coal = v['mtByLayerCountry']['C_coal']
    md += ['### Coal by country against published totals', '',
           '| Country | Catalogue (Gt) | Published (Gt) | Ratio | Published basis |', '|---|---:|---:|---:|---|']
    names = sorted(set(coal) | set(COAL_PUBLISHED), key=lambda c: -(coal.get(c, 0)))
    for c in names:
        got = coal.get(c, 0) / 1000
        pub = COAL_PUBLISHED.get(c)
        ratio = '{:.2f}'.format(got / pub[0]) if pub else ''
        md.append('| %s | %s | %s | %s | %s |' % (c, fmt(got), fmt(pub[0]) if pub else '', ratio, pub[1] if pub else ''))
    tot = sum(coal.values()) / 1000
    md += ['', 'World total in the catalogue: %s Gt (BGR world coal resources plus reserves: about 23,000 Gt).' % fmt(tot), '']

    # ---- other layers by country
    for layer, title, unit in (('P', 'Phosphorite', 'Mt rock'), ('K', 'Potash', 'Mt sylvite (MCS: Mt K2O)'), ('B', 'Borates', 'Mt ore (MCS: Mt B2O3)')):
        got = v['mtByLayerCountry'].get(layer, {})
        pub = MCS_PUBLISHED[layer]
        md += ['### %s by country against USGS MCS 2025 reserves' % title, '',
               '| Country | Catalogue (%s) | MCS 2025 reserves (approx.; - not listed or not quantified) | In both |' % unit, '|---|---:|---:|---|']
        for c in sorted(set(got) | set(pub), key=lambda c: -(got.get(c, 0))):
            both = 'yes' if got.get(c) and pub.get(c) else ('catalogue only' if got.get(c) else 'MCS only')
            md.append('| %s | %s | %s | %s |' % (c, fmt(got.get(c)) if c in got else '', fmt(pub.get(c)) if pub.get(c) else '-', both))
        md.append('')
    for layer in ('S', 'gypsum', 'NaCl', 'alum'):
        got = v['mtByLayerCountry'].get(layer, {})
        if got:
            md += ['%s catalogue tonnage by country (Mt): ' % layer + ', '.join('%s %s' % (c, fmt(m)) for c, m in sorted(got.items(), key=lambda kv: -kv[1])), '']

    # ---- 20 largest entries
    ent = [c for c in v['checks'] if c.get('mt')]
    ent.sort(key=lambda c: -c['mt'])
    md += ['### The 20 largest entries', '', '| # | Entry | Layer | Mt | Area km2 | Country | Age | q |', '|---:|---|---|---:|---:|---|---|---|']
    for i, c in enumerate(ent[:20], 1):
        md.append('| %d | %s (`%s`) | %s | %s | %s | %s | %s | %s |' % (i, c['name'], c['id'], c['layer'], fmt(c['mt']), fmt(c.get('areaKm2')), c['country'], c['age'], c['q']))
    md.append('')

    # ---- location checks
    areas = [c for c in v['checks'] if c['kind'] == 'area']
    deps = [c for c in v['checks'] if c['kind'] == 'deposit']
    low_land = [c for c in areas if c['landShare'] < 0.5]
    md += ['### Locations', '',
           '- Areas: %d; in their country (largest overlap or any overlap): %d; land share under 0.5: %d (%s).' % (
               len(areas), sum(c['countryOk'] for c in areas), len(low_land), ', '.join('%s %.2f' % (c['id'], c['landShare']) for c in low_land) or 'none'),
           '- Deposits: %d; on land: %d; in their country: %d. Off land: %s.' % (
               len(deps), sum(bool(c['onLand']) for c in deps), sum(c['countryOk'] for c in deps),
               ', '.join(c['id'] for c in deps if not c['onLand']) or 'none'),
           '- Evidence-only entries: %d; in their country: %d.' % (len(evid), sum(c['countryOk'] for c in evid)),
           '- Country problems: %s.' % (', '.join('%s (catalogue %s, Natural Earth %s)' % (c['id'], c['country'], c.get('largestOverlap') or c.get('foundCountry')) for c in v['countryProblems']) or 'none'),
           '']

    # ---- ages against 0060
    from collections import Counter
    tab = Counter()
    for c in v['checks']:
        tab[(c['layer'], '/'.join(sorted(set(c['codes']))), c['age'].split('-')[0])] += 1
    md += ['### Ages against the 0060 layer rules', '', '| Layer | Codes | Age | Entries |', '|---|---|---|---:|']
    for (layer, codes, age), n in sorted(tab.items()):
        md.append('| %s | %s | %s | %d |' % (layer, codes, age, n))
    md += ['', 'Conflicts: ' + ('; '.join('%s (%s, %s): %s' % (c['id'], c['age'], '/'.join(c['codes']), c['ageConflict']) for c in v['ageConflicts']) or 'none') + '.', '']

    # ---- implied tonnage
    it = v['impliedTonnage']
    off = [r for r in it if not (0.8 <= r['ratio'] <= 1.25)]
    md += ['### Tonnage the beds imply against the published tonnage', '',
           '%d areas carry a published tonnage; the derived cover reproduces it within 0.8 to 1.25 for %d; outside: %s.' % (
               len(it), len(it) - len(off), ', '.join('%s %.2f' % (r['id'], r['ratio']) for r in off) or 'none'), '']

    (OUT / 'validation-tables.md').write_text('\n'.join(md) + '\n', encoding='utf-8')

    with open(OUT / 'entries.csv', 'w', newline='', encoding='utf-8') as f:
        w = csv.writer(f)
        w.writerow(['kind', 'id', 'name', 'layer', 'codes', 'age', 'ageRule0060', 'country', 'countryOk', 'landShareOrOnLand', 'areaKm2', 'mt', 'q', 'evidenceWeight'])
        for c in v['checks']:
            w.writerow([c['kind'], c['id'], c['name'], c['layer'], '/'.join(c['codes']), c['age'], c['ageConflict'] or 'ok', c['country'], c['countryOk'],
                        c.get('landShare', c.get('onLand')), c.get('areaKm2', ''), c.get('mt') or '', c['q'], c.get('evidenceWeight', '')])
    print('\n'.join(md))


if __name__ == '__main__':
    main()
