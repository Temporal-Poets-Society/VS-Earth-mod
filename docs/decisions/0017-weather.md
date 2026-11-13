# 0017: Earth's weather: climate and weather in Earth, Sea takes the wind

Status: Agreed direction (the user, 2026-10-21); implemented 0.15.0 (climate, rain, clouds, winds, storms); offline tests
and server self-test passed; not played; the Sea side is in progress in its own session | Date: 2026-10-21 |
13 November 2026: weather rules 6 for new worlds (cold-sea cloud decks, hail, the weather in the temperature) and
the same weather bits on every system ([below](#weather-rules-6-and-the-same-weather-on-every-system-13-november-2026)).
7 November 2026: Earth's weather published as the open sea contracts' `weather` role and vanilla's wind written as a
vector ([below](#open-sea-contracts-the-weather-role-7-november-2026)); on `claude/weather-contract`, offline-tested, not
seen in a game, not merged.
Decided by: the user (ownership, no separate mod, no support for other world generators); the scales and the details
below are proposals built for checking in game

## Context

- Earth fed vanilla one yearly rainfall value per place (climate.bin). Vanilla's weather picks rain and clouds per
  512-block region at random, wetter places more often, with no seasons: Mumbai rained as often in January as in July.
  The baker already read monthly normals (University of Delaware, GPCP, NCEP 2 m air) and summed them away.
- The winds came from Sea, made for sailing: latitude belts shifting with the seasons, drift, travelling storms, gusts,
  the same on every map, with no monsoon and no idea of land. Sea also turned vanilla's clouds and rain with its wind.
  Its rain amount stayed vanilla's (Sea's open proposal SEA-P02: storms without rain).
- The user (2026-10-20/21): Sea "was initially made just for sailing. Does not feel like it should govern winds for
  Earth"; Earth should be "geographics and climate data". On a separate weather mod: "i don't like separate mod idea",
  it "would need anyway engage too much with other terrain generators, that i am not willing to participate. Sea can
  have generic calls that can be used by others." Other world generators: "if other world generators make own weather
  then they can use it, if not then not." The model has to make "sense also on MP": climate lookup merged with "real"
  development of cyclones, storms and rain clouds. Scales: "find correct balance … we don't want local weather changing
  too fast - so day would feel like day". Sea's built-in model: "we could remove it".

## Decision

Agreed by the user (2026-10-21):

1. **Earth owns climate and weather** for Earth worlds: monthly normals, prevailing winds, rain, clouds and storms. This
   widens [0003](0003-geography-only.md) from geography to geography, climate and weather; built content still goes to
   companion mods.
2. **Sea takes the wind from a provider** through a generic call any mod can use, and keeps the water: waves from the
   wind's history, water drawing, dry hulls. Sea's own weather model (belts, drift, storms, gusts) is removed; it moved
   into Earth. Without a provider Sea has vanilla's wind. The bridge from the wind to vanilla (wind lookups, region wind
   sound, the slant of rain and snow, the clouds' drift) moves to Earth with it, so two mods never patch the same weather
   code.
3. **No separate weather mod, no weather for other world generators.** Non-Earth worlds keep vanilla's weather (or what
   another mod gives Sea).

Proposed and built for checking (0.15.0):

4. **A pure function, nothing saved or sent.** The weather is a function of (the world's noise seed, its Earth layout,
   position, game time) that the server and every client build from the same `EarthParams`; storms that move and
   develop are rolled from the seed per place and time and their tracks worked out from where they formed. A late
   joiner sees the same sky, `/time` skips just evaluate a later moment, and turning it off leaves no trace.
5. **Two scales.** Fronts, rain areas, steering winds and storms use real kilometres and km/h, converted with the map's
   blocks per degree; one game hour is one weather hour (`WeatherPace` 1). At the standard 198 m per block a front's
   rain band (~200 km, 40 km/h) passes over a place in about 5 game hours, a tropical cyclone takes about a day to pass
   and a week to cross an ocean, whatever the map's size. Showers (600 blocks), gusts (300 blocks) and the gathering of
   clouds are sized for the player in blocks. The game's year is short (108 days), so each season has fewer weather
   systems than on the Earth; `WeatherPace` above 1 gives more of them at the cost of faster local change.
6. **Rain calibrated to the real month.** Two random fields (fronts ~800 km, showers 600 blocks) are carried along by
   the steering winds and mapped to a normal distribution; the month's real precipitation sets the share of the time it
   rains (`0.55·(1−e^(−mm/day/14))`: ~10 % for a temperate month, a quarter for the rainforest, half for a monsoon
   month), and the threshold for that share makes it rain exactly that often at every place. Showers over land peak in
   the mid-afternoon. Rain clouds gather over the last stretch before the rain starts; intensity follows the month's
   mm per rainy hour.
7. **Prevailing winds around the ITCZ.** For each month and degree of longitude the tropical rain band / heat low (the
   ITCZ, half the wettest latitude, half the warmest) is found from the data. Air flows toward it: turned west where that
   is toward the equator (the trades), east where the ITCZ has moved far from the equator and the air crosses toward it
   (the SW monsoon of India and West Africa, the NW monsoon of northern Australia). Beyond the subtropical ridges, Sea's
   westerly and polar belts, following the ITCZ part of the way; a third weaker over land. Day-to-day drift and gusts on
   top.
8. **Storms on the real Earth.** Every 12 weather hours each 5° cell rolls for a storm. Tropical cyclones need sea at
   26-28 °C+, 4-27° from the equator, with the ITCZ on their side of the equator (so none in the South Atlantic); lows
   form 28-70° from the equator, more over the sea and in winter. About 85 tropical cyclones and 1000 lows a year at
   `StormFrequency` 1. Tracks: steering winds plus poleward (and for cyclones westward) drift, so Atlantic hurricanes go
   west and recurve north-east; cyclones weaken over land and cool water, lows slow and fill. Their winds spin
   anticlockwise in the north, strongest at the eyewall; their rain (eyewall and spiral bands, a low's shield ahead of
   it) and low pressure come with them.
9. **The game.** Vanilla's rain cloudness (`WeatherSystemBase.GetRainCloudness`, which every precipitation, live
   rainfall, wetness and snow reading uses) becomes Earth's, unless an admin set the precipitation; each region's next
   cloud pattern is weighted by Earth's cloud cover there (clear in the desert and dry season, overcast at a front);
   vanilla's wind lookups, region winds, rain slant and cloud drift follow Earth's wind, and rain streaks are drawn
   along the drops' paths (Sea's particle-shader edit, moved here); Sea gets Earth's wind as its provider. `/earthweather` (`/ew`) reports the weather here, `forecast` the next day, `storms` the nearest storms.

## Alternatives

- **A separate weather mod** owning the atmosphere for any world: rejected by the user (it would have to engage with
  every terrain generator).
- **Sea keeps the weather and reads Earth's climate**: cheapest, but a sailing mod would keep governing a planet's
  weather; rejected by the user.
- **A server simulation that saves and syncs its state**: free development of systems, but saves, packets, migration,
  and clients only knowing what they were sent. Not needed: the storms' tracks are worked out from their origin.
- **Real-time-scaled weather** (systems shrunk to the map, or sped up to fit the 108-day year): weather at a place would
  change in minutes; rejected by the user's "day would feel like day". `WeatherPace` keeps the choice open.

## Consequences

- **Saves:** nothing is stored. The settings (`Weather`, `WeatherPace`, `RainFrequency`, `StormFrequency`) apply at each
  start, existing Earth worlds included, and are sent to clients with `EarthParams` (members 65-69, 71-72; 69 is the
  rules version and 71 the weather.bin fingerprint: a client with other rules or data keeps vanilla weather and says so). Vanilla's own region weather state stays in the
  map regions as before, so switching back is clean.
- **Performance:** about 1 µs per rain or wind lookup (offline, 200 000 calls); the storms alive in a 12-hour slot are
  worked out once for the Earth (a few ms) and cached. weather.bin adds 1.4 MB to the mod.
- **Multiplayer:** server and clients compute the same values from the same parameters (tested bit for bit offline).
  Cloud patterns are picked by the server as in vanilla.
- **Other mods:** Cylinder World's rain patch (a prefix) is overridden by Earth's postfix while the weather is on;
  Earth's field is continuous over the ring itself. Fluffy Clouds' renderer gets the same drift as vanilla's. An older
  Sea (no `SetWindProvider`) keeps its wind, cloud drift and rain slant; Earth then does rain and clouds only. Wmap
  reads the wind through Sea's members, which Sea routes to the provider.
- **Data (0.15.1):** the prevailing winds and the mean cloud cover are the NCEP/NCAR Reanalysis 1 long-term monthly means
  (`uwnd.10m`, `vwnd.10m`, `tcdc.eatm`, NOAA PSL, ~1 MB each, downloaded at the user's request on 2026-10-21 into
  `rawdata/ncep-reanalysis/`; weather.bin v2). They add the East Asian winter monsoon, the oceans' highs and the winds off
  the ice sheets; the ITCZ layout stays as the fallback for a v1 file and steers the weather systems aloft. Mean monthly
  winds are vectors and so weaker than the wind usually is where it turns a lot; the day-to-day drift was raised to make
  up for it (open-sea mean 5.8 m/s). R1 has far too little of the low cloud decks off Peru and Namibia (20-27 % in July
  where satellites see about 70 %); off California it has them (81 %).
- **UDel's missing months:** about 12 600 UDel land cells (15 % of the land) lack one month of precipitation. The bake
  counted such a cell as no land data, so the coarse ocean data took over and drew one-row stripes across Siberia,
  Kazakhstan and Alaska (found from Wmap's climate map, 2026-10-21). weather.bin (0.15.1) fills a missing month from the
  months around it: 1093 stripe cells → 240, the rest on mountain coasts. climate.bin has the same fault: fixed per
  world in [0018](0018-climate-stripes.md).
- **No December over land until 2026-11-07:** the "missing months" were a UDel download cut short three quarters into
  November ([0018](0018-climate-stripes.md), Correction). weather.bin from 0.15.1 (fingerprint 6dc1c95b299ecec1) read
  December as 0 mm on 87 % of the land north of 60°S, so inland Earth worlds had almost no December rain or snow
  (found by the ranges proposal, step 5b). Rebaked from the complete file (fingerprint bc9ea5a04cb1bc1d, on
  `claude/udel-december`): every month is 0 mm on at most 2.6 % of that land (the Sahara and Sahel in their dry season);
  Riga's December 46 mm, Moscow 51, Chicago 53, Tucson 31. The stripe count stays 240. Nothing is saved: the server
  writes its own file's fingerprint into `EarthParams` at every start, so an existing world or server gets the new
  weather with the mod update. A client still on an older build has the old fingerprint and keeps vanilla weather
  (and says so) until it updates; a client on the new build joining an old server likewise. No save, region or chunk
  data changes.
- **Local time:** the showers' afternoon peak follows the local sun (the calendar's hour plus longitude / 15) where
  AstraTerra's sky follows Earth's meridians (0010), else the calendar's hour like vanilla's single sun.
- **Not yet:** thunderstorms and hail are still vanilla's weather events, not tied to Earth's convection; snow cover follows the rain
  but vanilla's accumulation rules.

## Open sea contracts: the weather role (7 November 2026)

Status: in Earth 2.0.0 (built on `claude/weather-contract` from `release/1.0` `7edb1c7`, merged into the release candidate
on 8 November, `7145eb2` on `main`); re-claiming added and seen in a game with Sea 0.6.0-pre.1 on 13 November (EA-0037).
The contracts are a proposal ([Sea's spec](../../../Sea/docs/open-sea-contracts.md), sections 3-5; on Sea's branch
`claude/open-contracts`), not agreed with hoblin. Direction: the author, 31 October (Earth keeps the weather; for the Rough
Seas merge, the weather hook only) and 7 November (our side open to other mods); asked for by Sea's open-contracts session.

- **The role.** On each side Earth puts `api.ObjectCache["seacontracts:weather"]`, a `Dictionary<string, object>` of .NET
  base types only (`src/Core/WeatherContract.cs`): `owner` "earth", `version` 1, `priority` 100, `revision` (a
  `Func<long>` that changes when this side's weather is built, rebuilt or dropped), `wind(x, z, hours, gusts)` and
  `prevailing(x, z, hours)` toward (X east, Z south) in m/s (Earth's wind unit × 25; without gusts the steady wind seas grow
  from), `storm(x, z, hours)` → (Presence, WindMps, Kind), `rain(x, z, hours)` → (Cloudness, Precipitation), and
  `writesVanilla` true. Every function is a pure function of the world's parameters and game time (this decision's item 4),
  so it is thread-safe and the same on every side. There is no height: the answers are at sea level. While a side has no
  weather built, every answer is NaN (the contract's "no answer").
- **Claiming.** The server claims in `StartServerSide` when the save has Earth's parameters or is a new Earth world, so
  Earth never takes the role on a vanilla world. A client claims in `StartClientSide`, since it can't tell before the
  server's `EarthParams` arrive. Earth replaces only an entry of priority below 100. It withdraws the entry when the
  world's parameters say its weather is off here (a vanilla world, weather set to vanilla, other rules or data) and claims
  it again if the weather is rebuilt. A second world owner (priority 100 or more) keeps the role; Earth logs that once and
  its own weather still runs. (13 November, the contract's R2) While Earth wants the role it re-checks it every second
  (`EarthWeather.RecheckRole`): its entry goes back where it went missing or a lower priority took its place, and in where
  a world owner that kept Earth out has gone. The rule is `WeatherContract.MayReplace`.
- **Storm kinds** (the contract's open question 3): an Earth tropical cyclone is a `hurricane` at any strength, a
  mid-latitude low a `gale`. Earth never answers `squall`: its showers bring rain but no wind of their own. `Kind` is that
  of the storm whose wind is strongest at the place.
- **Presence.** For one storm, 1 within the radius of its strongest winds (a cyclone's eye and eyewall, a low's broad band),
  falling outward as its winds do ((r_max/r)^0.65 with the outer fade) to 0 at its edge, times its strength now over its
  peak (none as it forms, less as it fills or weakens over land and cool water). Storms combine as 1 − Π(1 − p). So 0.9 or
  more is a storm's core near its peak, which is what Rough Seas' hurricane-core test (its Extreme Weathers bridge) needs.
  `WindMps` is the storms' own wind at the place as it enters Earth's wind: their circulation, a third weaker over land,
  at most 37.5 m/s.
- **Rain** in vanilla's meaning: `Cloudness` = clamp(Earth's rain cloudness, 0, 1), vanilla's `RainCloudOverlay`;
  `Precipitation` = clamp(cloudness − 0.5, 0, 1), vanilla's `Rainfall` (`WeatherSystemBase.Event_OnGetClimate` in 1.22.7).
  An admin's `/weather` precipitation is not part of it.
- **Vanilla's wind as a vector** (section 5). Earth's `OnGetWindSpeed` now writes X east and Z south, the length its
  strength scaled for height in vanilla's unit; before, the strength went into X alone. Read in 1.22.7 first: the weather
  particles (rain, snow, hail, dust), entity wind (`PModuleWind`), floating items, cloth, the waterfall's spray and
  everything set from `GlobalConstants.CurrentWindSpeedClient` as a particle's parent velocity take X and Z as a velocity;
  body temperature, the player's wind sound and gnats take the length. So they now follow the wind's direction. Earth's
  own rain-slant transpiler on `WeatherSimulationParticles.asyncParticleSpawn` turned an X-only strength to Earth's
  direction and would now turn it twice, so it is removed.
- **Readers of X as the strength.** `DefaultShaderUniforms.Update` (four reads of the surface wind: foliage wave counters,
  `WindSpeed`, the vanilla water's wave intensity) and `EntityBoat.OnRenderFrame` (how much a boat rocks) get the vector's
  horizontal length through a client transpiler. The boat's weather vane, `Atan2(X, Z)`, keeps the vector and now turns
  with the wind. The patch goes in only while Earth writes the vector, and only when
  `api.ObjectCache["seacontracts:vanillawind-vector"]` is unset (Rough Seas patches the same two readers). Earth then sets
  the key to "earth" and removes both with the patch. Vanilla's other X readers are not in the contract and stay as they
  are, seeing the wind's east part: fruit-tree leaf fall (`BlockFruitTreePart`) and the calm-air checks of ambient
  particles (`ModSystemAmbientParticles`). Both are cosmetic.
- **Sea's older call kept.** Earth still calls Sea's `SetWindProvider` for Sea builds before the contracts. Its name is now
  "earth" (it was "Earth"), the role's owner, so a Sea that follows the role and Earth's own call name one provider.

Checks (7 November): `dotnet build` clean. `tests/WeatherContractTests` (`dotnet run -c Release -- <folder with
earthdata/weather.bin>`) all pass:
- the shape: exactly the nine members, the exact delegate types Sea tests for, every type from the base class library;
- NaN before the weather is built, and the revision changing when it is;
- m/s: Earth's units × 25 at 4,000 places and times; open sea 5.9 m/s on average; at most 30.6 m/s seen;
- toward, +X east, +Z south: the NE trades (−6.2, 3.1), the SE trades (−6.8, −1.5), the roaring forties (7.9, 1.3), the
  North Atlantic westerlies (4.5, −1.6);
- storms at 20,000 places and times: Presence 0-1, WindMps 0-37.5, a kind exactly where a storm is present, never
  `squall`, both kinds seen. A 36 m/s tropical cyclone is a `hurricane` with presence 1 in its eye and at its eyewall
  (37.5 m/s there); a 23 m/s low is a `gale`;
- rain: both 0-1, equal to vanilla's overlay and rainfall from Earth's cloudness at 20,000 places and times; Mumbai rains
  1.0 % of January and 42.3 % of July;
- the same answers bit for bit from a second model (another side), and from 32 threads asking one fresh model at once;
- the vector: its length the strength, along the wind, and the length a reader takes is the strength back.

The transpilers were run, through the game's own Harmony on the installed 1.22.7 DLLs, without patching anything: 4 of
`DefaultShaderUniforms.Update`'s reads and the one rocking read of `EntityBoat.OnRenderFrame` replaced, the vane's
`Atan2(X, Z)` left. `tests/WeatherTests` still pass (48 checks; storm presence only adds fields to
`WeatherStorms.Effect`). `SelfTest.CheckWeather` now expects the wind lookups as
a vector and checks the role on the server; it has not been run. Not seen in a game: the client's shader wind and boat
rocking from the length, the vane, rain blowing without the transpiler, and Sea 0.6.0-pre.1 following the role.

## Weather rules 6 and the same weather on every system (13 November 2026)

Status: built on `claude/weather-items` for the 2.0.0 release candidate (the author's request to close the weather
items, 13 November, by way of the coordinating session); offline-tested; seen in a game on the AgentBridge bench (one
client); hail not yet seen falling. Items EA-0019, EA-0023, EA-0025 (rules 6) and EA-0029.

- **Per-world weather rules.** Until now every world ran the build's weather rules (`EarthParams.WeatherRules` set on
  every start), so each rules bump changed existing worlds' weather. From rules 6 a world's own rules are frozen at
  creation in `EarthParams.WeatherModelRules` (field 201) and handed to the model as `WeatherSettings.Rules`; worlds made
  before have 0 there and keep rules 5. `WeatherRules` stays the handshake: the build's newest rules (now 6), which a
  client must share with the server. `WeatherContractTests hash rules5` shows a rules-5 world's weather unchanged.
- **The same bits everywhere (EA-0029).** The server and each client compute the weather separately. On Ubuntu (WSL2,
  .NET 8.0.33) 1-2 % of the answers at 40,000 places and hours had other last bits than on Windows (.NET 8.0.25): .NET's
  `Math.Exp`, `Log`, `Pow`, `Sin`, `Cos`, `Atan2` call the system's C library. None switched rain or thunder in that
  sample, but a Linux server with Windows clients could. `Core.PortableMath` (fdlibm's algorithms with only operations
  IEEE defines exactly) is used throughout the weather code; Windows .NET 8, Windows .NET 10 and Ubuntu .NET 8 now give
  the same fingerprint, as do Release and Debug builds, separate processes and a CPU without AVX/FMA intrinsics. Against
  the old maths the answers move by at most 1.5e-13 and nothing switches, so it applies to every world (no rules bump).
  Not tried: macOS and ARM (the same arithmetic; no machine here).
- **Low cloud decks over cold seas (EA-0023).** The NCEP/NCAR cover lacks the stratocumulus decks over the cold upwelling
  seas (29-30 % along the Peruvian and Namibian coasts at the deck's peak; the satellites see about 80 %). Where the sea is
  more than 1 °C colder than the open ocean's mean at its latitude (`WeatherData.ColdWater`, from weather.bin's own
  temperatures, a stand-in for the stable air over a cold sea that Klein and Hartmann tie the decks to), between about 10°
  and 45°, the month's cover is at least 28 % for each degree beyond, at most 80 %. Seasonal peaks (`WeatherTests
  lowcloud`): Peru 79 % (reanalysis 52), Namibia 79 (53), California 80 (72), Canaries 63 and Western Australia 65
  unchanged; the Peruvian coast 72 (30), the Namibian coast 65 (29; the 0.5° grid blurs the narrow Benguela upwelling).
  The cover moves the clouds drawn (the patterns' weights) and `/earthweather`; it does not change the rain.
- **Hail from Earth's thunderstorms (EA-0019)** in place of vanilla's random hail events: the strongest share of a storm's
  time, 0-9 % by the month's mean temperature at the ground (the weather's sea-level normal less 6.5 °C/km of the ground's
  height from Earth's vertical scale): none in cold months, most at 10-19 °C, none from 26 °C, where the freezing level
  is too high for hail to reach the ground. It hurts as vanilla's large hail does (`GetPrecipitationState`'s type) and is
  drawn and heard on the client (the regions' `BlendedPrecType` at the player); vanilla's hail events fall as rain or
  snow. Not fitted to hail records (none here). Three game years at stations (`WeatherTests rules6`): no hail in
  Singapore or Mumbai; 2-8 days a year at Munich, Moscow, London, Tokyo and Kansas City; 13 at Johannesburg; Denver 1
  (real about 7: Earth gives Denver too little thunder, EA-0022).
- **The weather in the temperature (EA-0025)**, on the live temperature of both sides (not world generation):
  warm and cold spells carried with the weather, as strong as at 56 GHCN-Daily stations, 1991-2020 (the daily mean's
  spread around its seasonal cycle: sd = 0.36 + 5.44 (1 - e^(-range/14)) (1 - 0.4 smoothstep(0, 20, T)), range the
  month normals' yearly range, T the month's mean at the ground; rms 0.75 °C over 574 station-months, r 0.80,
  `tools/fit_weather_temperature.py`), and the rain's own: on a wet day the afternoon is cooler and the night milder by
  what the same stations show for that warmth (wet minus dry days: a warm month's afternoon about 3 °C cooler, a
  winter's snowy night 3 °C milder). `/weather setprecip` hands both back to vanilla.

Checks: `WeatherTests` (all modes, ALL OK), `WeatherContractTests` (with the portable maths' accuracy: Exp and Log within
2.2e-16, Sin and Cos 1.1e-16, Atan2 4.4e-16, Pow 5.5e-15), `EarthWorldOptionsTests`. On the bench, a new 1:100 world:
both sides log "rules v6", the hail patch is in, `/earthweather` shows the hail, the weather's warmth and the month's
cover; in Nebraska in July the game's temperature at the player was 32.3 °C with Earth's weather and 28.5 °C with it
handed back by `/weather setprecip`, the 3.8 °C `/earthweather` reported. A thunderstorm passed overhead without hail at
that moment; hail itself was not seen falling.

Left open, with what they need:
- **EA-0024 fronts:** a cold and a warm front trailing each low (a shower line, a wind shift, a temperature step as it
  passes) belongs in `WeatherStorms` and would need its own rules version and a check against station records of
  frontal passages; not built.
- **EA-0026 fog, EA-0027 cloud edges:** both are in vanilla's drawing (the patterns' fog, the cloud tiles), which Earth
  only weights by cover per 512-block region. Tying them to Earth's fields means patching the client's cloud and fog
  renderers; not built. Rules 6's cold-sea decks are where coastal fog would start.
- **EA-0020, EA-0021, EA-0022:** the wet days, the rain rate with height and the thunder days were fitted to airport
  reports (METAR), station thunder days and WWLLN lightning, none of which are on this PC beyond 56 GHCN-Daily rain
  records; refitting them needs that data downloaded first.
- **EA-0016:** the remaining rain stripes (227 spans with climate fix v2) were audited: about two-thirds are the bake's
  switch from UDel to GPCP within a row or two of the coast (`tools/bake_earth.py`, `build_climate`, the `wt` blend; GPCP
  is about 1.4 times UDel on Arctic coasts), mostly at 55°N and north. The fix is a climate fix v3 for new worlds (and
  the same blend in `build_weather`); not built.

## Implementation

0.15.0. `tools/bake_earth.py --weather-only` → `earthdata/weather.bin` (0.5°, monthly precipitation and sea-level
temperature, land fraction). `src/Core/WeatherData.cs` (normals, ITCZ), `WeatherNoise.cs` (hash, value noise, normal
distribution; Sea's noise moved here), `WeatherModel.cs` (winds, rain, clouds, pressure), `WeatherStorms.cs` (genesis,
tracks, effects); `src/EarthWeather.cs` (patches, rain streaks, Sea link, `/earthweather`); config and `EarthParams` fields;
`SelfTest.CheckWeather`. The weather role (7 November): `src/Core/WeatherContract.cs` (the entry, storm kinds, rain,
the wind vector), `WeatherStorms.Effect.Presence` and `PresenceOf`, `EarthWeather` (claiming, `OnGetWindSpeed`'s vector,
`VanillaWindVector`'s reader patch), `SelfTest.CheckWeatherRole`, `tests/WeatherContractTests`.

Tests: `tests/WeatherTests` (`dotnet run -c Release -- <root> maps`): the ITCZ over India, West Africa, the Pacific and
northern Australia; no one-row stripes in the monthly rain; determinism; with the reanalysis, East Asia's northerly winter
monsoon and the summer cloud deck off California (78 % cloud, 0.5 % rain); measured rain share vs. the month's climate at 12 places (London, Mumbai, Rome,
Tamanrasset, Singapore, Darwin, Moscow, Manaus, Riga, Lisbon, Cherrapunji, Lima; worst excess 2 points beyond 25 %);
Mumbai 43 % of July, 0.6 % of January; Rome's dry summer; the Arabian Sea's SW/NE monsoon; trades, westerlies, roaring
forties; rain spells of hours without flicker; storm counts, basins, seasons and tracks; storm-force eyewall winds
turning the right way; the ring repeats exactly with no step at the jump line; mean sea wind 7 m/s, gales 1-2 % of the
time; 1 µs per lookup. Maps in `tests/out/weather/` (climate and prevailing winds in January and July, a moment of rain,
clouds, winds and storm tracks, a player-sized region around Biscay).

Server self-test (`tools/run_selftest.sh`, `CheckWeather`; `EXTRA_MODS="<sea zip>"` adds Sea): every patch goes in;
vanilla's precipitation at 8 places and its live rainfall where the map region is loaded are Earth's; vanilla's wind
lookups and region winds are Earth's; through vanilla's own `GetPrecipitation` Mumbai rains 0.5 % of January and 40.5 %
of July; a Sahara-side weather region picks fair-weather cloud patterns 80 % of the time against 56 % on Norway's coast;
with Sea 0.4.0, Sea's own `WindAt` (what ships use) equals Earth's wind at 4 of 4 places.

Thunderstorms (weather rules v3, 2026-11-07): `src/Core/Thunder.cs` (how many days of a month have thunder, from its
sea-level temperature, precipitation and land share; constants fitted to the WWLLN Global Lightning Climatology by
`tools/fit_thunder.py`, no lightning data shipped); `WeatherModel.ThunderAt` and `WeatherSample.Thunder` (a thunderstorm
is the top of the shower cells, its level set by Rice's formula from the spells a day the month needs and how fast the
field changes at the place; rain under it, or only its cloud where the month's air is dry); `src/EarthThunder.cs`
(vanilla's weather events strike no lightning, the server strikes through vanilla's `SpawnLightningFlash` where Earth
has thunder, the client's near and distant thunder follow Earth's storms; vanilla's `lightningFires` rule does the
burning). Tests: `WeatherTests thunder` (thunder days at 15 stations, dry lightning, afternoons; also in the full run),
`thunderrate` (measures the field's rate), `firedanger` (how often vanilla's ten-day fire test passes). Details in
[the biomes notes](../research/biomes-2026-11-07/README.md).

Wet and dry spells (weather rules v4, 2026-11-07): a third rain field in `WeatherModel.Rain`, 2000 km across and lasting
days, weighted 0.5 in cool months up to 0.9 in hot ones, so rain comes in runs of wet days with dry spells between them;
thunderstorms keep to it as far as the month's air is humid. Fitted to 56 GHCN-Daily stations' wet days and ten-day dry
spells (`tools/ghcnd_dryspells.py`, `WeatherTests dryspells <table>`); checked by `WeatherTests drydays` (21
station-months written into the test).

Rain hours (weather rules v5, 2026-11-08): `WeatherModel.RainRate`, mm per hour while it rains, by the month's
temperature, precipitation and cloud cover, fitted to the share of routine METAR reports with precipitation at the
airports next to the GHCN-Daily stations (`tools/iem_metar_precip.py`, IEM archive 2017-2023); the rain chance is the
month's precipitation over it. The spells' weight goes by temperature (0.9 in frost, 0.57 mild, 0.74 hot), refitted to
the wet days (`WeatherTests rainfreq`). Checked in the full `WeatherTests` run (eight places' January and July against
their airports) and by `drydays` (Moscow's and London's January wet days). Ground moisture (`Core.GroundMoisture`) now
reads the rain's millimetres (`WeatherModel.RainMmPerHour`) rather than its hours, so the heat's short downpours wet the
ground as much as their amount says.

## History

- 2026-10-20: the user asked whether rain and clouds follow Earth's weather and whether India gets monsoon rain (no);
  architecture options discussed.
- 2026-10-21: the user decided ownership (Earth), no separate mod, no support for other generators, Sea consuming wind
  through a generic call and its own model removed; asked for this proposal and the implementation, Sea in its own
  session. Implemented as 0.15.0 with the offline tests passing. Server self-test passes on a fresh Cylinder World
  256000×102400 world, alone and with Sea 0.4.0 (Sea's `wind-provider` branch, `a6b8c0b`: Sea logs "The wind now comes
  from Earth"). Not played in a client yet: rain and cloud visuals, cloud drift and the rain streaks are untested.
- 2026-10-21: committed `58d2984`, pushed and installed with Sea 0.4.1. The user asked for the reanalysis download and a
  rebake: 0.15.1 (weather.bin v2 with monthly winds and cloud cover; UDel's missing months filled).
- 2026-10-21: review by Claude and Codex (1 high, 5 medium, 2 low from Codex; 5 from Claude), fixed in 0.15.2:
  the storm caches keep the slots in use (least recently used go first, each built once; ConcurrentDictionary.Count
  is no longer taken per lookup), so the server catching up on 480 missed hours at pace 8 takes 0.1 s instead of
  rebuilding the storms millions of times; weather rules v2 and the server's weather.bin fingerprint
  (`EarthParams.WeatherDataSignature`): a client with other rules or data keeps vanilla weather; rain at the calendar's
  own time (vanilla's RainCloudDaysOffset left out) like the winds and storms; `RainFrequency` 0 means no rain at all,
  storms included; afternoon showers follow the local sun only when the server's AstraTerra sun really follows Earth's
  meridians (its `LongitudeAwareSun`), decided by the server (`EarthParams.WeatherLocalSun`) so every side has the
  same rain; a clear error when a world needs climatefix.bin and it is missing; the weather data kept as the file's
  bytes (about 15 MB per side instead of 60) and validated, the ITCZ search by running sums (load 250 → 115 ms); the
  rain fields' keys hashed per window and phase (a window's main octave repeated the previous one's second); the
  month fill made order-independent (no output change: the data has no two-month gaps; both files rebake
  byte-identical).
- 2026-10-21: Codex reviewed the fixes (8 resolved, 2 partial) and found three more in the new code, fixed: the cache
  trim now works on an atomic snapshot with each entry's recency read once, never drops an entry still being built or
  just used, and stamps new entries before they go in; rules v2 require the server's fingerprint (a server without
  weather.bin sends none, so its clients keep vanilla weather too). A test with 6 threads over 1400 slots against a
  fresh model passes. Final review by Claude and Codex: no findings of medium severity or above. Offline suite and the
  server self-test with Sea 0.4.1 pass on 0.15.2 (rain lookups 1.0-1.6 µs, wind 0.9 µs, catching up 480 hours at
  pace 8 in about 0.1 s). Not tested: bit-identical results across different machines (the determinism checks run on
  one host).
- 2026-11-07: the user asked for lightning and fire in grasslands and forests, then ("Yes, do it here") for lightning
  from Earth's own storms. Weather rules v3: thunderstorms from the shower cells, thunder days fitted to WGLC and
  checked against stations' yearly thunderstorm days (Singapore 144 against 153 fitted, Oklahoma City 60 against 63,
  Vienna 23 against 27, London 17 against 16); dry thunderstorms where the month's air is dry (Denver a fifth, Yakutsk
  two fifths); the rain fields at a fresh lattice phase each restart period (value noise spreads more at its lattice
  nodes than between them, which made the rare tops four times more common in some places); `EarthThunder`; the five
  play styles set `lightningFires`. Offline suite passes. Bench (world `thunder1`, 1:100): strikes under storms in
  Florida, Colorado and New Mexico and none once they passed, near and distant thunder, fire from vanilla's test bolts
  in the dry Central Valley. Not checked: long play, multiplayer, other machines. Hail is still vanilla's.
- 2026-11-07: the author's "Yes, do it here" to making dry months rain less often, so lightning fires happen more.
  Measured against GHCN-Daily, the rain was spread too evenly everywhere, not only in dry months (Vienna's summer almost
  never had ten dry days). Weather rules v4 add the wet and dry spells; dry months now rain on 5.4 % of days (real
  5.9 %) and Madrid's August passes vanilla's fire test on 82 % of days (real Augusts under 5 mm in ten days: 82 %).
  Offline suite passes. Open: cold months rain on too few days, Plains summers on too many.
- 2026-11-08: the author's "Yes, check it": rules v4 seen on the bench (world `spells1`, 1:100). Madrid's game August
  passed vanilla's fire test on every day and the first rain came after 13 dry days; Vienna from July passed on 4 of 27
  days, with a wet spell of afternoon showers and a 13-day dry spell; the client drew the rain the server reported. A
  game month is 9 days and a spell about five, so a game month is more often wet or dry as a whole than a real one.
- 2026-11-08: the author's "Yes, fix them here" to the wet days v4 left wrong. Airport reports (METAR) showed the rain
  chance far off: precipitation falls about a fifth of the time in frost and a fiftieth in the heat, where v4 rained 2 to
  5 % of the time in both. Weather rules v5 fit the rain rate to the reports; Moscow's January now has rain or snow on
  36 % of days (real 40 %, v4 20 %), London's 38 % (40 %). Ground moisture now counts the rain's millimetres, not its
  hours (with the hours right, Kano's monsoon had fallen under the level where dry land's tufts grow). Offline suite
  passes. Open: Plains summers still rain on too many days (their storms come as long night-time systems), Perth's
  winter on too few.
- 2026-11-07: Sea's open-contracts session asked for Earth's side of the open sea contracts (Sea 0.6.0-pre.1 follows a
  published weather owner). Built on `claude/weather-contract`: the `seacontracts:weather` role, storm presence in
  `WeatherStorms.Influence`, vanilla's wind as a vector with the guarded reader patch, the rain-slant transpiler removed,
  Sea's provider name "earth". Offline contract tests and the IL check pass. Sea's session checked the role against
  Sea's `FollowWeather` and found nothing to change in Earth.
- 2026-11-08: merged into the Earth 2.0.0 release candidate (the author: "Include it"), on top of weather rules v5.
- 2026-11-13 (EA-0037, the author's RC request): checked against Sea's spec sections 2-5 member by member; one gap, R2's
  re-check, now done (`RecheckRole`, `WeatherContract.MayReplace`, offline tests pass). Seen in a game for the first
  time (AgentBridge bench, 1:100, one client, Sea 0.6.0-pre.1, probe mod `tools/weatherprobe` reading only
  `api.ObjectCache` with base types): server and client gave the same answers to six decimals in the Gulf of Riga at a
  calm hour, a gale (presence 0.90, 27.4 m/s, `gale`) and a rainy hour; Sea's sea-state wind was the role's exactly;
  vanilla's wind was the role's / 25 as a vector on both sides, the two X readers transpiled by `earth.windvector`
  alone; a priority-50 rival was replaced within a second, a priority-150 one kept Earth out until removed. Not
  checked: multiplayer, a dedicated server, Rough Seas.
- 2026-11-13: the author asked to close as many of the weather items as possible for the release candidate. Portable
  maths (EA-0029), weather rules 6 frozen per world (EA-0019, EA-0023, EA-0025), audits of EA-0016 and EA-0018; see
  [Weather rules 6](#weather-rules-6-and-the-same-weather-on-every-system-13-november-2026).
