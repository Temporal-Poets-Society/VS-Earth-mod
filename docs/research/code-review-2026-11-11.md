# Earth code review, 11 November 2026 — findings for hand-over

This is a hand-over document: a session that has not seen the review should be able to pick any finding below, open the
cited lines and act on it. Everything here is a **finding or a proposal**. Nothing has been agreed with the author, and
nothing in the code was changed by the review. **12 November:** the findings were re-checked against rc.7 and most were
fixed in a separate session; [section 11](#11-fixes-12-november-2026) gives the status of each.

## 1. What was reviewed, and how far to trust it

| | |
|---|---|
| Repository | `C:\VS mods\Earth`, branch `release/1.0` (the trunk), worktree `C:\VS mods\Earth\.claude\worktrees\release-1.0` |
| Commit | `a42df52` (Earth 2.0.0-rc.5). The trunk has since moved to `93ab23e` (rc.6); `git diff a42df52 93ab23e -- src` is empty, so every `src` line number below is valid at `93ab23e` |
| Not reviewed | The `main` checkout in `C:\VS mods\Earth` (stale since 25 October), `tests/`, `tools/`, the Python bakes, the JSON assets beyond what a finding needed |
| Size | 195 C# files, 55,529 lines in `src/` |
| Engine reference | `C:\VS-ref\decompiled\1.22.7\` (decompiled engine and vanilla DLLs) |
| Method | Eight area reviewers read every file in `src/` in full (exceptions in section 9). The lead reviewer then re-read the cited lines of the main findings, and the engine code they depend on |
| Not done | No test project was run, the game was not started, nothing was profiled. A Debug build of `EarthMap.csproj` was made into a scratch folder: 0 warnings, 0 errors |

Each finding carries one of three statuses:

- **VERIFIED** — the lead reviewer opened the cited Earth lines (and the engine lines where the finding depends on them)
  and the mechanism is as described. The *effect in game* was still not observed unless the text says so.
- **REPORTED** — an area reviewer marked it confirmed by reading; the lead reviewer did not re-read it. Re-read before acting.
- **PLAUSIBLE** — the reviewer could not close the argument; the text says what is unverified.

Severity is the reviewer's judgement of consequence, not of likelihood. All performance costs are argued from the code
(operation counts), **not measured**.

Finding ids are `CR-<area><n>`. They are new to this document and do not continue the earlier audits' ids (A04, B03,
D16, E24 and so on).

Paths are relative to the repository root. Engine paths are relative to `C:\VS-ref\decompiled\1.22.7\`.

### Constraints a fix must respect

- **Old worlds keep generating as they did.** A fix that changes what a new chunk looks like in an existing world needs
  a new rules version frozen in `EarthParams` (the project's standing rule). The findings that need one are marked
  **needs rules version**.
- Per `C:\VS mods\AGENTS.md`: read how vanilla does a thing in `C:\VS-ref` before patching beside it; after changing
  scope, an interface or verification status, update the relevant overview card, roadmap and decision.
- Line endings are mixed (38 of 195 `src` files are CRLF, the rest LF; no `.gitattributes`). Git Bash `sed -i` turns a
  CRLF file LF; `src/Core/TerrainModel.cs` and `src/EarthTrees.cs` are CRLF.
- Merge Earth work into `release/1.0`, not `main`.

## 2. Summary

**Overall:** good quality, unusually careful for a mod this size. No finding crashes or corrupts a world in normal
play. The weaknesses cluster in three places:

1. **Failure paths that fail open.** Earth's stated rule is that an Earth world never generates without Earth's terrain
   and rules (`EarthWorldGen.Refuse`, `RequiredMods.Enforce`). Three paths get past it: CR-L1, CR-G2, CR-O1.
2. **Results that depend on which thread asks.** `TerrainModel.GetColumn` is documented as a pure function of position,
   but two per-thread flags (`FineElevation.FetchAllowed`, `FineElevation.BuiltInOnly`) change its answer: CR-D1 / CR-G1,
   CR-T1, CR-O2.
3. **Very large classes and parallel implementations** (section 7).

Suggested order of work, as a proposal:

| Order | Findings | Why first | Needs rules version |
|---|---|---|---|
| 1 | CR-L1, CR-G2, CR-O1 | A wrong chunk saved into an Earth world is permanent | no |
| 2 | CR-L2 | One-line fix; dedicated servers cannot run `/earth` from the console | no |
| 3 | CR-D1 (with CR-G1), CR-D2 | Main-thread stalls and thread-dependent heights | no |
| 4 | CR-G5 | One-line fix, visible artefact | yes (changes new chunks) |
| 5 | CR-R1, CR-E1, CR-T2 | Visible content errors, each changes generation | yes |
| 6 | Statics that survive a world: CR-E4, CR-O4, CR-G4, CR-L4 | Cheap, one pattern | no |
| 7 | Weather cost: CR-W1 to CR-W3, after measuring | Runs every frame and tick | no |

## 3. Bugs verified by the lead reviewer

### CR-L1 — A failed write of `earth.json` at server start turns Earth off in an Earth world

- **Status:** VERIFIED (Earth code and engine). **Severity:** high consequence, needs an unusual condition.
- **Where:** `src/EarthMapSystem.cs:222` (`Config = EarthConfigStore.Load(api, Mod.Logger);`),
  `src/EarthConfigStore.cs:107` (`api.StoreModConfig(cfg, FileName);`, last statement of `Load`, no try/catch).
- **Engine:** `VintagestoryLib/Vintagestory.Common/APIBase.cs:104-109` — `StoreModConfig` is a bare
  `File.WriteAllText`. `VintagestoryLib/Vintagestory.Common/ModLoader.cs:496-539` — `TryRunModPhase` catches every
  exception except `FormatException`, logs "An exception was thrown when trying to start the mod" and returns false;
  the server keeps starting.
- **Mechanism:** `Load` is called at line 222 of `StartServerSide`. The world set-up is registered after it, at
  `api.Event.ServerRunPhase(EnumServerRunPhase.WorldReady, InitWorld);` (line 235). If the write throws,
  `InitWorld` is never registered, so `Params` stays null.
  `src/EarthWorldGen.cs:293-299` (`InitWorldGen`) then returns at `if (p?.Enabled != true || core.Model == null) return;`
  with `Active = false`. `GenTerraPrefix` (`src/EarthWorldGen.cs:121-127`) and `InPlaceOfGenTerra` (`:176-181`) both
  fall through to vanilla's terrain when `!gen.Active`. `Refuse` and `CheckTerrain` only run for an active world.
- **Scenario:** `ModConfig/earth.json` is read-only, locked by an editor or backup tool, or the disk is full; an
  existing Earth world is opened. New chunks get vanilla terrain, saved for good, beside Earth's chunks.
- **Proposed fix (three layers, any one closes the hole):**
  1. catch around the `StoreModConfig` call (a config that cannot be written back is not a reason to stop);
  2. wrap `EarthMapSystem.StartServerSide` so any exception on a save that holds enabled `earth-params` stops the
     server, as `Refuse` does;
  3. in `EarthWorldGen.InitWorldGen`, refuse when the save holds enabled params but `core.Params` is null.
- **Note:** `src/RequiredMods.cs:121` already records that the mod loader swallows `StartServerSide` exceptions, so the
  hazard is known in the code base; this path was not covered.

### CR-G2 — A chunk whose terrain pass throws is still advanced and saved

- **Status:** VERIFIED (engine catch and Earth's lack of one). Scenario B is REPORTED. **Severity:** medium.
- **Where:** `src/EarthWorldGen.cs:121-127` and `:176-181` call `gen.terrain.Generate(request)` with no catch;
  `src/TerrainChunkWriter.cs` has no try/catch (grep: none).
- **Engine:** `VintagestoryLib/Vintagestory.Server/ServerSystemSupplyChunks.cs:1383-1405` (`runGenerators`) catches the exception, logs
  it to the worldgen log and, for a pass at or before `Terrain`, `break`s out of the handler list. The caller still does
  `chunkRequest.MapChunk.currentpass++` and `DirtyForSaving = true` (`:1374-1375`).
- **Scenario A:** `TerrainChunkWriter.Generate` throws (the code already expects `GetColumn` can throw:
  `src/Core/ColumnStore.cs:192` has a catch around the column build). The remaining terrain-pass handlers are skipped,
  including the surface pass that checks `MadeByEarth` (`src/EarthWorldGen.cs:192`), so a mantle-only chunk is saved.
- **Scenario B (REPORTED):** `Refuse()` called from the surface pass throws after `sapi.Server.ShutDown()`. The
  vanilla-terrain chunk is saved at the next pass and gets Earth's later passes on the next start, which is what the
  `Refuse` message says cannot happen.
- **Side effect (REPORTED):** a throw between the writer and the surface pass leaves that chunk's handoff entry
  (about 5 kB) in `TerrainHandoff`.
- **Proposed fix:** catch in `InPlaceOfGenTerra` / `GenTerraPrefix` and refuse; record refused or failed chunk
  coordinates and delete those columns at the next start; longer term, a persisted "terrain made by Earth" mark per
  chunk so the state can be detected on load.

### CR-O1 — A rules-v4 world whose ore layers fail to come up still binds, and saves floor-only ore

- **Status:** VERIFIED. **Severity:** medium.
- **Where:** `src/EarthOres.cs:163-169` (`AttachCatalogue`: with `s.Layers == null` it logs a warning and returns);
  `src/OreSession.cs:106` (`Layers = Geology?.Layers;`) and `:110` (`RulesV4 = Rules >= OreBackground.CatalogueRules;`);
  `src/OreSession.cs:209` (v4 background is the class floor); `src/OreV3Bodies.cs:43` (`if (session.RulesV4) return;`).
- **Contradiction:** `src/EarthOres.cs:119-127` rejects the same end state for the missing deposit hook ("Earth's ore
  layer is off instead (vanilla's deposits)") and returns without binding.
- **Scenario:** `EarthGround.TryCreate` or `CreateLayers` fails (`src/EarthGeology.cs:161-165`, `:206-209`), or geology
  is inactive, in a world with `OreRules` 4. No catalogue bodies, no v3 bodies, background at the floor: every chunk
  generated that session has floor-only ore, permanently.
- **Also REPORTED:** `PruneDeposits(layered=false)` then removes Earth's layer deposits, which shifts vanilla's random
  stream against earlier sessions of the same world.
- **Proposed fix:** treat `RulesV4 && Layers == null` like the missing hook (do not bind), or stop the server as the
  ground sets do.

### CR-L2 — `/earth` cannot be used from the server console

- **Status:** VERIFIED. **Severity:** medium (dedicated servers).
- **Where:** `src/EarthCommands.cs:41-45` — `.RequiresPrivilege(Privilege.chat).RequiresPlayer()` on the root command.
- **Engine:** `VintagestoryLib/Vintagestory.Common/ChatCommandImpl.cs:286-303` — `Execute` runs the command's own preconditions and
  returns on the first error before dispatching to a subcommand. The console caller has no player
  (reviewer's citation: `ServerMain.cs:1931-1936`).
- **Scenario:** with nobody online, `/earth elevation download all`, `/earth elevation cancel`, `/earth map`,
  `/earth checkrivers`, `/earth story`, `/earth date` all answer "Caller must be player".
- **Dead code that follows from it (REPORTED):** the `?? "the server console"` branches at `src/EarthCommands.cs:127`,
  `138`, `144`.
- **Proposed fix:** drop `RequiresPlayer` from the root; keep or add it on the positional subcommands. REPORTED: the
  `trees`, `geology`, `ores`, `fauna` and `plants` subcommands lack it and dereference `args.Caller.Player.Entity`, so
  they need it once the root no longer guards them.

### CR-D1 / CR-G1 — The permission to wait for a GLO-90 tile is a per-thread flag that is never cleared

- **Status:** VERIFIED (mechanism); consequences REPORTED by two reviewers independently. **Severity:** medium.
- **Where:** `src/EarthElevationSystem.cs:355` — `FineElevation.FetchAllowed = true;` in `PrepareChunk`, the only
  writer in `src/` (grep). `src/Core/FineElevation.cs:105-107` — `[ThreadStatic] static bool fetchAllowed;`.
  `src/Core/FineElevation.cs:388-391` — a missing tile on a thread with the flag calls `Fetch(latS, lonW)`, which
  blocks (`src/Core/DemDownloader.cs:74`, `FetchBlocking`).
- **Stated intent it contradicts:** `src/Core/ColumnStore.cs:24` — "only chunk generation sets it".
- **Engine (reviewers' citations):** `MagicNum.MaxWorldgenThreads` defaults to 1. Start-up spawn chunks are generated
  on the main thread: `ServerSystemSupplyChunks.loadChunkAreaBlocking` (`:1249-1317`), reached from `ServerMain.cs:1286`.
- **Consequences:**
  1. After a session that generated spawn chunks, the main thread holds the flag for the rest of the process, including
     the next single-player world. `/earth tp <lat> <lon>` (`src/EarthCommands.cs:370`, `Model.GetColumn` on the main
     thread) to a square not on disk then blocks the main thread on a download; see CR-D2 for how long that can be.
  2. After a restart of an existing world the main thread does *not* hold it. Plans built on the main thread then read
     built-in heights where a tile is missing: `/earth volcano` builds and caches a `VolcanoPlan`
     (`src/EarthVolcanoes.cs:100`) that later chunks carve a crater from; `StoryPlacer.GroundMean`
     (`src/StoryPlacer.cs:192`) decides a pad height the same way. So the result depends on engine scheduling history.
- **Proposed fix:** set the flag in try/finally around Earth's world-gen handlers and restore the previous value; have
  the plan caches reject a build during which `FineElevation.MissedReads` changed, as the river query already does.

### CR-R1 / CR-T3 — Springs are planned half a block away from the generated terrain

- **Status:** VERIFIED. **Severity:** medium. **Needs rules version** (it changes where springs appear).
- **Where:** `src/Core/StreamSprings.cs:121` — `model.GetColumn(bx + .5, bz + .5)` in `PlanHead`;
  `src/Core/StreamSprings.cs:280`, `:288`, `:295` — the same `+ .5` in `PlanSeep`.
  Generation samples whole blocks: `src/Core/ColumnStore.cs:190` (`model.GetColumn(x0 + lx, z0 + lz)`),
  `src/ChunkSettler.cs:49`.
  `src/Core/TerrainModel.cs:249` — `GetColumn(double x, double z)` is continuous; it does not floor.
  `src/EarthSprings.cs:237` — `Place` rejects when `mc.WorldGenTerrainHeightMap[col] != top`.
- **Effect:** wherever the top differs half a block diagonally (slopes, banks; seeps sit on slopes by definition), the
  spring is dropped and counted as "ground" (`headsGround`, `seepsGround`, logged at `src/EarthSprings.cs:274`, `:283`).
  The neighbour tests (`Dir`, lower neighbour, near water) are also read half a block off. The share dropped was not
  measured: those two counters in a server log give it directly.
- **Second cost (REPORTED):** `+ .5` coordinates miss the `ColumnStore` (`OnGrid` false), so each seep try costs up to
  30 full column evaluations.
- **Same pattern (REPORTED):** `src/Core/RiverAudit.cs:77` samples at `+ 0.5`.
- **Root cause (REPORTED, CR-T3):** `GetColumn(double, double)` has no stated block convention.
  `src/Core/TerrainModel.LandHeights.cs:79` (`FineMean`) treats (x, z) as the block centre.
- **Proposed fix:** plan from `ColumnStore.Near(model, x, z)` at whole blocks; add a `ColumnAtBlock(int, int)` and use
  it everywhere a block is meant.

### CR-E1 — The 1318 climate envelope for crops and fruit trees uses ambient rain, not the river green belt's

- **Status:** VERIFIED in code; the rain figures are the reviewer's, derived from the rain curve, not sampled in game.
  **Severity:** medium. **Needs rules version.**
- **Where:** `src/EarthPlants.cs:316` — `c.Rain[i] = (float)cs.Rain;` (ambient), passed at `:320` to
  `gate.ClimateAllows(...)`. `src/EarthPlants.cs:333` — `Arable()` counts a river belt as arable via
  `RiparianWeight(x, z, c.Rain[i]) >= 0.5`. `src/Core/PlacementCatalogue.cs:244` —
  `rain >= c.MinRain && rain <= c.MaxRain`. `src/EarthPlacement.cs:161` — `rain = cs.Rain` for fruit trees.
- **Compare:** trees, fauna and soil use the raised rain: `src/EarthTrees.cs:282`, `src/Core/RegionalFaunaClimate.cs:13`,
  `src/EarthGeology.cs:689`, `src/Core/TerrainModel.Climate.cs:143` (`RiparianRain`).
- **Effect:** a crop passes "arable" along the Nile and is then vetoed on rain. Reviewer's figures: Cairo's ambient rain
  about 2, Baghdad's about 18, while onion, fennel, flax, garlic and rice need 28 to 71. Same for peach and pomegranate.
  The reviewer notes `ranges-2026-11-07/README` says "the river belt handles" the Nile.
- **Proposed fix:** pass `Model.RiparianRain(x, z, rain)` (cached per cell, as the arable flag is) to the envelope in
  both files.

### CR-T2 — A two-block ledge along the 0 m contour on dry land below sea level

- **Status:** VERIFIED (formula); the look in game was not checked. **Severity:** medium. **Needs rules version.**
- **Where:** `src/Core/VerticalScale.cs:63-67`:
  `if (h >= 0) return SeaLevel + ...; return SeaLevel - 1 - d * Math.Log(1 - h / SeaD0);`
  used as `top = (int)Math.Floor(Vertical.Y(h))` at `src/Core/TerrainModel.NaturalColumn.cs:134` and `:182`.
- **Arithmetic:** h = 0 gives Y = SeaLevel, top = SeaLevel. h = −0.01 gives Y just under SeaLevel − 1, top = SeaLevel − 2.
- **Effect:** correct for sea floor, but the same branch serves dry land below 0 m outside any water ring: Caspian
  lowland, Jordan valley, Death Valley, polders beyond the coast's reach. A two-block step follows the 0 m contour, and
  ground is pitted where the detail noise straddles 0.
- **Related (REPORTED):** `WaterTopY` puts land at a negative lake level flush with the water, but one block above it
  for positive levels.
- **Proposed fix:** a continuous dry-land branch below 0 m, behind a new rules version.

### CR-G5 — Dry river beds get gravel in diagonal stripes

- **Status:** VERIFIED. **Severity:** low. **Needs rules version** (or accept the change in new chunks).
- **Where:** `src/AlpineSurfacePass.cs:136` — `MakeBeach(chunks,lx,lz,h,s.TopRock[i2],((s.Cx+lx)^(s.Cz+lz))%4==0);`
- **Why:** `(x ^ z) % 4 == 0` is true exactly when the low two bits of x and z are equal, that is x ≡ z (mod 4): every
  fourth diagonal, not a scatter.
- **Proposed fix:** `TerrainColumns.Hash(x, z) % 4 == 0` (`internal static uint Hash(int x, int z)`,
  `src/Core/TerrainColumns.cs:7`).

### CR-T1 — `/earth map` renders through the GLO-90 tile cache on pool threads

- **Status:** VERIFIED (no `BuiltInOnly` in `src/Core/Png.cs` or `src/EarthCommands.cs`, grep; raw `Parallel.For` at
  `src/Core/Png.cs:165`). Cost REPORTED, not measured. **Severity:** medium.
- **Where:** `src/Core/Png.cs:160-175` (`MapColors.Render` calls `model.GetColumn` per pixel inside
  `System.Threading.Tasks.Parallel.For`), called from `src/EarthCommands.cs:480-523`.
- **Effect (REPORTED):** in a GLO-90 world with the store on disk every pixel reads GLO-90 through `FineElevation`'s
  24-tile cache behind its `gate` lock (`src/Core/FineElevation.cs:52`, `:404-449`); each pixel row re-decodes every 1°
  square it crosses and evicts the generation thread's tiles. `src/Core/FineElevation.cs:76-89` says such a sweep must
  use built-in heights. Every pool thread also keeps its `TileCache` slots afterwards.
- **Proposed fix:** run it through `ParallelWork.For` with `BuiltInOnly` set and restored per worker.

## 4. Bugs reported by area reviewers (not re-read by the lead reviewer)

### Lifecycle, config, commands

- **CR-L3 — low, REPORTED.** `src/EarthMapSystem.cs:608-616`: `BuildAndAttachModel`'s data-problem path calls
  `sapi.Server.ShutDown()` and returns false instead of throwing as `Refuse` does. `ShutDown` is synchronous and
  disposes the mod systems, so the remaining `WorldReady` handlers (fauna, placement, plants, geology, water table,
  ores) run on disposed systems with `Params.Enabled` true and `Model` null. Safe today only because each tests
  `core.Model == null`; `EarthOres.InitWorld` still runs `PruneDeposits` and `SaveParams` (`src/EarthOres.cs:60-68`).
  Fix: throw after `ShutDown`.
- **CR-L4 — low, REPORTED.** `src/EarthMapSystem.cs:1042`: the client sets `Climate.Sealevel = p.SeaLevel` and never
  restores it (not in `Dispose`, not on a disabled-params packet). Vanilla resets it only on the server
  (`GenTerra.AssetsFinalize`). A multiplayer client that leaves an Earth server with a lowered sea level and joins a
  non-Earth server in the same process keeps Earth's level in `Climate.GetRainFall`. Fix: remember and restore.
- **CR-L5 — low, PLAUSIBLE.** `src/EarthCommands.cs:324`: `/earth tp` accepts non-finite coordinates. Confirmed by the
  reviewer: `DoubleArgParser` takes "NaN", and `Math.Clamp(NaN, …)` returns NaN, so NaN reaches `Model.GetColumn` and
  `entity.TeleportToDouble`. Unverified: what the engine does with a NaN position (the project's notes record NaN
  crashes elsewhere). Fix: reject unless `double.IsFinite`.
- **CR-L6 — low, REPORTED, perf.** `src/WhereReport.cs:51-53`: every `/earth where` (chat privilege) decodes the 945 KB
  `treeregions.json` and FNV-hashes it in `BotanicalRegions.Shared` on the main thread; about 2 MB of large-object
  allocation per call, spammable by any player. Fix: use the instance already loaded, or cache per session.

### World generation

- **CR-G3 — low, PLAUSIBLE.** `src/StructurePlacementGuards.cs:116-140`, `179-186`: the deferred underground guard
  tests a box at the position noted in the prefix, but vanilla's cave-attached path moves the structure first
  (`WorldGenStructure.cs:802`, `targetPos.Add(-blockPos.X - blockFacing.Normali.X * num2, …)` before `WouldOverlapAt`).
  With an uncentred origin the real footprint can lie up to `Size` blocks west or north of the checked box, so a
  cave-attached ruin can still break into a river or lake bed. Not reproduced. Fix: use `WouldOverlapAt`'s own `pos`
  and `schematic` arguments in `DeferredUndergroundGuard`.
- **CR-G4 — low, REPORTED.** `src/SpringsAndPonds.cs:96`, `182`; `src/StructurePlacementGuards.cs:112`, `156`: guard
  counters are static and never reset, so the "this session" lines in `EarthWorldGen.Dispose`
  (`src/EarthWorldGen.cs:270-271`) and the self-test include earlier worlds of the same process.
- **CR-G6 — low, REPORTED, design cost.** `src/ChunkSettler.cs:132-137`, `src/TerrainChunkWriter.cs:131`,
  `src/AlpineSurfacePass.cs:89`, `95`: `LakesFreeze`, `ColdCoast` and `Scoured` are decided once at the chunk centre.
  Lake ice (and with it all river ramps), sand versus gravel beaches and scoured turf change along 32-block chunk edges
  at each threshold. The per-chunk choice is documented; the staircase is not. Fix: decide per column from
  `c.Lat` / `c.Lon`, or dither with a position hash. Needs rules version.

### Terrain model

- **CR-T9 — low, PLAUSIBLE.** `src/Core/GroundRelief.cs:10-11`, `27-28` with `src/EarthGeology.cs:715-716`: the lattice
  step and disc membership depend on `metresX`, which the caller takes from each chunk's own latitude. Where
  `round(rx/3)` or a rim point flips, north-south neighbouring chunks use different lattices, against the "seamless"
  claim. Visible effect not verified.
- **CR-T10 — low, PLAUSIBLE.** `src/Core/TerrainModel.NaturalColumn.cs:255-275`: `StripGridLakes` compares against the
  single highest kept level. Where a higher fresh lake's ring overlaps a kept salt or terminal lake's ring, the kept
  lake's ring is dropped too (`ShoreLevel` becomes NaN), so land beside it is no longer held at its level. Not checked
  against the data.

### Data, downloads, elevation

- **CR-D2 — medium, REPORTED.** `src/Core/DemDownloader.cs:64`, `173`, `181-222`; `src/EarthElevationSystem.cs:360`: an
  on-demand tile fetch has no total deadline. Per square: 3 release tries plus 3 Copernicus tries, each bounded only by
  the 3-minute read timeout (`new DataTransport(TimeSpan.FromMinutes(3))`, line 64), pauses of 2 s and 8 s
  (`Pause`, line 173), plus a possible 3-minute manifest fetch: about 18 minutes worst case when packets are dropped or
  a server accepts and stalls. The blocked thread is the chunk thread, which also loads saved columns, so all chunk
  streaming stops. Fix: `SocketsHttpHandler.ConnectTimeout` of about 15 s and a per-square budget of about 60 s for
  generation-driven fetches.
- **CR-D3 — low, REPORTED.** `src/EarthElevationSystem.cs:396-417`: `Prefetch` relaunches `FetchAsync` every 5 s for up
  to 9 squares per player with no failure memory. Offline it logs about 7 lines per square per 20 s cycle indefinitely
  and holds the shared `slots` that generation fetches queue behind. Fix: per-square cooldown after a failure.
- **CR-D5 — low, REPORTED.** `src/Core/DataTransport.cs:119`, `src/EarthElevationSystem.cs:440`:
  `new DriveInfo(Path.GetPathRoot(...))` is "/" on Linux, so free space is the root filesystem's, not the data mount's.
  In Docker with a small overlay root and a large data volume, `src/EarthMapSystem.cs:305` then tells the admin to free
  disk space after a network failure. Fix: longest-prefix entry of `DriveInfo.GetDrives()`.
- **CR-D6 — low, REPORTED, information exposure.** `src/EarthElevationSystem.cs:470-481`: any player's "status" request
  gets `Source`, `Repo` and `FreeBytes`; only start and cancel check `controlserver`. The strings come from the admin's
  `earth.json` and can be local server paths or a private mirror URL. Fix: blank the three fields unless `canControl`.
- **CR-D8 — low, REPORTED.** `src/Core/DemDownloader.cs:295-299`: the cached `manifest-egl1.json` is used forever, is
  not tied to `Repo`, and is the only release artefact without a compiled-in SHA-256. After the admin switches
  `ElevationRepo` to a mirror packed differently, every tile fails its hash 3 times before Copernicus, with no refetch.
  Fix: record the repo URL and hash beside the cache; refetch after a hash mismatch.
- **CR-D9 — low, PLAUSIBLE.** `src/Core/DemTiles.cs:318-321`, `src/Core/FineElevation.cs:424`: `Cols * Rows` is
  allocated before the checksum is checked and never compared with the expected dimensions. A damaged header with valid
  magic throws `OverflowException` or `IndexOutOfRangeException`, which the `.bad` filter (IOException,
  InvalidDataException, UnauthorizedAccessException) does not catch, so it escapes on a worldgen thread (and then meets
  CR-G2). Needs bit rot or an odd mirror tile. Fix: validate dimensions in `Decode`.
- **CR-D10 — low, REPORTED, memory.** `src/Core/FineElevation.cs:119-121`: the thread-statics `lastOwner` and `lastTile`
  hold strong references, so a pool thread that once read heights keeps a closed single-player world's `FineElevation`
  alive (about 70 MB of tile cache plus the dam data). `src/Core/CoarseElevation.cs:411-414` avoids this with an id.
- **CR-D11 — low, REPORTED, text.** `modinfo.json:6` says "about 270 MB, once per machine". The reviewer's sums:
  `EarthDataStore.Files` total 286.8 MB, and the default world (`earthRocks` = groundcolumns) also fetches the 172.3 MB
  30″ ground sets, so about 459 MB before any tiles.

### Rivers and water

- **CR-R3 — medium, PLAUSIBLE.** `src/Core/RiverPoolPlanner.cs:455`: side arms (`src/Core/WaterOverlays.cs:476`) and
  delta branches (`src/Core/RiverDeltas.cs:148`) are source reaches, `P = min(waterTop(lev[0]), bound)`, with no link to
  the main stem's pool at the split, which may lag or lead by `StepLead` 3 or more. The split vertex is not pinned in
  `RiverStraighten` either (only chain ends are). Would give a step or backward ramp at an arm's head. Unverified:
  whether the bake or settling hides it. Fix: start an arm at the main reach's pool level and pin that vertex.
- **CR-R4 — low, REPORTED.** `src/Core/TerrainModel.Rivers.cs:379`, `398-400` against `src/Core/RiverNetwork.cs:1030`,
  `1038`: `dryReachCourses` keeps a reach by its own Q, wet flow or fade, but a hit pairs `Q = b.Q` (best piece) with
  `Reach = reach` (after walking across a joint). Piece Q < 10 in a hot desert, walked into a neighbour with Q ≥ 10:
  `ReachInMaskRiver` returns false in game (reaches released) but computes offline. Result: a dry strip at the joint,
  and the game and the tests disagree. Fix: decide the byte for every kept reach at start-up, drop the lazy path.
- **CR-R5 — low, REPORTED.** The legacy seam test exists in four forms: `src/Core/RiverCoursePreparer.cs:154`
  (`len > 4000` → length 1), `src/Core/RiverSegmentBuilder.cs:116` (`|dx| <= 4000 && |dz| <= 4000`),
  `src/Core/RiverStraighten.cs:155`, `src/Core/RiverSegmentBuilder.cs:166`. A piece with length in (4000, 5657] and both
  deltas ≤ 4000 is emitted with `Len = 1` while spanning thousands of blocks, so its pools are compressed. Only worlds
  without river-end rules on rings from about 1M blocks. Fix: one predicate.
- **CR-R7 — low, PLAUSIBLE.** `src/Core/RiverPoolPlanner.cs:310-324`: when `MaxRounds = 4` runs out with `lowered` still
  true, tributaries stay above the main river at confluences and nothing is counted or logged. Not verified to occur on
  real data. Fix: log the unconverged basins.
- **CR-R8 — low, PLAUSIBLE.** `src/Core/RiverStraighten.cs:435` writes a cut-out reach as two identical points, a
  zero-length piece. If it wins the nearest-piece score, `src/Core/RiverNetwork.cs:1017` yields `DirX = DirZ = 0`: river
  water with no flow under channel rules 0. Fix: skip `Len == 0` pieces as best.
- **CR-R9 — low, PLAUSIBLE, documented legacy.** `src/Core/RiverNetwork.cs:973-994`: the along-river blend takes pieces
  within `bestD + hw + 6` but only those in the column's own 128-block cell. For half-widths above about 13 at 1:10, two
  water columns either side of a cell border can blend different sets and the step line can jog. Line 279 documents the
  semantics; the cost is not stated.
- **CR-R12 — low, REPORTED, API.** `src/Core/RiverQuery.cs:58-65`, `src/EarthMapSystem.cs:133`: "not ready" is an
  exception with a formatted message per call. Only chunk failures back off (`RetryMs`); the missing-tile path throws
  on every water update. The public signature exposes `TerrainModel.Column`. Eviction is clear-all at 256 chunks.
  Fix: add a `TryRiverAt` returning a status, keep the throwing wrapper. This is Hydrology's contract (decisions 0024,
  0027), so a change is an interface change.

### Geology and ores

- **CR-O2 — medium, PLAUSIBLE, determinism.** `src/OreSession.cs:152`, `157`, `163`;
  `src/EarthOreGenerator.cs:333-335`: attempt factors and deposit plans read model heights farther out than the terrain
  pass guarantees. `Context.Top` is read for source chunks up to about 112 blocks outside the generating chunk,
  `CentreTop` up to about 80. `PrepareChunk` guarantees GLO-90 tiles only within 64 blocks
  (`src/EarthElevationSystem.cs:366-369`). With `MaxWorldgenThreads > 1` the deposit thread has no `FetchAllowed`, so
  near a 1° tile edge `Top` differs before and after the tile arrives; the deep-evidence threshold in `ChunkHeat`
  (`src/Core/RegionalOreCatalog.cs:627-632`) flips, the attempt count differs between target chunks, and deposits are
  cut at chunk borders. Unverified: that `GetColumn` really falls back on that thread, and how often. Related to CR-D1.
  Fix: set `FetchAllowed` in the deposit hooks (scoped), or make the chunk heat independent of `Top`.
- **CR-O3 — low, REPORTED.** `src/EarthOreGenerator.cs:197`: after `/wgen regen` the prospecting pick reads nothing for
  Earth-bound ores until restart. `ProPickWorkSpace` keeps the old `DepositVariants` (engine
  `ProPickWorkSpace.cs:508-541`); their old generators hit `owner.Session != Session` and return 0/0. Fix: forward to
  the current session's binding of the same code.
- **CR-O4 — low, REPORTED.** Error latches are static and never reset: `src/EarthOreGenerator.cs:167`, `193`, `359`,
  `372`, `445`; `src/EarthOres.cs:320`, `332`, `359`; `src/OreProspecting.cs:79`; `src/EarthOreLayers.cs:215`. One
  `HostShift` exception makes `Volatile.Read(ref searchErrors) > 0` disable the host search process-wide; it flips
  mid-world (plans completed before and after differ for one deposit across chunks) and carries into the next
  single-player world. Fix: keep the counters in `OreSession`, show them in `GenerationTotals`.
- **CR-O5 — low, REPORTED, latent.** Ore layers, ground columns and the catalogue have no per-world version pin:
  `src/EarthGeology.cs:157-158`, `195-196`; `src/Core/OreLayers.cs:18`; `src/Core/OreDeposits.cs:20`;
  `src/EarthOres.cs:229-239`. Geology picks `RulesAsset(version)`; here there is one constant asset, a warning that
  "this build has v{1}, which new chunks use", and an informational hash, while `ore-layers-v1.json` and decision 0060
  say worlds keep their version. Latent while only v1 exists; any shipped data correction changes new chunks of
  existing worlds. Fix: select assets by the saved version.
- **CR-O8 — low, REPORTED.** `src/OreV3Bodies.cs:101-107` against `:148`: placement interpolates district strength
  between chunk corners, prospecting uses the exact value at the column. At district edges the pick reports seams that
  were not placed, or misses ones that were. Fix: one shared strength function.
- **CR-O9 — low, REPORTED, latent.** `src/Core/OreDeposits.cs:520`, `661-666`, `856-862`;
  `src/EarthOreDeposits.cs:267-309`: the deposit catalogue ignores the ring's seam. Bodies are neither wrapped nor
  clipped, and `Place` has no gutter test. No current entry lies near −169.25 (the reviewer checked with a script).
  Fix: reject or split at load.
- **CR-O10 — low, PLAUSIBLE.** `src/EarthStoneTextures.cs:37`: `Ours()` is a path prefix test with no separator, so an
  unpacked sibling folder such as "Earth Ruins" beside "Earth" counts as Earth's. Matters only if that mod ships
  same-named stone textures. Fix: append a separator or compare origins.

### Trees, plants, animals, grass

- **CR-E2 — low to medium, REPORTED (not seen in game).** `src/EarthGrassSeason.cs:126`, `317`: `Anomaly` evaluates the
  wetness curve at the continuous `yearRel` (updated every 2 s), but meshes are redrawn only when the half-month step
  changes (`:128-133`). Chunks meshed at different moments within a step get different rain bytes; at a monsoon onset
  neighbouring chunks can show colour seams. Fix: evaluate at the step centre, `(step + 0.5) / Steps`.
- **CR-E3 — low to medium, REPORTED.** `src/EarthPlants.cs:150-154`: a missing or invalid `regionalplants.json`
  (`src/Core/RegionalPlantCatalog.cs:98-110`, `120` throws on any bad rule, even of an uninstalled provider) aborts
  `Initialize` before the hooks are applied. That also drops the placement catalogue's crop, berry and flower gates,
  while `/earth placement` still says "active" and the warning only mentions plant packs. Fix: catch the pack catalogue
  separately and continue with placement alone.
- **CR-E4 — low, REPORTED.** `src/EarthTrees.cs:58`, `src/EarthPlants.cs:34`, `src/EarthPlacement.cs:38`: the "log once"
  flags (`failureLogged`, `hookErrors`) are process statics, never reset. In single-player a failure in one world
  silences the only error line in every later world; `EarthTrees` then throws and catches per tree attempt unseen.
  Fix: reset in `BeginBinding` / `StartServerSide`.
- **CR-E5 — low, REPORTED, latent** (the reviewer's scripts show today's data does not trigger these):
  - `src/Core/RegionalTreeCatalog.cs:623-624`: `wetByRegion` / `shoreByRegion` are built from native entries only, so a
    riparian or shore species used as an analogue (habitat check at `:774`) can never be picked.
  - `src/Core/RegionalPlantCatalog.cs:214-222` with `src/Core/PlacementCatalogue.cs:232-234`: a multi-taxon patch
    passes on the union of ranges and any taxon's climate, so taxon A's range with taxon B's climate grows both.
  - `src/EarthFauna.cs:286`: a cached marine site keeps the region from whichever lookup (land fill 5° or sea 0.08°) an
    earlier candidate asked for, so the result depends on order.
  - `src/Core/RegionalTreeCatalog.cs:1050-1057`: the pick keeps vanilla's 0.001 slack but not vanilla's shuffle, so the
    first registration gains 0.001/total.

### Weather, climate hooks, thunder, bindings

- **CR-W5 — medium, REPORTED.** `src/EarthWeather.cs:183`, `486-489`, `709-723`: wind-as-vector is not kept whole. If
  a `VanillaWindVector` transpiler is refused after a game update, Earth warns but still writes the vector; with an
  easterly gale (X = −1) vanilla's `WaterWaveIntensity = 0.75f + X * 0.9f` goes negative and `WindWaveCounter` runs
  backwards. Likewise a failed rain patch leaves `EarthThunder` and ground moisture on the model while the game shows
  vanilla rain. So `src/PatchVanilla.cs:35`'s reason for keeping weather outside the table ("no feature to keep whole")
  does not hold. Fix: bind vector + readers and rain + thunder as `PatchVanilla` features; write strength-in-X when the
  readers are not patched.
- **CR-W6 — low, REPORTED.** `src/EarthWeather.cs:400-413`: `RainCloudDaysOffset` is ignored (documented), but
  vanilla's `/weather stoprain` (`WeatherSystemCommands.RainStopFunc:622`) still answers that rain should stop and
  nothing changes. Fix: apply the offset to the model's time, or intercept the command.
- **CR-W7 — low, REPORTED.** `src/Core/WeatherModel.cs:186-190`: the weather's year ignores `Calendar.SeasonOverride`,
  which `src/EarthClimateHooks.cs:99` honours. In a fixed-season world temperature stays summer while rain chance,
  thunder and cyclone seasons cycle by month. Not checked: whether Earth's play style exposes that option.
- **CR-W8 — low, REPORTED.** `src/EarthThunder.cs:227`: `LightningRate = 0` is written into the region's
  `WeatherEventState`, which vanilla serialises on every save (`WeatherSystemServer:227`). After a reload, `Taken.Note`
  records 0 as vanilla's value, so `GiveBack` (admin `/weather setprecip`, weather off) restores 0 and there is no
  vanilla lightning until the event expires. Fix: restore before `GameWorldSave`, or keep the rate out of the state.
- **CR-W9 — low, REPORTED.** `src/Core/WeatherContract.cs:55-57`: `writesVanilla` is constant true, also when an older
  Sea keeps the wind and before a client has a model. The comment says thunder stays vanilla's, stale since
  `EarthThunder`.
- **CR-W10 — low, PLAUSIBLE.** `src/CylinderCompat.cs:59-75`: the header says missing members are reported, but the
  getters throw `MissingMemberException`, caught only in `CylinderKeepsEarthSeam` (`src/EarthMapSystem.cs:885`);
  `CylinderWorldMissing` and `ComputeParams` (`:870-909`) let it escape `InitWorld`. Unverified: how the engine handles
  that throw. Fix: one `TryRead` that returns a named problem for `Refuse`.
- **CR-W11 — low, PLAUSIBLE.** `src/AstraTerraLongitude.cs:27-38`: reads AstraTerra's private field "config" by name,
  swallows failures to false, sampled once at server start (`src/EarthMapSystem.cs:623`). A rename silently puts showers
  on the calendar clock with no log line. AstraTerra's source was not read.
- **CR-W12 — low, REPORTED.** `AccessTools.Method` is evaluated outside the try at `src/EarthWeather.cs:346` and
  `689-690`, so an ambiguous match after a game update escapes `Watch`. `/ew` `Describe` (`:650`) picks snow or rain
  from the sea-level monthly normal, wrong on mountains.

## 5. Performance (all estimates; measure before changing)

| Id | Status | Where | Cost as argued | Proposed fix |
|---|---|---|---|---|
| CR-W1 | REPORTED path | `src/EarthWeather.cs:89`, `99`, `480-508` | Earth's `OnGetWindSpeed` runs after vanilla's handler and overwrites its work: one `GetWindSpeedAt` costs 5 `WindAt` + 1 `Cloudness`, 1 used. The client calls it twice per frame plus particles; `TickEvery25ms` asks per loaded region 40×/s | skip vanilla's handler while `ControlsWind`; cache each region-centre wind about 1 s |
| CR-W2 | PLAUSIBLE | `src/Core/WeatherModel.cs:438-484` via `src/EarthWeather.cs:413` | `Cloudness` is not memoised: about 100 lattice hashes, `Pow(Sin)` per alive storm before the distance reject (`src/Core/WeatherStorms.cs:323`). Vanilla's snow catch-up asks 8 corners × up to 480 hours per region per tick | per-thread memo of place-only terms; latitude reject before `Storm.At` |
| CR-W3 / CR-T5 | REPORTED | `src/EarthClimateHooks.cs:39`, `109-110`; `src/Core/TerrainModel.Climate.cs:86-88` | two full `SampleClimate` calls per live climate query when three temperatures are needed; the older path has a per-thread cache (`:107-121`), this one has none | temperature-only sample, cached per thread per column |
| CR-D4 | REPORTED | `src/Core/EarthBinary.cs:22-24`, `src/Core/EarthDataStore.cs:98-100` | store files read through an unsized `MemoryStream` + `ToArray()`; reviewer estimates about 1 GB of transient large-object allocation per server start over the 287 MB store | for seekable streams `new byte[Length - Position]` + `ReadExactly` |
| CR-O6 | PLAUSIBLE | `src/EarthOreGenerator.cs:300-327`, `461-464`; `src/EarthOres.cs:321` | every attempt from 49 source chunks draws a full plan before the bounds reject; the 2048-slot plan cache is smaller than one chunk's working set; rules v4 multiply attempts by `AttemptScale` (about 16× at 1:400) | reject by centre distance before `GetPlan`; cache the factor per binding |
| CR-O7 | REPORTED | `src/EarthOreLayers.cs:250-266`, `378-392`; `src/Core/OreLayers.cs:184-196` | the layer pass reads every block from the surface to y = 1 in every column; `CodeOf` redoes string-set matching per segment; up to 768 waiting tables of about 82 KB (about 63 MB) | memoise `CodeOf`; skip sections with no deposit ids |
| CR-R2 | PLAUSIBLE | `src/Core/RiverNetwork.cs:898-952` (`NearestCourse`) | no cache; each mask-river column beyond a course's reach rescans whole buckets (about 2,560 blocks square at 1:10) | project straight from `Seg`; build candidates only for the winner |
| CR-R6 | REPORTED | `src/Core/RiverStraighten.cs:61`, `src/Core/RiverPoolPlanner.cs:312`, `src/Core/RiverSpatialIndexBuilder.cs:23`, `src/Core/AridHydrology.cs:27` | raw uncapped `Parallel.ForEach`; `src/Core/ParallelWork.cs` documents why that pattern was replaced | `ParallelWork.For` |
| CR-R10 | REPORTED | `src/Core/TerrainModel.Rivers.cs:44`; `src/Core/RiverNetwork.cs:865-870`; `src/Core/TerrainModel.Climate.cs:157`; `src/Core/TerrainModel.NaturalColumn.cs:239` | per-column recomputation of widths (Pow, Log) the hit already carries; `Nearest` run again for the riparian weight | carry the values in the hit |
| CR-G7 | REPORTED | `src/EarthWaterTable.cs:266-272`, `src/Core/WaterTable.cs:228-251` | each `WaterTableAt` / `AquiferAt` rebuilds the drainage lattice: at least 64 `NodeAt` columns and six arrays, nothing reused | small per-thread cache of lattice nodes |
| CR-G8 | REPORTED | `src/RockFloorPass.cs:91`; `src/EarthVolcanoes.cs:200-210`; `src/EarthSprings.cs:159` | `structures.ToArray()` per chunk column; a `yield` iterator allocated per `InCrater` call; `plans.Count` takes every `ConcurrentDictionary` lock per stream head | reuse, non-allocating loop, counter |
| CR-E6 | REPORTED | `src/Core/GrassSeason.cs:62-67` via `src/Core/SoilFertility.cs:47-68` | about 7 `Math.Log` and 2 `Math.Exp` per grass block per chunk mesh, on the tesselation thread (reviewer: about 0.3 ms per grassland chunk) | precompute the log tables |
| CR-E7 | REPORTED | `src/Core/TerrainModel.Ecology.cs:60-113`; `src/EarthMapLayers.cs:56`, `72` | `ForestPotential` and `ShrubPotential` each sample the climate twice and each run `DryMonthsAt`, `Fire.Summer`, `GetColumn`, `RiparianWeight`; both are called per pixel | one site struct per pixel |
| CR-E8 | REPORTED | `src/EarthCorals.cs:44-45`; `src/Core/RegionalFaunaCatalog.cs:278`; `src/EarthGrassWinter.cs:164` | four `BlockPos` per coral target; a string switch per candidate; `pos.UpCopy()` on every soil random tick before the dice | reuse, precomputed enums |
| CR-T6 | REPORTED | `src/Core/PeakCatalog.cs:47-66`; `src/Core/KnownSummits.cs:43-45`; `src/Core/TerrainModel.LandHeights.cs:176` | `Near` runs twice per land column, 9 to 51 dictionary probes each; beyond about 85.7° it allocates a `List` per call even where rows are empty | test `rowColumns` first; one query per column |
| CR-T7 | REPORTED | `src/Core/TileCache.cs:59`, `115`, `158`; `src/EarthParams.cs:498-503` | per-thread slots are `id & 15`; a full-featured world allocates 17 ids (six of them discarded caches), the live ones span exactly 16: one more grid makes two hot caches share a slot. `Used` is stamped only in `Shared`, so tiles served from thread slots look oldest and are trimmed first | one `Select` for all choices, 32 slots, stamp on `Miss` |
| CR-T8 | REPORTED | `src/Core/EarthDataset.cs:460-467` | `TerrainTile` keeps `Elev` and `Ice` as floats decoded from 16-bit codes: 11 B/cell, about 114 MB at the 320-tile bound; keeping codes gives 7 B/cell | keep the codes, as `FineTemperature` does |

What held up: the per-column terrain loops have no LINQ or boxing, and scratch arrays are per thread.
`src/Core/ColumnStore.cs`'s own header says it gives no measurable chunk-time gain while costing 29 MB, an LRU and
three thread rules; the worldgen reviewer suggests re-justifying it or reducing it to "the current chunk's columns".

## 6. Thread safety — what was checked

No data race was found in any area. Checked and sound, per reviewer:

- `Params` is published before worldgen threads start; `ClimateLapse` uses an immutable table behind a volatile field.
- River network read paths: per-thread caches keyed by a never-reused owner id, no network reference held.
- Ores: every placement decision is a pure function of seed, position and a stable salt (`StableHash`, FNV
  `StableCode`); no `string.GetHashCode`, no shared `Random`; table pooling and the `Body.Ensure` lock are right.
- Ecology: picks use vanilla's per-chunk LCG and position hashes; hot-path caches are thread-static and keyed by a
  numeric world id; published state is immutable behind a volatile reference.
- Weather: the model, storm cache and the climate shift are thread-safe; no wall clock or per-process randomness, so
  client and server agree.
- Terrain: no unsafe double-checked locking, no int overflow at 4M-block scale, no static state that survives a world
  (the exceptions are the flags and latches listed above).
- The server registers no handler for client packets.

The open thread issues are all of one kind, implicit thread identity: CR-D1 / CR-G1, CR-T1, CR-O2. Also REPORTED
(CR-G9): `Active`, `terrain`, `surface`, `settler`, `queryScratch` in `EarthWorldGen` and `active` / `cells` in
`EarthVolcanoes` are plain fields swapped in `InitWorldGen` and read on other threads; this is safe only because
vanilla's `/wgen regen` pauses the chunk thread first. The comment at `src/EarthWorldGen.cs:324-325` gives a different
reason that cannot occur.

Engine facts the reviewers read in 1.22.7 and relied on: `MagicNum.MaxWorldgenThreads` defaults to 1 (every pass on the
chunk thread); with more, each pass has at most one thread; start-up spawn chunks generate on the main thread; the
prospecting pick runs on the main thread.

## 7. Architecture

**Sound:**

- `src/Core` (127 files) has no `using Vintagestory` at all (grep), so the test projects compile it directly
  (`<Compile Include="..\..\src\Core\*.cs" />`).
- `PatchVanilla`'s bind-all-or-nothing per feature, with signature pre-checks and `Undo`, is sound. The lifecycle
  reviewer counts 15 of 17 Harmony users on it.
- The world-gen pipeline: one writer, a narrow handoff, guards that are pure functions of position.
- The river build is staged (courses → pools → pieces → index) and deterministic by construction.
- Downloads: `.part` with resume, size and SHA-256 before the move, per-read timeouts, a cross-process turn lock; no
  zip-slip, path traversal or client-controlled URL found.

**Weak, with proposals:**

| Id | Problem | Proposal |
|---|---|---|
| CR-A1 | **Fail-closed is implemented case by case.** CR-L1, CR-G2, CR-O1 are the same class | one guarded start-up wrapper on the server; a persisted "terrain made by Earth" mark per chunk; one rule for a degraded session ("do not bind" or "stop") |
| CR-A2 | **Implicit thread identity.** `GetColumn` is documented as pure but `FetchAllowed` / `BuiltInOnly` change its result; session caches are filled by whoever asks first | an explicit per-chunk generation context (coordinates, fetch permission, columns, climate samples) passed through Earth's handlers |
| CR-A3 | **`TerrainModel` is a god object.** 11 partials; about a dozen `*Rules` ints tested inline; a roughly 35-field `Column` struct copied through eight stages; about 60 public mutable fields set by three systems after construction with no freeze; it holds the mutable `EarthData`, not a frozen `EarthDataView` (`src/Core/TerrainModel.cs:17`), and its caches are not keyed to the view. `tests/PlaytestReviewTests/Program.cs:293-294` already toggles `SetReservoirExclusions` under a live model | capture `Data.View` and an immutable rules record at build; the column pipeline as stateless stages over it |
| CR-A4 | **`EarthMapSystem` is a 1,100-line god class**: lifecycle, the river query API, data provisioning, params resolve/repair/compute, model-cache key, tree catalogue, client sync. `EarthParams` mixes frozen rules, per-start handshake fields, unsaved outputs and the model builder, and the whole object goes to every client | extract a `WorldParamsFactory`; a small client handshake DTO |
| CR-A5 | **A new world's params have two writers in sequence.** `ComputeParams` (`src/EarthMapSystem.cs:920-1007`) sets ore, geology, fauna and ground fields from `earth.json`; `EarthWorldOptions.Freeze` (`src/EarthWorldOptions.cs:69-142`) overwrites them from the Earth tab. `RepairSaved` reuses `ComputeParams` for a saved world, so its "this new world…" log lines and side effects fire for an old save | one builder with one precedence rule (tab, then config, then default); a layout-only function for the repair |
| CR-A6 | **Rule versions.** Contained in the pool planner (`RiverPoolRules`); elsewhere about ten raw `Rivers.PoolLayout >= n` tests in `TerrainModel.Rivers.cs`, more in `RiverAudit` and `EarthMapSystem`. Ore generations v2/v3/v4/0060 coexist as flags (`RulesV4`, `layers == null`, `Lens`, `anyRock`, `BodiesAfterStrata` / `AfterGround`) in one generator. Three "rules versions" share one number series for ores | a policy struct per rule family, as `RiverPoolRules` is; a per-session ore strategy object (v3, v3 with layers, v4) with assets pinned by the saved version |
| CR-A7 | **`RiverNetwork.cs` is 1,224 lines**: about 190 lines of layout history in doc comments, hydraulic formulas, the candidate cache, four queries and a 150-line `HitAt`. `ApplyRiver` and `ApplyChannel` duplicate about 100 lines; the beside-a-lake rule exists three times (`TerrainModel.Rivers.cs:118-128`, `285-300`, `568-579`); the arid `Dry(...)` expression four times | split into index/locator, hydraulics and pool-profile lookup |
| CR-A8 | **Five parallel "what lives where" implementations**: `RegionalTreeCatalog.Rule`, `RegionalPlantCatalog.Rule`, `PlacementCatalogue.Taxon`, `RegionalFaunaCatalog.Rule`, `VanillaFaunaRanges.Entry`. Three JSON stacks (System.Text.Json DTOs, `JsonDocument`, Newtonsoft); four error policies (trees skip and warn, fauna quarantines, plants/placement/vanilla fauna throw the whole file); the 1318 "lost unless current mask" logic twice; base64 mask decode four times | one `Range` type (regions, boxes, raster, 1318 overlay, elevation, climate envelope), one loader, one policy; one "ecology site" computed once per position |
| CR-A9 | **Patching is not uniform.** The weather's patches sit outside `PatchVanilla` (see CR-W5); `src/EarthGrassSeason.cs:121`, `131`, `287` look up `RedrawAllBlocks` and `GameMain.ColorMaps` by name with `?.Invoke`, a silent no-op after a rename; `src/EarthWorldGen.cs:113-114` duplicates `PatchVanilla.For`. **Patch lifetime differs:** most systems unpatch in `Dispose`; five keep their patches for the process behind a static `patched` flag (`src/ClimateLapse.cs:45`, `src/EarthGrassWinter.cs:85`, `src/EarthGroundMoisture.cs:87`, `src/EarthGrassSeason.cs:71`, `src/SeasonTintAltitude.cs:46`), so a vanilla world after an Earth session keeps a prefix on `Block.PerformSnowLevelUpdate` and two postfixes on `BlockSoil.ShouldReceiveServerGameTicks` | one policy; move the weather into `PatchVanilla` features |
| CR-A10 | **The weather model is built by a 1 Hz poll** of `earth.Params` (`src/EarthWeather.cs:83`, `134-189`, VERIFIED), not when the params arrive: up to 1 s of vanilla weather at start and join, and every client join forces a full `capi.Shader.ReloadShaders()` (the comment at `:184` admits the race) | `EarthMapSystem` raises an event when `Params` are set; build synchronously |
| CR-A11 | **Downloads have three copies** of "missing, take turn, missing again, fetch" with drifting policy (`src/Core/GroundDownloader.cs:83-93`, `src/Core/EarthDataStore.cs:157-170`, `src/Core/DemDownloader.cs:83-107`); three sync-over-async wrappers; four separate tile caches; the MED predictor and `UnPaeth` written twice; two square-key conventions (`FineElevation.Key` south-up, `DemTiles.SquareIndex` north-up) | one `FileSet.Ensure(folder, files, source)` returning Here/Lacking/Failed |
| CR-A12 | **Outline code is triplicated**: three Chaikin `Smooth` variants (`src/Core/Coasts.cs:303`, `src/Core/Islands.cs:272`, `src/Core/LakeOutlines.cs:724`); two near-identical edge-grid indexes (`Coasts.cs:196-388`, `LakeOutlines.cs:301-376`, `525-552`) | one shared `EdgeGrid` |
| CR-A13 | **The mod is not inert in non-Earth worlds**, and nothing says so: `assets/earth/patches/ground-*.json` and `pan-placers.json` are unconditional; `ResolveParams` writes `earth-params` (disabled) and `earth-creating` into every vanilla save (`src/EarthMapSystem.cs:475-476`); the creative panel gets Month/Day rows; `/earth date` and `/earth boattest` work everywhere | document it, or gate what can be gated |
| CR-A14 | **`WeatherContract`'s `version`** is a bare int with no stated compatibility rule | state the rule |

## 8. Tidiness

Measured by the lead reviewer on `src/`:

- Build: 0 warnings, 0 errors. `Nullable` is disabled; no analyzers; no `.editorconfig`; no `.gitattributes`.
- Line endings: 38 of 195 files CRLF, the rest LF.
- Long lines: 793 over 160 characters, 69 over 220, the longest 432 (`src/EarthGeology.cs`). Most in
  `src/PatchVanilla.cs` (82), `src/EarthGeology.cs` (49), `src/EarthMapSystem.cs` (46).
- Minified style (`harmony=new Harmony(HarmonyId);`, `try{got=...}`) in `src/EarthGeology.cs`, `src/EarthFauna.cs`,
  `src/Core/RegionalGeologyCatalog.cs`, `src/Core/RegionalFaunaCatalog.cs`; reviewers add `src/EarthGround.cs`,
  `src/EarthCorals.cs`, `src/Core/KnownSummits.cs`, `src/Core/PeakCatalog.cs`, `src/Core/NavigableStraits.cs`,
  `src/Core/WadiFans.cs`, `src/Core/AridHydrology.cs`.
- 142 catch-all blocks (`catch {` or `catch (Exception`), most of them log once; 7 empty catches, all on file clean-up
  or probing. 225 static mutable fields; 80 `[ThreadStatic]`; 49 `lock`.
- No TODO, FIXME or HACK markers.
- 18 `ModSystem` classes, each with a `Dispose`; 17 places create a `Harmony` instance.
- Tests: about 30 console programs under `tests/` with a hand-written `Check(...)`, no shared runner and no CI.

Reported by reviewers (not re-read):

- Stale or wrong comments: `src/EarthWorldGen.cs:324-325` (handoff kept on regen); `src/EarthPlants.cs:22-25`
  (on-trunk patches "never call Generate"); `src/EarthPlants.cs:242` ("that check has no site height");
  `src/Core/WeatherContract.cs:55-57` (thunder stays vanilla's).
- README drift: `README.md:839` still says the creative date's patches stay outside `PatchVanilla` (they are
  `CreativeDateTable`); `README.md:80` says 39 features where the enum has 45.
- Dead or unused: `WeatherSettings.Variability` and `Gusts` are never set; `TreeSite.RiverChannel`
  (`src/Core/RegionalTreeCatalog.cs:420`, `1011`) is never set by the game; `CoastData.Margin` is read but never
  checked against `Reach`.
- Duplicated constants and helpers: `0.0065` thirteen times in `src/Core/TerrainModel.Ecology.cs` beside
  `ZoneDrivers.LapseRate` and `EarthWorldGen.LapsePerMetre`; `WeatherModel.Fbm` duplicates `WeatherNoise.Fbm`; varint
  and Brotli readers re-implemented in `StreamHeads` and `DamRemoval`; "is rock" defined four ways
  (`EarthGeology.isRock`, `OreBlockNames.RockName` / `PlainRock`, `OreSession.IsRock`, `EarthOreDeposits.ground`).
- Misleading file names: `StableHash` is in `GeologyTransitions.cs`; `OreRules`, `OreTuning` and `OreProfile` are in
  `RegionalOreCatalog.cs`; `OreBackground` and `EarthDepositSet` are in `OreDeposits.cs`.
- Test-only oracles in production code: `RockAt`, `RockAtV2`, `AllowsClay` / `Peat` / `Salt`, `SaltEvidenceIntersects`.
- Inline thresholds in `src/AlpineSurfacePass.cs` (−12, 0.5, 6, 10, 13).
- Inconsistent exception types: grids and `GroundTile` throw `FormatException` where `EarthBinary` promises
  `InvalidDataException`.
- `src/EarthElevationSystem.cs` holds server logic, the release-file policy, protocol types and the client dialog.

## 9. What the review did not cover

- Nothing was run: no test project, no game session, no profiler. Every "effect in game" above is deduced.
- `src/SelfTest.cs`: entry points and gating only (it is fully config-gated).
- `src/Core/GroundColumns.cs` lines 520 to 1285 (rules and recipes); the tile and format half was read.
- `src/Core/GeothermalSites.cs`, `src/Core/GeothermalSitesOsm.cs` and the `HoloceneCones` peak table: skimmed for
  shape; array lengths not checked.
- `src/Core/WeatherData.cs` past line 140 was read by the weather reviewer, not the data reviewer.
- `worldconfig.json` and `assets/earth/patches/`: headers and counts only. JSON assets were checked by script, not
  line by line.
- Other mods' sources (Cylinder World, AstraTerra, Sea, Hydrology) were not read; bindings were judged from Earth's
  side only.
- Harmony targets were spot-checked against 1.22.7 in the weather and lifecycle areas; the rest rely on
  `PatchVanilla`'s own mismatch check.
- `tests/`, `tools/` and the Python bake pipeline were not reviewed.
- Not verified: `MayBeDrawnRapid`'s "necessary condition" claim against `EarthWorldGen.DrawnRapidSources`; whether
  `NearRiver` reaches as far as the settle margin; how `EarthMapSystem` treats unreadable optional grids for saved
  worlds; the memory size of the single-player client's second copy of the climate data (`src/EarthMapSystem.cs:1037`).

## 10. How to re-check a finding quickly

```bash
cd "/c/VS mods/Earth/.claude/worktrees/release-1.0"
git diff a42df52 HEAD -- src            # empty at 93ab23e: the line numbers hold
grep -rn "FetchAllowed" src             # CR-D1: one writer, EarthElevationSystem.cs:355
grep -rn "GetColumn([^)]*+ \?0\?\.5" src   # CR-R1: StreamSprings.cs:121, 280, 288, 295
grep -n  "BuiltInOnly" src/Core/Png.cs src/EarthCommands.cs   # CR-T1: none
```

Engine files used for the verified findings, under `C:\VS-ref\decompiled\1.22.7\VintagestoryLib\`:
`Vintagestory.Common/APIBase.cs` (`StoreModConfig`), `Vintagestory.Common/ModLoader.cs` (`TryRunModPhase`),
`Vintagestory.Server/ServerSystemSupplyChunks.cs` (`runGenerators`, `loadChunkAreaBlocking`),
`Vintagestory.Common/ChatCommandImpl.cs` (`Execute`).

## 11. Fixes, 12 November 2026

A fix session (branch `claude/review-1111-fixes` from rc.7 `5d7ca31`, in this worktree; **not committed**) re-checked
every finding of sections 3 and 4 against rc.7 (three read-only verifiers, then the lead re-read what it changed) and
fixed what held. Each change carries its id in a code comment. Nothing was run in game.

**New rules field.** The four fixes that change what a new chunk looks like are frozen per world, as the standing rule
asks: `EarthParams.GenFixRules` (ProtoMember 200, free on every branch on 12 November), `TerrainModel.GenFixRules`,
`GenFixRulesVersion = 1`, set by `EarthWorldOptions.Freeze` for new worlds only. Older saves read 0 and generate as before.

Status: **fixed**; **fixed (new worlds)** = behind `GenFixRules` 1; **text** = comment or document only; **left** = not
changed, with the reason; **refuted** = the verifier found that the finding does not hold as stated.

### Section 3

| Id | Status | What changed |
|---|---|---|
| CR-L1 | fixed | `EarthConfigStore.Load` catches a failed write-back. `EarthWorldGen.InitWorldGen` refuses when an Earth save (`EarthMapSystem.ExpectsEarth`: enabled or unreadable saved params, or a new world of an Earth play style) has no params, or has enabled params and no model. `EarthMapSystem.InitWorld` refuses when `EarthWorldGen` did not finish its `StartServerSide` (`Started`). Layer 2 (a wrapper round `StartServerSide`) is covered by these two cross-checks. |
| CR-G2 | fixed | Earth's terrain writer is called through `MakeTerrain`: a throw is logged, the chunk noted and the world refused. The surface pass notes a chunk that vanilla's terrain made before it refuses. Noted chunks (`earth-failed-chunks` in the save) are deleted with `DeleteChunkColumn` at the next start that sets the world's generation up, so they generate again. A persisted per-chunk "made by Earth" mark was not added. |
| CR-O1 | fixed | `EarthOres.Activate` leaves Earth's ore layer off when rules v4 have no ore layers this session (geology inactive or without layers), as it already did without the deposit hook. |
| CR-L2 | fixed | `RequiresPlayer` moved from `/earth` to the subcommands that read a position (`trees`, `geology`, `ores`, `fauna`, `plants` added). The bare `/earth` tells the console which subcommands it can use. |
| CR-D1 / CR-G1 | fixed | The main thread's `FetchAllowed` is cleared when the game enters RunGame (the spawn chunks are made by then). `StoryPlacer.GroundMean` may wait for a tile while it reads the world's own heights (scoped and restored). `/earth volcano` keeps a plan it built only when it read no built-in height in place of a tile still to come (`MissedReads`). |
| CR-R1 / CR-T3 | fixed (new worlds) | `StreamSprings.BlockColumn`: plans read `GetColumn(x, z)` at the block's corner, as generation does. `RiverAudit` (an audit, all worlds) now samples the same way. No `ColumnAtBlock` API was added. |
| CR-E1 | fixed (new worlds) | The plant packs' envelopes (`EarthPlants`, cached per cell as `EnvelopeRain`) and fruit trees (`EarthPlacement.EnvelopeRain`, also in `/earth placement`) read `RiparianRain`. |
| CR-T2 | fixed (new worlds) | `TerrainModel.DryLandTop`: dry land below 0 m stands one block higher than the sea floor's curve, so it continues from the land at 0 m. Land at a negative lake level then stands a block above the lake, as at positive levels (the related `WaterTopY` asymmetry). The sea floor and the coast's clamp are unchanged. |
| CR-G5 | fixed (new worlds) | `TerrainColumns.Hash(x, z) % 4 == 0` in `AlpineSurfacePass`. |
| CR-T1 | fixed | `MapColors.Render` runs on `ParallelWork.For`, each worker with `BuiltInOnly` set and restored. |

### Section 4

| Id | Status | What changed, or why not |
|---|---|---|
| CR-L3 | fixed | `BuildAndAttachModel` throws after `ShutDown`, as `Refuse` does. |
| CR-L4 | fixed | The client keeps the sea level it found and puts it back on a disabled-params packet and in `Dispose`. |
| CR-L5 | fixed | `/earth tp` rejects a latitude or longitude that is not a finite number. |
| CR-L6 | fixed | `/earth where` uses the regions the tree catalogues loaded; it reads the file only where neither loaded. |
| CR-G3 | fixed | The verifier confirmed it and found the proposed fix half right. The deferred guard now takes `WouldOverlapAt`'s own `pos` and schematic. Where vanilla moved the structure (the cave-attached path) it checks `[pos, pos + Size]`; otherwise the noted position, as before. `MiddleCenter` origins (the vugs) now count as centred, with their top half the height up. This changes which structures are placed in new chunks; structures already differ from run to run, so no rules version. |
| CR-G4 | fixed | `SpringsAndPonds.ResetCounters` and `StructurePlacementGuards.ResetCounters`, called in `EarthWorldGen.StartServerSide`. |
| CR-G6 | left | Needs a per-column rule (or a dither) and a rules version: a design choice. |
| CR-T9 | text | The "seamless" comment in `GroundRelief` now says that east-west neighbours share the lattice and north-south ones may not. The real fix changes the ground (ground rules version). |
| CR-T10 | left | Confirmed in code (it also strips a lower terminal lake under a higher kept one within 3 cells); not checked against the data; needs a water rules version. |
| CR-G9 | text | Confirmed benign (regen pauses the generation threads). The comment at the handoff now gives the real reason. |
| CR-D2 | fixed | The verifier corrected the figures: the 3 minutes is the header timeout, and each body read may wait 1 minute, so about 28 minutes worst case, and no bound for a server that trickles. `DataTransport` now has a 15 s connect timeout. While the game runs, chunk generation waits at most 2 minutes for a square (`DemDownloader.AwaitForGeneration`, used by `PrepareChunk` and the fetch inside a chunk). After that the square takes the built-in heights for the session through the existing failure path, and the download goes on. Before RunGame the wait stays unbounded (a failure stops the server, as before). |
| CR-D3 | fixed | `Prefetch` skips a square whose prefetch failed: 5 minutes, doubling to an hour. |
| CR-D5 | fixed | `DataTransport.FreeBytes`: the mounted drive whose root is the longest prefix of the folder. Not tried on Linux. |
| CR-D6 | fixed | `Source`, `Repo`, `FreeBytes` and the batch error go to players with `controlserver` only. |
| CR-D8 | fixed | `manifest-egl1.json.repo` records the release a cached manifest came from; a copy is used only for that release (a copy without the record counts as the default release's). There is no refetch after a hash mismatch. |
| CR-D9 | fixed (partly as stated) | The verifier: `IndexOutOfRange` can't come from `Decode`; it comes from readers given a tile that isn't 1200 rows. `Decode` refuses more than 4096² cells (InvalidDataException). `FineElevation.Load` refuses a tile whose size isn't GLO-90's, so it goes to `.bad`. |
| CR-D10 | fixed | The thread memo keys on a reader number, as `CoarseElevation` does. |
| CR-D11 | fixed | `modinfo.json` and `README.md`: about 290 MB, plus 170 MB of 30″ ground sets for the default ground columns. |
| CR-R3 | left | Confirmed in code; not small (arms laid out after their main reach, a pinned split vertex); it changes pools (rules version). |
| CR-R4 | fixed | `ReachInMaskRiver` reads the network's reaches only for a model without kept courses, so the offline tests answer as the game does. The game is unchanged. |
| CR-R5 | left | Confirmed (e.g. (3000, 3000), length 4243, `Len` 1), legacy worlds only (`RiverEnds` 0). Those worlds are frozen and can opt into river ends; a fix would change them. |
| CR-R6 | left | Performance; measure first (section 5). Behaviour-preserving if done. |
| CR-R7 | fixed | The planner counts the basins still lowered when its rounds run out; the start logs a warning with the count. |
| CR-R8, CR-R9 | left | They change river water (rules version). R9's threshold is unverified. |
| CR-R12 | left | Holds in part ("not ready" is backed off; the missing-tile path is not). A `TryRiverAt` is a change to Hydrology's contract (decisions 0024, 0027). |
| CR-O2 | refuted (mostly) | The engine runs TerrainFeatures only after all 8 neighbours have finished Terrain, each with its own 64-block margin, so tiles are here 96 blocks out: past the 80 and 95 the ore reads reach. What is left: a neighbour loaded as Done from the save while its tile isn't on this machine (a copied world, a cleared cache). Not changed. |
| CR-O3 | fixed | A reading through an old variant after `/wgen regen` goes to the current session's binding of the same code (`EarthOreGenerator`). |
| CR-O4 | fixed | The process-wide switch-off of the host search is gone (a failing call still returns shift 0). The error latches are per session (`OreSession`) or per world (`EarthOreLayers`). |
| CR-O5 | left | Latent while only v1 assets exist; selecting assets by the saved version is a design change. |
| CR-O8 | fixed | Prospecting uses placement's interpolated district strength (`OreV3Bodies.PlacedStrength`, shared with `Place`, the same arithmetic, so placement is unchanged). |
| CR-O9 | fixed | Bodies and areas whose box would leave the ring's usable span are left out at load, with one warning. A script check of the shipped catalogue found none within reach of the seam at any scale (nearest body 3.85° away, needing at most 0.70°). |
| CR-O10 | fixed | `Ours()` compares with a trailing separator. The fixing agent also found that for a zip install `Mod.SourcePath` is the zip while the assets come from the unpack cache folder, so `Ours()` may never match there and the stone-texture rebuild may never run from a release zip. Not changed: it would change crafted stone for zip players, the author's call. Not verified in game. |
| CR-E3 | fixed | With the placement catalogue, a missing or unreadable `regionalplants.json` turns the packs' ranges off and keeps the catalogue's gates. |
| CR-E4 | fixed | `failureLogged` is reset in `EarthTrees.BeginBinding`, `hookErrors` in `EarthPlants` and `EarthPlacement` `StartServerSide`. |
| CR-E5 | partly fixed | (c) fixed: the fauna site cache matches only the same lookup (coast or land). (a), (b) and (d) left: latent with today's data, and each changes generation. |
| CR-E2 | fixed | `EarthGrassSeason` works out the half-month step first and evaluates the wetness at its centre. |
| CR-W5 | fixed | Worse than stated (the wave counter already runs backwards at X < −0.25). Where a vanilla wind reader was not handed the vector's length, the client writes vanilla's form, the strength in X (`VanillaWindVector.ReadersTakeVector`). `EarthWeather.RainPatched`: Earth's thunder and the ground moisture use the model only while the rain patch is in. `PatchVanilla`'s header now says how weather is kept whole by these flags. |
| CR-W6 | fixed | A prefix on `RainStopFunc` answers `/weather stoprain` (with skip-forward) that it changes nothing while Earth's rain is in force and no admin has set the precipitation. `/whenwillitstopraining` already reads Earth's rain. |
| CR-W7 | left | Which half-year a fixed season means in each hemisphere is a decision. |
| CR-W8 | fixed | The region tick's prefix keeps vanilla's strike rate and a finalizer on the same patch (in `PatchVanilla.ThunderTable`, bound with the prefix) puts it back, so a save keeps vanilla's rate. Open: if a region unloads in the tick where vanilla writes the zeroed state into its mod data and before the next save, that one event keeps 0 after a reload (only while Earth's thunder is off). |
| CR-W9 | text | Holds in part (the role is published only once a model exists). The comment says thunder is `EarthThunder`'s and that the flag stays true while an older Sea keeps the wind. |
| CR-W10 | fixed | Holds in part (the start already aborted). `EarthMapSystem.InitWorld` turns a `MissingMemberException` from Cylinder World's reader into a `Refuse` line naming the member. |
| CR-W11 | fixed | `AstraTerraLongitude` warns once when the member it reads is missing or unreadable; a setting that is simply off stays silent. AstraTerra's current source still matches. |
| CR-W12 | fixed | The method lookups are inside their try blocks (`CloudTick`, the wind-vector readers). `/ew` says snow or rain by the temperature at the player's block and hour. |
| CR-A9 (grass lookups) | fixed | Both members exist in 1.22.7; `EarthGrassSeason` now warns once when a lookup comes back empty. |

### Sections 5 to 8

- **Performance (section 5):** only CR-D4 was changed (store files read into one array of their size; the bytes are the same). The rest were left to be measured first.
- **Architecture (section 7):** proposals; none were taken up. CR-A1's "one guarded start-up" is approached by the cross-checks of CR-L1 and the refusal path of CR-G2, not by a new wrapper.
- **Tidiness (section 8):** the stale comments at `EarthWorldGen` (handoff), `EarthPlants` (site height) and `WeatherContract` are corrected. The one at `EarthPlants.cs:22-25` was refuted (in 1.22.7 the on-trunk patches are placed with `TryPlaceBlockForWorldGen` and never call `Generate`). README: the `PatchVanilla` count is 45 (39 for world generation), and the creative date's patches are its `CreativeDateTable`.
- **Also found while fixing:** after `Refuse` turns Earth inactive, another generation thread could hand a chunk to vanilla's terrain in the seconds before the server stops. Earth's terrain wrappers now note such a chunk and throw instead.

### Verification

On the fixed tree, 12 November (offline only; rc.7 `5d7ca31` run alongside as the baseline where it builds):

- `EarthMap.csproj`, Debug and Release: 0 warnings, 0 errors. Every edited file kept its line endings.
- PatchVanillaTests 385 checks passed (45 features in 15 tables, with the thunder finalizer and the structure guard's new arguments). EarthWorldOptionsTests 758 passed (rc.7: 741; the 17 more are its release-zip checks, which found a zip here). ElevationTests on `rawdata/copernicus-glo90-sample`: all 16 pass, as on rc.7. ColumnStoreTests 81, RiverWaterTests 39, PlacementTests 511, SoilTests 232, RegionalTreesTests 189, OreDepositsTests, RegionalPlantsTests, WeatherTests and WeatherContractTests: all pass.
- EarthMapTests: the same single failure as rc.7 ("Lake Geneva: no steeper steps than before (6)"); the rest of its output, river audit included, equals rc.7's but for timings. Its new check `CheckDryLandBelowSea` (CR-T2: two-block ledge kept for older worlds, one block with `GenFixRules` 1, monotone from −500 m to 10 m) passes.
- Broken before this session, not changed: OreLayersTests does not compile (`GroundColumns.cs` uses `ShoreMaterial` since decision 0070, which that project doesn't include); RequiredModsTests stops at "the 1.0.2 packs' 9 files are store files, of 37" (the store has 38 files since `shores-osm-1.bin`).
- **AgentBridge bench, 12 November** (test zip `earth-2.0.0-rc.7-review.1` from this tree, with Cylinder World 1.1.0-rc.3 and Hydrology 0.6.2-rc.5 from the author's Mods; new 1:100 world `review1111`, sea level 110):
  - The world was created and ran with no warning or error line from any mod.
  - CR-T2 in generation: Caspian lowland at −24 m (47.3° N, 51.0° E) has its top block at y=104, both in the model (`/earth`) and in the generated chunk. The old rule gives 103 with this world's curve. Death Valley at −85 m: 97 in both (old rule 96). So `GenFixRules` 1 is frozen into a new world and generation follows it.
  - CR-E1: at the Nile at Luxor, `/earth placement` reads rain 145 (the desert's own is 0) and lists onion, flax, rice, fennel, garlic and pomegranate as in range and climate.
  - CR-L5: `/earth tp NaN 10` is refused with its message. CR-W6: `/weather stoprain` gives Earth's answer. `/ew`, `/earth where` and `/earth volcano` answer.
  - CR-L1 layer 1: restarted with `ModConfig/earth.json` read-only, the world loaded and ran with Earth active and one warning "Could not write earth.json back …". The same warning line was logged a second time when the player joined; Load has only one call site and no second start appears in the log, so where that copy comes from is not known (harmless).
  - The bench's Mods folder, `earth.json` and window setting were put back afterwards.
- Not seen in game: CR-R1 (this world has springs rules 1, cave springs only, so no stream springs are planned), CR-G5 (no dry river bed visited), the refusal paths of CR-L1 (layers 2 and 3) and CR-G2, the console commands (the bench has no console), the wind-vector fallback (needs a refused patch). `DataTransport.FreeBytes` was not run on Linux.

## 12. Check of the fixes, and three follow-ups, 12 November 2026

The reviewing session re-read the whole uncommitted diff of section 11 against each finding and against the 1.22.7 engine.
The fixes hold. Checked in the engine for them: the mod's start-up runs before WorldReady, the spawn chunks are made
after WorldReady and before RunGame (`ServerMain.cs:1278-1294`), so the cross-checks of CR-L1 run before any chunk and the
main thread's fetch flag (CR-D1) is cleared after the spawn chunks; `SaveGame.ModData` is a `ConcurrentDictionary`, and
`DeleteChunkColumn` queues for the chunk thread, which also drops a loaded copy (CR-G2); the console's caller has no
player (CR-L2). Rules v4 always come with ore layers (`EarthWorldOptions.Freeze`, `WorldUpgrades.ApplyOreRules`), so the
new early return of CR-O1 can't turn the ores off in a healthy world. `ProtoMember(200)` is on no other branch but
`backup/wip-2026-11-12-release-1.0`, a copy of this work.

Three flaws were found and fixed by two follow-up sessions (not committed either):

| What | Where | Fix |
|---|---|---|
| `tests/GroundDownloadTests` no longer compiled: CR-D4 made `EarthDataStore.Bytes` call `EarthBinary.ReadAll`, and the project lists its Core files by hand | `tests/GroundDownloadTests/GroundDownloadTests.csproj` | `src/Core/EarthBinary.cs` added to its files. No other test project has the gap |
| CR-D3's backoff counted one failed download once for every 5 s tick and player that asked while it ran (a 20 s failure as four: 40 minutes at once, not 5) | `EarthElevationSystem.Prefetch` | one continuation per square watched (`prefetchWatching`); 5, 10, 20, 40, 60 minutes as documented |
| CR-D2's two minutes ran from the call, so they also counted the time a square queued for one of the three download slots: on a slow link a square could run out without having started, and its chunks kept the built-in heights | `DemDownloader.AwaitForGeneration` | the budget runs from when the square's download first gets a slot (`started`); queued or not, the wait ends `BackstopBudgets` (5) budgets after the call, ten minutes while the game runs; `PrepareChunk` gives its squares one shared start. The log line says which limit ran out |

Run on the tree with the follow-ups: `EarthMap.csproj` Debug 0 warnings, 0 errors; GroundDownloadTests ALL PASS (47);
ElevationTests on `rawdata/copernicus-glo90-sample` ALL PASS, with three new CR-D2 checks (a queued square is waited
for, a stalled download ends the wait a budget after it began, a square that never gets a slot ends at the backstop);
ColumnStoreTests 81, OreDepositsTests, EarthWorldOptionsTests 741 (758 with a release zip in place), PatchVanillaTests 385
(against the Release build of 12 November 20:08); EarthMapTests: the four `CheckDryLandBelowSea` checks pass, the one
failure is "Lake Geneva: no steeper steps than before (6)", reported in section 11 as rc.7's too (not re-run on rc.7 here).
The prefetch fix has no automated check.

Consequences of the fixes to know, not changed:

- CR-G2: a chunk whose terrain throws now stops the server. One that throws every time stops it each time a player comes
  near (deleted, generated again, thrown again), where before it left one broken chunk and the game went on.
- CR-D1: a new world's story locations wait for their GLO-90 tiles at start, as the spawn chunks do, so a new GLO-90 world
  can't be made offline unless those tiles are on the machine.
- CR-O4: without the process-wide switch-off, a host search that fails every time throws and is caught for every deposit
  try (logged once, slow).

Still unexplained: the bench's server log has "Could not write earth.json back" twice, at the mods' start (20:32:28) and a
second after the player joined (20:32:47); `EarthConfigStore.Load` has one caller, and `ResetFlags` logs another text.

The test zip `earth-2.0.0-rc.7-review.1` (bench, and installed in the author's game on 12 November) was built before these
three follow-ups; it does not have them.
