# Required water provider verification

31 October 2026, Earth **0.17.0-pre.13**, based on `45efac9` in `codex/earth-playstyle-2026-10-31`. [Decision 0038](decisions/0038-required-water-providers.md) records the contract and rationale. No player profile was used or modified; all server worlds are disposable fixtures.

## Build and offline checks

- Release build against installed Vintage Story **1.22.7**, .NET **10.0.8**: **0 warnings, 0 errors**.
- `tests/RequiredModsTests`: **35 checks passed** against the production Earth DLL. Covers missing/old/invalid version strings, missing/incorrect APIs despite acceptable metadata, compatible additional overloads, server-only Hydrology, client Sea, saved/new/inactive eligibility and shutdown before the throwing loader callback.
- `tests/EarthWorldOptionsTests`: **167 checks passed**, including creation choices, saved settings, activation and Standard defaults.
- Read-only side-session review of the production guard changes: no actionable findings. No graphical-client test was run.

## Dedicated-server matrix

Server: Vintage Story **1.22.7**, loopback only, no advertising/UPnP. Valid providers: Cylinder World **0.11.15-playtest.6**, Hydrology **0.1.13**, Sea **0.4.7-pre.12**. Test worlds use width 256000, length 102400, height 512, seed 386217, Earth weather and rivers enabled. Story relocation is disabled for this focused startup test.

The reproducible driver is `tools/run-required-mods-server-test.ps1`; the observer is `tests/RequiredModsServerProbe`. Evidence is under `testruns/requiredmods-20261031-c/<case>/Logs/server-main.log` and `stdout.log`. Machine-readable expectations are in `tests/out/requiredmods/server-results.json`.

| Case | Observed result |
| --- | --- |
| `valid` | Earth reaches RunGame, river layout 10/query 2; Hydrology reports the query bound; Earth weather reports Sea link `provider`; Sea names its wind provider `Earth`. |
| `incompatible-hydrology` | A test-only copy of Hydrology 0.1.9 advertises 0.1.10 to bypass the native version check. Its actual query capability 1 is rejected; server stops before Earth parameters or the datum are written. |
| `incompatible-sea` | A test-only content package advertises Sea 0.4.0 without a SeaSystem/API. Earth rejects it and stops before Earth parameters or the datum are written. |
| `saved-incompatible-hydrology` | A copy of the valid test save is reopened with incompatible Hydrology and `Enabled=false` in Earth's config. The saved enabled Earth still enforces its dependencies; startup stops and the Earth parameter hash/datum remain unchanged. |
| `standard-incompatible-hydrology` | A new Standard world with the metadata-compatible but API-old Hydrology loads; Earth remains disabled with layout/query 0 and no Earth datum. Other mods retain their own behaviour. |
| `missing-hydrology` | Native loader reports `hydrology@0.1.10 - Missing` and excludes Earth. The game then errors resolving the unavailable `earthmap` play style before any Earth callback runs. |
| `missing-sea` | Native loader reports `sea@0.4.0 - Missing` and excludes Earth, followed by the same native play-style error. |

The original Hydrology/Sea archives were not modified; deliberately incompatible fixtures exist only under the test session's `shared` folder. The test observer records the state and shuts down a successful server after three seconds at RunGame.

The saved-world Earth parameter SHA-256 before and during shutdown is `2020C2F7B345802D0E05C1239E7306EA1B19539C9923853D3B451AA8FBFF54EA`, with datum `6E-00-00-00` (110). This checks Earth's saved terrain state, not byte identity of the complete database: graceful shutdown can save normal game metadata.

## Native-loader boundary

Manifest errors can exclude all Earth systems. Earth cannot intercept missing-dependency handling after the engine excludes its code. The new runtime checks protect Earth initialization when metadata permits the mod to load but the required provider contract is unavailable. The wholly missing-archive cases establish dependency rejection and the observed **game 1.22.7** play-style-loading error, not a successful Earth-issued error dialog or a guarantee for every existing-world load path.

## Reproduction

Build the candidate and test observer, then run each case with a fresh session under this checkout's `testruns`:

```powershell
dotnet build EarthMap.csproj -c Release -p:VintageStoryPath='C:/Users/andri/AppData/Roaming/Vintagestory'
dotnet run --project tests/RequiredModsTests -c Release -- .
dotnet run --project tests/EarthWorldOptionsTests -c Release -- .
dotnet build tests/RequiredModsServerProbe -c Release
powershell -NoProfile -File tools/run-required-mods-server-test.ps1 -Case valid -SessionRoot '<checkout>/testruns/new-session'
```

The driver accepts explicit game/provider paths and ports. Saved-world refusal uses `-Case saved-incompatible-hydrology -SavedWorld '<session>/valid/Saves/acceptance.vcdbs'`. Case directories are never reused or deleted automatically. Further cases are the row names above. Existing release ZIPs require a new version or an explicit overwrite of an unpublished build; the pre.12 ZIP remains preserved.

## Package identity

Final development package: `release/earthmap-0.17.0-pre.13.zip`, SHA-256 `0efd9b284eee2820f401967a087d568f7cea0b18488eca8bab9a7d83fb2f7141`. Its manifest agrees with the source and declares the two required minimum versions. Every archive entry is byte-identical to the pre.13 package exercised by the seven server cases (ZIP container timestamps may differ). The pre.12 archive remains byte-identical at SHA-256 `d2c50bc91e202f9e447bc7e2e44befb10f4a257badd91bde13db61d839b15d1a`. No package was installed or published.
