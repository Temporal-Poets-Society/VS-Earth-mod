# Audit: Earth regional rocks (regional geology strata)

Repo `C:\VS mods\Earth`, `main` at 1ac0f2d (v2.1.4). Read-only audit, 18 November 2026. No repo edits, builds, game or bench runs.

Method: read the code, rules and docs; compared with vanilla 1.22.7 (decompiled `GenRockStrataNew`, `GenBlockLayers`,
`BlockLayer`, `BeachLayerProperties`, `ModLoader`/`ModContainer`, `%APPDATA%\Vintagestory\assets\survival\worldgen`);
measured with Python scripts in `scripts\rocks\` over the shipped grid (`geology-grids-v1.json`), rules v2, and
Earth's own 2′ terrain (`ModData\earth\data\terrain.bin`, decoded to 0.1°):

| Script | What it measures |
|---|---|
| `coverage.py` | GLiM class shares, v2 coastal fill, profile shares, province cells, GUM under `su` |
| `landfill.py` | real land (Earth's terrain flags) in mapped / borrowed / unmapped GLiM cells |
| `points.py`, `places.txt`, `region_share.py`, `boxes.txt` | GLiM class at ~150 named geological localities and 27 regions |
| `v2lookup.py` | port of `RegionalGeologyCatalog.At` (v2: fill + jittered-seed Voronoi + provinces, no warp) over regional boxes |
| `hostcov.py` | vanilla ore host availability per profile and depth (vanilla deposits + Earth's `ground-rocks-deposits.json`, GA absent) |
| `scale.py`, `scale2.py` | approximation of `GeologyReference` + `ColumnV2` on real terrain at heights 256/384/1024 (current and legacy layouts) |
| `contacts.py` | port of `WarpV2` + Voronoi; straightness of contacts at 1:200 / 1:100 / 1:10 / 1:5 |

Caveats: the Python ports were written from the C# and checked for plausibility, not bit-compared with the C#.
`scale*.py` works on 0.1° (≈11 km) terrain, so it underestimates relief and therefore bed exposures (its legacy-layout
figure, 11.7 %, sits below the doc's 14.4 % measured on fine terrain); use its numbers for comparisons between world
heights, not as absolutes.

Mark legend: **V** = verified (code read and/or measured), **S** = suspected (mechanism plausible, not measured in game).

---

## 1. Pipeline map

All paths relative to `C:\VS mods\Earth`.

| # | Pass / function | File:line | Inputs | Outputs |
|---|---|---|---|---|
| 0 | Hook registration (strata, soil, deposits, deposit columns, GA reload) | `src/EarthGeology.cs:67-83` | `PatchVanilla.GeologyTable()` | Harmony postfixes/prefixes; `MapRegionGeneration` salt mask |
| 1 | `InitWorld` / `TryInit`: rules-version dispatch, competitor check (AG, RSV), contact width, data load, materials, block tables, ground columns, ore layers, reference surface | `src/EarthGeology.cs:85-197` | `EarthParams` (GeologyRulesVersion, GeologyTransitionBlocks, NoiseSeed, GroundColumns...), assets | `Active`, `Catalog`, `materialBlock`, `reference`, `ground`, `layers` |
| 1a | `ResolveMaterials`: saved block per material, else provider (GA), else Earth's own copy (gneiss/schist/dolostone...), else vanilla proxy; signature/fingerprint | `src/EarthGeology.cs:333-398` | rules materials, `GeologyProviderSignature` | `materialBlock[]`, saved signature (warn-and-overwrite on mismatch, :390-396) |
| 1b | `BuildBlockTables`: replace = any `rock-*` not in Preserve; sand/gravel of each rock | `src/EarthGeology.cs:420-445` | all blocks, `Preserve` | `replaceRock`, `isRock`, `sandOf`, `gravelOf`... |
| 2 | Catalogue load: validation, v2 coastal fill, fingerprint | `src/Core/RegionalGeologyCatalog.cs:176-229`, `Fill` :234-258, `ComputeFingerprint` :260-294 | grid JSON, rules JSON | immutable catalogue |
| 3 | Category lookup: warp (v1 `Warp` / v2 `WarpV2`) then `At` → Voronoi cell (v2), province polygons (first match, lithology filter), class → profile | `src/Core/RegionalGeologyWorld.cs:15-24`, `src/Core/GeologyTransitions.cs:65-90`, `RegionalGeologyCatalog.cs:296-340` | world x,z, seed, width (96 blocks), cellBlocks (0.5° in blocks) | `Sample` (lithology, sediment, profile, province, Filled) |
| 4 | Per-chunk sample cache (1024 columns, per thread) | `src/EarthGeology.cs:528-554` | chunk x,z | `Samples`, `Lat`, `Lon` |
| 5 | Reference surface (v2): 19-point stencil of `Vertical.Y(max(elev − ice, −150 m))` at radius 0.6 cell, + undulation ±0.3·T over 1.6 cells; T = max(16, 0.6·SeaLevel)·(1 ± 0.15) on a 64-block lattice, bilinear | `src/Core/GeologyReference.cs:19-82` | `TerrainModel.Data.SampleTerrain` (2′ grid), `Vertical`, seed | datum (block y), thickness T |
| 6 | Bed boundaries: v1 depth below own top (`ColumnV1`), v2 absolute y below datum (`ColumnV2`); bed search `BedV1/BedV2` | `RegionalGeologyCatalog.cs:363-405` | profile, datum, T | boundary arrays, bed index |
| 7 | Strata pass (postfix on `GenRockStrataNew.GenChunkColumn`, Terrain pass, after vanilla strata 0.1, before caves 0.3 and block layers 0.4) | `src/EarthGeology.cs:567-626`, `StrataBlock` :642-656, `StrataRock` :659-664, `StrataLayer` (bulk) :667-685, `StrataTop` (below glacier ice) :692-701 | chunk blocks, samples, boundaries | rock-* replaced by regional beds; `TopRockIdMap` |
| 8 | Soil pass postfix: `ApplyCover` (GUM dune sand, coarse gravel lenses), `ApplyGround` (ground columns), `RestoreTopRock` | `src/EarthGeology.cs:705-781`, lenses `GeologyTransitions.cs:93-114` | samples, terrain column, climate | sand/gravel cover; ground columns; top rock map |
| 9 | Deposit prefix: clay/redclay/peat frequency filters, halite (native / favoured / deny), ore-layer gates | `src/EarthGeology.cs:1068-1141`; `ClayFactor` `RegionalGeologyCatalog.cs:430-446`, `PeatFactor` :454-475, `SaltDecision` :485-490; site `RegionalGeologyWorld.cs:31-51` | deposit centre, terrain columns (5-point footprint ±12 blocks), climate | accept / reject / run generator directly |
| 10 | Deposit-column postfix: gravel under clay/peat becomes the patch (v2); ore layer pass; catalogue bodies | `src/EarthGeology.cs:876-920` | chunk blocks | cleaned patches |
| 11 | Salt map mask (outside Earth only) | `src/EarthGeology.cs:447-462` | halite ore map | zeroed gutter/beyond-pole values |
| 12 | Query `RegionalRock(x,z,depth,top)` / `RockColumnAt` (ground-column hand-off, per-thread cache) | `src/EarthGeology.cs:998-1064` | position, depth, heightmap top | block id or 0 (no profile / above ice / outside) — read by ore host search (`EarthOreGenerator.cs:244,375`), ore layers (`EarthOreLayers.cs:617,654`), prospecting (`OreProspecting.cs:94`), bauxite strata (`OreBauxiteStrata.cs:42-46`), water table (`EarthWaterTable.cs:352`) |
| 13 | `/earth geology` `Describe` | `src/EarthGeology.cs:1143-1170` | position | text |
| 14 | Rock floor (Vegetation pass): rock just above the floor copied down | `src/RockFloorPass.cs:81-151` | chunk, `PlainRock` (any `game:rock-*`) | plain rock floor |
| 15 | Block-layer adjustments: arid basin rock fill disabled (40 → 0), soil fertility, tall grass | `src/BlockLayerAdjustments.cs:29-47` (+ soil/grass) | vanilla IL | patched vanilla pass |
| 16 | Crafted-stone textures of Earth's rocks (client) | `src/EarthStoneTextures.cs:27-130`, `src/Core/StoneRecolour.cs:47-96` | vanilla templates, Earth's rock textures | recoloured PNG data |
| 17 | Bake (offline) | `tools/bake_geology.py` | GLiM zip, GUM asc | `geology-grids-v1.json` (refuses to change it) |

---

## 2. Findings, ranked by severity

Severity weighs player impact in worlds that use regional rock (rock choice `groundcolumns` or `regional`; the default
since 2.0.1 is `vanilla`, where none of the strata findings apply) and the fact that rules are frozen per save
(decision 0002): whatever ships as v2 is what the public 1:10 no-wipe server keeps if it is created with regional rock.

### HIGH

**R1. The stratigraphic thickness no longer follows world height; bed exposure and basement outcrops depend on the
world height (V).**
- `GeologyReference.cs:24`: `thickness = Math.Max(16, thicknessPerSeaLevel * model.SeaLevel)`; rules
  `geology-rules-v2.json:32` (0.6). Since decision 0031 (0.17, 27 Oct, after rules v2 of 2 Oct) the default sea level is
  110 at every height (`VerticalTerrainOptions.cs:26`), so T = 66 blocks at 256, 384 and 1024. The doc
  (`docs/regional-geology-implementation.md:65`) still says "99 blocks at height 384; it follows world height".
- The terrain's land curve does follow world height (`VerticalScale.cs:32`, top = height − 2): metres per block at sea
  level are 15.9 / 8.4 / 2.5 at 256 / 384 / 1024. The fixed 66-block sequence is ≈1,050 / 555 / 165 m on the land
  curve's sea-level scale.
- Measured (scale2.py, regional land, 0.1° terrain, with undulation): share of columns whose surface is below bed 0 /
  with the basement at the surface:

  | Layout | T | lower bed at surface | basement at surface |
  |---|---|---|---|
  | current default h256 (sea 110, log1000) | 66 | 11.6 % | 0.0 % |
  | current default h384 | 66 | 13.6 % | 0.2 % |
  | current default h1024 | 66 | 22.7 % | 4.5 % |
  | legacy h384 (what the tests use) | 99 | 11.7 % | 0.0 % |
  | legacy h1024 (what the tests use) | 265 | 11.7 % | 0.0 % |

  Under the legacy layout the design was height-invariant; under today's layout it is not. At h1024 (scale.py) 21 % of
  the central Alps, 11 % of the Himalaya box and 16 % of the Grand Canyon box lie deeper than 0.65·T, i.e. in the
  granite/gneiss basement of most profiles; at h256 none do.
- The tests never see the current layout: `tests/RegionalGeologyTests/Program.cs:347-350` builds `UserWorld` with
  `Sea = 0.431·height` and `GoldenWorlds.cs:23` uses `VerticalScale.ForWorld` defaults (LandCurve 200 m), the pre-0.17
  layout.
- Hand-off mismatch (rock side): ground columns convert their metres to blocks with the world's depth curve
  (`EarthGeology.cs:165`, `GroundColumns.DepthCurve.For` `src/Core/GroundColumns.cs:487-497`, default log4000: terrain
  scale at the top, 4,000 m at sea level + 4 blocks down), which
  does follow world height; the regional beds below the hand-off do not. Arithmetic with the default curve: 100 m of
  cover takes ≈6 blocks at h256 and ≈24 blocks at h1024, while bed 0 is 6-20 blocks at every height, so at h1024 the
  cover consumes bed 0 and often bed 1 before the hand-off. The regional sequence itself spans ≈1,570 / 1,155 / 640 m on
  that curve.
- Failure scenario: a 1024-high world, Grand Canyon (canyon ≈ 200 blocks deep there): the walls below ~43-45 blocks
  under the plateau datum are all granite basement (real canyon: ~1.2 km of Paleozoic beds over a thin inner gorge of schist); the same world at 256 shows only
  the top two or three beds.

**R2. Mantle peridotite lies under every continental flood basalt, shallow enough to mine (V).**
- `geology-rules-v2.json:45`: `mafic-volcanic` = basalt 0.45, gabbro 0.25 (vanilla proxy basalt without GA), basement
  **peridotite**; it is the profile of every GLiM `vb` cell (3.9 % of ice-free land), and `vb` is overwhelmingly
  continental: Deccan (Pune 3×3 all `vb`), Siberian Traps (5×5 all `vb`), Paraná, Ethiopian plateau (71 %), Karoo,
  Columbia River, Arabian harrats, Iceland/Hawaii being the minority.
- Basement starts at 0.70·T ≈ 46 blocks below the datum. Deccan plateau at ~600 m: datum ≈ y 166 at h384, so peridotite
  from y ≈ 120, above sea level.
- Consequence with vanilla ores: olivine and peridot (peridotite only), chromite, pentlandite, cassiterite, ilmenite and
  bismuthinite hosts in mantle rock under India, Siberia and southern Brazil. Real flood basalts sit on continental
  crust (Indian shield granite-gneiss, Tunguska basin sediments).
- Fix: basement by setting (continental vs oceanic). Earth already ships Hasterok et al. (2022) province types
  (`global-provinces.egts`, used by ground columns: `ground-rules-v1.json` "magmatic province" → platform beds over
  granite/gneiss/gabbro; "oceanic crust" / "ophiolite complex" → basalt/gabbro/peridotite).

**R3. No ultramafic or ophiolite rock at the surface anywhere; real ophiolites and layered intrusions are mapped as
sediment or granite (V).**
- GLiM's first level has no ultramafic class; peridotite appears only as the basement of `mafic-volcanic` and
  `mafic-plutonic` (`pb`, 0.7 % of land). Host table: peridotite (olivine/peridot) is reachable on 6.4 % of
  regional-rock land and never above 46 blocks below the datum.
- v2 lookup (v2lookup.py, boxes include sea): Troodos/Cyprus 89 % siliciclastic, 10 % felsic-plutonic, 0 % mafic;
  Oman ophiolite 43 % carbonate, 30 % covered, 26 % siliciclastic, 0 % mafic; Urals main belt 57 % `sm`, 22 % `sc`;
  Bushveld 53 % siliciclastic, 3 % `pb`; New Caledonia 24 % `pb` (best case); Zambales 2 of 6 cells `pb`.
- Player impact: no chromite/nickel/PGE-style landscapes where the real ones are (Bulqiza, Kempirsai, Troodos, Bushveld,
  Great Dyke shows `mt` 8 of 9 cells), no surface peridotite for olivine. "Lack of records ≠ lack of ore" is not broken
  (granite basement still hosts vanilla chromite and pentlandite), but positive evidence is ignored.
- Fix: v3 ophiolite/ultramafic envelopes (Hasterok "ophiolite complex" polygons are already shipped) with an ophiolite
  profile (serpentinised peridotite at top or in nappes, gabbro, basalt with chert/limestone cover), and a short list of
  layered intrusions.

**R4. The 0.5° dominant-class grid erases granite districts and coastal geology, including the project's own reference
tin district (V).**
- 2.1 % of ice-free land lies in GLiM ocean/lake/no-data cells and borrows the nearest cell's class
  (`RegionalGeologyCatalog.cs:234-258`); coastal cells are where many classic districts are.
- Cornwall + Devon (49.95-50.75 N, 5.75-3.5 W): 51 % mixed-sedimentary, 49 % siliciclastic, **0 % granite**, 60 %
  borrowed. Brittany: `ss` 82 %. Dartmoor, Bodmin, Carnmenellis, St Austell, Land's End granites are all gone.
- With vanilla ores, cassiterite needs andesite/granite/basalt/peridotite/phyllite/slate (+ Earth's gneiss/schist). In
  siliciclastic the first host is slate at 37-45 blocks below the datum, in mixed-sedimentary granite at 41+. No
  surface tin indicators in the district that `docs/geology-latvia-pilot.md` names as the first tin reference.
- Similar losses (points.py): Laurion (cell `oc`), Mt Etna and Vesuvius (`oc`), Phuket and Bangka tin (`oc`),
  Cyprus copper (`oc`).
- Fix: a finer lithology raster (GLiM polygons at 0.05°, or IGME 5000 for Europe, already shipped at 0.05° in the ground
  data), or envelopes for key districts. Large effort; v3.

**R5. Vanilla ores lose whole host families in crystalline and volcanic regions: lead/silver (galena), coal, sulfur and
the other sedimentary-hosted minerals have no host at any depth on 41 % of regional-rock land; malachite on 66 % (V).**
- Profiles `metamorphic`, `felsic-/intermediate-plutonic`, `mafic-/felsic-/intermediate-volcanic`, `mafic-plutonic`,
  `pyroclastic` contain no sedimentary bed (`geology-rules-v2.json:45-52`); vanilla galena, coal, sulfur, alum, borax,
  fluorite, kernite, phosphorite and halite host only sedimentary rocks; Earth's patch adds only dolostone/quartzite.
  Earth's own lead minerals (cerussite, vanadinite, wulfenite) host only limestone/dolostone/sandstone.
- hostcov.py, share of regional-rock land with a host (surface / ≤20 / ≤45 blocks / any depth): galena 58.7 % at every
  depth (41.3 % never); malachite 10.9 / 33.6 / 33.6 / 33.6 % (needs limestone/dolostone); cassiterite, chromite,
  pentlandite, ilmenite 100 % only from 43-46 blocks (granite/peridotite basement); limonite never in 34.5 %.
- Real failures: Freiberg, Kongsberg, Sala, Broken Hill (lead-silver in gneiss/schist/granite) → no galena; Central
  African Copperbelt and Katanga (`mt`) and Falun (`pa`) → no malachite.
- This is the vanilla host design applied to Earth's coarse, sediment-free crystalline profiles, which violates "lack of
  records ≠ lack of ore". Fix options: Earth deposits (Earth's own deposit set, so vanilla-rock worlds are untouched)
  that put galena/malachite in gneiss/schist/granite/andesite, gated by a rules version (asset patches are not frozen per
  world, so a plain patch would change ore placement in new chunks of existing worlds).

### MEDIUM

**R6. Vanilla's `{rocktype}` block layers cannot map Earth's own rocks or tuff; on those top rocks vanilla lays no
sand/gravel layer, beach or lake bed (mechanism V, visible extent S).**
- Vanilla builds `BlockIdMapping` only from `rockstrata.json` variants (`BlockLayer.cs:91-110`,
  `BeachLayerProperties.cs:20-40`, `LakeBedBlockCodeByMin.cs`). Earth adds gneiss, schist, quartzite, gypsum, dolostone
  only to `worldproperties/block/rock*.json` (`assets/earth/patches/ground-rocks-rock.json`,
  `ground-dolostone-rock.json`), and tuff is not in vanilla's strata. A miss returns 0 (`BlockLayer.cs:180-186`, layer
  skipped, `GenBlockLayers.cs:363-364`) and `GenBeach` returns early (`GenBlockLayers.cs:266-268`).
- The strata postfix (0.1) runs before `GenBlockLayers` (0.4), so the top rock vanilla sees is regional. Affected tops:
  Earth's `rock-schist` (bed 0 of `metamorphic`, 14.7 % of land), Earth's dolostone (Baltic pilot top), and tuff, which
  v2 does expose: the doc and rules say tuff "is never the top bed" (`geology-rules-v2.json:16`, doc :73), but datum
  geometry puts valley floors into beds 1-3 (pyroclastic lower-bed exposure 5.5 / 14.8 / 27 % at 256 / 384 / 1024).
- Effect in `regional` mode: shield tundra without its cold gravel layer, shield deserts without sand, no beach sand
  on schist coasts. In `groundcolumns` mode the ground pass rewrites most of these tops afterwards (less visible).
- Fix: extend the vanilla mappings at world start (postfix on block-layer init) for Earth's rocks; map tuff to an
  andesite fallback or keep tuff out of bed 1. Changes generation, so gate it (rules v3 or a GenFix flag).

**R7. v2 geology is unpinned; the save-safety guarantee rests on developer discipline (V).**
- Only v1 is pinned (`tests/RegionalGeologyTests/Program.cs:74-76`, hash of grid + rules v1; golden-v1.json). Nothing
  hashes `geology-rules-v2.json` or v2 strata output. `SavedWorldDigestTests` covers terrain columns, not rocks
  (decision 0084 says so).
- A changed fingerprint only warns and is overwritten (`EarthGeology.cs:390-396`).
- v2 boundaries depend on shared code and data outside the rules file: `GeologyTransitions.Noise`/`StableHash`,
  `VerticalScale`, and `TerrainModel.Data.SampleTerrain` (the 2′ terrain grid) through the datum
  (`GeologyReference.cs:47-52`). Any change there moves bed boundaries in new chunks of every existing v2 world, with a
  visible seam.
- Fix (small, high value before the public server): hash `geology-rules-v2.json`; add a v2 golden (strata hashes over
  e.g. 30k columns at 1:200, 1:100 and 1:10, heights 256/384/1024, current default layout).

**R8. `su` (unconsolidated cover, 28.1 % of ice-free land) keeps vanilla's random strata in `regional` mode (V).**
- `HasRegionalBedrock` false for `covered` (`RegionalGeologyCatalog.cs:131`, rules :40). The su area is the
  Indo-Gangetic plain, West Siberia, the North European Plain, the Amazon, Mesopotamia, the Sahara ergs, the Yilgarn
  craton (82 % su) and Florida.
- Vanilla's province noise then places any rock under them (granite, basalt, peridotite, bottom-up kimberlite), and
  regional/vanilla seams follow the su polygon edges.
- Ground-column worlds avoid this with Hasterok templates (`GroundColumns.cs:1381-1390`); `regional` worlds do not.
- Fix: in v3, fill su from the nearest non-su class within a few cells, or use the same Hasterok/CRUST1.0 templates.

**R9. One sequence thickness for the whole planet; real cover thickness is ignored (V data, impact S).**
- T ± 15 % everywhere (`GeologyReference.cs:24,57`). Thin-cover shields get a full 40-46-block sedimentary sequence
  before their basement: Ukrainian shield 75 % `sm`, Gawler 69 % su, Sino-Korean 40 % su.
- CRUST1.0 sediment thickness is already shipped (`global-crust1.egt`). Fix: scale sequence thickness by it
  (thin → basement near the surface, deep basins → no basement). v3.

**R10. Peat/clay relief footprint is fixed at 12 blocks, so the slope rule tightens sharply at 1:10 and 1:5 (V).**
- `RegionalGeologyWorld.cs:31-45`: relief over centre ±12 blocks, span = 24 blocks in km. At 1:10 the span is 0.24 km;
  one block of relief at h256 near sea level is 16 m = 66.7 m/km, so `PeatFactor`'s relief term
  (`RegionalGeologyCatalog.cs:467-469`, zero at 60 m/km unless hyper-oceanic or mapped peat) is 0. At h384 one block
  gives 35 m/km (factor 0.52) and two blocks give 0; at 1:5 it is twice as strict. At 1:200 the span is 4.8 km and one
  block is 3 m/km.
- The doc says relief "does not tighten with world height". True, but it tightens with map scale.
- Scope: only `regional` mode (ground columns lay clay/peat themselves, `EarthGeology.cs:1100`). Fix: footprint in
  metres (e.g. ±600 m, at least 12 blocks).

**R11. Everything above the datum is bed 0 (half of all regional land), and the test meant to catch this is
tautological (V).**
- `BedV2` (`RegionalGeologyCatalog.cs:399-403`): y above boundary 0 is bed 0, so the realised bed-0 thickness is
  (top − datum) + f₀·T. About 50 % of regional land has its rock top above the datum (scale2.py). At h1024, 16 % of the
  central Alps and 12 % of the Grand Canyon box are more than T above it, so whole mountains are one bed.
- The doc claim "first-bed thickness is the same under mountains and lowlands (19.5 vs 18.8 blocks)" (doc :69) comes
  from `Program.cs:496`, which averages `Beds[0].Fraction * thick` (the nominal thickness), not top − boundary0. The
  check at :501 cannot fail.
- Fix: measure the realised bed-0 extent. Design option for v3: repeat the upper part of the profile above the datum,
  or tie the datum's smoothing to relief.

### LOW

**R12. Crafted-stone recolouring (decision 0058) silently does nothing in zip installs, i.e. for every player (V).**
- `EarthStoneTextures.cs:36-40` treats an asset as Earth's only when its origin path is under `Mod.SourcePath`. For a
  zip mod `SourcePath` is the zip file, and vanilla 1.22.7 serves the assets from
  `Cache\unpack\<file>_<hash>` (`ModContainer.Unpack` :108-130, `ModLoader.cs:192,247` `FolderOrigin(item.FolderPath)`).
- So line 85 skips all five rocks, and no "crafted stone" log line appears. The same file already notes this for the
  crushed textures (:136-137, "CR-O10") and fixed only those (by domain). Decision 0058 itself lists "checked only with
  Earth as a folder mod".
- Fix (0.5 day): also accept origins under `GamePaths.Cache/unpack/` whose folder name starts with
  `Path.GetFileName(Mod.SourcePath) + "_"`. Client-only; no world effect.

**R13. Contacts at 1:10/1:5 are long straight lines, and the 0.5° cell is the lateral resolution (V).**
- The fine warp is in blocks (`GeologyTransitionBlocks` = 96, `EarthMapConfig.cs:54`; `GeologyTransitions.cs:65-73`);
  the low octave and jitter are in cells (`:79-90`, `RegionalGeologyCatalog.cs:322-340`).
- contacts.py: at 1:10 a contact deviates from a straight line by a median 34 blocks RMS (p90 70) over 1,000 blocks;
  at 1:5, 28 (p90 45). Each category patch is ~5,560 blocks across at 1:10 and ~11,000 at 1:5.
- No grid lines remain (Voronoi), but the polygon edges are 0.5-1 cell straight segments bent only on a ~5,000-block
  wavelength. At 1:200 the same 96 blocks is ~19 km real, so contacts sit up to ~0.35 + 0.35 cell from GLiM's.
- Acceptable for v2. A finer raster (R4) is the real fix.

**R14. Doc/code mismatches (V).**
- GA child-grade adapter: doc :45 says it "runs whenever GA is installed, whether or not Earth geology is active". The
  code runs it only in regional-geology Earth worlds (`EarthGeology.cs:90-92, 464-475`), so not in default
  (vanilla-rock) Earth worlds.
- Clay "favouring" (×1.3/×1.2) cannot exceed vanilla's rate: the accept test is `Hash < clay`
  (`EarthGeology.cs:1131`), so factors ≥ 1 all mean "always". The doc's mean multiplier 0.97 overstates acceptance.
- "99 blocks at height 384" (R1); "tuff never the top bed" (R6); "first-bed thickness the same" (R11).

**R15. The `evaporitic` profile contains no evaporite (V).** `geology-rules-v2.json:53`: mudstone, limestone,
dolostone, mudstone, sandstone over granite. Earth's own gypsum rock is not used; halite only via vanilla deposits.
Salar de Uyuni's cell is `ev` and gets mudstone at the top. Small v3 change.

**R16. Smaller notes.**
- `BuildBlockTables` replaces any block whose path starts with `rock-` in any domain (`EarthGeology.cs:431-432`), so
  another mod's special `rock-*` blocks present at strata time are replaced. S, low.
- `ClearGravelUnderPatches`: an air gap does not reset the patch (`EarthGeology.cs:914`), so gravel below a cave gap
  under clay is converted. V, negligible.
- Preserved vanilla bauxite strata (`geology-rules-v2.json:25`) remain in `regional` mode (no ground columns) anywhere,
  including the Latvia pilot, which says "no natural bauxite without positive evidence". V, low.
- Voronoi seeds use hash seed 0 (`RegionalGeologyCatalog.cs:334-335`), so every world has the same seed polygons; only
  the warp is per world. V, info.
- Hawaii Big Island's centre cell is `vi`, giving andesite on a basaltic shield. Data quirk, low.
- With GA, the material proxies flip to GA blocks for new worlds only. Saved worlds keep their codes, and Earth's
  own `game:rock-gneiss/schist/dolostone` share GA's codes, so adding or removing GA keeps the codes valid. V, good.

### Checked and found sound (V)
- Save safety: unknown/newer rules versions, missing data, missing hooks, lost provider blocks, competitor mods all turn
  the feature off. `CheckFeatures` then refuses the world unless `AcceptMissingFeatures` (`EarthWorldGen.cs:615-634`).
- Strata pass: the bulk-layer path (`StrataLayer`) is equivalent to the per-block path (forward bed search, all-replaceable
  check, top-rock bookkeeping). Chunks never straddle a 64-block lattice cell, so the reference cache is hit 4 nodes per
  chunk. The ice scan matches the terrain pass.
- `RegionalRock`/`RockColumnAt` agrees with the strata pass for v2 (absolute boundaries). Ground hand-off depth and
  GenFix-4 whole-column handling are guarded (`depth < GroundIds.Length`, `y ≥ 1`).
- Polygon test, coastal fill (deterministic tie order), Voronoi wrap at the antimeridian and clamping at the poles.

---

## 3. Scale behaviour

### Vertical (world height 256 / 384 / 1024, default sea 110, log1000 curve)

| | h256 | h384 | h1024 |
|---|---|---|---|
| Thickness scale T (blocks) = 0.6 × sea | 66 | 66 | 66 |
| Land curve m/block at 0 m / at 1000 m | 15.9 / 31.8 | 8.4 / 16.8 | 2.5 / 5.0 |
| Sequence (≈0.63-0.70 T) in land-curve metres at sea level | ~1,050 m | ~555 m | ~165 m |
| Same in ground DepthCurve (log4000) metres | ~1,570 m | ~1,155 m | ~640 m |
| Undulation ±0.3 T | ±20 blocks (≈±320 m) | ±20 (≈±170 m) | ±20 (≈±50 m) |
| Regional land exposing a lower bed / basement (scale2.py) | 11.6 % / 0.0 % | 13.6 % / 0.2 % | 22.7 % / 4.5 % |

What follows what:
- Bed thickness follows **sea level in blocks** (constant 66). The underground below sea level is also a constant
  ~110 blocks, so mines see the same sequence at every height. That is defensible for gameplay, but relief (land
  curve) and the ground cover (DepthCurve) scale with height while the beds don't.
- Recommendation: express bed thicknesses in metres and convert with the same DepthCurve the ground columns use, so
  hand-off and beds agree. Then decide by playtest whether the h1024 underground should hold fewer beds.
- The undulation (a fixed ±20 blocks) dominates lowland outcrop belts at h256 (≈ ±320 m equivalent) and is minor at
  h1024. At h256 belts of different beds outcrop on flat plains purely from the undulation noise. That is intended
  ("gentle regional dip"), but its real amplitude varies with height.

### Horizontal (map scale)

| | 1:200 | 1:100 | 1:10 | 1:5 |
|---|---|---|---|---|
| 0.5° cell (blocks) | 278 | 556 | 5,560 | 11,120 |
| Fine contact warp, 96 blocks = real | 19.2 km (0.35 cell) | 9.6 km | 0.96 km (0.017 cell) | 0.48 km |
| Low warp octave λ / A (blocks) | 250 / 97 | 500 / 195 | 5,004 / 1,946 | 10,008 / 3,892 |
| Datum radius (0.6 cell) | 167 | 334 | 3,336 | 6,672 |
| Undulation wavelength (1.6 cell) | 445 | 890 | 8,896 | 17,792 |
| Steepest bed dip from the undulation (value-noise max slope ≈ 60/λ blocks per block; typical about half) | ~7.7° | ~3.9° | ~0.4° | ~0.2° |
| Contact straightness, RMS deviation over 1,000 blocks (contacts.py) | n/a (trace hops contacts) | n/a | 34 (p90 70) | 28 (p90 45) |
| Peat/clay footprint span (24 blocks) | 4.8 km | 2.4 km | 0.24 km | 0.12 km |

- Datum, undulation, low warp and jitter follow cells, i.e. real metres: correct.
- The fine warp (96 blocks), the gravel lenses (40/14 blocks) and the peat/clay footprint (24 blocks) follow blocks.
  The lenses are a game-scale texture and fine. The footprint should be metres (R10). The fine warp gives heavily lobed
  contacts at 1:200 (0.35 cell) and only a ±30-block wiggle at 1:10.
- At 1:10/1:5 a 0.5° cell is visible as a 5-11 km patch of one rock with straight-ish edges, not as a grid. Strata are
  essentially flat-lying at 1:10 (dips ≤0.4°), a uniform "layer cake" in every valley wall. Real folds and dips are not
  modelled (documented limit).

---

## 4. Data coverage

GLiM 0.5° classes, share of mapped land (area-weighted; excl. water bodies; the second figure excludes ice):

| GLiM | profile | land | land excl. ice |
|---|---|---|---|
| su (unconsolidated) | covered → vanilla strata | 25.4 % | 27.9 % |
| ss | siliciclastic | 16.5 % | 18.1 % |
| sm | mixed-sedimentary | 14.6 % | 16.0 % |
| mt | metamorphic | 13.3 % | 14.6 % |
| ig (ice) | fill within 2 cells, else vanilla | 8.9 % | — |
| sc | carbonate | 7.2 % | 7.9 % |
| pa | felsic-plutonic | 5.8 % | 6.4 % |
| vb | mafic-volcanic (peridotite basement) | 3.6 % | 3.9 % |
| vi | intermediate-volcanic | 1.8 % | 1.9 % |
| va | felsic-volcanic | 1.0 % | 1.1 % |
| wb (lakes) | fill / vanilla | 0.9 % | 1.0 % |
| pb | mafic-plutonic | 0.7 % | 0.7 % |
| py | pyroclastic | 0.6 % | 0.6 % |
| pi | intermediate-plutonic | 0.4 % | 0.4 % |
| ev | evaporitic | 0.3 % | 0.3 % |
| nd | fill / vanilla | 0.1 % | 0.1 % |

- Cells with a regional bedrock profile in v2: 62,327 (doc: 62,326). Cells filled by the coastal fill: 18,361 (doc:
  18,351), of which 15,204 get bedrock. Filled cells borrow `mt` 3,787, `sm` 3,299, `su` 3,172, `ss` 2,862, `sc` 1,504,
  `vb` 1,246, `pa` 1,169 and others. The small differences from the doc are probably polygon or rounding details of my
  port.
- Real ice-free dry land (Earth's 2′ terrain flags, 0.1°):
  - 97.8 % lies in mapped GLiM cells, 2.1 % borrows a neighbour (1.6 % with bedrock), 0.08 % is unmapped (Svalbard and
    Franz Josef, the Kerguelen/Crozet area, New Siberian Islands, the old Aral Sea bed, small islands).
  - Regional bedrock covers **71.8 %**; **28.1 % is su** (vanilla strata in `regional` mode, Hasterok templates with
    ground columns).
- Under su the GUM classes are mostly 6 (Au, 20.8 %), 30 (Us, 16.9 %), no data (14.5 %), 14 (Eu, 11.5 %), 11 (dune
  sand, 9.7 %).
- Hand-drawn envelopes: 6 provinces, 207 cell centres. Chalk is 0.2 % of land: southern England 32, Paris Basin 58,
  Denmark/Scania/Rügen 56, Austin Chalk 18, Selma 10. The Baltic Devonian pilot is 33 cells, 0.02 % of land. Everything
  else uses 12 generic class profiles.
- Where the answer is a guess:
  - Data-backed: only the dominant surface class of each 0.5° cell, shifted by up to ~0.5-0.7 cell by jitter and warp.
  - Synthesized: every bed below the first, every thickness, every basement (granite, gneiss or peridotite by class),
    and the uniform T.
  - Unknown: su and unfilled no-data cells.
  - Minor classes inside a cell (plutons, ophiolites, greenstone belts, narrow belts under 50 km) are absent.

Regional checks (region_share.py, GLiM cells in the box):
- Shields and cratons are mostly right: Guiana `mt` 46 % / `pa` 18 %, Arabian-Nubian `mt` 63 %, Tanzania `mt` 69 %,
  Zimbabwe `mt` 80 %, Slave `pa`+`mt` 85 %, Superior `pa` 38 % / `mt` 17 %, Scottish Highlands `mt` 62 %.
- Wrong or lost: Ukrainian shield 75 % `sm` (thin cover; R9), Yilgarn 82 % su, Urals 57 % `sm`, Bushveld 62 % `ss`,
  Cornwall 100 % sedimentary, Troodos `ss`/`pa`, Kamchatka 32 % `ss`.
- Volcanics are right: Iceland 97 % `vb`, Ethiopia 71 % `vb`.

Vanilla-ore host availability by profile is listed under R5 (hostcov.py output in the original run, not kept).

---

## 5. Blindspots ranked by player impact

1. **Ore families missing from whole regions under vanilla ores** (R5): no lead/silver, coal or sulfur in any
   crystalline or volcanic region (≈30 % of ice-free land), and no malachite outside carbonate regions. Players starting
   in Scandinavia, Canada, Brazil, India, Africa or the Andes volcanics never find galena under regional rock.
2. **World-height-dependent geology** (R1, R11): an h1024 world shows basement in mountain valleys and single-bed
   mountains, an h256 world shows almost only bed 0. Untested on the layout players actually get.
3. **Mantle under flood basalts** (R2): olivine, peridot and peridotite-hosted ores under the Deccan, Siberia and Paraná
   at modest depth.
4. **Missing ultramafics and ophiolites** (R3): Troodos, Oman, the Urals, the Balkans and Bushveld have no peridotite
   at the surface, so chromite/Ni/PGE landscapes are absent where they are real.
5. **Lost granite districts and coastal geology** (R4): Cornwall, Brittany and coastal tin belts. A tin player who
   follows the real map finds sandstone.
6. **su = vanilla strata in `regional` mode** (R8): random vanilla rocks under the great plains and basins, with seams.
7. **Vanilla block layers skip Earth's rocks and tuff** (R6): bare schist where vanilla would lay gravel or sand.
8. **Kimberlite fields**: no positive placement at Kimberley, Yakutia, Lac de Gras or Orapa. Vanilla's random
   bottom-up kimberlite is preserved (rules v2 :25), so diamonds are random. The principle holds (no absence), but there
   is no geography.
9. **Greenstone belts / Archean cratons**: Abitibi `pa`/`mt` (no `vb` belts at Timmins), Kalgoorlie su, Barberton `ss`
   at the point. Gold is fine (quartz hosts nearly every rock); komatiite nickel is not representable.
10. **Carbonate platforms**: Yucatán, Nullarbor, Guilin, the Dinarides and the Edwards Plateau are right. Florida is su
    (vanilla in `regional` mode). The Bahamas are mostly ocean cells, filled `sc`.
11. **Salt basins**: no rock salt or gypsum beds anywhere (R15). Halite comes only from vanilla deposits (sedimentary
    hosts, native rules) plus the 3 occurrence kernels. Zechstein (`sm`/su) and the Permian Basin (su) are expressed only
    through vanilla deposits.
12. **Metamorphic belts**: one generic schist/gneiss profile, with no kyanite zonation, marble or quartzite. Earth's
    aluminosilicate deposits (decision 0077) host on schist/gneiss, so hosts exist.

### Other mods
- **Geology Additions**: provider for dolostone, gneiss, schist, gabbro, diorite and rhyolite. Mudstone is
  deliberately excluded. GA's own strata are replaced like any `rock-*`. The child-grade clamp runs only in
  regional-geology worlds (R14). Earth's own rock patches turn off with GA (`dependsOn geoaddons invert`), so ore hosting
  of these rocks follows GA's patches. Proxies change only for new worlds.
- **Advanced Geology / Rock Strata Variety**: regional geology is turned off (`EarthGeology.cs:126-131`). A saved
  regional world is then refused unless AcceptMissingFeatures. A new world with rock choice ≠ vanilla is refused too, so
  the player must pick vanilla rocks. AG uses "dolomite", so there is no code clash with Earth's "dolostone"; Earth's
  dolostone patch lacks the AG invert but is harmless.
- **Other mods that add strata rocks**: silently replaced (any `rock-*`). Their `{rocktype}` layers work only for rocks
  present in `rockstrata.json` (R6).

---

## 6. Suggested fixes and tests

Any change to generation needs **geology rules v3** (decision 0002). Existing v2 worlds keep v2. For the public 1:10
server: decide its rock choice first. If it is `groundcolumns` or `regional`, land v3 before the world is created, or
accept v2 as is (R1-R5 frozen).

| # | Fix | Rules? | Effort |
|---|---|---|---|
| T1 | Pin v2: SHA-256 of `geology-rules-v2.json` plus a v2 strata golden over ~30k columns at 1:200/1:100/1:10 × heights 256/384/1024 on the **current** default layout. Catches changes to noise, VerticalScale and terrain data (R7) | test only | 0.5-1 day |
| T2 | Run `RegionalGeologyTests`' exposure, Grand Canyon and anti-grid checks on the current layout (sea 110, log1000), not only the legacy one; assert height-invariance bands | test only | 0.5 day |
| T3 | Replace the tautological first-bed check (`Program.cs:496-501`) with realised bed-0 extent (top − boundary0), mountains vs lowlands | test only | 1 h |
| F1 | EarthStoneTextures zip-origin fix (R12), checked on a zip install on the bench | none (client) | 0.5 day |
| F2 | Docs: thickness (66 at every height), GA adapter scope, clay cap, tuff exposure, bed-0 thickness (R14) | none | 1 h |
| F3 | Bed thickness in metres through the world's DepthCurve, consistent with ground columns' hand-off; optionally repeat the upper profile above the datum (R1, R11) | v3 | 2-3 days with tests |
| F4 | Basement by tectonic setting from shipped Hasterok provinces: continental LIP → granite/gneiss; oceanic/ophiolite → gabbro/peridotite (R2) | v3 | 1-2 days |
| F5 | Ophiolite/ultramafic profile and envelopes (Hasterok "ophiolite complex" plus a reviewed list: Oman, Troodos, Urals belt, Bulqiza, New Caledonia, Zambales, Bay of Islands, Josephine, Vourinos), layered intrusions (Bushveld, Great Dyke, Stillwater, Norilsk) (R3) | v3 | 2-4 days + review |
| F6 | Sequence thickness scaled by CRUST1.0 sediment thickness (shipped) (R9) | v3 | 1-2 days |
| F7 | su in `regional` mode: nearest non-su class within N cells, or the ground templates' Hasterok/CRUST1.0 answer (R8) | v3 | 1 day |
| F8 | Galena/malachite (and coal where real) hosts in crystalline regions through Earth's own deposit set, gated per world (R5). Needs an author call on ore scope (vanilla host design vs "lack of records ≠ lack of ore") | ore rules / v3 | 1 day + decision |
| F9 | Map Earth's rocks into vanilla `{rocktype}` layers (block layers, beach, lake bed) at world start; keep tuff out of shallow beds or give it a fallback (R6) | v3 or GenFix gate | 1 day |
| F10 | Peat/clay relief footprint in metres (R10) | frequency rule; per the doc's convention may reach existing worlds, better gated | 0.5 day |
| F11 | Evaporitic profile with gypsum and halite beds (R15) | v3 | 0.5 day |
| F12 | Finer lithology: GLiM polygons rasterised at 0.05° (licence to check), IGME 5000 for Europe (already in ground data); keeps Cornwall, Brittany, plutons and ophiolites (R4, R13) | v3 + new data file (upload needs the author) | 1-2 weeks |

Suggested order if v3 happens before the server: T1-T3 and F1-F2 now (no rules change); then F3, F4, F7, F9, F11
(cheap and broad); then F5 and F6; F8 needs a scope decision; F12 is the larger data project.
