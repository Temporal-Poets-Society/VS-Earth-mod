# Regional ground plants

Implemented 2026-10-02 for Vintage Story 1.22 and the eight Floral Zones packs. Regional trees
(`regional-trees-implementation.md`) already restricted natural **tree and shrub generators** to their native
botanical countries. Ground plants were still climate-only: the Floral Zones packs add 275 block patches
(flowers, ferns, berry bushes, tall plants, aquatic plants, wild crops) that spawned on every continent with a
matching climate. For example, the Cape belladonna lily (14–18 °C) appeared in Chile, California and Australia.
This pass applies the same native-range rule to those block patches.

## What changes in play

In Earth worlds with regional trees, each Floral Zones ground plant now generates only in the WGSRPD level-3
botanical countries where its species is native (Kew WCVP). The Cape lily, proteas and heaths stay in the Cape
Provinces. Lemon daylily (Eurasian) no longer appears in Canada just because its pack is called "Circumboreal".
Spanish broom (invasive in California) and the NZ flax (introduced elsewhere) stay in their native ranges.
The pack's own climate, rainfall, forest, height and water rules still apply on top of this, so a plant only
grows where both the place and the climate fit.

These keep their climate-only rules:

- vanilla block patches, and patches of any mod that is not in the catalogue;
- the packs' **wild crops** (`crop.json`: agave, cotton, angelica, lettuce, garlic, sweet potato, bean, maize,
  sponge gourd, fuki, horseradish and dwarf cabbage tree). These are 14 patches. Cultivated/crop plants are left alone so their
  seeds stay obtainable everywhere;
- two Cosmopolitan-pack organisms that have no WCVP range: reindeer lichen and sea lettuce.

Existing chunks are never changed. The setting is read at every start and applies to chunks generated afterwards.

| Activation | Condition |
|---|---|
| Earth world | `Params.Enabled` |
| Regional trees in this save | `Params.RegionalTrees` (frozen at world creation) |
| Config | `RegionalPlants` in `ModConfig/earthmap.json`, default `true`, read at every start |
| Provider | at least one reviewed pack installed |

## Inventory (installed set)

| Provider | Release | Patches | By species range | By pack region | Climate-only |
|---|---:|---:|---:|---:|---:|
| Floral Zones: Cape | 1.0.37 | 25 | 25 | 0 | 0 |
| Floral Zones: Caribbean | 1.0.29 | 38 | 35 | 0 | 3 (crops) |
| Floral Zones: Central Australian | 1.0.27 | 34 | 34 | 0 | 0 |
| Floral Zones: Circumboreal | 1.0.2 | 54 | 41 | 10 | 3 (crops) |
| Floral Zones: Cosmopolitan | 1.0.13 | 42 | 33 | 0 | 9 (4 crops, 5 lichen/alga) |
| Floral Zones: East Asiatic | 1.0.24 | 24 | 22 | 0 | 2 (crops) |
| Floral Zones: Mediterranean | 1.0.24 | 27 | 26 | 0 | 1 (crop) |
| Floral Zones: Neozeylandic | 1.0.24 | 31 | 30 | 0 | 1 (crop) |
| **Total** | | **275** | **246 (89.5 %)** | **10 (3.6 %)** | **19 (6.9 %)** |

No pack patch is unmapped. The patches use 199 species tokens, and each token has one rule. Every patch is an `addmerge` into
`game:worldgen/blockpatches/{flower,fern,berrybush,other,crop,mushroom}.json`. Wildcraft: Trees and Shrubs adds
only tree generators (handled by regional trees), and Herbarium is a code library without assets, so neither has
ground-plant patches. The inventory, including each patch's comment, placement, block codes and rule, is stored in
`assets/earthmap/config/regionalplants.json` together with the pack versions and SHA-256 checksums.

## Data pipeline

`tools/bake_plant_regions.py` (run with the project venv; reads the pack zips read-only):

```
.venv\Scripts\python.exe tools\bake_plant_regions.py [--mods <folder with pack zips>] [--sources obj/ecology-inspect] [--check]
```

1. **Inventory.** Every entry of `patches/worldgen/*.json` in each pack is read with JSON5. The tool fails on any
   operation other than `addmerge` into `game:worldgen/blockpatches/`, and on wildcard or foreign-domain codes.
2. **Species token.** Each block code (e.g. `floralzonescaperegion:flower-amaryllisbelladonna-free`) must contain
   exactly one species token listed in `tools/plant-taxa.json` (`amaryllisbelladonna`). Unused tokens are also an
   error, so the manual table always matches the installed packs.
3. **Taxon.** `plant-taxa.json` gives each token's scientific name, with evidence: the token itself, the pack's
   English block name and, where it differs, the pack's own scientific name from its `blockdesc` lang entries. Names are
   matched in WCVP `wcvp_names.csv`, accepted names first. Synonyms resolve to the accepted taxon, as in
   `bake_tree_regions.py`. A taxon that differs from its token needs an explanatory note.
4. **Range.** Native, non-doubtful WCVP distribution records (`introduced=0`, `location_doubtful=0`; regionally
   extinct records kept) give the level-3 codes. All codes must exist in `treeregions.json`.
5. **Fallbacks.** `range: packRegion` uses the pack's floristic region (below), optionally intersected with a named
   region set. `range: climateOnly` produces no veto. Every token used by a `climateOnlyFiles` patch (`crop.json`)
   must be climate-only.
6. **Output.** `regionalplants.json` (version 1) contains the rules (codes, taxa, WCVP ids, regions, basis, notes),
   the provider list, the pack regions, the full patch inventory and the coverage summary.

### Mapping decisions

| Case | Decision and evidence |
|---|---|
| Spelling differences | `Disa purpurescens` → *Disa purpurascens*; `Acoelorrhaphe` → *Acoelorraphe wrightii*; `Opuntia triacantha` → *O. triacanthos*; `Ixodia achillaeoides` → *I. achilleoides*; `Polystichum polyblepharum` → *P. polyblepharon*. The last one only matches an illegitimate homonym in WCVP. |
| Synonyms (WCVP) | *Elytropappus rhinocerotis* → *Stoebe rhinocerotis*; *Calandrinia balonensis* → *Parakeelya balonensis*; *Rosa majalis* → *R. cinnamomea*; *Taraxacum officinale* → *Taraxacum* sect. *Taraxacum*; *Austroblechnum penna-marina* → *Blechnum penna-marina*; *Chamaenerion angustifolium* → *Epilobium angustifolium*; *Linnaea amabilis* → *Kolkwitzia amabilis*; *Lomaria discolor* → *Blechnum discolor* |
| Pack text errors | The Chinese peony `blockdesc` repeats "Lycoris radiata". The code and the name "Chinese Peony" identify *Paeonia lactiflora*. `juniperuscommunissmall` is a growth form of *Juniperus communis*. |
| Wild crops | All 14 `crop.json` patches (12 species) are climate-only (cultivated/crop-like). |
| Coconut, fig-leaf gourd | These are gated by their WCVP native ranges (C. Malesia–SW Pacific; Peru–Bolivia). This matches how `regionaltrees.json` treats the coconut and papaya trees. The packs place them as wild plants. |
| Non-vascular (not in WCVP) | Circumboreal pack: Iceland lichen and two Sphagnum mosses use the Circumboreal region. Egg wrack and oarweed use the Circumboreal region limited to North Atlantic coasts (`northAtlanticSeaweed`), because both are North Atlantic seaweeds absent from the Pacific. Cosmopolitan pack: reindeer lichen and sea lettuce are climate-only. |
| Wide native ranges | These follow WCVP even when the pack implies otherwise. Kangaroo grass (*Themeda triandra*) is also native in Africa and Asia. Forked sundew is also native in Australia and Tasmania. The King fern reaches Queensland and the Pacific. |

### Pack floristic regions (fallback only)

These are Takhtajan's regions, after which the packs are named, expressed as WGSRPD level-3 sets in `plant-taxa.json`.
Only the 10 Circumboreal non-vascular patches use them. Every other gated plant has a WCVP species range.

| Pack | Level-3 set |
|---|---|
| Cape | CPP (Cape Provinces) |
| Caribbean | West Indies (L2 81 without Bermuda), Central America (L2 80), MXG, MXT, MXS, MXI, FLA, GAL |
| Central Australian | NTA, SOA, WAU |
| Circumboreal | L2 10, 11, 14, France, Caucasus, Siberia, Russian Far East, L2 70–72, northern-tier US states east of the Rockies |
| East Asiatic | CHC, CHM, CHN, CHS, JAP, KOR, TAI, NNS, PRM, SAK, KUR, EHM |
| Mediterranean | Mediterranean basin, North Africa, Levant, Anatolia, Crimea, Macaronesia (AZO, MDR, CNY, SEL) |
| Neozeylandic | NZN, NZS, CTM, ATP (Auckland/Campbell), KER, NFK (Norfolk/Lord Howe) |
| Cosmopolitan | none: climate-only unless a species range exists |

## Runtime (src/EarthPlants.cs, src/Core/RegionalPlantCatalog.cs)

- **Startup** happens at `ServerRunPhase.WorldReady`, after `EarthMapSystem` has loaded the params. The catalogue loads
  `regionalplants.json` and `treeregions.json`. Missing assets, a parse error or missing hooks produce a warning and
  leave the system inactive, with vanilla/pack behaviour. Nothing is thrown into startup or world generation. A pack
  version that differs from the reviewed one produces a warning; its reviewed codes stay gated and new codes keep their climate rules.
- **Gate table.** A postfix on `GenVegetationAndPatches.initWorldGen` maps every loaded `BlockPatch` object
  (keyed by reference) to an immutable allowed-region bitset (`PlantGate`, 6 × 64 bits for 369 countries). It is
  rebuilt if world generation re-initialises. Patches with any climate-only code stay climate-only. A covered
  pack's unknown code, or a mix with foreign codes, keeps vanilla rules and is logged.
- **Hooks.**
  - A prefix on `BlockPatch.Generate` (Harmony id `earthmap.regionalplants`) returns `false` for a disallowed
    position. It covers surface, near-water, underwater and under-tree patches. It coexists with EarthWorldGen's
    reservation prefix on the same method.
  - Patches growing on tree trunks (`OnTrees`: wild vanilla, kahakaha) never call `Generate`. For these, the two
    `IsPatchSuitableUnderTree` call sites inside `ForestFloorSystem.GenPatches` are rewritten by a transpiler, with
    the tree position supplied by a prefix and finalizer. A transpiler is used because the small check could be
    inlined by the JIT. If this hook is unavailable, only those two patches keep their climate rules, and a warning is logged.
- **Position.** World x/z go through `Model.Geometry.ToGeo`. The gutter and positions beyond the poles have no
  region, so gated plants are blocked there. The lookup uses the patch's own position, resolved per 8×8-block cell
  (about 0.006°, finer than the 0.015° boundary simplification), with a one-entry `[ThreadStatic]` cache.
  The same bounded 0.08° coastal allowance as regional trees applies. Aquatic blocks (water lilies, aquatic plants)
  and patches with water placements fall back to the nearest botanical country within 2° (rings of 16 directions).
  This lets lakes outside the polygons and coastal seaweed beds resolve. The open ocean beyond 2° has no region.
- **Region index.** `PlantRegionIndex` is an allocation-free reimplementation of `BotanicalRegions.Find` over the
  same `treeregions.json`. It uses a 1° cell grid and per-ring 0.05° latitude bands of edges. On 40,000 random points it
  gives the same result as the tree catalogue at every point.
- **Thread safety.** The catalogue, gates and state are immutable after publication (a volatile reference). The
  caches and tree scope are thread-static. The only shared write is an `Interlocked` veto counter.

Measured in the test project (Release, .NET 8):

| Measurement | Result |
|---|---|
| Uncached land lookup + gate | 0.23 µs |
| Shore points | 0.6 µs |
| Open sea | 0.04 µs |
| Aquatic nearest-country search in open water | 3.3 µs |
| Cached gate test | 1.5 ns |
| Allocation per lookup | 0 bytes |

`EarthPlants.Describe(x, z)` returns the `/earth plants` text. It gives the botanical country at that position (or the
nearest one for aquatic plants), how many gated pack patches are allowed or blocked there, the climate-only and
unmapped counts, the vetoes since start, and up to 25 native pack plants. Climate is not evaluated.

## Tests

`tests/RegionalPlantsTests` (`dotnet run -c Release --project tests/RegionalPlantsTests -- .`) runs 1,664 checks.
They cover:

- **Parsing.** Catalogue build and parse errors: wrong version, unknown region, conflicting code, bad boundaries.
- **Provider filtering.** Absent packs activate nothing.
- **Inventory.** The 275/246/10/19 coverage. Every inventory code resolves to a rule, and every patch resolves to
  `Gated` or `ClimateOnly` as recorded.
- **Native ranges.**
  - Cape belladonna lily allowed at Cape Town, blocked in Santiago, Los Angeles, Sydney and Perth. No Cape pack plant is in the other Mediterranean-climate regions.
  - Spanish broom allowed in Greece, blocked in California. Every Mediterranean plant is in California only if WCVP lists CAL.
  - NZ flax blocked in Tasmania. No NZ endemic is in Tasmania, while the forked sundew (WCVP-native there) is allowed.
  - Lord Howe lily only on Lord Howe. The South Island endemic is not on the North Island.
  - The Circumboreal fallback applies, and wild vanilla is not in Madagascar.
- **Seaweeds.** Allowed off Maine and Norway, blocked in the Gulf of Alaska, and the mid-Pacific has no region.
- **Unknown and mixed patches.** Vanilla, unknown and mixed patches keep their climate rules. Multi-species patches use the union of their ranges.
- **Geography.** Parity with `BotanicalRegions` and the Earth seam round trip.
- **Performance.** Lookups stay under 2 µs with zero allocations.
- **Concurrency.** Concurrent lookups give consistent results.

A throw-away harness also loaded the real game assemblies on .NET 10 and applied the Harmony methods to the
game's methods. Both prefixes bound and coexisted on `BlockPatch.Generate`, and the transpiler rewrote 2 call sites.
A Cape patch generated at Cape Town but was vetoed at Santiago and Sydney, while a vanilla patch was unaffected.
No game server was started.

## Limitations

- **Coarse ranges.** Botanical countries are coarse. A Florida, Western Australia or Cape Provinces record allows
  the whole unit, and local habitat comes only from the pack's climate rules. Fine-scale endemics (e.g. the Galápagos lava cactus,
  Campbell Island daisies) also need their islands to exist at the world's scale.
- **Non-vascular organisms** (algae, lichens, mosses) have no WCVP data. Their ranges are pack regions or literature-based
  sets, not checked by the tool.
- **Wild crops stay climate-only by design.** Gating them would need a separate gameplay decision about seed
  availability.
- **Boundary format.** The plant index parses `treeregions.json` itself (format version 1). If that file's format
  changes, `PlantRegionIndex` must follow, or the system stays inactive with a warning.
- **New pack releases** need a re-bake: new codes stay climate-only, and a warning names them.

## Updating

When a pack releases a new version, run the bake tool against the new zips. If it fails:

- **Unknown token.** Add the token to `plant-taxa.json` with its evidence.
- **Unused token.** Remove the token from `plant-taxa.json`.
- **New crop.** Mark it climate-only.

Then rerun the tool and `tests/RegionalPlantsTests`, and update the coverage numbers (the inventory count checks are
intentionally exact).
