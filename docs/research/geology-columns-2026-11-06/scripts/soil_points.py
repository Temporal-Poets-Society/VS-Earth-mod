import sys,json,numpy as np,tifffile
D=sys.argv[1]; S=sys.argv[2]
deps=['0-5cm','5-15cm','15-30cm','30-60cm','60-100cm','100-200cm']
L={p:{d:tifffile.imread(f'{D}/{p}_{d}.tif').astype(float) for d in deps} for p in ['clay','sand','silt','cfvo']}
def px(lat,lon): return int((60-lat)/0.01), int((lon-20)/0.01)
def tex(c,s,si):
    c,s,si=c/10,s/10,si/10
    if c>=40: return 'clay'
    if c>=27 and s<=45: return 'clay loam' if s>20 else 'silty clay loam'
    if c>=20 and s>45: return 'sandy clay loam'
    if s>=85: return 'sand'
    if s>=70: return 'loamy sand'
    if s>=52 or (c<7 and si<50): return 'sandy loam'
    if si>=50: return 'silt loam'
    return 'loam'
pts=json.load(open(f'{S}/scripts/points.json'))
igme=np.load(f'{S}/igme5000/igme_baltic_idx.npy'); recs=json.load(open(f'{S}/igme5000/igme_baltic_recs.json'))
import numpy as _
b=_.loadtxt(f'{S}/crust1/x/crust1.bnds').reshape(180,360,9)
for name,lat,lon in pts:
    j,i=px(lat,lon)
    row=[]
    for d in deps:
        w=[L[p][d][j-4:j+5,i-4:i+5] for p in ['clay','sand','silt','cfvo']]
        m=(w[0]+w[1]+w[2])>900
        if not m.any(): row.append(f"{d}:nodata"); continue
        c,s,si,cf=(x[m].mean() for x in w)
        row.append(f"{d}:{tex(c,s,si)}({c/10:.0f}/{s/10:.0f}/{cf/10:.0f}%gr)")
    g=recs[igme[int((60-lat)/0.05),int((lon-20)/0.05)]]
    cb=b[int(89.5-(np.floor(lat)+0.5)+0.5) if False else int(round(89.5-(np.floor(lat)+0.5))), int(round(np.floor(lon)+0.5+179.5))]
    print(f"## {name} ({lat},{lon}) subcrop: {g['AgeName']} {g['Portr_Petr']}/{g['Portr_Pe_1']}/{g['Portr_Pe_2']}; CRUST1 sediment {round((cb[2]-cb[5])*1000)} m")
    print("   "+"; ".join(row))
