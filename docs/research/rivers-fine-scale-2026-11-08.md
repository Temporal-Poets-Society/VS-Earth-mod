# Rivers at fine scales: pools, necks, dry arms and wrong flow

Date: 8 November 2026. The author played tall.22 in a 1:10 world (ring 4,007,936 blocks, height 1024, sea level 110,
linear heights about 9.7 m per block; river courses 3, lake outlines, river outlines 2, dams removed, Streams "all",
pool layout 11) and reported the places listed in [0063](../decisions/0063-rivers-follow-the-real-channel.md). Four
offline investigations ran the mod's own `TerrainModel` on the author's saved settings (and on the bench world t23a's)
with the real lake, river-outline and dam files and the raw GLO-90 water body mask (WBM), and printed maps of water,
levels and flow labels at each place. Line numbers are tall-glo90 at a83a127 / 8e5d6fc.

## The water mask's rivers at 1:10

- A 3″ WBM pixel is 9.28 blocks at 1:10 (13.9 × 9.3 blocks at the Gauja, where pixels are 4.5″ east–west). The Tiber at
  Rome, the Gauja and the Tigris below Amarah are at most one pixel wide in the WBM.
- `tools/bake_river_outlines.py` contoured the pixels with `contourpy` (`corner_mask=False`) and simplified by half a
  pixel, so every corner-only contact split the outline. Rome: 18 such contacts among 83 river pixels gave 25 rings of
  3–11 vertices (diamonds and triangles); the Gauja: 32 rings in an 8 × 5.6 km box. The game's smoothing
  (`LakeOutlines.cs` 231–232, two passes) rounds each diamond off but can't join them: the shore wobbles a median
  2.4–2.9 blocks, up to 10, around a smooth line.
- Coverage is broken: the Tigris has river pixels in only 19 of 109 rows over 10 km (never more than one per row); at
  Ust-Ishim N57E070 has 1,003 river pixels while GLO-90 shows the whole Irtysh at ≤ 43.5 m, and whole loops are land.

## How the channel gave way

- `TerrainModel.ApplyRiver` (1372–1381): `hw = min(hw, gap + OutlineTaper)` with `OutlineTaper` 2 narrowed the course's
  channel to the gap plus 2 blocks beside every outline piece, and drew no channel where the centre line lay in one.
  Water across the course at Rome: p10 4 blocks (14 without outlines); Tigris: median 16.5, p10 5 (31.5 without).
- Banks beside a piece are held (`LakeOutlines.cs:173`, HoldReach 1.5; `TerrainModel.cs` 1538–1546), so where pieces
  end the river closes to necks of 9–12 blocks and land lines 1–3 blocks wide (Irtysh: 57.791 N 70.9275 E).
- The nearest course claims a column by `distance − HalfWidth` (`RiverNetwork.cs` ~1511) at its full width, then fills
  only the narrowed channel. Across Europe 69 of 736 junctions left a dry gap; the Aniene also meets the Tiber 2 blocks
  high. Letting the next river claim what the nearest leaves dry (a test of `NearestExcept`) left 13, mostly single
  blocks.

## Courses off the real channel

- `snap_rivers_wbm.py` moved only reaches from 100 m³/s onto the mask, by a 4-connected cheapest path through its pixels,
  simplified by half a pixel: vertices on pixel centres, turns of 0°, 45° or 90° (median 45°), segments 12–74 blocks —
  the 45° channel walls.
- The Gauja (about 90 m³/s) is under that floor and keeps `rivers-glo90.bin`'s course, which cuts its meander with one
  140–174-block segment. The Tiber's course at Rome (reach 422819, Q 220, also in the courses-2 list) cuts across the neck
  of the loop by Acqua Acetosa to Ponte Milvio more than 16 blocks from the mask, so a full channel is dug there and the
  real loop is left as slivers of 0.3–3.5 pixels: separate pools at the nearest course's level (109 or 110) with beds
  falling half a block per block (the terraces), in GLO-90's 8 m floor one block deep. There is no island: the "side
  arm" is the real main channel.

## Flow direction

Every column's flow label is the direction of the nearest straight course piece (`RiverNetwork.cs:1652`; set at
`TerrainModel.cs:1426` for the channel and 1558 for outline water, rounded to 8 directions, passed to Hydrology through
`RiverFlowAt`). Measured against the water's own local axis:

| Site | Outline water off by > 22.5° / > 45° | Course channel off by > 22.5° / > 45° |
|---|---|---|
| Rome | 54 % / 43 % | 6 % / 0 % |
| Gauja | 68 % / 48 % | 8 % / 0 % |

The error is in mask water the course doesn't follow; smoothing the course's own direction alone barely helps (Rome
35 % → 29 %).

## Single places

- **Tiber ring (41.9373 N 12.4827 E):** a 2×2-pixel lake in the lake file (level 16 m, flag "missing in Earth"), 8 m
  above the Tiber's 7–8 m, probably a pool at the CONI Acqua Acetosa centre. `NaturalColumn` keeps it as an outline lake
  (beds 1, 2, 3 blocks under its water top 110), so its rim stands flush with the river's 109; pool layout 4's rule
  only acts on lakes more than 3 blocks above.
- **Tigris pools (31.42 N 47.38 E):** not dams — N31E047 is not in `dams-glo90-1.bin` and there is no HydroLAKES
  reservoir within 65 km. The flow width (8√1521 m = 312 m, 31 blocks) pinches to the scattered pixels; all water is at
  one level (109).
- **Al-Qurna (30.985 N 47.482 E):** course reach 553230/552878 (Q 244), the Hawizeh marsh outflow. `AridHydrology.Dry`
  holds (rain 34 mm, hot desert) because the reach's wet-source flow is 0.3 m³/s — the Karkheh's flow is lost upstream
  (traced to 31.68 N 48.54 E, still 0.3). The arid rule cuts a dry channel at water top − 2; it also left 2,881 columns of
  the mask's own river dry (1513–1517). The straight-edged ponds are mask lakes, probably fish ponds.
- **Rīgas HES** (hid 13083, 56.851 N 24.274 E, level 14.5 m): the river's given half width is 248 m; the in-zone cap
  (0.5 × the distance transform of water, min-filtered over 1 km, then averaged) brings it to 69–86 m near the dam, so
  the corridor across the wall is about 137 m against 490–690 m above and 340–550 m below; the tailrace banks count as
  tailwater ground, never as wall, so they stay at 6–13 m (`bake_dam_removal.py` 634, 714–727, 738, 747–748, 762–780).
  Pļaviņas has the same neck. Of 1,148 dam runs (643 reservoirs), 624 have a corridor under half the given width near
  the dam (323 with a half width ≥ 100 m, 87 ≥ 200 m; 134 under a quarter): Three Gorges 40 m against 545 m, Zhiguli
  93 m against 723 m.
- **Kakhovka:** the corridor is about 1.1 km wide over 36 km (ridge half width 524–571 m). Its width runs from w_in
  409 m (measured at Dnipro city, because the run spans the Dnipro and Kakhovka reservoirs) to w_out 519.5 m measured
  within 3 km of the dam, in the tailwater basin where the WBM is 1.32 km wide (about 0.3 km 25 km downstream, 0.66 km at
  Kherson). Its levels (49 / 26 / 0 m knots; 26 m is the course's old level at the DniproHES foot, inside Kakhovka's
  surface) stand up to 11.5 m above GLO-90's flat 14.5 m for about 100 km. In the world that shows as water flush with
  its banks; the width is the visible problem.
- **Canals:** the WBM has modern canals as rivers: Suez and Lake Timsah (against [0020](../decisions/0020-canal-gaps.md)),
  Panama's Culebra Cut, Kiel, the North Sea Canal, the Kakhovka irrigation canals.

## What follows

The rules built from this are 0063's. The flow-weighted width ("dredging") takes the author's two refinements: "dredge
my mind should follow flow rate and take account depth aka cross section area", and "where river narrows irl and goes
deeper we should try similar, but need to keep also some width": the channel keeps the flow's cross-section, narrowed
to the measured river down to half the flow's width and deepened in proportion.

The probes, maps and scripts are scratch work, not in the repository.
