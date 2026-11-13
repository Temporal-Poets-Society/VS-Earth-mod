"""Validate integrated correction semantics, source geometry and delivered artifacts."""
from pathlib import Path
from collections import Counter
from html.parser import HTMLParser
import hashlib,json,math,struct,sys,tempfile
ROOT=Path(__file__).resolve().parent
ATLAS=ROOT.parent
EARTH=ATLAS.parents[2]
sys.path.insert(0,str(ATLAS))
from shapely.geometry import shape,box
from correction_layers import load_corrections
from build_mrds import COMMODITIES

checks=0;failures=[];manifest=[]
def check(condition,label):
    global checks
    checks+=1
    if not condition:failures.append(label)
def track(path):
    manifest.append({'file':str(path.relative_to(EARTH)).replace('\\','/'),'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
def coordinates(v):
    if isinstance(v,list) and v and isinstance(v[0],(int,float)):yield v
    elif isinstance(v,list):
        for x in v:yield from coordinates(x)

def main():
    registry=json.loads((ROOT/'overlay-registry.json').read_text());track(ROOT/'overlay-registry.json')
    source_counts={};withheld=Counter();topology_checked=0;gypsum_qualified_invalid=0
    for entry in registry:
        path=ATLAS/entry['file'];check(path.exists(),'Required source '+entry['file'])
        if not path.exists():continue
        track(path);features=json.loads(path.read_text(encoding='utf-8-sig'))['features'];source_counts[entry['file']]=len(features)
        for n,f in enumerate(features):
            label=entry['file']+':'+str(n);g=f.get('geometry');p=f['properties']
            if not g:withheld['null_geometry']+=1;continue
            check(all(len(c)>=2 and all(math.isfinite(v) for v in c[:2]) and -180<=c[0]<=180 and -90<=c[1]<=90 for c in coordinates(g['coordinates'])),label+' coordinate range')
            if p.get('geometry_valid') is False:
                withheld['flagged_invalid']+=1
                if 'gypsum' in entry['file'] and (p.get('gypsum_spawn_evidence_withheld') or p.get('mineral_identity')=='anhydrite'):gypsum_qualified_invalid+=1
                continue
            geom=shape(g);check(not geom.is_empty,label+' nonempty');check(geom.is_valid,label+' topology');topology_checked+=1
            if 'spawn_probability' in p:check(p['spawn_probability'] is None,label+' no invented spawn probability')
    overlays,metadata=load_corrections()
    check(all(f['properties'].get('geometry_valid') is not False for fs in overlays.values() for f in fs),'Flagged geometry excluded from renderer')
    context=[f for f in overlays['gypsum'] if f['properties'].get('gypsum_spawn_evidence_withheld') or f['properties'].get('mineral_identity')=='anhydrite']
    check(len(context)+gypsum_qualified_invalid==1094 and gypsum_qualified_invalid==1,'1094 gypsum qualifications: 1093 context features and one withheld invalid geometry')
    check(all(f['properties']['overlay_class']=='context_only' for f in context),'Anhydrite/dissolution features context only')
    check(any('Platinum-Group Elements'.casefold() in f['properties'].get('commodity_label','').casefold() for f in overlays.get('Pt',[])),'PGE label mapped case-insensitively')
    check(not any(f['properties'].get('commodity_label')=='Beryllium' for f in overlays.get('beryl',[])),'Elemental beryllium not generic gem beryl')
    regional=[]
    for name,bounds in [('Britain',(-8,50,2,59)),('Poland',(14,49,24,55)),('Germany',(5,47,16,55)),('SE Australia',(140,-40,154,-27))]:
        count=sum(f['geometry']['type'] in ('Polygon','MultiPolygon') and f['properties']['overlay_class']!='context_only' and shape(f['geometry']).intersects(box(*bounds)) for f in overlays['C_coal'])
        regional.append({'coal_region':name,'intersecting_source_areas':count});check(count>0,name+' source coal polygons')
    legal=[f for f in overlays['gypsum'] if f['properties'].get('source_id')=='latvia_cabinet321_2012']
    check({f['properties']['name'] for f in legal}=={'Salaspils','Skaistkalne'},'Latvia legal gypsum boundaries included')
    deep=json.loads((ROOT/'metals/latvia-deep-evidence.geojson').read_text())['features'];track(ROOT/'metals/latvia-deep-evidence.geojson')
    check(all(f['geometry'] is None for f in deep),'No invented Latvian deep ore coordinates')
    check(any(f['properties']['name']=='Staicele' and f['properties']['reported_depth_m']==688 for f in deep),'Staicele historical reported depth retained')
    audit=json.loads((ROOT/'resource-coverage-audit.json').read_text());track(ROOT/'resource-coverage-audit.json')
    rows=audit['resources'];check(len(rows)==42 and {r['commodity'] for r in rows}==set(COMMODITIES)|{'Cu','Sn'},'Every resource audited exactly once')
    check(all(r['generation_ready'] is False for r in rows),'No incomplete layer represented as ready for generation')
    for r in rows:check(r['additional_feature_links']==len(overlays.get(r['commodity'],[])),r['commodity']+' overlay audit count')
    maps=[ATLAS/'maps'/(c+'.png') for c in COMMODITIES]+[ATLAS/'maps/regional-coverage-review.png',ATLAS.parent/'copper-tin-preview-2026-09-29/copper-tin-evidence-preview.png']
    for path in maps:
        check(path.exists(),str(path)+' exists')
        if path.exists():
            data=path.read_bytes();check(data[:8]==b'\x89PNG\r\n\x1a\n',path.name+' PNG signature');w,h=struct.unpack('>II',data[16:24]);check(w>=1000 and h>=600,path.name+' dimensions');track(path)
    class Links(HTMLParser):
        def handle_starttag(self,tag,attrs):
            for k,v in attrs:
                if k in ('href','src') and v and not v.startswith(('http:','https:','#','data:')):check((ATLAS/v.split('#')[0]).exists(),'Atlas local link '+v)
    Links().feed((ATLAS/'index.html').read_text(encoding='utf8'));track(ATLAS/'index.html')
    for path in [EARTH/'docs/resource-coverage-correction.md',ATLAS/'map-validation.json',ATLAS/'correction_layers.py',ATLAS/'correction_atlas.py',ATLAS/'render_maps.py',ROOT/'build_audit.py',ROOT/'resource-coverage-audit.csv']:track(path)
    preview=json.loads((ATLAS.parent/'copper-tin-preview-2026-09-29/preview-audit.json').read_text())
    for item in preview['inputs']:
        path=Path(item['file']);path=path if path.is_absolute() else EARTH/path
        check(hashlib.sha256(path.read_bytes()).hexdigest()==item['sha256'],'CuSn current input hash '+str(path))
    tests=json.loads((EARTH/'docs/research/resource-coverage-salt-tests-2026-09-29.json').read_text(encoding='utf-8-sig'))
    check(len(tests['runs'])==2,'Two isolated server provider configurations')
    for run in tests['runs']:
        check(run['completed'] and run['stoppedGracefully'] and run['siteCount']==5,'Completed five-site salt smoke test '+run['runPath'])
        check(not any(run[k] for k in ['analysisFailures','timedOutSites','worldgenErrors','serverErrors','criticalErrors']),'No smoke-test failures '+run['runPath'])
        check(len(run['saltMapSamples'])==5 and all(s['positiveCells']>0 for s in run['saltMapSamples']),'Native salt potential survived all five sites '+run['runPath'])
    result={'status':'PASS' if not failures else 'FAIL','checks':checks,'failures':failures,'source_feature_counts':source_counts,'geometries_topology_checked':topology_checked,'withheld':dict(withheld),'context_only_gypsum_features':len(context),'regional_positive_controls':regional,'resources_audited':42,'global_generation_ready':False,'limits':'Validation checks the compilation and selected controls, not global geological completeness, calibrated abundance, grade, accessibility, unique deposits or historical reconstruction.'}
    (ROOT/'validation.json').write_text(json.dumps(result,indent=2),encoding='utf8')
    (ROOT/'correction-manifest.json').write_text(json.dumps({'scope':'Integrated correction inputs and delivered maps; baseline acquisition manifests remain separate.','files':manifest},indent=2),encoding='utf8')
    print(json.dumps(result,indent=2))
    if failures:raise SystemExit(1)
if __name__=='__main__':main()
