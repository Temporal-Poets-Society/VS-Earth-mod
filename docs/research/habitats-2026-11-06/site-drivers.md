# Site drivers: what decides a habitat (6 November 2026)

Status: **proposal.** The driver list is the author's direction from 6 November. The way each driver is computed, the grid, the spawn-rule order and the mod split below are proposals. A first code slice (`SiteDrivers` plus `/earth site`) is being built in the `habitat-overlay` worktree and is not committed; see "First code slice". This page extends [the habitat proposal](README.md), sections 1–3a.

The author (6 November):
- Plants depend on soil, temperature, solar radiation and water. Soil moisture and soil type (what lies below: sand, clay, rock) should drive the microbiomes.
- Add swamp biomes, and get several forest subtypes inside one climate, whether in Argentina, Latvia or Japan.
- An aquifer comes later.
- "Light – solar radiation matters per latitude very much."
- Dropping Wildcraft is aspirational, if Floral Zones can supply what is missing.

## The seven drivers

| # | Driver | What it decides | Computed from | Scale |
|---|---|---|---|---|
| 1 | **Heat** | zone, growing season, tree size, tree line | yearly mean, coldest and warmest month at real height → growing degree days above 5 °C (GDD) | climate cell, lapse-corrected per column |
| 2 | **Light** | season length for plants, sunny vs shady slopes, tree line by aspect, snow lying, ground flora under canopy | latitude, day of year, slope, aspect, elevation → clear-sky radiation; canopy closure for the ground layer | per column |
| 3 | **Water supply** | forest vs steppe vs desert, Mediterranean vs steppe | rain (aridity) and dry summer, stored | climate cell |
| 4 | **Site wetness** | dry pine vs fresh spruce vs swamp | water-table depth × how much water the soil holds × slope; the aquifer later feeds the water table | per column |
| 5 | **Soil texture and depth** | drainage, rooting, sand vs clay vs peat vs thin soil on rock | ground columns: cover class, WRB soil group, clay %, Quaternary thickness | per column (0.01° grid now) |
| 6 | **Soil chemistry** | poor vs rich forest, heath, calcareous grassland, salt marsh | ground columns: soil group, pH, clay; coast for salinity | per column |
| 7 | **Disturbance** | dunes, floodplains, fire-prone pine, grazed and mown land | coast distance, river and floodplain height, dry summer × sand, 1318 land use later | per column |

Fertility is not a driver. It stays a game value for farming (`soil-low`, `soil-medium` and so on) and is derived from richness, texture and pH. Placement reads wetness and richness separately: wet-poor (bog) and wet-rich (alder carr) are as different as dry-poor and dry-rich.

### Light in detail

The sun's energy at the top of the atmosphere, summed over a year on flat ground (FAO-56 daily formula, checked by numerical integration over the day):

| Latitude | Year, MJ/m² | 21 June, MJ/m²/day | 21 Dec, MJ/m²/day |
|---|---|---|---|
| 0° | 13,150 | 33.4 | 35.6 |
| 30° | 11,490 | 41.2 | 19.7 |
| 45° | 9,620 | 41.9 | 10.4 |
| 57° (Riga) | 7,830 | 41.4 | 3.6 |
| 66.5° | 6,460 | 41.7 | 0 |
| 80° | 5,540 | 44.7 | 0 |

The yearly total falls by 40 % from the equator to Riga. A June day in Riga gets more than a June day on the equator, because the day is 17 hours long. What changes with latitude is the season, not the summer day. Plants only use light while it is warm enough to grow, so the useful number is radiation during the growing season (days with mean temperature above 5 °C):

| Place | Growing season, days | Growing-season radiation, MJ/m² | GDD5 |
|---|---|---|---|
| Oulu, 65° N | 161 | 5,060 | 1,120 |
| Riga, 57° N | 203 | 6,280 | 1,590 |
| Sapporo, 43° N | 221 | 7,470 | 2,350 |
| Bariloche, 41° S | 243 | 8,330 | 1,330 |
| Kyoto, 35° N | 327 | 10,220 | 3,990 |

(The temperature curve is a sinusoid through the coldest and warmest month, so these are approximations.) Bariloche has a long growing season and plenty of light but few degree days. It is cool and even, so its forests (lenga beech) stay low and slow-growing. Heat and light separate the two cases; one "temperature" value cannot.

**Slope and aspect matter more the further you go from the equator.** A 30° slope compared with flat ground:

| Latitude | Sunny side, year | Shady side, year | Sunny side, growing season | Shady side, growing season |
|---|---|---|---|---|
| 0° | 0.88 | 0.87 | — | — |
| 30° N | 1.14 | 0.62 | | |
| 57° N | 1.47 | 0.50 | 1.20 | 0.61 |
| 30° S | 1.12 (north-facing) | 0.64 (south-facing) | | |

On the equator every steep slope loses a little, whichever way it faces. At Riga's latitude a south slope gets half again as much light as flat ground, and a north slope half as much. In the southern hemisphere the north face is the sunny one.

What light should drive (proposal):
- **Heat on the slope.** A sunny slope acts warmer and a shady one cooler. Convert the radiation ratio into a temperature offset for GDD, the tree line and snow lying. Steep north slopes keep snow and spruce longer; south slopes get oak, pine, steppe plants and vineyards earlier.
- **Dryness.** A sunny slope loses more water. In dry climates it drops one wetness class, which is the steppe-on-south, forest-on-north pattern of forest-steppe and Mediterranean hills.
- **Ground layer under trees.** Light under the canopy depends on the tree species and density. Spruce and beech shade heavily, pine, birch and larch lightly. Shade-tolerant ground plants (oxalis, mosses) go under spruce; heather, lichen and grasses go under pine and birch. This is where Floral Zones' "Forest" column fits (see the spawn rules).
- **Latitude itself** enters through heat and season length, not as a separate number.

Clear-sky first: radiation = (0.75 + 2×10⁻⁵ × elevation) × top-of-atmosphere radiation (FAO-56 eq. 37). Real sunshine also depends on clouds. Earth has no cloud plane yet. A monthly surface solar-radiation layer (for example WorldClim 2.1 `srad`, licence to check) could later replace the clear-sky factor per climate cell. Slope and aspect stay per column.

## Wetness × richness: the site grid

The zone (heat, light, water supply) gives the **species pool**. The site (wetness, texture, chemistry) picks from it. Every zone uses the same grid:

| Wetness \ Richness | A poor | B moderately poor | C fertile | D rich |
|---|---|---|---|---|
| 0 very dry | lichen pine, dune | dry pine | dry oak and pine | steppe slope, dry oak |
| 1 dry | heather pine | pine with bilberry | oak and pine, hazel | oak and lime |
| 2 fresh | pine with bilberry | pine and spruce | spruce with oxalis | oak, ash, lime, maple, elm |
| 3 moist | pine and birch | spruce and birch | spruce, oak, ash on clay | ash and oak, rich herbs |
| 4 wet | wet pine and birch | birch and spruce, sedges | birch, spruce and alder | alder and ash |
| 5 waterlogged | **raised bog** | **transition mire** | **fen**, **swamp forest** (birch) | **alder carr**, **swamp forest** |
| 6 flooded | bog pools | | **marsh**, reedbed | **marsh**, floodplain willow |

Special grounds sit outside the grid and override it:
- **Peat**: bog or fen by richness.
- **Salt**: salt marsh, and mangrove in the tropics.
- **Mobile sand**: foredune and grey dune.
- **Rock or thin soil**: rock pavement, scree, alvar on limestone.
- **Cleared in 1318**: fields, meadows, pasture.

**Swamps are the wet end of the grid**, not a separate system: rows 5–6 in every zone. Which kind of swamp you get depends on the zone's species pool:

| Zone | Bog (A) | Fen (C) | Swamp forest (C–D) | Marsh (6) |
|---|---|---|---|---|
| Boreal, hemiboreal | sphagnum, stunted pine | sedges, birch, willow | black alder, birch, spruce | reeds |
| Temperate broadleaf | sphagnum, rarer | sedges | alder, ash | reeds, bulrush |
| Warm temperate (Japan) | sphagnum at altitude | sedges, reeds | Japanese alder (*Alnus japonica*) | reeds |
| Subtropical, tropical | peat swamp forest | papyrus, sedges | flooded forest, swamp cypress | reeds, papyrus |
| Tropical coast, saline | — | — | **mangrove** | salt marsh |
| Tundra | palsa, polygon mire | sedge fen | — | — |

### One climate, many forests: three examples

**Latvia (hemiboreal).** Already prototyped: [prototype](prototype/README.md) and [1:10 in game](prototype/README.md#4-with-the-ground-columns-at-110-6-november-evening).
- Dune and outwash sand A: lichen and heather pine.
- Fresh loam C: spruce with oxalis.
- Clay D: oak, ash, lime and spruce.
- Wet A: raised bog. Wet C–D: alder carr.
- Coast: foredune, then dune pine.

**Japan, Hokkaido (cool temperate to hemiboreal).** Same grid, different species pool:
- Fresh C–D: Japanese oak (*Quercus crispula*), maple, linden and Yezo spruce.
- Moist and cooler: Sakhalin fir.
- Wet: Japanese alder and the Kushiro marsh reed and sedge fen.
- Volcanic Andosols are rich but free-draining and acid. They are a texture and chemistry case that Latvia does not have.
- Shady north slopes at 43° hold conifers lower than the sunny south slopes.

**Argentina, northern Patagonian Andes (cool temperate, strong rain gradient).**
- West, wet: coihue (*Nothofagus dombeyi*).
- Higher and colder up to the tree line: lenga (*N. pumilio*).
- Cold flats, frost hollows and poorly drained ground: ñire (*N. antarctica*).
- Drier east: Chilean cedar (*Austrocedrus chilensis*).
- Valley floors: wet meadows (*mallines*), the fen and meadow row of the grid.
- Further east: the rain-shadow steppe, where water supply decides, not the site.

The three use the same drivers and the same grid, but different species pools, which the native ranges (WCVP) already supply. What has to be added for each region is the cover mix per habitat. A new rule system per region is not needed.

## Water table and the aquifer

Site wetness comes from **water-table depth**, then the soil's water holding, then slope:

| Water table | Wetness | Correction |
|---|---|---|
| above the surface | 6 flooded | — |
| 0–0.3 m | 5 waterlogged | — |
| 0.3–1 m | 4 wet | — |
| 1–2 m | 3 moist | — |
| deeper | 2 fresh on loam, 1 dry on sand, 0 on dune sand or a high Podzol | Gleysols at least moist; Histosols and peat at least waterlogged; in arid climates one class drier; sunny steep slopes one class drier |

Now: the offline prototype computes the water table with a mound model, and the game reads its result only inside the baked Latvia map. Later: an **aquifer model** gives the regional groundwater level from geology (permeable Devonian sandstone, karst, gravel) and recharge, and the local water table rests on it. The interface stays the same, because wetness only reads "water-table depth here". The aquifer just makes that number better.

## Spawn rules: how a plant or tree decides

For every candidate species at a place, in this order:

1. **Range in 1318**: is the species native (or introduced by 1318) here? WCVP botanical countries, as Earth does now.
2. **Climate at real height**: Earth's temperature and water supply at the real elevation in metres, plus slope-corrected heat (light). This replaces the pack's own Temperature, Precipitation and Altitude columns.
3. **Site fit, soft 0–1**: how well the species' wetness, richness, pH and light needs match the site. This is a weight, not a cut-off, so edges blend.
4. **Cover mix by growth form**: each habitat has a target share for trees, shrubs, dwarf shrubs, grasses and sedges, herbs, mosses and lichens, and bare ground. Exemptions live here. A foredune has almost no grass in front (marram only), a raised bog has no grass and is mostly sphagnum, and a lichen pine forest is mostly lichen and heather. Species from step 3 fill the share of their growth form.

### Floral Zones columns, translated

Floral Zones' table columns are vanilla `BlockPatch` fields:

| Floral Zones column | What it is in vanilla | Under Earth |
|---|---|---|
| Temperature | °C range of the climate cell, adjusted by height above sea | replaced by Earth's real-height climate (step 2) |
| Precipitation % | rain 0–255 | replaced by Earth's water supply (step 2) |
| Fertility 0–1 | `Climate.GetFertility(rain, temp, y)`: climate, not soil | replaced by richness (step 3); keep as a hint for a default |
| Forest 0–1 | forest-noise value | **reused as light**: high Forest = shade-tolerant, low = open ground |
| Altitude 0–1 | share of world height above sea | only for plants flagged alpine, translated to metres; otherwise dropped |

Why altitude cannot be read directly: 0.3 is 2,660 m at 1:10 (1,024 high, linear), 520 m on a 384-high world with Earth's log curve, and 1,160 m with log 1000. Real metres from Earth's climate already move every plant up and down with the lapse rate, so only true alpine plants need an altitude band.

### Catalogue fields per species

| Field | Values | Default when unknown |
|---|---|---|
| wetness | min–max class 0–6 | from pack hints: `NearWater` → 3–6; otherwise 1–4 |
| richness | min–max A–D | A–D |
| pH | acid / neutral / calcareous | any |
| light | shade / half / open | from Floral Zones' Forest column |
| salt | no / tolerant / needs | no |
| habitats | list (bog, fen, dune, heath, meadow…) | none |
| growth form | tree, shrub, dwarf shrub, graminoid, herb, moss, lichen | from the block type |

**Data source for Europe: EIVE 1.0** (Dengler et al. 2023), Ellenberg-type indicator values for about 9,000 European species: moisture, nutrients, reaction, light and temperature. Moisture maps to wetness, nutrients to richness, reaction to pH and light to light. Reported as CC BY 4.0; verify before use. Outside Europe: hand values for the species in the packs, from floras, until a better source is found.

The same order for animals, and one shared region index instead of latitude lines and boxes: [ranges](../ranges-2026-11-07/README.md) (7 November, proposal).

## Who does what: Earth and the content mods

| Earth (data and rules) | Content mods (blocks) |
|---|---|
| the seven drivers per column; `/earth site` | plant, moss, litter and ground blocks |
| zone, wetness, richness, habitat | their species' own models and textures |
| species catalogue fields (above), cover mix per habitat, exemptions | optionally their own catalogue entries in Earth's format |
| the spawn-rule order; vetoes vanilla and pack climate rules | — |
| top block material (ground columns) and surface look (habitat) | — |

A content mod never decides where its plants grow under Earth. Earth reads its blocks through the catalogue. A mod that wants to cooperate can ship catalogue entries; one that does not still gets defaults from its patch hints.

### Mods

| Mod | Role (proposal) |
|---|---|
| **Floral Zones** (8 regional packs, Tentharchitect) | the main ground-flora and plant provider. Licence "All Rights Reserved", so collaboration, not copying |
| **Wildcraft: Trees and Shrubs** (+ Herbarium) | supplies Earth's Eurasian trees today: Norway spruce, aspen, ash, linden, elm, downy birch, alder, grey alder, willows, black poplar, yew, larch, beech; 59 generators worldwide. **Dropping it is aspirational**, only if Floral Zones supplies these trees (its spruces, aspen and larch are currently North American species) |
| **EcoMachina** | optional visual mod (render-only tapered trunks), not a dependency. Listed for 1.22.0–1.22.2, reported stutter, incompatible with Rooted Forests. Bench test before recommending |
| Biomes, Vara's species packs | not needed. Earth's ranges and habitats replace Biomes' realms, and the author does not want many one-species packs |

**Ask for Tentharchitect (Floral Zones), if the author agrees to contact:**
1. **Eurasian ground layer, missing from Circumboreal:** heather, bilberry, cowberry, crowberry, cranberry, marram, reindeer lichen, wood anemone and wood sorrel. Plus a sphagnum carpet block (Circumboreal has two sphagnum mosses as plants).
2. **Eurasian trees** if Wildcraft is to be dropped: Norway spruce, Siberian and European larch, aspen, grey alder, downy birch, ash, linden, elm, beech, black poplar, yew.
3. Optional: Floral Zones reads Earth's habitat query when Earth is present and uses its own rules otherwise. Or Earth ships catalogue entries for Floral Zones' species.

## First code slice (in progress, not committed)

In the `habitat-overlay` worktree:
- `src/Core/SiteDrivers.cs`, game-independent, with:
  - heat (GDD from a sinusoid year);
  - light (clear-sky radiation by latitude, slope, aspect and elevation; year, growing season, and ratio to flat ground);
  - water supply;
  - soil texture, richness and permeability from ground columns, using the same rules as the offline prototype;
  - wetness from water-table depth, where it is known;
  - coarse disturbance.
- `tests/SiteDriversTests`: FAO-56 checks by latitude, slope and polar day and night; GDD; soil rules.
- `/earth site`: prints every driver at the player.
  - Wetness reads "unknown" outside the baked Latvia map, because there is no water-table model in the game yet.

**Checked offline (6 November):**
- `SiteDriversTests` and `GroundColumnsTests` print ALL PASS, and the mod builds with 0 warnings.
- **Daily radiation:** within 1 % of the FAO-56 closed form at every latitude and day tested.
- **Annual flat radiation:** 13,151 MJ/m² at the equator and 7,826 at 57° N, matching the closed form summed over the year.
- **30° slopes at 57° N:** south-facing 1.47 × flat, north-facing 0.50 ×. At 30° S, north-facing 1.12 ×.
- **GDD5:** Riga-like 1,601, tundra-like 192, tropical 7,665.
- **Fast table:** the per-column path (by latitude, slope and aspect) is within 0.4 % of the exact sum below 75° and within 1 % near the poles.
- **Known limits:**
  - Clear-sky is applied as if all light were direct beam, which slightly overstates the gap between sunny and shady slopes.
  - The arid-climate shift in wetness is not yet modelled.
  - At the Ķemeri bog point the soil data reads Podzols on till, so the soil says "sand, poor, free-draining". The mapped wetland layer has to set mires, as the prototype found.
**In game, 6 November night (bench, world habitat10b at 1:10):**

`/earth site` ran at 13 Latvian points without errors:

| Point | Slope, light vs flat | Soil | Wetness | Other |
|---|---|---|---|---|
| Carnikava dunes | 2.6° NW, 0.96 | Podzols, sand A, free-draining | 1 dry | coastal (sea ~310 m); dune pine |
| Kēmeri raised bog | flat | Gleysols on lake clay | 5 waterlogged | raised bog |
| Teiči bog | flat | Histosols, peat | 5 waterlogged | raised bog |
| Zemgale plain | 2.5° W | Luvisols on lake clay, clay D, tight | 4 wet | inland |
| Gaiziņkalns, south side | 5.1° SE, 1.09 (1.04 in the growing season) | — | — | — |
| Gaiziņkalns, north side | 2.0° NW, 0.97 | — | — | — |
| Sigulda valley edge | — | 0 m cover → thin soil over rock (the Devonian sandstone outcrops) | — | — |
| Gauja floodplain | — | Fluvisols | 4 wet | "occasional floods", 2 m above the river |

Fixes made from the test:
- **Slope:**
  - **Problem:** Copernicus GLO-90 is a *surface* model that includes tree and roof heights. A two-point difference read a 10° slope on the flat Jelgava plain at a forest edge.
  - **Fix:** slope and aspect now come from a least-squares plane through 5 × 5 heights over 400 m.
  - **Open:** a terrain-only model (for example FABDEM, licence non-commercial) would be better at fine scales. The game terrain itself carries these canopy steps.
- **Wetness:**
  - **Problem:** the Latvia map could read below the soil group's minimum (Kēmeri: "fresh" on Gleysols). That happens because the map's domain warp samples a neighbouring cell.
  - **Fix:** the soil minimum now wins, and the result says so.
- **Peat:**
  - **Problem:** at Teiči the cover class says outwash under the bog, so the peat read as free-draining.
  - **Fix:** peat is now never free-draining. Its richness has three steps: bog A below pH 5, poor fen or transition B below 6, fen C. Teiči's smoothed SoilGrids pH of 5.1 now gives B.
  - **Divergence:** the offline prototype still uses the old rule. Its wetness floor for Histosols hides the difference there.
- **Coast:** "coast unknown" now reads "inland (no sea within 10 km)".

Latvia is flat at this resolution: the steepest slope found was 7°, and light varied from 0.96 to 1.09 of flat ground. The strong slope effects (1.5× and 0.5×) need real mountains.

It does not yet change what spawns. Order after that: species catalogue fields, then soft site fit in `EarthTrees` and `EarthPlants`, then cover mixes per habitat, then the water-table and aquifer model in the game.

## Decisions for the author

1. Light: clear-sky radiation now, with a cloud or surface-radiation layer later (recommended); or wait for a radiation dataset.
2. Floral Zones' Forest column read as the light need (recommended), or ignored.
3. EIVE as the European source for catalogue values (after the licence check).
4. Whether to contact Tentharchitect, and with which asks.
5. Wildcraft: keep it until Floral Zones covers the Eurasian trees (recommended), or replace the trees in Earth.
6. EcoMachina: bench test as an optional visual mod, or skip it.
