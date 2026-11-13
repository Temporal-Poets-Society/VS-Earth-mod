# 0059: A water table for every land column, and Hydrate or Diedrate's aquifers from it

Status: **Merged into release/1.0** (8 November 2026; the author: "Merge as is"). The author answered the well
question (section 8) with "a now, ask HoD (c)", and the build went ahead on that. Open questions 1–4 are built as
recommended and stay open until the author answers them. The bench found two flaws (questions 6 and 7); the author said
"Yes, fix both", and both are built (8 November): ratings by real metres, and mapped wetlands raise the table. Built on
`claude/water-table-hod` (from release/1.0 at 13b3ba9, with release/1.0 up to 2b0e832 merged in) and merged without a
second bench run: offline tests pass, and the bench check of 7 November ran before the two fixes. In Earth
1.0.3-tall.21 (release/1.0 61b92c9). The author decided on 8 November against a bench check of the two fixes for now:
"No we for now make support as it is for HoD". Earth supports HoD 2.5.8 as merged. Then: "we ship Earth now with
support of HoD as it is and when PR is accepted then we make chnages". Earth ships with this support. Earth's side of
configurable well blocks (section 8) waits until HoD's author accepts the pull request (HoD pull request 215, opened
8 November).
Date: 2026-11-07.

## What the author asked

- Hydrate or Diedrate (HoD) support: "i want add https://mods.vintagestory.at/hydrateordiedrate mod support and it
  adds aquifer we could look how it is done and so add". On writing HoD's data format from Earth: "data format is not
  code. We can do it." (7 November; [global-design section 9](../research/geology-columns-2026-11-06/global-design.md)).
- A water table under every land column (habitat proposal 3a): "dig a block and get water"; peat and sand take part
  like any layer ("sand can be waterlogged, peat can be dry").

## Who builds the water table

The habitat proposal's ownership table gives the water table its own row: a height per column from terrain, rivers and
layer permeability, read by the habitat (wetness) and by Hydrology's seep rule. Nobody had built it: there is no water
table in release/1.0, `claude/biomes-1.0`, `claude/region-index` or `claude/ground-columns`, and `SiteDrivers.cs` in the
uncommitted `habitat-overlay` build reads wetness only from the baked Latvia map. The biome session ("Vanilla game
biome system") answered on 7 November: go ahead; it is not building a water table and has no plan to. The
`habitat-overlay` worktree and `SiteDrivers.cs` belong to neither session, so hooking its wetness to this table is for
that work's owner or the author to agree. This change builds the table as a pure model in `src/Core` with a query, so
the habitat's wetness and Hydrology's seep rule can read it later without change.

## 1. The water table (v1, simple)

The pure model is `src/Core/WaterTable.cs` (rules `assets/earth/config/water-table-v1.json`); the game side is
`src/EarthWaterTable.cs`. For a block column it gives the table's elevation (real metres), its depth below the ground
(m) and its block Y. Deterministic, from data Earth already has:

- **Ground**: the column's real elevation E (TerrainModel).
- **Drainage base B**: the lowest water level within R = 1.5 km: the sea (0 m), lakes and rivers at their water
  surface, otherwise the lowest ground. Read on a world-aligned lattice a third of R apart (500 m), like
  `GroundRelief`, so neighbouring chunks agree. Height above drainage HAND = E − B. (The habitat prototype routed real
  flow; a radius minimum is the cheap in-game stand-in.) Water narrower than the lattice counts only where a lattice
  point falls on it; a river still counts through its valley floor, which lies a few metres above it.
- **Depth** = 0.25 m + k × HAND, the prototype's v1 rule. k comes from the permeability of the top 10 m of the ground
  column (thickness-weighted): very low 0.2, low 0.3, peat 0.15, fractured and medium 0.55, high 0.9, karst 1.0.
- **Climate**: from Earth's aridity index (precipitation over potential evaporation). In dry climates k rises towards 1
  (the table lies flat at the base, so hills are dry), and in deserts the base itself drops, by up to 50 m at an
  aridity index of 0.05.
- **Perched water**: in humid climates a very-low or low layer starting within the top 2 m, under a more permeable
  one, holds the table at its top on ground at most 5 m above the base (Stagnosols on Latvia's till plains). v1 counts
  everything below a perched table as saturated, so it overstates saturation by the few metres between it and the
  regional table (under 5 m, from the 5 m limit: less than one chunk).
- **Mapped wetlands** (data before the model's guess; built 8 November, question 7): `Core/WetlandGrid`, the GLWD v2
  grid the trees already read, gives each 0.1° cell (about 11 × 6 km in Latvia) the share of bog, marsh, saltmarsh,
  wooded peatland, swamp forest and mangrove (weight 1), and damp ground (weight 0.5, saturated part of the year).
  The share is not a place: the wetland lies on the cell's low ground. So the table rises towards the surface by
  lift = min(1, share / 0.3) × (1 − HAND / 30 m), held to 0–1: depth − (depth − 0.25 m) × lift, in any climate (a
  marsh in a dry land is still wet). HAND is the usual wetland predictor: Rennó et al. (2008) find waterlogged ground
  below about 5 m and uplands above 15 m; the fade runs to 30 m because raised bogs stand above their surroundings.
  A hill in a boggy cell keeps the model's table.
- Under open water the table is the water surface; on beaches it is sea level. It never lies above dry ground. Outside
  deserts it never lies, in blocks, below the drainage surface next to it.
- **Blocks**: the depth in metres is placed with the column's own layer boundaries, so "the table is in the gravel"
  holds both in the model and in the blocks. With "Rocks and ground" set to regional or vanilla (no ground column),
  the top 10 m count as medium.

All numbers live in the rules file (`waterTable` section), not in code. The prototype's v2 groundwater mound (Dupuit)
is a later step; the interface stays "table depth here".

**Permeability classes** (the amendment agreed in habitat 3a), one per ground material, in `water-table-v1.json`
(`materials`), so the ground rules stay unchanged. Every ground material must have a class, and a test checks that:

| Class | Materials |
|---|---|
| very low | raw clays, lake and Devonian marl, mud, claystone, shale, slate, phyllite, oozes, permafrost ground, unfractured crystalline rock (granite, gneiss, schist, quartzite, gabbro, diorite, peridotite), ironstone, salt crust |
| low | both tills, silt, loess, colluvium, saprolite, terra rossa, laterite, calcrete, gypsum earth, andesite, tuff, chert |
| fractured | the top 100 m of crystalline rock below the rock's top, under any cover |
| medium | topsoil, sandstone, conglomerate, basalt, regolith |
| high | sand, beach sand, gravel, limestone rubble, carbonate sand, tephra |
| karst | limestone, dolostone, chalk, gypsum |
| peat | peat |

Vanilla and regional-geology rock (`rock-*`, `crackedrock-*`, ores by their host rock; extra rock names in `rocks`),
`sand-*`, `sandwavy-*`, `gravel-*`, `rawclay-*`, `peat-*`, `soil-*` and `forestfloor-*` blocks get classes by name,
so the rating below works under any rock choice. At start-up Earth logs any `rock-*` block it cannot class.

## 2. Aquifer rating per 32-block chunk (HoD's 0–100)

- Sample 4 × 4 columns in the chunk (every 8 blocks). Only ground blocks at or below that column's water table count.
  Air, water, ice, caves, structures and ground above the table count nothing. A chunk wholly above the table reads 0:
  HoD's pick says "none" there and its hint points down, so the player digs to the water.
- **By real metres** (revised 8 November, question 6): at each sampled column the chunk's transmissivity T (m²/day)
  is the sum over its saturated ground blocks of the block's real thickness (from the column's own layers and the
  depth curve) × its class's hydraulic conductivity. The same ground then gives the same T at every vertical scale:
  30 m of gravel is 30 blocks at 1:1 and 3 blocks at 1:10, and reads 59 in both (a test checks it).
- Conductivity, m/day, near the ground (geometric middles of the ranges in Heath 1983, USGS Water-Supply Paper 2220,
  and Freeze and Cherry 1979): very low 0.00001, low 0.003, peat 0.03, fractured crystalline 0.1, medium 1, karst 10,
  high 30. It halves at 1 km below the ground (pores close).
- The column's rating is log-linear in T, on Krásný's transmissivity classes (1993; Krásný and Sharp 2007) put on the
  categories of HoD's prospecting pick: below 0.1 m²/day (VI, imperceptible) up to 10, "none"; 0.1–1 (V) up to 15,
  "very poor"; 1–10 (IV) up to 20, "poor"; 10–100 (III) up to 40, "light"; 100–1000 (II) up to 60, "moderate"; above
  1000 (I) "heavy", 100 at 10 000. HoD turns the rating into litres linearly, so the log scale keeps the categories
  right across the four orders of magnitude real aquifers span. A plain sum against a reference thickness (the first
  proposal) would have rated 320 m of till "heavy" at 1:10.
- Chunk rating = the mean of the sampled columns' ratings (what a well at a random place in the chunk finds) ×
  recharge, 0.5 + 0.5 × min(1, Q / 300 mm). Q is the yearly water surplus from Earth's precipitation and aridity
  (Budyko–Fu). Deserts keep half, since fossil water still yields. Rounded and held to 0–100.
- A chunk saturated through near the top at 1:1 (32 m; humid): sand and gravel about 60 (moderate to heavy), karst
  limestone and dolomite about 50 (moderate), Devonian sandstone about 30 (light), the fractured top of granite about
  17 (poor), peat about 15, till about 10 and clay or deep granite 0 ("none"). At 1:10 a chunk holds about 320 m: the
  same sandstone reads about 49 (moderate) and till 15 at most. In the tests, 62 m of saturated gravel reads 70
  (heavy), 18 m of gravel over the fractured granite top 54, and the fractured granite below 18.
- Read from the blocks the chunk actually holds at the end of generation, so ground columns, regional geology, vanilla
  strata and deposits are all counted. A block → class table is built once at start-up. Cost per new column: 16
  water-table lookups and at most 16 × the world height block reads (16k in a 1024-high world).

## 3. Salty water

A chunk is salty when most of its sampled saturated ground (by metres) is:
- under the sea floor;
- near the coast, where the sea is the drainage base within R: deeper below sea level than 40 × the table's height
  above sea level (the Ghyben–Herzberg fresh lens over sea water);
- under salt flats: salt-crust or gypcrete cover, or a salt lake within R;
- deep brine: more than 500 m below the ground (rules value).

## 4. When Earth writes `aquiferData`, and why HoD sees it first

Checked in the sources:
- **Vanilla 1.22.7** (`ServerSystemSupplyChunks`, decompiled): a new column runs every worldgen pass (Terrain …
  PreDone) in `PopulateChunk`. Only when it reaches Done does `loadOrGenerateChunkColumn_OnChunkThread` queue
  `mainThreadLoadChunkColumn`, which raises `ChunkColumnLoaded` with the same chunk objects. A saved column loads its
  moddata from disk before the event.
- **HoD 2.5.8** (`AquiferManager.Generation.cs`): on `ChunkColumnLoaded` it reads `aquiferData` per chunk (its live
  cache, else the chunk's moddata via protobuf). If any chunk's data is missing or older than version 2, it regenerates
  the whole column. If all are present but some lack a smoothed rating, it marks the column for its neighbour
  smoothing.

So Earth writes in its own PreDone handler for every new column, when HoD is enabled. It writes `aquiferData` for
every chunk of the column (y = 0 to height/32 − 1, air chunks included, rating 0), version 2, with the smoothed
rating equal to the raw one. Earth's ratings are already continuous, so HoD's smoothing is skipped. HoD still scans
each new column's fluids once for its own `game:waterCounts`; Earth leaves that key alone.

## 5. The contract in Earth

`src/HodAquiferContract.cs`: Earth's own protobuf classes with HoD's field numbers: chunk {1 data, 3 version},
data {1 raw rating, 2 salty, 3 smoothed rating (optional)}. They are serialized with the game's protobuf-net
(`RuntimeTypeModel.Default`, as `SerializerUtil` does) and stored with `SetModdata("aquiferData", bytes)`. Earth
references no HoD assembly and copies none of its code. protobuf-net leaves out a zero rating and `false`, which HoD
reads back as the same defaults; the smoothed rating is optional, so it is written even when 0. Rating 73, salty, is
`0A 06 08 49 10 01 18 49 18 02`; rating 0, fresh, is `0A 02 18 00 18 02`. Offline tests decode the bytes with a
separate wire-format reader and check field numbers, wire types and values.

## 6. If HoD raises its data version

At server start, with HoD enabled, Earth reads `AquiferManager.CurrentAquiferDataVersion` by reflection. It also
decodes one sample of its own bytes with HoD's `AquiferChunkData` type. Earth writes only if the version is 2 and the
sample comes back unchanged. Otherwise it logs once ("Hydrate or Diedrate's aquifer data is version N, Earth writes 2;
HoD makes its own aquifers") and writes nothing, so HoD's own generation runs as without Earth. Chunks Earth already
wrote stay version 2; a newer HoD that treats them as old regenerates them by its own rule (replaced, not broken).
Following a new version is a new Earth change.

## 7. Worlds and mods

- **Rules version, new worlds only**: `EarthParams.WaterTableRules` (proto member 194) records the rules a new world
  was made with (1). Earth writes HoD's data only in worlds with 1 or more. Saves made before it read 0, and HoD keeps
  making its own aquifers there, as before, even after Earth is updated. The query and `/earth water` answer in every
  world, since they save nothing. A later rules file (v2) will be read only by worlds that record 2.
- **Without HoD**: Earth writes no moddata and generation is unchanged. The query exists but nothing calls it during
  generation; `/earth water` shows it on demand.
- **HoD added later to a world made with the water table**: only chunks generated from then on get Earth's ratings;
  older ones get HoD's own. No backfill (see open questions).
- **Server and client**: the query runs on the server. It is a deterministic function of the place and the world's
  saved settings and data, like the river query (`RiverAt`), so a client copy would give the same answers if a client
  ever needs one.
- **HoD's wells** (checked in HoD 2.5.8): a pickaxe starts a well only in `rock*`/`crackedrock*` blocks and a shovel
  only in `soil-`, `sand-`, `gravel-` and `forestfloor-` blocks (both lists are in HoD's code). A well is "shallow" when
  its block matches HoD's soil list (`BlockUtils.SoilPathPrefixes`, a public setting). Shallow wells ignore the aquifer
  and need surface water within 3 blocks. So in HoD the best real aquifers, saturated sand and gravel, give only
  shallow wells, and Earth's till, silt, marl and loess take no well at all. See section 8.
- **Hydrology**: its seep rule can read the same query later (D06/D25: Earth gives the level, Hydrology the water
  blocks). Not part of this change.
- **Earth's other wetness data** (pointed out by the biome session; all pure lookups on release/1.0). This change
  alters none of `WetlandRules`, `FireRules` or the tree mix.
  - `Core/WetlandGrid` (GLWD v2 aggregated to 0.1° cells, `WetlandRules`): **read by the table since 8 November**
    (section 1, question 7). The table reads the grid only. It changes no wetland, fire or tree rule. It reads the
    grid whatever the world's `WetlandRules`, and its own rules version gates it for HoD.
  - `Core/GroundMoisture` (topsoil wetness 0..1 from the last 12 days' rain, nothing saved) is short-term surface
    wetness, not groundwater. The two stay apart: the water table reads no weather and the moisture reads no
    groundwater. How a habitat's site wetness combines them (for example the wetter of the two) is the habitat's design,
    not decided here.
  - `Core/SoilSiteGrid` (SoilGrids sites, Europe) is not used.

## 8. Any well mod: Earth's query, and one adapter per mod

The author (7 November), on HoD's rock-only aquifer wells: "kind of dumb as these are often layers with lot of
water. Rock has very little irl my mind. But build would be suitable for any well mod?"

What holds water is not what gives it. Clay, till, silt, marl and loess hold much water in their pores, but release it
very slowly, so a well in them gives little. Sand and gravel hold and release it: they are the best shallow
aquifers. Rock varies widely. Granite and gneiss hold almost none. Sandstone, chalk and karst limestone are major
aquifers: Latvia's Devonian sandstone and dolomite, the English chalk, the Nubian sandstone. The classes in section 1
follow that.

So the build is split:
- **Earth's query (any mod)**, on the `EarthWaterTable` mod system, read by reflection like the river flow query
  Hydrology reads (0027), and versioned (`WaterTableQueryVersion` = 1):
  - `bool WaterTableAt(int x, int z, out int tableY, out double depthMetres, out bool openWater)`: the table's block
    Y, its depth below the ground and whether open water lies there;
  - `bool AquiferAt(int x, int y, int z, out bool saturated, out double conductivity, out bool salty)`: one block,
    its layer as generated (the ground column's or regional geology's, elsewhere the block now in the world): saturated
    or not, its hydraulic conductivity in m/day (section 2, with the depth factor) and salty or fresh;
  - `bool WritesHodAquifers`: whether the HoD adapter is writing in this world.
  Both return false where Earth has no water table (outside the map, the water table off). Earth itself depends on no
  well mod.
- **Adapters**: the HoD writer (sections 2 to 6) is one adapter. It turns the query into HoD's per-chunk data. Another
  well mod either reads the query or gets its own small adapter.
- **HoD's well rules** are HoD's code. Options:
  - a. leave them: Earth's rating counts where a player digs a well in rock;
  - b. in Earth worlds, take `sand-` and `gravel-` off HoD's public soil list, so sand and gravel wells draw on the
    aquifer (no HoD code changed, but HoD behaves differently there, and its other uses of the list need checking);
  - c. ask HoD's author for configurable well blocks: wells in till, silt and loess, and aquifer wells in sand and
    gravel.
  Recommended: a now, and c. Wells that follow the table in any ground, "dig a block and get water", fit Hydrology's
  seep rule better than HoD, since it would read Earth's query directly.
- **Chosen** (the author, 7 November): "a now, ask HoD (c)". Earth leaves HoD's well rules alone. The author sent HoD's author a
  Discord message on 7 November asking for the well blocks as a config list, so wells can be dug in till, silt, loess
  and clay (Earth would fill the list for its worlds). After HoD's developer replied on Discord, the author had a
  separate session open [HoD pull request 215](https://github.com/Chronolegionnaire/HydrateOrDiedrate/pull/215) on
  8 November (open, waiting on HoD's review). It works by opting blocks out rather than listing them:
  - Any block whose block material the tool digs can take a well spring (shovel: soil, gravel, sand; pickaxe: stone,
    ore), unless its attributes say `"HoDnotWellable": true`.
  - A well is shallow by HoD's old code prefixes (`soil-`, `sand-`, `gravel-`, `forestfloor-`), unless
    `"HoDshallowWell"` says otherwise.

  Earth's till, silt and loess are soil material, so with it they take shovel wells that draw on the aquifer, with no
  change from Earth. On the bench a till and loess chunk rated 33 gave 8.25 L/day per well, with two wells there.
  Earth's other ground blocks will take aquifer wells too:
  - soil material: bog peat, ooze, permafrost, saprolite, terra rossa;
  - gravel material, but no `gravel-` code: colluvium, regolith, tephra;
  - stone material: marl, calcrete, gypcrete, salt crust.

  Once it is merged, these need a look: which of them should be marked shallow or not wellable (permafrost, for one),
  and Cylinder World's `glacier` and `wallice`. Those have no block material, so they count as stone and would take
  pickaxe wells. The author, 8 November: Earth ships now with HoD as it is and makes its changes once HoD accepts the
  pull request. Until then Earth changes no HoD rule.

## 9. Checks

- Offline, `tests/WaterTableTests` (`dotnet run`), **all pass** (7 November): a class for every ground material and
  for vanilla rock, sand, gravel, clay, peat and soil blocks; table cases (valley floor, hill on till and on sand,
  semi-arid and arid hill, perched and not, open water, base above the ground); block placement and the drainage-surface
  clamp; metres ↔ blocks through a column's own layers and its basement; the drainage base (river, lowest ground,
  sea, the same values for a grid and single points, salt lakes in and out of reach); Budyko surplus and recharge;
  ratings (gravel over granite chunk by chunk, clay, karst over sandstone, a desert's half) and salt (sea floor, salt
  flat, the Ghyben–Herzberg lens, deep brine); `aquiferData` for all 32 chunks of a 1024-high column decoded back by a
  separate wire reader and by protobuf-net, and the exact bytes above; columns from the real ground rules.
  **Again all pass after the two fixes** (8 November), with these added: the rating's anchors, rising and log-linear;
  30 m of gravel over clay reads the same (59) at 1 m and 10 m a block; 320 m of saturated till at 10 m a block stays
  "very poor" at most, 320 m of sandstone reads "moderate"; the wetland lift (Ķemeri's bench case, low ground, half
  the share, a hill in a boggy cell, a desert marsh, over a perched layer, open water); and the shipped grid's shares
  (Ķemeri 0.34, Teiči up to 0.57, Riga centre 0.08). The Earth Debug build succeeds.
- Bench (AgentBridge, other sessions asked first): a Latvia 1:10 world with HoD 2.5.8. The prospecting pick's aquifer
  reading in outwash gravel, on a till plain and in granite; on a dry hilltop above the table and below it; a dug well
  that fills; no HoD regeneration in the log; on bogs GLWD marks open (Ķemeri, Teiči), `/earth water` shows the table at
  or near the surface; a world made before the rules version writes nothing.
  **Done 7 November** (Earth `1.0.3-tall.16-water.1` from 75c16c6 with HoD 2.5.8, a new 1:10 world at Riga, 1024
  high, ground columns):
  - Start-up: "Hydrate or Diedrate 2.5.8 reads aquifer data version 2; Earth writes it"; no rock block unclassified.
  - At seven places HoD's own `/getaquifer` equals the rating Earth stored, so HoD kept Earth's data and generated
    none of its own: Riga 16, Ādaži sand plain 19, Zemgale clay plain 13, Gaiziņkalns top 7 (table 59.6 m down, the
    chunk below 27), Ķemeri bog 15, Jūrmala coast 14 (chunks below 64 salty: the sea in reach, the table 3.4 m above
    it), Helsinki till on granite 8 (below: 2, then 1 salty).
  - The prospecting pick on a rock block at Ādaži: "Poor fresh water aquifer detected" (19), and "The aquifer seems to
    get stronger to the north-west and below" (the chunk below reads 20). Like vanilla, the pick reads only rock
    blocks, so on Latvia's thick cover it reads nothing at the surface.
  - Wells: a wellspring lined with granite in that chunk gives fresh well water at 4.8 L/day, HoD's own formula on
    Earth's rating of 19, halved, most likely by the leftover entry from a first test spring there. One whose
    neighbours are sand is a shallow well (HoD's soil list): 0 L/day without surface water nearby, whatever the
    aquifer. That is HoD's rule that the request in section 8 is about.
  - Found, for the author (items 6 and 7 below): at 1:10 every Latvian chunk reads 7 to 27 ("none" to "light"); and
    the table at Ķemeri bog is 7.3 m down. (Both fixed on 8 November; the bench has not been run again since, and the
    author chose not to for now. The
    grid calls Ķemeri's cell 34 % wooded peatland and 1 % open bog, not open bog: the bench note took the bog on the
    ground for the grid's class. With the fix, Ķemeri's case rises to about 3.5 m, since it stands 13.8 m above its
    base.)
- `/earth water`: the table, its base, aridity, surplus, top-10 m k, the ratings of the player's column recomputed from
  its blocks, the stored `aquiferData` decoded, and HoD's status.

## Open for the author

1. Smoothed rating = raw (HoD does not re-smooth Earth's values), or leave it empty so HoD smooths with neighbours?
   _Built: = raw._
2. Deep brine below 500 m, or another depth, or none? _Built: 500 m (rules value)._
3. Chunks above the table rate 0 (dig to the water). HoD's own rule instead caps every chunk above sea level at 30.
   _Built: 0._
4. New chunks only, or also fill older chunks that have no aquifer data yet when they load (Earth's handler would then
   have to run before HoD's)? _Built: new chunks only._
5. HoD's well rules: a, b or c (section 8)? **Answered: a now, ask HoD (c).** Earth ships with a; its changes for c
   follow once HoD accepts the pull request (the author, 8 November).
6. **Ratings depend on the world's vertical scale** (found on the bench). A rating is the saturated block share of a
   32-block chunk. At 1:10 one block holds about 10 m of ground near the top, so a chunk spans about 300 m of mixed
   layers, and a 17 m sand or 50 m sandstone aquifer is one to five blocks of it: Ādaži's sand plain reads 19 ("poor").
   Proposal: rate the chunk by real metres instead, the sum of class yield × saturated metres in the chunk against a
   reference thickness (for example 30 m of saturated gravel = 100), so the same ground reads the same at every scale.
   **Answered: "Yes, fix both" (8 November). Built** by real metres, as transmissivity (conductivity × metres) on
   Krásný's classes rather than a plain sum against a reference, which would have rated 320 m of till "heavy" at
   1:10 (section 2). The rules version stays 1: the branch has never been released.
7. **Mapped bogs** (found on the bench). Ķemeri's raised bog (GLWD open bog) has its table 7.3 m down: the ground
   columns give lake clay there, not peat, and the perched rule stops at 5 m above the base. Proposal: where
   `WetlandGrid` marks open bog, marsh or saltmarsh, the table is at the surface (data before the model's guess);
   this reads GLWD and changes no wetland, fire or tree rule.
   **Answered: "Yes, fix both" (8 November). Built** with two changes from the proposal, both open to the author
   (section 1). First, all saturated GLWD classes count, wooded peatland and swamp forest too (damp ground at half
   weight), not only the open ones the trees use. Ķemeri's cell is mostly wooded peatland. Second, the grid's 0.1°
   share is not a place, so the lift fades with the height above the drainage base. Otherwise every hill in a boggy cell
   would get a wet top, Gaiziņkalns's cell for one (25 % peatland). Ķemeri's bench case rises from 7.3 m to about 3.5 m,
   not to the surface. Locating the wetlands themselves would need GLWD at its own 15″ resolution, a new asset.
8. **Probes half a block off the generated grid (found in 2.0.0-rc.2's review; proposal, not changed).** The water
   table, the aquifer query, Hydrate or Diedrate's data and the seeps probe each column at the block's centre (x + 0.5,
   z + 0.5), as the rest of the springs planning does (stream heads, hot and frozen ground, the drawn-river distance),
   while the terrain pass builds block column x, z from the model at the whole-block coordinate. In an offline 256k
   world the two ground tops differ in 3.8 % of columns (about 11 % of land), by 2 blocks or more in 0.1 %. Moving only
   the water table's probes (tried in rc.2 and reverted) changed generation in existing worlds: seeps in streams-springs
   worlds and HoD's aquifer data in new chunks, with no rules version, and left the seep planner's own reads at the
   centre. Proposal: align the water table and all springs planning to the generated grid together under a new water
   table rules version (new worlds only), after 2.0.
