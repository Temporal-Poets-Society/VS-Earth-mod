# Resource coverage correction — 29 September 2026

The earlier maps were inadequate for judging global resource distribution. They omitted major coalfields, gypsum regions and other well-established occurrences. Aggregating US-heavy point records reduced visual clutter but did not repair missing geological evidence. The corrected [map atlas](research/remaining-ores-2026-09-29/index.html) now includes geological survey areas, historical records, concealed deposits and broader occurrence compilations. **All 42 resource layers have been audited; none is a complete global absence map or a calibrated spawning heatmap.**

**Update, 2 October 2026.** The runtime grid was rebuilt as `earth-resource-evidence-2026-10-02-v2` after a review found that several corrections listed below were prepared but never read by the bake, or were dropped by it. Now used: the 446 Former Soviet Union coal points (Kuzbass, Donbas, Pechora, Ekibastuz; Karaganda only weakly), the 981 global potash sites (also stamped into halite), the LVGMC Latvian peat/clay areas, the BGS/GA coal context polygons (potential plane only), and Cu/Sn links of the USGS specialist compilations. Invalid and antimeridian polygons (13 porphyry Cu tracts, 7 potash tracts including all three Zechstein tracts, the Kupferschiefer tract and 12 coal polygons) are repaired with `make_valid` and part splitting instead of being dropped. Polygon evidence is weighted by covered cell fraction. The Polish (PIG-PIB MIDAS, CC BY 4.0) and Saxon (LfULG, dl-de/by-2-0) reuse terms were verified on 2 October and both remain in the runtime grid. New sources with the user's approval: USGS mineral operations outside the US (2,243 mines), PEATMAP global peatlands and Natural Earth countries (coordinate checks only). Model, metrics and remaining gaps: [v2 evidence model](remaining-ore-evidence.md#v2-evidence-model-2-october-2026). Statements below that call coal basins "context only", say one gypsum formation is withheld, or describe Polish reuse terms as unresolved describe the 29 September state.

## What changed

The [resource-by-resource audit](research/remaining-ores-2026-09-29/corrections/resource-coverage-audit.csv) covers 20 metals, including copper and tin, 20 other industrial minerals/gem groups, coal and gypsum. It records 367 country checks and 196 named metal-province queries, plus separate coal and gypsum regional checks. These controls expose omissions; passing sampled controls does not establish complete coverage.

| Evidence group | Correction | Remaining limitation |
|---|---|---|
| [Coal](research/remaining-ores-2026-09-29/corrections/coal/README.md) | 3,906 valid source polygons, 727 context polygons and 520 supplementary points. Adds British surface and concealed coal, Polish documented/archived deposits, German regional layers, NSW/Victorian coalfields, and wider Asian/American historical compilations. | Broader sedimentary basins are context only. Indonesia, southern Africa, parts of Europe/former Soviet region and Australian coalfield boundaries outside NSW/Victoria remain incomplete. |
| [Gypsum / anhydrite](research/remaining-ores-2026-09-29/corrections/gypsum/README.md) | 525 reviewed deposit/locality features and 28,753 formation features, one invalid formation withheld. Includes Salaspils/Skaistkalne legal boundaries, Polish deposits, British report localities, Spanish occurrences/geological units, Paris Basin and German units. | Mixed formations do not mean pure gypsum throughout. Pure anhydrite and frequently dissolved units are context only. This is still far from global stratigraphic coverage. |
| [Metals](research/remaining-ores-2026-09-29/corrections/metals/README.md) | 2,125 global USGS PP1802 features; 3,205 Australian identified-resource records; two explicitly sourced secondary-commodity joins. Deep Latvian iron retained separately with no invented coordinates. | Inventories overlap and favour identified resources. Seven named province windows still lack imported spatial witnesses; most commodities lack broad mineral-system potential polygons. |
| [Other resources](research/remaining-ores-2026-09-29/corrections/nonmetals/README.md) | 2,764 additional records from gemstone/diamond research, Australian national data and the USGS Afghanistan compilation; 147 valid Polish mineral-area features; four NRW halite areas supplied by the coal audit. | Historical nitrate/alum processes, ordinary quartz, olivine-bearing rocks and gem mineralogy require different models. Legacy location precision and reuse terms limit runtime use. |

These are source features or commodity associations, **not counts of independent deposits**. Source attributes, history, depth, mineral identity, scale and available grade/economic fields are retained. No current-production or commercial-reserve threshold determines inclusion. Baseline and added sources can describe the same district.

The [updated Cu/Sn preview](research/copper-tin-preview-2026-09-29/copper-tin-evidence-preview.png) includes 652 additional copper and 247 tin point associations. Its fixed-strength presence cells do not let dense reporting multiply abundance. Copper's existing assessed porphyry/sediment-hosted tracts remain separate; equivalent global tin potential polygons are still missing.

## Staicele: present, deep, and not automatically accessible

Staicele is a useful counterexample to using commercial mining as the map. A [2024 University of Latvia thesis abstract](https://dspace.lu.lv/items/f9bf4048-f4d6-48fa-a19f-fe0c605bbfbe) reports examination of Staicele and Gārsene drill cores and identifies banded iron formations. An [older LU-hosted geography account](https://priede.bf.lu.lv/LatvijasDaba/ainava/) reports iron at **688 m at Staicele**, approximately the user's 700 m, and 945 m near Gārsene. Those are reported historical depths, not a surveyed depth-to-top surface for an entire deposit or a current feasibility assessment.

The [Latvia deep-evidence records](research/remaining-ores-2026-09-29/corrections/metals/latvia-deep-evidence.geojson) therefore retain positive iron evidence and the depth qualification. Geometry remains null until reliable borehole/deposit coordinates are obtained: a town centre must not become an invented ore footprint. These records count as evidence even though they cannot yet be drawn accurately. Core assays cannot be promoted to a whole-deposit average grade.

For Earth, **presence, depth/accessibility, grade, deposit size and economics need separate fields**. A deposit can be uneconomic because it is deep, small, metallurgically difficult or expensive to reach, even when its mineralisation is substantial. Conversely, a trace-metal occurrence does not establish usable ore. Unknown economics and grade remain unknown.

## Generation policy this evidence supports

1. **Known geological evidence:** retain historical, closed, exhausted, unworked, deep and subeconomic deposits. Modern mining is one attribute, never the inclusion test. Preindustrial availability needs its own accessibility interpretation.
2. **Geological potential:** use documented mineral systems and appropriate host rocks, stratigraphy and structures. Host rock alone is necessary for some deposits but rarely sufficient. Analyst search boxes and country production totals cannot become permissive polygons.
3. **Unmapped areas:** retain uncertainty. A defensible host/mineral-system model can allow a low background chance; an empty catalogue cannot prove zero potential. Current geological coverage is insufficient to calculate credible global travel distances for copper/tin.
4. **Frequency and quality:** vanilla rarity settings can scale the eventual background opportunity. Low *geological prospectivity* may inform a tested, explicit gameplay grade distribution; low *survey coverage* cannot justify poor grade. Do not mistake these for the same probability.
5. **Depth:** model cover and layers before placing the deposit. If Earth needs vertical compression, make the rule explicit and test progression consequences. Horizontal world scale does not authorize converting a 700 m buried deposit into surface iron. Bog iron is a separate near-surface wetland/peat-associated process, not the shallow version of deep basement iron.

The geological evidence and these policies belong in Earth-owned data and rules. Optional providers map supported geological/material identities to their available blocks and generators. Resource evidence should not depend on a particular mod's IDs, recipe balance or continued availability. Metallic ore placement remains deferred until the relevant evidence and host layers are suitable.

## Salt bug found during this audit

The existing regional salt filter treated a small, positive-only salt dataset as exhaustive. It rejected native salt deposits and zeroed salt prospecting-map values outside the mapped cells/kernels. That made missing research data an unjustified hard exclusion.

The source correction preserves native salt attempts and ore-map values throughout valid Earth coordinates, while retaining existing physical restrictions such as gutters/beyond-pole regions and inappropriate exposed/ice locations. Positive evidence queries remain available for diagnostics. This introduces no new ore boost or metallic-ore heatmap. Previously generated deposits and already cached zeroed maps are not regenerated automatically; fresh regions/worlds exercise the corrected path.

The fix is built in an isolated Debug output with zero warnings/errors; it does not replace the concurrently created 0.11.0 release archive or installed mods. The geology regression executable passes **259,304 checks**, including preservation of native salt values and placement opportunities in unmapped regions. Two [isolated server runs](research/resource-coverage-salt-tests-2026-09-29.json), with vanilla and full optional providers, completed all five sites each with zero world-generation/server errors and graceful shutdown. Native halite prospecting values are positive at the previously unmapped Latvia, England, Poland and Australia test sites. These tests verify restored native opportunities, not calibrated geographic accuracy or guaranteed accessible salt.

## Validation and reproduction

The [overlay registry](research/remaining-ores-2026-09-29/corrections/overlay-registry.json) lists every added spatial input. Each research subfolder retains acquisition, provenance and validation records. The [correction validation](research/remaining-ores-2026-09-29/corrections/validation.json) checks the integrated inputs, geometry, all-resource audit, map outputs and local links. It is separate from the preserved original package validation, which only covered the earlier baseline.

The maps distinguish point cells, source-mapped evidence areas and contextual areas. They preserve tiny boundaries even when barely visible at world scale; labelled regional locators are display aids, not buffers. Country controls and province queries are coverage alarms, not exhaustive geological certification. All 42 layers remain explicitly marked **not ready for global ore generation**.

Rebuild with the bundled Python runtime and the isolated dependencies described in the package READMEs, from the Earth workspace:

```powershell
python docs/research/remaining-ores-2026-09-29/corrections/nonmetals/audit.py
python docs/research/remaining-ores-2026-09-29/corrections/build_audit.py
python docs/research/remaining-ores-2026-09-29/render_maps.py
python docs/research/copper-tin-preview-2026-09-29/render_preview.py --porphyry docs/research/copper-tin-global-2026-09-29/normalized/pCu_tracts-display-only.geojson --sedimentary docs/research/copper-tin-global-2026-09-29/normalized/sedCu_tracts-display-only.geojson
python docs/research/remaining-ores-2026-09-29/corrections/validate_integrated.py
```
