# 0005: River ends — continued rivers, revived terminal lakes, faded wadis

Status: Agreed; built in 0.14.1 (in Earth 1.0.0); in Earth 2.0.0. Exit channels since changed by 0071 and desert river ends by 0034; the Aral's level is to give way to 1318's under 0064 step 3 (not built). No in-game check recorded | Date: 2026-10-16 | Decided by: the user

## Context

In the user's in-game test (0.14 pre.3 world, 204.8k ring), rivers ended in nothing in Arabia, Australia and near the
Caspian. 18 rivers had gaps or stopped where their discharge dipped below the threshold. Rivers drained by irrigation
(the Amu Darya, Syr Darya) ended at dry beds where the Aral Sea used to be.

## Decision

- **Man-made disasters are undone.** The Aral Sea and the other terminal lakes are revived at their level before 1960
  (`lakes.bin`, 18 lakes, levels tuned against real areas, for example the Aral at 53 m = 65 640 km²).
- **Rivers drained by irrigation flow with their natural discharge.** HydroRIVERS `DIS_AV_CMS` is already naturalised,
  so nothing had to be guessed.
- **Rivers continue downstream to their end** once a reach is shown.
- **Rivers in dry basins fade out** (narrower and shallower over their last stretch) instead of ending at full width.
- **Rivers that stop short of a fjord, estuary or lake are led on to it.**
- **Exit channels are at least as deep as the river's end** (SeaChannels v2: depth max(6, river depth), routed to open
  water whose 8 neighbours are deep).
- **Green shores** around the lakes that had them (per-lake shore width: the Aral 40 km, Lake Chad 25 km, salt pans
  0), raising rainfall like the riparian weight.
- Saved as `EarthParams.RiverEnds = RiverEnds.Version` (1). Older worlds opt in with `UpgradeExistingWorldRiverEnds`.

## Alternatives

- *Present-day lakes (the dry Aral).* Rejected: the user wanted the disasters undone.
- *Hide rivers that end in nothing.* That loses real rivers and leaves the same gaps.

## Consequences

- Worlds with river ends also find seam crossings by geometry ([0007](0007-seam-crossings.md)).
- Open: the Mesopotamian marshes, Urmia's level, rivers across the antimeridian (5 left as they are), Lop Nur's blocky
  west shore on the 2′ grid.

## Implementation

`src/Core/RiverEnds.cs`, `src/Core/TerminalLakes.cs`, `src/Core/EarthData.cs` (lake overlay, shore weights),
`src/Core/SeaChannels.cs` (v2), `tools/bake_earth.py` (`TERMINAL_LAKES`, `--lakes-only`). Tests:
`tests/EarthMapTests -- <root> 384 lakes`; `NOENDS=1` checks an older world.

## History

- 2026-10-16: agreed and implemented as 0.14.1-pre.1/pre.2 (green shores in pre.2). Old worlds hash identical to the
  0.14.0 code.
- 2026-10-16 (review round 2): a river's way on to water was dropped when the cheapest way climbed too high for where it
  ended, even if a longer way over low ground existed. Route now searches again over low ground, then for a fjord.
  Every way found before is unchanged; 1 more river is led on (101 led, 217 faded in the test world).
- 2026-10-17 (review round 16): the exit-channel search (SeaChannels v2) keeps a state per (cell, land cells crossed),
  so a cheap arrival that used up the land budget can't hide a longer way that can still cross the last bar. Channels
  v1 (older worlds) keep the per-cell search. Same result on the real data (893 channels, 1 with no way out).
- 2026-10-17 (review round 17): the fjord fallback of a river's way on to the sea keeps a state per (cell, steps), at
  most FjordCells, so a cheaper but longer way can't hide a short fjord. Same result on the real data.

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: Agreed, implemented (0.14.1-pre, not committed), in-game check pending
