import json,sys,base64,math,collections
grid=json.load(open(sys.argv[1],encoding='utf-8'))
rules=json.load(open(sys.argv[2],encoding='utf-8'))
L=base64.b64decode(grid['lithology']);G=base64.b64decode(grid['sediment'])
W,H=720,360
names={0:'ocean/nodata',1:'su',2:'vb',3:'ss',4:'pb',5:'sm',6:'sc',7:'va',8:'mt',9:'pa',10:'vi',11:'wb',12:'py',13:'pi',14:'ev',15:'nd',16:'ig'}
def area(z):
    lat=90-(z+.5)*.5
    return math.cos(math.radians(lat))
# v2 fill
gen=rules['generation'];R=gen['fillRadiusCells'];fillc=set(gen['fillLithology'])
r=int(math.floor(R))
offs=[]
for dz in range(-r,r+1):
    for dx in range(-r,r+1):
        d=math.sqrt(dx*dx+dz*dz)
        if d>0 and d<=R+1e-9: offs.append((dx,dz,d))
offs.sort(key=lambda o:(o[2],o[1],o[0]))
L2=bytearray(L);G2=bytearray(G);F=[False]*(W*H)
for z in range(H):
    for x in range(W):
        i=z*W+x
        if L[i] not in fillc: continue
        for dx,dz,d in offs:
            zz=z+dz
            if zz<0 or zz>=H: continue
            j=zz*W+((x+dx)%W)
            if L[j] in fillc: continue
            L2[i]=L[j];F[i]=True
            if G[i]==0:G2[i]=G[j]
            break
classes={int(k):v for k,v in rules['classes'].items()}
profiles={p['id']:p for p in rules['profiles']}
provs=rules['provinces']
def contains(ring,x,y):
    inside=False;j=len(ring)-1
    for i in range(len(ring)):
        a=ring[i];b=ring[j]
        if (a[1]>y)!=(b[1]>y) and x<(b[0]-a[0])*(y-a[1])/(b[1]-a[1])+a[0]: inside=not inside
        j=i
    return inside
def profile_of(rock,lat,lon):
    for p in provs:
        lf=p.get('lithology')
        if (lf is None or rock in lf) and contains(p['polygon'],lon,lat): return p['profile'],p['id']
    return classes.get(rock,'unknown'),None
# land statistics (original grid): land = lithology not in {0,11,16}? report separately
tot=collections.Counter();tot_all=0
for z in range(H):
    a=area(z)
    for x in range(W):
        c=L[z*W+x]
        if c==0: continue
        tot[c]+=a
landA=sum(v for c,v in tot.items() if c not in (11,))
landNoIce=sum(v for c,v in tot.items() if c not in (11,16))
print('== GLiM class share of mapped land (area-weighted, excl. water bodies wb); also excl. ice')
for c,v in sorted(tot.items(),key=lambda t:-t[1]):
    print(f"{names[c]:>4} {c:2d} cells={L.count(c):6d} share_land={100*v/landA:5.1f}%  share_land_no_ice={(100*v/landNoIce if c!=16 else float('nan')):5.1f}%  -> {classes.get(c,'(fill/none)')}")
# v2 profiles over all cells (incl filled)
prof=collections.Counter();provc=collections.Counter();filledc=collections.Counter();filledProf=collections.Counter()
regional=0;regionalA=0;vanA=0;cellsReg=0
landProf=collections.Counter()
for z in range(H):
    lat=90-(z+.5)*.5;a=area(z)
    for x in range(W):
        i=z*W+x;lon=-180+(x+.5)*.5
        rock=L2[i]
        pr,pv=profile_of(rock,lat,lon)
        has=pr not in ('unknown','covered')
        prof[pr]+=1
        if pv: provc[pv]+=1
        if F[i]:
            filledc[names[rock]]+=1
            if has: filledProf[pr]+=1
        if has: cellsReg+=1
        if L[i] not in (0,11,16,15):
            landProf[pr]+=a
print('\n== v2 cells with regional bedrock:',cellsReg,' (doc says 62,326)')
print('filled cells total:',sum(F),' filled with regional bedrock:',sum(filledProf.values()))
print('filled by borrowed class:',dict(filledc.most_common()))
print('\n== v2 profile share of mapped land (orig land cells excl wb/ig/nd, area-weighted):')
lt=sum(landProf.values())
for p,v in landProf.most_common(): print(f"  {p:24s} {100*v/lt:5.1f}%")
print('\n== province cells (cell-centre count):',dict(provc))
# su breakdown by GUM class
suG=collections.Counter()
for z in range(H):
    a=area(z)
    for x in range(W):
        i=z*W+x
        if L[i]==1: suG[G[i]]+=a
st=sum(suG.values())
print('\n== GUM class under GLiM su (area share):',[(k,round(100*v/st,1)) for k,v in suG.most_common(12)])
# lat band of su
