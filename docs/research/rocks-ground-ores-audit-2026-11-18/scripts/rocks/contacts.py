# Contact geometry of v2 (WarpV2 + jittered-seed Voronoi over 0.5-degree cells) at several map scales.
# Measures, for contacts crossed by E-W transects, how straight they run over 1,000 blocks N-S.
import math,random
M=(1<<64)-1
def unit(seed,x,y,z,salt):
    v=(seed&M)^(((x&0xFFFFFFFF)*0x9E3779B185EBCA87)&M)^(((y&0xFFFFFFFF)*0xC2B2AE3D27D4EB4F)&M)^(((z&0xFFFFFFFF)*0x165667B19E3779F9)&M)^(salt&0xFFFFFFFF)
    v&=M;v^=v>>30;v=(v*0xbf58476d1ce4e5b9)&M;v^=v>>27;v=(v*0x94d049bb133111eb)&M;v^=v>>31
    return (v>>11)*(1.0/9007199254740992.0)
def sm(t):return t*t*(3-2*t)
def noise(x,z,seed,salt):
    ix=math.floor(x);iz=math.floor(z);tx=sm(x-ix);tz=sm(z-iz)
    a=unit(seed,ix,0,iz,salt);b=unit(seed,ix+1,0,iz,salt);c=unit(seed,ix,0,iz+1,salt);d=unit(seed,ix+1,0,iz+1,salt)
    return (a+(b-a)*tx)*(1-tz)+(c+(d-c)*tx)*tz
def warp1(x,z,seed,w):
    s=max(32,w*.6)
    dx=(.65*noise(x/s,z/s,seed,781)+.35*noise(x/(s*2.7),z/(s*2.7),seed,913))*2-1
    dz=(.65*noise(x/s,z/s,seed,1193)+.35*noise(x/(s*2.7),z/(s*2.7),seed,1583))*2-1
    return x+dx*w,z+dz*w
def warp2(x,z,seed,w,cb,lw=.9,la=.35):
    X,Z=warp1(x,z,seed,w)
    s=max(64,lw*cb);A=la*cb
    dx=noise(x/s,z/s,seed,2141)*2-1;dz=noise(x/s,z/s,seed,2683)*2-1
    dx=.7*dx+.3*(noise(x/(s*.57)+.37,z/(s*.57)+.71,seed,2909)*2-1)
    dz=.7*dz+.3*(noise(x/(s*.57)+.53,z/(s*.57)+.19,seed,3191)*2-1)
    return X+dx*A,Z+dz*A
def cell(u,v):
    ix=math.floor(u);iz=math.floor(v);best=1e18;r=None
    for dz in range(-2,3):
        for dx in range(-2,3):
            cx=ix+dx;cz=iz+dz;wx=cx%720
            sx=cx+.5+.45*(2*unit(0,wx,cz,0,6151)-1);sz=cz+.5+.45*(2*unit(0,wx,cz,0,6373)-1)
            d=(sx-u)**2+(sz-v)**2
            if d<best:best=d;r=(wx,cz)
    return r
seed=12345
for N in (200,100,10,5):
    bpd=111195/N;cb=bpd*.5
    rnd=random.Random(5)
    devs=[];segs=[]
    for trial in range(25):
        x0=rnd.uniform(0,360*bpd);z0=rnd.uniform(20*bpd,70*bpd)
        # walk E until the category (cell) changes -> a contact
        def C(x,z):
            X,Z=warp2(x,z,seed,96,cb);return cell(X/bpd*2,Z/bpd*2)
        step=max(1,cb/200)
        c0=C(x0,z0);x=x0
        while C(x,z0)==c0 and x<x0+3*cb: x+=step
        if C(x,z0)==c0: continue
        # trace that contact over 1000 blocks north: at each z find crossing near previous x by bisection-like scan
        pts=[]
        px=x
        ok=True
        for k in range(21):
            z=z0+k*50
            a=px-400;b=px+400
            # find first change from left side cell
            prev=C(a,z);found=None;xx=a
            while xx<b:
                xx+=4
                cur=C(xx,z)
                if cur!=prev: found=xx;break
            if found is None: ok=False;break
            pts.append((z,found));px=found
        if not ok or len(pts)<21: continue
        # straight-line fit
        n=len(pts);mz=sum(p[0] for p in pts)/n;mx=sum(p[1] for p in pts)/n
        szz=sum((p[0]-mz)**2 for p in pts);szx=sum((p[0]-mz)*(p[1]-mx) for p in pts)
        k_=szx/szz;res=[p[1]-(mx+k_*(p[0]-mz)) for p in pts]
        devs.append(math.sqrt(sum(r*r for r in res)/n))
    devs.sort()
    if devs: print(f"1:{N}: cell {cb:.0f} blocks; contacts traced {len(devs)}; RMS deviation from a straight line over 1000 blocks: median {devs[len(devs)//2]:.0f}, p90 {devs[int(.9*len(devs))-1]:.0f} blocks")
