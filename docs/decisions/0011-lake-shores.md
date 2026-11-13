# 0011: Eased lake shores instead of flat plateaus at the lake's level

Status: Agreed, implemented (0.14.3), server self-test passed, not played yet | Date: 2026-10-18 | Decided by: the user ("fix the
lake-shore plateaus with a ramp instead of a flat raise, as the next version, with a decision record")

## Context

The user saw squares on the Wmap terrain map near Rome (2026-10-17): flat patches south of Lake Bolsena and Lake
Bracciano. They were in Earth's terrain, not the map (Wmap's prediction is Earth's worldgen height), and cliffs in game.

`tools/bake_earth.py` gives every land cell within 3 grid cells (2′ cells, about 11 km) of water the level of the
nearest water and raises it to at least that level + 1 m. In game, `TerrainModel.NaturalColumn` holds every column
whose 2×2 cells carry such a level at least at the water's surface, and keeps the detail noise from dipping land within
30 m above it. Where the ground falls away from a lake (its outlet valley, the far side of a crater rim) the whole ring
stood flat at the lake's level, and where the ring ended the land dropped to the real ground in a straight cliff along
the cell edges: 12 blocks south of Bolsena, 11 south of Bracciano at a 256-high world. The same edge also cut
mountainsides: at the Rhône's mouth into Lake Geneva the noise's deep dips were held at the lake's level inside the ring
and not outside, a 7-block cliff. The worst raises elsewhere were hundreds of metres (a ring cell beside Lake Atitlán
stood at 1557 m over ground at about 820 m).

## Decision

For lakes (not the sea, not lakes at sea level), in new worlds:

- **Bake (`earthdata/shores.bin`).** Only the land touching a lake (grid distance up to 1.5 cells) keeps the full raise.
  From there to the ring's edge (3.5 cells) the raise fades out with a smoothstep, to the cell's own dry land: the mean
  of its 1′ cells that are neither sea, lake nor within two 1′ cells of a lake (narrow deep lakes such as Loch Ness put
  their bathymetry into the plain mean), never below sea level, and the raised value where no such cell exists (half sea
  loch, half lake). The same fade gives each ring cell a nearness to its lake, 1 to 0.
- **Runtime (`TerrainModel.LakeShores`).** The level a column is held at and the noise's dips fade with that nearness
  (`EarthData.TerrainSample.LakeNear`, bilinear over the corners at the level being held, so where a lake's ring meets
  the sea's the sea's corners count as far): at nearness 1, which covers every column with a lake cell among its
  corners, the old rules hold exactly; further out the held level sinks (100 m below the lake at nearness 0.5, a few
  hundred metres where the ring ends) and the dips come back. Nothing switches off at a cell edge.
- `terrain.bin` stays byte-identical. Cells that the islands or the terminal lakes overlay change take that overlay's
  values and the old rules.
- Saved per world as `EarthParams.LakeShores = ShoreData.FileVersion` (1). Older worlds keep the plateaus and opt in
  with `UpgradeExistingWorldLakeShores` ([0002](0002-save-compatibility.md)).

## Alternatives

- *Change `terrain.bin`.* Every existing world would meet the new shores with cliffs in new chunks.
- *Only drop the in-game floor outside the lake's cells.* The baked grid itself was raised, so the plateaus stayed. The
  first version also kept hard edges (where a lake's cells end, where the ring ends) and moved cliffs there; the
  nearness plane was added to remove them.
- *Ease towards the plain cell mean.* Put land 65 to 150 m below sea level beside Loch Ness and Norwegian fjord lakes.

## Consequences

- 9969 grid cells change elevation and 149 532 ring cells carry a nearness; shores.bin is 126 kB.
- Rings of the terminal lakes (lakes.bin, worlds with river ends) keep the old flat raise; they were baked with it.
- A lake whose outlet valley is lower than the lake now has dry ground below the lake's level from the second cell on.
  The lake's own cells and their neighbours still hold the water in (no spills in the tests).
- Upgraded old worlds meet the new slopes with steps where chunks already exist next to the old plateaus.
- Some lake levels look wrong in the data (a lake at 1226 m in Fiordland); this decision does not touch lake levels.

## Implementation

0.14.3. `tools/bake_earth.py` (`build_terrain` "shores"/"near_lake", `bake_shores`, `--shores-only`),
`src/Core/LakeShores.cs` (`ShoreData`), `src/Core/EarthData.cs` (overlay, nearness), `src/Core/TerrainModel.cs`
(`LakeShores`, `HoldFall`), `src/EarthParams.cs`, `src/EarthMapSystem.cs`, `src/EarthMapConfig.cs`. Tests:
`tests/EarthMapTests -- <root> 256 shores` (old against eased around Bolsena, Bracciano, Trasimeno, Geneva, Constance,
Garda and Titicaca: steepest step between dry neighbours and lake water beside lower land), also run in the full suite;
`shoredebug lat lon` prints the columns around a point in both. Before/after render:
`tests/out/lake-shores-rome-before-after.png`.

## Later: the crease at 30 m (GenFixRules 2, EA-0040, 13 November 2026)

On the grid's land the noise's dips near a water level were cut to a fraction below 30 m above it and taken whole above
it. With eased shores the land is no longer held up to the lake there, and the switch showed as a crease along that
contour: 6 blocks in the Rhône plain by Lake Geneva (EarthMapTests' lake-shore check, since 0.15.3's seam moved the
noise there). New worlds (`TerrainModel.GenFixRules` 2) fade the cut in from 20 to 40 m above the water. GLO-90's dry
land takes its own heights and is not affected.

## History

- 2026-10-17: squares near Rome reported; traced to the 3-cell raise.
- 2026-10-18: agreed by the user and implemented for 0.14.3. Offline: steepest dry step Bolsena 12 → 2, Bracciano
  11 → 2, Geneva 7 → 3, Titicaca 5 → 2, the others unchanged; no spills; the full EarthMapTests suite passes in new and
  old world mode; the bake reproduces `terrain.bin` exactly.
- 2026-10-18: dedicated-server self-test on a fresh 256 000 × 102 400 × 256 world with Cylinder World (seed 12345)
  south of Bolsena, south of Bracciano, at the Rhône's mouth and at Bolsena itself: generated terrain = model in
  98.8–99.9 % of columns, no water seams, no dry blocks below river surfaces. Not played yet.
