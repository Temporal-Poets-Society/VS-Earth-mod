"""Retrieve primary USGS discovery pages and linked public research archives."""
from pathlib import Path
from urllib.parse import urljoin
import concurrent.futures, datetime, hashlib, json, re, urllib.request
ROOT=Path(__file__).resolve().parent
RAW=ROOT/'raw';RAW.mkdir(exist_ok=True)
MANIFEST=ROOT/'acquisition-manifest.json'
PAGES={
 'phosphate':'https://mrdata.usgs.gov/phosphate/',
 'sedexmvt':'https://mrdata.usgs.gov/sedexmvt/',
 'laterite':'https://mrdata.usgs.gov/laterite/',
 'nicrpge':'https://mrdata.usgs.gov/nicrpge/',
 'podchrome':'https://mrdata.usgs.gov/podchrome/',
 'major-deposits':'https://mrdata.usgs.gov/major-deposits/',
 'pp1802':'https://mrdata.usgs.gov/pp1802/',
 'vms':'https://mrdata.usgs.gov/vms/',
 'potash':'https://pubs.usgs.gov/sir/2010/5090/s/',
}

def fetch(name,url):
 p=RAW/name;r={'file':'raw/'+name,'url':url,'retrieved_utc':datetime.datetime.now(datetime.timezone.utc).isoformat()}
 try:
  if p.exists():r['status']='already_present'
  else:
   with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'Mozilla/5.0 (Earth mod public geological research)'}),timeout=60) as f:
    b=f.read();r.update(final_url=f.url,content_type=f.headers.get('Content-Type'),last_modified=f.headers.get('Last-Modified'))
   if name.endswith('.zip') and not b.startswith(b'PK'):raise ValueError('Response is not a ZIP archive')
   p.write_bytes(b);r['status']='acquired'
  b=p.read_bytes();r.update(bytes=len(b),sha256=hashlib.sha256(b).hexdigest())
 except Exception as e:r.update(status='failed',error=str(e))
 print(json.dumps(r),flush=True)
 return r

def main():
 with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
  records=list(pool.map(lambda item:fetch(item[0]+'-page.html',item[1]),PAGES.items()))
 todo={}
 for code,url in PAGES.items():
  p=RAW/(code+'-page.html')
  if not p.exists():continue
  html=p.read_text(encoding='utf-8',errors='replace')
  for link in re.findall(r'href=["\x27]([^"\x27]+)["\x27]',html,re.I):
   link=link.replace('&amp;','&')
   if link.lower().endswith('-csv.zip') or code=='potash' and link=='PotashGIS.zip':
    todo[code+'-'+link.split('/')[-1]]=urljoin(url,link)
   elif '/metadata/' in link and link.endswith('.xml'):
    todo[code+'-metadata.xml']=urljoin(url,link)
 with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
  records+=list(pool.map(lambda item:fetch(*item),todo.items()))
 MANIFEST.write_text(json.dumps(records,indent=2),encoding='utf-8')

if __name__=='__main__':main()
