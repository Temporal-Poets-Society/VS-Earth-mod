# 0074: One width along a river, without the water mask's ponds

Status: **built** on `claude/river-ponds` for 2.0.0 (EA-0055; the author, 13 November 2026: fix the open issues that would
change new worlds' terrain before the release). New worlds only: channel rules 5 (`EarthParams.ChannelRules`,
`RiverNetwork.PondChannelRules`); older worlds keep the rules they saved (0002). It does in the game what 0069's rule 5 meant
to do in the outline bake. Date: 2026-11-13.

## The problem

0069's report, item 2: the Lielupe has "large ponds or lakes in river path". Rule 5 was to trim the mask's river water far
from the flow line in short pieces in the outline bake. It was tried and not built: it barely touched the Lielupe's ponds,
or it notched the far bank where the course swings (0069, [Build](0069-river-flow-field-canals-and-shore-zones.md#build-rc7)).

## What the probe found after channel rules 4

The depth-and-flow probe of 0071 and 0073 (a world's model rebuilt offline from its saved parameters), at 1:10 (2 blocks
a pixel) and 1:100 (1 block a pixel) at 20 places: the Lielupe and nine more Baltic and Central European rivers, the
Danube, the Rhine, the Elbe and the Rhône, and the Amur, Brahmaputra, Paraná, Mississippi, Amazon and Nile.

1. **The ponds are the mask's pixels.** The GLO-90 water mask draws a river of 150-250 m 2 or 3 of its 90 m pixels wide
   by turns. Channel rules 2 to 4 keep the mask's water where it is wider than the course and drop narrow pieces to the
   course. So the 3-pixel pieces stay as water a third wider than the river and the 2-pixel pieces between them are the
   course's own width: a chain of ponds along the Lielupe below Jelgava, and rectangles and a bulge at a bend on the Oder
   at Frankfurt.
2. **The bed follows them too.** The river's measured width per vertex (0063) alternates the same way. Where the mask is
   finer than a block (1:100), that width sets the channel's width and depth, so the ponds there are a chain of deep pools
   in the bed (the Lielupe at Jelgava).
3. **A fixed distance cannot tell a pond from a real widening.** Wide reaches, side arms, backwaters, oxbows, lakes the river
   runs through and braided rivers (the Amur, the Brahmaputra, the Paraná) are the same mask water. A test of distance from
   the course or of how long a piece is cuts them, or leaves detached slivers and straight radial cuts.

## Decision

Channel rules 5, new worlds:

1. **One measured width along a reach.** The measured half widths are averaged along each reach, weighted by a Gaussian of
   σ = 2 of the flow's widths (`RiverCoursePreparer.OneWidth`, `RiverNetwork.OneWidthSigma`). Where the mask has no water
   the width stays unmeasured. The channel's width and depth no longer step with the mask's pixels.
2. **A width profile per reach** (`TerrainModel.PondProfileOf`, built once per reach on demand). It samples every 2 blocks
   along the reach. On each side it measures how far the mask's river water goes before land: the run. A run crosses an
   island narrower than a half width (at least 4 blocks) to the water beyond.
   - The water **goes on** (the profile does not trim there) when either of these holds:
     - a ray straight out, or one at 45° up or down the river, reaches an outline lake, the sea or 6 half widths;
     - the water halfway out runs on along the river for 5 of the river's widths (a side arm's mouth, a long widening).
   - The **one width** is built in three steps:
     - the running median of the runs over 5 widths either way;
     - eased by a Gaussian of half a width;
     - raised in a bell a width wide where the water goes on.

   So it follows a widening longer than five widths and not a shorter one.
3. **Ponds become land.** These columns become land at the bank's height, with a beach within 2 blocks of the water:
   - mask river water further out than the one width (with 15 % and 1 block of slack, `PondSlack`, `PondSlackBlocks`);
   - only up to where the run meets land (plus half a mask pixel, so a lone pixel past the run's end goes too).

   The water short of the new shore shelves to it like any bank.
4. **What stays:**
   - water beyond land (side arms, oxbows);
   - water that goes on to a lake, the sea or far out;
   - widenings longer than five widths;
   - every river whose water is more than 450 m across (`PondMaxMetres`, a few mask pixels: the Daugava at Riga, the Rhine,
     the Danube, the Elbe and the big rivers keep the mask as it is);
   - real lakes, which are outline lakes, not river water.

The data files are unchanged: no new data to publish. The cost is not measurable (the Lielupe probe box takes 15 s on both
builds).

## Checks

The probe on the bench's 1:10 world (`params-rc7-fish`) and a 1:100 world (`params-review1111`), rules 4 (the trunk
`0b7f44a`) → rules 5. The measures:

- **River px**: the pixels of river water.
- **Steps**: neighbouring river-water pixels whose beds differ by 3 blocks or more.
- **Small bodies**: river-water bodies of 40 pixels or fewer, detached from the rest; given as count, then pixels.

| Place | Scale 1: | River px (lost / gained) | Steps | Small bodies (count, px) |
| --- | --- | --- | --- | --- |
| lielupe | 10 | 38,894 → 38,341 (944 / 391) | 43 → 16 | 0, 0 → 0, 0 |
| lielupe-jelgava | 10 | 10,077 → 10,043 (127 / 93) | 1 → 2 | 0, 0 → 0, 0 |
| venta-kuldiga | 10 | 6,054 → 5,998 (138 / 82) | 64 → 75 | 0, 0 → 0, 0 |
| neris-vilnius | 10 | 4,667 → 4,655 (107 / 95) | 483 → 478 | 0, 0 → 0, 0 |
| pripyat-pinsk | 10 | 3,428 → 3,455 (7 / 34) | 0 → 0 | 0, 0 → 0, 0 |
| odra-frankfurt | 10 | 7,545 → 7,235 (502 / 192) | 249 → 195 | 0, 0 → 0, 0 |
| danube-vienna | 10 | 22,741 → 22,725 (17 / 1) | 4 → 5 | 1, 59 → 1, 59 |
| loire-tours | 10 | 5,388 → 5,207 (209 / 28) | 170 → 182 | 0, 0 → 0, 0 |
| amur-khabarovsk | 10 | 222,921 → 223,809 (435 / 1,325) | 625 → 482 | 5, 83 → 4, 66 |
| brahmaputra-guwahati | 10 | 125,702 → 126,649 (159 / 1,115) | 123 → 121 | 1, 19 → 0, 0 |
| parana-rosario | 10 | 64,639 → 65,347 (59 / 767) | 123 → 112 | 1, 4 → 1, 4 |
| mississippi-ohio | 10 | 62,045 → 62,469 (183 / 607) | 26 → 7 | 0, 0 → 0, 0 |
| amazon-negro | 10 | 111,260 → 111,315 (117 / 172) | 517 → 526 | 1, 5 → 1, 5 |
| nile-khartoum | 10 | 31,166 → 31,127 (148 / 109) | 56 → 34 | 0, 0 → 0, 0 |
| riga-daugava | 10 | 40,919 → 41,106 (14 / 201) | 81 → 11 | 0, 0 → 0, 0 |
| gauja-sigulda | 10 | 4,567 → 4,574 (75 / 82) | 50 → 28 | 0, 0 → 0, 0 |
| rhine-pannerden | 10 | 17,661 → 17,820 (204 / 363) | 639 → 517 | 32, 184 → 32, 184 |
| danube-budapest | 10 | 13,669 → 13,759 (13 / 103) | 355 → 387 | 0, 0 → 0, 0 |
| elbe-hamburg | 10 | 44,942 → 44,899 (85 / 42) | 58 → 45 | 1, 559 → 1, 559 |
| rhone-geneva | 10 | 1,519 → 1,515 (7 / 3) | 220 → 215 | 0, 0 → 0, 0 |
| lielupe | 100 | 13,379 → 12,508 (1,043 / 172) | 7 → 3 | 1, 1 → 1, 1 |
| lielupe-jelgava | 100 | 4,479 → 4,122 (424 / 67) | 0 → 0 | 1, 2 → 1, 2 |
| venta-kuldiga | 100 | 2,532 → 2,356 (300 / 124) | 1 → 0 | 0, 0 → 0, 0 |
| neris-vilnius | 100 | 2,016 → 2,029 (172 / 185) | 0 → 0 | 0, 0 → 0, 0 |
| pripyat-pinsk | 100 | 1,758 → 1,710 (120 / 72) | 0 → 0 | 9, 87 → 8, 81 |
| odra-frankfurt | 100 | 2,795 → 2,624 (294 / 123) | 16 → 0 | 1, 5 → 1, 5 |
| danube-vienna | 100 | 2,768 → 2,736 (107 / 75) | 13 → 17 | 28, 102 → 28, 102 |
| loire-tours | 100 | 1,992 → 1,678 (402 / 88) | 8 → 0 | 0, 0 → 0, 0 |
| amur-khabarovsk | 100 | 26,985 → 25,145 (2,515 / 804) | 285 → 119 | 10, 367 → 13, 53 |
| brahmaputra-guwahati | 100 | 15,007 → 14,527 (746 / 286) | 69 → 23 | 6, 68 → 9, 109 |
| parana-rosario | 100 | 7,909 → 7,050 (1,423 / 605) | 209 → 62 | 8, 132 → 5, 89 |
| mississippi-ohio | 100 | 10,025 → 9,482 (544 / 54) | 149 → 48 | 4, 39 → 2, 55 |
| amazon-negro | 100 | 8,957 → 8,310 (748 / 101) | 59 → 59 | 0, 0 → 1, 3 |
| nile-khartoum | 100 | 5,384 → 5,522 (237 / 375) | 39 → 57 | 0, 0 → 0, 0 |
| riga-daugava | 100 | 5,256 → 4,771 (652 / 167) | 51 → 25 | 3, 5 → 3, 5 |
| gauja-sigulda | 100 | 2,014 → 1,873 (188 / 47) | 0 → 0 | 0, 0 → 0, 0 |
| rhine-pannerden | 100 | 3,635 → 3,562 (196 / 131) | 89 → 85 | 11, 78 → 10, 30 |
| danube-budapest | 100 | 2,576 → 2,274 (407 / 105) | 54 → 18 | 0, 0 → 0, 0 |
| elbe-hamburg | 100 | 4,554 → 4,353 (241 / 40) | 3 → 2 | 12, 236 → 12, 236 |
| rhone-geneva | 100 | 664 → 667 (10 / 13) | 5 → 6 | 1, 2 → 1, 2 |

Read with the maps:

- The Lielupe's ponds are gone at 1:10. At 1:100 its bed is even and the chain of deep pools at Jelgava is gone.
- The Oder's rectangles at Frankfurt eased to a gentle widening, and the bulge at the bend is gone.
- The braided rivers keep their arms. Their detached bodies are as many as before or fewer at 1:10, and at 1:100 the Amur's
  and the Paraná's are fewer pixels.
- At 1:100 the even width takes away round bulges a few hundred metres across. The measured width at single vertices
  drew them; on the Daugava at Riga they are most of the lost pixels.
- At 1 block a pixel at the Lielupe's bend (56.923-56.940° N, 23.60-23.63° E), river pixels go 11,009 → 9,654. There are no
  steps of 3 blocks or more, and no detached water. The new bank shelves from 1 to 6 blocks over about 10 blocks.

**Bench** (AgentBridge on the default bench, Earth `2.0.0-rc.10-ponds.1` and `-ponds.2` at 1:10, with Cylinder World
1.1.0-rc.4 and Hydrology 0.6.2-rc.6; `/earth where` reports channel rules 5):

- The Lielupe at the bend (56.93° N 23.62° E) and downstream: one width, smooth banks with a beach.
- The Oder at Frankfurt (52.35° N 14.56° E): an even river.
- The Daugava at Riga and Kundziņsala (57.01° N 24.10° E): as before, since the river is past the width gate.

Screenshots are in the suite's `backlog/media/EA-0055-*`.

**Tests**: RiverWaterTests 42 (3 new checks of `OneWidth`), ModelCacheTests 133, RiverIndexTests, EarthWorldOptionsTests 757
and EarthMapTests pass.

## What is left

- Widenings longer than five widths stay, by design: the mask may have some that are not real.
- Rivers wider than 450 m keep the mask's outline, pixels and all.
- At 1:100 the braided rivers' small arms break into a few more pieces (the Brahmaputra 6 → 9 bodies; the Amur 10 → 13
  bodies but 367 → 53 pixels).
- A few places have more steep steps than before, in channels the mask did not widen:
  - at 1:10: the Danube at Budapest (355 → 387), the Loire (170 → 182) and the Venta (64 → 75);
  - at 1:100: the Nile (39 → 57) and the Danube at Vienna (13 → 17).
- The outline bake's study code for 0069's rule 5 (`EARTH_TRIM_BULGES=1`) is left as it was; the files are baked without it.

## History

- 2026-11-13: built for EA-0055.
