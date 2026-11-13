"""Reproducible fauna catalogue (rules version 3) from the reviewed provider inventory and range evidence.

Build dependencies: numpy and tifffile (+imagecodecs), e.g. the repository .venv or obj/fauna-inspect/pylibs.

Offline by default. PHYLACINE v1.2.1 rasters are read from local caches (gitignored):
  --range-cache DIR    Present_natural/*.tif   (default docs/research/regional-fauna-2026-09-29/ranges)
  --current-cache DIR  Current/*.tif           (default docs/research/regional-fauna-2026-09-29/ranges-current)
When the current cache holds a living taxon's raster, its rule also gets 'currentMask' for the "current" range profile.
--fetch downloads missing pinned files from the v1.2.1 tag (present-natural and current) into those caches.
The raster base URLs, licence and SHA-256 of every raster used are written to catalogue-summary.json.
Rules version 3 adds the "1318" profile (decision 0054): tools/fauna-1318.json lists, per living taxon, the regions (and boxes)
it had lost by about 1318; they become each rule's lost1318Regions/lost1318Envelopes/evidence1318.
"""
import argparse, base64, csv, hashlib, json, re, sys, urllib.request, fnmatch
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'obj/fauna-inspect/pylibs'))
import numpy as np
import tifffile
SRC=ROOT/'docs/research/regional-fauna-2026-09-29'
ap=argparse.ArgumentParser()
ap.add_argument('--range-cache',type=Path,default=SRC/'ranges')
ap.add_argument('--current-cache',type=Path,default=SRC/'ranges-current')
ap.add_argument('--fetch',action='store_true')
args=ap.parse_args()
BASE='https://raw.githubusercontent.com/MegaPast2Future/PHYLACINE_1.2/v1.2.1/'
def fetch(path,target):
    if not target.exists():
        if not args.fetch: raise SystemExit(f'missing {target}; rerun with --fetch to download {BASE+path}')
        target.parent.mkdir(parents=True,exist_ok=True)
        with urllib.request.urlopen(BASE+path,timeout=90) as r: target.write_bytes(r.read())
    return target
fetch('Data/Traits/Trait_data.csv',SRC/'traits.csv')
fetch('Data/Ranges/Spatial_metadata.csv',SRC/'spatial-metadata.csv')
tree=json.loads((SRC/'phylacine-tree.json').read_text())
names={re.sub('[^a-z]','',Path(x['path']).stem.lower()):Path(x['path']).stem for x in tree['tree'] if x['path'].startswith('Data/Ranges/Present_natural/') and x['path'].endswith('.tif')}
traits=list(csv.DictReader((SRC/'traits.csv').open(encoding='utf-8-sig')))
traitby={re.sub('[^a-z]','',r['Binomial.1.2'].lower()):r for r in traits}
audit=json.loads((ROOT/'docs/research/ecology-2026-09-28/entity-spawn-audit.json').read_text(encoding='utf-8-sig'))
releases=json.loads((ROOT/'docs/research/ecology-2026-09-28/release-inventory.json').read_text(encoding='utf-8-sig'))
manual=json.loads((ROOT/'tools/fauna-taxa.json').read_text(encoding='utf-8'))
birds=json.loads((ROOT/'tools/fauna-birds.json').read_text(encoding='utf-8'))
for provider in ['dinornithidae','spheniscidae']:
    for regions,taxa in birds[provider].items():
        for taxon in taxa:
            key=taxon.rstrip('*')
            r={'regions':regions.split(),'extinct':taxon.endswith('*'),'evidence':manual['sources']['nzBirds' if provider=='dinornithidae' else 'penguins'], 'water':'coast' if provider=='spheniscidae' else 'land'}
            if r['extinct']: r['period']=birds['periods'][provider]
            r.update(birds['overrides'].get(key,{}))
            manual['taxa'][provider+':'+key]=r

# Island barriers for extinct land taxa defined by a CONTINENTAL box only: islands inside such a box that the
# animal never reached. Island-scale boxes (all < ISLAND_BOX_DEG2) are already drawn around the right island and get
# no exclusions; a region the rule lists itself is never excluded.
ISLANDS='ICE GNL SVA JAP NNS TAI CHH CYP COR SAR SIC BAL KRI EAI BAH CUB JAM HAI DOM PUE CAY ARU NLA TRT WIN LEE SWC GAL FAL NFK NZN NZS CTM ATP HAW'.split()
ISLAND_BOX_DEG2=25
def box_area(b):
    w,s,e,n=b
    return ((e-w)%360 or (360 if e!=w else 0))*(n-s)

rules=[]; seen=set(); climate=[]
for entry in audit:
    mod=entry['modid']
    if mod.startswith('floral'): continue
    groups={g['code']:g.get('states',[]) for g in entry.get('variantgroups') or []}
    if 'adult' not in groups.get('age',[]): continue
    if 'type' in groups: candidates=[([v],v) for v in groups['type']]
    elif 'genus' in groups:
        # Provider audit's spawn condition keys disambiguate constrained genus/species combinations.
        keys=list((entry.get('spawnConditionsByType') or {}).keys())
        candidates=[]
        for genus in groups['genus']:
            for species in groups['species']:
                if any('-'+genus+'-'+species in k for k in keys): candidates.append(([genus,species],genus+species))
    elif 'species' in groups:
        prefix={'sirenia':'','chelonioidea':'','pantherinae':'neofelis' if 'neofeli' in entry['code'] else 'panthera'}.get(mod,'')
        candidates=[([v],prefix+v) for v in groups['species']]
    else: continue
    for tokens,key in candidates:
        ident=mod+':'+key
        if ident in seen: continue
        seen.add(ident)
        override=dict(manual.get('taxa',{}).get(ident,{}))
        resolved=override.get('raster',key)
        name=names.get(resolved)
        tr=traitby.get(resolved,{})
        extinct=tr.get('IUCN.Status.1.2','') in ('EX','EP')
        r={'provider':mod,'taxon':name.replace('_',' ') if name else key,'tokens':tokens,'extinct':extinct,'water':'land'}
        split=override.pop('splitNote',None)
        r.update({k:v for k,v in override.items() if k!='raster'})
        # Present-natural ranges for living taxa are labelled as such. Extinct animals require documented regions/boxes.
        if name and not r['extinct'] and not r.get('quarantined') and not r.get('noMask'):
            r['_raster']=name
            phyl='PHYLACINE 1.2.1 present-natural range of '+name.replace('_',' ')+' (estimated range without human impact; not a dated historical polygon)'
            r['evidence']=phyl if 'evidence' not in override else override['evidence']+'; '+phyl
            if split: r['note']=(r.get('note','')+' '+split).strip()
        elif not r.get('envelopes') and not r.get('regions'):
            r['quarantined']=True
            r.setdefault('note','Unresolved taxonomy or historical range; excluded from new natural spawns pending review.')
        if r.get('extinct') and not r.get('quarantined') and not r.get('period'):
            raise SystemExit('extinct rule without a stated period: '+ident)
        r.pop('noMask',None)
        envs=r.get('envelopes') or []
        if r['water']=='land' and r.get('extinct') and envs and not r.get('regions') and max(box_area(b) for b in envs)>=ISLAND_BOX_DEG2:
            r['excludedRegions']=[c for c in ISLANDS if c not in (r.get('regions') or [])]
        rules.append(r)
        # Representative adult climate audit; runtime retains the provider's resolved conditions untouched.
        variant=[]
        for group in entry.get('variantgroups') or []:
            states=group.get('states',[])
            matching=[v for v in states if v in tokens]
            variant.append('adult' if group['code']=='age' else matching[0] if matching else states[0] if states else '')
        example=entry['code']+'-'+'-'.join(variant)
        base=entry.get('spawnConditions') or {}
        constraints=dict(base.get('climate') or {})
        for pattern,conditions in (entry.get('spawnConditionsByType') or {}).items():
            if fnmatch.fnmatchcase(example,pattern):
                constraints.update(conditions.get('climate') or {})
                break
        climate.append({'provider':mod,'tokens':tokens,'example':mod+':'+example,'climate':constraints})

def packed(path):
    a=tifffile.imread(path)
    assert a.shape==(142,360), path
    return base64.b64encode(np.packbits((a==1).ravel(),bitorder='little')).decode()
current_used=set(); current_missing=set(); natural_used=set()
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
for r in rules:
    if '_raster' not in r: continue
    name=r.pop('_raster')
    r['mask']=packed(fetch('Data/Ranges/Present_natural/'+name+'.tif',args.range_cache/(name+'.tif'))); natural_used.add(name)
    cur=args.current_cache/(name+'.tif')
    if not cur.exists() and args.fetch: fetch('Data/Ranges/Current/'+name+'.tif',cur)
    if cur.exists():
        r['currentMask']=packed(cur); current_used.add(name)
    else: current_missing.add(name)

# Decision 0054: regions each living taxon had lost by about 1318, keyed by "provider:token-token".
f1318=json.loads((ROOT/'tools/fauna-1318.json').read_text(encoding='utf-8'))
by_key={}
for binomial,t in f1318['taxa'].items():
    for k in t['keys']:
        if k in by_key: raise SystemExit('fauna-1318.json: key listed twice: '+k)
        by_key[k]=(binomial,t)
used=set()
for r in rules:
    k=r['provider']+':'+'-'.join(r['tokens'])
    if k not in by_key: continue
    used.add(k)
    binomial,t=by_key[k]
    if r.get('extinct') or 'mask' not in r: raise SystemExit('fauna-1318.json: '+k+' is not a living raster rule')
    if t.get('lost1318Regions'): r['lost1318Regions']=sorted(t['lost1318Regions'])
    if t.get('lost1318Envelopes'): r['lost1318Envelopes']=t['lost1318Envelopes']
    r['evidence1318']=t['evidence']+(' Sources: '+' '.join(t['sources']) if t.get('sources') else '')
unknown=[k for k in by_key if k not in used and not k.startswith('game:')]
if unknown: raise SystemExit('fauna-1318.json: keys match no rule: '+', '.join(unknown))

providers=[{'id':r['id'],'version':r['version']} for r in releases if not r['id'].startswith('floral')]
output={'version':4,'sources':manual['sources'],'providers':providers,'entries':rules}
(ROOT/'assets/earth/config/fauna-species.json').write_text(json.dumps(output,separators=(',',':'),ensure_ascii=False)+'\n',encoding='utf-8')
held=[r for r in rules if r.get('quarantined')]
counts={'rules':len(rules),'enabled':len(rules)-len(held),'held':len(held),'extinct':sum(bool(r.get('extinct')) for r in rules),
    'enabledExtinct':sum(bool(r.get('extinct')) and not r.get('quarantined') for r in rules),
    'heldLiving':sum(not r.get('extinct') for r in held),'heldExtinct':sum(bool(r.get('extinct')) for r in held),
    'presentNaturalRasters':sum('mask' in r for r in rules),'currentRasters':sum('currentMask' in r for r in rules),
    'lost1318':sum(bool(r.get('lost1318Regions') or r.get('lost1318Envelopes')) for r in rules)}
rasters={'licence':'CC0 1.0 (PHYLACINE v1.2.1 README: https://github.com/MegaPast2Future/PHYLACINE_1.2/blob/v1.2.1/README.md)',
    'presentNatural':{'url':BASE+'Data/Ranges/Present_natural/{name}.tif','sha256':{n+'.tif':sha(args.range_cache/(n+'.tif')) for n in sorted(natural_used)}},
    'current':{'url':BASE+'Data/Ranges/Current/{name}.tif','sha256':{n+'.tif':sha(args.current_cache/(n+'.tif')) for n in sorted(current_used)}}}
(SRC/'catalogue-summary.json').write_text(json.dumps({**counts,'active':counts['enabled'],
    'quarantined':[r['provider']+':'+r['taxon'] for r in held],'currentRastersMissing':sorted(current_missing),'providers':providers,
    'rasterSources':rasters,'inputHashes':{str(p.relative_to(ROOT)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in [SRC/'traits.csv',SRC/'spatial-metadata.csv',SRC/'phylacine-tree.json',ROOT/'tools/fauna-taxa.json',ROOT/'tools/fauna-birds.json',ROOT/'tools/fauna-1318.json',ROOT/'docs/research/ecology-2026-09-28/entity-spawn-audit.json',ROOT/'docs/research/ecology-2026-09-28/release-inventory.json']}},indent=2))
(SRC/'provider-climate.json').write_text(json.dumps(climate,indent=2))
with (SRC/'held-species.csv').open('w',newline='',encoding='utf-8') as f:
    writer=csv.writer(f);writer.writerow(['provider','taxon','extinct','reason'])
    for r in held: writer.writerow([r['provider'],r['taxon'],r['extinct'],r['note']])
print(json.dumps(counts))
if current_missing: print(f'current-range rasters not cached for {len(current_missing)} taxa (natural profile only for them)')
