"""Create an offline comparison report from original server PNGs; image pixels are unchanged."""
import argparse
import base64
import html
import json
import re
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument("matrix",type=Path)
parser.add_argument("output",type=Path)
args=parser.parse_args()
root=Path(__file__).resolve().parent.parent
fixtures=json.loads((root/"tests/fixtures/mountain-sites.json").read_text(encoding="utf-8"))
rows=json.loads(args.matrix.read_text(encoding="utf-8-sig"))
labels={"A":"Current defaults","B":"Sea 128, current curve","C":"2000 m curve","D":"6000 m curve","E":"Linear","F":"Height-adaptive curve","G":"Sea 110","H":"Half detail","I":"Local alpine ecology","J":"Physical rock slope","K":"Combined experiment"}
available_cases=sorted({row["Case"] for row in rows})
first_mods=Path(rows[0]["RunPath"])/"Mods"
version=json.loads((first_mods/"earthmap/modinfo.json").read_text(encoding="utf-8-sig"))["version"]
cylinder_names=[p.name for p in first_mods.glob("cylinderworld*.zip")]
vegetation="optional vegetation providers" if any(first_mods.glob("floralzones*.zip")) else "vanilla vegetation providers"
metadata=f"Earth {version} · {', '.join(cylinder_names)} · {vegetation} · seed 386217 · width 512000. Other test factors are listed in the playtest plan."
by_position={(round(s["latitude"],6),round(s["longitude"],6)):s for s in fixtures}
data=[]
for row in rows:
    for line in row["SiteSummaries"]:
        point=re.match(r"(-?[\d.]+),(-?[\d.]+) at .*top blocks: ([^;]+); image (.+)",line)
        if not point:
            continue
        lat,lon,cover,path=point.groups()
        fixture=by_position[(round(float(lat),6),round(float(lon),6))]
        png=Path(path.strip()).read_bytes()
        label="Source terrain (detail off)" if row["Case"]=="H" and row.get("Detail")=="off" else labels[row["Case"]]
        data.append(dict(site=fixture["name"],continent=fixture["continent"],case=row["Case"],label=label,height=row["Height"],repeat=row["Repeat"],cover=cover,image="data:image/png;base64,"+base64.b64encode(png).decode("ascii")))
if len(data)!=len(rows)*len(fixtures):
    raise ValueError("Not every expected site has its original rendered image")
case_controls="".join(f'<label><input type="checkbox" value="{case}" {"checked" if case in "ACFHIK" else ""}> <b>{case}</b> {html.escape(labels[case] if case!="H" else "Terrain detail variant")}</label>' for case in available_cases)
payload=json.dumps(data,separators=(",",":")).replace("<","\\u003c")
fixture_payload=json.dumps(fixtures).replace("<","\\u003c")
text='''<!doctype html>
<html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Earth mountain terrain comparisons</title>
<style>
body{font:15px/1.5 system-ui,sans-serif;color:#202c38;background:#f5f6f8;margin:0}main{max-width:1400px;margin:auto;padding:28px}h1{font-size:28px;margin:0 0 8px}h2{font-size:20px;margin:0}p{max-width:1000px}aside{background:#fff7df;padding:12px 16px;border-left:4px solid #a97800;border-radius:4px}fieldset{border:1px solid #bbc4ce;background:white;border-radius:6px;padding:12px 16px;margin:18px 0}label{display:inline-block;margin:5px 16px 5px 0}select{font:inherit;margin-left:6px;padding:3px}.cases{display:flex;gap:16px;overflow-x:auto;padding:12px 0}.card{background:white;border:1px solid #c6cdd5;border-radius:6px;padding:10px;min-width:180px;width:180px}.card img{image-rendering:pixelated;width:180px;height:180px;background:#ddd}.card p{font-size:12px;margin:6px 0 0}.card strong{display:block;margin-bottom:6px}section{margin:24px 0;padding-bottom:12px;border-bottom:1px solid #d5dbe3}.sub{color:#536579;font-size:13px}.empty{color:#536579;padding:12px}button{font:inherit;margin-right:8px;padding:4px 10px}
</style><main>
<h1>Earth mountain terrain comparisons</h1>
<p>WORLD_COUNT fresh worlds, SITE_COUNT locations in each, heights 384 and 512. The original server surface maps are embedded unchanged so this file works offline.</p>
<aside><b>These are overhead surface maps, not first-person skyline previews.</b> Each map covers 96 × 96 game columns. Percentages describe the full footprint, including lower slopes; they do not measure a summit's exact treeline. The user still needs to judge mountain silhouettes and travel in game.</aside>
<p>Compare geometry first, then ecology and rock exposure independently. Case K is a combined experiment, not a selected default: its physical-slope rule retains much more soil on several mountains and turns much St Helena rock into sand.</p>
<fieldset><legend>View</legend><label>World height <select id="height"><option>512</option><option>384</option></select></label><label>Location <select id="site"><option value="all">All locations</option></select></label><button id="all">All cases</button><button id="shortlist">Shortlist</button></fieldset>
<fieldset id="controls"><legend>Cases</legend>CASE_CONTROLS</fieldset>
<div id="results"></div>
<p class="sub">Branch codex/mountain-height-playtest · Vintage Story 1.22.7 · BUILD_METADATA</p>
</main><script>
const data=PAYLOAD, fixtures=FIXTURES, labels=LABELS;
const siteSelect=document.getElementById('site');
for(const fixture of fixtures){let option=document.createElement('option');option.value=fixture.name;option.textContent=fixture.name+' — '+fixture.continent;siteSelect.append(option)}
function render(){
 const height=Number(document.getElementById('height').value), chosen=new Set([...document.querySelectorAll('#controls input:checked')].map(input=>input.value)), site=siteSelect.value;
 const target=document.getElementById('results');target.replaceChildren();
 for(const fixture of fixtures){if(site!=='all'&&site!==fixture.name)continue;
  const section=document.createElement('section'),heading=document.createElement('h2');heading.textContent=fixture.name+' · '+fixture.continent;section.append(heading);
  const coordinates=document.createElement('div');coordinates.className='sub';coordinates.textContent=fixture.latitude+', '+fixture.longitude+' · height '+height;section.append(coordinates);
  const cases=document.createElement('div');cases.className='cases';
  for(const item of data.filter(item=>item.site===fixture.name&&item.height===height&&chosen.has(item.case))){
   const card=document.createElement('article');card.className='card';const title=document.createElement('strong');title.textContent=item.case+' · '+item.label;card.append(title);
   const image=document.createElement('img');image.src=item.image;image.alt=fixture.name+', case '+item.case+', world height '+height;card.append(image);
   const cover=document.createElement('p');cover.textContent=item.cover;card.append(cover);cases.append(card);
  }section.append(cases);target.append(section);
 }
 if(!chosen.size){const note=document.createElement('p');note.className='empty';note.textContent='Choose at least one comparison case.';target.prepend(note)}
}
document.getElementById('height').addEventListener('change',render);siteSelect.addEventListener('change',render);document.getElementById('controls').addEventListener('change',render);
document.getElementById('all').addEventListener('click',()=>{document.querySelectorAll('#controls input').forEach(input=>input.checked=true);render()});
document.getElementById('shortlist').addEventListener('click',()=>{document.querySelectorAll('#controls input').forEach(input=>input.checked='ACFHIK'.includes(input.value));render()});render();
</script></html>'''
text=text.replace("WORLD_COUNT",str(len(rows))).replace("SITE_COUNT",str(len(fixtures))).replace("BUILD_METADATA",html.escape(metadata)).replace("CASE_CONTROLS",case_controls).replace("PAYLOAD",payload).replace("FIXTURES",fixture_payload).replace("LABELS",json.dumps(labels))
args.output.parent.mkdir(parents=True,exist_ok=True)
args.output.write_text(text,encoding="utf-8")
print(f"Wrote {len(data)} original surface images to {args.output} ({args.output.stat().st_size:,} bytes)")
