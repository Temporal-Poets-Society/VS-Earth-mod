"""Official PP1802 global WFS/KML + GA AIMR complements. No runtime or atlas changes."""
import json,xml.etree.ElementTree as ET,re,html,hashlib,urllib.request,urllib.parse,concurrent.futures,collections
from pathlib import Path
BASE=Path(__file__).resolve().parent;RAW=BASE/'raw';OUT=BASE/'normalized';OUT.mkdir(exist_ok=True)
RAW.mkdir(exist_ok=True)
def save(p,x):p.write_text(json.dumps(x,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
def get(url,path):
    if not path.exists():
        with urllib.request.urlopen(url,timeout=90) as r:path.write_bytes(r.read())
    return json.loads(path.read_text(encoding='utf-8-sig'))

ppwfs='https://mrdata.usgs.gov/services/wfs/pp1802'
rawurls={
 'pp1802.kml':'https://mrdata.usgs.gov/pp1802/pp1802.kml',
 'pp1802-metadata.xml':'https://mrdata.usgs.gov/pp1802/metadata/pp1802.xml',
 'pp1802-wfs-capabilities.xml':ppwfs+'?service=WFS&version=1.0.0&request=GetCapabilities'}
for layer in ['points','polygons']:
 basequery=ppwfs+'?service=WFS&version=1.0.0&request=GetFeature&typeName='+layer
 rawurls[f'pp1802-{layer}.gml']=basequery
 rawurls[f'pp1802-{layer}-global.gml']=basequery+'&BBOX=-180,-90,180,90'
for filename,url in rawurls.items():
 if not (RAW/filename).exists():
  with urllib.request.urlopen(url,timeout=60) as response:(RAW/filename).write_bytes(response.read())
gmlns={'g':'http://www.opengis.net/gml','m':'http://mapserver.gis.umn.edu/mapserver'}
def wfs(layer):
    root=ET.parse(RAW/f'pp1802-{layer}-global.gml')
    result={}
    for e in root.findall('.//g:featureMember/*',gmlns):
        attrs={x.tag.split('}')[-1]:x.text for x in e if x.tag.startswith('{'+gmlns['m']+'}') and x.tag.split('}')[-1]!='geometry'}
        attrs['_wfs_point_coordinates']=e.findtext('.//g:Point/g:coordinates',namespaces=gmlns)
        result[e.attrib['fid']]=attrs
    return result
lookup={**wfs('points'),**wfs('polygons')}
tree=ET.parse(RAW/'pp1802.kml');ns={'k':tree.getroot().tag.split('}')[0][1:]}
def coords(t):return [[float(v) for v in x.split(',')[:2]] for x in t.split()]
features=[];counts=collections.Counter();issues=[]
for e in tree.findall('.//k:Placemark',ns):
    name=e.findtext('k:name',namespaces=ns);mineral=e.findtext('k:Snippet',namespaces=ns)
    desc=e.findtext('k:description',default='',namespaces=ns)
    gid=re.search(r'/html/(\d+)',desc).group(1)
    polygons=e.findall('.//k:Polygon',ns);point=e.find('.//k:Point/k:coordinates',ns)
    layer='polygons' if polygons else 'points';fid=layer+'.'+gid
    if polygons:
        parts=[]
        for poly in polygons:
            rings=[coords(poly.findtext('k:outerBoundaryIs/k:LinearRing/k:coordinates',namespaces=ns))]
            rings += [coords(x.text) for x in poly.findall('k:innerBoundaryIs/k:LinearRing/k:coordinates',ns)]
            parts.append(rings)
        geom={'type':'MultiPolygon','coordinates':parts}
    else:geom={'type':'Point','coordinates':coords(point.text)[0]}
    attrs=lookup.get(fid)
    if not attrs:raise ValueError('KML feature absent from explicit-global WFS: '+fid)
    if attrs['dep_name']!=name or attrs['mineral']!=mineral:raise ValueError('WFS/KML attribute mismatch: '+fid)
    if layer=='points':
        wfsxy=[float(x) for x in attrs['_wfs_point_coordinates'].split(',')]
        if abs(geom['coordinates'][0]-wfsxy[0])>1e-5 or abs(geom['coordinates'][1]-wfsxy[1])>1e-5:raise ValueError('Coordinate order/mismatch '+fid)
    prop={'record_id':'usgs_pp1802:'+fid,'source_id':'usgs_pp1802_2017','source_feature_id':fid,'name':name,'country':attrs['location'],'commodity_label':mineral,'deposit_style':attrs['dep_type'],'location_detail':attrs.get('loc_detail'),'evidence_class':'selected_mineral_region' if polygons else 'selected_mine_deposit_or_district','source_status':None,'economic_status':None,'depth_m':None,'early_accessibility':None,'geometry_role':'generalized_region_not_orebody_or_uniform_probability' if polygons else 'approximate_location_not_deposit_extent','source_url':attrs.get('url') or 'https://mrdata.usgs.gov/pp1802/','source_doi':'10.5066/F7GH9GQR','source_date':'2017','rights':'USGS public data; use constrained to regional-scale interpretation; preserve attribution/metadata','raw_properties':attrs}
    features.append({'type':'Feature','id':prop['record_id'],'geometry':geom,'properties':prop});counts[(layer,mineral)]+=1
assert len(features)==len(lookup),(len(features),len(lookup))
save(OUT/'pp1802-global.geojson',{'type':'FeatureCollection','features':features})
service='https://services.ga.gov.au/gis/rest/services/AustraliasIdentifiedMineralResources/MapServer'
get(service+'?f=pjson',RAW/'ga-aimr-service.json')
layercodes={30:['Sb'],31:['Al'],35:['Cu'],38:['Au'],41:['Fe'],42:['Pb','Zn'],45:['Mn'],46:['Ti'],47:['Mo'],48:['Ni'],50:['Pt'],54:['Ag'],55:['Sn'],56:['W'],57:['U'],58:['V']}
manifest=[]
def ga(layer):
    meta=get(service+f'/{layer}?f=pjson',RAW/f'ga-aimr-{layer}-meta.json')
    count=get(service+f'/{layer}/query?where=1%3D1&returnCountOnly=true&f=json',RAW/f'ga-aimr-{layer}-count.json')['count']
    data=[]
    for offset in range(0,count,2000):
        params=urllib.parse.urlencode({'where':'1=1','outFields':'*','outSR':4326,'f':'geojson','resultOffset':offset,'resultRecordCount':2000,'orderByFields':'objectid'})
        page=get(service+f'/{layer}/query?'+params,RAW/f'ga-aimr-{layer}-{offset}.geojson')
        data.extend(page['features'])
    assert len(data)==count,(layer,len(data),count)
    result=[]
    for f in data:
        p=f['properties'];codes=layercodes[layer]
        # Shared Pb/Zn layer: use each feature's stated commodity, never add the partner automatically.
        if layer==42:codes=[c for c,word in [('Pb','lead'),('Zn','zinc')] if word in str(p.get('commodity','')).lower()]
        r={'record_id':f'ga_aimr_2026:{layer}:{p["objectid"]}','source_id':'ga_aimr_2026','source_feature_id':str(p['objectid']),'source_layer':layer,'name':p.get('entity_id'),'country':'Australia','commodities':codes,'commodity_label':p.get('commodity'),'source_status':p.get('currentstatus'),'deposit_style':p.get('mineraldeposittype'),'evidence_class':'identified_deposit_or_operating_mine','economic_status':None,'depth_m':None,'early_accessibility':None,'source_url':service+f'/{layer}','source_reference':p.get('source'),'rights':'CC BY 4.0; Commonwealth of Australia (Geoscience Australia) 2026','raw_properties':p}
        result.append({'type':'Feature','id':r['record_id'],'geometry':f['geometry'],'properties':r})
    return result,{'layer':layer,'name':meta['name'],'count':count,'url':service+f'/{layer}','rights':'CC BY 4.0','counts_verified':True}
with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:results=list(pool.map(ga,layercodes))
gafeatures=[f for fs,m in results for f in fs];manifest=[m for fs,m in results]
save(OUT/'ga-aimr-metals.geojson',{'type':'FeatureCollection','features':gafeatures})
summary={'pp1802':{'points':sum(k[0]=='points' for k in []),'features':len(features),'geometry_counts':dict(collections.Counter(f['geometry']['type'] for f in features)),'commodity_counts':{l+' / '+m:n for (l,m),n in counts.items()},'default_bbox_trap':{'default_points':len(ET.parse(RAW/'pp1802-points.gml').findall('.//g:featureMember',gmlns)),'default_polygons':len(ET.parse(RAW/'pp1802-polygons.gml').findall('.//g:featureMember',gmlns)),'correction':'explicit BBOX=-180,-90,180,90 mandatory; WFS polygons omit geometry, full KML supplies it'},'wfs_kml_ids_attributes_coordinates_matched':True},'ga_aimr':{'features':len(gafeatures),'layers':manifest},'raw_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in RAW.iterdir() if p.is_file()}}
summary['pp1802'].pop('points')
save(BASE/'acquisition-validation.json',summary)
print(json.dumps({'pp1802_features':len(features),'pp1802_geometry_counts':summary['pp1802']['geometry_counts'],'ga_features':len(gafeatures),'ga_layers':len(manifest),'pp1802_commodities':sorted({p['properties']['commodity_label'] for p in features})},indent=2))
