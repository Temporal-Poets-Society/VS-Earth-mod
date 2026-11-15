# 0051: Copernicus GLO-90 land heights, downloaded per tile on demand

Date: 4 November 2026. Status: **in Earth 2.0.0** (and in every 2.0.0 release candidate since rc.1,
8 November 2026). The direction is the author's (below); the details were built and played in the 1.0.3
test builds and carried into 2.0 rather than agreed one by one. On `main`: new worlds take dry land's heights from
GLO-90 at every scale (`earthElevation` `auto`, as `glo90`); the squares come from Earth's own data release on GitHub
(`Temporal-Poets-Society/earth-glo90`, release `egl-v1`, 8.6 GB; [Data release](#data-release-tall10)), all of them
when a new world first opens unless the Earth tab says "as the world reaches them", with Copernicus's bucket as the
fallback and as a choice on that tab, and the included coarser GLO-90 grid for the session where a square can't be had
([A tile or file that can't be had](#a-tile-or-file-that-cant-be-had-rc3)). From Earth 2.0.1 the tab also offers to
download all before a new single-player world starts, on its loading screen (EA-0133, the author's choice of 15 November
2026). The prototype and test builds are in [History](#history).

## Author's direction

- 4 November 2026: "Could theoretically then build in data processing layer for players that would pull the data as
  needed and save compressed version. Our current data would stay as backup and for terrain. Players could click in
  config to download and process all of it. Not 71GB at time but by per tile and save only result. Later can add
  GLO-30."
- The same day: "write proposal and lets build prototype", and on the button: "yes UI button if want to download all
  and warning on data size and procesing".
- Then: "also GLO-90 usage would need to be toggle in Earth tab", and "Processed GLO-90 data probably would need to go
  to ModData or similar place so not every new world would need download it".
- 5 November 2026, on hosting the converted store (this replaces the per-tile download from the bucket and its 71 GB
  "Download all"; not built): "We need 9 GB and be 5 files and full download once in Earth UI tab". The files go on a
  GitHub release of the author's organisation `Temporal-Poets-Society` ("i made this org for ... Mod publishing"). The
  data is "version agnostic and would not change - this should be in mod Earth mod mechanics or change only when
  something important": every Earth rule (summit lift, coasts, rivers, scales) stays in the mod's code, and a new
  data release only for an important change. On keeping the bucket's per-tile download as a fallback: "Drop it".
- 5 November 2026, late evening (built in tall.10): "plan is to host all 9 gb public data repo under
  https://github.com/orgs/Temporal-Poets-Society/repositories that default download. Data should be version agnostic to
  be loaded once. Create repo, add correct license and split in 5 files under 2GB". This reverses "Drop it": "We can
  keep pull code from original 71GB as fallback option. So we have tab with dropdown for GLO 90 data where player can
  select and click download still in game UI." Then, on the dialog: "this i mean in world customization tab", "for
  earth before game is started", and "ok" to a dropdown on the Earth tab whose choice starts the download when the new
  world opens (no mod code runs before that).

## Context

Earth's land heights come from a 2′ grid (ETOPO 2022's 1′ cells averaged in pairs, about 3.7 km north-south). That is
fine for worlds up to about 1 million blocks wide. At 1:10 (decision [0050](0050-tall-worlds.md)) one cell is about 370
blocks wide, and the [elevation sources note](../research/coast-and-elevation-sources-2026-11-03.md) (in the main
checkout, not on this branch) measured what the grid loses:

- Summits are 390–1,390 m low: Everest 7,464 m on the 2′ grid (real 8,849 m), Fuji 2,666 m (3,776 m), the Matterhorn
  3,398 m (4,478 m).
- Narrow fjords and valleys are missing. The Nærøyfjord's walls rise 1,000–1,400 m within 1 km; on the 2′ grid the
  fjord is a gentle dip.
- Earth's detail noise and the 30″ summit residual (decision 0031) only imitate the missing relief.

Copernicus DEM GLO-90 has a 3″ grid (about 90 m north-south), 26,475 one-degree tiles, 71.1 GB as published. Stored in
1 m steps, predicted from the west, north and north-west neighbours, and compressed with Brotli, it takes about 9 GB (measured: 8.61 GB).
That is far beyond Mod DB's archive size (decision 0040: below 40 MB each), so it cannot ship with the mod.

How much a 90 m cell adds depends on the world:

| Earth size (width) | Blocks per 90 m cell | 2′ cell in blocks |
|---|---|---|
| 512 000 | about 1 | about 47 |
| 1 024 000 | about 2 | about 95 |
| 2 048 000 | about 4 | about 190 |
| 4 096 000 | about 7 | about 380 |

## Proposal

1. **A toggle on the Earth tab, fixed at creation.** `earthElevation` ("Land heights"): "Built-in" (default, today's
   terrain) or "Copernicus GLO-90 (downloads)". A two-value dropdown rather than a checkbox, so GLO-30 can be added as a
   third value. Saved in `EarthParams.FineElevation` (proto 95; 93 is decision 0050's `RockFloorDepth`, 94 the
   terrain-walls branch's `RiverValleyRules`). Saves without it keep built-in heights.
2. **Only dry land changes.** GLO-90 gives the height of land columns. Everything else stays on the current data:
   - sea floor and lake beds;
   - which columns are land, sea or lake (the grid, the coast outlines, the islands; river exit channels judge the low
     land they cut on the built-in heights from channel rules 3, [0053](0053-scales-included-glo90-and-glo90-rivers.md)
     item 13; from tall.14 the water mask's referee decides some of them, item 14);
   - river courses and their water levels;
   - ice thickness (GLO-90 has the ice surface, as the grid does).
   Where GLO-90 is used, the 30″ summit residual (0031) and the detail noise's relief-scaled ridges are dropped: the data
   has them. A gentle undulation of about a metre stays, so slopes aren't planar between 90 m points.

   **Summits on GLO-90 (rules 2).** GLO-90 comes within 13–46 m of most surveyed summits but is 123 m low at Everest
   and 161 m at the Matterhorn: a 90 m cell blends a sharp top with its flanks. The 2′ grid's summit correction (0039)
   doesn't fit finer data. It lifts by the gap between the survey and the ground at the catalogue's point, which lies
   17–199 m from GLO-90's actual top at the six summits measured, so on steep flanks it would raise a second point
   beside the real one. It also lowers ground and spreads over up to 3 km. The author chose a narrow lift instead
   (4 November: "Yes, add it"):
   - the Earth tab's summit cutoff applies as before ("off" lifts nothing);
   - for each listed peak at or above it, GLO-90's own highest point within 1 km of the catalogue's position;
   - not if that point lies within 185 m of the search's edge (a higher point may be outside it);
   - that point's column is raised to the surveyed height, the lift fading as (1 − r)² to nothing 300 m away
     (about three GLO-90 points);
   - never lowered; a gap over 300 m is taken as a catalogue error; where two peaks' lifts overlap, the larger holds.
   Worlds record it as `FineElevation` 2; prototype worlds with 1 keep heights only.
3. **Tiles on demand, on the server.** In singleplayer that is the player's machine; clients never need the data.
   - Source: the public AWS bucket `copernicus-dem-90m` (no account); since tall.10 Earth's data release first and the
     bucket as fallback ([Data release](#data-release-tall10)). The tile list (8 kB, which 1° squares exist) ships with
     the mod; squares without a tile are open sea.
   - Each tile is downloaded into memory (2–6 MB) and converted; the original is never written to disk. Only the result
     is kept, in `VintagestoryData/ModData/earth/glo90/`. That store belongs to the machine, not the world: every world
     there uses it, so a new world downloads only squares no world has needed before.
   - Before generating a chunk, the server makes sure the tiles under it (plus a margin) are there. It downloads them
     if not, and that chunk waits. Tiles around online players are fetched ahead.
4. **When a tile can't be had.** If a tile still fails after retries (offline, server error, damaged file), the world
   records that square as built-in for good, so later chunks match the ones already made. This is logged with the
   square's name. *(Narrowed in 2.0.0-rc.3, [below](#a-tile-or-file-that-cant-be-had-rc3): only the sources' answer
   that they lack a square is recorded for good.)*
5. **Download all, with a warning.** A dialog with a "Download all" button that warns about the cost before it starts:
   - about 71 GB downloaded, one tile at a time;
   - about 9 GB kept;
   - hours of processing.
   Only someone with the server's control privilege can start or cancel it (in singleplayer, the player). It shows
   progress and can be stopped and resumed (tiles already converted are skipped). The same is available as
   `/earth elevation download all|cancel|status`.
6. **File format.** Per tile: a header (magic, format and converter versions, the square, grid size, height range, the
   source file's size and ETag, a checksum of the heights), then the heights in 1 m steps. They are stored as
   median-edge-predictor residuals, zigzag coded, split into low and high byte planes and compressed with Brotli
   (quality 9). On eleven sample tiles that gave 2.22 bits per cell in 0.08 s. Quality 11 without the split gave
   2.24 bits in 2.8 s; with the split, 2.03 bits in 1.6 s, too slow for 26,475 tiles. Everything is in .NET; no
   native libraries.
7. **Provenance.** The dataset is as published in the bucket (files dated 9 May 2022). Each converted tile records its
   source's size and ETag and the converter version. A world records the converter version it was made with.
8. **Attribution and licence.** The dataset's licence (`eula_F.pdf` in each tile's `INFO` folder, read 4 November) is
   free and grants reproduction, distribution, communication to the public, and adaptation and combination with other
   data, worldwide and without time limit. In return, Article 6 asks for:
   - the notice for adapted data: "produced using Copernicus WorldDEM™-90 © DLR e.V. 2010-2014 and © Airbus Defence and
     Space GmbH 2014-2018 provided under COPERNICUS by the European Union and ESA; all rights reserved";
   - a sentence that the organisations in charge of the Copernicus programme incur no liability for any use of it;
   - no impression of endorsement;
   - anyone given the right to redistribute it bound by the same.
   Both notices are in the dialog, the command's output and `earthdata/ATTRIBUTION.txt`. Converted tiles may therefore
   be redistributed with the notices. Mod DB's rules on mods that download data are still to be read before any
   release.
9. **GLO-30 later.** The same pipeline can read the `copernicus-dem-30m` bucket: 589 GB as published, 1″ cells (about
   3 blocks at 1:10). It is not part of the prototype.

## Alternatives

- **Ship GLO-90 with the mod.** About 9 GB, far over Mod DB's archive limit.
- **Ship ETOPO 15″ (463 m, public domain).** It would help worlds of 1–2 million blocks but not the fjords at 1:10.
  Its size is not measured.
- **Host converted tiles ourselves.** The licence allows it with the notices (Proposal 8), and players would download
  about 9 GB instead of 71 GB. It needs hosting; the bucket is public and free. Chosen on 5 November as the default
  source, with the bucket kept as the fallback ([Data release](#data-release-tall10)).
- **Coarser height steps** (2 m: 4.6 bits per cell, up to 1 m off). At 10 m per block 1 m steps aren't needed, but the
  gain is small (about 15 %).

## Consequences and open questions

- **Rivers** keep their courses (HydroRIVERS) and levels (ETOPO 1′, lowest ground near the line). In narrow valleys a
  river may run beside GLO-90's valley floor or above it. Laying rivers on GLO-90 would be separate work.
- **Coasts.** GSHHG's outlines lie 60–170 m off the real shore. Land on the outline's side that GLO-90 has as water
  becomes a low strip at sea level. GLO-90 land on the outline's sea side stays sea. Fjords the built-in data lacks
  become dry valleys at sea level (Verification: the Nærøyfjord). GLO-90's water mask (`WBM` in each tile's `AUXFILES`
  folder; 37.7 kB for N60E006; 1 ocean, 2 lake, 3 river) could turn such columns into sea. That changes rule 2 and is
  the author's call.
- **A surface model.** GLO-90 includes forest canopy (partly) and modern buildings, dams, open-pit mines and road
  cuts. Reservoirs are flattened water surfaces, and Earth leaves reservoirs out (1318). So a reservoir valley
  (Three Gorges, Lake Nasser) would become a plain at the reservoir's level, behind a dam wall. This needs GLO-90's
  water-body mask (`WBM`, about 38 kB per tile) or the built-in heights under reservoirs. The prototype does neither.
- **Waiting.** Generation pauses while a tile downloads. At 1:10 a tile is about 11,100 blocks north-south, so this is
  rare once players are inside one.
- **Offline play** needs the tiles downloaded first. (Until 2.0.0-rc.3 the squares fell back to built-in heights for
  good; now a world that starts offline stops, and one that loses the network while it runs takes the built-in heights
  for that session only: [below](#a-tile-or-file-that-cant-be-had-rc3).)
- **Disk.** 8.61 GB for everything (measured). A world on GLO-90 needs only the tiles players reach.

## Prototype

What is built and checked is in [Implementation](#implementation) and [Verification](#verification). Both are filled
in as the prototype is built.

## Implementation

Prototype Earth 1.0.3-glo90.4 on `claude/glo90-elevation` (glo90.1: first build; glo90.2: tile cache race fixed and the
licence's notices; glo90.3: the summit lift; glo90.4: whole-world map threads on built-in heights):

- `src/Core/DemTiles.cs`: GLO-90 names and grid sizes; a GeoTIFF reader for Copernicus's COGs (classic little-endian
  TIFF, float32 tiles, deflate through `ZLibStream`, floating-point predictor 3) without native libraries; Earth's tile
  file (`EGLO`, Decision 6) encode and decode with a checksum.
- `src/Core/FineElevation.cs`: the store's lookups: which squares GLO-90 has, the tiles on this machine (24 decoded
  tiles kept in memory), bilinear heights across tile edges, squares a world keeps on built-in heights, and the
  per-thread permission to wait for a missing tile.
- `src/Core/DemDownloader.cs`: fetch one tile into memory, convert, read back, write; one fetch per square at a time;
  "Download all" with progress, stop and resume; a census of the store. The source is the bucket or a local folder.
- `src/EarthElevationSystem.cs`: the server side (attach to a world on GLO-90, the saved fallback squares
  `earth-glo90-fallback`, the chunk's tiles fetched before its terrain, prefetch around players) and the client dialog
  (`.earthelevation`): status, "Download all..." behind a confirmation that names the sizes still to download and keep,
  stop, and the attribution. Start and stop need the control privilege.
- `src/Core/TerrainModel.cs`: `Fine`; `NaturalColumn` and `CoastColumn` take GLO-90's height for dry land once the
  column is decided as land; the 30″ residual and the summit correction are skipped there. Rules 2 (`FineRules`,
  `FineSummitLift`): the narrow lift at listed peaks (Proposal 2), each peak's top found once and kept.
- `src/Core/FineElevation.cs` `TryHighest`: GLO-90's highest grid point within a radius, across tile edges.
- `src/Core/FineElevation.cs` `BuiltInOnly` (glo90.4): a per-thread switch to the built-in heights for maps of the whole
  world. It is on by itself for threads named in `SweepThreadNames` (Wmap's "Wmap world chart"); a reader on shared
  pool threads sets it around each sample and restores it. Never for anything that places blocks or entities.
- `src/EarthWorldGen.cs`: `GenerateTerrain` calls `PrepareChunk` first. `src/EarthMapSystem.cs`: attaches the world
  after the model is built; `/earth elevation`, `/earth elevation download all`, `/earth elevation cancel`.
- `src/EarthParams.cs`: `FineElevation` (proto 95). `src/EarthWorldOptions.cs`: `earthElevation` frozen at creation.
  `worldconfig.json`, `assets/game/lang/worldconfig-en.json`: the Earth tab's "Land heights".
- `src/EarthMapConfig.cs`: `ElevationSource` (bucket or folder), `ElevationDownloads` (3), `ElevationPrefetchBlocks`
  (4096).
- `data/glo90-tiles.bin` (embedded, 8,100 bytes) from `tools/bake_glo90_tiles.py` and the bucket's listing.
- `src/SelfTest.cs`: per place, GLO-90's share of dry land, highest GLO-90 and built-in heights, and a profile.
- `tools/fetch_glo90.py`: the whole source into a folder, resumable, checked against the bucket's MD5s.
  `tools/Glo90Convert`: a folder of source tiles into the store with the mod's own code. `tools/setup-glo90-world.py`:
  a 1:10 test server. `tests/ElevationTests`: reader, codec and lookups on local tiles.
- `EarthMap.csproj` and `tests/EarthWorldOptionsTests`: a reference to the runtime's `System.Private.Uri` (HttpClient).

### Data release (tall.10)

Test build 1.0.3-tall.10 on `claude/tall-glo90` (5 November 2026, uncommitted; built, not installed). The converted
store is release `egl-v1` of [Temporal-Poets-Society/earth-glo90](https://github.com/Temporal-Poets-Society/earth-glo90)
(repository commit `3486c14`: README, `LICENSE.md` for the data and the notices, `ATTRIBUTION.txt`, the dataset's
`eula_F.pdf`, `manifest.json`):

- Five zips stored without compression (the tiles are already compressed), 1.72 GB each and under GitHub's 2 GB limit
  per file, split in square order; each also holds `ATTRIBUTION.txt` and `eula_F.pdf`. `manifest.json` (2.6 MB) gives
  every part's size and SHA-256 and, for every square, its part, offset, length and SHA-256.
- The tag names the tile format (`egl1`), not a mod version. Earth versions that read format 1 use these files as they
  are, so a machine downloads them once; a new tile format would get a new tag beside this one.
- `src/Core/DemDownloader.cs`: the release is the default source (`EarthMapConfig.ElevationRepo`), the bucket the
  fallback (`ElevationSource`); an empty value turns either off. A square a world needs: the manifest (downloaded once
  and kept as `manifest-egl1.json` in the store), then one HTTP range read of the square's bytes in its part (through
  GitHub's redirect to its file host). The bytes must match the manifest's SHA-256 and the tile header (magic, format,
  square) before they are kept. A square the release can't give is converted from the bucket as before.
- Download all from the release: a part with more than a quarter of its bytes still missing is read whole in one
  stream, its squares kept as they pass and the rest skipped over; a broken stream resumes where it stopped, and a read
  that stalls for a minute counts as broken. The other parts' missing squares are read one by one. Anything left is
  fetched square by square, from the bucket if the release fails. Squares already here are skipped.
- The dialog (`.earthelevation`) has a "Download from" dropdown: "Earth's data release (GitHub, 8.6 GB, ready to use)"
  (default) or "Copernicus originals (Amazon S3, 71 GB, converted here)", with a line on what each means. The
  confirmation names the sizes still to download and keep for that source, and progress shows the source, squares
  kept and failed, GB downloaded of about the expected and the time left. The command takes the same choice:
  `/earth elevation download all [release|copernicus]`.
- **On the Earth tab:** "Terrain · GLO-90 land heights" (`earthGlo90Download`): "Download as the world reaches them"
  (`needed`), "Download all: Earth's data release (GitHub, 8.6 GB)" (`release`, the default since the 1.0.3 test build
  tall.12), "Download all before the world starts (GitHub, 8.6 GB)" (`first`, from 2.0.1) or "Download all: Copernicus
  originals (71 GB, converted here)" (`copernicus`). Its hover text holds what the Earth size's used to say about GLO-90,
  what each choice does, and the licence's notices.
  - The tab can't hold a button. The 1.22.7 main menu only reads each mod's `modinfo` (`ScreenManager.loadMods` calls
    `ModLoader.LoadModInfos`); mod assemblies load and run when a world starts (`SystemModHandler`,
    `ServerSystemModHandler`). So the choice is saved with the world, and the download starts when the world opens
    (`EarthElevationSystem.AutoDownload`, at the server's RunGame phase).
  - For players with the server's control privilege the dialog then opens by itself, once no other dialog is open (a
    new world's first join shows character creation first). Play goes on meanwhile; chunks fetch their own squares as
    before.
  - It starts again each time the world opens until every square is here or a player stops it. The save remembers
    which (`earth-glo90-download-all`: "done" or "stopped"); the server shutting down mid-way or failed squares don't
    count, so the next opening goes on.
- **Download all before the world starts** (EA-0133, Earth 2.0.1). The author asked for a button that downloads
  everything before the game is joined; of the places the author named, 1.22.7 runs mod code at none before a world
  starts (above: the main menu, the world creation screen; the game has no mod config screen). The first is the
  server's start-up, and a single-player world shows it on its loading screen (`GuiScreenConnectingToServer`), which
  prints the server's story-event log lines as they come and whose Cancel stops the server (`IServerAPI.IsShuttingDown`).
  The author chose (15 November 2026) a fourth choice on the tab, with "Download all" (play while it downloads) staying
  the default, over making "Download all" itself wait: on a slow connection the wait is 15 minutes to hours.
  - `EarthElevationSystem.DownloadBeforeStart`, at the `WorldReady` phase after `EarthMapSystem` attached the world and
    before the spawn chunks (`TriggerWorldgenStartup`): only an Earth world on GLO-90 heights, with `first`, at its first
    opening (`SaveGame.IsNew`), not on a dedicated server, and not when every square is here. It starts download all from
    the release through `StartAll` (the free-space rule, the store and its checks as for every download all) and waits
    on the server's main thread (`Core.DownloadFirst.Wait`, polling every 250 ms).
  - Progress goes to the loading screen as story-event lines: a first line (the squares missing, "up to 8.6 GB", that
    Cancel goes back to the menu and opening the world again plays while the rest downloads), then a line when the
    download has moved on 5 %, at most one per 20 s and at least one every 3 minutes, at most 40 (the screen stops
    updating past 10,000 characters), and a last line (done; squares that couldn't be had; or the error). The size comes
    from the progress lines, not the first one: a mostly missing part of the release is read whole.
  - Cancel stops the download with the server. A new world's save is stored when it is created, but nothing generated
    and no mod save data is kept before the RunGame phase; its next opening isn't new, so it plays while the rest
    downloads (`first` downloads as `release` from then on). A refused (no room) or failed download says why there and
    lets the world start, as `release`.
  - A dedicated server doesn't wait: it has no loading screen, and its players couldn't join meanwhile.
- `tests/ElevationTests`: a small release built from the store (two parts, 41 squares, one damaged copy): range reads,
  the cached manifest, the damaged copy refused and that square converted from the bucket instead, download all (28
  kept, 13 skipped, 27 from the release and 1 from the bucket, the expected bytes), an unreachable release reported as
  the error. All pass.

## Verification

**Reader and codec** (`tests/ElevationTests`, 100 published tiles from across the latitude bands): all pass. The C#
reader's heights equal `tifffile`'s on N05E102, N08E171 and N11E032. Every tile reads back exactly. 278 MB as
published became 34.2 MB (2.61 bits per cell); per tile 64 ms to read, 127 ms to encode, 13 ms to decode. The tests also
cover the tile list, open sea, a square kept on built-in heights, four requests for one tile fetching it once, a fetch
on first use, and a census of the store.

**A 1:10 world on GLO-90** (headless dedicated server, Earth 1.0.3-glo90.1, `tools/setup-glo90-world.py`): 5,120,000 ×
2,048,000 blocks, height 1024, sea level 128, linear curve, 11,378 blocks per degree. The self-test generated 13 × 13
chunk columns at each of three places, with tiles fetched live from the bucket into an empty store. No errors.

| Place | Tiles fetched | Dry land on GLO-90 | Highest GLO-90 (real) | Highest 2′ grid | Terrain y max |
|---|---|---|---|---|---|
| Nærøyfjord (60.90°N 6.87°E) | 1 | 43,038 of 43,038 sampled | 1,383 m | 1,028 m | 267 |
| Everest (27.99°N 86.93°E) | 4 | 43,264 of 43,264 | 8,723 m (8,849) | 7,479 m | 1,009 |
| Fuji (35.36°N 138.73°E) | 1 | 43,264 of 43,264 | 3,751 m (3,776) | 2,841 m | 507 |

The same world on built-in heights (same seed, places and settings, 9.9 m per block):

| Place | Terrain y 10 % / 50 % / 90 % / max, GLO-90 | Built-in | Built-in max in metres |
|---|---|---|---|
| Nærøyfjord | 127 / 143 / 215 / 267 | 164 / 179 / 203 / 232 | about 1,030 m |
| Everest | 835 / 899 / 961 / 1,009 | 838 / 910 / 968 / 1,022 | 8,849 m (summit correction) |
| Fuji | 394 / 439 / 487 / 507 | 379 / 421 / 494 / 537 | about 4,050 m (real 3,776) |

- On built-in heights the Nærøyfjord area is a rolling forested upland with ponds and no valley; on GLO-90 it is a
  valley floor at sea level between rock walls.
- Everest: the built-in world reaches the surveyed 8,849 m through the summit correction (0039); GLO-90 alone gives
  8,723 m. In the raw tiles, the highest point within 1 km of each summit is 123 m low for Everest, 46 m for K2, 13 m
  for Mont Blanc, 161 m for the Matterhorn and 21 m for Fuji (`glo90_summits.py` in the sources note's folder).
- Fuji: the built-in world overshoots by about 270 m (cause not looked into; it is not part of this prototype); GLO-90
  comes within 25 m.
- Columns follow GLO-90 to 0.2 m on average (the 1 m undulation); terrain matches the model in 99.6–100 % of columns.
- The mean difference from the 2′ grid is 325–528 m over each area: the grid smooths valleys up and ridges down.
- The server also fetched N00E010 and S01E010, where it generates its default spawn chunks before Earth moves the
  spawn. No square fell back to built-in heights.
- **The Nærøyfjord is a dry valley.** GLO-90's water mask has the fjord as ocean at 60.90°N (6.865–6.870°E, 0 m, walls
  of 1,440 m within 2 km). Earth's land/sea data has no fjord there, so under rule 2 its floor became dry land at sea
  level (column tops 126–128, small ponds) between walls that now rise to y 214–267. Fjords and other water narrower
  than the built-in coast data will look like this until GLO-90's water mask may decide sea (Consequences).
- The tile cache could, in a race, keep a tile written by another thread as missing, so a later chunk would have marked
  its square as built-in for good. Fixed in glo90.2 (a missing mark isn't kept if a tile arrived meanwhile; a successful
  fetch clears it).

**The summit lift (rules 2, glo90.3).** `TryHighest` finds the same tops as `glo90_summits.py` at Everest, K2, Mont
Blanc, the Matterhorn, Fuji and Denali, 17–199 m from the catalogue's points (`tests/ElevationTests` with the whole
source as fourth argument; it also checks a search across 47° N and none where a tile can't be had). A new 1:10 world,
with the Matterhorn added:

| Summit | GLO-90 top | From the catalogue's point | Lift | Column | Terrain y max (rules 1) |
|---|---|---|---|---|---|
| Everest | 8,726 m | 150 m | 127 m | 8,849 m, y 1022 | 1,022 (1,009) |
| Matterhorn | 4,317 m | 153 m | 162 m | 4,478 m, y 580 | 580 (not run) |
| Fuji | 3,755 m | 26 m | 24 m | 3,776 m, y 509 | 509 (507) |

The rest of each area is as before. At the Matterhorn the terrain differs from the model by up to 57 blocks in 0.4 % of
columns (6–21 at the other places); not looked into.

**The whole dataset** (4 November, with the author's permission for the download). `tools/fetch_glo90.py` fetched all
26,475 tiles, 70.78 GB, in 48.9 minutes (24 MB/s, eight at a time), each checked against the bucket's MD5; none
failed. `tools/Glo90Convert` (the mod's converter, eight workers) turned 71.06 GB into 8.61 GB in 12.0 minutes: 12.1 %,
2.74 bits per cell over 25.1 billion cells, none failed. The store is `VintagestoryData/ModData/earth/glo90` on the
author's machine, where every world on GLO-90 now finds its tiles without downloading. Everest's square came out 1.1 MB
and a 600-column square at 60° N about 0.5 MB. In the raw tiles Aconcagua is 54 m low and Denali 21 m.

**Who reads tiles off the generation thread.** The self-test records where lookups for tiles that aren't on the
machine come from. About 51,000 at startup came from Wmap's world chart (`BaseRaster.Bake`, below-normal threads that
sample the whole world row by row) and from Cylinder World's polar model (`EarthPolarModel.Sample`). With few tiles here
they cost little. With all tiles here, the world chart would decode tiles all over the globe, a few at a time through
a 24-tile cache, and would push the generating thread's tiles out of it.

**Whole-world maps with the whole store.** These are new worlds on the same 1:10 settings and four places (the
Nærøyfjord, Everest, Fuji and the Matterhorn), with Wmap 0.5.1. The server process's memory and CPU were sampled every
10 s.

| Build | Wmap world chart | Wmap climate raster | Tiles read by the last place | Server CPU | Memory peak |
|---|---|---|---|---|---|
| glo90.3 | unfinished when the self-test stopped the server (2 min 46 s) | not reached | 188,977 | 2,860 s | 15.0 GB |
| glo90.4: chart threads on built-in heights | 53.7 s | 107.0 s | 71,948 | 1,598 s | 13.0 GB |
| glo90.4 and the climate raster on built-in heights (scratch build of Wmap, below) | 43.1 s | 14.5 s | 535 | 594 s | 11.3 GB (one sample) |
| The same world on built-in heights (glo90.4; three places, without the Matterhorn) | 53.0 s | 10.2 s | none | 573 s | 11.7 GB |

- glo90.3: Wmap's world chart samples Earth's columns every 500 blocks across the globe on 16 threads. Reading GLO-90
  there, it decoded tiles everywhere and didn't finish.
- glo90.4 gives threads named "Wmap world chart" the built-in heights (`BuiltInOnly`, `SweepThreadNames`). That fixed
  the chart. `tests/ElevationTests` checks that such a thread gets no GLO-90 height while another thread does.
- The climate raster then read GLO-90. It samples `ForestPotential` about every 5,000 blocks, and with local alpine
  ecology that takes the column's own height. Between the chart's end and the raster's, memory rose from 7.0 to 13.0 GB
  and the server used about 1,100 s of CPU.
- Earth can't recognise the raster's threads. They are shared pool threads (`Parallel.For`), the same kind as Wmap's
  far-terrain workers, which must read GLO-90 for nearby terrain's heights and forest tint. Generation's forest map
  shouldn't change either: its treeline follows the column's GLO-90 height. So the raster has to ask for built-in
  heights itself.
- A scratch build of Wmap 0.5.1 with one change did that: in `AtlasServer.BakeClimate` each sample sets `BuiltInOnly`
  and restores it after. The change is not in Wmap. With it, the chart and the raster took as long as on the built-in
  world, and the self-test's places read 528–535 tiles in all. The 11–12 GB peak while the raster runs is the same on
  built-in heights, so it doesn't come from GLO-90. The chart's time varies between runs: 42.6 and 53.0 s on built-in
  heights.
- In every run the four places' terrain and summits were the same. Each place took 16–33 s with the scratch build
  against 23–66 s with glo90.4.
- **Wmap's climate raster adopts the switch (Wmap 0.5.2, 5 November; built on `claude/climate-builtin-heights` from
  Wmap's `release/0.5.0`, not committed or installed).** `Integrations/EarthHeights` finds `BuiltInOnly` by reflection
  and sets it around each climate sample, restoring the old value (Wmap WM-D45). Far terrain's biome tiles keep GLO-90.
  Offline, on this branch's tall.8 `EarthMap.dll` with the whole store (the author's 4,096,000-wide settings, 1024 ×
  512, 16 threads, below normal priority beside the running game): 6.8 s warm, 22.1 s cold and 0 tiles read, against
  87.9 s and 70,817 tiles without it. Forest differs in 1.5 % of the raster's texels; temperature and rain don't.
  Not yet seen on a server or in game. Wmap 0.5.1 and older still pay about 100 s once per world on a machine with
  many tiles.
- **Open:** Wmap setting the switch on its world chart's threads itself, in place of Earth's thread-name list.
- **Open, not checked:** a region's forest and shrub maps are made before its first chunk. On a generation thread's
  first region, tiles not yet on the machine may not be fetched for them (the thread may fetch only after its first
  chunk's `PrepareChunk`). That region's forest density would then follow the built-in heights there.

**A random start with the whole store (5 November 2026).** The author created a 1:10 world on tall.7 with all tiles
on the machine and a random temperate start. It took about 6 minutes to load and looked frozen; the server logged the
spawn 4 min 12 s after the river index (earlier random starts on built-in heights: 4–12 s). The random start's
catalogue (`SpawnSearch.SpawnCells`) checks every half-degree cell of land on the globe with `RandomSpawnable`, so on
full GLO-90 it decoded tiles everywhere. A scratch harness timed `FindClimatePlace` (seed 12345, temperate, the
author's 4,096,000-wide settings, the whole store):

| Build | Rules 2 | Rules 3 | Tiles read |
|---|---|---|---|
| tall.7 | 248.5 s | 235.3 s | 15,615 |
| tall.8 (not kept): the catalogue on a thread of its own with `BuiltInOnly` (the included GLO-90, else the built-in grid) | 11.6 s | 10.7 s | 2 |
| tall.9: the terrain check only on the cells drawn ([0037](0037-varied-random-starts.md), Selection on demand) | | 3.2 s | 75 |

- Rules 3 didn't cause it: both rules read the same tiles.
- The author asked whether the search needs the whole globe ("does it need to?"). It doesn't: only which regions have
  land at the climate, which the climate and land data give without terrain. tall.9 checks a cell's terrain when it is
  drawn, on the world's own heights, and puts aside a cell or region that fails; the odds are the same (0037). On the
  whole store, the first search per climate took 0.0–3.2 s and read 3–170 tiles (temperate 3.2 s, hot 0.0 s, warm
  3.0 s, cool 0.7 s, icy 0.6 s). tall.8's workaround is gone.
- The place a seed gives changes (seed 12345, temperate: 38.17° N 89.48° W on tall.7; 38.73° S 143.40° E on tall.9).
  Only new worlds search; a world keeps its saved spawn.
- The same world's log, after the player joined: Wmap's world chart 50.4 s, its climate raster 116.0 s (Wmap 0.5.1;
  0.5.2 asks for built-in heights there, above).
- `tests/EarthMapTests`: the random-start checks pass (built-in heights; the first search 1.1 s); the one failure,
  Dalmatia's rocky coast at 4M, was failing before this change. `tests/SpawnDistributionTests after` passes.
- Not checked: a new world made in game on tall.9.

**The author's world settings, 13 places (1.0.3-tall.4, 4 November).**
[terrain-vs-glo90-2026-11-04](../research/terrain-vs-glo90-2026-11-04.md) has the method, tables and figures. The
settings: Earth size 4,096,000, about 12 m per block; height 1024; sea level 110; linear. Earth's terrain model ran
offline against the published tiles at 13 places: the Himalaya, the Alps, the Dolomites, the Andes, Kilimanjaro, Fuji,
Yosemite, the Grand Canyon, the Nærøyfjord, Glen Coe, the Middle Rhine and the Gauja.

- **Dry land follows GLO-90.** The mean difference is 0.3–0.4 m (the undulation); column tops are within half a block.
  The number of distinct tops equals the real one within two. On built-in heights the same places are 7–241 m off on
  average and have 2–24 times the real number of tops in mountains (the detail noise).
- **Rivers don't follow it** (Consequences, Rivers). Where a mapped river crosses a window, its bank and valley band
  covers 12–35 % of the area and lies 17–193 m below GLO-90 on average there; 8–23 % of the area ends more than 30 m
  below the real ground. The Colorado's course crosses the Grand Canyon's bends, its water a median 210 m below
  GLO-90's ground at its own columns. The Middle Rhine's valley shaping levels about 3 km of the gorge's slopes.
- A headless server with these settings made the four test places with GLO-90 on every dry-land column; Everest's
  column 8,849 m at y 1022; no errors.

**The data release (5 November 2026, late evening).**
- Every asset's SHA-256 on GitHub equals the manifest's.
- Single squares by range read through GitHub's redirect took 0.1–0.9 s each and equal the local store byte for byte;
  .NET's HttpClient keeps the range across the redirect.
- Download all into an empty folder, outside the game (`DemDownloader`, this connection): 2 min 10 s for 8.6 GB,
  26,475 squares, none failed, every file equal to the store's.
- In game on the AgentBridge bench, tall.10, a new 1:400 world starting in the Alps (46.40° N 9.90° E), the bench's
  own store moved aside first: the world took the manifest and its first 114 squares from the release as it generated,
  without errors. The dialog showed the source dropdown and its explanation. Download all from the release then
  showed progress ("3,233 squares kept, 0 failed, 23,110 to go; 1.8 GB of about 8.6 GB downloaded") and finished with
  all 26,475 squares (8.6 GB) on the bench's server, none failed.
- Its time left read "about 0:00 h left" under a minute; now minutes and hours ("about 2 min left" in the next run).
- The Earth tab's download all, on the bench (new 1:400 worlds in the Alps, the play style's default set to the data
  release, part of the bench's store moved aside each time):
  - 301 squares missing: started as the world opened, all 301 fetched in 8 s, the save marked "done"; it had finished
    before the player joined, so no dialog.
  - About 5,900 squares missing: the dialog opened by itself after joining ("484 squares kept, 0 failed, 5,435 to go;
    0.2 GB of about 1.8 GB downloaded, about 2 min left"). `/earth elevation cancel` stopped it (1,222 fetched) and the
    save marked "stopped"; the same world opened again started nothing and opened no dialog.
  - A world closed mid-download wasn't marked; its squares' fetches ended as cancelled.
  - Not seen: the tab's row on the world creation screen itself (the bench makes worlds from the play style's defaults
    and can't reach the main menu).
- **Download all before the world starts** (EA-0133, 15 November 2026), on a private bench (its own data folder, the
  default bench's store minus 1,500 squares, the real release, the play style set to `first`): the loading screen showed
  the first line, "71% (1.3 GB of 1.8 GB; 25,125 of 26,475 squares here), under a minute left" and "every square is here
  (1.8 GB downloaded in 31 s). The world starts.", then vanilla's spawn chunks; the save was marked "done". A second new
  world: Cancel on the loading screen stopped the download ("stopped with the world") and the game was back in the main
  menu within 4 s; opened again, the world played at once, the dialog opened and the 1,500 squares came in the
  background. Offline: ElevationTests (a whole wait with its lines, a stop, a release that can't be reached) and
  EarthWorldOptionsTests (the choice and its row).
- **Story locations on a small Earth with the whole store.** The first of those worlds didn't open within 3 minutes.
  A dump of the server thread showed `StoryPlacer.Place` → `Search` → `Evaluate` → `TerrainModel.FineMean` →
  `FineElevation.Load` → `DemTiles.Decode`: the story-location searches (thousands of sites within up to ±12,000 blocks
  of each mirror image, hundreds of 1° squares at 1:400) decoded tiles again and again through the 24-tile cache. With
  few tiles on the machine (the earlier bench world, 114) they fell back to the included heights quickly, so this showed
  only with the whole store, as the random start's catalogue did (tall.7). tall.10's searches read the included heights
  on that thread (`FineElevation.BuiltInOnly`), and the chosen site's pad height is measured on the world's own heights:
  story placement took 4 s, the world opened in 43–46 s.
- Not checked: the download from Copernicus in game (the same code as before tall.10, which the tests run against a
  local folder), a slow or interrupted connection in game, multiplayer.

### A tile or file that can't be had (rc.3)

Review round 3 of 2.0.0-rc.2 (adversarial review A3 and A4, 9 November 2026) found that a short network failure became a
world's choice for good: a square fell back to the built-in heights after about 20 s of retries, the lake and river
outlines' and the dam file's "off" key was saved the first time a world needed the file, and a new world froze its
ground to the 2′ sets; an existing GLO-90 or 30″ world started offline went on with an error. Two servers on one machine
(a single-player game and a dedicated server share `VintagestoryData`) also broke each other's downloads. The rule
since (package L, `claude/rc2-fix-life`):

1. A choice for good is saved only on a source's answer that it lacks the file: HTTP 404 or 410, a local source folder
   without it (`DataTransport.NotThereException`, not retried), no source set, the release's manifest not listing a
   square while Copernicus answers 404 or isn't set (`DemDownloader.Lacks`), or a file had and checked that this build
   can't use.
2. Any other failure (the network, timeouts, a download that never checks out, the disk) while the world starts
   (WorldReady and the spawn chunks; nothing is saved before the game runs) stops the server with one error; the world
   opens as it was at the next start.
3. While the game runs, a square that can't be fetched now takes the built-in heights for that session only
   (`FineElevation`'s session fallback: not saved, one warning) and is fetched again at the next start. The release
   files and the 30″ ground sets are fetched only at start.
4. A world that has generated with a file (its "on" key, a 30″ world) never goes on without it: the server stops.
5. Every fetch into `ModData/earth` takes its folder's turn (`DataTransport.TakeTurnAsync`: `fetch.lock`, kept in the
   folder, waited for up to 20 minutes) and looks again once it has it; GLO-90 squares are written through a temporary
   name of their own.

Not changed (proposal): a square that falls back for the session while the game runs may meet later chunks with a step
once its tile arrives; waiting for the network instead would stall all world generation.

Changed on 14 November 2026 (EA-0099, the Fable pass's FP-03; option (a) of three, the author's decision of 14 November
2026): rule 3 no longer gives a square the built-in heights while the game runs. A chunk whose square can't be fetched
then is not made: Earth's chunk passes (`FineElevation.ChunkPass`) throw `TileUnavailableException`, EarthWorldGen notes the chunk (no refusal; the
world goes on), the game saves it empty, Earth's later passes skip it, and the next start deletes it so that it is made
on the square's heights. Players get a chat line, at most every 10 minutes a square; the square is tried again after 5
minutes and its chunks fail at once meanwhile. Reads outside a chunk pass (map regions and layers, the story pad: the
game doesn't guard those handlers) take the built-in heights for that read and don't give the square up. A chunk built
on in the meantime loses what was built, as every noted chunk does. (Fable's re-review of `f55cba9`: a volcano's planning
that reaches such a square leaves the chunk for the next start too, instead of refusing the world; the chat line says the
chunks are holes to keep away from and not to build in; the 5 minutes count from a fetch that failed.) The author took
this knowing what it costs: the chunks left unmade are open holes a player can fall into, and anything built in them is
deleted when they are made at the next start. Also the author's decisions of 14 November 2026: such chunks get no
placeholder floor (anything built on it would be deleted when the chunk is made), and a map region made while its square
can't be had keeps its coarse maps (climate, forest) from the built-in heights for good, as before EA-0099: a known limit
for 2.0, to be looked at again after it.

### Every tile the release's (EA-0106)

The Fable pass of the 14 November 2026 code review (FP-15, FP-17) found that only tiles from the release were checked
against its manifest: a square converted from Copernicus, or a tile kept from before egl-v1, was never compared, so two
computers could make one world on different heights; and downloads and the GeoTIFF reader took sizes from the source
unchecked (a body without end, a mirror's manifest asking for 1.5 GB a square, a damaged GeoTIFF directory allocating
4 GB). The rule since (`claude/tile-checks`):

1. Where the release has a square, the square's tile has the release's heights. They are judged by the heights'
   checksum (`DemTiles.Checksum`, FNV-1a over the decoded heights, which every tile's header carries and `Decode`
   verifies) against `data/glo90-heights.bin` (`Glo90Heights`: 26,475 checksums, 212 kB, embedded in the mod; baked from
   egl-v1's tiles by `tools/bake_glo90_heights.py`). A conversion with other heights (a source file issued again, another
   converter) is not kept and the release's tile is fetched instead; when the release can't be had either, the square is
   a failure that may pass, not one the sources lack. Without the table a conversion is kept as before, said once.
2. A tile already here is judged the same way at its first read in a session (`FineElevation.Verify`,
   `DemDownloader.KeptMatches`; no manifest needed): with the release's heights it passes, whatever its bytes (a tile
   converted earlier with the bucket's ETag is taken as it is). Any other stays in use for the session (the square never
   lacks heights, offline too), and the release's tile is fetched beside it (`.release`); at the next start it takes the
   old one's place (`FineElevation.ApplyReplacements`): with the same heights silently, with others the old kept as
   `.differs` and the square named in the log (chunks made before keep the old heights). (Fable's re-review of
   `90692fe`.)
2a. (Fable's re-review of `255baa7`) Judging by file bytes did not hold: a tile's bytes depend on the runtime's Brotli.
   On .NET 8.0.25, where the release was made, all 602 sampled release tiles encode again to the same bytes; on the
   game's .NET 10.0.8, 341 of them encode to other bytes with the same heights, and a canary tile could not tell. The
   byte comparison, the canary and the rewrite of converted tiles went; ElevationTests runs on both runtimes.
3. Limits: a manifest no larger than the default release's pinned size, or 16 MB for a mirror, and no entry over 8 MB
   (egl-v1's largest tile is 1.18 MB; an entry's end is found without overflow); a GeoTIFF no larger than 64 MB (GLO-90's
   largest is 6.1 MB), its directory, value arrays and tiles inside the file, at most 4,096 columns, rows, tile columns
   and tile rows and 65,536 values an entry; a conversion of other dimensions than GLO-90's is not kept (L8). A manifest
   whose copy can't be kept in the store is used for the session (L7).

Not changed: a missing local source folder is still retried as a failure that may pass (L7's second half): treating it
as the source's answer would let a typo in a setting take a square for good (rule 1 of the section above).

## History

- 4 November 2026: proposed at the author's direction; prototype started on `claude/glo90-elevation`. The same day,
  at the author's word ("Yes, commit"), prototype 1.0.3-glo90.4 committed there (`bbe2e60`).
- 4 November 2026: the author: "our game render does not look like GLO 90 at all", then "Get terrain match GLO data and
  sample different locations". Combined with 0050 (tall.3) and `claude/river-index-speed` in test build 1.0.3-tall.4
  on `claude/tall-glo90`; 13 places compared (Verification). Installed at 22:27 Europe/Riga at the author's
  request ("Yes, install tall.4"), replacing tall.3.
- 5 November 2026: [0053](0053-scales-included-glo90-and-glo90-rivers.md) (proposed, test build tall.5) adds exact scales with Automatic settings, included GLO-90 heights and rivers on GLO-90.
- 5 November 2026, evening: GLO-90 rules 3 in [0053](0053-scales-included-glo90-and-glo90-rivers.md) (test build tall.7, installed 20:07 at the author's request): new worlds join the 3″ points by a smooth curve (Catmull-Rom) instead of straight lines and add no undulation noise; rules 1 and 2 worlds keep theirs.
- 5 November 2026, 20:16–20:23: the author's new 1:10 world on tall.7 took about 6 minutes to load ("looks frozen",
  then "World looks amazing. but startup is terrible"). The random start's catalogue read full GLO-90 across the globe
  (Verification, A random start with the whole store). Test build tall.8 built the catalogue on the included heights
  (10.7 s instead of 235 s offline; not installed). The author: "does it need to?". tall.9 checks terrain only on the
  cells it draws (0037): 0.0–3.2 s on the whole store. Installed 21:05 at the author's request (0053 History).
- 5 November 2026, late evening: at the author's direction (above) the converted store published as release `egl-v1`
  of `Temporal-Poets-Society/earth-glo90` and made Earth's default source, with the bucket kept as the fallback and a
  source dropdown in the dialog ([Data release](#data-release-tall10)); test build 1.0.3-tall.10 (0053 History).
  At the author's word the Earth tab got the choice too ("Terrain · GLO-90 land heights", the download starting when
  the world opens); found on the way: story placement read full GLO-90 across hundreds of squares on small worlds
  (Verification). Built, not installed.
- 9 November 2026: review round 3 of 2.0.0-rc.2 (A3, A4): fallbacks for good only on a source's answer, a stop for
  failures while the world starts, session fallbacks while it runs, turns for every fetch
  ([A tile or file that can't be had](#a-tile-or-file-that-cant-be-had-rc3)); `claude/rc2-fix-life`, not merged.
- 14 November 2026: EA-0099 (the Fable pass's FP-03): while the game runs a chunk whose square can't be had is left
  for the next start instead of taking the built-in heights (option (a): don't make the chunk, note it and make it again
  at the next start, tell the players without flooding the chat; first the release coordinator's call, then the author's
  decision the same day, with no placeholder floor and map regions' coarse maps a known limit); `claude/tile-unavailable`.
- 14 November 2026: EA-0106 (the Fable pass's FP-15, FP-17): every tile the release's where its manifest lists it, tiles
  here compared at their first read, limits on every download and the GeoTIFF reader
  ([Every tile the release's](#every-tile-the-releases-ea-0106)); `claude/tile-checks`.
- 15 November 2026: EA-0133, the author's question whether a button could download all before the game is joined:
  the places 1.22.7 offers were read in the decompiled game, a fourth choice on the Earth tab was prototyped and run on
  the bench, and the author chose it as a separate choice with "Download all" staying the default
  ([Data release](#data-release-tall10)); `claude/glo90-download-first`, in Earth 2.0.1.
