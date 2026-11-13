# Fire, winter grass, soil and wetlands (7 November 2026)

The author's list of what the biome steps still lacked: the prairie and the Winnipeg forest (fire), beech's
north-eastern edge, trees and shrubs by soil, wetlands (data), the December fix on release/1.0 and the Dalmatia check;
plus grass in cold climates that turns brown, is pressed flat by snow and comes back green in spring, without leaving
the player short of grass ("we just despawn it" was rejected: it regrows instead). Branch `claude/biomes-1.0`.

## What vanilla does (1.22.7, read in `C:\VS-ref\decompiled\1.22.7`)

- **Lightning fires** (`ModSystemFireFromLightning`) need the world option `lightningFires`, off by default. When on, a
  strike burns combustible blocks next to air where `GetEnvironmentWetness(pos, 10 days) < 0.01`. That wetness sums
  `GetClimateAt(ForSuppliedDateValues).Rainfall`, which Earth's weather drives, so in Earth worlds dryness is Earth's.
  The strikes came from vanilla's region weather, not from Earth's storms; now they are Earth's (below).
- **Lightning and thunder** (`WeatherSimulationRegion.TickEvery25ms`, `WeatherSimulationLightning`): each 512-block
  region's weather event (`heavythunder`, `lightthunder`) has a lightning rate; every 25 ms the server may strike within
  200 blocks of the region's centre when it is at least 12 °C and rain clouds cover it. The client plays near and distant
  thunder from the blended event rates times the rain cloud cover overhead. `SpawnLightningFlash` makes the bolt, deals
  6 damage within 8 blocks, calls lightning rods and then the fire rule. Rain puts burning blocks out
  (`BEBehaviorBurning`, precipitation over 0.05 under open sky); spread follows `allowFireSpread`.
- **Burnt grass** turns to air and the soil stays; vanilla never grows tall grass back on it (`BlockSoil` grows tufts only
  while its grass cover grows).
- **Grass in winter**: tall grass swaps to its snow-covered variant and back to the same height when the snow melts
  (`Block.PerformSnowLevelUpdate` with `GetSnowCoveredVariant`). Its colour follows one calendar for the whole world
  (`seasonalGrass`), not the place's own seasons.
- **Earth 5b was not drawing**: in 1.22.7 the chunk mesh builds each block's colour data in
  `ChunkTesselator.TesselateBlock`, not through `ClientWorldMap.getColorMapData`, so the 5b postfix reached only blocks
  drawn through the world accessor. Fixed with a transpiler on `TesselateBlock`.

## Built

### Grass through the year (client) and winter (server; new worlds, `GrassWinterRules` 1, proto 123)

- Colour: each place gets one of Earth's season maps (`earthgrass-*.png`, `Core.GrassPhenology`): dead and brown while
  it is cold, bright fresh green in the weeks after it warms, summer green, then straw. Singapore and London stay
  evergreen, Seville dries in summer, Astana is steppe, Riga and Yakutsk get the long brown winter.
- Snow: a tuft coming out of the snow is at most "short" (`Core.GrassWinter.AfterSnow`). It grows back one stage at a
  time on vanilla's random soil ticks, once the month's mean at the ground reaches 5 °C, about three stages a month at
  10 °C, up to the height vanilla's block layers give the place. Grass never disappears, so it can still be cut.
- Bare grassy soil sprouts a short tuft again where vanilla's world generation put grass (same rule and density as
  `GenBlockLayers.PlaceTallGrass`, hashed per column): burnt, eaten or cut grass comes back. Dry land stays with ranges
  step 5c (after rain).

### Fire-kept grassland (worldgen; new worlds, `FireRules` 1, proto 124)

Real prairies, aspen parkland, the Pampas and the forest-steppe are open because of fire; climate alone would forest
them (the zones gave Des Moines and Winnipeg 0.9 forest). Calibrated against RESOLVE Ecoregions 2017 (biomes 7/8 vs
4/5/6) on cells where the zones give forest ≥ 0.5 and the coldest month is under 13 °C:

- Climate and WGLC lightning together reached 0.82 balanced accuracy, but opened Central Europe, the Balkans and the
  Sichuan Basin. The share of grassland soils (Chernozems, Phaeozems, Kastanozems: soils built under grass by fire and
  drought) separates them.
- Final rule, without lightning (WGLC's licence is stated two ways, and its accuracy gain was 0.016):
  `logit = −4.80 + 3.656·M + 0.278·Tsum − 0.354·ln(Psum + 5) − 1.151·ln(AI)`, balanced accuracy 0.847. M is the
  grassland-soil share, Tsum and Psum the mean temperature and monthly rain of the three warmest months, AI the aridity
  index.
- Openness = smoothstep((p − 0.5)/0.35) × (0.3 + 0.7·smoothstep(M/0.4)) × clamp((15 − coldest month)/4); forest ×
  (1 − 0.8·openness), shrubs × (1 − 0.5·openness).
- Results: Des Moines 0.90 → 0.23, Winnipeg 0.89 → 0.19, Pergamino 0.90 → 0.18, Saskatoon 0.62 → 0.12, Kharkiv,
  Voronezh and Omsk open; Columbus, Paris, Riga, Moscow, Tokyo and Thunder Bay keep 0.90.

### Open steppe without trees (8 November; new worlds, `ZoneRules` 2 and `FireRules` 2)

The author in a new 1:10 world north of Crimea (45.80° N 33.81° E) in August: far too many trees, where they had seen
none in all their sightline, and the Pontic steppe of 1318 had none outside its valleys and ravines. The rules had
the steppe open (zones: forest-steppe, forest 0.35; fire: chance 0.99, fully open, forest 0.06), but two things
still planted trees:

- **Vanilla's floor.** `GenVegetationAndPatches.genTrees` (1.22.7) makes about 70 tries a chunk and keeps each with
  `max(0.0025, f²)`, f the forest map over 255. On any fertile ground that is about 0.13 trees a chunk whatever the
  map says: a tree every 7 to 8 chunks, 1 to 2 a square kilometre at 1:10, dozens in sight. The Kazakh steppe (zone
  forest 0.02) got 0.131 a chunk, 0.008 without the floor. Vanilla's shrub pass has no floor (`f²` alone).
- **A fifth of the forest kept.** Fire rules 1 keep a fifth of the zones' forest on open land for groves: 0.06 at the
  spot, still about 0.10 trees a chunk.

Rules (new worlds; existing worlds keep their saved versions):

- Zone rules 2: Earth's species pick (`EarthTrees`, trees only) drops a try that vanilla let through under its floor
  with the chance `1 − f²/0.0025` (`ClimateZones.TreeKeep`, f at the middle of vanilla's truncated value), on a
  position hash so the remaining trees keep their shapes. A try is then kept with `f²` alone. From a forest map of
  13 up nothing changes.
- Fire rules 2: the share of the forest fully open land loses rises with the fire chance:
  `cut = 0.8 + 0.18·smoothstep((p − 0.9)/0.08)` (`FireRegime.ForestCutFor`). Up to a chance of 0.9 open land keeps a
  fifth as before (the aspen parkland, the forest-steppe at Kursk, the prairie's oak edge at Des Moines: groves); from
  0.98 a fiftieth (the core steppe, the Pampas, the tallgrass prairie). Trees there stand in the river belts the
  forest potential adds afterwards. Shrubs are unchanged.
- `/earth where` has a "Trees" line for the floor, and its fire line gives the rules version
  and the share of the forest open land keeps.

Trees a chunk (ZoneTests, vanilla's tries taken as 70 × 0.75, its noise even over 0..255), rules 1 → 2:

| Place | Rules 1 | Rules 2 |
|---|---|---|
| North Crimea (the author's spot) | 0.168 (one in 6 chunks) | 0.0015 (one in 678) |
| Kherson | 0.172 | 0.0073 (one in 137) |
| Odesa | 0.187 | 0.0016 (one in 613) |
| Zaporizhzhia | 0.199 | 0.0026 (one in 386) |
| Rostov | 0.942 | 0.0096 (one in 104) |
| Kazakh steppe | 0.131 | 0.0002 |
| Lincoln, Nebraska | 0.963 | 0.0099 (one in 101) |
| Pergamino (Pampas) | 0.963 | 0.0168 (one in 59) |
| Kursk, Winnipeg, Des Moines | 0.98, 1.03, 1.59 | unchanged |
| Kassel, Riga, Columbus (forest) | unchanged | unchanged |

The Pampas and the prairie keep a fiftieth of a larger zone forest (0.9 against the steppe's 0.35), so they keep more
trees than the steppe: at 1:10 one in about 6 km². Kharkiv (chance 0.95) goes from 0.17 forest to 0.05, Ames 0.05,
Fargo 0.06, Saskatoon 0.07, Omsk 0.16.

Why Kherson and Rostov were forested at all: Earth's aridity index reads high over the Pontic steppe (Kherson 0.61,
Odesa 0.66, Zaporizhzhia 0.67: forest-steppe; Dnipro 0.84, Rostov 0.85: temperate forest; all real steppe).
`tools/bake_rain.py` takes Holdridge's evapotranspiration, `max(58.93·biotemperature, 450 + 25·biotemperature)`,
which runs low where hot summers follow cold winters (the winter months count as 0). Fire rules 2 make this matter
much less for trees; the index itself is fixed for new worlds in the next section.

### The aridity index on Penman-Monteith (8 November; new worlds, `AridityRules` 1, proto 128)

The author, asked whether to fix the index here: "Yes, fix it here". Holdridge's evapotranspiration follows the
temperature alone, so it runs low wherever sun, wind and dry air drive the evaporation, and its index reads those lands
too wet; its 450 mm floor makes the cold lands read too dry. CHELSA V2.1 BIOCLIM+ has the Penman-Monteith
evapotranspiration on the same 30" grid as the rainfall (`pet_penman_mean`, 328 MB, downloaded with the author's
permission). On it (Holdridge index → Penman-Monteith index): Kherson 0.64 → 0.52, north Crimea 0.56 → 0.41, Lincoln
(Nebraska) 1.03 → 0.72, Denver 0.62 → 0.42, Lhasa 0.83 → 0.46, Nairobi 0.77 → 0.56, Paris 0.90 → 0.90, Moscow
1.10 → 1.12, Yakutsk 0.47 → 0.53, Manaus 1.55 → 1.91 (means of 4 × 4 CHELSA pixels).

- `earthdata/aridity.bin` (6.5 MB, `tools/bake_aridity.py`, 90 s): per 2' cell the factor Holdridge / Penman-Monteith
  evapotranspiration, the geometric mean of its 30" pixels, in log2 steps of 1/64; land only (24 % of cells).
- New worlds (`AridityRules` 1) multiply the index decoded from the rainfall by it (`EarthData.ClimateSample.Aridity`,
  `PenmanFactor`). The ratio, not an index, so it applies to whichever rainfall a rule reads (the 2' grid's, the 0.25°
  grid's, a river's green belt). The rainfall itself is unchanged: vanilla's climate map, the tree, plant and
  placement catalogues, arid rivers, the snow line and the client's grass colours read it as before. Older worlds read
  the Holdridge index and the limits they had.
- On the new index: the zones, the fire rule, the soil rule's 0.2, tall grass, the water table, the trees' soil
  moisture and `/earth where` (which names the evapotranspiration and the factor).
- Thresholds drawn on the real index keep their values, now on the index they were drawn on: the soil rule's 0.2
  (UNEP's arid boundary), the water table's 0.65 and 0.05, the tall-grass knots (Berdugo et al. 2020). The zone
  limits fitted at named places get a `"penman"` block in `zones.json`: steppe to 0.55; forest-steppe 0.55-0.65;
  temperate, continental and warm-humid broadleaf from 0.65 (UNEP's humid boundary; before 0.75); humid spruce-fir
  from 1.5, boreal and hemiboreal to 1.5 (before 1.8; mountains read about 0.75 of the Holdridge index);
  Mediterranean to 1.1, sub-Mediterranean 0.55-1.1, summer-dry forest from 1.1 (before 1.3 and 0.65-1.3; the
  Mediterranean's factor is about 0.82).
- Fire: the logistic, fitted on the Holdridge index, scores a balanced accuracy of 0.853 on the new index and zones
  (RESOLVE, the same selection of cells; 0.847 when it was fitted). A refit reaches 0.856 by moving weight from the
  grassland soils to the index (aridity −1.151 → −2.367, soils 3.656 → 2.871): not worth a new rule, `FireRules`
  stays 2.

| Place | Index (Holdridge → Penman) | Dominant zone | Forest after fire |
|---|---|---|---|
| North Crimea | 0.56 → 0.41 | forest-steppe → steppe | 0.01 → 0.00 |
| Kherson | 0.63 → 0.51 | forest-steppe → steppe | 0.01 → 0.00 |
| Odesa steppe | 0.67 → 0.50 | forest-steppe → steppe | 0.01 → 0.00 |
| Zaporizhzhia | 0.70 → 0.59 | forest-steppe | 0.01 |
| Dnipro, Rostov | 0.82, 0.85 → 0.72, 0.70 | temperate (fire-opened) | 0.02 |
| Kharkiv, Kyiv | 0.78, 0.94 → 0.72, 0.86 | unchanged | 0.05, 0.16 → 0.04, 0.14 |
| Vienna, Budapest | 0.88, 0.84 → 0.79, 0.73 | temperate | 0.63, 0.84 → 0.54, 0.79 |
| Hungarian puszta | 0.79 → 0.59 | temperate → forest-steppe | 0.73 → 0.30 |
| Astana, Denver | 0.52, 0.62 → 0.46, 0.41 | forest-steppe → steppe | 0.01 → 0.00 |
| Lincoln | 1.03 → 0.71 | temperate (fire-opened) | 0.02 |
| Lhasa (3650 m) | 0.83 → 0.46 | hemiboreal → steppe | 0.87 → 0.26 |
| East of Arkhangelsk (63° N 45° E) | 1.21 → 1.55 | boreal → humid spruce-fir | 0.87 → 0.89 |
| Paris, Berlin, Moscow, Columbus | about the same | unchanged | unchanged |
| Burgos | 0.79 → 0.57 | temperate → forest-steppe | 0.79 → 0.25 |
| Mexico City (2240 m) | 0.77 → 0.56 | warm-humid → subtropical scrub | 0.90 → 0.13 |

Over the land from 56° S to 84° N (0.25° cells, area-weighted), Holdridge → Penman-Monteith, UNEP's World Atlas of
Desertification (Middleton and Thomas 1997) in brackets: hyper-arid 7.3 → 9.1 % (7.5 %), arid 8.3 → 11.4 % (12.1 %),
semi-arid 14.8 → 16.6 % (17.7 %), dry sub-humid 7.9 → 6.6 % (9.9 %). Land under 0.2, without topsoil in new worlds:
15.6 → 20.5 % (19.6 %); the 4.9 % that becomes arid lies mostly on the Australian interior's margins (1.2 % of all
land), in Central Asia, the Sahel, the Great Basin and Patagonia; almost none (under 0.05 %) gains topsoil. Dominant zones: steppe
3.2 → 7.0 %, forest-steppe 4.3 → 1.1 %, semi-desert 6.7 → 9.4 %, desert 7.3 → 9.1 %, humid spruce-fir 1.8 → 3.4 %
(northern European Russia, Quebec-Labrador), boreal 8.0 → 6.4 %. Tall grass kept: 74.5 → 69.3 % of vanilla's on
average.

Weaker than before: one aridity line cannot tell winter-rain woodland from summer-rain steppe at the same index.
Burgos (the northern Meseta's oak and juniper), the Valley of Mexico and Kermanshah (Zagros oak, 0.58 → 0.39: steppe)
lose forest the Holdridge index had given them by its error.

### Soil sites (`assets/earth/config/soil-site.bin`, 1.6 MB)

From SoilGrids' 2′ soil groups and pH at 15–30 cm (the ground pack's tiles; Earth ships the grid so it works without
the optional pack). At 0.1°, over the 0.2° square around each cell: shares of grassland soils, waterlogged soils
(Gleysols, Histosols, Stagnosols, Planosols, Fluvisols), nutrient-poor soils (Podzols, Arenosols, Histosols,
Ferralsols, Acrisols, Plinthosols) and the mean pH. Albeluvisols (Retisols), Alisols and Umbrisols are not counted as
poor: as poor they took the Baltic's loamy forests and gave Latvia 45 % pine. SoilGrids' class is the most common per
2′ cell, so it misses small mires (Polesia reads as Albeluvisols) and maps the Landes' sands as Cambisols.

### Trees and shrubs by soil (tree rules, all worlds' new chunks)

- EIVE 1.0 (Dengler et al. 2023): niche position and width for moisture, nutrients and soil reaction, for 64 of the
  170 tree rules (European flora). A generic tree combines its species, weighted by how many indicator systems EIVE drew
  on, so vanilla's beech reads as European beech, not Japanese.
- A species fits each axis by a Gaussian; sigma is the standard deviation of an even spread across EIVE's niche width
  (nw3, a range: mean amplitude of the source systems plus twice the spread of their positions), width/√12. Over a
  place's mixed ground the fit is the share-weighted mean, and the axes multiply. The site: moisture from the aridity
  index (5 at P = PET, +1.5 near rivers and lakes; waterlogged share at 7.5), nutrients 5.5–7.5 (2.5 on the poor
  share), reaction from pH (Ellenberg classes).
- The fit redistributes weight among the rated species and keeps their total, so soil sorts the mix and does not thin
  the forest; non-European species keep their weight.
- Only in Europe (the WGSRPD regions of Europe, EIVE's own range). EIVE rates trees brought to Europe from elsewhere by
  the ground they were planted on: bald cypress scored 0.00 on the Everglades' marl, and with Wildcraft Trees' rated
  species in Florida it would have lost its own swamps. Outside Europe a generic rule also stands for other species
  (vanilla's fir in Iowa is the balsam fir). There the soil changes no species' share.
- `/earth where` shows the soil the tree picker sees and the native trees it suits best and worst.
- Central Germany on acid sand: pine 13 → 25 %, silver birch 20 → 30 %; on fen and gley black alder takes 42 %; on loess
  Norway maple, beech and oak lead. Latvia on its own soils and wetlands: pine 39 %, spruce 24 %, birch 16 %, other 21 %.

### Beech's edge and other 1318 ranges

Caudullo et al. 2017's chorological maps (CC BY 4.0) narrow 30 tree rules inside their WCVP regions (beech in the
Baltic States only at Kaliningrad's border). Two corrections for 1318: yew keeps its WCVP regions (the longbow-stave
trade of the 14th–16th centuries cut it out of much of its range; today's scattered stands are what is left), and
Scots pine gets the Highlands back (a box over the Caledonian pinewood; the mapped native stands are the remnants).

### Wetlands (GLWD v2; `assets/earth/config/wetlands.bin`, 2.3 MB, 0.1°)

Each 15″ pixel's wetland share counts for its dominant class (the per-class share grids were not downloaded). Seven
groups: bog, wooded peat, flooded swamp forest, flooded marsh and delta, mangrove, saltmarsh, and damp ground. Damp
ground is GLWD's seasonally saturated and ephemeral classes (14, 15, 18–21): GLWD calls them its least certain, and
their forested or open state is mostly today's farming (much of Picardy, the Beauce and the Po plain), so in a world
of 1318 they are wet ground, not open marsh.

- New worlds (`WetlandRules` 1, proto 125): open bog, marsh and saltmarsh take their share of the zones' forest. The
  Everglades are 67 % open, the Danube delta 78 %, the Flow Country 37 %, the Hudson Bay Lowlands 16 %; Sumatra's peat
  swamp, the Sundarbans and the Pantanal keep their trees.
- Trees: the wooded peat, swamp forest and half the damp ground, over the wooded part of a place, join the soil map's
  waterlogged share; wooded peat is also nutrient-poor.

### Lightning from Earth's thunderstorms (weather rules v3; all Earth worlds with Earth's weather)

- **How often** (`Core.Thunder`, fitted by `tools/fit_thunder.py`): the WGLC monthly stroke density at 0.5° from the three
  things Earth's weather knows, the month's sea-level temperature, precipitation and land share:
  D = exp(c0 + cl·land^0.18)·σ((T − 18.5 °C)/3.8)·(P/(P + 7.7))^1.40. R² 0.63 on log density, yearly log correlation
  0.87; the sea a sixth as stormy as land, coasts nearly as stormy as inland. The share of days with thunder,
  1 − exp(−(D/0.010)^0.88), fits stations' yearly thunderstorm days (Singapore 170, Tampa 85, Darwin 85, Oklahoma City
  50, Denver 40, Vienna 26, Moscow 23, Paris 19, London 13, Cairo 3, Reykjavik 1.5). Kampala's 240 is left out: WWLLN
  sees few African strokes (the model gives 124).
- **Where and when** (`WeatherModel.ThunderAt`, `WeatherSample.Thunder`): a thunderstorm is the top of the shower cells,
  so it moves with the steering wind and rains where the showers rain. Its level is set by Rice's formula from the spells
  a day the month's thunder days need and how fast the field changes at the place (the steering wind across its 600-block
  cells, its own change, the weather's pace; measured in `WeatherTests thunderrate`), so a place gets its thunder days on
  every world size and pace. Afternoons over land have 1 + 0.85·cos(h − 16 h) times the mean rate.
- **Dry lightning**: under a thunderstorm it rains hard (cloudness 0.5 to 1.5), unless the month's air is dry: rain
  reaches the ground in proportion to the month's precipitation over a rough evaporation demand (0.6 + 0.22·T mm/day,
  none at 0.05, all at 0.4; not fitted). Under the rest of the storm only its dark cloud is overhead. Denver's July
  storms are a fifth dry, Yakutsk's two fifths, Vienna's, London's and Singapore's none (rules v3; with v4's wet spells
  Denver 23 %, Yakutsk 11 %, below).
- **The rain fields** take a fresh lattice phase each restart period (`WeatherModel.Advected`): value noise spreads less
  between its lattice nodes than at them, so where the wind barely moved a field a place kept one spread, and the rare
  top that thunder is made of came four times more often in some places than others. Rain chance still matches each
  month's climate (`WeatherTests`).
- **In game** (`EarthThunder`): vanilla's weather events strike no lightning (`LightningRate` 0 before each region tick).
  Every quarter second the server draws 16 points within 320 blocks of each player and strikes through vanilla's
  `SpawnLightningFlash` with a chance of 1e-6 per block² and second times the thunder there (a place near several
  players counts once): about one bolt every 3 seconds in view under a storm's core. The client's near thunder (and the
  sky's flash) follows the storm overhead and within 140 blocks; distant claps are played toward the storms out to
  1200 blocks, under a clear sky too.
- **Fire**: vanilla's rule as it is. New worlds of Earth's five play styles set `lightningFires` "true" (the world option
  can be turned off). `/earthweather` says whether the last ten days were dry enough; `WeatherTests firedanger` gives the
  share of days that are. With v3's rain: Los Angeles about all summer, Darwin 81 to 89 % in its dry season, Nevada 70 %
  in August, Madrid half of August and September, Kansas and Vienna in summer next to none. With v4's wet and dry spells
  (below): Madrid 77 and 83 % in July and August, Los Angeles 90 to 97 % from June to September, Darwin 90 to 97 % from
  July to September, Elko 77 and 84 % in July and August, Kansas 11 to 15 % and Vienna 12 to 27 % in summer.
- `/earthweather` has a thunder line (the month's thunder days, the storm overhead, strikes so far, the fire test);
  `/earthweather thunder` finds the strongest storm within 3000 blocks and the next thunder here.
- Weather rules are v3: a client with v2 keeps vanilla weather and says so (its rain would differ from the server's).

### Wet and dry spells (weather rules v4; the author's "Yes, do it here" to making dry months rain less often)

- **The problem**: Earth's rain matched each month's share of rainy hours, but spread them evenly: a dry month had a
  short shower every few days instead of its two or three rainy days, and even Vienna's summer almost never had ten days
  without 5 mm of rain (19 % of real July days do). So vanilla's ten-day test for lightning fires rarely passed. Measured
  against 56 GHCN-Daily stations (`tools/ghcnd_dryspells.py`, 1971-2020, month by month; `WeatherTests dryspells` scales
  the model's daily rain to each station's monthly mean so that only the spread over the days counts): the model rained
  on 8.3 % of the days of dry months (real 5.9 %), 27 % of middling ones (21 %) and 58 % of wet ones (35 %); ten days
  under 5 mm came on 74, 25 and 3 % of days (real 82, 41 and 17 %).
- **The fix** (`WeatherModel.Rain`): a third random field under the fronts and showers, the wet and dry spells: 2000 km
  across, changing over about five days, drifting at half the steering wind. Its weight is 0.5 in cool months (frontal
  rain) up to 0.9 in hot months (showers), by the month's convective share. The mix stays a standard normal field, so
  each month still rains its share of the hours (`WeatherTests` rain chance, unchanged). Cloud cover and pressure follow
  the fronts and spells together: a dry spell is sunnier and higher.
- **Thunderstorms** keep to the wet spells as far as the month's air is humid (weight 0.6 × `Thunder.RainReach`): in
  humid climates storms come in runs with the rain; in dry air they come on their own, the dry lightning that starts
  fires. Storms in runs share days, so they get 1 + tie² times the spells, which keeps the thunder days of the fit
  (Singapore 154 against 153 fitted, Tampa 95 against 90, Oklahoma City 61 against 63, Vienna 27 against 27, Moscow 16
  against 21).
- **Result** at the stations: dry months rain on 5.4 % of days (real 5.9 %), with ten days under 5 mm on 78 % (82 %);
  middling months 20 % (21 %) and 39 % (41 %); wet months 38 % (35 %) and 13 % (17 %); cold months 19 % (25 %) and 38 %
  (38 %); warm months 17 % (16 %) and 54 % (57 %). Madrid's August passes vanilla's fire test on 82 % of days, as many as
  real Augusts have under 5 mm in ten days.
- Weather rules are v4: a client with v3 keeps vanilla weather.
- **Seen in a game** (8 November, the author's "Yes, check it"; AgentBridge bench, new world `spells1` at 1:100 with
  Earth 1.0.3-tall.21 from `b87a1eb`): server and client both log rules v4, no errors from Earth. Walking day by day
  (the forecast every three hours, vanilla's ten-day fire test at midnight): in Madrid every day of the game's August and
  six of September's nine passed the fire test; the first rain came after 13 dry days, then a run of three wet days in
  October; 14 of 31 days from August to November passed. In Vienna a wet spell of afternoon showers on days 3 to 6 of
  July, then 13 dry days, the last four passing the fire test, then rain again: 4 of 27 days from July to September
  passed (`WeatherTests firedanger`: 12 to 27 % in summer). Where the server said heavy rain the client drew it. A game
  month is 9 days (the play styles' `daysPerMonth`) while a spell lasts about five days at pace 1, so one spell can fill
  most of a game month: a game month is more often wet or dry as a whole than a real month.

### Rain hours (weather rules v5; the author's "Yes, fix them here" to the wet days v4 left wrong)

- **The problem**: with v4, cold months rained on too few days (Moscow's January 20 % against 40 %, London's 30 %
  against 40 %) and Plains summers on too many (Dodge City's July 32 % against 21 %). The share of the time it rains
  came from the month's precipitation over a rain rate guessed from its temperature, and airport reports show the guess
  was far off. The present weather of routine METAR reports at the airports next to the GHCN-Daily stations (the Iowa
  Environmental Mesonet's archive, 2017-2023, `tools/iem_metar_precip.py`) has precipitation falling 23 % of the time in
  months below -10 °C (sea-level temperature), 14 % at 0-5 °C, 6 % at 10-15 °C and about 2 % above 20 °C (geometric
  means over the station-months); v4 rained 1.5, 4.9, 6.4 and 3-5 % of the time. Light snow falls for hours; v4 made it
  short and heavy, so it missed days.
- **The fix** (`WeatherModel.RainRate`, `RainChanceFor`, `SpellWeight`): the rate while it rains or snows, fitted
  (least squares of its logarithm) to the airports below 1000 m: 9 % harder per degree up to 10 °C and 5 % above
  (between -20 and 30 °C), harder in wet months (precipitation to the power 0.41), lighter under a cloudy climate's grey
  skies (the reanalysis' cloud cover). The rain chance is the month's precipitation over 24 times that rate. With the
  hours fixed, the spells' weight was refitted to the wet days, wet spells and ten-day dry spells, now by the month's
  temperature: 0.9 in frost, 0.57 in mild months, 0.74 in hot months (`WeatherTests rainfreq <table> <out>` scans each
  station-month at ten rain chances).
- **Result**: Earth rains as much of the time as the airports report, band by band (model / METAR): 0-5 °C 11.0 / 14.1 %,
  5-10 °C 8.6 / 9.2 %, 10-15 °C 5.8 / 6.2 %, 15-20 °C 3.8 / 3.9 %, 20-25 °C 1.7 / 1.7 %, 25-30 °C 2.6 / 2.1 %, below
  -10 °C 29.6 / 22.9 %. Wet days: Moscow's January 36 % (real 40 %), London's 38 % (40 %); cold months 24.3 % (real
  25.2 %; v4 19 %), mild 19.7 % (21.1 %), warm 17.0 % (16.4 %), dry months 6.1 % (5.9 %). Ten days under 5 mm: cold
  months 35 % (38 %), mild 39 % (40 %), warm 53 % (57 %), dry months 78 % (82 %).
- **Not fixed**: the Plains' summer (Kansas City's July 38 % against 22 %, Oklahoma City's 31 % against 16 %, Dodge
  City's 30 % against 21 %), although its hours match the airports: real storms there come as long night-time systems
  on few days. Tried and dropped: fronts alone without more showers in summer (Kansas City 33 %, but warm months
  everywhere 14.8 % against 16.4 %) and no afternoon peak (no change). Perth's and Adelaide's winters rain on too few
  days (Perth's July 27 % against 46 %) and Tokyo's August on too many (45 % against 30 %): all at similar
  temperatures, so no rule by temperature fits them.
- **Ground moisture** (`Core.GroundMoisture`, dry land's tufts after rain, step 5c): it was the decayed share of the
  recent hours with rain, so once the heat's rain came in short heavy showers Kano's monsoon read 0.093 on average, under
  the 0.10 from which tufts grow (0.137 with v4). It now reads the rain that fell, in mm (`WeatherModel.RainMmPerHour`:
  the month's rate while it rains, scaled by the moment's intensity), decayed as before, over 40 mm per day: Kano's
  August 0.155 and February 0.001, Seville's July 0.005 and October 0.037. The grow and wither levels are unchanged
  (0.10, 0.03); a 20 mm storm reads about 0.15 at once and is under 0.03 after five dry days.
- Weather rules are v5: a client with v4 keeps vanilla weather.

## Data

Downloaded for this work (each in its own folder under `Earth/rawdata`):

| Data | Licence | Use |
|---|---|---|
| GHCN-Daily station records (NOAA NCEI), 56 stations | NOAA; no licence terms in its readme (non-US records may carry WMO Resolution 40 limits on resale) | monthly wet-day and dry-spell numbers for fitting and checking the wet and dry spells (numbers only, not shipped) |
| METAR present weather, Iowa Environmental Mesonet archive (Iowa State University), routine reports at the airports next to those stations, 2017-2023 | licence terms not looked up | the monthly share of reports with precipitation, for fitting the rain rate (numbers only, not shipped) |
| WGLC lightning climatology (Kaplan and Lau) | Zenodo: CC BY 4.0 in the metadata, CC BY-SA 4.0 in the text | the fire rule's calibration check; the constants of `Core.Thunder` (fitted numbers only, not shipped) |
| RESOLVE Ecoregions 2017 | CC BY 4.0 | calibration oracle only, not shipped |
| CHELSA V2.1 BIOCLIM+ `pet_penman_mean` 1981-2010 (Karger et al.) | CC BY 4.0 | `earthdata/aridity.bin` (its ratio to Holdridge's evapotranspiration, 2') |
| Caudullo et al. 2017 chorological maps | CC BY 4.0 | range masks in `regionaltrees.json` |
| EIVE 1.0, supplement 8 | CC BY 4.0 | soil indicators in `regionaltrees.json` |
| GLWD v2 combined classes | CC BY 4.0 (aggregate only; the authors ask that the grids are not redistributed whole) | `wetlands.bin` |

SoilGrids 2.0 (CC BY 4.0) was already in the ground pack. Not downloaded: GLWD's 33 per-class share grids (minor classes
inside mixed pixels), PEATMAP, burned-area products. Attributions: `geology-attribution.txt`,
`wetland-attribution.txt`, `TREE-DATA-ATTRIBUTION.txt`.

## Tests

ZoneTests (fire, soil grid rebake; from 8 November also fire rules 1 as before, the floor share, the rules 2 cut and
trees a chunk at the steppe, prairie, grove and forest places above; then read as new worlds read it, on the
Penman-Monteith index with its limits: the factor at Paris, Lincoln, Kherson and Yakutsk, older worlds' index and
limits unchanged, Kherson, north Crimea, Odesa, Dobruja, Volgograd, Lhasa and the western Plains steppe, Vienna and
Belgrade forest, UNEP's arid share within 3 points: 179 checks pass), SoilTests (grass phenology,
winter grass, sprouting), RegionalTreesTests (soil
grid at known places, soil mixes, Latvia on its soils, no change outside Europe), WetlandTests (format, known
wetlands, open share). The whole offline suite passes after release/1.0 (`6fed534`) was merged in. RegionalPlantsTests'
open-sea timing failed once while other games ran (12 µs against 5 µs) and passed rerun (4.6 µs). EarthWorldOptionsTests
passes up to its data pack check, which opens a published release zip this worktree does not build (the packaging step
stops on the release/1.0 version rule: Earth 1.0.3-tall.16 against data packs 1.0.2).

Thunder (`WeatherTests thunder`, also in the full run): ten game years at 15 stations every quarter hour. Thunder days
within a factor of 2 of the stations' and within 40 % of the fitted climate (Singapore 144 against 153 fitted, Tampa 83
against 90, Oklahoma City 60 against 63, Vienna 23 against 27, Moscow 18 against 21); Moscow none in winter; Yakutsk
none in January; the open Pacific a twentieth of the Amazon; afternoon 7.5 times the night at Oklahoma City; dry
lightning in Denver and Yakutsk, none in Vienna, Moscow, London, Singapore or the Amazon; nothing with the rain off. The
full WeatherTests run passes with the new rain fields (rain chance within its tolerance everywhere, a rain lookup
1.05 µs).

Wet and dry spells (weather rules v4): `WeatherTests drydays` (also in the full run) checks 21 station-months (numbers
from GHCN-Daily written into the test): wet days in dry months 2.5 % against 2.3 %, ten dry days 92 % against 92 %,
middling months 24 % against 27 % and 27 % against 29 % wet days, wet months 56 % against 59 %, Madrid's August 82 % for
vanilla's fire test, Vienna's July 23 % ten dry days (real 19 %). The thunder test now runs twenty game years (the
spells make ten noisy). Two older checks changed: London's January rain now ends within a quarter hour in 11 % of its
spells (at a wet spell's edge the showers come and go a while; the limit went from a tenth to an eighth), and the ground
moisture check averages twenty years instead of three (the spells made three years too few). The full run passes; a
rain lookup takes 1.34 µs (was 1.05).

Rain hours (weather rules v5): the full run's rain-chance check also compares eight places' January and July with
their airports (METAR numbers written into the test; model / airport, % of the time): London 20 / 13 and 6 / 7, Moscow
31 / 48 and 8 / 11, Riga 28 / 30 and 7 / 8, Rome's January 13 / 8, Lisbon's 13 / 7, Singapore 15 / 13 and 10 / 8,
Mumbai's July 30 / 34, Darwin's January 17 / 19; on average 1.06 times the airports, furthest Lisbon's January (1.82
times). `drydays` adds Moscow's January (rain or snow on 36 % of days, real 40 %) and London's (38 %, real 40 %); its
other checks hold (dry months 3.1 % against 2.3 % wet days, 91 % against 92 % ten dry days; middling 28 % against 29 %
and 22 % against 27 %; wet months 58 % against 59 %; Madrid's August 81 % for vanilla's fire test; Vienna's July 28 %
ten dry days, real 19 %). Ground moisture reads the rain's millimetres now; its check's Seville part became "October
above 0.02 and four times July" (0.037 against 0.005), Kano's is unchanged (August 0.155, February 0.001). New quick
modes: `WeatherTests moisture` and `rainfreq <table> <out>`. Thunder days unchanged (Singapore 154, Tampa 95, Oklahoma
City 61, Vienna 27, Moscow 16). The full run passes; a rain lookup takes 1.31 µs.

## In game (AgentBridge bench, 7 November)

New 1:100 worlds (`preset-earth`, 384 high) with the merged build, then with Wildcraft Trees and Herbarium:

- Start-up notices for zone, fire, wetland, grass winter and ground moisture rules. Missed then: the client log's
  "Failed to start system EarthMap.EarthGrassSeason" (an ambiguous match: 1.22.7 has two `TesselateBlock` overloads).
  Fixed in the thunder run below.
- Des Moines: "kept 100% open, forest 0.90 -> 0.18"; open grassland with scattered oaks.
- Lüneburg Heath (65 % nutrient-poor, pH 5.4): Scots pine and spruce best suited; a conifer-rich forest. The Hildesheim
  loess (pH 6.4): Norway maple, willow, ash and lime best; dense broadleaf forest.
- The Everglades: 67 % open wetland; scattered trees over open ground; "outside Europe ... soil does not sort the trees".
- Riga's grass in March, May, July and October: dead brown, fresh yellow-green, green, straw-brown. With Earth's grass
  season not running, that was vanilla's own seasonal colour; Earth's is checked below.
- Not seen in game: the snow flattening and spring regrowth (weeks of game time; SoilTests cover it), and alder taking
  the lead (the Spreewald's 0.1° cell is only 16 % waterlogged; maple, beech and lime suited it best).

Thunder run (world `thunder1`, 1:100, 384 high), 7 November evening:

- "Earth thunder: lightning from Earth's thunderstorms (thunder rules v1); lightning fires on"; weather rules v3 on both
  sides; the grass season starts ("grass colour follows each place's wet and dry seasons").
- Florida and Colorado in July, New Mexico in August: `/earthweather thunder` found storms 750 to 1500 blocks away;
  under them 2 to 5 bolts in 10 to 20 seconds, none once they had passed (12 in New Mexico over two storms). The storms
  rained hard. The client played vanilla's near thunder and Earth's distant claps (25 m toward the storm).
- Fire: the Central Valley in August ("No rain here for ten days") burnt where vanilla's test bolts hit dry grass and a
  pine; Madrid had had a shower in its ten days (wetness 0.02) and did not.
- Grass at Nagpur: straw in May before the monsoon, green in the August rain.

## Open

- Summer on the Great Plains rains on too many days (Kansas City's July 38 % against 22 %, Dodge City's 30 % against
  21 %), though it rains for as many hours as the airports report: the long night-time storm systems there come on few
  days. Perth's and Adelaide's winters rain on too few days (Perth's July 27 % against 46 %), Tokyo's August on too many
  (45 % against 30 %).
- The rain rate knows no ground height (the weather uses sea-level temperatures): airports above 1000 m were left out
  of its fit. Moscow's winter rains or snows 28-32 % of the time where its airport reports 38-48 %; Yakutsk's 29-37 %
  against 22-27 %.
- Weather rules v5 are not seen in a game yet; v4 was seen at two places over 58 game days (above); neither in long
  play or multiplayer.
- Thunder in the arid West is low (Phoenix 5 % of July's days; real about a fifth): WGLC's density there is low and the
  fit spreads it.
- Wetlands as firebreaks (the Mississippi bottomland forests inside the prairie) would need the fire rule refitted.
- Zone rules 2, fire rules 2 and aridity rules 1 (open steppe without trees, the Penman-Monteith index) were seen on the
  bench on 8 November in a new world (`a73eba2`, 1:100): north Crimea steppe 100 %, index 0.41, no trees; Kherson
  steppe, index 0.51, trees only along the Dnipro's floodplain channels; Kyiv parkland (index 0.86, fire 0.90 → 0.15
  forest); Polesia dense mixed forest (0.87, not opened). Not seen in game: the Plains, Kazakh and Lhasa steppe, and
  the weaker winter-rain places below.
- Winter-rain woodland at a steppe's index (the northern Meseta, the Zagros, the Valley of Mexico) needs a zone driver
  for when the rain falls; the zones read only the yearly index and summer drought.
- The client's grass colours and the summer-drought plane (`DrySummer`) still follow Holdridge's evapotranspiration.
- GLWD's per-class shares would restore minor classes inside mixed pixels.
- Soil for trees outside Europe would need indicator values from the species' home ranges (not looked for yet).
