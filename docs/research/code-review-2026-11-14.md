# Earth code review, 14 November 2026

A fresh look at Earth's code after the rebuild since the 11 November review
([code-review-2026-11-11.md](code-review-2026-11-11.md), fixed and merged 12 November as `7145eb2`). Since then: ground
rules 2 (0076), ore layers v2-v4 and ore rules v5, crafting rules 1 (0077), volcano interiors (0078), map calls (0079),
weather rules 6 and 7, climate fix v3 and v4, GenFixRules 2 and 3, channel rules 4 and 5, river mouths (0071), works
rules 3 and 4, shore rules 3, the model cache and the multi-core build. Asked for by the author on 14 November: structure,
improvements, cleanness, performance and maintainability.

Sections 1-10 are the first pass (Opus 5.5 leading). **Section 11 is a second, independent pass on Fable 5.2** over
`main` after the fixes (`fef6166` and the merges up to weather rules 10): it re-checks the fixes for ER-01 to ER-30 and
adds findings FP-01 to FP-30 (8 high, 22 medium) with 43 low and 22 refactor notes.

## 1. How this was done, and how far to trust it

- **Read-only.** Nothing in the code was changed. Read at `6371e32` (2.0.0-rc.13). `main` moved to `b70ebbf` during the
  review (site drivers 0080, packed config JSON 0081). In the areas reviewed only `src/EarthCommands.cs` (+97 lines, so its
  line numbers below are given at `b70ebbf`), `src/EarthWaterTable.cs` (+20) and the package (0081) changed. Weather
  rules 8 and the habitat steps were in progress in other sessions and are not covered.
- **Five area reviewers** (Fable 5.2) each read one area end to end against the decompiled 1.22.7 engine
  (`devtools\vs-ref`). Their full reports, with every file:line, quote and fix, are in
  [code-review-2026-11-14/](code-review-2026-11-14/):
  [lifecycle, params, patches](code-review-2026-11-14/lifecycle-params-patches.md) (F),
  [terrain and water](code-review-2026-11-14/terrain-water.md) (T, P, S),
  [world generation and underground](code-review-2026-11-14/worldgen-underground.md) (W),
  [weather, climate, ecology](code-review-2026-11-14/weather-climate-ecology.md) (W-),
  [data, package, tests, docs](code-review-2026-11-14/data-package-tests-docs.md) (A, B, C, D). The letters in brackets
  below point into those reports.
- **The lead reviewer (Opus 5.5)** re-read the code behind the top findings: ER-01 (`TerrainModel.Rivers.cs:514-546`
  with `Glo90Decides`), ER-03 (`EarthWeather.cs:587-609`), ER-06 (`EarthVolcanoes.cs:129-140` against `:285-294`), ER-07
  (`EarthMapSystem.cs:405-422`, `WorldUpgrades.cs:360-375`), ER-02 (the tile decoder `TiledGrid.cs:79-83`).
- **Measured:** the zip's entries (compressed sizes, below); `EarthMapTests <root> 384 cost` at `6371e32` (section 6); the
  package re-encodings in ER-02 (Python on copies, the round trip asserted). **Not measured:** every other cost figure is
  an estimate from counting operations. Nothing was looked at in game.
- **Marks:** VERIFIED = the code path was read end to end; PLAUSIBLE = one link (a trigger, a cost, a visible effect) is
  argued, not shown.

## 2. Summary

Earth is in good shape for a mod of 61,000 lines that changes this fast. The fail-closed start-up and terrain chain from
the last review holds (no single fault lets vanilla terrain into an Earth world), saved worlds are protected in detail
(protobuf fields never reused, random streams kept, rules frozen per world), and the core (`src/Core`) has no game
types, so almost everything can be tested offline. Every fix of 12 November that was checked still holds (section 7).

The weaknesses are of four kinds:

1. **Answers kept for the session that depend on which thread asked, or on whether a tile had arrived.** The
   11 November review fixed this for the river query and map plans (`MissedReads`, `BuiltInOnly`). Three places still
   lack the guard: the per-reach "in the mask" flag (ER-01, high), volcano plans on the generation path and ore rules
   v5's valley search (ER-06). All three can make chunks of the same world differ between sessions.
2. **Per-frame and per-column cost that nobody measures.** `/earth cost` and `EarthMapTests cost` time the climate hook
   and inland columns only. The client works out the fog four times a frame and the wind about five times per wind
   query (ER-03, ER-04); sea, river-mouth and coast columns repeat the same lookups up to five times (ER-11).
3. **Rules versions as raw numbers.** About 65 saved rules fields are tested at about 300 places, rules 6 of the
   weather as the bare literal `6` nine times. Rules 8 is being built now. A rules record per family, written with a
   table test first, would make "older worlds bit for bit" checkable by reading one function (ER-10).
4. **Room in the zip.** 313 kB of headroom after 0081. A spatial predictor in front of brotli in the seven climate files
   frees about 10 MB losslessly, with no rules version (ER-02).

## 3. Findings ranked

IDs are this review's (ER = Earth review, 14 November). Rules: "none" = a fix changes nothing a saved world generates;
"version" = needs a new rules version (or the author's acceptance as an RC fix).

| ID | Sev. | Finding | Where | Rules | Report |
|---|---|---|---|---|---|
| ER-01 | high | Per-reach "in the mask" flag is computed on a built-in-heights thread and kept for every thread | `src/Core/TerrainModel.Rivers.cs:514-546` | none | T-1 |
| ER-02 | high (size) | Climate files are brotli of unpredicted bytes: about 10.1 MB of 40 MB can go losslessly | `src/Core/TiledGrid.cs:79-83`, `tools/bake_rain.py:213` | none | B1 |
| ER-03 | high (perf) | Client works out Earth's fog four times every frame (rules 7) | `src/EarthWeather.cs:587-609` | none | W-1 |
| ER-04 | high (perf) | Every `GetWindSpeedAt` runs vanilla's handler (5 winds, 2 rains, the climate hook), then Earth overwrites it (CR-W1) | `src/EarthWeather.cs:98`, `:699-737` | none | W-2 |
| ER-05 | medium | A world opened by an older build runs that build's older rules, mostly silently | `src/EarthParams.cs:583`, `:596`; `src/EarthMapSystem.cs:601-612` | none | F1 |
| ER-06 | medium | Volcano plans (generation path) and ore v5 valley search are built from far heights per session, kept even when a tile was missing | `src/EarthVolcanoes.cs:285-294`; `src/Core/OreDeposits.cs:563`, `:686-796` | none | W1, W2 |
| ER-07 | medium | One-shot `UpgradeExistingWorld*` flags rewrite a world created in this start, over the Earth tab | `src/EarthMapSystem.cs:416-422`; `src/WorldUpgrades.cs:360-375` | none | F2 |
| ER-08 | medium | Strata, ground and ore passes fail open per chunk and go silent after three lines | `src/EarthGeology.cs:505-519`, `:580-593` | none | W3 |
| ER-09 | medium | `/earth checkrivers` and the self-test audit walk a network without the flow file's reroutes | `src/EarthCommands.cs:598` (b70ebbf); `src/EarthParams.cs:645-646` | none | F3 |
| ER-10 | medium (structure) | Rules versions: ~300 raw tests; no rules record per family | e.g. `src/Core/TerrainModel.Rivers.cs` (24), `src/Core/WeatherModel.cs:225-823` | none | F4, S-2, W-18, W13 |
| ER-11 | medium (perf) | Sea, mouth and coast columns: `ApplyMouth` does full work on every sea column; the same lookups 2-5 times per column; three whole-bucket river scans (CR-R2) | `src/Core/TerrainModel.Rivers.cs:306-345`; `src/Core/RiverNetwork.cs:944`, `:1006`, `:1082` | none | P-1-P-3 |
| ER-12 | medium (perf) | Weather sampling: storm intensity before the distance reject; the anomaly repeats `SteeringBlocks`; region winds and cloud tiles recomputed per asker | `src/Core/WeatherStorms.cs:320-326`; `src/Core/WeatherModel.cs:767`; `src/EarthWeather.cs:617-629`, `:724-737` | none | W-3-W-7 |
| ER-13 | medium (memory) | ~75 MB of climate planes for a fix-4 world (25 MB read), on clients too; terrain tiles as floats (CR-T8, ~40 MB); ore layer tables up to ~63 MB (CR-O7) | `src/Core/EarthDataset.cs:78-105`, `:619-627`; `src/EarthOreLayers.cs:297` | none | A1, A2, W10 |
| ER-14 | medium | Tests: no runner, no shared harness, 26 projects compile all of Core; 49 source files have no test; the cost benchmark misses sea, mouths and rules 7 | `tests\*`; `tests/EarthMapTests/Program.cs:906-945` | none | C1, C2, P-7 |
| ER-15 | medium (structure) | God classes unsplit: `EarthMapSystem` (1,172), `EarthParams` builds the model, `TerrainModel` (3,477 in 11 partials), `EarthWeather` (1,125); a new world's params still have two writers (CR-A4, A5, A3) | see reports | none | F7, S-1, W-19 |
| ER-16 | medium | First-start downloads (289 MB store, 172 MB ground) cannot be cancelled and block the starting thread | `src/EarthMapSystem.cs:313`; `src/EarthGeology.cs:252`; `src/Core/DemDownloader.cs:134-158` | none | A3 |
| ER-17 | medium | `NightHours` uses `Math.Tan`/`Math.Acos`, the one leak from `PortableMath` in the weather | `src/Core/WeatherModel.cs:627-634` | none (fog drawn only) | W-13 |
| ER-18 | medium | Systems still learn the params by polling (CR-A10), now six of them; vanilla weather until the first watch, a second shader reload | `src/EarthWeather.cs:92`, `:205`; `src/EarthThunder.cs:78`; `src/SnowMeltLight.cs:52` | none | F10, W-14 |
| ER-19 | low-medium | Rules 3 conduit lava is not capped by the column's own top (wet vents on falling ground) | `src/Core/VolcanoInterior.cs:431`; `src/EarthVolcanoes.cs:534-541` | version | W4 |
| ER-20 | low | `RegionalRock` knows only the top 512 blocks of a ground column | `src/EarthGeology.cs:829`, `:895` | version | W5 |
| ER-21 | low | A deposit plan that throws half way stays "complete" with another deposit's values | `src/EarthOreGenerator.cs:339-367` | none | W6 |
| ER-22 | low | Statics that outlive a world: `SnowMeltLight.Paused`, `TallGrassPatched`, two thread-statics holding the model, six process-lifetime patch flags | `src/SnowMeltLight.cs:33`; `src/BlockLayerAdjustments.cs:22`; `src/Core/TerrainModel.Coasts.cs:70`; `src/Core/TerrainModel.NaturalColumn.cs:141` | none | F8, T-3 |
| ER-23 | low | Not inert in vanilla worlds: disabled params saved into every save; `boattest`, `elevation download all` (8.6 GB), `cost`, `snowlight` work anywhere (CR-A13) | `src/EarthMapSystem.cs:521-522`; `src/EarthCommands.cs` | none | F9, D |
| ER-24 | low | `EarthParams` header says "next free 196" (196-203 taken); no pinned field-table test; ground rules 2 meant something else in the rc.1 commit | `src/EarthParams.cs:10-12`; `src/Core/GroundColumns.cs:374` | none | F5, F6 |
| ER-25 | low (perf) | Raw uncapped `Parallel.ForEach` in four builders (CR-R6) | `src/Core/RiverStraighten.cs:61`, `RiverPoolPlanner.cs:315`, `RiverSpatialIndexBuilder.cs:23`, `AridHydrology.cs:27` | none | T §3 |
| ER-26 | low | Data loading leftovers: three copies of the fetch protocol (CR-A11); GLO-90 manifest not pinned; unsized `MemoryStream`; the store hashed and read twice per start | `src/Core/EarthDataStore.cs:137-171`; `DemDownloader.cs:348-387`; `StreamHeads.cs:41-57` | none | A4-A10 |
| ER-27 | low | Dead code and generated data in C#: 7 unused methods, test-only members in `RiverNetwork`, 269 KB of generated geothermal literals | `src/Core/CoarseElevation.cs:60`, `:383`, `:515`, `:604`; `src/Core/GeothermalSites*.cs` | none | T §5, W12 |
| ER-28 | low | Docs drift: decision index lacks 0056 and 0058; 0017 says thunder and hail are vanilla's; 28 comments say "0053 (proposed)"; stale comments on dam rules, outline literals, "Experimental" | `docs/decisions/README.md`; `docs/decisions/0017-weather.md:133`; `src/EarthWorldOptions.cs:159-166` | none | D1-D3, F11, W §4 |
| ER-29 | low | Build: first `10.*` runtime folder by name; tests on .NET 8, game on .NET 10; zip not reproducible (timestamps, a worktree path) | `EarthMap.csproj:31`; `tools/package-earth.py:28-30`, `:79` | none | B3, B4 |
| ER-30 | low | `/earth tp` and `/earth info` read built-in heights where a tile has not arrived; `cw.SpawnX` read outside the Cylinder World guard | `src/EarthCommands.cs`; `src/EarthMapSystem.cs:869` | none | F12, F13 |

## 4. The top findings in brief

### ER-01 — A reach's "in the mask" flag is kept from whichever thread asked first (high, VERIFIED)

`ReachInMaskRiver` (`src/Core/TerrainModel.Rivers.cs:514-546`) decides, once per reach for the whole model, whether a
third of the reach's points lie in the water mask; channel rules 1 then keep that reach wet where the arid rules would
dry it. Each point is skipped unless `RiverOutlinesDecide` → `Glo90Decides` (`TerrainModel.NaturalColumn.cs:389-394`),
which returns false on a thread with `FineElevation.BuiltInOnly` and for a square whose tile is not on the machine. On
such a thread every point is skipped, `inside` stays 0, and `Volatile.Write(ref known[reach], 1)` stores "dry" for good.

`StoryPlacer` sets `BuiltInOnly` and calls `GetColumn` thousands of times before the first chunk of a new world
(`src/StoryPlacer.cs:100-102`, `:336-349`); `Png.Render` and World Map's sweeps can too. Generation threads then read the
stored "dry" and dry a reach the mask keeps wet: the dry bed beside wet water the rule exists to prevent. The story search
runs only at a world's first start, so the same reach can be dry in chunks of the first session and wet later.

**Fix:** never store an answer worked out without GLO-90: return false without writing when `BuiltInOnly`, and when a
point's square is not decided yet (`!Fine.SourceHas`) return without storing, as `EarthVolcanoes.MapPlan` does with
`MissedReads` (`src/EarthVolcanoes.cs:129-140`). Better: decide every reach once at start-up while the reaches are kept.
Put a comment on `ReachSpeed` and `PondProfileOf` (same lazy pattern, safe today only because they read no per-thread
switch). **Risk:** none for chunks made by generation threads that asked first; it removes the order dependence. Worlds
whose first session stored a wrong flag keep those chunks.

### ER-02 — About 10 MB of the zip can go, losslessly (high for size, VERIFIED by measurement)

The seven client climate files are brotli of raw planes (`TiledGrid.Decode`; `bake_rain.py`, `bake_aridity.py`;
`EarthDataset` for `climate.bin` and the fixes; `WeatherData` for `weather.bin`). GLO-90 tiles already put a MED
predictor in front of the compressor (`src/Core/DemTiles.cs:262-275`); the climate files do not.

| File | Now (bytes in zip) | Re-encoded | Saved | How |
|---|---:|---:|---:|---|
| `rain.bin` | 11,862,215 | 8,868,884 | 2,993,331 | best of three predictors per tile |
| `temperature.bin` | 8,006,059 | 5,400,888 | 2,605,171 | 0.5 °C steps, left residual, byte planes |
| `climate.bin` | 3,374,076 | 1,833,308 | 1,540,768 | MED; coldest and warmest as differences from the mean |
| `aridity.bin` | 6,468,128 | 5,412,623 | 1,055,505 | as rain |
| `climatefix2.bin` | 1,627,576 | 575,631 | 1,051,945 | cell bitmap, differences from `climate.bin` |
| `weather.bin` | 3,635,209 | ~2,881,000 | ~754,000 | MED per plane |
| `climatefix.bin` | 324,584 | 216,086 | 108,498 | as fix 2 |
| **Sum** | 35.30 MB | ~25.2 MB | **~10.1 MB** | |

The files travel only in the zip with the DLL that reads them, on both sides, and decode to the same values, so no rules
version is needed. Three things to carry along: `weather.bin`'s signature is FNV of its bytes, frozen into worlds and
pinned in `WeatherTests` (store the old signature in the new header, or leave that file); every world's model cache
rebuilds once (`DataIdentity`); the bakes must write the new form, and `package-earth.py` should refuse a file above its
expected size so a later bake cannot quietly bring the old sizes back. Smaller, separate options: leave `EarthMap.pdb`
out (244 kB, loses line numbers in crash logs: the author's call), `ore-heatmaps-v1.json` to the data store (372 kB,
server-only, needs an upload). Not worth it: JSON minifying (4 kB), PNG recompression (under 1 kB). `climatefix.bin`
cannot leave the zip (1.0 worlds' clients read it). Scripts and outputs: [scripts/](code-review-2026-11-14/scripts/).

### ER-03, ER-04 — Client weather cost every frame (high, VERIFIED paths, costs estimated)

- **Fog (new with rules 7).** `RegionFogPostfix` sits on `WeatherSimulationRegion.UpdateWeatherData`, which vanilla's
  render loop calls for the four regions round the player every frame. Each call is `FogAt`: three rain samples and a
  wind. About 12 rain samples a frame, 1.5-2 ms a second at 60 fps on the render thread, invisible to `/earth cost`.
  **Fix:** cache the fog per region for about 0.02 game hours (and per ground height and `Revision`). Client drawing
  only: no bits change.
- **Wind (CR-W1, grown).** Earth subscribes to `OnGetWindSpeed` after vanilla, so every `GetWindSpeedAt` first runs
  vanilla's handler (4 region winds through Earth's region postfix, a climate read that runs Earth's cloudness postfix
  and, since rules 6, the temperature anomaly) and then Earth's, which overwrites it. Called twice a frame, per weather
  particle, per body-temperature and cloth tick. **Fix:** a prefix (through `PatchVanilla`) on
  `WeatherSystemBase.Event_OnGetWindSpeed` that writes Earth's wind and skips vanilla's when Earth controls the wind;
  keep today's behaviour if the patch is refused. No bits change.

### ER-05 — An older build opening a newer world runs its older rules, mostly silently (medium, VERIFIED)

Some saved rules are clamped to the build's newest (`EarthParams.cs:583` DetailRules, `:596` GenFixRules, CoarseRules,
WorksRules, `WeatherModelRulesOrOld`), others pass raw into `>=` tests, and `AnnounceSavedRules` warns for 13 fields.
An rc.13 world opened with rc.12 makes rc.12's chunks for good (GenFix 2, geothermal 2, ore layers 2). **Fix:** one
table of saved against newest for every rules field, checked in `InitWorld` before the data loads; refuse above newest
("made with a newer Earth"). Add record-only `CreatedWith`/`LastSavedWith` fields (next free numbers 204, 205). Nothing
changes for a world opened with the same or a newer build.

### ER-06 — Plans kept for the session from far heights (medium; trigger VERIFIED, seam PLAUSIBLE)

- **Volcanoes.** Rules 3's index lists a body in every 512-block cell within 20 km (shields 40 km). `PlansNear`
  forces the plan for every listed body before testing the real reach, and the plan reads up to 7,728 model columns that
  far out. If a square's wait runs out (2 minutes, then built-in heights for the session) the plan is kept; `MapPlan`
  and `/earth volcano` refuse exactly that plan (`MissedReads`). The next session plans again from the real square, so
  vent, rim, cone base and layer profile can differ between chunks of two sessions.
- **Ore rules v5's valley search** (up to about 70,000 points over 30 km) runs lazily inside `Ensure`, under a lock, on
  whichever thread asks first: the deposit pass, the prospecting pick or `/earth ores` (main thread, a 50 km square of
  bodies one after another). Same session-only result.
- **Fix:** do not keep a generation plan whose build counted `MissedReads` (as `MapPlan`); better, save each volcano's
  plan numbers and each body's move in the save at first use. Keep `/earth ores` and the pick from starting a search.
  Cheap first step: test the vent's distance against `MaxReach` before forcing the `Lazy`. No rules version.

### ER-07 — One-shot upgrade flags are applied to a brand-new world (medium, VERIFIED)

`InitWorld` runs `upgrades.Layout`, `.Terrain` and `.Regional` for every enabled world without looking at `fresh`
(`src/EarthMapSystem.cs:416-422`). A flag left `true` in `earth.json` therefore overwrites the tab's choices right after
`Freeze`: with `UpgradeExistingWorldOreRules`, "ores vanilla" becomes Earth's ore maps (`Config.RegionalOres` defaults to
true, `WorldUpgrades.cs:362`). The one-shots also leave dependent fields stale (`CraftingRules` after the ore one-shot,
`PlacementRules` after fauna). **Fix:** skip the three tables when `fresh` (log the flags left set); derive dependent
fields in the same function `Freeze` uses. Nine of the 18 flags can only act on pre-1.0 worlds (see ER-10's floor).

### ER-08 to ER-10

- **ER-08:** the strata, ground and ore passes catch a throw, log "vanilla blocks are kept there" three times per session
  and go on; the chunk is saved half-Earth. CR-G2 closed this for terrain only. Use the terrain's path:
  `EarthWorldGen.NoteFailedChunk` (regenerated at the next start) and a refusal past a small count.
- **ER-09:** `WriteRiverCheck` calls the four-argument `RiverSource`, which passes `flow: null`, while the model was built
  with the flow's rerouted mouths in every `FlowRules >= 1` world. The audit (also in the self-test) can report false
  breaks at those mouths and miss real ones. Add `EarthElevationSystem.Flow` and one `RiverSourceAsBuilt` used by both.
- **ER-10:** see section 5.

## 5. Rules versions: what can be consolidated without touching a saved world

The full table (every rules field with its proto number, newest value, new-world value, 1.0.2 value, values each RC
could save, and where it is branched) is in [lifecycle-params-patches.md §2](code-review-2026-11-14/lifecycle-params-patches.md).
Field-number hygiene is sound: 131 members, no duplicates, nothing renumbered or retyped between 1.0.2, rc.1, rc.12 and
`main`; no field where "0 = old save" collides with a new-world value.

**Dead arms, in two groups.**

- *Dev-only values* (no released or RC build could save them): `FineElevation` 1-4 (`TerrainModel.LandHeights.cs:54-55`,
  `:81`, `:172`), `RiverCourses` 1-3 (with them the store files `rivers-glo90-snap.bin` and `-arms.bin`, read by no world
  yet required in place), `DamRules` 1, `RiverOutlines` 1-2, `SeaReferee` 1, `ShoreRules` 1, `ShoreZoneRules` 1,
  `SoilRules` 1-2, `ZoneRules` 1, `FireRules` 1, `GeothermalRules` 1, `OreLayers` 3. These arms can go after one check
  (ER-24: whether a world from the `eb322e4` rc.1 zip exists).
- *Pre-1.0 values* (0.12-0.16 worlds, which still load): `RiverPoolLayout` 0-9 (about 25 of 35 tests),
  `GutterSeabed` 0-1, `ClimateRules` 0-1, `Summits` 1-2, `Islands` 1-2, and nine of the 18 `UpgradeExistingWorld*`
  flags. These stay until the author declares a support floor.
- *Must stay:* every value a 1.0.x or RC world has (listed in the report), including channel rules 0 (new built-in
  heights worlds), pool layouts 10 and 11, `FineRules` 0 and 5, GenFix 0-3, Works 1-4, weather 5-7.

**Proposal.** One immutable rules record made once per start, with named facts per family (`Rivers`, `Channels`,
`Shore`, `Heights`, `Climate`, `Ground`, `Ores`, `Biomes`, `Weather`, `Fixes`), as `RiverPoolRules` and `SettleRules`
already are. Order: (1) a table test over every saved combination that exists (1.0.2, each RC, the tab's options),
written from today's code before any call site moves; (2) move call sites family by family, the offline digests
byte-equal after each; (3) one `Newest` list that `Freeze` and ER-05's refusal read; (4) a declared floor, and only then
delete the pre-1.0 arms. **Do the weather's family first** (`WeatherRules` struct: `ColdSeaDecks`, `Hail`,
`TemperatureAnomaly` for 6; `Ground`, `DryThunder`, `Fog`, `CloudTiles` for 7), before rules 8 adds a third layer of
literals; `WeatherContractTests hash` is its check. Then `TerrainRules` (24 tests in `TerrainModel.Rivers.cs` alone).
The ore generator's v3 / v3-with-layers / v4 / v5 flags (48 tests) become a per-session strategy object when rules v6 is
next.

**Adopted rules.** `TreeRulesVersion`, `FaunaRulesVersion`, `OreRulesVersion` and `PlacementRules` move up at every start
without a flag (`WorldUpgrades.cs:330-345`). They are today the only way generation in a saved world changes without the
admin asking. Keep that on purpose and write it down, or freeze them.

## 6. Performance: measured and estimated

`EarthMapTests "<root>" 384 cost` at `6371e32` (Release, .NET 8, this PC, warm caches, a 64 × 64 patch cycled):

| Call | Zermatt | Riga | Amazon | Sahara | Siberia | Alloc |
|---|---:|---:|---:|---:|---:|---:|
| `ClimateCorrection` (the hook) | 0.140 µs | 0.076 | 0.070 | 0.079 | 0.066 | 0 B |
| `ClimatePixel` (world gen) | 0.238 | 1.626 | 0.264 | 0.292 | 0.486 | 0 B |
| `GetColumn` | 1.518 | 2.717 | 2.028 | 0.625 | 1.270 | 0 B |
| `TemperatureAnomaly` (rules 6) | 3.692 | 3.222 | 2.793 | 3.142 | 2.811 | 32 B |
| same block and hour again | 2.485 | 3.293 | 3.309 | 2.571 | 2.339 | 32 B |

Data loaded in 1.3 s; 829,355 river segments indexed in 7.0 s. What this shows: the per-column terrain is cheap where it
is measured; the weather's anomaly is the dearest call and a repeat costs as much as the first (nothing is memoised).
What it does not show (ER-14): sea, mouth, wide-river and coast columns (ER-11), the fog, wind and cloud tiles (ER-03,
ER-04, ER-12), weather rules 7 (the benchmark builds rules 6), a 1:10 geometry. Extend it before optimising, and make
`/earth cost` also time the cloudness postfix (W-6.5), which today is outside the 4-8 ms per second of EA-0070.

Bit-identical wins, in order of value (each keeps saved worlds' weather and terrain bit for bit):

1. ER-04 skip vanilla's wind handler; ER-03 fog cache (client).
2. ER-12: storm intensity after the latitude reject; a per-thread memo of storms alive at the exact weather hour (the
   snow catch-up's 8 corners × 480 hours then hit for 7 of 8 corners); hand `vx, vz` back from `Rain` instead of
   `SteeringBlocks` again (`WeatherModel.cs:767`); region winds per tick; cloud tiles per 0.02 game hours.
3. ER-11: early return in `ApplyMouth` when no mask water and no river bucket is in reach (and no closure per column);
   a `ColumnWork` struct carrying `WorksAt`, coast distance, lake and river outline hits, `Glo90Decides` and the terrain
   sample down the pipeline (this also removes the two thread-statics of ER-22); one per-thread wide-candidate cache for
   `NearestCourse`, `MaxNear` and `ReachBlend` that keeps the scan order (prove with the 0074 probe set).
4. ER-25 `ParallelWork.For` in the four builders; CR-O6/O7/G7/G8 (still as they were: plan before the bounds reject,
   `CodeOf` not memoised, a one-point lattice per water-table query, `ToArray`/iterators per chunk).

With rules 8 (a version, so the author's call): read the anomaly at the block centre (`pos + 0.5`) as vanilla's rain
does; a one-entry memo then serves both rain samples of every live climate reading, about half of the weather's cost.

## 7. The 11 November findings: what still holds

| Finding | Now |
|---|---|
| CR-L1-L6, CR-G2, CR-O1, CR-W10, CR-W11, CR-A9 (grass lookups) | still fixed (CR-L1 re-checked against `ModLoader.TryRunModPhase` 1.22.7) |
| CR-D1/G1, CR-T1 | fixed for the paths named then; the same class remains in ER-01 and ER-06 |
| CR-W1 | holds, dearer since rules 6 (ER-04) |
| CR-W2 | holds (ER-12) |
| CR-W3, CR-E6, CR-E7, CR-E8 | hold; low (EA-0070 judged CR-W3 not worth a memo) |
| CR-R2 | holds and grew to three scans (ER-11) |
| CR-R6 | holds (ER-25) |
| CR-R10, CR-T6 | partly (depth and stream size per hit; `PeakCatalog.Near` once per column but still 9-51 probes) |
| CR-T7 | holds; two more caches since (speed only) |
| CR-T8 | holds (ER-13) |
| CR-D4 | fixed for the store; four unsized `MemoryStream` left (ER-26) |
| CR-O6, CR-O7, CR-G7, CR-G8 | hold unchanged |
| CR-A3, A4, A5 | hold (ER-15) |
| CR-A6 | holds and grew (ER-10) |
| CR-A8 | partly: one region index and box type shared; still five rule types, three error policies |
| CR-A9 (process-lifetime patches) | holds; six flags now (`SnowMeltLight` new) |
| CR-A10 | holds; six pollers (ER-18) |
| CR-A11 | half done: one transport, three copies of the protocol (ER-26) |
| CR-A12 | holds (three outline copies) |
| CR-A13 | holds in part: recipes now gated by `CraftingRules` (ER-23) |
| CR-A14 | holds (`WeatherContract.Version` with no compatibility rule) |
| CR-G6, CR-T9, CR-T10, CR-R3, CR-R5, CR-R8, CR-R9, CR-R12, CR-O5, CR-W7, CR-E5 | left as decided on 12 November (each needs a rules version or a decision) |

## 8. What is good

- **Fail-closed, end to end.** `ExpectsEarth`, `EarthWorldGen.Started`, `Refuse` in both systems, the `earth-creating`
  marker, `MakeTerrain` with a persisted list of chunks to make again, a handoff that always leaves an entry. No
  single-fault path lets vanilla terrain into an Earth world. ER-08 asks the other passes to follow it.
- **Saved worlds respected in the details.** Protobuf numbers never reused; deposits removed from vanilla's list rather
  than zeroed so vanilla's random stream does not move; `HostRoll` drawn last; each ore-layer version its own file;
  rules frozen per world, the weather in field 201 with `PortableMath` and a rules-5 hash test.
- **The model cache.** Keyed by hashing the whole params minus what is set after the build, so a new rules field is in
  the key by default; self-checking file, atomic write, never needed for a start.
- **`PatchVanilla`.** Every member resolved and signature-checked before anything is patched; features all-or-nothing with
  undo and one log line naming what vanilla does instead. `VanillaWindVector` checks the IL before it changes it.
- **Determinism in the river index and `ParallelWork`.** Candidate and summation order fixed and argued, per-item result
  slots joined in item order; this is what makes the caches proposed above safe.
- **A testable core.** `src/Core` has no game types; the climate hook was measured before anything was optimised.
- **Downloads.** One transport with `.part`, resume, size and SHA-256 check, atomic rename and per-read timeouts.

## 9. Plan (proposal; the author decides)

| Step | Findings | Why now | Size |
|---|---|---|---|
| 1 | ER-01, ER-06 (guard part), ER-21 | Chunks that differ between sessions are permanent; each fix is a few lines and needs no rules version | small |
| 2 | ER-07, ER-09, ER-22 (resets), ER-30 | One-line to few-line fixes in start-up and diagnostics | small |
| 3 | ER-10 weather part (`WeatherRules`), ER-17 | Before weather rules 8 lands; the portable `NightHours` before anything on the server reads `FogAt` | small-medium |
| 4 | ER-03, ER-04, ER-12 (bit-identical memos), with the benchmark extended first (ER-14 part) | The largest per-frame costs; checked by the existing hash tests | medium |
| 5 | ER-02 | 10 MB of room before 2.0 is published: every later asset or data change needs it | medium (decoders in five readers, bakes, a round-trip test) |
| 6 | ER-05, ER-24 | A refusal for downgrades and a pinned field table before 2.0 worlds exist in players' hands | small |
| 7 | ER-08, ER-16, ER-18 | One fail-closed policy for every pass; cancellable first-start downloads; params event instead of polls | medium |
| 8 | ER-11, ER-13, ER-25 | Generation speed and memory; bit-identity proven on the 0074 probe set | medium |
| 9 | ER-10 terrain and ore parts, ER-15, ER-14 (runner, shared harness) | Maintainability; behaviour-preserving moves behind table tests | large |
| 10 | ER-19, ER-20 | Generation fixes: the next geothermal or ore rules version | small, versioned |
| any time | ER-23, ER-26-ER-29 | Tidiness, docs, build reproducibility | small |

**Decisions for the author:** (a) a support floor: do 2.0 builds still have to open 0.12-0.16 worlds? If not, the
pre-1.0 arms and nine one-shot flags go; (b) did a zip built from `eb322e4` (rc.1) ever make a world (ER-24)? (c) keep
or freeze the four adopted rules (section 5); (d) ship `EarthMap.pdb` or not; (e) move `ore-heatmaps-v1.json` to the
data store (an upload); (f) read the weather anomaly at the block centre from rules 8.

**Backlog items** (suite backlog, status new): ER-01 EA-0076, ER-02 EA-0077, ER-03/04 EA-0078, ER-05/24 EA-0079,
ER-06 EA-0080, ER-07 EA-0081, ER-08 EA-0082, ER-09 EA-0083, ER-10 EA-0084, ER-11/25 EA-0085, ER-12/17 EA-0086, ER-13
EA-0087, ER-14 EA-0088, ER-15 EA-0089, ER-16/18 EA-0090, ER-19-23 and ER-30 EA-0091, ER-26-29 EA-0092.

## 10. What this review did not cover

Not read line by line: `RiverPoolPlanner`, `RiverSettling`, `RiverCoursePreparer`, `RiverDeltas`, `RiverEnds`,
`WaterOverlays`, `TerrainOverlays`, `DamRemoval`, `EarthLayout`, `StoryTerrain`, `TerrainExpansion`, `OreProspecting`,
`OreProviderBlocks`, `OreTotals`, `RegionalOreCatalog`, `RegionalGeologyCatalog`, `GeologyReference`,
`GeologyTransitions`, `Core/WaterTable`, the soil files, `StoneRecolour`, the bodies of most patch methods,
`SelfTest.cs` beyond its outline, the site drivers (0080, merged during the review). The test suites were not run (35
minutes; the review stayed read-only); only the `cost` benchmark was.

## 11. Fable pass

A second, independent full review, asked for by the author on 14 November: sections 1-10 and every re-review of the
fixes ran on Opus 5.5 (the model setting had been ignored), and this pass is Fable 5.2's own look. It does not take
sections 1-10 as given.

### 11.1 How it was done

- **Tree:** `main` at `fef6166`, pinned in a scratch worktree so that the reviewers read one tree while four sessions
  were merging. Line numbers below are at `fef6166`. What merged after it was reviewed as diffs by the lead: the
  TerrainRules, ore-facts and file-split refactors (`5a746ed`, `1cb98c3`, `2fbbfd7`, `2297c90`), the pick's valley
  search off the main thread (`5851a07`), the predicted climate files and memory (`4621f37`), the EarthWeather split
  (`6808dfa`) and weather rules 10 (`0cff689`).
- **Six reviewers (Fable 5.2), each one concern across the whole code**, not one subsystem each as in section 1, and
  told not to read sections 1-10: [concurrency and state that outlives its owner](code-review-2026-11-14/fable-pass/concurrency.md),
  [saved worlds and rules](code-review-2026-11-14/fable-pass/rules.md),
  [world-generation passes and patches](code-review-2026-11-14/fable-pass/worldgen.md),
  [terrain model and rivers](code-review-2026-11-14/fable-pass/terrain.md),
  [weather, climate, client, ecology](code-review-2026-11-14/fable-pass/weather.md),
  [data, commands, start-up, build, tests](code-review-2026-11-14/fable-pass/data.md). Their probes (sources and
  outputs) are in [fable-pass/probes/](code-review-2026-11-14/fable-pass/probes/).
- **The lead (Fable 5.2)** re-read ER-01, ER-05, ER-07 and ER-08's fixes, read the later merges, and verified in the
  code and the decompiled 1.22.7 engine every high finding and most medium ones before listing them (marked L below;
  P = shown by a probe or test, R = by reading only).
- **Run or measured in this pass:** the digest of every `TerrainModel.Column` field for v1.0.2, rc.8, rc.11 and rc.13
  worlds on each version's own code against `fef6166` (2.4 million columns: identical); weather rules 5-9 across call
  order, cold and warm caches, 16 threads (0 differing bits); 816,000 terrain columns at the seam, the poles, ±180° and
  3,000 places (no throw, no NaN); about 1,000 damaged inputs to the readers; a localhost listener for the downloader;
  a Release build twice (same SHA-256); `tests\Run-All.ps1 -Quick` (29 of 30 pass, see FP-19); `tests\ClimateDataTests`
  at `4621f37` (ALL OK); the per-column cost table in 11.6. **Not run:** the game. Nothing here was seen in game.

Severity: **critical** = loses or corrupts a world in normal play; **high** = permanent wrong chunks under a plausible
condition, or a defect or cost every player in the affected place or setting meets; **medium** = a real defect of
smaller scope, a contract that does not hold, or a cost worth fixing; **low**; **refactor** = structure, no defect.

**Counts: critical 0, high 8, medium 22, low 43, refactor 22.**

### 11.2 Do the fixes for ER-01 to ER-30 hold?

| Finding | Now | Evidence |
|---|---|---|
| ER-01 reach flag | holds | L: `ReachPointDecides` reads no per-thread switch; digest identical for generation-first answers |
| ER-02 climate files | fixed in `4621f37` | L, P: decoder read; `ClimateDataTests` run here: every value of the seven files equals the plain ones; zip 29.2 MB |
| ER-03, ER-04 client fog, wind | hold | weather reviewer: about 5 µs per client frame, 2 ms/s on the cloud thread |
| ER-05 newer worlds refused | **does not hold fully** | FP-04: a newer build that only adds a field is not refused, and the older build drops the field (probe) |
| ER-06 session plans | guard holds; rest open | FP-03 (session fallbacks), FP-12 (threads that may not fetch); the pick's search is off the main thread (`636137e`) |
| ER-07 one-shot flags | **not fixed** | FP-09: applied to the new world at its second start |
| ER-08 passes fail closed | holds for the passes named | gaps: FP-13 (handlers without a net, chunks in flight), FP-14 (no way back in old worlds), FP-02 (start-up) |
| ER-09 audit with the flow | holds | L |
| ER-10 rules records | weather, terrain and ore facts done | L: all 63 terrain rewrites and 8 ore tests read against their facts; pool layouts and the passes' own tests remain |
| ER-11, ER-25 sea columns, ParallelWork | hold | digest identical; open sea 1.3-1.4 µs a column. FP-08, FP-23, FP-24 are what is left |
| ER-12 weather memos | hold | FP-27 (snow catch-up) is what is left |
| ER-13 memory | fixed in `4621f37` for the climate and tiles | FP-18: `rivers.bin` and the referee's copies remain |
| ER-14 tests | runner exists | FP-19 (it replaces the release zip), FP-10 (no terrain digest) |
| ER-15 god classes | file splits done (no behaviour change, checked line for line) | two writers of a new world's params remain |
| ER-16 downloads cancellable | holds for the files | the spawn area's tile wait is not (low, C-L2) |
| ER-17 portable NightHours | holds | but rules 10 adds `Math.Tanh` (FP-29) |
| ER-18 ParamsReady | holds | weather reviewer: client calendar arrives before the params |
| ER-19 to ER-24, ER-26 to ER-30 | hold | L for 19-22, 24; data reviewer for 26-29 (manifest pinned, two builds one SHA-256) |

### 11.3 High

**FP-01 — Quitting while a chunk waits for a GLO-90 tile: Earth is disposed under the chunk thread, which goes on
generating without Earth** (L, P for the wait). `src/EarthWorldGen.cs:441-455` (`Dispose`: `patches?.Release()` puts
vanilla's GenTerra back, `UnpatchAll`, `Active` stays true), `src/PatchVanilla.cs:892-896`,
`src/EarthElevationSystem.cs:404-415`, `src/Core/DemDownloader.cs:99-123`; engine `ServerMain.cs:1629-1650`
(a `Suspend` that times out after 10 s is ignored), `:1754-1801` and the systems' order (`ServerSystemModHandler`
disposes the mods before `ServerSystemLoadAndSaveGame` saves, and only then are the threads told to exit).
With the default single generation thread, a tile wait longer than 10 s (offline, a slow link) still running at the
quit: the mods are disposed, the thread comes back, makes its chunk from the built-in heights with vanilla strata and
soil, and the next chunks at the terrain pass get vanilla terrain from the restored handler; the shutdown save keeps
every chunk past its terrain pass. Only the first vanilla-terrain chunk is noted for remaking. *Fix:* the generation
wait polls `sapi.Server.IsShuttingDown` and throws (the chunk noted, no refusal); `Dispose` sets `refused`, does not
put GenTerra back, and `BareRock` throws when refused. No rules version.

**FP-02 — Whole features fail open for a session: one log line, then vanilla blocks in an Earth world** (L for the
geology; R). Only the terrain refuses the world (`src/EarthWorldGen.cs:300-309`). Regional geology, the ground
columns and the ore layers turn themselves off ("new chunks use vanilla rock strata") on an unsupported rules
version, a strata hook that no longer binds after a game update, a competing mod, missing or invalid shipped data, or
any unexpected error (`src/EarthGeology.cs:86-99`, `:113-167`, `:218-222`, `:328-331`); likewise the ore layer
(`src/EarthOres.cs:75-80`, `:90-137`), every non-critical PatchVanilla feature (`src/PatchVanilla.cs:440-588`;
`Critical` only raises the log level, `:667-675`), the climate and ocean maps (`src/EarthMapLayers.cs:21-25`), plants,
placement, fauna and the per-tree picks (`src/EarthPlacement.cs:69-116`; one bad taxon drops the whole catalogue,
`src/Core/PlacementCatalogue.cs:125-132`), a volcano whose plan threw (`src/EarthVolcanoes.cs:307-321`), and story
placement, which is marked placed after a failure (`src/EarthWorldGen.cs:527-537`). The files inside the mod's zip
have no compiled hash, unlike the store's. A game patch release that breaks the strata hook therefore gives every
chunk made until the mod is updated vanilla rock under Earth's terrain, with a seam for good. *Fix:* one rule for a
world whose saved rules need a feature: it starts with the feature or it does not start (the refusal the terrain and
the ground sets already use), with the log saying what to remove or update. No rules version.

**FP-03 — A tile that cannot be fetched while the game runs gives its square the built-in heights for the session**
(L; by design since the 12 November fixes). `src/EarthElevationSystem.cs:378-398` (`OnFetchFailed`: "chunks generated
in that square in this session take the built-in heights and may meet later ones with a step"), `:404-416`, `:436`
(two minutes). Playing offline, or through a GitHub outage, with squares not yet downloaded: every chunk made there
that session is on the 2' heights and meets the GLO-90 chunks of later sessions with a step, for good; the player
sees no message, only the log has one. Later passes of the next session read GLO-90 columns under that terrain, and
a volcano can get two plans (`src/EarthVolcanoes.cs:300`). *Fix (the author's choice):* do not make the chunk: throw
in the terrain pass with the chunk noted (it is remade at the next start) and tell the players in chat that land
beyond needs the download; or stop the server as before the game runs. No rules version.

**FP-04 — The refusal of worlds from a newer Earth does not see new fields, and the older build then drops them**
(L, P; reopens EA-0079 / ER-05). `src/EarthMapSystem.cs:447-462`, `src/EarthParams.cs:16-17` (a plain
`[ProtoContract]`, no `IExtensible`), `:513`. Every new rule so far arrived as a new field (202, 203, 204). A world
made by a build with a field this build does not know has no known value above newest, so it is not refused;
protobuf-net skips the unknown field on read, and `InitWorld` stores the params again for `LastSavedWith`, without it.
Reopened with the newer build the rule reads 0 ("as before") for good. Probe (`probes/proto`, the game's own
protobuf-net): a record with field 207 = 1, read and saved through a class without 207, comes back 0 (14 bytes saved,
11 rewritten). *Fix:* refuse when the save's `LastSavedWith` is a newer version than this build's, and keep unknown
fields (`IExtensible`) so that a save by an older build cannot lose them. No rules version.

**FP-05 — A default new world starts the 8.6 GB download with no free-space check, and failing writes do not stop it**
(L for the default and the missing check; P for the rest). `worldconfig.json:266` (`earthGlo90Download` default
`release`), `src/EarthElevationSystem.cs:585-596`, `:617-632`, `src/Core/DemDownloader.cs:277-291`, `:304-316`,
`:626-641`. On a disk with less than about 8.7 GB free it fills the disk the save is on. Probe (every tile write
failing, 40 squares): read 4.0 times the release's bytes, 242 log lines, ended "finished" with an empty error, and so
starts again at every opening of the world; scaled, about 34 GB read for nothing and 160,000 log lines a start.
*Fix:* refuse to start, and stop, below the remaining bytes plus a reserve; a local write failure never falls
through to Copernicus; the same reserve for squares fetched as needed.

**FP-06 — Earth's hail is never drawn or heard on the client** (L, R; contradicts a bench note, so check in game
first). `src/EarthWeather.cs:398-399`, `:591-607`: the postfix sits on `WeatherSimulationRegion.clientUpdate`, but
vanilla's `TickEvery25ms` calls `clientUpdate` first and ends with
`weatherData.BlendedPrecType = CurWeatherEvent.State.PrecType;` (decompiled `WeatherSimulationRegion.cs:225-229`,
`:310`), which overwrites what the postfix set; particles and sound read the blended value afterwards
(`WeatherSimulationParticles.cs:301`, `WeatherSimulationSound.cs:183`). From rules 8 the hail hurts, so the player is
hurt under drawn rain, while vanilla's own hail events are still drawn and do nothing. Decision 0017 records hail seen
on the bench in Nebraska; by this reading that was vanilla's event. *Fix:* postfix `TickEvery25ms`. No rules version.

**FP-07 — The weather is torn along the whole equator, in every rules version** (L, P).
`src/Core/WeatherModel.cs:329-332` (`PrevailingAt`: `bool equatorward = flowNorth * lat <= 0;` then
`east = equatorward ? -0.9 * m : 1.05 * m * SmoothStep(0, 6, |lat|)`): with the ITCZ off the equator the trade branch
is -0.9 m right up to latitude 0 and the monsoon branch starts from 0 on the other side, so the steering wind steps
there (15.1 km/h on average) and the advected rain fields shear. Probe: one block apart across the equator it rains
on one side only 4.4 % of the time (0.02 % on control lines at 3° N and S); pressure differs by 3.4 hPa rms, 17.4 at
most; at Kampala's meridian cloudness 0.00 one block north and 1.50 one block south. *Fix:* blend the two branches
through latitude 0. Changes the weather: the next weather rules (10 is not published yet).

**FP-08 — Columns in and near the sea of 1318 cost 120-535 µs each (normal 1-3): 0.1-0.5 s a chunk around
Rotterdam** (P; EA-0095). `src/Core/TerrainModel.Coasts.cs:29-68`, `src/Core/TerrainModel.NaturalColumn.cs:91-139`,
`src/Core/TerrainModel.Rivers.cs:332`, `:377`, `:393-424`, `src/Core/TerrainModel.Shore.cs:56`, `:88`. The works-rules-3
ring search (162-290 samples, each up to 48 more lookups in `EnclosedBySea`) runs several times a column because
`Trough`'s lookups evict the one-entry cache. A bit-identical fix is prototyped
([probes/terrain/sea-of-1318-search-once.patch](code-review-2026-11-14/fable-pass/probes/terrain/sea-of-1318-search-once.patch)):
1,114,112 columns at each of 1:100 and 1:10 byte-identical in every field; Rotterdam 494 → 44 µs at 1:100 and
421 → 148-204 at 1:10, the Rhine-Maas delta 535 → 105, Flevoland 27.8 → 1.7, the Wadden Sea 76.7 → 5.2. No rules
version. The remaining 100-200 µs needs the coast of 1318 as geometry (a works rules version).

### 11.4 Medium

| ID | Finding | Where | Failure or cost | Fix |
|---|---|---|---|---|
| FP-09 | One-shot upgrade flags are applied to a new world at its second start (L; reopens EA-0081 / ER-07) | `src/WorldUpgrades.cs:63-80`, `src/EarthMapSystem.cs:417` | `fresh` is true only in the start that creates the world; the skipped flags stay set and nothing remembers the skip, so quitting and re-entering applies them: "ores vanilla" becomes Earth's ore maps, with a seam against the first session's chunks | clear the flags when a fresh world skips them (and say so), or apply a one-shot only to a world whose `CreatedWith` is null or older than this build |
| FP-10 | No test pins the terrain of an old-rules world (P: the probe exists) | `tests\*` | every "bit-identical" claim since rc.13 rests on a probe outside the repository; the refactors now in flight have no net | a digest test with one params file per published version: `probes/rules/digest-src`, `params-*.txt`, `hash-*.txt` are ready to adapt |
| FP-11 | A region's forest and shrub maps are sampled on built-in heights or on GLO-90 depending on what the thread did before (L, P) | `src/EarthWorldGen.cs:383-388`, `src/EarthMapLayers.cs:45-74`, `src/Core/TerrainModel.Ecology.cs:45-46`, `src/EarthElevationSystem.cs:407` | `FetchAllowed` turns on at a thread's first terrain pass; the engine makes a chunk's region and its 8 neighbours before that. The nine spawn regions of a first world, the first new regions of each session, and every region with `MaxWorldgenThreads` > 1 use built-in heights. Probe: forest map bytes differ in 647 of 1,089 pixels at Mont Blanc (238 by 32 or more of 255), 373 in the Tatra, 0 at Riga | set `FetchAllowed` around `ForestAndShrubs` (and restore it); no rules version, but the spawn regions' squares are then fetched first |
| FP-12 | Ore rules v5's valley search leaves a body out of a chunk when the deposit pass runs on a thread that may not fetch (R) | `src/Core/OreDeposits.cs:577`, `:712-765`, `src/EarthOreDeposits.cs:322`, `src/EarthOres.cs:372-377` | systematic with `MaxWorldgenThreads` >= 2 until the tiles arrive: chunk-shaped holes in placers and blankets for good; rare by default | `FetchAllowed` as a scope at the entry of every later-pass handler (C-R1) |
| FP-13 | Gaps in the refusal net (L for the chunks in flight; R) | `src/EarthWorldGen.cs:203-219`, `:277-279`, `:325-381`, `src/EarthVolcanoes.cs:360-369`, `:444-453`, `src/EarthSprings.cs:117-129`, `src/EarthWaterTable.cs:334-349` | (a) `BareRock`, `KnownHotSprings`, `FillRockFloor`, `ClearShorePlants` and the volcano passes have no fail-closed catch; springs and the water table log and keep the chunk. (b) After a refusal the context is withdrawn and `Active` is off, so chunks in flight finish their later passes with vanilla's guards and are saved un-noted. (c) A refusal during a static translocator's chunk peek notes the real chunk at those coordinates for deletion (`BlockEntityStaticTranslocator.cs:302-327`) | one wrapper for every Earth pass handler: the refusal check, the `FailedPass` catch, and a note for any chunk that passes a later pass after a refusal; skip the note for a peek |
| FP-14 | The first-failure stop also applies to worlds made before it, with no way back (R) | `src/EarthWorldGen.cs:182-194`, `src/EarthGeology.cs:512` | a place-dependent throw stops the server every time a player comes near that chunk; before `222120a` it logged and went on | on a repeat failure of the same chunk at the next start, fall back to "log and keep" for that chunk, said loudly; or an admin switch |
| FP-15 | GLO-90 tiles are checked against the release's manifest only when they come from the release (R) | `src/Core/DemDownloader.cs:188-190`, `:242-263`, `src/Core/FineElevation.cs:161`, `src/Core/DemTiles.cs:224-229` | tiles converted from Copernicus and tiles already on disk are never compared with egl-v1's hashes or even GLO-90's dimensions; two machines can generate one world on different heights | check every tile the store keeps against the manifest once (a marker file), convert-and-compare for Copernicus |
| FP-16 | A syntax or type error in `earth.json` makes Earth write defaults over the admin's file (L) | `src/EarthConfigStore.cs:22-28`, `:110` | `Enabled` back to true, mirrors back to GitHub, every setting lost, no copy kept | on a read error keep the file, copy it to `earth.json.bad-<time>`, and stop or run on defaults without writing |
| FP-17 | Downloads and readers take sizes from the source before checking them (P) | `src/Core/DataTransport.cs:258`, `:278-295`, `src/Core/DemDownloader.cs:253`, `:383-385`, `:427-431`, `src/Core/DemTiles.cs:82`, `:116-121`, `:186-199` | a mirror or a damaged file: 1,573 MB taken in 3.4 s (working set 4.5 GB), a manifest asking for 4.5 GB buffers, a GeoTIFF directory allocating 4 GB (OutOfMemory); the pinned manifest is checked only once whole in memory | a size limit on every body (the pinned size where there is one), counts checked against the file's length before allocating |
| FP-18 | Start-up memory holds data a new world never reads (P) | `src/Core/EarthDataset.cs:416`, `src/Core/TerrainOverlays.cs:86-87` | `rivers.bin` parsed at every start (0.27 s, 123-135 MB) though worlds with river courses 1 and later never read it; the referee's islands and coasts kept beside the as-read ones (+158 MB). (The climate fix planes, +50 MB, are fixed by `4621f37`.) | load `rivers.bin` only for a world on courses 0; build the referee's copies in place or drop the as-read ones once applied |
| FP-19 | `tests\Run-All.ps1` replaces the release zip of the current version; PlaytestReviewTests needs a worktree three folders deep (L, P) | `tests/Run-All.ps1:93` (`-p:OverwriteRelease=true`), `EarthMap.csproj:71`, `:120`; `tests/PlaytestReviewTests/Program.cs:25`, `LakeAudit.cs:7` | running the tests in the main checkout overwrites `release\earth-2.0.0-rc.13.zip` (two different rc.13 zips now exist, 39,989,244 and 39,706,525 bytes); PlaytestReviewTests fails in the main checkout and ignores `EARTH_RAW` | build into a test output (`-p:ReleaseDir`) or without packing; read `rawdata` from the root or `EARTH_RAW` |
| FP-20 | Islands crossing 180° are cut by a notch down to a sea-level beach (P) | `src/Core/Islands.cs:207-231`, `:236-263`, `:297-345`, `src/Core/TerrainModel.Coasts.cs:215` | Taveuni (a 19.6 km cut) and Rabi, the 4 of 167,555 outlines on the meridian: tops 210 211 177 164 ... 164 179 205 209 across it at 16.9° S (sea level 165) | join the halves in the bake or at load; island rules 4 |
| FP-21 | `RiverFlowData.At` clamps at its 1° tile edges instead of reading the neighbour (L, P) | `src/Core/RiverFlow.cs:150-173` (the clamp at 163) | a straight seam in wide rivers at 1:10: the Amazon on 3° S has 361 of 1,901 pairs with a bed step of 2 or more blocks (4 at most), 202 still/flowing flips and 17 flow turns, against 3-4 / 2-9 / 0 a few blocks on; the Lena on 130° E 47 / 30 / 24 | read the neighbouring tile's pixel; flow rules 2 |
| FP-22 | A lake's area is its piece in the current 1° tile (P) | `src/Core/LakeOutlines.cs:190`, `:436-468`; read at `src/Core/TerrainModel.Shore.cs:62`, `:111`, `src/Core/TerrainModel.NaturalColumn.cs:74`, `:484`, `src/Core/TerrainModel.Rivers.cs:468` | of 514 lakes crossing sampled tile edges, 192 have a piece under 1 km² on one side only, so the lake shore strip stops along the whole-degree line (84 % of shore columns south of 62° N against 42 % north in one lake) | the lake's whole area in the bake, per lake id; a rules version |
| FP-23 | Inside a large island a column costs 5-65 µs at 1:10 (P) | `src/Core/Islands.cs:207-231`, `:326-345`, `src/Core/TerrainModel.Coasts.cs:206`, `src/Core/TerrainModel.Shore.cs:101` | `Islands.Query` scans the island's edges and is called twice a column: Isabela 15.9, Åland 11.4, a Maine island 64.5 | reuse the hit, add a cell index over the edges; bit-identical |
| FP-24 | The mask's river water costs 6.8 µs a column at 1:10 (P) | `src/Core/RiverNetwork.cs:1083`, `:1091-1132`, `src/Core/TerrainModel.Rivers.cs:672`, `:731-732`, `:978` | `ReachBlend` walks whole 2,560-block buckets twice a column and allocates closures (the Amazon's meeting of waters) | one per-thread candidate list in scan order; bit-identical |
| FP-25 | A storm's rain is not faded at its outer radius (P) | `src/Core/WeatherStorms.cs:339-348`, `:395-413`, `src/Core/WeatherModel.cs:585` | the rain boost steps by up to 0.55 sd at r = R; rain on one side only along 2 % of the edge | fade to 0 at R; next weather rules |
| FP-26 | Vanilla's world-wide temperature noise stays on top of Earth's fitted spells (P, on the game's DLL) | `src/EarthClimateHooks.cs:58`, `:61-71`; vanilla `ModTemperature.cs:79-80` | sd 1.8-2.0 °C; whole game years 1.7-1.9 °C off, up to 4-5 °C; it swamps the fitted spells in the tropics and moves the live climate off the catalogue envelopes | remove it under a rules flag, or keep it and say so in 0017 |
| FP-27 | The snow catch-up costs 14.5-16.6 ms per region per tick (P) | `src/EarthWeather.cs:519-531`, `src/EarthClimateHooks.cs:61-71`, under vanilla `WeatherSimulationRegion.cs:164-223` | vanilla's own readings would take about 1.5 ms; 6 ticks per region after a year away | a per-thread memo of the place's part over the 480 consecutive hours; no result changes |
| FP-28 | The cold-sea cloud deck and sea fog end on a cliff at the 0.5 land-share contour (P) | `src/Core/WeatherData.cs:230`, `src/Core/WeatherModel.cs:641-657`, `:687` | off Peru the month's cover goes 0.80 → 0.31 and fog 1.0 → 0.0 within 0.57 blocks; Lima never gets fog | a ramp over the coast band; next weather rules |
| FP-29 | Weather rules 10 use `Math.Tanh` on the shared path (L; `0cff689`, after the pinned tree) | `src/Core/WeatherStorms.cs:478`, `:485` | the fronts' veer, and the wind every side and Sea read, can differ in the last bits between operating systems, against EA-0029 and decision 0017 | `1 - 2 / (PortableMath.Exp(2x) + 1)`; pin rules 10. Free now, a rules version once a rules-10 world exists |
| FP-30 | The strata, ground and layer passes rewrite every rock block one by one after vanilla's strata (estimate from measured primitives) | `src/EarthGeology.cs:527-568`, `src/EarthGround.cs:144-166`, `src/EarthOreLayers.cs:456-534` | about 8 ms a chunk at 256 high, 25-40 ms at 1024 | a bulk fast path for uniform deep layers; the same blocks |

### 11.5 Low and refactor

Each line: where, what, fix. The area reports have the detail under the code in brackets.

**Concurrency** ([report](code-review-2026-11-14/fable-pass/concurrency.md))
- C-L1 `src/EarthVolcanoes.cs:226-283`: `Init` publishes `cells` and the body arrays before filling them while map threads may read (`/wgen regen`); build into locals, assign last.
- C-L2 `src/EarthElevationSystem.cs:111`, `:436`, `src/Core/DemDownloader.cs:101`: the spawn area's tile wait cannot be cancelled (about 9 minutes per origin per square on a source that stalls); FP-01's poll fixes it.
- C-R1 `src/Core/FineElevation.cs:105-109`: `FetchAllowed` is thread history; make it a scope set at each generation entry point (root of FP-11, FP-12).
- C-R2 `src/EarthWorldGen.cs:441-455`, `src/EarthTrees.cs:494-504`, `src/ClimateCost.cs:16`: `Dispose` leaves the generator half alive; `EarthTrees.pickers` and `ClimateCost.On` survive it.
- L-1 (lead) `src/EarthOreDeposits.cs` `SearchLater` (`636137e`): the pool-thread search is not cancelled at `Dispose`; it can run seconds into the next world on the old model. Check a stop flag in the search loop.

**World generation** ([report](code-review-2026-11-14/fable-pass/worldgen.md))
- W-L1 `src/EarthWorldGen.cs:484`: `DeleteFailedChunks` runs before the spawn square loads, so a noted chunk there stays loaded.
- W-L2 `src/EarthWorldGen.cs:374-379`, `src/EarthVolcanoes.cs:324-340`, `:579-604`: raw writes over blocks that have block entities (wild fruiting bushes on the shore strip, structures in a volcano's depths); use the accessor or skip them.
- W-L3 `src/EarthOreLayers.cs:401-411`, `src/EarthGeology.cs:671-676`: a rebuilt ore-layer table reads tops lowered by craters (crater columns only).
- W-L4 `src/RockFloorPass.cs:37-51`: `Top` reads the world's map chunk, not the request's (only a peek differs).
- W-P2 `src/Core/ColumnStore.cs:181-200`: a 115 kB large-object array per chunk; pool it.
- W-P3 `src/TerrainChunkWriter.cs:120`, `:134`: the locked indexer and `SetFluid` per block; `SetBlockUnsafe` is about half the cost.
- W-P4 `src/RockFloorPass.cs:91`, `src/EarthElevationSystem.cs:408`, `:440-453`, `src/EarthWaterTable.cs:352-382`, `src/EarthVolcanoes.cs:290-305`, `src/SpringsAndPonds.cs:125-156`: small per-chunk allocations; the spring guard does climate work before vanilla's cheap rejections (1.3 ms at most).
- W-R1: six separate "generation is on" flags; one handler wrapper would close FP-13.

**Saved worlds** ([report](code-review-2026-11-14/fable-pass/rules.md))
- S-L1 `src/Core/WeatherModel.cs:707` (`5956d71`): rules 7 fog moved by an ulp without a gate and is still unpinned; pin it.
- S-L2 `src/Core/GroundColumns.cs:486`: the refusal covers int fields only; an unknown `GroundDepth`, `Canals` or `StreamMode` string reads as the default. FP-04's version check covers it.
- S-L3: the refusal exists only from `6354bbd` on; rc.13 and older builds still open newer worlds (say so in the release notes).
- S-L4 `src/EarthCommands.cs:113-131`: `/earth siterules` changes a saved world's generation for the session (warned; a test-world-only switch would be safer).
- S-L5 `tests/EarthWorldOptionsTests/Program.cs:348-353`: `Freeze` runs with `data: null`, so the GLO-90 rules and `ComputeParams` are untested against `RulesAgainstBuild`.
- S-L6 `AGENTS.md`: EarthWorldOptionsTests needs `-p:VintageStoryPath`, which the one-project line omits.
- S-L7 (`5b07374`, `a60929c`, `0fb3106`, `6a72c33`): rc.7 changed `works-1318.bin` and `rivers-nonperennial-1.bin` in place; history only, the naming rule has been kept since.
- S-L8 `src/EarthParams.cs:518`: v1.0.x worlds were made with weather rules 2 and now run rules 5 on a `weather.bin` changed in place (`a37fc76`); live weather only. Record it in decision 0002 or 0017.
- S-R1 `src/EarthMapSystem.cs:669-678`, `:699-700`, `src/EarthParams.cs:518`, `:690`, `:703-718`, `src/EarthElevationSystem.cs:106`, `:256`, `:278`, `:312`, `:340`, `src/WorldUpgrades.cs:345-349`: dead "newer than this build" warnings and `Math.Min` clamps now that such worlds are refused.
- S-R2 `tests/PlaytestReviewTests/Program.cs:354-357`: the unique-number check is duplicated. S-R3 `src/EarthParams.cs:11-12`: the hand-kept number list beside the pinned table.
- L-2 (lead, `4621f37`) `src/Core/EarthDataset.cs` `PredictedFix`: predicted `climatefix.bin` and `climatefix2.bin` are differences from `climate.bin` with no pin of which `climate.bin`; a re-bake would silently change frozen worlds' climate. Store a hash of the base in the fix's header.
- L-3 (lead, `4621f37`) `src/Core/WeatherData.cs`: a predicted `weather.bin`'s signature is a stored number, no longer a hash of what is read; recompute it at pack time.

**Terrain** ([report](code-review-2026-11-14/fable-pass/terrain.md))
- T-L7 `src/Core/TerrainModel.cs:385-408`, `src/Core/TerrainModel.Shore.cs:39-71`, `src/Core/TerrainModel.Rivers.cs:306-327`: every column repeats layer lookups; the shore strip is 15-25 % of an inland column, `ApplyMouth` half an open-sea one. A bit-identical reorder.
- T-L8 `src/Core/RiverNetwork.cs:276-290`, `src/Core/RiverSegmentBuilder.cs:187-194`: river pieces stored as float snap to 0.25 block east of 17.6° E at 1:10 and 0.5 block east of 18.4° E at 1:5 (by arithmetic).
- T-L9 `src/Core/RiverStraighten.cs:302-320`: the smoothing window never widens again without `cornerSigma` (a rules step).
- T-L10 `src/Core/FineElevation.cs:438-444`: a tile is renamed `.bad` on any IOException, also a transient sharing violation; retry first.
- T-L11 `src/Core/VerticalScale.cs:65-74`, `src/Core/TerrainModel.Coasts.cs:113`, `src/Core/RiverEnds.cs:198-265`, `src/Core/SeaChannels.cs:566-666`: the terrain uses `Math.Log/Exp/Pow/Sin/Cos` and `PriorityQueue` tie order; a world generated on two operating systems or runtimes could differ at thresholds. Say so in 0002, or move to PortableMath under a rules version.
- T-L12 `src/Core/TerrainModel.NaturalColumn.cs:361`, `src/Core/TerrainModel.Detail.cs:14-22`: `SeamNoise` jumps at the jump line (no grid lake lies there today).
- T-L13 `src/Core/RiverStraighten.cs:448-467`: Douglas-Peucker recurses over whole chains (stack depth).
- T-L14 `src/Core/TerrainSampler.cs:59-88`: `KeptLakeLevelNear` keeps only the highest level near a place (latent; all 18 terminal lakes are fine today).
- T-R1 one column context. T-R2 frozen rule versions interleaved in single methods (`ApplyRiver` and `ApplyChannel` are copies). T-R3 `LakeOutlineLayer.Hit` means different things per caller. T-R4 a column depends on per-thread switches. T-R5 public mutable fields with lazy per-reach tables.

**Weather, climate, ecology** ([report](code-review-2026-11-14/fable-pass/weather.md))
- X-L1 `src/Core/WeatherModel.cs:387`, `:582-585`: a hard edge at the 0.01 mm/day dry-month line (rain cloud on one side only in 1.12 % of pairs).
- X-L2 `src/EarthWeather.cs:527`, `:565`, `src/EarthClimateHooks.cs:68`: rain, hail state and anomaly are asked at `totalDays * HoursPerDay`, the rest at `TotalHours` (bits differ in 6.5 % of calls, by 8.8e-13 at most, no rain flips).
- X-L3 `src/Core/WeatherModel.cs:225-229`: the weather ignores the calendar's season override.
- X-L4 `src/EarthClimateHooks.cs:129`: the live temperature steps across the equator in January and July (1.83 °C on average, 3.97 at most).
- X-L5 `src/EarthWeather.cs:631-639`: land fog's top follows the player's rain-map height, so it sits above canopies and roofs.
- X-L6 `src/EarthWeather.cs:155-159`, `:180-193`: a client builds its model from unchecked parameters, and the handshake covers `weather.bin` only (not `weatherground.bin` or the fix planes).
- X-L7 `src/EarthGroundMoisture.cs:169-178`, `src/Core/GroundMoisture.cs:33-47`: ground moisture cached per cell depends on who asked first.
- X-L9 `src/EarthWeather.cs:847-855`: the cloud-drift cancel subtracts a different wind than vanilla adds (`CloudRenderer.cs:174`).
- L-4 (lead, `0cff689`) `src/Core/WeatherContract.cs:111-113`: on a squall line inside a low's gale field the contract answers Kind "squall" instead of "gale" (the wind in m/s is right).
- X-R1 `src/Core/WeatherStorms.cs:150-164`: 32 B per storm lookup from a closure (246 kB per snow catch-up). X-R2 fog and rules 9/10 fingerprints unpinned in the normal run. X-R3 `src/EarthWeather.cs:519`: vanilla's rain noise is still computed under Earth's postfix.

**Data, commands, build, tests** ([report](code-review-2026-11-14/fable-pass/data.md))
- D-L1 `EarthMap.csproj:3-19`: published zips carry the private repo's SourceLink URL with the commit and the build folder in the PDB and DLL (1.0.2 too); the zip's bytes depend on the checkout folder. Turn SourceLink and path mapping off for the release.
- D-L4 `src/EarthGeology.cs:252-253`, `src/Core/GroundDownloader.cs:86`, `src/Core/DemDownloader.cs:166`, `src/Core/EarthDataStore.cs:161`: a malformed repo URL ends the start with a raw exception that does not name the setting.
- D-L5 `src/EarthCommands.cs:193-197`: `/earth story` gives every chat-privilege player the coordinates of all story locations.
- D-L6 `src/EarthElevationSystem.cs:571-582`: the dialog's "start" packet re-checks privilege but not that the world reads GLO-90 (the command does).
- D-L7 `src/Core/DemDownloader.cs:381-404`, `:542`, `src/Core/DataTransport.cs:225-226`: an unwritable store is reported as "the release can't be reached"; a missing local source folder is retried for 21 s per file.
- D-L9 `src/EarthGeology.cs:238-242`, `src/Core/GroundColumns.cs:78-79`, `:183-200`: the 30" ground sets of worlds from before `GroundData` are checked by size only and unpacked without a bound.
- D-L10 `src/EarthWeather.cs:922-960`: `/ew hail` runs up to 35,000 samples on the main thread at chat privilege (5-64 ms).
- D-L11 `modinfo.json:6`: the 193 MB of lake, river, flow and dam files a new GLO-90 world fetches are not in the stated sizes.
- D-R1 `src/Core/EarthBinary.cs:97-122`: `UnbrotliAll` lets InvalidOperationException out against its own summary; readers trust header counts (reachable only from tools and tests: the files are hash-checked first). D-R2 `src/Core/GroundColumns.cs:183-209`: three large-object copies per lazy ground-tile unpack. D-R3 `.part`, `.bad` and `.copy` files of a killed process are never swept. D-R4 `src/Core/EarthDataset.cs:233-237`: `peaks.json` costs 51 MB live. D-R5 no test names any type of EarthCommands, EarthElevationSystem, EarthConfigStore, WorldUpgrades, SelfTest, DamRemoval, TiledGrid or WaterOverlays (7,538 game-side lines).
- L-5 (lead, `4621f37`) `src/Core/PlaneCoding.cs`: `Decode` allocates `new int[cells]` per block, 130 kB on the large object heap per tile on a cache miss; a per-thread scratch array.

### 11.6 Measured

Per column, warm, best of three passes (µs; the machine was shared, so the cheapest values vary up to 1.9 times):

| Place | 1:100 | 1:10 | Where the time goes |
|---|---:|---:|---|
| Open ocean 0, -30 | 1.28 | 1.41 | `ApplyMouth` about 0.65 |
| Daugava mouth | 6.70 | 3.3-4.1 | mouth 1.6-2.2, islands about 1 |
| Rotterdam | 494 | 161-420 | the sea-of-1318 search (FP-08) |
| Amazon at Manaus | 3.03 | 1.91 land, 6.78 all river | rivers (FP-24) |
| Zermatt | 1.97 | 1.32-1.36 | shore strip 0.3-0.5, GLO-90 0.7 |
| Sahara | 1.62 | 0.79-1.26 | the same |
| Large islands | - | 4.8-64.5 | `Islands.Query` twice (FP-23) |

Model build: 6.7-7.7 s and 2.1 GB at 1:100 (2.77 million river pieces), 15.4-19.6 s and 3.2 GB at 1:10 (9.37 million).
Client data: 185 ms, 53 MB live (130 MB with fix 4 and the CHELSA files, before `4621f37`). Server data on the
tracked files: 752 ms, 739 MB live with the tile cache full. Release build: 0 warnings, 11 s.

### 11.7 Found sound in this pass

- **Old worlds keep their terrain.** v1.0.2, rc.8, rc.11 and rc.13 worlds, each built by its own version's code and by
  `fef6166`: every `Column` field, climate pixel and climate sample identical over 2.4 million columns. Weather rules
  5-7 built from rc.13's source equal the pinned hashes. The EarthParams table is unchanged over 22 revisions since
  v1.0.0, and no rules constant is lower than in any release or RC.
- **The terrain is a pure function.** 256,000 columns identical row by row, shuffled, on 16 threads and on a fresh
  model; no step at the jump line; nothing thrown at the poles, ±180° or negative and huge coordinates.
- **The weather is deterministic** across call order, caches, threads and two models on one thread, for rules 5-9;
  time is continuous at every slot, month and year edge; no NaN in 360,000 samples under extreme settings.
- **Downloads and data files.** Every store file, ground set and release file is size- and SHA-256-checked before
  rename and again at each start; no file name from a manifest or config can leave the data folder; `file://`
  redirects are not followed; two servers sharing ModData are handled.
- **The refactors of 14 November** (TerrainRules, ore facts, the EarthMapSystem, EarthParams, EarthWeather and pond
  splits) change no behaviour: every rewritten test read against its fact, the moves checked line for line.
- **Patches.** `tests\PatchVanillaTests` passes against the installed 1.22.7 (387 checks, 46 features); the
  prospecting pick's own generators never reach Earth's chunk postfixes.

### 11.8 Order of work (proposal)

1. **Before publishing:** FP-01, FP-03 (decide), FP-04, FP-09, FP-13, FP-02 (decide the rule): each can leave wrong
   chunks for good, and none needs a rules version. FP-05 and FP-16 (a full disk, a wiped config).
2. **Weather rules 10, while unpublished:** FP-07, FP-29, FP-25, FP-28, and FP-26's decision; FP-06 after a look in
   game.
3. **The digest test (FP-10) before any more refactoring**, then FP-08's prototype, FP-23, FP-24, FP-27, FP-30.
4. **Next rules versions:** FP-20 (islands 4), FP-21 (flow 2), FP-22 (lakes).
5. **Hygiene:** FP-11, FP-12 (with C-R1), FP-15, FP-17, FP-18, FP-19, and the lows.

Backlog: EA-0079 (FP-04) and EA-0081 (FP-09) reopened; EA-0095 (FP-08) has the prototype. New items: FP-01 EA-0097,
FP-02 EA-0098, FP-03 EA-0099, FP-05 EA-0100, FP-06 EA-0101, FP-07 EA-0102, FP-10 EA-0103, FP-11/12 EA-0104, FP-13/14
EA-0105, FP-15/17 EA-0106, FP-16 EA-0107, FP-18 EA-0108, FP-19 EA-0109, FP-20 EA-0110, FP-21 EA-0111, FP-22 EA-0112,
FP-23/24/30 EA-0113, FP-25/26/28/29 EA-0114, FP-27 EA-0115, the lows and refactors of 11.5 EA-0116.

### 11.9 Not covered by this pass

Nothing was run in game, on Linux or on another .NET runtime. 1:5 and 1:200 models were not built. Not read:
`RiverPoolPlanner`'s forward pass, `RiverDeltas`, `RiverFalls`, `RiverSourceRepository`, `WaterOverlays`, `LakeShores`,
`LakeOutlets`, `TerminalLakes`' loaders, the ore session and binder (`OreSession`, `OreBinder`, `OreProspecting`,
`OreProviderBlocks`, `OreTotals`), `EarthSpawn`, `EarthStoneTextures`, `SelfTest`, the tree picker's internals,
`BotanicalRegions`, `VanillaFaunaRanges`, `FireRegime`, `ClimateZones`. The slow test projects were not run (the
quick set and the ones named above were).
