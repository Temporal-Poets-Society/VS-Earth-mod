# 0012: Big land's coasts from outlines in large worlds

Status: Agreed, implemented (0.14.5), in-game check pending | Date: 2026-10-18 | Decided by: the user ("commit first 2
steps and 3 we don't develop for now", after the three steps below were laid out)

## Context

Continents and big islands (1000 km² and more) take their coast from the 2′ terrain grid, about 3.7 km a cell. A cell
is 19 blocks at a 204 800 block ring, 47 at 512 000 and 370 at 4 million. At the standard size that is about as fine
as the coast matters; in large worlds coasts become smooth curves with a bend every few hundred blocks: headlands,
coves, spits and fjords are missing or misshapen, and polders below sea level are flooded. Small islands already come
from GSHHG outlines ([0004](0004-islands-from-outlines.md), rounded in rules v2).

Three steps were laid out on 2026-10-18:

1. Round off the small islands' raster staircases (island rules v2, 0.14.4).
2. Draw the big land's coasts from the same GSHHG outlines in worlds whose grid cells are many blocks wide (this record).
3. Better source data: the OpenStreetMap coastline (metre-accurate, ODbL, 50–150 MB baked) and the terrain grid at 1′
   (about 120 MB instead of 30 MB), as an optional high-resolution data pack.

The user chose 1 and 2, and not to develop 3 for now.

## Decision

As built in the 2026-10-14 prototype (`docs/coast-outlines.md` has the full design and measurements), ported onto
0.14.4:

- **Data**: `earthdata/coasts.bin` (9.5 MB), the GSHHG L1 polygons of 1000 km² and more north of 60°S plus the 130
  GSHHG lakes inside them that the grid has as sea (the IJsselmeer), in 1° tiles. `tools/bake_earth.py --coasts-only`
  reads `terrain.bin`'s flags; `terrain.bin` does not change.
- **Runtime**: `CoastLayer` (signed distance to the outline, rounded like the islands when a tile is decoded) and
  `TerrainModel.CoastColumn`: near the coast the outline decides land and sea, fading to the grid's own decision away
  from it, on ice and south of 60°S. The ground follows a profile from the coast line to the grid's elevation of that
  side, cliffs on rugged coasts and beaches on flat ones. The sea deepens to the world's `MinSeaDepth` a reach out; dry
  land is held at the level the grid holds it at (with eased lake shores, `hold`).
- **When**: new worlds with `Coasts` on (default), with the islands, and whose 2′ cell is at least `CoastMinCellBlocks`
  (32) blocks wide: rings from about 345 600 blocks. Worlds of the standard 204 800 ring are unchanged.
- **Save**: `EarthParams.Coasts = CoastData.FileVersion` (ProtoMember 61; the prototype's 57 is the river ends' now).
  Existing worlds keep their coasts and opt in with `UpgradeExistingWorldCoasts`
  ([0002](0002-save-compatibility.md)).
- **Step 3 is deferred**: no OpenStreetMap coastline and no finer terrain grid. Revisit if worlds of millions of blocks
  become something the user plays.

## Alternatives

- *A finer terrain grid alone* (1′ or 15″): 4× to 64× the data, and the coasts would still be bicubic blobs of the
  height grid, only smaller ones.
- *The OpenStreetMap coastline now* (step 3): better than GSHHG's few hundred metres, but heavy, with share-alike
  licensing for the baked file; worth it only where a block is about 10 m.
- *Smoothing in the bake*: `coasts.bin` would be 22 MB instead of 9.5 MB; the runtime smooths a tile when it is decoded.

## Consequences

- The pack grows by 9.5 MB.
- Coastal columns cost about 0.15 µs more to generate in large worlds; inland and open sea nothing measurable.
- The outline is no better than GSHHG "full" (little detail below about 200 m): fjords narrower than its trace stay
  land (the inner Nærøyfjord).
- Trees, geology and climate placement still read the grid's ocean fraction near coasts, so new outline land over the
  grid's sea (polders, headlands) may get a few trees fewer than inland.
- River exit channels are found on the grid's cells and only cut low land; a channel crossing a high outline headland
  stays blocked there, as with the grid's own headlands.
- An upgraded old world meets the new coasts with a step along every coast where chunks already exist.

## Implementation

0.14.5. `src/Core/Coasts.cs` (`CoastData`, `CoastLayer`), `src/Core/EarthData.cs` (`SeaNear`, `LandElev`, `SeaElev`,
`SampleTerrain(..., coastRefs)`), `src/Core/TerrainModel.cs` (`CoastColumn`, `CoastLandAt`), `src/EarthWorldGen.cs`
(the ocean map), `src/EarthParams.cs`, `src/EarthMapSystem.cs`, `src/EarthMapConfig.cs`, `src/Core/Png.cs` (render
overlay for the tests), `tools/bake_earth.py` (`bake_coasts`, `--coasts-only`). Tests: `tests/EarthMapTests -- <root>
384 coasts` (Sognefjord, Mekong delta and Dalmatia at 512k and 4M, with before/after renders `tests/out/coasts-*`),
also run without renders in the full suite; `coastprobe lat lon ring` and `coastrender lat lon ring w h step`.

## History

- 2026-10-14: prototype in a separate worktree (not committed), measured offline.
- 2026-10-18: agreed by the user with step 1, step 3 deferred; ported onto 0.14.4 as 0.14.5 (city code that had been in
  the same worktree left out; it lives in Earth Ruins). Offline: land and sea follow the outline at 99.7–100% of test
  spots (93.5% → 99.9% in the Sognefjord at 512k, 91.0% → 99.7% at 4M); the Sognefjord open to Flåm and Fjærland,
  the Mekong at Cần Thơ reaches the sea, the Noordoostpolder is land and the IJsselmeer water; inland, open sea and
  the Antarctic unchanged; no coasts at 204 800. The full EarthMapTests suite passes. Not played yet.
- 2026-10-21: found in game (512k): GSHHG splits the continents along the Suez and Panama canals, so the outlines cut
  them as thin sea channels with Gatun Lake at sea level whatever the canal option. coasts.bin closes those gaps from
  0.15.4 ([0020](0020-canal-gaps.md)).
