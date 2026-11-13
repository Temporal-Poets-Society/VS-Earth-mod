import json,csv,sys
from shapely.geometry import MultiPolygon,Polygon
W=sys.argv[1];D=sys.argv[2]
reg=json.load(open(W+'/assets/earth/config/treeregions.json',encoding='utf-8'))['regions']
cent={};names={}
for r in reg:
    g=MultiPolygon([Polygon(p[0],p[1:]) for p in r['polygons']]).buffer(0)
    c=g.representative_point(); cent[r['code']]=(c.y,c.x); names[r['code']]=r['name']
B_FORCE={'PAK','WHM','IND','NEP'}
def area(code):
    if code not in cent: return 'B'
    lat,lon=cent[code]
    if code in B_FORCE: return 'B'
    if lon< -30 or (lon>110 and lat< -10) or lon<-140: return 'C'
    if lat<=12 and -25<lon<60: return 'C'
    if lon<75 and lat>12: return 'A'
    return 'B'
out={'A':{},'B':{},'C':{}}
for row in csv.DictReader(open(D+'/lost.csv',encoding='utf-8')):
    if not row['lost_regions']: continue
    for tok in row['lost_regions'].split():
        code,n=tok.rsplit(':',1); code=code.rstrip('~')
        if code=='?': continue
        a=area(code)
        d=out[a].setdefault(row['code']+' ('+row['taxon']+')',{})
        d[code]=d.get(code,0)+int(n)
for a,d in out.items():
    with open(D+f'/area-{a}.txt','w',encoding='utf-8') as f:
        f.write('# taxon (entity key, binomial): TDWG L3 region code = number of ~96 km PHYLACINE cells in the present-natural range but NOT in the current range\n')
        codes=set()
        for k,v in sorted(d.items()):
            f.write(k+': '+', '.join(f'{c}={n}' for c,n in sorted(v.items(),key=lambda x:-x[1]))+'\n'); codes|=set(v)
        f.write('\n# region names\n'+'\n'.join(f'{c} = {names.get(c,"?")}' for c in sorted(codes))+'\n')
    print(a,len(d))
with open(D+'/all-regions.txt','w',encoding='utf-8') as f:
    for c in sorted(names): f.write(f'{c} = {names[c]} (lat {cent[c][0]:.1f}, lon {cent[c][1]:.1f})\n')
