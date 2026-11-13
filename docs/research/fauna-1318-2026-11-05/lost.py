import json,sys,base64,math,csv
import numpy as np
from shapely.geometry import shape, Point, MultiPolygon, Polygon
from shapely.strtree import STRtree
W=sys.argv[1]
reg=json.load(open(W+'/assets/earth/config/treeregions.json',encoding='utf-8'))['regions']
polys=[];codes=[]
for r in reg:
    ps=[]
    for p in r['polygons']:
        ps.append(Polygon(p[0],p[1:]))
    g=MultiPolygon(ps).buffer(0)
    polys.append(g);codes.append(r['code'])
tree=STRtree(polys)
a=6378137;e2=0.0066943799901413165;e=math.sqrt(e2);k=math.cos(math.pi/6)/math.sqrt(1-e2*.25)
def center(row,col):
    x=-17367529+(col+.5)*96486.27222222222;y=7348382-(row+.5)*96514.95774647887
    lon=x/(a*k)*180/math.pi;q=2*k*y/a;phi=math.asin(max(-1,min(1,q/2)))
    for i in range(20):
        s=math.sin(phi);d=1-e2*s*s;qp=(1-e2)*(s/d-math.log((1-e*s)/(1+e*s))/(2*e));phi+=d*d/(2*math.cos(phi))*(q-qp)/(1-e2)
    return phi*180/math.pi,lon
cache={}
def region(row,col):
    if (row,col) in cache: return cache[(row,col)]
    lat,lon=center(row,col);pt=Point(lon,lat)
    best=None
    for i in tree.query(pt):
        if polys[i].contains(pt): best=codes[i];break
    if best is None:
        idx=tree.nearest(pt); 
        best=codes[idx]+'~' if polys[idx].distance(pt)<1.0 else '?'
    cache[(row,col)]=best;return best
def unpack(s): return np.unpackbits(np.frombuffer(base64.b64decode(s),dtype=np.uint8),bitorder='little')[:142*360].astype(bool)
rows=[]
sp=json.load(open(W+'/assets/earth/config/fauna-species.json',encoding='utf-8'))
items=[(r['provider']+':'+'-'.join(r['tokens']),r['taxon'],r.get('mask'),r.get('currentMask'),r.get('regions')) for r in sp['entries'] if r.get('mask') and not r.get('extinct')]
import glob,tifffile
van=json.load(open(W+'/assets/earth/config/fauna-vanilla.json',encoding='utf-8'))
cur='/'.join(W.split('/')[:3])  # unused
for v in van['entries']:
    if 'mask' in v:
        name=v['taxon'].replace(' ','_')
        cm=None
        try:
            arr=tifffile.imread(sys.argv[2]+'/'+name+'.tif'); cm=base64.b64encode(np.packbits((arr==1).ravel(),bitorder='little')).decode()
        except Exception as ex: pass
        items.append(('game:'+v['family']+'-'+v['type'],v['taxon'],v['mask'],cm,None))
out=csv.writer(open(sys.argv[3],'w',newline='',encoding='utf-8'))
out.writerow(['code','taxon','natural','current','lost_regions'])
for code,taxon,m,cm,regs in items:
    n=unpack(m); c=unpack(cm) if cm else None
    lost={}
    if c is not None:
        for idx in np.nonzero(n & ~c)[0]:
            r=region(idx//360, idx%360)
            if regs and r.rstrip('~') not in regs: continue
            lost[r]=lost.get(r,0)+1
    s=' '.join(f'{k}:{v}' for k,v in sorted(lost.items(),key=lambda x:-x[1]))
    out.writerow([code,taxon,int(n.sum()),int(c.sum()) if c is not None else '',s])
    print(code,int(n.sum()),int(c.sum()) if c is not None else '-', s[:300])
