import json,sys,base64,math,collections
grid=json.load(open(sys.argv[1],encoding='utf-8'));rules=json.load(open(sys.argv[2],encoding='utf-8'))
L=base64.b64decode(grid['lithology']);G=base64.b64decode(grid['sediment'])
W,H=720,360
N={0:'oc',1:'su',2:'vb',3:'ss',4:'pb',5:'sm',6:'sc',7:'va',8:'mt',9:'pa',10:'vi',11:'wb',12:'py',13:'pi',14:'ev',15:'nd',16:'ig'}
classes={int(k):v for k,v in rules['classes'].items()}
def cell(lat,lon):
    return min(359,max(0,int(math.floor((90-lat)*2)))),int(math.floor((lon+180)*2))%720
pts=[l.split('|') for l in open(sys.argv[3],encoding='utf-8').read().strip().splitlines() if l.strip() and not l.startswith('#')]
for grp,name,lat,lon in pts:
    lat=float(lat);lon=float(lon);z,x=cell(lat,lon)
    c=L[z*W+x]
    nb=collections.Counter()
    for dz in (-1,0,1):
        for dx in (-1,0,1):
            zz=z+dz
            if 0<=zz<H: nb[N[L[zz*W+(x+dx)%W]]]+=1
    nb2=collections.Counter()
    for dz in range(-2,3):
        for dx in range(-2,3):
            zz=z+dz
            if 0<=zz<H: nb2[N[L[zz*W+(x+dx)%W]]]+=1
    print(f"{grp:10s} {name:28s} {lat:7.2f} {lon:8.2f}  cell={N[c]:2s}->{classes.get(c,'-'):22s} 3x3={dict(nb.most_common())}  5x5={dict(nb2.most_common(4))}")
