# 0088: Lake outlines drawn across the tiles' edges

Status: **built, in Earth 2.1.1** (EA-0147): new worlds read `lakes-glo90-3.bin` from `egl-v1`. Date: 2026-11-16.
Decided by the maintainer (D72): what players see is the seam the author reported; the release and the file's upload are
the author's ("Yes, release 2.1.1", 16 November 2026, relayed by the coordinator).

## Context

The author saw straight north-south seams along 27°E on World Map's chart at 1:10, at Tamula (57.84°N, Võru) and near
Mikkeli (61.75°N): lakes ending in straight edges on the meridian, the shore across it offset, relief lines breaking there.
The world was new, made with Earth 2.1.0 (GenFixRules 5, full GLO-90 heights, `lakes-glo90-2.bin`).

What it is (checked offline with the terrain model of a new 1:10 world, and in the data):

- **Not the heights.** GLO-90's tiles join seamlessly at every meridian checked (57-62°N, 22-31°E): the height difference
  across a tile edge is that between two columns inside a tile, the best fit is no shift, and dry land in the world does
  not step at 27°E (mean |dTop| 0.013 blocks at the meridian, 0.017-0.036 beside it).
- **Not the water mask itself, mostly.** Copernicus's water body mask is pixel-is-point (tile corners on whole degrees) and
  its facing columns at tile edges differ about as often as two columns inside a tile (22-31°E, 57-63°N: 192 rows at 27°E,
  139-182 inside the tiles; runs of 5 or more rows as frequent).
- **The lake bake.** `tools/bake_lake_outlines.py` contoured each 1° tile on its own, padded with land. A lake crossing
  a tile edge got, on each side, a shore along the edge from the centre of its first water cell there to the centre of its
  last, leaving at 45°. Where the facing columns differ by a cell (wherever the shore crosses the edge at a slant) the
  joined shore stepped a whole cell straight along the edge (at Tamula the west tile's piece meets 27°E over rows 193-201,
  the east tile's over 194-200: 9 blocks at each end at 1:10); where they agree, both pieces were notched by half a cell;
  a single water cell beside the edge became a diamond touching the other piece at one point. In `lakes-glo90-2.bin`,
  the two tiles' cuts differ on 7,985 of 8,590 shared sides that lakes cross (8.4 % of the cut length on one side only).
  In the world, at 61.69-61.80°N, water changed between the two block columns at 27°E in 255 of 1,226 rows, against 11-19
  (up to 61) at a mask cell boundary inside a tile.

## Decision

1. **`lakes-glo90-3.bin`** (`tools/recontour_lake_outlines.py`): version 2's lakes, levels and flags, outlined again.
   Each tile's drawn lake cells are recovered from version 2 (a mask label is drawn where a version 2 ring comes within
   0.75 cells of one of its cells' centres; version 2's own method gives version 2's tiles back from them, byte for byte,
   in every tile, or the bake stops). Each tile is then contoured with one cell of its eight neighbours' drawn lake cells
   around it (across the parallels where GLO-90 changes its column spacing, the neighbour's cells resampled to this tile's
   by cell centre) and clipped to its square; polygons wholly inside stay exactly as version 2 drew them. So the cells
   either side of an edge are contoured alike in both tiles and a lake's two pieces end at the same points; a shore
   crossing the edge crosses it diagonally, as it crosses a cell boundary inside a tile.
2. **No lake is added, removed or re-levelled**, and the mask's own shape is kept: where the mask has water exactly up to
   a tile edge and land beyond (its own north-south or east-west shore, as often there as inside a tile), the shore stays
   straight on the edge. EA-0117's proposed easing of such places (a lake tapering across the line) is not done: it would
   invent shape the mask lacks, and the counts above show those places are no more common on the edges than inside.
3. **Lake outlines rules 3** (`LakeOutlineData.FileVersion` 3, `EarthParams.LakeOutlines` 102): new worlds on full GLO-90
   heights read version 3. Saved worlds keep the file they were made with (`SavedWorldDigestTests` unchanged).
4. The file goes on the GLO-90 data release `egl-v1` under its new name (beside version 2, which older worlds keep
   reading); a build with rules 3 is released only once it is there, since its new worlds would not find their file.

## Consequences

- Tamula at 1:10: water changes at the meridian in 2 of 669 rows (was 28), as at any other column. Near Mikkeli 177 of
  1,226 (was 255): the rest is the mask's own straight shores on 27°E there (5-13 rows where one tile's lake ends on the
  edge and the other tile's cells are land 4-20 m above it, in the DEM too). They are real: on OpenStreetMap the west shore
  of Hirvijärvi (61.75°N) and the east shore of the water at 61.71°N run north-south at about 27.00°E. The mask's 6″
  columns (19 blocks at 1:10 on the ring at 61°N) draw them as straight lines on the meridian.
- The shore strip, the beach and pool-joins-river read the lake's whole area (GenFixRules 5); the pieces now meet along
  the whole edge, which that lookup's spans also see.
- A new world downloads `lakes-glo90-3.bin` (about the size of version 2, 58 MB) instead of version 2.
