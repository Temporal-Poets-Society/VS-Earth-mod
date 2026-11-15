# Earth: the terrain model and water (src/Core) — independent review

Pinned tree: `scratchpad\earth-fable` at fef6166. Read-only for source; nothing committed. Every number below was
measured on this machine on 14 November 2026 with throwaway probes in
`scratchpad\fable\probes-terrain\` (`Probe\`: the pinned Core compiled as the test projects do, with `EarthParams.cs`,
so the model is built by `EarthParams.BuildModel` as a new world builds it; `ProbeFix\`: the same with three patched
copies of Core files, for the prototype in finding 1). The worlds probed are new Earth worlds at 1:100 and 1:10 (world
height 384, real seam, coasts, works of 1318 rules 4, GLO-90 with lake outlines 2, river outlines 4, flow rules 1,
channel rules 5, shore rules, dam rules 3), with GLO-90 tiles copied from the local store into a scratch folder. The
machine was shared with other sessions, so timings carry some noise (best of 3 warm passes; the same place measured twice
differed by up to 1.9 times on the cheapest columns).

Severity counts: **critical 0, high 1, medium 5, low 8, refactor 5.**

No crash, NaN or broken invariant was found in 816,000 probed columns, and no dependence on thread, order or cache
state (see "Checked and found sound"). The defects found are seams along data tile edges and the 180° meridian, and
per-column costs 10 to 400 times the normal one in three kinds of place.

---

## Findings

### 1. HIGH (performance) — columns in and near the sea of 1318 cost 120–535 µs each (normal: 1–3 µs)

`src/Core/TerrainModel.Coasts.cs:29-58` (`CoastDistanceAt`, the ring search at 43-52, the one-entry cache at 37-38 and 56),
`:61-68` (`Land1318`); `src/Core/TerrainModel.NaturalColumn.cs:91-139` (`WorksAt`, `EnclosedBySea`, the array made per call
at 126); `src/Core/TerrainModel.Rivers.cs:393-424` (`Trough`, the coast distance at each course's centre at 400-405),
`:332`, `:377`; `src/Core/TerrainModel.Shore.cs:56`, `:88`.

**Measured** (µs per column; a chunk is 1,024 columns, 5,184 with its river margin):

| Place | 1:100 | 1:10 | bytes allocated per column |
|---|---|---|---|
| Rotterdam, 96² warm (51.95 N 4.1 E) | 494 | 161 (all sea of 1318) | 5,936 / 737 |
| Rotterdam Nieuwe Maas, 128² warm | — | 420 | 882 |
| Hoek van Holland, 128² warm | — | 67 | 279 |
| Rotterdam 512², one pass | 120 | 421 | 1,557 / 4,273 |
| Rhine–Maas delta 384² (51.85 N 4.35 E) | 179 | 535 | 2,072 / 4,516 |
| Flevoland 384² (52.5 N 5.45 E) | 24.9 | 27.8 | 188 / 121 |
| Wadden / Afsluitdijk 384² (53.0 N 5.2 E) | 16.4 | 76.7 | 106 / 629 |
| Rotterdam 96² with the shore strip, rivers and islands off, works on | 162 | 150 | |
| the same with the works layer off as well | 2.4 | 1.2 | 0 |

So a chunk around Rotterdam takes 0.12–0.55 s of one worldgen thread instead of 1–3 ms, at both scales, over the whole
area the works file changes (the Rhine–Maas delta, the former Zuiderzee, the Wadden coast).

**Where the time goes.** With works rules 3 the sea's side near a SEA area measures its coast to the land of 1318 by a
search: up to 12 rings of 8–32 samples (162 samples at 1:100, about 290 at 1:10), each sample a `Works.At` (203 ns here),
an `EnclosedBySea` for every sample outside the areas (up to 48 more `Works.At`, and a 4-element array allocated per
call) and a `Coasts.SignedDistance`. One such search is 54 µs at 1:10 (`CoastSideAt`, measured). A column pays for it
several times:

- `CoastColumn` searches for the column's own point; the result is kept in a one-entry per-thread cache.
- `ApplyMouth` → `MaxNear` → `Trough` then asks the coast distance at the nearest point of *every course within reach*
  (`SeaSideAt(hit.CentreX, hit.CentreZ)`), a different point for every column: a full search each, and each one replaces
  the cache entry.
- `ApplyShoreStrip` (and `MouthShelf` on river columns) then ask the column's own point again, find the cache holding
  one of `Trough`'s points and search again. Measured at the Nieuwe Maas (1:10): 420 µs with the shore strip, 303
  without it, 131 without `ApplyMouth`.

**Fix, bit-identical (prototyped).** `ProbeFix\patched\` holds it; the columns of nine areas (1,114,112 columns at
1:100: Rotterdam, the delta, Flevoland, the Wadden coast, Zeeland, and the Daugava's, Amazon's, Elbe's and Thames's
mouths) are byte for byte the same in every field as the pinned tree's, and at 1:10 likewise (table 3):

1. `Trough`: ask the coast distance at the course's centre only where the result can depend on it. With no drop left
   (`drop * w == 0`) the function returns `own` whatever that distance is, and beyond `hw + widen + drop * w /
   OutlineRiverSlope` from the course it returns `own` too (the line's depth can only fall with that distance).
2. `Land1318`: ask today's outline before `EnclosedBySea`. Today's sea is not land of 1318 whether SEA areas enclose
   it or not, so the 48-lookup enclosure search is only needed for samples on today's land.
3. `CoastDistanceAt`: inside a SEA area, skip the rings wholly nearer than the area's nearest edge (every point there
   is in the same sea). **Trap:** `wk.Inside` is also true for a place `EnclosedBySea` made "inside" (its `Dist` is then
   a distance along an axis, not to an edge). My first prototype used `wk.Dist` and changed 5,714 columns of 1.1
   million; the byte comparison caught it. The raw `Works.At` hit must be asked.
4. Keep the last 8 searches per thread instead of 1, and do not let a search's own samples overwrite the
   `EnclosedBySea` entry of the column.
5. `EnclosedBySea`: the four axes as a static array (the allocations fall from 1,557 to 160 bytes per column).

Result (one pass, table 3): at 1:100 Rotterdam 120 → 44 µs, the delta 179 → 66, Flevoland 24.9 → 4.0, Wadden 16.4 →
3.9; at 1:10 Rotterdam 421 → 148–204, the delta 535 → 105, Flevoland 27.8 → 1.7, Wadden 76.7 → 5.2. No rules version
needed.

**What stays.** Columns within the coast's reach of an area's edge, and enclosed places, still run one full search
(44–66 µs per column around Rotterdam at 1:100, 100–200 at 1:10). Removing that needs the coast of 1318 as geometry (the areas and
today's outline combined once per tile into edges, measured by `CoastLayer`'s own index), which gives slightly other
distances than the ring search's steps of a twelfth of the reach: a new works rules version, new worlds only.

### 2. MEDIUM (defect) — islands that cross the 180° meridian are cut by a notch down to sea level

`src/Core/Islands.cs:207-231` (`Query`: one shape at a time), `:236-263` (`BuildEdges`), `:297-308` (`OnCut`: the cut's
points are kept), `:326-345` (`Distance` counts the cut as coast); `src/Core/TerrainModel.Coasts.cs:215`
(`h = hit.Height * SmoothStep(hit.Dist / hit.ProfileBlocks)`).

GSHHG cuts its polygons at 180°, and islands.bin holds each half as an island of its own: 4 outlines of 167,555 have
an edge on the meridian, the two halves of **Taveuni** (147.6 + 286.9 km², 992/980 m; the cut is 19.6 km long) and of
**Rabi** (10.7 + 55.7 km², 173/269 m; 4.8–5.0 km). The island profile rises from the nearest "coast", and the cut edge
counts as one, so the ground falls to a beach at sea level along the meridian. Measured at 1:100 (sea level 165), column
tops along z at 16.9° S, offsets −40 … +39 blocks from the meridian:

`210 211 177 164 164 b164 b164 b164 164 164 164 179 205 209` (b = beach flush with the sea)

and at all four cuts: 176–187 twelve blocks west, `b164` at the meridian, 184 twelve blocks east. The same with GLO-90
and with the built-in heights (the island layer sets the height). The big land's outlines are sound there: Chukotka
and Wrangel Island have a signed distance of the full reach on both sides and no step (probed).

**Fix.** Join the halves when the island layer is built: pair the outlines with edges on +180° and −180° whose cut
spans match, drop the cut edges, shift one half by 360° and make one shape (height the larger, width measured anew or
the larger). Or bake them as one outline (a new islands.bin). Either changes Taveuni and Rabi only, but it changes what a
saved world generates there: island rules 4, new worlds.

### 3. MEDIUM (defect) — the flow file is not interpolated across its 1° tile edges: a straight step in wide rivers' beds

`src/Core/RiverFlow.cs:150-173` (`At`): `Math.Clamp(i0 + di, 0, tile.H - 1)` and the same for columns at 163 take the
tile's own edge pixel twice instead of the neighbouring tile's first pixel. `:135-144` (`State`) also wraps a longitude
of 180 or more to the tile at −180 but keeps `lon - lon0` unwrapped (`AroundSpeed`, `TerrainModel.Rivers.cs:747`, can
ask there; this part by reading, not reproduced).

Within half a pixel (45 m) of a whole parallel or meridian the speed and direction are those of one pixel row, and
they jump to the next row's at the line. The same point asked 1e-9° either side of the line differs by more than 0.02
m/s at 790 of 879 wet points on 3° S in the Amazon (largest 0.249 m/s), 27 of 31 on 51° N in the Rhine (0.288 against
0.020 m/s), 287 of 318 on 19° E in the Danube (largest 1.33 m/s). The speed sets the mask water's depth
(`MaskWaterDepth`, `TerrainModel.Rivers.cs:681`: 3 blocks in still water to the full depth at 0.1 m/s), the still flag
and the flow labels, so at 1:10 (a pixel is 9 blocks):

| River water pairs straddling the line (1:10) | pairs | bed step, mean / max | 2+ blocks | still flips | flow turns > 45° |
|---|---|---|---|---|---|
| Amazon on 3° S, at the edge | 1,901 | 0.80 / 4 | 361 | 202 | 17 |
| the same 4, 13, 31 blocks on | ~2,000 | 0.03–0.05 / 2–3 | 3–4 | 2–9 | 0 |
| Lena on 130° E, at the edge | 466 | 0.35 / 3 | 47 | 30 | 24 |
| the same 4 blocks on (13 and 31 on: 19 two-block steps each, 4–6 still flips, 0 turns) | 819 | 0.14 / 2 | 1 | 2 | 0 |
| Volga on 48° N, at the edge (4 on: 0 turns) | 226 | 0.07 / 1 | 0 | 4 | 13 |

Narrow rivers and fast ones show nothing in the bed (Rhine, Elbe, Vistula, Daugava, Nile, the Danube on 19° E: steps
of at most a block at the edge as elsewhere; the Danube's speeds are above 0.1 m/s on both sides). At 1:100 a pixel is a block and the step cannot be told from the data's own.

**Fix.** Read the neighbouring tile's pixel where `i0 + di` or `j0 + dj` leaves the tile (a tile without flow counts 0,
as a pixel without flow does), and wrap `lon` before `lon - lon0`. It changes river columns within 45 m of whole-degree
lines: a new flow rules version (2) for new worlds; saved worlds keep the clamp.

### 4. MEDIUM (defect) — a lake's area is its piece in the current 1° tile, so rules that read it change along tile edges

`src/Core/LakeOutlines.cs:190` ("each lake's area in this tile"), `:436-468` (`Assemble` sums the rings of one tile).
Read by `TerrainModel.Shore.cs:62` and `:111` (a shore strip for lakes of `LakeMinKm2` = 1 km² or more),
`TerrainModel.NaturalColumn.cs:74` (`JoinsRiver`: a pool of at most 16 mask pixels is the river's water), `:484` (beach),
`TerrainModel.Rivers.cs:468` (`MaskPondJoins`: a pond no bigger than the channel's disc becomes river).

A lake cut by a tile edge has two areas. Of 514 lakes crossing eleven sampled tile edges (Finland, Sweden, Canada, the
Alps, Russia; 1:100), **192 have a piece under 1 km² on one side only and 139 a piece of 16 pixels or fewer on one side
only**. The shore strip then stops along the whole-degree line. Measured (dry columns within 1.5 blocks of the lake
that are beach or strip; water columns that carry the shore class):

| Lake (level) | piece north of 62° N | shore / water | piece south | shore / water |
|---|---|---|---|---|
| at 24.05° E (95 m) | 0.98 km² | 37 of 89 / 3 of 108 | 72.3 km² | 146 of 174 / 127 of 209 |
| at 25.11° E (97 m) | 0.017 km² | 39 of 142 / 0 of 113 | 16.4 km² | 175 of 217 / 180 of 490 |
| Näsijärvi (95 m) | 174.9 km² | 553 of 804 / 486 of 1,226 | 0.004 km² | 21 of 98 / 0 of 44 |

The pool rules were not caught changing a column (it needs the mask's river water beside such a sliver); by reading,
a sliver of a large lake beside a river within `MaskPondRise` blocks of its level becomes river water at the river's
level, with the lake at its own level across the line.

**Fix.** The whole lake's area: best in the bake (a field per lake piece; a new lake outline file version), or at
load by adding the areas of the pieces that share a cut edge in the neighbouring tiles. A cheap stopgap is to count any
piece with a cut edge as large. All change columns: shore rules 3 / a channel rules step, new worlds.

### 5. MEDIUM (performance) — inside a large island a column costs 5–65 µs: `Islands.Query` scans the island's edges, twice

`src/Core/Islands.cs:207-231` (`Query`), `:326-345` (`Distance`: every edge of every strip within ±cap, and inside an
island cap is `ProfileBlocks`, half the island's width); called by `TerrainModel.Coasts.cs:206` (`ApplyIslands`) and
again by `TerrainModel.Shore.cs:101` (`ApplyShoreStrip`).

Measured at 1:10 (96², all land on the island; µs per column):

| Island | full | without the shore strip | without the island layer |
|---|---|---|---|
| off Marajó (1,461 km², 1,713 points) | 19.3 | 7.5 | 0.56 |
| Isabela, Galápagos (979 km²) | 15.9 | 8.2 | 0.54 |
| Åland (871 km², 3,741 points) | 11.4 | 7.6 | 0.76 |
| Taveuni | 4.8 | 2.1 | 0.55 |
| a Maine island (45 km², 10,376 points) | 64.5 | 40.5 | 8.8 |
| Daugava's mouth (`Islands.Query` alone) | 753 ns per call of a 3.2 µs column | | |

The "without the island layer" column also has the rivers off (the layers come off cumulatively). On the Maine island
the coast outline is another 7.7 µs of the column (8.15 → 0.48 when the coast layer goes): an intricate coast asked
three times a column (finding 7). By the arithmetic the island cost per column is the same at 1:100 (the profile's
width and the strips scale together), so a chunk on Åland would be about 12 ms at any scale; measured at 1:10 only.

**Fix, bit-identical.** (a) Hand `ApplyIslands`' hit to the shore strip (a per-thread last hit by latitude and
longitude, or a field of the column): half the cost. (b) Give each island's edges a cell index like `CoastLayer`'s and
search outward ring by ring of cells until the best distance is within the ring: the minimum over the same edges is the
same number in any order. No rules version.

### 6. MEDIUM (performance) — the mask's river water costs 6–7 µs a column at 1:10; `ReachBlend` walks whole buckets, twice a column

`src/Core/RiverNetwork.cs:1091-1132` (`ReachBlend`: every piece of every bucket in reach is tested by its cell range,
and each one whose cells touch the search's is projected at 1110, with no box test on the piece itself), `:1083`
(`DepthNear`, a closure per call); `src/Core/TerrainModel.Rivers.cs:672` (`MaskWaterDepth` → `DepthNear`), `:731-732`
(`FlowingShare`, a closure per call), `:978`.

Measured at 1:10: the Amazon's meeting of waters (128², all river) 6.8 µs per column, 70 bytes allocated; 3.0 with
channel rules 2 (no `ApplyMouth`, the nearest course's depth); 0.75 without rivers. `Rivers.DepthNear` alone: 0.5 µs in
Kansas, 0.8 at the Daugava's mouth, 1.6 at Rotterdam, against 11–27 ns for `Rivers.Nearest`. At 1:10 a bucket is 20 × 20
cells (2,560 blocks square), so a 2 km search walks the piece lists of up to four such buckets for every column, and
`Nearest`'s per-thread list of a cell's candidates is not used.

**Fix, bit-identical.** The reaches near (reach, nearest distance, flow) gathered once per column and used by both
`DepthNear` and `FlowingShare` (they walk the same pieces in the same order); the box test `Nearest` has
(`x < MinX - within` …) before `Project`; a per-thread list of the pieces within the search of a fine cell, in index
order, as `Candidates` keeps for `Nearest`; static delegates instead of closures. The blend sums in the same order, so
nothing changes.

### 7. LOW (performance) — what every column pays: repeated lookups, the shore strip, `ApplyMouth` on open sea

`src/Core/TerrainModel.cs:385-408` (the pipeline), `src/Core/TerrainModel.Shore.cs:39-71`,
`src/Core/TerrainModel.Rivers.cs:306-327`.

- A land column asks the same place of the same layer two or three times: `Works.At` (NaturalColumn, ApplyIslands,
  the shore strip), `Coasts.SignedDistance` (CoastColumn, the shore strip, ApplyMouth), `LakeOutlines.At` (NaturalColumn,
  the shore strip; 62–86 ns each), `RiverOutlines.At`, `SampleTerrain` (77–165 ns), `Islands.Query`.
  On an intricate coast the repeats are the column: a Maine island at 1:10 (43.82 N 69.75 W) pays 7.7 µs for the
  coast outline alone (`Coasts.SignedDistance` is 23 ns inland, 162 ns at the Daugava's mouth).
- `ApplyShoreStrip` runs on every dry column and reads the OSM class and a 2-octave noise before it knows whether any
  shore is near: 0.2–0.5 µs, 15–25 % of an inland column (Kansas 1:100: 1.94 → 1.48 µs without it; Zermatt 1.97 →
  1.47). Testing the distances first (against the widest strip any class can have) and reading the class and the noise
  only then is bit-identical.
- On open sea `ApplyMouth` is about half the column (1.2 → 0.55 µs at both scales): `RiverOutlinesDecide`, the canal
  boxes and `AnyBucketNear` (77–141 ns at 1:10) run before the early return.
- At 1:100 GLO-90 is the largest part of an inland column (Kansas 1.04 → 0.30 µs without it): rules 4 read 2 × 2 smooth
  points per block. That is the rule, not waste.

A column context (the place's works hit, coast distance, outline hits and sample, made once and handed down) would
remove the repeats; see refactor 1.

### 8. LOW — river pieces are stored as floats: centre lines snap to 0.25 block (1:10) and 0.5 block (1:5) in the east

`src/Core/RiverNetwork.cs:276-290` (`Seg.X1 … Z2, S0, Len` are `float`), `src/Core/RiverSegmentBuilder.cs:187-194`.

A float has 24 bits: its step is 0.25 from x = 2,097,152 and 0.5 from 4,194,304. At 1:10 (ring 16,384 … 4,024,320)
every piece east of 17.6° E has its ends on a quarter-block grid in x; at 1:5 (to 8,031,232) on a half-block grid east
of 18.4° E; z likewise at half those steps in the southern half of the map. A piece's `Len` is the unrounded length. The error is
at most 0.125–0.25 block, a turn of up to 7° on a 4-block piece of a one-block stream at 1:5. By arithmetic; not shown by
a probe (it needs the unrounded courses, which the network drops). Doubles would add 16 bytes to each of 9.4 million
pieces at 1:10 (150 MB) and move pieces by a fraction of a block: a rules version if ever done. Listed so the limit is
known before a scale finer than 1:5.

### 9. LOW — `RiverStraighten`'s smoothing window never widens again without `cornerSigma`

`src/Core/RiverStraighten.cs:302-320`. `lo` and `hi` only move forward; the window is widened back only `if (corners)`
(310-312). Where the sigma grows from one reach to the next (a tributary joins: wider river, larger sigma, worlds on
the built-in heights with `RiverSmoothWidths`), the first points of the wider reach are averaged over a window cut off
on the upstream side at the narrower reach's 3 sigma. By reading; not reproduced. The courses of saved worlds depend
on it, so a fix is a rules step.

### 10. LOW — `FineElevation.Load` sets a tile aside as `.bad` on any `IOException`

`src/Core/FineElevation.cs:438-444`. A sharing violation or a read that fails once (another process writing or
scanning the file) is treated as a damaged file: the tile is renamed, counted absent for the session, and a generation
thread fetches it again or the square falls back. Only `InvalidDataException` proves damage. Not reproduced (the probes
read copies for this reason). Fix: rename on `InvalidDataException` only; on `IOException` return null without marking
`notHere`.

### 11. LOW — what the terrain owes to the machine's maths library and the runtime

`src/Core/VerticalScale.cs:65-66, 72-74` (`Math.Log`, `Math.Exp` for every block height), `TerrainModel.Coasts.cs:113`
(`Math.Pow`), `TerrainModel.Rivers.cs:802` (`Math.Cos`/`Sin`), `RiverCoursePreparer.cs:200`, `RiverStraighten.cs:318`
(`Math.Exp`); `RiverEnds.cs:198-265` and `SeaChannels.cs:566-666` (`PriorityQueue` with equal costs).
`PortableMath` exists because these differ in the last bit between Windows, Linux and macOS, and the weather uses it;
the terrain does not. A block height changes only where `Vertical.Y` lands within an ulp of a whole number (about one
column in 10^13), but the start-up layout (river courses smoothed with `Exp`, ways found by a queue whose order among
equal costs is the runtime's) can differ by more between a world's sessions on two systems or two .NET versions. Not
reproduced (one machine). If a world is ever moved between systems, the river audit would show it.

### 12. LOW — `SeamNoise` asked 7,919 blocks east of the column

`src/Core/TerrainModel.NaturalColumn.cs:361` (`SeamNoise(n2, x + 7919, z - 7919, …)`), `TerrainModel.Detail.cs:14-22`.
`SeamNoise` takes a position beyond the ring's end as "at the jump line": within 8,431 blocks west of the ring's end
the beach wobble of grid lakes is half this lap's noise and half the other's, and it jumps at the jump line itself.
Only a grid lake's beach edge reads it and no grid lake lies on 169.25° W, so no column is wrong today. Fix when
touched: wrap the offset position into the ring first.

### 13. LOW — Douglas–Peucker by recursion on whole chains

`src/Core/RiverStraighten.cs:448-467`. `Dp` recurses once per kept point in the worst case (a curve whose farthest
point is always next to an end); a chain is one point per 4 blocks, tens of thousands at 1:10, on a worker thread's
1 MB stack. Real courses split near the middle and the depth stays logarithmic; not reproduced. An explicit stack (as
`SeaChannels.RefineCourse` has at 862-878) removes the risk without changing a result.

### 14. LOW — `KeptLakeLevelNear` keeps one level per place

`src/Core/TerrainSampler.cs:59-88`, `TerrainModel.NaturalColumn.cs:407-420`. `StripGridLakes` keeps a terminal lake
only if its level equals the *highest* kept level within three cells; a lower terminal lake beside a higher kept one
would be stripped where the outlines decide. Today's data has no such pair: all 11,205 cells of the 18 terminal lakes
are at the kept level near them, and 6 cell centres are dry (edges). Latent; a set of kept levels instead of the
highest would close it.

---

## Structure: what is hard to change safely, and why

**R1. A column is assembled by layers that each start again from latitude and longitude.** `ColumnForExpansion`
calls seven steps in order, and each re-derives what an earlier one knew (the works hit, the coast's distance, the
outline hits, the terrain sample). Results travel through 30-odd fields of `Column` and through one-entry per-thread
caches. Finding 1's worst part is exactly this: a later step asks again what an earlier one computed, after a step in
between has evicted it. A `ColumnContext` struct made once per column (sample, works hit raw and enclosed, coast
distance, lake and river outline hits, island hit) and passed by reference would remove the repeats, the caches and the
order dependence between steps. It is bit-identical by construction if each value is computed by the same call.

**R2. Frozen rule versions are interleaved in one function body.** `ChannelRules` 0–5, `PoolLayout` 0–11, works
rules 1–4, `GenFixRules` 0–4, GLO-90 rules 1–5 and shore rules 1–2 branch inside the same methods. `ApplyRiver` (rules
0) and `ApplyChannel` (rules 1+) share about a hundred lines by copy; `OutlineRiver` has four depth formulas chosen by
three flags. Any edit must be checked against every combination a save can hold, and the offline tests check a
handful of places. What made this review's optimisation safe to try was a dump of every field of a million columns
compared byte for byte; the repository has no such golden test for the terrain. Suggested: a small set of saved
parameter sets (one per release that froze rules), a fixed list of areas, and a hash of every column field per set
checked in the test run. Then move old rules into their own methods, new rules never touching them.

**R3. One hit type with meanings that differ by caller.** `LakeOutlineLayer.Hit` serves lakes, river outlines and the
works of 1318; `WorksAt` returns it with `Inside` set for a place *between* SEA areas and `Dist` then a distance along
an axis (finding 1's trap), and `Area` is a tile's piece (finding 4). A separate result for `WorksAt` (kind: none, in a
SEA area, enclosed, LAND, LAKE; edge distance only where it is one) would make the wrong use a compile error.

**R4. What a column is depends on the thread.** `FineElevation.BuiltInOnly` and `FetchAllowed` are per-thread
switches, the model's per-thread caches are keyed by `CacheId` (which `MemberwiseClone` copies), `mouthModel` carries
the model to a static delegate. The code documents each, and the probes found the results the same on 16 threads; but
"a column is a pure function of its position" is true only with the tiles on the machine and the switches as generation
has them, and nothing in a signature says so. A reader argument (which heights may be read, may it wait) instead of
ambient switches would let `ColumnStore` and the river query state their premise in types.

**R5. The model is a bag of public mutable fields.** Rules, layers and caches are assigned after construction by three
systems (`BuildModel`, `AttachServer`, world generation), the lazy per-reach caches (`reachInMask`, `reachSpeed`,
`pondProfiles`, `fineSummits`) are filled by whichever thread asks first, and the probes switch layers off by assigning
null. A builder that returns an immutable model, with the lazy tables behind one small class that owns the "pure in the
reach" rule, would make the review comments at `TerrainModel.Rivers.cs:533-542` and `:1029-1031` unnecessary.

---

## Measured tables

Warm µs per column (best of 3 passes over the patch, row by row as a chunk fills), then the same with layers taken off
one after the other, cumulatively: shore strip, straits, sea channels, `ApplyMouth` (channel rules 2), rivers and
fans, islands, works, lake and river outlines, coasts, GLO-90. Each difference is what that layer costs there (the
column is then another column, so read them as attribution, not as an option).

### Table 1 — 1:100 (100 m per block), 96 × 96

| Place | cold | warm | −shore | −strait | −chan | −mouth | −rivers | −isl | −works | −outl | −coast | −fine | what the patch is |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Open ocean 0, −30 | 1.81 | 1.28 | 1.14 | 1.20 | 1.19 | 0.55 | 0.45 | 0.37 | 0.27 | 0.28 | 0.23 | 0.23 | sea |
| Daugava mouth 57.05, 24.03 | 27.6 | 6.70 | 5.73 | 5.78 | 5.73 | 3.55 | 3.20 | 2.17 | 2.03 | 1.59 | 1.09 | 0.40 | sea 53 %, land 23 %, river 9 %, bank 9 % |
| Rotterdam 51.95, 4.1 | 516 | 494 | 427 | 434 | 429 | 165 | 160 | 162 | 2.39 | 1.65 | 1.01 | 0.37 | sea 76 %, land 16 % |
| Amazon at Manaus −3.1, −60.0 | 22.9 | 3.03 | 2.70 | 3.15 | 2.45 | 3.03 | 1.59 | 1.43 | 1.48 | 1.08 | 1.00 | 0.31 | land 38 %, bank 36 %, river 23 % |
| Zermatt 45.98, 7.65 | 8.26 | 1.97 | 1.47 | 1.57 | 1.55 | 1.50 | 1.38 | 1.22 | 1.42 | 1.11 | 1.09 | 0.39 | land |
| Sahara 23, 10 | 10.7 | 1.62 | 1.39 | 1.30 | 1.40 | 1.25 | 1.31 | 1.28 | 1.14 | 1.13 | 1.08 | 0.31 | land |
| Kansas 38.5, −98.5 | 4.45 | 1.94 | 1.48 | 1.53 | 1.52 | 1.43 | 1.39 | 1.37 | 1.30 | 1.11 | 1.04 | 0.30 | land |
| Sognefjord coast 61.1, 6.0 | 5.55 | 2.25 | 1.77 | 1.84 | 1.92 | 1.84 | 1.72 | 1.77 | 1.61 | 1.37 | 1.03 | 0.38 | land 70 %, sea 28 % |
| Lake Geneva 46.45, 6.55 | 1.52 | 0.97 | 0.83 | 0.76 | 0.81 | 0.78 | 0.51 | 0.42 | 0.41 | 0.98 | 1.06 | 0.33 | lake |
| Lena 62.0, 129.7 | 10.9 | 2.76 | 1.96 | 2.34 | 2.41 | 2.24 | 1.81 | 1.82 | 1.83 | 1.06 | 1.01 | 0.30 | land 78 %, bank 11 %, river 9 % |

Model build at 1:100: 6.7–7.7 s, 2.77 million river pieces, 2.1 GB managed.

### Table 2 — 1:10 (10 m per block)

First run, 96 × 96, quiet machine:

| Place | cold | warm | −shore | −mouth | −rivers | −isl | −works | −outl | −coast | −fine | what the patch is |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Open ocean | 2.44 | 1.41 | 1.21 | 0.58 | 0.40 | 0.38 | 0.33 | 0.33 | 0.22 | 0.22 | sea |
| Daugava mouth 57.05, 24.03 | 17.8 | 4.11 | 3.07 | 2.01 | 1.89 | 1.04 | 1.06 | 0.82 | 0.55 | 0.45 | land 75 %, sea 20 % |
| Rotterdam 51.95, 4.1 | 171 | 161 | 158 | 142 | 151 | 150 | 1.15 | 0.71 | 0.57 | 0.38 | sea of 1318 |
| Amazon at Manaus −3.1, −60.0 | 19.9 | 1.91 | 1.37 | 1.13 | 1.10 | 1.08 | 1.16 | 0.74 | 0.69 | 0.36 | land |
| Zermatt | 4.62 | 1.36 | 1.05 | 1.01 | 0.85 | 0.99 | 0.93 | 0.68 | 0.63 | 0.47 | land |
| Sahara | 10.4 | 1.26 | 1.05 | 1.02 | 0.95 | 0.93 | 0.87 | 0.73 | 0.62 | 0.34 | land |
| Kansas | 3.95 | 1.08 | 0.88 | 0.86 | 0.80 | 0.78 | 0.75 | 0.56 | 0.50 | 0.35 | land |
| Sognefjord 61.1, 6.0 | 6.99 | 2.17 | 1.95 | 1.51 | 1.22 | 1.36 | 1.17 | 1.10 | 0.96 | 0.51 | land |
| Lake Geneva | 1.66 | 0.88 | 0.98 | 0.72 | 0.40 | 0.30 | 0.42 | 0.49 | 0.52 | 0.35 | lake |
| Lena 62.0, 129.7 | 6.85 | 1.13 | 1.15 | 1.12 | 0.78 | 0.72 | 1.04 | 0.48 | 0.51 | 0.33 | land |

Second run, 128 × 128, places put on the water (machine busier: the open ocean read 2.62 here):

| Place | cold | warm | −shore | −mouth | −rivers | −isl | −works | −outl | −coast | −fine | what the patch is |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Daugava at Riga 56.9465, 24.0915 | 25.4 | 3.55 | 3.43 | 2.43 | 1.24 | 1.35 | 1.34 | 0.79 | 0.42 | 0.41 | land 50 %, river 44 % |
| Daugava's mouth 57.0635, 24.0215 | 6.68 | 3.27 | 2.81 | 1.29 | 1.15 | 0.98 | 0.94 | 0.67 | 0.51 | 0.39 | sea 88 % |
| Rotterdam, Nieuwe Maas 51.9085, 4.486 | 476 | 420 | 303 | 131 | 145 | 141 | 1.23 | 0.71 | 0.40 | 0.38 | sea of 1318 |
| Rotterdam 51.95, 4.1 | 166 | 166 | 162 | 158 | 156 | 157 | 0.99 | 0.74 | 0.37 | 0.38 | sea of 1318 |
| Hoek van Holland 51.985, 4.115 | 69.5 | 66.6 | 63.4 | 52.8 | 50.0 | 51.7 | 1.42 | 0.86 | 0.44 | 0.38 | land 58 %, sea 28 % |
| Amazon, meeting of waters −3.137, −59.902 | 25.0 | 6.78 | 6.72 | 2.97 | 0.75 | 0.80 | 0.86 | 0.46 | 0.45 | 0.33 | river |
| Zermatt | 3.39 | 1.32 | 1.04 | 0.92 | 0.91 | 0.90 | 0.86 | 0.63 | 0.58 | 0.47 | land |
| Sahara | 5.53 | 0.79 | 0.62 | 0.89 | 0.55 | 0.55 | 0.54 | 0.66 | 0.60 | 0.24 | land |
| Kansas | 2.94 | 1.27 | 0.87 | 0.88 | 0.77 | 0.57 | 0.73 | 0.55 | 0.48 | 0.32 | land |
| Jūrmala beach 56.9755, 23.80 | 5.19 | 3.53 | 3.04 | 1.36 | 1.22 | 1.18 | 1.13 | 1.00 | 0.64 | 0.46 | sea 41 %, land 41 %, bank 11 % |
| Bergen harbour 60.397, 5.31 | 5.65 | 3.97 | 2.92 | 1.95 | 1.93 | 1.55 | 1.77 | 1.58 | 0.56 | 0.48 | land 44 %, sea 42 %, beach 15 % |
| Montreux, lake shore 46.431, 6.908 | 3.19 | 1.37 | 0.86 | 0.81 | 0.78 | 0.79 | 0.77 | 0.68 | 0.65 | 0.51 | lake 76 %, land 21 % |
| Rhine at Cologne 50.94, 6.965 | 9.32 | 2.03 | 2.19 | 1.36 | 0.75 | 0.77 | 0.92 | 0.48 | 0.45 | 0.27 | river 46 %, land 45 % |

Model build at 1:10: 15.4–19.6 s, 9.37 million river pieces, 3.2 GB managed, 3.5 GB working set.

Single lookups at 1:10 (ns per call, a 64 × 64 patch cycled):

| | Kansas | Daugava's mouth | Rotterdam |
|---|---|---|---|
| `Geometry.ToGeo` | 13 | 13 | 17 |
| `Data.SampleTerrain` with coast references | 77 | 114 | 165 |
| `Coasts.SignedDistance` | 23 | 162 | 87 |
| `Works.At` | 29 | 28 | 203 |
| `LakeOutlines.At` | 79 | 62 | 86 |
| `RiverOutlines.At` | 57 | 69 | 74 |
| `Islands.Query` | 18 | 753 | 84 |
| `Rivers.Nearest` | 11 | 11 | 27 |
| `Rivers.AnyBucketNear` (`ApplyMouth`'s reach) | 141 | 82 | 77 |
| `Rivers.DepthNear` (2 km) | 504 | 807 | 1,560 |
| `Fine.TrySmoothHeight` | 104 | 110 | 108 |
| `CoastSideAt` (works and the coast of 1318) | 95 | 254 | 53,615 |
| `GetColumn` | 793 | 3,162 | 68,893 |
| detail noise, `FbmTurned` / `RidgedTurned` (185 to 6 blocks) | 94 / 95 | | |

### Table 3 — finding 1's prototype against the pinned tree (one pass, µs per column; every field of every column equal)

| Area (columns) | 1:100 pinned | 1:100 prototype | 1:10 pinned | 1:10 prototype (two runs) |
|---|---|---|---|---|
| Rotterdam 51.95, 4.1 (512²) | 120 | 44 | 421 | 148 / 204 |
| Rhine–Maas delta 51.85, 4.35 (384²) | 179 | 66 | 535 | 108 / 105 |
| Flevoland 52.5, 5.45 (384²) | 24.9 | 4.0 | 27.8 | 1.7 / 1.9 |
| Wadden / Afsluitdijk 53.0, 5.2 (384²) | 16.4 | 3.9 | 76.7 | 5.2 / 6.7 |
| Zeeland 51.55, 3.9 (384²) | 15.8 | 12.5 | 11.6 | 5.1 / 5.5 |
| Daugava's mouth (256²) | 6.4 | 5.7 | 3.5 | 3.3 / 3.4 |
| Amazon's mouth −0.1, −50.3 (256²) | 7.8 | 7.4 | 3.4 | 3.6 / 3.2 |
| Elbe's mouth 53.87, 8.9 (256²) | 4.6 | 4.4 | 3.5 | 3.6 / 3.2 |
| Thames's mouth 51.48, 0.6 (256²) | 3.8 | 3.9 | 3.5 | 4.5 / 3.2 |
| bytes allocated per column, Rotterdam | 1,557 | 160 | 4,273 | 316 |

1,114,112 columns per scale, 95 bytes of fields each: `cmp` of the pinned tree's dump and the prototype's reports no
difference at 1:100 and at 1:10 (the 1:10 runs are the prototype without and with step 4; the machine was busier
during the second). The first, wrong prototype (step 3 with `wk.Dist`) differed in 2,968 columns of Rotterdam's 262,144
at 1:100, in `Top`, `Elev` and `Shore`.

Around Rotterdam itself the bit-identical steps leave 100–200 µs per column at 1:10: nearly every column there lies
within the reach (64 blocks) of an area's edge or between areas, and still runs one full search. That part needs the
rules-version fix.

---

## Checked and found sound

- **Order and threads.** 256,000 columns (ten 160 × 160 areas at 1:100: ocean, the Daugava's mouth, Rotterdam, Manaus,
  Zermatt, the Sahara, Kansas, the Sognefjord, Lake Geneva, the Lena) asked row by row, then shuffled on one thread,
  shuffled on 16 threads, and on a fresh model whose lazy per-reach tables were first filled by the 16-thread shuffled
  pass: every field of every column bit-identical, 0 differences in all four comparisons.
- **The real seam.** 64,465 columns one ring apart (x, x ± ring) identical; the step across the jump line is that of
  two neighbouring columns 40 blocks inside (St Lawrence Island mean |Δtop| 0.093 against 0.090, Antarctica 0.076 /
  0.071, max 4 / 4).
- **Poles, the antimeridian, odd coordinates.** 216,070 columns at and 60 blocks beyond both poles, on 180°, −180°,
  0°, ±90° and the seam meridian at every latitude, at negative, fractional and huge coordinates (to 1e15): none threw,
  none broke an invariant (top and water top inside the world, water above the bed, dry columns without water, finite
  elevation and flow, latitude and longitude in range, ice bottom valid).
- **A sweep of the Earth.** 600,000 columns at 3,000 random land-biased places: none threw, none broke an invariant.
- **Tile edges of the heights, coasts and outlines.** Fifteen whole-degree lines at 1:100 (Riga, the Alps, Holland,
  the Amazon, Chukotka, Wrangel, Fiji, the Lena, Geneva, Greenwich, the equator in the Congo): the step across each
  line is that of a pair five blocks on (except Fiji: finding 2). The coast tiles' margin (0.05°) is seven times the
  reach in degrees at the coarsest scale with coasts (0.0072–0.0083°).
- **Keys and indices.** Every `(long)x << 32 | (uint)z` key masks z; `TerrainReservationIndex` xors a `(uint)`;
  cell and bucket numbers use `Math.Floor` and floor division (`Bucket`); grid columns wrap with `% W` plus `W`; rows
  clamp. Tile-local float vertices of coasts and outlines are exact enough (a tile is 22,263 blocks at 1:5: 0.002
  block). No int overflow found at 1:5 in the cell arrays (the river outlines' index is 0.48 million cells a tile there).
- **The river index's 128-block cells.** A column's position along its river jumps by 10–20 blocks between neighbours in
  places (confluences: the tributary's tail against the main stem), but as often in the middle of a cell as on a cell
  boundary (Amazon 120 / 74 jumps over 2.5 blocks at boundary / mid-cell, Lena 27 / 90, Congo 37 / 34): no artefact of
  the cells' membership rule.
- **GLO-90's tile list against the grid.** No square without a tile has dry grid land above 0 m.
- **The start-up builds on several cores** (`RiverCoursePreparer`, `RiverStraighten`'s chains, `RiverPoolPlanner`'s
  basins, `RiverSpatialIndexBuilder`, `CellIndex`, `SeaChannels.FindWays`, `NavigableStraits.FollowSea`): each item
  writes its own slots, results are put together in item order, the cut-off search takes the farthest point whatever
  the order. Read, not probed across worker counts.
- **`TileCache`, `TerrainTileCache`, `ColumnStore`, `SearchBuffers`, `CoarseElevation`'s caches**: pure values keyed
  by number, per-thread shortcuts keyed by an instance number, eviction that cannot change a result.
- **`RiverSettling`**: what a column's settled state reads reaches at most 19 columns (edge 1, ramp 5, window 6 + 5,
  bank and ramp of those); the margin is 20.
- **`VerticalScale`, `EarthGeometry`, `EarthLayout`**: divisions guarded by `Sanitize` and `IsUsable`; `ToGeo` and
  `FromGeo` consistent at the ring's ends.

## Not reached

- `RiverPoolPlanner`'s forward pass (`LayOutReach`, gorges, falls) beyond its parallel structure; `RiverDeltas`,
  `RiverFalls`, `RiverSourceRepository`, `RiverOutlines`' bake-side flags.
- `WaterOverlays`, `LakeShores`, `LakeOutlets`, `TerminalLakes` loaders and `EarthBinary`'s bounds checks (only
  `TerrainOverlays` and the decode order in `TerrainTileCache` were read).
- `SeaChannels`' river exits in a world without coasts (1:200) were read, not run; `NavigableStraits` read, not run
  beyond the model build.
- 1:5 was not built (15.5 million pieces, 7 GB by decision 0068); 1:200 and worlds on the built-in heights only for
  the antimeridian probe.
- Another operating system or .NET version (finding 11), a download failing mid-generation (finding 10), saved worlds
  of older rule versions (every probe used the newest rules).
- `TerrainModel.Climate`, `TerrainModel.Ecology`, `KnownSummits`, `PeakCatalog`, the game-side passes.

## Probe files

`scratchpad\fable\probes-terrain\Probe\` (`World.cs`: the model as a new world; `Program.cs`: `cost`; `Probes.cs`:
`determinism`, `seam`, `poles`, `fuzz`, `edges`, `cells`, `misc`, `floats` (the antimeridian), `islands180`,
`flowedge`, `riveredges`, `lakeareas`, `lakestrip`, `dump`, `dumpdiff`), `ProbeFix\patched\` (the prototype), and
their outputs `out-*.txt` beside them. Run as `dotnet Probe.dll "<pinned tree>" <scale> <command>…`.
