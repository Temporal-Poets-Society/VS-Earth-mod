# 0016: Story sites at their own ground; no vanilla ruins beyond 70°N / 60°S

Status: Agreed, implemented 0.14.8, server self-test passed, not played | Date: 2026-10-20 |
Decided by: the user, after playing `earth 0145` (Lisbon spawn, Cylinder World)

## Context

- Wmap showed a perfectly round flat area about 0.7° across near Lisbon. It was the treasure hunter's story site: Earth
  levelled every surface story location over vanilla's landform radius (200 blocks + 16, then a blend of at least 96),
  whatever the building's size (the treasure hunter is 18×19). Vanilla's "surface" placement (village, Tobias' cave,
  devastation area) puts the building at sea level, so those sites were levelled down to sea level + 1. The user: the
  flat areas look "too big and level out of place"; the treasure hunter, village and Tobias' cave were "really sore
  eye", the lazaret fitted well, and the devastation area (not moved: no suitable land, so vanilla's spot in central
  Saudi Arabia, unflattened) was "a great fit actually". Tobias' cave brings its own hill (its schematic stands about
  47 blocks above its edges), which looked like "a big hill in the middle of nowhere" on the flat Libyan desert.
- Vanilla ruins spawned in Antarctica. The user wants ruins rethought later through Earth Ruins; for now the easiest
  fix is a latitude limit, north and south, without an ice-sheet rule ("do second for north and south and not no
  surface ruins on ice sheets anywhere").

## Decision

- **Story sites** (new worlds): each surface building gets a levelled pad that follows its footprint (the schematic's
  box, 8 blocks beyond it, then a blend of 0.6 × the larger half-side, 32..96 blocks), at the site's own mean ground
  height (at least sea level + 1). Sites are still dry, even, ice-free lowland off rivers, searched around vanilla's
  distances from their parent, and (since 0.15.5) at least 75 % of vanilla's distance from spawn and from the parent.
- A building whose blocks stand at least 30 blocks above its edges (Tobias' cave) looks for hilly surroundings: the
  upper quartile of two rings at 1.6 and 2.4 site radii must rise at least 0.4 × its own relief, and the site scores
  best near 0.7 × that relief. Without such a site it settles for an even one.
- The devastation area (it has a generation radius: its blight spreads about 700 blocks beyond its 67×67 tower, over
  whatever terrain is there) is not searched for: it stays at vanilla's own spot (as Cylinder World moved it) when that
  is dry land, ice-free and off rivers, else it moves to the nearest such land (widening from 256 blocks up to 4 ×
  `StorySearchRadius`). Only its tower's footprint gets a pad, at the local height, like the others. The user chose
  this (option D, 2026-10-21) over keeping the old rule (A), a lowland pad like the others (B, which would never have
  kept it on the Saudi plateau) and never moving or levelling it (C: vanilla's sea-level height, so it could stand in
  the sea or be buried in mountains).
- **Heights, every start** (`StoryPlacer.ApplyHeights`): vanilla resets story heights at each start; Earth then sets
  each zoned building's height from its pad (vanilla's "surface" buildings switch to the worldgen-height path, one block
  below the pad's top, which is their old relation to the sea-level pad). Older worlds therefore keep exactly the
  heights they had; the pad also replaces vanilla's sampling of whichever chunk generates first.
- **Ruins by latitude** (*superseded 2026-11-07, see History: where people lived around 1318, no latitude*): vanilla's worldgen structures (every placement: surface, underground, in water; this includes
  vanilla's structure-placed translocators) and villages are only generated between `RuinsNorthLimit` (70) and
  `RuinsSouthLimit` (−60). Config, read at every start, new chunks only.

## Alternatives

- Keep the landform-wide disc but smaller: still a circle of perfect flatness, and the sea-level height stays wrong.
- No levelling at all: the village (195×225) and Tobias' cave need even ground under them.
- Ruins: no surface ruins on ice sheets anywhere (declined by the user), or a full Earth Ruins placement now (later).

## Consequences

- Saves: `FlatZoneData` gains `HalfX`/`HalfZ` (proto 7/8); old zones read 0 and stay discs. Existing worlds keep
  their sites (positions, discs, target heights) and the heights their buildings had; only new worlds get pads. The
  latitude limit reaches existing worlds for chunks generated from now on.
- Vanilla's own skip radii still apply around story sites (e.g. no trees within 120 blocks of the village centre).

## Implementation

0.14.8: `src/StoryPlacer.cs` (pads, hill search, `ApplyHeights`), `src/Core/TerrainModel.cs`
(`FlatZone` box distance), `src/EarthParams.cs`, `src/Core/RiverAudit.cs`, `src/EarthWorldGen.cs` (heights at every
start, `StructureLatitudeGuard`, `VillageLatitudeGuard`), `src/EarthMapConfig.cs`, README config table.

## History

- 2026-10-20: agreed by the user and implemented. Dedicated-server self-test (seed 12345, 256000×102400, Cylinder
  World 0.11.6, spawn 48.56°N 7.76°E): treasure hunter levelled over 34×35 at y=137, lazaret 66×78 at y=123, village
  211×241 at y=126, Tobias' cave 213×206 at y=127 in hills (45.23°N 26.97°E), devastation area old rule (29.46°N
  23.30°E, still the old disc at that point); renders checked. Not played.
- 2026-10-21: the user chose option D for the devastation area; the old disc rule (`FlatSeaLevel`) is gone.
- 2026-10-21 (0.15.5): the user spawned near Lisbon (world `e 153`, 0.15.3) with the village 615 blocks away; vanilla
  had it about 14 000 blocks from spawn. Each mirror image was chosen on its own: the lazaret went east into Spain, the
  village's mirror pointed back west into the Atlantic, and the search around that target settled on the Tagus lowland
  next to spawn (nothing kept sites away from spawn, and the distance penalty is small against flatness). The user
  agreed: every site, the devastation area included, now keeps at least 75 % of vanilla's distance from spawn and from
  its parent (candidates closer are skipped), and mirror images whose target is closer to spawn than that floor are not
  searched (when all are, only the farthest). The log line gives each site's distance from spawn and vanilla's. Existing
  worlds keep their sites. Server test (Lisbon spawn, 1024000², Cylinder World 0.11.9, seeds 12345 / 777 / 424242):
  village 13 457 / 13 607 / 13 548 blocks from spawn near Córdoba (vanilla 14 158 / 15 470 / 14 514), lazaret 7 139 /
  7 590 / 7 239 (vanilla 7 884 / 8 061 / 7 951), every site at or above its floor. Not played.
- 2026-10-22 (0.15.5): the devastation area still followed Cylinder World's spawn (map centre 10.75°E, 44°N), not
  Earth's: Cylinder World moves vanilla's chain to its own spawn, and option D keeps that spot, so with a Lisbon spawn
  it landed in southern England (in the user's world) or 40 000–80 000 blocks away in tests. The central Saudi spot of
  `earth 0145` came from the same mismatch. The user chose a public `SetSpawn(x, z)` in Cylinder World 0.11.11
  (its CW-D28; nothing is released, so no fallback for older versions) over Earth writing its fields: Earth hands it its
  spawn in new worlds right after choosing it, and the whole vanilla chain starts there. When vanilla's devastation
  spot has no dry land within 4 × `StorySearchRadius`, it now searches around the mirrored offsets from its parent
  like the other sites (before, it stayed in the sea). Server tests: Lisbon spawn, 1024000², seeds 12345 / 777 /
  424242: devastation area at 40.20°N 3.76°W and 39.30°N 3.23°W (vanilla's spot, 18 225 / 17 437 blocks from spawn)
  and 39.23°N 4.04°W (open sea at vanilla's spot, mirror fallback, 15 138 blocks; floor 14 050), the other sites as
  above; default spawn (Upper Rhine) at 256000 × 102400, seed 12345: every site between 86 % (Tobias' cave) and 112 % (resonance
  archive) of vanilla's distance from spawn, devastation area at 65.80°N 34.50°E (Karelia). Not played.
- 2026-11-07 (ranges proposal, step 4, branch `claude/region-index`, not merged; agreed by the author: "Yes. Later i
  want to remake ruins completely"): the latitude lines and `RuinsNorthLimit` / `RuinsSouthLimit` are gone (the keys
  drop out of earth.json at the next start). Vanilla's worldgen structures (ruins, trader camps, structure-placed
  translocators) and villages are generated only where people lived around 1318 (`assets/earth/config/people-1318.json`,
  `PeopleRange`): every region but those not settled by then (Antarctica, Svalbard, the sub-Antarctic islands, and
  islands found later such as the Azores, St Helena, the Mascarenes, Galápagos, Bermuda and the Chathams), and a
  warmest month of at least 0 °C at the ground (no ice-cap climate: the Greenland ice sheet, the highest summits). The
  user had declined an ice-sheet rule here on 20 October; the climate step is not a mask (ice-free coasts and
  nunataks stay eligible) and was agreed with this step. Same fields and reader (`RegionRange`) as the vanilla animals'
  ranges. Every Earth world, new chunks only. Offline (`PlaytestIssuesTests … after`): 28 named sites as expected
  (Hammerfest, Utqiagvik, Resolute, Qaanaaq in; Longyearbyen, the Antarctic Peninsula, the Greenland ice sheet,
  Everest out), none of 26,618 land samples south of 60°S; against the old lines on a 1° land grid 818 Arctic cells
  gain structures (Canadian archipelago, Taymyr, Yakutia, Greenland's ice-free coasts) and 109 lose them (93 on the
  Greenland ice sheet). Interim: the author plans to remake ruins (and traders' rules) completely. Not played.
