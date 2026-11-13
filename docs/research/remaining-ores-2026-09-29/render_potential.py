"""Show source expert permissive tracts separately from occurrence evidence."""
from pathlib import Path
import importlib.util,json
from render_maps import load,base,plt,ROOT,MAPS
from matplotlib.patches import Patch

def main():
 helper=ROOT.parent/'copper-tin-preview-2026-09-29/render_preview.py'
 spec=importlib.util.spec_from_file_location('earth_existing_map_helper',helper)
 mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod)
 ocean,*_=load();features=json.loads((ROOT/'normalized/PotashTracts.geojson').read_text(encoding='utf-8'))['features']
 fig,ax=plt.subplots(figsize=(14,7));fig.subplots_adjust(left=.055,right=.985,bottom=.20,top=.78);base(ax,ocean)
 shown=0;withheld=0
 for f in features:
  p=f['properties'];g=f['geometry']
  if not p['geometry_valid']:withheld+=1;continue
  style=p['deposit_style'];brine='brine' in style
  polys=[g['coordinates']] if g['type']=='Polygon' else g['coordinates']
  for rings in polys:mod.plot_polygon(ax,rings,'#2b7897' if brine else '#b4743d','///' if brine else None)
  shown+=1
 fig.suptitle('Evaporite-related potash: geological potential',x=.055,y=.975,ha='left',fontsize=21,weight='bold')
 fig.text(.055,.892,f'USGS expert tracts: {shown} valid source polygons shown; {withheld} invalid polygons withheld.',fontsize=12)
 fig.text(.055,.844,'These are potash assessment areas, not complete halite maps or evidence of shallow salt.',fontsize=11,color='#73511a')
 fig.legend(handles=[Patch(facecolor='#b4743d',alpha=.5,label='Solid-salt / other evaporite tracts'),Patch(facecolor='#2b7897',hatch='///',alpha=.5,label='Brine-containing tracts')],loc='lower left',bbox_to_anchor=(.05,.12),ncol=2,frameon=False)
 fig.text(.055,.073,'Source: USGS SIR2010-5090-S (2014). Original WGS84 geometry; no boundary repair. Assessment can extend to 3 km depth.',fontsize=9)
 fig.text(.055,.04,'Source maps vary in scale and position. Unassessed regions are not barren. No spawn probability or game-grade calibration.',fontsize=9)
 fig.savefig(MAPS/'evaporite-permissive.png',dpi=160,facecolor='white');plt.close(fig)
 print(json.dumps({'valid_tracts_shown':shown,'invalid_withheld':withheld}),flush=True)

if __name__=='__main__':main()
