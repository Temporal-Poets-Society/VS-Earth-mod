"""Presence check for ~200 well-known deposits/basins: same-layer body within R km (area polygons: within R km of the
polygon) or a name match. Reference coordinates are approximate (general knowledge), so R is generous (30 km; 60 km
for basins/belts)."""
import csv, json, math, re, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import shapely
from shapely.geometry import shape, Point
from common import load_catalogue

HERE = Path(__file__).resolve().parent
cats = load_catalogue()
bodies = []
for g, d in cats.items():
    for x in d['deposits']:
        bodies.append((x['layer'], x['name'], Point(x['lon'], x['lat']), x))
    for a in d.get('areas', []):
        geom = shape(a['geometry'])
        if not geom.is_valid: geom = shapely.make_valid(geom)
        bodies.append((a['layer'], a['name'], geom, a))
# co-product presence: base-metals grades
def layers_of(x):
    s = {x['layer']}
    s.update((x.get('grades') or {}).keys())
    for o in x.get('ores', []):
        c = o['code']
        if c in ('chalcopyrite', 'malachite', 'azurite', 'nativecopper', 'chalcocite'): s.add('Cu')
        if c == 'gold': s.add('Au')
        if c in ('sphalerite', 'smithsonite', 'hemimorphite'): s.add('Zn')
        if c in ('galena', 'cerussite'): s.add('Pb')
        if c == 'silver': s.add('Ag')
        if c in ('cassiterite', 'earth-placertin'): s.add('Sn')
        if c == 'uranium': s.add('U')
        if c == 'pentlandite': s.add('Ni')
        if c in ('sperrylite', 'earth-placerplatinum'): s.add('Pt')
    for b in x.get('beds', []) or []:
        if b['code'] in ('bituminouscoal', 'lignite', 'anthracite'): s.add('C_coal')
        if b['code'] == 'sylvite': s.add('K')
        if b['code'] == 'halite': s.add('NaCl')
    return s
B = [(layers_of(x), name, geom, x) for lay, name, geom, x in bodies]

KM = 111.195
def dist_km(geom, lat, lon):
    p = Point(lon, lat)
    d = geom.distance(p)  # degrees, crude
    return d * KM * max(.2, math.cos(math.radians(lat))) if d > 0 else 0.0

# (label, lat, lon, layer, radius_km, regex)
L = [
 # copper
 ('Collahuasi', -20.98, -68.64, 'Cu', 30, 'collahuasi|rosario|ujina'), ('Los Pelambres', -31.72, -70.49, 'Cu', 30, 'pelambres'),
 ('Cerro Verde', -16.53, -71.59, 'Cu', 30, 'cerro verde'), ('Toquepala', -17.25, -70.61, 'Cu', 30, 'toquepala'),
 ('Cuajone', -17.04, -70.70, 'Cu', 30, 'cuajone'), ('Antamina', -9.54, -77.05, 'Cu', 30, 'antamina'),
 ('Las Bambas', -14.07, -72.30, 'Cu', 30, 'bambas|ferrobamba|chalcobamba'), ('Morenci', 33.08, -109.36, 'Cu', 30, 'morenci'),
 ('Resolution', 33.30, -111.10, 'Cu', 30, 'resolution'), ('Pebble', 59.90, -155.30, 'Cu', 30, 'pebble'),
 ('Kamoa-Kakula', -10.80, 25.25, 'Cu', 40, 'kamoa|kakula'), ('Cobre Panama', 8.83, -80.65, 'Cu', 30, 'petaquilla|botija|colina|cobre panam'),
 ('Cananea', 30.98, -110.30, 'Cu', 30, 'cananea'), ('Aktogay', 46.95, 79.95, 'Cu', 30, 'aktogay'),
 ('Almalyk Kalmakyr', 40.85, 69.62, 'Cu', 30, 'kalmakyr|almalyk|dalnee'), ('Dexing', 28.98, 117.72, 'Cu', 30, 'dexing|tongchang'),
 ('Yulong', 31.40, 97.73, 'Cu', 30, 'yulong'), ('Reko Diq', 29.10, 62.05, 'Cu', 30, 'reko'),
 ('Sar Cheshmeh', 29.95, 55.87, 'Cu', 30, 'sar cheshmeh|sarcheshmeh'), ('Bor-Majdanpek', 44.07, 22.10, 'Cu', 40, 'bor|majdanpek'),
 ('Batu Hijau', -8.97, 116.87, 'Cu', 30, 'batu hijau'), ('Mount Isa Cu', -20.73, 139.48, 'Cu', 15, 'mount isa'),
 ('Butte', 46.01, -112.53, 'Cu', 30, 'butte'), ('Panguna', -6.32, 155.50, 'Cu', 30, 'panguna'),
 ('Kennecott (Alaska)', 61.50, -142.88, 'Cu', 30, 'kennecott|bonanza'), ('Keweenaw native copper', 47.20, -88.50, 'Cu', 60, 'keweenaw|calumet|quincy|white pine'),
 ('Dzhezkazgan', 47.80, 67.70, 'Cu', 40, 'dzhezkazgan|zhezkazgan|jezkazgan'), ('Troodos/Cyprus', 35.00, 33.00, 'Cu', 60, 'skouriotissa|mavrovouni|kalavasos|cyprus'),
 ('Aitik', 67.07, 20.95, 'Cu', 30, 'aitik'), ('Palabora', -23.98, 31.13, 'Cu', 30, 'palabora|phalaborwa'),
 ('Kounrad', 46.98, 75.00, 'Cu', 30, 'kounrad|konyrat'), ('Erdenet', 49.03, 104.07, 'Cu', 30, 'erdenet'),
 ('Tampakan', 6.48, 125.04, 'Cu', 30, 'tampakan'), ('Salobo/Sossego (Carajas Cu)', -5.80, -50.55, 'Cu', 60, 'salobo|sossego|cristalino|igarap'),
 ('Spence/Sierra Gorda', -22.80, -69.30, 'Cu', 40, 'spence|sierra gorda'), ('Radomiro Tomic', -22.18, -68.90, 'Cu', 12, 'radomiro|tomic'),
 ('Ok Tedi', -5.20, 141.13, 'Cu', 30, 'ok tedi'), ('Frieda River', -4.70, 141.75, 'Cu', 40, 'frieda'),
 ('Bystrinskoe', 51.48, 118.57, 'Cu', 30, 'bystrin'), ('Peschanka/Baimskaya', 66.75, 164.60, 'Cu', 60, 'peschan|baim'),
 ('Mufulira etc (Zambia)', -12.55, 28.24, 'Cu', 30, 'mufulira'), ('Kipushi', -11.77, 27.24, 'Zn', 30, 'kipushi'),
 ('Mansfeld', 51.55, 11.50, 'Cu', 40, 'mansfeld|sangerhausen'), ('Falun', 60.60, 15.62, 'Cu', 20, 'falun'),
 ('Outokumpu', 62.72, 29.02, 'Cu', 30, 'outokumpu'), ('Kidd Creek', 48.69, -81.37, 'Zn', 30, 'kidd'),
 ('Brunswick No. 12', 47.47, -65.81, 'Zn', 30, 'brunswick'), ('Flin Flon', 54.77, -101.88, 'Cu', 30, 'flin flon'),
 ('Neves-Corvo', 37.57, -7.97, 'Cu', 30, 'neves'), ('Tharsis', 37.60, -7.12, 'Cu', 30, 'tharsis|esperanza'),
 ('Mount Lyell', -42.07, 145.58, 'Cu', 30, 'lyell'), ('Gai/Urals VMS', 51.48, 58.43, 'Cu', 40, 'gai'),
 # gold
 ('Lihir', -3.12, 152.64, 'Au', 30, 'lihir|ladolam'), ('Porgera', -5.47, 143.10, 'Au', 30, 'porgera'),
 ('Pueblo Viejo', 18.93, -70.17, 'Au', 30, 'pueblo viejo'), ('Yanacocha', -6.98, -78.52, 'Au', 30, 'yanacocha'),
 ('Natalka', 61.95, 148.95, 'Au', 40, 'natalka'), ('Olimpiada', 59.90, 92.90, 'Au', 40, 'olimpiad'),
 ('Kibali', 2.95, 29.60, 'Au', 40, 'kibali|kilo|moto'), ('Geita', -2.87, 32.20, 'Au', 30, 'geita'),
 ('Boddington', -32.75, 116.37, 'Au', 30, 'boddington'), ('Cadia', -33.45, 148.98, 'Au', 30, 'cadia'),
 ('Telfer', -21.71, 122.21, 'Au', 30, 'telfer'), ('Homestake', 44.35, -103.75, 'Au', 30, 'homestake|lead'),
 ('Hemlo', 48.70, -85.90, 'Au', 30, 'hemlo'), ('Porcupine (Timmins)', 48.47, -81.33, 'Au', 40, 'porcupine|timmins|hollinger|dome'),
 ('Kirkland Lake', 48.15, -80.03, 'Au', 30, 'kirkland'), ('Red Lake', 51.05, -93.80, 'Au', 30, 'red lake'),
 ('Cortez', 40.18, -116.60, 'Au', 40, 'cortez|pipeline'), ('Mother Lode', 38.00, -120.50, 'Au', 80, 'mother lode|sierra nevada|grass valley|tertiary'),
 ('Klondike', 63.90, -139.10, 'Au', 40, 'klondike'), ('Ballarat', -37.56, 143.85, 'Au', 30, 'ballarat'),
 ('Bendigo', -36.76, 144.28, 'Au', 30, 'bendigo'), ('Tarkwa', 5.30, -1.99, 'Au', 30, 'tarkwa'),
 ('Sadiola/Loulo (Mali)', 13.90, -11.40, 'Au', 60, 'sadiola|loulo|yalea|gounkoto'), ('Sukari', 24.95, 34.70, 'Au', 30, 'sukari'),
 ('Jiaodong (Linglong-Jiaojia)', 37.30, 120.40, 'Au', 60, 'jiaodong|linglong|jiaojia|zhaoyuan'), ('Morro Velho', -19.98, -43.85, 'Au', 30, 'morro velho|nova lima'),
 ('Serra Pelada', -5.95, -49.65, 'Au', 30, 'serra pelada'), ('Barberton', -25.78, 31.05, 'Au', 30, 'barberton|sheba'),
 ('Berezovsk', 56.90, 60.80, 'Au', 30, 'berez'), ('Fort Knox', 65.00, -147.36, 'Au', 30, 'fort knox|fairbanks'),
 ('Juneau (A-J/Treadwell)', 58.30, -134.40, 'Au', 30, 'juneau|treadwell|alaska-juneau'), ('Nome placers', 64.50, -165.40, 'Au', 40, 'nome'),
 ('Waihi', -37.39, 175.84, 'Au', 30, 'waihi|martha'), ('Otago placers', -45.50, 169.50, 'Au', 80, 'otago'),
 ('Hishikari', 31.98, 130.68, 'Au', 30, 'hishikari'), ('Kumtor', 41.865, 78.19, 'Au', 30, 'kumtor'),
 ('Zarmitan/Kochbulak', 39.80, 66.90, 'Au', 60, 'zarmitan|kochbulak|charmitan'), ('Kuranakh/Aldan', 58.60, 125.40, 'Au', 60, 'kuranakh|aldan'),
 ('Vatukoula', -17.50, 177.85, 'Au', 30, 'vatukoula|emperor'), ('Ashanti Ahafo', 7.00, -2.33, 'Au', 30, 'ahafo'),
 ('Kalimantan Kelian', 0.15, 115.35, 'Au', 30, 'kelian'), ('Wits (any)', -26.20, 27.90, 'Au', 30, 'witwatersrand'),
 ('Siberian Kolyma placers', 62.50, 150.00, 'Au', 150, 'kolyma|susuman'), ('Minas Gerais placers (Ouro Preto)', -20.38, -43.50, 'Au', 40, 'ouro preto|mariana'),
 ('Akan (Gold Coast)', 6.0, -1.8, 'Au', 30, 'akan'), ('Bambuk/Bure (W. Africa medieval)', 12.5, -11.0, 'Au', 120, 'bambuk|bure|siguiri|falem'),
 # lead-zinc-silver
 ('Tri-State', 37.00, -94.60, 'Zn', 40, 'tri-state|joplin|picher'), ('Viburnum Trend / Old Lead Belt', 37.60, -91.10, 'Pb', 60, 'viburnum|lead belt|buick|brushy'),
 ('Coeur d\'Alene', 47.50, -115.90, 'Ag', 40, 'coeur|silver belt|bunker'), ('Leadville', 39.25, -106.30, 'Pb', 30, 'leadville'),
 ('Santa Eulalia', 28.60, -105.90, 'Pb', 30, 'santa eulalia'), ('Naica', 27.85, -105.50, 'Pb', 30, 'naica'),
 ('Penasquito', 24.65, -101.70, 'Zn', 30, 'penasquito|peñasquito'), ('San Cristobal (Bolivia)', -21.10, -67.20, 'Zn', 30, 'san crist'),
 ('Zinkgruvan', 58.82, 15.10, 'Zn', 30, 'zinkgruvan'), ('Garpenberg', 60.32, 16.20, 'Zn', 30, 'garpenberg'),
 ('McArthur River HYC', -16.43, 136.10, 'Zn', 30, 'mcarthur|hyc'), ('Century', -18.72, 138.60, 'Zn', 30, 'century'),
 ('Rosebery', -41.77, 145.53, 'Zn', 30, 'rosebery'), ('Zawar', 24.35, 73.70, 'Zn', 30, 'zawar'),
 ('Jinding', 26.42, 99.42, 'Zn', 30, 'jinding'), ('Fankou', 25.05, 113.65, 'Zn', 30, 'fankou'),
 ('Huize', 26.60, 103.30, 'Zn', 40, 'huize'), ('Ozernoye', 52.40, 111.60, 'Zn', 40, 'ozern'),
 ('Kholodninskoye', 56.00, 111.50, 'Zn', 60, 'kholodnin'), ('Ridder-Sokolny', 50.35, 83.50, 'Zn', 30, 'ridder|sokol|leninogorsk'),
 ('Bleiberg', 46.63, 13.68, 'Pb', 30, 'bleiberg'), ('Reocin', 43.35, -4.10, 'Zn', 30, 'reocin|reocín'),
 ('Mezica', 46.52, 14.85, 'Pb', 30, 'mezica|mežica'), ('Trepca', 42.95, 20.90, 'Pb', 40, 'trep'),
 ('Laisvall', 66.07, 17.15, 'Pb', 30, 'laisvall'), ('Pine Point', 60.80, -114.50, 'Zn', 40, 'pine point'),
 ('Polaris', 75.38, -96.95, 'Zn', 30, 'polaris'), ('Faro/Anvil', 62.35, -133.40, 'Zn', 30, 'faro|anvil'),
 ('Howards Pass', 62.47, -129.20, 'Zn', 40, 'howard'), ('Gamsberg', -29.20, 18.95, 'Zn', 30, 'gamsberg'),
 ('Skorpion', -27.83, 16.60, 'Zn', 30, 'skorpion'), ('Tsumeb', -19.25, 17.71, 'Cu', 30, 'tsumeb'),
 ('Kabwe', -14.44, 28.45, 'Zn', 30, 'kabwe|broken hill'), ('Touissit-Bou Beker', 34.48, -1.77, 'Pb', 30, 'touissit|bou beker'),
 ('Laurion', 37.72, 24.05, 'Pb', 30, 'laur|lavrion'), ('Iglesiente (Sardinia)', 39.30, 8.53, 'Zn', 40, 'iglesi|monteponi|sardin'),
 ('Silvermines/Lisheen/Galmoy', 52.75, -7.90, 'Zn', 60, 'silvermines|lisheen|galmoy'), ('North Pennines orefield', 54.75, -2.20, 'Pb', 40, 'penni|alston|weardale'),
 ('Derbyshire Peak orefield', 53.20, -1.70, 'Pb', 40, 'derby|peak'), ('Mendip', 51.27, -2.65, 'Pb', 40, 'mendip'),
 ('Banska Stiavnica', 48.45, 18.90, 'Ag', 30, 'stiavnica|štiavnica|schemnitz'), ('Kremnica', 48.70, 18.92, 'Au', 30, 'kremnica'),
 ('Iwami Ginzan', 35.11, 132.44, 'Ag', 30, 'iwami'), ('Guanajuato', 21.02, -101.26, 'Ag', 30, 'guanajuato|veta madre'),
 ('Comstock', 39.31, -119.65, 'Ag', 30, 'comstock'), ('Taxco', 18.56, -99.60, 'Ag', 30, 'taxco'),
 ('Schwaz', 47.35, 11.71, 'Ag', 30, 'schwaz|falkenstein'), ('Sala', 59.92, 16.60, 'Ag', 30, 'sala'),
 ('Kongsberg', 59.67, 9.62, 'Ag', 30, 'kongsberg'), ('Jachymov', 50.37, 12.92, 'U', 30, 'jachym|joachim'),
 ('Zeravshan/Kansai (Ag-Pb)', 40.5, 69.6, 'Pb', 80, 'kansai|altyn-topkan|kurgashin'), ('Dukat', 62.53, 155.38, 'Ag', 40, 'dukat'),
 ('Cannington', -21.87, 140.92, 'Ag', 30, 'cannington'), ('Imiter', 31.38, -5.80, 'Ag', 30, 'imiter'),
 # tin, tungsten
 ('Renison', -41.80, 145.43, 'Sn', 30, 'renison'), ('Mount Bischoff', -41.42, 145.53, 'Sn', 30, 'bischoff'),
 ('Greenbushes', -33.86, 116.06, 'Sn', 30, 'greenbushes'), ('Dachang', 24.85, 107.60, 'Sn', 30, 'dachang'),
 ('Phuket', 7.90, 98.35, 'Sn', 40, 'phuket'), ('Jos Plateau', 9.60, 8.90, 'Sn', 60, 'jos'),
 ('Uis (Namibia)', -21.20, 14.87, 'Sn', 30, 'uis'), ('Deputatskoye', 69.30, 139.90, 'Sn', 60, 'deputat'),
 ('Solnechnoye/Komsomolsk', 50.70, 136.60, 'Sn', 60, 'solnech|komsomol'), ('Kavalerovo', 44.30, 135.10, 'Sn', 60, 'kavaler|khrustal'),
 ('Iultin', 67.85, -178.75, 'Sn', 60, 'iultin'), ('Dartmoor/Devon', 50.55, -4.00, 'Sn', 40, 'dartmoor|devon|tavistock'),
 ('Mawchi', 18.82, 97.17, 'W', 40, 'mawchi'), ('Shizhuyuan', 25.75, 113.12, 'W', 30, 'shizhuyuan'),
 ('Sangdong', 37.15, 128.85, 'W', 30, 'sangdong'), ('Tyrnyauz', 43.40, 42.92, 'W', 30, 'tyrnyauz'),
 ('Climax', 39.37, -106.18, 'Mo', 30, 'climax'), ('Henderson', 39.77, -105.85, 'Mo', 30, 'henderson'),
 ('Questa', 36.70, -105.55, 'Mo', 30, 'questa'), ('Jinduicheng', 34.33, 109.90, 'Mo', 40, 'jinduicheng'),
 # Ni, Cr, PGE, Mn, Ti, V
 ('Voisey\'s Bay', 56.33, -62.10, 'Ni', 30, 'voisey'), ('Thompson', 55.73, -97.86, 'Ni', 30, 'thompson'),
 ('Kambalda', -31.20, 121.67, 'Ni', 30, 'kambalda'), ('Jinchuan', 38.50, 102.20, 'Ni', 30, 'jinchuan'),
 ('Pechenga', 69.40, 30.80, 'Ni', 40, 'pechenga|nikel|zapolyar'), ('Monchegorsk', 67.93, 32.90, 'Ni', 40, 'monche'),
 ('New Caledonia laterites', -21.50, 165.50, 'Ni', 150, 'caledon|goro|koniambo|thio'), ('Sorowako', -2.53, 121.36, 'Ni', 40, 'sorowako|soroako'),
 ('Stillwater', 45.40, -109.90, 'Pt', 40, 'stillwater'), ('Kemi', 65.78, 24.55, 'Cr', 30, 'kemi'),
 ('Sukinda', 21.00, 85.75, 'Cr', 40, 'sukinda'), ('Guleman', 38.60, 39.70, 'Cr', 40, 'guleman'),
 ('Panzhihua', 26.60, 101.70, 'Ti', 40, 'panzhihua'), ('Tellnes', 58.33, 6.42, 'Ti', 30, 'tellnes'),
 ('Lac Tio (Allard Lake)', 50.55, -63.45, 'Ti', 30, 'tio|allard'), ('Richards Bay sands', -28.70, 32.10, 'Ti', 60, 'richards bay|zulti'),
 ('Kachkanar', 58.70, 59.50, 'V', 40, 'kachkanar'), ('Moanda', -1.55, 13.20, 'Mn', 30, 'moanda'),
 # iron
 ('Labrador Trough (Schefferville)', 54.80, -66.80, 'Fe', 60, 'schefferville|labrador|knob lake'), ('Labrador City/Wabush', 52.95, -66.90, 'Fe', 40, 'wabush|carol|labrador city'),
 ('Mont-Wright/Fermont', 52.77, -67.35, 'Fe', 40, 'mont-wright|fermont|mont wright'), ('Kostomuksha', 64.60, 30.60, 'Fe', 30, 'kostomuksh'),
 ('Kachkanar Fe', 58.70, 59.50, 'Fe', 40, 'kachkanar'), ('Magnitogorsk', 53.40, 59.05, 'Fe', 30, 'magnit'),
 ('Sishen', -27.70, 23.00, 'Fe', 40, 'sishen|kolomela|postmasburg'), ('Kediet ej Jill (Zouerate)', 22.70, -12.60, 'Fe', 40, 'kediet|zouerat|tazadit|guelb'),
 ('Gara Djebilet', 26.75, -7.40, 'Fe', 40, 'djebilet'), ('Bailadila', 18.65, 81.25, 'Fe', 40, 'bailadila'),
 ('Bellary-Hospet', 15.10, 76.60, 'Fe', 60, 'bellary|hospet|sandur|donimalai'), ('Singhbhum (Noamundi)', 22.15, 85.50, 'Fe', 60, 'noamundi|singhbhum|joda|barbil|bonai'),
 ('Goa', 15.40, 74.10, 'Fe', 40, 'goa'), ('Panzhihua Fe', 26.60, 101.70, 'Fe', 40, 'panzhihua'),
 ('Sokolov-Sarbai', 52.95, 63.10, 'Fe', 40, 'sokolov|sarbai|kachar'), ('Lisakovsk', 52.55, 62.50, 'Fe', 40, 'lisakovsk'),
 ('Gol-e-Gohar', 29.10, 55.30, 'Fe', 40, 'gohar'), ('Chador Malu', 32.35, 55.50, 'Fe', 40, 'chador|chadormalu'),
 ('Divrigi', 39.40, 38.10, 'Fe', 40, 'divri'), ('Erzberg', 47.52, 14.90, 'Fe', 30, 'erzberg'),
 ('Bilbao (Vizcaya)', 43.25, -3.00, 'Fe', 40, 'bilbao|vizcaya|somorrostro'), ('Cleveland ironstone', 54.55, -1.10, 'Fe', 40, 'cleveland'),
 ('Salzgitter', 52.10, 10.40, 'Fe', 30, 'salzgitter'), ('Grangesberg (Bergslagen)', 60.08, 14.98, 'Fe', 40, 'grangesberg|grängesberg|bergslag'),
 ('Sydvaranger', 69.70, 30.00, 'Fe', 40, 'sydvaranger|kirkenes|bjornevatn'), ('Red Mountain (Birmingham AL)', 33.50, -86.80, 'Fe', 40, 'birmingham|red mountain|clinton'),
 ('Iron Mountain/Pilot Knob MO', 37.60, -90.65, 'Fe', 40, 'iron mountain|pilot knob|pea ridge'), ('Marcona', -15.20, -75.10, 'Fe', 30, 'marcona'),
 ('Cerro Bolivar', 7.50, -63.40, 'Fe', 40, 'bolivar|san isidro|el pao'), ('El Romeral / Chilean IOA', -29.70, -71.25, 'Fe', 60, 'romeral|algarrobo|los colorados'),
 ('Iron Knob (Middleback)', -32.73, 137.15, 'Fe', 40, 'iron knob|middleback|iron duke'), ('Savage River', -41.50, 145.20, 'Fe', 30, 'savage'),
 ('Itakpe', 7.60, 6.30, 'Fe', 40, 'itakpe'), ('Chongqing/Hainan Shilu', 19.25, 109.05, 'Fe', 40, 'shilu'),
 # coal
 ('Illinois basin', 38.50, -88.50, 'C_coal', 60, 'illinois'), ('Western Interior', 38.50, -94.50, 'C_coal', 80, 'interior|cherokee'),
 ('Gulf Coast lignite', 31.50, -96.00, 'C_coal', 100, 'gulf|texas lignite|wilcox'), ('Elk Valley (BC)', 49.80, -114.80, 'C_coal', 60, 'elk valley|crowsnest|kootenay'),
 ('Alberta plains coal', 53.00, -114.00, 'C_coal', 100, 'alberta|ardley|horseshoe'), ('Cerrejon', 11.10, -72.60, 'C_coal', 40, 'cerrej'),
 ('Rio Grande do Sul coal', -30.50, -52.50, 'C_coal', 100, 'grande do sul|candiota|parana'), ('Moatize', -16.10, 33.70, 'C_coal', 40, 'moatize|tete'),
 ('Hwange', -18.37, 26.50, 'C_coal', 40, 'hwange|wankie'), ('Tavan Tolgoi', 43.60, 105.50, 'C_coal', 40, 'tavan|tolgoi|gobi'),
 ('Zonguldak', 41.45, 31.80, 'C_coal', 40, 'zonguldak'), ('Asturias', 43.25, -5.70, 'C_coal', 40, 'asturia'),
 ('Saar', 49.30, 7.00, 'C_coal', 30, 'saar'), ('Campine/Limburg', 51.00, 5.50, 'C_coal', 40, 'campine|kempen|limburg'),
 ('Nord-Pas-de-Calais', 50.45, 2.80, 'C_coal', 30, 'nord|calais'), ('Yorkshire coalfield', 53.60, -1.30, 'C_coal', 40, 'yorkshire|nottingham'),
 ('South Wales coalfield', 51.70, -3.50, 'C_coal', 30, 'wales'), ('Lusatia lignite', 51.50, 14.30, 'C_coal', 40, 'lusati|lausitz'),
 ('Central German lignite', 51.20, 12.30, 'C_coal', 40, 'leipzig|central german|mitteldeutsch|halle'), ('North Bohemian lignite', 50.50, 13.50, 'C_coal', 40, 'bohemia|most'),
 ('Maritsa East', 42.15, 25.90, 'C_coal', 40, 'maritsa'), ('Kosovo lignite', 42.65, 21.05, 'C_coal', 40, 'kosovo'),
 ('Ptolemais', 40.50, 21.70, 'C_coal', 40, 'ptolema'), ('Afsin-Elbistan', 38.30, 37.00, 'C_coal', 40, 'elbistan|afsin|afşin'),
 ('Moscow basin', 54.10, 37.40, 'C_coal', 60, 'moscow'), ('Kansk-Achinsk', 56.10, 93.75, 'C_coal', 100, 'kansk'),
 ('Irkutsk basin', 53.00, 103.00, 'C_coal', 100, 'irkutsk|cheremkhovo'), ('South Yakutia (Neryungri)', 56.66, 124.70, 'C_coal', 60, 'yakut|neryung|aldan'),
 ('Bureya', 50.50, 131.00, 'C_coal', 80, 'bureya'), ('Sakhalin', 49.00, 142.50, 'C_coal', 100, 'sakhalin'),
 ('Ekibastuz', 51.72, 75.30, 'C_coal', 40, 'ekibastuz'), ('Junggar (Xinjiang)', 44.50, 87.00, 'C_coal', 200, 'junggar|zhunge|xinjiang'),
 ('Turpan-Hami', 42.80, 91.50, 'C_coal', 150, 'turpan|hami|tuha'), ('Huainan-Huaibei', 33.00, 116.80, 'C_coal', 80, 'huainan|huaibei'),
 ('Pingdingshan', 33.73, 113.30, 'C_coal', 60, 'pingdingshan'), ('Kailuan (Tangshan)', 39.70, 118.30, 'C_coal', 40, 'kailuan|tangshan'),
 ('Fushun', 41.85, 123.90, 'C_coal', 30, 'fushun'), ('Heilongjiang (Hegang, Jixi)', 46.00, 131.00, 'C_coal', 150, 'hegang|jixi|shuangyashan|heilong'),
 ('Chikuho (Kyushu)', 33.65, 130.70, 'C_coal', 40, 'chikuho|kyushu|miike'), ('Ishikari (Hokkaido)', 43.30, 141.90, 'C_coal', 60, 'ishikari|hokkaido|yubari'),
 ('Quang Ninh', 21.00, 107.20, 'C_coal', 40, 'quang ninh|hon gai|hongay'), ('Singareni (Godavari)', 17.60, 80.30, 'C_coal', 80, 'godavari|singareni'),
 ('Thar', 24.80, 70.30, 'C_coal', 60, 'thar'), ('Surat/Galilee', -23.60, 145.70, 'C_coal', 60, 'galilee'),
 ('Collie', -33.36, 116.15, 'C_coal', 30, 'collie'), ('Leigh Creek', -30.47, 138.42, 'C_coal', 30, 'leigh creek'),
 ('Waikato (NZ)', -37.60, 175.10, 'C_coal', 60, 'waikato|huntly'), ('West Coast NZ', -41.80, 171.80, 'C_coal', 60, 'west coast|buller|greymouth'),
 ('Morupule/Kalahari (Botswana)', -22.50, 26.80, 'C_coal', 150, 'morupule|botswana|kalahari'), ('Enugu', 6.45, 7.50, 'C_coal', 40, 'enugu'),
 ('Sabinas (Mexico)', 27.90, -101.20, 'C_coal', 60, 'sabinas|coahuila'), ('Rio Turbio', -51.55, -72.30, 'C_coal', 40, 'turbio|magallanes'),
 ('Sakoa (Madagascar)', -22.5, 44.6, 'C_coal', 60, 'sakoa'), ('Maamba (Zambia)', -17.37, 27.15, 'C_coal', 60, 'maamba|zambezi'),
 # evaporites, industrial, gems, U
 ('Carlsbad potash (Delaware basin)', 32.40, -103.80, 'K', 60, 'carlsbad|delaware|permian basin'), ('Qarhan (Qinghai)', 36.80, 95.00, 'K', 80, 'qarhan|qaidam'),
 ('Khorat (Thailand/Laos)', 16.00, 103.00, 'K', 150, 'khorat|sakon|vientiane'), ('Uyuni', -20.20, -67.50, 'K', 80, 'uyuni'),
 ('Sergipe potash', -10.80, -37.20, 'K', 60, 'sergipe|taquari'), ('Solikamsk', 59.6, 56.8, 'K', 30, 'kama'),
 ('Bahia Blanca? (skip)', -38.7, -62.3, 'NaCl', 1, 'zzzz'),
 ('Atacama nitrates', -21.50, -69.60, 'N', 150, 'nitrat|caliche|salitre'), ('Searles Lake borates', 35.75, -117.35, 'B', 40, 'searles|boron|kramer'),
 ('Turkey borates (Kirka/Emet/Bigadic)', 39.40, 29.30, 'B', 120, 'kirka|emet|bigadi|kestelek'), ('Inder borates', 48.40, 51.90, 'B', 40, 'inder'),
 ('Frasch salt-dome sulfur (Gulf Coast)', 29.50, -94.50, 'S', 150, 'boling|frasch|sulphur|gulf'), ('Hallstatt', 47.56, 13.64, 'NaCl', 20, 'hallstatt'),
 ('Cheshire salt', 53.25, -2.50, 'NaCl', 40, 'cheshire|northwich|nantwich'), ('Hormuz salt domes', 27.00, 55.50, 'NaCl', 150, 'hormuz|persian gulf'),
 ('Lake Assal/Danakil', 14.20, 40.30, 'NaCl', 150, 'danakil|dallol|assal|afar'), ('Taoudenni', 22.67, -3.98, 'NaCl', 60, 'taoudenni|taghaza'),
 ('Khibiny apatite', 67.65, 33.70, 'P', 40, 'khibin|kirovsk'), ('Kola Kovdor', 67.56, 30.46, 'P', 30, 'kovdor'),
 ('Karatau phosphorite', 43.50, 69.50, 'P', 100, 'karatau'), ('Jordan/Eshidiya', 29.90, 36.20, 'P', 100, 'eshidiya|jordan|hasa|abiad'),
 ('Gafsa', 34.40, 8.50, 'P', 60, 'gafsa|metlaoui'), ('Florida', 27.80, -81.90, 'P', 40, 'florida'),
 ('Ruby: Mogok', 22.92, 96.50, 'corundum', 30, 'mogok'), ('Sapphire: Ratnapura', 6.70, 80.40, 'corundum', 40, 'ratnapura'),
 ('Sapphire: Chanthaburi', 12.60, 102.10, 'corundum', 40, 'chanthaburi'), ('Sapphire: Ilakaka', -22.70, 45.20, 'corundum', 40, 'ilakaka'),
 ('Emerald: Sandawana', -20.90, 29.95, 'emerald', 40, 'sandawana'), ('Emerald: Kafubu (Zambia)', -13.00, 28.30, 'emerald', 60, 'kafubu|kagem|ndola'),
 ('Emerald: Itabira/Carnaiba', -10.55, -40.20, 'emerald', 300, 'carna|itabira|santa terezinha'), ('Emerald: Panjshir', 35.30, 69.70, 'emerald', 60, 'panjshir'),
 ('Emerald: Habachtal', 47.20, 12.35, 'emerald', 30, 'habach'), ('Emerald: Urals (Malysheva)', 57.10, 61.40, 'emerald', 40, 'malysh|ural'),
 ('Emerald: Sikait (Egypt)', 24.60, 34.80, 'emerald', 60, 'sikait|zabara'), ('Peridot: Zabargad', 23.61, 36.20, 'olivine', 40, 'zabargad'),
 ('Lapis: Sar-e-Sang', 36.20, 70.80, 'lapis', 30, 'sar-e-sang'), ('Garnet: Bohemian pyrope', 50.50, 13.90, 'garnet', 60, 'bohemi|pyrope|podsedice|třebenice'),
 ('Diamond: Ekati/Diavik', 64.60, -110.50, 'diamond', 60, 'ekati|diavik'), ('Diamond: Venetia', -22.43, 29.32, 'diamond', 30, 'venetia'),
 ('Diamond: Premier/Cullinan', -25.67, 28.52, 'diamond', 30, 'premier|cullinan'), ('Diamond: Namibia coast (Namdeb)', -28.00, 16.00, 'diamond', 150, 'namdeb|namaqualand|sperrgebiet|orange'),
 ('Diamond: Mbuji-Mayi', -6.15, 23.60, 'diamond', 60, 'mbuji|kasai'), ('Diamond: Koidu/Kono', 8.65, -10.97, 'diamond', 60, 'koidu|kono|sierra'),
 ('Diamond: Marange', -19.95, 32.40, 'diamond', 60, 'marange'), ('Diamond: Grib/Lomonosov (Arkhangelsk)', 65.30, 41.80, 'diamond', 80, 'grib|lomonosov|arkhangel'),
 ('Diamond: Diamantina', -18.24, -43.60, 'diamond', 60, 'diamantina'), ('Diamond: Panna', 24.73, 80.04, 'diamond', 40, 'panna|majhgawan'),
 ('U: Athabasca', 57.8, -105.0, 'U', 60, 'mcarthur|cigar|key lake|rabbit'), ('U: Elliot Lake', 46.40, -82.65, 'U', 40, 'elliot'),
 ('U: Ranger/Jabiluka', -12.68, 132.90, 'U', 40, 'ranger|jabiluka|alligator'), ('U: Rossing/Husab', -22.48, 15.05, 'U', 40, 'rossing|rössing|husab'),
 ('U: Arlit (Niger)', 18.75, 7.35, 'U', 40, 'arlit|akouta|somair|cominak'), ('U: Grants (NM)', 35.30, -107.80, 'U', 60, 'grants|ambrosia|laguna'),
 ('U: Colorado Plateau (Uravan)', 38.37, -108.73, 'U', 80, 'uravan|lisbon|colorado plateau'), ('U: Schlema-Alberoda (Wismut)', 50.60, 12.67, 'U', 30, 'schlema|alberoda|wismut'),
 ('U: Ronneburg', 50.85, 12.18, 'U', 30, 'ronneburg'), ('U: Streltsovka', 50.40, 118.10, 'U', 40, 'strelts|krasnokamensk'),
 ('U: Olympic Dam U', -30.44, 136.88, 'U', 10, 'olympic'), ('U: Jaduguda', 22.65, 86.35, 'U', 40, 'jadugud'),
 ('U: Shinkolobwe', -11.05, 26.55, 'U', 30, 'shinkolobwe'), ('U: Mounana/Oklo', -1.40, 13.15, 'U', 40, 'mounana|oklo'),
 ('U: Kvanefjeld', 60.97, -45.98, 'U', 30, 'kvanefjeld'), ('U: Zhovti Vody', 48.35, 33.50, 'U', 30, 'zhovt'),
 ('Graphite: Borrowdale', 54.50, -3.17, 'C_graphite', 30, 'borrowdale'), ('Graphite: Passau/Kropfmuehl', 48.62, 13.60, 'C_graphite', 30, 'kropf|passau'),
 ('Graphite: Balama', -13.33, 38.60, 'C_graphite', 40, 'balama'), ('Graphite: Heilongjiang (Jixi/Luobei)', 46.0, 131.0, 'C_graphite', 200, 'jixi|luobei|heilong'),
 ('Graphite: Sri Lanka (Bogala)', 7.05, 80.30, 'C_graphite', 60, 'bogala|kahatagaha|sri lanka'), ('Fluorite: Illinois-Kentucky', 37.45, -88.20, 'F', 60, 'illinois|kentucky|cave-in-rock|rosiclare'),
 ('Fluorite: Las Cuevas (Mexico)', 21.80, -100.60, 'F', 60, 'cuevas|san luis'), ('Fluorite: Vergenoeg', -25.30, 28.55, 'F', 40, 'vergenoeg'),
 ('Fluorite: Inner Mongolia (Sumochagan)', 42.4, 112.6, 'F', 200, 'sumochagan|mongolia'), ('Hg: Wanshan', 27.50, 109.20, 'Hg', 60, 'wanshan'),
 ('Hg: Monte Amiata', 42.88, 11.62, 'Hg', 30, 'amiata'), ('Hg: Nikitovka', 48.32, 38.06, 'Hg', 30, 'nikitov|mykyt'),
 ('Sb: Kadamjay', 40.13, 71.73, 'Sb', 30, 'kadamjay'), ('Sb: Murchison/Gravelotte', -23.90, 30.70, 'Sb', 40, 'murchison|gravelotte|consolidated'),
 ('Bi: Tasna', -20.80, -65.95, 'Bi', 60, 'tasna'), ('Bi: Shizhuyuan', 25.75, 113.12, 'Bi', 30, 'shizhuyuan'),
 ('Alum: Tolfa', 42.15, 11.93, 'alum', 30, 'tolfa'), ('Alum: Phocaea/Karahisar', 40.27, 38.40, 'alum', 120, 'phoca|karahisar|şebinkarahisar'),
 ('Bauxite: Les Baux', 43.75, 4.80, 'Al', 40, 'baux|var|brignoles'), ('Bauxite: Jamaica', 18.10, -77.40, 'Al', 80, 'jamaica|manchester|st ann'),
 ('Bauxite: Trombetas', -1.50, -56.40, 'Al', 60, 'trombetas|porto trombetas'), ('Bauxite: Paragominas', -3.20, -47.70, 'Al', 80, 'paragominas'),
 ('Bauxite: Darling Range', -32.70, 116.10, 'Al', 80, 'darling|huntly|willowdale'), ('Bauxite: Odisha (Panchpatmali)', 18.80, 82.95, 'Al', 80, 'panchpat|koraput|odisha'),
 ('Bauxite: Guangxi (Pingguo)', 23.30, 107.60, 'Al', 80, 'pingguo|guangxi'), ('Bauxite: Suriname/Guyana', 5.30, -55.00, 'Al', 200, 'suriname|moengo|guyana|linden'),
 ('Bauxite: North Urals (Severouralsk)', 60.15, 60.00, 'Al', 60, 'severoural|ural'), ('Bauxite: Fria/Kindia (Guinea)', 10.40, -13.50, 'Al', 60, 'fria|kindia|friguia'),
]

out = []
for lab, lat, lon, lay, R, rx in L:
    if rx == 'zzzz': continue
    rgx = re.compile(rx, re.I)
    cands = []
    for ls, name, geom, x in B:
        if lay not in ls: continue
        d = dist_km(geom, lat, lon)
        nm = bool(rgx.search(name))
        if d <= R or (nm and d <= 400):
            cands.append((d, name, x.get('mt'), nm))
    cands.sort()
    status = 'present' if cands else 'MISSING'
    best = cands[0] if cands else None
    out.append({'check': lab, 'layer': lay, 'lat': lat, 'lon': lon, 'radiusKm': R, 'status': status,
                'nearest': best[1] if best else '', 'distKm': round(best[0], 1) if best else '', 'mt': best[2] if best else '',
                'n_within': len(cands)})
with open(HERE / 'presence-check.csv', 'w', newline='', encoding='utf8') as f:
    w = csv.DictWriter(f, fieldnames=list(out[0])); w.writeheader(); w.writerows(out)
miss = [o for o in out if o['status'] == 'MISSING']
print(len(out), 'checked;', len(miss), 'missing')
for o in miss: print('MISSING', o['layer'], o['check'], o['lat'], o['lon'])
print()
for o in out:
    if o['status'] == 'present' and o['distKm'] and float(o['distKm']) > 10:
        print('far', o['layer'], o['check'], '->', o['nearest'], o['distKm'], 'km')
