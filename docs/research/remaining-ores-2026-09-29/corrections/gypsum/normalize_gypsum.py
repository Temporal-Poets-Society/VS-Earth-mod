"""Normalize reviewed positive evidence; never convert an unmapped place to absence.

Requires pyogrio, shapely and pyproj. Existing isolated research dependency
directories are supported; nothing is installed by this script.
Raw geometries remain in source files. Full normalized polygons are lossless
apart from CRS conversion; separate 0.005-degree simplified output is DISPLAY ONLY.
"""
from pathlib import Path
from collections import Counter
from zipfile import ZipFile
import csv,gzip,html,json,math,re,sys,tempfile
from pyogrio.raw import read
from shapely import from_wkb
from shapely.geometry import shape,mapping,Polygon,Point
from shapely.ops import transform
from pyproj import Transformer
from fetch_gypsum_sources import SOURCES

ROOT=Path(__file__).resolve().parent;RAW=ROOT/'raw';OUT=ROOT/'normalized';OUT.mkdir(exist_ok=True)
reviewed=[];formations=[];native=[];crslog={};audit=[]
def load(name):return json.loads((RAW/name).read_text(encoding='utf-8-sig'))
def clean(v):
 if hasattr(v,'item'):v=v.item()
 if isinstance(v,float) and not math.isfinite(v):return None
 return v
def tx(crs):
 key=str(crs)
 if key not in crslog:
  t=Transformer.from_crs(crs,'EPSG:4326',always_xy=True,allow_ballpark=False)
  crslog[key]={'transformer':t,'accuracy_m':t.accuracy,'operation':t.description}
 return crslog[key]['transformer'].transform
def feature(collection,source,fid,geometry,name,country,identity,kind,attrs,crs,url,**extra):
 props={'source_id':source,'source_feature_id':str(fid),'source_url':url,'name':name,'country':country,
  'mineral_identity':identity,'evidence_kind':kind,'source_attributes':attrs,'source_crs':str(crs),
  'geometry_valid':bool(geometry.is_valid),'geometry_provenance':'Source geometry reprojected, no buffer or invented footprint',
  'depth_min_m':None,'depth_max_m':None,'current_operating_status':None,'economic_status':None,
  'absence_evidence':False,'spawn_probability':None,'source_retrieved_utc_date':'2026-09-29',**extra}
 if identity=='anhydrite':props['gypsum_spawn_evidence_withheld']=True
 collection.append({'type':'Feature','id':source+':'+str(fid),'geometry':mapping(geometry),'properties':props})
 return props
def shp(name,path,layer=0):
 m,f,w,a=read(path,layer=layer,return_fids=True)
 for i,fid in enumerate(f):yield int(fid),from_wkb(w[i]),{str(k):clean(v[i]) for k,v in zip(m['fields'],a)},m['crs']
def zip_path(name,inner):return '/vsizip/'+str((RAW/name).resolve()).replace('\\','/')+'/'+inner

# Legal annexes contain actual declared deposit boundaries, not survey formation extents.
s=html.unescape(re.sub('<[^>]+>',' ',(RAW/'latvia-state-deposits-law.html').read_text(encoding='utf-8')))
s=re.sub(r'\s+',' ',s)
parts=s.split('robežpunktu koordinātas')[1:3]
for name,part,expected in zip(['Salaspils','Skaistkalne'],parts,[16,29]):
 part=part.split('Vides aizsardzības')[0]
 rows=re.findall(r'(\d+)\.\s+(\d+)\s+(\d+,\d+)\s+(\d+,\d+)',part)
 assert len(rows)==expected,(name,len(rows))
 assert [int(r[1]) for r in rows]==list(range(1,expected+1))
 # The law labels X=northing and Y=easting. EPSG3059 always_xy expects E,N.
 coords=[(float(y.replace(',','.')),float(x.replace(',','.'))) for _,_,x,y in rows]
 poly=Polygon(coords);assert poly.is_valid
 native.append({'name':name,'crs':'EPSG:3059','source_axis_labels':'X northing; Y easting','coordinate_rows':rows,'geometry':mapping(poly)})
 feature(reviewed,'latvia_cabinet321_2012',name,transform(tx('EPSG:3059'),poly),name,'Latvia','gypsum','declared_geological_deposit_boundary',{'annex':1 if name=='Salaspils' else 2,'source_coordinate_count':expected},'EPSG:3059',SOURCES['latvia-state-deposits-law.html'],
  geometry_provenance='Exact numbered boundary vertices transcribed from official legal annex; X/Y axes explicitly reordered for EPSG3059',
  source_scale_limitation='Declared deposit boundary; not formation coverage or uniform gypsum ore',license_note='Factual boundary coordinates extracted from official legal act; original act attribution retained',depth_note='Depth not stated in boundary table',overlap_note='Salaspils overlaps LVGMC licensed-subarea polygon; do not count as independent deposit')
(OUT/'latvia-legal-boundaries-native.json').write_text(json.dumps(native,ensure_ascii=False,indent=2),encoding='utf-8')

commodities={f['properties']['IFCID']:f['properties'] for f in load('latvia-commodity.geojson')['features'] if f['properties']['commodity_code']=='gypsum'}
mines={f['properties']['IFCID']:f['properties'] for f in load('latvia-mines-native.geojson')['features']}
for f in load('latvia-occurrence-polygons-native.geojson')['features']:
 a=f['properties'];key=a['IFCID']
 if key not in commodities:continue
 m=mines.get(key,{})
 feature(reviewed,'lvgmc_inspire_licensed_occurrence',f['id'],transform(tx('EPSG:4258'),shape(f['geometry'])),m.get('minename_1','unnamed gypsum'),'Latvia','gypsum','licensed_mineral_occurrence_subarea',{'geometry_attributes':a,'commodity':commodities[key],'mine':m},'EPSG:4258',SOURCES['latvia-occurrence-polygons-native.geojson'],license_note='Latvia data catalogue CC0-1.0; WFS access constraints NONE',current_operating_status=m.get('status_code'),status_date=m.get('id_versionId'),coverage_warning='Source is licensed occurrence subset, not complete geological occurrence map; overlaps legal Salaspils boundary')

for prefix in ['poland','poland-archived']:
 for f in load(prefix+'-gypsum-wgs84.geojson')['features']:
  a=f['properties']
  feature(reviewed,'pgi_midas_'+('historical' if 'archived' in prefix else 'documented'),a['OBJECTID'],shape(f['geometry']),a['NAZWA_ZL'],'Poland','gypsum_or_anhydrite_undifferentiated','documented_deposit_boundary',a,'EPSG:2180',SOURCES[prefix+'-gypsum-native.json'],source_document_crs=a.get('UKL_WSP'),deposit_identifier=a['NR_ZLOZA'],license_note='PIG-PIB reuse notice requires source URL and retrieval date attribution',geometric_accuracy_note=a.get('LOKACJA'),source_scale_limitation='Documented deposit, not all gypsum-bearing formation; some original boundaries calibrated from map sheets',economic_status='removed_from_national_balance' if 'archived' in prefix else None,mineral_note='MIDAS commodity group combines gypsum and anhydrite; do not silently convert all records to gypsum')

for f in load('spain-gypsum-wgs84.geojson')['features']:
 a=f['properties'];identity='gypsum_and_anhydrite' if 'anhidr' in a['Sustancia'].lower() and 'yes' in a['Sustancia'].lower() else 'anhydrite' if 'anhidr' in a['Sustancia'].lower() else 'gypsum'
 feature(reviewed,'igme_bdmin',a['Codigo_roca'],shape(f['geometry']),(a['Municipio'] or 'unnamed locality')+' / '+a['Codigo_roca'],'Spain',identity,'industrial_mineral_occurrence_or_working',a,'EPSG:4326',SOURCES['spain-gypsum-native.json'],current_operating_status=a['Estado_Explotacion'],host_geology=a['Unidad_Geotectonica'],license_note='IGME free reuse with attribution; Origen de los datos: ©Instituto Geológico y Minero de España (IGME)',coverage_warning='Source indications/workings inventory; all recorded statuses retained, not a complete mineral distribution')

for r in json.loads((ROOT/'reviewed-bgs-localities.json').read_text()):
 feature(reviewed,'bgs_report_locality',r['id'],transform(tx('EPSG:27700'),Point(r['easting'],r['northing'])),r['name'],'United Kingdom',r['identity'],r['kind'],r,'EPSG:27700',SOURCES[r['source']],host_geology=r['host'],position_precision_m=r['precision_m'],depth_note=r['depth_note'],historical_status=r['status'],economic_status='noncommercial_as_reported' if r['id']=='fairlight-borehole' else None,license_note='Small factual locality extraction from attributed primary report; BGS licensed resource-linework NOT copied',geometry_provenance='Report national-grid reference converted to WGS84; point only, no deposit extent inferred')

path=zip_path('france-idf-resources.zip','dataset/L_RESSOURCES_HSDC_S_R11.shp')
for fid,g,a,crs in shp('france',path):
 if a['SUBSTANCE_']!='Gypse':continue
 feature(formations,'drieat_idf_resources',fid,transform(tx(crs),g),a['FORM_GEOL_'] or a['APPELLATIO'],'France','gypsum','mapped_gypsum_resource_under_cover',a,crs,SOURCES['france-idf-catalog.json'],source_scale_limitation='Harmonized departmental resource maps; average gypsum limit below cover, not exposed outcrop',depth_note=a['APPELLATIO'],license_note='Licence Ouverte / Open Licence 2.0; DRIEAT Île-de-France',source_update_date='2018-11-19')

unit_counts=Counter()
seen=set()
for path in sorted(RAW.glob('spain-geode-gypsum-page-*.json.gz')):
 # GDAL preserves Esri ring topology and returns exact original vertices.
 meta,fids,wkbs,arrays=read('/vsigzip/'+str(path.resolve()).replace('\\','/'),return_fids=True)
 assert '3857' in str(meta['crs']) or 'Pseudo-Mercator' in str(meta['crs']),meta['crs']
 for i,w in enumerate(wkbs):
  a={str(k):clean(v[i]) for k,v in zip(meta['fields'],arrays)};fid=a['OBJECTID'];assert fid not in seen;seen.add(fid)
  desc=a['DESC_UNIT'];low=desc.lower();unit_counts[desc]+=1
  assert not re.search(r'\bsin\b|desyes|disolu|residu',desc,re.I),desc
  identity='gypsum_and_anhydrite' if 'anhidr' in low and 'yes' in low else 'anhydrite' if 'anhidr' in low else 'gypsum'
  feature(formations,'igme_geode50_gypsum_units',fid,transform(tx('EPSG:3857'),from_wkb(w)),desc,'Spain',identity,'mapped_mineral_bearing_geological_unit',a,'EPSG:3857',SOURCES['spain-geode-layers.json'],source_scale_limitation='1:50,000 geological unit; mixed lithology, mineral not present uniformly; no depth/grade inferred',license_note='Origen de los datos: ©Instituto Geológico y Minero de España (IGME)',gypsum_dominant_description=bool(re.match(r'^yesos(?:\s|,|$)',low)),geology_unit=a.get('CODE_UNIO'),source_zone=a.get('ZONA'))
ids=set(load('spain-geode-gypsum-ids.json')['objectIds']);assert seen==ids

# DL-DE-BY2 overview units: gypsum-bearing lithology is permissive, not positive
# gypsum at every point. Do not collapse dissolution residues to intact gypsum.
path=zip_path('germany-gkbw-gpkg.zip','dGKBW_gpkg/data/geo_ge3.gpkg')
unit_review={663:('gypsum_and_anhydrite','mixed_mineral_bearing_geological_unit'),650:('gypsum_and_anhydrite','mixed_mineral_bearing_geological_unit'),1015:('gypsum','mixed_mineral_bearing_geological_unit'),298:('gypsum_and_anhydrite','mixed_mineral_bearing_geological_unit'),931:('gypsum_and_anhydrite_often_dissolved','mineral_bearing_unit_with_major_preservation_uncertainty'),817:('anhydrite','anhydrite_bearing_geological_unit')}
for fid,g,a,crs in shp('germany',path):
 if a['ge_id'] not in unit_review:continue
 identity,kind=unit_review[a['ge_id']]
 feature(formations,'lgrb_gkbw_ge3',fid,transform(tx('EPSG:25832'),g),a['name'],'Germany',identity,kind,a,crs,'https://geoportal.lgrb-bw.de/produkt/dgkbw',source_scale_limitation='GKBW finest overview tier GE3; generalized formation geometry, not GK50 deposit survey',source_update_date=a.get('statdat'),license_note='Datenlizenz Deutschland - Namensnennung - Version 2.0; LGRB Baden-Württemberg',gypsum_spawn_evidence_withheld=a['ge_id'] in [931,817],depth_note='Unknown; Middle Muschelkalk sulphates commonly dissolved in source description',geometry_provenance='Original GE3 projected vertices reprojected from ETRS89 UTM32N; 3-axis CRS source WKT retained, horizontal coordinates transformed')

def dump(name,features,compressed=False):
 data=json.dumps({'type':'FeatureCollection','features':features},ensure_ascii=False,allow_nan=False,separators=(',',':')).encode('utf-8')
 if compressed:data=gzip.compress(data,mtime=0)
 (OUT/name).write_bytes(data)
dump('gypsum-reviewed.geojson',reviewed)
dump('gypsum-permissive-formations.geojson.gz',formations,True)
display=[]
for f in formations:
 p={k:v for k,v in f['properties'].items() if k not in ['source_attributes','source_crs','geometry_provenance','license_note']}
 p.update(display_only=True,display_simplification_degrees=0.005,not_for_generation=True)
 display.append({'type':'Feature','id':f['id'],'geometry':mapping(shape(f['geometry']).simplify(0.005,preserve_topology=True)),'properties':p})
dump('gypsum-permissive-formations-display-only.geojson',display)
with (ROOT/'spain-reviewed-unit-descriptions.csv').open('w',encoding='utf-8',newline='') as f:
 w=csv.writer(f);w.writerow(['source_description','source_polygon_count']);w.writerows(sorted(unit_counts.items()))
stats={'reviewed_count':len(reviewed),'formation_count':len(formations),'by_source':dict(Counter(f['properties']['source_id'] for f in reviewed+formations)),
 'by_mineral_identity':dict(Counter(f['properties']['mineral_identity'] for f in reviewed+formations)),
 'invalid_source_geometry_ids':[f['id'] for f in reviewed+formations if not f['properties']['geometry_valid']],
 'no_economic_or_operation_status_filter':True,'geode_source_id_count':len(ids),'geode_complete_id_match':seen==ids,
 'all_depth_endpoints_unknown_until_reviewed':True,'display_simplification_degrees':0.005,
 'projection_operations':{k:{a:v for a,v in d.items() if a!='transformer'} for k,d in crslog.items()}}
(ROOT/'validation.json').write_text(json.dumps(stats,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in stats.items() if k!='projection_operations'},ensure_ascii=False),flush=True)
