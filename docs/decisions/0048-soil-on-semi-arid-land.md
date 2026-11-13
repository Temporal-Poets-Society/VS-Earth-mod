# 0048: Soil on semi-arid land, sand for deserts

Date: 3 November 2026. Status: **Implemented** (merged into `release/1.0`, not released);
offline checks and a native server pass, not played.

## Author's direction

"Fix the soil but look at soil problem as bigger problem and not just Greece" (3 November 2026, after the placement
slice of [0047](0047-placement-catalogue-and-audit.md) found Athens' fruit trees falling on sand).

## Problem

Vanilla's block layers take fertility from rain and temperature with a formula tuned to vanilla's own climate maps.
Earth stores real rainfall, a log curve of the aridity index. On that scale vanilla's fertility falls below its soil
threshold (0.25) across semi-arid and summer-dry land, and the top layer becomes sand (8 °C and warmer) or gravel. A
survey of all land between 56° S and 72° N ([research](../research/soil-2026-11-03/README.md)):

- Sand on 24.7% of the land, more than the 18.7% that is desert.
- Sand or gravel on 42% of Mediterranean land, about half of the hot steppes and three quarters of the cold steppes.
- The same in the dry savannas, the Great Plains' west, the Spanish Meseta, Anatolia, the Pampas' dry edge and South
  Australia.

Real soils there are Luvisols, Cambisols, Calcisols, Chernozems and Kastanozems. Sandy soils (Arenosols) cover about 7%
of the land, mostly the great dune fields. Sand blocks have fertility 10, so fruit trees (more than 20 needed), tall
grass and farming fail on land that grew olives, wheat and pasture.

## Decision

- **Rule.** In vanilla's block layers, every column whose real aridity index (precipitation / potential
  evapotranspiration) is **0.2 or more** sees a fertility of at least **0.3**. 0.2 is UNEP's boundary between arid and
  semi-arid land. Vanilla's own fertility still applies where it is higher.
- **Index.** The index is recovered from Earth's ambient rainfall and summer dryness by inverting `tools/bake_rain.py`.
- **Why 0.3.** It is vanilla's threshold for low subsoil (`soil-low-none`) under low topsoil.
- **Vanilla still picks the blocks.** It chooses the grade (`soil-low`, or `soil-medium` where its fertility is higher),
  the grass cover by rain (sparse at Athens' rain 0.25, very sparse in dry steppe), the subsoil, the soil depth and the
  tall grass.
- **Deserts are unchanged.** Arid land (index below 0.2) keeps vanilla's sand (warm) and gravel (cold).
- **Cold land is unchanged.** Below −10 °C, vanilla's coldest grassed soil, the layers stay vanilla's.
- **Other surfaces keep their systems:**
  - mapped dunes: GUM aeolian sediment, through Earth geology, after the layers;
  - coarse sediments: gravel lenses;
  - beaches and dry river beds;
  - glaciers;
  - mountain cover: alpine zones, scree and steep rock, from 0031/0032.
- **One global rule.** There is no regional list and nothing special for Greece.
- **Save compatibility (0002).** `EarthParams.SoilRules` (ProtoMember 92, `Core.SoilFertility.RulesVersion` = 1) is set
  for new Earth worlds. Older worlds keep vanilla fertility: new chunks with soil next to old chunks of sand would show
  straight seams, so the rule is not adopted automatically. Like the terrain upgrades, an opt-in upgrade flag for older
  worlds could come later.
- **Independent of the nature option.** The rule does not depend on the **Plants and animals** world option (0047). It
  is terrain, and a world set to vanilla placement by climate gets it too.

## Implementation

- `src/Core/SoilFertility.cs`: `Aridity(rain, drySummer)`, `HasSoil(...)` and `Apply(vanilla, ...)`.
- `src/EarthWorldGen.cs` (`SoilFertilityTranspiler`, `SoilFertilityAt`):
  - A transpiler on `GenBlockLayers.LoadBlockLayers` replaces each call to `Climate.GetFertilityFromUnscaledTemp`.
  - The replacement also receives the layer's temperature and the column's position.
  - Earth's climate is sampled once per column.
  - If the method changes in a game update, the transpiler leaves it alone, and the log warns that semi-arid land keeps
    vanilla's sand.
- `EarthParams.SoilRules`; new worlds in `EarthMapSystem`; startup log line "Earth soil rules v1".
- `tests/SoilTests`: 36 checks.
  - The inverse aridity curve.
  - The floor only raises fertility, and only off the deserts.
  - 21 semi-arid, Mediterranean, steppe and savanna places get soil, among them Athens, Seville, Murcia, Ankara,
    Los Angeles, Perth, Santiago, the Kazakh steppe, Ulaanbaatar, Amarillo, Bamako and Mendoza.
  - 7 deserts stay desert: Western Desert, Rub' al Khali, Atacama, Taklamakan, Death Valley, Namib and the Simpson.
  - Vanilla's fertility at Athens is below its soil threshold (the reported bug).
- `docs/research/soil-2026-11-03/SoilCheck.cs`: the worldwide survey.

## Verification

- Offline: `SoilTests` 36 checks pass; the survey is in the research note.
- Native server (VS 1.22.7, new world, eight places, census within 192 blocks):
  - **Athens:** soil-low 87,517 columns against about 107,000 sand before; fruit trees peach 4, olive 3, pomegranate 3,
    yellow apple 2, cherry 1, where the run before had none.
  - **Murcia, Kazakh steppe, Amarillo, Bamako, Adelaide, Los Angeles:** soil.
  - **Western Desert:** 99% sand.
  - No errors in the log.

Not verified: grass and tall grass on the new soil in a client, farming on it, and play.

## Rules 2: real deserts in ground-column worlds (8 November 2026)

The author, after sand and soil near Suez in their 1:10 ground-column world: "real deserts should not have topsoil. top
soil was meant for semi deserts, where is still some vegetation and actual soil."

- **The fault.** Ground columns ([0056](0056-rocks-and-ground-apart-from-ores.md), `Core.GroundColumns.Build`) put a humus
  horizon on every dry land column, the Sahara's and the Rub' al Khali's included. This rule's line never reached them.
- **Rules 2.** A ground-column world saved with `SoilRules` 2 leaves arid land without topsoil: aridity index below
  0.2 on the ambient rainfall, and still below it with a river's green belt counted in (`TerrainModel.RiparianRain`,
  the raised rainfall vanilla's fertility sees in the climate map). The cover's own top is the ground there: sand
  sheets and dunes sand, alluvium silt, fans gravel, playas mud, thin cover regolith. Semi-arid land (0.2 and wetter)
  keeps its topsoil as before. Peat stays peat. There is no temperature test: cold land (larch taiga on permafrost) is
  not desert. Vanilla-path worlds are unchanged (they already keep vanilla's sand there).
- **Code.** `SoilFertility.RulesVersion` 2 and `AridGround`; `GroundColumns.Place.Arid` → `Sample.Arid` (skips the
  topsoil); `EarthGeology.GroundAt` sets it for dry land in worlds with soil rules 2, asking the river belt only where
  the ambient rainfall is arid. The startup log says "Earth ground columns: soil rules v2, arid land … has no topsoil".
- **Tests.** `SoilTests` (231 checks): the seven deserts are arid ground, the 21 semi-arid places are not, half way
  into a river's belt is not. `GroundColumnsTests`: an arid place's column starts with sand, silt or gravel by its
  cover and has no topsoil fertility; the same place not arid keeps topsoil.
- **Known gap: arid floodplains.** The green belt is about a kilometre wide at 1:10 (the river's half width, 8 blocks
  and up to 20 + 30 + 6 × half width more, fading). Across the Nile valley at Luxor and Asyut, the Nile delta and
  Mesopotamia 98–99 % of samples stay arid (probe, 8 November), so their alluvial plains outside the belt show silt,
  not farmland, though in 1318 they were Egypt's and Iraq's fields. The 0.1° wetland grid is too coarse to draw them
  and the cover map's alluvium also covers desert wadis and old deltas (the Suez spot is 58 % alluvium). A floodplain
  map from the heights (land a few metres above its river, as GFPLAIN250m does) could give them their soil. Rules 3
  (below) does that.
- Offline only: not in a test build, not seen in game.

## Rules 3: arid floodplains keep their soil (8 November 2026)

The parent session's brief, from the author's Suez report: "Give arid floodplains their soil in ground-column worlds, as
a global rule for new worlds only", the Nile valley and delta getting soil, the Suez spot, the Western Desert, the Negev
and the Gobi staying bare. Asked for a source, the author chose "GLO-90 + Earth rivers": no download.

- **Rules 3.** In a ground-column world saved with `SoilRules` 3, arid ground keeps its topsoil where it lies in the
  natural flood of a river that carries water through the dry season, unless its cover is wind-blown sand (dunes, sand
  sheets). The flood is baked once (`tools/FloodplainBake`, `assets/earth/config/floodplains.bin`, 2.71 MB): from every
  course of 10 m³/s or more with at least 10 m³/s from moist catchments, where it runs through land with an aridity
  index below 0.3 (Holdridge's or the Penman-Monteith one of aridity rules 1, so one grid serves worlds with and
  without those rules), the flood stands 1.2·Q^0.3 m above the water (Nile 12.6 m, Euphrates 8.7 m, 100 m³/s 4.8 m) and
  falls 0.15 m per km of the way out over connected land, seas and lakes stopping it. It stands no higher than the
  nearest river's top, so it does not run down a valley above the river beside it, and covers no ground more than one
  flood height below that river's water, so the Salton basin and Wadi Natrun stay dry. The grid keeps the level on 15″
  cells; a column is in the flood where its own height is at or below the level there, so the edge follows the valley
  foot, not the grid. Sources and the rejected ones (GFPLAIN250m excludes deserts by design, MERIT Hydro's drainage
  starts in every wadi): [research](../research/arid-floodplains-2026-11-08/README.md).
- **Wind-blown sand.** The flood reaches the dunes beside the Tarim, the Indus (the Thar) and the Amu Darya, but dunes
  are not floodplain: GUM's eolian units and Arenosols (`GroundColumns.WindSand`: the `dune`, `dune-on-till` and
  `sand-sheet` covers) stay sand. No other cover is excluded: GUM's rock units and Solonchaks would cut rectangles into
  the flood and bare half of southern Iraq.
- **Code.** `SoilFertility.RulesVersion` 3, `FloodplainRules`; `Core.FloodplainGrid` (format, bilinear level);
  `TerrainModel.Floodplains`, read by `EarthWorldGen.InitWorldGen` in worlds with soil rules 3 and ground columns;
  `GroundColumns.Place.Floodplain`, set by `EarthGeology.GroundAt` for arid columns in the flood and applied by
  `GroundColumns.At` once the cover is known. The startup log says "… river belts and floodplains excepted". Worlds with
  soil rules 2 or older are unchanged.
- **Results** (1:10, the game's rule): arid samples with topsoil across the Nile delta 161/196, 203/225 and 158/167 at
  30.85, 31.10 and 31.30 N (rules 2: about 1 %), the valley floor at Luxor (11 km) and Asyut, Mesopotamia 202/240 and
  194/221; the Suez transect 0/34; the Negev, Western Desert, Gobi, Sahara, Rub' al Khali, Atacama and Namib 0. The
  same in worlds with and without aridity rules 1; with them, the Volga's floodplain at Astrakhan too (85 of 90).
- **Tests.** `FloodplainTests` (48 checks): the format, the shipped grid's levels over 13 field places in Egypt, Iraq,
  Sindh and Khorezm, no flood at ten desert places or on the plain around the Suez spot. `GroundColumnsTests`: the flood
  keeps topsoil except on wind-blown sand and never makes land arid. `SoilTests` (232): new worlds get rules 3.
- **Known limits.** The Karakum beside the Amu Darya lies about level with the river downstream, so salt flats and
  alluvium within about 60 km of it get topsoil (80/151 samples at 39.2 N); a height-above-drainage rule would separate
  them but needs a world-wide flow routing. The Amu and Syr Darya deltas flood widely (medieval Khorezm). From 31.0 N,
  5 km north of the Suez spot, the flats on Lake Manzala's eastern shore are in the delta's flood. Towns stand above the
  flood in GLO-90's surface heights and show silt. Earth's rivers have no Damietta branch; the eastern delta is flooded
  across from the Rosetta branch.
- Offline only: not in a test build, not seen in game.

## Open

- **Desert surfaces.** Non-dune deserts are still all vanilla sand. Real hot deserts are mostly stony and gravel
  plains (reg, hamada) and bare rock, with sand seas covering about a quarter. GUM's sediment classes could choose
  between them. Not decided.
- **Soil grade.** Steppe soils (Chernozems) are richer than `soil-low`, and old tropical soils (Ferralsols) poorer than
  vanilla's rain-driven `soil-medium`. A soil-grade map, such as HWSD or SoilGrids, could replace the floor. Not decided.
- **Plant placement.** Climate fertility for plant patches and trees is unchanged: only the block layers see the floor.

## History

- 3 November 2026: written and implemented after the author's direction; survey, offline tests and native server run.
- 7 November 2026 (ranges proposal step 5a, branch `claude/region-index`, uncommitted; the author: "Yes, build 5a", "New worlds sure"): the soil this rule gave semi-arid land was carpeted by vanilla's tall grass, which fills about 95 % of treeless soil (`GenBlockLayers.PlaceTallGrass`), for example at Tucson. New worlds now keep only a share of that grass by the same aridity index (`Core.GroundCover`, `assets/earth/config/ground-cover.json`: 15 % at 0.2, 30 % at 0.3, 70 % at 0.5, all from 0.75), saved as `EarthParams.GroundCoverRules`. The soil itself, the soil's grass cover and vanilla's grass colour by rainfall are unchanged. Offline only.
- 8 November 2026 (branch `claude/biomes-1.0`; the author: "Fix. But real deserts should not have topsoil"): soil rules 2,
  real deserts without topsoil in ground-column worlds (above). New worlds only; offline only.
- 8 November 2026 (branch `claude/arid-floodplains` from `claude/biomes-1.0`; the author chose "GLO-90 + Earth rivers"
  as the source): soil rules 3, arid floodplains keep their soil (above). New worlds only; offline only. The branch
  merges `claude/biomes-1.0` at `663205a` (in `release/1.0` as `63fe489`), and the grid is seeded on both aridity
  indices.
