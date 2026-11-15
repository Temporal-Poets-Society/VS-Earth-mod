# 0025: Start-up memory: reuse the sea-channel search buffers, release start-up-only river data

Status: Agreed (the user, 2026-10-24); steps 1-2 built in Earth 1.0.0; in Earth 2.0.0. Step 3 proposed, not built as listed; measured on 2.0.0 (EA-0127, 15 November 2026): items 1 and 4 covered by later work, 2 and 5 in part, 3 not, left for a later performance round | Date: 2026-10-23 | Decided by: the user (proposed by Claude after a profiling investigation)

## Context

Dedicated servers on Earth worlds showed 0.5–0.9 s stalls on gen-2 garbage collections during chunk generation
(Devkit profiles).

The investigation is in [server-gc-stalls-2026-10-23](../research/server-gc-stalls-2026-10-23.md). It found:
- **The stalls are vanilla's.** The game forces a blocking, compacting full GC every 3 s while chunks unload on a
  server with no client connected. Vanilla stalls 350–580 ms the same way. With unloading off (as with a player
  online), Earth had no gen-2 collection during generation.
- **Earth makes each full collection ≈ 40 % longer.** It keeps ≈ 700 MB more live data than vanilla (1.43 GB against
  0.73 GB before generation). ≈ 200 MB and ≈ 4 M objects of that are river data only start-up and diagnostics read.
- **Earth's start-up allocates ≈ 20 GB** on the main thread. `SeaChannels.Route` allocates 24 MB of search arrays per
  river mouth. The heap peaks at 3–4 GB and the working set reaches 8 GB and stays there.
- **Per chunk column, Earth allocates about what vanilla does.**

## Decision

1. **`SeaChannels.Route` buffers.**
   - Allocate `dist`/`prev`/`land` and the priority queue once per `SeaChannels.Build` and reset only the touched
     states (no full `Array.Fill`).
   - Do the same for `RiverEnds.Search`.
   - The channels must come out identical: the same search from the same initial state.
2. **Release start-up-only river data** after `EarthParams.BuildModel`: `EarthData.Rivers`, `RiverNetwork.Reaches`,
   `lineX/lineZ` and `dbgBound/dbgCap`. `/earth checkrivers` and the self-test rebuild what they need on demand.
3. **Smaller follow-ups**, each independent (gains in the report):
   - shrink the terrain tile cache after start-up;
   - cut the river build's garbage and read files at their exact size;
   - compact the RiverNetwork pools;
   - bound the island edge cache;
   - smaller resident islands, climate and ore grids.

No world data or save format changes; generated terrain must stay byte-identical.

## Alternatives

- **One `GC.Collect` with large-object-heap compaction after `BuildModel`.** It returns the working set but leaves the
  8 s and the 20 GB. Interim only.
- **Patching vanilla's empty-server `GC.Collect`.** It changes vanilla behaviour for a case where nobody is playing.
  Not Earth's concern.
- **Server GC** (`DOTNET_gcServer=1`). It cut forced pauses to 138–178 ms in a test, but it is the operator's
  configuration, not a mod change. Worth documenting for server hosts.

## Consequences

- **Expected (estimates):**
  - start-up ≈ 5–8 s faster;
  - start-up allocation −19.7 GB (1) and −1.5 GB (3);
  - working set ≈ 8 GB → ≈ 2 GB;
  - resident memory −196 MB and −4 M objects (2), ≈ −350 MB with 3;
  - forced full collections ≈ 50–60 ms shorter (2), close to vanilla's with 3.
- **Not affected:** worldgen CPU. Earth generates ≈ 3× slower than vanilla. The user's play profile shows Earth's
  structure guards on `WorldGenStructure.TryGenerate` make vanilla's structure pass ≈ 5× as expensive; the report's
  proposal 9 covers it and is not part of this record.
- **Diagnostics after 2:** `/earth checkrivers` becomes slower, because it rebuilds what it needs.

## Implementation

Steps 1–2, on branch `claude/review-fixes-earth` (review round 4; not yet released):

1. `src/Core/SearchBuffers.cs`: the distance, predecessor and land arrays and the priority queue, made once per
   `SeaChannels.Build` and per `RiverEnds.Prepare` and handed to every `SeaChannels.Route` / `RiverEnds.Search`. `Begin`
   resets only the states the previous search reached (a touched list) and clears the queue, so every search starts as
   with fresh arrays.
2. After `BuildModel` and the start-up log (`EarthMapSystem.InitWorld`): `RiverNetwork.ReleaseStartupData` (Reaches,
   `lineX/lineZ`, `dbgCap/dbgBound`) and `EarthData.ReleaseRivers`. `EarthData.RiverReaches()` reads rivers.bin again from
   the mod folder or zip. `EarthParams.BuildRivers` (split out of `BuildModel`, unchanged) builds the same network for
   `/earth checkrivers` and the self-test's river check (`EarthMapSystem.WriteRiverCheck` → `RiverAudit.Run(model, rivers:)`).
   Columns, `segs` (read by Wmap) and every runtime query keep using the model's network.

Offline check (scratch harness kept with the review notes; 384-high world, ring 204 800, islands, coasts, lake and shore
overlays, river ends, deltas, channels; main `27399f1` against the branch):
- every hash of 160×160 columns at 12 places (computed after the release), settled areas, frozen-ground answers and
  terrain samples identical, so the channels and columns are byte-identical;
- the network built again equals the one built at start-up (619 723 reaches: kept flags and every course point);
- start-up allocation 23.87 → 2.93 GB, model built in 7.9 → 4.9–5.0 s, peak working set 3.55 → 2.56–2.70 GB, live heap
  after start-up 645 → 426 MB (−219 MB).

Not measured in a game or on the Devkit server.

## Measured on Earth 2.0.0 (EA-0127)

15 November 2026, at the author's request ("test it, if it does not take long"); nothing was changed.

**Dedicated servers.** Vintage Story 1.22.7 on .NET 10, workstation GC; Devkit's runner (`tests/server/run.py`, a scratch
copy that adds `/devkit gc` and the process's memory after start-up and after generation); seed 386217; `/devkit gen 8`
(289 columns), unloading on and no client, as in October's default runs; Earth main `753b818` with the play style's own
Earth tab, Cylinder World 1.1.0, Hydrology 0.6.2. "Retained" is `/devkit gc`, the heap after a forced full collection.

| | Vanilla | Earth 1:100 | Earth 1:10 |
|---|---|---|---|
| Retained after start-up | 703 MB | 1,965 MB | 2,469 MB |
| Retained after generation | 801 MB | 2,115 MB | 2,589 MB |
| Working set after start-up (peak) | 1,091 MB (1,354) | 2,837 MB (3,412) | 3,719 MB (4,891) |
| A forced full collection (`/devkit gc`) | 204 ms, 444 ms | 264 ms, 274 ms | 283 ms, 311 ms |
| Gen-2 collections, GC paused (whole run) | 16, 5.4 s in 65 s | 18, 6.2 s in 108 s | 19, 7.0 s in 118 s |
| Worst stall (all GC) | 559 ms | 813 ms (start-up) | 906 ms (start-up) |
| 289 columns generated in | 4.9 s | 26.7 s | 28.4 s |

Against October (Earth 0.16.5 on the built-in heights, 1.43 GB against 0.73 GB): Earth now keeps **1.26 GB more than
vanilla at 1:100** and 1.77 GB more at 1:10. What was added is mostly 2.0's GLO-90 layers and the sea referee (below).
A forced full collection is no longer longer than vanilla's (264–311 ms against 204–444 ms; October 490–860 against
350–580): the growth is in large arrays, which are quick to mark. Every stall over 100 ms in all three runs was a GC
pause; the longest are vanilla's forced collections on a server with no client (as in October) and Earth's start-up.

**What the model keeps, offline** (scratch probe: a 2.0.0 world's model built as a server builds it, from
`tests/SavedWorldDigestTests/worlds/2.0.0.params.txt`, columns read over a spawn area around Riga, then each part sized by a
reflection walk; approximate, and parts may share objects). 1.39 GB in all, of which the server frees 277 MB of river data
after start-up (`ReleaseStartupData`: reaches 186 MB, course lines 80 MB, debug arrays 10 MB), so about **1.1 GB** at
1:100. The larger parts: the river network 245 MB (segments 158 MB, pools 59 MB, its grid 19 MB); the coarse heights'
packed parts 131 MB; river flow 117 MB (the file kept whole, 94 MB, and its tiles 23 MB); the sea referee's island and
coast tables 144 MB (islands 86 MB, coasts 58 MB) and its data 66 MB; the coast layer 60 MB; lake outlines 60 MB; the
island layer 55 MB (its shapes 53 MB); river outlines 37 MB; GLO-90 tiles in their cache 31 MB; the 2′ terrain file 30 MB;
climate about 70 MB (codes and fixes 25 MB, fine temperature and rain 25 MB, weather 16 MB, Penman 7 MB); peaks 18 MB.
Outside the model, the ore heat maps take 60 MB once all 44 layers are unpacked (1.3 MB each; 3 MB as read). Start-up
allocates 3.1 GB (data and model, offline; October after step 1: 2.9 GB). The 1:10 world's extra 500 MB was not broken
down (the probe built 1:100 only).

**Step 3, item by item:**
1. *Shrink the terrain tile cache after start-up*: **covered** by the tile-cache rework (each cache bounded). After a
   spawn area's reads all tile caches together grew by 67 MB (GLO-90 tiles 31 MB, flow tiles 23 MB, coast tiles 2 MB);
   shrinking them after start-up would win little.
2. *Cut the river build's garbage, read files at their exact size*: **in part**. Store files are read whole at their size
   (EA-0092 A6) and a new world doesn't read rivers.bin (EA-0108). Start-up still allocates 3.1 GB and peaks at 3.4 GB
   working set (4.9 GB at 1:10): worth a look for start time and peak memory, not for what stays resident.
3. *Compact the RiverNetwork pools*: **not covered**, worth doing: 245 MB resident (segments 158 MB, pools 59 MB).
4. *Bound the island edge cache*: **covered** by EA-0113 (a per-thread last query and a cell grid): no island cache
   over 1 MB.
5. *Smaller resident islands, climate and ore grids*: **in part**. EA-0108 dropped the as-read islands and coasts
   (158 MB). Still: islands 139 MB (the layer's shapes 53 MB, the referee's 86 MB), climate about 70 MB, the ore heat maps
   60 MB.

Not in step 3 but as large: river flow kept whole (117 MB), the coarse heights' parts (131 MB), the sea referee (210 MB),
lake and river outlines (97 MB). These and items 2, 3 and 5 are left for a later performance round (EA-0127).

## History

- 2026-10-23: proposed with the investigation report.
- 2026-10-23: the user's 455 s play profile confirms that with a player connected gen-2 collections are background ones
  (16–40 ms per 5 s), and that start-up repeats the 4 GB/s allocation burst.
- 2026-10-24: Agreed (user, 2026-10-24): "then Decision 0025, start-up memory (E1-11) is no brainer - of course what is
  better for gamplay".
- 2026-10-24: steps 1–2 implemented on `claude/review-fixes-earth` (review round 4, finding E1-11); offline checks above.
- 2026-11-15: measured on Earth 2.0.0 on dedicated servers and offline (EA-0127, [above](#measured-on-earth-200-ea-0127)).

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: Agreed (the user, 2026-10-24); steps 1–2 implemented (branch `claude/review-fixes-earth`, not yet released), offline checks only; step 3 not started
