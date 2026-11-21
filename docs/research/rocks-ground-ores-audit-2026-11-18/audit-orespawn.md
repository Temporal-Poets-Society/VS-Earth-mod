# Earth ore spawn audit (Earth 2.1.4, `main` at 1ac0f2d)

Read-only audit, 18 November 2026. No repo edits, no builds into `release\`, no game or bench runs. Numbers come from the
code paths re-implemented in small scripts in
`scripts\orespawn\`
(`list_deposits.py`, `scaling.py`, `quantize.py`, `table.py`, `catalogue.py`; outputs `*.txt`, `*.json` beside them),
using vanilla 1.22.7 deposit JSON, Earth's patches (`assets/earth/patches/ground-rocks-deposits.json`), Earth's deposit
assets and the four catalogue files. "Verified" = read in code and, where a number is given, reproduced by script.
"Suspected" = follows from the code but needs a model or bench check.

Abbreviations: v3/v4/v5 = `EarthParams.OreRules` 0 (logged "v3"), 4, 5. H = world height. h = metres per block
north-south. v = the depth curve's metres per block. bpc = ore blocks per chunk column (32x32), host rock assumed
present (an upper bound; the same assumption for vanilla and Earth so ratios hold).

---

## 1. Pipeline map

### 1.1 Which rules a new world gets

| Earth tab | `OreLayers` | `OreRules` | Heat maps | What runs |
|---|---|---|---|---|
| Rocks vanilla, ores vanilla (default since 2.0.1) | 0 | 0 | none | vanilla deposits on vanilla strata + Earth's own ores/gems (deposit set 1). No layer pass, no layer deposits. |
| Rocks ground columns, ores vanilla | 4 | 0 | none | vanilla deposits on Earth's columns, 0060 layer pass, Earth layer deposits (placers, salt flats, bauxite, kaolin, gypsum, bog iron with GA, aluminosilicates), surface flint/chert |
| Rocks ground columns, ores maps | 4 | **5** | v2 | rules v5 below |
| Rocks **regional or vanilla**, ores maps | 0 | **0** | **v1** | **rules v3** (fill any rock in districts, coal measures, iron formations, rich districts, heat-boosted background) |

Source: `src/WorldParamsBuilder.cs:98` (`oreLayers` only with ground columns), `:224`
(`OreRules = RegionalOres && Layers ? CurrentRules : 0`), `src/EarthOres.cs:113-114` (v2 heat maps only for rules >= 4),
`src/EarthWorldOptions.cs:108-130` (rocks and ores are independent tab choices).

### 1.2 Passes per chunk column (order)

1. **Terrain / rock strata** (Earth's terrain writer, vanilla `GenRockStrata`). Hook `EarthOres.AfterStrata`
   (`src/EarthOres.cs:354-383`): bauxite-strata replacement (`src/OreBauxiteStrata.cs:17-54`) when the ore maps are on
   and the strata hook bound; v3 ore bodies here **only in worlds without ore layers** (`OreSession.BodiesAfterStrata`,
   `src/OreBinder.cs:59-60`).
2. **Ground pass** (`EarthGeology`), records the chunk's layer table (`EarthOreLayers.Begin/Record/Keep`,
   `src/EarthOreLayers.cs:366-408`). Event `AfterGround` (`src/EarthGeology.cs:719`): v3 ore bodies in ore-layer worlds
   (`src/EarthOres.cs:400-411`, `src/OreV3Bodies.cs:109-151`); in v4/v5 `V3OreBodies.Build` returns early
   (`src/OreV3Bodies.cs:43`).
3. **TerrainFeatures: vanilla `GenDeposits`** (`decompiled/1.22.7/VSEssentials/.../GenDeposits.cs:189-220`):
   for each of the 7x7 neighbour chunks (`depositChunkRange = 3`, line 15), every variant gets
   `tries * oreMapFactor * chanceMultiplier * (H/256 if scaleWithWorldheight)` attempts, the fraction rounded with
   `NextInt(100) < 100*frac` (lines 202-204). Earth's prefilter `EarthGeology.BeforeDeposit`
   (`src/EarthGeology.cs:1069-1110`): clay/peat/soil/gravel/GA bog iron/surface halite off under columns, layer deposits
   only where columns own the column.
   - **Vanilla ores:** vanilla generators place as they do.
   - **Ore maps:** every supported vanilla/Earth/other-mod disc and child variant is wrapped by `EarthOreGenerator`
     (`src/OreBinder.cs:35-97`, `:155-212`); vanilla asks `GetOreMapFactor`, post-patched to
     `OreSession.AttemptFactor` (`src/EarthOres.cs:341-350`, `src/OreSession.cs:200-215`). Each disc is a pure
     function of (seed, variant, centre) (`src/EarthOreGenerator.cs:317-379`), host search inside its depth window
     (`:387-438`), every block asks its 0060 layer (`:535-539`). Authored adapters uranium and corundum are appended
     (`src/OreBinder.cs:109-115`).
4. **Postfix `EarthGeology.AfterDeposits`** (`src/EarthGeology.cs:876-895`): gravel-lens cleanup, then the **0060 layer
   pass** `EarthOreLayers.Apply` (`src/EarthOreLayers.cs:467-545`: deposit blocks in a disallowed layer get the column's
   material back, orphan indicators removed, v3 layer rules' surface flint/chert laid `:553-576`), then
   **`AfterDepositLayers`**: the v4/v5 **catalogue** `EarthOreDeposits.Place` (`src/EarthOreDeposits.cs:310-352`),
   then the layer table is released.
5. **Vegetation pass:** rock floor fill (`src/RockFloorPass.cs`) in worlds taller than 512 (auto 160 blocks below the
   chunk's lowest top, `src/EarthWorldOptions.cs:304-311`).

### 1.3 Rules generations

| | v3 (`OreRules` 0) | v4 | v5 (current for new map worlds) |
|---|---|---|---|
| Background attempts | class ratio x heat boost (`OreSession.cs:214`) | class floor only, heat raises only quartz, clay, peat, bog iron, salt flats (`OreDeposits.cs:1225-1228`) x `AttemptScale` (`OreSession.cs:213`) | as v4 |
| Background size | 1.6R / 1.25T / 0.85 fill | radius x 100/h, capped 64 (`OreDeposits.cs:1231-1239`, `EarthOreGenerator.cs:115-142`) | as v4 |
| Fill any rock | heat >= 0.6 and surface starters, without layers (`OreBinder.cs:208`) | never (layers) | never |
| Ore bodies | coal measures, iron formations, districts (`RegionalOreCatalog.cs:462-524`) | catalogue bodies and basins (`OreDeposits.cs:410-1192`) | + blanket valley search, frozen loose hosts (`OreDeposits.cs:438-456`, `:836-867`; `EarthOreDeposits.cs:74-88`) |
| Heat maps | v1 | v2 | v2 |

---

## 2. Findings, ranked by player impact

Severity is for players, with the planned public 1:10 no-wipe server in mind.

### F1 (High, Verified). Vanilla's 1 % attempt rounding breaks rules v4/v5's scale compensation

- **Where:** `GenDeposits.cs:203-204` (vanilla: `num8 += NextInt(100) < 100f*(num7-num8) ? 1 : 0`) against
  `src/OreSession.cs:213` and `src/EarthOreGenerator.cs:142` (`AttemptScale = baseVolume/vol`).
- **Logic:** v4 makes deposits bigger at fine scales and divides attempts by the volume gain so bpc stays as at 1:100.
  But vanilla turns any fractional attempt rate into a chance in whole percent, rounded **up**: every rate in
  (0, 0.01] per chunk and variant becomes 0.01. Earth's texture never reaches 0 (clustered floor 0.3,
  `RegionalOreCatalog.cs:381-385`), so every chunk is rounded up. The prospecting pick uses the unrounded expectation
  (`src/OreProspecting.cs:50-53`), so it under-reads by the same factor.
- **Size (script `quantize.py`, `quantize.txt`):** effective / intended attempts per variant:

  | Variant | 1:100/384 | 1:10/384 | 1:10/1024 | 1:5/1024 |
  |---|---|---|---|---|
  | cassiterite (common) | 1.03 | 3.0 | 1.6 | 1.6 |
  | cassiterite (massive, 0.1 tries) | **3.8** | 38.6 | 14.5 | 14.5 |
  | surface copper | 1.02 | 4.9 | 4.9 | 9.8 |
  | surface cassiterite | 1.5 | 81 | 30 | 117 |
  | magnetite / hematite | 1.8 / 1.4 | 2.8 / 2.0 | 1.5 / 1.3 | same |
  | anthracite | 1.08 | 3.4 | 1.7 | 1.7 |
  | halite domes | **2.7** | 22.8 | 8.6 | 8.6 |
  | sphalerite / lapis / kernite | 1.0-1.2 | 4.6 / 7.0 / 8.6 | 2.1 / 2.8 / 3.4 | same |
  | Earth's azurite / tetrahedrite / chalcopyrite | 1.05-1.2 | 5.9 / 11.6 / 2.7 | 5.9 / 11.6 / 2.7 | same |
  | corundum adapter (refractory substitute, 0077) | 2.8 | **153** | **153** | 153 |
  | uranium adapter | 1.1 | 24 | 24 | 24 |

- **Failure scenario:** a new 1:10 ore-maps world (the server) has about 6x the intended tin per chunk, 8x the salt,
  150x the corundum, and the "clustered" texture (rich and poor patches) is flattened to a uniform 1 % per chunk for every
  rare variant. Even at the reference scale 1:100 the massive cassiterite and halite domes are 2.7-3.8x their design.
- **Fix:** in a new ore rules version keep vanilla's rate at or above ~0.05 per chunk: `AttemptFactor` returns
  `F*K` with `K = ceil(0.05/rate)` and `GetPlan` drops a deposit unless a seeded roll on (seed, variant, centre) is
  below `1/K` (exact in expectation, deterministic, chunk-order free). Apply the same `K` in prospecting. Effort: about a
  day plus a test that replays vanilla's loop (`NextInt(100)` rounding) and asserts bpc within 5 % of 1:100 at 1:200,
  1:10 and 1:5.

### F2 (High, Verified). World height multiplies background ore in lowlands; the 1:100 calibration does not hold at 1:10

- **Where:** `GenDeposits.cs:197,202` (`* MapSizeY/256` for `scaleWithWorldheight`, default true for every vanilla
  file); `src/EarthOreGenerator.cs:114` uses `triesScale` only for prospecting; `AttemptFactor` does not compensate.
  Earth fixes sea level at 110 at every height (`src/Core/VerticalTerrainOptions.cs:26`), so a lowland column has the
  same ~110-120 blocks of rock at H 256, 384 and 1024.
- **Size:** bpc of every vanilla-file variant x 1.5 at H 384, x 4 at H 1024 (vs 256). The 1:10 play style is H 1024, the
  1:100 calibration world H 384: x 2.67 before F1. Earth's own `earth-ores.json` ores have `scaleWithWorldheight:
  false`, so the balance set by `tools/earth_deposit_rates.py` (chalcopyrite "= malachite per chunk") holds only at H
  256; at H 1024 Earth's copper, lead and zinc ores are a quarter of their vanilla counterparts' weight.
- **Combined F1 + F2, Earth v4 background bpc (`table.txt`):**

  | | vanilla H384 | vanilla H1024 | v5 1:200/384 | v5 1:100/384 | v5 1:10/1024 | v5 1:5/1024 | v5 1:10/384 |
  |---|---|---|---|---|---|---|---|
  | copper | 536 | 1232 | 372 | 378 | **1154** | 1189 | 674 |
  | tin | 36.5 | 97 | 26 | 31 | **182** | 227 | 155 |
  | iron | 208 | 556 | 140 | 179 | **429** | 429 | 242 |
  | coal | 669 | 1784 | 414 | 424 | **1315** | 1314 | 627 |
  | lead/zinc | 255 | 544 | 88 | 90 | 355 | 356 | 275 |
  | sulfur (+pyrite) | 295 | 590 | 104 | 108 | 333 | 333 | 239 |
  | borax/kernite | 94 | 251 | 34 | 36 | 177 | 177 | 145 |
  | halite | 1467 | 3912 | 541 | 1045 | **8177** | 8205 | 8052 |
  | quartz | 2890 | 7705 | 2862 | 2876 | 7672 | 7672 | 2911 |
  | flint | 69 | 184 | 24 | 25 | 151 | 143 | 127 |

  So "blocks per chunk stay as at 1:100" (0065 §5) is 3-8x off on the 1:10 play style.
- **Fix:** in the same new rules version multiply bound variants' factor by `384/H` (or 256/H with recalibrated class
  floors). Vanilla-ore worlds keep vanilla behaviour (or get an optional height-neutral switch). Effort: small, with the
  F1 test extended over H.

### F3 (High, Verified). Highland spawns have no iron, no bituminous coal, no sulfur, borax or saltpeter within reach

- **Where:** vanilla `disc-followsealevel` deposits are at absolute y = yPosRel x sea level (iron 5-94, bituminous
  11-99, anthracite 0-88, sulfur 33-99, borax 44-110, saltpeter <= 88; decision 0045 keeps them at Earth's 110). The ore
  maps' host search moves only inside the same window (`src/EarthOreGenerator.cs:160-165, 401-402`). The auto rock floor
  cuts everything more than 160 blocks below the chunk's lowest top in worlds over 512 high
  (`src/EarthWorldOptions.cs:306-311`). Bog iron exists only with Geology Additions (`src/EarthOreLayers.cs:82-85`,
  `src/EarthOreContent.cs:20-26`). Already observed in `docs/research/tall-world-ores-2026-11-03.md` ("highland regions
  get no iron, coal, chromite ... at any depth"), with no backlog item.
- **Size** (flat column; elevations approximate):

  | Spawn | m | 1:100/384: iron depth (blocks) | 1:10/1024: iron above floor | bit. coal | native Cu | malachite |
  |---|---|---|---|---|---|---|
  | Addis Ababa | 2355 | 160-248 | 0 % | 0 % | 9 % | 42 % |
  | Mexico City | 2240 | 156-244 | 0 % | 0 % | 12 % | 44 % |
  | Nairobi | 1795 | 139-227 | 0 % | 0 % | 24 % | 51 % |
  | Pretoria | 1339 | 118-206 | 6 % | 12 % | 41 % | 62 % |
  | Tehran / Brasilia | ~1180 | 110-198 | ~25 % | ~31 % | 49 % | 67 % |
  | Madrid | 657 | 77-165 | 86 % | 92 % | 83 % | 89 % |

  The catalogue does not help: nearest catalogue iron from Addis Ababa 2,375 km, Nairobi 2,574 km.
- **Failure scenario:** a player who spawns at Addis Ababa on the 1:10 server has copper (panning, malachite), lignite,
  partial tin, but no iron ore anywhere on the Ethiopian, East African, Mexican, Iranian, Anatolian, Tibetan or Andean
  plateaus (thousands of km), under either ore choice. At 1:100 iron exists but 140-250 blocks down.
- **Fix options:** (a) in the ore maps' new rules version, read followsealevel deposits as depth below
  `min(top, sea level + k)` (keeps lowlands exactly, lifts highland iron into reach); (b) an Earth-owned near-surface
  iron source in ground-column worlds independent of GA: ironstone/gossan limonite in laterite and saprolite, bog iron
  in peat (needs an iron item or reuse of a vanilla nugget, a scope call for the author because of "no new blocks for a
  while"). Effort: (a) small-medium, (b) medium.

### F4 (Medium-High, Verified). Ore maps on regional or vanilla rocks silently give new worlds rules v3 with heat maps v1

- **Where:** `src/WorldParamsBuilder.cs:98,224`; `src/EarthOres.cs:113-114`.
- **Logic:** 0065 §7 says new "Earth-like ores" worlds use the catalogue rules. The tab lets a player pick ores "maps"
  with rocks "regional" or "vanilla"; such a world gets the v3 generation 0065 replaced for its faults (background raised
  up to 12x where the US/Europe/Australia data are dense, coal measures and iron formations from the 0.5° smear, e.g. the
  Ruhr's 35,000 km² of measures; fill-any-rock in districts).
- **Fix:** either refuse/grey the combination, or give non-column worlds a catalogue rules version without the layer
  gate (bodies in any rock). Effort: small for the UI rule, medium for a column-free v4.

### F5 (Medium, Verified by math). "Real volumes" hold only for the catalogue, which is ~0.03 % of world ore

- Catalogue copper ore 395 Gt over 149 M km² of land = 0.98 mm of ore on average = **0.12 blocks per chunk** at 1:100
  and 1:200, 0.10 at 1:10 (`catalogue.txt`). Background copper at 1:100: 378 bpc (host-unlimited; perhaps 100-200 with
  hosts), i.e. **~1,000-3,000x the known endowment**; at 1:10/1024 ~10,000x (F1, F2). Iron 0.05 vs 179 bpc, tin 0.012 vs
  31, gold 0.014 bpc. Coal basins (13.9 Tt) average 8 bpc against 424 background.
- So the ore-maps world is "vanilla x 0.35-0.69 in host rock (`scaling.txt`: copper/tin 0.68-0.69, iron/coal 0.61,
  standard 0.34-0.35 of vanilla volume) plus hotspots". This is the right gameplay compromise (a real-volume world would
  have one copper block per eight chunks) and follows the author's floor principle, but 0065 and the README should say
  it plainly instead of "quantity reflects real-world volumes".

### F6 (Medium, Verified). At 1:10 the catalogue is out of reach and invisible

- Nearest catalogue deposit from the 36 spawn cities (`catalogue.txt`): copper median 363 km = 4,187 blocks at 1:100,
  **41,875 at 1:10**; tin 964 km (10.6k / 106k); iron 511 km (5.7k / 57k); gold 363 km; lead/zinc 318 km; bauxite 1,189
  km; salt bodies 2,045 km (salt basins 496 km); borax 2,516 km; sulfur 3,818 km. Catalogue bodies place no surface
  indicators (runtime notes, Limits), and the prospecting pick reads only the ground around where it is used.
- Consequence: on the 1:10 server progression is entirely the background; the catalogue serves late-game expeditions
  and trade, as the author wanted for transport (0006), but nothing tells players where they are.
- Fix: loose-ore markers over outcropping bodies (top < ~2 blocks) and a map/handbook hint. Effort: medium.

### F7 (Medium, Suspected). Borax, halite and sulfur catalogue lenses/stocks may place nothing

- **Where:** `src/Core/OreDeposits.cs:634` sets the basin rule only for domes; lenses/stocks of evaporite codes fall to
  the 0060 table: borax/kernite need an evaporite bed **of Cenozoic age** (`ore-layers-v4.json:74`), halite an
  evaporite bed (`:73`). The runtime notes claim bodies inside a basin get the relaxation
  (`docs/research/ore-deposits-2026-11-08/runtime.md:122-124`); the code gives it to areas and domes only (doc/code
  mismatch: Verified).
- **Bodies at risk:** 7 borax lenses (Boron/Kramer, Furnace Creek, Kirka, Bigadic, Emet, Kestelek, Tincalayu, Loma
  Blanca), Wieliczka, Bochnia, Turda, Praid, 3 gypsum lenses, 3 sulfur lenses. The 0060 probe kept 0 % kernite and 8 %
  borax at its sites, a hint that Cenozoic evaporite beds are rare in the columns.
- **Test:** an offline census like `OreLayersTests`: for every catalogue body, planned blocks vs blocks whose model
  column allows them; any body under ~20 % is a finding. Fix: `layers` on those bodies (`["bed","deep-cover","playa"]`)
  or the basin rule for every `IsBasinCode` body. Effort: small (data) plus the census test (medium, reusable).

### F8 (Medium, Verified). Coal seams and thin ore beds are white-noise speckle

- **Where:** `src/Core/OreDeposits.cs:1112-1120` (`count = floor(expected) + (Roll(x,k,z) < frac)` per column), and
  stocks/lenses `:1020-1021` (per-block roll against the fill).
- **Size:** blocks per column of the catalogue's beds at H 384: bituminous median 1.41 (min 0.18), anthracite 1.05,
  malachite (Copperbelt) 0.80, Kupferschiefer chalcopyrite down to 0.02, silver 0.01, Witwatersrand gold 0.01-0.05;
  Ruhr concealed coal (30 m at 600 m, v = 21 m) 1.4. A 0.2-block seam is a layer where 80 % of columns are empty,
  independently per column.
- **Fix:** round with a spatially coherent field (value noise at a few blocks, threshold at `frac`) so a thin seam is
  continuous one-block sheets with gaps, and cluster per-block fill and grade the same way (the runtime notes already
  list "rich blocks not clustered"). Same expected volume, new rules version. Effort: small.

### F9 (Medium, Verified). Surface starters are scaled like bulk ore

- `src/EarthOreGenerator.cs:118` excludes gem pockets, soil, layer deposits and children from `RadiusScale`, but not the
  Surface class (surface copper, surface cassiterite). At 1:10 surface copper is radius ~42 blocks, 3,556 blocks per
  patch, ~700 blocks apart (320 with F1's rounding); at 1:5 ~1,000 blocks apart. They exist to put nuggets near every
  start; panning (15 % copper, 11 % flint per sand/gravel pan, vanilla `pan.json`) keeps copper reachable, so this is
  medium, not high.
- Fix: exclude `OreClass.Surface` from the radius scale (keep vanilla size and spacing at every scale). XS.

### F10 (Low-Medium, Verified). Background grade still follows the survey-biased heat

- `src/EarthOreGenerator.cs:367` and `:578` pass the raw heat to `GradeQuantile` (`RegionalOreCatalog.cs:662`), so
  background ore of a four-grade host is 61 % poor where the heat is 0 (most of Africa, Siberia, Amazon) and 18 % poor at heat 1 (well-mapped
  US/Europe/Australia). 0065 §3 keeps this on purpose, but it re-imports the survey bias the author rejected for amounts
  (0065 §5). Fix: grade from `Evidence(...)` (0 for catalogued resources) or a fixed prior. XS.

### F11 (Low, Verified). Bulk deposits at 1:10 and 1:5 are identical; thickness never scales

- The 64-block cap binds for almost every variant at 1:10 (`scaling.txt`: mean radius 55-62), so 1:5 equals 1:10 in
  blocks, and per real km² 1:10 has 3.6x the background ore of 1:100 even before F1/F2. Thickness is not scaled
  (deliberate, runtime notes), so 1:10 deposits are 110-128-block discs 1-3 blocks thick. Within vanilla's own limits
  (radius 64, skew to 80, chunk range 3 = 96+ blocks), never sub-block (smallest: surface cassiterite at 1:200, radius
  1.5, 5 blocks). Document; no fix needed.

### F12 (Low, Verified). Catalogue host fallback is alphabetical, not "most similar"

- `src/EarthOreDeposits.cs:97-98` sorts a code's host rocks by name before `SimilarHosts`; `OreRules.SimilarHost`
  (`RegionalOreCatalog.cs:396-403`) returns `hosts[0]` when the rock's similar list has no host, so a limestone or gypsum
  bed crossed by a chalcopyrite body gets `ore-*-chalcopyrite-andesite`. Rocks missing from `SimilarRocks` (gypsum,
  kimberlite neighbours aside) always take the alphabetically first host. Visual only. Fix: carbonate and evaporite
  rows in `SimilarRocks` or a "matrix" fallback by rock family. XS.

### F13 (Low, Verified). Vein lean uses the curve's top scale at every depth

- `src/Core/OreDeposits.cs:680`: `lean = (d+.5) * Curve.Top / min(Hx,Hz) / tan(dip)`. On log curves v grows with depth
  (8.4 m at the top, ~29 m at 1 km), so deep vein sheets lean less than their real dip implies. Volume unaffected
  (`Envelope` uses the same `OnSheet`). Fix: `Curve.Metres(d+.5)`. XS, new rules version.

### F14 (Low, Verified). Latent: compact blankets ignore their layer search

- `src/Core/OreDeposits.cs:768-776, 953-984`: a compact cluster sits at `floor(Blocks(mid))` and a blanket skips d < 1,
  with no `Snap` to its layer. With today's data no blanket is sub-block (`catalogue.txt`), and a blanket goes compact
  only if its fill stays above 1 after 1.9x growth (deposit rate above ~3.5, not offered: max 300 %). Keep in mind
  when blankets get smaller. Also a one-block off-by-one between `Window` (`:784-788`) and the blanket `Snap` window
  (`:996`).

### F15 (Low, Suspected). v5 search on a missing tile leaves a body out of that chunk for good

- `src/Core/OreDeposits.cs:777` returns "unplanned" when `FineElevation.MissedReads` changed; `BodiesNear` then skips the
  body in the current chunk (`:577`), and later chunks place it: a cut body at a chunk border. The deposit pass waits for
  tiles, so this needs a failed download (offline server, Copernicus outage). Test: force a missed read in
  `OreDepositsTests`.

### F16 (Low, Verified). Documentation drifts

- 0065 §3 example bands (Cu 0.6/1.5/4 %) differ from the shipped `base-metals.json` (0.4/1.2/3.0 %).
- Runtime notes' basin rule for bodies (F7).
- 0060 calls surface starters "kept in the rock under the cover"; in practice they are put back wherever cover is
  thicker than their 0.5-8-block window (followsurface yPosRel 0.965 ± 0.03).
- Ni laterite bodies use pentlandite (a sulfide) as the Ni proxy and the rock's ore block in laterite; heavy-mineral
  sands place `ore-ilmenite-{rock below}` in beach sand. Known gameplay proxies; say so in the handbook.

---

## 3. Layers, depth and shapes

### 3.1 Shapes and fit to ore types

| Shape (code) | How | Catalogue use | Fit |
|---|---|---|---|
| stock | upright elliptic cylinder topM-baseM, random per-block fill (`OreDeposits.cs:663-665, 1013-1029`) | Cu 539 (porphyry), Mo 22, diamond 25 (kimberlite), W 10, Au 25 | Porphyry = large low-grade cylinder with a malachite top zone: good. Kimberlite = pipe: good (Kimberley 57 blocks at 1:100). The fill is white noise (a stockwork look) with per-block lognormal grade, no grade shells. |
| lens | dipping ellipsoid, true thickness from volume, hung below topM (`:605-618, 650-662`) | Cu 700 + Zn 446 (VMS/SEDEX), Cr 876 (podiform), Fe 96, U 19 | VMS/podiform = lens: good. 993 lenses are sub-block at 1:100 (mostly chromite, median 0.0 blocks) and placed whole with p = target. |
| veins | parallel 1-block sheets every n <= 12 blocks across strike, dipping (`:629, 676-684`) | Au 79, Ag 23, Sn 21, Hg/Pb/Sb/W | Lode gold/tin = narrow steep veins: good. Sheet spacing is in blocks (12 blocks = 2.4 km at 1:200, 120 m at 1:10). Lean error F13. |
| blanket | flat sheet snapped to the first allowed cover layer, never the top block; v5 moves it up to 30 km to a valley (`:986-1012, 836-867`) | Ni laterite 121, bauxite 41, placers Au 21 / Sn 16 / Pt 5, diamond gravels 13, Ti sands 15, playa borax 4 | Laterite/placers/salt flats = blankets in cover: good. Placers depend on Earth's alluvium (Iss and Tura 111 of 1,150). |
| dome | vertical cylinder, basin rule for evaporites (`:634`) | NaCl 35, S 5 | Salt domes with sulfur cap: good. |
| areas (beds) | polygon x ~3 km patches at a cover share; bed at real depth, moved to the nearest allowed layer within half its depth (`:1056-1173`) | coal 119, salt/potash 54, phosphorite 26, Cu (Copperbelt, Kupferschiefer) 94, BIF Fe 11, gypsum 14, Witwatersrand 7 | Coal = continuous seams: geometry right, rendering speckled below 1 block per column (F8). Beds follow the local surface (2.5D), so a seam sits at the same depth under a hill and a valley. Evaporites = thick beds: good (halite median 16.6 blocks per column). |

Missing on purpose: no separate pipe, stockwork or skarn shape (stock covers them). Placers are cover blankets, not
valley-floor channels; the v5 search finds the nearest alluvium.

### 3.2 Depth, floor, borders, determinism

- **Depth curve** (`GroundColumns.cs:451-503`, re-implemented in `catalogue.py`): log4000 fitted to 4,000 m at y = 1
  under a lowland. Top v: 8.41 m (H 384 log1000: 1:200, 1:100, 1:50), 9.70 m (H 1024 linear: 1:10), 22.1 m (H 512
  linear: 1:25), 15.9 m (H 256). 100 m = 10.6 / 9.4 / 4.4 / 6.1 blocks; 1,000 m = 60 / 57 / 39 / 47 blocks. Catalogue
  top/bottom go through it per column relative to that column's surface (terrain-following).
- **Rock floor:** catalogue `col.Floor` cuts (`OreDeposits.cs:974, 1002, 1016, 1155`; `EarthOreDeposits.cs:320`);
  background discs cut by `RockFloorPass` after the deposit pass. The cut is per chunk (lowest top - 160), so ridges
  keep up to 160 + relief blocks of ore.
- **Borders and determinism:** catalogue bodies and beds are pure functions of the body and the block, indexed in
  512-block cells with room for growth and the v5 move (`OreDeposits.cs:523-551`): seamless. Background discs: plan
  cached per (seed, variant, centre), acceptance read at the centre's chunk (`EarthOreGenerator.cs:317-379`): seamless.
  Layer gating reads each chunk's own table, rebuilt identically from the model when missing. Exceptions: F15; and the
  catalogue reads the chunk's actual blocks (`EarthOreDeposits.cs:259`), so a vanilla/background ore block already there
  wins (no overwrite in bed/basement), which is deterministic.
- **Host fallback:** catalogue bodies fill any plain rock in an allowed layer with the most similar host's ore
  (`EarthOreDeposits.cs:289-293`); loose cover takes the ore block of the first host/plain rock below (up to 96 blocks,
  `:264-274`); frozen loose blocks count as their thawed block in v5 (`:262`). The background (layer worlds) never
  fills non-host rock (`OreBinder.cs:208`). Fallback order is alphabetical (F12).
- **Grade bands:** catalogue blocks draw a lognormal (sigma 0.5) around the deposit grade or a co-product's own grade
  (`OreDeposits.cs:932-940`); a band is looked up without a unit check for co-products (`:635`, `BandFor(o.Metal, null)`),
  so a placer's Sn in % meets the hard-rock band (all placers "poor"; their blocks are ungraded anyway). Background
  grade from the heat (F10).

---

## 4. Amounts and scaling

### 4.1 Check of 0065 §2 (bodies)

`Target = FillReal x EnvelopeQuadrature x Rate` with `EnvelopeQuadrature = sum(Blocks(s)-Blocks(t)) x cell² /
(Hx Hz)` (`OreDeposits.cs:631, 687-698`) = V / (Hx Hz v_eff), v_eff the harmonic mean of the curve over the body's
depth: **matches** 0065 §2 (with Hx the latitude's own east-west metres, `:362`). Placement spreads `Fill = Target /
envelope` over the envelope's blocks, exact count up to 40,000 columns. Blocks per chunk over a deposit are scale-free in
h, as claimed, but **not in v**: the 1:25 play style (v 22 m) has 2.6x fewer catalogue blocks per chunk than 1:100 while
the background is unchanged, so the catalogue weighs 2.6x less there.

### 4.2 Check of 0065 §5 (background)

Radius x 100/h clamped so the mean stays in [0.5, 64] blocks (`OreDeposits.cs:1231-1239`), thickness not scaled,
attempts x baseVolume/vol (`EarthOreGenerator.cs:115-142`): **expected bpc equal to 1:100 by construction**, but
broken in practice by F1 (rounding) and F2 (world height). No 1/v term: per chunk, not per real volume.

### 4.3 Per scale (main variant; H 384 at 1:200/1:100, H 1024 at 1:10/1:5; with vanilla's rounding)

| | 1:200 | 1:100 | 1:10 | 1:5 | vanilla H384 |
|---|---|---|---|---|---|
| native copper: radius / blocks per deposit / spacing | 6 / 77 / 20 blk (3.9 km) | 11 / 306 / 39 blk (3.9 km) | 57 / 7,657 / 115 blk (1.2 km) | 57 / 7,653 / 115 blk (0.6 km) | 7 / 113 / 20 blk |
| surface copper | 2 / 11 / 39 blk | 4 / 36 / 71 blk | 42 / 3,556 / 320 blk | 60 / 7,101 / 320 blk | 4 / 36 / 72 blk |
| cassiterite | 4 / 31 / 37 blk | 8 / 122 / 73 blk | 59 / 6,449 / 262 blk | same | 5 / 45 / 37 blk |
| limonite | 21 / 832 / 112 blk | 42 / 3,330 / 209 blk | 58 / 6,256 / 180 blk | same | 26 / 1,231 / 110 blk |
| bituminous coal | 8 / 261 / 39 blk | 16 / 1,049 / 77 blk | 60 / 14,250 / 164 blk | same | 10 / 385 / 37 blk |
| galena | 3 / 22 / 22 blk | 6 / 83 / 43 blk | 55 / 5,865 / 202 blk | same | 4 / 32 / 16 blk |
| sulfur | 6 / 75 / 35 blk | 13 / 303 / 70 blk | 62 / 7,036 / 193 blk | same | 8 / 112 / 25 blk |
| borax | 6 / 76 / 56 blk | 13 / 303 / 109 blk | 62 / 7,036 / 263 blk | same | 8 / 111 / 40 blk |

Gold and silver are children (per parent block, unscaled): their bpc follows quartz and galena (gold in quartz 0.35x
vanilla, lower where silica heat raises quartz). Saltpeter is limited by cave air. Bauxite substitutes: Earth's
bauxite, kaolin and aluminosilicate deposits are layer deposits and never scale (native size and attempts at every
scale); corundum (ore maps only) is hit hardest by F1 (153x at 1:10).

### 4.4 Catalogue spacing and totals

| | deposits | mean spacing sqrt(land/N) | at 1:100 | at 1:10 |
|---|---|---|---|---|
| copper | 1,244 (+94 areas) | 346 km | 3.5k blk | 34.5k blk |
| tin | 66 | 1,503 km | 15k | 150k |
| iron | 116 (+11) | 1,133 km | 11k | 113k |
| gold | 961 bodies with gold | 394 km | 3.9k | 39k |
| lead/zinc | 499 | 546 km | 5.4k | 54k |
| sulfur | 8 (+3) | 4,316 km | 43k | 430k |
| borax | 12 | 3,524 km | 35k | 352k |
| bauxite | 43 | 1,861 km | 19k | 186k |
| salt bodies | 39 (+54 basins) | 1,955 km | 20k | 195k |

Totals in planned blocks (`catalogue.txt`): copper 1.3 M at 1:100, 123 M at 1:10; iron 0.5 M / 47 M; salt 1.7 M /
173 M; coal basins ~7.9 G at 1:100 (runtime notes).

### 4.5 Is "the same blocks per chunk at every scale" right for 1:10?

Total ore within a walking radius stays the same; deposits get ~5-10x larger and 3-6x farther apart in blocks
(bounded by the 64 cap). For bulk ores that suits a shared server (big shared mines, transport, 0006's wish), and the
background spacings at 1:10 (115-320 blocks) stay far inside the 10-20k-block limit. Three things do not fit: the
starter features (F9) should stay at vanilla spacing; the rounding and height factors (F1, F2) make 1:10 3-8x richer
than intended; and the catalogue is unreachable at 1:10 (F6), which is fine for a trade-and-expedition layer if it is
signposted.

---

## 5. Progression check

Hosts include Earth's patch (`ground-rocks-deposits.json`: gneiss/schist/quartzite/dolostone added to many vanilla
deposits). Layers per `ore-layers-v4.json`. "Vanilla" = vanilla ores on ground columns (0060); "maps" = rules v5.
Under rocks "vanilla" (the default world) everything is vanilla's own logic and none of these gaps apply.

| Material | Sources (both choices unless marked) | Region type with none in reach |
|---|---|---|
| Native copper / malachite | native Cu in 15 rocks (all common silicate rocks incl. gneiss, schist, quartzite), malachite in limestone/dolostone/marbles, Earth chalcopyrite (granite, andesite, basalt, schist, gneiss; 15-80 blocks), azurite (limestone, sandstone, andesite; 5-35); panning 15 % | none (panning). Surface starters vanish under cover thicker than ~1-8 blocks (laterite/saprolite tropics, thick till, loess, alluvium) and at 1:10 highlands keep 9-24 % of native Cu above the floor |
| Tin | cassiterite in igneous/metamorphic rocks (2 variants; depth 25-60 % and 40-100 % of the surface height); placer tin in alluvium/outwash (ground-column worlds only, hosts `sand-sandstone`/`gravel-granite`, not frozen); panning 0.02 %. Bronze alternatives: bismuthinite (same hosts), black bronze (gold placers/quartz + galena silver) | thick sedimentary platforms away from rivers: Saharan and Arabian ergs, loess plateaus, carbonate platforms (Nullarbor, Yucatan), Great Plains interfluves; permafrost alluvium (Siberia, north Canada) has no background placers (v5 thaws only catalogue bodies) |
| Iron | limonite (basalt, shale, chert), hematite (granite, peridotite, limestone, phyllite, dolostone, schist), magnetite (andesite, claystone, chalk, conglomerate, slate, gneiss), all at absolute y 5-94; bog iron only with Geology Additions; maps: catalogue (116) | **highlands (F3)**: Ethiopian, East African, Mexican, Iranian, Anatolian, Tibetan, Andean plateaus; sandstone/quartzite-only and marble terrains; the Baltic bog-iron country has no bog iron without GA |
| Coal | lignite (Cenozoic/Mesozoic beds), bituminous (Devonian+ to Cenozoic), anthracite (Paleozoic/Mesozoic), sedimentary hosts incl. dolostone; maps: 119 basins | shields and crystalline belts (Fennoscandia, Canadian Shield, African/Brazilian/Australian cratons), early-Paleozoic platforms (Estonia), highlands (bituminous/anthracite). Low impact: charcoal 1,300 °C beats lignite 1,100 and bituminous/anthracite 1,200 °C (vanilla item JSON); coal matters for coke (1,340 °C) |
| Sulfur | sedimentary beds, y 33-99; maps: 8 bodies, 3 areas | shields, crystalline belts, highlands. Soft gate (blasting powder, acid) |
| Saltpeter | cave air at y <= 88 | highlands; columns without caves below sea level. Soft gate |
| Borax | Cenozoic evaporite beds only (0060 probe: borax 8 % kept, kernite 0 %); maps: 4 playa blankets + 7 lenses (F7) | almost everywhere outside Cenozoic evaporite basins; soft gate (tanning alternative to lime, white dye) |
| Fire clay / refractory | vanilla fire clay patches, underclay below coal, kaolin in saprolite/laterite, 0077 recipes from blue/red clay + grog or crushed quartz (crafting rules 1 in ground-rules-2 or maps worlds); tier 2/3 need bauxite (warm laterite/terra rossa), kyanite/sillimanite/andalusite (metamorphic belts, ore layers 4 only), or corundum (maps only) **plus olivine (peridotite only) and ilmenite (followsealevel)** | tier 1: none. Tiers 2-3: temperate sedimentary platforms far from metamorphic belts and ophiolites (Baltic, Russian platform, Paris basin, Great Plains) - Suspected, EA-0069 step 3 (reach measurement) is still open |
| Flint / chert | flint in chalk/limestone beds (5-40 % kept), loose flint on glacial covers and chert on karst (ore layers v3+), panning 11 % | none |
| Lime / limestone | rock beds, not ore | shields and crystalline belts (outside this audit; worth one reach check) |
| Silver | galena child (sedimentary hosts), quartz child; maps: catalogue | none of note |
| Gold | quartz child, placer gold, panning 0.4 % | none of note |
| Quartz | veins class ~vanilla rate in most rocks; panning 3 % | none |
| Lapis | limestone, dolostone, marbles | shields, crystalline belts (cosmetic) |
| Halite | salt domes/beds in "evaporite" beds (any non-Precambrian gypsum, claystone, shale, mudstone, dolostone, limestone; 46 % kept), salt flats in playas; maps: 39 bodies, 54 basins | shields, crystalline mountain belts, laterite over basement (Fennoscandia, Canadian Shield, Congo/Amazon cratons). Medium: salt for curing and cheese |

Judgement on 0060's kept shares: halite 46 % leaves salt common wherever sedimentary beds are (gap only on crystalline
terrain); borax 8 % / kernite 0 % make borates a regional rarity (acceptable as a soft gate, but under the ore maps the
0.128x floor times 8 % is about 1 % of vanilla, so borax is effectively catalogue-only, see F7); flint 5-40 % is fully
covered by the surface rules and panning.

Regional types asked for: **Baltic till plain** - all hard gates met (copper in Devonian sandstone, malachite in
dolostone, tin from basement at 38-75 blocks overlapping cassiterite's 29-69-block window and from outwash placers, iron
in dolostone/claystone, flint on till); no bog iron without GA. **Amazon laterite** - copper deep in rock (surface
starters lost in laterite), tin placers in alluvium, iron at lowland depth, bauxite and kaolin. **Shield** - copper,
tin, iron in gneiss/schist/granite; no coal, salt, sulfur, limestone. **Desert erg** - copper and iron in sandstone and
limestone, salt flats; no tin unless crystalline rock or wadis are near. **High plateau** - F3.

---

## 6. Blindspots ranked, fixes and tests

Rules versions are frozen per world (0002): every generation change below needs ore rules 6 for new worlds (and, for
the layer table, ore layers 5). The 1:10 server must be created after they land, or it keeps 2.1.4's behaviour forever.

| # | Blindspot | Impact | Fix | Effort |
|---|---|---|---|---|
| 1 | F1 vanilla 1 % rounding | High | rate floor K with seeded drop in `GetPlan`, same K in prospecting | ~1 day |
| 2 | F3 highland iron/coal/sulfur/saltpeter | High | followsealevel read relative to min(top, sea + k) in the ore maps; Earth-owned near-surface iron (ironstone/gossan, bog iron without GA) for both choices | 1-3 days; the new item is the author's call |
| 3 | F2 world height x attempts | High (1:10) | factor 384/H for bound variants | hours |
| 4 | F4 maps + regional/vanilla rocks = v3 | Medium-High | refuse the combination on the tab, or a column-free catalogue rules version | hours / days |
| 5 | F7 evaporite bodies off their layers | Medium | `layers` or basin rule; offline placeability census for all 3,551 bodies | 1 day incl. test |
| 6 | F6 catalogue invisible at 1:10 | Medium | outcrop markers, handbook/map hints | 1-2 days |
| 7 | F8 speckled thin seams | Medium | coherent rounding | hours |
| 8 | F9 starters scaled | Medium | exclude Surface class from radius scale | minutes |
| 9 | Refractory tiers 2-3 reach (olivine, ilmenite, aluminosilicates) | Medium (Suspected) | EA-0069 step 3 measurement, then substitutes | 1 day measurement |
| 10 | Tin in thick sedimentary interiors | Medium-Low | accept (real), or placer tin in any alluvium already covers rivers; check spawn reach | measurement first |
| 11 | F10 grade from heat | Low-Medium | grade from gated evidence | minutes |
| 12 | F5 doc framing | Low | rewrite 0065 "Limits" and README | hours |
| 13 | F11-F16 | Low | as listed | small each |

**Tests to add (offline, fast):**

1. *Vanilla loop replay:* simulate `GeneratePartial`'s attempt loop (`NextInt(100)` rounding, H/256) with
   `AttemptFactor` for every bound variant at 1:200/1:100/1:50/1:25/1:10/1:5 and H 256/384/512/1024; assert bpc within 5 %
   of the 1:100/384 calibration. Would have caught F1 and F2.
2. *Spawn reach census:* for the 36 named spawns plus a few thousand random land points, with the model's columns
   (as `OreLayersTests` does), list per progression material the nearest column (within, say, 2,000 blocks and 64
   blocks deep) whose host and layer allow it, per play style. Would have caught F3 and answers EA-0069 step 3.
3. *Catalogue placeability:* for each body/bed, planned target vs blocks the model column allows; fail under a
   threshold or list them. Catches F7 and data gaps like Iss and Tura.
4. *Seam continuity:* fraction of a bed's present patch with at least one block per column vs the expected share;
   guards F8 after the fix.
5. *Bench (before the server):* a 1:10/1024 ore-maps world, `/earth orelayers` and ore totals at Addis Ababa, Riga,
   Riyadh-like erg, Canadian Shield, Amazon; compare bpc with test 1's prediction.

---

## Appendix: method notes and assumptions

- Vanilla deposit parameters from `%APPDATA%\Vintagestory\assets\survival\worldgen\deposits\**` with Earth's
  host-rock patches; NatFloat distributions as in decompiled `NatFloat.cs:183-250`; disc volume by Earth's own
  expectation formula (`EarthOreGenerator.cs:134-141`); vanilla ore-map mean 0.0705 (0.0303 for scale 1.5 maps) as in
  `RegionalOreCatalog.cs:348-353`; texture means from a re-implemented `ChunkNoise`.
- Vanilla's own sparse ore map is 0 in most chunks, so vanilla rounds up far less often than Earth (whose texture is
  never 0); the vanilla columns in the tables use the unrounded mean.
- h: 200.4 / 100.2 / 10.02 / 5.01 m (1110.8 blocks per degree at 1:100). Land 149 M km². Spawn coordinates are the city
  centres; elevations approximate. Distances on the cylinder use blocks per degree on both axes.
- Catalogue targets: analytic for stock/veins/blanket/dome, approximate for lenses (vertical span of the ellipsoid);
  the runtime's quadrature differs by a few per cent.
