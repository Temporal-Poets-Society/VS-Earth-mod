"""Acquire primary research inputs; public downloads only, no game asset writes."""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from urllib.request import Request, urlopen
from urllib.parse import urlencode, urljoin
import datetime, hashlib, json, re

ROOT=Path(__file__).resolve().parent
RAW=ROOT/'raw'; RAW.mkdir(parents=True,exist_ok=True)
GA='https://services.ga.gov.au/gis/rest/services/AustraliasIdentifiedMineralResources/MapServer'

def fetch(item):
    name,url=item; url=url.replace(' ','%20'); p=RAW/name
    rec={'file':str(p.relative_to(ROOT)), 'url':url, 'retrieved_utc':datetime.datetime.now(datetime.timezone.utc).isoformat()}
    try:
        if not p.exists():
            with urlopen(Request(url,headers={'User-Agent':'Mozilla/5.0 Earth geological research'}),timeout=45) as r:
                data=r.read();rec['final_url']=r.url
            if name.endswith('.zip') and not data.startswith(b'PK'): raise ValueError('Expected ZIP')
            if name.endswith('.pdf') and not data.startswith(b'%PDF'): raise ValueError('Expected PDF')
            p.write_bytes(data)
        data=p.read_bytes();rec.update(status='acquired',bytes=len(data),sha256=hashlib.sha256(data).hexdigest())
    except Exception as e:rec.update(status='failed',error=str(e))
    print(json.dumps(rec),flush=True)
    return rec

def main():
    todo=[('gemdata.zip','https://www.paivilujala.com/uploads/4/7/2/1/47215405/gemdata.zip'),
      ('gemdata-page.html','https://www.paivilujala.com/gemdata.html'),
      ('diadata-page.html','https://www.prio.org/data/10'),
      ('afghanistan-page.html','https://pubs.usgs.gov/of/2002/0110/'),
      ('mcs2026.pdf','https://pubs.usgs.gov/periodicals/mcs2026/mcs2026.pdf'),
      ('ga-service.json',GA+'?f=pjson')]
    for layer in [36,37,39,49,51]:
        todo.append((f'ga-layer-{layer}.json',f'{GA}/{layer}?f=pjson'))
        todo.append((f'ga-layer-{layer}-points.geojson',f'{GA}/{layer}/query?'+urlencode({'where':'1=1','outFields':'*','outSR':4326,'f':'geojson'})))
        todo.append((f'ga-layer-{layer}-count.json',f'{GA}/{layer}/query?'+urlencode({'where':'1=1','returnCountOnly':'true','f':'json'})))
    with ThreadPoolExecutor(max_workers=4) as pool:results=list(pool.map(fetch,todo))
    more=[]
    for page in ['diadata','afghanistan']:
        p=RAW/(page+'-page.html')
        if not p.exists():continue
        base=next(u for n,u in todo if n==page+'-page.html')
        for link in re.findall(r'href=["\x27]([^"\x27]+)["\x27]',p.read_text(errors='replace'),re.I):
            link=link.replace('&amp;','&')
            if re.search(r'\.(zip|xls|pdf|txt)(\?|$)',link,re.I):
                name=page+'-'+link.split('?')[0].split('/')[-1].replace('%20','_')
                more.append((name,urljoin(base,link)))
    with ThreadPoolExecutor(max_workers=4) as pool:results+=list(pool.map(fetch,dict(more).items()))
    (ROOT/'acquisition.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
if __name__=='__main__':main()
