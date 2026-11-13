"""Public primary coal GIS acquisition; archived responses and exact URLs."""
from pathlib import Path
from urllib.request import Request, urlopen
import concurrent.futures,datetime,hashlib,json,sys,ssl
try:
 import certifi
 TLS_CONTEXT=ssl.create_default_context(cafile=certifi.where())
except ImportError:
 TLS_CONTEXT=ssl.create_default_context()
ROOT=Path(__file__).resolve().parent
RAW=ROOT/'raw';RAW.mkdir(exist_ok=True)
MANIFEST=ROOT/'acquisition-manifest.json'

def fetch(item):
 name,url=item;p=RAW/name
 row={'file':'raw/'+name,'url':url,'retrieved_utc':datetime.datetime.now(datetime.timezone.utc).isoformat()}
 try:
  if p.exists(): row['status']='already_present'
  else:
   with urlopen(Request(url,headers={'User-Agent':'Mozilla/5.0 (public geological research)'}),timeout=90,context=TLS_CONTEXT) as response:
    body=response.read();row.update(final_url=response.url,content_type=response.headers.get('Content-Type'))
   if name.endswith('.zip') and not body.startswith(b'PK'):raise ValueError('not a ZIP response')
   p.write_bytes(body);row['status']='acquired'
  body=p.read_bytes();row.update(bytes=len(body),sha256=hashlib.sha256(body).hexdigest())
 except Exception as ex:row.update(status='failed',error=str(ex))
 print(json.dumps(row,ensure_ascii=True),flush=True)
 return row

def batch(items):
 old=json.loads(MANIFEST.read_text()) if MANIFEST.exists() else []
 with concurrent.futures.ThreadPoolExecutor(max_workers=5) as pool:result=list(pool.map(fetch,items.items()))
 old=[r for r in old if r['file'] not in {x['file'] for x in result}]
 MANIFEST.write_text(json.dumps(old+result,indent=2),encoding='utf-8')
 return result

if __name__=='__main__':batch(json.loads(sys.argv[1]))
