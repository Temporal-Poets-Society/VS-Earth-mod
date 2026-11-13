# Water atlas: Earth's water against the GLO-90 water mask (6 November 2026)

The author asked for a systematic way to find water problems instead of one report at a time. The reports were the Patagonian fjord ending in a hard wall where the Grey glacier should be, missing river islands, the cut channel in New York, the Mississippi and the Ganges delta. They also gave a rule for dredging: "Purpose of dredging was to make river mouths bit wider and deeper where needed for ships", and it "should follow mainly natural river path".

Status: the author asked for all the fixes ("Yes do all the fixes and test again"). Rules 1–4 below are applied on branch `claude/water-atlas`. They are committed as 717fb18 (not merged), not built into a release and not tested in game. Rule 5 is not needed: the game already has the Aral Sea (see the correction below). Everything was measured offline with the harness and the atlas.

Later (7 November): 717fb18 and the side arms (e5d1d75) are on release/1.0, and release/1.0 was merged into the tall line (`claude/tall-glo90`, merge `3fc1d7d`) at the author's request ("Merge both to get right land, water and rivers"). There the data in place is not used: `terrain.bin`, `islands.bin`, `coasts.bin` and `rivers.bin` stay as published in the 1.0.2 data packs, and new worlds get the fixes as difference files read only by worlds that save them (Earth 0053 items 14–16: the sea referee, the courses moved into the mask's water, the side arms on the rivers on GLO-90).

Review page with 72 cards, each a Sentinel-2 picture beside a disagreement map, to mark "real problem" or "fine": https://claude.ai/artifact/88WXEmJcPjE7HGYNUKjbtL (private to the author). The marks are kept in the page's `verdicts` collection.

## Referee

The Copernicus GLO-90 Water Body Mask (WBM) is the `_WBM.tif` next to every GLO-90 height tile (`AUXFILES`). It is on the same 3″ grid, with 0 land, 1 ocean, 2 lake and 3 river. The editing flattened and flagged these water bodies while the heights were made, so it agrees with the heights Earth already uses. `tools/fetch_glo90_wbm.py` downloads all 26,475 tiles (575 MB) into `rawdata/copernicus-glo90-wbm`.

Known weaknesses:
- It counts salt flats and seasonal marshes as lakes: the Rann of Kutch, Uyuni, Eyre and Torrens.
- It shows dried salt lakes as land: Urmia, Poopó and Tuz were lower in 2011–2015 than in 1318.
- Floating ice and polar coasts are unreliable. Greenland and the high Arctic are counted apart.

## What was measured

`tools/water_atlas.py` compares the layers on a 30″ grid. Earth's sea is GSHHG L1 less the GSHHG lakes the game treats as sea. Its lakes are the baked Natural Earth lakes and the terrain grid's lake flags. Glaciers are RGI 6.2.

| Disagreement | Places | Area | What it mostly is |
|---|---:|---:|---|
| Sea over land | 3,383 | 23,606 km² | glacier fjords (Patagonia first by area × height), tidal flats |
| Land over sea | 82,698 | 375,679 km² | tidal flats, deltas, lagoons (Rann of Kutch, Limfjord, Mackenzie) |
| Lake over land | 22,513 | 422,754 km² | salt and dried lakes, old outlines, and the terminal lakes Earth revives (recount below) |
| Missing lakes | 47,651 | 479,996 km² | reservoirs left out on purpose, salt flats (mask weakness), small lakes (recount below) |
| Water over glaciers | 23 | 118 km² | glacier fronts; 222 more in Greenland and the Arctic |
| Lost river and lake islands | 12,719 of 42,122 | | islands of 0.25 km² or more under Earth water |

The counts above leave out Greenland and the high Arctic. Lists are in `preview/water-atlas/summary.txt`, `sites.json` and `islands.json`, with a world picture in `overview.png`.

### River courses and exit channels

`water_atlas.py --courses` walks every course at 100 m steps. A point is off water where the river's half width (4·√Q m) plus 90 m does not reach WBM water. The courses are the tall line's `rivers-glo90.bin` as this worktree's code places them (release/1.0 at `28f92a1`: RiverEnds, SeaChannels channel rules 2 on the built-in heights, no GLO-90 land heights) in a 1:10 world (`WORLD=5009920`) at world height 384, from the harness mode `EarthMapTests … 384 channels` with `RIVERS_FILE`. The tall line's 1:10 play style is 1024 high and linear, and tall.13 has channel rules 3, which decide the low-land cut on GLO-90 land from the built-in heights too (Earth 0053 item 13), so the cuts should match; route planning on the 2′ grid is the same in both, so the over-land routes apply to tall.13 as it stands.

| Discharge (1:10 world) | Length | Bed off water | Centre on land | Same with HydroRIVERS (rivers.bin) |
|---|---:|---:|---:|---|
| 500–1,000 m³/s | 116,213 km | 18.9 % | 26.7 % | 23.6 % / 45.6 % |
| 1,000–2,000 | 76,556 km | 14.9 % | 20.5 % | 16.9 % / 37.6 % |
| 2,000–5,000 | 59,818 km | 11.5 % | 16.2 % | 12.7 % / 31.0 % |
| 5,000–10,000 | 23,945 km | 13.7 % | 18.1 % | 14.6 % / 29.0 % |
| 10,000–30,000 | 19,803 km | 13.4 % | 20.1 % | 14.3 % / 27.4 % |
| 30,000 and up | 5,179 km | 0.5 % | 9.8 % | 0.6 % / 16.8 % |

Snapping to GLO-90 heights roughly halved the centre line on land. It barely moved the bed off water, because the courses still follow HydroRIVERS' 15″ vertices. Those vertices make the straight runs seen in New York, on the Mississippi and in the Ganges delta. At fine scales the game carves its own bed along them and cuts across shores, banks and islands.

Exit channels: 91 have a dredged bed off real water for 1 km or more, 350 km in all. 140 have 1 km or more of centre line on land, 871 km in all. The worst are in the Rhine–Meuse delta (51.78 N 4.43 E), the Ems (53.24 N 7.05 E), Albemarle Sound (35.98 N 76.13 W), the Scheldt (51.49 N 3.97 E) and Lagoa dos Patos (31.5 S 51.2 W). The channel is routed on the 2′ grid to open water and may cut land up to 8 m high, so it takes a shortcut over land where the real mouth bends.

## Fixes applied (6 November)

### Results

All figures are for a 1:10 world at world height 384, on the release code with the new data.

| Measure | Before | After |
|---|---:|---:|
| Exit channels whose bed leaves real water for 1 km or more (land pieces only) | 113, 1,041 km | 3, 4 km |
| Exit channel centre line on land (land pieces only) | 155, 1,546 km | 5, 8 km |
| Rivers of 500 m³/s and up: bed off real water | 46,210 km (15.3 %) | 40,127 km (13.1 %) |
| Rivers of 500 m³/s and up: centre line on land | 65,235 km (21.6 %) | 46,164 km (15.0 %) |
| Rivers of 30,000 m³/s and up: centre line on land | 9.8 % | 2.9 % |
| Earth sea on high WBM land, over the 1,107 changed tiles | 32,096 km² | 10,916 km² |

| Rivers of 100–300 m³/s: centre line on land | 50.2 % | 40.9 % |
| Rivers of 300–500 m³/s: centre line on land | 35.0 % | 21.9 % |

Of the river bed still off water, 74 % (29,806 of 40,126 km) has no mask water anywhere within the snapping radius. Those are rivers narrower than the mask can show, or seasonal ones. Snapping cannot fix them.

River audit with the tall line's rivers: blocked 105 → 106, uphill 81 → 80, falls of 2+ blocks 202 → 205 and rapids 603 → 601. With the release's rivers: blocked 101 → 101, uphill 78 → 76, falls 148 → 153 and rapids 377 → 373.

The test suite (release code and data, height 384) keeps its two old failures, "icy, seed 2026: -66.07,101.25" and "Lake Geneva: no steeper steps than before (6)". Two checks that passed before now fail, both because of the snapped release rivers:
- **"rules v3: the Tagus's islands stay whole":** 13 of 915 island samples are now under river water, at 38.896–38.900 N, 9.009–9.011 W. The snapped Tagus course runs in the real channel between the Mouchão islands, 1.4 km from them, and the river's game width reaches an island's edge. The old straight course ran 1.8 km away. Island rules 3 only protect island land over the grid's sea.
- **"no water walls beside the channels":** one dry column 3 blocks below sea level beside the sea, at 26.4503 N, 82.0661 W, by the channel of a 160 m³/s river (San Carlos Bay, Florida).

Neither test has been changed. Both are open.

Before and after pictures are in `preview/water-atlas/fixes/`, rendered by the harness at height 384: New York, Rotterdam, the Mississippi birdfoot, the Bangladesh delta and Balmaceda. They are also on the review page.

### River courses snapped to mask water

`tools/snap_rivers_wbm.py` moves each reach's centre line into the mask's water nearby (rule 1).

- **Search window:** a corridor of 500 m + 32·√Q m around the old course, at most 5 km.
- **Route:** the cheapest path through the 3″ mask between the reach's two ends. Water is cheap, mid-channel is cheaper and land is 8 times dearer.
- **Accepted only if** the share of the course on water rises by at least 5 points, and the new course is at most 1.5 times as long (plus 1 km).
- **Kept as before:**
  - Each reach's ends stay put, so the network's topology and the joins between reaches don't change.
  - Levels and fixed flags carry over by the share of the way along, and levels never rise downstream.
- **Size limits:**
  - Reaches from 100 m³/s are moved.
  - The game's small islands (`islands.bin`, after the sea referee) count as land even where the mask has water (tidal marsh), so no course is drawn over an island the game builds.
  - Below 300 m³/s, a reach moves only if at least 30 % of it already lies on mask water. That catches delta channels like the straight Sundarbans distributary (Q 227, 117 km). It keeps a narrow tributary running beside a big river from being pulled into the big river's water.

Results:
- `rivers-glo90.bin` (the tall line): 87,227 reaches moved, 346,866 km of course.
- The release's `rivers.bin`: 120,501 reaches moved. It replaces `earthdata/rivers.bin`; the original is in `preview/water-atlas/orig/`.
- The tall line's file is handed over as `preview/water-atlas/rivers-glo90-wbm.bin`.

### Dredging in place: exit channel rules 4

`SeaChannels` rules 4 carry out rule 2 of the proposals, as the author narrowed it: "Could we get rid of Dredging or do it only in real river bed or erode it if we need it larger".

The number is 4 on this branch too. Test build tall.13 already uses rules 3 for the GLO-90 low-land cut (Earth 0053 item 13), and 0052 proposes another rules 3. A world saved with either must not load as dredging in place, so the lines settle the numbers when they merge.

- **Grid route:** still planned on the 2′ grid, but over at most 2 land cells instead of 6. A longer land route was the cut over a polder or behind a dam.
- **Fine route:** the course is then laid on a grid 16 times finer (about 230 m) inside that corridor, using the GSHHG shore through `TerrainModel.CoastSideAt`.
  - Crossing land costs 20 times as much as water.
  - Water shallower than ship depth costs more, so the course keeps to the real channel.
- **Land cut:**
  - Only the first land stretch may be cut: it joins the river's end to the water, at most 12 fine cells.
  - Nowhere else does a channel cut land. Where the fine route fails, only the first grid piece may cut.
- **Widening by erosion:** near the mouth (8 half widths along the channel, up to 2 half widths across), low banks are worn down: no higher than 8 m, with tops at most 2 above sea. The channel is not cut through.
  - **Small worlds only (7 November).** At world height 256 the erosion turned about 60 km² of Mekong delta land into sea at the Bassac's mouth (9.53–9.57 N, 106.38–106.51 E), because the delta stands within 2 blocks of the sea there. The tall-line session found it; the suite only ran at 384 before. The author: "turn off for big worlds". The erosion now runs only where the world has no outline coasts, which are the small worlds where mouths come out too narrow. With outline coasts the shore is GSHHG's.
  - Suite at 256 after the change: the Mekong follows the outline at 100% (512k and 4M). Two failures remain: the Tagus islands (open) and "4M dalmatia: the rocky coast rises steeply" (1.2 blocks). The Dalmatia check fails the same way on release/1.0 before the water work (bbd00c0), so it comes from height 256, not from this. At 384 the suite is unchanged: the 4 known failures.
- **Count:** 855 of the 872 channels take the fine route, and 51 of them join their river end over land.
- **Old saves:** each world stores its channel rules. Worlds made with rules 2 keep them, and `UpgradeExistingWorldRiverChannels` moves one up.

**Risk for existing worlds:** the data changes are not versioned per world. New `coasts.bin`, `islands.bin`, `terrain.bin` and `rivers.bin` would reach existing saves in chunks generated after an update. Coasts and river courses could then meet the old ones with a step. Shipping them would need a gate, as the channel rules have, or a note that they are for new worlds.
- **Turning it off:** dredging could be switched off altogether as a world option. That has not been built.

### Mask weaknesses tagged

`water_atlas.py --tag` writes a `mask_note` into `sites.json` and a tally into `summary-tags.txt`. The review cards show the note.

| Tag | Rule | Places | Area |
|---|---|---:|---:|
| salt flat or playa | missing lake, under 250 mm of rain a year, flat (rough under 1 m) | 4,915 | 90,552 km² |
| dry salt lake | lake on land, under 400 mm, rough under 2 m | 1,788 | 47,558 km² |
| tidal flat, marsh or lagoon | sea and land disagree within 2 m of sea level, flat | 26,690 land over sea, 737 sea over land | |
| high Arctic | floating ice, the mask is unreliable | all polar sites | |

### Sea refereed by the mask

Proposed rule 3 is applied to the data in this worktree, not yet tested in game. `tools/sea_referee.py` rewrote `earthdata/coasts.bin`, `islands.bin` and `terrain.bin`. The originals are in `preview/water-atlas/orig/` and in git.

- **Made land:** Earth sea (coasts.bin's land and islands.bin's islands, read back as the game reads them) where the WBM has no ocean and GLO-90 stands at least 15 m high, or an RGI glacier covers half the 30″ cell. Strips 1–2 pixels wide are dropped (shoreline offsets), and so are areas under 0.2 km². An area also has to reach 60 m somewhere, which leaves out mangrove and swamp-forest canopy on low coasts (Amapá, the Amazon mouth, southern New Guinea: 15–40 m in this surface model). Greenland, the high Arctic and south of 60°S are left alone.
  - Result: 10,515 areas, 22,031 km². Patagonia accounts for 11,144 km². The rest is mostly Melanesia, Indonesia, Kamchatka and the Commander Islands, Vanuatu (Maewo, 209 km²) and New Zealand (the Rakaia, which GSHHG has as a sea inlet).
- **Opened as sea:** in the Patagonian archipelago and on many small islands, GSHHG is the mask shifted by 1–4 km, not a stray fjord. Filling only GSHHG's channel would have closed real straits. So within 2.5 km of a made-land area, Earth land that the WBM has as ocean becomes sea, 28,477 km² in all. A small piece of such land opens whole when it reaches that close: up to 30 km², or of any size on an islands.bin island. Shifted islands therefore move instead of leaving a strip behind.
- **Grid:** 111 grid cells go from sea to land, at the GLO-90 mean height. 1,268 go from land to sea (5 m deep), and the water levels within 3 cells are redone as the bake does. 93 islands.bin grid patches follow the change.
- **Check:** over the 1,107 tiles with areas, Earth sea on high WBM land fell from 32,096 to 10,916 km² (most of what is left is in strips and specks). Earth land on WBM ocean fell from 75,379 to 48,338 km².
- **Pictures:** `preview/water-atlas/sea-referee/{grey,balmaceda,vanuatu,kamchatka}.png`. The full list is in `sea-referee.txt` next to this file.

Risks:
- 123 HydroRIVERS segments of 100 m³/s or more now end on new land.
- 612 changed cells lie in islands.bin patches.
- The rest of the coast is still GSHHG, which can leave a step where the two meet.
- **Open, found on the tall line (7 November):** GSHHG land over WBM ocean stays land more than 2.5 km from made land. Example: the sound arm north-east of the Patagonia spot (-51.56 to -51.59, -72.79 to -72.69), where the WBM has ocean and GLO-90 reads 0.00 m. Earth keeps it one block above the sea: about 17 km² at 1:100, a strip up to 5 km wide. On GLO-90 heights it is a dead-flat plain, and beach sand once the outline shift puts the sea beside it.
  - The tall-line session suggests: GSHHG land where the WBM is ocean and GLO-90 is 0 m becomes sea.
  - Not decided. Worldwide, Earth land on WBM ocean is 375,679 km² (the atlas's "land over sea"), mostly tidal flats, deltas and lagoons. A rule this broad would need its effect on those measured first (the Wadden Sea, the Rann of Kutch, delta flats).


### Lakes recounted: revived lakes and reservoirs

The atlas now counts `lakes.bin`'s 11,133 grid cells of revived terminal lakes as Earth lakes. It also tags what is missing on purpose.

**Reservoirs:** Earth leaves modern reservoirs out ("No modern dams", decision 0034).
- Natural Earth reservoirs and HydroLAKES reservoirs are removed in the bake. The river bake replaces each reservoir's flat water with a natural slope (Three Gorges, Lake Nasser, Kariba, the Volga and Dnieper cascades).
- Dammed sea arms such as the IJsselmeer are sea again.
- Natural lakes that a dam only regulates (Mjøsa, Inari) stay.
- The atlas used to miss a reservoir when its dam lay beyond the part the mask flags (Krasnoyarsk, Cahora Bassa). It now looks for a HydroLAKES dam within about one reservoir length.

| Missing lakes (outside the high Arctic) | Places | Area |
|---|---:|---:|
| All | 47,651 | 479,996 km² |
| Reservoirs, left out on purpose | 4,391 | 153,250 km² |
| Natural lakes raised by a dam: the raised part, left out on purpose | 1,190 | 21,796 km² |
| Salt flats and playas the mask counts as lakes | 4,837 | 66,182 km² |
| Not explained: real gaps | 37,233 | 238,768 km² |

The real gaps are mostly small lakes: 93,311 km² of them under 10 km² each, and 81,395 km² between 10 and 100 km². Natural Earth's 1:10m lakes leave those out.

The largest real gaps:
- **Natural lakes and lagoons:** Chilika Lake (19.7 N 85.3 E), Songkhla Lake (7.6 N 100.3 E), Lake Chilwa (15.3 S 35.7 E), Lake Rukwa (7.6 S 31.7 E), Laguna Mar Chiquita (30.5 S 62.4 W), Lagoa Mirim's neighbours (33.1 S 52.8 W) and the Amazon's floodplain lakes near Santarém (2.2 S 55.5 W).
- **Seasonal marshes:** the Rann of Kutch (10,942 km²) and the Mweru Wantipa swamps.
- **Modern and missed:** Yellow River delta salt pans and fish ponds, and Williston Lake, a reservoir whose dam lies too far off for the new test.

Lake over land now includes 91,709 km² (180 places) where Earth's terminal lakes cover today's dry beds: the revived Aral Sea and others, and salt lakes such as Kati Thanda–Lake Eyre filled as in wet years. That is on purpose.

### Doles sala and Rīgas HES (Daugava)

The author asked whether this stretch is right. The pictures are in the Sentinel-2 and mask comparison (scratch).
- **Rīgas HES:** correct for 1318. Its reservoir is left out, the Daugava runs at its natural level, and the suite's "Daugava Riga" and "Daugava Plavinas" checks find no walls or bad steps. Snapping left the course alone here, so it was not pulled into the reservoir.
- **Doles sala: was missing; now in `earthdata/rivers.bin` as a side arm (see below).** Below the dam the Daugava splits: the main arm goes northwest to Riga, and the southern arm (Sausā Daugava) curves around Doles sala. Earth draws only the main arm, so Doles sala is part of the south bank.
  - The cause is general. HydroRIVERS has one line per river and no side arms, and Earth makes river islands only in deltas and lakes.
  - The water mask does show the arm, so side arms are built from it (next section).
- **On the tall line:** GLO-90 heights include the dam itself and the reservoir's flat surface. Decision 0051 records this: a reservoir valley becomes a plain behind a dam wall.

### River side arms (installed in rivers.bin, committed as e5d1d75)

The author asked to try building side arms from the mask, starting with Doles sala. `tools/river_arms.py` does it:
- **Islands:** land in the mask, 0.5 to 400 km², within 400 m of a river of 200 m³/s or more, not touching the sea. Small gaps in the water (up to 600 m bridged in all) are closed first, because embankments cut some arms off today. The Sausā Daugava is cut at Rīgas HES.
- **The arm:** the cheapest way round the island through mask water, from the main river's course above the island to its course below it. The main course between is blocked.
- **Kept only if:**
  - it leaves the course by 500 m or more;
  - at least half of it is mask river water;
  - it isn't more than three times the main course's length;
  - it isn't already in the data, and doesn't overlap another arm.
  - Stretches where the river is held at a lake's or the sea's level are skipped; the game's water covers them already.
- **In the data:** each arm is a new reach. Its discharge is its width's share of the river (15 to 50%). It flows into the main river where it rejoins, and that reach is split there. Its level follows the main river's own profile, entered 3 km below the split and left 3 km above the join. The file keeps the game's flow order: every reach flows into a later one.

**Worldwide:** 2,598 arms, 22,257 km, around 24,800 km² of islands (2,140 under 10 km², 39 over 100 km², the largest on the Volga at Astrakhan, the Paraná, the Amazon and the Kolyma). 16,551 mask islands were looked at. The commonest reasons for leaving one out: no way round through water (3,146), the ends outside the tile (2,025) and the arm too close to the course (973).

**Checks** (release, 1:10 world), with the arms installed as `earthdata/rivers.bin` (the same file as `preview/water-atlas/rivers-arms.bin`):

| | Without arms | With arms |
|---|---:|---:|
| Centre line in water | 99.90% | 99.91% |
| Blocked | 101 | 101 |
| Running uphill | 76 | 163 (88 of them on arms) |
| Falls of 2+ blocks | 153 | 155 |
| Suite failures | the 4 known | the same 4 |

The uphill spots on arms are one-block steps where an arm leaves or rejoins its river. Each reach gets its own stepped surface in the game, and at a confluence the lowest inflow sets the level. Level rules tried: holding the split's level gave 157; the main profile without inset gave 103; 3 km inset gave 88; lowering the arm further made it worse. Removing the rest would need the game to treat an arm's ends as one water body with the main river.

**The renders** match the mask: Doles sala, Yangzhong on the Yangtze, the Volga at Astrakhan, the Ganges at Patna and the Amazon above Macapá.

**Not done:**
- Installed in `earthdata/rivers.bin` at the author's request ("Yes, install them"), not committed. The file before the arms is the snapped set from 717fb18. Existing worlds get the arms in chunks generated from now on (the data files aren't versioned per world), so an arm can end at a chunk border in an old world.
- Not in a release package: the Earth Water Data package would have to be rebuilt to ship them.
- The tall line's `rivers-glo90-wbm.bin` has them from test build 1.0.3-tall.16 (Earth 0053 item 16): the same tool on it gives 2,408 arms, 19,669 km, shipped as `earthdata/rivers-glo90-arms.bin` for new worlds (river courses rules 3).
- Straight modern canals are not told apart. The river-water share and the 500 m offset keep most out, but none are checked.

## Causes, by report

- **Patagonia (Grey, Balmaceda):** GSHHG draws sea up the fjords to where the glaciers were decades ago. GLO-90 and RGI have ice and land there at 300–900 m, and the sea is cut down through it. This class ranks first.
- **Missing river islands:** Earth's river and lake water covers islands that the mask keeps as land, either through the river width drawn along a course off the main channel or through lake polygons.
- **New York, Mississippi, Ganges delta:** the river courses' straight runs, plus exit channels that take the shortest way to open water. It is not the dredging depth.

## Proposed rules

Rules 1–4 are applied as described above: rule 1 from 100 m³/s instead of 500, and rule 2 as exit channel rules 4 with erosion. Rule 5 is deferred.

1. **River courses snapped to mask water:** where a course of 500 m³/s or more runs beside mask river or lake water, move it into that water (centre line), keeping topology. This would also bring back islands the course now crosses.
2. **Dredging only in place:** exit channels follow the real mouth through mask water and only widen it to the ship width and deepen it to ShipDepth. They never take a new route over land. Where mask water is missing or too narrow, they widen around the existing course. On the tall line this would be channel rules 4 (tall.13, installed, has rules 3); 0052's unmerged rules would then need another number.
3. **Sea refereed by the mask:** where GSHHG sea lies on mask land above a few metres, especially over RGI glaciers, Earth uses land or ice. Tidal flats stay as they are unless reviewed.
4. **Mask weaknesses tagged:** salt flats, dried and frozen lakes (flat, at lake level) and polar ice are not counted as missing or wrong water.
5. ~~**The Aral Sea** as it was in 1318~~: already in the game, see below.

### Correction: the Aral Sea is not missing

The atlas built Earth's lakes from Natural Earth and the grid's lake flags only. It did not read `lakes.bin`, where Earth revives the terminal lakes at their level before 1960 (decision 0005): the Aral Sea at 53 m, Lop Nur, the Hamun lakes, Lake Chad and others. A harness render at 45.1 N 59.7 E shows the full Aral Sea, and the Amu Darya is led into it.

The atlas's 2,725 km² "missing lake" there was the mask's remnant of the sea, inside Earth's lake. The atlas now reads `lakes.bin` (see the recount below).

GLO-90's dried sea floor fits that level. Flooded to 53 m, the basin holds 65,889 km², close to the recorded 1960 area of about 67,000 km². At 45 m it would hold 49,752 km², at 40 m 37,950, at 35 m 27,207 and at 31 m 15,339.

One open question for whoever owns lakes: the sea may have been lower around the 14th century than in 1960. Medieval remains, such as the Kerderi mausoleum, lie on today's dry sea floor. Decision 0005 chose the 1960 level, and nothing here changes it.

## Reproduce

```
.venv/Scripts/python -I tools/fetch_glo90_wbm.py
.venv/Scripts/python -I tools/water_atlas.py --workers 16
.venv/Scripts/python -I tools/sea_referee.py --write   # then --verify and --pictures
.venv/Scripts/python -I tools/snap_rivers_wbm.py <rivers.bin> <out.bin> --workers 10
.venv/Scripts/python -I tools/water_atlas.py --tag
dotnet build tests/EarthMapTests -c Release
COASTS=1 RIVERS_FILE=<snapped rivers-glo90> WORLD=5009920 dotnet <EarthMapTests.dll> <repo> 384 channels   # CHANNELRULES=2: the old rules
.venv/Scripts/python -I tools/water_atlas.py --courses --channels tests/out/channels.json --rivers tests/out/game-rivers.bin --ends tests/out/game-river-ends.bin --rivers-base earthdata/rivers.bin
.venv/Scripts/python -I tools/water_atlas_cards.py tests/out/game-rivers.bin tests/out/channels.json --per-class 8 --bands <bands.txt>
```

Satellite pictures: Sentinel-2 cloudless 2016 by EOX IT Services (s2maps.eu), CC BY 4.0. They are fetched for review only and not shipped. Google imagery may be viewed by hand only; its terms forbid automated analysis.
