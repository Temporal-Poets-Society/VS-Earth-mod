# Global copper and tin: initial evidence package

**Coverage review:** the [all-resource correction](resource-coverage-correction.md) audits copper/tin alongside 40 other layers and adds 652 copper and 247 tin point associations to the linked preview. Initial acquisition counts below remain baseline counts. Deep, historical and noncommercial occurrences remain evidence; neither blank map areas nor low survey density establish absence or poor grade.

Date: 29 September 2026. Status: acquired research data and integration guidance; no gameplay implementation or calibrated ore-generation heatmap.

**2 October 2026 update (runtime dataset `earth-resource-evidence-2026-10-02-v2`, see [v2 evidence model](remaining-ore-evidence.md#v2-evidence-model-2-october-2026)):** the runtime bake now (1) reads Cu and Sn links from the USGS specialist compilations, which `normalize_supplements.py` previously intersected away (Major Deposits 662 Cu / 79 Sn, Ni-Cr-PGE 5,529 Cu / 133 Sn, VMS 852 Cu with positive `cugrd`, SEDEX/MVT 29 Cu; +881 Cu grade rows), (2) repairs instead of dropping the 13 porphyry and 1 sediment-hosted copper tracts that were invalid or antimeridian-crossing (Gangdese, Cascade Arc, Pacific Margin, Chukotka, Aleutians, Melanesia, Hercynian-Thuringian Kupferschiefer, ...), using `shapely.make_valid` and part splitting, and (3) weights records by tier instead of one 0.7 peak: Major Deposits 0.95, specialist deposits 0.75-0.85, MRDS by status and score (occurrences 0.25-0.5), with MRDS survey-density normalisation. Tract polygons are area-fraction weighted (0.5 x (0.3 + 0.7 x covered fraction)) in the potential plane. The statement below that eleven invalid polygons are "preserved and flagged, not silently repaired" still holds for the research exports; the repair happens in the bake and is counted in its manifest.

**There is a usable worldwide starting dataset now.** It contains reported occurrences beyond producing mines, specialist copper deposit records with grades where available, and expert-drawn copper permissive regions. Tin has a worldwide occurrence baseline but still needs a comparable reviewed permissiveness layer and much better historical/secondary-deposit mapping.

The requested rule is supported: **allow a low background chance inside reviewed suitable geological regions and compatible local rocks even when no occurrence has been mapped.** Known deposits can supply stronger local evidence. The low chance is a game calibration informed by geology, not a scientific probability measured by these datasets. Unmapped ground inside a permissive region is different from unassessed ground with no supporting geological model.

An [evidence-map preview](research/copper-tin-preview-2026-09-29/copper-tin-evidence-preview.png) shows fixed-strength one-degree cells containing imported point records, plus 229 valid copper tract polygons. It withholds the eleven invalid source polygons and does not show calibrated spawn probabilities. Co-located records do not strengthen a display cell; the source exports retain the individual records and their overlap.

## Delivered data

The [research folder](research/copper-tin-global-2026-09-29/README.md) includes raw archives, reproducible extraction scripts, download/hash manifests, normalized tables and GeoJSON. The [source inventory](research/copper-tin-global-2026-09-29/source-inventory.csv) records coverage, dates, exact verified download or discovery URLs, coordinate information, reuse terms and acquisition failures.

| Acquired and normalized layer | Count | Meaning |
|---|---:|---|
| MRDS copper candidate sites | 44,378 | Reported natural mineral sites after excluding 558 copper processing-plant records. Includes producers, past producers, prospects, occurrences and unknown-status sites. |
| MRDS tin candidate sites | 2,749 | Same screening, excluding 114 tin processing-plant records. |
| Specialist porphyry copper sites | 2,199 | Deposits and prospects in the USGS global assessment. |
| Specialist sediment-hosted copper sites | 1,200 | Deposits and prospects, with host/mineral/grade fields where supplied. |
| Additional Precambrian porphyry reference sites | 21 | Separate reference layer; generally not used by the source to delineate permissive tracts. |
| Porphyry copper permissive polygons | 192 | Expert geological assessment areas, not mine outlines. |
| Sediment-hosted copper permissive polygons | 48 | A mixture of quantitatively and qualitatively assessed regions. |
| Cited historical extraction overlay | 7 regional entries | Nonspatial evidence awaiting verified district footprints; does not invent coordinates. |

These are **evidence records, not 50,547 unique deposits**. The two catalogues overlap; districts and individual workings may also be separate records. Do not add their counts or heat contributions without entity matching. All normalized spatial files use WGS84 longitude/latitude (EPSG:4326).

Useful entry points:

- [MRDS copper/tin catalogue](research/copper-tin-global-2026-09-29/normalized/mrds-copper-tin-occurrences.csv), [tin GeoJSON](research/copper-tin-global-2026-09-29/normalized/mrds-tin.geojson), [compressed copper GeoJSON](research/copper-tin-global-2026-09-29/normalized/mrds-copper.geojson.gz).
- [Specialist copper catalogue](research/copper-tin-global-2026-09-29/normalized/global-copper-assessment-occurrences.csv). There are **726 positive source-reported Cu average grades**; there is no imported numeric tin-grade table yet. A missing grade stays unknown.
- [Porphyry permissive tracts](research/copper-tin-global-2026-09-29/normalized/pCu_tracts.geojson) and [sediment-hosted tracts](research/copper-tin-global-2026-09-29/normalized/sedCu_tracts.geojson). The full porphyry export is about 187 MB. Small `*-display-only.geojson` versions simplify geometry by 0.02 degrees solely for browsing.
- [Historical evidence](research/copper-tin-global-2026-09-29/historical-overlay.json), [validation results](research/copper-tin-global-2026-09-29/validation-summary.json), [authoritative acquisition manifest](research/copper-tin-global-2026-09-29/acquisition-manifest.json).

`heatmap_eligible` means only that a record passed automatic record-type screening. It does **not** mean independently verified, deduplicated, suitable for early extraction, or approved for generation.

## What “modern”, “historical” and “theoretical” mean here

Keep four evidence classes instead of forcing them onto one intensity scale:

1. **Observed mineralization:** a survey reports copper/tin at a site. A producing mine is one subtype; a small showing or unexplored occurrence also counts. Record the minerals, deposit style and source quality.
2. **Reported grade/economics:** retain measured or estimated grade, units, sample/resource basis and dated economic assessment independently. “Past producer”, “prospect” and “occurrence” do not mean low grade or uneconomic. The compact MRDS download has no numeric grade fields.
3. **Historical extraction:** evidence that people actually collected/mined the mineral, with a period, extraction form and confidence. Modern exhausted reserves must not erase a former natural deposit from a historical Earth profile. A bronze artifact, ingot cargo, smelter or settlement does not prove local ore.
4. **Geological permissiveness:** a defensible mineral system could occur in this region, even without a mapped deposit. This is inferred potential, not a measured low-grade occurrence.

For example, the specialist copper table reports Jinduicheng in China at 0.028 wt% Cu, with a mixed molybdenum/copper classification and a source comment describing mining. That low copper grade does not establish that the whole operation was uneconomic. Conversely, the NPS account of Minong describes nineteenth-century stamp rock at about 1.5% Cu as unprofitable to stamp and transport there. Economics depend on mineral form, processing, scale and circumstances; neither percentage becomes an automatic early-game yield. [USGS source database](https://pubs.usgs.gov/sir/2010/5090/z/), [NPS Minong history](https://home.nps.gov/articles/000/minong-copper.htm).

The worldwide MRDS baseline is an older compilation, not a census of active mines in 2026. A newer [2025 porphyry release](https://www.usgs.gov/data/a-global-database-porphyry-copper-deposits-and-prospects), current through spring 2024, was identified and its DOI metadata saved, but the ScienceBase attachment returned HTTP 403. It is a replacement/augmentation candidate, not a file claimed to be imported.

## Geological potential should be deposit-specific

The acquired copper polygons are a strong first approximation for the requested low-chance background. Preserve their geology, age, tectonic context, source references and assessment depth. They cover porphyry and sediment-hosted systems; they do not exhaust copper geology. Later reviewed layers must address volcanic-hosted massive sulphides, iron-oxide copper-gold, skarns, veins, native-copper systems and other supported styles.

The USGS assessment is intended for **1:1,000,000 or coarser** use. Porphyry potential generally considers deposits within 1 km of the surface. Sediment-hosted metadata describe assessed depths of 1–2.5 km in the abstract, while a purpose statement says the upper 2 km; retain the tract/report-specific scope rather than inventing one universal depth. None of these depths establishes early accessibility. Source tract outlines cannot be presented as accurate local ore boundaries. [USGS assessment](https://pubs.usgs.gov/sir/2010/5090/z/sir20105090z.pdf).

For tin, start from reviewed fertile intrusive complexes and metallogenic belts, then distinguish veins/greisen, replacement or skarn environments, and sedimentary concentrations derived from them. The USGS tin synthesis is useful for the broad mineral-system rules, including evolved granitic associations and primary versus placer sources. It does not license “tin in every granite” or “tin in every river”. Some less common tin systems also need explicit treatment. [USGS tin synthesis](https://www.usgs.gov/publications/tin).

The best global tin expansion located is the [GSC World Tin and Tungsten Deposit Database](https://www.publications.gc.ca/site/eng/9.819753/publication.html), with host rocks and deposit-style attributes. Its publication is from 2014, with most data current to early 2008. The old GEOSCAN endpoint failed, the replacement repository had an SSL connection failure in this environment, and a PDF request returned an HTML archive notice. **The actual database was not acquired.** No global tin permissive polygons have been fabricated as a substitute. The [USGS US tin release](https://www.usgs.gov/data/tin-deposits-united-states) is another useful next source because it explicitly includes enrichment without implying economic potential, but it is not global.

## Historical and secondary supplies

The initial overlay covers Minong, Penzance, Gonamena, Timna, Rooiberg/Smelterskop, Kestel/Göltepe and Mušiston. Each entry records its sources and interpretation limits. Its null geometries are intentional: a named district is not a verified polygon, and a published smelting location is not an ore coordinate.

This is a seed overlay, not globally complete historical coverage. It includes pre-industrial extraction and explicitly later examples. It keeps contested interpretation qualified, does not infer tin provenance for every ancient bronze, and does not label MRDS “Past Producer” records as Bronze Age mines. Modern source production-year strings are retained separately in the catalogue.

For natural spawn design, distinguish:

- **Primary mineralization and weathered outcrops.** Bedrock ore and surface oxidized material need different depth/visibility/processing rules.
- **Residual, eluvial and alluvial tin.** Link cassiterite concentrations to credible source geology, weathering, catchments and deposition. Transport can put ore outside its original rock unit.
- **Native copper and glacial drift.** Transported metal requires a reviewed provenance/transport area; a loose piece need not overlie a vein.
- **Historical exhaustion.** A reconstructed historical deposit may have existed before modern mining removed it; industrial dumps are a separate anthropogenic category.

The imported MRDS text explicitly identifies 399 tin and 165 copper records as placer/alluvial through the deposit/model fields. These are simple flags for review, not comprehensive secondary-source polygons. No worldwide drainage, glacial transport or pre-industrial exposure reconstruction was imported in this pass.

## Heatmap composition

Produce independent map layers before blending anything into game rules:

| Layer | Intended role | What it must not imply |
|---|---|---|
| Observed occurrence/district | Establish local positive evidence after matching duplicates and checking record type | Raw point density is true ore abundance |
| Historical extraction | Select the chosen historical profile and investigate recoverable surface forms | Smelting/find sites are local sources |
| Permissive geology | Enable low background chance within reviewed source-region and local geological conditions | Every eligible cell contains ore |
| Grade/mineral form | Guide separate local grade, size and processing choices where supported | Modern resource average equals a hand-selected surface ore grade |
| Secondary supply | Route weathered material along supported transport/deposition processes | Circular buffers or all rivers produce tin |
| Evidence/coverage | Display uncertainty, source age, survey coverage and validation status | Missing records mean zero geological potential |
| Early accessibility | Apply exposure, depth, tool, quantity and route requirements | A deep modern deposit solves first copper |

Use a reviewed mineral-system mask to establish eligibility. Inside it, define a low game-calibrated background rate; documented districts may increase local intensity or constrain local deposit style. For observed sites, the spatial influence should follow the geological body/district, not an arbitrary universal radius. Survey effort belongs in uncertainty handling, not as a direct ore multiplier. Prefer saturation or ranked district evidence to summing every duplicate point.

Local host-rock compatibility is one condition, together with source/process, structure/contact, age and geological province. At Earth scale, broad rocks may require several province-specific templates. Keep inferred procedural local geology explicitly separate from measured source data. Apply secondary transport after the source model; the receiving sediment can be geologically different from the parent rock.

Maintain separate copper and tin models and separate primary/secondary output bands. Do not convert the assessment's `Prob_0_Cu`, `N_expected`, `Mean_Cu_t` or economic-filter fields directly into block probabilities. Those quantities describe differently sized tracts, depth scopes and statistical resource models. Tune generation within credible eligibility and then run the [resource-access assessment](geology-resource-access-plan.md), including enough copper for the first anvil. Bog iron does not bypass that requirement.

## Coverage, reuse and validation limits

MRDS retained records span 113 copper and 56 tin country labels, including older naming conventions. **About 78% of retained copper records and 53% of tin records are from the United States.** Tin has only 14 Indonesian and 13 UK records in this digest; these low counts clearly cannot be read as low real-world importance. The [country-coverage table](research/copper-tin-global-2026-09-29/country-coverage.csv) makes this imbalance visible. The source A–E rating is not a numerical probability of ore. [MRDS metadata](https://mrdata.usgs.gov/metadata/mrds.xml).

The report's 50,547 source evidence rows passed unique source-key checks, coordinate-range checks and grade-sentinel checks. Raw sources and normalized outputs have SHA-256 manifests. Nine ambiguous zero grades in reference-only Precambrian prospects are withheld as unknown; source CSVs preserve those zeros for review. No tin grades have been invented. **Eleven copper source polygons fail geometry-validity checks** and are listed by source object ID. They are preserved and flagged, not silently repaired. Geometry repair/review, deduplication, host-rock rules and regional depth/access review remain required before generation.

The source inventory distinguishes acquired data from verified discovery links and blocked attachments. The USGS baseline has appropriate source metadata and attribution requirements. Other candidate data must keep their own terms: the [Nordic database](https://www.gtk.fi/en/fennoscandian-mineral-deposits-application-ore-deposits-database-and-maps/) has specific non-profit reproduction conditions and additional-license provisions, rather than a blanket unrestricted redistribution license. GSC archive terms still need checking. These optional sources have not been copied into a distributable mod dataset.

Next data work should prioritize the GSC tin catalogue, current survey data for Southeast Asia/China, Central Africa, Brazil, the Andes and Australia, and specific small/subeconomic occurrence studies; then historical extraction footprints and placer catchments. Regional surveys should also extend copper beyond the two imported mineral systems. That produces a credible low-chance background plus evidence-based concentrations, rather than a map of present-day mining investment.
