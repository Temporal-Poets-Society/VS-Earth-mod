import json,sys,base64,collections
g=json.load(open(sys.argv[1],encoding='utf-8'));G=base64.b64decode(g['sediment'])
for code in map(int,sys.argv[2].split(',')):
    pts=[(90-(i//720+.5)*.5,-180+(i%720+.5)*.5) for i,v in enumerate(G) if v==code]
    c=collections.Counter((int(la//5*5),int(lo//10*10)) for la,lo in pts)
    print(code,len(pts),c.most_common(8))
