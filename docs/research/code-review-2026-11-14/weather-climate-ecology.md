# Earth review, area: weather, climate and ecology (14 November 2026)

Read-only review of `C:\VS mods\Earth` at `main` `6371e32` (2.0.0-rc.13). Nothing was built, run or changed.
Vanilla call chains were read in `C:\VS mods\devtools\vs-ref\decompiled\1.22.7`.

Status words: **VERIFIED** = the code path was read end to end (Earth and vanilla). **PLAUSIBLE** = the code was
read, the consequence or the size of the cost is argued, not measured. All cost figures are estimates from counting
operations; none was measured here. "Bits" = whether the fix changes weather or generation for saved worlds.

Shorthand: one `Rain` = one call of `WeatherModel.Rain` (`src/Core/WeatherModel.cs:487-545`): `Place`, three
`Advected` fields (each up to 2 phases x 2 `Value` x 8 hashed corners), `Storms.Influence`, about 15 `PortableMath`
calls for the rain chance and the thunder threshold. EA-0070 measured `TemperatureAnomaly` (one `Rain` plus one more
`Advected`) at 2.2-3.3 us, so one `Rain` is taken as about 2 us below.

---

## 1. Performance: hot paths

### W-1. The client computes Earth's fog four times every frame (new with rules 7) — high — VERIFIED path, cost PLAUSIBLE

- Where: `src/EarthWeather.cs:587-609` (`RegionFogPostfix`), `src/Core/WeatherModel.cs:595-623` (`FogAt`).
- Evidence. The postfix sits on `WeatherSimulationRegion.UpdateWeatherData`. Vanilla calls that for the four regions
  round the player from the render loop: `WeatherSystemClient.cs:153`
  `WeatherDataAtPlayer.UpdateAdjacentAndBlendWeatherData()` → `WeatherDataReaderBase.cs:123-126` (four
  `UpdateWeatherData()`), in `OnRenderFrame` at stage `Before`, so once a frame. Each postfix runs
  `m.FogAt(...)`, and `FogAt` is three `Rain` calls and a wind:
  `Rain(x, z, hours, ref s, true, ...)` (`:599`), `Wind(...)` (`:614`), and
  `Math.Max(Cloudness(x, z, hours - FogRainHours), Cloudness(x, z, hours - 2 * FogRainHours))` (`:617`).
  So 12 `Rain` + 4 drift winds + 4 `GetRainMapHeightAt` per frame: about 25-30 us a frame, 1.5-2 ms per second at
  60 fps and proportionally more at higher frame rates, on the render thread. The two look-back calls at
  `hours - 9` and `hours - 18` also touch other storm slots than "now", which defeats `SlotCache`'s one-entry
  `last` (see W-9).
- `/earth cost` does not see this (it counts only the climate hook).
- Fix: cache the fog per region on the client. A small dictionary keyed by `(regionX, regionZ)` holding the
  `WeatherFog`, the ground height it was made for and the `TotalHours` it was made at; recompute when
  `|TotalHours - made| > 0.02` game hours (about 1 s of real time at default speed), when the player's ground
  height changes by more than a few blocks, or when `EarthWeather.Revision` changes. The postfix then only writes
  the three ambient values.
- Risk: none for bits. Fog is drawn on the client only (`:589` returns on the server); the model function is
  unchanged. No rules version.

### W-2. CR-W1 still holds and got dearer: every `GetWindSpeedAt` runs vanilla's handler first, whose result Earth overwrites — high — VERIFIED

- Where: `src/EarthWeather.cs:98` and `:108` (subscription "after vanilla's handler"), `:699-721`
  (`OnGetWindSpeed`), `:724-737` (`RegionWindPostfix`).
- Evidence. `EventManager.TriggerOnGetWindSpeed` (`VintagestoryLib/Vintagestory.Common/EventManager.cs:113-133`)
  calls every handler in order. Vanilla's is `WeatherSystemBase.Event_OnGetWindSpeed`
  (`WeatherSystemBase.cs:62-65`): `windSpeed.X = WeatherDataSlowAccess.GetWindSpeed(pos)`. That runs
  1. `LoadAdjacentSimsAndLerpValues` → `LoadLerp` → `ws.GetClimateFast(tmpPos, climate)`
     (`WeatherDataReaderBase.cs:~115`) → `GetClimateAt(pos, int)` → `TriggerOnGetClimate(NowValues)`
     (`ClientWorldMap.cs:1190`, `ServerWorldMap.cs:594`). That is vanilla's rain handler → Earth's
     `RainCloudnessPostfix` (one `Rain`) and Earth's climate hook: `ClimateCorrection`, `SeasonCorrection`, and with
     rules 6+ `TemperatureAnomaly` (another `Rain` + one `Advected`);
  2. `pgetWindSpeed` → four `WeatherSimulationRegion.GetWindSpeed` → four `RegionWindPostfix` → four
     `m.WindAt(..., false)`.
  Then Earth's own handler runs one `m.WindAt(..., true)` and writes `windSpeed.X` and `.Z`, discarding all of
  the above. Per `GetWindSpeedAt`: 5 `WindAt` (7 `Storms.Influence` in all) + 2 `Rain` + the climate hook, one
  `WindAt` used. The previous review counted "5 WindAt + 1 Cloudness"; rules 6 added the anomaly to the thrown-away
  part.
- Callers: `WeatherSystemClient.OnRenderFrame` twice a frame (`WeatherSystemClient.cs:155`, `:164`), every weather
  particle spawn (`WeatherSimulationParticles.cs:321`), `EntityBehaviorBodyTemperature.cs:243`, every cloth system
  tick (`ClothSystem.cs:464`).
- Fix: a Harmony prefix on `WeatherSystemBase.Event_OnGetWindSpeed` (private; bind by name through `PatchVanilla`,
  as a feature of the weather table) that returns `false` exactly when Earth's handler will answer: this side's
  model is not null, `ControlsWind`, `pos != null`. When the patch is refused, behaviour is today's.
- Risk: none for bits (Earth's handler overwrites X and Z in every case where the prefix skips; when Earth's
  handler throws, the wind would be 0 for that call where it is vanilla's today, so keep the skip decision and
  Earth's computation in one place: let the prefix itself write Earth's wind and unsubscribe the event handler).
  No rules version.

### W-3. The region winds are recomputed per region per tick and four times per slow-access lookup — medium — VERIFIED

- Where: `src/EarthWeather.cs:724-737`.
- Evidence. `WeatherSimulationRegion.TickEvery25ms` ends with `GetWindSpeed(ws.api.World.SeaLevel)`
  (`WeatherSimulationRegion.cs:292`), on the server for every loaded map region every server tick
  (`WeatherSystemServer.cs:205-211`), on the client for the four regions round the player. Windmills
  (`BEBehaviorWindmillRotor.cs:78`), butterflies and the axe go through `WeatherDataSlowAccess.GetWindSpeed`,
  four region lookups each. Each lookup is a full `WindAt` at the region's centre and the current hour.
- Fix (bit-identical): memoise per region `(regionX, regionZ)` the un-scaled strength keyed by the exact
  `TotalHours` double; within one tick every asker gets the cached value (`AltitudeScale` stays per call). The
  calendar's `TotalHours` is constant within a tick, so N windmills cost 4 `WindAt` a tick, not 4N.
- Risk: none (same inputs, same outputs).

### W-4. Cloud tiles: one full `CloudCover` per tile per pass on the cloud thread (new with rules 7) — medium — VERIFIED path, cost PLAUSIBLE

- Where: `src/EarthWeather.cs:617-629` (`CloudThicknessPostfix`).
- Evidence. `CloudRenderer.UpdateCloudTilesOffThread` (`CloudRenderer.cs:~293-325`, and the same in
  `FluffyClouds/CloudRendererMap.cs:412`) calls `wreaderpreload.GetBlendedCloudThicknessAt` for every tile on
  each pass; `QuantityCloudTiles = CloudTileLength²` with `CloudTileLength = Clamp(viewDistance / 50, 20, 200)`
  (`CloudRenderer.cs:260-261`), so at least 400 tiles, 1,600 at a 2,000-block view. The thread loops with
  `Thread.Sleep(40)` and runs a pass whenever the main thread has taken the last one. Each postfix call is
  `m.CloudCover(...)` = one `Rain` with `full: true`. About 1 ms a pass at 400 tiles, up to 25 passes a second.
- Threading: the postfix runs on vanilla's `cloudTileUpdThread`, not the main thread. What it touches is safe
  (`model` is volatile, `SlotCache` is concurrent, `HookFailed` uses a `ConcurrentDictionary`); it reads
  `api.World.Calendar.TotalHours` off-thread, so tiles within one pass can be a tick apart, which does not show.
- Fix: a per-thread cache keyed by `(cloudTileX, cloudTileZ)` (a flat array over the tile window, or a
  `Dictionary<long, (double hours, double cover)>` trimmed when it grows) that recomputes a tile's cover when it
  is older than about 0.02 game hours. Cloud cover moves over tens of minutes of game time; tiles already blend
  toward their target over several passes.
- Risk: none for bits (client drawing only). No rules version.

### W-5. `Storms.Influence` works out every storm's intensity before it rejects by distance (CR-W2's storm half still holds) — medium — VERIFIED

- Where: `src/Core/WeatherStorms.cs:320-326`, `:60-70`.
- Evidence.
  ```
  foreach (var s in AliveIn(wh)) {
      if (!s.AliveAt(wh)) continue;
      s.At(wh, out double clat, out double clon, out double inten);   // Pow(Sin(pi a), 1.2): Sin + Log + Exp
      if (inten <= 0) continue;
      double dn = (lat - clat) * 111.32;
      if (Math.Abs(dn) >= s.RadiusKm) continue;
  ```
  With the documented rates (about 1,085 storms a year, lives of 48-240 h) roughly ten storms are alive at once,
  and `Influence` is called from every `Rain`, `WindAt` and the contract's `storm`. Nearly all are rejected by the
  latitude test, after the three `PortableMath` calls were paid.
- Fix A (bit-identical): split `Storm.At` into the position (two lerps) and the intensity; reject by `dn` (and by
  `r`) first, compute the intensity only for storms in reach. The values that survive are the same expressions.
- Fix B (bit-identical, larger): a per-thread direct-mapped memo keyed by the exact `wh` bits (for example 1,024
  slots) of "storms active at wh": an array of `(Storm, clat, clon, inten)` with `inten > 0`. Within a tick every
  live call shares one `wh`; in vanilla's snow catch-up the eight corners of a region ask the same run of hours
  one after the other (`WeatherSimulationRegion.cs:177-205`: corners outside, hours inside), so corners 2-8 hit.
  Key the memo to the model instance (a counter, as `WeatherData.cacheId` does) so a new world misses.
- Risk: none for bits; `At` is a pure function of the storm and `wh`.

### W-6. The live climate reading computes the rain twice, half a block apart, and `/earth cost` sees only one half (EA-0073) — medium — VERIFIED

- Where: `src/EarthClimateHooks.cs:61-72`, `src/EarthWeather.cs:507-517`, `src/Core/WeatherModel.cs:760-778`,
  `src/ClimateCost.cs:30-45`.
- Evidence. For every climate reading that is not `WorldGenValues`, vanilla's handler runs first
  (`WeatherSystemBase.cs:139-147`): `GetRainCloudness(climate, pos.X + 0.5, pos.Z + 0.5, totalDays)`, which
  Earth's postfix answers with `m.Cloudness(posX, posZ, ...)`: one `Rain` at the block's centre. Then Earth's hook
  runs `w.TemperatureAnomaly(pos.X, pos.Z, ...)`: a second `Rain` at the block's corner, same hour. The two
  can't share a result because the coordinates differ by 0.5. `ClimateCost.Count` wraps only Earth's `Adjust`,
  so the measured "4-8 ms per second, 82 % the anomaly" leaves out an about equal cost in the cloudness postfix.
- Inside `TemperatureAnomaly` one piece is plainly redundant: `:767`
  `SteeringBlocks(s.Lat, Data.ItczLat(s.Lon, s.YearRel), out double vx, out double vz);` repeats what `Rain`
  computed at `:490` and `:501` with the same inputs.
- Fixes, in order of how safe they are:
  1. (bit-identical) Let the private `Rain` hand back `vx, vz` (an overload or two more `out`s) and drop `:767`.
     Saves an `ItczLat`, a `PrevailingAt` chain and a `Cos` per anomaly.
  2. (bit-identical) W-5's storm memo: it serves both `Rain` calls of a climate reading.
  3. (bit-identical) A `[ThreadStatic]` one-entry memo of the last rain result in `WeatherModel`, keyed by
     (model id, x, z, hours): `Cloudness`, the thunder level, the normals and `vx, vz`. On its own it only hits
     the 2-5 % exact repeats EA-0070 counted.
  4. (needs a rules version) From weather rules 8 (being built now) let the anomaly read the weather at the
     block's centre, `pos.X + 0.5, pos.Z + 0.5`, as vanilla's rain does. Then memo 3 turns the second `Rain` of
     every live reading into a hit: about half of the whole weather cost of the climate event, and the cooling
     under rain then belongs to exactly the rain the game shows at that block. Rules 5-7 keep the corner.
  5. Make `/earth cost` also time `RainCloudnessPostfix` (a second counter beside `CountWeather`), so the next
     measurement shows the whole weather share.
- EA-0073's own proposal (a cache per 32-block cell and 10 game minutes) changes rules 6 and 7 temperatures and
  needs a rules version, as the item says; 1-3 do not.

### W-7. CR-W2 still holds: the snow catch-up now pays the rain twice per corner-hour — medium — VERIFIED path, cost PLAUSIBLE

- Where: vanilla `WeatherSimulationRegion.cs:164-223` and `:233-237`; Earth `src/EarthWeather.cs:507`,
  `src/EarthClimateHooks.cs:68`.
- Evidence. `UpdateSnowAccumulation(Math.Min(num, 480))` asks `GetClimateAt(..., ForSuppliedDate_TemperatureRainfallOnly, totalDays)`
  for 8 corners x up to 480 hours per region per tick. Each is one `Cloudness` plus, from rules 6, one
  `TemperatureAnomaly`: about 3,840 x 5 us = roughly 20 ms per region per tick while a region catches up (a
  region loaded after a long absence catches up as much as a game year, `:623`, over several ticks). `Cloudness`
  itself is still not memoised.
- Fix: W-5 B (corners 2-8 hit the storm memo) and W-6.1; with rules 8, W-6.4. A per-thread "last place" memo
  (lat, lon, annual range, ground metres, lattice x for the three scales, keyed by exact x and z) is also
  bit-identical and serves the 480 consecutive hours at one corner.
- Risk: none for bits for the memos.

### W-8. CR-W3 still holds: two full `SampleClimate` per live climate reading — low — VERIFIED

- Where: `src/EarthClimateHooks.cs:47` → `src/Core/TerrainModel.Climate.cs:86-88`
  (`Data.SampleClimate(g.Lat, g.Lon).TMean`), and `src/EarthClimateHooks.cs:131-132` (`SeasonCorrection`:
  `core.Data.SampleClimate(g.Lat, g.Lon)`). Each also runs the fine temperature quad, the fine rain and the
  Penman lookups (`src/Core/ClimateSampler.cs:49-51`), of which the hook needs only three temperatures.
- Note the two use different projections (`TerrainGeoAt` and `Geometry.ToGeo`); check that they agree before
  sharing one sample.
- EA-0070 measured both at 0.05-0.1 us and judged a memo not worth it. Agreed: leave it, or add a
  temperatures-only sampler when the hook is next touched. Bit-identical if the same arithmetic is kept.

### W-9. `SlotCache.last` is one field shared by all threads — low — VERIFIED

- Where: `src/Core/WeatherStorms.cs:128`, `:138-151`.
- Evidence: `volatile Last last;` ... `last = new Last { Slot = slot, Entry = e };`. A server thread catching up
  snow at past hours, the client's render thread at "now" and W-1's look-backs at -9 h and -18 h ask different
  slots in turn; each change of slot allocates a `Last` and goes through the `ConcurrentDictionary` and two
  `Interlocked` operations.
- Fix: make the last-slot shortcut `[ThreadStatic]` (with the owner cache in the key), or keep the last two.
- Risk: none.

### W-10. `WeatherGround.PrecipScale`: four dictionary lookups in every `Place` (rules 7) — low — VERIFIED

- Where: `src/Core/WeatherGround.cs:85-95`, called from `src/Core/WeatherModel.cs:226` (so from every `Rain`,
  `WindAt`, `ThunderAt`, `HailAt`, `NormalTemp`).
- Evidence: `double R(int i) => precipRatio.TryGetValue(i, out float f) ? f : 1;` four times, although fix 4
  changes only the desert coasts.
- Fix (bit-identical): at load build a coarse bitmap (for example one bit per 8 x 8 climate cells) of blocks
  holding any changed cell, or a per-row [min, max] column span; return 1 at once outside. Or store the ratios in
  a flat `float[]` over the bounding boxes.

### W-11. Hail drawn: the same `HailAt` four times per client tick — low — VERIFIED

- Where: `src/EarthWeather.cs:557-572`.
- Evidence: the postfix on `clientUpdate` runs for each of the four regions round the player
  (`WeatherSystemClient.cs:~203-208`), and each computes `m.HailAt(pos.X, pos.Z, ...)` at the player's own
  position, so the four answers are equal.
- Fix: compute once per tick (key on `TotalHours`) and reuse. Bit-identical.

### W-12. CR-E6, CR-E7, CR-E8 still hold — low — VERIFIED

- CR-E6: `src/Core/GrassSeason.cs:62-67` via `src/EarthGrassSeason.cs:214`, per grass block on the tesselation
  threads: `SoilFertility.Aridity(...) * Math.Exp(...)` then `RainFromAridity` (`src/Core/SoilFertility.cs:48-68`:
  `Math.Exp(Math.Log(..) + t * (Math.Log(..) - Math.Log(..)))` and three more `Math.Log`). Fix as before: the
  logs of the `Ai` table as constants, and `Aridity(rain, 0)` as a 256-entry table (rain is a byte). Client
  colour only; a table of the same doubles is bit-identical anyway.
- CR-E7: `src/Core/TerrainModel.Ecology.cs:60-88` and `:90-113`; `src/EarthMapLayers.cs:56`, `:72`. Both
  potentials sample the climate, build `ZoneDrivers.From`, read the fire regime and the riparian weight; the
  forest map and the shrub map loop over the same region. Fix: one "ecology site" per pixel where the two maps
  have the same size, else leave.
- CR-E8: `src/EarthGrassWinter.cs:164` still allocates `pos.UpCopy()` on every soil random tick before the dice
  (`:173`). The block above decides which bound the dice use, so: roll first against the larger bound
  (`MaxGrowChance(days)`), return, and only then look at the block above. The draw order stays, so the outcome
  distribution is the same.
- Also: `src/Core/GroundMoisture.cs:36-45` runs 96 `RainSample` and 96 `Math.Exp(-age / tau)` per cache miss
  (per 3-hour bucket and cell); the weights are constants of the calendar and could be a static table. Small.

---

## 2. Correctness

### W-13. `NightHours` uses `Math.Tan` and `Math.Acos`: the one place the weather leaves the portable maths — medium — VERIFIED

- Where: `src/Core/WeatherModel.cs:627-634`, read by `FogAt` (`:612`) and `/ew` (`src/EarthWeather.cs:929`).
- Evidence:
  ```
  double x = -Math.Tan(lat * Math.PI / 180) * Math.Tan(decl);
  ...
  return 24 - 24 / Math.PI * Math.Acos(x);
  ```
  `PortableMath` has no `Tan` or `Acos`. Decision 0017 says "`Core.PortableMath` ... is used throughout the
  weather code" (`docs/decisions/0017-weather.md:226-227`); rules 7 came a day later and added this.
- Consequence today: only the drawn fog's `longNight` factor can differ in the last bit between operating
  systems. Fog is not shared state, so nothing desynchronises; but the stated property no longer holds for the
  whole model, and a later use of `FogAt` on the server (wet ground, crops, the weather role) would inherit it.
- Fix: `tan = Sin / Cos` and `acos(x) = Atan2(Sqrt((1 - x) * (1 + x)), x)`, both from `PortableMath`; add
  `FogAt` to the cross-OS fingerprint test.
- Risk: moves the fog's bits by an ulp on every OS. Drawn only, so no rules version is needed now; do it before
  anything on the server reads `FogAt`.

### W-14. CR-A10 still holds: the model is built by a one-second poll — medium — VERIFIED (the poll), PLAUSIBLE (consequences)

- Where: `src/EarthWeather.cs:92` (`RegisterGameTickListener(_ => Watch(), 1000)`), `:143-209`, `:204-205`.
- Consequences:
  - Until the first `Watch` every hook answers as vanilla (`ModelFor` null). On the server the first ticks of
    `WeatherSystemServer.OnServerGameTick` create the weather regions round the spawn and may run their snow
    catch-up and first pattern pick with vanilla's rain noise and without the anomaly. Whether a tick lands in
    that window depends on listener timing; not checked in game.
  - Shader reloads on a client: `RainStreaks` (`:1096-1099`) and `SeasonTintAltitude` (`src/SeasonTintAltitude.cs:57-61`)
    each ask for one 500 ms after `LevelFinalize` and de-duplicate through their `Injected` / `loaded` flags, but
    only when the weather model already exists. When the poll comes later, `:205` reloads all shaders a second
    time ("The first one-second watch can run after LevelFinalize's shader callback").
  - `EarthThunder` (`src/EarthThunder.cs:53`) polls the same way, one more second behind.
- Fix: as proposed before: `EarthMapSystem` raises an event (or calls `EarthWeather.For(side).Watch()`) when
  `Params` is set on that side (server: at the end of `InitWorld`; client: in `OnParams`). Keep the poll only for
  `RecheckRole`.
- Risk: none for bits. Order matters on the client: the model must exist before the `LevelFinalize` callbacks.

### W-15. `EarthGrassSeason`'s shared caches can take a value computed for the world before — low — PLAUSIBLE

- Where: `src/EarthGrassSeason.cs:368-381` (`Curve`), `:84-96` (`Dispose`), `:104-113` (`Tick`).
- Evidence: a mesh thread reads `var geo = geometry; var w = weather;`, computes the curve, then
  `curves.Set(key, curve)`. `Dispose` and `Tick` clear the static caches and raise `generation`, but a thread
  that read the old geometry before the clear writes its result after it. The per-thread "last cell" is guarded
  by `generation`; the three shared `BoundedCache`s (`curves`, `monthShapes`, `phenologyCache`) are not.
  `geometry` and `weather` are plain statics read from mesh threads (not volatile).
- Only at a world change in one process while chunks still mesh; the effect would be a few wrongly coloured
  grass cells until the next half-month redraw.
- Fix: read `generation` before computing and skip the `Set` when it changed; or put the generation in the key.

### W-16. The rain and the rest of the weather read the hour two ways — low — VERIFIED

- Where: `src/EarthWeather.cs:514` (`totalDays * m.Settings.HoursPerDay`), `src/EarthClimateHooks.cs:68` (the
  same), against `api.World.Calendar.TotalHours` at `:567`, `:599`, `:625`, `:661`, `:705`, `:733` and in
  `EarthThunder`.
- `TotalDays * HoursPerDay` need not round to `TotalHours` exactly. Both sides do the same, so nothing
  desynchronises; the rain, the hail drawn and the thunder heard are an ulp of time apart. No change needed; if a
  memo keyed by exact hours is added (W-5, W-6), key the two forms knowingly (they will not match each other).

### W-17. `WeatherModel`'s statement about a missing ground does not cover the thunder — low — VERIFIED

- Where: `src/Core/WeatherModel.cs:173-175` ("without it rules 7 read as 6 there"), `:809`, `:823`
  (`Thunder.SpellsPerDay(..., Settings.Rules)`), `:597` (`FogAt`).
- With `Rules = 7` and `Ground == null` the rate temperature and the coastal precipitation fall back to rules 6,
  but the dry-month thunder boost and the fog stay rules 7. The game never builds such a model
  (`src/EarthWeather.cs:154-155` refuses), so only tests can see the mixture. Fix: correct the comment, or throw
  in the first `Rain` when rules 7 have no ground.
- Related: `Thunder.Density`, `DayShare`, `SpellsPerDay` default `rules = 6` (`src/Core/Thunder.cs:67`, `:79`,
  `:95`) while the newest is 7; both production callers pass the world's rules. Drop the defaults so a new caller
  must choose.

### Threading, statics, Harmony: checked and found sound

- `EarthWeather`: `model` volatile, set after `Ground` and `LocalHourOffset` (`:187-195`); patches counted per
  process (`users`) and removed with the last user (`:443-456`); `Active`, `RainPatched`, `cloudFields` cleared
  there. The rules-gated patches (`:388`, `:400`) are decided by the first side to apply; both sides of a
  single-player process build from the same params, and a world change unpatches first.
- `EarthClimateHooks.Shift` keeps its reuse memo `[ThreadStatic]` (`:76-77`); world generation never reaches the
  weather part (`:61`).
- `EarthFauna`'s `SuppliedEarthTemperature` is `[ThreadStatic]` and reset in a `finally`
  (`src/EarthFauna.cs:355-357`, `src/EarthMapSystem.cs:1159`).
- `EarthTrees.pickers` is emptied at `:359` and `:410`.
- No `Parallel` loop in the weather; the storms' per-slot build is sequential and hash-driven, so order cannot
  change results. `Gaussianizer` (`src/Core/WeatherNoise.cs:141-173`) is built from hashes, `Array.Sort` of
  doubles and `PortableMath`, the same everywhere; one static instance for all models is right.
- Other `Math.*` transcendental calls in this area are outside the shared weather: `GrassSeason.cs:35`, `:65`
  (client colour), `GroundMoisture.cs:42` (server), `SoilFertility.cs:55`, `:66`, `FireRegime.cs:103-105`,
  `ClimateZones.cs:88`, `:227-228`, `:256`, `:259` (world generation, server). They only matter if one world is
  ever generated by servers on two operating systems; then a chunk edge at a threshold could differ. Say so in
  decision 0017 rather than change it (changing it moves generation bits).
- Not unregistered: `src/EarthWeather.cs:109` (`UpdateLocalWind`, every 100 ms) keeps no id, unlike `tickId`. The
  client's event API goes with the world, so it is harmless.

---

## 3. Structure

### W-18. The rules versions are about 25 scattered tests, the number 6 without a name — medium — VERIFIED

- Where: `src/Core/WeatherModel.cs:225`, `:241`, `:538`, `:597`, `:648`, `:720`, `:762`, `:809`, `:823`;
  `src/Core/Thunder.cs:72`; `src/EarthWeather.cs:154`, `:189`, `:388`, `:400`, `:545`, `:562`, `:592`, `:621`,
  `:822`, `:915`, `:923`; `src/EarthClimateHooks.cs:65`.
- Rules 7 has three named thresholds for one number (`GroundRulesFrom`, `FogRulesFrom`, `Thunder.DryRulesFrom`);
  rules 6 is the literal `6` in nine places. Rules 8 will add another layer to each list.
- Fix: one immutable policy made once from the saved number, as `RiverPoolRules` is for the pools:
  ```
  public readonly struct WeatherRules {
      public readonly int Version;
      public bool ColdSeaDecks, Hail, TemperatureAnomaly;   // 6
      public bool Ground, DryThunder, Fog, CloudTiles;      // 7
      public static WeatherRules For(int version);
  }
  ```
  `WeatherSettings.Rules` becomes that struct; `EarthWeather` asks `m.Rules.Fog`, `Thunder.Density` takes
  `bool dryBoost`. Rules 8's switches then go in one place, and the "frozen per world, older worlds bit for bit"
  promise is checked by reading one function.
- Risk: none for bits if done as a rename; `WeatherContractTests hash rules5` (and the same hash for 6 and 7,
  if it is not there yet) is the check.

### W-19. What to split in `EarthWeather.cs` (1,125 lines) and `WeatherModel.cs` (913) — low — VERIFIED

`EarthWeather.cs` holds seven things; by line range:

| Part | Lines | Proposed file |
|---|---|---|
| lifecycle, `Watch`, model build | 39-209 | `EarthWeather.cs` |
| the weather role (claim, recheck, withdraw) | 211-300 | `EarthWeather.Role.cs` (partial) |
| Sea link | 302-355 | stays, or with the role |
| `Apply` / `Release` and the nine patch methods (rain, stoprain, hail x2, fog, cloud tiles, pattern pick) | 357-682 | `EarthWeather.Patches.cs`; better, the table into `PatchVanilla` (CR-A9: the weather is still the one system patching outside it, `:369-430`) |
| wind (`OnGetWindSpeed`, region wind, cloud drift) | 684-774 | `EarthWeather.Wind.cs` |
| `/earthweather`, 160 lines of text | 776-943 | `EarthWeatherCommand.cs` |
| `VanillaWindVector`, `RainStreaks` | 959-1058, 1066-1124 | their own files |

`WeatherModel.cs`: the value types (`WeatherWind`, `WeatherSettings`, `WeatherFog`, `WeatherSample`, `:7-113`) into
`WeatherTypes.cs`; the static circulation (`Belts`, `PrevailingAt`, `SteeringKmh`, `:253-357`) into
`WeatherCirculation.cs`; then partials by subject: rain (`:359-545`, `:651-682`), decks and fog (`:547-649`),
thunder and hail (`:684-739`, `:788-862`), temperature (`:741-786`), wind (`:864-911`). `HailAt` and `ThunderAt`
(`:694-739`) repeat the same twelve lines (place, steering, tie, threshold, the two fields); one private
`ThunderField(x, z, hours, out theta)` serves both and `ShowerField`.

The caches of W-1, W-3, W-4 and W-11 belong in one small client class ("what the client draws from the model"),
not in the postfixes.

### W-20. CR-A8 partly reduced, still five rule types and three error policies — low — VERIFIED

- One region index and one box type are shared now (`BotanicalRegions`, `RegionSet`, `GeoBox`;
  `src/Core/RegionalFaunaCatalog.cs:17` "one region index for every catalogue").
- Still separate: `RegionalTreeCatalog.Rule` (`:19`), `RegionalPlantCatalog.Rule` (`:21`),
  `PlacementCatalogue.Taxon` (`:38`), `RegionalFaunaCatalog.Rule` (`:33`), `VanillaFaunaRanges.Entry` (`:30`).
  Error policy: trees drop a bad raster and go on (`RegionalTreeCatalog.cs:250`), fauna quarantines an entry
  (`RegionalFaunaCatalog.cs:152-172` return a reason), plants and placement throw the whole file
  (`RegionalPlantCatalog.cs:90-120`, `PlacementCatalogue.cs:122-150`). Base64 raster decode at
  `RegionalTreeCatalog.cs:170`, `RegionalFaunaCatalog.cs:164`, `:172`. The 1318 "lost unless current" logic in
  both fauna files (`RegionalFaunaCatalog.cs:158-166`, `VanillaFaunaRanges.cs:39-41`).
- `RegionalFaunaCatalog.cs` and `EarthFauna.cs` are written without spaces (`if(profile!=NaturalProfile && ...`,
  `:113-172`), as is `src/Core/TerrainModel.Climate.cs:158-163`; the rest of the area follows one style.
- The proposal stands (one `Range` type, one loader, one policy); not urgent while the files are stable.

---

## 4. Documents against the code

| Where | Says | Code |
|---|---|---|
| `docs/decisions/0017-weather.md:133` | "Not yet: thunderstorms and hail are still vanilla's weather events" | thunder is Earth's since rules 3, hail since rules 6 (`src/EarthThunder.cs`, `src/EarthWeather.cs:386-397`) |
| `docs/decisions/0017-weather.md:226-227` | "`Core.PortableMath` ... is used throughout the weather code" | `WeatherModel.NightHours` (`:630`, `:633`), see W-13 |
| `src/Core/WeatherContract.cs:55-56` | "vanilla's hail stays its own" | rules 6 replace it (`PrecipitationStatePostfix`) |
| `docs/decisions/0017-weather.md` Implementation, "1 us per lookup" | | `TemperatureAnomaly` 2.2-3.3 us (EA-0070); `FogAt` about three times a rain sample |
| `src/Core/WeatherModel.cs:173-175` | "without it rules 7 read as 6" | see W-17 |
| EA-0070 / EA-0073 | the climate hook costs 4-8 ms per second | counts Earth's hook only; the cloudness postfix, the fog, the cloud tiles and the wind chain are outside it (W-1, W-2, W-4, W-6) |

No dead code of note was found in the weather files. `WeatherSettings.Variability` and `Gusts`
(`src/Core/WeatherModel.cs:60-62`) are never set by the game (always 1); they are test knobs.

---

## 5. What is good

- **Failure containment.** Every hook catches, keeps vanilla's value for that call and logs once per world and
  hook through an instance-held set (`src/EarthWeather.cs:482-499`), so a bug in the model can't silently turn
  the weather vanilla, and the next world in the same process logs again.
- **Patches that check before they change.** `VanillaWindVector` reads the original IL, counts the reads it would
  replace, patches both or says which one it left, and the wind handler then writes vanilla's own form
  (`:975-1053`, `:710-715`). `RainPatched` ties the thunder and the ground moisture to the one patch they depend
  on (`:67`, `src/EarthThunder.cs:82`, `src/EarthGroundMoisture.cs:~160`).
- **`PortableMath` and frozen rules.** fdlibm kernels on IEEE-exact operations, rules frozen per world in field
  201, a rules-5 hash test: the right way to keep "same world, same weather" true; W-13 is the single leak.
- **`WeatherStorms.SlotCache`.** Lazy per-slot build with waiting askers, least-recently-used trim that never
  drops an entry being built, and results that do not depend on what is cached (`src/Core/WeatherStorms.cs:108-179`).
- **`src/Core` has no game types**, so every model function reviewed here is testable offline, and the cost of
  the climate hook was measured (`/earth cost`, `EarthMapTests cost`) before anything was optimised.
- **`EarthGrassSeason`'s per-thread last cell** (`:240-253`, `:341-362`) is the pattern W-5 and W-6 propose for
  the weather: a mesh thread's blocks share a cell, so the shared cache is touched once per chunk.

---

## 6. Suggested order

1. W-2 (skip vanilla's wind handler) and W-1 (fog cache): the two largest per-frame costs, no bits change.
2. W-5 A and B, W-6.1: bit-identical, inside `Core`, checked by the existing hash tests.
3. W-13 (portable `NightHours`) before any server reader of `FogAt`.
4. W-18 (the rules policy) before or with rules 8, and W-6.4 as part of rules 8 if the owner agrees.
5. W-14 (build the model on the params event).
6. W-4, W-3, W-11, W-10, W-9 as time allows; W-12 and W-20 with other work in those files.

Before 2: extend `/earth cost` (W-6.5) so the gain shows.
