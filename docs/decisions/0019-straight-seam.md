# 0019: A straight seam along 169.25°W

Status: Agreed (the user, 2026-10-21); implemented 0.15.3 | Date: 2026-10-21 | Decided by: the user

## Context

Wmap's latitude/longitude grid showed the meridians near the Bering Strait bent in an S shape (Wmap session,
2026-10-21). The grid was right: Earth cut the globe along a path through the Pacific found in the data (one longitude
per 0.1° of latitude, 168.5°W..163.6°W, never touching land) and shifted each row so the path landed on the jump line.
The shift faded out within 45° of the seam, so the Pacific, eastern Siberia and Alaska were sheared by up to about 2.6°,
and every meridian there followed the path's bends. The user expected the seam to be straight on the globe and on the
map, and accepts that a straight cut goes through some land: "A straight cut goes through some land - that is fine".

## Decision

- The Earth is cut along one meridian, `EarthLayout.SeamLon` = 169.25°W (`SeamLon0` of new worlds). Every meridian is a
  straight X column at every latitude: `EarthGeometry.LonAt(u)` = SeamLon0 + u·360, `FromGeo` is its inverse, and the
  sky's longitude (`SkyLonAt`, [0010](0010-real-day-and-night.md)) is now the map's longitude everywhere.
- The meridian was chosen on the baked 2′ grid for little land on it and room around it (ocean and floating ice shelves
  count as water, as for the old path; 169.1-169.2°W cross a few kilometres less of St. Lawrence Island but pass 3 km from
  Big Diomede). It crosses only St. Lawrence Island (63.2-63.3°N, about 16 km) and the Antarctic plateau
  (84.7°S-90°S). North of 60°S it passes at least 0.1° from all other land: west of Big Diomede, east of the Pribilofs,
  of Samoa's Manu'a group and of Niue. The gutter's sea splits St. Lawrence Island; Antarctica is carried across the
  gutter as before (`TerrainModel.GutterBridges`).
- No compatibility with existing worlds, by the user's decision (2026-10-21, the mod is not released: "fuck existing
  worlds"). This overrides the save policy of [0002](0002-save-compatibility.md) for this change. A saved world keeps its
  saved `SeamLon0` (about 165.6°W, the old path's mean) and is now cut straight along it: new chunks within 45° of the
  seam no longer meet the old ones, and that cut crosses Alaska.

## Alternatives

- **Keep the bent path:** land-free, but the grid bends near the seam. Rejected by the user.
- **A world setting, or only new worlds:** proposed in the Wmap session; the user wanted the straight seam for all and
  no protection for existing worlds.
- **Straight meridians drawn on the map only (Wmap):** the grid would no longer match the coastlines or the coordinates.

## Consequences

- The world's centre meridian moves from 14.15°E to 10.75°E; every place gets a new x (Lisbon in the standard world:
  x 114 898 → 116 810).
- `earth.json` still carries the baked seam path (`tools/bake_earth.py`, `tools/preview.py`); the mod no longer reads it.
- Wmap needs no change: it reads `EarthGeometry.ToGeo`, and its prediction caches follow Earth's assembly version.

## Implementation

0.15.3: `src/Core/EarthGeometry.cs` (no seam path or shear), `src/Core/EarthLayout.cs` (`SeamLon`), `src/EarthParams.cs`
(`BuildGeometry()` without data; `BuildSkyGeometry` merged into it), `src/Core/EarthData.cs` (seam path no longer
loaded), callers in `src/EarthMapSystem.cs` and `src/EarthWeather.cs`.

Tests:
- `SkyLongitudeTests` checks that the sky equals the map's longitude everywhere, the Bering seam and the poles included,
  and that `FromGeo`/`ToGeo` round-trip.
- `TerrainExpansionTests` places Giza and the Colosseum from their coordinates instead of fixed world positions.
- `RegionalGeologyTests`' frozen v1 output is keyed by world position, so it was rewritten for the new layout
  (`GOLDEN_V1_REWRITE=1`) after it passed on the old one. The cell lookup and contact warp hashes are unchanged.
- RegionalPlants, RegionalTrees, RegionalFauna, RegionalOres, RiverIndex and Weather tests pass.
- `EarthMapTests` (384 high) fails one check, "Lake Geneva: no steeper steps than before": steepest dry step 5 → 6
  blocks at 46.34°N 6.90°E. The check compares the steepest step in a ±0.45° box, which depends on where the detail noise
  falls. On the old layout with model seed 6 the eased shore makes the same 6-block step at 46.33°N 6.93°E, and seed 2
  fails Lake Bracciano's check, so this is not caused by the seam. Left for a decision on the check (decision 0011's
  shores).

## History

- 2026-10-21: agreed by the user; implemented in 0.15.3, offline tests run (one known shore check failing, above). Not
  yet checked in game.
