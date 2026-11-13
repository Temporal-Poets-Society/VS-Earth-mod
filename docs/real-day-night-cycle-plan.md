# Plan: real day and night on the Earth map (UTC clock, longitude sun, time zones)

Status: **Historical proposal** (17 October 2026). This records the original design; the current agreement and implementation are in [decision 0010](decisions/0010-real-day-and-night.md). Phases 1–2 were implemented in Earth. On 31 October, the user assigns the animal portion of phase 5 to Astra Terra for Earth: dev.24 implements both standard AI schedule checks using local solar time, with 1356/1356 tests and in-game observation pending. Scripted NPC activities, the no-AstraTerra sky fallback and civil time-zone work remain deferred/open as recorded in that decision.

## Goal

On an Earth Map world, players at different places in the same multiplayer game see day and night at the right time
for where they stand: the game clock is UTC, the sun follows each place's longitude, the seasons are right in both
hemispheres, and polar day and night happen where they should. A clock can show UTC and the local time.

## What is already true (verified 17 October in source and by calculation)

- **The sun's path is real astronomy already.** VSSurvivalMod's `GetSolarSphericalCoords` uses the latitude from
  `Calendar.OnGetLatitude`, the hour of day and an axial tilt of 0.40910518 rad (23.44°) over the year:
  declination = −tilt · cos(2π(yearRel + 0.0274)), so the northern summer solstice falls at yearRel ≈ 0.473 (about
  21 June with January as month 1) and the winter solstice at yearRel ≈ 0.973 (about 22 December).
- **Latitude is Earth's.** With Cylinder World (`PolarClimate: true`, planet layout) Cylinder World sets
  `OnGetLatitude` to its signed latitude, and Earth lays its map on that same scale
  (`EarthLayout`: `BlocksPerDegreeZ = CwPoleDistance / 90`). Without Cylinder World, Earth's `ApplyLatitude` sets it.
  Latitude is signed (south negative).
- **So seasons, the southern hemisphere and polar day/night are already correct**, with no work. Hours of daylight
  from the game's formula (sun above the horizon, no refraction):

  | Latitude | ~21 June | ~22 December | March equinox |
  |---|---|---|---|
  | 78°N / 70°N | 24 h (polar day) | 0 h (polar night) | ~11.8 h |
  | 60°N | 18.5 h | 5.5 h | 11.9 h |
  | 51.5°N (London) | 16.4 h | 7.6 h | 11.9 h |
  | 38.7°N (Lisbon) | 14.7 h | 9.3 h | 11.9 h |
  | 0° | 12.0 h | 12.0 h | 12.0 h |
  | 33.9°S (Cape Town) / 38.7°S | 9.8 / 9.3 h | 14.2 / 14.7 h | 12.1 h |
  | 55°S | 6.9 h | 17.1 h | 12.1 h |
  | 70°S / 78°S | 0 h (polar night) | 24 h (polar day) | ~12.2 h |

  Real values are slightly longer (refraction, the sun's disc), e.g. London 16.6/7.8 h.
- **Seasonal temperature already flips in the south**: Earth's `SeasonCorrection` peaks at month 0.5 (N) or 6.5 (S),
  and vanilla's seasons use `OnGetHemisphere` from the latitude's sign.
- **The vanilla clock is one global clock** (no longitude anywhere in vanilla), which is what we treat as UTC.
- **Earth's longitude**: `EarthGeometry.LonAt(u, lat) = SeamLon0 + u·360 + Warp(u)·shift(lat)`. `Warp(u)` is 1 within
  5° of the seam and 0 beyond 45° from it, so **away from the Bering seam, longitude is linear in X**. Live world:
  562.3 blocks per degree east-west, world centre meridian 14.15°E, Lisbon 9.15°W at x=114898.
- **AstraTerra 0.15.0** (third-party, AGPL-3.0-only, optional) has a longitude-aware sun (`LongitudeAwareSun`,
  server-synced) that wraps the survival solar delegate and shifts `dayRel` by longitude/360. Every longitude it uses
  (sun, local moon, stars, planets, near-body light, its sky clock) goes through `LatitudeMapper.MapWorldLongitude`,
  which centres the prime meridian on the map: wrong for Earth. It already offers `DisplayedClockTime`:
  `local` (solar), `universal` (UTC) and `zones` (whole-hour offsets, `round(lon/15)`).
- **A provider hook for that exists on a local branch, not upstream yet**: `C:\VS mods\AstraTerra` (a clone of
  github.com/lalmei/astraterra, remote `upstream`), branch `feat/world-longitude-provider`, adds
  `AstraTerraModSystem.SetLongitudeProvider(System.Func<double, double?>? provider)`: degrees east for a world X, or
  null for no opinion; per world; call on both sides. Upstream's 1209 tests pass. The user is contacting the author
  first; **do not push it or open a PR**. Build it with the repo-local SDK:
  `$env:VINTAGE_STORY="$env:APPDATA\Vintagestory"; & "C:\VS mods\AstraTerra\.dotnet\dotnet.exe" build src/AstraTerra/AstraTerra.csproj -c Release`.

## Phases

### 1. Earth longitude service (Earth, generic geography)
- Public, side-neutral API on Earth's mod system, for example `double? LongitudeAt(double x)` (sky meridian, see
  below), plus `GeoAt(x, z)` if not already public.
- **Sky meridian = the unwarped longitude** `Wrap180(SeamLon0 + u·360)`, with u from x (gutter clamped). One X column
  then has one solar time. Within 45° of the seam the true map longitude bends by `Warp(u)·shift(lat)`; the sky uses
  the straight meridian there. Document this; it only affects the Pacific/Siberia/Alaska band. An (x, z) provider
  upstream is a possible later improvement, not part of this plan.
- Null when Earth is not active in the world.

### 2. Feed AstraTerra (when it is loaded and has the hook)
- At world load on **both** sides, after AstraTerra has started: find `AstraTerraModSystem` by type name and call
  `SetLongitudeProvider` **by reflection only**. Earth is GPL-3.0 and must not reference the AGPL assembly. Clear it
  on dispose.
- If AstraTerra is absent, or is 0.15.0 without the method: log once at notification level and do nothing.
- Result: with `LongitudeAwareSun: true`, AstraTerra's sun, daylight, moon, stars and clock follow Earth's meridians.
  Sea needs no change: it reflects the calendar sun and AstraTerra's local moon.
- Recommend `DisplayedClockTime: universal` or `zones` in the docs for players who want a UTC or zone clock.

### 3. Sun without AstraTerra (needs the user's decision first)
- Without AstraTerra, someone has to install a longitude-aware solar delegate (wrap the survival delegate, shift
  `dayRel` by longitude/360, like AstraTerra does) and patch the moon: vanilla's moon has no longitude, and the client's
  cached path (`ClientGameCalendar.Update` → `GetMoonPosition(z)` → `GetMoonCelestialAnglesFromCache`) takes only z,
  so it needs a Harmony patch that adds the observer's longitude to the hour angle.
- **Never with AstraTerra's longitude sun active** (it would shift twice): skip when AstraTerra is loaded.
- Open question for the user: does this belong in Earth (generic, but it is sky rather than terrain; see decision 0003
  and suite D06) or in a small separate mod? Write both options into the record; implement only after the user decides.

### 4. Local civil time display (optional, after 1–2)
- Nautical zones at sea: `round(lon/15)`. On land: a simplified public-domain dataset (Natural Earth
  `ne_10m_time_zones`) rasterised to about 0.25° (1440×720 bytes before compression); straight-line boundaries are
  acceptable. Check the licence and record attribution.
- Show it in `/earth` (it already prints latitude and longitude) and, if wanted, a small HUD line: "UTC 14:05 · local
  15:05 (WEST+1)". Do not duplicate AstraTerra's solar clock line.
- Mind custom day lengths: AstraTerra's `zones` rounds `lon/360 · hoursPerDay`; do the same.

### 5. Creature and NPC schedules on local time (user's request, 17 October)
With a longitude sun, darkness, monster spawning, beehives and bed checks already follow the local sun: they ask
`Calendar.GetDayLightStrength(x, z)`, which goes through the solar delegate with the position. Only **four** places in
the game's logic read the global hour instead (verified in VS 1.22.7 decompiles), and each has the entity at hand:

| Code | What it schedules |
|---|---|
| VSEssentials `AiTaskBase.IsInValidDayTimeHours` | AI tasks with `duringDayTimeFrames` in entity JSON (sleeping, idling, roaming hours) |
| VSEssentials `AiTaskBaseR.CheckDayTimeFrames` | the same for the newer AI task base |
| VSSurvivalMod `TimeOfDayCondition.ConditionSatisfied` | villager/NPC activity schedules (`EntityActivitySystem`) |
| VSSurvivalMod `JumpAction.Start` | "jump until hour X" steps in those activity lists |

(`GameCalendar.PrettyDate` also reads it, but that is the displayed clock only.)

Approach: local hour = `(HourOfDay + longitude(entity.X) / 360 · HoursPerDay) mod HoursPerDay`, using the same sky
meridian as phase 1. Harmony transpilers on those four methods replace the `HourOfDay` read with a helper that takes
the calendar and the entity. Do not patch the `HourOfDay` getter itself: it is tiny, and .NET's dynamic PGO can inline
it, so a patch there would be unreliable. Keep vanilla's per-task time inaccuracy as it is.

**Invariant: creatures may only use local time while the visible sun does.** Otherwise animals would sleep at noon.
Detect it without depending on any one mod: the sun follows longitude when
`Calendar.GetCelestialAngles(x1, z, t)` and `(x2, z, t)` differ for two X positions far apart. That's true with
AstraTerra's `LongitudeAwareSun` or the phase 3 fallback, and false otherwise. Re-check now and then, not per call.
Server-authoritative: AI runs on the server.

Ownership is the same open question as phase 3 (Earth or a small separate mod); record both with phase 3.

**Sleeping stays global.** `ModSleeping` only fast-forwards when every player sleeps, and then for everyone. Players at
different longitudes rarely sleep together, but as the user notes, in a bigger multiplayer game that almost never
happens anyway. Document it; no change planned.

## Tests and verification
- Offline: `LongitudeAt` against `LonAt` away from the seam (exact), across the gutter and wrap, and the bounded
  difference within 45° of the seam; Lisbon (x=114898) ≈ 9.15°W; the provider returns null when Earth is inactive.
- Reflection binding: binds when the method exists (branch DLL), no-ops cleanly on the installed 0.15.0, and never
  throws into the sun.
- Phase 5 (when built): local hour wraps correctly across midnight and custom day lengths; the four call sites
  bind (log once and stay vanilla if a game update changes them); the sun-follows-longitude probe is true with a
  longitude sun and false without.
- Optional calculation check: daylight hours for the table above, from the game's formula plus the provider.
- In game (disposable world or copy only; never a player save; don't install over the user's packages without asking):
  Earth + Cylinder World + the branch AstraTerra build with `LongitudeAwareSun: true`. Two players (or two clients) far
  apart in longitude: one at noon while the other is at night; `/time` changes move both. Lisbon noon has the sun due
  south. Check a polar-day location in June and a southern one in December.

## Done when
Phases 1–2 implemented in an Earth pre-release with offline tests; record `0010` written (Proposed until the user
agrees); README and the suite overview card, roadmap and inventory updated per `C:\VS mods\AGENTS.md`; Phase 3 and 4
choices (and phase 5's owner) put to the user; in-game checks listed as pending until actually run.
