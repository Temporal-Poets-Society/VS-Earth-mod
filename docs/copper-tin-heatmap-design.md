# Copper and tin heatmaps for regional generation

Date: 29 September 2026. Status: proposed data interpretation and integration; no generation changes.

**Implementation update:** [Earth 0.12.0 regional ores](regional-ores-implementation.md) implements the first editable suitability model and host-gated spawning. The following remains the design/research record; coverage and progression-validation gaps have not been declared solved.

**2 October 2026 (evidence model v2):** the baked grid now follows several recommendations below that v1 did not: evidence is tiered by record type and size instead of one fixed peak; dense MRDS reporting is normalised by local survey effort (commodity enrichment relative to all MRDS records in a 2.5-degree window, plus effort damping and wider kernels for lone records in sparsely surveyed regions); assessment tracts are area-fraction weighted; and low-weight (0.15-0.2) host-rock priors from 0.5-degree GLiM lithology mark arc volcanics/intermediate plutonics (Cu 0.2), basic volcanics (Cu 0.15) and granites plus their aureoles (Sn/W 0.15-0.2) in the potential plane. These priors are gameplay permissiveness from dominant cell lithology, not reviewed mineral-system polygons; the reviewed-province work described here is still outstanding. Details and before/after metrics: [remaining-ore evidence, v2 section](remaining-ore-evidence.md#v2-evidence-model-2-october-2026).

## Purpose

Use the requested global modern, historical and theoretical occurrence research to support geographically credible ore generation. Include small and subeconomic occurrences where documented. Treat geological potential as useful evidence without presenting it as a measured ore deposit or a calibrated discovery probability.

**User clarification:** allow a low background spawn chance where the regional geology and local rocks could contain the ore, even if no occurrence has been mapped. Reviewed permissive geology supplies this background eligibility; documented occurrences can supply stronger local hotspots. Missing mine records must not force zero chance in an otherwise suitable region.

The [resource-access plan](geology-resource-access-plan.md) remains the progression requirement. Copper supply must support the first tools and anvil; peat-associated bog iron does not remove that dependency. A map of metal occurrence alone cannot establish a viable start.

The [initial global data package](copper-tin-global-data.md) now supplies documented sites, 240 copper permissive regions and cited historical cases. The [evidence preview](research/copper-tin-preview-2026-09-29/copper-tin-evidence-preview.png) shows fixed-strength one-degree presence cells from the point records and 229 valid copper regions; 11 source polygons with invalid geometry are withheld from this preview. Tin potential and historical extraction footprints remain incomplete. The preview is not a spawn-probability map.

## Separate the questions before combining layers

| Layer | Question it answers | What it cannot establish alone |
|---|---|---|
| Documented occurrences | Where has copper or tin mineralization been recorded? | Recoverable quantity, surface access, or whether a record refers to a small showing versus a large deposit |
| Historical extraction | Where and in what form was material obtained in the past? | Remaining modern reserves or availability with the game's starting equipment |
| Geological potential | Where do reviewed mineral systems make additional occurrences plausible? | An actual discovery, grade, tonnage or assured local supply |
| Secondary deposits | Where could weathered or transported material accumulate, supported by source and transport evidence? | Ore directly beneath a loose find; every river being metal-bearing |
| Grade, depth and deposit form | What kind of material is present and how accessible is it? | Economic viability independent of date, technology and extraction conditions |
| Coverage and confidence | Where are the data useful, coarse, uncertain or missing? | Absence of minerals in areas without records |

Keep copper and tin separate. Keep deposit styles separate where they imply different host rocks, ore minerals, geometry or extraction paths. A single blurred map of all metal-related records is insufficient.

## Evidence states

The authoring catalogue should distinguish:

- **Observed:** a source records mineralization or a deposit. Preserve the source's feature type and economic status. Missing grade remains unknown; do not label every occurrence or closed mine as low grade.
- **Historical extraction:** extraction/collection is supported for a stated period and material. An archaeological artifact or trade destination alone is evidence of use or exchange, not local natural supply.
- **Assessed potential:** a cited geological assessment or documented interpretation supports a mineral system or prospective tract. Retain its assumptions, deposit types, depth scope and map scale.
- **Author inference:** Earth derives a proposed area from cited geology. Record the method and inputs, mark it visibly as inference and require review before enabling it for generation.
- **Unassessed:** available data do not support a decision. Render as no-data, not as zero metal.
- **Excluded for a specific purpose:** reviewed evidence rules out the selected deposit style, or its depth/extraction requirements rule it out as an early supply. This is narrower than asserting that the region contains no copper or tin at all.

Geological potential must use more than a broad rock-name match. The agent's mineral-system research should establish which combinations of source rocks, geological history, structures, alteration and sediment transport are meaningful. Having granite, basalt or sandstone is not enough by itself to assign a deposit.

## Build process

1. Preserve raw data, source identifiers, retrieval dates, licenses and checksums. Keep source geometries/CRS and a normalized geographic copy with explicit longitude/latitude ordering. Record positional precision; decimal places are not an accuracy estimate.
2. Classify records before calculating weights. Exclude standalone processing plants, smelters and trade/find locations from natural occurrence layers unless independent evidence establishes local mineralization. Identify overlapping records: retain all supporting references, but avoid making one district richer because several catalogues describe the same deposit. Record separate mines within a district without assuming they are independent geological systems.
3. Create reviewed geological regions and deposit-specific eligibility. Point coordinates are evidence locations, not complete deposit footprints. Broad assessment polygons are not uniformly mineralized rock volumes. Where a footprint is unknown, retain that uncertainty rather than inventing a survey boundary.
4. Build surface and secondary collection layers independently from buried mineralization. Transport-based candidates require reviewed source and sediment context. Historical workings and industrial tailings must remain distinguishable from natural deposits.
5. Produce authoring maps with switchable evidence and uncertainty layers. A point-density view may be useful for finding data gaps, but label it as record density and never use it directly as ore abundance.
6. Review candidate generation weights by deposit style and region. At first, use ordinal potential classes with explicit rules. Do not call an arbitrary normalized score a percentage chance of finding ore. Numeric spawn rates and block quantities are game calibration, stored separately from the scientific evidence.
7. Bake approved layers into versioned local data and validate regional generation and resource access. Runtime generation should not depend on survey websites or live APIs.

The first meaningful preview should show documented occurrences, available assessed tracts and missing coverage. It may still have substantial gaps. A visually complete global heatmap created by interpolating between known mines would hide those gaps and spread evidence across unrelated geology.

The initial raw MRDS audit illustrates this: its copper- and tin-related records include processing plants, and US records account for about 78% of the copper selection and 51% of the tin selection. These are properties of this catalogue selection, not shares of world mineral resources. The source report must preserve coverage and exclusion counts. Neither raw point density nor forcing equal values by country is an appropriate correction.

## Generation interpretation

A proposed lookup returns eligibility, deposit style, potential class, evidence class and data version. It then combines with the generated host rock, surface/depth conditions and the chosen content adapter to select an eligible deposit. Missing provider assets must be handled through the [regional architecture's mapping policy](regional-world-architecture.md).

Within a reviewed permissive region, suitable local host rocks and deposit conditions can enable a low baseline chance without any nearby recorded mine. Documented mineralization may raise the local weight. Use a deterministic world seed for individual placements. The baseline is a configurable game representation of unrecorded geological potential, not a measured probability. Its exact rate remains to be calibrated; a region with neither occurrence evidence nor a geological assessment remains visibly unassessed.

Keep **frequency, quantity, grade, depth and visibility** as separate controls. Documented subeconomic material may justify small or poor deposits, but it should not automatically become rich starter nuggets. A giant buried modern deposit may have high metal potential and no pre-metal access. A small secondary deposit could be a useful first supply if its recoverable quantity and tools satisfy the actual recipes.

### Agreed tuning direction for the later ore pass

The maintainer's 29 September clarification applies to all ore commodities: use the existing vanilla occurrence/rarity settings wherever their actual semantics fit. These should primarily tune the weak/background part of the distribution outside reviewed hotspots, rather than erase the contrast between resource provinces. A low-potential setting should also make high-quality deposits less likely. This is a requested game rule, not a claim that undocumented geology necessarily has poor grade.

The [remaining-ore audit](remaining-ore-evidence.md) verifies the relevant settings: `globalDepositSpawnRate` patches the global deposit `chanceMultiplier`, while `surfaceCopperDeposits` and `surfaceTinDeposits` patch the corresponding surface `triesPerChunk`. Normal disc/child deposit grades are chosen separately. Earth will therefore need a narrow quality adapter to implement the requested coupling; changing those frequency settings alone will not do it. The audit distinguishes the installed asset verification from pinned public source, whose current revision is not assumed binary-identical to the installed game.

Keep the evidence score and the user multiplier separate. A proposed implementation first establishes deposit-style eligibility from the regional mineral system, generated host rocks and process/depth conditions, then samples a potential class. That class controls both an occurrence distribution and a conditional quality distribution. Vanilla controls can adjust those distributions after their actual implementation has been inspected. Do not invent a new vanilla “ore quality” option or silently repurpose a setting that affects something else.

The background remains zero where the particular deposit style is excluded. It can be small but nonzero where suitable geology is reviewed even without a mapped occurrence. Lack of survey data alone is neither an exclusion nor evidence for poor grade. High grades must be progressively rarer in the weak-potential game distribution, with the chosen probabilities documented as balancing parameters. Preserving an occasional rich find is a separate configurable decision; it must not make low-potential regions statistically equivalent to established resource provinces.

Test the result over many seeds with occurrence counts, quality distributions and accessible quantities reported separately for hotspots, permissive background and exclusions. Also verify that prospecting reflects the same final distribution. The current regional geology/fauna implementation deliberately does not install these ore rules yet; the evidence maps are the next review input.

Avoid circular inference: a desired travel time cannot justify inventing a geological source. Use the generated distribution to measure travel, then adjust supported quantities or discoverability within credible areas if appropriate. Clearly identify regions where the available data or actual generated supply cannot support the claimed progression.

## Fit with Earth's existing geography

[EarthData](../src/Core/EarthData.cs) already loads geographic grids, compressed terrain tiles and bounded caches. Geology can follow that offline-data pattern without modifying the existing terrain file format during this research pass. Choose its resolution from the source precision and required world scale; upsampling a coarse province map does not add geological knowledge.

Use [EarthGeometry.ToGeo](../src/Core/EarthGeometry.cs) for runtime sampling and `FromGeo` for world-coordinate previews. Respect wrapping, the Pacific seam, the inserted sea gutter and beyond-pole areas. Do not smear mainland mineral potential into the artificial gutter or polar filler. Categorical evidence and deposit IDs need categorical sampling; continuous reviewed weights can have suitable boundary treatment without crossing geological exclusions.

The visual heatmap, the deposited blocks and prospecting results should describe the same approved distribution. Save its dataset and generation-profile versions with the world. New datasets affect new generation only under an explicit update policy.

## Acceptance of the data foundation

- Every imported record or region is traceable to a source; every inference has a method and evidence references.
- Coordinates, commodity classification, duplicate handling, missing values and record counts are checked against the source.
- Survey coverage, geometry precision and redistribution limits are visible in the report.
- Historical supply, modern economics and hypothetical potential remain distinguishable in exports and maps.
- Starter accessibility is tested separately from abundance, including first-anvil quantities and copper-stage tin.

This design does not yet supply calibrated ore probabilities, generated deposits or measured global travel distances. Those follow the reviewed source catalogue and gameplay validation.
