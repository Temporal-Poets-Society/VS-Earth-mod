"""Curated research checklists. Proposed mineral-system rules are not runtime masks."""
from pathlib import Path
import csv,json,re
from build_mrds import COMMODITIES,MINERALS,csvout,js
ROOT=Path(__file__).resolve().parent
BASE='https://pubs.usgs.gov/bul/b1693/html/'
MODELS={
 'Fe':('Banded iron formations; iron skarns; volcanic magnetite; weathered iron oxides; wetland iron as a separate hydrological system',['bull1h45.htm','bull971h.htm','bull86em.htm','bull7mn9.htm'],'Identify mineral and formation; bog iron must use groundwater/peat conditions, not generic bedrock iron.'),
 'Au':('Hydrothermal veins and several other primary gold systems; source-linked placers',['bull8x2r.htm','bull6945.htm','bull7m7b.htm'],'Gold need not be enclosed in quartz. Placer pathways require source and sediment history.'),
 'Ag':('Polymetallic and epithermal veins; replacement and sulfide associations',['bull7kdv.htm','bull7a0j.htm','bull7q0k.htm'],'Silver-bearing sulfides are not proof of native silver or quartz/galena-hosted game variants.'),
 'Pb':('Carbonate-hosted MVT, sediment-hosted, skarn and vein systems',['bull6mem.htm','bull5nmq.htm','bull4u7i.htm'],'Galena and weathered cerussite need different mineral/oxidation rules.'),
 'Zn':('MVT, sediment-hosted, VMS, skarn and vein systems',['bull6mem.htm','bull5nmq.htm','bull4u7i.htm'],'Sphalerite is distinct from oxidized smithsonite/hemimorphite; carbonate host alone is insufficient.'),
 'Bi':('Association with reviewed polymetallic and evolved intrusive mineral systems',['bull7kdv.htm','bull9qer.htm'],'Bismuthinite distribution needs mineral evidence; an elemental by-product report is insufficient.'),
 'Ni':('Mafic/ultramafic sulfide systems OR lateritic weathering systems',['bull46k5.htm','bull6sol.htm','bull40rt.htm'],'Never convert laterite nickel automatically into pentlandite. Weathering age and exposure matter.'),
 'Cr':('Podiform chromitite in ophiolites; stratiform chromitite in layered intrusions',['bull3lr9.htm','bull8c6q.htm'],'Not every peridotite/serpentinite block contains chromite ore.'),
 'Ti':('Fe-Ti oxide-bearing intrusions; anorthosite systems; heavy-mineral shoreline placers',['bull5vsm.htm','bull473d.htm','bull273d.htm'],'Ilmenite and rutile must be distinguished. Ordinary beach sand is not an ore deposit.'),
 'Mn':('Sedimentary, volcanic, epithermal and replacement systems',['bull7rji.htm','bull4exx.htm','bull1rqm.htm','bull3cq6.htm'],'Rhodochrosite is one manganese mineral, not a substitute for all manganese ores.'),
 'Hg':('Almaden-type, hot-spring and silica-carbonate mercury systems',['bull56g7.htm','bull4von.htm','bull69rb.htm'],'Cinnabar requires mineral-system and mineral identification.'),
 'W':('Tungsten veins and skarns',['bull8pv7.htm','bull2y9a.htm'],'Wolframite is distinct from scheelite; not all granite is fertile.'),
 'U':('Sandstone redox, unconformity, volcanic and other uranium systems',['bull1ghh.htm','bull0zw5.htm','bull2shh.htm'],'Vanilla definition exists but no standalone natural deposit JSON was found in installed assets.'),
 'Pt':('Layered intrusion reefs, mafic/ultramafic systems and derived placers',['bull75et.htm','bull4cdh.htm','bull0wc5.htm'],'PGE group evidence may not identify platinum; native platinum and sperrylite need explicit mineral rules.'),
 'V':('Fe-Ti-V intrusion associations and other reviewed vanadium systems',['bull5vsm.htm'],'Vanadinite is an oxidized lead-vanadium mineral; do not place it from every elemental V occurrence.'),
 'Mo':('Porphyry molybdenum systems and polymetallic contexts',['bull56zz.htm','bull0pwm.htm'],'Wulfenite is not molybdenite; elemental Mo occurrence alone cannot choose the GA mineral.'),
 'Sb':('Antimony and polymetallic veins',['bull40bm.htm','bull7kdv.htm'],'Tetrahedrite and silver-rich freibergite require species/chemistry and associated deposits.'),
 'P':('Marine sedimentary phosphorites; igneous phosphate; secondary phosphate sources',['bull74kl.htm','bull6nl1.htm','bull5rsj.htm'],'Apatite-bearing intrusion is not automatically sedimentary phosphorite.'),
 'Al':('Lateritic bauxite and karst bauxite',['bull21ut.htm','bull20dh.htm'],'Regional weathering history, drainage and substrate chemistry are required; aluminium is common without bauxite ore.'),
 'diamond':('Diamond-bearing pipes and source-linked placers',['bull9fhv.htm','bull2xv7.htm'],'Not every ultramafic rock or kimberlite is diamondiferous.'),
 'emerald':('Reviewed emerald-bearing vein systems',['bull51tf.htm'],'Beryl/beryllium evidence does not establish emerald or gem quality.'),
 'alum':('Alunite alteration or separately documented alum-bearing shale feedstocks',['bull0vxx.htm'],'Manufactured alum from shale is not a deposit of native alum crystals.'),
}
GAPS={
 'B':'Specific borate basin chemistry, evaporative setting and thermal/diagenetic history; borax and kernite cannot be selected from generic boron records.',
 'N':'Separate arid natural nitrate accumulations, cave-associated nitrates and historical manufactured saltpeter. Generic N occurrence does not prove potassium nitrate.',
 'K':'Expert potash basin tracts acquired, but salt beds versus brines, minerals and depth remain distinct. No unrestricted global eligibility.',
 'NaCl':'Halite/rock-salt layers, saline basins and surface brines are distinct; potash tracts are useful context but not a complete halite footprint.',
 'S':'Native sulfur, sulfide minerals and industrial recovered sulfur are distinct. Volcanic and sedimentary sulfur systems need separate regional models.',
 'C_graphite':'Graphite-bearing metamorphic or vein systems require carbon-rich protolith/metamorphic context and flake/amorphous/lump distinctions.',
 'C_coal':'Coal-bearing stratigraphic formations, basin history and coalification/rank; modern peat and generic sedimentary rocks are insufficient.',
 'F':'Fluorite veins/replacement and selected igneous associations require regional hydrothermal evidence; no blanket limestone eligibility.',
 'SiO2':'Quartz veins, pegmatites, silica sands and ordinary rock-forming quartz have different useful material forms.',
 'gypsum':'Evaporitic sulfate units; gypsum versus anhydrite depends on geological/dehydration history, not modern rainfall alone.',
 'corundum':'Specific metamorphic/igneous corundum sources and derived placers; aluminium and ordinary corundum do not establish ruby/sapphire quality.',
 'olivine':'Suitable mafic/ultramafic mineralogy may support ordinary olivine; gem peridot requires additional evidence.',
 'garnet':'Metamorphic/skarn/igneous systems by garnet species and chemistry, then possible derived placers; gem grade not implied.',
 'lapis':'Specific lazurite-bearing metamorphic carbonate systems; generic limestone or bauxite does not establish lapis.',
 'topaz':'Reviewed evolved intrusive or volcanic systems; colour variants and gem grade cannot be inferred from granite alone.',
 'tourmaline':'Boron-bearing intrusive/metamorphic systems; tourmaline species and colour are not interchangeable.',
 'beryl':'Reviewed beryl-bearing pegmatitic or other systems; aquamarine/red beryl are not equivalent to generic beryl.',
 'spinel':'Specific metamorphic/igneous gem sources and derived placers; ordinary spinel occurrence does not establish red gem spinel.',
}

def main():
 model_rows=[]
 for c,(label,_) in COMMODITIES.items():
  if c in MODELS:
   model,refs,note=MODELS[c];urls=[BASE+r for r in refs];status='Named primary model references identified; province/local eligibility not delineated'
  else:model='Model and regional review pending';note=GAPS[c];urls=[];status='Evidence gap; conceptual checklist only, not a reviewed permissive layer'
  if c=='K':urls=['https://pubs.usgs.gov/sir/2010/5090/s/'];status='Expert polygons acquired; 7 invalid geometries withheld pending review; depth/mineral interpretation required'
  model_rows.append({'commodity':c,'label':label,'mineral_system_candidates':model,'source_urls':urls,'interpretation_limit':note,
   'spatial_permissive_layer':'normalized/PotashTracts.geojson' if c=='K' else None,
   'low_chance_background_allowed_now':False,'reason':'Future Earth generation must first validate regional process, compatible host and source-scale limitations; these research maps do not authorize runtime probabilities',
   'readiness':status})
 js(ROOT/'permissiveness-review-catalogue.json',model_rows)
 audit=json.loads((ROOT/'game-asset-audit.json').read_text());scope=[]
 lookup={k:v[0] for k,v in MINERALS.items()}
 lookup.update({'nativecopper':'Cu','surfacecopper':'Cu','malachite':'Cu','cassiterite':'Sn','surfacecassiterite':'Sn','gold':'Au','silver':'Ag','lapis':'lapis','saltpeter':'N',
  'uranium':'U','peridot':'olivine','bogironlarge':'Fe','bogironsmall':'Fe','tetrahedrite':'Sb',
  'berylaquamarine':'beryl','berylbixbite':'beryl','spinelred':'spinel','corundumruby':'corundum','corundumsapphire':'corundum',
  'azurite':'Cu','chalcocite':'Cu','chalcopyrite':'Cu'})
 for d in audit['deposits']:
  p=d['source_asset'];code=d['code'] or ''
  if not any(k in p for k in ['/metalore/','/mineralore/','/gem/','/coal.json','/other/bogiron.json']):continue
  com=lookup.get(code)
  for prefix,cat in [('garnet','garnet'),('topaz','topaz'),('tourmaline','tourmaline')]:
   if code.startswith(prefix):com=cat
  scope.append({'provider':d['provider'],'deposit_code':code,'commodity_layer':com,'source_asset':p,'parent_deposit':d['parent_deposit'],
   'natural_deposit_asset_present':True,'evidence_status':'Existing copper/tin package; not altered here' if com in ['Cu','Sn'] else 'Broad commodity evidence exported; exact mineral evidence separate',
   'runtime_generation_implemented_in_this_package':False})
 for code,com in [('uranium','U'),('wolframite','W'),('corundum','corundum')]:
  scope.append({'provider':'vanilla','deposit_code':code,'commodity_layer':com,'source_asset':'worldproperties/block/ore-graded.json' if code!='corundum' else 'worldproperties/block/ore-ungraded.json',
   'parent_deposit':None,'natural_deposit_asset_present':False,'evidence_status':'Ore variant declared, but no standalone natural deposit JSON found; do not claim active generation',
   'runtime_generation_implemented_in_this_package':False})
 assert all(s['commodity_layer'] is not None for s in scope),[s for s in scope if s['commodity_layer'] is None]
 csvout(ROOT/'game-mineral-scope.csv',scope,list(scope[0]))
 historical=[
  {'id':'ngwenya','commodity':'Fe','place':'Ngwenya, Eswatini','period':'Prehistoric pigment extraction; later iron metallurgy','evidence':'State heritage nomination distinguishes hematite/specularite ochre extraction from later iron-ore mining. Do not treat the earliest pigments as smelted iron.', 'source_urls':['https://whc.unesco.org/en/tentativelists/5421/','https://pmc.ncbi.nlm.nih.gov/articles/PMC11502791/']},
  {'id':'las-medulas','commodity':'Au','place':'Las Medulas, Spain','period':'Roman, first to early third centuries CE','evidence':'Hydraulic washing of gold-bearing conglomerate documents a secondary source and a former landscape changed by mining. Heritage boundaries include tailings/infrastructure and are not an ore-body outline.','source_urls':['https://whc.unesco.org/en/list/803']},
  {'id':'iwami-ginzan','commodity':'Ag','place':'Iwami Ginzan, Japan','period':'Sixteenth to twentieth centuries','evidence':'Mine, processing and transport remains document extraction; does not prove native silver as the mineral or Bronze-Age accessibility.','source_urls':['https://whc.unesco.org/en/list/1246']},
  {'id':'almaden','commodity':'Hg','place':'Almaden, Spain','period':'Ancient through modern mining history','evidence':'Long-lived mercury extraction documented by the heritage assessment. Exact source mineral/footprint requires geological record joining.','source_urls':['https://whc.unesco.org/en/decisions/4810']},
  {'id':'idrija','commodity':'Hg','place':'Idrija, Slovenia','period':'Medieval through modern mining history','evidence':'Historical mercury mining; keep separate from locations where mercury was used to process imported silver/gold.','source_urls':['https://whc.unesco.org/en/decisions/4810']},
  {'id':'hallstatt','commodity':'NaCl','place':'Hallstatt, Austria','period':'Bronze and Iron Age underground salt extraction','evidence':'Excavating museum reports salt under roughly 30m of salt-free cover; historical extraction does not mean surface halite. Earlier brine collection is plausible but not directly demonstrated there.','source_urls':['https://www.nhm-wien.ac.at/hallstatt/en/salt_mine/beginnings','https://www.nhm-wien.ac.at/hallstatt/fundort']},
  {'id':'wieliczka-bochnia','commodity':'NaCl','place':'Wieliczka and Bochnia, Poland','period':'Thirteenth century onwards','evidence':'Historical rock-salt mining in a shared geological deposit; natural extent and accessible depth remain to be mapped.','source_urls':['https://whc.unesco.org/en/list/0032/']},
  {'id':'peak-alum','commodity':'alum','place':'Peak works, North Yorkshire, England','period':'Post-medieval / early industrial','evidence':'Alum was manufactured from quarried shale by calcination and chemical processing. This is feedstock evidence, not a natural deposit of game alum crystals.','source_urls':['https://historicengland.org.uk/listing/the-list/list-entry/1018146']},
 ]
 for r in historical:r.update(geometry=None,spatial_status='No validated extraction-footprint geometry imported',early_accessibility=None,generation_ready=False)
 js(ROOT/'historical-evidence.json',historical)
 print(json.dumps({'scope_rows':len(scope),'permissiveness_review_rows':len(model_rows),'historical_cases':len(historical)}),flush=True)

if __name__=='__main__':main()
