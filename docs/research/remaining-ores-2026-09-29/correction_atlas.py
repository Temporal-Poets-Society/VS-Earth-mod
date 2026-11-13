"""Render the coverage-audited local atlas and regional inspection sheet."""
from pathlib import Path
import html,json
ROOT=Path(__file__).resolve().parent
def write_atlas(mapaudit):
    audit=json.loads((ROOT/'corrections/resource-coverage-audit.json').read_text(encoding='utf8'))
    rows=audit['resources'];esc=html.escape
    table=''.join('<tr><th scope="row">'+esc(r['label'])+'</th><td>'+esc(r['audit_verdict'])+'</td><td>'+esc(r['known_gaps'] or 'No empty sampled controls; completeness still unproven')+'</td><td>'+esc(r['interpretation_problem'])+'</td><td><a href="corrections/'+r['detail_report']+'">Review</a></td></tr>' for r in rows)
    cards=[]
    for r in rows:
        code=r['commodity']
        if code not in mapaudit:continue
        m=mapaudit[code]
        cards.append(f'<article data-search="{esc((r["label"]+" "+code).lower())}"><h2>{esc(r["label"])}</h2><p class="status">Coverage incomplete · not ready for generation</p><a href="maps/{code}.png"><img loading="lazy" src="maps/{code}.png" alt="{esc(r["label"])} evidence and survey-area map"></a><p>{esc(r["interpretation_problem"])}</p><p><a href="corrections/{r["detail_report"]}">Resource review</a> · <a href="normalized/{code}-occurrences.geojson.gz">Baseline points</a></p><p class="small">{m["correction_points"]:,} correction point links; {m["correction_polygons"]:,} correction area links. Sources overlap; these are not unique deposits.</p></article>')
    page='''<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Earth resource coverage audit</title><style>
body{font:16px system-ui;max-width:1500px;margin:2rem auto;padding:0 1rem;background:#f4f6f7;color:#19303d}h1{font-size:2.3rem;line-height:1.15}p{max-width:1100px;line-height:1.6}a{color:#125b82}.notice{background:#fff3df;border-left:5px solid #ad6422;padding:1rem 1.3rem}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,510px),1fr));gap:1rem}article{background:white;padding:1.2rem;border:1px solid #d5dee3;border-radius:8px}img{width:100%;height:auto}h2{font-size:1.25rem}.status{font-size:.85rem;color:#865018;margin:.25rem 0}.small{font-size:.85rem;color:#536673}table{border-collapse:collapse;font-size:.9rem}td,th{text-align:left;vertical-align:top;border:1px solid #d2dce1;padding:.65rem}thead{background:#e1e9ed}input{font:inherit;padding:.6rem;margin:.3rem 0 1rem;max-width:95%;width:25rem}details{margin:2rem 0}.table-scroll{overflow:auto}summary{cursor:pointer;font-weight:600}article[hidden]{display:none}</style></head><body>
<h1>Earth resource evidence: coverage audit</h1>
<div class="notice"><strong>All 42 resource layers have been audited; none establishes global absence.</strong><p>The original maps missed major coalfields, gypsum regions and many other occurrences. This revision adds geological survey areas, historical and nonproducing deposits, and broader occurrence research. The maps remain incomplete evidence compilations, not abundance heatmaps or spawn probabilities.</p></div>
<p><strong>Read the layers separately:</strong> point cells record reported evidence; green areas are source-mapped mineral/resource areas or formations; gold outlines are contextual basins, technical zones or other areas that do not establish resource presence everywhere. A 1° point cell is a display unit, not a deposit footprint. Blank means unknown coverage. Depth, grade and economic status remain separate; deep or unprofitable deposits still count as geological evidence.</p>
<p><a href="../../resource-coverage-correction.md">Correction report and generation policy</a> · <a href="corrections/resource-coverage-audit.csv">42-resource audit CSV</a> · <a href="corrections/resource-coverage-audit.json">Audit JSON</a> · <a href="corrections/overlay-registry.json">Added spatial layers</a> · <a href="../copper-tin-preview-2026-09-29/copper-tin-evidence-preview.png">Updated copper/tin map</a></p>
<h2>Inspect the original failure regions</h2><a href="maps/regional-coverage-review.png"><img src="maps/regional-coverage-review.png" alt="European and Australian coal evidence, European gypsum and Latvian gypsum detail"></a>
<details><summary>Review every resource and its remaining gaps</summary><div class="table-scroll"><table><thead><tr><th>Resource</th><th>Audit result</th><th>Known gaps / controls</th><th>Interpretation problem</th><th>Details</th></tr></thead><tbody>'''+table+'''</tbody></table></div></details>
<label for="filter"><strong>Filter map cards</strong></label><br><input id="filter" type="search" placeholder="Coal, iron, gypsum, quartz…"><div class="grid">'''+''.join(cards)+'''</div><p class="small">Research snapshot: 29 September 2026. Source credits, rights, geometry scale and acquisition checks are in each corrections subfolder. Some legacy research sources need location and redistribution review before any runtime use. Records may refer to the same geological district; record counts never multiply abundance.</p><script>document.getElementById('filter').addEventListener('input',e=>{const q=e.target.value.trim().toLowerCase();document.querySelectorAll('article').forEach(a=>a.hidden=!a.dataset.search.includes(q));});</script></body></html>'''
    (ROOT/'index.html').write_text(page,encoding='utf8')

def regional_sheet(ocean,allmaps,corrections,base,presence,polygons):
    import matplotlib.pyplot as plt
    import numpy as np
    from matplotlib.patches import Patch
    fig,axes=plt.subplots(2,2,figsize=(16,12));fig.subplots_adjust(left=.05,right=.98,top=.86,bottom=.11,hspace=.24,wspace=.14)
    specs=[('C_coal','Coal · Europe',(-12,33,42,62)),('C_coal','Coal · Australia',(110,156,-45,-9)),('gypsum','Gypsum / anhydrite · Europe',(-12,33,35,62)),('gypsum','Gypsum · Latvia',(20.5,28.5,55.5,58.4))]
    for ax,(code,title,(x0,x1,y0,y1)) in zip(axes.flat,specs):
        base(ax,ocean);ax.set_xlim(x0,x1);ax.set_ylim(y0,y1);presence(ax,*allmaps[code]);polygons(ax,corrections.get(code,[]));ax.set_xticks(np.linspace(x0,x1,5));ax.set_yticks(np.linspace(y0,y1,4));ax.set_title(title,loc='left',pad=12)
        if title=='Gypsum · Latvia':
            # Labels point at centres of source bounds for visibility; never spawn buffers.
            for f in corrections.get(code,[]):
                p=f['properties']
                if p.get('source_id')!='latvia_cabinet321_2012':continue
                rings=f['geometry']['coordinates']
                if f['geometry']['type']=='MultiPolygon':rings=[r for poly in rings for r in poly]
                xy=np.asarray([v for ring in rings for v in ring]);x,y=(xy.min(axis=0)+xy.max(axis=0))/2
                ax.scatter([x],[y],s=30,facecolors='none',edgecolors='#246f50',linewidths=1.3,zorder=8)
                offset=(30,20) if p['name']=='Salaspils' else (30,-30)
                ax.annotate(p['name'],(x,y),xytext=offset,textcoords='offset points',fontsize=11,color='#194e38',arrowprops={'arrowstyle':'-','color':'#246f50'},zorder=9)
            ax.text(.025,.04,'Circles locate tiny mapped boundaries;\nmarker size does not show deposit extent.',transform=ax.transAxes,fontsize=9,color='#194e38')
    fig.suptitle('Coverage correction · geology survives mine closure',x=.05,y=.975,ha='left',fontsize=23,weight='bold')
    fig.text(.05,.933,'Source areas include historical, unworked and concealed resources. They do not mean continuous accessible ore.',fontsize=11)
    fig.text(.05,.908,'Gold context outlines do not establish coal/gypsum everywhere; mixed formations retain their original mineral distinctions.',fontsize=11,color='#73511d')
    fig.legend(handles=[Patch(facecolor='#bb782b',label='MRDS point cells'),Patch(facecolor='#216c8c',label='Other point cells'),Patch(facecolor='#2b865e',alpha=.45,label='Mapped evidence areas'),Patch(facecolor='none',edgecolor='#ab771f',label='Context only')],loc='lower left',bbox_to_anchor=(.045,.045),ncol=4,frameon=False,fontsize=11)
    fig.text(.05,.018,'Mixed source scales; tiny features may be invisible at this zoom. Blank areas remain unassessed. Exact source geometry/provenance retained.',fontsize=10)
    fig.savefig(ROOT/'maps/regional-coverage-review.png',dpi=150,facecolor='white');plt.close(fig)
