"""Export legacy regional coal point evidence; native Pulkovo coordinates are preserved."""
from pathlib import Path
import csv,json,sys,tempfile
from pyproj import Transformer
ROOT=Path(__file__).resolve().parent
RAW=ROOT/'raw';OUT=ROOT/'normalized'

def main():
 transformer=Transformer.from_crs('EPSG:4284','EPSG:4326',always_xy=True)
 features=[];stats={}
 for layer in ['coal_dep','coal_pt']:
  n=0
  for line in (RAW/('fsu-'+layer+'.gen')).read_text().splitlines():
   parts=line.split()
   if len(parts)!=3:continue
   fid=int(parts[0]);x,y=map(float,parts[1:]);lon,lat=transformer.transform(x,y)
   assert -180<=lon<=180 and -90<=lat<=90
   feature={'type':'Feature','id':'fsu:'+layer+':'+str(fid),'geometry':{'type':'Point','coordinates':[lon,lat]},
    'properties':{'source_id':'usgs_fsu_coal_2001','source_layer':layer,'source_feature_id':fid,'commodity':'C_coal',
     'source_url':'https://pubs.usgs.gov/of/2001/ofr-01-104/','source_crs':'EPSG:4284','source_longitude':x,'source_latitude':y,
     'output_crs':'EPSG:4326','coordinate_transform':transformer.get_last_used_operation().description,
     'location_precision':'Original metadata state coordinates specified to nearest degree; source materials heterogeneous. Do not use as precise deposit outlines.',
     'name':None,'coal_rank':None,'grade_value':None,'early_accessibility':None,
     'record_type':'coal_deposit_location' if layer=='coal_dep' else 'coal_quality_sample_location',
     'note':'Raw ASCII attribute table retained, but rank/name relations not imported until documented join schema is verified; source layers can overlap.'}}
   features.append(feature);n+=1
  stats[layer]=n
 (OUT/'fsu-coal-points.geojson').write_text(json.dumps({'type':'FeatureCollection','features':features},ensure_ascii=False,separators=(',',':')),encoding='utf-8')
 (ROOT/'coal-validation.json').write_text(json.dumps({'source_point_counts':stats,'points_total':len(features),'confirmed_unique_deposits':None,
  'rank_join_status':'withheld pending full relational table schema review','basin_lines_status':'Partial raw shape without attributes; not exported or drawn',
  'reuse':'Coal-layer metadata Use_Constraints=None. Do not redistribute ESRI coastline/country layers from the parent project; those were not acquired.'},indent=2),encoding='utf-8')
 print(json.dumps(stats),flush=True)

if __name__=='__main__':main()
