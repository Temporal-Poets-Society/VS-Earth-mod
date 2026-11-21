# Rocks, soil and ores

What the ground is made of, and where the ores are. Earth gives you a choice: keep vanilla's rocks and ores on Earth's
landscape, or take the rock from real geology. The soil on top comes from the real soil map unless you choose otherwise.

## Rocks and ground

**Vanilla:** rock layers come from noise: bands of granite, limestone, basalt and the rest, wherever the noise puts
them, with soil, sand or gravel on top by climate. Each layer is laid for vanilla's own landscape, where land stands a
little above sea level, and grows thicker in a taller world.

**Earth:** **Terrain · Rocks and ground** on the Earth tab:
- **Vanilla rock layers** (default): the game makes its own rock layers, laid under the real ground. Each place gets the
  layers vanilla gives a place at sea level, so a plateau keeps its sediments on top and has a lowland's layers within
  reach instead of bare granite, while a ridge standing above the land around it shows its hard core as a vanilla
  mountain does. In tall worlds the layers are as thick as in a standard vanilla world, not stretched with the height.
- **Regional rock regions only:** the real rock of each region, from a map of about 0.9 km cells made from the world's
  geological maps (GLiM, and Macrostrat's national maps where they exist): limestone where the land is limestone,
  granite where it is granite, basalt in Iceland, marble at Carrara. Ophiolites (Troodos, Oman), layered intrusions
  (the Bushveld, Sudbury), ocean islands and granite districts (Cornwall) have their real rock; flood basalts lie on
  granite; land under loose sediment has the rock beneath it; evaporite regions carry beds of gypsum and rock salt. The
  first such world on a machine downloads the rock map (45 MB, kept for every world).

With regional rock (and in ground-column worlds), five rocks join vanilla's where the real land has them: **gneiss,
schist, quartzite, dolostone and gypsum**.

### Ground columns

Ground columns (real soil, sediments and rock) are not on the world creation screen: they need more work, and are
there only for development (`ExperimentalGroundColumns` in `earth.json`, see [Settings](Settings)). Worlds made with
them keep them. In such a world the rock is the real rock of each region, and over it the ground is built from real
data: the soil by its real soil type, and the real sediments below it, metre by metre, such as glacial till over
northern Europe, loess in China and on the Danube plains, laterite in the tropics, salt crust and gypsum crust in
deserts, frozen ground in the Arctic. The sea floor gets its own sediment, and beaches their sand, gravel or rock.
Around the Baltic (20–29° E, 53.5–60° N) the real bedrock layers continue down to the crystalline basement. The first
ground-column world on a machine downloads Earth's finer ground data (about 172 MB, kept for every world).

How real depth becomes blocks is the world's ground depth (`earthGroundDepth` in its world configuration, no longer on
the Earth tab). Every curve starts at the terrain's own scale, so digging down is no deeper than the hills are high, and
every layer at least half a metre thick keeps at least one block. The default, **Log, strong**, squeezes thick deep
layers so that 2,000 m of ground sits about 30 blocks above the bottom of the world under lowland, with basement rock
below.

### Earth's ground blocks

With ground columns you'll meet blocks vanilla doesn't have:

| Block | What it is | Where |
|---|---|---|
| Blue-grey till, Reddish-brown till | Clay, sand and stones left by the ice sheets | The glaciated north of Europe and North America |
| Frozen silt, till, sand, gravel, clay | Permafrost | The Arctic and high mountains |
| Loess | Wind-blown silt, deep and fertile | China's Loess Plateau, the Danube and Ukrainian plains, the Midwest |
| Silt, Colluvium, Weathered rock | Flood silt, slope rubble, rock rotted in place | Valleys, hill slopes, old land |
| Saprolite, Red saprolite | Rock weathered deep in warm wet climates | The tropics |
| Mottled laterite, Ironstone crust | Red tropical ground and its hard iron crust | The tropics |
| Terra rossa | Red clay soil on limestone | Warm karst (a yearly mean of 12 °C or more) |
| Calcrete, Gypcrete, Salt crust | Lime, gypsum and salt crusts | Dry lands and salt flats |
| Grey-green marl, Lake marl | Limy clay | The Baltic's Devonian beds, old lake beds |
| Peat with bog iron | Bog peat with iron ore in it | Bogs (only with Geology Additions installed) |
| Tephra | Volcanic ash | On volcanic soils. Fresh ash also lies around every active volcano on land, in every Earth world. |
| Chalk ooze, Siliceous ooze | Deep-sea sediment | The ocean floor |
| Gold-, tin- and platinum-bearing sand and gravel | Placer deposits | River and outwash sand and gravel; with the ore maps, weighted to the real gold, tin and platinum districts |

Placer sand and gravel can be panned like vanilla's sand and gravel. The crusts, marl, calcrete, laterite's ironstone
crust and the rock answer a prospecting pick; loose cover such as till, loess and silt doesn't, just as vanilla's soil
and gravel don't.

### Everyday materials with ground columns

- Vanilla's random patches of clay, peat, gravel and soil are off. Clay and peat come as layers where the ground data
  maps them.
- Hard-rock ores lie in the bedrock under the cover, not in the cover itself; where the cover is thick, dig down.
- Salt lies in evaporite beds and under salt flats.
- Bauxite, gypsum, natural fire clay (in saprolite) and placers are laid with the ground, whatever the Ores choice.

## Soil

**Vanilla:** soil fertility comes from rain and temperature by a formula made for vanilla's own climate maps. On real
climate data that formula turns a lot of farmland into sand (olive groves, wheat fields and steppe pasture), gives most
of Europe low fertility soil, and gives the warm, wet tropics medium soil although their real soils are among the
poorest.

**Earth:** **Terrain · Soil** on the Earth tab. The rocks underneath are the same with every choice.
- **Earth soil, realistic tiers** (default): the topsoil follows the real soil map (SoilGrids' soil classes), in five
  tiers: **barren** on the poorest soils (thin soil on rock, dune sand and other sands, peat, salt and gypsum soils,
  the tropical laterites), **low** (among them the podzols of the northern forests), **medium** (most of central
  Europe), **high fertility soil**, and **black earth** (the game's terra preta) on the chernozems of the steppes.
- **Earth soil, vanilla tiers:** the same map in the game's barren, low, medium and high fertility soils, with barren
  only on inhospitable land (dunes, salt and gypsum soils, cold ground).
- **Climate only:** the game's own soil from rain and warmth, with one rule of Earth's: land that is semi-arid or wetter
  (real rainfall at least a fifth of what the land could evaporate) keeps at least low fertility soil, so the
  Mediterranean, the steppes and the dry savannas have soil, grass and farmland. True deserts keep vanilla's sand
  (warm) and gravel (cold).

With either Earth soil:
- **Deserts** have no topsoil (sand or gravel), except where a river floods the land: the Nile and Mesopotamia are
  fertile across their floods and bare beyond them.
- Ground beyond the tree line, on mountains and in the polar lands, is barren.
- Breaking the **forest floor** gives the soil beneath it, at that place's fertility, instead of always low soil.
- The game still lays its own grass, subsoil and soil depth.
- The first such world on a machine downloads the soil map (21 MB, kept for every world).

A world made with ground columns keeps its own soil whatever this option says. With ground columns, arid land shows the
top of its own cover (sand sheets, silt, fan gravel, salt crust, weathered rock), and the floodplains of desert rivers
keep their soil. Also with ground columns:
- Naturally black, fertile ground (the chernozem of the steppes) is vanilla's **high fertility soil**.
- **Terra rossa** lies only on warm karst. Cool or high limestone has dark soil on limestone rubble, or bare rock.
- **Flint** lies loose in the till, outwash, river gravel and beach shingle of the lands the ice sheets covered; chert
  weathers out of limestone almost everywhere.

## Ores

**Vanilla:** ore deposits by the game's own rules, in whatever rock lies there. Many deposits are placed at a height
made for vanilla's landscape, measured from sea level, and grow richer in a taller world.

**Earth:** **Terrain · Ores** on the Earth tab has one value, **Vanilla ore deposits, plus Earth's gems and ores**:
- The game places its own deposits, each as deep below the real ground as below land at sea level in a standard vanilla
  world. So a plateau has a lowland's ores within reach: iron, coal and the rest under Addis Ababa, Mexico City or
  Lhasa. The prospecting pick reads them where they lie.
- In tall worlds deposits are as rich as in a standard vanilla world, not multiplied by the world's height: lowland at
  1:10 (1024 blocks high) has about a quarter of the ore vanilla's height rule would give it, at 1:100 about two
  thirds. A server that wants more raises the game's own deposit rate (`globalDepositSpawnRate`), as in vanilla.
- Earth adds ores and gems the game doesn't place, by the game's own deposit rules, in whatever rock the rock choice
  gives.
- With regional rock, **galena and its silver** also form in granite, slate, phyllite, gneiss and schist, as at
  Freiberg, Kongsberg, the Harz and Cornwall, besides vanilla's sedimentary rocks.

**Earth's ore maps** (ores richest in their real districts, from real geology and mining records, in suitable host
rocks; copper, tin, iron and coal also, more thinly, in suitable rock everywhere; uranium and the game's corundum ore
too) are not on the world creation screen: they are being reworked, and are there only for development
(`ExperimentalOreMaps` in `earth.json`, see [Settings](Settings)). Worlds made with them keep them.

### Earth's ores and gems

| Ore | Smelts to | Pickaxe |
|---|---|---|
| Chalcopyrite, Tetrahedrite, Azurite | Copper | Copper |
| Cerussite, Vanadinite, Wulfenite | Lead | Copper |
| Smithsonite, Hemimorphite | Zinc | Copper |
| Sperrylite | Platinum (see below) | Steel |
| Native platinum (from platinum placers, ground columns only) | Platinum (see below) | Panning |
| Wolframite (tungsten) | Nothing yet | Iron |
| Pyrite | Grinds to sulfur powder | |

Break the chunks into nuggets with a hammer in the crafting grid, as with vanilla's ores, then smelt the nuggets.
Platinum melts at 1,770 °C, hotter than any vanilla fire, and nothing uses it yet.

**Kyanite, sillimanite and andalusite** (ground-column worlds only; copper pickaxe) are heat-resistant minerals from
metamorphic rock: they stand in for bauxite in refractory bricks (see [Progression](Progression)).

The gems are **beryl, ruby and sapphire (corundum), garnet, spinel, topaz and tourmaline**, in low, medium and high
grade, in the rocks they form in. They need a bronze pickaxe and drop rough gems; no recipe uses them yet.

## Prospecting

The prospecting pick works as in vanilla: break rock blocks and it reads the ores around (its density search needs
three samples, as in vanilla). With ground columns, the deep loose cover (till, loess, silt, weathered rock) can be tens
of blocks thick in some places, and the pick reads nothing in it. Dig down to rock, or to one of the crusts or marls,
and prospect there.

## Rock floor

**Terrain · Rock floor** on the Earth tab: how far below the lowest ground in each 32 × 32 area the ground turns to plain
rock, with no caves, ores or lava. It keeps the saves of tall worlds small. **Automatic** (default) is 160 blocks in
worlds taller than 512 (the 1:10 play style) and none otherwise.
