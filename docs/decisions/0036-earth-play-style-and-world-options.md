# 0036: Earth play style and creation choices

Status: Built in 0.17.0-pre.12 (`45efac9`); in Earth 1.0.0 and 2.0.0. The play-style code changed to `earth` by 0044, and one play style per scale by 0068 | Date: 2026-10-31

## Decision

The Earth play style uses code `earthmap`, matching this mod's id. `earth` belongs to the third-party EarthWorldGen
mod and must not collide. `playStyles` stays a non-null list (0023). Earth belongs to the survival playlist, uses the
standard world type and appears at listOrder 5.5, between Standard (5) and Exploration (6).

Its 18 survival values are copied exactly from **Survival Mode 1.22.7's ModInfoAttribute.WorldConfig** in
`VSSurvivalMod.dll`, decompiled locally. They are recorded in `tests/fixtures/standard-survival-1.22.7.json` and checked
against Earth's preset. These are explicit Standard overrides; all other survival attributes retain the game's
attribute defaults through WorldConfig.allDefaultValues. Earth's creation defaults are added to that preset.
No play style world-height setting is invented: the game's height slider is independent.

Without saved EarthParams, a world generates Earth only when all of these hold: `SaveGame.IsNew`,
`SaveGame.WorldType == "standard"`, `SaveGame.PlayStyle == "earthmap"`, config Enabled, and usable Cylinder World.
Missing, unknown and foreign style codes are inactive, including EarthWorldGen's `earth`. The game fills Earth
attribute defaults into every style, so their presence is never evidence that a world chose Earth. The early size,
sea-level and fauna-asset paths use this same style gate. At WorldReady, inactive worlds store disabled EarthParams;
they cannot become Earth on a later restart. Existing worlds with no parameters are never converted.

Saved enabled EarthParams take precedence over style and config Enabled, retaining the established Cylinder World
requirements and failed-session behaviour. Old terrain fields and protobuf numbers are unchanged. Legacy sea level,
height curves, reduced detail, legacy ecology and physical-rock test settings still deserialize and execute.

## Menu and persistence

Remove legacy values from sea level and height curves, and remove detail/ecology/rock test attributes. The existing
defaults supply full detail, real-altitude ecology and physical slopes inside alpine cover. Hidden old keys remain
readable for compatibility; no older save is reinterpreted from the new menu.

New creation attributes choose regional or vanilla rocks/ores (both RegionalGeology and RegionalOres), regional or
vanilla animals (provider and vanilla range systems together), extinct animals, Earth or vanilla weather, rain and
storm frequency. Freeze them into EarthParams, using existing fields. Add protobuf 87 CreationOptionsVersion=1 and
88 FaunaRangeProfile. New worlds retain these settings after config edits. An absent attribute falls back to the
existing mod config; invalid enum/numeric input uses a safe fallback, including legitimate zero frequencies.
The shipped menu/preset supplies Earth defaults. Config remains the fallback for callers that omit attributes.

An absent creation marker is 0 in older saves. They keep config-on-restart weather and extinct-animal behaviour;
their regional flags and explicit one-shot upgrades retain decision 0002. Weather rules/signatures are still
negotiated every session, rather than freezing a particular weather implementation. Existing animals are untouched.

Decompiled WorldConfigurationAttribute and GuiScreenWorldCustomize have no style-dependent visibility/enabling
condition; controls are only disabled by OnlyDuringWorldCreate on existing worlds. Therefore each Earth description
says "Used by the Earth play style." No main-menu patch is added. Frequency descriptions also say they apply only
with Earth weather. Other suite mods' entries in category `earth` keep their own meaning and declarations; see README.

## Inactive-world hook audit

| Surface | Inactive behaviour |
|---|---|
| Size / vertical bootstrap | No size or datum changes for new foreign styles; old saved datum compatibility retained |
| EarthWorldGen, EarthTrees, EarthCorals | GenTerra, arid-fill transpiler, maps, bare rock, patch reservations, trees, coral, ponds, springs, structures/villages and both creature guards return through vanilla when gen.Active is false |
| ClimateLapse, SeasonTintAltitude | No lapse table/real-rain vertical scale; vanilla altitude/rain/tints |
| EarthMapSystem climate, sky, spawn, story | Enabled/model guards; no data load, sky provider, story relocation or ChooseSpawn on inactive worlds |
| EarthPlants | No active plant state or model, so native patch and forest-floor predicates pass through |
| EarthFauna | No state/patches/entity rewrites; AssetsLoaded compatibility repair also checks the style and animal option |
| EarthGeology | Active guards on strata/soil/deposits/salt; the previously independent GA child-grade repair is now gated on enabled regional Earth, including reload |
| EarthOres / EarthOreGenerator | No ore session, generator replacement, factor, bauxite or coal-seam writes |
| EarthWeather / RainStreaks | No model, weather patches or Sea wind-provider registration; shader injection/reload now also requires an Earth weather model |
| Hydrology river interface | Query version 0, RiverAt false, RiverFlowAt -1; no compatibility handshake writes |
| ClientMapRaceFix | Lock prefixes now pass through unless enabled EarthParams have arrived |
| EarthOreContent | Registers Earth's own block class/assets only; places nothing without regional ore generation |
| SelfTest / commands | Activation guards; no world mutation while inactive |

The installed Cylinder World/Sea/Hydrology/Wmap may still apply their own rules. "Vanilla" here means Earth leaves
the world's generation alone; Earth does not disable another mod.

## Validation

`tests/EarthWorldOptionsTests` links production options/params/config and real API/protobuf assemblies; it checks
style gating, exact Standard overrides, new choices and zero frequencies, creation freeze across config changes,
missing-marker compatibility and hidden legacy values through protobuf. It runs offline with .NET 10 and the
coordinator-provided `.local/vs` references. No game client, installation or userdata access is involved.

The existing server self-test and mountain-playtest emitters now select `earthmap`; the latter explicitly selects vanilla weather to preserve its weather-off test setup.

Coordinator acceptance: build against game, server self-test in Earth and Standard styles, reopen each with changed
config/style, inspect the Earth row/description and shared tab in the menu, and compare an inactive vanilla world's
terrain/climate/deposit/animal/weather behaviour. The screen's conditional controls are unavailable, so verify the
descriptions are readable alongside the companion mods' entries.

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: Implemented in 0.17.0-pre.12; offline checks passed, coordinator server/client acceptance pending
