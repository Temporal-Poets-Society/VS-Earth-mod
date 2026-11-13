# 0002: Save compatibility — per-world rule versions, one-shot upgrades

Status: Agreed, implemented (0.13.0) | Date: 2026-10-02 | Decided by: the user, during the 0.13.0 review

## Context

Chunks are generated once and kept. If a world's generation rules change after some chunks exist, new chunks can meet
old ones with a cliff, a cut river or a broken coast. Players keep their worlds across Earth updates, and 0.12.2 threw
at startup on unknown rule versions.

## Decision

- Each world stores the version of every rule set that shapes terrain in `EarthParams` (protobuf, one `ProtoMember`
  per rule set: `Islands`, `RiverFalls`, `RiverChannels`, `RiverEnds`, `GutterBridges`, `GeologyRulesVersion`, ...).
  A missing member reads as 0, the behaviour before that rule existed.
- **Terrain rules that can leave a visible seam** (coasts, rivers, lakes, the gutter bridge, rock layers) keep the
  saved version. New worlds get the current one. An existing world changes only through a one-shot
  `UpgradeExistingWorld*` config flag, which applies to chunks generated from then on and resets itself.
- **Rule fixes without a visible seam** (tree, plant, fauna and ore rules) reach existing regional worlds
  automatically in new chunks; existing deposits and trees stay.
- Nothing throws at startup because of a version: an unknown newer version logs a warning and uses the current rules
  for this session, without changing the saved version (ores, and geology since 2026-10-16: before, geology fell back
  to vanilla strata, a larger break in the rock layers).
- When a fix would change an existing world's terrain, it is gated on a version the world already stores, or on a new
  one; see [0007](0007-seam-crossings.md) for an example.

## Alternatives

- *Always use the newest rules.* Simple, but every terrain fix would put seams into players' worlds.
- *Refuse old worlds.* Safe, but players lose their worlds on an update.
- *Regenerate on upgrade.* Vintage Story does not regenerate player-changed chunks safely.

## Consequences

- Old code paths stay in the code for as long as saves may use them (for example the 4000-block seam test in
  `RiverNetwork`). Each is documented where it branches.
- A world opened with 0.13 or newer can't be opened with 0.12.2 (its rules v2 make the old build throw).
- Old worlds see the old behaviour until the player opts in, including its known faults.

## Implementation

`src/EarthParams.cs` (versions), `src/EarthMapSystem.cs` (upgrade flags), `src/EarthMapConfig.cs`; README "Config"
lists the flags. Golden test of geology v1: `tests/RegionalGeologyTests` (`golden-v1.json`).

## History

- 2026-10-02: agreed and implemented in 0.13.0.
- 2026-10-16: written down as this record. Geology aligned with the newer-version rule (review round 2).
- 2026-10-17 (review round 13): settings frozen into a new world are checked first (a terrain curve of 0 gave NaN heights
  for good: `VerticalScale.Sanitize`, defaults with a warning), and a world already saved with an unusable vertical scale
  is repaired once to the defaults for its height, with an error in the log; its broken chunks stay. Round 14: the same
  for the other frozen terrain and river settings (`EarthParams.RepairSettings`: non-finite, negative or absurd values;
  zero stays, since older saves use it for "not set"; the geology contact width within geology's own 0..512).
- 2026-10-17 (review round 18): the layout too (`EarthLayout.Sanitize`: MaxStretch at least 1, edge sea and gutter
  margin at most an eighth of the world's width), and a saved layout with no room for the Earth is laid out again once
  (`EarthGeometry.IsUsableLayout`).
