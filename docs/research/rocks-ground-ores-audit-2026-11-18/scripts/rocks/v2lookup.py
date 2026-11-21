# Port of RegionalGeologyCatalog.At (v2: fill + jittered-seed Voronoi + provinces), without the seed-dependent warp.
import json,sys,base64,math,collections
M=(1<<64)-1
def unit(seed,x,y,z,salt):
    v=(seed&M) ^ (((x&0xFFFFFFFF)*0x9E3779B185EBCA87)&M) ^ (((y&0xFFFFFFFF)*0xC2B2AE3D27D4EB4F)&M) ^ (((z&0xFFFFFFFF)*0x165667B19E3779F9)&M) ^ (salt&0xFFFFFFFF)
    v&=M
    v^=v>>30; v=(v*0xbf58476d1ce4e5b9)&M; v^=v>>27; v=(v*0x94d049bb133111eb)&M; v^=v>>31
    return (v>>11)*(1.0/9007199254740992.0)
grid=json.load(open(sys.argv[1],encoding='utf-8'));rules=json.load(open(sys.argv[2],encoding='utf-8'))
L=base64.b64decode(grid['lithology'])
gen=rules['generation'];R=gen['fillRadiusCells'];fillc=set(gen['fillLithology']);J=gen['voronoiJitter']
r=int(R);offs=sorted([(dx,dz,math.hypot(dx,dz)) for dz in range(-r,r+1) for dx in range(-r,r+1) if 0<math.hypot(dx,dz)<=R+1e-9],key=lambda o:(o[2],o[1],o[0]))
L2=bytearray(L);FL=[False]*len(L)
for z in range(360):
    for x in range(720):
        i=z*720+x
        if L[i] not in fillc: continue
        for dx,dz,_ in offs:
            zz=z+dz
            if zz<0 or zz>=360: continue
            j=zz*720+(x+dx)%720
            if L[j] in fillc: continue
            L2[i]=L[j];FL[i]=True;break
N={0:'oc',1:'su',2:'vb',3:'ss',4:'pb',5:'sm',6:'sc',7:'va',8:'mt',9:'pa',10:'vi',11:'wb',12:'py',13:'pi',14:'ev',15:'nd',16:'ig'}
classes={int(k):v for k,v in rules['classes'].items()}
def contains(ring,x,y):
    inside=False;j=len(ring)-1
    for i in range(len(ring)):
        a=ring[i];b=ring[j]
        if (a[1]>y)!=(b[1]>y) and x<(b[0]-a[0])*(y-a[1])/(b[1]-a[1])+a[0]: inside=not inside
        j=i
    return inside
def at(lat,lon):
    u=(lon+180)*2;v=(90-lat)*2
    ix=math.floor(u);iz=min(359,max(0,math.floor(v)));best=1e18;cx_=ix%720;cz_=iz
    for dz in range(-2,3):
        cz=iz+dz
        if cz<0 or cz>=360: continue
        for dx in range(-2,3):
            cx=ix+dx;wx=cx%720
            sx=cx+.5+J*(2*unit(0,wx,cz,0,6151)-1);sz=cz+.5+J*(2*unit(0,wx,cz,0,6373)-1)
            d=(sx-u)**2+(sz-v)**2
            if d<best: best=d;cx_=wx;cz_=cz
    i=cz_*720+cx_;rock=L2[i]
    for p in rules['provinces']:
        lf=p.get('lithology')
        if (lf is None or rock in lf) and contains(p['polygon'],lon,lat): return N[rock],p['profile'],FL[i]
    return N[rock],classes.get(rock,'unknown'),FL[i]
for name,la0,la1,lo0,lo1 in [('Cornwall+Devon',49.95,50.75,-5.75,-3.5),('Troodos/Cyprus',34.6,35.4,32.3,34.0),('Oman ophiolite',22.5,24.5,56.0,59.0),('New Caledonia',-22.5,-20.0,164.0,167.0),('Deccan',16,22,73,80),('Bushveld',-26,-24,26,30),('Erzgebirge',50.3,50.9,12.3,14.0)]:
    c=collections.Counter();n=0;fl=0
    steps=60
    for a in range(steps):
        for b in range(steps):
            la=la0+(la1-la0)*(a+.5)/steps;lo=lo0+(lo1-lo0)*(b+.5)/steps
            g,p,f=at(la,lo);c[(g,p)]+=1;n+=1;fl+=f
    print(f"{name}: "+', '.join(f"{g}->{p} {100*k/n:.0f}%" for (g,p),k in c.most_common(6))+f"  (borrowed {100*fl/n:.0f}%; box incl. sea)")
