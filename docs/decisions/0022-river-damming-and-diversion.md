# 0022: River damming and diversion: a river flow query in Earth, the water behaviour in a separate future mod

Status: Partly built elsewhere, partly proposed. Part A (the river flow query) built under 0027 in 0.16.13 (in Earth 1.0.0); in Earth 2.0.0. Part B became Hydrology (0024): dams that raise and spill their reach are built in Hydrology 0.6.2 (its decisions 0008-0010), which Earth 2.0.0 requires. Dams that give way (B4), drying below a diversion (B5) and canal locks: proposed, not built | Date: 2026-10-22 | Decided by: not decided (written down at the user's request; proposed by Claude)

## Context

The user (2026-10-22) wants to redirect rivers to build water mills, and to dig a canal (Panama) and have the water
fill it by itself. Today digging a river's or the sea's shore lets vanilla water flow a few blocks and stop.

Research in the same conversation (mod pages read 2026-10-22; nothing installed by that conversation):

- **Vanilla 1.22 already has water wheels** (`assets/survival/blocktypes/mechanics/waterwheel.json`,
  `MPWaterWheel`). The handbook: they must sit above or next to rapid water, which "cannot be placed or created by the
  player". Earth already writes rapid water on its rapids (`rapidIds`, `src/EarthWorldGen.cs`), so vanilla wheels
  should work there (not checked in game).
- **Realistic Water** (blondie, 0.4.0, 1.22.0-rc.8 to 1.22.7) spreads water about 100 blocks looking for a way down;
  a trench 2+ blocks wide fills with source water ("lake extension"), a 1-wide one carries flowing water that dries
  when cut off. Lakes stay "infinite"; the page does not describe a level rising behind a dam. It says Watersheds'
  river streams are source blocks and so cannot be dried by a dam. **Realistic Rapids** (same author, 0.1.3) lets a
  fall of about 10 blocks or more power the vanilla wheel (max power 4, wheel directly below the fall).
- **Hydrate or Diedrate** (thirst, wells, aquifers; 2.5.8, 1.22.7) does not change how water flows.
- True Archimedes Screw (1.22.0–1.22.4, multiplayer untested), Aqueducts and Sluices / hcwforked (1.22.0–1.22.4,
  reported crash on 1.22.7) and Hydraulics Redone (discontinued, 1.21) are not candidates for now.

What no mod does: water rising behind a dam to its spill point, a dam giving way, a natural river drying below a
correct diversion, canal locks.

Earth's river pools are source blocks (the lake water block, `src/EarthWorldGen.cs` at the water fill), so with any
water mod a single row of soil "dams" the Amazon and nothing happens: the pool upstream stays, and so does the river
below. The user: "if trying to dam river then it overflows to restore the downstream flow or destroys weak dam (now can
dam amazon with single row soil blocks). if diversion is done correctly then drying should be possible." The user also
does not want to depend on many small mods, and has no time for a new water mod now.

## Decision (proposed)

Two parts with separate owners. Neither is agreed; nothing may rely on them.

### A. Earth: a read-only river flow query

Earth offers a generic, read-only service over the river network it already lays out (`RiverNetwork.Nearest`,
`RiverReach.Next`, `Q`, `Level`), in the style of `LongitudeAt`:

- `RiverAt(x, z)` → null when Earth is inactive or no river is there; else the reach id, the discharge (m³/s, the
  HydroRIVERS long-term mean, as the river was laid out), the water surface Y and bed Y at that column, the downstream
  direction, the width in blocks, and whether the column is a river, an exit channel or a lake.
- `RiverReachInfo(id)` → discharge, downstream reach, upstream reaches (an inverse of `Next`, built once), length.
- Server side only (the client has no terrain model), any thread, a pure function of the world's data and parameters:
  nothing saved or sent. It describes the river **as generated**; it does not track what players did to it.

This fits [0003](0003-geography-only.md): geography as a generic service; the water behaviour is not Earth's.

### B. A separate future water mod (working name only)

A companion mod (not Earth, not a fork, see Alternatives) that reads A and acts only when a player changes something:

1. **Blockage check.** When a block is placed in or broken next to river water, a bounded search through the water
   blocks (a few thousand columns, off the main thread) checks whether the upstream side still reaches the downstream
   side. If it does, nothing happens.
2. **Rising water.** A blocked river becomes a pond record `{area, level, inflow, spill point}`. The same search finds
   the lowest point of its rim (priority flood). The level rises by the inflow divided by the area, in whole blocks:
   one layer of block writes at a time. Inflow is the reach's discharge times a configurable gameplay scale: a creek
   takes minutes, the Amazon seconds.
3. **Spilling.** When the level reaches the spill point, the flow leaves there: over the dam (the river below goes on,
   with a fall that can drive a wheel, e.g. with Realistic Rapids) or into a player's channel (a diversion).
4. **Dams give way.** Each dam block holds a head of water set by its material and the dam's thickness
   (illustrative: loose soil or sand 1 block of water per block of thickness, packed earth or clay 2, cobble 3,
   mortared stone 6+). Above that the weakest blocks break; an overtopped soil dam wears from the top.
5. **Drying below a correct diversion.** If the spill point leads away and the water does not rejoin the river below,
   the river below the dam dries over a bounded stretch: down to the next tributary that brings the flow back above a
   threshold (from the reach graph), and never beyond a server-configurable length. The stretch is one saved record
   (reach, from, to, flow removed): loaded chunks drain at once, unloaded ones when they load, and new chunks read it
   too. Removing the dam refills it. A diversion that rejoins the river (a mill race) keeps the river wet.
6. **Multiplayer.** Server-authoritative and event-driven; clients only see the occasional block changes. Land claims
   already stop building in other players' claims; the length cap stops draining a river through a town far
   downstream.

Water that is not an Earth river (vanilla lakes, the sea, player water) keeps the behaviour of whatever water mod is
installed. Without Earth the mod would have no river data; whether it should work on other world generators (e.g. by
inferring flow from the pool's size) is open.

## Alternatives

- **Rely on Realistic Water and Realistic Rapids alone.** Covers canals that fill and mills under a built fall; does
  not cover rising ponds, failing dams or drying rivers. Recommended as the companions to test in the meantime; the
  proposal adds to them rather than replacing them.
- **Fork Realistic Water.** Its mod page names no source repository or license, and the 0.4.0 package has none
  (`modinfo.json`: author "blondie", no license field). A fork needs the author's permission and would fall behind
  each upstream release. Preferred: a companion like Realistic Rapids, ideally with a few hooks asked of the author
  ("is this natural river water", "this stretch is drained").
- **Simulate pressure per water block.** Too costly over thousands of kilometres of river and too many block updates
  for multiplayer.
- **Put the water behaviour in Earth.** Against [0003](0003-geography-only.md); it would tie Earth releases to water
  and mechanical-power changes in the game.
- **Algernon's Watersheds.** Replaces the terrain generator (Earth logs it as incompatible) and invents its own rivers;
  no published source or license.

## Consequences

- A: small, no save impact, no change to generation. It answers for the world as generated, so a consumer must keep its
  own record of player changes.
- B: a new project to maintain, with its own saves (pond and drained-stretch records). Drained stretches change chunks
  away from the player, so the length cap and refilling must be safe on reload and across server restarts.
- Earth's river steps carry short ramps of flowing water below each pool; how a water mod treats them when disturbed
  affects B. A Realistic Water run on Earth worlds appears to be under way in
  `docs/research/realistic-water-test-2026-10-22/` (scenarios for trenches, a mill, a dam, the ramps and a wheel; no
  report yet). Its results should be read before B is designed further.

## Open questions

1. Realistic Water's license, and whether its author would accept hooks or a companion.
2. The gameplay scale of inflow (how fast a pond fills) and the dam strength table.
3. The drying threshold and the default length cap.
4. Whether B should work without Earth.
5. Locks for canals (they follow from rising ponds, but need gates: Aqueducts and Sluices has sluices but is not
   working on 1.22.7).

## Implementation

None. Part A would reuse `RiverNetwork.Nearest` and `RiverReach.Next` behind a public method on `EarthMapSystem`.

## History

- 2026-10-22: Proposed (the user asked for it to be written down as a proposal; the user has no time for the water mod
  now).
- 2026-10-22, later: the Realistic Water test has a report ([realistic-water-test-2026-10-22](../research/realistic-water-test-2026-10-22.md)).
  A dam changes nothing with or without Realistic Water. Realistic Rapids removes Earth's rapids, which the user also
  saw in game. The user then chose a separate water mod, **Hydrology**, that Earth will require
  ([0024](0024-hydrology-water-mod.md)). Part B's future water mod is Hydrology. Realistic Water and Realistic Rapids
  are no longer proposed as companions: they patch the same water behaviours, so they are incompatible with Hydrology.
  Part A stays as proposed here.

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: Proposed
