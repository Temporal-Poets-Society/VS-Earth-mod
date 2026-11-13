# Regional ores rules v2 — first in-game validation (2026-10-15)

First run of rules v2 in a real Vintage Story 1.22.7 dedicated server. The tree under test is the working tree of 2026-10-15 (`earthmap 0.14.0-pre.3`, uncommitted 0.13.2–0.14 work on top of `879b922`). Nothing was committed or installed, no rules were changed, and no player save was opened. All test worlds are new and live in the git-ignored `testruns/ore-ingame-20261015-*` folders (about 7.9 GB, safe to delete).

**Verdict.** The adapter's own arithmetic works in-game:
- attempt rates per class match the design;
- vanilla's ore-map means are confirmed;
- for ores whose host rocks are always present, volume per attempt is about 2.8× vanilla, so the no-evidence floors land in the documented band;
- nothing is placed above ground or exposed on the seabed beyond vanilla's own level;
- the server logs have no errors.

Gameplay balance does **not** yet meet the targets for about half of the resources. The offline suite assumes vanilla's host-rock availability. In-game, Earth's host search and Earth's rock columns change that availability a lot:
- many sedimentary-hosted ores come out 5–200× above vanilla, even where there is no evidence;
- several of the strongest hotspots place nothing, because their column has no allowed host;
- halite and sylvite are far below target;
- the prospecting pick under-reads most ores and over-reads halite.

Details and proposed fixes are below. They are proposals only and need the user's decision.

## Method

| World | Content | Sites |
|---|---|---|
| **E**: Earth, regional ores on | Earth + Cylinder World 0.11.3 (user's CW config, `PolarIceMode: earth`), the user's `earthmap.json` (all regional systems on, default ore tuning), 512000 × 204800 × 384, seed 386217 | 19 named sites (7 hotspots r=3, 8 no-evidence areas, 4 sea), 150 random ice-free land sites (r=1, area-uniform), 20 hotspots × 9 sub-sites (r=1, ±0.135°) |
| **B**: Earth, `RegionalOres=false` | identical, vanilla ore generation on Earth's terrain and rocks | the same 349 sites |
| **V**: pure vanilla | no Earth or Cylinder World, same size, height and seed | 640 sites of 3×3 chunks on a 3000-block grid (585 mostly land) |

A test-only helper mod, kept in [ore-ingame-validation-2026-10-15/oreaudit](ore-ingame-validation-2026-10-15/oreaudit), does the measuring. It is not part of Earth. After the chunks load it records:
- every placed deposit block per family (`ore-*`, `rawclay-*`, `peat-*`, `rock-halite`, `rock-bauxite`, kimberlite) with a depth histogram below `WorldGenTerrainHeightMap`;
- blocks above the terrain, blocks sharing a cell with fluid, and blocks with air or fluid directly above;
- each variant's real `DepositVariant.GetOreMapFactor` for every sampled chunk (Earth's patched factor in E, vanilla's ore map in B and V);
- prospecting-pick readings from the game's own `ItemProspectingPick.GenProbeResults` at five points per site.

Vanilla's sparse ore map is zero in about 45% of chunks, so raw counts are very noisy. Expected baseline volume is therefore estimated as (pooled blocks ÷ pooled actual attempts) × attempts at vanilla's mean factor. That removes ore-map noise but keeps host-rock effects. Scripts, site lists and the compact [summary.json](ore-ingame-validation-2026-10-15/summary.json) are in the same folder.
- **×V**: Earth volume ÷ pure-vanilla land volume. This is what a player notices.
- **×B**: Earth volume ÷ vanilla ores on the same Earth rocks. This isolates the adapter.

Families share block codes, so surface starters are folded into their family (cassiterite, native copper). The shallow band is reported separately.

## What matches the design

| Check | Measured in-game | Design |
|---|---|---|
| Vanilla mean ore-map factor (V, 640 sites) | 0.062–0.083 default maps; 0.025–0.035 sparse maps (anthracite, chromite, ilmenite, pentlandite) | 0.0705 / 0.0303 |
| Earth attempts vs vanilla, no evidence: standard | 0.11–0.14 | 0.128 |
| … progression Sn / Fe / coal / Cu | 0.25 / 0.21–0.24 / 0.22–0.27 / 0.13–0.15 | 0.25 / 0.23 / 0.23 / 0.12 |
| … soil (clay, peat) / quartz | 1.00–1.04 / 1.04 | 1.0 / 1.0 |
| Volume per attempt, ×B, host-robust ores | bismuthinite 3.1, chromite 2.8, ilmenite 3.0, sphalerite 2.9, native copper 2.7, cassiterite 3.5, hematite 2.9, pentlandite 3.9, saltpeter 2.4 | ≈ 2.8 |
| No-evidence volume ×B for those ores | 0.35, 0.37, 0.39, 0.42, 0.39, 0.88, 0.84, 0.46, 0.30 | 0.36, 0.36, 0.36, 0.36, 0.34, 0.73, 0.62, 0.36, 0.32–0.37 |
| Blocks above the terrain surface | 0 in E (V: 2) | none |
| Ore sharing a cell with fluid | 34 blocks in E (B 236; V 184 over 640 sites) | at most vanilla's level |
| Ore exposed on the seabed (4 sea sites) | 152 blocks in E vs 571 in B; mostly saltpeter and quartz, which vanilla also exposes | at most vanilla's level |
| Deep ocean (Pacific 3×3) | 2.5k ore blocks in E vs 36k in B: sea-level ores over a deep seabed are skipped | documented above-ground skip |
| Shallow native copper (0–9 blocks, surface-starter band) | 1.25 blocks/chunk vs V 1.82 (0.69×) | ≥ 0.5× |
| Earth's self-test | bog peat adapter PASS (no GA, so peat-based bog iron inactive); 44 finite prospecting readings PASS at all 8 places | — |
| Server logs, 7 servers | **0 errors or exceptions** | clean |

## Measured vs target, by resource

×V = Earth ÷ pure vanilla land. "No evidence" pools 110–157 Earth sites whose cell has zero evidence for that resource. "Land avg" is the 149 random land sites. Each hotspot pools 9 sub-sites (81 chunks) around the 0.5° cell with the highest heat. Targets come from the regional-ores doc (floor / land average / hotspot volume vs vanilla).

| Resource (families) | No evidence ×V | Land avg ×V | Hotspots ×V (mean heat) | Target | Status |
|---|---|---|---|---|---|
| Tin (cassiterite) | 0.79 | 0.88 | Cornwall 6.8 (0.95) | 0.73 / 0.85 / 3.8 | floor and average OK, hotspot high |
| Copper (native, malachite) | 0.42 | 0.78 | Chuquicamata 3.0 (0.84), Escondida 1.3 (0.95) | 0.34 / 0.82 / 3.8 | OK; Escondida low; **no malachite** at either |
| Iron (limonite, hematite, magnetite) | 1.67 | 1.87 | Pilbara 3.4 (0.82), Kiruna 6.4 (0.95) | 0.62 / 0.79 / 3.7 | **floor 2.7× target**; no limonite at either hotspot |
| Coal (lignite, bituminous, anthracite) | 2.37 | 2.56 | Ruhr 11.6 (0.85) | 0.64 / 0.79 / 3.8 | **3–4× high**; bituminous 29× at Ruhr |
| Gold (in quartz) | 0.38 | 0.94 | Witwatersrand 2.6 (0.95) | 0.32 / 0.53–0.66 / 3.9 | OK; hotspot low-ish |
| Silver in quartz | 0.39 | 0.61 | hotspot group 4.5 | 0.32 / 0.53–0.66 / 3.9 | OK |
| Silver in galena | 35 | 46 | group 226 | 0.30 / 0.51 / 3.8 | **far too high** (inherits galena's host search) |
| Lead (galena) | 1.20 | 2.08 | Broken Hill **0** (0.95) | 0.37 / 0.72 / 4.6 | floor 3× high, **hotspot empty** |
| Zinc (sphalerite) | 0.41 | 0.62 | group 3.6 | 0.36 / 0.63 / 4.5 | OK |
| Nickel (pentlandite) | 0.43 | 0.56 | Sudbury 4.0 | 0.36 / 0.44–0.51 / 4.5 | OK |
| Chromium (chromite) | 0.35 | 0.41 | Bushveld 5.3 | 0.36 / 0.44–0.51 / 4.5 | OK |
| Mercury (cinnabar) | 0.47 | 0.51 | Almadén 3.6 | 0.37 / 0.38–0.50 / 4.5 | OK |
| Bismuth, titanium (ilmenite), saltpeter | 0.34, 0.42, 0.35 | 0.37, 0.37, 0.35 | — | 0.32–0.37 band | OK |
| Emerald | 0.48 | 0.53 | Colombia 7.4 | 0.28 / 0.28 / 3.5 | high |
| Olivine / peridot in olivine | 0.23 / 0.50 | 0.29 / 0.58 | — | 0.32–0.37 | olivine slightly low |
| Sulfur | 1.07 | 1.59 | Sicily 27 | 0.37 / 0.41–0.60 / 4.5 | **3× and 6× high** |
| Boron (borax, kernite) | 1.35 (kernite 33) | 1.46 | Boron, CA **0** | 0.37 / … / 4.5 | high, **hotspot empty** |
| Lapis | 11 | 14 | Sar-e-Sang **0** | 0.32–0.37 / … / 4.0–4.6 | **far too high, hotspot empty** |
| Phosphorite | 176 | 212 | Khouribga ≈ 4900 | 0.36–0.37 / … / 4.5 | **vanilla places almost none**; Earth ≈ 3 blocks/chunk |
| Graphite, rhodochrosite, fluorite, alum | 7.9, 8.1, 26, 29 | 11.6, 11.3, 40, 17 | — | 0.36–0.37 band | **far too high** |
| Halite | 0.06 | 0.08 | Zechstein 0.73, Gulf 0.78 | 0.35 / 0.53 / 4.4 | **far too low** |
| Sylvite (in halite) | 0.06 | 0.12 | Saskatchewan 1.3 | 0.29–0.37 / … / 3.6–4.6 | **far too low** |
| Diamond | 0 | 0 | Kimberley **0** | 0.42 / 0.46 / 5.3 | **none placed**; vanilla also placed none in 585 sites |
| Quartz | 1.24 | 1.28 | group 1.54 | 1.0 / 1.06 / ≤ 1.55 | OK |
| Red clay / blue clay | 2.1 / **0** | 2.5 / **0** | — | ≈ 1 | blue clay absent (also in B) |
| Peat | 1.09 | 2.52 | — | 1.0 / 1.01 / 1.75 | floor OK; average high from wetter Earth terrain (B shows the same) |
| Bauxite | 0.00 | 0.01 | — | vanilla strata replaced by design | as designed |

Per-site texture matters. The clustered texture (0.3 floor up to about 5×, 8-chunk cells) moved a single 7×7-chunk Witwatersrand site to 0.94× gold attempts, against 2.86× without texture. Single-site hotspot numbers are therefore unreliable, and the hotspot table uses 9 spread sub-sites. Earth's own attempt factor still ranges 0.54–2.37 across hotspots, mostly because of texture and because the bilinear heat falls off within 0.1–0.2° of the peak cell.

## Problems found and proposed fixes (not applied)

1. **Host search inflates host-limited ores (largest effect).** Vanilla's limonite, phosphorite, lapis, malachite, coal, sulfur, galena, borax/kernite, fluorite, alum, graphite and rhodochrosite usually find no allowed host at the drawn depth. Most of those vanilla attempts place nothing. Earth moves every attempt to the nearest host band, across the whole column, so their volume per attempt is 8–150× vanilla instead of ≈ 2.8×:

   | Ore | Volume per attempt ×B |
   |---|---|
   | lapis | 154 |
   | phosphorite | 96 |
   | limonite | 85 |
   | rhodochrosite | 55 |
   | bituminous coal | 33 |
   | malachite | 23 |
   | borax | 17 |
   | sulfur | 15 |
   | galena | 8 |

   The offline targets assume vanilla's host availability, so the suite cannot see this.
   - *Proposal:* accept a moved deposit only with the probability that vanilla's own column would have had a host in the window. That fraction could be measured once per variant from vanilla's simulated rock column (`ProPickWorkSpace.GetRockColumn`), or calibrated per variant from this harness (×B volume per attempt ÷ 2.8). Restricting the search to the variant's depth window (dropping the whole-column pass) is a cheaper partial fix.
   - Silver in galena inherits galena's inflation. Its child chance would need the same correction.
2. **Hotspots without a host in Earth's column place nothing.** This affects:
   - Kimberley diamond (needs kimberlite or suevite; vanilla kimberlite exists only as the bottom 41 blocks where Earth keeps vanilla strata);
   - Broken Hill lead and Boron borax/kernite (sedimentary hosts only);
   - Sar-e-Sang lapis (limestone or marble);
   - limonite at Pilbara and Kiruna;
   - malachite at both Chilean copper hotspots.

   Where Earth keeps vanilla strata (`RegionalRock` = 0, e.g. Pilbara, Chuquicamata and Ruhr are "preserve vanilla bedrock"), host search does nothing by design. In these places the regional signal is inverted: no-evidence land with the right rocks gets more ore than the world's richest districts.
   - *Proposal (geology owns rocks):* in cells with strong evidence, Earth geology synthesizes the host lithology the provider requires, such as a kimberlite pipe at diamond evidence or carbonate or sedimentary lenses at lapis, Pb-Zn and borate evidence. The alternative of placing ore in the column's own rock is not possible where the provider has no such block (for example `ore-*-galena-granite`).
3. **Halite and sylvite are far below target.** No-evidence floor 0.06 vs 0.35; salt hotspots 0.7–0.8 vs 4.4; potash hotspot 1.3 vs 3.6–4.6. At Zechstein, Earth's halite lies only in the 40–79-block depth band, while vanilla's dome on the same rocks runs from 40 to past 160 blocks. That suggests Earth truncates vanilla's 150-block dome thickness (host-band start, burial or thickness handling for very thick variants). Domes are rare and lumpy (one vanilla dome = 34k blocks), so this needs a targeted look rather than more sampling.
   - *Proposal:* review disc placement for `thickness.avg ≥ 100` variants and add an offline test for a full-height dome.
4. **Prospecting readings are biased.**
   - Pooled over all sites, readings are 1.9× below placed ore. They are 2.3–3.5× low for host-robust ores (cassiterite 2.9, pentlandite 2.6, chromite 2.5, galena 2.3) and 7–8× low for host-limited ones (limonite 7.0, magnetite 7.6, malachite 8.3, lapis 7.3).
   - Halite is over-read 4× on average, with halite reported at 69 of 99 sites where none was placed (Zechstein: 3.7 ppt read vs 0.55 placed). Hematite reads > 0.05 ppt at 62 sites with no hematite placed.
   - At Cornwall the pick shows native copper (1.55 ppt) above tin (0.19 ppt), although placed tin is 1.1 ppt.
   - Log-space correlation between reading and placed ore per code is only 0.1–0.7.
   - *Hypothesis (not verified):* the disc shape factor in `ExpectedPerChunk` (π·r²·0.55·thickness, `src/EarthOreGenerator.cs:94`) is too small, and host search and halite truncation are not modelled.
   - *Proposal:* calibrate expected volume per variant against this harness's measured blocks per attempt. Model the host-search fraction from fix 1. Use the same thickness rule as placement.
5. **Blue clay is missing.** There are 0 blue-clay blocks in 349 Earth sites, both with and without Earth ores; vanilla gives 0.9 blocks per chunk. Red clay is 2.1× vanilla at no-evidence sites. Because B shows the same, the cause is upstream of the ore adapter: geology's `ClayFactor`/habitat gate or terrain. This should be checked with the geology owner.
6. **Log noise.** Only with Earth ores, the server logs 13 `Deposit <code> has no handbook page code` warnings: clay, red clay, peat, quartz, olivine, saltpeter, surface copper, surface cassiterite, earth-bauxite, uranium, corundum. Vanilla's prospecting workspace warns for every `WithOreMap` variant without a page code, and Earth turns `WithOreMap` on for formerly unmapped variants and its authored ones. The warning is cosmetic: those codes stay unlisted because Earth clears their map layer.
   - *Proposal:* give adapted variants a `HandbookPageCode` (for example `rawclay-blue-none`, `ore-quartz-granite`).
7. **Moderate overshoots:** Cornwall tin 6.8×, Kiruna iron 6.4×, Colombia emerald 7.4× (all above 3–5×), emerald floor 0.48 (target 0.28). These are partly fix 1 and partly texture. Re-measure after fix 1.

Other log entries need no action: "server overloaded" ticks during bulk chunk generation, entity-initialisation warnings that also appear in pure vanilla, and one shutdown "thread not suspended" while three servers shared the disk.

## Addendum: copper and tin around the Baltic start

121 sites of 3×3 chunks on a 0.5° × 1° grid (54.5–59.5°N, 20–30°E; 88 of them on land), run in worlds E and B. The pure-vanilla figures come from the 585 land sites above.

| Share of 96 × 96-block land samples with … | Earth ores | Vanilla ores on Earth rocks | Pure vanilla |
|---|---|---|---|
| surface copper nuggets | **17%** | 35% | 47% |
| copper ore within 9 blocks of the surface | 19% | 36% | 52% |
| copper ore within 39 blocks | 32% | 49% | 62% |
| tin within 79 blocks | 44% | 31% | 47% |
| tin at any depth | **83%** | 43% | 52% |

Only 3 of the 88 land sites have any copper evidence. Two effects compound:
- Earth's rocks lose about a quarter of the surface copper: limestone and dolostone (shown as limestone) are not surface-copper hosts.
- The surface-starter class (0.6× vanilla attempts) halves what is left.

The nearest sample with nuggets is a median 1,100 blocks away and at most about 2,500 blocks (the grid is 1,132 × 569 blocks, so these are upper bounds). Tin is easier to find than in vanilla, but it sits 40–80 blocks deep.

## Heat maps

`tools/render_ore_heatmaps.py` was re-run. The 44 maps in [ore-heatmaps-v1/maps](ore-heatmaps-v1/maps) and the viewer [ore-heatmaps-v1/index.html](ore-heatmaps-v1/index.html) came out byte-identical to the committed files, so the published maps match the shipped `ore-heatmaps-v1.json`.

## Reproducing

1. Build the helper: `dotnet build ore-ingame-validation-2026-10-15/oreaudit/OreAudit.csproj`. `mkrun.py` expects its output at `oreaudit/bin/mod` next to the script.
2. Create each world: `python mkrun.py <new run dir> earth|vanilla <port> <sites.json> ["lat,lon;…" self-test places]`. For world B, set `RegionalOres=false` in that run's `ModConfig/earthmap.json`.
3. Start each world: `VintagestoryServer.exe --dataPath <run dir>`. The helper stops the server when done (about 2–8 s per site).
4. Analyse: `python analyze3.py <E results,…> <B results,…> <V results> out.json`, then `python show.py out.json`.
