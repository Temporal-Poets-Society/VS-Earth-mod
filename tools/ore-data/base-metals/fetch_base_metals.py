"""Download the base-metals group's public-domain inputs into rawdata/ore/base-metals (never into git). Re-runnable:
files already present are kept unless --force.

* USGS OFR 2008-1155 (Singer, Berger and Moring 2008), porphyry copper grade-tonnage database, tab-separated text and
  metadata: ore-zone areas and axes for the porphyry bodies.
* USGS Mineral Commodity Summaries 2024, one PDF per commodity of this group, and their extracted text (validation:
  world reserves, mine production and resources).

The other inputs are already on disk (docs/research/remaining-ores-2026-09-29/raw and
docs/research/copper-tin-global-2026-09-29/normalized, linked into worktrees by tools/ore-data/link-raw.ps1).
Run: C:\\VS mods\\Earth\\.venv\\Scripts\\python.exe -I tools/ore-data/base-metals/fetch_base_metals.py
"""
import sys, urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import RAW  # noqa: E402

UA = {'User-Agent': 'earth-vintage-story-mod data fetch (public-domain USGS data)'}
FILES = {
    'usgs-porcu-2008/PorCuTX2008.txt': 'https://pubs.usgs.gov/of/2008/1155/data/PorCuTX2008.txt',
    'usgs-porcu-2008/PorCu_meta.txt': 'https://pubs.usgs.gov/of/2008/1155/data/PorCu_meta.txt',
}
for c in ['copper', 'zinc', 'lead', 'silver', 'tin', 'tungsten', 'molybdenum', 'antimony', 'bismuth', 'mercury']:
    FILES[f'usgs-mcs-2024/mcs2024-{c}.pdf'] = f'https://pubs.usgs.gov/periodicals/mcs2024/mcs2024-{c}.pdf'


def main():
    force = '--force' in sys.argv
    for rel, url in FILES.items():
        dst = RAW / rel
        if dst.exists() and not force:
            print('have', rel); continue
        dst.parent.mkdir(parents=True, exist_ok=True)
        with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=120) as r:
            data = r.read()
        dst.write_bytes(data)
        print('got', rel, len(data))
    try:
        import pypdf
    except ImportError:
        print('pypdf not installed: skip text extraction'); return
    for pdf in sorted((RAW / 'usgs-mcs-2024').glob('*.pdf')):
        txt = pdf.with_suffix('.txt')
        if txt.exists() and not force: continue
        txt.write_text('\n'.join(p.extract_text() for p in pypdf.PdfReader(pdf).pages), encoding='utf-8')
        print('text', txt.name)


if __name__ == '__main__':
    main()
