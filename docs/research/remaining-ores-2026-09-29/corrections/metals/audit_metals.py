"""All 20 metallic commodity layers: independent country alarms and named province probes.
Probe boxes are search windows, NEVER deposit geometries or permissive polygons.
"""
from pathlib import Path
import json,gzip,csv,collections,unicodedata,math
BASE=Path(__file__).resolve().parent;PACKAGE=BASE.parents[1];DOCS=PACKAGE.parent
MCS='https://pubs.usgs.gov/periodicals/mcs2026/mcs2026.pdf'
MAJOR='https://mrdata.usgs.gov/major-deposits/'
PP='https://doi.org/10.3133/pp1802'
GA='https://www.ga.gov.au/aimr2025/australias-identified-mineral-resources'
UDEPO='https://infcis.iaea.org/UTHDEPO/Deposits'
profiles={
'Fe':('Iron',113,'Australia|Brazil|Canada|China|India|Iran|Kazakhstan|Russia|South Africa|Sweden|Turkey|Ukraine|United States|Latvia','Banded iron formations, iron-oxide apatite, skarn, sedimentary ironstone; bog iron is a separate wetland/peat system.','Deep concealed and low-grade iron remains evidence; do not replace Staicele basement ore with shallow limonite.'),
'Au':('Gold',95,'Australia|Canada|China|Ghana|Indonesia|Kazakhstan|Mexico|Peru|Russia|South Africa|United States|Uzbekistan','Orogenic, epithermal, intrusion-related and paleoplacer systems; alluvial gold requires a source and sediment transport.','Historic/exhausted placers and districts matter even when absent from modern mine lists; native gold is not guaranteed by every elemental record.'),
'Ag':('Silver',177,'Argentina|Australia|Bolivia|Chile|China|India|Kazakhstan|Mexico|Peru|Poland|Russia|United States','Epithermal veins and Pb-Zn-Cu polymetallic systems; some sediment-hosted deposits.','Byproduct silver is not necessarily native silver or freibergite; historical workings and obsolete production must remain.'),
'Pb':('Lead',119,'Australia|Bolivia|China|India|Kazakhstan|Mexico|Peru|Russia|Sweden|Turkey|United States','MVT, sedimentary-exhalative, Irish-type, VMS, vein and skarn Pb-Zn.','Galena versus weathered cerussite requires mineral evidence; deep concealed districts cannot be promoted to surface resources.'),
'Zn':('Zinc',217,'Australia|Bolivia|Canada|China|India|Kazakhstan|Mexico|Peru|Russia|Sweden|Turkey|United States','MVT, sedimentary-exhalative, Irish-type and VMS Pb-Zn; weathering zones differ.','Elemental zinc is not proof of sphalerite, smithsonite or hemimorphite; trace zinc is not a zinc orebody.'),
'Bi':('Bismuth',59,'Bolivia|China|Vietnam','Granite-related W-Sn-Mo, intrusion-related Au and polymetallic sulfide systems.','MCS refinery countries are not local ore locations; common unreported byproduct/trace Bi makes pure commodity mapping underinclusive.'),
'Ni':('Nickel',137,'Australia|Brazil|Canada|China|Cuba|Finland|Indonesia|Madagascar|New Caledonia|Philippines|Russia|South Africa|United States','Magmatic Ni-Cu sulfides and ultramafic laterites are separate mineral systems.','Laterite cannot become pentlandite. Retain exploration showings, depleted sulfide workings and uneconomic disseminated ore.'),
'Cr':('Chromium',71,'Albania|Brazil|Finland|India|Kazakhstan|South Africa|Turkey|Zimbabwe|United States','Podiform ophiolitic chromite versus stratiform layered-intrusion chromite.','Do not mark all ultramafic rock as chromite; small pods and subeconomic disseminations differ from giant layered deposits.'),
'Ti':('Titanium',203,'Australia|Canada|China|India|Kenya|Madagascar|Mozambique|Norway|Senegal|Sierra Leone|South Africa|Ukraine|United States','Anorthosite/mafic Fe-Ti oxides, weathering and source-linked heavy-mineral sands.','Titanium minerals/concentrates not titanium sponge/refinery production; mineral sands do not identify ilmenite versus rutile.'),
'Mn':('Manganese',129,'Australia|Brazil|China|Gabon|Ghana|India|South Africa|Ukraine','Sedimentary, weathered/enriched manganese and some hydrothermal systems.','Most Mn evidence is oxide ore, not rhodochrosite; Latvia Mn-oxide assays are associated mineralization, not a separate proven Mn mine.'),
'Hg':('Mercury',131,'China|Kyrgyzstan|Mexico|Peru|Russia|Slovenia|Spain|Ukraine|United States','Low-temperature hydrothermal cinnabar in volcanic/structural belts.','A modern production filter erases Almaden/Idrija and other closed historic sources. Recycled Hg, export and byproduct recovery are not new cinnabar deposits.'),
'W':('Tungsten',205,'Australia|Austria|Bolivia|Canada|China|Kazakhstan|North Korea|Portugal|Russia|Rwanda|Spain|Vietnam|United States','Evolved intrusive greisen/veins and contact skarns.','Scheelite is not wolframite. Granite fertility, contacts, veins, depth and weathered/placer derivatives must be kept separately.'),
'U':('Uranium',None,'Argentina|Australia|Brazil|Canada|China|Czech Republic|Kazakhstan|Namibia|Niger|Russia|Ukraine|United States|Uzbekistan','Sandstone redox, unconformity-related, IOCG, alaskite and sedimentary/phosphate systems.','MCS nonfuel tables do not supply a full U inventory; UDEPO includes occurrences and low-grade/unconventional resources, but is not acquired here.'),
'Pt':('Platinum/PGE',149,'Canada|Russia|South Africa|United States|Zimbabwe','Layered mafic intrusions, Ni-Cu sulfide systems and derived placers.','PGE or Pd-only evidence is not native platinum/sperrylite; historical alluvials need their own source-to-placer evidence.'),
'V':('Vanadium',207,'Australia|Brazil|China|Russia|South Africa|United States','Titaniferous magnetite, U-V sandstone and some weathered lead vanadates.','Vanadium in oil/ash/phosphate is not vanadinite. Australia has reserves despite zero production in the cited table.'),
'Mo':('Molybdenum',135,'Armenia|Australia|Canada|Chile|China|Iran|Kazakhstan|Mexico|Mongolia|North Korea|Peru|Russia|South Korea|United States|Uzbekistan','Porphyry Mo and porphyry Cu-Mo; granite-related skarn/vein systems.','Low-grade bulk-mined porphyry presence does not prove hand-recoverable ore. Associated Mo can be missed when source commodities are primary-only.'),
'Sb':('Antimony',47,'Australia|Bolivia|Canada|China|Guatemala|Iran|Kazakhstan|Kyrgyzstan|Laos|Mexico|Myanmar|Pakistan|Russia|South Africa|Tajikistan|Turkey|Vietnam|United States','Hydrothermal Sb-Au veins, polymetallic replacements and some Pb-Zn byproducts.','Stibnite or antimony content does not prove tetrahedrite/freibergite; old workings and unmined resources remain relevant.'),
'Al':('Aluminium/bauxite',55,'Australia|Brazil|China|Greece|Guinea|India|Indonesia|Jamaica|Kazakhstan|Russia|Saudi Arabia|Turkey|Vietnam','Residual lateritic bauxite and karst bauxite; parent material, long weathering history and preservation are required.','Exclude refinery-only countries from bauxite alarms. Aluminium-bearing clay/anorthosite/alunite is real subeconomic Al evidence, not bauxite.'),
'Cu':('Copper',77,'Australia|Canada|Chile|China|Democratic Republic of the Congo|Indonesia|Kazakhstan|Mexico|Peru|Poland|Russia|United States|Zambia','Porphyry, sediment-hosted, VMS, IOCG, skarn and native-copper volcanic systems.','2015 global permissive polygons cover porphyry and sediment-hosted Cu, not all Cu systems. Native copper, oxidized ore and deep sulfide accessibility differ.'),
'Sn':('Tin',199,'Australia|Bolivia|Brazil|China|Democratic Republic of the Congo|Indonesia|Laos|Malaysia|Myanmar|Nigeria|Peru|Russia|Rwanda|Vietnam|United States|United Kingdom|Czech Republic|Germany','Tin-fertile evolved granites, greisen/veins/skarn and source-linked alluvial or coastal cassiterite.','Modern producers omit historic Cornwall/Erzgebirge and exhausted placers. PP1802 adds global Sn; GSC World Tin/Tungsten OF7688 remains a priority detailed source.')}
# Named geological witnesses, using approximate search centres (not claimed surveyed deposit coordinates).
# Official global deposit compilations, PP1802 commodity chapters and GA AIMR are source inventories.
# Latvia has its own primary university evidence and depth entry below.
probe_text={
'Fe':'Kiruna,Europe,20.2,67.9;Lorraine,Europe,6,49.3;Kryvyi Rih,Europe,33.4,47.9;Anshan,Asia,123,41.1;Pilbara,Oceania,118,-22;Carajas,South America,-50,-6;Labrador,North America,-66,54;Mesabi,North America,-92.7,47.5;Mauritania,Africa,-12.5,22.7;Simandou,Africa,-8.9,8.5;Staicele,Europe,24.75,57.84',
'Au':'Witwatersrand,Africa,27.5,-26;Ashanti,Africa,-1.5,6;Kalgoorlie,Oceania,121.5,-30.75;Victoria,Oceania,144,-37;Carlin,North America,-116.2,40.7;Abitibi,North America,-78,48;Yanacocha,South America,-78.5,-7;Muruntau,Asia,64.6,41.5;Sukhoi Log,Asia,116.5,58;Rosia Montana,Europe,23.1,46.3;Las Medulas,Europe,-6.77,42.46',
'Ag':'Fresnillo,North America,-102.9,23.2;Potosi,South America,-65.75,-19.6;Kongsberg,Europe,9.65,59.7;Kutna Hora,Europe,15.27,49.95;Iwami Ginzan,Asia,132.4,35.1;Broken Hill,Oceania,141.5,-31.95;Imiter,Africa,-5.6,31.4',
'Pb':'Missouri,North America,-91,37;Irish Midlands,Europe,-7.5,53;Silesia,Europe,19,50.4;Mount Isa,Oceania,139.5,-20.7;Broken Hill,Oceania,141.5,-31.95;Jinding,Asia,99.4,26.5;Angouran,Asia,47.4,36.6;Cerro de Pasco,South America,-76.3,-10.7;Tsumeb,Africa,17.7,-19.2;Kabwe,Africa,28.45,-14.4',
'Zn':'Red Dog,North America,-162.8,68.1;Irish Midlands,Europe,-7.5,53;Silesia,Europe,19,50.4;Mount Isa,Oceania,139.5,-20.7;Broken Hill,Oceania,141.5,-31.95;Jinding,Asia,99.4,26.5;Cerro de Pasco,South America,-76.3,-10.7;Tsumeb,Africa,17.7,-19.2',
'Bi':'Shizhuyuan,Asia,113.09,25.76;Tasna,South America,-65.8,-20.5;Nui Phao,Asia,105.6,21.6;Boliden,Europe,20.34,64.86;Tennant Creek,Oceania,134.2,-19.6;Climax,North America,-106.2,39.4',
'Ni':'Sudbury,North America,-81,46.5;Thompson,North America,-97.8,55.7;Norilsk,Asia,88.2,69.3;Pechenga,Europe,30.7,69.4;Outokumpu,Europe,29,62.7;New Caledonia,Oceania,165.8,-21.2;Soroako,Asia,121.3,-2.5;Moa,North America,-74.9,20.6;Niquelandia,South America,-48.4,-14.5;Ambatovy,Africa,48.3,-18.9;Kambalda,Oceania,121.6,-31.2',
'Cr':'Bushveld,Africa,29.4,-25.2;Great Dyke,Africa,30.5,-19;Kempirsai,Asia,58.5,50.4;Albania,Europe,20,41.5;Odisha,Asia,85.9,21;Stillwater,North America,-109.9,45.4;Kemi,Europe,24.6,65.8;Campo Formoso,South America,-40.3,-10.6;Coobina,Oceania,120.1,-23.4',
'Ti':'Tellnes,Europe,6.4,58.3;Lac Tio,North America,-63.45,50.55;Southwest WA,Oceania,115.7,-33;Murray Basin,Oceania,142,-34;Richards Bay,Africa,32,-28.8;Moma,Africa,39.6,-16.7;Pulmoddai,Asia,81,8.96;Kerala,Asia,76.5,9.4;Panzhihua,Asia,101.7,26.5;Mataraca,South America,-35.1,-6.6',
'Mn':'Kalahari,Africa,22.8,-27;Moanda,Africa,13.25,-1.56;Nikopol,Europe,34.3,47.6;Chiatura,Asia,43.3,42.3;Groote Eylandt,Oceania,136.4,-13.97;Azul,South America,-50.3,-6.1;Nagpur,Asia,79,21;New Mexico,North America,-107.8,32.3',
'Hg':'Almaden,Europe,-4.85,38.77;Idrija,Europe,14,46;Monte Amiata,Europe,11.6,42.9;Khaidarkan,Asia,71.3,39.94;Wanshan,Asia,109.2,27.5;Azzaba,Africa,7.1,36.74;Huancavelica,South America,-74.97,-12.8;New Almaden,North America,-121.82,37.18;Pulganbar,Oceania,152.6,-29.4',
'W':'South China,Asia,114,24;Nui Phao,Asia,105.6,21.6;Sangdong,Asia,128.8,37.15;Panasqueira,Europe,-7.75,40.1;Mittersill,Europe,12.5,47.28;Hemerdon,Europe,-4,50.41;Cantung,North America,-128.24,61.96;King Island,Oceania,143.85,-39.9;Rwanda,Africa,29.9,-1.9;Chojlla,South America,-67.7,-16.39',
'U':'Athabasca,North America,-107,58.2;Elliot Lake,North America,-82.65,46.4;Colorado Plateau,North America,-108.5,38;Chu Sarysu,Asia,69,44;Streltsovsk,Asia,118,50;Jachymov,Europe,12.93,50.36;Arlit,Africa,7.4,18.7;Rossing,Africa,15,-22.49;Olympic Dam,Oceania,136.89,-30.44;Ranger,Oceania,132.92,-12.68;Sierra Pintada,South America,-68.6,-34.58;Lagoa Real,South America,-42.23,-13.96',
'Pt':'Bushveld,Africa,29.4,-25.2;Great Dyke,Africa,30.5,-19;Norilsk,Asia,88.2,69.3;Sudbury,North America,-81,46.5;Stillwater,North America,-109.9,45.4;Kola,Europe,30.7,69.4;Choco,South America,-76.8,5.6;Fifield,Oceania,147.46,-32.8',
'V':'Bushveld,Africa,29.4,-25.2;Panzhihua,Asia,101.7,26.5;Kachkanar,Asia,59.4,58.7;Windimurra,Oceania,118.5,-28.4;Maracas,South America,-40.45,-13.4;Colorado Plateau,North America,-108.5,38;Mustavaara,Europe,28.5,65.9;Abenab,Africa,17.8,-19.5',
'Mo':'Climax,North America,-106.2,39.4;Henderson,North America,-105.85,39.75;British Columbia,North America,-120,49;Chuquicamata,South America,-68.9,-22.3;Toquepala,South America,-70.6,-17.25;Luanchuan,Asia,111.6,33.8;Kajaran,Asia,46.2,39.15;Knaben,Europe,7.06,58.66;Molyhil,Oceania,135.73,-22.75',
'Sb':'Xikuangshan,Asia,111.45,27.75;Anzob,Asia,68.55,39.12;Bolivian antimony belt,South America,-66.5,-18;Pezinok,Europe,17.2,48.3;Gediz,Asia,29.4,39;Murchison,Africa,30.7,-23.9;Costerfield,Oceania,144.8,-36.88;Donlin,North America,-158.5,61.2;Stibnite,North America,-115.3,44.9',
'Al':'Boke,Africa,-14.3,11;Awaso,Africa,-2.3,6.2;Jamaica,North America,-77.5,18.1;Suriname,South America,-54.7,5.6;Trombetas,South America,-56.4,-1.46;Weipa,Oceania,141.8,-12.5;Darling Range,Oceania,116.3,-32.8;Guangxi,Asia,107,23.5;Odisha,Asia,83.2,19.5;Parnassos,Europe,22.4,38.6;Hungarian karst,Europe,18.2,47.2;Provence,Europe,6.1,43.4',
'Cu':'Zambian Copperbelt,Africa,27.9,-12.6;Kolwezi,Africa,25.5,-10.7;Chuquicamata,South America,-68.9,-22.3;Southern Peru,South America,-71,-16;Arizona,North America,-111,33;Keweenaw,North America,-88.5,47.2;Cornwall,Europe,-5.2,50.2;Iberian Pyrite Belt,Europe,-6.6,37.7;Bor,Europe,22.1,44.1;Zhezkazgan,Asia,67.7,47.8;Dexing,Asia,117.7,29;Grasberg,Asia,137.1,-4.05;Olympic Dam,Oceania,136.89,-30.44;Mount Isa,Oceania,139.5,-20.7',
'Sn':'Cornwall,Europe,-5.2,50.2;Erzgebirge,Europe,13.75,50.7;Kinta Valley,Asia,101.1,4.5;Bangka,Asia,106.1,-2.2;Gejiu,Asia,103.16,23.35;Llallagua,South America,-66.6,-18.42;Pitinga,South America,-60.15,-.8;Renison,Oceania,145.4,-41.8;Jos Plateau,Africa,8.9,9.9;Rwanda,Africa,29.6,-1.95;Lost River,North America,-167.15,65.5'}
def norm(t):return ''.join(c for c in unicodedata.normalize('NFKD',str(t)).lower() if c.isalnum())
aliases={'unitedstatesofamerica':'unitedstates','usa':'unitedstates','burma':'myanmar','czechia':'czechrepublic','congokinshasa':'democraticrepublicofthecongo','congodemrep':'democraticrepublicofthecongo','korearepublicof':'southkorea','koreasouth':'southkorea','koreanorth':'northkorea','russianfederation':'russia','england':'unitedkingdom','scotland':'unitedkingdom','wales':'unitedkingdom'}
def country(t):n=norm(t);return aliases.get(n,n)
def load(path,optional=False):
    if not path.exists():
        if optional:return []
        raise FileNotFoundError(f'Required audit input missing: {path}')
    with (gzip.open(path,'rt',encoding='utf-8') if path.suffix=='.gz' else path.open(encoding='utf-8-sig')) as f:return json.load(f)['features']
pp=load(BASE/'normalized/pp1802-global.geojson');ga=load(BASE/'normalized/ga-aimr-metals.geojson')
enriched=load(BASE/'normalized/reviewed-secondary-metals.geojson')
afghan=load(BASE.parent/'nonmetals/normalized/additional-evidence.geojson')
probe_text['U']+=';Kirovohrad,Europe,32.3,48.5;Kyzylkum,Asia,63,42'
probe_text['Sb']+=';Krinj,Asia,71.76,36.06'
primary_province_sources={
 'Nui Phao':('https://pubs.usgs.gov/publication/pp1802G','USGS chapter explicitly names W-F-Bi-Cu-Au at this deposit; source GIS has incomplete secondary commodity labels'),
 'Boliden':('https://resource.sgu.se/dokument/publikation/c/c651rapport/c651-rapport.pdf','SGU primary mine study, section7.1.10; Bi tellurides occur in named sulfide deposit, not a separate confirmed economic Bi orebody'),
 'Pulganbar':('https://www.clarence.nsw.gov.au/files/assets/public/community/files/heritage/thematic_history_copmanhurst.pdf','Government commissioned heritage study pp45-46 documents historical local Hg ore and 1914-1916 extraction; smelter location is not an orebody coordinate'),
 'Chu Sarysu':('https://www-pub.iaea.org/MTCD/Publications/PDF/te_1425_web.pdf','IAEA primary Kazakhstan technical contribution maps uranium provinces and named sandstone deposits; retained despite no imported point witness'),
 'Lagoa Real':('https://conferences.iaea.org/event/44/contributions/7944/','Primary UERJ geological study presented at IAEA; Cachoeira albitite-hosted uranium in Lagoa Real province'),
 'Kirovohrad':('https://www-pub.iaea.org/MTCD/publications/PDF/TE1843_web.pdf','IAEA global uranium inventory, Ukraine metasomatite deposits; verify exact local geometries before importing'),
 'Kyzylkum':('https://www-pub.iaea.org/MTCD/publications/PDF/WOR_URA_web.pdf','IAEA World Uranium country exploration history names Kizylkum sandstone and Boztau black-shale exploration; not an economic-mine-only filter'),
 'Krinj':('https://gsp.gov.pk/wp-content/uploads/2026/02/Matellogeny-and-Mineral-Deposits-of-Pakistan-Kazmi-and-Abbas.pdf','GSP Metallic Minerals chapter pp62-63 documents quartz-stibnite veins, showings and intermittent mining around Krinj and Partsan')}
ppmap={'Sb':'Antimony','Mn':'Manganese','Pt':'Platinum-Group Elements','Ti':'Titanium','V':'Vanadium','Sn':'Tin'}
baseline={};oldextra={};newextra={};allcountries=[];allprobes=[];audit=[]
for code,(label,page,countrylist,systems,formrisk) in profiles.items():
    if code in ['Cu','Sn']:
        cu=DOCS/'copper-tin-global-2026-09-29/normalized'
        baseline[code]=load(cu/('mrds-copper.geojson.gz' if code=='Cu' else 'mrds-tin.geojson'))
        oldextra[code]=sum([load(cu/p) for p in ['pCu_deps_pros.geojson','pCu_Precambrian_deps_pros.geojson','sedCu_deps_pros.geojson']],[]) if code=='Cu' else []
    else:
        baseline[code]=load(PACKAGE/f'normalized/{code}-occurrences.geojson.gz')
        oldextra[code]=load(PACKAGE/f'normalized/{code}-specialist.geojson',optional=True)
    newextra[code]=[f for f in pp if ppmap.get(code,'IMPOSSIBLE') in f['properties']['commodity_label'].split('; ')] + [f for f in ga if code in f['properties']['commodities']]
    newextra[code]+=[f for f in enriched if code in f['properties']['commodities']]
    # Separate root's additions, schema may use commodity string or list.
    for f in afghan:
        p=f['properties'];v=p.get('commodities',p.get('commodity',[]))
        if code in (v if isinstance(v,list) else [v]):newextra[code].append(f)
    def countrycounts(data):return collections.Counter(country(f['properties'].get('country','')) for f in data)
    before=countrycounts(baseline[code]+oldextra[code]);after=countrycounts(newextra[code]);countries=[]
    for c in countrylist.split('|'):
        n=country(c);r={'commodity':code,'expected_country':c,'existing_records':before[n],'new_complement_records':after[n],'verdict':'ABSENT in imported evidence' if before[n]+after[n]==0 else 'Country has positive evidence; completeness NOT established','basis':('IAEA UDEPO/NEA global inventory' if code=='U' else 'USGS MCS2026 country table/resources plus named historical cases'),'source_url':UDEPO if code=='U' else MCS+f'#page={page}'}
        if c=='Latvia':r['source_url']='https://dspace.lu.lv/items/f9bf4048-f4d6-48fa-a19f-fe0c605bbfbe';r['basis']='Primary drill-core study; deep buried ore, not current mining'
        if code=='Sn' and c in ['United Kingdom','Czech Republic','Germany']:r['basis']='Historical tin province; PP1802-S/primary geological surveys, not modern production'
        if code=='Sb' and c=='Pakistan':r['source_url']=primary_province_sources['Krinj'][0];r['basis']=primary_province_sources['Krinj'][1]
        if code=='U' and c=='Uzbekistan':r['source_url']=primary_province_sources['Kyzylkum'][0];r['basis']=primary_province_sources['Kyzylkum'][1]
        if code=='U' and c=='Ukraine':r['source_url']='https://www-pub.iaea.org/MTCD/publications/PDF/TE_1629_CD/Deposits/652.html';r['basis']='IAEA named dormant Yuzhnoye deposit, zero cumulative production in historical record: occurrence does not require mining'
        countries.append(r);allcountries.append(r)
    probes=[]
    for line in probe_text[code].split(';'):
        name,continent,x,y=line.split(',');x,y=float(x),float(y);delta=.45 if name=='Staicele' else 2
        box=[x-delta,y-delta,x+delta,y+delta]
        def inside(f):
            g=f['geometry']
            if g is None or g['type']!='Point':return False
            a,b=g['coordinates'][:2];return box[0]<=a<=box[2] and box[1]<=b<=box[3]
        b=[f for f in baseline[code]+oldextra[code] if inside(f)];a=[f for f in newextra[code] if inside(f)]
        r={'commodity':code,'province':name,'continent':continent,'search_bbox':box,'existing_point_records':len(b),'new_point_records':len(a),'existing_example_names':'; '.join(dict.fromkeys(str(f['properties'].get('name','')) for f in b[:8])),'new_example_names':'; '.join(dict.fromkeys(str(f['properties'].get('name','')) for f in a[:8])),'verdict':'NO POINT EVIDENCE in search window' if not b and not a else 'Positive regional witness; nearby records do NOT verify the named deposit','basis':'Named geological province sentinel; broad search window, not geological footprint','source_url':(GA if continent=='Oceania' else PP if code in ppmap else UDEPO if code=='U' else MAJOR)}
        if name=='Staicele':r['source_url']='https://dspace.lu.lv/items/f9bf4048-f4d6-48fa-a19f-fe0c605bbfbe';r['basis']='Verified deep crystalline-basement banded iron ore; 688m secondary academic depth account'
        if name in ['Almaden','Idrija']:r['source_url']='https://whc.unesco.org/en/list/1313/';r['basis']='Historical extraction documented independently of current production'
        if name=='Las Medulas':r['source_url']='https://whc.unesco.org/en/list/803/';r['basis']='Ancient hydraulic gold mining; exhausted/altered landscape'
        if name=='Iwami Ginzan':r['source_url']='https://whc.unesco.org/en/list/1246/';r['basis']='Historic silver landscape, not modern operating mine'
        if name=='Cornwall':r['source_url']='https://webapps.bgs.ac.uk/memoirs/docs/B01737.html';r['basis']='BGS bedrock/vein/placer and historical extraction account'
        if name=='Tasna':r['source_url']=MCS+'#page=59';r['basis']='MCS explicitly names inactive primary Bi mine (inactive since1996)'
        if name in primary_province_sources:r['source_url'],r['basis']=primary_province_sources[name]
        r['point_witness_records']=[{'record_id':f['properties'].get('record_id'), 'name':f['properties'].get('name'), 'source_url':f['properties'].get('source_url')} for f in (b+a)[:12]]
        r['secondary_enrichment_records']=sum(f['properties'].get('source_id')=='reviewed_secondary_commodity_20260929' for f in a)
        probes.append(r);allprobes.append(r)
    gaps=[r['province'] for r in probes if r['existing_point_records']+r['new_point_records']==0]
    holes=[r['expected_country'] for r in countries if r['existing_records']+r['new_complement_records']==0]
    audit.append({'commodity':code,'label':label,'existing_mrds_records':len(baseline[code]),'existing_specialist_records':len(oldextra[code]),'new_complement_records':len(newextra[code]),'country_checks':len(countries),'named_province_checks':len(probes),'missing_country_witnesses':holes,'empty_province_windows':gaps,'verdict':'INCOMPLETE - explicit geographic alarms remain' if holes or gaps else 'PARTIAL - major sampled provinces have witnesses; global completeness NOT established','mineral_systems':systems,'mineral_form_and_depth_caveat':formrisk,'historical_coverage':'Incomplete; source Past Producer is not proof of ancient extraction and modern inactivity does not erase ore','subeconomic_coverage':'Incomplete; retain occurrence/exploration/deep concealed evidence, unknown economics stays null, no production/reserve cutoff applied','permissiveness_coverage':'Only Cu porphyry/sediment-hosted expert tracts already available; no universal all-system potential polygon' if code=='Cu' else 'No comprehensive audited global permissive polygon acquired; country/point buffers are not geological potential','next_source': 'GSC World Tin and Tungsten OF7688; national survey veins/placers and historical inventories' if code in ['Sn','W'] else 'IAEA UDEPO/NEA and national surveys including unconventional/low-grade/deep resources' if code=='U' else 'National geological survey occurrence/mineral-system and historical inventories; PP1802/GA complements remain selective','source_urls':[UDEPO if code=='U' else MCS+f'#page={page}',GA,PP if code in ppmap else MAJOR]})
    audit[-1]['mrds_source_status_counts']=dict(collections.Counter(str(f['properties'].get('source_status')) for f in baseline[code]))
    audit[-1]['existing_us_record_share']=round(before['unitedstates']/max(1,sum(before.values())),4)
    audit[-1]['post_complement_us_record_share']=round((before['unitedstates']+after['unitedstates'])/max(1,sum(before.values())+sum(after.values())),4)
    audit[-1]['record_share_caveat']='Source-record counts are reporting-density diagnostics, not geological abundance, independent deposits or probability.'
def writecsv(path,rows):
    with path.open('w',newline='',encoding='utf-8') as f:w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows({k:json.dumps(v,ensure_ascii=False) if isinstance(v,(list,dict)) else v for k,v in r.items()} for r in rows)
for name,rows in [('country-checks',allcountries),('province-checks',allprobes),('metal-audit',audit)]:
    (BASE/(name+'.json')).write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8');writecsv(BASE/(name+'.csv'),rows)
print(json.dumps({'commodities':len(audit),'province_checks':len(allprobes),'country_checks':len(allcountries),'empty_windows':[(r['commodity'],r['province']) for r in allprobes if r['existing_point_records']+r['new_point_records']==0],'missing_countries':[(r['commodity'],r['expected_country']) for r in allcountries if r['existing_records']+r['new_complement_records']==0]},indent=2))
