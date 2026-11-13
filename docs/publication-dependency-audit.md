# Earth publication dependency and startup audit

Read-only side-session audit requested by the author on 31 October 2026. No build, game run, manifest edit, release rebuild or save change was performed. This distinguishes the author's publication requirements from the inspected candidate's actual loader and startup behaviour.

> **Status (8 November 2026, Earth 2.0.0-rc.1):** this records the 0.17.0-pre.12 candidate. Since then Hydrology is a
> manifest dependency (`modinfo.json`: `hydrology` 0.6.1, `cylinderworld` 1.0.4), and `RequiredMods` refuses to start an
> Earth world (new, or saved as Earth) without Hydrology at that version and river query 2 or newer. The Hydrology row
> below describes the old candidate, not the current one.

## Baseline candidate

- Source: `Earth/.claude/worktrees/codex-earth-playstyle`, version **0.17.0-pre.12**.
- Commit: `45efac9a2687c067e11b6ddf3100e330131da050`; worktree clean when inspected.
- Existing ZIP: `release/earthmap-0.17.0-pre.12.zip`; its manifest matches the source manifest exactly.
- ZIP SHA-256: `d2c50bc91e202f9e447bc7e2e44befb10f4a257badd91bde13db61d839b15d1a`.

## Publication requirements and implemented checks

| Mod | Author's publication category | Current candidate behaviour |
| --- | --- | --- |
| [Cylinder World](https://mods.vintagestory.at/cylinderworld) | Mandatory | Manifest requires `cylinderworld` 0.11.11 or newer. New Earth worlds also need active Cylinder parameters, layout version 3 or newer, polar walls and polar climate. Inadequate configuration disables Earth generation and logs an error. |
| [Hydrology](https://mods.vintagestory.at/hydrology) | Mandatory | No manifest dependency or startup refusal. Reflection reads `EarthRiverQueryVersion`; missing provider or errors return 0. New worlds use the current river layout with capability 2+, at most layout 8 with capability 1, or at most layout 7 without it. |
| [Sea](https://mods.vintagestory.at/sea) | Mandatory | No manifest dependency or startup refusal. Earth optionally finds `Sea.SeaSystem.SetWindProvider`. Missing Sea is a normal return; missing or rejected binding logs warnings. An older Sea can keep its own wind while Earth supplies rain and clouds. |
| [World Map](https://mods.vintagestory.at/wmap) | Strongly recommended | No manifest dependency. A missing `wmap` produces only the recommendation that Earth works without it. |
| [Astra Terra for Earth](https://mods.vintagestory.at/astraearth) | Strongly recommended | No manifest dependency. Earth finds `AstraTerra.AstraTerraModSystem` and calls `SetLongitudeProvider` by reflection. Missing/incompatible integrations log the fallback; longitude-aware sunlight also requires that provider's `LongitudeAwareSun` setting. The page alias does not establish an assembly identity or capability. |
| [Hansa ships](https://mods.vintagestory.at/ships) | Recommended | No Earth startup check or direct compiled dependency found. Hansa main was 0.1.0-dev.17 during this audit and requires Sea 0.3.5+. |
| [Sea Monsters](https://mods.vintagestory.at/seamonsters) | Optional | No Earth startup check or direct compiled dependency found. Sea Monsters main was 0.2.1 and requires Hansa 0.1.0-dev.15+ and Sea 0.3.3+. |

These Mod DB destinations were supplied by the author. Some pages are not published yet; their contents could not be fetched during this update. The URLs do not establish public release availability or compatibility of a particular uploaded file.

## Activation and saved worlds

New generation requires a new `standard` world, play style `earthmap`, Earth enabled and a suitable Cylinder layout. Inactive worlds receive a saved disabled Earth record. An existing vanilla save is not converted into Earth by installing the mod or changing its play style.

An existing enabled Earth record takes precedence over later play-style/config activation changes. If suitable Cylinder parameters are absent, Earth is disabled **for that session**, preserving its original saved record. The warning explicitly says newly generated chunks get vanilla terrain: this path does not stop the whole server. Distinguish that runtime fallback from the loader's manifest requirement.

Creation choices persist in the saved Earth parameters; older saves retain their former config-on-restart behaviour. New worlds preflight Cylinder's enabled/polar flags before adopting Earth's lowered sea level, and saved sea level is restored separately at WorldReady.

The real seam needs Earth polar mode and Cylinder layout 10+; otherwise a new world retains a sea gutter. Weather-data/rules mismatches can fall back to vanilla weather without rejecting Earth. Missing sky-look suggestion APIs leave the other mod's settings unchanged.

## Source anchors

All Earth source links below are relative to the inspected candidate.

- [Manifest](../.claude/worktrees/codex-earth-playstyle/modinfo.json): lines 11–15 establish client/server installation and the declared game/Cylinder dependencies.
- [EarthMapSystem](../.claude/worktrees/codex-earth-playstyle/src/EarthMapSystem.cs): 222–281 activation and Cylinder fallback; 554–555 Wmap recommendation; 558–561 save preservation; 743–765 Hydrology lookup and Cylinder configuration; 774–781 real-seam capability; 839–840 river-layout selection.
- [EarthWorldOptions](../.claude/worktrees/codex-earth-playstyle/src/EarthWorldOptions.cs): 18–25 world eligibility; 43–65 creation persistence and older-save behaviour.
- [EarthWeather](../.claude/worktrees/codex-earth-playstyle/src/EarthWeather.cs): 109–122 weather fallback; 154–191 optional Sea binding and warnings.
- [AstraTerraLongitude](../.claude/worktrees/codex-earth-playstyle/src/AstraTerraLongitude.cs): 17 and 25–60 type binding, sunlight option and fallback messages.
- [AstraTerraLook](../.claude/worktrees/codex-earth-playstyle/src/AstraTerraLook.cs): 23–28 optional sky-settings API.
- [EarthVerticalBootstrap](../.claude/worktrees/codex-earth-playstyle/src/EarthVerticalBootstrap.cs): 24–44 sea-level handling; [CylinderCompat](../.claude/worktrees/codex-earth-playstyle/src/CylinderCompat.cs): 35–47 Cylinder preflight.
- [Earth project references](../.claude/worktrees/codex-earth-playstyle/EarthMap.csproj): 37–43 list game/framework references, with no direct companion-mod assembly references.
- Current [Hansa manifest](../../Hansa/modinfo.json) and [Sea Monsters manifest](../../Sea%20monsters/modinfo.json) establish those mods' own loader dependencies, not Earth's.

## Remaining release work

The agreed mandatory set is **Cylinder World, Hydrology and Sea**. Only Cylinder World is enforced by the inspected Earth manifest. Add verified minimum Hydrology and Sea versions to the selected publication manifest, and decide whether disabled/incompatible runtime cases should refuse startup or retain the existing fallbacks. Verify those cases on disposable worlds before claiming enforcement. Do not copy Hansa's Sea minimum as proof of Earth's required provider capability.

Align the final display name `Earth` and author with the publication fields; the inspected candidate still says `Earth Map` and `Your Name`. Preserve pre.12 when preparing a new publication package, and refresh the mod inventory after manifest/package changes. Those implementation and release steps remain outside this publication-copy update.

## Implementation follow-up

After this read-only baseline audit, the author requested implementation. **0.17.0-pre.13** in the same candidate worktree now declares **Hydrology 0.1.10+** and **Sea 0.4.0+**, with loaded API checks before Earth's world-size/datum/parameter mutations and a client Sea check before enabled parameters are accepted. Existing terrain rule versions are preserved. [Decision 0038](../.claude/worktrees/codex-earth-playstyle/docs/decisions/0038-required-water-providers.md) and [verification report](../.claude/worktrees/codex-earth-playstyle/docs/verification-required-providers.md) supersede the pending-enforcement status above.

Verification: clean Release build, 35 provider-contract checks, 167 existing world-option checks and seven isolated server scenarios. Compatible providers bind the river query and Earth wind; incompatible runtime APIs stop initialization, saved Earth state remains intact, and inactive Standard worlds remain inactive. Missing archives are rejected by the native loader; 1.22.7 then errors resolving the excluded Earth play style before Earth's own checks can run. This engine boundary and the lack of a graphical-client run remain explicit. The pre.12 ZIP is preserved. Nothing was installed in the player's profile or published.
