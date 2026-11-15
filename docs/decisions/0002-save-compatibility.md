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
- A world saved by a newer Earth is refused (since 14 November 2026, ER-05 of the code review): any saved rules number
  above this build's newest (`EarthParams.RulesAgainstBuild`, every rules field) stops the server with one line naming
  the fields and the version that last started the world (`LastSavedWith`); the world is unchanged. Before, an unknown
  newer version logged a warning (13 fields had one) and ran this build's rules, some clamped and others passed raw, so
  an older build made its own chunks in a newer world for good (an rc.13 world opened with rc.12). Within the rules a
  build knows, nothing throws because of a version. The adopted rules (tree, fauna, ore and placement rules, which move
  up at every start) count too: a world started once with a newer build can't be opened with an older one, even where
  nothing was generated with the newer rules.
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
- The rule versions pin the code, not the floating point under it (T-L11, code review of 14 November 2026). The
  terrain calls `Math.Log`, `Exp`, `Pow`, `Sin` and `Cos` (the vertical scale, the coast profile, river ends, sea
  channels), whose last bit may differ between runtimes and operating systems, and `PriorityQueue`, whose order of
  equal priorities is the runtime's. A world generated further on another machine can therefore differ where a value
  lies at a rule's threshold: a block's height or a channel's route, never the world's layout. The saved-world digest
  (`tests/SavedWorldDigestTests`) pins the terrain on the machine it runs on. Moving those calls to a portable
  implementation would itself be a rules version.

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
- 2026-11-14 (code review ER-05, ER-24): a world saved above this build's newest rules is refused (the bullet above;
  before, a warning and this build's rules). Record-only `CreatedWith` (205) and `LastSavedWith` (206) name the Earth
  versions that made and last started a world. `EarthWorldOptionsTests` pins every `EarthParams` number with its name
  and type, and fails for an int field that is neither a rules field nor listed as a plain number. Later the same day
  (the Fable pass, FP-04): a save that holds fields this build doesn't know is refused too (a newer Earth's new rules come
  as new fields), and the params keep such fields when saved again (`IExtensible`), so an older build that still opens
  them can no longer drop them.
