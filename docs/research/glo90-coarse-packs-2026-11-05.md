# GLO-90 averaged to 24″ for the included heights (four height data packs)

Date: measured on the evening of 4 November 2026 for decision
[0053](../decisions/0053-scales-included-glo90-and-glo90-rivers.md)'s included GLO-90 heights. The brief: Copernicus
GLO-90 downsampled to the finest grid that fits four data packs of below 40,000,000 bytes each, with no worse than 4 m
height steps near sea level, a sentinel where a cell has no land, and, if it fits, a layer giving the spread of the 3″
heights inside each cell. The coordinator later asked for room in the same four packs for `rivers-glo90.bin` (up to
about 25 MB), which leaves about 135 MB for the heights.

Results, scripts and outputs are in [glo90-coarse-packs-2026-11-05](glo90-coarse-packs-2026-11-05). The converter is
`tools/Glo90Coarse`, the reader `src/Core/CoarseElevation.cs`.

## Result

- **Grid:** 24″ rows (741.3 m); 150 × cos(the tile row's middle latitude) columns per degree, rounded, so cells are about
  square in metres (as GLO-90's own longitude bands, but in 1° steps).
- **Cell value:** the mean of GLO-90's land points in the cell (points of exactly 0 m, GLO-90's sea, are left out),
  then sharpened by 0.3 × (the mean − the mean of the land cells beside it). A cell without land holds no value.
- **Height steps:** 1 m below 64 m, 2 m below 256 m, 4 m below 1,536 m, 8 m above (−512 m to 9,000 m).
- **Roughness:** on 72″ cells, the root mean square of GLO-90's heights around the reader's own interpolated surface,
  in 16 logarithmic steps (0, then 1 m to 1,024 m).
- **Size:** heights 121.67 MB, roughness 7.50 MB, index 0.18 MB; four zips of 39.78, 39.80, 39.79 and 9.98 MB.
  Pack 4 has 30.0 MB free for `rivers-glo90.bin`. A trial packing (into a scratch folder) with the rivers file as it
  stood at 23:39 (23,976,950 bytes) gave a pack 4 zip of 33,964,106 bytes and 153.3 MB for all four.
- **Accuracy against full GLO-90** (every GLO-90 land point, 20.5 billion): RMS 17.16 m; plain means without the
  sharpening gave 18.77 m. Per window, from 2.2 m on the Dutch coast to 68.5 m in the Grand Canyon (table below).

## Method

### Source

The published tiles in `rawdata/copernicus-glo90` (26,475 GeoTIFFs, read with `DemTiles.ReadGeoTiff`), not the
converted store. In the published floats, sea is exactly 0.0 and there are no NaNs in the tiles checked (N51E004,
N52E004, N60E006, N31E035, N41E049, N27E086, S78E166, N36W113). Land near sea level is not 0.0: N52E004 has 533,840
points of exactly 0 (the North Sea) and 37,887 non-zero points between −0.5 and 0.5 m. The store rounds to whole
metres and writes 0 for both, so it can't tell them apart. Inland water keeps its level (the Caspian at −28.0 m, the
Dead Sea area down to −427 m) and is averaged as land. 307 tiles hold no land point at all and are not stored.

### Averaging

A GLO-90 point stands for the area half a point around it. A coarse cell is a 1/n share of one tile's points: each point
counts with the length it shares with the cell, in rows and in columns (rows: 1200 per degree into 150; columns: the
tile's 1200, 800, 600, 400, 240 or 120 into 150 × cos φ). The coarse grid is therefore shifted by half a GLO-90 point
(1.5″ north, and 1.5–15″ west by band) from the tile corners; the reader uses the same shift. Point areas are not
weighted by cos φ within a cell.

The sharpening uses the four neighbours (west and east in the row; north and south, the cell nearest by longitude,
across tile and band edges), land cells only. It was chosen after a test on single tiles (`sharpen-test.py`): with
Catmull-Rom interpolation, α = 0.3 lowered the RMS error by 8–11 % for about 5 % more data in a zlib proxy (α = 0.4 gave
no further gain). On the real build it cost 8.6 % more height data.

### Coding

Per tile: a flag (every cell land), the land mask, then each land cell's code minus a prediction, binary arithmetic
coded (a range coder with adaptive 12-bit probabilities, integer arithmetic only). The prediction is
W + (36 (N − W) − 15 (NW − W) + 14 (NE − W) − 9 (WW − W) − 12 (NN − W)) / 64, the least-squares weights fitted on the
1-m codes of 1,324 tiles; near the tile's edges the median edge predictor (LOCO-I). The context is the size of the
nearby residuals and gradients (20 classes). A cell without land isn't coded and takes the median edge prediction, so
the predictor runs on across the sea. The roughness codes are coded the same way without a mask.

### Files

`earthdata/glo90c-index.bin` (header, grids, steps, coding, roughness table, where each tile is; Brotli) and
`earthdata/glo90c-1.bin` .. `glo90c-4.bin` (tile payloads, north to south and west to east). Each tile is decoded when
first used and kept in a cache.

## Measurements

### Grid and height steps (Brotli, as decision 0051's tiles)

Every 20th published tile (1,324), scaled to 26,475. Land-only means with a land mask; the median edge predictor,
zigzag, byte planes, Brotli quality 9, window 22 (`measure-grids-brotli.json`). MB for all land:

| Grid | Land cells (M) | 1 m | 2 m | 4 m | 1/2/4/8 m (< 256, < 1,024, < 2,560) |
|---|---|---|---|---|---|
| 30″, cells about square | 176 | 123.4 | 103.7 | 85.7 | 104.0 |
| 24″, GLO-90's bands | 325 | 212.8 | 177.8 | 145.8 | 178.3 |
| 24″, cells about square | 274 | 185.0 | 154.8 | 127.2 | 155.6 |
| 22.5″, about square | 312 | 207.8 | 173.7 | 142.5 | 174.7 |
| 20″, about square | 395 | 257.1 | 214.4 | 175.4 | 216.0 |
| 18″, about square | 487 | 311.2 | 259.1 | 211.4 | 261.4 |

- Columns of about square cells instead of GLO-90's bands: 15 % fewer land cells and 13 % less data at 24″.
- Each halving of the step everywhere costs 0.8–0.9 bits more per land cell (24″: 3.71, 4.51 and 5.39 bits for 4, 2
  and 1 m). Of the land cells, 29 % are below 200 m, 46 % at 200–1,000 m, 21 % at 1,000–3,000 m and 5 % above, so
  coarser steps only in the mountains save little: the height-dependent 1/2/4/8 m steps cost 4.54 bits, about as much
  as 2 m everywhere.
- Rounding error (RMS) is step / √12: 0.29, 0.58, 1.15 and 2.31 m for 1, 2, 4 and 8 m steps (measured).

### Coder

At 24″ (cells about square), MB for all land:

| Steps | Brotli 11 | Arithmetic, median edge | Arithmetic, linear (least squares) |
|---|---|---|---|
| 4 m | 116.1 | 109.1 | 105.2 |
| 2 m below 128, 4 m below 1,024, 8 m | 114.6 | 107.7 | 103.9 |
| 2 m below 256, 4 m below 1,536, 8 m | 121.8 | 114.9 | 110.9 |
| **1 m below 64, 2 m below 256, 4 m below 1,536, 8 m** | 124.5 | 117.7 | 113.6 |
| 2 m below 256, 4 m | 125.1 | 118.2 | 114.1 |
| 2 m | 144.4 | 137.4 | 132.7 |

Every 20th tile (`measure-steps-every20.json`). On every 40th tile (`measure-coders-every40.json`): the plane predictor
was 2–4 % worse than the median edge one, faster adaptation (shift 4) about 1 % worse, contexts without gradients
0.2–0.5 % worse. The arithmetic coder decodes a 24″ tile in about 0.44–0.63 ms.

### Roughness layer

Cost of the layer (arithmetic coded; a plane fitted per cell stands in for the interpolated surface, so these are
estimates), MB: 4-bit codes on 24″ cells 64.7, on 48″ 17.6, on 72″ 8.3, on 90″ 5.5, on 120″ 3.3; 3-bit codes about a
third less. The build's layer (72″, 4-bit, against the real interpolated surface) is 7.50 MB.

### The build

`tools/Glo90Coarse build` with 32 workers: pass 1 (means, sharpening, coding, and a decode check of every tile) 124 s;
pass 2 (every GLO-90 land point against the interpolated grid, for the roughness and the overall error) 256 s.
26,168 tiles, 271.9 million land cells of 332.5 million (`build-final.json`).

| File | Bytes | Tiles | Pack zip | Zip bytes | Zip SHA-256 |
|---|---|---|---|---|---|
| glo90c-index.bin | 177,324 | – | earthheightdata1 | 39,780,474 | fadecfe94ef49d291211cd101fb5479ab132d814e3e8f209b439d078d59d944f |
| glo90c-1.bin | 39,594,940 | N83W078 – N45E078 | earthheightdata1 | (as above) | |
| glo90c-2.bin | 39,791,138 | N45E079 – N15W093 | earthheightdata2 | 39,798,036 | e7069cab6c0d29ac06e83cda0455cb388aa02d0e784d5ccf8f26a54c7902a581 |
| glo90c-3.bin | 39,796,380 | N15W092 – S31W051 | earthheightdata3 | 39,790,762 | d26aeb93de91cc626492de319f149eed9e3497f5a990d6fc2c467792783fc016 |
| glo90c-4.bin | 9,984,522 | S31E017 – S90E179 | earthheightdata4 | 9,979,718 | e85e41190b143c6dc5890d3835054bc1b68a9baa4eb151b3d28860f1324e8ce1 |

### Accuracy against full GLO-90

`tools/Glo90Coarse verify`: `FineElevation.TryHeight` on the store against `CoarseElevation.TryHeight`, at GLO-90's own
points with a non-zero height (rows every 3″, about 93 m; columns strided to about 100 m: 75–90 m apart, 129 m in the
Matterhorn window). "Plain" is the first build without the sharpening (`verify-box-means.json`); the rest is the
final build (`verify-final.json`). Differences are coarse minus GLO-90.

| Window | Points | RMS, plain (m) | RMS (m) | Mean abs (m) | Mean (m) | 95 % abs (m) | Max abs (m) | Highest: GLO-90 / plain / final (m) |
|---|---|---|---|---|---|---|---|---|
| Everest 27.85–28.15 N 86.75–87.1 E | 151,981 | 73.4 | 65.1 | 46.9 | +0.1 | 137 | 428 | 8,726 / 8,477 / 8,584 |
| Kali Gandaki, Dhaulagiri 28.55–28.85 N 83.4–83.8 E | 173,641 | 70.6 | 61.7 | 46.7 | −0.1 | 126 | 366 | 8,116 / 8,013 / 8,140 |
| Matterhorn 45.9–46.1 N 7.55–7.85 E | 43,621 | 64.5 | 57.4 | 42.5 | 0.0 | 120 | 364 | 4,462 / 4,295 / 4,377 |
| Grand Canyon 36.0–36.3 N 112.3–111.9 W | 173,641 | 75.0 | 68.5 | 47.6 | 0.0 | 149 | 384 | 2,742 / 2,733 / 2,737 |
| Middle Rhine 50.0–50.3 N 7.55–7.85 E | 86,941 | 22.7 | 21.1 | 14.9 | 0.0 | 47 | 113 | 622 / 601 / 620 |
| Gauja, Sigulda 57.1–57.25 N 24.75–25.0 E | 36,381 | 8.5 | 8.0 | 5.5 | 0.0 | 17 | 59 | 150 / 133 / 135 |
| Nærøyfjord 60.8–61.0 N 6.8–7.2 E | 53,828 | 71.1 | 60.9 | 43.3 | +3.9 | 131 | 377 | 1,757 / 1,731 / 1,770 |
| Netherlands coast 51.8–52.2 N 4.0–4.6 E | 160,530 | 2.2 | 2.2 | 1.4 | +0.1 | 4.8 | 25 | 36 / 20 / 21 |

- Every GLO-90 land point in the windows got a coarse height (none fell on the no-land side).
- The sharpening lowered the RMS by 5–14 % in the seven inland windows and by 1 % on the Dutch coast.
- It can overshoot: at Dhaulagiri the highest coarse point is 24 m above GLO-90's, at the Nærøyfjord 13 m.
- The Nærøyfjord's mean is +3.9 m: the fjord is sea in GLO-90, and the cells over it are means of its walls.
- The highest points are measured at the sampled points only. The surveyed summits are 8,849 m (Everest), 8,167 m
  (Dhaulagiri) and 4,478 m (Matterhorn); restoring them is decision 0053's job, not this data's.

### Roughness against the actual spread

Per 72″ cell with at least 20 sampled points: the actual RMS of (GLO-90 − coarse) against the layer's value at the same
points.

| Window | Actual RMS / layer (m) | Correlation (log) | Median actual / layer |
|---|---|---|---|
| Everest | 65.1 / 65.0 | 0.89 | 0.97 |
| Kali Gandaki | 61.7 / 61.2 | 0.87 | 0.99 |
| Matterhorn | 57.4 / 56.7 | 0.90 | 0.98 |
| Grand Canyon | 68.5 / 67.7 | 0.97 | 0.99 |
| Middle Rhine | 21.1 / 21.0 | 0.94 | 0.96 |
| Gauja | 8.0 / 8.2 | 0.87 | 0.95 |
| Nærøyfjord | 60.9 / 59.7 | 0.91 | 0.98 |
| Netherlands coast | 2.2 / 2.0 | 0.91 | 1.04 |

The layer is built from the same quantity on its own cells, so a close match is expected. The checks confirm that the
reader, the 16 logarithmic steps and the bicubic interpolation between 72″ cells keep it. It is a spread, not a shape:
it says how much relief the 24″ surface misses, not where.

### Edges, threads, speed, memory

- **Edges:** heights 10⁻⁹° on either side are equal to 3 decimals across a tile edge in latitude and in longitude, the
  band edges at 50° N (1,200 | 800 GLO-90 columns) and 60° N, and the antimeridian (65.5° N; and longitude 540 = 180).
  Along four parallels from 5° to 15° E, the largest change between points 7 m apart was 5.8 m, on a steep slope at
  46.2° N 7.883° E, not at an edge.
- **Threads:** 600,000 lookups (half in the windows, half random between 60° S and 80° N), heights and roughness, on 32
  threads with a 2 MB cache (248,427 tile decodes) and again on a warm cache: all bit-identical to a serial run.
- **TryOpen:** null without the index, with a part missing, and when the opener throws FileNotFoundException for a part;
  the same heights from read-only MemoryStreams (as EarthDataPacks serves them). Mid-Pacific (0.5° N 150.5° W): no height
  and no roughness. The South Pole reads 2,832 m.
- **Speed:** warm lookups in one window took 100–160 ns for a height (110–190 ns on the coast) and 100–170 ns for the
  roughness on one thread; with 32 threads at once, 200–340 ns each (94–158 million lookups per second in all). The
  ranges are over several runs on a machine with other work running. The first lookup in a tile decodes it: 0.44–0.63
  ms for heights, 0.03–0.04 ms for roughness. The 600,000 mixed lookups above (a height and a roughness each), run
  serially from a cold cache, took 49–52 s.
- **Memory:** the parts are read once and kept: +130.9 MB on the managed heap. Decoded tiles are cached up to 96 MiB
  (`CacheBytes`); touching all 18,844 tiles between 60° S and 80° N filled it to 97.9 MB and grew the heap by 98.6 MB. Each
  thread also holds its last eight tiles (at most 45 KB each), which stay alive after the shared cache drops them. Opening took 78–93 ms from local files.

### Finer grids with the rivers in a mod of their own (evening, 5 November)

What the room would buy if `rivers-glo90.bin` left the height packs. This was first recorded as the author's wish
("due to size limit - not technical"); the author later said no move was meant (Earth 0053, Packaging). The author
would accept 200 MB of heights. Whole-dataset scratch builds with the same converter (`build --rows`; summaries in
[finer](glo90-coarse-packs-2026-11-05/finer)); RMS over all 20.5 billion GLO-90 land points, as above. MB = heights +
roughness; the index adds about 0.2 MB.

| Grid | Height steps | Roughness | MB | Packs of 40 MB | RMS (m) |
|---|---|---|---|---|---|
| 24″ (tall.5) | 1 m < 64, 2 < 256, 4 < 1,536, 8 m | 72″ | 129.2 | 4 (with the rivers) | 17.16 |
| 22.5″ | as tall.5 | 72″ | 143.4 | 4 | 16.16 |
| 20″ | as tall.5 | 72″ | 174.2 | 5 | 14.44 |
| 20″ | 2 m < 128, 4 < 1,024, 8 m | 90″ | 157.5 | 4 (2.3 MB spare) | 14.46 |
| 18″ | 2 m < 128, 4 < 1,024, 8 m | 90″ | 188.3 | 5 (11 MB spare) | 13.04 |

- The coarser steps cost 0.02 m of RMS at 20″ (rounding error 0.6–2.3 m against 14 m from the grid) and save 9 %.
- 18″ is 24 % closer to GLO-90 than tall.5's 24″; it still can't hold valleys narrower than about 1 km.
- Coded bits per land cell fall as the grid gets finer (3.6 at 24″, 3.1 at 20″, 3.0 at 18″).
- [grandcanyon-grids.jpg](detail-gullies-2026-11-05/grandcanyon-grids.jpg): GLO-90 averaged to 24″, 20″, 18″ and 9″ in
  Python (sharpened as the build, cubic spline instead of Catmull-Rom) next to the game's 24″ without noise. The
  Python 24″ and the game's differ by 15 m on average in that close-up. The Grand Canyon's side canyons show only at 9″
  (about 0.6 GB by the bits per cell above; estimate, not built).

## Choices

- **24″, not 20″ or 22.5″.** 20″ needs about 38 % more data (1/2/4/8 m: 216 against 156 MB with Brotli) and would
  not fit 135 MB with any steps tried. 22.5″ needs about 12 % more (from the Brotli measurements; not built): about
  125 MB of plain means or 136 MB sharpened with the chosen steps, plus the roughness, which is at or over the 135 MB
  left beside the rivers. 30″ fits easily but is 25 % coarser.
- **Cells about square in metres.** GLO-90 thins its columns towards the poles in the same way. GLO-90's own bands
  would cost 18 % more land cells and 15 % more data at 24″. The cost is in the game: Earth's world is equirectangular,
  so at 60° a coarse cell is about twice as wide in blocks as it is high.
- **1 m below 64 m.** Low coasts and river plains; 2 m up to 256 m, 4 m up to 1,536 m, 8 m above. This is within the
  brief's 4 m near sea level, and the 8 m steps (RMS 2.3 m) are small next to the grid's own error in the mountains
  (57–69 m RMS).
- **Sharpening (0.3).** 8.6 % more data for 5–14 % less error inland; there was room.
- **Roughness on 72″ in 4 bits.** 7.5 MB. On 48″ it would have taken about 17.6 MB, which leaves too little for the
  rivers.

## Not done or not checked

- Not run in the game, not wired into `TerrainModel`, and the packs not loaded by Vintage Story (only the files and the
  zips' contents were checked: every SHA-256 in `pack-index-fragment.json` matches the zips and `earthdata/`).
- No comparison with the built-in 2′ heights at the same points; [terrain-vs-glo90](terrain-vs-glo90-2026-11-04.md) has
  that comparison for the game's terrain.
- GLO-90 is a surface model: forest canopy, buildings, dams and flattened reservoirs are in the means (as in 0051).
- The sharpening uses neighbours across tile edges; cells beside a tile that GLO-90 doesn't have see it as sea.
