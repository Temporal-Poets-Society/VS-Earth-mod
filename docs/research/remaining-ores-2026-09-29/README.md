# Remaining-ore evidence package — 29 September 2026

**Revised after coverage review:** start with the [correction report](../../resource-coverage-correction.md), [42-resource audit](corrections/resource-coverage-audit.csv) and updated atlas. The `corrections/` subfolders add source areas and broader occurrence evidence; their validation is separate from the original package manifest. `corrections/baseline/` preserves the previous renderer/atlas and selected maps. The old inventory below describes baseline inputs, not all corrected inputs. All 42 layers, including copper/tin, remain incomplete for global generation.

Start with the [report](../../remaining-ore-evidence.md) or [local map atlas](index.html). Research only: nothing here is an ore-generation probability, a completed regional biome implementation, or a distributable runtime dataset.

## Deliverables

- `maps/`: 40 individual commodity PNGs, five contact sheets, three explicit-mineral sheets, and the separate evaporite-permissiveness figure.
- `normalized/mrds-records.csv.gz`: one row per retained MRDS source ID, with source references and null unreported grade/economics/access fields.
- `normalized/record-commodities.csv.gz`: many-to-many links to 40 commodities, distinguishing source commodity labels from explicit ore-mineral-only evidence.
- `normalized/record-explicit-minerals.csv.gz`: named mineral mentions in the MRDS **ore** field. Gangue does not become mineable ore.
- `normalized/*-occurrences.csv.gz` and `*-occurrences.geojson.gz`: separate per-commodity MRDS tables and geometry.
- `normalized/*-specialist.geojson`, `specialist-records.csv`, `specialist-commodity-links.csv`, `specialist-grades.csv`: specialist source evidence, with positive grades and stated units/bases. Source records can overlap MRDS and each other.
- `normalized/*coverage.csv`: commodity and country coverage for MRDS and specialist sources. Source counts are not unique geological deposit counts.
- `normalized/duplicate-candidates.json`, `cross-source-duplicate-candidates.json`: strict name/country/coordinate candidates only; incomplete entity matching. No claim that everything else is unique.
- `normalized/PotashTracts.geojson`, `PotashDeps.geojson`: full, unchanged-source geometry with source attributes and validity flags. Seven invalid tracts are preserved for review but withheld from the potential figure. The `*-display-only.geojson` simplification is for browsing, not generation.
- `normalized/fsu-coal-points.geojson`: 446 regional points from two legacy source layers. Original Pulkovo 1942 coordinates and applied transformation descriptions accompany WGS84 display coordinates. Rank/name joins remain withheld until fully documented; point layers may overlap.
- `game-mineral-scope.csv`, `game-asset-audit.json`: actual installed vanilla and uninstalled Geology Additions 1.4.8 mineral/deposit scope, hashes and generator settings.
- `permissiveness-review-catalogue.json`: model/province review checklist for every commodity. Only potash has newly acquired expert spatial permissive tracts. Narrative geological models are not eligibility polygons.
- `historical-evidence.json`: eight cited historical cases, with null geometry pending verified extraction footprints.
- `source-inventory.json` / `.csv`: acquisition state, scope, licensing and limits; `acquisition-manifest.json` and `coal-acquisition-manifest.json`: download URLs, failures and hashes.
- `package-manifest.json`, `final-validation.json`: output hashes and final consistency checks.

## Data meaning

Keep observed mineral sites, explicit minerals, historical extraction, measured grades, economic status, geological potential, unassessed coverage and early accessibility separate. In CSV, an empty field is null/unknown, not zero. Original source tables are retained in raw archives. Unknown grade and unknown economics are not synonymous with poor ore or uneconomic resources. Nonpositive/ambiguous specialist grades are withheld from the positive measurement table, with originals preserved.

The fixed-strength one-degree cells used in the PNGs reduce visual overplotting. They do **not** correct survey bias, imply that the entire cell is mineralized, or define an in-game influence radius. The map window is 65°S to 85°N; normalized points outside it are not deleted. Empty map cells are unassessed. No density smoothing or country-by-country abundance normalization is used.

Raw source archives and source-code excerpts must stay outside runtime releases. The existing Cu/Sn MRDS ZIP is reused in place at `../copper-tin-global-2026-09-29/raw/mrds-csv.zip`; its SHA-256 is recorded in the validation summary. It is not copied or modified.

## Reproduction

Run with Python 3.12 from the Earth workspace:

```text
python docs/research/remaining-ores-2026-09-29/acquire_supplements.py
python docs/research/remaining-ores-2026-09-29/audit_game_assets.py
python docs/research/remaining-ores-2026-09-29/build_mrds.py
python docs/research/remaining-ores-2026-09-29/normalize_supplements.py
python docs/research/remaining-ores-2026-09-29/export_potash.py
python docs/research/remaining-ores-2026-09-29/export_coal.py
python docs/research/remaining-ores-2026-09-29/build_review_catalogues.py
python docs/research/remaining-ores-2026-09-29/render_maps.py
python docs/research/remaining-ores-2026-09-29/render_potential.py
python docs/research/remaining-ores-2026-09-29/finalize.py
```

The standard-library MRDS/supplement parsers work without scientific packages. Asset inspection uses `json5`. Since 2 October 2026 every script here uses the project venv (`.venv`, packages pinned in [tools/requirements-ore.txt](../../../tools/requirements-ore.txt): numpy, shapely, pyogrio, pyproj, matplotlib, xlrd); the former `%TEMP%\Earth-*` `sys.path` insertions were removed. Plotting uses Earth's existing `preview/grids.npz` land/water mask. No game runtime dependency was added. The coal manifest records the exact legacy ASCII URLs; some companion shapefile links are broken and are intentionally reported as failures. Re-run only against preserved raw inputs for deterministic scientific interpretation; gzip timestamps and rendering-library versions can change binary hashes without changing table values.

## Reuse

USGS metadata are preserved per release. Several say `Use_Constraints: none`; the Ni/Cr/PGE and potash releases additionally require respecting map-scale/source limitations, attribution and non-endorsement. Do not remove those caveats. The former-Soviet **coal layers** state no use constraints, but the parent project includes separately restricted ESRI coastline/country data. Those boundary data were not acquired or used; Earth's existing terrain mask supplies the preview background.

Historical-source pages are cited and briefly paraphrased; no maps or protected heritage geometry were copied. Game/provider files are only read for research and represented by metadata/hashes; no mod archives or assets should ship from this folder. Geology Additions remains optional and uninstalled.

## 2 October 2026 additions (runtime evidence model v2)

The runtime grid `assets/earthmap/config/ore-heatmaps-v1.json` (dataset `earth-resource-evidence-2026-10-02-v2`) is rebuilt by `tools/bake_ore_heatmaps.py`; the model, metrics and remaining gaps are documented in [docs/remaining-ore-evidence.md](../../remaining-ore-evidence.md#v2-evidence-model-2-october-2026). New or changed inputs in this folder:

- `normalize_supplements.py` now keeps Cu, Sn and clay links (written to `normalized/Cu-specialist.geojson`, `Sn-specialist.geojson`, `clay-specialist.geojson`), adds VMS/SEDEX Cu grades and records `ore_tonnage_mt`, `production_status` and `source_record_type` for tiering.
- `corrections/overlay-registry.json` gained `role`, `tier`, `status`, `licence` fields and the previously unread `normalized/fsu-coal-points.geojson`, `normalized/PotashDeps.geojson`, the LVGMC peat/clay areas, USGS minfac mines, PEATMAP and the copper/potash tracts. Entries whose `status` starts with `hold` are excluded from the bake.
- `corrections/nonmetals/latvia_lvgmc.py` -> `corrections/nonmetals/normalized/latvia-lvgmc-commodities.geojson` (111 peat and 6 clay licensed extraction areas, CC0).
- `corrections/minfac/normalize_minfac.py` -> `corrections/minfac/normalized/usgs-minfac-mines.geojson` (2,243 extraction sites outside the US from USGS "Mineral operations outside the United States"; plants/smelters/refineries excluded).
- `corrections/peat/normalize_peatmap.py` -> `corrections/peat/normalized/peatmap-fraction-0p5deg.npz` (PEATMAP peatland area fraction per 0.5-degree cell, CC BY 4.0).
- `corrections/coordinate-corrections.csv` (reviewed relocations/drops, e.g. Mir-Sputnik, Kivu, Koma Bangou) and `corrections/licence-verification-2026-10-02.json` (PGI MIDAS = CC BY 4.0 via dane.gov.pl; LfULG Saxony = dl-de/by-2-0).
- `acquisition-2026-10-02.json`: URLs, sizes, SHA-256 and licences of the 2 October downloads (USGS minfac, PEATMAP, Natural Earth admin-0) and of the sources that could not be fetched.
