# 0018: Climate without UDel's one-row stripes (climatefix.bin, per world)

Status: Agreed (the user, 2026-10-21: "fix climate.bin with version gate"); implemented 0.15.1; cause corrected
2026-11-07 (a cut-short download, see Correction): fix v2 (climatefix2.bin, the complete UDel normals) for new worlds,
the user's "rebake for full data" (2026-11-07); fix v3 (climatefix3.bin, the coast band) for new worlds from 2026-11-13,
the author's "fix open issues that would change new worlds' terrain before the release" (EA-0016); fix v4
(climatefix4.bin, the desert coasts) for new worlds from 2026-11-14 (EA-0060) | Date: 2026-10-21 |
Decided by: the user

## Context

Wmap's terrain map showed straight horizontal green lines and bands across Africa and Siberia (reported by the Wmap
session, 2026-10-21). They are in Earth's climate grid: single 0.25° rows differ from both neighbours, e.g. at 60°E
the rainfall of the row at 48.88°N was 91.7 between 66.0 and 56.0, in Chad (20°E) the warmest month at 11.85°N was
30.9 °C between 36.8 and 35.3. World generation reads the same grid (forest amount, soil, grass and leaf tints), so the
stripes are in the game world too.

Cause: University of Delaware's precipitation normals lack exactly one month in 12 642 land cells (about 15 % of the
land, in runs along rows). The bake counted a cell with any missing month as having no land data, so on the two 0.25°
rows around such a UDel row the coarse ocean data (GPCP rain, NCEP 2 m temperature without the height correction) took
over. The source data itself is smooth there.

## Decision

- The bake fills a missing month from the months around it (`fill_month_gaps`), north of 60°S only: Antarctica's climate
  comes from NCEP and GPCP as before, and the ice sheets are still judged by UDel's own coverage.
- **Per world:** `climate.bin` stays as it was; `earthdata/climatefix.bin` holds the 57 480 cells (5.5 %) the fill
  changes, old and new values. `EarthParams.ClimateData` 1 applies it (`EarthData.SetClimateFix`, server and client): new
  worlds from 0.15.1. Older worlds keep their climate, so new chunks match old ones; they opt in with the one-shot
  `UpgradeExistingWorldClimateData` (new chunks then get the fixed climate and may meet old ones with a straight edge in
  forests, soil and grass along the old stripes).
- The weather's own data (`weather.bin`, [0017](0017-weather.md)) has the fill everywhere (runtime only, no save
  involved).

## Alternatives

- **Rebake climate.bin for everyone:** every existing world would get straight edges where new chunks meet old ones
  along the stripes. Rejected for the save policy ([0002](0002-save-compatibility.md)).
- **A second full climate file:** 3.4 MB more in the mod and twice the memory; the patch is 0.36 MB.

## Consequences

- Wmap re-bakes its climate raster when Earth's version changes, so its map follows by itself (Wmap session).
- Mountain cells inside the old stripes change most (up to 17 °C sea-level equivalent in the Zagros and Tibet: they had
  had NCEP's temperature without the height correction).
- Measured (`tests/WeatherTests`): row spans of 5° offset like a stripe (mean difference from the rows around over 6 in
  rain or 0.8 °C in the warmest month) 3212 → 857; the rest are mostly mountains and coasts. At the reported places: 60°E
  48.88°N rain 90.5 → 66.0 (neighbours 66.3, 56.7); 20°E 11.85°N warmest month 30.8 → 36.1 °C (36.6, 35.3), rain 96.6 →
  64.2; 75°E 48.88°N rain 102.3 → 91.5, but its warmest month stays 28.1 °C between 24.2 and 25.8: that spike is not
  from the missing months.

## Implementation

0.15.1. `tools/bake_earth.py --climate-fix-only` (checks that the unfilled build still reproduces `climate.bin`, but
for rounding between library versions: 5 values by up to 2 steps), `src/Core/EarthData.cs` (`climatefix.bin`,
`SetClimateFix`), `EarthParams.ClimateData` (member 70), `EarthMapSystem` (new worlds, the upgrade flag, the client),
config `UpgradeExistingWorldClimateData`. Test: `tests/WeatherTests` `CheckClimateFix` (stripe cells without and with the
fix, the probe places, switching back restores older worlds' climate exactly).

## Correction (2026-11-07): the missing months were a cut-short download

The "missing months" are not in UDel. `rawdata/udel.airt.precip_precip.mon.v501.ltm.nc` was a download cut short at
11 151 009 of 18 669 612 bytes (netCDF-3, which netCDF-C reads past the end without an error). The precip variable ends
three quarters into November (about 45°S): 9 929 land cells read December and 2 713 November as missing (the 12 642
above), and the other ~75 600 read December (and ~21 600 cells south of 45°S read November) as 0 mm. The complete
file (PSL, last modified 2022-01-12) has all twelve months in all 85 794 land cells, and its first 11 151 009 bytes are
the old file's. Found from the ranges proposal's step 5b (December precipitation missing over land in weather.bin).

- **weather.bin** (runtime only) is rebaked from the complete file: December over land like November and January
  (Riga 46 mm, Moscow 51, Chicago 53, Tucson 31; before, 0). The bake no longer fills months for it, stops on a file cut
  short (`netcdf3_size`) and before writing a month that is 0 mm over more than 4 % of the land north of 60°S;
  `tests/WeatherTests` `CheckMonthsOverLand` checks the stored file the same way. The rebake changes the weather.bin
  fingerprint: see [0017](0017-weather.md).
- **climate.bin and climatefix.bin** have the same gap and stay as existing worlds have them ([0002](0002-save-compatibility.md)):
  `build_climate` reads the cut-short bytes explicitly (`UDEL_LEGACY_PRECIP`, rebuilt from the complete file's first
  bytes when missing) and still reproduces both (climatefix.bin byte for byte).
- **Fix v2, `earthdata/climatefix2.bin`** (the user, 2026-11-07: "rebake for full data"): the climate built from the
  complete file, as the cells of climate.bin it changes (334 733 of 1 036 800, 32 %; 1.86 MB), like v1 against
  climate.bin itself. `EarthParams.ClimateData` 2 applies it: new worlds from this build (the newest fix whose file is
  there). Worlds with 0 or 1 keep their climate; `UpgradeExistingWorldClimateData` now moves a world to the newest fix
  (new chunks may meet old ones with a straight edge in forests, soil and grass). Against v1, on land north of 60°S: yearly
  precipitation a median 5.6 % higher (10–90 %: 0 to 15 %), VS rain a median +2.5 (by 5 or more on 20 % of the land,
  10 or more on 6 %), summer dryness by 0.1 or more on 3 %. Riga 582 → 627 mm (rain 125 → 129), Moscow 634 → 684,
  Chicago 825 → 882, Tucson 325 → 358 (61 → 65). v1 still took 3 888 high cells (all over 500 m, all in the old stripe
  rows: Tibet, the Pamirs, the Zagros, the Andes) for an ice sheet without UDel data and gave them NCEP's temperature;
  v2 has UDel there (sea-level-equivalent mean up to 49.1 → 40.8 °C at most). Row spans offset like a stripe (`tests/WeatherTests`): 3212 without a fix, 857 with v1, 347 with v2. Antarctica: UDel covers 24 109 cells
  south of 60°S in the complete file against 18 620 in the cut one, so 13 % of its cells change by over 2 °C. FineRain
  worlds ([0031](0031-mountain-height-playtest.md)) take rainfall and summer dryness from CHELSA (rain.bin); their
  yearly precipitation in mm (dunes, geology) and temperatures outside CHELSA follow the fix version too.
- `tools/download_data.sh` downloads into `.part` files (renamed when complete) and fetches UDel from
  downloads.psl.noaa.gov into `rawdata/udel-v501/` (PSL's THREDDS server answered 429).

## Correction (2026-11-13): the coast switch (fix v3)

The 227 rain spans left with v2 (EA-0016) were audited one by one against UDel's own rows and CHELSA's yearly
precipitation (bio12, 30″, as 0.25° means). 142 were made by the bake: `build_climate` blends UDel into the ocean data by
`wt = clip((wU - 0.25) / 0.5, 0, 1)`, wU the bilinear share of UDel nodes with data, so UDel switches off within one or
two 0.25° rows past its last land node and GPCP takes over at its own level. The two differ most where the gauges catch
too little snow, which GPCP corrects for (GPCP about 1.4 times UDel on Arctic coasts), and in the wet tropics: the
switch row offsets a whole 5° span, along the coasts of Siberia (42 spans), Alaska (35), Greenland (17), Central America
and the Caribbean (21) and SE Asia (19). 51 spans follow relief (CHELSA has them), 23 are switch rows that CHELSA has a
gradient for anyway, 10 are UDel's own.

- **Fix v3, `earthdata/climatefix3.bin`** (the author, 2026-11-13: "fix open issues that would change new worlds' terrain
  before the release"): v2 with UDel's precipitation faded into GPCP's over a band of 4 cells (1°) from the edge of its
  coverage, a smoothstep of the distance to the nearest cell without UDel nodes near it (`coast_band_weight`,
  `COAST_BAND`), instead of over the one row where wU falls. The temperatures (worlds take theirs from CHELSA,
  [0009](0009-climate-in-tall-worlds.md)), the ice sheets and everything south of 60°S keep v2's one-row weight. `EarthParams.ClimateData` 3:
  new worlds from this build; older worlds keep 0, 1 or 2.
- **Alternatives tried** (offline, against CHELSA): scaling GPCP to UDel's level near the coast left 65 rain spans but
  moved the changed cells away from CHELSA (closer on 47 %; the Arctic coasts lost the precipitation GPCP's gauge
  correction gives them); CHELSA scaled to UDel's level as the coastal value left 183 (CHELSA has coast gradients of its
  own); blending only where GPCP is wetter, or with a narrower band where it is drier, left 81-147. A band of 3, 4, 6, 8
  or 12 cells left 68, 65, 63, 58 or 44 spans and changed 19 000 to 83 000 land cells; 4 cells is where the spans stop
  falling fast. A wider band dries orographic coasts the coarse 2.5° GPCP can't resolve (the Western Ghats).
- **What changes**: 43 914 cells (4.2 %), 28 745 of the 352 903 land cells (at least half land; 8.1 %), by more than 5 in
  VS rain on 11 869; mean +3.5, median 4, at most +80 / -69 (a few 0.25° cells at narrow coasts). Their yearly
  precipitation is closer to CHELSA's on 67 % of them (median |log ratio| 0.291 → 0.249; 3 389 cells closer by more
  than 25 %, 900 further). Utqiaġvik 164 → 225 mm (rain 77 → 93), Nome 567 → 535, Riga 627 → 727 (CHELSA 718), Mumbai
  1203 → 1506 (rain 100 → 134; CHELSA 2621), Bergen 2213 → 2088; inland places (Moscow, Dikson, Khatanga, Tiksi,
  Belize City, Nuuk) are as in v2. Coastal deserts too wet as in v2 (CHELSA under 250 mm, the grid over three times
  that): 1 305 land cells north of 60°S with v2, 1 347 with v3 (mostly UDel's own in Arabia and the Sahel; the Peruvian
  coast gains a few, Lima is 497 mm in v2 against CHELSA's 24, 434 in v3).
- **Spans** (`tests/WeatherTests`): rain 227 → 67 (warmest month 120, as v2: v3 leaves the temperatures). Of the 67, 45
  follow relief that CHELSA has too, 10 partly, 8 are UDel's own gradients in high mountains (2-5.5 km of relief within
  50 km: Kilimanjaro, the Colombian Andes, Chiapas, Bhutan, the Chugach), 3 are switch rows the band can't reach (the
  Gulf of Tomini's narrow arms, Sulawesi; the Peruvian coast at 10°S) and 1 is over the open Atlantic (27.9°N, no land).
- **File layout** (the mod zip's 40 MB limit): fixes 1 and 2 are each against climate.bin; fix 3 and later are against the
  fix before and hold only what they change: header EMCF, version, width, height, count, plane mask (precipitation,
  rainfall and summer dryness for v3), then brotli of the cell indices as steps and the masked planes (103 933 bytes,
  against 1.92 MB as a v2-style file of all six planes). `EarthDataset` applies v2 then v3 and counts v3 as there only
  with v2. The Earth zip grows to 39 853 499 bytes (146 501 under the limit).
- **Not changed**: weather.bin (the weather's monthly normals at 0.5°, read by the zones' dry months and the fire rules)
  blends the same way in `build_weather`; it is pinned (worlds freeze its signature), so a coast band there needs a new
  weather file and a new zone and fire rules version. Its own stripe count (`CheckStripes`, land rows only) is 240 cells,
  all UDel's own (EA-0018).

## Correction (2026-11-14): the desert coasts (fix v4, EA-0060)

The coastal deserts left too wet by v3 (Lima's cell 434 mm against CHELSA's 24) were traced through the bake: at Lima
UDel's own 0.5° value is 94 mm, GPCP's is 497, and the coast band gives GPCP 84 % of the cell. GPCP's 2.5° cells mix the
Humboldt and Benguela coasts with the Andes and the tropics (Arica UDel 31 / GPCP 210, Walvis Bay 20 / 125, Antofagasta
5 / 37). Inland, where the grid is UDel's alone, the other cells the audit counted (the Sahel's northern edge, Arabia) are
not settled: at 16-18°N UDel is wetter than CHELSA, and the stations there (Agadez about 160 mm against CHELSA's 60 and the
grid's 250 at 16°N 10°E) side with neither.

- **What new worlds read already:** worlds made from the menu read CHELSA's rain and summer dryness from rain.bin
  (climate rules 3, decision 0031), so Lima's VS rain there is 10.6 and its aridity 0.03 (a desert) whatever the grid says.
  The grid's millimetres still reach the water table, the springs and the regional geology, and the weather's own normals
  (weather.bin, the same blend at 0.5°: Lima 320 mm a year) made it rain on the desert coast.
- **Fix v4, `earthdata/climatefix4.bin`** (13 kB, against v3): GPCP's share of the blend at CHELSA's level where GPCP is
  much wetter than CHELSA on a dry coast: GPCP's year becomes GPCP^(1-k) CHELSA^k, k = smooth((ln(GPCP/CHELSA) - ln 2) /
  ln 2) smooth((300 - CHELSA) / 150) (fully from four times CHELSA and under 150 mm), its months scaled alike
  (`desert_coast_factor`, CHELSA bio12 as 0.25° means). UDel's share is untouched, so inland cells keep v3. 7 756 cells
  change (precipitation, rainfall and summer dryness planes): Lima 434 → 60 mm (UDel's 94 keeps a sixth of the cell),
  Walvis Bay 81 → 19, Moscow, Riga, Niamey and Agadez as v3; no temperature changes. `EarthParams.ClimateData` 4 for new
  worlds; older worlds keep theirs.
- **The weather reads it** (weather rules 7, [0017](0017-weather.md#weather-rules-7-the-ground-under-the-weather-14-november-2026)):
  weather.bin stays pinned; a rules-7 world scales its monthly precipitation by fix 4's over fix 3's at the place (1 where
  v4 changes nothing), so Lima's July is 0.0 mm a day and its weather no longer rains on the desert.
- **Not changed**: the Sahel and Arabia cells (UDel's own), for want of stations that settle which source is right.

## History

- 2026-10-21: found from Wmap's terrain map; cause traced to UDel's missing months; the user chose the per-world fix.
  Implemented 0.15.1; offline test and the server self-test pass (a new world gets fix v1).
- 2026-11-07: the missing months traced to a cut-short UDel download (Correction); weather.bin rebaked from the complete
  file on `claude/udel-december`. The same day the user asked to "rebake for full data": fix v2 (climatefix2.bin) for
  new worlds, older worlds unchanged; `tests/WeatherTests` checks v0/v1/v2 and switching between them.
- 2026-11-13: EA-0016 audited (142 of v2's 227 rain spans from the coast switch); fix v3 (climatefix3.bin, the coast
  band, in a layered file against v2) for new worlds on `claude/climate-fix-v3`; `tests/WeatherTests` checks v3 too.
- 2026-11-14: EA-0060 (the author's request to work through the backlog, by way of the coordinating session): fix v4
  (climatefix4.bin, GPCP's share on desert coasts at CHELSA's level) for new worlds on `claude/weather-items`, read by
  the weather from rules 7; `tests/WeatherTests` checks v4.
