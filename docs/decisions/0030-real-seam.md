# 0030: The real Earth across the seam (no gutter)

Status: Agreed (the user, 2026-10-25); built in 0.16.14-realseam.1 (in Earth 1.0.0); in Earth 2.0.0. A new world's seam band has not been checked in game (EA-0001 closed on the author's general review, 12 November) | Date: 2026-10-25 |
Decided by: the user, after the play-test report on St. Lawrence Island

## Context

Earth inserted a strip of open sea, the seam gutter (2 368 blocks in a 256k world, 2 240 at 1M), where the ring's ends
meet. It held no real geography, and the real 360° filled the ring less the gutter. The straight seam (0019) runs along
169.25°W and crosses St. Lawrence Island. [0029](0029-seam-land.md) carried the island across the gutter by drawing its
north-south profile at the seam across the whole gutter. In the play-test (issue 2, 25 October) Wmap's World Atlas showed
a straight bar of constant width. The user: "St Lawrence Island over the seam looks stretched over the seam and not its
real shape", and expects the island as on an equirectangular Blue Marble.

The gutter was meant to guarantee sea at the jump line in any world. The user (25 October): the jump now works well on
land too, so careful placement (preferably at sea) is enough; a gutter may still be useful for custom worlds.

A first fix, capes and a spit across the gutter, removed the bar but still drew about 2 km of invented land. The user
chose the real Earth instead.

## Decision

- New worlds have no gutter (`EarthGeometry.Gutter = 0`, `LayoutInput.Gutter = false`): the ring holds exactly 360°,
  and the real Earth runs across the jump line on 169.25°W. They save `EarthParams.GutterSeabed = 3`
  (`TerrainModel.RealSeamVersion`).
- `TerrainModel.RealSeam`: a column is taken at x wrapped into the ring, so both laps of the ring (one jump apart) are
  the same column. The data (elevation, coasts, islands, rivers) already repeats with longitude. The detail noise is made
  to repeat by `SeamNoise`: within 512 blocks of the jump line it blends into the same noise one ring further, half and
  half at the line. The two ends of the ring meet without a step, and elsewhere the noise is unchanged.
- Cylinder World needs no change. It reads `GutterSeabed >= 1` as "Earth's seabed repeats at the seam" (CW-D26) and
  `>= 2` as "keep Earth's land in the band" (CW-D31), so its band keeps Earth's real terrain. In the identical part it
  rebuilds land from Earth's height. Wmap draws Earth's columns.
- Terrain expansions stay 1 280 blocks from the jump line, out of Cylinder World's identical band.
- Only where Cylinder World keeps Earth's terrain in its band: its Earth polar mode in a new world (`PolarIceMode`
  "earth", `SeamParams.EarthPolarIce`) with layout v10 or later. In its default mode its own sea band (672 blocks each
  side at 256k) would cut the Earth at the jump line, so such worlds keep the gutter, with a log note.
- `EarthMapConfig.SeamGutter` (default false) gives new worlds the old layout with a gutter (GutterSeabed 2), for custom
  worlds that want sea at the jump.
- Existing worlds keep their saved gutter and GutterSeabed (0, 1 or 2): their terrain does not change.

## Alternatives

- *Capes and a spit across the gutter* (built and checked on this branch first, never installed): the bar became a cape
  on each side and a narrow, low spit across the jump line. Still invented land; replaced at the user's choice.
- *A sea channel at the jump line* (before 0029): the island split, with the part under the gutter missing.
- *Move the seam off the island*: every meridian near the Bering Strait crosses land (St. Lawrence Island, the
  Diomedes, Umnak); 0019 chose 169.25°W as the one crossing the least.

## Consequences

- St. Lawrence Island has its real shape and length (171.84-168.70°W in a 256k world, real 171.85-168.68°W), and the
  real longitude scale holds right up to the jump line (1.2 % more blocks per degree east-west at 256k, 0.3 % at 1M).
- Land within Cylinder World's identical band has its surface rebuilt by Cylinder World: soil and grass, tall grass, no
  trees, no structures. In a 256k world that band is under 672 blocks (about 1.1°) each side of the jump line. It covers
  the middle of St. Lawrence Island, Big and Little Diomede, Cape Dezhnev (0.4°), the Pribilofs' St. George
  (0.25-0.45°), Samoa's Manu'a (0.15-0.45°, which lose their forest there) and possibly Niue's west coast (0.55°). In
  bigger worlds the same blocks cover fewer degrees. Rock strata under the surface still differ across the jump, as under
  the sea band.
- Ships cannot cross the jump line at 63.2-63.3°N; players walk across the island.
- Wmap no longer marks positions as "seam waters": no position is outside the Earth.

## Implementation

0.16.14-realseam.1 (branch `playtest/seam-island-2026-10-25`, from 0.16.14-playtest.1): `src/Core/TerrainModel.cs`
(`RealSeam`, `SeamNoise`, `RealSeamKeepOut`), `src/Core/EarthLayout.cs` (`LayoutInput.Gutter`), `src/EarthMapConfig.cs`
(`SeamGutter`), `src/EarthMapSystem.cs` (new worlds), `src/EarthParams.cs`.

Tests: `EarthMapTests` `CheckRealSeam` (also alone as the `realseam` mode). For the real seam, no column within 700 blocks
of the jump line differs one ring apart (0 of 183 367 at 256k, 0 of 733 990 at 1M), and no row along the whole meridian
steps at the jump line (0 of 6 323; 0 of 25 310). The same layout without the noise blend differs in 4 507 (17 328)
columns and steps in 4 (13) rows. St. Lawrence Island is 3.14° east-west (3.15° at 1M) and continues across the jump
line. `render` mode takes `NOGUTTER=1`, `GUTTER=n` and `COASTS=1`.

## History

- 2026-10-25: capes and a spit tried first; the user chose the real Earth. Implemented in 0.16.14-realseam.1. Not played.

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: Agreed (the user, 2026-10-25); implemented in 0.16.14-realseam.1; installed 2026-10-26 in 0.16.14-playtest.2, not played
