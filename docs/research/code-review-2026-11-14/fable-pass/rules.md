# Earth: saved worlds (rules versions, EarthParams, data pinning) — independent review

Tree: `earth-fable` worktree at `fef6166` (main, modinfo 2.0.0-rc.13). Read-only for source. Everything below was read
in the code or the git history, or measured with the probes in
`<scratch>\fable\probes-rules\`.
Paths are relative to the pinned tree. `docs\research\code-review-*` was not read.

## Result in short

No critical finding. The terrain model keeps old worlds bit for bit: I ran the same saved params through each
release's own `src\Core` and through `fef6166` and hashed every `TerrainModel.Column` field, the climate pixel and the
climate sample:

| World (params made by that version's own constants) | Columns | Result against `fef6166` |
|---|---|---|
| v1.0.2 default world (512000 x 204800, height 384, built-in heights) | 673,440 (35 places 128x128, 500 random 200-column runs) | identical: all 25 Column fields 1.0.2 has, the climate pixel, 6 climate sample fields (rc.13's code gives the same) |
| rc.8 (`761f681`) 1:100 world, GLO-90 tiles, lake outlines 2, river outlines 4, dams 3, flow 1, channel rules 3, GenFix 1 | 573,440 | identical |
| rc.11 (`3c72333`) same set-up, channel rules 5, works rules 4, GenFix 2 | 573,440 | identical |
| rc.13 (`6371e32`) same set-up, GenFix 3 | 573,440 | identical |

The weather model of rules 5, 6 and 7 is bit-identical to rc.13's (I built rc.13's `WeatherContractTests` and added the
anomaly and hail hashes: ALL, anomaly and hail equal the values pinned at `tests\WeatherContractTests\Program.cs:160-163`).
EarthParams' protobuf table (number, name, type) is unchanged from v1.0.0 through every RC to `fef6166` (22 revisions
compared), and no rules constant is lower at `fef6166` than in any release or RC (8 revisions compared).

Limits of what the probes cover: one thread, the default tab choices, the model only (not the chunk writer, surface,
geology, ore, plant and tree passes, which need the game), rc.1 to rc.7 worlds not run (their GLO-90 release files
`rivers-outline-glo90-1/2/3`, `dams-glo90-1/2`, `lakes-glo90-1` are not on this machine), and the rc.8 world was run
with `Works1318 = 0` (rc.8's code reads `works-1318.bin`, which the tree no longer has; the file's content was checked
apart, see "Sound").

Counts: critical 0, high 2, medium 4, low 7, refactor 3.

---

## High

### H1. A failed tile fetch while the game runs writes built-in heights into a GLO-90 world's chunks for good
Where: `src\EarthElevationSystem.cs:384-398` (`OnFetchFailed`), `:404-416` (`PrepareChunk` marks the square
`ForSession`), `:436` (`GenerationWait` two minutes once running); `src\Core\FineElevation.cs:190-200`.

Failure: every world on full GLO-90 (the default of every 2.0 play style). Once the game runs, a square whose tile is
not on the machine and cannot be fetched within two minutes "takes the built-in heights for this session"; the chunks
generated there are saved. At the next start the tile is fetched and the neighbouring chunks get GLO-90: a step in the
terrain, rivers and lakes that do not meet, for good. The code says so itself (`:396`: "may meet later ones with a
step"). Before the game runs the same failure stops the server (`:387-394`), so the rule "a failure that may pass must
not become the world's terrain" is applied at start-up and dropped afterwards. Likely cases: playing offline in a world
whose 8.6 GB download did not finish (the Earth tab's default starts that download when the world opens), a release or
network outage on a server, a stalled download slot. Also left open by `db3e4ee`'s message: plans built during such a
session are kept for the session.

Not reproduced in a running game: read in the code only.

Fix: do not generate from the wrong heights. Either note the chunk for the next start and throw from the terrain pass
(`NoteFailedChunk`, as `MakeTerrain` does, without stopping the server), or stop the server as before `RunGame`. A
config switch could keep today's behaviour for those who prefer play over consistency. No rules version: it only
removes a session-dependent result.

### H2. No test pins the terrain of an old-rules world; every "bit-identical" claim rests on a one-off probe
Where: `tests\` as a whole. What exists: `EarthWorldOptionsTests` (the params table, defaults, the refusal),
`WeatherContractTests` (rules 5-8 weather hashes), `RegionalGeologyTests` `golden-v1.json` (geology v1 on all-rules-0
models, rewritable with `GOLDEN_V1_REWRITE=1`), `TerrainExpansionTests` (a copy of 0.12.1's model), and
`SelfTest.Digest` (`src\EarthMapConfig.cs:265-276`, an in-game hash of generated chunks for comparing two builds by
hand; `tools\run_selftest.sh` is stale per AGENTS.md).

Failure: nothing in `tests\Run-All.ps1` fails when a commit changes `GetColumn` for a v1.0.x or RC world. The commits
since rc.13 that claim identity (`f6095e9` "408,914 columns ... every place's hash the same", `7d40059`, `db3e4ee`,
`0cb4431`) each used a probe that is not in the repository. The claims are true today (table above), but the next
unguarded change to a rules-0 branch would ship unnoticed. This is the largest hole in the protection of the contract.

Fix: add a `SavedWorldDigestTests` project: params files per published version (v1.0.2, and one per RC that has saved
worlds), a fixed place list, one pinned hash per `Column` field and for `ClimatePixel`. The built-in-heights world
needs only `earthdata\`; the GLO-90 worlds skip (not fail) without the store, as `Run-All.ps1` already does for
missing needs. The probe is ready to adapt: `probes-rules\digest-src\Program.cs`, `Glo90.cs`, `MakeV102.cs`,
`MakeRc13.cs`, with `params-v102.txt`, `params-rc8.txt`, `params-rc11.txt`, `params-rc13.txt` and the `hash-*.txt`
outputs. The chunk passes need the game: wire `SelfTest.Digest` into a repeatable script with stored digests per
params file. No rules version.

---

## Medium

### M1. Passes that used to log and go on now stop the server at the first failure, in old worlds too
Where: commit `222120a` (ER-08); `src\EarthWorldGen.cs:182-194` (`FailedPass`), `src\EarthGeology.cs:512-516`,
`src\EarthOres.cs:351-396`, `src\EarthOreGenerator.cs:474-481`.

Failure: any world, including every RC and 1.0.x world. Until rc.13 a throw in rock strata, ground, the ore layer
pass, the deposit filter, bauxite strata, coal measures or a deposit generator logged (at most three times a session) and
went on with that chunk as the pass had left it. Now the chunk is noted for deletion, the server stops, and the next start makes
the chunk again. A throw that is a function of the place (a deposit variant of another content mod, a data case)
repeats: the server stops every time a player comes near that chunk and the area cannot be entered. The world's
generation there also differs from what the same build line did before (vanilla blocks kept). Whether such a throw
exists in a real world is unknown; the refusal text itself names "a block or deposit of another content mod". There
is no setting to return to the old behaviour.

Fix: keep the refusal for the first failure, but when the same chunk fails again after a restart (the chunk is in
`earth-failed-chunks` and fails anew) fall back to the old "log and keep" for that pass, or add an earth.json switch.
Alternatively gate the refusal on `GenFixRules >= 4` so worlds made before it keep "log and go on". No rules version
for the retry rule; the gate uses the existing GenFix 4.

### M2. Data a saved world's rules read that is missing or unreadable is a log line, not a refusal
Where: `src\EarthMapSystem.cs:689-704` (`ReportDataMismatch`: sea referee, works 1318, non-perennial rivers),
`:915-923` (`ReportMissingClimateData`: only CHELSA temperatures and the climate fix; nothing for `rain.bin` with
`FineRain` or `aridity.bin` with `AridityRules`), `src\EarthParams.cs:707-719` (included heights and works silently
skipped when null), `:729-731` (GIRES file with another course key: the rainfall estimate, no line),
`src\EarthGeology.cs:141-167, 220, 330` ("regional geology / ground columns / ore layers are off for this session"),
`src\WorldGenRules.cs:52-66` ("Never throws: the world still generates, without that rule's data").

Failure: the files in the mod's own zip (`climatefix*.bin`, `temperature.bin`, `rain.bin`, `aridity.bin`,
`outlets.bin`, `reservoirs-v1.json`, `rivers-nonperennial-2.bin`, `works-1318-2.bin`, the config JSON) have no
compiled hash and no presence check for a saved world. A damaged or repacked zip, a failed brotli unpack of a packed
config asset (0081), or a rock block that no longer exists (a provider mod removed) lets the world go on with other
climate, coasts, rock or ground in new chunks, with one error or warning in the log. The store files and the GLO-90
release files are treated the opposite way (hash-checked, the server stops), and so is `peaks.json`
(`src\EarthParams.cs:640`). Unlikely with an intact zip, but the outcome is the one the contract forbids, and ER-08
already decided that a failing pass must refuse.

Fix: in `InitWorld`, for a saved world, refuse when a rule above 0 lacks its data (one `DataProblem`-style check per
rule), and compile size and SHA-256 of the in-mod data files into the build as `EarthDataStore.Files` does (checked
by `PackageChecks`). No rules version.

### M3. GLO-90 tiles are pinned only when they come from the release
Where: `src\Core\DemDownloader.cs:188-190` (a tile file that exists is taken as it is: `FineElevation.IsHere` is
`File.Exists`, `src\Core\FineElevation.cs:161`), `:242-262` (`FetchFromCopernicus`: converted and read back, never
compared with egl-v1), `:296` (the per-tile SHA-256 check exists only for the release's parts).

Failure: worlds on full GLO-90 on a machine that takes tiles from Copernicus (the Earth tab's "copernicus" choice, or
the fallback when the release does not deliver a square) or that kept tiles converted before egl-v1 (the folder is
shared by every world and version). Where the bucket's file for a square is not the one egl-v1 was converted from (a
re-issued tile; I did not compare any), the same world gets other heights on that machine than on one with the
release's tiles, or on the same machine after the tile folder is emptied. No check would notice it.

Fix: after converting a Copernicus tile, compare it with the manifest's entry for the square (the encoded file's
SHA-256 if the encoding is reproducible, else a heights checksum added to a new manifest file name); a mismatch is
said and the release's tile preferred. Optionally verify tiles already on disk against the manifest once per start or
on first read. No rules version; a heights checksum list would be a new data file under a new name.

### M4. Worlds of v1.0.0-1.0.2 do not keep their weather
Where: v1.0.2 `src\EarthWeather.cs:38` (`RulesVersion = 2`); commits `2b0e832` (rules 4), `d9eb206` (rules 5),
`a37fc76` (`earthdata\weather.bin` changed in place, 3,607,117 to 3,634,099 bytes), `318022f` (portable maths, up to
1.5e-13); `src\EarthParams.cs:518` (`WeatherModelRulesOrOld`: 0 means rules 5).

Failure: weather rules became a per-world number only with rules 6 (rc.10). A 1.0.x world was made and played with
rules 2; opened with any 2.0 build it runs rules 5 (wet and dry spells, rain hours from airports) on a weather.bin with
other December precipitation. `WeatherDataMade` (rc.3) is null in those worlds, so nothing is said. rc.1 to rc.9 worlds
moved by at most 1.5e-13 in rc.10 (`318022f`, stated in its message); the pin's comment "rules 5 and 6: 2.0.0-rc.9 to
rc.12" (`tests\WeatherContractTests\Program.cs:152`) is exact only from rc.10. Weather is not stored in chunks, so no
seam; but "the weather" is named in the contract and this is the one verified change to published worlds without a
gate.

Fix: a decision, not code: record in 0002 or 0017 that weather before rules 6 was never frozen and that 1.0.x worlds
get rules 5 (restoring rules 2 would mean bringing back deleted code and the old weather.bin under another name).
Correct the pin's comment. No rules version.

---

## Low

### L1. Rules 7 fog moved by an ulp without a gate, and is unpinned since
Where: commit `5956d71`, `src\Core\WeatherModel.cs:707` (`NightHours` through PortableMath);
`tests\WeatherContractTests\Program.cs:150-153` ("The fog of rules 7 is not pinned").
rc.13 worlds (rules 7): drawn fog differs in the last bit; drawn only, same on both sides of one build. Fix: pin the
fog hash of rules 7 and 8 now that it is portable. No rules version.

### L2. The refusal of newer worlds covers int fields only
Where: `src\EarthParams.cs:438-514`; `src\Core\GroundColumns.cs:486` (an unknown `GroundDepth` reads as the default
curve), `Canals`, `StreamMode`, `FaunaRangeProfile`.
A later build that adds a ground depth curve, a canal code or a stream mode without also raising an int rules field
would be opened by this build with another curve or without the canal, silently. Fix: refuse a saved world whose
`GroundDepth`, `StreamMode` or canal codes this build does not know. No rules version.

### L3. The refusal protects only from this build on
Where: commit `6354bbd`; rc.13 and older have no such check (`AnnounceSavedRules` warnings and clamps).
A world made or adopted by 2.0 final and opened with rc.13 or older still generates the older build's chunks for good.
Cannot be fixed in old builds; say it in the release notes ("do not open with an RC"). No rules version.

### L4. `/earth siterules` changes generation of a saved world for the session
Where: `src\EarthCommands.cs:113-131` (controlserver; a warning in the log and the reply).
Chunks made while it differs from the save keep the other rules. Fix: keep it out of release builds or behind the
self-test switch. No rules version.

### L5. `EarthWorldOptionsTests` does not exercise what a real new world gets
Where: `tests\EarthWorldOptionsTests\Program.cs:348-353` calls `Freeze` with `data: null`, so `RiverCourses`,
`DamRules`, `RiverOutlines`, `FlowRules`, `CoarseElevation` are 0 and `ComputeParams` (game-bound) is not run at all.
I checked the constants by hand: every value a new world can get is at or below `RulesAgainstBuild`'s newest
(`RiverOutlines` 4 = `RiverOutlineData.FileVersion`, `DamRules` 3, `RiverCourses` 4, `SeaReferee` 2, `ClimateData` 4).
Fix: run the check on params built with the repository's data (as `probes-rules\digest-src\MakeRc13.cs` does).

### L6. Two of the protecting tests do not run as documented
`tests\PlaytestReviewTests\Program.cs:26` reads `root\..\..\..\rawdata\ne_lakes`: it runs only from a worktree three
folders below the repository (from the repository root it looks in `C:\rawdata`). `tests\EarthWorldOptionsTests`
needs `-p:VintageStoryPath=...` (its csproj defaults to `.local\vs`); AGENTS.md's one-project line omits it
(`Run-All.ps1` passes it). Fix: take the raw folder from `EARTH_RAW` or an argument; say the property in AGENTS.md.

### L7. rc.7 changed two data files in place (history only)
`earthdata\works-1318.bin` went from version 1 to 2 under one name (`5b07374`, `a60929c`) and
`rivers-nonperennial-1.bin` was replaced by `-2` under `PerennialRules` 1 (`0fb3106`, `6a72c33`), all between the two
commits that set modinfo to rc.7 (`908c1db`, `f2e5dcd`). Worlds of an rc.7 build made before `6a72c33` would read
other GIRES classes now; the installed rc.7 (backup folder dated 12 November 18:45) is after it. Nothing to fix; the
naming rule in AGENTS.md ("a changed file gets a new name") has been kept since.

---

## Refactor

- R1. Dead "newer than this build" branches now that such worlds are refused first (`src\EarthMapSystem.cs:447-454`):
  `AnnounceSavedRules` `:669-678`, `ReportDataMismatch` `:699-700`, the `Math.Min` clamps in
  `src\EarthParams.cs:690, 703, 704, 710, 718` and `:518`, `src\EarthElevationSystem.cs:106-107, 256-257, 278-279,
  312-313, 340-341`, `src\WorldUpgrades.cs:345-349`, `OreLayers.Supported`, `RiverOutlineData.Usable`. Their comments
  still describe the old behaviour.
- R2. `PlaytestReviewTests` repeats the unique-number check of `EarthWorldOptionsTests` through protobuf stubs
  (`tests\PlaytestReviewTests\Program.cs:354-357`); one place is enough.
- R3. `EarthParams.cs:11-12` lists the taken numbers by hand beside the pinned table in the test; derive the comment
  from the table or drop it.

---

## Checked and found sound

1. Commits since rc.13 (`6371e32..fef6166`, the non-merge commits that touch `src`: generation code read by diff, the
   later weather and start-up commits by their diff stat, their final code and the measured hashes):
   - `f6095e9` ApplyMouth's early return: `AnyBucketNear` reads the same bucket range as `MaxNear`; with no piece
     there `MaxNear` returns `own`, `want` stays `own`, the top is the same (`src\Core\TerrainModel.Rivers.cs:307-357`,
     `src\Core\RiverNetwork.cs:1006-1045`). The four ParallelWork conversions write each item's own slot. Confirmed by
     the rc.13 digest.
   - `7d40059`, `db3e4ee`, `29f3a30`: they remove thread- and session-dependent answers; the generation-first answer
     is unchanged (rc.13 digest, one thread).
   - `fe8cb7a` GenFix 4: both parts gated (`src\Core\VolcanoInterior.cs:120, 438-439`, `src\EarthGeology.cs:902-907`);
     an old world's 512-long read is kept through `AsSpan(0, 512)`.
   - `557e030`, `1116818`, `b3a8d57` site rules: gated on `model.SiteRules >= 1`; with 0 the size factor is 1.
   - `00c6716`, `c37f45e`, `7c5db07`, `54ad2bc`, `f793ef7`, `5956d71`: rules 8 and 9 behind `WeatherRules`; rules 5-7
     hashes equal rc.13's (measured). `9783aa0`, `0cb4431`, `a9c4682`, `e6cb26e`, `e194f79`, `5e841a0`, `c2da1ba`,
     `e89e378`, `60efda3`, `0fc8342`: no change to what a chunk gets.
2. EarthParams: the table is append-only over 22 revisions (`probes-rules\proto_table.py`); absent fields read as
   "before" (`MinSeaDepth` 0 is 5, `RiverRelief` 0 is 1, `StreamMode` null is none, `GroundDepth` null is log4000,
   `WeatherModelRules` 0 is 5, `SummitMinElevation` 0 keeps all peaks). No published or RC world can be refused: no
   constant went down (`probes-rules\consts.py`). Fields written after creation: the record fields and signatures,
   `GroundData` and `DepositSet` (first start only), the adopted tree, fauna, ore and placement numbers (decision
   0002's exception), `IncludeExtinctAnimals`, the weather choices and `ExtendTemperature` only in worlds made before
   they were frozen. One-shots skip a world created in the same start. A refused world is not written (nothing is
   saved before the game runs). The client gets the whole record and builds its climate from the same fields; the
   weather handshake (`WeatherRules`, `WeatherDataSignature`) covers a build with other rules.
3. Data: the 38 store files, the three 30" ground sets and the GLO-90 release files (lake outlines 1-2, river
   outlines 1-4, dams 1-3, flow 1) each have their own name per version with compiled size and SHA-256, and a world
   that generated with one stops when it is missing (`src\EarthElevationSystem.cs:194-237`). The repository's
   `earthdata` copies match the store's hashes (16 files). Against the built zips of rc.8, rc.10, rc.12 and rc.13 the
   only changed assets are `ground-rules-v1.json` (the new region is behind `"rules": [2, 1000]`, commit `be5e7b3`)
   and a comment in `zones.json`. `works-1318-2.bin` holds version 1's 692 areas and version 2's 2,383 areas
   unchanged and in order once the `Canal` and `Late` flags are skipped (`probes-rules\worksprobe`). `GroundData` 0
   (ground from whatever sets the machine has) exists only in worlds made before 7 November, older than rc.1.
4. Model cache: the key holds the DLL's module id, the mod version, the in-mod data's identity (zip CRCs), the
   runtime, whether the dam and flow files went in, and the params without the fields set after the build
   (`src\EarthMapSystem.cs:790-823`). The dam and flow files are fixed by build and params (hash-checked), the cached
   steps run before GLO-90 and the story zones are attached, and the file checks itself. I found no way to meet a
   stale file.
5. Tests run on the pinned tree: EarthWorldOptionsTests 948 checks passed (with `-p:VintageStoryPath`),
   WeatherContractTests ALL OK, ModelCacheTests 133 passed, PlaytestReviewTests 3,519 checks with 0 failures (run
   through a scratch folder so that its raw-data path resolves), RegionalGeologyTests PASS 172 checks in 275 s (the
   geology v1 golden file among them). The pinned worktree is unchanged (`git status` clean); the two scratch
   junctions used for PlaytestReviewTests were removed again.
