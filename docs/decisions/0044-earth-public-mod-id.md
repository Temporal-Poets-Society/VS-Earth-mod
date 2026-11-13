# 0044: Publish as Earth, mod ID earth

Date: 31 October 2026. Status: implemented; build, offline checks and native active-suite startup pass. Not installed or uploaded by this task.

## Author direction

The author wants the display name and ZIP to match Earth on Mod DB. Asked explicitly whether the internal ID should also change, the author selects **"Change internal ID to earth and update affected integrations"**. Existing saves need no preservation/migration, because the mod is not published yet. The author subsequently excludes **Earth Ruins and Underground Cities**, which are parked; edits briefly prepared there were restored and their source/build references remain unchanged.

## Identity and files

- Display name **Earth**, manifest ID **earth**, publication archive **earth-1.0.0.zip**. Mod DB page/alias: https://mods.vintagestory.at/earth.
- Earth, Terrain Data and Water Data all remain version **1.0.0**. Both data ZIPs remain byte-identical to the prior set.
- Owned assets move from `assets/earthmap` to `assets/earth`; asset lookups and block IDs use `earth:`. The creation play style and preset labels use `earth`.
- Config path is now `VintagestoryData/ModConfig/earth.json`, network channel `earth`, saved parameter key `earth-params` and sea-level key `earth-sealevel`. Harmony owner IDs and deferred callback identifiers use `earth`. No legacy aliases or save/config migration were added.
- **EarthMap.dll**, assembly binding identity **1.0.0.0**, C# namespaces and the public `EarthMap.EarthMapSystem` API type remain unchanged. These are existing companion API contracts, independent of the mod ID and asset domain.
- Archive/report names derive from the manifest ID in the packager; MSBuild’s release-file guard reads the same ID. Test fixture paths and the current native smoke runner follow the new identity. Data generation/analysis tools use the new asset paths; archived outputs and historical experiments remain preserved.

## Active integration audit

Cylinder World, World Map and Hydrology find `EarthMap.EarthMapSystem` through the existing public type/system lookup, which remains valid. Astra Terra for Earth, Sea and Ships do not declare the old Earth loader ID in their current production manifests or runtime source. The Sea standalone boat probe now rejects either old or new Earth IDs, so a no-Earth test cannot accidentally include the renamed mod. No active companion runtime or release ZIP needed rebuilding.

Parked Earth Ruins/Underground Cities still contain their historical `earthmap` declarations; they are intentionally excluded at the author's direction and need updating if development resumes. The root Earth main checkout likewise remains the separate historical 0.16.13 snapshot; this publication source is `codex-earth-playstyle-2026-10-31`.

## Verification

- Release build: zero warnings/errors. Main archive **28,489,946 bytes**; SHA-256 `2ff974ef525428d1f811433434f15b6a275f4e1cdb3308362bfbbb4a3bc17531`.
- **509** world-option/data-package checks and **35** provider checks pass. Tests verify the new manifest identity, new asset domain, `earth` play style and rejection of the old creation style, along with deferred archive loading and regional model comparisons.
- Main/data ZIP CRCs and SHA-256 values pass; all files remain below 40,000,000 bytes. Both external data packs are unchanged. Numeric geography/climate data and generation settings remain those of the earlier tested build.
- Minimal native world: `testruns/earth-identity-20261031/valid`, port 42764, installed Cylinder World .8/Hydrology 0.6.0/Sea 0.5.1 versions. New `earth` identity/style loads, layout 10/query 2 is active, Sea wind provider is Earth, and the disposable server shuts down cleanly with zero errors. The serialized parameter hash matches the earlier minimal fixture.
- Active-suite native world: `testruns/earth-identity-active-suite-20261031/run`, port 42765, **45 enabled mods**, including World Map pre.15, Astra Terra dev.25, Ships dev.23, current Cylinder World/Hydrology/Sea and installed flora/fauna. Earth reaches RunGame with layout 10/query 2, binds its Sea wind provider, maps **195/195** natural tree generators and recognizes **20 fauna providers / 278 mappings / 11 held taxa**. Zero errors/fatals and graceful shutdown. Parked content mods and the old installed Earth ID are excluded.
- No graphical-client or multiplayer run is claimed for the renamed build. No player profile, saved world or installed package was modified. Older unpublished ZIPs are kept as release backups; obsolete `earthmap-1.0.0.zip` copies were removed from the upload folder after verifying their retained backups, to prevent selecting the wrong identity.

[Current package verification](../../../../../docs/publication-packages-1.0.0.json). Source: `modinfo.json`, `worldconfig.json`, `EarthMap.csproj`, `src`, `assets/earth`, `tools/package-publication.py`, current smoke runner and identity/package checks. Native fixture input manifests/hashes are recorded under the active-suite run folder.

## Installation follow-up

On **31 October at 23:23 Europe/Riga**, the author explicitly requests installing Earth 1.0.0 for gameplay testing and removing the old EarthMap package. The verified `earth-1.0.0.zip` is installed; pre.16 is removed from active Mods and retained under the profile's Backups folder. Both required data packs already match the 1.0.0 release and are kept unchanged. The installer makes a one-time exact copy of the author's old `earthmap.json` preferences to `earth.json`; runtime code still has no legacy migration. All three current IDs occur once, are enabled and match expected hashes. No player save or game launch. On **1 November 2026**, the author reports **"ok it works"** for the installed renamed Earth 1.0.0. Installed-build gameplay smoke is author-confirmed; detailed coverage was not specified. [Installation receipt](../../../../../docs/installation-earth-1.0.0-2026-10-31.json).
