# 0045: Sea-level deposits follow Earth's sea level

Date: 2 November 2026. Status: implemented on `claude/sealevel-deposits` (from `release/1.0`), version 1.0.2 in source; native-server and graphical-client verified (below). Not packaged, installed or uploaded.

## Report

A player (Dskater84, Discord, 2 November) reports saltpeter forming on the surface. Their world: Earth play style, world height at the slider's maximum (512), **Sea level 110**, **Vanilla rocks and ores**, start Athens, Earth size 1,024,000 (derived from the screenshot's coordinates, see Verification). Their screenshot (45°31′ N 7°52′ E, near Ivrea; block `-36109, 170, -17160` from spawn) shows white saltpeter coating on exposed ground. The player says it looks like "all the deposits spawn above ground". The player had about 130 other mods earlier, but only the Earth suite was installed for this world.

## Cause

Vanilla's server start (VS 1.22.7, decompiled `VSEssentials`/`VintagestoryLib`):

1. `AssetsFinalize`: `GenTerra` (order 0) sets `TerraGenConfig.seaLevel` to 0.431 x world height (220 at 512). `GenDeposits` (0.2) then initialises every deposit. **`FollowSealevelDiscGenerator.Init` multiplies its `YPosRel` by that sea level, once.**
2. `StartServerSide`: `GenBlockLayers` and `GenDevastationLayer` seed noise with `World.SeaLevel` (vanilla's value).
3. `WorldReady`: `EarthVerticalBootstrap` lowers the sea level to Earth's (110 or 128, decisions 0031/0036).
4. `InitWorldGenerator` and chunk generation: everything else reads the sea level live, or initialises here (rock strata noise, block patches, story structures, devastation noise).

So every `disc-followsealevel` deposit kept vanilla's scale: saltpeter, three iron variants, two coal variants, chromite, ilmenite, rhodochrosite, borax, cinnabar, fluorite, graphite, kernite, phosphorite, sulfur, emerald, peridot and obsidian. At height 512 they are placed up to 0.8 x 220 = 176 blocks (plus distortion) instead of 88; at the default 384, 1.5x too high. Rock-hosted ones land in hills and near the surface. Saltpeter replaces air and vanilla's disc placement does not compare with the ground height; `BlockFullCoating` only refuses Y outside 0.063-0.275 x world height (32-140 at 512). With Earth's ground starting at 110, every ground surface between Y 110 and 140 that a saltpeter disc crosses gets coated. Prospecting readings (`GetYMinMax` reads `YPosRel`) and Earth's regional ore layer (copies the same `YPosRel`) used the same wrong heights. Worlds at height 256 (vanilla's sea level is also 110) and worlds without a lowered level are unaffected.

## Decision

Keep setting the sea level at `WorldReady` and correct the deposits there. Setting it earlier was rejected: `GenBlockLayers` seeds its noise from the sea level in `StartServerSide`, so an earlier change would shift soil layers in new chunks of every existing Earth world.

- `SealevelDeposits`: a Harmony postfix on `FollowSealevelDiscGenerator.Init`, installed in `EarthVerticalBootstrap.Start` (before `AssetsFinalize`), records the sea level each generator was scaled with. It covers vanilla, other mods' deposits, child deposits, subclasses and `/wgen` reloads.
- `EarthVerticalBootstrap` (WorldReady, after setting the level, in any world Earth's sea-level choice applies to): `Rescale` multiplies each generator's `YPosRel` by current / recorded level and records the new one. Idempotent; a `NatFloat` shared by two generators is scaled once. Unrecorded generators (hook unavailable after a game update) are assumed to carry the level seen before Earth changed it, with a warning in the log line.
- `EarthOres.Activate` repeats `Rescale` before building its generators (a no-op after the bootstrap; logged as a warning if it ever moves anything).
- Self-test: `deposit sea agreement` FAILs if any sea-level generator is not scaled for the current level, or the Init hook is off.

Existing saves: chunks already generated keep their deposits; new chunks get the corrected heights, so an ore-depth boundary is possible at explored edges. No cleanup of placed saltpeter (not requested). Not covered: a third-party generator that caches the sea level some other way than through `FollowSealevelDiscGenerator`.

## Verification

Release build: zero errors. Isolated dedicated servers, Earth play style, seed 1319125318 (the player's), sea level 110, vanilla rocks and ores, Cylinder World 1.0.4, Hydrology 0.6.1, data packs 1.0.1; the test-only ore audit (`docs/research/ore-ingame-validation-2026-10-15/oreaudit`) counted deposit blocks in 5 x 5 chunks around Ivrea, Turin, Vienna, the Pannonian plain (47.2° N 19.8° E) and the Alps (46.45° N 8.1° E). Earth 1.0.1 release ZIP against this build:

| Height | Build | Log | Saltpeter above ground (Vienna / Pannonia) | Saltpeter highest Y | Highest other sea-level deposit |
| --- | --- | --- | ---: | ---: | --- |
| 512 | 1.0.1 | — | **293 / 407** | 140 | bituminous coal Y 193, borax 189, sulfur 131 |
| 512 | fix | 28 generators rescaled 220 → 110 | 0 / 0 | 91 | coal 106, borax 110, sulfur 98 |
| 384 | 1.0.1 | — | 0 / 0 | 105 | coal 145, hematite 124 |
| 384 | fix | 27 generators rescaled 165 → 110 | 0 / 0 | 95 | coal 100, hematite 82 |
| 256 | fix | nothing to rescale (vanilla's level is 110) | 0 / 0 | 70 | coal 100 |

Every fixed run's self-test reports `deposit sea agreement: PASS; 38 sea-level deposit generator(s), 0 not scaled for sea level 110; Init hook on` (38 includes generators initialised after WorldReady, already at 110). The self-test's other results match 1.0.1's; the one FAIL (live rainfall, no map region loaded in a short run) is the same in both builds. No errors in either server log. Regional ores (`earthOres` regional) were not run; the ore layer's repeated `Rescale` is a no-op by construction.

Graphical client (2 November, VS 1.22.7 Linux client and server in WSL2, off-screen X display; the player's settings including start Athens, at heights 512, 384 and 256, 1.0.1 and fix each). A copy of the ore audit that also lists surface saltpeter blocks (depth ≤ 2, open above) found them in 1.0.1 only at 512: 1,830 above ground in 7 x 7 chunks east of Vienna (48.17° N 16.9° E), 698 on the Pannonian plain, none at 384 or 256. The player's world is Earth size 1,024,000, not 512,000: their screenshot shows `-36109, 170, -17160` (vanilla's X and Z are relative to the Athens spawn) at 45°31′ N 7°52′ E, which is 2,276 blocks per degree on both axes. A 1,024,000 world with their settings (Athens spawn at 37.98° N 23.73° E) puts that block within about 20 blocks of the coordinate, on ground averaging Y 170 (their Y). There, 1.0.1 places saltpeter up to Y 140, below the hills, and on the surface only where valleys drop to it (4 blocks within 10 chunks); 1.0.2 logs 28 generators rescaled and places it no higher than Y 90, with none above ground. The author installed the 1.0.2 zips, visited the player's spot in their own game and found it clean (2 November). The client stood on recorded blocks and looked down: 1.0.1 at 512 shows white saltpeter coating on the grass east of Vienna and on the plain, as in the player's screenshot; the fix at the same blocks shows grass only. Both builds show grass at 384 and 256. Fixed servers logged 28 generators rescaled (220 → 110 at 512, 165 → 110 at 384) and none at 256.

Source: `src/SealevelDeposits.cs`, `src/EarthVerticalBootstrap.cs`, `src/EarthOres.cs`, `src/SelfTest.cs`, `modinfo.json`.
