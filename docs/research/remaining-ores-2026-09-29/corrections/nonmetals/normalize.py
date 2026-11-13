"""Normalize supplemental occurrence evidence without mine-status/economic filters.

Research only. Gazetteer/region points never become deposit footprints. Unknown
depth/grade/economics stay null. GEMDATA/DIADATA are legacy research compilations,
not survey-grade locations or licensed runtime masks.
"""
from pathlib import Path
from collections import Counter
import csv,gzip,json,math,re,sys,tempfile
from pyogrio.raw import read
from shapely import from_wkb
from shapely.geometry import mapping
import xlrd
ROOT=Path(__file__).resolve().parent; RAW=ROOT/'raw'; OUT=ROOT/'normalized'; OUT.mkdir(exist_ok=True)
ATLAS=ROOT.parents[1]
sys.path.insert(0,str(ATLAS))
from build_mrds import MINERALS,PATTERNS

def clean(v):
    if hasattr(v,'item'):v=v.item()
    if v is None or isinstance(v,float) and not math.isfinite(v):return None
    if hasattr(v,'isoformat'):return v.isoformat()
    return v
def js(p,x):p.write_text(json.dumps(x,ensure_ascii=False,allow_nan=False,indent=2),encoding='utf8')
def shape_rows(archive,layer):
    meta,ids,wkb,cols=read('/vsizip/'+str(RAW/archive).replace('\\','/')+'/'+layer,return_fids=True)
    assert meta['crs']=='EPSG:4326',meta['crs']
    for i,fid in enumerate(ids):yield int(fid),mapping(from_wkb(wkb[i])),{str(k):clean(v[i]) for k,v in zip(meta['fields'],cols)}
def feature(id,geom,props):
    assert geom['type']=='Point'
    lon,lat=geom['coordinates'][:2]; assert -180<=lon<=180 and -90<=lat<=90
    return {'type':'Feature','id':id,'geometry':geom,'properties':dict(record_id=id,depth_m=None,grade=None,economic_status=None,early_accessibility=None,spawn_probability=None,**props)}
def main():
    allf=[];held=[];counts={}
    gems={'RUBY':'corundum','SAPPHIRE':'corundum','EMERALD':'emerald','AQUAMARINE':'beryl','HELIODOR':'beryl','MOGANITE':'beryl','GOSHENITE':'beryl','LAPIS_LAZU':'lapis','TOURMALINE':'tourmaline','PERIODIT':'olivine','TOPAZ':'topaz','GARNET':'garnet','SPINEL':'spinel','QUARZ':'SiO2'}
    # The source's MOGANITE field is ambiguous (morganite vs moganite).
    # Withhold that term rather than silently assign a mineral species.
    del gems['MOGANITE']
    # GOSHENITE/PERIODIT/QUARZ are source spellings for the listed gem varieties.
    gemrows=list(shape_rows('gemdata.zip','GEMDATA.shp'));assert len(gemrows)==1022
    for fid,geom,a in gemrows:
        if a['MOGANITE']==1:held.append({'source':'gemdata','id':a['PRIMKEY'],'reason':'MOGANITE field ambiguous; this term alone does not identify beryl. Other explicit gem flags retained.'})
        forms=[k for k in gems if a[k]==1];codes=sorted({gems[k] for k in forms})
        if not codes:continue
        attrs={k:a[k] for k in ['PRIMKEY','COUNTRY','NAME','GEOSOURCE','GEOCOM','MINING','DISC_Y','DISC_P','PRO_Y','PRO_P','LOCSOURCE','INFOSOURCE']}
        allf.append(feature('gemdata:'+str(a['PRIMKEY']),geom,{'name':a['NAME'],'country':a['COUNTRY'],'commodities':codes,'mineral_forms':forms,'source_id':'gemdata_2005','source_url':'https://www.paivilujala.com/gemdata.html','evidence_kind':'reported_gem_locality_or_region','coordinate_precision':'Mixed mine, gazetteer and administrative-region locations; see GEOCOM. No inferred footprint.','source_status':a['MINING'],'source_attributes':attrs,'historical_dates':'Source year and precision codes retained together; 1945 can encode pre-1946, not an exact date.','runtime_use':'hold_for_location_and_rights_review','license_note':'Primary author offers research download and asks citation; no explicit open redistribution license located.'}))
    counts['gemdata_source_rows']=len(gemrows);counts['gemdata_scope_records']=len(allf)
    dia=list(shape_rows('diadata-DIADATA Data.zip','DIADATA.shp'));assert len(dia)==1175
    for fid,geom,a in dia:
        if a['RES']!='DIA':held.append({'source':'diadata','id':a['PRIMKEY'],'reason':'Resource identity null'});continue
        allf.append(feature('diadata:'+str(a['PRIMKEY'])+':fid'+str(fid),geom,{'name':a['NAME'],'country':a['COUNTRY'],'commodities':['diamond'],'mineral_forms':['diamond'],'source_id':'diadata_1a','source_url':'https://www.prio.org/data/10','evidence_kind':'reported_diamond_locality_or_region','coordinate_precision':'Mixed site and gazetteer positions; source LOCDER/LOCINFO retained; no footprint inferred.','source_status':a['MINEINFO'],'source_attributes':{k:a[k] for k in ['PRIMKEY','NAME','COUNTRY','LOCDER','LOCINFO','LOCSOURCE','RES','RESINFO','DIAINFO','MINEINFO','DISC','DISCPRES','PROD','PRODPRES','SOURCEINFO']},'runtime_use':'hold_for_location_and_rights_review','license_note':'PRIO public research download with citation; no explicit open redistribution license located.'}))
    counts['diadata_source_rows']=len(dia);counts['diadata_scope_records']=len(dia)-1;counts['diadata_duplicate_original_key_groups']=1
    for layer,code in [(36,'diamond'),(37,'F'),(39,'C_graphite'),(49,'P'),(51,'K')]:
        data=json.loads((RAW/f'ga-layer-{layer}-points.geojson').read_text());expected=json.loads((RAW/f'ga-layer-{layer}-count.json').read_text())['count'];assert len(data['features'])==expected
        counts[f'ga_{layer}']=expected
        for f in data['features']:
            a=f['properties'];allf.append(feature(f'ga_aimr:{layer}:'+str(a['objectid']),f['geometry'],{'name':a['entity_id'],'country':'Australia','commodities':[code],'mineral_forms':[a['mineraldeposittype']] if a['mineraldeposittype'] else [],'source_id':'ga_aimr_2026_service','source_url':f'https://services.ga.gov.au/gis/rest/services/AustraliasIdentifiedMineralResources/MapServer/{layer}','evidence_kind':'identified_deposit_or_mine','coordinate_precision':'Source GDA94 (4283), transformed by source service to WGS84. Original coordinates preserved.','source_status':a['currentstatus'],'source_attributes':a,'runtime_use':'geological_form_depth_and_host_review_required','license_note':'Commonwealth of Australia (Geoscience Australia) 2026, CC BY 4.0.'}))
    # USGS table includes small showings, fields, historical producers and
    # deposits, not just current mines. Exclude publisher-restricted starred
    # records from derived output; retain entire supplied archive as research.
    sheet=xlrd.open_workbook(RAW/'afghanistan-of02-110.xls').sheet_by_index(0)
    headers=sheet.row_values(1);afgcount=0
    extra={'coal':'C_coal','salt':'NaCl','fluorspar':'F','sulfur':'S','sulphur':'S','gypsum':'gypsum','phosphate':'P','graphite':'C_graphite','lapis':'lapis','alunite':'alum'}
    elements={'Fe','Au','Ag','Pb','Zn','Bi','Ni','Cr','Ti','Mn','Hg','W','U','Pt','V','Mo','Sb','B','P','Cu','Sn','Al'}
    for row in range(5,sheet.nrows):
        vals=sheet.row_values(row);a=dict(zip(headers,vals));name=str(vals[0]);commodity=str(vals[6]);materials=str(vals[11]);text=commodity+' '+materials
        if not commodity.strip():continue
        if '*' in name or '*' in str(vals[1]):held.append({'source':'usgs_afghanistan_2002','row':row+1,'reason':'Publisher-restricted starred record; no derived geometry'});continue
        codes={x for x in elements if re.search(r'(?<![A-Za-z])'+x+r'(?![A-Za-z])',commodity)}
        codes|={c for k,c in extra.items() if re.search(r'\b'+k+r'\b',text,re.I)}
        forms=[m for m,pat in PATTERNS.items() if pat.search(materials)]
        codes|={MINERALS[m][0] for m in forms}
        if not codes:continue
        try:lat=float(vals[15]);lon=float(vals[16]);assert 29<=lat<=39 and 60<=lon<=75
        except (ValueError,TypeError,AssertionError):held.append({'source':'usgs_afghanistan_2002','row':row+1,'name':name,'reason':'Missing, ambiguous or multiple coordinates; no invented center'});continue
        attrs={headers[i]:clean(vals[i]) for i in [0,2,3,4,5,6,7,8,9,10,11,12,14,15,16]}
        allf.append(feature(f'usgs_afghanistan_2002:row{row+1}',{'type':'Point','coordinates':[lon,lat]},{'name':name,'country':'Afghanistan','commodities':sorted(codes),'mineral_forms':forms,'source_id':'usgs_afghanistan_2002','source_url':'https://pubs.usgs.gov/of/2002/0110/','evidence_kind':'reported_occurrence_deposit_or_field','coordinate_precision':'Source supplied decimal coordinates; some are district/field centers. Original precision retained.','source_status':vals[8] or None,'source_attributes':attrs,'runtime_use':'geological_form_depth_and_host_review_required','license_note':'USGS OFR02-110; publisher-restricted starred records withheld from derived output.'}));afgcount+=1
    counts['afghanistan_scope_records']=afgcount
    ids=[f['id'] for f in allf];assert len(ids)==len(set(ids))
    counts['normalized_records']=len(allf);counts['withheld']=len(held)
    js(OUT/'additional-evidence.geojson',{'type':'FeatureCollection','features':allf})
    js(OUT/'withheld.json',held)
    js(ROOT/'validation.json',{'status':'PASS','source_count_assertions':True,'coordinates_valid':True,'unique_source_ids':True,'no_economic_status_filter':True,'counts':counts,'commodity_links':dict(Counter(c for f in allf for c in f['properties']['commodities'])),'all_game_spawn_probabilities_null':all(f['properties']['spawn_probability'] is None for f in allf)})
    print(json.dumps(counts),flush=True)
if __name__=='__main__':main()

