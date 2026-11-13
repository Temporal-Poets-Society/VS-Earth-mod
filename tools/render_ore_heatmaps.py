"""Render the runtime grids, with an offline interactive tuning preview and rock contacts."""
from pathlib import Path
import base64, gzip, hashlib, json
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.colors import ListedColormap
from matplotlib.patches import Patch

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'docs/research/ore-heatmaps-v1'
(OUT/'maps').mkdir(exist_ok=True,parents=True)
runtime=ROOT/'assets/earth/config/ore-heatmaps-v1.json'
data=json.loads(runtime.read_text())
results=json.loads((OUT/'core-test-results.json').read_text())
defaults=results['defaultTuning']
W,H=data['width'],data['height'];N=W*H
with np.load(ROOT/'preview/grids.npz') as p:
    f=p['flags']
    rows=np.minimum(f.shape[0]-1,((np.arange(H)+.5)*f.shape[0]/H).astype(int))
    cols=np.minimum(f.shape[1]-1,((np.arange(W)+.5)*f.shape[1]/W).astype(int))
    ocean=((f[np.ix_(rows,cols)]&1)>0).astype(np.uint8)
plt.rcParams.update({'font.family':'DejaVu Sans','font.size':10,'axes.titleweight':'bold'})
cmap=plt.get_cmap('magma')
lut=(cmap(np.linspace(0,1,256))[:,:3]*255).astype(np.uint8).tolist()

for layer in data['layers']:
    raw=gzip.decompress(base64.b64decode(layer['data']))
    assert len(raw)==N*5
    planes=np.frombuffer(raw[:3*N],dtype=np.uint8).reshape(3,H,W)/255
    depths=np.frombuffer(raw[3*N:],dtype='<u2').reshape(H,W)
    heat=np.maximum(np.maximum(planes[0],planes[1]),np.where(depths<=100*defaults['MetresPerBlock'],planes[2],0))
    fig,ax=plt.subplots(figsize=(12,6.5),layout='constrained')
    ax.imshow(ocean,extent=(-180,180,-90,90),cmap=ListedColormap(['#e7e8e2','#f7fafc']),vmin=0,vmax=1,interpolation='nearest')
    im=ax.imshow(np.ma.masked_where(heat==0,heat),extent=(-180,180,-90,90),vmin=0,vmax=1,cmap=cmap,interpolation='nearest')
    ax.set(xlim=(-180,180),ylim=(-65,85),xlabel='Longitude',ylabel='Latitude',title=f"{layer['label']} · relative evidence weight at 100 blocks below surface")
    ax.grid(alpha=.2);fig.colorbar(im,ax=ax,shrink=.7,label='Authored weight (0–1), not measured abundance')
    fig.text(.02,.02,f"{data['datasetId']} · blank = no evidence or prior; suitable hosts still receive the configurable background chance.\n0.5° cells; tiered point kernels 25–75 km; authored best guesses (not survey data) ≤0.85, 40–150 km; priors ≤0.25 (potential plane). Coverage remains uneven.",fontsize=9)
    fig.savefig(OUT/'maps'/f"{layer['code']}.png",dpi=140);plt.close(fig)

contact=json.loads((OUT/'geology-contact-sample.json').read_text())
palette=['#4477aa','#ee9955','#66aa77','#aa66aa','#c4aa66','#66bbbb']
colors=ListedColormap(palette[:len(contact['profiles'])])
fig,axes=plt.subplots(1,2,figsize=(11,6.5))
for ax,key,title in zip(axes,['before','after'],['Previous categorical boundary','Coherent transition · 96-block half-width']):
    a=np.array(contact[key]).reshape(contact['size'],contact['size'])
    ax.imshow(a,cmap=colors,vmin=0,vmax=len(contact['profiles'])-1,extent=(-256,256,256,-256),interpolation='nearest')
    ax.set(title=title,xlabel='World blocks from contact centre',ylabel='World blocks')
    ax.set_xticks(np.arange(-256,257,64));ax.set_yticks(np.arange(-256,257,64))
    ax.grid(alpha=.18,color='black')
fig.suptitle(f"Rock contacts from the actual geology catalogue · {contact['latitude']}°N, {contact['longitude']}°E",fontsize=14)
fig.legend(handles=[Patch(facecolor=palette[i],label=label) for label,i in contact['profiles'].items()],loc='lower center',ncol=1,fontsize=9)
fig.subplots_adjust(bottom=.18,top=.85,wspace=.3)
fig.savefig(OUT/'geology-contacts.png',dpi=160);plt.close(fig)

template=(ROOT/'tools/ore-heatmap-viewer.html').read_text(encoding='utf8')
payload={'runtime':data,'defaults':defaults,'ocean':base64.b64encode(gzip.compress(ocean.tobytes(),mtime=0)).decode(),'colors':lut,'sha256':hashlib.sha256(runtime.read_bytes()).hexdigest()}
(OUT/'index.html').write_text(template.replace('__PAYLOAD__',json.dumps(payload,separators=(',',':'))),encoding='utf8')
print(json.dumps({'maps':len(data['layers']),'viewer':str(OUT/'index.html'),'contacts':str(OUT/'geology-contacts.png')}))
