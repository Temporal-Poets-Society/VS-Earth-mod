# Realistic ground columns: Baltic pilot (6 November 2026)

Status: **Baltic prototype built 6 November, refactored the same day into data tiles plus name-based rules** ([global-design.md](global-design.md)) (Earth worktree `.claude/worktrees/ground-columns`, branch `claude/ground-columns` from `release/1.0`; uncommitted; off unless the `GroundColumnsPrototype` config flag is on when a world is created). See [Prototype](#prototype-6-november) below. The rest of this page is the research behind it; items not in the prototype stay proposals. The author asked (6 November) for realistic soil and geological layers ("at any scale even at 1:10 almost all places would have actual soil layer 1m or less"), a logarithmic depth scale to about 2,000–3,000 m ("nothing deeper is accessible by 14th century methods"), new blocks for real sediments ("different clays/sand mixtures"), clay abundant where it really is, and a Baltic pilot to decide which blocks are needed. Ore placement by layer comes after this.

## What Earth does now

Vanilla's soil pass lays 3–4 soil blocks; Earth adds dune sand or gravel lenses from the 0.5° GUM grid; below that, rock beds from the 0.5° GLiM grid hang below a smoothed reference surface, the first bed about 100 blocks thick in a 384-high world ([regional geology](../../regional-geology-implementation.md)). GLiM's unconsolidated cover (`su`, e.g. most of Poland) keeps vanilla strata. There is no layer between soil and rock, and no real depth to basement.

## Proposed depth scale

Above ground Earth's height curve gives about 5 m per block near sea level, so a 1 m soil would be 0.2 block. Underground needs its own curve, measured below the smoothed reference surface: one block per metre at the top, logarithmic with depth, reaching 3,000 m at the bottom of a lowland column in a 384-high world (sea level 165; lowland surface ~y 185).

`blocks(d) = A · ln(1 + d / d0)`, with d0 = 42 m and A = 42.5 (A = 1 / ln(1 + 1/d0), so the first metre is one block):

| Real depth | 1 m | 2 m | 5 m | 10 m | 20 m | 50 m | 100 m | 300 m | 500 m | 700 m | 1,000 m | 2,000 m | 3,000 m |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Blocks down | 1 | 2 | 5 | 9 | 17 | 33 | 52 | 89 | 109 | 122 | 137 | 165 | 182 |

The author (6 November): a log scale is right; "3000m was just guess. Maybe 2000m is enough"; the block curve "we can change later, layers should be kind of tied to meters"; sea level is now 110 by default.

**So the model is in metres, the curve is a setting.** Every layer keeps its real top and thickness in metres (from the data and the templates). One function turns metres below the reference surface into blocks, with its parameters saved per world like the other generation rules, so it can be retuned without touching the data; a retune changes new chunks only.

Starting curve: 2,000 m fitted to the blocks between a lowland surface and the world floor. With sea level 110 and Baltic lowlands a few blocks above it, about 115 blocks are left, so d0 ≈ 26 m, A ≈ 26.5:

| Real depth | 1 m | 10 m | 21 m (Latvian median cover) | 50 m | 300 m | 688 m (Staicele iron) | 1,000 m | 2,000 m |
|---|---|---|---|---|---|---|---|---|
| Blocks down | 1 | 9 | 16 | 29 | 67 | 88 | 98 | 115 |

Below 2,000 m: basement only (in the Baltic the basement is at 382–1,916 m, so it is reached almost everywhere). Mountains have more rock above the same curve.

**Is 2,000 m enough?** For play, yes: a 14th-century mine reached tens to a few hundred metres, and the floor depth hardly changes the upper column, because a log curve spends its blocks near the top. Fitted to the same 115 blocks, 3,000 m instead of 2,000 m moves 50 m from block 29 to 27 and 300 m from 67 to 62. What a shallower floor drops is only depth: deeper basins (the Kurzeme–Lithuania coast at 2.2–2.5 km, the North Sea, the Pannonian Basin) end in sediment instead of reaching basement. Ore at those depths is out of reach in 1318 either way, and deposits that matter come up with their rock (salt domes, the Ruhr's coal measures within 1,500 m). Being a setting, the floor depth can be changed if a region needs more.

**Revised the same night (the author, 6 November, late):** the 1 m-per-block top meant digging deeper than real next to terrain at about 10 m per block. The author: "baseline world 1:10 but below is still 110 blocks (1024 high)"; smaller worlds have other heights and curves; "we can keep thin layers as min layer is 1 block but we more shrink thick layers. we can try several (linear but with min layer height) and some logs." Built on `claude/ground-columns`, commit `d3c20ff`:
- The curve starts at the terrain's own metres per block at sea level (`1 / VerticalScale.Slope(0)`): 9.7 at 1:10 (1024 high, linear), 22.1 at 1:25 (512, linear), 8.4 in the 384-high log1000 worlds.
- Earth tab **Ground depth** (`earthGroundDepth`, saved as EarthParams.GroundDepth, ProtoMember 191), four choices:
  - `terrain`: linear at that scale; y = 1 under lowland at 1,106 m (1:10), 2,522 m (1:25), 959 m (384). The first default.
  - `log2000` and `log4000`: that scale at the top, 2,000 or 4,000 m at y = 1 under lowland; at 1:10, 1000 m costs 72 or 57 blocks instead of 103. Linear where the terrain scale already reaches deeper (log2000 at 1:25).
  - `linear2000`: an even scale coarse enough to reach 2,000 m (17.5 m per block at 1:10 and 384).
- Every layer at least 0.5 m thick (rules `curve.minLayerMetres`) keeps one block; the blocks below shift down until a thick layer takes up the shift. Columns keep each layer's real depth in metres (`Column.EndMetres`), so readings and `/earth geology` give true metres. Topsoil stays one block (chernozem two) at every scale.
- Riga at 1:10 (`terrain`): soil, silt, sand, gravel, till, dolostone, marl, dolostone, sandstone and red clay are one block each (the top 82 m), Gauja sandstone 6, then the thick Devonian units, granite from 931 m at block 96. In `log4000` the granite is at block 54; at 1:25 everything above about 350 m is one block per layer. Offline tests check the curves in all three presets and that Riga keeps exactly the layers it has at one block per metre.
- **Bench, 7 November:** four new 1:10-style worlds (Riga, 1024 high, linear terrain, world size 4,096,000 = 12 m per block across, the largest this branch offers; the bench's `serverconfig.json` MapSizeY set to 1024 for the test and restored after), one per choice, each with a 16×32 shaft to y = 1 at 2,170,200 / 300,506. The columns match `/earth geology` and the offline model; saves `g10-terrain`, `g10-log2000`, `g10-log4000`, `g10-linear2000` stay on the bench. Shots: `g10-*-small.png` in this folder. Seen on the way: lignite in Devonian sandstone (regional ores, the known coal-in-Latvia issue), and at the shaft two of the four seeds read the `thin` till fallback instead of alluvium, the same GUM coastal gap the habitat session found at Carnikava (GUM's 0.5° cell is sea; the Latvian grid gives 40–50 m of cover).
- **The author picked the strong log (`log4000`) from the shafts** ("this one"), then asked why 4000 when the model stops at 2,000 m: the number only sets the bend; 2,000 m lands at block 83 (1:10) or 85 (384) with basement below. The author: "we just keep in settings for now different log and linear and then player can choose and we can change what feels best." Commit `ff6199e`: `log4000` is the default (also for saves without the field), the four choices stay, and the labels say what each does (strong log: 2,000 m about 30 blocks above the bottom).

**Trade-off to accept or reject (written for the first curve):** above ground a 1:10 world is about 10 m per block vertically, underground the top is 1 m per block. A river cutting 3 blocks (30 m) into the land therefore exposes only the first 3 m of the column in its banks, not 30 m. The layers would hang below a smoothed reference surface as in geology rules v2, so hills keep their layers and valleys cut into them; the cut is shallower than in reality. Existing worlds: a new geology rules version, new chunks only ([0002](../../decisions/0002-save-compatibility.md)).

## The column, layer by layer, and the data for it

| Layer | Source tried | Resolution | Baltic result |
|---|---|---|---|
| Soil (1 block; 2–3 only on chernozem/loess) and its texture | **SoilGrids 2.0** (ISRIC, CC BY 4.0), WCS clips of clay/sand/silt/coarse fragments at 6 depths to 2 m | 250 m, fetched at 0.01° (~1 km) | Works without login (24 files, ~11 MB for 20–29 E, 53.5–60 N). Smooth model output: Latvia is loam / sandy loam, Zemgale (Jelgava) clay loam below 30 cm, uplands sandier. Useful for choosing the soil and subsoil block, not for deeper layers. Masked towns and water read 0 and must be skipped |
| Quaternary cover type (till, glaciolacustrine clay, outwash sand/gravel, alluvium, peat, loess) | **GUM** (CC BY 3.0) 0.5° grid, already in Earth | 0.5° | Latvia box: till 23 cells, glaciolacustrine 11, glaciofluvial 8, alluvium 10, peat 1. Jelgava = glaciolacustrine (right for the Zemgale clay plain). The GUM polygons (1.3 GB archive) would be much finer; not fetched |
| Quaternary thickness, Latvia | **Popovs et al. 2022**, bedrock surface of Latvia, band 2 = Quaternary thickness, from >20,000 boreholes ([Zenodo 7053754](https://zenodo.org/records/7053754), **CC BY 4.0**, 17.7 MB) | 250 m | Fetched. Median 21 m; 10th–90th percentile 7–67 m; 20 % of Latvia ≤ 10 m, 66 % ≤ 30 m, 13 % > 60 m; max 219 m. Sites: Riga 17, Jelgava 18, Staicele 11, Liepāja 12, Saldus 20, Daugavpils 70, Gulbene 27, Cēsis 8 m ([quaternary-thickness-latvia.txt](quaternary-thickness-latvia.txt)) |
| Quaternary thickness, worldwide | **Pelletier et al. 2016** (ORNL DAAC 1304), soil / regolith / sediment thickness, 1 km, capped at 50 m | 30″ | **Needs a NASA Earthdata login** (files under `/protected/`), so not fetched. The author would have to download it (≈1 GB). National grids such as Latvia's are better where they exist |
| First bedrock under the cover (subcrop) | **IGME 5000** (BGR, pre-Quaternary geology of Europe, free download, 47 MB shapefile; cite "IGME5000, (c) BGR Hannover, 2007"; BGR terms apply to redistributing derived grids, to be checked) | 1:5 M | Rasterised at 0.05° for 20–29 E, 53.5–60 N. Latvia: Late Devonian over most of it (often "undifferentiated"), Middle Devonian sandstone/clay/marl in the north (Staicele, Tartu), Late Permian and Early Carboniferous in the south-west (Saldus, Liepāja), Triassic/Jurassic further south-west, Ordovician limestone in north Estonia. Fixes "unknown bedrock" under cover, but lithology often needs a regional formation template |
| Sedimentary thickness = depth to crystalline basement | **CRUST1.0** (Laske et al. 2013; no licence stated, citation requested) | 1° | Staicele 700 m (reported iron at 688 m), Riga 1,000 m, Liepāja 1,700 m, eastern Latvia 500 m, north Estonia 100 m, Kurzeme–Lithuania coast 2,200–2,500 m. Matches the known pattern; coarse |
| Formation sequence between subcrop and basement | none global; Macrostrat is thin outside North America | — | Regional templates from the real columns below |

Per-site readings of all four sources: [baltic-sites-datasets.txt](baltic-sites-datasets.txt) (scripts in [scripts/](scripts/); downloaded data kept outside the repository).

## Real columns (ground truth)

See [baltic-real-columns.md](baltic-real-columns.md) (literature research of the same day, every fact sourced). In short: soil; Quaternary cover 5–20 m in lowlands and 40–60 m in uplands, up to 310 m in buried valleys, top down peat, Baltic Ice Lake and glaciolacustrine clay, the reddish-brown Latvian (Weichselian) till, the dense blue-grey Kurzeme (Saalian) till; then Devonian almost everywhere (Permian to Jurassic only in the south-west); crystalline basement at 382–1,916 m (209 deep boreholes). The author's "thin dolomite, then rock-hard clay" fits central Latvia: the Pļaviņas–Salaspils–Daugava carbonates (22–50 m around Riga, with the Salaspils clay, marl and gypsum inside), then Amata/Gauja sandstone with clay; in the north-east the Devonian Lode clay; the 90–130 m Narva clay and marl lower down. The older Kurzeme till is also described as dense and hard.

## The Baltic Sea floor and sandy beaches

The author (6 November): "also for layers consider Baltics sea and sandy beaches." The same model applies under water: the sea floor is the column's top, the depth curve runs from it, and only the top layers differ.

**Sea floor data: EMODnet Geology seabed substrate** (GTK GeoServer WFS, `gtk:seabed_substrate_250k`, also `_1m` and finer national layers; listed CC BY 4.0; national sources such as LVĢMC's bottom-sediment maps; modified Folk classes: mud, sandy mud, muddy sand, sand, coarse sediment, mixed sediment, rock and boulders). Fetched for 55.5–60 N, 19.5–29 E: 5,533 polygons, 46 MB as GeoJSON (in web Mercator although EPSG:4326 was asked for; axis order lat,lon in the request). By mapped area ([seabed-baltic.txt](seabed-baltic.txt)):

| Waters (mapped at 1:250,000) | Mud + sandy mud | Sand + muddy sand | Coarse + mixed | Rock & boulders | No data |
|---|---|---|---|---|---|
| Latvia (11,849 km², less than half its waters) | 36 % | 23 % | 22 % | 1 % | 18 % |
| Estonia (24,782 km²) | 67 % | 2 % | 29 % | 2 % | — |
| Lithuania (5,426 km²) | — | 21 % | 8 % | 5 % | 66 % |

Spot checks are mixed: sand in the Irbe Strait, off Ventspils and at Kolka; boulder-gravel off Pape and Salacgrīva (the Vidzeme rocky coast); varved clay in Pärnu Bay; but a "Gulf of Riga centre" point reads sand and a point off Jūrmala reads bedrock, where mud and sand would be expected. The 1:1,000,000 layer would fill the gaps; the classes need the same kind of check as the land data before use.

**Proposed sea-floor column:** the substrate class sets the top layer (mud as silt or blue clay, sand, coarse as gravel, mixed as muddy gravel, rock and boulders as exposed till or bedrock with loose boulders), a few metres of Holocene marine sediment (thicker mud in the basins; the research gives Litorina and lagoon deposits up to 15–20 m on the coast), then the Baltic Ice Lake clays and till, then bedrock as on land. Real thicknesses offshore still need a source.

**Sandy beaches:** Earth's terrain model already flags beach columns (`Beach`; the cover pass skips them). Proposal: beach and foredune columns get sand in metres (a few metres on the beach, more in the dunes: the research gives Holocene dune sand 0.5–10+ m and coastal lagoon sand behind it) over lagoon sand and silt, then the land column. Not every Baltic shore is sand: boulder and pebble shores (Vidzeme rocky coast, north Estonia), till and sand cliffs (Jūrkalne, Pape) and the Estonian limestone klint need a coast-type source; EMODnet's coastal type layers are the candidate, not yet checked.

## How the datasets compare with the real columns

| Quantity | Dataset | Real | Verdict |
|---|---|---|---|
| Depth to basement | CRUST1.0: Staicele 700, Ventspils 1,100, Ruhnu 900, Kuldīga 1,700, Daugavpils 490 m | Staicele iron at 688 m, Ventspils 1,170, Ruhnu 784, Kuldīga ~1,100, Daugavpils ~800 m | Right range and NE→SW slope; single cells off by up to 40–55 % (1° cells). Good enough worldwide; replace with published control points where a country has them (no open basement grid exists for the Baltic states) |
| Quaternary thickness | Latvian 250 m grid | the literature's 5–20 m lowlands, 40–60 m uplands, 230 m Daugavpils valley max | Agrees; use it for Latvia. Elsewhere Pelletier (50 m cap) or a rule by GUM type and relief |
| Quaternary type | GUM 0.5° | till dominant, glaciolacustrine clay in Zemgale and the coastal lowlands | Agrees at 0.5°; the local mosaic (till ridges, clay plains, sand) needs the LVĢMC 1:200,000 Quaternary map (**CC0**, data.gov.lv) or the GUM polygons |
| Subcrop | IGME 5000 | Devonian almost everywhere, Permian–Jurassic in the SW | Agrees; lithology "undifferentiated" for much of Latvia, so the formation sequence comes from a template. LVĢMC's 1:200,000 pre-Quaternary map is also **CC0** |
| Formations between subcrop and basement | none | full sequence for Ventspils, Ruhnu, Daugavpils; Riga as a composite | A Baltic template per age band (Devonian carbonate/clay/sandstone packages, Silurian–Ordovician carbonates and shales, Cambrian sandstone and blue clay, granite basement), thickened to the basement depth |
| Soil texture | SoilGrids | loam and sandy loam, clay loam on the Zemgale plain | Plausible but smooth; enough to choose the soil and its fertility modifier |

## Topsoil fertility

The author (6 November): "Topsoil fertility also is part of pie." Vanilla picks the soil block's fertility from climate alone (`blocklayers.json`: verylow, low or medium by rain, temperature and a fertility value; compost and high only from players), and Earth does not change it.

**SoilGrids' chemistry cannot set it directly.** At 15–30 cm it gives Latvian soils 30–60 g/kg organic carbon, more than the Ukrainian chernozem at Poltava and Kursk (25–36 g/kg), and the Sahara a cation exchange capacity of 24 cmol/kg, close to Latvian loam. An index from carbon, CEC, clay and pH rated almost every site "high" ([fertility-report.txt](fertility-report.txt)). Organic litter in boreal forest soils and model smoothing explain it; it is a second "cannot be trusted" layer like the ore heat map.

**Soil type works better.** Proposal: natural (pre-fertiliser, 1318) fertility by WRB reference soil group, then one class up on base-rich clay loams (clay ≥ 25 %, pH ≥ 6):

| Fertility | Soil groups |
|---|---|
| high (vanilla never generates it; **chernozems decided by the author**, 6 November: "chernozems must be natural high fertility"; the others proposed) | Chernozems (decided), Kastanozems, Phaeozems, Vertisols |
| medium | Luvisols, Cambisols, Nitisols, Fluvisols (floodplains), Umbrisols, Andosols, Lixisols, Calcisols |
| low | Albeluvisols/Retisols, Gleysols, Stagnosols, Planosols, Regosols, Alisols, Solonetz |
| verylow | Podzols, Arenosols, Leptosols, Ferralsols, Acrisols, Plinthosols, Histosols (peat), Cryosols, Gypsisols, Durisols, Solonchaks |

On SoilGrids' most probable group this gives Latvia 21 % verylow, 50 % low, 22 % medium, 6 % high. **But the most probable group is noisy:** about 12 % of Latvia comes out as Kastanozems, Durisols, Ferralsols or Cryosols, steppe, desert, tropical and permafrost soils that do not occur there. Next step: the FAO Harmonized World Soil Database v2 (1 km, open), or SoilGrids' per-group probabilities limited to the groups the climate allows. Climate keeps deciding grass cover as in vanilla.

**Topsoil thickness, in metres, by soil type.** The author: chernozem is "a big part of Ukraine and more than 1 block probably". Its dark humus horizon is typically about 0.6–1.5 m deep and more in the thick chernozems, against 0.2–0.4 m in Baltic forest soils (textbook ranges, to be sourced before building). On the 1-block-per-metre top of the curve that is 1–2 blocks of high soil on chernozem and 1 block elsewhere, as the author asked. SoilGrids cannot supply this depth either: its organic carbon at 60–100 cm is 11–18 g/kg under the chernozems at Poltava, Kharkiv and Kursk but 37–39 g/kg at Jelgava and Gulbene, where real mineral subsoil at 1 m is far poorer. So the thickness comes from the soil type (a table in metres per soil group, chernozem thickest), like fertility.

## Blocks the Baltic column needs

"New" means Earth would add it. Geology Additions is excluded from publication, so its dolostone, siltstone, mudstone and gneiss are not available.

| Layer | Block | Status |
|---|---|---|
| Topsoil, 1 block (2–3 on chernozem and loess) | vanilla `soil-{verylow,low,medium}`, fertility from soil type | exists; "high" naturally is a choice |
| Till (moraine): reddish-brown Weichselian, dense blue-grey Saalian | **till** in two colours: hard to dig, drops stones and some clay | new |
| Glaciolacustrine and Baltic Ice Lake clay | vanilla blue and red raw clay as continuous layers (the author: clay abundant where it is), optionally a varved-clay look | exists; frequency is a design change |
| Outwash, coastal and dune sand; gravel | vanilla sand and gravel | exists |
| Silt / loess / alluvial loam | **silt** (or loess) | new |
| Peat | vanilla peat (and Earth's bog peat) | exists |
| Lake marl (freshwater lime) | **lake marl**, optional | new |
| Devonian dolomite | **dolostone** | new (GA's is excluded; limestone is today's proxy) |
| Devonian clay and marl (Lode, Salaspils, Narva) | red/blue raw clay where soft, **marl** where firmer | marl new |
| Devonian siltstone | vanilla shale or claystone as proxy, or **siltstone** | open |
| Gypsum beds (Salaspils, Skaistkalne) | Earth's gypsum as a rock layer | check the existing gypsum ore block |
| Sandstone, limestone, shale; Cambrian blue clay | vanilla sandstone, limestone, shale; raw blue clay | exists |
| Basement: rapakivi granite, gneiss, amphibolite | vanilla granite; **gneiss** new or granite as proxy | open |

Fire clay only where evidence shows refractory clay; Latvian clays are illitic ([Latvia pilot](../../geology-latvia-pilot.md)).

### Chosen for the prototype (author, 6 November): till, silt, marl, dolostone

Proposed definitions, checked against vanilla's `rawclay` (resistance 4, Soil), `gravel` and `sand` (2.4 / 1.8, falling), `rock` (8, mining tier 2) and Earth's own `bogpeat`. Textures would be drawn new (procedural, Earth's own), not recoloured vanilla files.

| Block | What it is | Behaviour | Drops | Variants |
|---|---|---|---|---|
| `earth:till-reddish`, `earth:till-grey` | Glacial till (moraine, *morēna*): unsorted clay, silt, sand, gravel and boulders, compacted under ice | Soil-like, **does not fall** (it stands in steep banks); resistance about 5, between raw clay and rock, no mining tier, so a shovel or pick digs it slowly | itself, plus a chance of loose stones of the region's erratic rock (granite in the Baltic) | `reddish` (Latvian/Weichselian, upper) and `grey` (Kurzeme/Saalian, lower, denser) |
| `earth:silt` | Silt and loess: fine, cohesive, the parent of the best soils | Soil, does not fall (loess stands in vertical walls), resistance about 2 | itself | one; a `loess` look for the steppe later |
| `earth:marl-devonian`, `earth:marl-lake` | Marl: soft calcareous clay to clayey limestone (Devonian Salaspils and Koknese, lake marl) | Soft rock, resistance about 4, no mining tier | itself, or marl lumps; spreading marl on fields (historical liming) is a possible later use, not in the prototype | `devonian` (grey-green) and `lake` (pale freshwater lime) |
| `game:rock-dolostone` | Dolomite rock, the Devonian dolomite and much of Estonia's carbonate | A full rock: rock, stone, cobble, bricks, polished, gravel, sand and ore variants come from adding it to vanilla's rock lists (`worldproperties/block/rock.json`, `rockwithdeposit.json`), not to the vanilla strata (Earth places it) | as every rock | — |

**Dolostone keeps Geology Additions' code on purpose.** GA adds `game:rock-dolostone` the same way. Earth's patches carry `dependsOn: [{ modid: "geoaddons", invert: true }]` (GA's real mod id), so with GA installed GA's patches register the codes and saves stay interchangeable; without GA, Earth supplies them. Asset files cannot be switched off that way: Earth loads after GA, so with both installed Earth's dolostone textures and names win. A separate small content mod for them would avoid that. Earth's material table already maps `dolostone`; today it falls back to limestone. A rock is the costly block: every derived form needs a texture (rock, stone item, cobble, bricks, polished, gravel, sand, and the ore blocks take the rock as base).

Clay layers use vanilla's blue and red raw clay; peat, sand, gravel and soil stay vanilla.

## Prototype (6 November)

Built after the author's "Yes, start building". The same evening it was refactored, at the author's request, so the generator takes new data without code changes ("not to hard code ground but have generator capable of taking new data and integrating"): the fields live in stacked, self-describing tiles and the rules match the sources' own names. The resolution, data and no-data plan for the whole Earth is in [global-design.md](global-design.md). Nothing is committed.

**What it does.** In new Earth worlds created with "Rocks and ores: Ground columns (Baltic test), regional elsewhere" in the Earth tab (the author, 6 November: keep vanilla and this as choices in the tab; the third choice is the existing regional geology). The author, later the same evening: **vanilla rocks and ores is the default for now** (commit `35d4872`). Worlds made without the tab use `GroundColumnsPrototype` in `ModConfig/earth.json`. The choice is saved as `EarthParams.GroundColumns`, ProtoMember 190, since 93–100 are taken on the tall and other development lines; it needs regional geology), every column inside 20–29 E, 53.5–60 N has its natural ground rewritten after vanilla's soil pass. The column runs from the top down, in metres converted to blocks by `blocks(d) = A ln(1 + d/d0)`. One block is one metre at the top, and 2,000 m falls at the bottom of a lowland column (sea level 110: d0 = 25.6 m; 10 m = 9 blocks, 50 m = 28, 300 m = 66, 1,000 m = 96). The layers, top down:
- **Top:**
  - land: topsoil of the soil group's natural fertility, one block (chernozem two). High fertility is vanilla `soil-compost` ("High fertility soil") over the whole black-soil area, not vanilla's small patches; `soil-high` is terra preta, a crafted soil, and is never generated (the author, 6 November, correcting an earlier misunderstanding).
  - beaches: 3 m of sand
  - sea and lake floors: the EMODnet substrate by Folk class name (mud as silt, sand, gravel); mud where EMODnet has no data
  - river beds: 1 m of silt
- **Quaternary cover:** by GUM unit code, through a recipe of shares of the column's Quaternary thickness. Latvia uses the 250 m grid (Popovs et al. 2022); elsewhere a typical thickness per recipe.
  - Glacial cover is cycles (shaft review): upper reddish till, sand and gravel between tills, lower grey till, more sand, grey till again. Shares vary a little by position.
  - Lake clay is red raw clay over sand and till; outwash is sand and gravel over till; peat lies over lake marl.
- **Bedrock template:** from the IGME 5000 subcrop age down to the basement. Thicknesses are the sourced Latvian formations (Daugava–Salaspils–Pļaviņas 38 m, Amata, Gauja, Burtnieki, Arukila, Narva 110 m, Pärnu, Lower Devonian, a Silurian fading from 650 m in the west to nothing at 26.5 E, Ordovician, Cambrian with Lontova blue clay, Ediacaran), scaled to the basement depth.
  - Where IGME names a lithology ("sandstone clay"), the column starts at that layer of the unit.
  - The basement depth is CRUST1.0 corrected to the boreholes (Staicele, Ventspils, Ruhnu, Kuldīga, Daugavpils).
- **Compaction (shaft review):** loose sediment below a depth set per material becomes rock: raw clay and silt become claystone below 200 m, sand becomes sandstone and gravel conglomerate below 150 m. The Devonian red clay under Riga (69–84 m) and the Lontova blue clay under Tallinn (about 100 m) stay raw clay. The same blue clay at 850 m under Riga is claystone.
- **Basement (shaft review):** not all granite. Without a basement map, the basement is a deterministic mix of bodies about 40 km across: about two thirds gneiss and one third granite (Latvia sample: 72 % and 27 %). Gneiss is `game:rock-gneiss` with Geology Additions, else vanilla slate.
- **Order:** follows age, younger over older. Marl under sandstone is correct where it occurs: the Narva marl lies under the Arukila sandstone.

Air, water, ice and caves are kept. Vanilla clay and peat patches are skipped inside the grid, because the ground lays them as layers. Earth's beach pass no longer resands those beaches. `/earth geology` prints the column, and ore readings (`TryGetColumnRocks`, propick, Wmap) see the same blocks.

**Files.**
- **Data:** `tools/ground/bake_baltic.py` bakes seven EGT1 tiles into `assets/earth/config/ground/` (about 0.74 MB together); `tools/ground/tiles.py` writes the format. Each tile keeps its source's grid and licence:

  | Tile | Grid | Fields | Priority |
  |---|---|---|---|
  | `latvia-quaternary` | 0.01°, Latvia only | `quaternary_m` (Popovs et al. 2022) | 300 |
  | `baltic-soilgrids` | 0.01° | `soil_group` (WRB names), `clay_pct`, `ph` | 200 |
  | `baltic-emodnet-seabed` | 0.01° | `seabed` (Folk class names) | 200 |
  | `baltic-basement` | 0.05° | `basement_m`: CRUST1.0 plus a borehole residual (`tools/ground/basement-control-baltic.csv`) | 200 |
  | `europe-igme5000` | 0.05° | `subcrop` (IGME age names), `subcrop_lith` | 100 |
  | `global-gum` | 0.5°, world | `cover_unit` (GUM codes) | 10 |
  | `global-crust1` | 1°, world | `basement_m` | 10 |

  For each field the highest-priority tile with data answers, and nodata falls through. Where no tile answers, the rules give the value: typical thickness per recipe, default soil, default seabed, default bedrock unit. A name the rules do not know is logged once and gets the default.
- **Rules:** `assets/earth/config/ground-rules-v1.json` lists the tiles and extent and holds the curve, materials with block fallbacks, cover units and recipes, seabed classes, the subcrop-name map and the Baltic template, compaction, the basement mix, and all 30 WRB soil groups.
- **Superseded:** the first single-grid version (`ground-baltic-v1.bin` and its baker) is in [legacy/](legacy/); the habitat prototype's `GROUND=` option still reads that file.
- **Code:**
  - `src/Core/GroundColumns.cs`: game-independent model (`GroundTile` reader, `GroundData` stack, `GroundColumns` rules and builder).
  - `src/EarthGround.cs`: block resolution and the writer.
  - Hooks in `src/EarthGeology.cs` and `src/EarthWorldGen.cs`.
- **Tests:** `tests/GroundColumnsTests`.
- **Blocks:** `earth:till-reddish`, `earth:till-grey`, `earth:silt`, `earth:marl-devonian`, `earth:marl-lake` and the full `game:rock-dolostone` family (patches guarded with `dependsOn geoaddons invert`; textures drawn procedurally by `tools/make_ground_textures.py`).

**Checked.**
- **Offline tests pass on the tile stack** (refactor, 6 November), against the published columns:
  - Riga: Quaternary 18 m, carbonates 18–49 m (source: 22–50 m from about 25 m), Narva 261–349 m (source: about 300–400 m), basement 915 m.
  - Ventspils: Silurian about 335–846 m (source: 265–875 m).
  - Staicele: Middle Devonian sandstone at 10 m.
  - Tallinn: Ordovician limestone.
  - Fertility over Latvia: 16 % verylow, 49 % low, 28 % medium, 1 % high (unchanged by the refactor; median cover 15 blocks).
  - Every name in the Baltic tiles has a rule. The check found three IGME age names the first rules lacked ("Proterozoic", "Proterozoic III - Cambrian", "Proterozoic III - Middle Cambrian").
  - The shaft-review rules: a 40 m till is five layers in cycles; nothing loose lies below its compaction depth; Tallinn keeps raw blue clay.
  - No-data positions (central Africa, north of GUM, the Pacific) get full columns from the global tiles and the rules.
- **On the AgentBridge bench after the refactor** (world ground4, 1:78, spawn Riga, chosen in the Earth tab; commits `07b41ca` and `2127b96` on `claude/ground-columns`): all seven tiles load, no unknown names, no generation errors. The column under spawn matches `/earth geology` block for block: medium soil, silt, sand, gravel, grey till, dolostone, Devonian marl, dolostone, sandstone, red clay at y 74–76 (69–80 m), the deeper sandstone/claystone/marl/limestone package, and slate (gneiss stand-in without Geology Additions) from y 15 (952 m). A 16 × 16 shaft cut beside spawn: [overview](bench-tiles-shaft-overview.png), [near the basement](bench-tiles-shaft-basement.png). Vanilla ore deposits still sit in the deep sandstone (ores by layer are not built).
- **On the AgentBridge bench, before the refactor** (1:78 world, spawn Riga): every material resolved with no fallback, and the server log shows no generation errors. The column under spawn reads block for block as `/earth geology` describes it: soil-medium at y 110, silt, sand, gravel, grey till, then dolostone, Devonian marl, dolostone, sandstone, red clay … granite from y 16. The beach is plain sand, and the Gulf of Riga floor is 4 m of silt over dolostone.
- **Not checked:** how it looks in game (textures were not viewed), performance, and a long walk across the region.

**Known gaps.**
- IGME 5000 at 1:5M cannot separate Pļaviņas dolomite from the Amata/Gauja sandstone: Cēsis and Daugavpils get the carbonate package on top where the real subcrop is sandstone. The tests report these as known gaps. The fix is the LVĢMC 1:200k pre-Quaternary map (CC0).
- Outside Latvia the Quaternary thickness is a per-recipe guess: Tallinn gets 20 m against the real 5–10 m.
- EMODnet's 1:250k layer leaves parts of the open Baltic unmapped (for example off Liepāja); those floors get the default mud.
- The Baltic template is the only one. The model applies it wherever no other template answers, but the game only switches ground columns on inside the extent. Handing the bedrock to regional geology outside templated regions (global-design.md, section 5) is not built yet.
- IGME 5000's terms are unchecked, so its tile is for the prototype and tests only.
- Layers follow each column's own top. At depth, where one block is about 80 m, a 10-block hill shifts deep contacts by 10 blocks.
- Caves by rock type, the water table and the habitat-composed top block are not in the prototype. Vanilla caves still cut the cover.
- Vanilla ore deposits do not list dolostone as a host; ores by layer come next.
- Geology Additions' dolostone textures lose to Earth's when both are installed (Earth loads later). A small separate content mod would avoid that.
- The `release/1.0` packaging check fails before this work: the data packs require Earth 1.0.2.

## Ores in this column (later)

Each layer carries age and type, so coal can be limited to coal-bearing formations (none in Latvia; the Early Carboniferous of Kurzeme is marine), bog iron to peat, and the deep iron to the basement at its real depth (Staicele 688 m, 88 blocks on the starting curve). This replaces the rock-type-only check that puts coal in Latvia today.

## Decisions for the author

1. Settled 6 November: layers in metres, the block curve a retunable setting, starting at 2,000 m to the floor with sea level 110. Still open: the valley trade-off above.
2. Settled 6 November: the prototype's new blocks are till, silt, marl and dolostone (lake marl as a marl variant). Still open: siltstone and gneiss, or proxies.
3. Clay as continuous layers where mapped.
4. Settled 6 November: chernozems are natural "high" fertility, with a topsoil thicker than one block. Still open: high for kastanozems, phaeozems and vertisols.
5. **Split with the habitat proposal** ([habitats](../habitats-2026-11-06/README.md), section 3a; agreed between the two sessions on 6 November, pending the author). The ground column decides soil group, texture, topsoil thickness and fertility, Quaternary cover and its thickness, each layer's permeability (very low: lake and varved clay, raw clay layers, claystone, shale, unbroken basement; low: till, marl, silt; medium: topsoil, Devonian sandstone, fractured basement top; high: outwash, beach and dune sand, gravel; karst: dolostone, limestone and gypsum where karstic; peat special), and peat thickness. Earth geography computes the water table from terrain, rivers and **all** layers, peat included. The author (6 November) corrected an earlier draft that left peat out as "saturated": sand can be waterlogged and peat can be dry. Wetness is where the table lies relative to a layer, for any material; permeability only shapes the table. Peat must take part: its low permeability is what holds a raised bog's water mound above the surrounding table. The loop is avoided by order, not exclusion: mapped peat (Earth's mapped peat, PEATMAP, GLWD, the Latvian peat areas) is an input layer; small unmapped kettle and valley mires are then picked from that table and not fed back (their effect on the regional table is negligible; one deterministic pass). The habitat decides moisture, habitat, surface look, microrelief and plants; plants read waterlogging from the table and richness from the soil, both downstream. Where soil data cannot tell gleyic from non-gleyic, the ground column reads the water table. The top block is written once: the soil pass asks the ground column for material and thickness and the habitat for the surface variant, and composes the block; microrelief and pools edit after it.
6. **Caves, settled 6 November** (the author: "Yes, by rock type"; earlier, to the habitat session: "we will need to get rid of caves at least on top"; the habitat proposal removes vanilla cave carving above the water table and through the soil and cover). The ground column's view: none in soil or the Quaternary cover at any depth (till, clay, sand, silt and peat hold no open caves), so the cut is the base of the cover, in metres per column; in rock, caves by material: karst in limestone, dolostone, chalk, gypsum and halite, lava tubes in basalt, few or none in insoluble rock; air-filled caves belong above the water table (below it real caves are flooded, which Hydrology's seep rule could give). The author, same day: "we keep toggle in UI for vanilla caves still", so a world-creation setting chooses vanilla caves or caves by rock type; default caves by rock type (the author, same day).
7. Data: download Pelletier with an Earthdata account (≈1 GB), the GUM polygons (1.3 GB), HWSD v2; check BGR's terms for shipping grids made from IGME 5000.

## Files

- [baltic-real-columns.md](baltic-real-columns.md): sourced real columns and open national data.
- [baltic-sites-datasets.txt](baltic-sites-datasets.txt), [quaternary-thickness-latvia.txt](quaternary-thickness-latvia.txt), [fertility-report.txt](fertility-report.txt), [seabed-baltic.txt](seabed-baltic.txt): per-site readings.
- [scripts/](scripts/): the Python used (projections written out by hand; pyshp for shapefiles). Downloaded data (SoilGrids clips, CRUST1.0, IGME 5000, the Latvian grid) is kept outside the repository.
