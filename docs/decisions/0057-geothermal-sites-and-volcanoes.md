# 0057: Hot springs and lava from real geothermal sites; volcanoes

Status: part 1 (geothermal rules 1) is in test build 1.0.3-tall.19 (`eed43a3` on `claude/tall-glo90`, not pushed),
checked offline and on the bench, installed in the author's game, not played yet. Part 2 (volcano bodies, geothermal
rules 2) is in test build 1.0.3-tall.20 at the author's request (`57219f9`, with release/1.0 merged), checked offline and
on the bench. Both parts are merged into release/1.0 (`03dddab`, 8 November, not pushed) and installed in the author's
game as combined test build 1.0.3-tall.21, not played yet. New worlds only. Date: 2026-11-07.

## What the author asked

7 November 2026:
- "Hot springs should be only in places with hot springs".
- "Lava - now sometimes it spawns out of mountain side or randomly inside rock. We need to make proper volcanoes, with
  magma chamber etc. Ash and active volcanoes could have lava lake inside. Later we can add eruptions."

## Cause

Vanilla keeps one "geologic activity" byte per chunk in the climate map's low byte. It draws it as noise
(`NoiseClimateRealistic`, (random/255)^20 x 255 at the default strength 0.05, so a few per cent of the map is over 128).
Earth's climate layer (`EarthClimateLayer`) replaced temperature and rain with real values but passed this byte
through. Vanilla (1.22.7, `C:\VS-ref\decompiled\1.22.7\VSEssentials`) makes three things from it:
- Hot springs (`GenCaves` 420-434, `GenHotSprings`): a deep, wide cave segment (y -5 to 16, radius over 4) under a
  chunk whose byte is over 128 marks a hot spring. The next pass builds it on the surface above, if the ground there is
  dry land above sea level with less than 4 blocks of relief.
- Lava springs (`GenRivulets` 89-124): 500 x activity / 255 x (map height / 32) tries per chunk. Each places lava in a
  cliff face or cave wall at y up to activity / 2 x map height / 256 + 10: up to y 520 in a 1024-high world. These are
  the lava streams coming out of mountain sides.
- Cave-bottom lava (`GenCaves` 435-494): cave cells below y 12 are lava up to y = activity x 16 / 128, basalt above
  that.

So hot springs and lava came wherever the noise was high, with nothing to do with real geology.

## Part 1: geothermal rules 1 (implemented)

`EarthParams.GeothermalRules` (ProtoMember 104) is set to 1 for new worlds; older saves keep 0 and vanilla's behaviour.

- **Hot springs only where real ones are.** Vanilla's caves make no hot springs any more (the byte is never over 128,
  below). Each known hot spring and geyser is marked in its own chunk for vanilla's hot spring generator
  (`EarthWorldGen.KnownHotSprings`), the same way vanilla's caves mark theirs (`hotspringlocations`, radius 4: a pool 16
  blocks across with boiling water in the middle). Vanilla still decides whether the ground takes it: dry land above
  sea level, less than 4 blocks of relief across the pool, no structure there. A spring that would overlap a pool already
  marked in its chunk is left out: at coarse scales one chunk holds a whole spring field.
  - 5,234 from OpenStreetMap: 4,715 `natural=hot_spring` and 519 `natural=geyser`, after merging duplicates within 100
    m and leaving out 39 tagged below 25 °C or named as cold-water geysers (Andernach's is driven by gas).
  - 287 from GeoNames (SPNT, GYSR) with no OpenStreetMap spring within 1 km.
  - Geysir, Strokkur, Old Faithful and Yellowstone's basins are among them.
- **Deep lava only under real volcanoes.** From rules 1, `Core.Geothermal.Activity(lat, lon)` replaces the climate map's
  byte. It is the most heat any volcano of the Smithsonian Global Volcanism Program's Holocene list (1,214) gives there,
  falling linearly with distance, and 0 everywhere else:

  | GVP volcanoes | Count | Heat at the vent | Gone at |
  | --- | --- | --- | --- |
  | Active: an eruption dated in the Common Era | 676 | 128 | 30 km |
  | Holocene: dated before the Common Era, or credible without a date | 410 | 128 | 20 km |
  | Uncertain ("Evidence Uncertain", "Unrest / Holocene") | 128 | 96 | 10 km |

  The byte only decides cave-bottom lava now. At 128 every cave cell below y 12 is lava, at 64 those up to y 8, so the
  deep lava thins out away from the vent. It is capped at 128 because over 128 vanilla does both things at once: random
  hot springs, and lava in every deep cave floor. Hot springs from it would put magma under spa towns with only deep warm
  water (Bath, Budapest).
  - An eruption after 1318 counts as active: the system was alive then. Only vents born after 1318 are wrong
    (Parícutin 1943); the GVP lists them inside older fields.
  - GeoNames' own volcanoes are not used: no ages, and long-dead ones such as Hohentwiel (about 7 million years). The
    Oregon State list of Holocene stratovolcanoes (`HoloceneCones`) stays for ecology only: the GVP list covers it.
- **No lava springs.** Earth's spring guard skips vanilla's lava springs. Vanilla makes them last in each chunk, so the
  water springs stay where they were. Open lava is left for real volcanoes (part 2).
- **What stays vanilla:**
  - Water springs only above y = activity / 2 x map height / 256: right at a volcano, none below a quarter of the
    world's height. That is y 256 in a 1024-high world, which can be above its sea level; it falls to 0 where the heat
    does.
  - In tall worlds the rock floor ([0050](0050-tall-worlds.md)) fills caves deep below the surface, including their
    lava, as before.

Files:
- `src/Core/Geothermal.cs`.
- Generated by `tools/bake_geothermal_sites.py`:
  - `src/Core/GeothermalSites.cs`: GVP and GeoNames.
  - `src/Core/GeothermalSitesOsm.cs`: OpenStreetMap, kept apart as ODbL data.
- Sources, downloaded with the author's permission on 7 November 2026:
  - `rawdata/gvp-holocene/Smithsonian_VOTW_Holocene_Volcanoes.csv` (1.9 MB, GVP web feature service).
  - `rawdata/osm-geothermal/osm-hot-springs-geysers.json` (2.1 MB, Overpass API; the query is beside it).
  - `rawdata/geonames/allCountries.zip`, as before.
- `TerrainModel.Pixel`.
- `EarthWorldGen.SpringGuard` and `KnownHotSprings`.
- `EarthMapSystem`: new worlds, the client model, and a warning for newer rules.

Tests: `EarthMapTests` `CheckGeothermal` and `CheckGeothermalPixel`.

Licences: the OpenStreetMap list is a derivative database under ODbL 1.0 (© OpenStreetMap contributors). Publishing
the mod publishes that list under ODbL; the rest of the mod is not affected. The GVP list is cited as the Smithsonian
asks; GeoNames is CC BY 4.0. All three are in `earthdata/ATTRIBUTION.txt`.

### Known gaps

- OpenStreetMap's coverage is uneven: the American West and Anatolia are mapped densely, other places thinly.
- Some spa springs come from wells drilled after 1318.
- A spring by a river or on a slope gets no pool: vanilla's ground check refuses it.
- Yellowstone is not on the GVP Holocene list (its last eruption is older), so it gets geysers but no deep lava.

## Part 2: volcano bodies, geothermal rules 2 (implemented)

The author, of the proposal: "download what is needed and build it. 1318 is not the game date. Game happens unknow time
after 1318 so we don`t need to be super precise", then "i assume 100-200 years later ?". New worlds save
`GeothermalRules` 2 (no new save field); rules 1 worlds (tall.19) get no bodies. Part 1 stays as it is.

**Which volcanoes.** The GVP's volcanic mountains get a body: composite cones, shields and calderas (825), not its
volcanic fields (Eifel, Chaîne des Puys) or minor vents, and not the uncertain ones.
- **Active in the game's time** (332 of them): the game is some 100–200 years after 1318, so about 1420–1520. A confirmed
  eruption starting 1300–1700 counts (the GVP's Holocene eruption list, downloaded for this). So does a volcano with
  at least five confirmed eruptions since: the record before 1700 is thin away from Europe, Japan and Indonesia, and a
  volcano that busy now was busy then. The eight lava-lake volcanoes are active whatever their record.
- **Dormant**: the other Holocene ones (Shasta, last eruption about 1250; Elbrus).

**The body** (`Core.VolcanoPlan`, planned once per world from the world's own terrain model, so every chunk agrees). Sizes
are real metres, kept to a few blocks at coarse scales.

| Part | Active | Dormant |
| --- | --- | --- |
| Vent | The middle of the highest ground within 1.2 km of the GVP's place: a crater's rim around it, or a peak | Same |
| Magma chamber | A lens of lava, 800 m half-width (calderas 1.2 km), 300 m half-height, in 2 blocks of basalt | A lava core 0.6 of a 500 m lens; the rest basalt |
| Chamber depth | 5 km under the summit (calderas 4 km, shields and lava-lake volcanoes 2.5 km) | Same |
| Conduit | Lava, 20 m radius (at least 1 block), in a basalt sleeve, up to 3 blocks of basalt crust at the vent | Solid basalt, up to 4 blocks under the surface |
| Crater | In cones and shields on dry land: 200 m radius, 100 m deep, lined with scoria; a flat floor and a wall up to the rim | None |
| Fresh ash | Earth's tephra over soil, gravel, sand or rock within 2.5 km of the vent, 3 blocks at the vent, thinning to a ragged edge | None |

- **Depth on the world's scale.** Above sea level a depth is on the land's height scale (`VerticalScale.Y`). Below sea
  level it uses the land's blocks per metre at sea level, but at most 10 km between sea level and the world's bottom.
  The chamber always keeps 8 blocks of rock over it and 3 under it.
- **Tall worlds.** The chamber is placed after the rock floor ([0050](0050-tall-worlds.md)) is filled, so a chamber
  below the floor stays.
- **Danger.** Mining into a chamber or conduit lets the lava out, by design, with no safety guards. The chambers and
  conduits are sealed in basalt, as vanilla seals its cave lava, and are not lit until opened. Submarine volcanoes get a
  chamber and conduit under the sea floor, no crater and no ash.

**Lava lakes.** The eight volcanoes with persistent lava lakes in the historical record have one in their crater: Erta
Ale, Nyiragongo, Kīlauea (Halemaʻumaʻu, set apart from the GVP's place, which is the caldera's middle), Erebus, Masaya
(Santiago), Villarrica, Ambrym (Marum) and Nyamulagira.
- Each has its own lake and crater size: lake radius 15–150 m, crater 100–600 m, 80–250 m deep.
- The lava stands below the crater's rim and below every column next to it, so it can't spill. The lake's floor and the
  rock beside it, from 2 blocks under the floor up to the lava's top, are made solid basalt (vanilla's caves may have
  opened them).
- Its top layer is lit, as vanilla lights the cave lava that is open to air.
- The conduit's lava joins the lake from below.

**Craters.** A crater's edge strays up to 15 % of its wall's width from a circle, in three slow waves around the vent
with phases from the volcano's GVP number; the lake and the rock next to it stay round. A crater and its rim are hot
ground: Earth's guards on vanilla's ponds and springs skip them (`EarthVolcanoes.InCrater`). Without that, vanilla's
ponds filled Etna's and Vesuvius's craters and springs ran down Nyiragongo's walls towards its lake.

Blocks: vanilla's still lava (fluid layer), `rock-basalt`, `rock-scoria`, and Earth's `tephra` from the rocks and
ground work ([0056](0056-rocks-and-ground-apart-from-ores.md); without it there is no fresh ash). Earth's ground
columns already lay tephra where the soil maps show andosols (volcanic ash soils).

**Not in rules 2:**
- Eruptions (later, as the author said).
- Glow or heat at dormant volcanoes.
- Pumice: there is no block.
- Lava flows on the slopes.
- Crater lakes of water.

Files:
- `src/Core/Volcanoes.cs`: the catalogue and `VolcanoPlan`.
- `src/EarthVolcanoes.cs`: world generation (terrain pass: ash, craters, lava lakes; vegetation pass: chambers,
  conduits).
- `src/Core/Geothermal.cs`: rules version 2.
- `tools/bake_geothermal_sites.py`: GVP number, landform, elevation and game-time activity.
- Source, downloaded with the author's permission: `rawdata/gvp-eruptions/Smithsonian_VOTW_Holocene_Eruptions.csv` (2.2 MB,
  GVP web feature service, 11,089 eruptions).

Tests: `EarthMapTests` `CheckVolcanoes` and `CheckVolcanoPlans`.

## History

- 2026-11-07: part 1 implemented on `claude/tall-glo90` after the author's report; part 2 written as a proposal.
- 2026-11-07: with the author's yes ("Yes, download both"), the GVP Holocene list and OpenStreetMap's hot springs and
  geysers replace the stratovolcano list and GeoNames' volcanoes; hot springs come only from known sites, and the
  activity is capped at 128 so that no lava comes under spa towns. Still rules 1: nothing was built or played before.
- 2026-11-07: with the author's yes ("Yes, build tall.19"), test build 1.0.3-tall.19 as `eed43a3` (Earth's zip only,
  31.6 MB; still requires Earth Water Fix Data tall.17). On the bench (a new 1:10 world, 1024 high, no errors in the
  log), Strokkur and Old Faithful each have a pool 3 blocks below the site, with heat beside it at Strokkur. Within 160
  blocks there is no lava on the surface at Hekla and no hot spring or lava at Riga.
- 2026-11-07: installed in the author's game at 21:16 at their request ("Yes, install tall.19"); tall.18 backed up.
- 2026-11-07: part 2 agreed and built as geothermal rules 2 ("download what is needed and build it"; the game some
  100–200 years after 1318). The GVP's Holocene eruption list downloaded (2.2 MB); the proposal's defaults kept: all
  eight lava lakes, open lava only in active volcanoes, no glow at dormant ones.
- 2026-11-07: test build 1.0.3-tall.20 (`57219f9`): part 2 with release/1.0 merged (`8461b52`: ores 0056, stone colours
  0058, Earth's tephra block). Earth's zip only, 31.9 MB; still requires Earth Water Fix Data tall.17. Suite at 256: all
  825 plans sound, only Dalmatia's old failure. On a private bench (a new 1:10 world, 1024 high, its own data folder;
  no errors in the log), the log reports rules v2 with 825 bodies, 332 active and 8 lava lakes:
  - Kīlauea: a lava lake 15 blocks across at y 206 under a rim at 215, glowing at night, with tephra around. Open lava
    runs down the vent to the chamber at y 93–99.
  - Nyiragongo: a lake 12 blocks across, 26 blocks under the rim, with the conduit from y 206.
  - Etna and Vesuvius: dry craters, with a 3-block basalt crust over the conduit's lava.
  - Shasta (dormant): no crater; a basalt plug from the chamber up to 4 blocks under the summit.
  The first build's craters held vanilla ponds (Etna, Vesuvius) and springs (Nyiragongo), and their walls were perfect
  circles. Both were fixed before this build; see Craters.
- 2026-11-08: merged into release/1.0 and installed at the author's word ("we will install when it is merged"; "Commit
  work and merge as main manager"). Combined test build 1.0.3-tall.21 (`03dddab`, release/1.0 fast-forwarded, not
  pushed): the tall line with the dams taken out (0053 item 19), the ores in their layers (0060) and the water table
  (0059). Suite at 256: all checks pass. On the private bench (a new 1:10 world) Etna's chamber, its conduit's lava up to
  y 430 and the basalt crust over it are intact after the ore layer pass; no Earth errors. Installed at 01:31 with
  tall.19 moved to `Backups/earth-before-1.0.3-tall.21-20261108-013116`.
