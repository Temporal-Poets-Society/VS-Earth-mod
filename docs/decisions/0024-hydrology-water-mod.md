# 0024: Hydrology, a water mod Earth will require; river drops that follow the real slope

Status: Agreed direction (the user, 2026-10-22); rule details and interfaces Proposed; step 1 implemented 0.16.2, offline tests pass, not played | Date: 2026-10-22 | Decided by: the user (direction), Claude (proposed details)

## Context

Earth draws each river as a staircase of flat source-water pools. Below every 1-block step it lays a ramp of flowing
water: rapid water where steps follow each other closely, ordinary water elsewhere. That ramp is a finished picture
made at generation. No source of its own feeds it; it is fed by the ordinary water of the pool above. The user found
in game (2026-10-22):
- diverting Earth's rapids with blocks makes them fade;
- opening them again brings back ordinary water, not rapids;
- "does not have source blocks like vanilla rapids".

Vanilla's rivulets are made the other way. Generation places one `rapidwater-still-7` source and schedules a block
update, and the liquid rules draw the flowing part (`GenRivulets.tryGenMountainSideRivers`, VSEssentials, 1.22.7).

A server test the same day ([report](../research/realistic-water-test-2026-10-22.md)) found:
- Realistic Water fills 2-wide canals with source water.
- Realistic Rapids removes Earth's rapids the first time anything touches them, and the user confirmed this in game.
  The vanilla wheel works on Earth's rapids only without that mod.
- A dam changes nothing, with or without these mods.
- A channel the user dug around rapids ended in a flat 1-block face of water. Realistic Water turns both levels into
  source water, so no slope forms.

User direction, 2026-10-22:
- "make separate water mod that initial goal is to serve for earth and make it dependency and then update with all
  other water mechanics we want from Realistic Water and Realistic Rapids".
- Rapids: "generate rapid source block when having 2 or higher block drop".
- Filling: from Realistic Water, "but add vanilla like flattening of water blocks so we don't have vertical faces.
  Vertical faces are for higher rapid blocks".
- "overflow features we can add later but build with discussed aspirational targets in mind".
- Rivers are "static pools connected by drops", and a drop has a regular or a rapid source depending on its height.
- Name: **Hydrology** (mod id `hydrology`).
- Flowing water: vanilla's 6 levels are enough for the river fixes, "but we should consider Realistic Water mechanics
  for flooding etc for also later overflow ideas".
- First, Watersheds' "height aware stream systems" idea, in this sense: **the drops of Earth's rivers follow the real
  slope**.

## Decision

Earth stays geography. It decides where the water is, the level of each pool, and where each drop is and how high.
Hydrology decides which water blocks a drop is made of, both at generation and when players change things. This keeps
[0003](0003-geography-only.md).

### 1. Earth: drops that follow the real slope (agreed direction; details proposed)

Today every step is exactly 1 block:
- gentle reaches step once the valley floor is a block lower, after at least `MinPool` blocks of flat water;
- steep reaches step every `RampLength` (6) blocks, and those steps are marked "rapid" when closer than 0.75 × `MinPool`
  (`RiverNetwork`);
- named waterfalls (`RiverFalls`) are the only drops taller than 1 block.

Proposed instead:
- A pool still lasts at least `MinPool`.
- A drop is as high as the valley floor has fallen since the pool began: 1 block on gentle reaches, 2 or more on steep
  ones. There is no cap: rapids and falls are as tall as the valley needs (the user, 2026-10-22).
- Named waterfalls (Victoria, Niagara, Kaieteur, ...) follow the same rules as any other drop, with their real heights.
  Hydrology draws them like rapids (H4). Earth's part is to set them up right: the lip, the drop height, the plunge
  pool and the gorge below.
- Each drop records its height. Under Hydrology's rules below, a drop of 1 is an ordinary step and a drop of 2 or more
  is rapids.

This is the geography half of Watersheds' height-aware streams. It applies only to Earth's own rivers, which are laid
out from HydroRIVERS and the real elevations.

Watersheds ([study](../research/watersheds-streams-study-2026-10-22.md)) never chooses a drop height. Its whole-block
level follows the lowest bank upstream, and it marks rapids by an averaged slope. Earth knows its valley floor, so it
can set each drop exactly.

Two ideas from the study are worth borrowing:
- an averaged-slope test, to place a steep stretch's drop where the real river falls fastest;
- a short incised approach in the bed above a drop of 2 blocks or more (their "trench": up to 3 blocks over 10), so
  the bank does not end in a hard notch.

It changes the river layout, so it is for new worlds only ([0002](0002-save-compatibility.md)).

### 1b. Rapids and waterfalls by drop height (agreed 2026-10-22; pool layout 2)

The user saw 0.16.3 in game (2026-10-22). Drops of 2–5 blocks on the Congo looked like "small waterfalls, not rapids".
Above Victoria Falls the river stays 7 blocks deep right to the lip, which "looks bit strange". Below Niagara the
curtain stands next to only a thin layer of flowing water. The user chose: **rapids up to 4 blocks, waterfalls from 5**.

- **Rapids** (a drop of 2–4 blocks, not a listed waterfall) are a steep, shallow, rocky chute instead of a curtain.
  - The drop is spread as 1-block steps 3 blocks apart: a 3-block drop takes about 7 blocks. It was 2 apart at first,
    but then step lines met in bends.
  - All the chute's water is rapid.
  - The bed under the chute is 1–2 blocks below the water. A caravel needs 3 blocks (the user, 2026-10-22), so
    seagoing ships cannot pass.
  - Rock breaks the surface, a block above the water, in about 1 column in 8.
- **Waterfalls** (a listed waterfall, or any drop of 5 or more) keep the vertical curtain.
  - A shallow lip: over the last 8 blocks above the edge the bed rises to 2 blocks below the water.
  - A plunge pool at the foot, deeper than the river's bed by half the drop (at most 8 blocks), over as many blocks as
    the drop is high.
- **Hydrology** counts rapids by the fall within a short distance, so a chute stays rapid when touched. The rule is
  "the water falls 2 or more blocks within 3 blocks", rather than a single drop of 2 or more.

This is pool layout 2, for new worlds only. Worlds with pool layout 1 keep their curtains.

**Pool layout 3** (agreed 2026-10-23, after the user played 0.16.4; new worlds only):
- No rocks in the chutes. The user preferred them without, and they came out as soil.
- The bed eases over 8 blocks into and out of chutes, lips and plunge pools, instead of the "deep basins before rapid
  shallow walls" the user saw.
- Above a listed waterfall, forced drops wait for the lip too.

**Pool layout 4** (agreed 2026-10-23, after the user saw Niagara in a 1M world; new worlds only), lake rims:
- A lake more than 3 blocks above a river beside it gives way to the river's channel and banks, plus 2 blocks of rim.
  The lake meets the lower river only at the river's own drop, so the curtain is as wide as the river. Before, the lake
  data for the upper Niagara reached past the falls, and its whole edge along the gorge was one curtain.
- Near such a lake, the lower river's banks and valley keep the land as high as the lake's eased shore holds it (decision
  0011), but never above the natural ground. Before, the valley cut the lake's shore, and its water stood up to 11
  blocks above dry land, with no curtain or rapids.
- When the world generator settles the water, dry land next to any lake's water is raised to the water's top, as it
  already was next to river water.

**Pool layout 5** (2026-10-23, the user's "Build needed changes in both mods"; new worlds only), step 3: drops in the
shape the rule study found stays as generated under Hydrology's rules (Hydrology `watersim verify-shape`):
- a sill under every drop: the upper water's edge column has its bed at least at the lower pool's surface, so
  Hydrology's communicating vessels (deep canals fill layer by layer) can't flood steps or curtain feet;
- the upper water's edge sources labelled with the flow: rapid at a chute's step and down a curtain's lip;
- curtains of falling rapid water on a rapid foot, and a spill of falling rapid water where a chute's ramp ends beside
  the next step;
- the world-config attribute `hydrologyFillVessels`, which turns Hydrology's vessels on for these worlds only;
- ramps one column per height from the step (0.16.9). The river model's ramp heights stretched one height over 2–3
  columns, and vanilla's first update rebuilt them;
- pool layout 6 (0.16.10): a 1-block step and its ramp are rapid only where the river model has rapids and Hydrology's
  rapids window agrees (the rule study's RapidRule); spills from ramp height 2;
- pool layout 7 (0.16.11; the user's choice of gorge sections and 3-deep pools, 2026-10-24): chute steps 5 apart and laid
  out across reach ends; where a chute begins and the valley keeps falling, a gorge section of up to 6 steps; rapids runs;
  white-water sections 3 deep across the full width (a caravel can then pass the rapids), as 1-deep pools flooded when a
  sill was broken.

### 2. Hydrology: a separate mod, first scope (agreed direction; rules proposed)

Hydrology replaces the spreading behaviour of `water`, `saltwater` and `rapidwater`. Version 1 uses only vanilla's
blocks, so removing the mod leaves no unknown blocks.

- **H1 Sources stay put.** Source water never moves or drains, as in vanilla.
- **H2 Filling** (Realistic Water's idea, fresh and salt water).
  - A flowing cell becomes a source when it rests on solid ground or source water and has two source neighbours of
    the same liquid. Realistic Water's diagonal case counts too.
  - Unlike Realistic Water, the new source wakes itself. So a trench opened at its far end fills all the way, and so
    does one dug outward from the water.
  - A 1-wide channel carries flowing water only, for 6 blocks.
- **H3 A 1-block drop is flattened.** The settled shape is a ramp of flowing water, heights 6 to 1, on the lower water.
  This is the shape Earth draws today, and the one vanilla draws from a fed source. There is no vertical face.
- **H4 A drop of 2 or more makes rapids.**
  - Where source water reaches an edge with a drop of 2 or more blocks, the edge cell becomes `rapidwater-still-7`.
  - Below it are falling rapid water (vertical faces belong here) and a rapid ramp on the lower water.
  - When the drop is filled back to 1 or less, the edge becomes ordinary source water again.
  - Any height counts, from a 2-block mill race to a tall waterfall.
  - Vanilla water wheels then work on built mill races as well as on natural rapids. Players may make rapids (the
    user, 2026-10-22); H6 limits the free-power case.
- **H5 Local and cheap.** Same event model as vanilla: a neighbour change schedules a delayed update. There are only
  bounded searches, no ticking of still water, and the server decides everything; clients get ordinary block changes.
- **H6 Buckets don't make sources, by default.** Hydrology turns on vanilla's world setting "Prevent liquid source
  transport by buckets" (`noLiquidSourceTransport`) by default for new worlds (the user, 2026-10-22). Rapids then need
  water that is really there. A server can turn the setting off.

### 3. Earth draws drops with Hydrology (proposed interface)

- Hydrology offers a pure function that Earth calls in its generation column loop. Given the drop height, the distance
  below the edge and the flow direction, it returns the fluid block.
- Generated drops are then exactly the settled shape H3 and H4 would produce, so nothing changes on first touch.
- Rapids get their rapid source, so diverted rapids flow again when reopened.
- Earth's `modinfo.json` then requires `hydrology` (new worlds and old ones alike: old chunks keep their vanilla blocks,
  and H2–H4 apply when they are touched).

### 4. Designed for, not built in version 1 (the aspirational targets)

These are not in version 1, but must not be blocked by its design:
- long-running flowing water with sub-levels (Realistic Water's way), for flooding and overflow;
- ponds that rise behind a dam to their spill point;
- dams that give way, by material and thickness;
- a river that dries below a correct diversion, over a bounded stretch;
- power from drop height × flow instead of from rapid water alone. Watersheds already turns vanilla wheels on still
  river water: a patch on the wheel's water check reads stored flow data instead of flowing blocks. Hydrology can take
  direction and discharge from Earth's river flow query instead, with no extra save data;
- sluices and canal locks.

[0022](0022-river-damming-and-diversion.md) describes them. Requirements that follow from them:
- Each rule is a separate piece of code.
- Lasting state lives either in blocks or in saved records keyed by area.
- Which water is a natural river comes from Earth's read-only river flow query ([0022](0022-river-damming-and-diversion.md)
  part A), not from per-block flags.
- Searches are bounded and run off the main thread.

## Alternatives

- **Rely on Realistic Water and Realistic Rapids.**
  - Rapids removes Earth's rapids.
  - Water leaves flat 1-block faces.
  - Neither mod has a license or source. They patch the same behaviours Hydrology needs, so they are incompatible with
    it rather than companions.
- **Earth places only rapid sources at its rapids and keeps vanilla rules.** Fixes the fading rapids, but not the flat
  faces in player channels, filling, or the later targets.
- **Earth simulates all its river water at generation.** Vintage Story water has no volume, and a source never moves.
  Flow exists only at drops, so a whole-river simulation adds cost and chunk-edge settling without adding flow.
- **Put the rules in Earth.** Against [0003](0003-geography-only.md); it would tie Earth's releases to water mechanics.

## Consequences

- A new project with its own repository, to be created as `C:\VS mods\Hydrology`.
- Earth will require a second mod besides Cylinder World ([0023](0023-world-creation-tab-and-cylinder-world.md)).
- Hydrology cannot be installed together with Realistic Water or Realistic Rapids; it must say so in its description and
  at start-up.
- Taller drops make steep reaches impassable for boats going upstream, as real rapids are. Gentle reaches keep the
  1-block ramps tested with `/earth testcanals`.
- Old worlds: Earth's river layout stays as it is, and Hydrology's rules apply to touched water. Old rapid ramps without
  a rapid source still fade when touched, unless the drop there is 2 or more.

## Open questions

1. *Answered 2026-10-22: allowed, with buckets unable to place sources by default (H6).* **Player-made rapids.**
   Vanilla deliberately forbids them (wheels on bucket-placed water would be free power).
   Options:
   - accept them when the vanilla world setting stops buckets from moving source water;
   - require the edge to be fed by a natural water body (Earth's query) or by a large connected body (bounded count);
   - power by height × flow later (section 4).
2. *Cap answered 2026-10-22: no cap (section 1).* Still open: whether a drop of 2 in a small stream should be rapids.
3. Whether H3's ramp also forms when water spills onto a dry floor one block lower, or only on lower water.
4. *Answered 2026-10-22:* the same as the other mods, GPL-3.0 (Earth, Cylinder World, Sea and Wmap).
5. Whether Hydrology's generation interface should also be offered to other world generators (its rules work on any
   world).

## Implementation

**Step 1 (drops that follow the slope): implemented in 0.16.2 (2026-10-22), banks beside its curtains fixed in
0.16.3**, offline tests and a generated-world check pass, not played in the client.
In Earth's code this is the *pool layout* (where pools end and how far each drop falls), kept apart in name from
Hydrology's water rules.
- `RiverNetwork` takes `poolLayout` (`PoolLayoutVersion` 1). Pool layout 1, in the forward pass:
  - after a pool of at least `MinPool`, the river drops at once to the valley floor;
  - where a lake, the sea or the main river ahead needs it lower (the backward bound), it drops at once to that bound;
  - pool layout 0's "step every `RampLength`" is gone;
  - the level is held above a listed fall also when the fall's lip is exactly at the current position, else the
    valley's own drop landed on the lip;
  - a listed fall is as high as the river must come down there, never below its floor (Iguazu: 5 → 8 blocks, into the
    Paraná).
- Rapids are drops of 2 blocks or more (`Pools.Rapid`). The mark carries into the next reach when the drop sits at a
  reach's end (`RapidIn`).
- `EarthParams.RiverPoolLayout` (proto 73) is stamped on new worlds; older saves keep 0 and their layout. There is no
  upgrade flag for existing worlds yet.
- `RiverAudit` has a new kind, "rapids": a 2+ drop where the pool layout puts one.
- The test harness takes `POOLS=0` for the old layout.

Offline results, 384-high standard world, all 404 496 reaches, compared with the old rules:

| | blocked | uphill | unintended 2+ falls | listed waterfalls | rapids |
|---|---|---|---|---|---|
| pool layout 0 (old) | 101 | 85 | 138 | 14 | — |
| pool layout 1 | 101 | 82 | 123 | 14 | 10 173 |

- The river checks show no water walls and no bare steps at any of the 11 places. All 14 listed waterfalls pass,
  Iguazu now at 8 blocks.
- With `POOLS=0`, the audit and the full suite come out identical to the baseline (timings aside).
- The one failing check, the Lake Geneva shore steps, failed before the change too.
- On the 400 biggest rivers the rapids are mostly 2–3 blocks (198 of drop 2, 106 of 3), with a few of 10 and more.

Generated-world check, 2026-10-22 ([report](../research/river-drops-test-2026-10-22.md)): a dedicated server with
0.16.2, against the same places with 0.16.1. Sites: the Daugava, the Rhine gorge, the Congo rapids and Iguazu.
- The drops come out as laid out, each filled with a ramp, or with falling water over rapid water for 2+.
- Water next to open air is unchanged from 0.16.1.
- A few bank columns beside 2+ drops were 1–2 blocks lower than the water beside them (Rhine 2 of 33, Congo 8 of 81).
  SettleSteps did not raise banks to a curtain's top. Fixed in 0.16.3: with pool layout 1, dry land beside a waterfall's
  curtain is raised to the curtain's top (`SettleSteps(..., curtainBanks)`). The same places generated again have no low
  banks, and the offline checks are unchanged.

Proposed order:
1. **Earth:** drops follow the slope (section 1), tested offline against the river checks (`/earth checkrivers`,
   `RiverAudit`).
2. **Hydrology:** version 1 rules H1–H5 in a new project. Tested on servers with the
   [wateraudit](../research/realistic-water-test-2026-10-22/wateraudit) harness, moved into Hydrology's tests:
   - trenches opened last and dug outward, fresh and salt;
   - 1-wide channels;
   - a channel around a step;
   - drops of 2–3 blocks with a vanilla wheel;
   - filling a drop back in;
   - the bucket case.
3. **Earth:** draws its drops with Hydrology's function and requires `hydrology` (section 3).
4. **Earth:** the river flow query (0022 part A), and Hydrology reading it.

The later targets (section 4) come after that.

## History

- 2026-10-22: Agreed direction from the user (separate mod Hydrology, Earth depends on it, drop rules, filling with
  flattening, drops following the slope first); details proposed by Claude.
- 2026-10-22, later: Watersheds 7.0.5 studied ([report](../research/watersheds-streams-study-2026-10-22.md)): drop heights,
  knickpoint approach and wheel power from flow data added to sections 1 and 4 as proposals.
- 2026-10-22, later: the user answered open questions 1, 2 and 4. Players may make rapids, and buckets cannot place
  sources by default (H6). Drops have no cap, and named waterfalls follow the same rules with their real heights, set up
  right by Earth. The license is GPL-3.0, like the other mods.
- 2026-10-22, later: step 1 implemented and tested offline (see Implementation); named the *pool layout* in Earth's
  code at the user's request (Earth sets pool levels and drop heights, Hydrology decides the water blocks).
  Committed as 0.16.2, not played.
- 2026-10-22, later: 0.16.2 checked in a generated world (see Implementation): drops as laid out, no new water walls,
  a few low banks beside 2+ drops.
- 2026-10-22, later: 0.16.3 raises banks beside 2+ drops to the curtain's top (pool layout 1 only). The generated-world
  check has no low banks any more.
- 2026-10-22, later: after seeing 0.16.3 in game, the user chose rapids up to 4 blocks as shallow rocky chutes, waterfalls
  from 5 with a shallow lip and a plunge pool (section 1b, pool layout 2). Caravels need 3 blocks of water.
- 2026-10-22, later: pool layout 2 implemented in 0.16.4 (`RiverNetwork.PoolLayoutVersion` 2: chutes, lips, plunge
  pools; `TerrainModel` shapes the bed). Offline and generated-world checks:
  - chute beds 1–2 deep;
  - Victoria's lip 1–2 deep (was 7) and its plunge pool 15;
  - all listed waterfalls pass;
  - 5 more 1-block uphill blips on small rivers.

  See [river-drops-test-2026-10-22](../research/river-drops-test-2026-10-22.md). Not played.
- 2026-10-23: correction. The offline audit of 0.16.4 recorded above (87 uphill, 119 falls) was from a run with chute
  steps 2 apart. The committed 0.16.4 gives 93 uphill, 125 unintended falls and 4 016 drops of 2+, and Kaieteur's curtain
  there is 47 blocks (a forced 9-block drop above the 38-block lip).
- 2026-10-23: pool layout 3 in 0.16.5 (no chute rocks, bed eased over 8 blocks across reach ends, forced drops held above
  listed falls). Server: 4+ block bed jumps Rhine gorge 60 → 0, Niagara 89 → 24, Victoria 84 → 58, Congo 304 → 291 (the
  rest is an older lake/river seam). Offline: all waterfalls pass. Not played.
- 2026-10-23: pool layout 4 in 0.16.6 (lake rims). Niagara in a 1M world (server): lake water above dry land 65 → 0,
  lake edge against the lower river 131 → 61 columns. At 256k: 62 → 55, nothing else changed. Offline: the audit and
  suite change only at Niagara, and lake walls are 0 at all 12 places at 256k and 9 at 1M where the rims act. See
  [lake-rims-test-2026-10-23](../research/lake-rims-test-2026-10-23.md). Not played.
- 2026-10-23: pool layout 5 in 0.16.8 (sills under every drop, the study's edge, curtain and spill blocks, world asks
  Hydrology for vessels). Server: every upper pool column beside a lower pool has its sill (Congo 1211/1211, was 233);
  ramp cells unchanged. Codex review: two problems fixed (sills only at 1-block steps; lake-only chunks not settled). See
  [drop-shapes-test-2026-10-23](../research/drop-shapes-test-2026-10-23.md). Not played.
- 2026-10-24: 0.16.9, ramps one column per height in pool layout 5. The rule study's Earth test of 0.16.8 with
  Hydrology 0.1.5 found stretched ramps rebuilt on the first update (39–336 cells per site). Server: no flowing cell
  without a neighbour one higher at the four sites (the Congo had 2369). Not played.
- 2026-10-24: 0.16.10, pool layout 6: rapids step by step from the rule study's port of H4's window, applied only to
  remove rapids; spills from ramp height 2; a 20-column settling margin. Hydrology's shape test: Congo chute 116 -> 2,
  Rhine 2 -> 0, the rest unchanged; at a long single Congo step, vanilla water plants on the edge still flip rows to
  ordinary water. Not played.
- 2026-10-24: 0.16.11, pool layout 7. White water grouped into gorge sections (continuous white water is not possible
  with the Congo's fall at this scale), sections 3 deep across the full width. Hydrology 0.1.7 shape test 0 at all sites,
  sill-break test at most 7 cells; audit falls 125 -> 91, uphill 92 -> 78. Not played.
