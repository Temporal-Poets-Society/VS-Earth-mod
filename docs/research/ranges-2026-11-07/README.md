# One geography for ranges, climate and habitats (7 November 2026)

Status: **proposal; steps 1–4 implemented** (step 4 committed as 1ba583c; step 5a, dry grass, planned) (7 November, on branch `claude/region-index` from `claude/fauna-1318`; step 1 committed as 4eac881, step 2 as 4b80409, step 3 as 6d2ff13; none merged; offline tests only). The author approved starting step 1 ("Yes, start step 1"). The author (7 November):
- The hard-coded rule keeping polar bears and other land animals out of Antarctica below a latitude "feels arbitrary". Animals already have ranges.
- Unify this as geofences or similar for regions.
- "Asking that we don't end with parallel systems and coded edge cases."
- Also asked: can the biomes and microbiomes already be applied to the whole world? (Answer in the first section: not yet.)

## 1. Can habitats be applied worldwide today? No

| Piece | Coverage now | What makes it worldwide |
|---|---|---|
| Heat, light, water supply (`SiteDrivers`) | **worldwide**, from `climate.bin`, latitude and terrain. Only `/earth site` reads them; nothing spawns from them yet | wire into the spawn rules |
| Zones (boreal, steppe, Mediterranean …) | **not built** | GDD and rain-season planes, zone rules checked against PNV maps ([habitats](../habitats-2026-11-06/README.md), section 1) |
| Soil texture and chemistry | **Baltic only** (ground columns, 53.5–60° N, 20–29° E) | a global soil source in the ground-column work (SoilGrids or HWSD v2, under discussion there) |
| Site wetness | **Latvia only** (the baked dev map) | the water-table model, plus mapped wetlands (GLWD v2, PEATMAP); the aquifer later |
| Habitat classes, cover mixes, tree species by habitat | **Latvia only, hard-coded** in `EarthHabitatOverlay` (DEV) | species catalogue fields (wetness, richness, pH, light) and cover mixes per habitat, written as data |

The dev overlay is itself the kind of parallel system to avoid. It carries its own Latvian species weights next to `RegionalTreeCatalog`. It was built only to see the result in game. The target is for the generic rules to reproduce the Latvia map; the overlay then becomes a test oracle and leaves the build.

## 2. What decides "where" today

Earth has six kinds of "where", written at different times:

| # | Kind | Where in code | Geometry |
|---|---|---|---|
| A | **Region sets** (WGSRPD level-3 botanical countries, 369 regions) | plants (`RegionalPlantCatalog`, `PlacementCatalogue`), trees (`RegionalTreeCatalog`), pack fauna (`RegionalFaunaCatalog`), vanilla fauna 1318 profile | `treeregions.json`, loaded by **two separate lookups** (`PlantRegionIndex` and `BotanicalRegions`), six load sites |
| B | **Range rasters** | vanilla land animals (`VanillaFaunaRanges`, PHYLACINE masks) | its own 360 × 142 Behrmann grid |
| C | **Lat/lon boxes** | chickens' envelope; some pack birds; `NativeDesertPlants.InAmericas` (agave and yucca: 60° S–55° N, 170–30° W); `SpawnSearch` region labels | literals in code and JSON |
| D | **Latitude lines** | vanilla land animals south of 60° S (`AntarcticLandAnimal`, decision 0021); every point south of 60° S counts as region `ANT` (`RegionalFaunaCatalog.RegionAt`); no ruins or villages beyond 70° N / 60° S (config, decision 0016) | constants |
| E | **Climate thresholds in code** | vanilla animals above the tree line (`AboveItsRange`: 6 °C, 0.5 °C for an "alpine" family list, goats exempt); frozen ground | constants per animal family |
| F | **Data coverage** | ground columns (Baltic grid), habitat map (Latvia), regional geology, ore districts | each dataset's own extent |

**Why the 60° S line exists:**
- **Order of work.** Decision 0021 (22 October) came before the vanilla animals had ranges (0031). It was the first fix for goats and polar bears in Antarctica.
- **A data mismatch.** The WGSRPD polygons miss about half of the coastline that Earth's terrain draws in Antarctica (`regional-fauna-implementation.md`). Without the line, points on Earth's Antarctic land would be in no region at all.

Both causes have since gone or can be fixed at the source:
- **Ranges now exist.** Every listed vanilla land animal has a PHYLACINE range, and none of them includes Antarctica. The line still matters only for butterflies (unlisted), and for worlds created with vanilla ranges off.
- **The coastline gap is a geometry problem.** It can be fixed once, in the region index, for every user.

## 3. Proposal: one rule order, one geography

Every living thing Earth places or spawns (tree, ground plant, vanilla or pack animal) goes through the same four steps, using the same data structures:

1. **Range (where it lives, historically)**: the taxon's region set, in the world's range profile (natural, current or 1318). Optional finer masks sit on the same grid.
2. **Climate envelope (can it live here)**: catalogue fields at the place's real elevation. These are the warmest-month minimum, coldest-month minimum and rain range, from data per taxon.
3. **Habitat fit (does this site suit it)**: the site drivers and the habitat class, as a soft weight. Plants already have this proposed in [site-drivers](../habitats-2026-11-06/site-drivers.md). Animals use the same fields, for example polar bear: sea-ice coast; moose: wet forest and fen; reindeer: tundra and lichen pine.
4. **Density**: the cover mix for plants; spawn weights for animals.

No step names a place in code.

### 3a. One region index, conformed to Earth's land

- **One loader.** Merge `PlantRegionIndex` and `BotanicalRegions` into one shared `Regions` service in `src/Core`, loaded once and used by every catalogue.
- **Every Earth land column gets exactly one region.** At bake time, fill Earth's land raster (and coastal sea, for marine animals) with the nearest WGSRPD region. Earth's Antarctic coast then lies in `ANT` because it is nearest to `ANT`, not because of a latitude. The same fill closes the smaller gaps elsewhere: deltas, new coasts from the GLO-90 heights, islands below the polygons' resolution.
- **Coarser groupings come from the same index.** WGSRPD level 1 and 2 (continents, subcontinents) replace boxes such as "the Americas" and `SpawnSearch`'s labels.
- **`/earth where`** prints the region, its continent, the range profile and which datasets cover this point (ground columns, habitat map, geology region, ore district).

### 3b. Ranges as data, the same format for every taxon

- **One range entry format** for plants, trees, vanilla animals and pack animals: a region set; optional finer masks; and per-profile changes (natural, current, 1318 lost and added regions, as decisions 0049 and 0054 already define).
- **PHYLACINE rasters:** they stay as finer masks, resampled once onto the shared grid, so one point lookup serves all.
- **Agave and yucca:** become a region set ("Northern and Southern America", WGSRPD level 1 regions 7 and 8) in the catalogue. `NativeDesertPlants.InAmericas` is removed.
- **Unlisted taxa** (butterflies, other mods' animals):
  - They get a range only if someone writes one. Butterflies get "all regions except `ANT`" as an ordinary catalogue entry.
  - Animals with no entry still pass through steps 2–3, and Antarctica's climate stops them there. Its ice-free coast has a warmest month around 0–2 °C and almost no plant cover.
- **The 60° S constants go:** `AntarcticLandAnimal`, `RegionalFaunaCatalog.AntarcticLatitude` and `RegionAt`'s `ANT` override.

### 3c. Climate envelopes as catalogue fields

- `AboveItsRange`'s hard-coded 6 °C and 0.5 °C, its alpine family list and the goat exemption become per-animal envelope fields in `fauna-vanilla.json`, with the same values, so behaviour stays identical. They then use the same envelope check as trees (`RegionalTreeCatalog`'s temperature limits) and pack fauna.
- Frozen ground (springs, decision 0021) is physical terrain, not a range. It stays as is.

### 3d. What is not a range

| Rule | Keep, move or replace |
|---|---|
| Ruins and villages beyond 70° N / 60° S (0016, config) | **replace** with a habitability rule from the drivers: no structures on ice sheets or where the warmest month is below about 0 °C. Long term, Earth Ruins decides where people built (1318 settlement data). The config limits stay until then, as the fallback |
| Antarctic gutter bridge, south of 60° S (0019, 0029) | **keep**: it is terrain geometry, not a range |
| Dataset extents (ground columns, habitat map, geology, ores) | **keep**: they are data coverage, not geofences. Shown together in `/earth where` |
| Climate stripes and the coast-outline limit at 60° S (0012, 0018) | **keep**: they describe the datasets themselves |

### 3e. Habitats worldwide fit the same order

Habitats (zone × site) are step 3 for everything. Nothing about Latvia, Argentina or Japan is in code:
- **Region sets** say which species exist there (step 1).
- **The zone** comes from climate rules that work everywhere (step 2).
- **Site drivers** come from global data where it exists (step 3).

Where a driver is missing (soil outside the Baltic today), the step uses a neutral value and `/earth site` says so, instead of a regional special case.

## 4. Order of work (proposal)

1. **Shared region index:**
   - one loader; nearest-region fill onto Earth's land at bake time;
   - `/earth where`;
   - remove the `ANT` latitude override.
   - **Check:** every one of 360 Antarctic coast samples resolves to `ANT`; the plant, tree and fauna audits give the same regions everywhere else.
2. **Vanilla animals:**
   - envelope fields in `fauna-vanilla.json` (same values as today's constants);
   - a range entry for butterflies;
   - remove `AntarcticLandAnimal`.
   - **Check:** no vanilla land animal eligible on any Antarctic sample; old and new allowed areas identical elsewhere.
3. **Agave and yucca** as a region set; `InAmericas` removed.
4. **Ruins:** the habitability rule (needs the author's agreement, since 0016 is agreed).
5. **Habitats:** catalogue fields and cover mixes as data ([site-drivers](../habitats-2026-11-06/site-drivers.md)). Animals' habitat fields come with the fauna-by-habitat step.

**Save compatibility (0002):** steps 1–3 change spawn rules, so they go under new rules versions. Existing worlds keep their saved behaviour, as the fauna rules versions do today.

## Step 1 as built (7 November)

Worktree `Earth/.claude/worktrees/region-index`, branch `claude/region-index` from `claude/fauna-1318` (the 1318 animals, also unmerged), because step 1 touches the same files.

- **One region index.** `src/Core/BotanicalRegions.cs` replaces both `PlantRegionIndex` and the old `BotanicalRegions`. The plant tests had already found the two identical on 40,000 points. Every catalogue gets the same instance (`BotanicalRegions.Shared`): trees, ground plants, placement, the fauna packs and the vanilla animals' 1318 ranges.
- **Lookup by surface** (`At(lat, lon, Land | Water | Sea)`):
  - **Land:** a land point outside every polygon takes the first region on rings up to 5°.
  - **Water:** aquatic plants and shore water keep the old 2° search.
  - **Sea:** open sea keeps the 0.08° coast tolerance.
  - **Fauna:** land and coastal sites use the land lookup.
  - **Plants:** a land patch tried over open sea uses the sea lookup, so it never pays for a ring search.
  - This was done at run time on each world's own terrain, not baked as the proposal first said: it follows the terrain the world actually has (GLO-90 or built-in), and needs no new data file.
- **The 60°S region rule is gone:** `RegionAt`'s `ANT` override and `AnyRegion`'s latitude clause. Fauna rules v4 (`fauna-species.json` version 4); worlds use it for future natural spawns, as for every fauna rules update. `AntarcticLatitude` stays only for the vanilla land-animal guard, which is step 2.
- **`/earth where`:**
  - the region at the player and how it was found: inside an outline, within 0.08° of one, or nearest within 5°;
  - the blended region the trees use;
  - the world's range settings;
  - whether regional geology covers the place.
- **Checks (offline):**
  - fauna 972 assertions pass, including Earth's own Antarctic coast at 360 of 360 longitudes for Adélie and emperor penguins, now without the line;
  - plants 1664, trees 169, placement 413, playtest review 3519: all pass.
  - The reports are identical to the baseline except trees: on forest-capable land the old climate fallback drops from 0.1 % to 0.0 %, because land the polygons missed now has a region.
- **Rewritten tests:** the two fauna checks that asserted the old line ("every site south of 60°S is ANT", and penguins at 70°S 170°W, open Ross Sea) now check penguins on the `ANT` outline at seven longitudes and that the open Southern Ocean has no region.
- **Not done:**
  - not run in game; the bench was with another session;
  - WGSRPD levels 1–2 are not in `treeregions.json` yet, which `InAmericas` (step 3) needs;
  - `/earth where` lists only regional geology among the datasets.

## Step 2 as built (7 November)

Same worktree and branch, on top of step 1 (committed as 4b80409).

- **No latitude guard.** `EarthWorldGen.AntarcticLandAnimal`, its family list, the `AlpineAnimals` list and `RegionalFaunaCatalog.AntarcticLatitude` are gone. One check, `OutsideItsRange`, runs from the two spawn prefixes (renamed `WorldgenSpawnRangeGuard` and `RuntimeSpawnRangeGuard`). It tests the animal's range, then its altitude limit, both read from `fauna-vanilla.json`.
- **Altitude limits as data.** `fauna-vanilla.json` is now version 3. Every entry except goats carries `minWarmMonth`: the warmest month at the spawn's real elevation must reach it. Values are the old constants (0.5 °C for sheep, hare, fox, wolf, bear and butterfly; 6 °C for the rest). A test compares all 48 vanilla types against the old family rule in four modes.
- **Butterflies have a range:** every region except `ANT` (`excludedRegions`), looked up with the region index's land lookup. Without the index they stay unlimited, as before.
- **"Chickens everywhere"** used to mean no range, so only the 60°S line kept chickens out of Antarctica. It now means every region except `ANT` (`everywhereExcludedRegions` on the chicken entry; decision 0054 updated).
- **Loading.** World generation always loads the region index with the vanilla ranges and uses the 1318 ranges only when `PlacementRules ≥ 1`. It also loads the file in real-altitude worlds without ranges, because the altitude limits now live there. The placement inventory loads the index too, so butterflies are listed under "vanilla ranges".
- **Checks (offline):**
  - Fauna: 977 assertions pass.
  - South of 60°S: 21,218 land samples (the first coast at every degree of longitude, plus a 0.5° grid). No vanilla type is eligible on any of them, in natural, 1318 and Chickens-everywhere modes.
  - North of 60°S: 14,971 land samples on a 1° grid. Butterflies and Chickens-everywhere chickens are allowed on all of them, so nothing is newly excluded.
  - Plants, trees, placement, playtest review and playtest issues: reports identical to step 1's.
- **Saves:** every release since 1.0.0 has vanilla ranges, so released worlds keep Antarctica empty. Only pre-release development worlds (regional fauna with `VanillaFaunaRanges` 0) lose the exclusion for future spawns.
- **Not done:**
  - not run in game;
  - the Release build's packaging step fails on the branch's existing data-pack version mismatch (1.0.2 vs 1.0.3-rivers.3); the Debug build is fine.

## Step 3 as built (7 November)

Same branch, on top of step 2 (committed as 6d2ff13).

- **The Americas box never fired.** `NativeDesertPlants` looked for "agave" and "yucca" in `game:` block codes. Vanilla 1.22.7 has neither. Agave exists only as a Floral Zones wild crop, which already has its own catalogue range (`agave-tequilana`).
- **The real American plants were not gated.** The game's saguaro, barrel and silver torch cacti were listed in `placement.json` as "controlled elsewhere (0032)", but nothing controlled them. They could grow in any hot desert, the Sahara included (playtest #21's Hoggar).
- **Now they are ordinary catalogue taxa** of a new kind, `wild`, in `tools/placement-taxa.json`, baked by `tools/bake_placement.py`. Each has:
  - a WCVP native range:
    - saguaro: ARI, CAL, MXE, MXN;
    - barrel cactus (*Ferocactus wislizeni*, *F. cylindraceus*): 7 regions, Arizona to Texas and Mexico;
    - silver torch: BOL;
  - manual climate limits with a reason, because whole-region envelopes would admit Arizona's pine plateaus and Bolivia's Amazon lowlands.
  - The limits were set on Earth's own climate (fine rain), not textbook values, because Earth's grid is coarse in the Andes (Cochabamba reads rain 144).
- **No step names a place in code:** `NativeDesertPlants`, its box and EarthPlants' separate patch list are deleted.
- **Scope:** worlds placed as around 1318 (`PlacementRules` ≥ 1), like every catalogue taxon. With vanilla nature, vanilla's climate placement stays, as that option promises.
- **Checks (offline):**
  - placement: 429 checks pass, including the cacti mapped and kept out of the Sahara, Egypt, Australia and Hawaii;
  - playtest regression (`PlaytestIssuesTests … after`): 5,083 checks, 0 failures. They include each cactus on Earth's climate at named sites:
    - saguaro: in at Phoenix, Tucson, Organ Pipe and Hermosillo; out at Prescott, Flagstaff, Yuma, El Paso and the Hoggar;
    - barrel cactus: in at Tucson, El Paso and Chihuahua; out at Houston, Flagstaff and the Hoggar;
    - silver torch: in at Tarija, Sucre and Cochabamba; out at Riberalta, Villamontes, the Hoggar and Tucson;
  - plants, trees, review and fauna: unchanged.
- **Not done:**
  - not run in game;
  - the proposal's WGSRPD level 1–2 index isn't needed for this step (species ranges are finer than continents), so it is still not built.

## Step 4 plan: ruins by where people lived (7 November, for the author's decision)

**Today (decision 0016):**
- `EarthWorldGen.OutsideRuinLatitudes` skips every vanilla worldgen structure north of `RuinsNorthLimit` (70) or south of `RuinsSouthLimit` (−60), both earth.json values read at every start. This covers surface, underground and water ruins, structure-placed translocators, and villages (`WorldGenVillage`).
- In 0016 the author chose these lines as the easiest fix and declined "no surface ruins on ice sheets anywhere".

**What the lines get wrong, on Earth's own climate:**

| Place | Lines | Warmest month at the ground | People by 1318 |
|---|---|---|---|
| Hammerfest, Vardø (70.4–70.7° N) | out | 9.5–11 °C | yes (Sámi, Norse) |
| Utqiagvik (71.3° N), Resolute (74.7° N), Qaanaaq (77.5° N) | out | 3.5–4.4 °C | yes (Thule culture) |
| Greenland ice sheet at 65° N, 1,760 m | in | −2.9 °C | no |
| Antarctic Peninsula coast (Esperanza, King George I.) | out | 0.7–1.5 °C | no |
| Svalbard (Longyearbyen) | out | 6.0 °C | no (found 1596) |
| South Georgia, Kerguelen, Falklands, Azores, St Helena, Mascarenes | in | 2.7–10 °C or more | no |
| Everest, Aconcagua, Kilimanjaro summits | in | −0.6 to −7.5 °C | no |

**Proposed rule, in the same order and format as animals and plants:**
1. **Range: where people lived around 1318.** "Every region except" a list in data. The entry format would be the same as the butterflies' `excludedRegions`, read through the region index. All codes exist in `treeregions.json`:
   - **Never settled:** ANT, SVA, BOU, SGE, SSA, KEG, CRZ, MPE, HMD and MAQ.
   - **Settled only after 1318:** FAL, TDC, STH, ASC, AZO, MDR, SEL, CVI, MAU, REU, ROD, SEY, CGS, ALD, BER, GAL, JNF, OGA, KZN, KER and NFK.
   - **CTM (Chathams):** Moriori settlement is dated about 1500, so the list would include it; low confidence.
   - **Same sources as the chicken range (0054):** that range already names the same uninhabited islands.
2. **Climate: warmest month ≥ 0 °C at the column's real ground elevation.**
   - This means no ice-cap climate (Köppen EF). It is the same `minWarmMonth` field the animals use.
   - It keeps ruins off the Greenland ice sheet interior and the highest summits.
   - It is climate, not an ice mask, so ice-free nunataks and coasts stay eligible.
3. **No latitude anywhere.** `RuinsNorthLimit`, `RuinsSouthLimit` and `OutsideRuinLatitudes` are deleted. Old earth.json keys are ignored with a log note.

**Code:**
- **Data file:** a small `assets/earth/config/people-1318.json` (the region list with sources and the climate limit).
- **Shared reader:** its `excludedRegions` and `minWarmMonth` are read by the same entry reader as `fauna-vanilla.json`. That means a small refactor that moves those two fields out of `VanillaFaunaRanges` into a shared range entry, so there is one format, not a copy.
- **Guard:** `StructureLatitudeGuard` and `VillageLatitudeGuard` become one `OutsideWherePeopleLived` check.
- **Cost:** one region lookup (per-thread cached) and one climate sample per attempt.

**Consequences:**
- **New chunks only:** existing chunks keep their ruins, as with any config change.
- **Arctic gains ruins:** coasts north of 70° N (northern Norway, Alaska's North Slope, the Canadian archipelago, NW Greenland, Taymyr) get vanilla ruins and villages.
- **Islands lose them:** uninhabited islands and ice caps lose ruins, villages and structure-placed translocators. Regular translocators come from Underground Cities and are not affected.
- **Coarse regions:**
  - Islands inside a large region can't be told apart. Franz Josef Land and Wrangel Island fall in Russian regions, so the climate step is all that limits them.
  - The DEM is coarse on peaks; Mont Blanc's top reads 1.9 °C on the grid. In game, the column's own elevation is used.
- **Earth Ruins:** when it decides where people built, it replaces the region list, not the code.

**Checks (planned):**
- Offline: every site in the table above gives the expected answer.
- Offline: no structure is allowed on any ANT sample (the 21,218 from step 2).
- Offline: a 1° land grid comparing old and new, with the changed area listed by region.
- On the bench: a structure-count self-test near Hammerfest and on the Greenland ice.

**Decisions needed:**
1. **Replace the lines with this rule?** The climate step keeps ruins off ice caps, close to the ice-sheet rule declined in 0016. Recommended: yes, because it is climate, not a mask.
2. **Island list:** the full "settled after 1318" list (recommended, it fits the 1318 world of 0047), or only the never-settled polar and sub-Antarctic regions.
3. **Old config keys:** delete them (recommended, no parallel system), or keep them as an optional extra limit, off by default.

## Step 4 as built (7 November)

The author agreed to the plan with the three recommended choices: "Yes. Later i want to remake ruins completely. Also traders spawn in places where they should not and has some arbitrary rules. Like in Antarctica or Greenlan ice sheet."

- **Traders are covered.** Vanilla's traders come from its trader-camp structures (`trader-forest`, `-shallow`, `-hotdry`, `-repurposed`, `-cold` in `worldgen/structures.json`). Both passes, the normal one and the post-pass, place them through `WorldGenStructure.TryGenerate`, the same hook as ruins (checked in the decompiled 1.22.7 `GenStructures`). So the new rule keeps camps off the Greenland ice sheet, which the old lines allowed below 70° N. The 60° S line already blocked Antarctica on this code, so Antarctic traders the author saw came from chunks made before 0.14.8 or from another mod's structures. Vanilla's own per-camp limits (temperature, rain, forest) still apply inside the allowed area; the author plans to redo traders with ruins.
- **Data:** `assets/earth/config/people-1318.json`:
  - 30 excluded regions with notes; `minWarmMonth` 0.
  - Correction to the plan: Kermadec and Norfolk stay settled, since Polynesians lived there around the 14th century (chickens didn't).
- **One format:** `src/Core/RegionRange.cs` holds the shared readers for region lists and `minWarmMonth`, and `PeopleRange`. `VanillaFaunaRanges` now uses the same readers.
- **Guard:** `OutsideWherePeopleLived` replaces `OutsideRuinLatitudes`.
  - It checks the region, then the climate at the ground (`TerrainModel.WarmMonthAtGround`, the same elevation as the forest potential).
  - The seam gutter and the polar filler get no structures.
  - It applies in every Earth world, new chunks only.
  - `RuinsNorthLimit` and `RuinsSouthLimit` are deleted from the config and the README.
- **Checks (offline):**
  - Playtest regression: 5,112 checks, 0 failures. It covers 28 named sites (12 settled, 16 not) and 26,618 land samples south of 60° S, none of which get structures.
  - Against the old lines on a 1° land grid:
    - gained 818: NUN 213, KRA 196, YAK 140, GNL 120, NWT 64, WSB 57, ASK 15, NOR 10, MAG 3;
    - lost 109: 93 on the Greenland ice sheet by climate, plus a few summits and islands (KEG, CTM, GAL by region).
  - Fauna, placement and review suites pass.
  - `EarthWorldOptionsTests` doesn't build in this worktree: it can't find the game's DLLs, a setup issue that predates this step.
- **Not done:** not run in game.

## In game (7 November, AgentBridge bench)

Bench world `regions-s4` (Earth play style 1:100, profile 1318, regional fauna on, "Chickens everywhere" on) with the branch at 1ba583c plus an uncommitted `/earth where` extension. That extension prints the world's own loaded rules at the ground under the player: whether ruins, trader camps and villages may generate; which vanilla animal types may spawn; and which wild catalogue plants may grow. The bench was restored afterwards (Mods and ModConfig byte for byte).

| Site | Structures | Vanilla animals that may spawn | Wild plants |
|---|---|---|---|
| Hammerfest | yes (11.8 °C) | 10, incl. brown bear, arctic fox, moose, caribou | — |
| Utqiagvik | yes (4.4 °C) | 7, incl. polar bear, muskox | — |
| Svalbard | no (not settled) | polar bear, arctic fox, caribou, chicken, butterfly | — |
| Greenland ice 65° N | no (−2.7 °C) | Valais goat | — |
| Antarctic Peninsula, McMurdo | no | none (0 of 48) | — |
| Azores | no (not settled) | chicken, butterfly | — |
| Everest | no (−15.2 °C) | angora goat | — |
| Tucson | yes | 10 | saguaro, barrel cactus |
| Hoggar | yes | 5 | none |
| Tarija | yes | 3 | silver torch |

- **Saguaros and barrel cacti appear around Tucson.** The author saw them next to a trader camp. `/earth census` does not count `wild` taxa yet.
- **The Hoggar has no cacti.**
- **No patch warnings** in the server log.

**Found, not caused by these steps:**
- **Desert grass carpet at Tucson** (author's screenshot): continuous tall grass on `soil-low` (fertility 150) where the Sonoran desert floor is mostly bare. It comes from the soil rule (0048: semi-arid land gets soil) and vanilla's tall grass in the block layers, which fills open ground almost completely (see "Step 5a plan"). This belongs to the ground and habitat work (step 5).
- **Wild 1318 crops in the central Sahara:** flax (247), onion, carrot and fennel within 192 blocks in the Hoggar. This is the same whole-region envelope problem as the cacti: Algeria, Libya and Egypt as regions admit their hyper-arid desert.
- **Goats on ice caps:** goats have no altitude limit (0031: "goats anywhere"). Valais goats may spawn on the Greenland ice sheet and angora goats on Everest. Giving them `minWarmMonth` 0, like the structures, would close it. **Done 7 November** (the author: "fix remaining issues"): goats, the tur and muskox need a warmest month of at least 0 °C at the ground, so none on ice caps or the highest summits; muskox keep the Arctic coasts.
- **"Chickens everywhere" on uninhabited land:** Svalbard and the Azores allow chickens. Intended, the author said: "Chickens-everywhere on uninhabited is tick box as fallback for game while we don`t have birds in all regions where birds would be (Americas - turkey etc)". No change.
- **Cylinder World has its own Antarctic rule.** Its log says "no polar bears or arctic foxes in Antarctica", a second system beside Earth's ranges, in another mod.
- **The story village was placed on the Falklands** (51.6° S 58.8° W). Story sites (0016) don't use the people range.

## Step 5a plan: dry grass by aridity (7 November, for the author's decision)

The author's idea: "We need dry grass for steppe and other dry areas and i was thinking just changing color based on moisture / rainfall similar as vanilla does it for seasons. Also dry areas could maybe have soil cover also with more yellow when out of season and sparce dry grass?" The author then asked for this plan ("Yes, plan it").

### What vanilla already does (1.22.7, read in `C:\VS-ref`)

| Part | Vanilla rule | On Earth today |
|---|---|---|
| **Colour** | Tall grass and the grass on soil use the `climatePlantTint` map. Its row is the block's rainfall and its column the temperature, both read from the world's climate map (`ClientWorldMap.getColorMapData`). The `seasonalGrass` tint browns grass in autumn and winter. | Earth writes real rainfall into that climate map, so dry land is already straw-coloured: the map is yellow to orange-brown below about 0.4 rain and green above it. The yellow at Tucson is this tint. **No change needed.** |
| **Grass on the soil block** | `blocklayers.json`: `normal` from rain 0.33, `sparse` from 0.19, `verysparse` from 0.12. | Steppe and Mediterranean land get `sparse`, wetter land `normal`. **No change needed.** |
| **Tall grass height** | `blocklayers.json` `tallgrass`: very short from rain 0.2, short from 0.35, taller from 0.4 (by temperature). | Semi-arid land gets very short grass, wetter land medium to very tall. **No change needed.** |
| **How much tall grass** | `GenBlockLayers.PlaceTallGrass`: grass wherever noise passes `clamp(forest − max(0, rain × temp − 0.25), 0.05, 0.99)`. With no trees the bar is 0.05. Soil-low (fertility 150) never fails the fertility test. | **About 95 % of open ground on soil gets tall grass.** Since 0048 gave semi-arid land soil, deserts' edges, the steppe and Mediterranean land are carpeted. **This is the gap.** |
| **Grass patches** | `blockpatches/grass.json`: forest-clearing patches, rare huge desert patches (rain 0.1–0.15), savanna patches. | Separate. They belong to the catalogue's next step ("the remaining vanilla patches … on Earth envelopes"), not to this plan. |

What vanilla can't do is **follow the rain through the year**. Its seasonal grass tint turns grass brown in the cold season only, and its strength depends on temperature alone (strong in cold climates, about 0.1–0.2 in hot ones), so a monsoon's green summer and dry winter, or a Mediterranean summer, don't show. The author: "Color my mind should follow rainfall. Many places are lush for few months and then go dry and yellow. Monsoons and also semi-desert area could flower and boom after rain and go barren after." That is steps 5b and 5c below.

### Earth's numbers at named sites (offline probe, fine rain)

| Site | Aridity | Rain (0–1) | Vanilla soil cover | Tallest vanilla grass |
|---|---|---|---|---|
| Phoenix, Hoggar | 0.16, 0.12 | 0.18, 0.15 | very sparse, no soil (sand) | none |
| Tucson, El Paso, Alice Springs, Mendoza, Murcia | 0.22–0.25 | 0.21–0.23 | sparse | very short |
| Chihuahua, Athens, Ulaanbaatar, Kazakh steppe | 0.35–0.40 | 0.25–0.33 | sparse | very short |
| Madrid, Seville, Serengeti, Dodoma | 0.47–0.50 | 0.27–0.37 | sparse / normal | very short / short |
| Ankara, Bamako | 0.56 | 0.30–0.31 | sparse | very short |
| Denver, Crimea steppe, Amarillo, Odesa steppe | 0.62–0.67 | 0.41–0.43 | normal | medium |
| Puszta, Kano, Paris, Pampas, Riga, Flint Hills | 0.79–1.30 | 0.46–0.56 | normal | medium to very tall |

### Proposal: one density factor from the aridity Earth already uses

The rule order stays range → climate → habitat → **density**. This is the first density rule.

- **Rule:** the share of vanilla's tall grass that is kept is a function of the aridity index, the same value 0048 reads (`SoilFertility.Aridity`). Vanilla still chooses where and how tall; Earth only thins it.
- **Data:** knots in a data file (`assets/earth/config/ground-cover.json`, version 1), interpolated in log aridity:

  | Aridity | 0.2 | 0.3 | 0.5 | 0.75 and up |
  |---|---|---|---|---|
  | Tall grass kept | 15 % | 30 % | 70 % | 100 % |

  Below 0.2 there is no soil (0048), so no tall grass, as today. The knots follow the general pattern that plant cover falls steeply as land gets drier than an aridity index of about 0.5 (Berdugo et al. 2020, *Science*, global aridity thresholds; to check before building). They are a look, not a measurement, and are tuned in game.
- **Result:** Tucson, El Paso and Alice Springs about 20 % (scattered very short tufts on sparse grassy soil), the Kazakh steppe and Athens about 50 %, Madrid and Seville about 65 %, Denver and the Crimean steppe about 90 %, and Riga, Paris and the Pampas unchanged.
- **Code:**
  - A prefix on `GenBlockLayers.PlaceTallGrass`. It reads the column's rain and summer drought from the cache `SoilFertilityAt` has just filled for the same column (vanilla calls `PutLayers` → `LoadBlockLayers` before `PlaceTallGrass`). There is no second climate lookup, and the column's position is checked against the cache.
  - The keep/skip choice is a hash of the world position, so a chunk generates the same way twice.
  - Earth worlds only, new chunks only, under a new rules version in the world's saved parameters (like `SoilRules`). Existing chunks keep their grass; old worlds keep vanilla's density unless the version is raised.
- **`/earth where`** prints the aridity and the tall-grass share at the ground.
- **Later, same slot:** habitats (wet hollows, sand, rock, grazing) refine the share by site instead of adding a second grass system. The habitat session's Latvia ground flora is the first user, so tell that session before building.

### Checks

- **Offline:** the site table above with expected shares; Riga, Paris and the Pampas exactly vanilla; nothing changes where 0048 gives no soil.
- **In game (bench):** screenshots at Tucson, the Kazakh steppe, Madrid and Riga in a fresh world, compared with `regions-s4`.

### Decisions for the author (5a)

1. The knots above (recommended) or sparser or denser dry grass. *Built with the knots above ("Yes, build 5a"); to tune in game.*
2. ~~Apply only to new worlds through the rules version (recommended), or to new chunks in existing Earth worlds too.~~ **Decided: new worlds only** (the author, 7 November: "New worlds sure.").

## Step 5a as built (7 November)

The author: "Yes, build 5a". Branch `claude/region-index`, uncommitted, offline-tested only.

- **Data:** `assets/earth/config/ground-cover.json` v1, with knots for the share of tall grass kept by aridity: 0.05 → 0, 0.2 → 15 %, 0.3 → 30 %, 0.5 → 70 %, 0.75 → 100 %.
- **Rule:** `src/Core/GroundCover.cs`.
  - Interpolates in log aridity, using the aridity from `SoilFertility.Aridity` (the same one 0048 uses).
  - `Keeps(x, z, share)` is a fixed hash of the block column, so a chunk generates the same way every time.
- **Hook:** `EarthWorldGen`.
  - A prefix on `GenBlockLayers.OnChunkColumnGeneration` notes the chunk.
  - A prefix on `GenBlockLayers.PlaceTallGrass` skips the tufts that aren't kept.
  - The column's rain comes from the soil rule's cache when it holds this column, otherwise from the climate.
  - The seam gutter and the polar filler keep vanilla's grass.
- **Saved setting:** `EarthParams.GroundCoverRules` (ProtoMember 120). New worlds get 1; older saves read 0 and keep vanilla's density.
- **`/earth where`** prints "Tall grass (new chunks): N% of vanilla's kept (aridity index A)".
- **Checks (offline):**
  - `SoilTests` (160 checks): the knots, monotonic in aridity, the hash keeps its share within 0.5 %, and named sites:
    - Tucson 23 %, El Paso 19 %, Alice Springs 18 %, Murcia 23 %;
    - Kazakh steppe 52 %, Athens 48 %;
    - Madrid 66 %, Seville 69 %;
    - Denver 86 %, Crimea 88 %;
    - Riga, Paris, the Pampas and the Puszta 100 %.
  - The fauna, placement (429), review (3,519) and playtest regression (5,112) suites pass.
- **Not done:** not run in game. Next on the bench: a fresh world, screenshots at Tucson, the Kazakh steppe, Madrid and Riga, compared with `regions-s4`.

## Step 5b plan: grass colour through the wet and dry seasons (7 November, for the author's decision)

**Goal:** grass greens in each place's wet months and turns straw-yellow in its dry months, from Earth's real monthly rain: green winters and yellow summers around the Mediterranean, yellow winters and green summers in the monsoon, Sahel and cerrado, two green seasons in East Africa, and a short green flush in semi-desert.

**Data Earth already has.** `weather.bin` holds monthly precipitation and temperature normals on a 0.5° grid, loaded on both server and client. An offline probe gives the expected seasons (Walter–Lieth dry month: precipitation in mm below twice the temperature in °C):

| Place | Wet months | Dry months |
|---|---|---|
| Seville, Athens, Los Angeles | October–April | May–September |
| Perth (south) | May–October | November–April |
| Kano, Bamako (Sahel) | June–September | October–May |
| Nagpur (monsoon) | June–October | November–May |
| Darwin, Cuiabá (south) | October/November–April | May–September |
| Serengeti, Nairobi | March–May and November | June–October |
| Riga, Pampas | all year | none |
| Tucson, Phoenix, Alice Springs | none by this test | all year (Tucson's July–August monsoon is 50–63 mm: a short flush, see below) |

**Blocker found:** `weather.bin` has **no December precipitation over most land** (75,959 of 88,207 land cells read 0 mm; Riga's November reads 58 mm and its December 0). Coastal cells, where the ocean data is blended in, are fine (Athens 102 mm, Darwin 258 mm). The bake fills UDel's missing months (`fill_month_gaps`), but the shipped file still has the gap. It also affects Earth's weather today (little or no December rain or snow inland) and possibly `climate.bin`'s yearly totals. It must be fixed before 5b, and belongs to the weather data, not this step.

**How (proposal):**

- **The greenness of a month:** each month's precipitation against its evaporative demand from temperature, carried a month forward (soil water lags the rain), with vanilla's winter browning kept for cold months. Computed on the client from `weather.bin` per place. No new data file.
- **Seasons in local terms:** the game's `seasonRel` is already per hemisphere (`Calendar.GetSeasonRel(playerPos)`). So "dry in summer" is the same pattern north and south, and a few patterns cover the world. Baked offline into **8 patterns** (clusters of the 12-month curves), drawn as one Earth colour map: x = time of year, one row per pattern, alpha = how far the dry-season colour replaces the climate tint.
- **Choosing the pattern per block:** a client patch on `ClientWorldMap.getColorMapData` (it already gets the block and its position). For grass and soil grass it selects Earth's map and writes the place's pattern into the 3 spare bits grass doesn't use (vanilla uses them only for leaves' per-tree shade).
- **Rendering:** vanilla's colour-map shader include gets a small inserted branch at load, for Earth's map only: the row comes from those 3 bits instead of noise, and the weight from the map's alpha instead of temperature. The text is inserted at load at a known line (no copy of vanilla's shader is shipped). If the line isn't found (a game update, or a shader mod), Earth logs it and grass keeps vanilla's colour, which is still dry by aridity.
- **Scope:** grass first (tall grass and grass on soil). Ferns, bushes and drought-deciduous trees (teak, the cerrado, monsoon forest) can use the same map later.
- **Existing worlds:** colour is client-side and not saved, so it applies to every Earth world as soon as the mod is updated. An Earth setting turns it off.
- **Risk:** this is the first time Earth touches rendering. Shader mods (such as Volumetric Shading) patch the same files. It needs an in-game check with and without one.

## Step 5b as built: grass colour through the wet and dry seasons (7 November)

**Simpler than the plan: no shader is changed.** Vanilla colours grass from a rainfall byte per block (`ClientWorldMap.getColorMapData`) through its own plant tint, which is straw at low rainfall and green at high. So Earth replaces only that byte, with the rainfall of the time of year. The 8-pattern colour map, the spare bits and the shader insertion are not needed, and shader mods are not touched.

- **Model:** `src/Core/GrassSeason.cs`.
  - Each place gets a yearly curve of 24 half-month points from `weather.bin`: precipitation against twice the temperature (the same Walter–Lieth line as the zones' dry months), carried about a month forward for soil water.
  - Months below about 5 °C count as not dry, because vanilla's season tint browns dormant grass.
  - The curve is the log of that wetness minus its yearly mean, capped at ±2.
  - The yearly rainfall vanilla already reads becomes an aridity index, which is scaled by the curve and turned back into rainfall (`SoilFertility.RainFromAridity`, the inverse of the 0048 mapping). So the yearly average colour is unchanged.
- **Client:** `src/EarthGrassSeason.cs`.
  - A postfix on `getColorMapData` for blocks tinted with `seasonalGrass` (grass on soil, tall grass). Leaves, ferns and bushes keep vanilla's colour.
  - Curves are cached on a 32-block lattice and blended between its corners.
  - Chunk meshes keep the colour they were built with, so all blocks are drawn again when the half-month changes, twice a game month.
  - Every Earth world with weather data gets it, existing ones too, because nothing is saved. `ModConfig/earth-client.json` `SeasonalGrassColour: false` turns it off.
- **Offline (ZoneTests, rainfall byte by month, January to December):**

  | Place | Yearly | January → December |
  |---|---|---|
  | Seville | 71 | 116 122 109 103 82 43 15 15 39 84 114 104 |
  | Perth | 85 | 33 37 43 65 112 142 148 147 133 102 68 39 |
  | Kano | 122 | 37 36 38 73 144 202 235 235 235 176 98 55 |
  | Darwin | 195 | 235 235 235 235 205 120 81 81 113 178 235 235 |
  | Phoenix | 45 | 67 90 83 49 19 11 35 49 48 47 53 40 (the July–August monsoon flush) |
  | Riga | 136 | 117–148 all year |

  Checks pass for Seville, Athens, Perth, Kano, Nagpur, Darwin, Bamako and Phoenix (wet month greener than dry month by at least 30) and for Riga (green all year).
- **Known:**
  - The weather data's December gap makes December look drier inland in warm places until the rebake from the December session lands. Cold places are not affected (months below 5 °C are neutral).
  - Earth's humid tropics' inflated yearly aridity keeps Darwin's dry season greener (81) than the Sahel's (37).
  - The curve is read at sea level, so a dry-climate mountain's slope gets the seasons of its lowland.
- **Not done:** not run in game. To check there:
  - the colour against vanilla's at Seville in July and January, and Kano in March and August;
  - the cost of the redraw twice a game month at a large view distance;
  - with a shader mod (none is needed, but the redraw must not fight it).

## Step 5c plan: ground moisture, blooms after rain (7 November, for the author's decision)

**Goal (the author):** "semi-desert area could flower and boom after rain and go barren after", and "special plants in dry areas would also spawn only when there is enough moisture in ground".

- **Ground moisture without saved state:** Earth's weather is a deterministic function of place and time (`WeatherModel.Sample(x, z, hours)`, the same on server and client). So the ground moisture at a place is the rain of the past weeks, read back from the weather and decaying with evaporation. A storm in the Sonoran desert wets the ground there and nowhere else, and it dries out over the following weeks. It is computed on demand and needs no new save data.
- **What it drives:**
  - **Blooms:** dry-land annuals (catalogue taxa of kind `wild`, flagged `ephemeral`, using vanilla's existing flower and grass blocks, no new blocks) appear on soil by the soil's random ticks while the ground is wet enough, and die back when it dries. The range, climate envelope and habitat still decide *which* plants may appear; moisture decides *when*.
  - **Grass flush:** tall grass on dry land grows back during wet spells, through vanilla's own soil growth (`BlockSoil` grows a tuft when its grass stage rises), and thins again in the dry season, down to the 5a share.
- **Rule order:** range → climate → habitat → density (5a) → **timing (5c)**. One moisture value, used by grass colour (5b) for the climatology and by blooms (5c) for actual rain, so there is no parallel system.
- **Scope:** only loaded chunks change (where players are), as with vanilla's grass growth. Persisted blocks, new worlds only (rules version).
- **Open:** which vanilla flowers count as desert or steppe annuals (poppies, lupines and others; to list from the catalogue), and how fast blooms come and go (days to weeks, at the world's day length).

## Step 5c as built: ground moisture and the grass flush (7 November)

- **Ground moisture:** `src/Core/GroundMoisture.cs`.
  - The decay-weighted share of the past 12 game days it rained, read back from Earth's deterministic weather every 3 game hours. Heavy rain counts fully, light rain in part.
  - The ground keeps 1/e of a rain after 3 game days, about a third of a game month, as a real dry spell's ~10 days in the game's 9-day months.
  - Nothing is saved; a storm wets only the ground it passed over.
- **The grass flush:** `src/EarthGroundMoisture.cs`, server.
  - It applies only on dry land, where 5a keeps part of vanilla's tall grass.
  - The columns 5a keeps (`GroundCover.Keeps`) hold their tuft all year. On the others, a short tuft grows while the moisture is at least 0.10 and withers below 0.03.
  - Months below 5 °C are dormant: no flush and no die-back.
  - Driven by vanilla's idle random soil ticks: a postfix on `BlockSoil.ShouldReceiveServerGameTicks` decides off the main thread, and `OnServerGameTick` sets the block. So only loaded land changes, like vanilla's own grass growth.
  - Moisture is cached per 64-block cell and 6 game hours (a reading costs about 0.26 ms offline).
- **Saved setting:** `EarthParams.MoistureRules` (ProtoMember 122), new worlds only; it needs 5a's ground cover rules too. `/earth where` prints the moisture now.
- **Checks (`tests/WeatherTests`):**
  - Deterministic.
  - Kano 0.31 in August and 0.00 in February.
  - Seville 0.02 in July and 0.11 in October.
  - A year's probe gives Phoenix wet spells in both its winter and its monsoon rains.
- **Not done:**
  - Blooms of dry-land annuals came with step 6 (below).
  - Not run in game.

## Habitats step 1 as built: climate zones (7 November)

The author: "Then do not done things in order and let me know when all is done. Then we merge into last version at the time as still height and water is being ironed and ore and ground layers are being improved." The remaining biome work is being built in order on `claude/region-index`, starting with zones (the habitats proposal's step 1). Each step is committed locally on that branch and nothing is pushed. Merging into the latest version comes once all steps are done.

- **Data:** `assets/earth/config/zones.json` v1, with 22 zones (20 at first; the spruce-fir and continental temperate zones came with the tree mixes below). Each zone is a soft envelope over the drivers plus a forest and shrub density.
  - **Zones:** ice; tundra; forest-tundra; boreal; humid spruce-fir; hemiboreal; temperate; continental temperate; oceanic; warm-temperate humid; humid summer-dry forest; sub-Mediterranean; forest-steppe; steppe; subtropical scrub; Mediterranean; semi-desert; desert; savanna; tropical dry forest; rainforest; tropical highland grassland.
  - **Thresholds:** after Prentice et al. 1992 (growing degree days, coldest month), Köppen (frost-free tropics), UNEP aridity classes and the ~10 °C warmest-month tree line. They are tuned on Earth's own climate at named places.
- **Drivers:** `src/Core/ClimateZones.cs` `ZoneDrivers`.
  - Growing degree days above 5 °C come from a sine through the coldest and warmest months, so no new data is needed.
  - Coldest and warmest month, aridity (the same as 0048 and 5a) and dry summer come from climate.bin.
  - **Dry months:** Walter–Lieth, counted smoothly from `weather.bin`'s monthly normals, read bilinearly so forests get no grid edges.
  - Everything is at the ground's real elevation, so mountains pass through the same zones as latitude, with no montane special cases.
- **Weights:** the product of logistic steps at each limit, normalised, so borders blend.
- **Density:** the zones' forest and shrub densities by weight replace the single rain curve in `TerrainModel.ForestPotential` and `ShrubPotential`.
  - The river green belt through dry land and loose volcanic ground still apply.
  - The tree line (`TreeSuitability`) and the hot-desert rule are now part of the zones (tundra, forest-tundra, desert), so they are no longer separate rules in zone worlds.
- **Saved setting:** `EarthParams.ZoneRules` (ProtoMember 121). New worlds only; older saves keep the rain curve.
- **`/earth where`:** prints the top zones with their weights, the densities and every driver.
- **Checks (offline, `tests/ZoneTests`, 58 checks):**
  - Named places all land in their expected zone: tundra to rainforest, Mediterranean on five continents, Sahel, monsoon and cerrado savanna, Davos in montane forest, Churchill at the tree line, Everest on ice.
  - Only 31 of 88,430 land cells (0.04 %) fall between envelopes.
  - Land shares: savanna 13.5 %, ice 10.6 %, boreal 8.4 %, rainforest 7.1 %, desert and semi-desert 12.6 %.
  - Every other suite passes unchanged.
- **Changes against the old curve:**
  - Mediterranean woodland and maquis now grow: Madrid, Athens and Los Angeles forest 0.33–0.40, was about 0.02; shrubs 0.75–0.9.
  - Sahel and monsoon savannas open up: Kano 0.15, was 0.87; Darwin and Nagpur 0.15, were 1.0.
  - Churchill is forest-tundra (0.30, was 0.97).
  - The Kazakh and Mongolian steppe have fewer trees (0.08–0.09, were about 0.2).
  - Humid forests are almost unchanged (0.8–1.0).
- **Known data issues:**
  - The Odesa steppe is too wet in Earth's climate (aridity 0.67), so it comes out temperate forest. The test reports it as a warning; the habitats proposal had already flagged it ("check the suspicious rain cells").
  - The dry months use `weather.bin`, whose December precipitation is missing over most land (see 5b). The tropics' dry-month counts must be rechecked once that fix lands.
- **Not done:**
  - Tree species by zone: done next, see below.
  - Calibration against a potential-natural-vegetation map: Hengl et al. 2018 or RESOLVE need a download, which the author has not approved.
  - Not run in game.

## Habitats step 2 as built: tree mix by zone (7 November)

- **What changed:** in zone worlds, the zones that set the forest density also set the tree mix. The coded climate weights of `RegionalTreePicker.ForestTypeFactors` (Mediterranean, boreal, hemiboreal, broadleaf, montane by elevation, hot-dry) are no longer used there. One system decides both how much forest and which kind.
- **Data:** `zones.json` gains `treeMixes`, a weight per forest-type group of the tree picker (other, fir, pine, larch, birch, oak, maple, walnut, arid). Each zone names its mix.
  - The values are the pre-zone tables, including the Latvia- and Finland-tuned hemiboreal and boreal ones.
  - New mixes: forest-steppe (oak, pine, birch), sub-Mediterranean, tropical dry (fewer thorn trees than savanna), and rainforest (no forest type, so the native species keep their own weights, with no thorn trees).
  - A mix only shifts weight among the species a region has; it never adds a species.
- **New zone, humid spruce-fir forest (`montaneconifer`):** cool and very humid (aridity ≥ 1.8, coldest month ≤ 1 °C, 700–1950 degree days).
  - This replaces the old "montane from 450 m" elevation rule with climate.
  - It covers humid mountains (Black Forest, Vosges, Alps, Carpathians, Caucasus and Appalachian crests at their real heights), humid coasts, and the dark taiga of Quebec, Labrador, the Urals, Altai and Sikhote-Alin.
  - Boreal and hemiboreal now stop at aridity 1.8. The drier pine, larch and birch taiga and the Baltic lowlands (aridity about 0.8–1.2) keep their mixes.
- **Temperate broadleaf against hemiboreal (the audit's "beech does not show in Germany"):** Earth's growing degree days hardly separate central Germany (1609) from Riga (1577), but winter cold does: about −1 °C against −4 °C. The border now follows a coldest month near −2.5 °C, about beech's eastern limit. Temperate broadleaf has two zones with one tree mix:
  - **Temperate:** short summers, mild winters (from 1350 degree days, coldest month above −2.5 °C, warmest above 15.5 °C, so hyperoceanic Ireland and western Norway stay oceanic). Covers Denmark, central Germany and Bohemia.
  - **Continental temperate:** long summers, winters down to Prentice's −15 °C summergreen limit. Covers the US Midwest, Ukraine and Manchuria.
  - Latvia east of Kurzeme, Lithuania and Belarus stay hemiboreal; Kurzeme and Bohemia are mixed.
- **Hemiboreal mix recalibrated on Latvia's inventory**, because zones always add up to a full mix, while the coded weights left a neutral remainder at climate borders. Fir/spruce 0.8, pine 2.1, birch 1.5.
  - Latvia, vanilla trees with the regional catalogue (report `tests/RegionalTreesTests -- . --report --latvia --zones`): pine 32 %, birch 29 %, spruce 17 %, other 22 %.
  - Inventory: pine 34 %, birch 31 %, spruce 18 %.
  - Coded weights: pine 41 %, birch 27 %, spruce 17 %.
- **Species report with zones (`--spots --zones`, every provider):**
  - Central Germany: oaks 45 %, beech 16 %, Norway maple 15 %, birch 8 %. The coded weights gave birch 17 %, maple 15 %, spruce 14 %, pine 11 %, beech 6 %.
  - Upper Rhine and central France: oak and beech led.
  - Beech shares the oak forest type, so beech above oak needs species weights in `regionaltrees.json`, not zones. The wildcraft beech rule lists the Baltic States, which is right at region level: WCVP's "Baltic States" includes Kaliningrad, where beech is native. A winter-cold limit would also remove the Carpathian beech belt (about −6 °C in January at 900 m), so beech's north-eastern edge needs a per-species continentality limit (warmest minus coldest month), splitting the generic beech entry by species. Not done.
- **Checks (`tests/ZoneTests`, 80 checks):**
  - Riga, Helsinki and Arkhangelsk stay pine-led, then birch, then spruce (the Latvia and Finland oracle).
  - Central Germany and Denmark are broadleaf; Latgale and Belarus are hemiboreal.
  - The Black Forest (1000 m), Alps (1500 m) and Caucasus (1800 m) are spruce-fir led.
  - Paris, Chicago and Atlanta are oak-led.
  - Athens and Madrid are pine and oak.
  - Kano, Alice Springs and Phoenix are thorn and dry trees.
  - Manaus is neutral.
  - Land between envelopes fell from 31 to 13 cells.
  - Regional trees 169, regional plants 1664, soil 160, placement 429 and review 3519 checks pass. The plants suite's speed check failed twice while other sessions held the CPU at 96 %, then passed.
- **Changes against the coded weights:**
  - Tropical savannas and rainforests no longer get the temperate broadleaf mix (oak ×5, walnut ×5 at Kano and Manaus).
  - Chicago, Kyiv, central Germany and Denmark go from pine-led or mixed to broadleaf.
  - Humid mountains go from pine-equal to spruce-fir led.
- **Older worlds:** unchanged. The coded weights stay for worlds without zone rules.
- **Known data issue:** the Tatras at 1200 m come out forest-tundra, because Earth's climate cell there is too cold for its height. This is a climate-data matter, not a zone matter.
- **Not done:** not run in game.

## Habitats step 3 as built: crops on arable land (7 November)

- **Why:** the catalogue's wild and feral crops (`kind: crop`) were gated by botanical country and a climate envelope only. Algeria, Libya and Egypt reach deep into the Sahara, so their crops reached it too.
- **Rule:** each zone in `zones.json` can say `"arable": false` (ice, tundra, semi-desert, desert). A crop patch grows where arable zones hold at least half the weight, or in a river's green belt (`RiparianWeight` ≥ 0.5: the Nile, oases). Wild taxa are not affected. Worlds without zones count every place as arable.
- **Code:** `ClimateZones.ArableShare`, `PlantGate.ClimateAllows(..., arable)`, `EarthPlants.Arable` (cached per 8×8 cell).
- **Census:** `EarthPlacement` now counts every catalogue-mapped block as mapped, wild or crop; only unmapped crop and berry blocks count as unmapped.
- **Checks:** `tests/ZoneTests` (the Nile Delta's climate alone is not arable; the river belt handles it in the model).

## Step 6 as built: vanilla flowers in the 1318 catalogue, blooms after rain (7 November)

- **Flowers as taxa:** `tools/placement-taxa.json` gains 19 `wild` taxa for vanilla's flowers, with WCVP native ranges (baked by `tools/bake_placement.py` into `placement.json`):
  - catmint, cornflower, forget-me-not, edelweiss, heather, field horsetail, desert globemallow (vanilla's orange mallow), oxeye daisy (wild daisy), western gorse, cow parsley, California poppy (golden poppy), lily of the valley, woad, redtop, bluebell, ghost pipe (all three colours), wild daffodil, mugwort, lupines (every colour; the North American and Mediterranean species).
  - Medieval spread from Preston, Pearman and Hall 2004 (archaeophytes): cornflower in Britain (with cereal fields since the Neolithic) and woad across northern and central Europe (grown for dye).
  - So in catalogue worlds, vanilla's flower patches follow the same order as every other plant: range, then climate envelope. Bluebells stay in western Europe, ghost pipe in North America and East Asia, the California poppy in California.
- **Ephemeral annuals:** a taxon can carry `"ephemeral": true` (`PlacementCatalogue.Taxon.Ephemeral`). Now: the California poppy and the cornflower.
- **Blooms after rain (`src/EarthGroundMoisture.cs`):** on dry land, a quarter of the tufts that step 5c's wet spells grow are the place's ephemeral annuals instead, where one's range and envelope (with the lapse rate) admit the column. They die back with the grass below moisture 0.03. Only in worlds with both the catalogue and moisture rules.
- **Checks:** `tests/PlacementTests` 511 (lupines mapped; ephemeral flags; poppy in California and not in Spain in 1318; cornflower and woad in England; bluebell not in Russia; ghost pipe not in Germany). Regional plants 1664, zones 107.
- **Saved rules:** `PlacementRules` stays 1, as with step 3's cacti: the branch has not shipped. A world made with an older v1 catalogue gets flower ranges in new chunks only.
- **Not done:** all 20 of vanilla's flower types are mapped; ferns, mushrooms and herbs are not, and reeds and other wetland plants wait for the wetland data. Not run in game.

## Zones recalibrated on the game's temperatures (7 November, after the merge)

- **Found:** the zone tests read Earth's 0.25° grid temperatures, but new worlds read CHELSA's 2′ temperatures (climate rules 2, decision 0009). With CHELSA, Riga's coldest month is −3.0 °C, not about −4 °C, and the soft −2.5 °C border gave it a 21 % broadleaf share, so oak rivalled pine. The tests now load the data as new worlds do: CHELSA temperatures, fine rain and the climate fix.
- **The Tatras were never wrong in game.** With CHELSA, 1200 m is spruce-fir and Kasprowy Wierch (1987 m) is alpine tundra; Zakopane's July is 15.5 °C (real about 15). The 0.25° grid's fixed lapse rate made them too cold only in the tests.
- **Hemiboreal against broadleaf by yearly mean temperature.** No single month separates them on CHELSA: Denmark (coldest month +0.9 °C) and Lviv (−2.9 °C, beech and oak) are broadleaf, Stockholm (−2.1 °C) and Riga (−3.1 °C) are hemiboreal. A yearly mean near 7.7 °C separates all of them: broadleaf Denmark, Skåne, Gdańsk, Kaliningrad 7.9, Warsaw 8.3, Lviv 8.1, Bohemia; hemiboreal Białystok 7.5 (the transition), Liepāja 7.4, Kaunas, Riga 6.9, Vilnius, Stockholm 6.4, Tallinn, Minsk. New zone driver `tMean`; the temperate zone keeps winters down to −15 °C.
- **Forest-steppe border at aridity 0.75.** Earth's Holdridge evapotranspiration runs low in hot steppe summers, so its index reads about 0.15 high there (Odesa 0.67, real about 0.5; the December session checked that the data gap is not the cause). Odesa, Kherson, Chișinău and Saratov are forest-steppe; Kyiv, Vinnytsia, Bucharest, Belgrade and Vienna broadleaf.
- **Checks:** zones 115 (land between envelopes 10 cells), regional trees 169, soil 160, review 3519 pass. Riga, Helsinki and Arkhangelsk pine-led again.
- **Still open:** the tallgrass prairie (Des Moines, Kansas City) and Winnipeg's aspen parkland come out forest. Their prairies were kept by fire, not drought, so climate alone gives forest there. Earth's aridity reads them 0.9–1.3.

## Bench check and fixes before tall.17 (7 November)

The author: "fix remaining issues and then on play test on next big zip also with others".
- **Fixed on release/1.0:**
  - goats, the tur and muskox need a thawing summer (d40ab81);
  - zones on CHELSA temperatures (42c7353);
  - savanna from 4 dry months, for the complete December data (559af94);
  - three bench finds (0632486, merged as 058215d): grass dormancy used the weather's sea-level normals (the Serengeti read 31 °C), edelweiss was allowed in lowland Germany and Ukraine, and cow parsley in the Serengeti. Both plants now carry manual alpine and montane limits with reasons.
- **Bench (AgentBridge), 1:100 Earth world, test zips `earth-1.0.3-tall.16.play.1` and `.play.2`** (branch `claude/playtest-big`: release/1.0 plus `claude/udel-december`; not for release):
  - The startup log shows zone rules v1 (22 zones), ground cover v1, ground moisture v1 with 2 annuals that bloom, grass season on, and the placement catalogue with 69 taxa. No errors.
  - `/earth where`:
    - Riga: hemiboreal 100 %, pine 2.1, birch 1.5; flowers native or medieval only.
    - Kassel: temperate broadleaf 98 %, oak 5.0.
    - Odesa: forest-steppe 95 %, 90 % of the grass kept.
    - Serengeti: savanna 60 %, dry forest 40 %; gazelle, warthog and hyena.
    - Mojave: semi-desert, 7 % of the grass kept; saguaro, barrel cactus and desert globemallow.
  - Not seen on the bench: the grass colour through the seasons and blooms after rain (those need game time), and the play.2 re-check, cut short to hand the bench to tall.17. The tall session made that re-check on tall.17 (world t17b, 1:100). Kassel: temperate broadleaf 98 %, no edelweiss. Serengeti: savanna 60 %, dry forest 40 %, no cow parsley. Both moisture lines read the month's mean at the ground. Startup without errors.
- **In tall.17** (`claude/tall-glo90` 75e177d, built by the tall line session): release/1.0 7edb1c7, which includes all of the above, ground columns (off by default) and the date tools, plus the December fix (`claude/udel-december` a37fc76) and the tall line's lake outlines.
- **Still open:**
  - EarthMapTests' "4M dalmatia" check: it already failed on bbd00c0, and nobody has looked at it.
  - The tallgrass prairie and Winnipeg come out forest.
  - Beech's north-eastern edge needs a continentality limit.
  - Species by soil, now that ground columns are on release/1.0.
  - Wetlands, which need the data downloads.

## Step 7: shrubs and animals by habitat (7 November)

What the zones already give, and what waits for the site drivers.

- **Shrubs:** vanilla's shrubs go through the same tree picker as its trees (`EarthTrees`, `genShrubs`), so since habitats step 2 they take the zone's tree mix: maquis-style oak and pine in the Mediterranean, birch and pine in the hemiboreal, thorn and dry shrubs in the semi-desert. Shrub density comes from the zones since step 1. Nothing new was needed.
- **Animals, forest and open land:** vanilla's animals gate on the forest and shrub density at the spawn position (`ClimateSpawnCondition` in 1.22.7: wolf `minForest` 0.5, fox 0.5, raccoon 0.25, gazelle `maxForest` 0.1, sheep and pig 0.4–0.5, goat up to 0.5, moose `minForestOrShrubs` 0.35, hare and deer by variant). In zone worlds those densities come from the zones, so the animals follow them with no Earth rule of their own: one system decides the forest and who lives in it.
- **Checks (`tests/ZoneTests`, 114):** with vanilla's density noise scaled as Earth does (`EarthWorldGen`), the gates admit wolves and moose but not gazelles in Arkhangelsk, Riga and the Black Forest; gazelles but not wolves or moose in the Kazakh and Mongolian steppe; gazelles only in Riyadh and the central Sahara.
- **Observation, vanilla's own rule:** wolves need forest density 0.5, so they don't spawn on open steppe, where real wolves live. Earth leaves vanilla's gates as they are.
- **Waits for the site drivers (blocked):**
  - Shrub and tree species by moisture and richness (juniper on dry poor ground, hazel on rich, willow and alder on wet), tree size by productivity: need soil richness and a water table. The ground-column session answered (7 November): `Core.GroundColumns` on `claude/ground-columns` (6d810e5, not yet on release/1.0) gives each column's WRB soil group, clay, pH, cover recipe (till, loess, peat, alluvium) and Quaternary depth, and `Core.GroundRelief` gives hollow against crest. Richness would come from the soils table per WRB group, wetness from relief and cover. In game it loads only when Rocks and ores is set to ground columns; that session offered a read-only accessor for any setting. This step can start once that branch is on release/1.0.
  - Wetland habitats and their animals and plants: need the wetland data (GLWD v2, PEATMAP downloads, not approved).
  - Animals with habitat fields beyond forest and open (wetland, rocky, riverine): come with the wetlands.

## Merged (7 November)

All of the above (steps 1–7 and habitats steps 1–2) merged into release/1.0 at Tall.16 as e67fc73 through branch `claude/biomes-1.0`: each file merged three-way on normalised line endings, keeping release/1.0's. Not pushed. Offline suites pass on the merged tree; EarthMapTests' one failure (4M Dalmatia coast steepness) is the same on release/1.0 before the merge. Still to do: in game on the bench; recheck 5b and the tropics' dry months once the December precipitation fix (`claude/udel-december`) lands; species by soil once `claude/ground-columns` is on release/1.0.

## Order (proposal)

1. Fix December precipitation in `weather.bin` (weather data owner).
2. 5a: dry-grass density (decided: new worlds only).
3. 5b: seasonal grass colour (client, the shader branch).
4. 5c: ground moisture and blooms.

## Decisions for the author

1. Replace the 60° S animal rule with ranges plus the shared region index, as above (recommended); or keep it as a deliberate backstop.
2. Unlisted animals from other mods: no range limit unless someone writes one, with climate and habitat still applying (recommended); or a default "everywhere except Antarctica".
3. Ruins: a habitability rule from climate (recommended), or keep the 70° N / 60° S lines until Earth Ruins.
4. Start with step 1 now, while the soil and ground work continues in the other sessions.
