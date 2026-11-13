"""Authored catalogue of the coal and evaporites group (decision 0065). Read by build_catalogue.py.

Every entry names its sources (SOURCES ids). Geometry comes from open polygon layers on disk where they exist
(USGS, BGS, PGI, GD NRW, LfULG Saxony, NSW, Victoria, Geoscience Australia, USGS potash tracts); elsewhere it is an
authored outline (a few to twenty vertices) drawn from the published maps the source names, marked est 'geometry'.
Tonnages (mt, million tonnes) are pre-mining endowment as far as the source gives it: past production + reserves +
resources in place to the source's depth limit. Coal subbituminous coal is written as lignite (VS has brown coal,
bituminous coal and anthracite only). Bed thickness is the summed workable seam thickness of that bed group; where a
published tonnage exists the build scales each bed's cover so the beds hold that tonnage (est 'cover').
Confidence q: A measured/production record, B compilation estimate, C order of magnitude.
"""

RET = '2026-11-08'
SOURCES = [
    {'id': 'lit', 'title': 'Published figures cited per entry in docs/research/ore-deposits-2026-11-08/coal-evaporites.md (survey yearbooks, assessments, reviews); used as facts, no text or data tables copied',
     'url': 'docs/research/ore-deposits-2026-11-08/coal-evaporites.md', 'licence': 'facts with citation', 'retrieved': RET},
    {'id': 'bgr-2023', 'title': 'BGR Energy Study 2023: Data and Developments concerning German and Global Energy Supplies (Bundesanstalt fuer Geowissenschaften und Rohstoffe), coal reserves and resources by country, end of 2022',
     'url': 'https://www.bgr.bund.de/EN/Themen/Energie/Produkte/produkte_node_en.html', 'licence': 'facts with citation (country totals used for validation only)', 'retrieved': RET},
    {'id': 'usgs-mcs-2025', 'title': 'USGS Mineral Commodity Summaries 2025 (salt, potash, gypsum, boron, sulfur, phosphate rock, nitrogen)',
     'url': 'https://pubs.usgs.gov/publication/mcs2025', 'licence': 'public domain (USGS)', 'retrieved': RET},
    {'id': 'usgs-coal-us-2012', 'title': 'Coal Fields of the Conterminous United States, NCRA updated version (USGS OFR 2012-1205)',
     'url': 'https://pubs.usgs.gov/of/2012/1205/', 'licence': 'public domain (USGS)', 'retrieved': '2026-09-29'},
    {'id': 'usgs-coal-americas-2008', 'title': 'Coal-bearing areas of North, Central and South America (USGS OFR 2008-1257, Tewalt, Kinney and Merrill)',
     'url': 'https://pubs.usgs.gov/of/2008/1257/', 'licence': 'public domain (USGS)', 'retrieved': '2026-09-29'},
    {'id': 'usgs-coal-china-2014', 'title': 'Chinese coal-mining areas as of 2001 (USGS OFR 2014-1219, Trippi, Belkin, Dai, Tewalt and Chou)',
     'url': 'https://pubs.usgs.gov/of/2014/1219/', 'licence': 'public domain (USGS)', 'retrieved': '2026-09-29'},
    {'id': 'usgs-coal-india-2011', 'title': 'Coal-bearing areas of India and Bangladesh (USGS OFR 2011-1296, after Geological Survey of India)',
     'url': 'https://pubs.usgs.gov/of/2011/1296/', 'licence': 'public domain (USGS)', 'retrieved': '2026-09-29'},
    {'id': 'bgs-coal-2004', 'title': 'BGS Coal Resources for New Technologies (coal-bearing strata at surface and concealed, 1:1 million)',
     'url': 'https://www.bgs.ac.uk/datasets/coal-resources-for-new-technologies/', 'licence': 'Open Government Licence v3 (contains British Geological Survey materials (c) UKRI)', 'retrieved': '2026-09-29'},
    {'id': 'pgi-midas', 'title': 'PIG-PIB MIDAS documented mineral-deposit boundaries (hard coal)',
     'url': 'https://geologia.pgi.gov.pl/srv/rest/services/midas/MapServer', 'licence': 'PIG-PIB public-sector information reuse conditions (attribution, date, processing disclosed)', 'retrieved': '2026-09-29'},
    {'id': 'nrw-rk500', 'title': 'Geologischer Dienst NRW, IS RK500 Rohstoffkarte (Steinkohle bis 2000 m, Braunkohle)',
     'url': 'https://www.opengeodata.nrw.de/produkte/geologie/geologie/RK/ISRK500/', 'licence': 'Datenlizenz Deutschland - Namensnennung 2.0', 'retrieved': '2026-09-29'},
    {'id': 'saxony-lfulg', 'title': 'LfULG Saxony, RoeK400 fossil fuels and lignite-field inventory 1:50,000',
     'url': 'https://luis.sachsen.de/geologie/rohstoffgeologische-karten.html', 'licence': 'Datenlizenz Deutschland - Namensnennung 2.0', 'retrieved': '2026-09-29'},
    {'id': 'nsw-coalfields', 'title': 'NSW Mining Coalfields (Geological Survey of NSW)',
     'url': 'https://data.nsw.gov.au/data/dataset/nsw-mining-coalfieds', 'licence': 'Creative Commons Attribution', 'retrieved': '2026-09-29'},
    {'id': 'vic-coalfields', 'title': 'Victorian Coal Fields 2006 inventory (Geological Survey of Victoria)',
     'url': 'https://discover.data.vic.gov.au/en_AU/dataset/victorian-coal-fields', 'licence': 'Creative Commons Attribution 4.0', 'retrieved': '2026-09-29'},
    {'id': 'ga-basins-2013', 'title': 'Geoscience Australia, Australian Coal Basins 2013',
     'url': 'https://data.gov.au/data/dataset/9d5a8d74-a201-42bd-9d49-3c392244be16', 'licence': 'Creative Commons Attribution 3.0 Australia', 'retrieved': '2026-09-29'},
    {'id': 'usgs-potash-2014', 'title': 'Global potash tracts and deposits (USGS SIR 2010-5090-S, Orris and others)',
     'url': 'https://pubs.usgs.gov/sir/2010/5090/s/', 'licence': 'public domain (USGS)', 'retrieved': '2026-09-29'},
    {'id': 'usgs-phosphate-2002', 'title': 'World phosphate deposits (USGS mineral-resource data, phosphate)',
     'url': 'https://mrdata.usgs.gov/phosphate/', 'licence': 'public domain (USGS)', 'retrieved': '2026-09-29'},
    {'id': 'usgs-mrds', 'title': 'USGS Mineral Resources Data System (MRDS)',
     'url': 'https://mrdata.usgs.gov/mrds/', 'licence': 'public domain (USGS)', 'retrieved': '2026-09-29'},
    {'id': 'usgs-pp1809', 'title': 'Luppens and others (2015) Coal geology and assessment of coal resources and reserves in the Powder River Basin, USGS Professional Paper 1809',
     'url': 'https://doi.org/10.3133/pp1809', 'licence': 'public domain (USGS)', 'retrieved': RET},
]
GRADE_BANDS = {}  # no graded ores in this group (coal, salts, gypsum, borates, sulfur, phosphorite, alum are ungraded in VS)

DENSITY = {'lignite': 1.2, 'bituminouscoal': 1.35, 'anthracite': 1.5, 'halite': 2.16, 'sylvite': 2.0,
           'earth-saltflat': 2.0, 'earth-saltflat-sylvite': 2.0, 'earth-gypsum': 2.3, 'borax': 1.9, 'kernite': 1.9,
           'sulfur': 2.3, 'phosphorite': 2.3, 'alum': 2.6}
MT_CODES = {'C_coal': ['lignite', 'bituminouscoal', 'anthracite'], 'K': ['sylvite', 'earth-saltflat-sylvite'],
            'NaCl': ['halite', 'earth-saltflat'], 'gypsum': ['earth-gypsum'], 'P': ['phosphorite'], 'S': ['sulfur'],
            'B': ['borax', 'kernite'], 'alum': ['alum']}
AREA_WEIGHT = {'A': 0.8, 'B': 0.7, 'C': 0.6}


def DEPOSIT_TIER(d):
    big = {'NaCl': 500, 'K': 500, 'gypsum': 100, 'B': 100, 'S': 100, 'P': 500, 'alum': 20}.get(d['layer'], 100)
    mt = d.get('mainMt') or d['mt']
    if mt >= big * 4 and d['q'] in 'AB': return 'major'
    if mt >= big or d['q'] == 'A': return 'deposit_large'
    return 'deposit'


# ---------------------------------------------------------------------------------------------- helpers
def ring(*pts, close=0.0):
    pts = list(pts)
    if pts[0] != pts[-1]: pts.append(pts[0])
    return {'kind': 'ring', 'ring': pts, 'close': close}


def coal(src, bbox, where=None, notbox=None, close=0.03, clip=None, minus=None):
    r = {'kind': 'coal', 'src': src if isinstance(src, list) else [src], 'bbox': bbox, 'where': where,
         'notbox': notbox or [], 'close': close}
    if clip: r['clip'] = clip
    if minus: r['minus'] = minus
    return r


def ga(name, clip=None):
    r = {'kind': 'ga', 'name': name, 'close': 0.0}
    if clip: r['clip'] = clip
    return r


def potash(*ids, clip=None, minus=None):
    r = {'kind': 'potash', 'ids': list(ids), 'close': 0.0}
    if clip: r['clip'] = clip
    if minus: r['minus'] = minus
    return r


def union(*parts, close=0.0):
    return {'kind': 'union', 'parts': list(parts), 'close': close}


def bed(code, topM, thickM, cover=None):
    b = {'code': code, 'topM': topM, 'thickM': thickM}
    if cover is not None: b['cover'] = cover
    return b


AREAS, DEPOSITS, EVIDENCE_ONLY = [], [], []


def A(id, name, layer, country, age, geom, beds, mt=None, q='C', src=('lit',), worked=None, est=(), also=(), mtCodes=None,
      grade=None, unit=None):
    e = {'id': id, 'name': name, 'layer': layer, 'country': country, 'age': age, 'geom': geom, 'beds': beds, 'mt': mt,
         'q': q, 'src': list(src), 'worked': worked, 'est': list(est) + (['geometry'] if geom['kind'] == 'ring' else []),
         'alsoCodes': list(also), 'grade': grade, 'unit': unit}
    if mtCodes: e['mtCodes'] = mtCodes
    AREAS.append(e)


def D(id, name, lat, lon, layer, ores, shape, mt, age, country, q='C', src=('lit',), grade=None, unit=None, topM=0, baseM=None,
      lenKm=1.0, widKm=1.0, strikeDeg=0, dipDeg=0, density=None, est=('topM', 'baseM', 'lenKm', 'widKm'), worked=None,
      layers=None, also=(), mainMt=None):
    """A deposit body. `layers`: 0060 layer classes (0065 contract, coordinator commit 50420af). `also`: extra heat-map
    codes for its evidence point. `mainMt`: tonnage of the layer's own commodity when the body holds more (a salt dome
    with a sulfur cap counts its sulfur ore for S tiers and totals, not its halite)."""
    DEPOSITS.append({'id': id, 'name': name, 'lat': lat, 'lon': lon, 'layer': layer,
                     'ores': [{'code': o[0], 'share': o[1], **({'zone': o[2]} if len(o) > 2 else {})} for o in ores],
                     'shape': shape, 'mt': mt, 'grade': grade, 'unit': unit, 'topM': topM, 'baseM': baseM, 'lenKm': lenKm,
                     'widKm': widKm, 'strikeDeg': strikeDeg, 'dipDeg': dipDeg, 'density': density, 'age': age,
                     'country': country, 'q': q, 'src': list(src), 'est': list(est), 'worked': worked,
                     'layers': layers, 'alsoCodes': list(also), 'mainMt': mainMt})


BIT, ANT, LIG = 'bituminouscoal', 'anthracite', 'lignite'
UK, DE = 'United Kingdom', 'Germany'
BGS = 'bgs_coal_2004'
OUT_ = {'exposure': 'outcropping'}
CON_ = {'exposure': 'concealed'}

# ============================================================================================== COAL
# ---- Britain (BGS 1:1M outlines; exposed coalfields split from concealed ones). In-place tonnages are order of
# magnitude (past production + remaining resources), BGS/Coal Authority figures and coalfield histories.
A('coal:great-northern-exposed', 'Northumberland and Durham coalfield (exposed)', 'C_coal', UK, 'Carboniferous',
  coal(BGS, [-2.3, 54.45, -1.25, 55.45], OUT_), [bed(BIT, 0, 12)], mt=3000, src=['lit', 'bgs-coal-2004'],
  worked='sea coal shipped from Newcastle by the 13th century (borough coal rights 1239)')
A('coal:great-northern-concealed', 'Durham coalfield under the Permian (concealed)', 'C_coal', UK, 'Carboniferous',
  coal(BGS, [-2.3, 54.45, -1.15, 55.45], CON_), [bed(BIT, 150, 12)], mt=4000, src=['lit', 'bgs-coal-2004'])
A('coal:east-pennine-exposed', 'Yorkshire and Nottinghamshire coalfield (exposed)', 'C_coal', UK, 'Carboniferous',
  coal(BGS, [-1.95, 52.75, -0.9, 54.0], OUT_), [bed(BIT, 0, 14)], mt=4000, src=['lit', 'bgs-coal-2004'],
  worked='outcrop coal dug around Leeds and Wakefield in the 13th-14th centuries')
A('coal:east-pennine-concealed', 'Yorkshire and Nottinghamshire coalfield (concealed)', 'C_coal', UK, 'Carboniferous',
  coal(BGS, [-1.95, 52.75, -0.4, 54.0], CON_), [bed(BIT, 400, 14)], mt=16000, src=['lit', 'bgs-coal-2004'])
A('coal:south-wales-bituminous', 'South Wales coalfield, eastern (bituminous)', 'C_coal', UK, 'Carboniferous',
  coal(BGS, [-3.95, 51.45, -2.9, 51.95]), [bed(BIT, 0, 20)], mt=8000, src=['lit', 'bgs-coal-2004'],
  worked='outcrop working in the coalfield rim by the 13th century')
A('coal:south-wales-anthracite', 'South Wales coalfield, western (anthracite) and Pembrokeshire', 'C_coal', UK, 'Carboniferous',
  coal(BGS, [-5.3, 51.6, -3.95, 51.95]), [bed(ANT, 0, 15)], mt=5000, src=['lit', 'bgs-coal-2004'],
  worked='Pembrokeshire and Gower outcrop coal worked in the 13th century')
A('coal:midland-valley', 'Scottish Midland Valley coalfields (Fife, Lothians, Lanarkshire, Ayrshire)', 'C_coal', UK, 'Carboniferous',
  coal(BGS, [-4.9, 55.35, -2.6, 56.35]), [bed(BIT, 0, 12)], mt=10000, src=['lit', 'bgs-coal-2004'],
  worked='coal granted to Newbattle (Lothian) and Dunfermline (Fife) abbeys about 1200')
A('coal:lancashire', 'Lancashire and North Wales coalfields', 'C_coal', UK, 'Carboniferous',
  coal(BGS, [-3.4, 52.85, -2.0, 53.75]), [bed(BIT, 0, 12)], mt=6000, src=['lit', 'bgs-coal-2004'],
  worked='Flintshire and Lancashire outcrop coal in 13th-century records')
A('coal:english-midlands', 'English Midlands coalfields (Staffordshire, Warwickshire, Leicestershire, Shropshire)', 'C_coal', UK, 'Carboniferous',
  coal(BGS, [-2.75, 52.3, -1.25, 53.15]), [bed(BIT, 0, 15)], mt=8000, src=['lit', 'bgs-coal-2004'],
  worked='Black Country Thick Coal and Shropshire outcrop coal dug by the 13th-14th centuries')
A('coal:dean-somerset', 'Forest of Dean, Bristol and Somerset coalfields', 'C_coal', UK, 'Carboniferous',
  coal(BGS, [-2.85, 51.1, -2.25, 51.95]), [bed(BIT, 0, 6)], mt=1500, src=['lit', 'bgs-coal-2004'],
  worked='Forest of Dean free miners worked outcrop coal in the 13th century')
A('coal:cumbria', 'Cumberland coalfield', 'C_coal', UK, 'Carboniferous',
  coal(BGS, [-3.65, 54.4, -3.0, 54.95]), [bed(BIT, 0, 8)], mt=1500, src=['lit', 'bgs-coal-2004'])

# ---- Low Countries, France, Iberia (authored outlines from national coalfield maps)
BE, FR, NL = 'Belgium', 'France', 'Netherlands'
A('coal:liege', 'Liege coal basin (Meuse valley)', 'C_coal', BE, 'Carboniferous',
  ring((5.28, 50.57), (5.50, 50.59), (5.70, 50.62), (5.90, 50.65), (5.90, 50.72), (5.70, 50.71), (5.50, 50.68), (5.28, 50.65)),
  [bed(BIT, 0, 18)], mt=2000, worked='houille mined at Liege from about 1200 (among the earliest coal mining in Europe)')
A('coal:haine-sambre', 'Haine-Sambre basin (Borinage, Centre, Charleroi)', 'C_coal', BE, 'Carboniferous',
  ring((3.65, 50.39), (4.00, 50.37), (4.40, 50.37), (4.75, 50.41), (5.10, 50.45), (5.10, 50.52), (4.75, 50.50), (4.40, 50.48), (4.00, 50.47), (3.65, 50.47)),
  [bed(BIT, 0, 20)], mt=4000, worked='outcrop coal at Charleroi and in the Borinage worked in the 13th century')
A('coal:campine', 'Campine (Kempen) coal basin (concealed)', 'C_coal', BE, 'Carboniferous',
  ring((5.00, 50.98), (5.40, 50.95), (5.85, 50.95), (5.85, 51.20), (5.40, 51.22), (5.00, 51.18)),
  [bed(BIT, 500, 15)], mt=6000)
A('coal:nord-pas-de-calais', 'Nord-Pas-de-Calais coal basin (under the Chalk)', 'C_coal', FR, 'Carboniferous',
  ring((2.25, 50.50), (2.70, 50.47), (3.10, 50.37), (3.50, 50.35), (3.68, 50.40), (3.68, 50.47), (3.30, 50.47), (2.90, 50.50), (2.60, 50.56), (2.25, 50.57)),
  [bed(BIT, 130, 15)], mt=5000)
A('coal:limburg-aachen', 'South Limburg and Aachen coalfields (Wurm, Inde)', 'C_coal', DE, 'Carboniferous',
  ring((5.80, 50.82), (6.30, 50.78), (6.42, 50.86), (6.25, 50.96), (5.95, 50.97), (5.80, 50.92)),
  [bed(BIT, 0, 12)], mt=3000, worked='Kloosterrade (Rolduc) coal 1113; Aachen Wurm coal in the 13th century')
A('coal:saint-etienne', 'Saint-Etienne coal basin', 'C_coal', FR, 'Carboniferous',
  ring((4.15, 45.40), (4.40, 45.40), (4.62, 45.45), (4.66, 45.52), (4.40, 45.51), (4.15, 45.46)),
  [bed(BIT, 0, 15)], mt=1000, worked='coal dug at Saint-Etienne and Roche-la-Moliere by the late 13th century')
A('coal:lorraine', 'Lorraine coal basin (concealed continuation of the Saar)', 'C_coal', FR, 'Carboniferous',
  ring((6.45, 49.05), (6.85, 49.12), (6.92, 49.22), (6.80, 49.26), (6.58, 49.22), (6.40, 49.12)),
  [bed(BIT, 300, 20)], mt=4000)
A('coal:asturias', 'Central Asturian coal basin', 'C_coal', 'Spain', 'Carboniferous',
  ring((-5.95, 43.20), (-5.70, 43.10), (-5.45, 43.13), (-5.40, 43.25), (-5.62, 43.37), (-5.90, 43.34)),
  [bed(BIT, 0, 15)], mt=2000)

# ---- Germany, Czechia, Poland
A('coal:ruhr-outcrop', 'Southern Ruhr (outcrop coal of the Ruhr valley)', 'C_coal', DE, 'Carboniferous',
  ring((6.95, 51.33), (7.25, 51.33), (7.55, 51.37), (7.68, 51.43), (7.45, 51.47), (7.15, 51.44), (6.95, 51.41)),
  [bed(BIT, 0, 25)], mt=3000, src=['lit', 'nrw-rk500'],
  worked='outcrop seams of the Ruhr valley used from about 1300')
A('coal:ruhr-concealed', 'Ruhr coalfield under the Cretaceous (to 2000 m)', 'C_coal', DE, 'Carboniferous',
  coal('nrw_rk500', [6.3, 51.2, 8.1, 51.95], {'ROHSTOFFART': 'Steinkohle'},
       minus=ring((6.95, 51.33), (7.25, 51.33), (7.55, 51.37), (7.68, 51.43), (7.45, 51.47), (7.15, 51.44), (6.95, 51.41))),
  [bed(BIT, 600, 30)], mt=45000, src=['lit', 'nrw-rk500'])
A('coal:ibbenbueren', 'Ibbenbueren anthracite (Carboniferous horst)', 'C_coal', DE, 'Carboniferous',
  ring((7.60, 52.25), (7.85, 52.25), (7.88, 52.31), (7.62, 52.33)),
  [bed(ANT, 100, 8)], mt=800)
A('coal:saar', 'Saar coalfield (Saarkohlenwald)', 'C_coal', DE, 'Carboniferous',
  ring((6.80, 49.22), (7.00, 49.23), (7.25, 49.35), (7.27, 49.45), (7.05, 49.46), (6.88, 49.38), (6.75, 49.28)),
  [bed(BIT, 0, 25)], mt=6000, worked='outcrop coal exposed in the Saar valley (first written mining record 1429)')
A('coal:zwickau-lugau', 'Zwickau and Lugau-Oelsnitz coalfields', 'C_coal', DE, 'Carboniferous',
  union(ring((12.38, 50.68), (12.55, 50.67), (12.59, 50.75), (12.45, 50.77), (12.37, 50.73)),
        ring((12.63, 50.69), (12.80, 50.69), (12.81, 50.76), (12.65, 50.77))),
  [bed(BIT, 0, 12)], mt=300, worked='outcrop coal used by Zwickau smiths (first written record 1348)')
A('coal:rhenish-lignite', 'Rhenish lignite (Ville, Garzweiler, Hambach, Inden)', 'C_coal', DE, 'Neogene',
  coal('nrw_rk500', [5.9, 50.6, 7.2, 51.35], {'ROHSTOFFART': 'Braunkohle'}),
  [bed(LIG, 100, 50)], mt=55000, q='B', src=['lit', 'nrw-rk500'],
  worked='lignite exposed on the Ville ridge (dug for fuel from the 16th century)')
A('coal:lusatia', 'Lusatian lignite (Lausitz)', 'C_coal', DE, 'Neogene',
  union(coal(['saxony_lignite50', 'saxony_ruk400'], [13.7, 51.15, 15.1, 51.7]),
        ring((13.60, 51.45), (14.75, 51.45), (14.75, 51.85), (14.30, 51.95), (13.60, 51.75)), close=0.03),
  [bed(LIG, 60, 12)], mt=12000, src=['lit', 'saxony-lfulg'])
A('coal:central-german-lignite', 'Central German lignite (Leipzig, Halle, Geiseltal, Zeitz)', 'C_coal', DE, 'Paleogene',
  union(coal(['saxony_lignite50', 'saxony_ruk400'], [11.8, 50.9, 12.8, 51.5]),
        ring((11.45, 51.05), (11.85, 51.0), (12.15, 51.1), (12.15, 51.5), (11.75, 51.55), (11.45, 51.35)), close=0.03),
  [bed(LIG, 30, 15)], mt=10000, src=['lit', 'saxony-lfulg'])
A('coal:north-bohemia', 'North Bohemian lignite basins (Most, Sokolov)', 'C_coal', 'Czechia', 'Neogene',
  union(ring((13.20, 50.40), (13.55, 50.42), (13.90, 50.55), (14.05, 50.64), (13.92, 50.70), (13.60, 50.61), (13.35, 50.53), (13.20, 50.46)),
        ring((12.45, 50.14), (12.85, 50.15), (12.85, 50.24), (12.45, 50.23))),
  [bed(LIG, 50, 25)], mt=10000)
A('coal:upper-silesia-pl', 'Upper Silesian coal basin, Polish part', 'C_coal', 'Poland', 'Carboniferous',
  coal('pgi_midas', [18.1, 49.75, 19.75, 50.55], {'COMMODITY': 'hard coal'}, close=0.04),
  [bed(BIT, 100, 40)], mt=55000, q='B', src=['lit', 'pgi-midas'])
A('coal:ostrava-karvina', 'Upper Silesian coal basin, Czech part (Ostrava-Karvina)', 'C_coal', 'Czechia', 'Carboniferous',
  ring((18.18, 49.70), (18.65, 49.70), (18.78, 49.84), (18.62, 49.95), (18.25, 49.92)),
  [bed(BIT, 50, 30)], mt=15000)
A('coal:lublin', 'Lublin coal basin (concealed)', 'C_coal', 'Poland', 'Carboniferous',
  coal('pgi_midas', [22.5, 50.8, 24.2, 51.75], {'COMMODITY': 'hard coal'}, close=0.04),
  [bed(BIT, 700, 10)], mt=9000, q='B', src=['lit', 'pgi-midas'])
A('coal:lower-silesia', 'Lower Silesian coal basin (Walbrzych, Nowa Ruda)', 'C_coal', 'Poland', 'Carboniferous',
  coal('pgi_midas', [16.0, 50.5, 16.7, 50.95], {'COMMODITY': 'hard coal'}, close=0.03),
  [bed(BIT, 0, 8)], mt=1000, src=['lit', 'pgi-midas'])
A('coal:belchatow', 'Belchatow lignite (central Poland)', 'C_coal', 'Poland', 'Neogene',
  ring((19.15, 51.20), (19.55, 51.20), (19.55, 51.30), (19.15, 51.30)),
  [bed(LIG, 150, 50)], mt=2000)
A('coal:lviv-volyn', 'Lviv-Volyn coal basin', 'C_coal', 'Ukraine', 'Carboniferous',
  ring((23.6, 50.2), (24.6, 50.3), (24.8, 50.8), (24.3, 50.9), (23.8, 50.6)),
  [bed(BIT, 400, 3)], mt=2500)

# ---- Southeast Europe, Turkey, Iran
A('coal:maritsa-east', 'Maritsa East lignite', 'C_coal', 'Bulgaria', 'Neogene',
  ring((25.75, 42.05), (26.25, 42.05), (26.25, 42.30), (25.75, 42.30)), [bed(LIG, 30, 15)], mt=2500)
A('coal:kosovo', 'Kosovo lignite basin', 'C_coal', 'Kosovo', 'Neogene',
  ring((20.85, 42.45), (21.25, 42.45), (21.25, 42.85), (20.85, 42.85)), [bed(LIG, 20, 40)], mt=12000, q='B')
A('coal:kolubara', 'Kolubara lignite', 'C_coal', 'Republic of Serbia', 'Neogene',
  ring((20.10, 44.35), (20.50, 44.35), (20.50, 44.60), (20.10, 44.60)), [bed(LIG, 30, 25)], mt=3000)
A('coal:ptolemais', 'Ptolemais-Amyntaio lignite', 'C_coal', 'Greece', 'Neogene',
  ring((21.55, 40.35), (21.95, 40.35), (21.95, 40.75), (21.55, 40.75)), [bed(LIG, 30, 30)], mt=4000)
A('coal:zonguldak', 'Zonguldak coal basin', 'C_coal', 'Turkey', 'Carboniferous',
  ring((31.45, 41.20), (31.90, 41.25), (32.45, 41.45), (32.60, 41.70), (32.30, 41.65), (31.75, 41.45)),
  [bed(BIT, 0, 10)], mt=1500)
A('coal:afsin-elbistan', 'Afsin-Elbistan lignite', 'C_coal', 'Turkey', 'Neogene',
  ring((36.90, 38.15), (37.30, 38.15), (37.30, 38.40), (36.90, 38.40)), [bed(LIG, 40, 40)], mt=4800)
A('coal:tabas', 'Tabas coalfield', 'C_coal', 'Iran', 'Jurassic',
  ring((56.80, 32.80), (57.60, 32.80), (57.60, 33.90), (56.80, 33.90)), [bed(BIT, 50, 6)], mt=10000)

# ---- former Soviet Union (authored outlines after the Vernadsky Museum / USGS OFR 01-104 maps)
RU, UA, KZ = 'Russia', 'Ukraine', 'Kazakhstan'
DONBAS = ring((35.6, 48.40), (36.5, 48.65), (37.6, 48.95), (38.6, 49.05), (39.6, 48.95), (40.6, 48.75), (41.0, 48.30),
              (40.4, 47.80), (39.4, 47.65), (38.3, 47.70), (37.4, 47.85), (36.5, 48.05), (35.8, 48.15))
A('coal:donbas', 'Donets basin (Donbas), bituminous part', 'C_coal', UA, 'Carboniferous',
  {**DONBAS, 'clip': [35.0, 47.0, 38.8, 50.0]}, [bed(BIT, 0, 30)], mt=100000)
A('coal:donbas-anthracite', 'Donets basin (Donbas), eastern anthracite part', 'C_coal', UA, 'Carboniferous',
  {**DONBAS, 'clip': [38.8, 47.0, 42.0, 50.0]}, [bed(ANT, 0, 30)], mt=40000)
A('coal:moscow-basin', 'Moscow brown-coal basin (Tula, Kaluga, Ryazan)', 'C_coal', RU, 'Carboniferous',
  ring((34.0, 54.8), (35.0, 54.4), (36.5, 53.6), (38.3, 53.4), (39.6, 53.8), (39.0, 54.4), (37.5, 54.7), (35.5, 55.0)),
  [bed(LIG, 50, 3)], mt=20000)
A('coal:pechora', 'Pechora coal basin (Vorkuta, Inta)', 'C_coal', RU, 'Permian',
  ring((59.8, 65.8), (61.5, 65.6), (63.5, 66.8), (65.0, 67.4), (64.5, 67.9), (62.8, 67.9), (61.0, 67.2), (59.9, 66.5)),
  [bed(BIT, 50, 15)], mt=340000)
A('coal:kuzbass', 'Kuznetsk basin (Kuzbass)', 'C_coal', RU, 'Permian',
  ring((85.6, 55.55), (86.3, 55.90), (86.75, 55.55), (87.3, 54.80), (88.0, 54.10), (88.4, 53.65), (87.9, 53.40), (87.1, 53.55),
       (86.4, 54.05), (85.9, 54.6), (85.4, 55.05)),
  [bed(BIT, 0, 60), bed(ANT, 300, 5, 0.1)], mt=640000)
A('coal:kansk-achinsk', 'Kansk-Achinsk lignite basin', 'C_coal', RU, 'Jurassic',
  ring((89.5, 55.9), (91.5, 55.8), (93.5, 55.7), (96.0, 55.6), (98.0, 55.6), (98.0, 56.3), (95.8, 56.6), (93.0, 56.5), (90.5, 56.6), (89.5, 56.3)),
  [bed(LIG, 10, 50)], mt=600000)
A('coal:irkutsk', 'Irkutsk coal basin (Cheremkhovo)', 'C_coal', RU, 'Jurassic',
  ring((101.0, 53.8), (101.6, 52.8), (103.0, 52.2), (104.6, 52.0), (104.6, 52.4), (102.6, 53.3), (101.6, 54.0)),
  [bed(BIT, 10, 8)], mt=30000)
A('coal:south-yakutia', 'South Yakutian coal basin (Neryungri)', 'C_coal', RU, 'Jurassic',
  ring((123.5, 56.4), (126.2, 56.4), (126.2, 57.2), (123.5, 57.2)), [bed(BIT, 20, 20)], mt=40000)
A('coal:tunguska', 'Tunguska coal basin', 'C_coal', RU, 'Permian',
  ring((90, 58), (100, 57), (108, 58), (110, 62), (108, 67), (100, 70), (90, 70), (87, 66), (88, 61)),
  [bed(BIT, 100, 10)], mt=2000000)
A('coal:lena', 'Lena coal basin', 'C_coal', RU, 'Jurassic',
  ring((110, 62), (116, 61.5), (122, 62), (128, 64), (128, 70), (120, 72), (112, 70), (110, 66)),
  [bed(LIG, 50, 8)], mt=1600000)
A('coal:karaganda', 'Karaganda coal basin', 'C_coal', KZ, 'Carboniferous',
  ring((72.6, 49.6), (73.6, 49.6), (73.9, 49.95), (73.3, 50.1), (72.7, 50.0)), [bed(BIT, 0, 20)], mt=50000)
A('coal:ekibastuz', 'Ekibastuz coalfield', 'C_coal', KZ, 'Carboniferous',
  ring((75.05, 51.60), (75.50, 51.60), (75.50, 51.85), (75.05, 51.85)), [bed(BIT, 0, 130)], mt=12000, q='B')
A('coal:turgai', 'Turgai brown-coal basin (Priozernoye, Kushmurun, Orlovka)', 'C_coal', KZ, 'Jurassic',
  ring((63.2, 51.2), (65.6, 51.2), (66.0, 52.4), (65.4, 53.6), (63.8, 53.6), (63.0, 52.4)), [bed(LIG, 20, 30)], mt=60000)

# ---- China (USGS coal-mining areas of 2001 by province and rank)
CN, CNs = 'China', 'usgs_china_2014'
A('coal:shanxi', 'Shanxi coal province (Datong, Ningwu, Xishan, Hedong)', 'C_coal', CN, 'Carboniferous',
  coal(CNs, [110.0, 34.5, 114.7, 40.9], {'Province': 'Shanxi', 'Rank': 'Bituminous'}, close=0.05),
  [bed(BIT, 0, 15)], mt=220000, q='B', src=['lit', 'usgs-coal-china-2014'],
  worked='coal burned on a large scale in Song-dynasty north China (11th century); Shanxi outcrop coal')
A('coal:qinshui-anthracite', 'Qinshui anthracite (Jincheng, Yangquan)', 'C_coal', CN, 'Carboniferous',
  coal(CNs, [110.0, 34.5, 114.7, 40.9], {'Province': 'Shanxi', 'Rank': 'Anthracite'}, close=0.05),
  [bed(ANT, 0, 12)], mt=50000, src=['lit', 'usgs-coal-china-2014'])
A('coal:ordos', 'Ordos basin Jurassic coal (Shendong, Shenfu-Dongsheng)', 'C_coal', CN, 'Jurassic',
  coal(CNs, [106.5, 36.5, 111.5, 40.6], {'Province': ['Shaanxi', 'Inner Mongolia', 'Ningxia'], 'Rank': 'Bituminous'}, close=0.05),
  [bed(BIT, 50, 20)], mt=220000, src=['lit', 'usgs-coal-china-2014'])
A('coal:fushun', 'Fushun coalfield', 'C_coal', CN, 'Paleogene',
  ring((123.65, 41.82), (124.05, 41.83), (124.05, 41.88), (123.65, 41.87)), [bed(BIT, 0, 50)], mt=1500,
  worked='outcrop coal of Fushun worked before the Yuan')
A('coal:western-hills', 'Western Hills of Beijing (Mentougou)', 'C_coal', CN, 'Jurassic',
  coal(CNs, [115.4, 39.5, 116.4, 40.2], {'Province': 'Beijing'}, close=0.05),
  [bed(ANT, 0, 8)], mt=2000, src=['lit', 'usgs-coal-china-2014'],
  worked='coal supplied Yuan Dadu (Beijing) from the Western Hills')
A('coal:henan', 'Henan coalfields (Pingdingshan, Hebi, Jiaozuo, Yima)', 'C_coal', CN, 'Permian',
  coal(CNs, [110.3, 31.3, 116.7, 36.4], {'Province': 'Henan'}, close=0.05),
  [bed(BIT, 0, 10, 0.6), bed(ANT, 50, 6, 0.4)], mt=40000, src=['lit', 'usgs-coal-china-2014'],
  worked='Song-dynasty coal mines excavated at Hebi')
A('coal:hebei-kaiping', 'Hebei coalfields (Kaiping, Fengfeng, Xingtai)', 'C_coal', CN, 'Carboniferous',
  coal(CNs, [113.4, 36.0, 119.9, 42.6], {'Province': 'Hebei', 'Rank': 'Bituminous'}, close=0.05),
  [bed(BIT, 50, 10)], mt=20000, src=['lit', 'usgs-coal-china-2014'])
A('coal:shandong', 'Shandong coalfields (Zibo, Yanzhou, Zaozhuang)', 'C_coal', CN, 'Carboniferous',
  coal(CNs, [114.8, 34.3, 122.7, 38.4], {'Province': 'Shandong'}, close=0.05),
  [bed(BIT, 150, 10)], mt=30000, src=['lit', 'usgs-coal-china-2014'],
  worked='Zibo (Boshan) coal worked in the Song')
A('coal:huainan-huaibei', 'Huainan and Huaibei coalfields', 'C_coal', CN, 'Permian',
  coal(CNs, [114.8, 29.4, 119.7, 34.7], {'Province': ['Anhui', 'Jiangsu']}, close=0.05),
  [bed(BIT, 300, 20)], mt=60000, src=['lit', 'usgs-coal-china-2014'])
A('coal:guizhou', 'Guizhou coalfields (Liupanshui, Zhina)', 'C_coal', CN, 'Permian',
  coal(CNs, [103.5, 24.5, 109.6, 29.3], {'Province': 'Guizhou'}, close=0.05),
  [bed(BIT, 0, 15, 0.5), bed(ANT, 50, 10, 0.5)], mt=60000, src=['lit', 'usgs-coal-china-2014'])
A('coal:heilongjiang', 'Heilongjiang coalfields (Hegang, Jixi, Shuangyashan, Qitaihe)', 'C_coal', CN, 'Cretaceous',
  coal(CNs, [121.0, 43.4, 135.1, 53.6], {'Province': 'Heilongjiang', 'Rank': 'Bituminous'}, close=0.05),
  [bed(BIT, 20, 10)], mt=20000, src=['lit', 'usgs-coal-china-2014'])
A('coal:inner-mongolia-lignite', 'Inner Mongolian lignite (Shengli, Huolinhe, Yimin, Baorixile)', 'C_coal', CN, 'Cretaceous',
  coal(CNs, [111.0, 42.0, 126.0, 50.5], {'Province': 'Inner Mongolia', 'Rank': 'Lignite'}, close=0.05),
  [bed(LIG, 30, 40)], mt=150000, src=['lit', 'usgs-coal-china-2014'])
A('coal:xinjiang', 'Xinjiang Jurassic coal (Junggar, Turpan-Hami, Ili)', 'C_coal', CN, 'Jurassic',
  coal(CNs, [73.0, 34.0, 96.5, 49.2], {'Province': 'Xinjiang', 'Rank': 'Bituminous'}, close=0.05),
  [bed(BIT, 30, 40)], mt=800000, src=['lit', 'usgs-coal-china-2014'])
A('coal:yunnan-lignite', 'Yunnan Neogene lignite (Xiaolongtan, Zhaotong)', 'C_coal', CN, 'Neogene',
  coal(CNs, [97.5, 21.1, 106.2, 29.3], {'Province': 'Yunnan', 'Rank': 'Lignite'}, close=0.05),
  [bed(LIG, 20, 40)], mt=15000, src=['lit', 'usgs-coal-china-2014'])

# ---- India, Pakistan
IN, INs = 'India', 'usgs_india_2011'
A('coal:damodar', 'Damodar valley coalfields (Raniganj, Jharia, Bokaro, Karanpura)', 'C_coal', IN, 'Permian',
  coal(INs, [84.5, 23.2, 87.6, 24.2], {'Coalfield': ['Raniganj', 'Jharia', 'Bokaro', 'Karanpura', 'Ramgarh', 'Giridih', 'Auranga', 'Hutar', 'Daltonganj']}, close=0.03),
  [bed(BIT, 0, 40)], mt=100000, q='B', src=['lit', 'usgs-coal-india-2011'])
A('coal:mahanadi', 'Mahanadi coalfields (Talcher, Ib River)', 'C_coal', IN, 'Permian',
  coal(INs, [83.0, 20.5, 85.5, 22.5], close=0.03),
  [bed(BIT, 0, 50)], mt=80000, q='B', src=['lit', 'usgs-coal-india-2011'])
A('coal:son-hasdeo', 'Son and Hasdeo coalfields (Singrauli, Korba, Mand-Raigarh, Hasdo-Arand, Sohagpur)', 'C_coal', IN, 'Permian',
  coal(INs, [80.5, 21.5, 84.0, 24.5], close=0.03), [bed(BIT, 0, 40)], mt=110000, src=['lit', 'usgs-coal-india-2011'])
A('coal:godavari', 'Pranhita-Godavari coalfields (Singareni)', 'C_coal', IN, 'Permian',
  coal(INs, [78.8, 16.8, 81.6, 19.9], close=0.03), [bed(BIT, 0, 20)], mt=23000, src=['lit', 'usgs-coal-india-2011'])
A('coal:neyveli', 'Neyveli lignite', 'C_coal', IN, 'Neogene',
  ring((79.40, 11.48), (79.60, 11.48), (79.60, 11.65), (79.40, 11.65)), [bed(LIG, 70, 20)], mt=4000)
A('coal:thar', 'Thar lignite field', 'C_coal', 'Pakistan', 'Paleogene',
  ring((69.9, 24.6), (70.9, 24.6), (71.1, 25.3), (70.2, 25.6), (69.8, 25.1)), [bed(LIG, 150, 20)], mt=175000, q='B')

# ---- East and Southeast Asia
A('coal:tavan-tolgoi', 'Tavan Tolgoi coalfield', 'C_coal', 'Mongolia', 'Permian',
  ring((105.30, 43.50), (106.20, 43.50), (106.20, 43.80), (105.30, 43.80)), [bed(BIT, 0, 30)], mt=7500, q='B')
A('coal:east-mongolia-lignite', 'East Mongolian lignite basins (Baganuur, Choir-Nyalga, Shivee Ovoo, Aduunchuluun)', 'C_coal', 'Mongolia', 'Cretaceous',
  union(ring((107.9, 45.9), (109.2, 45.9), (109.4, 47.0), (108.9, 47.9), (108.0, 47.8), (107.8, 46.8)),
        ring((113.6, 47.6), (115.2, 47.6), (115.2, 48.5), (113.6, 48.5))), [bed(LIG, 15, 30)], mt=50000)
A('coal:quang-ninh', 'Quang Ninh anthracite (Hon Gai, Cam Pha)', 'C_coal', 'Vietnam', 'Triassic',
  ring((106.60, 20.98), (107.45, 20.98), (107.45, 21.25), (106.60, 21.18)), [bed(ANT, 0, 20)], mt=10000)
A('coal:pyongan', 'Pyongan anthracite (South Pyongan; Samcheok)', 'C_coal', 'North Korea', 'Permian',
  union(ring((125.6, 39.1), (126.3, 39.1), (126.3, 39.5), (125.6, 39.5)),
        ring((129.0, 37.1), (129.3, 37.1), (129.3, 37.4), (129.0, 37.4))), [bed(ANT, 0, 8)], mt=6000)
A('coal:kyushu', 'Chikuho and Miike coalfields (Kyushu)', 'C_coal', 'Japan', 'Paleogene',
  union(ring((130.55, 33.50), (130.85, 33.50), (130.85, 33.85), (130.55, 33.85)),
        ring((130.38, 32.95), (130.55, 32.95), (130.55, 33.10), (130.38, 33.10))), [bed(BIT, 0, 8)], mt=5000)
A('coal:ishikari', 'Ishikari coalfield (Hokkaido)', 'C_coal', 'Japan', 'Paleogene',
  ring((141.75, 42.90), (142.20, 42.90), (142.20, 43.60), (141.75, 43.60)), [bed(BIT, 0, 10)], mt=7000)
A('coal:mae-moh', 'Mae Moh lignite', 'C_coal', 'Thailand', 'Neogene',
  ring((99.65, 18.25), (99.80, 18.25), (99.80, 18.40), (99.65, 18.40)), [bed(LIG, 30, 30)], mt=1100, q='B')
A('coal:kutai', 'Kutai basin coal (East Kalimantan)', 'C_coal', 'Indonesia', 'Neogene',
  ring((116.6, -1.2), (117.6, -1.2), (118.0, 0.0), (117.7, 1.0), (116.9, 0.8), (116.4, -0.2)), [bed(BIT, 0, 15)], mt=50000)
A('coal:barito', 'Barito basin coal (South Kalimantan)', 'C_coal', 'Indonesia', 'Paleogene',
  ring((114.6, -3.8), (115.9, -3.9), (116.1, -2.5), (115.4, -1.6), (114.8, -2.3)), [bed(BIT, 0, 15)], mt=30000)
A('coal:south-sumatra', 'South Sumatra basin lignite (Muara Enim)', 'C_coal', 'Indonesia', 'Neogene',
  ring((102.9, -4.4), (104.6, -4.2), (104.9, -3.0), (103.8, -2.4), (102.9, -3.2)), [bed(LIG, 10, 30)], mt=50000)

# ---- Australia, New Zealand
AU = 'Australia'
A('coal:bowen', 'Bowen basin', 'C_coal', AU, 'Permian', ga('Bowen Basin'), [bed(BIT, 30, 15)], mt=50000, src=['lit', 'ga-basins-2013'])
A('coal:sydney-gunnedah', 'Sydney and Gunnedah basin coalfields (Hunter, Newcastle, Western, Southern, Gunnedah)', 'C_coal', AU, 'Permian',
  coal('nsw_coalfields', [148.0, -35.5, 153.0, -29.5], close=0.0), [bed(BIT, 50, 15)], mt=70000, src=['lit', 'nsw-coalfields'])
A('coal:surat', 'Surat basin (Walloon Coal Measures)', 'C_coal', AU, 'Jurassic', ga('Surat Basin'), [bed(BIT, 50, 10)], mt=25000, src=['lit', 'ga-basins-2013'])
A('coal:galilee', 'Galilee basin', 'C_coal', AU, 'Permian', ga('Galilee Basin'), [bed(BIT, 50, 10)], mt=30000, src=['lit', 'ga-basins-2013'])
A('coal:latrobe-valley', 'Latrobe Valley brown coal (Morwell, Yallourn, Loy Yang)', 'C_coal', AU, 'Neogene',
  coal('victoria_coalfields', [145.9, -38.5, 146.9, -38.0], {'coal_type': 'Brown'}, close=0.02),
  [bed(LIG, 15, 150)], mt=65000, q='B', src=['lit', 'vic-coalfields'])
A('coal:buller', 'Buller and Greymouth coalfields (West Coast)', 'C_coal', 'New Zealand', 'Paleogene',
  ring((171.45, -41.95), (171.85, -41.62), (172.00, -41.75), (171.70, -42.05), (171.35, -42.45), (171.25, -42.35)), [bed(BIT, 0, 10)], mt=1000)

# ---- Africa
ZA = 'South Africa'
A('coal:witbank-highveld', 'Witbank and Highveld coalfields', 'C_coal', ZA, 'Permian',
  ring((28.6, -26.8), (29.9, -26.6), (30.3, -25.8), (29.6, -25.6), (28.6, -25.9), (28.4, -26.4)), [bed(BIT, 20, 10)], mt=35000)
A('coal:waterberg', 'Waterberg coalfield', 'C_coal', ZA, 'Permian',
  ring((27.2, -23.9), (28.0, -23.8), (28.0, -23.4), (27.2, -23.4)), [bed(BIT, 20, 60)], mt=75000)
A('coal:kwazulu-natal', 'KwaZulu-Natal coalfields (Vryheid, Utrecht, Klip River)', 'C_coal', ZA, 'Permian',
  ring((29.9, -28.3), (31.0, -28.0), (31.2, -27.5), (30.2, -27.4), (29.7, -27.9)), [bed(BIT, 0, 4, 0.6), bed(ANT, 0, 3, 0.4)], mt=8000)
A('coal:moatize', 'Moatize coalfield (Tete)', 'C_coal', 'Mozambique', 'Permian',
  ring((33.4, -16.3), (34.1, -16.0), (34.1, -15.7), (33.4, -15.9)), [bed(BIT, 0, 30)], mt=25000)
A('coal:hwange', 'Hwange coalfield', 'C_coal', 'Zimbabwe', 'Permian',
  ring((26.0, -18.6), (27.3, -18.2), (27.0, -17.7), (25.9, -18.1)), [bed(BIT, 20, 10)], mt=5000)
A('coal:botswana-kalahari', 'Botswana Karoo coalfields (Morupule, Mmamabula)', 'C_coal', 'Botswana', 'Permian',
  ring((25.5, -23.5), (27.5, -23.5), (27.5, -21.5), (25.5, -21.5)), [bed(BIT, 100, 10)], mt=200000)
A('coal:enugu', 'Enugu coalfield', 'C_coal', 'Nigeria', 'Cretaceous',
  ring((7.30, 6.20), (7.60, 6.20), (7.60, 6.70), (7.30, 6.70)), [bed(LIG, 0, 3)], mt=2000)

# ---- North America (USGS coal fields)
US, USs, AMs = 'United States of America', 'usgs_us_2012', 'usgs_americas_2008'
PA_ANTH = [-77.3, 40.3, -75.4, 41.7]
A('coal:appalachian-north', 'Northern Appalachian coal region (Pittsburgh, Upper Freeport)', 'C_coal', US, 'Carboniferous',
  coal(USs, [-83.0, 38.7, -77.0, 42.2], {'NAME': 'Appalachian Region'}, notbox=[PA_ANTH]),
  [bed(BIT, 0, 6)], mt=120000, src=['lit', 'usgs-coal-us-2012'])
A('coal:appalachian-central', 'Central Appalachian coal region', 'C_coal', US, 'Carboniferous',
  coal(USs, [-85.2, 36.2, -79.5, 38.7], {'NAME': 'Appalachian Region'}), [bed(BIT, 0, 8)], mt=110000, src=['lit', 'usgs-coal-us-2012'])
A('coal:appalachian-south', 'Southern Appalachian coal region (Warrior, Cahaba, Coosa, Tennessee)', 'C_coal', US, 'Carboniferous',
  coal(USs, [-88.5, 32.5, -84.0, 36.2], {'NAME': 'Appalachian Region'}), [bed(BIT, 0, 6)], mt=40000, src=['lit', 'usgs-coal-us-2012'])
A('coal:pennsylvania-anthracite', 'Pennsylvania anthracite region', 'C_coal', US, 'Carboniferous',
  coal(USs, PA_ANTH, {'NAME': 'Pennsylvania Anthracite'}, close=0.0), [bed(ANT, 0, 20)], mt=21000, q='B', src=['lit', 'usgs-coal-us-2012'])
A('coal:illinois', 'Illinois basin', 'C_coal', US, 'Carboniferous',
  coal(USs, [-92.0, 36.5, -85.5, 42.5], {'PROVINCE': 'Interior', 'NAME': 'Eastern Region'}), [bed(BIT, 30, 6)], mt=300000, src=['lit', 'usgs-coal-us-2012'])
A('coal:western-interior', 'Western Interior coal region (Iowa, Missouri, Kansas, Oklahoma)', 'C_coal', US, 'Carboniferous',
  coal(USs, [-98.5, 33.5, -91.0, 43.0], {'PROVINCE': 'Interior', 'NAME': 'Western Region'}), [bed(BIT, 10, 2)], mt=50000, src=['lit', 'usgs-coal-us-2012'])
A('coal:powder-river', 'Powder River basin (Fort Union, Wyodak)', 'C_coal', US, 'Paleogene',
  coal(USs, [-107.6, 42.6, -104.6, 46.4], {'NAME': 'Powder River Region'}), [bed(LIG, 30, 30)], mt=970000, q='B', src=['usgs-pp1809', 'usgs-coal-us-2012'])
A('coal:fort-union-lignite', 'Fort Union lignite region (Williston basin)', 'C_coal', US, 'Paleogene',
  coal(USs, [-108.5, 45.0, -98.5, 49.0], {'NAME': 'Fort Union Region'}), [bed(LIG, 10, 10)], mt=300000, src=['lit', 'usgs-coal-us-2012'])
A('coal:green-river-hams-fork', 'Green River, Hams Fork and Hanna coal regions', 'C_coal', US, 'Cretaceous',
  coal(USs, [-111.5, 39.8, -105.5, 43.5], {'NAME': ['Green River Region', 'Hams Fork Region', 'Hanna Field']}), [bed(BIT, 50, 10)], mt=100000, src=['lit', 'usgs-coal-us-2012'])
A('coal:uinta-piceance', 'Uinta region (Book Cliffs, Wasatch Plateau, Piceance)', 'C_coal', US, 'Cretaceous',
  coal(USs, [-112.5, 37.5, -106.5, 40.6], {'NAME': 'Uinta Region'}), [bed(BIT, 50, 8)], mt=80000, src=['lit', 'usgs-coal-us-2012'])
A('coal:san-juan', 'San Juan basin (Fruitland)', 'C_coal', US, 'Cretaceous',
  coal(USs, [-109.6, 34.5, -106.5, 37.6], {'NAME': 'San Juan River Region'}), [bed(BIT, 100, 10)], mt=150000, src=['lit', 'usgs-coal-us-2012'])
A('coal:gulf-lignite', 'Gulf Coast lignite (Wilcox, Jackson; Texas to Mississippi)', 'C_coal', US, 'Paleogene',
  coal([USs, AMs], [-100.5, 27.5, -87.5, 34.5], {'coal_rank_source': 'Lignite'}), [bed(LIG, 20, 5)], mt=100000, src=['lit', 'usgs-coal-us-2012', 'usgs-coal-americas-2008'])
A('coal:alaska-north-slope', 'North Slope of Alaska (Nanushuk, Colville)', 'C_coal', US, 'Cretaceous',
  coal(AMs, [-166.0, 67.5, -145.0, 71.0]), [bed(BIT, 30, 30)], mt=2000000, src=['lit', 'usgs-coal-americas-2008'])
CA = 'Canada'
A('coal:alberta-plains', 'Alberta plains coal (Horseshoe Canyon, Paskapoo, Scollard)', 'C_coal', CA, 'Cretaceous',
  coal(AMs, [-116.0, 49.0, -110.0, 55.5], {'coal_rank_source': ['Subbituminous', 'Lignite']}), [bed(LIG, 30, 6)], mt=600000, src=['lit', 'usgs-coal-americas-2008'])
A('coal:rocky-foothills', 'Rocky Mountain foothills and Elk Valley (Kootenay, Mist Mountain)', 'C_coal', CA, 'Jurassic',
  coal(AMs, [-122.5, 49.0, -114.0, 56.5], {'coal_rank_source': 'Bituminous'}), [bed(BIT, 0, 20)], mt=60000, src=['lit', 'usgs-coal-americas-2008'])
A('coal:sydney-nova-scotia', 'Sydney coalfield (Cape Breton)', 'C_coal', CA, 'Carboniferous',
  ring((-60.40, 46.05), (-60.10, 46.15), (-59.80, 46.22), (-59.85, 46.30), (-60.20, 46.27), (-60.45, 46.15)), [bed(BIT, 0, 8)], mt=2000)
A('coal:sabinas', 'Sabinas basin (Coahuila)', 'C_coal', 'Mexico', 'Cretaceous',
  ring((-101.6, 27.6), (-100.9, 27.5), (-100.6, 28.3), (-101.3, 28.5)), [bed(BIT, 50, 3)], mt=3000)
A('coal:cerrejon', 'Cerrejon coalfield (La Guajira)', 'C_coal', 'Colombia', 'Paleogene',
  ring((-72.85, 10.90), (-72.45, 11.00), (-72.35, 11.25), (-72.60, 11.30), (-72.85, 11.15)), [bed(BIT, 0, 40)], mt=5000)
A('coal:cesar', 'Cesar coalfields (La Loma, Calenturitas, La Jagua)', 'C_coal', 'Colombia', 'Paleogene',
  ring((-73.75, 9.35), (-73.35, 9.35), (-73.25, 9.95), (-73.60, 9.95)), [bed(BIT, 0, 30)], mt=6000)
A('coal:rio-grande-do-sul', 'Parana basin coal of Rio Grande do Sul (Candiota, Leao)', 'C_coal', 'Brazil', 'Permian',
  ring((-54.2, -31.9), (-52.0, -31.0), (-51.5, -29.8), (-52.5, -29.6), (-54.0, -30.6)), [bed(BIT, 20, 4)], mt=28000)
A('coal:magallanes', 'Magallanes basin lignite (Pecket, Isla Riesco)', 'C_coal', 'Chile', 'Neogene',
  ring((-71.9, -53.2), (-70.9, -53.2), (-70.9, -52.4), (-71.9, -52.4)), [bed(LIG, 10, 10)], mt=5000)
A('coal:svalbard', 'Svalbard coal (Longyearbyen, Svea)', 'C_coal', 'Norway', 'Paleogene',
  ring((15.30, 78.00), (16.60, 78.00), (16.60, 78.30), (15.30, 78.30)), [bed(BIT, 0, 2)], mt=3000)

# ============================================================================================== EVAPORITES
# Salt and potash basins on the USGS potash tracts (SIR 2010-5090-S). Halite beds have no published tonnage (the
# resource is effectively unlimited); their cover is authored. Potash tonnage is ore (sylvinite, carnallite).
A('evap:zechstein', 'Zechstein basin, bedded salt and potash (Germany, Netherlands, Poland, Denmark)', 'K', DE, 'Permian',
  potash('150sbK0010e'), [bed('halite', 500, 250, 0.9), bed('sylvite', 600, 6)], mt=20000, src=['usgs-potash-2014', 'lit'],
  also=['NaCl'], worked='Zechstein brine and salt springs (Lueneburg, Halle, Stassfurt) boiled in the Middle Ages')
A('evap:zechstein-domes', 'Zechstein salt structures (North German and Polish salt domes and pillows)', 'NaCl', DE, 'Permian',
  potash('150haK0010b'), [bed('halite', 300, 1000, 0.35)], src=['usgs-potash-2014'])
A('evap:boulby', 'Zechstein of North Yorkshire (Boulby potash and polyhalite, Teesside salt)', 'K', UK, 'Permian',
  potash('150sbK0010g'), [bed('halite', 1000, 40, 1.0), bed('sylvite', 1100, 7)], mt=1500, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:upper-kama', 'Upper Kama potash basin (Verkhnekamskoye; Solikamsk, Berezniki)', 'K', RU, 'Permian',
  potash('150sbK0045a'), [bed('halite', 250, 400, 1.0), bed('sylvite', 270, 20)], mt=20000, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:elk-point', 'Elk Point basin, Prairie Evaporite potash (Saskatchewan)', 'K', CA, 'Devonian',
  potash('003sbK0001a', '003sbK0001b', '003sbK0001d'), [bed('halite', 900, 150, 0.9), bed('sylvite', 950, 8)], mt=150000,
  src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:pripyat', 'Pripyat trough potash (Soligorsk, Starobin)', 'K', 'Belarus', 'Devonian',
  potash('150sbK0043'), [bed('halite', 400, 500, 0.8), bed('sylvite', 400, 10)], mt=15000, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:khorat', 'Khorat and Sakon Nakhon basins (Maha Sarakham Formation)', 'K', 'Thailand', 'Cretaceous',
  potash('142mxK0001a', '142mxK0001b', '142mxK0001c'), [bed('halite', 150, 200, 0.8), bed('sylvite', 200, 10)], mt=50000,
  src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:permian-salado', 'Delaware basin Salado potash (Carlsbad district)', 'K', US, 'Permian',
  potash('003sbK0003b'), [bed('halite', 300, 600, 1.0), bed('sylvite', 300, 3)], mt=2000, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:permian-basin-salt', 'Permian basin bedded salt (Kansas to Texas: Wellington, Blaine, Salado)', 'NaCl', US, 'Permian',
  potash('003sbK0003', minus=potash('003sbK0003b')), [bed('halite', 300, 150, 0.6)], src=['usgs-potash-2014'],
  )
A('evap:paradox', 'Paradox basin salt and potash (Moab)', 'K', US, 'Carboniferous',
  potash('003haK0002'), [bed('halite', 900, 1000, 0.6), bed('sylvite', 900, 5)], mt=5000, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:michigan-salina', 'Michigan and Appalachian basins, Salina salt (Detroit, Goderich, Retsof)', 'NaCl', US, 'Silurian',
  potash('003sbK0004', '003sbK0007'), [bed('halite', 400, 150, 0.7)], src=['usgs-potash-2014'])
A('evap:maritimes', 'Maritimes basin, Windsor salt and potash (Sussex, Pugwash)', 'K', CA, 'Carboniferous',
  potash('003haK0005a', '003sbK0005b'), [bed('halite', 300, 300, 0.5), bed('sylvite', 400, 5)], mt=1000, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:gulf-louann', 'Gulf of Mexico basin, Louann salt (mother salt; domes listed separately)', 'NaCl', US, 'Jurassic',
  potash('003haK0010'), [bed('halite', 3000, 1000, 0.5)], src=['usgs-potash-2014'])
A('evap:pricaspian', 'Pricaspian (Caspian) depression salt domes', 'NaCl', KZ, 'Permian',
  potash('142haK0007a'), [bed('halite', 500, 2000, 0.4)], src=['usgs-potash-2014'])
A('evap:cisuralian', 'Cisuralian salt (southern Urals foredeep; Sol-Iletsk)', 'NaCl', RU, 'Permian',
  potash('150haK0045c'), [bed('halite', 100, 1000, 0.4)], src=['usgs-potash-2014'])
A('evap:dnieper-donets', 'Dnieper-Donets salt (Soledar, Artemivsk)', 'NaCl', UA, 'Permian',
  potash('150sbK0042a', '150haK0042b', '150sbK0042c'), [bed('halite', 200, 300, 0.5)], src=['usgs-potash-2014'])
A('evap:carpathian', 'Carpathian foredeep salt and potash (Kalush, Stebnyk)', 'K', UA, 'Neogene',
  potash('150haK0005'), [bed('halite', 50, 100, 0.5), bed('sylvite', 100, 5)], mt=2500, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:transylvania', 'Transylvanian basin salt (Ocna Mures, Dej, Ocna Sibiului)', 'NaCl', 'Romania', 'Neogene',
  potash('150mxK0004'), [bed('halite', 30, 500, 0.3)], src=['usgs-potash-2014', 'lit'],
  worked='rock salt mined at Turda, Dej and Ocna Sibiului in the 11th-13th centuries')
A('evap:south-pyrenean', 'South Pyrenean (Catalan) potash basin (Cardona, Suria, Sallent)', 'K', 'Spain', 'Paleogene',
  potash('150mxK0006'), [bed('halite', 0, 300, 0.6), bed('sylvite', 400, 10)], mt=1500, src=['usgs-potash-2014', 'lit'], also=['NaCl'],
  worked='Cardona salt mountain worked since prehistory; medieval salt trade')
A('evap:rhine-graben', 'Upper Rhine graben potash (Alsace, Buggingen)', 'K', FR, 'Paleogene',
  potash('150sbK0011'), [bed('halite', 600, 200, 0.8), bed('sylvite', 600, 5)], mt=500, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:aquitaine', 'Aquitaine Triassic salt structures (Salies-de-Bearn, Dax)', 'NaCl', FR, 'Triassic',
  potash('150haK0002'), [bed('halite', 300, 500, 0.3)], src=['usgs-potash-2014'],
  worked='Salies-de-Bearn brine worked in the Middle Ages')
A('evap:dead-sea', 'Dead Sea basin (Sedom Formation; Dead Sea brines)', 'K', 'Israel', 'Neogene',
  potash('142haK0010'), [bed('halite', 0, 2000, 0.3), bed('sylvite', 0, 5)], mt=2000, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:zagros', 'Zagros Gachsaran evaporites', 'NaCl', 'Iran', 'Neogene',
  potash('142sbK0008b'), [bed('halite', 500, 300, 0.4)], src=['usgs-potash-2014'])
A('evap:hormuz', 'Hormuz salt diapirs (Persian Gulf)', 'NaCl', 'Iran', 'Cambrian',
  potash('142haK0008a'), [bed('halite', 1000, 1000, 0.2)], src=['usgs-potash-2014'])
A('evap:qom-kavir', 'Qom and Great Kavir salt (Lower and Upper Red Formations)', 'NaCl', 'Iran', 'Paleogene',
  potash('142haK0009'), [bed('halite', 200, 500, 0.3)], src=['usgs-potash-2014'])
A('evap:east-siberian', 'East Siberian Cambrian salt (Angara-Lena, Nepa)', 'NaCl', RU, 'Cambrian',
  potash('150sbK0050'), [bed('halite', 800, 500, 0.6)], src=['usgs-potash-2014'])
A('evap:amazonas', 'Amazon basin Nova Olinda salt and potash (Autazes)', 'K', 'Brazil', 'Carboniferous',
  potash('005sbK0004'), [bed('halite', 1000, 200, 0.5), bed('sylvite', 700, 3)], mt=3000, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:sergipe', 'Sergipe potash (Taquari-Vassouras)', 'K', 'Brazil', 'Cretaceous',
  potash('005sbK0002'), [bed('halite', 400, 100, 0.6), bed('sylvite', 450, 5)], mt=500, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:neuquen', 'Neuquen basin potash (Huitrin Formation)', 'K', 'Argentina', 'Cretaceous',
  potash('005sbK0001'), [bed('halite', 1000, 100, 0.5), bed('sylvite', 1000, 5)], mt=1500, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:lower-congo', 'Lower Congo potash (Loeme Formation; Kouilou)', 'K', 'Republic of the Congo', 'Cretaceous',
  potash('002sbK0001a'), [bed('halite', 300, 400, 0.6), bed('sylvite', 400, 5)], mt=5000, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:danakil', 'Danakil depression salt and potash (Dallol)', 'K', 'Ethiopia', 'Quaternary',
  potash('002sbK0005'), [bed('halite', 0, 500, 0.8), bed('sylvite', 50, 10)], mt=3000, src=['usgs-potash-2014', 'lit'], also=['NaCl'],
  worked='Afar salt bars (amole) cut on the Dallol salt flat and traded across Ethiopia')
A('evap:khemisset', 'Moroccan Meseta Triassic salt (Khemisset potash)', 'K', 'Morocco', 'Triassic',
  potash('002mxK0006'), [bed('halite', 300, 200, 0.4), bed('sylvite', 400, 5)], mt=500, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:saharan', 'Saharan Triassic salt (Algeria, Tunisia, Libya)', 'NaCl', 'Algeria', 'Triassic',
  potash('002mxK0007'), [bed('halite', 1500, 500, 0.4)], src=['usgs-potash-2014'])
A('evap:gissar-amudarya', 'Gaurdak salt and potash (Gissar, Amu Darya)', 'K', 'Turkmenistan', 'Jurassic',
  potash('142mxK0005a', '142sbK0005b'), [bed('halite', 300, 500, 0.5), bed('sylvite', 300, 5)], mt=2000, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:lop-nur', 'Lop Nur potash brines and salt crust (Luobupo)', 'K', CN, 'Quaternary',
  potash('142brK0004a'), [bed('earth-saltflat', 0, 5, 0.8), bed('earth-saltflat-sylvite', 0, 1)], mt=2000, src=['usgs-potash-2014', 'lit'], also=['NaCl'])
A('evap:qarhan', 'Qarhan salt lake (Qaidam basin)', 'K', CN, 'Quaternary',
  ring((94.2, 36.6), (96.2, 36.6), (96.2, 37.2), (94.2, 37.2)), [bed('earth-saltflat', 0, 15, 0.9), bed('earth-saltflat-sylvite', 0, 1)],
  mt=1000, src=['lit', 'usgs-potash-2014'], also=['NaCl'])
A('evap:simao', 'Simao and Lanping salt (Mengyejing; Yunnan brine wells)', 'NaCl', CN, 'Cretaceous',
  potash('142haK0001d', '142mxK0001e'), [bed('halite', 100, 200, 0.3)], src=['usgs-potash-2014', 'lit'],
  worked='Yunnan brine wells (Heijing, Mojiang) worked in the Tang to Yuan')
A('evap:zigong', 'Sichuan basin Triassic brine and salt (Zigong)', 'NaCl', CN, 'Triassic',
  ring((104.45, 29.15), (105.00, 29.15), (105.00, 29.50), (104.45, 29.50)), [bed('halite', 1000, 50, 0.6)],
  worked='Zigong brine wells drilled since the Han; percussion-drilled wells in the Song')
A('evap:salt-range', 'Salt Range (Khewra, Warcha, Kalabagh)', 'NaCl', 'Pakistan', 'Cambrian',
  ring((71.55, 32.60), (72.40, 32.55), (73.20, 32.72), (73.55, 32.95), (73.00, 33.02), (72.20, 32.88), (71.55, 32.88)),
  [bed('halite', 0, 300, 0.3)], worked='Khewra rock salt traded by the 13th century')
A('evap:cheshire', 'Cheshire salt field (Northwich Halite)', 'NaCl', UK, 'Triassic',
  ring((-2.75, 53.05), (-2.25, 53.05), (-2.25, 53.35), (-2.75, 53.35)), [bed('halite', 50, 280, 0.7)],
  worked='brine boiled at Nantwich, Middlewich and Northwich since Roman times (Domesday wiches)')
A('evap:droitwich', 'Droitwich salt (Worcestershire)', 'NaCl', UK, 'Triassic',
  ring((-2.25, 52.22), (-2.08, 52.22), (-2.08, 52.32), (-2.25, 52.32)), [bed('halite', 60, 60, 0.6)],
  worked='Droitwich brine works in Domesday and throughout the Middle Ages')
A('evap:lorraine-salt', 'Lorraine Keuper salt (Dieuze, Vic, Varangeville)', 'NaCl', FR, 'Triassic',
  ring((6.20, 48.55), (6.90, 48.65), (6.95, 48.90), (6.35, 48.85)), [bed('halite', 60, 60, 0.7)],
  worked='salines of the Seille valley (Vic, Marsal, Dieuze) worked since antiquity and in the Middle Ages')
A('evap:salins', 'Salins-les-Bains brine (Jura Triassic salt)', 'NaCl', FR, 'Triassic',
  ring((5.80, 46.90), (5.95, 46.90), (5.95, 47.00), (5.80, 47.00)), [bed('halite', 150, 50, 0.6)],
  worked='Salins brine works, among the great medieval salt works')
A('evap:muschelkalk-wuerttemberg', 'Middle Muschelkalk salt of Wuerttemberg (Heilbronn, Bad Friedrichshall, Schwaebisch Hall)', 'NaCl', DE, 'Triassic',
  ring((9.05, 49.05), (9.55, 49.05), (9.85, 49.15), (9.85, 49.35), (9.30, 49.35), (9.05, 49.25)), [bed('halite', 150, 40, 0.6)],
  worked='Schwaebisch Hall brine spring worked in the Middle Ages')

# ---- salt flats (playas; earth-saltflat in the playa layer)
A('evap:uyuni', 'Salar de Uyuni', 'NaCl', 'Bolivia', 'Quaternary',
  ring((-68.25, -20.05), (-67.80, -19.75), (-67.30, -19.80), (-66.95, -20.20), (-67.05, -20.55), (-67.55, -20.60), (-68.10, -20.45)),
  [bed('earth-saltflat', 0, 10, 0.95), bed('earth-saltflat-sylvite', 0, 0.5, 0.5)], also=['K'])
A('evap:atacama-salar', 'Salar de Atacama', 'K', 'Chile', 'Quaternary',
  ring((-68.45, -23.10), (-68.05, -23.10), (-68.00, -23.75), (-68.30, -23.85), (-68.50, -23.45)),
  [bed('earth-saltflat', 0, 15, 0.9), bed('earth-saltflat-sylvite', 0, 1)], mt=500, src=['lit', 'usgs-potash-2014'], also=['NaCl'])
A('evap:bonneville', 'Bonneville Salt Flats and Great Salt Lake Desert', 'K', US, 'Quaternary',
  ring((-114.00, 40.60), (-113.60, 40.60), (-113.45, 40.95), (-113.80, 41.10), (-114.05, 40.90)),
  [bed('earth-saltflat', 0, 2, 0.9), bed('earth-saltflat-sylvite', 0, 0.3)], mt=50, src=['lit', 'usgs-potash-2014'], also=['NaCl'])
A('evap:taghaza-taoudenni', 'Taghaza and Taoudenni rock-salt pans', 'NaCl', 'Mali', 'Quaternary',
  union(ring((-5.10, 23.50), (-4.90, 23.50), (-4.90, 23.70), (-5.10, 23.70)), ring((-4.10, 22.55), (-3.85, 22.55), (-3.85, 22.80), (-4.10, 22.80))),
  [bed('earth-saltflat', 0, 3, 0.8)], worked='Taghaza rock salt carried to Mali and Timbuktu by caravan (14th century)')
A('evap:bilma', 'Kawar salt pans (Bilma, Fachi)', 'NaCl', 'Niger', 'Quaternary',
  ring((12.85, 18.60), (13.00, 18.60), (13.00, 18.80), (12.85, 18.80)), [bed('earth-saltflat', 0, 2, 0.6)],
  worked='Bilma salt traded across the central Sahara in the Middle Ages')
A('evap:sambhar', 'Sambhar Lake', 'NaCl', IN, 'Quaternary',
  ring((74.90, 26.85), (75.25, 26.88), (75.25, 27.02), (74.95, 27.02)), [bed('earth-saltflat', 0, 1, 0.8)],
  worked='salt made at Sambhar since antiquity')
A('evap:elton-baskunchak', 'Lakes Elton and Baskunchak', 'NaCl', RU, 'Quaternary',
  union(ring((46.55, 49.05), (46.78, 49.05), (46.78, 49.22), (46.55, 49.22)), ring((46.80, 48.13), (46.98, 48.13), (46.98, 48.27), (46.80, 48.27))),
  [bed('earth-saltflat', 0, 10, 0.9)], worked='Baskunchak salt used by the Golden Horde (Saray) in the 14th century')
A('evap:tuz-golu', 'Tuz Golu (Lake Tuz)', 'NaCl', 'Turkey', 'Quaternary',
  ring((33.15, 38.55), (33.55, 38.65), (33.65, 39.05), (33.35, 39.10), (33.20, 38.85)), [bed('earth-saltflat', 0, 1, 0.8)],
  worked='salt collected at Tuz Golu since antiquity')
A('evap:chott-djerid', 'Chott el Djerid', 'NaCl', 'Tunisia', 'Quaternary',
  ring((7.90, 33.60), (8.70, 33.55), (9.00, 33.80), (8.40, 34.00), (7.95, 33.85)), [bed('earth-saltflat', 0, 1, 0.7)])
A('evap:lake-eyre', 'Kati Thanda-Lake Eyre North', 'NaCl', AU, 'Quaternary',
  ring((136.85, -28.95), (137.70, -28.90), (137.95, -28.35), (137.45, -28.05), (137.00, -28.40)), [bed('earth-saltflat', 0, 0.5, 0.7)])

# ---- salt domes and alpine salt bodies (deposits; NaCl)
def dome(id, name, lat, lon, top, base, diam, age, country, q='C', worked=None, mt=None, src=('lit',), est=('topM', 'baseM', 'lenKm', 'widKm')):
    vol = math.pi * (diam * 500) ** 2 * (base - top)  # m3 of a halite column of that diameter
    D(id, name, lat, lon, 'NaCl', [('halite', 1.0)], 'dome', mt or round(vol * 2.16 / 1e6), age, country, q=q, src=src, topM=top,
      baseM=base, lenKm=diam, widKm=diam, dipDeg=90, est=est, worked=worked)


import math  # noqa: E402
dome('dome:avery-island', 'Avery Island salt dome', 29.900, -91.905, 15, 3000, 2.5, 'Jurassic', US)
dome('dome:weeks-island', 'Weeks Island salt dome', 29.800, -91.795, 30, 3000, 2.0, 'Jurassic', US)
dome('dome:jefferson-island', 'Jefferson Island salt dome', 29.975, -91.965, 60, 3000, 1.5, 'Jurassic', US)
dome('dome:cote-blanche', 'Cote Blanche salt dome', 29.735, -91.715, 30, 3000, 2.0, 'Jurassic', US)
dome('dome:grand-saline', 'Grand Saline salt dome', 32.670, -95.710, 60, 5000, 1.5, 'Jurassic', US)
dome('dome:hockley', 'Hockley salt dome', 30.035, -95.820, 300, 5000, 2.5, 'Jurassic', US)
dome('dome:bryan-mound', 'Bryan Mound salt dome', 28.930, -95.380, 600, 5000, 1.5, 'Jurassic', US)
dome('dome:big-hill', 'Big Hill salt dome', 29.795, -94.205, 500, 5000, 1.5, 'Jurassic', US)
dome('dome:gorleben', 'Gorleben-Rambow salt dome', 53.020, 11.360, 250, 3500, 4.0, 'Permian', DE)
dome('dome:asse', 'Asse salt anticline', 52.130, 10.660, 100, 2000, 2.0, 'Permian', DE)
dome('dome:lueneburg', 'Lueneburg salt dome', 53.245, 10.400, 40, 3000, 2.0, 'Permian', DE,
     worked='Lueneburg saltworks from about 956; the Hansa\'s main salt in 1318')
dome('dome:bad-segeberg', 'Bad Segeberg salt dome (Kalkberg cap rock)', 53.935, 10.310, 100, 3000, 2.0, 'Permian', DE,
     worked='Segeberg Kalkberg gypsum cap quarried in the Middle Ages')
dome('dome:stassfurt', 'Stassfurt salt saddle', 51.855, 11.580, 150, 2000, 3.0, 'Permian', DE,
     worked='Stassfurt brine boiled in the Middle Ages')
dome('dome:bernburg', 'Bernburg salt dome', 51.800, 11.740, 300, 1500, 2.0, 'Permian', DE)
dome('dome:sperenberg', 'Sperenberg salt dome', 52.145, 13.360, 90, 3000, 2.0, 'Permian', DE)
dome('dome:mors', 'Mors salt dome', 56.800, 8.750, 600, 4000, 3.0, 'Permian', 'Denmark')
dome('dome:hengelo', 'Hengelo-Boekelo salt (Twente)', 52.200, 6.800, 400, 600, 5.0, 'Triassic', NL)
dome('dome:klodawa', 'Klodawa salt dome', 52.250, 18.920, 150, 5000, 2.0, 'Permian', 'Poland')
dome('dome:inowroclaw', 'Inowroclaw salt dome', 52.795, 18.260, 100, 5000, 1.5, 'Permian', 'Poland',
     worked='Inowroclaw brine salt in the 12th-14th centuries')
dome('dome:wapno', 'Wapno salt dome', 52.880, 17.470, 150, 4000, 1.0, 'Permian', 'Poland')
dome('dome:slanic', 'Slanic Prahova salt massif', 45.235, 25.940, 0, 500, 2.0, 'Neogene', 'Romania')
dome('dome:solotvyno', 'Solotvyno salt diapir', 47.955, 23.865, 30, 1500, 1.5, 'Neogene', UA,
     worked='Maramures rock salt mined in the Middle Ages')
dome('dome:mount-sodom', 'Mount Sodom salt diapir', 31.070, 35.390, 0, 5000, 2.0, 'Neogene', 'Israel')
dome('dome:hormuz-island', 'Hormuz Island salt diapir', 27.065, 56.460, 0, 5000, 5.0, 'Cambrian', 'Iran')
dome('dome:namakdan', 'Namakdan salt diapir (Qeshm)', 26.630, 55.520, 0, 5000, 5.0, 'Cambrian', 'Iran')
dome('dome:djelfa', 'Rocher de Sel (Djelfa)', 34.800, 3.070, 0, 1500, 1.5, 'Triassic', 'Algeria')
dome('dome:cardona', 'Cardona salt diapir (Muntanya de Sal)', 41.920, 1.680, 0, 1500, 1.5, 'Paleogene', 'Spain',
     worked='Cardona salt worked since prehistory')
dome('dome:remolinos', 'Remolinos rock salt (Ebro basin)', 41.840, -1.110, 0, 200, 2.0, 'Paleogene', 'Spain')
dome('dome:hallstatt', 'Hallstatt salt body (Haselgebirge)', 47.565, 13.630, 0, 600, 1.5, 'Permian', 'Austria',
     worked='Hallstatt salt mined since the Bronze Age; medieval mine from 1311')
dome('dome:altaussee', 'Altaussee salt body (Haselgebirge)', 47.645, 13.780, 0, 600, 1.5, 'Permian', 'Austria',
     worked='Altaussee salt worked from 1147')
dome('dome:bad-ischl', 'Bad Ischl salt body (Haselgebirge)', 47.715, 13.630, 0, 600, 1.0, 'Permian', 'Austria')
dome('dome:duerrnberg', 'Duerrnberg (Hallein) salt body', 47.665, 13.090, 0, 600, 1.5, 'Permian', 'Austria',
     worked='Duerrnberg salt mined by the Celts and again from the 12th century')
dome('dome:berchtesgaden', 'Berchtesgaden salt body', 47.635, 13.010, 0, 600, 1.5, 'Permian', DE,
     worked='Berchtesgaden salt mined from 1193')
dome('dome:bad-reichenhall', 'Bad Reichenhall brine (Haselgebirge)', 47.725, 12.880, 0, 500, 1.0, 'Permian', DE,
     worked='Reichenhall brine works, the chief salt of Bavaria in the Middle Ages')
dome('dome:hall-in-tirol', 'Hall in Tirol salt body (Absam)', 47.300, 11.470, 0, 600, 1.5, 'Permian', 'Austria',
     worked='Hall salt mine opened about 1272')
D('salt:wieliczka', 'Wieliczka salt deposit', 49.983, 20.055, 'NaCl', [('halite', 1.0)], 'lens', 4000, 'Neogene', 'Poland',
  topM=60, baseM=330, lenKm=10.0, widKm=1.0, strikeDeg=90, dipDeg=30, worked='Wieliczka rock salt mined from the 13th century (shaft about 1280)')
D('salt:bochnia', 'Bochnia salt deposit', 49.970, 20.430, 'NaCl', [('halite', 1.0)], 'lens', 1000, 'Neogene', 'Poland',
  topM=30, baseM=470, lenKm=4.0, widKm=0.2, strikeDeg=90, dipDeg=70, worked='Bochnia rock salt mined from 1248')
D('salt:turda', 'Turda salt deposit', 46.588, 23.787, 'NaCl', [('halite', 1.0)], 'stock', 2000, 'Neogene', 'Romania',
  topM=0, baseM=1200, lenKm=2.0, widKm=1.0, dipDeg=90, worked='Turda salt mined from at least 1075')
D('salt:praid', 'Praid salt diapir', 46.550, 25.130, 'NaCl', [('halite', 1.0)], 'stock', 2000, 'Neogene', 'Romania',
  topM=0, baseM=2700, lenKm=1.5, widKm=1.2, dipDeg=90, worked='Praid salt worked in the Middle Ages')
# Khewra is part of evap:salt-range (one body per real deposit, 8 Nov).

# ============================================================================================== GYPSUM (earth-gypsum)
G = 'earth-gypsum'
A('gyp:paris-basin', 'Paris basin gypsum (Montmartre, Cormeilles, Vaujours)', 'gypsum', FR, 'Paleogene',
  ring((2.20, 48.80), (2.70, 48.80), (2.82, 49.05), (2.45, 49.10), (2.15, 48.98)), [bed(G, 5, 15, 0.4)], q='B',
  worked='Paris plaster quarried at Montmartre since Roman times; plaster of Paris widely used by the 13th century')
A('gyp:suedharz', 'Southern Harz Zechstein gypsum belt (Nordhausen to Sangerhausen)', 'gypsum', DE, 'Permian',
  ring((10.55, 51.48), (11.35, 51.43), (11.42, 51.53), (10.62, 51.60)), [bed(G, 0, 30, 0.5)], q='B')
A('gyp:windsor', 'Windsor Group gypsum of Nova Scotia (Windsor, Milford, Little Narrows)', 'gypsum', CA, 'Carboniferous',
  union(ring((-64.40, 44.85), (-63.40, 44.95), (-63.00, 45.20), (-64.20, 45.15)), ring((-60.95, 45.95), (-60.75, 45.95), (-60.75, 46.05), (-60.95, 46.05))),
  [bed(G, 5, 30, 0.3)], q='B')
A('gyp:sorbas', 'Sorbas and Vera Messinian gypsum (Almeria)', 'gypsum', 'Spain', 'Neogene',
  ring((-2.25, 37.03), (-1.85, 37.03), (-1.85, 37.18), (-2.25, 37.18)), [bed(G, 0, 60, 0.6)], q='B')
A('gyp:sivas', 'Sivas basin gypsum', 'gypsum', 'Turkey', 'Paleogene',
  ring((36.6, 39.3), (37.8, 39.3), (37.8, 39.9), (36.6, 39.9)), [bed(G, 0, 100, 0.3)])
A('gyp:blaine', 'Blaine Formation gypsum (western Oklahoma)', 'gypsum', US, 'Permian',
  ring((-99.9, 35.0), (-98.6, 35.6), (-98.4, 36.6), (-99.0, 36.9), (-99.9, 36.0)), [bed(G, 0, 30, 0.3)])
A('gyp:fort-dodge', 'Fort Dodge gypsum (Iowa)', 'gypsum', US, 'Jurassic',
  ring((-94.25, 42.40), (-94.05, 42.40), (-94.05, 42.58), (-94.25, 42.58)), [bed(G, 5, 9, 0.6)], q='B')
A('gyp:michigan', 'Michigan Formation gypsum (Tawas, Grand Rapids)', 'gypsum', US, 'Carboniferous',
  union(ring((-83.75, 44.15), (-83.45, 44.15), (-83.45, 44.35), (-83.75, 44.35)), ring((-85.80, 42.90), (-85.60, 42.90), (-85.60, 43.00), (-85.80, 43.00))),
  [bed(G, 10, 8, 0.5)])
A('gyp:midlands-alabaster', 'Mercia Mudstone gypsum and alabaster (Chellaston, Tutbury, Fauld, East Leake)', 'gypsum', UK, 'Triassic',
  ring((-1.75, 52.80), (-1.05, 52.80), (-0.95, 52.95), (-1.70, 52.95)), [bed(G, 10, 5, 0.3)],
  worked='Chellaston and Tutbury alabaster carved in the 14th century')
A('gyp:volterra', 'Volterra alabaster', 'gypsum', 'Italy', 'Neogene',
  ring((10.75, 43.35), (10.95, 43.35), (10.95, 43.45), (10.75, 43.45)), [bed(G, 0, 20, 0.4)], worked='Volterra alabaster worked since Etruscan times')
A('gyp:vena-del-gesso', 'Vena del Gesso Romagnola', 'gypsum', 'Italy', 'Neogene',
  ring((11.55, 44.18), (11.85, 44.18), (11.85, 44.28), (11.55, 44.28)), [bed(G, 0, 100, 0.5)])
A('gyp:salaspils', 'Salaspils Formation gypsum near Riga (Saulkalne, Sauriesi)', 'gypsum', 'Latvia', 'Devonian',
  ring((24.30, 56.80), (24.55, 56.80), (24.55, 56.92), (24.30, 56.92)), [bed(G, 3, 5, 0.5)], q='B')
A('gyp:birzai-pasvalys', 'Birzai and Pasvalys gypsum karst', 'gypsum', 'Lithuania', 'Devonian',
  ring((24.35, 56.05), (24.95, 56.05), (24.95, 56.35), (24.35, 56.35)), [bed(G, 10, 10, 0.3)])
A('gyp:pinega', 'Pinega gypsum karst (Arkhangelsk)', 'gypsum', RU, 'Permian',
  ring((43.3, 64.5), (44.3, 64.5), (44.3, 64.9), (43.3, 64.9)), [bed(G, 0, 20, 0.4)])
D('gyp:san-marcos', 'San Marcos Island gypsum (Baja California Sur)', 27.215, -112.080, 'gypsum', [(G, 1.0)], 'lens', 500, 'Neogene', 'Mexico',
  topM=0, baseM=60, lenKm=4.0, widKm=2.0)
D('gyp:plaster-city', 'Fish Creek Mountains gypsum (Plaster City quarry)', 32.940, -116.120, 'gypsum', [(G, 1.0)], 'lens', 100, 'Neogene', US,
  topM=0, baseM=40, lenKm=2.0, widKm=1.0)
D('gyp:yingcheng', 'Yingcheng gypsum (Hubei)', 30.950, 113.550, 'gypsum', [(G, 1.0)], 'lens', 1000, 'Paleogene', CN,
  topM=100, baseM=500, lenKm=10.0, widKm=5.0)
D('gyp:lake-macdonnell', 'Lake MacDonnell gypsum', -32.030, 133.980, 'gypsum', [(G, 1.0)], 'blanket', 200, 'Quaternary', AU,
  topM=0, baseM=10, lenKm=8.0, widKm=3.0)

# ============================================================================================== BORATES
D('bor:boron', 'Boron (Kramer) borate deposit', 35.040, -117.680, 'B', [('borax', 0.7), ('kernite', 0.3, 'bottom')], 'lens', 130, 'Neogene', US,
  grade=25, unit='%', topM=45, baseM=330, lenKm=1.6, widKm=0.8, src=['lit', 'usgs-mcs-2025'])
D('bor:searles', 'Searles Lake borate and potash evaporites', 35.730, -117.330, 'B', [('borax', 1.0)], 'blanket', 1000, 'Quaternary', US,
  grade=1.5, unit='%', topM=0, baseM=100, lenKm=12.0, widKm=8.0, src=['lit', 'usgs-potash-2014'])
D('bor:furnace-creek', 'Furnace Creek colemanite (Death Valley)', 36.380, -116.700, 'B', [('borax', 1.0)], 'lens', 20, 'Neogene', US,
  grade=20, unit='%', topM=0, baseM=200, lenKm=3.0, widKm=0.5, strikeDeg=135, dipDeg=40)
D('bor:kirka', 'Kirka borax deposit', 39.300, 30.530, 'B', [('borax', 1.0)], 'lens', 600, 'Neogene', 'Turkey',
  grade=25, unit='%', topM=30, baseM=150, lenKm=2.0, widKm=1.5, src=['lit', 'usgs-mcs-2025'])
D('bor:bigadic', 'Bigadic colemanite and ulexite', 39.390, 28.130, 'B', [('borax', 1.0)], 'lens', 600, 'Neogene', 'Turkey',
  grade=29, unit='%', topM=20, baseM=400, lenKm=12.0, widKm=4.0, src=['lit', 'usgs-mcs-2025'])
D('bor:emet', 'Emet and Hisarcik colemanite', 39.340, 29.260, 'B', [('borax', 1.0)], 'lens', 800, 'Neogene', 'Turkey',
  grade=30, unit='%', topM=20, baseM=200, lenKm=8.0, widKm=3.0, src=['lit', 'usgs-mcs-2025'])
D('bor:kestelek', 'Kestelek colemanite', 39.980, 28.850, 'B', [('borax', 1.0)], 'lens', 10, 'Neogene', 'Turkey',
  grade=28, unit='%', topM=10, baseM=80, lenKm=1.0, widKm=0.5)
D('bor:tincalayu', 'Tincalayu borax (Salar del Hombre Muerto)', -25.270, -67.050, 'B', [('borax', 0.8), ('kernite', 0.2, 'bottom')], 'lens', 30, 'Neogene', 'Argentina',
  grade=20, unit='%', topM=0, baseM=60, lenKm=1.0, widKm=0.6)
D('bor:loma-blanca', 'Loma Blanca borax (Jujuy)', -22.950, -66.430, 'B', [('borax', 1.0)], 'lens', 20, 'Neogene', 'Argentina',
  grade=18, unit='%', topM=10, baseM=100, lenKm=1.5, widKm=0.5)
D('bor:surire', 'Salar de Surire ulexite', -18.850, -69.050, 'B', [('borax', 1.0)], 'blanket', 50, 'Quaternary', 'Chile',
  grade=15, unit='%', topM=0, baseM=2, lenKm=12.0, widKm=8.0)
D('bor:ascotan', 'Salar de Ascotan ulexite', -21.550, -68.270, 'B', [('borax', 1.0)], 'blanket', 30, 'Quaternary', 'Chile',
  grade=15, unit='%', topM=0, baseM=2, lenKm=15.0, widKm=5.0)
D('bor:zabuye', 'Zabuye salt lake borax (Tibet)', 31.400, 84.070, 'B', [('borax', 1.0)], 'blanket', 20, 'Quaternary', CN,
  grade=5, unit='%', topM=0, baseM=5, lenKm=10.0, widKm=5.0)

# ============================================================================================== NATIVE SULFUR
S_ = 'sulfur'
def caprock(id, name, lat, lon, mt, top, base, diam, country, worked=None, age='Jurassic', saltBase=5000):
    """A Frasch salt dome: one body (coordinator's deduplication rule, 8 Nov), the sulfur-bearing cap rock (`mt` of ore
    at about 25 % S, from `top` to `base`) as the top zone of the dome's halite column (to `saltBase`). Owned by S: the
    sulfur was what these domes were mined for."""
    salt = round(math.pi * (diam * 500) ** 2 * (saltBase - base) * 2.16 / 1e6)
    share = round(mt / (mt + salt), 6)
    D(id, name, lat, lon, 'S', [(S_, share, 'top'), ('halite', round(1 - share, 6))], 'dome', mt + salt, age, country,
      grade=25, unit='%', topM=top, baseM=saltBase, lenKm=diam, widKm=diam, dipDeg=90, also=['NaCl'], mainMt=mt, worked=worked)
caprock('sul:boling', 'Boling salt dome (sulfur cap rock)', 29.270, -95.950, 300, 120, 300, 4.0, US)
caprock('sul:sulphur-mines', 'Sulphur Mines salt dome (Calcasieu; first Frasch mine)', 30.250, -93.420, 40, 130, 250, 1.0, US)
caprock('sul:grand-ecaille', 'Grand Ecaille salt dome (sulfur cap rock)', 29.350, -89.880, 150, 400, 600, 2.0, US)
caprock('sul:spindletop', 'Spindletop salt dome (sulfur cap rock)', 30.020, -94.080, 30, 300, 500, 1.5, US)
caprock('sul:jaltipan', 'Jaltipan salt dome (sulfur cap rock, Isthmus of Tehuantepec)', 17.970, -94.730, 100, 150, 400, 2.0, 'Mexico')
D('sul:culberson', 'Culberson sulfur (Castile Formation)', 31.900, -104.050, 'S', [(S_, 1.0)], 'lens', 300, 'Permian', US,
  grade=20, unit='%', topM=150, baseM=400, lenKm=4.0, widKm=2.0)
D('sul:mishraq', 'Mishraq sulfur (Fatha Formation)', 36.000, 43.300, 'S', [(S_, 1.0)], 'lens', 800, 'Neogene', 'Iraq',
  grade=25, unit='%', topM=50, baseM=200, lenKm=6.0, widKm=3.0)
D('sul:gaurdak', 'Gaurdak sulfur', 37.800, 66.050, 'S', [(S_, 1.0)], 'lens', 200, 'Jurassic', 'Turkmenistan',
  grade=20, unit='%', topM=50, baseM=300, lenKm=5.0, widKm=2.0)
A('sul:tarnobrzeg', 'Tarnobrzeg sulfur district (Machow, Jeziorko, Osiek, Grzybow)', 'S', 'Poland', 'Neogene',
  ring((21.00, 50.42), (21.95, 50.45), (21.95, 50.62), (21.00, 50.58)), [bed(S_, 25, 20)], mt=2000)
A('sul:yavoriv-rozdil', 'Yavoriv and Rozdil sulfur (Carpathian foredeep)', 'S', UA, 'Neogene',
  union(ring((23.30, 49.90), (23.55, 49.90), (23.55, 50.05), (23.30, 50.05)), ring((23.95, 49.40), (24.15, 49.40), (24.15, 49.55), (23.95, 49.55))),
  [bed(S_, 20, 20)], mt=600)
# One body for the Caltanissetta basin (deduplication rule, 8 Nov): its Messinian gypsum, sulfur limestone and salt
# (Realmonte, Petralia) were three areas over the same ground. Owned by S, the basin's main commodity by value.
A('sul:sicily', 'Sicilian Messinian evaporite basin: sulfur, gypsum and salt (Caltanissetta, Agrigento, Enna, Realmonte)', 'S', 'Italy', 'Neogene',
  union(potash('150haK0013'), ring((13.4, 37.2), (14.4, 37.2), (14.4, 37.65), (13.4, 37.65))),
  [bed(G, 0, 50, 0.3), bed(S_, 20, 5), bed('halite', 50, 300, 0.25)], mt=300, src=['lit', 'usgs-potash-2014'],
  also=['gypsum', 'NaCl'], worked='Sicilian sulfur used since antiquity; Messinian gypsum quarried')

# ============================================================================================== PHOSPHORITE
P_ = 'phosphorite'
A('phos:ouled-abdoun', 'Ouled Abdoun phosphate basin (Khouribga)', 'P', 'Morocco', 'Paleogene',
  ring((-7.40, 32.70), (-6.30, 32.60), (-5.90, 32.90), (-6.40, 33.10), (-7.20, 33.05)), [bed(P_, 10, 15)], mt=26000,
  grade=31, unit='%', src=['lit', 'usgs-phosphate-2002', 'usgs-mcs-2025'])
A('phos:gantour', 'Gantour phosphate basin (Youssoufia, Benguerir)', 'P', 'Morocco', 'Paleogene',
  ring((-8.90, 32.00), (-7.40, 31.95), (-7.30, 32.35), (-8.80, 32.40)), [bed(P_, 20, 10)], mt=20000,
  grade=30, unit='%', src=['lit', 'usgs-phosphate-2002', 'usgs-mcs-2025'])
A('phos:meskala', 'Meskala phosphate basin', 'P', 'Morocco', 'Paleogene',
  ring((-9.20, 31.20), (-8.60, 31.20), (-8.60, 31.50), (-9.20, 31.50)), [bed(P_, 50, 8)], mt=4000, grade=28, unit='%')
A('phos:bou-craa', 'Bou Craa phosphate', 'P', 'Western Sahara', 'Paleogene',
  ring((-12.95, 26.25), (-12.60, 26.25), (-12.60, 26.45), (-12.95, 26.45)), [bed(P_, 10, 6)], mt=1000, grade=34, unit='%')
A('phos:phosphoria', 'Phosphoria Formation (Idaho, Utah, Wyoming, Montana)', 'P', US, 'Permian',
  ring((-112.0, 42.0), (-110.6, 41.8), (-110.2, 43.2), (-111.0, 44.6), (-112.5, 45.0), (-113.0, 44.0), (-112.3, 42.8)),
  [bed(P_, 0, 3)], mt=10000, grade=24, unit='%', src=['lit', 'usgs-phosphate-2002'])
A('phos:central-florida', 'Central Florida phosphate district (Bone Valley, Peace River)', 'P', US, 'Neogene',
  ring((-82.30, 27.40), (-81.60, 27.40), (-81.60, 28.20), (-82.30, 28.20)), [bed(P_, 5, 5)], mt=8000, grade=10, unit='%',
  src=['lit', 'usgs-phosphate-2002'])
A('phos:aurora', 'Aurora phosphate (Pungo River Formation, North Carolina)', 'P', US, 'Neogene',
  ring((-77.00, 35.25), (-76.60, 35.25), (-76.60, 35.50), (-77.00, 35.50)), [bed(P_, 30, 12)], mt=3000, grade=13, unit='%')
A('phos:karatau', 'Karatau phosphorite basin (Zhanatas, Koksu)', 'P', KZ, 'Cambrian',
  ring((68.6, 43.2), (70.3, 42.4), (70.6, 42.7), (69.0, 43.6)), [bed(P_, 0, 15)], mt=4000, grade=25, unit='%')
A('phos:georgina', 'Georgina basin phosphorite (Phosphate Hill, Duchess, Lady Annie)', 'P', AU, 'Cambrian',
  ring((139.5, -22.0), (140.2, -22.0), (140.2, -20.6), (139.5, -20.6)), [bed(P_, 0, 15)], mt=4000, grade=17, unit='%')
A('phos:baltic-obolus', 'Baltic Obolus phosphorite (Maardu, Toolse, Rakvere, Kingisepp)', 'P', 'Estonia', 'Ordovician',
  ring((24.9, 59.30), (27.3, 59.20), (28.7, 59.35), (28.7, 59.55), (26.5, 59.62), (24.9, 59.50)), [bed(P_, 10, 3)], mt=3700,
  grade=10, unit='%', q='B')
A('phos:jordan', 'Jordan phosphate (Eshidiya, Al-Hasa)', 'P', 'Jordan', 'Cretaceous',
  union(ring((36.00, 29.90), (36.40, 29.90), (36.40, 30.20), (36.00, 30.20)), ring((35.95, 30.75), (36.30, 30.75), (36.30, 30.95), (35.95, 30.95))),
  [bed(P_, 20, 6)], mt=2000, grade=28, unit='%')
A('phos:al-jalamid', 'Al Jalamid phosphate (northern Saudi Arabia)', 'P', 'Saudi Arabia', 'Paleogene',
  ring((39.00, 31.20), (39.70, 31.20), (39.70, 31.50), (39.00, 31.50)), [bed(P_, 20, 5)], mt=1000, grade=20, unit='%')
A('phos:gafsa', 'Gafsa-Metlaoui phosphate basin', 'P', 'Tunisia', 'Paleogene',
  ring((8.15, 34.15), (8.80, 34.15), (8.80, 34.55), (8.15, 34.55)), [bed(P_, 10, 8)], mt=2500, grade=28, unit='%')
A('phos:djebel-onk', 'Djebel Onk phosphate', 'P', 'Algeria', 'Paleogene',
  ring((7.90, 34.60), (8.20, 34.60), (8.20, 34.80), (7.90, 34.80)), [bed(P_, 10, 20)], mt=2000, grade=26, unit='%')
A('phos:abu-tartur', 'Abu Tartur phosphate (Western Desert)', 'P', 'Egypt', 'Cretaceous',
  ring((30.00, 25.30), (30.40, 25.30), (30.40, 25.50), (30.00, 25.50)), [bed(P_, 50, 4)], mt=1000, grade=26, unit='%')
A('phos:khneifis', 'Palmyrides phosphate (Khneifis, Sharqiyah)', 'P', 'Syria', 'Cretaceous',
  ring((37.60, 34.30), (38.40, 34.30), (38.40, 34.60), (37.60, 34.60)), [bed(P_, 10, 6)], mt=1700, grade=25, unit='%')
A('phos:akashat', 'Akashat phosphate (western Iraq)', 'P', 'Iraq', 'Paleogene',
  ring((40.90, 33.60), (41.40, 33.60), (41.40, 33.95), (40.90, 33.95)), [bed(P_, 10, 10)], mt=1000, grade=21, unit='%')
A('phos:negev', 'Negev phosphate (Oron, Zin, Arad)', 'P', 'Israel', 'Cretaceous',
  ring((35.00, 30.85), (35.30, 30.85), (35.30, 31.05), (35.00, 31.05)), [bed(P_, 10, 3)], mt=1000, grade=25, unit='%')
A('phos:sechura', 'Sechura phosphate (Bayovar)', 'P', 'Peru', 'Neogene',
  ring((-80.90, -6.05), (-80.40, -6.05), (-80.40, -5.65), (-80.90, -5.65)), [bed(P_, 20, 10)], mt=5000, grade=17, unit='%')
D('phos:san-juan-de-la-costa', 'San Juan de la Costa phosphorite', 24.390, -110.690, 'P', [(P_, 1.0)], 'lens', 500, 'Neogene', 'Mexico',
  grade=18, unit='%', topM=0, baseM=60, lenKm=10.0, widKm=3.0, strikeDeg=0, dipDeg=10)
A('phos:taiba', 'Taiba phosphate (Senegal)', 'P', 'Senegal', 'Paleogene',
  ring((-16.95, 14.85), (-16.75, 14.85), (-16.75, 15.05), (-16.95, 15.05)), [bed(P_, 20, 6)], mt=300, grade=36, unit='%')
A('phos:hahotoe', 'Hahotoe-Kpogame phosphate (Togo)', 'P', 'Togo', 'Paleogene',
  ring((1.40, 6.25), (1.65, 6.25), (1.65, 6.45), (1.40, 6.45)), [bed(P_, 10, 5)], mt=300, grade=36, unit='%')
A('phos:kunyang', 'Kunyang phosphorite (Yunnan)', 'P', CN, 'Cambrian',
  ring((102.40, 24.60), (102.75, 24.60), (102.75, 24.90), (102.40, 24.90)), [bed(P_, 0, 10)], mt=3000, grade=25, unit='%')
A('phos:kaiyang', 'Kaiyang phosphorite (Guizhou)', 'P', CN, 'Precambrian',
  ring((106.85, 27.00), (107.05, 27.00), (107.05, 27.20), (106.85, 27.20)), [bed(P_, 0, 6)], mt=2000, grade=34, unit='%')
A('phos:yichang', 'Yichang phosphorite (Hubei)', 'P', CN, 'Precambrian',
  ring((110.90, 31.00), (111.30, 31.00), (111.30, 31.35), (110.90, 31.35)), [bed(P_, 50, 5)], mt=2000, grade=22, unit='%')
A('phos:lao-cai', 'Lao Cai apatite-phosphorite belt', 'P', 'Vietnam', 'Cambrian',
  ring((103.90, 22.25), (104.35, 22.25), (104.35, 22.55), (103.90, 22.55)), [bed(P_, 0, 10)], mt=2500, grade=20, unit='%')
A('phos:patos-de-minas', 'Patos de Minas phosphorite', 'P', 'Brazil', 'Precambrian',
  ring((-46.60, -18.70), (-46.30, -18.70), (-46.30, -18.40), (-46.60, -18.40)), [bed(P_, 0, 10)], mt=400, grade=13, unit='%')

# ============================================================================================== ALUM AND ALUNITE
AL = 'alum'
def alum(id, name, lat, lon, mt, age, country, worked=None, shape='stock', top=0, base=150, l=1.5, w=1.0, q='C'):
    D(id, name, lat, lon, 'alum', [(AL, 1.0)], shape, mt, age, country, q=q, topM=top, baseM=base, lenKm=l, widKm=w,
      dipDeg=90 if shape == 'stock' else 5, worked=worked)
alum('alum:phocaea', 'Phocaea alum (Foca)', 38.700, 26.830, 20, 'Neogene', 'Turkey',
     worked='Phocaean alum worked by the Genoese Zaccaria from 1275')
alum('alum:karahisar', 'Sebinkarahisar alum (Koloneia)', 40.290, 38.420, 20, 'Cretaceous', 'Turkey',
     worked='Koloneia alum shipped through Genoese Black Sea trade in the 13th century')
alum('alum:tolfa', 'Tolfa alunite', 42.150, 11.930, 50, 'Neogene', 'Italy')
alum('alum:vulcano', 'Vulcano and Lipari alum', 38.400, 14.960, 2, 'Quaternary', 'Italy', worked='Lipari alum traded in the Middle Ages', base=50, l=1.0, w=0.5)
alum('alum:milos', 'Milos alum', 36.700, 24.470, 5, 'Neogene', 'Greece', worked='Melian alum known since antiquity', base=80)
alum('alum:zaglik', 'Zaglik alunite', 40.480, 46.050, 160, 'Jurassic', 'Azerbaijan', base=300, l=3.0, w=1.0)
alum('alum:fanshan', 'Fanshan alunite (Cangnan)', 27.430, 120.420, 200, 'Cretaceous', CN, base=300, l=4.0, w=1.5)
alum('alum:lujiang', 'Lujiang alunite (Anhui)', 31.250, 117.250, 100, 'Cretaceous', CN, base=200, l=3.0, w=1.0)
alum('alum:bulahdelah', 'Bulahdelah alunite', -32.400, 152.200, 5, 'Carboniferous', AU, base=100, l=1.0, w=0.5)
alum('alum:marysvale', 'Marysvale alunite', 38.450, -112.220, 5, 'Neogene', US, base=100, l=1.0, w=0.5)
alum('alum:andrarum', 'Andrarum alum shale', 55.730, 13.980, 50, 'Cambrian', 'Sweden', shape='blanket', top=0, base=20, l=3.0, w=2.0)
alum('alum:whitby', 'Whitby alum shale (Jurassic Lias)', 54.470, -0.700, 50, 'Jurassic', UK, shape='blanket', top=0, base=30, l=15.0, w=2.0)

# ============================================================================================== EVIDENCE ONLY (heat maps, no catalogue body)
def ev(code, id, name, lat=None, lon=None, ring_=None, tier=None, peak=None, radius=None, weight=None, country=None, plane='obs', why=''):
    e = {'code': code, 'id': id, 'name': name, 'plane': plane, 'source': 'coal-evaporites evidence', 'why': why}
    if ring_: e['ring'] = ring_; e['weight'] = weight or 0.6
    else:
        e['lon'], e['lat'] = lon, lat
        if tier: e['tier'] = tier
        else: e['peak'], e['radiusKm'] = peak, radius
    if country: e['country'] = country
    EVIDENCE_ONLY.append(e)

ev('N', 'nitrate:atacama', 'Atacama nitrate belt (Tarapaca to Taltal)', ring_=[(-70.2, -19.3), (-69.4, -19.3), (-69.2, -21.5), (-69.4, -23.5),
   (-69.8, -25.8), (-70.3, -25.8), (-70.0, -23.5), (-69.9, -21.5), (-70.1, -20.0)], weight=0.85,
   why='caliche nitrate fields; saltpeter in VS forms in cave air, so nitrates stay evidence only')
ev('S', 'volcanic:aucanquilcha', 'Aucanquilcha volcanic sulfur', -21.22, -68.47, tier='deposit', country='Chile', why='volcanic sulfur, not a sedimentary bed')
ev('S', 'volcanic:namafjall', 'Namafjall sulfur (Iceland)', 65.64, -16.82, tier='deposit', country='Iceland', why='medieval sulfur export from Iceland')
ev('S', 'volcanic:vulcano', 'Vulcano sulfur', 38.40, 14.96, tier='prospect', country='Italy', why='volcanic sulfur worked since antiquity')
ev('S', 'volcanic:ijen', 'Kawah Ijen sulfur', -8.06, 114.24, tier='prospect', country='Indonesia', why='volcanic sulfur')
ev('B', 'borate:inder', 'Inder borates (Permian salt dome cap)', 48.55, 51.75, tier='deposit', country='Kazakhstan',
   why='real borate deposit, but Permian: outside 0060 borate rule (Cenozoic evaporites), so evidence only')


# ---- 0060 layer classes for blankets (0065 contract addition, coordinator commit 50420af). The blanket default
# (alluvial, outwash, laterite, saprolite) fits none of this group's blankets: salt-lake and playa bodies take the
# playa layer, the alum shales are sedimentary beds.
for _d in DEPOSITS:
    if _d['shape'] == 'blanket':
        _d['layers'] = ['bed'] if _d['layer'] == 'alum' else ['playa']

# ---- Overlapping outlines of the same bed (open polygons clipped by box catch shared fields): the second loses the
# ground of the first, so coal is not counted twice. Stacked basins of different age (Surat over Bowen, Rhenish
# lignite over Limburg-Aachen) are real and stay.
OVERLAP_CUTS = [
    ('coal:great-northern-exposed', 'coal:great-northern-concealed'),
    ('coal:east-pennine-exposed', 'coal:east-pennine-concealed'),
    ('coal:east-pennine-exposed', 'coal:english-midlands'),
    ('coal:east-pennine-concealed', 'coal:english-midlands'),
    ('coal:lancashire', 'coal:english-midlands'),
    ('coal:haine-sambre', 'coal:nord-pas-de-calais'),
    ('coal:saar', 'coal:lorraine'),
    ('coal:upper-silesia-pl', 'coal:ostrava-karvina'),
    ('coal:shanxi', 'coal:hebei-kaiping'),
    ('coal:henan', 'coal:hebei-kaiping'),
    ('coal:henan', 'coal:huainan-huaibei'),
    ('coal:shandong', 'coal:huainan-huaibei'),
    ('coal:mahanadi', 'coal:son-hasdeo'),  # the Ib River field (Odisha) stays with Mahanadi
    ('coal:waterberg', 'coal:botswana-kalahari'),
    ('evap:hormuz', 'evap:zagros'),
]
