# 0063: Rivers follow the real channel at fine scales ("dredging")

Status: **agreed, built** in test build 1.0.3-tall.25 on `claude/tall-glo90` (not released, not installed, not pushed;
the author, 8 November 2026: "Nothing is released from this branch yet. Plan and do proper solutions for all and
implement and test and then merge"; the courses' smoothing at the author's "fix" for the Tigris's angular bends). Checked
offline at every reported place and on the bench at 1:10. The two new GLO-90 folder files are on the data release
(`egl-v1`, the author's "yes", 8 November). Date: 2026-11-08. The diagnoses are in
[the research note](../research/rivers-fine-scale-2026-11-08.md).

## What the author reported

The author's 1:10 world (tall.22: courses 3, lake outlines, river outlines 2, dams removed, Streams "all"), 8 November:
- **Tiber at Rome:** "something spawning in middle of river" (a stepped silt ring with a pit); "also here supposed to be
  river around island - i guess" (dry, terraced hollows); "banks are jagged and also flow direction follows general river
  drop and does not account for local turns"; the Aniene: "the same and side does not connect to main".
- **Rīgas HES:** "dam still partially visible and not fully removed".
- **Gauja:** "also has meandering problems and banks are jagged as not connected and flow direction".
- **Suez:** "we now include Suez even if not selected".
- **Tigris south of Amarah:** pools joined by zig-zag necks, "is it small dams causing this? Can me make maybe some
  intelligent dredging on rivers?"; at Al-Qurna: "here is empty river".
- **Irtysh at Ust-Ishim:** "here also dams left" (land strips across the river).
- **Dnipro above Kakhovka:** "dam is removed but upstream water level my mind is too large"; "also 'rivers' below Kakhovka
  are my mind irrigation channels".

## Causes (measured; the note has the numbers)

1. **The water mask's rivers are one or two pixels wide** (a 3″ pixel is 9.3 blocks at 1:10) and often broken: pixels that
   touch only at a corner became separate outline pieces, and some rivers are marked in only part of their length (the
   Tigris in 19 of 109 rows over 10 km; whole Irtysh loops missing). Each piece was drawn on its own.
2. **The course's channel gave way to the outlines:** none where its centre line lay in an outline, at most the gap plus
   2 blocks beside one (`TerrainModel.ApplyRiver`). Pools and pinches; land strips between pieces; the nearest course
   claimed columns at its full width that it then didn't fill (69 of 736 junctions in Europe left dry, the Aniene among
   them).
3. **Courses off the real channel:** only reaches from 100 m³/s were moved onto the mask (4-connected), so the Gauja
   (90 m³/s) and the Tiber at Rome kept courses that cut across meander necks; the moved ones are 45° staircases on the
   mask's pixel centres.
4. **Flow direction** is the nearest straight course piece's: in mask water the course doesn't follow, 43–48 % of columns
   point more than 45° off.
5. **A mask pond in the channel:** a 2×2-pixel mask lake a block above the Tiber keeps its own bed (the ring and pit).
6. **The arid rule dried a 244 m³/s river** at Al-Qurna (its wet-source flow came out at 0.3 m³/s) and 2,881 columns of the
   mask's own river there.
7. **Dam necks:** the corridor across a removed dam's wall was capped by the reservoir's averaged width and left out the
   tailrace banks (Rīgas HES 137 m against 490–690 m above; 624 of 1,148 dam runs under half their width).
8. **Kakhovka's corridor** took its width from the basin just below the dam (1.32 km of mask water) and its upper level
   from the course's old level in the reservoir: about 1.1 km wide where the river was a few hundred metres.
9. **Canals:** the mask has canals as rivers (Suez and Lake Timsah against [0020](0020-canal-gaps.md), Panama's Culebra
   Cut, Kiel, the North Sea Canal, the Kakhovka irrigation canals).

## Rules (new worlds on full GLO-90; older worlds keep what they saved)

1. **River courses rules 4** (`EarthParams.RiverCourses` 4): every reach from 20 m³/s (Streams' floor) moved onto the mask's
   river water where it lies near: an 8-connected cheapest path through the mask's water (diagonal steps allowed, so a
   one-pixel river is one line), through GLO-90's lowest ground where the mask has a gap, then smoothed off its pixel
   centres. Each vertex carries the mask's half width there (0: no mask water near); in a confined gorge (ground 50 m
   above the water within 500 m on both sides) the valley floor's half width where that is smaller (the author: "where
   river narrows irl and goes deeper we should try similar, but need to keep also some width"), never a width where the
   mask has none. Side arms rebuilt on them. Files `rivers-glo90-snap-2.bin` and `rivers-glo90-arms-2.bin`
   (earthwaterfixdata, beside the version 1 files); the moved courses' inner vertices lie on a 1/65536° grid, which
   the snap file stores as whole steps.
2. **River outlines 3** (`rivers-outline-glo90-3.bin`): corner-only contacts joined; the water split into **wide** water
   (three pixels and more across, contoured smooth from the blurred mask) and **narrow** pieces (flag `Narrow`);
   corridors and bridges from dam rules 2.
3. **Dam rules 2** (`dams-glo90-2.bin`): the corridor keeps the river's full width across the wall and the tailrace banks,
   pinned to the corridor's level; corridor width the smaller of the river's widths going in and coming out, measured
   beyond the dam's foot on river water and capped by the flow's; levels never above GLO-90's ground less the bank; a
   cascade's foot level from the next measured river, not the course's old level.
   - **Implemented** (`tools/bake_dam_removal.py` version 2; `DamRemovalData.Rules2`, `CourseRules2` = 4).
   - **Values chosen:**
     - full width within 1 km of a wall, easing to the zone's cap by 2 km;
     - the tailrace banks: ground below the reservoir's level less 1 m within 1 km of a wall;
     - the flow cap: a half width of at most 10√Q m or 186 m, whichever is more. The mod's own 4√Q is narrower than
       the mask's Daugava (7.6√Q at Rīgas HES);
     - the bank: 1 m below GLO-90's lowest ground within the corridor's half width plus a pixel.
   - **Final file** baked on courses rules 4's list (course key 9693b65a3a265a62): `dams-glo90-2.bin` 5,142,018 bytes,
     sha256 `93180bc47cab9e5fadce7d74ad36c9442bdbb7f91e93a72b548dd0b93cd7061c` (`DamRemovalData.FileBytes2`,
     `FileSha2562`). New worlds get dam rules 2 with courses 4.
   - [The dam note](../research/dam-removal-2026-11-07.md) has the numbers.
4. **Channel rules 1** (`EarthParams.ChannelRules`, 127) — the dredging:
   - the course's channel is drawn everywhere along it, never cut by outlines; its half width is the flow's
     (`RiverNetwork.HalfWidth`), narrowed where the river's measured half width (`RiverReach.Width`: the mask's, or a
     confined gorge's valley floor from GLO-90) is narrower than the real width usual for its flow (8√Q m,
     `RealWidthMetres`), by the same ratio, but never under half the flow's; there the channel is deepened by the same
     factor (Depth(Q) × the flow's half width / the half width, at most 2×), keeping the flow's cross-section (the
     author: "where river narrows irl and goes deeper we should try similar, but need to keep also some width"). The
     ratio, not the measured width in blocks: the game draws most rivers wider than real ones (the playable floor; at
     1:100 every measured river), and a width in blocks halved and doubled the depth of every such river, not just the
     narrow ones (Rome's Tiber at 1:10 and 1:100 is now 1.0–1.75× deep, the lower Tigris 2×, the Daugava and the
     Dnipro 1.0). The width and depth a course claims columns with are the same ones (channel, banks, valley, bed,
     plunge pools and shoals), so what it claims it fills;
   - outline water's depth (beyond the shore's slope of half a block per block) is capped from every course whose
     influence reaches it (`RiverNetwork.BlendNear`): each pulls towards its centre depth within its water, easing back to
     3 blocks across its bank and valley; the deepest governs, a shallower one shallows the water only where no deeper
     one reaches. Wide water far from any course shelves to 3 blocks (it took the nearest course's full depth). The flow
     is the blend of the courses' directions near, each weighted by its half width and falling off with the square of the
     distance beyond it. Depths are rounded through a smooth noise (±0.6 blocks, 24-block wavelength; rapids, lips and
     shoals keep theirs) and the flow turned by up to 0.3 radians, so a step of a block and a change of the eight flow
     labels the water's blocks carry wander instead of running straight across (the nearest course alone made
     straight-edged shallower patches and wedges of another flow label at Rīgas HES, and a straight 7/8 step across the
     Irtysh);
   - the courses are smoothed along the way (`RiverStraighten`'s `cornerSigma`, a Gaussian of 200 m,
     `RiverNetwork.CourseSmoothMetres`), held where the mask measured the river to its real half width there
     (`RiverReach.Width`): every course runs in eight directions (rule 1 moved reaches along an 8-connected pixel path
     smoothed by one 90 m pixel and left the rest on HydroRIVERS' 15″ grid), which at 1:10 showed as straight legs of 50
     to 150 blocks with 45° and 90° corners (the Tigris below Amarah, the author's "angular bends"). A pixel path's corner
     is cut (a 45° one moves in by about 30 m) and stays in the water; a real meander keeps its swing (the Gauja). Chain
     ends (confluences with another kept river, mouths) and lake vertices stay where they are, the smoothing easing in
     over two sigmas. Only where the sigma is 4 blocks or more (scales finer than about 1:50); coarser worlds' courses are
     unchanged;
   - wide outline water joins the channel at the course's level; narrow outline pieces are not drawn where the world
     draws streams down to 20 m³/s (the courses draw those rivers), but for those off the courses (half or more of
     the outline beyond a drawn course's half width and 1.5 mask pixels, `TerrainModel.CourseDrawsAt`) that run into
     the tile's wide water in two places at least 5 pixels apart: side arms and channels between wide waters no course
     runs in (the Daugava's at Rīga, the Danube's delta arms), which the courses don't draw;
   - a mask lake inside a river's channel or banks within 3 blocks of the river's level gives way to the river (a pond
     no bigger than a disc of the channel's and banks' half width; a lake the river runs through stays a lake);
   - water the mask shows as river is never dried by the arid rules;
   - outline water (rivers and lakes) and the grid's lakes (salt and filled terminal lakes too) along the Suez and Panama
     canals are not drawn: without the canal option the land is as before the canals ([0020](0020-canal-gaps.md); the
     Great Bitter Lake was a dry salt basin until the canal filled it in 1869, Gatún Lake is the canal's reservoir), with
     it the canal is the mod's own cut. This is the Suez part of decision 0064's works after 1318 (coasts and waters as
     of 1318, on `claude/works-1318`);
   - a hit's flow direction follows the course's local direction, blended across its vertices (and across courses, above).
5. **Springs rules** (`EarthParams.SpringRules`, 129: release/1.0 saves AridityRules at 128; the Earth tab's **Mountain
   springs and streams**, new worlds only).
   The author, 8 November: "for vanilla rapids - i don't like that they spawn in random mountain locations. in setting UI
   we should add config to switch off", then: "do better placement and this is as config. Instead of 'off' - better
   placement". Vanilla's `GenRivulets` makes single water sources in rock with one open face (cliffs and cave walls), and
   streams dug 2–4 blocks into a slope that are rapids whenever the ground is above sea level + 20 (always, at Earth's
   sea levels above 100). Options:
   - **caves** (default, rules 1): water springs only at least 2 blocks below the ground of their column and its four
     neighbours (cave walls), none in open cliff faces; no mountain-side streams;
   - **streams** ("Real stream heads", rules 2): vanilla's cave-wall springs as in caves (no vanilla springs at the
     surface, no mountain-side streams), and springs of the mod's own (`EarthSprings`, Vegetation pass after vanilla's,
     rules in `Core.StreamSprings`): one source block each, vanilla's rivulet water or rapid water with a scheduled
     update, so vanilla's liquid rules run it downhill. Where they go is decided from the terrain model and the data
     alone (the seeps' columns hashed from the world's noise seed and the chunk), the same whatever order chunks are made
     in. Two kinds:
     - **stream heads**: a spring where each perennial stream of HydroRIVERS v1.0 starts (`earthdata/stream-heads-1.bin`,
       baked by `tools/bake_stream_heads.py`). A head is a reach nothing flows into (Strahler order 1, 4,367,076 of
       them), at the 15″ cell where 10 km² drain to it (HydroRIVERS starts its streams there, so the spring marks where
       the mapped stream starts, below its real channel head). Its discharge there is Q10 = DIS_AV_CMS × min(1,
       10 / UPLAND_SKM). Heads under 0.032 m³/s (101 mm of runoff a year) are left out as intermittent: the heads'
       runoff splits there between dry and wet country (90% of the Hoggar's heads under 52 mm, central Sahara 18 mm,
       Arabia 45 mm; 90% of Latvia's over 217 mm); kept 3,040,058 heads (Latvia, the Alps, Norway and the Amazon all;
       the Meseta and Kansas a third; the Sahara, Arabia and the Australian outback almost none). The spring moves onto
       the valley floor within 450 m (about one HydroSHEDS cell; `SnapRadius` = 450 m in blocks, 1–80): the lowest land
       counting each metre from the head as 0.15 m of height (`SnapPenalty`; steeper than a stream's floor near its head,
       gentler than the valley sides), so it steps down a valley's side but doesn't slide down the valley; it is set into
       the ground where a neighbour is lower, else on the ground. **Rapid water** where the real fall down-valley over
       about 150 m (3–24 blocks of steepest descent) passes the rivers' rapids rule of
       [0046](0046-calm-rivers.md) (fall ≥ 0.3 m/km and fall × √Q10 ≥ 0.025, so 4–8% for the heads' 0.1–0.4 m³/s),
       still water otherwise, and never in a world with **River rapids** off. No spring where a river the world draws
       lies within the snap radius, in or within 2 blocks of drawn water, on ground frozen all year or glacier ice, in a
       volcano's crater, in a structure that suppresses vanilla's springs, on a column another head took, or where the
       generated ground isn't the model's (a cave mouth);
     - **seeps** where the water table of [0059](0059-water-table-and-hod-aquifers.md) meets a slope: a few hashed
       columns per chunk (`SeepTriesFull` 4 where the yearly water surplus of 0059's Budyko rule is full, 300 mm, fewer
       as it falls, none where the aridity index is under 0.2 or the table's query is off), each a seep only where the
       column is land, a land neighbour is lower, no open water is within 2 blocks, and the table stands above that
       neighbour's ground (the source sits at the table or the ground, whichever is lower, open to that side); at least
       8 blocks from another spring of the chunk;
   - **vanilla** (0, and every older world): as vanilla, with the frozen-rock and crater skips of 0.15.6.

   Lava springs keep the geothermal rules. A missing or unreadable `stream-heads-1.bin` is logged once and gives no
   stream-head springs (seeps still come). How far the water runs is vanilla's: a source spreads 6 blocks on the flat
   and goes on only where the ground drops within those (vanilla looks 3 blocks round for a way down), about one block
   of drop in six. At 1:10 (10 m across, 9.7 m high) that is a real slope of about 16%, so most springs run 6 blocks
   (60 m) and spread out on the valley floor; at 1:100 (100 m across, 8–25 m high) 2–4% does, and mountain springs run
   1–1.4 km (measured below). Proposed, not agreed: springs at the heads of the drawn streams layer of
   [0061](0061-small-streams-at-fine-scales.md) if that layer is built (its heads would replace HydroRIVERS' where it
   draws streams).

Narrow canals that only leave a river or a reservoir (the irrigation canals below Kakhovka) go with the narrow pieces
at fine scales; narrow canals between two wide waters in one tile (most of the Kiel Canal) stay like side arms, and
wide modern canals other than Suez and Panama are not yet told apart from rivers (open).

## Compatibility and data

Nothing from the tall line is released (the author). New worlds on full GLO-90 save courses 4, outlines 3, dam rules 2 and
channel rules 1; worlds made earlier keep their saved versions and read the files of those versions. The new GLO-90
folder files (`rivers-outline-glo90-3.bin`, `dams-glo90-2.bin`) are on the data release `egl-v1` of
`Temporal-Poets-Society/earth-glo90` since 8 November (the author: "yes"; sizes and SHA-256 as the mod checks them), and
so is the lake outlines' `lakes-glo90-1.bin` (0053 item 17; the author: "Yes, upload it", 8 November), which new worlds
read beside them. The older test builds' `rivers-outline-glo90-1.bin`/`-2.bin` and `dams-glo90-1.bin` are not (no
released world reads them).

## River outlines 3 (rule 2): as built

`tools/bake_river_outlines.py` (version 3 is its default with the dam bake's corridors; `--reservoirs` and
`--corridors` take the dam bake's `reservoirs-dams.npz` and `corridors.npz` from where they are), read by
`RiverOutlineData` (`FileVersion` 3; worlds that saved 1 or 2 keep reading those files):
- **Corner contacts:** in each 2 × 2 window with river on one diagonal only, the lower of the other two pixels by GLO-90
  becomes river (a lake's or the sea's pixel first), repeated until none are left. Joined pixels and bridge decks don't
  count towards a piece's level.
- **Wide and narrow:** wide water is what survives an opening and a closing by a 3 × 3 square (three pixels and more
  across, 3.5 on the diagonal); a narrow fringe whose every pixel touches the wide water goes with it; a wide piece under
  20 pixels that runs on as narrow water (a pool in a narrow river) is narrow unless it reaches the tile's edge.
- **Wide water** is the 0.5 contour of its pixels (with a pixel of the lakes and sea it touches) blurred by a Gaussian of
  0.7 pixels, simplified by 0.1 pixel. 0.8 was barely smoother and closes one-pixel land strips (their centre blurs to
  0.50); 0.6 left more of the staircase. Within 3 pixels of a tile's edge the blur fades out, so on the edge the
  outline is the pixels' own as in version 2 and neighbouring tiles meet as before.
- **Narrow pieces** (flag 16) are version 2's outlines of their pixels, reaching a pixel into the wide water, lakes and sea
  they touch, cut back to 0.3 pixel inside the wide water's outline: the two overlap, with no land between them.
- **Unit** 24,000 a degree (a twentieth of a pixel north-south; versions 1 and 2: 2,400, a half pixel). Half a pixel is a
  whole number of units at every tile width, so tile-edge vertices are exact (2,400 rounded them at 800-pixel tiles).

Regional bake (Rome, Rīga and the Gauja, the Tigris, the Irtysh at Ust-Ishim, Suez, Kakhovka; 9 tiles, version 2's
inputs). Version 2's tiles came out byte for byte the same from the reworked tool.
- Pieces 3,255 → 1,539 (44 wide, 1,495 narrow); wide water 175.6 km², narrow 109.7 km²; 1,765 corner pixels joined.
- The Tiber at Rome, the Gauja, the Tigris at Amarah and Al-Qurna: no wide pixel in their windows; rings 45 → 8 (Rome),
  51 → 5 (Gauja), 29 → 18 (Al-Qurna: the mask's own gaps). The Daugava at Rīga 947 of 1,193 river pixels wide (the
  rest side channels), the Irtysh at Ust-Ishim 721 of 1,082 (the rest the Ishim), the Dnipro's corridor above Kakhovka
  all wide (2,072 pixels).
- Banks of wide water, distance of the outline's vertices from the outline smoothed by a Gaussian of 1.5 pixels along it
  (median / 95th percentile): as written 0.32 / 0.65 px (3.0 / 6.3 blocks at 1:10) → 0.17 / 0.44 px (1.6 / 4.0 blocks);
  after the game's own rounding at 1:10 1.4 / 4.1 → 0.8 / 3.3 blocks.
- Junctions of narrow pieces with wide water: 0 of 115 left without water between them (version 2's outlines on the same
  junctions: 2), 4 of 653 pixel pairs with land between their centres (version 2: 29; pixel centres the half-pixel
  simplification of narrow pieces leaves outside), the same with the game's rounding.
- Tile edges: the water's runs along the shared edges end where version 2's do.

Global bake (8 November, on dam rules 1's `reservoirs-dams.npz` and `corridors.npz`, the inputs of version 2; 42
minutes on 4 workers at low priority): `rivers-outline-glo90-3.bin` 35,765,705 bytes (version 2: 30,630,118), sha256
`10f3752fe5b6a03c04ac5910540cd805c24875af236c1f45b360cc1aa922b985`. Pieces 4,050,193 → 2,119,765: 85,281 wide
(308,378 km², 39.4 million pixels) and 2,034,484 narrow (172,168 km², 22.4 million pixels); 2.21 million corner
pixels joined, 281,675 fringe pixels given to wide water, 704,363 pool pixels to narrow.

Final bake on dam rules 2's `reservoirs-dams.npz` and `corridors.npz` (courses 4), with **union shores**: where two
pieces' outlines cross, their edges are split at the crossings and an edge with water on both sides is inside the
water and left out of the shore (`LakeOutlineLayer` reads version 3's shores so; overlapping pieces made shore lines and
one-block land lines inside the water). `rivers-outline-glo90-3.bin` 35,782,184 bytes, sha256
`ed4bb2d57db40175a9cc76731136a62321fa85a6d755fb10e7d27e324bd37e28` (`RiverOutlineData`). Published on `egl-v1` on
8 November with `dams-glo90-2.bin` (5,142,018 bytes, `93180bc4…7061c`).

Open: the courses still have to draw the narrow rivers (channel rules 1, in progress); where narrow pieces are drawn
(coarse worlds) the game measures a shore at the narrow piece's end inside the wide water, a one-block-deep line there;
mask gaps that are not corner contacts (bridges the bridge rule doesn't find, the Tigris's broken reaches) stay gaps;
wide water's straight banks keep a gentle step where the pixels step (the blur rounds corners, it doesn't straighten).

## Stream heads (rule 5): data

`earthdata/stream-heads-1.bin` (springs rules 2) is 4.7 MB: "EMSH", version 1, then brotli of the heads per 1° tile
(15″ cells, a byte discharge class each, Q = 0.001 × 2^(class/8) m³/s). It ships in the water-fix pack
(earthwaterfixdata 1.0.3-tall.24, 30.6 MB with the courses 4 files and release/1.0's `aridity.bin`), not the Earth zip,
which would pass its 40 MB limit with them (43.4 MB after the release/1.0 merge; 36.9 MB without them). Data: HydroRIVERS v1.0 (Lehner & Grill 2013, www.hydrosheds.org), HydroSHEDS licence (free for
scientific, educational and commercial use, with attribution), as in `earthdata/ATTRIBUTION.txt`. Rules 2 meant "off" in the
unreleased commit 38974c2 (tall.24); a test world saved with it gets the stream springs in its new chunks.

## Verification

- Offline probes at every reported place (maps of water, levels and flow), the junction count over Europe, the dam
  cross-sections and the count of necked dam runs, and the test suites.
- Bench: a new 1:10 world, from above at each place.
- Springs rules 2, offline (the terrain model with GLO-90, the water table at medium permeability without wetlands, and
  a copy of vanilla's spreading run on the model's ground; up to 1,600 chunks a window), springs placed / rapid water,
  seeps per km²:

  | Window | heads in the file | 1:10 springs (rapid) | 1:10 seeps per km² | 1:100 springs (rapid) | 1:100 seeps per chunk |
  |---|---|---|---|---|---|
  | Bernese Oberland | 119 | 106 (86) | 0.12 | 107 (85) | 0.007 |
  | Gauja at Sigulda | 18 | 17 (2) | 0.31 | 14 (0) | 0.30 |
  | Alps around Bolzano | 68 | 67 (42) | 0.20 | 66 (47) | 0.016 |
  | Sahara (Hoggar) | 0 | 0 | 0 | 0 | 0 |
  | southern Norway (Hardanger) | 113 | 107 (55) | 0.05 | 104 (54) | 0.020 |

  The rest of the heads were skipped beside drawn water (1–9 a window) or with a drawn river within the snap radius
  (0–4). Water runs (median, 90th percentile): 1:10 60 m and 350 m in the Oberland, 60 m and 80–110 m elsewhere;
  1:100 1.5 km and 5.3 km in the Oberland, 1.3 and 4.2 km at Bolzano, 0.6–0.7 km in Latvia and Norway. With the snap
  penalty the springs move a median 212 m (Oberland), 161 m (Bolzano) and 0 m (Sigulda, Hardanger) at 1:10, 0–200 m
  at 1:100; the first build (lowest ground in the radius) moved them a median 447 m, nearly always to the rim down
  the valley. Seeps come where the table reaches the ground at the foot of a slope; in mountain valleys the table
  mostly stays inside the ground.
- Bench, 8 November (1:10 worlds t24s and t24d on tall.24 with springs "streams", the final data files):
  - the Tiber at Rome one continuous channel round its loop, the mask's pond outside it; the Tigris at Amarah
    continuous (its bends still angular); the junction at Al-Qurna wet; the neck at Rīgas HES gone; no irrigation
    canals below Kakhovka; no canal water at Ismailia;
  - Rīgas HES and the Irtysh before the blend (above): a rotated, lighter rectangle where a tributary's end was the
    nearest course, and a straight step across the Irtysh; after it the rectangle is gone. A fainter straight wedge of
    another flow label at the Rīga junction led to blending the flow too; probe at 1:10: flow jumps over 45° between
    neighbouring river columns at Rīgas HES 166 → 82 of 120,687, at Rome and the Irtysh 0;
  - springs, one session of a few thousand chunks: 6 stream-head springs (1 rapid water) and 10 seeps of 9,001 tries
    (7,619 without a lower neighbour, 961 with the table inside the ground); vanilla's surface springs turned away
    61,660 times, its mountain-side streams 54,059. The single source blocks weren't found from the air at the two heads
    looked at (the Bernese Oberland, Sigulda), so their look in the game is not yet checked; no errors in the log.
- Probe on the final build (new worlds, 1:10 and 1:100), against the final-data run before the blend and the width
  ratio: necks under half the width 0 everywhere; depth factor 1.00 at Rīgas HES, Kakhovka, Rīga at 1:100; Rome
  1.0–1.75 at both scales (before: 2.00 on every masked column at 1:100); the Tigris 2.00 (its mask is 3.4× narrower than
  its flow); the Great Bitter Lake's window 0 lake columns (before 87,798), Lake Timsah 0. Open: the flow labels' angle
  to the water's local axis at Rīga at 1:100 (over 45° at 41%, 62% in its outline water; 38 and 45% before; the courses'
  own directions 22%): the metric's axis is unsteady in the wide estuary, the labels follow the Daugava north-west.

## History

- 2026-11-08: reports and diagnoses (four offline investigations); the author: "Plan and do proper solutions for all".
- 2026-11-08: rule 5 (springs) added at the author's request for a switch; default cave springs only.
- 2026-11-08: rule 3 (dam rules 2) implemented and baked globally on courses rules 3's list (12 minutes on 4 workers,
  5,119,779 bytes).
  - Necked dam runs went 624 → 0 by the cap's count; the drawn widths are in the note.
  - Across the wall: Rīgas HES' corridor went from 80–100 m to 420–490 m. Kakhovka's went from 0.8–1.1 km to
    0.5–0.7 km, with levels 13 → 0 m against GLO-90's 14 m (before: 26 → 0 m).
  - Offline probe at 1:10: dam steps still 0 at Pļaviņas, Ķegums and Zhiguli; two-block steps 0 at Gezhouba and Kariba.
  - The mod reads `dams-glo90-2.bin` for `DamRules` 2. New worlds don't get rules 2 yet (wired with the other rules).
- 2026-11-08: channel rules 1 implemented (`TerrainModel.ApplyChannel`; `RiverNetwork`'s widths, depths and blended flow;
  `LakeOutlineLayer`'s skipped flags; `SeaChannels.InCanalZone`). Offline at 1:10 on the current files (courses 3, river
  outlines 2, so no measured widths yet: every depth factor 1.00): Rome's dry hollows 5 → 0, the mask pond and the
  Aniene joined to the Tiber; side junctions left dry in Europe 70 → 2 of 736; outline water along Suez, Port Said and
  Panama 17,515, 33,069 and 21,016 columns → 0 (a course's channel along the Chagres and Gatún remains); the Karkheh
  and the mask's water at Al-Qurna wet; channel rules 0 the same column for column at five places; cost per chunk
  unchanged within the noise. Open: the arid rules' wet-source share starts at rainfall 100, so the Zagros catchment
  (70–85) leaves the Karkheh and the Hawizeh outflow below Al-Qurna 0.3 m³/s of their 244–267; channel rules keep
  them wet only where the mask has them.
- 2026-11-08: rule 2 built (river outlines 3, `tools/bake_river_outlines.py`, `RiverOutlineData` version 3), baked
  regionally and globally on dam rules 1's reservoirs and corridors; to be rebaked on dam rules 2's.
- 2026-11-08: "off" replaced by **streams** (rules 2) at the author's request for better placement: springs at the heads
  of HydroRIVERS' perennial streams and seeps where the water table meets a slope; built, checked offline, not on the
  bench.
- 2026-11-08: rule 1 baked (`tools/snap_rivers_channel.py`, then `--widths-only`, `river_arms.py`, `water_overlays.py
  --snapped2/--armed2`). 191,387 of 619,723 reaches moved (153,679 already on their water, 147,699 without a clear
  gain, 111,410 with no mask water), 2,413 side arms; 624,024 reaches, course key 9693b65a3a265a62. On the moved
  reaches the median turn went from 26.6° to 18.7° and exact 45°/90° pixel turns from 17 % to 0.3 % (41 % to 0.3 % on
  the ones courses rules 2 had snapped); median vertex spacing 371 m to 184 m. Widths at 4.34 million of 6.10 million
  vertices; the gorge rule narrows 30,072 (Rhône and Yangtze gorges; GLO-90 flattens water to the mask, so most gorge
  floors measure no narrower than the mask). `rivers-glo90-snap-2.bin` 8.2 MB, `-arms-2.bin` 0.2 MB; the C# reader
  gives the bake's list bit for bit.
- 2026-11-08: channel rules 1 on the final files (courses 4, outlines 3, dam rules 2), offline as a new 1:10 world
  against t23a's (courses 3, outlines 2, dam rules 1, channel rules 0). Leaving out every narrow piece split the
  Daugava at Rīga (the largest water in a 6 km square 86,153 → 54,570 columns) and took out side arms no course runs
  in: the Danube's delta at 45.2° N 29.4° E 14,755 → 0, the Rhine's at Rotterdam 3,658 → 0. Narrow pieces off the
  courses that join the wide water in two places now stay: Rīga 62,199 (an arm across the tile's edge at 57° N still
  goes: each tile sees one mouth), the Danube 7,459, Rotterdam 491, the Kiel Canal 15,466 → 27,556 (t23a 33,219); the
  irrigation canals below Kakhovka stay out (t23a 11,095), the Amsterdam–Rhine Canal too (13,916). Rome, the Aniene,
  the Gauja, the Tigris at Amarah, Al-Qurna, the Karkheh, the Irtysh, Rīgas HES, Pļaviņas, Kakhovka, Suez and Panama
  the same column for column. Open: at 1:100 the flow's half width keeps the game's floor (13.7 blocks for the Dnipro),
  above every measured width there, so every masked river is drawn at half the flow's width and twice its depth, and the
  Dnipro narrows from 2.75 to 1.35 km where the mask starts below Kakhovka.
- 2026-11-08: the 1:100 width issue fixed by narrowing by the measured width's ratio to the real width usual for the
  flow (rule 4), at every scale: a floor at the playable width alone (tried first) also ended the narrowing at 1:10,
  against the author's "where river narrows irl and goes deeper we should try similar". Outline water's depth and every
  river column's flow taken from all courses near, rounded through a smooth noise (rule 4), after the bench showed
  straight-edged patches at Rīgas HES and a straight step across the Irtysh. The grid's lakes go along the Suez and
  Panama canals too (the Great Bitter Lake stayed). `SpringRules` saved at 129 (release/1.0's AridityRules has 128).
  Springs log wording: "onto the valley floor". Open, as found: an Al-Suwaib course (550397) now draws a wet dug
  channel 1.7 km across land up to 1 km from the mask's water, where t23a had a dry trench; a side arm crossing a 1°
  tile's edge is still left out at fine scales (Rīga's eastern arm at 57° N); the Karkheh's wet-source flow (0.3 m³/s,
  the arid calibration); a channel along the Chagres at Panama; the Tigris's angular bends.
- 2026-11-08: merged release/1.0 (`0c2d78f`: ore rules v4, Penman-Monteith aridity); release/1.0's `aridity.bin` goes in
  the water-fix pack too (Earth 36.9 MB, the pack 30.6 MB). Suites on the merged build: EarthMapTests, WetlandTests,
  RiverIndexTests, WaterTableTests and ZoneTests pass; EarthWorldOptionsTests stops where it stopped before (the tall
  zip's `earth.json` has no `dataPacks`). Bench, a new 1:10 world with springs on streams: Rīgas HES and the Irtysh dam
  open water with the bed deepening smoothly, the Great Bitter Lake dry sand, the Aniene joining the Tiber with smooth
  banks; no errors in the server log.
- 2026-11-08: `rivers-outline-glo90-3.bin` and `dams-glo90-2.bin` uploaded to `egl-v1` (the author: "yes"); the release's
  digests match the mod's. The author asked what "a channel along the Chagres" was: with the Panama Canal off, Gatún
  Lake and the Culebra Cut are dry land (0020), but the Chagres' course (HydroRIVERS, traced on today's reservoir) still
  crosses the old lake bed from Gamboa to the Gatún spillway, as a river at the reservoir's level (26 m) along the
  shipping channel's straight legs. The pre-1914 Chagres did run down that valley to the Caribbean, so a river there is
  right; its line and level are the reservoir's. Left as it is (the smoothing below rounds its legs).
- 2026-11-08: the Tigris's angular bends fixed ("fix"): the courses smoothed along the way (rule 4), held to the
  measured half width. The Tigris below Amarah (reach 584119) was one of rule 1's moved reaches: straight 45° and 90°
  legs of up to 144 blocks from the pixel path. Offline at 1:10 against the committed build (`20e86e9`): channel water
  at the Tigris 9,600 → 9,461 columns, Gauja 7,026 → 6,608, Rome 11,187 → 10,576, Karkheh 5,080 → 4,856 (the shorter,
  smoother courses), widths unchanged (half width / flow's median 1.00 at the Gauja, 0.76 at Rome); no new gaps, dry
  hollows or land strips; Rīgas HES, the Daugava at Rīga, the Irtysh and Kakhovka within about 1% (checked before the
  hold, which only keeps courses closer to where they were). A first try at
  200 m without the hold took a twelfth off the Gauja's meanders. Cost at 1:10: river pieces 6.1 → 9.5 million, the
  probe's managed memory 2.32 → 2.62 GB, network start-up 23 → 26 s (the smoothed lines kept within 1.25 blocks,
  `RiverStraighten.CornerTolerance`). Bench (tall.25, new 1:10 world): the Tigris in smooth bends (one corner left at a
  confluence), the Gauja's meanders with smooth banks; no errors.
- 2026-11-08: `lakes-glo90-1.bin` (58,106,553 bytes, `8f0075ac…36384`) uploaded to `egl-v1` too (the author: "Yes,
  upload it"); the three files' download links resolve.
