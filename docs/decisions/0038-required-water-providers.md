# 0038 Required Hydrology and Sea providers

Status: Agreed by the author 31 October 2026; implemented in 0.17.0-pre.13, verification recorded below.

## Decision

**Superseded for Sea by [0041](0041-optional-sea.md), 31 October (later author direction):** Sea is strongly recommended
and optional from pre.16. The Hydrology/Cylinder World requirements and river guard remain. The following records
the pre.13 implementation and its original verification.

Earth requires Cylinder World, Hydrology and Sea. The manifest retains Cylinder World 0.11.11 and adds **Hydrology 0.1.10** and **Sea 0.4.0**. The Hydrology minimum is the first version advertising river query capability 2 (`2463955`); the Sea minimum is the first with the public wind-provider registration and clearing APIs (`a6b8c0b`). These versions come from the provider implementations, not the looser requirements of the ship mod.

An active/new Earth world also checks loaded provider systems and their runtime contracts. Hydrology must advertise `EarthRiverQueryVersion >= 2` on the server. Sea must have the public instance `bool SetWindProvider(string, Func<double,double,double,bool,(double,double)>, Func<double,double,double,(double,double)>)` and `void ClearWindProvider(string)` methods. The methods must be invocable non-generic methods. Version strings need a numeric three-part core and must satisfy the game's version comparison.

Guard active Earth before `ApplySize`, before the sea-level bootstrap adopts/stores a datum, and before `InitWorld` creates or repairs saved terrain parameters. Skip the fauna asset repair preflight if the provider contracts are unavailable. On clients, validate Sea before accepting enabled Earth parameters; Hydrology's server-only API is not required on clients.

Missing/incompatible providers in these guarded paths log an actionable error, request the server's public graceful shutdown and throw to stop the callback. An ordinary `StartServerSide` exception alone is insufficient because the game's mod loader catches it. No new terrain-rule version is introduced; existing Earth parameters retain their river layout and scale. Changing a saved Earth's play style or config enable flag does not bypass its requirements. Inactive/vanilla worlds do not run the Earth-specific guard.

## Initialization order and boundaries

Do not require Sea's model or Hydrology's bound Earth query during preflight. Sea supports registration before its model is built; Hydrology binds the query at RunGame after Earth establishes its saved river layout. Individual wave/flooding switches are not global mod enable flags.

The engine may exclude Earth entirely when a manifest dependency is missing or too old. Earth's callbacks cannot stop a dedicated server in that case because they never load. Manifest rejection and Earth's runtime refusal are separate checks. A missing required mod should be corrected before opening an Earth world; this change cannot redefine the engine's behaviour for excluded mods.

Graceful shutdown can update normal game save metadata. The guard prevents Earth's size/datum/terrain initialization from running after a provider-contract failure; it does not promise a byte-identical whole save database.

## Implementation and verification

Production files: `modinfo.json`, `src/RequiredMods.cs`, the server/client entry points in `EarthMapSystem`, `EarthVerticalBootstrap`, `EarthFauna` and the shared method lookup in `EarthWeather`.

- Release build against installed Vintage Story 1.22.7: zero warnings/errors.
- `tests/RequiredModsTests`: 35 checks, covering version/API mismatches, compatible overloads, client/server differences, saved/new/inactive world eligibility, shutdown-before-throw and manifest/runtime minimum agreement.
- `tests/EarthWorldOptionsTests`: 167 existing checks pass.
- Dedicated-server cases use `tools/run-required-mods-server-test.ps1` and the test-only observer in `tests/RequiredModsServerProbe`. All data paths are unique directories under this checkout's `testruns`; player profiles are not copied or installed into.

All seven dedicated-server cases completed: compatible startup binds Hydrology's query and Sea's wind provider; incompatible APIs stop Earth initialization; saved Earth state remains intact; Standard stays inactive; missing archives are rejected by the native loader and expose its 1.22.7 play-style error. [Full report and limits](../verification-required-providers.md). No graphical-client verification is claimed.

## History

- 31 October: the author requests making Hydrology and Sea mandatory in Earth's manifest and loading checks. The read-only dependency audit establishes the minimum versions and startup-order constraints. Implemented on `codex/earth-playstyle-2026-10-31`; the existing pre.12 ZIP is preserved.

**2.0.0-rc.2 (9 November 2026):** the Hydrology minimum is 0.6.2-rc.1 with `EarthRiverQueryVersion` 3 or newer (was
0.6.1 with 2), so every new Earth world gets pool layout 11's calm rivers (decision 0027's history); the author:
"old save does not matter with this release".
