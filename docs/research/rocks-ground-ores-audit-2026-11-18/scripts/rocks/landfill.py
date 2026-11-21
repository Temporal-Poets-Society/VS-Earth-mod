import json,sys,base64,math,collections,numpy as np
d=np.load(sys.argv[1]);F=d['flags'];I=d['ice']
grid=json.load(open(sys.argv[2],encoding='utf-8'));rules=json.load(open(sys.argv[3],encoding='utf-8'))
L=np.frombuffer(base64.b64decode(grid['lithology']),np.uint8).reshape(360,720)
gen=rules['generation'];R=gen['fillRadiusCells'];fillc=set(gen['fillLithology'])
r=int(R);offs=sorted([(dx,dz,math.hypot(dx,dz)) for dz in range(-r,r+1) for dx in range(-r,r+1) if 0<math.hypot(dx,dz)<=R+1e-9],key=lambda o:(o[2],o[1],o[0]))
L2=L.copy();Fl=np.zeros_like(L,bool)
for z in range(360):
    for x in range(720):
        if L[z,x] not in fillc: continue
        for dx,dz,_ in offs:
            zz=z+dz
            if zz<0 or zz>=360: continue
            v=L[zz,(x+dx)%720]
            if v in fillc: continue
            L2[z,x]=v;Fl[z,x]=True;break
classes={int(k):v for k,v in rules['classes'].items()}
land=((F&1)==0)&((F&2)==0)  # not ocean, not lake
ice=(F&8)>0
cls=np.repeat(np.repeat(L,5,0),5,1);cls2=np.repeat(np.repeat(L2,5,0),5,1);fl=np.repeat(np.repeat(Fl,5,0),5,1)
lat=90-(np.arange(1800)+.5)*.1;w=np.cos(np.radians(lat))[:,None]*np.ones((1,3600))
m=land&~ice
tot=w[m].sum()
def sh(mask): return 100*w[m&mask].sum()/tot
print(f"ice-free dry land (Earth terrain flags, 0.1 deg): {tot:.0f} weight units")
print(f"  in GLiM cells with a mapped class (not 0/11/15/16): {sh((cls>0)&(cls!=11)&(cls!=15)&(cls!=16)):.1f}%")
print(f"  in GLiM ocean/lake/nodata/ice cells: {sh((cls==0)|(cls==11)|(cls==15)|(cls==16)):.1f}%  of which v2-filled: {sh(fl):.1f}%  still unmapped (vanilla strata): {sh(((cls2==0)|(cls2==11)|(cls2==15)|(cls2==16))):.2f}%")
reg2=np.vectorize(lambda c: c in classes and classes[c] not in ('covered','unknown'))(cls2)
print(f"  v2 regional bedrock (incl. fill, excl. su; ignoring provinces): {sh(reg2):.1f}%;  su (covered -> vanilla strata in 'regional' mode): {sh(cls2==1):.1f}%")
print(f"  land whose category is BORROWED from a neighbour cell: {sh(fl):.1f}% (with bedrock: {sh(fl&reg2):.1f}%)")
# biggest unmapped land areas
um=m&((cls2==0)|(cls2==11)|(cls2==15)|(cls2==16))
ys,xs=np.nonzero(um)
cnt=collections.Counter()
for y,x in zip(ys,xs): cnt[(int(90-(y+.5)*.1)//5*5,int(-180+(x+.5)*.1)//5*5)]+=1
print('  unmapped land by 5-degree box (lat,lon):',cnt.most_common(10))
