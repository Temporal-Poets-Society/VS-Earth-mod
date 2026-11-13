# Regional trees — rules version 2 (Earth 0.13)

Rules v2 (2026-10-02) replace the 0.9–0.12 rules (v1) for Vintage Story 1.22.7. They cover
natural tree and tree-generator shrub **species selection** for vanilla, the eight Floral
Zones packs and Wildcraft: Trees and Shrubs (with Herbarium). Ground plants are handled by
a separate Earth ground-plant integration; block-patch berry bushes, crops, animals, rocks
and ores are not part of this layer.

## What changes in play

Each tree attempt (vanilla decides where and how often) asks Earth which species to grow.
Earth answers in three tiers:

1. **Native species** of the botanical country at the actual planting position (WCVP native
   range × TDWG level-3 polygons), filtered by climate, elevation and water. Where only one
   or two native species qualify (effective diversity below 2.5), the biome's analogues join
   them at a reduced share (up to 50 % for a single native species), so a lone native does
   not become a monoculture (Borneo's coast, vanilla-only New Zealand, Honshu, Georgia).
2. If none of the installed native species qualifies there: **regional analogues** —
   designated stand-ins for the biome, e.g. kapok/purpleheart/bamboo/tree ferns for humid
   tropical forest with vanilla only, acacia/ebony for savanna, oak/conifer/tree ferns for
   tropical montane and southern temperate rainforest, Floral Zones' silver beech for
   Valdivian Nothofagus forest when installed. These are explicitly analogues, not natives;
   each rule lists its stand-ins and why (`tools/tree-taxa.json`, `analogues`).
3. If still nothing qualifies: the **pre-0.9 Earth climate pick** among vanilla trees, never
   with a species the forest type rules out (no oak in the taiga or the desert).

Native species keep at least half of the trees wherever they qualify. Analogues are
biome-specific: savanna and dry woodland get acacia/ebony (never oak), Afromontane forest
conifer stand-ins and tree ferns (no oaks south of the Sahara), Mediterranean woodland
requires dry summers. Earth's forest types (boreal, hemiboreal, temperate broadleaf,
Mediterranean, montane, hot-dry) shape the mix of vanilla's temperate species and of the
providers' conifers, birches, oaks, beech and maples; they no longer inflate vanilla's
species against other providers' natives.

**Shrubs** (vanilla's shrub pass: English oak, dwarf birch, kapok … and Floral Zones'
shrub forms) are native-only: no analogue or climate-fallback shrubs, so no oak scrub in
the Highveld or Arizona and no dwarf birch in Kenya. Dwarf birch is limited to tundra,
mountains and northern mires (real yearly mean ≤ 1.5 °C).

**Water.** No tree or shrub grows in a river channel (they block boats), whatever its
habitat; on a river bed or lake floor whose block above is water the attempt counts as
underwater even where vanilla's check missed it, so only water trees (bald cypress,
mangroves in coastal shallows) qualify there, and shrubs never. Floodplain and lake-shore
trees — black and grey alder, white and black poplar, American sycamore — grow only within
about 16 blocks of a river or by a lake; beach trees (tamanu, coconut) only near the sea.

Other changes in v2:

- **Earth climate envelopes.** Wildcraft and Floral Zones climate limits (and vanilla's for
  tropical trees) are calibrated to vanilla's climate scale; Earth uses real temperatures
  (Latvia ≈ 6 °C, not Wildcraft's −14..2 °C for spruce or 14..20 °C for beech). Every
  non-forest-type rule now carries a real-temperature and rainfall envelope derived from
  the species' native botanical countries on Earth's own climate grid
  (`tools/tree-region-climate.json`, 3rd–97th weighted percentiles ± margin). It replaces
  the provider's temperature, rainfall, fertility and altitude limits; the provider's
  forest-density preference (open vs closed stands) is kept.
- **Taxa.** The generic vanilla fir now also stands for the native spruces (Picea abies,
  obovata, glauca, mariana, rubens, sitchensis, engelmannii, jezoensis, orientalis,
  koraiensis) and more firs. Wildcraft elm includes European and Asian elms; tree and shrub
  alder are separate rules (black/grey/red/Manchurian/Japanese/Italian alder vs green/grey/
  hazel alder thickets); spruce, true fir, cedar (stone pines), beech, white birch, linden,
  ash, yew and the willows represent their regional relatives. Wildcraft's eucalyptus is the
  rainbow eucalyptus (Mindanao–New Guinea) — kept and documented, not an Australian gum.
- **WCVP error exclusions.** Native records that are evident introductions are removed with
  a note (`exclusions`): silver birch in Argentina South (made Patagonia 100% birch). A scan
  of all mapped taxa for Eurasian species in South America/Australasia found no other case.
- **Shared alias weights.** Generators of one species (English oak and two old-oak entries;
  kapok and Floral Zones' Ceiba) share the species' weight (its largest alias weight, split
  in proportion to the aliases' own weights) instead of the average.
- **Blended borders.** The region lookup point is displaced by smooth deterministic noise
  (≈0.3° typical, ≤1°), so political lines such as 31°N (Georgia/Florida) or 49°N become
  irregular transition zones. Shores and small islands fall back to the undisplaced position.
- **Lowland band.** Vanilla limits Norway maple to below sea level (0–40 % of world height);
  the rule ignores that band so the maple appears in its native European range.
- **Hemiboreal and boreal mix.** With spruce now native, the regional hemiboreal and boreal
  tables are pine-led with spruce and birch.
- **Northern and frost limits.** English oak needs a warmest month ≥ 16.5 °C (northern limit
  ≈ 60–61 °N), sessile oak also a coldest month ≥ −3.5 °C (absent from the continental Baltic
  interior), Norway maple ≥ 15 °C; boreal firs, spruces and larches stay where the warmest
  month is ≤ 21–21.5 °C; frost-tender Caribbean trees (Lysiloma, royal palm) need a coldest
  month ≥ 17 °C (South Florida, not central Florida). Green alder is subalpine/boreal (≤ 7 °C).
- **Provider bands.** A provider registering one species several times with different climate
  bands and weights keeps that structure inside the species' Earth envelope.
- **Weight calibration.** Wildcraft's spruce (×8) and beech (×12) are scaled up so that, when
  installed, they form real spruce and beech stands next to vanilla's generic fir and oak.

## Measured effect (offline, real Earth climate)

`dotnet run -c Release --project tests/RegionalTreesTests -- . --report --step 1` evaluates
the production pick on Earth's baked climate for forest-capable land (forest potential ≥ 0.3,
1° grid, area-weighted), with the exact registrations of the tested pack
(`tests/RegionalTreesTests/registrations.json`). "Treeless" = an attempt yields no tree with
probability > 90 %.

| Configuration | Treeless 0.12.2 | Treeless v2 | Attempt success 0.12.2 | v2 | v2 tiers native / analogue / fallback |
|---|---:|---:|---:|---:|---:|
| Pre-0.9 Earth, vanilla | 10.1 % | (unchanged) | 86.1 % | — | — |
| Vanilla only | 46.9 % | 0.0 % | 52.4 % | 100 % | 50.2 / 49.8 / 0.1 % |
| + Wildcraft | 41.2 % | 0.0 % | 56.6 % | 100 % | 72.2 / 27.7 / 0.1 % |
| + Floral Zones (8) | 39.0 % | 0.0 % | 58.8 % | 100 % | 60.1 / 39.8 / 0.1 % |
| Full pack | 35.4 % | 0.0 % | 61.3 % | 100 % | 76.0 / 24.0 / 0.1 % |

The analogue column includes analogues blended into thin native pools.

Attempt success is now at or above the pre-0.9 level: forest density follows Earth's forest
map again instead of failing silently in the tropics and the southern hemisphere.

- Full-pack probes (`tests/RegionalTreesTests/Probes.cs`): 27 places from Latvia, Finland,
  Sweden, Norway, Scotland, the Upper Rhine and Black Forest to Ontario, the Appalachians,
  Kaibab, Florida, Amazon, Congo, Kenya, Highveld, Borneo, Honshu, SE Australia and New
  Zealand; the top genera must include the regionally dominant ones and implausible genera
  stay small. Examples (share of trees on upland sites):
  Latvia pine 39 %, birch 24 %, spruce/fir 17 %, maple/oak/beech 15 % (vanilla only: pine 44 %,
  birch 27 %, spruce/fir 17 %); central Finland pine 42 %, spruce/fir 32 %, birch 20 %;
  Upper Rhine oak 49 %, maple 15 %, beech 15 %, birch 8 %; Kenya highlands tree heath 33 %,
  Afrocarpus 27 %, conifer stand-in 18 %, tree ferns 13 %; Highveld acacia 62 %, Searsia 38 %;
  inland Borneo kapok stand-in 53 %, purpleheart 23 %, bamboo 15 %, tree ferns 9 % (tamanu on
  the coast only); Honshu konara oak 68 %, beech 15 %, pine 7 %.- Wildcraft temperate species are now eligible on most of their native forest (beech 95 %,
  linden 93 %, ash 92 %, elm 95 %, spruce 74 %, willow 98 %, aspen 95 %; alder 69 % and
  shrub alder 37 % on wet sites; yew 42 %; 0.12.2: effectively 0). Beech (×12) averages 12 %
  of trees in its range, spruce (×8) 2.5 %; the others stay at 0.1–2 % by their provider weights.- Borders: along 49°N the southern share per 0.1° band changes by at most 0.14 (exact
  border 0.51); about 30 % of points within 0.5° of the line take the other side's flora.
- Performance: 0.4–0.6 µs and 0 bytes per attempt with 208 registrations (0.12.2: ≈30 µs and
  ≈24 KB), blended region lookup ≈0.02 µs when cached per 0.02° cell.

These are offline measurements of the selection logic. A server run (below) is still the
check for actual placement.

## Known limits

- Botanical countries are coarse: Russia Central, Brazil Southeast or a Mexican state are
  one unit, so a native species can appear anywhere in its countries where the climate fits.
  Climate envelopes inherit that coarseness (e.g. mountain Douglas fir's warm limit comes
  from its Mexican countries).
- The in-game self-test logs canopy by **wood**: vanilla's generic fir (the spruce form) uses
  pine logs and leaves, so spruce counts as "pine" there; canopy share also weights trees by
  crown size (Floral Zones' black alder, white poplar and sessile oak are large, vanilla's
  birches slender) and includes shrubs. The selection trace in the same report (region,
  generator, tier) is the direct measure of composition.
- Thin native pools are blended with analogues; a single native species still keeps half.
- Provider forest-density preferences are kept, so edge species are rare in Earth's dense
  forests. "Near water" uses Earth's river network and lake cells, not mires or wet soils.
- Regional analogues are stand-ins chosen by biome; they are documented as such and are not
  claims of nativeness. Falkland Islands and a few unmapped coastal cells fall to the
  climate fallback.
- Vertical relative-height limits of vanilla's forest-type species are kept (except Norway
  maple); river birch remains as rare as in vanilla.

## Data and licensing

Native distribution: [Kew WCVP](https://sftp.kew.org/pub/data-repositories/WCVP/) snapshot
2026-06-04 (CC BY 3.0). Introduced and doubtful records are excluded; regionally extinct
native records are retained; listed WCVP errors are excluded.

Boundaries: Biodiversity Information Standards (TDWG), World Geographical Scheme for
Recording Plant Distributions, 2nd ed. (Brummitt 2001), level 3, from
[github.com/tdwg/wgsrpd](https://github.com/tdwg/wgsrpd) (originally Kew shapefiles).
**The licence is an open question**: TDWG's website content, including the WGSRPD standard
page, is CC BY 4.0, but the boundary files carry no explicit licence (the repository states
none; an ESRI "All Rights Reserved" credit appears only in the file metadata). Earth claims
no licence for them; see `assets/earthmap/config/TREE-DATA-ATTRIBUTION.txt`. If
clarification fails, the boundaries need replacing by an openly licensed equivalent.

Exact taxon IDs, regions, exclusions, envelope settings and source hashes are in
`docs/research/tree-range-provenance.json`; identities, analogues and corrections in
`tools/tree-taxa.json`.

## Code

- `src/Core/RegionalTreeCatalog.cs` — rules (`RegionalTreeCatalog`, version 2, `TryLoad`
  never throws), the game-independent pick (`RegionalTreePicker`: tiers, envelopes, alias
  sharing, forest types; compiled once per registered variant list, no per-attempt
  allocation, thread-local scratch) and the botanical index (`BotanicalRegions`, unchanged
  public API plus `FindIndex`/`FindBlended`).
- `src/EarthTrees.cs` — Harmony hooks; builds the site (actual position, blended region,
  climate, water type only where a rule needs it) and turns the pick into a
  `TreeGenInstance`. A failing pick falls back to vanilla's (logged once); missing hooks set
  `EarthTrees.Patched = false` with `PatchError` instead of throwing.
- Without regional trees (or if the rules cannot be loaded) the pre-0.9 Earth pick is used
  unchanged.

## Saves

New Earth saves use regional trees by default. Saves recorded with rules v1 are moved to v2
by Earth at load (logged); only newly generated chunks change. Saves without regional trees
keep the pre-0.9 pick.

## Reproduction

1. Registrations: `tools/inspect_tree_inventory.py [--fixture tests/RegionalTreesTests/registrations.json]`
   (Python `json5`; reads VS 1.22.7 and the provider zips from the game's Mods folder).
2. Region climate: `dotnet run -c Release --project tests/RegionalTreesTests -- . --region-climate`
   (writes `tools/tree-region-climate.json` from `earthdata/` and `treeregions.json`).
3. Rules: `tools/bake_tree_regions.py --sources <folder with wcvp.zip, tdwg-level3.geojson, pylibs>`
   (Python `shapely`). New source data requires review and a rules-version increment.
4. Tests: `dotnet run -c Release --project tests/RegionalTreesTests -- .` (169 checks: data,
   synthetic tiers/aliases/envelopes, global treeless share and success, every forest-capable
   region served, Wildcraft eligibility, Latvian mix, border blending, zero allocation and
   < 5 µs per pick, 27 full-pack probes, riparian/shore trees, native-only shrubs, no trees in
   river channels or land trees under water, thin-pool blending, northern oak limits). Report:
   add `--report [--step 1] [--spots] [--coverage] [--latvia] [--fallback] [--bench] [--probes]
   [--points "lat,lon;..."] [--shrubs "lat,lon;..."] [--tiers "lat,lon;..."]`.
5. `dotnet build -c Debug`; `tools/run-regional-trees-test.ps1` and `-VanillaOnly` start an
   isolated self-test server (selection traces list region, generator and tier).
6. Pack: `dotnet build -c Release`, then `tools/build-regional-tree-pack.ps1` (records Earth and
   Cylinder World hashes in `modpack/pack-lock.json`); `tests/PackInstallerTests.ps1` (offline).
