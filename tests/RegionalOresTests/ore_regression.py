"""Shared helpers for the ore heat-map regression checks (famous deposits + regional coverage metrics).

Grid convention (same as RegionalOreCatalog.At): x=floor(((lon+180) mod 360)*2), y=floor((90-lat)*2).
'Any' = max(observed, potential, deep) / 255. Land and ice masks come from preview/grids.npz (the earthdata bake);
continents and the United States are coarse hand-drawn polygons that are adequate for 0.5-degree statistics.
"""
from pathlib import Path
import base64, gzip, json, math
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SHIPPED = ROOT / 'assets/earth/config/ore-heatmaps-v1.json'
BASELINE_V1 = ROOT / 'docs/research/ore-heatmaps-v1/baseline-v1/ore-heatmaps-v1.json'
W, H = 720, 360
METALS = ['Cu', 'Sn', 'Fe', 'Au', 'Ag', 'Pb', 'Zn', 'Ni', 'Cr', 'Mn', 'W', 'U', 'Hg', 'Mo', 'Sb', 'Bi', 'Ti', 'Pt', 'V']


def load_grid(path=SHIPPED):
    data = json.loads(Path(path).read_text(encoding='utf8'))
    assert data['width'] == W and data['height'] == H
    grids = {}
    for layer in data['layers']:
        raw = gzip.decompress(base64.b64decode(layer['data']))
        assert len(raw) == W * H * 5
        planes = np.frombuffer(raw[:3 * W * H], dtype=np.uint8).reshape(3, H, W)
        depth = np.frombuffer(raw[3 * W * H:], dtype='<u2').reshape(H, W)
        grids[layer['code']] = (planes, depth)
    return data, grids


def cell(lat, lon):
    return min(H - 1, max(0, int(math.floor((90 - lat) * 2)))), int(math.floor(((lon + 180) % 360) * 2)) % W


def value(grids, code, lat, lon, ring=0):
    """Max 'Any' weight (0..1) at the containing cell (ring=0) or within a (2*ring+1)^2 neighbourhood."""
    planes = grids[code][0]
    y, x = cell(lat, lon)
    ys = slice(max(0, y - ring), min(H, y + ring + 1))
    xs = [(x + i) % W for i in range(-ring, ring + 1)]
    return float(planes[:, ys][:, :, xs].max()) / 255


# ------------------------------------------------------------------------------------------------ deposits
# (name, lat, lon, code, minimum Any within a 3x3 neighbourhood, note). Coordinates are well-known public
# locations of the district/mine (0.05-0.1 degree precision); the 3x3 ring tolerates source coordinate slop.
MAJOR = 0.5
FAMOUS = [
    ('Chuquicamata', -22.29, -68.90, 'Cu', MAJOR), ('Escondida', -24.27, -69.07, 'Cu', MAJOR),
    ('Grasberg', -4.05, 137.11, 'Cu', MAJOR), ('Grasberg Au', -4.05, 137.11, 'Au', MAJOR),
    ('Bingham Canyon', 40.52, -112.15, 'Cu', MAJOR), ('Olympic Dam', -30.44, 136.88, 'Cu', MAJOR),
    ('Olympic Dam U', -30.44, 136.88, 'U', MAJOR), ('Norilsk', 69.35, 88.20, 'Ni', MAJOR),
    ('Norilsk PGE', 69.35, 88.20, 'Pt', MAJOR), ('Sudbury', 46.49, -81.00, 'Ni', MAJOR),
    ('Bushveld Rustenburg', -25.67, 27.25, 'Pt', MAJOR), ('Bushveld Steelpoort', -24.70, 30.20, 'Cr', MAJOR),
    ('Great Dyke', -19.50, 30.00, 'Cr', MAJOR), ('Kiruna', 67.85, 20.22, 'Fe', MAJOR),
    ('Pilbara Mt Whaleback', -23.36, 119.67, 'Fe', MAJOR), ('Carajas', -6.06, -50.18, 'Fe', MAJOR),
    ('Witwatersrand', -26.20, 27.90, 'Au', MAJOR), ('Kalgoorlie', -30.78, 121.50, 'Au', MAJOR),
    ('Muruntau', 41.50, 64.57, 'Au', MAJOR), ('Broken Hill', -31.95, 141.47, 'Pb', MAJOR),
    ('Red Dog', 68.07, -162.87, 'Zn', MAJOR), ('Mount Isa', -20.72, 139.48, 'Pb', MAJOR),
    ('Potosi Cerro Rico', -19.62, -65.75, 'Ag', MAJOR), ('Cornwall', 50.22, -5.25, 'Sn', MAJOR),
    ('Bangka', -2.10, 106.10, 'Sn', MAJOR), ('Gejiu', 23.36, 103.15, 'Sn', MAJOR),
    ('Kolwezi (Katanga)', -10.72, 25.47, 'Cu', MAJOR), ('Chingola (Zambian Copperbelt)', -12.53, 27.85, 'Cu', MAJOR),
    ('Lubin (Kupferschiefer)', 51.40, 16.20, 'Cu', MAJOR), ('Mansfeld (Kupferschiefer)', 51.53, 11.45, 'Cu', MAJOR),
    ('Almaden', 38.77, -4.84, 'Hg', MAJOR), ('Freiberg (Erzgebirge)', 50.92, 13.34, 'Ag', MAJOR),
    ('Altenberg (Erzgebirge)', 50.76, 13.76, 'Sn', MAJOR), ('Jachymov (Erzgebirge)', 50.37, 12.91, 'U', MAJOR),
    ('Falun', 60.60, 15.63, 'Cu', MAJOR), ('Rio Tinto', 37.70, -6.59, 'Cu', MAJOR),
    ('Saskatchewan potash (Esterhazy)', 50.65, -102.07, 'K', MAJOR), ('Saskatoon potash belt', 52.10, -106.60, 'K', MAJOR),
    ('Saskatoon halite (sylvite parent)', 52.10, -106.60, 'NaCl', MAJOR),
    ('Stassfurt (Zechstein) K', 51.85, 11.58, 'K', MAJOR), ('Stassfurt (Zechstein) NaCl', 51.85, 11.58, 'NaCl', MAJOR),
    ('Gorleben salt dome NaCl', 53.03, 11.35, 'NaCl', 0.3), ('Gorleben salt dome K', 53.03, 11.35, 'K', 0.3),
    ('Upper Silesia coal', 50.26, 19.02, 'C_coal', MAJOR), ('Ruhr coal', 51.45, 7.00, 'C_coal', MAJOR),
    ('Appalachian coal (WV)', 38.35, -81.63, 'C_coal', MAJOR), ('Powder River coal', 44.29, -105.50, 'C_coal', MAJOR),
    ('Kuzbass coal', 54.50, 86.50, 'C_coal', MAJOR), ('Donbas coal', 48.00, 37.80, 'C_coal', MAJOR),
    ('Karaganda coal', 49.80, 73.10, 'C_coal', MAJOR), ('Pechora coal (Vorkuta)', 67.50, 64.00, 'C_coal', MAJOR),
    ('Bowen Basin coal', -22.00, 148.04, 'C_coal', MAJOR),
    ('Kimberley', -28.74, 24.76, 'diamond', MAJOR), ('Mirny (Mir pipe)', 62.53, 113.99, 'diamond', MAJOR),
    ('Muzo emeralds', 5.53, -74.10, 'emerald', MAJOR), ('Sar-e-Sang lapis', 36.20, 70.79, 'lapis', MAJOR),
    ('Guinea bauxite (Sangaredi)', 11.11, -13.83, 'Al', MAJOR), ('Jamaica bauxite', 18.10, -77.30, 'Al', MAJOR),
    ('Weipa bauxite', -12.67, 141.87, 'Al', MAJOR), ('Khouribga phosphate', 32.88, -6.91, 'P', MAJOR),
    ('Atacama nitrates (Maria Elena)', -22.35, -69.67, 'N', MAJOR), ('Kirka borates', 39.29, 30.53, 'B', MAJOR),
    ('Bigadic borates', 39.39, 28.13, 'B', MAJOR), ('Las Cuevas fluorite (MX)', 22.10, -100.60, 'F', MAJOR),
    ('Dayu tungsten (Jiangxi)', 25.40, 114.35, 'W', MAJOR), ('Panasqueira', 40.15, -7.75, 'W', MAJOR),
    ('Climax', 39.37, -106.18, 'Mo', MAJOR), ('McArthur River U (Athabasca)', 57.76, -105.05, 'U', MAJOR),
    ('Kivu tin belt (DRC, corrected Major Deposits record)', -2.60, 27.50, 'Sn', MAJOR),
    ('Jinchuan Ni-Cu', 38.50, 102.20, 'Cu', MAJOR), ('Pomalaa Ni laterite', -4.18, 121.65, 'Ni', MAJOR),
    ('Tarnobrzeg native sulfur', 50.57, 21.68, 'S', MAJOR),
    ('East Kalimantan coal', 0.50, 117.60, 'C_coal', MAJOR), ('Quang Ninh coal (VN)', 21.00, 107.30, 'C_coal', MAJOR),
    ('Hwange coal (ZW)', -18.37, 26.50, 'C_coal', MAJOR), ('Zonguldak coal (TR)', 41.45, 31.80, 'C_coal', MAJOR),
    ('Asturias coal (ES)', 43.30, -5.70, 'C_coal', MAJOR), ('Witbank coal (ZA)', -25.87, 29.23, 'C_coal', MAJOR),
    ('Vasyugan mire (PEATMAP)', 57.50, 79.00, 'peat', MAJOR), ('Irish midland bogs (peat)', 53.30, -7.00, 'peat', 0.3),
    ('Central Kalimantan peat (PEATMAP)', -2.50, 113.50, 'peat', MAJOR),
]

# Representative interior points of assessment tracts that v1 dropped as invalid or antimeridian-crossing
# (computed with tools/bake_ore_heatmaps.polygon_parts on the source polygons). Checked on the POTENTIAL plane.
REPAIRED_TRACTS = [
    ('pCu tract Gangdese (was invalid)', 31.258, 82.238, 'Cu'),
    ('pCu tract Outer Melanesian Arc II (antimeridian)', -17.794, 177.984, 'Cu'),
    ('pCu tract Alaska Peninsula and Aleutians (antimeridian)', 57.213, -156.966, 'Cu'),
    ('pCu tract Chukotka (antimeridian)', 68.307, 166.888, 'Cu'),
    ('pCu tract Pacific Margin (antimeridian)', 61.569, 144.412, 'Cu'),
    ('sedCu tract Hercynian-Thuringian / Kupferschiefer (was invalid)', 51.142, 11.342, 'Cu'),
    ('Potash tract Zechstein halokinetic (was invalid)', 53.052, 17.689, 'K'),
    ('Potash tract Qom-Great Kavir (was invalid)', 35.498, 51.621, 'K'),
    ('Potash tract Red Sea (was invalid)', 21.122, 37.019, 'K'),
]

# Extra deposits from the 2026-10-02 review (spot2.py); reported in the evaluation table, not asserted.
EXTRA = [
    ('San Rafael Sn (PE)', -14.23, -70.32, 'Sn'), ('Pitinga Sn (BR)', -0.75, -60.08, 'Sn'), ('Bisie Sn (DRC)', -1.07, 27.62, 'Sn'),
    ('Renison Sn (AU)', -41.79, 145.44, 'Sn'), ('Dachang Sn (CN)', 24.83, 107.60, 'Sn'), ('Rondonia Sn (BR)', -9.90, -63.00, 'Sn'),
    ('Jos Plateau Sn (NG)', 9.90, 8.90, 'Sn'), ('Neves-Corvo Cu', 37.57, -7.97, 'Cu'), ('Kidd Creek Cu', 48.69, -81.37, 'Cu'),
    ('Aitik Cu', 67.07, 20.95, 'Cu'), ('Udokan Cu', 56.58, 118.40, 'Cu'), ('Kamoa-Kakula Cu', -10.80, 25.30, 'Cu'),
    ('Dzhezkazgan Cu', 47.80, 67.70, 'Cu'), ('Almalyk Cu', 40.80, 69.60, 'Cu'), ('Bor Cu (RS)', 44.08, 22.10, 'Cu'),
    ('Aynak Cu (AF)', 34.27, 69.30, 'Cu'), ('Dexing Cu (CN)', 28.98, 117.72, 'Cu'), ('Obuasi Au (GH)', 6.20, -1.67, 'Au'),
    ('Sadiola Au (ML)', 13.90, -11.70, 'Au'), ('Geita Au (TZ)', -2.87, 32.22, 'Au'), ('Kibali Au (DRC)', 3.12, 29.60, 'Au'),
    ('Sukhoi Log Au', 58.60, 115.15, 'Au'), ('Olimpiada Au', 59.98, 92.90, 'Au'), ('Natalka Au', 61.85, 148.10, 'Au'),
    ('Kumtor Au', 41.87, 78.19, 'Au'), ('Yanacocha Au', -6.98, -78.50, 'Au'), ('Lihir Au', -3.12, 152.64, 'Au'),
    ('Voiseys Bay Ni', 56.33, -62.10, 'Ni'), ('Jinchuan Ni', 38.50, 102.20, 'Ni'), ('New Caledonia Ni', -21.50, 165.50, 'Ni'),
    ('Sorowako Ni', -2.53, 121.36, 'Ni'), ('Simandou Fe', 8.60, -8.90, 'Fe'), ('KMA Kursk Fe', 51.30, 37.00, 'Fe'),
    ('Kryvyi Rih Fe', 47.90, 33.40, 'Fe'), ('Quadrilatero Ferrifero Fe', -20.20, -43.80, 'Fe'), ('Mesabi Fe', 47.50, -92.50, 'Fe'),
    ('Wanshan Hg (CN)', 27.50, 109.20, 'Hg'), ('Moanda Mn (GA)', -1.55, 13.27, 'Mn'), ('Sukinda Cr (IN)', 21.00, 85.80, 'Cr'),
    ('Rossing U (NA)', -22.48, 15.05, 'U'), ('Arlit U (NE)', 18.75, 7.35, 'U'), ('Ranger U (AU)', -12.68, 132.91, 'U'),
    ('Cerrejon coal (CO)', 11.10, -72.60, 'C_coal'), ('Ostrava coal (CZ)', 49.83, 18.30, 'C_coal'),
    ('Hudson Bay Lowlands peat', 53.00, -85.00, 'peat'), ('Finland peat (Ostrobothnia)', 64.00, 25.50, 'peat'),
    ('Belarus Polesie peat', 52.00, 27.50, 'peat'), ('Riga gypsum (Skaistkalne)', 56.38, 24.65, 'gypsum'),
]

# The five v2 gaps (no on-disk survey dataset). Since dataset v3 they are filled by the authored best-guess layer
# (docs/research/remaining-ores-2026-09-29/corrections/authored/; general knowledge, not survey data) and ASSERTED
# present (>= 0.5 within one cell). 'better source' documents the survey data that should eventually replace the guess.
FORMER_GAPS = [
    ('Tavan Tolgoi coal (MN)', 43.60, 105.50, 'C_coal', MAJOR, 'USGS Asia GIS compilation (ScienceBase, Cloudflare-blocked for scripted download) or a national Mongolian deposit register'),
    ('Moatize coal (MZ)', -16.10, 33.75, 'C_coal', MAJOR, 'USGS Compilation of GIS data for the mineral industries of Africa (doi:10.5066/P97EQWXP; coal occurrence areas)'),
    ('Nord-Pas-de-Calais coal (FR)', 50.45, 2.80, 'C_coal', MAJOR, 'BRGM historical coalfield outlines (closed 1990, so absent from facility lists)'),
    ('Inkai U (Chu-Sarysu, KZ)', 45.33, 67.40, 'U', MAJOR, 'USGS Asia/Eurasia GIS compilations (in-situ-leach U mines); IAEA UDEPO is excluded by policy'),
    ('Sicily native sulfur', 37.50, 14.00, 'S', MAJOR, 'Historic sulfur mines are not in MRDS or 2006-2010 facility lists; needs an Italian (ISPRA) abandoned-mine inventory if openly licensed'),
]

# Best-guess layer spot checks for the near-empty gem layers and strengthened v2-weak deposits (reported, not asserted).
BEST_GUESS_SPOTS = [
    ('Mogok ruby (MM)', 22.92, 96.50, 'corundum'), ('Ratnapura spinel (LK)', 6.68, 80.40, 'spinel'), ('Ilakaka sapphire (MG)', -22.68, 45.21, 'corundum'),
    ('Ouro Preto topaz (BR)', -20.42, -43.58, 'topaz'), ('Murzinka topaz (RU)', 57.67, 61.00, 'topaz'), ('Shigar aquamarine (PK)', 35.45, 75.65, 'beryl'),
    ('Alto Ligonha tourmaline (MZ)', -15.50, 38.30, 'tourmaline'), ('Merelani tsavorite (TZ)', -3.58, 36.98, 'garnet'), ('Swat emerald (PK)', 34.78, 72.36, 'emerald'),
    ('Aheim olivine (NO)', 62.03, 5.53, 'olivine'), ('Tolfa alunite (IT)', 42.15, 11.93, 'alum'), ('Flor de los Andes lapis (CL)', -30.30, -70.45, 'lapis'),
    ('Kibali Au (DRC)', 3.12, 29.60, 'Au'), ('Cigar Lake U (CA)', 58.07, -104.53, 'U'), ('Schlema U (DE)', 50.60, 12.67, 'U'),
    ('Freiberg Pb (DE)', 50.92, 13.34, 'Pb'), ('Ekibastuz coal (KZ)', 51.73, 75.32, 'C_coal'), ('Jharia coal (IN)', 23.75, 86.42, 'C_coal'),
]

# Places still without deposit-strength evidence; kept visible so that adding a dataset is measurable (reported, not asserted).
KNOWN_GAPS = [
    ('Zabargad peridot (EG)', 23.61, 36.20, 'olivine', 'St John\'s Island is absent from Natural Earth 1:10m, so the authored entry fails the in-country check; needs a reviewed coordinate exception'),
]

REGIONS = {  # name: (lat0, lat1, lon0, lon1)
    'Latvia': (55.7, 58.1, 21.0, 28.2),
}


# ------------------------------------------------------------------------------------------------ masks
def _poly_mask(polys):
    from shapely.geometry import Polygon
    import shapely
    ys, xs = np.mgrid[0:H, 0:W]
    lat = 89.75 - ys / 2; lon = xs / 2 - 179.75
    m = np.zeros((H, W), bool)
    for p in polys:
        g = Polygon(p); shapely.prepare(g)
        m |= shapely.contains_xy(g, lon.ravel(), lat.ravel()).reshape(H, W)
    return m


US_POLYS = [
    [(-124.8, 48.4), (-123.3, 49.0), (-95.2, 49.0), (-89.6, 48.0), (-84.8, 46.5), (-82.4, 45.3), (-82.5, 41.7), (-79.0, 43.3),
     (-76.4, 44.2), (-74.7, 45.0), (-71.5, 45.0), (-70.0, 46.7), (-69.2, 47.45), (-67.8, 47.07), (-66.9, 44.8), (-70.0, 41.0),
     (-75.5, 35.0), (-80.0, 32.0), (-80.0, 25.0), (-82.0, 24.5), (-83.0, 29.0), (-89.0, 29.0), (-97.2, 25.9), (-99.5, 27.5),
     (-101.4, 29.8), (-103.0, 29.0), (-104.7, 29.8), (-106.5, 31.8), (-108.2, 31.3), (-111.1, 31.3), (-114.8, 32.5), (-117.1, 32.5),
     (-120.5, 34.5), (-124.5, 40.0), (-124.8, 48.4)],
    [(-141.0, 60.3), (-141.0, 69.7), (-156.0, 71.5), (-168.0, 69.0), (-166.0, 65.5), (-168.0, 65.6), (-162.0, 60.0), (-168.0, 54.0),
     (-179.9, 51.5), (-179.9, 53.0), (-160.0, 55.5), (-152.0, 57.0), (-142.0, 59.5), (-136.0, 58.5), (-130.0, 55.0), (-130.0, 56.0),
     (-135.0, 59.5), (-141.0, 60.3)],
]


def masks():
    """Ice-free land, continents and the United States on the 0.5-degree grid."""
    with np.load(ROOT / 'preview/grids.npz') as p:
        flags = p['flags']
    def block(a):
        return a.reshape(H, 15, W, 15).mean(axis=(1, 3))
    land = block(((flags & 1) == 0).astype(np.float32)) >= .5
    ice = block(((flags & 8) > 0).astype(np.float32)) >= .5
    free = land & ~ice
    ys, xs = np.mgrid[0:H, 0:W]
    lat = 89.75 - ys / 2; lon = xs / 2 - 179.75
    antarctica = lat < -60
    americas = (lon < -30) & (lon > -170) | ((lon <= -170) & (lat > 50))
    south_am = americas & (lat < 12.6) & (lon > -81.5) & ~((lat > 7.2) & (lon < -77.2))
    north_am = americas & ~south_am & ~antarctica
    red_sea = 32.5 + (30 - lat) * (43.3 - 32.5) / (30 - 12.5)
    africa = (~americas) & (lat < 37.5) & (lat > -36) & (lon > -20) & (lon < 52) & (
        ((lat <= 12.5)) | ((lat > 12.5) & (lat <= 30) & (lon < red_sea)) | ((lat > 30) & (lon < 32.5)))
    oceania = (~americas) & (((lat < -10) & (lon > 110)) | ((lat < 0) & (lon > 130)) | ((lat < 30) & ((lon > 160) | (lon < -150))))
    europe = (~americas) & ~africa & (lon > -25) & (lat >= 36) & (
        ((lon < 26) & (lat >= 36)) | ((lon < 60) & (lat >= 50)) | ((lon < 45) & (lat >= 44)) | ((lon < 29) & (lat >= 40.5)))
    asia = (~americas) & ~africa & ~oceania & ~europe & ~antarctica
    us = _poly_mask(US_POLYS)
    return {'free': free, 'land': land, 'us': us & land, 'continents': {
        'North America': north_am & free, 'South America': south_am & free, 'Europe': europe & free,
        'Africa': africa & free, 'Asia': asia & free, 'Oceania': oceania & free}}


def metrics(grids, m):
    """Coverage and saturation statistics used by the regression thresholds and the report."""
    free = m['free']; us = m['us'] & free; row = free & ~m['us']
    obs_any = np.zeros((H, W), bool); any_heat = np.zeros((H, W), bool); lit_count = np.zeros((H, W), np.int32)
    sat_us = []; sat_row = []; us_share = {}
    for c in METALS:
        planes = grids[c][0]
        o = (planes[0] > 0) | (planes[2] > 0); a = planes.max(0)
        obs_any |= o; any_heat |= a > 0; lit_count += o
        sat_us.append(float((a[us] >= 128).mean())); sat_row.append(float((a[row] >= 128).mean()))
        lit = (a > 0) & m['land']
        us_share[c] = round(float((lit & m['us']).sum() / max(1, lit.sum())), 3)
    out = {'continents': {}, 'usShareOfLitLandCells': us_share,
           'usMeanSaturation': round(float(np.mean(sat_us)), 4), 'restOfWorldMeanSaturation': round(float(np.mean(sat_row)), 4)}
    out['usToRestSaturationRatio'] = round(out['usMeanSaturation'] / max(1e-9, out['restOfWorldMeanSaturation']), 2)
    mass = [grids[c][0].max(0).astype(np.float64) for c in METALS]
    out['usHeatMassShare'] = round(float(sum(a[us].sum() for a in mass) / max(1.0, sum(a[free].sum() for a in mass))), 3)
    out['usIceFreeLandShare'] = round(float(us.sum() / free.sum()), 3)
    out['iceFreeLandObservedMetalShare'] = round(float(obs_any[free].mean()), 3)
    out['iceFreeLandAnyMetalHeatShare'] = round(float(any_heat[free].mean()), 3)
    out['usMeanObservedMetalLayers'] = round(float(lit_count[us].mean()), 2)
    out['restOfWorldMeanObservedMetalLayers'] = round(float(lit_count[row].mean()), 2)
    for name, mask in m['continents'].items():
        out['continents'][name] = {'cells': int(mask.sum()), 'observedMetalShare': round(float(obs_any[mask].mean()), 3),
                                   'anyMetalHeatShare': round(float(any_heat[mask].mean()), 3),
                                   'meanObservedMetalLayers': round(float(lit_count[mask].mean()), 2)}
    return out


def region_counts(grids, m, name, threshold=0.5):
    a0, a1, o0, o1 = REGIONS[name]
    ys = slice(int(math.floor((90 - a1) * 2)), int(math.floor((90 - a0) * 2)))
    xs = slice(int(math.floor((o0 + 180) * 2)), int(math.floor((o1 + 180) * 2)))
    land = m['land'][ys, xs]
    res = {}
    for code, (planes, _) in grids.items():
        a = planes.max(0)[ys, xs][land]
        if (a > 0).any():
            res[code] = {'lit': int((a > 0).sum()), 'atLeast': int((a >= threshold * 255).sum()), 'max': round(float(a.max()) / 255, 2)}
    return res


def classify(v):
    return 'present' if v >= .5 else 'weak' if v > 0 else 'missing'
