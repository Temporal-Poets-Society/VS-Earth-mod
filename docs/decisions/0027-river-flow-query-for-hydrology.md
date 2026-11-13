# 0027: Hydrology reads Earth's river flow query: no canal fill in rivers, rapids by slope, no sills

Status: Agreed (2026-10-25), implemented (Earth 0.16.13, Hydrology 0.1.8); pool layout 9 implemented (Earth 0.16.14-rivers.1, Hydrology 0.1.10), installed 2026-10-26 (Earth 0.16.14-playtest.2, Hydrology 0.1.10), not played | Date: 2026-10-25 | Decided by: the user (2026-10-25)

## Context

The user played Earth 0.16.11 (pool layout 7, in the 0.16.12-playtest.1 build) with Hydrology 0.1.7 at the Congo
rapids (-5.60, 13.59):

> now rapids looks patch from top with rapids and still water between. Flooding does not happen when remove blocks.
> But now rapids have 1 block thick walls that does not look right.

The user asked to think more broadly, and later: "have the rule study check flow-query rules in the simulator (no vessels
in natural rivers, rapids by average slope, sills removed), then write it up as a decision before building."

Two limits of the block-only approach (layouts 5–7, [0024](0024-hydrology-water-mod.md)):
- **Patchy rapids.** A river of whole-block steps can only show white water where it steps down. Each block of fall
  gives at most 5–6 columns of it. The Congo at 256k falls about 1 block per 35–46 columns, so its rapids are strips.
- **Walls.** The 1-block walls are the sills under every step. They exist because Hydrology's communicating vessels
  (deep canals fill layer by layer) cannot tell a broken sill from the front of a dug canal. Every local rule that told
  them apart also stopped real canals filling (rule study, 2026-10-24).

The comparison ([river-mods-comparison-2026-10-25](../research/river-mods-comparison-2026-10-25.md)) found how other
mods separate a river's look and push from its blocks:
- **Watersheds:** rapids where the average slope is steep; a push vector per block for boats and wheels.
- **Rivers:** flat sea-level rivers with a stored flow field for the water shader, boats and wheels.

[0022](0022-river-damming-and-diversion.md) part A already proposes a read-only river flow query in Earth. 0024 section 4
says which water is a natural river should come from that query, not from per-block flags.

**The simulator** (Hydrology `docs/flowquery-2026-10-25.md`, commits `dd8627b` and `7e3e5ef`, not pushed; recommended
rules plus 0.1.7, with the query as a per-column stub, five kinds of reach):
- **With the query rules and no sills:**
  - first update 0 cells and 0 visible labels; 0 of 150 pokes change anything;
  - 0 floods from single breaks or from 2-deep holes;
  - the whole surface of rapids columns is rapid;
  - canals 2, 3 and 6 wide, 1–3 deep, off a lake and off a river, fill to 100 % as in 0.1.7.
- **The same reaches with sills and 0.1.7:** 2-deep holes flood 200–694 cells in tapered reaches, and only 0–27 % of the
  surface is rapid.
- **Older scenes without sills, without the query:** step, chute, fall and Victoria are unsafe. Breaks flood up to 132
  cells and pokes don't settle. Sills are what hold the steps today.

## Decision

The user's words (2026-10-25): "Agree 0027: build the Earth flow query and pool layout 8, and have the study session build the
query-aware Hydrology; test on a server world and with reviews before installing."

### Earth: the river flow query (0022 part A, narrowed for Hydrology)

Earth answers per column, server side, a pure function of the world's data and parameters (nothing saved):
- `River`: natural river water as generated. Banks, exit channels, dug canals and ports are not river; lakes and the
  sea are not river either.
- `Rapids`: the reach's average slope is at or above a threshold (the study used 1 block of fall per 50 columns). It is
  constant along a reach, never a sliding window: a window put a rapids strip into the middle of a calm pool.
- `Slope` and `Discharge`: for water wheels only.
- Hydrology reads it about 1.3–1.8 times per liquid update, so it must be O(1): for example a per-chunk-column cache
  filled when the chunk generates or first queried.
- A query version Hydrology can check, so each side knows the other supports it.

### Earth: pool layout 8 (new worlds only)

- Steps where the valley falls, as in pool layout 3 (no chute spacing, no gorge sections), in the Hydrology shape:
  6..1 ramps, spills from ramp height 2 where steps are 5 or fewer apart.
- **No sills:** step edges are as deep as their pools.
- **Rapid marks:** in a rapids column every top source is rapid (labelled, `rapidwater-<dir>-7` at a step edge), and so
  are the ramps. A ramp follows the step edge above it, so it is rapid when that edge's column is rapids, even over a
  calm pool. The first step edge of a reach in a calm column stays ordinary unless the existing rapid rule says rapid.
- Earth draws layout 8 only when the installed Hydrology supports the query version. Otherwise it keeps layout 7,
  because sill-less steps are unsafe without the query.

### Hydrology (a query-aware version)

- **No vessels in a river:** the vessels case does not fill a cell whose column the query calls river. Outside rivers it
  works as in 0.1.7 (depth and containment).
- **Rapids by slope:** in a rapids column, a source with no water of its liquid above it becomes rapid, and H4 never
  turns it back. Flowing water there is rapid too. Outside rapids columns H4 is unchanged.
- **Wheels:** a postfix on the vanilla wheel's flow-rate method takes power from slope × discharge in a river column
  (the method name is still to be checked against the game's DLL).
- **Without the query** (vanilla worlds, older Earth): 0.1.7's rules, unchanged.

## Alternatives

- **Keep layout 7.** Floods are solved, but the rapids stay patchy and the sills show as walls.
- **2-deep sections or deeper sills.** These don't remove the walls or the patchiness, and the study found 2-deep holes
  still flood.
- **A Rivers-style flow shader only.** Client work. It doesn't fix the walls or canal fill, and it doesn't give white
  water as blocks. It could come later from the same query.
- **A rapids threshold by sliding window.** Rejected by the simulator (rapids strips inside calm pools).

## Consequences

- Whole steep reaches render as rapid water and push boats and swimmers along their full length. Caravels can pass
  where pools are 3+ deep. Rapids are then a look and a current, not a depth barrier.
- The query's `River` flag must be exact. A river column marked "not river" falls back to 0.1.7's protection, and with
  no sills its steps can then flood.
- A dam makes no pond yet (that is 0022 part B). After a dam is removed, the gap refills to the same level.
- Earth and Hydrology gain an interface: a query version check in both directions.
- The game's own sources: the wheel method name, and how vanilla labels rapid sources under player edits, still need
  checking on a server.

## Open questions

1. The rapids threshold: 1 block per 50 columns at 256k. Does it hold at 1M (4× the distances, so 4× fewer steps per
   column)? It is probably better expressed in real metres per km.
2. Boat push: Sea or Hansa may already push boats; who owns the current?
3. Wheel power scale from slope × discharge.
4. Whether layouts 5–7 stay supported for existing worlds (yes: saves keep their layout, decision 0002).

## Implementation

1. **Earth 0.16.13** (pool layout 8):
   - `EarthMapSystem.RiverQueryVersion` (1 for layout 8 worlds, else 0) and `RiverAt(x, z, out rapids, out slope,
     out discharge)`. `River` and `Rapids` are the terrain column's own flags (`Column.River`, `Column.RapidsReach`), so
     banks, exit channels, sea channels and canals are not river. Slope and discharge come from the nearest reach at the
     terrain's source position (a companion's terrain expansion moves it).
   - Rapids reach: the real valley falls `RapidsSlope` = 0.5 m/km or more, averaged over `RapidsWindow` = 100 blocks
     (times the world's size scale) each way along the main river from the reach's middle. This replaces the study's
     "1 block per 50 columns", which depended on the world size (open question 1).
   - Steps as layout 6, without sills. In a rapids column every top source is `rapidwater-still-7`; step edges and
     ramps are rapid where the column is rapids or the layout 6 rule says so.
   - New worlds get layout 8 only when the installed Hydrology reports `EarthRiverQueryVersion` 1 or more (read by
     reflection); otherwise layout 7. Layout 8 worlds ask for vessels under a new key,
     `hydrologyFillVesselsWithRiverQuery`, so a Hydrology older than 0.1.8 never fills vessels in sill-less steps.
   - Offline, share of a river's columns in rapids reaches (256k): Congo 100 %, Victoria 100 %, Three Gorges 93 %,
     Aare 93 %, Niagara 82 %, Daugava at Pļaviņas 17 %, Rhine gorge 13 %; Danube, Seine, Nile and the Rhine delta 0.
     Audit equal to layout 6; layout 7 numbers unchanged; RiverIndexTests pass.
2. **Hydrology 0.1.8** (`953684d`, by the rule study): the query reader (mod system and reflection, no hard dependency,
   answers cached per column), QueryNoVessels, QueryRapids and the H8 wheel postfix on
   `BEBehaviorMPWaterWheel.CheckWater` (flow up to 3 times faster from slope and discharge). Vanilla-world labs unchanged.
3. **Server tests on an Earth 0.16.13 world** (study): shape test 0 first-update changes at 7 sites; breaks change only
   the broken cell; canals fill to 100 %; 9,342 vessel fills refused in rivers; tick time unchanged.
4. **Reviews** (independent Claude reviewers; the Codex CLI was not installed): RiverAt under a companion's projection,
   reach lengths without falls, the vessels key with an older Hydrology, sea channels clearing RapidsReach. All fixed
   before the server tests.
5. **Open:** in some test runs, edits at three Congo spots were undone about 30 s later. The study's swap test found it
   intermittent with the same builds (one run kept the edits, another lost them), so it does not follow either mod's
   version. The saved world shows a real revert (canal cells back to soil, broken beds back to muddy gravel), with no
   chunk generation, load or unload of those columns. The Congo sites alone never reverted (4 of 4 runs); only the
   full 13-site runs did (3 of 7). Hydrology never places solids, so the study suspects an engine-side chunk state
   race under many kept-loaded far loads (Hydrology `96304fb`, probe mode). Follow-up (Hydrology `23a3126`,
   `docs/congo-revert-2026-10-25.md`): baseline 3 of 11 runs reverted; Congo sites loaded last 0 of 4; without
   Cylinder World's runtime (a test stand-in) 1 of 4; idle chunks packed after 1.5 s 3 of 8, also at other sites;
   no chunk packing during the run 0 of 4. No chunk event of any kind in the revert window, and nothing was marked
   dirty. Best guess, unconfirmed: the engine's chunk compression committing an older copy of an idle chunk. Not
   caused by Earth or Hydrology. Players could meet it rarely (chunks are not packed near a player). Reproduced
   without mods (Hydrology `7fdaf72`): 4 of 13 vanilla runs with `UncompressedChunkTTL` 1500, 0 of 20 at the
   default. Cause found in 1.22.7's code: a write between the compression thread's pack and the main thread's
   later commit keeps the older packed copy, which is committed once the chunk is idle again. Draft upstream report,
   Hydrology `docs/upstream/chunk-pack-commit-lost-edits.md`; posted by the user as
   [anegostudios/VintageStory-Issues#10177](https://github.com/anegostudios/VintageStory-Issues/issues/10177) (2026-10-25).
   Hydrology 0.1.9 (`ef044ff`) works around it with a patch to the engine's pack commit (config `ChunkPackFix`, on
   for game 1.22.x): 0 of 17 Earth runs lost edits with it (4 of 15 without), 0 of 33 vanilla runs at TTL 1500 (5 of
   21 without); other results and tick time unchanged. Not installed.

## Pool layout 9 (the play-test's river issues, 2026-10-25)

Status: implemented (Earth 0.16.14-rivers.1, Hydrology 0.1.10), server-tested; installed 2026-10-26 (Earth 0.16.14-playtest.2 with Hydrology 0.1.10); played 2026-10-26 (issues #12 and #13, which layout 10 addresses).

The user played layout 8 (0.16.14-playtest.1 with Hydrology 0.1.9, a 512k world) and reported:

> #3 River here looks strange with rapids in two directions. (the Ticino leaving Lake Maggiore, 45.71°N 8.66°E)
> #4 rapids have breaks where they should not have and I would expect no jumps in water height. Same issue on Congo
> rapids that also missing rapid water in betweens. (the Ticino at Bellinzona, 46.17°N 8.96°E)

Read from a copy of the save (`wateraudit` probe) and reproduced exactly in a new server world of the same seed and size:
- **#4, the faces:** the layout waits MinPool blocks (64 at 512k), then takes the valley's whole fall as a chute of
  1-block steps 3 apart. Each ramp (7, 6, 5, 4) is cut off by the next step, whose ramp starts at 6 a block lower: a
  vertical water face of 5/8-7/8 block at every step (Bellinzona 19 faces over half a block, the Congo 134).
- **#4, "missing rapid water in between":** between the chutes the pools are 40-60 blocks of `rapidwater-still-7`. A
  still-labelled rapid source has no flow: it looks calm and has no push (vanilla's push vectors are per flow label).
- **#3, two directions:** the river beside a lake took the lake's level column by column (`NearLakeTop`, from the lake
  data's blurred edge). Leaving Lake Maggiore at 198, the Ticino's course steps to 197; the lake data reach ~40 blocks
  further down one side, so for 30 blocks the river was 198 on its west half and 197 on its east half, with its step
  running along the river and then turning across it. No pool level along the course rises (the profile and the world
  audit agree); the fault was across the river.

Layout 9 (new worlds only, when the installed Hydrology reports query version 2):
- **Steps** (`RiverNetwork`): one block at a time as soon as the valley floor is a block lower, at least `StepSpacing` =
  7 blocks after the last step, so each ramp (6..1) ends where the next step's edge begins and the surface falls an
  eighth of a block per block. Where the valley falls faster the water lags it and the gap shrinks (`StepGap`: lag 2 →
  5, 3 → 4, more → 3); a lag of more than 4 goes down at once (a curtain, as before). A floor of 6 had no spills but
  340 drops of 2+ in a 512k world instead of 121, so the ladder stayed.
- **Lake level** (`TerrainModel.ApplyRiver`): decided at the river's centre line (the nearest point of its course), so
  the whole width changes level at one place; only towards the reach's own end levels (up to the lake only where the
  reach starts at or above it, down only where it ends at or below it).
- **Flow labels** (`EarthWorldGen.PlaceRamps`): a rapids reach's top sources are `rapidwater-<flow>-7` (the river's
  direction), not `rapidwater-still-7`.
- **Interface:** `RiverQueryVersion` is 2 in layout 9 worlds, and `RiverFlowAt(x, z)` gives the river's flow (0..7, n to
  nw, -1 off the river). Hydrology 0.1.10 declares `EarthRiverQueryVersion` 2: it relabels a rapids reach's surface
  sources with that flow whenever vanilla would relabel them (vanilla labels a source by the levels around it: "still"
  in a pool, toward a dug hole). Earth draws layout 9 only with it; with Hydrology 0.1.8-0.1.9 new worlds stay layout 8.

Results (512k, seed 1660372859, the user's world settings; layout 8 = the installed builds; Hydrology report
`docs/server-test-0.1.10.md`):

| | Layout 8 | Layout 9 |
|---|---|---|
| Bellinzona (#4): faces over 0.55 block in the river water / largest | 19 / 0.88 | 0 / 0.38 |
| Sesto Calende (#3): faces / river at two levels side by side | 10 / yes | 0 / no |
| Congo (-5.6°, 13.6°): faces in rapid water | 134 | 34 (spills where steps are 5 or 4 apart) |
| Rapid surface sources still-labelled (Bellinzona, Congo) | 740, 7671 | 0, 0 |
| Steps closer than 7 along the Bellinzona-Lake Maggiore profile / along 4000 blocks of the Congo | 7 of 16 / 13 of 35 | 0 of 15 / 1 of 41 |
| World audit (512k): drops of 2+ where the layout puts one; uphill spots; other falls of 2+ | 4919; 79; 106 | 121; 72; 113 |

Layouts 0, 7 and 8 are unchanged (the offline areas at the three places and the Congo profile are byte-identical).

Not verified: the client's view (how a flow-labelled source and the remaining spill cells look, boats and swimmers
pushed along whole reaches), and wheels on a layout 9 river.

## Pool layout 10 (the play-test of layout 9, 2026-10-26)

Status: implemented (Earth 0.16.14-rivers.2, Hydrology 0.1.11), server-tested; installed 2026-10-26 (Earth 0.16.14-rivers.2 on branch playtest/2026-10-26, Hydrology 0.1.11), not played.

The user played layout 9 (0.16.14-playtest.2 with Hydrology 0.1.10) at the Congo rapids (5.53°S 13.59°E):

> Congo rapids looks quite good but there is a gap with still water. And very strange: the water looks to be just
> flowing, but as soon as I spend time next to it, it turns into rapids and the animation kind of pulses from rapid
> whitewater to regular, but the sound is all the time rapids. If I move away then it goes back to normal flowing water.

Read from a copy of the save (`wateraudit` probe) and in new worlds with the same seed:
- **No still blocks.** Every surface source of the reach is `rapidwater-s-7`; the "still" bands are the flat pools
  between ramps, which look calm from a distance.
- **The wall of water:** spills, cells of falling rapid water one block above a ramp. SettleSteps puts them where a
  ramp of 2 or more meets the next step. On the centre line the steps are 8 or more apart, but on the inside of a wide
  river's bend two steps' lines come within a few blocks of each other. A 129-block square at the spot has 81 faces
  over half a block, every one at a spill (66 spill columns).
- **The pulse is the client's foam, not the blocks.**
  - The save, loaded and watched for 5 minutes with its chunks kept loaded, changed 0 of 6 561 columns, and Hydrology
    relabelled nothing.
  - On the client, vanilla's flowing water (every flow label but still and falling) spawns foam particles within 37
    blocks of the player, fading in over 7 seconds of ticking. Rapid water's foam is about 24 particles alive per
    block.
  - At the spot the scan holds 4 405 rapid sources and 1 446 flowing rapid cells: about 140 000 particles against the
    async pool's 80 000. The pool runs full, and the foam comes and goes in waves.
  - Flowing rapid water's ambient sound is "rapids" whatever the particles do, so the sound never changes.
  - Fixed in Hydrology 0.1.11 (client rule C1).

Layout 10 (new worlds only, with Hydrology 0.1.10 or later):
- **Steps led by the valley ahead** (`RiverNetwork`, `StepLead` 3): a backward pass gives the level the valley
  downstream asks for, reached at one step per `StepSpacing`. Where the valley ahead falls faster than a block per 7,
  the river starts down early, at most 3 below the valley here, instead of lagging behind and taking the fall in
  steps 5, 4 or 3 apart. Listed falls are not anticipated. A lead of 6 cut close steps only from 3 610 to 2 745, and
  lets rivers cut deeper, so 3 stayed.
- **No spills and even ramps** (`TerrainModel.SettleSteps`, `noSpills`): where the next step comes within a ramp's
  length, the ramp falls evenly to it. The surface goes from 7/8 above the upper water to 7/8 above the lower water in
  equal parts, and the lower edge carries no ramp.
- **The trade-off:** that shape is not vanilla's. Where the water is woken (a player digging or building nearby),
  vanilla spreads the ramp again one level per block and the spill forms there, as in layout 9.
  - At the Congo spot the server's wake-all of a 49-block square changed 112 water cells and ended with 41 faces.
    Layout 9 had 0 changes and 81 faces. Without the even ramps it is 27 cells and 53 faces.
  - Water nobody touches stays as generated: the 5-minute watch above.

Results (512k, new worlds, seed 1757651272, the play-test's world settings, the real seam):

| | Layout 9 | Layout 10 |
|---|---|---|
| Congo spot (129-block square): faces over 0.55 block, as generated | 81 (66 spill columns) | 15 (0 spill columns) |
| The same after a wake-all and a poke | 81 | 41 |
| Congo profile from Kinshasa (4 000 blocks): steps closer than 7 / smallest gap | 1 / 4 | 0 / 8 |
| World: steps closer than 7 to the one before (layout 8: 21 198 of 95 947) | 6 110 of 121 415 | 3 610 of 121 648 |
| World audit: drops of 2+ at the layout's places; uphill spots; other falls of 2+ | 117; 72; 117 | 90; 63; 122 |
| Ticino at Bellinzona and Sesto: faces over 0.3 (offline) | 0, 0 | 0, 0 |
| Mean tick after 30 s | 47.19 ms | 47.14 ms |

Layouts 0, 7, 8 and 9 are byte-identical offline (the user's world keeps layout 9). Not verified: the client's view of
layout 10, and of Hydrology 0.1.11's foam.

## History

- 2026-10-25: proposed after the user's layout 7 play report and the mod comparison. The rule study's simulator supports
  it (Hydrology `docs/flowquery-2026-10-25.md`).
- 2026-10-25: agreed by the user. Earth 0.16.13 and Hydrology 0.1.8 built, reviewed and server-tested; not installed.
- 2026-10-25: pool layout 9 for the play-test's issues #3 and #4 (Earth 0.16.14-rivers.1, Hydrology 0.1.10, query
  version 2); server-tested, not installed.
- 2026-10-26: pool layout 10 for the play-test of layout 9 at the Congo (Earth 0.16.14-rivers.2), with Hydrology
  0.1.11's client foam rule; server-tested, not installed.
- 2026-11-08/09 (Earth 2.0.0-rc.2, with Hydrology 0.6.2's review): query version stays 3. Its rapids answer (pool layout
  11) now comes from the columns as the generator settles and draws them (ChunkSettler, the ramp word and source
  kind it writes), not the model's raw ramps, which ran up to 4 columns past the drawn ramp on diagonal steps (Congo at
  1:10: 139 calm sources turned rapid on first load, now 0; an offline probe over 375,282 river columns agrees
  everywhere), and an answer is kept only when its rebuild read no built-in fallback height. New method
  `RiverSpanAt(x, z, out bed, out waterTop)`: a natural river column's bed and water surface, so Hydrology applies its
  river rules only in the river's own water. Earth requires Hydrology 0.6.2-rc.1 or newer with query version 3
  (decision 0038's minimum raised; the author: "old save does not matter with this release").
- 2026-11-09 (Earth 2.0.0-rc.3, Hydrology's second review round): query version stays 3. Where `RiverAt` can't have the
  generator's answer yet (a chunk's drawn rapid sources not ready, or a GLO-90 tile its columns read not on this machine
  yet) it throws `EarthMap.Core.RiverQueryNotReadyException` instead of answering the model's rough guess, which
  Hydrology kept for good; every accepted Hydrology (0.6.2-rc.1 on) catches it, keeps nothing and asks again. A chunk is
  settled only for a column that can be drawn rapid (a rapids reach's, or an upper edge of a drop), and one whose
  settling failed or was not ready waits 10 s before it is asked again. `RiverSpanAt` gives no span where a river column
  holds no water (a rock breaking a chute's surface). Implementation: `src/Core/RiverQuery.cs`.
