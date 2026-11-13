# 0060: Ores, gems and deposits in their ground layer, whatever the ore choice

Status: agreed (the author, 7 November 2026: "Yes, build it", with the recommended answers below); built on
`claude/ores-in-layers` (see "Built" at the end); offline probe passes; bench check under both ore choices done on
8 November (see "Bench"); merged into `release/1.0` on 8 November (merge 1cb597c, in Earth 1.0.3-tall.21 at
03dddab) by the combined build, on the author's instruction.
Date: 2026-11-07.

## What the author asked

"Ores etc. must be in correct layers independent of heat map" (7 November; global-design section 9). Ores, gems and
other deposits go in their own host layer of the ground column, whatever the ore choice is (vanilla or the Earth ore
maps). For example, placer tin and gold go in alluvium, bog iron in peat, coal in coal-bearing beds, and salt and
gypsum in evaporites. The ore maps scale only how much there is; they never decide the layer.

## How it works today (survey, 1.22.7 and `release/1.0` 13b3ba9)

- **Vanilla's generator** (`DiscDepositGenerator`) draws a depth. The depth is either a share of the surface height
  or sea level (`yPosRel`) or a number of blocks below the surface (`depth`). The generator puts ore only into blocks
  in its host table (`inblock`). Deposits run in the TerrainFeatures pass, which comes after Earth writes the ground
  column. So hard-rock ores already land only in rock: beds, basement, or regional geology's rock. The generator knows
  nothing else about the column:
  - coal of any age goes into any sedimentary bed, so lignite can end up in Cambrian shale;
  - deep salt goes into sandstone, chalk or chert;
  - ore can sit in cover sediment that the column turned into rock below 150–200 m;
  - near-surface copper, tin and flint vanish wherever there is cover.
- **Deposits vanilla lacks.** Vanilla has no placers and no bog iron. Geology Additions adds rocky bog iron in muddy
  gravel. There is no bauxite deposit either: bauxite is a vanilla rock stratum that Earth's strata keep, so it turns
  up anywhere in the column, including inside the cover. There is no kaolin deposit; fire clay forms only in sand and
  gravel over bauxite.
- **Vanilla's soil deposits.** Its clay, peat and soil patches are already off where the column lays those layers.
  Its alluvial gravel patches still overwrite the top of any column.
- **Earth's ore maps** (`EarthOreGenerator`, rules v3) keep the provider's hosts, but the heat map chooses layers in
  three places:
  - "Known districts fill any rock": at heat 0.6 and above, and always for the surface starters, ore goes into any
    rock, using the ore block of a similar host.
  - Coal measures, iron formations and rich districts fill any rock at fixed depths.
  - Those beds are laid after the strata but before the ground column. The column then builds its cover around them,
    so ore blocks are left inside till or alluvium.

  Earth's bog peat comes only with the peat deposit, and ground columns switch that deposit off. So ground-column
  worlds have no bog iron under either ore choice. Earth's authored bauxite, gypsum, uranium, wolframite and corundum
  deposits exist only with the ore maps.
- **Earth's own ores and gems** (`earth-ores.json`, `earth-gems.json`) are ordinary rock-hosted deposits, in the same
  situation as vanilla's.
- **The ground column** (`GroundColumns.Column`) knows only each layer's material and depth. It does not record
  whether a layer is cover, bed or basement, which recipe or unit it came from, or its age.

## Proposal

### 1. The column names its layers

Each layer of a column records its part: soil, cover (with its recipe), deep cover (cover that has turned to rock),
bed (with its template unit) or basement. The column also keeps its bedrock age (Chorlton's era, which is already
sampled). A new rules file, `assets/earth/config/ore-layers-v1.json`, turns this into a layer class and sets where
each deposit may go.

| Class | Layers |
|---|---|
| alluvial | sand and gravel of the alluvium, fan and delta recipes, and the river-bed floor |
| outwash | outwash sand and gravel |
| peat | peat layers, in any recipe |
| laterite | laterite, ironstone and terra rossa |
| saprolite | felsic and mafic saprolite |
| playa | salt crust, and the clay and sand of the playa recipe |
| other cover | soil, till, lake, loess, dune, marine, slope, crust (calcrete, gypcrete), tephra and ocean floor: only the column's own blocks |
| deep cover | cover below its compaction depth |
| bed | bedrock beds, with their rock and age. Ages are Cenozoic, Mesozoic, Paleozoic and Precambrian; the Baltic units also split the Paleozoic at the Devonian. A bed is an evaporite bed if it belongs to regional geology's evaporitic sequence, or if it is gypsum, claystone, shale, mudstone, dolostone or limestone of Cambrian or later age. |
| basement | crystalline rock under the beds |

Where a column hands off to regional geology, which keeps its own rock below the cover, regional geology's beds count
as beds with the column's age, and its basement rock counts as basement.

### 2. Where each deposit goes

Hosts stay the provider's (vanilla's, Geology Additions' or Earth's). The layer rule only narrows where those hosts
count.

| Deposits | Layers |
|---|---|
| Hard-rock ores and gems: native copper, cassiterite, bismuthinite, chromite, galena with silver, sphalerite, ilmenite, limonite, hematite, magnetite, malachite, pentlandite, rhodochrosite, cinnabar, graphite, lapis, quartz with gold and silver, olivine and peridot, emerald, diamond; marble, obsidian, travertine, cracked rock; Earth's chalcopyrite to pyrite and its gems; the maps' uranium, wolframite and corundum | bed or basement; never cover, deep cover or soil |
| Alum, phosphorite, fluorite, sulfur | bed (their hosts are all sedimentary) |
| Flint | chalk and limestone beds |
| Lignite | beds of Cenozoic or Mesozoic age |
| Bituminous coal | beds of Devonian or later age |
| Anthracite | beds of Paleozoic (Devonian or later) or Mesozoic age |
| Halite in beds, with sylvite; Earth's gypsum | evaporite beds |
| Borax, kernite | evaporite beds of Cenozoic age |
| Salt flats (new Earth deposit: halite with sylvite under the crust) | playa. Vanilla's surface salt variant is switched off where ground columns own the column; under cover it finds no host rock anyway. |
| Bauxite (Earth's deposit, moved into the cover) | laterite. The column no longer keeps vanilla's bauxite strata, and keeps kimberlite and suevite only in bedrock. |
| Fire clay (new Earth kaolin deposit) | saprolite and laterite (vanilla's own fire clay over bauxite stays as it is) |
| Bog iron (Earth's bog peat, when Geology Additions' `stone-bogiron` exists) | peat, below its top block |
| Placer gold and placer tin (new) | alluvial and outwash |
| Vanilla's clay, red clay, peat, soil and gravel patches | off where ground columns own the column; the column lays these itself (the gravel patches are newly off) |
| Saltpeter | unchanged (cave air) |

Deposit codes the file does not name, such as other mods' deposits, follow a default: rock-hosted deposits go in bed
or basement; any other deposit is left unchanged.

### 3. How placement asks the column

`EarthGeology.LayerAt(x, y, z)` returns the layer class at a block, or "none" where ground columns do not own the
column. The ground pass already builds every column. It now also keeps each column's layer bottoms and classes for
its chunk until that chunk's deposit pass is over, about 24 KB per chunk column. If the table is missing, for example
after a restart between the passes, it is rebuilt from the model, which gives the same answer.

- **Earth's ore maps.** The generator asks before it places each block, and its host search within the depth window
  looks only for hosts in an allowed layer. "Fill any rock" is removed. Coal measures, iron formations and rich
  districts are laid after the ground pass, and only in their hosts within allowed layers. The heat maps still scale
  attempts, sizes, grades and district richness: how much, not where.
- **Vanilla ores, and generators Earth does not wrap** (other mods' generators, under either choice). Vanilla's
  generator places ore as it does now. Right after the deposit pass, in the same `GenDeposits.GenChunkColumn` postfix
  where the clay and peat clean-up already runs, Earth's layer pass:
  - puts the column's own block back wherever a deposit block sits in a layer that deposit may not use;
  - removes a loose-ore indicator from the surface when no allowed ore of its kind is left below it.
- **Earth's layer deposits** (placers, salt flats, bog iron, bauxite, kaolin and gypsum) become ordinary deposit
  assets, so both ore choices have them. The ore maps scale them like any other deposit.
- **Prospecting** counts only hosts in allowed layers. The ore maps' reading already walks Earth's column. Vanilla's
  reading gets the same column; today it reads vanilla's simulated strata, not Earth's rock.
- `/earth geology` names each layer's class.

### 4. Ore choices and other mods

- **Vanilla ores and the ore maps** use the same layer rules; only the number of attempts differs.
- **Geology Additions on:** its rocks are the hosts, and its deposits get layer rules by code. Its bog iron gives way
  to Earth's bog peat, as the ore maps already do.
- **Advanced Geology on:** Earth already turns off ground columns and its ore layer, so nothing changes.
- **Both off (the default):** Earth's own gneiss, schist, quartzite and gypsum, with their ore variants.
- **Rocks and ground set to regional or vanilla:** there are no columns and no layer pass, so generation is as now.

### 5. Saved worlds

The change applies to new worlds only. `EarthParams.OreLayers` is set to 1 when a world is created. Older worlds keep
today's generation for new chunks: no layer pass, ore-map rules v3 with fill any rock, and vanilla bauxite strata.
Existing chunks never change. A later change to the rules ships as a v2 file for new worlds. The new placer blocks and
the changed deposit list alter no saved block.

## Cost and what to watch

- **Amounts.** Vanilla's generator draws some depths the column cannot use (lignite in old beds, salt in sandstone).
  Those attempts now place nothing. The probe reports how much of each deposit is kept; vanilla's rates are not raised
  to make up for it. I expect hard-rock ores to be nearly unchanged, and coal and salt to drop where the beds are old
  or of the wrong rock.
- **Surface starters.** Most lowlands have cover, so near-surface copper, tin and flint stay in the rock under it. In
  the Baltic, flint comes from the till; a till flint block is a follow-up. The ore maps' surface starters no longer
  fill any rock.
- **Speed.** The layer pass is one walk per chunk column after the deposits, with one lookup per block. It will be
  measured on the bench.

## Tests

- **Offline** (`tests/OreLayersTests`). Sample columns at named sites, built with the play styles' depth curves:
  - Ruhr, Pennsylvania anthracite, Lusatia lignite, Zechstein salt, Death Valley, Uyuni;
  - Cornwall, Klondike, Kalgoorlie;
  - Latvia, Boké, Jamaica;
  - the Canadian Shield and the Amazon.

  Each deposit's vanilla depth draws and hosts go through the layer rules, and the test counts deposit blocks by layer
  class before and after. Checks:
  - no hard-rock ore in cover;
  - no lignite older than the Mesozoic, and no coal in basement;
  - salt only in evaporite beds and playas;
  - placers only in alluvial or outwash layers;
  - bog iron only in peat;
  - bauxite only in laterite.
- **Bench.** Two new ground-column worlds, one with vanilla ores and one with the ore maps, at a few sites each.
  `/earth orelayers` counts deposit blocks by layer class around the player, and the counts are compared with the
  probe's.

## Questions for the author

Answered on 7 November with the recommended answers ("Yes, build it"): placers are new blocks; vanilla's
attempts that land in the wrong bed are accepted as lost (the probe reports what is kept); uranium, wolframite and
corundum stay with the ore maps. Bauxite and gypsum move to both choices, as section 3 says.

1. Should placers be new blocks? These would be placer gravel and sand holding gold or tin, with a generic look at
   first: digging gives gravel plus nuggets, and panning gives more. The alternative is only richer panning in
   alluvial gravel.
2. Vanilla's generator will lose the attempts that land in the wrong beds. Do we accept fewer coal and salt blocks,
   or let vanilla's deposits move to their layer within their own depth window, as the ore maps do (which means Earth
   wraps vanilla's deposits under both choices)?
3. Uranium, wolframite and corundum have vanilla blocks but no vanilla deposits. Should they become deposits under
   both choices, or stay with the ore maps only?

## Built (7 November, `claude/ores-in-layers`)

What was built, and where it differs from the proposal above.

- **Column and rules.** `GroundColumns.Column` records each layer's part (top, cover, deep cover, bed, basement) and
  source as segments; its blocks are unchanged (`GroundColumnsTests` pass, and the probe checks that the segments
  rebuild the blocks). `src/Core/OreLayers.cs` reads `assets/earth/config/ore-layers-v1.json`. Classes as in section
  1, with two clarifications: the playa class covers every layer of the playa recipe (salt crust, mud, gypsum earth,
  clay, sand), and frozen sand and gravel count as alluvial or outwash only where the recipe says so (no placer
  deposit uses frozen hosts yet). An evaporite bed is a bed of gypsum, claystone, shale, mudstone, dolostone or
  limestone that is not Precambrian, or any bed of regional geology's evaporitic sequence.
- **Saved worlds.** `EarthParams.OreLayers` (ProtoMember 193) is 1 for new ground-column worlds. Earth's layer
  deposits are a separate asset file (`worldgen/deposits/earth-layers.json`) that every other world removes from the
  deposit list at start and after each reload. They are removed rather than given 0 attempts because vanilla draws a
  random number per variant and chunk even at 0 attempts, so older and non-Earth worlds keep drawing exactly the
  deposits they drew before.
- **Layer pass** (`src/EarthOreLayers.cs`). The ground pass records each chunk column's layers in a table (rebuilt from
  the model if missing). After the deposit pass, any deposit block in a layer that none of its deposits may use gets
  the column's own block back (in a hand-off zone the regional rock, or the host rock it replaced), and a loose-ore
  indicator with no ore of its kind left below is removed. This runs under both ore choices and for other mods'
  deposits.
- **Ore maps.** No "fill any rock" and no surface-starter lens in ore-layer worlds; every block asks the layer, the
  host search looks only in allowed layers, and coal measures, iron formations and rich districts are laid after the
  ground pass in allowed layers only.
- **Layer deposits** (both choices; they start only where ground columns own the column). Placer gold and tin in
  alluvial gravel and sand (new blocks `earth:placer-{gravel,sand}-{gold,tin}`: digging gives the gravel or sand and a
  few nuggets; one pan gives a nugget more often, then the block is ordinary gravel or sand); salt flats (halite with a
  sylvite point cloud) in playa mud and clay; bog iron (Earth's bog peat, only when Geology Additions' `stone-bogiron`
  exists) in peat; bauxite in laterite and, as karst bauxite, in terra rossa; kaolin (fire clay) in saprolite; gypsum
  in evaporite beds. Under the ore maps they keep their native attempts, depth and size and are scaled by their
  resource's evidence like vanilla's clay (soil class, ×1 to ×1.75); they have no ore bodies. Rates are first guesses.
- **Vanilla patches off under columns:** gravel and soil, surface salt, and Geology Additions' rocky bog iron, besides
  clay and peat as before. Preserved bauxite strata in a column become the column's material in every layer, so
  bauxite there comes only from the bauxite deposit; kimberlite and suevite stay only in beds and basement. Vanilla bauxite blocks below a hand-off column are
  also put back by the layer pass (bauxite only in laterite).
- **Prospecting.** Vanilla's pick reading, for generators Earth does not wrap, reads Earth's own blocks with every
  block in a disallowed layer left out (and nothing at or below the rock floor). The ore maps' reading gates by layer
  the same way.
- **Commands.** `/earth geology` names each layer's class, age and evaporite flag. `/earth orelayers [radius]` counts
  deposit blocks in loaded chunks by layer rule and class, reports any in a layer they may not use (expected 0), and
  the layer pass's session counts.
- **Changed from the proposal after the probe:** bog iron may take the top peat block. Peat is mostly one block thick
  at these scales, so "below its top block" left ground-column worlds with no bog iron at all. Bauxite also takes
  terra rossa: the model gives Boké and Jamaica residual-karst cover, and their bauxite is karst bauxite.

### Offline probe (`tests/OreLayersTests`)

Ground columns at the fourteen sites of "Tests" (with `RegionalRock` off, so each column runs to its basement), in a
1:10 world 1024 high and a 384-high world. Every vanilla and Earth deposit's depth draws (vanilla's distributions and
anchors) and hosts go through the layer rules. All checks pass: no hard-rock ore or gem in cover, deep cover, peat or
playa; no lignite older than the Mesozoic; no coal in basement or Precambrian beds; salt only in evaporite beds and
playas; placers only in alluvial or outwash layers; bog iron only in peat; bauxite only in laterite; fire clay only in
saprolite or laterite; and the layer deposits find their layers at their sites (salt flats at Death Valley and Uyuni,
placers at Cornwall and Klondike, bauxite at Boké and Jamaica, bog iron in Latvian peat). Kept shares over all sites
and both worlds: hard-rock ores and gems 99 %, bituminous coal 86 %, lignite 71 %, anthracite 60 %, flint 64 %,
halite 46 %, borax 8 %, kernite 0 % (no Cenozoic evaporite beds at these sites), Earth's layer deposits 51–100 %. The
probe's columns are the model's: at the Ruhr and in Pennsylvania the generic templates give no coal measures, which is a
ground-model matter, not a layer rule.

### Bench (8 November, AgentBridge bench)

Earth from this branch with the 1:200 play style (384 high, log1000) in creative, once with vanilla ores (worlds
`ores-v1`, `ores-v2`) and once with the ore maps (`ores-m1`), with the 1.0.2 terrain and water packs, the tall.5 height
packs and the tall.17 ground pack. Geology Additions was not loaded, so there is no bog iron, as designed. At each site
`/earth orelayers 64` counted the deposit blocks within 64 blocks by layer.

The bench found four faults, all fixed on the branch:

- **Salt flats never placed** under vanilla ores. They had `maxYRoughness` 2, which vanilla measures from the deposit
  centre's height. GenDeposits sets that height only for deposits with a climate check and otherwise leaves it at -99,
  so every block was skipped. The setting is gone, and the probe now fails any Earth deposit that sets it without a
  climate check (8f5c8eb).
- **Readings a block off the written column.** The relief cache keeps one chunk per thread but took its metres per block
  from whichever column asked first. A later reading (prospecting, `/earth orelayers`) could therefore rebuild a column
  that differs from the one the ground pass wrote: at Death Valley the deep-cover sandstone was 16 blocks in the world
  and 17 in the reading. The cache now always uses the chunk corner's latitude, as the ground pass did whenever its
  first column was in the ground grid (f26d478). Chunks generated before the fix keep their columns, and the census
  reports blocks there as misplaced: 15 at Death Valley and 128 at Death Valley north, all in chunks made before the
  fix. Chunks made after it showed none.
- **The census** did not look up the strata rock for blocks with several host rocks, as the layer pass does, so gypsum
  showed as "basement". It does now, and it names one block of each misplaced kind with its position (f26d478).
- **Prospecting names.** Under the ore maps the layer deposits use an ore map, so vanilla's pick reports them. They now
  have `ore-…` names and handbook pages (eff8d82).

After the fixes, in chunks generated by the fixed build, the census found no deposit block in a layer it may not use:

| Ore choice | Sites (none misplaced) | Layer deposits seen |
| --- | --- | --- |
| Vanilla | Salar de Uyuni, Bonneville, Lake Eyre, Owens Lake, Dead Sea basin, Jamaica, Cornwall, Riga, Lusatia | salt flats in playa (Uyuni 5,505, Bonneville 7,470), bauxite in laterite (Jamaica 624), placers in alluvium (Lusatia 84, Riga 166, Jamaica 32), fire clay in saprolite |
| Ore maps | Salar de Uyuni, Jamaica, Cornwall, Bonneville, Lusatia (one exception below) | salt flats in playa (Uyuni 4,777, Bonneville 6,448), bauxite in laterite (Jamaica 828), placers in alluvium (Lusatia 246), fire clay in saprolite (Cornwall 43) |

The one exception at Lusatia is a bituminous-coal block at the bottom of a channel cut after the ground pass. The
census rebuilds each column from the present terrain height, so it read the block as topsoil. In fact it lies in the
conglomerate bed beside till, so the census was wrong, not the ore.

What the layer pass put back over the whole session (kept / put back):

- **Salt flats:** vanilla's own disc tries the mud of every column, so most of its tries land outside playas and are put
  back (12–18 % kept). The ore maps search only allowed layers (100 % kept).
- **Hard-rock ores and gems:** 100 % kept under both choices.
- **Flint:** 5–40 % kept. Flint stays only in chalk and limestone beds.

**Fire clay (EA-0059, ore layers v2).** Under the ore maps the pass put back 93 % of fire-clay blocks (3,616 kept,
47,077 put back), against 10 % under vanilla ores. The put-back blocks were not Earth's kaolin: they were vanilla's
underclay. Vanilla's bituminous coal and anthracite (`BlockBituCoal`, `ore-ungraded.json`) lay `rawclay-fire-none` under
a coal block when the block below is a sedimentary rock (a random per 100 × 100 blocks, above 0.6), in beds 23–56 blocks
down. The ore maps make much more coal, so much more underclay. Fire clay under a coal seam is real (the seat earth), so
it should stay. Ore layers v2 (`ore-layers-v2.json`, `EarthParams.OreLayers = 2` for new worlds) give a rule an `under`
list: a block of that rule is also kept directly under a block that one of those rules kept, in any layer. Fire clay
names bituminous coal and anthracite. Worlds made with v1 keep v1. Bench, ore maps, same place (=67487 =50750): v1 put
back 1,004 and 1,425 (3,893 and 4,832 kept); v2 kept 3,922 and later 8,282, with 0 put back. The census within 96
blocks: fire clay in beds 132, none in a layer it may not use. `/earth orelayers` also names the first put-back block
of each rule (code, place, depth, layer). Speed was not measured on its own. The ore-maps world's first spawn area
took over three minutes to generate and the vanilla-ores world's under three; that includes the ore maps' own work,
not only the layer pass.

