# Mountain height: Claude review and integration, 27 October 2026

Reviewed: Codex's `codex/mountain-height-playtest` (`e51a2ea`, production code `852584b`, 0.17.0-pre.2, based on main
`59c17fc`). Result: `claude/mountain-height-integration`, Codex's branch merged onto `playtest/2026-10-26` (`8a38b78`, the
installed 0.16.14-rivers.2 line), 0.17.0-pre.3. Nothing installed, pushed or merged to main.
[Decision 0031](decisions/0031-mountain-height-playtest.md) records the scope; this file records the review.

## Findings

| # | Severity | Finding | Change |
| --- | --- | --- | --- |
| R1 | High (integration) | The installed playtest line already gives every new world decision 0009's climate rules 2 (`ClimateLapse`, CHELSA temperatures). Codex's branch, based on older main, defined a different rules 2 (also real rainfall) and patched the same five vanilla helpers with its own Harmony instance. Merged as-is, both prefixes would run and a rules-2 world's rain would decode differently depending on which line created it. | Codex's `EarthAltitudeClimate` dropped; the temperature path is `ClimateLapse`. The test profiles get rules 3 = rules 2 + real rainfall (`ClimateLapse` patches `GetRainFall`, no-op elsewhere). `ClimatePixelForVanillaLapse` (Wmap) keeps vanilla's rain compensation, since Wmap's shader applies vanilla's formula. |
| R2 | Medium | `EarthAltitudeClimate.Clear()` in a mod system's `Dispose` unpatched the process-wide helpers and cleared the scale. In single player server and client share them, and the client can dispose while the server still generates; chunks made then would get vanilla's decoding permanently. | Gone with R1: `ClimateLapse` holds per-side claims and never unpatches; `SeasonTintAltitude` never unpatches either (its postfix is a no-op without rules 3). |
| R3 | Medium (saves) | The lowered sea level was re-applied at each start only from an active saved EarthParams. If Earth refused the new world after the early change (Cylinder World turns itself off on a map too narrow for its seam, or without walls), that vanilla world was generated at 128 and reopened at vanilla's 220. | `EarthVerticalBootstrap` saves a lowered level on its own (`earthmap-sealevel`, 4 bytes) and re-applies it when there is no active EarthParams. |
| R4 | Low (multiplayer) | Vanilla sets `Climate.Sealevel` only in the server's `GenTerra`; a multiplayer client keeps 110, which `GetRainFall` reads. With a lowered sea level this skewed client rain (and already did for every height other than 256). | `OnParams` sets it from the world's EarthParams. |
| R5 | Low | Codex's edit re-encoded three comment characters in `EarthParams.cs` (`°`, `à`, `–` as cp1252 mojibake). | Restored; the file now differs from the playtest line only by fields 74-77 and `BuildVertical`. |
| R6 | Low | Seasonal tint: the shader replacement ran only from a `LevelFinalize` callback keyed on a flag, and depended on the unpatching in R2 to restore vanilla's line. | `SeasonTintAltitude` records which scale the loaded shaders were written for and reloads after `LevelFinalize` when that differs from the world's (also back to vanilla's line in a later legacy world). The server sends EarthParams in `PlayerJoin`, before `LevelFinalize` (checked in the decompiled join sequence). |

Checked and found sound: the inverse mapping and linear curve (`VerticalScale`), fixed 47-block sea depth, the
configuration preflight before WorldReady, the WorldReady ordering before Cylinder World (-0.05 < 0.01 < 0.05), the
shader needle (`shaderincludes/colormap.vsh`, inlined by `ShaderRegistry.HandleIncludes` before compile, so a
`LoadShaderProgram` postfix sees it), the GLSL literals, the local tree path's thread-static factor buffer, the
protobuf tags 74-77 (no other Earth branch uses them), and the vanilla consumers of the climate helpers (server
`ServerWorldMap`, client `ClientWorldMap` and `ChunkTesselator`: all go through the patched methods).

Not changed (tuning, for the playtest to decide): curve values, local tree line thresholds, physical slope (keeps too
much soil at 512; St Helena becomes sand), the vanilla fertility and tree height-band inputs that still use `y` in
blocks (`HeightRel = y / mapHeight` outside local ecology; vanilla trees and the installed providers set no height
limits, so it only weights suitability slightly).

## Compatibility

- Existing worlds (rivers.2 and older): no new fields, so no linear curve, no local ecology, no physical rock, their
  own climate rules and vanilla's shader line; `EarthParams.CurrentVersion` is unchanged. The bootstrap changes nothing
  where the saved sea level equals vanilla's.
- Codex's pre.1/pre.2 test worlds saved rules 2 with real rainfall; pre.3 reads that as decision 0009's rules 2.
  Don't continue them with pre.3: create new test worlds.
- Other mods: Cylinder World, Sea, AstraTerra and Wmap read `World.SeaLevel` live or capture it at WorldReady after
  the bootstrap; Hydrology reads no sea level. Wmap tints through `ClimatePixelForVanillaLapse`.

## Validation

Release build 0 warnings; VerticalProfileTests 142,141 assertions pass; EarthMapTests 384 only the known Lake Geneva
check; five dedicated-server worlds (K 512, C 384, A 512, rivers.2 world reopened with pre.3, full K 512 with rivers)
pass every self-test check. Details: [the playtest plan](mountain-height-playtest-plan.md#claude-integration-checks).
Codex's own focused run reports 141,861 assertions; rerun on `e51a2ea` it gives 141,581 (probably a typo).

## Remaining before merge

The user's client playtest: creation tab, silhouettes, seasonal tint, rivers/coasts at sea 128, caves and ores. Merging
this branch to main also brings the whole `playtest/2026-10-26` line, which is itself waiting on play-testing.
