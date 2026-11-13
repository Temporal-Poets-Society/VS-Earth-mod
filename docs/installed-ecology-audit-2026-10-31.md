# Earth flora and fauna installation audit

Checked and installed on 31 October 2026. All packs in the publication's flora/fauna lists are represented in the installed Earth placement catalogues.

| Set | Before | After |
| --- | --- | --- |
| Eight Floral Zones packs | All eight installed and loaded | Preserved |
| Wildcraft Trees and Shrubs and Herbarium | Both installed and loaded | Preserved |
| Twenty listed Fauna of the Stone Age packs | None installed | All twenty installed and enabled |

The previous game-session log explicitly reported no supported fauna provider installed. It also confirmed all eight flora packs and Wildcraft in Earth's tree selection. Its loaded Earth was pre.12; the archive now installed is pre.14. Changing files in Mods does not update an already loaded game session.

## What Earth actually uses

Earth chooses natural trees and shrubs from the installed providers using botanical region and climate rules. Ground-plant gating covers the Floral Zones packs. Herbarium is a supporting library, with no independent tree or animal placement list. Vanilla ground plants keep their climate rules, and selected wild crops/non-plant organisms remain climate-only.

The latest isolated startup confirms **195 of 195 registered natural tree generators mapped** and **256 of 275 pack plant patches gated by native range**. The remaining **19** stay climate-only. The shipped tree/plant rule counts below describe catalogue entries, not a measured population or a promise that every species grows at every site.

| Flora component | Tree rule entries | Ground-plant rule entries |
| --- | --- | --- |
| Floral Zones Cape | 10 | 24 |
| Floral Zones Caribbean | 15 | 26 |
| Floral Zones Central Australian | 7 | 25 |
| Floral Zones Circumboreal | 9 | 31 |
| Floral Zones Cosmopolitan | 7 | 27 |
| Floral Zones East Asiatic | 11 | 19 |
| Floral Zones Mediterranean | 9 | 25 |
| Floral Zones Neozeylandic | 16 | 22 |
| Wildcraft Trees and Shrubs | 59 | Not part of this layer |
| Herbarium | Library | Library |

Earth does not supply the optional animals itself. The fauna mods supply their entities, models and behaviour; Earth limits their natural spawning to geographic ranges and suitable habitat. This applies to world generation and later natural spawning. Suitable animals may therefore appear in existing explored areas after a restart, subject to their normal population/spawn conditions and the save's regional-animal setting. It does not repopulate every area immediately.

The installed Earth archive contains **278 taxon rules across all twenty providers**. With supported extinct animals included, **267 are eligible** and **11 are deliberately held**. Every listed provider has eligible taxa. Four additional domestic-dog entity variants are excluded from natural spawning as unmapped; they are not missing packs. Native conditions, region/habitat, the extinct-animal choice and the provider's population rules still determine which animals can appear at a particular location.

## Fauna packs installed

| Pack | Installed version | Eligible taxon rules | Held rules |
| --- | --- | --- | --- |
| Bovinae | 0.3.6 | 23 | 5 |
| Caninae | 1.1.9 | 44 | 1 |
| Capreolinae | 2.0.18 | 33 | 4 |
| Casuariidae Plus | 1.1.13 | 8 | 0 |
| Cervinae | 0.1.14 | 1 | 0 |
| Chelonioidea | 1.0.8 | 7 | 0 |
| Dinornithiformes Plus | 1.0.25 | 42 | 0 |
| Elephantidae | 1.0.20 | 8 | 0 |
| Felinae | 0.2.16 | 4 | 0 |
| Iniidae Plus | 0.1.7 | 1 | 0 |
| Machairodontinae | 1.1.13 | 3 | 0 |
| Manidae | 1.0.23 | 8 | 1 |
| Meiolaniidae | 0.1.11 | 1 | 0 |
| Pantherinae | 1.2.18 | 9 | 0 |
| Rhinocerotidae | 1.0.28 | 6 | 0 |
| Sirenia | 1.0.32 | 5 | 0 |
| Spheniscidae | 1.0.18 | 20 | 0 |
| Thylacinidae Plus | 0.1.7 | 2 | 0 |
| Viverridae Plus | 1.0.10 | 35 | 0 |
| Vombatidae Plus | 0.4.6 | 7 | 0 |

Held taxa include domestic cattle/reindeer/buffalo and animals whose range evidence does not meet the catalogue policy. The exact names and reasons are in [the held-species register](research/regional-fauna-2026-09-29/held-species.csv).

## Installation and validation

The twenty original cached archives were checked against the project's recorded SHA-256 hashes, manifest IDs/versions and ZIP CRCs. They were missing from the configured Mods directory, so only new files were added. The existing flora, configurations and saves were preserved. [Initial plan](research/installed-ecology-2026-10-31/install-plan.json), [final installation receipt](research/installed-ecology-2026-10-31/install-receipt.json) and [machine-readable audit](research/installed-ecology-2026-10-31/audit-summary.json) record the exact files.

The first isolated test reproduced the known Felinae 0.2.14 lynx `idle` sound error. The author's [Felinae 0.2.16 release for 1.22.7](https://mods.vintagestory.at/felinae) was downloaded from its official Mod DB link, checked and tested instead. Its SHA-256 is `7bd347a71eb599cf17b43e75402ca9fd7d290c30a139c947767e73b30fba70a3`. The repeat server loaded all twenty fauna providers with **zero errors**, with all four Felinae taxon mappings retained. That validated archive replaced only the older Felinae copy installed by this task; the older file is preserved under `research/installed-ecology-2026-10-31/superseded`.

Earth still logs a reviewed-version notice for newer Felinae because its catalogue records 0.2.14. The repeat test validates asset loading and regional mapping, not every AI or population behaviour. Existing Wildcraft wood-trellis recipe warnings remain outside this placement audit.

The repeat uses the installed Earth pre.14, Cylinder World playtest.8, Hydrology 0.1.13, Sea pre.12 and all thirty flora/fauna/library components in a separate loopback-only server. Its own disposable world and observer are under `testruns/ecology-install-20261031-felinae16`; the observer shuts down that test server after startup. No player world or profile was copied into it. Source logs are `Logs/server-main.log` in the two ecology test folders. The graphical client and worldwide population density were not tested.

## Next game launch

**Fully restart Vintage Story** to load the newly installed packs. The current game session's old mod list will not change on disk installation alone. `/earth fauna`, `/earth trees` and `/earth plants` report the rules and candidates at your location. Flora generation affects newly generated terrain; the added animals can also use later natural spawning in existing areas where their requirements are met.
