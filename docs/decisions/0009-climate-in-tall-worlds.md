# 0009: Climate temperatures in tall worlds

Status: Agreed (the user, 2026-10-24); the hook built in 0.14.1, climate rules 2 (real lapse, CHELSA temperatures) in Earth 1.0.0; in Earth 2.0.0. New worlds take climate rules 3 (0031), which build on it. No in-game check recorded | Date: 2026-10-16 | Decided by: Claude, fixing a review finding the user asked to fix; the real lapse agreed by the user 2026-10-24

## Context

Vintage Story stores one temperature byte per climate-map pixel (-20..40 °C) and reads it back less 1 °C per 6.375
blocks above sea level, cut to -20..40 °C again (`Climate.GetScaledAdjustedTemperatureFloat`). Earth stores the real
surface temperature plus that altitude compensation (`TerrainModel.ClimatePixel`), so vanilla gets the real value back
at the surface, and Earth's climate hook (`OnGetClimate`, `ExtendTemperatureRange`) adds what lies beyond -20..40 °C.

Earth's vertical scale puts mountains much higher above sea level in tall worlds, while vanilla's lapse per block stays
the same. The stored value then no longer fits the byte. Measured on the real data (sea at 0.43 of the height):

| World height | Warm highlands (Addis Ababa 16 °C, Mexico City 17 °C, Quito 12 °C) as vanilla reads them |
|---|---|
| 256, 384 | exact; nothing saturates |
| 512 | up to 3.7 °C too cold |
| 768 | 15–19 °C too cold (Addis −1 °C) |
| 1024 | 25–31 °C too cold (Addis −15 °C, La Paz −20 °C) |

The climate hook also assumed vanilla's reading was the real temperature cut to -20..40 °C. That is only true where the
stored value fits.

## Decision (implemented)

- Earth's climate hook adds exactly what vanilla's reading lacks: the real temperature at that height less vanilla's
  value with both its cuts (`TerrainModel.ClimateCorrection`). Live temperatures (crops, body temperature, weather) get
  it in every world. Within 0.24 °C (the byte's step) at heights 256–1024 in the offline test.
- The world-generation temperature that goes through the hook uses it in new worlds (`EarthParams.ClimateRules = 1`).
  Older worlds keep the earlier correction there, so their new chunks match. The two agree wherever the stored value
  fits, which is everywhere in 256–384 high worlds (the user's).
- Clients load the climate but not the terrain, so they can't know the stored value: they keep the earlier correction
  (the real temperature's part beyond vanilla's cut). Corrected 2026-10-24 (review round 4, E4-1): vanilla's client cuts its
  reading to −50..40 °C, not −20..40 like the server, so the client now undoes −50..40. That is the server's reading
  wherever the stored value fits the byte; it was wrong before wherever the real temperature lay below −20 °C (Vostok
  read about −78 instead of −55 on the client). Where the stored byte itself is cut at −20 (the coldest ice: Vostok, Dome
  A) the client still reads warmer than the server in worlds before v2: Vostok −49.0 against −55.5 in a 384-high world
  (−79.0 before), −41.2 in a 256-high one (−71.2 before); Summit in Greenland is now exact (−28.4; −36.8 before).
- Underground, the surface's correction applies (vanilla's own rule below the surface), and the real temperature never
  takes an elevation below -500 m (`TerrainModel.MinLandElevation`; the Dead Sea is -430 m). Before, the inverse of the
  seabed curve turned y 80 in a 384 world into -35 km and about 237 °C in every mine, in every world.
- A caller that supplies Earth's own temperature as the base (regional fauna's runtime spawn check) gets only the range
  extension (`EarthMapSystem.SuppliedEarthTemperature`), so highland warmth isn't added twice.

## Decision: the real lapse (climate rules v2, agreed 2026-10-24)

Most of vanilla's world generation (soil and block layers, snow, grass, ponds, creatures, deposits' climate rules) reads
the climate map directly, not through the hook, so in worlds 512+ blocks high its warm highlands stay too cold. The fix
would give Earth worlds their real lapse: a Harmony patch on vanilla's altitude adjustment (server and client) that
subtracts 0.0065 °C per real metre of Earth's vertical scale instead of 1 °C per 6.375 blocks, with the climate map
storing the sea-level temperature. It needs climate rules v2 for new worlds only, the vertical scale on the client, and
an in-game check that weather and the HUD agree. Until then: worlds 512+ blocks high have too-cold highlands in world
generation, and 256–384 high worlds are exact.

As implemented (the most efficient form the user asked for):
- **Climate map** (`TerrainModel.ClimatePixel`, rules ≥ 2): the sea-level temperature (`TMean` of the climate data),
  which fits the byte at any world height.
- **Vanilla's altitude adjustment** (`ClimateLapse`): Harmony prefixes on the four methods every reader goes through,
  `Climate.GetScaledAdjustedTemperatureFloat`, `…FloatClient`, `GetScaledAdjustedTemperature` and `GetAdjustedTemperature`
  (world generation: block layers, snow, ponds, rivulets, trees, patches, structures, deposits, creatures; the live
  climate on server and client; the client's plant and water tints; other mods' calls). They replace
  `distToSealevel / 1.5` with the real lapse in byte units, read from a table per block distance built once per world:
  `0.0065 °C × max(Elevation(y), −500 m)`, the same rule as `TrueTemperature`. Without a v2 world on that side the
  prefixes let vanilla run. Each side (server, client) claims the table for its world and drops its claim when it shuts
  down; the table goes when no side claims it, so in single player one side closing first doesn't switch the real lapse
  off under the other (review round 4, E4-2). They are installed at mod start, before anything compiles vanilla's methods into its own
  code (MonoMod.Core, which Harmony 2.4 uses, also tries to disable inlining of a patched method; the self-test checks the result).
- **Earth's hook** (`TerrainModel.ClimateCorrection`, rules ≥ 2): only vanilla's two cuts remain to undo, the byte's
  −20..40 °C and that side's final cut (−20..40 on the server, −50..40 on the client, `TerrainModel.ClientClimate`). It
  needs no terrain, so the client's complete reading is exactly the server's (E1-10's client/server difference is gone in
  these worlds; offline both sides read the real temperature within the byte's 0.2 °C down to −107 °C).
- **New worlds only**: `EarthParams.ClimateRules = 2` is written at world creation. Saved worlds keep 0 or 1 and vanilla's
  adjustment (their new chunks must match).
- **Self-test**: logs whether vanilla's method returns the real lapse, and compares the game's worldgen temperature with
  the real one at every probe site (PASS/FAIL), which a vanilla path that skipped the patch would fail.

Known effects and limits:
- Below sea level and underground the rule is `TrueTemperature`'s (at most +3.25 °C from the −500 m floor), where
  vanilla warmed 1 °C per 6.375 blocks without limit (≈ +24 °C at y 10 in a 384-high world): deep deposits and caves read
  the surface climate of their place rather than a hot one.
- Vanilla's colormap shader reverses the old lapse (`seaLevelDist * 1.5`) only to weight seasonal browning; that
  weighting stays vanilla's.
- Wmap's far-terrain shader applies vanilla's lapse to the stored byte itself (`aboveSea / 1.5`), so in v2 worlds its
  distant highlands are tinted as if warmer than the near terrain until Wmap uses the real lapse (cross-mod follow-up).
  Earth offers `TerrainModel.ClimatePixelForVanillaLapse` for that: the pixel as worlds before v2 store it (surface
  temperature plus vanilla's 1 °C per 6.375 blocks), which a reader applying vanilla's lapse turns into the real surface
  temperature, in any world (the same as `ClimatePixel` before v2).
- Cylinder World's polar hook assumes vanilla's lapse; Earth switches it off in its worlds whenever its own range
  extension is on (the default), so only a world with `ExtendTemperatureRange: false` would see it.

## Decision: CHELSA temperatures on the terrain grid (part of climate rules v2, agreed 2026-10-24)

**Context.** A user saw snow on Mauna Loa (2026-10-24). The 0.25° climate grid is UDel's 0.5° station data reduced to sea
level with 6.5 °C/km from each cell's mean height; a constant 6.5 °C/km over such coarse cells overstates the cooling on
tropical oceanic mountains (above the trade-wind inversion the real lapse from Hilo to Mauna Loa Observatory is about
4.8 °C/km). The drawn summit (y 320, 3 940 m in a 384-high world) had its warmest month at −0.7 °C, so BareRock laid
permanent snow blocks there; Mauna Kea likewise. Not a world-height problem (the same in 384- and 512-high worlds).

**Chosen** (the user, of three options: a variable lapse rate, higher-resolution data that already includes elevation,
a hybrid): "then use real data".
- **Data:** CHELSA V2.1 monthly mean air temperature 1981–2010 (tas, 30″, ERA5 downscaled with the terrain; CC BY 4.0)
  and CHELSA's own orography, from CHELSA's official hosting (WSL EnviCloud), about 3.7 GB in `rawdata/chelsa-v2.1/`
  (not in git; `tools/download_data.sh`).
- **Bake** (`tools/bake_chelsa.py` → `earthdata/temperature.bin`, 19.8 MB): per 2′ terrain cell the 4 × 4 CHELSA pixels
  are averaged together with CHELSA's height for the same pixels, and the yearly mean, coldest and warmest month are
  stored reduced to sea level with 6.5 °C/km from that reference height (0.1 °C, the terrain grid's 6° tiles).
- **In game:** `EarthData.SampleClimate` takes these temperatures (bilinear between 2′ cell centres) in worlds with
  climate rules v2 (`SetFineTemperature`, set like the climate fix, on both sides). The game's own
  `T − 0.0065 × ground elevation` then extrapolates only between CHELSA's reference height and the drawn ground:
  T(ground) = T(reference) − 0.0065 (ground − reference). Storing the sea-level value gives exactly that (it is linear in
  height, also bilinearly), so no separate height plane is stored. Temperature only: precipitation, VS rainfall and the
  summer drought stay as in climate.bin; the weather keeps weather.bin, so `/earthweather`'s rain-or-snow wording can
  differ from the live temperature on summits (the rain and snow the game shows follow the live temperature).
- **Loading:** `temperature.bin` is read when a world first turns it on (worlds before v2 never hold it); a missing or
  damaged file logs an error and leaves the 0.25° grid (`EarthData.FineTemperatureError`).
- **Coverage:** CHELSA covers land and sea from 90°S to 84°N (its sea pixels have height 0, plain ERA5-based air
  temperatures). North of 84°N the 0.25° grid stays; between 83°N and 83.95°N CHELSA fades into it (CHELSA's Arctic is up
  to a few degrees off the grid's).
- **Every reader follows:** BareRock, ponds, frozen springs, the climate map (`ClimatePixel`), `TrueTemperature`, trees,
  plants, fauna, and through the climate map vanilla's own world generation and the live climate. Older worlds and
  worlds with rules < 2 are unchanged (checked: identical column hashes with it off).

**Station check before switching on** (`tests/EarthMapTests` `CheckFineTemperature`; at each station's own height; the
normals are approximate published values, periods differ):

| Station (m) | Yearly mean: old → CHELSA (normal) | Warmest month: old → CHELSA (normal) |
|---|---|---|
| Mauna Loa Observatory (3 397) | 1.4 → 5.4 (≈ 7.0) | 2.9 → 7.8 (≈ 8.6) |
| Mauna Kea summit (4 205) | −2.7 → −0.8 | −1.1 → 1.5 |
| Hilo (11) | 24.1 → 22.6 (≈ 23.4) | 25.4 → 24.0 (≈ 24.8) |
| Quito (2 850) | 11.7 → 12.3 (≈ 14.0) | 11.9 → 12.6 (≈ 14.5) |
| La Paz El Alto (4 061) | 9.1 → 7.1 (≈ 6.5) | 11.1 → 8.9 (≈ 8.5) |
| Kilimanjaro summit (5 895) | −7.6 → −7.9 (≈ −7.1) | −5.6 → −5.9 |
| Sonnblick (3 106) | −3.6 → −7.0 (≈ −4.7) | 7.1 → 2.9 (≈ 2.5) |
| Säntis (2 502) | −5.5 → −3.1 (≈ −1.6) | 5.5 → 5.6 (≈ 6.5) |
| Jungfraujoch (3 571) | −9.4 → −9.7 (≈ −7.0) | −0.3 → 0.6 (≈ −0.5) |
| Geneva (411) | 11.4 → 11.2 (≈ 10.5) | 20.9 → 20.6 (≈ 20.0) |
| Lhasa (3 650) | 11.6 → 6.7 (≈ 8.5) | 19.3 → 15.4 (≈ 16.0) |
| Kathmandu (1 337) | 18.0 → 18.2 (≈ 18.5) | 23.1 → 23.1 (≈ 24.0) |
| Pyramid lab, Khumbu (5 050) | −2.5 → −4.8 (≈ −2.5) | 3.0 → 4.0 (≈ 3.5) |

Worst difference from the normals: 5.7 °C before, 2.7 °C with CHELSA (sharp summits, whose stations stand above their
2′ cell's mean height: Jungfraujoch, Sonnblick, Pyramid; Quito and Lhasa about 1.7 °C cool). The drawn summits as
BareRock reads them (warmest month at the block's height, climate at the chunk's centre): Mauna Loa y 320 (3 940 m)
−0.7 → +4.4 °C, Mauna Kea y 322 (4 105 m) −0.3 → +2.0 °C: above the nival 1 °C, so no permanent snow; bare rock on
moderate slopes above the tree line (5 °C), as on the real summits. A finer grid at sharp peaks was optional and is not
needed for this.

## Implementation

`src/Core/TerrainModel.cs` (`ClimateCorrection`, `StoredTemperature`), `src/EarthMapSystem.cs` (`OnGetClimate`),
`src/EarthParams.cs` (`ClimateRules`). Test: `tests/EarthMapTests` `CheckClimateStore` (heights 256, 384, 768, 1024;
highlands, plateaus, lowlands below sea level).

Climate rules v2 (branch `claude/review-fixes-earth`, review round 4; not yet released): `src/ClimateLapse.cs` (new),
`src/Core/TerrainModel.cs` (`ClimateRules`, `RealLapseRules`, `LapseAt`/`Lapse`, `ClimatePixel`, `ClimateCorrection`),
`src/EarthParams.cs` (BuildModel passes `ClimateRules`), `src/EarthMapSystem.cs` (patch at `Start`, `ClimateLapse.Use` on
server and client, new worlds get rules 2), `src/SelfTest.cs` (the two checks), `src/EarthFauna.cs` (its diagnostic reads
through vanilla's method); CHELSA: `tools/bake_chelsa.py`, `earthdata/temperature.bin`, `src/Core/FineTemperature.cs`,
`EarthData.SetFineTemperature` / `SampleClimate`, `earthdata/ATTRIBUTION.txt`. Tests: `tests/EarthMapTests` `CheckFineTemperature`
(the station table above, the drawn summits, the 84°N fade) and `CheckRealLapse`, heights 256, 384, 512, 768 and 1024 at ten
places (Addis Ababa, Bogotá, La Paz, Lhasa, Vostok, Death Valley, Everest, Riga, Mexico City, Quito), at the surface, 20
blocks above it and in a mine at y 10: vanilla's own reading is the real temperature within 0.20 °C (the byte's step)
wherever it lies in −20..40 °C, with Earth's hook within 0.20 °C everywhere, and the client's correction equals the
server's. Not checked in game.

## Experimental follow-up

The 2026-10-26 [mountain-height branch](0031-mountain-height-playtest.md) adds opt-in test profiles for new worlds. Merged
onto this record's climate rules v2 on 27 October (`claude/mountain-height-integration`), its separate temperature patches
were dropped for `ClimateLapse`; what remains is climate rules 3 for those profiles only: as v2, and the map stores the real
rainfall while vanilla's `GetRainFall` leaves it unadjusted (`ClimateLapse`), plus a client seasonal-tint adjustment
(`SeasonTintAltitude`). Default new worlds keep rules v2; older worlds keep theirs. Client playtest pending.

## History

- 2026-10-16: found by review round 9; the hook fixed and versioned; the world-generation patch proposed.
- 2026-10-17 (review round 10): the client fallback, the underground bound (an older bug: mines read about 237 °C) and
  fauna's supplied base.
- 2026-10-24: Agreed (user, 2026-10-24): "For Temperatures (decision 0009) - then we do real cooling with height in most
  efficient way. "Cost: a climate rule change for new worlds only" - does not matter as no mods are released to public".
- 2026-10-24: climate rules v2 implemented on `claude/review-fixes-earth` (review round 4, finding E1-10); offline checks
  above. In-game check still needed (self-test PASS lines; weather and the HUD agree).
- 2026-10-24: snow on Mauna Loa (user report). The user chose real data (CHELSA, download approved): "then use real data".
  CHELSA temperatures on the 2′ grid added to climate rules v2 (review round 5); station check above. Not checked in
  game.
- 2026-10-24 (review round 4 of the branch): the client undoes its own −50 cut (E4-1, also in worlds before v2), per-side
  claims on the lapse table (E4-2), `temperature.bin` read only when used and guarded (E4-4), the climate sampled once per
  map pixel (E4-5), a self-test check through vanilla's own patch placement (E4-6), `ClimatePixelForVanillaLapse` for Wmap
  (E4-3). Offline: complete readings on both sides agree within the byte's step down to −107 °C in v2 worlds.

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: Agreed (the user, 2026-10-24); hook implemented 0.14.1; real lapse (climate rules v2) implemented for new worlds on branch `claude/review-fixes-earth` (not yet released), offline checks only
