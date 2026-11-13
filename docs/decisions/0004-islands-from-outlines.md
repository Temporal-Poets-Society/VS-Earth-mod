# 0004: Small islands from GSHHG outlines, sized by the world

Status: Agreed, implemented (0.14.0; rules v2 in 0.14.4), v1 verified in game | Date: 2026-10-14 | Decided by: the user ("do as you think is best" for the method)

## Context

ETOPO 2022 has false land 10–95 m high on reef rims next to drop-offs (the Great Bahama Bank's southern rim, the Tongue
of the Ocean, Chagos, the Spratlys). Bicubic sampling also overshot by up to +35 m between a 2 m deep reef flat and a
500 m drop, so chains of islands stood where there is only reef.

## Decision

- Islands under 1000 km² come from their GSHHG 2.3.7 full-resolution outlines (`earthdata/islands.bin`). The 2′ grid
  is re-run with only the big land masses, and its cells around the islands are patched in as an overlay.
  `terrain.bin` and `climate.bin` stay byte-identical.
- No fixed size filter. An island is shown where it is at least `MinIslandBlocks` (4) blocks wide in this world, so the
  largest worlds (a 4 million block ring, 10 m per block) get every cay and small worlds skip what is too small.
- Island rules v1 include the reef clamp: at the sea, the bicubic sample may not rise above the highest of its 4 grid
  cells (`EarthData.SampleTerrain`, gated by the island overlay).
- Island rules v2 (0.14.4) round off the outlines GSHHG traced from a raster (straight runs of ~550 m with 45° cuts,
  50-block staircases at a 4 million block ring): two passes of Chaikin corner cutting, each cut at most 0.0015°
  (~170 m), points on 180° kept, the same as the big-land coast prototype (`IslandLayer.Smooth`).
- Saved per world as `EarthParams.Islands = IslandLayer.RulesVersion` (1: 0.14.0's outlines as GSHHG has them,
  2: rounded) and `MinIslandBlocks`. Older worlds keep their rules and opt in with `UpgradeExistingWorldIslands`
  ([0002](0002-save-compatibility.md)).

## Alternatives

- *Filter islands by area.* Rejected by the user: the biggest world should get every feature.
- *Fix ETOPO cells by hand.* Doesn't scale to the world's reefs.
- *Vector coasts for big land too.* Wanted later (a patch exists in the coast session); not part of v1.

## Consequences

- An adversarial review on 2026-10-16 suggested the clamp changes existing 0.14.0 island worlds. It doesn't: the clamp
  shipped in 0.14.0 with island rules v1 (commit 879b922), and worlds without islands don't get the overlay or the clamp.
  Any later change to the clamp needs a new island rules version (v2 is the rounding and leaves the clamp as it was).
- islands.bin is 9.9 MB. GSHHG is LGPL; its attribution is in `earthdata/ATTRIBUTION.txt`.

## Implementation

`src/Core/Islands.cs`, `src/Core/EarthData.cs` (overlay, clamp), `tools/bake_earth.py`. Test:
`tests/EarthMapTests -- <root> 384 islands` (renders `tests/out/islands-*`, rules v1 against v2 in
`islands-4M-ciovo-before/after`).

## History

- 2026-10-14: agreed and implemented (879b922).
- 2026-10-16: released as 0.14.0 (47b2816). Checked on a fresh 512 000 × 204 800 world: terrain = model in 99–100 % of
  columns at the Bahama rims and the Exumas.
- 2026-10-18: island rules v2, rounded corners (not yet checked in game).
