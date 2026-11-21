# EA-0124: the revived terminal lakes in 2.2.0-rc.13, and step 3 of decision 0064 sized (21 November 2026)

Throwaway work for the Earth 2.2 release coordinator. Nothing here changes Earth's code; the probe lives on the
never-merged branch `claude/ea-0124-probe` (cut from rc.13, `c8d996c3`), folder `docs/research/ea-0124/probe`.

## How it was checked

`Ea0124Probe` (a trimmed copy of the EA-0148 probe) builds a new world's terrain model the way a server does
(WorldParamsBuilder's params for the `earth-10` and `earth` play styles, seed 386217) on the full GLO-90 store
(`work\ea-0148\store`, 26,475 tiles), and samples `GetColumn` on the 1:10 world's block grid; the 1:100 world at the
same latitudes and longitudes. Params as rc.13 makes them: RiverEnds 2, FineElevation 5, LakeOutlines 5, LakeRules 1,
RiverOutlines 5, ChannelRules 5, GenFixRules 5, RiverCourses 5, Works1318 5. Variant `noends` is the same world with
RiverEnds 0 (no lakes.bin overlay): the control.

- `overview/`: whole lakes, one sample per 40 blocks (about 400 m). Aral, Lake Chad, Lop Nur, Tulare Lake.
- `detail/`: Aral shores, one sample per 2 blocks (20 m): the Syr Darya mouth, Muynak (the Amu Darya), the Ustyurt
  cliff in the west, an eastern patch of low islands.
- `bitter/`: the Great Bitter Lake (every 4 blocks). `drained/`: the Valley of Mexico, Fucino, Copais, the Fens (every
  10 blocks).
- `analyse.py <dir> <place> [level seedlat seedlon]` prints the counts (`*-counts.txt`) and draws `<place>_<scale>_<variant>.png`.
- The raw samples (`*.bin`, 888 MB) were deleted after the analysis; to redo them, check out `claude/ea-0124-probe`
  (64ccf169), build `docs/research/ea-0124/probe`, and run it with `EARTH_STORE=C:/VS mods/work/ea-0148/store`,
  `EA0124_STRIDE` as in each `places-*.txt` header, e.g. `dotnet <Ea0124Probe.dll> <checkout> overview
  places-overview.txt 10,100 base,noends` (about a minute).

Map colours: blue = the grid's lake water (0005's terminal lake), cyan = the water mask's outline lakes, purple = river
water, yellow line = the world's river courses where they are not water, sand = beach, orange = dry ground (not
beach) below the lake's level inside its grid cells, grey = dry land shaded by GLO-90 height, red = a water-surface
step, magenta = lake water beside lower dry land. Maps: `map1-aral-1to10-without-and-with-0005.png`,
`map2-aral-shores-1to10-muynak-and-west-cliff.png`, `map3-chad-lopnur-tulare-1to10.png`.

## 1. The Aral in an rc.13 world

**It is there, whole, at 0005's 53 m.** At 1:10 its water top is Y 114 (sea 110), at 1:100 Y 115; one level
throughout. Lake water in the window: 68,303 km² at 1:10 (67,393 the grid's lake, 870 small mask lakes in the deltas
and on the shore, 39 river), 68,534 at 1:100; 0005's bake gave 65,640 km² on the 2′ grid, the real 1960 sea about
66,000–68,000. Salt water. Without river ends (`noends`) the same world has today's Aral as the grid's two salt lakes
(the North Aral at 41 m, the western basin at 27 m; 8,124 km²) and the eastern basin dry.

- **No steps, no water above land, no clash with the mask.** No two neighbouring lake columns with different water tops
  in any window at either scale; one lake column beside lower dry land in the whole lake at 1:10, two at 1:100. The
  water mask's outlines hold none of the Aral: today's remnants are the 2′ grid's salt lakes, which the outlines leave
  to the grid (StripGridLakes keeps salt lakes and terminal-lake levels), so 0005's patch simply replaces them. The
  mask's delta lakes at 52–55.5 m round to the lake's own water top (10 m is about a block near sea level at 1:10).
- **The rivers reach it.** The Amu Darya's course (q 1,590 m³/s) enters the lake at Muynak, about 43.76 N 59.28 E;
  the Syr Darya's (q 363 m³/s) at about 46.07 N 61.04 E, after crossing a lake lobe at 61.6 E. Both are water up to
  the lake at both scales.
- **The shore is the 2′ grid's, not GLO-90's.** The lake is a 2′ (3.7 km) grid lake laid on GLO-90 ground: water stands
  where the grid's ground is below 53 m; dry land beside it takes GLO-90's height, held at the water's top near the
  lake. So 4,646 km² of the water (6.8 %) lies where today's GLO-90 ground is above 53 m (the bed there is the grid's,
  lowered), and 801 km² of ground below 53 m that joins the lake stays dry. What a player sees:
  - on the flat south and east shores, cell-shaped land and bays: at Muynak a peninsula with a straight 3.7 km north
    shore and a straight east side, rectangular sand flats, the Syr Darya's bay with a square corner (map 2, left;
    `detail/aral-syr_10_base.png`);
  - under the Ustyurt cliff in the west the water stops 1–3 km short of the cliff; the gap is a flat beach exactly at
    the water's top (45,131 of 45,131 such columns in the window at 1:10 are beach at Y 114), where in 1960 the sea
    met the cliff (map 2, right);
  - low islands in the east (45.0 N 61.3 E) are flat beach-level flats pocked with single water columns (the shore's
    fine noise on ground within a few metres of the level); the 1960 east coast was a maze of low islands, so this
    reads as shoals rather than as an error.
- Both scales look the same (the counts differ by under 1 %).

**Other revived lakes** (overview, 1:10; 1:100 within 1 %): the same mechanism and the same look.

| Lake | Water in rc.13 (km²) | 0005's reference | GLO-90 fill at the level from the seed | Notes |
|---|---|---|---|---|
| Lake Chad (283 m) | 24,117 | 1963 about 25,000 | 20,818 | Chari and Komadugu reach it; staircase shore in the east, where today's dune-archipelago pools (mask lakes at 278–279.5 m) stay outside as separate ponds |
| Lop Nur (788 m) | 3,526 | 1930s about 3,000 | – | Tarim reaches it; blocky north and east shores; the concentric "ear" ridges are beach-level sand rings inside the lake |
| Tulare Lake (61 m) | 1,893 | 1870s about 1,800 | 1,562 | Kings River reaches it; blocky east shore |

**The Bitter Lakes:** already as 0064 wants them, apart from the surface. Channel rules 1 strip all water in the canal
zone: no water in a 26 × 26 km window at either scale; the lakes' bed is a dry plain at sea level (198 km² of GLO-90
below 1.5 m, 98 % of it at Y 110), ordinary ground, no beach. Not there: the pre-1869 basin's depth (a salt basin
several metres below the sea; GLO-90 has the canal's water surface at 0 m) and a salt-crust surface.

## 2. What new worlds have where the drained lakes were (rc.13, 1:10)

- **Valley of Mexico:** 42 km² of water, all modern: Zumpango's regulated lagoon (18 km², 2,243.5 m), Lago Nabor
  Carrillo (8.9 km², built 1982), Tláhuac-Xico (3.4 km², formed by subsidence since the 1980s), Texcoco's Caracol
  evaporation spiral (2.7 km²). Texcoco itself is missing: confirmed. A fill of today's GLO-90 from the old lake bed:
  470 km² below 2,236 m, 825 below 2,240, 1,157 below 2,245; GLO-90 is a surface model (buildings) and Mexico City has
  sunk up to about 10 m, so a fill is not the 1318 outline.
- **Fucino:** no water. GLO-90 fill: 97 km² below 655 m, 133 below 660, 149 below 665, 161 below 670: a clean basin; a
  fill at the old level gives a usable outline.
- **Copais:** no lake (9.8 km² of water at 72.5 m in the window is a modern reservoir to the east). GLO-90 fill: 112 km²
  below 93 m, 195+ below 95 m (runs out of the window).
- **The Fens:** no meres; the fen is at −3 to −1 m in GLO-90, and Holme Fen's birch wood stands at +9.7 m on Whittlesey
  Mere's bed (a surface model). A fill cannot draw the meres: traced outlines only.

## 3. The Aral at 29 m: what the level means here

Hypsometry of the 1960 basin from GLO-90 (connected to the western basin, km²): 28 m 3,905; **29 m 7,429**; 30 m
9,562; 31 m 13,722 (the eastern basin's lowest part joins); 33 m 20,510; 35 m 26,275; 42 m 43,914 (the North Aral joins);
53 m 64,459. The 2′ grid at the same levels: 4,091; **4,244**; 4,526; 14,852; 21,539; 27,212; 65,314. GLO-90's fill
at 29 m is already two basins: the western basin (about 3,900 km²) and a lobe of the eastern basin north-east of
Vozrozhdeniya (about 3,500 km²), much like the 2006–07 satellite Aral at 29.5 m, which the research takes as the best
analogue (map 4: `map4-aral-extents-53-31-29.png`, light blue today's 53 m lake, mid blue the GLO-90 fill at 31 m, dark
blue at 29 m). The 2′ grid at 29–30 m has only the western basin, and jumps to 14,852 km² at 31 m. GLO-90 shows the
western basin as its 2011–15 water surface (27 m) and the grid has no bathymetry under it either (26.97 m), so the
basin's depth comes from the lake rules, not data. In the game the level itself hardly shows: at 1:10 29 m and 31 m are
the same water top (Y 113), 53 m is Y 115; the level chooses the extent.

## 4. Sources (research by a helper agent: `research-drained-lakes.md`)

- **Aral, 1318:** about 29 m (range 27–30, Krivonogov et al. 2014): the medieval Syr Darya delta's edge, eastern-basin
  cores, and the Kerderi farms dry at 34 m. The western basin held the lake, the eastern basin a shallow lagoon; the
  Syr Darya reached it along the Kerderi braid across today's dry bed; the Small Aral was low (no high North Aral
  spilling south). No open published outline: draw it from a GLO-90 fill at about 29 m, checked against the 2006–08
  water in JRC Global Surface Water (free, credit "EC JRC/Google"). The Amu Darya was split between its Aral delta and
  the Daryalyk to the Sarykamysh, whose 1318 lake was probably low (about 9 m or less, like today's), Uzboy dry.
- **Bitter Lakes:** dry before 1869; a salt bed 13 × 5 km, up to 13.2 m thick, brine only in grooves (Morcos 1967).
  The floor's depth below the sea was not found in an open source.
- **Outlines:** OpenHistoricalMap (CC0, no source tags) has the five Valley of Mexico lakes (846 km² together,
  pre-1635), Fucino (141 km²), Copais (328 km², probably the wet season), Hula (13) and Antela (26). CDMX's `lago1519`
  is traced from a CC BY-SA Wikimedia map (share-alike). Others: public-domain survey maps (OS Old Series and
  Bodger 1786 for the Fens, Austrian and AMS sheets, USGS topoView, IGN Spain's MTN50 under CC BY 4.0, Danish and
  Swedish surveys). No global dataset has drained-lake outlines.
- **Subsidence:** filling today's heights to the old level fails at most big drained lakes: Mexico City sank more than
  9 m, Holme Fen about 4 m, Copais about 4 m, Fucino's lowest point about 3 m; GLO-90 is a surface model (Holme Fen's
  trees, Mexico City's buildings). Outlines plus a flat level are the way; a fill works for Fucino and as a check.
- **1318 is not always the pre-drainage state:** Fucino was very low in the 16th century and peaked in 1816 (1318 level
  uncertain, 655–660 m); Tulare, Owens and Winnemucca were near medieval lows; the Salton Sea basin was probably dry.

## 5. Step 3 sized

**How it fits Earth's pipeline.** In GLO-90 worlds (1:100 and finer, so the server's 1:10 world) a drained lake is a
LAKE area of the works of 1318 (decision 0064 step 1's machinery: `Works1318Data`, `TerrainModel.WorksAt`). It
already does most of what is needed: NaturalColumn draws a LAKE area as an outline lake at its own level before
anything else (today's mask lakes and reservoirs inside it go, e.g. Lago Nabor Carrillo inside Texcoco); CourseInLake
and LakeTopAt treat it as a lake, so courses run into it and draw nothing inside it; the salt flag
(`LakeOutlineData.FlagSalt`) makes it salt water. The works file ships in the mod zip (works-1318-3.bin today,
143 kB), so **no data upload**. Missing, as works rules 6 (`Works1318Data.RulesVersion` 6, new worlds only; older
worlds keep works rules 5 and skip the new areas through `SkipFor`):
1. the bake: `tools/bake_works_1318.py` reading a list of 1318 lakes (name, level, salt, depth, outline from a GeoJSON
   file or a GLO-90 fill within a box), written as `works-1318-4.bin` (file version 5);
2. shores: land beside an inland LAKE area held at its water top (as `OutlineShore` does for mask lakes); the Dutch
   lakes needed none (land below the sea is held at sea − 1), but Texcoco over sunken Mexico City and Fucino's bed
   would otherwise stand above lower land beside the water;
3. depth: a depth per area in the file (shallow Texcoco and the Fens meres, Fucino up to 18 m); today a LAKE area takes
   the old outline-lake rule (0.05 × distance to the shore up to 40 m, at least 3 blocks), and lake rules 1's real
   depths only reach the mask's lakes;
4. tests (EarthMapTests' works places), a probe at 1:10 and 1:100 with maps, the SavedWorldDigestTests pins.

Rivers: the 1:10 world's course list has no reach in the Valley of Mexico, Fucino or Copais windows, so the later
outlets (the Nochistongo cut and the Gran Canal, the Torlonia tunnel, the Karditsa tunnel) draw no river today; the
2.2 streams layer was not probed and must be. Re-routings (the Nene through Whittlesey Mere, the Syr Darya's Kerderi
braid, the Amu Darya to the Sarykamysh, the Aare, Linth and Kander) are course edits: later. Soil and plants: nothing
lake-specific beyond what mask lakes get; the Aral's green shore (`ShoreKm` 40 km) was set for the 1960 lake.

| Lake | 1318 state | Outline and level source (licence) | New file / upload | Fit | Rules | Days | Risks |
|---|---|---|---|---|---|---|---|
| **The Aral** | about 29 m; western basin and an eastern lagoon, about 7,400 km² on today's ground | level: Krivonogov et al. 2014 (cited only); outline: GLO-90 fill at 29 m (Copernicus, credited), checked against JRC GSW 2006–08 (credit) | `lakes-2.bin` (0005's file with the Aral at 29 m on the 2′ grid, 17 kB) and the Aral's LAKE area in works-1318-4.bin; both can ship in the zip (no upload) | the grid lake keeps 0005's river ends (rivers led into it), green shores and the coarse scales; the LAKE area gives GLO-90 shores where outlines decide, at the same level (no step) | RiverEnds 3 picks lakes-2.bin; works rules 6 | 1.5 (on top of works rules 6) | the Amu Darya's course runs over 60–100 km of dry eastern bed at 29 m: must be checked; the North Aral stays at today's 41 m (the Kokaral dam of 2005; its 1318 depth is unknown); the western basin's depth is the lake rules', not data; the 40 km shore width to shrink |
| **Valley of Mexico** (Texcoco, Xochimilco, Chalco, Xaltocan, Zumpango) | about 850 km²; Texcoco about 2,237–2,240 m and brackish, the others 1–3 m higher; mean depth about 1.5 m; no Tenochtitlan (founded 1325) | OHM (CC0), checked against Humboldt 1807/1811 (PD) and the Wikimedia 1519 map (CC BY-SA, a check only) | works-1318-4.bin (zip) | LAKE areas, one per lake and level; mask ponds and reservoirs inside go | works rules 6 | 0.5 | sunken, built-over ground: the bed and the shore hold carry it; the levels are within a block of each other at 1:10 |
| **Fucino** | 140–155 km², shore about 660 m (1318 possibly lower), endorheic, up to 18 m deep | OHM (CC0, 141 km²) or a GLO-90 fill at 660 m (133 km²) | works-1318-4.bin | LAKE area | works rules 6 | 0.25 | the 1318 level (655–660 m) |
| **Copais** | about 200–330 km² (seasonal), water 92–97 m, 1–5 m deep | OHM (CC0, 328 km², wet season) or a GLO-90 fill at 95–96 m (195–216 km²) | works-1318-4.bin | LAKE area | works rules 6 | 0.25 | 4 m of bed subsidence (a fill's middle too deep; the depth per area covers it) |
| **The Fens' meres** (Whittlesey 7.6–12 km², Soham about 5.5, Ugg, Ramsey, Benwick, Trundle, Brick) | water at about +1 m OD, 1–2 m deep, in fen at about sea level | traced from public-domain maps (OS Old Series via NLS, Bodger 1786, Moore 1658–1720; NLS scan terms to check) | works-1318-4.bin | LAKE areas at the sea's water top; the fen around is already at sea − 1 | works rules 6 | 0.5–1 | tracing accuracy (±200 m is ±20 blocks at 1:10); the small meres' sizes are unknown |
| **Hula, Antela** (cheap extras) | 13 and 26–36 km² | OHM (CC0) | works-1318-4.bin | LAKE areas | works rules 6 | 0.25 | none beyond the above |
| **The Bitter Lakes** | dry salt basin | Morcos 1967 | none | already dry in rc.13 (channel rules 1) | none | 0 for 2.2 | later: the basin's depth (no source found) and a salt-flat surface (no surface rule exists) |
| Works rules 6 code (bake input, shore hold, depth per area, skip flag) | | | | | works rules 6 | 1.5 | a new rules version late in 2.2's candidates |
| Probe, maps, tests, digest pins, the author's look | | | | | | 0.5–1 | |

**Total for 2.2:** about 4.5–5.5 days of work, one more release candidate and the author's look at 1:10 (Texcoco,
Fucino, the Aral).

**Later (2.3 or after), sized roughly:** the worldwide list in `research-drained-lakes.md` (Amik, Karla, Achinos,
Giannitsa, Xynias, Suğla, Bientina, La Janda, Kolindsund, Martin Mere, Hachirōgata, the Klamath pair, Comacchio's
reclaimed valli, Fetzara, Ecsed, the Jura lakes with the Aare, Dongting and Poyang): half a day to a day each once the
works rules 6 path exists, most from public-domain survey sheets; removals (the Salton Sea, Mareotis, Abu Qir,
Aydarkul, the Lake of Homs' raising): a LAND-area pass; 0005's other revived lakes at their 1318 levels (Tulare and
Owens at medieval lows); the river re-routings; the Bitter Lakes' salt basin; and the revived lakes' 2′ shores on
GLO-90 (each of 0005's lakes as a LAKE area from a GLO-90 fill, the Aral's method).

## 6. Recommendation

Build in 2.2, as one branch off `claude/terrain-2.2` and one release candidate: works rules 6 with the Valley of
Mexico, Fucino, Copais, the Fens' meres (Whittlesey and Soham at least), Hula and Antela, plus the Aral at 29 m
(RiverEnds 3 with lakes-2.bin and the Aral's LAKE area). Every file ships in the mod zip, so nothing waits on an
upload. Leave the Bitter Lakes as they are (already dry) and everything else for later. If 2.2's schedule can't take
five more days and a candidate, the smallest useful piece is the Aral alone (about 2 days: lakes-2.bin and RiverEnds 3,
on the 2′ grid only: the western basin, 4,244 km², with the cell-shaped shores seen at 53 m); Texcoco would then wait
for 2.3, which the public server's world would never get.
