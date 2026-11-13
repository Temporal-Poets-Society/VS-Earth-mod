# 0020: The land as before the canals; the canals as a gameplay cut

Status: Agreed (the user, 2026-10-21); implemented 0.15.4 | Date: 2026-10-21 | Decided by: the user

## Context

In worlds e 152 and e 153 (512k, 0.15.2) the Suez Canal showed as a sea channel 2-6 blocks wide from Port Said to Suez,
too narrow to sail, and Panama had Gatun Lake with a thin cut to each sea, all at sea level (the real lake is 26 m up,
behind locks). The Chagres and the other rivers there came down at their own levels and met that water with steps,
which broke the terrain. Neither world had the canal option on (their saved `Canals` was empty). The water came from
the big land's outline coasts ([0012](0012-coast-outlines.md), 0.14.5): GSHHG's L1 shorelines keep Africa apart from
Eurasia and North America apart from South America, split along the canals, so the canals, Gatun Lake, Lake Timsah and
the Bitter Lakes were sea in the outlines.

The user (2026-10-21): "default we need terrain prior canals. with canals we can deviate from real and make something
that look ok but don't need to reflect real canals". Existing worlds need no consideration.

## Decision

- **coasts.bin closes the canal gaps** (`tools/bake_earth.py` `close_canal_gaps`, `CANAL_GAPS`): in a box around each
  canal, the water is split into wide water and channels narrower than 1.6 km (`CANAL_NARROW_KM` 0.8 on each side).
  Wide water that no longer reaches the box's sides (Gatun Lake, the Bitter Lakes, Lake Timsah) becomes land, and so do
  narrow channels that lead into it or join two seas (the cuts). Narrow inlets with open sea on one side only stay.
  Channels beside the smaller islands (islands.bin) are closed too where they touch what closes (the canal's two arms at
  Port Said), and islands the closed water held join the land. The two continents are then one outline in each place:
  312 km² of water made land at Suez, 424 km² at Panama. Lake Manzala and the bays at both ends stay.
- **Without the canal option** a new world has the land as before the canals: desert from Port Said to Suez with the
  Great Bitter Lake as the terrain grid has it (a salt lake at sea level), and at Panama the Chagres valley, the river
  running to the Caribbean, no Gatun Lake.
- **With the canal option** (`SuezCanal`, `PanamaCanal`) the canal is the mod's own cut (SeaChannels, unchanged course
  and width: 16 blocks each side in the standard world, scaled with the world; 79 blocks of ship-deep water in a 512k
  world), with its lakes as discs at sea level. Not the real canals' shape. A natural lake at or just above sea level
  within three radii of a canal lake becomes the canal's sea (`SeaChannels.LakeCatch`): the whole Great Bitter Lake,
  not salt lake water beside the canal's sea at the same level.
- No compatibility with existing worlds, by the user's decision: a saved world with outline coasts gets the closed gaps
  in chunks generated from now on, and its already generated chunks on the canals keep their water.

## Alternatives

- **Use GSHHG's real lake and canal shapes when the option is on** (widened for ships, rivers brought down to sea level
  where they reach them): closer to the real canals, but the user doesn't need them real, and every river join would
  need its own checks.
- **Mask the outlines at run time** in the canal zones (the grid decides there): the coastal profile would follow the
  old canal banks inside the land, and the zone edges would show where the grid's coarse coast meets the outlines.
- **Canals on by default:** not asked for; the default is the land before the canals.

## Consequences

- Only `coasts.bin` changes (same file version 1, 9.5 MB); worlds without outline coasts (cells under
  `CoastMinCellBlocks` wide) never had the gaps and are unchanged.
- The islands at Port Said and in Gatun Lake are still in islands.bin, now standing on land.

## Implementation

0.15.4: `tools/bake_earth.py` (`CANAL_GAPS`, `canal_gap_fill`, `close_canal_gaps`, called by `coast_rings`; rebaked with
`--coasts-only`, needs shapely), `earthdata/coasts.bin`, `src/Core/SeaChannels.cs` (`LakeCatch`, `IsLowLake`; the
canal's cut no longer keeps lake water at sea level beside it as a lake).

Tests (`tests/EarthMapTests`):
- `ringcanals <ring>` runs the canal checks (ship-deep water from sea to sea, no water walls, lakes at sea level) in a new
  world of any ring size, and lists water beside water at another level. `RINGCOASTS=1` adds the outline coasts,
  `RINGCANALS=suez,panama` the canals (also for `ringrender`), `PROBE=lat,lon;...` prints columns. At 510 976 with
  outline coasts and both canals: 79 blocks of 6-deep water through each, no water walls, no level steps.
- The full suite (world height 256) fails only "4M dalmatia: the rocky coast rises steeply", as it did before this change.
- Renders at 512k with outline coasts, canals off and on: `tests/out/panama-512k-coasts-*.png`,
  `tests/out/suez-512k-coasts-*.png`.

## History

- 2026-10-21: the user reported the canals as unnavigable streams and broken terrain at Panama; cause found in the
  outline coasts; direction agreed by the user; implemented in 0.15.4, offline checks only. Not yet checked in game.
