"""Check the atlas integration contract without altering evidence geometry."""
from pathlib import Path
from collections import Counter
import json,math
ROOT=Path(__file__).resolve().parent
reviewed=json.loads((ROOT/'normalized/gypsum-reviewed.geojson').read_text(encoding='utf-8'))['features']
display=json.loads((ROOT/'normalized/gypsum-permissive-formations-display-only.geojson').read_text(encoding='utf-8'))['features']
checks=0
def check(ok,message):
 global checks
 assert ok,message
 checks+=1
def coords(c):
 if isinstance(c[0],(int,float)):yield c
 else:
  for child in c:yield from coords(child)
for name,features,expected in [('reviewed',reviewed,525),('display',display,28753)]:
 check(len(features)==expected,name+' count')
 check(len({f['id'] for f in features})==len(features),name+' source IDs are unique')
 for f in features:
  p=f['properties']
  check(p['absence_evidence'] is False,f['id']+' is positive-only evidence')
  check(p['spawn_probability'] is None,f['id']+' has no invented spawn probability')
  check(p['depth_min_m'] is None and p['depth_max_m'] is None,f['id']+' retains unmeasured depth')
  check(all(math.isfinite(x) and math.isfinite(y) and -180<=x<=180 and -90<=y<=90 for x,y,*z in coords(f['geometry']['coordinates'])),f['id']+' geographic coordinates')
  if name=='display':check(p['display_only'] and p['not_for_generation'] and p['display_simplification_degrees']==.005,f['id']+' display classification')
latvia=[f for f in reviewed if f['properties']['source_id']=='latvia_cabinet321_2012']
for f in latvia:
 xy=list(coords(f['geometry']['coordinates']))
 check(all(23<x<26 and 55<y<58 for x,y in xy),f['id']+' LKS92 axis conversion')
 check(len(xy)==(17 if f['properties']['name']=='Salaspils' else 30),f['id']+' boundary vertex count and closure')
check(any(f['properties']['economic_status']=='noncommercial_as_reported' for f in reviewed),'Noncommercial evidence retained')
check(any(f['properties']['source_id']=='pgi_midas_historical' for f in reviewed),'Removed-from-balance deposit retained')
check(any(f['properties'].get('current_operating_status')=='Abandonada' for f in reviewed),'Abandoned Spanish occurrence retained')
check(all(f['properties'].get('gypsum_spawn_evidence_withheld') for f in display if f['properties']['mineral_identity'] in ['anhydrite','gypsum_and_anhydrite_often_dissolved']),'Anhydrite and dissolved evidence cannot masquerade as confirmed gypsum')
check(all(f['properties']['mineral_identity']=='gypsum_or_anhydrite_undifferentiated' for f in reviewed if f['properties']['country']=='Poland'),'Polish combined commodity identity retained')
check(not any('min4eu' in f['properties']['source_id'] for f in display),'Unresolved German licence data withheld')
result={'passed_checks':checks,'reviewed_count':len(reviewed),'display_count':len(display),
 'context_only_withheld_count':sum(bool(f['properties'].get('gypsum_spawn_evidence_withheld')) for f in display),
 'known_invalid_source_geometry_ids':[f['id'] for f in reviewed+display if not f['properties']['geometry_valid']],
 'no_country_or_mine_point_buffer_created':True}
(ROOT/'validation-integration.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
source_rows=[]
for source,count in Counter(f['properties']['source_id'] for f in reviewed+display).items():
 p=next(f['properties'] for f in reviewed+display if f['properties']['source_id']==source)
 # Full reviewed source attributes/licence remain on the full normalized features
 # and in README; this index only routes consumers to the right provenance.
 source_rows.append({'source_id':source,'feature_count':count,'country':p['country'],'source_url':p['source_url'],'license_and_coverage_notes':'README.md','native_requests_and_checksums':'download-manifest.json'})
(ROOT/'sources.json').write_text(json.dumps(source_rows,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(result),flush=True)
