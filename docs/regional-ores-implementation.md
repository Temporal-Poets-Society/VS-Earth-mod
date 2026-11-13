# Regional ores — rules v3 (Earth-like ores)

Earth uses an offline, editable evidence model to influence ore generation. Rules v2 (0.13.0) replaced the 0.12.0–0.12.2 runtime (rules v1): they fixed rates that had collapsed for many deposits, gave resources explicit rate classes, and made generation independent of cached region ore maps. Rules v3 (0.14.1) make the ores Earth-like ([decision 0006](decisions/0006-ore-balance.md)): deposits stay in their own depth range and no longer turn vanilla's misses into hits, known districts fill any rock, the coal basins are coal measures, and copper is within reach everywhere. Rock-contact smoothing is unchanged and is described in the geology documentation.

**Two modes.** `RegionalOres = true` is the Earth-like mode described here. `RegionalOres = false` is vanilla ore generation (vanilla's ore deposits, rates and ore maps) on Earth's rocks. Clay, peat and salt beds follow regional geology's gates in both modes while `RegionalGeology` is on (see "Peat, bog iron, clay, salt and bauxite"). Both follow the world's settings: vanilla multiplies every attempt by `globalDepositSpawnRate`, and the surface copper and surface tin settings set those variants' tries, before Earth's factor applies; `ScaleWithWorldheight` works as in vanilla. Earth applies the global rate to the coal measures and the prospecting readings too.

Open the [interactive heat-map preview](research/ore-heatmaps-v1/index.html) or the [static resource maps](research/ore-heatmaps-v1/maps/Cu.png). The viewer only changes its preview. Its download is a configuration fragment to merge into `ModConfig/earthmap.json`.

## Data and provider boundaries

The shipped JSON (`assets/earthmap/config/ore-heatmaps-v1.json`, file format 1) contains **44 layers: 42 resource commodities plus peat and ordinary clay**. Each 0.5° cell stores four values: observed evidence, assessed potential, deep evidence, and the minimum burial in metres of that deep evidence. Evidence is combined by maximum strength, not by record density. This reduces duplicate-count effects but **does not solve uneven survey coverage**, including over-representation of the US. Missing records mean "unknown", not "absent". Source hashes and authoring parameters are in [bake-manifest.json](research/ore-heatmaps-v1/bake-manifest.json). Attribution is in `assets/earthmap/config/ore-heatmaps-SOURCES.txt`.

Earth owns geography, tuning and the placement adapter. The installed providers (vanilla, Geology Additions, …) own block identities, recipes, valid host rocks and grades. Earth adapts **only the exact vanilla generator types**: `disc-followsurface`, `disc-followsurfacebelow`, `disc-followsealevel`, `disc-anywhere` and `childdeposit-pointcloud`. Subclasses or other generators from other mods keep their native behaviour, and the startup log lists them. If Advanced Geology, Interesting Ore Generation or Rock Strata Variety is enabled, Earth's ore layer stays off for that session and a warning is logged. Vanilla or the other mod's deposits are used instead.

## How attempts are computed

Vanilla multiplies each variant's `TriesPerChunk` by its ore-map factor. Earth post-patches `DepositVariant.GetOreMapFactor` for bound variants and computes that factor on the fly for each chunk:

```text
factor   = vanilla mean x class ratio(heat) x texture        (texture has mean 1)
ratio(h) = frequency x (background + (hotspot - background) x h)
heat     = bilinear, depth-accessible evidence at the chunk centre; parents use max(own, bound children)
```

- **vanilla mean** is the average factor vanilla had for that variant. It is 0.0705 for variants that vanilla generated with its sparse noise ore map (default map settings) and 0.0303 for those with scale 1.5, contrast 1.5 and sub 40. These values were measured over 864 region maps with VS 1.22.7's own `GenMaps.GetOreMap`. Unknown map settings from other mods are measured from the live layer at start-up. The mean is **1 for variants vanilla generated without an ore map**, such as clay, red clay, peat, surface copper and tin, quartz, olivine, saltpeter, the surface halite variant and sylvite. As a result, the class ratio is exactly the expected number of attempts relative to vanilla at that location.
- **texture** adds local variety. Variants that vanilla generated with an ore map get a clustered texture: a 0.3 floor with broad patches up to about five times richer, so prospecting still matters. All other variants get a soft texture of 1 ± 0.3 (1 ± 0.15 for surface starters). Both are deterministic value noise with an 8-chunk cell, salted per deposit code.
- **heat** is interpolated bilinearly between cell centres (`(x+0.5)/2-180`, `90-(y+0.5)/2`). Longitude wraps and latitude is clamped. Accessibility of deep evidence is evaluated per cell, then interpolated. There are no 0.5° steps any more.

Region ore maps are no longer read or written for bound variants. Saves from older Earth versions therefore generate new chunks at the same rates as new worlds, whether their cached maps are stale, modified by 0.12 or missing. Vanilla still builds its own noise maps for variants that vanilla maps; the prospecting pick uses their keys to know which codes to list. For variants that vanilla does not map, Earth clears the map layer so that no region maps are created for them.

### Rate classes (built into the rules)

Per-resource overrides in `Ores.Resources` always win over these built-in values.

| Class | Deposits | background / hotspot / frequency | Attempts vs vanilla | Size and burial |
|---|---|---|---|---|
| Soil | clay, red clay, peat | 0.571 / 1 / 1.75 | 1.0× without evidence, up to 1.75× at evidence; never below 0.7× | Native size; native burial, so `MinDepthBlocks`/depth settings never apply |
| Surface starters | `surface…` variants (surface copper, surface cassiterite) | 0.5 / 1 / 2 × `SurfaceFrequencyMultiplier` (v2: 0.3) | vanilla's attempts without evidence, 2× at hotspots; they fill any rock (v3) | Native size and burial |
| Progression | Cu, Sn, Fe and coal (native copper, malachite, GA copper ores, cassiterite, limonite, hematite, magnetite, lignite, bituminous coal, anthracite) | Cu 0.19 (v2: 0.09), Sn 0.19, Fe 0.17, coal 0.17 / 1 / 1.33 | 0.23–0.25× without evidence, 1.33× at hotspots | Global radius/thickness/concentration (fewer, larger deposits) |
| Quartz veins | quartz | 0.667 / 1 / 1.5 | 1.0× everywhere (silica evidence only boosts, to at most 1.5×) | Native size |
| Standard | everything else | global background/hotspot (0.08 / 1); frequency = `FrequencyMultiplier` × 4 (1.6 by default) | 0.128× without evidence, 1.6× at hotspots | Global multipliers |

**Standard compensation.** Rules v1 applied a 0.2 texture floor over vanilla's sparse map. That made every mapped ore's effective rate (0.2 + 0.8 × 0.0705) / 0.0705 = 3.64× higher than the documented `0.08 × 0.4`, and v1's undistorted ellipses were also about 15% larger per attempt. Rules v2 keep that effective volume explicitly with a built-in ×4 on `FrequencyMultiplier`; per-resource `FrequencyMultiplier` overrides are multiplied too, so they keep their 0.12 meaning. For anthracite, chromite, ilmenite and pentlandite, vanilla's sparser map (scale 1.5, contrast 1.5, sub 40) had inflated v1 to 7×. These ores now share the standard band; anthracite is in the coal progression class. Earth-authored providers (uranium, wolframite, corundum, gypsum, bauxite) keep their 0.12 tries. They have a default vanilla map, so the same rule gives their 0.12 attempt rate (+10%; volume within about 6%).

**Gem pockets.** Emerald and standalone peridot have a native radius of 1 ± 0.5. Their pockets keep the native radius: vanilla truncates the radius to 0 or 1, while Earth's rounded 1.6× radius would make the pockets 1–2 blocks and quadruple their volume (7.8× vanilla at hotspots).

**Children** are point clouds inside a parent ore, such as gold and silver in quartz, silver in galena, peridot in olivine and sylvite in halite. The parent's attempts are driven by the maximum of the parent's and the child's heat. Each placed parent block then spawns the child with a chance relative to vanilla of `child ratio(child heat) / parent ratio`, capped at 6×. As a result, a child's overall rate depends only on its own evidence; the parent's frequency is never applied a second time. A child point cloud does not get a disc's radius-squared volume gain (about 0.88× vs 2.8× vanilla volume per attempt). Child frequency is therefore `FrequencyMultiplier` × 11, which places children in the standard volume band. The child chance is computed once per chunk, not once per block.

### Measured rates relative to vanilla

These rules-v2 figures come from a scratch harness that ran the real VS 1.22.7 `NatFloat`, `LCGRandom` and `NormalizedSimplexNoise`. Rules v3 change the surface starters (×1.67), copper's floor (×2.1) and how host rock limits every class; see [decision 0006](decisions/0006-ore-balance.md) and the validation status below for the measured result. They are land-weighted over ice-free land cells of the heat map `earth-resource-evidence-2026-10-02-v2`, at each variant's typical depth. They **assume the same host rocks as vanilla**: Earth's geology changes host availability separately (see "Host bands" below). Volume is expected placed blocks per attempt multiplied by the attempts. The previous dataset (2026-09-29) gives the same floors and hotspots. Its land averages differ by up to 0.2 (sulfur 0.60 vs 0.41; progression ores within 0.08).

| Variant | v1 volume: no evidence / land avg / hotspot | v2 attempts: no evidence / land avg / hotspot | v2 volume: no evidence / land avg / hotspot |
|---|---|---|---|
| clay (each variant) | 0.03 / 0.04 / 0.34 | 1.00 / 1.03 / 1.75 | 0.99 / 1.03 / 1.74 |
| peat | 0.03 / 0.03 / 0.34 | 1.00 / 1.01 / 1.75 | 0.99 / 1.01 / 1.74 |
| surface copper | 0.07 / 0.18 / 0.85 | 0.60 / 0.80 / 2.00 | 0.58 / 0.77 / 1.92 |
| surface cassiterite | 0.06 / 0.09 / 0.75 | 0.60 / 0.66 / 2.00 | 0.56 / 0.62 / 1.88 |
| cassiterite (main) | 0.40 / 0.58 / 4.96 | 0.25 / 0.30 / 1.33 | 0.73 / 0.85 / 3.82 |
| limonite/hematite/magnetite | 0.38 / 0.62 / 4.73 | 0.23 / 0.29 / 1.33 | 0.62 / 0.79 / 3.66 |
| native copper / malachite | 0.39 / 1.01 / 4.87 | 0.12 / 0.29 / 1.33 | 0.34 / 0.82 / 3.77 |
| lignite / bituminous coal | 0.39 / 0.61 / 4.9 | 0.23 / 0.28 / 1.33 | 0.64 / 0.79 / 3.78 |
| anthracite | 0.80 / 1.25 / 10.1 | 0.23 / 0.28 / 1.33 | 0.65 / 0.80 / 3.82 |
| quartz | 0.02 / 0.03 / 0.31 | 1.00 / 1.06 / 1.50 | 1.04 / 1.10 / 1.55 |
| galena | 0.39 / 0.68 / 4.9 | 0.13 / 0.25 / 1.60 | 0.37 / 0.72 / 4.56 |
| sphalerite | 0.39 / 0.68 / 4.9 | 0.13 / 0.22 / 1.60 | 0.36 / 0.63 / 4.54 |
| pentlandite / chromite / ilmenite | 0.79–0.80 / 0.95–1.12 / 9.9–10 | 0.13 / 0.15–0.18 / 1.60 | 0.36 / 0.44–0.51 / 4.5 |
| halite (salt domes) | 0.38 / 0.57 / 4.7 | 0.13 / 0.19 / 1.60 | 0.35 / 0.53 / 4.39 |
| fluorite, phosphorite, graphite, rhodochrosite, sulfur, cinnabar, bismuthinite, borax, kernite | 0.39 / 0.42–0.54 / 4.9 | 0.13 / 0.13–0.17 / 1.60 | 0.36–0.37 / 0.38–0.50 / 4.5–4.6 |
| saltpeter, lapis, alum, olivine | 0.02–0.39 / 0.03–0.39 / 0.3–4.9 | 0.13 / 0.13 / 1.60 | 0.32–0.37 / 0.33–0.37 / 4.0–4.6 |
| diamond | 0.41 / 0.45 / 5.2 | 0.13 / 0.14 / 1.60 | 0.42 / 0.46 / 5.26 |
| emerald, peridot (gem pockets) | 0.57 / 0.57 / 7.1 | 0.13 / 0.13 / 1.60 | 0.28 / 0.28 / 3.45 |
| halite surface crust | 0.01 / 0.02 / 0.16 | 0.13 / 0.19 / 1.60 | 0.17 / 0.25 / 2.11 |
| silver in galena (overall) | 0.01 / 0.06 / 1.8 | 0.35 / 0.59 / 4.40 | 0.30 / 0.51 / 3.78 |
| gold / silver in quartz (overall) | 0.001 / 0.002 / 0.13 | 0.35 / 0.74 / 4.40 (gold) | 0.32 / 0.53–0.66 / 3.94 |
| sylvite, peridot in olivine (overall) | ≈0 | 0.35 / 0.35–0.51 / 4.40 | 0.29–0.37 / 0.32–0.54 / 3.6–4.6 |

Standard ores have a no-evidence floor of 0.32–0.37 and hotspots of 4.0–4.6× vanilla volume. Their land average equals the floor where evidence covers only a few percent of land (lapis 0%, alum 0.2%, borax 1%) and reaches about 0.6–0.7 where evidence is widespread (galena 20% coverage → 0.72). Exceptions:
- **Diamond** reaches 5.3× at hotspots, as in 0.12. Its larger pockets round up slightly.
- **The halite surface crust variant** stays low (0.17 / 0.25 / 2.1). Vanilla's climate rule caps its radius, so the 1.6× radius cannot apply. Most salt volume comes from the salt-dome variant (0.35 / 0.53 / 4.4).
- **Gem pockets** are slightly below the band (0.28). Their native radius is kept for the reason given above.

Rules v1 also had a second error: variants that vanilla generated without an ore map were multiplied by a sparse vanilla noise map (mean 0.07) that only appeared once Earth forced `WithOreMap`, leaving them at 1–3% of vanilla.

## Deposits

Each deposit is a seeded ellipse that uses vanilla's disc outline: the same edge-distortion noise and the same thickness taper. All per-deposit decisions are pure functions of (world seed, variant, deposit centre) and are cached per thread. These decisions are radius, skew, burial, grade, acceptance and host shift. As a result, every chunk that a deposit spans agrees on them. Earth's radius cap is 64 blocks, or 31 for climate-limited deposits.

- **Above-ground deposits are skipped.** Vanilla places nothing when the drawn deposit plane lies above the ground, for example sea-level ores over a deep seabed or `disc-anywhere` quartz above the surface. v1 clamped such deposits to depth 0, producing sheets directly under the soil or seabed. In v2 the whole attempt is skipped when its centre is above the ground, and any individual column above the plane is skipped too.
- **Depth-specific evidence.** Attempts are drawn at the column's best accessible heat. Each deposit is then accepted with `chance(heat at its burial) / chance(column heat)`. A buried-only hotspot therefore never boosts a shallow deposit.
- **Host bands (v3).** When Earth's strata at the deposit centre have no host for the variant at the drawn burial, the deposit may move to the nearest host band **within the variant's own depth window**, starting at the top of the band. A moved deposit is kept with the window's share of host rock: as often as a deposit there would have found its host by itself. The strata are evaluated with `EarthGeology.RegionalRock`, which is deterministic, so all chunks agree. Where Earth keeps vanilla strata (`RegionalRock` returns 0) nothing moves. Rules v2 also searched the whole column and kept every moved deposit: that turned vanilla's misses into hits, 8–154× vanilla's volume per attempt for host-limited ores (lapis, phosphorite, limonite, coal, galena).
- **Known districts fill any rock (v3).** At evidence 0.6 and above (and always for the surface starters), a standard or progression deposit places its ore in whatever rock it meets, with the ore block of the most similar host where the rock isn't one of the provider's hosts (`OreRules.SimilarHost`: carbonates take chalk's, basement the nearest igneous host, ...). Districts whose rocks have no host (Kimberley, Broken Hill, Sar-e-Sang, Pilbara, the Chilean copper) get their ore, and salt domes in salt basins reach their full height. Surface copper in the Baltic's limestone and dolostone, which have no surface-copper host, uses chalk's blocks.
- **Coal measures (v3).** Where the coal evidence reaches 0.45, coal basins begin; from 0.75 they are full coal measures (`OreRules.CoalMeasures`: Upper Silesia, Yorkshire and the Appalachians are full, South Wales and the Ruhr about 0.9). Until 2026-10-17 the ramp was 0.3 to 0.6, which spread the Ruhr's measures over 76,000 km² of world (the 0.5° evidence grid blurs neighbouring fields); see "Blended maps" for the totals and how to tune it. Up to three seams run under the basin as a pure function of position (`OreRules.BedsAt`, placed after the rock strata): the first 2–10 blocks below the surface, the others about 22 and 40 blocks down, 1–4 blocks thick, each covering 75–94% of a full basin in broad patches and pinching out at their edges. The upper two are bituminous coal, the deepest anthracite; they replace rock only, with the coal block of that rock or of the most similar host. A full basin holds about 7,500 coal blocks per chunk (vanilla: a few dozen); a basin's edge about 75. `Ores.CoalBasins` scales them.
- **Iron formations and rich districts (v3).** The same kind of ore bodies (`OreRules.BedSet`) in the great districts, graded towards rich ore with the district's strength. Iron (`OreRules.IronFormations`, iron evidence .55 to .85: Kiruna, Krivoy Rog, Lorraine, Carajás): separate bodies a few hundred blocks across, a limonite cap 2–8 blocks down (up to 3 thick), hematite about 18 down (up to 6), magnetite about 36 down (up to 8); about 3,900 iron blocks per chunk across a full district, each body a hundred or more vanilla iron deposits. Every other resource whose main deposit is rock-hosted (`OreRules.District`, evidence .7 to .9; known deposits sit at .85–.95 in the data): one bed of lenses 2–5 blocks thick at the deposit's usual depth, about 1,400 blocks per chunk across a full district. Children (gold and silver in quartz, sylvite in halite), gem pockets, soil, quartz and Earth bauxite keep their deposits. `Ores.IronFormations` and `Ores.RichDistricts` scale them; all ore bodies also follow the world's deposit rate and each resource's saved settings: they are filled with its concentration (0.85 by default, so about 15% fewer blocks than the bed counts above), stay within its MinDepthBlocks/MaxDepthBlocks, and are off where its frequency, hotspot chance or concentration is 0.
- **Concentration** is the fraction of eligible blocks filled inside the shape. **Quality** selects among the poor/medium/rich/bountiful variants that the provider actually supplies. Weak or background areas favour poor ore but keep a chance of richer finds. These are explicit gameplay priors, not measured grades.
- Surface indicator blocks follow the provider's chance, fading with depth on vanilla's scale (none below 1.11 × 9 × sea level / 110 blocks: 10 in a 256-high world, 15 in a 384-high one). Until 2026-10-24 Earth used a fixed 9 blocks, so deposits 10+ blocks down showed no indicators in 384-high worlds (new chunks only).

## Prospecting

The pick asks one variant per deposit code. Earth answers for the whole code by summing over all of that code's variants that vanilla maps. For each variant it averages, over 16 fixed draws of the native depth distribution, the share of a deposit's thickness that lands in host rock in **Earth's strata** at the probed column (vanilla's simulated column where Earth keeps vanilla strata), using placement's own rules: moved within the window and kept with the window's host share, or filling any rock in a known district (rules v2 divided by the whole column height instead, so most ores read 2–8× low and halite 4× high). The factors are the per-chunk Earth factors used for generation, times the world's deposit rate. Coal measure seams under the column are added to bituminous coal and anthracite. Codes that vanilla never listed, such as clay, quartz and the surface starters, stay unlisted. Readings remain expectations, not scans; child readings are approximate.

## Blended maps (decision 0006, "Tunable maps")

Each resource's attempts per chunk blend two maps, then the game's settings scale the result:

- **Background map**: the class floor (`BackgroundChance`, or the built-in floor of the progression, surface, soil and quartz classes), the same everywhere, gated by Earth's host rocks: "vanilla, limited to plausible rocks".
- **Evidence map**: the real data, 0.5° cells holding a shallow and a deep value with its least burial (depth is 2.5D: the data rarely says how deep a deposit is, the strata decide where hosts are, and `MinDepthBlocks` / `MaxDepthBlocks` limit depths).
- **Blend**: attempts = vanilla's × Frequency × (`BackgroundWeight` × background + `EvidenceWeight` × (hotspot − background) × evidence). Both weights default to 1 and are global or per resource.
- **Ore bodies** (coal measures, iron formations, rich districts) grow where the evidence passes a resource's district ramp, `DistrictFrom` (none) to `DistrictTo` (full), with `Richness` (times `CoalBasins`, `IronFormations` or `RichDistricts`, at most 3). Built-in ramps: coal 0.45–0.75, iron 0.55–0.85, everything else 0.7–0.9. An evidence weight of 0 switches the resource's ore bodies off.
- **Game settings last**: `globalDepositSpawnRate` scales every attempt, the ore bodies' richness and the readings; the surface copper and tin settings scale the surface starters.

District sizes at a 204,800-block ring (196 m per block; offline, `OreRules.BedsAt` at concentration 0.85; the "full" part is where the ramp is complete):

| District | Ramp | Area of world | Full chunks | Ore blocks | Per chunk |
|---|---|---:|---:|---:|---:|
| Ruhr coal | 0.3–0.6 (before) | 76,000 km² | — | 6.6 million | ~3,400 |
| Ruhr coal | 0.45–0.75 | 35,000 km² | 296 | 3.1 million | 3,400 |
| Upper Silesia coal | 0.45–0.75 | 13,000 km² | 33 | 0.9 million | 2,600 |
| Yorkshire coal | 0.45–0.75 | 39,000 km² | 140 | 2.1 million | 2,100 |
| South Wales coal | 0.45–0.75 | 10,000 km² | 22 | 0.5 million | 1,900 |
| Kiruna iron | 0.55–0.85 | 19,000 km² | 59 | 0.7 million | 1,400 |
| Lorraine iron | 0.55–0.85 | 9,600 km² | 33 | 0.4 million | 1,700 |

Vanilla averages about 64 coal and 56 iron blocks per chunk. A larger ring scales the area in chunks with the square of the ring (Ruhr at 512,000: 18.7 million coal blocks). To make a district smaller, raise its `DistrictFrom`; to make it poorer, lower its `Richness`; to shift a resource between the real data and a uniform spread, change its weights.

## Configuration

Merge this [default fragment](regional-ores-config.json) into `ModConfig/earthmap.json`. Settings are saved per world. Invalid saved settings never stop a world from loading. Unknown resource keys are ignored with an error in the log. Any other invalid value makes Earth use the defaults and log an error.

| Setting | Default | Effect |
|---|---:|---|
| `RegionalOres` | `true` | Use the geographic ore adapter in new worlds |
| `Ores.BackgroundChance` / `HotspotChance` | `0.08` / `1.0` | Standard class: regional factor without evidence and at evidence 1 |
| `Ores.FrequencyMultiplier` | `0.4` | Standard attempts = value × 4 (0.12 effective rate); children = value × 11. Progression, soil, quartz and surface classes use built-in frequencies unless a per-resource override sets one |
| `Ores.RadiusMultiplier` / `ThicknessMultiplier` / `Concentration` | `1.6` / `1.25` / `0.85` | Standard and progression deposit size. Soil, surface starters and quartz keep native size |
| `Ores.QualityBias` | `0` | −1 favours poor grades, +1 rich grades |
| `Ores.DepthMultiplier`, `MinDepthBlocks`, `MaxDepthBlocks` | `1`, `0`, `0` | Burial remapping of rock deposits. Never applied to soil, surface starters or Earth bauxite |
| `Ores.MetresPerBlock` | `5` | Converts the reported burial in metres to blocks |
| `Ores.SurfaceFrequencyMultiplier` | `1` | Extra multiplier on the surface-starter class |
| `Ores.BogIronChance` | `0.025` | Fraction of buried natural peat blocks that are bog peat, × (1 + 2 × shallow iron evidence) |
| `Ores.CoalBasins` | `1` | Coal measures under the coal basins (v3): 1 = default cover and thickness, 0 = none, up to 3 (thicker seams) |
| `Ores.IronFormations` | `1` | Iron formations in the great iron districts (v3), the same scale |
| `Ores.RichDistricts` | `1` | Rich ore bodies in the great districts of every other resource (v3), the same scale |
| `Ores.BackgroundWeight` / `EvidenceWeight` | `1` / `1` | Weights of the background map and the evidence map in every class's attempts (0..10). Evidence weight 0 also switches the ore bodies off |
| `Ores.Resources.<code>.DistrictFrom` / `DistrictTo` | built-in ramp | Evidence where the resource's ore bodies start and where they are full (coal 0.45 / 0.75, iron 0.55 / 0.85, others 0.7 / 0.9); at least 0.05 apart, within 0..1 |
| `Ores.Resources.<code>.Richness` | `1` | The resource's ore bodies, 0..3, times its family setting (`CoalBasins`, `IronFormations`, `RichDistricts`); the product is capped at 3 |

`Ores.Resources` accepts per-resource overrides with exact layer codes, such as `Cu`, `Sn`, `Fe`, `C_coal`, `NaCl`, `Al`, `peat` and `clay`. These overrides win over the built-in class values, for example `{ "Sn": { "BackgroundChance": 0.3 } }` or `{ "C_coal": { "DistrictFrom": 0.55, "Richness": 0.7 } }`. A per-resource weight replaces the global one. Changing tuning for an existing world needs `UpgradeExistingWorldOreRules=true` once.

## Peat, bog iron, clay, salt and bauxite

Natural peat, clay and halite attempts still pass Earth's wetness, relief, substrate and freshwater gates (`EarthGeology`). Where those gates allow, the soil class gives vanilla attempt rates.

When the provider's `game:stone-bogiron` item exists (Geology Additions), some **buried** natural peat blocks become `earthmap:bogpeat`. The grassed surface layer stays ordinary peat. Bog peat looks like vanilla's grass-free buried peat. Breaking it gives two peat bricks and one bog-iron item; the handbook lists these drops. It never drops itself, and placing ordinary peat never creates ore. With default settings a typical vanilla-sized peat body has roughly 1,000 blocks, about 600 of them below the surface layer. It holds roughly 15 bog-peat blocks, and up to about three times as many where iron evidence is strong. GA's rocky `bogironlarge`/`bogironsmall` deposits are disabled **only when this replacement is active**. Without the provider, nothing changes for peat and bog iron.

Clay uses mapped sediment/habitat evidence only as a boost; colour says nothing about mineral chemistry or refractory grade. Halite uses salt evidence plus the standard background on native hosts; potash evidence boosts halite attempts through its sylvite child.

Vanilla's unrestricted bauxite strata are replaced only while Earth's near-surface bauxite lenses (`earth-bauxite`, aluminium evidence, native burial of 2–15 blocks) are active. The replacement is the column's own rock: Earth strata where Earth owns the column, otherwise the neighbouring vanilla rock. This is an approximate weathering-lens model, not a lateritic palaeoclimate reconstruction.

## Existing saves and safety

- Worlds saved with ore rules v1 or v2 adopt the current rules for **newly generated chunks** only; existing blocks are never rewritten. Rate changes can be noticeable where old and new chunks meet, and a coal seam ends where it meets an older chunk. Terrain is unaffected. A setting added later (such as `CoalBasins`) takes its default in older worlds without counting as changed tuning.
- The data/provider fingerprint is informational. It hashes rules version, heat-map content (not labels), the saved tuning and the bound codes with their host blocks. It does not hash game or mod versions. A mismatch logs a warning and stores the new value.
- Missing or corrupt heat-map data, a missing hook, a conflicting ore mod or a binding failure disables only the ore layer for that session, with a log entry. The world keeps loading with vanilla deposits.
- `/wgen regen` re-binds Earth's adapter after vanilla reloads its deposit generators.

## Updating evidence

1. Update the normalized evidence/correction registry and keep source, rights, confidence and depth provenance.
2. Adjust `tools/ore-heatmap-settings.json` if needed.
3. Run `python tools/bake_ore_heatmaps.py` (NumPy and Shapely), then `python tools/render_ore_heatmaps.py` to refresh the review maps.
4. Run `dotnet run --project tests/RegionalOresTests -c Release -- <repo root>`. The rate-class targets are checked against the new data. With `--write-results`, the summary is written to `research/ore-heatmaps-v1/core-test-results.json`.

## Validation status

`tests/RegionalOresTests` (464,689 checks) covers the following:

- decoding and bindings
- rate-class assignment of all 78 bound deposit codes
- land-weighted rate targets on the real grid:
  - progression ores including coal: floor ≥ 30%, land average ≥ 70% and hotspots 3–5× vanilla volume
  - every standard layer: floor 0.30–0.45, hotspots 3–5×, land average between the floor and 0.9
  - soil and quartz at vanilla attempts; surface starters ≥ 50%
  - children in the standard band
  - Earth-authored variants within 12% of their 0.12 attempt rate
  - The suite passes on both the 2026-09-29 and the 2026-10-02 heat maps.
- bilinear continuity and wrapping
- the above-ground skip
- child gating
- tuning fallbacks, including the saved tuning of existing worlds
- texture means
- identical results on eight threads
- rules v3: the similar-host choice, district strength at named basins and districts (none in Latvia), and the ore bodies' cover, thickness, continuity, overlap and blocks per chunk

**Rules v3 in game (2026-10-16/17).** Three fresh 512000 × 204800 × 384 worlds with the 2026-10-15 harness (same seed and sites), compared with vanilla ores on the same Earth rocks and with pure vanilla:

| | Rules v2 | Rules v3 |
|---|---|---|
| No-evidence volume vs vanilla: lapis / phosphorite / limonite / rhodochrosite / sulfur | 11 / 176 / 23 / 8.1 / 1.1 | 0.84 / 3.4 / 0.14 / 0.21 / 0.36 |
| Copper / tin, no evidence and land average | 0.40, 0.73 / 0.79, 0.88 | 0.82, 1.07 / 0.78, 0.88 |
| Coal, land average (coal measures) | 2.6 | 4.8 (Ruhr: 8,600 coal blocks per chunk, 136×) |
| Iron districts | Kiruna 6.3×, Pilbara 3.4× | Kiruna 122× (6,800 blocks per chunk), Pilbara 85× |
| Empty districts | Broken Hill, Boron, Sar-e-Sang, Kimberley: nothing | 143×, 109×, 254 and 380 blocks per chunk |
| Salt domes | Zechstein 0.7× | 29× |
| Baltic start: surface copper / copper within 9 blocks / tin within 79 | 17% / 23% / 44% of land sites | 38% / 49% / 52% (pure vanilla 47% / 52% / 47%) |

Where host-limited ores are still above pure vanilla without evidence (phosphorite 3.4, graphite 2.4, malachite 2.5), Earth's rocks host them more often than vanilla's (vanilla ores on the same rocks give the same or more); the adapter itself is now at 0.1–0.9 of that. Open: silver in galena about 10× vanilla, emerald 0.15× (gem pockets are host-limited now), Gulf Coast salt below the district threshold. Scripts and results: `testruns/ore-ingame-20261016-v3*` (local), analysed with `research/ore-ingame-validation-2026-10-15/analyze3.py`.

The rules-v2 runtime has compiled against VS 1.22.7. **It was first run in a game server on 2026-10-15** (vanilla providers, Cylinder World, 512000 × 204800 × 384): see [ore-ingame-validation-2026-10-15.md](research/ore-ingame-validation-2026-10-15.md). Attempt rates, the vanilla ore-map means, the above-ground skip and the volume of host-robust ores match the design, and the server log was clean. Earth's host search and rock columns, however, push many host-limited ores far above target and leave several hotspots without hosts. Halite/sylvite and prospecting readings are off too. On 2026-10-16 the user deferred the fix; the open problems and the direction for an Earth-like ore mode are in [decision 0006](decisions/0006-ore-balance.md). The 0.12.2 server validation reports in `research/ore-heatmaps-v1/` describe rules v1. Travel distances and other providers (Geology Additions) remain gameplay validation work.
