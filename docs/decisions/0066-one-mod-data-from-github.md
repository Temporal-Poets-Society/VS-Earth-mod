# 0066: Earth 2.0 is one mod: the server fetches its data from GitHub, the data-pack mods retire

Status: **agreed** (the author, 8 November 2026: "i think one mod + git", then "Yes, go ahead"); data uploaded,
built in 2.0.0-rc.1; in Earth 2.0.0. Asked for by the author on 8 November: "Check data split and current
role of these mods (they where made just due to size limit on VS mod database) … as we are now hosting largest data on
GitHub (if some data is missing then we need to upload). And plan correct update for all of them as in reality it is
one mod." Uploads approved: "Yes, upload when ready". Date: 2026-11-08.

**Uploaded (8 November):** the public repository `Temporal-Poets-Society/earth-data` with release `ed-v1`: 33 data
files (273.3 MB; 9 terrain, 12 water, 12 ground and ecology), `manifest.json`, `ATTRIBUTION.txt` and Copernicus'
`eula_F.pdf`. Every file's SHA-256 equals the pack index the tall line already checks. `egl-v1` gained
`rivers-outline-glo90-1.bin`, `rivers-outline-glo90-2.bin` and `dams-glo90-1.bin`.

**Licences of the ground sets (the author, 8 November):** `europe-igme5000.egt` "just credit" (uploaded with
"Datenquelle: IGME5000, (c) BGR Hannover, 2007"); `global-crust1.egt` "cite it" (uploaded, cited as the CRUST1.0
authors ask). `baltic-basement.egt` was first "dropped", with the Baltic and Latvian ground data and the Baltic bedrock
template, then kept: "o sorry. Baltic we can keep. we will add more detailed for areas we can find data". So ground
rules stay v1 for every world (the short-lived ground rules v2 without them was reverted before any world saved it), and
`baltic-basement.egt` is on `ed-v1` with the CRUST1.0 citation and its boreholes' sources. Regional detail is welcome
wherever good data exists. `global-glaciation.egts`, the Northern Hemisphere ice-sheet extents of Batchelor et
al. 2019: the article is CC BY 4.0 and the authors' data on the Open Science Framework (project `7jen3`) has no licence
set; the author: "we ship it and just credit … probably they just forgot to add license". Uploaded with the full credit.
`ed-v1` then held 37 files (273.5 MB) and nothing was held back; [0070](0070-shore-strip-and-beach-material.md)'s
`shores-osm-1.bin` (2.2 MB) was added on 12 November (rc.7), so the store has 38 files.

**Built (2.0.0-rc.1, 8 November):** `EarthDataStore` (the 37 files with sizes and SHA-256; ensured before the server
builds a world: copied from an installed Earth 1.0 data pack, else fetched through `DataTransport` with resume, retries
and timeouts; a size-and-time stamp spares rehashing at every start; a server that can't get them stops with the missing
files and where to put them); the server reads each file from the mod, else the store; a client reads only the
climate files in the mod; soil sites, wetlands, floodplains, the ice sheets' ground and the 2′ ground sets fall back to
the store; `EarthDataPacks` and the pack packagers are gone; `tools/package-earth.py` builds the one zip (rc.1:
39,592,025 bytes, 0.4 MB under the limit, so anything new and large goes to the store) and refuses store files, pack
dependencies, versions the Mod DB can't parse and anything over 40,000,000 bytes; `modinfo.json` depends on the game,
Cylinder World 1.0.4 and Hydrology 0.6.1 only; `DataRepo` in earth.json names the source.

**Stays in the mod zip:** besides the client's climate files and `aridity.bin`, the small server files `earth.json`,
`outlets.bin`, `reservoirs-v1.json` and `works-1318.bin` (under 0.2 MB together; since works rules 4,
`works-1318-2.bin`, 0064).

**Review round 3 (9 November 2026, package L, `claude/rc2-fix-life`, not merged):**
- *Which files a world needs (A5).* rc.1/rc.2 demanded all 37 files for every world, so an offline 1.0.x world with its
  two Mod DB packs (9 files) was refused for 28 files its rules never read. Now a saved world needs the files its saved
  rules read (`EarthMapSystem.StoreFilesRead`): the 2′ grid, `rivers.bin`, `soil-site.bin` and `wetlands.bin` for
  every world, the rest by rule (islands, coasts, sea referee, lakes, shores, summits, peaks, glaciers, the included
  GLO-90 heights, the rivers on GLO-90 and their overlays, the stream heads, the 2′ ground sets and floodplains, the ice
  sheets' ground; since rc.7 also 0070's OSM beaches in worlds with shore rules). A new world, or a start with an
  `UpgradeExistingWorld*` flag set, needs all of them (37 then, 38 since rc.7).
  **Open point for the author:** 2.0 reads `soil-site.bin` and `wetlands.bin` in every world (the regional trees' soil
  fit, since the tall line), so a 1.0.x world with regional trees still needs those two files (3.9 MB), which the 1.0.2
  packs don't carry; the server names exactly those two. Either accept that (the message says where to get them), or
  make the soil fit a saved rule so 1.0.x worlds skip it (that changes what such a world generates on a fully stocked
  2.0 server, so it needs the author's decision).
- *Two servers on one store (A4).* Every fetch into `ModData/earth` (the store, the 30″ ground sets, the release
  files beside the GLO-90 tiles) takes its folder's turn (`fetch.lock`, kept in the folder: deleting it on close let a
  pending deletion refuse the next open on Windows and two holders on Linux) and looks again once it has it.
- *Saying what happened (A9).* A wait that runs out says another server holds the turn; a fetch that fails on a full
  disk names the disk (free space against the bytes still needed); the stop message carries the fetch's own error.
- *Fallbacks (A3):* see [0051's rule](0051-glo90-elevation-on-demand.md#a-tile-or-file-that-cant-be-had-rc3): only a
  source's answer that it lacks a file is ever a world's choice for good.

## Where it stands (release/1.0 at 2e3dc53, Earth 1.0.3-tall.25)

- **Nine archives.** Earth needs, besides Cylinder World and Hydrology, eight data-pack mods: `earthterraindata` and
  `earthwaterdata` 1.0.2 (on the Mod DB), and `earthheightdata1`-`4`, `earthwaterfixdata` and `earthgrounddata`
  (built locally only: on neither the Mod DB nor GitHub). The tall line is 9 zips, 317 MB. The packs exist only
  because the Mod DB takes files of at most 40 MiB (`vsmoddb lib/upload-limits.php`; Earth's packagers keep to
  40,000,000 bytes).
- **The game cannot fetch most of them.** The game's own "download missing mods" asks the Mod DB for the exact
  `modid@version`. Six packs aren't there, and `-tall.N` versions don't parse (the Mod DB accepts only `x.y.z` and
  `-dev|-pre|-rc.N`). A multiplayer client therefore can't join without installing them by hand.
- **A missing pack on a dedicated server turns Earth off silently.** The engine doesn't load a mod whose dependency is
  missing and still opens the world, so new chunks come out vanilla next to Earth's (ModLoader.CheckAndSortDependencies;
  saves keep no mod list).
- **Clients carry and hash everything.** A client reads only the climate files, yet needs every pack installed and
  hashes about 255 MB at each start (`EarthDataPacks.Load`).
- **GitHub already carries the big data.** Since 5 November `earth-glo90` (`egl-v1`: GLO-90 heights 8.6 GB, lake
  and river outlines, dams) and since 7 November `earth-ground` (`egts-v1`: the 30″ ground sets) are downloaded by
  the server into `VintagestoryData/ModData/earth`.
- **Gaps:** the pack data itself (terrain, water, coarse heights, water fixes, 2′ ground) is on no GitHub release;
  `rivers-outline-glo90-1.bin`, `rivers-outline-glo90-2.bin` and `dams-glo90-1.bin`, which older tall-line worlds read
  (and outline 1 also a new world that lost its dam file), are on no public release; lakes, river outlines and dams are
  fetched once with no retry, so a short network fault at world creation turns them off in that world for good.

## Proposal

1. **One mod on the Mod DB.** `earth` 2.0.0 is the only Earth download. Its zip (under 40,000,000 bytes) holds the code,
   the assets, and the data a **client** reads: `climate.bin`, `climatefix.bin`, `climatefix2.bin`, `temperature.bin`,
   `rain.bin`, `weather.bin` and `aridity.bin` (moved in from `earthwaterfixdata`). Server-only assets leave the zip
   (`wetlands.bin`, `soil-site.bin`, `floodplains.bin`). Estimated zip: about 39.3 MB of the 40.0 MB. Anything new and
   large goes to the download set, never into the zip.
2. **One data set on GitHub for the server, grouped by subject, not by pack.** A new public repository
   `Temporal-Poets-Society/earth-data`, release `ed-v1`, holds every other file any world's rules read (about 300 MB):
   - **terrain**: `terrain.bin` (the 2′ base grid every world reads, GLO-90 worlds included: sea depths, which GLO-90
     doesn't have, land/sea/lake flags, lake levels, ice thickness, and the land heights of 1.0.x worlds and of any
     square GLO-90 can't supply), `summits.bin`, `peaks.json`, `glaciers.bin`, and the included GLO-90 heights
     `glo90c-index.bin`, `glo90c-1..4.bin`;
   - **water**: `rivers.bin`, `islands.bin`, `coasts.bin`, `lakes.bin`, `shores.bin`, `rivers-glo90.bin`, the GLO-90
     water corrections (`sea-referee.bin`, `rivers-glo90-snap*`, `rivers-glo90-arms*`), `stream-heads-1.bin`;
   - **ground and ecology**: the 2′ ground sets, `wetlands.bin`, `soil-site.bin`, `floodplains.bin`;
   - their ATTRIBUTION.

   The water corrections stay files of their own rather than being baked into the originals: older worlds read the
   originals unchanged, and each correction names the exact files it applies to. With the packs gone, "fix data" stops
   being a name. `earthterraindata`'s contents are still needed by every world; only the pack retires. `egl-v1` gains
   the three older water files (`rivers-outline-glo90-1.bin`, `-2.bin`, `dams-glo90-1.bin`, 66 MB). Like `egl-v1`, the
   tag names a data generation, not an Earth version: a later Earth adds a new file name or a new tag and never
   replaces or deletes a file, so every older world finds its files.

   The author, 8 November, on the pack layout: "i had in mind that current earthterraindata could be replaced by
   earthheightdata1–4 pack on VS mod DB, but now earthwaterfixdata and earthgrounddata makes it explode anyway … would it
   make sense to merge earthwaterdata and earthwaterfixdata or at least make it two part and not "fixdata"".
3. **The server fetches before the first chunk.** A new `EarthDownloads` service (one transport for all of Earth's
   files: resume from `.part`, retries with backoff, size and SHA-256 against a list compiled into the DLL) makes sure
   every file in the list is in `VintagestoryData/ModData/earth/data` before the model is built, logging progress.
   A legacy data-pack mod that is still installed counts as a local source (its files are copied when their hash
   matches), so existing installs and offline servers keep working. `ModConfig/earth.json` keeps a local folder or
   mirror as the source, as `GroundRepo` and `ElevationRepo` do. The GLO-90, outline, dam, lake and 30″ ground fetches
   move onto the same service (this also gives the outline, lake and dam fetches their missing retries).
4. **Fail loudly, never silently.** If a file can't be had, the server logs which file, from where, and how to place it
   by hand, and refuses to open the Earth world (as `RequiredMods` does), instead of generating with parts missing. The
   game's own missing-mod handling still covers Cylinder World and Hydrology.
5. **Clients download nothing.** A client loads only the files in its zip: no packs, no hashing of 255 MB at start-up.
   Joining a server needs only the Mod DB's `earth` (fetched by the game's own downloader at the exact released version).
6. **Dependencies:** `game`, `cylinderworld`, `hydrology`. The eight pack dependencies go. Versions: `2.0.0` for the
   release, `2.0.0-rc.N` for candidates (both parse on the Mod DB).
7. **The Mod DB pages.** `earthterraindata` and `earthwaterdata` stay up for Earth 1.0.x worlds with a note that Earth
   2.0 no longer needs them; the six local-only packs are never published.
8. **Older worlds** keep their rules (save compatibility unchanged): their files are in `ed-v1` and `egl-v1` under the
   same names and hashes, so a 1.0.x or tall test world finds the same data.

## Alternatives

- **Keep packs, all at one version on the Mod DB:** eight more Mod DB pages, every release re-uploads all of them
  (about 320 MB) for the exact-version rule, and players and clients still install nine mods. Rejected: it keeps the
  split the author wants gone.
- **Clients download too:** unneeded, since a client's data fits in the zip.

## Open points

- Whether the Mod DB's rules allow a mod to fetch data at run time: its terms have no clause on it; Earth has
  fetched GLO-90 since 1.0.3-tall.10.
- Uploads (`earth-data` repository and `ed-v1`, about 300 MB; three files to `egl-v1`) need the author's go-ahead.
