# Earth 2.0.0-rc.13 review: lifecycle, params and save compatibility, commands, patches

Read-only review of `C:\VS mods\Earth` at `6371e32` (main, 2.0.0-rc.13), 14 November 2026. Nothing was built or run;
every finding comes from reading the code, `git show`/`git grep` at tags and RC commits, and the decompiled 1.22.7
engine in `devtools\vs-ref`. VERIFIED = the code path was read end to end; PLAUSIBLE = read in part, or depends on
something outside the files read.

Files read in full: `EarthMapSystem.cs`, `EarthParams.cs`, `EarthWorldOptions.cs`, `EarthConfigStore.cs`,
`WorldUpgrades.cs`, `RequiredMods.cs`, `EarthCommands.cs`, `WorldGenRules.cs`, `WorldGenerationContext.cs`,
`CylinderCompat.cs`, `AstraTerraLongitude.cs`, `AstraTerraLook.cs`, `HodAquiferContract.cs`, `Core/ModelCache.cs`,
`EarthVerticalBootstrap.cs`, `EarthCrafting.cs`. Read in part: `PatchVanilla.cs` (binding machinery, the first table,
`OreVanilla`), `SelfTest.cs` (start, finish, outline), `WhereReport.cs`, `Core/WeatherContract.cs`, `EarthWorldGen.cs`
(start, refusal, set-up, dispose), `EarthWeather.cs` (start, watch, patch lifetime), `ClimateLapse.cs`,
`EarthGrassWinter.cs`, `SnowMeltLight.cs`, `EarthGrassSeason.cs`, `EarthElevationSystem.cs` (dam and flow files),
`Core/EarthDataStore.cs`, `Core/FineElevation.cs`. Not read: the bodies of the other patch sites' patch methods.

---

## 1. Findings

### F1. A world saved by a newer build runs older rules, mostly without a word (medium, VERIFIED)

- `src/EarthParams.cs:583` and `:596` clamp silently: `DetailRules = System.Math.Min(DetailRules, TerrainModel.DetailRulesVersion)`,
  `GenFixRules = System.Math.Min(GenFixRules, TerrainModel.GenFixRulesVersion)`. Also `:602` (CoarseRules), `:610`
  (WorksRules), `:411` (`WeatherModelRulesOrOld`), `src/WorldGenRules.cs:133` (`Math.Min(p.FireRules, FireRegime.RulesVersion)`).
- `src/EarthParams.cs:589-593` pass others raw (`ClimateRules`, `GeothermalRules`, `ChannelRules`, `ShoreZoneRules`,
  `ShoreRules`), as do `AlpineSurfaceRules`, `GutterSeabed`, `RiverPoolLayout`, `RiverChannels` (`:627`, the saved number is
  handed to `SeaChannels.Build` as its version). With `>=` tests they behave as this build's newest.
- `src/EarthMapSystem.cs:601-612` (`AnnounceSavedRules`) warns for 13 fields only (summits, alpine surface, arid, straits,
  reservoir, outlets, sea referee, courses, lake/river outlines, dams, channels, geothermal, detail). Nothing is said for
  GenFixRules, ShoreRules, ShoreZoneRules, SoilRules, ZoneRules, FireRules, WetlandRules, GroundCoverRules, OreLayers,
  OreRules, ClimateRules, RiverPoolLayout, RiverChannels, Islands, WeatherModelRules, CraftingRules, PerennialRules.
- Case that happens with RCs: a world made on rc.13 (GenFixRules 3, weather rules 7, geothermal 3, ore layers 4, ground
  rules 2, climate fix 4) opened with rc.12. rc.12 has `GenFixRulesVersion = 2`, `Geothermal.RulesVersion = 2`,
  `OreLayers.RulesVersion = 2` (checked with `git grep` at `3840e71`): new chunks get rc.12's rules and stay in the save.
  The saved numbers are kept, so going back to rc.13 gives a second seam.
- This is the same class as CR-A1 (fail-closed case by case): a downgrade is the one "wrong chunk saved for good" path
  left without a refusal.
- Fix: one table `(name, saved value, this build's newest)` over every rules field, checked once in `InitWorld` before
  the data loads. Any saved value above this build's: `Refuse("this world was made with a newer Earth (rules X n, this
  build has m); install that version or newer")`. Add two record-only fields at the next free numbers (204, 205):
  `CreatedWith` and `LastSavedWith` (mod version strings), so the message can name the version and a support question
  can be answered from the save. Drop the per-field `Math.Min` clamps once the refusal is in.
- Risk: none for a world opened with the build that made it or a newer one (no value is above newest). It only stops a
  downgrade. New fields are record-only.

### F2. One-shot upgrade flags are applied to a world created in this start, over the Earth tab's choices (medium, VERIFIED)

- `src/EarthMapSystem.cs:416-422`: `upgrades.Layout(p)`, `upgrades.Terrain(p)`, `upgrades.Regional(p)` run for every
  enabled world; `fresh` is not consulted.
- `src/WorldUpgrades.cs:360-375` (`ApplyOreRules`): `p.RegionalOres = Config.RegionalOres;` (config default `true`,
  `EarthMapConfig.cs:50`), while the tab's default is `earthOreDeposits: vanilla`. Likewise `ApplyGeologyRules` (`:377`),
  `ApplyFaunaRules` (`:392`), `ApplyTreeRules` (`:401`), `ApplyIslands` (`:238`), `ApplyCoasts` (`:257`).
- A flag left `true` in `earth.json` (set for another world; a flag is reset only once the world that took it is saved)
  therefore rewrites a brand-new world's creation choices right after `EarthWorldOptions.Freeze` wrote them. With
  `UpgradeExistingWorldOreRules`: ores "vanilla" on the tab becomes Earth's ore maps, `OreRules` 5.
- The one-shots also leave dependent fields behind, in new and saved worlds alike: `ApplyOreRules` does not recompute
  `CraftingRules` (`EarthCrafting.RulesFor(GroundColumns, RegionalOres)`, `EarthWorldOptions.cs:119`), so a ground-rules-1
  world switched to the ore maps places bauxite by its real districts without decision 0077's recipes. `ApplyFaunaRules`
  sets `RegionalFauna` without `PlacementRules`, `VanillaFaunaRanges` or `FaunaRangeProfile`. `ApplyGeologyRules` can set
  `RegionalGeology = false` in a world with `GroundColumns > 0` and `OreLayers > 0` (both documented as needing regional
  geology).
- Fix: in `InitWorld`, skip the three `Run` tables when `fresh` (log one line naming the flags left set, leave them set).
  Derive the dependent fields in one place (see section 2: the same function `Freeze` uses), or have each one-shot call it.
- Risk: none for saved worlds from skipping on `fresh`. Recomputing `CraftingRules` in `ApplyOreRules` changes recipes
  (not generation) only in a world whose admin asked for the ore one-shot.

### F3. `/earth checkrivers` and the self-test audit rebuild the rivers without the flow file's rerouted mouths (medium, VERIFIED)

- `src/EarthCommands.cs:501`: `var source = Params.RiverSource(Data, Elevation?.Dams, out _, out _);` This is the
  four-argument overload, `src/EarthParams.cs:645-646`, which passes `flow: null`.
- The model was built with the flow: `src/EarthParams.cs:616` `RiverSource(data, dams, ..., FlowRules >= 1 ? flow : null, ...)`,
  and `:663` `reroutes = flowFits ? RiverFlowData.Reroute(source, flow) : -1` changes the course list.
- The doc at `EarthParams.cs:642-643` says "BuildModel and the river audit both start here, so the audit walks the
  network that was generated". That is no longer true in a world with `FlowRules >= 1` (every default full-GLO-90 world
  since rc.8). The audit then walks un-rerouted mouth reaches against a model whose channels were made from the rerouted
  ones: false breaks at those mouths are possible, and real ones can be missed. The self-test runs it
  (`SelfTest.cs:1430`), so it is part of the release check.
- `EarthElevationSystem` keeps the flow (`RiverFlowData flow;`, set in `LoadFlow`, `:330`) but has no accessor like
  `Dams` (`:296`).
- Fix: add `public RiverFlowData Flow => flow;` and call the seven-argument `RiverSource` with
  `Params.FlowRules >= 1 ? Elevation?.Flow : null` in `WriteRiverCheck`; better, one `EarthParams.RiverSourceAsBuilt(data, dams, flow)`
  used by both, and delete the four-argument overload.
- Risk: none for generation (diagnostics only).

### F4. Rules versions: about 65 saved fields tested at about 300 places in about 45 files (medium, structure; counts by grep)

Still CR-A6, grown. See section 2 for the table, the dead branches and the proposal.

### F5. `EarthParams`' header gives field numbers that are taken as "next free" (low, VERIFIED)

- `src/EarthParams.cs:10-12`: "1-53, 55-93, 95-106, 108, 109, 120-129 and 190-195 are taken; ... Next free: 110, 130, 196."
- 196 to 203 are taken (`:364` PerennialRules 196 ... `:407` CraftingRules 203). Next free are 110, 130, 204.
- Field-number hygiene is otherwise sound: 131 members, no duplicate, and no member removed, renumbered or retyped
  between `v1.0.2`, rc.1 (`eb322e4`), rc.12 and HEAD (extracted and diffed). 54 and 94 are documented as burnt.
- `tests/PlaytestReviewTests/Program.cs:355-356` checks only that numbers are unique. Nothing fails if a number is
  reused for another field after a removal, or a field's type changes.
- Fix: correct the header; add a pinned table test (number, name, type for all 131, append-only) to a fast test project.
- Risk: none.

### F6. Ground rules number 2 has had two meanings (low, VERIFIED in git; whether a world has it: PLAUSIBLE)

- The commit that is `2.0.0-rc.1` (`eb322e4`, 8 Nov 19:42:51) has `GroundColumns.RulesVersion = 2` meaning "v1 without
  the Baltic and Latvian prototype sets" (`git show eb322e4:src/Core/GroundColumns.cs`, line 353). `60d9bcd` reverted it
  to 1 two minutes later (19:44:43); rc.2 to rc.12 have 1.
- rc.13 uses 2 again for decision 0076 (`src/Core/GroundColumns.cs:374`): warm karst only, no vanilla bauxite strata,
  and `EarthCrafting.RulesFor(groundColumns >= 2, ...)`.
- A world made by a zip built from exactly `eb322e4` saved `GroundColumns = 2` and now gets 0076's ground in new chunks,
  with `CraftingRules` 0 (no bauxite strata and no bauxite-free refractory recipes).
- The author knows whether an rc.1 zip from that commit ever made a world. If none did, this is only a lesson for the
  policy: a rules number, once committed under a version string, is burnt like a protobuf number.
- Fix (only if such a world exists): none in code; recreate it. Policy: note burnt numbers next to each `RulesVersion`.
- Risk: none.

### F7. `EarthMapSystem` and `EarthParams` are still the god classes of CR-A4; a new world's params still have two writers (CR-A5) (medium, structure, VERIFIED)

- `src/EarthMapSystem.cs` is 1,172 lines (was about 1,100): river query API (`:109-200`), data provisioning (`:274-371`),
  `InitWorld` (`:379-445`), resolve/repair/runtime (`:452-596`), rule announcements (`:601-638`), model build (`:644-692`),
  model cache key, open, save and "what changed" (`:705-844`), new-world finish, tree catalogue (`:880-920`), Cylinder
  World checks, `ComputeParams` (`:953-1069`), client (`:1073-1152`), formatting.
- `src/EarthParams.cs` is a protobuf record that also holds the model builder (`BuildModel`, `RiverSource`, `BuildRivers`,
  `LoadPeaks`, `ConfigureClimateData`, `RepairSettings`: `:490-696`) and unsaved outputs (`DamCourseLevels`,
  `DamCourseError`, `FlowReroutes`).
- Two writers: `ComputeParams` sets `RegionalGeology`, `RegionalOres`, `OreRulesVersion`, `GeologyRulesVersion`,
  `RegionalFauna`, `FaunaRulesVersion`, `IncludeExtinctAnimals`, `GroundColumns`, `OreLayers`, `OreRules`,
  `VanillaFaunaRanges` from `earth.json` (`EarthMapSystem.cs:984-994`, `:1021`, `:1034-1037`), and
  `EarthWorldOptions.Freeze` overwrites every one of them (`EarthWorldOptions.cs:109-136`). The values `ComputeParams`
  wrote are dead.
- `RepairSaved` still calls `ComputeParams` for a saved world (`EarthMapSystem.cs:567`), so its "this new world" lines
  (`CylinderKeepsEarthSeam`, `:948`; `DataHas`, `:1120`) and its data switches (`d.SetPenmanAridity(true)`, `:1045`) run
  for an old save.
- The whole `EarthParams` goes to every client (`:254`), with `OreSettingsJson`, the data signatures and the spawn. The
  client reads the geometry, the vertical scale, `SeaLevel`, `NoiseSeed`, `ClimateRules`, `ClimateData`, `FineRain`,
  `AridityRules`, `GeothermalRules`, the weather fields and a few rules the client systems test.
- Proposed split (names kept on `EarthMapSystem` where other mods call by reflection: `RiverAt`, `RiverSpanAt`,
  `RiverFlowAt`, `RiverQueryVersion`, `LongitudeAt`, `SkyFollowsLongitude`, `Params`, `Model`, as thin forwarders):
  1. `WorldParamsFactory`: `ResolveParams`, `ComputeParams` and `Freeze` merged into one builder with one precedence
     (tab, then config, then default), plus a layout-only `Layout(cw, config)` for `RepairSaved`.
  2. `EarthDataProvisioning`: `LoadData`, `EnsureServerData`, `StoreFilesRead`.
  3. `TerrainModelBuilder` (static): everything from `EarthParams.BuildGeometry` to `BuildRivers`, with a result object
     for `DamCourseLevels`, `DamCourseError`, `FlowReroutes`. `EarthParams` becomes a plain record.
  4. `ModelCacheSession`: `ModelCacheInputs`, `OpenModelCache`, `SaveModelCache`, `WhatChanged` (140 lines that are one
     unit already).
  5. `SavedRulesCheck`: `AnnounceSavedRules`, `ReportDataMismatch`, `ReportMissingClimateData`, and F1's table.
  6. `EarthClientSession`: `OnParams`, the sea level, the sky; with a `ClientWorld` message of the fields listed above.
  7. `LoadRegionalTrees` to `EarthTrees`.
- Risk: refactor only if the builder keeps field-for-field output. Check: serialize the params of a new world for each
  play style before and after and compare bytes (`EarthWorldOptionsTests` already builds params). The client message is a
  network change, not a save change (client and server always have the same mod version).

### F8. Process-lifetime patches and statics that outlive a world (low, VERIFIED): CR-A9 stands, now six flags

- `static bool patched;` at `src/ClimateLapse.cs:22`, `src/EarthGrassSeason.cs:48`, `src/EarthGrassWinter.cs:27`,
  `src/EarthGroundMoisture.cs:38`, `src/SeasonTintAltitude.cs:26` and, new since the last review,
  `src/SnowMeltLight.cs:35` ("it stays for the process; it does nothing while no Earth world runs"). Each is gated by a
  static `active`/`on`/table that is cleared in `Dispose`, so a later vanilla world runs vanilla's code plus one
  early-out per call. Not a bug; two policies remain (the other dozen systems `UnpatchAll` in `Dispose`).
- `src/SnowMeltLight.cs:33` `internal static volatile bool Paused;` set by `/earth snowlight off`
  (`EarthCommands.cs:127`) is never reset: it stays off for every later world of the process.
- `src/BlockLayerAdjustments.cs:22` `TallGrassPatched` is set `true` by `Bound` (`PatchVanilla.cs:450`, `:458`) and never
  set back (`AridFillPatched` and `SoilFertilityPatched` have a `Before`). After a session where it bound, a later
  server in the same process that fails to bind still logs "Earth ground cover rules ..." (`WorldGenRules.cs:154`). Log
  only.
- The weather's patches are still outside `PatchVanilla` (`EarthWeather.cs:369-428`, `:986-1020`, `:1085`), each with its
  own try. `EarthWorldGen.cs:93` plus `BindVanilla` still builds its `PatchVanilla` by hand beside `PatchVanilla.For`.
- Fix: reset `Paused` and `TallGrassPatched` where the systems start (`SnowMeltLight.StartServerSide`; a `Before` on the
  TallGrass spec). For the six flags, either document "process-lifetime, gated" as the one allowed second policy in
  `PatchVanilla`'s header, or give `PatchVanilla` a refcounted owner as `EarthWeather.Apply/Release` has (`:361-455`,
  `users++` / `--users`), which is the correct pattern for a process that holds a client and a server.
- Risk: none for saves.

### F9. The mod is still not inert outside Earth worlds; test commands work everywhere (low, VERIFIED): CR-A13 stands in part

- `src/EarthMapSystem.cs:521-522` stores `earth-params` (`Enabled = false`) and an empty `earth-creating` into every save
  opened with Earth installed. `EarthWeather.MayBeEarthWorld` (`EarthWeather.cs:~131`) then reads "has earth-params" as
  "may be an Earth world" and claims the weather role in a vanilla world until WorldReady.
- Fixed since the last review: Earth's refractory recipes are taken out where `CraftingRules` is 0, vanilla worlds
  included (`EarthCrafting.Apply`, called before the `Enabled` test, `EarthMapSystem.cs:405`).
- Still unconditional: `assets/earth/patches/ground-*.json`, `pan-placers.json`, `fish-rivers.json`.
- Commands with no "is this an Earth world" test: `/earth boattest` (`EarthCommands.cs:185-191`, builds four 100-block
  canals), `/earth elevation download all` (`:166-178`; `EarthElevationSystem.StartAll`, `:572`, has no
  `Params.Enabled` test, so 8.6 GB can be started in a vanilla world), `/earth cost` (`:108-119`), `/earth snowlight`
  (`:120-130`), `/earth relight` (`:131-137`).
- Debug leftovers to move behind one switch or into `/earth dev ...`: `cost`, `snowlight`, `boattest`, `orelayers`,
  `census`. The console hint at `:47` lists five subcommands; `cost`, `snowlight` and `elevation download/cancel` also
  work from the console.
- Fix: store nothing in a save Earth leaves vanilla (the three "disabled" branches of `ResolveParams` can return an
  unsaved disabled record; `ExpectsEarth` already handles "no params"); gate `StartAll` on an enabled world.
- Risk: not storing disabled params changes one thing: "Earth added to an existing world" is decided again at each
  start, with the same answer (`NewWorldAtStart` is false). No Earth world is touched.

### F10. Systems still find out about the params by polling (low, VERIFIED): CR-A10 stands, and the pattern spread

- `src/EarthWeather.cs:92` (1 s `Watch`, `p == builtFor`), `src/EarthGrassSeason.cs:81` and `:101` (2 s),
  `src/EarthThunder.cs:78` (1 s), `src/EarthGrassWinter.cs:47` and `src/EarthGroundMoisture.cs:56` (5 s),
  `src/SnowMeltLight.cs:52` (0.5 s, also the place its patch is applied).
- Fix: `EarthMapSystem` raises `ParamsReady(EarthParams)` at the end of `InitWorld` (server) and of `OnParams` (client);
  the six systems subscribe in `Start`. Keeps the reflection surface as it is.
- Risk: none for saves; the weather starts up to a second earlier.

### F11. Stale comments and names that now contradict the code (low, VERIFIED)

- `src/EarthWorldOptions.cs:159-162`: the comment says "rules 2, 0063: on courses rules 4; rules 1 on courses rules 3";
  the code gives `DamRemovalData.NewestRules` (3) on courses 4.
- `src/EarthWorldOptions.cs:166`: `RiverOutlines` from literals `4 : 3 : 2 : 1` beside a comment that numbers them
  "version 3 ... version 2 ... version 4"; `RiverOutlineData.FileVersion` is not used, so a new outline version needs an
  edit here that nothing points to.
- `src/EarthMapSystem.cs:1017`: "the test profiles of decision 0031 also store the real rainfall (rules 3); the defaults
  keep rules 2". `src/Core/VerticalTerrainOptions.cs:23-25` says the opposite and is right: every menu-made world is
  `Experimental` (budget "110" is the default and "legacy" is not on the tab, in 1.0.2's `worldconfig.json` either). So
  every 1.0 and 2.0 world has `ClimateRules` 3, `FineRain` 1, `Summits` 3, `Glaciers` 1. The name `Experimental`, and
  "Experimental profile (decision 0031)" at `EarthParams.cs:214`, mislead.
- `src/EarthMapSystem.cs:1014`: `hydrologyQuery >= 3 ? ... : Math.Min(10, ...)`. `RequiredMods` refuses a new world below
  query version 3 (`RequiredMods.cs:25`, `:91`); the comment above the line says so. Dead branch.
- `src/EarthParams.cs`: per-field docs stop short of the newest value for `FineElevation` (doc 1-4, newest 5),
  `GeothermalRules` (doc: on/off, newest 3), `ShoreZoneRules` (newest 2), `Works1318` (newest 4), `OreLayers` (doc 1-2,
  newest 4), `OreRules` (doc 4, newest 5), `GroundColumns` (newest 2), `DamRules` (doc 1-2, newest 3), `RiverOutlines`
  (doc 1-2, newest 4), `LakeOutlines` (newest 2). The versions' meanings are in the Core classes; the record should point
  there, not half-repeat them.
- `src/CylinderCompat.cs:8-10`: "Cylinder World is a required dependency (0.11.11 or newer ...) ... missing ones are
  reported"; `modinfo.json` requires 1.0.4, and a missing member throws `MissingMemberException`.
- `src/EarthMapSystem.cs:15-18`: the class summary still lists "/earth" (now `EarthCommands`).
- `src/PatchVanilla.cs:52`: "checked against C:\VS-ref" (now `C:\VS mods\devtools\vs-ref`).

### F12. `MissingMemberException` from Cylinder World is turned into a refusal only around two calls (low, VERIFIED)

- `src/EarthMapSystem.cs:389-400` catches it around `ResolveParams` and `RepairSaved`.
- `src/EarthMapSystem.cs:869` reads `cw.SpawnX, cw.SpawnZ` for a warning's text in `FinishNewWorld`, outside the try: a
  renamed `SpawnX` crashes WorldReady with a raw exception. Still fail-closed (nothing is generated), but without the
  named line CR-W10 added.
- Fix: read the two values with a helper that returns "?" on a missing member, or widen the try to `InitWorld`'s body.
- Risk: none.

### F13. `/earth tp` and `/earth info` read built-in heights where a GLO-90 tile has not arrived (low, PLAUSIBLE)

- `src/EarthCommands.cs:410-412`: `var c = Model.GetColumn(x, z); int y = water ? c.WaterTop + 1 : Math.Max(c.Top, c.WaterTop) + 2;`
  on the main thread, whose `FineElevation.FetchAllowed` is cleared at RunGame (`EarthElevationSystem.cs:80`).
  `FineElevation.TileAt` (`Core/FineElevation.cs:394-403`) then counts a missed read and the column falls back to the
  2' grid.
- In a world that downloads tiles "as needed", a teleport into a square not yet on the machine lands at the 2' grid's
  height; the chunk then generates from the tile. The player can end inside the ground or in the air. The river query
  already guards this with `MissedReads` (`EarthMapSystem.cs:142-152`).
- Fix: compare `FineElevation.MissedReads` before and after in `TeleportTo` and `CmdInfo`; where it moved, say "heights
  here are approximate until the tile arrives" and teleport to `MapSizeY - 2` or let the game place the player.
- Risk: none.

### F14. "Is this start making an Earth world?" is computed in six places (low, VERIFIED)

- `EarthVerticalBootstrap.cs:31-32` (StartPre), `:70-72` (WorldReady), `EarthMapSystem.cs:74-84` (`ExpectsEarth`),
  `:246` (StartServerSide), `RequiredMods.cs:27-28`, `EarthWeather.MayBeEarthWorld`. Each combines saved params,
  `IsNewWorld`, world type, play style and a config read; the config is read three ways (`ConfigEnabled` with a default
  of `true` on error, `EarthConfigStore.Load`, `earthSystem?.Config?.Enabled == true`).
- Fix: one `WorldKind Kind(ICoreServerAPI)` (Earth saved / Earth new / not Earth / unreadable) computed once in StartPre
  and kept on `EarthVerticalBootstrap` (it starts first), read by the others.
- Risk: none if the function is `ExpectsEarth` as it stands.

### F15. `SelfTest` (1,459 lines) and `/earth`'s test helpers ship in the release DLL (low, structure)

- `src/SelfTest.cs` is a `ModSystem` loaded on every server; it is inert unless `earth.json` has `SelfTest.Enabled`
  (`:35-46`). Its probes (`AnalyzeArea` about 200 lines, `ProbeClimate` 110, `CheckWeather` 110, `WriteDigest` 135) are
  one class with no shared boundary. Split by probe into files of a partial class, or a `SelfTest/` folder of probe
  classes with a small runner; no behaviour change.

---

## 2. Rules versions

### 2.1 The table

Newest = the constant at HEAD. New world = what a world from the menu with the tab's defaults gets (full GLO-90,
ground columns, ores vanilla, nature 1318, biomes earth). 1.0 = what `v1.0.2` saves (fields from 90 up do not exist
there: 0). RC = values any 2.0 RC build could save (constants checked with `git grep` at each commit that set
`modinfo.json` to rc.1 ... rc.13). Branch sites are from a grep of comparisons, not a hand trace of each file.

| Field (proto no.) | Newest | New world | 1.0 | RC values | Branched in |
|---|---|---|---|---|---|
| Version (1) | 11 | 11 | 11 | 11 | nowhere ("nothing reads it") |
| TreeRulesVersion (37) | 2 | 2 | 2 | 2 | adopted, `WorldUpgrades.Regional` |
| GeologyRulesVersion (39) | 2 | 2 | 2 | 2 | `EarthGeology`, `RegionalGeologyCatalog` (>=2) |
| FaunaRulesVersion (42) | 4 | 4 | 2 | 4 | adopted; `EarthFauna` |
| OreRulesVersion (45) | 3 | 0 (ores vanilla) | 3 | 0/3 | record only (its own doc) |
| RiverFalls (51) | 1 | 1 | 1 | 1 | `EarthParams.BuildRivers` |
| RiverChannels (52) | 5 | 5 | 2 | 4 (rc.1-7), 5 | `SeaChannels` (`InPlace`, `NoExitsWithCoasts`, >=3) |
| Islands (55) | 3 | 3 | 3 | 3 | `Islands.cs` (>=2), `EarthParams` |
| RiverEnds (57), RiverDeltas (62), RiverWidths (63) | 1 | 1 | 1 | 1 | `EarthParams.BuildRivers`, `RiverNetwork` |
| ClimateRules (58) | 3 | 3 | 3 | 3 | `TerrainModel.Climate`, `EarthClimateHooks` (<1), `ClimateLapse` |
| MinSeaDepth (59) | 6 | 6 | 6 | 6 | `EarthParams.BuildModel` |
| LakeShores (60) | 1 | 1 | 1 | 1 | `EarthParams.BuildModel` |
| Coasts (61) | 1 | 1 (0 where a grid cell is under 32 blocks, e.g. 1:200) | 0/1 | 0/1 | `EarthParams.BuildModel` |
| GutterSeabed (64) | 3 | 3 (2 with a gutter) | 2/3 | 2/3 | `TerrainModel.SeamGutter` (>=1, >=2, >=3) |
| WeatherRules (69) | 7 | build's, every start | - | - | handshake, `EarthWeather` |
| ClimateData (70) | 4 | 4 | 1 | 2 (rc.1-10), 3 (rc.11-12), 4 | `EarthData.SetClimateFix` |
| RiverPoolLayout (73) | 11 | 11 | 10 (8/9 with an older Hydrology) | 11 | about 35 tests: `ChunkSettler`, `RiverSettling`, `TerrainModel.Rivers`, `RiverBlockPass`, `RiverNetwork`, `RiverPoolPlanner`, `TerrainChunkWriter`, `EarthMapSystem` |
| VanillaFaunaRanges (78) | 3 | 3 | 1 | 3 | `WorldGenRules`, `SpawnRangeGuards`, `EarthPlacement` (>=1 only) |
| FineRain (79) | 1 | 1 | 1 | 1 | `EarthParams.ConfigureClimateData` |
| Summits (80) | 3 | 3 | 3 | 3 | `TerrainModel.LandHeights`, `.NaturalColumn` (>=2), `KnownSummits` (>=3) |
| Glaciers (81) | 1 | 1 | 1 | 1 | `AlpineSurfacePass`, `EarthParams` |
| AlpineSurfaceRules (82) | 2 | 2 | 1 | 2 | `AlpineSurfacePass` (>=1 x5, >=2), `WorldGenRules` |
| AridRiverRules (84), StraitRules (85), ReservoirRules (86) | 1 | 1 | 1 | 1 | `TerrainModel.Rivers/.Climate`, `TerrainChunkWriter`, `AlpineSurfacePass`, `EarthTrees`, `EarthParams` |
| CreationOptionsVersion (87) | 1 | 1 | 1 | 1 | `EarthWorldOptions.Runtime`, `WorldUpgrades`, `EarthFauna`, `SpawnRangeGuards` |
| PlacementRules (91) | 1 | 1 | 0 | 0/1 | `EarthPlacement`, `EarthFauna`, `WorldGenRules`; adopted |
| SoilRules (92) | 3 | 3 | 0 | 3 | `BlockLayerAdjustments` (<1), `EarthGeology` (>=2, >=3), `WorldGenRules` |
| FineElevation (95) | 5 | 5 | 0 | 0/5 | `TerrainModel.LandHeights` (`FineRules` >=2, >=3, >=4, >=5), `EarthElevationSystem` |
| CoarseElevation (96) | 1 | 1 | 0 | 0/1 | `TerrainModel.LandHeights` |
| RiverCourses (97) | 4 | 4 | 0 | 0/4 | `RiverSourceRepository` (>=2, >=3, >=4), `DamRemoval`, `RiverNetwork`, `EarthMapSystem` |
| DetailRules (98) | 1 | 1 | 0 | 1 | `TerrainModel.Detail` |
| LakeOutlets (100) | 1 | 1 | 0 | 1 | `EarthParams` |
| SeaReferee (101) | 2 | 2 | 0 | 2 | `EarthParams`, `EarthMapSystem` (<2 warning) |
| LakeOutlines (102) | 2 | 2 | 0 | 1 (rc.1-7), 2 | `LakeOutlines.cs`, `EarthElevationSystem` (>=2) |
| RiverOutlines (103) | 4 | 4 | 0 | 3 (rc.1-7), 4 | `RiverOutlines.cs` (>=3), `EarthElevationSystem`, `EarthWorldOptions` (>=4) |
| GeothermalRules (104) | 3 | 3 | 0 | 2 (rc.1-12), 3 | `TerrainModel.Climate` (>=1), `SpringsAndPonds` (<1), `EarthVolcanoes` (>=2, >=3), `Volcanoes` |
| DamRules (105) | 3 | 3 | 0 | 2 (rc.1-7), 3 | `DamRemoval` (`Rules2` x4, `Rules3` x3), `EarthElevationSystem` |
| Works1318 (106) | 4 | 4 | 0 | 1 (rc.1-7), 2 (rc.8-9), 3 (rc.10), 4 | `TerrainModel.Coasts` (<3, >=4), `.NaturalColumn` (>=3), `Works1318.cs` (>=2, >=4) |
| DepositSet (107) | 1 | 1 | 0 | 1 (also inferred, `EarthDepositSet.Of`) | `EarthOres`, `EarthOreLayers` |
| TemperatureRangeFrozen (108) | 1 | 1 | 0 | 0/1 | `EarthMapSystem.ApplyRuntime` |
| GroundCoverRules (120), MoistureRules (122), GrassWinterRules (123), WetlandRules (125) | 1 | 1 | 0 | 0/1 | `BlockLayerAdjustments`, `EarthGroundMoisture`, `EarthGrassWinter`, `WorldGenRules` |
| ZoneRules (121) | 2 | 2 | 0 | 0/2 | `WorldGenRules` (>=1, >=2) |
| FireRules (124) | 2 | 2 | 0 | 0/2 | `WorldGenRules`, `FireRegime` (<2) |
| PondRules (126) | 2 = none (an option, not a version) | 2 | 0 | 0/1/2 | `SpringsAndPonds`, `WorldGenRules`, `EarthWorldGen` |
| ChannelRules (127) | 5 | 5 | 0 | 1 (rc.1-7), 3 (rc.8-9), 4 (rc.10), 5 | about 35 tests: `TerrainModel.Rivers`, `.NaturalColumn`, `RiverNetwork`, `RiverCoursePreparer`, `RiverSegmentBuilder`, `EarthElevationSystem`, `WhereReport` |
| AridityRules (128) | 1 | 1 | 0 | 1 | `EarthParams.ConfigureClimateData`, `WorldGenRules` |
| SpringRules (129) | 2 = streams (an option) | 1 | 0 | 0/1/2 | `SpringsAndPonds`, `EarthSprings`, `EarthWorldGen`, `WorldGenRules` |
| GroundColumns (190) | 2 | 2 | 0 | 0/1 (rc.2-12), 2 (rc.13; see F6) | `EarthGeology` (>=2 x5), `GroundColumns.Place.GroundRules` |
| GroundData (192) | 2 | set by the ground download | 0 | 0/1/2 | `EarthGeology`, `GroundDownloader` |
| OreLayers (193) | 4 | 4 | 0 | 0/1 (rc.1-10), 2 (rc.11-12), 4 | `OreLayers.cs` (<1, <2, <3), `EarthGeology`, `EarthOreLayers` |
| WaterTableRules (194) | 1 | 1 | 0 | 1 | `EarthWaterTable` |
| OreRules (195) | 5 | 0 (ores vanilla), 5 with the ore maps | 0 | 0/4/5 | `OreDeposits` (<4), `EarthOres`, `OreSession` (>=4), `EarthOreDeposits` (>=5) |
| PerennialRules (196), FlowRules (197) | 1 | 1 | 0 | 0 (rc.1-7), 1 | `EarthParams`, `TerrainModel.Rivers`, `EarthElevationSystem` |
| ShoreZoneRules (198) | 2 | 2 | 0 | 0 (rc.1-7), 2 | `ShoreZones` (>=2), `AlpineSurfacePass`, `EarthTrees` (>=1) |
| ShoreRules (199) | 3 | 3 | 0 | 0 (rc.1-7), 2 (rc.8-10), 3 | `TerrainModel.Shore` (<2 x3, >=2 x2), `GroundColumns` (>=3), `BlockLayerAdjustments`, `EarthWorldGen` (<1) |
| GenFixRules (200) | 3 | 3 | 0 | 0 (rc.1-7), 1 (rc.8-9), 2 (rc.10-12), 3 | `TerrainModel.NaturalColumn` (<2, <3, >=2), `AlpineSurfacePass`, `StreamSprings`, `EarthPlacement`, `EarthPlants`, `TerrainModel` (>=1) |
| WeatherModelRules (201) | 7 | 7 | 0 (runs 5) | 0 (rc.1-9), 6 (rc.10-12), 7 | `WeatherModel` (<6, >=6, `GroundRulesFrom`, `FogRulesFrom`), `EarthWeather`, `EarthClimateHooks`, `Thunder` (`DryRulesFrom`) |
| VanillaBiomes (202) | 1 (an option) | 0 | 0 | 0/1 (rc.12+) | `TerrainModel`, `WorldGenRules`, `WorldUpgrades` |
| CraftingRules (203) | 1 | 1 | 0 | 0, 1 (rc.13) | `EarthCrafting.Apply` |

Booleans and options frozen the same way, not versions: `Rivers`, `RegionalTrees/Geology/Fauna/Ores`,
`IncludeExtinctAnimals`, `GutterBridges`, `LinearLand`, `LocalAlpineEcology`, `PhysicalRockExposure`, `NoRiverRapids`,
`ChickensByRange`, `Weather`, `SummitMinElevation`, `RockFloorDepth`, `Canals`, `StreamMode`, `GroundDepth`,
`FaunaRangeProfile`.

### 2.2 Defaults: "0 = old save" against a new world

Checked for each field: 0 (or null, false) means the behaviour before the field existed, never something a new world
would want by accident. Where the new default is "off", the field is inverted or uses a non-zero code:
`NoRiverRapids`, `ChickensByRange` (false = old), `PondRules` 2 = none, `SummitMinElevation` -1 = off / 0 = all peaks,
`MinSeaDepth` 0 = 5, `WeatherModelRules` 0 = rules 5, `GroundDepth` null = "log4000". No field found where a missing
value and a deliberate new-world value collide. Two notes:

- `GroundData` 0 means "reads whatever 30-second sets the machine has" (`EarthParams.cs:264-267`): for worlds made
  before the field, generation depends on the machine. Known and documented; no fix without changing those worlds.
- `Works1318`, `ShoreRules`, `PerennialRules` and the like are saved at their newest in every new world, also where
  they cannot apply (`Works1318` 4 with `Coasts` 0 at 1:200). The saved record is "rules on offer", not "rules in
  effect"; `/earth where` and the log have to work the second out again each time. The policy object below should hold
  the effective rules.

### 2.3 Branches that only serve values no 1.0.x or RC world can have

Assumption to confirm with the author: the oldest worlds 2.0 must keep right are 1.0.0 to 1.0.2 worlds and worlds from
2.0.0-rc.1 on. 0.12 to 0.16 (October, never tagged) and the "tall" and pre-rc dev lines are not.

Dev-only values (between two RCs, or on the tall line), so the test that tells them apart is dead:

- `FineElevation` 1 to 4: `src/Core/TerrainModel.LandHeights.cs:54-55` (`FineRules >= 4`, `>= 3`), `:81` and `:55`
  (`FineRules >= 5`), `:172` (`FineRules < 2`). Every RC world has 0 or 5.
- `RiverCourses` 1 to 3: `RiverSourceRepository.cs` (`>= Glo90SnapCourses`, `>= Glo90ArmsCourses`),
  `EarthMapSystem.cs:362-363` and `:508-509`, `DamRemovalData.CourseRules` (3) with dam rules 1
  (`EarthWorldOptions.cs:162`). With them the store files `rivers-glo90-snap.bin` (4.5 MB) and `rivers-glo90-arms.bin`
  are read by no world, yet every courses-4 world must have them in place (`StoreFilesRead`).
- `DamRules` 1, `RiverOutlines` 1 and 2: the `>= 1 ? 2 : 1` arms at `EarthWorldOptions.cs:166`; `DamRemoval`'s
  rules-1 paths where they differ from `Rules2`.
- `SeaReferee` 1 (its own doc: "saved only by tall.14's test worlds"): the warning at `EarthMapSystem.cs:628-630`.
- `ShoreRules` 1: `TerrainModel.Shore.cs` (`ShoreRules < 2` x3, `>= 2` x2) can read `>= 1`.
- `ShoreZoneRules` 1: `ShoreZones.cs` (`>= 2`).
- `SoilRules` 1 and 2: `EarthGeology.cs` (`soilRules >= SoilFertility.GroundRules`, `>= FloodplainRules`),
  `WorldGenRules.cs:116`, `EarthMapSystem.cs:368` can read `>= 1`.
- `ZoneRules` 1 (`WorldGenRules.cs:105`), `FireRules` 1 (`FireRegime` `rules < 2`, `WorldGenRules.cs:135`),
  `GeothermalRules` 1 (`EarthVolcanoes` `>= 2`), `OreLayers` 3 (`OreLayers.cs` `Version < 3`), `ChannelRules` 2 (no dead
  test: 1 and 3 are both RC values), `VanillaFaunaRanges` 2 (no test).
- `ComputeParams`' non-`Experimental` arms (`EarthMapSystem.cs:1018`, `:1021-1023`, `:1047-1048`): only the hand-typed
  "legacy" profile reaches them.
- `EarthMapSystem.cs:1014`: the Hydrology query < 3 arm (F11).

Pre-1.0 only (0.12 to 0.16 worlds with Cylinder World; they still load today, so these are live until a floor refuses
such worlds):

- `RiverPoolLayout` 0 to 7 (and 8, 9 unless a 1.0 world was made with Hydrology query 0 or 1): about 25 of the 35
  tests. With a 1.0 floor the planner needs "10" and "11" only.
- `GutterSeabed` 0 and 1, `ClimateRules` 0 and 1 (`EarthClimateHooks` `< 1`), `Summits` 1 and 2 (`SummitRules < 2`,
  `KnownSummits` `>= 3`), `Islands` 1 and 2, `RiverChannels` 1, `MinSeaDepth` 0, `LakeShores` 0, `RiverEnds` /
  `RiverDeltas` / `RiverWidths` / `RiverFalls` 0, `AridRiverRules` / `StraitRules` / `ReservoirRules` 0,
  `GutterBridges` false, `LinearLand`/`LocalAlpineEcology` false, `StreamMode == null` (`EarthParams.cs:688-689`).
- `CreationOptionsVersion == 0`: `EarthWorldOptions.Runtime` (`:398-405`), `WorldUpgrades.cs:351-356`, `EarthFauna`,
  `SpawnRangeGuards`.
- Nine of the eighteen `UpgradeExistingWorld*` flags can only change a pre-1.0 world (RiverFalls, RiverEnds,
  RiverDeltas, RiverWidths, SeaDepth, LakeShores, GutterBridges, WaterTerrain, and Islands' rules step): in every 1.0 and
  RC world they log "nothing to do". The repo guide says "Earth 2.0 is for new worlds: don't build upgrades for older
  saves".

Must stay (a 1.0 or RC world has the value): `AlpineSurfaceRules` 1 against 2, `RiverChannels` 2/4/5, `ClimateData`
1/2/3/4, `PoolLayout` 10 against 11, `ChannelRules` 1/3/4/5, `GenFixRules` 0/1/2/3, `Works1318` 1/2/3/4, `ShoreRules`
0/2/3, `OreLayers` 0/1/2/4, `OreRules` 0/4/5, `GeothermalRules` 0/2/3, `LakeOutlines` 1/2, `RiverOutlines` 3/4,
`DamRules` 2/3, `WeatherModelRules` 5/6/7, `GroundColumns` 0/1/2, `TemperatureRangeFrozen` 0/1, every 0 of a field
1.0 lacks.

### 2.4 Proposal: one immutable rules object, made once, that cannot change a saved world

1. `WorldRules.From(EarthParams p, EarthData data)` builds a read-only record once per start, after the one-shots and
   before the model. It holds named facts, not numbers: `rules.Rivers.CalmSources` (pool layout >= 11),
   `rules.Channels.OneBedAtMouths` (>= 3), `rules.Shore.BeachAtWaterLevel` (>= 2), `rules.Fixes.GridLakeEdge`
   (GenFix >= 3), and the effective state where rules depend on each other or on data (`Works1318` needs coasts; `Fire`
   needs zones and soils; `OpenWetlands`). One sub-record per family, as `RiverPoolRules` already is for the pool
   planner: `Rivers`, `Channels`, `Shore`, `Heights`, `Climate`, `Ground`, `Ores`, `Biomes`, `Weather`, `Fixes`.
2. The mapping from saved numbers to facts lives in that one file, each line with the version that introduced it.
   Call sites test `rules.X.Fact`. `TerrainModel`'s raw `*Rules` ints (`ChannelRules`, `ShoreRules`, `GenFixRules`,
   `AlpineSurfaceRules`, `GeothermalRules`, `SummitRules`, `FineRules`, ...) go away in favour of the record
   (this is CR-A3's "immutable rules record at build").
3. Safety net that proves no saved world changes: a table test over every saved combination that exists
   (1.0.2's values; each RC's values from the table above; the tab's options) asserting the facts each yields, written
   from today's code before any call site moves. Then move call sites family by family; after each family the offline
   digests (`SelfTest.Digest`, `EarthMapTests`) must be byte-equal for one 1.0-valued and one rc-valued parameter set.
4. `WorldRules.Newest` is the one list of this build's newest numbers: `Freeze` reads new-world values from it (no more
   literals like `4 : 3 : 2 : 1`), F1's "saved above newest" refusal reads it, the per-field docs in `EarthParams`
   shrink to a pointer.
5. A declared floor: `WorldRules.From` refuses a combination below it with one line ("made by Earth 0.x; open it with
   the version that made it"), as `ResolveParams` already refuses worlds without Cylinder World. Only then delete the
   pre-1.0 arms of 2.3. The dev-only arms can go at once: by the git record no released or RC build could save those
   values (one exception to settle first: F6).
6. Three kinds of field, said in the record: frozen (never changes for a world), adopted (`TreeRulesVersion`,
   `FaunaRulesVersion`, `OreRulesVersion`, `PlacementRules`: `WorldUpgrades.Regional` moves them up at every start
   without a flag, `WorldUpgrades.cs:330-345`), and per-start (`WeatherRules`, `RealSeasons`, the weather handshake).
   The adopted ones are today the only way generation in a saved world changes without the admin asking; a bump of any
   of the four constants reaches every RC world. Either keep that on purpose and write it down, or freeze them too.

Risk of the whole proposal: zero for saved worlds if step 3 is done first and holds; the fields and their numbers are
untouched.

---

## 3. Checks of earlier findings

| Id | State at rc.13 | Evidence |
|---|---|---|
| CR-A4 (god class, params mix, whole params to clients) | holds, slightly worse | F7; `EarthMapSystem.cs` 1,172 lines; `:254` |
| CR-A5 (two writers; repair reuses ComputeParams) | holds | F7; `EarthMapSystem.cs:567`, `:984-1037`; `EarthWorldOptions.cs:109-136` |
| CR-A9 (patching not uniform; process-lifetime flags) | holds; six flags now | F8 |
| CR-A9 (grass lookups warn once) | still fixed | `EarthGrassSeason.cs:140-146`, `:304-308` |
| CR-A10 (1 Hz poll for the params) | holds, more pollers | F10 |
| CR-A13 (not inert in vanilla worlds) | holds in part; recipes now gated | F9 |
| CR-A14 (weather contract version without a rule) | holds | `Core/WeatherContract.cs:27` `public const int Version = 1;`, no compatibility rule in the header |
| CR-L1 (failed config write; start-up that ended early) | still fixed | `EarthConfigStore.cs:110-111`; `EarthMapSystem.cs:409-410`; `EarthWorldGen.cs:449-452`. Checked against `ModLoader.TryRunModPhase` (1.22.7): a system whose `StartServerSide` throws is logged and dropped from `Systems`, so `GetModSystem<EarthWorldGen>()` is null and `InitWorld` refuses; `EarthWorldGen` holds `core` from before the drop. A hole needs both systems to throw before their registrations |
| CR-L2 (`/earth` from the console) | still fixed | `EarthCommands.cs:44-48`; subcommands that read a position have `RequiresPlayer` |
| CR-L3 (model build failure ends the step) | still fixed | `EarthMapSystem.cs:663-674`; `DataProblem` also unwraps `AggregateException` from the multi-core build (`:696-703`) |
| CR-L4 (client sea level put back) | still fixed | `EarthMapSystem.cs:1106-1107`, `:1139-1143`, `:1150`. In single player the value "before" is the one the server side already set; equal to vanilla's static default (110) with the default budget |
| CR-L5 (`/earth tp NaN`) | still fixed | `EarthCommands.cs:363` |
| CR-L6 (`/earth where` decoding the regions) | still fixed | `WhereReport.cs:51-58` |
| CR-W10 (Cylinder World member missing) | still fixed for resolve and repair; one read outside | `EarthMapSystem.cs:394-400`; F12 |
| CR-W11 (AstraTerra private field) | still fixed | `AstraTerraLongitude.cs:33-46`, `Unreadable` warns once |

---

## 4. Model cache: does the key cover every input? (VERIFIED for the inputs listed)

`EarthMapSystem.ModelCacheInputs` (`:724-756`) hashes: a layout string, the DLL's module version id, the mod version,
the identity of the data files the mod carries (`EarthDataset.DataIdentity`: zip entries by name, length and CRC),
runtime, architecture and OS, whether the dam file and the flow file are in use, and the whole `EarthParams` minus
fields set after the build.

- New rules fields are covered without anyone remembering to add them: the params are serialized whole and fields are
  taken out, not put in. A future field is in the key by default. This is the right way round.
- The fields taken out (`:727-735`) are each set after the cached steps or never read by them: spawn, `StoryPlaced`,
  `Zones` (added to the model last, `EarthParams.cs:628-634`), the three signatures, the weather handshake,
  `GroundData`, `DepositSet`. Checked against `BuildModel`: the two cached parts are `NavigableStraits.FollowSea`
  (`:613-614`) and `SeaChannels` ways (`SeaChannels.cs:332`); neither reads those.
- Store files are pinned by the build: `EarthDataStore.Missing` hashes every file a world reads at each start against
  the SHA-256 compiled into the DLL, and the DLL's id is in the key.
- The dam and flow files enter as booleans. Sound: both are checked against a compiled SHA-256 before use
  (`EarthElevationSystem.cs:314-315`, `:339`), and a world that keeps the dams for good gets `dams == null`, which is in
  the key.
- GLO-90 tiles are not an input: the model is cached before `AttachServer` gives it the tiles (`:675-677`).
- `ModelCache.Use` is called from one thread (both call sites are sequential in `BuildModel`); its dictionary and lists
  are not thread-safe and need not be. Note it in the class doc so the multi-core build never moves a `Use` into a
  parallel loop.
- Failure paths are closed: a damaged or foreign file is ignored and rebuilt (`ModelCache.cs:197-217`), a part the
  reader rejects is rebuilt (`:108-121`), writing is atomic (`:141-151`), and `SaveModelCache` cannot stop the start
  (`EarthMapSystem.cs:775-782`).
- One soft spot (low): for an unpacked mod folder `DataIdentity` uses name, length and modification time
  (`EarthDataset.cs:592-593`), not content. A dev folder whose file was replaced with the same size and time would hit
  a stale cache. Developer machines only.

---

## 5. Good

1. Fail-closed start-up is now cross-checked from both sides and matches the engine's behaviour: `ExpectsEarth`,
   `EarthWorldGen.Started`, `Refuse` in both systems, the `earth-creating` marker for a first start that stopped, and
   the "nothing is saved before RunGame" reasoning in `WorldUpgrades.Hook`. I found no single-fault path that lets
   vanilla terrain into an Earth world.
2. `PatchVanilla`: every member resolved and signature-checked before anything is patched (`Mismatch`), features bound
   all-or-nothing with undo, a second way per feature, one log line naming the game version, the member and what
   vanilla does instead, and a test project that sees its internals.
3. The model cache (section 4): key by exclusion, self-checking file, atomic write, and a "what changed" line.
4. `EarthParams` field numbers have never been reused or renumbered across 1.0.2, rc.1, rc.12 and HEAD, and every
   new field states which number was free and when.

---

## 6. Top findings

1. F1 medium: saved rules newer than the build run older rules, mostly silently. `src/EarthParams.cs:583`, `:596`; `src/EarthMapSystem.cs:601-612`.
2. F2 medium: one-shot upgrade flags rewrite a world created this start and leave `CraftingRules` and others stale. `src/EarthMapSystem.cs:416-422`; `src/WorldUpgrades.cs:360-375`.
3. F3 medium: the river audit rebuilds the network without the flow file's reroutes. `src/EarthCommands.cs:501`; `src/EarthParams.cs:645-646`.
4. F4 medium: about 65 rules fields tested at about 300 places; dead dev-only and pre-1.0 arms; proposal in 2.4. e.g. `src/Core/TerrainModel.LandHeights.cs:54-55`.
5. F7 medium: `EarthMapSystem` (1,172 lines) and `EarthParams` unsplit; two writers of a new world's params. `src/EarthMapSystem.cs:953-1069`; `src/EarthWorldOptions.cs:92-180`.
6. F5 low: header says "Next free: ... 196"; 196-203 are taken; no pinned-table test. `src/EarthParams.cs:10-12`.
7. F6 low: ground rules number 2 meant something else in the rc.1 commit. `src/Core/GroundColumns.cs:374`; `eb322e4`.
8. F8 low: six process-lifetime patch flags; `SnowMeltLight.Paused` and `TallGrassPatched` never reset. `src/SnowMeltLight.cs:33`, `:35`; `src/BlockLayerAdjustments.cs:22`.
9. F9 low: disabled params written into vanilla saves; `/earth boattest` and `/earth elevation download all` work in any world. `src/EarthMapSystem.cs:521-522`; `src/EarthCommands.cs:163-191`.
10. F11 low: comments that contradict the code (dam rules, river outline literals, "Experimental", dead Hydrology fallback). `src/EarthWorldOptions.cs:159-166`; `src/EarthMapSystem.cs:1014-1018`.
