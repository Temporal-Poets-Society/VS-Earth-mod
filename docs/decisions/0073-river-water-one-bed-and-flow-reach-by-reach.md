# 0073: River water as one bed, open side channels, and still or flowing water reach by reach

Status: **built** on `claude/river-flow` for 2.0.0 (EA-0048, EA-0049, EA-0051, EA-0052; the author's report on rc.9 at 1:10,
13 November 2026). New worlds only: channel rules 4 (`EarthParams.ChannelRules`, `TerrainModel.FlowChannelRules`); older
worlds keep the rules they saved (0002). Date: 2026-11-13.

## What the author reported

On rc.9 at 1:10, 13 November: "i said to inspect the upstream and tributaries depth map. Here at Daugava and Ķīšezers
connection is weird bottom and patchy still and flowing water. There should not be really flowing water from the lake.
Again this is question of all rivers. Kundziņsala island small water flow around is blocked and now has dead end flow."
And: "Flow model needs [to be] systematic built or prebuilt to make water not flow in dead ends if they form and also get
correct local flows around bends, changes etc." At 57.0245° N 24.0930° E the bed showed square and round pits, a raised
flat patch and straight edges.

## What the probe found

The depth-and-flow probe of 0071 (a world's model rebuilt offline from its saved parameters; each column's rule; now also
the flow labels the blocks get, still water grey, and the flow file's speed), on the bench's 1:10 world with this build's
rules, at the Daugava from Riga past Kundziņsala to the sea, then at 13 more places.

1. **The square, the straight edges and the triangle** (Ķīšezers' outlet into the Daugava): the mask's river water is as
   deep as the courses near it make it, and beyond their reach as deep as the *nearest* course's flow (0069 rule 3,
   channel rules 3's `MaskWaterDepth`). Between the Daugava (729 m³/s, 7 blocks) and Ķīšezers' outflow (course 5065,
   22 m³/s, 1 block; the lone depth 3 at least) the nearest course changed along the straight line halfway between them:
   a 7-block bed against a 3-block one along a ruler line, and at the reaches' ends a triangle.
2. **Clipped courses**: a course the mask widens (channel rules 2, up to 3 times its flow's width) was indexed and
   looked up by its flow's reach: its banks, valley and depth blend ended along an axis-aligned box and a line along the
   course.
3. **The raised flat patch**: a small course in wide water (Ķīšezers' outflow, 1 block deep) made the water shallower
   along its line (the courses' blend lets a shallower course pull the depth down).
4. **The round pit**: a pool the water mask has as lake (a few pixels) inside the river's water where the Mīlgrāvis joins
   the Daugava, a block below the river's level, with the river's bed shelving to it as to a shore: a ring.
5. **Kundziņsala**: the Sarkandaugava, the side channel east of the island, is two narrow pieces in the mask. The flow
   bake solved the river's flow through them (4-6 cm/s), but the game drops narrow pieces where streams go down to
   20 m³/s (0063: the courses draw those), and these two joined the wide water in one place each: the channel was blocked
   1.6 km short and both halves carried flow into a dead end.
6. **Grooves and walls**: in the mask's wide water the course's own channel profile was cut deeper than the water
   round it, with walls for sides (the whole Danube at Vienna, 2,160 steps of 3 blocks or more in a 1:10 map), and where
   the mask narrows a big river its channel is deepened up to twice the flow's depth: the Pannerdensch Kanaal 13 blocks
   wide and 15 deep, its sides 2-3 blocks a step (0063 channel rules 1).
7. **Patchy still and flowing water**: water slower than 5 cm/s was still, pixel by pixel. The Mīlgrāvis (22 m³/s through
   a channel 100-200 m wide) runs at 6-13 cm/s by the flow file and the lower Lielupe at 10-20: any threshold leaves dozens
   of still and flowing patches where the speed hovers round it (studied at 5-15 cm/s, smoothed over 0-20 blocks).

## Decision

Channel rules 4, new worlds:

1. **Courses reach as far as their water**: a piece the mask widens is indexed and found by its widened water, bank and
   valley (`RiverNetwork.SegReach`).
2. **One depth across wide water**: beyond the courses the mask's river water is as deep as the flows of every course
   within 2 km, blended by discharge over distance squared and easing out at the search radius (`ReachBlend`, `DepthNear`);
   the courses near only deepen it, so a shallow small course makes no ridge along its line.
3. **Small pools join the river**: a mask lake of at most 16 pixels inside or against the mask's river water, within three
   blocks of its level, is river water, and its edge is no shore for the river's bed (`JoinsRiver`).
4. **Side channels the flow passes through stay open**: a narrow piece the courses don't draw stays where the flow file
   carries the river's flow through a third of it or more (`FlowCarriesAt`, `LakeOutlineLayer.KeepWhere`).
5. **Still or flowing reach by reach**: each reach's speed is the median of the flow file's along its own course
   (`ReachSpeed`); its water flows when that is 15 cm/s or more (0069's first proposal). The mask's river water is still
   where it carries none of the river's flow (under 2 cm/s: dead ends, backwaters), or where the reaches near it, blended
   as above, are slow ones (`FlowingShare`) and the water within 200 m of it is no faster on average (`AroundSpeed`: slow
   water by a bank beside a fast core flows with it). A lake's outflow through a wide channel (the Mīlgrāvis) is still as
   a whole; a fast river flows as a whole. Hydrology reads the same through the river query (no flow where still).

6. **No groove in wide water**: where the flow is solved, the mask's water takes its one depth (which over a course's
   water is already the course's), and the course's channel profile only where the course comes into the mask's water
   from its edge, fading out a shore reach in.
7. **Channel sides no steeper than 2 blocks a block**: a channel's bed eases from its bank's depth to its middle's over
   70 % of its half width (40 % before), and is no deeper than that lets its sides be at 2 blocks a block (plunge pools
   as before). A wide river keeps its depth; a narrow deepened one (the Pannerdensch Kanaal: 15 → 11) gets a bed it can
   hold.

The flow file (0069's bake) is unchanged: no new data to publish. Its direction field already follows the banks; the colour
patches by direction are EA-0050's (the drawing side).

## Checks

**Depth and flow maps** (the probe of 0071 with flow labels and the flow file's speed; the bench's rc.7 worlds `rc7-fish`,
1:10, at 2 blocks a pixel, and `review1111`, 1:100, at 1; before = trunk `5c84cf9` with today's rules, after = this
build with channel rules 4). "Bed steps": neighbouring river columns at one water level whose depths differ by 3 blocks or
more (at 2 blocks a pixel a step of 3 is 1.5 a block). "Boundary": neighbouring river columns one still and one flowing.
"Patches": still or flowing pieces under 100 pixels.

| Place | Scale 1: | Bed steps | Still/flowing boundary | Small patches | Still % |
| --- | --- | --- | --- | --- | --- |
| riga-daugava | 10 | 150 → 81 | 1674 → 605 | 131 → 43 | 28 → 29 |
| lielupe | 10 | 260 → 14 | 1755 → 521 | 88 → 24 | 9 → 23 |
| gauja-sigulda | 10 | 432 → 50 | 0 → 0 | 0 → 0 | 0 → 0 |
| gauja-mouth | 10 | 77 → 27 | 0 → 0 | 0 → 0 | 0 → 0 |
| rhine-pannerden | 10 | 982 → 639 | 312 → 249 | 62 → 44 | 12 → 15 |
| danube-vienna | 10 | 2160 → 4 | 78 → 23 | 25 → 5 | 13 → 13 |
| danube-budapest | 10 | 836 → 355 | 20 → 0 | 6 → 0 | 0 → 0 |
| elbe-hamburg | 10 | 219 → 58 | 939 → 526 | 85 → 23 | 42 → 42 |
| rhone-geneva | 10 | 499 → 227 | 0 → 0 | 1 → 1 | 0 → 0 |
| neva-ladoga | 10 | 107 → 83 | 494 → 97 | 66 → 23 | 5 → 1 |
| nile-khartoum | 10 | 642 → 56 | 948 → 568 | 61 → 22 | 11 → 4 |
| mississippi-ohio | 10 | 316 → 26 | 672 → 56 | 191 → 13 | 1 → 0 |
| amazon-negro | 10 | 631 → 517 | 1486 → 743 | 104 → 35 | 3 → 3 |
| loire-tours | 10 | 254 → 170 | 35 → 0 | 5 → 0 | 1 → 0 |
| riga-daugava | 100 | 0 → 51 | 556 → 409 | 70 → 39 | 10 → 8 |
| lielupe | 100 | 3 → 2 | 297 → 386 | 52 → 4 | 2 → 6 |
| gauja-sigulda | 100 | 1 → 0 | 0 → 0 | 0 → 0 | 0 → 0 |
| gauja-mouth | 100 | 0 → 0 | 1 → 0 | 1 → 0 | 0 → 0 |
| rhine-pannerden | 100 | 196 → 89 | 108 → 124 | 30 → 25 | 3 → 4 |
| danube-vienna | 100 | 514 → 13 | 110 → 100 | 37 → 35 | 11 → 11 |
| danube-budapest | 100 | 82 → 54 | 73 → 64 | 5 → 3 | 2 → 2 |
| elbe-hamburg | 100 | 6 → 3 | 354 → 297 | 44 → 33 | 26 → 25 |
| rhone-geneva | 100 | 75 → 5 | 13 → 17 | 5 → 6 | 1 → 1 |
| neva-ladoga | 100 | 13 → 12 | 89 → 34 | 23 → 14 | 2 → 1 |
| nile-khartoum | 100 | 19 → 39 | 259 → 149 | 22 → 22 | 3 → 1 |
| mississippi-ohio | 100 | 146 → 149 | 92 → 56 | 15 → 8 | 0 → 0 |
| amazon-negro | 100 | 115 → 59 | 348 → 207 | 46 → 36 | 2 → 1 |
| loire-tours | 100 | 48 → 8 | 20 → 20 | 3 → 3 | 0 → 0 |

- The junction of the author's report (57.010-57.045° N, 24.06-24.13° E, 1 block a pixel): bed steps 127 → 0, boundary
  2,115 → 524, small patches 84 → 20; the square, the triangle, the straight edges and the ring are gone, the Mīlgrāvis is
  still as a whole and meets the flowing Daugava along one line.
- Kundziņsala: the Sarkandaugava is one open channel from the Mīlgrāvis to the Daugava, still (4-6 cm/s by the flow file).
- What remains: the steepest sides of narrow deep channels (the Rhine at Pannerden, the Danube at Budapest, the Amazon:
  at 2 blocks a pixel), a few hundred steps where a river meets another at 1:100 (the Riga and Khartoum boxes), and the
  colour patches by flow direction, which are EA-0050's.

**Bench** (AgentBridge, Earth `2.0.0-rc.9-flow.3` at 1:10 and `-flow.4` at 1:100 with Cylinder World 1.1.0-rc.3 and
Hydrology 0.6.2-rc.6): the author's own view at Ķīšezers' outlet (57.0245° N 24.0930° E, y 222, heading NNE) without the
pits, patch or edges; the Sarkandaugava open round Kundziņsala; the Danube at Vienna and the Rhine at Pannerden with
smooth beds; the Mississippi and the Ohio at 1:100 meeting in contours a block apart. Screenshots in the suite's
`backlog/media/EA-0048-*`, `EA-0049-*`, `EA-0051-*`, `EA-0052-*`.

**Tests** (on the trunk 5c84cf9 plus this): RiverWaterTests 39, ModelCacheTests 133, RiverIndexTests, EarthWorldOptionsTests
757 and EarthMapTests (396) pass.

## History

- 2026-11-13: built for EA-0048, EA-0049, EA-0051 and EA-0052.
