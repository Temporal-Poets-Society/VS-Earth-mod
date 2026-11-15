# 0064: Coasts and waters as of 1318 ("works after 1318")

Status: **agreed**; step 1 (the Netherlands), the canals (with 0069) and works rules 3 (EA-0042) and 4 (EA-0053, EA-0057) built, in Earth 2.0.0, new worlds only. Steps 2 and 4 and the rest of step 3 (the drained lakes, the Aral as of 1318, the Bitter Lakes) not built. Date: 2026-11-08. The survey, probe tables
and sources are in [the research note](../research/coasts-1318-2026-11-08.md).

## What the author asked

Found on 8 November while fixing beach sand near Suez (0053 item 20): new worlds take today's coasts and waters. The
Suez Canal comes in as lake water from the water mask's outlines, and the Dutch polders are dry land below the sea.
Surveyed and proposed the same day. The author's answers to the six questions put to them:

1. "Yes": undo human works made after 1318 and keep natural change after 1318.
2. Sunken peat land (Holland's peat polders, the Fens, the Sacramento delta): "i guess shallow water if that would be
   real one". It wasn't water in 1318: these were fen and bog at about sea level, with open water only in the meres and
   lakes. So: fen at sea level, and water where the 1318 maps have water.
3. A first pass, with the polder rule measured before it is adopted: "iterate as needed".
4. Downloads (the RCE 1250 map, Zhang's reclamation layer, the Valley of Mexico 1519 outline, Global Dam Watch): "yes".
5. Reservoirs older than 1318 kept, from a list checked one by one: "yes".
6. The Aral and the Bitter Lakes: "As in 14-15th century". The Aral stood at about 29 m (the Kerderi low, 12th–15th
   century: the Amu Darya ran mostly to the Sarykamysh hollow after Khwarezm's dike near Urgench was neglected from
   1221); the Bitter Lakes were a dry salt basin until the canal flooded them in 1869.

## Decision

New worlds only (a saved rule version; older worlds keep their coasts, lakes and heights). Human works after 1318 are
undone; natural change after 1318 (storm floods such as 1362 and 1421, the Yellow River's 1855 shift) stays as today's
data has it. Works of 1318 or before stay, and are protected where a rule would remove them.

| Kind | 1318 | Today in a new world | Becomes |
|---|---|---|---|
| Reclaimed sea and tidal flats | sea | land (GSHHG, or sea referee 2's added land) | sea |
| Drained lakes and meres | lake | land at sea − 1 | lake at its level |
| Sunken peat and marsh | fen at about sea level | dry land at sea − 1 | fen (wet ground at sea level) |
| Canals, mine and pit lakes, peat-digging lakes, ponds | land | water | land |
| Reservoirs older than 1318 | water | dropped by 0053 items 17–19 | kept |
| The Aral | about 29 m | 53 m (0005) | about 29 m |
| The Bitter Lakes | dry salt basin | salt lake (0020, the grid's) | dry salt flat |

Order of work (each step probed against today's world before the next):

1. **The Netherlands** from the RCE paleogeographic maps (Vos et al., CC BY 4.0). Built, below.
2. Reclamation since 1985 worldwide: sea referee 2's added land that Zhang's reclamation layer dates is GSHHG's sea
   again; natural accretion stays.
3. Drained lakes elsewhere (Valley of Mexico, Fucino, Copais, Hachirōgata, Lammefjord, the Fens' meres), canals (with
   0063, which already leaves Suez and Panama out), the Aral, the reservoir keep list. The Bitter Lakes are already
   dry land in new worlds: 0063's channel rules 1 strip the lakes in the Suez and Panama canal zones (probe after the
   merge: the Great Bitter Lake all land). Whether they need a salt-flat surface is still open.
4. The polder rule (land below the sea reached from the sea) measured, then decided.

## Step 1: the Netherlands (built)

**Data.** `tools/bake_works_1318.py` compares the RCE maps of 1250, 1500 and 2000 AD class by class and writes
`earthdata/works-1318.bin` (101,317 bytes; in the main mod, like `outlets.bin`). The file has the lake outlines' layout
(`LakeOutlineData`, 1° tiles, 1/2400° vertices) under its own magic `EMWK`. Each area's kind is in its flags
(`Works1318Data`: 16 sea, 32 lake at its level, 64 land, 128 fen, which is not used yet). 1250 is the map nearest 1318;
1500 adds the natural change after it.

| Kind | Rule | Area | Largest |
|---|---|---|---|
| Sea | open water or tidal flats in 1500, or in 1250 and not land by 1500, ∩ drained, embanked, built-over or inland water in 2000 | 5,541 km², 104 areas | the Zuiderzee and the IJ over the Zuiderzee polders and the Wieringermeer (3,985 km² with the IJsselmeer), the Delta's arms, the Lauwerszee, the Zijpe |
| Lake | inland water in 1250 or 1500 ∩ drained, embanked or built-over in 2000, outside the sea | 486 km², 153 | the Schermer, Beemster, Haarlemmermeer, Purmer, Wormer, the Grote Waard drowned in 1421 |
| Land | inland water in 2000 ∩ land in 1250 and 1500, apart from pieces touching the sea | 225 km², 398 | Loosdrecht, Belter- and Beulakerwijde, Reeuwijk, Westeinder, Vinkeveen, the Maas gravel pits |

- Big rivers are inland water on all three maps, so they stay. Today's natural classes (dunes, salt marsh, tidal flats)
  stay, so the Biesbosch and the Dollard are as today.
- 1500 water that is natural land by 2000 (silted) is not made a lake again.
- Dug-lake pieces that touch the restored sea (53.6 km², by Gaasterland) are the Zuiderzee eating into the peat after
  1500, a natural change, so they count as sea.
- Sea of 1250 that is land by 1500 (734 km² embanked, 2 km² drained) stays land. Mostly it was salt marsh or already
  diked by 1318: the Middelzee and Zeeland's 14th-century polders. Counting it as sea instead gives 6,363 km².
- Holland's sunken peat polders need no fen for 1318. They were drained peat land at about sea level then, and the
  game already holds land below the sea at the sea's level.

**Game.** `EarthParams.Works1318` (106) is 1 in new worlds whose Earth build has the file. Older saves read 0.

- It applies where the coasts come from their outlines (`Coasts`). `TerrainModel.Works` is a `LakeOutlineLayer` over
  the file with the coast's reach.
- The coast's signed distance takes the areas in (`Works1318Data.Combine`): the union with the sea areas, then with the
  land areas. The same distance feeds the coast columns, the ocean map (`CoastLandAt`) and the river exits
  (`CoastSideAt`, SeaChannels), so all three agree.
- Inside a sea or land area the outline decides alone. Today's outline lakes, grid lakes, river outlines and (on land)
  the grid's sea are not drawn there.
- A lake area is an outline lake at its level (0 m for every Dutch one). It uses the outline lakes' bed, at least 3
  blocks deep a few blocks out.
- The sea reaches `MinSeaDepth` (6 blocks) a Reach out, as every sea does, so the Zuiderzee is deeper than its real
  3 to 5 m.

**Checked.** EarthMapTests `works after 1318` covers the distance rule and the shipped file at eleven places; all
pass. The probe used a new 1:10 tall.22 world model built from this branch's code, with the file on and off; its maps
are in the research note's folder (`probe-nl-step1-*.txt`).

- In 121 columns at each place: Flevoland, the Wieringermeer and the Lauwerszee go from all land to all sea. The
  Beemster and the Haarlemmermeer go from land to lake. Loosdrecht's lakes, which today's world draws as grid sea, go to
  land. The Middelzee stays land.
- The maps show Urk and Schokland as islands, the IJ open, Zeeland as islands between tidal arms, and North Holland's
  lakes back.
- Cost: a 12,221-column map of the Dutch tiles takes 114 ms instead of 91 ms (about 2 µs a column). Outside the twelve
  tiles the layer only looks up a tile state.

**Open.**

- Beach sand: before the merge, almost all Dutch low land came out as beach sand, with or without the file. GLO-90
  rules 5 (0053 item 20, in release/1.0 since `63fe489`) fixes that. After merging release/1.0 `20e86e9` (the tall line,
  0063's channel rules 1, 0065) into the branch, the quick tests pass and the probe with channel rules 1 and the file
  (`probe-nl-step1-merged-20e86e9-on.txt`) keeps the sea (4,734 columns in the Zuiderzee map) and lakes (272), with
  beach at 427 columns (4,558 before rules 5). The conflicts were all "keep both": in `NaturalColumn` the 1318 areas
  and 0063's canal zone each skip today's outline lakes and river outlines where they apply. That probe ran with the
  river outlines file v2, because v3 (0063) isn't on this machine.
- Frisian lakes such as the Fluessen are grid sea in today's world too (water both ways). This is an older grid
  issue, not part of this step.
- Not in the file: the Ems–Dollard on the German side, Belgian Flanders (RCE covers the Netherlands only); fen marking
  (the Fens, the Sacramento delta, step 3); a world option for today's coasts.
- The file is unreleased and has version 1. A later bake that moves areas a saved world already has needs a new
  version beside it.

## Works rules 3: no ridges along today's moles (EA-0042, 13 November 2026)

Found at 1:100 off Rotterdam (EA-0042, after 0071 took away the river exit channel that had covered it): the SEA areas
cover today's port land but not today's water inside it (the basins, the Nieuwe Waterweg) nor the slivers between areas
that the 90 m raster lost. There GSHHG's coarse outline says land, so the water mask drew today's basins as river water
with dry beach strips between them, and inside the areas the sea floor was measured from every hole's edge: shallow
ridges of 1-3 blocks along the moles in water 5-10 deep. With works rules 3 (`Works1318Data.RulesVersion` 3, new worlds;
`TerrainModel.WorksRules`):

1. A place outside every SEA area with a SEA area within the coast's reach both ways along one of four axes, and no
   LAND area first, is part of that sea (`TerrainModel.EnclosedBySea`): the basins, the river and the slivers between
   the areas are the sea of 1318, and today's river and lakes are not drawn there.
2. On the sea's side near a SEA area the coast's distance is measured to the land of 1318 (`CoastDistanceAt`: LAND
   areas and the outline's land outside SEA areas, searched in rings up to the reach) at every place that read
   `Works1318Data.Combine`: the coast profile, the shore strip and the river mouths' shelf.

Probe at 1:100 (51.94-52.02 N 4.00-4.20 E): neighbouring water columns 3 or more blocks apart in depth 230 -> 112,
open sea against mask water 63 -> 1; the rest is the edge of the Nieuwe Waterweg course's trough (the river's own rules).
Flevoland (52.30-52.60 N 5.20-5.75 E): no 3-block steps with either rules. Not covered: Maasvlakte 2 (2008-2013) is not
in the RCE map of 2000, so it stays land (works rules 4 below).

## Works rules 4: Maasvlakte 2 and four more canals (EA-0053, EA-0057, 13 November 2026)

The author, 13 November: fix the open issues that change new worlds' terrain before the release. Built
(`Works1318Data.RulesVersion` 4, new worlds; the file is now `earthdata/works-1318-2.bin`, file version 3, 143,044 bytes,
in the mod zip like its predecessor; no upload):

1. **Land made in the sea that the map of 2000 does not show** (`tools/bake_works_1318.py --post2000`, flags SEA|LATE).
   Only named works, in a box each: Maasvlakte (1965-2013), Rotterdam's port in the North Sea. Inside the box, the water
   mask's land (GLO-90 WBM class 0, which the game draws) on the sea of 1318 (sea in 1500, or in 1250 and not land by
   1500), closed by 600 m so its basins and lagoons are in, grown by 150 m: one area of 47.1 km², Maasvlakte 2 and the
   dunes heaped on Maasvlakte 1 (which the 2000 map has as natural high dune). Natural sandbank changes after 2000 stay.
   The island layer (`islands.bin`) draws no land inside such an area (`TerrainModel.ApplyIslands`); 0064's older SEA
   areas keep their islands (Urk, Schokland).
2. **Four canals OSM names differently** (0069 rule 6): `tools/fetch_osm_canals.py --qlever` (Overpass was down all day)
   finds them by their local names and these are undone like the others (LAND|CANAL|LATE): the Corinth Canal (Διώρυγα
   της Κορίνθου, 62 pixels), the White Sea–Baltic Canal (Беломорско-Балтийский канал, 414) and the Ismailia Canal
   (ترعة الإسماعيلية, 1,081). The **Nieuwe Waterweg** (1866-1872) is kept: its OSM line lies on open water (buitenwater)
   in the RCE maps of 1250, 1500 and 2000 and in no 0064 area. In 1318 it was the Maas mouth's open water, so undoing it
   as land would be wrong; the 1872 cut through the Hook's dunes is below the 90 m grid.

Older worlds skip the LATE areas (`Works1318Data.SkipFor`: rules 1 skip canals and LATE, rules 2 and 3 skip LATE), and
version 3 keeps every area of version 2 byte for byte in each tile (checked against `works-1318.bin`, which is gone from the
mod: the new file serves every world).

Checked: the map tests (Maasvlakte 2 at 51.961 N 3.980 E sea with rules 4 and land with rules 3; the three canals land),
river water tests (the flags and `SkipFor`); a depth probe at 1:100 over Maasvlakte (51.90-52.06 N 3.85-4.30 E): rules 3 left 1,292 pixels of land there,
rules 4 none inside the area; a slightly shallower ring, under water, follows the area's western edge (the island
layer's sea-floor distance; open, small); the bench, a new 1:10 world: open sea in every direction from
51.975 N 3.975 E.

## History

- 2026-11-08: survey and proposal (research note); the author's answers above; worktree `claude/works-1318`; downloads
  of the RCE 1250, 1500 and 2000 AD layers (WFS), Zhang's reclamation rasters (`changeyear`, `landuse`) and Global Dam
  Watch 1.0 into `rawdata/`.
- 2026-11-08: step 1 built: the bake, `earthdata/works-1318.bin`, `Works1318Data`, the coast combination in
  `TerrainModel`, save field 106 (free on all 56 local branches), the attribution, and a test. Probed against today's
  world. The 1500 map was added to the rules after the first bake, which made the Biesbosch (natural since 1421) dry
  land and the Middelzee sea.
- 2026-11-08: the author: "Commit and merge". Step 1 committed (`dd167e6`); release/1.0 `20e86e9` merged into the
  branch (`a704ed6`); the branch merged into release/1.0. Not pushed.

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: **agreed; step 1 (the Netherlands) built and on `main`; works rules 3 (EA-0042) and 4 (EA-0053, EA-0057) for new worlds** (8 November; `dd167e6` on
`claude/works-1318`, with release/1.0 `20e86e9` merged in as `a704ed6`; not pushed). Checked offline (probe and
tests); not in a test build, not seen in a game.
