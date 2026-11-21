# Proposal: Earth's ores, the next model (18 November 2026)

Status: **proposal**. Nothing here is built. Agreed so far: the order of work in section 7 (the author, 18 November
2026); the rest is not agreed. Technical choices are the maintainer's (D72); the questions
for the author (how the game plays, scope, new blocks, the public server) are in section 8, each with a
recommendation. Evidence: [the audit of 18 November](../research/rocks-ground-ores-audit-2026-11-18.md) and its
reports, above all [research-vanilla-and-nodata.md](../research/rocks-ground-ores-audit-2026-11-18/research-vanilla-and-nodata.md).

The author asked (18 November 2026): "for ores we need to think bigger plan on how to correctly implement in game
correct heatmap and spawn logic. Especially where we don't have data support. Mineapi was one of ideas for places with
minimum or no data", and "amount we should somehow scale similar to world".

## 1. Where the ore model stands

Earth's ore maps (ore rules v5, decision 0065) have three parts:

- **Known deposits**: a catalogue of 3,551 deposits and 337 basins, placed where they are at their real size.
- **Background**: vanilla-like disc deposits at a fixed share of vanilla's attempts (0006's class floor), in every
  host rock and allowed layer, whether or not anything is recorded nearby (the author, 8 November: a missing record is
  not a missing ore).
- **Heat maps**: 0.5° evidence grids. Since v4 they no longer set amounts; they still set the background's quality,
  boost clay, peat, bog iron, quartz veins and salt flats, and draw the map overlays.

What the audit shows about the model itself, apart from its bugs:

1. **"Real amounts" cover almost nothing.** The catalogue's known deposits come to about 0.12 copper blocks a chunk on
   the world average, against about 378 from the background. Even all undiscovered copper the USGS expects would not
   reach 1 block a chunk. Real ore, at real size, is roughly a thousand times scarcer than vanilla. Every amount a
   player meets is a gameplay number.
2. **The background ignores geology beyond the host rock.** Central Latvia (Devonian sediments, the crystalline
   basement hundreds of metres to 2 km down) got 12,235 background copper blocks around one place on 0065's bench;
   Chile's copper belt gets the same rate per host rock. The
   "lack of records" rule removed survey bias, but it also removed what geology says.
3. **Where nothing is recorded, nothing distinguishes a likely place from an unlikely one.** The heat maps are 76-97 %
   MRDS for several metals, so they cannot do it; that is why 0065 stopped using them for amounts.
4. **Scale works for the mean, not for the shape.** The mean per chunk is the same at every scale (decision 0065's
   formula is right), but background deposits at 1:10 grow to a 57-block radius, each a kilometre-wide body every
   1.15 km, while the known deposits lie a median 42,000 blocks (copper) to 106,000 (tin) from the spawn cities and show
   nothing on the surface.

## 2. Principles

1. **Lack of records is not lack of ore** (the author, 8 November). Nothing may depend on how well a region was
   surveyed: no gate, boost or quality tilt from record density.
2. **Geology is not records.** Lithology, age, tectonic province, sediment and cover thickness, glaciation and climate
   history are mapped evenly over the whole Earth. Using them to say where a deposit type can form is not survey bias,
   and it is what real surveys do for undiscovered deposits.
3. **Real distribution, gameplay amount.** Real data decide where ore is rich and where poor, relative to the rest of
   the world. The game decides the absolute amount: on the world average, about vanilla's ore per chunk, times the
   world's deposit rate setting.
4. **Scale changes the grain, not the mean.** The average ore per chunk is the same at every map scale and every world
   height. Large deposits grow in blocks and lie further apart at fine scales, as they do now; small finds keep a size
   a player can use at every scale.
5. **Every player can progress by real means.** Each progression material is reachable from any spawn by a real
   source or a real substitute (the author, 13 November), within a distance the author sets per scale.
6. **Frozen per world.** Every input is versioned and saved with the world, so a later fix reaches new worlds only and
   never leaves seams in an old one.

## 3. The model: four sources of ore

| Tier | What | Where it comes from | Size | Status |
|---|---|---|---|---|
| T1 Known deposits | Real mines and basins | The catalogue (corrected and versioned: EA-0172, EA-0166) | Real size at the world's scale | Built (v4/v5) |
| T2 Expected deposits | The deposits that geology says are there but nobody has recorded | A baked file from permissive tracts × deposit density × grade-tonnage models, with known deposits taking the place of expected ones | Real size, like T1 | New |
| T3 Showings | The small finds a player mines most: vanilla-like discs in host rock and allowed layer | Everywhere host rock allows, at a floor; more where T1 + T2 hold more | Vanilla's disc sizes at every scale | Rework of today's background |
| T4 Spread | Ore moved from its source: placers down rivers, ore boulders in till down the ice, gossans and loose ore over shallow bodies | Computed from T1-T3 sources and Earth's rivers, ice and cover | By the carrier | New (placers exist as layer deposits) |

### T2: expected deposits

Real surveys estimate undiscovered deposits with a three-part method (USGS; Singer 1993, Singer and Menzie 2010):
permissive tracts from geology, the number of deposits from deposit densities, and their sizes from grade-tonnage
models. Earth can do the same once, offline, and ship the result as a file in the catalogue's format.

1. **Permissive tracts per deposit type.** From inputs that cover the world evenly: GLiM lithology (shipped), the
   geological provinces and cratons of Hasterok et al. 2022 (CC BY 4.0), CRUST1.0 sediment thickness (shipped), the
   ground's cover thickness, glaciation and climate history (shipped), and the USGS global assessment tracts where they
   exist (porphyry copper 192 tracts, sediment-hosted copper 48, potash 84; public domain). Rules follow the deposit
   models (USGS Bulletin 1693):

   | Deposit type | Permissive ground | Shape in Earth |
   |---|---|---|
   | Porphyry copper (Cu, Mo, Au) | Young magmatic arcs and orogens; USGS tracts | Stock |
   | VMS (Cu, Zn, Pb, Ag, Au) | Volcanic and greenstone belts, ophiolites | Lenses |
   | Sediment-hosted copper | Red beds over reduced shale in rift and intracratonic basins; USGS tracts | Bed |
   | SEDEX and MVT lead-zinc | Rifted shale basins; carbonate platforms | Lenses, bed |
   | Orogenic and greenstone gold | Archean and Proterozoic greenstone belts, slate belts | Veins |
   | Tin-tungsten | Granite batholiths and their aureoles | Veins, stock |
   | Banded iron | Archean and Proterozoic cratons | Bed |
   | Iron skarn, magnetite | Granite against carbonate | Lenses |
   | Chromite, nickel, PGE | Ophiolites; layered intrusions | Pods, beds |
   | Laterite nickel and bauxite | Old tropical surfaces on ultramafic or aluminous rock | Blanket |
   | Coal | Sedimentary basins of coal-bearing age (USGS world petroleum provinces as basin outlines) | Seams |
   | Evaporites, potash | Evaporite basins; USGS potash tracts | Beds, domes |
   | Kimberlite (diamond) | Archean cratons | Pipe |
   | Placers | From T4, not from tracts | Blanket |

2. **How many.** Expected count per tract = deposit density × tract area: USGS Professional Paper 1640-A (for example
   porphyry copper 0.00015-0.0012 per km², orogenic gold veins 0.0043-0.0054, kuroko VMS 0.003-0.03, kimberlite pipes
   0.000012), Singer et al. 2005 for porphyry copper, the Singer-Menzie regression on tract area where it applies. The
   known deposits of that type inside the tract are subtracted. **This is the step that removes survey bias**: a
   well-surveyed tract and an unsurveyed one with the same geology get the same total; the known deposits only decide
   where some of them are.
3. **Where.** Clustered inside the tract (districts of 10-50 km, as real deposits cluster), weighted by a simple
   prospectivity surface from the same geology, and kept apart from known deposits by the type's proximity rule.
4. **How big.** Tonnage and grade drawn from the type's lognormal grade-tonnage model (Bulletin 1693 Appendix B; the
   updated USGS models for porphyry copper, VMS, nickel laterite and chromite). Depth and shape by the type's rules,
   through the depth curve, like T1.
5. **Check.** Sum contained metal per type and continent against the USGS identified-plus-undiscovered estimates and
   long production history; correct a type's density where a continent comes out implausibly rich or poor. A bias
   test compares tract totals in well-surveyed and poorly surveyed regions with the same geology.
6. **Freeze.** One fixed seed per tract, baked once, shipped as `expected-deposits-v1` and saved per world like the
   catalogue (EA-0166). Size: tens of thousands of bodies, similar in form to the catalogue.

### T3: showings

The background stays everywhere host rock and layer allow (principle 1), but its rate follows the world's endowment:

- **Rate** per chunk = vanilla's mean × the world's deposit rate × w, where w is the T1 + T2 contained ore of that
  resource, smoothed over a district (about 50 km), divided by its world mean, and kept between a floor and a cap
  (section 8, question 3). The mean of w over the land is 1, so the world as a whole has about vanilla's amount.
- **Size**: vanilla's disc sizes at every scale. At 1:10 a vanilla copper disc is 60-100 m across, a real small
  showing; at 1:100 it is a large one. Today's background grows to a kilometre-wide body every 1.15 km at 1:10, which
  is neither real nor needed.
- **Quality** from the host and a flat distribution, never from records (EA-0171).
- **Starters** (surface copper and tin) at vanilla's size and spacing at every scale.

Survey bias cannot come back through w: within a tract the total is geology's (T2 step 2), so a well-surveyed region is
not richer than an unsurveyed one with the same rocks.

### T4: spread from the sources

Real ore is found far more often in what moved away from a deposit than in the deposit itself, and these are the real
sources that give reach:

- **Placers.** Gold, cassiterite, platinum, magnetite and ilmenite sands and gems carried down Earth's own rivers
  (flow field and network are built) from T1-T3 sources into alluvium and outwash: richest at slope breaks and inside
  bends, fading with distance, frozen alluvium included (EA-0168).
- **Ore boulders in till.** In glaciated ground, pieces of ore carried down the ice flow from sources tens to hundreds
  of kilometres up-ice (Finland's boulder tracing found Outokumpu in 1910). Loose finds, thin, but they put a little
  copper and iron into the Baltic and Canadian till plains. Needs an ice-flow direction field (from the ice-sheet
  centres of the last glaciation) and probably a loose-ore item per ore (section 8, question 5).
- **Gossans and stains.** Loose ore and iron caps over shallow T1 and T2 bodies, as vanilla does for its discs, so a
  great deposit can be found by looking.
- **Other real sources** the audit points to: salt springs over salt basins (Lüneburg, Halle, Droitwich: the inland
  salt of 1318; Earth has springs), sulfur at fumaroles and hot springs (Earth has 5,521 geothermal sites), bog iron in
  wetlands (EA-0170), laterite ironstone on tropical and highland plateaus, clay in till and lake beds (EA-0167).

## 4. Amounts and scale

Definitions: h is a block's horizontal size in metres (100 at 1:100, 10 at 1:10); v is the depth curve's metres per
block, which follows world height only.

| | Mean per chunk | Large deposits (T1, T2) | Showings (T3) | Spread (T4) |
|---|---|---|---|---|
| Map scale | Same at every scale | Real size: blocks = volume / (h² × v); larger and further apart at fine scales | Vanilla size; spacing set by the mean, so about vanilla's at every scale | Follows the carrier: river and ice distances in real metres |
| World height | Same per block of rock: attempts follow the chunk's own rock thickness, not world height / 256 (fixes EA-0169) | Through the depth curve | Through the depth curve | Through the depth curve |
| Deposit rate setting | Multiplies everything | Multiplies grade or count, not position | Multiplies attempts | Multiplies richness |

What the player then sees (copper, roughly):

| Scale | Showings | Great mines | Travel to a great mine |
|---|---|---|---|
| 1:100 | vanilla-like, richer in real copper belts | Chuquicamata about 77,000 blocks | a few thousand blocks |
| 1:10 | vanilla-like, richer in real copper belts | Chuquicamata about 7 million blocks | tens of thousands of blocks: an expedition or trade, signposted by gossans and placers |

The background is then not "real volume" and does not claim to be: real volume is about a thousandth of a playable
world. Decision 0065 should say so when this is decided (the audit's finding on "real amounts").

## 5. Where there is little or no data

- **Mindat / OpenMindat is not usable.** Its terms forbid copying data "for use in any other product"; the OpenMindat
  data are not licensed for redistribution yet (private, non-commercial use only; CC BY-NC-SA 4.0 announced). It has
  no tonnage or grade, and its coverage follows collectors, so it is thin exactly where Earth's gaps are and would add
  more US and European points. Re-check if the CC licence arrives; even then it would add occurrences, not amounts.
- **Geology fills the gaps instead** (T2, T3): the inputs in section 3 cover Congo as well as Arizona.
- **Open data worth adding to T1** (all openly licensed; details in the research report): the USGS deposit-type world
  databases (porphyry copper 422 deposits, VMS 1,090, sediment-hosted copper 785, lead-zinc 240, nickel laterite 120,
  podiform chromite 1,124), the USGS global assessment tracts, Geoscience Australia OZMIN (CC BY), the Nordic ore
  deposit database (check its terms), SGU (CC0), OpenStreetMap mines (ODbL; Earth already uses OSM). Not usable:
  CGMW maps, South Africa's CGS, S&P and other commercial databases, Chinese and Russian surveys.
- **The map overlays** should show observed evidence and geology-based potential apart, and say which is which.

## 6. Reach

Before any of this is tuned, measure it: a reach census from several hundred land spawns (and the 36 spawn cities) at
1:100 and 1:10, giving for each progression material the distance to the nearest minable source. Alloys count by
any route: tin bronze, bismuth bronze (zinc and bismuth) or black bronze (gold and silver).

| Material | Real sources and substitutes on Earth |
|---|---|
| Copper | Showings, native copper in basalt, malachite in red beds, ore boulders in till, black or bismuth bronze for the alloy |
| Tin | Showings in granite districts, placers down their rivers, else the other bronzes |
| Iron | Showings near the local surface (EA-0170), bog iron in wetlands, laterite ironstone, magnetite sands |
| Clay, fire clay | Till and lake clay (EA-0167), river clay, kaolin in saprolite, seat earth under coal, decision 0077's recipes |
| Flint | Chalk and limestone beds, till, river gravel, shingle (decision 0076) |
| Salt | Evaporite beds, playas, salt springs |
| Sulfur | Fumaroles and hot springs, evaporites, pyrite |
| Coal | Charcoal everywhere (it burns hotter than coal in Vintage Story) |

## 7. Order of work (the author, 18 November 2026)

The author set the order: "1st we need to fix vanilla settings to work correct", "2nd to fix is regional rocks with
vanilla ores", then "Ground columns how we can systematic fix?". The ore maps and the next model (sections 3-6) come
after those. Each step changes generation only for new worlds, through a version saved with the world.

**Step 1: vanilla rocks with vanilla ores** (the world screen's defaults; EA-0170). On Earth's terrain vanilla's own
height rules go wrong: sea-level deposits sit at a fixed height near sea level, surface-following ones at a fraction of
the local ground height, so on ground above about 1,450 m (10 % of ice-free land; 20 % is above 1,000 m) all iron
(limonite, hematite, magnetite), bituminous coal, anthracite, sulfur, borax, saltpeter, chromite, ilmenite, cinnabar,
rhodochrosite and emerald lie far below the ground: 140-640 blocks down at 384 and 512 high, removed outright by the
rock floor at 1:10 (measured on 3 November with vanilla ores: none left in the Alps, at Zermatt, in Ethiopia, Tibet and
the Atacama; deep native copper 87-99 % gone, quartz with its gold and silver 49-86 %). And vanilla multiplies attempts by
world height / 256 while Earth's sea level stays 110, so lowland ore thickens with world height (1.3-1.6 times per column
from 512 to 1024 high; the Dead Sea's halite 8.6 times). The fix:
- read every vanilla height rule as a depth below the local rock top, the depth it has in vanilla's standard world
  (sea-level deposits as below a surface at sea level, surface-following ones as a fraction of a standard column, the
  "anywhere" ones within the rock column), so a plateau has the same ore within reach as a lowland;
- attempts without the world-height factor, since the depth band no longer grows with height;
- Earth's own ore and gem deposits on the same footing; the prospecting pick reading the same;
- a version saved with the world (existing worlds keep theirs).

Built 18 November 2026 as [decision 0097](../decisions/0097-vanilla-deposits-below-the-local-ground.md) (Earth's own
deposits already followed the surface and needed nothing); what it left was EA-0181, vanilla's rock layers giving one or
two host rocks within reach, built the same day as [decision 0099](../decisions/0099-vanilla-rock-layers-under-the-local-ground.md). Planned checks were: a replay of vanilla's attempt loop at every play style; a headless census at highland and lowland places
(Addis Ababa, Mexico City, Nairobi, La Paz, Lhasa, Tehran, Johannesburg, Brasília; Vienna, Riga, the Pannonian plain, the
Dead Sea) at 1:100 and 1:10; a bench look. About 3-4 days.

**Step 2: regional rocks with vanilla ores** (EA-0173, EA-0174). Step 1 applies here too. Of the rock findings only one
family comes from the vertical layout (bed thickness tied to sea level, everything above the reference surface in bed
0: R1, R11); the rest are the made-up profiles (peridotite under flood basalts, no ophiolites, one sequence thickness, an
evaporitic profile without evaporites: R2, R3, R9, R15), the 0.5° data (granite districts and coasts lost, `su` cells
left to vanilla's strata, straight contacts: R4, R8, R13), and the fit with vanilla (vanilla's ore hosts and sand and
gravel layers not knowing Earth's rocks: R5, R6). Geology rules 3: thickness in metres through the ground columns' depth
curve; real basement by province; ophiolite and layered-intrusion envelopes; sequence thickness from CRUST1.0; `su`
filled; evaporite beds; Earth's rocks in vanilla's sand and gravel layers; vanilla's ore hosts mapped by rock class
(galena and silver in Kongsberg's gneiss, malachite in the Copperbelt's rocks). First pin v2 with a golden test (part of
EA-0166). A finer lithology raster (GLiM polygons, IGME 5000 for Europe) is a later, separate step with a licence check.
About 2 weeks.

**Step 3: ground columns, systematically** (section 9; EA-0166, EA-0167, EA-0168, EA-0176). About 3 weeks.

**Step 4: the ore maps and the next model.** First the fixes to today's ore maps (ore rules 6: EA-0169, EA-0171, EA-0135,
the catalogue's EA-0172 with its version), then T2 expected deposits, T3 showings by endowment, T4 spread from sources,
the overlays showing evidence and potential apart; only then decide whether the ore maps stop being experimental.

Throughout: the stone recolouring fix (EA-0175) whenever convenient.

## 8. Questions for the author

1. **The public 1:10 server.** With which rocks and ores should it be created? Recommendation: vanilla rocks and
   vanilla ores once step 1 is in (section 7); regional rock or ground columns only if steps 2 or 3 are finished and
   benched before it starts; not with today's ore maps.
2. **The amount rule.** Real distribution with vanilla's amount on the world average (recommended, section 4), or real
   absolute volumes (a thousand times scarcer than vanilla), or vanilla everywhere on Earth's rock as now?
3. **Contrast.** How much richer may a real ore belt be than the world mean, and how poor may ground without that
   geology be? Recommendation: floor 0.25 × the mean, cap 8 ×: Latvia keeps a quarter of vanilla's copper showings;
   Chile's belt gets several times vanilla's, plus its great mines.
4. **Reach targets.** Recommendation for 90 % of spawns: copper, iron, clay, flint and lime within 2,000 blocks; an
   alloy route within 5,000; gems and luxuries no limit. At 1:10 that is 20 and 50 km.
5. **New blocks after 2.0.** The author wanted none for a while (decision 0077). Steps 3-4 would want two: Earth's own
   bog iron ore and loose ore pieces for ore boulders in till. Bog iron is the main medieval iron of northern Europe;
   today Earth places it only with Geology Additions' block, and Geology Additions is not a supported route (the author,
   18 November 2026: "we were not planning support for it"). Without a new block the stand-in would be vanilla's
   limonite ore as nodules in the sand or clay under peat. Recommendation: allow an Earth bog iron block with step 3; ore
   boulders can wait for step 4.
6. **Lead and silver, malachite under vanilla ores on Earth's rock.** Vanilla's hosts leave Freiberg, Kongsberg, Sala and
   Broken Hill without galena, and the Copperbelt without malachite (EA-0174). Recommendation (part of step 2): map
   vanilla's hosts by rock class, so each vanilla ore also takes Earth's rocks of the classes it already takes, plus the
   real hosts it lacks (galena with silver in gneiss and schist veins); amounts stay vanilla's.

## 9. Ground columns: a systematic fix

The ground findings (EA-0167, EA-0168, EA-0176) come from six causes, and fixing each cause once fixes the family:

| Cause | Findings it explains | Systematic fix |
|---|---|---|
| **One recipe per column, chosen by a first-match list of regions.** "Glaciated" or "peatland" wins over "river valley", so a valley on old ice ground is till. | No alluvium in glaciated valleys; peat or dune in floodplains; land recipes on sea floors | Build the column as a stack of processes in geological order, each deciding its own presence and thickness from its own data: bedrock, weathering (saprolite, laterite by climate history and age), glacial (till, outwash), slope (colluvium), river (alluvium by height above the river), lake and sea, wind (loess, dune), peat, soil. A valley on old ice ground is then alluvium over till, with no rule order to get wrong. |
| **Vanilla materials switched off without a replacement.** Ground columns turn off vanilla's clay, peat, gravel and soil patches because they lay those themselves, but nothing checks that they do. | Clay on 20 % of land; peat on 3.3 %; no gravel where alluvium is thin | A materials contract: for every material vanilla provided (clay, peat, gravel, sand, fire clay, flint, salt, bog iron) the real sources in the column are listed, and a census measures each one's share of land and distance from many spawns against a target. Till gets its clay (boulder clay), weathered rock its residual clay, wetlands their peat (Earth's wetlands grid, PEATMAP). |
| **Thin layers rounded away one by one.** Each recipe share becomes whole blocks on its own. | A 1 m alluvium vanishes; gravel needs 3.5-5 m | Round the column as a whole (largest remainder), and give a layer that matters for play at least one block where its real thickness passes a threshold. |
| **Block variants not known to every reader.** Frozen sand is not sand to the placers; Earth's rocks are not rocks to vanilla's layers. | No placers in permafrost alluvium; river-bed silt not pannable | One equivalence table (frozen X counts as X; each Earth rock's vanilla class) used by ore layers, deposits, the pan and prospecting alike. |
| **Cover type from 0.5° cells and soil-group proxies.** Thickness is 30″ data, but what the cover is comes from GUM's 0.5° cells or a soil group on most land. | Narrow alluvium; whole cells of one cover; seams at tile edges | Landform from GLO-90 heights (height above the nearest river, slope, hollow or crest) at the ground grid's resolution decides river, slope and crest covers; the 0.5° map keeps only what it is good for (glacial, wind-blown and volcanic provinces). |
| **No versions and no global checks.** One rules file for every world; tests at a few sites. | Every fix would reach old worlds (EA-0166) | Versioned ground rules saved per world; the ground review's probe (compiles `src/Core`, samples all land at 0.1°) kept as a test tool; a truth table of named places with the column they must have (the Vistula at Warsaw: alluvium over till; a Kolyma valley: frozen alluvium with gold placers; Bangka: tin placers; Riga: till with clay; a Finnish shield knoll: thin till on granite; the Deccan: laterite on basalt; the Amazon: deep saprolite; a Saharan erg: dune sand; Death Valley: playa); a digest per published version. |

Order inside step 3: versions and the checks first (they fail today, which shows the work), then the stack of processes,
the materials contract, rounding and the equivalence table together as ground rules 3, then the landform bake as a
later ground rules version.

Made concrete (data, algorithm, data fusion, code, checks) in [ground columns 3](2026-11-19-ground-columns-3.md).
