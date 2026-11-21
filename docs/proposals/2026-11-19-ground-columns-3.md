# Ground columns 3: data, algorithm, data fusion, code, checks

Status: **agreed; phase 0 done** (20 November 2026, on branch `claude/ground-phase0`, baseline in `docs/research/ground-baseline-2026-11-19` there), written by Claude as maintainer (D72) after the author's questions: how the
order of layers is decided when Namibia, Vietnam, Siberia, Norway, Latvia and Chile have different histories, how
Baghdad gets fertile soil, how a column is judged without data to compare it with, and what to do where nobody has
mapped the ground. It makes section 9 of [the ores plan](2026-11-18-ores-next-model.md) concrete (step 3 of the
author's order: EA-0166, EA-0167, EA-0168, EA-0176, EA-0180). Nothing here is built.

Agreed by the author so far (19 November 2026):
- Topsoil is part of the plan (section 4.1), with a fallback: "Climate only", today's behaviour.
- Two soil systems on one ranking: vanilla tiers (four blocks, barren only on inhospitable land) and realistic tiers
  (five, Terra preta lying naturally on the black earths); both follow the real soil map, not vanilla's climate.
- The Terra preta block keeps its name.
- Sunlight for crops is a separate mechanic, apart from soil (idea EA-0196).
- The public 1:10 server does not wait for this (section 9, item 4).
- The "Soil" choice defaults to "Climate only" until the author's bench look (item 6).
- No reach targets: clay and the other materials lie where they really are; where they are rare, "such is life"
  (item 3). Gravel is not critical; clay is, so its census is reported.
- The grading of every soil class into both tier sets (section 4.1, item 7).

- The rest of the plan (the column as events in time order, the data, the checks against measured columns, the
  phases), with phase 0 first ("Yes, phase 0 first").

From 2.2.0-rc.11 (the author, 20 November 2026; EA-0197, decision 0104) the world creation screen no longer offers
ground columns, Ground depth or Earth's ore maps, as they need this work; saved worlds keep what they chose, and both
stay reachable through an experimental, off-by-default server setting in `ModConfig/earth.json` for work on branches.
The topsoil part (section 4.1) still matters for every world: vanilla-rock worlds are the default.

Still open: items 1 and 5 of section 9 and the 30″ soil file in item 6, which wait for the phase that needs them. Until
Earth 2.2 ships, the code is on its own branch (`claude/ground-phase0`).

## 1. What "correct" means

Nobody knows the ground column by column, not even in Denmark: between boreholes it is interpolated. So the target is
not every column true. It is:

1. **The right kind of ground for the place's position and history, everywhere**: valley floors with river sediment,
   scraped shields with thin till, frozen silt in Yakutia, dunes on dunes, a gravel skin on a gravel plain.
2. **Thicknesses within the measured range** for that kind of place.
3. **Agreement with measured columns** (boreholes, well logs, soil profiles) within an error that is measured and
   published per kind of landscape, and no kind of landscape getting worse from one version to the next.
4. **No contradiction with what is known** (no till outside the ice limits, nothing frozen outside permafrost), no
   seams at data tile edges.
5. **Every vanilla material within reach**: clay, gravel, sand, peat, fire clay, flint, salt and bog iron at measured
   distances from spawns (the materials contract).

Resolution: landscape position comes from GLO-90 heights (90 m) and Earth's own rivers and lakes, which the game has
everywhere; history and climate from 1 km to 0.5° maps, which change slowly across a region. Below 90 m nothing is
known anywhere; at 1:10 that is 9 blocks, so detail there is natural-looking variation, not data.

## 2. What today's ground does at the author's places

The offline probe of the rocks audit (`docs/research/rocks-ground-ores-audit-2026-11-18/scripts/ground`), run on
`main` for 20 × 20 km around each place (aridity set by hand for the three deserts, no temperature):

| Place | Ground columns today | Published descriptions (to be replaced by data points) |
|---|---|---|
| Namib at Gobabeb | 60 m dune sand on 99 % of the area | Gobabeb lies where the dune sea meets the gravel plain: a thin gravel and crust skin on schist and granite |
| Windhoek | 0.9 m soil on rock | thin soil on rock |
| Vietnam, basalt plateau | 2 m regolith, 6 m laterite, 52 m saprolite; very low topsoil | deep red basalt soils, farmed for coffee |
| Red River delta | lake clay; very low topsoil | delta silt, rice land |
| Lena at Yakutsk | 32 m frozen river sand, silt and gravel | frozen river terraces |
| West Siberia | 8 m peat on clay and sand | peat plain |
| Sognefjord | 8 m till everywhere, beside rivers too | mostly bare rock, gravel on valley floors |
| Trondheim | sea clay on till | sea clay below the old shoreline |
| Riga, Zemgale | river sand or lake clay on till on Devonian rock | Latvian survey data (reviewed regional input) |
| Atacama at Calama | 80 m of the one river recipe: silt, blue clay, sand, gravel | gravels with gypsum and salt crusts |
| Santiago | 2 m regolith on sandstone | hundreds of metres of river gravel fans |
| Baghdad | 43 m river sediment; topsoil only on the floodplain (no topsoil on dry desert ground) | fine Tigris silt and clay; salt flats |

Two causes show already: river sediment is one recipe whatever the river or climate, and what the cover is comes from
0.5° cells (Gobabeb's cell is "wind-blown sand", so the gravel plain is dunes). The six causes are in section 9 of the
ores plan.

## 3. Data

### 3.1 What the generator reads

Fine: known everywhere at 90 m to 1 km. Coarse: 0.5° or region-wide. "In Earth" means Earth already reads it.

| Role | Data | Resolution | Status |
|---|---|---|---|
| Landscape position: relief, slope, hollow or crest | GLO-90 heights (the 8 GB store) through `GroundRelief` | 90 m | in Earth (runtime) |
| Height above the nearest river, river size | Earth's rivers: `RiverNetwork.Nearest` gives distance, discharge `Q` and water top | blocks | in Earth (runtime); not yet read by the ground |
| Floodplain | `FloodplainGrid` (natural flood of rivers with dry-season water) | ~ 1 km | in Earth (soil rules 3) |
| Lakes, sea, shore class | terrain model, OSM shore classes | blocks | in Earth |
| Wetland and peatland | GLWD v2 combined classes (`wetlands.bin`) | 15″ | in Earth (plants); not yet read by the ground |
| Climate: temperature, rain, aridity, dry season | CHELSA and Earth's climate set | 30″ | in Earth |
| Permafrost | Obu et al. 2019 | 1 km | in Earth (30″ and 2′) |
| Ice-age ice limits | Batchelor et al. 2019 (older, LGM) | 2′, Northern Hemisphere only | in Earth |
| Soil class, clay, pH | SoilGrids 2.0 WRB group | 30″ | in Earth |
| Depth to bedrock | Shangguan et al. 2017 | 30″ | in Earth |
| Surface sediment type | GUM (Börker et al. 2018) | 0.5° | in Earth (decides the recipe today) |
| Bedrock, provinces, era | regional geology (GLiM, GUM, envelopes), Hasterok 2022, Chorlton 2007 | 0.5° / 2′ | in Earth |
| Reviewed regional data | Latvia Quaternary thickness, Baltic SoilGrids, EMODnet seabed, Baltic basement | survey scale | in Earth |
| Regolith versus sediment thickness, uplands versus lowlands | Pelletier et al. 2016 | 1 km | new (section 3.4) |
| Soil texture and stones (grain size of the cover) | SoilGrids 2.0 sand, silt, clay, coarse fragments | 250 m → 30″ | new |
| Loess | GUM's loess class | 0.5° and polygons | in Earth (as a cover unit) |
| Ice-rich silt (yedoma) | IRYP v2, Strauss et al. | vector | new |
| Old shorelines (sea clay below them) | Swedish, Norwegian and Finnish marine-limit maps | national | new |
| Southern ice limits (Patagonia, New Zealand, Tasmania) | none found | | gap |
| Dune fields | Gunn 2026 dune mask, placed inside each cell by the ridges in GLO-90 relief | 0.1°; 90 m | new |

### 3.2 What the generator is checked against (offline only, never shipped)

Measured columns: a place, and layers with depths down to bedrock or as far as the hole went.

| Kind | Sources | Covers |
|---|---|---|
| Borehole and well logs, whole columns | national databases (section 3.4: USGS, Sweden, Denmark, Netherlands, Norway, Finland, Australia, Canada) | dense in northern Europe, North America, Australia |
| Soil profiles, top 1-2 m | ISRIC WoSIS | worldwide, including Africa and the tropics |
| Frozen columns | GTN-P permafrost boreholes | Siberia, Alaska, Canada |
| Published sections | papers and survey reports, entered by hand with their citation | where the databases are thin (Namib, Atacama, Mekong, Mesopotamia, Siberian valleys) |

Rule: a check uses data the generator did not read. Depth to bedrock is an input (Shangguan, trained on wells), so a
well depth that went into it proves only that the code read the map; those checks count as consistency, not truth.
Where only one source exists, the check is an invariant (section 5.6).

### 3.3 Gaps and best guesses

Where no measured columns exist (most of Siberia, the Sahara, Amazonia, Central Asia's mountains), the column is the
same rules on the same fine data. The error expected there is the error measured on the same kind of landscape
elsewhere (frozen river lowlands in Alaska and Canada for Siberia's; hyperarid plains in the Atacama and Namib for the
Sahara's). A kind of landscape with no measurement anywhere is listed as a best guess, with the published sections it
rests on.

### 3.4 Source check (19 November 2026)

Checked on the web for this plan (licence, access, resolution). "Ship" means a file derived from it may go in Earth's
downloads; validation data stays offline and is only cited.

New inputs:

| Need | Choice | Resolution, coverage | Licence | Notes |
|---|---|---|---|---|
| Regolith and sediment thickness, upland versus valley floor | Pelletier et al. 2016 (ORNL DAAC 1304, doi:10.3334/ORNLDAAC/1304) | 30″, 60° S-90° N, capped at 50 m | NASA open data, cite | 985 MB; free Earthdata login |
| Grain size of the cover | SoilGrids 2.0 sand, silt, clay, coarse fragments (doi:10.5194/soil-7-217-2021) | 250 m, 1 km aggregates, 0-200 cm | CC BY 4.0 | trained on WoSIS profiles (so WoSIS cannot judge it) |
| Peat extent | GLWD v2 peat classes (in Earth); PEATMAP (doi:10.5518/252) as a cross-check | 15″; vector | CC BY 4.0 | extent only, no depth |
| Loess | GUM's loess class (in Earth) | 0.5° and polygons | CC BY 3.0 | Börker 2020 and Lehmkuhl 2021 loess maps are non-commercial: checks only |
| Ice-rich silt (yedoma) | IRYP v2, Strauss et al. (doi:10.1594/PANGAEA.940078) | vector, Siberia, Alaska, Canada; 1,124 points | CC BY 4.0 | |
| Dune fields | Gunn 2026 global dune mask (doi:10.1038/s41467-026-75466-y, github.com/geomorphlab/dune-world) | 0.1°, dunes on about 7.8 % of land | MIT / CC BY 4.0 | where inside a cell: the ridges in GLO-90 relief |
| Old shorelines (sea clay) | Sweden SGU shore-displacement model (CC0), Norway NGU Marin grense (NLOD), Finland GTK isobases (terms unclear), Pohjola 2019 points (CC BY 4.0) | national | as listed | no Canadian national layer; the glacial-isostasy model ICE-6G_C has no stated licence |
| North American ice retreat | NADI-1, Dalton 2023 (doi:10.5281/zenodo.8161764) | North America | CC BY 4.0 | could replace Batchelor there |
| Siberian Quaternary deposits | USGS, Bryant 2017, East and Central Siberia (doi:10.5066/F7VT1Q89) | 11 Russian maps, 1:50k-1:500k | CC0 | VSEGEI's 1:2.5M Quaternary map: no public download or terms found |
| Height above the river | computed by Earth from GLO-90 and its own rivers | 90 m, runtime | Earth's own | MERIT Hydro's HAND: CC BY-NC or ODbL and no redistribution of originals; not used |
| Landforms | computed by Earth from GLO-90 at runtime | 90 m | Earth's own | Geomorpho90m (CC BY 4.0) as a check |
| Southern ice limits | none found | | | Ehlers and Gibbard's global limits are no longer downloadable: a gap |

Inputs Earth already ships, with terms to settle (section 9, item 5): Shangguan's depth to bedrock is ODbL 1.0, share-alike
(a derived database must be ODbL too); Batchelor et al.'s ice-sheet shapefiles have no licence on OSF (the article is
CC BY 4.0). Earth's `geology-attribution.txt` cites both without terms.

Validation (offline):

| Source | What | Points, coverage | Licence | Tests |
|---|---|---|---|---|
| WoSIS snapshot 2023 (doi:10.17027/isric-wdcsoils-20231130) | soil profiles with horizons | 228,000 profiles, 174 countries; rarely to bedrock | CC BY or CC BY-NC per profile | topsoil, peat, crusts, top 2 m. Not soil class or texture: SoilGrids learned from these |
| USGS standardised lithologic logs, glaciated US (Reddy 2018, doi:10.5066/F71R6PQG) | well logs in standard classes | 1,565,051 | CC0 | till, outwash, lake clay, depth to bedrock |
| Minnesota CWI, Kansas WWC5, Ontario WWIS, Alberta AWWID | well logs | 280,000 to 720,000 each | MN, KS unstated; Ontario OGL; Alberta conflicting | as above |
| Sweden SGU well archive and 10 m soil-depth model | depth to bedrock, layers | about 800,000 wells | CC0 | thin till, sea clay, scraped shield |
| Denmark GEUS Jupiter | well logs | over 280,000 | free use with attribution | till, outwash, clay |
| Netherlands BRO and DINO | borehole descriptions | over 425,000 | CC0 | delta, peat, river and sea sediments |
| Norway NGU GRANADA | wells, depth to bedrock | | NLOD | scraped shield, sea clay |
| Finland GTK | boreholes, clay depths | | open (CC BY compatible) for these | till, clay |
| UK BGS borehole index | locations, depths | over 1 million | OGL; logs as scans | depth only unless licensed |
| Australia NGIS | bore lithology logs | over 900,000 | mostly CC BY per owner | deep weathering: saprolite, laterite |
| IGRAC GGIS | well records by country | patchy | per provider | Africa, Asia where any exists |
| GTN-P, IRYP points | permafrost temperatures, active layer; yedoma points | about 1,100 boreholes | CC BY 3.0 / 4.0 | frozen ground (no layers) |
| Popovs et al. 2022 (Zenodo 7053754) | Latvian Quaternary thickness from over 20,000 boreholes | 250 m | CC BY 4.0 | an input already (consistency only) |

Depth to bedrock in North America and Europe overlaps the wells Shangguan trained on (about 1.6 million, not
published), so it counts as consistency there; layer sequences are independent.

**Where no open measured columns exist**: Russia and Central Asia (maps and permafrost points only), China (6,400
depth-to-bedrock points), South and Southeast Asia, Africa and South America below 2 m, the Sahara, Arabia and the
Middle East, tropical saprolite outside Australia. There the column is a best guess on the rules measured elsewhere
(section 3.3), and published sections are entered by hand.

## 4. Algorithm: the column as events in time order

Younger deposits lie on older ones everywhere, so the order is the same on every continent. What differs is which events
happened at a place, how much each left, and what each removed. Each event reads its own data; none of them is chosen by a
list of regions.

| Order | Event | Acts where (data) | Leaves | Removes |
|---|---|---|---|---|
| 1 | Bedrock | regional geology (hand-off), or the column's template | rock | |
| 2 | Deep weathering | land never under ice, warm and wet now or long ago, stable uplands and plateaus (old surfaces) | saprolite, laterite cap (seasonal tropics), residual clay | |
| 3 | Glaciation | inside the ice limits (older, LGM) | till (thick on lowlands and soft rock, thin or none on crystalline uplands), knob till | the weathering below (scraped shields) |
| 4 | Ice retreat | inside or at the ice limits | outwash sand and gravel (valleys, margins), lake clay (low ground of ice-dammed lakes), sea clay (below the old shoreline) | |
| 5 | Cold-climate ground | cold periods, outside the ice | loess (loess map), ice-rich silt (yedoma map), slope debris | |
| 6 | Rivers | low height above the nearest river, floodplains, fans at mountain fronts | river sediment: gravel where rivers are steep or near mountains, sand mid-course, silt and clay on flat plains and deltas (river slope, discharge, SoilGrids texture) | top of older layers where a river cut in (terraces) |
| 7 | Lakes and coasts | present lakes and their former extent, shores, deltas | lake mud and clay, beach sand, delta silt | |
| 8 | Wind | dune fields (Gunn's dune mask, the ridges in GLO-90 relief, aridity), sand sheets | dune and sheet sand | |
| 9 | Slopes | slope bases and hollows; crests and steep slopes shed | colluvium | cover on crests and steep slopes (today's relief factor) |
| 10 | Peat | wet hollows and flats (GLWD v2 peat classes, soil class Histosols) | peat, its thickness by climate and age | |
| 11 | Dry-land crusts | arid and semi-arid ground (aridity, Calcisols, Gypsisols, Solonchaks) | calcrete, gypsum crust, salt flats | |
| 12 | Soil | everywhere on land | topsoil from the soil class, the climate and what it formed on (river silt gives loam; dune sand gives none) | |
| — | Permafrost | permafrost ground | frozen variants of each layer below the active layer | |

Examples:
- **Namib gravel plain**: bedrock, little weathering (dry), no ice, no river, no dune ridges → a thin gravel and crust
  skin on schist. The dunes 5 km south get dune sand because their relief has dune ridges.
- **Norway**: bedrock, weathering removed by the ice, thin till on the uplands, sea clay below the old shoreline, river
  gravel on valley floors, peat in hollows.
- **Vietnam**: deep saprolite on the basalt plateau (never iced, warm and wet for a long time); river silt in the delta.
- **Baghdad**: tens of metres of fine river silt (flat plain, low river slope), salt flats in the closed hollows (arid),
  topsoil on the floodplain from river silt.

### 4.1 Topsoil: one decision for every world

Two systems decide topsoil today, and they disagree:

| World | What decides | Soil class | Floodplain grid | High fertility (`soil-compost`) |
|---|---|---|---|---|
| Ground columns | `ground-rules-v1.json` `soils`: each SoilGrids soil class gives a fertility and a humus depth; arid land has none (soil rules 2) unless it lies in a river's flood (soil rules 3) | yes | yes | Chernozems, 1.2 m: the black-earth belt of Ukraine and southern Russia |
| Vanilla rock (the default since 2.0.1) | vanilla's block layers from the climate map's rain and temperature; Earth lifts land that is not arid to low (`SoilFertility`, decision 0048) | no | no: only the green belt along rivers | never: vanilla's natural layers stop at medium |

`soil-site.bin` (plants, fire, trees) reads soil-class shares from the same SoilGrids map; it lays no blocks.

**One decision instead.** A `Topsoil` part of Core, beside `SoilFertility`, answers for any land column whether it has
topsoil, its fertility (very low, low, medium, high and, in realistic tiers, black earth) and its humus depth. Both
kinds of world call it:

- **Inputs**: the measured topsoil properties (organic carbon, cation exchange, pH, nitrogen, texture; SoilGrids 2.0,
  new) and the soil class (30″ where the file is there, else the 2′ map Earth ships), aridity, temperature, the
  floodplain grid, wind sand, salt flats, landform (crest or hollow), and in ground-column worlds what the soil formed on.
- **Rules**, in one file (`soil-rules-v4.json`): the class table, starting from today's ground-column table (Chernozems
  high and 1.2 m, Kastanozems and Phaeozems medium, Podzols and Arenosols very low...), then the place: arid land none,
  except in a river's flood and at oases; dunes and beaches none; salt flats very low; fresh river silt on floodplains
  and deltas at least medium; crests thinner, hollows and valley floors thicker; vanilla's cold limit as now.
- **Ground-column worlds**: the soil event (12 above) calls it. What the soil formed on sets its texture (river silt
  gives loam), not a second fertility table.
- **Vanilla-rock worlds**: Earth's fertility patch gives vanilla's layers the fertility of the class (very low, low,
  medium), so vanilla still lays its own blocks and depths. High and black earth are put in by a top-block pass after
  vanilla's layers: where the decision says so, vanilla's `soil-medium` becomes `soil-compost` or `soil-high` with the
  same grass (blocks ground columns can already lay).
- **Class edges**: at 2′ (3.7 km; 370 blocks at 1:10) a class boundary would run in steps, so boundaries are blended
  over a band with coherent noise, as rock contacts are.
- **No parallel system**: the ground-column soils table and soil rules 1-3 stay only for the worlds that saved them, and
  nothing is added to them; `soil-site.bin` stays the plants' view of the same classes.

**Which soils are high.** Measured on SoilGrids' 30″ map (every 0.25°, by area), the class table makes only Chernozems
high: 2.4 % of mapped land, in Kazakhstan and West Siberia's steppe (19 % of it), Ukraine and Moldova (19 %), southern
European Russia (15 %), the US Great Plains and Midwest (10 %), north-east China and Mongolia (7 %), the Canadian
Prairies (6 %), the Danube and Pannonian plains (3 %) and elsewhere (20 %). Other soils known for natural fertility are
medium: Phaeozems (1.7 % of land; the Pampas, the US Corn Belt, north-east China: prairie black soils), Fluvisols (1.7 %;
river plains and deltas, the Nile and Mesopotamia among them), Andosols (0.9 %; volcanic), Nitisols (0.5 %; the red
highland soils of East Africa and Java) and Vertisols (2.8 %; the black clays of the Deccan, Sudan and Ethiopia, rich
but heavy). Mesopotamia's alluvium was among the most productive farmland of antiquity, its fertility renewed by river
silt and irrigation, not by humus; its southern plains salted up already in antiquity (Jacobsen and Adams 1958), and by
1318 many canals had decayed.

**One world ranking.** The game has four natural soils (farmland nutrients 5, 25, 50 and 65: very low,
low, medium and high, `soil-compost`) and one crafted, terra preta (`soil-high`, 80). Today's class table splits the
mapped land 42.6 % very low, 17.8 % low, 37.2 % medium and 2.4 % high, before its one-class bump for clay of 25 % and
more with pH 6 and above, and before the desert rule. The class name alone cannot tell Zemgale's clay loams from
Latgale's sandy till when both are Luvisols, or the Nile's silt from desert soil where the map calls the valley
Leptosols. So the scale comes from measured topsoil properties per place: SoilGrids 2.0's organic carbon, cation
exchange capacity, pH, nitrogen and texture for 0-30 cm (CC BY 4.0, 1 km), combined into one fertility index, with the
class as a check and the floodplain grid for valleys too narrow for the map. All land with topsoil is ranked on that
index and cut into blocks at world shares the author sets (two ways, below), so a place is ranked by world
comparison: Zemgale medium, Latgale low or very low, the steppes at the top.

**What fertility does in the game** (vanilla 1.22.7, `BlockEntitySoilNutrition.GetGrowthRate`,
`BlockEntityFastForwardGrowth`): a crop grows at its moisture factor times a step for the nutrient it needs: 0.1 at 5
and below (barren), 0.3 above 5, 0.6 above 20 (low, 25), 0.9 above 35 (medium, 50), 1.0 above 50 (high, 65), 1.1 above
75 (terra preta, 80). Fallow land recovers only up to its soil's own fertility; fertiliser raises it. Separately, growth
slows in weak sunlight and pauses more often the further the temperature falls below a threshold, so warm land grows
for more of the year. Climate and water are therefore the game's own; the soil scale ranks **nutrients only**, and a
sunny place is not made more fertile for its sun (that would count the climate twice). This is also how farming works:
sunlight, warmth and season length set what a crop can yield, and water and nutrients decide how much of it is
reached; a poorer soil in a warm place with water and manure or flood silt can out-yield a richer soil with a short
cold season. The Nile is high because its flood brought nutrients, not because of its sun. Warmth is not light: a
heated greenhouse in Iceland still yields little in its dark, short and weak light season (the author). Vanilla's
light factor reads the block's light level, not the season's sunlight; real sunlight for crops is a separate mechanic,
filed as an idea (EA-0196), and stays out of the soil.

**A, B and C ranked by their natural fertility**, best first: Chernozems (the deepest black humus, neutral and rich in
bases, the benchmark); Phaeozems (the same humus, a little more leached; the Pampas and the Corn Belt farm as well as the
steppes); river silt a flood renews every year (as good as the best where it is loamy and fresh, but Fluvisols also
include sandy, salty and acid mangrove soils, so only where the properties say so); Andosols (rich in organic matter and
easy to work but locking up phosphorus); Nitisols (the best tropical soils, deep and well structured, with moderate
nutrients); Vertisols (rich clays, hard to work). Where each falls is section 9, item 7.

**Two soil systems on the one ranking** (the author, 19 November 2026). Both read the same measured index and the same
rules, so both place soil by Earth's real soil map and landscape (soil classes, measured properties, floodplains,
dunes, salt flats, slope); they differ only in the blocks they use and where the cuts fall. Placing by vanilla's own
climate rule is the third choice, "Climate only":

| | Vanilla tiers | Realistic tiers |
|---|---|---|
| Blocks | barren, low, medium, high (`soil-compost`, 65) | barren, low, medium, high, black earth (`soil-high`, 80) |
| Barren (5; crops at 0.1 until fertilised) | inhospitable land only: polar and alpine ground, Iceland's sand and lava fields, bare rock, dunes, salt | the poorest soils by measurement, about a quarter of land with topsoil: podzol and dune sands, thin soils on rock, salt and gypsum soils, the most leached tropical soils |
| Top | high: the black earths and the best river silt | black earth: Chernozems and the best Phaeozems (about 3 % of land); high: the other Phaeozems, fresh river silt, the best volcanic and red highland soils |
| Shares | near vanilla's own spread (measured on a vanilla world) | the world's range: about barren 25 %, low 30 %, medium 35 %, high 7 %, black earth 3 % |

**Grading, class by class** (agreed by the author, 19 November 2026; the share is of SoilGrids' mapped land, `soilshare` probe of 19
November 2026). The class is the starting point; within a class the measured index moves a place up or down (Zemgale's
clay loams and Latgale's sandy till are both Luvisols or Albeluvisols). Arid land has no topsoil in either system, so
most desert sands, thin desert soils, gypsum and salt soils never show a tier.

| Soil class (share of land) | What it is | Realistic tiers | Vanilla tiers | Today's table |
|---|---|---|---|---|
| Chernozems (2.4 %) | deep black steppe humus | Terra preta | high | high |
| Phaeozems (1.7 %) | prairie black soils, a little leached | best: Terra preta; rest: high | high | medium |
| Fluvisols in a river's flood (≤ 1.7 %) | fresh loamy silt renewed by floods | high (salty: low) | high (salty: low) | medium |
| Andosols (0.9 %) | volcanic, rich in organic matter | high | medium (barren on Iceland's sand and lava fields) | medium |
| Nitisols (0.5 %) | deep red tropical highland soils | high | medium | medium |
| Cambisols (17.9 %) | young brown soils | medium | medium | medium |
| Luvisols (8.6 %) | clay-enriched forest and farm soils | medium | medium | medium |
| Kastanozems (3.1 %) | dry-steppe chestnut soils | medium | medium | medium |
| Vertisols (2.8 %) | rich heavy cracking clays | medium | medium | medium |
| Fluvisols outside floods, Umbrisols | river soils above the flood; dark acid soils | medium | medium | medium |
| Acrisols (8.9 %) | leached red tropical soils | low | low | very low |
| Cryosols (5.6 %) | permafrost soils | low (polar desert: barren) | low (polar: barren) | low |
| Regosols (3.3 %) | weakly developed soils | low | low | low |
| Calcisols (3.0 %) | lime-rich dry soils | low | low | low |
| Albeluvisols (1.9 %) | bleached clay-sand forest soils (Latgale) | low | low | low |
| Gleysols (1.4 %) | waterlogged soils | low | low | low |
| Lixisols, Alisols, Planosols, Solonetz (2.6 %) | leached, dense or sodic soils | low | low | low |
| Ferralsols (8.8 %) | the most leached tropical soils (Amazon, Congo) | barren | low | very low |
| Podzols (3.6 %) | acid bleached sands of the north | barren | low | very low |
| Arenosols (8.7 %) | sands | barren | low (dunes: barren) | very low |
| Leptosols (7.7 %) | thin soil on rock | barren | barren on bare rock and high ground, else low | very low |
| Plinthosols, Durisols (0.1 %) | ironstone and silica hardpan soils | barren | low | very low |
| Gypsisols, Solonchaks (1.6 %) | gypsum and salt soils | barren | barren | very low |
| Histosols (3.0 %) | peat | peat on top, no topsoil | same | peat |

Shares of land with a tier, before the measured index moves places: realistic about Terra preta 3 %, high 5 %, medium
35 %, low 27 %, barren 30 % (less where deserts have no topsoil); vanilla tiers about high 5 %, medium 37 %, low 48 %,
barren 10 %.

Black earth is vanilla's `soil-high` block and keeps its name "Terra preta" (the author, 19 November 2026); Portuguese
terra preta and Russian chernozem both mean "black earth". Terra preta itself (Amazonian dark earth) is a made soil: charcoal from slow
burning, pottery sherds, bone, ash and waste, built up over centuries beside Amazonian rivers. It is not richer than the
best natural black soils; it is remarkable for staying fertile in the tropics, where the native soils are among the
poorest. Vanilla's recipe (compost, charcoal, bonemeal) stays in both systems; in realistic tiers the same block also
lies naturally on the black earths (until now it was never generated: the author, 6 November 2026). Placing it at the
Amazon's known dark-earth sites, which existed in 1318, would be a separate choice, like ruins.

**The "Soil" choice** on the Earth tab (`earthSoil`, and in the server config): "Earth soil, vanilla tiers", "Earth soil,
realistic tiers", or "Climate only", today's behaviour in both kinds of world and the fallback the author asked for.
Soil rules 4 and the choice are saved per world (`EarthParams.SoilRules` and a new field, the next free number when it
is built), so worlds made before keep theirs. The default stays "Climate only" until the author's bench look.

**Checks**: WoSIS topsoil depth and organic carbon by class and landform (they test our depths and fertility rules, not
the classes, which SoilGrids learned from WoSIS); the share of land in each fertility class per climate zone; named
places: Poltava steppe high, the Nile at Luxor and Baghdad fertile across the flood and bare beyond it, the Red River
delta at least medium, Amazonia low, boreal podzol very low, the Sahara none; then the author's look on the bench.

**When**: it needs only the class table, the floodplain grid and the fertility patch, not the event engine, so it can
come right after phase 0 (row T below). It is also the soil half of EA-0188 (the Nile and Mesopotamia report).

## 5. Data fusion

1. **Each source has a stated role and a rank.** Reviewed regional data (Latvian survey, any national Quaternary map
   with usable terms) outrank fine global maps (SoilGrids, permafrost, GLWD, depth to bedrock), which outrank coarse
   maps (GUM, GLiM), which outrank the priors measured per kind of landscape. A source answers only its own question:
   GUM says how likely wind sand or ice-lake clay is in a region, not which column has it.
2. **Coarse maps give odds, fine data decide the column.** A GUM "wind-blown sand" cell or a 0.1° dune-mask cell raises
   the odds of dunes; the dune ridges in the 90 m relief and the aridity decide where they are. A GUM "lake" cell raises the odds of lake
   clay; hollows below the old lake level decide where it lies.
3. **One thickness budget.** The total cover comes from depth to bedrock (Shangguan) and Pelletier's upland and lowland
   thicknesses, moved by landform as today (crests shed, hollows collect, total kept). It is shared among the events
   present at their measured median shares for that kind of landscape, each within its measured range.
4. **Grain size from the river and the ground.** River sediment's sand, silt, clay and gravel come from the river's
   slope and discharge (`RiverNetwork.Nearest`), distance from mountains and SoilGrids texture, not from one recipe.
5. **The column rounded as a whole**, largest remainder first, with at least one block for a layer that matters in play
   (gravel, clay, peat) where its real thickness passes a threshold.
6. **Variation without speckle or seams**: seeded noise fields per event (coherent over tens of blocks), and tile edges
   blended over a few cells.

## 6. Calibration and checks

1. **Reference set** (`tools/ground/build_reference.py`): each source's layer words mapped to about twenty classes
   (topsoil, peat, organic mud, clay, silt, sand, gravel, boulders, till, loess, saprolite, laterite, calcrete, gypsum,
   salt, volcanic ash, frozen variants, bedrock) by a reviewed table per source. Output in `rawdata/ground-reference/`
   (never in the mod), with source, citation and licence per point.
2. **Kinds of landscape** computed from the same fine data the generator reads: glaciated or not, permafrost, climate
   group, landform (crest, slope, plain, valley floor, delta, dune field, hollow), rock group. Every reference point
   and every land sample gets one.
3. **Calibration and validation split by region** (1° blocks alternating, and whole countries held out), so the
   numbers are not tuned on the points they are judged by.
4. **Priors baked from the calibration half** (`tools/ground/bake_priors.py` → `ground-rules-v3.json`): per kind of
   landscape, how often each event leaves something and its thickness quantiles. A changed prior is a reviewed diff.
5. **Scores** (`tools/GroundCheck`): per point, the depth to bedrock (log ratio), the material in 0-1, 1-5 and 5-20 m,
   and whether clay, gravel, sand and peat are present within 5 m; added up per kind of landscape and per region, and
   drawn as world maps of disagreement.
6. **Invariants** over the whole land sample: younger never under older; no till outside the ice limits; nothing
   frozen outside permafrost; no topsoil on dune sand; thicknesses within the measured range for the kind of landscape;
   nothing that crosses a tile edge as a step.
7. **Named places** (the probe's places and more, each with its published section): fixed expectations as tests.
8. **Materials contract**: a census on dedicated servers (as the ore censuses) of each vanilla material's share of land
   and distance from spawns, against targets the author sets.
9. **Gate**: a ground rules version ships when its validation score is better overall, no kind of landscape is worse
   than a set tolerance, every invariant holds and the materials contract is met.

## 7. Code

```
src/Core/Ground/                        pure C#, no game types (the probe and tests compile it)
  GroundContext.cs                      everything an event may read at a column (fine and regional), filled by the caller
  GroundEvent.cs                        Leaves(in GroundContext, budget) and Removes(...) per event, in time order
  Events/Weathering.cs ... Soil.cs      one file per event in section 4
  Permafrost.cs                         frozen variants below the active layer
  ThicknessBudget.cs                    the total, its shares, whole-column rounding
  MaterialClasses.cs                    the twenty classes, their blocks, and the equivalence table (frozen X counts as X;
                                        each Earth rock's vanilla class), read by ore layers, deposits, the pan and the pick
  GroundRules3.cs                       ground-rules-v3.json: priors per kind of landscape, grain tables, thresholds
  GroundExplain.cs                      why each layer is there (event, data values), for /earth ground and the tools
src/Core/GroundColumns.cs               rules 1 and 2 unchanged; At/Build hand rules 3 to the new engine
src/Core/Topsoil.cs                     the one topsoil decision (section 4.1, soil-rules-v4.json), read by the soil
                                        event and by BlockLayerAdjustments (vanilla-rock worlds) with its top-block pass
src/EarthGeology.cs                     fills GroundContext: adds height above the river and river size (RiverNetwork),
                                        curvature (GroundRelief), GLWD peat, Pelletier, texture
tools/ground/bake_*.py                  new 30″ inputs into the ground tiles (.egts)
tools/ground/build_reference.py         the reference set
tools/ground/bake_priors.py             priors from the calibration half
tools/GroundCheck/                      C# console on src/Core (through tests/Earth.Core.Tests.Common): score, global
                                        maps, explain a place, named places
tests/GroundColumnsTests                invariants, named places, rules 1 and 2 goldens unchanged
tests/SavedWorldDigestTests             ground column digests per published version (EA-0166)
```

- **Versions** (EA-0166 first): ground rules saved per world as today (`EarthParams.GroundColumns`), with rules 1 and 2
  frozen by goldens and rules 3 in its own file. New data goes into a new ground data release, so worlds made with the
  current files keep reading them.
- **Runtime cost**: the per-chunk relief, climate and river lookups exist already; the events are table lookups and a
  few arithmetic steps per column. The budget is today's ground pass time per chunk, measured by the ground pass's own
  timers.
- **`/earth ground`** in game: the column under the player with each layer's reason, for the author's checks.

## 8. Phases

Worlds change only in phase 4 and row T, and only new ones.

| Phase | Work | Output | Gate |
|---|---|---|---|
| 0 | Versions and digests (EA-0166); the reference set from the strongest open sources; `GroundCheck`; score and maps of today's rules | research note with the baseline numbers and error maps | none: measures only |
| 1 | The event engine with today's data; whole-column rounding; equivalence table | rules 3 draft | score not below the baseline anywhere |
| 2 | New inputs one by one (height above river, GLWD peat, texture, Pelletier, loess, yedoma, old shorelines, southern ice, dune ridges) | each with its score change | each must raise its kinds of landscape |
| 3 | Priors from the calibration half; materials census on servers; the author's look on the bench | calibrated rules 3 | section 6.9 |
| 4 | Release as ground rules 3 for new worlds | Earth release | the author's word |
| T | Topsoil (section 4.1): soil rules 4 for both kinds of world, with the "Soil" choice as fallback; any time after phase 0 | soil rules 4 | named places pass; the author's look before the default changes |

## 9. Decisions for the author

1. **New ground data release**: the ground pack is downloaded by the game from a GitHub release
   (Temporal-Poets-Society/earth-ground, `egts-v1`) when a world first needs it; new data (SoilGrids properties,
   Pelletier, the other phase 2 inputs) would go up as a new release (`egts-v2`, new file names), which new worlds fetch
   and worlds made before never need. The upload is outward-facing: asked for when phase 2 or the topsoil part needs it.
   Recommended: yes, then.
2. **No new blocks needed** (corrected 19 November 2026): Earth already has its own calcrete (lime crust of dry-land
   soils), gypcrete (gypsum crust), salt crust, lake marl, silt, loess, colluvium, weathered rock, saprolite, laterite,
   terra rossa, till, tephra and frozen ground blocks, and its own bog iron block ("Peat with bog iron"), which only needs
   its drop (vanilla's limonite nugget, EA-0180). Organic lake mud (gyttja) has no block and needs none: lake marl,
   clay or peat stand in.
3. **Materials: no reach targets** (the author, 19 November 2026): clay, gravel and the rest lie where they really
   are; where a material is rare, "such is life". Gravel is not critical; clay is, so the census reports how far clay
   is from spawns, and checks that the ground does not miss real clay (till's clay, lake and sea clays, river banks)
   rather than adding clay that is not there.
4. **The public 1:10 server does not wait** (the author, 19 November 2026): it uses vanilla rock or today's ground
   columns; ground columns 3 and soil rules 4 come after.
5. **Terms of two inputs Earth already ships** (outward-facing): Shangguan's depth to bedrock is ODbL (share-alike),
   so Earth's tiles derived from it would be ODbL too; Batchelor et al.'s ice limits carry no licence. Recommended: name
   ODbL for the Shangguan-derived tiles in the attribution and the earth-ground release notes; ask Batchelor's authors
   for terms (a message the author sends or approves), and use NADI-1 (CC BY 4.0) for North America meanwhile.
6. **Soil choice** (section 4.1): "Earth soil, vanilla tiers", "Earth soil, realistic tiers" and "Climate only", the
   default until the author's bench look (the author, 19 November 2026). Still open: whether vanilla-rock worlds fetch
   the 30″ soil classes (one 21 MB file of the ground pack) or use the 2′ map Earth ships. Recommended: fetch it.
7. **The grading of every soil class** into both tier sets: the table in section 4.1, agreed by the author (19 November
   2026). The black earth block keeps vanilla's name, "Terra preta".
