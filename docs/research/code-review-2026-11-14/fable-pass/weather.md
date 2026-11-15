# Earth: weather, climate, client side and living things: independent review

Tree: `scratchpad\earth-fable` (detached worktree, commit `fef6166` of `main`). Read-only for source; nothing committed.
Vanilla read from `C:\VS mods\devtools\vs-ref\decompiled\1.22.7` (and `vs-ref\src` where noted).
All line numbers are of the pinned tree. Probes and their outputs: `scratchpad\fable\probes-weather\`
(`Probe\` compiles `src\Core\*.cs`; `Vanilla\` loads the game's `VintagestoryAPI.dll` by reflection, read only).

Counts: critical 0, high 2, medium 4, low 9, refactor 3.

What was run:
- `tests\WeatherContractTests` in the pinned tree: ALL OK. Rules 5, 6, 7, 8 give the pinned fingerprints
  (ALL 18C6D3C6B8B0F4E7, C99BE19573CD5E22, 04AE435FBB7B4301, 4D19B0486DDC9A14).
- `tests\EarthMapTests <root> 384 cost` (numbers quoted below; the machine was shared, so timings are upper bounds).
- `Probe determinism`: 4,500 places and hours x 18 outputs per rules version 5-9 (hours spread over 20,000, so the storm
  caches trim): 0 differing bits between a fresh model in reverse order, the same model warm in shuffled order, 16 threads
  on one fresh model, and two models taking turns on one thread.
- `Probe nan`: 360,000 samples under 15 settings (rain 0/10/0.001/-1, storms 0/20/-1, pace 0.05/20, 2.4 hours a day, 12 and
  720 days a year, gusts and variability 5), poles, beyond the poles, outside the ring, negative time, hour 1e7: 0 NaN,
  0 out of range (cloudness 0..1.6, cover 0..1, wind <= 1.5, thunder/hail/fog 0..1).
- `Probe time`, `space`, `equator`, `storm`, `deck`, `dry`, `timeways`, `slots`, `eco`; `Vanilla` (vanilla's own noise).

---

## HIGH

### H1. The weather is torn along the whole equator (rain, clouds, pressure, temperature anomaly)

`src\Core\WeatherModel.cs:329-332` (`PrevailingAt`), read by `SteeringKmh` 361-368, `SteeringBlocks` 371-377 and used as the
advection velocity in `Advected` 465. All rules versions (5-9), every world.

Between the subtropical ridges the eastward part of the layout wind is chosen by a boolean:

    bool equatorward = flowNorth * lat <= 0;
    double east = equatorward ? -0.9 * m : 1.05 * m * SmoothStep(0, 6, Math.Abs(lat));

Wherever the ITCZ is off the equator (most longitudes and months), a place just on the ITCZ's side of the equator gets
`east = 1.05 m * smoothstep(0) = 0` and a place just across gets `-0.9 m`. The surface wind is not affected in worlds with
weather.bin v2 (it uses the reanalysis), but the steering wind always comes from this layout, and each rain field is
sampled at `x - vx * age` with an age of up to 4 h (showers), 24 h (fronts), 48 h (the temperature spells) and 96 h at
half speed (wet and dry spells). So the two sides of lat 0 read the fields hundreds of kilometres apart.

Measured (`Probe equator`, the same for rules 5 and 9):
- the steering wind steps by 15.1 km/h on average across lat 0 (19.5 at most; 180 longitudes x 12 months);
- one block apart across the equator, 40,000 places and hours: rain on one side only 4.4 % of the time (control lines at
  3 N and 3 S: 0.02 %), a rain cloud on one side only 10.8 % (0.05 %), cloud cover differs by 0.094 on average (0.0003),
  pressure by 3.4 hPa rms, 17.4 at most (0.00); rules 9: the temperature anomaly 33 blocks apart differs by 0.51 C on
  average, 4.9 at most (control 0.02);
- examples, blocks north and south of the equator, cloudness/cover/hPa:
  Kampala's meridian `N1: 0.00/0.66/1016  S1: 1.50/1.00/1010`; Macapa `N1: 1.42/1.00/999  S1: 0.01/0.80/1008`;
  Sumatra `N1: 0.37/0.93/1008  S1: 1.60/1.00/1002`; Quito's meridian `N1: 0.49/1.00/1002  S1: 1.60/1.00/995`.
- `Probe space` found no other line like it: the ring's jump line, x against x + ring length (1e-12), lon 180, lon 0, the
  poles, lat 4, lat 70 and the lattice edges are all at the level of two points 2e-4 blocks apart anywhere.

The player sees a ruler-straight east-west edge: heavy rain on one side and a dry sky on the other, cloud tiles (rules 7+)
ending on the line, and the live temperature stepping with it. It runs through Kampala and Lake Victoria, the Congo, Gabon,
Sumatra, Borneo, the Amazon's mouth and Ecuador.

Fix: make the eastward part continuous through lat 0, e.g. blend the two branches by how far the air has crossed:
`double cross = SmoothStep(-3, 3, flowNorth * lat); east = Lerp(-0.9 * m, 1.05 * m * SmoothStep(0, 6, |lat|), cross);`
(any blend that is continuous in lat does it; `Probe equator` is the check: the lat 0 line must read like the controls).
Storm tracks use `SteeringKmh` too and move with it.
Saved worlds: this changes the rain, clouds, pressure and storm tracks within the tropics, so it needs a new weather rules
version (10); rules 5-9 keep the tear unless the author decides those worlds may change.

### H2. Earth's hail is never drawn or heard on the client; the server's hail hurts under drawn rain

`src\EarthWeather.cs:398-399` (the patch), `591-607` (`RegionClientUpdatePostfix`). Rules 6+.

Earth's postfix is on `WeatherSimulationRegion.clientUpdate` and writes `weatherData.BlendedPrecType = Hail`. Vanilla calls
`clientUpdate` at the top of `TickEvery25ms` (decompiled `WeatherSimulationRegion.cs:227-229`) and the same method ends with
`weatherData.BlendedPrecType = CurWeatherEvent.State.PrecType;` (`:310`, unconditional, both sides; the published source has
the same line, `vs-ref\src\vsessentialsmod\Systems\Weather\WeatherSimulationRegion.cs:382`). `TickEvery25ms` runs from
`WeatherSystemClient.OnClientGameTick` (`WeatherSystemClient.cs:202-208`); the particles and sounds read the type from the
blend made later in `OnRenderFrame` (`:152-153` -> `WeatherDataSnapshot.SetLerped`, `WeatherSimulationParticles.cs:301`,
`WeatherSimulationSound.cs:183`). All on the main thread, so the value Earth wrote lives for the rest of one call and is
never read. The `else` branch (vanilla's hail event drawn as rain or snow) is overwritten the same way.

So on a client: Earth's hail falls as rain or snow, and vanilla's random hail events are still drawn and heard as hail.
On the server `PrecipitationStatePostfix` (`EarthWeather.cs:555-584`) does work (`GetPrecipitationState` returns a fresh
object, `WeatherSystemBase.cs:107-116`), so from rules 8 a player and every creature with health
(`EntityBehaviorHealth.cs:500-511`) takes hail damage under what is drawn as rain, and takes none under what is drawn as
hail. Rules 6 and 7: the same mismatch without the damage.

Verified in the code only (decompiled 1.22.7 and the published source agree). It contradicts decision 0017's bench note that
hail was seen falling; a vanilla hail event at that moment would look the same. A two-minute check in game: under
`/earthweather hail` "falling now", log `WeatherDataAtPlayer.BlendedWeatherData.BlendedPrecType` in a render frame.

Fix: write the type after the tick, not inside it: a postfix on `WeatherSimulationRegion.TickEvery25ms` (EarthThunder
already patches that method with a prefix and a finalizer, `PatchVanilla.cs:1077`; the hail can ride in the same table).
Saved worlds: no change to the weather; no rules version.

---

## MEDIUM

### M1. A storm's rain stops on a line at its outer radius

`src\Core\WeatherStorms.cs:339-348` (reach test, `RainBoost`), `395-413` (`RainOf`); `src\Core\WeatherModel.cs:585`
(a storm in a dry month sets the rain threshold to 2.5 only inside). All rules versions.

The wind fades to 0 at the radius (`fade`, 379) and the pressure as `(1 - r/R)^2` (349), but `RainOf` is not faded:
at `r = R` a tropical cyclone's bands are `1.9 e^(-1/0.55) (0.55 + 0.45 sin ...)`, up to 0.31, and a low's shield
`2.3 e^(-(0.58/0.5)^2)` = 0.6 ahead of it, each times the strength. One step further the storm is out of reach and the
boost is 0. In a month with no rain chance the threshold also switches from 2.5 to none at the same circle.

Measured (`Probe storm`, a game year, 3,396 storm-moments, 81,504 points on the edges, 0.05 % of the radius inside against
outside): the boost steps by up to 0.55 sd; by 0.1 sd or more at 27 % of the edge; rain on one side only at 2.0 % of the
edge; largest cloudness step 1.0 (rules 5) and 0.95 (rules 9: 1.60 -> 0.65 over 3.2 blocks at Tropical cyclone 253).

The player sees the rain of a passing storm switch off (or on) at once, or stands on a curved line with rain on one side.
Rare at one place (a storm's edge passes twice per storm), but it is a hard edge when it happens.

Fix: fade the rain as the wind is faded, `RainOf(...) * (1 - SmoothStep(0.45 R, R, r))` or `(1 - r/R)^2`, and let the dry
month's threshold come in with the boost rather than with `RainBoost > 0`.
Saved worlds: changes rain near storms' edges; needs a rules version (with H1).

### M2. Vanilla's world-wide temperature noise stays on top of Earth's climate and of the weather's own spells

`src\EarthClimateHooks.cs:58` (seasons) and `61-71` (the anomaly) against vanilla `ModTemperature.cs:27-28, 79-80`.

Vanilla's handler (subscribed in `Start`, so before Earth's) sets the live temperature and adds
`YearlyTemperatureNoise.Noise(totalDays, 0) * 3 + DailyTemperatureNoise.Noise(totalDays, 0)`: no position, so the same
everywhere in the world at one moment. Earth corrects the swing and the mid-offset and (rules 6+) adds its own warm and
cold spells fitted to stations' day-to-day spread, but leaves this noise in.

Measured from the game's own `SimplexNoise` (`Vanilla` probe, 200 game years of 108 days, three seeds): the yearly noise
x3 has sd 1.7-1.9 C and reaches -5.4..+4.4 C; the daily noise sd 0.61 C, +-2.1; together sd 1.8-2.0 C. Whole game years
sit 1.7-1.9 C (rms) off the climate, the worst of 200 by 4.0-5.0 C.

What that does: (a) rules 6's spread is no longer the stations': where the fit gives 0.4-1 C (tropics, sea air) the
thermometer moves with sd about 2 C; (b) the live climate drifts for years against the climate the plants and animals
were placed by (`TMean - lapse`, `TCold - lapse`, EarthPlacement 161-163, EarthTrees 319-321): a world can be 4 C colder
than its catalogue envelopes for a game year, everywhere at once (frost on crops at the warm edge of their cold limit,
snow lines, grass regrowth by `WeatherTemperature` not moving with it since that reads weather.bin).

Fix: in the hook, for live modes, subtract what vanilla added (the two noise objects are public fields of `ModTemperature`;
`- Yearly.Noise(totalDays, 0) * 3 - Daily.Noise(totalDays, 0)`) in worlds whose weather has `TemperatureAnomaly`, or
decide to keep it and say so in 0017.
Saved worlds: changes the live temperature (not generation, not the weather's fingerprints); gate it by a rules number if
existing worlds must keep their thermometer.

### M3. The snow catch-up costs 15-17 ms per weather region per tick with Earth's weather

`src\EarthWeather.cs:519-531` (rain postfix) and `src\EarthClimateHooks.cs:61-71` (anomaly), called from vanilla
`WeatherSimulationRegion.UpdateSnowAccumulation` (`:164-223`: 8 corners x up to 480 hours a tick, `:233-236`; a region
loaded after a long time catches up a year at most, `:623`).

Measured (`EarthMapTests 384 cost`, "snow catch-up: 8 corners x 480 hours"): 14.5-16.6 ms per region for rules 6, 7 and 9
alike (246 kB allocated, see R1). Rules 8's anomaly cells do not help here: each hour is a new ten-minute step, so every
reading is a full rain sample for the anomaly plus one for the cloudness (1.7-2.0 + 2.0-2.5 us). Vanilla's own readings
there are 0.26 us (rain noise) + 0.14 us (temperature noise), measured on the game's DLL: about 1.5 ms for the same loop.

Server tick: the weather tick runs every 200 ms over all loaded map regions (`WeatherSystemServer.cs:203-215`). Coming back
to land not visited for a year: 2,592 hours = 6 ticks per region at 108 days a year, 18 ticks with 30-day months; with 9
regions loaded that is about 140 ms of a 200 ms weather tick, 6 to 18 times in a row; `/time add` of months (and
`/earth date`) does the same for every loaded region.

Fix (no change to any result): the eight corners are four columns at two heights with the same (x, z, hour) sequence, so a
per-thread memo of the last few hundred (x, z, hour) -> (cloudness, anomaly parts) halves it; the anomaly's rain sample
and the cloudness could share one `Rain` call when asked for the same place and hour; or cap the hours Earth answers per
tick. Decision 0017 lists this memo as not done (W-5 B, W-7).

### M4. The cold-sea cloud deck and its fog end on a cliff at the 0.5 land-share contour

`src\Core\WeatherData.cs:230` (`ColdWater` answers NaN where `land > 0.5`), `src\Core\WeatherModel.cs:641-648`
(`LowCloudDeck`), `652-657` (`DeckStrength`), `687` (sea fog). Rules 6+ (cover), 7+ (fog, cloud tiles).

Measured (`Probe deck`, places 0.57 blocks apart on a line across the coast): Peru 12 S: the month's cover 0.800 -> 0.305
and sea fog 1.000 -> 0.000 at lon -77.058 (land share 0.500); Namibia 23 S: 0.800 -> 0.098, fog 1.0 -> 0.0 at 14.492;
California 36 N: 0.800 -> 0.267, fog 1.0 -> 0.0 at -121.547.

The contour is weather.bin's 0.5 degree land share, bilinear: a smooth curve some kilometres off the real coast, on either
side of it. The cloud tiles (scaled by this cover from rules 7) end on that curve all through the deck's season, a static
edge in the sky; Lima itself (77.03 W) lies on the land side and never gets the garua. The term
`(1 - land * (1 - night))` at 687 ("over land at night and in the morning only") can only act for land shares up to 0.5.

Fix: fade instead of cut, e.g. return the cold-water anomaly for any land share and multiply the deck by
`1 - SmoothStep(0.35, 0.85, land)` (the fog's night term then does what its comment says).
Saved worlds: changes cover and fog (not rain); the pinned fingerprints include cover, so a rules version.

---

## LOW

### L1. A hard edge at the dry-month line (0.01 mm/day)
`src\Core\WeatherModel.cs:387` (`RainChanceFor` returns 0 at or below 0.01 mm/day; just above it the chance is already
about 6e-4, threshold 3.2 sd) and `582-585`. `Probe dry`: 178 crossings found, 4,272 pairs a hair apart: a rain cloud on
one side only in 1.12 %; largest cloudness step 0.73 (Sahara, 26.1 N 12.7 E, rain chance 6.3e-4 against 0). Fix: let the
chance fade in (`f *= SmoothStep(0.01, 0.03, precip)`), and M1's threshold with it. Rules version.

### L2. The rain and the anomaly are asked at `totalDays * HoursPerDay`, everything else at `Calendar.TotalHours`
`src\EarthWeather.cs:527, 565` and `src\EarthClimateHooks.cs:68` against `602, 636, 663, 699, 780, 814, 833` and
`EarthThunder.cs:296, 331`. Vanilla's `TotalDays` is `TotalHours / (double)HoursPerDay + timelapse`
(`GameCalendar.cs:116`). `Probe timeways`: the two hours differ in bits in 16 % of 200,000 moments, the cloudness in 6.5 %,
by 8.8e-13 at most, 0 rain flips: no visible effect, but one moment's rain is not the same bits as the weather role's
`rain(x, z, TotalHours)`. With the client's `.timelapse` running, `timelapse` shifts the rain, hail state and anomaly by
days while wind, fog, cloud tiles, thunder and the drawn hail stay at the calendar's hour. Fix: one helper for the hook's
hours, `(totalDays - calendar.Timelapse) * HoursPerDay`, and `Calendar.TotalHours` itself where the caller asked for now.
No rules version.

### L3. The weather ignores the calendar's season override
`src\Core\WeatherModel.cs:225-229` (`YearRel` from the hours alone); `src\EarthClimateHooks.cs:121-125` does honour it. In
a world with the season fixed (world option "seasons", `GameCalendar.SeasonOverride`) the thermometer stays in that season
while the weather's month normals still go round the year: the rain's hours by the month's temperature, thunder, hail's
share and the fog's cool season follow January in a world that is always spring. Fix: hand the override to
`WeatherSettings` on both sides (the calendar has it on both) and use it in `YearRel`. Changes those worlds' weather only.

### L4. The live temperature steps across the equator in January and July
`src\EarthClimateHooks.cs:129`: the season curve is the northern one for `lat > 0`, the southern one otherwise, times the
place's real swing, which is not 0 at the equator (vanilla's own swing is `|lat| * 65`, 0 there). `Probe eco`: on the
equator's land the swing is 1.83 C on average, 3.97 C at most, over 2 C on 37 % of it (Macapa 2.4, Libreville 2.5, Mount
Kenya's meridian 2.5, Kampala 1.9): the thermometer steps by that much one block across lat 0 in mid-January and
mid-July. Fix: blend the two curves over a few degrees (`Lerp(sSouth, sNorth, SmoothStep(-5, 5, latDegrees))`).
Live temperature only.

### L5. Land fog's top follows the player's rain-map height
`src\EarthWeather.cs:631-639`: `groundY` is `GetRainMapHeightAt` at the player, so under a tree or a roof the layer's
top is 6 blocks above the canopy or the roof, and it rises with the player on a slope: valley fog can't be climbed out of,
and it is as deep on the hilltop as in the valley. Fix: a fixed top per region (the lowest ground in a ring around the
player, or the region centre's terrain height from Earth's vertical model + 6). Drawing only.

### L6. A client builds its weather from the server's numbers unchecked, and the handshake covers weather.bin only
`src\EarthWeather.cs:155-159, 180-193`. (a) No `EarthGeometry.IsUsableLayout` or finite check before `new WeatherModel`
(the sky's longitude has one, `SkyLongitude.cs:16-20`). `Probe badlayout`: with blocks per degree 0 or NaN, or a ring
length 0 or NaN, every lookup throws IndexOutOfRangeException (caught by each hook: vanilla's value and one log line, but
an exception on every reading, thousands a second); an infinite `StormFrequency` gives 15,918 storms alive in a slot and a
320 ms first lookup. The server clamps its config (`EarthConfigStore.cs:104-106`) and repairs its layout, so this needs a
broken or hostile server. (b) `weatherground.bin` and the climate fix
planes the ground's rain scale is made from (`EarthDataset.cs:124-150`) are not in the fingerprint: a client whose files
differ computes other rain in rules 7+ worlds without a word. Fix: check the layout and the three factors like the other
mismatches (vanilla weather and a line); add the ground's fingerprint to `WeatherDataSignature`.

### L7. The ground moisture kept per cell depends on who asked first
`src\EarthGroundMoisture.cs:169-178` keys the cache by cell and 6-hour bucket but computes `GroundMoisture.At(..., hours)`
at the asking moment, and `GroundMoisture.cs:33-47` samples every 3 hours where rules 8's rain events last 2-4 hours.
Two servers (or one after a restart) can give one cell different moisture for the same bucket, and a shower between two
samples is missed. Server only, small. Fix: evaluate at the bucket's start; sample hourly (the cache makes it affordable)
or integrate `RainShare` over the step.

### L8. A missing or bad rules asset turns that part to vanilla for the session and generation goes on
`src\EarthPlacement.cs:69-78, 87-89, 111-116` (placement.json or treeregions.json missing, or `PlacementCatalogue`'s
constructor throwing on one bad taxon, `src\Core\PlacementCatalogue.cs:125-132`: one entry takes the whole catalogue
down); `src\WorldGenRules.cs:100` (zones.json: "forests and shrubs follow the rain alone"); `src\EarthMapSystem.cs:954-986`
(regional trees). Each logs an error and the server keeps generating, so the chunks made in that session carry vanilla
placement or the fallback forest for good, beside chunks made by the world's saved rules. The fauna catalogue holds a bad
rule on its own and lists it (`RegionalFaunaCatalog.Problems`), which is the better pattern. Units and lapse are right
(below). Fix: hold bad entries one by one in `PlacementCatalogue` as the fauna catalogue does; for a world whose saved
rules need an asset that is missing, stop as `Refuse` does for the model data, or at least say on the screen that new
chunks differ.

### L9. The clouds' drift cancels another wind than vanilla adds
`src\EarthWeather.cs:847-855` subtracts `3 x` the player's own gusty wind (`LocalWind[3]`), vanilla's `CloudTick` adds
`3 x` the four regions' blended steady wind at their centres (`CloudRenderer.cs:174`, through `RegionWindPostfix`). The
remainder (three times the difference, typically 0.1-0.3 of a drift of 1-10) pushes the clouds along X. Fix: subtract
`3 * WeatherDataAtPlayer.GetWindSpeed(playerY)`, the value vanilla adds. Drawing only.

---

## REFACTOR

### R1. Every storm lookup allocates 32 bytes
`src\Core\WeatherStorms.cs:150-164` (`SlotCache.Get`): the lambda `() => build(slot)` captures the parameter, so its
closure object is made at the method's entry, also on the per-thread fast path at 152. The cost benchmark shows 32 B a
call on `Storms.Influence`, `Cloudness`, `WindAt`, `CloudCover`, and 245,773 B per snow catch-up (7,680 lookups). Move the
miss path into its own method.

### R2. Pins and dead code
- `WeatherContractTests` pins ALL, anomaly and hail for rules 5-8 in its normal run; the fog fingerprints (decision 0017:
  4450DF488F399D91, 2E3EC4B7A86D4C7A) are only printed by `hash`, and rules 9 gets a pin only when 10 exists. Pin fog
  too, and pin rules 9 at release.
- `WeatherModel.cs:687`: `(1 - land * (1 - night))` (see M4).
- `WeatherContract.cs:142` `VanillaStrength` uses `MathF.Sqrt` (exact, fine); the only `float` maths on the weather's
  path, client drawing only.

### R3. Vanilla's rain noise is still computed under Earth's postfix
`src\EarthWeather.cs:519`: 0.26 us of vanilla noise per reading thrown away (the postfix is there because Cylinder World
prefixes the method). If Cylinder World's prefix could stand down for Earth worlds, a prefix would save it; small.

---

## The hooks: what vanilla does there, how often, and whether Earth keeps the contract

| Hook (Earth) | Vanilla (1.22.7) | Thread, rate | Contract |
|---|---|---|---|
| `GetRainCloudness` postfix, `EarthWeather.cs:519` | `WeatherSystemBase.cs:149-161`: noise >= 0, about 0..2.6; every live climate reading sets `Rainfall = clamp(c - 0.5, 0, 1)`, `RainCloudOverlay = clamp(c, 0, 1)` (`:139-147`) | any thread; every `GetClimateAt` but `WorldGenValues` and `TemperatureOnly`; `/earth cost` counted 1,200-1,800 a second | kept: 0..1.6; honours `OverridePrecipitation`; snow accumulation (`WeatherSimulationRegion.cs:202-209`) reads the same rainfall and temperature as before. 1.7-2.0 us against vanilla's 0.26 |
| climate hook, `EarthClimateHooks.cs:24` | `ModTemperature.cs:44-82` sets `Temperature` from `WorldGenTemperature`; vanilla reuses readings (`ServerWorldMap.GetClimateAt(pos, base, ...)`) | any thread, same calls | the reuse is handled per thread (`Shift`, 93-110): checked against every vanilla reuse loop (snow, farmland, berry bush, fruit tree, transient, harvestable): each is one thread, one object, nothing between. Seasons: M2, L4. Coldest and warmest month come out as `TCold - lapse`, `TWarm - lapse`: what the ecology filters by |
| `GetPrecipitationState` postfix, `:555` | `:107-116`, a new object per call; read only by `EntityBehaviorHealth.cs:500-511`, once a second per creature with health | server main thread | kept (type, size 0.6-1.0, level); the client half does not work: H2 |
| `Event_OnGetWindSpeed` prefix, `:761`, and `OnGetWindSpeed`, `:737` | `:62-65`: loads four regions, one climate reading, writes X only | any thread (particles, physics); client: twice a frame (`WeatherSystemClient.cs:155, 164`), once per particle tick, per entity every 5 s (`PModuleWind.cs:35-39, 43-58`) | 1.0-1.3 us in place of 5.4-9.6; the per-thread "written" record is safe across sides. Vector in X and Z; X readers patched on the client or the strength written in X |
| `WeatherSimulationRegion.GetWindSpeed` postfix, `:803` | `:351-364`, <= 1.5 | every region tick (client 20 Hz x 4 regions, server 5 Hz x loaded regions), `CloudTick` each frame x 4 | kept (<= 1.5); 0.7 us, memo for the same hour only |
| `RandomWeatherPattern` prefix, `:691` | `:491-509`, one `Rand.NextFloat()` | server at each transition; both sides at region creation | one draw as vanilla; cover weights only |
| `UpdateWeatherData` postfix (fog), `:622` | `:159-162` -> `SetAmbientLerped` | client main thread, 4 a frame (`WeatherDataReaderBase.cs:123-126`); server returns at once | ambient written after vanilla's; FogAt 6.0-6.5 us, kept 0.02 game hours (about 2.4 s): about 10 us a second. L5 |
| `pgetBlendedCloudThicknessAt` postfix, `:654` | `WeatherDataReaderBase.cs:164-169` | the cloud thread, every tile every pass (>= 40 ms; 400 tiles up to 1,000 blocks of view, `CloudRenderer.cs:260`) | 0..1 kept; CloudCover 1.4-2.0 us per tile per 2.4 s: about 0.3 ms a second |
| `CloudTick` prefix, `:847` | `CloudRenderer.cs:153-176` | client, each frame | L9 |
| `TickEvery25ms` prefix and finalizer (thunder), `EarthThunder.cs:184-204` | reads the event's rates each tick | both sides | the server's rate restored after the tick (the save keeps vanilla's); strikes through vanilla's `SpawnLightningFlash` |
| `clientUpdate` postfix (hail), `:591` | see H2 | client | broken: H2 |

A client frame costs Earth about 5 us (two wind lookups, four region winds, cached fog and hail); the cloud thread about
2 ms a second (tiles' cover and, every eleventh pass, each tile's climate reading through `LoadLerp` -> `GetClimateFast`,
vanilla's own doing). The first lookup in a new 12-hour storm slot costs 0.3-0.8 ms (1.75 at most), the first of a world
26 ms (`Probe slots`). The one heavy case is M3.

## Checked and found sound

- No `Math.*` transcendental and no `float` arithmetic on the shared weather path (WeatherModel, WeatherStorms,
  WeatherNoise, WeatherData, WeatherGround, Thunder, EarthGeometry, Geo.Wrap180; the climate planes the ground reads are
  decoded from integers). `Math.Sqrt/Floor/Round/Clamp/ScaleB` only. `Math.Pow/Exp/Cos` remain in server-only or
  drawing-only code (`CoverWeight`, `GroundMoisture`, commands, thunder's sampling rings).
- Results never depend on cache state, call order or thread (determinism probe above), including the rules-8 anomaly
  cells, the storm slot caches under trimming, the per-thread last-slot memo and two models on one thread.
- Time: the model's lattices, phase restarts (2, 4, 12, 24, 48, 96 h), the storm slots, the month edges, the year wrap,
  hour 0 and negative hours are continuous (`Probe time`: at the level of an ordinary moment, 1e-5 for 2e-6 h). The only
  steps in time are rules 8's ten-minute anomaly steps, by design (up to 1.8 C at a rain's onset among 3,000 places).
- The client's calendar: the server sends the calendar packet (`ServerMain.cs:475`, systems' `OnPlayerJoin`) before the mod
  event that sends `EarthParams` (`:477`), so `DaysPerYear` and `HoursPerDay` are the server's when the client builds its
  model; `/time` and speed changes only move the hour the same function is asked at.
- `LocalHourOffset` is decided by the server (`WeatherLocalSun`) and computed from the layout on both sides; the hour
  wraps correctly across the seam.
- Storms across lon 180 (`Wrap180` in the track, the centre and the distance), the poles (latitude clamped, `cos` floored),
  `Pow` of negative bases (whole powers only where the base can be negative), `Log` arguments (> 0.01), divisions
  (`Math.Max` floors) and the clamps: no NaN path found, none hit in 360,000 samples.
- One process, two worlds: every static (Harmony instances and user counts, `LocalWind`, the draw caches by model number,
  the grass season's caches, the lapse table's users, the sea level) is released or re-keyed in `Dispose`.
- Sides: the fog and cloud-tile postfixes return unless the weather system's api is a client's; the hail state postfix
  serves the server's damage; ground moisture, grass in winter and the snow relight load on the server only, the grass
  season and the creative date rows on the client only; the cloud pattern prefix also runs on a client when it first
  makes a region (one draw, as vanilla's, and the server's packet replaces the pick). Nothing reached on the wrong side.
- Packets: one message (`EarthParams`) at `PlayerJoin`; a later joiner gets the same object; other rules or another
  weather.bin fall back to vanilla weather with a log line; the data load is inside a try (`EarthMapSystem.cs:1171-1187`).
- Ecology units: every envelope takes Earth's rain as vanilla's 0..255 value and degrees C (PlacementCatalogue 240-249,
  RegionalTreeCatalog 42-47, 117-118; fauna gets `rain / 255` for vanilla's 0..1 conditions, RegionalFaunaClimate 14).
  The lapse is applied once everywhere checked: placement (EarthPlacement 161-163, 306-308), trees (EarthTrees 213-214,
  319-321), blooms (EarthGroundMoisture 141-147), grass in winter (203-210), hail, fog, the rate's ground temperature, the
  grass phenology (the block's byte already holds the lapse; the weather gives only the monthly shape).
- Hemispheres: the weather's months are the real ones at the real latitude; the live season curve and vanilla's season
  tint flip with the sign of the calendar's latitude (`SurvivalCoreSystem.cs:165-172`); the grass season maps are chosen
  by season length, not date, so vanilla's half-year shift serves the south.

## Not reached

- The inside of `RegionalTreeCatalog` (the picker's scoring), `BotanicalRegions`, `VanillaFaunaRanges`, `SiteDrivers`,
  `SoilNiche`, `SoilFertility`, `FireRegime`, `ClimateZones`, `GroundCover`, `ClimateLapse`'s patch table and
  `EarthMapLayers`: read only for units, lapse and what happens on a load failure.
- Nothing was run in the game: H2 and the visual findings (H1, M1, M4, L5) are from code and offline probes.
- Linux, macOS and ARM runs of the fingerprints (only Windows .NET 8 here; the vanilla probe on .NET 10).
- `tests\WeatherTests` full run (the contract tests and the cost benchmark were run).
- Cylinder World's latitude provider and Sea's side of the wind were taken as given.
