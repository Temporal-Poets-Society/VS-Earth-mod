# 0025: Start-up memory: reuse the sea-channel search buffers, release start-up-only river data

Status: Agreed (the user, 2026-10-24); steps 1–2 implemented (branch `claude/review-fixes-earth`, not yet released), offline checks only; step 3 not started | Date: 2026-10-23 | Decided by: the user (proposed by Claude after a profiling investigation)

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

## History

- 2026-10-23: proposed with the investigation report.
- 2026-10-23: the user's 455 s play profile confirms that with a player connected gen-2 collections are background ones
  (16–40 ms per 5 s), and that start-up repeats the 4 GB/s allocation burst.
- 2026-10-24: Agreed (user, 2026-10-24): "then Decision 0025, start-up memory (E1-11) is no brainer - of course what is
  better for gamplay".
- 2026-10-24: steps 1–2 implemented on `claude/review-fixes-earth` (review round 4, finding E1-11); offline checks above.
