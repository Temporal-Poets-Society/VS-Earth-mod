"""Normalize acquired specialist tables while retaining originals and source references.

No guessed minerals, zero-grade imputation, or automatic economic/access ranking.
"""
from pathlib import Path
from zipfile import ZipFile
from collections import defaultdict,Counter
import csv,gzip,io,json,math,re,xml.etree.ElementTree as ET
from build_mrds import COMMODITIES,MINERALS,PATTERNS,js,csvout,normalize_name
ROOT=Path(__file__).resolve().parent
# 2026-10-02: copper, tin and clay are runtime layers too (Cu/Sn come from the copper-tin package, clay from
# sediment classes). Specialist Cu/Sn/clay links were previously intersected away because they are not MRDS
# COMMODITIES keys; they are now kept so the bake can read Cu-/Sn-/clay-specialist.geojson.
EXTRA_LAYERS={'Cu':'Copper','Sn':'Tin','clay':'Clay (kaolin, bentonite, common clay)'}
TARGETS=set(COMMODITIES)|set(EXTRA_LAYERS)
LAYER_LABELS={**{k:v[0] for k,v in COMMODITIES.items()},**EXTRA_LAYERS}
RAW=ROOT/'raw';OUT=ROOT/'normalized'
ALIASES={'phosphate':'P','apatite':'P','PGE':'Pt','salt':'NaCl','potash':'K','sylvite':'K','graphite':'C_graphite',
 'fluorspar':'F','fluorite':'F','silica':'SiO2','silica sand':'SiO2','quartz':'SiO2','amethyst':'SiO2',
 'sapphire':'corundum','ruby':'corundum','aquamarine':'beryl','nitrate':'N','lapis lazuli':'lapis',
 'coal':'C_coal','subbituminous':'C_coal','magnetite':'Fe','ochre':'Fe','anhydrite':'gypsum',
 'rhodochrosite':'Mn','alunite':'alum','peridot':'olivine','pyrite':'S','bauxite':'Al','diamonds':'diamond',
 'rutile':'Ti','ilmenite':'Ti','kaolinite':'clay','kaolin':'clay','bentonite':'clay','ball clay':'clay','fire clay':'clay'}

def tables(code):
 result={}
 with ZipFile(next(RAW.glob(code+'-*-csv.zip'))) as z:
  for name in z.namelist():
   if not name.endswith('.csv'):continue
   data=z.read(name)
   try:text=data.decode('utf-8-sig')
   except UnicodeDecodeError:text=data.decode('cp1252')
   result[Path(name).stem]=list(csv.DictReader(io.StringIO(text)))
 return result

def grouped(rows,key,val='value'):
 d=defaultdict(list)
 for r in rows:d[r[key]].append(r[val])
 return d

def positive(v):
 try:x=float(v)
 except (ValueError,TypeError):return None
 return x if math.isfinite(x) and x>0 else None

def normalize():
 records=[];links=[];grades=[];excluded=[];validation={};source_inventory=[]
 for code in ['major-deposits','nicrpge','phosphate','sedexmvt','laterite','podchrome','vms']:
  t=tables(code);main=t.get('main',t.get('deposit'));primary_key={'major-deposits':'gid','nicrpge':'site_id','phosphate':'rec_no'}.get(code,'rec_id')
  min_table=t.get('mineral',t.get('minerals',[])); minerals=grouped(min_table,primary_key) if min_table else {}
  commodities=grouped(t['commodity'],primary_key) if 'commodity' in t else {}
  hosts=grouped(t['host_rock'],primary_key) if 'host_rock' in t else {}
  refs=grouped(t['reference'],primary_key,'reference' if code=='sedexmvt' else 'value') if 'reference' in t else {}
  if code in ['laterite','podchrome']:refs=grouped(t['ref'],primary_key)
  counts=Counter();excluded_types=Counter()
  for s in main:
   rid=s[primary_key];record_id=code+':'+rid
   try:lon,lat=float(s['longitude']),float(s['latitude'])
   except (ValueError,KeyError):excluded.append({'record_id':record_id,'reason':'Missing numeric coordinates','source_record':s});counts['missing_coordinates']+=1;continue
   if not(-180<=lon<=180 and -90<=lat<=90):excluded.append({'record_id':record_id,'reason':'Out-of-range coordinates','source_record':s});counts['invalid_coordinates']+=1;continue
   rec_type=s.get('record_tp') or s.get('status') or None
   if rec_type and re.search(r'\b(plant|refinery|smelter|cross reference|handling)\b',rec_type,re.I):excluded.append({'record_id':record_id,'reason':'Processing/handling or cross-reference entry; not accepted as independent natural site','source_record':s});excluded_types[rec_type]+=1;continue
   explicit_mins=minerals.get(rid,[])
   if not explicit_mins and s.get('minerals'):explicit_mins=[s['minerals']]
   ore_text='; '.join(explicit_mins) or None
   if code in ['major-deposits','nicrpge']:codes={ALIASES.get(c,c) for c in commodities.get(rid,[])}&TARGETS
   elif code=='phosphate':codes={'P'} # All entries belong to the phosphate inventory; other commodities remain source-only.
   elif code=='sedexmvt':codes={'Pb','Zn'} # Entry in Pb-Zn deposit-model compilation does not specify galena/sphalerite.
   elif code=='laterite':codes={'Ni'} # Do not map lateritic Ni into pentlandite.
   elif code=='podchrome':codes={'Cr'}
   elif code=='vms':codes={c for c,f in [('Cu','cugrd'),('Au','augrd'),('Ag','aggrd'),('Pb','pbgrd'),('Zn','zngrd')] if positive(s.get(f)) is not None}
   exact_minerals={m for m,p in PATTERNS.items() if ore_text and p.search(ore_text)}
   if code=='vms':codes|={MINERALS[m][0] for m in exact_minerals}&{'Au','Ag','Pb','Zn','Fe','S','Bi','Ni','Sb','Mo'}
   # Unknown/zero grades do not supply a positive commodity observation.
   row={'record_id':record_id,'source_id':'usgs_'+code,'source_feature_id':rid,'name':s.get('dep_name') or s.get('depname') or s.get('site_name') or s.get('name') or s.get('location') or None,
    'country':s.get('country') or None,'longitude':lon,'latitude':lat,'crs':'EPSG:4326',
    'location_precision':s.get('accuracy') or s.get('loc_comm') or 'Source point; accuracy varies by underlying record',
    'commodity_labels':';'.join(sorted(codes)) or None,'source_commodity_labels':';'.join(commodities.get(rid,[])) or None,'source_status':rec_type,
    'evidence_class':'geochemical_anomaly_or_trace_mention' if re.search(r'\b(anomaly|trace|detected)\b',rec_type or '',re.I) else 'reported_mineral_site_or_unknown_status',
    'deposit_style':s.get('deptype') or s.get('dep_type') or s.get('class') or None,
    'host_geology':s.get('hostrock') or s.get('host_lith') or s.get('srcbedrock') or '; '.join(hosts.get(rid,[])) or None,
    'minerals':ore_text,'explicit_minerals':';'.join(sorted(exact_minerals)) or None,
    'tectonic_context':s.get('tectset') or s.get('lithotect') or None,
    'depth_source_text':s.get('oredepth') or s.get('overburden') or s.get('cover') or None,
    'grade_value':None,'economic_status':None,'early_accessibility':None,
    'source_url':'https://mrdata.usgs.gov/'+code+'/',
    'source_references':'; '.join(refs.get(rid,[])) or s.get('ref') or s.get('citation') or s.get('refgen') or None,
    'source_comments':s.get('comments') or s.get('descript') or None,
    'historical_period':'; '.join(k+'='+s[k] for k in ['discdate','startdate','disc_yr'] if s.get(k)) or None,
    'historical_evidence_kind':'Source chronology only; ancient extraction not established',
    'review_status':'Source imported; not independently verified or matched across databases; no generation approval',
    # Size/status attributes used only for evidence-tier weighting in tools/bake_ore_heatmaps.py.
    'ore_tonnage_mt':positive(s.get('oreton')),'production_status':s.get('prod') or None,
    'source_record_type':s.get('record_tp') or s.get('dep_type') or None}
   records.append(row);counts['source_records']+=1
   for com in sorted(codes):links.append({'record_id':record_id,'commodity':com,'evidence_basis':'specialist_source_commodity_or_positive_grade','source_id':row['source_id']})
   fields=[]
   if code=='sedexmvt':fields=[('Pb','pbgrd','wt% Pb','deposit ore average'),('Zn','zngrd','wt% Zn','deposit ore average'),('Ag','aggrd','g/t Ag','deposit ore average'),('Cu','cugrd','wt% Cu','deposit ore average')]
   if code=='vms':fields=[('Cu','cugrd','wt% Cu','deposit ore average'),('Pb','pbgrd','wt% Pb','deposit ore average'),('Zn','zngrd','wt% Zn','deposit ore average'),('Ag','aggrd','g/t Ag','deposit ore average'),('Au','augrd','g/t Au','deposit ore average')]
   if code=='podchrome':fields=[('Cr','crgrd','wt% Cr2O3','deposit chromic oxide grade; not elemental Cr'),('Pt','ptgrd','g/t Pt','deposit ore average')]
   if code=='laterite':fields=[('Ni','nigrd','wt% Ni','reserve tonnage grade'),('Ni','niprgrd','wt% Ni','production tonnage grade'),('Ni','niregrd','wt% Ni','resource tonnage grade'),('Ni','nitogrd','wt% Ni','total tonnage grade')]
   for com,field,unit,basis in fields:
    val=positive(s.get(field))
    if val is None:continue
    grades.append({'record_id':record_id,'commodity':com,'source_field':field,'source_value':s[field],'grade_value':val,'grade_unit':unit,'grade_basis':basis,'economic_viability':None,
     'source_url':row['source_url'],'limitation':'A source deposit/resource average is not a hand-selected surface-ore yield or a current reserve estimate'})
    if com not in codes:
     links.append({'record_id':record_id,'commodity':com,'evidence_basis':'positive_source_grade','source_id':row['source_id']});codes.add(com)
   row['commodity_labels']=';'.join(sorted(codes)) or None
  validation[code]={**dict(counts),'raw_main_records':len(main),'processing_types_excluded':dict(excluded_types),'source_table_names':list(t)}
  metadata=ET.parse(RAW/(code+'-metadata.xml'))
  dates=[e.text for e in metadata.findall('.//pubdate')]
  source_inventory.append({'source_id':'usgs_'+code,'title':next(iter([e.text for e in metadata.findall('.//title')]),code),
   'publication_year':dates[0] if dates else None,'source_url':'https://mrdata.usgs.gov/'+code+'/',
   'scope':'Global specialist/major-deposit compilation; not complete mineral inventory','reuse_terms':'; '.join(' '.join(e.itertext()) for e in metadata.findall('.//useconst')),
   'raw_archive':str(next(RAW.glob(code+'-*-csv.zip')).relative_to(ROOT)).replace('\\','/'),
   'normalization':'Records retained separately by source; zero/nonpositive grade withheld; relation tables remain in raw archive',
   'global_complete':False,'generation_ready':False})
 csvout(OUT/'specialist-records.csv',records,list(records[0]))
 csvout(OUT/'specialist-commodity-links.csv',links,list(links[0]))
 csvout(OUT/'specialist-grades.csv',grades,list(grades[0]))
 js(OUT/'specialist-exclusions.json',excluded)
 js(ROOT/'specialist-validation.json',validation)
 js(ROOT/'specialist-source-inventory.json',source_inventory)
 index={r['record_id']:r for r in records};countrycoverage=[];overview=[]
 for com,label in LAYER_LABELS.items():
  relevant=[index[l['record_id']] for l in links if l['commodity']==com]
  features=[{'type':'Feature','id':r['record_id']+':'+com,'geometry':{'type':'Point','coordinates':[r['longitude'],r['latitude']]},'properties':r} for r in relevant]
  js(OUT/(com+'-specialist.geojson'),{'type':'FeatureCollection','name':label+' specialist evidence; sources overlap','features':features})
  country=Counter(r['country'] or 'unknown' for r in relevant)
  overview.append({'commodity':com,'specialist_source_records':len(relevant),'country_labels':len(country),'us_records':country['United States'],'confirmed_unique_deposits':None,'positive_grade_rows':sum(g['commodity']==com for g in grades)})
  for c,n in country.items():countrycoverage.append({'commodity':com,'country':c,'specialist_source_records':n})
 csvout(OUT/'specialist-coverage.csv',overview,list(overview[0]))
 csvout(OUT/'specialist-country-coverage.csv',countrycoverage,list(countrycoverage[0]))
 # Exact names at identical coordinates flag cross-source duplicates for review.
 candidate=defaultdict(list)
 with gzip.open(OUT/'mrds-records.csv.gz','rt',encoding='utf-8') as f:
  for r in csv.DictReader(f):
   name=normalize_name(r['name'])
   if name and name not in ['unknown','unnamed','unnamedmine','unnamedprospect']:candidate[(name,r['country'],float(r['longitude']),float(r['latitude']))].append(r['record_id'])
 cross=[]
 for r in records:
  key=(normalize_name(r['name'] or ''),r['country'] or '',r['longitude'],r['latitude'])
  if key[0] and key in candidate:cross.append({'record_id':r['record_id'],'candidate_mrds_record_ids':candidate[key],'basis':'Exact normalized name, country, and unchanged coordinates; not confirmed identity'})
 js(OUT/'cross-source-duplicate-candidates.json',cross)
 assert len(index)==len(records)
 assert len({(l['record_id'],l['commodity']) for l in links})==len(links)
 print(json.dumps({'specialist_source_records':len(records),'commodity_links':len(links),'grade_rows':len(grades),'excluded':len(excluded),'exact_cross_source_candidates':len(cross),'sources':validation}),flush=True)

if __name__=='__main__':normalize()
