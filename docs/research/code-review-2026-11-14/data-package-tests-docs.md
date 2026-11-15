# Earth review, 14 November 2026: data loading, package, tests and docs

Read-only review of `C:\VS mods\Earth`. Nothing in the repo was changed, built or committed.

**Which tree.** The release zip measured is `release\earth-2.0.0-rc.13.zip` (commit `6371e32`). While this review ran,
`main` moved to `b70ebbf` (11:08 -> 12:07): site drivers (0080) and brotli-packed config JSON (0081, `src\PackedAssets.cs`,
a `PackJsonAssets` build step). File and line numbers below are from `main` at `b70ebbf`; the files of area A did not
change between the two commits. Size numbers are for the rc.13 zip unless said otherwise.

**How sizes were measured.** The zip was unpacked to a scratch copy and each file decoded and re-encoded in Python
(`scripts/grids.py`, `grids2.py`, `small.py`, `cover.py` in the `scripts` folder beside this file, with their outputs `grids.out`, `grids2.out`). "Lossless" means the
script decoded the new form back to the same values (asserted for the left predictor and for the temperature steps) or
the transform is an exact integer predictor. No C# decoder was written or run.

Marks: VERIFIED = read in the code, or measured; PLAUSIBLE = follows from the code but the effect was not measured.

---

## Part B first: the package (the largest finding)

### B1. The five big climate files are brotli of unpredicted bytes: about 10 MB of the 40 MB can go, losslessly — high, VERIFIED (sizes measured; decoder not written)

- `src/Core/TiledGrid.cs:79-83` decodes a tile as plain `Unbrotli` of raw planes; `tools/bake_rain.py:213-214` and
  `tools/bake_aridity.py:102` compress the raw `u8` planes with no predictor. `temperature.bin` has a planar predictor
  but stores the residuals as interleaved little-endian int16 (`src/Core/FineTemperature.cs:118-131`,
  `tools/reduce-earth-data.py:47-51`). `climate.bin` and `weather.bin` are one brotli stream of raw planes
  (`src/Core/EarthDataset.cs:455-465`, `src/Core/WeatherData.cs:62`). `climatefix.bin` / `climatefix2.bin` store all six
  planes for every listed cell with a raw int32 index (`src/Core/EarthDataset.cs:468-511`), although most cells keep
  their temperatures.
- Brotli has no model for 2-D smooth fields; a one-line spatial predictor in front of it does what PNG filters do.
  GLO-90 tiles already do this (`src/Core/DemTiles.cs:262-275`: MED predictor, zigzag, byte planes).

| File | In the zip now | Re-encoded | Saved | How |
| --- | ---: | ---: | ---: | --- |
| `rain.bin` | 11,862,215 | 8,868,884 | **2,993,331** | per tile the best of three predictors (left, MED, mean of west and north), 1 byte says which; one predictor alone (left) gives 9,373,826 (-2.49 MB) |
| `temperature.bin` | 8,006,059 | 5,400,888 | **2,605,171** | values in their 0.5 °C steps (every value is a multiple of 5), left-neighbour residual, zigzag, low bytes then high bytes; MED instead gives 6,107,905; best per tile not measured |
| `aridity.bin` | 6,468,128 | 5,412,623 | **1,055,505** | as rain.bin |
| `climate.bin` | 3,374,076 | 1,833,308 | **1,540,768** | MED residual per plane, coldest and warmest month as differences from the mean, byte planes |
| `climatefix2.bin` | 1,627,576 | 575,631 | **1,051,945** | a bitmap of the cells, each plane as differences from climate.bin (only 97 k of its 335 k cells change a temperature) |
| `weather.bin` | 3,635,209 | about 2,881,000 | **about 754,000** | MED residual per plane (the land plane left raw) |
| `climatefix.bin` | 324,584 | 216,086 | **108,498** | as climatefix2.bin |
| **Sum** | 35.30 MB | about 25.2 MB | **about 10.1 MB** | |

- Why it breaks nothing: these files travel only inside the mod zip, with the DLL that reads them, on both sides
  (client: `src/EarthMapSystem.cs:1096-1097` -> `LoadData(false)` -> `EarthParams.ConfigureClimateData`,
  `src/EarthParams.cs:542-550`; none is in `EarthDataStore.Files` or in a 1.0 data pack). The decoded values are the
  same bit for bit, so no rules version is needed and a saved world generates as before.
- Three things to carry along:
  1. `weather.bin`'s signature is FNV-1a of the file's bytes (`src/Core/WeatherData.cs:63`); it is frozen into each world
     (`WeatherDataMade`, `src/EarthMapSystem.cs:684-687`, a warning when it differs) and pinned in
     `tests/WeatherTests/Program.cs:177-179`. A re-encoded file must keep the old signature: write it into the new header
     and read it from there (or leave weather.bin alone: it is the smallest gain).
  2. `EarthDataset.DataIdentity` (`src/Core/EarthDataset.cs:586-604`) keys the model cache by the zip entries' CRC: every
     world rebuilds its cached model once after the update (a slow first start, as after any data change).
  3. The repo's `earthdata\` copies, the bakes (`bake_rain.py`, `bake_aridity.py`, `bake_chelsa.py`,
     `bake_earth.py write_climate_fix`) and `reduce-earth-data.py` must write the new form, or a later bake silently
     brings the old sizes back. A check in `package-earth.py` (largest allowed size per file) would catch that.
- Fix, concretely: a new version number in each header (`TiledGrid` version 2: first byte of a tile = predictor;
  `EMCL` 4; `EMCF` as a new layered kind; `EMWX` 3), the reader accepting only the new one; a one-off
  `tools/repack-client-data.py` that decodes the current files, re-encodes and asserts the round trip value for value;
  a C# test that decodes every tile of old and new files and compares (old files kept in `data\archive`).
- Risk: low for the values (exact integer transforms, checkable over every cell offline); the cost is a decoder change
  in five readers and re-running the pinned-signature tests. Decode cost per tile is a few hundred microseconds more
  (a prefix sum over 32,400 cells), on a cache miss only.
- Lossy steps exist beyond this but need a rules version and are not recommended: aridity or rain at half precision
  (even codes only) would take another 1.3 MB and 2.0 MB.

### B2. What decision 0081 already did, and what else is small — VERIFIED

| Option | Bytes | State / risk |
| --- | ---: | --- |
| Config JSON brotli-packed (0081) | about -302,000 (my estimate of the same set: -330,000) | done on `main` after rc.13; zip 39,686,896 with the site-driver code |
| Minify JSON instead | -4,169 in all | no use: deflate already removes the white space |
| Leave `EarthMap.pdb` out of the zip | -244,464 | no line numbers in players' crash logs. `EarthMap.csproj` has no `DebugType`; keep the pdb beside the zip in `release\` for symbolising by hand |
| `ore-heatmaps-v1.json` into the data store | -371,523 | server-only (`src/EarthOres.cs:43`, `:103-105`: read for worlds below `OreBackground.CatalogueRules`, and as the fallback when v2 is missing). Frozen, so it fits the store's "a name never changes meaning". Needs an upload (the author's yes), a `StoreFilesRead` entry, and v2 must then never be missing. Low risk for clients (they never read it) |
| Ore heat maps' layers gzip -> brotli | about -110,000 (0081's number, not re-measured) | changes bytes the bake manifests pin |
| Store entries that don't deflate instead of deflating them | -4,434 | `tools/package-earth.py:28-29` deflates everything; rain.bin grows 57 bytes, weather.bin 1,110, each PNG 5. Trivial and safe: `ZIP_STORED` when deflate is not smaller |
| A stronger deflate for the DLL, pdb and JSON (zopfli / 7-Zip) | about -40,000 to -60,000 | PLAUSIBLE, not measured (no zopfli here) |
| PNG recompression (504 files, 1,238,665 bytes) | -572 | measured with Pillow `optimize`; none can be paletted losslessly (32x32 RGBA noise). Nothing to gain |
| Zip entry overhead of the 504 PNGs | 93,626 | only a packed texture file unpacked like 0081 would save it; not worth it |
| Crafted-stone textures drawn at run time only | up to about -0.9 MB (310 files under `assets/game/textures/block`, 866,710 bytes + entries) | `src/EarthStoneTextures.cs` already redraws them from vanilla's templates and keeps the shipped ones as fallback; but the last review found the redraw may never run from a zip install (CR-O10, `Ours()` at `:36-39`, left as the author's call). Only an option once that is settled in game |
| `climatefix.bin` out of the zip | not possible | the client reads it for worlds with `ClimateData` 1 and downloads nothing; B1 shrinks it by 108 kB instead |

Which files the **client** reads (so they must stay in the zip): `earth.json`, `climate.bin`, the climate fix chain of the
world (`climatefix2` + `3` + `4` for new worlds, since 3 and 4 are differences from the one before,
`src/Core/EarthDataset.cs:68-69`; `climatefix.bin` for 1.0.x worlds), `temperature.bin`, `rain.bin`, `aridity.bin`,
`weather.bin`, `weatherground.bin` — exactly `CLIENT_DATA` in `tools/package-earth.py:13`. Server-only in the zip:
`reservoirs-v1.json`, `outlets.bin`, `works-1318-2.bin`, `rivers-nonperennial-2.bin` (106 kB together) and all of
`assets/earth/config` (every reader is a server-side ModSystem).

### B3. The build picks the first .NET 10 runtime folder by name, and the tests run on .NET 8 — low, VERIFIED (code) / PLAUSIBLE (effect)

- `EarthMap.csproj:31`: `GetDirectories(..., '10.*')[0]` takes the first folder in name order (`10.0.10` sorts before
  `10.0.2`), and throws an index error with no message when no .NET 10 runtime is installed.
- The mod targets `net8.0` but compiles against and runs on the game's .NET 10; all test projects are `net8.0` and run
  on the .NET 8 runtime. The model cache's key already includes the runtime "for the searches' arithmetic"
  (`src/EarthMapSystem.cs:744-748`), i.e. results are known to differ between runtimes. Tests therefore check another
  runtime than players get.
- Fix: sort and take the highest 10.x with an `<Error>` when none; let tests roll forward
  (`<RollForward>LatestMajor</RollForward>` or `DOTNET_ROLL_FORWARD=LatestMajor` in the runner). Risk: low; golden
  values pinned on .NET 8 may move by a last digit.

### B4. `package-earth.py` writes a machine path and a time-stamped zip — low, VERIFIED

- `tools/package-earth.py:79`: `'file': str(target)` — `release\earth-2.0.0-rc.13-package.json` holds
  `C:\VS mods\Earth\.claude\worktrees\rc13\release\...`. `release\` is not tracked, so it is only untidy.
- `:28-30`: `writestr(name, data)` stamps the current time, so two builds of the same tree give different SHA-256
  (the report's `sha256` cannot be reproduced). Fix: `ZipInfo(name, date_time=(1980,1,1,0,0,0))`. Risk: none.
- The build needs `python` on PATH (`EarthMap.csproj:119` on `main`); the script uses only the standard library, so
  that holds, but the error when Python is missing is MSBuild's "exited with code 9009". An `<Error>` line would say it.

---

## Part A: data loading and downloads

### A1. CR-T8 is still open: terrain tiles keep elevation and ice as floats — medium, VERIFIED

- `src/Core/EarthDataset.cs:531-539`, `:619-627`; bound `src/Core/EarthData.cs:32` (320 tiles).
- 180 x 180 cells x (4 + 2 + 1 + 4) bytes = 356 kB a tile, 114 MB at the bound; elevation as its 16-bit code (looked up
  through `ElevationOf`, the table is already there, `:45`) and ice as a 16-bit count of 4 m steps would be 7 bytes a
  cell, 73 MB. A world's view also makes a new `TerrainSampler` with its own cache each time an overlay choice changes
  (`src/Core/EarthDataView.cs:87`), so several caches can exist while a model is set up.
- Fix: `short[] ElevCode`, `short[] IceCode` in `TerrainTile`, decode at the read sites. Risk: medium — overlays write
  elevations into tiles (`TerrainTileCache`), so a patched value that is not on the code table needs a float side
  table or its own code; measure the sampling speed before and after (the table lookup is one more memory read per
  cell in the hottest path).

### A2. A fix-4 world keeps about 75 MB of climate planes of which it reads 25 MB — medium, VERIFIED (sizes computed from the files)

- `src/Core/EarthDataset.cs:78-105` (`ClimatePlanes`), `:61-64`. For fix 4 the chain 0 -> 2 -> 3 -> 4 is built and every
  step is kept in `planesByFix`: the six base planes (1440 x 720 floats = 4.15 MB each, 24.9 MB), six clones for fix 2
  (it changes cells in all six planes), three for fix 3 and three for fix 4 (mask 0x38): 24.9 + 24.9 + 12.4 + 12.4 =
  74.6 MB, on the client too. `fixNew[2]` adds 8 MB and its index 1.3 MB.
- Fix: keep only the version a view asks for plus `climate.bin`'s own (drop the intermediate arrays after building the
  last; rebuild on a later ask, which only tools and tests make), or hold the planes as the file's int16/byte codes
  (10 bytes a cell, 10.4 MB a version) and convert in `ClimateSampler`. Risk: low for the first; the second touches the
  climate sampling path. `WeatherGround` (`:124-153`) needs fix 3's precipitation once and must be built before the
  intermediates go.

### A3. The first-start downloads cannot be cancelled and block the starting server — medium, VERIFIED

- `src/EarthMapSystem.cs:313` passes `default` as the token to `EarthDataStore.Ensure` (289 MB, 38 files);
  `src/EarthGeology.cs:252` calls `GroundDownloader.Get` without a token (172 MB); `DemDownloader.FetchFile`
  (`src/Core/DemDownloader.cs:134-158`) has no token at all and its `TakeTurnAsync` can wait 20 minutes for another
  server's lock (`src/Core/DataTransport.cs:95`). All three block the thread that starts the world
  (`Task.Run(...).GetAwaiter().GetResult()`: `EarthDataStore.cs:129`, `GroundDownloader.cs:68`, `DemDownloader.cs:142-149`).
- Effect: in single player the first start shows the loading screen for the whole 460 MB with progress only in the
  log; leaving is not possible until it ends (each read gives up after 1 minute, each file after 4 tries, so it is
  bounded, but by many minutes on a bad link).
- No deadlock risk: the game has no synchronisation context and every wrapper goes through `Task.Run`.
- Fix: one `CancellationTokenSource` in `EarthMapSystem`, cancelled in `Dispose` and on the server's shutdown phase,
  handed to all three; a line every 15 s is already logged (`DataTransport.cs:328-333`) and could also go to
  `sapi.Logger.StoryEvent` so the loading screen shows it. Risk: low; a cancelled download leaves a `.part` that resumes.

### A4. CR-A11 is half done: one transport, still three copies of "missing, turn, missing, fetch" with different policy — low, VERIFIED

- Transport, retries, `.part`, hash and turns are now all `DataTransport` (good). The orchestration is still written
  three times: `src/Core/EarthDataStore.cs:137-171`, `src/Core/GroundDownloader.cs:70-96`, `src/Core/DemDownloader.cs:142-149`.
- Drift between them: the store stops at the first file that fails ("the remaining n are not tried", `:162-168`), the
  ground sets try every file (`:83-93`); the ground sets answer `Lacking` for an empty source before taking the turn
  (`:75`), the store only after trying local packs (`:149-153`); the store returns a reason string, the ground sets an
  enum, `FetchFile` two out-parameters; only the ground sets log "checked" per file; three keys for a square
  (`FineElevation.Key`, `DemTiles.SquareIndex`, and `latS * 360 + lonW` in `src/EarthElevationSystem.cs:475`).
- Fix: `FileSet.Ensure(folder, files, source, local, policy)` returning Here / Lacking / Failed plus the reason, used
  by all three; `FineElevation.Key` in the prefetch. Risk: low, covered by `tests/GroundDownloadTests`.

### A5. The GLO-90 release's manifest is trusted as fetched — low, VERIFIED

- `src/Core/DemDownloader.cs:348-387`: every tile is checked against the manifest's SHA-256 (`Keep`, `:281-289`), but the
  manifest itself is whatever `manifest.json` the source returns, and a cached copy is used without a check. The data
  store and the ground sets have their hashes compiled in (`EarthDataStore.cs:30-74`); here the root of trust is HTTPS
  and GitHub alone.
- Also `:373-375`: the `.repo` marker is written after the manifest is moved into place; a crash between the two
  leaves a mirror's manifest read as the default release's (`:357`). Write the marker first, or put the repo inside one file.
- Fix: a constant with the size and SHA-256 of `egl-v1`'s manifest, checked when `Repo == Glo90Repo` (a mirror set by
  hand stays unchecked). Risk: none while release assets are never replaced, which is the repo's rule.

### A6. Every start reads and hashes the stores twice or more — low, VERIFIED (code) / PLAUSIBLE (seconds)

- `EarthDataStore.Missing` hashes all 289 MB at each start (`src/Core/EarthDataStore.cs:106-112`, called from
  `EnsureCoreAsync:140`), then the loaders read the same files again. `rivers-glo90.bin` (24 MB) is then read and
  SHA-256'd twice more (`src/Core/RiverSourceRepository.cs:84-91` and `:122-125`), `terrain.bin` (30 MB) once more for
  the referee (`src/Core/EarthDataset.cs:192`), the three ground sets (172 MB) in `src/EarthGeology.cs:252`.
- About 1 s warm, several seconds from a cold disk. Fix: hash while loading (`EarthDataStore.Bytes` can check the
  array it returns and remember the result per file), keep `Sha8` of `rivers-glo90.bin` from the first read. Risk: low.

### A7. Unsized `MemoryStream` + `ToArray` left after CR-D4 — low, VERIFIED

- CR-D4's fix holds: `src/Core/EarthBinary.cs:18-33`, `src/Core/EarthDataStore.cs:96-100`, `src/Core/EarthDataset.cs:564-575`.
- Still there: `src/Core/StreamHeads.cs:41-43` (stream-heads-1.bin, 4.7 MB) and `:53-57` (its decoded body);
  `src/Core/CoarseElevation.cs:431` (index); `src/Core/DataTransport.cs:290-294` (a Copernicus GeoTIFF, tens of MB: the
  stream is sized, but `ToArray` copies it once more, with up to 8 downloads at once); `src/PackedAssets.cs:47-53`
  (new on `main`: `MemoryStream(packed.Length * 4)` then `ToArray`; fauna-species unpacks 45 kB to 2.8 MB, so the
  buffer doubles six times). Fix: `EarthBinary.ReadAll` / `UnbrotliAll` in the first three; in `GetAsync` read into
  `new byte[n]` when the length is known. Risk: none.

### A8. The included GLO-90 heights are 129 MB of managed arrays for the server's life — low, VERIFIED (code) / PLAUSIBLE (impact)

- `src/Core/CoarseElevation.cs:449-458`, `:494`: the four `glo90c-*.bin` parts (39.6 + 39.8 + 39.8 + 10.0 MB) are read
  whole and every `TileRef` points into them. Only worlds with `CoarseElevation > 0` pay it.
- Fix: memory-map the parts (`MemoryMappedFile`, read a tile's bytes on a cache miss). Risk: low; file handles stay
  open, which matters for the "replace by hand" instructions on Windows.

### A9. The elevation dialog polls the disk on the main thread — low, VERIFIED

- `src/EarthElevationSystem.cs:527`: every status request (each open dialog asks every 2 s, `:687`) from a player with
  the control privilege runs `Directory.CreateDirectory` and `DataTransport.FreeBytes`, which enumerates all drives
  (`src/Core/DataTransport.cs:47-61`; slow with a sleeping or network drive). Every 30 s `Census` lists up to 26,475
  files (`src/Core/DemDownloader.cs:642-659`).
- Fix: cache the free space with the census (30 s) and take both on a pool thread. Risk: none.

### A10. Small things in the downloader — low, VERIFIED

- `src/Core/DemDownloader.cs:236`, `:266`: `slots.WaitAsync()` and the retries take no token; `Dispose` (`:661-665`)
  cancels only "download all". On-demand fetches end by themselves within a retry or two after the transport is
  disposed (an `ObjectDisposedException` is not transient), logging through the mod's logger after it is disposed.
  `slots` and `Batch.Cancel` are never disposed.
- `src/Core/DemTiles.cs:77-119` (`ReadGeoTiff`): header sizes are not bounded (`new float[width * height]`,
  `new byte[tw * th * 4]`) and bad offsets throw `ArgumentOutOfRangeException`, not `InvalidDataException` as
  `Decode` does since CR-D9; such a file fails once without the retries (`DataTransport.IsTransient`). The source is
  Copernicus over HTTPS, so this is tidiness.
- `src/Core/DataTransport.cs:336-343`: a finished `.part` is read a second time for its hash (135 MB for the largest
  ground set); hashing while writing a fresh download would save the pass. No `Flush(true)` before the rename: after
  a power cut a renamed file can hold zeros; every reader does catch that (store hashed each start; tiles' checksum,
  `src/Core/FineElevation.cs:434-438`, sets the file aside as `.bad`; release files `IsExpected`).
- `src/Core/TileCache.cs:51`, `:164`: each thread keeps up to 8 + 16 tiles of an unloaded world alive (documented in the
  comment; a client that leaves an Earth server keeps a few MB per pool thread).
- `src/Core/EarthDataset.cs:343`: `coarseTried` is read outside its lock without `Volatile`; harmless on the runtimes in use.

### What was checked and holds (area A)

- Temp file and rename: whole files go to `name.part`, are size- and hash-checked and only then moved
  (`DataTransport.cs:306-344`); tiles go to a unique `.part` per writer (`DemDownloader.cs:291-308`); a local copy is
  staged as `.copy` so it can't collide with a resumable `.part` (`EarthDataStore.cs:210-231`).
- Hashes: store files and ground sets against compiled-in SHA-256, at each start; tiles against the manifest and an
  FNV checksum inside the file; release files against `IsExpected`.
- Range resume handles a server that ignores the range (`DataTransport.cs:238-239`, `:316`; `DemDownloader.cs:584`).
- "The source doesn't have it" (404, 410) is kept apart from failures that may pass (`NotThereException`), and only
  the first may become a world's choice for good.
- Chunk generation's wait for a square is bounded (2 minutes of its own download, 10 in all,
  `DemDownloader.cs:87-121`); before the game runs a failure stops the server with nothing saved.

---

## Part C: tests

### C0. The projects (36 folders: 33 test programs, 3 helpers)

All are console programs with a hand-written `Check`; each returns a non-zero exit code on failure (checked by grep
for `Environment.Exit` / `return 1` / a throwing `Check` in every project). Lines are the test's own code.

| Project | Lines | Covers | Needs | Notes |
| --- | ---: | --- | --- | --- |
| EarthMapTests | 3,641 | the terrain model end to end (size 384 and modes `audit`, `mouths`, `coasts`), rivers, lakes, climate, golden worlds | earthdata + data store | the widest; one known failure noted in the last review (Lake Geneva steps) |
| RiverIndexTests | 1,640 | river spatial index, tile cache, binary readers | a report path only | |
| WeatherTests | 1,556 | weather model against pinned normals and station data | earthdata | about 8 minutes |
| RegionalTreesTests | 1,318 | tree catalogue and regions (shared `Harness.cs`, 170 lines, linked by 2 other projects) | repo | |
| RegionalGeologyTests | 836 | rock regions | repo | compiles 3 Core files by name |
| PatchVanillaTests | 810 | every Harmony target and private member exists in the installed game | game DLLs + built `EarthMap.dll` | 385 checks |
| TerrainExpansionTests | 790 | vertical expansion, summits | earthdata or a zip | |
| WeatherContractTests | 575 | the weather contract other mods read | earthdata | |
| GroundColumnsTests | 560 | ground columns | 8 Core files by name | |
| EarthWorldOptionsTests | 540 | world options, params, config, the release zip's contents (`PackageChecks.cs`) | game API | 758 checks |
| OreDepositsTests | 520 | ore deposits at real size | repo | |
| RegionalFaunaTests | 499 | fauna catalogue | repo | |
| RegionalOresTests | 454 (+ Python) | ore heat maps, `check_artifacts.py` | `data\earth-research` | |
| SkyLongitudeTests | 451 | sky longitude, day and night | | |
| ElevationTests | 424 | `DemTiles`, `DemDownloader` (local source), `FineElevation` | `rawdata/copernicus-glo90-sample` | 16 checks |
| OreLayersTests | 403 | ores in their layers | 9 Core files by name | did not compile on 12 November (hand-listed files) |
| PlaytestReviewTests / PlaytestIssuesTests | 395 / 321 | reported places re-checked; write PNGs to `tests\out` | earthdata | |
| GroundDownloadTests | 377 | `DataTransport`, `GroundDownloader`, `EarthDataStore` (turns, resume, hashes) | 6 Core files by name | did not compile on 12 November (same cause) |
| ZoneTests | 357 | climate zones | earthdata | |
| ColumnStoreTests | 355 | column store | earthdata | |
| WaterTableTests | 339 | water table, HoD aquifer contract | 11 files by name + game API | |
| VolcanoTests | 299 | volcanoes and interiors | | |
| SpawnDistributionTests | 278 | random starts over the world | earthdata + store | about 14 minutes (the slowest) |
| RiverWaterTests | 274 | river water rules | | |
| RegionalPlantsTests | 246 | plant ranges | | |
| ModelCacheTests | 195 | model cache | | |
| WetlandTests / FloodplainTests / SoilTests / PlacementTests / VerticalProfileTests | 193 / 142 / 144 / 131 / 150 | each its grid or rule | | |
| SiteDriversTests | 187 | site drivers (new on `main`, 14 November) | 9 files by name | not in AGENTS.md's count |
| RequiredModsTests | 100 | required providers | built `EarthMap.dll` | stopped on 12 November at "9 files of 37" (the store has 38) |
| RequiredModsServerProbe, RawElevationSurvey, `PackInstallerTests.ps1` | 38 / 98 | helpers | game / rawdata | not `*Tests` programs |

Timing: AGENTS.md says 35 minutes for all; the mod card says SpawnDistributionTests about 14 and WeatherTests 8 of them.
I did not run them (running builds into the repo's `tests\*\bin` and `obj`).

### C1. No runner, no shared harness, and every project compiles the whole Core — medium, VERIFIED

- 26 of the 36 projects include `../../src/Core/*.cs` (137 files, 39,286 lines), so a full test run compiles the Core
  26 times. Ten list Core files by hand, and two of those were found not compiling on 12 November because a Core file
  gained a dependency (`docs/research/code-review-2026-11-11.md` sections 11-12); `SiteDriversTests` (new) is another
  hand list.
- `Check(bool, string)` is written out in at least 12 `Program.cs` files, in three behaviours (count and go on; throw
  at the first failure, e.g. `tests/PatchVanillaTests/Program.cs:35`, `tests/TerrainExpansionTests/Program.cs:9`;
  collect and print). There is no solution file, no script that runs all of them, and no summary.
- Arguments differ per project (repo root; root and size; a report path; a data folder; nothing), and five need paths
  outside the repo (`rawdata`, `data\earth-research`, the game folder, `ModData`).
- Fix, in this order: (1) `tests/Run-All.ps1` with one table (project, arguments, needs, minutes) that builds once,
  runs each, tees a log to `work\` and prints pass/fail and time; it can skip the two slow ones with `-Quick`.
  (2) `tests/Earth.Core.Tests.Common` — a class library compiling `src/Core/*.cs` once plus one `Check`/summary helper,
  referenced by the 26 projects (the mod itself keeps compiling the sources into `EarthMap.dll`). (3) Move the hand
  lists onto it too, which ends the "forgot to add a file" breakage. Risk: low; (2) changes `internal` visibility
  (tests now see Core internals because they compile them; a library needs `InternalsVisibleTo`).
- CI-like running is possible on this PC only: the tests need the 289 MB store or `earthdata\`, some the 87 GB
  `rawdata`, some the installed game. A `-Quick` set that needs only the repo (the catalogue, rule and download tests)
  would run anywhere in a few minutes.

### C2. Code with no test of its own — medium, VERIFIED by search (type names of each `src` file looked for in `tests\`)

49 of 209 source files (10,393 of 62,102 lines) have no type named in any test. Core files among them are mostly
exercised through `EarthData` and `TerrainModel`; the game-side ones are covered only by the in-game self test or by
nothing:

- `src/EarthElevationSystem.cs` (907): the release-file policy (`ReleaseFile`, `Unavailable`, `KeptWithout`, `:181-244`)
  decides whether a world stops or goes without a file for good; it is plain logic over a save's keys and could be
  tested with a fake save. `DemDownloader.FetchFile` and `StreamPart` (the "download all" stream with resume,
  `src/Core/DemDownloader.cs:571-639`) have no test either; `ElevationTests` covers `StartAll` from a local folder only.
- `src/WorldUpgrades.cs` (410), `src/EarthCommands.cs` (694), `src/WhereReport.cs` (267), `src/WorldGenRules.cs` (176).
- World generation passes: `AlpineSurfacePass` (346), `RockFloorPass` (146), `RiverBlockPass` (130),
  `TerrainChunkWriter` (149), `EarthSprings` (295), `SnowMeltLight` (127).
- Ores in game: `OreSession` (260), `OreBinder` (241), `OreV3Bodies` (204), `OreProspecting` (108).
- Core without a direct test: `WaterOverlays` (513), `DamRemoval` (229), `TiledGrid` (132), `WeatherNoise` (175),
  `TerrainOverlays` (246), `TerrainTileCache` (131).
- `src/PackedAssets.cs` is covered for its bytes (`tests/EarthWorldOptionsTests/PackageChecks.cs:76-85`) but not for the
  asset-manager step, which only the game can show.

### C3. Tests run on .NET 8, the game on .NET 10 — see B3.

---

## Docs against code

### D1. Decision index and status lines — low, VERIFIED

- `docs/decisions/README.md` does not list `0056-rocks-and-ground-apart-from-ores.md` and
  `0058-crafted-stone-from-vanilla-templates.md` (80 decision files, 78 indexed; no index entry points to a missing file).
- Status lines that no longer match: `0069` "proposed" (the flow file is read by every new world,
  `src/EarthElevationSystem.cs:121-130`); `0053` is "implemented, on main" while 28 code comments still say
  "0053 (proposed)" (e.g. `src/Core/EarthData.cs:181`, `:201`, `src/EarthElevationSystem.cs:299`, and "decision 0051
  (proposed)" at `src/EarthMapSystem.cs:676`); `0057` names test build 1.0.3-tall.19 on `claude/tall-glo90`; `0059`
  "Merged into release/1.0" and `0054` "on claude/fauna-1318 (from release/1.0)" name branches that no longer exist;
  `0039`/`0040` speak of 0.17.0-pre builds. AGENTS.md's release step says to bump "the decisions' status lines".

### D2. AGENTS.md, README, changelog, modinfo — low, VERIFIED

- AGENTS.md: "Published versions are tags (`v1.0.0`-`v1.0.2`)" — there is also `v2.0.0-rc.12`. The stated limit and the
  test instructions hold; `SiteDriversTests` takes no argument.
- `modinfo.json`: "about 290 MB" = 289,004,358 bytes in 38 files (`EarthDataStore.Files`), "170 MB" = 172,251,299 in 3
  (`GroundDownloader.Sets`): both right. Dependencies (game 1.22.0, cylinderworld 1.0.4, hydrology 0.6.2-rc.4) match
  README and the changelog.
- README (766 lines): 70 places in `src` and several in README (`:300`, `:323`, `:351-376`) still say "Earth Map", and
  log lines tell players to "Reinstall the mod" for files of "this Earth Map" (`src/EarthMapSystem.cs:851-854`);
  `README.md:548-583` describe settings as "frozen for pre.12+ worlds" (0.17.0-pre.12, before 1.0);
  `README.md:593` "tools/download_data.sh  # ~700 MB into rawdata/" where rawdata is 87 GB today;
  `README.md:726` points to `docs/open-issues-weather.md`, which is now only a pointer to the backlog.
- CHANGELOG's 2.0.0 section already has the packed JSON (0081) although the version in `modinfo.json` is still rc.13,
  whose published zip does not have it: the next build needs rc.14 (the build refuses to overwrite, so this cannot go
  wrong silently).

### D3. Old plans and reports in `docs\` — low, VERIFIED (dates from Git, references searched in README, AGENTS, CHANGELOG, decisions and the mod card)

`docs\` has 958 tracked files (22 MB), 802 of them under `docs\research` (7.1 MB). Top-level files last changed on
2 October and referenced nowhere, candidates for `docs\archive\`:
`resource-coverage-correction.md`, `geology-resource-access-plan.md`, `geology-latvia-pilot.md`,
`copper-tin-heatmap-design.md`, `copper-tin-global-data.md`, `ecology-integration-plan.md`,
`ecology-mod-inventory-2026-09-28.csv`, `river-index-scaling-2026-09-30.md` and `.patch`,
`local-terrain-expansion-0.12.2.md`, `regional-world-0.10.0.md`, `selftest-report-cyl-256k-h256.txt`,
`selftest-report-cyl-512k-h384.txt`, `regional-ores-config.json`.
Later and also unreferenced: `mountain-height-claude-review.md`, `verification-publication-data-pre15.json`,
`verification-publication-1.0.0.json`, `publication-packages-pre15.json`, `publication-packages-pre16.json`,
`verification-river-index-speed.md`, `remaining-ore-evidence.md` (34 kB), `installed-ecology-audit-2026-10-31.md`,
`publication-dependency-audit.md`, `moddb-logo-source.md`.
Superseded but referenced once: `moddb-page.md` (by `moddb-page-2.0.md`), the three `mountain-height-*` hand-off
files (decision 0031 is built), `open-issues-mountain-height-and-vegetation.md` and `open-issues-weather.md` (the
suite's rule: no issue lists in mod docs). `tests\out` holds 140 PNGs, 56 MB, ignored by Git: disposable output that
the suite's rules want deleted or in `work\`.

---

## What is good

1. One transport with honest failure classes: `.part` + resume + size + SHA-256 + atomic rename, per-read timeouts,
   turns between servers through a lock file with the reasons written down, and "the source lacks it" kept apart from
   "it failed for now" all the way up to what a world may freeze (`DataTransport.cs`, `EarthElevationSystem.cs:212-237`).
2. Damage fails at load with the file's name, not on a generation thread: `EarthBinary.Expect` / `TileIndex` / `InFile`,
   bounded decoded sizes, CR-D9's cell limit in `DemTiles.Decode`.
3. `TileCache`: lock-free hits, one decode per tile under contention, a failed decode not cached, and the comment says
   why each layer exists.
4. The package script refuses the mistakes that matter: over 40,000,000 bytes, a store file leaked into the zip, client
   data missing, a version the Mod DB rejects, an existing release overwritten; and `PackageChecks.cs` re-checks the
   built zip, including that each packed JSON unpacks to the repository's bytes.
