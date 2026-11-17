# Earth publication draft

Prepared 31 October 2026 for the Vintage Story Mod DB. Selected publication version: **1.0.0** on `codex/earth-playstyle-2026-10-31`, with required providers, summit controls and publication data and optional-Sea/shared-version decisions 0038–0044. The publication version is selected; no upload has been performed by this chat. The main checkout remains the separate 0.16.13 snapshot.

## Page fields

| Field | Suggested value |
| --- | --- |
| Name | Earth |
| URL alias | `earth`; page URL: https://mods.vintagestory.at/earth. Internal mod ID is `earth` |
| Summary | Explore the real Earth with regional terrain, climate, weather, wildlife, rocks and ores. |
| Category | Game Mod |
| Tags | Worldgen, Immersion; Exploration and Survival if offered |
| Side | Client and server; required on both |
| Game version | Vintage Story 1.22; development checks use 1.22.7. Select versions verified for the uploaded file |
| Required dependencies | [Earth Terrain Data](https://mods.vintagestory.at/earthterraindata), [Earth Water Data](https://mods.vintagestory.at/earthwaterdata), [Cylinder World](https://mods.vintagestory.at/cylinderworld), [Hydrology](https://mods.vintagestory.at/hydrology) |
| Strongly recommended | [Sea](https://mods.vintagestory.at/sea), [Astra Terra for Earth](https://mods.vintagestory.at/astraearth), [World Map](https://mods.vintagestory.at/wmap), the flora/fauna packs linked below, Terrain Slabs and Place On Slabs |
| Recommended | [Ships](https://mods.vintagestory.at/ships) |
| Optional encounters | [Sea Monsters](https://mods.vintagestory.at/seamonsters) |
| Code licence | GPL-3.0; bundled datasets retain their own attribution and terms |
| Author | Use the author's public name; Earth's current manifest still says `Your Name` |
| Source and issues | Add public URLs when available. The recorded Earth repository is private; do not present it as accessible |
| Release version | **1.0.0** for Earth, Earth Terrain Data and Earth Water Data; keep all three in step |

The publication package uses display name **Earth**, internal mod ID **earth** and ZIP **earth-1.0.0.zip**. Both required data parts use the same 1.0.0 release version.

## Publication files

The current candidate uses three mandatory Earth downloads, each below 40 MB:

| Download | Contents | Size |
| --- | --- | ---: |
| [Earth](../release/earth-1.0.0.zip) | Code, settings, ecology/resources, climate and weather | 28.5 MB |
| [Earth Terrain Data](../release/earthterraindata-1.0.0.zip) | Base elevation, fine mountain relief, named peaks and glaciers | 36.4 MB |
| [Earth Water Data](../release/earthwaterdata-1.0.0.zip) | Rivers, coasts, islands, lake overlays and shores | 31.6 MB |

Data-pack Mod DB pages supplied by the author: [Earth Terrain Data](https://mods.vintagestory.at/earthterraindata) and [Earth Water Data](https://mods.vintagestory.at/earthwaterdata). Their internal IDs are `earthterraindata` and `earthwaterdata`. Both destinations are linked in the Markdown and HTML descriptions. Upload every archive and set both data packs as required dependencies. All three are required on client/server. [Data choices and package design](../.claude/worktrees/codex-earth-playstyle/docs/decisions/0040-publication-data-and-packaging.md), [verification and checksums](publication-packages-1.0.0.json). The full-data pre.14 archives remain preserved. This candidate intentionally changes pre-release data; the author does not require compatibility with older worlds.

## Description


The paste-ready description is in [moddb-description.html](moddb-description.html). In the Mod DB description editor, open the `<>` Source code view, paste the file's contents and apply. Its plain-text wording follows.

**Explore the real Earth in Vintage Story.**

Earth builds a survival world from real elevation, coastlines, rivers and climate data. Cross familiar continents, follow great river valleys, climb mountain ranges and explore distant islands. Geography shapes the journey: the weather, vegetation, wildlife, rocks and resources change as you travel.

### Geography

- Real continents, ocean basins, continental shelves, coastlines and islands, adapted to the world's scale.
- Major rivers along their mapped courses, lakes at their real elevations, rapids and selected great waterfalls.
- Greenland and Antarctic ice sheets, mapped mountain glaciers and alpine terrain.
- An east-west wrapping world supplied by the required Cylinder World mod, with polar ice walls at the northern and southern limits.

The terrain combines geographic data with procedural detail. World size controls how much geography fits into each block; elevation is scaled separately to fit the available world height. Additional fine mountain relief focuses on elevations above 3,000 m, with a gradual transition; lower mountains and hills remain represented by the base terrain.

### Restoring natural geography

Earth aims to remove modern human alterations to the landscape as far as the available data allows. Identified artificial reservoirs and their flooded areas are removed, dammed river levels are adjusted toward the natural valley slope, and selected lakes depleted by modern water diversion, including the Aral Sea, are restored toward earlier extents. Natural lakes remain, including those later regulated by dams.

The land connections at Suez and Panama are intact by default. You can enable simplified canals as optional sailing shortcuts when creating a world.

### Rivers and navigation

Rivers follow their mapped courses, but their widths and depths are adapted for gameplay. Minimum widths in game blocks keep channels usable on smaller maps; on larger maps, estimated real widths take over wherever they are wider. River mouths and selected narrow passages, including the Bosporus, are widened and deepened to give ships room to sail. Rapids and waterfalls can still interrupt a route.

### A regional survival world

Trees and shrubs follow botanical regions as well as local climate. Regional substitutes fill gaps where the installed species cannot represent the local forest. Supported Floral Zones ground plants also follow their native ranges; vanilla ground plants retain their climate rules.

Rocks follow global geological maps. Ore distribution uses evidence of real deposits and mining districts, with background deposits in suitable rocks to support survival outside known hotspots. Geological coverage is approximate and varies by region.

Regional animal rules use geographic ranges and habitat checks for vanilla animals and supported optional animal mods. The default natural-range approach represents ranges without human impact, so wildlife can differ from today's distribution. Additional species come from the animal mods you install.

### Climate and weather

Temperature and rainfall vary with location, season and altitude. Earth weather uses monthly climate data to produce wet and dry seasons, monsoons, prevailing winds, moving rain systems and seasonal storms. Latitude also gives the two hemispheres their opposing seasons.

### Real day and night

With **[Astra Terra for Earth](https://mods.vintagestory.at/astraearth)**, the day/night cycle follows both latitude and longitude: sunrise, noon and sunset occur at different times as you travel east or west. Latitude and season determine day length, including polar day and night. Use the Earth-compatible fork for longitude-dependent daylight.

### Vanilla story locations

Earth adapts where vanilla story locations spawn and how their surrounding terrain is generated. Sites can be relocated to suitable ground, their building heights follow the local terrain, and bounded areas are levelled to fit their structures. Their positions and the terrain around them therefore differ from a normal world. The aim is to preserve the game's story and lore while fitting its locations into real geography.

### Starting your world

Earth is a single mod split into three downloads to fit the Mod DB upload size limit: **Earth**, **[Earth Terrain Data](https://mods.vintagestory.at/earthterraindata)** and **[Earth Water Data](https://mods.vintagestory.at/earthwaterdata)**. **All three parts are required** and use the same release version.

Install all three parts, along with Cylinder World and Hydrology. **Sea is strongly recommended and optional.** Create a **new world** using the **Earth** play style, then open **Customize → Earth** to choose your settings.

- **Recommended size:** at least **256,000 blocks wide**, with **512,000 or more preferred** for finer geographic detail. Earth sets the corresponding length automatically.
- **Recommended height:** **512 blocks for the best representation of high mountains**, selected separately on the World generation tab.
- **Starting place:** a random suitable location for your starting climate, one of 36 capital presets, or your own latitude and longitude. A capital preset starts you on nearby dry land.

### Height scaling and configuration

Horizontal and vertical scales are independent. By default, **logarithmic height scaling** gives lowlands and hills readable relief while compressing high mountains into the available vertical space. The default **log 1000 m** curve balances lowland relief and mountain height. Smaller curve values exaggerate lowlands more; larger values leave more relative height for high mountains. Linear scaling preserves the proportions between real elevations.

| Mountain heights option | Effect |
| --- | --- |
| Log 1000 m | Default balanced relief |
| Log 250 m or log 500 m | Stronger lowland and hill relief, with more compression of high mountains |
| Log 2000 m | Less lowland exaggeration and more relative height for high mountains |
| Linear | Heights proportional to real elevation |

At the default sea level of **110**, a 512-high world has about 400 blocks of vertical space above the sea. The 8,849 m Everest reference elevation maps to block height 510. Individual landforms also depend on the resolution of the terrain data and the procedural detail.

The **Earth** tab also provides these creation choices:

| Setting | Default | Choices |
| --- | --- | --- |
| Sea level | 110 | 110 or 128; a higher sea level leaves less room for mountains |
| Summit height correction | From 3,000 m | Off, or minimum named summit elevations from 1,000 to 8,000 m; separate from the fine mountain dataset |
| Rocks and ores | Regional geology and ores | Regional or vanilla rock layers and deposits |
| Animals | Real ranges | Geographic ranges and habitats, or the game's and animal mods' own spawn rules |
| Include extinct animals | On | Allow or exclude supported extinct species; the animal packs supply the species |
| Weather | Earth real-climate weather | Earth or vanilla weather |
| Rain frequency | 1 × real climate | Selected multipliers from off to 3 × the monthly rain chance |
| Storm frequency | 1 × real climate | Selected multipliers from off to 5 × storm frequency |
| Suez Canal and Panama Canal | Off | Optional navigable sea-level cuts; Panama uses a simplified cut without locks |

Other installed suite mods can add settings to the same tab, including seam mist, water flooding, storm waves, wakes, shared charts and exploration by sight. Advanced Earth settings are in `VintagestoryData/ModConfig/earth.json`.

World-generation choices are saved with the world. Terrain updates generally require a new world to see the complete result; already generated chunks keep their terrain.

### Required mods

- **[Earth Terrain Data](https://mods.vintagestory.at/earthterraindata)** — base elevation, high-mountain detail, named summit heights and glaciers.
- **[Earth Water Data](https://mods.vintagestory.at/earthwaterdata)** — river networks, coastlines, islands, lake overlays and shores.
- **[Cylinder World](https://mods.vintagestory.at/cylinderworld)** — world wrapping and polar limits.
- **[Hydrology](https://mods.vintagestory.at/hydrology)** — river and water mechanics.

Install Earth on both the client and server for multiplayer. Use matching releases of the suite mods and install each companion's own required dependencies. Some linked Mod DB pages are still awaiting publication.

### Strongly recommended suite mods

- **[Sea](https://mods.vintagestory.at/sea)** — waves and water rendering using Earth's wind; pre.13 also adds wave-following vanilla boats and wind-driven vanilla sails. Earth runs its own weather without Sea.
- **[Astra Terra for Earth](https://mods.vintagestory.at/astraearth)** — the real day/night cycle by longitude, astronomy, sky and navigation instruments.
- **[World Map](https://mods.vintagestory.at/wmap)** — charts, navigation and distant terrain.

### Recommended ships

- **[Ships](https://mods.vintagestory.at/ships)** — sailing ships and life aboard. **Early release, with substantial further development planned.**

### Optional encounters

- **[Sea Monsters](https://mods.vintagestory.at/seamonsters)** — Kraken encounters at sea; requires Ships and Sea. **Early release, with substantial further development planned.**

### Strongly recommended flora

Install the eight **Floral Zones** packs for vegetation across the world's regions, plus **Wildcraft: Trees and Shrubs** with its required **Herbarium** library:

- [Floral Zones: Cape Region](https://mods.vintagestory.at/floralzonescaperegion).
- [Floral Zones: Caribbean Region](https://mods.vintagestory.at/floralzonescaribbeanregion).
- [Floral Zones: Central Australian Region](https://mods.vintagestory.at/floralzonescentralaustralianregion).
- [Floral Zones: Circumboreal Region](https://mods.vintagestory.at/floralzonescircumborealregion).
- [Floral Zones: Cosmopolitan Region](https://mods.vintagestory.at/floralzonescosmopolitanregion).
- [Floral Zones: East Asiatic Region](https://mods.vintagestory.at/floralzoneseastasiaticregion).
- [Floral Zones: Mediterranean Region](https://mods.vintagestory.at/floralzonesmediterraneanregion).
- [Floral Zones: Neozeylandic Region](https://mods.vintagestory.at/floralzonesneozeylandicregion).
- [Wildcraft: Trees and Shrubs](https://mods.vintagestory.at/wildcrafttree).
- [Herbarium](https://mods.vintagestory.at/herbarium) — required by Wildcraft: Trees and Shrubs.

### Strongly recommended fauna

For a richer regional animal world, install the supported **Fauna of the Stone Age** packs below. Earth applies its geographic range and habitat rules to their supported species. Choose provider versions supported by your Earth release.

- [Bovinae](https://mods.vintagestory.at/bovinae).
- [Caninae](https://mods.vintagestory.at/caninae).
- [Capreolinae](https://mods.vintagestory.at/capreolinae).
- [Casuariidae Plus](https://mods.vintagestory.at/casuariidae).
- [Cervinae](https://mods.vintagestory.at/cervinae).
- [Chelonioidea](https://mods.vintagestory.at/chelonioidea).
- [Dinornithiformes Plus](https://mods.vintagestory.at/dinornithidae).
- [Elephantidae](https://mods.vintagestory.at/elephantidae).
- [Felinae](https://mods.vintagestory.at/felinae).
- [Iniidae Plus](https://mods.vintagestory.at/iniidae).
- [Machairodontinae](https://mods.vintagestory.at/machairodontinae).
- [Manidae](https://mods.vintagestory.at/manidae).
- [Meiolaniidae](https://mods.vintagestory.at/meiolaniidae).
- [Moschidae](https://mods.vintagestory.at/moschidae).
- [Pantherinae](https://mods.vintagestory.at/pantherinae).
- [Rhinocerotidae](https://mods.vintagestory.at/rhinocerotidae).
- [Sirenia](https://mods.vintagestory.at/sirenia).
- [Spheniscidae](https://mods.vintagestory.at/spheniscidae).
- [Thylacinidae Plus](https://mods.vintagestory.at/thylacinidae).
- [Viverridae Plus](https://mods.vintagestory.at/viverridae).
- [Vombatidae Plus](https://mods.vintagestory.at/vombatidae).

### Strongly recommended terrain smoothing

Install these two together:

- [Terrain Slabs](https://mods.vintagestory.at/terrainslabs) — smooths terrain edges into half-block steps.
- [Place On Slabs](https://mods.vintagestory.at/show/mod/38090) — Terrain Slabs' required companion, which offsets plants and placed objects on slabs.

### Optional testing tool

**Devkit** helps with performance measurements and diagnostics during testing. Public link coming soon.

### Useful commands

- `/earth` — latitude, longitude, elevation and climate at your location.
- `/earth trees`, `/earth plants`, `/earth geology`, `/earth ores`, `/earth fauna` — inspect local regional rules and data.
- `/earth tp <latitude> <longitude>` — teleport to geographic coordinates; requires teleport permission.
- `/earth map` — export a map image; requires server-control permission.

### Development and feedback

Earth is under active development. Geographic data has finite resolution, some small features cannot be represented at every world size, and optional mod combinations are still being tested.

When reporting a problem, include your Earth, Cylinder World and game versions, world seed, size and height, latitude and longitude, other installed mods, and relevant logs or screenshots.

### Shared roadmap

Earth and its connected mods have three main goals:

- **Educational geography:** make real landscapes, climates, ecosystems and geological regions recognizable, and improve the geographic data and its representation at game scale.
- **Travel and exploration:** make different regions worth visiting through their landscapes, resources and discoveries, supported by navigation, sailing and life aboard a ship.
- **Respect for Vintage Story lore:** stay as close to the established lore as possible while fitting story locations to Earth's geography and developing a connected side story.

Planned content across the companion mods includes:

- **Earth Ruins:** historically and geographically placed ruins of buildings that existed by the **Diet of Kings in 1318**, with architecture and ruin states reviewed against the lore and setting.
- **Underground Cities:** explorable shelter cities, authored rooms, entrances, discoveries and regular translocators.
- **The Hansa voyage quest line:** a campaign developed through Voyage and the ship, map and content mods, connecting discoveries, navigation, ship progression and journeys between distant places alongside the vanilla main story.

These are development targets for the connected mods. Earth provides the geography, climate and terrain services that support them; the complete linked campaign is still in development.

### Credits

Earth uses geographic and environmental data from NOAA ETOPO, NOAA PSL climate datasets, CHELSA, Natural Earth, GSHHG, HydroSHEDS, the Randolph Glacier Inventory, Kew WCVP, TDWG, PHYLACINE, GeoNames and regional geological sources. Full citations and dataset terms accompany the data. Optional content remains credited to its respective mod authors.

### Contact and development

**You can contact me on Discord: ifeelveryunlucky.**

**Anyone who would like to join forces on development is welcome.** If you're interested in contributing, get in touch!

### AI use and licence

**AI disclaimer:** All code in this mod was created using AI tools. The ideas and design are mine, and I have put extensive work into testing, refining and improving the mod.

Licensed under GPL-3.0.

## Publisher notes

Keep these notes out of the public description.

- **The flora/fauna list has been checked against the author's profile and installed Earth pre.14.** All ten flora/library components were present; all twenty fauna packs were missing and are now installed. The isolated repeat startup loads all twenty with 278 mappings/11 held and zero errors after updating Felinae. [Placement and installation audit](installed-ecology-audit-2026-10-31.md) records the actual support, four excluded domestic-dog variants, exact archives and test limits. This validates startup/mapping, not every species' population or graphical behaviour.

- **Ruins retain the 1318 cutoff.** The author explicitly reconfirmed 1318 during the 31 October roadmap discussion. Include only buildings/fabric that existed by the Diet of Kings; the earlier broad reference to the 14th century does not extend the cutoff. Exact site eligibility, architectural fabric and ruin state still need the existing per-site review.

- **Natural-geography wording follows decisions 0005, 0020 and 0034.** The goal is to undo modern alterations, including artificial reservoirs and water-diversion damage. It is not proof that every modern modification has been removed worldwide. Decision 0034 retains unresolved natural basins and notes that the corrected global reservoir filter still needs a full rebake outside its audited overlay. Older README paragraphs conflict about reclaimed polders, so the public text makes no specific claim that all reclaimed land has been restored. River gameplay widths and ship passages follow decisions 0014 and 0035. The screenshot confirms the visible Earth menu choices; it does not establish acceptance of every server/client behaviour.

- **Select the public release package.** The description now targets the pre.13 development candidate, including the pre.12 play-style/world-options work and the new required-provider enforcement. Pre.13 has a clean build, 35 provider checks, 167 world-option checks and seven dedicated-server scenarios. Broader client/visual acceptance remains separate; main still identifies itself as 0.16.13. No public release version or upload is selected by this draft.
- **Set the release identity.** Choose the public version, replace the author placeholder and confirm game/dependency versions against the final ZIP. A 1.0.0 release decision for Cylinder World does not select Earth's version. Run `tools/Update-ModInventory.ps1` after any manifest/package change and update the relevant suite records when the release status changes.
- **Sea becomes optional in pre.16.** The manifest/startup guard retains Hydrology but no longer requires Sea; Earth supplies its own wind/weather without it. The new candidate passes 35 dependency checks and a native Earth startup without Sea. [Decision 0041](../.claude/worktrees/codex-earth-playstyle/docs/decisions/0041-optional-sea.md) supersedes the Sea portion of the following historical record. **Required water providers were implemented in pre.13.** The manifest requires Cylinder World 0.11.11+, Hydrology 0.1.10+ and Sea 0.4.0+. Earth validates loaded provider contracts before its world-size/datum/terrain initialization; client Sea validation precedes enabled parameters. The [audit follow-up](publication-dependency-audit.md#implementation-follow-up) and [server report](../.claude/worktrees/codex-earth-playstyle/docs/verification-required-providers.md) record the checks, saved-world behaviour and native-loader boundary. The pre.12 ZIP remains preserved.
- **Finish the existing dataset publication questions.** `TREE-DATA-ATTRIBUTION.txt` explicitly records unresolved reuse terms for the TDWG boundary files. The README also records a question about PHYLACINE current ranges derived from IUCN maps. Resolve the existing notes for the datasets actually shipped; avoid copying the older blanket claim that every dataset is freely redistributable. This draft adds no licence determination.
- **Include the code licence in the publication package.** The inspected pre.12 ZIP contains no root `LICENSE`, matching its omission from the project's copied files. Include it and all applicable dataset/source notices when preparing the publication ZIP. The existing candidate ZIP was inspected without modification.
- **Recommendations follow the author's supplied mod list.** The 31 October screenshot includes Earth pre.12, AstraTerra dev.23-earthtest, Hansa dev.16, Wmap pre.14, Cylinder World playtest.6, Sea pre.12, Hydrology 0.1.13, Sea Monsters 0.2.0, the flora set, Terrain Slabs 1.0.17, Place On Slabs 1.2.0 and Devkit. The supported fauna links come from Earth's 20-provider catalogue. Strong recommendation does not claim that every newer provider version has passed client/multiplayer testing. The slab pair has a recorded 1.22.7 server test; client rendering and shore-water behaviour remain outside that test.
- **Geology Additions is excluded from the publication recommendations at the author's request.** Optional adapters remain in source; this wording change does not remove them. Earth's regional geology and ore features still apply without that mod.
- **Use the author's supplied suite links, including unpublished draft pages.** The latest links are `/astraearth`, `/ships`, `/wmap`, `/cylinderworld`, `/hydrology`, `/sea` and `/seamonsters` on the Mod DB. The author classifies Astra Terra for Earth and World Map as strongly recommended, Hansa ships as recommended and Sea Monsters as optional. Public page contents could not be fetched during this update; the URLs are author-supplied destinations, not evidence that the pages are published. Flora/fauna links retain the provider-inventory sources.
- **Real day/night names the Earth-compatible fork.** The public description links Astra Terra for Earth at the author's `/astraearth` draft page. Earth's longitude provider remains conditional on a build that accepts it; ordinary upstream AstraTerra 0.15.0 lacks that hook in the recorded integration checks. The new link does not establish new game acceptance.
- **Keep companion content distinct.** Authored landmarks belong to Earth Ruins, generated shelter cities and regular translocators to Underground Cities, and the campaign remains separate. The copy promises geographic terrain and existing vanilla content, not those companion features.

## Logo

Upload [moddb-logo.png](moddb-logo.png) under **Screenshots**, then select it as the **ModDB logo**. It is exactly **480 × 320 pixels**, one of the accepted sizes supplied by the author (480 × 480 or 480 × 320).

The logo wraps Earth's actual [map render](../preview/global.png) around a cylinder using the existing Cylinder World logo style. Its geography comes from the raster rather than the earlier generated concept. [Source and reproduction notes](moddb-logo-source.md) record the map and commands. A [1440 × 960 master](moddb-logo-master.png) is retained for later use.

## Suggested screenshots

Use real in-game captures from the publication build, with other mods identified in captions where they materially affect the view.

1. A recognizable mountain range or summit, showing the regional terrain and alpine cover.
2. A coastline or island, with a river mouth or continental shelf visible.
3. A major river, rapids or waterfall.
4. Two contrasting regions, such as a temperate forest and an arid river valley.
5. The Earth play style and Customize → Earth options.
6. A Wmap overview as an additional image, labelled as requiring Wmap.

## Verification references

The current draft was checked against these local sources, not inferred from the older main README alone:

- Latest candidate [manifest](../.claude/worktrees/codex-earth-playstyle/modinfo.json), [world configuration](../.claude/worktrees/codex-earth-playstyle/worldconfig.json) and [README](../.claude/worktrees/codex-earth-playstyle/README.md).
- [Earth play style and saved options](../.claude/worktrees/codex-earth-playstyle/docs/decisions/0036-earth-play-style-and-world-options.md) and [regional random starts](../.claude/worktrees/codex-earth-playstyle/docs/decisions/0037-varied-random-starts.md).
- [Production commands, activation and Hydrology negotiation](../.claude/worktrees/codex-earth-playstyle/src/EarthMapSystem.cs).
- [Regional trees and boundary licence notes](../.claude/worktrees/codex-earth-playstyle/docs/regional-trees-implementation.md), [animal-range meaning](../.claude/worktrees/codex-earth-playstyle/docs/regional-fauna-implementation.md) and [terrain data attribution](../.claude/worktrees/codex-earth-playstyle/earthdata/ATTRIBUTION.txt).
- [Suite ownership](../../overview/ARCHITECTURE.md), [weather and river data flow](../../overview/DATA-FLOW.md) and the existing [Cylinder World publication draft](../../Cworld/docs/moddb-page.md).

- Companion link sources: [flora pack lock](../.claude/worktrees/codex-earth-playstyle/modpack/pack-lock.json), [flora/fauna inventory](../.claude/worktrees/codex-earth-playstyle/docs/ecology-mod-inventory-2026-09-28.csv), [supported fauna catalogue](../.claude/worktrees/codex-earth-playstyle/assets/earthmap/config/fauna-species.json) and [slab compatibility test](research/terrain-slabs-test-2026-10-22.md).

- Geography and scale wording: [natural river flow and restored terminal lakes](../.claude/worktrees/codex-earth-playstyle/docs/decisions/0005-river-ends.md), [river widths](../.claude/worktrees/codex-earth-playstyle/docs/decisions/0014-river-widths.md), [pre-canal geography](../.claude/worktrees/codex-earth-playstyle/docs/decisions/0020-canal-gaps.md), [reservoir classification](../.claude/worktrees/codex-earth-playstyle/docs/decisions/0034-arid-drainage-and-reservoirs.md), [ship-clearance straits](../.claude/worktrees/codex-earth-playstyle/docs/decisions/0035-navigable-straits.md), [vertical presets](../.claude/worktrees/codex-earth-playstyle/src/Core/VerticalTerrainOptions.cs) and [conditional longitude daylight](../.claude/worktrees/codex-earth-playstyle/docs/decisions/0010-real-day-and-night.md).

- Shared roadmap and story fitting: [suite campaign roadmap](../../overview/ROADMAP.md), [lore and progression](../../overview/LORE.md), [story site terrain decisions](../.claude/worktrees/codex-earth-playstyle/docs/decisions/0016-story-sites-and-ruin-latitudes.md) and [current StoryPlacer](../.claude/worktrees/codex-earth-playstyle/src/StoryPlacer.cs). The [pre-1318 landmark proposal](../../Earth%20ruins/docs/proposals/landmark-pipeline-1318.md) retains the cutoff reconfirmed by the author; its per-site proposals and review gates remain development work.

- Required-provider implementation: [decision 0038](../.claude/worktrees/codex-earth-playstyle/docs/decisions/0038-required-water-providers.md), [production guard](../.claude/worktrees/codex-earth-playstyle/src/RequiredMods.cs), [pre.13 manifest](../.claude/worktrees/codex-earth-playstyle/modinfo.json) and [verification](../.claude/worktrees/codex-earth-playstyle/docs/verification-required-providers.md).
