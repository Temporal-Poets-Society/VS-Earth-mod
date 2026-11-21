# 0092: The water mask's water on GLO-90's own grid

Status: **in Earth 2.2.0** (21 November 2026), built on branch `claude/ea-0148-fix` (EA-0148), for new worlds. Its data files are on their
releases (`egl-v1`, `ed-v1`), uploaded 21 November 2026 on the author's word ("upload now"). Date: 2026-11-17. Decided by the maintainer (D72): where the
data's cells lie is technical; what players see (lakes and rivers on their flat water) and the uploads are the author's,
through the coordinator (the author approved the fix on 17 November 2026).

## Context

Copernicus GLO-90's height and water-mask tiles are **pixel-is-point**: the GeoTIFFs' raster type is RasterPixelIsPoint
and the tie point is the tile's north-west corner, so cell (r, c) of the tile with south-west corner (lat0, lon0) is
centred on (lat0 + 1 - r/1200, lon0 + c/cols), and its area reaches half a cell either way. `FineElevation` reads the
heights so. Every bake that turned the mask's cells into positions took cell (r, c) as the area from that point down and
east to the next row and column: half a cell south-east of the heights.

Measured offline in new 2.1.4 worlds (EA-0148 step 1, `docs/research/ea-0148-offset-2026-11/`): at 1:10 the lakes' and
rivers' water lay 4.5-5.2 blocks south and 3.6-13.9 blocks east of GLO-90's grid at all 18 places with outline water
(exactly half a cell for each column band); lakes followed their outlines exactly, so the shore rules hid none of it.
Against GLO-90's flat water the north and west shores had almost no dry flat while the water ran 3-12 blocks up the
south bank and 2-26 blocks up the east bank. At 1:100 it was half a block south and 0.4-1.0 east. The mask's own
straight shores that lie on a whole degree (Mikkeli on 27°E, Näsijärvi's bay on 62°N) sat exactly on the tiles' edges,
where the real shore is half a cell off them (OpenStreetMap agrees at Näsijärvi).

The same placement was in every product made from the mask's cells, which therefore agreed with each other: the lake
and river outlines, the works of 1318's canal areas, the snapped river courses (partly: the moved reaches' inner
vertices), the flow file's reroutes, and the flow file's reader (`RiverFlowData` read pixel (i, j) at (i + 0.5, j + 0.5)).
Moving only the outlines (the step 1 trial) flipped choices made against the courses (a narrow harbour piece of the Rhine
dropped) and left canal water where the canal areas didn't move.

## Decision

1. **One placement for the mask's cells** (`tools/glo90grid.py`): pixel-is-point. The bakes keep their area logic (a cell
   spans r..r+1, c..c+1 from an origin; floor from a position, + 0.5 back) and take the origin from it: half a cell north
   and west of the tile's corner. Narrow high-latitude tiles go onto the uniform 1/1200° grid by the nearest cell (a
   tile's last column or two take the next tile's first). `EARTH_GLO90_GRID=cell` gives the old placement back, so the
   files older worlds read can still be rebaked (checked: `works-1318-2.bin` comes back byte for byte).
2. **The whole set moves together, under new rules for new worlds** (old worlds keep their files and their terrain):
   - **River courses rules 5** (`RiverNetwork.Glo90PointCourses`): `rivers-glo90-snap-3.bin` and `rivers-glo90-arms-3.bin`,
     the snap and the arms run on the new grid (data store, `ed-v1`).
   - **Dam rules 4** (`dams-glo90-4.bin`, keyed to courses rules 5's list).
   - **River outlines 5** (`rivers-outline-glo90-5.bin`): version 4's rules, each tile contoured with its south and east
     neighbours' first row and column (on the new grid they lie on its edges) and clipped to its square, in two passes
     (`tools/bake_river_outlines.py --version 5`).
   - **Lake outlines 5** (`lakes-glo90-5.bin`): version 4's lakes, levels and flags, every tile outlined anew with its
     neighbours' cells and clipped; a piece made only of the neighbours' half cells takes their lake; 4,800 units a degree
     (half a cell is then a whole number at every column count).
   - **Flow rules 2** (`rivers-flow-glo90-2.bin`): solved on the new grid and courses rules 5's list; `RiverFlowData`
     reads a version 2 file's pixels on their own grid points.
   - **Works rules 5** (`works-1318-3.bin`, in the mod): the canals' and the late areas from the mask's cells on the new
     grid; the RCE areas as before.
   - **`rivers-nonperennial-3.bin`** (in the mod): GIRES matched to courses rules 5's list; the world takes the file
     whose course key is its own.
   A new world gets all of these or none: `WorldParamsBuilder` gives lake outlines 5, flow rules 2 and works rules 5
   only with river outlines 5, which come only with dam rules 4 on courses rules 5.
3. **Two details the new grid needs** (found on the way): the half-pixel simplification must keep the vertices next to
   a tile's edge (a shore between the first and second column lies exactly half a pixel from the edge and was folded onto
   it), and a clipped piece's own cells on the first row and column lie on its boundary (they count).

Considered and not done:

- **Moving the shipped files half a cell** (the step 1 trial): it leaves each tile half a cell short on its south and east
  edges and over its north and west ones, and keeps the courses, canals and flow behind.
- **A per-reach correction of the courses in the game**: the snapped courses are off unevenly (moved reaches' inner
  vertices by up to half a cell, reach ends not at all, many reaches not moved), so no single shift fits; re-snapping
  costs the course-keyed files (dams, flow, GIRES), which were rebaked.
- **The sea referee's coast pieces** (`sea-referee.bin`) were made from mask cells the same way where they follow the
  mask; the sea coasts measured (Bergen) are GSHHG's and have no such offset. Left for its own item.

## Consequences

Checked offline (`docs/research/ea-0148-offset-2026-11/`, the probe at the same 20 places, a new world of each rules set):

- **The water sits on GLO-90's grid**: at 1:10 the lakes' and rivers' water lies −0.5..+0.7 blocks south and −0.4..+0.8
  east of it at the 18 places with outline water (shipped: 4.5-5.2 south, 3.6-13.9 east), at 1:100 within ±0.1. The
  flats show it: the dry flat left between the water and the slope is now alike on the north and south shores.
- **Tile edges**: lakes drawn one-sidedly along a tile edge fall from 3.48 % to 0.53 % of the edge cells
  (`tools/check_lake_edges.py`), rivers from 23.47 % to 0.00 %; Mikkeli's straight shore on 27°E is gone.
- **The courses**: the re-snap moves 146,064 reaches (rules 4: 191,387; more already lay in the mask's water on the right
  grid), and the courses in Europe lie −0.06 cells south and +0.07 east of the mask's rivers. The flow file's labels lie
  −0.8..+3.0 blocks from the river outlines (the Rhine at Cologne +4.9 south, below).
- **What moved besides the shores**: the Kiel Canal's strip of canal water is gone (140 blocks → 0). The Danube at Budapest
  keeps both arms (−2.1 % water; the step 1 trial lost one). The Rhine at Cologne loses its harbour piece (−9 %): the
  re-measured course is 150-205 m wide in half widths there against 90-95 m before, so the narrow-piece rule drops the
  harbour basin, a modern work. The Lena at Kyusyur shows −11 %: its window holds only the south-east bank, which moved
  the half cell (14 blocks west at 70°N).
- **Saved worlds**: their pinned terrain is unchanged (SavedWorldDigestTests, 12 worlds); the new files have new names.
- **Data**: four new files for `egl-v1` (`lakes-glo90-5.bin` 57,108,482 bytes, `rivers-outline-glo90-5.bin` 35,947,613,
  `dams-glo90-4.bin` 6,390,099, `rivers-flow-glo90-2.bin` 93,894,092) and two for the data store, `ed-v1`
  (`rivers-glo90-snap-3.bin` 6,791,105, `rivers-glo90-arms-3.bin` 218,049), each upload on the author's word;
  `works-1318-3.bin` and `rivers-nonperennial-3.bin` travel in the mod. The streams layer (EA-0131) re-snaps its heads
  against courses rules 5's list.
- Released with river ends rules 2 ([0093](0093-rivers-end-at-the-sea.md)), which ends a river where it reaches the sea.
- **Seen in game** on the AgentBridge bench (18 November 2026, test build `2.1.4-ea0148.1`, a new 1:10 world on the new
  files): Näsijärvi's straight bay shore ends 4.6 blocks north of 62°N (block by block: water to z 311756, 62°N at
  z 311761.6), where OpenStreetMap has it; Mikkeli's and Tamula's shores cross 27°E without a step or a straight piece;
  Lake Brienz's south-east shore meets its slope like the north-west one; the Daugava's banks are even; the Kiel Canal
  is land; the Danube keeps both arms round Margaret Island.
