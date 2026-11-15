# Earth code review: untrusted input, commands, start-up cost, build and tests

Pinned tree: `scratchpad\earth-fable` at fef6166 (main). Independent pass; `docs\research\code-review-*` not read.
Nothing in the source was changed. Probes and their outputs are in `scratchpad\fable\probes-data\`
(`ProbeCore\Program.cs`, `ProbeLoad\Program.cs`, `out-*.txt`, `tests-quick\`).

Counts: critical 0, high 1, medium 6, low 11, refactor 5.

What was run on the pinned tree:

- `dotnet build EarthMap.csproj -c Release -p:OverwriteRelease=true`: 0 warnings, 11 s, zip 39,706,525 bytes. A second
  build in the same folder gave the same SHA-256 (248534cb...).
- `tests\Run-All.ps1 -Quick -NoBuild` (EARTH_RAW set to the repo's rawdata link): 30 run, 29 pass, 1 fail
  (PlaytestReviewTests, finding M6), 4 skipped as slow. Times in the table at the end.
- `EarthMapTests <root> 384 cost`: exit 0 in 34 s, peak working set 2,053 MB.
- Probes: reader mutations (about 1,000 damaged inputs), GeoTIFF directory mutations (229), a mirror's manifest (16
  cases), a localhost HTTP listener (redirects, endless body), bad source strings, "download all" with failing
  writes, `/ew` command loops, load time and memory step by step.

---

## High

### H1. A default new world starts the 8.6 GB "download all" with no free-space check, and a full disk does not stop it

Where: `worldconfig.json:266` (`earthGlo90Download` default `release`), `src\EarthWorldOptions.cs:293-294` (`auto` is
full GLO-90), `src\EarthElevationSystem.cs:617-632` (AutoDownload), `:585-596` (StartAll), `src\Core\DemDownloader.cs:304-316`
(Write), `:277-291`, `:626-641`, `:222-232`.

What happens: every new Earth world made with the tab's defaults calls `StartAll` when the game starts running. Neither
AutoDownload, StartAll nor `Write` looks at free space (`DataTransport.FreeBytes` is used only for the status text and
for a warning inside the client dialog, `EarthElevationSystem.cs:842`). On a machine with less than about 8.7 GB free
the download fills the disk that usually also holds the save. After that:

- each tile write throws IOException, which `RetryAsync` treats as transient: three range requests, then the square is
  tried from Copernicus (a whole GeoTIFF, three tries);
- a streamed part goes on to its end, every square logged and queued again (`StreamPart` catch, `:632-641`);
- the batch ends as "finished" with `Error` empty, `Failed > 0`, so AutoDownloadEnded does not mark it and it starts
  again at every opening of the world.

Probe (`out-diskfull.txt`; a store where every tile write throws, 40 squares, 4.8 MB in the release):

```
ended yes after 141 s; done 0, failed 40, error ''
bytes read from the sources: 19.0 MB = 4.0 times the release; log lines: 242
  x120: N10E022: attempt 1 of 3 from the release failed: Access to the path is denied.
  x40: N10E020 in part1.zip: Access to the path is denied.
  x40: N10E021: not from the release; trying Copernicus
```

Scaled to the real set: about 34 GB read from GitHub for nothing, then up to three Copernicus downloads per square,
about 160,000 log lines, repeated at each start. The fill-to-zero part is from reading the code (no reserve anywhere);
the "does not stop" part is the probe.

Who: any admin or single player with a small disk, by default. Likelihood: real for rented servers with 20-40 GB
disks and for laptops.

Fix: (1) AutoDownload and StartAll refuse when free space is under the remaining bytes plus a reserve (2 GB, say), log
why, and leave the world on "as needed"; (2) the batch checks the reserve before each write and stops with
`Error = "disk full"` on the first write that fails for lack of space; (3) a local write failure never falls through
to Copernicus; (4) the same reserve for squares fetched as the world needs them.

---

## Medium

### M1. A syntax error in earth.json replaces the admin's file with defaults

Where: `src\EarthConfigStore.cs:22-28` and `:110`; vanilla `APIBase.LoadModConfig` (decompiled `Vintagestory.Common\APIBase.cs:94-109`)
throws on any JSON or type error.

What happens: the catch logs one error line and goes on with `new EarthMapConfig()`; line 110 then writes those
defaults over the file. One missing comma or `"ElevationDownloads": "three"` loses every setting: `Enabled: false`
becomes true, a mirror or local `DataRepo`/`ElevationRepo` goes back to GitHub, an emptied `ElevationSource` is
Copernicus again. No copy is kept. `StoreModConfig` is `File.WriteAllText`, so a crash or full disk during the write
leaves a truncated file, which the next start then replaces the same way. Verified by reading both sides.

Fix: when the file exists and can't be parsed, copy it to `earth.json.bad-<time>`, do not write, and either stop with
the parser's line and column or run on defaults without storing. Write through a temporary file and rename.

### M2. Bodies are read without a size limit: the manifest before its hash check, and Copernicus GeoTIFFs

Where: `src\Core\DataTransport.cs:278-295` (GetAsync); callers `src\Core\DemDownloader.cs:383-385` (manifest, checked
after the whole body is in memory) and `:253` (GeoTIFF).

Probe (`out-http.txt`, localhost listener sending a body with no end as `manifest.json`):

```
http [mirror's manifest.json is an endless body]: fetch ended False after 3355 ms; server sent 1573 MB;
process working set peaked at 4541 MB
```

It ended only because the probe's server stopped at 1.5 GB. The default release's manifest is pinned
(`Glo90ManifestBytes` 2,597,215 and its SHA-256), but the pin is applied after the read. Who: whoever answers for the
release URL or a mirror (a proxy's error stream, a hijacked mirror). Likelihood low, cost a server out of memory.

Fix: `GetAsync(location, maxBytes)`: refuse a larger Content-Length, stop reading past the limit. Manifest: the pinned
size for the default release, 16 MB for a mirror. GeoTIFF: 64 MB (the largest GLO-90 tile is far below).

### M3. About 330 MB of start-up memory holds data a new world's rules never read

Measured with `ProbeLoad` on the tracked `earthdata` (`out-load2.txt`; workstation GC, live bytes after a full collection):

```
RiverData.Read(rivers.bin): 619723 reaches, 2205168 points   271 ms | live +123 MB
server: EarthData.Load with terrain                           752 ms | live +350 MB (LOH 210 MB) | allocated 685 MB
server: rivers.bin's reaches released                                  | live -135 MB
server: climate fix 4                                           6 ms | live  +50 MB
server: sea referee                                           793 ms | live +158 MB | allocated +540 MB
server: peaks.json (155318)                                   232 ms | live  +51 MB
server: terrain sampled every 2.9 degrees (tile cache full)  1743 ms | live  +95 MB
peak working set 1639 MB
```

- `src\Core\EarthDataset.cs:416`: `rivers.bin` is parsed at every server start (0.27 s, 123-135 MB, 620,000 lists).
  A world with `RiverCourses >= 1` (every new GLO-90 world, `EarthWorldOptions.cs:158`) builds from `rivers-glo90.bin`
  (`EarthParams.cs:770`, `RiverSourceRepository.cs:79-105`); nothing reads `Data.Rivers` then (only RiverAudit's
  fallback). The list is alive through the river network build, the start's memory peak, and released after it
  (`EarthMapSystem.cs:494`). Fix: read `rivers.bin` on first use (`RiverReaches()`), not in `Load`.
- `src\Core\TerrainOverlays.cs:86-87`: the referee makes its own islands and coasts (+158 MB live) while the as-read
  ones stay referenced by the dataset for the session. A world reads one of the two. Fix: let the dataset drop the
  as-read objects once the world's view is chosen (one world per server), or apply the referee while reading.
- `src\Core\EarthDataset.cs:78-104`: choosing fix 4 keeps the planes of fixes 2 and 3 as well (+50 MB in all, about
  two thirds of it intermediate), on the client too. Only `ClimatePlanes(3)[3]` is read once more (WeatherGround).
  Fix: build 4 without caching 2 and 3, keep the one precipitation plane WeatherGround needs.

### M4. The zip has 293,475 bytes (0.73 %) of headroom under 40,000,000

Where: `tools\package-earth.py:10,65-66`; `EarthMap.csproj:53-57`.

Measured: pinned tree 39,706,525 bytes. `C:\VS mods\Earth\release`: rc.8 39,735,982; rc.9 39,736,537; rc.10 39,749,030;
rc.11 39,865,025; rc.12 39,865,553; rc.13 39,989,244 (10,756 bytes left; built before the brotli step). In the newest
zip `earthdata` is 35,566,857 bytes compressed of 35,948,491 (89 % of the zip, incompressible), `assets/earth/config`
1.65 MB after packing, `assets/game/textures` 866,710 (310 PNG), `EarthMap.dll` 765,603, `EarthMap.pdb` 244,464.

Nothing can push it over silently: the packager refuses a larger zip and the build fails. What uses the headroom up:
a re-bake of `rain.bin` 2.5 % larger; about 24,000 more source lines (the DLL packs to about 12 bytes a line); one set
of textures; building with Visual Studio's msbuild.exe (no BrotliStream: +310 kB, the csproj only warns); a stray
file under `assets\` (the csproj takes `assets\**\*` minus five patterns, the packager only refuses store file names).

Cheapest levers: leave `EarthMap.pdb` out or embed it small (244 kB; see L1); `climatefix.bin` (324,584 bytes) is read
only by worlds of climate fix 1; `ore-heatmaps-v1.json` (371,523) is worth the same question. Fix besides: let
`PackageChecks` (EarthWorldOptionsTests) fail under a chosen reserve, e.g. 150 kB, so the limit is met before a release
day, and print the five largest entries.

### M5. Running the tests replaces the release zip of the current version

Where: `tests\Run-All.ps1:93` (`-p:OverwriteRelease=true`), `EarthMap.csproj:71,120`, `tools\package-earth.py:71-79`.

`Run-All.ps1` builds with the overwrite switch every time. In a checkout whose `modinfo.json` still names a version
that has a zip, the suite writes a new zip under that name (the old one goes to `release\.package-backups`). This is
the state now: `C:\VS mods\Earth\release\earth-2.0.0-rc.13.zip` is 39,989,244 bytes (SHA f54ad95b...), the pinned tree,
also "2.0.0-rc.13", builds 39,706,525 bytes (SHA 248534cb...). Running the suite in the main checkout swaps one for
the other. A zip that was handed to anyone then no longer matches its name.

Fix: a property that skips `PackageMod` (`-p:SkipPackage=true`) for the test build, or build the tests' DLL into the
run's output folder; keep `OverwriteRelease` for a person.

### M6. PlaytestReviewTests passes only in a worktree three folders below a `rawdata` link

Where: `tests\PlaytestReviewTests\Program.cs:25`, `LakeAudit.cs:7` (`root/../../../rawdata/...`), `tests\Run-All.ps1:49`
(needs declared as none).

Quick run on the pinned tree:

```
PlaytestReviewTests    FAIL (-532462766)
System.IO.DirectoryNotFoundException: ...\9657fcaf66b21d0e\rawdata\ne_lakes\ne_10m_lakes.dbf
```

From `C:\VS mods\Earth` the path resolves to `C:\rawdata` (not there), so the suite cannot pass in the main checkout
either, only in `C:\VS mods\Earth\.claude\worktrees\<name>`. `EARTH_RAW` is ignored. The test dies with an unhandled
exception, not a skip. Fix: take `{raw}` as an argument like ElevationTests, declare `N = @('raw')`, skip when it is
missing.

---

## Low

### L1. The published zip carries the private repository's URL and the build folder; its bytes depend on the folder

Where: `EarthMap.csproj:3-19` (no PathMap, SourceLink left on).

Read from the zips in `release\`:

```
earth-2.0.0-rc.13.zip EarthMap.dll debug path: C:\VS mods\Earth\.claude\worktrees\rc13\obj\Release\EarthMap.pdb
earth-2.0.0-rc.13.zip EarthMap.pdb source link: "documents":{"C:\\VS mods\\Earth\\.claude\\worktrees\\rc13\\*":
  "https://raw.githubusercontent.com/andrisberzins-1/VS-Earth-mod/f652abe6.../*"}
earth-1.0.2.zip       EarthMap.pdb source link: ".../andrisberzins-1/VS-Earth-mod/ec2ed7f4.../*"
```

The pinned build's PDB holds `<user folder>\AppData\...`. No secret, but the private account and repo name are in
every published zip, and a build from another folder gives another DLL and SHA, so the package report's hash can be
reproduced only in the same folder (same folder: verified equal). Fix: `<PathMap>$(MSBuildProjectDirectory)=/earth/</PathMap>`,
`<EnableSourceLink>false</EnableSourceLink>`, and decide on the PDB (line numbers in players' logs against 244 kB).

### L2. A mirror's manifest can ask for gigabyte buffers

Where: `src\Core\DemDownloader.cs:427-431` (length only checked against the manifest's own part size), `DataTransport.cs:258`.

Probe (`out-manifest.txt`): `length 1500000000, three squares at once: 4500 MB allocated, 209 ms`; length 2147483647
gives "Array dimensions exceeded supported range" (caught, logged). The default release's manifest is pinned, so this
needs `ElevationRepo` set to a mirror. Also `offset + len` can overflow (harmless: the read fails). Fix: refuse
`len > 8 MB` in ParseManifest (the largest real tile is under 3 MB).

### L3. The GeoTIFF reader allocates from the file's counts before checking them

Where: `src\Core\DemTiles.cs:82` (offset cast), `:116-121` (width, height, tile size), `:186-199` (Values, Doubles).

Probe (`out-tiff.txt`, 229 directory mutations of a real Copernicus tile): 105 InvalidDataException, 98 accepted,
14 InvalidDataException after more than 400 MB, 8 ArgumentOutOfRangeException, 6 OutOfMemoryException, 4
OverflowException, 2 after a 4.0 GB allocation. None of these escapes (FetchAsync logs and the square falls back for
the session), but a 4-16 GB allocation can end the process. Input: Copernicus's bucket over HTTPS, no hash exists.
Fix: `count * Size(type) <= f.Length - at` before each array; width, height, tile width and height at most 4096;
`across * down` in long.

### L4. A malformed repo URL ends the start with a raw exception

Where: `src\EarthGeology.cs:252-253` (filter lacks UriFormatException, InvalidOperationException), `src\Core\GroundDownloader.cs:86`,
`src\Core\DemDownloader.cs:166` and `EarthDataStore.cs:161` (lack InvalidOperationException).

Probe (`out-badsource.txt`):

```
GroundDownloader.Get(source "http://exa mple.invalid/"): THROWS UriFormatException
GroundDownloader.Get(source "http://"): THROWS InvalidOperationException: An invalid request URI was provided...
EarthDataStore.Ensure(source "http://"): THROWS InvalidOperationException
```

Who: an admin's typo in `GroundRepo`, `DataRepo`, `ElevationRepo`. The server stops either way; the message does not
name the setting. Fix: check each source once in EarthConfigStore (`Uri.TryCreate` for http(s), else a folder) and
say which setting is wrong.

### L5. `/earth story` gives every player the coordinates of all story locations

Where: `src\EarthCommands.cs:193-197` (`Privilege.chat`, set on purpose), `StoryPlacer.cs:482-499`.

On a multiplayer server any player lists the exact x, z of every story structure and the spawn. Vanilla keeps these
behind maps and `/wgen story` (controlserver). Fix: `Privilege.controlserver` (or `tp`), or a config switch.

### L6. The dialog's "start" packet lacks the check the command has

Where: `src\EarthElevationSystem.cs:571-582` against `src\EarthCommands.cs:215`.

The privilege is checked again on every packet (sound). But the command refuses in a world that does not read GLO-90,
and the packet does not: a player with controlserver starts the 8.6 GB download in a vanilla world through
`.earthelevation`. Fix: the same `Params.Enabled && FineElevation >= 1` test in OnRequest (and H1's space check).

### L7. A store that can't be written is reported as "the release can't be reached"; a missing local folder is retried for 21 s

Where: `src\Core\DemDownloader.cs:381-404` (the cache write is inside the fetch's try) and `:542`;
`src\Core\DataTransport.cs:225-226` (only FileNotFoundException is "not there").

Probe: with an unwritable tile folder, `download all` ended at once with `error 'the release can't be reached (...)'`.
`C:\no\such\folder` as a source: `Failed (21027 ms)` for each file asked. Fix: keep the parsed manifest when only its
copy can't be written, and say so; treat DirectoryNotFoundException as NotThereException.

### L8. A converted Copernicus tile is kept without checking GLO-90's dimensions

Where: `src\Core\DemDownloader.cs:256-263`, `src\Core\DemTiles.cs:224-229` (one degree and origin are checked, not
1200 rows and `Glo90Cols`).

`ElevationSource` pointed at another product (the 30 m bucket) stores tiles that `FineElevation.Load` then refuses
(`FineElevation.cs:433`), moves to `.bad` and fetches again: counted as done by "download all", unusable. Fix: the
same dimension test before `Write`.

### L9. The 30" ground sets can fail at first use

Where: `src\EarthGeology.cs:238-242` (a world from before GroundData is checked by size only), `src\Core\GroundColumns.cs:183-200`.

Probe (`out-ground.txt`): a set cut to a third after its headers were read: `75 tiles still read, 77 throw at first
column read; first: FormatException: ground tile ... is truncated`. On a generation thread this goes through
`EarthWorldGen.FailedPass`: the chunk is noted, the server stops, the chunk is made again (sound, but late). The
inflate is unbounded (`z.CopyTo`, `:194-198`) and `ReadAll` trusts the set's count (`:78-79`:
`OutOfMemoryException` for count int.MaxValue, which EarthGround.TryCreate's filter does not take). Reachable only
with unverified bytes: old worlds, or a `.egt` asset of another mod. Fix: hash on the old-world path too; read at most
`bodyBytes + 1`; refuse `count > (file.Length - 8) / 12`.

### L10. `/ew hail` runs up to 35,000 model samples on the main thread for a chat-privilege player

Where: `src\EarthWeather.cs:922-960`. Measured (`out-weathercmd.txt`): 5-26 ms with 9-day months, up to 63.5 ms with
30-day months (Riga); fog 3-43 ms, thunder and map under 6 ms. One tick's worth at worst, per command. Fix if wanted:
stop after a time budget, or search in day steps first.

### L11. 193 MB of downloads are not in the stated sizes

Where: `modinfo.json:6` and the README's sizes; `src\EarthElevationSystem.cs:196-209`; sizes in `LakeOutlines.cs:28`
(57.7 MB), `RiverOutlines.cs:47` (35.7 MB), `RiverFlow.cs:23` (93.8 MB), `DamRemoval.cs:61` (6.2 MB).

A new GLO-90 world fetches these four files before its first chunk, blocking, and holds them in memory. The
description names 290 MB and 170 MB. Fix: say about 650 MB before the tiles.

---

## Refactor

- R1. `src\Core\EarthBinary.cs:97-122`: `UnbrotliAll` lets BrotliStream's InvalidOperationException out, against its own
  summary ("an InvalidDataException naming it"); readers build arrays from header counts (`RiverNetwork.cs:44`,
  `Islands.cs:47,73`, lakes, shores, outlets). Probe (`out-readers2.txt`, 580 damaged inputs to the server files):
  231 clean InvalidDataException, 189 other types (InvalidOperationException about 150, IndexOutOfRange, Overflow,
  ArgumentOutOfRange, OutOfMemory 10; `rivers.bin` with version 0 or -1 allocated 5.5 GB), 160 accepted at load (lazy
  parts or unused header fields), no hang. All of these files are SHA-256-checked before they are parsed at every
  start, so this is reachable only from tools and tests. Wrap the decoder's exception; check `count <= decoded / minRecord`.
- R2. `src\Core\GroundColumns.cs:183-209`: each unpack of a lazy tile makes the packed bytes, a MemoryStream, its
  `ToArray` copy and the typed arrays (three times the tile on the large object heap); tiles are packed again under the
  192 MB budget and unpacked again as generation moves. Inflate straight into the typed arrays.
- R3. Temporary files of a killed process stay for good: `DemDownloader.cs:321` (`name.egl.<id>.part`),
  `FineElevation.cs:442` (`.bad`, never deleted after a good refetch), `EarthDataStore.cs:213` (`.copy`). Sweep those
  older than a day when the store is first used.
- R4. `src\Core\EarthDataset.cs:233-237`: `peaks.json` costs 51 MB live for 155,318 peaks (names included) and 0.23 s;
  the terrain needs position and height.
- R5. Tests, what has none. No test names any type of: `EarthCommands` (878 lines), `EarthElevationSystem` (920),
  `EarthConfigStore` (133), `WorldUpgrades` (431), `SelfTest` (1,459), `WhereReport`, `TerrainChunkWriter`,
  `RockFloorPass`, `RiverBlockPass`, `EarthSprings`, `OreBinder`, `OreSession`, `SnowMeltLight` and 19 smaller
  game-side files (7,538 lines in all); in Core: `DamRemoval` (227, it rewrites GLO-90 heights), `TiledGrid`,
  `TerrainTileCache`, `TerrainOverlays`, `WaterOverlays` (513), `RiverSourceRepository` (reached only through whole
  loads). The transport tests cover a stalled body, a cut connection, an ignored range and 404; not redirects, a body
  without end, write failures, a hostile manifest, the GeoTIFF directory, or earth.json. `PackageChecks` skips all zip
  checks without failing when no zip is built (`tests\EarthWorldOptionsTests\PackageChecks.cs:68-72`). I found no
  test that cannot fail (each has an exit code path); I did not audit their expectations one by one.

---

## Checked and found sound

Downloads and files
- Store files (38, 289 MB), ground sets, and the four release files: written as `.part`, size and SHA-256 checked,
  then renamed; hashed again at every start before any parser sees them (`EarthDataStore.cs:106-112`,
  `EarthGeology.cs:252`, `LakeOutlines.cs:54-62`). A same-size changed file counts as missing.
- Release tiles: SHA-256 from the manifest, magic, format and square checked before the write (`DemDownloader.cs:294-302`);
  the default manifest pinned by size and hash, its kept copy checked again.
- A tile on disk: own checksum; a damaged, empty or wrong-size file is refused, moved to `.bad` and fetched again.
  Probe: bit flip `tile data damaged`, zero length `not an Earth elevation tile`, 100 x 100 tile `not GLO-90's 800 x 1200`;
  192 single-byte header changes gave only InvalidDataException or a valid read.
- Path handling: every file name written is compiled in or made from integers. Manifest part names with a separator
  or `..` are refused (probe: manifest rejected). `C:secret` passes the test but only reads, from a local folder source.
- Schemes: only `http://` and `https://` are URLs; `file://`, `ftp://` are taken as folder paths and fail. A redirect
  to `file://` is not followed (probe: UriFormatException). A redirect to another local port is followed (probe: the
  inner listener saw the request): a mirror can make the server GET an internal address; the body is used only if its
  hash fits. For a setting only the admin can write this does not matter. HTTPS to HTTP redirects are not followed by
  .NET; no automatic decompression, so no gzip bomb.
- Two servers on one ModData: an exclusive `fetch.lock` per folder for whole files, with a second look after the wait;
  unique temporary names for tiles, the manifest copy and the model cache; rename over the target.
- ModelCache: SHA-256 over the file, key over its inputs, lengths checked against the rest of the file, atomic save.
- Decompression: tiles and grids inflate into buffers of the declared size (`TryDecompress`), tile cells capped at
  4096 x 4096; `UnbrotliAll` capped (1 GB default, far above need).
- Exceptions on generation threads: caught per pass, the chunk noted for deletion, the server stopped, the chunk made
  again at the next start (`EarthWorldGen.cs:163-195`); vanilla catches what is left (`ServerSystemSupplyChunks.cs:1396`).
- Disk full during the first-start fetch: the error names the disk and the bytes needed (`EarthMapSystem.cs:362`).
- Client files (11): every damaged variant gave InvalidDataException or JsonReaderException at load, or an error
  string with vanilla weather or the coarse grid kept (`out-readers.txt`, 390 inputs; `terrain.bin` 38 more: damaged tiles show when first sampled).
- Save data: lengths checked (`RockFloorPass`, datum, failed chunks); `SaveGame.StoreData` is a ConcurrentDictionary.
- Config numbers: checked in EarthConfigStore, `EarthParams.Fix`, `VerticalScale`, `EarthLayout`, `StoryPlacer`;
  `ElevationDownloads` clamped where used.

Commands and packets
- Channels: `earth` (server to client only, no server handler) and `earth-elevation` (privilege checked per packet;
  repo, source, free space and batch errors only to controlserver).
- Console: every subcommand that reads a position has RequiresPlayer; the others are null-safe (`Caller.Player?`).
- Input: `/earth tp` refuses NaN and clamps; radii clamped (orelayers 128, woods 128, census 256, relight 128,
  propick 64); map 16-4096 blocks a pixel with a 120 MB limit, one at a time, off the main thread; checkrivers one at
  a time, off the main thread; the water search for tp off the main thread and cancelled by the next.
- Non-Earth worlds: every `/earth` subcommand answers "not active" or its own "off" text, except `propick`, `woods`,
  `date` (harmless there) and `elevation` (status; see L6).
- World state: `siterules` (controlserver, chunks until the world is reopened, logged), `boattest` (blocks),
  `relight`, `date` (time privilege, forward only), `snowlight` and `woods trace` (reset at world start).
- Cost: `/earth info`, `where`: one column. `/earth site`: about 1,400 columns at 0.35-2.4 us each (EarthMapTests
  cost mode), a few ms; the main thread never waits for a tile (FetchAllowed off after RunGame).

Start-up and memory (measured parts)
- Client: load 185 ms, 53 MB live; with fix 4 and the three CHELSA files 130 MB; 250,000 climate samples over
  5 x 5 degrees 137 ms and +1 MB (`out-load2-client.txt`).
- Server, tracked `earthdata` only: 739 MB live with the terrain tile cache full (table in M3).
- SHA-256 of 16 store files (99 MB), in parallel, warm cache: 36 ms; all 654 MB hashed at a start come to about 0.3 s
  warm, disk-bound when cold.
- EarthMapTests (server GC, 256,000-wide world, built-in rivers): data 644 ms, 829,355 river segments indexed in
  3,973 ms, peak working set 2,053 MB.
- Not measured, from sizes: the included heights `glo90c-*` (129 MB of bytes held), the four release files (193 MB
  held), the rivers on GLO-90 and their network, ground sets (2' sets packed plus a 192 MB unpack budget), GLO-90
  tile cache (24 x 2.9 MB), column store (29 MB). My estimate for a 1:10 GLO-90 world is 1.4 to 1.8 GB of managed
  memory for Earth while it runs, more during the river build; a server start was not run.

Build and package
- The 40,000,000 limit is enforced by the packager; store files, data-pack dependencies, a bad version string and
  missing client data stop the build. Entries are sorted and dated 1980; same folder, same bytes (verified twice).

## Not reached

- A real server start (time, peak memory, first-start downloads against GitHub); no game was run, no real host
  contacted.
- The slow test projects other than EarthMapTests' cost mode (RegionalGeologyTests, SpawnDistributionTests, WeatherTests),
  the Python ore checks, `PackInstallerTests.ps1`, `run_selftest.sh`.
- Lazy tile decoders of the outline, coast and flow files under damage (only TiledGrid, terrain.bin and ground tiles
  were exercised after load); CoarseElevation's reader (its files are not in the tracked tree).
- JSON assets patched by other mods (catalogue loaders): read for their catch filters only, not probed.
- A hostile server's packets to the client beyond reading `OnParams` (it loads inside a try).
- SelfTest beyond its entry points: it runs only from `earth.json` (`SelfTest.Enabled`), has no command, and shuts
  the server down by default when it ends.

## Quick run, times

```
ColumnStoreTests 6 s | EarthWorldOptionsTests 8 (948 checks) | ElevationTests 35 | FloodplainTests 9 | GroundColumnsTests 5
GroundDownloadTests 60 | ModelCacheTests 2 | OreDepositsTests 4 | OreLayersTests 4 | PatchVanillaTests 5 | PlacementTests 2
PlaytestIssuesTests 11 | PlaytestReviewTests FAIL 7 | RegionalFaunaTests 24 | RegionalOresTests 3 | RegionalPlantsTests 2
RegionalTreesTests 10 | RequiredModsTests 2 | RiverIndexTests 11 | RiverWaterTests 2 | SiteDriversTests 2 | SkyLongitudeTests 2
SoilTests 3 | TerrainExpansionTests 18 | VerticalProfileTests 3 | VolcanoTests 31 | WaterTableTests 2 | WeatherContractTests 13
WetlandTests 4 | ZoneTests 9 | skipped as slow: EarthMapTests, RegionalGeologyTests, SpawnDistributionTests, WeatherTests
30 run, 1 failed, 4 skipped; about 5 minutes of test time
```

The build and test runs left `bin\`, `obj\`, `release\` and `tests\out\` (all git-ignored) in the pinned worktree.
