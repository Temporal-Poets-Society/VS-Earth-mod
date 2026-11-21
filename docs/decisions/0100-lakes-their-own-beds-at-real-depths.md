# 0100: Lakes keep their own beds, at their real depths

Status: **in Earth 2.2.0** (21 November 2026), built on branch `claude/lakes-2.2` (EA-0183, EA-0184), from `claude/terrain-2.2` (258393c5, 2.2.0-rc.5), for
new worlds (lake rules 1); merged into `claude/terrain-2.2` for the author's in-game test (test build 2.2.0-rc.7, 18
November 2026); not seen in game. Date: 2026-11-18. Decided by the maintainer (D72): how courses
and lake beds are drawn is technical. What players see is the trench and the shallow, overgrown lake the author
reported; the plants stay vanilla's on the author's word ("dont change plants, just lake depths", 18 November 2026); the
coordinator decided the depth file ships in the mod (D72), with no upload.

## Context

The author's in-game test of 2.2.0-rc.5 at 1:10, at 46.452°N 6.523°E near Lausanne: Lake Geneva's bed had a walled
trench forking in a Y, the bed was plainly visible, and it was covered in rooted water plants like a shallow pond. "Look
at fix as systematic and not just this spot."

Measured offline (`docs/research/lakes-2026-11/`); both causes date from decision 0053 item 17 (7 November) and are
the same in 2.1.4:

- **The trench.** Where a lake's water is at a river's level, `ApplyChannel` dug the lake's bed down to the course's
  channel depth ("the channel's depth through the shallows"). HydroRIVERS draws a lake's through-flow as a line, so
  every lake a river passes through got walled trenches along those lines (the Y: two courses meeting). On the 1:10
  world's list, courses run 86,400 km inside 32,460 lake pieces (7,934 with a kilometre or more): Superior, Victoria,
  Titicaca, Huron, Ladoga and Malawi lead. The course's banks and valley also shaped the shores near such lines.
- **The depth.** An outline lake's bed was 5 % of its distance to the shore (that distance stops at the outlines'
  reach, 640 to 800 m), capped at 40 m, and half the 2′ grid's bed (ETOPO's bathymetry for a few big lakes, else a
  profile by distance to shore on the 2′ grid), and at least 3 blocks from 4.5 blocks out. It knew nothing of the lake:
  Lake Geneva's bed lay 30 m down (real mean 156 m), Constance 28 (94), Baikal 388 (739), but Peipus 40 (7) and
  Pielinen 27 (10); at 1:10 nearly every lake was 3 blocks deep. Of HydroLAKES' 185,181 lakes from 1 km², 85 % have a
  mean depth under 10 m and 0.1 % over 100 m: most lakes were far too deep, the deep ones too shallow.
- **The plants** follow from the depth: vanilla's underwater patches grow in water 4 to 15 blocks deep (hornwort,
  muskgrass) or less; a 3-block lake is all within their reach.

## Decision

**Lake rules 1** (`EarthParams.LakeRules`, field 220; new worlds with lake outlines 5 and the depth file):

1. **A course draws nothing inside a lake** (`TerrainModel.CourseInLake`). Where the nearest point of a course's line
   lies in a lake at the course's level (within a block), at least the course's channel and bank width from the lake's
   shore (and at most a block short of the outlines' reach), the course changes nothing in that column: no channel cut
   into the lake's bed, no bank or valley on its shores. Near the shore, where a river comes into the lake or leaves it,
   the course draws as before, so its channel meets the lake. Lakes of 1318 count; none inside a SEA or LAND area of
   1318 (a reservoir undone by 1318 is land, and its river keeps its channel). A pond the river's water joins (channel
   rules 4) stays the river's, and a lake well above or below the course keeps pool layout 4's rim and gorge.
2. **Lakes at their real depths** (`TerrainModel.LakeDepthAt`, `lake-depths-1.bin`):
   - **The data** (`tools/bake_lake_depths.py`, in the mod): lakes-glo90-5.bin's pieces joined into whole lakes (pieces
     meeting along a tile's side at one level); each HydroLAKES lake from 1 km² put on the whole lake its outlet (pour
     point) lies on, within 400 m, the nearest in area (within a factor of 8); per piece of a matched lake its mean depth
     (the matched lakes' volume over their area), its size (its area over its shore's length) and whether ETOPO 2022
     holds its bed (its surface grid below the lake's level at 60 % of its inside points).
   - **The bed:** at a distance d from the shore the depth is Dmax (1 - e^(-d/L)), L half the lake's size and Dmax its
     mean depth over 0.568 (that curve's mean across a lake as wide as its size): steep from the shore, a broad floor,
     the right mean. A lake the file doesn't list (under 1 km², or not found) takes HydroLAKES' median depth for its area
     and a compact lake's size. Where ETOPO holds the bed (Baikal, the Great Lakes), the 2′ grid's whole bathymetry stays
     wherever it is deeper. At least one block of water; never below the deepest sea floor's block + 1 (below sea level
     the vertical scale is the sea's, logarithmic, to the deepest sea floor at y 62 in every preset: Baikal's 1,186 m
     below sea level lie at y 78, 16 blocks above that and 78 above the mantle; the clamp is a guard none reaches).
   - The file is pinned by its size and SHA-256 (`LakeDepthData.FileBytes`, `FileSha256`); another is not read, and new
     worlds then go without lake rules.
   - **The distance to the shore** beyond the outlines' reach: on a lattice a reach apart, the shortest of 16 rays
     stepping out by the outlines' distance (never past the shore), up to 5 L, cached per world, the column taking its
     four lattice points' blend. Each lattice point depends on its own place alone, so the bed is the same in any order
     of generation.

The plants stay vanilla's: with real depths the floors of deep lakes lie below the deepest underwater patch (15 blocks),
and shallow lakes keep their plants.

Considered and not done:

- **Rescaling vanilla's underwater patch depths** (read as metres, in the world's blocks): built and dropped on the
  author's word.
- **Ending the courses at a lake's shore in the data** (as river ends rules 2 ends them at the sea): the course list's
  key would change, and with it the dam, flow and GIRES files keyed to it.
- **A depth from the land's relief around the lake** (no new data): it tells Geneva from Peipus only roughly.
- **A depth raster**: tens of MB for what two numbers per lake give.

## Consequences

Checked offline (`docs/research/lakes-2026-11/`):

- **Trenches gone.** With lake rules 0 the probe's windows (every tenth column) had beds dug deeper along the courses at
  994 columns in Lake Geneva at 1:10 (2.6 blocks on average), 1,050 in Constance, 515 in Peipus, 64 in Ladoga; at the
  author's spot 2,479 of 29,882 lake columns, up to 3 blocks. With rules 1, none. Shore land changes in a few dozen
  sampled columns at 1:100 (the course's valley no longer shaping it). Worldwide, of 86,388 km of course inside lakes
  the courses draw nothing along 73,318 km; they still draw along 13,070 km within their channel and bank of a shore
  (where a river comes in or leaves) and in lakes no wider than the river itself.
- **Depths** (the lake's bed, mean / deepest, against HydroLAKES' mean and the literature's maximum; blocks at 1:10):
  Geneva 162 / 245 m (156 / 310), 15.3 blocks on average, 24 at most (3 before); Constance 96 / 136 (94 / 251), 8.8;
  Baikal's middle 776 / 1,636 (739 / 1,642), 56; Ladoga 54 / 62 (48 / 230), 9.5; Pielinen 10 / 16 (10 / 61) and
  Peipus 8 / 12 (7 / 15), a block. At the author's spot 23 blocks deep (238 m), 3 before. The means come out right; the
  deepest points less so, the curve flattening to one floor where real lakes have one deep basin.
- **The data:** 84,800 of HydroLAKES' 185,181 lakes from 1 km² matched; most of the rest are not among the outlines'
  lakes (reservoirs, salt and terminal lakes drawn from the 2′ grid, which keep their beds). Onega and Great Slave
  stayed unmatched (their outlets lie more than 30 km from the outlines' lakes) and take the median depth for their area
  (12.9 m; Onega's real mean is 30 m). 80,856 lakes in 252 kB; the mod's zip 35.3 MB (the coordinator's limit 38 MB).
- **Plants** grow as before (vanilla's patches, 2 to 15 blocks of water). At 1:10, 58 % of Lake Geneva lies deeper and is
  bare, 37 % is in their range; Peipus and Pielinen, a block deep at their real depths, have none (vanilla needs 2
  blocks of water), where they were grown over before. **Fish** (decision 0096): 77 % of Geneva lies within their
  21-block band at 1:10, all of Peipus.
- **Streams** (EA-0131): the stream columns are the same with lake rules 1 and 0 at Lauterbrunnen, Eidfjord and on Lake
  Thun's shore (2,811, 1,185 and 634 columns), with no leaks.
- **Saved worlds** keep lake rules 0: their terrain is unchanged (SavedWorldDigestTests, 12 worlds). New worlds' params
  change (`new-world-params.txt`). Seen in game: not yet.
