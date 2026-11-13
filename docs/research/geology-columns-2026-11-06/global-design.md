# Ground columns for the whole Earth: resolution, data, and no-data rules (6 November 2026)

Status: **design proposal**, written for the refactor the author approved ("Ok refactor. But we need to think for to apply this for the world and what resolution we are going to use. and how we get data and what we do when we don't have data."). The Baltic prototype (README, section Prototype) is the first user of this design and implements sections 1, 3 and 6, the tile format and the no-data rules for its fields (6 November: seven tiles, offline tests pass, not yet run in game). Lazy loading, the global base tiles, generic templates, the hand-off to regional geology and saved tile hashes are not built. Nothing here is decided beyond what the author said.

The author, 6 November (late): **ground columns are to cover the whole world**, with the critical missing ground and rock types added; vanilla rocks and ores stay the default choice in the Earth tab for now.

## 1. The principle

The generator is fixed code; the Earth it builds is **data plus rules**. Better data for a region is a new file in a list, never a code change. Every position has an answer: where no dataset reaches, rules derive one from what is known, and the column says how it was obtained (`/earth geology` shows mapped vs derived per layer).

Three kinds of input, kept apart:

| Kind | What | Format | Who changes it |
|---|---|---|---|
| **Fields** | Measured or mapped quantities at a position: soil group, clay %, Quaternary thickness, surface sediment unit, bedrock unit under the cover, depth to basement, basement rock, seabed substrate | Self-describing raster **tiles**, each at its source's own resolution, stacked by priority | Bake scripts, one adapter per source |
| **Rules** | How a column is built from fields: depth curve, recipes per unit (by name), stratigraphic templates per region, compaction with depth, fallbacks | `ground-rules-vN.json` | Hand-edited, reviewed |
| **Code** | Interpolation, the stack lookup, column assembly, writing blocks | C# (`Core/GroundColumns`, `EarthGround`) | Rarely |

## 2. Resolution

Horizontal block size on Earth worlds runs from about 10 m (ring 4,096,000, "1:10") through 78 m (512,000, the default) to 313 m (128,000). Vertically the ground is in metres; since 6 November (late) its block curve starts at the terrain's own vertical scale at sea level (about 9.7 m per block at 1:10) and is chosen per world in the Earth tab (Ground depth: linear, two logs, or linear to 2,000 m), with every layer of 0.5 m or more keeping one block (see the README's depth scale section).

There is no single right resolution, because the fields are not equally detailed in nature or in the sources:

| Field | Natural scale of change | Best open global source | Its resolution |
|---|---|---|---|
| Soil group, texture, pH | 100 m – 1 km | SoilGrids 2.0 (CC BY 4.0) | 250 m |
| Regolith / sediment thickness | 100 m – 1 km (valleys thick, hilltops thin) | Pelletier et al. 2016 (ORNL DAAC; free, Earthdata login); Shangguan et al. 2017 depth to bedrock (ISRIC, CC BY 4.0) | 1 km; 250 m |
| Surface sediment unit (till, loess, alluvium, dunes …) | 1–10 km | GUM (CC BY 3.0, already in Earth) | 0.5° grid (polygons exist, 1.3 GB) |
| Bedrock under the cover | 1–50 km | GLiM (CC BY 3.0, in Earth; no ages); IGME 5000 (Europe, ages) | 1:3.75M; 1:5M |
| Depth to basement | 10–100 km | CRUST1.0 | 1° |
| Seabed substrate | 1–10 km | EMODnet (Europe, CC BY 4.0) | 1:250k – 1:1M |

So **each field lives at its source's resolution**, in its own tiles. The format does not fix a resolution; the runtime samples each field where it is stored (bilinear for quantities, warped nearest for categories so cells never show as squares).

Proposed standard grids, so tiles from different sources line up and stay cheap:

- **Global base, shipped with Earth:** 2 arc-minutes (1/30°, about 3.7 km) for soil, texture, thickness and sediment unit; the coarse fields at their native 0.5° and 1°. Estimated 15–30 MB compressed for the land. At the default scale a 2′ cell is about 47 blocks, at 1:10 about 370.
- **Detail, downloadable by region:** 30 arc-seconds (1/120°, about 0.9 km), the native grid of Pelletier and the aggregated SoilGrids, in 10° × 10° tiles (1200 × 1200 cells, a few MB each). The whole land surface would be roughly 200–400 MB, so it is fetched on demand like the GLO-90 heights, not shipped.
- **National, where open data exists:** at the source's resolution (Latvia's Quaternary grid is 250 m; the LVĢMC maps are polygons at 1:200k), as small tiles that override the global ones only where they have data.

Below a field's resolution the generator **synthesizes** detail, deterministically: warped category contacts, thickness modulated by the terrain (thicker cover in hollows and valleys, thinner on crests and steep slopes, as Pelletier's model does physically) and by low-amplitude noise. That is the same policy Earth already follows for heights and rock contacts.

## 3. How tiles stack

A tile is a rectangle in latitude and longitude holding one or more fields on one grid, with a header that names each field, its unit and scale, its nodata value, how to interpolate it, and for categories the **legend as names** (e.g. `"Gt"`, `"D3pl"`, `"Luvisols"`). Each tile also carries its source and licence.

The rules list the tiles with a priority. For a field at a position, the runtime asks the tiles that cover the position, highest priority first, and takes the first that has a value there (nodata falls through). So a Latvian 250 m Quaternary grid sits on top of Pelletier, which sits on top of the 2′ global base; outside Latvia the lower tiles answer.

Categories are matched to rules **by name**, never by index. A new map with new units only needs rule entries for its names; unknown names fall back (section 5) and are reported once in the log.

Detail tiles load lazily (first query in their rectangle, then cached), so a world only holds the tiles its players visit.

## 4. Getting the data

One small adapter per source format writes tiles: GeoTIFF (any projection we can write out by hand, as for EPSG:3034, 25884 and 3857 already), polygons (shapefile or WFS GeoJSON, rasterized at cell centres), ASCII grids, and point tables (boreholes, control points as CSV with a source column). A manifest lists each source, its URL, licence, hash, the fields it feeds and the tile grid it is baked to, so a re-bake is reproducible.

Data still to fetch for the world, by priority: Shangguan depth to bedrock (open), SoilGrids at 1 km for the base, GUM polygons (finer surface units), Pelletier (needs the author's Earthdata account), GLiM at full polygon resolution, a global ages map (Macrostrat, CC BY 4.0, terms to check; USGS world geologic maps, public domain), HWSD v2 (terms to check), and national maps where open (LVĢMC is CC0).

Licences: CC BY and CC0 sources ship with attribution in `geology-attribution.txt`. Sources whose terms are unclear (IGME 5000, HWSD) are baked only for tests until their terms are checked.

## 5. When there is no data

Every field has a fallback chain in the rules, ending in something that always exists:

| Field | 1. National / regional tile | 2. Global tile | 3. Derived by rule | 4. Last resort |
|---|---|---|---|---|
| Soil group | national soil map | SoilGrids (base or detail) | climate + parent material + wetness (e.g. cold humid on sand → Podzol; steppe on loess → Chernozem; waterlogged → Gleysol) | vanilla-like by climate |
| Fertility, topsoil depth | — | — | soil group table (already in the rules) | `default` entry |
| Quaternary / regolith thickness | national grid | Shangguan, Pelletier | typical metres per surface unit, scaled by relief | 3 m |
| Surface sediment unit | national Quaternary map | GUM | climate history: glaciated in the last glaciation → till; else residual soil over rock | thin cover |
| Bedrock under the cover | national pre-Quaternary map | IGME (Europe), GLiM (world) | nearest mapped unit of the same basin | Earth's existing regional geology (v2 beds) below the cover |
| Stratigraphic column | regional template (Baltic, later Moscow, Polish, North German, Paris …) | — | generic template per setting: shield (basement at the top), platform (flat-lying cover), foldbelt, volcanic | Earth's regional geology v2 |
| Depth to basement | borehole-corrected regional tile | CRUST1.0 | — | (CRUST1 is global) |
| Basement rock | national basement map | — | synthesized mix by setting (shields: mostly gneiss with granite plutons) | granite |
| Seabed substrate | EMODnet and national | deep-ocean lithology (Dutkiewicz et al. 2015, terms to check) | depth and distance to shore: sand near shore, mud offshore | mud |

The key fallback for a world rollout: **outside regions that have a template, the ground columns still write soil and the Quaternary cover, and hand everything below to Earth's existing regional geology**. So switching the system on globally never makes a region worse than today; templates improve regions one at a time.

## 6. Rules that make the column believable (from the author's shaft review, 6 November)

- **Glacial cover as cycles**, not one unit: upper till, sand and gravel between tills, lower till, with lake or marine clay on top where mapped; shares and caps per unit, thicknesses varied a little by position.
- **Compaction with depth:** loose sediments become rock below a depth set per material (raw clay → claystone, sand → sandstone; around 150–200 m by default). Shallow Devonian clay stays raw clay (it is quarried at the surface); the Cambrian blue clay at 700–900 m under Latvia becomes claystone, while under Tallinn at about 100 m it stays clay.
- **Basement is not all granite:** without a basement map, a deterministic mix by setting (in the Baltic about two thirds gneiss, one third granite plutons), each a large body tens of kilometres across.
- **Order follows age** (younger above older); marl under sandstone is right where the formations say so (Narva under Arukila).

## 7. Saves

A world records the rules version and the content hash of every tile it was generated with (as regional geology does, decision 0002). New data reaches new worlds, or existing worlds by an explicit opt-in that warns about seams with chunks already generated.

## 8. World placement model and size budget (proposal, 7 November)

The author, 7 November: rock types, textures and models are secondary. "We first need global placement model either on data or good guesses. We will not have data for everywhere so will need fill lot of blanks. If needed we can create needed blocks with generic textures and just fill them later. First comes actual world placement and resolution (trying to keep that still kind reasonable size, lets say less than 1GB)."

### 8.1 Resolution and size

The author, 7 November, on this section: "3-5km grids are fine for first step … Most of it probably anyways will be made up. So i think first we need to understand what is reasonable data we can get and then decide resolution. plus inside the cell we anyways have some variation." So the table below is an upper bound, not a decision: the first step is a 3–5 km grid (2′ = 1/30°, about 3.7 km at the equator, is Earth's own terrain grid; 2.5′, about 4.6 km, is also common), and the resolution is chosen after the data inventory ([data-inventory.md](data-inventory.md)).

Land without Antarctica is about 14 M cells at 2′ and about 225 M at 30″. Measured on the SoilGrids test tiles (2′, zlib, u8): WRB group 0.33, clay 0.39, pH 0.44 bytes per cell. Finer grids compress a little better per cell; the 30″ figures below assume 0.25–0.35 B/cell for quantities and about 0.05 for map units (large uniform areas).

| Field | Grid | Source | Estimated size |
|---|---|---|---|
| Soil group + "base-rich clay" flag (the only use of clay and pH in the rules) in one u8 | 30″ | SoilGrids 2.0 WRB, clay, pH (WCS) | 55–80 MB |
| Cover thickness (depth to bedrock, log-coded u8) | 30″ | Shangguan 2017 1 km | 55–80 MB |
| Surface sediment unit | 30″ | GUM polygons, coastal gaps filled from the nearest land unit | 10–20 MB |
| Bedrock lithology + age class | 2′ | GLiM (already in Earth) + Chorlton GSC 1:35M ages | 5–10 MB |
| Clay %, pH as raw values (for later rules) | 2′ | SoilGrids | about 12 MB |
| Tectonic province, basement depth, seabed | 0.1°–1° | Hasterok 2022, CRUST1.0, Dutkiewicz 2015 (licence open) / rules | under 2 MB |
| Regional overrides (Latvia Quaternary 250 m, EMODnet, Baltic basement …) | native | national data | a few MB each |
| **Total** | | | **about 150–200 MB** |

That leaves room under the author's 1 GB: storing clay and pH separately at 30″ would add about 120–160 MB; 15″ soil would roughly quadruple the soil share and break the budget. At 30″ a cell is about 9 blocks at 1:100 and 75–90 blocks at 1:10; below that the generator synthesizes detail (warped contacts, cover thinning on slopes and crests, thickening in hollows and valley floors). Shipping: a 2′ base inside the mod (about 25 MB, so the system works with no download) and the 30″ set as a data release fetched like the GLO-90 heights.

Measured 7 November (item 7 below): soil groups at 30″ 21 MB, depth to bedrock at 30″ (log-coded byte) 135 MB, permafrost at 30″ 16.5 MB, 172 MB together. Each Earth archive must stay under 40 MB, so the 30″ sets live in `VintagestoryData/ModData/earth/ground` as optional tiles over the 2′ sets in the mod; how they reach players (several data packs, or a download like GLO-90's) is not decided.

### 8.2 What decides each layer (data first, then guesses)

Inputs that exist everywhere, most already in Earth: climate (temperature, rainfall, aridity), height, slope and relief, rivers, lakes and coast (Earth's water data), GLiM lithology, GUM, CRUST1.0. To add: the extent of ice at the Last Glacial Maximum (e.g. Batchelor et al. 2019, CC BY 4.0, terms to check), and the permafrost zone (e.g. Obu et al. 2019, ESA CCI, terms to check).

| Layer | Data where it exists | Guess where it doesn't |
|---|---|---|
| Soil | SoilGrids WRB group | climate × parent material × wetness (section 5) |
| Surface unit | GUM (gaps filled) | refined by place: river floodplain → alluvium; lake floor or former lake → lake clay and marl; within reach of the coast → beach sand, dunes, marine clay; inside the LGM ice → till cycles; permafrost zone → permafrost silt or till; humid tropics and unglaciated shields → saprolite (laterite crust where seasonally dry); drylands → calcrete or gypsum crust over sand or colluvium; volcanic arcs → tephra |
| Cover thickness | Shangguan (Latvia: national grid) | typical metres per unit × relief (thin on slopes, thick in valleys), saprolite thickness by rainfall and surface age |
| First bedrock | GLiM lithology + Chorlton age | nearest mapped unit |
| Layers down to the basement | regional templates (Baltic now) | generic templates by province type × age: platform (flat beds thickening to CRUST1's sediment depth), foldbelt (steep, thinner, metamorphosed downward), shield (basement right under the cover), rift and passive margin (thick young beds), volcanic arc (lavas and tephra over intrusions), oceanic crust (basalt) |
| Basement | regional basement maps | by province: shield/craton gneiss with granite bodies, orogen schist and granite, arc diorite, ocean basalt and gabbro |
| Seabed | EMODnet, then Dutkiewicz | glaciated shelf → gravelly sand or till, river mouth → mud, tropical shelf → carbonate sand, otherwise sand above about 50 m and mud below; deep sea by latitude and depth (calcareous ooze above the carbonate compensation depth, siliceous ooze under high-productivity belts, red clay elsewhere) |

With generic templates everywhere, the hand-off to regional geology becomes the last resort instead of the default.

### 8.3 Blocks the placement needs (generic textures first)

To be added as plain placeholder blocks, textured later: saprolite (felsic, mafic), laterite (hard, mottled), calcrete, loess, tephra, salt crust, permafrost (silt, till), calcareous and siliceous ooze; rocks gneiss, schist, quartzite and gypsum as full rocks with the dolostone pattern (guarded so a mod that provides the same `game:rock-<x>` wins). Each enters the rules as a material with fallbacks, so the placement can run before any texture is final.

### 8.4 Order of work (proposed)

1. Fix the GUM rules (loess, lake, ice, evaporite, coastal, volcanic codes) and fill GUM's coastal gaps (Carnikava). **Done 7 November, commit 1896f53.**
   - GUM's 0.5° grid leaves a cell empty both at sea and where no loose sediment is mapped: the Sahara's hamadas, the shields, the high mountains, Iceland and the Deccan are all empty. GLiM's grid, on the same cells, tells them apart. Empty land gets a rock-surface code by GLiM class: `Rx`, `Rm` for mafic, `Rk` for carbonate; pyroclastic, evaporite, ice and water use GUM's own codes. GLiM's unconsolidated cells take the nearest GUM unit (the central Amazon becomes alluvium). Sea cells within 3 cells of land take the nearest land code, so Carnikava is `Au` with the Latvian 50 m, no longer the default.
   - Every GUM code now has a recipe: loess, lake, ice, playa, coastal, delta, tephra, colluvium, mixed sediment, and residual covers over bare rock.
   - Outside formerly glaciated ground, recipes no longer end in till. A cover `regions` override keeps the till-based recipes inside two placeholder boxes (northern Europe to 51° N, North America to 40° N) until the ice-age extent tile replaces them.
   - New materials (loess, colluvium, regolith, saprolite, terra rossa, limestone rubble, tephra, salt crust, gypsum earth) are named `earth:*` and fall back to vanilla blocks until step 2.
   - Tile: 33 KB. Tests check seven sites worldwide and that no non-glacial unit sits on till outside the boxes.
   - Open: the deep ocean outside the 3-cell coastal fill still gets the default residual cover under its seabed layer; the seabed rules in step 4 replace it.
2. Placeholder blocks for section 8.3. **Done 7 November, commit bdac7d6.**
   - Earth blocks with generic procedural textures: saprolite (felsic, mafic), laterite (mottled; hard ironstone crust), calcrete, loess, tephra (falls like gravel), salt crust, gypcrete, permafrost (silt, till), ocean ooze (calcareous, siliceous), colluvium, weathered rock and terra rossa.
   - Gneiss, schist, quartzite and gypsum are full vanilla-family rocks (`game:rock-<x>` with cobble, bricks, gravel, sand, dry stone and polished forms), patched in the way dolostone is. The patches are off when geoaddons or Advanced Geology is installed, since both provide these rocks.
   - The rocks have no ore variants yet.
   - Side effect: Earth's existing `earth-gypsum` deposit (`src/EarthOres.cs`), skipped until now because no gypsum block existed, generates in worlds using Earth's ore layer.
   - Known clash: with Advanced Geology installed, its rock textures and Earth's share paths (`game:textures/block/stone/...`), so one mod's look wins.
   - Bench: the mod loads, every patch applies, every new block exists, no missing textures. No ground-column world was generated, because the bench world came out 256 high.
3. Global bakes at 2′ first (soil, Shangguan, GUM, GLiM + ages, province), then the 30″ release. **2′ done 7 November, commit 7f99531.**
   - Six world-wide sets at 2′ in 10° tiles, 32.5 MB shipped (2,043 tiles):
     - SoilGrids WRB group, clay and pH (16.1 MB)
     - Shangguan depth to bedrock (11.8 MB)
     - Batchelor ice extents, LGM and older (0.1 MB)
     - Obu permafrost probability (1.4 MB)
     - Hasterok province type, last orogen and crust type (1.6 MB)
     - Chorlton bedrock era and class (1.5 MB)
   - GUM and CRUST1.0 stay at their native grids.
   - A set is one asset, a tile set (EGTS). The game unpacks a tile only when a column inside it is generated, under a 192 MB budget, and packs the least recently used again. A sweep over the whole world stayed at 191 MB.
   - The glaciated cover region now follows the ice extents instead of boxes. Recipes can cap the thickness: Shangguan gives 86–112 m on bare rock in central Africa and the Sahara, so weathered-rock covers are capped at 20–25 m until the climate rules of step 4.
   - Bench: a 1024-high world loads every tile and uses every new block directly (no fallbacks).
   - Baked but not read by the rules yet: permafrost, provinces, ages. They are step 4's inputs.
   - Limits: SoilGrids comes from the WCS's own resampling to 2′ (no mean or mode). Chorlton has eras only, no periods. The ice extents cover the Northern Hemisphere only. The ice shapefiles state no licence on OSF (the article is CC BY 4.0), so confirm with the authors before a release (checked 7 November: none set; section 9).
4. Surface-unit refinement by place (floodplain, coast, LGM ice, permafrost, climate), then generic templates by province and age, then seabed rules. **Done 7 November, commit 1fe85e4.**
   - The game passes each column's place: real elevation, lake or sea, Earth's river valleys, and whether regional geology maps rock there. Cover regions take general conditions (fields, elevation, latitude, surface, river valley), first match first.
   - Lake floors: lake clay (over till on glaciated ground). Open sea beyond the coastal fill: siliceous ooze below 1000 m at 50° and beyond, red clay below 4500 m, calcareous ooze below 200 m. Shelves: till on glaciated shelves, carbonate sand and mud within 30° of the equator, marine sand and clay elsewhere. Seabed zones choose the floor the same way where no seabed map exists.
   - River valleys on rock or mixed sediment get alluvium.
   - Climate comes from the SoilGrids soil group, which covers all land:
     - floodplain soils: alluvium
     - peat soils: peat
     - volcanic ash soils: tephra
     - seasonally dry tropics: ironstone crust over laterite
     - humid tropics: saprolite up to 60 m
     - calcic and gypsic drylands: calcrete and gypcrete crusts
     - saline soils: playa
     - shallow soils: a metre or two of weathered rock
     - sandy soils: sand sheets
     - steppe soils: loess
     These apply only where the sediment map has bare rock or mixed sediment.
   - Permafrost probability of 50 % or more freezes silt, loess, till, colluvium and weathered rock below 1 m. Lake and river floors stay thawed.
   - Generic templates by Hasterok province apply only where regional geology maps no rock: loose sediment, unmapped ground and the sea floor, which would otherwise keep vanilla strata. Elsewhere the GLiM profiles stay as they are, so no work is duplicated.
     - Hasterok's province type is the crust's origin, often buried: Lake Ladoga, the North Sea and the Great Plains read "volcanic arc". So every continental province shares the same flat beds. Chorlton's era picks the starting unit, from Cenozoic down to Proterozoic; Archean starts at the basement. Fold belts get slate, phyllite and schist with age.
     - The beds stretch to CRUST1.0's sediment depth in deep basins. Where the sediment is thinner, they are cut at that depth, so a shield keeps a few young beds instead of a sliver of every era.
     - The province picks the basement: shield gneiss and granite, arc diorite and granite, magmatic crust with gabbro, fold belt schist, ophiolite peridotite, oceanic basalt with gabbro.
   - Offline checks (all pass):
     - deep Pacific, North Pacific and mid-Atlantic floors
     - the North Sea and Bahamas shelves
     - Lake Ladoga, a Saharan river valley, frozen Yakutia
     - the Amazon and the Canadian Shield without regional rock
     - printed columns for the Sahel, Alps, Iceland, Great Plains and West Siberia
   - Not checked in game yet (step 5).
   - Limits:
     - Deep-sea depths follow the terrain's real elevation, so they depend on how Earth's terrain reproduces the ocean.
     - The southern ice is still missing, so Patagonian shelves get no till.
     - The packaging step of this branch's build stops on a version mismatch already on the branch: Earth 1.0.3-rivers.3 against data packs 1.0.2. The DLL builds.
5. Offline checks at sample sites per setting (Amazon, Sahel, Siberia, Alps, Iceland, Paris Basin, Canadian Shield, Great Plains, North Sea, deep Pacific), then the bench. **Done 7 November, commits 1fe85e4 (offline) and 9faf594 (river-bed fix).**
   - Bench: a 1024-high ground-columns world, 98 m per block.
     - Load: every tile loaded, no errors, no unknown names. The only block fallbacks are gabbro to basalt and diorite to andesite (vanilla has neither rock).
     - Sixteen sites read with `/earth geology`:
       - deep Pacific, North Pacific and mid-Atlantic floors
       - North Sea, Bahamas, Lake Ladoga
       - Amazon, Sahel, Yakutia, Alps, Iceland, Paris
       - Canadian Shield, Great Plains, Sahara, West Siberia
     - Each gives the setting's cover, floor and rock, with the rock below cover going to regional geology where it maps rock. Some fit well: Ladoga gets lake clay over till over sandstone over granite, the North Sea glacial gravel and till over 4 km of basin beds.
   - Fixed from the bench: river beds on loess or dunes (the Seine at Paris, the Niger at Niamey) showed loess or 90 m of dune sand. The river-valley rule now covers river beds and dune units.
   - Seen but left:
     - In permafrost only silt, loess, till and weathered rock freeze. Sand, gravel and clay keep their blocks until frozen variants exist.
     - Regional geology still uses its own stand-ins (gneiss as slate, schist as phyllite), though the real rocks exist since step 2.
   - Bench notes:
     - The Earth play style sets `earthOres` to vanilla, so a test copy must change it there, not only in the option's default.
     - `/earth geology` needs an `arrive` after `/earth tp`.
6. Real rocks and frozen ground, 7 November (commit ff9211f), after step 5's open points.
   - Ores and deposits: each added rock takes its stand-in's ore blocks and deposit host entries. The patches are generated from vanilla's own lists by `tools/make_rock_ore_patches.py`, so a region keeps the ores it had with the stand-in:
     - gneiss from slate
     - schist from phyllite
     - quartzite from sandstone, without coal
     - dolostone from limestone: its ore blocks already existed; it now also hosts deposits
     That makes 345 ore variants and 105 deposit host entries. They are off with Geology Additions or Advanced Geology, like the rocks. Marble, which vanilla places in slate or phyllite by name only, is left as it is.
   - Regional geology: new worlds use Earth's gneiss, schist and dolostone instead of slate, phyllite and limestone. Saved worlds keep the blocks recorded in their material signature, so existing chunks and new chunks still match.
   - Frozen ground: frozen sand, gravel and clay join frozen silt and till. Below the active layer every loose material except peat freezes, down to its compaction depth: frozen sand still turns to sandstone below 150 m.
   - Bench (world ground6):
     - All 723 patches apply with no errors.
     - Regional geology no longer stands in for dolostone, gneiss or schist. Only gabbro, diorite and rhyolite still have stand-ins: Earth has no such rocks.
     - The new ore blocks exist (tin ore in gneiss, bismuth ore in schist, quartz in quartzite, lead ore bits in dolostone), as do the three frozen blocks.
     - Yakutia's alluvium is frozen from 1 m to the bedrock.
     - The 28 deposit warnings about missing handbook page codes are vanilla's and appeared before the patches.

## Existing mods: Advanced Geology (reviewed 7 November)

The author asked not to remake existing mods and to look at [vs-AdvancedGeology](https://github.com/funions123/vs-AdvancedGeology) for rock types; the author also noted it has no till. Read-only clone in `C:\VS-ref\mods\advancedgeology` (commit f36324b; mod `advancedgeology` 2.1.2, ModDB 7167, for 1.22.7).
- **Licence: none** (no LICENSE file, none on ModDB; textures credited "mostly by Viceroy"). Its assets cannot be copied; players installing it beside Earth is fine.
- **Rocks** (all `game:rock-<x>` with full vanilla behaviour and ore-host variants): amphibolite, dacite, diorite, dolomite, gabbro, gneiss, granulite, greenstone, migmatite, quartzite, rhyolite, schist, serpentinite, tonalite, syenite; deposit hosts anorthosite, carbonatite, gypsum, pegmatite, phosphorite, pyroxenite, soapstone, umber. Soils: gossan, raw kaolinite, geyserite. From our shortlist it covers gneiss, schist, quartzite and gypsum; not till, saprolite, laterite, calcrete, loess, oozes, salt crust, tephra or permafrost.
- **Placement:** replaces vanilla `rockstrata.json` and the province thicknesses, empties every ore deposit file of every mod through Harmony and places its own procedural deposits. It calls itself incompatible with other geology mods, and Earth already turns regional geology, ground columns and its ore layer off when it is enabled (`src/EarthGeology.cs`, `src/EarthOres.cs`).
- **Use:** as a pattern for full-behaviour rock blocks, and possibly as an optional provider of the same `game:rock-<x>` codes. Earth still needs its own gneiss, schist, quartzite and gypsum for worlds without it, plus every soft ground type on the shortlist.
7. 30″ data, cover by relief, and a world QC, 7 November (the author: "if native data is 30″ then lets use if we are still in MB range"; "high parts would stick out … Finland could have valley filled with sediment and hill next to almost bare rock").
   - Soil groups were wrong in places at 2′: the SoilGrids WCS answers scaled requests from overviews built by averaging, so class numbers were averaged (Podzols and Cambisols came back as Histosols, a lake and Cambisols as Calcisols; Chernozems in Lapland, Gypsisols on the Canadian Shield). It does so whatever interpolation is asked for. `tools/ground/fetch_soilgrids_30s.py` now takes full-resolution tiles (7.5″) and keeps the most common class of each 30″ cell; the WCS writes 0 for both Acrisols and no data, so a 0 counts as Acrisols only where the Acrisols probability says so. The 2′ fallback in the mod is rebuilt from the same classes (13.2 MB, was 16.1).
   - 30″ sets (`tools/ground/bake_30s.py`): soil groups, Shangguan depth to bedrock at its own grid (log-coded in one byte, about 2.5 % steps), Obu permafrost from the full 1 km grid. 172 MB, read from the data folder lazily (only the tile headers at start). Missing files fall back to the 2′ sets and are named in the log.
   - GUM and GLiM stay at 0.5°: their polygons are 1.4 GB and more to download; the relief term hides most of their straight cell edges.
   - Relief (`src/Core/GroundRelief.cs`, rules `relief`): each column's height above the mean ground within 1.2 km and its slope, from the terrain itself (GLO-90 in the QC). Cover = data × exp(−relief/8 m) / (1 + (slope/0.4)²), divided by the same factor's mean over the neighbourhood, so sediment moves from crests to hollows and the neighbourhood keeps the data's total (mean factor 0.92–1.00 in the QC; steep mountains less). Dunes, loess, sand sheets and tephra are the relief themselves and are left alone.
   - Crest regions: on glaciated land a knoll at least 8 m above the mean ground or steeper than 0.35 keeps a skin of till (`knob-till`, at most 2 m); elsewhere at 12 m or 0.45 thin regolith on rock. Peat, dunes and loess keep their recipe.
   - GUM's wind-blown units with silty or loamy soils are loess, not dune sand (the Pampas, Yakutia's ice-rich silts); river valleys keep their alluvium. Alluvium and mixed sediment are capped at 80 m and dune sand at 60 m, where Shangguan's deep desert values met the hollow factor.
   - World QC (`GROUND_PROBE=qc`, 40 boxes of 100 × 100 km on every continent, `tools/ground/qc-boxes.txt`, report [qc-2026-11-07.txt](qc-2026-11-07.txt)): all 43 loose checks pass. Examples:
     - Finland lakes: under 1 m 7 %, under 3 m 20 %, over 20 m 26 %, median 8 m; bare-till knolls beside thick valley fills.
     - Norway fjords: 44 % under 1 m, plateaus bare, till only on valley floors.
     - Namibia Damaraland: 45 % under 1 m, calcrete and colluvium over rock.
     - Latvia: under 1 m 0–1 %, the national thickness grid with relief on top.
   - Re-run the same evening with only the 2′ sets that ship in the ground pack (`GROUND_DATA` pointing at an empty folder, report [qc-2026-11-07-2min.txt](qc-2026-11-07-2min.txt)): all 43 checks pass; medians stay within a metre in 35 of 40 boxes (Ethiopia 15 → 19 m, Australian outback 38 → 40 m), and the bare share drops in the dry rock lands (Kalahari under 1 m 24 → 14 %, Namibia 45 → 38 %).
   - Seen and left, then fixed in item 8:
     - Shangguan is thick on the shields: Finland's mean is 12.6 m against GTK's 8.6 m.
     - The Congo box is all dune sand (GUM's unit there). West Siberia is half loess where peat is truer.
     - The Kalahari reads only 4 m of sand, because Shangguan is shallow there.
   - Bench, 7 November (world relief1, the 1:10 play style at 1024 high, the 30″ sets in the bench's data folder):
     - The 30″ sets load from the data folder with nothing missing. No errors; the only warnings are vanilla's own.
     - The game computes relief from its own terrain (`core.Model.GetColumn`), on a lattice of a third of the radius per chunk.
     - On a 4 km line through the Finnish lakes (62° N), the readings match the offline probe:
       - a knoll 29 m above its surroundings: 0.3 m of till on rock
       - one 13 m up: 2 m
       - hollows 9 and 14 m down: 8 m, the cap of the thin-till recipe on this bare-rock (`Rx`) cell
     - In Norway a fjord slope keeps 0.1 m and the valley floor 8 m. In Namibia, flat calcrete varies from 5 to 10 m.
     - At 1:10 the top of the column is 9.7 m per block, so these contrasts are one block of till or none. They show more where the cover is thicker (till plains, valleys), and in worlds whose depth curve gives more blocks at the top.
     - Generation speed was not measured.
   - Delivery, 7 November (the author: "Separate ground pack"): the 13 tiles under `assets/earth/config/ground/` (28.6 MB compressed) are the optional content mod `earthgrounddata` (`tools/package-ground-pack.py`; side Server, not a dependency, not in pack-index.json). They stay assets in Earth's domain, so the loader is unchanged. Block types, textures, patches and the rules stay in Earth. A ground-columns world without the pack logs that it is missing and keeps regional geology. The 30″ sets are still separate (ModData). Checked on the bench with tall.17 (earthgrounddata-1.0.3-tall.17, no 30″ sets, world t17gc200 at 1:200): "Earth ground columns v1 … from 2057 tiles", the 30″ sets listed as not installed, no Earth errors.
8. Fixes from the world QC, 7 November (the author: "fix probe issues"; commit 7be14da on `claude/ground-columns`, merged into release/1.0 at 13b3ba9 with the author's "Yes, merge").
   - Thickness rules (rules `cover.thickness`, `GroundColumns.ThicknessRule`) correct a thickness data set where it is known to run thick or thin. First match wins. A rule applies only to values read from its source, optionally only to some recipes, as a factor and a range; `/earth geology` names it after the source.
     - `shield-cover`: Shangguan × 0.68 on glaciated Precambrian shields (Chorlton's era). Finnish lakes: mean 12.6 → 9.0 m (GTK 8.6 m), median 8 m. Canadian Shield mean 4.6 m, Lapland 14 m.
     - `sand-sea`: a dune field stands on at least 15 m of sand. Kalahari: median 4 → 15 m.
   - Histosols are peat whatever GUM maps under them, over till on glaciated ground.
   - Boreal lake plains: GUM's wind-blown units at 55–75° on wet soils (Albeluvisols, Gleysols, Stagnosols, Planosols), away from rivers, are lake sediments. West Siberia: loess 46 % → 0 %; now lake 46 %, peat 31 %, alluvium 20 %.
   - Loess also needs at least 12 % clay in the soil, so the Kalahari's sands stay sand (loess 5 %).
   - Humid tropics: GUM's wind-blown units on Ferralsols, Acrisols, Nitisols and Alisols (the Batéké and Kalahari sands of the Congo basin) are sand over laterite and saprolite (`sand-on-saprolite`, typically 25 m, at most 60), not dune sand down to the rock. Congo: median 27.5 m. The relief term leaves them alone: GLO-90 is a surface model, and the forest canopy reads as relief.
   - The QC check on Finland's share over 20 m is relaxed from 10 % to 3 %. It was set from the reading before the correction (26 %); now 5 % (2′: 3 %).
   - World QC on both data sets ([30″](qc-2026-11-07-fixes.txt), [2′](qc-2026-11-07-fixes-2min.txt)): all 43 checks pass. The loess lands keep their loess: the Pampas 99–100 %, the Loess Plateau 99 %, Yakutia 77 %.
   - On the merge (13b3ba9, after the biome work): the ground, soil, wetland and tree tests pass, and the 2′ QC is identical to the branch's.
   - Not seen on the bench yet.

## 9. The author's decisions and discussion after the QC (7 November)

- **Regional maps: same treatment everywhere.** Latvia's LVĢMC 1:200k subcrop map is not needed (the author: "not needed, all get the same treatment"). The national 250 m Quaternary thickness grid already baked for Latvia stays until the author says otherwise.
- **Caves by rock type, with the vanilla-caves toggle (agreed 6 November): entrances are under discussion, not decided.** The author, 7 November: "need to think about cave entrances. in hills or mountains cave entrances would be ok. I don't like vanilla caves entrances in flat terrain". One idea to weigh when caves are designed: a cave opens to the surface only where the relief term reads hill or slope (height above the neighbourhood mean, or steepness), and stays sealed under the cover on flat ground. The author, later the same day: "There are probably some karst or similar entrances on flat ground but not as vanilla does", so flat karst (sinkholes, dolines) may open where it is real, unlike vanilla's entrances anywhere. Also open: whether the toggle's vanilla caves would follow the same rule.
- **Generation speed** is measured at the full merge, as part of performance testing (the author: "will do later for full merge - performance testing").
- **30″ delivery: a public repository under Temporal-Poets-Society**, like the GLO-90 heights (the author: "pulbic repo similar as there is already height data. if the data is too large for VS mod server"). 172 MB is over the 40 MB per archive, so it is. Plan, not done: repository `earth-ground` with release `egts-v1` holding the three sets, a manifest with sizes and SHA-256, and attribution. SoilGrids and Shangguan (ISRIC's data policy: SoilGrids products are CC BY 4.0) and Obu's permafrost (PANGAEA 888600, CC BY 3.0) allow redistribution with attribution. **Published 7 November at the author's "you do it"**: [Temporal-Poets-Society/earth-ground](https://github.com/Temporal-Poets-Society/earth-ground), release `egts-v1` with the three sets (172,251,299 bytes), `manifest.json` (sizes, SHA-256, sources) and `ATTRIBUTION.txt`; the README documents the EGTS/EGT1 format and each source's changes. Downloaded back from the release, all three match the manifest. Local copy of the repository: `C:\VS mods\earth-ground`. Earth's download, built 7 November at the author's "Yes, build it" (commit 0a687cc on `claude/ground-download`, merged into release/1.0 at eef6fc6 with the author's "Yes, merge"): a new ground-columns world fetches the sets into `ModData/earth/ground` before its first chunk (the server waits; 3 s here), checks each file's size and SHA-256, resumes a broken download, and records its choice in `EarthParams.GroundData` (2: the 30″ sets; 1: the 2′ sets for good when the 30″ ones can't be had), so one world never mixes 2′ and 30″ cover. A 30″ world fetches missing sets again; worlds made before this read what the machine has. The source is `GroundRepo` in earth.json (a local folder works; empty: never fetched). It happens when the world opens, not in the Earth tab: the world creation screen runs no mod code. Checked with `tests/GroundDownloadTests` (also against the real release, resuming over HTTP) and on headless servers: a new world fetched and read the sets; reopened without them it fetched them again; a new world without a source kept the 2′ sets, and reopened with the 30″ sets present still did not read them.
- **Ice extents (Batchelor et al. 2019): no licence.** Checked 7 November: OSF project 7jen3 has no licence set (`node_license` null; no components, no licence file; the README gives a citation and use caveats only). The article is CC BY 4.0, which covers the article, not the shapefiles. The LGM outline is "derived mainly from the compilation of Ehlers et al. (2011)", so its terms matter too. Before a public release: written permission or a licence on OSF from Christine Batchelor (OSF admin and corresponding author), or an openly licensed replacement. The 2′ ice tiles are in the ground pack, so this blocks a public ground pack, not play-tests. What the data is: the paper's best-estimate outlines of the Northern Hemisphere ice sheets at the Last Glacial Maximum (about 21,000 years ago) and at three older maxima (MIS 6, 12 and 16, about 140,000, 450,000 and 650,000 years ago), baked into the `glaciation` field (`lgm`, `older`). Nine rules read it: till only on glaciated ground, knob-till on crests, peat and lake clay over till, glaciated shelves and sea floors, glaciated loess, and the shield thickness correction. The Cambridge Apollo record (doi:10.17863/CAM.56359) that the source inventory listed as CC BY 4.0 is the article, not the shapefiles (checked 7 November). A replacement to check: PaleoMIST 1.0 (Gowan et al. 2021), global from 80,000 years ago, so it would also add the southern ice; its plot archive is CC BY 4.0, its ice grids were not located. It has no older maxima.
- **Water table and aquifers: Hydrate or Diedrate support** (the author: "i want add https://mods.vintagestory.at/hydrateordiedrate mod support and it adds aquifer we could look how it is done and so add"). How HoD 2.5.8 does it (source on GitHub, Chronolegionnaire/HydrateOrDiedrate):
  - Each 32-block chunk gets an aquifer rating 0–100 and a salty flag, generated lazily on the column's first load (`AquiferManager.Generation.cs`), not in world generation. The rating counts the fresh, salt and boiling water blocks in the column, adds rainfall and a seeded random part with a bonus below sea level, and caps it at 30 above sea level. It reads no rock or soil. A smoothing pass evens neighbours (a transpiler on vanilla's chunk supply).
  - Stored in chunk moddata `aquiferData` (protobuf: rating, salty, smoothed rating; version 2). If every chunk of a column already has version-2 data, HoD skips its own generation.
  - Players dig wells: a pickaxe in rock gives a deep well, a shovel in soil, gravel or sand a shallow one; a chunk's rating is shared among its wells (at most about 50 L a day per chunk). The prospecting pick shows the rating.
  - No provider API or event. `AquiferManager.SetAquiferRating` exists but cannot set salt and rejects values over 100.
  - On Earth's worlds: ratings fall with world height (about 1.8× lower at 512 high, 3.5× at 1024), the first load scans whole columns, and its rainfall lookup swaps coordinates, so it reads rain near the map's north edge.
  - Licence: custom, personal or internal educational use; no modification or derivative works without permission. Writing HoD's data format from Earth copies none of its code. **The author, 7 November: "data format is not code. We can do it."**
  - Agreed approach: Earth, with HoD installed (optional, no dependency), writes `aquiferData` for every chunk of a column as it generates it, from its own ground model: the rating from how permeable the layers below the water table are (gravel, sand, sandstone and karst high; clay, till, shale and crystalline rock low) and from recharge; salty near the sea and under salt flats. HoD then uses Earth's aquifers for wells and prospecting. This needs Earth's water table first (habitat proposal, [habitats README](../habitats-2026-11-06/README.md)); without HoD, Hydrology's seep rule uses the same water table.
  - **Merged into release/1.0** (8 November, 149aa3e; the author: "Merge as is"): decision 0059 (`docs/decisions/0059-water-table-and-hod-aquifers.md`, built on branch `claude/water-table-hod`; new worlds only, `EarthParams.WaterTableRules` 194). The biome session confirmed it builds no water table. Earth's own water table: the drainage base (lowest water or ground within 1.5 km), height above it, the permeability of the top 10 m by ground material, aridity, and perched tables on humid till plains. With HoD enabled, each new column's chunks are rated from the blocks they hold and written as version-2 `aquiferData` in PreDone, so HoD's first load finds them and skips its own generation; a reflection check of HoD's data version gates the writer. A public query (`WaterTableAt`, `AquiferAt`) serves any well mod, and `/earth water` shows it. HoD's well rules (aquifer wells only in rock; sand and gravel give shallow wells): the author chose "a now, ask HoD (c)": leave them, and ask HoD's author for configurable well blocks (the author sent the Discord request on 7 November; HoD's author asked for a pull request, which a separate session is preparing). Checked in game with HoD 2.5.8 on 7 November. After the bench, on the author's "Yes, fix both" (8 November): chunks are rated by real metres (transmissivity on Krasny's classes, so 1:1 and 1:10 read the same), and the GLWD wetland grid raises the table on a boggy cell's low ground. Offline tests pass; the bench has not been run again since the fixes, and the author chose not to for now (8 November: HoD supported as it is).
- **"Habitat top block"** is the habitat proposal's surface block: material from the ground column (peat, sand, loam), look from the habitat (bog moss and pools on a raised bog, litter under spruce, heather on dry pine sand, wet meadow), set in one pass so neither overwrites the other.
- **Ores by layer: decided.** The author, 7 November: "Ores etc. must be in correct layers independent of heat map". Ores, gems and other deposits go in their right host layer of the column (placer tin and gold in alluvium, bog iron in peat, coal in coal-bearing beds, salt and gypsum in evaporites) whatever the ore choice is, vanilla or the Earth ore maps. The ore maps (real-world heat maps of where each ore occurs) only scale how much; they do not decide the layer. **Built on `claude/ores-in-layers`** (decision 0060, `docs/decisions/0060-ores-in-their-layers.md`, agreed with the author's "Yes, build it"; merged into `release/1.0` on 8 November, merge 1cb597c, in Earth 1.0.3-tall.21 at 03dddab; new ground-column worlds only, `EarthParams.OreLayers` 193): each column layer records its part (top, cover, deep cover, bed, basement) and source, `ore-layers-v1.json` turns that into a layer class with age and an evaporite flag and says where each deposit may go; right after the deposit pass a layer pass puts the column's own block back wherever a deposit block sits in a layer it may not use, under both choices; the ore maps ask per block and lay their beds after the ground pass. New layer deposits under both choices: placer gold and tin (new placer blocks) in alluvium and outwash, salt flats with sylvite in playas, bog iron in peat (with Geology Additions' item), bauxite in laterite and karst terra rossa, kaolin in saprolite, gypsum in evaporite beds; vanilla's gravel and soil patches and surface salt are off under columns. Prospecting reads only allowed layers; `/earth orelayers` counts deposit blocks by layer class. The offline probe (`tests/OreLayersTests`, 14 sites in two play-style worlds) passes: no hard-rock ore in cover, no old lignite, salt only in evaporite beds and playas, placers, bog iron and bauxite in their layers; 99 % of hard-rock host blocks kept, halite 46 %. Bench check on 8 November under both ore choices: after four fixes (salt flats never placed under vanilla ores, the relief cache, the census, prospecting names) no deposit block was in a layer it may not use in chunks made by the fixed build; under the ore maps the layer pass still puts back most fire-clay blocks, cause open.
