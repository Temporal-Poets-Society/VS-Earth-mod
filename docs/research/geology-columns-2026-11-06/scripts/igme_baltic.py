import sys,json,collections
sys.path.insert(0,sys.argv[1]); sys.path.insert(0,sys.argv[2])
import shapefile, numpy as np
from shapely.geometry import shape, Point, box
from shapely.strtree import STRtree
from lcc import fwd
r=shapefile.Reader(sys.argv[3])
W,E,S_,N=20,29,53.5,60; step=0.05
x0,y0=fwd(S_,W); x1,y1=fwd(N,E); xa,ya=fwd(N,W); xb,yb=fwd(S_,E)
bb=box(min(x0,x1,xa,xb)-5e4,min(y0,y1,ya,yb)-5e4,max(x0,x1,xa,xb)+5e4,max(y0,y1,ya,yb)+5e4)
geoms=[];recs=[]
for sr in r.iterShapeRecords():
    if sr.shape.bbox and not bb.intersects(box(*sr.shape.bbox)): continue
    g=shape(sr.shape.__geo_interface__)
    if not g.is_valid: g=g.buffer(0)
    geoms.append(g); recs.append(sr.record.as_dict())
tree=STRtree(geoms)
nx=int(round((E-W)/step)); ny=int(round((N-S_)/step))
grid=np.full((ny,nx),-1,np.int32)
for j in range(ny):
    lat=N-(j+0.5)*step
    for i in range(nx):
        lon=W+(i+0.5)*step; p=Point(*fwd(lat,lon))
        for k in tree.query(p,predicate='within'):
            grid[j,i]=k; break
np.save(sys.argv[4]+'/igme_baltic_idx.npy',grid)
json.dump(recs,open(sys.argv[4]+'/igme_baltic_recs.json','w'))
print(len(recs),'polygons; cells',grid.size,'unassigned',(grid<0).sum())
# Latvia-ish box summary 21-28E 55.7-58N
c=collections.Counter()
for j in range(ny):
    lat=N-(j+0.5)*step
    if not(55.7<=lat<=58): continue
    for i in range(nx):
        lon=W+(i+0.5)*step
        if 21<=lon<=28 and grid[j,i]>=0:
            d=recs[grid[j,i]]
            if d['MARIN']!=3 and False: pass
            c[(d['AgeName'],d['Portr_Petr'],d['Portr_Pe_1'],d['Portr_Pe_2'],d['Portr_Pe_3'],d['MARIN'])]+=1
for k,v in c.most_common(25): print(v,k)
