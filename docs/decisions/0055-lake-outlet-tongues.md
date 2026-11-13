# 0055: Lakes end at their outlet (outlet tongues as land)

Status: implemented in source (`claude/lake-outlets`, from release/1.0), offline checked; not built into a release,
not played. New worlds only. Date: 2026-11-05. On the tall line (`claude/tall-glo90`) since test build
1.0.3-tall.13, loaded on the bench and installed in the author's game on 6 November 07:34 ([0053](0053-scales-included-glo90-and-glo90-rivers.md)
History); the two lines merged on 7 November at the author's request.

## What the author saw

Niagara Falls in the author's 1:10 world (10 m per block, height 1024, full GLO-90, Earth 1.0.3-tall.9): "there looks
to be missing something or not right". Above the falls a flat lake several kilometres wide where the river is, a
separate pond east of it, the falls at the lake's edge, and a long one-block wall beside the river below the falls.

## Cause

Natural Earth's `ne_10m_lakes` "Lake Erie" polygon runs on down its outflow: it holds the whole upper Niagara River
(Grand Island as a hole) and ends at 43.106°N, about 3 km past the Horseshoe Falls (43.0775°N), over the gorge.
HydroLAKES puts Erie's outlet (pour point) at Buffalo (42.904°N, 78.908°W).
- The bake rasterizes the polygon as Erie (2' cells, 370 blocks at 1:10): a lake at Erie's level 4–7 km wide over the
  river; the Tonawanda channel, cut off by the coarse grid, becomes a separate pond.
- The river data marks its points there as in the lake, so the river is held at Erie's level up to the lake's end.
- Past the listed fall the lower river runs along the raised lake. On the tall line, pool layout 4's lake rim
  ([0024](0024-hydrology-water-mod.md); lake rims test 2026-10-23) holds the lake in with a one-block rim: about 3.5 km
  of wall at 1:10, the "wall" in the screenshot. At 256k the same overreach was 62 columns.

This is not Niagara's alone. Any Natural Earth lake whose polygon runs on past its outlet down the river puts the
river under still lake water at the upper level.

## Decision

`tools/bake_earth.py outlet_tongues`, for every Natural Earth lake with a natural HydroLAKES pour point in it:
1. The outflow is the biggest HydroRIVERS reach (5 m³/s or more) starting within 0.03° of the pour point, followed
   downstream while it stays in the lake (gaps up to 5 km: the river data leaves the coarse polygon now and then).
2. A gate disk on that course, as far past the pour point as its radius (2 km, then 3, 4.5, 6, 8), cuts the lake
   (8-connected). The parts it cuts off from the main body that hold the course further on, with the gate's cells
   nearer to them, are the tongue. Detached pieces within 4 km, nearer the tongue than the main body and no wider
   inside than a channel (no cell over 2.5 km from their shore), go with it: the Tonawanda channel east of Grand
   Island, cut off by the 1' grid.
3. The tongue is land (ETOPO's surface) unless it is a chain of lakes: HydroLAKES lakes (pour points in it or within
   two cells, all eight ways) over a quarter of its area, or wider than 3.5 km on average (area / course length within
   a cell of it). Päijänne's tongue (9.3 km, Ruotsalainen and Konnivesi below it), Lake Winnipeg's (Playgreen Lake),
   Lake of the Woods, Tonlé Sap and Garry Lake stay lake; the upper Niagara, both channels round Grand Island, is
   3.3 km.

Checked and corrected on the way:
- Clearwater Lake (Wells Gray, BC) is one Natural Earth polygon but two HydroLAKES lakes at 680 m; the northern one's
  pour point lies at the polygon's north tip and was taken for the outlet, and the southern one's (the real outlet)
  two cells down and one across from the polygon's end, missed by a four-way count. The lower half of the lake was
  trimmed and the river audit found it running 3 blocks uphill below. Counting all eight ways keeps it, and seven
  other lake chains (Lake Bienville, Ozero Ergipskiy Sor on the Ob, Hill Island Lake, four unnamed).
- On the Churchill River at Stanley Mission (HydroLAKES 6005) a compact lake piece (2.8 km inside) joined the tongue
  as a "detached piece"; the inner-width limit leaves it lake.

`outlets.bin` holds the 2' grid cells that change (as `lakes.bin`). Worlds store `EarthParams.LakeOutlets` (ProtoMember
100): new worlds 1 when the file is there, older saves 0 and unchanged. With it, `EarthData.SetOutletOverlay` applies
the patches last, and `OutletData.ReleaseRiverPoints` stops holding river points in those cells at the lake's level
(they keep it as their valley level; pools, rapids and the listed falls are laid out through them as elsewhere). The
file is small and ships in the main mod, like `reservoirs-v1.json`; the published data packs don't change.

## Results

Bake: 2208 Natural Earth lakes with a HydroLAKES outlet; 114 run on past it, 31 tongues are trimmed (903 km² with
their channel pieces) and 83 kept as lake chains. `outlets.bin` is 3.8 kB: 1945 grid patches, 130 2' cells no longer
lake. Trimmed, largest first: HydroLAKES 13077 (56.9°N 32.8°E), Lake Erie (upper Niagara), Ramsey Lake, the
Churchill River at Stanley Mission, Lac Tasialujjuaq, Harrison Lake, Clay Lake, Pitt Lake, Chao Hu, Lake Kwania, Lake
Débo, Lake George (Florida), Lake Griffin, Lake Louise (Alaska), Pielinen, Lac Duvert, Nowyak Lake, Hjälmaren, Lac
Giffard, Parker Lake, Ozero Krasnoye, Hyde Lake and nine unnamed. Sheets and the full list:
[research/lake-outlets-2026-11-05](../research/lake-outlets-2026-11-05/) (`trimmed-tongues.png`,
`kept-lake-chains.png`, `outlets-report.json`, `niagara-1to10-before-after.png`).

Offline harness (`tests/EarthMapTests`, `OUTLETS=0/1`, the author's world size):
- Suite at 384: the same two failures as without the overlay (icy seed 2026 at -66.07,101.25; Lake Geneva "no steeper
  steps than before (6)"), both there on release/1.0 before this change. New checks pass: Lake Erie still a lake; the
  gorge, Chippawa, Tonawanda and Grand Island west channel not Erie; at all 14 listed falls no lake water above the
  river below.
- Niagara: river columns beside lake 46 → 0, lake curtain 33 → 0 columns; the falls one 11-block curtain, 45 columns.
- River audit: blocked 101 → 101, uphill 79 → 78 (one gone in Norway, none new), falls 149 → 148 (Harrison Lake's
  lake-to-river step is a river now). Niagara reads river y195 → river y184 instead of lake y195 → river y184.
- At 1:10 (probe render, 43.085,-79.04): Erie ends at Buffalo, one river to the falls, the pond east of it gone, no wall
  below.

## Not done

- Inflow tongues (a lake polygon running up the river that feeds it) are not looked for; they hold a river at the
  lake's level rather than above a lower river.
- The tall line (`claude/tall-glo90`) uses `rivers-glo90.bin` and pool layout 4's rim. Applied there in
  1.0.3-tall.13: the river release works on `rivers-glo90.bin` too, and Niagara on GLO-90 rivers ends Erie at Buffalo
  (0053 History).
- Existing worlds: no one-shot upgrade flag.
- The Tonawanda channel (east of Grand Island) becomes land with the tongue; only the river data's channel (west of
  the island) carries water.
- HydroLAKES polygons are not used (only its points are in rawdata); a chain of lakes is recognised by the pour
  points in or by the tongue. Lakes whose pour point lies further off can still be trimmed as river.
