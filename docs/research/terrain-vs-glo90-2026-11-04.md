# Game terrain against GLO-90 at 13 places (built-in and GLO-90 heights, a 1:12 tall world)

Date: 4 November 2026. The author's questions, about their world "vintage adventure lands" (Earth 1.0.3-tall.3):

- "In map at least there are some 45deg ringing. Also in place it looks many small peaks where my mind should be less
  according google maps. Can you maybe map terrain in game around Everest and compare?"
- "Do we need such noise at least on so large effect?"
- "our game render does not look like GLO 90 at all"
- "Get terrain match GLO data and sample different locations"
- "Game loading for 4m world and 1024 h was very long"; "Can we speed up loading?"

## Method

- **Offline, with Earth's own terrain code.** `TerrainModel.GetColumn` is the call world generation makes. The model
  was built with `EarthParams.BuildModel` from the author's saved world settings: the save's `earth-params`, read
  read-only.
  - Earth size 4,096,000: 12.2 m per block across.
  - World height 1024, sea level 110, linear mountain heights: Everest at y 1022, 9.7 m per block up.
  - Detail noise 1, summit rules 3, rivers on.
  - Data: `earthdata/`, the same files as the installed packs earthterraindata and earthwaterdata 1.0.2 (CRC-checked;
    only `ATTRIBUTION.txt` differs).
- **The sample.** Every second block in windows of 0.16–0.4° (18–40 km), in two variants:
  - built-in: the author's world as it generates;
  - GLO-90: `FineElevation` 2, decision [0051](../decisions/0051-glo90-elevation-on-demand.md)'s rules with the summit
    lift, reading the author's converted store in `VintagestoryData/ModData/earth/glo90`.
- **Real.** Copernicus GLO-90 from `rawdata/copernicus-glo90` (the published tiles), bilinear onto the same points.
- **Measures:**
  - "Dry land": columns that are not sea, lake, river or river bank and valley.
  - "Tops": local highest points within 600 m that stand at least 150 m above that disc's lowest point.
  - "Column tops": the top block turned back into metres (half a block is about 4.9 m).
- **Files.** Scripts, results and figures are in [terrain-vs-glo90-2026-11-04](terrain-vs-glo90-2026-11-04):
  - `Sampler.cs` and `.csproj`, `render2.py`, `sheet.py`;
  - `SamplerNoise.cs` and `render.py` for the noise variants;
  - `orient2.py` for directions;
  - `StartupTimer.cs` for the start-up steps;
  - `run-server.sh` for the test server;
  - `results.json`.

## Built-in heights at 1:12 (the author's world)

| Place | Real tops | Built-in tops | Mean \|game − real\|, dry land | Highest built-in (real) |
|---|---:|---:|---:|---|
| Everest, Khumbu | 120 | 331 | 180 m | 9,111 m (8,721; survey 8,849) |
| Dhaulagiri, Kali Gandaki | 74 | 409 | 241 m | 8,479 m (8,116) |
| Matterhorn, Zermatt | 47 | 161 | 177 m | 4,672 m (4,463) |
| Dolomites | 64 | 131 | 171 m | 3,327 m (3,293) |
| Aconcagua | 91 | 239 | 159 m | 7,161 m (6,906) |
| Kilimanjaro | 16 | 382 | 139 m | 6,553 m (5,879) |
| Fuji | 8 | 183 | 82 m | 4,163 m (3,754) |
| Yosemite Valley | 34 | 142 | 144 m | 3,042 m (3,014) |
| Grand Canyon | 102 | 314 | 161 m | 2,722 m (2,648) |
| Nærøyfjord | 83 | 154 | 168 m | 1,678 m (1,756) |
| Glen Coe | 75 | 31 | 134 m | 797 m (1,121) |
| Middle Rhine gorge | 11 | 0 | 31 m | 516 m (578) |
| Gauja valley, Sigulda | 0 | 0 | 7 m | 128 m (149) |

- **Many small peaks: the detail noise.** In mountains the built-in world has 2–24 times the real number of tops.
  - `TerrainModel.Detail` adds three noises with wavelengths fixed in blocks (56, 150 and 260). At 12 m per block these
    are features 0.7–3 km across, below the 2′ grid's 3.7 km cell.
  - Their amplitude scales with the cell's relief (up to 2,500 m): ±0.22 × relief (fractal) and 0.4 × relief (ridged,
    offset by −0.45).
  - Around Everest the noise is ±144 m (standard deviation), and −444 to +301 m (1st–99th percentile); at Dhaulagiri
    ±209 m, −562 to +404 m.
  - The amount of relief below 3 km is about right: ±208 m in the game, ±239 m real at Everest. Its shape is not: round
    knolls everywhere instead of ridges and valleys.
  - Without the noise the terrain is closer to real (RMS 222 m instead of 235 m at Everest, 264 instead of 304 at
    Dhaulagiri), but has too few tops (50 and 35). The 30″ summit excess then shows as squares about 76 blocks wide:
    it is bilinear between 30″ cells.
- **The 45° streaks: partly the noise, partly Wmap.**
  - The noise is 2-D simplex noise. Its lattice is skewed along the diagonal, four of its twelve gradients are
    diagonals of length √2, and all octaves share one orientation. Lit from the north, the same game blocks show
    zigzag creases along both diagonals (`noise-light.jpg`).
  - In numbers the bias is small: 26–28 % of slope energy in the NE–SW sector against 23–25 % in the others, at 0.3–1.2 km.
  - Wmap lights from the north-west, which brings out NE–SW lines. Its paper style also hatches shadowed slopes with
    45° pen lines (`map.frag`, `h1` and `h2`).
- **Overshoot.** Built-in summits stand 30–670 m above GLO-90 (Kilimanjaro 6,553 m against 5,879 m; Fuji 4,163 m
  against 3,754 m). That is noise on top of the summit correction. At Everest the model reaches y 1,048. World
  generation clamps column tops at y 1,022, so about 770 columns (0.11 km²) end flat at the world's top.

## GLO-90 heights (Earth 1.0.3-tall.4, below)

| Place | Mean \|game − real\|, dry land (RMS) | Column tops (blocks) | Tops: game / real | River and valley band | Below real by more than 30 m |
|---|---|---|---|---|---|
| Everest | 0.4 m (2.0) | 4.9 m | 118 / 120 | none mapped | 0.0 % |
| Dhaulagiri | 0.3 m (0.6) | 4.9 m | 74 / 74 | 12.0 % of the area, mean 193 m below real | 9.7 % |
| Matterhorn | 0.4 m (2.5) | 5.0 m | 46 / 47 | none | 0.0 % |
| Dolomites | 0.3 m (1.4) | 4.9 m | 64 / 64 | none | 0.0 % |
| Aconcagua | 0.3 m (0.4) | 4.9 m | 91 / 91 | none | 0.0 % |
| Kilimanjaro | 0.3 m (0.5) | 4.9 m | 16 / 16 | none | 0.0 % |
| Fuji | 0.3 m (0.4) | 4.9 m | 8 / 8 | none | 0.0 % |
| Yosemite Valley | 0.3 m (0.5) | 4.9 m | 35 / 34 | none | 0.0 % |
| Grand Canyon | 0.3 m (0.6) | 4.9 m | 104 / 102 | 30.8 %, mean 154 m below | 23.2 % |
| Nærøyfjord | 0.3 m (0.3) | 4.9 m | 85 / 83 | none | 0.0 % |
| Glen Coe | 0.3 m (0.4) | 4.8 m | 75 / 75 | none | 0.0 % |
| Middle Rhine gorge | 0.3 m (0.3) | 4.9 m | 12 / 11 | 34.2 %, mean 73 m below | 22.1 % |
| Gauja valley | 0.3 m (0.3) | 5.0 m | 0 / 0 | 35.0 %, mean 17 m below | 8.3 % |

- **Dry land matches GLO-90.** The mean difference is the metre of undulation that 0051 keeps. RMS rises to 2–2.5 m
  where listed peaks are lifted to their survey (Everest 8,838 m at this sampling, the column itself 8,849 m). Column tops
  are within half a block. The number of tops equals the real one within two.
- **Rivers don't match.** Rivers keep the built-in courses (HydroRIVERS, straightened) and levels (ETOPO 1′), as 0051
  says. At 1:12 their bank and valley band is wide, and it is shaped down to the river over GLO-90's land. Where the
  built-in course leaves the real valley floor, it cuts through slopes:
  - In the Grand Canyon the Colorado's smooth course crosses the canyon's bends. River water lies a median 210 m below
    GLO-90's ground at the river's own columns.
  - In the Middle Rhine gorge a band about 3 km wide is levelled towards the river's level across the gorge's slopes
    (`rhine.jpg`); river water lies a median 47 m below GLO-90's ground.
  - Along the Kali Gandaki the valley's sides are cut by up to about 250 m.
  - On the Gauja the cut is shallower (17 m on average).
  - In these windows 8–23 % of the area lies more than 30 m below the real ground. This is the main remaining
    difference from GLO-90.
- **Coasts.** Fjords and lochs narrower than the built-in coast data are dry valleys at sea level: 0.6 % of the
  Nærøyfjord window and 0.5 % of Glen Coe's (0051, Consequences).

## Start-up time

The author's server log (tall.3): "3704590 river segments ... indexed in 92910 ms". Per step, with the same settings
(`StartupTimer.cs`):

| Step | Time |
|---|---:|
| Data | 0.6 s |
| Navigable straits | 2.7 s |
| River ends and deltas | 0.4 s |
| River network | 68.7–75.6 s; of it, straightening the courses 64.9 s |
| Wet-source flow and wadi fans | 0.7 s |
| Sea channels | 3.8 s |
| All | 84 s |

Straightening resamples each river every 4 blocks, so its work grows with the world's width. It ran on one thread.
`claude/river-index-speed` (`12b7fe7`, 3 November) runs it chain by chain in parallel, and sea channels on up to four
workers. It was not merged. Applied to tall.4 (it applies without conflicts):

- The model builds in 14.3 s instead of 77.7 s on the author's 32-thread machine (both measured in the same sampler).
  On the test server below: "indexed in 15510 ms".
- The terrain is byte-identical: 104 of 104 sample files equal (13 places × built-in and GLO-90 × top, elevation,
  water/river mask, water top).

## Test servers (1.0.3-tall.4)

Headless dedicated servers (VS 1.22.7, `run-server.sh`), with:

- the author's settings as the menu sets them (`tools/setup-tall-glo90-world.py`): Earth size 4,096,000, Earth-tab
  height 1024, sea level 110, linear, rock floor automatic;
- spawn on the Gauja (57.17° N 24.85° E), seed 12345;
- data packs 1.0.2, Cylinder World 1.0.4 and Hydrology 0.6.2-rivers.7;
- self-test places Everest, the Middle Rhine, the Gauja and the Nærøyfjord, 13 × 13 chunk columns each;
- GLO-90 tiles converted on demand from `rawdata/copernicus-glo90` into an empty store.

| Run | River index | Server run (start to stop) |
|---|---:|---:|
| tall.4, GLO-90 | 15.5 s | 121 s |
| tall.4, built-in | 14.2 s | 120 s |
| tall.3 (installed build), built-in | 79.2 s | 185 s |

- GLO-90 heights on every dry-land column sampled at all four places; mean |column − GLO-90| 0.2–0.5 m; 6–9 tiles
  converted, none failed, no square left on built-in heights.
- Everest: GLO-90's top 8,726 m, 150 m from the catalogue's point, lifted 125 m; that column 8,849 m, y 1022.
- `deposit sea agreement: PASS`. No errors.
- The packaged `earth-1.0.3-tall.4.zip` (sha256 `3020ca8d…8c373534`) on the same setup, at Everest alone: river index
  14.5 s, the same GLO-90 and summit lines, no errors. That zip was installed for the author at 22:27.
- All three runs logged 192,500 vanilla warnings "Dungeon at … would reach out of bounds with tile tiles-entrance"
  (refinery corridors at the Nærøyfjord and the Gauja). They come with tall.3 as well, so they are not from GLO-90 or
  the speed-up. Not looked into.

## Not checked

Rivers outside these windows, river levels and pools in a played world, a graphical client, and the other start-up
costs: spawn search, Wmap's world chart and climate raster.
