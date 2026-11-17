# 0090: Lakes cut into their water bodies, each at its own level

Status: **built, in Earth 2.1.3** (EA-0150); `lakes-glo90-4.bin` on `egl-v1` (the author's "Yes, include it", 17
November 2026). Date: 2026-11-16. Decided by the maintainer (D72): how the lakes are levelled is technical; what players
see is the trench a player reported, and the file's upload is the author's.

## Context

A player testing Earth 2.1.x at 1:50 sent two shots of the Pielisjoki near Joensuu (EA-0150): in game a dead-straight
river in a trench with near-vertical earth walls several blocks high, marshy land and pools above it on both sides, a
narrow straight channel beside it edged with a grey strip; on World Map's chart the river split into long straight
parallel channels with a straight strip of land between.

What it is (checked offline with the terrain model of a new world of the "Earth · 1:50" play style, built from the data
store as `SavedWorldDigestTests` builds a saved world, with GLO-90's own tiles; probe in the EA-0150 notes):

- **The place.** The chart's shapes (the loop of the old channel at Kuurna, the arms north and east) put it on the
  Pielisjoki between the Kuurna dam (29.90°E, 62.69°N; 1970, 14 m) and 30.17°E: the Kuurna reservoir (HydroLAKES 12260,
  a reservoir, 25.2 km²; its water flat at 82.5 m in GLO-90). The chart's faint vertical line is World Map's grid line
  near 30°E; the trench runs across it unchanged, and the lakes meet there (lake outlines rules 3).
- **The world's height.** The player's world is taller than the play style's 384. At 1:50 the trench appears from a
  world height of about 896: up to 768 the 10 m below are within 3 blocks and the lake gives way to the river (one
  water); at 896 and 1024 the model draws the chart's shapes one for one (the straight channel, the strip south of it
  with the lake piece behind, the sliver north), and the minimap's y 158 is 10-25 blocks above the land there.
- **The water mask joins it to Pielinen.** The reservoir's lake cells connect through the Pielisjoki, which the mask also
  has as lake, to Pielinen: one 976 km² component over four 1° tiles. In N62E030 a single 8-connected label holds 9,590
  cells at 92.5 m and 2,049 at 82.5 m.
- **The lake bake.** `tools/bake_lake_outlines.py` joins labels across tiles, levels the joined lake at its largest
  piece's median height (Pielinen's 92.5 m) and judges it as a whole: the reservoir rule (a dam at the lake whose
  reservoir is at least 0.3 of its area) fails (25.2 of 976 km²), Pielinen's Kaltimo lake control flags it raised. So
  `lakes-glo90-3.bin` draws the old reservoir as a lake at 92.5 m, 10 m above its own water.
- **The game.** The dam removal (`dams-glo90-3.bin`) runs the river course through the reservoir falling from 82.5 to
  76 m (it rebuilds only about 100 pixels there: the floor stays GLO-90's flat at 82.5 m). Channel rules: an outline lake
  within `MaskPondRise` (3 blocks) of the river gives way to it; a higher one keeps its level behind a rim at its height
  (pool layout 4) and the river runs below in its own channel. At 1:50 × 1024, 10 m is 4-5 blocks: at 29.995°E the river
  stands at +30 with banks at +34 and +35, the lake at +34 beyond a strip of land. The straight channel is the course's
  (HydroRIVERS runs straight through the reservoir); the grey strip is the perched lake's shore.
- **Not the other leads.** No canal is drawn here (works rules 4's 29 canals don't include the Pielisjoki's; the course is
  HydroRIVERS'); the dams file is right about the reservoir (it is the lakes that keep it); the river's bed is the
  channel rules' usual 4-7 blocks for its 227 m³/s.
- **Worldwide** (`lakes-glo90-3.bin` against the median GLO-90 height inside each piece, every third cell): 2,179 pieces
  (1,099 km²) drawn 3 m or more above their own water, 11,592 (1,667 km²) 3 m or more below it. The largest: Cross Lake on
  the Nelson (518 km², 206 m) at Lake Winnipeg's 215.5 m, joined through Playgreen Lake.

## Decision

1. **`lakes-glo90-4.bin`** (`tools/relevel_lake_outlines.py`), from versions 2 and 3, the water mask and GLO-90:
   - GLO-90 flattens each water body to one height (half metres). Each tile's drawn labels (version 2's, recovered as
     `tools/recontour_lake_outlines.py` recovers them) are cut into **water bodies** where a label holds two or more
     flats (heights on `FLAT_MIN` = 30 cells or more) at least `SPLIT_M` = 2 m apart: each cell goes to the flat nearest
     it in the label, and each flat's 8-connected parts are bodies. An uncut label is one body.
   - Bodies are **joined** across the tiles' edges as the bake joins labels, except two flat bodies (one height on half
     their cells or more) 2 m or more apart. An unflattened lake's pieces either side of a whole degree, whose medians
     differ, stay one lake: no new seam.
   - A lake no body of which was cut and none of whose links was dropped is **version 2's lake**, level and flags. Any
     other is **judged again** as the bake judges a lake, on what can be judged without the bake's lost atlas cache: its
     level the median GLO-90 height of its largest body; left out as a reservoir where a HydroLAKES or Global Dam Watch
     reservoir's dam is at one of its bodies (the bake's box) and the reservoir is 0.3 of the lake's area or more, or 0.3
     of it lies in Natural Earth's reservoirs; raised where such a lake control is; salt and missing as its largest body's
     lake was. The bake's other tests (Earth's sea, its kept salt and terminal lakes, glaciers, the Natural Earth lakes
     its grid dropped as reservoirs) decided the whole lake that was drawn and are not run again on its parts.
   - Tiles whose lakes and neighbours' facing lakes are as before keep version 3's bytes; the rest are outlined as
     version 3 outlines them, except the levels that were cut or meet another level across a tile's edge, each outlined
     with the neighbours' cells of its own level, so two bodies at different levels each keep their own shore where they
     meet.
2. **Lake outlines rules 4** (`LakeOutlineData.FileVersion` 4, `EarthParams.LakeOutlines` 102): new worlds on full GLO-90
   heights read version 4. Saved worlds keep the file they were made with (`SavedWorldDigestTests` unchanged).
3. The file goes on `egl-v1` under its new name, beside versions 2 and 3; a build with rules 4 is released only once it
   is there.

Considered and not done:

- **A rule in the game** (drop or relevel an outline lake where GLO-90 under it lies lower): per-column levels would
  step inside lakes, and a natural lake joined to another at a different level (Cross Lake) would dry instead of sitting
  at its own level.
- **A works-of-1318 LAND area over Kuurna** (the canals' mechanism, in the mod's zip): mends one place and leaves the
  level error everywhere else.
- **Running `bake_lake_outlines.py` again with a level-aware join**: its atlas cache inputs are gone (sea, Natural Earth
  lakes, the grid's terminal lakes), and rebuilding them risks changing lakes the fix doesn't touch; the derived bake
  keeps every unaffected lake byte for byte.

## Consequences

- **The file:** `lakes-glo90-4.bin`, 56,791,888 bytes, SHA-256 `269f85a7…d87c9795` (made 16 November 2026 from
  `lakes-glo90-2.bin` and `-3.bin` in about 5 minutes on 16 workers; `work\ea0150-lakes\full`, report beside it). 2,059 mask
  labels cut into bodies, 206 links across tile edges not made; 18,874 lakes judged again, 14,847 of them at a new level
  (most by half a metre to a metre: unflattened pieces now at their own median), 4 left out as reservoirs (26 km²: Kuurna's
  22.8 km² and three under 2.1 km²); 1,970 of 14,879 tiles changed, the rest version 3's bytes.
- **Worldwide** (the same measure as above, on version 4): pieces 3 m or more above their own water 1,099 → 150 km²
  (5 m or more: 878 → 57; 10 m or more: 87 → 14), 3 m or more below 1,667 → 1,108 km². What is left are pieces under
  1 km², mostly small lakes GLO-90 didn't flatten, where its surface model reads the trees round them.
- **No new seams:** cut length on one side of a shared tile side only, `tools/check_lake_edges.py`: 3.44 → 3.48 % (same
  band), 7.39 → 7.46 % (where GLO-90 changes column spacing): the few places where two bodies at different levels now meet
  on a whole degree.
- **Kuurna at 1:50** (the player's reach, offline model with GLO-90's tiles): at world height 1024 land beside the river
  standing 3 or more blocks above its water, 1,996 → 631 sampled columns (5 or more: 1,204 → 154); what is left is the
  old channel round the loop at Kuurna, whose banks were steep before too. The section at 29.995°E: water at +30 with
  banks at +30 (was +34 and +35 beside a lake at +34). The old reservoir is GLO-90's flat at 82.5 m with the river in
  it; at height 384 the reach is as before (the lake had already given way to the river there).
- **Cross Lake at 1:50 × 1024:** water at +73 with its shore flush (was +76 on a rim at Lake Winnipeg's level).
- Saved worlds keep their lake files: `SavedWorldDigestTests` all 10 pinned worlds unchanged; `new-world-params.txt`
  written again (the 285 full-GLO-90 cases change by LakeOutlines 3 → 4).
- A new world downloads `lakes-glo90-4.bin` (57 MB) instead of version 3. Not yet seen in game.
