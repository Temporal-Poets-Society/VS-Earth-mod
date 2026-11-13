"""Presence-only evidence maps. No KDE, probability calibration, or country rescaling."""
from pathlib import Path
from collections import defaultdict
import csv,gzip,html,json,math,sys,tempfile
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.colors import ListedColormap
from matplotlib.patches import Patch
from build_mrds import COMMODITIES
ROOT=Path(__file__).resolve().parent
OUT=ROOT/'normalized';MAPS=ROOT/'maps';MAPS.mkdir(exist_ok=True)
EARTH=ROOT.parents[2]
plt.rcParams.update({'font.family':'DejaVu Sans','font.size':10,'axes.titleweight':'bold'})

def load():
 with np.load(EARTH/'preview/grids.npz') as p:
  f=p['flags'];step=max(1,f.shape[1]//1800);ocean=((f[::step,::step]&1)>0).astype(np.uint8)
 records={}
 with gzip.open(OUT/'mrds-records.csv.gz','rt',encoding='utf-8') as f:
  for r in csv.DictReader(f):records[r['record_id']]=r
 minerals=defaultdict(list)
 with gzip.open(OUT/'record-explicit-minerals.csv.gz','rt',encoding='utf-8') as f:
  for r in csv.DictReader(f):minerals[r['mineral']].append(records[r['record_id']])
 cov={r['commodity']:r for r in csv.DictReader((OUT/'commodity-coverage.csv').open(encoding='utf-8'))}
 spec={r['commodity']:r for r in csv.DictReader((OUT/'specialist-coverage.csv').open(encoding='utf-8'))}
 return ocean,records,minerals,cov,spec

def cells(points):
 a=np.zeros((180,360),dtype=np.uint8)
 for lon,lat in points:a[min(179,math.floor(lat+90)),min(359,math.floor(lon+180))]=1
 return a

def base(ax,ocean,compact=False):
 ax.imshow(ocean,extent=(-180,180,-90,90),origin='upper',cmap=ListedColormap(['#e7e8e2','#f5f8fa']),interpolation='nearest',zorder=0)
 ax.set(xlim=(-180,180),ylim=(-65,85),aspect='equal')
 ax.set_xticks([-120,-60,0,60,120]);ax.set_yticks([-60,0,60]);ax.tick_params(length=0,labelsize=7 if compact else 9)
 ax.grid(color='#cbd5da',linewidth=.35,zorder=1)
 for s in ax.spines.values():s.set_edgecolor('#c7cfd2')

def presence(ax,a,b=None):
 c=a.copy()
 if b is not None:c+=b*2
 masked=np.ma.masked_where(c==0,c)
 ax.imshow(masked,extent=(-180,180,-90,90),origin='lower',cmap=ListedColormap(['#bb782b','#216c8c','#153e55']),vmin=1,vmax=3,interpolation='nearest',zorder=2)

def main():
 ocean,records,minerals,cov,spec=load();allmaps={};mapaudit={}
 for code,(label,_) in COMMODITIES.items():
  with gzip.open(OUT/(code+'-occurrences.csv.gz'),'rt',encoding='utf-8') as f:mrds=list(csv.DictReader(f))
  features=json.loads((OUT/(code+'-specialist.geojson')).read_text(encoding='utf-8'))['features']
  supplemental=[]
  if code=='C_coal' and (OUT/'fsu-coal-points.geojson').exists():supplemental=json.loads((OUT/'fsu-coal-points.geojson').read_text())['features']
  if code in ['K','NaCl']:
   potash=json.loads((OUT/'PotashDeps.geojson').read_text(encoding='utf-8'))['features']
   # Entire potash source includes concessions and basin centroids. Map only
   # source types naming a mine/deposit/mineral occurrence for point evidence.
   allowed={'mine','mineral occurrence','deposit','mine(?)','mineral occurrence, structure','grouped mines/occurrences'}
   supplemental+=[f for f in potash if f['properties']['source_attributes']['TYPE_REC'] in allowed and
     (code=='K' or f['properties']['halite_explicit_in_source'])]
  a=cells((float(r['longitude']),float(r['latitude'])) for r in mrds)
  b=cells(f['geometry']['coordinates'][:2] for f in features+supplemental)
  allmaps[code]=(a,b)
  fig,ax=plt.subplots(figsize=(13.5,6.6));fig.subplots_adjust(left=.055,right=.985,bottom=.22,top=.83)
  base(ax,ocean);presence(ax,a,b)
  fig.suptitle(label,x=.055,y=.965,ha='left',fontsize=22,weight='bold')
  fig.text(.055,.91,f"Presence of reported evidence  |  {len(mrds):,} MRDS records + {len(features):,} specialist links + {len(supplemental):,} additional records",fontsize=11,color='#40535e')
  share=float(cov[code]['us_record_share'] or 0)*100
  fig.text(.055,.875,f"MRDS US share: {share:.1f}%  |  Survey coverage is uneven; counts do not measure abundance.",fontsize=10,color='#735627')
  legend=[Patch(facecolor='#bb782b',label='MRDS evidence only'),Patch(facecolor='#216c8c',label='Specialist/regional evidence only'),Patch(facecolor='#153e55',label='Both source groups')]
  fig.legend(handles=legend,loc='lower left',bbox_to_anchor=(.05,.125),ncol=3,frameon=False,fontsize=10)
  fig.text(.055,.075,'Each coloured 1° cell contains ≥1 record. Cells are a display aggregation, not deposit footprints or calibrated spawn chance.',fontsize=9)
  fig.text(.055,.04,'Grey = no imported evidence / unassessed. Sources may overlap. Minerals, grade, historical use and early access need separate review.',fontsize=9)
  fig.text(.985,.04,'Earth research · 29 Sep 2026',fontsize=8,ha='right',color='#626a70')
  fig.savefig(MAPS/(code+'.png'),dpi=150,facecolor='white');plt.close(fig)
  mapaudit[code]={'mrds_records':len(mrds),'specialist_record_links':len(features),'additional_potash_or_coal_records':len(supplemental),'mrds_cells':int(a.sum()),'specialist_cells':int(b.sum()),'map':'maps/'+code+'.png'}
 codes=list(COMMODITIES)
 for page in range(5):
  subset=codes[page*8:(page+1)*8]
  fig,axes=plt.subplots(4,2,figsize=(16,15.4));fig.subplots_adjust(left=.045,right=.99,bottom=.055,top=.90,hspace=.28,wspace=.10)
  fig.suptitle(f'Remaining ores · evidence atlas {page+1}/5',x=.045,y=.975,ha='left',fontsize=23,weight='bold')
  fig.text(.045,.945,'Orange: MRDS  |  Blue: specialist/regional  |  Dark: both  —  one fixed-strength mark per 1° cell',fontsize=11)
  fig.text(.045,.922,'These are evidence maps, not abundance heatmaps. Uncoloured regions remain unassessed.',fontsize=11,color='#72511a')
  for ax,code in zip(axes.flat,subset):
   base(ax,ocean,True);presence(ax,*allmaps[code]);a=mapaudit[code]
   ax.set_title(COMMODITIES[code][0],loc='left',fontsize=11,pad=18)
   ax.text(0,1.028,f"MRDS {a['mrds_records']:,}  |  other source links {a['specialist_record_links']+a['additional_potash_or_coal_records']:,}",transform=ax.transAxes,fontsize=8,color='#46535a')
  fig.text(.045,.022,'Source records are not unique deposits. Source coverage, mineral identity, geological potential and accessibility are separate layers.  |  29 Sep 2026',fontsize=9)
  fig.savefig(MAPS/f'contact-sheet-{page+1}.png',dpi=140,facecolor='white');plt.close(fig)
 # Explicit mineral evidence is deliberately distinct from element presence.
 selected=['hematite','magnetite','limonite','bogiron','galena','sphalerite','bismuthinite','pentlandite',
  'chromite','ilmenite','rhodochrosite','wolframite','cinnabar','borax','kernite','niter',
  'sylvite','halite','sulfur','pyrite','phosphorite','alunite','lapislazuli','peridot']
 for page in range(3):
  fig,axes=plt.subplots(4,2,figsize=(16,15.4));fig.subplots_adjust(left=.045,right=.99,bottom=.055,top=.90,hspace=.28,wspace=.10)
  fig.suptitle(f'Explicit mineral mentions · {page+1}/3',x=.045,y=.975,ha='left',fontsize=23,weight='bold')
  fig.text(.045,.94,'MRDS ore field only. An element record without a named mineral is withheld from these maps.',fontsize=11)
  fig.text(.045,.918,'Missing mentions are a data gap, not mineral absence. A mention does not establish recoverable grade.',fontsize=11,color='#72511a')
  for ax,m in zip(axes.flat,selected[page*8:(page+1)*8]):
   base(ax,ocean,True);recs=minerals[m];a=cells((float(r['longitude']),float(r['latitude'])) for r in recs);presence(ax,a)
   ax.set_title(m.replace('lapislazuli','Lapis lazuli').capitalize()+f'  |  {len(recs):,} source records',loc='left',fontsize=11,pad=9)
  fig.text(.045,.022,'Orange cells: explicit mineral terms in the source ore field; variants, oxidation and processing still require review.  |  Earth research · 29 Sep 2026',fontsize=9)
  fig.savefig(MAPS/f'mineral-forms-{page+1}.png',dpi=140,facecolor='white');plt.close(fig)
 (ROOT/'map-validation.json').write_text(json.dumps({'date':'2026-09-29','map_units':'binary 1-degree evidence presence; not calibrated probability','country_normalization':False,'maps':mapaudit,
  'individual_commodity_maps':40,'commodity_contact_sheets':5,'mineral_form_contact_sheets':3,'source_attribution':'USGS MRDS and cited USGS specialist/regional sources; base land/water from Earth preview grids. Aggregation performed by Earth research; no USGS endorsement.'},indent=2),encoding='utf-8')
 tiles=''.join(f'<article><a href="maps/{c}.png"><img loading="lazy" src="maps/{c}.png" alt="{html.escape(v[0])} evidence map"></a><h2>{html.escape(v[0])}</h2><a href="normalized/{c}-occurrences.csv.gz">MRDS CSV</a> · <a href="normalized/{c}-occurrences.geojson.gz">MRDS GeoJSON</a> · <a href="normalized/{c}-specialist.geojson">Specialist GeoJSON</a></article>' for c,v in COMMODITIES.items())
 (ROOT/'index.html').write_text('<!doctype html><html><meta charset="utf-8"><title>Earth mineral evidence atlas</title><style>body{font:16px system-ui;max-width:1500px;margin:2rem auto;padding:0 1rem;background:#f3f5f6;color:#172c3a}h1{font-size:2.3rem}p{max-width:1050px;line-height:1.6}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(480px,1fr));gap:1rem}article{background:white;padding:1rem;border:1px solid #d3dbe0;border-radius:8px}img{width:100%}h2{font-size:1.15rem}a{color:#17587c}</style><h1>Earth mineral evidence atlas</h1><p>Research snapshot: 29 September 2026. These 40 maps show imported evidence, <strong>not ore abundance or spawn probabilities</strong>. Fixed-strength 1° presence cells avoid counting dense survey records as stronger geology. Uncoloured areas are unassessed, not barren. Element maps do not establish mineral species, grade or early accessibility.</p><p>Sources: USGS MRDS, specialist global mineral compilations, potash assessment and former-Soviet coal study. Source records overlap; no global unique-deposit count is claimed. See <a href="../../remaining-ore-evidence.md">research report</a> and <a href="normalized/commodity-coverage.csv">coverage audit</a>. The <a href="maps/evaporite-permissive.png">separate evaporite potential map</a> shows expert tracts, not observed sites or accessible halite.</p><div class="grid">'+tiles+'</div></html>',encoding='utf-8')
 print(json.dumps({'commodity_maps':40,'contact_sheets':8,'atlas':'index.html'}),flush=True)

if __name__=='__main__':main()
