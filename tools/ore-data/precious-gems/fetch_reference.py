"""Download the public-domain reference figures used to validate the precious-gems catalogue and keep the parsed tables.

  python -I tools/ore-data/precious-gems/fetch_reference.py

Downloads (into C:\\VS mods\\Earth\\rawdata\\ore\\precious-gems\\, never into git) the USGS Mineral Commodity Summaries
2026 chapters for gold and gemstones, extracts their world mine production and reserves tables, and writes
tools/ore-data/precious-gems/reference-mcs2026.json (country rows, the source URL and the PDF's sha256). USGS
publications are public domain. Needs pypdf in the project venv (pip install pypdf).
"""
from pathlib import Path
import hashlib, json, re, urllib.request

from pypdf import PdfReader

HERE = Path(__file__).resolve().parent
RAW = Path(r'C:\VS mods\Earth\rawdata\ore\precious-gems')
CHAPTERS = {
    'gold': ('https://pubs.usgs.gov/periodicals/mcs2026/mcs2026-gold.pdf', 'metric tons of gold content',
             'World Mine Production and Reserves', 'World Resources'),
    'gemDiamond': ('https://pubs.usgs.gov/periodicals/mcs2026/mcs2026-gemstones.pdf', 'thousand carats of gem-quality natural diamond',
                   'World Gem-Quality Natural Diamond Mine Production and Reserves', 'World Resources'),
}
ROW = re.compile(r'^([A-Z][A-Za-z ()\'.,-]+?)\s+(?:e)?([\d,\u2014\u2013-]+|\S)\s+([\d,\u2014\u2013-]+|\S)\s+([>\d,]+|NA)\s*$')
COMMA_NUMBER = re.compile(r'^>?\d{1,3}(,\d{3})*$')


def reserves(tok):
    # A footnote number printed against the figure ("1113,000" = footnote 11 + 13,000): strip it when the token is not a
    # well-formed number but its tail is.
    if COMMA_NUMBER.match(tok) or tok == 'NA': return tok
    for k in (1, 2):
        if COMMA_NUMBER.match(tok[k:]): return tok[k:]
    return tok


def number(s):
    s = s.replace(',', '').lstrip('>e')
    return float(s) if re.fullmatch(r'\d+(\.\d+)?', s) else None


def main():
    RAW.mkdir(parents=True, exist_ok=True)
    out = {'retrieved': '2026-11-08', 'licence': 'public domain (USGS)', 'tables': {}}
    for key, (url, unit, start, end) in CHAPTERS.items():
        path = RAW / url.rsplit('/', 1)[1]
        if not path.exists():
            req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0 (Earth mod ore data build)'})
            path.write_bytes(urllib.request.urlopen(req, timeout=60).read())
        text = ''.join(p.extract_text() for p in PdfReader(str(path)).pages)
        block = text[text.find(start):text.find(end, text.find(start))]
        rows = []
        notes = {c: n for n, c in re.findall(r'(\d{1,2})For ([A-Z][a-z]+)', text)}  # "11For Australia, ..." footnotes
        for line in block.splitlines():
            line = line.strip()
            m = ROW.match(line)
            if not m or m.group(1).startswith(('Mine production', 'World Mine', 'World Gem')): continue
            name = m.group(1).strip(); tok = m.group(4)
            if name in notes and tok.startswith(notes[name]) and COMMA_NUMBER.match(tok[len(notes[name]):]):
                tok = tok[len(notes[name]):]
            rows.append({'country': name, 'production2024': number(m.group(2)), 'production2025e': number(m.group(3)),
                         'reserves': number(reserves(tok)), 'line': line})
        out['tables'][key] = {'url': url, 'unit': unit, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'rows': rows}
    (HERE / 'reference-mcs2026.json').write_text(json.dumps(out, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
    for k, t in out['tables'].items():
        print(k, len(t['rows']), [(r['country'], r['production2025e'], r['reserves']) for r in t['rows']])


if __name__ == '__main__':
    main()
