# 0039: Minimum elevation for listed summit correction

Date: 31 October 2026. Status: built in 0.17.0-pre.14; in Earth 1.0.0 and 2.0.0. Not inspected in game; land heights have since changed with GLO-90 (0051, 0053).

## User direction

The author reported conspicuous small hills near 35.3932 S, 149.1185 E (Canberra) and asked to inspect the existing rules first. Installed pre.12 contains 155,318 GeoNames peaks, with no minimum elevation. Local examples are Mount Wanniassa (890 m), Mount Taylor (856 m) and Tuggeranong Hill (855 m). The author wants recognizable mountains with substantial bases preserved, including Fuji, then selected a concrete control: correction only from 3 km by default, configurable in Earth's customization screen, starting with Off, 1 km and subsequent height steps.

## Implementation

- Earth tab dropdown **Summit height correction**, key `earthSummitCorrection`: `off`, `1000`, `2000`, `3000`, `4000`, `5000`, `6000`, `7000`, `8000`. Default `3000`; labels use metres above sea level and include the threshold itself.
- This controls the surveyed/listed-peak correction from decision 0032. The finer ETOPO 30-arcsecond residual from decision 0031 remains independent, including with this option Off. It does not select landmarks by fame or implement a new measured-base-width rule.
- `EarthWorldOptions.Freeze` saves the choice in `EarthParams.SummitMinElevation`, protobuf field 89. `BuildModel` applies it on both sides, including companion terrain prediction. No additional companion API is needed.
- `KnownSummits.Restore` bypasses listed corrections when Off, filters peaks below the chosen minimum and omits those records from the neighbour-spacing radius limit. The correction's shape and bounds remain the existing rules. The former public `Restore` overload remains available with its prior all-peaks behaviour.
- The saved field is `-1` for Off, a positive minimum in metres, or `0` for older saves whose field is absent. Existing saves retain all previously eligible peaks, independent of the menu's new default. Creation-only parsing is never run for them. Existing mountain-upgrade behaviour does not replace this saved cutoff.
- Invalid/missing new-world values use 3000 m. Existing finer terrain data and the catalogue fingerprint contract are unchanged. No data rebake, download or player-save edit.

## Verification

`dotnet run --project tests/EarthWorldOptionsTests -c Release -- .` passes **473 checks**. These cover native game-API deserialization of the menu, every label and value, 3000 m default, invalid values, real protobuf round trips including Off and absent legacy field, model construction, exact cutoff inclusion, excluded-neighbour spacing and the original Restore overload for rules 2/3.

Production catalogue/terrain regression, seed 386217, height 512, sea 110, log1000, ring 409600, no river or coast overlays in the fixture:

| Site | Legacy all-peaks elevation | Correction Off / default 3000 m |
| --- | ---: | ---: |
| Mount Wanniassa | 890.0 m | 715.3 m |
| Mount Taylor | 856.0 m | 643.6 m |
| Tuggeranong Hill | 855.0 m | 719.7 m |
| Fuji | 3776.0 m | Off: 3539.1 m; default: 3776.0 m |

The three hills and a 25-by-25-block neighbourhood sampled every three blocks match correction Off exactly at both 1000 and 3000 m cutoffs. Fuji switches to Off's elevation at a 4000 m cutoff. Everest still reaches 8849 m at an 8000 m cutoff. These are model predictions from a test fixture, not measurements of the author's save or final generated chunks.

Release build against installed Vintage Story assemblies: zero warnings/errors. Package: `release/earthmap-0.17.0-pre.14.zip`, based on the existing pre.13 provider-requirement candidate in `codex/earth-playstyle-2026-10-31`. Pre.12 and pre.13 archives are preserved. The initial build step did not install a package or open a player save; installation at the author's later request is recorded below. Menu appearance and new-world terrain still need client inspection.

Initial package SHA-256: `d73fda071e6713fe7ff117052fdbbfa5e32966515370088161a1cde8c34bc1b0`. Compared with pre.13, only the DLL, its PDB, manifest, world configuration and English world-configuration labels differed; all 38 packaged files matched the build output, and the three configuration files matched source. The catalogue and other terrain data are unchanged. Pre.12/pre.13 hashes still match their existing verification record. The shared mod inventory was refreshed; its root-main snapshot remains distinct from this worktree package.

## Installation follow-up, 31 October 2026

The author explicitly requested: "Install the updated Earth mod so I can test it." The concurrently installed Earth+ creative-tab change (suite D42) had added two source/installed assets after the initial pre.14 build. The initial pre.14 ZIP is preserved in `.local/package-backups/earthmap-0.17.0-pre.14-before-earthplus-20261031-185758.zip`; the installable pre.14 ZIP adopts only `assets/earthmap/blocktypes/bogpeat.json` and `assets/earthmap/lang/en.json` from the current source. Binary comparison confirms all other entries, including the tested DLL/PDB and summit configuration, remain byte-identical to the initial pre.14 package.

Installed at **18:58:39 Europe/Riga** to `C:/Users/andri/AppData/Roaming/VintagestoryData/Mods/earthmap-0.17.0-pre.14.zip`. Final source/installed SHA-256: `c70064522028fe9884727c540d220e97569ba68b3fec8455b6df26ec02ede77e`. Only pre.14 remains installed. Cylinder World 0.11.15-playtest.7, Hydrology 0.1.13 and Sea 0.4.7-pre.12 satisfy its manifest minima.

The prior installed pre.12 Earth+ asset revision (SHA-256 `9f0a75b6ae2b7e772e53c8459e2bd76f0e0aae6ad09cd8d2bd765fdc3b25b41a`) and a copy of `earthmap.json` are preserved in `C:/Users/andri/AppData/Roaming/VintagestoryData/Backups/earthmap-before-0.17.0-pre.14-20261031-185839`. The active config hash is unchanged (`d240052367b94aebb4d6d69a4f063ad0fdc0857b28ac7a171595544353f2602c`). No game launch or player-save access. New worlds expose the dropdown and 3000 m default; existing worlds retain their prior cutoff behaviour. [Machine-readable installation record](../installation-summit-correction-2026-10-31.json).

## Source locations

`src/EarthWorldOptions.cs`, `src/EarthParams.cs`, `src/Core/TerrainModel.cs`, `src/Core/KnownSummits.cs`, `worldconfig.json`, `assets/game/lang/worldconfig-en.json`; regression: `tests/EarthWorldOptionsTests/SummitChecks.cs`.

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: implemented and built as 0.17.0-pre.14; offline verification passed. Installed at the author's request at 18:58 Europe/Riga; client inspection pending.
