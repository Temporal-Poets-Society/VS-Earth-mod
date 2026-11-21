# Lakes: course channels in their beds, and their depths (EA-0183, EA-0184; 18 November 2026)

Earth 2.2.0-rc.5 (`claude/terrain-2.2` 258393c5), branch `claude/lakes-2.2`. Decision 0100. The windows come from the
EA-0148 probe (`../ea-0148-offset-2026-11/probe`), which builds a world's terrain model from the data store and samples
its columns; `EA0148_STRIDE=10` samples every tenth block of the 1:10 world, so a whole lake fits in a window, and the
variant `base-lake0` builds the same world with lake rules 0 (as rc.5). Large output is in `C:\VS mods\work\lakes-2.2`.

## The author's report

At 46.452°N 6.523°E near Lausanne, 1:10, 2.2.0-rc.5: a walled trench forking in a Y in Lake Geneva's bed; the bed
plainly visible and grown over with rooted water plants. "Look at fix as systematic and not just this spot." Then:
"dont change plants, just lake depths".

## 1. Course channels in lake beds (EA-0183)

`TerrainModel.ApplyChannel`: a lake column whose water is at a course's level, within the course's half width, had its
bed dug to the channel's depth ("the channel's depth through the shallows"). HydroRIVERS draws a lake's through-flow as a
line, so wherever a river passes through a lake its line became a walled trench; the Y is two lines meeting. The course's
banks and valley also shaped the shore land near such lines. Unchanged since decision 0053 item 17 (7 November): 2.1.4
has it; neither courses rules 5 nor river ends rules 2 touch it.

**Worldwide** (the probe's `courses` mode on the 1:10 world: every reach of the prepared list every 50 m against the
lake outlines; `results/courses-in-lakes-top200.csv`): courses run 86,400 km inside 32,460 lake pieces, on 45,999
reaches; 7,934 pieces hold a kilometre or more, 1,671 ten or more. The most: Superior, Victoria, Titicaca, Huron, Ladoga,
Malawi.

**Lake rules 1:** the course draws nothing where its line lies well inside a lake at its level (`CourseInLake`). Columns
whose bed was dug deeper with rules 0 (`compare_rules.py`, `results/rules1-vs-0.txt`; stride 10, each column stands for
a hundred), before the real depths:

| lake | 1:10 | 1:25 | 1:100 |
|---|---|---|---|
| Geneva | 994 (2.6 blocks deeper on average) | 2,001 | 3,167 |
| Constance | 1,050 | 2,096 | 2,881 |
| Peipus | 515 | 532 | 0 |
| Ladoga | 64 | 480 | 0 |
| Baikal | 0 | 130 | 0 |
| Pielinen | 0 | 0 | 0 |

With rules 1 none. Shore land changed in 51 sampled columns at Constance and 59 at Ladoga at 1:100 (the course's valley
no longer shaping it).

**Worldwide after the fix** (the same count, each point judged as `CourseInLake` does by its reach's flow): of the
86,388 km of course inside lakes the courses draw nothing along 73,318 km (85 %); they still draw along 13,070 km within
their channel and bank of a shore, where a river comes in or leaves (so its channel meets the lake), and in lakes no
wider than the river itself (river widenings): 31,761 pieces have some of it, 2,396 a kilometre or more. At the author's spot (`images/lausanne-trench-10.png`, 134 × 223 blocks at full resolution,
rules 0 left, rules 1 right) 2,479 of 29,882 lake columns were cut up to 3 blocks deep; none with rules 1.

## 2. Lake depths (EA-0184)

**Before** (`OutlineLakeColumn`): an outline lake's bed lay `min(0.05 × distance to shore, 40 m)` below its level, the
distance stopping at the outlines' reach (640 m at 1:10, 800 m at 1:25 and 1:100), deepened towards half the 2′ grid's
bed, and at least 3 blocks from 4.5 blocks out. The 2′ grid's bed (tools/bake_earth.py) is ETOPO 2022's bathymetry where
it has one, else `min(6 + 14 × km^0.7, 400)` m from the distance to shore on the 1′ grid. Nothing knew the lake's real
depth. Of HydroLAKES' 185,181 lakes from 1 km², 85 % have a mean depth under 10 m and 0.1 % over 100 m
(`hydrolakes_depths.py`, `results/hydrolakes-depths.txt`); Earth drew nearly all of them 25–45 m deep, and at 1:10
nearly every lake 3 blocks deep.

**Lake rules 1** (`TerrainModel.LakeDepthAt`, `lake-depths-1.bin` from `tools/bake_lake_depths.py`,
`results/bake-lake-depths.log`):

- The bake joins lakes-glo90-5.bin's 5,449,463 pieces into 5,408,465 whole lakes (41,246 joins across tile sides) and
  puts each of HydroLAKES' 185,181 lakes from 1 km² on the whole lake its outlet lies on (within 600 m, up to 30 km for
  the largest; the whole lake at least an eighth of its area; the nearest and closest in area). 84,800 match; most of the
  rest aren't in the outlines at all (of 600 sampled 1–10 km² lakes, 251 have no outline within 3 km: reservoirs, salt
  lakes drawn from the 2′ grid, lakes the water mask doesn't have). Large lakes: 80 of 178 from 1,000 km² (the rest
  reservoirs, salt and terminal lakes, the Caspian as sea; Onega and Great Slave unmatched, their outlets more than
  30 km from the outlines' lakes).
- 80,856 whole lakes get a depth: the matched HydroLAKES lakes' volume over their area (if they cover at least a tenth of
  it), or ETOPO's where ETOPO holds the bed (22 lakes, 4 of them not in HydroLAKES' match). Per piece the floor depth
  that gives that mean over the lake's own distances to the shore (pieces from 5 km² rasterised at 15″, a distance
  transform; 16,053 lakes), the size (area over shore length) and the ETOPO flag. 91,740 pieces, 252 kB; the mod's zip
  35.3 MB.
- The bed: the floor × (1 − e^(−d/L)), L half the size, d the distance to the shore (the outlines' own within their reach,
  traced on a cached lattice beyond); lakes not listed take HydroLAKES' median depth for their area; ETOPO's lakes keep
  the 2′ grid's bathymetry as their bed; at least one block; never below the deepest sea floor's block; the floor's
  noise at most 3 m.

**Results** (`lake_depths.py`, `results/depths-rules1.txt` against `results/depths-rc5.txt`; the lake's bed in metres,
the water in blocks):

| lake | real mean / max (m) | rc.5 bed, mean (m) | lake rules 1 bed, mean / max (m) | blocks at 1:10 (mean / max) | 1:25 | 1:100 |
|---|---|---|---|---|---|---|
| Geneva | 156 / 310 | 30 | 162 / 245 | 15.3 / 24 (was 3.0) | 6.3 | 14.4 |
| Constance | 94 / 251 | 28 | 96 / 136 | 8.8 / 13 (3.0) | 3.3 | 7.6 |
| Baikal (mid-lake window) | 739 / 1,642 | 388 | 776 / 1,636 | 56 / 77 (37) | 33 | 54 |
| Ladoga | 48 / 230 | 60 | 54 / 62 | 9.5 / 11 (10.1) | 9.5 | 9.4 |
| Pielinen | 10 / 61 | 27 | 10 / 16 | 1.0 / 3 (3.0) | 1.0 | 1.0 |
| Peipus | 7 / 15 | 40 | 8 / 12 | 1.0 / 6 (5.9) | 1.0 | 1.0 |

The means come out right; the deepest points less so (Geneva 245 m against 310, Constance 136 against 251, Ladoga 62
against 230): the curve flattens to one floor, where real lakes have a deep basin in one part (Ladoga's north).
Baikal's 1,636 m is ETOPO's own bed.

(Blocks per metre: 0.103 at 1:10, 0.045 at 1:25, 0.113 at 1:100; below sea level the vertical scale is the sea's,
logarithmic, so Ladoga's 54 m are about 10 blocks at every scale. Baikal's bed at 1:10 lies at y 78, above the deepest
sea floor's y 62.) At the author's spot (267 × 445 blocks, `images/lausanne-depth-10.png`) the lake is 23 blocks deep on
average (238 m; the lake's deepest point, 310 m, lies off Lausanne), 3 before.

**The world's floor** (`floor_and_life.py`, `results/floor-and-life.txt`; Baikal's deepest basin, 53.25°N 108.1°E):
below sea level the vertical scale is the sea's, logarithmic, down to the deepest sea floor at y 62 in every preset
(1:10 1024 high, 1:25 512, 1:100 and 1:200 384, sea level 110). Baikal's bed (1,636 m, 1,186 m below sea level) lies at
y 78 at every scale: 16 blocks above that floor and 78 above the mantle. Lake rules 1 also clamps a lake's bed at the
deepest sea floor's block + 1 (`LakeFloorY`), which no lake reaches.

## 3. Water plants

Vanilla's underwater patches give their water depth in blocks (hornwort and muskgrass 4..15, the milfoils 1..8, water
crowfoot 2..8), about a metre each in vanilla's world but 10 m at 1:10; on 3-block lakes they covered every floor. A
rescale (depths read as metres, in the world's blocks) was built and dropped on the author's word ("dont change plants,
just lake depths"). With real depths they keep to water up to 15 blocks deep by themselves: Geneva's floor lies deeper
than that from a few dozen blocks out. At 1:10 (`results/floor-and-life.txt`): 58 % of Lake Geneva lies deeper than
15 blocks and is bare, 37 % is 2 to 15 blocks deep, where hornwort and muskgrass may grow (15 % 2 to 8, crowfoot's); at
1:25, where Geneva is 6 blocks deep, 89 %. Peipus and Pielinen are one block deep at their real depths, and vanilla
places an underwater patch only in water at least 2 blocks deep: they have none (before, at 3 to 6 blocks, they were
grown over).

**Fish** (decision 0096) spawn on the bed within a depth band below their water's surface (freshwater fish in the top 21
blocks, in light 3 or more): 77 % of Lake Geneva at 1:10 is within 21 blocks, 8 % of Baikal's deepest basin, all of
Peipus (a block deep, water to every side: their habitat test).

## 4. Streams

The streams layer (EA-0131) with `tests/StreamsTests` probe at five places (`results/streams-lake-rules-1-vs-0.txt`):
the stream columns' digests are the same with lake rules 1 and 0 at all three with streams (Lauterbrunnen 2,811
columns, Eidfjord 1,185, Thun 634 on Lake Thun's shore), no leaks either way; whole-window digests differ only where a
lake is in the window (Lauterbrunnen, without one, is the same throughout).

## Tools

- `lake_depths.py <dir>`: each window's main lake, its depth in blocks and the lake's own bed in metres.
- `compare_rules.py <dir> [a] [b]`: two variants column by column (beds and shore land that differ).
- `bed_picture.py <dir>/<place>_<scale> a b out.png` (`BED_SCALE` blocks for the darkest): the bed under two variants.
- `hydrolakes_depths.py`: HydroLAKES' lakes by mean depth.
- `tools/bake_lake_depths.py`, `tools/lake_outline_reader.py`: the depth file.

Reproduce (from the repository root):

```
.venv\Scripts\python.exe -I tools\bake_lake_depths.py <store>\glo90\lakes-glo90-5.bin <HydroLAKES_points_v10.shp> <ETOPO_2022_v1_60s_N90W180_surface.tif> earthdata\lake-depths-1.bin
dotnet build -c Release -o C:\VS mods\work\lakes-2.2\probe-bin docs\research\ea-0148-offset-2026-11\probe
set EARTH_STORE=<a data store with the GLO-90 tiles and the EA-0148 files>
set EA0148_STRIDE=10
dotnet ...\Ea0148Probe.dll <repo> <out> lakes.txt 10,25,100 base,base-lake0
dotnet ...\Ea0148Probe.dll <repo> <out> lakes.txt courses "46,6"          (courses-in-lakes.csv)
set STREAMS_LAKERULES=0   (and unset)
dotnet run -c Release --project tests\StreamsTests -- <repo> probe <streams-glo90-1.bin> lauterbrunnen=46.595,7.905 ...
```

`lakes.txt`: `geneva; 46.36; 6.53; 0.17; 0.43`, `constance; 47.63; 9.33; 0.15; 0.45`, `baikal; 53.50; 108.10; 0.18;
0.50`, `ladoga; 60.85; 31.40; 0.65; 1.20`, `pielinen; 63.25; 29.60; 0.35; 0.32`, `peipus; 58.70; 27.45; 0.45; 0.42`.
