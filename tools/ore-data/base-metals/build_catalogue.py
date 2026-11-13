"""Build the base-metals deposit catalogue of decision 0065 (layer codes Cu, Sn, Pb, Zn, Ag, Bi, Hg, W, Mo, Sb).

Inputs (no network; run fetch_base_metals.py first for the downloaded ones):
  * USGS global copper assessment spatial database (Dicken and others 2016, SIR 2010-5090-Z): porphyry copper
    (pCu_deps_pros, pCu_Precambrian_deps_pros) and sediment-hosted copper (sedCu_deps_pros), already normalised on disk
    in docs/research/copper-tin-global-2026-09-29/normalized.
  * USGS porphyry copper grade-tonnage database (Singer, Berger and Moring 2008, OFR 2008-1155): ore-zone areas and axes
    (rawdata/ore/base-metals/usgs-porcu-2008/PorCuTX2008.txt).
  * USGS VMS database (Mosier, Berger and Singer 2009, OFR 2009-1034) and the MVT / clastic-dominated Pb-Zn compilation
    (Taylor and others 2009, OFR 2009-1297): raw zips on disk in docs/research/remaining-ores-2026-09-29/raw.
  * Named deposits with published figures (named-*.csv in this folder; per-row citation, confidence B or C).
  * Kupferschiefer and Central African Copperbelt districts become `areas` (stratiform beds); other deposits are bodies.

Outputs: assets/earth/config/ore-deposits-v1/base-metals.json (minified) and build-report.json (validation numbers).
Run: C:\\VS mods\\Earth\\.venv\\Scripts\\python.exe -I tools/ore-data/base-metals/build_catalogue.py
"""
import csv, io, json, math, re, sys
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *  # noqa: F401,F403

import shapely
from shapely.geometry import Point, Polygon, mapping, shape

TODAY = '2026-11-08'
SOURCES = [
    {'id': 'usgs-gcra-pcu', 'title': 'Spatial database for a global assessment of undiscovered copper resources: porphyry copper deposits and prospects (Dicken, Dunlap, Parks, Hammarstrom and Zientek 2016, USGS SIR 2010-5090-Z)',
     'url': 'https://pubs.usgs.gov/sir/2010/5090/z/', 'licence': 'public domain (USGS)', 'retrieved': '2026-09-29'},
    {'id': 'usgs-gcra-sedcu', 'title': 'Spatial database for a global assessment of undiscovered copper resources: sediment-hosted stratabound copper deposits and prospects (Dicken and others 2016, USGS SIR 2010-5090-Z)',
     'url': 'https://pubs.usgs.gov/sir/2010/5090/z/', 'licence': 'public domain (USGS)', 'retrieved': '2026-09-29'},
    {'id': 'usgs-porcu-2008', 'title': 'Porphyry copper deposits of the world: database and grade and tonnage models, 2008 (Singer, Berger and Moring, USGS OFR 2008-1155)',
     'url': 'https://pubs.usgs.gov/of/2008/1155/', 'licence': 'public domain (USGS)', 'retrieved': TODAY},
    {'id': 'usgs-vms-2009', 'title': 'Volcanogenic massive sulfide deposits of the world: database and grade and tonnage models (Mosier, Berger and Singer 2009, USGS OFR 2009-1034)',
     'url': 'https://pubs.usgs.gov/of/2009/1034/', 'licence': 'public domain (USGS)', 'retrieved': '2026-09-29'},
    {'id': 'usgs-sedexmvt-2009', 'title': 'Compilation of mineral resource data for Mississippi Valley-type and clastic-dominated sediment-hosted lead-zinc deposits (Taylor, Leach, Bradley and Pisarevsky 2009, USGS OFR 2009-1297)',
     'url': 'https://pubs.usgs.gov/of/2009/1297/', 'licence': 'public domain (USGS)', 'retrieved': '2026-09-29'},
    {'id': 'earth-named-2026', 'title': 'Named deposits compiled for Earth from published tonnage and grade figures (citation per entry in its ref field)',
     'url': 'https://github.com/Temporal-Poets-Society', 'licence': 'published facts (tonnage, grade, place, date) with a citation per entry; no tables or text copied', 'retrieved': TODAY},
]

GCRA_PCU = CUTIN / 'pCu_deps_pros-source.csv'
GCRA_PCU_PC = CUTIN / 'pCu_Precambrian_deps_pros-source.csv'
GCRA_SEDCU = CUTIN / 'sedCu_deps_pros-source.csv'
SINGER = RAW / 'usgs-porcu-2008' / 'PorCuTX2008.txt'
VMS_ZIP = ATLAS / 'raw/vms-vms-csv.zip'
SEDEXMVT_ZIP = ATLAS / 'raw/sedexmvt-sedexmvt-csv.zip'
NAMED = sorted(HERE.glob('named-*.csv'))

log = Counter()
dropped = []


# ------------------------------------------------------------------------------------------------ body geometry
def clamp(x, lo, hi): return max(lo, min(hi, x))


def stock_dims(mt, rho, area_km2=None, a_km=None, b_km=None):
    """Upright body. Vertical extent from the ore-zone area when the source gives one, else from a size rule."""
    V = mt * 1e6 / rho
    if area_km2 and area_km2 > 0:
        H = clamp(V / (area_km2 * 1e6), 150, 1800); est = []
    else:
        H = clamp(200 * (mt / 100) ** .3, 150, 1500); est = ['lenKm', 'widKm', 'baseM']
    area = V / H
    ratio = clamp(a_km / b_km, 1, 6) if a_km and b_km and a_km > 0 and b_km > 0 else 1.3
    wid = math.sqrt(area * 4 / math.pi / ratio)
    return round(ratio * wid / 1000, 3), round(wid / 1000, 3), H, est


LENS = {  # length : down-dip width : thickness, maximum thickness (m), default dip
    'vms': (1, .5, .05, 80, 60), 'sedex': (1, .6, .02, 60, 45), 'mvt': (1, .4, .01, 40, 5), 'sedcu': (1, .5, .004, 25, 15),
    'lens': (1, .5, .03, 60, 45),
}


def lens_dims(mt, rho, kind, a_km=None, b_km=None):
    lr, wr, tr, tmax, _ = LENS[kind]
    V = mt * 1e6 / rho
    if a_km and b_km and a_km > 0 and b_km > 0:
        L, Wd = a_km * 1000, b_km * 1000
        t = V / (math.pi / 4 * L * Wd)
        est = []
        if t > tmax or t < 1:
            t = clamp(t, 1, tmax); k = math.sqrt(V / (math.pi / 4 * L * Wd * t)); L *= k; Wd *= k; est = ['lenKm', 'widKm']
    else:
        L = (V / (math.pi / 4 * wr * tr)) ** (1 / 3); Wd = wr * L; t = tr * L
        if t > tmax:
            t = tmax; L = math.sqrt(V / (math.pi / 4 * wr * t)); Wd = wr * L
        est = ['lenKm', 'widKm']
    return round(L / 1000, 3), round(Wd / 1000, 3), round(t, 1), est


def base_of(top, wid_km, thick, dip):
    return round(top + wid_km * 1000 * math.sin(math.radians(dip)) + thick * math.cos(math.radians(dip)))


def finish(d, kind=None):
    """Fill the footprint fields of a body from its shape (keeps given values)."""
    est = list(d.get('est') or [])
    rho = d['density']
    if d['shape'] == 'stock':
        if not d.get('lenKm') or not d.get('widKm'):
            L, Wd, H, e = stock_dims(d['mt'], rho)
            d['lenKm'], d['widKm'] = L, Wd; est += [x for x in ('lenKm', 'widKm') if x not in est]
            if d.get('baseM') is None: d['baseM'] = round(d['topM'] + H); est.append('baseM')
        elif d.get('baseM') is None:
            area = math.pi / 4 * d['lenKm'] * d['widKm'] * 1e6
            d['baseM'] = round(d['topM'] + clamp(d['mt'] * 1e6 / rho / area, 30, 2500)); est.append('baseM')
        for k, v in (('dipDeg', 90), ('strikeDeg', 0)):  # named rows carry an explicit None
            if d.get(k) is None: d[k] = v; est.append(k)
    elif d['shape'] in ('lens', 'veins'):
        dip = d.get('dipDeg')
        if dip is None: dip = LENS[kind or 'lens'][4] if d['shape'] == 'lens' else 75; d['dipDeg'] = dip; est.append('dipDeg')
        if d.get('strikeDeg') is None: d['strikeDeg'] = 0; est.append('strikeDeg')
        if not d.get('lenKm') or not d.get('widKm'):
            if d['shape'] == 'veins':
                # vein swarm: footprint holds the veins; about 3 % of the swarm's volume is ore (typical lode districts)
                V = d['mt'] * 1e6 / rho / .03
                depth = 600 if d.get('baseM') is None else max(100, d['baseM'] - d['topM'])
                area = V / depth
                Wd = math.sqrt(area / 3); L = 3 * Wd
                d['lenKm'], d['widKm'] = round(L / 1000, 3), round(Wd / 1000, 3)
                if d.get('baseM') is None: d['baseM'] = round(d['topM'] + depth); est.append('baseM')
            else:
                L, Wd, t, e = lens_dims(d['mt'], rho, kind or 'lens')
                d['lenKm'], d['widKm'] = L, Wd; d['thickM'] = t
            est += [x for x in ('lenKm', 'widKm') if x not in est]
        if d['shape'] == 'lens' and 'thickM' not in d:
            d['thickM'] = round(clamp(d['mt'] * 1e6 / rho / (math.pi / 4 * d['lenKm'] * d['widKm'] * 1e6), .5, 150), 1)
        if d.get('baseM') is None:
            if d['shape'] == 'lens': d['baseM'] = base_of(d['topM'], d['widKm'], d['thickM'], dip)
            else: d['baseM'] = d['topM'] + 600
            est.append('baseM')
    elif d['shape'] == 'blanket':
        if not d.get('lenKm') or not d.get('widKm'):
            thick = max(2, (d.get('baseM') or (d['topM'] + 8)) - d['topM'])
            area = d['mt'] * 1e6 / rho / thick
            Wd = math.sqrt(area / 4); L = 4 * Wd
            d['lenKm'], d['widKm'] = round(L / 1000, 3), round(Wd / 1000, 3); est += ['lenKm', 'widKm']
        if d.get('baseM') is None:
            area = math.pi / 4 * d['lenKm'] * d['widKm'] * 1e6
            d['baseM'] = round(d['topM'] + clamp(d['mt'] * 1e6 / rho / area, 1, 60), 1); est.append('baseM')
        for k in ('dipDeg', 'strikeDeg'):
            if d.get(k) is None: d[k] = 0; est.append(k)
    if est: d['est'] = sorted(set(est))
    elif 'est' in d: del d['est']
    return d


# ------------------------------------------------------------------------------------------------ readers
def porphyry_singer():
    rows = []
    if not SINGER.exists():
        log['singer_2008_missing (run fetch_base_metals.py)'] += 1; return rows
    t = SINGER.read_bytes().decode('mac_roman').replace('\r\n', '\n').replace('\r', '\n')
    for r in csv.DictReader(io.StringIO(t), delimiter='\t'):
        la, lo = num(r['LatitudeDecimal']), num(r['LongitudeDecimal'])
        if la is None or lo is None: continue
        rows.append({'name': (r['NameDeposit'] or '').strip(), 'lat': la, 'lon': lo, 'area': num(r['OreArea']) or None,
                     'a': num(r['OreAAxis']) or None, 'b': num(r['OreBAxis']) or None, 'mt': num(r['Tonnage'])})
    return rows


def norm(s): return re.sub(r'[^a-z]', '', (s or '').lower())


def match_singer(singer, name, lat, lon):
    best = None
    for s in singer:
        if abs(s['lat'] - lat) > .3: continue
        d = haversine_km(lat, lon, s['lat'], s['lon'])
        same = norm(s['name'])[:5] and norm(s['name'])[:5] == norm(name)[:5]
        if d < 3 or (same and d < 25):
            if best is None or d < best[0]: best = (d, s)
    return best[1] if best else None


def comment_top(comment):
    c = comment or ''
    m = re.search(r'(?:cover(?:ed)?|beneath|under(?:neath)?|concealed|buried|depth of)[^.;]{0,50}?(\d{2,4})\s*m\b', c, re.I)
    if m: return float(m.group(1)), False
    if re.search(r'\bexposed\b', c, re.I) and not re.search(r'not exposed|partly exposed|partially exposed', c, re.I): return 0.0, False
    if re.search(r'concealed|covered|buried|blind', c, re.I): return 100.0, True
    return 0.0, True


def read_porphyry(singer):
    out = []
    for path, pc in ((GCRA_PCU, False), (GCRA_PCU_PC, True)):
        with open(path, encoding='utf-8-sig') as f:
            for r in csv.DictReader(f):
                mt = num(r.get('Tonnage_Mt') if not pc else r.get('Tonnage'))
                cu = num(r.get('Cu_pct'))
                if not mt or mt <= 0:
                    log['porphyry_no_tonnage (heat-map background only)'] += 1; continue
                la, lo = num(r['Latitude']), num(r['Longitude'])
                grades = {'Cu': cu, 'Mo': num(r.get('Mo_pct')), 'Ag': num(r.get('Ag_g_t')), 'Au': num(r.get('Au_g_t'))}
                grades = {k: round(v, 4) for k, v in grades.items() if v and v > 0}
                if not grades.get('Cu') and not grades.get('Mo'):
                    log['porphyry_no_grade'] += 1; continue
                layer = 'Mo' if value('Mo', grades.get('Mo')) > value('Cu', grades.get('Cu')) else 'Cu'
                rid = r['GMRAP_ID'] if not pc else r['source_objectid']
                comment = r.get('Comments') or ''
                top, top_est = comment_top(comment)
                ma = num(r.get('Age_Ma'))
                age = age_from_ma(ma) or age_from_text(r.get('Strat_age')) or age_from_text(r.get('Tract_name')) or age_from_text(r.get('Age_range'))
                s = match_singer(singer, r['Name'], la, lo)
                rho = 2.65
                if s and s['area']:
                    L, Wd, H, est = stock_dims(mt, rho, s['area'], s['a'], s['b'])
                else:
                    L, Wd, H, est = stock_dims(mt, rho)
                if top_est: est.append('topM')
                ores = make_ores(grades, oxide=.1 if top < 50 else 0)
                d = {'id': ('usgs-gcra-pcu:' if not pc else 'usgs-gcra-pcu-pc:') + str(rid).split('.')[0], 'name': r['Name'].strip(),
                     'country': r['Country'], 'lat': round(la, 4), 'lon': round(lo, 4), 'layer': layer,
                     'type': 'porphyry ' + ('Cu' if layer == 'Cu' else 'Mo-Cu') + ('' if r.get('Dep_subtype') in (None, '', 'NA') else ' (' + r['Dep_subtype'] + ')'),
                     'ores': ores, 'shape': 'stock', 'mt': round(mt, 3), 'grade': grades.get(layer), 'unit': '%',
                     'grades': grades, 'density': rho, 'topM': top, 'baseM': round(top + H), 'lenKm': L, 'widKm': Wd,
                     'strikeDeg': 0, 'dipDeg': 90, 'age': age, 'ageMa': ma, 'src': 'usgs-gcra-pcu',
                     'q': 'A' if r.get('SiteStatus', 'deposit') == 'deposit' else 'B', 'est': sorted(set(est + ['strikeDeg']))}
                if s and s['area']: d['srcFootprint'] = 'usgs-porcu-2008'
                out.append(d)
    return out


KUPFER_TRACTS = {'Dolny Slask (Lower Silesia)', 'Hercynian-Thuringian Basin', 'Spremberg-Wittenberg', 'Hessian Depression'}
BELT_TRACTS = {'Ore Shale tract', 'Roan Arenite tract', 'Central Carbonate Ecaille tract', 'Southern Carbonate Ecaille tract',
               'Northern Carbonate Ecaille tract'}
# Kupferschiefer districts: ore-bed depth (top, m) and thickness of the mined interval (m). Lubin-Sieroszowice ore lies at
# 600-1250 m (KGHM), Spremberg-Graustein at 800-1500 m (source comment); Mansfeld and Sangerhausen were worked from the
# outcrop down to about 1000 m in a 0.3-0.5 m seam; Richelsdorf medieval workings near the outcrop.
KUPFER_BEDS = {
    'Lubin-Sieroszowice': (600, 3.0, 2.5, 135), 'Konrad-Grodziec-Wartowice': (500, 1.5, 1.6, 120),
    'Lena-Nowy Kosci': (250, 1.5, 1.6, 120), 'Mansfeld': (60, .4, 1.4, 150), 'Sangerhausen': (150, .4, 1.6, 100),
    'Tiefscholle Osterhausen': (700, 1.0, 1.5, 120), 'Feld Heldrungen': (900, 1.0, 1.5, 120), 'Graustein': (800, 2.0, 1.5, 120),
    'Spremberg': (800, 2.0, 1.5, 120), 'Richelsdorf': (60, .5, 1.5, 150),
}


def read_sedcu():
    bodies, area_members = [], defaultdict(list)
    with open(GCRA_SEDCU, encoding='utf-8-sig') as f:
        for r in csv.DictReader(f):
            mt, cu = num(r['Tonnage_Mt']), num(r['Cu_pct'])
            if not mt or not cu:
                log['sedcu_no_tonnage (heat-map background only)'] += 1; continue
            la, lo = num(r['Latitude']), num(r['Longitude'])
            grades = {k: round(v, 4) for k, v in {'Cu': cu, 'Ag': num(r['Ag_g_t'])}.items() if v and v > 0}
            age = age_from_text(r['Age_host']) or age_from_ma(num(r['Age_Ma']))
            rec = {'gid': str(r['GMRAP_ID']).split('.')[0] or 'o' + r['source_objectid'], 'name': r['Name'].strip(), 'country': r['Country'], 'lat': la, 'lon': lo,
                   'mt': mt, 'grades': grades, 'age': age, 'tract': r['Tract_name'], 'sub': r['Dep_subtype'], 'comment': r['Comments']}
            if r['Tract_name'] in KUPFER_TRACTS: area_members['kupferschiefer'].append(rec); continue
            if r['Tract_name'] in BELT_TRACTS: area_members['copperbelt'].append(rec); continue
            top, top_est = 0.0, True
            m = re.search(r'at (\d{2,4})\s*[-\u2013]\s*\d{2,4}\s*m depth', r['Comments'] or '')
            if m: top, top_est = float(m.group(1)), False
            rho = 2.65
            L, Wd, t, est = lens_dims(mt, rho, 'sedcu')
            ores = make_ores(grades, oxide=.1)
            d = {'id': 'usgs-gcra-sedcu:' + rec['gid'], 'name': rec['name'], 'country': rec['country'], 'lat': round(la, 4), 'lon': round(lo, 4),
                 'layer': 'Cu', 'type': 'sediment-hosted Cu (' + (r['Dep_subtype'] or 'stratabound') + ')', 'ores': ores, 'shape': 'lens',
                 'mt': round(mt, 3), 'grade': cu, 'unit': '%', 'grades': grades, 'density': rho, 'topM': top, 'lenKm': L, 'widKm': Wd,
                 'thickM': t, 'strikeDeg': 0, 'dipDeg': 15, 'age': age, 'src': 'usgs-gcra-sedcu', 'q': 'B',
                 'est': sorted(set(est + ['strikeDeg', 'dipDeg'] + (['topM'] if top_est else [])))}
            d['baseM'] = base_of(top, Wd, t, 15)
            bodies.append(d)
    return bodies, area_members


def build_areas(members):
    """Kupferschiefer and Copperbelt districts as areas of stratiform beds. Each district's polygon is an ellipse around its
    deposit sized so the bed covers about 70 % of it at the district's real thickness; overlapping ellipses of one belt
    merge into one area that keeps the summed tonnage."""
    areas = []
    for belt, recs in members.items():
        polys = []
        for rec in recs:
            rho = 2.65
            V = rec['mt'] * 1e6 / rho
            if belt == 'kupferschiefer':
                key = next((k for k in KUPFER_BEDS if rec['name'].startswith(k)), None)
                top, thick, ratio, strike = KUPFER_BEDS.get(key, (500, 1.0, 1.5, 120))
                oxide_top = 0
            else:
                zambia = rec['country'] == 'Zambia'
                thick = 12.0 if rec['tract'] == 'Ore Shale tract' else 15.0
                top = 200.0 if rec['name'].startswith('Konkola') else 20.0 if zambia else 0.0
                ratio, strike = (3.0, 135) if zambia else (2.0, 100)
                oxide_top = 0 if top >= 50 else .1 if zambia else .45  # Katanga ores are largely oxidised to 80-100 m (malachite, heterogenite)
            area_km2 = V / thick / .7 / 1e6
            b = math.sqrt(area_km2 / math.pi / ratio); a = ratio * b
            poly = Polygon(ellipse(rec['lat'], rec['lon'], max(a, .5), max(b, .3), strike))
            polys.append((poly, rec, top, thick, oxide_top))
        # merge overlapping ellipses
        groups = []
        for p in polys:
            hit = [g for g in groups if any(p[0].intersects(q[0]) for q in g)]
            merged = [p] + [q for g in hit for q in g]
            groups = [g for g in groups if g not in hit] + [merged]
        for g in groups:
            g.sort(key=lambda x: -x[1]['mt'] * x[1]['grades']['Cu'])
            geom = shapely.union_all([x[0] for x in g]).simplify(.001)
            lead = g[0][1]
            mt = sum(x[1]['mt'] for x in g)
            cu = sum(x[1]['mt'] * x[1]['grades']['Cu'] for x in g) / mt
            ag = sum(x[1]['mt'] * x[1]['grades'].get('Ag', 0) for x in g) / mt
            top = sum(x[1]['mt'] * x[2] for x in g) / mt
            thick = sum(x[1]['mt'] * x[3] for x in g) / mt
            oxide = sum(x[1]['mt'] * x[4] for x in g) / mt
            lat0 = geom.centroid.y
            area_m2 = geom.area * (111320 ** 2) * math.cos(math.radians(lat0))
            V = mt * 1e6 / 2.65
            cover = V / (area_m2 * thick)
            if cover > .95: thick *= cover / .95; cover = .95
            beds = []
            if oxide > 0:
                beds.append({'code': 'malachite', 'topM': round(top), 'thickM': round(thick * oxide, 2), 'cover': round(cover, 3), 'grade': round(cu, 3)})
                beds.append({'code': 'chalcopyrite', 'topM': round(top + (80 if oxide > .3 else 20)), 'thickM': round(thick * (1 - oxide), 2),
                             'cover': round(cover, 3), 'grade': round(cu, 3)})
            else:
                beds.append({'code': 'chalcopyrite', 'topM': round(top), 'thickM': round(thick, 2), 'cover': round(cover, 3), 'grade': round(cu, 3)})
            if ag >= 30:  # Kupferschiefer silver (KGHM is the world's largest silver producer from this bed)
                agv = value('Ag', ag); cuv = value('Cu', cu); s = agv / (agv + cuv)
                for bd in beds: bd['thickM'] = round(bd['thickM'] * (1 - s), 2)
                beds.append({'code': 'silver', 'topM': round(top), 'thickM': round(thick * s, 2), 'cover': round(cover, 3), 'grade': round(ag, 1), 'unit': 'g/t'})
            names = [x[1]['name'] for x in g]
            ages = Counter(x[1]['age'] for x in g if x[1]['age'])
            area = {'id': 'usgs-gcra-sedcu:' + lead['gid'] + ('+' + str(len(g) - 1) if len(g) > 1 else ''),
                    'name': (lead['name'] if len(g) == 1 else lead['name'] + ' district') + (' (Kupferschiefer)' if belt == 'kupferschiefer' else ' (Copperbelt)'),
                    'layer': 'Cu', 'type': 'sediment-hosted Cu, ' + ('Kupferschiefer' if belt == 'kupferschiefer' else 'Central African Copperbelt'),
                    'country': lead['country'], 'age': ages.most_common(1)[0][0] if ages else None, 'src': 'usgs-gcra-sedcu', 'q': 'B',
                    'geometry': mapping(shapely.set_precision(geom, .0001)), 'beds': beds, 'mt': round(mt, 2), 'grade': round(cu, 3), 'unit': '%',
                    'grades': {k: v for k, v in {'Cu': round(cu, 3), 'Ag': round(ag, 1)}.items() if v},
                    'members': names, 'est': ['geometry', 'topM', 'thickM']}
            fw = [AREA_FIRST_WORKED[k] for n in names for k in AREA_FIRST_WORKED if n.startswith(k)]
            if fw: area['firstWorked'] = min(fw)
            areas.append(area)
    return areas


AREA_FIRST_WORKED = {'Mansfeld': 1199, 'Sangerhausen': 1200, 'Richelsdorf': 1300}  # Mansfeld mining is recorded from 1199


def authored_areas():
    """Districts too young for the USGS compilation: Kamoa-Kakula (discovered 2008)."""
    kk_mt = 1551 + 1046
    kk_cu = (1551 * 2.70 + 1046 * 1.70) / kk_mt
    geom = Polygon(ellipse(-10.82, 25.22, 14, 7, 20))
    lat0 = geom.centroid.y
    area_m2 = geom.area * (111320 ** 2) * math.cos(math.radians(lat0))
    thick = 6.0
    cover = kk_mt * 1e6 / 2.65 / (area_m2 * thick)
    return [{'id': 'named:kamoa-kakula', 'name': 'Kamoa-Kakula (Copperbelt)', 'layer': 'Cu',
             'type': 'sediment-hosted Cu, Central African Copperbelt (reduced facies, Grand Conglomerat base)', 'country': 'Democratic Republic of the Congo',
             'age': 'Precambrian', 'src': 'earth-named-2026', 'q': 'B',
             'ref': 'Ivanhoe Mines, Kamoa-Kakula Integrated Development Plan 2023 (Indicated 1,551 Mt at 2.70 % Cu; Inferred 1,046 Mt at 1.70 % Cu)',
             'geometry': mapping(shapely.set_precision(geom, .0001)),
             'beds': [{'code': 'chalcopyrite', 'topM': 400, 'thickM': thick, 'cover': round(min(.95, cover), 3), 'grade': round(kk_cu, 3)}],
             'mt': kk_mt, 'grade': round(kk_cu, 3), 'unit': '%', 'grades': {'Cu': round(kk_cu, 3)},
             'est': ['geometry', 'topM', 'thickM']}]


def read_vms():
    out = []
    for r in read_zip_csv(VMS_ZIP, 'vms/main.csv'):
        mt = num(r['oreton'])
        if not mt:
            log['vms_no_tonnage'] += 1; continue
        grades = {k: num(r[f]) for k, f in (('Cu', 'cugrd'), ('Zn', 'zngrd'), ('Pb', 'pbgrd'), ('Ag', 'aggrd'), ('Au', 'augrd'))}
        grades = {k: round(v, 4) for k, v in grades.items() if v and v > 0}
        layer = main_metal(grades, ('Cu', 'Zn', 'Pb', 'Ag'))
        if not layer:
            log['vms_no_grade'] += 1; continue
        cover = num(r['cover']); top = cover if cover is not None else 0.0
        rho = 3.6
        L, Wd, t, est = lens_dims(mt, rho, 'vms', num(r['ore_a']), num(r['ore_b']))
        ores = make_ores(grades, oxide=.05 if top == 0 else 0)
        age = age_from_ma(num(r['agemy'])) or age_from_text(r['depage'])
        d = {'id': 'usgs-vms-2009:' + r['rec_id'], 'name': r['depname'].strip(), 'country': r['country'], 'lat': round(num(r['latitude']), 4),
             'lon': round(num(r['longitude']), 4), 'layer': layer, 'type': 'VMS (' + (r['deptype'] or 'unclassified') + ')', 'ores': ores,
             'shape': 'lens', 'mt': round(mt, 3), 'grade': grades[layer], 'unit': UNIT[layer], 'grades': grades, 'density': rho,
             'topM': top, 'lenKm': L, 'widKm': Wd, 'thickM': t, 'strikeDeg': 0, 'dipDeg': 60, 'age': age, 'ageMa': num(r['agemy']),
             'src': 'usgs-vms-2009', 'q': 'A', 'est': sorted(set(est + ['strikeDeg', 'dipDeg'] + ([] if cover is not None else ['topM'])))}
        d['baseM'] = base_of(top, Wd, t, 60)
        out.append(d)
    return out


def read_sedexmvt():
    out = []
    for r in read_zip_csv(SEDEXMVT_ZIP, 'sedexmvt/main.csv'):
        mt = num(r['oreton'])
        if not mt:
            log['sedexmvt_no_tonnage'] += 1; continue
        grades = {k: num(r[f]) for k, f in (('Zn', 'zngrd'), ('Pb', 'pbgrd'), ('Cu', 'cugrd'), ('Ag', 'aggrd'))}
        grades = {k: round(v, 4) for k, v in grades.items() if v and v > 0}
        layer = main_metal(grades, ('Zn', 'Pb', 'Ag', 'Cu'))
        if not layer:
            log['sedexmvt_no_grade'] += 1; continue
        mvt = r['deptype'] == 'MVT'
        kind = 'mvt' if mvt else 'sedex'
        rho = 2.9 if mvt else 3.3
        L, Wd, t, est = lens_dims(mt, rho, kind)
        dip = LENS[kind][4]
        top = 50.0 if mvt else 0.0
        ores = make_ores(grades, oxide=.08)
        age = age_from_text(r['hostage'])
        name = r['depname'].strip() or r['district'].strip()
        d = {'id': 'usgs-sedexmvt-2009:' + r['rec_id'], 'name': name, 'district': r['district'].strip() or None, 'country': r['country'],
             'lat': round(num(r['latitude']), 4), 'lon': round(num(r['longitude']), 4), 'layer': layer,
             'type': ('MVT Pb-Zn' if mvt else 'clastic-dominated (SEDEX) Pb-Zn' if r['deptype'] == 'CD' else 'sediment-hosted Pb-Zn (unclassified)'),
             'ores': ores, 'shape': 'lens', 'mt': round(mt, 3), 'grade': grades[layer], 'unit': UNIT[layer], 'grades': grades, 'density': rho,
             'topM': top, 'lenKm': L, 'widKm': Wd, 'thickM': t, 'strikeDeg': 0, 'dipDeg': dip, 'age': age, 'src': 'usgs-sedexmvt-2009',
             'q': 'A', 'est': sorted(set(est + ['strikeDeg', 'dipDeg', 'topM']))}
        d['baseM'] = base_of(top, Wd, t, dip)
        out.append(d)
    return out


def parse_pairs(s, cast=float):
    out = {}
    for part in re.split(r'[;|]', s or ''):
        part = part.strip()
        if not part or ':' not in part: continue
        k, v = part.split(':', 1)
        out[k.strip()] = v.strip()
    return out


def read_named():
    out = []
    for path in NAMED:
        with open(path, encoding='utf-8-sig', newline='') as f:
            for r in csv.DictReader(f):
                rid = (r.get('id') or '').strip()
                mt, la, lo = num(r.get('mt')), num(r.get('lat')), num(r.get('lon'))
                if not rid or not mt or la is None or lo is None:
                    log['named_row_incomplete ' + path.name] += 1; dropped.append({'file': path.name, 'id': rid, 'why': 'incomplete'}); continue
                layer = (r.get('layer') or '').strip()
                grades = {k: num(v) for k, v in parse_pairs(r.get('grades')).items() if num(v) is not None and num(v) > 0}
                g = num(r.get('grade'))
                if g and layer: grades[layer] = g
                ores = []
                for code, v in parse_pairs(r.get('ores')).items():
                    zone = None
                    if '@' in v: v, zone = v.split('@', 1)
                    if '@' in code: code, zone = code.split('@', 1)
                    code = code.strip(); sh = num(v)
                    if code not in ALLOWED_ORES or not sh:
                        log['named_ore_code_dropped ' + code] += 1; continue
                    o = {'code': code, 'share': sh}
                    if zone and zone.strip() in ('top', 'bottom'): o['zone'] = zone.strip()
                    ores.append(o)
                if not ores:
                    ores = make_ores(grades)
                if not ores:
                    log['named_no_ores'] += 1; dropped.append({'file': path.name, 'id': rid, 'why': 'no ores'}); continue
                tot = sum(o['share'] for o in ores)
                for o in ores: o['share'] = round(o['share'] / tot, 3)
                shp = (r.get('shape') or 'veins').strip()
                if shp not in ('stock', 'lens', 'veins', 'blanket', 'dome'): shp = 'veins'
                placer = any(o['code'] == 'earth-placertin' for o in ores)
                if placer: shp = 'blanket'
                if shp == 'blanket' and not placer:
                    log['named_blanket_without_placer_code -> lens'] += 1; shp = 'lens'
                rho = 1.9 if shp == 'blanket' else 2.7 if shp in ('stock', 'veins') else 3.0
                est = [x for x in re.split(r'[;, ]+', r.get('est') or '') if x]
                fw = num(r.get('firstWorked'))
                d = {'id': 'named:' + rid, 'name': r['name'].strip(), 'country': r['country'].strip(), 'lat': round(la, 4), 'lon': round(lo, 4),
                     'layer': layer, 'type': (r.get('type') or '').strip(), 'ores': ores, 'shape': shp, 'mt': mt,
                     'grade': grades.get(layer), 'unit': UNIT.get(layer, '%'), 'grades': {k: round(v, 4) for k, v in grades.items()},
                     'density': rho, 'topM': num(r.get('topM')) or 0.0, 'baseM': num(r.get('baseM')), 'lenKm': num(r.get('lenKm')),
                     'widKm': num(r.get('widKm')), 'strikeDeg': num(r.get('strikeDeg')), 'dipDeg': num(r.get('dipDeg')),
                     'age': (r.get('age') or '').strip() or None, 'ageMa': num(r.get('ageMa')), 'src': 'earth-named-2026',
                     'q': (r.get('q') or 'C').strip()[:1].upper(), 'ref': (r.get('citation') or '').strip(), 'est': est,
                     'notes': (r.get('notes') or '').strip() or None}
                if num(r.get('topM')) is None and 'topM' not in est: d['est'].append('topM')
                if fw is not None: d['firstWorked'] = int(fw)
                if placer: d['layers'] = ['alluvial', 'outwash']  # 0065 layers: placers in alluvium and outwash, not laterite
                if d['q'] not in 'ABC': d['q'] = 'C'
                if d['q'] == 'A': d['q'] = 'B'  # named figures are cited facts, not checked against the primary report here
                cit = d['ref'].lower()
                if 'recalled' in cit or cit.startswith(('estimate', 'order of magnitude')):
                    if d['q'] != 'C': log['named_q_lowered_to_C (recalled or estimated figure)'] += 1
                    d['q'] = 'C'
                if layer not in CODES:
                    log['named_layer_not_in_group ' + layer] += 1; dropped.append({'file': path.name, 'id': rid, 'why': 'layer ' + layer}); continue
                d['_file'] = path.name
                out.append(finish(d, 'lens'))
    return out


# ------------------------------------------------------------------------------------------------ merge and checks
FAMILY = {'Cu': 'base', 'Zn': 'base', 'Pb': 'base', 'Ag': 'base', 'Sn': 'Sn', 'W': 'W', 'Mo': 'Mo', 'Sb': 'Sb', 'Bi': 'Bi', 'Hg': 'Hg'}
# For named rows against the databases copper stands apart from lead-zinc-silver: a porphyry under a silver-lead vein
# district (Toromocho/Morococha, Vysoka/Banska Stiavnica) or a Revett copper bed beside a silver vein (Snowstorm/Lucky
# Friday) is two bodies, not one.
GROUP = {'Cu': 'Cu', 'Zn': 'PbZnAg', 'Pb': 'PbZnAg', 'Ag': 'PbZnAg', 'Sn': 'Sn', 'W': 'W', 'Mo': 'Mo', 'Sb': 'Sb', 'Bi': 'Bi', 'Hg': 'Hg'}
# Reviewed pairs that the distance and name rules would merge but that are different deposits.
NOT_DUPLICATE = {('named:ag-iwami', 'usgs-vms-2009:775'),  # Iwami Ginzan silver mine; the Iwami kuroko deposit lies 11 km NE
                 ('named:ag-banska-stiavnica', 'usgs-gcra-pcu:3100')}  # epithermal Ag-Pb-Zn veins above the buried porphyry


def worth(d):
    """Value of the whole body (USD), to compare a named district with a database entry inside it."""
    return d['mt'] * sum(value(m, g) for m, g in d.get('grades', {}).items())


def contained(d, metal=None):
    m = metal or d['layer']
    g = d.get('grades', {}).get(m)
    if g is None: return 0.0
    mt = d['mt'] if m == 'Fe' else d['mt'] - d.get('gossanMt', 0)  # gossan tonnage carries only the Fe
    return mt * g / (1e6 if UNIT.get(m) == 'g/t' else 100)  # Mt of metal (Ag: Mt too; x1000 for kt)


def dedupe(db, named, areas):
    """Named rows that repeat a USGS database entry (same metal group, within 6 km, or same name within 25 km) are dropped;
    their history (first worked, notes) moves onto the database entry. A named district worth at least three times the
    database entry it meets (Zawar over Mochia, Mantoverde over the Farola mine) replaces that entry instead. Named-named
    repeats across the authored tables keep the larger; rows of one table are separate by intent (Monte Amiata split)."""
    keep = []
    area_geoms = [(shape(a['geometry']), a) for a in areas]
    for n in sorted(named, key=lambda d: -contained(d) * 1):
        hit = None
        for d in db:
            if abs(d['lat'] - n['lat']) > .3 or (n['id'], d['id']) in NOT_DUPLICATE: continue
            dist = haversine_km(n['lat'], n['lon'], d['lat'], d['lon'])
            samefam = GROUP.get(d['layer']) == GROUP.get(n['layer'])
            samename = norm(d['name'])[:5] == norm(n['name'])[:5] and len(norm(n['name'])) >= 4
            if (samefam and dist < 6) or (samename and dist < 25):
                if hit is None or dist < hit[0]: hit = (dist, d)
        if hit and worth(n) >= 3 * worth(hit[1]):
            d = hit[1]
            db.remove(d)
            if d.get('firstWorked') is not None and n.get('firstWorked') is None: n['firstWorked'] = d['firstWorked']
            log['usgs_entry_inside_larger_named_district (replaced)'] += 1
            dropped.append({'id': d['id'], 'why': 'inside the larger named district ' + n['id'] + ' ' + n['name']})
            hit = None
        ahit = next((a for g, a in area_geoms if g.distance(Point(n['lon'], n['lat'])) < .05 and GROUP.get(n['layer']) == 'Cu'), None)
        if hit or ahit:
            tgt = hit[1] if hit else ahit
            if n.get('firstWorked') is not None and tgt.get('firstWorked') is None: tgt['firstWorked'] = n['firstWorked']
            if n.get('notes'): tgt.setdefault('history', n['notes'])
            log['named_duplicate_of_usgs (dropped, history kept)'] += 1
            dropped.append({'file': n['_file'], 'id': n['id'], 'why': 'duplicate of ' + tgt['id'] + ' ' + tgt['name']})
            continue
        twin = next((k for k in keep if k['_file'] != n['_file'] and FAMILY.get(k['layer']) == FAMILY.get(n['layer']) and (k['shape'] == 'blanket') == (n['shape'] == 'blanket')
                     and (haversine_km(n['lat'], n['lon'], k['lat'], k['lon']) < 1.5 or (norm(k['name'])[:6] == norm(n['name'])[:6]
                          and haversine_km(n['lat'], n['lon'], k['lat'], k['lon']) < 15))), None)
        if twin:
            log['named_duplicate_of_named'] += 1
            dropped.append({'file': n['_file'], 'id': n['id'], 'why': 'duplicate of ' + twin['id']}); continue
        keep.append(n)
    return keep


class Geo:
    def __init__(self):
        sys.path.insert(0, str(ROOT / 'tools'))
        import bake_ore_heatmaps as bake  # the bake's country check, so the catalogue and the heat maps agree
        cfg = json.loads((ROOT / 'tools/ore-heatmap-settings.json').read_text())['coordinateCheck']
        self.cc = bake.CountryCheck(ROOT / cfg['table'], cfg['marginDegrees'], ROOT / cfg['naturalEarth'], .25)
        from pyogrio.raw import read
        _, _, wkb, cols = read('/vsizip/' + NATURAL_EARTH.as_posix() + '/ne_10m_admin_0_countries.shp', columns=['ADMIN'])
        self.land = shapely.union_all(shapely.from_wkb(wkb)); shapely.prepare(self.land)
        self.geoms = shapely.from_wkb(wkb); self.names = [str(x) for x in cols[0]]
        self.tree = shapely.STRtree(self.geoms)

    def on_land(self, lat, lon, tol=.02):
        return bool(shapely.dwithin(self.land, Point(lon, lat), tol))

    def country_at(self, lat, lon):
        idx = self.tree.query(Point(lon, lat), predicate='intersects')
        if len(idx): return self.names[idx[0]]
        idx = self.tree.query_nearest(Point(lon, lat))
        return self.names[idx[0]] + ' (nearest)' if len(idx) else None


COUNTRY_FIX = {'Russia (Buryatia Republic)': 'Russia', 'Kirghistan': 'Kyrgyzstan', 'Congo (Kinshasa)': 'Democratic Republic of the Congo'}
# Reviewed coordinate fixes the sign-flip repair cannot find (id: lat, lon, why).
COORD_FIX = {
    'usgs-sedexmvt-2009:220': (34.46, -1.76, 'Touissit-Bou Beker mines lie on the Moroccan side of the border (source point is 25 km east, in Algeria)'),
}


def repair_coords(entries, geo):
    """Source points outside their country: reviewed fixes first, then the bake's single sign-flip/swap repair."""
    out = []
    for d in entries:
        if d['id'] in COORD_FIX:
            la, lo, why = COORD_FIX[d['id']]
            out.append({'id': d['id'], 'name': d['name'], 'from': [d['lat'], d['lon']], 'to': [la, lo], 'how': 'reviewed: ' + why})
            d['lat'], d['lon'] = la, lo; continue
        lab = COUNTRY_FIX.get(d.get('country'), d.get('country'))
        if geo.cc.inside(lab, d['lat'], d['lon']) is False:
            how, fixed = geo.cc.repair(lab, d['lat'], d['lon'])
            if how:
                out.append({'id': d['id'], 'name': d['name'], 'from': [d['lat'], d['lon']], 'to': [round(fixed[0], 4), round(fixed[1], 4)], 'how': how})
                d['lat'], d['lon'] = round(fixed[0], 4), round(fixed[1], 4)
    return out


def check(entries, areas, geo):
    rep = {'offLand': [], 'outsideCountry': [], 'ageConflicts': [], 'shapeLayerConflicts': []}
    for d in entries:
        lab = COUNTRY_FIX.get(d.get('country'), d.get('country'))
        offshore = 'offshore' in (d.get('type') or '').lower()
        if not geo.on_land(d['lat'], d['lon']) and not offshore:
            rep['offLand'].append({'id': d['id'], 'name': d['name'], 'lat': d['lat'], 'lon': d['lon'], 'country': lab})
        ok = geo.cc.inside(lab, d['lat'], d['lon'])
        if ok is False:
            rep['outsideCountry'].append({'id': d['id'], 'name': d['name'], 'lat': d['lat'], 'lon': d['lon'], 'country': lab,
                                          'at': geo.country_at(d['lat'], d['lon'])})
        # 0060 layer rules: placer codes only as blankets (alluvial); every other ore of this group is hard rock (bed or basement)
        placer = any(o['code'] == 'earth-placertin' for o in d['ores'])
        if placer != (d['shape'] == 'blanket'):
            rep['shapeLayerConflicts'].append({'id': d['id'], 'name': d['name'], 'shape': d['shape']})
        if placer and d.get('age') not in (None, 'Cenozoic'):
            rep['ageConflicts'].append({'id': d['id'], 'name': d['name'], 'why': 'placer in ' + str(d.get('age'))})
        # host age must not be younger than the mineralisation age
        if d.get('ageMa') and d.get('age') in ERA:
            lo, hi = ERA_MA[ERA[d['age']]]
            if d['ageMa'] > hi * 1.05 + 5:
                rep['ageConflicts'].append({'id': d['id'], 'name': d['name'], 'why': f"host {d['age']} younger than ore age {d['ageMa']} Ma"})
        if not d.get('age'):
            rep['ageConflicts'].append({'id': d['id'], 'name': d['name'], 'why': 'no age'})
    for a in areas:
        c = shape(a['geometry']).representative_point()
        if not geo.on_land(c.y, c.x): rep['offLand'].append({'id': a['id'], 'name': a['name']})
    return rep


PRECIOUS = [CATALOGUE.parent / 'precious-gems.json',  # after the group branches are merged
            ROOT.parent / 'ore-precious-gems' / 'assets/earth/config/ore-deposits-v1/precious-gems.json']  # sibling worktree
# One real deposit appears once across the group catalogues, owned by the group of its main commodity by value; the
# co-products are ore shares inside that body (coordinator rule, 8 Nov). Reviewed pairs where precious-gems owns the
# deposit, so this catalogue leaves it out (id: precious id, why).
OWNED_BY_PRECIOUS = {
    'usgs-vms-2009:881': ('pg:au-mount-morgan', 'Au 3.8 g/t adds more value than Cu 0.75 %; precious lists it with a chalcopyrite share'),
    'usgs-gcra-pcu:468': ('pg:au-zijinshan', 'one Au-Cu system; precious lists it (600 Mt at 0.5 g/t) with a chalcopyrite share'),
    'usgs-gcra-pcu:359': ('pg:au-barza-brad', 'Musariu is inside the Barza-Brad (Ruda-Musariu) gold district; Au adds more value than Cu 0.2 %'),
    'named:ag-jachymov': ('pg:u-jachymov', 'the same vein system; uranium is the main value'),
}
# Bodies other groups also name within a few km that are different deposits (checked, both kept): Rosia Poieni porphyry
# and the Rosia Montana epithermal gold, Bolcana and Sacaramb, Brezove Hory Ag-Pb veins and the Pribram uranium field,
# Schneeberg and Schlema-Alberoda, Wildcat and the Baguio gold district, Outokumpu Cu and its uvarovite, VMS lenses and
# ophiolite chromite pods (iron-ferro).


def drop_owned_elsewhere(deps):
    keep = []
    for d in deps:
        if d['id'] in OWNED_BY_PRECIOUS:
            pid, why = OWNED_BY_PRECIOUS[d['id']]
            log['owned by precious-gems (left out)'] += 1
            dropped.append({'id': d['id'], 'why': f'owned by precious-gems as {pid}: {why}'}); continue
        keep.append(d)
    return keep


def add_coproducts(deps):
    """Gold and uranium join a body's ores by the value they add per tonne of ore, like the other metals, when that is at
    least 3 %. The precious group leaves the gold of base-metal bodies (porphyry Cu-Au, VMS, Ag-Au epithermal veins such
    as the Comstock) and the uranium of Olympic Dam to this group. Placers (blankets) keep their tin only."""
    for d in deps:
        if d['shape'] == 'blanket': continue
        for m, code in (('Au', 'gold'), ('U', 'uranium')):
            g = d.get('grades', {}).get(m)
            if not g: continue
            tot = sum(value(k, x) for k, x in d['grades'].items() if k in PRIMARY or k in ('Au', 'U'))
            s = value(m, g) / tot if tot else 0
            if s < .03: continue
            for o in d['ores']: o['share'] = round(o['share'] * (1 - s), 3)
            d['ores'].append({'code': code, 'share': round(s, 3)})
            d['ores'][0]['share'] = round(d['ores'][0]['share'] + 1 - sum(o['share'] for o in d['ores']), 3)
            log[f'{code} share added'] += 1


# Co-products handed over by iron-ferro (8 Nov): its own bodies for them sat on these deposits and are gone.
# Gossan: the iron-oxide cap of the massive sulphides, extra material above them. Its tonnage joins the body (mt, density,
# thickness), limonite takes the gossan's share of the tonnage in the top zone, and gossanMt marks that part: the other
# metals' grades stay over the sulphide tonnage (mt - gossanMt). (id: gossan Mt, Fe %, gossan density, source)
GOSSAN = {
    'usgs-vms-2009:953': (100.0, 45.0, 3.2, 'Rio Tinto gossans (Cerro Colorado and others): about 100 Mt limonite-hematite at about '
                                            '45 % Fe, iron-ferro estimate (q C) added to the sulphide tonnage'),
    'usgs-vms-2009:712': (15.5, 45.0, 3.2, 'Tharsis gossan over Filon Sur: 15.5 Mt with 29 g/t Ag and 1.74 g/t Au (Leistel and others '
                                           '1998, in the USGS record); Fe about 45 % as iron-ferro\'s Iberian gossans'),
}
# Oxidised ore inside the body (no extra tonnage): id: [(ore, share, metal, grade of the oxidised ore, source)].
OXIDISED_EXTRA = {
    'usgs-sedexmvt-2009:22': [('vanadinite', .10, 'V', 1.0, 'descloizite (stands as vanadinite) in the oxidised ore, about 10 % of '
                                                            'the ore at about 1 % V2O5 (iron-ferro, q C)')],
}


def rescale(d, share, ore):
    """Make room for a new ore share: the others shrink by (1 - share); rounding drift goes to the first ore."""
    for o in d['ores']: o['share'] = round(o['share'] * (1 - share), 3)
    d['ores'].append(ore)
    d['ores'][0]['share'] = round(d['ores'][0]['share'] + 1 - sum(o['share'] for o in d['ores']), 3)


def add_handed_over(deps):
    by = {d['id']: d for d in deps}
    for i, (gmt, fe, rho_g, why) in GOSSAN.items():
        d = by[i]
        vs, vg = d['mt'] / d['density'], gmt / rho_g
        s = gmt / (d['mt'] + gmt)  # share of the tonnage (README)
        rescale(d, s, {'code': 'limonite', 'share': round(s, 3), 'zone': 'top'})
        d['mt'] = round(d['mt'] + gmt, 3); d['density'] = round(d['mt'] / (vs + vg), 2); d['gossanMt'] = gmt
        d['grades']['Fe'] = fe
        given = 'lenKm' not in d.get('est', [])
        L, Wd, t, e = lens_dims(d['mt'], d['density'], 'vms', d['lenKm'] if given else None, d['widKm'] if given else None)
        d['lenKm'], d['widKm'], d['thickM'] = L, Wd, t
        d['baseM'] = base_of(d['topM'], Wd, t, d['dipDeg'])
        d['notes'] = ((d.get('notes') or '') + ' ' + why + '.').strip()
        log['gossan (limonite) added'] += 1
    for i, extra in OXIDISED_EXTRA.items():
        d = by[i]
        for code, s, m, g, why in extra:
            rescale(d, s, {'code': code, 'share': s, 'zone': 'top'})
            d['grades'][m] = g
            d['notes'] = ((d.get('notes') or '') + ' ' + why + '.').strip()
            log[f'{code} share added'] += 1


def near_other_groups(deps):
    """Bodies of other group catalogues within 5 km, for review (no action; reviewed pairs are listed above)."""
    out = []
    for grp in ('precious-gems', 'iron-ferro', 'coal-evaporites'):
        src = next((p for p in (CATALOGUE.parent / f'{grp}.json', ROOT.parent / f'ore-{grp}' / f'assets/earth/config/ore-deposits-v1/{grp}.json') if p.is_file()), None)
        if not src: continue
        for o in json.loads(src.read_text(encoding='utf-8'))['deposits']:
            for d in deps:
                if abs(d['lat'] - o['lat']) < .1 and haversine_km(d['lat'], d['lon'], o['lat'], o['lon']) < 5:
                    out.append({'id': d['id'], 'name': d['name'], 'other': o['id'], 'otherName': o['name'], 'group': grp})
    return out


def strip(d):
    d = {k: v for k, v in d.items() if not k.startswith('_') and v is not None and v != [] and v != ''}
    for k in ('lenKm', 'widKm'):
        if k in d: d[k] = round(d[k], 3)
    for k in ('topM', 'baseM', 'strikeDeg', 'dipDeg'):
        if k in d: d[k] = round(d[k]) if abs(d[k]) >= 10 else round(d[k], 1)
    return d


def main():
    singer = porphyry_singer()
    porph = read_porphyry(singer)
    sedcu, members = read_sedcu()
    areas = build_areas(members) + authored_areas()
    vms = read_vms()
    smvt = read_sedexmvt()
    db = porph + sedcu + vms + smvt
    # one entry per place: the VMS and SEDEX/MVT compilations share a few deposits (Rammelsberg-type); keep the larger
    seen = []
    uniq = []
    for d in sorted(db, key=lambda d: -d['mt']):
        twin = next((s for s in seen if s['src'] != d['src'] and abs(s['lat'] - d['lat']) < .02 and abs(s['lon'] - d['lon']) < .02 and FAMILY[s['layer']] == FAMILY[d['layer']]), None)
        if twin:
            log['usgs_cross_database_duplicate'] += 1; dropped.append({'id': d['id'], 'why': 'same place as ' + twin['id']}); continue
        seen.append(d); uniq.append(d)
    named = dedupe(uniq, read_named(), areas)
    for d in named: d['lat'] = round(d['lat'], 3); d['lon'] = round(d['lon'], 3)
    geo = Geo()
    repairs = repair_coords(uniq + named, geo)
    bodies = drop_owned_elsewhere(uniq + named)
    add_coproducts(bodies)
    add_handed_over(bodies)
    deposits =[strip(d) for d in sorted(bodies, key=lambda d: (d['layer'], -contained(d)))]
    near = near_other_groups(deposits)
    areas = [strip(a) for a in areas]
    checks = check(deposits, areas, geo)
    checks['coordinateRepairs'] = repairs

    cat = {'version': 1, 'group': 'base-metals', 'built': TODAY, 'sources': SOURCES, 'gradeBands': GRADE_BANDS,
           'oreMetal': ORE_METAL,
           'notes': ['mt is the pre-mining ore tonnage (production + reserves + resources) at the source cut-off; grades in grades{} per metal '
                     '(% except Ag and Au in g/t; W as % WO3). ores[] share a body between its ores by the value each metal adds; an ore\'s tier '
                     'follows its own metal\'s grade through gradeBands (oreMetal). gold (porphyry Cu-Au, VMS, Ag-Au veins) and '
                     'uranium (Olympic Dam) are ore shares here, graded with the precious group\'s Au and U bands. Each real deposit '
                     'is in one group catalogue only, owned by its main commodity by value.',
                     'Co-products handed over by iron-ferro: limonite gossans at Rio Tinto and Tharsis (gossanMt: the gossan '
                     'tonnage included in mt, carried by the limonite share in the top zone; the other metals\' grades are over '
                     'mt - gossanMt) and vanadinite (for descloizite) in the oxidised zone of Berg Aukas, graded with iron-ferro\'s '
                     'Fe and V bands.',
                     'Tin placers (blankets) carry layers ["alluvial", "outwash"]; hard-rock bodies use the 0060 table (bed, basement).',
                     'wulfenite stands in for molybdenite (the game has no molybdenite); tetrahedrite stands in for stibnite.',
                     'lens: lenKm along strike, widKm down dip, thickM true thickness; veins: footprint of the swarm, about 3 % of it ore; '
                     'blanket: tin placers in alluvial ground (0060 class alluvial/outwash).',
                     'est lists the fields estimated from the deposit type rules of tools/ore-data/base-metals/build_catalogue.py.'],
           'deposits': deposits, 'areas': areas}
    CATALOGUE.parent.mkdir(parents=True, exist_ok=True)
    CATALOGUE.write_text(json.dumps(cat, separators=(',', ':'), ensure_ascii=False) + '\n', encoding='utf-8')
    size = CATALOGUE.stat().st_size

    # ---- report
    by_layer = Counter(d['layer'] for d in deposits)
    by_src = Counter(d['src'] for d in deposits)
    metal = defaultdict(float); metal_country = defaultdict(lambda: defaultdict(float))
    for d in deposits:
        for m in CODES:
            c = contained(d, m)
            if c:
                metal[m] += c; metal_country[m][COUNTRY_FIX.get(d['country'], d['country'])] += c
    for a in areas:
        for m, g in a['grades'].items():
            c = a['mt'] * g / (1e6 if UNIT.get(m) == 'g/t' else 100)
            metal[m] += c; metal_country[m][COUNTRY_FIX.get(a['country'], a['country'])] += c
    tiers = defaultdict(Counter)
    for d in deposits:
        for o in d['ores']:
            mm = ORE_METAL[o['code']].split('|')
            m = next((x for x in mm if d['grades'].get(x)), mm[0])
            t = tier(m, d['grades'].get(m))
            tiers[m][t] += d['mt'] * o['share']
    top20 = sorted(deposits + [dict(a, lat=None, lon=None) for a in areas], key=lambda d: -value(d['layer'], d['grade']) * d['mt'])[:20]
    rep = {'built': TODAY, 'catalogueBytes': size, 'deposits': len(deposits), 'areas': len(areas), 'byLayer': dict(by_layer),
           'bySource': dict(by_src), 'byShape': dict(Counter(d['shape'] for d in deposits)),
           'quality': dict(Counter(d['q'] for d in deposits)),
           'containedMt': {m: round(v, 4) for m, v in metal.items()},
           'containedByCountry': {m: dict(sorted(((k, round(v, 4)) for k, v in c.items()), key=lambda kv: -kv[1])[:15]) for m, c in metal_country.items()},
           'tierShareOfTonnage': {m: {k: round(v / max(1e-9, sum(c.values())), 3) for k, v in c.items()} for m, c in tiers.items()},
           'top20ByValue': [{'id': d['id'], 'name': d['name'], 'country': d.get('country'), 'layer': d['layer'], 'mt': d['mt'], 'grade': d['grade'],
                             'unit': d['unit'], 'containedMt': round(d['mt'] * d['grade'] / (1e6 if d['unit'] == 'g/t' else 100), 3)} for d in top20],
           'checks': checks, 'log': dict(log), 'dropped': dropped, 'nearOtherGroups': near,
           'preWorked1318': sorted([{'id': d['id'], 'name': d['name'], 'firstWorked': d['firstWorked']} for d in deposits + areas
                                    if d.get('firstWorked') is not None and d['firstWorked'] <= 1318], key=lambda x: x['firstWorked'])}
    REPORT.write_text(json.dumps(rep, indent=1, ensure_ascii=False), encoding='utf-8')
    print(json.dumps({k: rep[k] for k in ('catalogueBytes', 'deposits', 'areas', 'byLayer', 'bySource', 'containedMt', 'log')}, ensure_ascii=False))
    print('checks:', {k: len(v) for k, v in checks.items()})


if __name__ == '__main__':
    main()
