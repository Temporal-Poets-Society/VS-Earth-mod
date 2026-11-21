# Soil rules 4: Earth soil over vanilla's block layers, checks (20 November 2026)

For [decision 0105](../../decisions/0105-earth-soil-over-vanilla-layers.md) and EA-0199, on `claude/soil-rules-4`.

## Offline census (`census.txt`)

`dotnet run -c Release --project tests/SoilTests -- <root> census <VintagestoryData/ModData/earth> 0.25`

The census reads what a new world with Earth soil reads: the 30″ SoilGrids set, the data store's 2′ maps, the climate as
new worlds load it, the floodplain grid at GLO-90 heights and the wetland grid. Rivers aren't built offline, so a river's
green belt is not counted.

Regions, as share of land in vanilla tiers / realistic tiers:

| Region | Result |
|---|---|
| Poltava steppe | high 94 % / black earth 94 % (Chernozems 94 %) |
| Zemgale plain | medium 68 %, the rest low or barren (Gleysols, Podzols, Histosols) |
| Central Europe (49–52.5 N, 7–16 E) | medium 93 % |
| Finland 61–65 N | low 88 % / barren 82 % (Podzols 81 %); Cambisols medium |
| Karelia and Arkhangelsk | low 99 % / low 77 %, barren 22 % (Albeluvisols 77 %, Podzols 20 %) |
| Canadian shield 48–52 N | medium 62 % on SoilGrids' Cambisols (Canada's Brunisols); its Podzols low / barren |
| Amazon | low 97 % / low 75 %, barren 22 % |
| Sahara | none 100 % |

Transects show soil across the flood and the irrigated (oasis) ground and none beyond:
- the Nile at Luxor along 25.70 N;
- the Euphrates and Tigris at Baghdad along 33.30 N.

At a point, Iceland's interior and the Alps at 2700 m are barren beyond the tree line.

Shares of mapped land with topsoil, every 0.25°:

| Tier set | Barren | Low | Medium | High | Black earth |
|---|---|---|---|---|---|
| Vanilla tiers | 6 % | 49 % | 40 % | 5 % | — |
| Realistic tiers | 29 % | 26 % | 38 % | 4 % | 3 % |

Section 4.1's estimates were 10, 48, 37, 5 (vanilla tiers) and 30, 27, 35, 5, 3 (realistic). Of all land, 15 % is arid
without topsoil and 5 % keeps vanilla's rule (colder than −10 °C, or no class mapped).

The Canadian shield is the one place where the class table shows its limit. SoilGrids maps 62 % of it as Cambisols,
which the table makes medium. The measured-properties index (after 2.2) is what would set them apart.

## In game, on dedicated servers (`audit-and-digests.txt`)

`run.sh` runs each check one server after another:
- the test build `2.2.0-rc.11-soil4.1`, Cylinder World 1.1.2 and Hydrology 0.6.2;
- spawn at Kyiv;
- the test-only `soilaudit` mod (not part of Earth), which counts the top block of every land column in 3 × 3 chunks
  around each site in `sites.json`;
- for the digests, Earth's self-test hashing the Terrain pass.

Paths in `run.sh` are this session's; change them to rerun.

Top block of land columns, as share of land columns:

| Site | 1:100 realistic | 1:100 vanilla | 1:10 realistic | 1:10 vanilla |
|---|---|---|---|---|
| Poltava | black earth 98 % | high 98 % | black earth 92 % | high 92 % |
| Zemgale, Bauska | medium 62 %, forest floor 26 % | the same | medium 92 % | medium 92 % |
| Göttingen | medium 45 %, forest floor 49 % | the same | medium 90 % | medium 90 % |
| Finland, Jyväskylä | barren 70 %, medium 11 % | low 70 %, medium 11 % | barren 99 % | low 99 % |
| Amazon, Manaus | forest floor 50 % | forest floor 52 % | low 11 %, forest floor 51 % | low 12 %, forest floor 52 % |
| Nile at Luxor, valley | medium 59 %, high 21 %, barren 11 % | medium 59 %, high 21 %, low 11 % | medium 77 %, high 18 % | the same |
| Nile at Luxor, desert edge | sand 86 %, barren 12 % | sand 86 %, low 12 % | sand 96 % | sand 96 % |
| Baghdad | high 86 % | high 87 % | high 98 % | high 98 % |
| Sahara | sand 98 % | sand 98 % | sand 98 % | sand 98 % |
| Alps, 2700 m | barren 71 %, gravel 15 % | barren 60 %, low 11 % | barren 55 %, gravel 29 % | barren 55 %, gravel 29 % |

Forests show vanilla's `forestfloor` on top: it replaces the top soil block in the vegetation pass. Since the author's
"Yes, soil beneath" (20 November 2026), test build `2.2.0-rc.11-soil4.2` checks what lies under it and what it drops. The
audit calls the game's own drop code on 40 forest-floor blocks per site:

| Site | Under the forest floor | Drops |
|---|---|---|
| Jyväskylä | barren 99 % (realistic), low 99 % (vanilla tiers) | barren or low soil |
| Zemgale | medium 93 %, low 5–7 % | medium or low soil, as beneath |
| Göttingen | medium 92–96 %, peat 7 %, clay 2 % (1:100) | medium soil; on clay low soil; on peat low (vanilla tiers) or barren (realistic), test build soil4.4 |
| Amazon | low 99–100 % | low soil |

Where air lies beneath (about 1 % of forest floors, overhangs) the drop is vanilla's low soil. The third round (test build
soil4.3, with the closest sensible block where no soil lies beneath) checks up to 10 drops for each kind of block
beneath.

The tables above are the second round's; the top blocks are the same as the first round's.

**Climate only against rc.11.** The test build and the installed 2.2.0-rc.11 made new climate-only worlds at 1:10 and
1:25, with the same seed and settings. The Terrain pass's digests at spawn, Poltava and Luxor are identical: 6 of 6 files
at each scale. 1:10 was run again with soil4.2: identical.

## The author's look at rc.12 (21 November 2026), realistic tiers

`SoilTests <root> probe <ModData/earth> lat0 lon0 lat1 lon1 steps` prints every input along a line: the soil class and
the map that gave it, rain and aridity, the flood's level against GLO-90's height, GLWD's wetland share, GUM's unit, the
temperatures and both tier sets' decisions.

- **Nile, west bank near Mallawi (27.7579 N 30.6815 E): a fault, fixed.**
  - The dead-straight sand/barren edge was the oasis rule. GLWD's fresh wetland, in 0.1° cells read bilinearly,
    crossed 0.25 at 30.6825 E, so barren Leptosols began along a cell's line on the desert plateau, 130–145 m high and
    90 m above the valley. The valley floor's flood starts at 30.71 E, where the ground drops to 46 m below the flood's
    46.2 m.
  - The rule acted only beside the Nile and in Mesopotamia; real oases (Siwa, Kharga) have no wetland in that grid. It
    is removed, so the plateau is sand and soil starts where the flood reaches the ground.
  - The step from medium straight to sand is the valley foot: the black land of the flood against the red land of the
    desert. In the real valley that boundary is just as abrupt.
  - In game (1:10, realistic tiers): the author's point is 98 % sand and the valley floor 93 % medium.
- **Mesopotamia (33.2260 N 44.2073 E): right by the data.**
  - The point is 15.8 km from the Tigris (1,141 m³/s) and 17.6 km from the Euphrates (858 m³/s; HydroRIVERS).
  - The flood's level is 35.9 m on ground at 33 m, and the 30″ class is Fluvisols, so it is high.
  - The two rivers are about 34 km apart here and run on their own levees above a near-flat plain. The flood grid
    covers the plain between them (levels 35–47 m), as the rivers' spring floods did; SoilGrids marks salty patches as
    Solonchaks, which are low.
  - The plain is Holocene alluvium, irrigated since antiquity by the canals between the two rivers. After 1258 many
    canals decayed and fields salted, which the map shows only as those Solonchaks.
- **North of Crimea (45.7618 N 33.9430 E): right by the data.** Kastanozems (medium) all along, with a band of Chernozems
  about 0.6 km wide at 45.765–45.770 N (black earth); the steppe's chestnut and southern black soils meet here.
- **Germany: right by the data.**
  - Near Gifhorn the 30″ map is a patchwork of Podzols and Histosols (heath and moor), with Cambisols between them.
  - Near Lüneburg the author stood on Podzols (the Geest's sandy heath) next to Fluvisols of the Elbe and Ilmenau
    marshes, which are medium.
  - Netherlands: the Veluwe and Drenthe are Podzols (barren), and the IJssel valley Cambisols (medium).
- **Scotland and Finland: mostly as the author expects.**
  - Scotland's Highlands and Southern Uplands are Podzols (barren). The eastern Grampians' Cambisols are medium below
    the tree-line rule, which is 900–1000 m there by the warmest month.
  - Finland: Podzols (barren) from 61 to 66.6 N, with Cambisols (medium) in Ostrobothnia around 63.6–64.1 N.

**Resolution of the inputs, in blocks at 1:10 / 1:100:**

| Input | Cell | Blocks |
|---|---|---|
| Soil classes, 30″ (SoilGrids, release egts-v1) | 0.93 km N–S, 0.65 km E–W at 46° N | 93 × 65 / 9 × 6 |
| Baltic soil map, 0.01° | 1.1 km | 111 / 11 |
| 2′ fallback | 3.7 km | 370 / 37 |
| Floodplain level, 15″ | 0.46 km | 46 / 5, but each column's own GLO-90 height decides the edge |
| Rain and temperatures (CHELSA 30″ on the 2′ grid, bilinear; temperature lapsed to the column) | 3.7 km | smooth |
| GUM's dune unit, 0.5° | 55 km | 5,560 / 556 (barren only on Arenosols) |

Class edges are hard, not blended. Each edge is warped with coherent noise by up to 0.8 of a cell, so it wanders but is
never a straight cell line; section 4.1's blended band is not built.
