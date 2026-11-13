# 0052: No walls beside raised rivers or along river exit channels

Date: 4 November 2026. Status: **not taken** (closed 8 November 2026 for the Earth 2.0.0 release candidate, when the
author asked for the leftover branches to be "addressed and cleaned up"). It was implemented on `claude/terrain-walls`
(cut from `release/1.0` `bbd00c0`), never agreed, played, merged or released. Why it was closed rather than ported:
- new worlds stand on GLO-90 land heights (0051, 0053), where the reported raised-river wall hardly arises even with the
  old rules (Sion's worst step 9 blocks, not 24; see below);
- rivers at fine scales now follow the real channel with measured widths and dredged courses (0063, channel rules 1),
  which replaced the carving this branch changed, so its code no longer applies to `RiverNetwork` or `SeaChannels`;
- the exit-channel part was left as an open proposal on GLO-90 even here.

What remains of it: the confluence valleys (Chur, Bolzano and Koblenz 8-9 blocks on GLO-90) are open issues to re-measure
on channel rules 1 worlds, not this branch's code. The branches `claude/terrain-walls` and `walls-glo90-test` stay
as they are for reference.

At the author's request it was also tested with the GLO-90 land heights (0051, proposed, not agreed). That test ran
offline and on headless servers, on the test branch `walls-glo90-test`, which merges both branches and is not to be
merged anywhere. See [On GLO-90 land heights](#on-glo-90-land-heights-0051).
- **Valleys.** On GLO-90, the reported raised-river wall hardly arises, even with the old rules: Sion's worst step is 9,
  not 24. The confluence part of rules 2 still takes Chur, Bolzano and Koblenz from 10–22 blocks to 8–9 or none.
- **Channels.** The exit channels' low-land cut reads GLO-90's heights. That changes which columns are sea along 137 of
  983 exit channels, and older channel rules do the same. A proposal to read the built-in heights there is open.

## Report

A user report passed on by the author ("user report bad terrain generation at given locations", 4 November), with two
screenshots from a world of 512,000 blocks:

- **Sion, Rhône valley** (5632, 194, 2958; 46.2562° N 7.3295° E). A straight vertical wall of soil and stone, 8 to 10
  blocks high, along the valley. A raised shelf lies on the river's side and a trench on the slope's side.
- **Westerschelde, Zeeland** (1860, 124, −2913; 51.4183° N 4.0143° E). Under a sand beach, a vertical underwater wall
  drops to a flat floor 6 blocks deep. A straight channel runs to the horizon. The minimap shows a hook-shaped strip of
  land.

## Causes

Measured offline with the reporter's world geometry (512,000 blocks, height 512, sea level 110) and the default Earth
tab.

1. **Sion: the valley's lower rim.** `TerrainModel.ApplyRiver` lowers ground above a river's water across its valley,
   easing back to the natural ground. Ground below the river's water level (a river on a fan or terrace above its
   valley floor) was handled differently. It was raised to the water level out to a quarter of the valley's easing
   (`f < 0.25`), then dropped back to natural height in one step. The step is as high as the river stands above the
   ground. Around the site, with seed 386217, the river stands at 165–166 over natural ground at 144–156, giving walls
   of 9 to 22 blocks. The worst steps within 400 blocks at Sion are 24 (seed 386217), 7, 15 and 11 (seeds 1, 2, 3).
   Martigny and Grenoble show the same pattern (24 and 32 blocks).
2. **Zeeland: the channel's fan dredged up to the beach.** River exit channels (`SeaChannels`, rules 2) widen 30° per
   side up to `MaxHalfWidth` (268 blocks for the Scheldt). Every water column inside that fan was deepened to the
   channel's full depth (6), right up to the beach. That made the underwater wall and the flat 6-deep floor. With the
   channels switched off, the shore ramps down as everywhere else.
3. **Zeeland: the channel crossed the outlines' land.** The channel's route runs on the 2′ terrain grid. That grid has
   Zuid-Beveland's polders as sea, because they lie a few metres below sea level. The world draws them as land from the
   coast outlines (GSHHG). So the Scheldt's way out led north across Zuid-Beveland to the Oosterschelde, and the cut
   was a straight canal 24 blocks wide. Worldwide, 73 of 980 exit channels had route points on outline land (197
   points).
4. **Not changed: the Scheldt's last reach.** HydroRIVERS ends the Scheldt at 51.38° N 4.03° E, on the outlines'
   south shore of the estuary. Its last reach is a fresh-water river capsule inside the estuary, ringed by its own
   banks: the hook in the minimap. That comes from where the river data and the outlines put the shore. This decision
   does not change it.
5. **Found while measuring: valleys where rivers meet.** A column takes the valley of its nearest river alone
   (`RiverNetwork.Nearest`: the smallest distance minus half width among rivers whose reach covers it). At Chur the
   Plessur (q 54, reach 35 blocks) and the Rhine (q 107, reach about 53) meet. Where the Plessur's reach ends, the next
   column is the Rhine's, at 70% of the Rhine's valley easing: one column stays at its own height, the next is lowered 15.
   The worst steps within 400 blocks of Chur are 22–26 blocks with rules 1 (up to 33 before), and smaller ones appear
   at Koblenz (Rhine and Moselle, 7–8), Bolzano (12), Chongqing (9) and Innsbruck (9).

## Decision

Proposed and implemented, pending the author's agreement:

1. **River valley rules 1** (`TerrainModel.RiverValleyRules`). Ground below a river's water rises to the water at the
   bank and eases back to its own height across the valley. This is the same easing as ground above the river, so
   there is no step at a quarter of the valley.
2. **River valley rules 2**, which also include rules 1. The author asked for this follow-up on 4 November. A column in
   the nearest river's valley that other rivers' valleys reach too takes all of them, one per reach (its closest
   piece).
   - **Cut, then raise.** First every valley cuts the ground down, and the lowest cut stands. Then every river standing
     above the result raises it towards its own level, and the highest raise stands.
   - **No step.** Each cut and raise fades to nothing at its valley's edge, and the min and max don't depend on which
     river is nearest. So there is no step where the nearest river changes, and no reach counts twice.
   - **One river.** With one river the result is exactly rules 1.
   - **Like the nearest river.** Other rivers follow its rules: their level beside a lake (pool layout 9), no valley for
     a mouth's prolongation or a dry river.
   - **Unchanged:** banks and water still come from the nearest river alone.
   - **New worlds** store `RiverValleyRules` 2 (`TerrainModel.RiverValleyVersion`).
3. **Channel rules 3** (`SeaChannels.Version`):
   - **The route keeps to the outlines' water.** In worlds with coast outlines, a grid sea cell is land for the route
     when the outlines (big land or islands) have at least three of five points in it as land: the centre and four
     points 0.3 cells out. That land can still be crossed like a bar, at the usual cost and within the usual count of
     land cells. Results are cached per build.
   - **Fallback.** If a river has no way out under that test, it keeps the grid's route as in rules 2. So no river
     loses an exit it had before.
   - **The opening shelves.** Outside the river's own width, the fan deepens only as far as the shore's own profile
     lets the bed go down there (`Column.Shelf`): about 1 block at the shore, full depth where the shore no longer
     holds the bed up. Over the river's width, the lane keeps its full depth all the way to open water.
   - **The lane has sloping sides.** Through low land (bars, spits, beaches) the lane's sides rise one block per
     block out. The river's own banks beside the lane are still not cut, as in rules 2.
4. **New worlds only (0002).**
   - New worlds store `EarthParams.RiverValleyRules` (ProtoMember 94) = 2 and `RiverChannels` = 3. ProtoMember 93 is
     left for the tall worlds' rock floor (0050), which is in progress on `claude/tall-1to10`.
   - Older worlds read 0 and their saved channel version, and generate exactly as before.
   - `UpgradeExistingWorldRiverChannels` now also moves rules 1 or 2 to 3 for chunks generated from then on. Its log
     names the old and new rules.
   - There is no upgrade flag for river valleys.
   - **The reporter's world.** Chunks already generated keep their terrain, so the two spots stay as they are. A new
     world gets the fix.

## Implementation

- `src/Core/TerrainModel.cs`:
  - the `RiverValleyRules` field and `RiverValleyVersion`;
  - `Column.Shelf`, set where the sea or a lake is shaped: grid shores, coast outlines and islands;
  - the eased rule for low ground in `ApplyRiver`;
  - `CombinedValley` (rules 2), and `RiverLevel`, the river's level beside a lake, factored out of `ApplyRiver` so the
    nearest and the other rivers use the same rule.
- `src/Core/RiverNetwork.cs`:
  - `Others`, the other rivers reaching a column;
  - `HitAt`, `Nearest`'s hit-building part, split out unchanged so `Others` builds the same hits;
  - `Hit.LineReach`.
- `src/Core/SeaChannels.cs`:
  - `Version` 3 and `RouteCell` (the outline test);
  - the outline-aware `Route` and the fallback in `Build`;
  - the shelved opening and the lane with sloping sides in `Apply`.
  - Rules 1 and 2 take the old code paths.
- `src/Core/SearchBuffers.cs`: `OutlineLand`, the per-build cache of the outline test.
- `src/EarthParams.cs` (the new field and `BuildModel`) and `src/EarthMapSystem.cs` (new worlds, the upgrade flag's log).
- `docs/research/terrain-walls-2026-11-04/`: the offline probe (`WallsProbe.cs`) and the Zeeland renders.
- `tests/EarthMapTests`:
  - the new-world test model has the current valley rules (`VALLEY=n` overrides);
  - `CheckConfluenceValleys` (also alone, as mode `valleys`, which lists the steps): at Chur, Koblenz and Bolzano, no
    step the valleys make where the nearest river changes from one column to the next may be over 6 blocks or higher
    than with the nearest river's valley alone. In the 256k test world it measures Chur 16 → 4, Koblenz 11 → 3 and
    Bolzano 17 → 5. The remaining 4–5 blocks lie between one river's short reaches on steep slopes, where its own
    easing steepens the mountainside.

## Verification

All at 512,000 blocks, height 512, sea level 110, the Earth tab's defaults, offline with the terrain model (no game).
Up to [On GLO-90 land heights](#on-glo-90-land-heights-0051), all on the built-in 2′ heights.

- **Old rules unchanged.** The new code with rules 2/0, and with 1/0, gives column fingerprints identical to
  `release/1.0` at Sion, Zeeland, the Rhine–Maas mouth, the Meghna, the Tagus, the Amazon, the Mississippi, the Rhône
  delta, the Thames and the Yukon. Each fingerprint covers Top, water, beach, river and ice fields within 500 blocks.
- **Valley walls.** Man-made land steps of 4 or more beside rivers within 400 blocks: the worst before → after.

  | Site | Seed 386217 | Seed 1 | Seed 2 | Seed 3 |
  | --- | --- | --- | --- | --- |
  | Sion | 24 → 7 | 7 → 6 | 15 → 7 | 11 → 7 |
  | Martigny | 24 → 9 | 8 → 7 | 15 → 8 | 11 → 8 |
  | Grenoble | 10 → 5 | 32 → 5 | 20 → 5 | 12 → 5 |
  | Innsbruck, Basel, Wachau, Lyon | unchanged | unchanged | unchanged | unchanged |

  The remaining 4–7 block steps are mountainsides that the valley's lowering steepens. They were there before and are
  not changed.
- **Valleys where rivers meet (rules 1 → 2).** The same measure:

  | Site | Seed 386217 | Seed 1 | Seed 2 | Seed 3 |
  | --- | --- | --- | --- | --- |
  | Chur (Rhine, Plessur) | 26 → 7 | 25 → 5 | 22 → 7 | 22 → 6 |
  | Koblenz (Rhine, Moselle) | 8 → none | 7 → none | 7 → none | 7 → none |
  | Innsbruck (Inn, Sill) | 9 → 6 | 6 → 5 | 7 → 6 | 6 → 6 |
  | Martigny | 9 → 7 | 7 → 7 | 8 → 8 | 8 → 7 |
  | Sion, Grenoble | about the same | about the same | about the same | about the same |
  | Basel, Passau, Lyon, Belgrade | none either way | none either way | none either way | none either way |

  The 15 further confluences below were measured at seed 386217:
  - **Steps removed:** Bolzano 12 → 6, Chongqing 9 → none, Khanty-Mansiysk (Ob, Irtysh) 5 → none, Villach and
    Pittsburgh 4 → none.
  - **None either way:** St. Louis, Mainz, Prayagraj, Manaus, Wuhan, Pasco, Valence, Phnom Penh, Khartoum and Koblenz
    on the Aare.

  No site is worse.
- **River water above dry land (rules 1 → 2).** River or lake water beside dry ground lower than the water, within 400
  blocks of eight confluences: the same counts (0 everywhere, Pittsburgh's four 1-block cases in both).
- **Column cost of rules 2.** Within 400 blocks of confluences, making a column takes about 0.3–0.7 µs more:
  - Koblenz 1.3 → 1.7–1.9 µs;
  - Chongqing 1.4 → 2.1 µs;
  - Bolzano 1.4 → 1.6 µs;
  - noise level at Chur, Innsbruck and Sion.

  Elsewhere the cost is one extra candidate pass for valley columns, and nothing away from rivers.
- **Old rules after the `Nearest` split.** The fingerprints for rules 2/0 are again identical to `release/1.0` at
  the ten sites.
- **Zeeland's sea bed.** Steps of 3 or more from a shore to the sea bed within 600 blocks: Zeeland 8,147 → 664,
  Vlissingen 5,728 → 76, Rhine–Maas mouth 4,682 → 836. There are no steps on the bed itself. Most of what remains
  (drops of 7) lies along the Rhine–Maas channel's fallback cut between the Oude Maas and Nieuwe Waterweg banks.
- **The Scheldt's route.** It now leaves westward along the Westerschelde, past Vlissingen, to open water off
  Walcheren. Every point is on the outlines' sea, where before it crossed three cells of Zuid-Beveland. The renders and
  the probe are in the [research note](../research/terrain-walls-2026-11-04/README.md).
- **Worldwide routes.** Channels with route points on outline land: 73 → 31, points 197 → 83. No-way rivers: 1 (the
  same one, 58.0° N 133.6° W). Three rivers keep the grid's route through the fallback: the Meghna (23.08° N 90.62° E),
  the Rhine–Maas (51.77° N 4.54° E) and one in north-east Greenland (80.62° N 23.72° W).
- **Start-up cost.** Building the exit channels takes about 1.8–2.0 s instead of 0.6 s. That is about +1.2 s, once,
  when a coast-outline world loads.
- **EarthMapTests 384** (the new-world test model with valley rules 2 and channel rules 3): the three confluence checks
  pass. Apart from them, the output matches the run with valley rules 0 line for line, timings aside. 2 checks fail,
  the same 2 as `release/1.0`'s code on the same data:
  - "Lake Geneva: no steeper steps than before (6)", a known failure;
  - "icy, seed 2026", where the spawn search picks 66.07° S 101.25° E, south of the check's 60° S.

  Other differences between the two outputs:
  - **Lisbon.** The ship lane across the bar off Lisbon is still 7 deep, but 32 blocks wide instead of 47: the fan
    beside it no longer dredges to full depth.
  - **River depths at two mouths.** At the Rhine in the Netherlands and the Congo, the depth histograms shift slightly
    (about 650 and 300 columns), because river water at sea level near the mouth is no longer dredged beyond the lane.
  - **Timings.** The per-column timings differ in both directions. The new run shared the CPU with the probe runs, so
    they are not comparable.
- **Release build:** the mod compiles without errors. The package step stops on the branch's existing version
  mismatch (Earth 1.0.3-rivers.3 against data pack 1.0.2), which this change does not touch.

### On GLO-90 land heights (0051)

The author asked for these rules to be tested with the GLO-90 land heights before merging. Before the test the
expectations were:
- GLO-90's natural ground has larger steps of its own;
- more ground might lie below a raised river or above its bank top;
- channel rules 3 should not move, since the coast outlines and the 2′ grid stay the same.

Tested 4 November. Neither 0051 nor this decision is agreed.

#### Setup

- **Branch.** `walls-glo90-test` is `claude/terrain-walls` (`a6b1345`) with `claude/glo90-elevation` (`d570834`,
  prototype 1.0.3-glo90.4) merged in (`8af5850`). The only conflict was `EarthParams`, where both members stay:
  `RiverValleyRules` (proto 94) and `FineElevation` (proto 95). The branch is for measuring only.
- **Heights.** The whole converted store (26,475 tiles in `VintagestoryData/ModData/earth/glo90`), with GLO-90 rules 2.
  It is attached after the model is built, as the server does. Nothing was downloaded.
- **World.** As above: 512,000 blocks, height 512, sea level 110, the Earth tab's defaults, seed 386217 unless said.
- **Sites.**
  - Sion and Zeeland: the report's points.
  - Chur (46.85° N 9.53° E), Koblenz (50.36° N 7.60° E) and Bolzano (46.5° N 11.35° E): the test's points.
  - Grenoble (45.19° N 5.72° E) and Martigny (46.10° N 7.07° E). Sion's and Martigny's windows overlap.
- **Tools.** The probe has a `GLO90` switch and three new modes: `levels`, `finediff` and `lanes`. EarthMapTests has
  `GLO90` too. See the [research note](../research/terrain-walls-2026-11-04/README.md).

#### Tests

- `tests/ElevationTests` (100 sample tiles, and the whole source for the summit search): all 21 checks pass.
- EarthMapTests `valleys` (the 256k test world) gives the highest step where the nearest river changes, with the
  nearest river's valley alone → rules 2. All checks pass; on GLO-90, Bolzano is at the check's limit of 6.

  | Site | Built-in | GLO-90 |
  | --- | --- | --- |
  | Chur | 16 → 4 | 23 → 5 |
  | Koblenz | 11 → 3 | 13 → 3 |
  | Bolzano | 17 → 5 | 20 → 6 |

- EarthMapTests 384 on the built-in heights with the merged code: 286 checks pass and the same 2 fail as before (Lake
  Geneva, icy seed 2026).

#### Valley walls

Man-made land steps of 4 or more beside rivers within 400 blocks (probe `walls`): the worst with valley rules 0 → 2.

| Site | Built-in | GLO-90 |
| --- | --- | --- |
| Sion | 24 → 7 | 9 → 9 |
| Martigny | 24 → 7 | 9 → 9 |
| Grenoble | 10 → 5 | 8 → 8 |
| Chur | 26 → 7 | 21 → 8 |
| Bolzano | 12 → 6 | 22 → 9 |
| Koblenz | 8 → none | 10 → none |
| Zeeland | none | none |

- **Seeds 1, 2 and 3.** Built-in, rules 0 → 2:
  - Sion 7, 15, 11 → 6, 7, 7;
  - Martigny 8, 15, 11 → 7, 8, 7;
  - Grenoble 32, 20, 12 → 5, 5, 4;
  - Chur 33, 27, 33 → 5, 7, 6;
  - Bolzano 15, 13, 15 → 8, 7, 6;
  - Koblenz 7 → none at each.

  On GLO-90 every seed gives the table's worst steps, with counts within 10 of each other. The seed now moves only
  GLO-90's 1 m undulation.
- **The raised river's step (cause 1) has little to act on.** Dry valley columns whose natural ground lies below the
  nearest river's water (probe `levels`), and how far below:

  | Site | Built-in | GLO-90 |
  | --- | --- | --- |
  | Sion | 4.1 %, down to 30 blocks | 0.1 %, 1 |
  | Martigny | 7.7 %, 30 | none |
  | Grenoble | 11.3 %, 20 | none |
  | Chur | 8.6 %, 31 | 1.5 %, 7 |
  | Bolzano | 1.2 %, 11 | 2.3 %, 17 |
  | Koblenz | under 0.1 %, 1 | 0.5 %, 8 |

  On GLO-90, the ground beside these rivers lies at or above their water almost everywhere. So the expectation that
  more ground would lie below a raised river was wrong at these sites. Even with the old rules, Sion's worst step on
  GLO-90 is 9, not 24.
- **The confluence part of rules 2 still matters:** Chur 21 → 8, Bolzano 22 → 9 (the one site with more ground below
  the river), Koblenz 10 → none.
- **What remains on GLO-90** (8–9 blocks, against 5–7 on the built-in heights): mountainsides inside one river's
  valley, 0.6–0.8 of the way across its easing.
  - The natural ground there already falls about 5 blocks per column, and the valley's lowering adds 3.
  - Rules 2 does not change them: they are the same with rules 0.
  - They are the same kind as the 4–7 block steps above, steeper because GLO-90's slopes are steeper.
- **GLO-90's own ground is much rougher.** Natural steps of 4 or more at the same river pairs:
  - Sion 1,307 → 31,126 (the highest 6 → 21);
  - Martigny 1,617 → 29,500;
  - Grenoble 348 → 26,200;
  - Chur 642 → 18,896;
  - Bolzano 623 → 30,464 (the highest 30);
  - Koblenz 0 → 19,574.

  Made steps are more numerous too (Sion 2,723 → 4,486 with rules 2), mostly of 4–6 blocks.
- **River water above dry land** (probe `spills`): none at any site, with either rules and either heights.
- **Column cost** (probe `spills`, one thread):
  - No site is slower on GLO-90.
  - Near summits it is faster: Sion 5.8 → 1.3–1.4 µs, Martigny 3.6–4.4 → 1.0 µs. GLO-90 skips the 30″ residual and
    the summit correction, which is probably why.
  - Rules 2 adds 0–0.2 µs, within the noise.

#### Sea bed and exit channels

- **Valley rules don't touch the sea bed:** rules 0 and 2 give the same counts.
- **Shore-to-bed steps of 3 or more** (probe `seabed`, 600 blocks):

  | Site | Built-in | GLO-90 |
  | --- | --- | --- |
  | Zeeland | 664 | 692 |
  | Vlissingen (51.44° N 3.57° E) | 56 | 56 |
  | Rhine–Maas mouth (51.98° N 4.12° E) | 801 | 833 |

  The last two points are this test's own; the 76 and 836 above came from other points.
  - All 664 built-in steps at Zeeland are still there.
  - The 28 new ones are 7-block drops in the Rhine–Maas fallback cut, at 51.94° N 4.16° E and on the Oude Maas at
    51.87° N 4.34° E. There, 13–14 columns that the cut makes sea on the built-in heights stay land at sea level on
    GLO-90. That leaves a small islet in the 7-deep cut.
  - Elsewhere within 600 blocks of Zeeland and Vlissingen, no column changes between sea and land (probe
    `finediff`). The Westerschelde's lane and shelved opening are the same on both heights.
- **Routes** (probe `crossings`) are the same on both heights, since they run on the grid: 31 channels with 83 points
  on outline land, and one river with no way out.

#### What these rules get wrong on GLO-90: the channel cut reads GLO-90's heights

- **The rule.** A channel cuts low land on its way (bars, spits, beaches) where two things hold:
  - the column's height is at most 16 m (`2 × SeaChannels.CutHeight`);
  - its top is at most 4 blocks above the sea.

  Rules 3's sloping lane sides use the same test. On GLO-90, that height is GLO-90's. GLO-90 is a surface model, with
  forest canopy, dunes, dikes and port ground.
- **Worldwide** (probe `lanes`: every exit channel's course, plus the river's half width and the channel's depth on
  each side; 983 channels, 31.0 million columns):
  - In 137 channels, 31,477 lane columns that are sea on the built-in heights are not sea on GLO-90. 29,687 of them
    are dry land.
  - 282 of those columns, in 42 channels, lie on the course's centre line, so those lanes have land in them.
  - In 115 channels, 59,266 columns that are land on the built-in heights become sea where GLO-90 is lower than 16 m.
- **The largest changes:**
  - the Amazon: 2,547 columns, where GLO-90 has 24 m of forest and the grid 6 m;
  - the Meghna: 1,807;
  - two rivers at 63.95° N 93.5° W: 1,219 and 1,146, each with 12 on the centre line and about 3,400 new sea;
  - the Yenisei: 1,024, and 5,137 new sea;
  - the Susquehanna: 890, 11 on the centre line;
  - the Anadyr: 839;
  - the Columbia and the Kuban: 678 each.
- **Not new with rules 3.** Channel rules 2 do the same: 110 channels, 49 of them with land on the centre line (370
  columns). Rules 1 do too: 77 channels, 30 with land on the centre line. Rules 3's sloping sides make more columns
  depend on the test, but leave land on the centre line of fewer lanes (42 against 49).
- **It contradicts 0051's rule** that which columns are land, sea or lake stays the built-in data's.
- **Proposal, not implemented:** decide the channel's low-land test on the built-in heights, so exit channels are the
  same on both height sources. Rules 3 has not been released, so it could take this without a new version. That is
  the author's call.

#### In-game check

Headless servers ran the merged code as a folder mod, with Cylinder World 1.0.4, Hydrology 0.6.2-rivers.7 and the data
packs 1.0.2. Each made a new world like the one above, with a custom spawn at Sion. Earth's self-test then covered 6
chunks around the spawn, Sion, Zeeland and Chur. There were two worlds: one on GLO-90 and one on the built-in heights.
The runs are in `testruns/` (git-ignored).

| Place | GLO-90: terrain = model | Built-in: terrain = model |
| --- | --- | --- |
| Sion | 99.5 % | 99.5 % |
| Zeeland | 99.6 % | 99.6 % |
| Chur | 99.5 % | 99.3 % |
| Spawn | 99.8 % (46.27° N 7.00° E) | 99.8 % (46.08° N 7.30° E) |

- **GLO-90 everywhere it should be.** All sampled dry columns are on GLO-90 (28,679–42,078 per place), within
  0.2–0.3 m of it. No square fell back to the built-in heights. Earth moved the spawn away from Sion differently on
  each world.
- **Sion.** On both worlds: no water seams and no dry blocks below river surfaces.
  - In the four-place runs, 42 % (built-in) and 31 % (GLO-90) of Sion's columns were not yet loaded when it was
    analysed. The built-in run then counted 1,714 dry blocks below river surfaces, and the GLO-90 run 45 water seams
    against unloaded neighbours.
  - Run alone, all 173,056 columns loaded, with none of either. These Sion figures are from those runs. Their build
    had one extra self-test log line, which is not committed.
- **Zeeland.** 94 seams of salt water next to fresh water at y 109 on both worlds: the Scheldt's fresh-water last reach
  (cause 4).
- **Chur.** No seams and no dry blocks on both worlds.
- **Logs.** No errors or exceptions on either server.

Not checked in game: the probe's step counts (the self-test doesn't measure walls), and what the worlds store as
`RiverValleyRules`.

#### Still open

- A played new-world test at Sion, Zeeland and Chur (the author's).
- The channel cut on GLO-90 (above).
- On GLO-90, the 8–9 block steps where a valley steepens a mountainside (above). These are not walls of the reported
  kind, and no rule here addresses them.

## Known, not changed

- **Banks where rivers meet.** A column within one river's bank but nearer, by distance minus half width, to another
  still takes the nearer river's bank or valley. Rules 2 eases that column's ground to the other river's level, but
  does not make it that river's bank.
- **Rivers that end inside the outlines' land.** The Meghna, the Rhine–Maas at Rotterdam and the river in north-east
  Greenland keep a straight cut to the sea through the fallback.
- **The Scheldt's fresh-water last reach** in the estuary (cause 4).
