# 0015: The seam gutter's real seabed

Status: Agreed, implemented 0.14.7, not played | Date: 2026-10-20 | Decided by: the user, choosing
"real seabed across the gutter" over removing the gutter, in the Cylinder World session after play-testing Cylinder
World 0.11.5

## Context

With Cylinder World, Earth inserts open sea (the gutter, 2 368 blocks in the standard 256k world) where the ring's ends
meet. Its floor faded from the real ETOPO seabed at the gutter's edges to a flat `GutterSeabedY` (sea level − 40) over
512 blocks, and Cylinder World 0.11.5 (layout v10) laid ring-periodic noise relief over its seam band on top of that.
The user expected the sea floor at the jump line to follow real ocean-bottom mapping (seen on Wmap's chart, which draws
what the game generates). Cylinder World had recorded this only as a proposal on Earth's side (its CW-P01).

## Decision

- New wrapping worlds save `EarthParams.GutterSeabed = 1` (`TerrainModel.GutterSeabed`). Older saves keep 0 and the
  old fade.
- With it, every gutter column north of the Antarctic bridge is open sea whose floor blends the east edge's natural
  seabed into the west edge's across the gutter (smoothstep), with a middle column of its own (detail at an x outside
  the ring), exactly as `TryGutterBridge` does for land. Both edges are the same Earth point (the seam path at that
  latitude), so this is the seam meridian's real seabed, and it depends only on the position across the gutter and on
  z: both sides of the jump line match block for block.
- The floor is kept at least `MinSeaDepth` under water; an edge that is land (where the seam path touches land north
  of the bridge) counts as that depth.
- Cylinder World reads `TerrainModel.GutterSeabed` (reflection, its Earth polar adapter) and in its new Earth-mode
  worlds keeps Earth's floor as its seam band's seabed (its CW-D26), instead of its noise relief.

## Alternatives

- *No gutter* (true Earth at the seam): the most real, but Cylinder World would have to accept land and vegetation
  near the jump line (the Diomedes lie in the Bering Strait) and keep them identical on both sides, and the longitude
  scale changes. The user chose the smaller change.
- *Keep the fade, let Cylinder World add relief*: the 0.11.5 state the user rejected.

## Consequences

- Across the gutter the floor is the seam's real depth profile stretched east–west (no real geography exists there).
- The gutter's edges use fixed edge columns instead of the column's own x, so unlike the old fade they repeat exactly
  one ring apart.
- New worlds only: it moves the sea floor under existing chunks.

## Implementation

0.14.7: `src/Core/TerrainModel.cs` (`GutterSeabed`, `SeamSeabedColumn`), `src/EarthParams.cs` (`GutterSeabed`,
ProtoMember 64; 62 and 63 are the river deltas and widths of the same version), `src/EarthMapSystem.cs` (new wrapping worlds).
Checked by Cylinder World's Earth polar smoke test (`Cworld/tests/PolarChecks/smoke.ps1`): the band's floor equals
Earth's seabed and its mirror one ring away (0 of 1 024 columns differ at 56°N).

## History

- 2026-10-20: agreed by the user; implemented on branch `gutter-seabed` (first numbered 0014, which the river
  widths took), checked with test build 0.14.5-gutter.1; merged and released in 0.14.7 at the user's request.
