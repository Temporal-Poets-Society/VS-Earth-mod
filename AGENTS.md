# Earth (`earth`)

Earth makes a new world the real Earth: heights, coasts, rivers and lakes, climate and weather, rocks, soil, ores,
plants and animals as around 1318, on Cylinder World's ring at 1:200 to 1:5. Suite rules: `C:\VS mods\AGENTS.md`.
Current state: the card `C:\VS mods\overview\mods\earth.md`. Open issues: `tools\Update-Backlog.ps1 -Mod earth` from
`C:\VS mods` (prefix EA). Decisions: `docs\decisions\` (index `docs\decisions\README.md`, not in number order).

## Repository
- Trunk `main`, tracking the private `origin` (`andrisberzins-1/VS-Earth-mod`); no public code repo. Published
  versions are tags (`v1.0.0`-`v1.0.2`, and the test build `v2.0.0-rc.12`); old lines are `archive/*` tags. The version is in `modinfo.json`.
- Earth's data is published separately in Temporal-Poets-Society: `earth-data` (release `ed-v1`), `earth-glo90`
  (`egl-v1`, 8.6 GB of heights) and `earth-ground` (`egts-v1`); the local repos are in `C:\VS mods\data\`.

## Build
- `dotnet build EarthMap.csproj -c Release` gives `release\earth-<modinfo version>.zip`, packed by
  `tools\package-earth.py` with `python` from PATH; it must stay under 40,000,000 bytes (Mod DB limit). Before that,
  `PackJsonAssets` brotli-packs every `assets/earth/config` JSON of 40 kB and more (not the ore heat maps) to
  `name.json.br`; `src\PackedAssets.cs` unpacks them when the game loads assets (decision 0081).
- An existing zip of that version stops the build: bump the version, or pass `-p:OverwriteRelease=true` for an
  unpublished rebuild. A Release build empties the output folder first.
- On a work branch the zip still gets the release's name: copy it into a test version (rewrite `version` in a
  copy of the zip's modinfo, e.g. `2.0.0-rc.8-<topic>.1`) and delete the mislabelled zip.
- Game path: `-p:VintageStoryPath`, else `VINTAGE_STORY`, else `%APPDATA%\Vintagestory`. No compile-time
  references to other mods; Underground Cities and Earth Ruins reference `EarthMap.csproj` by path.

## Test
- Offline, all at once: `powershell -File tests\Run-All.ps1` (builds once, runs every `tests\*Tests` project with its
  arguments, logs and a summary in `C:\VS mods\work\earth-tests-<time>`; `-Quick` leaves out the four slow ones, about
  4 minutes; `-Only A,B`; `-Python` adds the ore checks). It packs the zip into its own output, never over `release\`.
  A new test project needs its line in the script's table, and one that reads `src\Core` references
  `tests\Earth.Core.Tests.Common` (Core compiled once) and gets an `InternalsVisibleTo` line there.
- One project: `dotnet run -c Release --project tests\<Name> -- "<repo root>"` for each `tests\*Tests` project (the
  ones that compile against the game find it as the mod does: `-p:VintageStoryPath`, `VINTAGE_STORY`, `.local\vs`, else
  `%APPDATA%\Vintagestory`)
  (EarthMapTests: `"<root>" 384` plus modes such as `audit`, `mouths`, `coasts`; RiverIndexTests takes only a
  report path). Build `EarthMap.csproj` first (RequiredModsTests links the DLL). All of them take about 35 minutes
  and pass (13 November 2026).
- Python: always `.venv\Scripts\python.exe -I` (plain `python` lacks brotli and other packages), for
  `tests\RegionalOresTests\*.py` (`check_artifacts.py` reads `C:\VS mods\data\earth-research`, or `EARTH_RESEARCH`
  in a worktree) and for every bake in `tools\`. Also `tests\PackInstallerTests.ps1` and
  `tools\run-earth-playstyle-tests.ps1`.
- Server: `tools/run_selftest.sh [--size WxL] [--height N] [--places "a;b"]` in Git Bash (data in `testserver\`).
  Stale defaults: it takes `..\Cworld\release\cylinderworld-<Cworld's version>.zip` (not built at the moment) and no
  Hydrology, which Earth requires. Pass `CW_ZIP=<zip>` and `EXTRA_MODS="<hydrology zip>"`.
- In game: the AgentBridge bench with the Earth zip plus `cylinderworld-*.zip` and `hydrology-*.zip` (from the
  player's `VintagestoryData\Mods`), and a play style langCode: `client.ps1 -PlayStyle preset-earth` (1:100) or
  `preset-earth-200`, `-50`, `-25`, `-10`. The bench's `ModData\earth` store is complete; a fresh data folder starts
  the 8.6 GB GLO-90 download. A new world can need `game.ps1 wait 420`. After `/earth tp lat lon`, run
  `game.ps1 arrive =X Y =Z` from its reply before `/earth geology` and similar commands.
- Generation is not repeatable run to run (vanilla ruins, bamboo, water plants): compare terrain pass by pass.

## Release
- Bump `version` (and `dependencies` when a minimum changes) in `modinfo.json`, the README's title line and the
  version paragraph at the top of `README.md`, and the decisions' status lines. Build, check, commit, tag `vX.Y.Z`
  on `main`.
  Pin the release's terrain for its saved worlds (EA-0103): `dotnet run -c Release --project tests\SavedWorldDigestTests
  -- <root> --make <version>` (with `-p:VintageStoryPath`), check the params against a real new world's, then
  `--write <version>`, and commit both files. The hashes are taken on Windows x64 with .NET 10 (Math.Log/Exp/Pow may
  differ in their last bits elsewhere); without the data store its GLO-90 worlds are skipped (exit 3, a partial pass). The Mod DB upload and any install or push wait for the author's word.
- Release check (EA-0191, uses the network, so not in `Run-All.ps1`): `dotnet run -c Release --project
  tests\DataReleaseCheck` asks ed-v1, egts-v1 and egl-v1 for every data file this build pins and fails (exit 1) on one
  that is missing or has another size. A pinned file its release lacks makes every new world that needs it stop at
  creation (decision 0103), so it must pass before a release goes out.
- Human works undone (decision 0064; the author, 21 November 2026): every change to Earth's data that undoes a human work
  after 1318, or leaves one on purpose, gets its entry in `tools/works_ledger.py`, and `docs/works-1318-ledger.md` is written
  again by it, in the same commit. `tests/DataReadersTests` checks the page's facts (the files, their areas and levels,
  the rules versions) against the data and the code and fails until they match.
- Data: new or changed data files go up under a new file name (or a new tag), each upload only with the author's
  yes; release assets are never replaced or deleted. A file baked again gets a new name even when the earlier bake
  was never uploaded: copies of the earlier bake live on in stores (benches, testers), and a store's file of a pinned
  name that isn't the pinned bytes stops new worlds until the release has the right one (EA-0191). The data-store
  file list is compiled into `src\Core\EarthDataStore.cs`; credit every source in `ATTRIBUTION` and the data repo's
  README.

## Gotchas
- Mixed line endings: about 50 C# files (e.g. `src\Core\TerrainModel.cs`) are CRLF in Git. Git Bash `sed -i`
  rewrites them as LF; check `tr -cd '\r' < FILE | wc -c` and edit with the Edit tool or byte-level Python.
- `rawdata` is a directory link to `C:\VS mods\data\earth-raw` (87 GB): never delete through it. A worktree has
  no `rawdata`: set `EARTH_RAW` (but `bake_earth.RAW` ignores it; patch its path constants when importing it).
- Saves: key `earth-params` freezes each world's choices and rules versions (decision 0002). Never renumber or
  reuse its ProtoMember fields (e.g. 192 GroundData, 200 GenFixRules); change behaviour through a new rules
  version. Earth 2.0 is for new worlds: saves before 2.0.0 (0.x, 1.x, the release candidates) are not supported (the
  author, 15 November 2026; removing their rules versions is EA-0119, after the release set), so don't build upgrades
  for them. From 2.0.0 on, saves are kept working.
- Other mods read about 60 of Earth's members by reflection (Hydrology's river query version 3, Cylinder World's polar
  layout, World Map's terrain, rivers, barometer and ore heat maps): the list is `docs/REFLECTION-SURFACE.md`, and
  `tests/ReflectionContractTests` checks each the way its mod looks it up. Keep their names, kinds (field or property)
  and signatures; no optional parameter or new overload on a method bound by its exact types or its bare name.
- `testserver\serverconfig.json` (git-ignored) is the template for Cylinder World's and Sea's server checks.
- Worktree paths can pass 260 characters: remove one with
  `cmd /c rmdir /s /q "\\?\C:\VS mods\Earth\.claude\worktrees\<name>"` (a literal path, no variables).
