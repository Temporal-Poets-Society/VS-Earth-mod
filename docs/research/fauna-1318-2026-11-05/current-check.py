import json,sys,base64,math
import numpy as np, tifffile
sys.path.insert(0,sys.argv[3]); 
exec(open(sys.argv[3]+'/lost.py').read().split('rows=[]')[0].replace("W=sys.argv[1]","W=sys.argv[1]"))
def unpack(s): return np.unpackbits(np.frombuffer(base64.b64decode(s),dtype=np.uint8),bitorder='little')[:142*360].astype(bool)
def inbox(lat,lon,b): return b[1]<=lat<=b[3] and (b[0]<=lon<=b[2] if b[0]<=b[2] else (lon>=b[0] or lon<=b[2]))
def removed(cur,regs,boxes):
    out={}
    for idx in np.nonzero(cur)[0]:
        row,col=idx//360,idx%360; lat,lon=center(row,col)
        r=region(row,col).rstrip('~')
        if r in regs or any(inbox(lat,lon,b) for b in boxes): out[r]=out.get(r,0)+1
    return out
sp=json.load(open(W+'/assets/earth/config/fauna-species.json',encoding='utf-8'))
for r in sp['entries']:
    if not (r.get('lost1318Regions') or r.get('lost1318Envelopes')): continue
    o=removed(unpack(r['currentMask']),set(r.get('lost1318Regions',[])),r.get('lost1318Envelopes',[]))
    if o: print(r['provider']+':'+'-'.join(r['tokens']), sum(o.values()), o)
van=json.load(open(W+'/assets/earth/config/fauna-vanilla.json',encoding='utf-8'))
for ent in van['entries']:
    if 'mask' not in ent or not (ent.get('lost1318Regions') or ent.get('lost1318Envelopes')): continue
    arr=tifffile.imread(sys.argv[2]+'/'+ent['taxon'].replace(' ','_')+'.tif')
    arr=np.asarray(arr).reshape(-1)[:142*360]
    o=removed((arr==1),set(ent.get('lost1318Regions',[])),ent.get('lost1318Envelopes',[]))
    if o: print('game:'+ent['family']+'-'+ent['type'], sum(o.values()), o)
