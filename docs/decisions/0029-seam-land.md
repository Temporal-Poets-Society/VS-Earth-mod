# 0029: Land carried across the seam gutter (St. Lawrence Island)

Status: Agreed (the user, 2026-10-25); implemented in play-test build 0.16.12-playtest.2, not played | Date: 2026-10-25 |
Decided by: the user, in the Cylinder World session

## Context

Since the straight seam ([0019](0019-straight-seam.md)) the jump line runs along 169.25°W, which crosses St. Lawrence
Island (63.2-63.3°N). North of 60°S the gutter is always open sea ([0015](0015-real-gutter-seabed.md)), so a sea channel
the width of the gutter split the island, and the part under it was missing. The user, after playing: "I think we can try
to stich it together without mandatory water (at least for earth) and do it similar to Poles" (Antarctica is carried
across the gutter by `TryGutterBridge`).

## Decision

- New wrapping worlds save `EarthParams.GutterSeabed = 2` (`TerrainModel.SeamCarriedColumn`). Older saves keep 1 or 0.
- North of the Antarctic bridge, the gutter carries the seam meridian's ground across, land included: the east edge's
  column blends into the west edge's with detail of its own in the middle, as for the seabed. Sea counts with the
  seabed's floor (at least `MinSeaDepth` deep), a lake as land at its water surface; where the blend reaches the water
  surface the column is land (flush with the water: a beach), below it sea.
- Where the east edge, the west edge and the middle are all sea, the column is exactly the GutterSeabed 1 column, so the
  open-sea gutter is unchanged. The Antarctic bridge (south of 60°S) is unchanged.
- The column depends only on the position across the gutter and on z, so both sides of Cylinder World's jump line match.
  The ocean map treats the carried land as land (`GutterBridgedAt`).
- Cylinder World keeps the land in its seam band when the world has it (its CW-D31).

## Alternatives

- *No gutter* (true Earth at the seam, rejected in 0015): would remove the stretch, but the jump line would then run
  through natural terrain that isn't periodic.
- *Move the seam off the island* (169.1-169.2°W cross less of it but pass 3 km from Big Diomede, 0019): still a cut.

## Consequences

- The island is stretched east-west by the gutter's width (2 368 blocks in the standard 256k world): the middle is the
  seam meridian's north-south profile carried across, so the strait between the two halves is about 90 blocks of land
  north to south.
- Gutter land is still "outside the Earth" for regional plants, trees, ores and fauna, as before.
- Ships can no longer cross the jump line at 63.2-63.3°N; players can walk across.
- New worlds only: it changes the terrain under the gutter.

## Implementation

0.16.12-playtest.2 (branch `playtest/2026-10-24`): `src/Core/TerrainModel.cs` (`SeamCarriedColumn`, `GutterColumn`,
`GutterBridgedAt`), `src/EarthMapSystem.cs` (new worlds), `src/EarthParams.cs`.

Tests: `EarthMapTests` `CheckSeamLand`: 43 rows (every 2 blocks between 63.6°N and 62.9°N) of land at the jump line,
none with GutterSeabed 1; 0 of 96 277 gutter columns differ one ring apart; largest step across the gutter 2 blocks; the
open-sea gutter at seven other latitudes unchanged (0 of 427 columns differ). The suite's only failure is the known Lake
Geneva shore check (0019).

## History

- 2026-10-25: agreed by the user; implemented in 0.16.12-playtest.2 with Cylinder World 0.11.15-playtest.2. Not played.
- 2026-10-25: Cylinder World's Earth polar smoke test (`seam-land`, new and reload passes) generates the island's band
  chunk on both sides of the jump: 1 024 land columns at 63.26°N, 0 cells differing, all at Earth's height.
- 2026-10-25: the play-test showed the carried profile as a straight bar of constant width across the gutter. New worlds
  have no gutter and the real island instead ([0030](0030-real-seam.md), GutterSeabed 3); saves with 2 keep the bar.
