# Rivers snapped to GLO-90's valley floors (RiverSnap)

Date: 4–5 November 2026. Task: move the river courses of `earthdata/rivers.bin` into the real valleys of Copernicus
GLO-90, take their levels from GLO-90 by `bake_rivers.py`'s rules, and write `earthdata/rivers-glo90.bin` in the same v2
format, at most about 25 MB. This note records the tool, the measured result and what the runtime still does to the
courses. Nothing in `src/` was changed by this work.

## Result

- **File.** `earthdata/rivers-glo90.bin`: 23,976,950 bytes (23.98 MB), SHA-256 `461aff76…6baed4`.
  - The same 619,723 reaches, in the same order, with the same `Q` and `next` as `rivers.bin`.
  - Vertices: 4,191,338. `rivers.bin` has 2,205,168 (×1.90).
- **Checks** (`RiverSnap`, over the whole file):
  - 0 joints apart: each reach's last vertex equals its next reach's first vertex.
  - 0 rises within reaches, 0 rises at joints. The 17 rises at lake or sea vertices are the same 17 as in
    `rivers.bin` (lake levels kept as they are).
  - 0 lake or sea vertices lost: every `fixed` vertex keeps its position (bit-identical float32) and level.
- **Courses** (stations every 90 m, lake and sea stations left out). Distance moved from the original line: p10 22 m,
  p50 108 m, p90 324 m, p99 904 m.
- **Levels.** Level minus the GLO-90 floor at the station: p1 −11.7 m, p10 −1.8, p50 0.0, p90 1.4, p99 8.0.
  - 0.16 % of stations stand more than 30 m above the floor; 0.22 % lie more than 30 m below it.
  - Level minus the original level: p1 −145 m, p10 −27, p50 −2.0, p90 4.2, p99 41.
- **Run** on the author's machine (32 threads), with the 5 size variants below: 266 s in total.
  - Snapping: 36.5 s, reading 21,827 tile loads (12.3 GB). The first full run, with a 2,500-tile cache, took 55.8 s
    for the same step.
  - Levels: 2.7 s. The size variants: 132 s. Brotli 11 on the final file: 87 s.
  - Peak working set: 12.7 GB with a 2,000-tile cache. The first run, with a 2,500-tile cache and 7 variants,
    peaked at 14.6 GB.

## Validation at eight windows

`validate.py` compares `rivers.bin` (before) and `rivers-glo90.bin` (after) against the published GLO-90 tiles in
`rawdata/copernicus-glo90`, read bilinearly. The courses are resampled every 30 m inside each window, as the runtime
reads them (levels linear between vertices). Rows show each window's main river: the reaches with at least half the
window's largest discharge.

- **Floor distance:** across the course, ±(500 + 4√Q) m every 15 m. The distance from the course to the nearest point
  within 2 m of that profile's lowest point.
- **Level − GLO-90:** the river's level minus GLO-90 at the course.
- **Rises:** vertex pairs whose level rises downstream.

| Window | Floor distance med/p90 (m), before → after | Level − GLO-90 median (p10…p90) (m), before → after | Cut > 30 m, before → after | Rises after |
|---|---|---|---|---|
| Grand Canyon 36.0–36.3N 112.3–111.9W | 405/585 → 15/30 | −170.3 (−447.7…64.4) → −4.8 (−14.9…2.3) | 77.5 % → 0.0 % | 0 |
| Middle Rhine 50.0–50.3N 7.55–7.85E | 0/120 → 0/0 | 11.2 (4.0…16.5) → 0.0 (−0.5…0.5) | 1.8 % → 0.0 % | 0 |
| Kali Gandaki 28.55–28.85N 83.4–83.8E | 30/210 → 0/30 | 22.1 (−174.5…57.0) → −0.6 (−10.3…5.1) | 29.3 % → 3.2 % | 0 |
| Gauja at Sigulda 57.1–57.25N 24.75–25.0E | 105/315 → 0/270 | 7.0 (−3.3…13.5) → −2.5 (−8.8…0.3) | 0.0 % → 0.0 % | 0 |
| Danube, Iron Gates 44.55–44.75N 22.0–22.6E | 0/0 → 0/0 | −8.5 (−10.4…6.5) → −5.4 (−8.0…0.0) | 0.4 % → 0.0 % | 0 |
| Rhône at Sion 46.15–46.3N 7.2–7.5E | 0/90 → 0/0 | 6.2 (−1.3…8.2) → −0.2 (−2.0…0.5) | 0.0 % → 0.0 % | 0 |
| Amazon near Manaus 3.2–3.0S 60.2–59.8W | 0/105 → 0/735 | 0.0 (−7.5…0.0) → 0.0 (−10.5…0.0) | 0.0 % → 0.0 % | 0 |
| Mississippi near St. Louis 38.5–38.8N 90.3–90.0W | 0/0 → 0/0 | 3.2 (2.0…4.0) → 0.0 (−0.5…0.5) | 0.0 % → 0.0 % | 0 |

There are 0 rises before or after in every window. In every window except Manaus the main river is the only river
of 20 m³/s or more. At Manaus all rivers together go from floor distance 0/45 m to 0/0 m.

What the numbers mean, window by window:

- **Grand Canyon.** HydroRIVERS runs about 2 km north of the canyon floor here. The wide first pass finds the canyon,
  so the river now lies in it rather than 170 m (median) below the rim.
- **Iron Gates.** The level stays below GLO-90 because the Đerdap reservoir stretch is de-dammed: its level is a
  slope from the reservoir's tail to the river below the dam.
- **Manaus.** The after p90 of 735 m comes from a branch of 97,500 m³/s south of the meeting of the waters
  (3.20–3.15S, 59.92–59.91W). Its line crosses land 15–38 m high in GLO-90 and stays there.
  - The search there is held by the width model: a half width of 4√Q = 1.2 km for one channel.
  - The data give that branch most of the Amazon's flow, so the real narrow channel is filtered out.
- **Gauja.** The p90 of 270 m: the profile's lowest point lies in another channel of the floodplain.

Before and after hillshades (before in red on the left, after in blue on the right; line width by discharge):
[grandcanyon.png](river-snap-2026-11-05/grandcanyon.png), [rhine.png](river-snap-2026-11-05/rhine.png),
[kaligandaki.png](river-snap-2026-11-05/kaligandaki.png), [gauja.png](river-snap-2026-11-05/gauja.png),
[irongates.png](river-snap-2026-11-05/irongates.png), [sion.png](river-snap-2026-11-05/sion.png),
[manaus.png](river-snap-2026-11-05/manaus.png), [stlouis.png](river-snap-2026-11-05/stlouis.png).
The numbers are in [results-final.json](river-snap-2026-11-05/results-final.json).

## Method (`tools/RiverSnap`)

A C# console tool built like `tools/Glo90Convert`: .NET 8 SDK, rolled forward to the installed .NET 10 runtime. It
links `src/Core/FineElevation.cs` and `src/Core/DemTiles.cs`.

1. **Read.** `rivers.bin` v2, plus the converted GLO-90 store read through `FineElevation`'s folder and tile list.
   - The store is `%APPDATA%\VintagestoryData\ModData\earth\glo90`, 26,475 tiles, read only.
   - The tool's sampler repeats `FineElevation.TryHeight`'s arithmetic. A self-test at start compares 28,000 points
     with `TryHeight`; the largest difference is 0 m.
2. **Chains.** Each river's main stem is one chain, from its source to its mouth or to its junction with a bigger
   river: 18,906 chains, nested up to 6 deep.
   - The main stem follows the biggest inflow at each junction.
   - Chains are processed main rivers first. A tributary's last station is put exactly on its main river's snapped
     junction station, so joints stay exact.
3. **Stations.** Every 90 m along the original line: 23,035,088 stations, about 2.07 million km.
   - Original lake and sea (`fixed`) vertices, reach starts and chain ends are stations of their own.
   - Lake and sea stations and mouths are held at their original positions.
4. **Course search.** Two passes of a Viterbi path across profiles perpendicular to a smoothed line.
   - **Pass 1.** Profiles ±(2000 + 8√Q) m, at most 4 km, sampled every 60 m. The line searched around is the
     original, smoothed with σ = 300 m. The profile directions come from the original smoothed with σ = 1500 m, so
     the profiles of a sharp bend don't converge.
   - **Pass 2.** Profiles ±(600 + 4√Q) m, sampled every 30 m. The line searched around is pass 1's course, smoothed
     with σ = 200 m. The profile directions come from that course smoothed with σ = 400 m.
   - **Cost at a profile point:** the GLO-90 maximum within 0.75 · 4√Q m along the profile. A wide river therefore
     only fits where the whole water width is low, which puts big rivers in GLO-90's flat water surface. Added to
     that: 4 m × (offset / 1 km)².
   - **Cost between stations:** λ·(Δoffset)²/Δs, with λ = 0.1 · (1 + 8√Q / 200), doubled in pass 1. The offset
     changes by at most 1.5·Δs between stations.
   - **Limits on each profile:**
     - it stops before it would cross a neighbouring station's profile on the inside of a bend;
     - it reaches at most half way to another river's original line, except within 2 km of a junction with that
       river.
   - The offsets are smoothed with σ = 90 m; constrained stations keep theirs.
5. **Floor.** At each station: the lowest GLO-90 within max(45 m, 0.5 · 4√Q) of the course. Then a morphological
   opening over ±2 stations along the river, which takes out bumps up to about 360 m long (bridges).
6. **Barriers.** Some stretches have a floor more than 50 m above the chain's own monotone fit of it (step 7). There,
   GLO-90 shows no valley the course could follow, and the course eases back to the original line over 10 stations
   each way.
   - The full run found 56,489 such stations (0.25 %) in 750 chains.
   - Examples: the Dadu at 29.3N 102.95E and the Usumacinta at 17.27N 91.43W.
   - A minimax search on the published tiles finds a barrier of 1,614 m and 253 m respectively between the valley
     floors above and below. The gorge is not in GLO-90 there, so no course can follow it.
7. **Levels.** `bake_rivers.py`'s rules, with GLO-90's floor in place of ETOPO's:
   - **Start.** The floor. Lake and sea stations take their original level.
   - **Dams.** De-damming as in `bake_rivers.py`, using HydroLAKES reservoirs (Lake_type 2, at least 1 km²; 5,394 of
     them) with its step limits scaled to the 90 m stations. 1,119 stretches (484,962 stations) became a straight
     slope from the reservoir's tail to the river below the dam.
   - **Monotone fit, per chain, main rivers first.** The least-absolute-deviation fit that never rises downstream
     (L1 isotonic regression).
     - Each stretch between lake or sea stations is fitted on its own.
     - It is held between the next lake or sea level downstream (or the main river's level at the junction) and the
       last lake or sea level upstream. Where these disagree, the lower bound wins.
     - A bump is cut through where that moves fewer metres than filling up to it, and a pit is filled where that
       does.
   - **Then, as in `bake_rivers.py`:**
     - raise each station to the lake or sea level downstream of it;
     - make the levels non-increasing;
     - smooth with 60 rounds of the mean of the station, its mean inflow and its outflow (lake and sea stations held);
     - raise and make non-increasing again.
   - Since a tributary's last station flows into its main river's junction station, no tributary ends below its
     confluence.
   - 18 lake or sea stations are lower than one downstream of them (data conflicts, kept as they are).
8. **Simplify and write.** Each reach is simplified with Douglas–Peucker between kept stations: the reach's ends and
   its original lake and sea vertices.
   - Tolerances: 30 m horizontally, 2 m in level, and no piece longer than 5 km.
   - Lake and sea vertices keep their original float32 coordinates.
   - Output: Brotli quality 11, window 24. The file is never written over an existing one.

### Why the levels use an L1 fit

The first version averaged a carved profile (never rising) and a filled one (never falling). Over the whole file,
0.62 % of stations then stood more than 30 m above the floor. In single places one ridge raised a river for tens of
kilometres:

- **Dadu.** 330 m above the floor for about 90 km above the barrier.
- **Usumacinta and Pasión basin.** 80–145 m above the floor.

The L1 fit cut that to 0.16 %, and the windows above stayed within 1 m of the first version except the Gauja
(median 0.1 → −2.5 m).

## Size against tolerance

Each line is the same run, simplified with a different horizontal (and vertical) tolerance and compressed with
Brotli 11/24:

| Tolerance | Vertices | Size |
|---|---:|---:|
| 15 m, 2 m | 5,863,942 | 31.35 MB |
| 20 m, 2 m | 5,085,404 | 27.98 MB |
| 25 m, 2 m | 4,568,834 | 25.69 MB |
| **30 m, 2 m (chosen)** | **4,191,338** | **23.98 MB** |
| 30 m, 4 m | 4,186,111 | 23.95 MB |
| 40 m, 2 m | 3,667,945 | 21.55 MB |

- **Vertical tolerance.** It hardly matters: between 1 and 3 m it changes the size by under 0.1 MB (first run).
- **Rounding.** Coordinates rounded to 0.0001° saved 0.2 MB (first run, 20 m).
- **Why 30 m.** It is the only tolerance in the 15–30 m range that stays under 25 MB. At 1:10 (9.8 m per block) that
  is 3 blocks, a third of a GLO-90 cell.
- **If a smaller tolerance is wanted.** 25 m gives 25.7 MB, and 20 m gives 28.0 MB, still within the 40 MB pack
  limit.

## Runtime notes (for RiverNetwork and RiverStraighten; nothing implemented here)

These were measured with [bench/StartupBench.cs](river-snap-2026-11-05/bench/StartupBench.cs), which builds
`RiverNetwork` from the worktree's current `src/`:

- A 1:10 world: ring 4,096,000 blocks, 9.78 m per block, size scale 20.
- River ends and deltas on, streams "ends", minimum discharge 100, width scale 1.5.
- Width rules 1, pool layout 11, waterfalls on, 32 threads.
- **Moved:** the laid-out course's distance from the reach it was laid out from, over every 10th kept reach.

| Data, rules | Network build | Vertices in → laid out | Segments | Managed memory kept | Moved p50/p90/p99 (max) |
|---|---:|---|---:|---:|---|
| rivers.bin, courses 0, straighten 0.5, smooth 2 (today) | 8.7 s | 1,404,541 → 4,695,548 | 4,292,266 | 580 MB | 57/510/1,326 m (25.9 km) |
| rivers-glo90.bin, courses 0, 0.5, 2 | 8.5 s | 2,638,533 → 4,799,279 | 4,395,996 | 511 MB | 51/527/1,376 m (26.3 km) |
| rivers-glo90.bin, courses 1, 0.5, 2 (the current 0053 code) | 3.6 s | 2,638,533 → 6,083,679 | 5,680,422 | 604 MB | 2.4/16/50 m (3.5 km) |
| rivers-glo90.bin, courses 1, 0.5, 0 (loop cut-offs only) | 4.2 s | 2,638,533 → 4,342,023 | 3,938,782 | 539 MB | 0/0/2.4 m (3.3 km) |
| rivers-glo90.bin, courses 1, 0, 2 (smoothing only) | 3.2 s | 2,638,533 → 6,085,137 | 5,681,880 | 604 MB | 2.4/16/50 m (562 m) |
| rivers-glo90.bin, courses 1, 0, 0 (none) | 1.8 s | 2,638,533 → 2,638,533 | 2,235,292 | 328 MB | 0/0/0 |

River ends and deltas took 0.2–0.5 s in every case. The bench process peaked at 3.3 GB.

1. **Course widths that grow with the world undo the snap.** With courses rules 0, `CourseHalfWidth` grows with the
   size scale, and the laid-out course leaves the snapped line by a p90 of 527 m and up to 26 km. The current 0053
   code already straightens with the water's own `HalfWidth` when `landCellBlocks > 0`. That brings the change down
   to a p90 of 16 m.
2. **What remains is the smoothing.**
   - With `smoothWidths` 2 the course still moves by p90 16 m and p99 50 m (1.6 and 5 blocks).
   - The 0.75-block Douglas–Peucker after it more than doubles the vertices: 2.64 → 6.08 million laid out, 5.68 million
     segments.
   - Proposal: no smoothing for courses ≥ 1. The data are already smooth: Gaussian offsets with σ 90 m, simplified at
     30 m.
3. **Loop cut-offs.** With strength 0.5 a loop is cut where two points of the river come within 2·hw·1.5. That is 1.5
   water widths, not the water's own width as the 0053 comment says.
   - The cut-offs move few points (p99 2.4 m) but cut whole meanders (up to 3.3 km).
   - Strength 0 switches the cut-offs off entirely (`if (strength <= 0 && smoothWidths <= 0) return;` and the
     `strength <= 0` test in the loop).
   - To cut only at the water's own width, the factor `(1 + strength)` must become 1 for courses ≥ 1, through a
     separate parameter.
   - The cut-off path also resamples every 4 blocks and simplifies at 0.75 blocks. With cut-offs alone the laid-out
     vertices grow from 2.64 to 4.34 million, though 99 % of laid-out points lie within 2.4 m of the data. Proposal:
     keep the original vertices of chains without a cut.
4. **Size-scaled lengths that remain for courses ≥ 1.** These do not move courses, but they are in blocks times the
   size scale rather than metres:
   - `FadeLength` (64 · SizeScale + 16 · hw);
   - the fade's pieces (4 · SizeScale);
   - `RapidsWindow · SizeScale` (±100 · 20 blocks ≈ ±19.5 km at 1:10);
   - the waterfall search reach (32 · SizeScale);
   - `indexSpan` (the river index's cell grouping).

   `MinPool` (32 · SizeScale) is not read by pool layouts 9 and later. Banks and valleys are already in metres for
   courses ≥ 1 (`BankOf` and `ValleyOf` with `landCellBlocks`).
5. **Start-up and memory with the denser lines.** The file has 1.90 times the vertices, and the kept reaches after
   river ends have 1.88 times as many (1.40 → 2.64 million). Measured:
   - Without straightening, the denser file builds faster and smaller than today's: 1.8 s and 328 MB against
     8.7 s and 580 MB.
   - With the current 0053 settings it builds in 3.6 s but keeps 604 MB, with 5.68 million segments (+32 % on today).
   - The segment count is what drives the river index's memory and per-column query cost.
6. **Barrier stretches cut deep.** Where GLO-90 has no valley (step 6), the levels are cut through the barrier. The
   runtime then carves a channel with the bank and valley easing of `ApplyRiver` through GLO-90 ground. At the Dadu
   (29.25–29.35N, 102.90–103.02E), the level lies up to 1,668 m below GLO-90 at the course, after the course went back
   to the original line over the mountain. A slot that deep and narrow is what the game will show there. A later step
   could widen such cuts or lead the course along the lowest col instead; that is not tried here.

## Limits

- **GLO-90 is a surface model.** Forest canopy over narrow rivers raises the floor. The L1 fit cuts through short
  stretches of it; the Gauja's level lies a median 2.5 m below GLO-90.
- **Branching rivers.** Where the data give one branch most of the flow, the width filter (4√Q) can hide the real,
  narrower channel (Manaus above).
- **Lost gorges.** Where a gorge is not in GLO-90, the course goes back to the original line and the levels cut
  through (750 chains).
- **Reservoirs.** Reservoirs are de-dammed only where they are HydroLAKES reservoirs on a river's main stem and not
  lake stations of the river data. A reservoir drawn as a lake keeps its lake level, and the stations above it are
  raised to that level.
- **Stations well above the floor.** 0.085 % of stations stand more than 50 m above the floor. Grouped by 0.2° cell,
  16 of the 25 largest groups lie in British Columbia (49–52.4N, 117–126W), for example around the Arrow Lakes. The
  others include Eritrea (14.1–14.3N 40.3E), the Dadu's pit above its barrier and Labrador. Their causes were not
  checked one by one.

## Files and how to run

- `tools/RiverSnap/`: `RiverSnap.csproj`, `Program.cs`, `Snapper.cs`, `Levels.cs`, `Monotone.cs` (L1 fit), `Glo90Grid.cs`,
  `RiverFile.cs`, `HydroLakes.cs`, `GeoShim.cs`.
  ```
  RiverSnap earthdata/rivers.bin data/glo90-tiles.bin %APPDATA%/VintagestoryData/ModData/earth/glo90 \
            rawdata/hydrolakes/HydroLAKES_points_v10_shp/HydroLAKES_points_v10.dbf earthdata/rivers-glo90.bin \
            quality=11 cache=2000 tol=30 vtol=2
  ```
  - Options: `box=latMin,latMax,lonMin,lonMax` snaps only the chains reaching into a box, for tuning.
  - `variants=tol:vtol:maxseg:quant,...` logs the size of other tolerances.
  - `dump=` writes the stations; `debug=lat,lon,prefix` writes one chain's profiles and passes as CSV.
- [river-snap-2026-11-05/validate.py](river-snap-2026-11-05/validate.py): the window validation and the PNGs, run as
  `python validate.py earthdata/rivers.bin earthdata/rivers-glo90.bin <out> final`.
- [river-snap-2026-11-05/bench/](river-snap-2026-11-05/bench/): the start-up bench above. Build it with `-o` and
  `-p:BaseIntermediateOutputPath` outside the repository.
