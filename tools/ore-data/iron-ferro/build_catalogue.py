"""Build assets/earth/config/ore-deposits-v1/iron-ferro.json (decision 0065, iron, ferroalloys and aluminium).

Inputs (all re-read on every run; no network):
  tools/ore-data/iron-ferro/authored-deposits.csv   well-known deposits with published endowment figures (q B/C)
  tools/ore-data/iron-ferro/authored-areas.json     iron formations, ironstones, Mn basins, chromitite and PGE reefs
  docs/research/remaining-ores-2026-09-29/raw/laterite-laterite-csv.zip    USGS Ni-Co laterites (Berger et al. 2011)
  docs/research/remaining-ores-2026-09-29/raw/podchrome-podchrome-csv.zip  USGS podiform chromite (Mosier et al. 2012)

Run:  C:\\VS mods\\Earth\\.venv\\Scripts\\python.exe -I tools/ore-data/iron-ferro/build_catalogue.py
"""
from pathlib import Path
import csv, io, json, math, re, zipfile

import numpy as np
import shapely
from shapely.geometry import Polygon, MultiPolygon, mapping

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
RAW = ROOT / 'docs/research/remaining-ores-2026-09-29/raw'
OUT = ROOT / 'assets/earth/config/ore-deposits-v1/iron-ferro.json'
RETRIEVED = '2026-11-08'

SOURCES = [
    {'id': 'usgs-laterite-2011', 'title': 'Berger, Singer, Bliss, Moring 2011, Ni-Co laterite deposits of the world: database and grade and tonnage models (USGS OFR 2011-1058)',
     'url': 'https://pubs.usgs.gov/of/2011/1058/', 'licence': 'public domain (USGS)', 'retrieved': '2026-09-29'},
    {'id': 'usgs-podchrome-2012', 'title': 'Mosier, Singer, Moring, Galloway 2012, Podiform chromite deposits: database and grade and tonnage models (USGS SIR 2012-5157)',
     'url': 'https://pubs.usgs.gov/sir/2012/5157/', 'licence': 'public domain (USGS)', 'retrieved': '2026-09-29'},
    {'id': 'usgs-mcs-2025', 'title': 'U.S. Geological Survey 2025, Mineral Commodity Summaries 2025 (iron ore, manganese, chromium, nickel, titanium mineral concentrates, vanadium, platinum-group metals, bauxite)',
     'url': 'https://pubs.usgs.gov/periodicals/mcs2025/', 'licence': 'public domain (USGS)', 'retrieved': RETRIEVED},
    {'id': 'published-endowment', 'title': 'Earth iron-ferro compilation of well-known published endowment figures (production + reserves + resources) from company reports, national geological surveys and review papers named in each entry\'s ref; positions and outlines from published maps',
     'url': 'https://github.com/Temporal-Poets-Society (Earth repository: tools/ore-data/iron-ferro/authored-deposits.csv, authored-areas.json)',
     'licence': 'facts with citation (no third-party data copied); compilation CC0 by the Earth project', 'retrieved': RETRIEVED},
]

GRADE_BANDS = {
    'Fe': {'unit': '%', 'basis': 'Fe', 'medium': 30, 'rich': 45, 'bountiful': 60},
    'Mn': {'unit': '%', 'basis': 'Mn', 'medium': 20, 'rich': 35, 'bountiful': 45},
    'Cr': {'unit': '%', 'basis': 'Cr2O3', 'medium': 25, 'rich': 38, 'bountiful': 46},
    'Ni': {'unit': '%', 'basis': 'Ni', 'medium': 0.8, 'rich': 1.5, 'bountiful': 2.5},
    'Ti': {'unit': '%', 'basis': 'TiO2', 'medium': 3, 'rich': 10, 'bountiful': 25},
    'V': {'unit': '%', 'basis': 'V2O5', 'medium': 0.3, 'rich': 0.8, 'bountiful': 1.4},
    'Pt': {'unit': 'g/t', 'basis': 'PGE (3E/4E)', 'medium': 1.5, 'rich': 4, 'bountiful': 8},
    'Al': {'unit': '%', 'basis': 'Al2O3', 'medium': 40, 'rich': 48, 'bountiful': 55},
}
UNIT = {k: v['unit'] for k, v in GRADE_BANDS.items()}

# Default vertical extent (m) and thickness (m) by shape when the source gives none.
DEFAULT_EXTENT = {'stock': 600, 'lens': 500, 'veins': 600, 'blanket': 10}
DEFAULT_THICK = {'lens': 40, 'veins': 2}


def r(x, n=3):
    return None if x is None else float(round(x, n))


def fill_dims(d, density):
    """Fill missing topM/baseM/lenKm/widKm/strikeDeg/dipDeg from the real volume; record what was estimated in est."""
    est = []
    vol = d['mt'] * 1e6 / density  # m3
    shape = d['shape']
    if d.get('topM') is None: d['topM'] = 0; est.append('topM')
    if d.get('baseM') is None:
        d['baseM'] = d['topM'] + DEFAULT_EXTENT[shape]; est.append('baseM')
    h = max(1.0, d['baseM'] - d['topM'])
    if d.get('lenKm') is None or d.get('widKm') is None:
        if shape in ('stock',):
            side = math.sqrt(vol / h); d['lenKm'] = r(side * 1.3 / 1000); d['widKm'] = r(side / 1.3 / 1000)
        elif shape == 'blanket':
            area = vol / h; ln = math.sqrt(area * 2.5); d['lenKm'] = r(ln / 1000); d['widKm'] = r(area / ln / 1000)
        elif shape == 'veins':
            area = vol / h / 0.05  # 5 % of the footprint is vein
            ln = math.sqrt(area * 3); d['lenKm'] = r(ln / 1000); d['widKm'] = r(area / ln / 1000)
        else:  # lens: thickness across strike, the rest along strike
            if d.get('dipDeg') is None and h < 100:  # thin shallow sedimentary lens: flat
                d['dipDeg'] = 0; est.append('dipDeg')
            dip = d.get('dipDeg') if d.get('dipDeg') is not None else 60
            thick = min(DEFAULT_THICK['lens'], max(2.0, (vol / h) ** 0.5 / 4))
            down = h / max(.2, math.sin(math.radians(dip))) if dip > 5 else h
            ln = vol / (thick * down)
            if dip <= 5:  # flat lens: footprint from volume / thickness
                area = vol / h; ln = math.sqrt(area * 2.5); thick = area / ln
            d['lenKm'] = r(max(ln, 1) / 1000); d['widKm'] = r(max(thick, 1) / 1000)
        est += ['lenKm', 'widKm']
    if d.get('strikeDeg') is None and shape in ('lens', 'veins'): d['strikeDeg'] = 0; est.append('strikeDeg')
    if d.get('dipDeg') is None:
        d['dipDeg'] = {'stock': 90, 'lens': 60, 'veins': 80, 'blanket': 0}[shape]; est.append('dipDeg')
    if d.get('strikeDeg') is None: d['strikeDeg'] = 0
    if est: d['est'] = est
    return d


def num(v):
    v = (v or '').strip()
    return float(v) if v else None


def authored_deposits():
    out = []
    with (HERE / 'authored-deposits.csv').open(encoding='utf-8') as f:
        for row in csv.DictReader(f):
            ores = []
            for part in row['ores'].split(';'):
                bits = part.split(':')
                o = {'code': bits[0], 'share': float(bits[1])}
                if len(bits) > 2: o['zone'] = bits[2]
                ores.append(o)
            layer = row['layer']
            d = {'id': 'iron-ferro:' + row['id'], 'name': row['name'], 'country': row['country'],
                 'lat': float(row['lat']), 'lon': float(row['lon']), 'layer': layer, 'ores': ores, 'shape': row['shape'],
                 'mt': float(row['mt']), 'grade': num(row['grade']), 'unit': UNIT[layer], 'density': num(row['density']),
                 'topM': num(row['topM']), 'baseM': num(row['baseM']), 'lenKm': num(row['lenKm']), 'widKm': num(row['widKm']),
                 'strikeDeg': num(row['strikeDeg']), 'dipDeg': num(row['dipDeg']), 'age': row['age'],
                 'src': 'published-endowment', 'q': row['q'], 'ref': row['ref']}
            fill_dims(d, d['density'])
            out.append(d)
    return out


def zip_csv(zpath, member):
    with zipfile.ZipFile(zpath) as z:
        b = z.read(member)
    lines = []
    for line in b.split(b'\n'):  # the USGS tables mix UTF-8 and cp1252 rows
        try: lines.append(line.decode('utf-8'))
        except UnicodeDecodeError: lines.append(line.decode('cp1252', errors='replace'))
    raw = '\n'.join(lines)
    return list(csv.DictReader(io.StringIO(raw)))


def first_number(v):
    """A number field, or the first number of a text such as '>15' or '10-30'. Handles 8.6e-05 (USGS tonnages)."""
    s = str(v or '').strip()
    try: return float(s)
    except ValueError: pass
    m = re.search(r'\d+(?:\.\d+)?(?:[eE][-+]?\d+)?', s)
    return float(m.group()) if m else None


def age_from_text(t, default='Cenozoic'):
    t = (t or '').casefold()
    for key, age in [('cretaceous', 'Mesozoic'), ('jurassic', 'Mesozoic'), ('triassic', 'Mesozoic'), ('permian', 'Permian'),
                     ('carboniferous', 'Carboniferous'), ('devonian', 'Devonian'), ('paleozoic', 'Paleozoic'), ('silurian', 'Paleozoic'),
                     ('ordovician', 'Paleozoic'), ('cambrian', 'Paleozoic'), ('proterozoic', 'Precambrian'), ('archean', 'Precambrian'),
                     ('precambrian', 'Precambrian'), ('mesozoic', 'Mesozoic')]:
        if key in t: return age
    return default


def laterites():
    """USGS Ni-Co laterites: blanket bodies. Weathering age is the deposit's age (laterite cover)."""
    out = []
    for row in zip_csv(RAW / 'laterite-laterite-csv.zip', 'laterite/main.csv'):
        # nitogrd is the Ni grade of 'tonnage' (production + reserves + resources); nigrd is often blank
        mt = first_number(row['tonnage']); ni = first_number(row['nitogrd']) or first_number(row['nigrd'])
        if not mt or mt <= 0 or not row['latitude'] or not row['longitude']: continue
        lat, lon = float(row['latitude']), float(row['longitude'])
        sub = (row.get('subtype') or '').casefold()
        if 'oxide' in sub and 'silicate' not in sub.split('(')[-1]: lim = 0.7
        elif 'clay' in sub: lim = 0.5
        elif 'hydrous' in sub: lim = 0.3
        else: lim = 0.5
        thick = first_number(row.get('latthick')) or 20.0
        area = first_number(row.get('deparea'))
        country = "Cote d'Ivoire" if 'Ivoire' in row['country'] else row['country'].replace('�', '-')  # source has U+FFFD
        d = {'id': 'usgs-laterite-2011:' + row['rec_id'], 'name': row['depname'], 'country': country, 'lat': lat, 'lon': lon,
             'layer': 'Ni', 'ores': [{'code': 'limonite', 'share': lim, 'zone': 'top'}, {'code': 'pentlandite', 'share': round(1 - lim, 2), 'zone': 'bottom'}],
             'shape': 'blanket', 'mt': mt, 'grade': ni, 'unit': '%', 'density': 1.6, 'topM': 0, 'baseM': float(min(60, max(5, thick))),
             'lenKm': None, 'widKm': None, 'strikeDeg': None, 'dipDeg': 0,
             'age': age_from_text(row.get('ageweather'), 'Cenozoic'), 'src': 'usgs-laterite-2011', 'q': 'B',
             'ref': 'subtype ' + (row.get('subtype') or '?') + '; pentlandite stands for the Ni silicate and Ni limonite ores'}
        if area and area > 0:
            ln = math.sqrt(area * 2.5); d['lenKm'] = r(ln); d['widKm'] = r(area / ln)
        fill_dims(d, 1.6)
        if 'baseM' not in (d.get('est') or []) and not row.get('latthick'): d.setdefault('est', []).append('baseM')
        out.append(d)
    return out


def podchrome(skip_near):
    out = []
    for row in zip_csv(RAW / 'podchrome-podchrome-csv.zip', 'podchrome/main.csv'):
        mt = first_number(row['oreton']); gr = first_number(row['crgrd'])
        if not mt or mt <= 0 or not row['latitude'] or not row['longitude']: continue
        lat, lon = float(row['latitude']), float(row['longitude'])
        if any(abs(lat - a) < .1 and abs(lon - b) < .15 for a, b in skip_near): continue
        agemy = first_number(row.get('agemy'))
        age = ('Cenozoic' if agemy < 66 else 'Mesozoic' if agemy < 252 else 'Paleozoic' if agemy < 541 else 'Precambrian') if agemy else age_from_text(row.get('depage'), 'Mesozoic')
        top = first_number(row.get('oredepth'))
        d = {'id': 'usgs-podchrome-2012:' + row['rec_id'], 'name': row['depname'], 'country': row['country'], 'lat': lat, 'lon': lon,
             'layer': 'Cr', 'ores': [{'code': 'chromite', 'share': 1.0}], 'shape': 'lens', 'mt': mt, 'grade': gr, 'unit': '%',
             'density': 4.0, 'topM': 0.0, 'baseM': None, 'lenKm': None, 'widKm': None, 'strikeDeg': None, 'dipDeg': None,
             'age': age, 'src': 'usgs-podchrome-2012', 'q': 'A' if mt >= 0.01 else 'B',
             'ref': (row.get('depform') or '').strip()[:60]}
        # pods: vertical extent grows with size (0.1 Mt ~ 60 m, 10 Mt ~ 300 m, 100 Mt ~ 600 m)
        d['baseM'] = float(round(min(800, max(20, 130 * (mt ** (1 / 3)))), 0))
        fill_dims(d, 4.0)
        d['est'] = sorted(set((d.get('est') or []) + ['baseM']))
        out.append(d)
    return out


def ring_area_km2(coords):
    lon = np.radians([c[0] for c in coords]); lat = np.radians([c[1] for c in coords])
    # spherical excess-free equal-area approximation (fine at district size)
    lat0 = np.mean(lat); x = lon * np.cos(lat0) * 6371.0; y = lat * 6371.0
    return abs(0.5 * np.sum(x[:-1] * y[1:] - x[1:] * y[:-1]))


def areas():
    data = json.loads((HERE / 'authored-areas.json').read_text(encoding='utf-8'))
    out = []
    for a in data['areas']:
        polys = [Polygon(p) for p in a['polygons']]
        for p in polys: assert p.is_valid, a['id']
        geom = polys[0] if len(polys) == 1 else MultiPolygon(polys)
        area_km2 = sum(ring_area_km2(p) for p in a['polygons'])
        beds = []
        for b in a['beds']:
            dens = b.get('density', 3.5)
            cover = b['mt'] * 1e6 / (dens * area_km2 * 1e6 * b['thickM'])
            assert cover <= 1.0, (a['id'], b, cover)
            beds.append({'code': b['code'], 'topM': b['topM'], 'thickM': b['thickM'], 'cover': r(cover, 4), 'grade': b.get('grade'),
                         'density': dens, 'mt': b['mt']})
        out.append({'id': 'iron-ferro:' + a['id'], 'name': a['name'], 'layer': a['layer'], 'age': a['age'], 'country': a['country'],
                    'src': 'published-endowment', 'q': 'C', 'unit': UNIT[a['layer']], 'areaKm2': r(area_km2, 0), 'ref': a['ref'],
                    'geometry': mapping(geom), 'beds': beds, 'mt': sum(b['mt'] for b in a['beds'])})
    return out


def body_layers(d):
    """0060 layer classes for a catalogue body (0065, README 'layers'); None = the runtime's default for its shape."""
    codes = {o['code'] for o in d['ores']}
    if d['shape'] == 'blanket':
        if d['layer'] == 'Ni': return ['laterite', 'saprolite']  # limonite cap over saprolite Ni ore
        if d['layer'] == 'Al': return ['laterite']
        if d['layer'] == 'Pt': return ['alluvial', 'outwash']  # placers
        if d['layer'] == 'Ti' or 'magnetite' in codes: return ['alluvial', 'cover']  # mineral sands and ironsands, beach and dune sand
        if d['layer'] == 'Fe': return ['laterite']  # iron caps and lateritic iron
        raise ValueError('no layers rule for blanket ' + d['id'])
    if 'earth-bauxite' in codes: return ['bed']  # buried karst bauxite in Paleozoic beds
    return None


PERIOD_ERA = {'Cambrian': 'Paleozoic', 'Ordovician': 'Paleozoic', 'Silurian': 'Paleozoic', 'Devonian': 'Paleozoic', 'Carboniferous': 'Paleozoic',
              'Permian': 'Paleozoic', 'Triassic': 'Mesozoic', 'Jurassic': 'Mesozoic', 'Cretaceous': 'Mesozoic', 'Paleogene': 'Cenozoic',
              'Neogene': 'Cenozoic', 'Quaternary': 'Cenozoic'}


def main():
    deps = authored_deposits()
    authored_cr = [(d['lat'], d['lon']) for d in deps if d['layer'] == 'Cr']
    deps += laterites()
    deps += podchrome(authored_cr)
    ids = [d['id'] for d in deps]; assert len(ids) == len(set(ids)), 'duplicate ids'
    for d in deps:
        lay = body_layers(d)
        if lay: d['layers'] = lay
    for d in deps:  # 0060 ages are eras; the period, where a source gives one, stays in 'period'
        if d['age'] in PERIOD_ERA: d['period'], d['age'] = d['age'], PERIOD_ERA[d['age']]
        assert d['age'] in ('Cenozoic', 'Mesozoic', 'Paleozoic', 'Precambrian'), (d['id'], d['age'])
    for d in deps:
        assert abs(sum(o['share'] for o in d['ores']) - 1) < 1e-6, d['id']
        assert d['mt'] > 0 and -90 <= d['lat'] <= 90 and -180 <= d['lon'] <= 180, d['id']
        assert d['baseM'] > d['topM'], d['id']
        for k in ['lat', 'lon']: d[k] = r(d[k], 4)
        for k in ['grade', 'topM', 'baseM', 'density', 'strikeDeg', 'dipDeg']:
            if d.get(k) is not None: d[k] = r(d[k], 4)
        d['mt'] = float(f"{d['mt']:.4g}")  # significant figures: pods under 50 t must not round to 0 (coordinator, integration)
    ars = areas()
    for a in ars:
        a['geometry'] = json.loads(json.dumps(a['geometry']))
    cat = {'version': 1, 'group': 'iron-ferro', 'sources': SOURCES, 'gradeBands': GRADE_BANDS, 'deposits': deps, 'areas': ars}
    OUT.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(cat, separators=(',', ':'), ensure_ascii=False)
    OUT.write_text(text + '\n', encoding='utf-8')
    by = {}
    for d in deps: by[d['layer']] = by.get(d['layer'], 0) + 1
    print('deposits', len(deps), by, 'areas', len(ars), 'bytes', len(text))


if __name__ == '__main__':
    main()
