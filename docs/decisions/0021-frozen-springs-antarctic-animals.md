# 0021: No springs or rapids in frozen rock; no vanilla land animals in Antarctica

Status: Agreed (the user: springs 2026-10-21, land animal list 2026-10-22); implemented 0.15.6 | Date: 2026-10-22 | Decided by: the user

## Context

The user saw rapids coming out of a mountain wall in Antarctica, and a mountain goat there. Neither comes from Earth's
own rivers or fauna rules:

- **Springs and rapids** are vanilla's rivulets (`GenRivulets`, VSEssentials, vegetation pass): water springs in
  cliff faces and cave walls (`tryGenRivulet`) and streams breaking out of mountain sides (`tryGenMountainSideRivers`,
  5 % of them rapid water, the rapid ones at any height). Vanilla's only cold check is the climate map's temperature at
  sea level, at least -15 °C. Earth stores the real surface temperature plus vanilla's altitude compensation there,
  so the coastal mountains of Antarctica (and the high Himalaya) pass, though their rock is frozen all year. Earth's
  own river rapids were already left out on frozen lakes (yearly mean below -12 °C).
- **Goats**: vanilla's `goat-mountain` lives between -19 and 1 °C high up, `goat-muskox` between -30 and -10 °C;
  Antarctica's mountains match. Polar bears, arctic foxes, hares and wolves match too. Earth's regional fauna
  ([EarthFauna](../../src/EarthFauna.cs)) only governs the supported fauna packs' animals (penguins and seals in
  Antarctica), and only when a pack is installed; vanilla's own animals were not checked by location.

The user (2026-10-21): "we should not spawn those anywhere were is permafrost and negative all the year", and the
goat "should not be there".

## Decision

- **Frozen ground** (`TerrainModel.FrozenAllYear`): an ice sheet column, or the warmest month below 0 °C at the
  block's height (the real elevation that height stands for); underground the surface's warmth counts.
- **No vanilla water springs or mountain-side streams (plain or rapid) in frozen ground.** Harmony prefixes on
  `GenRivulets.tryGenRivulet` (water only; lava springs unchanged) and `tryGenMountainSideRivers` make vanilla's own
  random draws for the spot first, then skip it with those draws used up when the ground is frozen there, else rewind
  the generator so vanilla picks the same spot.
- **No natural spawning of vanilla land animals in Antarctica.** *Superseded 2026-11-07 (ranges proposal, step 2, see
  History): ranges in `fauna-vanilla.json` keep them out; there is no latitude line.* As first built, south of 60°S
  (`RegionalFaunaCatalog.AntarcticLatitude`, the regional fauna's Antarctic line), at world generation and at run time: bear, butterfly, chicken, deer, fox, gazelle,
  goat, hare, hyena, moose, pig, raccoon, sheep, wolf (`game:` codes). Fish and monsters (drifters, locusts, the lore
  creatures) are not affected. Real Antarctica has no land mammals; the list covers all of them rather than the
  goats only (proposed by Claude, agreed by the user 2026-10-22: "Keep all vanilla land animals out of Antarctica as
  built").

## Alternatives

- **Clear the springs after vanilla placed them** (a later pass): would need to tell rivulet water from lakes, ponds
  and rivers by shape, and restore the carved notch.
- **Per chunk** (skip a chunk's rivulets when its centre is frozen): a valley below a frozen summit would lose its
  springs, or a frozen face above a thawing valley keep them.
- **Ponds' 0.5 °C threshold** (`PondGuard`, "no month clearly above freezing"): the user named negative all year.
- **Only goats in Antarctica**: polar bears and arctic foxes would still be there.

## Consequences

- New chunks only; already generated springs, streams and animals stay. Other worldgens are unaffected (the guards
  check `Active`).
- The Antarctic Peninsula's shore (warmest month just above 0 °C) and Arctic coasts with a thaw keep vanilla springs.
- New vanilla animals in later game versions are not in the list until added.

## Implementation

0.15.6: `src/Core/TerrainModel.cs` (`FrozenAllYear`), `src/EarthWorldGen.cs` (`SpringGuard`, `SideRiverGuard`,
`AntarcticWorldgenSpawnGuard`, `AntarcticRuntimeSpawnGuard`; a warning is logged when a hook is missing).

Tests (`tests/EarthMapTests`, `CheckFrozenGround`): Graham Land peaks, Vinson Massif, the Transantarctic Mountains,
Ross Island and Everest frozen; Chamonix, Longyearbyen, Reykjavik and Tromsø thaw. Not checked in game.

## History

- 2026-11-07 (ranges proposal, step 2, branch `claude/region-index`, not merged): the vanilla land-animal guard is gone
  (`AntarcticLandAnimal`, its family list and `AntarcticLatitude`). Antarctica stays empty of them through data: every
  vanilla animal has a range in `fauna-vanilla.json` (v3) — the PHYLACINE masks and the chicken box never reach it,
  butterflies now have "every region but ANT", and "Chickens everywhere" (0054) means every region but ANT rather than
  no range. One prefix pair (`WorldgenSpawnRangeGuard`, `RuntimeSpawnRangeGuard`) checks range and altitude limit.
  Checked: no vanilla type eligible on 21,218 land samples south of 60°S (first coast at every degree and a 0.5°
  grid) in natural, 1318 and Chickens-everywhere modes; north of 60°S nothing newly excluded (14,971 land samples).
  Change for saves: worlds with regional fauna but without vanilla ranges (`VanillaFaunaRanges` 0, only pre-release
  development worlds; every release since 1.0.0 has them) lose the Antarctic exclusion for future spawns.

- 2026-11-07 (ranges proposal, step 1, branch `claude/region-index`, not merged): the regional fauna no longer uses
  the 60°S line to decide the region. Earth's single region index (`BotanicalRegions`) gives every land and coastal
  site the nearest TDWG region where the polygons miss the terrain's coast, so the Antarctic coast is `ANT` by
  geography (360 of 360 first-coast samples, as with the line). Fauna rules v4. The vanilla land-animal guard in this
  decision still uses `AntarcticLatitude`; step 2 of the proposal replaces it with ranges and per-animal envelopes.

- 2026-10-24 (review round 1, E1-1; not yet released): the spring guard's cost. Vanilla makes about 2,500 spring attempts
  per chunk column in a rainy 384-high world, ~97 % of them at y = MapSizeY − 2, and each computed a full terrain column
  (`GenRivulets` 1.7–2.0 ms per column on the Devkit test server against 0.05 ms on vanilla; the research report
  `server-gc-stalls-2026-10-23.md` took it for unpatched vanilla). `FrozenAllYear` now answers from the climate first
  when the block's own height is already frozen (the elevation only rises with y, so the result is the same) and
  computes the column only otherwise. Same answers and the same random draws; not measured in game.
