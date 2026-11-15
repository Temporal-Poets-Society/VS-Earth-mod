# 0047: One placement catalogue for flora and fauna, with audits

Date: 3 November 2026. Status: **Agreed** in direction (see Author's decisions); the first slice built, in Earth 2.0.0 (see [Implementation](#implementation-first-slice)); animals by 0054 and vanilla flowers and cacti by the ranges work. One shared catalogue format (step 2) and the GBIF audit (step 5) remain **proposed**, not built.

## Author's decisions

The author, 3 November, answering this record:

- **"one placement catalogue was in my mind that does not depend on mods"**: Earth keeps one placement catalogue that
  does not depend on any content mod. Mods only supply assets mapped to it.
- **"yes world as till 14th century"**: the 1318 rule of [0049](0049-flora-ranges-as-of-1318.md) applies to the
  whole world, animals included, not only plants.
- **"Player toggle in Earth configs to vanilla and just per climate"**: players can switch this off in Earth's
  settings, back to vanilla placement that goes by climate alone.
- **"Yes, opt-in, off"** (asked whether to keep mammoths and other animals extinct before 1318 as an opt-in extra):
  the **Include extinct animals** option stays, but now defaults to **off**, and it covers only taxa that were
  extinct before 1318: Pleistocene megafauna such as mammoths, cave lions, woolly rhinos and sabre-tooths, plus
  early island dwarfs. Animals that were still alive in 1318 belong to the 1318 world and spawn without the option,
  within their 1318 range. That follows directly from the agreed rule, and includes the aurochs, moa, Steller's sea
  cow, Falkland wolf, Tasmanian emu and baiji. The option changes only new natural spawns, as it does today.

## Problem

Content mods come and go. Earth uses vanilla, eight Floral Zones packs, Wildcraft Trees and twenty Fauna of the Stone
Age packs today, and may switch to better providers later. Placement must stay correct when that happens, and correct
means regional, not only climatic: a Mediterranean climate in California must not bring olives. We also need a way
to audit what actually gets placed.

The [regional world architecture](../regional-world-architecture.md) (29 September) already set the boundary: Earth
owns stable taxon IDs and the geography, providers only map assets to taxa, and small adapters connect the two. It was
built as three parallel pipelines:

| System | Ranges | Climate | Checks |
| --- | --- | --- | --- |
| [Trees and shrubs](../regional-trees-implementation.md) | WCVP native, TDWG level 3 | Earth envelopes from the native range | 169 tests, 27 probes, `/earth trees` |
| [Floral Zones ground plants](../regional-plants-implementation.md) | WCVP native, TDWG level 3 | The pack's own rules, on vanilla's scale | 1,664 tests, `/earth plants` |
| [Fauna packs](../regional-fauna-implementation.md) | PHYLACINE natural, TDWG regions, boxes | Provider limits read at Earth's ground climate | 950 assertions, `/earth fauna` |

## Where it leaks

1. **Paths nobody controls.** About 188 vanilla worldgen patches (berry bushes, flowers, mushrooms, ferns, grasses,
   aquatic plants, wild crops, reeds), the fruit-tree type choice and all vanilla animals run on vanilla's climate
   rules. Only decision 0032's American desert plants are an exception. *(Correction, 3 November 2026: vanilla land
   animals are not uncontrolled. Since [0031](0031-mountain-height-playtest.md) they follow PHYLACINE present-natural
   ranges, `fauna-vanilla.json`, in worlds with real animal ranges. Butterflies, fish and lore creatures have none.)* Nothing reports these paths, which is how
   the olive problem shipped ([research](../research/fruit-trees-2026-11-03/README.md)).
2. **Two climate scales.** Trees use real-climate envelopes. Ground plants and animals keep providers' thresholds,
   which were tuned for vanilla's climate. The fauna coverage scan already lists 27 taxa whose range has no sample
   passing the provider's limits.
3. **Species chosen inside one block.** A fruit tree picks its type inside `BlockFruitTreeBranch`; a patch gate
   cannot reach that choice.
4. **Three catalogue formats.** A provider swap means a separate re-bake and review for each system. Crops are
   climate-only by design, which [0049](0049-flora-ranges-as-of-1318.md) now reverses.

## Proposal

Four layers. Only the last two depend on a content mod or a game version.

### 1. Site context (Earth)

Evaluated at the actual placement position, through `EarthGeometry.ToGeo`:

- the botanical region (TDWG level 3) and the zoological range cell;
- real climate: yearly mean, coldest and warmest month, rainfall and summer drought;
- elevation;
- habitat: sea, fresh water, saline lake, coast, floodplain, forest share, and later substrate.

Most of this exists in `TerrainModel`, `EarthTrees` and `EarthFaunaSystem`. The work is to share one implementation.

### 2. Taxon catalogue (Earth, independent of providers)

One entry per real taxon, keyed by a stable ID and the source IDs (WCVP, PHYLACINE, GBIF). It holds:

- **ranges by profile.** `natural`; `1318`, which is native plus dated introductions established by 1318
  ([0049](0049-flora-ranges-as-of-1318.md)); and `current` where a source exists. Each introduction record carries its
  region, the date or period, and its evidence. WCVP marks records only as native or introduced, without dates, so
  the 1318 additions are curated. Candidate sources include standard histories of plant domestication and crop
  diffusion, cited per record.
- **habitat** needs.
- **a climate envelope** derived from the range on Earth's own climate grid, as regional trees already do. An
  override needs a stated reason.
- **status:** wild, cultivated, archaeophyte, or extinct with its period.

Example shape (illustrative, not a schema):

```json
{
  "id": "taxon:olea-europaea",
  "sources": { "wcvp": "...", "gbif": "..." },
  "kind": "tree",
  "ranges": {
    "natural": { "tdwg3": ["GRC", "KRI", "TUR", "LBS", "..."] },
    "1318":    { "add": [ { "tdwg3": ["..."], "since": "antiquity", "evidence": "..." } ] }
  },
  "habitat": { "terrestrial": true, "summerDryTolerant": true },
  "climate": { "derived": true, "override": null }
}
```

### 3. Provider mappings (one file per provider version)

Each file maps provider assets (generator, block patch, block variant, entity code) to taxa, with the evidence for
each match. A provider's non-climate preferences such as density and open or closed stands are kept. Its
climate thresholds are replaced by the catalogue's envelope. Mapping files record the provider version and its
SHA-256. Switching to a better pack means writing a new mapping file and re-running the audits; the catalogue does
not change.

Overlaps are resolved explicitly. When two providers offer the same taxon, one is preferred, and duplicates share a
single density budget. An installed provider's unmapped asset follows a strict or permissive policy and is always
reported.

### 4. Placement adapters (one per game path)

Every adapter asks the same evaluator one of two questions: "may this taxon go here?" or "pick among these
candidates here".

| Path | Hook |
| --- | --- |
| Tree generator pick | Existing `EarthTrees` |
| Block-patch gate | Existing `EarthPlants` prefix on `BlockPatch.Generate`, extended to vanilla patches |
| Forest floor and trunk patches | Existing transpiler in `EarthPlants` |
| Choice inside one block | New, starting with fruit trees: a prefix on `BlockFruitTreeBranch.TryPlaceBlockForWorldGen`, which chooses the type from the candidates allowed here and keeps vanilla's fertility check |
| Animal spawns at worldgen and runtime | Existing `EarthFaunaSystem`, extended to vanilla animals if the animal profile is decided |

As the architecture record says, exactly one system owns each stage. Two independent filters must not intersect into
an empty region.

## Audits

1. **Report on uncontrolled paths (startup and offline).** Enumerate every worldgen block patch, tree generator,
   in-block choice (fruit-tree conditions) and entity spawn config. Classify each as controlled, climate-only by an
   agreed decision, or uncontrolled. Uncontrolled organisms are warnings; a release requires the list to be
   reviewed. Non-organisms (stones, shells, meteorites, lore content) are excluded explicitly, not silently.
2. **Eligibility maps per taxon (offline).** Combine range, climate envelope and habitat on Earth's grid for each
   enabled taxon and render a global map. Tests:
   - **Feasibility:** every enabled taxon has eligible ground, as fauna already checks.
   - **Expected presence:** Greece allows olive, Aleppo pine and holm oak; the Cape allows proteas.
   - **Must be absent:** no olive or wheat in the Americas, no maize or potato in Europe, no maple in Africa.
   - **No empty land:** every forest-capable region gets trees, as regional trees already checks.
3. **Comparison with GBIF occurrences (offline, never shipped).** For each taxon, measure the share of its native
   (and, for the 1318 profile, historically cultivated) records that fall in eligible ground, and the eligible area
   with no records. This turns a hand-picked probe list into a measured error rate. Filter GBIF for cultivated and
   modern-introduction records before scoring.
4. **Census in the running game.** For example `/earth census <radius>`, plus a headless-server script at the probe
   sites, counting what was actually placed in generated chunks by taxon and by path. Blocks and entities are counted
   however they got there, so the census also catches paths the startup report missed.

## Player setting

One choice in Customize → Earth, saved with the world as other world-generation choices are
([0036](0036-earth-play-style-and-world-options.md), [0002](0002-save-compatibility.md)):

| Choice | Behaviour |
| --- | --- |
| **World around 1318** (default) | Every adapter uses the catalogue's 1318 ranges, habitats and Earth climate envelopes, for plants and animals. |
| **Vanilla, by climate** | Earth's adapters stand aside. Vanilla and the installed mods place by their own climate rules, read on Earth's real climate. Earth still supplies terrain, climate and weather. |

The existing **Animals** option (real ranges or the game's own spawn rules) and the separate regional-trees and
regional-plants switches would fold into this choice. Whether the setting also keeps separate plant and animal
controls is decided at implementation; the default and the vanilla fallback are agreed.

## Order of work

1. The uncontrolled-path report and the census. They are cheap and measure the real gap.
2. The shared catalogue format, with the existing tree, plant and fauna catalogues moved into it without changing
   their behaviour. The existing test suites must pass unchanged.
3. Fruit trees as the first in-block adapter, then vanilla patches including wild crops, under the 1318 profile.
4. Ground plants switched to Earth envelopes, then fauna, including vanilla animals, on 1318 ranges.
5. The GBIF audit.

Each step is checked against the existing offline suites, the new must-be-present and must-be-absent probes, and a
census on a native server.

## Implementation (first slice)

Built 3 November 2026 on branch `claude/placement-catalogue` and merged into `release/1.0` the same day, with calm rivers (0046). Not released.

- **Catalogue.** `tools/placement-taxa.json` (curated: taxa, WCVP names, dated 1318 introductions with cited sources,
  manual climate limits with reasons, provider mappings, exemptions) is baked by `tools/bake_placement.py` into
  `assets/earth/config/placement.json`: 47 taxa (12 fruit trees, 25 crops, 10 berry bushes), 50 mappings. A range is
  the WCVP native range plus introductions minus exclusions; the envelope uses regional trees' method (3rd–97th
  percentile of real yearly mean and rain over the range, margins 1 °C and 8). Manual limits: olive and sweet orange
  coldest month ≥ 5 °C, pomegranate ≥ 2 °C, mango ≥ 10 °C. `--check` verifies the shipped file.
  `src/Core/PlacementCatalogue.cs` parses it; it does not depend on any mod.
- **Choices made at implementation.** The vanilla orange is the sweet orange (southern China, Indochina, India in 1318;
  not yet Spain). Vanilla amaranth is the American grain amaranths. Vanilla white currant is the red currant.
- **Fruit trees.** `EarthPlacement` prefixes `BlockFruitTreeBranch.TryPlaceBlockForWorldGen`: vanilla's order, chance
  and fertility check stay; each mapped type's climate test is replaced by its 1318 range (botanical country at the
  position) and Earth envelope (yearly mean and coldest month at the site's elevation, rain). Unmapped types keep
  vanilla's test. The rare fruit tree patch's forest minimum (0.3) drops to 0, since olive, fig and pomegranate grow
  in open country.
- **Crops and berries.** `EarthPlants` gates every block patch whose codes all map to the catalogue (16 vanilla wild
  crops, 10 vanilla berry bushes, 12 Floral Zones wild crops) by the taxa's 1318 regions, with the existing prefix
  and forest-floor hooks. The taxa's Earth envelopes replace the patch's own temperature and rain limits (the first
  server run showed why: red currant, spelt and parsnip came up around Seville, inside their range but outside their
  climate). Forest, fertility and height limits stay. Patches on or under trees keep their own limits. Without plant
  packs, or with RegionalPlants off, the catalogue gates still apply.
- **Animals.** Fauna-species taxa listed as alive in 1318 (Tasmanian emu, Steller's sea cow, Chatham penguin,
  Falkland wolf, *Dusicyon avus*, aurochs, baiji and the New Zealand, Chatham and Lord Howe birds) spawn in 1318
  worlds without the extinct option. Ranges are still the existing profiles; the 1318 animal range source is open.
- **Player setting.** `earthNature` in Customize → Earth: **As around 1318** (default) or **Vanilla, by climate**.
  1318 saves `PlacementRules` 1 (`EarthParams` field 90) and real animal ranges. Vanilla saves neither and also turns
  regional trees off. The old **Animals** dropdown left the menu; a world configuration without `earthNature` still
  reads `earthAnimals`. **Include extinct animals** now defaults to off (menu and `IncludeExtinctAnimals`).
- **Older worlds** keep `PlacementRules` 0: vanilla fruit, crop and berry placement, as before.
- **Audits.** At startup the placement inventory classifies every block patch (catalogue, plant-pack ranges, other
  Earth systems, not organisms, uncontrolled), the fruit-tree types and every naturally spawning entity (regional
  fauna, vanilla ranges, none), logs a summary and writes
  `VintagestoryData/EarthMap/placement-inventory-<seed>.txt`. `/earth placement` shows the site, the catalogue plants
  in range and climate, and this session's fruit trees; `/earth census [radius]` counts what generated in loaded
  chunks. The self-test logs both at each probe place.

### Verification

Offline, 3 November 2026:

- New `tests/PlacementTests`: 413 checks. Every taxon has a range, an envelope, cited introductions and a mapping, and
  finds climate inside its envelope in its own regions. No olive, pomegranate, spelt, rye, apple, orange, rice, soybean
  or mango in eight American cities. No maize, sunflower, peanut, pineapple, cassava, sweet potato, bean, grain amaranth
  or agave in ten Old World cities. Olives in Athens, Seville, Tunis, Jerusalem, Heraklion and Rome, not in Berlin or
  London. Same climate (17.1 °C, coldest month 10 °C, rain 64): olive in Athens, not in Los Angeles.
- `EarthWorldOptionsTests` 514 checks, including the new option, its fallback to `earthAnimals`, protobuf round trip of
  `PlacementRules` and the extinct default. `RegionalPlantsTests`, `RegionalFaunaTests` (950) and `RegionalTreesTests`
  (169) pass. `EarthMapTests` fails only the known "4M dalmatia: the rocky coast rises steeply" (see 0020).
- `bake_placement.py --check` reproduces the shipped `placement.json`.

Native server (VS 1.22.7, Earth 1.0.2 + this branch, Cylinder World 1.0.4, Hydrology 0.6.2, world 256,000 blocks,
seed 386217, self-test radius 6 chunks, about 220 blocks around each place):

| Place | Site | Eligible catalogue plants (examples) | Generated (census) |
| --- | --- | --- | --- |
| Athens | Greece, 18.3 °C, coldest month 9.8 °C, rain 64 | olive, pomegranate, peach, flax, licorice | flax 175, rye 58, turnip 47, carrot 39, onion 35, licorice 25, fennel 23, parsnip 4; no fruit tree |
| Seville | Spain, 19.0 °C, 11.0 °C, rain 70 | olive, pomegranate, rice | flax 376, rice 124, carrot 115, turnip 73, onion 54, fennel 34; peach 3, pomegranate 1, three apples |
| Tunis | Tunisia, 18.9 °C, 11.9 °C, rain 67 | olive, pomegranate | flax 282, carrot 45, onion 41, turnip 21, fennel 14 |
| Los Angeles | California, 18.0 °C, 12.6 °C, rain 64 | none in climate (sunflower, bean, maize in range) | sunflower 17; no fruit tree |
| Mexico City | Mexico Central, 16.2 °C, 13.2 °C, rain 117 | grain amaranth, peanut, sunflower, maize, bean | grain amaranth 41, peanut 9, sunflower 8 |

- Fruit trees over the session: 138 attempts, 92 on ground of fertility 20 or less, 28 with no eligible type (every
  attempt in California and Mexico, where no vanilla fruit is native in 1318). Placed: pomegranate 5, peach 4, red
  apple 3, yellow apple 3, pink apple 1, olive 1, cherry 1, all in Spain and Tunisia.
- The first run, before Earth envelopes replaced the patches' own climate limits, grew red currant (98), spelt (38)
  and parsnip (7) around Seville. They are gone in the second run.
- With **Vanilla, by climate**: no placement catalogue, regional trees off (maple and birch around Athens), animal
  ranges and vanilla fauna ranges off, extinct animals off.
- Inventory: 27 patches by 1318 range (26 crop and berry patches, 1 fruit-tree patch), 11 gated by other Earth
  systems, 26 not organisms, 124 uncontrolled. All 12 fruit types are mapped. Animals: 82 vanilla types with natural
  ranges; 248 without (170 butterfly variants, 56 fish, 22 lore creatures). No errors in the server log.

**Olives around Athens were blocked by the ground:** 32 of 34 fruit-tree attempts there fell on sand (fertility 10),
which fails vanilla's fertility check (more than 20). Earth's dry Mediterranean surface was mostly sand and muddy
gravel (census around Athens: muddy gravel 64,035 columns, sand about 107,000, soil 722). Resolved worldwide by
[0048](0048-soil-on-semi-arid-land.md): semi-arid land gets soil in new worlds. Its server run counted around Athens
soil 87,517 columns and fruit trees peach 4, olive 3, pomegranate 3, yellow apple 2 and cherry 1.

Not verified: Floral Zones wild crops in game (no pack was installed), animals alive in 1318 spawning (no fauna pack),
and play in a client.

### Not done in this slice

- Moving the tree, ground-plant and fauna catalogues into one format (step 2).
- Earth envelopes for other ground plants and fauna (step 4); flowers, mushrooms, ferns, grasses and aquatic plants
  (124 vanilla patches stay uncontrolled in the inventory). Butterflies (170 variants), fish (56) and lore creatures
  have no range.
- A 1318 range source for animals (open question below); vanilla animals people had spread by 1318 still follow
  present-natural ranges.
- The GBIF comparison (step 5), eligibility maps, and traders and loot.

## Open questions

- **Animals under 1318 (rule agreed, details open):** the rule means no wild boar, chickens, sheep, goats or horses in
  the Americas. Ranges are as of 1318, not PHYLACINE's "natural range without human impact": lions are gone from
  Greece, while aurochs, moa and Steller's sea cow are still alive in their relict ranges. The extinct-animals option is decided
  (see Author's decisions). Still open: which range source replaces the present-natural rasters, and each extinct
  taxon's classification as before or after 1318, with evidence.
- **The vanilla orange,** and the other vanilla taxa that stand for several species: which taxon each one represents.
  The first slice chose sweet orange, the American grain amaranths and red currant (see Implementation); the author
  can override.
- **Older Earth worlds:** do they adopt the 1318 profile for new chunks, given [0002](0002-save-compatibility.md)?
  The first slice leaves them on vanilla placement; no upgrade flag exists yet.
- **Traders, loot and story structures:** do they follow the 1318 ranges?
- **Licences:** the TDWG boundary licence is still unresolved ([regional trees](../regional-trees-implementation.md)).
  Any curated 1318 sources must be citable without shipping their text.

## History

- 3 November 2026: proposed at the author's request after the olive report.
- 3 November 2026: direction agreed by the author: one catalogue independent of mods, the world as around 1318 for
  plants and animals, and a player setting back to vanilla climate placement.
- 3 November 2026: the author keeps animals extinct before 1318 as an opt-in extra, off by default.
- 3 November 2026: first slice implemented and checked offline and on a native server (Implementation section).
  Correction: vanilla land animals have had natural ranges since 0031.
- 3 November 2026: the sand blocking fruit trees around Athens is fixed by 0048 (soil on semi-arid land).
- 7 November 2026 (ranges proposal, step 3, branch `claude/region-index`): the vanilla cacti (saguaro, barrel, silver
  torch) move from "controlled elsewhere" to taxa of a new kind, `wild`, with WCVP native ranges and manual climate
  limits (0032 History). Their whole-region envelopes would have admitted Arizona's pine plateaus and Bolivia's Amazon
  lowlands. The baked-envelope sanity check now also accepts a taxon whose envelope holds any of a region's
  temperature bins and any of its rain bins, since a desert specialist fits part of a region, not its median.

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: **Agreed** in direction (see Author's decisions). A first slice is **Implemented**
(see [Implementation](#implementation-first-slice)); the rest of the layers, audits and order of work below remain
**Proposed**.
