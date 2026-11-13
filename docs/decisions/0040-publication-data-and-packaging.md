# 0040: Publication data precision and three-file distribution

Date: 31 October 2026. Status: agreed and implemented in 0.17.0-pre.15; build/offline checks passed; final native smoke verification recorded below. Not installed or published.

## Decisions

The author reports a 40 MB Mod DB upload limit and wants at most two or three Earth downloads. The author selects a **3,000 m minimum for added fine mountain relief** and **0.5°C temperature storage steps**, then confirms a logical three-file split. Existing pre-release worlds do not need compatibility with the changed dataset. Previous releases and player saves remain preserved; there is no old-world migration or dataset-compatibility gate.

The cutoff is elevation above sea level, not mountain prominence. The normal 2' terrain grid still represents lowlands, hills and lower mountains. The separate 30" residual grid now contributes zero below 3,000 m, fades in between 3,000 and 3,250 m and retains its previous bytes above 3,250 m. The summit grid's coordinate resolution and 8 m residual units remain unchanged. The full GeoNames named-peak catalogue and its fingerprint remain unchanged; the creation dropdown from decision 0039 independently controls surveyed summit correction, Off or 1–8 km, default 3 km.

The temperature grid keeps its original 2' geographic resolution, all three temperature planes, coverage mask, sea-level convention and runtime interpolation/lapse correction. Stored int16 values still use tenths of a degree, rounded to multiples of five: existing stored values differ by at most **0.2°C**. Live temperature remains continuous after interpolation and altitude adjustment. No rain, weather, base terrain, river, coastline, island, glacier or ecology data is reduced.

## Package ownership

| Archive | Contents | Bytes |
| --- | --- | ---: |
| `earthmap-0.17.0-pre.15.zip` | Code, world settings, flora/fauna/geology/ore rules, climate, temperature, rainfall, weather and reservoir exclusions | 28,490,266 |
| `earthterraindata-1.0.0.zip` | Base terrain, filtered summit residuals, named peak catalogue, glaciers | 36,424,452 |
| `earthwaterdata-1.0.0.zip` | Rivers, islands, coastlines, lake overlays and lake shores | 31,583,561 |

Total **96,498,279 bytes**, down from the installed pre.14 archive's 145,254,639 bytes. Every file is strictly below **40,000,000 bytes**. All three are required on client and server. These are Earth-owned data components, not new runtime service boundaries; Earth's terrain APIs and ownership stay the same. The author subsequently supplied the [Earth Terrain Data](https://mods.vintagestory.at/earthterraindata) and [Earth Water Data](https://mods.vintagestory.at/earthwaterdata) page URLs; they are now linked in both publication descriptions. Public upload status is not independently verified.

Earth's manifest requires both data mods at 1.0.0, as well as Cylinder World, Hydrology and Sea. The content-only data packs require the game, avoiding a dependency cycle. `earthdata/pack-index.json` in Earth maps each external filename to its provider and SHA-256. `EarthDataPacks` verifies every external file before loading, then routes both immediate and deferred reads to the correct archive. Missing or mixed data is refused, rather than substituted. The server requests shutdown if data initialization fails. Source-folder/monolithic development loading remains available when no pack index exists.

The release packager checks all three complete ZIP sizes before writing new files. Identical data versions are reused. Changed data requires a data-pack version bump. Explicit `OverwriteRelease=true` preserves a SHA-named backup before replacing an unpublished main archive; it cannot overwrite different data-pack contents under the same version.

Consumer audit: Wmap uses Earth's live model/data rather than opening the main archive independently. Earth Ruins fingerprints `earth.json`, terrain, climate and rivers directly in the main archive. The packager embeds the external-file index/hashes in `earth.json` as well, keeping that existing consumer's metadata fingerprint sensitive to all relocated data without changing Earth Ruins or its build references.

## Reproduction and measurements

`tools/bake_summits.py` defaults to the 3,000 m per-cell cutoff and 250 m transition. `tools/bake_chelsa.py` defaults to 0.5°C precision. Both retain controls to reproduce higher-precision experiments. The faster `tools/reduce-earth-data.py` converts full baked source grids using the original ETOPO 30" raster, copies all other files unchanged and refuses in-place or repeated conversion. The input must be the full pre.14 dataset, extracted from its retained archive if needed.

The successful conversion used `--source earthdata --output .local/publication-data/earthdata --rawdata "C:/VS mods/Earth/rawdata"` before replacing the three source files. Originals of `summits.bin`, `temperature.bin` and `earth.json` are preserved in `.local/data-backups/before-compact-pre15/`. `reduction-report.json` records input/output hashes and full-grid error bounds. All untouched geographic files compare byte-for-byte with the original dataset.

Compressed summit payload: **39,391,564 → 2,403,843 bytes** (475 retained tiles). Temperature payload: **19,752,664 → 8,006,055 bytes**. A 0.2°C temperature experiment was 14,216,218 bytes with at most 0.1°C change; the author chose 0.5°C.

Additional sample-only experiments suggest rainfall/dryness quantization could reduce its 11.9 MB payload to roughly 6–9 MB. These are estimates at Brotli quality 9, not accepted production data. Coarser ocean-floor precision showed no useful improvement over the existing quality-11 terrain file in the initial samples. Neither experiment is applied. Rivers and coast/island geometry are retained to preserve waterways and regional geography; three packages already meet the upload limit.

## Verification

- Release build: zero warnings/errors. All three ZIPs are below the strict decimal 40 MB limit.
- `EarthWorldOptionsTests`: **507 checks passed**, including named summit controls, Fuji at 3,776 m and Everest at 8,849 m, production data across seven regions, deferred grid/catalogue/rivers loading, client climate loading, Earth Ruins metadata fingerprints and rejection of absent/mismatched data packs.
- `RequiredModsTests`: **35 checks passed** against the built DLL.
- Full conversion asserts zero fine relief below the cutoff, unchanged residual bytes above 3,250 m, valid Brotli round trips, unchanged temperature mask and maximum 0.2°C stored-cell difference.
- Final native server fixture: `testruns/compact-data-pre15-final-20261031/valid`, disposable new Riga world, 256k by 102.4k, height 512, loopback port 42762. The outcome is recorded in the package verification JSON. Earlier packaging layouts were smoke-tested separately and preserved as unpublished experiments.
- No player save was opened, no user ModConfig was changed, and no package was installed or uploaded. Visual/client acceptance remains pending.

Sources: `tools/reduce-earth-data.py`, `tools/package-publication.py`, `src/Core/EarthDataPacks.cs`, `src/Core/EarthData.cs`, `src/EarthMapSystem.cs`, `modinfo.json`, `EarthMap.csproj`, `tests/EarthWorldOptionsTests/DataPackChecks.cs`; source measurements under `C:/VS mods/Earth/.local/release-size-20261031/`.
