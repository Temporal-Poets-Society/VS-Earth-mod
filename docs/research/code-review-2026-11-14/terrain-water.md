# Earth code review, area: terrain model and water (src/Core)

Read-only review of `C:\VS mods\Earth` at `6371e32` (2.0.0-rc.13), 14 November 2026. Nothing was built or run.
Files read in full or in their hot parts: TerrainModel*.cs (all 11), ColumnStore, TerrainSampler, TerrainTileCache,
TileCache, RiverNetwork (queries, 560-1441), LakeOutlines (At/Scan/Inside), Coasts (SignedDistance/Build), SeaChannels
(Apply, Build's parallel part), ModelCache, ParallelWork, ConcurrentTools, SearchBuffers, TerrainReservations,
VerticalScale, FineElevation (flags and reads), PeakCatalog.Near; the parallel sites of RiverStraighten,
RiverPoolPlanner, RiverSpatialIndexBuilder; EarthMapSystem.ModelCacheInputs; tests/EarthMapTests `cost` mode.
Not read line by line: RiverPoolPlanner, RiverSettling, RiverCoursePreparer, RiverDeltas, RiverEnds, WaterOverlays,
TerrainOverlays, DamRemoval, CoarseElevation's decoder, EarthLayout, StoryTerrain, TerrainExpansion.

Marks: VERIFIED = the code was read and the mechanism is there as described; PLAUSIBLE = follows from what was read,
one link not checked. "Rules" = whether a fix changes what a saved world generates.

Background that decides what is dead code: Earth 1.0.x worlds still load and keep generating (README line 19), and new
worlds on the built-in heights get `ChannelRules = 0` (EarthWorldOptions.cs:169). So `ApplyRiver` (channel rules 0),
pool layouts 1-10, `FineRules < 5`, `GenFixRules 0` are all live paths, not dead.

---

## 1. Correctness

### T-1. `ReachInMaskRiver` caches a per-thread answer for every thread (HIGH, VERIFIED)
`src/Core/TerrainModel.Rivers.cs:514-546` (cache `reachInMask`, line 484), read through `MaskKeepsWet` (481) from
`DriesOut` (470), `TerrainModel.Climate.cs:162` and `EarthTrees.cs:259`.

```csharp
if (!RiverOutlinesDecide(lat, lon) || SeaChannels.InCanalZone(lat, lon)) continue;   // 539
...
System.Threading.Volatile.Write(ref known[reach], wet ? (byte)2 : (byte)1);           // 544
```
`RiverOutlinesDecide` -> `Glo90Decides` (NaturalColumn.cs:389-394) returns false on a thread with
`FineElevation.BuiltInOnly`. On such a thread every point is skipped, `inside` stays 0, and **1 ("not in the mask") is
written into the model-wide array for good**. Generation threads then read 1 and let the arid rules dry a reach the
water mask keeps wet: the dry bed beside wet water that the rule exists to prevent ("the Karkheh", comment at 510).

Threads that do this on the live server model:
- `StoryPlacer` (src/StoryPlacer.cs:101-102, 340-349): the story search sets `BuiltInOnly = true` and evaluates
  thousands of candidates with `model.GetColumn` (GroundMean, 379) before the first chunk. Any candidate ring within a
  river's reach in arid land, on a reach that is not `Masked`, poisons that reach.
- `Png.Render` (src/Core/Png.cs:170) and any Wmap sweep thread (`SweepThreadNames`, `EarthHeights.OnBuiltInHeights`)
  where the lazy array is shared (Wmap's `MemberwiseClone` snapshot shares `reachInMask` when it was already allocated
  at clone time; otherwise the snapshot poisons only itself, which still mixes its sweep answers into its near tiles).

It is also session-dependent: the story search runs only in the world's first start (`StoryPlaced`), so the same reach
can be dry in chunks of session 1 and wet in chunks of later sessions (a seam along chunk borders).

The same array also freezes `Fine.IsFallback` as it was when the reach was first asked (a square that falls back for
the session later, FineElevation.cs:196); that part is inherent to session fallbacks and minor.

Fix: never store an answer worked out without GLO-90. Smallest change, at the top of the compute path:
`if (FineElevation.BuiltInOnly) return false;` before reading or writing `known[reach]` (a sweep thread's own answer is
"not decided" anyway). `EarthVolcanoes.MapPlan` (src/EarthVolcanoes.cs:129-140) already does exactly this with
`BuiltInOnly` and `MissedReads`; copy that guard. Better: decide per reach once at start-up, while
`KeepDryReachCourses` has the reaches (TerrainModel.Rivers.cs:495), on the building thread with the squares' state from
`Fine.SourceHas/IsFallback` only (no thread flag), and store a `bool[]`.
Rules: none needed. Generation threads already compute the right value when they ask first; the fix removes the
order dependence. Worlds that got a poisoned reach in their first session keep those chunks.

### T-2. Other lazily filled per-reach caches: safe today, by accident of their inputs (LOW, VERIFIED)
`ReachSpeed` (Rivers.cs:652-676) and `PondProfileOf` (986-1003) are also "first thread that asks fills it". Both read
only thread-independent data (`Flow.At`, `RiverOutlines.At`, `LakeOutlines.At`, `Coasts.SignedDistance`), and
`PondProfileOf` is reached only where `c.RiverOutline == RiverInOutline`, which a `BuiltInOnly` thread never sets. So
they hold. They carry no guard and no comment saying which inputs they may read; one added `Glo90Decides` call would
reproduce T-1. Fix: a one-line comment on each ("no per-thread switch may be read here"), or move all three into one
`ReachFacts` object built in one place.
`peakBases`, `coarseBases`, `fineSummits` (LandHeights.cs:11, 114, 143): `FineSummitOf` correctly does not keep a peak
whose tiles were not readable (148-149). Good.

### T-3. Thread-statics that hold the model (LOW, VERIFIED)
`TerrainModel.Coasts.cs:70-71` (`coast1318Owner`) and `TerrainModel.NaturalColumn.cs:141-142` (`EnclosedEntry.Owner`)
store a `TerrainModel` reference per thread. Every other per-thread cache in the area was deliberately keyed by a number
"so an unloaded world is not kept alive by a thread" (TerrainModel.cs:20 `CacheId`, FineElevation.cs:120-127,
TerrainTileCache.cs:22, RiverNetwork.cs:731-733, ColumnStore.cs:59-62). A pool thread that last built a column near a
SEA area keeps the whole model (river index, layers, tile caches) of a closed world.
Fix: compare `CacheId` (a `long` field in the entry) instead of `ReferenceEquals(owner, this)`. Note for Wmap's
snapshot: a `MemberwiseClone` has the same `CacheId`, which is fine here (same layers, same answer).
Rules: none.

### T-4. `EnclosedBySea`'s one-entry cache is overwritten by the ring search it sits under (LOW-MEDIUM perf, VERIFIED)
`CoastDistanceAt` (Coasts.cs:38-50) walks up to 12 rings of 8-32 points and calls `Land1318` -> `WorksAt` ->
`EnclosedBySea` (NaturalColumn.cs:107-139) at each ring point; each of those replaces the single `enclosedCache` entry
(137). The column's own entry is gone when `ApplyMouth` (Rivers.cs:322), `MouthShelf` (357) and `ApplyShoreStrip`
(Shore.cs:57, 83) ask `WorksAt(c.Lat, c.Lon)` again, so each recomputes up to 8 directions x 6 steps of `Works.At`.
Only near SEA areas of 1318 with works rules >= 3 (the Dutch coast), but there a column costs thousands of `Works.At`.
Also `new[] { (1.0, 0.0), ... }` at line 126 allocates per miss.
Fix: see P-3 (compute `wk` and `d` once per column and pass them down); make the four axes a `static readonly` array.
Rules: none (same values).

### T-5. ModelCache key (no defect found; one thing to keep in mind) (INFO, VERIFIED/PLAUSIBLE)
`EarthMapSystem.ModelCacheInputs` (src/EarthMapSystem.cs:724-754) hashes the module version id, the mod version, the
mod's own data identity, runtime + architecture + OS, `dams`, `flow`, and the whole `EarthParams` minus the fields set
after the build. The two cached parts (straits' courses, sea channels' ways) are built in `BuildModel` before
`Fine`/`LakeOutlines`/`RiverOutlines` are attached, so tile presence does not reach them (VERIFIED: SeaChannels.FindWays
reads grid cells). `Ways.Read(r, exits)` re-checks the exits (SeaChannels.cs:479). Store files are pinned by the hashes
compiled into the DLL, which the module id covers (PLAUSIBLE: I did not check that every store file the build reads has
a compiled hash; the comment itself says "a few have no hash compiled in" for the mod's own files, which
`DataIdentity` covers).
Weak point by construction: anything `BuildModel` reads that is neither in `EarthParams` nor in the DLL (a future
config value, an environment variable such as a worker count that changes results) would not be in the key. Today
`ParallelWork` results are order-independent, so the worker count is not an input. Keep `ModelCacheInputs` next to
`BuildModel` in reviews; a test that builds twice with the cache off and on and compares `Ways` bytes would pin it.

### T-6. `GetSurfaceColumn` vs `GetColumn` (no defect) (INFO, VERIFIED)
TerrainModel.cs:353-361: `GetColumn` plus the crater layer, only lowering `Top`, skipped on water. It does not go
through `ColumnStore`, so a map thread never fills or reads the store (right: map threads may be `BuiltInOnly`).
`EarthVolcanoes.MapPlan` refuses to plan on `BuiltInOnly` threads and drops a plan whose planning missed a GLO-90 read.
One gap in the contract: a reservation moves `c.Top` (TerrainModel.cs:319-326) but leaves `c.Elev`, so on a companion
pad `Elev` and `Top` disagree by up to `MaxCutFill` blocks for every reader of `Elev` (alpine surface, ecology, shore
strip's `FlatShoreMetres` test runs before, in ColumnForExpansion, so it is not affected). Document it on `Column.Elev`
or set `Elev = Vertical.Elevation(Top)` under a new rules version if a reader turns out to care. LOW, PLAUSIBLE.

### T-7. Block conventions (no defect) (INFO, VERIFIED)
`WaterTopY` (TerrainModel.cs:298-302), `DryLandTop` (311), `RiverWaterTop` (Rivers.cs:10-14) are continuous through
0 m and agree for `RiverRelief = 1` except for the `+ 1e-6` in `RiverWaterTop`, which can put a river one block above a
lake at the same level when `Y(level)` is within 1e-6 below a whole number; `RiverLevel` (408-423) snaps the river to a
lake within 3 blocks, so nothing shows. `ColumnStore.OnGrid` (ColumnStore.cs:117-124) is careful about -0.0 and NaN.

---

## 2. Performance in the per-column path

Per column `ColumnForExpansion` (TerrainModel.cs:377-399) runs NaturalColumn -> ApplyIslands -> ApplyRiver/ApplyChannel
-> ApplyMouth (sea only) -> DryFans -> Channels -> Straits -> ApplyShoreStrip -> StoryTerrain. `ColumnStore` saves the
later passes' rebuilds (89 % of lookups, its own header) but not the first build.

### P-1. `ApplyMouth` does its full work on every sea column (MEDIUM, VERIFIED)
`src/Core/TerrainModel.Rivers.cs:306-345`, called from TerrainModel.cs:386 for every `WaterSea` column when
`ChannelRules >= 3` and the world has rivers.
```csharp
double trough = Rivers.MaxNear(x, z, within, hit => Trough(in hit, own, shore, within), own);   // 341
```
- The lambda captures `own`, `shore`, `within` and `this`: a closure object at method entry and a delegate per call,
  about 100 B of garbage per sea column (about 100 kB per open-ocean chunk).
- Before knowing whether any river is near it computes `RiverOutlinesDecide`, `InCanalZone`, `WorksAt`,
  `CoastDistanceAt` (-> `Coasts.SignedDistance`) and `DepthNoise` (2 simplex octaves).
- `MaxNear` (RiverNetwork.cs:1006-1066) rebuilds a `Candidate` for every segment of every bucket within
  `2 * reach + 60` blocks (up to 188) on each call; nothing is kept between neighbouring columns.

Fix, in order of value: (a) early return when `!mask` and no bucket within `within` holds a piece
(`Rivers.AnyBucketNear(x, z, within)`: the same `grid.TryGetValue` probes, no candidates): then `MaxNear` would return
`own`, `want == own`, and `top == c.Top`, so the result is bit-identical; move `CoastDistanceAt`/`DepthNoise` below
it. (b) replace the `Func<Hit, double>` with a loop over hits written into a per-thread scratch array (or a
`struct` callback), no allocation. (c) keep the last wide candidate list per thread keyed by (owner, bucket range), as
`Candidates()` does for cells (RiverNetwork.cs:754-801); the scan order must stay the same.
Rules: none for (a) and (b); (c) is safe only if the candidate order is kept (ties and sums depend on it).

### P-2. CR-R2 still holds and has grown: three whole-bucket scans per mask-river column (MEDIUM, VERIFIED)
`NearestCourse` (RiverNetwork.cs:944-998), `MaxNear` (1006), `ReachBlend` (1082-1123) each walk every bucket within
`within` and project every piece on each call; `OutlineCourseSearch` is 32-512 blocks (TerrainModel.cs:148). A column
of the mask's river water with solved flow (`OutlineRiver`, Rivers.cs:883-887, 935) calls
`MaskWaterDepth` -> `DepthNear` (= `ReachBlend`) + `BlendNear`, then `FlowingShare` (= `ReachBlend` again) when the
water around is slow, plus `NearestCourse` when no course's reach covers it. Wide rivers at 1:10 are thousands of
columns across.
Also `DepthNear` (1074) and `FlowingShare` (Rivers.cs:688-689) pass lambdas that capture `this`: one delegate per call.
Fix: one per-thread "wide" cache (owner id, bucket rectangle -> pieces in scan order, with their reach ids) shared by the
three; `ReachBlend` then needs no `Candidate` at all. Cache the two delegates in fields
(`depthOfReach ??= (r, q) => Depth(q)`). Keep the iteration order exactly (the blend's sum order and the reaches'
first-seen order feed floating-point sums).
Rules: none if bit-identical; prove it with a before/after dump of `GetColumn` over the 20-place probe set of 0074.

### P-3. The same lookups repeated down the column pipeline (MEDIUM, VERIFIED)
For one column near a coast in a rules-5 GLO-90 world:
- `WorksAt(lat, lon)`: NaturalColumn.cs:14; again Rivers.cs:322 (ApplyMouth) or 357 (MouthShelf); again Coasts.cs:211
  (ApplyIslands, works rules 4); again Shore.cs:57 or 83; again Rivers.cs:442 (MaskPondJoins), 1181 (LakeTopAt).
- `Coasts.SignedDistance` via `CoastDistanceAt`: Coasts.cs:99 (CoastColumn), Rivers.cs:322/357, Shore.cs:57/84.
- `LakeOutlines.At`: NaturalColumn.cs:26, Rivers.cs:446, 1189, Shore.cs:62, 101.
- `RiverOutlines.At`: NaturalColumn.cs:75 (JoinsRiver), 158 (MarkRiverOutline), Rivers.cs:315 (ApplyMouth), 781.
- `Islands.Query`: Coasts.cs:208 and Shore.cs:95.
- `Glo90Decides` (reads a thread-static and two table lookups): up to 5 times (NaturalColumn.cs:24, 50, 72;
  Rivers.cs:312; Shore.cs:99).
- `Data.SampleTerrain`: NaturalColumn.cs:11, Shore.cs:86 and 127, Rivers.cs:1185 (LakeTopAt, at the course's centre).
`ApplyShoreStrip` (Shore.cs:39-49) also computes `ShoreData.At` and a 2-octave noise for every column, sea included,
before it knows the column is within any strip.
Fix: a small `ref struct ColumnWork` made in `ColumnForExpansion` carrying what NaturalColumn already has (`s`, `wk`,
`d` (coast distance, NaN = not asked), `lk`, `rv`, `glo90Decides`, island hit), passed by `ref` to the Apply steps
instead of being asked again. In `ApplyShoreStrip` test the distance against the widest possible strip first and only
then read the class and the noise. Each value is the same call with the same arguments, so results are identical.
This also removes the need for the two thread-static one-entry caches (T-3, T-4).
Rules: none.

### P-4. CR-R10 partly holds: widths are carried, depth and stream size are not (LOW, VERIFIED)
- `HitAt` (RiverNetwork.cs:1237): `Depth(b.Q)` (Log, and Pow for streams) and `StreamT(b.Q)` (two Logs, one of them of
  constants: `Math.Log(MinQ / StreamMinQ)`, line 582) per hit per column.
- `BlendNear` (911): `Depth(segs[s.Seg].Q)` per candidate per column.
- `ApplyRiver` (channel rules 0, Rivers.cs:44): `Rivers.HalfWidth(hit.Q)`, `Bank`, `Valley` again (3 Pow, 4 Log)
  although the hit carries `HalfWidth`, `BankWidth`, `ValleyWidth` computed from the same `q` (PLAUSIBLE that they are
  bit-identical in rules 0: `Masked` is false there, so `hwHere = bc.HalfWidth`).
Fix: add `Depth` and `StreamT` to `Candidate` and fill them in `FillCandidate` once per run of equal `Q`, as `Reach`
and `HalfWidth` already are (785-792); precompute `1 / Math.Log(MinQ / StreamMinQ)` in the constructor only if the
result stays bit-identical (a division replaced by a multiplication is not: keep the division, hoist the Log).
Rules: none when the same expressions are evaluated once instead of many times.

### P-5. CR-T6 partly holds (LOW, VERIFIED)
`PeakCatalog.Near` (src/Core/PeakCatalog.cs:43-65) now walks `rowColumns` near the poles but still allocates a
`List` per call there (`new List<KnownSummits.Peak>()`, 50) and, elsewhere, still probes 3 x (2 x span + 1) = 9-51
dictionary keys per call. It is now called once per land column (LandHeights.cs:171 on GLO-90, or once in
`KnownSummits.Restore`), not twice.
Fix: skip rows whose `rowColumns[r]` is null in the enumerator; a per-thread reusable list for the polar path.
Rules: none.

### P-6. Small allocations and repeats (LOW, VERIFIED)
- `SeamEdges` is a class holding three `Column`s (TerrainModel.SeamGutter.cs:157): about 400 B per gutter column.
  Make it a struct passed by `ref`.
- `FineElevation.lookups` is a `ThreadLocal<StrongBox<long>>` touched on every height read (FineElevation.cs:303);
  `FineMean` does that 4-9 times per column. A `[ThreadStatic]` counter slot is cheaper; the count is only for the
  self-test.
- `GridColumn` and `CoastColumn` take `EarthData.TerrainSample` by value (NaturalColumn.cs:171, Coasts.cs:95); `in`
  where it is not changed.
- `Vertical.Y` is a `Math.Log` per call, 5-8 per column; fine, but `ColumnWork` (P-3) could carry `top` for `h`.

### P-7. What the benchmark measures (INFO, VERIFIED)
`tests/EarthMapTests/Program.cs:906-945` (`cost`) times `GetColumn` at five inland places (Zermatt, Riga, Amazon,
Sahara, Siberia) on the test model. None of them exercises `ApplyMouth` over open sea, `OutlineRiver` with solved flow,
`ApplyShoreStrip` at a coast, or a SEA area of 1318. P-1 to P-4 are invisible to it. Add places: open ocean
(0, -30), a river mouth with mask water (Daugava 57.05, 24.03), wide mask river (Amazon at -3.1, -60.0 with
RiverOutlines + Flow attached), Rotterdam (51.95, 4.1), a 1:10 geometry beside the 1:100 one, and print B/call as now.

---

## 3. Previous items: do they still hold?

| Id | Now | Evidence |
|---|---|---|
| CR-R2 | holds, wider | P-2: `NearestCourse` unchanged (RiverNetwork.cs:944-998); `MaxNear` and `ReachBlend` added with the same scan |
| CR-R6 | holds | raw uncapped `Parallel.ForEach` at RiverStraighten.cs:61 and RiverPoolPlanner.cs:315 (both with per-replica state, the pattern ParallelWork.cs:10-13 says to avoid), RiverSpatialIndexBuilder.cs:23, AridHydrology.cs:27 |
| CR-R10 | partly | P-4 |
| CR-T6 | partly | P-5 |
| CR-T7 | holds | TileCache.cs:59 `slot = (int)id & (TileCacheThread.Count - 1)` with `Count = 16` (163); `Used` stamped only in `Shared` (114), not on thread-slot hits. Since then RiverFlow (RiverFlow.cs:79) and Works (EarthParams.cs:609) added two more caches; each `EarthData.Set*` overlay change still makes a new `TerrainSampler` and burns an id (EarthDataView.cs:87). Only speed, never results (the key carries the cache's tag) |
| CR-T8 | holds | EarthDataset.cs:619-627: `float[] Elev`, `float[] Ice`, 11 B per cell; 320 tiles kept (EarthData.cs:32), about 115 MB at the bound |

CR-R6 fix: `ParallelWork.For(count, init, body, done, chunk)` at all four; the results are already written to per-item
slots. CR-T7 fix: 32 slots, stamp `Used` in `Miss` when the tile came from `recent` (one interlocked increment per
tile switch, not per lookup), and let `EarthData` apply all overlay choices in one `With`. CR-T8 fix: keep the 16-bit
codes and decode on read (`data.ElevationOf(code)`, already used by `ApplyPatches`, TerrainTileCache.cs:99); the decoded
value is the same float, so results are identical; saves about 40 MB at the bound.
Rules: none for any of them.

---

## 4. Structure

### S-1. TerrainModel is still the god object, now in 11 files (CR-A3) (MEDIUM, VERIFIED)
3,477 lines; one class owns the rule numbers, about 20 layers as public mutable fields, the column pipeline, climate,
ecology, companions' pads and expansions, lazy per-reach caches, and four `ConcurrentDictionary`s. Wmap takes a
`MemberwiseClone` of it (Wmap/src/Integrations/WorldSource.cs:75), and the tests set rule fields after construction
(`model.GenFixRules = gen`, tests/EarthMapTests/Program.cs:958). Both work only because everything is a public field
and nothing is derived at construction. T-1 is a direct result: a lazy cache inside a class whose answers depend on a
thread flag.
Safe steps, each on its own, none changing a column:
1. `ColumnWork` (P-3): the pipeline's shared per-column values in one struct.
2. Move the per-reach lazy facts (`reachInMask`, `reachSpeed`, `pondProfiles`, `dryReachCourses`) into a
   `ReachFacts` class with one documented rule about thread flags (T-2), held by reference so a clone shares it on
   purpose.
3. Move the pond profile builder (Rivers.cs:959-1168, 210 lines, no column state) into its own static class taking the
   three layers and the network.
4. Leave the public field names and `GetColumn`, `GetSurfaceColumn`, `GrassSeasonAt`, `ClimatePixel` signatures alone
   (reflection from Wmap, Hydrology, Cylinder World).

### S-2. Raw rules tests (CR-A6) (MEDIUM, VERIFIED)
Counted with grep over src: `TerrainModel.Rivers.cs` 24 raw `>= n` / `< n` tests, NaturalColumn 6, LandHeights 6,
Shore 5, Coasts 4, RiverNetwork 3; across src `ChannelRules >= 1` 11 times, `AridRiverRules >= 1` 10,
`PoolLayout >= 8` 7, `PoolLayout >= 9` 5, `GenFixRules >= 1` 5. Named constants exist for some
(`MouthChannelRules`, `FlowChannelRules`, `PondChannelRules`, `RealLapseRules`) and not for others in the same file
(`ChannelRules >= 1`, `GenFixRules >= 2`, `>= 3`, `WorksRules >= 3`, `>= 4`, `ShoreRules >= 2`, `FineRules >= 5`,
`PoolLayout >= 4/8/9`).
The codebase already has the right pattern twice: `SettleRules` (RiverSettling.cs:8) and `PoolRules.EasedBeds/Chutes`
(RiverNetwork.cs:1262, 1312). Plan:
1. `public readonly struct TerrainRules` with one named bool per behaviour, each documented where the constant is now:
   `Dredged` (channel >= 1), `MaskWidths` (>= 2), `MouthBed` (>= 3), `OneBed` (>= 4), `OneWidth` (>= 5),
   `DryLandLedgeFix` (genfix >= 1), `EasedDips` (>= 2), `LakeShareEdge` (>= 3), `Sea1318Distance` (works >= 3),
   `LateSeaIslands` (>= 4), `ShoreStrip2`, `BoundedFine` (fine >= 5), `LakeRims` (pool >= 4), `RapidsReaches` (>= 8),
   `CentreLineLakeLevel` (>= 9), `Arid`.
2. Built by one `static TerrainRules From(int channel, int genFix, ...)`; `TerrainModel` keeps the int fields (the
   tests and Wmap's clone set and copy them) but turns them into properties whose setters rebuild `rules`. A clone
   copies the struct with the fields.
3. Replace tests file by file, starting with Rivers.cs; a unit test asserts `From(v...)` equals the old comparisons
   for every version combination a world can have (channel 0-5, genfix 0-3, works 0-4, shore 0-3, fine 0-5,
   pool 0-11).
Rules: none; it is a rename of conditions. Risk is a mistyped threshold, which the table test catches.

### S-3. RiverNetwork (CR-A7) (LOW, VERIFIED)
1,441 lines after the builders were split out (RiverPoolPlanner, RiverSegmentBuilder, RiverSpatialIndexBuilder,
RiverCoursePreparer). Lines 700-1440 are the per-column queries and share nothing with the build but `segs`, `grid`,
`pools`. Make them `RiverNetwork.Queries.cs` (a partial, no behaviour change) and give `NearestCourse`, `MaxNear`,
`ReachBlend`, `DistanceToWater` one shared bucket walk: the 20-line loop with the "first bucket this search scans"
test is copied four times (956-977, 1017-1036, 1091-1112, 1397-1417).

### S-4. Outline code in three copies (CR-A12) (LOW, VERIFIED)
The same machinery (rings -> cell-bucketed edge index by counting sort -> inside test by parity from the cell centre
-> nearest-edge distance -> Chaikin corner cutting) exists in `CoastLayer` (Coasts.cs:195-290, 303, 340, 365),
`LakeOutlineLayer` (LakeOutlines.cs:324-408, 556-581, 755) and `IslandLayer` (Islands.cs:272, 310, 326). They differ
in real ways (lakes carry a lake id per edge and unions; coasts carry margins; islands are per shape), so a full merge
is not free. Safe first step: one `EdgeIndex` struct (CellStart/CellEdges build from a span callback) used by the
coasts and the lakes, whose two builds are line-for-line the same (Coasts.cs:240-252, LakeOutlines.cs:570-582). The
three `Smooth` functions must stay bit-identical to what worlds were made with: move them only with a test that
compares decoded tiles before and after.

---

## 5. Dead code and stale text

- Unused methods (grep over src, tests, and the Earth ruins, Underground cities, Wmap, Hydrology, Cworld folders
  finds only the declaration) (LOW, VERIFIED): `CoarseElevation.RowLat` (CoarseElevation.cs:60),
  `LonSpacingArcSeconds` (383), `HasTile` (515), `ClearCache` (604); `DamRemovalData.CourseRulesFor`
  (DamRemoval.cs:49); `FineElevation.FromKey` (FineElevation.cs:206); `RiverNetwork.OutlineDepthNear`
  (RiverNetwork.cs:878, only named in comments since `BlendNear` took its place).
- Test-only members living in production classes: `RiverNetwork.RapidsLengths`, `DebugReach`, `DebugShapes`, `Drops`,
  `CellCandidateIds`, `ResetThreadCache`, `IndexedExtent`, `ReachValleySlope`; `dbgCap`, `dbgBound0`. Fine to keep;
  an `#if` or a `RiverNetwork.Debug.cs` partial would take about 80 lines out of the main file.
- Rules branches that no world can have: I found none to delete. Channel rules 0 is new built-in-heights worlds;
  1-4 are rc.1-rc.12 worlds; pool layouts below 11 and `FineRules < 5` are 1.0.x worlds, which still load.
  If the author decides 1.0.x worlds no longer open in 2.0, the removable set is large (`PoolRules.Chutes` branch in
  `HitAt` 1312-1330, `Hit.Plunge/Lip/Rocks`, most `SettleRules` flags, `Detail` rules 0, `FineUndulation`), but that is
  a product decision, not a clean-up.
- Stale comments (LOW, VERIFIED): TerrainModel.cs:150 and 132, 123 still say "(proposed)" for 0053 items 17-19 and
  0053 itself, which have shipped; `ColumnStore.cs:35` says a column is 112 bytes (true only until the next field;
  `ChunkBytes` computes it, the comment need not); `ReachInMaskRiver`'s "(audit C F1)" and "(CR-R4)" notes describe
  the previous bug, not the present contract.

---

## 6. Memory

| What | Size | Note |
|---|---|---|
| Terrain tiles (`TerrainTileCache`) | 320 x ~360 kB = ~115 MB bound | CR-T8: 16-bit codes would make it ~75 MB |
| `ColumnStore` | 256 chunks x 115 kB = 29 MB | by design; `Column` is 112 B, 16 of them bool/byte flags that would pack into 2-3 bytes (about 96 B) |
| `keptNear` (TerrainSampler.cs:92-95) | up to 2^18 entries, ~10-12 MB | cleared whole at the bound; fine |
| Coast tiles | 96 tiles; at 1:5 a 1 degree tile is ~22,000 blocks, cell 64: ~121,000 cells x (4 B + 1 B) + edges | fine |
| Lake / river / works outline tiles | 24 each | fine |
| GLO-90 tiles (`FineElevation.Capacity`) | 24 x 2-3 MB = ~70 MB | plus one per thread (lastTile) |
| `IslandLayer` shapes' `Edges` (Islands.cs:261) | never dropped once built | grows with the islands visited in a session; small per island (LOW, PLAUSIBLE that it matters only on archipelago tours) |
| Per-thread: `threadCells` 4 x 32+ candidates (120 B each), `wideScratch`, `maxScratch` grow to the largest bucket scan and are never shrunk | tens of kB per thread, MB at 1:10 deltas | fine |
| Per-thread model references | T-3 | a closed world's model kept alive |
| `pondProfiles` | 6 float/bool arrays per reach asked, bins every 2 blocks | never dropped; a long river at 1:10 is ~100 kB per reach; fine |

---

## 7. Good

1. Determinism discipline in the river index: candidates are kept "in index order (the legacy order: it fixes ties and
   the blend's summation order)" (RiverNetwork.cs:723), `Project` on a candidate is the same operations in the same
   order as on a segment (1431), and the box test's slack is argued (717-721). This is what makes caching safe.
2. `ParallelWork` (ParallelWork.cs): long-lived workers, per-item result slots, results put together in item order;
   the comment says why `Parallel.For` with local state was dropped. `SearchBuffers` resets only touched states.
3. `ModelCache`: key = hash of the inputs, self-checking file, atomic replace, `read` may refuse a part, and the start
   never depends on it. The inputs deliberately leave out what is set after the build so the second start hits.
4. `ColumnStore`: clear contract for which thread may fill it (`FetchAllowed`), refuses an unsealed model, keyed by
   store number per thread, hands columns out by value.
5. `EarthVolcanoes.MapPlan`'s `MissedReads` guard is the right pattern for "a lazily kept answer must not come from a
   thread that read other heights"; T-1 is the one place it is missing.
