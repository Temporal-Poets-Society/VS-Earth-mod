# EA-0148 step 1: the half-cell offset of lake and river water, measured offline (17 November 2026)

Earth 2.1.4 (main 1ac0f2d), branch `claude/ea-0148-measure`. Nothing shipped was rebaked or replaced; the moved outlines
exist only inside the probe. The probe and its scripts are in `probe/`, the numbers in `results/`, pictures in `images/`.

## Answer in short

- **Yes, it shows at 1:10, at full size.** In a new 2.1.4 world the lake and river water lies half a mask cell south-east
  of GLO-90's grid: 4.5–5.2 blocks south (Mississippi 2.8, where courses draw much of the water) and, east, 3.6–4.8
  blocks below 50°, 7.1–7.5 at 50–60°, 8.8–9.6 at 60–70°, 13.9 at 70°N (predicted 4.6 / 4.6 / 7.0 / 9.3 / 13.9). Lakes follow their outlines exactly (the world's water and the
  outline give the same numbers); the shore rules hide none of it. At 1:100 it is half a block south and 0.4–1.0 east.
- **A half-cell move puts the water on GLO-90's water.** With every outline vertex moved half a cell north-west, the
  offset falls to within ±0.8 blocks at 1:10 (Tamula +1.5 east; median 0.1) and ±0.1 at 1:100, at all 18 places with
  outline water to measure.
  Against GLO-90's flat water the north and south shores become alike: the water reaches about 3 blocks past the flat on
  both (a shore halfway between a water and a land point reaches at most half a cell past the last water point).
- **Products that must move together:** lake outlines, river outlines, the works of 1318's canal areas, the flow file's
  reader (the C# reader adds the half cell itself), and the snapped river courses (they are partly offset, unevenly).
  The dam height patches and the heights themselves are already right.
- **Built** on `claude/ea-0148-fix` (decision 0092): offsets under a block at 1:10 at all 18 places (section 5). The
  same branch ends rivers at the sea (EA-0160, decision 0093, section 6).

## Height path

Full GLO-90 (`FineElevation`, rules 5, the bounded Catmull-Rom surface) from the AgentBridge bench's store
`%APPDATA%\VintagestoryData-agentbridge\ModData\earth` (26,475 tiles, with `lakes-glo90-4.bin`,
`rivers-outline-glo90-4.bin`, `rivers-flow-glo90-1.bin`, `dams-glo90-3.bin`). Every window sampled read
GLO-90 only (`FineElevation.MissedReads` 0: no built-in heights). At 9 grid points per place (6 where the place is on a whole
degree) the world's height equals the raw Copernicus DEM cell on the pixel-is-point grid to within 0.5 m at all 20 places
(the store's whole metres); the raw tiles'
GeoTIFF keys are RasterPixelIsPoint with the tie point on the tile's north-west corner.

## How it was measured

`probe/Program.cs` builds a new world as a server does (SavedWorldDigestTests' Build; params from WorldParamsBuilder for
the play styles `earth-10`, 1:10 at height 1024, and `earth`, 1:100 at 384): GenFixRules 5, lake outlines 4, river
outlines 4, dams 3, flow 1, channel rules 5, courses 4. Around each place it samples a window of 445 × 557 blocks of the
1:10 world (0.04° × 0.05°), block by block, and the 1:100 world at the same points (a tenth of a block apart). Variants,
all in memory:

| variant | what moves half a cell north-west |
|---|---|
| shipped (`base`) | nothing |
| `shift` | lake and river outlines (vertices on a tile's edge stay on it, see question 4) |
| `shiftall` | and the works of 1318's canal areas |
| `shiftflow` | and the flow file (its tiles written at twice the resolution, each pixel from the mask cell it belongs to) |
| `shiftcourses` | and the inner vertices of every reach rivers-glo90-snap-2/-arms-2 moved, network rebuilt |

`probe/analyse.py` compares with the raw mask and DEM: truth M is the mask's nearest grid point (where a correct outline
puts the shore); truth F is GLO-90's surface within 0.5 m of the nearest mask water's level (the heights' flat water).
The displacement is the shift that makes most samples agree (inside a margin); the shore strips are the mean distance,
along columns and rows, from the truth's shore to the world's.

## 1. The offset at 1:10 and 1:100

Displacement of the world's water against the mask, blocks south / east (+ = south, east). "—": no single displacement
fits (at 1:100 big rivers are drawn from their courses, not the outlines).

| place | mask columns | half cell S / E at 1:10 | 1:10 shipped | 1:10 shifted | 1:100 shipped | 1:100 shifted |
|---|---|---|---|---|---|---|
| Näsijärvi, 62°N edge (EA-0117) | 600 | 4.6 / 9.3 | +4.7 / +9.6 | +0.2 / −0.6 | +0.5 / +0.9 | +0.0 / −0.0 |
| Näsijärvi, Tampere shore | 600 | 4.6 / 9.3 | +4.9 / +9.5 | +0.0 / +0.8 | +0.5 / +1.0 | −0.0 / +0.1 |
| Pielinen (Joensuu) | 600 | 4.6 / 9.3 | +4.8 / +8.8 | +0.0 / −0.4 | +0.5 / +0.9 | +0.0 / −0.0 |
| Kuurna reservoir | 600 | 4.6 / 9.3 | — (left out as a reservoir) | — | — | — |
| Mikkeli, 27°E edge (EA-0147) | 600 | 4.6 / 9.3 | +4.6 / +9.2 | −0.2 / +0.4 | +0.5 / +0.9 | −0.0 / +0.0 |
| Tamula, 27°E edge (EA-0147) | 800 | 4.6 / 7.0 | +4.6 / +7.5 | +0.0 / +1.5 | +0.5 / +0.7 | +0.0 / +0.1 |
| Daugava at Riga | 800 | 4.6 / 7.0 | +4.7 / +7.4 | −0.1 / +0.5 | — | — |
| Rhine at Cologne | 800 | 4.6 / 7.0 | +4.9 / +7.1 | +0.7 / +0.1 | — | — |
| Lena at Kyusyur, 70.7°N | 400 | 4.6 / 13.9 | +4.9 / +13.9 | −0.1 / +0.5 | — | — |
| Lake Brienz (mountain) | 1200 | 4.6 / 4.6 | +4.8 / +4.4 | −0.1 / +0.0 | +0.5 / +0.4 | −0.0 / +0.0 |
| Lake Tahoe (mountain, W) | 1200 | 4.6 / 4.6 | +4.9 / +4.8 | +0.1 / +0.1 | +0.5 / +0.5 | +0.0 / +0.0 |
| Titicaca at Puno (mountain, S, W) | 1200 | 4.6 / 4.6 | +4.5 / +4.5 | +0.0 / −0.1 | +0.5 / +0.5 | −0.0 / −0.0 |
| Wakatipu (mountain, S) | 1200 | 4.6 / 4.6 | +4.8 / +4.8 | +0.0 / +0.4 | +0.5 / +0.4 | −0.0 / −0.0 |
| Balaton at Siófok (lowland) | 1200 | 4.6 / 4.6 | +4.8 / +3.8 | −0.1 / −0.3 | +0.5 / +0.4 | −0.0 / −0.0 |
| Victoria at Kampala (tropics) | 1200 | 4.6 / 4.6 | +5.2 / +4.1 | +0.4 / −0.1 | +0.5 / +0.4 | +0.0 / −0.0 |
| Amazon at Manaus (tropics, S, W) | 1200 | 4.6 / 4.6 | +4.8 / +3.6 | +0.2 / −0.1 | — | — |
| Mississippi at Natchez (W) | 1200 | 4.6 / 4.6 | +2.8 / +4.7 | −0.1 / −0.1 | — | — |
| Danube at Budapest | 1200 | 4.6 / 4.6 | +4.7 / +4.7 | +0.2 / +0.2 | — | — |
| Kiel Canal (removed in 1318) | 800 | 4.6 / 7.0 | outlines alone: +4.9 / +6.7 | +0.0 / +0.6 | +0.5 / +0.7 | −0.1 / −0.0 |
| Bergen coast (sea, GSHHG) | 600 | 4.6 / 9.3 | +0.4 / −19.2 | unchanged | +0.5 / −1.6 | unchanged |

Same sign and size in both hemispheres, east and west, lowland and mountain; east-west it scales with the mask's
column spacing, as half a cell should. Bergen's sea coast is GSHHG's and has no offset of this kind.

Shore strips against GLO-90's flat water, blocks (north and west: + is a dry flat strip, − water onto the bank; south
and east: + is water up the bank). Lakes at 1:10:

| place | shipped N / S / W / E | shifted N / S / W / E |
|---|---|---|
| Näsijärvi, 62°N | −1 / +8 / −5 / +16 | −5 / +5 / −11 / +10 |
| Näsijärvi, Tampere | +2 / +8 / +5 / +26 | −3 / +2 / −3 / +8 |
| Pielinen | +1 / +7 / −1 / +11 | −2 / +4 / −6 / +4 |
| Mikkeli 27°E | +0 / +8 / −1 / +19 | −4 / +5 / −9 / +11 |
| Tamula 27°E | +0 / +4 / +5 / +9 | −3 / +0 / +0 / +8 |
| Brienz | +1 / +12 / +0 / +15 | −6 / +6 / −4 / +6 |
| Tahoe | +4 / +7 / −1 / +5 | +0 / +1 / −2 / +1 |
| Titicaca | +3 / +5 / +3 / +3 | −0 / +1 / −1 / +1 |
| Wakatipu | −1 / +12 / +3 / +10 | −5 / +5 / −8 / +8 |
| Victoria | +3 / +3 / +4 / +2 | −2 / +3 / +1 / +1 |
| median | +1 / +7.5 / +1.5 / +10.5 | −3 / +3.5 / −3.5 / +7 |

So the shipped lakes leave little dry flat on the north and west shores (−5 to +5 blocks, median 1–1.5) but put water
3–12 blocks up the south bank and 2–26 blocks up the east bank (the bank cut down to the lake bed). GLO-90's flat ends just past the last
water point, while a shore drawn from the mask lies halfway to the first land point; the shipped outlines add half a
cell on the south and east. Moved, the overreach is the same on the north and south shores (3 blocks); east-west 3.5 blocks west and 7 east, a lean
of about 2 blocks that the comparison with the mask does not show (there ±0.8). At 1:100 the shipped strips are
0.4–2.5 blocks on the south and east, about 0 on the north and west; moved, ±0.1–1.1 both ways. Rivers and the full
table: `results/tables.md`.

Pictures (1:10, 120 × 160 blocks, 3 pixels a block; blue water in both, red the world's water on the mask's land, yellow
mask water the world leaves dry; panels: shipped, then the variants in the order above):
`images/nasijarvi-62n.png`, `images/brienz.png`, `images/daugava-riga.png` (shipped | shift),
`images/mikkeli-27e.png` (the straight shore on 27°E, see 4), `images/rhine-cologne.png` and
`images/danube-budapest.png` (shipped | shift | shiftflow | shiftcourses).

## 2. What else moves out of line when only the outlines move

| check (1:10) | shipped | outlines moved | and canals | and flow | and courses |
|---|---|---|---|---|---|
| Kiel Canal: world water blocks in the window (canal removed in 1318) | 140 | 336 | 149 | 149 | 149 |
| flow against river outlines, blocks S / E (Daugava, Rhine, Lena, Mississippi) | −1.5..+0.8 / −0.6..+3.0 | +3.8..+9.5 / +6.0..+18.1 | same | −1.0..+1.2 / −1.0..+1.5 | same |
| Rhine at Cologne: world water blocks | 34,012 | 30,933 | 30,933 | 30,933 | 33,781 |
| Mississippi at Natchez | 53,876 | 51,616 | 51,616 | 51,616 | 52,682 |
| Danube at Budapest | 29,127 | 26,062 | 26,062 | 26,062 | 26,375 |

- **Works of 1318:** the canal areas are made from the mask's river cells (+0.5 centres), so with the outlines alone moved
  a strip of canal water half a cell wide comes back along the Kiel Canal; moving the canal areas too removes it.
- **Flow:** `RiverFlowData.At` reads pixel (i, j) at its cell centre (`fi = (lat0 + 1 - lat) * H - 0.5`, `PixelAt` floors),
  half a cell south-east of the mask cell it was solved on. Today that matches the outlines (offset ≈ 0); move the
  outlines alone and the flow is half a cell off them; read on the pixel-is-point grid it is back in line. No new flow
  data is needed for this; the flow's reroute vertices (+0.5 centres) move too.
- **Courses:** the narrow-piece rule (a narrow mask piece is dropped where the course draws) reads the courses; with them
  left behind, the Rhine at Cologne lost a harbour piece (−9 %; with every narrow piece kept the loss is 1 %), and moving
  the courses brought it back. The snapped courses are not a clean half cell off, though: over 26 squares with big
  rivers (21,500 vertices, `probe/courses.py`, `results/courses.json`), the inner vertices of snapped reaches lie
  +0.23 ± 0.05 cells south and +0.26 ± 0.05 east of the mask water's middle; within 1 km of a reach's end +0.3–0.4,
  1–3 km +0.25, beyond 3 km about 0. Reach ends and arm vertices: about 0 (medians). So a uniform course move is not a faithful
  correction; a re-snap on the pixel-is-point grid is.
- **Channel rules 5's pond trimming (decision 0074)** changes its answer on the move itself: at Budapest a 2,469-block
  stretch of the Danube's west half is held as dry land in every moved variant, courses moved or not; built with channel
  rules 4 (no trimming) the window has the same water moved or not (29,090 / 29,085). The trimming must be re-checked
  (its 20-place probe) after the rebake.
- **Dams:** the dam file's height patches are written by DEM index (already on the true grid); the corridors through
  former reservoirs are drawn by the river outlines, so after the fix the carved ground and the drawn water agree.

## 3. The tools

How each tool turns mask cell (r, c) into a position; "corner": contour of a one-cell-padded array at 0.5 mapped with
`X = (x - 0.5) U / wp`, `Y = (hp - (y - 0.5)) U / hp`, putting cell (r, c) at (r + 0.5, c + 0.5); "+0.5": `lat0 + 1 -
(r + 0.5) / hp`, `lon0 + (c + 0.5) / wp`; both half a cell south-east of the pixel-is-point position (r, c). "floor":
position to cell by `floor` (the inverse of +0.5: a round trip through the two is exact, so the tools agree with each
other). Lines checked against the code.

| tool | conversion | file it feeds | offset in it |
|---|---|---|---|
| bake_lake_outlines.py | corner, :377-378 | lakes-glo90-1/-2.bin | yes |
| recontour_lake_outlines.py | corner, :214-215; tile clip `box(0.5, 0.5, wp + 0.5, hp + 0.5)` :169; neighbour rows `floor((i + 0.5) m / n)` :335 | lakes-glo90-3.bin | yes |
| relevel_lake_outlines.py | through recontour's outline; +0.5 only for areas :125 | lakes-glo90-4.bin (current) | yes |
| measure_lake_outlines.py | corner, :268-269 | nothing (report) | — |
| bake_river_outlines.py | corner, :452-453 (v1/2), :726-727 (v3+, unit 24,000) | rivers-outline-glo90-1..4.bin | yes |
| bake_works_1318.py | +0.5 test :308-309, :351; corner boxes :314, :353 | earthdata/works-1318-2.bin | canal and late areas yes; RCE (Netherlands) areas no |
| bake_river_flow.py | raster by mask cell; courses floor :155-156; canals +0.5 :249-250; reroutes +0.5 :462 | rivers-flow-glo90-1.bin | raster: by the reader (below); reroutes yes |
| bake_dam_removal.py | floor lookups; courses rasterised by floor :771-776, :825-830; written by DEM index | dams-glo90-2/-3.bin; corridors and reservoirs (native indices) for the river bake | patches no (true grid); course cells floor-picked |
| snap_rivers_wbm.py | floor in :189-190, +0.5 out :235-236, ends kept | rivers-glo90-snap.bin (courses 2/3) | moved inner vertices yes |
| snap_rivers_channel.py | floor in; path pixels `(pr + 0.5)` :535, out :556-557; ends kept; smoothed | rivers-glo90-snap-2.bin (courses 4, current) | partly: +0.25 cell on average, uneven |
| river_arms.py | floor :141; +0.5 out :272 | rivers-glo90-arms/-arms-2.bin | arm vertices by construction; measured ≈ 0 |
| water_overlays.py | none (packs the above) | sea-referee.bin, the snap and arms files | what its inputs carry |
| sea_referee.py / sea_ocean.py | +0.5 samples :192-193; corner boxes :366 | sea-referee.bin | mask-made coast pieces yes (not measured; GSHHG coasts no) |
| fetch_glo90_wbm.py | none | raw mask tiles | — |
| water_atlas.py, water_atlas_cards.py | floor / +0.5 / cell-area drawing | previews only | — |
| RiverSnap (C#) | heights as FineElevation (pixel-is-point) | rivers-glo90.bin | no |
| FloodplainBake (C#) | heights by FineElevation; own 240/° grid consistent | floodplains.bin | no |

C# readers: `LakeOutlineData`/`LakeOutlineLayer` (lakes, rivers, works: unit from the header, vertices from the tile's
south-west corner) and `WaterOverlays` add nothing, so a bake's offset reaches the world unchanged.
`RiverFlowData.At`/`PixelAt` add the half cell themselves (`- 0.5`, floor), which keeps the flow in line with today's
outlines. `DamRemovalData` writes by DEM index. `FineElevation` reads pixel-is-point.

Unit: at 2,400 units a degree (lakes) half a cell is 1.5 units at 800 columns (50–60°); the shipped corner form there
already rounds half-unit vertices to even (a ±0.5-unit jitter). A rebake needs 4,800 or more (river outlines' 24,000 is
whole at every band). The probe doubled the lake unit; the readers take any unit.

## 4. Tile edges

On the pixel-is-point grid a tile's row 0 lies on its north edge and column 0 on its west edge; its own cells' water
reaches half a cell past those edges, and the strip between its last row (column) and its south (east) edge belongs to
the south (east) neighbour's row 0 (column 0). A rebake should contour each tile with the neighbours' facing cells
(recontour_lake_outlines' method, which already does this) at pixel-is-point positions and clip to the tile:
`X = (x - 1) U / wp`, `Y = (hp + 1 - y) U / hp` in padded coordinates, clip box `(1, 1, wp + 1, hp + 1)`, the south
neighbour's row 0 and the east neighbour's column 0 (and the south-east corner cell) supplying the in-tile strips; what
crosses the north and west edges is clipped off (the neighbours draw it). Both tiles then compute a shore's crossing of
the edge from the same cells, so they meet exactly, as in lakes 3. Where the column count changes (50, 60, 70, 80, 85°)
the neighbour row should be taken by nearest grid point, `round(i m / n)`.

27°E and 62°N are inside one band, so pieces meet exactly there. What changes: the mask's own straight shores that today
lie exactly on those edges (Mikkeli: 57 rows change water across 27°E, against 1 for any other column pair;
Näsijärvi: 106 columns across 62°N) move off them, half a cell west of 27°E (9.3 blocks at 1:10) and half a cell north
of 62°N (4.6 blocks). At Näsijärvi that is where OpenStreetMap has the bay's south shore (40–50 m north of 62°N). The
probe's `shift` keeps edge vertices on the edge, so along those two lines it still shows the half-cell strip
(`images/mikkeli-27e.png`, second panel: the straight yellow strip); away from the edges it is the rebake's geometry.
`probe/seams.py` gives the counts.

## Fix shape and effort (recommendation)

For new worlds only, under new rules versions (old worlds keep their files; their saved-world pins must stay equal):

1. Lake outlines 5 (`lakes-glo90-5.bin`): the recontour and relevel chain with the pixel-is-point mapping above, unit 4,800.
2. River outlines 5 (`rivers-outline-glo90-5.bin`): the same in bake_river_outlines (corridors and reservoirs come from
   native indices and need nothing).
3. Works rules 5 (`works-1318-3.bin`, in the mod): canal and late areas from pixel-is-point cells.
4. A flow-reading rule (a GenFixRules or FlowRules step): `RiverFlowData` reads pixel (i, j) at (i/H, j/W), across the
   south and east edges from the neighbour's first row and column, and moves the reroute vertices half a cell north-west.
5. Courses rules 5: a re-snap (snap_rivers_channel and river_arms on the pixel-is-point grid, the 1,200-column mosaic
   replaced by native columns). The course list's key changes, so the files keyed to it are rebaked on the new list:
   dams-glo90-4 (course levels; course cells by round, not floor), rivers-flow-glo90-2, and the GIRES non-perennial file.
6. Checks: this probe at all places (offsets ≈ 0), `check_lake_edges.py`, the pond-trimming 20-place probe (0074) and the
   narrow pieces, SavedWorldDigestTests unchanged for old worlds and new pins, then a look in game at 1:10.

Effort: items 1–4 about 2–3 days (code half a day; the global rebakes take hours each on this shared PC; checks a day).
Item 5 with its dependents about 3–4 more days (three global bakes and the river checks). Together about a week of work,
plus the uploads on the author's word: lakes-5 (~57 MB) and rivers-5 (~36 MB) on egl-v1, and with item 5 dams-4 (~5 MB)
and flow-2 (~94 MB). I recommend all of it in one release before the server's world is made: items 1–4 alone leave the
courses a quarter cell off, unevenly, and the trial shows course-based choices (narrow pieces) flipping when the water
moves without them.

## 5. The fix, built (decision 0092)

Branch `claude/ea-0148-fix`: the set of items 1-5 above under one placement (`tools/glo90grid.py`), for new worlds,
all or none (courses rules 5, dam rules 4, river and lake outlines 5, flow rules 2, works rules 5, GIRES file 3). The
probe at the same 20 places in a new world of the fix against shipped 2.1.4 (`results/rules5-vs-shipped.txt`; offsets
S/E in blocks, "—" where the correlation peak sits at the window's edge; water blocks in the 1:10 window; the flow
file's labels against the river outlines; the dry flat's width per side):

- Offsets at 1:10 −0.5..+0.7 south and −0.4..+0.8 east (Bergen is GSHHG's coast and Kuurna a reservoir left out, as
  before); at 1:100 within ±0.1. The flats per side come out alike.
- Lake edges drawn one-sidedly 3.48 % → 0.53 % of the edge cells, river edges 23.47 % → 0.00 % (`check_lake_edges.py`).
- Water in the window: the Kiel Canal 140 → 0; the Danube −2.1 % (both arms kept); the Rhine at Cologne −9 %, its
  harbour piece dropped by the narrow-piece rule (the re-measured course half widths are 150–205 m there against
  90–95 m); the Lena −11 %, its window holding only the south-east bank, which moved the half cell (14 blocks west at
  70°N).
- The re-snap moved 146,064 reaches (rules 4: 191,387); Europe's courses lie −0.06 cells south, +0.07 east of the mask.
- SavedWorldDigestTests: the 12 saved worlds' terrain unchanged.

## 6. EA-0160: the Sandsfjorden trench (decision 0093)

A player's straight walled trench inland from Sandsfjorden (59.49°N 6.15°E, 1:25). The probe's `courses` mode dumps the
1:10 world's courses in named 1° squares and counts the reaches river ends rules 2 cuts; `ea0160_trench.py` finds the
columns cut 8 blocks or more below GLO-90 on the mask's land and the courses through them; `ea0160_pastmouth.py` counts,
worldwide on a course list, the reaches that run on over land after reaching the ocean (`results/ea0160-pastmouth-5.log`).

- **Cause**: reach 41302 (41303 on courses rules 5), Q 26 m³/s, which `rivers-glo90.bin` leads from the head of
  Hylsfjorden down the fjords to the open sea on the 2′ grid at sea level, 40 km in straight steps over the headlands.
  Its channel cuts to that level wherever it crosses land.
- **Courses rules 5 doesn't remove it** (the snap leaves reaches that run in the sea or over land without mask water).
- **Worldwide**: 352 such reaches on courses rules 5's list, 1,560 km over land; mostly British Columbia, Norway,
  Greenland, Labrador, Patagonia.
- **Fix**: river ends rules 2 (a runtime rule, no data): a river ends where it reaches the sea, before land that reaches
  5 m (the 2′ grid's land height near the sea); low land only ends the stretch in the sea; a cut end gets no mouth
  prolongation. Two drafts were dropped on the way: cutting at the first point in the sea ended the Grand Rhône about
  10 km above its mouth at 1:100 (its estuary is sea in patches in the mask) and, with the old prolongation, ran it
  5 km across the delta; letting any low land point end the stretch missed 25 % of the trench length (a fjord's shore
  reads low on the 2′ grid just before the headland).
- **Results** (`results/ea0160-mouths.txt`, `results/ea0160-river-ends-2.csv`): Sandsfjorden's trench columns at 1:25
  16,523 shipped → 15,956 courses rules 5 → 0; at 1:10 16,344 → 15,859 → 0 on a course (2,246 left on an island, not
  a river's). Worldwide 1,038 reaches cut, 283 left out, 291 of the 352 trench reaches fixed (1,438 of 1,560 km). The
  9 mouths and deltas (Daugava, Rotterdam, Damietta, Grand Rhône, Amazon, Mississippi, Ganges, Mekong, Venice) are the
  same to the block at 1:10 and 1:100.

## 7. In game (AgentBridge bench, 18 November 2026)

Test build `2.1.4-ea0148.1` (this branch at e4555506), a new `preset-earth-10` world on a private bench whose data store
held the new files. From 120-150 blocks up, north at the top, and obliquely: Näsijärvi's straight bay shore ends 4.6
blocks north of 62°N (read block by block: water to z 311756, 62°N at z 311761.6); Mikkeli and Tamula cross 27°E with
no step or straight piece; Brienz's south-east shore meets its slope as the north-west one does; the Daugava's banks
are even; the Kiel Canal at the probe's window is land; the Danube keeps both arms round Margaret Island; Sandsfjorden
(EA-0160) is forest down to the fjord's beaches, no trench. Seen on the way, not part of this work: Näsijärvi's beach is
`sand-chalk` (white chalk sand on a Finnish lake). The shots are in `C:\VS mods\work\ea-0148\bench-shots` (not kept
in the repository).

## Reproduce

```
dotnet build -c Release -o C:\VS mods\work\ea-0148\probe-bin docs\research\ea-0148-offset-2026-11\probe
dotnet C:\VS mods\work\ea-0148\probe-bin\Ea0148Probe.dll <repo> <out> probe\places.txt 10,100 base,shift,shiftall
.venv\Scripts\python.exe -I probe\analyse.py <out> <report> nasijarvi-62n,brienz
dotnet ...\Ea0148Probe.dll <repo> <out> probe\places.txt courses "56,24;50,6;..."   (then probe\courses.py)
```

Variants also: `shiftflow`, `shiftcourses`, and the controls `-allnarrow` (every narrow river piece kept), `-ch4`
(channel rules 4) and `-ends1` (river ends rules 1). EA-0160: `courses "43,4;50,-126;59,6"` writes
`river-ends-2.csv` and its vertices too; `probe\ea0160-places.txt` holds the mouths, the wide Rhône and Sandsfjorden. EARTH_STORE picks another data store; EARTH_RAW the raw Copernicus tiles. Model builds took 10–22 s
and a window under a second; other sessions were running Python tools on this PC at the same time.
