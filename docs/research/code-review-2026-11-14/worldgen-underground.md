# Earth code review, 14 November 2026: world-generation passes and the underground

Area reviewer's findings. Read-only: nothing in `C:\VS mods\Earth` was changed, built or run. Read at `6371e32`
(2.0.0-rc.13); `main` moved to `b70ebbf` during the review (site drivers, packed config JSON). Of this area only
`src/EarthWaterTable.cs` changed in that merge (+20 lines for the site query); the line cited from it was re-checked.
Engine facts are from `devtools\vs-ref\decompiled\1.22.7`.

VERIFIED = the code path was read end to end. PLAUSIBLE = the code allows it; the trigger, the cost or the visible
effect was not measured or seen. Nothing here was measured or reproduced in game.

Files read whole: EarthWorldGen, TerrainChunkWriter, TerrainHandoff, ChunkSettler, AlpineSurfacePass, RockFloorPass,
EarthVolcanoes, Core/Volcanoes, Core/VolcanoInterior, Core/Geothermal, EarthCrafting, SnowMeltLight, EarthGeology,
EarthGround, EarthOreLayers, Core/OreLayers, EarthOreGenerator, EarthOres, OreSession, OreBauxiteStrata, EarthWaterTable,
Core/GroundRelief, EarthCorals. Read in the parts that run per chunk: EarthOreDeposits, Core/OreDeposits (Ensure,
Relocate, BodiesNear), OreV3Bodies, OreBinder, EarthSprings, RiverBlockPass, SealevelDeposits, BlockLayerAdjustments,
Core/GroundColumns (tile cache, `At`). Only grepped: StructurePlacementGuards, SpringsAndPonds, WorldGenBlocks,
StreamSprings, StreamHeads. Not read: OreProspecting, OreProviderBlocks, OreTotals, RegionalOreCatalog,
RegionalGeologyCatalog, GeologyReference, GeologyTransitions, Core/WaterTable, SoilFertility/SoilNiche/SoilSiteGrid,
StoneRecolour, HoloceneCones (beyond its header), EarthStoneTextures (beyond its first 80 lines).

---

## A. Correctness

### W1. A volcano's plan (and rules 3's cone) is built from far-away heights, by whichever chunk comes first, and is not kept across sessions
- **Severity:** medium. **VERIFIED** for the trigger distance and the missing guard; **PLAUSIBLE** for the visible seam.
- **Where:** `src/EarthVolcanoes.cs:262` (index reach), `:285-294` (`PlansNear`), `:297-312` (`MakePlan`);
  `src/Core/VolcanoInterior.cs:129-149` (the cone's rings); `src/Core/Volcanoes.cs:191-192`.
- **Evidence:** the 512-block cell index lists a body in every cell within `VolcanoPlan.MaxReach(mpb, rules, v.Form)`,
  which in rules 3 is the cone search: `Math.Max(16, CompositeEdificeM / metresPerBlock) + 64` (20 km; shields 40 km).
  `PlansNear` forces the plan for every body listed in the chunk's cell before it tests the real reach:
  `var plan = plans.GetOrAdd(i, k => new Lazy<VolcanoPlan>(() => MakePlan(k))).Value;`. The plan's constructor then
  reads up to 161 x 48 = 7,728 full model columns out to 20 or 40 km: `var c = model.GetColumn(ventX + r * Math.Cos(a), ventZ + r * Math.Sin(a));`.
  So the first chunk generated within 20 to 40 km of a volcano (plus up to 512 blocks of index slack) plans it on the
  generation thread, reading GLO-90 squares up to 40 km away. That thread may wait for those squares (CR-D2's fix: two
  minutes each in the running game), after which a square "takes the built-in heights for the session". The two other
  callers refuse such a plan (`MapPlan`: `if (FineElevation.MissedReads != missed) { mapRetryAt[i] = ...; return null; }`;
  `CmdVolcano` the same); the generation path keeps it. The plan is per session, so the next session plans again from
  the real squares: the vent, rim, crater floor, cone base (`BaseY`), cone radius (`EdgeR`) and layer profile can differ
  between chunks made in the two sessions.
- **Also a cost:** one plan is thousands of `GetColumn` calls plus square loads, inside the Terrain pass of a chunk that
  may be 40 km from the volcano. Once per volcano and session.
- **Fix:** save each plan's numbers in the save the first time generation makes it (vent, summit, rim, floors, base,
  edge radius, the ring profile: a few hundred bytes per volcano) and read them back in later sessions; do not keep a
  generation-thread plan whose planning counted `MissedReads` without saving it as final. Cheaper first step: test the
  vent's distance against `MaxReach` per body before forcing the `Lazy` (the cell index over-reaches by up to a cell).
- **Risk:** none to worlds whose squares were always there (same numbers). No rules version. Needs one new save key.

### W2. Ore rules v5's valley search runs inside `Ensure`, on whichever thread asks first, and has the same session-only result
- **Severity:** medium. **VERIFIED** for the call paths; **PLAUSIBLE** for the cost (the code times it as `SearchMs`).
- **Where:** `src/Core/OreDeposits.cs:563` (`BodiesNear` calls `Ensure(b)`), `:686-734` (`Ensure`, under `lock (b.Gate)`),
  `:765-796` (`Relocate`), `:444` (`RadiusKm = 30`, `StepMetres = 200`); `src/EarthOreDeposits.cs:192-203` (`LayerBlocks`),
  `:396`, `:413`; `src/OreSession.cs:254`.
- **Evidence:** a movable blanket with none of its layers under its footprint is searched ring by ring:
  `for (double r = ...; r <= max; r += step) { int n = Math.Max(8, (int)Math.Ceiling(2 * Math.PI * r / step)); ...`.
  A body that finds nothing asks about pi x (30000/200)^2, some 70,000 points. Each point is `LayerBlocks`:
  `session.TopAt` (a full model column) and `geology.LayerCodeAt` -> `RockColumnAt` -> `GroundAt` -> `ReliefFor`, whose
  one-chunk cache (W7) is rebuilt whenever the point lies in another chunk. Three callers reach it:
  the deposit pass (generation thread); the prospecting pick through `ColumnBlocks` -> `BodiesNear` (main thread); and
  `/earth ores`, which calls `Plan.BodiesNear(x0, z0, x1, z1)` over a radius of about 50 km
  (`Math.Clamp(50000 / MetresPerBlock, 256, 4096)` blocks) and so runs `Ensure` for every body in that square, one after
  another, on the main thread.
- **Same hazard as W1:** the search reads model columns up to 30 km away. Where a GLO-90 square is not on the machine
  (or its wait ran out) it reads built-in heights, and `b.X`, `b.Z`, `b.A`, `b.B` are kept for the session only. A body
  whose centre differs between two sessions is cut at the chunks made in each.
- **Fix:** save `(body id -> moved x/z, growth, found)` in the save at the first `Ensure` and read it back; keep
  `/earth ores` and the pick from starting a search (report "not searched yet" for a body not yet planned, or plan off
  the main thread).
- **Risk:** none to generation where the squares were there. No rules version.

### W3. Geology, ground and ore passes still fail open, chunk by chunk, and go silent after three lines
- **Severity:** medium. **VERIFIED.** (CR-A1's class; CR-G2 fixed it for terrain only.)
- **Where:** `src/EarthGeology.cs:505-509`, `:513-519` (strata), `:580-593` (cover and ground), `:744-759` (layer pass);
  `src/EarthOreGenerator.cs:463-467`; `src/EarthOres.cs:351-364`, `:374-378`.
- **Evidence:** `if(Interlocked.Increment(ref generationErrors)<=3) Mod.Logger.Error("Earth geology: {0} failed for a chunk; vanilla blocks are kept there. {1}",pass,e);`
  A chunk whose strata or ground pass throws is saved with vanilla strata or half-written ground, for good. An error
  that repeats (a block table one entry short after another mod's blocks, a bad tile) gives a whole session of such
  chunks with three log lines. In the ground pass a throw also leaves the chunk's layer table un-kept, so the deposit
  pass rebuilds it from the model and puts "the column's own block" back into ground that was never written.
- **Fix:** one policy for a failed pass in an Earth world: count per session and stop the server past a small number
  (as `Refuse` does), or note the chunk with `EarthWorldGen.NoteFailedChunk` so it is made again. At least log a
  running count at shutdown.
- **Risk:** none to generation. A design choice (a repeating error then stops the server, as CR-G2's does).

### W4. Rules 3: the conduit's lava is not capped by the column's own top
- **Severity:** low to medium. **PLAUSIBLE** (needs an active vent without a crater, on ground that falls away).
- **Where:** `src/Core/VolcanoInterior.cs:431`; `src/EarthVolcanoes.cs:534-541`.
- **Evidence:** `if (dc <= r) { if (y <= ConduitLavaTop) return Part.Lava; part = Max(part, Part.Crust); }` reads no
  `top`; the caller loops `hi = Math.Min(mapHeight - 2, Math.Max(top, it.ConduitTop))` and writes
  `case VolcanoInterior.Part.Lava: SetLava(chunks, lx, y, lz); break;` for any y. `top` is used only for dykes, the ring
  dyke and the hot zone. With a crater the floor is flat out to 0.35 x its radius and the funnel is 0.25 x, so the lava
  (3 blocks under the vent's top) is covered. Without one (`Crater` is false when the vent column is wet: a crater lake,
  a submarine or shore vent) the conduit is `R0 = max(1.5, 40 m / mpb)` blocks wide, 8 at 1:5, 10 for shields, and a
  column inside it whose top is more than 3 blocks under the vent's gets lava above its ground, in air or water. The
  class comment's promise ("Lava is always 1.5 blocks or more inside solid rock") is not enforced for the conduit.
  Rules 2's `Body` has the same shape with a 20 m conduit.
- **Fix:** in `At`, a conduit block above `top - 2` is `Crust`, not `Lava` (and no solid part above `top` where the
  column is under water).
- **Risk:** changes blocks in those columns of rules-3 worlds: geothermal rules 4, or accepted as an RC fix.

### W5. `RegionalRock` knows only the top 512 blocks of a ground column
- **Severity:** low. **VERIFIED** in code; needs a world higher than 512 and a column that does not hand off.
- **Where:** `src/EarthGeology.cs:829`, `:895`, `:867`, `:907`.
- **Evidence:** `public readonly int[] GroundIds=new int[512];` and
  `c.GroundDepth=ground.Column(gs,surface,c.GroundIds,Math.Max(0,top),out var built);` (`EarthGround.Column` stops at
  `idsByDepth.Length`). For a column whose top is above y 512, `GroundDepth` is 512 although the ground column owns
  everything down to y 1 (`EarthGround.Write` has no such limit). Deeper, `if(c.Ground && depth<c.GroundDepth)` is
  false and the answer is regional geology's bed (or 0), not the block the ground pass wrote. Readers: the ore plan's
  host search (`HostShift`), prospecting (`PlacedShare`, `EarthOreLayers.Reading`), the water table's `ClassAt`.
  `LayerCodeAt` is right (it reads the segments).
- **Fix:** size `GroundIds` by the map height at init.
- **Risk:** changes which deep deposits under high mountains are moved or kept (`HostShift`), so it needs a version
  gate (GenFixRules or ore rules) for saved worlds.

### W6. A deposit plan that throws half way stays "complete" with another deposit's values
- **Severity:** low. **VERIFIED.**
- **Where:** `src/EarthOreGenerator.cs:339-367` (`Complete`), `:317-337` (`GetPlan`).
- **Evidence:** `p.Stage2 = true;` is the first statement of `Complete`; the slot's `Plan` object is reused
  (`p ??= cache[slot] = new Plan();`) and `GetPlan` resets only the first-stage fields. A throw after that line (the
  model, the heat lookup) is caught in `GenDeposit` for this chunk, but the next chunk asking for the same deposit finds
  `Stage2` set and `Valid` true, with `CentreTop`, `Shift`, `Quantile`, `Clues`, `Lens` left from whatever plan used
  the slot before.
- **Fix:** set `Stage2` last, or `p.Valid = false` in a catch.
- **Risk:** none (error path only).

### W7. Smaller points (all low)
- **Layer table of a crater column, recorded against rebuilt. PLAUSIBLE.** The ground pass (a postfix on vanilla's block
  layers) records `t.Top[k]` before `EarthVolcanoes.Craters` (0.71, same pass) lowers `WorldGenTerrainHeightMap`; a table
  rebuilt later (`EarthGeology.cs:664-669`, after a restart or an eviction) reads the lowered top and the fluid above it.
  The two tables differ in crater and lava-lake columns, so the layer pass can decide differently there in two sessions.
  Fix: none needed unless seen; or record the terrain pass's top in the table's rebuild.
- **`SnowMeltLight`, a cell noted before its commit. PLAUSIBLE.** `SnowMeltLight.cs:101-102` drops a cell whose block is
  still snow (`if (now < m.OldAbsorption)`); only an unloaded chunk is retried. Vanilla commits in the same call on both
  paths (`WeatherSimulationSnowAccum.cs:156-159`, `:244-245`), so only a commit on another thread could lose one. Fix: retry
  an unchanged cell once.
- **`EarthCrafting.Apply` fails open. VERIFIED.** `EarthCrafting.cs:52-56`: a throw is a warning and `return 0`; a
  rules-0 world then keeps Earth's recipes in the server's index. Harmless (they only add options).
- **`TerrainHandoff` entries of a chunk whose pass throws between the writer and the surface pass. VERIFIED.**
  `TerrainChunkWriter.cs:145-146` puts, `AlpineSurfacePass`/`RiverBlockPass` take; a throw in vanilla's block layers in
  between leaves a 1 KB flags array and up to 4 KB of ramps for the session. Negligible; `Clear` them in `InitWorldGen`
  if the handoff is kept across `/wgen regen`.

### Checked and found sound
- **Crafting recipes per world (asked):** `GridRecipes` and `FastSearchRecipesByIngredient` belong to the server's
  `GameMain` instance (`GameMain.cs:36`, `:55`), made anew for each world, and recipes load again from assets, so there
  is nothing to restore for the next world in the same process. `Apply` runs in the WorldReady handler
  (`EarthMapSystem.cs:405`), before `StartBuildServerAssetsPacket()` (`ServerMain.cs:1278-1283`), and before the early
  return for a non-Earth world, so vanilla worlds lose the recipes too. Removing from the ingredient index as well as
  the list is right (`InventoryCraftingGrid.cs:169` searches the index).
- **Fail-closed terrain (CR-G2):** in place. `MakeTerrain` notes the chunk and refuses; `ThrowIfRefused` covers the
  seconds after; `handoff.PutFlags` always leaves an entry and `BareRock` refuses a chunk without one;
  `DeleteFailedChunks` runs after `Active = true`.
- **CR-O1:** in place (`EarthOres.cs:131-137`: rules v4 without ore layers leaves Earth's ore layer off).
- **Threading:** 1.22.7 gives each world-generation pass its own thread (`ServerSystemSupplyChunks.cs:66-71`), so a
  pass never runs beside itself. The state shared between passes is safe as written: volcano plans
  (`ConcurrentDictionary<int, Lazy<VolcanoPlan>>`), layer tables (`ConcurrentDictionary`, released only after the last
  reader), spring plans, the ore session (immutable once sealed). Per-thread caches are keyed by a session number
  (`OreSession.CacheId`, `EarthGeology.epoch`, `TerrainModel.CacheId`), not by an object, so a thread does not keep a
  closed world alive. `HeadPlan` is a struct, so `EarthSprings.Plan`'s `plan.X += shift` changes a copy.
- **`SnowMeltLight` on the main thread:** `MarkAbsorptionChanged` only queues a lighting task
  (`ServerWorldMap.cs:369-390`); vanilla feeds at most 10 chunk columns per 100 ms, so a tick drains tens of thousands
  of cells at the very most, each a map-chunk lookup, one `GetBlock` and one locked enqueue. Safe; a per-tick cap
  (say 5,000) would bound the worst case. The patch stays for the process behind `static bool patched` (the sixth such
  system, CR-A9), and does nothing while `on` is false.
- **Ore layer masks:** rule indices are bits of a `uint`; `OreLayers.cs:208` refuses more than 32 rules.
- **Rules gating of the new work:** `strataBauxite` only with ground rules 2; surface stones only from ore layers v3;
  the aluminosilicate deposits pruned below v4 (removed, not zeroed, so the per-chunk random stream of older worlds is
  unchanged); interiors only with geothermal rules 3 (`rules = model.GeothermalRules >= 3 ? 3 : 2`, and rules 2's index
  reach is the old `MaxReach(mpb)`); crafting by `CraftingRules`. No ungated behaviour change was found in the files read.

---

## B. Performance (none measured)

### W8. The climate and the ground sample are worked out per column, several times per chunk
- **Severity:** medium. **PLAUSIBLE.**
- **Where:** `src/AlpineSurfacePass.cs:159-163` (every dry land column in worlds with zones: `TerrainGeoAt` and
  `SampleClimate`, then glacier cover, four to eight neighbour heights through `Vertical.Elevation`,
  `LooseVolcanicGround`, before `AlpineColumn` returns for warm ground); `src/EarthGeology.cs:703`, `:709-713` (ground
  pass: `SampleClimate`, `RiparianRain`, `Floodplains`); `src/BlockLayerAdjustments.cs` (soil fertility and tall grass,
  cached for one column only); `src/EarthGeology.cs:645` (`ApplyCover`); `src/Core/GroundColumns.cs:967-1024` (`At`).
- **Evidence:** `GroundColumns.At` makes about eight category lookups per column, each
  `byField.TryGetValue(field, ...)` by string plus two `GeologyTransitions.Noise` calls for the warp, three or four
  bilinear numbers, and the cover-region, thickness, relief, seabed and frozen conditions; every cell read goes through
  `Cells()`, which does `Interlocked.Increment(ref clock)` on one process-wide counter (`GroundColumns.cs:217`).
  `GroundPass` does that for all 1,024 columns, and again in full for a rebuilt layer table. `OreLayers.CodeOf`
  (`Core/OreLayers.cs:270-292`) walks the class rules with three `HashSet<string>` lookups per segment
  (`EarthOreLayers.cs:322`).
- **Fix (after measuring a chunk):** one per-thread, per-chunk table of climate samples and geo positions shared by
  the Terrain-pass readers; in the ground model, resolve field sources and category names to indices at load and
  memoise `CodeOf` by (part, source, material); drop the shared `clock` increment to once per column or per chunk.
- **Risk:** none if the values are the same doubles (a cache, not a coarser sample).

### W9. `ReliefFor` keeps one chunk per thread, and the deposit thread asks for many
- **Severity:** medium to low. **VERIFIED** structure, **PLAUSIBLE** cost.
- **Where:** `src/EarthGeology.cs:727-740`; `src/Core/GroundRelief.cs:37-43`.
- **Evidence:** `[ThreadStatic] static ReliefCache reliefCache;` holds the last chunk. On the Terrain thread that is the
  chunk being made. On the deposit thread `RegionalRock`/`LayerCodeAt` are asked for deposit centres in any of the 49
  source chunks (`Complete` -> `HostShift`), for the valley search's points (W2) and for prospecting, so most calls
  rebuild the field: `GroundRelief.Field` allocates `new double[ew * eh]`, `m` and `w` and a closure, reads every
  lattice elevation through `ColumnStore.Near` (a full model column where the chunk is not stored; about 1,500 reads
  at coarse scales where the lattice step is one block) and sums each disc twice.
- **Fix:** a small per-thread LRU (16 chunks) and reused scratch arrays; merge the two disc loops.
- **Risk:** none.

### W10. Earlier performance findings: still as they were
| Id | State | Where now |
|---|---|---|
| CR-O6 | **holds** (unchanged) | `EarthOreGenerator.cs:471-474`: `GetPlan(center)` (five random draws, two distributions) before the bounds reject; `:319` the cache is still `new Plan[2048]`; rules v4 still multiply attempts by `AttemptScale` (`OreSession.cs:213`) |
| CR-O7 | **holds** | `EarthOreLayers.cs:420` reads every block from the top to y 1 in every column; `CodeOf` not memoised (W8); `:297` `MaxTables = 768`, each table 16 KB of per-column ints, 3 KB of bytes and `End`/`Code` fixed at `new int[8192]` (64 KB): about 63 MB when full. In a long session the tables of the frontier's chunks (Terrain done, deposits not) keep it full, and every `Keep` then takes `tables.Count` (all locks) and scans all 768 for the oldest (`:336-342`) |
| CR-G7 | **holds** | `EarthWaterTable.cs:270`: each `WaterTableAt`/`AquiferAt` builds a one-point `BaseField`; `ProbeAt` also uses `core.Model.GetColumn` (not the store) and a full ground column. Seeps ask it per try (`EarthSprings.cs:217`) |
| CR-G8 | **holds**, all three | `RockFloorPass.cs:91` `structures.ToArray()` per chunk; `EarthVolcanoes.cs:68`, `:285` a `yield` iterator per `InCrater` (now also per spring head and seep, `EarthSprings.cs:153`); `EarthSprings.cs:159` `plans.Count > 200_000` per head |
| CR-E8 | **holds** | `EarthCorals.cs:44-45`: four `BlockPos` copies per target |

Cheap wins among them: the `ToArray` (iterate under the list's own contract or copy once per map region), a plain
`for` over the cell's list in `InCrater`, a counter in place of `plans.Count`, and exact-size or `ushort` `End`/`Code`
arrays in the layer table (most of the 63 MB).

### W11. `EarthVolcanoes` reads and writes blocks through the checked indexer in its inner loops
- **Severity:** low. **PLAUSIBLE.**
- **Where:** `src/EarthVolcanoes.cs:314-348` (`SetSolid`, `SetLava`, `Clear`, `Seal`), `:520-531`, `:569-593`.
- **Evidence:** `int id = data[idx];` per block of the cone, and per block a binary search in `it.Layer(c, y)`. The
  indexer's getter is `GetSolidBlock` then `GetFluid` (`ChunkData.cs:63-73`), where the other passes use
  `GetBlockIdUnsafe`/`SetBlockUnsafe`. A chunk inside a big cone at a fine scale walks 1,024 columns x the cone's
  relief this way.
- **Fix:** the unsafe accessors, as `EarthGeology` uses; walk a column's layers by their boundaries (the layer changes
  only where `depth` crosses `layerStart[k]`).
- **Risk:** none.

---

## C. Structure

### W12. The geothermal site lists are generated C#, 269 KB of source
- **Severity:** low. **VERIFIED.**
- **Where:** `src/Core/GeothermalSitesOsm.cs` (1,980 lines, 158 KB), `src/Core/GeothermalSites.cs` (1,661 lines, 110 KB),
  both headed "Generated by tools/bake_geothermal_sites.py. Do not edit."; `src/Core/HoloceneCones.cs` likewise.
- **Assessment:** they are data, not hand code, and their size says nothing about tangles: the hand-written part is
  `Geothermal.cs` (145 lines) and `Volcanoes.cs`. Compiled in, they cost DLL size and build time, every data refresh is a
  code diff, and the ODbL list sits inside the assembly. A small binary (`float32` pairs and a kind byte, 5,234 OSM
  springs is about 47 KB) in `assets/earth/config` or the data store would do, loaded once in the static constructor's
  place.
- **Risk:** generation is unchanged only if the same float values come back bit for bit (they are `float` literals
  today); the store route adds a file every server must have (a failure path), the asset route does not.

### W13. Ore generations still live as flags in one generator (CR-A6 holds, and grew)
- **Severity:** low (structure). **VERIFIED.**
- **Where:** 48 tests of `RulesV4`, `Lens`, `anyRock`, `layers == null`, `Layers != null`, `BodiesAfterStrata`,
  `BodiesAfterGround`, `Rules >=` across `src/Ore*.cs`, `src/EarthOre*.cs`, `src/Core/OreDeposits.cs`.
  `EarthOreGenerator` (602 lines) is at once rules v3 ("fill any rock", `Lens`), v3 with ore layers (`LayerGate`),
  v4 (`AttemptScale`, `Evidence`) and the prospecting model of each; v5 added `ValleySearchRules` in `OreDepositPlan`.
  "Ore rules", "ore layers", "deposit set" and the heat maps' "fix level" are four separate numbers.
- **Fix:** as CR-A6: one strategy object per session, chosen at bind (v3, v3 with layers, v4 and later), so the hot
  loops hold no version tests. Behaviour-preserving, but a large move; do it when rules v6 is next on the table.
- **Risk:** none to generation if done as a pure move; high regression surface without the offline ore tests.

### W14. Duplicated logic worth one copy (low, VERIFIED)
- "rock-" block tables are built four times with slightly different rules: `WorldGenBlocks` (`RockKind`, `SandOf`,
  `GravelOf`), `EarthGeology.BuildBlockTables` (`isRock`, `sandOf`, `gravelOf`), `EarthVolcanoes.InitMaterials`
  (`rockKind`, `oreIn`), `EarthGround.TryCreate` (`natural`), plus `OreBlockNames`.
- The chunk key `(long)cx << 32 | (uint)cz` is written out in `TerrainHandoff`, `EarthOreLayers`, `EarthVolcanoes`,
  `OreSession` (with `^`), `OreDepositPlan`, `StreamSprings`.
- `SmoothStep` exists in `Volcanoes.cs`, `VolcanoInterior.cs` and `Geo`.
- The physical-slope arithmetic is written three times in `AlpineSurfacePass.Column` (`:184-186`, `:189-191`, `:205-208`).
- One file name does not match its class: `src/OreBauxiteStrata.cs` holds `BauxiteStrata`.

---

## D. Assets: patches and recipes (are they gated to Earth worlds?)

- **`recipes/grid/refractory.json`: gated, correctly** (decision 0077; see "Checked and found sound").
- **Not gated, by design or by omission. VERIFIED** (CR-A13 still holds):
  `patches/ground-rocks-*.json` and `ground-dolostone-*.json` (nine files, about 170 KB: rock variants, ore variants,
  deposit hosts, recipes) carry only `dependsOn` other mods, so every world on a machine with Earth gets Earth's gneiss,
  schist, quartzite, gypsum and dolostone blocks and their ore variants; they never generate outside an Earth world
  because vanilla's strata do not name them. `patches/fish-rivers.json` adds `water-*-7` and `rapidwater-*-7` to
  freshwater fish in every world. `recipes/grid/ore-nuggets.json` and `recipes/barrel/mortar-tephra.json` load in every
  world; their inputs exist only where Earth placed them. JSON patches cannot be per world, so the choice is to
  document it (README: "installed, Earth adds these blocks to every world") or to move what can move into code.
- **Risk of gating in code:** block ids in saved worlds depend on the block list; removing block variants per world is
  not possible. Leave the block patches; only recipes and spawn conditions could be gated.

---

## E. Memory

- Layer tables: up to about 63 MB (W10, CR-O7).
- Volcano index at fine scales (W1): each composite body is listed in every 512-block cell within 20 km (about 290
  cells at 1:5), each shield within 40 km (about 1,090): some hundreds of thousands of list entries, a few MB.
  Plans are kept for the session (a `double[]` profile of up to 161 values each); fine.
- `TerrainChunkWriter.extCache`, `columnsCache` and `EarthWorldGen.queryScratch` are `ThreadLocal`s made anew at every
  `InitWorldGen` (`/wgen regen`) and never disposed (`TerrainChunkWriter.cs:25`, `:30`; `EarthWorldGen.cs:484`); each
  value is a 72 x 72 array of the roughly 35-field `Column`. Freed when the old writer is collected; `Dispose` them.
- `EarthSprings.plans` clears at 200,000 entries; `EarthVolcanoes.mapRetryAt` is not cleared at `Init`. Both small.
- Ground tiles: a 192 MB budget, held weakly across worlds (`GroundColumns.cs:51-57`). Good.

---

## F. Things that are good

1. **The terrain's fail-closed chain is complete and readable**: one wrapper (`MakeTerrain`), a persisted list of
   chunks to make again, a handoff that always leaves an entry so "made by Earth" is checked per chunk, and a guard for
   the seconds after a refusal. It is the model W3 asks the other passes to follow.
2. **Saved worlds are respected in the details**: deposits are removed from vanilla's list rather than set to 0 tries
   (vanilla draws per variant even at 0), Earth's adapters are appended after the list so vanilla's random stream does
   not move, each ore-layer version is its own file, and `HostRoll` is "drawn last: the earlier draws are those of rules v2".
3. **Cross-world hygiene**: error latches are per session, per-thread caches are keyed by numbers, static instances are
   released in `Dispose` with the reason written down (`EarthVolcanoes.Dispose`), ground tiles are weak.
4. **Cheap exits where they matter**: `BauxiteStrata.Replace` asks each chunk's palette first and skips whole chunks;
   `ChunkColumn.Resolve` answers `Allows` then `Place` from one lookup; the layer table is fetched once per deposit, at
   the first block that asks; `ChunkSettler` is the one routine for the writer and the river query.
5. **`MapPlan` and `/earth volcano` already refuse a plan built on built-in heights**; W1 is that same rule applied to
   the generation path.
