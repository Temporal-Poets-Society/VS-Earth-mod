"""Compact source-schema inspection; no data edits or downloads."""
from pathlib import Path
import json, re, html
P=Path(__file__).resolve().parent/'raw'
for n in ['spain-lithology1m-layers.json']:
 j=json.loads((P/n).read_text(encoding='utf-8-sig'))
 print(n,[(x['id'],x['name'],[f['name'] for f in x.get('fields',[])]) for x in j['layers']])
j=json.loads((P/'latvia-mines-native.geojson').read_text())
print('LV mine',[(f['id'],{k:v for k,v in f['properties'].items() if v is not None}) for f in j['features'] if '1539' in str(f)])
s=(P/'germany-lgrb-min4eu-catalog.html').read_text()
print('German data', [html.unescape(x) for x in re.findall(r'href="([^"]+)"',s) if 'lgrb' in x or 'wfs' in x.lower()])
print('German search', [html.unescape(x) for x in re.findall(r'href="([^"]+)"',(P/'germany-lgrb-portal.html').read_text()) if 'produktsuche' in x])
