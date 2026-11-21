# The ledger of human works undone

Earth builds the world as around 1318 (decision [0064](decisions/0064-coasts-and-waters-as-of-1318.md)): where people changed the coasts and waters after 1318, the data is changed back, and natural change after 1318 stays. This page lists every such change Earth makes, and the human works it leaves as they are, with the reason. Every change is for new worlds only: a saved world keeps the rules versions it was made with (decision 0002), so its land never changes.

Written by `tools/works_ledger.py` from its entries and the files the mod ships; its facts block (the last section) is checked against the files and the code by `tests/DataReadersTests`. A new change of this kind, or a change to one of these files, comes with its entry there and this page written again, in the same commit (Earth's AGENTS.md).

## The changes

### The Netherlands: the Zuiderzee, the Delta's arms and the drained lakes

- **The work:** Land won from the sea and from lakes after 1318: the Zuiderzee works and the Afsluitdijk (1920-1968), the Wadden and Zeeland polders, the Delta Works' dams, the North Holland lakes pumped dry (Beemster 1612, Purmer 1622, Wormer 1626, Schermer 1635, the Haarlemmermeer 1849-1852); and lakes dug in the peat after 1318 (Loosdrecht, the Vinkeveen and Reeuwijk lakes, gravel pits).
- **What Earth does:** Areas of `works-1318-2.bin` (works rules 1-4) and `works-1318-3.bin` (rules 5): SEA areas are sea over today's land, LAKE areas lakes at 0 m, LAND areas land over today's peat-digging lakes (table A). Holland's sunken peat polders need nothing: the game holds land below the sea at the sea's level.
- **Source and licence:** RCE paleogeographic maps of 1250, 1500 and 2000 AD (Vos et al., Atlas van Nederland in het Holoceen), CC BY 4.0, compared class by class.
- **Confidence:** high (the map nearest 1318 is 1250's; natural change after it, the storm floods of 1362 and 1421, kept from 1500's)
- **Rules:** `Works1318` (EarthParams 106) 1-5; on wherever the coasts come from their outlines (1:100 and finer); new worlds only
- **Decision:** [0064](decisions/0064-coasts-and-waters-as-of-1318.md) step 1

### Maasvlakte 2 and the dunes on Maasvlakte 1

- **The work:** Rotterdam's port land built in the North Sea, 1965-2013 (after the RCE map of 2000).
- **What Earth does:** One SEA area (flagged LATE in `works-1318-2.bin`/`-3.bin`): the water mask's land on the sea of 1318 inside the works' box; the island layer draws no land there.
- **Source and licence:** Copernicus GLO-90 water mask (as Earth's heights), RCE 1250/1500 for the sea of 1318.
- **Confidence:** high
- **Rules:** `Works1318` 4 and later; new worlds only
- **Decision:** 0064, works rules 4 (EA-0053)

### Canals dug after 1318 (OpenStreetMap)

- **The work:** Ship and irrigation canals built from the 18th century on (table B, with the years).
- **What Earth does:** LAND|CANAL areas: the water mask's river water inside each canal's corridor is land; lake water a canal passes through stays (the Kiel Canal's lakes). The Nieuwe Waterweg is kept (table B).
- **Source and licence:** OpenStreetMap (ODbL; Overpass 12 November 2026, QLever 13 November), cut to the GLO-90 water mask.
- **Confidence:** high for the 29 named canals; smaller canals the mask draws as river stay (not in the list)
- **Rules:** `Works1318` 2 and later (the Corinth, White Sea-Baltic and Ismailia canals from 4); new worlds only
- **Decision:** [0069](decisions/0069-river-flow-field-canals-and-shore-zones.md) rule 6, 0064

### The Suez and Panama canals, the Bitter Lakes, Lake Timsah and Gatun Lake

- **The work:** The Suez Canal (1859-1869; the Bitter Lakes and Lake Timsah, dry before, flooded by it) and the Panama Canal (1904-1914; Gatun Lake, its reservoir).
- **What Earth does:** `coasts.bin` closes the canal gaps in GSHHG's outlines (312 km² of water made land at Suez, 424 at Panama); channel rules 1 and later draw no outline lake or river water and no grid lake in the two canal zones (`SeaChannels.InCanalZone`): the Bitter Lakes' bed is a dry plain at sea level, no Gatun Lake. The Earth tab's canal option cuts a gameplay canal (not the real one).
- **Source and licence:** GSHHG (LGPL), Earth's own canal boxes.
- **Confidence:** high (the Bitter Lakes' salt-crust floor, some metres below the sea, is not drawn)
- **Rules:** `Coasts` (outline coasts), `ChannelRules` 1 and later; new worlds only
- **Decision:** [0020](decisions/0020-canal-gaps.md), [0063](decisions/0063-rivers-follow-the-real-channel.md)

### Reservoirs behind dams built after 1318

- **The work:** Reservoirs worldwide, nearly all of the 19th to 21st centuries.
- **What Earth does:** Left out of the water: the GLO-90 lake outlines leave out every HydroLAKES reservoir and Global Dam Watch's reservoirs that HydroLAKES lacks (24,459 lakes, 283,086 km², in `lakes-glo90-2.bin`; carried on to `-5`), and the river outlines leave out their river water; the 2' grid drops the Natural Earth lakes Wikidata calls reservoirs and Lake Razazah (`reservoirs-v1.json`). Lakes a dam only raised (HydroLAKES counts some, Moosehead Lake) keep their water at today's level.
- **Source and licence:** HydroLAKES 1.0 (CC BY 4.0), Global Dam Watch 1.0 (CC BY 4.0; barriers built after 1318 with a reservoir from 0.1 km², 1,015 of them on lakes HydroLAKES has as natural: built from 1900, 15 m or higher), Wikidata (CC0).
- **Confidence:** high for the big ones; reservoirs without a HydroLAKES dam point and water more than 3 m above the reservoir's level stay river
- **Rules:** `LakeOutlines` 2 and later (`lakes-glo90-*.bin`), `RiverOutlines`, `ReservoirRules` 1 (the grid's); new worlds only
- **Decision:** [0053](decisions/0053-scales-included-glo90-and-glo90-rivers.md) items 17-18, 0069 rule 9

### Dams and the valleys under their reservoirs

- **The work:** The dam walls and the flat water GLO-90 measured on the reservoirs.
- **What Earth does:** `dams-glo90-*.bin` (the data release `egl-v1`): per GLO-90 square, the heights that change: walls taken down, the valley floor rebuilt under the reservoir with the river's course falling evenly from where it comes in to the foot of each dam (3,488 squares in rules 3).
- **Source and licence:** Copernicus GLO-90, HydroLAKES pour points, Global Dam Watch 1.0 (CC BY 4.0).
- **Confidence:** medium: the old valley is rebuilt, not measured (no pre-dam heights exist)
- **Rules:** `DamRules` (EarthParams 105) 1-4; new worlds only
- **Decision:** 0053 item 19, 0069 rule 9, [0092](decisions/0092-water-on-glo90s-own-grid.md)

### Terminal lakes dried by irrigation and dams in the 20th century (decision 0005)

- **The work:** Rivers drained into irrigation (the Amu Darya and Syr Darya, the Tarim, the Helmand, the Kor, the Owens River, the Kings and Kern, ...) left their end lakes dry or shrunk, mostly after 1950 (table C).
- **What Earth does:** `lakes.bin` fills each basin on the 2' grid to a level before the drying ("revived"), and the rivers run into it at that level; the natural salt pans in the same file are filled to a wet-year level (not a human work). Green shores where the lakes had them.
- **Source and licence:** ETOPO 2022 (the basins), the levels from the lakes' records (decision 0005).
- **Confidence:** medium: the level is the lake's of about 1960 (1870s for Tulare, 1913 for Owens), not 1318's; for the Aral, 1318's replaces it (next entry)
- **Rules:** `RiverEnds` (EarthParams 57) 1-2 read `lakes.bin`; 3 read `lakes-2.bin`; new worlds only
- **Decision:** [0005](decisions/0005-river-ends.md)

### The Aral Sea at its level of 1318

- **The work:** The 20th-century irrigation drying (as above); in 1318 the lake was at its medieval low anyway (the Amu Darya ran mostly to the Sarykamysh after Khwarezm's dikes were broken in 1221).
- **What Earth does:** At 29 m instead of 1960's 53 m: on the 2' grid in `lakes-2.bin` (the western basin, its other 1960 cells the grid's own again), and in GLO-90 worlds a LAKE area of `works-1318-4.bin` contoured from GLO-90 at 29 m (the western basin and the eastern basin's lagoon, table D). The rivers end in it.
- **Source and licence:** Krivonogov et al. 2014 (Gondwana Research 26): about 29 m (27-30) in the 12th-15th centuries; Copernicus GLO-90.
- **Confidence:** medium (the level from cores and the medieval delta; no outline exists)
- **Rules:** `RiverEnds` 3, `Works1318` 6; new worlds only
- **Decision:** [0107](decisions/0107-lakes-of-1318.md)

### Lakes drained after 1318

- **The work:** Lakes pumped or tunnelled dry from the 17th to the 20th century (table D, with the years).
- **What Earth does:** LAKE areas of `works-1318-4.bin` at their levels (table D): water wherever the area lies, today's ponds and reservoirs inside it gone, the land beside held at its top, the rivers inside at its level.
- **Source and licence:** OpenHistoricalMap (CC0) and OpenStreetMap (ODbL) outlines, or Copernicus GLO-90 contoured at the level where the ground has not sunk; the levels from the sources in table D.
- **Confidence:** medium: the outlines are the last state before drainage, which can differ from 1318's (Fucino was low in the 16th century; Ogura-ike's is its 1933 remnant)
- **Rules:** `Works1318` 6; new worlds only
- **Decision:** 0107

### The Salton Sea

- **The work:** Made by the Colorado River's breach of an irrigation canal in 1905-1907.
- **What Earth does:** A LAND area of `works-1318-4.bin`: its basin is dry land (GLO-90's surface, the sea's flat at about -71 m).
- **Source and licence:** Rockwell et al. 2022: Lake Cahuilla stood high before 1241 and in 1459-1503, not in 1318.
- **Confidence:** medium
- **Rules:** `Works1318` 6; new worlds only
- **Decision:** 0107

## Left as they are

| Human work | Why it stays |
|---|---|
| The North Aral at 41 m | the Kokaral dam of 2005 holds it; the Small Aral of 1318 was low, but its bed under today's water is unknown (0107) |
| Tulare Lake at 61 m | decision 0005's 1870s lake; the lake of about 1300 was near a medieval low, with no level or extent in the sources (0107) |
| Reservoirs older than 1318 | dropped with the rest (Parakrama Samudra, Minneriya): HydroLAKES has no dates; 0064's keep list of them is not built yet |
| Reclamation since 1985 | Palm Jumeirah, the airports at Chek Lap Kok, Kansai and Incheon, Tuas, Caofeidian: 0064 step 2, not built |
| Polders below the sea outside the Netherlands | 0064 step 4 (the polder rule), not built; the Fens and the Sacramento delta are land at sea level |
| Ponds and pits | opencast-mine lakes, gravel pits, fish and salt ponds the water mask draws as lakes; no classified source yet (0064 R3) |
| Lakes made after 1318 not yet listed | the Aydar-Arnasay lakes (1969), Lake Nasser's Toshka lakes, Mareotis (dry in 1318), Abu Qir (formed about 1770-1784) |
| Other drained lakes | Tule and Lower Klamath lakes, La Janda, Kolindsund, Martin Mere, the other Fens meres, Bientina, Comacchio's reclaimed valli, Dongting and Poyang's polders: no quick open outline or level yet (docs/research/lakes-1318-2026-11) |
| Rivers moved by works | the Aare into Lake Biel (1878), the Linth into the Walensee (1811), the Kander into Lake Thun (1714), the Nene by Morton's Leam, the Syr Darya's and Amu Darya's courses of 1318: course edits, not built |
| The Mesopotamian marshes | drained in the 1990s; decision 0005's open item |
| The Bitter Lakes' surface | dry (above), but not its salt crust nor its depth below the sea |

## Table A: the works files' areas

Area pieces (an area is cut at the 1° tiles) and their surface, by kind. Each file keeps the one before byte for byte and adds its own after it (`works-1318-3.bin` moves the canals and Maasvlakte 2 onto GLO-90's own grid).

| File | Kind | Pieces | km² |
|---|---|---|---|
| `works-1318-2.bin` | lake | 162 | 486 |
| `works-1318-2.bin` | land | 411 | 225 |
| `works-1318-2.bin` | land+canal | 1691 | 726 |
| `works-1318-2.bin` | land+canal+late | 69 | 32 |
| `works-1318-2.bin` | sea | 119 | 5,543 |
| `works-1318-2.bin` | sea+late | 2 | 47 |
| `works-1318-3.bin` | lake | 162 | 486 |
| `works-1318-3.bin` | land | 411 | 225 |
| `works-1318-3.bin` | land+canal | 1695 | 745 |
| `works-1318-3.bin` | land+canal+late | 69 | 34 |
| `works-1318-3.bin` | sea | 119 | 5,542 |
| `works-1318-3.bin` | sea+late | 2 | 47 |
| `works-1318-4.bin` | lake | 198 | 10,212 |
| `works-1318-4.bin` | land | 413 | 1,305 |
| `works-1318-4.bin` | land+canal | 1695 | 745 |
| `works-1318-4.bin` | land+canal+late | 69 | 34 |
| `works-1318-4.bin` | sea | 119 | 5,542 |
| `works-1318-4.bin` | sea+late | 2 | 47 |

## Table B: the canals

| Canal | Built | Undone | From works rules |
|---|---|---|---|
| Nord-Ostsee-Kanal (Kiel Canal) | 1887-1895 | yes | 2 |
| Noordzeekanaal (North Sea Canal) | 1865-1876 | yes | 2 |
| Amsterdam-Rijnkanaal (Amsterdam-Rhine Canal) | 1931-1952 | yes | 2 |
| Nieuwe Waterweg (Rotterdam's dug mouth) | 1866-1872 | no: the Maas mouth was open water in 1318 (EA-0057) | 4 |
| Manchester Ship Canal | 1887-1894 | yes | 2 |
| Mittellandkanal | 1906-1938 | yes | 2 |
| Dortmund-Ems-Kanal | 1892-1899 | yes | 2 |
| Main-Donau-Kanal | 1960-1992 | yes | 2 |
| Albertkanaal | 1930-1939 | yes | 2 |
| Kanaal Gent-Terneuzen | 1823-1827 | yes | 2 |
| Boudewijnkanaal (Bruges-Zeebrugge) | 1896-1907 | yes | 2 |
| Grand Canal d'Alsace | 1928-1959 | yes | 2 |
| Canal de Donzère-Mondragon | 1947-1952 | yes | 2 |
| Διώρυγα της Κορίνθου (Corinth Canal) | 1881-1893 | yes | 4 |
| Canalul Dunăre Marea Neagră (Danube-Black Sea Canal) | 1976-1984 | yes | 2 |
| Kanal Dunav Tisa Dunav (Danube-Tisa-Danube) | 1957-1977 | yes | 2 |
| Волго-Донской (Volga-Don Canal) | 1948-1952 | yes | 2 |
| Канал имени Москвы (Moscow Canal) | 1932-1937 | yes | 2 |
| Беломорско-Балтийский канал (White Sea-Baltic Canal) | 1931-1933 | yes | 4 |
| Каховський магістральний канал (Kakhovka main canal) | 1979- | yes | 2 |
| Північнокримський канал (North Crimean Canal) | 1961-1971 | yes | 2 |
| Garagum (Karakum Canal) | 1954-1988 | yes | 2 |
| Иртыш-Караганда (Irtysh-Karaganda Canal) | 1962-1974 | yes | 2 |
| Indira Gandhi Nahar (Indira Gandhi Canal) | 1958- | yes | 2 |
| المحمودية (Mahmoudiyah Canal) | 1817-1820 | yes | 2 |
| قناة الإسماعيلية (Ismailia (Sweet Water) Canal) | 1861-1863 | yes | 4 |
| Welland Ship Canal | 1829-1932 | yes | 2 |
| Chicago Sanitary and Ship Canal | 1892-1900 | yes | 2 |
| Cape Cod Canal | 1909-1914 | yes | 2 |
| Chesapeake and Delaware Canal | 1829 | yes | 2 |

## Table C: the terminal lakes (decision 0005)

Each lake's level in `lakes.bin` (river ends rules 1-2) and `lakes-2.bin` (rules 3), whether it is revived (dried by people) or natural (a salt pan filled to a wet year), and the 2′ grid cells the file makes lake there (Tashk and Bakhtegan share one basin, so their cells are the same). Only the Aral's level differs between the two files.

| Lake | Kind | Level, rules 1-2 (m) | Level, rules 3 (m) | Salt | Green shore (km) | Cells made lake, rules 1-2 (km²) |
|---|---|---|---|---|---|---|
| Aral Sea | revived | 53 | 29 | yes | 40 | 66,825 |
| Hamun lakes | revived | 475 | 475 | no | 15 | 3,607 |
| Lop Nur | revived | 788 | 788 | yes | 10 | 3,293 |
| Tashk | revived | 1562 | 1562 | yes | 0 | 863 |
| Bakhtegan | revived | 1562 | 1562 | yes | 0 | 863 |
| Gavkhouni | revived | 1452 | 1452 | yes | 5 | 606 |
| Manas Lake | revived | 256 | 256 | yes | 8 | 614 |
| Lake Chad | revived | 283 | 283 | no | 25 | 22,779 |
| Owens Lake | revived | 1096 | 1096 | yes | 0 | 310 |
| Tulare Lake | revived | 61 | 61 | no | 12 | 1,726 |
| Dead Sea | revived | -392 | -392 | yes | 0 | 1,116 |
| Kati Thanda-Lake Eyre | natural | -14 | -14 | yes | 0 | 6,909 |
| Paraku (Lake Gregory) | natural | 276 | 276 | no | 5 | 246 |
| Salar de Coipasa | natural | 3656.8 | 3656.8 | yes | 0 | 2,130 |
| Makgadikgadi | natural | 902 | 902 | yes | 0 | 3,013 |
| Namak Lake | natural | 789 | 789 | yes | 0 | 2,280 |
| Salinas Grandes | natural | 178 | 178 | yes | 0 | 955 |
| Etosha Pan | natural | 1081 | 1081 | yes | 0 | 2,778 |

## Table D: the lakes of 1318 (decision 0107, works rules 6)

| Area | Kind | Level (m) | km² | Drained or made | Outline | Source of the level |
|---|---|---|---|---|---|---|
| Aral Sea of 1318 (the Kerderi low stand) | lake (salt) | 29 | 8,021.8 | the Kerderi low stand, 12th-15th c. | GLO-90 below 29 m | Krivonogov et al. 2014, Gondwana Research 26: about 29 m (27-30) in the 12th-15th centuries; western basin full, a shallow lagoon in the eastern basin |
| Lake Texcoco | lake (salt) | 2237 | 525.4 | Huehuetoca tunnel 1607-08, Tajo de Nochistongo by 1789, Gran Canal 1900 | OpenHistoricalMap relation/2856566 | OpenHistoricalMap 'Lago de Texcoco' (to 1635), CC0; level about 2,236-2,240 m; the islands west of 99.10 W (Tenochtitlan, Tlatelolco and their chinampas, from 1325) are lake in 1318, the Peñón hills stay islands |
| Lake Xochimilco | lake | 2240 | 91.0 | as Texcoco; springs captured 1900s | OpenHistoricalMap relation/2856623 | OpenHistoricalMap 'Lago de Xochimilco' (to 1635), CC0 |
| Lake Chalco | lake | 2240 | 103.1 | as Texcoco; 1890s | OpenHistoricalMap relation/2856625 | OpenHistoricalMap 'Lago de Chalco' (to 1635), CC0 |
| Lake Xaltocan | lake (salt) | 2241 | 92.8 | cut off by 1607-1789 | OpenHistoricalMap relation/2856628 | OpenHistoricalMap 'Lago de Xaltocan' (to 1750), CC0 |
| Lake Zumpango | lake (salt) | 2241 | 56.6 | the Cuautitlan diverted 1607 | OpenHistoricalMap relation/2856629 | OpenHistoricalMap 'Lago de Zumpango' (to 1700), CC0; drawn at Xaltocan's level (2,241 m), where they meet (it stood a few metres higher: a block at 1:10) |
| Lake Fucino | lake | 660 | 131.4 | Torlonia tunnel 1855-1878 | GLO-90 below 660 m | Giraudi 1989/1990: the shore before the Torlonia drainage (1855-1878) at about 660 m; 140-155 km² (OpenHistoricalMap 'Lago Fucino', CC0: 141 km²) |
| Lake Copais | lake | 97 | 234.0 | 1880s-1931 | GLO-90 below 97 m | Mamassis et al. (after Constantinidis 1984): water 92-97 m, spilling to the eastern sinkholes above 97 m; drained land 241 km² |
| Lake Hula | lake | 70 | 13.5 | 1951-1958 | OpenHistoricalMap way/199762052 | OpenHistoricalMap 'Lake Hula' (to 1958), CC0; about 70 m |
| Lagoa de Antela | lake | 615 | 25.6 | 1958 on | OpenHistoricalMap way/205384766 | OpenHistoricalMap 'Lagoa de Antela' (to 1958), CC0; 615 m |
| Whittlesey Mere | lake | 1 | 4.1 | 1851 | OpenStreetMap way/1567216446 | OpenStreetMap 'Whittlesey Mere' (historic=yes), ODbL; about +1 m OD before the fen shrank |
| Stretham Mere | lake | 1 | 0.3 | by 1804 | OpenHistoricalMap way/201288938 | OpenHistoricalMap 'Stretham Mere' (to 1804), CC0 |
| Ogura-ike | lake | 12 | 7.7 | 1933-1941 | OpenHistoricalMap relation/2798692 | OpenHistoricalMap '巨椋池' (to 1933), CC0: the lake as drained in 1933-41, smaller than in 1318 |
| Lake Karla (Boebeis) | lake | 48 | 130.6 | tunnel 1962 | GLO-90 below 48 m | 50.10 m in 1921 on ground that has sunk since (cracks from pumping); 45-180 km² by season and year, drawn at about 130 km² (GLO-90 below 48 m); drained 1962 |
| Lake Amik (Lake of Antioch) | lake | 79 | 148.0 | 1954-1975 | GLO-90 below 79 m | 79.4 m in 1949, 83.4 m in 1953 (DSİ), on peat burned down 1-1.5 m since; 90-100 km² of lake, 310 with its marsh: drawn at about 150 km² (GLO-90 below 79 m); drained 1954-1975 |
| Hachirōgata | lake (salt) | 0 | 145.4 | 1957-1977 | GLO-90 below 0 m | a brackish lagoon of about 220 km² at sea level, reclaimed 1957-1977 |
| Salton Sea (dry in 1318) | land | - | 1,079.9 | made 1905-1907 | GLO-90 below -68 m, grown 300 m | made by the Colorado River's breach of 1905-1907; Lake Cahuilla stood high before 1241 and in 1459-1503 only (Rockwell et al. 2022) |

## Facts checked by the tests

```ledger-facts
file works-1318-2.bin 143044 2bbf0ea88c9c8a2217a7b9e94b35a99e8bbdc20941ca1bbc906611fb4120282f
file works-1318-3.bin 143321 49506c2fd443ea9bc771dbd8ffff7bbb7ace73fbb9a6ed8dbd8cdd983429c6d5
file works-1318-4.bin 162150 017d2a173697fb84401d0f61a6db463b8891d9e8bd2f6a1ad62b7f3ec4c28ebb
file lakes.bin 17444 cbec2376eba342320098a7856d6ec7f62463273699ae6c3edb064e794718560d
file lakes-2.bin 11516 0aa5122b129e3735a2e59422408866a14e41cbb1cb880277c218bbbb13429186
file reservoirs-v1.json 4734 1a856e9bf9097cedfb7ff09691518afe7c223be30e0dd91803c0f644ac796b4c
rules Works1318 6
rules RiverEnds 3
rules DamRules 4
rules ChannelRules 5
works works-1318-2.bin 16 119
works works-1318-2.bin 20 2
works works-1318-2.bin 32 162
works works-1318-2.bin 64 411
works works-1318-2.bin 72 1691
works works-1318-2.bin 76 69
works works-1318-3.bin 16 119
works works-1318-3.bin 20 2
works works-1318-3.bin 32 162
works works-1318-3.bin 64 411
works works-1318-3.bin 72 1695
works works-1318-3.bin 76 69
works works-1318-4.bin 16 119
works works-1318-4.bin 20 2
works works-1318-4.bin 32 184
works works-1318-4.bin 33 14
works works-1318-4.bin 64 413
works works-1318-4.bin 72 1695
works works-1318-4.bin 76 69
terminal lakes.bin 53 Aral Sea
terminal lakes.bin 475 Hamun lakes
terminal lakes.bin 788 Lop Nur
terminal lakes.bin 1562 Tashk
terminal lakes.bin 1562 Bakhtegan
terminal lakes.bin 1452 Gavkhouni
terminal lakes.bin 256 Manas Lake
terminal lakes.bin 283 Lake Chad
terminal lakes.bin 1096 Owens Lake
terminal lakes.bin 61 Tulare Lake
terminal lakes.bin -392 Dead Sea
terminal lakes.bin -14 Kati Thanda-Lake Eyre
terminal lakes.bin 276 Paraku (Lake Gregory)
terminal lakes.bin 3656.8 Salar de Coipasa
terminal lakes.bin 902 Makgadikgadi
terminal lakes.bin 789 Namak Lake
terminal lakes.bin 178 Salinas Grandes
terminal lakes.bin 1081 Etosha Pan
terminal lakes-2.bin 29 Aral Sea
terminal lakes-2.bin 475 Hamun lakes
terminal lakes-2.bin 788 Lop Nur
terminal lakes-2.bin 1562 Tashk
terminal lakes-2.bin 1562 Bakhtegan
terminal lakes-2.bin 1452 Gavkhouni
terminal lakes-2.bin 256 Manas Lake
terminal lakes-2.bin 283 Lake Chad
terminal lakes-2.bin 1096 Owens Lake
terminal lakes-2.bin 61 Tulare Lake
terminal lakes-2.bin -392 Dead Sea
terminal lakes-2.bin -14 Kati Thanda-Lake Eyre
terminal lakes-2.bin 276 Paraku (Lake Gregory)
terminal lakes-2.bin 3656.8 Salar de Coipasa
terminal lakes-2.bin 902 Makgadikgadi
terminal lakes-2.bin 789 Namak Lake
terminal lakes-2.bin 178 Salinas Grandes
terminal lakes-2.bin 1081 Etosha Pan
lake1318 aral lake 29
lake1318 texcoco lake 2237
lake1318 xochimilco lake 2240
lake1318 chalco lake 2240
lake1318 xaltocan lake 2241
lake1318 zumpango lake 2241
lake1318 fucino lake 660
lake1318 copais lake 97
lake1318 hula lake 70
lake1318 antela lake 615
lake1318 whittlesey lake 1
lake1318 stretham lake 1
lake1318 ogura lake 12
lake1318 karla lake 48
lake1318 amik lake 79
lake1318 hachirogata lake 0
lake1318 salton land 0
```
