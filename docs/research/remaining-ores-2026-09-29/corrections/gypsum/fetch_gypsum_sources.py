"""Reproduce public gypsum-source retrieval, preserving responses and request URLs.

Python stdlib only. No authentication, no mine-operation/economic-status filter.
By default downloads only missing cache files. --geode retrieves complete explicit
gypsum/anhydrite-bearing GEODE 1:50k units (not all evaporites).
"""
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.parse import urlencode
from concurrent.futures import ThreadPoolExecutor
import argparse, gzip, hashlib, json, time

ROOT=Path(__file__).resolve().parent
RAW=ROOT/'raw'
RAW.mkdir(exist_ok=True)
IGME='https://mapas.igme.es/gis/rest/services/'
WFS='https://geoserver.lvgmc.lv/geoserver/mr/ows?'
POL='https://geologia.pgi.gov.pl/srv/rest/services/midas/MapServer/'
GYPSUM="UPPER(DESC_UNIT) LIKE '%YES%' OR UPPER(DESC_UNIT) LIKE '%ANHIDR%'"
SOURCES={
 'bgs-minerals-service.json':'https://map.bgs.ac.uk/arcgis/rest/services/GeoIndex_Onshore/minerals_wms/MapServer?f=pjson',
 'bgs-minerals-layers.json':'https://map.bgs.ac.uk/arcgis/rest/services/GeoIndex_Onshore/minerals_wms/MapServer/layers?f=pjson',
 'igme-industry-service.json':IGME+'BasesDatos/IGME_BDMIN_Explotaciones/MapServer?f=pjson',
 'igme-industry-layer.json':IGME+'BasesDatos/IGME_BDMIN_Explotaciones/MapServer/0?f=pjson',
 'igme-geological-services.json':IGME+'Cartografia_Geologica?f=pjson',
 'spain-geode-layers.json':IGME+'Cartografia_Geologica/IGME_Geode_50/MapServer/layers?f=pjson',
 'spain-lithology1m-layers.json':IGME+'Cartografia_Geologica/IGME_Litologias_1M/MapServer/layers?f=pjson',
 'latvia-data-catalog.json':'https://data.gov.lv/dati/api/3/action/package_show?id=valsts-nozmes-dergo-izrakteu-atradnes-wms1',
 'latvia-state-deposits-law.html':'https://likumi.lv/ta/id/247498-noteikumi-par-valsts-nozimes-derigo-izraktenu-atradnem',
 'latvia-dpps.xml':'https://geo-dpps.viss.gov.lv/api/DPPSPackage/client/Valsts_noz_235_t1kDoc/1576b162-8313-4073-9bce-b7d876447673',
 'latvia-wfs-capabilities.xml':WFS+'service=WFS&request=GetCapabilities&version=2.0.0',
 'latvia-all-wfs-capabilities.xml':'https://geoserver.lvgmc.lv/geoserver/ows?service=WFS&request=GetCapabilities&version=2.0.0',
 'latvia-occurrence-schema.xsd':WFS+'service=WFS&request=DescribeFeatureType&version=2.0.0&typeNames=mr:mrMineralOccurrence',
 'poland-midas-layers.json':POL+'layers?f=pjson',
 'france-idf-catalog.json':'https://www.data.gouv.fr/api/1/datasets/ressources-geologiques-dile-de-france/',
 'france-idf-resources.zip':'https://atom.geo-ide.developpement-durable.gouv.fr/atomArchive/GetResource?id=408f119c-8d68-4025-9d4d-8f7e345b64cd&dataType=dataset',
 'germany-lgrb-wfs-capabilities.xml':'https://services.lgrb-bw.de/m4eu/services/m4eu?REQUEST=GetCapabilities&SERVICE=WFS&VERSION=2.0.0',
 'germany-lgrb-portal.html':'https://geoportal.lgrb-bw.de/',
 'germany-lgrb-geology-catalog.html':'https://geoportal.lgrb-bw.de/produktsuche/thema/geologie',
 'germany-lgrb-min4eu-catalog.html':'https://data.gov.de/suche/daten/min4eu-lgrb-bw-mining-sites-harmonized-dataset',
 'germany-gkbw-catalog.html':'https://geoportal.lgrb-bw.de/produkt/dgkbw',
 'germany-gk50-catalog.html':'https://geoportal.lgrb-bw.de/produkt/dgk50',
 'germany-gkbw-gpkg.zip':'https://media.lgrb-bw.de/geoportal/data/dGKBW_gpkg.zip',
 'germany-min4eu-occurrence-sample.xml':'https://services.lgrb-bw.de/m4eu/services/m4eu?service=WFS&request=GetFeature&version=2.0.0&typeNames=mr-m4eu:MineralOccurrence_Extension&count=5',
 'germany-min4eu-occurrences.xml':'https://services.lgrb-bw.de/m4eu/services/m4eu?service=WFS&request=GetFeature&version=2.0.0&typeNames=mr-m4eu:MineralOccurrence_Extension',
 'germany-min4eu-commodity.xml':'https://services.lgrb-bw.de/m4eu/services/m4eu?service=WFS&request=GetFeature&version=2.0.0&typeNames=mr-m4eu:Commodity_Extension',
 'germany-min4eu-mappedfeatures.xml':'https://services.lgrb-bw.de/m4eu/services/m4eu?service=WFS&request=GetFeature&version=2.0.0&typeNames=ge:MappedFeature',
 'bgs-gypsum-factsheet-page.html':'https://www.bgs.ac.uk/mineralsuk/planning/mineral-planning-factsheets/',
 'bgs-hastings-memoir.html':'https://webapps.bgs.ac.uk/Memoirs/docs/B01704.html',
 'bgs-penrith-memoir.html':'https://webapps.bgs.ac.uk/Memoirs/docs/B01503.html',
 'bgs-brough-memoir.html':'https://webapps.bgs.ac.uk/Memoirs/docs/B01507.html',
 'bgs-melton-memoir.html':'https://webapps.bgs.ac.uk/memoirs/docs/B06079.html',
 'igme-data-license.pdf':'https://info.igme.es/ISE/doc/LICENCIA_USO_IGME.pdf',
 'bgs-digital-licensing.html':'https://www.bgs.ac.uk/mineralsuk/exploration-and-maps/digital-licensing/',
 'poland-gypsum-survey.html':'https://www.pgi.gov.pl/surowce/skalne-i-inne/gipsy-anhydryty.html',
 'poland-reuse-notice.html':'https://www.pgi.gov.pl/aktualnosci/display/11489-nowe-dane-dotyczace-zloz-dostepne-w-internecie.html',
}
for n,t in [('occurrence','mr:mrMineralOccurrence'),('occurrence-polygons','mr:MR.MineralOccurrence1'),('occurrence-points','mr:mrMineralOccurrenceP'),('mines','mr:MR.Mine'),('commodity','mr:Commodity')]:
 SOURCES['latvia-'+n+('-native' if n!='commodity' else '')+'.geojson']=WFS+urlencode(dict(service='WFS',request='GetFeature',version='2.0.0',typeNames=t,outputFormat='application/json'))
for name,endpoint,where in [
 ('spain',IGME+'BasesDatos/IGME_BDMIN_Explotaciones/MapServer/0',"UPPER(Sustancia) LIKE '%YES%' OR UPPER(Sustancia) LIKE '%ANHIDR%'"),
 ('spain-lithology1m',IGME+'Cartografia_Geologica/IGME_Litologias_1M/MapServer/0',"UPPER(Litologia) LIKE '%YES%' OR UPPER(Litologia) LIKE '%ANHIDR%'"),
 ('poland',POL+'0',"UPPER(KOPALINA_ALL) LIKE '%GIPS%' OR UPPER(KOPALINA_ALL) LIKE '%ANHYDR%'"),
 ('poland-archived',POL+'3',"UPPER(KOPALINA) LIKE '%GIPS%' OR UPPER(KOPALINA) LIKE '%ANHYDR%'"),
 ('bgs','https://map.bgs.ac.uk/arcgis/rest/services/GeoIndex_Onshore/minerals_wms/MapServer/28','1=1')]:
 for suffix,extras in [('native.json',dict(f='json')),('wgs84.geojson',dict(f='geojson',outSR=4326))]:
  SOURCES[name+'-gypsum-'+suffix]=endpoint+'/query?'+urlencode(dict(where=where,outFields='*',returnGeometry='true',**extras))

def fetch(name,url):
 path=RAW/name
 if not path.exists():
  for attempt in range(3):
   try:
    with urlopen(Request(url,headers={'User-Agent':'Earth-geological-evidence-research/1.0'}),timeout=90) as r: data=r.read()
    if name.endswith('.gz'):data=gzip.compress(data,mtime=0)
    path.write_bytes(data)
    break
   except Exception:
    if attempt==2:raise
    time.sleep(2)
 data=path.read_bytes()
 return {'file':'raw/'+name,'url':url,'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data),'retrieved_utc_date':'2026-09-29'}

def geode():
 endpoint=IGME+'Cartografia_Geologica/IGME_Geode_50/MapServer/8/query?'
 idurl=endpoint+urlencode(dict(where=GYPSUM,returnIdsOnly='true',f='json'))
 rows=[fetch('spain-geode-gypsum-ids.json',idurl)]
 ids=sorted(json.loads((RAW/'spain-geode-gypsum-ids.json').read_text())['objectIds'])
 tasks=[]
 for offset in range(0,len(ids),500):
  part=ids[offset:offset+500]
  # Bounded ID ranges keep URLs below the service's request-length limit.
  where=f'({GYPSUM}) AND OBJECTID >= {part[0]} AND OBJECTID <= {part[-1]}'
  tasks.append((f'spain-geode-gypsum-page-{offset//500:03}.json.gz',endpoint+urlencode(dict(where=where,outFields='*',returnGeometry='true',f='json'))))
 def work(t):
  row=fetch(*t)
  j=json.loads(gzip.decompress((ROOT/row['file']).read_bytes()))
  assert not j.get('error') and not j.get('exceededTransferLimit'),(t[0],j.get('error'))
  print(t[0],len(j['features']),flush=True)
  return row
 with ThreadPoolExecutor(max_workers=3) as pool:rows.extend(pool.map(work,tasks))
 seen=[]
 for r in rows[1:]:seen += [f['attributes']['OBJECTID'] for f in json.loads(gzip.decompress((ROOT/r['file']).read_bytes()))['features']]
 assert sorted(seen)==ids,('Missing/duplicate GEODE features',len(seen),len(ids))
 print('GEODE complete',len(ids),flush=True)
 return rows

if __name__=='__main__':
 parser=argparse.ArgumentParser();parser.add_argument('--geode',action='store_true');args=parser.parse_args()
 rows=[fetch(n,u) for n,u in SOURCES.items()]
 if args.geode:rows+=geode()
 path=ROOT/'download-manifest.json'
 prior=json.loads(path.read_text()) if path.exists() else []
 byfile={r['file']:r for r in prior+rows}
 path.write_text(json.dumps(sorted(byfile.values(),key=lambda r:r['file']),ensure_ascii=False,indent=2),encoding='utf-8')
 print('Manifest',len(byfile),flush=True)
