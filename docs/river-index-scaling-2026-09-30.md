# Earth 0.12.1: river-index scaling

Changelog: large worlds use a compact river lookup index, reducing startup memory without changing river geometry, widths, water levels, seams, or saved world data. This isolated candidate is based on the released Earth 0.12.0 source and assets. Shared project metadata remains at 0.12.0; the candidate mod/product version is 0.12.1 and DLL file version is 0.12.1.0. Its public assembly identity remains `EarthMap, Version=1.0.0.0`, preserving existing companion-mod references. Native inspection/reload testing is handled separately by the companion-mod runtime workflow.

## Why the lookup is equivalent

The previous index inserted every segment into every overlapping 128-block cell. River widths grow with world size, so entry counts grow approximately with the square of that scale. The new index groups `ceil(SizeScale)` legacy cells per bucket in each direction, with a minimum span of one. Each segment retains its exact former fine-cell bounds.

Both passes of `Nearest` reject segments outside the queried legacy cell. Segments remain in ascending original order, preserving winner ties and the order of floating-point additions in the water-level blend. Filtering only the first pass would change some ramps and water levels; the tests deliberately demonstrate this regression.

`DistanceToWater` visits coarse buckets but rejects segments whose legacy fine-cell rectangle does not overlap the original fine-cell query rectangle. This preserves exactly the previous candidate union. A repeated segment may be evaluated fewer times, which does not change the minimum distance. Negative cell grouping uses floor division, including exact boundaries.

No river selection, straightening, segment positions, mouth extension, pool/drop construction, width/depth formula, projection, or save format was changed. At standard scale the grid is unchanged; the four membership integers add 16 bytes per segment.

## Verification

The frozen oracle in `tests/RiverIndexTests/LegacyRiverNetwork.cs` is independent of the optimized index. Rejoining its unchanged class body with the original file header produces SHA256 `6fc3c1cce33c17e6ddc823e9c736723b77605dfe68c4a381b4c8602be8c86b2b`, matching the portable PDB document hash in both released Earth 0.11.0 and 0.12.0.

The suite passed 187,427 `Nearest` comparisons, 372,456 distance comparisons, and 10,962 candidate membership/order comparisons. Every floating-point `Hit` field is compared by its bit representation; integral and Boolean fields are exact. Reach lines, drop positions/levels, segment counts, and pool summaries also match.

Cases cover scales 0.5, 1, 1.01, 2, 2.3, 4, 15.625, 19.53125, 20, and 32; wrapping and flat worlds; all/ends/none stream selection; junctions at different levels; close parallel reaches; U-bends; score ties; zero-length segments; mouth extension; and seam guards. Queries cover positive/negative coordinates and exact fine/coarse boundaries with adjacent epsilon positions. A deliberately broken index with widened blend membership yields 190 water/ramp differences, confirming the tests detect the subtle regression this change must avoid.

Reproduce the suite:

```powershell
dotnet run --project tests/RiverIndexTests/RiverIndexTests.csproj -c Release -- tests/RiverIndexTests/results.json
```

## Actual world benchmark

The isolated benchmark reads the unchanged original 0.11.0 ZIP data with the inspection world's exact defaults: 4,096,000 × 2,048,000, height 384, sea 165, no Cylinder World, `ends`, minimum discharge 100/20, width 1.5, straightening 0.5, smoothing 2, and river relief 1. The segment count is the same 4,038,107 reported by the original native run.

| Measurement | Result |
|---|---:|
| Legacy index entries, exact calculated total | 741,531,182 |
| Compact index entries | 10,349,622 |
| Entry reduction | 71.65× |
| Compact buckets | 85,099 |
| Complete river construction | 174,872 ms |
| Peak benchmark process working set | 1,362,214,912 bytes |
| Retained managed memory added by network | 454,655,152 bytes |
| 50,000 sampled queries | 185 ms |

The legacy entry count is the exact sum of unchanged legacy membership rectangles, computed without allocating its enormous grid. The prior native Earth startup took 350,459 ms and exceeded 11 GB, but that process includes more than the focused benchmark, so these are not identical memory/timing scopes. Total temporary allocations remain about 15.3 GB across construction: pool/straightening work is unchanged and collected during the run. Large-world initialization still takes minutes; this is an index compaction fix, not a rewrite of river construction.

```powershell
dotnet run --project tests/RiverIndexTests/RiverIndexTests.csproj -c Release -- --real release/earthmap-0.11.0.zip tests/RiverIndexTests/real-dataset-results.json
```

## Isolated package provenance

Portable PDB hashes in released 0.12.0 matched all 31 other current source files, with no extra or missing compiled source. Only `RiverNetwork.cs` differs, and its original bytes match the released hash above. The candidate's own portable PDB matches all 32 isolated source files. Release assets were extracted from 0.12.0, and every package entry was hashed: only `EarthMap.dll`, `EarthMap.pdb`, and `modinfo.json` differ. No original ZIP or shared metadata was overwritten.

The isolated build is under `obj/river-index-0.12.1`, built with configuration `RiverIndex`, optimization enabled, portable PDBs, and explicit project properties `Version=0.12.1`, `FileVersion=0.12.1.0`, and `AssemblyVersion=1.0.0.0`. The assembly identity is checked before packaging; changing it prevents existing companion references from loading in Vintage Story. The normal `Release` packaging target was not invoked. The resulting installable candidate is `release/earthmap-0.12.1.zip`.

| Artifact | SHA256 |
|---|---|
| Preserved Earth 0.11.0 ZIP | `3ad6e7f4ecbc352ca8c323e9127352e37789b0d9b35f99aa6b1927d3f69b067f` |
| Preserved Earth 0.12.0 ZIP | `1a2eb7a459484c6287c5fd94c2957b3d9ef5b974743e5892476a51b1adb1b3e5` |
| Candidate Earth 0.12.1 ZIP | `c2b432f86157c78b11e7103846e98a0bfcd561a795df0d1464ceb5bef36e6081` |
| Candidate EarthMap.dll | `c30580fceca8bc3accc492fc4c69ec55e51c669dc3aebdfa8b8331e928b684ac` |

Machine-readable evidence is in `tests/RiverIndexTests/results.json`, `real-dataset-results.json`, `source-baseline-0.11.0.json`, `source-baseline-0.12.0.json`, `source-built-0.12.1.json`, and `package-audit-0.12.1.json`. The focused implementation patch is `docs/river-index-scaling-2026-09-30.patch`.

## Native companion inspection

The corrected ZIP passed Vintage Story 1.22.7 native integration with Earth Ruins 0.2.0-pre.1 in a
4,096,000 × 2,048,000 × 384 creative world. Fresh generation and default-configuration reload matched
44,972 sampled chiseled entities, 234,888 ordinary/air cells, and 10,224 terrain heights. The reload
performed zero terrain/geometry writes. Both sites retained their historical anchors; the existing
unmarked save guard also passed with no writes.

Native river indexing took 100,424 ms in the fresh run and 97,511 ms on reload. The fresh-run final
working set was 2,569,568,256 bytes; no complete peak was captured for that run. The separately observed
reload process peaked at 3,504,173,056 bytes. These include the game server and detailed ruins, unlike
the focused benchmark above; they do not measure client memory or frame rate.

Evidence is under `C:\VS mods\Earth ruins\quality\runtime\testruns`: fresh
`preview-20260930-003214-451`, reload `preview-20260930-003650-543`, and old-save guard
`preview-20260930-003214-525`. All three servers exited cleanly.

## Follow-up (2026-10-02): per-column lookup speed

A review found that the compact index, while exact, made `Nearest` (called for every terrain column from
`TerrainModel.ApplyRiver`) slightly slower than the legacy grid at normal world sizes. The index layout is unchanged by
this follow-up; only the query path changed.

### Profile

In river-dense areas, a query in chunk order scans about 40 candidates per 128-block cell at both scale 1.0 and 2.5. For
every candidate, legacy and 0.12.1 evaluated `Reach(q)`, which is three `HalfWidth` calls (three `Math.Pow`) plus two
`StreamT` calls (logarithms for streams), and evaluated `HalfWidth(q)` again for candidates in range. This transcendental
math, not the index, dominated the cost of 2–3 µs per column. 0.12.1 added a fine-cell test, 52-byte instead of
36-byte segments, and two integer divisions per lookup on top of that. Only about 15 (scale 1.0) or 28 (scale 2.5) of the
40 candidates are near enough to need a projection at all.

### Change

- **Per-thread cell cache.** World generation queries columns chunk by chunk, and a 32 × 32 chunk always lies inside one
  128-block cell. Each worldgen thread keeps its last 4 cells (`[ThreadStatic]`). An entry is keyed by a per-network id
  and the fine cell. It holds copies of that cell's candidates: the same coarse-bucket list, the same fine-cell filter,
  in the same order. For each candidate it stores the projection terms (computed with the expressions `Project` uses),
  `Reach(q)` and `HalfWidth(q)`. The index stays immutable and shared. A cache never references a network, so it cannot
  keep an unloaded world alive. The ring of 4 entries covers the 8-block settling margin around a chunk, which can touch
  up to 4 cells.
- **Exact box pre-test.** A candidate is skipped without projecting when the column lies more than `Reach + 1/64` outside
  the box of the segment's projected points (first pass), or more than `bestD + W + 1/64` outside it (blend pass).
  Rounding is monotonic, so `X1 + t·DX` never leaves that box. The 1/64 slack is orders of magnitude above any rounding at
  world coordinates, so every skipped candidate is one the exact test would also reject. NaN falls through to the exact
  test.
- **One evaluation of each width term.** `Reach(q)` computes `HalfWidth` and `StreamT` once. `Bank` and `Valley` share
  helpers with it, with the same operations in the same order. Cell construction evaluates the terms once per run of
  equal discharge (compared by bits). `DistanceToWater` does the same for `HalfWidth`.

Candidate membership and order, tie-breaks (`score >= best`), and the order of the blend's floating-point summation are
those of the legacy oracle. Every cached value is a pure function of the segment, so no result bit can change.

### Verification

- `tests/RiverIndexTests` passes: 1,563,683 `Nearest` comparisons, 372,456 distance comparisons and 21,924 candidate
  checks. The new candidate checks also compare the cache's own list against the legacy grid cell.
- The deliberately broken index (fine-cell membership removed) is still detected, with 190 water/ramp differences. The
  test now drops the thread's cache after mutating the index.
- A new cache-isolation case queries two live networks over the same cells alternately and from several threads
  (2.75 M comparisons). Removing the network id from the cache key makes the suite fail immediately.
- The real-data benchmark compares every `Hit` field bit for bit against the legacy oracle on every query of every pattern,
  plus `DistanceToWater` at 0/16/64/300 blocks: 2,949,120 + 449,392 comparisons per world with 0 differences. Its 4-thread
  runs also have 0 mismatches. At 4096k it compares against 0.12.1 instead, again with 0 differences.
- `tests/TerrainExpansionTests` passes with unchanged counts: 2,331,950 checks and 129,938 column comparisons.

### Measurements

The benchmark uses the real `earthdata/` and the settings of the earlier benchmark (minimum discharge 100/20, width 1.5,
`ends`, straightening 0.5, smoothing 2). It covers six river-dense places: Rhine, Nile delta, Amazon, Yangtze, Danube
and Daugava. Each place has 8 × 8 chunks of 32 × 32 columns, in generation order, giving 393,216 columns. The
`+margin` pattern adds the 8-block settling ring, giving 884,736 queries. `scattered` is 196,608 random columns within
4 km, nearly always a new cell, so it measures the cache-miss path. Figures are medians of 5 interleaved passes after
warm-up, single-threaded, in ns per query. The machine was shared with other jobs, so treat differences under about 10%
as noise.

| World (river scale) | Engine | Nearest, chunks | Nearest, chunks + margin | Nearest, scattered | DistanceToWater(64) | Build | Retained |
|---|---|---:|---:|---:|---:|---:|---:|
| 256000 × 102400 CW (1.0) | legacy 0.11 | 2,004 | 1,998 | 1,177 | 2,566 | 3.1 s | 165.2 MB |
| | 0.12.1 | 2,023 | 2,019 | 1,200 | 2,745 | 2.5 s | 178.4 MB |
| | **this change** | **145** | **144** | **734** | **1,762** | 2.4 s | 178.4 MB |
| 256000 × 102400 (1.25) | legacy 0.11 | 2,896 | 2,146 | 1,346 | 2,689 | 2.9 s | 169.3 MB |
| | 0.12.1 | 2,982 | 2,145 | 1,133 | 1,949 | 2.7 s | 174.5 MB |
| | **this change** | **205** | **162** | **622** | **1,237** | 2.6 s | 174.5 MB |
| 512000 × 204800 (2.5) | legacy 0.11 | 2,448 | 2,429 | 1,857 | 2,711 | 5.5 s | 218.4 MB |
| | 0.12.1 | 2,313 | 2,305 | 1,394 | 1,563 | 4.9 s | 203.2 MB |
| | **this change** | **197** | **209** | **705** | **889** | 4.9 s | 203.1 MB |
| 4096000 × 2048000 (20) | legacy 0.11 | not built (≈741 M index entries) | | | | | |
| | 0.12.1 | 6,593 | 6,549 | 2,464 | 1,859 | 144 s | 454.8 MB |
| | **this change** | **482** | **479** | **849** | **724** | 101 s | 455.1 MB |

With 4 threads, the chunk + margin pattern takes 35 / 36 / 47 / 115 ms, against 488 / 510 / 583 ms for legacy and 1,585 ms
for 0.12.1 at 4096k.

- The per-column lookup is 11.6–14× faster than legacy at 256k and 512k, and 13.7× faster than 0.12.1 at 4096k.
- The cache-miss path is faster than both older versions.
- `DistanceToWater` gains another 1.5–2.6× over 0.12.1.
- The index, and so its memory, is that of 0.12.1, keeping the 4096k-class reduction. The differences of 0.1–0.3 MB are
  measurement noise.
- Each worldgen thread adds tens of kilobytes of cache: 4 cells of typically 40 candidates at 104 bytes each, with arrays
  that grow to the largest cell seen.
- Build time is unchanged within noise. The 4096k build difference comes from a shared machine, not a design change;
  `Reach` itself is now cheaper.

`TerrainModel.ApplyRiver` still calls `HalfWidth`, `Bank`, `Valley` and `Depth` for each column near a river. These cost
about 60–80 ns per hit, which is now a similar order to `Nearest` itself. A per-thread memo of the last discharge would be
the next step if worldgen profiling shows it matters.

Reproduce the benchmark. It writes nothing unless `--out` is given; `--no-legacy --worlds 4096000x2048000` runs the
4096k class:

```powershell
dotnet run --project tests/RiverIndexTests/RiverIndexTests.csproj -c Release -- --bench --passes 5
dotnet run --project tests/RiverIndexTests/RiverIndexTests.csproj -c Release
dotnet run --project tests/TerrainExpansionTests/TerrainExpansionTests.csproj -c Release
```

The 0.12.1 column comes from a scratch harness that registered a renamed copy of the 0.12.1 `RiverNetwork` as a third
benchmark engine. That copy is not kept in the repository.

`tests/TerrainExpansionTests` now reads the checkout's own `earthdata/` by default. That data is byte-identical to the
data in `release/earthmap-0.12.1.zip`. A zip is used only when named with `--data <zip>`. It writes `results.json` only
with `--out <path>`. A single positional argument, as in the older command line, is read as the data folder.
