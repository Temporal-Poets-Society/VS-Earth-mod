import json,sys,base64,math,collections
grid=json.load(open(sys.argv[1],encoding='utf-8'))
L=base64.b64decode(grid['lithology'])
N={0:'oc',1:'su',2:'vb',3:'ss',4:'pb',5:'sm',6:'sc',7:'va',8:'mt',9:'pa',10:'vi',11:'wb',12:'py',13:'pi',14:'ev',15:'nd',16:'ig'}
boxes=[l.split('|') for l in open(sys.argv[2],encoding='utf-8').read().strip().splitlines()]
for name,la0,la1,lo0,lo1,expect in boxes:
    la0,la1,lo0,lo1=map(float,(la0,la1,lo0,lo1))
    c=collections.Counter();n=0
    for z in range(360):
        lat=90-(z+.5)*.5
        if not(la0<=lat<=la1):continue
        for x in range(720):
            lon=-180+(x+.5)*.5
            if not(lo0<=lon<=lo1):continue
            v=L[z*720+x]
            if v in(0,11):continue
            c[N[v]]+=1;n+=1
    if n==0: print(name,'no land');continue
    print(f"{name:34s} expect {expect:22s} cells {n:4d}: "+', '.join(f"{k} {100*v/n:.0f}%" for k,v in c.most_common(5)))
