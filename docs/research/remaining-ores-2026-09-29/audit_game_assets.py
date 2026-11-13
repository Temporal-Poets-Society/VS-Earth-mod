"""Read installed assets and the uninstalled Geology Additions archive; save only research metadata."""
from pathlib import Path
from zipfile import ZipFile
import csv,hashlib,json,re
import json5
ROOT=Path(__file__).resolve().parent
GAME=Path('C:/Users/andri/AppData/Roaming/Vintagestory/assets/survival')
GA=ROOT.parent/'regions-2026-09-29/geoaddons.zip'

def main():
 assets=[];deposits=[];variants=[];errors=[]
 def add(provider,path,data):
  assets.append({'provider':provider,'path':path,'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data)})
  try:obj=json5.loads(data.decode('utf-8-sig'))
  except Exception as e:errors.append({'provider':provider,'path':path,'error':str(e)});return
  if '/deposits/' in path:
   def walk(entry,parent=None):
    if not isinstance(entry,dict):return
    attr=entry.get('attributes',{})
    deposits.append({'provider':provider,'source_asset':path,'code':entry.get('code'),'parent_deposit':parent,
     'generator':entry.get('generator'),'enabled':entry.get('enabled',True),'tries_per_chunk':entry.get('triesPerChunk'),
     'with_ore_map':entry.get('withOreMap'),'placeblock':attr.get('placeblock'),'inblock':attr.get('inblock'),
     'grade_distribution':attr.get('gradeDistribution'),'y_position':attr.get('yPosRel'),
     'radius':attr.get('radius'),'thickness':attr.get('thickness'),'surface_block':attr.get('surfaceBlock')})
    for child in entry.get('childDeposits',[]):walk(child,entry.get('code'))
   for entry in obj if isinstance(obj,list) else [obj]:walk(entry)
  if 'worldproperties/block/ore-' in path:
   for v in obj.get('variants',[]):variants.append({'provider':provider,'asset':path,'variant':v.get('Code') or v.get('code')})
 for p in (GAME/'worldgen/deposits').rglob('*.json'):add('vanilla',str(p.relative_to(GAME)).replace('\\','/'),p.read_bytes())
 for p in (GAME/'worldproperties/block').glob('ore-*.json'):add('vanilla',str(p.relative_to(GAME)).replace('\\','/'),p.read_bytes())
 with ZipFile(GA) as z:
  for n in z.namelist():
   if n.endswith('.json') and ('worldgen/deposits/' in n or n.startswith('assets/game/patches/ores/')):add('geoaddons_1.4.8',n,z.read(n))
 for name in ['globaldepositspawnrate','surfacecopper','surfacetin']:
  p=GAME/'patches'/(name+'.json');add('vanilla','patches/'+p.name,p.read_bytes())
 (ROOT/'game-asset-audit.json').write_text(json.dumps({'game_assets_root':str(GAME),'game_version_reported':'1.22.7','geoaddons_archive_sha256':hashlib.sha256(GA.read_bytes()).hexdigest(),
  'assets':assets,'vanilla_ore_variants':variants,'deposits':deposits,'parse_errors':errors},indent=2),encoding='utf-8')
 print(json.dumps({'audited_files':len(assets),'deposits_including_children':len(deposits),'vanilla_variants':len(variants),'parse_errors':errors,
 'vanilla_deposit_codes':[d['code'] for d in deposits if d['provider']=='vanilla'],'ga_deposit_codes':[d['code'] for d in deposits if d['provider'].startswith('geoaddons')]}),flush=True)

if __name__=='__main__':main()
