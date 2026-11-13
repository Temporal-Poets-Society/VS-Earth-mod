"""Download the iron-ferro validation sources into C:\\VS mods\\Earth\\rawdata\\ore\\iron-ferro\\ (never into git).

* USGS Mineral Commodity Summaries 2025 chapters (public domain) -> mcs2025/*.pdf, plus extracted text (*.txt, needs pypdf)
  used by validate_catalogue.py (the reserve figures are typed into its MCS table with footnote markers removed).
* The USGS Ni-Co laterite and podiform chromite compilations are already on disk from the 29 September 2026 package
  (docs/research/remaining-ores-2026-09-29/raw/, fetched by acquire_supplements.py there); this script only checks them.

Run:  C:\\VS mods\\Earth\\.venv\\Scripts\\python.exe -I tools/ore-data/iron-ferro/fetch_sources.py
"""
from pathlib import Path
import hashlib, json, urllib.request

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
RAW = Path(r'C:\VS mods\Earth\rawdata\ore\iron-ferro')
MCS = ['iron-ore', 'manganese', 'chromium', 'nickel', 'titanium-minerals', 'vanadium', 'platinum-group', 'bauxite-alumina']
URL = 'https://pubs.usgs.gov/periodicals/mcs2025/mcs2025-{}.pdf'
CHECK = ['docs/research/remaining-ores-2026-09-29/raw/laterite-laterite-csv.zip', 'docs/research/remaining-ores-2026-09-29/raw/podchrome-podchrome-csv.zip',
         'docs/research/remaining-ores-2026-09-29/raw/naturalearth/ne_10m_admin_0_countries.zip']


def main():
    out = RAW / 'mcs2025'; out.mkdir(parents=True, exist_ok=True)
    manifest = []
    for name in MCS:
        dest = out / ('mcs2025-%s.pdf' % name)
        if not dest.exists():
            req = urllib.request.Request(URL.format(name), headers={'User-Agent': 'Earth-mod-data-fetch/1.0'})
            with urllib.request.urlopen(req, timeout=60) as r: dest.write_bytes(r.read())
        manifest.append({'file': str(dest), 'url': URL.format(name), 'sha256': hashlib.sha256(dest.read_bytes()).hexdigest()})
        try:
            import pypdf
            text = '\n'.join(p.extract_text() for p in pypdf.PdfReader(dest).pages)
            dest.with_suffix('.txt').write_text(text, encoding='utf-8')
        except ImportError:
            pass
    for rel in CHECK:
        p = ROOT / rel
        manifest.append({'file': rel, 'present': p.exists(), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest() if p.exists() else None})
    (RAW / 'fetch-manifest.json').write_text(json.dumps(manifest, indent=1), encoding='utf-8')
    print(json.dumps(manifest, indent=1))


if __name__ == '__main__':
    main()
