# As scale.py, with the undulation (+-0.3T, ~0.8 deg wavelength) and thickness variation (+-15 %, ~1.36 deg) modelled as
# smooth value noise, and the legacy layout (sea 0.431*height, LandCurve 200 m, top margin 6.25 %) for comparison.
import sys,json,base64,math,numpy as np
d=np.load(sys.argv[1]);E=d['elev'].astype(np.float64);F=d['flags'];I=d['ice'].astype(np.float64)
grid=json.load(open(sys.argv[2],encoding='utf-8'));rules=json.load(open(sys.argv[3],encoding='utf-8'))
L=np.frombuffer(base64.b64decode(grid['lithology']),np.uint8).reshape(360,720)
classes={int(k):v for k,v in rules['classes'].items()};prof={p['id']:p for p in rules['profiles']}
H_,W_=E.shape
bed=E-np.where(I>15,I,0);land=(F&1)==0
lat=90-(np.arange(H_)+.5)*.1;latw=np.cos(np.radians(lat))[:,None]*np.ones((1,W_))
cls=np.repeat(np.repeat(L,5,0),5,1)
def vnoise(wave_cells,seed):
    rng=np.random.default_rng(seed);ny=int(H_/wave_cells)+3;nx=int(W_/wave_cells)+3
    g=rng.random((ny,nx));y=np.arange(H_)/wave_cells;x=np.arange(W_)/wave_cells
    iy=np.floor(y).astype(int);ix=np.floor(x).astype(int);ty=y-iy;tx=x-ix
    ty=ty*ty*(3-2*ty);tx=tx*tx*(3-2*tx)
    a=g[iy][:,ix];b=g[iy][:,ix+1];c=g[iy+1][:,ix];e=g[iy+1][:,ix+1]
    return (a+(b-a)*tx[None,:])*(1-ty[:,None])+(c+(e-c)*tx[None,:])*ty[:,None]
und=vnoise(8,1)*2-1;var=vnoise(13.6,2)*2-1
st=[(0,0,3)]+[(.5*math.cos(i*math.pi/3),.5*math.sin(i*math.pi/3),2) for i in range(6)]+[(math.cos((i+.5)*math.pi/6),math.sin((i+.5)*math.pi/6),1) for i in range(12)]
def shifted(A,dx,dz,R=3.0):
    ix=int(round(dx*R));iz=int(round(dz*R));B=np.roll(A,-ix,axis=1)
    if iz>0:B=np.vstack([B[iz:],np.repeat(B[-1:],iz,axis=0)])
    elif iz<0:B=np.vstack([np.repeat(B[:1],-iz,axis=0),B[:iz]])
    return B
def curve(Hgt,sea,H0,top,depth):
    s=(top-sea)/math.log(1+8849/H0);dd=depth/math.log(1+11000/20.0)
    return lambda h: np.where(h>=0,sea+s*np.log1p(np.maximum(h,0)/H0),sea-1-dd*np.log1p(np.maximum(-h,0)/20.0))
m=land&(cls>0)&~np.isin(cls,[1,11,15,16])
for label,Hgt,sea,H0,top,depth in [('current default h256',256,110,1000,254,47),('current default h384',384,110,1000,382,47),('current default h1024',1024,110,1000,1022,47),('legacy h384 (tests)',384,165,200,384-24,min(165-12,round(165*.43))),('legacy h1024 (tests)',1024,441,200,1024-64,min(441-12,round(441*.43)))]:
    Y=curve(Hgt,sea,H0,top,depth);T0=max(16,.6*sea)
    Ytop=Y(bed);Yd=Y(np.maximum(bed,-150))
    num=sum(w*shifted(Yd,dx,dz) for dx,dz,w in st);datum=num/sum(w for _,_,w in st)
    datum=datum+.3*T0*und;T=T0*(1+.15*var)
    D=datum-Ytop
    tot=low=base=0
    for c,pid in classes.items():
        if pid in('covered','unknown'):continue
        mc=m&(cls==c)
        if not mc.any():continue
        fr=np.cumsum([b['fraction'] for b in prof[pid]['beds']])
        Dc=D[mc];Tc=T[mc];wc=latw[mc]
        b0=np.maximum(2,prof[pid]['beds'][0]['fraction']*Tc)
        # cumulative with max(2) per bed
        cum=np.zeros_like(Tc);lowmask=np.zeros(Tc.shape,bool)
        for i,bd in enumerate(prof[pid]['beds']):
            cum=cum+np.maximum(2,bd['fraction']*Tc)
            if i==0: lowmask=Dc>=cum
        basem=Dc>=cum
        tot+=wc.sum();low+=wc[lowmask].sum();base+=wc[basem].sum()
    above=(latw[m][D[m]<0]).sum()/latw[m].sum()
    print(f"{label:24s} T={T0:4.0f}  surface below bed 0: {100*low/tot:5.1f}%   basement at surface: {100*base/tot:4.1f}%   rock top above datum (thickened bed 0): {100*above:4.1f}%")
