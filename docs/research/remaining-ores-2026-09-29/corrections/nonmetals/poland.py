"""Documented and archived Polish mineral areas; no commercial-status filter."""
from pathlib import Path
from urllib.parse import urlencode
import json,sys,tempfile
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT));from acquire import fetch
from shapely.geometry import shape
BASE='https://geologia.pgi.gov.pl/srv/rest/services/midas/MapServer'
MAPPING={'sulfur':['S'],'rock salts':['NaCl'],'potassium-magnesium salts':['K'],'vein quartz':['SiO2'],'quartzitic schists':['SiO2'],'quartzites':['SiO2'],'phosphorites':['P'],'glass sands and sandstones':['SiO2']}
def main():
    todo=[]
    for layer in [0,3]:
        field='KOPALINA_ALL' if layer==0 else 'KOPALINA'
        where='COMMODITY IN ('+','.join("'"+x+"'" for x in MAPPING)+f") OR UPPER({field}) LIKE '%FLUOR%'"
        params={'where':where,'outFields':'*','outSR':4326,'f':'geojson'}
        todo.append((f'pgi-{layer}-selected.geojson',f'{BASE}/{layer}/query?'+urlencode(params)))
        todo.append((f'pgi-{layer}-count.json',f'{BASE}/{layer}/query?'+urlencode({'where':where,'returnCountOnly':'true','f':'json'})))
    acquired=[fetch(x) for x in todo];features=[];issues=[]
    for layer in [0,3]:
        data=json.loads((ROOT/f'raw/pgi-{layer}-selected.geojson').read_text(encoding='utf8'));count=json.loads((ROOT/f'raw/pgi-{layer}-count.json').read_text())['count'];assert len(data['features'])==count
        for f in data['features']:
            a=f['properties'];codes=MAPPING.get(a.get('COMMODITY'),[]).copy();primary=a.get('KOPALINA_ALL',a.get('KOPALINA','')) or ''
            if 'FLUOR' in primary.upper():codes.append('F')
            if not codes:raise ValueError('Unmapped PGI commodity')
            geom=shape(f['geometry'])
            if geom.is_empty or not geom.is_valid:issues.append({'id':f.get('id'),'layer':layer,'reason':'Invalid source geometry; withheld from display'});continue
            lo,la,hi,ha=geom.bounds;assert 13<lo<25 and 13<hi<25 and 48<la<56 and 48<ha<56
            ident=f'pgi_midas:{layer}:'+str(a.get('NR_ZLOZA'))+':'+str(f.get('id'))
            features.append({'type':'Feature','id':ident,'geometry':f['geometry'],'properties':{'record_id':ident,'name':a.get('NAZWA_ZL'),'country':'Poland','commodities':sorted(set(codes)),'source_id':f'pgi_midas_layer{layer}','source_url':f'{BASE}/{layer}','source_attributes':a,'evidence_class':'documented_or_archived_mineral_area','geometry_role':'Official documented deposit area; not every volume inside contains accessible ore.','source_status':'documented' if layer==0 else 'removed_from_balance_or_archived','depth_m':None,'economic_status':None,'early_accessibility':None,'spawn_probability':None,'rights':'PGI source attribution and retrieval date required; research snapshot 2026-09-29','mineral_form_note':'Rock salts distinct from brine; potassium-magnesium salts not necessarily sylvite. Quartz sands/rock distinct from vein crystals.'}})
    (ROOT/'normalized/poland-minerals.geojson').write_text(json.dumps({'type':'FeatureCollection','features':features},ensure_ascii=False),encoding='utf8')
    (ROOT/'poland-validation.json').write_text(json.dumps({'status':'PASS','records':len(features),'withheld':issues,'acquisition':acquired},indent=2),encoding='utf8')
    print(json.dumps({'pgi_records':len(features),'withheld':len(issues)}))
if __name__=='__main__':main()
