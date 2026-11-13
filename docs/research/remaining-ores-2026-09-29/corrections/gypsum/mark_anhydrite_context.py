"""One-off schema-preserving correction; normalize_gypsum.py now applies it too."""
from pathlib import Path
import gzip,json,os
P=Path(__file__).resolve().parent/'normalized'
for name in ['gypsum-permissive-formations-display-only.geojson','gypsum-permissive-formations.geojson.gz']:
 p=P/name;raw=p.read_bytes();compressed=name.endswith('.gz')
 if compressed:raw=gzip.decompress(raw)
 j=json.loads(raw);changed=0
 for f in j['features']:
  a=f['properties']
  if a['mineral_identity']=='anhydrite' and not a.get('gypsum_spawn_evidence_withheld'):
   a['gypsum_spawn_evidence_withheld']=True;changed+=1
 if changed:
  tmp=p.with_name(p.name+'.tmp')
  with (gzip.open(tmp,'wt',encoding='utf-8',compresslevel=1) if compressed else tmp.open('w',encoding='utf-8')) as out:
   json.dump(j,out,ensure_ascii=False,allow_nan=False,separators=(',',':'))
  os.replace(tmp,p)
 print(name,'corrected flags',changed,flush=True)
