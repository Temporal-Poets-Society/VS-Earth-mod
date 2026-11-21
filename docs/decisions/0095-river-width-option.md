# 0095: River width on the Earth tab, a multiplier of 1:10's widths

Status: **in Earth 2.2.0** (21 November 2026), built on branch `claude/river-width` (EA-0159), from `claude/terrain-2.2` (2.2.0-rc.3), for new worlds;
merged into `claude/terrain-2.2` for the author's in-game test (test build 2.2.0-rc.4, 18 November 2026); not seen in
game yet. Date: 2026-11-18. Decided by the author:

- Design B's named styles were rejected ("no we don`t have wide as Earth 2.1 - that is wrong for 1:50-1:200. My mind we could
  have some setting which is multiplier for different scales. 1:10 does not needs scaling. but others might").
- Then: "but we have scale presets then slider could be set for every scale at 'recommended' size".
- Rivers under the 12-block navigable floor at coarse scales are accepted.

The rule's details and the recommended values are the maintainer's (D72), from the measurements below. The 1:25 value is
put to the author.

## Context

The author made a 1:100 world on Earth 2.1.4 and found its rivers too wide, and wider than in Earth 1.x (EA-0159). The
study (`C:\VS mods\work\ea-0159\report.md`, disposable) found two causes:

1. **The gameplay floor in blocks** ([0014](0014-river-widths.md), width rules 1, unchanged since 0.14.7).
   - A river is at least `1.5 × clamp(2·Q^0.3, 5, 48)` blocks wide at every scale, its real width (8·√Q m) only where that
     is wider.
   - At 1:10 that floor is about the real width.
   - At 1:100 it always wins, so 1.x was already 3-20 times too wide there.
2. **The water mask's width as a ratio of the drawn width** (channel rules 1-2, [0063](0063-rivers-follow-the-real-channel.md),
   [0069](0069-river-flow-field-canals-and-shore-zones.md), kept by [0074](0074-one-width-along-rivers-no-mask-ponds.md)).
   - The mask's measure over 8·√Q, between 0.5 and 3, multiplies the width the game draws.
   - It was built for 1:10. At 1:100 it multiplies the floor: the Daugava at Riga goes 21 → 45 blocks (4.5 km, against
     about 650 m).

Side arms (courses rules 3-4) add a floor-wide channel each.

The 1.22 main menu runs no mod code, so a choice can't change another field on the screen. The play styles (one per Earth
size) set the Earth tab's values instead ([0053](0053-scales-included-glo90-and-glo90-rivers.md) item 11).

## Decision

**River width** on the Earth tab (`earthRiverWidth`, the Terrain group), a slider from 50 to 1000 % in steps of 25, saved
per world as `EarthParams.RiverWidthMultiplier` (ProtoMember 217, a double; 216 kept in reserve).

**100 % is 1:10's widths.** At a coarser scale, a higher value draws the gameplay floor that many times as wide in metres
as at 1:10.

**Each Earth size's play style sets its recommended value:**

| Earth size | 1:10 | 1:25 | 1:50 | 1:100 | 1:200 |
|---|---|---|---|---|---|
| River width | 100 % | 200 % | 300 % | 500 % | 700 % |
| Floor in blocks (FloorFactor) | ×1 (2.1's) | ×0.8 | ×0.6 | ×0.5 | ×0.35 |

Between the play styles' sizes (a hand-typed width), the value follows the logarithm of the scale; coarser than 1:200 it
grows as √N.

**The rule** (`RiverNetwork`):

- **The floor:** the gameplay floor in blocks × `FloorFactor` = multiplier × 10 ÷ N at 1:N
  (`FloorFactorFor`, N from the ring, to the nearest whole number), never more than the multiplier (1:5 keeps 1:10's
  blocks).
- **The real width is never multiplied.** The flow's half width (`HalfWidth`, which World Map reads) is the scaled floor,
  or the real width where wider.
- **The mask's ratio moves the floor only part of the way.** By `MaskWeight` (`MaskedHalfWidth` / `MeasuredHalfWidth`):
  fully where the floor is 1:10's in blocks or more, not at all at half or less (1:100 and 1:200 at their values), linearly
  between. It always moves the real width (within 0.5-3 times 8·√Q). So a broad lowland river no longer triples an
  exaggerated floor, while 1:10 keeps channel rules 2.
- **Never under one block**, the smallest stream's half width (0.8).
- **A channel no deeper than its half width.** The gameplay depth (4-9 blocks) would make a 4-block-wide river a slot 7
  deep.
- **The ships' least width of river exit channels** (`SeaChannels`, 12 blocks a side; only where a world has no outline
  coasts) shrinks with the floor.
- **Side arms** are reaches of their own and take the same rule. So do the streams layer's networks
  ([0061](0061-small-streams-at-fine-scales.md); `StreamLayer` builds them from the world's `RiverBuildOptions`). Streams
  under 20 m³/s are the smallest width at every multiplier.
- **Saved as 0** when the multiplier gives exactly 1:10's floor in blocks: ×1 at 1:10, ×2.5 at 1:25, ×5 at 1:50, ×10 at
  1:100. Those worlds take 2.1's widths by the code paths as they were, and their params are as 2.2.0-rc.3's. Every older
  save reads 0.
- The world's log line "Earth Map: … river segments …" names the multiplier and the floor factor.

**Why a slider in percent:** the game's `doubleRange` slider needs a "multiplier" key for decimal steps, which the Mod DB's
parser refuses ([0089](0089-world-options-the-mod-db-reads.md)). The `intRange` World height uses passes.

**Why the fade between half and whole:**

- A switch at one factor would jump a lowland river's width as the slider passes it (the Daugava 46 → 18 blocks between
  ×2.5 and ×2 at 1:25).
- A fade over the whole range would keep the mask's widening at 1:100 (the Daugava 15.5 blocks at ×5).
- Over half to whole, the width grows with the slider for nearly every river. Rivers the mask measures at half to 0.6 of
  their usual width, where the floor rules, can narrow by up to 6 % as the slider nears 1:10's floor.

## Alternatives

- *Named styles (Automatic, Wide, Playable, Real; design B as first built, `c38613a`).* Rejected by the author: Wide was
  wrong at 1:50-1:200, and one number per scale is simpler.
- *A multiplier on everything drawn (design C).* Scales the mask's ×3 too, and at 1:10 moves big rivers off their real
  width.
- *Recommended values from √(25 m ÷ metres per block) alone* (2.5 at 1:25, 3.5 at 1:50). With the mask's fade, 1:50 at
  ×3.5 draws the Daugava 20 blocks (1 km) against 15 accepted. 1:25 at ×2.5 is 2.1's widths, the Daugava 46 blocks (1.15
  km). See What is left.

## Implementation

- `src/Core/RiverNetwork.cs`:
  - `WidthMultiplier`, `ReferenceScale`, `FloorFactor`, `MaskWeight`, `MaskFadeFrom`, `ShipFloorFactor`;
  - `ScaleOf`, `FloorFactorFor`, `MaskWeightFor`;
  - `HalfWidth` (the path without a multiplier unchanged), `FloorHalfWidth`, `RealPartHalfWidth`, `MeasuredHalfWidth`;
  - `Depth` (the cap), `MaskedHalfWidth`.
- `src/Core/RiverCoursePreparer.cs`: the measured half widths through `MeasuredHalfWidth` with a multiplier.
- `src/Core/RiverBuildOptions.cs` (`WidthMultiplier`) and `src/Core/SeaChannels.cs` (the ships' least width).
- `src/EarthParams.cs` (217) and `src/EarthParams.Model.cs`.
- `src/EarthWorldOptions.cs` (`RiverWidthKey`, `RecommendedRiverWidthPercent`, `RiverWidthMultiplier`) and
  `src/WorldParamsBuilder.cs`.
- `src/EarthMapSystem.cs` (the log line).
- `worldconfig.json` (the slider; each play style's value), `assets/game/lang/worldconfig-en.json` and
  `docs/wiki/Settings.md`.
- **Other mods:** no contract change. Hydrology's river query (version 3) follows the columns. World Map reads
  `RiverNetwork.HalfWidth(double)` by reflection (`docs/REFLECTION-SURFACE.md`), which carries the scaled floor. The mask's
  measure was never in it: World Map draws the flow's width, at 1:100 under a texel of its chart.

## Checks

Offline, on `claude/river-width` from 2.2.0-rc.3 (`80a21a3f`), 18 November 2026.

The probe builds a new world's terrain model as a server does:
- EarthMapSystem's build, then EarthElevationSystem.AttachServer: dams, lake outlines, river outlines, the streams layer,
  the flow;
- from params made by this build's `WorldParamsBuilder` with the play style's Earth tab;
- on full GLO-90 heights (the player's data store).

The code and output are in `C:\VS mods\work\ea-0159\` (disposable).

**Unchanged where it must be:**

- **Terrain digests** (every `TerrainModel.Column` field over 128 × 128 columns at six places: the Daugava at Riga, the
  Norway gorge at 61.55° N 7.58° E, the Rhine at Lobith, the Esk at Langholm, the Volga at Volgograd and the Danube at
  Vienna), against 2.2.0-rc.3's. Identical:
  - at 1:10 at its value (the streams layer built);
  - with 2.1's multipliers: ×2.5 at 1:25 (the streams layer built), ×5 at 1:50 and ×10 at 1:100.
- **`SavedWorldDigestTests`:** the 12 saved worlds' terrain unchanged.
- **`new-world-params.txt`, written again:**
  - every 1:10 case is as before;
  - the 1:25, 1:50, 1:100 and 1:200 cases changed, all but "rivers off";
  - 15 new cases (50, 200 and 1000 % per style);
  - 1000 % at 1:100 has rc.3's own hash for 1:100.
- **`ReflectionContractTests`:** 90 checks pass.

**Widths** (blocks; the course's channel, `2 × Hit.HalfWidth`, median of 13 transects over about 2 km; in brackets the
water across, which counts the mask's outline water, side arms and lakes it joins). The Saale at Halle is 101 m³/s in the
data.

| River | 1:10 100 % (= 2.1) | 1:25 200 % | 1:25 250 % (= 2.1) | 1:50 300 % | 1:100 500 % | 1:200 700 % | 1:100 50 % | 1:100 200 % |
|---|---|---|---|---|---|---|---|---|
| Daugava at Riga (677 m³/s) | 46.0 (86) | 26.8 (37) | 46.0 (47) | 14.8 (21) | 10.6 (12) | 7.3 (10) | 4.6 (8) | 4.9 (8) |
| Rhine at Lobith (2,355) | 31.1 (31) | 21.2 (21) | 24.1 (26) | 17.7 (17) | 15.4 (15) | 10.6 (11) | 3.1 (3) | 6.2 (7) |
| Danube at Vienna (1,937) | 20.0 (34) | 16.6 (17) | 16.5 (17) | 15.6 (18) | 14.5 (15) | 10.0 (9) | 2.0 (3) | 5.8 (5) |
| Volga at Volgograd (8,220), arms | 71.6 (184) | 35.4 (74) | 45.1 (79) | 27.0 (42) | 22.4 (26) | 15.4 (15) | 6.9 (19) | 9.0 (19) |
| North China Plain, the author's 1:100 spawn river (208) | 12.0 (11) | 10.3 (11) | 11.6 (11) | 8.6 (9) | 7.4 (7) | 5.1 (5) | 1.6 (2) | 3.0 (3) |
| Saale at Halle (101) | 8.9 (9) | 8.0 (9) | 9.0 (9) | 6.8 (7) | 6.0 (6) | 4.1 (5) | 1.6 (1) | 2.4 (3) |
| Gauja at Sigulda (91) | 13.0 (13) | 9.8 (9) | 13.2 (13) | 7.0 (7) | 5.6 (5) | 3.9 (3) | 1.6 (2) | 2.3 (3) |

In metres at 1:25 200 %:
- the Daugava 670 m (real about 650 m);
- the Rhine 530 m (about 350 m);
- the Danube at Vienna 415 m (about 300 m);
- the Saale 200 m (about 60 m).

At 250 % (2.1's), the Daugava is 1.15 km and the Rhine 600 m. At 1:50 350 %: the Daugava 20.4, the Rhine 19.9, the Danube
16.4 blocks.

**Tests:**

- `RiverWaterTests`: 80 checks, 31 new. They cover:
  - the formula without a multiplier, unchanged;
  - the scale, floor factor and mask weight at every Earth size;
  - ×5 at 1:100's widths and the mask's measure;
  - never under one block, and the depth cap;
  - next to ×1 at 1:10, 2.1's widths within 0.5 %;
  - built networks with and without a multiplier, ×1 at 1:10 drawing as 2.1.
- `EarthWorldOptionsTests`: 1,480 checks, 1,502 in the quick suite with its release zip's (the Mod DB's parser takes the
  slider). They cover the value at each scale and the clamps, the int a slider saves, 2.1's multipliers saved as 0, older
  saves, rivers off, each play style's value, and the field table.
- `ReflectionContractTests` and `SavedWorldDigestTests`.
- The quick suite: 40 run, 0 failed (`StreamsTests` 19 among them). EarthMapTests pass.

## What is left

- **1:25's value is the author's to confirm.**
  - 200 % brings broad lowland rivers to about their real width (the Daugava 46 → 27 blocks) and leaves confined ones as
    they were.
  - 250 % keeps 2.1's widths at 1:25 exactly.
- **Not seen in game.** A bench look at 1:100 (Riga, the Volga's arms), 1:50 and 1:25 waits for the author's word.
- **Changing the Earth size on the tab doesn't move the slider:** a player who does sets it again (the tab's description
  says so).
- At 1:100 50 %, rivers are about their real width (1-5 blocks). Banks, valleys and the pond profile (0074) measure in half
  widths and follow; the mask's outline water stays at its real size around the channel.

## History

- 2026-11-17: EA-0159 study (causes, numbers, designs A-C); the author chose design B (named styles).
- 2026-11-18: B built on `claude/river-width` (`c38613a`). The author rejected its styles for one multiplier with
  per-scale presets, rebuilt the same day on 2.2.0-rc.3 (217 redefined; never released).
