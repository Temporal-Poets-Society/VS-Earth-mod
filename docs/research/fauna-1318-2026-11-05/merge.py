"""Merge the research results (result-A/B/C/D.json) into tools/fauna-1318.json, validating keys and region codes."""
import json,sys,os
W=sys.argv[1];D=sys.argv[2]
codes={r['code'] for r in json.load(open(W+'/assets/earth/config/treeregions.json',encoding='utf-8'))['regions']}
sp=json.load(open(W+'/assets/earth/config/fauna-species.json',encoding='utf-8'))
mod_keys={r['provider']+':'+'-'.join(r['tokens']) for r in sp['entries'] if r.get('mask') and not r.get('extinct')}
van=json.load(open(W+'/assets/earth/config/fauna-vanilla.json',encoding='utf-8'))
van_keys={'game:'+e['family']+('-'+e['type'] if 'type' in e else '') for e in van['entries']}
rank={'high':0,'medium':1,'low':2}
taxa={};problems=[]
for area in 'ABC':
    path=f'{D}/result-{area}.json'
    if not os.path.exists(path): problems.append('missing '+path); continue
    for binomial,t in json.load(open(path,encoding='utf-8')).items():
        m=taxa.setdefault(binomial,{'keys':[],'lost1318Regions':[],'lost1318Envelopes':[],'evidence':[],'sources':[],'confidence':'high','areas':[]})
        m['areas'].append(area)
        for k in t.get('keys',[]):
            if k not in mod_keys|van_keys: problems.append(f'{binomial}: unknown key {k}')
            elif k not in m['keys']: m['keys'].append(k)
        for c in t.get('exclude',[]) or []:
            if c not in codes: problems.append(f'{binomial}: unknown region {c}')
            elif c not in m['lost1318Regions']: m['lost1318Regions'].append(c)
        for b in t.get('excludeBoxes',[]) or []:
            if len(b)!=4 or b[1]>b[3]: problems.append(f'{binomial}: bad box {b}')
            else: m['lost1318Envelopes'].append(b)
        if t.get('evidence'): m['evidence'].append(t['evidence'].strip())
        for s in t.get('sources',[]) or []:
            if s not in m['sources']: m['sources'].append(s)
        c=t.get('confidence','medium')
        if rank.get(c,1)>rank[m['confidence']]: m['confidence']=c
out={}
for b,m in sorted(taxa.items()):
    if not m['keys']: problems.append(f'{b}: no keys'); continue
    e={'keys':sorted(m['keys']),'lost1318Regions':sorted(m['lost1318Regions'])}
    if m['lost1318Envelopes']: e['lost1318Envelopes']=m['lost1318Envelopes']
    e['evidence']=' '.join(m['evidence']); e['sources']=m['sources']; e['confidence']=m['confidence']
    out[b]=e
dup={}
for b,e in out.items():
    for k in e['keys']: dup.setdefault(k,[]).append(b)
for k,v in dup.items():
    if len(v)>1: problems.append(f'key {k} under several binomials: {v}')
print(json.dumps({'taxa':len(out),'withLosses':sum(bool(e['lost1318Regions'] or e.get('lost1318Envelopes')) for e in out.values())}))
for p in problems: print('PROBLEM',p)
json.dump(out,open(D+'/merged-taxa.json','w',encoding='utf-8'),indent=1,ensure_ascii=False)

# ---- domestic animals (result-D): feral where people kept them in 1318
d=json.load(open(D+'/result-D.json',encoding='utf-8'))
def chk(name,lst):
    bad=[c for c in lst if c not in codes]
    if bad: print('PROBLEM',name,'unknown regions',bad)
    return sorted(lst)
def src(v): return {'evidence':v.get('evidence',''),'sources':v.get('sources',[]),'confidence':v.get('confidence','')}
vanilla={}
vanilla['chicken']={'range1318Regions':chk('chicken',d['chicken']['regions']),**src(d['chicken'])}
vanilla['sheep-mouflon']={'added1318Regions':chk('sheep',d['sheep-mouflon']['regions']),**src(d['sheep-mouflon'])}
gd=d['goat-domestic']
for b in ('valais','angora','nubian','sirohi'):
    v=d['goat-'+b]
    vanilla['goat-'+b]={'range1318Regions':chk(b,v['regions']),'evidence':v.get('evidence','')+' Goats kept in 1318: '+gd.get('evidence',''),'sources':gd.get('sources',[])+v.get('sources',[]),'confidence':gd.get('confidence','')}
pig=d['pig-eurasian']
vanilla['pig-eurasian']={'added1318Regions':chk('pig',pig['addRegions']),**src(pig)}
if pig.get('excludeRegions'): vanilla['pig-eurasian']['lost1318Regions']=chk('pig',pig['excludeRegions'])
vanilla['deer-fallow']={'range1318Regions':chk('fallow',d['deer-fallow']['regions']),**src(d['deer-fallow'])}
t=d['goat-turdag']
turdag={'species':t['species'],'regions':t['regions'],**src(t)}
json.dump({'vanilla':vanilla,'turdag':turdag},open(D+'/merged-vanilla.json','w',encoding='utf-8'),indent=1,ensure_ascii=False)
print('vanilla',{k:len(v.get('range1318Regions') or v.get('added1318Regions')) for k,v in vanilla.items()})
