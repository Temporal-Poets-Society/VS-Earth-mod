# 0087: The climate as around 1318

Status: **implemented** in Earth 2.1.0, with decision 0086 (EA-0144). New
worlds only: worlds made before keep the modern normals. Date: 2026-11-16.
Decided by: the author ("keep in mind global warming + little ice age"; "Yes, in this release", 16 November 2026, relayed
by the coordinator); the sources and the design are the maintainer's (D72).

## Context

Earth's temperatures are modern normals: CHELSA V2.1's 1981–2010 (temperature.bin, and the monthly layer of decision
0086), and the weather's 1991–2020. Earth's world is as of about 1318 (its plants, animals, rivers and works), between the
end of the Medieval warm period and the early Little Ice Age (the Great Famine years 1315–17), and before the modern
warming. So the climate should be too.

## Decision

**The correction** (`tools/bake_1318.py`, an intermediate `paleo1318.bin` in `C:\VS mods\work\seasons`, not shipped):
for each month and each 2-degree cell, 1300–1330 against 1981–2010, the normals' period:

- **The level** is the Last Millennium Reanalysis v2.1 (Tardif et al. 2019): its annual anomaly for 1300–1330 (against
  1951–1980; the mean of its 20 Monte Carlo runs' ensemble means; its own 1951–1980 comes out at +0.03, so the reference
  period is as stated), less GISTEMP v4's 1981–2010 annual anomaly (also against 1951–1980). LMR assimilates real proxies
  (tree rings, ice cores, corals) into a model's climate, so it carries the early 1300s' actual regional pattern, not only a
  model's response to the sun and volcanoes.
- **The split over the months** is the CMIP6/PMIP4 past1000 models': each model's 1300–1330 (past1000) against its
  1981–2010 (historical), less that difference's yearly mean, averaged over ACCESS-ESM1-5, MIROC-ES2L, MRI-ESM2-0,
  INM-CM4-8 and EC-Earth3-Veg-LR. Only the split, never the models' level: their internal variability isn't the real
  one's, and their 1981–2010 holds their own warming (their levels range from −0.10 to −1.20 °C).
- GISTEMP enters as its **annual** 1981–2010 anomaly only. A monthly bridge (that month's 1981–2010 − 1951–1980) as well
  as the models' monthly split would count the seasonal shape of the modern warming twice: the models' split is already
  1300–1330 against 1981–2010, so it holds the base-to-modern seasonal change. (The author asked for monthly data, 16
  November: the months come from the models' split.)
- PAGES2k (regional, annual) serves as a check, not a source. Berkeley Earth is CC BY-NC and not used.

**Where it applies: in the data, not in the code** (the author, 16 November: "Download data and bake it"), with a new
params field `Climate1318` (212; 1 in new worlds) that only picks the files:

- **`earthdata/temperature-1318.bin`** (`tools/bake_chelsa.py --paleo`): CHELSA's temperature planes rebaked with each
  month's correction added before the yearly mean, coldest and warmest month are taken (so a cell's coldest and warmest
  month are its own, not January and July by rule). A world with `Climate1318` reads it in place of `temperature.bin`
  (`EarthDataView`, `EarthDataset.FineTemperatures1318`); every reader goes through `SampleClimate` unchanged: world
  generation's climate map (vanilla's snow, crops, vegetation and fertility read it), Earth's climate zones and forest
  types, tree lines and alpine surfaces, BareRock, soil, the range extension's correction, and the live temperature's
  rules 0 shape. 5.49 MB (the modern file 5.42 MB), shipped in the mod for both sides; the zip is 34.9 MB.
- **A missing or damaged file** (Fable's review F4): a new world is given `Climate1318` 1 only where this Earth reads
  `temperature-1318.bin` (`WorldParamsBuilder`, through `DataHas` as the other data files); a world with 1 on an install
  without it reads the modern `temperature.bin` instead (not the coarse climate grid), and the log names
  `temperature-1318.bin` and why.
- **Pinned** (`SeasonTests` with the repo root, in `tests/Run-All.ps1`): a 1318 world reads Paris at 10.5 / 3.0 / 18.5 °C
  (year, coldest and warmest month at sea level) and Moscow at 5.0 a year and −8.5 in its coldest month; turning it off
  gives the modern values back exactly. A rebake that moves them is a change to new worlds.
- **The monthly layer's means** (decision 0086) carry the correction month by month (`tools/bake_seasons.py final`), so
  seasons rules 1 is as around 1318 too.
- Per cell the planes keep their 0.5 °C step, so a single cell's shift reads in half degrees (`SeasonTests paleo`: Paris
  −1.0 a year and −1.5 in its coldest month, Moscow −1.4 and −2.0, Greenland's Eastern Settlement about 0); averaged over
  cells the shift is the baked one.
- Not corrected: the day-night range and the day-to-day spread (they describe variability, which the sources don't
  resolve for 1318); and the weather's own monthly normals (weather.bin), which set how often it rains and storms, not the
  temperature: whether it falls as rain or snow follows the live temperature, which is corrected. A known limit: the
  weather's hail and fog fits read the weather's own (modern) month temperature.

## The correction (as baked)

1300–1330 against 1981–2010, area-weighted:

| | Year | January | July |
| --- | --- | --- | --- |
| Globe | −0.54 °C | | |
| North of 60°N | −1.36 | −1.72 | −0.62 |
| 30–60°N | −0.68 | −0.77 | −0.84 |
| Tropics | −0.46 | −0.43 | −0.51 |
| South of 30°S | −0.36 | −0.34 | −0.38 |
| Western Europe (Paris) | −1.10 | −1.34 | −0.97 |
| The Alps | −1.12 | −1.25 | −1.19 |
| Scandinavia | −1.28 | −2.03 | −0.82 |
| Moscow | −1.37 | −2.34 | −0.97 |
| Iceland | −0.70 | −1.37 | −0.32 |
| Greenland, Eastern Settlement | −0.28 | −0.23 | −0.02 |
| Arctic Ocean | −1.83 | −1.70 | −0.28 |

## Checks against the record

- **Globally**, −0.54 °C against 1981–2010 is LMR's −0.12 °C against 1951–1980 plus the +0.42 °C the world warmed from
  1951–1980 to 1981–2010; reconstructions put the early 1300s a little below the 1951–1980 mean, as here.
- **Greenland's Norse settlements** were still farmed in the early 1300s (the Western Settlement was given up around
  1350–1400): the Eastern Settlement's −0.28 °C (−0.02 in July) leaves its summers as they are.
- **The Alps' glaciers** began to advance after about 1300: −1.1 °C a year and −1.2 °C in summer lowers the tree line and
  the snow line by about 170–185 m (6.5 °C/km).
- **Northern Europe and Russia's winters** (−2.0 to −2.3 °C in January) fit the early Little Ice Age's harsher winters;
  the Arctic's −1.7 °C in January is less than the guessed 2–3 °C, and the data are kept.
- **The station check** (decision 0086) runs on the modern means, before the correction is added, against the 1981–2010
  stations; it is unchanged.
