import sys,json,collections,numpy as np,tifffile
sys.path.insert(0,sys.argv[2]); from lks92 import fwd
D=sys.argv[1]
W=["Acrisols","Albeluvisols","Alisols","Andosols","Arenosols","Calcisols","Cambisols","Chernozems","Cryosols","Durisols","Ferralsols","Fluvisols","Gleysols","Gypsisols","Histosols","Kastanozems","Leptosols","Lixisols","Luvisols","Nitisols","Phaeozems","Planosols","Plinthosols","Podzols","Regosols","Solonchaks","Solonetz","Stagnosols","Umbrisols","Vertisols"]
# proposal: natural (pre-fertiliser) topsoil fertility by WRB reference soil group
BASE={'high':["Chernozems","Kastanozems","Phaeozems","Vertisols"],
      'medium':["Luvisols","Cambisols","Nitisols","Fluvisols","Umbrisols","Andosols","Lixisols","Calcisols"],
      'low':["Albeluvisols","Gleysols","Stagnosols","Planosols","Regosols","Alisols","Solonetz"],
      'verylow':["Podzols","Arenosols","Leptosols","Ferralsols","Acrisols","Plinthosols","Histosols","Cryosols","Gypsisols","Durisols","Solonchaks"]}
ORDER=['verylow','low','medium','high']; G2C={g:c for c,gs in BASE.items() for g in gs}
wrb=tifffile.imread(f'{D}/wrb.tif'); clay=tifffile.imread(f'{D}/clay_15-30cm.tif').astype(float)/10; ph=tifffile.imread(f'{D}/phh2o_15-30cm.tif').astype(float)/10
lv=tifffile.imread(sys.argv[3])[...,1]  # Latvia mask from the Quaternary grid
cnt=collections.Counter(); fc=collections.Counter()
for j in range(wrb.shape[0]):
    lat=60-(j+0.5)*0.01
    for i in range(0,wrb.shape[1]):
        lon=20+(i+0.5)*0.01
        if not(55.6<lat<58.1 and 20.9<lon<28.3): continue
        x,y=fwd(lat,lon); jj=int((6439000-y)/250); ii=int((x-312750)/250)
        if not(0<=jj<lv.shape[0] and 0<=ii<lv.shape[1]) or lv[jj,ii]==-32768: continue
        w=wrb[j,i]
        if w>=len(W) or clay[j,i]<=0: continue
        g=W[w]; cnt[g]+=1; c=G2C.get(g,'low'); k=ORDER.index(c)
        if clay[j,i]>=25 and ph[j,i]>=6.0 and k<2: k+=1   # base-rich clay loam: one class up (Zemgale)
        fc[ORDER[k]]+=1
n=sum(cnt.values()); print(f"Latvia, {n} cells of 0.01 deg: WRB most probable groups")
for g,v in cnt.most_common(12): print(f"  {g:13s} {v/n*100:5.1f}%  -> {G2C.get(g)}")
print("Proposed natural fertility classes:", {k:f"{fc[k]/n*100:.0f}%" for k in ORDER})
