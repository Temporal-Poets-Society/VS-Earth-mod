# Coal evidence coverage correction — 29 September 2026

The original point-based coal preview missed major known coal regions. Its 240 MRDS rows included 204 from the United States, while Britain, Poland, Germany, Australia, India, Indonesia and Canada had none. Six specialist points did not fix that sampling failure. A mineral-occurrence database alone was unsuitable as a map of global coal geology. **The correction adds geological polygons; source density still must not determine ore abundance.**

This is a research package, not a generator or a globally complete coal inventory. It preserves buried, historical and non-operating evidence without requiring economic viability. Unknown depth, coal quality, mining status and accessibility remain unknown. No game code, deposits, configuration or saves were changed.

## Integration files

| File | Source features | Meaning |
|---|---:|---|
| `normalized/coal-polygons.geojson` | 3,918 | Coal-bearing strata, seams, coalfields, documented deposits and explicitly qualified historical/generalized coal areas |
| `normalized/coal-context-polygons.geojson` | 729 | 29 Australian whole-basin extents, 360 British technical opencast-potential areas and 340 historical workings areas with the **source's** 500 m buffer |
| `normalized/coal-supplement-points.geojson` | 520 | 421 Australian resource sites and 99 Central American literature occurrence points |
| `normalized/nrw-halite.geojson` | 4 | Explicit NRW rock-salt resource polygons, separate from coal; depth/accessibility unknown |
| `known-region-audit.json` / `.csv` | 21 review rows | Coverage audit, including sourced unresolved regions with null geometry |
| `source-inventory.json`, `layer-inventory.json` | Source and layer catalogues | Rights, limitations, layer identity, original CRS and record counts |
| `acquisition-manifest.json`, `validation.json` | Download and validation records | Exact URLs, dates, SHA-256 hashes, archive checks, coordinate operations and geometry defects |

All GeoJSON uses longitude/latitude, EPSG:4326. `source_attributes` preserves the original feature fields; source archive/layer and feature IDs remain traceable. `feature_kind` distinguishes what a boundary actually represents. `coal_rank_source` retains the reported label rather than guessing a game coal variant. `depth_min_m` and `depth_max_m` can be category bounds, not measured seam depth: read `depth_source_text`. Every feature has `game_generation_ready=false`.

**Exclude `geometry_valid=false` from map/spawn geometry pending review.** Twelve coal polygons and two context polygons contain source geometry defects, retained without repair or simplification. There are 3,906 currently valid coal polygons, 727 valid context polygons and 520 valid points. The four salt polygons are valid. Validation records each excluded feature and reason. GeoJSON does not imply better precision than its original map.

## Acquired coal geometry

| Primary release | Imported coal polygons | Interpretation and limitation |
|---|---:|---|
| [BGS Coal Resources for New Technologies](https://www.bgs.ac.uk/datasets/coal-resources-for-new-technologies/) | 638 | Britain and Northern Ireland: 393 surface coal-bearing strata areas, seven surface lignite areas, 219 concealed areas shallower than 1,200 m and 19 deeper than it. The less-than/deeper-than classes describe depth, not a precise seam elevation. The 1:1 million source is a legacy geological compilation, not a current producer map. |
| [PIG-PIB MIDAS](https://geologia.pgi.gov.pl/srv/rest/services/midas/MapServer) | 343 | 315 documented current-register coal deposit boundaries plus 28 selected archival boundaries. No operating-mine filter. Archives are incomplete; some original deposit boundaries were reconstructed from mining areas, with caveats retained. |
| [LfULG Saxony](https://luis.sachsen.de/geologie/rohstoffgeologische-karten.html) | 289 | 170 polygons from six individual coal-seam distributions at 1:400,000 plus 119 lignite-field polygons. Vertically overlapping seams and duplicate field inventories cannot be added as abundance. The aggregate duplicate seam layer and pit boundaries were not used. |
| [Geologischer Dienst NRW RK500](https://www.gd.nrw.de/pr_kd_rohstoffkarte-500000.php) | 6 | Five hard-coal resource polygons and one lignite multipolygon. The actual layer and attributes say **2,000 m** maximum depth; the landing page says 200 m. That conflict is flagged rather than silently treating deep coal as shallow. Separate rock-salt layer supplies four polygons. |
| [NSW Mining Coalfields](https://data.nsw.gov.au/data/dataset/nsw-mining-coalfieds) | 5 | Western, Southern, Newcastle, Hunter and Gunnedah coalfields. Structural coalfield definitions, not individual seam outlines. |
| [Victorian Coal Fields](https://discover.data.vic.gov.au/en_AU/dataset/victorian-coal-fields) | 46 | Black and brown coal fields, including historical fields; original age, overburden, development and resource fields retained. Linked seam tables were not imported. Resource tonnage is not grade. |
| [USGS India/Bangladesh, OFR 2011-1296](https://pubs.usgs.gov/of/2011/1296/) | 313 | 305 Indian and eight Bangladeshi coal-bearing area polygons, derived from GSI and cited geological sources. Some contacts interpreted beneath alluvium. Coal rank is absent and remains null. |
| [USGS China, OFR 2014-1219](https://pubs.usgs.gov/of/2014/1219/) | 906 | Historical atlas coal-mining areas as of 2001: 550 bituminous, 249 anthracite and 107 lignite polygons. Not a filter for today's operating mines; also not exhaustive geometry of all coal-bearing formations. |
| [USGS Americas, OFR 2008-1257](https://pubs.usgs.gov/of/2008/1257/) | 770 | Generalized coal-bearing areas constrained by literature and appropriate-age surficial geology. Includes Canada, Mexico, Colombia, Brazil, southern South America and Central America. Does not account adequately for structure or deep concealed coal; unsuitable for resource-volume calculations. |
| [USGS US coalfields, OFR 2012-1205](https://pubs.usgs.gov/of/2012/1205/) | 602 | Updated generalized conterminous coalfields. Overlaps the US portion of the Americas compilation. Source archives also contain detailed assessment layers that were not mixed into this extent dataset. |

Australia additionally has [29 national coal-basin context areas](https://data.gov.au/data/dataset/9d5a8d74-a201-42bd-9d49-3c392244be16) and [421 AIMR resource sites](https://services.ga.gov.au/gis/rest/services/AustraliasIdentifiedMineralResources/MapServer). The whole-basin outlines include covered regions, have a recommended regional display scale, and were released with incomplete QA. **They do not demonstrate coal throughout every basin.** Queensland, Western Australia and other states therefore have improved evidence but still lack true coalfield polygons in this correction. The Australian `RANK` attribute means geological province hierarchy; coal rank comes only from `Coal_Type`.

## Gaps that remain visible

The machine-readable audit separates newly mapped regions from missing geometry. No coordinates or buffers were invented for unresolved narrative records.

- **Indonesia:** the [Geological Agency's inventory](https://geologi.esdm.go.id/media-center/indonesia-s-mineral-and-coal-resources-and-reserves-2025) establishes major coal regions in Kalimantan and Sumatra. Its 2021 national coal-distribution PDF is retained, but no reusable coalfield polygons were normalized. The previous empty preview was a coverage failure.
- **South Africa:** [CGS provides a national coalfield map](https://www.geoscience.org.za/cgs/systems/publications/downloadable-material/); the [CSIR geological review](https://researchspace.csir.co.za/server/api/core/bitstreams/2f13ac0c-d434-4fe7-ade2-cbb62baf495a/content) includes economic and subeconomic fields. The few existing points are inadequate spatial coverage. No reviewed polygon geometry acquired here.
- **Botswana, Zimbabwe and Zambia:** [USGS geological assessment](https://pubs.usgs.gov/fs/2017/3003/fs20173003.pdf) documents the coal-bearing regional setting. A coalbed-gas assessment province is not a coal-body outline and has not been imported as one.
- **France, Saar and further European regions:** [BRGM documents Nord–Pas-de-Calais historical mining](https://www.brgm.fr/fr/reference-projet-acheve/nord-pas-calais-surveillance-travaux-urgence-deux-anciens-puits-charbon) and the [Lorraine–Saar coal-bearing setting](https://sigesrm.brgm.fr/Reservoir-minier-du-bassin-houiller-lorrain). [Czech Geological Survey summaries](https://cgs.gov.cz/sites/geology/files/extranet-eng/publications/online/mineral-commodity-summaries/MINERAL-COMMODITY-SUMMARIES-OF-THE-CZECH-REPUBLIC-2011.pdf) include exhausted resources. No corresponding national coalfield polygons were acquired in this pass. NRW and Saxony are not full German coverage; Brandenburg and other state gaps remain.
- **Former Soviet region:** the earlier package retains 446 regional source points from [USGS OFR 01-104](https://pubs.usgs.gov/of/2001/ofr-01-104/). These do not supply reviewed coalfield polygons or complete rank/depth attribution.
- Other regions remain unassessed or incomplete. A blank area means missing or unresolved evidence, never demonstrated geological absence.

## Historical, economic and gameplay interpretation

The map should combine evidence at fixed strength per evidence class or spatial unit, preserving overlap and uncertainty separately. Counts here measure **source records**, not unique coal deposits. US segmentation, overlapping publications and national surveying effort must not increase abundance. No country-density normalization is proposed.

Historic/closed/exhausted workings remain evidence of geological presence. For Earth before industrial extraction, their closure or modern depletion should not erase the original coalfield. A modern resource site need not be economically viable, shallow or accessible with early tools. Coal rank, calorific value, ash, sulphur, seam thickness, burial depth, resource tonnage and economic reserve classification are different properties; missing values are not interchanged. This correction contains no measured grade synthesis.

Later generation can use regional coal-bearing stratigraphy, age, depositional setting and depth constraints to allow a low background probability outside observed districts. Generic sandstone, shale or a whole sedimentary basin alone is insufficient evidence for coal. User rarity/frequency settings may later scale both deposit frequency and quality, but this package does not implement those controls or claim that a source polygon is a generation mask.

## Reproduction, precision and rights

`acquire.py` downloads public source URLs and hashes the bytes. `normalize.py` selects the declared layers, retains source attributes, transforms native CRSs, and writes the four GeoJSON files and inventories. `audit_validate.py` checks counts, identity, provenance, finite coordinate bounds, geometry flags, source hashes and ZIP CRCs; it writes the 21-region audit and `validation.json`. It requires Python, pyogrio, shapely and pyproj; the local research environments are referenced at the top of the scripts. Raw archives are research inputs, not runtime assets.

The original CRS is retained per feature/layer. India/Bangladesh is **Indian 1975, EPSG:4240**; the earlier draft provenance label saying Kalianpur 1937 was corrected. China uses Beijing 1954, and some conversions report a ballpark datum offset. Source map/georeferencing uncertainty dominates any extra decimal places. The older 2000 China shapefile, which lacks a declared CRS, remains raw and is deliberately excluded. Native Polish EPSG:2180 API output is stored alongside the provider's WGS84 output.

Licences and required attribution are detailed per source in `source-inventory.json`: BGS Open Government Licence; German state data under Datenlizenz Deutschland–Namensnennung 2.0; GA basin data CC BY 3.0 Australia; GA sites and Victorian fields CC BY 4.0; NSW catalogue CC Attribution; USGS releases with original source acknowledgements; and [PIG-PIB public-information reuse conditions](https://www.pgi.gov.pl/rejestry-i-bazy-danych-geologicznych/zasady-korzystania-z-isp.html). Derived Polish outputs identify the institute, snapshot, coal-only subset and reprojection; retain the full linked reuse conditions and database-specific limitations with any onward distribution. The present source inventory does not relicense underlying data.

Four attempted PGI policy-page downloads returned WAF/block-page HTML despite successful HTTP responses. `validation.json` flags these as unusable cached content; the actual official policy was read through the web tool on 29 September 2026. A separately discovered restricted Hunter coal-contour dataset was **not** downloaded; the NSW layer here is the independently published open coalfield source. Publicly viewable maps for unresolved regions have not automatically been treated as reusable vectors.
