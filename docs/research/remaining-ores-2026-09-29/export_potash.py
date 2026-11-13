"""Preserve global expert potash-tract geometry and attributes, not a halite mask."""
from pathlib import Path
from collections import Counter
from zipfile import ZipFile
import csv,json,math,sys,tempfile
from pyogrio.raw import read
from shapely import from_wkb
from shapely.geometry import mapping
ROOT=Path(__file__).resolve().parent
RAW=ROOT/'raw';OUT=ROOT/'normalized';OUT.mkdir(exist_ok=True)

def clean(v):
 if hasattr(v,'item'):v=v.item()
 if isinstance(v,float) and not math.isfinite(v):return None
 if isinstance(v,str) and v.strip() in ['', 'None','-9999']:return None
 return v

def export():
 stats={}
 for layer in ['PotashTracts','PotashDeps']:
  path='/vsizip/'+str(RAW/'potash-PotashGIS.zip').replace('\\','/')+'/PotashGIS/global_potash/Shapefiles/'+layer+'.shp'
  meta,fids,wkb,arrays=read(path,return_fids=True)
  assert str(meta['crs'])=='EPSG:4326'
  features=[];simple=[];issues=[];source=[]
  for i,fid in enumerate(fids):
   a={str(k):clean(v[i]) for k,v in zip(meta['fields'],arrays)};geom=from_wkb(wkb[i]);source.append(a)
   if not geom.is_valid:issues.append(int(fid))
   props={'source_id':'usgs_global_potash_2014','source_feature_id':a.get('Coded_ID') or a.get('ID_NO'),
    'source_url':'https://pubs.usgs.gov/sir/2010/5090/s/','source_attributes':a,
    'geometry_valid':bool(geom.is_valid),'source_scale_limitation':'1:1,000,000 or coarser; tract-specific source-map precision varies',
    'early_accessibility':None,'spawn_probability':None}
   if layer=='PotashTracts':
    props.update(evidence_kind='expert_geological_permissiveness_for_potash',name=a['Tract_name'],country=a['Country'],deposit_style=a['Dep_type'],
     host_geology=a['Geology'],unit=a['Unit'],assessment_depth_source=a['Asmt_depth'],
     halite_eligibility_review='Potash tract, NOT a validated halite footprint or surface exposure. Review saline basin type, stratigraphy and depth; brine tracts must remain distinct.')
   else:
    props.update(evidence_kind='source_record_type_'+str(a['TYPE_REC']),name=a['SITE_NAME'],country=a['COUNTRY'],deposit_style=a['DEP_TYPE'],
     explicit_minerals='; '.join(x for x in [a['K_MINERALS'],a['OTHER_MINS']] if x) or None,
     halite_explicit_in_source=bool(a['OTHER_MINS'] and 'halite' in a['OTHER_MINS'].lower()))
   feature={'type':'Feature','id':layer+':'+str(props['source_feature_id']),'geometry':mapping(geom),'properties':props};features.append(feature)
   if layer=='PotashTracts':
    p={k:v for k,v in props.items() if k!='source_attributes'}
    p['display_only_simplification_degrees']=0.02
    simple.append({'type':'Feature','id':feature['id'],'geometry':mapping(geom.simplify(0.02,preserve_topology=True)),'properties':p})
  (OUT/(layer+'.geojson')).write_text(json.dumps({'type':'FeatureCollection','features':features},ensure_ascii=False,allow_nan=False,separators=(',',':')),encoding='utf-8')
  if simple:(OUT/(layer+'-display-only.geojson')).write_text(json.dumps({'type':'FeatureCollection','features':simple},ensure_ascii=False,allow_nan=False,separators=(',',':')),encoding='utf-8')
  with (OUT/(layer+'-source.csv')).open('w',encoding='utf-8',newline='') as f:
   writer=csv.DictWriter(f,fieldnames=list(source[0]));writer.writeheader();writer.writerows(source)
  stats[layer]={'source_features':len(features),'invalid_geometry_source_fids':issues,
   'types':dict(Counter(a['Dep_type'] if layer=='PotashTracts' else a['TYPE_REC'] for a in source)),
   'coordinate_crs':meta['crs']}
 with ZipFile(RAW/'potash-PotashGIS.zip') as z:
  for n in z.namelist():
   if '/Metadata/' in n and n.endswith('.xml'):(RAW/Path(n).name).write_bytes(z.read(n))
 (ROOT/'potash-validation.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
 print(json.dumps(stats),flush=True)

if __name__=='__main__':export()
