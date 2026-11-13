# Earth 0.10.0: regional geology, fauna and ore evidence

**Later correction:** the original resource maps and salt exclusion policy described in this release snapshot were insufficient. See the [42-resource coverage correction](resource-coverage-correction.md). Corrected source preserves native salt opportunity in unmapped ground; this historical release archive has not been rebuilt by that work.

Implementation and research snapshot: 29 September 2026, Vintage Story 1.22.7.

This release extends the regional trees from 0.9.0 with independent geology and optional fauna components.
Geographic metallic ore generation is a later step. The new ore maps are review inputs, not installed spawn probabilities.

## Delivered components

| Component | Implemented scope | Important boundary |
|---|---|---|
| Geology | Global GLiM/GUM lithology and sediment context; broad rock profiles; separate peat/clay eligibility; limited halite evidence and prospecting masking; optional Geology Additions materials | The 0.5° source categories do not determine exact formations or thicknesses. Unknown bedrock retains legacy strata. Buried salt and clay mineralogy coverage remain incomplete. |
| Fauna | 278 taxon mappings across 20 optional Fauna of the Stone Age packs; 245 enabled and 33 held; natural-spawn range/habitat checks, corrected ground-climate sampling and configurable extinct animals | Unsupported taxa are held for review. Region/habitat eligibility does not guarantee a local population; provider conditions and population rules still matter. |
| Ore research | 40 remaining commodity maps, explicit mineral-form views, source records, historical examples and permissiveness review catalogue | Source coverage is uneven. Missing records do not establish geological absence. Global potential polygons, accessible quantities and grade models are still incomplete. |

Detailed reports: [geology](regional-geology-implementation.md), [fauna](regional-fauna-implementation.md),
[remaining ores](remaining-ore-evidence.md), and [the broader architecture](regional-world-architecture.md).

## Maps and data

- [Browsable atlas of remaining commodities](research/remaining-ores-2026-09-29/index.html)
- [Copper/tin preview](research/copper-tin-preview-2026-09-29/copper-tin-evidence-preview.png)
- [Copper/tin source catalogue](copper-tin-global-data.md)
- [Future ore-generation interpretation and tuning](copper-tin-heatmap-design.md)

The revised copper/tin preview combines records into 2,747 copper and 592 tin presence cells, with the existing
229 valid copper-potential polygons retained. A cell has the same display strength whether one or many source
records occur there. This limits repeated-record emphasis without inventing equal survey coverage by country.
Exact source coordinates remain in the exports. The other atlas uses the same one-degree presence convention,
with MRDS and specialist evidence distinguished. Neither visualization is an abundance or probability heatmap.

The agreed later ore policy keeps host-rock and mineral-system eligibility ahead of probability. Vanilla
`globalDepositSpawnRate`, `surfaceCopperDeposits` and `surfaceTinDeposits` can supply frequency inputs, especially
for permissive background outside stronger evidence areas. Grade is selected separately by vanilla, so Earth's
requested link between weak potential and rarer high-quality ore requires a separate quality adjustment.
Survey uncertainty must not itself be treated as evidence of poor ore.

## Use and compatibility

Build or install `release/earthmap-0.10.0.zip`. Vanilla content works without the optional providers. The tested
optional setup uses the 20 fauna and eight Floral Zones releases in the [release inventory](research/ecology-2026-09-28/release-inventory.json),
plus Wildcraft Trees, Herbarium and Geology Additions 1.4.8. The older `Earth-Regional-Trees-0.9.0.zip` installer
remains an earlier release; it has not been silently updated to this setup.

The [release package audit](research/regional-world-release-2026-09-29.json) records the final ZIP checksum,
version and required-entry/content-hash checks. The Release build completed with zero warnings or errors.
The package contains runtime data and attribution files; raw research and test profiles are excluded.

New Earth worlds default to `RegionalTrees=true`, `RegionalGeology=true`, `RegionalFauna=true` and
`IncludeExtinctAnimals=true`. The first three choices/rule versions are saved with the world. Existing saves
retain their old settings unless the corresponding `UpgradeExistingWorld…Rules` option is explicitly enabled.
Reset that upgrade option after restarting. Geology updates affect new generation; existing chunks and cached
map regions retain their previous contents. Creating a new world gives the most coherent regional geology.

`IncludeExtinctAnimals` is applied on restart to future natural spawns in a world with regional fauna enabled.
It does not delete animals already present. `/earth fauna` and `/earth geology` explain local eligibility and
data limitations. Geology saves also freeze the selected data/provider mapping; missing or changed mappings
require restoration or an explicit upgrade. Removing a content mod still requires handling its saved blocks
and entities, even though Earth's geographic rules are independent of the provider.

The integration includes narrowly scoped repairs for Felinae 0.2.14's silent-lynx sound-range declaration and
Geology Additions 1.4.8's short child-deposit grade arrays. The latter applies the existing parent-generator
clamping behavior to 33 affected host mappings. It is a compatibility repair, not geographic ore placement
or the future ore-quality policy. Advanced Geology and Rock Strata Variety have no reviewed coexistence mode
with Earth's enabled geology generator.

## Validation

The [machine-readable runtime audit](research/regional-world-tests-2026-09-29.json) records exact test snapshots,
DLL/data hashes, settings, site counts and errors. Tests use hidden localhost servers and isolated workspace
profiles; installed player mods and saves were not changed. A legacy test uses a copy of a prior 0.9 test save.

Six completed isolated runs cover 18 vanilla site visits, one legacy-save visit, 18 full-provider visits,
ten additional full-provider visits after disabling extinct animals in a copied save, eight visits after
correcting fauna ground climate, and three final visits with detached runtime-climate probes. Some locations
are repeated; these are 58 site visits, not 58 unique locations. Each completed without server errors,
world-generation exceptions, analysis failures or site timeouts. The legacy save retained geology/fauna
disabled; the copied enabled world applied the extinct-off setting on restart.

The final actual Harmony-patched runtime climate helper passed nine detached checks covering worldgen climate,
seasonal climate callbacks and restrictive-temperature rejection for Ethiopian wolf, New Zealand rock wren and
Baltic roe deer conditions. These probes create no entities and are counted separately from natural runtime
spawning. The Ethiopian game sample used 15.68°C ground climate instead of the previous 34.04°C reference-height
value. The adapter keeps the provider's temperature/rain/height/vegetation limits. Native generation iterates
each spawn group with its own attempts; a geographic veto does not consume another species' attempt.

Final standalone checks passed: 46 regional-tree assertions, 259,285 geology grid/rule/compatibility assertions,
3,062 fauna/range/climate assertions, and the existing terrain/river regression suite. The fauna coverage audit
includes 76 region/habitat probes and a wider native-range climate screen. These are generation and compatibility
checks, not a sustained multiplayer or full client-animation playtest. Small local entity snapshots are not
population estimates.

## Remaining regional gaps

The provider/range audit distinguishes missing content, held mappings and environmental restrictions. Some
islands, including Madagascar, Cuba and Hawaii, still need additional native-species content or range review;
open-ocean whales and dolphins for
Magelan remain future work. Detailed per-location results and the extinct-on/off comparison belong in the
fauna report. Vanilla and unrelated fauna retain their own geographic rules.

Geology still needs finer bedrock beneath sediment cover, real stratigraphic thicknesses, broader buried-salt
evidence, and clay mineralogy/refractoriness. Legacy bauxite and metallic deposits remain present. The adapter
avoids selecting GA mudstone for Earth's own profiles, but does not globally remove the provider's mudstone
recipes or mudstone in preserved legacy strata. Bog iron from finite natural peat and realistic copper/tin
travel distances remain part of the later resource-generation work; bog iron does not remove the first
copper/bronze-anvil requirement.
