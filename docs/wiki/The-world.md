# The world

How big an Earth world is, how tall, where sea level is, and how the world wraps around. Climate is on
[Climate and seasons](Climate-and-seasons); the land itself is on [Terrain](Terrain).

## The scales

**Vanilla:** you set the world's width and length in blocks, and the land is made up by noise.

**Earth:** you choose a **scale**, and the world is the real Earth at that scale. Each Earth play style is one scale,
and it sets the world's size and height for you:

| Play style | One block is about | World (east-west × north-south) | World height | Mountains |
|---|---|---|---|---|
| Earth · 1:200 | 200 m | 233,472 × 100,352 blocks | 384 | compressed |
| Earth · 1:100 | 100 m | 433,152 × 200,192 blocks | 384 | compressed |
| Earth · 1:50 | 50 m | 834,560 × 400,896 blocks | 384 | compressed |
| Earth · 1:25 | 25 m | 1,635,328 × 801,280 blocks | 512 | in proportion |
| Earth · 1:10 | 10 m | 4,040,704 × 2,003,968 blocks | 1024 | in proportion |

A sixth scale, **1:5** (5 m a block), is only on the Earth tab (**Map · Earth size**), not a play style of its own. It
keeps the play style's world height and mountain heights, and its server needs about 7 GB of memory while it starts.

The east-west size includes 16,384 hidden blocks of sea beyond each end of the jump line. The way round the world is
twice its north-south length: 400,384 blocks at 1:100.

What the scale means for you:
- **Distances.** At 1:100, London to Paris is about 4,000 blocks; at 1:10 it's ten times that. Coarse scales make a
  world you can cross; fine scales make every valley and village-sized island real, but a continent is a long journey.
- **Detail.** The finer the scale, the more of the real land shows: small islands (any island at least 4 blocks wide is
  there), narrow fjords, small rivers. At 1:25 and finer, Earth also draws the streams down to the smallest brooks,
  1 m³/s (**Terrain · Smallest stream**); at 1:100 and 1:200 the play styles draw rivers only.
- **Cost.** Finer scales need more disk and memory and take longer to start. See
  [Data and performance](Data-and-performance).

The map is a simple grid of latitude and longitude: north-south distances are true everywhere, but east-west distances
stretch toward the poles (twice as wide at 60° north or south). Every meridian is a straight north-south line in the
world, so a compass course is easy to follow.

## Height and sea level

**Vanilla:** sea level sits at about 43 % of the world's height, and mountains are as tall as the noise makes them.

**Earth:** sea level is a fixed number of blocks above the bottom: 110 by default (**Map · Sea level** offers 110, 128 or
160). The sea floor below it follows the real ocean: shallow over the continental shelves, deep in the open ocean.
Above sea level, the real height of the land is mapped into the blocks left:
- At **1:200 to 1:50** (384 blocks high), heights follow a curve that keeps low land lively and squeezes high mountains,
  so the lowlands have relief and Everest still fits under the top of the world.
- At **1:25 and 1:10** (512 and 1024 high), heights are in proportion: twice as high is twice as many blocks, and
  Everest stands near the top of the world.
- **Map · Mountain heights** and **Map · World height** on the Earth tab change this. See [Settings](Settings).

Real summits of 3,000 m and more are lifted to their surveyed heights where the height data rounds them off.

## Latitude and the ring

An Earth world always runs on Cylinder World's ring. The middle of the world's north-south length is the equator; the
north edge is the North Pole and the south edge the South Pole. Every block row has its real latitude, so the sun's
height, the length of the day, the seasons in both hemispheres, midnight sun and polar night are where they really are.
See [Climate and seasons](Climate-and-seasons).

## The seam

**Vanilla:** a world has edges. Walk to one and you stop.

**Earth (with Cylinder World):** the world wraps east to west. Walk, ride, sail or fly far enough east and you arrive in
the far west, and the other way round, without a loading screen or a fall. Earth cuts the globe along the meridian at
169.25° W, which runs through the **Bering Strait** between Asia and Alaska and then through the open Pacific to
Antarctica. The real Earth runs across the line with no extra strip of sea; since the line runs through the strait and
the open ocean, most crossings are by sea. Land on the line continues across: St. Lawrence Island in the Bering Sea, and the Antarctic ice south of about 78.5° S.

What it means for you:
- **Crossing is seamless.** On foot, flying and by boat, the jump keeps your height, your speed, your health and your
  seat in a boat, and the land ahead is loaded when you arrive. On a boat you may feel a short tug at the jump.
- **Building near the seam.** In survival you can't place or break blocks in the strip beside the jump line (Cylinder
  World protects it; creative players can). The strip exists twice, and what changes in one copy, chests included,
  shows in the other.
- **Mist.** Cylinder World can hide the jump in sea mist (its **Seam mist** world option, off by default; admins can
  turn it on with `/worldconfig cylinderSeamMist true`; it applies from the next start of the world or server).
- Not yet checked: crossing with several players at once, and crossing in a ship from a ship mod.

## The ice walls

**Vanilla:** no poles; the world's north and south edges are just edges.

**Earth (with Cylinder World):** both poles are tall **walls of ice** along the north and south edges
of the world. In front of the north wall lies the frozen Arctic Ocean; at the south wall, Antarctica's ice sheet reaches
it. The walls are Cylinder World's: see its wiki for how they look and are made.

What it means for you:
- The walls can't be crossed. A survival player who climbs onto one or gets behind it is put back in front of it.
- In survival you can't mine the wall, blow it up, or build within 32 blocks of it. Creative players can.
- The polar regions are cold: about −60 °C on the Antarctic plateau in winter. Dress for it.
- Cylinder World's wiki explains the cold near the walls, the lifeless last stretch before them and the sea ice.
