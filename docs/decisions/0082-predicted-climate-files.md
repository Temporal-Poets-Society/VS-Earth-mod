# 0082: The client climate files as predicted planes

Status: **built** (14 November 2026, EA-0077; code review 14 November ER-02). Date: 2026-11-14.

## Why

The rc.13 zip was 39,989,244 bytes; packing the config JSON ([0081](0081-packed-config-assets.md)) left 313 kB under the
Mod DB's 40,000,000, not enough for the habitat steps' ground blocks and data ([0080](0080-habitats-by-site.md)). Seven
client climate files took 35.3 MB of it as brotli of their raw planes. Brotli has no model for a smooth 2-D field; a
spatial predictor in front of it (what PNG's filters do, and what GLO-90's tiles already do) codes the same values in
about a third less room. The files travel only in the zip with the DLL that reads them, on both sides, so a new coding
that decodes to the same values needs no rules version ([0002](0002-save-compatibility.md)).

## What

- **Predicted planes** (`src/Core/PlaneCoding.cs`, `tools/planecoding.py`): a header's version carries the coding in its
  high 16 bits (0 plain brotli, 1 predicted planes) and the content version in its low 16, so every reader takes both.
  Per plane three bytes name a predictor (none, left, up, MED, the mean of west and north, planar), a step (the values'
  common factor: 0.5 °C in temperature.bin's tenths) and a reference plane subtracted first (climate.bin's coldest and
  warmest month against the mean, weather.bin's temperatures against the month before, where that is smaller). The
  residuals, modulo 2^bits, follow plane by plane; 16-bit ones zigzag-coded, low bytes then high bytes. The bakes pick
  per tile and plane what compresses smallest. Exact integer steps throughout.
- **rain.bin, aridity.bin, temperature.bin** (TiledGrid): each tile a block of predicted planes. temperature.bin holds
  the values themselves, no longer their planar residuals.
- **climate.bin, weather.bin**: one block for the whole grid. weather.bin keeps its signature: the predicted file stores
  the plain file's FNV-1a (`bc9ea5a04cb1bc1d`), which worlds froze when they were made (`WeatherDataMade`), right after
  the header, and beside it the first 8 bytes of the SHA-256 of the planes it was made of; the reader refuses a file
  whose decoded planes are other ones, so the signature stays tied to what is read (Fable re-review L-3).
- **climatefix.bin, climatefix2.bin**: a bitmap of the cells and each plane's new values as differences from
  climate.bin's. Their header names that climate.bin by the SHA-256 prefix of its planes; the reader refuses a fix made
  against another, so a re-bake of climate.bin cannot quietly change frozen worlds' climate (L-2). climatefix3.bin and climatefix4.bin (layered, 117 kB together) stay as they are: the same coding would
  save 22 kB.
- `tools/repack-client-data.py` made the files from the plain ones and checked every value back; the bakes
  (`bake_rain`, `bake_aridity`, `bake_chelsa`, `bake_earth` for climate.bin, the fixes and weather.bin,
  `reduce-earth-data`) write the predicted form, and `package-earth.py` refuses a plain client climate file or one above
  its ceiling. `tests/ClimateDataTests` reads the plain files from Git (tag `v2.0.0-rc.12`; the seven were the same bytes
  from a37fc76 through rc.13) and the predicted ones through the game's readers and compares every value: 169 million
  rain values, 27 million aridity values, 58 million cells of temperatures, the climate with each of its four fixes and
  every weather plane.

| File | Plain | Predicted | Saved |
| --- | ---: | ---: | ---: |
| rain.bin | 11,862,158 | 8,847,166 | 3,014,992 |
| temperature.bin | 8,012,000 | 5,421,847 | 2,590,153 |
| aridity.bin | 6,475,409 | 5,408,416 | 1,066,993 |
| climate.bin | 3,373,093 | 1,752,597 | 1,620,496 |
| climatefix2.bin | 1,860,725 | 575,659 | 1,285,066 |
| weather.bin | 3,634,099 | 2,661,987 | 972,112 |
| climatefix.bin | 361,524 | 216,114 | 145,410 |
| **Sum** | 35,579,008 | 24,883,786 | **10,695,222** |

The zip went from 39,692,877 bytes (`main` 27c460f) to 29,169,646 (on 1ca3b3e): 10.83 MB under the limit.

## Watch

weather.bin's signature `bc9ea5a04cb1bc1d` stays the frozen name of today's normals. A later content version of
weather.bin takes the planes' SHA-256 prefix (the hash the predicted file already stores) as its signature, so nothing
has to rebuild a plain file to name it (Fable's re-review, 14 November 2026).

The data store's grids (summits.bin, glaciers.bin) stay plain and read as before. A rebuilt zip changes the files' CRCs
in the model cache's key, so every world builds its model cache once after this update (as after any build). A bake
that wrote plain brotli again would bring back about 10 MB: `package-earth.py` stops on it.
