"""Shared paths, metal tables and helpers for the base-metals group (decision 0065)."""
from pathlib import Path
import csv, io, json, math, re, zipfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def _rawdata():
    for p in [ROOT, *ROOT.parents]:
        if (p / 'rawdata').is_dir():
            return p / 'rawdata'
    raise SystemExit('rawdata folder not found above ' + str(ROOT))


RAWDATA = _rawdata()
RAW = RAWDATA / 'ore' / 'base-metals'
ATLAS = ROOT / 'docs/research/remaining-ores-2026-09-29'
CUTIN = ROOT / 'docs/research/copper-tin-global-2026-09-29/normalized'
NATURAL_EARTH = ATLAS / 'raw/naturalearth/ne_10m_admin_0_countries.zip'
CATALOGUE = ROOT / 'assets/earth/config/ore-deposits-v1/base-metals.json'
EVIDENCE = HERE / 'evidence.geojson'
OVERRIDES = HERE / 'heatmap-overrides.json'
REPORT = HERE / 'build-report.json'

CODES = ['Cu', 'Sn', 'Pb', 'Zn', 'Ag', 'Bi', 'Hg', 'W', 'Mo', 'Sb']

# Grade bands (decision 0065 section 3). Tier = poor below medium. Units as the catalogue's grades.
GRADE_BANDS = {
    'Cu': {'unit': '%', 'medium': 0.4, 'rich': 1.2, 'bountiful': 3.0},
    'Zn': {'unit': '%', 'medium': 3.0, 'rich': 7.0, 'bountiful': 15.0},
    'Pb': {'unit': '%', 'medium': 2.0, 'rich': 5.0, 'bountiful': 10.0},
    'Ag': {'unit': 'g/t', 'medium': 60, 'rich': 200, 'bountiful': 800},
    'Sn': {'unit': '%', 'medium': 0.4, 'rich': 1.0, 'bountiful': 2.5},
    'W': {'unit': '%', 'basis': 'WO3', 'medium': 0.2, 'rich': 0.6, 'bountiful': 1.5},
    'Mo': {'unit': '%', 'medium': 0.04, 'rich': 0.12, 'bountiful': 0.3},
    'Sb': {'unit': '%', 'medium': 2.0, 'rich': 5.0, 'bountiful': 15.0},
    'Bi': {'unit': '%', 'medium': 0.1, 'rich': 0.3, 'bountiful': 1.0},
    'Hg': {'unit': '%', 'medium': 0.3, 'rich': 1.0, 'bountiful': 4.0},
    # Gold carried in base-metal bodies (porphyry Cu-Au, VMS, Ag-Au veins): the precious group's bands, so one gold
    # block means the same grade in both catalogues.
    'Au': {'unit': 'g/t', 'medium': 1.0, 'rich': 4.0, 'bountiful': 10.0},
    # Uranium of Olympic Dam (% U3O8), the precious group's U bands for the same reason.
    'U': {'unit': '%', 'medium': 0.1, 'rich': 0.5, 'bountiful': 2.0},
    # Co-products handed over by iron-ferro (gossan limonite, Berg Aukas vanadinite): its Fe and V bands.
    'Fe': {'unit': '%', 'basis': 'Fe', 'medium': 30, 'rich': 45, 'bountiful': 60},
    'V': {'unit': '%', 'basis': 'V2O5', 'medium': 0.3, 'rich': 0.8, 'bountiful': 1.4},
}
UNIT = {c: b['unit'] for c, b in GRADE_BANDS.items()}

# Rough long-run metal values (USD per tonne of metal; W per tonne WO3). Used only to share an ore body's blocks
# between its ores (a body's blocks follow the value each metal adds per tonne of ore), never for amounts. Mo is taken at
# about half its price: by-product molybdenite is recovered at roughly 50-60 % from porphyry copper ore.
PRICE = {'Cu': 8000, 'Zn': 2600, 'Pb': 2100, 'Sn': 25000, 'W': 30000, 'Mo': 20000, 'Sb': 10000, 'Bi': 8000,
         'Hg': 30000, 'Ag': 800000, 'Au': 60000000, 'U': 130000}  # U per tonne U3O8 (about 60 USD/lb)

# Game ore code of each metal's primary ore, and its oxidised (supergene) ores for the top zone.
PRIMARY = {'Cu': 'chalcopyrite', 'Zn': 'sphalerite', 'Pb': 'galena', 'Ag': 'silver', 'Sn': 'cassiterite',
           'W': 'wolframite', 'Mo': 'wulfenite', 'Sb': 'tetrahedrite', 'Bi': 'bismuthinite', 'Hg': 'cinnabar'}
OXIDE = {'Cu': [('malachite', .75), ('azurite', .25)], 'Pb': [('cerussite', 1.0)],
         'Zn': [('smithsonite', .6), ('hemimorphite', .4)]}
# Metal whose grade sets an ore's tier (tetrahedrite: Sb where the body has Sb, else Ag).
ORE_METAL = {'chalcopyrite': 'Cu', 'malachite': 'Cu', 'azurite': 'Cu', 'nativecopper': 'Cu', 'sphalerite': 'Zn',
             'smithsonite': 'Zn', 'hemimorphite': 'Zn', 'galena': 'Pb', 'cerussite': 'Pb', 'silver': 'Ag',
             'cassiterite': 'Sn', 'earth-placertin': 'Sn', 'wolframite': 'W', 'wulfenite': 'Mo',
             'tetrahedrite': 'Sb|Ag', 'bismuthinite': 'Bi', 'cinnabar': 'Hg', 'gold': 'Au', 'uranium': 'U',
             'limonite': 'Fe', 'vanadinite': 'V'}
ALLOWED_ORES = set(ORE_METAL)


def value(metal, grade):
    """USD per tonne of ore that one metal adds at this grade."""
    if grade is None or grade <= 0 or metal not in PRICE: return 0.0
    return grade * PRICE[metal] / (1e6 if UNIT.get(metal) == 'g/t' else 100.0)


def tier(metal, grade):
    b = GRADE_BANDS.get(metal)
    if not b or grade is None: return None
    return 'bountiful' if grade >= b['bountiful'] else 'rich' if grade >= b['rich'] else 'medium' if grade >= b['medium'] else 'poor'


def make_ores(grades, oxide=0.0, min_share=0.03, exclude=('Au',)):
    """Ore shares from the value each metal adds; `oxide` is the share of each oxidisable metal's part in the top zone."""
    vals = {m: value(m, g) for m, g in grades.items() if m in PRIMARY and m not in exclude}
    vals = {m: v for m, v in vals.items() if v > 0}
    tot = sum(vals.values())
    if not tot: return []
    shares = {m: v / tot for m, v in vals.items()}
    shares = {m: s for m, s in shares.items() if s >= min_share}
    tot = sum(shares.values())
    out = []
    for m, s in sorted(shares.items(), key=lambda kv: -kv[1]):
        s /= tot
        ox = OXIDE.get(m) if oxide > 0 else None
        if ox and s * oxide >= .01:
            out.append({'code': PRIMARY[m], 'share': s * (1 - oxide)})
            for code, part in ox:
                if s * oxide * part >= .005: out.append({'code': code, 'share': s * oxide * part, 'zone': 'top'})
        else:
            out.append({'code': PRIMARY[m], 'share': s})
    tot = sum(o['share'] for o in out)
    for o in out: o['share'] = round(o['share'] / tot, 3)
    drift = round(1 - sum(o['share'] for o in out), 3)
    out[0]['share'] = round(out[0]['share'] + drift, 3)
    return out


def main_metal(grades, allowed=CODES):
    best = max(((value(m, g), m) for m, g in grades.items() if m in allowed and g), default=(0, None))
    return best[1]


AGE_WORDS = [
    ('Cenozoic', r'quaternary|pleistocene|holocene|neogene|paleogene|palaeogene|tertiary|miocene|pliocene|eocene|oligocene|paleocene|cenozoic'),
    ('Cretaceous', r'cretaceous'), ('Jurassic', r'jurassic'), ('Triassic', r'triassic'),
    ('Permian', r'permian'), ('Carboniferous', r'carboniferous|mississippian|pennsylvanian'), ('Devonian', r'devonian'),
    ('Silurian', r'silurian'), ('Ordovician', r'ordovician'), ('Cambrian', r'cambrian'),
    ('Mesozoic', r'mesozoic'), ('Paleozoic', r'paleozoic|palaeozoic'),
    ('Precambrian', r'proterozoic|archean|archaean|precambrian|vendian|ediacaran|neoproterozoic|riphean|sinian'),
]


def age_from_text(text):
    t = (text or '').lower()
    if 'precambrian' in t or ('proterozoic' in t and 'cambrian' not in t.replace('precambrian', '')): pass
    hits = []
    for name, rx in AGE_WORDS:
        m = re.search(rx, t)
        if m: hits.append((m.start(), name))
    if not hits: return None
    hits.sort()
    name = hits[0][1]
    # 'Neoproterozoic-Cambrian' style ranges start with the older word; 'Cambrian' inside 'Precambrian' is handled by order
    if name == 'Cambrian' and re.search(r'precambrian', t) and not re.search(r'(?<!pre)cambrian', t): return 'Precambrian'
    return name


def age_from_ma(ma):
    if ma is None or not math.isfinite(ma) or ma < 0: return None
    if ma < 66: return 'Cenozoic'
    if ma < 145: return 'Cretaceous'
    if ma < 201: return 'Jurassic'
    if ma < 252: return 'Triassic'
    if ma < 299: return 'Permian'
    if ma < 359: return 'Carboniferous'
    if ma < 419: return 'Devonian'
    if ma < 444: return 'Silurian'
    if ma < 485: return 'Ordovician'
    if ma < 541: return 'Cambrian'
    return 'Precambrian'


ERA = {'Cenozoic': 'Cenozoic', 'Cretaceous': 'Mesozoic', 'Jurassic': 'Mesozoic', 'Triassic': 'Mesozoic', 'Mesozoic': 'Mesozoic',
       'Permian': 'Paleozoic', 'Carboniferous': 'Paleozoic', 'Devonian': 'Paleozoic', 'Silurian': 'Paleozoic',
       'Ordovician': 'Paleozoic', 'Cambrian': 'Paleozoic', 'Paleozoic': 'Paleozoic', 'Precambrian': 'Precambrian'}
ERA_MA = {'Cenozoic': (0, 66), 'Mesozoic': (66, 252), 'Paleozoic': (252, 541), 'Precambrian': (541, 4600)}


def num(x):
    try:
        v = float(str(x).replace(',', '').strip())
        return v if math.isfinite(v) else None
    except (TypeError, ValueError):
        return None


def read_zip_csv(zpath, member_suffix, encodings=('utf-8', 'cp1252', 'latin-1')):
    zf = zipfile.ZipFile(zpath)
    name = [x for x in zf.namelist() if x.endswith(member_suffix)][0]
    b = zf.read(name)
    for enc in encodings:
        try:
            return list(csv.DictReader(io.StringIO(b.decode(enc))))
        except UnicodeDecodeError:
            continue
    raise ValueError(zpath)


def haversine_km(lat1, lon1, lat2, lon2):
    p = math.pi / 180
    a = math.sin((lat2 - lat1) * p / 2) ** 2 + math.cos(lat1 * p) * math.cos(lat2 * p) * math.sin((lon2 - lon1) * p / 2) ** 2
    return 12742 * math.asin(math.sqrt(min(1, a)))


def ellipse(lat, lon, a_km, b_km, strike_deg=0, n=24):
    """Ellipse polygon ring (lon, lat) around a point: a along strike, b across, in km."""
    pts = []
    k = 111.32
    c = math.cos(math.radians(lat))
    s = math.radians(strike_deg)
    for i in range(n):
        t = 2 * math.pi * i / n
        x, y = a_km * math.cos(t), b_km * math.sin(t)  # x along strike
        north = x * math.cos(s) - y * math.sin(s)
        east = x * math.sin(s) + y * math.cos(s)
        pts.append((round(lon + east / (k * c), 4), round(lat + north / k, 4)))
    pts.append(pts[0])
    return pts
