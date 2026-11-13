import sys,json,numpy as np,tifffile
D=sys.argv[1]; S=sys.argv[2]
W=["Acrisols","Albeluvisols","Alisols","Andosols","Arenosols","Calcisols","Cambisols","Chernozems","Cryosols","Durisols","Ferralsols","Fluvisols","Gleysols","Gypsisols","Histosols","Kastanozems","Leptosols","Lixisols","Luvisols","Nitisols","Phaeozems","Planosols","Plinthosols","Podzols","Regosols","Solonchaks","Solonetz","Stagnosols","Umbrisols","Vertisols"]
def fert(soc,n,ph,cec,clay):
    # proposal: natural (pre-fertiliser) fertility 0..1 from nutrient holding, organic matter and pH
    f=0.45*min(1,cec/30)+0.35*min(1,soc/30)+0.20*min(1,clay/30)
    if ph<5.0: f*=0.6
    elif ph<5.5: f*=0.8
    elif ph>8.3: f*=0.7
    return f
def cls(f): return 'verylow' if f<0.25 else 'low' if f<0.45 else 'medium' if f<0.65 else 'high'
def stats(get):
    soc=get('soc')/10; n=get('nitrogen')/100; ph=get('phh2o')/10; cec=get('cec')/10; clay=get('clay')/10
    f=fert(soc,n,ph,cec,clay); return f"SOC {soc:4.1f} g/kg, N {n:4.2f} g/kg, pH {ph:3.1f}, CEC {cec:4.1f} cmol/kg, clay {clay:2.0f}% -> index {f:.2f} {cls(f)}"
print("Baltic sites, subsoil-top 15-30 cm (SoilGrids 2.0 means; WRB most probable group):")
L={p:tifffile.imread(f'{D}/{p}_15-30cm.tif').astype(float) for p in ['soc','nitrogen','phh2o','cec','clay','sand','silt']}
wrb=tifffile.imread(f'{D}/wrb.tif')
for name,lat,lon in json.load(open(f'{S}/scripts/points.json')):
    j,i=int((60-lat)/0.01),int((lon-20)/0.01); sl=(slice(j-4,j+5),slice(i-4,i+5))
    m=(L['clay'][sl]+L['sand'][sl]+L['silt'][sl])>900
    if not m.any(): print(name,'nodata'); continue
    g=lambda p:L[p][sl][m].mean()
    wv=wrb[sl][m]; wv=wv[wv<len(W)]; grp=W[np.bincount(wv).argmax()] if len(wv) else '-'
    print(f"  {name:24s} {grp:12s} {stats(g)}")
print("References outside the Baltic (0.2 deg clips):")
for n in ['poltava','kursk','ardennes','brittany','podzolfin','sahara']:
    A={p:tifffile.imread(f'{D}/ref/{n}_{p}_15.tif').astype(float) for p in ['soc','nitrogen','phh2o','cec','clay','sand']}
    m=A['clay']>0
    if not m.any(): print(' ',n,'nodata'); continue
    wv=tifffile.imread(f'{D}/ref/{n}_wrb.tif')[m]; wv=wv[wv<len(W)]
    print(f"  {n:24s} {W[np.bincount(wv).argmax()] if len(wv) else '-':12s} {stats(lambda p:A[p][m].mean())}")
