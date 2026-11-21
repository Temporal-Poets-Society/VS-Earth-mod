import json,sys,base64,math,collections
grid=json.load(open(sys.argv[1],encoding='utf-8'));rules=json.load(open(sys.argv[2],encoding='utf-8'))
L=base64.b64decode(grid['lithology'])
classes={int(k):v for k,v in rules['classes'].items()};prof={p['id']:p for p in rules['profiles']}
# material -> block without GA (Earth's own gneiss/schist/dolostone present)
blk={'granite':'granite','basalt':'basalt','andesite':'andesite','peridotite':'peridotite','sandstone':'sandstone','shale':'shale','claystone':'claystone','limestone':'limestone','conglomerate':'conglomerate','slate':'slate','chalk':'chalk','chert':'chert','tuff':'tuff','dolostone':'dolostone','gneiss':'gneiss','schist':'schist','gabbro':'basalt','diorite':'andesite','rhyolite':'granite','mudstone':'claystone'}
IG=['andesite','granite','basalt','peridotite'];SED=['claystone','sandstone','shale','chalk','limestone','chert','conglomerate']
hosts={
 'cassiterite':IG+['phyllite','slate','gneiss','schist'],
 'bismuthinite':IG+['phyllite','slate','gneiss','schist'],
 'chromite':IG+['kimberlite'],
 'pentlandite':IG,
 'nativecopper':IG+['claystone','sandstone','shale','chalk','chert','conglomerate','phyllite','slate','gneiss','quartzite','schist'],
 'malachite':['limestone','dolostone','marble'],
 'galena(+silver)':SED+['dolostone','quartzite'],
 'sphalerite':IG+SED+['phyllite','slate','dolostone','gneiss','quartzite','schist'],
 'hematite':['granite','peridotite','limestone','phyllite','dolostone','schist'],
 'limonite':['basalt','shale','chert'],
 'magnetite':['andesite','claystone','chalk','conglomerate','slate','gneiss'],
 'ilmenite':IG+['phyllite','slate','gneiss','schist'],
 'quartz/gold':IG+SED+['phyllite','slate','dolostone','gneiss','quartzite','schist'],
 'coal':SED+['dolostone'],
 'sulfur':SED+['dolostone','quartzite'],
 'cinnabar':IG+['phyllite','slate','gneiss','schist'],
 'olivine/peridot':['peridotite'],
 'emerald':['basalt','peridotite','shale','limestone','phyllite','slate','dolostone','gneiss','schist'],
 'graphite':['phyllite','slate','gneiss','schist'],
}
T=66
def layers(pid):
    p=prof[pid];c=0;out=[]
    for b in p['beds']:
        t=max(2,b['fraction']*T);out.append((c,c+t,blk[b['material']]));c+=t
    out.append((c,1e9,blk[p['basement']]));return out
area=collections.Counter()
for z in range(360):
    a=math.cos(math.radians(90-(z+.5)*.5))
    for x in range(720):
        c=L[z*720+x]
        if c in classes and classes[c] not in('covered','unknown'): area[classes[c]]+=a
tot=sum(area.values())
print('profiles (T=66, sea 110):')
for pid in area:
    print(f"  {pid:22s} "+' | '.join(f"{a:.0f}-{(b if b<1e8 else 999):.0f}:{m}" for a,b,m in layers(pid)))
print('\nshare of regional-rock land (excl su) where the ore has a host: at surface (bed 0) / within 20 blocks below datum / within 45 / anywhere')
for ore,h in hosts.items():
    s0=s20=s45=sany=0;none=[]
    for pid,a in area.items():
        L_=layers(pid)
        d=[lo for lo,hi,m in L_ if m in h]
        first=min(d) if d else None
        if first is None: none.append(pid);continue
        sany+=a
        if first==0:s0+=a
        if first<20:s20+=a
        if first<45:s45+=a
    print(f"  {ore:16s} {100*s0/tot:5.1f}% {100*s20/tot:5.1f}% {100*s45/tot:5.1f}% {100*sany/tot:5.1f}%   no host at any depth: {', '.join(none) if none else '-'}")
