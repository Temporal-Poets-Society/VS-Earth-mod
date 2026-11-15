# 0041 — Sea is strongly recommended and optional

Status: built in 0.17.0-pre.16; in Earth 1.0.0 and 2.0.0. The two data packs it kept required are superseded by 0066 (one mod, data from GitHub).

Author direction, 31 October 2026, boat compatibility chat: **Make Sea optional.** This supersedes decision 0038's
Sea requirement. Cylinder World, Hydrology's river-query contract and the two content data packs remain required.

Implemented in 0.17.0-pre.16 after the publication-data rebuild finished. The pre.15 archives and data files are
preserved. No world-generation rule, data precision, terrain datum or saved layout is changed by this revision.

The manifest no longer declares Sea. `RequiredMods.Problem` no longer queries or rejects its version/provider API;
active Earth startup guards still enforce Hydrology on the server. Client preflight accepts the absence of Sea.
The shared `SeaWindSetter`/`SeaWindClearer` reflection helpers remain because `EarthWeather` still optionally binds
a compatible Sea. Without Sea, Earth controls vanilla wind, rain and clouds itself. With a compatible Sea, Earth's
wind also drives Sea's waves and ship/boat consumers. Existing older-provider fallbacks remain in `EarthWeather`.

Publication guidance strongly recommends Sea for waves, water rendering and wind-driven vanilla sailing (the latter
requires Sea 0.4.7-pre.13+). Ships still requires Sea independently. No optional integration becomes a terrain-startup
requirement through that recommendation.

Verification: the revised 35 dependency checks accept absent/older/incompatible Sea while retaining Hydrology failures,
guard eligibility and manifest agreement. A disposable native Earth startup without Sea checks both data packs,
Hydrology query 2, Earth weather and enabled terrain. Final build/native/package results are in the suite's
`overview/verification/boat-waves.json`. Graphical-client verification remains pending.

## Installation follow-up

At the author's explicit request, pre.16 and both required 1.0.0 data packs were installed on **31 October 2026 at 21:40 Europe/Riga**. The installed hashes match the tested archives. The actual prior pre.14 ZIP is backed up under `VintagestoryData/Backups/earthmap-before-pre16-20261031-214006`, along with settings snapshots; no settings or companion archive was changed, and no save or game was opened. Following installation, the author reports **"ok it works"** on 31 October. Installed-build gameplay smoke is author-confirmed; the feedback does not specify detailed regional, Sea-absent-client or multiplayer coverage. [Installation receipt](../../../../../docs/installation-earth-pre16-2026-10-31.json).
