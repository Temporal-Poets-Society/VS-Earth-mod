# Approximate GeologyReference + ColumnV2 over real 0.1-degree terrain at several world heights.
import sys,json,base64,math,numpy as np
d=np.load(sys.argv[1]);E=d['elev'].astype(np.float64);F=d['flags'];I=d['ice'].astype(np.float64)
grid=json.load(open(sys.argv[2],encoding='utf-8'));rules=json.load(open(sys.argv[3],encoding='utf-8'))
L=np.frombuffer(base64.b64decode(grid['lithology']),np.uint8).reshape(360,720)
classes={int(k):v for k,v in rules['classes'].items()};prof={p['id']:p for p in rules['profiles']}
H_,W_=E.shape  # 1800x3600 at 0.1 deg
bed=E-np.where(I>15,I,0)
ocean=(F&1)>0
land=~ocean
lat=90-(np.arange(H_)+.5)*.1
latw=np.cos(np.radians(lat))[:,None]*np.ones((1,W_))
# lithology class per 0.1 cell (containing 0.5 cell, no fill/voronoi)
cls=np.repeat(np.repeat(L,5,axis=0),5,axis=1)
def Ycurve(h,Hgt,sea=110,H0=1000.0):
    top=Hgt-2; s=(top-sea)/math.log(1+8849/H0); dd=min(sea-12,47)/math.log(1+11000/20.0)
    return np.where(h>=0, sea+s*np.log1p(np.maximum(h,0)/H0), sea-1-dd*np.log1p(np.maximum(-h,0)/20.0))
# stencil (radius 0.3 deg = 3 cells)
st=[(0,0,3)]+[(.5*math.cos(i*math.pi/3),.5*math.sin(i*math.pi/3),2) for i in range(6)]+[(math.cos((i+.5)*math.pi/6),math.sin((i+.5)*math.pi/6),1) for i in range(12)]
R=3.0
def shifted(A,dx,dz):
    ix=int(round(dx*R));iz=int(round(dz*R))
    B=np.roll(A,-ix,axis=1)
    if iz>0: B=np.vstack([B[iz:],np.repeat(B[-1:],iz,axis=0)])
    elif iz<0: B=np.vstack([np.repeat(B[:1],-iz,axis=0),B[:iz]])
    return B
def cum(pid,T):
    p=prof[pid];c=0;out=[]
    for b in p['beds']:
        c+=max(2,b['fraction']*T);out.append(c)
    return out
res={}
for Hgt in (256,384,1024):
    T=0.6*110
    Ytop=Ycurve(bed,Hgt)
    Yd=Ycurve(np.maximum(bed,-150),Hgt)
    num=np.zeros_like(Yd);ws=0
    for dx,dz,w in st:
        num+=w*shifted(Yd,dx,dz);ws+=w
    datum=num/ws
    D=datum-Ytop   # blocks below datum of the rock top (positive: below)
    m=land&(cls>0)&(cls!=11)&(cls!=16)&(cls!=15)&(cls!=1)  # regional bedrock land (excl su)
    w=latw[m];Dm=D[m]
    def q(p): 
        o=np.argsort(Dm);cw=np.cumsum(w[o]);return Dm[o][np.searchsorted(cw,p*cw[-1])]
    print(f"\n=== world height {Hgt}: thickness scale T={T:.0f} blocks; blocks/m at 0 m={(Hgt-2-110)/math.log(1+8.849)/1000:.3f} (={1000*math.log(1+8.849)/(Hgt-112):.1f} m/block), at 1000 m: {(Hgt-112)/math.log(9.849)/2000:.3f}")
    print("  top minus datum (blocks), regional land: p5 %.0f p25 %.0f p50 %.0f p75 %.0f p95 %.0f p99 %.0f"%tuple(-q(p) for p in (.95,.75,.5,.25,.05,.01)))
    above=(w[Dm<0]).sum()/w.sum()
    print("  share of regional land whose rock top is above the datum (all bed 0 above it): %.1f%%"%(100*above))
    big=(w[Dm< -T]).sum()/w.sum()
    print("  share where bed 0 is thicker than T (top > datum+T-ish): %.1f%%"%(100*(w[(-Dm)>T*0.86]).sum()/w.sum()))
    # exposure by class profile
    tot=0;base=0;deeper=0
    per={}
    for c,pid in classes.items():
        if pid in('covered','unknown'):continue
        mc=m&(cls==c)
        if not mc.any():continue
        b=cum(pid,T);wc=latw[mc];Dc=D[mc]
        surfbed=np.searchsorted(np.array(b),Dc,side='left')  # index: D< b0 ->0 ...
        nb=len(b)
        wb=wc[surfbed>=nb].sum();w1=wc[(surfbed>=1)&(surfbed<nb)].sum()
        per[pid]=(wb/wc.sum(),w1/wc.sum(),wc.sum())
        tot+=wc.sum();base+=wb;deeper+=w1
    print("  regional land with a lower bed at the surface: %.1f%%; with BASEMENT at the surface: %.1f%%"%(100*deeper/tot,100*base/tot))
    for pid,(b_,d_,ww) in sorted(per.items(),key=lambda t:-t[1][2]):
        print(f"     {pid:22s} basement-at-surface {100*b_:5.1f}%  lower-bed {100*d_:5.1f}%")
    # mountain example: Alps box
    for name,(la0,la1,lo0,lo1) in {'Alps 45.5-47.5N 6-14E':(45.5,47.5,6,14),'Himalaya 27-30N 82-92E':(27,30,82,92),'Grand Canyon 35.8-36.5N 112.5-111.7W':(35.8,36.5,-112.5,-111.7),'N European lowland 52-54N 5-20E':(52,54,5,20)}.items():
        r0=int((90-la1)*10);r1=int((90-la0)*10);c0=int((lo0+180)*10);c1=int((lo1+180)*10)
        sub=D[r0:r1,c0:c1][land[r0:r1,c0:c1]]
        print(f"  {name}: depth below datum p5 {np.percentile(sub,5):.0f}, p50 {np.percentile(sub,50):.0f}, p95 {np.percentile(sub,95):.0f} blocks; share deeper than 0.65T (granite basement under most profiles): {100*np.mean(sub>0.65*T):.0f}%; above datum by >T: {100*np.mean(sub<-T):.0f}%")
