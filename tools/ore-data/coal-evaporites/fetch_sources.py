"""Fetch and check the inputs of the coal and evaporites group (decision 0065). Re-runnable.

1. Downloads the public-domain archives the build reads (or whose normalised form it reads) into
   C:\\VS mods\\Earth\\rawdata\\ore\\coal-evaporites\\ (never into git), skipping files already there, and records
   their SHA-256 in rawdata/ore/coal-evaporites/fetch-manifest.json.
2. Checks that the normalised on-disk layers the build uses exist (they were made by the 29 September research
   packages; their own acquire/normalise scripts are named below) and records their SHA-256.

Run: C:\\VS mods\\Earth\\.venv\\Scripts\\python.exe -I tools/ore-data/coal-evaporites/fetch_sources.py [--check-only]
"""
from pathlib import Path
import hashlib, json, sys, time, urllib.request

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
RAW = Path(r'C:\VS mods\Earth\rawdata\ore\coal-evaporites')
UA = 'Earth-mod-ore-data/1.0 (Vintage Story mod research; contact via project repository)'

DOWNLOADS = [
    # (file name, url, what it is, licence)
    ('PotashGIS.zip', 'https://pubs.usgs.gov/sir/2010/5090/s/PotashGIS.zip',
     'USGS SIR 2010-5090-S global potash tracts and deposits (GIS)', 'public domain (USGS)'),
    ('phosphate-csv.zip', 'https://mrdata.usgs.gov/phosphate/phosphate-csv.zip',
     'USGS world phosphate deposits (CSV)', 'public domain (USGS)'),
    ('ne_10m_admin_0_countries.zip', 'https://naciscdn.org/naturalearth/10m/cultural/ne_10m_admin_0_countries.zip',
     'Natural Earth 1:10m admin-0 countries (land and country check)', 'public domain (Natural Earth)'),
    ('fsu-coal_dep.csv', 'https://pubs.usgs.gov/of/2001/ofr-01-104/fsucoal/export/ascii/coal_dep.csv',
     'USGS OFR 01-104 coal deposits of the former Soviet Union (cross-check of the authored FSU basin outlines)', 'public domain (USGS)'),
]

ON_DISK = [
    # (path relative to the Earth root, made by)
    ('docs/research/remaining-ores-2026-09-29/corrections/coal/normalized/coal-polygons.geojson', 'corrections/coal/acquire.py + normalize.py'),
    ('docs/research/remaining-ores-2026-09-29/corrections/coal/normalized/coal-context-polygons.geojson', 'corrections/coal/acquire.py + normalize.py'),
    ('docs/research/remaining-ores-2026-09-29/normalized/PotashTracts.geojson', 'remaining-ores acquire_supplements.py + normalize_supplements.py'),
    ('docs/research/remaining-ores-2026-09-29/normalized/P-specialist.geojson', 'remaining-ores build_mrds.py / normalize_supplements.py'),
    ('docs/research/remaining-ores-2026-09-29/normalized/S-occurrences.csv.gz', 'remaining-ores build_mrds.py'),
    ('docs/research/remaining-ores-2026-09-29/raw/naturalearth/ne_10m_admin_0_countries.zip', 'remaining-ores acquisition 2026-10-02'),
]


def sha(p):
    h = hashlib.sha256()
    with open(p, 'rb') as f:
        for b in iter(lambda: f.read(1 << 20), b''): h.update(b)
    return h.hexdigest()


def main():
    check_only = '--check-only' in sys.argv
    RAW.mkdir(parents=True, exist_ok=True)
    man = {'retrieved': time.strftime('%Y-%m-%d'), 'downloads': [], 'onDisk': []}
    for name, url, what, lic in DOWNLOADS:
        dst = RAW / name
        if not dst.exists() and not check_only:
            print('fetch', url, flush=True)
            req = urllib.request.Request(url, headers={'User-Agent': UA})
            with urllib.request.urlopen(req, timeout=120) as r, open(str(dst) + '.part', 'wb') as f:
                while True:
                    b = r.read(1 << 20)
                    if not b: break
                    f.write(b)
            Path(str(dst) + '.part').replace(dst)
        man['downloads'].append({'file': name, 'url': url, 'what': what, 'licence': lic,
                                 'bytes': dst.stat().st_size if dst.exists() else None, 'sha256': sha(dst) if dst.exists() else None})
    for rel, made in ON_DISK:
        p = ROOT / rel
        man['onDisk'].append({'file': rel, 'madeBy': made, 'present': p.exists(), 'sha256': sha(p) if p.exists() else None})
        if not p.exists(): print('MISSING', rel, '(run', made + '; in a worktree run tools/ore-data/link-raw.ps1 first)')
    (RAW / 'fetch-manifest.json').write_text(json.dumps(man, indent=1) + '\n', encoding='utf-8')
    print(json.dumps({'downloads': [(d['file'], d['bytes']) for d in man['downloads']],
                      'missingOnDisk': [d['file'] for d in man['onDisk'] if not d['present']]}))


if __name__ == '__main__':
    main()
