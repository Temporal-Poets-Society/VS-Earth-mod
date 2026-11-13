"""Retrieve GLiM/GUM open archives (GUM grid only via HTTP Range, not its 1.3 GB shapes)."""
from pathlib import Path
import io, json, struct, urllib.request, zipfile, hashlib

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'docs/research/regional-geology-2026-09-29'
OUT.mkdir(parents=True,exist_ok=True)

class RemoteZip(io.RawIOBase):
    def __init__(self,url,offset=0,size=None):
        self.url=url
        self.offset=offset
        self.size=size
        if size is None:
            with urllib.request.urlopen(urllib.request.Request(url,method='HEAD')) as r:
                self.size=int(r.headers['Content-Length'])
        self.pos=0
    def seekable(self):return True
    def readable(self):return True
    def tell(self):return self.pos
    def seek(self,n,w=0):
        self.pos=n if w==0 else self.pos+n if w==1 else self.size+n
        return self.pos
    def read(self,n=-1):
        end=self.size-1 if n<0 else min(self.size-1,self.pos+n-1)
        if end<self.pos:return b''
        req=urllib.request.Request(self.url,headers={'Range':f'bytes={self.pos+self.offset}-{end+self.offset}'})
        with urllib.request.urlopen(req,timeout=120) as r:
            if r.status!=206:raise RuntimeError('Server did not honor range; refusing full archive')
            data=r.read()
        self.pos+=len(data)
        return data

url='https://hs.pangaea.de/Maps/Boerker-etal_2018/Boerker_et_al_GUM_v1.0.zip'
outer=RemoteZip(url)
with zipfile.ZipFile(outer) as z:
    item=z.getinfo('GUM_v1.0.zip')
    if item.compress_type!=zipfile.ZIP_STORED:raise RuntimeError('Nested archive is compressed')
    outer.seek(item.header_offset)
    header=outer.read(30)
    namelen,extralen=struct.unpack_from('<HH',header,26)
    offset=item.header_offset+30+namelen+extralen
with zipfile.ZipFile(RemoteZip(url,offset,item.file_size)) as z:
    inventory=[{'name':i.filename,'size':i.file_size,'compressed':i.compress_size} for i in z.infolist()]
    (OUT/'gum-archive-inventory.json').write_text(json.dumps(inventory,indent=2))
    print(json.dumps(inventory,indent=2))
    for i in z.infolist():
        if i.filename.lower().endswith(('.asc','.txt','.pdf','.csv')):
            if i.file_size>150_000_000:continue
            target=OUT/Path(i.filename).name
            target.write_bytes(z.read(i))
            print('Fetched',target.name,target.stat().st_size,hashlib.sha256(target.read_bytes()).hexdigest())
