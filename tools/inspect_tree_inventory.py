"""Inventory natural supplier registrations from vanilla, eight Floral Zones zips and Wildcraft.

Requires json5. Run from the repository root after obtaining the releases recorded
in modpack/pack-lock.json (by default they are read from the game's installed Mods
folder, %APPDATA%/VintagestoryData/Mods). The temporary dependency folder is an
optional convenience.

--output   the generator inventory used by tools/bake_tree_regions.py (unchanged format)
--fixture  every registration with its list (treegens/shrubgens) and complete climate
           limits, in registration order (vanilla, then the providers in load order), as
           used by tests/RegionalTreesTests for the offline pick/coverage checks.
"""
import argparse, glob, json, os, sys, zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
for extra in (ROOT/'obj/ecology-inspect/pylibs', ROOT.parents[2]/'obj/ecology-inspect/pylibs' if len(ROOT.parents)>2 else None):
    if extra and extra.exists(): sys.path.insert(0,str(extra))
import json5
mods=Path(os.environ.get('APPDATA',''))/'VintagestoryData/Mods'
p=argparse.ArgumentParser()
p.add_argument('--game',type=Path,default=Path(os.environ.get('APPDATA',''))/'Vintagestory')
p.add_argument('--floral',type=Path,default=mods)
p.add_argument('--wildcraft',type=Path,default=next(iter(sorted(mods.glob('wildcrafttree-*.zip'))),mods/'wildcrafttree.zip'))
p.add_argument('--output',type=Path,default=None)
p.add_argument('--fixture',type=Path,default=None)
a=p.parse_args()
if not a.output and not a.fixture: a.output=ROOT/'docs/research/tree-generator-inventory.json'

def provider_of(path):
    stem=Path(path).stem.lower()
    return stem.split('-')[0] if '-' in stem else stem

def walk(value):
    if isinstance(value,dict):
        lowered={k.lower():v for k,v in value.items()}
        if isinstance(lowered.get('generator'),str): yield lowered
        for v in value.values(): yield from walk(v)
    elif isinstance(value,list):
        for v in value: yield from walk(v)

sources=[('vanilla',a.game/'assets/survival/worldgen/treengenproperties.json')]
sources += [(provider_of(f),f) for f in sorted(a.floral.glob('floralzones*.zip'))]
sources += [('wildcrafttree',a.wildcraft)]
records=[]
fixture=[]
DEFAULTS={'weight':0,'habitat':'land','mintemp':-40,'maxtemp':40,'minrain':0,'maxrain':255,'minfert':0,'maxfert':255,
          'minforest':0,'maxforest':255,'minheight':0.0,'maxheight':1.0}
def full(provider,kind,row):
    out={'provider':provider,'kind':kind,'generator':'game:'+row['generator'] if ':' not in row['generator'] else row['generator']}
    for k,v in DEFAULTS.items(): out[k]=row.get(k,v)
    out['habitat']=str(out['habitat']).lower()
    return out
for provider,path in sources:
    if path.suffix=='.zip':
        with zipfile.ZipFile(path) as z:
            texts=[(n,z.read(n).decode('utf-8-sig')) for n in sorted(z.namelist()) if n.endswith('.json') and ('treegen' in n.lower() or 'treengen' in n.lower())]
    else: texts=[('assets/survival/worldgen/treengenproperties.json',path.read_text(encoding='utf-8-sig'))]
    for name,txt in texts:
        data=json5.loads(txt)
        for row in walk(data): records.append({'provider':provider,'source':name,**row})
        if isinstance(data,dict):
            for kind in ('treegens','shrubgens'):
                for row in data.get(kind) or data.get(kind.capitalize()) or []:
                    fixture.append(full(provider,kind,{k.lower():v for k,v in row.items()}))
        elif isinstance(data,list):
            # JSON patches adding to /treegens/- or /shrubgens/-
            for patch in data:
                if not isinstance(patch,dict) or patch.get('op')!='add' or not isinstance(patch.get('value'),dict): continue
                kind=str(patch.get('path','')).strip('/').split('/')[0].lower()
                if kind in ('treegens','shrubgens'):
                    fixture.append(full(provider,kind,{k.lower():v for k,v in patch['value'].items()}))
if a.output:
    a.output.parent.mkdir(parents=True,exist_ok=True)
    a.output.write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8')
    print(f'{len(records)} registrations; {len(set((r["provider"],r["generator"]) for r in records))} distinct natural generators.')
if a.fixture:
    a.fixture.parent.mkdir(parents=True,exist_ok=True)
    a.fixture.write_text('[\n'+',\n'.join(json.dumps(r,separators=(',',':')) for r in fixture)+'\n]\n',encoding='utf-8')
    print(f'{len(fixture)} registrations written to {a.fixture}.')
