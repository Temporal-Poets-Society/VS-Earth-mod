"""Spot checks: well-known deposits vs the catalogue (nearest bodies of the expected layer(s), name matches)."""
import csv, json, math, re, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))

HERE = Path(__file__).resolve().parent
rows = list(csv.DictReader(open(HERE / 'catalogue-flat.csv', encoding='utf8')))

# name, reference lat, lon (orebody / pit / district centre from general knowledge, approx), layers, name regex
CHECKS = [
    ('Chuquicamata', -22.29, -68.90, ['Cu'], r'chuquicamata'),
    ('Escondida', -24.27, -69.07, ['Cu'], r'escondida'),
    ('El Teniente', -34.09, -70.35, ['Cu'], r'teniente'),
    ('Bingham Canyon', 40.52, -112.15, ['Cu'], r'bingham'),
    ('Grasberg-Ertsberg', -4.06, 137.11, ['Cu', 'Au'], r'grasberg|ertsberg'),
    ('Olympic Dam', -30.44, 136.88, ['Cu', 'U', 'Au'], r'olympic'),
    ('Oyu Tolgoi', 43.0, 106.85, ['Cu'], r'oyu'),
    ('Kolwezi / Katanga Copperbelt', -10.72, 25.47, ['Cu'], r'kolwezi|kamoto|tenke|kov'),
    ('Zambian Copperbelt (Nchanga)', -12.52, 27.85, ['Cu'], r'nchanga|konkola|mufulira|nkana'),
    ('Kupferschiefer Lubin', 51.45, 16.15, ['Cu'], r'lubin|kupferschiefer|polkowice|rudna|mansfeld'),
    ('Udokan', 56.6, 118.45, ['Cu'], r'udokan'),
    ('Norilsk-Talnakh', 69.45, 88.3, ['Ni', 'Cu', 'Pt'], r'noril|talnakh|oktyabr'),
    ('Sudbury', 46.6, -81.0, ['Ni', 'Cu'], r'sudbury'),
    ('Bushveld Merensky/UG2', -25.0, 27.5, ['Pt', 'Cr'], r'bushveld|merensky'),
    ('Great Dyke', -18.5, 30.3, ['Pt', 'Cr'], r'great dyke'),
    ('Kempirsai', 50.2, 57.9, ['Cr'], r'kempirsai|donskoy|khromtau'),
    ('Kiruna', 67.835, 20.195, ['Fe'], r'kiruna|kiirunavaara'),
    ('Carajas N4/S11D', -6.2, -50.2, ['Fe'], r'caraj'),
    ('Quadrilatero Ferrifero', -20.2, -43.8, ['Fe'], r'quadril|itabira'),
    ('Hamersley', -22.7, 118.0, ['Fe'], r'hamersley'),
    ('Mesabi Range', 47.5, -92.6, ['Fe'], r'mesabi|biwabik'),
    ('Kryvyi Rih', 47.9, 33.4, ['Fe'], r'kryvyi|krivoy|kryvbas'),
    ('KMA (Kursk)', 51.2, 37.5, ['Fe'], r'kursk|kma|lebedin|mikhailov|stoilen'),
    ('Lorraine minette', 49.3, 6.0, ['Fe'], r'lorraine|minette'),
    ('Simandou', 8.6, -8.9, ['Fe'], r'simandou'),
    ('Witwatersrand', -26.2, 27.9, ['Au'], r'witwatersrand|rand'),
    ('Muruntau', 41.52, 64.57, ['Au'], r'muruntau'),
    ('Kalgoorlie Golden Mile', -30.77, 121.50, ['Au'], r'kalgoorlie|golden mile|fimiston'),
    ('Carlin trend', 40.85, -116.3, ['Au'], r'carlin|goldstrike|gold quarry'),
    ('Obuasi', 6.2, -1.67, ['Au'], r'obuasi|ashanti'),
    ('Sukhoi Log', 58.6, 115.2, ['Au'], r'sukhoi|lena|bodaibo'),
    ('Kolar', 12.95, 78.27, ['Au'], r'kolar'),
    ('Las Medulas', 42.47, -6.77, ['Au'], r'medulas'),
    ('Broken Hill NSW', -31.96, 141.46, ['Pb', 'Zn', 'Ag'], r'broken hill'),
    ('Red Dog', 68.07, -162.85, ['Zn', 'Pb'], r'red dog'),
    ('Mount Isa', -20.73, 139.48, ['Pb', 'Zn', 'Cu', 'Ag'], r'mount isa|mt isa'),
    ('Rammelsberg', 51.89, 10.42, ['Zn', 'Pb', 'Cu', 'Ag'], r'rammelsberg'),
    ('Falun', 60.60, 15.62, ['Cu', 'Zn'], r'falun'),
    ('Rio Tinto (Iberian Pyrite Belt)', 37.70, -6.58, ['Cu', 'Zn'], r'rio tinto|riotinto'),
    ('Potosi Cerro Rico', -19.62, -65.75, ['Ag', 'Sn'], r'potos|cerro rico'),
    ('Llallagua-Catavi', -18.42, -66.58, ['Sn'], r'llallagua|catavi|siglo'),
    ('Huanuni', -18.28, -66.83, ['Sn'], r'huanuni'),
    ('Cornwall Camborne-Redruth', 50.22, -5.25, ['Sn'], r'cornwall|camborne|redruth|crofty|dolcoath'),
    ('Bangka', -2.1, 106.1, ['Sn'], r'bangka'),
    ('Belitung', -2.85, 107.9, ['Sn'], r'belitung|billiton'),
    ('Kinta Valley', 4.6, 101.1, ['Sn'], r'kinta'),
    ('Gejiu', 23.36, 103.15, ['Sn'], r'gejiu'),
    ('Erzgebirge Sn (Altenberg/Cinovec)', 50.73, 13.77, ['Sn'], r'altenberg|cinovec|zinnwald|ehrenfried'),
    ('Panasqueira', 40.155, -7.755, ['W'], r'panasqueira'),
    ('Almaden', 38.775, -4.84, ['Hg'], r'almad'),
    ('Idrija', 46.0, 14.03, ['Hg'], r'idrij'),
    ('Huancavelica', -12.79, -74.98, ['Hg'], r'huancavelica|santa barbara'),
    ('Kimberley', -28.738, 24.758, ['diamond'], r'kimberley'),
    ('Mir pipe', 62.53, 113.99, ['diamond'], r'\bmir\b'),
    ('Udachnaya', 66.43, 112.32, ['diamond'], r'udachn'),
    ('Jwaneng', -24.53, 24.70, ['diamond'], r'jwaneng'),
    ('Orapa', -21.31, 25.37, ['diamond'], r'orapa'),
    ('Catoca', -9.41, 20.30, ['diamond'], r'catoca'),
    ('Argyle', -16.72, 128.39, ['diamond'], r'argyle'),
    ('Golconda / Kollur', 16.7, 80.0, ['diamond'], r'golconda|kollur'),
    ('Cigar Lake / McArthur River', 58.07, -104.5, ['U'], r'cigar|mcarthur'),
    ('Inkai / Chu-Sarysu', 45.3, 67.7, ['U'], r'inkai|chu-sarysu|budenov'),
    ('Ruhr coalfield', 51.5, 7.2, ['C_coal'], r'ruhr'),
    ('Upper Silesia coal', 50.2, 19.0, ['C_coal'], r'silesia|slask|ostrava'),
    ('Donbas', 48.0, 38.0, ['C_coal'], r'donbas|donets'),
    ('Kuzbass', 54.0, 87.0, ['C_coal'], r'kuzbass|kuznetsk'),
    ('Pechora', 67.5, 64.0, ['C_coal'], r'pechora|vorkuta'),
    ('Karaganda', 49.8, 73.1, ['C_coal'], r'karaganda'),
    ('Shanxi / Ordos', 37.8, 112.5, ['C_coal'], r'shanxi|ordos|datong'),
    ('Bowen Basin', -22.5, 148.5, ['C_coal'], r'bowen'),
    ('Sydney Basin', -33.0, 151.0, ['C_coal'], r'sydney|hunter'),
    ('Witbank / Highveld', -26.0, 29.2, ['C_coal'], r'witbank|highveld|emalahleni'),
    ('Powder River Basin', 44.5, -105.5, ['C_coal'], r'powder river'),
    ('Appalachian basin', 39.0, -81.0, ['C_coal'], r'appalach|pittsburgh'),
    ('Damodar (Jharia/Raniganj)', 23.75, 86.4, ['C_coal'], r'jharia|raniganj|damodar'),
    ('Kalimantan coal', -1.0, 116.5, ['C_coal'], r'kalimantan|kutai'),
    ('Zechstein', 52.5, 11.0, ['K', 'NaCl'], r'zechstein'),
    ('Stassfurt', 51.86, 11.58, ['K', 'NaCl'], r'stassfurt|stassfurt|staßfurt'),
    ('Wieliczka-Bochnia', 49.98, 20.06, ['NaCl'], r'wieliczka|bochnia'),
    ('Hallstatt', 47.56, 13.64, ['NaCl'], r'hallstatt|salzkammergut'),
    ('Khewra (Salt Range)', 32.65, 73.0, ['NaCl'], r'khewra|salt range'),
    ('Saskatchewan potash (Prairie Evaporite)', 52.0, -106.0, ['K'], r'saskatchewan|prairie|elk point'),
    ('Upper Kama (Berezniki-Solikamsk)', 59.4, 56.8, ['K'], r'kama|berezniki|solikamsk'),
    ('Starobin (Pripyat)', 52.8, 27.5, ['K'], r'starobin|pripyat|soligorsk'),
    ('Dead Sea', 31.2, 35.4, ['K', 'NaCl'], r'dead sea'),
    ('Chilean nitrates', -21.5, -69.6, ['N'], r'nitrate|caliche|tarapac|atacama'),
    ('Sicily sulfur', 37.45, 14.05, ['S'], r'sicil|caltanissetta'),
    ('Tarnobrzeg sulfur', 50.55, 21.7, ['S'], r'tarnobrzeg'),
    ('Khouribga (Ouled Abdoun)', 32.9, -6.9, ['P'], r'khouribga|abdoun'),
    ('Bou Craa', 26.3, -12.85, ['P'], r'bou craa|boucraa'),
    ('Phosphoria / Florida', 27.8, -81.9, ['P'], r'florida|bone valley|phosphoria'),
    ('Weipa', -12.65, 141.9, ['Al'], r'weipa'),
    ('Boke / Sangaredi', 11.08, -13.79, ['Al'], r'bok|sangar'),
    ('Kalahari Mn', -27.2, 22.95, ['Mn'], r'kalahari'),
    ('Nikopol Mn', 47.6, 34.4, ['Mn'], r'nikopol|marganets|pokrov'),
    ('Chiatura Mn', 42.29, 43.28, ['Mn'], r'chiatura'),
    ('Moanda Mn', -1.55, 13.2, ['Mn'], r'moanda|bangombe'),
    ('Groote Eylandt Mn', -13.97, 136.45, ['Mn'], r'groote'),
    ('Mogok', 22.92, 96.5, ['corundum', 'spinel'], r'mogok'),
    ('Muzo / Chivor', 5.53, -74.1, ['emerald'], r'muzo|chivor|coscuez'),
    ('Sar-e-Sang', 36.2, 70.8, ['lapis'], r'sar-e-sang|sar-i-sang|badakh'),
    ('Bayan Obo', 41.8, 109.97, ['Fe'], r'bayan'),
    ('Bisie / Kivu tin', -1.0, 28.0, ['Sn'], r'bisie|kivu|maniema'),
    ('San Rafael (Peru tin)', -14.23, -70.32, ['Sn'], r'san rafael'),
    ('Rondonia tin', -10.0, -63.0, ['Sn'], r'rond|bom futuro|ariquemes|pitinga'),
    ('Khaidarkan Hg-Sb', 39.95, 71.35, ['Hg', 'Sb'], r'khaidarkan|haidarkan'),
    ('Xikuangshan Sb', 27.75, 111.45, ['Sb'], r'xikuang'),
    ('Freiberg', 50.92, 13.35, ['Ag', 'Pb', 'Zn'], r'freiberg'),
    ('Kutna Hora', 49.95, 15.27, ['Ag', 'Zn', 'Pb'], r'kutn'),
    ('Cerro de Pasco', -10.68, -76.26, ['Zn', 'Pb', 'Ag', 'Cu'], r'pasco'),
    ('Navan (Tara)', 53.66, -6.72, ['Zn', 'Pb'], r'navan|tara'),
    ('Sullivan', 49.71, -116.0, ['Zn', 'Pb'], r'sullivan'),
    ('Rampura Agucha', 25.83, 74.73, ['Zn', 'Pb'], r'rampura|agucha'),
]

def hav(a, b, c, d):
    p = math.pi / 180
    x = math.sin((c - a) * p / 2) ** 2 + math.cos(a * p) * math.cos(c * p) * math.sin((d - b) * p / 2) ** 2
    return 2 * 6371 * math.asin(math.sqrt(x))

out = []
for name, lat, lon, layers, rx in CHECKS:
    rgx = re.compile(rx, re.I)
    named = [r for r in rows if rgx.search(r['name']) and (r['layer'] in layers or True)]
    near = sorted(((hav(lat, lon, float(r['lat']), float(r['lon'])), r) for r in rows if r['layer'] in layers), key=lambda t: t[0])[:3]
    print('==', name, f'({lat}, {lon})', layers)
    for r in named[:6]:
        dkm = hav(lat, lon, float(r['lat']), float(r['lon']))
        print(f"   NAME {r['layer']:7} {r['kind']:7} {r['name'][:60]:60} {r['lat']},{r['lon']} d={dkm:.1f}km mt={r['mt']} g={r['grade']}{r['unit'] or ''} top={r['topM']} base={r['baseM']} len={r['lenKm']} wid={r['widKm']} area={r['areaKm2']} shape={r['shape']} q={r['q']} est={r['est']} src={r['src']}")
    for d, r in near:
        if any(r is n for n in named): continue
        print(f"   NEAR {r['layer']:7} {r['kind']:7} {r['name'][:60]:60} {r['lat']},{r['lon']} d={d:.1f}km mt={r['mt']} g={r['grade']}{r['unit'] or ''} q={r['q']} src={r['src']}")
    best = min([hav(lat, lon, float(r['lat']), float(r['lon'])) for r in named if r['layer'] in layers] + [near[0][0] if near else 9e9])
    out.append({'check': name, 'lat': lat, 'lon': lon, 'layers': '/'.join(layers), 'named_hits': len([r for r in named if r['layer'] in layers]),
                'nearest_same_layer_km': round(near[0][0], 1) if near else '', 'nearest_name': near[0][1]['name'] if near else ''})
with open(HERE / 'spotcheck-raw.csv', 'w', newline='', encoding='utf8') as f:
    w = csv.DictWriter(f, fieldnames=list(out[0])); w.writeheader(); w.writerows(out)
