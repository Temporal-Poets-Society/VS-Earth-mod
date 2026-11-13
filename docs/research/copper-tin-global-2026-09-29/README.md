# Copper and tin research data — 29 September 2026

This directory is a research package, not runtime assets. See the [main report](../../copper-tin-global-data.md) and [heatmap design](../../copper-tin-heatmap-design.md).

## Files

- `raw/`: downloaded USGS CSV/GIS archives, report, metadata and source responses. `gsc-tin-tungsten-report-response.html` is an unsuccessful archive-notice response, **not** the GSC report or data.
- `source-inventory.json` / `.csv`: ten primary-source datasets/discovery paths, acquisition status, coverage, coordinates/resolution, source date, terms and biases.
- `acquisition-manifest.json`: authoritative latest recorded attempt for each acquisition URL, including sizes and SHA-256 hashes. `download-manifest.json` records the earlier mostly failed Python requests; `powershell-download-manifest.json` records the successful PowerShell retry.
- `normalized/`: 47,127 commodity-specific MRDS natural-site candidate rows, 672 excluded processing-plant commodity rows, 3,420 specialist Cu source records, 240 full permissive polygons, reference tables and original source metadata. Entries overlap across sources and must not be added as unique deposits.
- `historical-overlay.json` / `.csv`: seven cited historical extraction entries. Coordinates are deliberately absent until a verified source-district footprint can be linked. Source and find/smelter locations are distinct.
- `validation-summary.json`: counts, coverage by source country labels, source status/quality summaries, and invalid source geometry identifiers.
- `country-coverage.csv`: source-country record counts for coverage review, never an ore-abundance estimate.
- `normalized-manifest.json`: output paths, sizes and SHA-256 hashes for the normalization run.

## Schema and normalization

`record_id` is source-specific and commodity-specific; `source_feature_id` preserves the original identifier/layer. `source_url` and `source_references` supply provenance. Coordinates are longitude, latitude in EPSG:4326. Location precision is qualitative because source decimal places do not establish accuracy.

`grade_value` and `grade_unit` are null unless supported by a source numeric field. Specialist Cu grades are deposit-average wt% Cu; MRDS compact records have no numeric grade. Source negative missing-value sentinel `-9999` and non-finite numbers become null. Nine source-zero grades in reference prospects are withheld as unvalidated rather than interpreted as absent copper; original source CSVs retain the zero. Text such as `NA` in original attributes is retained and requires field-specific interpretation.

`economic_status` and `early_accessibility` are unknown/unassessed. Development labels are preserved in `source_status`; they do not determine grade or accessibility. `historical_period` in MRDS rows preserves source production-year strings and flags, not a modern-to-ancient classification. The cited archaeological overlay is separate.

`heatmap_eligible=true` only means automatic source-record-type screening passed. It is not permission to generate at every point. Processing plants are excluded because they can import their feed. Remaining candidates still need duplicate matching, source review, district outlines, local host-rock checks and exposure/quantity rules.

`occurrence_form` is generally unknown. Explicit placer/alluvial words in MRDS deposit/model fields are flagged for review. A copper primary-system classification does not establish surface availability. No transport footprints are invented.

The `*-source.csv` tables preserve source fields (with non-finite/-9999 missing numerics converted to null); the raw archive preserves exact originals. The normalized tract GeoJSON includes source attributes and flags source-invalid geometries. `*-display-only.geojson` files simplify polygons by 0.02 degrees with topology preservation; they are for overview rendering only, not generation decisions. No invalid geometry was silently repaired.

## Reproduction

`acquire.ps1` retrieves the public USGS baseline using PowerShell; it does not install software or modify the game. `acquire.py` preserves the first attempted retrieval method, which mostly received HTTP 403 responses on this host. `normalize.py` requires Python, numpy, pyogrio 0.13.0 and shapely 2.1.2. The two latter packages were installed only under the OS temporary directory `Earth-copper-tin-tools`; no game/mod dependencies were changed. Adjust that temporary library path if reproducing elsewhere. `finalize.py` creates inventory CSVs and the authoritative acquisition audit, and validates file signatures.

The source files are frozen snapshots. Re-downloads can change upstream content; preserve an existing audit if updating. Normalization has no network calls. Source-specific GIS import and preview dependencies are research tooling, not an Earth runtime requirement.

## Remaining gates

The global copper archive represents two mineral systems and is intended for 1:1,000,000 or coarser use. Source depth scopes are not early-accessible depths. Eleven source polygons need topological review. MRDS has strong US bias and sparse/dated records. No global tin permissive polygon layer or worldwide historical extraction layer is complete. A scientifically and geographically reviewed low game-generation chance can still be enabled in suitable regional geology without a mapped occurrence; it must remain distinguished from measured ore.
