"""Deterministic offline validation. No build, game, atlas or original-source mutations."""
from pathlib import Path
import json,math,hashlib,collections

B=Path(__file__).resolve().parent
checks=[]
def check(ok,message):
 checks.append({'pass':bool(ok),'check':message})
 if not ok:raise AssertionError(message)
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
summary={}
for file,expected in [('pp1802-global.geojson',2125),('ga-aimr-metals.geojson',3205),('reviewed-secondary-metals.geojson',2)]:
 p=B/'normalized'/file;features=read(p)['features'];ids=[f['properties']['record_id'] for f in features]
 check(len(features)==expected,f'{file} count = {expected}')
 check(len(ids)==len(set(ids)),f'{file} unique source identifiers')
 for f in features:
  g=f['geometry'];check(g['type'] in ['Point','MultiPolygon'],f'{f["id"]} supported source geometry')
  if g['type']=='Point':points=[g['coordinates']]
  else:
   rings=[r for poly in g['coordinates'] for r in poly]
   check(bool(rings) and all(len(r)>=4 and r[0]==r[-1] for r in rings),f'{f["id"]} nonempty closed polygon rings')
   points=[xy for ring in rings for xy in ring]
  check(bool(points) and all(len(xy)>=2 and all(math.isfinite(n) for n in xy[:2]) and -180<=xy[0]<=180 and -90<=xy[1]<=90 for xy in points),f'{f["id"]} finite longitude/latitude bounds')
  check(f['properties'].get('economic_status') is None and f['properties'].get('depth_m') is None,f'{f["id"]} unknown economy/depth not fabricated')
 summary[file]={'features':len(features),'geometry_counts':dict(collections.Counter(f['geometry']['type'] for f in features)),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()}
pp=read(B/'normalized/pp1802-global.geojson')['features'];ppids={f['properties']['record_id']:f for f in pp}
secondary=read(B/'normalized/reviewed-secondary-metals.geojson')['features']
vms=read(B.parents[1]/'normalized/Ag-specialist.geojson')['features'];vmsids={f['properties']['record_id']:f for f in vms}
for f in secondary:
 p=f['properties'];original=(ppids|vmsids)[p['coordinate_source_record_id']]
 check(f['geometry']==original['geometry'],f'{f["id"]} geometry exactly preserved from named survey record')
 check(p['country']==original['properties']['country'] and p['name']==original['properties']['name'],f'{f["id"]} exact identity match')
 check(p['count_as_new_deposit'] is False and len(p['evidence_source_urls'])>0,f'{f["id"]} source attribution and duplicate flag')
deep=read(B/'latvia-deep-evidence.geojson')['features']
check(len(deep)==2 and all(f['geometry'] is None and f['properties']['heatmap_eligible'] is False for f in deep),'Deep Latvian records stay nonspatial and out of point heatmaps')
audit=read(B/'metal-audit.json');probes=read(B/'province-checks.json');countries=read(B/'country-checks.json')
check({r['commodity'] for r in audit}==set('Fe Au Ag Pb Zn Bi Ni Cr Ti Mn Hg W U Pt V Mo Sb Al Cu Sn'.split()),'All20 metallic commodity layers explicitly audited')
check(len(probes)==196 and len(countries)==230,'All196 province windows and230 country checks present')
check(next(r for r in audit if r['commodity']=='Cu')['existing_mrds_records']>10000,'Compressed copper input really loaded; no silent missing-file fallback')
expected={('Fe','Staicele'),('Hg','Pulganbar'),('U','Chu Sarysu'),('U','Lagoa Real'),('U','Kirovohrad'),('U','Kyzylkum'),('Sb','Krinj')}
check({(r['commodity'],r['province']) for r in probes if r['existing_point_records']+r['new_point_records']==0}==expected,'Seven known unresolved spatial alarms retained visibly')
check(all(r['existing_mrds_records']>0 and r['named_province_checks']>=6 and 'complete' not in r['verdict'].lower().split() for r in audit),'No metal claimed globally complete or silently missing base evidence')
result={'pass':True,'assertions':len(checks),'normalized_outputs':summary,'audited_commodities':20,'named_province_windows':196,'country_alarms':230,'known_remaining_spatial_alarms':sorted(expected),'notes':['Source identity, finite coordinates and ring closure checked; general polygon self-intersection/topology not evaluated.','Source geometry checks do not establish orebody dimensions, grade, depth, mineability or calibrated probability.','Sentinel windows are search queries, never exported as geological permissive polygons.','No cross-dataset automatic deduplication or resource-count interpretation.']}
(B/'validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(result,indent=2))
