# Server GC stalls during chunk generation (2026-10-23)

This investigates why dedicated servers on Earth worlds stall for 0.5–0.9 s on gen-2 garbage collections while
generating chunks, and how much of it is Earth's doing and how much is vanilla's.

Nothing in Earth was changed. The worlds are new and disposable: `C:\VS mods\Devkit\testruns\*`. The fixes below are
**proposals**: see [decision 0025](../decisions/0025-startup-memory.md) (Proposed).

**Verdict.**
- **The stalls are vanilla's.** `ServerSystemUnloadChunks.OnServerTick` runs a forced, blocking, compacting full
  collection (`GCSettings.LargeObjectHeapCompactionMode = CompactOnce; GC.Collect()`) every 3 s on the main server
  thread. It does this whenever chunk columns are unloaded and **no client is connected**.
  - Devkit's `/devkit gen` loads columns with no player near them, so each 3 s unload pass forces one full GC.
  - Vanilla stalls the same way (350–580 ms).
  - With a player online that branch never runs. With unloading turned off (a stand-in for that), Earth had **no gen-2
    collection at all** during or after generation, and no tick over 64 ms.
- **Earth makes each forced collection longer** (490–860 ms against 350–580 ms). It keeps about **0.7 GB more live data**
  than vanilla (1.43 GB against 0.73 GB retained before generation). About 200 MB and 4 M objects of that are river
  data needed only at start-up.
- **Earth's per-chunk allocation is not the problem.** It is about the same as vanilla's per chunk column (≈ 2.7 MB
  against ≈ 2.5 MB), and Earth's own hooks allocate almost nothing; the rest is vanilla's work inside the methods Earth
  patches.
- **Earth's start-up is the real allocation problem.** In about 10 s on the main thread, `SeaChannels.Build` allocates
  **≈ 19.7 GB** of short-lived large-object-heap arrays (24 MB per river mouth, ≈ 824 mouths). This is most of the
  "750 MB/s" average. It also drives the heap to a 3–4 GB peak and the working set to **8 GB**, which the GC keeps
  committed afterwards.

## Method

All runs:
- Vintage Story 1.22.7 dedicated server, Devkit 0.1.0, seed 386217, workstation GC (the game's default).
- `/devkit gen 8`: 289 chunk columns around spawn.
- Earth worlds: Earth 0.16.5 + Cylinder World 0.11.14 + Hydrology 0.1.3, 256000 × 384 × 102400.
- Vanilla worlds: standard, 1024000 × 256 × 1024000.
- The runner is `Devkit/tests/server/run.py`. A scratch copy added pre- and post-gen console commands and heap dumps.

| Run | World | Variation | Purpose |
|---|---|---|---|
| `earth2` | Earth | default | the original report |
| `vanilla2` | vanilla | default | matched baseline |
| `earth4-dump` | Earth | `dotnet-gcdump` at start-up, mid-gen and after gen | retained types |
| `earth5-nounload` | Earth | `/chunk unload false` before gen | no forced GC (as with a player online) |
| `earth6-servergc` | Earth | `DOTNET_gcServer=1` | the same forced GC, parallel |
| `vanilla3-dump` | vanilla | unload off, full heap dump (`dotnet-dump`) | vanilla's live heap |
| `earth10-retained`, `vanilla4-retained`, `vanilla5-retained-h384` | both | unload off; `/devkit gc` before and after gen | retained heap; height 384 vs 256 |

The code audit of Earth's allocations (start-up step by step, per-chunk hooks, caches) used a scratch harness. It
compiled `src/Core` unchanged, loaded `earthdata/` and replayed `EarthParams.BuildModel` step by step, measuring
`GC.GetTotalAllocatedBytes` and live memory. Its figures are for the current source (0.16.6), which matches 0.16.5 in
these paths.

Full heap dumps of Earth servers failed: `dotnet-dump` returned "Value does not fall within the expected range" four
times, also under Server GC, while the same tool dumped the vanilla server. The gcdumps are lossy on .NET 10 (they
capped at 10 M objects), so their per-type sizes are only indicative. Earth's retained figures come from `/devkit gc` and
the harness.

## The stalls: vanilla's forced GC on an empty server

From `VintagestoryLib`, `Vintagestory.Server.ServerSystemUnloadChunks` (decompiled):

```
accum3s += dt;
if (accum3s >= 3f) {
    accum3s = 0f;
    FindUnloadableChunkColumnCandidates();
    if (mapChunkUnloadCandidates.Count > 0) {
        UnloadChunkColumns();
        if (server.Clients.IsEmpty) {
            server.serverChunkDataPool.FreeAll();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
        }
    }
}
```

The same collection also runs in `OnPlayerDisconnect` when the last player leaves. No suite mod calls `GC.Collect` in
production code.

Spike times confirm the 3 s cadence. Every spike was a single gen-2 collection:

| Run | Spikes at (s) | GC pause in each (ms) |
|---|---|---|
| `earth2` | 36.86, 42.75, 45.82, 48.87, 55.00 | 809, 492, 498, 506, 492 |
| `vanilla2` | 26.63, 32.63, 35.80, 39.01 | 582, 351, 354, 403 |
| `earth6-servergc` (Server GC) | 39.64, 45.72, 48.76, 51.83, 57.93 | 178, 152, 138, 150, 178 |

The intervals are 3.0–3.1 s, or 6 s when a pass found nothing to unload or the pause was short.

**Without the forced collection** (`earth5-nounload`, Earth, unloading off), during and after generation (t = 35–98 s,
4 056 chunks loaded):
- 0 gen-2 collections;
- gen-0/1 pauses of 20–100 ms per 5 s in total;
- server tick maximum 49–64 ms.

The 14 gen-2 collections of that session all fell during start-up. One 29.5 s tick is an artefact of the failed
`dotnet-dump` suspending the process.

So on a server with players online, chunk generation does not cause these stalls, on Earth or on vanilla. The stalls a
player can meet are:
- the forced collection when the last player leaves (nobody is left to notice);
- a player joining a server that has been empty while chunks unload: the collections run until the client is counted.

Players on an empty-then-joined server, admins watching the console, and Devkit runs all see them.

**Why Earth's forced collections are longer.** A blocking compacting full GC marks and moves the whole live heap,
including the large-object heap.

Retained heap after `/devkit gc` (a blocking full collection, `GC.GetTotalMemory(true)`), with unloading off:

| Run | Before gen | After gen (chunks loaded) | Full GC after gen |
|---|---|---|---|
| `earth10-retained` (Earth, height 384) | 1 432 MB | 1 613–1 621 MB (4 056) | 363–368 ms |
| `vanilla4-retained` (height 256) | 729 MB | 832 MB (2 312) | 266–267 ms |
| `vanilla5-retained-h384` (height 384) | 732 MB | 845 MB (3 468) | 269–271 ms |

Findings:
- Earth's extra ≈ 700 MB is resident before any chunk is generated. It matches the harness: ≈ 621 MB after
  `BuildModel` plus ≈ 70 MB of regional catalogs.
- The chunks themselves add about the same per chunk in both worlds.
- World height does not matter here: 384 instead of 256 adds 13 MB and 3 ms.
- A full collection is ≈ 100 ms (≈ 37 %) longer on Earth. The game's forced one also compacts the large-object heap,
  where Earth keeps more (tile cache, terrain.bin, segments), and was ≈ 40 % longer.
- A rough fit is ≈ 15 ms per million live objects. Earth's river reaches and pools are ≈ 6 M small objects; vanilla's
  whole heap is 6.1 M objects (`vanilla3-dump`: 868 MB live after generation).

The lossy gcdump of `earth4-dump` reported only 673 MB for Earth, so it undercounts. Its type lists still show the
same big Earth items: 620k `RiverReach`, 283k `Pools`, 180k `Island`.

## Allocation: Earth versus vanilla

| | Earth (`earth5-nounload`) | vanilla (`vanilla2`) |
|---|---|---|
| Allocated in the session | 46.0 GB in 100 s | 20.1 GB in 39 s |
| Start-up, from 10 s until the server runs (timeline) | ≈ 25 GB in 20 s (peaks 2.6–2.9 GB/s) | ≈ 3 GB in 10 s |
| Heap peak during start-up (`GetTotalMemory`) | 2.7–4.2 GB | 0.9 GB |
| Working set | 8.0 GB, kept | 1.4 GB |
| Worldgen handlers, during generation (measured) | 2.33 GB / 850 map chunks ≈ 2.7 MB each | 1.55 GB / 625 ≈ 2.5 MB each |
| Gen time, 289 columns | 14.2–15.1 s | 4.5–4.8 s |

### Start-up (Earth's own, on the main thread)

`EarthMapSystem.InitWorld` (the `WorldReady` run phase, EarthMapSystem.cs:78) calls `LoadData` and
`EarthParams.BuildModel` (:446–448). Devkit does not see this because it is not a mod-system load step. The log line
"829441 river segments … indexed in 6.5–9.5 s" is this step.

| Step | Allocated | Retained | Time |
|---|---|---|---|
| `SeaChannels.Build` → `Route()` (Core/SeaChannels.cs:340–348) | **19 729 MB** | ≈ 46 MB | 8.6 s |
| `RiverNetwork` constructor (of which `RiverStraighten` ≈ 1 150 MB) | 1 656 MB | 194 MB | 4.0 s |
| `EarthData.Load` (zip → files) | 749 MB | 298 MB | 0.7 s |
| `RiverEnds.Prepare` | 163 MB | ≈ 5 MB | 0.5 s |
| `RiverDeltas.Apply`, overlays, islands, coasts | ≈ 80 MB | ≈ 10 MB | |
| Regional catalogs (ore 55.5 MB retained, plants, fauna, trees, geology) | ≈ 114 MB | ≈ 70 MB | |

**How `Route()` allocates.** For a ship river the search window is 513 × 513 cells × 7 land layers = 1 842 183 states.
Each call allocates:
- `double[]` dist (14.7 MB), `int[]` prev (7.4 MB) and `byte[]` land (1.8 MB): 24 MB, all on the large-object heap;
- then it fills `dist` with +∞, although the Dijkstra usually stops after a few cells.

There are about 824 calls (854 ship channels, minus 252 mouths already deep).

### Per chunk column

Earth's own hooks allocate almost nothing. Devkit charges a Harmony-patched method's whole body to the patching mod, so
the per-call numbers include vanilla's work:
- **`WorldGenStructure.TryGenerate`** (4.5 KB/call, 640 MB in earth2, the "biggest Earth allocator"): Earth's prefixes
  (`StructureLatitudeGuard`, `SurfaceStructureGuard`, `RiverStructureGuard`, `UndergroundStructureGuard`) allocate 0 B.
  The bytes are vanilla's structure attempts (≈ 500 per column). In a vanilla world the same bytes show up as
  `GenStructures` self allocation (1.6–2.3 MB per call there, about 1.8 MB with Earth).
  - The prefixes are CPU-heavy, though: up to 33 × 33 `GetColumn` calls per surface attempt.
- **`GenRockStrataNew.GenChunkColumn`** (78 KB/call): Earth's postfixes `ApplyStrata` and `EarthOres.AfterStrata`
  allocate nothing per call; one 57 KB `ColumnCache` per thread.
  - The 813 ms first call is not explained by Earth's cold cost (under 100 ms measured). It is probably a blocking GC
    after the start-up churn landing in it (not verified).
- **`GenDeposits.GenChunkColumn`** (375 KB/call): mostly vanilla; Earth adds a small closure per deposit plan.
  - Ore attempts for `ScaleWithWorldheight` variants scale with `MapSizeY / 256`, as in vanilla: 1.5× at 384.
- **`GenTerra` (replaced)**: `bool[1024]` and `ushort[1024]` on coast and river columns only. One per-thread
  `Column[1024]` and `Column[2304]` (LOH, 98 + 221 KB) at first use.
- **Cache misses**:
  - a terrain tile decode is ≈ 0.71 MB (4 LOH arrays);
  - a coast tile ≈ 0.43 MB;
  - island edges, built on first touch and never evicted, ≈ 0.76 GB allocated in total and up to 175 MB retained once
    every usable island is built.

### Retained by Earth

The harness measured ≈ 621 MB after `BuildModel`, ≈ 690 MB with the regional catalogs. The largest parts:

| What | Retained | Needed at runtime? |
|---|---|---|
| River reaches (`EarthData.Rivers`, 620k `RiverReach` × 4 small arrays ≈ 3.1 M objects), `RiverNetwork.lineX/lineZ`, `dbgBound/dbgCap`, `Reaches` | ≈ 196 MB, ≈ 4 M objects | no: only start-up, `/earth checkrivers` and the self-test read them |
| Terrain tile cache (320 tiles × 356 KB), full right after start-up | ≈ 113 MB | partly; 48 tiles would do |
| `RiverNetwork` pools (404k objects with 4 mostly empty arrays) | ≈ 67 MB, ≈ 2 M objects | yes, but mostly empty |
| Ore heat grids (42 × 1.3 MB) | 55.5 MB | yes |
| `segs` (877k segments) | 45.6 MB | yes |
| Islands (180k outlines as `double[]`; 19k usable here) | ≈ 92 MB | about a third |
| terrain.bin kept whole (30 MB), coasts.bin (9.5 MB), climate as float (25 MB), weather (15 MB) | ≈ 80 MB | yes |

## Proposals, ranked

Expected gains are estimates from the measurements above. None is implemented.

### Earth

1. **Reuse `SeaChannels.Route`'s search buffers.**
   - **Change:**
     - Allocate dist/prev/land and the priority queue once per `Build`.
     - Reset only the touched states, with a touched list or a generation stamp; no full `Array.Fill`.
     - Do the same for `RiverEnds.Search` (RiverEnds.cs:199–206).
   - **Result:** the same Dijkstra from the same initial state, so the channels are identical. Check by comparing
     `SeaChannels.Build`'s output before and after on the real data.
   - **Gain:**
     - **−19.7 GB** allocated at start-up;
     - most of `SeaChannels.Build`'s 8.6 s, so start-up faster by ≈ 5–8 s;
     - no 3–4 GB heap peak; working set ≈ 8 GB → ≈ 2 GB expected.
     - fewer gen-2 collections at start-up: 14 on Earth against about 8 on vanilla.
2. **Drop start-up-only river data after `BuildModel`.**
   - **Change:** null `EarthData.Rivers`, `RiverNetwork.Reaches`, `lineX/lineZ` and `dbgBound/dbgCap`; rebuild them on
     demand for `/earth checkrivers` and the self-test.
   - **Gain:**
     - **−196 MB** retained and **≈ −4 M objects**;
     - each full collection (the forced ones, and any blocking gen-2 later) has that much less to mark: ≈ 50–60 ms
       less per forced collection, by the fit above (estimate).
3. **Shrink the terrain tile cache after start-up.**
   - **Change:** after start-up, set `MaxCachedTiles` from 320 to ≈ 48; pool the per-decode `raw`/`q` scratch per
     thread.
   - **Gain:**
     - **−95 MB** retained;
     - −356 KB of LOH garbage per tile decode during generation.
4. **Cut river build garbage.**
   - **Change:**
     - reuse per-chain scratch in `RiverStraighten`;
     - presize `List<Seg>`;
     - store reach xs/zs as float;
     - read files at their exact length (no MemoryStream → `ToArray` → copy chains; no extra copy in `LoadFromZip`).
   - **Gain:** **−1.5 GB** allocated at start-up, ≈ 1–2 s.
5. **Compact the RiverNetwork pools.** `Array.Empty` or lazy arrays for reaches without drops: **−40 MB, −1.5 M
   objects** retained.
6. **Bound island edges** (LRU, or float without the doubled smoothing copies): caps **up to 175 MB** of growth while
   players explore.
7. **Smaller resident data.**
   - island outlines as float offsets, only the islands this world can use: ≈ −60 MB;
   - climate planes as i16/u8: −14.5 MB;
   - sparse ore grids: ≈ −40 MB.
8. **Interim only:** one `CompactOnce` + `GC.Collect()` after `BuildModel` to return the start-up working set to the OS.
   - Untested.
   - Becomes unnecessary with 1.

Proposals 1–2 are the ones that matter for memory: 1 for start-up time and memory, 2 for every full collection
afterwards. 3–7 are smaller and independent. For worldgen CPU, proposal 9 (below, from the play session) matters most.

Taken together, 2, 3, 5 and 7 would bring Earth's resident extra from ≈ 700 MB to roughly 350 MB, and ≈ 6 M objects
fewer. A forced full collection on Earth would then be within ≈ 20–40 ms of vanilla's (estimate).

### Not Earth: vanilla and the test setup

- **Server operators** can run the server with Server GC (`DOTNET_gcServer=1`). In `earth6-servergc`:
  - the forced collections paused **138–178 ms** instead of 490–860 ms;
  - total GC pause was 2.2 s instead of 6.1 s;
  - working set peaked at 4.7 GB instead of 8 GB.
  - It uses more threads and memory per heap on small hosts.
  - This is configuration, not a mod change. It is worth a line in a server-hosting note.
- **A mod could stop the forced collection.** A Harmony patch on `ServerSystemUnloadChunks.OnServerTick` could skip or
  space out the empty-server `GC.Collect`.
  - It only matters while the server is empty, and it changes vanilla behaviour, so it is not proposed for Earth.
- **Devkit tests.** `/devkit gen` on an empty server measures vanilla's forced collections, not the mods.
  - Run worldgen profiles with `/chunk unload false` first, or have Devkit label induced collections separately.
  - Otherwise every profile's findings will say "mostly GC" for a reason no mod controls.

## A real play session

The Devkit session forwarded the user's own play profile (`%APPDATA%\VintagestoryData\ModData\devkit\profiles\20261023-210613`):
- 455 s in singleplayer, so client and server share one process and one heap;
- Earth 0.16.5, Cylinder World, Hydrology, Hansa, Sea, AstraTerra, Wmap, floralzones and others;
- 2 692 chunk columns generated with a player connected.

It confirms the conclusion: with a player online, gen-2 collections do not stall the server.
- **GC:** 35 gen-2 collections, all background ones. A 5 s window with one in it paused 16–40 ms in total.
- **Server spikes:** only 4 of the 134 spikes were server ticks (125, 178, 383, 549 ms). The 125 ms one, at join, was a
  gen-0/1 collection; the other three had no GC in them and were unmeasured engine work. The other 130 spikes were
  client frames of 50–211 ms, none of them mostly GC.
- **The 18.2 s of total GC pause:**
  - ≈ 7 s during start-up (Earth's start-up burst again: 4.2 GB/s at t = 20 s);
  - ≈ 2.5 s at shutdown;
  - the rest as many short gen-0/1 pauses shared by client and server.

What it adds:
- **Memory:** the heap swung between 4 and 7.7 GB, and the working set reached **22 GB** at t = 135 s. That came after a
  second burst of 4.5 GB/s while the player's area generated (4 300 → 5 000 chunks).
  - This is the client and the server together, with many mods, so it cannot be attributed from the timeline. Earth's
    start-up working set (8 GB) is part of it.
- **Worldgen thread CPU:** handlers took 4–5× longer per call than on the test servers, vanilla ones as well: `GenTerra`
  6.5 against 1.3–1.5 ms, `TryGenerate` 69 against 16–19 µs. That points at contention with the client and the other
  mods rather than at one handler. The relative costs are what matter:

| Per chunk column (worker) | Earth test (`earth2`/`earth5`) | vanilla test (`vanilla2`) | Earth play |
|---|---|---|---|
| `GenStructures.OnChunkColumnGen` incl. `TryGenerate` | 3.6–4.3 ms | 0.68 ms | ≈ 17 ms |
| `WorldGenStructure.TryGenerate` calls | ≈ 230 | (not hooked) | ≈ 240 |
| `GenRivulets.OnChunkColumnGen` (vanilla, unpatched) | 1.7–2.0 ms | 0.05 ms | 8.9 ms |
| `GenTerra` (Earth's replacement) | 1.3–1.5 ms | 1.6 ms (vanilla's) | 6.5 ms |

`TryGenerate` is the largest worldgen cost in play: 44.8 s self in 455 s. Earth (`StructureLatitudeGuard`,
`SurfaceStructureGuard`, `RiverStructureGuard`, `UndergroundStructureGuard`) and Cylinder World (`earthpolar`) both
patch it. Earth's structure guards make vanilla's structure pass about **5× as expensive**. They allocate nothing,
but they do up to 33 × 33 `GetColumn` terrain lookups per surface attempt.

**Proposal 9 (Earth, CPU):** make the structure guards cheaper.
- Answer the cheap rejections first (latitude, then the river or reservation box test), and sample the terrain
  footprint coarsely, for example a 5 × 5 grid or the corners plus the centre, before any full scan.
- Reuse one footprint result for attempts at the same spot.
- Expected: most of ≈ 3 ms per column on a test server and ≈ 13 ms in play. Not measured; the guards' rules must stay
  the same.

`GenRivulets` is vanilla code that Earth does not patch. Its spring attempts per column scale with `MapSizeY/32`
(1.5× at height 384) and with the rain in the climate map, and it skips columns colder than −15 °C. The vanilla test
spawn is evidently cold or dry, so its 0.05 ms is not a fair baseline. Earth's rainy temperate spawn and taller world
make more attempts. No Earth fix is proposed.

## Not covered

- **CPU** beyond the structure guards and rivulets above: Earth generates 289 columns in 12–15 s against vanilla's
  4.4–5.4 s on a test server. That is a separate investigation.
- **A dedicated server with players**, connected while chunks generate. `/chunk unload false` and the singleplayer
  play profile stand in for it.
- **Proposal gains** are estimated from the harness and the fits above, not measured on changed code.
