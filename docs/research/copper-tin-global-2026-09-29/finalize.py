"""Build inventory CSV and an authoritative acquisition audit without network calls."""
from pathlib import Path
import json,csv,hashlib,datetime
ROOT=Path(__file__).resolve().parent
raw=ROOT/'raw'
bad=raw/'gsc-tin-tungsten-report.pdf'
if bad.exists() and not bad.read_bytes().startswith(b'%PDF'):
    bad.rename(raw/'gsc-tin-tungsten-report-response.html')
inventory=json.loads((ROOT/'source-inventory.json').read_text(encoding='utf-8'))
fields=list(dict.fromkeys(k for r in inventory for k in r))
with (ROOT/'source-inventory.csv').open('w',encoding='utf-8',newline='') as f:
    w=csv.DictWriter(f,fieldnames=fields);w.writeheader();w.writerows(inventory)
attempts=json.loads((ROOT/'download-manifest.json').read_text(encoding='utf-8'))
attempts+=json.loads((ROOT/'powershell-download-manifest.json').read_text(encoding='utf-8-sig'))
latest={r['url']:r for r in attempts}
for r in latest.values():
    if r.get('file','').endswith('gsc-tin-tungsten-report.pdf'):
        r.update(file='raw/gsc-tin-tungsten-report-response.html',status='not_acquired_expected_pdf',
            validation='Server returned HTML archive notice, not the report or dataset.')
    p=ROOT/r['file']
    if p.exists():
        b=p.read_bytes();r.update(bytes=len(b),sha256=hashlib.sha256(b).hexdigest())
        if p.suffix=='.zip': assert b.startswith(b'PK'),p
        if p.suffix=='.pdf': assert b.startswith(b'%PDF'),p
json.dump({'note':'Authoritative latest attempt per URL; initial download-manifest.json records the earlier failed Python attempt.',
           'downloads':list(latest.values())},(ROOT/'acquisition-manifest.json').open('w',encoding='utf-8'),indent=2)
overlay=json.loads((ROOT/'historical-overlay.json').read_text(encoding='utf-8'))
assert all(r['geometry'] is None for r in overlay['records'])
with (ROOT/'historical-overlay.csv').open('w',encoding='utf-8',newline='') as f:
    rows=[{**r,'commodities':';'.join(r['commodities'])} for r in overlay['records']]
    keys=list(dict.fromkeys(k for r in rows for k in r));w=csv.DictWriter(f,fieldnames=keys);w.writeheader();w.writerows(rows)
print('Source inventory:',len(inventory),'Historical evidence entries:',len(overlay['records']))
