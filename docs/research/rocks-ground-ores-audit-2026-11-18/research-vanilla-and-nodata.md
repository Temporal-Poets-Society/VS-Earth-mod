# Ore placement: vanilla 1.22.7 mechanics, and data/methods for thin-record regions

Research notes, 18 November 2026. Read-only study; nothing in any repo was changed.
Scripts used for the numbers: `simdeposits.py`, `simchild.py` (same folder). Downloaded USGS report text
used for parameters: `web/` and `web/pdf/` (same folder).

Quoting note: licence and terms texts are paraphrased with their URLs and dates; one short verbatim quote is
given for Mindat. The pages themselves are the authority.

---------------------------------------------------------------------------------------------------------

## Part A: how vanilla Vintage Story places ores (1.22.7)

Sources read (decompiled 1.22.7 is the truth; Anego `src` matches on the points below):
`devtools\vs-ref\decompiled\1.22.7\VSEssentials\Vintagestory.ServerMods\` — `GenDeposits.cs`, `GenPartial.cs`,
`DepositVariant.cs`, `DepositGeneratorBase.cs`, `DepositGeneratorRegistry.cs`, `DiscDepositGenerator.cs`,
`AnywhereDiscGenerator.cs`, `FollowSealevelDiscGenerator.cs`, `FollowSurfaceDiscGenerator.cs`,
`FollowSurfaceBelowDiscGenerator.cs`, `ChildDepositGenerator.cs`, `AlluvialDepositGenerator.cs`, `DepositBlock.cs`,
`EnumGradeDistribution.cs`, `GenMaps.cs` (GetOreMap, GetDepositVerticalDistort), `MapLayerOre.cs`, `NoiseOre.cs`,
`MapLayerPerlinWobble.cs`, `MapLayerPerlin.cs`, `TerraGenConfig.cs`, `GenTerra.cs` (sea level), `GenRockStrataNew.cs`,
`NoObf\GeologicProvinceVariant.cs`; `VintagestoryAPI\...\NatFloat.cs`, `IMapRegion.cs`;
`VSSurvivalMod\...\ItemProspectingPick.cs`, `Properties\AssemblyInfo.cs` (world config).
Assets: `%APPDATA%\Vintagestory\assets\survival\worldgen\deposits.json`, `worldgen\deposits\**\*.json`,
`worldgen\rockstrata.json`, `worldgen\geologicprovinces.json`, `patches\globaldepositspawnrate.json`,
`patches\surfacecopper.json`, `patches\surfacetin.json`, `blocktypes\stone\ore-graded.json`,
`itemtypes\resource\ore-graded.json`.

### A1. GenDeposits flow

- Registration (`GenDeposits.StartServerSide`): chunk-column pass `EnumWorldGenPass.TerrainFeatures` ("standard"
  world type), plus a `MapRegionGeneration` handler. `ExecuteOrder` 0.2. Server only.
- Loading (`initAssets`): every `worldgen/deposits/` asset of every domain is loaded as `DepositVariant[]`,
  **sorted by asset path**, flattened into one array (child deposits stay attached to their parent).
  `chanceMultiplier` = `worldgen/deposits.json` → `chanceMultiplier` (vanilla value 1).
- World config `globalDepositSpawnRate` (dropdown 0.2 … 3, default 1; defined in VSSurvivalMod `AssemblyInfo.cs`) is
  applied by the asset patch `survival/patches/globaldepositspawnrate.json`, which replaces `/chanceMultiplier`
  in `deposits.json` with the chosen value. So the code only ever sees `chanceMultiplier`.
  Two more world-config patches rewrite single deposits: `surfaceCopperDeposits` (default **0.12**, replaces
  surface copper `triesPerChunk` 0.2) and `surfaceTinDeposits` (default 0.007).
- Shape noise: one shared `NormalizedSimplexNoise` (3 octaves, freq 0.1, persistence 0.9) used for disc edge wobble.
- Per chunk column (`GenPartial.GenChunkColumn`): loops over the 7×7 neighbourhood (`depositChunkRange = 3`). For
  each source chunk (cx+dx, cz+dz) it seeds `chunkRand` by that source chunk's position, so a deposit centred in a
  neighbour is regenerated identically and only the part inside the current chunk is written.
- `GeneratePartial` (`GenDeposits.cs` l.189–220), for every deposit variant in order:
  - `F` = ore-map factor of the **source** chunk if `withOreMap`, else 1.
  - `q = triesPerChunk × F × chanceMultiplier × (scaleWithWorldheight ? MapSizeY/256 : 1)`.
  - Number of attempts = `floor(q)` plus one more with probability `frac(q)` (so the expectation is exactly q).
  - Each attempt: centre at a random x,z in the source chunk, Y = −99 (set later by the generator), the shared
    `depositRand` reseeded from `chunkRand` and the source chunk position, then `GenDeposit`.
  - After all variants, queued child deposits (`subDepositsToPlace`) are generated.
- Climate-gated variants (`climate:` block, `GenDeposits.GenDeposit` l.222–259): centre Y is set to the rain
  height map; the region climate map gives rain (0–1) and temperature at that Y; and relative height
  (`y/seaLevel` below sea level, `1 + (y−sea)/(H−sea)` above). Out-of-range → no deposit. Such variants may not
  have radius ≥ 32 (Init forces radius 10 otherwise).

### A1b. Ore maps ("heat" per deposit)

- `GenDeposits.initWorldGen` creates one map layer **per deposit code** (cached by code, so variants with the
  same code share it: both `cassiterite` variants; galena's and quartz's `silver` children share one `silver`
  map). Child deposits get their own code's map but with the **parent's** scale/contrast/sub parameters.
- Layer = `MapLayerOre(seed + hash(code) + 1)` → `MapLayerPerlinWobble(5 octaves, persistence 0.85, scale 110,
  intensity 16.5)` (`GenMaps.GetOreMap`).
- Units: ore map pixel = `oreMapScale` = 16 blocks; a 512-block region holds 32×32 (+1 padding) pixels
  (`DepositVariant.OnMapRegionGen` stores them in `IMapRegion.OreMaps[code]`).
- `MapLayerOre` places random control values on a lattice every `oreMapSubScale × OreMapScale` = 16×scale pixels
  = **256 × OreMapScale blocks**, and bilinearly interpolates between them. Each control point
  (`NoiseOre.GetRandomOre`, out of 1024): 30/1024 (2.9 %) → 255 (1/1024 of these also flags bit 0x100);
  75/1024 (7.3 %) → 75–174; 85/1024 (8.3 %) → 20–39; 834/1024 (81.4 %) → 0. Upper bits (grade-ish flags at
  bits 8, 10–11) are carried but deposits only read the low byte.
- Contrast and offset per pixel: `b' = clamp((b − 128) × OreMapContrast + 128 − OreMapSub, 0, 255)`.
- Wobble: each pixel is displaced by up to ~16 pixels (≈ 264 blocks) along noise of wavelength 110 px
  (≈ 1,760 blocks), so blobs are smeared and boundaries irregular.
- Factor `F = b'/255`, read at the chunk centre with bilinear interpolation (`DepositVariant.GetOreMapFactor`).
  For main deposits it multiplies `triesPerChunk`; for child deposits it multiplies the per-block spawn chance.
- The prospecting pick (`ItemProspectingPick`, `DiscDepositGenerator.GetPropickReading`) reads the same
  `OreMaps[code]` at the player's position: `ppt = F × (share of host-rock blocks in the column within the
  deposit's Y range) × absAvgQuantity / (terrain height × 1024) × 1000`, where `absAvgQuantity = mean(T) ×
  mean(R)² × π × triesPerChunk`. The reading is a forecast from the map, not a count of placed ore.

Ore-map statistics (Monte Carlo of the lattice + interpolation + contrast; wobble does not change area shares):

| setting | mean F | area F>0 | F>0.25 | F>0.5 | F>0.8 | p99 |
|---|---|---|---|---|---|---|
| default (scale 1, contrast 1, sub 0): all ore maps except below | **0.074** | 52 % | 10 % | 2.6 % | 0.37 % | 0.66 |
| scale 1.5, contrast 1.5, sub 40: chromite, ilmenite, pentlandite, anthracite | **0.023** | 8.9 % | 3.6 % | 1.4 % | 0.37 % | 0.58 |

Size of rich/poor areas: one full-value lattice point makes a pyramid-shaped hotspot with support
512×512 blocks (768×768 at scale 1.5); the part with F > 0.5 is ≈ 0.61 cell² ≈ 40,000 blocks² (≈ 200×200
blocks; ≈ 300×300 at scale 1.5). Full-value points are 1 in 34 lattice points, i.e. on average one strong
hotspot per ≈ 1,500×1,500 blocks (≈ 2,200 at scale 1.5); adjacent hotspots merge into larger irregular belts
after wobble. Each ore has an independent map, so rich areas of different ores do not correlate (except shared
codes). 48 % of the area has F = 0 for each default-map ore: those deposits simply do not occur there.

### A1c. Variants, host tables, grades, children

- `inblock` (`DepositBlock`): block code with wildcard, `name` (the wildcard key, e.g. `rock`),
  `allowedVariants` filter. Only target blocks whose id is in the resolved host table are replaced.
- `placeblock`: code with `{rock}` substituted from the host; either `allowedVariants` (same list for all hosts)
  or `allowedVariantsByInBlock` (grade list per host). `MaxGrade` = the longest list.
- Grade per deposit (`DiscDepositGenerator.GenDeposit` l.202–203): `g = min(MaxGrade−1, rand(MaxGrade) + bonus)`,
  bonus (only for `gradeDistribution: RandomPlusDepthBonus`) = +1 with probability `1 − relativeDepth`
  (`ypos/terrainHeight` for followsurface, `ypos/seaLevel` for followsealevel, `ypos/MapSizeY` for anywhere).
  Per placed block the index is clipped to that host's list length − 1. Consequence: hosts with short lists get
  their top grade often (e.g. native copper MaxGrade 3: sedimentary hosts [poor, medium] get medium 2/3 of the
  time; basalt list is [poor, medium, bountiful], so basalt copper is bountiful 1/3 of the time and never rich).
  The whole deposit has one grade.
- Ore value per block: graded ore blocks drop 1.25 ore items on average (`ore-graded.json`); metal units per
  item (`itemtypes/resource/ore-graded.json`): default poor 15 / medium 20 / rich 25 / bountiful 35; cassiterite
  5/10/15/20; hematite 20/25/30/40; quartz gold 5/10/15/20; quartz silver 10/15/20/25 (100 units = 1 ingot).
- `surfaceBlock` (loose ores): rolled once per deposit with `genSurfaceBlockChance` (default 1); then for every
  placed ore block, chance `surfaceBlockChance (default 0.05) × max(0, 1.11 − depthBelowSurface / (9 ×
  seaLevel/110))` to put a loose ore on the surface above → only deposits within ~10 blocks of the surface show.
- `withBlockCallback` (bituminous coal, anthracite, saltpeter): placement via `TryPlaceBlockForWorldGen`.
- Child deposits (`childDeposits`, generator `childdeposit-pointcloud`, `ChildDepositGenerator.cs`): for **every
  parent block placed**, chance `childTries / 1024` (× child ore-map factor if `withOreMap`) to queue a child at
  that block (keyed by position, so a later child type at the same block overwrites an earlier one). A child picks
  `r = floor(Radius)+1` (skips if floor ≤ 0) and `randomTries` random points in the cube of side 2r+1 around the
  centre; a point is converted only if it is a block of the parent's placed ore; grade index chosen once per
  child. Expected converted blocks per child (sheet model, `simchild.py`): quartz/olivine children ≈ 5.9,
  galena-silver ≈ 1.5, salt-dome sylvite ≈ 38, dry-lake sylvite ≈ 7.5.
- Quirks worth knowing: child target positions use `x % 32` without a chunk-bounds check, so points beyond the
  chunk edge wrap to the other side of the same chunk (only onto parent ore blocks, so the effect is small);
  `ilmenite.json` lists kimberlite grades but kimberlite is not in its `inblock`, so no ilmenite in kimberlite;
  `chromite.json` has an unknown key `method: randombyparent` (ignored); `NatFloat.nextFloat(IRandom)` has no
  `verynarrowgaussian` case (returns 0) — never use that distribution in deposit JSON.

### A2. Generators

Common disc algorithm (`DiscDepositGenerator.GenDeposit`):

- `r = min(64, floor(Radius.nextFloat()))`; r ≤ 0 → nothing. Elliptic squash `k = clamp(U − 0.5, ±0.25)`,
  `rx = r − int(r·k)`, `rz = r + int(r·k)`.
- Skip if the ellipse does not reach the current chunk ±6. Loop over columns inside the current chunk.
- Per column: `e = 1 − (r > 3 ? 0.2·noise : 0) − (dx²/rx² + dz²/rz²)`; e < 0 → outside.
- Thickness: `T = Thickness.nextFloat()`, rounded stochastically once per deposit; per column
  `here = T × clamp(2e − 0.2, 0, 1)` (stochastically rounded) → full thickness in the inner part (e ≥ 0.6), linear
  taper to zero at e = 0.1. Averaged over the disc: volume ≈ π·rx·rz·T·(0.65 − 0.2·n̄) ≈ **0.55 π r² T**.
- Blocks are written **downward** from the column's start Y for `here` blocks (y ≤ 1 skipped, y ≥ H skipped).
- `MaxYRoughness`: columns whose start Y differs from the centre Y by more than this are skipped (1 for dry lakes,
  clay, peat → flat ground only).
- `IgnoreParentTestPerBlock: true` (salt domes, soil, gravel): once one host block matched, all following blocks of
  the deposit are replaced without testing — salt domes cut through any rock.
- Vertical distortion (`getDepositYDistort`): two Perlin maps (`GenMaps.GetDepositVerticalDistort`, 4 octaves,
  persistence 0.8, scale 50 px of 2 blocks = 100 blocks, ×40 − 20) give ±20 blocks, blended top/bottom by y/H.
  **Absolute blocks, not scaled with world height.**

Placement of the start Y:

| generator (code) | class | start Y per column | relative depth for grade bonus |
|---|---|---|---|
| `disc-anywhere` | AnywhereDiscGenerator | `YPosRel × MapSizeY` + distort | ypos/H |
| `disc-followsealevel` | FollowSealevelDiscGenerator | `YPosRel × seaLevel` + distort (seaLevel = 0.4314 × H, 110 at H 256) | ypos/seaLevel |
| `disc-followsurface` | FollowSurfaceDiscGenerator | `YPosRel × local terrain height` − distort (so the sheet follows the surface) | ypos/terrain height at centre |
| `disc-followsurfacebelow` | FollowSurfaceBelowDiscGenerator | `local terrain height − Depth` (blocks) | depth/terrain height |
| `childdeposit-pointcloud` | ChildDepositGenerator | point cloud around a parent block (see A1c) | — |
| `disc-alluvialdeposit` | AlluvialDepositGenerator | surface; places `gravel-{rock}` (or `sand-{rock}` 1/3 of the time below 0.8 H) of the top rock, replacing soil only; distort noise ×1.5 | — |

Kimberlite and bauxite are not deposits: they are rock strata (`rockstrata.json`). Kimberlite: Volcanic group,
BottomUp, amplitude 100 / threshold 80 / frequency 8 → narrow pipes, only in provinces with a Volcanic group
(large igneous province, extended crust; `geologicprovinces.json`). Bauxite: Metamorphic group, TopDown layer
(platform/orogen/basin provinces). Fire clay is a deposit inside bauxite sand/gravel.

### A3. Per-ore table (H = 256, sea level 110, `chanceMultiplier` 1)

Formula (expected ore blocks per 32×32 chunk column, if the whole deposit volume were host rock):

    N = triesPerChunk × chanceMultiplier × (H/256 if scaleWithWorldheight) × F̄ × V̄
    V̄ = E[ Σ_columns here ] ≈ π · E[rx·rz] · (0.65 − 0.2·n̄) · E[T]   (n̄ ≈ 0.5; 0.65 when r ≤ 3)
    rx·rz ≈ r²(1 − k²), E[k²] ≈ 0.042;  r = floor(Radius) ≤ 64
    children: N_child = N_parent_blocks × childTries/1024 × F̄_child × (blocks per child)

V̄ and N below come from a Monte Carlo of the actual integer loop (`simdeposits.py`, 1,500–6,000 deposits each).
F̄ = 0.074 (default map), 0.023 (scale 1.5 / contrast 1.5 / sub 40), 1 without ore map. "@F=1" is the peak inside
a hotspot. Real numbers are lower by the host share: e.g. galena only replaces sedimentary rock, so in a shield
province (no sedimentary strata) it is zero. For comparison, vanilla's own propick estimate
`absAvgQuantity = tries·π·R̄²·T̄` ignores the edge taper, flooring and squash and is ≈ 2× larger.
Y ranges: "×S" = fraction of the local terrain height S (e.g. S ≈ 130 → ×130); "SL" = absolute Y at sea level 110;
all ±20 vertical distortion; deposits grow downward from that Y by their thickness.

| ore (file) | generator | tries/chunk | ore map | radius (dist) | thickness | start Y (H 256) | hosts | V̄ blocks/deposit | N @F̄ | N @F=1 |
|---|---|---|---|---|---|---|---|---|---|---|
| native copper, deep (metalore/nativecopper) | followsurface | 25 | default | 4–10 (uniform 7±3) | 1–3 (strong-invexp, mean 1.25) | 0–0.6 ×S | andesite, granite, basalt, peridotite, claystone, sandstone, shale, chalk, chert, conglomerate, phyllite, slate | 95 | 175 | 2,376 |
| native copper, surface | followsurface, no ore map, not height-scaled | 0.12 (world default; JSON 0.2) | none | 3.25–5.25 | 1–2 (mean 1.125) | 0.935–0.995 ×S | same | 29 | 3.5 | 3.5 |
| malachite (metalore/malachite) | followsurface | 10 | default | 4–10 | 1–3 | 0–0.94 ×S | limestone, red/green/white marble | 95 | 70 | 950 |
| cassiterite (metalore/cassiterite) | followsurface | 7 + 0.1 massive + 0.007 surface | default (shared) | 2–8 gauss; massive 9–15 | 1 | 0.4–0.75 ×S; massive 0–0.6 ×S; surface 0.92–0.98 ×S | andesite, granite (to bountiful), basalt, peridotite, phyllite, slate | 36 / 223 / 13 | 19 + 1.6 + 0.1 | 256 + 22 |
| bismuthinite | followsurface | 16 | default | 1–7 (triangle 4±3) | 1 | 0.05–0.85 ×S | as cassiterite (basalt to bountiful) | 25 | 29 | 395 |
| sphalerite | followsurface | 12.5 | default | 1–7 gauss | 1 | 0–0.95 ×S | 13 rocks: 4 igneous (poor/medium), 7 sedimentary (to rich), phyllite and slate (to bountiful) | 23 | 22 | 292 |
| galena | followsurface | 40 | default | 0–8 gauss | 1 | 0–1.0 ×S | claystone, sandstone, shale, chalk, limestone, chert, conglomerate | 25 | 74 | 1,004 |
| silver in galena (child) | pointcloud | 4/1024 per galena block | `silver` | — | — | in galena | galena | ≈1.5 / child | 0.03 | ≈6 |
| limonite (metalore/iron) | followsealevel | 0.8 | default | 16–36 | 1 | 5–94 SL | basalt, shale, chert | 1,125 | 67 | 900 |
| hematite | followsealevel | 0.5 | default | 16–36 | 1 | 5–94 SL | granite, peridotite, limestone, phyllite | 1,143 | 42 | 571 |
| magnetite | followsealevel | 0.3 | default | 16–36 | 1 | 5–94 SL | andesite, claystone, chalk, conglomerate, slate | 1,145 | 25 | 344 |
| lignite (coal) | followsurface | 8 | default | 5–15 gauss | 2–3.8 (mean 2.2) | 0.48–0.92 ×S | sedimentary 7 | 350 | 207 | 2,800 |
| bituminous coal | followsealevel | 7 | default | 5–15 | 2–3.3 (mean 2.2) | 11–99 SL | sedimentary 7 | 337 | 174 | 2,357 |
| anthracite | followsealevel | 6 | 1.5/1.5/40 | 3–13 | 1.5–2.1 | 0–88 SL | sedimentary 7 | 158 | 22 | 945 |
| sulfur | followsealevel | 15 | default | 6–10 | 1 | 33–99 SL | sedimentary 7 | 95 | 106 | 1,429 |
| saltpeter | followsealevel, `inblock: air` | 7 | none | 0–20 | 4–10 | −88…88 SL (only ≥ 0 counts) | cave air only | (≤1,197 if all air) | cave-dependent | — |
| borax | followsealevel | 6 | default | 6–10 | 1 | 44–110 SL | sedimentary 7 | 96 | 42 | 573 |
| halite, salt dome | followsurface, ignores host after first block | 0.3 | default | 2–18 | ~150 (cut at y 1 → ≈78) | top 0.45–0.85 ×S | starts in sedimentary 7, then any rock | ≈14,400 | 319 | 4,317 |
| halite, dry lake | followsurfacebelow, climate (rain ≤ 0.3, 15–40 °C, flat) | 0.07 | none | 25–31 | 2.3–4.3 | 1 below surface | sedimentary 7 | 4,195 | 294 (where climate allows) | 294 |
| sylvite (children of halite) | pointcloud | dome 0.3/1024, lake 1/1024 per halite block | none | — | — | in halite | halite | 38 / 7.5 per child | ≈3.6 / ≈2.1 | ≈48 |
| quartz | anywhere, no ore map | 0.5 | none | 0–64 gauss 32±32 | 1.2–4.2 (mean 1.95) | 0–256 (×H) | 13 common rocks | 3,572 | 1,786 | 1,786 |
| gold in quartz (child) | pointcloud | 6/1024 per quartz block | `gold` | — | — | in quartz | quartz | ≈5.9 / child | 4.6 | 62 |
| silver in quartz (child) | pointcloud | 20/1024 per quartz block | `silver` | — | — | in quartz | quartz | ≈5.9 / child | 15 | 206 |
| lapis | followsurface | 5 | default | 2–12 (invexp 2+10UU) | 1 | 0.05–0.85 ×S | limestone, marbles | 37 | 13.5 | 183 |
| cinnabar | followsealevel | 1 | default | 6–10 | 1 | 0–44 SL | andesite, granite, basalt, peridotite, phyllite, slate | 96 | 7 | 96 |
| olivine | anywhere, no ore map | 0.5 | none | 0–64 | 1.2–4.2 | 0–256 | peridotite | 3,598 | 1,799 | 1,799 |
| peridot (child of olivine; + gem/peridot 64 tries tiny) | pointcloud | 10/1024 per olivine block | `peridot` | — | — | in olivine | olivine | ≈5.9 / child | 7.7 | 104 |
| diamond (gem/diamond) | followsurface | 18 | default | 1–3 | 1 | 0.1–0.9 ×S | kimberlite (low/med/high), suevite (low) | 4.6 | 6.1 | 83 |
| emerald | followsealevel | 64 | default | 0–1 (mostly 0) | 1 | 0–29 SL | basalt, peridotite, shale, limestone, phyllite, slate | 0.5 | 2.4 | 32 |
| chromite | followsealevel | 3 | 1.5/1.5/40 | 5–15 (invexp 5+10UU) | 1 | 0–44 SL | andesite, granite, basalt, peridotite, kimberlite (to bountiful) | 92 | 6.4 | 275 |
| ilmenite | followsealevel | 2 | 1.5/1.5/40 | 6–16 | 1 | 5–94 SL | andesite, granite, basalt, peridotite, phyllite, slate | 116 | 5.4 | 232 |
| pentlandite | followsurface | 5 | 1.5/1.5/40 | 6–12 | 1–3 | 0–0.4 ×S | andesite, granite, basalt, peridotite (to bountiful) | 157 | 18 | 786 |
| rhodochrosite | followsealevel | 5 | default | 3–13 | 1 | 0–44 SL | 7 sedimentary + phyllite, slate | 52 | 19 | 262 |
| fire clay (soil/clay) | followsurfacebelow, climate (rain ≥ 0.27 / ≥ 0.1, ≥ −10 °C) | 0.004 + 0.0002 | none | 2–6; 7–17 | 1–3; 2.3–4.3 | surface layer | bauxite sand/gravel | 48; 775 | 0.2 + 0.2 | — |
| bauxite, kimberlite | rock strata, not deposits | — | — | — | — | — | — | — | — | — |
| flint (rock/flint) | followsurface | 1.5 + 0.05 large | none | 1.5–5.5; 4–10 | 1–2 | 0.94–1.0 ×S | sandstone, shale, claystone, chalk, limestone (large: chalk, limestone) | 21; 85 | 31 + 4 | — |

Distributions (`NatFloat`): uniform/gaussian/triangle are symmetric around avg (±var; gaussian = mean of 3
uniforms, triangle = mean of 2); `invexp` = avg + var·U·U (mean avg + var/4); `stronginvexp` = avg + var·U³
(mean + var/8); `strongerinvexp` = avg + var·U⁴. Default `dist` is uniform.

### A4. What vanilla already scales with world size or height

Scales with world height H:
- `triesPerChunk × H/256` for every variant with `scaleWithWorldheight` (default true; only surface copper is
  false) — together with the depth placements below, this keeps ore per block of rock constant.
- Depth placement: `YPosRel × H` (anywhere), `× seaLevel` where `seaLevel = 0.4314 × H` (`GenTerra.cs` l.117),
  `× local terrain height` (followsurface). `Depth` of followsurfacebelow is absolute blocks.
- Rock strata: province max thickness × H/256 (`GeologicProvinceVariant.init`), strata noise amplitudes and
  thresholds × H (`GenRockStrataNew` l.110–114).
- Loose-ore visibility depth: 9 × seaLevel/110 blocks. Climate `minY/maxY` are relative to sea level and sky.
- Terrain: noise scale max(1, H/256), +1 octave per 128 blocks above 256.

Does not scale (absolute blocks): radius (cap 64), thickness, vertical distortion ±20, `Depth`,
`MaxYRoughness`, child tries per block, the 256-block ore lattice, 1,760-block wobble, 512-block regions, and
anything horizontal: tries are per chunk regardless of how much real ground a block stands for.

Observations for Earth:
- Vanilla ore amount is a constant per chunk area, so at 1:10 (chunk = 320 m) it represents ~100× more ore per
  real km² than the same world at 1:1. Real districts (10–100 km) are 1,000–10,000 blocks at 1:10, 100–1,000 at
  1:100, while vanilla hotspots are ~200–500 blocks.
- The cleanest data hook is `IMapRegion.OreMaps[code]` (a public `Dictionary<string, IntDataMap2D>`, 16 blocks per
  pixel, values 0–255): it drives both spawn chance and the prospecting pick. `DepositVariant.OnMapRegionGen` only
  fills a code that is not yet present, so values written for a code (before vanilla, or overwritten after) are
  what both systems use. Earth already has its own ore classes (`EarthOreDeposits`, `EarthOreGenerator`,
  `SealevelDeposits`, `OreProspecting` …); this note does not review them.
- If Earth's world height differs from 256, mirror `scaleWithWorldheight` and the sea-level-relative depths;
  the ±20-block distortion and thickness do not follow height on their own.

---------------------------------------------------------------------------------------------------------

## Part B: data and methods where records are thin

### B1. Mindat / OpenMindat

What it holds: crowd-sourced, expert-curated localities with coordinates (points or polygons), mineral species
and locality-mineral occurrence lists; no tonnage or grade. About 400,000 localities and 1.54 million occurrences
(May 2024; Am. Mineral. 110, 2025, Ma et al.); 395,558 localities and 1,503,650 occurrences (Aug 2023; Que et al.
2025). Known biases: collector and economic minerals are over-reported, common minerals under-listed; coverage
follows where collectors and mining literature are (strong in Europe/North America); coordinates are not always
present and the precision varies (Am. Mineral. 2025 discusses locality bias; full text not reachable from here).

Access: free API token on registration; default limit 1,000 requests per hour for individual researchers, more
on request (Que, Zhang, Chen, Ralph, Ma 2025, "The OpenMindat v1.0.0 R package", GMD 18:4455–4467, §4,
https://gmd.copernicus.org/articles/18/4455/2025/). The same section says Mindat staff vet registrations and
defend against malicious mass downloading.

Terms (mindat.org pages return HTTP 403 to automated fetches; read via search-engine copies on 18 Nov 2026):
- Terms and Conditions, last updated 21 November 2020 (https://www.mindat.org/terms.php, mirror
  https://app2.fnc.mindat.org/terms.php): bound on all use; no archiving of non-trivial parts of the site, and
  "not allowed to copy data from this site for use in any other product". Terms can change without notice.
  A second terms text asks users not to extract information for uses other than viewing the site.
- Copyright page (https://www.mindat.org/copyrights.php; search copy timestamped 13 Aug 2025): individual pieces of
  information may be used freely; taking the whole database (e.g. commercially) is what they want to stop; they
  are working towards releasing core data (not photos) under a Creative Commons share-alike licence once a full
  API exists; personal downloads under review.
- Login page: content may not be used to train or develop AI/ML models without prior written permission.
- OpenMindat README (https://github.com/ChuBL/OpenMindat, read 18 Nov 2026): API in beta, free for all, but the
  data are not yet licensed for redistribution and are for private, non-commercial use only; lists the intended
  data licence as CC BY-NC-SA 4.0 and says data will be open once launched.

Verdict: **not usable** for anything shipped in Earth (data files, baked maps, or derived density grids) under the
current terms; UK database right also protects substantial extracts, so "derived" grids are not a safe way
around it. Private calibration by the maintainer (looking up whether a region has tin) is within
"private, non-commercial". If the announced CC BY-NC-SA 4.0 arrives, a free mod could redistribute derived data
with attribution, the NC condition, and the same licence on the derived files; re-check then.

### B2. Open alternatives and complements

| dataset | what it gives Earth | resolution / coverage | licence (as found) | verdict |
|---|---|---|---|---|
| USGS MRDS (Mason & Arndt 1996, DS-20; https://mrdata.usgs.gov/mrds/) | ~304,600 records: site, commodity, deposit type text, sometimes production/reserves; quality grade A–E | points, world, US-heavy; systematic updates stopped 2011 (last extraction 2016); "best collection USGS has" outside US | US public domain (USGS policy; data catalogue marks public domain) | **usable**, stale and uneven |
| USGS mineral operations outside the US (NMIC) | mines/plants with commodity, status | points; ~2010 | CC0 (data.gov / ODI listing) | **usable**, dated |
| USGS global distribution of critical-mineral mines, deposits, districts (Labay et al. 2018, doi:10.5066/F7GH9GQR) | points + polygons for 22 critical commodities | world | CC0 | **usable** |
| USGS Global Mineral Resource Assessment Project, SIR 2010-5090 (24 chapters, 2008–2016) | permissive tracts + **estimated numbers of undiscovered deposits** + grade-tonnage models | porphyry Cu 192 tracts, sediment-hosted Cu 48 tracts (global DB: Dicken et al. 2016, SIR 2010-5090-Z, ~66 MB GDB); potash 84 tracts + 981 sites (Orris et al. 2014, SIR 2010-5090-S); PGE southern Africa (ch. Q); regional porphyry chapters | public domain | **usable**, the most direct input for undiscovered deposits |
| USGS deposit-type world DBs with grade-tonnage models | positions + tonnage + grades of well-explored deposits | porphyry Cu 422 (OFR 2008-1155); VMS 1,090 (OFR 2009-1034); sediment-hosted Cu 785 (OFR 03-107 v1.3 2007); MVT + clastic Pb-Zn 240 (OFR 2009-1297, Taylor et al.); Ni-Co laterite 120 (OFR 2011-1058); podiform chromite 1,124 (SIR 2012-5157); carbonatite Nb-REE 58 (OFR 2009-1139) | public domain (carbonatite listed CC0) | **usable** |
| USGS Bulletin 1693, Mineral Deposit Models (Cox & Singer 1986) | 85 descriptive models (permissive geology) + 60 grade-tonnage models, App. B summary stats | global, 1986 | public domain | **usable** |
| Critical Minerals Mapping Initiative (GA–USGS–GSC): CMiO database, classification scheme (Hofstra et al. 2021, OFR 2021-1049) | deposit-environment classification (mineral systems), geochemistry of ~20,000 samples from 100+ deposit types | world | GA record: CC BY 4.0 (one mirror says not specified) | **usable** with attribution |
| Lawley et al. 2022 (Ore Geol. Rev. 141:104635) continental prospectivity for MVT and clastic Pb-Zn | gradient-boosting prospectivity grids, input layers | Canada, USA, Australia (discrete global grid) | GA record CC BY 4.0 | usable; not thin-data regions, but a worked template |
| CGMW metallogenic / tectonic maps (ccgm.org) | metallogenic maps (South America GIS, Central America), tectonic maps | continental | sold under licence agreements; scanned-map key for teaching/research only | **not usable** |
| OneGeology portal | national geological maps via WMS/WFS, target 1:1M | patchy world | per provider; central policy (c. 2015) says free of charge, conditions in each layer's metadata; e.g. BGS OGL vs a non-commercial BGS WFS mirror | **unclear**, per layer |
| GLiM (Hartmann & Moosdorf 2012, G³ 13:Q12004) | 16 lithology classes (+12/14 subclasses), ~1.2 M polygons, avg scale 1:3.75 M | world | 0.5° grid on PANGAEA: CC BY 3.0 (doi:10.1594/PANGAEA.788537); full-resolution GDB (Dropbox link) has no stated licence | grid **usable**; full-res **unclear** (ask authors) |
| USGS World Energy Project geologic maps (OFR 97-470 series, by region) | age-based geologic units for 8 world regions; also USGS world petroleum provinces (sedimentary basins) | 1:5–1:10 M-class | public domain | **usable** (from knowledge of the series; check each region's metadata) |
| Macrostrat (macrostrat.org) | harmonised geologic map units with lithology and age | world, mixed source scales | CC BY 4.0 from memory, not checked here; underlying source maps vary | likely usable; check sources |
| Hasterok et al. 2022, "New maps of global geological provinces and tectonic plates" (ESR 231:104069) | plates, boundaries, geological provinces and orogens, cratons (Archean basement), last orogenic event | world vector | Zenodo record 6586972 (1.2 GB): **CC BY 4.0**; GitHub repo dhasterok/global_tectonics labelled GPL-3.0; preprint CC BY 4.0 | **usable** (cite the Zenodo version) |
| GlobSed (Straume et al. 2019, G³ 20:1756) | total sediment thickness | oceans and marginal seas, 5′ | distributed by NOAA NCEI / PANGAEA 982339; licence not confirmed | unclear; low value on land |
| CRUST1.0 (Laske et al.) | sediment thickness incl. continents | 1° | no licence seen | unclear; too coarse anyway |
| Pelletier et al. 2016 (ORNL DAAC 1304) | thickness of soil, regolith and sediment above bedrock | 30″ (~1 km), land | NASA Earthdata: shared without restriction | **usable** (placers, laterite profiles, cover depth) |
| Depth to bedrock, Shangguan et al. 2017 (SoilGrids) | depth to bedrock | 250 m | not confirmed (SoilGrids 2017 products were ODbL from memory) | unclear |
| OpenStreetMap mines/quarries (`landuse=quarry`, `man_made=mineshaft`, `historic=mine`, `resource=*`) | where mining actually happens, worldwide incl. Africa/Asia | vector | ODbL (share-alike database; Earth already uses OSM) | usable with ODbL obligations |
| Global mining footprint polygons (Maus et al.; Tang & Werner 2023) | mine areas | world | open, licence not checked here | check |
| Kimberlite compilations (CONSOREM world kimberlites; Tappe et al. GFZ 2022 doi:10.5880/DIGIS.E.2024.001; EarthChem Woodhead set CC BY-SA 4.0) | pipe positions/ages, geochemistry | world | CONSOREM not confirmed; GFZ usually CC BY 4.0 (not confirmed); EarthChem CC BY-SA 4.0 | check per source |
| S&P Capital IQ / SNL Metals & Mining, MinEx Consulting, similar | resources, grades, owners | world | commercial | **not usable** |
| Geological Survey of Finland / SGU / NGU: Nordic ore deposit DB (NODD, ex-FODD, ~1,700 deposits) | deposits with type, size class | Fennoscandia (Russia removed 2022) | Finnish catalogue: public; SGU mineral products CC0; GTK has its own EULA page; NGU usually NLOD/CC BY | usable after checking the specific download |
| Geoscience Australia OZMIN / MINLOC | >1,000 major deposits, 60 commodities | Australia | CC BY 3.0 AU on data.gov.au (one copy "not specified") | **usable** |
| Canada (NRCan/GSC; provinces: BC MINFILE, Ontario OMI, Québec SIGEOM) | occurrences and deposits | Canada | Open Government Licence – Canada / provincial OGLs (from knowledge; verify) | usable |
| Brazil SGB/CPRM GeoSGB | geology, occurrences, mining titles | Brazil | reproduction allowed with citation on publications; no explicit open licence found for the data | unclear |
| South Africa CGS (SAMINDABA) | deposits | South Africa | much data free since 2021 but tiered access, some sold, past licences restrictive | **not usable** without agreement |
| India GSI Bhukosh / NGDR | geology, mineral occurrences | India | free after click-through; Data Sharing Policy 2019 Annex III licence text not read | unclear |
| China (CGS), Russia (VSEGEI) | — | — | no open redistribution licence known | not usable |

### B3. Methods and parameter sources

**Knowledge-driven prospectivity (mineral systems)**: map the processes a deposit type needs (source, pathway,
trap, preservation) as GIS proxies, then combine. Origins: Wyborn, Heinrich & Jaques 1994 (Australian Proterozoic
mineral systems); McCuaig, Beresford & Hronsky 2010 (Ore Geol. Rev. 38:128–138, targeting with mineral systems).
Combination methods: fuzzy logic (expert membership values per evidence layer, fuzzy gamma) for areas with few
known deposits; weights of evidence (Bonham-Carter 1994, "Geographic Information Systems for Geoscientists";
Agterberg, Bonham-Carter & Wright 1990) when enough training deposits exist; recent data-driven variants (random
forest, gradient boosting: Lawley et al. 2022). Surveys (GA, GTK, GSC) publish such maps; for a game the fuzzy
form is the right one: hand-set memberships from descriptive models, no training data needed.

**USGS three-part quantitative assessment** (Singer 1993, Nonrenewable Resources 2:69–81; Singer & Menzie 2010,
"Quantitative Mineral Resource Assessments: An Integrated Approach", Oxford UP):
1. delineate permissive tracts per deposit type from geology (descriptive model);
2. grade-tonnage model = frequency distributions of tonnage and grades of well-explored deposits of that type
   (lognormal in practice; grade and tonnage nearly independent, e.g. porphyry Cu r = 0.1);
3. estimate the number of undiscovered deposits (expert N90/N50/N10, guided by deposit densities);
then Monte Carlo the contained metal (MARK3 → EMINERS → **MapMark4**, Ellefsen 2017, USGS TM 7-C14, R, public
domain; v1.1 2024).

**Deposit densities**:
- Singer, Menzie, Sutphin, Mosier & Bliss 2001, "Mineral deposit density—an update", USGS PP 1640-A, Table 1
  (https://pubs.usgs.gov/pp/p1640a/p1640a.html): deposits per km² of permissive rock in well-explored control
  areas — low-sulfide Au-quartz veins 0.0043–0.0054; placer Au (Alaska) 0.0054–0.0061; Cyprus VMS (Troodos)
  0.0082; kuroko VMS 0.0033–0.03; porphyry Cu Nevada 0.00015, Arizona 0.0012; Climax Mo 0.00031–0.00033;
  wolframite veins (Xihuashan) 0.04; podiform chromite (California) 0.115; diamond kimberlite pipes (southern
  Africa) 0.000012; bedded barite (Nevada) 0.074; volcanogenic Mn 0.0066–0.1. Density falls with larger tract
  area (needs a regression for unbiased use).
- Singer, Berger, Menzie & Berger 2005, "Porphyry copper deposit density", Econ. Geol. 100:491–514: 2–128
  deposits per 100,000 km² of exposed permissive rock (half the tracts ≥ 15, 10 % ≥ 35); independent of age;
  inversely related to tract area.
- Singer 2008, "Mineral deposit densities for estimating mineral resources", Math. Geosci. 40:33–46 (10 types,
  108 control tracts) and Singer & Kouda 2011, Nat. Resour. Res. 20:89–93: number of deposits (and total ore
  tonnage) regressed on log permissive area (km²) and log median deposit tonnage (Mt), R² 0.91 / 0.95, with
  separate 90 %/10 % equations; coefficients in Singer & Menzie 2010 (not reachable here — get the paper).

**Grade-tonnage parameters (lognormal; log10 of tonnes, mean and SD; grades log10 % or g/t unless noted)**.
Bulletin 1693 Appendix B (Singer; https://pubs.usgs.gov/bul/b1693/html/bull5n36.htm), numbers read 18 Nov 2026:

| model (B1693 no.) | log10 t mean | SD | n | main grade (log10 unless %) |
|---|---|---|---|---|
| Porphyry Cu (17) | 8.159 | 0.686 | 208 | Cu −0.269 ± 0.190 (0.54 %) |
| Porphyry Cu-Au (20c) | 8.005 | 0.475 | 40 | |
| Porphyry Cu-Mo (21a) | 8.706 | 0.483 | 16 | |
| Kuroko VMS (28a) | 6.175 | 0.850 | 432 | Cu 0.100 ± 0.349; Zn 0.448 ± 0.463 |
| Cyprus VMS (24a) | 6.105 | 0.877 | 49 | Cu 0.204 ± 0.307 |
| Sediment-hosted Cu (30b) | 7.341 | 0.919 | 57 | Cu 0.332 ± 0.254 |
| SEDEX Zn-Pb (31a) | 7.167 | 0.732 | 45 | Zn 0.752 ± 0.291; Pb 0.444 ± 0.344 |
| SE Missouri Pb-Zn + Appalachian Zn (32) | 7.542 | 0.930 | 20 | Zn 0.608 ± 0.362 |
| Low-sulfide Au-quartz veins (orogenic, 36a) | 4.470 | 1.163 | 313 | Au 1.203 ± 0.333 g/t |
| Homestake Au (36b) | 5.974 | 0.859 | 116 | Au 0.965 ± 0.252 g/t |
| Placer Au-PGE (39a) | 6.030 | 1.312 | 65 | Au −0.698 ± 0.293 g/t |
| Sn greisen (15c) | 6.857 | 0.745 | 10 | Sn −0.549 ± 0.171 |
| Sn skarn (14b) | 6.774 | 0.618 | 4 | Sn −0.503 ± 0.301 |
| Sn veins (15b) | 5.374 | 1.000 | 43 | Sn 0.104 ± 0.203 |
| W skarn (14a) | 6.016 | 1.025 | 28 | WO3 −0.183 ± 0.243 |
| W veins (15a) | 5.748 | 0.857 | 16 | WO3 −0.040 ± 0.141 |
| Algoma + Superior Fe (BIF, 34a/b) | 8.218 | 0.911 | 66 | Fe 50.8 ± 13.7 % (arithmetic) |
| Fe skarn (18d) | 6.858 | 1.041 | 168 | Fe 49.6 ± 10.3 % |
| Lateritic Ni (38a) | 7.645 | 0.586 | 71 | Ni 0.135 ± 0.104 |
| Laterite bauxite (38b) | 7.401 | 1.142 | 122 | Al2O3 45.0 ± 7.7 % |
| Karst bauxite (38c) | 7.366 | 0.681 | 41 | Al2O3 49.2 ± 7.9 % |
| Major podiform Cr (8a) | 4.321 | 0.760 | 174 | Cr2O3 44.0 ± 7.3 % |
| Minor podiform Cr (8a) | 2.112 | 0.929 | 435 | |
| Carbonatite (10) | 7.777 | 0.441 | 20 | Nb2O5 −0.195 ± 0.356 |
| Shoreline placer Ti (39c) | 7.942 | 0.701 | 61 | |

Model numbers follow the 1986 B1693 scheme; 17, 20c, 21a, 24a, 28a were confirmed in the USGS reports, the
others are from memory. A few cells in the USGS HTML have typos (".3o68", "1 . 142", "1-312"), read as 0.3068,
1.142, 1.312.

Updated models (percentiles: 10 % of deposits ≥ first value, median, 90 % ≥ last value):
- Porphyry Cu, Singer, Berger & Moring 2008, OFR 2008-1155, Table 1: general (n 422) 1,500 / **240** / 33 Mt,
  Cu 0.75 / 0.44 / 0.24 %; Cu-Au 1,200 / 200 / 34 Mt, Au 0.76 / 0.38 / 0.21 g/t; Cu 1,400 / 250 / 30 Mt;
  Cu-Mo 4,800 / 280 / 48 Mt. (log10 SD ≈ 0.65.)
- VMS, Mosier, Berger & Singer 2009, OFR 2009-1034, Table 2: felsic (n 421) 36 / 3.0 / 0.15 Mt, Cu 3.2 / 1.2 /
  0.3 %, Zn 10 / 3.2 / 0 %; bimodal-mafic (272) 31 / 1.9 / 0.14 Mt, Cu 3.5 / 1.4 / 0.35 %; mafic (174)
  15 / 0.74 / 0.03 Mt, Cu 4.1 / 1.7 / 0.61 %.
- Ni-Co laterite, Berger, Singer, Bliss & Moring 2011, OFR 2011-1058, Table 10 (n 117): 290 / 56 / 9 Mt,
  Ni 1.8 / 1.3 / 0.83 %, Co 0.11 / 0.04 / 0 %.
- Podiform chromite, Mosier et al. 2012, SIR 2012-5157: major median 11,000 t at 45 % Cr2O3; minor 100 t; banded
  650 t (100 m proximity rule).
- Sediment-hosted Cu, Cox et al. OFR 03-107 v1.3 (2007): general + reduced-facies, red-bed, Revett subtypes
  (2 km rule); numbers in the PDF.
- MVT / clastic-dominated Pb-Zn: Taylor et al. 2009, OFR 2009-1297 (121 CD + 113 MVT deposits; data, not fitted
  models — fit lognormals from table A1).
- Potash: Orris et al. 2014, SIR 2010-5090-S (84 tracts, 981 sites).
- Kimberlites: Bliss 1992, "Grade-tonnage and other models for diamond kimberlite pipes", Nonrenewable Resources
  1:214–230 (not read here).
- Coal basins and evaporite basins have no deposit-count models; treat them as basin polygons (USGS world
  petroleum provinces / geologic maps) with seam/bed thickness rules instead.

Deposit definitions differ (proximity rules 100 m, 1.6 km, 2 km); densities and grade-tonnage models must use the
same definition, which the USGS sets keep consistent per type.

**How a generator could use this (sketch for planning, not an agreed design)**:
1. Offline, per deposit type: permissive mask from lithology (GLiM grid / USGS world geology / Macrostrat) ×
   tectonic setting and age (Hasterok provinces, cratons, plate boundaries; descriptive models in B1693 and the
   CMMI classification). Examples: arcs and young orogens → porphyry Cu, epithermal Au; Archean cratons with
   greenstone → orogenic Au, komatiitic Ni; ophiolites → podiform Cr, Cyprus VMS; rifted sedimentary basins with
   red beds over reduced shales → sediment-hosted Cu, SEDEX; carbonate platforms → MVT; evaporite basins → halite,
   potash; tropical old surfaces on ultramafic / aluminous rock → Ni laterite / bauxite; large mafic intrusions →
   stratiform chromite, PGE; drainages below known lodes → placers.
2. Number per tract: where the USGS GMRAP has a tract, use its N90/N50/N10 for undiscovered deposits; elsewhere
   apply a density (PP 1640-A, Singer et al. 2005) or the Singer–Menzie regression on tract area and median
   tonnage; draw N from a distribution matching those percentiles; subtract nothing for unmapped regions (the
   point is to fill them), but keep known deposits (MRDS, USGS type DBs, national DBs) as fixed points.
3. Positions: inhomogeneous Poisson (or Neyman–Scott clustered, districts of ~10–50 km) inside the tract, weighted
   by a fuzzy prospectivity surface; keep the type's proximity rule from known deposits.
4. Size: tonnage ~ lognormal(log10 mean, SD) from the tables above; grades drawn independently (lognormal);
   contained metal = tonnage × grade.
5. Endowment check: sum contained metal by type and continent and compare with identified + estimated
   undiscovered resources (USGS global copper assessment, potash, PGE chapters) and production history; rescale N
   where a region comes out implausibly rich or poor.
6. Game mapping: contained metal → ore-map intensity (per code, as in `OreMaps`) and/or the number and size of
   game deposits inside the real footprint; grade percentiles → poor / medium / rich / bountiful. Bake once with a
   fixed seed per tract so world generation stays repeatable. Keep the existing rule that every area has a
   host-rock background (lack of records is not lack of ore).

Scale check: at 1:10 a porphyry Cu tract with 15 deposits per 100,000 km² has one deposit per ~1,000 chunks
(chunk = 0.1 km²); vanilla puts deep copper in every chunk. Real deposits can define *where* the rich districts
are, but gameplay density per chunk will need its own calibration.

### B4. Procedural-generation precedents (brief)

- Dwarf Fortress: minerals declared per host stone with environment and inclusion type (vein, cluster, small
  cluster, single), so ores follow lithology layers.
- TerraFirmaCraft (1.18+ docs, https://terrafirmacraft.github.io/Documentation/1.20.x/worldgen/features/veins):
  vein features per rock list with rarity (1 in N chunks), size, density, Y range, cluster/disc/pipe shapes, and
  surface indicators with their own rarity and depth — closest to "deposit + indicator" design.
- GregTech-style vein layers, Geocraft (coal layers, kimberlite pipes), "ore veins" mods: lens shapes tapering at
  the edges, rock- and biome-dependent spawning.
- Spatial statistics: deposits cluster at several scales (fractal clustering, e.g. Carlson 1991, Geology 19;
  citation from memory, not re-checked),
  and size distributions are lognormal with heavy upper tails — both argue for clustered placement and lognormal
  sizes rather than uniform noise.
- No peer-reviewed work on "believable resource distribution in game worlds" turned up; the USGS assessment
  literature is the practical basis.

### Sources (accessed 18 Nov 2026)

- Mindat terms: https://www.mindat.org/terms.php (mirror https://app2.fnc.mindat.org/terms.php); copyright:
  https://www.mindat.org/copyrights.php; OpenMindat: https://github.com/ChuBL/OpenMindat;
  Que et al. 2025: https://gmd.copernicus.org/articles/18/4455/2025/
- USGS: MRDS https://mrdata.usgs.gov/mrds/; copyright policy https://www.usgs.gov/information-policies-and-instructions/copyrights-and-credits,
  data licensing https://www.usgs.gov/data-management/data-licensing; SIR 2010-5090 https://pubs.usgs.gov/publication/sir20105090;
  ch. Z https://pubs.usgs.gov/publication/sir20105090Z; ch. S https://pubs.usgs.gov/publication/sir20105090S;
  OFR 2008-1155 https://pubs.usgs.gov/of/2008/1155/; OFR 2009-1034 https://pubs.usgs.gov/of/2009/1034/;
  OFR 03-107 https://pubs.usgs.gov/of/2003/of03-107/; OFR 2009-1297 https://pubs.usgs.gov/of/2009/1297/;
  OFR 2011-1058 https://pubs.usgs.gov/of/2011/1058/; SIR 2012-5157 https://pubs.usgs.gov/sir/2012/5157/;
  OFR 2009-1139 https://pubs.usgs.gov/of/2009/1139; Bulletin 1693 https://pubs.usgs.gov/bul/b1693/;
  PP 1640-A https://pubs.usgs.gov/pp/p1640a/p1640a.html; Singer 2008 https://pubs.usgs.gov/publication/70197201;
  Singer et al. 2005 https://pubs.usgs.gov/publication/70197200; Singer & Kouda 2011 https://pubs.usgs.gov/publication/70034133;
  MapMark4 https://pubs.usgs.gov/publication/tm7C14; critical minerals global https://www.usgs.gov/data/global-distribution-selected-mines-deposits-and-districts-critical-minerals;
  CMiO https://pubs.usgs.gov/publication/fs20253002, OFR 2021-1049 https://pubs.usgs.gov/publication/ofr20211049
- GLiM https://doi.pangaea.de/10.1594/PANGAEA.788537; Hasterok et al. https://zenodo.org/records/6586972,
  https://github.com/dhasterok/global_tectonics; Pelletier https://daac.ornl.gov/cgi-bin/dsviewer.pl?ds_id=1304;
  GlobSed https://doi.org/10.1029/2018GC008115
- CGMW shop https://ccgm.org/en/?p=681, https://ccgm.org/en/?p=964; OneGeology IPR https://onegeology.org/docs/OneGeologyIntellectualPropertyRights200815-English.pdf
- GTK/NODD https://www.gtk.fi/en/fennoscandian-mineral-deposits-application-ore-deposits-database-and-maps/;
  SGU https://www.sgu.se/en/mineral-resources/mineral-exploration-in-sweden/; OZMIN https://data.gov.au/data/dataset/34247a24-d3cf-4a98-bb9d-81671ddb99de;
  GSI policy https://geodataindia.gov.in/assets/Document_Pdf/4.%20Data%20Dissemination%20policy%202019.pdf;
  CGS https://www.engineeringnews.co.za/article/council-for-geoscience-goes-live-with-digital-geoscience-data-2021-06-01
- Lawley et al. 2022 https://pubs.usgs.gov/publication/70226879; TFC docs above.
