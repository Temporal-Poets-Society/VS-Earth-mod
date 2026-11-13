# 0032: Global alpine cover, desert ecology and surveyed summit catalogues

Status: source implemented; offline checks passed; coordinator acceptance and game-assembly validation pending.
The global peak download/bake is pending separately. Date: 2026-10-30. Issues #16-#18, #21, #23.

## Cause and decision

The pre.8 surface pass used chunk-centre climate and block slope, retained too much alpine soil, and its tree
transition began at an 8 C warmest month. Thermal climate alone also missed
the substrate-limited boundary on volcanic cones. Use each column's inverse-mapped real altitude, CHELSA warmest month, physical slope
and RGI glaciers. Tree establishment fades over 10-13 C; shrubs over 5-10 C. Steep or cold exposed ground becomes
rock/scree, with soil pockets on gentle benches. The warm-lowland cliff gate stays; valleys are not stripped.

Round 1's Fuji circle and Sahara box were rejected. Their replacements are global:

- `HoloceneCones.cs` embeds **all 712 terrestrial stratovolcano rows with numeric coordinates/elevation** in the
  [SI/USGS Holocene list adapted by Oregon State University](https://volcano.oregonstate.edu/volcanoes-2), retrieved
  2026-10-30. No selection by play-test site. The complete embedded source is about 49 kB. Latitude buckets avoid scanning the world catalogue per column.
  The upper edifice has a common loose-substrate proxy: fade from 60% to 66% of summit elevation; a cone footprint
  scales with summit height and fades at its edge. Thermal treeline and slope still apply everywhere. This is a
  global approximation, not mapped ash deposits: historical catalogue coordinates and heights are approximate,
  and some Holocene slopes have recovered soil. It is not advertised as the current complete 2026 GVP inventory.
  [GVP's Fuji observations](https://volcano.si.edu/volcano.cfm?vn=283030&vtab=Photos) support the scoria/lava mechanism.
- Hot desert means ambient CHELSA aridity rainfall <70, local warmest month >15 C, and summer-drought score <0.25.
  Rainfall already represents precipitation/potential evaporation (0031); riparian boosts do not reclassify a
  desert. The seasonality condition protects Mediterranean climates. All picker tiers reject temperate stand-ins;
  native range/habitat checks still select supported acacias/tamarisks and native riparian trees. If none fits,
  leave the site empty. Density is reduced, rather than carpeting the upland with its surviving species.
- The non-regional tree path now always supplies actual Lat/Lon/elevation, or chunk-centre geography when no
  position is available. It also applies the native-range veto after its legacy selection, so an African acacia
  is not exported to the Atacama or Australian interior. Native Australian acacias remain available with a pack.
- Keep the accepted vanilla agave/yucca Americas guard. No provider or game assets are removed. *Replaced 2026-11-07
  (ranges proposal, step 3, see History): the guard matched no vanilla 1.22 block; the vanilla cacti are now
  placement-catalogue taxa.*

## Summit data handoff

Both 2-minute averaging and the raw 30-second source lose sharp summits. Direct TIFF decoding found Everest's
highest nearby raw pixel **8353.833 m -> Y500**, versus **8849 m -> Y510**; max-preserving rebaking alone cannot
recover the missing surveyed height. All 15 raw results are in `tests/out/playtest-issues/raw-etopo-survey.csv`;
`tests/RawElevationSurvey` reads the existing raw TIFF without modifying it.

`tools/bake_peaks.ps1 INPUT/allCountries.zip` runs the offline .NET baker (`tools/PeakBake`). It streams the
[GeoNames documented columns](https://download.geonames.org/export/dump/readme.txt): class T, PK/PKS/MT/VLC,
**explicit elevation column 16 (zero-based 15), never DEM column 17**. Missing/invalid elevations are omitted.
Exact-coordinate duplicates retain the highest explicit elevation, then lowest ID; a deterministic 1.4 km
spacing pass suppresses near-duplicate names. Output `earthdata/peaks.json` holds name/lat/lon/elevation and source
attribution (CC BY 4.0). No summit dataset was downloaded in this task.

`PeakCatalog` validates and spatially indexes the optional file. Existing C2 rounded caps and whole-column tops
are retained; support shrinks before another catalogue summit, so caps do not flatten each other's tops. A world
stores the catalogue SHA256 (protobuf 83). A missing file on creation selects the nine fallback peaks, as requested.
Adding a file later cannot silently change that world: the mountain opt-in adopts it. A catalogue-bound world
requires its exact file; a changed/missing catalogue is a data-integrity error, not a rule-version migration.

The following is a **loader/shaping fixture using the 15 reference heights, not the missing production catalogue**.
The fallback column is the actual result with no baked file. All numbers are blocks at 512-high/log1000/512k:

| Summit | Target | Nine-peak fallback | Catalogue fixture |
|---|---:|---:|---:|
| Everest | 510 | 510 | 510 |
| Lhotse | 503 | 503 | 503 |
| Makalu | 503 | 503 | 503 |
| K2 | 505 | 505 | 505 |
| Kangchenjunga | 505 | 500 | 505 |
| Cho Oyu | 497 | 490 | 497 |
| Nanga Parbat | 496 | 499 | 496 |
| Mont Blanc | 417 | 417 | 417 |
| Matterhorn | 407 | 378 | 407 |
| Elbrus | 441 | 444 | 441 |
| Fuji | 383 | 383 | 383 |
| Kilimanjaro | 447 | 447 | 447 |
| Denali | 454 | 454 | 454 |
| Aconcagua | 472 | 472 | 472 |
| Chimborazo | 456 | 456 | 456 |

`review/summit-catalogue-handoff.csv` explicitly marks production results pending. Both axes, +/-6 km at 100 m
steps, are in `review/catalogue-slope-profiles.csv`: largest correction change 207.519 m/100 m (Lhotse), no step
above the 250 m test bound, and no influence outside support. These fixture checks do not establish the accuracy
or completeness of the eventual GeoNames file. The coordinator must inspect its heights, coverage and profiles.

## Before/after evidence

`tools/run-playtest-issues.ps1` writes `tests/out/playtest-issues/report.html`, `before/` and `after/` maps/tables.
225x225-column north-up maps are surface-policy predictions, not game renders. Seed 386217; sea 110; ring 409600,
world width 512000; fine temperature/rain and island/coast overlays. The user's seed was not supplied.

| Map centred on site | Rock+scree before % | After % | Soil before % | After % |
|---|---:|---:|---:|---:|
| Norway 61.6, 8.5 | 39.35 | 54.73 | 40.29 | 24.91 |
| Alps 45.8, 6.9 | 31.90 | 38.05 | 43.09 | 36.07 |
| Fuji summit | 3.52 | 7.99 | 96.48 | 92.02 |
| Santorini control | 0 | 0 | 100 | 100 |

The Fuji map includes surrounding low forest: its summit/upper cone is bare, not the whole map. Full loose-ground
establishment heights: Fuji 2492 m, Etna 2198, Teide/Tenerife 2452, Rainier 2899, Kibo/Kilimanjaro 3891,
Ararat 3409, Klyuchevskaya/Kliuchevskoi 3191. Thermal limits can make lower slopes bare too.
`review/volcanic-cones.csv` checks all 712 entries. Fuji at 2500 m is fully loose ground, at 2000 m it is not.
Chamonix and lower Fuji planting points retain forest potential 1.0; Santorini is not classed as hot desert.

Real pre.8 picker compiled from commit 28817e2: Hoggar at 1400 m gives 81.08% bristlecone pine / 18.92% cypress;
after gives 99.01% acacia / 0.99% dead acacia, before the sparse density veto. Wet-site controls with rainfall
boosted to 120 still have ambient aridity: vanilla pine share Arabia 28.99% -> 0, Thar 9.62% -> 0,
Australia 15.16% -> 0. Vanilla acacia is also rejected outside its native range. Full packs can supply native
Australian acacia. `before/global-tree-picks.csv`, `after/global-tree-picks.csv` and `review/global-desert-trees.csv`
cover all six deserts plus Santorini, three provider modes, including non-regional selection.

## Compatibility and verification

No vertical datum changes. Summits=1 retains pre.8 terrain; new worlds use 2. AlpineSurfaceRules (82) freezes
surface changes. `UpgradeExistingWorldMountainTerrain=true` is a one-shot opt-in for future terrain; old chunks
stay. Non-terrain species fixes follow 0002 and affect only new generation. No game client, installation, push,
staging or commit was performed. The coordinator builds against game assemblies and commits.

Offline: 5066 play-test checks; 169 regional-tree checks (0 bytes/pick); reviewed source/config and EarthParams
compile in the review suite with protobuf attribute stubs; baker CLI fixture retained 4 of 9 rows, rejected DEM-only
and wrong-class/code/invalid rows. Review suite also covers 0033-0035: 3473 checks pass; peak/cone hot paths allocate 0 bytes in 10000 calls each. Game hooks, final vegetation and the actual
global peak catalogue remain unvalidated in the game. Shared overview rows #16-#26 require coordinator updates;
they were read, not edited outside this working copy.

## History

- 2026-11-08 (play-test, 1:10 world on tall.22; branch `claude/alpine-turf`): a ridge at 4,100–4,300 m in western
  Bhutan (27.08 N 89.66 E, June, warmest month about 7–8 °C) was gravel with green 4 × 4-block squares, where alpine
  meadow and rhododendron scrub stand. Rules 1 made about two thirds of gentle ground between the tree line and the
  snow line scree, put soil in hash squares and bared slopes from a rise of 0.45 even in the montane forest. **Alpine
  surface rules 2** (`MountainEcology.Surface2`, `SurfaceVersion` 2; new worlds, or
  `UpgradeExistingWorldMountainTerrain` for future chunks):
  - closed turf from the tree line (warmest month about 10 °C) to about 6.5 °C, thinning to none at 2 °C (Körner,
    *Alpine Plant Life*, ch. 2: the Alps' closed grassland ends at 2,600–2,800 m, the nival belt starts near 3,000 m);
    the snow line is unchanged;
  - dry high mountains keep a fifth of it (Earth's ambient rain below 50, all from 110): alpine steppe on gravel in
    the Pamirs and western Tibet, meadow on the wet eastern plateau;
  - **ground under the last ice sheets** (`global-glaciation`, Batchelor et al. 2019, value `lgm`, blurred over about
    10 km): turf from 4.5 °C, closed only near the tree line, and bare rock from a rise of 0.55 above the tree line.
    This keeps play-test issue #16 (Jotunheimen, "no visible rock") fixed. The soil groups do not separate
    Jotunheimen from Bhutan: Cambisols and Luvisols both. The ice-sheet map does: Scandinavia, Scotland, Iceland,
    the Canadian Rockies and the Brooks Range are inside; the Alps, the Caucasus, the Pyrenees, the Himalaya,
    Colorado and the Sierra Nevada are outside (the data maps ice sheets, not mountain ice fields). Southern ice
    (Patagonia, New Zealand) is not in the data. The set's licence is still to be confirmed before release (see
    `tools/ground/bake_global.py`);
  - bare rock elsewhere from a rise of 0.8 (39°) where turf or forest holds the ground, and from 0.65 (33°, about
    the angle of repose of scree) in the subnival belt. Steep turf above the tree line thins to half towards the rock;
    the forest below keeps its ground;
  - patches follow smooth noise over about 16 and 6 blocks, ranked to a uniform share (`TurfPatch`), not 4 × 4 squares.

  At 1:10 from GLO-90 heights, 2.25 km around each place (scratch preview, rules 1 → 2, % of the map):

  | Place | Rock | Scree | Alpine soil | Snow and ice |
  |---|---:|---:|---:|---:|
  | Bhutan reported | 36.3 → 8.3 | 50.3 → 4.6 | 12.3 → 76.1 | 0 |
  | Jotunheimen, 61.6 N 8.5 E (#16) | 8.7 → 6.0 | 42.6 → 27.1 | 21.9 → 40.1 | 26.8 |
  | Jotunheimen, Leirvassbu | 12.8 → 11.2 | 58.8 → 17.0 | 28.4 → 71.9 | 0 |
  | Hardangervidda | 0 → 0 | 57.2 → 8.0 | 42.8 → 92.0 | 0 |
  | Gornergrat, Zermatt | 39.1 → 12.8 | 21.8 → 9.7 | 7.7 → 46.1 | 31.4 |
  | Tibet east, 34.5 N 100.2 E | 24.6 → 0 | 56.9 → 5.4 | 13.5 → 89.1 | 0 |

  `tests/PlaytestIssuesTests` (1:100) gains Bhutan, both Tibets, the Pamir, the Colorado Front Range and the
  Altiplano, a `v1` mode for comparison and 28 checks of rules 2. 5140 checks pass. Earth's warmest month seems about
  1–2 °C warm in Jotunheimen (its tree line stands near 1,250 m, the real birch line near 1,000–1,100 m), which leaves
  the Norwegian alpine belt somewhat greener than the real one. Not checked in game.
- 2026-11-07 (ranges proposal, step 3, branch `claude/region-index`, not merged): `NativeDesertPlants` and its box
  (60°S–55°N, 170–30°W) are gone. The guard looked for "agave" and "yucca" in `game:` block codes; vanilla 1.22.7 has
  neither, so it never fired. The game's American plants are the saguaro, barrel and silver torch cacti
  (`worldgen/blockpatches/other.json`), which the placement catalogue listed as "controlled elsewhere (0032)" while
  nothing gated them, so they could grow in any hot desert, the Sahara included. They are now ordinary catalogue taxa
  (kind `wild`): WCVP native ranges (*Carnegiea gigantea*: Arizona, California, Mexico Northeast and Northwest;
  *Ferocactus wislizeni* and *F. cylindraceus*: Arizona to Texas and Mexico; *Cleistocactus strausii*: Bolivia) and
  manual climate limits with reasons, set on Earth's own climate (fine rain) at named sites
  (`tests/PlaytestIssuesTests`: saguaro at Phoenix, Tucson, Organ Pipe and Hermosillo, not Prescott, Flagstaff, Yuma,
  El Paso or the Hoggar; silver torch at Tarija, Sucre and Cochabamba, not Riberalta or Villamontes). Worlds placed as
  around 1318 only (`PlacementRules` ≥ 1); with vanilla nature they keep vanilla's climate placement. Not checked in game.

- 2026-10-30 (0.17.0-pre.10, play-test): with the GeoNames catalogue every named low hill got a summit cap, and the cap
  (summit height minus 1,200 m × r²) fell below the ground round a hill of a few hundred metres, so the blend dug a
  ring-shaped moat round it (round pits on the map near Burgas, 42.1 N 27.5 E). Summit rules 3 never lower ground
  except ground standing above the surveyed summit itself. Over 30,940 columns near Burgas: rules 2 lowered 835
  (worst −261 m), rules 3 lowered 33 (worst −15 m, all above a surveyed summit). Saves on rules 2 keep them;
  `UpgradeExistingWorldMountainTerrain` moves them to 3 for future chunks. The 15 reference peaks are unchanged.
- 2026-10-30 (0.17.0-pre.11, play-test): the round caps also made the Himalaya a field of domes with flat tops
  (Everest's top almost flat): a cap that falls 1,200 m × r² is flattest at the top and steepest at its edge, the
  opposite of a peak, and it replaced the ground's own ridges. Summit rules 3 now lift the ground's own shape by what
  the grid lost at the surveyed summit (the ground at the peak's column before any lift), by (1 − r)², sharpest at
  the top and gone at the radius, bounded to −0.3…1.0 × the radius. All 15 reference peaks reach their surveyed
  block; next to each summit the lift steepens the ground by at most 0.76 m per metre (Lhotse; Everest 0.57,
  Matterhorn 0.54). Near Burgas rules 3 lowers no ground at all. pre.10 (raise-only caps) was never released.
