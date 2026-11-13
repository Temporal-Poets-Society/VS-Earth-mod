"""Reproducible research export. Reads the existing MRDS ZIP; never edits game assets.

Commodity evidence is not a mineral identification. Explicit ore-field matches
are exported separately. No record count is converted into a spawn probability.
"""
from pathlib import Path
from zipfile import ZipFile
from collections import Counter, defaultdict
import csv, gzip, hashlib, io, json, math, re, unicodedata

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "normalized"
OUT.mkdir(exist_ok=True)
MRDS = ROOT.parent / "copper-tin-global-2026-09-29/raw/mrds-csv.zip"

# Labels are exact comma-separated MRDS commodity terms, not substring guesses.
COMMODITIES = {
 "Fe": ("Iron", ["Iron", "Iron-Pyrite", "Iron Oxide Pigments"]),
 "Au": ("Gold", ["Gold"]), "Ag": ("Silver", ["Silver"]),
 "Pb": ("Lead", ["Lead"]), "Zn": ("Zinc", ["Zinc"]),
 "Bi": ("Bismuth", ["Bismuth"]), "Ni": ("Nickel", ["Nickel", "Nickel Laterite"]),
 "Cr": ("Chromium", ["Chromium"]), "Ti": ("Titanium", ["Titanium", "Titanium-Ilmenite", "Titanium-Rutile", "Titanium-Heavy Minerals"]),
 "Mn": ("Manganese", ["Manganese"]), "Hg": ("Mercury", ["Mercury"]),
 "W": ("Tungsten", ["Tungsten"]), "U": ("Uranium", ["Uranium"]),
 "Pt": ("Platinum and PGE evidence", ["Platinum", "PGE"]),
 "V": ("Vanadium", ["Vanadium"]), "Mo": ("Molybdenum", ["Molybdenum"]),
 "Sb": ("Antimony", ["Antimony"]),
 "B": ("Boron / borates", ["Boron-Borates"]),
 "P": ("Phosphates", ["Phosphorus-Phosphates"]),
 "N": ("Nitrates", ["Nitrogen-Nitrates"]),
 "K": ("Potassium / potash", ["Potassium", "Potash"]),
 "NaCl": ("Salt / halite", ["Salt", "Halite"]),
 "S": ("Sulfur, including sulfide feed", ["Sulfur", "Sulfur-Pyrite", "Pyrite"]),
 "C_graphite": ("Graphite", ["Graphite"]),
 "C_coal": ("Coal, all ranks", ["Coal", "Lignite", "Anthracite", "Bituminous", "Subbituminous"]),
 "F": ("Fluorite", ["Fluorine-Fluorite"]),
 "SiO2": ("Quartz / silica", ["Quartz", "Silica"]),
 "Al": ("Aluminium evidence; bauxite not assured", ["Aluminum"]),
 "gypsum": ("Gypsum / anhydrite", ["Gypsum-Anhydrite"]),
 "diamond": ("Diamond", ["Diamond"]),
 "corundum": ("Corundum, including ruby / sapphire", ["Corundum", "Ruby", "Sapphire", "Emery"]),
 "emerald": ("Emerald", ["Emerald"]),
 "olivine": ("Olivine; gem grade not assured", ["Olivine"]),
 "garnet": ("Garnet; species / gem grade not assured", ["Garnet"]),
 "lapis": ("Lapis lazuli", []), "alum": ("Alum / alunite evidence", []),
 "topaz": ("Topaz; colour / gem grade not assured", []),
 "tourmaline": ("Tourmaline; species / gem grade not assured", []),
 "beryl": ("Beryl; colour / gem grade not assured", []),
 "spinel": ("Spinel; colour / gem grade not assured", []),
}

# Explicit matches against MRDS 'ore', never inferred from an element or gangue.
# These are mineral mentions, not proof of recoverable grade or game processing.
MINERALS = {
 "hematite": ("Fe", r"\bhematite\b|\bhaematite\b"),
 "magnetite": ("Fe", r"\bmagnetite\b"),
 "limonite": ("Fe", r"\blimonite\b"),
 "goethite": ("Fe", r"\bgoethite\b"),
 "bogiron": ("Fe", r"\bbog[ -]iron\b|\bbog[ -]ore\b"),
 "nativegold": ("Au", r"\bnative gold\b"),
 "nativesilver": ("Ag", r"\bnative silver\b"),
 "galena": ("Pb", r"\bgalena\b"),
 "cerussite": ("Pb", r"\bcerussite\b"),
 "sphalerite": ("Zn", r"\bsphalerite\b"),
 "smithsonite": ("Zn", r"\bsmithsonite\b"),
 "hemimorphite": ("Zn", r"\bhemimorphite\b"),
 "bismuthinite": ("Bi", r"\bbismuthinite\b"),
 "pentlandite": ("Ni", r"\bpentlandite\b"),
 "chromite": ("Cr", r"\bchromite\b"),
 "ilmenite": ("Ti", r"\bilmenite\b"),
 "rutile": ("Ti", r"\brutile\b"),
 "rhodochrosite": ("Mn", r"\brhodochrosite\b"),
 "cinnabar": ("Hg", r"\bcinnabar\b"),
 "wolframite": ("W", r"\bwolframite\b"),
 "scheelite": ("W", r"\bscheelite\b"),
 "uraninite": ("U", r"\buraninite\b|\bpitchblende\b"),
 "nativeplatinum": ("Pt", r"\bnative platinum\b"),
 "sperrylite": ("Pt", r"\bsperrylite\b"),
 "vanadinite": ("V", r"\bvanadinite\b"),
 "wulfenite": ("Mo", r"\bwulfenite\b"),
 "freibergite": ("Ag", r"\bfreibergite\b"),
 "tetrahedrite": ("Sb", r"\btetrahedrite\b"),
 "franckeite": ("Pb", r"\bfranckeite\b"),
 "teallite": ("Pb", r"\bteallite\b"),
 "pyrite": ("S", r"\bpyrite\b"),
 "borax": ("B", r"\bborax\b|\btincal\b"),
 "kernite": ("B", r"\bkernite\b"),
 "phosphorite": ("P", r"\bphosphorite\b"),
 "apatite": ("P", r"\bapatite\b|\bfluorapatite\b"),
 "niter": ("N", r"\bniter\b|\bnitre\b|\bsaltpeter\b|\bsaltpetre\b"),
 "nitratine": ("N", r"\bnitratine\b|\bnitratite\b|\bsoda niter\b"),
 "sylvite": ("K", r"\bsylvite\b|\bsylvinite\b"),
 "halite": ("NaCl", r"\bhalite\b|\brock salt\b"),
 "sulfur": ("S", r"\bnative sul[fp]hur\b"),
 "graphite": ("C_graphite", r"\bgraphite\b"),
 "lignite": ("C_coal", r"\blignite\b|\bbrown coal\b"),
 "anthracite": ("C_coal", r"\banthracite\b"),
 "bituminouscoal": ("C_coal", r"(?<!sub)\bbituminous\b"),
 "fluorite": ("F", r"\bfluorite\b|\bfluorspar\b"),
 "quartz": ("SiO2", r"\bquartz\b"),
 "bauxite": ("Al", r"\bbauxite\b"),
 "gypsum": ("gypsum", r"\bgypsum\b"),
 "anhydrite": ("gypsum", r"\banhydrite\b"),
 "diamond": ("diamond", r"\bdiamonds?\b"),
 "corundum": ("corundum", r"\bcorundum\b"),
 "ruby": ("corundum", r"\bruby\b|\brubies\b"),
 "sapphire": ("corundum", r"\bsapphires?\b"),
 "emerald": ("emerald", r"\bemeralds?\b"),
 "olivine": ("olivine", r"\bolivine\b"),
 "peridot": ("olivine", r"\bperidot\b"),
 "garnet": ("garnet", r"\bgarnets?\b|\balmandine\b|\bandradite\b|\bgrossular\b|\bpyrope\b|\bspessartine\b|\buvarovite\b"),
 "lapislazuli": ("lapis", r"\blapis[ -]lazuli\b|\blazurite\b"),
 "alum": ("alum", r"\balum\b"),
 "alunite": ("alum", r"\balunite\b"),
 "topaz": ("topaz", r"\btopaz\b"),
 "tourmaline": ("tourmaline", r"\btourmaline\b|\bschorl\b|\brubellite\b|\belbaite\b"),
 "beryl": ("beryl", r"\bberyl\b|\baquamarine\b|\bred beryl\b|\bbixbite\b"),
 "spinel": ("spinel", r"\bspinel\b"),
}
PATTERNS = {k:re.compile(v[1], re.I) for k,v in MINERALS.items()}
SOURCE_FIELDS = ["record_id","source_id","source_feature_id","name","country","region","longitude","latitude","crs",
 "location_precision","commodity_labels","explicit_ore_minerals","source_status","deposit_style","host_geology",
 "associated_geology","ore_text","gangue_text","structure","tectonic","ore_control","occurrence_form",
 "work_type","production_chronology","grade_value","grade_unit","economic_status","early_accessibility",
 "source_quality","source_url","source_references","duplicate_candidate_group","review_status"]

def js(path, data):
 path.write_text(json.dumps(data,ensure_ascii=False,allow_nan=False,indent=2),encoding="utf-8")

def csvout(path, rows, fields):
 op=gzip.open if path.suffix==".gz" else open
 with op(path,"wt",encoding="utf-8",newline="") as f:
  w=csv.DictWriter(f,fieldnames=fields);w.writeheader();w.writerows(rows)

def normalize_name(name):
 return re.sub(r"[^a-z0-9]","",unicodedata.normalize("NFKD",name.casefold()))

def normalize():
 rows=[]; excluded=[]; links=[]; mineral_links=[]; invalid=[]; raw_subset=[]
 source_ids=set(); exact_dups=[]; source_count=0
 commodity_stats={}; aliases=defaultdict(set)
 for code,(_,labels) in COMMODITIES.items():
  for label in labels:aliases[label].add(code)
 with ZipFile(MRDS) as z:
  reader=csv.DictReader(io.TextIOWrapper(z.open("mrds.csv"),encoding="utf-8-sig")); original_fields=reader.fieldnames
  for s in reader:
   source_count+=1
   labels={x.strip() for k in ["commod1","commod2","commod3"] for x in s[k].split(",") if x.strip()}
   commodity_matches={c for label in labels for c in aliases.get(label,[])}
   explicit={m for m,pat in PATTERNS.items() if pat.search(s["ore"])}
   mineral_only={MINERALS[m][0] for m in explicit}
   codes=commodity_matches|mineral_only
   if not codes:continue
   raw_subset.append(s)
   if s["dep_id"] in source_ids:
    exact_dups.append(s["dep_id"]);continue
   source_ids.add(s["dep_id"])
   try:lon,lat=float(s["longitude"]),float(s["latitude"])
   except ValueError:invalid.append({"dep_id":s["dep_id"],"reason":"missing or invalid coordinates"});continue
   if not (math.isfinite(lon) and math.isfinite(lat) and -180<=lon<=180 and -90<=lat<=90):
    invalid.append({"dep_id":s["dep_id"],"reason":"coordinate out of range"});continue
   style="; ".join(filter(None,[s["dep_type"],s["model"]]))
   r=dict.fromkeys(SOURCE_FIELDS)
   r.update(record_id="mrds:"+s["dep_id"],source_id="usgs_mrds_digest",source_feature_id=s["dep_id"],
    name=s["site_name"] or None,country=s["country"] or None,region=s["region"] or None,
    longitude=lon,latitude=lat,crs="EPSG:4326",location_precision="Source coordinates retained; positional accuracy not quantified",
    commodity_labels=";".join(sorted(codes)),explicit_ore_minerals=";".join(sorted(explicit)),source_status=s["dev_stat"] or None,
    deposit_style=style or None,host_geology="; ".join(filter(None,[s["hrock_unit"],s["hrock_type"]])) or None,
    associated_geology="; ".join(filter(None,[s["arock_unit"],s["arock_type"]])) or None,
    ore_text=s["ore"] or None,gangue_text=s["gangue"] or None,structure=s["structure"] or None,tectonic=s["tectonic"] or None,
    ore_control=s["ore_ctrl"] or None,occurrence_form="secondary_explicit_in_source" if re.search(r"\b(placer|alluvial|eluvial)\b",style,re.I) else None,
    work_type=s["work_type"] or None,production_chronology="; ".join(k+"="+s[k] for k in ["yfp_ba","yr_fst_prd","ylp_ba","yr_lst_prd","prod_yrs"] if s[k]) or None,
    source_quality=s["score"] or None,source_url=s["url"],source_references=s["ref"] or None,
    review_status="Imported source; mineral mentions and commodity identity require deposit review; not generation-ready")
   if s["dev_stat"].casefold()=="plant":
    excluded.append(r);continue
   rows.append(r)
   for code in sorted(codes):
    links.append({"record_id":r["record_id"],"commodity":code,"evidence_basis":"reported_commodity" if code in commodity_matches else "explicit_ore_mineral_only",
     "matching_source_commodity_labels":";".join(sorted(label for label in labels if code in aliases.get(label,[]))) or None,
     "explicit_matching_minerals":";".join(sorted(m for m in explicit if MINERALS[m][0]==code)) or None})
   for m in sorted(explicit):mineral_links.append({"record_id":r["record_id"],"mineral":m,"evidence_basis":"explicit_ore_field_mention","review_status":"Mineral occurrence, not proof of mineable or gem grade"})
 # Source IDs are unique; possible real-world duplicate workings are retained.
 groups=defaultdict(list)
 for r in rows:
  name=normalize_name(r["name"] or "")
  if name not in ["","unknown","unnamed","unnamedprospect","unnamedmine"]:
   groups[(name,r["country"],r["longitude"],r["latitude"])].append(r)
 duplicate_groups=[]
 for key,items in groups.items():
  if len(items)<2:continue
  group="same-name-coordinate:"+hashlib.sha256(repr(key).encode()).hexdigest()[:16]
  for r in items:r["duplicate_candidate_group"]=group
  duplicate_groups.append({"candidate_group":group,"records":[r["record_id"] for r in items],"basis":"Exact normalized name, country and unchanged coordinates; NOT a confirmed unique deposit"})
 csvout(OUT/"mrds-source-subset.csv.gz",raw_subset,original_fields)
 csvout(OUT/"mrds-records.csv.gz",rows,SOURCE_FIELDS)
 csvout(OUT/"mrds-excluded-plants.csv.gz",excluded,SOURCE_FIELDS)
 csvout(OUT/"record-commodities.csv.gz",links,list(links[0]))
 csvout(OUT/"record-explicit-minerals.csv.gz",mineral_links,list(mineral_links[0]))
 js(OUT/"duplicate-candidates.json",duplicate_groups)
 byid={r["record_id"]:r for r in rows}; bycommodity=defaultdict(list);bymineral=defaultdict(list)
 for link in links:bycommodity[link["commodity"]].append(link)
 for link in mineral_links:bymineral[link["mineral"]].append(link["record_id"])
 coverage=[];country_rows=[];presence=[]
 for code,(label,labels) in COMMODITIES.items():
  joined=[{**byid[l["record_id"]],**{k:v for k,v in l.items() if k!="record_id"}} for l in bycommodity[code]]
  csvout(OUT/(code+"-occurrences.csv.gz"),joined,SOURCE_FIELDS+["commodity","evidence_basis","matching_source_commodity_labels","explicit_matching_minerals"])
  features=[{"type":"Feature","id":r["record_id"]+":"+code,"geometry":{"type":"Point","coordinates":[r["longitude"],r["latitude"]]},
   "properties":{k:r[k] for k in ["record_id","name","country","commodity","evidence_basis","explicit_matching_minerals","source_status","source_quality","source_url","duplicate_candidate_group"]}} for r in joined]
  with gzip.open(OUT/(code+"-occurrences.geojson.gz"),"wt",encoding="utf-8") as f:json.dump({"type":"FeatureCollection","name":label+" evidence; no abundance inference","features":features},f,ensure_ascii=False,allow_nan=False,separators=(",",":"))
  countries=Counter(r["country"] or "unknown" for r in joined);cells=defaultdict(list)
  for r in joined:cells[(min(359,int(math.floor(r["longitude"]+180))),min(179,int(math.floor(r["latitude"]+90))))].append(r)
  for (x,y),cellrows in cells.items():
   presence.append({"commodity":code,"west":x-180,"south":y-90,"cell_degrees":1,"observed_presence":1,"source_records":len(cellrows),"display_only":True,"meaning":"At least one record within cell; not a mineralized footprint or generation probability"})
  for country,n in countries.items():country_rows.append({"commodity":code,"country":country,"source_records":n,"record_share":n/len(joined) if joined else None})
  coverage.append({"commodity":code,"label":label,"source_records":len(joined),"country_labels":len(countries),"us_records":countries["United States"],
   "us_record_share":countries["United States"]/len(joined) if joined else None,
   "reported_commodity_records":sum(r["evidence_basis"]=="reported_commodity" for r in joined),"explicit_mineral_only_records":sum(r["evidence_basis"]=="explicit_ore_mineral_only" for r in joined),
   "explicit_mineral_records":sum(bool(r["explicit_matching_minerals"]) for r in joined),"presence_cells_1_degree":len(cells),
   "records_in_duplicate_candidate_groups":sum(bool(r["duplicate_candidate_group"]) for r in joined),"confirmed_unique_deposits":None,
   "grade_values":0,"reviewed_early_access_records":0,"global_completeness":"Not globally complete; heterogeneous coverage"})
  commodity_stats[code]={"source_status_counts":dict(Counter(r["source_status"] for r in joined)),"country_counts":dict(countries)}
 csvout(OUT/"commodity-coverage.csv",coverage,list(coverage[0]))
 csvout(OUT/"country-coverage.csv",sorted(country_rows,key=lambda r:(r["commodity"],-r["source_records"])),list(country_rows[0]))
 csvout(OUT/"evidence-presence-cells.csv.gz",presence,list(presence[0]))
 js(ROOT/"commodity-catalogue.json",[{"code":c,"label":v[0],"mrds_labels":v[1],"explicit_ore_mineral_patterns":{m:p for m,(com,p) in MINERALS.items() if com==c}} for c,v in COMMODITIES.items()])
 js(ROOT/"explicit-mineral-coverage.json",{m:{"parent_commodity":v[0],"explicit_ore_mentions":len(bymineral[m]),"confirmed_unique_deposits":None,"pattern":v[1]} for m,v in MINERALS.items()})
 js(ROOT/"validation-summary.json",{"raw_mrds_records_scanned":source_count,"matching_raw_records":len(raw_subset),"retained_unique_source_records":len(rows),"commodity_evidence_links":len(links),
  "explicit_mineral_links":len(mineral_links),"excluded_processing_plants":len(excluded),"duplicate_source_ids":exact_dups,"invalid_coordinates":invalid,
  "same_name_coordinate_duplicate_candidate_groups":len(duplicate_groups),"confirmed_unique_deposits":None,"commodities":commodity_stats,
  "grade_values":0,"all_source_coordinates_unchanged":True,"historical_chronology_not_ancient_extraction":True,
  "mrds_input_file":str(MRDS.relative_to(ROOT.parent)).replace("\\","/"),"mrds_input_sha256":hashlib.sha256(MRDS.read_bytes()).hexdigest()})
 assert len(byid)==len(rows)
 assert len({(r["record_id"],r["commodity"]) for r in links})==len(links)
 assert all(r["grade_value"] is None and r["economic_status"] is None and r["early_accessibility"] is None for r in rows)
 print(json.dumps({"unique_source_records":len(rows),"commodity_links":len(links),"excluded_plants":len(excluded),"maps":len(COMMODITIES),"duplicate_candidate_groups":len(duplicate_groups)}),flush=True)

if __name__=="__main__":normalize()
