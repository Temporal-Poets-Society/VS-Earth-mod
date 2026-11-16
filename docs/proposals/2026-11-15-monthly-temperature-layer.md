# One monthly temperature layer: real seasons, day-night ranges and spells by place (15 November 2026)

Status: **proposal, after the 2.0 release set** (EA-0144). Nothing here is built. The shape of the data is the author's
decision (15 November 2026, relayed by the coordinator): "better to have just one data layer and have it then 2′ cells
for each month + correct daily and monthly spreads". How it is built is the maintainer's (D72); the downloads, the
upload and the new data release need the author's yes (listed at the end).

## The problem

Earth's live temperature today (`EarthClimateHooks.SeasonCorrection` on top of vanilla's `ModTemperature`):

- **One season shape everywhere.** Vanilla's smootherstep with the coldest day in mid-January and the warmest in mid-July
  (mirrored in the south), stretched between each 2′ cell's real coldest and warmest month (`temperature.bin`: yearly
  mean, coldest, warmest). Wrong for monsoon lands (Mumbai is warmest in May, before the rains), maritime lags (coldest in
  February on many coasts; Los Angeles and San Francisco warmest in August–September), the tropics' double peaks, and
  places whose coldest and warmest months aren't six months apart.
- **One day-night swing rule.** Vanilla's `18 − 13 × rainfall` °C, coldest at 04:00 and warmest at 16:00 whatever the
  season and the day length. Wrong in both directions: a Baltic or Danish January has a real range of about 4 °C (vanilla
  gives 10–12), a desert summer 15–20 °C, and a polar night almost none.
- **Spells from a station formula.** Weather rules 6 add warm and cold spells (`WeatherModel.TemperatureAnomaly`) whose
  size is a GHCN-Daily fit to the yearly range and the month's warmth, not the place's own.
- **Vanilla's global noise is still in.** `ModTemperature` adds `YearlyTemperatureNoise` (±3 °C) and
  `DailyTemperatureNoise` (±1 °C) that depend only on the date: the whole world warms and cools together, on top of
  Earth's spells.

## The layer

One file, `seasons.bin`, on Earth's 2′ terrain grid (10,800 × 5,400 cells, the grid of `temperature.bin`), with for each
of the twelve months and each cell:

| Plane (× 12 months) | What | Unit and step | Source |
| --- | --- | --- | --- |
| Mean | the month's mean air temperature, reduced to sea level at 6.5 °C/km from the source's own height (as `temperature.bin`) | 0.1 °C units, published at 0.5 °C | CHELSA V2.1 `tas` 1981–2010 (already on disk) |
| Day-night range | the month's mean daily maximum minus mean daily minimum | 0.5 °C | CHELSA V2.1 `tasmax` and `tasmin` 1981–2010 |
| Day-to-day spread | the standard deviation of the daily mean around the place's smooth seasonal cycle, over the days of that month in 30 years | 0.1 °C | ERA5 daily mean 2 m temperature, 1991–2020, upsampled to 2′ |

All three are on the same 2′ cells, as decided. The spread comes from a 0.25° source and is smooth, so upsampled to 2′ it
costs little after prediction.

### Sources

| Source | Resolution | Licence | Download |
| --- | --- | --- | --- |
| CHELSA V2.1 monthly `tas` 1981–2010 (Karger et al. 2017) | 30″, land and sea (no data north of 84°N) | CC BY 4.0 | **on disk** (`earth-raw/chelsa-v2.1`, 12 × ~155 MB) |
| CHELSA V2.1 monthly `tasmax` and `tasmin` 1981–2010 | 30″, land and sea | CC BY 4.0 | 24 GeoTIFFs, about **3.7 GB** (estimated from the `tas` files) |
| ERA5 post-processed daily statistics on single levels (Copernicus C3S, "derived-era5-single-levels-daily-statistics"), daily mean 2 m temperature, 1991–2020 | 0.25° (about 28 km), land and sea | CC BY 4.0 (the ERA5 licence since 2024; to be confirmed on the download page) | about **15–25 GB** as NetCDF (0.25° × 10,958 days; estimate). Needs a free Copernicus CDS account and its licence accepted. |
| Optional cross-check: NOAA CPC Global Unified daily `tmax`/`tmin` 1991–2020 | 0.5°, land only | NOAA, public (no licence restrictions stated) | about **5–8 GB** (estimate) |

Rejected: ERA5-Land daily (0.1°, land only) would be about ten times ERA5's size for a spread that doesn't need that
resolution. Berkeley Earth daily grids are CC BY-NC (non-commercial), which the suite's sources avoid. CHELSA daily
(W5E5-based) would be terabytes.

Where CHELSA has no data (north of 84°N) the means fall back to `climate.bin`'s yearly cycle as today, the range to ERA5's
daily max minus min (an extra variable from the same ERA5 product, the same size again; or a constant 2 °C over the polar
ocean, decided when the ERA5 request is made), and the spread is ERA5's everywhere.

### The file

A `TiledGrid` like `temperature.bin` (6° tiles of 180 × 180 cells, read on demand into a `TileCache`), with planes
predicted by `PlaneCoding`:

- the yearly mean, then the twelve monthly means as differences from it (they are smooth across a tile and highly
  correlated month to month, so they predict well);
- the twelve ranges;
- the twelve spreads;
- the data mask.

Size, estimated from `temperature.bin` (three planes at 0.5 °C step, 5.4 MB): the means about 10–18 MB, the ranges 5–12 MB,
the spreads 1–2 MB, together **about 15–30 MB compressed**. Step 1 measures it. With the Earth zip at 29.4 MB against the
Mod DB's 40,000,000-byte limit, it doesn't fit beside everything else, so it is planned as a **data-release download**:
one release asset on the Earth data repository (as `egl-v1` for GLO-90 and `egts-v1` for the ground sets), fetched by the
server the first time a new world needs it, kept in `ModData/earth/` for every world on the machine, checked by size and
SHA-256.

**Clients don't download it.** The server sends each client the cells of the map regions around it, as those regions
load: at 1:100 a 512-block region spans about 14 × 14 cells, 48 bytes a cell raw, under 10 KB a region before
compression, and a player's surroundings a few tens of regions. At finer scales a region holds one or two cells. So
players' games fetch nothing new, the server's file is the only copy, and client and server can't disagree. (The other
options were a client download on joining, which is a new outward-facing download and a version handshake, or the
zip, which it doesn't fit.)

## The runtime

New code in `EarthClimateHooks` for worlds with the new rules (below). It replaces vanilla's season, day-night and noise
terms instead of correcting them: Earth's handler computes vanilla's own contribution (the same formulas and seeds as
`ModTemperature.updateTemperature`) and adds the difference to its target.

1. **The month's curve.** A mean-preserving periodic curve through the twelve monthly means: a periodic quadratic or
   cubic spline whose integral over each month equals that month's mean (solved once per cell and kept with the
   tile), so the month you live through averages what the data says. The game's month is the real month (as now), the
   southern hemisphere needs no mirroring (the data are the place's own), and a season override (vanilla's calendar
   setting) picks the curve's point for that month. The fallback is the exact Fourier form (six harmonics with the
   sinc correction) if the spline overshoots at monsoon onsets; the validation decides.
2. **The day's shape, tied to the sun.** Coldest just before sunrise, warmest about 2–3 hours after solar noon (the lag
   fitted at the stations), a sine rise through the day and an exponential fall after sunset (the Parton–Logan form
   used for crop models). Sunrise and day length come from the game's own sun at that latitude and date, so they match
   the sky the player sees; with AstraTerra's longitude-aware sun the hour is local solar time (Earth's
   `LongitudeAt`). The curve is scaled so that the day's mean is the month curve's value and its max minus min is the
   month's day-night range (interpolated between months like the means). In polar day and polar night, where there is
   no sunrise, the range itself is small in the data and the shape becomes a gentle swing about local noon.
3. **Spells.** Today's warm and cold spells (the field that travels with the weather) keep their timing and size in
   space, and take their strength from the cell's day-to-day spread for that time of year instead of the station
   formula. The rain's own term (a wet afternoon cooler, a wet night milder) is kept as a deviation that averages to zero
   over a month, since the month's range already includes its wet and dry days.
4. **No global noise.** Vanilla's two date-only noise terms are taken out (computed with vanilla's seeds and subtracted).
5. **Height and the range extension** stay as now: the sea-level values go through vanilla's lapse and
   `ClimateCorrection`, and the extension beyond −20..40 °C applies.

Cost: one tile read per 6° area, cached, and a few multiplications per reading; the hook's per-thread cell cache
(rules 8) keeps the curve's coefficients for the cell. Measured with the existing hook timing (`ClimateCost`) before and
after.

## Rules and saves

- A new params field, `SeasonRules` (the next free protobuf number when built), 0 in every world made so far: those keep
  exactly today's live temperature. New worlds get 1. It touches the live temperature only, never world generation, so it
  is independent of the weather rules and of `ClimateRules`.
- **Existing 2.0 worlds can opt in**, because nothing generated depends on it: a one-shot `UpgradeExistingWorldSeasons`
  in `earth.json` (as the other `UpgradeExistingWorld…` flags) sets the world's `SeasonRules` to 1 at its next start and
  resets itself. What a player of such a world notices: different temperatures from then on (crops, food, body
  temperature, snow and rain), nothing in the terrain. Cylinder World's seasonal sea ice follows its own ocean model and
  is unaffected; the validation checks that the two still agree in timing.
- If the server can't have the file in a session (offline), the world keeps its `SeasonRules` and uses today's shape
  for that session, with a warning, and tries again at the next start (as built: nothing generated depends on it, so
  no for-good state is needed).
- The new temperatures are pinned like the weather: a fingerprint per rules in the contract tests, and the 0 rules
  unchanged bit for bit.

## Validation

Against the 56 GHCN-Daily stations already on disk (`earth-raw/ghcnd/access`; 55 have TMAX and TMIN), with the existing
fit tools (`tools/fit_weather_temperature.py`), offline through the same code the game runs, each station at its own
height:

| Check | Target |
| --- | --- |
| Monthly means | rms under 1.0 °C over all station-months after the height correction (today's rms as the baseline) |
| Warmest and coldest month | the right month at every station with a clear peak (more than 1 °C above its neighbours); named cases: Bombay Santacruz warmest in May, Los Angeles and Lisboa warmest in August, Darwin before the monsoon, Singapore's flat year |
| Day-night range | rms under 1.5 °C over station-months; Heathrow's and Jelgava's January, Alice Springs' and Phoenix's summer, Singapore |
| Day-to-day spread | the station's spread of the daily mean per month within 20 %, with the spells' field sampled over 30 game years |
| Timing within the day | coldest within an hour of sunrise and warmest 1–4 hours after noon at the stations with hourly METAR records on disk (`earth-raw/metar-iem`, used for the weather rules) |

Plus the new pins in `WeatherContractTests`, the rules-0 pin unchanged, and a bench look in a world at 1:100: the HUD
temperature over a game day in Mumbai in May and in Riga in January.

## Steps and effort

| Step | What | Effort |
| --- | --- | --- |
| 1 | Bake: `tools/bake_seasons.py` (CHELSA means and ranges to 2′, ERA5 spread to 0.25° then 2′, the polar fallback), measure the size | 1–2 days, after the downloads |
| 2 | The loader, the server download, the region packets to clients | 1–2 days |
| 3 | The runtime: curve, day shape, spells, noise removal, `SeasonRules`, the opt-in | 2 days |
| 4 | Validation tool and pins; fix what it finds | 1–2 days |
| 5 | Bench look, review (Fable) and its rounds | 1–2 days |

About **6–10 working days** of session time, plus waiting for the downloads.

## What needs the author's yes

1. **Downloads:** CHELSA V2.1 `tasmax` and `tasmin` monthly 1981–2010 (about 3.7 GB); ERA5 daily statistics, daily mean
   2 m temperature 1991–2020 (about 15–25 GB, plus the same again if the polar range is taken from ERA5's daily
   max and min). ERA5 needs a free Copernicus CDS account created and its licence accepted, which is the author's to do.
   Optionally NOAA CPC daily `tmax`/`tmin` (about 5–8 GB) as a cross-check.
2. **Upload:** the baked `seasons.bin` (about 15–30 MB) as a new release asset on the Earth data repository, credited
   to CHELSA and ERA5 (Copernicus) in the README and the data credits.
3. **A new server download** for new worlds (players' games download nothing).
4. Starting it: after the 2.0 release set, and after or alongside weather rules 10 (independent code, but both change
   what players feel, so one release is easier to explain).
