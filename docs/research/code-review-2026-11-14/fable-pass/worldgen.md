# Earth world generation and vanilla patches: independent review

Pinned tree: `scratchpad\earth-fable` at fef6166 (paths below are relative to it). Engine references are to
`C:\VS mods\devtools\vs-ref\decompiled\1.22.7`: `Lib` = `VintagestoryLib\Vintagestory.Server`, `Ess` =
`VSEssentials\Vintagestory.ServerMods`, `Surv` = `VSSurvivalMod\Vintagestory.GameContent`.

Scope: everything in `src\` outside `src\Core` that writes blocks or patches vanilla. Nothing in the tree was changed.

Counts: critical 0, high 1, medium 4, low 4, performance/refactor 5.

What was run:
- `tests\PatchVanillaTests` against the installed 1.22.7: **387 checks passed, 46 features in 16 tables** (log:
  `scratchpad\fable\probes-worldgen\patchvanilla-tests.log`). Every table-bound patch target, argument, `__result`,
  `___field` and transpiler binds on this game version.
- Two throwaway probes in `scratchpad\fable\probes-worldgen\`: `corecost` (model calls) and `chunkcost` (the game's own
  `ChunkData`, from the installed `VintagestoryLib.dll`). Their numbers are in the performance section.

Not verified in game: no world was generated. Every failure below is argued from the code path and the engine source;
none was reproduced live.

---

## The engine facts the findings rest on

1. A handler that throws: `runGenerators` (Lib `ServerSystemSupplyChunks.cs:1383-1405`) logs to the **worldgen** log
   only. In the Terrain pass it `break`s, so the pass's remaining handlers are skipped (`:1399-1402`); in later passes
   it goes on with the next handler. Either way `PopulateChunk` then does `currentpass++` and marks the chunk dirty
   (`:1370-1375`).
2. The shutdown save keeps generating chunks whose pass is past Terrain (`ServerSystemLoadAndSaveGame.cs:504-564`,
   the skip is `:513`). Before RunGame nothing is saved: the save handler is subscribed in `OnBeginRunGame`
   (`:186-189`).
3. `DeleteChunkColumn` (Lib `WorldAPI.cs:448-452`) unloads **at the call** and queues the index. The queue is worked
   only once the game runs (`ServerSystemSupplyChunks.cs:79-83`), and `deleteChunkColumns` (`:148-240`) removes the DB
   rows and the map chunk but never touches `server.loadedChunks`.
4. Chunk peeking (`PeekChunkAreaLocking`, `:1159-1247`) runs every registered handler on throwaway chunks at real
   coordinates. Vanilla's only caller is the static translocator (Surv `BlockEntityStaticTranslocator.cs:302-327`):
   3 x 3 chunks through Terrain, the centre through TerrainFeatures, at a random place inside its range.
5. Default threading: `MaxWorldgenThreads = 1` (Lib `MagicNum.cs:117`), so `additionalWorldGenThreadsCount = 0`
   (`ChunkServerThread.cs:44-49`): one chunk thread runs every pass. Several threads only where the server's magic
   numbers ask for them.
6. `Server.ShutDown()` from a generation thread while the game runs queues `Stop` on the main thread and waits up to
   7.5 s (Lib `ServerMain.cs:1680-1703`); other chunk passes run until `stopped` is set.

---

## HIGH

### H1. Only the terrain writer and per-chunk throws are fail-closed; whole features fail open for a session

Severity high: permanent seams in a save, with plausible triggers (a game update, a changed mod list, a missing file).

`EarthWorldGen.CheckTerrain` refuses the world only for `Feature.Terrain` (`src\EarthWorldGen.cs:300-309`). Every other
block-changing rule of the world turns itself off with one log line and the world goes on generating. The chunks made
in that session are saved for good with vanilla's blocks where the world's saved params say Earth's belong. That is the
opposite of what `FailedPass` enforces for one chunk (`src\EarthWorldGen.cs:173-192`).

Verified fail-open paths (each ends in "off for this session" or "vanilla is used"):

| Where | What stays vanilla |
|---|---|
| `src\PatchVanilla.cs:440-588`, logged by `Log` `:827-832` | every non-critical feature of the world-gen table: arid basin fill, soil fertility, tall grass, tree species, coral salinity, the structure, pond, spring and spawn guards, prospecting |
| `src\PatchVanilla.cs:667-675` | `Placement1318` is `Critical`, which only changes the log level to error |
| `src\EarthGeology.cs:94-99, 113-155, 328-331` | regional geology (unsupported rules version, missing hook, competing mod, bad width, missing or invalid asset, a material block gone, any exception): vanilla strata |
| `src\EarthGeology.cs:166-167, 218-222` | ground columns, ore layers |
| `src\EarthOres.cs:75-80, 90-137`; per call `:335-339` | Earth's ore layer (ten separate conditions); the attempt factor per variant |
| `src\EarthMapLayers.cs:21-25` | the climate and ocean maps ("Earth climate not applied") |
| `src\EarthSprings.cs:86-105` | stream-head springs and seeps |
| `src\EarthWaterTable.cs:63-78` | the water table (seeps, tree sites) |
| `src\EarthPlants.cs:134, 165, 229`, `src\EarthPlacement.cs:72`, `src\EarthFauna.cs:144-195` | plant, placement and fauna rules |
| `src\EarthTrees.cs:163-173`, `src\EarthPlants.cs:373-379`, `src\EarthPlacement.cs:194-197` | per attempt: "vanilla's pick is used", said once |
| `src\EarthVolcanoes.cs:307-321` | a volcano whose plan threw is left out for the session (a cached null); the next session may plan it and carve the rest of it |
| `src\EarthWorldGen.cs:527-537` | story placement failed: vanilla's sites are kept **and `StoryPlaced` is saved**, so it is never tried again |

Fix: after the WorldReady set-ups, in `EarthWorldGen.InitWorldGen`, compare what the saved params require (regional
geology, ground columns, ore layers, regional ores, spring rules, placement rules, the PatchVanilla features that change
blocks) with what is active, and `Refuse` when one is off. One setting in `earth.json` for an owner who accepts the
seams. Story placement: do not set `StoryPlaced` after a failure.

Rules version: none needed. Nothing a healthy session generates changes.

---

## MEDIUM

### M1. Several of Earth's own pass handlers have no fail-closed catch, and a cut-short Terrain pass goes unnoticed

- No catch at all: `BareRock` (river ramps and every surface column), `KnownHotSprings`, `FillRockFloor`,
  `ClearShorePlants` (`src\EarthWorldGen.cs:325-381`); `EarthVolcanoes.Craters` and `Depths`
  (`src\EarthVolcanoes.cs:360-369, 444-453`).
- A catch that logs and keeps the chunk: `EarthSprings.OnVegetation` (`src\EarthSprings.cs:117-129`),
  `EarthWaterTable.OnPreDone` (`src\EarthWaterTable.cs:334-349`).

Failure: an exception there lands in the engine's catch (fact 1). The chunk advances and is saved with half a surface
pass, half a crater or an unfilled rock floor (mantle with caves in it), with a line in the worldgen log only. In the
Terrain pass the `break` also skips every later handler.

The same hole from the other side: anything that throws in the Terrain pass between Earth's terrain and `BareRock`
skips `BareRock`, so its `MadeByEarth` check never runs. That includes Earth's own code running inside vanilla's
block-layer handler without a catch: `SoilFertilityAt` and `TallGrassCoverPrefix`
(`src\BlockLayerAdjustments.cs:85-104, 171-181`). `EarthGeology.AfterSoil` is a postfix, so it is skipped too. That
chunk's handoff entries stay in `TerrainHandoff` for the session.

I found no concrete input that throws in these handlers (the index arithmetic I checked is under "found sound"). The
finding is the missing net, not a known throw.

Fix: wrap each handler as the geology passes are (`catch -> EarthWorldGen.FailedPass`). For the cut-short pass, let
the first Earth handler of TerrainFeatures refuse a chunk whose handoff flags were never taken.

Rules version: none.

### M2. A refusal raised while a chunk is peeked marks the real chunk at those coordinates for deletion

`NoteFailedChunk` (`src\EarthWorldGen.cs:203-219`) records `request.ChunkX/Z` whoever asked. During a peek (fact 4)
the chunks are throwaway copies, but the coordinates are real. A throw in any fail-closed pass there (terrain, strata,
ground, ore layers, deposits) notes that position and stops the server; the next start deletes whatever chunk column is
saved there (`DeleteFailedChunks`, `:225-243`), a generated and possibly built-on one.

Likelihood low: it needs a pass to throw during a translocator's search. Consequence: the one the comment at `:179`
says must not happen. `EarthGeology.BeforeDeposit` already makes this distinction for the prospecting pick
(`src\EarthGeology.cs:950-955`).

Fix: note a chunk only when the request's map chunk is the world's own,
`sapi.WorldManager.GetMapChunk(cx, cz) == request.Chunks[0].MapChunk`. A peeked map chunk is never put in
`loadedMapChunks` (Lib `ServerSystemSupplyChunks.cs:1185-1191`). Whether a peek failure should stop the server at all
is a separate choice.

Rules version: none.

### M3. A GLO-90 tile that can't be fetched while the game runs gives that square the built-in heights for the session

`src\EarthElevationSystem.cs:384-398`. By design (the comment cites review A3), and the log says the chunks "may meet
later ones with a step". Listed because it is the most likely real trigger of chunks made without the world's rules (a
network drop), and because of what follows from it:

- The next session fetches the tile. Every later pass and query of the chunks saved at an intermediate pass then reads
  GLO-90 columns under terrain written from the built-in heights (`ColumnStore.At` in ground columns, tree sites,
  guards, the water table).
- A volcano planned in the failed session is planned from built-in heights and cached (`src\EarthVolcanoes.cs:300`).
  The next session plans it from GLO-90, so one cone can get two vents. `PlansNear` has no
  `FineElevation.MissedReads` check; `MapPlan` and `CmdVolcano` have one (`:138-142, 165-167`).
- With several worldgen threads (fact 5) only the Terrain thread may fetch: `FineElevation.FetchAllowed` is set in
  `PrepareChunk` alone (`:407`). A chunk resumed after a restart on a machine without the tile cache gets its later
  passes, and a volcano plan made in `Depths`, from built-in heights.

Fix: treat a failed fetch while running as a refusal of that chunk (note it and stop), or keep the square's fallback
for good, as `OnFellBack` does for a tile the sources lack. In `PlansNear`, do not keep a plan whose planning missed a
read.

Rules version: none (only sessions with a failed fetch change).

### M4. Chunks in flight when a refusal happens finish their current pass with Earth withdrawn, un-noted

`Refuse` sets `Active = false` and withdraws the context (`src\EarthWorldGen.cs:277-279`), then the server stops
asynchronously (fact 6). Until then:

- a chunk in the Terrain pass is noted and thrown out (`ThrowIfRefused`, `:150-155`): sound;
- a chunk in a later pass runs it half-Earth. `FillRockFloor` and `ClearShorePlants` return (`:350, 361`) and every
  guard that reads `WorldGenerationContext.Current` lets vanilla through, while `EarthGeology`, `EarthVolcanoes` and
  `EarthSprings` keep their own flags and go on. The shutdown save keeps it (fact 2) and the next session resumes it
  at the following pass.

Window: a few chunk passes at most on the default single chunk thread.

Fix: one check at the start of every Earth handler of the later passes (`if (refused) { NoteFailedChunk; throw; }`).

Rules version: none.

---

## LOW

### L1. `DeleteFailedChunks` can't remove a noted chunk that the start-up loads

It runs in `InitWorldGen` (`src\EarthWorldGen.cs:484`), before the spawn square is loaded (Lib
`ServerSystemSupplyChunks.cs:1040-1113`; 7 x 7 chunks, `MagicNum.cs:105`). By fact 3 the unload happens before the
chunk is in memory, and the queued delete leaves it loaded. The log says "deleted and generate again"; the chunk is
played as it is and saved again later without its map chunk.

Reachable only when the noted chunk lies in the spawn square (a failed `/wgen regen` there, or M2). A start-up failure
itself saves nothing (fact 2), so there is no restart loop.

Fix: call `DeleteFailedChunks` from the RunGame phase handler that already exists (`CheckTerrainBeforeRunning`,
`:287-290`). The unload then finds the loaded chunks, and the queue is worked on the chunk thread's first tick.

### L2. Raw block writes over blocks that have block entities

- `ClearShorePlants` (`src\EarthWorldGen.cs:374-379`) sets air over any `BlockMaterial.Plant` block on a shore-strip
  column. Wild fruiting bushes are Plant blocks with entity class `WildFruitingBush`
  (`assets\survival\blocktypes\plant\fruitingbush.json`). The block goes, its block entity stays in the chunk. On sand
  a bush can't stand; the strip also has rock and mud classes (`src\Core\ShoreMaterial.cs:23`), and I did not check
  what ground those get, so whether a bush can reach the strip at all is open.
- `EarthVolcanoes.Depths` runs in the Vegetation pass, after structures. `SetSolid`, `SetLava` and `Fill`
  (`src\EarthVolcanoes.cs:324-340, 579-604`) overwrite whatever is there, a ruin's chest included.

`EarthSprings` already excludes such blocks (`src\EarthSprings.cs:81-82`). Vanilla's pattern is
`serverChunk.RemoveBlockEntity(pos)` (Ess `GenRivulets.cs:174-177`). The fix changes no generated block.

### L3. A rebuilt ore-layer table reads the surface after the craters

The ground pass records each column's top before `EarthVolcanoes.Craters` lowers `WorldGenTerrainHeightMap`
(`src\EarthVolcanoes.cs:423, 433`). A table rebuilt later (`TableFor`, `src\EarthOreLayers.cs:401-411`: after a
restart between the passes, or past 768 waiting tables) reads the lowered top (`src\EarthGeology.cs:671-676, 688`).
In crater columns the layer pass then keeps or puts back deposit blocks by other depths than in a same-session chunk.

Crater columns only. Fix: build the rebuilt table from the model's column top, or record the ground pass's tops in the
map chunk. No rules version if the rebuilt table is made to equal the kept one.

### L4. `RockFloorPass.Top` reads the world's map chunk, not the request's

`src\RockFloorPass.cs:37-51` asks `WorldManager.GetMapChunk`. Called from generation
(`src\EarthOreDeposits.cs:320`) during a peek, it returns the real chunk's floor or none. Peeked chunks are thrown
away, so nothing is saved wrong; a peek only differs from generation there.

---

## PERFORMANCE AND REFACTOR

Measured on this PC after warm-up (`probes-worldgen\chunkcost` on the game's `ChunkData`; `probes-worldgen\corecost`
on a model without rivers):

| Call | Cost |
|---|---|
| `Data[i] = id` (the indexer, which takes the layer's lock) | 12 ns |
| `SetBlockUnsafe`, one id in the palette / twelve ids | 7 ns / 25-33 ns |
| `SetBlockBulk`, a 32 x 32 layer | 0.1-0.2 ns a block |
| `SetFluid` | 12-14 ns |
| `GetBlockIdUnsafe` | 10-15 ns |
| `TerrainGeoAt` + `SampleClimate` | 33 ns |
| `FrozenAllYear` at the build limit / near the ground | 41 ns / 350-960 ns |
| `GetColumn` (a lower bound: no rivers in the probe's model) | 270-360 ns |

Estimates per land chunk: those numbers times the blocks each loop visits. Nothing here was timed in game. The
surface heights stand for a 256-high world (top about 120) and a 1024-high one (top about 450, my assumption).

| Pass | Work | top = 120 | top = 450 |
|---|---|---|---|
| `EarthGeology.ApplyStrata` (`src\EarthGeology.cs:527-568`) | read and rewrite every rock block of every mapped column | 5 ms | 18 ms |
| `EarthGround.Write` (`src\EarthGround.cs:144-166`) | read, and rewrite where different, down to the hand-off | 1 ms, up to as much again as the strata | same |
| `EarthOreLayers.Apply` (`src\EarthOreLayers.cs:456-534`) | read every block below the top | 1.5 ms | 5.5 ms |
| `TerrainChunkWriter` (`src\TerrainChunkWriter.cs:118-135`) | relief above the lowest column, and water, block by block | 1 ms (100 blocks of relief) | 4 ms (ocean 290 deep) |
| `ColumnStore.Build` | 1024 model columns | 0.3-1 ms | same |
| `SpringGuard` (`src\SpringsAndPonds.cs:125-156`) | once per vanilla try, up to about 4,500 / 18,000 a chunk | 0.3 ms | 1.3 ms |

### P1. The strata, ground and layer passes rewrite the column block by block after vanilla's strata did the same

Worth fixing for tall worlds: about 8 ms a chunk at 256 high, 25-40 ms at 1024.

`ApplyStrata` runs right after vanilla's strata, before caves and ores, so below the chunk's lowest strata top every
block is plain rock. Where all 1024 columns have regional bedrock and the same bed material at a height, and the chunk
section's palette holds no preserved rock (`ContainsBlock`, as `src\OreBauxiteStrata.cs:25-28` does), one
`SetBlockBulk` per layer replaces 1024 reads and writes. Same blocks out, so no rules version.

### P2. `ColumnStore.Build` allocates a 115 kB array per chunk

`src\Core\ColumnStore.cs:181-200`, called from every terrain pass. 1024 x 112 bytes is over the large-object
threshold, so each generated chunk adds 115 kB that only a gen-2 collection frees once the store drops it. Reusing
dropped arrays needs care: `lastColumns` (`:61-62`) keeps a per-thread reference to an array without checking that it
is still the store's.

### P3. The terrain writer uses the locked indexer and `SetFluid` per block

`src\TerrainChunkWriter.cs:120, 134`. `SetBlockUnsafe` costs about half the indexer; the chunk is the thread's own
during the pass. Water has no bulk call in `IChunkBlocks`, so ocean columns stay per block, as in vanilla's GenTerra.

### P4. Small per-chunk allocations (none worth a change on its own)

- `structures.ToArray()` of the whole region's structure list for every chunk (`src\RockFloorPass.cs:91`). The list is
  replaced, never changed in place (Lib `ServerMapRegion.cs:277-284`), so it can be read as it is.
- `PrepareChunk` and `ChunkReady`: a `HashSet`, a `List` and a tuple array per chunk
  (`src\EarthElevationSystem.cs:408, 440-453`).
- `EarthWaterTable.Rate`: two closures per probe and four arrays per chunk (`src\EarthWaterTable.cs:352-382`), only
  with Hydrate or Diedrate.
- `PlansNear` is an iterator: one object per guard call that reaches it (`src\EarthVolcanoes.cs:290-305`).
- `SpringGuard` does its climate lookup before vanilla's own cheap rejections. 99 % of vanilla's tries are at the build
  limit (the product of two draws, Ess `GenRivulets.cs:121`); `if (y > mapChunk.YMax + 1) return false;` first would
  skip them with the same outcome.

### R1. One wrapper for Earth's pass handlers, one "generation is on" flag

`EarthWorldGen.Active`, `EarthGeology.Active`, `EarthOres.session`, `EarthVolcanoes.active`, `EarthSprings.active` and
`WorldGenerationContext.Current` are set and cleared separately. M1 and M4 both come from that. One helper that
registers a handler with the refusal check and the `FailedPass` catch, and one flag every pass reads, would close both.

---

## Checked and found sound

Harmony patches, beyond the binding test:

- Vanilla methods read against their patches: `GenBlockLayers` (the arid fill constant, `LoadBlockLayers`' arguments,
  `PlaceTallGrass`' x and z), `GenRivulets` (the guard replays vanilla's draws in vanilla's order, Ess `:120-122`),
  `GenDeposits.GenDeposit` (arguments and both injected fields), `WorldGenStructure` (argument 2 is the start position
  in all five; `WouldOverlapAt`'s true means "turn away"), `GenPonds.TryPlacePondAt` (its recursion re-enters the
  guard with the same position hash), `GenCreatures`, the runtime spawner, `BlockCoral`.
- A transpiler that finds nothing turns its feature off and is taken back (`PatchVanilla.TryBind`, `:865-872`); the
  coral one throws, which is caught at `:874-882`.
- The prospecting pick's own generators (Surv `ProPickWorkSpace.cs:488-507, 558-600`). It calls `preLoad`,
  `genBlockColumn`, `initAssets`, `initWorldGen` and `GeneratePartial`, never `GenChunkColumn` or `reloadWorldGen`, so
  `AfterStrata`, `AfterSoil`, `AfterDeposits` and both reload postfixes don't run for it. `BeforeDeposit` does, and
  tells the instances apart before refusing (`src\EarthGeology.cs:953`). `FactorPostfix` ignores generators that are
  not this session's. Its sea-level generators are rescaled through the recorded-levels table.
- A non-Earth world in the same process: every prefix returns to vanilla on a null context or session,
  `AridFillFactor` gives 40, and the handler replacements are put back in `Dispose` (`src\EarthWorldGen.cs:450-451`).
- `/wgen regen`: the handoff is kept, the ore session re-binds or goes, the layers unbind, the volcano and spring
  set-ups rebuild.

Block writing:

- Index arithmetic is `((y % 32) * 32 + z) * 32 + x` with `y / 32` everywhere I read it. Tops are clamped to
  `mapHeight - 2` in the writer, and every `y + 1` write is guarded.
- The height maps after each pass that moves the surface: the writer (both maps and YMax), ramps (rain map and YMax),
  craters (both), springs (both). Snow and beaches replace blocks in place.
- Lake ice and water go to the fluid layer, as in vanilla's GenTerra (Ess `GenTerra.cs:476, 503`).
- `ClearBlocks` clears blocks, fluids and light, which `RockFloorPass` relies on (`src\RockFloorPass.cs:133`). The
  rock floor and the volcano depths run before the light pass (0.7 and 0.71 against 0.95).
- A scheduled lava light under a filled rock floor places no light: the engine re-reads the block's light at that
  place (Lib `BlockAccessorWorldGen.cs:59-82`).
- `SetBlockUnsafe(i, 0)` is only called after a non-zero read of the same chunk.
- Earth's passes write only into the requesting chunk column. Neighbours are read through the model, never through
  chunk data, so no pass assumes a neighbour's pass.
- Missing blocks: ramp, snow, sand, gravel and tephra ids of 0 are tested before use. A missing geology material turns
  geology off (H1); it never writes id 0.

Refusal and state between passes:

- No deadlock when `Refuse` runs inside a pass (`FastRWLock` gives way while shutting down). The failed list is a
  `ConcurrentDictionary` entry behind Earth's own lock. A start-up failure saves nothing, so nothing loops.
- The handoff is written and taken in the same pass. The rock floor's top is in the map chunk. Ore-layer tables are
  rebuilt from the model when missing (but see L3) and capped at 768.

## Not reviewed

Read in part only: `EarthTrees` (the pick up to line 330), `EarthPlants` (from 236), `EarthFauna` (from 272),
`EarthOreDeposits` (`Place` only), `StoryPlacer` and `EarthPlacement` (catch sites only).

Not read: `OreSession`, `OreBinder`, `OreProspecting`, `OreProviderBlocks`, `OreTotals`, `EarthOreContent`,
`WorldGenRules`, `EarthSpawn`, `EarthStoneTextures`, `EarthCrafting`, `EarthCommands`, `WorldUpgrades`, `SelfTest`,
`EarthMapSystem`, `EarthWorldOptions`, `CylinderCompat`, `AstraTerra*`.

Runtime and client patches (`EarthGrassSeason`, `EarthGrassWinter`, `EarthGroundMoisture`, `EarthThunder`,
`EarthCreativeDate`, `SeasonTintAltitude`): covered by the binding test only. `EarthWeather` binds its patches outside
the tables and was not read.
