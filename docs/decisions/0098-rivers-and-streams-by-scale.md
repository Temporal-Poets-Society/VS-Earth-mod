# 0098: Rivers and streams by scale, and no loose river water, at 1:50 and coarser

Status: **in Earth 2.2.0** (21 November 2026). **Rules 1 built** (EA-0179, `claude/river-cutoff`), merged into `claude/terrain-2.2` for the author's in-game test
(test build 2.2.0-rc.6, 18 November 2026). **Rules 2 built on `claude/river-cutoff-2`** (rivers to their end, head streams,
the steep streams' lag), for new worlds, merged into `claude/terrain-2.2` for the author's in-game test (test build
2.2.0-rc.8, 19 November 2026). **Smallest stream from 1 m³/s** (EA-0200, below) on `claude/terrain-2.2` (test build
2.2.0-rc.12). Offline checks only, not seen in game. Date: 2026-11-18. Decided by the author:

- The report, on 2.2.0-rc.4 at 1:200, then 1:100 and 1:50: "smaller contributories are dashed and my mind we need just
  remove at each scale some river size".
- The direction: "i think we need to have both continues rivers and remove rivers based on scale. What is decent river at
  1:10 becomes maybe 1 block stream at 1:50 and 1:100 disappears. I think similar ideas as small streams for 1:10 which are
  1 block wide".

The thresholds and how it is drawn are the maintainer's (D72).

## What the dashes were

The author's three screenshots (1:200, 1:100, 1:50) all show the Aiviekste (63-77 m³/s in the data), leaving the Daugava's
bend at Pļaviņas (56.61° N 25.77° E) east-north-east.

- **It had no course at those scales.** Coarse worlds drew courses from 100 m³/s, and each one's upper main branch on
  upstream, narrowing to 20 m³/s (Streams "ends", decision 0061's quick step). The Aiviekste is neither.
- **What showed was the water mask's own river water** (the river outlines, decision 0053 item 18), 50-180 m wide in 90 m
  pixels.
  - Coarse worlds kept all of it.
  - At 1:100 and 1:200 a river that narrow falls into single columns. At 1:50 it is 1-3 blocks but broken where the mask is
    (the probe's map: separate pieces, nothing between them and the Daugava's bend, where Pļaviņas' reservoir is left out of
    the outlines).
- **Courses never broke.** In Latvia (55.6-58.1° N, 20.9-28.3° E) and the Main valley (49.5-51.0° N, 7.5-11.0° E), at 1:50,
  1:100 and 1:200, 100 % of every course's centre line is wet, the narrowest "ends" included.

## Decision

**Coarse river rules 1** (`EarthParams.CoarseRiverRules`, ProtoMember 216; `RiverNetwork.CoarseRulesVersion`), new worlds at
1:50 and coarser whose streams are on Automatic: three classes by discharge, and no loose water.

1. **Rivers, from 100 m³/s** (`RiverMinDischarge`, as before): drawn by the River width rule (decision 0095).
2. **One-block streams, from the Earth tab's Smallest stream up to the rivers.**
   - Every reach in that range (Streams "all", not only the main branches), drawn as the smallest stream:
     `RiverNetwork.OneBlockStreams`, `StreamT` 0, `MinHalfWidth` (about one block), a block deep, the smallest bank and
     valley, whatever the mask measured.
   - They lie on the courses already snapped to GLO-90's valley floors with their levels (courses rules 5).
   - They are laid out by the river network's own pools, steps, chutes and falls, and they join the river, lake or sea they
     flow into at its level, as the 20-100 m³/s streams of 1:25 and the streams layer's networks are. No new data.
3. **Left out:** every smaller river, and its water.

**No loose dashes:** a piece of the mask's river water under 2 blocks wide stays only along a drawn river or stream
(`RiverOutlineData.Layer` courseOnly, `LakeOutlineLayer.CourseOnlyAt`, `CourseOnlyWideBlocks`, `Thinned`).

- "Along" means half or more of the points along its rings are by a drawn course (`TerrainModel.CourseDrawsAt`).
- The width is its mean width: twice its area over its shore, in blocks.
- Every other narrow piece goes, whatever its flags.
- Wider pieces stay wherever they are. These are real channels, side arms and backwaters the river data has no course for,
  and they are continuous at these scales: Riga's Buļļupe and Sarkandaugava, the Mīlgrāvis to Ķīšezers, the old arm round
  Doles sala.
- Lakes are untouched.

**Smallest stream** (`earthSmallestStream`, a slider of 20-100 m³/s in steps of 5, saved as `StreamMinDischarge`; each Earth
size's play style sets its own, `EarthWorldOptions.RecommendedSmallestStream`):

| Earth size | 1:10 | 1:25 | 1:50 | 1:100 | 1:200 |
|---|---|---|---|---|---|
| Smallest stream | 20 (no effect) | 20 (no effect) | **40 m³/s** | **100 m³/s** (no streams) | **100 m³/s** (no streams) |

- **The rule:** the discharge whose real width (8·√Q m) is a block, (N ÷ 8)². That gives 39 at 1:50, 156 at 1:100 and 625 at
  1:200. It is bounded by the data's 20 m³/s and the rivers' 100, so at 1:100 and 1:200 there are no streams.
- **The same rule fits the fine scales:** 1:25's 20 m³/s streams and 1:10's streams layer from 1 m³/s.
- **The author's anchor:** a river like the Aiviekste is a stream at 1:50 and gone at 1:100.
- At 1:25 and finer the slider has no effect: the rules don't apply there, and new 1:10 and 1:25 worlds are as before.
- A world with Streams set by hand in earth.json ("all", "ends", "none") keeps the old rules.

The world's log lines say which rules apply: the river segments line names the streams' mode and floor; the river outlines
line says "only along the drawn rivers and streams"; "Earth rivers: coarse river rules N" counts what rules 2 did.

**Coarse river rules 2: rivers to their end** (the coordinator, D72, 18 November 2026, after the worldwide check below).

- A reach below a river whose flow drops under 100 m³/s is drawn as a river (`CoarseRivers`, `RiverBuildOptions.RiversToTheirEnd`).
  - Its flow for the network is 100 m³/s, so it keeps the river rule's width, depth and banks.
  - These are losing rivers. Under rules 1 their lower course was a one-block stream, a step from 6 blocks to 1 where
    rc.5's streams had narrowed smoothly.
  - Examples: the lower Darling below Wilcannia (101 → 99.9 m³/s, 437 km), the Shebelle (584 km), the Chobe/Linyanti
    (384 km), the Ewaso Ng'iro, the Río Colorado in Argentina, Cooper Creek. There are 60 places worldwide.
- The data's reaches are not changed: the network works on copies, as `RiverDeltas` does. Its reach list, and so
  `Hit.Q` there, reports 100 m³/s.
- **Steep one-block streams** (the coordinator, 18 November 2026, from the streams session's rc.6 notes): rules 1 laid out
  a coarse world's streams with a river's lag (`ChuteMaxDrop`, 4 blocks). In a valley falling faster than a block in 3, a
  stream stood on banks built up above the slope, or its pools were held under its own levels in a trench: the Dudh Kosi,
  high Andean streams, the Passer at Meran with a smaller Smallest stream.
  - Under rules 2 the one-block streams fall as soon as they lag `StreamMaxLag`, as a streams layer's do (decision 0061;
    `RiverBuildOptions.SteepStreams`, `RiverNetwork.LagOf`).
  - The streams layer's other half is not taken: there, a stream's pools are no longer held under its own levels.
    Measured with it, 7 of the 114 windows below got worse (Greenland 0 → 9 blocks above the land, the Indian Himalaya
    2 → 4): without a fit to the land, the water rose to levels the land drawn at 1:50 lies under. Without it, none got
    worse.
  - **Measured** (the streams session's window set at 1:50, 512 blocks round each reach of 40-100 m³/s falling 8 % or more,
    all 64, and 50 at random of 3-8 %; river and stream water against the land drawn without the rivers, steps settled as
    a chunk settles them):

    | | Rules 1 | Rules 2 |
    |---|---|---|
    | 8 % or more: water 2+ above the land | 40 of 64 | 33 |
    | 8 % or more: 3+ above | 30 | 16 |
    | 3-8 %: water 2+ above | 23 of 50 | 21 |
    | 3-8 %: 3+ above | 11 | 0 |
    | Trench (the window's median water 5+ under the land) | 8 of 114 | 8 |
    | Leaks (water beside lower dry land) | 0 | 0 |

    Over the ~850 reaches falling 3 % or more, water 3+ blocks above the land goes from about 200 to about 16, all among the
    64 steepest. The Western Ghats at 14.40° N 74.75° E (10 → 8), the Andes at 10.18° S 75.58° W (7), Nepal (5),
    Greenland's ice edge (46 and 13, as before) keep it; what stays is mostly water 2 blocks above, on a bank 1 block up.
  - The rivers keep `ChuteMaxDrop`. 1:10 and 1:25 are unchanged, since rules 2 apply only at 1:50 and coarser.
  - The streams layer's other steep step is not taken: fitting the levels to the land drawn without the rivers, and laying
    the network out again where a pool still stands too high. The world network is laid out once at world start, before
    GLO-90 is attached; fitting there would mean a second layout and could let World Map's client model drift from the
    server's terrain. That is left for after 2.2 (the coordinator). What remains is a course that doesn't follow the
    valley as 1:50 draws it: the Andes at 10.21° S 75.59° W, whose snapped levels fill a dip 7 blocks deep and cut a spur
    8 blocks high.
- **Head streams** (the author, 18 November 2026: "Yes, taper them"):
  - Above each river whose biggest inflow the world doesn't draw, that inflow's own upper main branch is drawn as a
    one-block stream for 10 km of the data (`CoarseRivers`, `RiverNetwork.HeadStreamKmRule`), the last reach cut there.
  - At 1:100 and 1:200, where no stream is drawn, that is every river's start. A river now starts from a stream, as at
    1:50, not out of nothing at its full 6 or 4 blocks.
  - Tributaries stay left out, and the step from a stream to a river at 1:50 is as before. At 1:50 only the 2 rivers whose
    biggest inflow is under the Smallest stream get one.
  - Measured at 1:100: 3,750 of the 3,752 rivers that started out of nothing under rules 1 now start from a head stream,
    38,700 km (+4 % of what is drawn); at 1:200 3,751 and 38,700 km. The 2,422 rivers still starting with nothing above
    are those whose data has no inflow at all (lake outlets, sources), as in rc.5.
- New worlds at 1:50 and coarser record rules 2 (`RiverNetwork.CoarseRulesVersion`). Worlds made on rc.6 and rc.7 keep
  rules 1.

## Numbers

**Which rivers** (discharge in the data):

| River | m³/s | 1:50 | 1:100, 1:200 | rc.4 at every coarse scale |
|---|---|---|---|---|
| Daugava | 447-677 | river | river | river |
| Aiviekste | 63-77 | **stream** | left out | mask dashes |
| Gauja at Valmiera, Sigulda | 67, 91 | stream | left out | narrowing "ends" |
| Venta at Kuldīga | 91 | stream | left out | "ends" |
| Lielupe at Jelgava | 80 | stream | left out | "ends" |
| Mēmele at Bauska | 65 | stream | left out | "ends" |
| Mūsa at Bauska | 35 | left out | left out | "ends" |
| Salaca | 39 | left out | left out | not drawn |
| Main at Würzburg, Frankfurt | 101, 187 | river | river | river |
| Saale at Halle, Neckar at Heidelberg | 101, 162 | river | river | river |
| Fulda at Kassel | 62 | stream | left out | "ends" |
| Lahn at Limburg | 40 | stream | left out | not drawn |
| Werra at Eschwege | 37 | left out | left out | not drawn |

**River length** (every reach of the data in the box, km):

| | Latvia | Main valley |
|---|---|---|
| 100 m³/s and more (rivers, at every coarse scale, as rc.4) | 679 | 445 |
| 40-100 m³/s: 1:50 one-block streams; 1:100, 1:200 left out | 475 (rc.4 drew 362 as "ends") | 205 (rc.4: 150) |
| 20-40 m³/s: left out at every coarse scale | 469 (rc.4 drew 107 as "ends") | 195 (rc.4: 15) |

**The author's places** (window 56.45-56.82° N, 25.55-26.35° E, east of Koknese), columns of water by source:

| Scale | rc.4: course water | rc.4: mask water | rc.4: stream channel | New: course water | New: mask water | New: stream channel |
|---|---|---|---|---|---|---|
| 1:50 | 17,243 | 3,169 | 0 | 17,242 | 2,105 (along the drawn ones, or wide) | 2,235 (the Aiviekste, joined to the Daugava) |
| 1:100 | 6,881 | 721 | 0 | 6,881 | 143 | 0 |
| 1:200 | 2,367 | 172 | 0 | 2,367 | 16 | 0 |

**Loose water** (pieces of mask water touching no drawn course; windows 56.4-56.9° N 25.0-26.5° E and 49.9-50.4° N
8.6-10.0° E), rc.4 → new:

| Scale | Latvia | Main valley |
|---|---|---|
| 1:50 | 115 pieces (2,352 columns) → 2 (133 columns, pools 2 blocks wide or more) | 4 (5 columns) → 2 (3 columns) |
| 1:100 | 81 (575) → 1 (31, a pool) | 1 (7) → 0 |
| 1:200 | 66 (138) → 0 | 0 → 0 |

**Riga** (window 56.85-57.12° N, 23.85-24.45° E), mask river water, in columns: 1:50 9,604 → 9,509 (the channels kept); 1:100
1,877 → 1,167 (channels under 200 m wide go there, where they were dashes).

## The worldwide check (18 November 2026)

Offline, every reach of the data (2.03 million km of 20 m³/s or more), rc.5's coarse rules against rules 1, and 24 windows in
13 regions. Full numbers are in EA-0179's status evidence.

- **Drawn length, share of the data:**

  | Scale | rc.5 | Rules 1 | With Smallest stream 60 |
  |---|---|---|---|
  | 1:50 | 64 % | 72 % | |
  | 1:100 and 1:200 | 64 % | 45 % | 58 % |

  The thinnest at 1:100 are the Maghreb (31 → 16 %), Australia (49 → 30 %) and Iberia (55 → 32 %).
- **Dashes** (pieces of 1–3 columns touching no water): 1,520 → 134 at 1:50, 1,463 → 51 at 1:100, 1,123 → 37 at 1:200.
- **Steep and flat ground:** nothing misbehaves. The one-block streams are 100 % wet, with no water steps of 3+ blocks and
  almost no water above a dry bank. Hot deserts' new streams are mostly dry beds, by the arid rule.
- **What it found:**
  - the losing rivers (rules 2);
  - 3,750 rivers starting out of nothing at 1:100 and 1:200 (rules 2's head streams);
  - delta branches the river data lacks:
    - the Atchafalaya: decision 0013's delta rules 2;
    - the Damietta branch: not in the data at all, so it goes at 1:200;
  - the mask's wider pieces of a channel otherwise dropped, left as broken bits. A later item: judge whole channels.
- **No change to the thresholds:** (N ÷ 8)² and the 100 m³/s floor do what the author asked everywhere.

## Alternatives

- **The mask's water only along the drawn courses, whatever its width** (built and measured, not kept). It also took Riga's
  real channels at 1:50 (mask river water 9,604 → 5,286 columns there).
- **Leaving out the narrow mask water alone, with no stream class** (first built, not kept). It removed the dashes but drew
  no stream at 1:50, where the author wants one.
- **A least discharge for the courses alone** (the "ends" cut per scale), or **"no river narrower than N blocks"**. Neither
  touches the mask's water, which made the dashes; the second also drops navigable rivers at a low River width.
- **Smallest stream from the width-equivalence but at the rivers' width** (the River width floor): it would mix the
  exaggerated gameplay floor into what is a statement about the real river.

## Implementation

- `src/Core/RiverNetwork.cs`: `OneBlockStreams`, `CoarseRulesVersion`, `HeadStreamKmRule`, `LagOf`, `StreamT` (0 under
  MinQ with them), and what rules 2 did (`RiversToTheirEnd`, `HeadStreamCount`, `HeadStreamKm`).
- `src/Core/CoarseRivers.cs` (rules 2), `RiverBuildOptions.RiversToTheirEnd`, `HeadStreamKm` and `SteepStreams`,
  `RiverCoursePreparer` (it keeps the head streams), and `RiverPoolPlanner` (each reach's lag, `RiverNetwork.LagOf`).
- `src/Core/RiverBuildOptions.cs` (`OneBlockStreams`), and `src/Core/RiverCoursePreparer.cs` (no measured width for a stream
  under them).
- `src/Core/LakeOutlines.cs` (`CourseOnlyAt`, `CourseOnlyWideBlocks`, `Thinned`) and `src/Core/RiverOutlines.cs` (`Layer`
  courseOnly).
- `src/EarthParams.cs` (216, in `RulesAgainstBuild`) and `src/EarthParams.Model.cs`.
- `src/WorldParamsBuilder.cs`: the coarse flag, Streams "all", the Smallest stream.
- `src/EarthWorldOptions.cs`: `SmallestStreamKey`, `RecommendedSmallestStream`, `SmallestStream`.
- `src/EarthElevationSystem.cs`: the layer and its log line.
- `worldconfig.json` (the slider; each play style's value), `assets/game/lang/worldconfig-en.json` and
  `docs/wiki/Settings.md`.
- **Tests:**
  - `RiverWaterTests`: one-block streams, the rivers unchanged, nothing undefined with streams from the rivers' own 100. Rules
    2 and 3 on one synthetic losing river with a head and a tributary: the reaches below drawn at 100 m³/s, the data
    unchanged, 10 km of head stream with its head reach cut, the tributary left out; through a built network too. The
    steep streams' lag: a stream falling a block in 2 stands at most StreamMaxLag + 1 above its level under rules 2, more
    under rules 1; the river keeps ChuteMaxDrop.
  - `EarthMapTests` (synthetic): the river along a course and the wide water stay; a 1.8-block channel and a canal off the
    courses go; with no course at all, the wide water stays and the narrow river goes.
  - `EarthWorldOptionsTests`: the rules and streams at each scale, Smallest stream's values and clamps, the play styles, the
    field table, `new-world-params.txt`.
  - `SavedWorldDigestTests`: its build passes the rules.

## Checks

Offline, on `claude/river-cutoff` from 2.2.0-rc.4 (`72ece90c`), rebased on 2.2.0-rc.5 (`258393c5`, fish spawning only, no terrain
code), 18 November 2026.

- **Terrain digests** (every `TerrainModel.Column` field over 128 × 128 columns at six places: Riga, the Norway gorge,
  Lobith, Langholm, Volgograd, Vienna) identical to rc.4's at 1:10 and 1:25, the streams layer built.
- **`new-world-params.txt`, written again:**
  - every 1:10 and 1:25 case is as before, Smallest stream 60 there included;
  - the 1:50, 1:100 and 1:200 cases changed, but rivers off and Streams set by hand;
  - 10 new cases.
- **Every course continuous at 1:50, 1:100 and 1:200** in both boxes, the new one-block streams included: 100 % of their
  centre lines wet, 1.6 blocks wide.
- **The 12 saved worlds, the quick suite, EarthMapTests and the reflection contract:** see the commit.

## What is left

- **Not seen in game.** World Map's chart and the terrain read the same columns, so the classes show on both.
- **At 1:100 and 1:200, Latvia keeps only the Daugava and its 100 m³/s reaches** (the lower Lielupe). The Gauja and the
  Venta go with the Aiviekste. The slider brings them back as one-block streams (60 m³/s).
- **The streams' one-block channels and the rivers' River width meet with a step at 100 m³/s:** a 1.6-block stream becomes
  a 4-7-block river where its flow passes 100 (rc.4 narrowed between).
- **A river whose data has no inflow** (a lake's outlet, a source of 100 m³/s or more) still starts at its full width, as
  before rules 1.
- **Steep streams whose course doesn't follow the valley as 1:50 draws it** still stand up to 8 blocks above the land in
  the steepest places (above). Fitting the coarse network to the land is for after 2.2.

## Smallest stream from 1 m³/s at every scale (EA-0200, 20 November 2026)

The author on rc.11's Earth tab: "Now GUI give smallest stream 20m3/s for 1:25 and 1:10 but we just build that we get small
streams up to 1m3/s and my mind it is working". The slider (20-100, the 1:10 and 1:25 play styles at 20) had no effect at
those scales, where the courses draw streams from 20 m³/s and the streams layer (decision 0061) everything from 1. Built
by the streams session (D72), on the coordinator's brief:

- **The slider runs 1-100 m³/s in steps of 1**: steps of 1 reach 1, 40 and 100 alike, and the game and the Mod DB's
  parser take it (no skipValues, decision 0089). Its default is 1; the 1:10 and 1:25 play styles set 1, 1:50 keeps 40,
  1:100 and 1:200 keep 100 (`RecommendedSmallestStream`: 1 at 1:25 and finer, (N ÷ 8)² to 5 m³/s coarser).
- **At 1:25 and finer it is the streams layer's smallest** (`EarthParams.StreamLayerMinDischarge`, field 222; 221 is the
  soil branch's). The layer leaves out each system's streams below it (`StreamLayer.KeepFrom`): a stream stays when its
  flow is at least the value or when a kept stream flows into it, so every stream left reaches its outlet even where flow
  drops downstream; what goes is headwaters. The courses' own streams from 20 m³/s stay as they are, so the value
  goes up to 20 (higher acts as 20), and 20 leaves out the whole layer. Raising the courses' streams too was not done:
  at these scales the water mask's narrow river water is dropped only where the courses draw everything from 20
  (`DropsNarrowRiverPieces`), and leaving out courses would bring back the loose dashes this decision removed at coarser
  scales. A world at 1 saves 0 (every stream of the layer), so its params and terrain are exactly as before.
- **At 1:50 and coarser nothing changes**: values under 20 act as 20, the river data's smallest.
- **Saves**: worlds made before have no field 222 and keep every stream of their layer; a world made with a cut-off has
  it, which an older build refuses ("saved by a newer Earth").
- **A world configuration from before**: a dedicated server's configuration copied from an earlier 1:10 or 1:25 play style
  holds 20, which now leaves out the whole streams layer; the play styles' 1 is right for every new world from the menu.

## History

- 2026-11-18: EA-0179 found (the author on rc.4 at 1:200, 1:100, 1:50). A first build left out the mask's thin water away
  from the courses. The author asked for continuous one-block streams by scale; built on `claude/river-cutoff`. A course-only
  mask rule took Riga's channels and was narrowed to the water under 2 blocks.
- 2026-11-18: The author: "ok, but it is not just about latvia". Rules 1 merged for rc.6, and checked worldwide offline. The
  coordinator (D72): rules 2, rivers to their end, for new worlds; head streams built behind a switch for the author; the
  Atchafalaya into decision 0013's delta list; then the steep streams' lag (the streams session's rc.6 notes). The author:
  "Yes, taper them": head streams joined rules 2 and the switch went. Built on `claude/river-cutoff-2`.
- 2026-11-20: EA-0200: Smallest stream from 1 m³/s; at 1:25 and finer it trims the streams layer (above).
