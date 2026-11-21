# Audit: Earth ground columns (main 1ac0f2d = v2.1.4)

Read-only audit, 18 November 2026. Nothing in `C:\VS mods\Earth` was edited, built or committed (`git status` clean).
Measurements come from a scratch probe that compiles `src\Core\*.cs` into its own assembly and runs `GroundColumns.Load`,
`At` and `Build` and `OreLayers.CodeOf` exactly as the game does. Inputs: the repo's rules and 2′ tiles, plus the 30″ sets
and GLO-90 store in `%APPDATA%\VintagestoryData\ModData\earth`. Probe and outputs:
`scripts\ground\`
(`probe\Program.cs`, `out\probe-*.txt`, `out\distances.txt`, `out\boxes.txt`, `py\*.py`).

What the offline probe cannot see:
- Earth's rivers and lakes. The global sample is dry land with `NearRiver` false, plus a second "valley" pass with
  `NearRiver` true that shows what an Earth river valley would get.
- Relief, except at the 20 site boxes, which use GLO-90 relief exactly as `GroundColumnsTests` does.
- The yearly temperature. With no temperature, every karst cell reads warm `residual-karst`; in rules 2 worlds karst
  below 12 °C becomes `rendzina`. So terra rossa and the "laterite class" shares here are overstated outside the warm
  belt.

Shares are area-weighted (cos lat) over the land that SoilGrids maps, 84° N to 56° S, sampled every 0.1°: 1.57 M cells.

Severity is by player impact on a ground-column world (the planned 1:10 no-wipe server, if it uses ground columns).
"Verified" means read in code or rules and reproduced in the probe. "Suspected" means read in code or data, with a
plausible failure that was not reproduced end to end.

---

## 1. Pipeline map

| # | Stage | Function (file:line) | Inputs | Output |
|---|---|---|---|---|
| 1 | Bake: GUM cover unit, 0.5° | `bake_baltic.gum_global` (tools/ground/bake_baltic.py:48; `GLIM_EMPTY` :43, `COAST_CELLS=3` :45) | GUM 0.5° ASCII grid, GLiM 0.5° grid | `global-gum.egt`. Empty land cells get R*/Wu/Iy/Pu/Du from GLiM's class. GLiM `su` cells get the nearest GUM unit at any distance. Sea cells within 3 cells (1.5°) of land get the nearest land code. |
| 2 | Bake: CRUST1 basement depth; Baltic correction | `crust1_global` :84, `basement_baltic` :92 | crust1.bnds (top of sediments minus top of upper crust); borehole CSV | `global-crust1.egt` at 1°, `baltic-basement.egt` at 0.05° |
| 3 | Bake: national and regional tiles | `latvia_quaternary` :114, `igme_subcrop` :129, `soilgrids` (Baltic) :156, `seabed` :182 | Popovs 250 m, IGME 5000, SoilGrids 0.01° clips, EMODnet 1:250k | Baltic and Latvia tiles (priority 200–300) |
| 4 | Bake: world sets at 2′ | `bake_global.soil` :51, `shangguan` :89, `glaciation` :165, `permafrost` :195, `provinces` :227, `ages` :237 | SoilGrids WCS (classes from the 30″ mode), Shangguan BDTICM 1 km, Batchelor 2019 (Northern Hemisphere only), Obu 2019 (Northern Hemisphere, overview level), Hasterok 2022, Chorlton 2007 | 10° EGTS tile sets |
| 5 | Bake: 30″ sets | `bake_30s.soilgroup` :39, `shangguan` :74 (log1p byte, 2.5 % steps, cap 570 m), `permafrost` :108 (north of 25° N); `fetch_soilgrids_30s.mode16` :46 (Acrisol/no-data split :62) | SoilGrids at 7.5″, BDTICM, Obu | `ground/global-*-30s.egts` (release egts-v1) |
| 6 | Download and choice of the 30″ sets | `GroundDownloader` (src/Core/GroundDownloader.cs:52–77); `EarthGeology.GroundSets` (src/EarthGeology.cs:235–306) | GroundRepo, SHA-256 | `EarthParams.GroundData` (2 = 30″, 1 = 2′ for good) |
| 7 | Tile reading | `GroundTile.Read/Unpack/Cells` (src/Core/GroundColumns.cs:140, 184, 219; 192 MB LRU budget), `TryLinear` :267 (bilinear, nodata corners dropped, log1p in code space), `TryCategory` :294 (warped about 0.8 cell, unwarped where the warp leaves the tile) | EGT1/EGTS | Field values |
| 8 | Tile stack | `GroundData.Add/TryNumber/TryCategory` (:337, :351, :358) | Priority list | First tile with data |
| 9 | Depth curve | `DepthCurve.For/Fit/Blocks/Metres` (:451–503) | 1/`VerticalScale.Slope(0)`, sea level, Earth-tab choice | Metres → blocks |
| 10 | Model load | `GroundColumns.Load` :415, constructor :762–935 | `ground-rules-v1.json` (one file for every rules version), tiles | Recipes, regions, templates and so on |
| 11 | Place | `EarthGeology.GroundAt` (src/EarthGeology.cs:826–856), `ReliefFor` :861–871 → `GroundRelief.Field` (src/Core/GroundRelief.cs:27) | Terrain column (Elev, Water, River, NearRiver, Beach, Shore), climate (aridity, temperature), floodplain grid, relief | `GroundColumns.Place` |
| 12 | Sample | `GroundColumns.At` (:972–1026): soil → cover unit → regions (first match) → thickness (data, thickness rule, relief factor, recipe cap) → basement → subcrop → seabed (tile, else zones) → frozen | Tiles, rules, place | `Sample` |
| 13 | Column | `Build` (:1234–1376): top (soil, peat, beach, shore, seabed) → cover shares (`Vary` :1150, frozen split, `AddLayer` compaction :1134) → template beds (`TemplateAt` :1383, era or subcrop start unit, stretched to basement or cut) → basement (`BasementAt` :1393). Blocks via `B()` :1129 and `Put()` :1131 (layers of 0.5 m or more keep one block, `Column.AddBlocks` :1101) | Sample, surface | `Column` (blocks plus segments) |
| 14 | Shore material | `ShoreSubstrate` :1168 (decision 0070, shore rules 1–3) | OSM class, cover, slope, seabed | Sand, gravel, mud or bare rock |
| 15 | Write | `EarthGeology.GroundPass` (src/EarthGeology.cs:799–824) → `EarthGround.Write` (src/EarthGround.cs:144–166) | Chunk, height map, fluid above the top | Blocks from the top down; a hand-off column stops below its cover |
| 16 | Ore layers | `EarthOreLayers.Record` (src/EarthOreLayers.cs:374) → `OreLayers.CodeOf` (src/Core/OreLayers.cs:270), `RegionalCode` :299, `Allows` :225, `SurfaceRuleOf` :248; layer pass `Apply` (EarthOreLayers.cs:467) | Segments, Chorlton era, regional profile | Allowed layer per deposit block |
| 17 | Other readers | `GroundColumnAt` (EarthGeology.cs:931, water table), RockColumn host search (EarthGeology.cs:1023–1035), `/earth geology` (:1148–1153); `WaterTable.BlocksAt` (src/Core/WaterTable.cs:375); ore catalogue bodies `OreDeposits.ColumnBlocks/Window` (src/Core/OreDeposits.cs:668–673, 784–788) | Same model | Readings |
| 18 | Vanilla deposit gating | `EarthGeology.FilterDeposit` (src/EarthGeology.cs:1094–1100) | Deposit code | Vanilla clay, redclay and peat off wherever columns cover; gravel, soil, surface salt and bog iron off with ore layers |
| 19 | Rock floor | `RockFloorPass.Fill` (src/RockFloorPass.cs:81) | Floor 160 blocks below the chunk's lowest surface (worlds over 512 high) | Plain rock below |

---

## 2. Findings by function

### F1. Raw clay exists only as recipe sub-layers; vanilla clay is off everywhere (High, Verified)

**Evidence**
- `EarthGeology.FilterDeposit` returns false for `clay`, `redclay` and `peat` wherever ground columns cover a column
  (src/EarthGeology.cs:1099–1100). The rules' extent is the whole world (ground-rules-v1.json:24).
- Clay then comes only from these layers:
  - `clay-red` and `clay-blue` in the lakeclay, lake, alluvium, marine, delta, sediment, peat and playa recipes
  - the Baltic template's clays (deep)
- Till, thin-till, knob-till, residual, saprolite, laterite, loess, colluvium, dune, sand-sheet, calcrete, thin and
  karst recipes have no clay at all.
- The till block drops only itself and granite stones (assets/earth/blocktypes/till.json:30–32).
- No recipe turns silt, loess, till or terra rossa into clay (assets/earth/recipes holds only barrel and grid recipes
  for tephra, nuggets and refractory).

**Measured (1:10 curve)**

| Where | Clay within the top 4 blocks | Over 100 km from such a column | Over 200 km |
|---|---|---|---|
| All land, away from rivers | 19.9 % of land | 20.3 % | 5.9 % |
| Glaciated land (17.4 % of land) | 7.5 % | 31.7 % | 10.1 % |

- If every cell were an Earth river valley, clay would be within reach on 91 % of unglaciated land but only 8.4 % of
  glaciated land.
- Sites with 0 % of columns holding raw clay: Ballarat, Mother Lode, Otago, Cornwall, Nizhny Tagil, Bodaybo, Kinta,
  Suriname, Lemmenjoki, Vistula at Warsaw (out\probe-sites.txt).

**Failure scenario.** A 1:10 spawn on the Swedish, Finnish or Canadian till plains, on a Mediterranean residual
upland, or on a shield. Pottery, the first craft, has no clay within 10–20k blocks. The only exceptions are lake
floors (lake clay under water, which the probe did not count) and GUM lake-clay cells.

Real till is mostly clay-rich boulder clay, and residual soils carry clay horizons.

### F2. River valleys and floodplains on formerly glaciated ground get no alluvium (High, Verified)

**Evidence**
- In ground-rules-v1.json the regions `glaciated` (:134–139), `peatland-glaciated` and `peatland` (:119–120) and the
  eolian regions (:121–127) all come before `river-valley` (:140) and `floodplain` (:141). Regions are first match
  first (GroundColumns.cs:983–987).
- So on glaciated ground a river valley over Rx, Us, Ca, Cu or a default unit becomes thin-till or till, an eolian
  unit becomes dune-on-till, and Histosols become peat-on-till.

**Measured**
- Glaciated river valleys: alluvium-on-till 4.4 % and outwash 3.8 %, against till 42.5 %, thin-till 24.1 % and
  peat-on-till 11.3 %. Alluvial or outwash class in only 7.9 % of glaciated land.
- Sites:
  - Vistula at Warsaw: dune-on-till, no alluvium.
  - Daugava at Pļaviņas: till.
  - Kolyma at Susuman (a world-class placer district, "older" glaciation in the data): peat-on-till, 0 % alluvial.
  - Bangka (tin placers): peat, which shadows the river valley.
- This contradicts the river-valley rule's own note ("their bed, banks and valley floor take alluvium where the map
  has rock …") and decision 0060's "placer tin and gold in alluvium".

### F3. Frozen alluvium cannot host placers under vanilla ores (High in placer belts, Medium overall; Verified)

**Evidence**
- The frozen rule turns sand and gravel below 1 m into `permafrost-sand` and `permafrost-gravel`
  (ground-rules-v1.json:402–409; GroundColumns.cs:1301–1309).
- Every placer deposit's `inblock` is `game:gravel-granite` or `game:sand-sandstone` only
  (assets/earth/worldgen/deposits/earth-layers.json:10, 24, 38, 52, 67, 81).
- EA-0058's fix ("a frozen loose block takes a deposit as the block it froze from would") is in ore rules v5, the
  experimental ore maps only.

**Measured.** 5.4 % of alluvial-recipe land is frozen (0.73 % of all land). That is the Yukon, Alaska, Kolyma and
Lena placer belt. Kondyor's alluvium is frozen.

**Failure.** Under the default vanilla ores, ground-column worlds have no placer gold or platinum in permafrost
valleys.

### F4. Ground rules cannot be versioned except through conditions, and nothing pins them (High process risk for a no-wipe server; Verified)

**Evidence**
- `RulesAsset` is fixed to `ground-rules-v1.json` (GroundColumns.cs:380). The constructor demands that the file's
  `version` equals the build's `RulesVersion` (:769), so every saved world, rules 1 or 2, reads the newest file.
- Only `Condition` objects can carry `"rules": [from, to)` (:670): regions, thickness rules, seabed zones, frozen and
  relief `when`.
- These cannot be versioned at all:
  - recipes (shares, max, vary, typical and max metres), `materials` candidate lists
  - soils (fertility, topsoil metres), compaction, frozen material pairs, relief constants
  - templates, the basement mix, seabed classes, `minLayerMetres`
- `SavedWorldDigestTests` pins the terrain (`TerrainModel.Column`) only. No test hashes ground columns or ore-layer
  codes at fixed sites.

**Failure.** Any fix to a recipe share, a material fallback or a soil depth silently changes the next chunks of every
existing ground-column world, so seams appear beside old chunks. That breaks decision 0002 on the public server.
Ore-layer rules, by contrast, are one file per version (good).

### F5. Recipe sub-layers under 0.5 m vanish; thin covers disappear at every scale (Medium, Verified)

**Evidence**
- `Put` keeps a layer only if `to − from ≥ MinLayerMetres` (0.5; GroundColumns.cs:1131). Otherwise it merges into the
  layer above (:1109–1111).
- The cover loop slices q into recipe shares first (:1293–1311), and the topsoil eats the first 0.15–1.2 m (:1299).
- Synthetic columns (out\probe-synthetic.txt), the same at 384 and 1024 high:

| Alluvium thickness q | Result |
|---|---|
| 0.6 m, 1 m | Topsoil only: the alluvium vanishes |
| 1.5–3 m | One sand block |
| About 5 m | Gravel appears (sand, gravel, sand) |
| About 10 m | Blue clay appears |
| 1 m thin-till | Vanishes |
| Laterite on Lixisols, 3 m | Loses its ironstone crust |

- Globally:
  - 0.27 % of land (2 % of alluvial-recipe land) has an alluvial recipe but no alluvial block.
  - 3.3 % of land has no loose cover block at all.
- The docs say "every layer of 0.5 m or more keeps one block" (global-design §2). That is true for each synthetic
  share, not for each real deposit.

### F6. Every wet river bed is 1 m of silt, which cannot be panned (Medium, Verified)

**Evidence**
- `seabed.riverBed = {m: silt, metres: 1}` (ground-rules-v1.json:274); Build uses it on `Surface.RiverBed` (:1280).
- Vanilla's pan takes `@(sand|gravel|sandwavy)-.*` and bony soil (survival pan.json). `earth:silt` has no pannable
  attribute (silt.json).

**Failure.**
- Panning in streams, the classic early source of flint (11 %) and nuggets, finds silt in every river of a
  ground-column world. Mountain torrents are silt too.
- Placer hosts lie under the silt.
- Only the 0076 "river flint" stones (1 %, glacial covers only) help.

Dry beds are fine: they get sand or gravel from AlpineSurfacePass (AlpineSurfacePass.cs:136–142).

### F7. Floodplain alluvium follows a narrow river strip plus 0.5° GUM cells (Medium; Verified rules, Suspected widths)

**Evidence**
- Valley alluvium needs `NearRiver`, which is the river's water, bank and valley (TerrainModel.Rivers.cs:97, 224,
  484, 874).
- At 1:10 (GLO-90 cell 92.6 m, BankMetres 15) the strip reaches past the water edge by about:

| Discharge Q | Strip past the water edge, each side | Total width |
|---|---|---|
| 100 m³/s | About 125 m | About 330 m |
| 1000 m³/s | About 190 m | About 630 m |

  These come from RiverNetwork.cs:427–497 by hand. Real floodplains are kilometres to tens of kilometres wide.
- Dry (arid) river channels set `NearRiver = false` (TerrainModel.Rivers.cs:107, 231), so wadis get no alluvium cover.
- Broad alluvium comes instead from GUM's 0.5° alluvial cells: 12.7 % of land is alluvium recipe. That includes GLiM
  `su` cells that borrowed the nearest GUM unit, such as the Amazon interfluves.
- Fluvisols (1.7 % of land) add 77 % alluvium; glaciated Fluvisols do not (F2).
- Result: alluvium shows as 55 km blobs with 15 km wavy edges, plus a thin strip along rivers. Real floodplains outside
  them are residual, colluvium or "sediment" (Ganges at Patna: 91 % "sediment", not the alluvial class; Rhine at
  Strasbourg: the GUM cells split 48 % alluvium and 51 % loess).

### F8. The evaporite flag covers almost every Phanerozoic bed (Medium, Verified)

**Evidence.** `evaporiteRocks` = gypsum, claystone, shale, mudstone, dolostone, limestone (ore-layers-v4.json). Any
such bed not of Precambrian age is an evaporite bed, in templates (OreLayers.cs:290) and in regional rock
(OreLayers.cs:302).

**Measured.** Every generic-template limestone, shale and claystone bed reads "evaporite" (probe-synthetic, Mesozoic
and Paleozoic platform columns).

**Failure.**
- "Salt only in evaporite beds" (0060) does not narrow halite, sylvite or Earth's gypsum to evaporite basins.
- Borax and kernite are allowed in any Cenozoic shale or limestone.

### F9. Peat comes from Histosols only, so peat and bog iron are absent from most of the world (Medium, Verified)

**Evidence.**
- Peat recipes come only from Histosols (peatland regions) or GUM O* units.
- Vanilla peat deposits are off (F1). Gleysols and the GLWD wetlands Earth already has are not used.

**Measured.**
- Peat class on 3.3 % of land.

  | Area | Present | Over 100 km from peat | Over 1000 km |
  |---|---|---|---|
  | Unglaciated land | 1.9 % | 89 % | 47 % |
  | Africa | | | 63 % |
  | Australia | | | 92 % |
  | Europe | 4.2 % | 63 % | |

- Bog iron, Earth's only iron in peat country, and peat fuel are reachable from a minority of spawns.

### F10. Cover type is a 0.5° map or a soil-group proxy on almost all land (Medium-Low, Verified; data limit)

**Cover-unit source**

| Source | Share of land |
|---|---|
| GUM mapped sediment cell (0.5°) | 48.6 % |
| R* (no sediment mapped; the rock class from GLiM) | 43.8 % |
| GUM unit borrowed from the nearest cell (GLiM `su`) | 3.5 % |
| Other fill | 3.0 % |
| GLiM-derived Wu/Iy/Pu/Du | 0.95 % |

**Recipe decided by a region rule** (soil group, glaciation or place) on 40.8 % of land. The largest swaps:

| Region recipe | Replaces | Share of land |
|---|---|---|
| saprolite | residual | 10.6 % |
| thin (Leptosols) | residual | 3.35 % |
| thin-till | residual | 2.6 % |
| loess | dune | 2.3 % |

Suspected misreads:
- Eolian GUM units on clayey Calcisols or Luvisols become loess: 22 % of arid central Australia (30–20° S, 120–140° E)
  and some Sahel cells (loess thicker than 100 m in the 10–20° N boxes; uncapped recipe).
- Temperate unglaciated uplands are "residual" (saprolite plus regolith, up to 20 m): 28 % of Western Europe at
  45–55° N, 45 % of the Mediterranean.

### F11. Thickness: data, caps and missing compaction (Low-Medium, Verified)

**Thickness source**

| Source | Share of land |
|---|---|
| 30″ Shangguan | 94.9 % |
| 2′ Shangguan | 0.3 % |
| Latvia 250 m | 0.05 % |
| `typicalMetres` (rule) | 4.8 % |

The rule fills Arctic thin-till and till, and tropical saprolite where Shangguan has no data.

**Recipe caps** bind on about 32 % of land, so there the cap is the thickness model:

| Recipe (cap) | Share of land at the cap |
|---|---|
| residual (20 m) | 5.1 % |
| thin (2 m) | 4.4 % |
| residual-karst (4 m) | 3.8 % |
| dune (60 m) | 3.8 % |
| alluvium (80 m) | 3.4 % |
| colluvium | 2.3 % |
| saprolite | 2.1 % |
| sand-sheet | 1.7 % |
| calcrete | 1.7 % |
| thin-till | 1.6 % |

Thickness rules applied: `sand-sea` 5.2 %, `shield-cover` 5.2 %.

**Uncapped recipes** take Shangguan's basin depths:

| Recipe | Largest thickness | Notes |
|---|---|---|
| lake | 571 m | |
| playa | 492 m | Median 77.5 m |
| loess | 431 m | |
| till | 234 m | |
| alluvium-on-till | 220 m | |

Till, loess, peat, colluvium, regolith, saprolite and laterite have no compaction depth, so they stay loose at any
depth (ground-rules-v1.json:382–393).

**Relief.** The relief factor is divided by its neighbourhood norm, but the recipe cap after it (GroundColumns.cs:1013)
breaks the "total kept" promise in capped hollows. That is minor.

### F12. Sea floors inside GUM's 1.5° coastal fill take land recipes (Low, Verified)

**Evidence.** The deep-sea regions map only `default` (ground-rules-v1.json:113–118). The 3-cell fill gives 11.6 % of
the sea and no-soil area a land unit: 6 % R* rock codes, the rest alluvium, sediment, coastal, till and so on
(probe-global).

**Examples** (probe-extra):
- Black Sea centre at −2200 m: colluvium.
- Red Sea axis at −1500 m: alluvium, so the alluvial (placer) class on the deep floor.
- Aegean at −800 m: "lake" (GLiM water code Wu).

Shangguan has no data at sea, so the recipe's `typicalMetres` applies, often swallowed by the 2–4 m seabed top.

### F13. Seams along tile and box edges (Low)

**Verified**
- `GroundTile.TryCategory` drops its warp when the warped point leaves the tile (GroundColumns.cs:301). Within about
  0.8 cell of every 10° tile edge, category contacts follow the source cell edges: about 74 blocks wide for 30″ soil
  at 1:10, about 3 km for 2′ fields.
- The Baltic area template (rules :328, GroundColumns.cs:1385–1386) replaces regional rock inside 20–29° E,
  53.5–60° N, giving straight bedrock seams along 29° E, 53.5° N and 20° E.
- The Baltic soil tile (priority 200) and the Latvia thickness grid (priority 300) also change at their borders.

**Suspected.** Bilinear interpolation stops at 10° tile edges (`TryLinear` clamps inside the tile, :271), a one-cell
step on the seam.

### F14. Age of regional beds below a hand-off column (Low-Medium, Verified)

**Evidence.** `EarthOreLayers.Record` stores one `RegionalAge` per column from Chorlton's surface era
(EarthOreLayers.cs:386). Every regional bed below the cover gets that one age.

**Measured.** Chorlton's era is missing on 4.4 % of land and is a range ("Mesozoic-Cenozoic" and the like, which
counts as Unknown and passes every age rule) on 4.6 %. 21.9 % of land is Precambrian, so it has no coal and no
evaporites in its regional beds.

**Coal rules are lenient:**
- bituminous coal is allowed in Cenozoic beds
- lignite is allowed in Mesozoic beds
- generic Paleozoic counts as "paleozoic", which takes bituminous coal and anthracite

Real Carboniferous coal under a Mesozoic or Cenozoic Chorlton cell keeps its coal, but may also take lignite. In
platforms built from the province template the units carry their own ages (correct).

### F15. Smaller ones

- **Kaolin host** (Low, Verified). The kaolin deposit's host is saprolite only (earth-layers.json:180). Ore layers and
  0060 also allow laterite, but no laterite block can take it.
- **Till stones** (Low, Verified). Till drops `stone-granite` everywhere (till.json:32), whatever the region's
  erratic rock.
- **Ore catalogue depths** (Low, Verified). Catalogue bodies (ore maps, experimental) turn metres into blocks with the
  bare curve (OreDeposits.cs:672, 786–787). Written columns are shifted down by kept thin layers. The water table uses
  the column-aware `BlocksAt` (WaterTable.cs:375). The bodies' wide windows (at least 8 blocks) hide most of it.
- **Surface mismatch** (Low, Suspected).
  - The writer takes the surface from the chunk's fluid above the top (EarthGeology.cs:817–818).
  - The readers take it from the terrain model (`SurfaceOf`, :873): the water table, the RockColumn host search and
    prospecting, and `/earth geology`.
  - Where the two disagree, a reading rebuilds a different column.
- **Old ground pack** (Low, Suspected). Assets win over the hashed data store (EarthGround.cs:51–52), so an old
  `earthgrounddata` pack left installed silently changes the 2′ tiles of a server.
- **Southern Hemisphere** (Low, Verified). Batchelor ice and Obu permafrost are Northern Hemisphere only
  (bake_global.py:165–193, 195–219; bake_30s.py:108).
  - Patagonia and New Zealand get no glacial logic.
  - The Andes and Southern Alps get no permafrost.
- **Full column** (Low, Verified). `Column.Max = 64` (GroundColumns.cs:1046). When full, the last layer absorbs the
  basement's end (:1114). Not reached by the current templates.
- **Salt crust** (Low, Verified). It caps at exactly `minLayerMetres` (max 0.5), so it shows only on arid playas with
  q ≥ 16.7 m. It never shows on non-arid Solonchak playas, because the topsoil eats it.
- **Relief lattice** (known). The lattice can differ between chunks that are neighbours north–south
  (GroundRelief.cs:12–14, CR-T9).

Checked and not a problem:
- Mountain valley floors are not turned into "thin" by the slope test: 0–2 % of valley floors at the placer sites.
- The 30″ download and choice logic.
- Data-store hashes.
- Compaction and frozen splits.
- Basement cut and stretch.
- Relief normalisation.

---

## 3. The depth curve

`DepthCurve.For(choice, 1/VerticalScale.Slope(0), seaLevel)` (GroundColumns.cs:487). It depends on world height and
height curve only; the horizontal scale does not enter. Sea level is 110 for every Earth budget. The lowland column has
114 blocks. Default choice is `log4000`.

**Pure curve, `log4000`**

| World (presets) | m per block at top | 1 m | 10 m | 100 m | 1 km | 2 km | m per block at 1 km |
|---|---|---|---|---|---|---|---|
| 256 high, log1000 (auto below 512) | 15.9 | 0.06 | 0.63 | 6.1 | 46.5 | 75.7 | 28.3 |
| 384 high, log1000 (1:200, 1:100, 1:50) | 8.41 | 0.12 | 1.17 | 10.6 | 59.6 | 85.2 | 29.5 |
| 512 high, linear (1:25) | 22.1 | 0.05 | 0.45 | 4.4 | 38.9 | 68.9 | 29.7 |
| 1024 high, linear (1:10; 1:5 at the maximum height) | 9.70 | 0.10 | 1.02 | 9.4 | 56.9 | 83.4 | 28.9 |
| 1024 high, log1000 (chosen by hand) | 2.51 | 0.40 | 3.7 | 24.8 | 76.5 | 95.0 | 38.1 |

`terrain` (linear) at 1:10 gives 103 blocks for 1 km. `linear2000` is 17.5 m per block (out\probe-curve.txt).

**What a built column actually gets** (it ignores the curve near the top):
- **1 m of soil:** the topsoil gets `max(1, ceil(m − 0.15))` blocks at every scale (GroundColumns.cs:1256). That is
  1 block, or 2 for Chernozem's 1.2 m.
- **10 m of till:** 3–4 blocks at every height (topsoil, reddish till, sand, grey till), because every share of
  0.5 m or more keeps a block. The curve alone would give 0.5–1.2.
- **100 m of sediment:** the curve's 4–11 blocks, plus 3–5 for the kept thin layers. Example: 80 m of "sediment" is
  8 blocks of cover at both 384 and 1024 high.
- **1 km to basement:** the curve's 39–60 blocks plus that shift. A platform column at 1000 m reaches basement at block
  57 (1024 high) or 60 (384 high). With 3 km of beds it is block 101–102.
- This applies only where ground columns own the bedrock: 28 % of land (regional geology unknown or covered) and the
  Baltic box. Elsewhere (72 %) the column hands off below its cover and regional geology's beds remain.

**What gets rounded away.**
- Any recipe share under 0.5 m. A 1 m alluvium is lost at every scale, 1:200 included (F5). It is the split into
  shares that loses it, not the curve.
- Gravel appears from about 3.5–5 m of alluvium, blue clay from about 5–10 m, an ironstone crust from about 3.3 m of
  laterite.

**Against the horizontal scale.**
- The top of the curve equals the terrain's own vertical scale. That is deliberate (the author, 6 November: digging
  should not reach deeper than real next to the terrain).
- Horizontal metres per block are about N for 1:N. So the vertical exaggeration at the top is:

| Scale and height | Vertical exaggeration at the top |
|---|---|
| 1:200 at 384 | 24× |
| 1:100 at 384 | 12× |
| 1:50 at 384 | 6× |
| 1:25 at 512 | 1.1× |
| 1:10 at 1024 | 1.03× |
| 1:5 at 1024 | 0.52× (one block down is 9.7 m, one block across is 5 m) |

- Below the knee the strong log compresses further: about 29 m per block at 1 km at every height.

**Do the ore and water code use the same curve?**
- Yes for anything built by `Build`: the layer table, the layer pass, prospecting, `RegionalRock`, and the water table
  through `BlocksAt`.
- Only the ore catalogue bodies (experimental ore maps) use the bare curve (F15).

---

## 4. Data coverage

**Fields and their sources**

| Field | Source and resolution | Share of land it answers | Fallback |
|---|---|---|---|
| Soil group | SoilGrids 2.0 WRB at 30″ (mode of 16 full-resolution pixels) | 99.5 % | 2′ 0.22 %, Baltic 0.01° 0.25 % |
| Clay and pH (fertility bump only) | SoilGrids at 2′, from the WCS's averaged overview | — | — |
| Cover unit | GUM 0.5°, warped about 15 km | See F10 | — |
| Cover thickness | Shangguan BDTICM 30″ (modelled; known thick on shields and in deserts, hence two thickness rules and caps) | 94.9 % | 2′ 0.3 %, Latvia 0.05 %, rule 4.8 % |
| Bedrock era | Chorlton 1:35M at 2′ (eras only, no periods) | 91.0 % a single era | Range eras 4.6 %, none 4.4 % |
| Province | Hasterok at 2′ | 99.9 % | — |
| Basement depth | CRUST1 1° | 99.75 % | Baltic corrected 0.25 %; read only where ground columns own the rock (28.1 % of land) |
| Glaciation | Batchelor 2′, Northern Hemisphere only | 17.4 % of land glaciated (LGM 11.9 %, older 5.5 %) | — |
| Permafrost | Obu 30″, north of 25° N | 10.2 % of land frozen | — |
| Seabed | EMODnet, Baltic only | — | Zones by depth, latitude and glaciation elsewhere |
| Shore | OSM beaches (6″) | — | The cover rule (0070) |

**Recipes on land**

| Recipe | Share of land |
|---|---|
| alluvium | 12.7 % |
| saprolite | 12.4 % |
| residual | 11.0 % |
| till | 7.8 % |
| loess | 6.6 % |
| dune | 5.3 % |
| colluvium | 5.2 % |
| thin | 4.8 % |
| residual-karst | 4.5 % |
| thin-till | 4.2 % |
| sediment | 2.9 % |
| sand-sheet | 2.1 % |
| lake | 2.1 % |
| calcrete | 2.1 % |
| residual-mafic | 2.0 % |
| peat-on-till | 2.0 % |
| peat | 1.6 % |
| tephra | 1.15 % |
| laterite | 1.07 % |
| playa | 0.96 % |
| outwash | 0.87 % |
| lakeclay | 0.86 % |
| gypcrete | 0.79 % |
| alluvium-on-till | 0.77 % |

**Layer classes present in the column (1:10 curve, away from rivers)**

| Class | Share of land |
|---|---|
| Saprolite | 28.0 % |
| Laterite, including terra rossa | 19.4 % (overstated, no temperature) |
| Alluvial | 13.3 % |
| Peat | 3.3 % |
| Playa | 0.96 % |
| Outwash | 0.84 % |

**What decides alluvium:**
1. GUM A* units, mapped or borrowed, give about 12 % of land.
2. Fluvisols (1.7 % of land, 77 % of it alluvium) add about 0.6 % of land over rock or mixed units.
3. Earth's river strip (`NearRiver`) over rock, mixed sediment, loess or dune units, outside glaciated, peat and
   eolian regions. 76 % of a river valley's columns would be alluvium, but the strip is about 120–190 m beside the
   water at 1:10.

Glaciated floodplains are lost (F2). Arid wadis are not counted (F7).

**Other covers**

| Cover | Where it comes from | Extent |
|---|---|---|
| Laterite | Recipes carrying a laterite layer: Ferralsols, Acrisols, Nitisols and Alisols as saprolite; Plinthosols and Lixisols as laterite; sand-on-saprolite | 36 % of tropical land; 9.8 % at 23.5–30°; 0.6 % poleward of 35°; SE USA 25 %, south China 37 % |
| Playa | Solonchaks (60 % of them become playa) plus GUM P* units | 0.96 % of land |
| Loess | — | Pampas 77 %, Ukraine to S Russia 48 %, Great Plains 49 %, Loess Plateau 36 % |
| Karst | GLiM carbonate (Rk) | 4.5 % of land |
| Tephra | Andosols (73 % of them become tephra) plus GUM Iy | 1.15 % of land |
| Till recipes | — | About 13 % of land |

**Permafrost** (Obu ≥ 50 % on land and beaches). Loose cover below 1 m freezes down to its compaction depth. Peat
does not freeze. Lake and river floors stay thawed. Frozen materials host nothing under vanilla ores (F3).

**Ocean and shore.** 88 % of the sea and no-soil area lies beyond the GUM fill and uses the depth and latitude zones
(oozes, red clay, shelf sand and mud). The coastal band takes land units (F12).

---

## 5. Blindspots ranked by player impact

1. **Clay for pottery** (F1). Missing from most interfluves, and from glaciated valleys. 10 % of glaciated land is
   more than 20k blocks from any clay at 1:10. This is the progression rule ("reachable from any spawn") at its most
   basic material.
2. **Placers and gravel in valleys** (F2, F3, F6, F7).
   - Glaciated river valleys hold till.
   - Permafrost alluvium takes no placers.
   - River beds are unpannable silt.
   - Alluvium is a narrow strip plus 0.5° blobs.
   - Kolyma and Bangka have none. Klondike, the Urals and Ballarat get placers only in Earth's river strip.
3. **Frozen rules on the no-wipe server** (F4). Unversioned recipes, soils and materials, and no ground digest.
4. **Peat and bog iron extent** (F9).
5. **Salt and gypsum not tied to evaporite basins** (F8).
6. **Thin real covers vanish** (F5).
7. **Fire clay and kaolin.**
   - Saprolite is absent on glaciated land: 30 % of it is more than 1000 km away, 22 % of North America, 14 % of
     Europe.
   - Fire clay then depends on clay (F1) plus calcined flint (EA-0069), so F1 decides it.
8. **Laterite and bauxite.**
   - Over 1000 km away for 31 % of North America and 16 % of Europe in this probe (more with rules 2's cool karst).
   - A real-world limit; EA-0069 and 0077 provide substitutes.
9. **Loess and eolian misreads** (Australia, Sahel) and "residual" saprolite over temperate Europe (F10).
10. **Resolution at 1:10.**
    - 30″ is about 95 blocks; quantities are bilinear and smooth, categories are warped, except along 10° tile edges
      (F13).
    - The real contrast comes from the GUM 0.5° (5,500 blocks) unit map, whose wavy 15 km contacts ignore terrain.
11. **Bed ages for coal and evaporites** (F14). Basement depth matters on only 28 % of land. CRUST1 at 1° is adequate
    there.

---

## 6. Suggested fixes and tests

Every generation change needs `GroundColumns.RulesVersion` 3, with regions gated `"rules": [3, 1000)` and new recipe
names (not edits) for anything recipe-level, or a new ore-layers or deposits file version.

| # | Fix | Effort | Test |
|---|---|---|---|
| 1 | Add a ground and ore-layer digest to `SavedWorldDigestTests`: hash `Describe(col)` and `OreLayers.DescribeColumn` at about 40 sites × (rules 1, rules 2) × (384 log4000, 1024 log4000), with the 2′ and 30″ sets. Document "recipes, soils, materials append-only; change behaviour via new names plus gated regions", or split the rules per version like ore layers. **Do this before the server.** | M | — |
| 2 | Clay. New recipes for rules ≥ 3: `till` and `thin-till` with a clay lens (boulder clay, for example `clay-blue` share 0.1, vary 1.0, max 3); `residual` and `colluvium` with a clay horizon; or allow vanilla `clay`/`redclay` deposits again under columns but only in cover layers (till, silt, loess, regolith, colluvium, alluvium) through the layer rules. Cheapest: till drops `clay-blue` 0.3 (a drop change, not generation). | S–M | Probe: share of land within 2k, 5k and 20k blocks of clay in the top 4 blocks, at the Finland, Canadian Shield, Mediterranean and Cornwall sites |
| 3 | Glaciated valleys. A gated region `river-valley-glaciated` before `glaciated`: `nearRiver` true on land, beach and riverbed, glaciation older or lgm, units Rx, Rm, Rk, Us, Ca, Cu, Gt, Gu, Gm, Gp, Ed, Eu, Er, Ea and default become `alluvium-on-till` (or a new `outwash-valley`). Put `floodplain` (Fluvisols) before `glaciated` too. Decide whether peatland should still shadow valleys (keep peat on the valley sides only by relief). | M | Vistula at Warsaw, Daugava at Pļaviņas, Kolyma and Lemmenjoki valley floors are alluvial |
| 4 | Frozen placers: an ore-layers v5 and `earth-layers-v2.json` adding placer variants with `inblock` permafrost-sand and permafrost-gravel. New placer blocks, or pan-on-thaw. | S–M | Kondyor and Kolyma valley columns take placers under vanilla ores in OreLayersTests |
| 5 | River beds by flow. `riverBed` becomes a list of conditions (slope, discharge, cover, glaciation): gravel in mountains and on glacial cover, sand on sandy cover, silt only for slow lowland reaches. Needs the reach slope from the river index on Place. | M | Pannable bed shares per site |
| 6 | Thin covers. In `Build` (behind `Place.GroundRules >= 3`), when q ≥ minLayer but a share is under it, give the recipe's first sand or gravel (signature) layer the merged metres instead of dropping everything. Or set `minLayerMetres` per recipe (alluvium 0.3). | M | Alluvium q = 1 m keeps one sand block |
| 7 | Floodplains. Mark land within a few metres of the nearest river's water level (as `FloodplainGrid` does for arid rivers, from GLO-90) for all rivers above a discharge, or use GFPLAIN250m (CC BY 4.0, to 60° N). Treat `DryRiver` as `NearRiver` for the cover. | L (bake plus format) | Rhine, Vistula and Ganges floodplain shares |
| 8 | Evaporites. Ore-layers v5: evaporite only in regional geology's evaporitic profile, GLiM `ev` cells and a salt-basin mask (USGS or Warren world evaporite basins); keep the rock list as hosts. | S–M | Zechstein, Permian Basin and Michigan basins are evaporite; Bavarian Jura limestone is not |
| 9 | Peat. Gated `peatland-wet` region: Gleysols, Histic or Stagnosols where GLWD (WetlandGrid) peat or swamp-forest share ≥ 0.3 gives peat. Or bake PEATMAP (Xu et al. 2018, CC BY 4.0). | M | Congo Cuvette, Western Siberia, Ireland and the Hudson Bay lowland peat shares |
| 10 | Deep-sea regions keyed `"*"` below −200 m on sea surfaces (gated), so coastal-fill land units stop at the shelf break | S | Black Sea and Red Sea floors are ooze |
| 11 | Small items: <br>• category warp across tile edges (sample the warped point through the stack) <br>• a kaolin host in laterite, or drop laterite from the rule and the 0060 text <br>• till stones from the regional rock <br>• one surface source shared by writer and readers <br>• data-store tiles before assets for 2′ ground tiles | S each | — |

Tests missing today:
- A digest of ground columns (#1).
- Placer and clay reachability probes (#2, #3).
- Frozen-host deposits.
- A check that no alluvial recipe column lacks an alluvial block.
- River bed material.

Existing `OreLayersTests` builds placer sites with `NearRiver` true and the Klondike site unfrozen, so it does not
catch F2 or F3.
