"""Normalize acquired public data. No network and no gameplay edits.

Core CSV parsing uses the standard library. FileGDB export additionally uses
pyogrio 0.13.0 and shapely 2.1.2 from a temporary research-only environment.
"""
from pathlib import Path
from zipfile import ZipFile
from collections import Counter
import csv, io, json, math, re, sys, tempfile, gzip, hashlib

ROOT = Path(__file__).resolve().parent
RAW, OUT = ROOT / 'raw', ROOT / 'normalized'
OUT.mkdir(exist_ok=True)
import pyogrio
from pyogrio.raw import read
from shapely import from_wkb
from shapely.geometry import mapping

REPORT_URL = 'https://pubs.usgs.gov/sir/2010/5090/z/'
FIELDS = ['record_id','source_id','source_feature_id','commodity','name','country','region',
 'longitude','latitude','crs','location_precision','evidence_kind','source_status',
 'deposit_style','host_geology','associated_geology','minerals','occurrence_form',
 'grade_value','grade_unit','grade_basis','economic_status','early_accessibility',
 'historical_period','historical_evidence_kind','source_url','source_references',
 'source_quality','review_status','heatmap_eligible','exclusion_reason','notes']
stats = {}

def clean(v):
    if hasattr(v, 'item'): v = v.item()
    if isinstance(v, float) and (not math.isfinite(v) or v == -9999): return None
    if isinstance(v, int) and v == -9999: return None
    return v

def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, allow_nan=False, separators=(',',':')), encoding='utf-8')

def csv_write(path, rows, fields=None):
    with path.open('w', newline='', encoding='utf-8') as f:
        writer = csv.DictWriter(f, fieldnames=fields or FIELDS, extrasaction='raise')
        writer.writeheader(); writer.writerows(rows)

def point_feature(r, full=False):
    fields = FIELDS if full else ['record_id','source_id','source_feature_id','commodity','name','country',
        'evidence_kind','source_status','deposit_style','occurrence_form','grade_value','grade_unit',
        'source_quality','heatmap_eligible','source_url']
    return {'type':'Feature','id':r['record_id'],
        'geometry':{'type':'Point','coordinates':[r['longitude'],r['latitude']]},
        'properties':{k:r.get(k) for k in fields}}

def base():
    r = dict.fromkeys(FIELDS)
    r.update(crs='EPSG:4326',location_precision='source point; positional accuracy not quantified',
        economic_status='unknown',early_accessibility='unassessed',review_status='source imported; record not independently verified',
        heatmap_eligible=True,occurrence_form='unknown',historical_evidence_kind='not established')
    return r

def normalize_mrds():
    source_rows, rows, excluded = [], [], []
    total = 0
    with ZipFile(RAW/'mrds-csv.zip') as z:
        reader = csv.DictReader(io.TextIOWrapper(z.open('mrds.csv'), encoding='utf-8-sig'))
        original_fields = reader.fieldnames
        for s in reader:
            total += 1
            commodities = ' '.join(s[k] for k in ['commod1','commod2','commod3'])
            matches = [(code,label) for code,label in [('Cu','Copper'),('Sn','Tin')] if re.search(r'\b'+label+r'\b',commodities,re.I)]
            if not matches: continue
            source_rows.append(s)
            lon,lat = float(s['longitude']),float(s['latitude'])
            assert -180 <= lon <= 180 and -90 <= lat <= 90
            for code,label in matches:
                r = base()
                r.update(record_id='mrds:'+s['dep_id']+':'+code,source_id='usgs_mrds_digest',source_feature_id=s['dep_id'],
                    commodity=code,name=s['site_name'],country=s['country'] or None,region=s['region'] or None,
                    longitude=lon,latitude=lat,evidence_kind='reported_mineral_site',source_status=s['dev_stat'],
                    deposit_style=s['dep_type'] or s['model'] or None,
                    host_geology='; '.join(filter(None,[s['hrock_unit'],s['hrock_type']])) or None,
                    associated_geology='; '.join(filter(None,[s['arock_unit'],s['arock_type']])) or None,
                    minerals=s['ore'] or None,source_url=s['url'],source_references=s['ref'] or None,source_quality=s['score'] or None,
                    notes='Source commodity priority: '+', '.join(k for k in ['commod1','commod2','commod3'] if re.search(r'\b'+label+r'\b',s[k],re.I)))
                if re.search(r'\b(placer|alluvial)\b',s['dep_type']+' '+s['model'],re.I):
                    r['occurrence_form']='secondary_placer_or_alluvial_explicit_in_source'
                if any(s[k] for k in ['yr_fst_prd','yr_lst_prd','prod_yrs']):
                    r['historical_period']='; '.join(k+'='+s[k] for k in ['yfp_ba','yr_fst_prd','ylp_ba','yr_lst_prd','prod_yrs'] if s[k])
                    r['historical_evidence_kind']='database production chronology; not proof of ancient extraction'
                if s['dev_stat'].strip().lower() == 'plant':
                    r.update(heatmap_eligible=False,exclusion_reason='Processing plant may use imported feed; not evidence for a local natural deposit.')
                    excluded.append(r)
                else: rows.append(r)
    csv_write(OUT/'mrds-copper-tin-source-subset.csv',source_rows,original_fields)
    csv_write(OUT/'mrds-copper-tin-occurrences.csv',rows)
    csv_write(OUT/'mrds-excluded-plants.csv',excluded)
    for code in ['Cu','Sn']:
        selected=[r for r in rows if r['commodity']==code]
        obj={'type':'FeatureCollection','name':'MRDS '+code+' reported sites; plants excluded','features':[point_feature(r) for r in selected]}
        if code == 'Sn': write_json(OUT/'mrds-tin.geojson',obj)
        else:
            with gzip.open(OUT/'mrds-copper.geojson.gz','wt',encoding='utf-8') as f: json.dump(obj,f,ensure_ascii=False,allow_nan=False,separators=(',',':'))
        stats['mrds_'+code]={'retained':len(selected),'excluded_plants':sum(r['commodity']==code for r in excluded),
            'source_status_counts':dict(Counter(r['source_status'] for r in selected)),
            'country_counts':dict(Counter(r['country'] or 'unknown' for r in selected)),
            'source_quality_counts':dict(Counter(r['source_quality'] for r in selected)),
            'explicit_secondary_records':sum(r['occurrence_form']!='unknown' for r in selected),
            'production_chronology_records':sum(bool(r['historical_period']) for r in selected)}
    stats['mrds_source_rows']=total
    stats['mrds_matching_source_rows']=len(source_rows)
    return rows

def gdb_layers():
    gdb='/vsizip/'+str(RAW/'copper-assessment-gis.zip').replace('\\','/')+'/sir20105090z_gis.zip/USGS_global_copper.gdb'
    points=[]
    for layer in ['pCu_deps_pros','sedCu_deps_pros','pCu_Precambrian_deps_pros','pCu_tracts','sedCu_tracts',
                  'pCu_sites_references','pCu_tracts_references','sedCu_references']:
        info=pyogrio.read_info(gdb,layer=layer)
        meta,fids,wkb,arrays=read(gdb,layer=layer,return_fids=True)
        features=[]; source_rows=[]; issues=[]
        for i,fid in enumerate(fids):
            s={str(field):clean(values[i]) for field,values in zip(meta['fields'],arrays)}
            source_rows.append({'source_objectid':int(fid),**s})
            if wkb is None: continue
            geom=from_wkb(wkb[i])
            if not geom.is_valid: issues.append(int(fid))
            if layer.endswith('_tracts'):
                normalized={'source_id':'usgs_global_copper_assessment_2016','source_layer':layer,
                    'source_feature_id':s['Coded_ID'],'commodity':'Cu','evidence_kind':'expert_geological_permissiveness',
                    'name':s['Tract_name'],'deposit_style':s['Dep_type'],'host_geology':s['Geology'],
                    'source_url':REPORT_URL,'precision':'intended scale 1:1,000,000 or coarser',
                    'game_spawn_probability':None,'early_accessibility':'unassessed; assessment depth is not accessible depth',
                    'geometry_valid':bool(geom.is_valid),'source_attributes':s}
                features.append({'type':'Feature','id':layer+':'+s['Coded_ID'],'geometry':mapping(geom),'properties':normalized})
            else:
                r=base(); grade=s.get('Cu_pct')
                zero_grade = grade == 0
                if grade is not None and grade <= 0: grade=None
                source_id=str(s.get('GMRAP_ID') if s.get('GMRAP_ID') is not None else int(fid))
                r.update(record_id='global_cu:'+layer+':'+source_id,source_id='usgs_global_copper_assessment_2016',
                    source_feature_id=layer+':'+source_id,commodity='Cu',name=s.get('Name'),country=s.get('Country'),
                    region=s.get('Study_area'),longitude=geom.x,latitude=geom.y,
                    location_precision='source point; database intended scale 1:1,000,000 or coarser',
                    evidence_kind='reported_'+str(s.get('SiteStatus') or 'mineral_site'),source_status=s.get('SiteStatus'),
                    deposit_style='; '.join(filter(None,[s.get('Dep_type'),s.get('Dep_subtype')])) or None,
                    host_geology='; '.join(filter(None,[s.get('Unit'),s.get('Host_rocks')])) or None,
                    associated_geology=s.get('Assd_rock'),minerals=s.get('Minerals'),
                    occurrence_form='primary_mineralization; surface expression unassessed',grade_value=grade,
                    grade_unit='wt% Cu' if grade is not None else None,
                    grade_basis='source deposit average; not an early hand-sorted ore grade' if grade is not None else None,
                    source_url=REPORT_URL,source_references='; '.join(filter(None,[s.get('Ref_list'),s.get('Short_ref')])) or None,
                    source_quality='specialist compilation; see regional report',
                    notes=s.get('Comments'))
                if layer=='pCu_Precambrian_deps_pros':
                    r['notes']='Reference-only Precambrian layer; not generally used to delineate tracts. '+(r['notes'] or '')
                if zero_grade:
                    r['notes']=(r['notes'] or '')+' Source grade zero not validated as a measured value; normalized grade withheld, original retained in source CSV.'
                points.append(r); features.append(point_feature(r,full=True))
        if source_rows: csv_write(OUT/(layer+'-source.csv'),source_rows,list(source_rows[0]))
        if features:
            write_json(OUT/(layer+'.geojson'),{'type':'FeatureCollection','name':layer,'features':features})
            if layer.endswith('_tracts'):
                from shapely.geometry import shape
                preview=[]
                for feature in features:
                    p={k:v for k,v in feature['properties'].items() if k!='source_attributes'}
                    p['display_only_simplification_degrees']=0.02
                    geom=shape(feature['geometry']).simplify(0.02,preserve_topology=True)
                    preview.append({'type':'Feature','id':feature['id'],'geometry':mapping(geom),'properties':p})
                write_json(OUT/(layer+'-display-only.geojson'),{'type':'FeatureCollection','name':layer+' simplified for display; NOT generation-ready','features':preview})
        stats[layer]={'features':int(info['features']),'crs':info['crs'],'invalid_geometry_source_objectids':issues}
        print(layer,len(source_rows),flush=True)
    csv_write(OUT/'global-copper-assessment-occurrences.csv',points)
    with ZipFile(RAW/'copper-assessment-gis.zip') as z:
        for n in z.namelist():
            if '/metadata/' in n and n.endswith('.xml'):
                target=OUT/'source-metadata'/Path(n).name
                target.parent.mkdir(exist_ok=True); target.write_bytes(z.read(n))
    return points

def summarize(all_rows):
    assert len({r['record_id'] for r in all_rows})==len(all_rows), 'duplicate source identifiers'
    assert all(-180.000001<=r['longitude']<=180.000001 and -90<=r['latitude']<=90 for r in all_rows)
    assert all(r['grade_value'] is None or r['grade_value']>=0 for r in all_rows)
    stats['total_normalized_evidence_rows']=len(all_rows)
    stats['not_deduplicated_across_sources']=True
    stats['grades_present']=sum(r['grade_value'] is not None for r in all_rows)
    stats['tin_permissive_polygon_imported']=False
    write_json(ROOT/'validation-summary.json',stats)
    manifests=[]
    for p in sorted(OUT.rglob('*')):
        if p.is_file(): manifests.append({'file':str(p.relative_to(ROOT)).replace('\\','/'),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
    write_json(ROOT/'normalized-manifest.json',manifests)
    print(json.dumps({k:v for k,v in stats.items() if not isinstance(v,dict)},indent=2))

if __name__=='__main__':
    mrds=normalize_mrds()
    copper=gdb_layers()
    summarize(mrds+copper)
