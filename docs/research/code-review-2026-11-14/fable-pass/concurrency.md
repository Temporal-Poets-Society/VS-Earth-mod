# Earth: concurrency and state that outlives its owner

Independent review of the pinned tree `earth-fable` (commit fef6166), one concern: threads, per-thread switches, lazily
filled caches, statics across worlds and sides, Harmony patch lifetime, blocking waits. Paths below are relative to the
pinned tree; engine paths are relative to `C:\VS mods\devtools\vs-ref\decompiled\1.22.7`.

Counts: critical 0, high 1, medium 2, low 2, refactor 2.

Nothing here was reproduced in the game. Two claims were shown offline with probes in
`scratchpad\fable\probes-concurrency\` (results under each finding); the rest is the code path read end to end.

## Thread facts, as verified (they differ from the brief)

- **World generation is one thread by default.** `MagicNum.MaxWorldgenThreads = 1` (`VintagestoryLib\Vintagestory.Server\MagicNum.cs:117`;
  the installed `servermagicnumbers.json` also has 1), so `additionalWorldGenThreadsCount = 0` (`ChunkServerThread.cs:45`) and
  every pass of every chunk runs on the chunk thread ("chunkdbthread"). Only when an admin raises it do passes 1..N-1 each get a
  thread (`ServerSystemSupplyChunks.cs:66-73`), the chunk thread keeping pass 0 and the passes above.
- **The spawn area is generated on the server's main thread** before the game runs (`ServerMain.cs:1286` ->
  `ServerSystemSupplyChunks.InitWorldgenAndSpawnChunks`, line 1040), after WorldReady and before the RunGame phase. Earth's
  comments have this right.
- **Map regions are made before a chunk's terrain pass**, three by three: `loadOrGenerateChunkColumn_OnChunkThread` calls
  `GetOrCreateMapChunk` first (`ServerSystemSupplyChunks.cs:347`), which creates the chunk's region and its eight neighbours
  (`:712-735`), also when it only loads a saved map chunk (`:855-863`).
- **Mods are disposed before the server's threads are told to exit.** `ServerMain.Stop` (`ServerMain.cs:1705`): `Suspend(true, 10000)`
  (1754), then the Shutdown phase, in which `ServerSystemModHandler.OnBeginShutdown` disposes every mod (`ServerSystemModHandler.cs:96-100`)
  and `ServerSystemLoadAndSaveGame.OnBeginShutdown` saves (`:191-208`), and only then `ServerThread.shouldExit = true` (1801).
  A `Suspend` that times out un-suspends and returns false (`ServerMain.cs:1629-1650`); `Stop` ignores that and goes on.
- In single player the server's main thread is a new thread per world (`Vintagestory.Client\ClientProgram.cs:401`), and `Stop` runs on it.

---

## HIGH

### H1. Quitting while a chunk waits for a GLO-90 tile: Earth is disposed under the chunk thread, which goes on generating without Earth's patches, and the shutdown save keeps the result

Where: `src\EarthWorldGen.cs:441-455` (Dispose), `:325-327` (BareRock), `:150`, `:313`; `src\PatchVanilla.cs:260-265`, `:892-896`;
`src\EarthElevationSystem.cs:404-415`, `:436`, `:719-723`; `src\Core\DemDownloader.cs:99-123`, `:679-683`; `src\Core\FineElevation.cs:398-402`.

The path:

1. The chunk thread is inside one chunk's terrain pass, in `PrepareChunk` -> `DemDownloader.AwaitForGeneration`, which waits up to
   2 minutes for a square (10 in all). Offline, each new square holds it at least 20 s (three tries with 2 s and 8 s pauses from the
   release, then the same from Copernicus); on a slow link a 1-3 MB tile takes that long too.
2. The player quits (or `/stop`). `Stop` waits 10 s for every server thread to pause. A thread's `paused` is only set between
   `Update` calls (`ServerThread.cs:47-58`), so the chunk thread, still in its wait, never pauses. `Suspend` gives up, sets
   `suspended = false` again and returns; `Stop` goes on.
3. Shutdown phase: all mods are disposed. `EarthElevationSystem.Dispose` disposes the downloader; `EarthWorldGen.Dispose` puts
   **vanilla's GenTerra handler back** in the live handler list (`patches.Release()`), **unpatches** Earth's Harmony patches and
   nulls `Instance`; EarthGeology, EarthOres, EarthPlants, EarthPlacement unpatch theirs. `Active` stays true.
4. The chunk thread comes back. Probe `DisposeWaitProbe` (a local server that accepts and never answers): 2.0 s after the dispose
   the wait ends with "no tile", `TileAt` marks the square fallen back for the session, and the chunk's terrain is made from the
   **built-in heights**. The rest of that chunk's terrain pass then runs vanilla's rock strata and block layers **unpatched**
   (EarthGeology.AfterStrata/AfterSoil are gone); `BareRock` still finds the handoff and accepts the chunk.
5. The same tick goes on: its loop runs up to `ChunkColumnsToGeneratePerThreadTick` = 30 column passes (`MagicNum.cs:103`,
   `ServerSystemSupplyChunks.cs:135-146`) and breaks only on `server.Suspended`, which is false again. The next chunk at the terrain
   pass gets **vanilla terrain** from the restored GenTerra. `BareRock` notes it and calls `Refuse`, which sets `Active = false`.
   Every terrain pass after that is vanilla terrain that `BareRock` no longer looks at (`if (!Active || surface == null) return;`),
   and `ThrowIfRefused` is not reached because the wrapper that calls it was taken out of the list. Chunks at later passes get
   vanilla deposits, structures, trees and patches without Earth's guards.
6. Meanwhile the main thread runs the shutdown save. `SaveAllDirtyGeneratingChunks` (`ServerSystemLoadAndSaveGame.cs:504-552`)
   saves every generating chunk past the terrain pass, waiting on each chunk's lock for a pass in progress. The engine advances a
   chunk past a pass whatever its handlers did (`ServerSystemSupplyChunks.cs:1345-1381`). So every pass the chunk thread finishes
   before that step ends is in the save. After exploring, the save of dirty loaded chunks takes seconds; the chunk thread needs
   about 2 s to come back.

What the player sees after loading the world again: at the edge of the explored land, a chunk whose heights are the built-in
ones (a step to its neighbours) with vanilla rock and soil, and possibly chunks of vanilla terrain, or Earth terrain with vanilla
ores and trees. Only the first vanilla-terrain chunk is on the list of chunks to make again; the others stay for good.

How likely: it needs the default single generation thread, a tile wait of more than 10 s still running at the quit, and a
shutdown save that lasts until the chunk thread is back. A player who is offline or on a slow link and walks into a square
without its tile sees chunks stop loading for 20 s to minutes; quitting then is the natural reaction. On a fast link it does
not happen (the wait ends within the 10 s and the thread pauses normally). With `MaxWorldgenThreads` > 1 it does not happen
this way: the chunk thread pauses, saves and empties the generating queue (`FullyClearGeneratingQueue`) before the mods are
disposed, so a late pass on a worldgen thread is never saved.

Fix (no rules version: it changes nothing a healthy session generates):

- Make the generation wait end when the server is stopping, so the engine's own rule (all threads paused before mods are disposed)
  holds again: `AwaitForGeneration` polls `sapi.Server.IsShuttingDown` (`ServerAPI.cs:41`, true from the moment the exit is asked
  for) a few times a second and throws; `MakeTerrain` treats that throw as "noted, no refusal": `NoteFailedChunk` and rethrow,
  without `Refuse`'s error. The chunk is then saved empty at the pause and made again at the next start, as CR-G2 already does.
  The same poll in the unbounded spawn-phase wait fixes L2.
- Harden `Dispose` for the case the engine still gets there first: set `refused = true` and `Active = false` before unpatching,
  do not call `patches.Release()` (the handler list dies with the server; the wrapper is what throws and notes a chunk asked for
  late), and let `BareRock` throw instead of returning when `refused`.

---

## MEDIUM

### M1. A region's forest and shrub maps are made on built-in heights or on GLO-90 depending on what the thread did before, and saved

Where: `src\EarthWorldGen.cs:383-388` -> `src\EarthMapLayers.cs:45-74` -> `src\Core\TerrainModel.Ecology.cs:45-46`, `:60`, `:90`
(`PotentialElevation` reads `GetColumn(x, z).Elev` in worlds with local alpine ecology, the default); `src\Core\FineElevation.cs:105-109`,
`:398`; `src\EarthElevationSystem.cs:407` (the only place that turns `FetchAllowed` on for a generation thread).

`FetchAllowed` is a per-thread flag that stays on once a thread has run one terrain pass. The engine makes a chunk's region and
its eight neighbours before that chunk's terrain pass (thread facts above). So the first region group a thread makes in a
session is sampled with `FetchAllowed` off: where the square's tile is not on the machine, `TileAt` gives no height, the column
takes the built-in one, and the count of missed reads is not looked at. Every region made after the thread's first terrain pass
waits for its tiles and reads GLO-90. The maps are stored in the region.

Cases, default configuration:

- A new world on a machine without the spawn's tiles (a player's first Earth world, unless a story site's pad happened to
  fetch the same square): the nine regions round the spawn, 1,536 x 1,536 blocks, get their forest and shrub density from the
  built-in heights; all others from GLO-90.
- Each later session: the first chunk the chunk thread loads or generates creates any neighbour regions that do not exist yet,
  again without fetching. This matters where those regions lie in a square not downloaded yet: often at 1:100 and 1:200 (a
  region is 50-100 km), seldom at 1:10.
- With `MaxWorldgenThreads` > 1 the chunk thread, which makes the regions, never runs a terrain pass: no region ever waits for a tile.

Probe `RegionForestProbe` (pinned Core, zones.json, the test suite's layout, a 512-block region sampled every 16 blocks, the
same model and positions on a thread that may not fetch against one that may; tiles copied from the machine's store on fetch):

| Region | pixels | potentials that differ | largest | forest map bytes that differ | by 32 or more of 255 |
| --- | --- | --- | --- | --- | --- |
| Mont Blanc | 1,089 | 1,076 | 0.893 | 647 | 238 |
| Tatra | 1,089 | 913 | 0.615 | 373 | 35 |
| Caucasus | 1,089 | 994 | 0.898 | 336 | 126 |
| Riga (flat) | 1,089 | 259 | 0.000 | 0 | 0 |

What the player sees: nothing in lowlands. In mountains the forest and shrub density of those regions follows the coarse relief
(wooded where the real valley is above the tree line by the map, open where it is below), with a seam along the region border
to regions made later. Two installs of the same world differ. The tree pass's own per-attempt check still keeps trees off real
alpine ground, so it is density, not trees on glaciers.

Fix: make the switch belong to the work, not to the thread's past. Smallest: in `OnMapRegionGen` set `FineElevation.FetchAllowed = true`
round `ForestAndShrubs` and put the old value back (as `StoryPlacer.GroundMean` does), and set it at the entry of Earth's
handlers of the later passes (see M2, R1). No rules version: regions made from then on come out as all other regions already
do; saved regions keep their maps. Cost to know: world creation then fetches every square under the spawn's nine regions before
the first chunk (one or two squares at 1:10, up to about a dozen at 1:200), and a failure there stops the server as a spawn
chunk's does. If that is not wanted, the alternative is region maps on heights that never need a download (the included GLO-90
or the grid), which changes what worlds generate and needs a rules version.

### M2. Ore rules v5's valley search leaves a body out of a chunk when the deposit pass runs on a thread that may not fetch

Where: `src\Core\OreDeposits.cs:712-765` (`TryEnsure`: a search that read a missed height puts the body back and returns false),
`:577` (`BodiesNear`: `if (!unsearched && !TryEnsure(b)) continue;`), `src\EarthOreDeposits.cs:322` (`Place`),
`src\EarthOres.cs:372-377` (`AfterDepositLayers`), reached from `EarthGeology.AfterDeposits`, a postfix on vanilla's
`GenDeposits.GenChunkColumn`, which is in the TerrainFeatures pass (`VSEssentials\Vintagestory.ServerMods\GenDeposits.cs:60`).

ER-06's fix assumes "the deposit pass, which waits for its tiles". It does only on a thread whose `FetchAllowed` is on, and
that comes from a terrain pass on the same thread (M1). The search reads columns up to 30 km from the body (`RadiusKm = 30`,
`:444`), so it crosses into neighbouring 1 degree squares for most bodies. Where one of those tiles is not on the machine and the
thread may not fetch, the movable blanket is skipped for that chunk; the chunk's deposit pass never runs again, so its share of
the body is missing for good.

- `MaxWorldgenThreads` >= 2: TerrainFeatures runs on the chunk thread (or a worldgen thread of its own), which never runs a
  terrain pass. Every chunk generated before the search area's tiles happen to arrive (by a terrain pass's 64-block margin,
  the prefetch round players, or the download of all squares) gets none of that body. Systematic on such a server without the
  full store.
- Default single thread: only chunks resumed at the TerrainFeatures pass in the first moments of a session, before the chunk
  thread's first terrain pass, and only for a body no earlier session planned (else its tiles are here). Rare.

What the admin sees: a placer or blanket deposit of the catalogue with chunk-shaped holes, or absent near a square's border, in
worlds on servers with several generation threads. Not shown offline (the catalogue set-up is game-side); read end to end.

Fix: the same as M1's: `FetchAllowed` on at the entry of the deposit pass (and Earth's other later-pass handlers). No rules
version. Until then the statement in `ColumnStore.cs:100-102` ("the game runs each pass on its own thread when it has several
worldgen threads, so there the later passes' At reads a stored chunk or builds the one column") describes a mode in which
reads beyond the chunk silently take built-in heights: springs' head plans (`EarthSprings.Plan`, kept per head for the session)
have the same exposure in that mode.

---

## LOW

### L1. `EarthVolcanoes.Init` publishes its tables before they are filled, while map threads may be reading them

Where: `src\EarthVolcanoes.cs:226-283` (Init), `:115-130` (MapSurface), `:290-305` (PlansNear).

At the first Init nothing reads yet (`model.Carved.Craters` is set last). On `/wgen regen` vanilla pauses the chunk thread and
calls every InitWorldGen again (`VSEssentials\Vintagestory.ServerMods\WgenCommands.cs:1575-1582`); `Craters` is already
`MapSurface` then, and World Map's far-terrain threads call it through `GetSurfaceColumn`. Init assigns `cells = new Dictionary`
and fills it afterwards, and replaces `bodies`, `bodyX`, `bodyZ`, `bodyReach` and `plans` one by one: a reader meets a plain
Dictionary being written (undefined: a wrong miss or an exception on the map's thread) and arrays of zeros. Development command
only, a moment long, maps only (generation is paused). Fix: build into locals and assign the fields at the end; no rules version.

### L2. The spawn area's tile wait cannot be cancelled

Where: `src\EarthElevationSystem.cs:111`, `:436` (`GenerationWait` is infinite before the game runs), `src\Core\DemDownloader.cs:101`
(`fetch.GetAwaiter().GetResult()`), `:187` ("Once started a fetch isn't cancelled").

ER-16 gave the first start's file downloads a cancellation token; the spawn chunks' tile fetches have none. They run on the
server's main thread, which in single player is also the thread that notices the exit request. A source that accepts and never
answers holds it for three tries of the transport's 3-minute header wait per origin, about 9 minutes for the release and as
long again for Copernicus, per square; leaving the loading world waits that long. A plain failure (offline) ends in seconds and
stops the server as designed. Fix: H1's poll of `IsShuttingDown` in `AwaitForGeneration`, also where the budget is infinite.

---

## REFACTOR

### R1. `FineElevation.FetchAllowed` is thread history; make it a scope

It is turned on in `PrepareChunk` and never off on a generation thread, off once on the main thread at RunGame
(`EarthElevationSystem.cs:80`). Everything that asks "may this thread wait for a tile" therefore depends on whether the thread
has run a terrain pass in this session (M1, M2), and on the chunk thread it also covers work that is not generation (chunk
loading creating neighbour regions). One helper, `using (FineElevation.Generating())`, set at every generation entry point
(the terrain wrapper, each of Earth's pass handlers and patch entry points that read columns, `OnMapRegionGen`) and restored
at exit, makes the rule checkable in one place and makes the `ColumnStore` fill rule ("only a thread that may fetch") mean
"only generation".

### R2. `EarthWorldGen.Dispose` leaves the generator half alive

After Dispose `Active` is still true and the instance's pass handlers (`BareRock`, `FillRockFloor`, `ClearShorePlants`,
`OnMapRegionGen`) still act on a model whose column store is detached and whose patches are gone (H1 is the case where it
matters). Dispose should first close the generator (`refused`/`Active`), then take things away. Statics that survive Dispose
until the next Earth server start and hold the closed world's objects: `EarthTrees.pickers` (the catalogue and vanilla's tree
variant arrays; cleared in `BeginBinding`, `EarthTrees.cs:494-504`, not at Dispose) and the diagnostic switch `ClimateCost.On`
(`ClimateCost.cs:16`, never reset). Memory and a stopwatch per climate call, no wrong data.

---

## Checked and found sound

Enumerated by grep: every mutable `static` field (about 200 declarations, 102 of them `[ThreadStatic]`), the static
read-only containers, `ThreadLocal`, `volatile`, `lock`, `Interlocked`, `ConcurrentDictionary`, `ConditionalWeakTable`,
`Lazy`, `Parallel.`, `Task.Run`, `GetAwaiter`, `??=`, `GetOrAdd`. Not every one was read to the same depth: the generation
path, the height sources, the downloaders and the mod systems' lifetimes fully; the weather model's internals and the ore
generator's per-thread plans only for how they are keyed.

- **Per-thread caches are keyed by a number, not the owner**, and the number is unique per instance in the process:
  `TerrainModel.CacheId`, `ColumnStore.id`, `FineElevation.ownerId`, `CoarseElevation.id`, `TileCache.tag`, `RiverNetwork.cacheOwner`,
  `WeatherModel.Id`, `WeatherData.cacheId`, `WeatherStorms.SlotCache.cacheId`, `OreSession.CacheId`, `EarthPlants.State.CacheId`,
  the epochs of EarthGeology and EarthFauna. A second world in the process cannot read the first one's entries. The few keyed
  by reference (`BotanicalRegions.SmallCache.Owner`, `GeologyReference.cacheOwner`, `EarthClimateHooks.lastShifted`,
  `EarthTrees.ctxOwner`, `StructurePlacementGuards.deferredStructure`) compare the reference before use and hold small objects.
- **Per-thread switches are restored in finally blocks or Harmony finalizers**: `BuiltInOnly` (StoryPlacer, Png, World Map's
  wrapper), `SuppliedEarthTemperature`, `ReadingAsBefore`, EarthTrees' and EarthPlants' scopes, the deferred structure guard,
  EarthFauna's runtime climate type. StoryPlacer hands its switch to its workers and puts theirs back.
- **ColumnStore** (`Core\ColumnStore.cs`): never read or filled on a sweep thread, filled only by a thread that may fetch, looked
  up by model through a ConditionalWeakTable (a value that points at its key does not keep it alive), detached at Dispose.
  `tests\ColumnStoreTests` run in the pinned tree: 81 checks pass, including its thread and GLO-90 thread rules.
- **Model-level lazy caches read nothing that differs by thread**: `peakBases`, `coarseBases`, `KnownSummits.SupportCache`
  (pure), `fineSummits` (kept only when every tile was read), `reachInMask`, `reachSpeed`, `pondProfiles` (ER-01's rules hold in
  the code), TileCache users (coasts, lake and river outlines with their `CourseDrawsAt`/`FlowCarriesAt` callbacks, flow, grids:
  none reads GLO-90 heights), `TerrainOverlays`' island patches (a duplicate at worst).
- **MissedReads users**: `EarthMapSystem.RiverAt`, `EarthWorldGen.DrawnRapidSources` (checked inside the Lazy's factory, on the
  thread that runs it), `EarthVolcanoes.MapPlan` and `/earth volcano`, `OreDeposits.TryEnsure`, the commands. The volcano
  plan that generation makes is forced first in the terrain pass (`Craters`), after `PrepareChunk`.
- **FineElevation.Load / TileArrived / notHere**: the arrival counter closes the race between "file not there" and a tile
  written meanwhile; duplicate decodes are harmless. **DemDownloader**: `inFlight` gives one task per square to generation,
  prefetch and the batch; continuations run asynchronously; unique part names; the manifest task is guarded.
- **GroundTile's process-wide budget** (`Core\GroundColumns.cs:212-249`): unpack under the tile's lock, registration and
  eviction under one static lock, weak references; no double count, no use of an evicted plane (callers keep their `Planes`).
- **RiverNetwork's per-thread cells and scratch arrays**: `Nearest` and `NearestCourse` call nothing outside the network
  while they hold them; `MaxNear`'s callback (`Trough`) and `ReachBlend`'s (`ReachSpeed`, which walks the reach's own pieces)
  start no query that uses the same scratch.
- **Client and server in one process**: `ClimateLapse` (claims per side, the table stays while either claims it),
  `EarthWeather` and `EarthThunder` (one Harmony instance with a user count, hooks routed by the patched object's side),
  `Climate.Sealevel` (set to the same value by both, restored by the client), `WindHandlerPatched`/`written` (per thread, per
  lookup). Server-only statics (`EarthWorldGen.Instance`, `WorldGenerationContext`, EarthGeology, EarthOres, EarthPlacement,
  EarthPlants, EarthFauna, EarthVolcanoes, EarthGrassWinter, EarthGroundMoisture, SnowMeltLight) are set at start and cleared
  in Dispose with an `== this` test; the client-only ones (`EarthGrassSeason`, `EarthCreativeDate`, `SeasonTintAltitude`) too.
- **The six process-lifetime patches** each test a switch that Dispose clears (`ClimateLapse.table`, `EarthGrassSeason.active`,
  `EarthGrassWinter.active`, `EarthGroundMoisture.active`, `SnowMeltLight.on`, `SeasonTintAltitude` through `ClimateLapse`).
  `EarthGrassSeason`'s static caches are emptied with a generation count at Dispose and when the params change.
- **Vanilla statics Earth writes** (`TerraGenConfig.seaLevel`, `Climate.Sealevel`): vanilla sets both again at every server
  start (`GenTerra.cs:117-119`).
- **Save data from other threads**: `SaveGame.ModData` is a ConcurrentDictionary (`VintagestoryLib\SaveGame.cs:49`);
  `NoteFailedChunk` adds under its own lock; `OnFellBack` goes through the main thread.
- **Blocking**: all sync-over-async waits run their tasks on the pool with `ConfigureAwait(false)` (no deadlock); the first
  start's file downloads take the stop token; no wait is made while holding one of Earth's locks except a ground tile's own
  unpack and a Lazy's own decode, neither of which takes a second cache's Lazy that could wait on the first.
- **Static error latches and counters** are reset per world (`hookErrors`, `failureLogged`, `siteErrors`, the pond, spring and
  structure counters, EarthTrees' trace), `SnowMeltLight.Paused` too.
- **World Map's MemberwiseClone of the model** shares `CacheId` with the live model; the per-thread caches under that key
  (coast of 1318, enclosed water, stored temperature) depend only on layers both copies share.
