# Regional geology implementation

First written 29 September 2026 (rules v1, Earth 0.12); revised 2 October 2026 for **geology rules v2** and the review fixes that apply to every world.

Status: coarse regional geology with offline core tests on the real shipped data and the real Earth terrain model. In-game integration runs are done separately by the release lead. This is not a geological reconstruction, and nothing below is a calibrated ore or resource survey.

## Rules versions and existing saves

A world generates with the geology rules version saved in it (`EarthParams.GeologyRulesVersion`); the code dispatches on it instead of on the newest version.

| | Rules v1 (0.12 saves) | Rules v2 (new worlds) |
|---|---|---|
| Bed boundaries | depth below each column's own surface, thickness ∝ column height (frozen) | absolute heights below a smoothed regional reference surface |
| Category lookup | 0.5° cell containing the warped point (frozen) | nearest of one jittered seed per cell, plus a cell-scale warp octave |
| Coast / no-data cells | vanilla strata | filled from the nearest mapped cell within 2 cells |
| GLiM pyroclastics (`py`) | vanilla strata | andesite/tuff/diorite sequence |
| Never replaced | `rock-bauxite` | `rock-bauxite`, `rock-kimberlite`, `rock-suevite` |
| Chalk, chert | not placed | chalk envelopes; chert/flint bands in chalk and carbonate sequences |
| Coarse-sediment gravel | continuous 2-block sheet (frozen) | discontinuous lenses (25–50 % of the ground, 1 block, 2 in lens cores), merged into clay/peat patches above them |
| Clay / peat / salt eligibility | **new rules (below), same for both versions** | same |

v1 is kept **bit-identical** for rock strata, contact warp, profile selection, sample lookup and the gravel/sand cover decisions. `tests/RegionalGeologyTests/golden-v1.json` was generated from the unmodified 0.12.2 code (commit db80766, generator kept as `GoldenBaselineGenerator.cs`) over 48,000 columns in three world layouts (the 512000×204800 h384 cylinder with two seeds, and a 256k h256 world): every hash still matches. The geology files `geology-grids-v1.json` and `geology-rules-v1.json` are byte-identical to 0.12, so a 0.12 save's stored fingerprint is recognised and **not rewritten** (the save stays loadable by 0.12 as well).

Changes that do reach v1 worlds, in newly generated chunks only, and do not produce rock seams:

- clay, peat and halite eligibility (resource frequency only);
- `RegionalRock` returns 0 (instead of a granite default) where no regional profile applies, and measures depth below glacier ice like the strata pass. This only affects regional-ores bauxite replacement and ore host lookups;
- performance and robustness changes with identical output.

A world whose saved rules version is unknown to the running Earth version (e.g. after a downgrade) gets an error in the log and vanilla strata for that session. It is never upgraded silently; `UpgradeExistingWorldGeologyRules` (handled by the core system) is the explicit opt-in, for new chunks only.

## Save safety

Nothing in the geology system throws during startup. Each of these logs a clear message and either keeps the world's saved behaviour or turns regional geology off for the session (vanilla strata in new chunks):

- a Harmony target missing or renamed in a new game version (each hook is null-checked and patched separately; only the strata hook is essential);
- unknown saved rules version, invalid saved contact width, a competing geology mod (Advanced Geology, Rock Strata Variety);
- missing or corrupt shipped data, or a vanilla block named by the rules that does not exist;
- generation exceptions inside a hook (logged, at most three times; the chunk keeps vanilla blocks there).

**Fingerprint.** It is informational only. It hashes generation-relevant content: grid bytes and the *parsed* rules (materials, profiles, classes, province polygons/filters, salt kernels, preserved rocks, v2 generation settings), not notes, URLs, labels, whitespace or mod versions. A mismatch logs a warning and stores the new value.

**Materials.** The saved signature also records which block each canonical material used (`dolostone=game:rock-limestone`, …). A saved world keeps those blocks whenever they still exist, so adding or updating Geology Additions never changes an existing world's rocks. If a saved provider block has disappeared (provider removed), the vanilla proxy is used with a warning; the contact with older chunks will then be visible, and the provider's already-placed blocks still need their own migration. New worlds use a provider asset when the provider is enabled and the block exists, otherwise the vanilla proxy with a warning.

**Geology Additions child grades.** The adapter that clamps GA child-deposit grade lists (it prevents a native crash) runs in Earth worlds with regional geology whenever GA is installed (not in vanilla-rock Earth worlds or other worlds, whose GA generators are left as they are; corrected 18 November 2026, audit R14), for any GA version (1.4.8 was reviewed), and again after `/wgen regen` reloads deposits. It never throws.

## What the datasets provide

Earth reads two real global categorical datasets: **GLiM surface lithology** and **GUM unconsolidated surface sediments**, the authors' openly published 0.5-degree grids. The runtime dataset is offline and immutable.

- Hartmann & Moosdorf (2012), **GLiM v1.0, 0.5-degree grid**: [PANGAEA](https://doi.org/10.1594/PANGAEA.788537), [paper](https://doi.org/10.1029/2012GC004370). CC BY 3.0.
- Börker, Hartmann, Amann & Romero-Mujalli (2018), **GUM v1.0, 0.5-degree grid**: [PANGAEA](https://doi.org/10.1594/PANGAEA.884822), [paper and class definitions](https://doi.org/10.1002/2017GC007273). CC BY 3.0.
- **Baltic pilot**: [LVĢMC 2020–2023 resource review](https://videscentrs.lvgmc.lv/files/Zemes_dziles/Kopsavilkuma%20p%C4%81rskats%202023%202.pdf), see also [Latvia report](geology-latvia-pilot.md).

GLiM has 720×360 cells; GUM's grid (720×347, north edge 83.514743°N) is sampled at GLiM cell centres by containing cell, without interpolation. `tools/bake_geology.py --check` verifies the shipped grid against the sources; the script refuses to overwrite the v1 grid with different content. A 0.5° cell is about 55 km (≈ 570 blocks in the 512000×204800 world): small islands, narrow outcrops and real contacts are necessarily incomplete.

Of 259,200 cells, 46,707 have a regional bedrock profile in v1 and 62,326 in v2 (18,351 v2 cells borrow a neighbour's category; most are near-shore sea floor). Unconsolidated cover (`su`) still means *unknown bedrock* and keeps vanilla strata in both versions, unless a reviewed province covers it.

## Rock strata

The strata pass runs after vanilla `GenRockStrataNew` and replaces existing `rock-*` blocks below the terrain top or below Earth's glacier ice. Columns without a regional profile keep vanilla strata.

**v1** (frozen): bed i ends at depth Σ max(2, round(fraction × max(24, top) × (1 + fold))) below the column's own top, with a ±12 % fold from latitude/longitude. Every column therefore shows its profile's first bed at the surface, and beds thicken under mountains. The code now computes these boundaries once per column (identical results; checked against the per-block original on 797,433 synthetic blocks and against the golden file).

**v2**: bed boundaries hang at fixed thicknesses below a **reference surface**: the terrain's bedrock elevation (below ice sheets) averaged over ~0.6 cell (19-point stencil; sea floor clamped at −150 m), converted to block height, plus a gentle undulation of up to ±0.3 × the thickness scale over ~1.6 cells. The thickness scale is 0.6 × sea level, varied ±15 %. When v2 was written the sea level followed world height (99 blocks at height 384); since decision 0031 it is 110 at every height, so the scale is 66 blocks at 256, 384 and 1024 alike while the land's metres per block change, and bed exposure differs with world height (corrected 18 November 2026; [audit](research/rocks-ground-ores-audit-2026-11-18.md) R1, EA-0173). Values live on a 64-block lattice and are interpolated bilinearly, so chunk borders match. Effects measured on the h384 user layout:

- valleys, canyons, cliffs and coasts cut into lower beds: across the Grand Canyon transect the surface shows sandstone/shale/conglomerate (siliciclastic) and dolostone/shale/limestone/chert (carbonate); v1 shows one bed per sequence;
- 14.4 % of regional land columns expose a bed below the first (0.1 % basement); v1: 0 %;
- first-bed thickness is the same under mountains and lowlands (19.5 vs 18.8 blocks).

New v2 profiles and envelopes (rules file `geology-rules-v2.json`):

- **pyroclastic** for GLiM `py` (Andes, Japan, Kamchatka, Taupō, Sierra Madre Occidental): andesite, tuff, andesite, tuff, basalt, diorite over granite. Vanilla tuff has no sand/gravel variant, so it is never the first bed; valleys and slopes that cut below the first bed can still bring it to the surface, where vanilla's sand and gravel layers then skip it (corrected 18 November 2026, audit R6).
- **chalk-cretaceous** (chalk with flint/chert bands over Gault-like claystone, greensand, Jurassic limestone and shale): author-drawn envelopes for southern England, the Paris Basin ring, Denmark/Scania/Rügen, the Austin Chalk band and the Selma/Black Belt chalk. They override only sedimentary or unconsolidated GLiM cells. Tertiary basin fills (London, Paris) are simplified to chalk at the top. These polygons are not survey contacts.
- chert bands in the carbonate sequence; tuff in the felsic-volcanic sequence.
- kimberlite and suevite (the vanilla diamond hosts) are never replaced, so vanilla kimberlite pipes and their diamonds survive in regional bedrock. In v1 worlds they remain replaced (changing it would break v1 bit-identity).

**Contacts (v2).** v1 displaced sample points by a 96-block warp (median 37, p90 64, max ~116 blocks against 569-block cells), which left the 0.5° grid visible. v2 adds a cell-scale octave (0.9 cell wavelength, 0.35 cell amplitude; median 88, p90 153, max 285 blocks) and assigns each point to the nearest of one jittered seed per cell (jitter 0.45 cell, exact 5×5 search). Every category still comes from a real source cell. Measured along 120 transects, contacts now lie on average 0.212 cell from the nearest grid line (v1: 0.125; 0 would be a pure grid, 0.25 no trace of it). The warp stays continuous: largest change per block step 6.3 (v1 6.5).

## Surface cover

Unambiguous GUM textures only: dune sand (`Ed`) replaces the top two blocks in dry climates (< 600 mm) or the two below a soil cap; coarse fan, glaciofluvial and till classes (`Af`, `Gf`, `Gt`) get gravel below a two-block soil cap. In v1 this is a continuous two-block sheet (frozen). In v2 it forms lenses on 50 % / 45 % / 25 % of the ground respectively, one block thick and two in lens cores (~40 % of the lens area). After the deposit pass, any gravel directly below a clay or peat patch becomes part of that patch.

## Clay, peat and salt (all worlds)

Clay and peat are filtered at the deposit centre by a deterministic **frequency multiplier**: an attempt is accepted when a position hash is below the multiplier, so all chunks that share a deposit agree. Vanilla's own size, substrate, climate (clay) and roughness checks still apply.

Relief is the range of the water-or-ground surface over the centre and four columns 12 blocks away, converted to **metres** and divided by the footprint span (km): a river or lake bed is no longer counted as relief, and the rule does not tighten with world height.

**Clay** (`ClayFactor`): never denied on rock type alone (weathered basalt and crystalline rock form clay-rich soils). ×1.3 lacustrine/marine/glaciolacustrine clays, ×1.2 other fine sediments, till, laterite/residual weathering and alluvium, ×1.1 mafic rock, ×0.65 dune sand/glaciofluvial gravel/fans; ×1.2 near fresh water; reduced by up to 60 % on slopes from 40 to 320 m/km; ×0.85 for centres under fresh water. Zero only on the sea floor, salt lakes, glaciers and outside Earth. On 12,000 vanilla-eligible land columns of the 512000×204800 layout: allowed on 100 % (0.12.2: 64.5 / 57.1 / 33.1 % at heights 256 / 384 / 1024), mean multiplier 0.97 at all three heights; every column near fresh water allowed (0.12.2: 25–63 of 408).

**Peat** (`PeatFactor`): physical exclusions (sea, salt lake, ice, centre under water). Temperature ramps from −12.5 °C to −5 °C mean (mapped GUM peat: −17 to −9 °C, for permafrost peat plateaus and polygon mires); no upper limit, so tropical peat swamps are allowed (Earth calls the native generator directly, bypassing vanilla's 18 °C ceiling). Wetness is the climate's precipitation/potential-evaporation index (Vintage Story rainfall ÷ 255: deserts ≈ 0.25, humid temperate ≈ 0.5, rainforest ≈ 0.78), ramping 0.36–0.50; mapped peat bypasses it (floor 0.75). Slopes above 12 m/km reduce it, to zero at 60 m/km, or up to 200 m/km in hyper-oceanic climates (blanket bog) and 160 m/km for mapped peat. Substrate: mapped peat 1, lacustrine/marine clays 0.9, glacial/fine/alluvial 0.75, other 0.5, at least 0.85 near fresh water, ×1.3 below 6 °C. Results: mapped peat eligible on 415 of 415 dry-land cell centres (0.12.2: 139); eligible at every sampled land point in Latvia, Lithuania, Finland, the West Siberian Lowland, Hudson Bay Lowlands, the Irish midlands, the Flow Country, Central Kalimantan and Riau (mean multipliers 0.5–0.9).

**Halite**: missing evidence is unknown, never absence. Native attempts keep their own host/chance/depth checks outside ice; exposed surface beds are not placed on the sea floor or under water. **Positive evidence is favoured**: a surface salt-bed attempt in a GUM salt cell or within a reviewed occurrence kernel (Wieliczka, Bochnia, Cardona; 5 km Earth-authored kernels) skips vanilla's warm-desert climate gate, so cold salt flats such as the Altiplano can form beds. The kernel lookup now runs only for surface-bed attempts outside mapped salt. Native halite map values inside Earth are preserved; gutter/beyond-pole values are zeroed.

## Generation hooks (Vintage Story 1.22)

1. `GenRockStrataNew.GenChunkColumn` postfix (Terrain pass): strata. The rock top below glacier ice is found from the ice blocks the Earth terrain pass wrote (equal to the model's `IceBottom − 1` by construction), so no terrain-model call is needed.
2. `GenBlockLayers.OnChunkColumnGeneration` postfix (Terrain pass): surface cover, then `TopRockIdMap` repair. The terrain model is consulted only for dune/coarse candidates without water above them (to read the beach flag).
3. `GenDeposits.GenDeposit` prefix (TerrainFeatures pass): clay, redclay, peat and halite filters.
4. `GenDeposits.GenChunkColumn` postfix (v2 only): gravel below clay/peat patches.
5. `GenDeposits.reloadWorldGen` postfix: GA child-grade clamp after a reload.

Column samples are computed once per chunk and shared by the strata and cover passes through a per-thread cache; bed boundaries are computed once per column. Measured per 32×32 chunk with 200 rock blocks per column: v1 strata 4.84 ms per-block → 0.32 ms per-column; v2 strata 0.68 ms; one terrain-model column query costs ~1.2 ms per chunk (0.12.2 made two to three per column in these passes). All shared data is immutable after start-up; per-call state is local or `[ThreadStatic]`; 20,000 v2 lookups give identical results sequentially and across threads in shuffled order.

`EarthGeology.RegionalRock(x, z, depth, top)` returns the block the strata pass would place at `depth` below the heightmap `top`, or 0 where Earth leaves vanilla strata (no profile, above the rock under glacier ice, outside Earth). Repeated calls for one column hit a per-thread cache. `TryGetColumnRocks(x, z, top, Span<int> idsByDepth)` fills a whole column at once.

`/earth geology` (`Describe`) reports the rules version, categories, whether the category was borrowed from a neighbouring cell, the top regional rock, the v2 reference height and thickness, peat/clay multipliers with slope and wetness, and salt evidence.

## Tests

`tests/RegionalGeologyTests` (`dotnet run -c Release -- <repo> [quick]`), 172 checks, about 200 s with the terrain model:

- data and save safety: v1 files byte-identical to 0.12; the fingerprint ignores notes/URLs/formatting and changes with a bed thickness; corrupt, truncated, unsupported-version and degenerate-polygon data are rejected as `ArgumentException`;
- every material and preserved rock exists in the installed game's `rockwithdeposit.json`; chalk/chert have sand and gravel variants, tuff does not;
- v1 golden output (above); v1 per-column strata equal the per-block original;
- v2 categories: Bergen and Hawaiian coasts get bedrock, five `py` regions get the pyroclastic profile, six chalk sites get chalk with flint, Brittany does not;
- clay/peat rates on real terrain at heights 256, 384 and 1024; mapped-peat and named peatland coverage;
- v2 exposure (Grand Canyon, Middle Rhine, Valais, Dover), anti-grid metric, warp continuity, threaded determinism, gravel lens coverage and timings.

## Limits

- Bed thicknesses, order and basement are synthesized hypotheses at game scale, not stratigraphy. Chalk and the Baltic pilot are broad author-drawn envelopes.
- The reference surface is a smoothed terrain; it is not a structural map. Real dips, faults, folds and unconformities are not modelled.
- GLiM categories do not identify formations or ages; `su` cover still hides bedrock (vanilla strata there).
- Clay/peat multipliers are geomorphological heuristics; colour, mineralogy and refractoriness of clay are not modelled. Real peatland extent is not reproduced cell by cell.
- No salt district probabilities; positive salt records remain sparse.
- In v1 worlds kimberlite and suevite stay replaced in regional bedrock, so those regions keep fewer diamonds; chalk and chert are not added there.
