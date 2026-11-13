# Ore rules v4 runtime (decision 0065), 8 November 2026

Branch `claude/ore-runtime` (from `claude/ore-rework` 120a342). What the runtime session built for
[0065](../../decisions/0065-ores-at-their-real-size.md), the choices it made where the decision left room, the test
results and what is still open. The catalogue schema is [tools/ore-data/README.md](../../../tools/ore-data/README.md).

## What was built

| Part | Where |
|---|---|
| Catalogue reader and checks, world scale, body plans by shape, area (basin) plans, spatial index, placement into any column, rules v4 background helpers | `src/Core/OreDeposits.cs` (no game types; the offline tests compile it) |
| The game side: catalogue loading from `assets/earth/config/ore-deposits-v1/*.json`, ore code to block binding, layer gate, placement after the 0060 layer pass, prospecting, `/earth ores` text, totals | `src/EarthOreDeposits.cs` |
| Rules v4 switches: saved `EarthParams.OreRules` (ProtoMember 195), heat maps v2, no rules v3 ore bodies, background gate and scaling, fingerprint | `src/EarthParams.cs`, `src/EarthMapSystem.cs`, `src/EarthWorldOptions.cs`, `src/EarthOres.cs`, `src/EarthOreGenerator.cs`, `src/EarthGeology.cs` (one event), `src/EarthOreLayers.cs` (the chunk's layer table kept until the catalogue pass is done) |
| Offline tests with a test catalogue covering every shape | `tests/OreDepositsTests/` |

### Saved field and which worlds use it

- `EarthParams.OreRules` (ProtoMember 195, unused before; checked against every `ProtoMember` in `src/`). New worlds
  save 4 when Earth's ore maps are chosen and ore layers (0060) are on, so with "Ores: Earth's ore maps" and "Rocks and
  ground: ground columns" (`EarthWorldOptions.Freeze`; the server-config path in `EarthMapSystem` likewise needs
  RegionalOres, RegionalGeology and GroundColumnsPrototype). Otherwise 0.
- Worlds saved before have 0 (protobuf default) and run exactly as before: heat maps v1, rules v3 coal measures,
  iron formations and rich districts, the background raised by the heat at its old sizes, no catalogue.
  `OreBackground.Evidence` returns the heat and `RadiusScale` 1 for rules below 4 (tested).
- Rules v4 read `ore-heatmaps-v2.json` when it exists and `ore-heatmaps-v1.json` otherwise (v2 is not baked yet on this
  branch). v4 worlds add `orerules=4|catalogue=<hash of all catalogue files>` to the ore fingerprint, so a changed
  catalogue shows as a changed fingerprint like a changed heat map.
- If a v4 world loads in a session where the ore layers are off, the catalogue is left out with a warning; an
  unexpected error while building the plan logs an error and leaves the background running.

### Catalogue loader

`OreDepositCatalog.AddFile` never throws. Each problem goes to a list that the server log shows (first 40 lines, then
a count): invalid JSON or another schema version skips the file; a deposit without positive `mt`, with an unknown
shape, a latitude/longitude out of range, no usable ore, or an id already used is skipped; an area with a geometry other
than Polygon/MultiPolygon, no beds, or crossing the antimeridian (a ring spanning more than 180° of longitude) is
skipped. Missing `density`, `topM`, `baseM`, `lenKm`/`widKm` and `dipDeg` take defaults by shape (and density by ore,
`DefaultDensity`), noted once per deposit. Ore shares are normalized to 1. `gradeBands` are read per file and looked up
by the deposit's layer and unit.

Ore codes bind to the game's deposit variants by code (`DepositVariant.Code`, including children and Earth's own
variants). A code with no generator in this game is listed in the log ("no generator in this game for ...") and its ore
is left out of the body (the rest of the body stays).

### Scale (0065 §2)

- `h_z = 111320 / BlocksPerDegreeZ` and `h_x(lat) = 111320 · cos(lat) / BlocksPerDegreeX` (Earth's cylinder stretches
  east-west away from the Equator, so a body's footprint in blocks is its real footprint over `h_x` and `h_z`
  separately; its blocks are real volume / (h_x · h_z · v)). The 1:100 play style has 1110.8 blocks per degree
  north-south, so h ≈ 100 m (0065's text says 80 m; the server's layout line says 100 m per block, and the runtime
  uses the world's own numbers).
- Depth and thickness go through the world's depth curve (`DepthCurve.For(GroundDepth, 1/vertical slope, sea level)`,
  the one the ground columns use), so v is the curve's metres per block at each depth. Bodies are planned in real
  metres and each column's real interval is turned into blocks through the curve; the planned target is the integral
  of 1/v over the body.
- Sub-block bodies: a body whose target is under one block (or which cannot hold its target in its envelope) is placed
  "compact": a ball-ordered cluster of `floor(target)` blocks plus one more with probability `frac(target)`, decided
  by the seed. No minimum size.
- The world's deposit rate multiplies every target (tested: rate 0.5 halves it).
- A column's floor (rock floor top) cuts the body; blocks below are not placed.

### Shapes

All shapes are ellipses in plan view along `strikeDeg` with semi-axes `lenKm/2` and `widKm/2`, seamless across chunks
(every column of a body is computed from the body alone, and chunk order does not matter).

- **stock**: a vertical ellipse cylinder from `topM` to `baseM`; the fill (share of the envelope that is ore) is set so
  the body holds its real volume.
- **lens**: an ellipsoid dipping at `dipDeg`: half-length `lenKm/2` along strike; down-dip extent from `topM`..`baseM`
  when the dip is 45° or more, else from `widKm` read as plan width (`widKm / cos(dip)`); true thickness from the
  volume. Centred at `(topM+baseM)/2` but deep enough that the dipping lens stays below the surface; each column takes
  the real interval where its vertical line crosses the ellipsoid.
- **veins**: parallel dipping sheets one block thick, every n-th block across strike (n up to 12, fewer when the
  body fills more of its envelope), over the footprint and depth range; the fill thins the sheets' blocks to the volume.
- **blanket**: a flat sheet from `topM` to `baseM` (placers, laterites, bauxite), snapped down to the first layer the
  body may use within twice its depth (at least 8 blocks), never the column's top block. Each of a column's blocks
  takes a slot of the sheet's depth profile, jittered inside it, so zones keep their order and a sheet one block thick
  keeps its shares (before the jitter such a sheet always took its middle ore).
- **dome**: a salt stock (vertical cylinder) from `topM` to `baseM`; coal/evaporite rules of a basin apply inside it.
- **areas** (basins, sediment-hosted beds): each bed (`code`, `topM`, `thickM`, `cover`) covers the polygon as a
  patchwork of about 3 km cells (value noise at a quantile so a share `cover` of the area has the bed). Each column of a
  patch places the bed at its real depth and thickness through the curve; where that depth holds no allowed layer the
  bed moves to the nearest allowed layer within half its depth (at least 8 blocks; down first). Bed blocks keep their
  expected volume, not rounded per column (a fractional block by chance).

Ore shares: each block of a body takes an ore by its share; ores with `"zone": "top"` take the top part of the body's
depth interval (their share of it), `"zone": "bottom"` the bottom part, the rest the middle. A body of one block takes
any of its ores by share.

### Grade (0065 §3)

Each graded ore block draws its grade from a lognormal around the deposit's grade (σ = 0.5 in ln grade) and takes the
tier of the band it falls in (poor below `medium`, then medium, rich, bountiful). A deposit with no grade or no band
for its layer and unit places medium. The tier picks the host's graded block where the variant has graded blocks
(sorted by the variant's own grade order); ungraded ores ignore it.

Co-products: when a file has a top-level `"oreMetal": {"<ore code>": "<band key>"}` (or `"Sb|Ag"`: the first the
body has a grade for) and a deposit has `"grades": {"<band key>": <grade>}`, that ore takes its tier from its own
metal's grade and band (a VMS body's sphalerite from its Zn, not from its Cu). Both fields are optional (asked for by
the base-metals session; not in the README schema yet). Area beds use their ore's metal band the same way, or the
area layer's band when the bed's `unit` (optional) matches it. Tested: a 2.1% Cu / 12% Zn lens places its sphalerite
0.0 / 21.9 / 52.1 / 26.0% poor / medium / rich / bountiful (expected 0.0 / 20.4 / 55.3 / 24.3).

### Layers (0060 and 0065 §4)

Placement runs after the 0060 layer pass (`EarthGeology.AfterDeposits` raises `AfterDepositLayers`), so every catalogue
block asks Earth's layer code at its own block (`EarthOreLayers.CodeAt`). The layer pass now leaves the chunk's layer
table in place (`Apply(..., release: false)`) and `EarthGeology` drops it after the catalogue pass (`Release`), so the
catalogue reads the same table the layer pass used instead of rebuilding it in every chunk it touches (the first
server runs with all four catalogues spent 20–30 ms per chunk column on that rebuild). A block is placed only where:

1. the block there is a host rock of the ore's variant, or a rock with a similar host (rules v3's similar-host table),
   whose ore block is then used; or loose ground (soil, sand, gravel, laterite, saprolite: block material soil, sand,
   gravel, stone or ore) in a cover layer (any class but bed and basement), which takes the ore block of the first host
   or similar rock below it in the column (up to 96 blocks down). VS has no ore blocks hosted in sand or gravel, so a
   placer or laterite block is the ore block of the rock under its sheet; and
2. the layer allows the body: a body's own `"layers"` (contract change 50420af: a list of 0060 classes, which for
   catalogue bodies replaces 0060's table of its codes; a blanket naming none takes alluvial, outwash, laterite and
   saprolite; a block where Earth has no layer code is allowed), otherwise the code's 0060 rule (`OreLayers.Allows`):
   hard-rock ores in bed or basement, coal and evaporites in sedimentary beds of an allowed age, placers, laterites and
   bog iron in their cover layers.
3. For bodies without their own layers, inside a catalogue basin (an area with a coal or evaporite bed) or a salt dome,
   coal and evaporite codes (`IsBasinCode`: lignite, bituminouscoal, anthracite, halite, sylvite, earth-gypsum, borax,
   kernite, sulfur) may take any sedimentary bed when the column lacks the right age.

Water at a placed block is cleared. Nothing goes into air or water, and a blanket never replaces the column's top
block. An unknown name in `"layers"` is logged as a problem and left out (the body keeps its other layers; with none
left a blanket takes the default and any other shape its codes' table).

### Ores without a provider (0065 §4, contract change 50420af)

Some codes exist only with another mod (Geology Additions: vanadinite, nativeplatinum, sperrylite, freibergite,
wulfenite and others). After reading the catalogue, Earth collects every deposit code the game's generators can place
(vanilla, other mods', Earth's own, children included). A code with none is left out of every body and bed: its share
goes to the body's other codes (shares renormalised; the body keeps its volume), one log line per code with the number
of bodies or beds it was in. A body or area with no installable code left is skipped and listed (first 40 lines, then
a count). This runs before planning, so `/earth ores`, prospecting and the totals see only what can be placed. The
summary line adds the count of bodies skipped this way and the codes not installed.

### Background (0065 §5)

- Rules v4 turn off rules v3 ore bodies (`BuildBeds` returns early: no coal measures, iron formations or rich
  districts).
- The background's attempt factor is the class floor everywhere (`OreBackground.Evidence`: the heat raises only quartz
  veins, clay, peat, bog iron and salt flats); host rock and 0060 layer decide where it lands. This runtime's first
  version gated it on the heat (no attempts at heat 0 except copper, quartz veins and the soil classes); the gate was
  removed on 8 November at the author's word that a missing record is not a missing ore (0065 §5). Rules v3 keep
  their factor.
- Rock-hosted, non-child, non-gem-pocket, non-soil, non-layer background deposits scale their radius by
  `100 m / h` (clamped between half a block and 64 blocks; exactly 1 within 2% of 100 m, so the 1:100 play style's
  100.2 m keeps today's numbers) and their attempts by the inverse of the volume change, so blocks per chunk stay as
  at 1:100 (today's calibration). Thickness in blocks is not scaled: the world's height and
  curve already carry the vertical scale, and vanilla's thickness is a few blocks at every scale.

### Prospecting (0065 §6) and `/earth ores`

- The propick reading adds the catalogue's expected blocks of the variant's code in the column (the same plan as
  placement, without placing) to the reading's ppt and total factor. Tested: expected chalcopyrite in a stock body
  5030 against 5061 placed.
- `/earth ores` (and the SelfTest's ore line) ends with the catalogue deposits and basins within about 50 km (256 to
  4096 blocks), up to 8, each with name, ores, shape, depth, footprint, tonnage, planned blocks and blocks placed so
  far this session.
- The ore totals line starts with the catalogue's blocks placed, chunk columns and milliseconds per column.

## Tests

`tests/OreDepositsTests` (run `dotnet run -c Release` in that folder; extra catalogue files as arguments are
smoke-tested). Test catalogue `test-catalogue.json`: one deposit of each shape, a coal lens outside a basin, a basin
with a bituminous and an anthracite bed, and broken entries. All checks pass (3 s):

- Loader: 6 deposits and 1 area read; 5 bad entries skipped (no tonnage, unknown shape, latitude 100, duplicate id,
  LineString area); a broken file and schema version 2 skipped; shares sum to 1; default density used.
- Volume, placed against real volume / (h_x h_z v) on a linear curve (v = 20 m) and against the planned target on the
  default log curve (log4000, 110 blocks):

  | Shape | 1:400 linear | 1:100 linear | 1:10 linear | 1:10 log |
  |---|---|---|---|---|
  | stock | +1.7% of 492 | −0.4% of 7 869 | +0.1% of 786 852 | +0.3% |
  | lens | +10% of 31 | +0.6% of 493 | +0.1% of 49 303 | −0.6% |
  | veins | −34% of 24 | −2.5% of 388 | +0.4% of 38 787 | +0.7% |
  | blanket | +19% of 14 | −3.1% of 228 | +0.4% of 22 812 | −0.1% |
  | dome | +0.7% of 5 030 | +0.7% of 80 480 | +0.0% of 8 047 953 | −0.4% |
  | coal lens | −13% of 18 | +2.7% of 295 | −0.3% of 29 490 | −0.1% |

  The planned target equals the real volume over the block volume within 0.6% everywhere; the 1:400 differences are
  the chance in bodies of a few dozen blocks (tolerance from their count).
- Basins: the plan's expected blocks are within 0.1% of the real volume at 1:400, 1:100 and 1:10; placed +1.6% (1:400,
  9 681 patch cells), +3.1% (1:100, 688), −6.7% (1:10, 28 patch cells; the share of patches in a small basin is a draw).
- Sub-block: 3 000 small vein deposits (all compact): expected 19.1 blocks at 1:400, placed 23; 1:100: 305.5 / 314
  (+2.8%); 1:10: −1.5%.
- Layers, on a model column (soil, alluvium, cover, sandstone bed to 1000 m, granite basement): nothing placed where the
  layer does not allow it; the placer blanket finds the alluvium; coal outside a basin stays out of a bed of the wrong
  age; basin coal takes it; the 600 m anthracite bed lies in the sandstone; the salt dome takes the beds it pierces
  (halite 6 252, sulfur 1 115 in the top zone); hard-rock ores fill bed and basement.
- Grade: a 0.88% Cu stock places 30.3% poor, 60.3% medium, 9.5% rich, 0.0% bountiful (expected from the lognormal
  30.3 / 60.3 / 9.4 / 0.05); a 2.1% Cu lens's chalcopyrite is mostly medium and rich, its sphalerite (12% Zn) mostly rich and bountiful; no grade gives medium; top-zone malachite keeps its
  15% share (15.6%); top ores above, bottom ores below.
- Determinism and seams: the same 317 vein blocks whatever the chunk order; another seed gives other blocks.
- A body's own layers and missing codes (inline catalogue): `layers` read as class bits; a stock naming none keeps its
  codes' table; the test blanket naming none takes the four default classes; an unknown name ("dunes") is one problem
  and left out. A laterite blanket (`["laterite", "saprolite"]`, limonite top 60%, pentlandite bottom 40%) on a column
  that is laterite from its top block down: only laterite and saprolite take blocks, none at the top block, limonite
  above pentlandite in every one of 1 326 columns holding both, limonite 59.7% of the blocks. A stock of chalcopyrite
  0.5 + nativeplatinum 0.5 without a nativeplatinum provider becomes chalcopyrite 1 with the same planned volume; a
  placer of nativeplatinum alone is skipped and reported.
- Older rules: gate and radius scale are 1 below v4; v4 gates iron, tin and coal without evidence and keeps copper,
  quartz veins and soil; radius ×1 at 1:100, ×10 at 1:10 (capped at 64 blocks), ×0.25 at 1:400.

### Smoke test with the group catalogues

`git show` of the group branches' first files at about 10:00 UTC (not committed here). Blocks are the planned
bodies plus the basins' expected blocks; planning time is the offline plan of every body plus the test's sum over the
areas' expected blocks, which only the test does.

| File | Deposits | Areas | Problems | 1:400 | 1:100 | 1:10 | Planning |
|---|---|---|---|---|---|---|---|
| base-metals.json (`claude/ore-base-metals` 1ce0a52) | 1 739 | 94 | 0 | 43 k + 1.3 k | 688 k + 21 k | 123 M + 3.6 M | 132 / 139 / 420 ms |
| iron-ferro.json (`claude/ore-iron-ferro` 0c0bb89) | 1 223 | 20 | 0 | 28 k + 26 k | 446 k + 421 k | 84 M + 88 M | 95 / 119 / 141 ms |
| coal-evaporites.json (`claude/ore-coal-evaporites` 14bcf78) | 77 | 216 | 0 | 107 k + 491 M | 1.7 M + 7.9 G | 174 M + 833 G | 68 / 252 ms / 2.3 s |
| precious-gems.json (`claude/ore-precious-gems` d285dcb) | 281 | 7 | 0 | 9.8 k + 0.7 k | 157 k + 11 k | 30 M + 1.1 M | 23 / 73 / 56 ms |

Every entry of the four files loads without a problem. The coal and evaporite basins hold most of the blocks (7.9
billion at 1:100, about half a block per land column on average): Tunguska and the North Slope alone are 2 000 000 Mt
of coal each, and the salt basins are larger still.

Again at about 10:25 UTC, with the group heads that name `layers` (every class name valid):

| File | Deposits | Areas | Problems | 1:400 | 1:100 | 1:10 | Planning |
|---|---|---|---|---|---|---|---|
| base-metals.json (95ae0c1; 16 blankets alluvial/outwash) | 1 933 | 94 | 0 | 51 k + 1.3 k | 820 k + 21 k | 149 M + 3.6 M | 160 / 199 / 474 ms |
| iron-ferro.json (e5c739f; 186 blankets, 2 lenses with layers) | 1 076 | 20 | 156 | 30 k + 27 k | 481 k + 428 k | 92 M + 90 M | 81 / 103 / 118 ms |
| coal-evaporites.json (bde792f; 5 playa, 2 bed blankets) | 76 | 216 | 0 | 125 k + 491 M | 2.0 M + 7.9 G | 197 M + 833 G | 64 / 240 ms / 2.4 s |
| precious-gems.json (bc97cd3; 40 placer blankets) | 310 | 7 | 0 | 9.8 k + 0.7 k | 156 k + 11 k | 31 M + 1.1 M | 30 / 85 / 86 ms |

iron-ferro's 156 problems are `usgs-podchrome-2012:*` entries without `mt` (skipped).

## Headless server (SelfTest)

VS 1.22.7 dedicated server, same mods as the player's Earth install (CylinderWorld 1.0.4, Hydrology, the Earth data
mods), seed 386217, "Rocks and ground: ground columns", "Ores: Earth's ore maps", built-in heights, 30″ ground sets
present, SelfTest radius 1 chunk at five places: Chuquicamata (porphyry stock), Kachkanar (magmatic stock), the
Zambian Copperbelt (sediment-hosted areas), Hamersley (iron formation area, Mount Tom Price) and Riga (no catalogue
deposit). "v3" is the base commit's DLL (the same world options give rules v3 there); "v4" is this branch with the
base-metals and iron-ferro catalogues copied into the mod's assets (not committed).

At 1:100 (play style "earth", 100.2 m per block north-south, depth curve 8.4 m per block at the top):

- The v4 log: `Earth ore deposits (0065): 2962 deposits and 114 basins from 2 file(s) (0 skipped) ... 20 codes bound;
  no generator in this game for nativeplatinum (their bodies are left out)`; rules v4 line with "deposit catalogue on;
  no rules v3 ore bodies"; background radius x1 at this scale. No errors in either run; prospecting adapters PASS (81
  readings per place) in both.
- `/earth ores` text at the places names the deposits, e.g. Chuquicamata 21 277 Mt at 0.592% Cu, stock 0–287 m,
  11.9 x 3 km: 77 510 blocks expected (fill 0.998), 53 882 placed in the chunks generated around the place;
  Kachkanar 9 000 Mt: 34 256 expected, 28 159 placed; Mount Tom Price 1 000 Mt hematite lens: 2 303 expected, 1 934
  placed; the Hamersley area's beds (cover 0.12% and 0.1% from the catalogue's tonnage) 1 252 blocks.
- Catalogue pass: 90 343 blocks in 574 chunk columns, 2.0 ms per chunk column (including each body's one-time plan;
  measured before the layer-table fix below, which brought the pass to 0.3–0.4 ms).
- Generation time per place (9 216 block columns each, wall clock, the same machine with other sessions' work running):

  | Place | v3 | v4 |
  |---|---|---|
  | Chuquicamata | 8.7 s | 9.0 s |
  | Kachkanar | 6.2 s | 6.1 s |
  | Copperbelt | 8.9 s | 7.8 s |
  | Hamersley | 8.2 s | 8.6 s |
  | Riga | 4.1 s | 4.7 s |
  | SelfTest start to done | 43 s | 44 s |

  The difference is within the run-to-run noise; the catalogue pass was about 2 ms of a chunk column's generation
  (0.3–0.4 ms after the fix).
- Resource totals over the five places (all blocks, background and catalogue): Fe 453 954 in v3 (rules v3 iron
  formations) against 71 589 in v4 (catalogue bodies and the gated background); coal 2 214 against 0 (no coal evidence
  at these places); Cu 421 019 against 344 250; Sn 33 858 against 17 107.

At 1:10 (play style "earth-10", 10 m per block, world height 1024, depth curve 9.7 m per block at the top):

- Background radius x10 of 1:100 with attempts to match (log line). No errors in either run.
- Chuquicamata 7 082 283 blocks expected (91 times 1:100, the h² ratio with the curve's slightly larger v), 1 356 484
  placed in the generated chunks; Kachkanar 3 252 343 expected, 2 476 848 placed; Mount Tom Price 213 171 expected.
- Catalogue pass: 3 908 055 blocks in 574 chunk columns, 8.5 ms per chunk column before the layer-table fix. Every
  test place sits on a large body, so this is the dense case; columns with no body or basin nearby only look up the
  index.
- Generation time per place, v3 / v4: Chuquicamata 8.2 / 9.5 s, Kachkanar 6.0 / 7.4 s, Copperbelt 9.6 / 7.2 s,
  Hamersley 7.8 / 8.4 s, Riga 4.8 / 5.1 s; SelfTest start to done 42 / 44 s.

With all four catalogues at 1:100 (3 320 deposits, 337 basins from 4 files, 0 skipped, 63 codes bound, only
`nativeplatinum` without a generator), at Gorleben (salt dome in the Zechstein), the Ruhr, the Powder River basin,
Kimberley, the Sierra Nevada placers and Chuquicamata, against the base DLL (v3) at the same places:

| Place | v3 | v4 before the table fix | v4 |
|---|---|---|---|
| Gorleben | 5.9 s | 10.1 s | 10.1 s |
| Ruhr | 7.4 s | 11.2 s | 11.7 s |
| Powder River | 10.1 s | 11.9 s | 10.7 s |
| Kimberley | 10.9 s | 12.0 s | 15.9 s |
| Sierra Nevada | 8.5 s | 8.1 s | 9.1 s |
| Chuquicamata | 14.0 s | 12.8 s | 12.0 s |
| SelfTest start to done | 71 s | 74 s | 79 s |
| Catalogue pass per chunk column | – | 2.9 to 33 ms | 0.3 to 0.4 ms |

- The per-place times swing by several seconds between runs of the same build (other sessions were working on the
  machine); the catalogue pass itself is now 0.3–0.4 ms of a chunk column (655 chunk columns, 605 639 blocks, the
  same blocks before and after the fix).
- Catalogue blocks: Gorleben-Rambow dome 178 820 expected, 169 325 placed; the Zechstein salt structures 220 465;
  Ruhr outcrop coal 14 471 and concealed coal 8 711; Powder River lignite 133 511; Kimberley 57 diamond blocks (54.3
  expected) and the pipe cluster 705 (755 expected).
- The Sierra Nevada Tertiary gravels (blanket, 28 024 blocks expected) placed none in these chunks: Earth's column
  there has no alluvium or cover layer for placer gold within the blanket's reach (0065 §4 keeps placers in those
  layers). The `/earth ores` "expected" count is the plan, not what the layers let in.
- Resource totals over these six places, v3 → v4: coal 1 037 351 → 197 795 blocks (rules v3 coal measures against
  the basins' real volume), salt (NaCl) 21 474 → 528 549, potash (K) 282 → 2 333, Fe 64 946 → 9 404, Sn 19 365 →
  10 561, Cu 322 000 → 331 831, Au 4 484 → 3 515, diamond 0 → 762.
- No errors; prospecting adapters PASS at every place.

Older worlds unchanged: the v3 world made by the base DLL was reopened twice, once by the base DLL and once by this
branch with all four catalogues installed, and generated the Ruhr and Gorleben anew. Both log rules v3 (coal measures,
iron formations and districts on, no catalogue line) and their ore placement totals are identical (every resource and
tier, same hash of the totals lines).

With all four catalogues at 1:10 and the fix: no errors; catalogue pass 7 750 917 blocks in 655 chunk columns, 1.6 ms
per chunk column (3.3 ms around Gorleben, where the dome alone placed 6.3 million blocks in 250 chunk columns);
Kimberley 4 179 diamond blocks placed of 5 221 expected; Powder River lignite 65 954 in the generated chunks; the three
by three chunks at Gorleben fell in a gap between the Zechstein structures' 3 km patches (35% cover). Places took 6.5
to 13.3 s each, SelfTest start to done 66 s (no v3 run at 1:10 with these places).

### Body layers and codes without a provider (about 10:30 UTC)

The same 1:100 world with this branch's DLL after contract change 50420af and the group heads that name `layers`
(base-metals 95ae0c1, coal-evaporites bde792f, iron-ferro e5c739f, precious-gems bc97cd3), at Sangaredi (bauxite
blanket, `["laterite"]`), the Kinta Valley (tin placers, `["alluvial", "outwash"]`), the Iss placers (nativeplatinum
only) and Chuquicamata. No errors; SelfTest 102 s.

- Load: 3 380 deposits and 337 basins, 156 entries skipped (iron-ferro's podiform chromite without `mt`), 15 bodies
  without an installable ore, 69 codes bound. Not installed in VS 1.22.7 without other mods: `nativeplatinum` (5
  bodies; all five platinum placers skipped and listed) and `wolframite` (39 bodies or beds; the 10 wolframite-only
  tungsten bodies skipped and listed, the rest keep their other ores). One log line per code.
- Placed: Sangaredi 5 565 earth-bauxite blocks in Earth's laterite (loose ground over host rock), the Kinta placers
  12 113 earth-placertin blocks in the alluvium, Chuquicamata 49 103 chalcopyrite. The catalogue pass took 0.09 to 0.61
  ms per chunk column.

## Decisions taken here

- **Loose ground takes the ore block of the rock below**: VS has no sand- or gravel-hosted ore blocks; a placer or
  laterite block in Earth's alluvium or laterite uses the ore block (graded where it is) of the first host or similar
  rock under it in the same column. This also lets bodies on the 0060 code table (no `layers`) place in their cover
  layers, which took nothing before (the Sierra Nevada gravels below).
- **A block without a layer code is allowed for a body naming layers**, as 0060 does for its table: where Earth's
  ground columns do not own the column the layers cannot be told.
- **Salt flats and bog iron must name their layer**: the blanket default (alluvial, outwash, laterite, saprolite) has
  no `playa` or `peat`; coal-evaporites' playa blankets already name `["playa"]`.

- **Reference scale 100 m**: today's background calibration is kept at h = 100 m (the 1:100 play style's real h).
- **Block volume h_x · h_z**: east-west size uses the latitude's own metres per block, so a body keeps its real volume
  at high latitudes, where the cylinder stretches blocks.
- **After the layer pass**: catalogue bodies are laid after 0060's layer pass, from the same chunk event, so they see
  the final rock and layer codes and are not overwritten by vanilla's deposits.
- **Basin relaxation also for salt domes**: domes pierce beds of every age, so evaporites in a dome get the basin rule.
- **Beds move to the nearest allowed layer within half their depth** (down first): Earth's column may put a basin's
  coal depth in cover or basement where the real basin has its beds; a bed is not dropped for a few blocks of
  difference, and nothing goes into a layer the ore may not use.
- **Background gate (removed)**: heat exactly 0 meant no attempts (copper, quartz veins and soil classes excepted);
  the coordinator narrowed it while merging, then removed it at the author's word (0065 §5): the background is the
  class floor in its host rock everywhere. In v4 the heat no longer boosts the background of what the catalogue
  places (`OreBackground.Evidence`).
- **Thickness not scaled** for the background (see above).
- **Zone shares by depth interval**: top/bottom ores take their share of each column's interval, so their share is in
  blocks, not tonnes (equal when the body's density is uniform).

## Limits and what is left

- No surface signs (no boulders or indicator blocks) for catalogue bodies yet; vanilla's surface ore indicators only
  come from the background.
- Codes without a provider are left out with their share given to the body's other codes (above; with the four group
  files and no other ore mod: `nativeplatinum` and `wolframite`). Prospecting reads only codes with bound ore-map variants.
- A body's blocks are only placed where Earth's own column has an allowed layer and a host, or loose ground over a
  host: placers and laterites need Earth's alluvium, laterite or cover there within twice the sheet's depth, hard-rock
  bodies need bed or basement. The planned ("expected") count in `/earth ores` does not know the layers. Bodies are not
  moved sideways to find their layer.
- `lenKm`/`widKm` of a lens: the README's plan-view footprint is used; base-metals gives the down-dip extent as
  `widKm`, which matters only for lenses dipping under 45°; the README should settle the convention.
- The optional `oreMetal`/`grades` fields (co-product tiers) and bed `unit` are read but not in the README schema.
- The grade speckle is per block (each block draws its own grade); rich blocks are not clustered.
- Small basins are a draw of about 3 km patches, so a basin of a few patches can place ±30% of its volume.
- Areas crossing the antimeridian are skipped; none of the group files has one so far.
- `ore-heatmaps-v2.json` was not baked on this branch, so v4 worlds here read v1 under the (since removed) v4 gate. (Since baked and
  merged on `claude/ore-rework`; the coordinator's bench with all four catalogues is in 0065's Built section.)
