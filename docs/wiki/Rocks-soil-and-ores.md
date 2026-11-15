# Rocks, soil and ores

What the ground is made of, and where the ores are. Earth gives you a choice: keep vanilla's rocks and ores on Earth's
landscape, or build the ground from real geology.

## Rocks and ground

**Vanilla:** rock layers come from noise: bands of granite, limestone, basalt and the rest, wherever the noise puts
them, with soil, sand or gravel on top by climate.

**Earth:** **Terrain · Rocks and ground** on the Earth tab:
- **Vanilla rock layers** (default): the game makes its own rock layers under Earth's landscape, as in any vanilla
  world. Soil still follows Earth's real climate (below).
- **Ground columns: real soil, sediments and rock.** The rock is the real rock of each region, from real geological maps:
  limestone where the land is limestone, granite where it is granite, basalt in Iceland. Over it, the ground is built from real data:
  the soil by its real soil type, and the real sediments below it, metre by metre, such as glacial till over northern
  Europe, loess in China and on the Danube plains, laterite in the tropics, salt crust and gypsum crust in deserts,
  frozen ground in the Arctic. The sea floor gets its own sediment, and beaches their sand, gravel or rock. Around the
  Baltic (20–29° E, 53.5–60° N) the real bedrock layers continue down to the crystalline basement.
- **Regional rock regions only:** the real rock regions and surface materials, without the deep ground columns.

The first ground-column world on a machine downloads Earth's finer ground data (about 172 MB, kept for every world).

With ground columns, **Terrain · Ground depth** decides how real depth becomes blocks. Every choice starts at the
terrain's own scale, so digging down is no deeper than the hills are high, and every layer at least half a metre thick
keeps at least one block. The default, **Log, strong**, squeezes thick deep layers so that 2,000 m of ground sits
about 30 blocks above the bottom of the world under lowland, with basement rock below. The other choices are test
choices; see [Settings](Settings).

### Earth's ground blocks

With ground columns or regional rocks you'll meet blocks vanilla doesn't have:

| Block | What it is | Where |
|---|---|---|
| Blue-grey till, Reddish-brown till | Clay, sand and stones left by the ice sheets | The glaciated north of Europe and North America |
| Frozen silt, till, sand, gravel, clay | Permafrost | The Arctic and high mountains |
| Loess | Wind-blown silt, deep and fertile | China's Loess Plateau, the Danube and Ukrainian plains, the Midwest |
| Silt, Colluvium, Weathered rock | Flood silt, slope rubble, rock rotted in place | Valleys, hill slopes, old land |
| Saprolite, Red saprolite | Rock weathered deep in warm wet climates | The tropics |
| Mottled laterite, Ironstone crust | Red tropical ground and its hard iron crust | The tropics |
| Terra rossa | Red clay soil on limestone | Warm karst: the Mediterranean and the subtropics |
| Calcrete, Gypcrete, Salt crust | Lime, gypsum and salt crusts | Dry lands and salt flats |
| Grey-green marl, Lake marl | Limy clay | The Baltic's Devonian beds, old lake beds |
| Peat with bog iron | Bog peat with iron ore in it | Bogs |
| Tephra | Volcanic ash | Around volcanoes and on volcanic soils |
| Chalk ooze, Siliceous ooze | Deep-sea sediment | The ocean floor |
| Gold-, tin- and platinum-bearing sand and gravel | Placer deposits | Sand and gravel in gold, tin and platinum regions |

Placer sand and gravel can be panned like vanilla's sand and gravel. The crusts, marl, calcrete, laterite's ironstone
crust and the rock answer a prospecting pick; loose cover such as till, loess and silt doesn't, just as vanilla's soil
and gravel don't.

## Soil

**Vanilla:** soil fertility comes from rain and temperature by a formula made for vanilla's own climate maps. On real
climate data that formula turns a lot of farmland into sand: olive groves, wheat fields and steppe pasture.

**Earth:** every new Earth world, whatever the rock choice, keeps soil on land that is semi-arid or wetter (real
rainfall at least a fifth of what the land could evaporate), so the Mediterranean, the steppes and the dry savannas
have soil, grass and farmland. True deserts keep vanilla's sand (warm) and gravel (cold).

With ground columns:
- Naturally black, fertile ground (the chernozem of the steppes) is vanilla's **high fertility soil**.
- **Terra rossa** lies only on warm karst. Cool or high limestone has dark soil on limestone rubble, or bare rock.
- **Flint** lies loose in the till, outwash, river gravel and beach shingle of the glaciated north, as the ice spread it
  from the chalk of Denmark and the North Sea; chert weathers out of limestone almost everywhere.

## Ores

**Vanilla:** ore deposits by the game's own rules, in whatever rock lies there.

**Earth:** **Terrain · Ores** on the Earth tab:
- **Vanilla ore deposits, plus Earth's gems and ores** (default): the game places its own deposits, and Earth adds ores
  and gems the game doesn't place, by the game's own deposit rules, in whatever rock the rock choice gives.
- **Earth ore maps (experimental):** ore districts from real geology and mining records, in suitable host rocks. It also places deposits the game never
  places (uranium, bauxite, gypsum). This choice is being reworked; expect changes.

### Earth's ores and gems

| Ore | Smelts to |
|---|---|
| Chalcopyrite, Tetrahedrite, Azurite | Copper |
| Cerussite, Vanadinite, Wulfenite | Lead |
| Smithsonite, Hemimorphite | Zinc |
| Sperrylite, native platinum | Platinum |
| Wolframite (tungsten) | Nothing yet |

Break the chunks into nuggets with a hammer in the crafting grid, as with vanilla's ores, then smelt the nuggets.

**Kyanite, sillimanite and andalusite** are heat-resistant minerals from metamorphic rock: they stand in for bauxite in
refractory bricks (see [Progression](Progression)). **Pyrite** is there too.

The gems are **beryl, corundum (ruby and sapphire), garnet, spinel, topaz and tourmaline**, in low, medium and high
grade, in the rocks they form in. They drop rough gems; no recipe uses them yet.

## Prospecting

The prospecting pick works as in vanilla: break a rock block and it reads the ores around. With ground columns, the
deep loose cover (till, loess, silt, weathered rock) can be tens of blocks thick in some places, and the pick reads
nothing in it. Dig down to rock, or to one of the crusts or marls, and prospect there.

## Rock floor

**Terrain · Rock floor** on the Earth tab: how far below the lowest ground in each 32 × 32 area the ground turns to plain
rock, with no caves, ores or lava. It keeps the saves of tall worlds small. **Automatic** (default) is 160 blocks in
worlds taller than 512 (the 1:10 play style) and none otherwise.
