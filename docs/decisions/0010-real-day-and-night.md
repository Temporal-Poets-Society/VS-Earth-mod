# 0010: Real day and night on the Earth map (UTC clock, longitude sun, local time)

Status: **Agreed** (18 October; animal scheduling updated 31 October). Phases 1-2 built in 0.14.2 (in Earth 1.0.0); in Earth 2.0.0. Phase 5's animal schedules are in the Astra Terra fork (earth-dev.24); scripted NPC schedules deferred. Phase 3 deferred (needs Astra Terra). Phase 4 (local civil time) proposed, not built. In-game checks not run | Date: 2026-10-18 |
Decided by: the user (phases 1–2 asked for on 17 October; option C chosen on 18 October)

Plan and evidence: [`../real-day-night-cycle-plan.md`](../real-day-night-cycle-plan.md) (written 17 October by a Sea
session from the game's and AstraTerra's source). Suite context: `overview/DECISIONS.md` D06 (Earth stays geography),
D22 and O13.

## Context

On an Earth Map world, players in different places should see day and night at the right time for where they stand.
Latitude is already Earth's (Cylinder World's or Earth's `OnGetLatitude`), so the sun's height, the seasons in both
hemispheres and polar day and night are already right. What is missing is longitude: vanilla has one global clock, and
the sun stands at the same hour angle for every X.

AstraTerra (third-party, AGPL-3.0-only, optional) has a longitude-aware sun (`LongitudeAwareSun`, on by default and on in
the user's config) that shifts the day by longitude/360, and routes every longitude it uses (sun, local moon, stars,
near-body light, its clock) through `LatitudeMapper.MapWorldLongitude`. Its own mapping puts the prime meridian at
x = MapSizeX/2 on the latitude scale. On an Earth world that is the wrong meridian: x = 128000 is 14.15°E in the live
world, so today AstraTerra's sun already varies along X, but on invented meridians. The local branch
`feat/world-longitude-provider` (not pushed; the user is contacting the author first) adds
`AstraTerraModSystem.SetLongitudeProvider(Func<double, double?>)`. The installed 0.15.0 lacks it.

## Decision

### Phases 1–2 (implemented in 0.14.2, as the user asked on 17 October)

1. **Longitude service.** `EarthMapSystem.LongitudeAt(double x)` returns degrees east (-180..180) of the **sky
   meridian** at a world X, or null when Earth isn't active in the world (or the layout holds no Earth, or X isn't a
   number). Both sides, any thread, from the saved layout alone (no Earth data needed, so it works on clients whose
   climate data failed to load).
   - Sky meridian = the straight meridian `Wrap180(SeamLon0 + u·360)` (`EarthGeometry.SkyLonAt`), with u from x and the
     gutter clamped to the seam. One X column has one solar time.
   - Beyond 45° of the Bering seam it is exactly the map's longitude. Within it the map's longitude bends by
     `Warp(u)·shift(lat)` (the Pacific, eastern Siberia, Alaska) and the sky keeps the straight meridian: at most
     2.6° apart at the tested latitudes, about 10 minutes of solar time. An (x, z) provider upstream could remove this
     later; not part of this decision.
   - The gutter has the seam's meridian on both sides of the jump line, so the sky is continuous across it.
   - Map latitude and longitude of a position stay `Model.Geometry.ToGeo` (public already); no separate `GeoAt` added.
2. **AstraTerra gets it, by reflection only.** On each side, once that side has Earth's layout (server: `WorldReady`;
   client: when the world's Earth params arrive), Earth finds `AstraTerra.AstraTerraModSystem` among the loaded mod
   systems and calls `SetLongitudeProvider(LongitudeAt)` through reflection. Earth (GPL-3.0) never references the
   AGPL-3.0 assembly. On `Dispose` it passes null. Absent AstraTerra, a version without the method (0.15.0), or a call
   that throws: Earth logs once per side at notification level and goes on. It never throws into the sun; AstraTerra
   itself treats a null, non-finite or throwing answer as "no opinion".
   - Result with the branch build and `LongitudeAwareSun: true`: AstraTerra's sun, daylight, local moon, stars and
     clock follow Earth's meridians. Sea needs no change (it reflects the calendar sun and AstraTerra's moon).
   - For a UTC or zone clock, players set AstraTerra's `DisplayedClockTime` to `universal` or `zones` (`local`, the
     default, is local solar time).

### Phase 3: sun and moon without AstraTerra (for the user to decide)

Without AstraTerra someone must install a longitude-aware solar delegate (wrap the survival delegate and shift `dayRel`
by longitude/360, as AstraTerra does) and patch the moon: vanilla's moon has no longitude, and the client's cached path
(`ClientGameCalendar.Update` → `GetMoonPosition(z)` → `GetMoonCelestialAnglesFromCache`) takes only z, so a Harmony
patch must add the observer's longitude to the hour angle. It must stay off whenever AstraTerra's longitude sun is
active, or the day shifts twice.

### Phase 5: animal AI implemented in Astra Terra for Earth; scripted NPC schedules deferred

**31 October user direction:** implement animals' local day/night schedules in the Earth fork of AstraTerra and update the publication details. The fork owns this behaviour alongside its longitude-aware sun; Earth keeps the existing longitude service and gains no AI patch. Implemented in dev.24: both native AI time-window methods below read each animal's local solar hour only while the server's own longitude sun is active. Native species windows, nocturnal behaviour and random offsets are preserved; no new seasonal sunrise/sunset routine is introduced. Scripted NPC `TimeOfDayCondition` and `JumpAction` are outside this animal change and remain deferred. [Implementation and verification](../../../AstraTerra/docs/dev/local-animal-time.md).

Darkness, monster spawning, beehives and beds already follow the local sun once the sun does (they ask
`GetDayLightStrength(x, z)`). Four places in the original game read the global hour instead (VS 1.22.7, per the plan):
`AiTaskBase.IsInValidDayTimeHours`, `AiTaskBaseR.CheckDayTimeFrames` (VSEssentials), `TimeOfDayCondition.ConditionSatisfied`
and `JumpAction.Start` (VSSurvivalMod). Transpilers would replace the `HourOfDay` read with
`(HourOfDay + lon(entity.X)/360 · HoursPerDay) mod HoursPerDay`, server side, using the same sky meridian. Not a patch
on the `HourOfDay` getter (small enough for the JIT to inline). Invariant: creatures use local time only while the
visible sun does, detected without naming a mod (the sun's angles differ at two distant X), re-checked now and then.
Each call site logs once and stays vanilla if a game update changes it. Sleeping stays global (`ModSleeping` skips the
night for everyone when everyone sleeps); documented, not changed.

### Where phases 3 and 5 live: options considered on 18 October

| | A. In Earth | B. A small separate mod (for example "Earth Time") | C. Upstream only (no fallback) |
|---|---|---|---|
| What | Earth installs the fallback sun/moon and the four transpilers itself | A new mod reads Earth's `LongitudeAt` (by reflection, or a declared dependency) and does phases 3 and 5 | Phase 3 isn't built: the real sun needs AstraTerra. Phase 5 is offered upstream to AstraTerra, or goes to B |
| For | One install; no second mod to keep in step | Keeps Earth to geography (decision 0003, suite D06); sky and AI patches can break with game updates without holding up Earth's releases; works with any longitude source; easy to leave out | Least code here; one sun implementation in the suite |
| Against | Sky and creature behaviour aren't terrain; Harmony transpilers on VSEssentials/VSSurvivalMod AI code would be Earth's first patches outside world generation and climate; a broken patch means an Earth release | One more mod, manifest, inventory entry and test harness; needs Earth's API to stay stable | No real day and night without AstraTerra; depends on an AGPL project's roadmap and on the author accepting the branch |

Claude recommended **B** for both, with C's "skip when AstraTerra's longitude sun is on" rule built in.

### The user's choice (18 October): C for now

In the user's words: "for now we can go with C and just document that this would be needed to fix later if decide to
make it work without Astra".

- **Phase 3 is not built.** A sun and moon that follow longitude need AstraTerra with `SetLongitudeProvider`. Without
  it (or with 0.15.0) the game keeps one global clock: everyone sees sunrise at the same moment, whatever their
  longitude. Latitude effects (day length, seasons, polar day and night) stay right either way.
- **Phase 5 was deferred on 18 October; animal AI is implemented on 31 October.** Astra Terra for Earth dev.24 now shifts both standard AI schedule checks to each animal's local solar hour while its server sun follows longitude. Scripted NPC activity schedules remain on the global hour. In-game animal behaviour is still pending observation; the full automated suite passes.
- **To make it work without AstraTerra later**, the phase 3 sky fallback and a separate local-schedule implementation would be needed; their owner is chosen then. The animal change now lives in the AstraTerra fork and can be offered upstream, without assuming acceptance. The remaining scripted NPC work is still deferred.

### Phase 4: local civil time display (notes for the user)

- At sea: nautical zones, `round(lon/15)` hours, scaled for custom day lengths as AstraTerra's `zones` does
  (`round(lon/360 · HoursPerDay)`).
- On land: Natural Earth `ne_10m_time_zones` (public domain per Natural Earth's terms; re-check the licence and record
  attribution when downloading), rasterised to about 0.25° (1440 × 720 bytes before compression); straight-line
  boundaries are acceptable. Standard time only: the game's calendar has no real year, so no daylight saving.
- Display in `/earth` (it already prints latitude and longitude), and optionally a small HUD line such as
  "UTC 14:05 · local 15:05 (UTC+1)". Not a second copy of AstraTerra's solar clock line.
- Ownership question (open): the zone raster is political geography and could be Earth data read by `/earth`; a HUD
  line has no owner while phases 3 and 5 are deferred. Not scheduled.

## Alternatives

- **Patch vanilla's calendar directly in Earth for phases 1–2** instead of feeding AstraTerra: duplicates AstraTerra's
  sun and fights it when both are installed. Rejected for phases 1–2; it is phase 3's question.
- **Use the map's bent longitude for the sky** (`ToGeo(x, z).Lon`): needs z, which AstraTerra's provider doesn't take, and
  gives one X column several solar times near the seam. Rejected.
- **Reference AstraTerra's assembly**: not possible under the licences (GPL-3.0 Earth, AGPL-3.0-only AstraTerra) without
  relicensing questions, and would make AstraTerra a hard dependency. Rejected.

## Consequences

- Saves: nothing new is saved; the longitude is derived from the layout already in `earthmap-params`. Old worlds get it
  too. Turning AstraTerra or Earth off changes only the sky.
- Without AstraTerra or with 0.15.0: no change in behaviour, one notification line per side.
- With the branch AstraTerra and `LongitudeAwareSun: true`: daylight, monster spawning and bed checks follow the local
  sun on the server too, because they ask the sun with the position. Standard animal AI schedules now follow local solar time in Astra Terra for Earth dev.24; scripted NPC activity schedules remain deferred (phase 5).
- Performance: one modulo and a multiply per call, no allocation, no lock.

## Implementation (phases 1–2)

- Version 0.14.2 (`release/earthmap-0.14.2.zip`; not installed).
- `src/Core/EarthGeometry.cs`: `UAt` (factored out of `ToGeo`, behaviour unchanged) and `SkyLonAt`.
- `src/Core/SkyLongitude.cs`: the thread-safe service behind `EarthMapSystem.LongitudeAt`.
- `src/Core/LongitudeProviderBinding.cs`: reflection-only call of `SetLongitudeProvider(Func<double, double?>)`.
- `src/AstraTerraLongitude.cs`: finds AstraTerra's mod system, binds, logs once, clears.
- `src/EarthMapSystem.cs`, `src/EarthParams.cs` (`BuildSkyGeometry`): wiring on both sides and `Dispose`.
- Tests: `tests/SkyLongitudeTests` (`dotnet run -c Release --project tests/SkyLongitudeTests -- "<root>"`), on the live
  world's layout and a non-wrapping one: equality with `LonAt` beyond 45° of the seam, the bounded difference within it,
  gutter, jump line and wrap continuity (block by block), Lisbon (x=114898) at 9.15°W, null when inactive / unusable /
  NaN, parallel calls; binding against fakes, the branch build (end to end: Earth's provider bound through a stand-in
  world, AstraTerra's `MapWorldLongitude` returns Earth's meridian at 208 X positions, clearing restores the fallback)
  and the installed 0.15.0 package read in memory (no method: clean no-op). The branch DLL is built with the repo-local
  SDK: `$env:VINTAGE_STORY="$env:APPDATA\Vintagestory"; & "C:\VS mods\AstraTerra\.dotnet\dotnet.exe" build
  src/AstraTerra/AstraTerra.csproj -c Release`.

### In-game checks (pending; none run)

On a disposable world or a copy, never a player save, without replacing installed packages unless the user agrees:
Earth 0.14.2 + Cylinder World + the branch AstraTerra build, `LongitudeAwareSun: true`.

1. Both logs show "Earth Map longitude: AstraTerra's sky follows Earth's meridians".
2. Two clients far apart in longitude: one at noon while the other is at night; `/time set` moves both.
3. Lisbon at local noon has the sun due south; crossing the jump line has no jump in the sun.
4. A polar-day place in June and a southern one in December.
5. With the installed 0.15.0 instead: one "has no SetLongitudeProvider" line per side and otherwise unchanged behaviour.
6. Without AstraTerra: one "not loaded" line per side, Earth works as before.

## History

- 2026-10-17: plan written by a Sea session (`../real-day-night-cycle-plan.md`); the user asked for phases 1–2 and
  this record.
- 2026-10-18: phases 1–2 implemented in 0.14.2, offline tests pass. Record Proposed; phases 3–5 and their owner await
  the user.
- 2026-10-18: the user chose option C: phases 3 and 5 deferred, the real sun needs AstraTerra for now; documented as
  the work needed to make it work without AstraTerra. Record Agreed. Phase 4 still open.
- 2026-10-21: the map's seam is straight from 0.15.3 ([0019](0019-straight-seam.md)): the sky's meridian (`SkyLonAt`) now
  equals the map's longitude everywhere, so the shear exception above no longer applies.

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: **Agreed** (18 October; animal scheduling updated 31 October). Phases 1–2 implemented in 0.14.2 (offline tests pass; in-game checks pending). Phase 5's animal AI schedules are implemented in **Astra Terra for Earth 0.15.0-dev.24** (1356/1356 tests, in-game pending); scripted NPC schedules remain deferred. Phase 3 is **deferred** (the real sun needs AstraTerra for now). Phase 4 is still open
