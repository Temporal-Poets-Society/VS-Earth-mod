# Audit: regional rocks, ground columns and ores (18 November 2026)

The author asked (18 November 2026) for an audit of Earth's regional rocks, ground columns and ore heat maps: each
function's implementation, the data coverage, and the blindspots "where we could get terribly wrong". For ores, the
spawn logic as well (layers, depth, shapes, the overall amount, and how amounts scale with the world), and a bigger
plan for heat maps and spawning where data is thin. The plan is a separate proposal:
[2026-11-18-ores-next-model.md](../proposals/2026-11-18-ores-next-model.md).

- **Line audited:** `main` at `1ac0f2d` (= v2.1.4). No open branch changes any of these files (checked 18 November).
- **Nothing was changed** in the code, data or any world. The fixes are filed as backlog items (table below).
- **Method:** five read-only reviews run in parallel, each with its own measuring scripts, then checked and combined by
  the coordinating session. The full reports are in [rocks-ground-ores-audit-2026-11-18/](rocks-ground-ores-audit-2026-11-18/):
  [audit-rocks.md](rocks-ground-ores-audit-2026-11-18/audit-rocks.md) (R1-R16),
  [audit-ground.md](rocks-ground-ores-audit-2026-11-18/audit-ground.md) (F1-F15),
  [audit-orespawn.md](rocks-ground-ores-audit-2026-11-18/audit-orespawn.md) (F1-F16),
  [audit-oredata.md](rocks-ground-ores-audit-2026-11-18/audit-oredata.md) and
  [research-vanilla-and-nodata.md](rocks-ground-ores-audit-2026-11-18/research-vanilla-and-nodata.md) (vanilla's ore
  generation, Mindat and other data, deposit-density methods). The scripts behind their numbers are in `scripts/`;
  they were run from a scratch folder, so paths in them need adjusting.
- **Limits:** no game or bench runs. The numbers come from Python or C# ports of Earth's and vanilla's code paths
  (the ground probe compiles `src/Core` itself), from the shipped data, and from the data store. Real-world shares
  of reserves and endowment are approximate figures from memory, marked so in the reports.

## The short answer

| Part | Code | Data | Verdict |
|---|---|---|---|
| Regional rocks | Sound: save safety, strata path, hand-off guards, caches | 0.5° GLiM/GUM; every bed below the surface class, every thickness and every basement is made up | Wrong at scale: bed thickness tied to a sea level that no longer follows world height; mantle peridotite under every flood basalt; no ophiolite or ultramafic rock anywhere at the surface; granite districts (Cornwall) lost in the 0.5° grid |
| Ground columns | Mostly sound | 30″ SoilGrids and depth to bedrock on 95-99 % of land; cover type from a 0.5° map or a soil-group proxy | Real gaps in gameplay materials: clay only where a recipe has it (vanilla clay is off), no alluvium in glaciated valleys, no placers in frozen alluvium, peat only on Histosols |
| Ore spawn (ore maps) | Shapes and determinism sound; scale formula right | — | Amounts wrong at fine scales and tall worlds (vanilla's 1 % rounding, height multiplier: 1:10 is 3-8× richer than intended); highland spawns have no iron or coal; ore maps without ground columns still give the old rules v3 |
| Ore data | Catalogue checks pass | 3,551 deposits, 337 basins; heat maps 76-97 % MRDS for several metals | Not frozen per world; heat still sets background quality (survey bias); coal on mixed tonnage bases; about 35 well-known deposits missing; 14 % of positions 5 km or worse off |

The design questions behind the ore findings (what "real amounts" should mean in a game, what to do where records
are thin, how far a player may have to travel) are taken up in the proposal.

## What it means for the public 1:10 server

A player is planning a public no-wipe 1:10 world before Christmas 2026. Earth freezes the rock, ground and ore rules
per world at creation (decision 0002), so whatever generation exists when that world is made is what it keeps.

- **With vanilla rocks and vanilla ores** (the world screen's defaults since 2.0.1) almost none of these findings
  apply. Two do: iron, coal, sulfur, borax and saltpeter tied to sea level lie deep under high plateaus (EA-0170),
  and Earth's own gem and ore deposits do not follow world height (part of EA-0169).
- **With ground columns** (vanilla or Earth ores): clay, valley alluvium and placers (EA-0167, EA-0168) should be fixed
  first, and the ground rules must be versioned before any fix can reach new worlds without changing older ones
  (EA-0166).
- **With Earth's ore maps:** additionally the amount bugs, the highland gap, the grade bias and the catalogue's
  versioning (EA-0169, EA-0170, EA-0171, EA-0166). The ore maps are still marked experimental; with today's code they
  should not be used for a no-wipe world.

The world's settings are the server owner's and the author's choice; the proposal (section 8) gives a recommendation.

## Findings, ranked

Severity is player impact in the worlds that use the part, weighted by the fact that rules are frozen per world.
"Checked" means the coordinating session read the cited code itself; the rest rests on the review's evidence (each
report marks Verified or Suspected per finding). Report IDs: R = rocks, G = ground (the ground report's F numbers),
O = ore spawn (the ore-spawn report's F numbers), D = ore data (its section 0 and 7 numbers).

### High

| # | Finding | Evidence | Checked | Fix needs | Item |
|---|---|---|---|---|---|
| 1 | **Nothing freezes the ground rules, geology v2, ore layers or the deposit catalogue per world.** Ground rules have one file for every world; only rule conditions can be limited to later rules versions, not recipes, soils, materials or compaction. Geology v2 and the catalogue keep an informational fingerprint that only warns. `SavedWorldDigestTests` pins terrain only. Any fix silently changes new chunks of existing worlds (bodies cut at the explored edge, moved blankets duplicated). | G-F4 (`GroundColumns.cs:380, 769`), R7 (`EarthGeology.cs:390-396`), D1 (`EarthOres.cs:247-259`, `OreDeposits.cs:20`) | yes (ores) | tests and versioned file names; no rules change | EA-0166 |
| 2 | **Clay is in the ground only where a ground recipe carries it.** Wherever ground columns cover (the whole world), vanilla's clay, red clay and peat deposits are off; till, residual, saprolite, loess and colluvium have no clay layer. Away from rivers 19.9 % of land has clay in the top 4 blocks, 7.5 % of glaciated land; 31.7 % of glaciated land is more than 100 km (10,000 blocks at 1:10) from it. Real till is often boulder clay. | G-F1 (`EarthGeology.cs:1099`) | yes | ground rules 3 | EA-0167 |
| 3 | **Glaciated river valleys and floodplains get no alluvium, frozen alluvium takes no placers, and every river bed is 1 m of silt.** The `glaciated` and peat regions match before `river-valley` and `floodplain` ("first match first"); only 7.9 % of glaciated river valleys get an alluvial or outwash layer (the Vistula at Warsaw is dune on till, the Kolyma peat on till). Placers take only unfrozen gravel and sand (5.4 % of alluvial land is frozen: Yukon, Kolyma, Lena). River-bed silt cannot be panned. | G-F2, G-F3, G-F6 (`ground-rules-v1.json:119-141`, `earth-layers.json:10-81`) | yes (rule order) | ground rules 3, ore layers v5 | EA-0168 |
| 4 | **Ore maps: vanilla rounds any fractional attempt up to a whole percent, and multiplies attempts by world height / 256.** Earth shrinks attempts at fine scales to make up for larger deposits; every rate under 0.01 a chunk becomes 0.01, so at 1:10 / 1024 big cassiterite comes out 14×, halite domes 8.6×, corundum 153×. With the height factor the 1:10 play style is 3-8× richer than decision 0065 §5 intends; even 1:100 gets big cassiterite 3.8×. The prospecting pick uses the unrounded value and under-reads by the same factor. Earth's own ore and gem deposits do not follow world height at all. | O-F1, O-F2 (vanilla `GenDeposits.cs` 1.22.7, `OreSession.cs:213`, `EarthOreGenerator.cs:142`) | yes | ore rules 6 | EA-0169 |
| 5 | **Highland spawns have no iron, bituminous coal, sulfur, borax or saltpeter within reach, under either ore choice.** These deposits follow sea level, which Earth keeps at 110 (decision 0045); at 1:10 / 1024 the rock floor removes all iron at Addis Ababa, Mexico City and Nairobi, and 6-25 % survives at Pretoria, Tehran and Brasília; at 1:100 the iron lies 140-250 blocks down. Bog iron needs Geology Additions; the nearest catalogue iron is over 2,300 km away. Recorded by the 3 November tall-world research, never filed. | O-F3 (`EarthOreLayers.cs:82-85`, `SealevelDeposits.cs`) | yes (sea level) | ore rules 6 (and a decision on an Earth bog-iron block) | EA-0170 |
| 6 | **Bed thickness no longer follows world height.** It is 0.6 × sea level (`GeologyReference.cs:24`), and the sea level has been 110 at every height since decision 0031, so beds are 66 blocks thick at 256, 384 and 1024 while the land's vertical scale is 15.9, 8.4 and 2.5 m per block. Lower beds reach the surface on 11.6 / 13.6 / 22.7 % of regional land, basement on 0 / 0.2 / 4.5 %. Ground cover meanwhile follows the depth curve, so at 1024 high 100 m of cover eats bed 0. The tests build only the old layout. | R1, R11 | yes | geology rules 3 | EA-0173 |
| 7 | **Mantle peridotite under every continental flood basalt, and no ophiolite or ultramafic rock at the surface anywhere.** The mafic-volcanic profile ends in peridotite about 46 blocks below the reference surface (3.9 % of ice-free land: Deccan, Siberian Traps, Paraná, Ethiopia; in the Deccan at 384 high from about y 120, above sea level). Troodos is 89 % sedimentary, Oman 0 % mafic, the Urals 57 % mixed sediment, the Bushveld 62 % sedimentary. Chromite, nickel and platinum hosts are missing where they really are and present where they are not. | R2, R3 (`geology-rules-v2.json:45`) | yes (profile) | geology rules 3 | EA-0173 |

### Medium

| # | Finding | Evidence | Checked | Fix needs | Item |
|---|---|---|---|---|---|
| 8 | **Ore maps with regional or vanilla rocks still give new worlds ore rules v3 and heat maps v1**, the generation 0065 replaced (fill-any-rock districts, survey-driven hotspots). Since 2.0.1 this is one click from the defaults. | O-F4 (`WorldParamsBuilder.cs:224`, `EarthOres.cs:113`) | yes | a world-screen rule or rules v6 without layers | EA-0135 (updated) |
| 9 | **Background ore quality still follows the survey-biased heat.** Grade = `GradeQuantile(u, heat)`: about 61 % poor and 10 % bountiful in a cold cell, 18 % poor and 30 % bountiful in a hot one; the heat is 76-97 % MRDS (mostly US points) for gold, silver, lead, zinc, iron, manganese, uranium and vanadium. The bog-iron boost follows hard-rock iron heat (zero over Latvia) and the quartz-vein boost SiO2 heat (USA and Canada 40 %, Russia and China 0.5 % each). | D2, D6, O-F10 (`EarthOreGenerator.cs:367, 578`, `RegionalOreCatalog.cs:662`) | yes | ore rules 6 | EA-0171 |
| 10 | **Vanilla ores on regional rock lose whole host families.** Galena (with silver), coal, sulfur and the other sediment-hosted minerals have no host at any depth on 41 % of regional land; malachite on 66 %, the Central African Copperbelt included. Freiberg, Kongsberg, Sala and Broken Hill get no lead or silver. Vanilla's `{rocktype}` sand, gravel, beach and lake-bed layers skip Earth's own rocks (schist, gneiss, dolostone) and tuff. | R5, R6 (`BlockLayer.cs:180-186` vanilla) | no | Earth deposits in crystalline hosts (the author's call), geology rules 3 | EA-0174 |
| 11 | **Catalogue data.** About 35 well-known deposits missing (Antamina, Mount Isa's copper, Palabora, Kipushi, Peñasquito, Pine Point, Garpenberg, Greenbushes, the Atacama nitrates, Khibiny, Inder and others); coal tonnage on mixed bases (Russia 38 % of catalogue coal from prognostic resources, against about 14 % of the real total; China 12 % against about 25 %); the iron-ferro group's co-products ungraded (Norilsk's chalcopyrite on nickel bands); about 20 borax, halite, gypsum and sulfur lenses may place nothing (only domes and areas get the basin relaxation); 14 % of point bodies 5 km or more off (±550 blocks at 1:10), up to 82 % of bauxite by tonnage; giant stocks flattened (Chuquicamata 287 m over 12 × 3 km). | D3-D5, D7, D8, O-F7 (`OreDeposits.cs:634`) | no | catalogue v2 (versioned), ore rules 6 for the lenses | EA-0172 |
| 12 | **Ground data and recipe limits.** Recipe sub-layers under 0.5 m vanish (a 1 m alluvium at every scale; gravel needs 3.5-5 m of alluvium); valley alluvium only 120-190 m past the water at 1:10, dry channels not counted; peat only on Histosols (3.3 % of land; 89 % of unglaciated land over 100 km from peat); almost every Phanerozoic bed counts as an evaporite; sea floors in GUM's coastal fill take land recipes; category warp stops at 10° tile edges; all regional beds under a column get its surface age. | G-F5, G-F7 to G-F14 | no | ground rules 3, ore layers v5 | EA-0168, EA-0176 |
| 13 | **Rock data resolution and design.** The 0.5° dominant class erases granite districts and coasts (Cornwall and Devon 0 % granite, so no tin host where the Latvia pilot names its tin reference; Laurion, Etna, Vesuvius, Bangka and Cyprus fall in sea cells); one sequence thickness for the planet (CRUST1.0, already shipped, unused); `su` cells (28.1 % of ice-free land) keep vanilla's random strata in "regional" mode; the evaporitic profile has no gypsum or halite bed; the peat/clay relief footprint is 12 blocks at every scale, so inferred peat goes to zero at 1:10 / 256. | R4, R8-R10, R15 (`RegionalGeologyWorld.cs:31-45`) | no | geology rules 3 | EA-0173 |
| 14 | **Catalogue bodies are out of reach and unmarked at 1:10.** Median nearest catalogue copper from 36 spawn cities is 41,875 blocks, tin 106,000; bodies put no loose ore on the surface. The surface copper and tin starters are scaled like bulk ore, 320-700 blocks apart at 1:10. Thin seams round per column into speckle. | O-F6, O-F8, O-F9 (`OreDeposits.cs:1119`, `EarthOreGenerator.cs:118`) | no | ore rules 6; the reach question is the proposal's | EA-0169, proposal |

### Low

| # | Finding | Evidence | Item |
|---|---|---|---|
| 15 | Crafted-stone recolouring (decision 0058) never runs from a zip: `Ours()` compares asset origins with `Mod.SourcePath`, the zip, while the game serves a code mod's assets from `Cache\unpack\<zip>_<hash>` (`ModContainer.cs:108`). The 11 November review suspected it (CR-O10). | R12 (`EarthStoneTextures.cs:36-40, 85`) | EA-0175 |
| 16 | Catalogue host fallback picks the ore block alphabetically, not the most similar host; vein lean uses the depth curve's top scale at every depth; compact blankets ignore their layer search; the 64-block radius cap makes 1:5 the same as 1:10 for background deposits. | O-F11 to O-F15 | EA-0172 |
| 17 | Docs drift: the geology doc's "99 blocks at height 384, follows world height"; "tuff is never the top bed" (valleys cut into it); the Geology Additions grade adapter runs only in regional-geology worlds; 0065's copper grade bands (0.6/1.5/4 %) against the file's (0.4/1.2/3.0 %). | R14, O-F16 | fixed in this commit (docs only) |

## Data coverage, in numbers

**Regional rocks** (share of ice-free land): GLiM unconsolidated `su` 27.9 %, siliciclastic 18.1 %, mixed sediment
16.0 %, metamorphic 14.6 %, carbonate 7.9 %, acid plutonic 6.4 %, basic volcanic 3.9 %, the rest under 2 % each.
97.8 % lies in mapped cells, 2.1 % borrows a neighbour's class. Regional bedrock covers 71.8 %; author-drawn envelopes
207 cells (chalk 0.2 % of land, the Baltic pilot 0.02 %); everything else is 12 generic profiles. Only the dominant
surface class of each 0.5° cell is data.

**Ground columns:** soil group (SoilGrids 30″) on 99.5 % of land; cover thickness (Shangguan 30″) on 94.9 %, a rule's
typical value on 4.8 %; cover type from GUM's mapped sediment 48.6 %, from GLiM's rock class 43.8 %, borrowed 3.5 %,
and on 40.8 % a soil-group or place rule picks the recipe instead; bedrock era from one Chorlton era on 91 %;
basement CRUST1.0 1° on 99.75 %; glaciation (Northern Hemisphere only) 17.4 %, frozen cover 10.2 %. Recipes: alluvium
12.7 %, saprolite 12.4 %, residual 11.0 %, till 7.8 %, loess 6.6 %, peat 3.6 %, tephra 1.15 %, laterite 1.07 %, playa
0.96 %.

**Depth curve** (blocks for real depth; it follows world height only, deliberately matching the terrain's vertical
scale near the top):

| World | m per block at top | 1 m | 10 m | 100 m | 1 km |
|---|---|---|---|---|---|
| 256 high | 15.9 | 0.06 | 0.63 | 6.1 | 46.5 |
| 384 high (1:200, 1:100, 1:50) | 8.41 | 0.12 | 1.17 | 10.6 | 59.6 |
| 512 high linear (1:25) | 22.1 | 0.05 | 0.45 | 4.4 | 38.9 |
| 1024 high linear (1:10, 1:5) | 9.70 | 0.10 | 1.02 | 9.4 | 56.9 |

**Ore catalogue against the real world** (share of contained metal or tonnage; real shares approximate): copper
USA+Canada 19 % (≈6 %), Russia 3 % (≈8 %), Oceania 6 % (≈11 %); coal Russia 38 % (≈14 %), China 12 % (≈25 %),
Australia 2 % (≈9 %); zinc USA+Canada 23 % (≈5 %), Latin America 6 % (≈16 %); iron Oceania 17 % (≈32 %), China 3 %
(≈8 %); bauxite, nickel, manganese, chromium, diamonds and Latin American copper close to the world. Positions: 64 %
of point bodies within about 100 m, 22 % about 1 km, 14 % 5 km or worse. No heat and no catalogue body within 100 km:
copper on 87 % of Africa, iron on 92 % of Africa and 90 % of Russia. In ore rules v4/v5 the empty blocks matter only
for the map overlays and the quality tilt (finding 9), because the background is everywhere.

**Amounts against vanilla** (ore blocks per 32 × 32 chunk column, background, host rock assumed, with vanilla's
rounding; from the ore-spawn report's section 4.3):

| Ore | vanilla 384 high | vanilla 1024 high | maps 1:100 / 384 | maps 1:10 / 1024 |
|---|---|---|---|---|
| copper | 536 | 1,232 | 378 | 1,154 |
| tin | 37 | 97 | 31 | 182 |
| iron | 208 | 556 | 179 | 429 |
| coal | 669 | 1,784 | 424 | 1,315 |
| halite | 1,467 | 3,912 | 1,045 | 8,177 |

The catalogue adds about 0.12 copper blocks a chunk on the world average: the "real volumes" of decision 0065 are
real only for the known deposits, which hold about 0.03 % of the world's ore. In practice the ore maps are vanilla
× 0.35-0.69 in host rock plus the known deposits at real size. That is a sensible compromise, but 0065 does not say
it, and the proposal takes it up.

## Blindspots: where we could be badly wrong

Ranked by what a player would meet.

1. **A spawn with no way forward.** Clay in uplands with till (finding 2), iron and coal on high plateaus (finding 5),
   tin anywhere far from a tin district at 1:10 (median 106,000 blocks to a catalogue tin body, finding 14), bog iron
   without Geology Additions. Under the author's rule (13 November: real sources and substitutes, 10-20k blocks too
   far) these are the most serious gaps. Nobody has measured reach from many spawns yet; the proposal makes that the
   first test.
2. **Frozen mistakes on the no-wipe server.** Without per-world versions (finding 1) every later fix is either never
   applied or applied with seams at the explored edge.
3. **Survey bias in what the player sees.** Background quality (finding 9) and the map overlays follow MRDS's
   US-heavy points; Africa, Siberia and Amazonia look empty on the overlays although the background is there.
4. **Geology that contradicts famous places.** Cornwall without granite, Troodos and Oman without ophiolite, the
   Deccan on mantle rock, Kongsberg and Broken Hill without lead or silver under vanilla ores, the Kolyma without
   placers. Players will go to such places on purpose.
5. **Tall worlds behave differently from the calibration.** World height changes bed exposure (finding 6) and ore
   amounts (finding 4); every calibration was done at 384 high, and the public server is 1024 high.
6. **Thin data hidden behind confident maps.** Clay, graphite, fluorite, gypsum, molybdenum, aluminium and uranium
   heat is 60-90 % prior; the overlays draw it like evidence.

## What is sound

Save safety and fail-closed passes (EA-0098); the strata bulk path and its caches; the hand-off guards between ground
columns and regional rock; deterministic ore bodies and background across chunk borders; the shapes chosen per
deposit type (porphyry stocks, VMS lenses, coal and salt as area beds, placers and laterites as blankets, kimberlite
stocks, salt domes); the body scale formula (blocks = volume / (h_x × h_z × v)); the v5 valley search for blankets;
the layer pass keeping hard-rock ores out of cover; the catalogue's own checks (0 errors). The ground probe
reproduced the documented recipe shares from the code.

## Checked by the coordinating session

Read in the code on 18 November: vanilla clay and peat off under ground columns (`EarthGeology.cs:1099`); the
ground rule order (`ground-rules-v1.json:119-141`, "first match first"); the catalogue folder fixed and the fingerprint
informational (`OreDeposits.cs:20`, `EarthOres.cs:247-259`); background quality from heat (`EarthOreGenerator.cs:367`,
`RegionalOreCatalog.cs:662`); vanilla's percent rounding and height factor (`GenDeposits.cs`, 1.22.7 decompiled);
ore maps without layers giving ore rules 0 and heat maps v1 (`WorldParamsBuilder.cs:224`, `EarthOres.cs:113`); sea
level fixed at 110 by design (decision 0045, `SealevelDeposits.cs`, `VerticalTerrainOptions.cs:26`); bed thickness from
sea level (`GeologyReference.cs:24`); the mafic-volcanic basement (`geology-rules-v2.json:45`); the unpack folder
(`ModContainer.cs:108`, and the player's `Cache\unpack` holds `earth-2.2.0-rc.2.zip_9de73f2d46ba`).
