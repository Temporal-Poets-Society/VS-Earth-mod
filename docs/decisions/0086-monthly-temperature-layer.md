# 0086: One monthly temperature layer: the real seasons, day-night ranges and spells by place

Status: **implemented** in Earth 2.1.0 (EA-0144). New worlds only;
existing worlds keep their live temperature (see "Existing worlds"). Date: 2026-11-15.
Decided by: the author ("ok build the new temperature. We will delay publishing", 15 November 2026; the shape: "better
to have just one data layer and have it then 2′ cells for each month + correct daily and monthly spreads"). How it is
built is the maintainer's (D72). The proposal it implements: [2026-11-15-monthly-temperature-layer](../proposals/2026-11-15-monthly-temperature-layer.md).

## Context

Earth's live temperature (seasons rules 0, every world before this decision) is vanilla's `ModTemperature` corrected by
`EarthClimateHooks.SeasonCorrection`: one season shape everywhere (vanilla's smootherstep, coldest in mid-January,
warmest in mid-July, mirrored south) stretched between each 2′ cell's real coldest and warmest month, vanilla's day-night
rule (18 − 13 × rainfall °C, coldest at 04:00, warmest at 16:00 whatever the sun), weather rules 6's spells sized by a
station formula, and vanilla's two date-only noise terms (±3 °C and ±1 °C, the same everywhere at once).

## Decision

### The layer

One data layer on Earth's 2′ terrain grid (10,800 × 5,400, rows from 90°N, columns from 180°W), for each month and cell:
the month's **mean** (CHELSA V2.1 `tas` 1981–2010, the 4 × 4 pixels of the cell, reduced to sea level at 6.5 °C/km from
CHELSA's own height as `temperature.bin`; int16 °C × 10 at a 0.5 °C step), its **day-night range** (CHELSA V2.1 `tasmax` −
`tasmin`; uint8, 0.2 °C) and its **day-to-day spread** (the standard deviation of the daily mean around the place's smooth
seasonal cycle, from ERA5's daily means, 0.25° interpolated to 2′; uint8, 0.1 °C), and a provenance byte (bit 0 the mean
measured, bit 1 the range, bit 2 the spread). Cells the source doesn't cover (north of 84°N) are filled from the
nearest measured cell in their column, so the reader meets no holes. Baked by `tools/bake_seasons.py`.

Split of the work (agreed with the data session, 15 November): the values and the bake are the weather side's; the file
format (PlaneCoding planes, each month predicted from the month before), its writer, the data-store entry, the
data-release download for new worlds and the reader are the data side's. Clients don't download it: the server sends each
client the tiles around it over Earth's channel, and until a tile has arrived the client uses rules 0's shape.

### Data side (the data session's, merged on main as 8ccae4f)

- **The file:** `seasons-1.bin` (magic `EMSN`; a rebake gets a new name, `seasons-2.bin`, never the same name with other
  bytes), written by `tools/write_seasons.py` from the bake's `seasons-layer.npz`, read by `Core.SeasonData`
  (`TryCell`, `TryCells4`, `TryProvenance`). 90-cell tiles (3°, about 3,600 blocks at 1:100): a decoded tile is 397 kB
  against 1.6 MB at 180 cells, for about 10 % more file; each month's plane coded on its own (it beat month-to-month
  differences on every probed tile). The server keeps only the index and at most 12 decoded tiles (about 4.8 MB).
- **The download:** from Earth's data release into `ModData/earth/data`, checked by size and SHA-256, at InitWorld of a
  world with `SeasonRules` 1 or more. **No for-good choice:** a session that can't have the file (offline, not published
  yet) keeps the world's rules and runs on rules 0's shape that session, with a warning, and tries again at the next start;
  nothing generated depends on the layer.
- **Players' games download nothing:** the server sends each player the 3 × 3 tiles around them, checked every second, so a
  teleport's destination follows within about a second (Earth's channel). Until a tile has arrived the reading falls back to rules 0's shape.

### The runtime (seasons rules 1)

- **The month's curve** (`tools/season_curve.py`): a periodic cubic spline through knots at the months' middles, the knot
  values chosen so that the curve's average over each month is that month's mean. With the game's twelve equal months
  the curve is a fixed table of twelve weights on the means (384 points, 32 a month, linear between them); the table's
  month averages are exact to 3 × 10⁻⁴ of a degree per degree of mean. The game's month is the real month; the southern
  hemisphere needs no mirroring.
- **The day's shape** (`tools/fit_day_shape.py`): Parton and Logan's: coldest 0.49 h after sunrise, rising as a sine with
  the lag 1.60 h (its half-period the day's length + 2 × 1.60) to its top 2.09 h after solar noon, whatever the day's
  length, falling exponentially after sunset (b = 2.24) to the next minimum, scaled so that the day's mean is the month
  curve's value and its maximum minus minimum the month's range. Sunrise and sunset come from the calendar's own sun
  (`OnGetSolarSphericalCoords`, which AstraTerra replaces with its longitude-aware sun), so the day follows the sky the
  player sees. **Short days and nights** (Fable's review F1): the shape is blended with a plain cosine warmest at the same
  hour, by a smooth step of the shorter of day and night (all cosine below 0.75 h, all Parton and Logan from 4 h), so the
  shape changes continuously with the day's length (a day 0.01 h longer moves it by at most 0.02 of its range; a switch
  at 0.75 h moved it 0.77) and the weeks around the polar night have no spike at noon; a sun that doesn't set or rise
  takes its noon from the hour of the highest sampled sun. `SeasonTests` checks the continuity and the polar edges.
- **The cost** (Fable's F2): sunrise, sunset and noon are kept per thread in a table of 256 entries (about 12 kB),
  keyed by the world, the day and a 2048 × 256-block patch. Vanilla's catch-up loops read many places and days in turn:
  the data session measured 20.8 % misses with 1 player and 54.5 % with 13 at 32 entries, 1.2 % and 7.6 % at 256. The
  layer's cells come from the data side's `TryMonths4` and a tile cache sized by the players online: eight, plus for each
  player the tiles their loaded land can touch at the world's scale and the server's largest view distance
  (`SeasonData.TilesPerPlayer`: four at the usual distances, nine at 1:200 past a radius of about 30 chunks; Fable's
  re-review of 47c9b98), 12 to 256.
- **`RealSeasons: false`** (earth.json, read at every start) keeps vanilla's seasons and day-night swing in every world,
  rules 1 included (Fable's F3: before, it acted only where the layer was missing); Earth's weather spells still come on
  top, as in rules 0.
- **Spells**: weather rules 6's field keeps its motion and takes its size from the cell's spread for that time of year;
  the rain's own term becomes a deviation that averages to zero over a month (the month's range includes its wet days).
- **No global noise**: vanilla's `YearlyTemperatureNoise` and `DailyTemperatureNoise` are computed with vanilla's seeds
  and taken out.
- Height and the range extension work as today.

### Existing worlds

`SeasonRules` (params field 210): 0 in every world made before, which keep exactly today's live temperature; new worlds
get 1. A world with rules 1 whose server lacks the file (or a client before its tile arrives) keeps rules 0's shape
reading by reading, so nothing breaks while the file is fetched.

**Existing 2.0 worlds may opt in** (the maintainer's call, D72): the one-shot `UpgradeExistingWorldSeasons` in `earth.json`
sets the world's `SeasonRules` to 1 at its next start and resets itself. Reason: world generation never reads the seasons
rules (the hook returns before rules 0's correction only for live readings; world generation reads `WorldGenValues`), so no
chunk can disagree with another; what changes is what players feel from then on (crops, food, body temperature, rain or
snow), which is the point. Not automatic, because a running world's players would meet changed temperatures without asking
for them. **What such a world gets** (Fable's F7, kept as a choice): the layer's months carry the 1318 shift (decision
0087), while an upgraded 2.0 world keeps its modern climate map (`Climate1318` 0). The yearly level cancels (the curve is
taken against the twelve months' own mean), but the months' split stays 1318's, so its January is a little colder against
its own year than its modern climate says (Moscow about 1 °C, the Arctic more). Kept: one file serves every world, the
split is small next to the spells, and opting in is a choice for 1318's seasons anyway.

**Handler order** (Fable's F5): what Earth takes out of vanilla's reading is ModTemperature's season, day and noise, right
because Earth's handler runs after ModTemperature's (vanilla subscribes in `Start`, Earth at server and client start). The
day term's rainfall is read from `WorldgenRainfall`, the climate map's, so it is the same whether Earth's handler runs before
or after the weather system's, which replaces `Rainfall` with the precipitation now. `/earthweather`'s seasons line prints
what is left over (the live temperature less its base and the parts): 0 when the handlers add up.

## Evidence so far

At the GHCN-Daily stations (`tools/check_seasons.py`; 47 of the 56 have every month in 1981–2010), the means of the layer
against rules 0:

| | Layer | Rules 0 |
| --- | --- | --- |
| Monthly means, rms over 564 station-months | 0.79 °C (bias −0.27) | 1.96 °C (bias −0.35) |
| Day by day against the stations' smoothed cycle, rms | 0.85 °C | 2.02 °C |
| Warmest month right | 40 of 47 | 30 of 47 |
| Coldest month right | 36 of 47 | 32 of 47 |

Bombay, Nagpur, Mandalay and Chiang Mai are now warmest in April–May, Delhi and Karachi in June, Tokyo, Lisbon, Miami and
Los Angeles in August, Harare in November; the misses left are December/January near-ties. The curve's peak lies above the
warmest month's mean by 0.16 °C in the median cell (99 %: 1.7 °C), as a real daily cycle does; the day-by-day score shows
it isn't ringing.

At the 22 METAR stations with hourly temperatures (`tools/fit_day_shape.py`, 264 station-months, 2017–2023), the scaled
daily cycle: Parton–Logan rms 0.066 against vanilla's 0.165; the coldest hour off by 0.63 h (vanilla 2.06 h), the warmest
by 0.59 h (vanilla 1.15 h).

The day-night range (CHELSA `tasmax` − `tasmin`, downloaded 15 November with the author's yes) at the same stations,
564 station-months: rms 1.83 °C (bias −0.25) against rules 0's vanilla rule (18 − 13 × rainfall with Earth's rainfall)
3.41 °C (bias +1.50). CHELSA's own range is narrower than the stations' by about a tenth (rms 2.12, bias −1.27: its daily
extremes come from a smoothed reanalysis); the bake scales it by 1.10, the least-squares factor over the station-months
(1.099). Taking a coastal cell's range from its land pixels only was tried and changed nothing (rms 2.12, bias −1.26),
so the narrowing isn't the sea's.

The spells' size: the weather's spell field, sampled once a game day over three game years at 40 places
(`SeasonTests spell`), has a spread of 0.99 (0.87 to 1.06 by place) and a mean of 0, so rules 1's `spread × spell` gives
each place the day-to-day spread the data say.

The day-to-day spread (ERA5 daily means). **The years: 1991–1998, 2000 and 2001**, the ten in hand when the author
decided on 16 November to release 2.1.0 on them ("Use 10 years"; first "20 years enough", 1991–2010, but the Copernicus
queue slowed to hours a year and stopped delivering after 2001). Reasons: against the 9-year provisional bake the ten
years move the spread by 0.13 °C rms over the globe (bias −0.01), and the station check holds (rms 0.36 °C against rules
0's 0.77). The spread enters only the live temperature, never world generation or the saved params, so a later 20-year
file (a new name, `seasons-2.bin`) would change no save; it is post-release weather tuning (EA-0145), taken only if it
moves the spread clearly more than 0.13 °C rms. The years are listed in `tools/bake_seasons.py` (`ERA5_YEAR_LIST`).
Before that decision: With spells lasting several days the sampling error of a month's spread
is about 8–10 % from 9 years, 6 % from 20 (about 0.2 °C on a 3 °C spread) and 5 % from 30. 2020, fetched first as the size
test, is left out (`ERA5_YEARS` in the bake), so the spread is one unbroken period close to the layer's 1981–2010 normals
rather than 20 years and a detached recent one. A provisional bake from 9 years (1991–1998 and 2020) at the same
stations: rms 0.38 °C (bias −0.18) against weather rules 6's station fit 0.77 °C (bias −0.02), though that fit was made on
these stations. A candidate bake from 10 years (1991–1998, 2000, 2001; 16 November, while the rest queued at Copernicus):
rms 0.36 °C (bias −0.19) at the stations; against the 9-year bake it moves the spread by 0.13 °C rms over the globe
(bias −0.01, 7.5 % relative, about the sampling error expected; the largest single cells about 2 °C, in the Sea of Okhotsk in
February and on the Yamal coast in August, where one year's sea ice or heat decides).

**In game** (16 November, the data session's bench with the provisional file, a 1:100 world with seasons rules 1; the live
temperature from the server's reading at the player every 2–3 game hours, with `/earthweather`'s seasons line):
- A bug found and fixed: the sun never rose (every day was taken as polar night), because vanilla's survival sun gives its
  "zenith angle" as 2π − acos(sin of the altitude); the sun's height is its cosine, as `GetSunPosition` reads it.
- After the fix the live temperature less the layer's parts (curve, the day's part, spell × spread, the rain's day part)
  is one constant at each place (the place's yearly mean at the ground), so the hook adds what it should.
- Riga and Copenhagen in January: sun up about 08:20–15:40; the day's part from −1.6 °C before sunrise to +2.3 °C in the
  early afternoon (a range of 3.9–4.8 °C, the real one about 4 °C; vanilla's rule gave 10–12); passing spells move the
  whole day by several degrees. Mumbai in May: coldest at sunrise (26.5 °C), warmest at 15:00 (30.8 °C), the month 2.8 °C
  above the year's mean (its warmest); in July 25.4–28.2 °C, the curve 0.2 °C above the mean (the monsoon). San Francisco
  in September: 8.4–17.3 °C, the curve 3.0 °C above the year's mean (its warmest month). Riyadh in July: 30–46 °C, coldest
  at sunrise. Longyearbyen in January: no sunrise, the gentle polar swing (±2.4 °C) and spells to −10 °C.
