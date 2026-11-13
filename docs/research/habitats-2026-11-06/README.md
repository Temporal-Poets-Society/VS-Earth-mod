# Habitats: biomes from climate and local site (6 November 2026)

Status: **proposal only.** Nothing here is implemented or decided. The author (6 November): Crimea's steppe has shrubs and trees and looks like Greece; German and Latvian forests look the same; "we need a system that could fix world wide the biomes"; northern pine forests lack a moss layer and broadleaf forests lack leaf litter; all Latvian forests look alike although soil moisture changes the species in real life; there are no swamps; trees in Finland and Latvia are the same size; birch and pine forests are different biomes, and a Latvian meadow is not a steppe.

**Follow-up, 6 November:** [site-drivers.md](site-drivers.md) restates this proposal around the author's drivers: heat, light (solar radiation by latitude, slope and aspect), water supply, site wetness, soil texture, soil chemistry and disturbance. It adds the wetness × richness grid with swamps at its wet end, the spawn-rule order, the Floral Zones column translation and the Earth/content-mod split. It is still a proposal.

## What decides vegetation today

Vanilla has no biomes. Each content rule tests its own climate ranges: soil and grass cover in `blocklayers.json` (temperature, rain, fertility, height); tree species in `treengenproperties.json` (the same plus a forest-noise value); ground plants in `blockpatches` (climate plus forest and shrub noise). A `BiomeMap` exists in every map region and block layers and patches can filter on `biome`, but it is filled with plain noise and no vanilla asset uses it (`GenMaps.OnMapRegionGen`; `ClimateCondition.Biome`, "New biome stuff!").

Earth replaces the inputs, not the model:

| Earth layer | Input | Where |
|---|---|---|
| Climate map | real temperature and one "rain" value from the yearly aridity, lowered up to 25 % for summer drought | `tools/bake_earth.py`, `climate.bin` |
| Forest density | `ForestPotential` = moisture ramp on rain 60→140 × warmest month 8→13 °C, times vanilla noise | `TerrainModel.cs:1311`, `EarthWorldGen.OnMapRegionGen` |
| Shrub density | `ShrubPotential` = ramp on rain 40→100 | `TerrainModel.cs:1327` |
| Tree species | native range (WCVP botanical country) + Earth climate envelope + forest-type weights (boreal, hemiboreal, broadleaf, Mediterranean, montane, hot-dry) | `EarthTrees`, `RegionalTreeCatalog` |
| Ground plants | Floral Zones patches gated by native range; vanilla patches keep climate rules | `EarthPlants` |
| Soil | vanilla by climate; Earth 0048 gives low soil where the aridity index is 0.2 or more | Earth 0048 |
| Bare rock, snow | tree line and slope | `EarthWorldGen.BareRock` |

Nothing describes the site, only the climate cell (0.25°, about 28 km). So every place with the same climate looks the same.

### The two reported cases, measured

| Place | Rain | Coldest month* | Summer drought | Forest potential | Shrub potential |
|---|---|---|---|---|---|
| North Crimean steppe | 91 | 0.8 °C | 0.18 | 0.33 | 0.94 |
| Peloponnese | 90 | 9.9 °C | 0.93 | 0.32 | 0.93 |
| Kherson steppe | 99 | -1.3 °C | 0.03 | 0.48 | 1.00 |
| Kyiv (forest/forest-steppe) | 120 | -2.6 °C | 0 | 0.84 | 1.00 |
| Riga | 125 | -3.1 °C | 0 | 0.91 | 1.00 |
| Hesse | 131 | 2.6 °C | 0 | 0.96 | 1.00 |

\* reduced to sea level, as stored.

The steppe and the Mediterranean differ in winter cold and in the season of the rain. A steppe has cold winters and spring and early-summer rain, so grass wins and fire and grazing kill tree seedlings. The Mediterranean has mild wet winters and rainless summers, so evergreen hard-leaved shrubs and trees win. Earth stores both signals but uses neither for density. Suspicious cells: Rostov reads rain 134 (783 mm), the Hungarian Puszta 139 (809 mm) and Odesa 116; real totals are lower. A threshold change alone would still forest them, so they must be checked first.

Latest tree self-test (2 October, after the last rule change): Germany 75 % English oak, 1 % beech; Baltic states 39 % English oak, 26 % Scots pine, 14 % silver birch, 12 % fir and spruce. Both are oak-led. The catalogue's own target for Latvia is pine 34 %, birch 31 %, spruce 18 %, and beech is scaled ×12 for central Europe but does not show.

## Proposal: habitat = zone × site

Each land column gets a **habitat**: a large-scale *zone* from climate and a local *site* from moisture, soil richness, relief and disturbance. Every vegetation rule then reads the habitat instead of raw climate. This is how foresters already classify land. The Latvian forest site types (Bušs) and the Pogrebnyak grid used across eastern Europe are a grid of soil richness against moisture, applied inside one climate.

### 1. Zone (climate, smooth, worldwide)

Replace the single rain value with the drivers that separate formations:

| Driver | Separates | Source |
|---|---|---|
| Warmth: growing degree days above 5 °C | tundra / boreal / temperate / subtropical; tree size | monthly temperatures, already baked (needs a GDD plane) |
| Coldest month | evergreen broadleaf and beech limits; steppe vs Mediterranean | `tCold`, stored |
| Aridity (P / PET) | desert / semi-desert / steppe / forest-steppe / forest | `rain`, stored |
| Rain season: winter-, summer- or even | Mediterranean vs steppe and savanna; monsoon | needs a seasonality plane (`drySummer` covers only one side) |
| Continentality (warm minus cold month) | hemiboreal vs oceanic; heath vs forest | stored |

Zones (a first list; each is a weight, so borders blend): ice and polar desert, tundra, forest-tundra, boreal, hemiboreal, temperate broadleaf, oceanic heath and broadleaf, forest-steppe, steppe, semi-desert, desert, Mediterranean, warm-temperate humid, subtropical and tropical dry forest, savanna, tropical rainforest, and montane, subalpine and alpine belts by elevation.

Two ways to set them:

- **Rules from the drivers** (like `ForestTypeFactors` today, extended). Works everywhere and blends smoothly, but has to be calibrated.
- **Calibrate against a potential-natural-vegetation map**: Hengl et al. 2018 (1 km, BIOME 6000 classes), RESOLVE Ecoregions 2017, and for Europe Bohn's European natural vegetation map.

Recommendation: rules for the game, with the PNV maps as the test oracle. Each zone's area in a test box must match the map within a tolerance. Rules keep the world independent of one map's errors and licence. The maps catch formula mistakes such as the steppe one.

### 2. Site (local, per column)

| Site factor | Classes | How Earth can get it |
|---|---|---|
| **Moisture regime** | 0 very dry, 1 dry, 2 fresh, 3 moist, 4 wet, 5 waterlogged | Earth's own terrain and rivers: height above nearest drainage (HAND), topographic wetness index (upslope area ÷ slope), slope; drainage from the substrate (sand drains, clay holds); the climate water balance shifts the whole scale. Mapped wetlands override: GLWD v2 (bogs, fens, swamp forest, floodplains) and Earth's mapped peat |
| **Soil richness (trophic)** | A poor, B moderately poor, C fertile, D rich | the ground-column pilot ([geology-columns](../geology-columns-2026-11-06/README.md)): outwash and dune sand A, till loam C, clay and limestone D; WRB soil group; raised bog A, fen C |
| **Relief** | ridge, slope (by aspect), flat, hollow, floodplain | Earth terrain: a south slope is drier and warmer, a north slope cooler and moister |
| **Disturbance and history** | fire-prone, flooded, grazed, cleared in 1318 | fire from dry substrate × summer drought × continentality; flooding from the river model; 1318 land use from HYDE 3.x or KK10 |
| **Stand mosaic** | patch age and succession | low-frequency noise per habitat: birch patches in pine after fire, gaps, edges |

Site factors make one climate show many habitats. Latvia, one climate, by site (Bušs types):

| Site | Moisture × richness | Habitat |
|---|---|---|
| Dry dune or outwash sand | 1 × A | lichen pine forest (sils): pine, lichen and heather floor |
| Fresh sand | 2 × A–B | pine with blueberry and moss (mētrājs, lāns) |
| Fresh loam | 2 × C | spruce with oxalis, some birch and aspen (vēris) |
| Rich clay and loam | 2 × D | broadleaf and spruce: oak, ash, lime, maple, elm, hazel (gārša) |
| Wet mineral soil | 4 × B–C | birch and spruce, sedges (slapjais types) |
| Fen peat | 5 × C | black alder swamp (liekņa), birch fen |
| Raised bog | 5 × A | open sphagnum bog with pools, stunted pine at the edge (purvājs) |
| Floodplain | 3–4 × C–D, flooded | meadows, willow and alder belts |
| Cleared in 1318 | any | fields, mown meadows, pasture; reverts to forest if left |

Grasslands differ the same way:

| Grassland | Why it exists | Look |
|---|---|---|
| Steppe | climate: too dry for trees, cold winter | bunchgrass and feather grass, bare soil between tufts, black soil, no trees outside ravines and valleys |
| Latvian meadow | people mowed and grazed it, or a river floods it | dense sward, many flowers, moist, forest returns if abandoned |
| Mediterranean garrigue | climate plus grazing and fire | low aromatic shrubs over thin rocky soil |
| Tundra and alpine | too cold | low sedges, dwarf shrubs, lichen |

### 3. What a habitat controls

| Layer | Parameter | Game lever |
|---|---|---|
| Canopy | density | Forest map (already patched) |
| Canopy | species weights per habitat: pine on A and dry, spruce on B–C fresh and moist, oak, ash and lime on D, alder on wet and rich, birch as pioneer and on wet poor soil | `EarthTrees` pick (already patched); add moisture and richness ranges per species in `regionaltrees.json`. For Europe, EIVE 2023 indicator values (moisture, nutrients, soil reaction, light) cover trees and ground plants |
| Canopy | **tree size** from site productivity: GDD × moisture × richness (Finland < Latvia; bog pines stunted) | `TreeGenInstance.size`, which Earth already sets (vanilla: random in the species range, nothing more). A canopy-height map such as Lang et al. 2023 can calibrate relative heights |
| Canopy | trunk moss and lichen | `mossGrowthChance`, which Earth already sets |
| Shrubs | density and species: juniper on dry poor, hazel on rich, willow on wet, heather on poor oceanic, maquis in the Mediterranean | Shrub map (already patched), shrub pick |
| Ground | surface block: soil fertility, grass level, forest floor | block layers; vanilla `forestfloor` has 8 grass levels but one litter texture |
| Ground | moss carpet, needle litter, leaf litter, lichen carpet, sphagnum | **new blocks** |
| Ground | herbs and dwarf shrubs (blueberry, cowberry, oxalis, sedges, cotton grass, feather grass) | patch gating by habitat plus native range (extend `EarthPlants`); vanilla berry bushes; Floral Zones; new blocks where missing |
| Water | bog pools, wet hollows, swamp standing water | terrain pass (Earth), a few blocks deep; water behaviour stays with Hydrology |
| Fauna (later) | spawn by habitat | `EarthFauna` |

### 3a. Groundwater, soil moisture and wetlands

The author (6 November): soil moisture has to be modelled, which needs aquifers; wetlands need a real model: "dig a block and get water", and the top block should look different.

**Water table (geography, Earth).** Every land column gets a water-table height in real metres. It is deterministic, computed at generation and also exposed as a query, like `RiverAt`:

- **Base level**: rivers, lakes and the sea; the table meets them.
- **Depth under the surface** grows with the height above the nearest drainage (HAND). How fast it grows depends on recharge and permeability: `depth ≈ HAND × k`. Here `k` is small where it is humid and the subsoil holds water (till, clay: the table follows the land, near the surface), and close to 1 where it is arid or the subsoil drains freely (sand, gravel, karst: the table lies flat at river level, so hills are dry).
- **Perched water**: in humid climates a till or clay layer within ~2 m keeps water above it. That gives Stagnosols and wet forests on flat till plains, which is common in Latvia.
- **Test oracle**: Fan et al. 2013 global water-table depth (~1 km), licence to check; national groundwater maps where they exist.

Underground follows the ground-column depth curve (one block per metre at the top), so a water table 1 m down is the block under the topsoil. Above ground the terrain is ~5–10 m per block and GLO-90 is 90 m, so heights do not show bog microrelief. Hummocks, hollows, pools and levees inside a wetland are generated, not read from heights.

**Soil moisture regime** comes from water-table depth, soil texture (how much water it holds) and the climate water balance:

| Water table | Moisture | Example (Latvian climate) |
|---|---|---|
| above the surface | open water, flooded | bog pool, reedbed |
| 0–0.3 m | waterlogged (5) | mire, alder swamp |
| 0.3–1 m | wet (4) | birch-spruce on gley, wet meadow |
| 1–2 m | moist (3) | spruce, oak-ash on clay |
| deeper, loam | fresh (2) | spruce, mixed |
| deeper, sand | dry (0–1) | pine |

In an arid climate the same depths give drier classes, because the topsoil dries out between rains.

**Digging into groundwater (behaviour, Hydrology).** The world is not filled with underground water blocks. That would flood every cave and cost memory. Instead, when a player opens a block below the water table (Earth's query), Hydrology fills the hole with still water up to the table, at a seep rate. Wells, flooded pits and mines that need drainage below the table follow from that. **Caves (author decision, 6 November, recorded in the ground-column README, decision 6):** caves go by rock type ("Yes, by rock type"):
- No caves in soil or the Quaternary cover at any depth; the cut is the cover base, which the ground column gives per column.
- In rock: karst in soluble rock (limestone, dolostone, chalk, gypsum, salt), lava tubes in basalt, few or none in insoluble rock.
- Caves above the water table are air-filled; below it they are flooded or seep with Hydrology's rule.
- A world-creation toggle keeps vanilla caves available ("we keep toggle in UI for vanilla caves still"); the default is caves by rock type.

Still open: whether vanilla's deep caves stay below some depth for gameplay. This keeps D06/D25: Earth decides levels, Hydrology decides water blocks.

**Wetland types and look:**

| Type | Where | Surface | Water | Plants |
|---|---|---|---|---|
| Raised bog | flat watersheds, cool humid (rain-fed) | sphagnum moss (red, green, brown), hummocks and hollows | dark pools 1–2 deep; table at the surface | stunted pine and birch at the edge, heather, cranberry, cloudberry, Labrador tea, cotton grass |
| Fen | hollows and valleys fed by groundwater | sedge turf, brown moss, mud | table at the surface, small pools | sedges, reeds, scattered birch and willow |
| Swamp forest (alder carr, birch or spruce swamp) | wet rich or poor soil, valley floors | black muck, root hummocks, fallen wood | standing water between hummocks | black alder or birch, ferns, marsh marigold |
| Marsh and reedbed | lake shores, deltas, slow rivers | mud | 0–1 block of water | reeds and bulrush (vanilla reeds), sedges |
| Wet meadow and floodplain | floodplains, cleared wet ground | wet grass | table 0.3–1 m, flooded in spring | grasses, sedges, meadowsweet; willow belts |
| Other zones later | | | | tundra and palsa mires, salt marsh, mangrove, tropical peat swamp, papyrus swamp, flooded forest |

**Placement.** Mapped wetlands set where and what type: GLWD v2 (500 m, bog, fen, swamp forest, floodplain classes) and PEATMAP, plus Earth's mapped peat. Inside them, and for small kettle and valley mires outside them, the water-table model picks the wettest, flattest columns. Peat thickness (a few metres in Latvian raised bogs) belongs to the ground column.

**New blocks the prototype needs:** sphagnum moss (top, with peat under it), sedge and fen turf, black muck, mud, gley (wet grey) soil look, bog pool edge; plants: sedge tussocks, cotton grass, cloudberry, Labrador tea, marsh marigold, meadowsweet where missing. Vanilla gives peat, reeds, cranberry and blueberry.

**Ground column and habitat: who owns what (to agree with the ground-column work):**

| Owner | Decides |
|---|---|
| Ground column (soil and geology) | soil group, texture, topsoil thickness and fertility, Quaternary cover and its thickness, layer permeability, peat body thickness |
| Water table | height per column from terrain, rivers and layer permeability |
| Habitat | moisture regime, habitat, surface variant (grass level, forest floor, moss, litter, wet look), microrelief and pools, plants |

There is no loop: permeability comes from the cover layers, the water table from permeability, and wet soil groups (Gleysols, Stagnosols, Histosols) from the water table. The top block is set in one pass: material from the ground column, surface look from the habitat, so the two do not overwrite each other.

The ground-column session agreed to this split on 6 November, pending the author, with these amendments (recorded in its [README](../geology-columns-2026-11-06/README.md)):
- Every layer carries a permeability class:
  - very low: glaciolacustrine and varved clay, raw clay layers, claystone and shale, unfractured basement
  - low: both tills, marl, silt
  - medium: topsoil, Devonian sandstone (Latvia's main aquifers), fractured basement top
  - high: outwash, beach and dune sand, gravel
  - karst: karstic dolostone, limestone and gypsum
  - peat: special
- The water table is computed from **all layers, including mapped peat**. The author: sand can be waterlogged and peat can be dry; wetness is where the table lies relative to a layer, for any material. Peat's low permeability is what holds a raised bog's water mound above its surroundings. The order avoids a loop: mapped peat (Earth's mapped peat, PEATMAP, GLWD, national peat maps) is an input layer with its own permeability; small unmapped kettle and valley mires are then picked from the table and not fed back. Plants read waterlogging from the table and richness from the soil, both downstream. (This replaces an earlier "mineral layers only" amendment the author overturned.)
- The soil group comes from soil data (group probabilities or HWSD v2); where it cannot tell gleyic from non-gleyic, it reads the water table.
- The ground column's soil pass owns the top-block writer. It composes the block from `GroundColumn.Top(x,z)` (material and thickness in metres) and `Habitat.Surface(x,z)` (grass level, forest floor, moss, litter, wet look). Habitat microrelief and pools edit after that pass.
- The ground column also covers sea floor and beaches; the water table meets the sea there.

**Prototype (built 6 November, [report](prototype/README.md)):** with 90 m heights and ~1 km soil data, terrain alone ranks wetlands only about four times better than chance (best AUC 0.74). Mapped wetlands must set mire extent, and the water table gives the gradient between them. With OSM as the stand-in mapped layer, the full chain gives a plausible Latvian mosaic, from pine sands to clay-plain broadleaf to bogs. The SoilGrids stand-ins for richness and permeability are the weakest link.

Original plan: a Latvia box offline first. Render water table, moisture classes, wetland types and habitats as maps. Compare against Latvian mires (about a tenth of the land; Teiči, Ķemeri, Cena) and the forest register's site-type shares. Then in game with the first blocks and the seep rule.

### 4. Where it lives in the code

- A game-independent `HabitatModel` in `src/Core`, unit-tested offline like `RegionalTreePicker`. It takes (position) → zone weights, moisture class, richness class, relief and disturbance, and returns a habitat ID plus continuous values.
- Existing hooks call it: forest and shrub maps, tree pick, plant gating, block layers, bare rock.
- Optionally write the dominant habitat into vanilla's `BiomeMap`, so other content can filter on `biome` without Harmony. Caveats: `BiomeMap` uses forest-map resolution (coarse), and `ClimateCondition.Lerp` averages biome numbers as if they were quantities. IDs must be read unlerped, so Earth's own hooks should not depend on it.
- `/earth site` prints all inputs and the result at the player's position; an offline renderer draws habitat maps of test boxes for comparison with the PNV maps.
- New worlds only, under a new rules version ([0002](../../decisions/0002-save-compatibility.md)).

### 5. Order

1. **Zones**: check the suspicious rain cells; bake GDD and rain seasonality; replace `ForestPotential` and `ShrubPotential` with zone-driven density. Test boxes: Crimea and Ukraine, Greece, Spain, Latvia, Finland, Germany, Hungary, Kazakhstan. Fixes steppe vs Mediterranean worldwide.
2. **Tree mix audit**: why English oak leads Latvia and beech does not show in Germany; compare per-region shares with forest inventories.
3. **Water table and moisture** (prototype in a Latvia box, offline maps first), **richness** from the ground-column soil data; species moisture and richness ranges; tree size from productivity.
4. **Wetlands**: bogs, fens and swamp forest with their surface blocks, microrelief and pools; Hydrology's seep rule for digging below the water table.
5. **Ground layer content**: moss, litter and bog-plant blocks; habitat-gated herb patches.
6. **1318 land use**: fields, meadows, pasture and heath around settlements.
7. **Fauna by habitat.**

## Decisions for the author

1. Rules calibrated against PNV maps (recommended), or the PNV map baked in directly.
2. Whether new ground blocks (moss, litter, sphagnum, bog plants) are Earth assets or a separate content mod: Earth owns geography; flora content so far comes from providers.
3. Whether 1318 human land use (cleared fields and meadows) belongs in Earth or later in Earth Ruins and villages.
4. Data to fetch and licence-check before anything is baked: Hengl PNV, RESOLVE, Bohn, GLWD v2, PEATMAP, Fan et al. water-table depth, EIVE, HYDE or KK10, a canopy-height map.
5. Groundwater: a query plus Hydrology's seep rule (recommended), or real water blocks underground. (Caves are decided: by rock type, with a vanilla toggle; only deep vanilla caves for gameplay are open.)
6. The ownership split with the ground-column work (section 3a).
