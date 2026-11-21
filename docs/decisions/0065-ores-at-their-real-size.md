# 0065: Ores at their real size (ore rules v4)

Status: **implemented, on `main`** (built 8 November 2026 on `claude/ore-rework`, merged into release/1.0 as `0c2d78f`
and `63d5615`, with tungsten and platinum as `853f85f`; in Earth 2.0.0). Ore rules v4
apply in new worlds whose Earth tab chooses Earth's ore maps with ground columns (`EarthParams.OreRules` 195); the tab's
default ores are vanilla's, so v4 is experimental for now. Decided by: Claude, with the author's delegation ("Make all
decisions on own for next 4h"); builds on [0006](0006-ore-balance.md) and [0060](0060-ores-in-their-layers.md)

## What the author asked (8 November 2026)

> "use sub sessions to independently work on revisiting and reworking real source ore placements and heat maps based on
> real data. Also quantity and quality or ore placements should reflect real world volumes (according to world scale).
> can use several session (if think is best) to work on several groups of ore heatmaps. Ore (resource) placement shall
> be in correct geo layer. Like coal etc. Make all decisions on own for next 4h. This branch if good will be merged into
> main"

and, a little later: "you can download any needed data".

## Where ores stand before this (release/1.0 at 2e31feb)

- The heat maps (`assets/earth/config/ore-heatmaps-v1.json`, 0.5° cells, observed, potential and deep planes per layer
  code) say how likely a resource is. They scale vanilla's attempts per chunk by a rate class (0006), so amounts are
  "times vanilla", not tonnes. Rules v3 adds ore bodies (coal measures, iron formations, rich districts) where the heat
  passes a ramp; their size is authored ("practically unlimited for one person"), not measured.
- 0060 keeps every deposit in its ground layer (bed, basement, cover classes, bed age).
- Known faults: a background floor gives any host rock about 23 % of vanilla's attempts, which puts coal and hematite
  across Latvia; the heat of one 0.5° cell smears a 2 km porphyry over 55 km; grades come from the heat, not the ore.

## Decision

### 1. Known deposits are placed where they are, as big as they are

A deposit catalogue (`assets/earth/config/ore-deposits-v1/<group>.json`, schema in
[tools/ore-data/README.md](../../tools/ore-data/README.md)) lists real deposits and basins with position, ore tonnage,
grade, depth to top and bottom, footprint, shape and age, each with its source. New worlds place every catalogue
deposit at its true place as an ore body of its true size at the world's scale. The heat maps no longer make ore bodies
(rules v3 coal measures, iron formations and rich districts are replaced by catalogue basins and deposits).

### 2. Real volume at the world's scale

- Real volume = ore tonnage / density (default density by shape and ore when the source gives none).
- Horizontal size is divided by the world's metres per block, `h = 111320 / BlocksPerDegreeZ` north-south (about 400 m
  at 1:400, 100 m at 1:100, 10 m at 1:10) and the latitude's own east-west metres per block (the cylinder stretches
  blocks away from the Equator).
- Depth and thickness go through the ground column's depth curve (`GroundColumns.DepthCurve`), the same one that puts
  real rock at its depth, so a seam 300 m down sits where Earth's column has its 300 m rock and its thickness in blocks
  is its real thickness over the curve's metres per block there.
- So a body's blocks = its real volume / (h² × v). Ore blocks per chunk over a deposit are then the same at every
  horizontal scale; at 1:10 deposits are larger in blocks and farther apart, at 1:400 smaller and closer.
- A deposit smaller than one block keeps its expected volume: it is placed whole with probability equal to its volume
  in blocks (no minimum size).
- The world's deposit rate setting still multiplies everything, as in 0006.

### 3. Quality from the real grade

Graded ores (poor, medium, rich, bountiful) take their tier from the deposit's real grade through per-resource grade
bands in the catalogue (for example copper as built: medium from 0.4 % Cu, rich from 1.2 %, bountiful from 3.0 %; this
text first said 0.6, 1.5 and 4 %, corrected 18 November 2026 from `base-metals.json`), with spread inside the body. Ungraded ores ignore the grade. Background deposits keep the heat-driven quality of 0006.

### 4. In their layer

Placement follows 0060: catalogue bodies fill only hosts in the layers their deposit may use. Where Earth's column has
no host rock of that deposit at that place and depth, the body uses the ore block of the most similar host rock (rules
v3's similar-host table) but only inside allowed layers: hard-rock bodies in bed or basement, coal and evaporites in
sedimentary beds of an allowed age (inside a catalogue basin any sedimentary bed when the column lacks the right age),
placers, laterites and bog iron only in their cover layers. Never in soil or loose cover otherwise. A body deeper than
the column's floor is cut at the floor.

A catalogue body may name its layers (`layers`); for catalogue bodies that replaces 0060's table of its ore codes.
Blankets default to alluvial, outwash, laterite and saprolite, so a Ni laterite's limonite and pentlandite sit in
laterite and saprolite, heavy-mineral sands in alluvium and beach or dune sand, and placers in alluvium and outwash,
although the same ores in hard rock stay in bed and basement. A blanket never replaces the top block (raised by the
iron, ferroalloys and aluminium session, 8 November).

### 5. The heat maps are the background

The heat maps are rebaked from real data per group (`ore-heatmaps-v2.json` for new worlds) and still drive the small
background deposits (vanilla-like discs) between the catalogue's deposits, with the 0006 rate classes:

- The background is 0006's configurable class floor in its host rock and 0060 layer, everywhere, whether or not any
  record is near. A missing record is not a missing ore, and the open data cover the United States, Europe and
  Australia far better than Africa, Siberia or the Amazon; a background that needed data would copy that bias into the
  world. The author, 8 November: "We have configurable background chance for resource with correct rock. Lack of
  finding does not mean lack of resource. Especially in less surveyed regions we make strong bias towards US and other
  places with good online data coverage." `Ores.BackgroundChance`, `BackgroundWeight` and the per-resource overrides
  in `earth.json` set the floor (a resource's `BackgroundChance` 0 takes its background away). A gate that took the
  background away where the heat was 0 was tried on the bench while merging and removed the same day at those words.
- The heat does not raise the background either: background deposits come at their 0006 class floor (about 0.13 times
  vanilla's attempts for the standard class, 0.23 to 0.25 for copper, tin, iron and coal). The known deposits under a
  hotspot are the catalogue's, at their real size; 0006's hotspot boost (up to 12 times the floor) on top counted them
  twice and spread each over its 0.5° cell, the fault named above, and favoured the well-surveyed countries. Quartz
  veins, clay, peat, bog iron and salt flats, which the catalogue never places, keep the boost. (Settled on the bench
  while merging, 8 November; see Built.)
- Background deposit size follows the world's scale like the catalogue: attempts per chunk scale with h² and each
  deposit's volume with 1 / (h² × v), relative to 1:100, where today's calibration is kept. Blocks per chunk stay as
  at 1:100.

### 6. Prospecting

The prospecting pick reads catalogue bodies exactly (blocks within its reach), plus the background as now.

### 7. Ore choices and saved worlds

- "Earth-like ores" in new worlds use rules v4 (`EarthParams.OreRules = 4`, a new saved field), the catalogue and
  `ore-heatmaps-v2.json`; since 13 November rules v5, the same with the blankets' valley search (Built). Worlds made before keep rules v3 with 0060 and `ore-heatmaps-v1.json`; existing chunks never
  change.
- "Vanilla ore generation" stays vanilla's counts on Earth's rocks with 0060's layers; no catalogue.

## How the work was split (8 November)

One runtime session and four data sessions, each on its own branch off `claude/ore-rework`, each writing only its own
files; Claude (the coordinating session) merges them, rebakes the heat maps and checks the whole.

| Session | Owns | Heat-map layer codes |
|---|---|---|
| Runtime | `src/` ore code, `EarthParams`, `tests/OreDepositsTests`, `docs/research/ore-deposits-2026-11-08/runtime.md` | none |
| Coal and evaporites | `tools/ore-data/coal-evaporites/`, `ore-deposits-v1/coal-evaporites.json` | C_coal, NaCl, K, gypsum, B, S, P, N, alum |
| Base metals | `tools/ore-data/base-metals/`, `ore-deposits-v1/base-metals.json` | Cu, Sn, Pb, Zn, Ag, Bi, Hg, W, Mo, Sb |
| Iron, ferroalloys and aluminium | `tools/ore-data/iron-ferro/`, `ore-deposits-v1/iron-ferro.json` | Fe, Mn, Cr, Ni, Ti, V, Pt, Al |
| Precious metals, gems and industrial minerals | `tools/ore-data/precious-gems/`, `ore-deposits-v1/precious-gems.json` | Au, diamond, corundum, emerald, olivine, garnet, lapis, topaz, tourmaline, beryl, spinel, C_graphite, F, SiO2, U |

Peat and clay stay with the ground columns. Placers go with their metal's group (gold placers: precious; tin placers:
base metals).

## Limits

- Pre-mining endowment (past production + reserves + resources) is used. Production before 1318 is small next to it
  and is not subtracted.
- Catalogues are only as complete as the open compilations; small deposits fall to the background.
- At 1:400 and 1:200 most single deposits are a few blocks; basins still give many blocks per chunk.

## Built

On `claude/ore-rework` (from release/1.0 `2e31feb`, with release/1.0 `63fe489` merged in), 8 November, under the
author's delegation. Each session worked on its own branch and wrote only its own files; Claude merged them.

| Part | Branch head | What |
|---|---|---|
| Contract | `120a342`, `50420af` | This decision, the catalogue schema ([tools/ore-data/README.md](../../tools/ore-data/README.md)), group evidence in the bake, body `layers` |
| Runtime | `claude/ore-runtime` `ff1cda2` | `OreDeposits` (catalogue, plan, placement), `EarthOreDeposits`, `EarthParams.OreRules` 195, the v4 gate (since removed) and scale, `/earth ores`, prospecting, [notes](../research/ore-deposits-2026-11-08/runtime.md) |
| Coal and evaporites | `claude/ore-coal-evaporites` `bde792f` | 76 deposits, 216 basins ([notes](../research/ore-deposits-2026-11-08/coal-evaporites.md)) |
| Base metals | `claude/ore-base-metals` `476a94d` | 1,933 deposits, 94 areas ([notes](../research/ore-deposits-2026-11-08/base-metals.md)) |
| Iron, ferroalloys, aluminium | `claude/ore-iron-ferro` `e5c739f` | 1,232 deposits, 20 areas ([notes](../research/ore-deposits-2026-11-08/iron-ferro.md)) |
| Precious metals and gems | `claude/ore-precious-gems` `bc97cd3` | 310 deposits, 7 Witwatersrand areas ([notes](../research/ore-deposits-2026-11-08/precious-gems.md)) |
| Heat maps v2 | `c3303ee` (settings `d402e3b`, `5082d71`, coordinates `2ec07aa`, bake `b1b4d35`) | `ore-heatmaps-v2.json` ([report](../research/ore-heatmaps-v2/)), the catalogue check and `ore-deposits-SOURCES.txt` |

**Catalogue.** 3,551 deposits and 337 basins from public-domain, CC BY, OGL and dl-de/by compilations and cited facts
(`assets/earth/config/ore-deposits-SOURCES.txt`). `tools/ore-data/check_catalogues.py`: 0 errors; 18 pairs of
same-named or very close entries across groups, all separate real deposits
([catalogue-check.json](../research/ore-deposits-2026-11-08/catalogue-check.json)). Pre-mining endowment in the
catalogue, for example: copper 408 Gt of ore with 2.1 Gt Cu; coal 13.9 Tt in 119 basins; iron 449 Gt of ore;
salt 1.0 Tt; gold 57 Gt of ore with 84 kt Au; chromite 15 Gt of ore.

**Heat maps v2** (dataset `earth-resource-evidence-2026-11-08-v4`, same 44 codes and format as v1, which stays for
older worlds): the v1 evidence model plus each group's `evidence.geojson` and source drops. MRDS is dropped for Ni,
Cr, Pt, Hg, W, Mo, Sb, Bi and S, where its US-heavy points are replaced by the groups' filtered evidence; tin and
tungsten leave the granite and aureole priors, so tin's potential falls from 10,604 to 2,254 cells (the real belts);
permissive tracts weigh 0.35; copper's hot cells fall from 7,174 to 4,541. Latvia has no coal, iron, tin or gold heat.
In rules v4 the heat no longer decides whether background ore is there (section 5); it raises only quartz veins, clay,
peat, bog iron and salt flats, and `/earth ores` and the map overlays show it.

**Settled on the bench while merging** (the coordinator, 8 November; `OreDepositsTests` cover each):

- The grade bands that several files repeat (Au, Cu, Fe, U, V, all identical) warned "defined twice"; only a differing
  copy is a problem now, and the catalogue check fails on one.
- The hotspot boost on top of the catalogue's bodies (section 5): on the first v4 bench run about 52,000 background
  copper blocks lay around Chuquicamata next to about 64,000 catalogue blocks, and 30,000 background coal blocks over
  the Ruhr basins next to 21,000.
- The first v4 runtime gated the background on the heat (none where the heat is 0, except copper, quartz veins and the
  soil classes), and the coordinator first widened that gate to placers, bauxite and the child ores. The author then
  said a missing record is not a missing ore (section 5), so the gate is gone: every background ore is at its floor in
  its host rock and layer everywhere, placers in alluvium and bauxite in laterite included.

**Checks.** `OreDepositsTests` (including all four real catalogues: 0 problems) and `OreLayersTests` pass. The runtime
session's bench (see its notes): v3 worlds generate identically with and without the catalogues; catalogue volumes
match the plan; the catalogue pass costs 0.1 to 0.6 ms per chunk column at 1:100 and 1.6 ms at 1:10.

The coordinator's bench after these fixes, with the gate removed (headless server, new 1:100 world, seed 386217,
Earth-like ores and the realistic ground columns, all four catalogues and heat maps v2; each place 3 by 3 chunks and
what the server loads around them, which differs a little from run to run; no errors). Background = the resource's
blocks minus the catalogue's there; "with the boost" is the first v4 run, before the fixes:

| Place | Catalogue blocks | Background | With the boost |
|---|---|---|---|
| Central Latvia (56.60 N 25.60 E) | none here | copper 12,235, gypsum 6,380, iron 5,545, tin 3,052, zinc 1,694, lead 1,285, gold 19; no coal (no bed of coal age) | (not measured apart) |
| Ruhr (51.50 N 7.20 E) | coal 21,437 (concealed basin 13,952, outcrop 7,485) | coal 4,642 | 29,718 |
| Chuquicamata | 66,453 (Chuquicamata 56,991 of 77,510 planned, Toki 6,760, Mansa Mina 2,702) | copper 12,543 | 52,187 |
| Kimberley | 698 diamond blocks (Big Hole 56, the pipe cluster 642) | no diamond | none |
| Johannesburg | Central Rand reef, 105 gold blocks about 1,000 m down (477 on the run before) | gold 91 | 1,449 |
| Central African Republic (6.50 N 21.00 E), few records | none here | copper 18,632, bauxite 5,516, antimony 2,883, uranium 1,451, tin 1,420, gold 131 | (not run) |
| Central Siberia (63.00 N 105.00 E), few records | Tunguska coal and East Siberian salt basins, none in these chunks | olivine 50,353, copper 9,361, iron 2,782, chromite 1,970, platinum (sperrylite) 290 | (not run) |

**Limits found.**

- Vintage Story 1.22.7 has no tungsten ore block: wolframite is in its ore list, with ore textures, ore chunks and a
  nugget, but no block variant is made and no deposit places it. Platinum is a vanilla metal (ingot, plate, block; ore
  textures and ore-chunk models) without an ore block, nugget or deposit; native platinum grains exist only with
  Geology Additions 1.4.8 (`nativeplatinum`, a hard-rock ore there). Until the follow-up below, wolframite was left out
  of 39 bodies or beds and the 10 tungsten-only deposits (Zhuxi, Mactung, Xihuashan, Pangushan, Yaogangxian, Dajishan,
  Mittersill, Los Santos, Kara, Boulder County) and the 5 platinum placers (Iss and Tura, Nizhny Tagil, Kondyor,
  Goodnews Bay, Chocó) were skipped. The author, 8 November: "we can make own if needed"; Earth's own were built and
  are kept as placeholders, since neither has a use in the game yet (Built, "Tungsten and platinum"). No tungsten metal or smelting
  is added: nothing before the 18th century reduced tungsten, and platinum keeps vanilla's 1,770 °C, beyond any
  furnace of 1318.
- The background's floor is 0006's gameplay calibration, not a measured volume: copper, tin, iron and coal at about a
  quarter of vanilla, the other ores at about an eighth, each only in its host rock and layer. The catalogue carries
  the real volumes of the known deposits; the floor stands for the ones nobody has recorded.
- A body's blocks only go where Earth's column has an allowed layer: the Sierra Nevada's Tertiary gravels found no
  alluvium on the bench, so they placed nothing there, and nor did the Iss and Tura, Nizhny Tagil and Kondyor platinum
  placers (colluvium or regolith over rock in those valleys at the ground data's 30″). Ore rules v5 (EA-0058, below)
  move such a blanket to its nearest valley.
- Gold placers are all graded "poor" (their real g/m³ grades sit below the hard-rock bands); lead and zinc are
  catalogued less fully than copper; U and garnet evidence is still mostly MRDS (US-heavy).
- New worlds get rules v4 only with "Earth-like ores" and the realistic ground columns. The ore option's default is
  still vanilla ore generation ("for now"); whether it changes is the author's decision.

### Tungsten and platinum

On `claude/ore-tungsten-platinum` (from release/1.0 `63d5615`), 8 November, after the author's "we can make own if
needed". It follows Earth's own-ore pattern (e96456e) and reuses vanilla's art; nothing is copied from the game's code.

**Placeholders** (the author, 8 November, first: "Why to add if no use for the blocks? We can park them and not
activate for now"; then: "ok we can keep them and will be placeholders with more info"). Neither has a use in the game
yet: the wolframite nugget does not smelt, and vanilla 1.22.7's platinum is a metal without a survival source (left
out of the handbook, like chromium and titanium) that melts at 1,770 °C, above the hottest vanilla fuel's 1,340 °C;
its ingots serve only for plates and decoration. In 1318 platinum had no use either; the goldsmiths of the Ecuador and
Colombia coast had sintered its grains with gold, but more than a thousand years earlier. So they are kept, and the
handbook says what they are and that nothing in this world uses them yet: wolframite's mineral and use texts (wolfram,
tungsten's 3,422 °C, first won in 1783), the native platinum nugget's own two sections (its heavy grains and where they
lie, the old sintering, the Spanish "platina"), and sperrylite's use text, which had promised platinum from "a very hot
furnace". The art is vanilla's (wolframite overlays, ore-chunk models and nugget; the placers look like plain gravel
and sand, as gold's and tin's do), except the native platinum nugget's 16×16 texture, generated here; neither the
texture nor the handbook text has been seen in a game client. The author approved merging this into release/1.0
(8 November: "Yes, merge it").

- **Wolframite** is Earth's own block, `earth:ore-{poor,medium,rich,bountiful}-wolframite-{rock}` in granite, gneiss,
  schist, phyllite, slate, limestone and dolostone (greisen, veins and skarn), mining tier 4, drawn with vanilla's
  wolframite overlays; its ore chunks use vanilla's ore-chunk models, and a hammer crushes them into vanilla's
  `nugget-wolframite` (3/4/5/7 by grade). The nugget has no smelting: in this world wolframite is the heavy mineral that
  spoils tin ore. Its deposit is in `earth-ores.json` under the code `wolframite`, so it places under both ore choices
  and the catalogue's 39 bodies and the 10 tungsten deposits bind to it. 0006's wolframite adapter in `EarthOres.cs`,
  which never placed (there was no block), is gone. Rate: 0.43 tries per chunk, about 75 blocks per chunk, a quarter
  of vanilla cassiterite's ore per chunk, from tungsten's mine output against tin's (about 80 kt against 300 kt a year,
  USGS Mineral Commodity Summaries), by `tools/earth_deposit_rates.py`.
- **Native platinum** is a placer, like gold's and tin's (0060): `earth:placer-{gravel,sand}-platinum`, the layer
  deposit `earth-placerplatinum` in `earth-layers.json` and the 0060 placers rule, resource Pt (so, like the other
  placers, only with the realistic ground columns). Breaking it drops
  `earth:nugget-nativeplatinum`, and the pan gives it with chromite, magnetite, a little gold and peridotite. The nugget
  melts at vanilla's 1,770 °C into `game:ingot-platinum`, beyond any furnace of 1318. The iron-ferro catalogue's five
  PGE placers name `earth-placerplatinum` (they named `nativeplatinum`, Geology Additions' hard-rock ore).
- **Providers.** Where another mod defines a deposit code Earth also ships (Geology Additions' `wolframite`, say), only
  that mod's blocks count as providers for the catalogue, as `DropDuplicateAuthoredDeposits` already gives that mod the
  attempts. Not benched with Geology Additions (not on this PC).

Checks: `OreDepositsTests` and `OreLayersTests` pass; the four real catalogues have 0 problems and
`check_catalogues.py` 0 errors. Headless 1:100 bench, as above (seed 386217, Earth-like ores, realistic ground columns,
no errors): 3,551 deposits and 337 basins with 0 skipped and 0 without an installable ore (15 before), 71 codes bound
(69), and the "no installed generator" line is empty (it named W).

| Place | Catalogue blocks expected | Placed | Ground |
|---|---|---|---|
| Panasqueira (40.155 N 7.755 W) | wolframite 175 | 172; all wolframite blocks there 2,284 against tin's 7,751 | rock |
| Hemerdon (50.416 N 4.01 W) | wolframite 1,589 | 1,281 | rock |
| Goodnews Bay (59.08 N 161.55 W) | platinum placer 115 | 116 | outwash 32 m |
| Chocó (5.10 N 76.65 W) | platinum placer 148 | 146 | alluvium 10 m |
| Iss and Tura (59.10 N 59.40 E) | platinum placer 1,150 | 0 | topsoil, colluvium 13 m, regolith |
| Nizhny Tagil (57.85 N 59.85 E) | platinum placer 555 | 0 | colluvium 11 m |
| Kondyor (57.60 N 134.65 E) | platinum placer 660 | 0 | colluvium 1 m over shale |

The three Russian placers place nothing because Earth's column has no alluvium in their valleys (Limits found).

### Ore rules v5: the blankets' valley search (EA-0058, 13 November)

A catalogue position is often a district's or a town's rather than the valley floor, and Earth's column has alluvium only
next to a river of the ground data or on Fluvisols. New worlds with the ore maps get ore rules v5
(`OreBackground.CurrentRules`; v4 worlds keep v4). Its changes:

- **Search.** A blanket whose layers are all cover layers (alluvial, outwash, peat, laterite, saprolite, playa) and whose
  footprint has none of them within its depth window moves to the nearest place that has one. The search looks at
  rings 200 m apart out to 30 km, clockwise from north, from the world's model only, so it repeats. A footprint with its
  layer anywhere stays where it is.
- **Size.** A moved blanket is sized by the blocks its layers can hold, column by column, at most its interval's depth
  per column. It grows in steps of at most 1.3 up to 4 times its axes. Where room is still short, each column takes more
  of the body, and it is not made into a cluster.
- **Frozen ground.** A frozen loose block (permafrost sand or gravel) takes a deposit as the block it froze from would.
  Kondyor's alluvium is permafrost sand.
- **Index.** Bodies are indexed with room for the move and the growth.

`/earth ores` says how far a body moved and how long the search took. The search takes 0.3–3.7 s, once per body on the
worldgen thread.

Bench (1:100, ore maps, rules v5, 13 November):

| Place | Expected | v4 | v5 |
|---|---|---|---|
| Iss and Tura | 1,150 | 0 | 111 (moved 20.6 km; only a few alluvial columns within reach, fill 45 after growing 4 times) |
| Nizhny Tagil | 555 | 0 | 556 (moved 13.4 km) |
| Kondyor | 660 | 0 | 689 (moved 5–10 km by world seed; frozen alluvium) |
| Goodnews Bay | 115 | 116 | 114 (not moved) |
| Chocó | 148 | 146 | 150 (not moved) |

Iss and Tura stays short because the ground data has little alluvium there. That is a gap in the ground data, not in
the ore code. `OreDepositsTests` checks the search with a made-up valley: it moves 8 km, places 371 of 393 only in the
alluvium, and repeats. With no search (v4) it places nothing; a footprint that has its layer stays; with no layer in
reach it does not move.

### Audit of 18 November 2026

The [rocks, ground and ores audit](../research/rocks-ground-ores-audit-2026-11-18.md) found the scale formula of
section 2 right, and two things this decision does not say:

- **"Real volume" holds only for the catalogue.** The known deposits come to about 0.12 copper blocks a chunk on the
  world average against about 378 from the background at 1:100, so in practice the ore maps are vanilla × 0.35-0.69
  in host rock plus the known deposits at real size.
- **The background's amounts do not hold at fine scales and in tall worlds.** Vanilla rounds a fractional attempt up
  to a whole percent and multiplies attempts by world height / 256, so the 1:10 play style is 3-8 times richer than
  section 5 intends (EA-0169). Background quality still follows the heat (EA-0171).

A proposal for the next ore model takes both up: [2026-11-18-ores-next-model.md](../proposals/2026-11-18-ores-next-model.md).
Nothing in this decision changes until that is decided.
