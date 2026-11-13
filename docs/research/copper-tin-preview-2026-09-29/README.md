# Copper/tin evidence preview

[Open the map](copper-tin-evidence-preview.png).

This is an authoring preview for the [global data report](../../copper-tin-global-data.md), not an ore-probability map. The copper panel overlays recorded sites and expert-assessed potential for two deposit systems. The tin panel currently has recorded sites only. Blank regions remain unknown; survey coverage and duplicate records must not become abundance multipliers.

The revised preview aggregates 44,378 MRDS copper records plus 3,420 specialist copper records, and 2,749 MRDS tin records into fixed-strength 1° presence cells. Multiple records in one cell do not darken it. This addresses the visual effect of dense US reporting without pretending global coverage is equal or changing the evidence by country. The source records and precise coordinates remain in the research exports. Cell-centre marks are display summaries, not relocated deposits.

The coverage correction adds **652 copper and 247 tin point associations**, bringing display inputs to 48,450 and 2,996 respectively. These are overlapping source associations, not unique deposits. Added USGS PP1802, Australian identified resources, Afghanistan and explicitly reviewed secondary-commodity evidence come through the [shared correction registry](../remaining-ores-2026-09-29/corrections/overlay-registry.json). See the [all-resource correction report](../../resource-coverage-correction.md) for depth, noncommercial occurrences and unresolved geological potential. All input hashes are refreshed in `preview-audit.json`.

The map also shows 229 of 240 copper tracts. Source catalogues overlap. Eleven invalid source polygons are withheld; no repair has been silently applied. Historical case studies without verified coordinates remain nonspatial in the research catalogue; historical occurrence records with usable coordinates are included. The axes use ordinary geographic longitude/latitude, not Earth's warped game coordinates.

## Reproduction

`render_preview.py` reads the adjacent global research package and Earth's existing `preview/grids.npz` terrain flags. It uses matplotlib 3.11.2 and numpy 2.5.3 from `%TEMP%/Earth-copper-tin-plot-20260929`; dependency versions, input hashes and counts are recorded in [preview-audit.json](preview-audit.json). These plotting dependencies are separate from the mod environment and are not shipped with the mod.

From the Earth workspace, using a Python interpreter that can read that temporary environment:

```powershell
python docs/research/copper-tin-preview-2026-09-29/render_preview.py --porphyry docs/research/copper-tin-global-2026-09-29/normalized/pCu_tracts-display-only.geojson --sedimentary docs/research/copper-tin-global-2026-09-29/normalized/sedCu_tracts-display-only.geojson
```

If the temporary libraries are no longer available, install the recorded versions into an isolated environment, or recreate the temporary target used by the script. `preview/grids.npz` must be regenerated through Earth's existing data bake if missing; it is not a new download requirement introduced by the ore catalogue.

The copper display polygons were simplified by the research normalizer for visualization only. Their source validity flags are used to withhold invalid geometry. Rendering unwraps rings at the antimeridian to avoid bands spanning the world. No smoothing, intensity interpolation or inferred tin polygons are added.

Validation: point counts and input hashes recorded; image inspected for map alignment, legends, readable captions and overlapping text. Source/normalization validation is documented separately in the global research package.
