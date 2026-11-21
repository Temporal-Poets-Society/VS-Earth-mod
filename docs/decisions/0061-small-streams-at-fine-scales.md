# 0061: Small streams in 1:25 to 1:10 worlds

Status: **in Earth 2.2.0** (21 November 2026); agreed (the author, 17 November 2026: build the streams layer now, at 1 m³/s, for a public
no-wipe 1:10 server before Christmas 2026; EA-0131), built on `claude/streams-layer` and merged with EA-0148's water on
GLO-90's grid (decisions 0092, 0093) into `claude/terrain-2.2` (18 November 2026), re-baked there on courses rules 5, for
the author's test in game (test builds 2.2.0-rc.1 and rc.2); seen on the AgentBridge bench twice. The quick step below (every stream from 20 m³/s at 1:25 and finer) is **built** at the author's choice
("Quick step first", 8 November), in Earth 2.0.0. Date: 2026-11-08 (proposal), 2026-11-17 (agreed).

## The author's choices (17 November 2026)

- **Smallest stream 1 m³/s** (the author: "isnt 0.2 m³/s a tiny stream? More or less we are looking still 5-10m minimum
  width irl?"): about 8 m wide by Earth's width rule (8·√Q m), so a one-block stream at 1:10 is close to its real width.
- **Summer-dry streams left out** (the author, relayed: "Yes, leave them out"): a reach GIRES v1.0 has stopping 30 days
  a year or more is left out where dryness stops it, kept where frost does (the game freezes it itself); no dry beds.
- **Scales:** 1:25 to 1:10, new worlds only, under a new rules version saved with the world (`EarthParams.StreamRules`).
- **Hosting:** the stream file in Earth's GLO-90 data release (`earth-glo90`), fetched with the heights; the upload
  waits for the author's yes.
- **Banks match the land** (the author, after the first bench: "Yes, match the land"): on gentle ground a stream's banks
  keep the land's own surface (grass, soil, sand), with bare stone or gravel only in steep gullies.
- **Why now:** a public no-wipe server on a 1:10 Earth world launches before Christmas 2026; its world keeps its launch
  terrain, so streams added later would stop at the edge of explored land. Go/no-go around 10 December 2026: without an
  in-game proof at 1:10 by then the server starts without the layer.

## The streams layer as built (EA-0131, in progress)

**Data** (`tools/streams/`, run in this order; output in `C:\VS mods\work\streams-layer`):
1. `extract_hydrorivers.py`: HydroRIVERS v1.0's attributes as arrays.
2. `build_stream_input.py`: the reaches from 1 m³/s up to 20 m³/s (1,764,198), without the 284,613 that GIRES has dry
   30 days a year or more where the coldest month (CHELSA v2.1, January or July) is -10 °C or warmer (143,472 frozen
   ones kept): 1,479,585 streams, 8.2 million vertices, in flow order; then the large rivers as new worlds lay them on
   GLO-90 before the side arms (courses rules 5: `rivers-glo90.bin` with `rivers-glo90-snap-3.bin` applied by the game's
   own reader, written by `tests/StreamsTests -- <root> courses <out>`; `--base` the store's `rivers.bin`), linked by
   HydroRIVERS' junctions (each of the 154,309 outlets on a large river matched a
   rivers.bin reach exactly). `--keep-dry` keeps the summer-dry ones.
3. `tools/RiverSnap` with `frozenfrom=<stream count>`: the streams snapped onto GLO-90's valley floors, the large rivers
   held where they are (a station at each vertex, at its level), so each stream ends on its river's snapped course.
   Narrower searches than the large rivers' (`passes=500:8:30:2:150:600,200:4:15:1:100:300`: about ±0.5 km, then
   ±0.2 km; ±2 km led streams falling into Hardanger's fjords onto the fjord's surface). Heights through
   FineElevation's sampler (pixel-is-point, as the game reads GLO-90; RiverSnap's self-test checks the two agree).
   3.5 minutes on 24 threads: the course moved a median 90 m (p99 659 m); the level within ±3 m of the floor at 80 % of
   the stations.
4. `bake_stream_tiles.py`: `streams-glo90-1.bin` (layout in `src/Core/StreamData.cs`): the 191,027 stream systems
   (trees down to their outlet: 154,309 at a large river, 24,078 at the sea or a sink, 12,640 where the data ends),
   each in the 1° tile of its outlet, each tile listing the home tiles whose systems come within 1.5 km of it.
   **64.6 MB** (12,766 tiles, 16.9 million vertices). Baked again on courses rules 5 (18 November 2026, after the merge
   with EA-0148): the same systems and reaches, 227 vertices more, 64,553,844 bytes, SHA-256 `37d36799…` (pinned in
   `StreamData`; the snap: course moved a median 90.5 m, p99 659 m, as before).

**Drawing** (`src/Core/StreamLayer.cs`, `TerrainModel.Streams.cs`):
- The world's own river network is not touched. A home tile's systems become a small `RiverNetwork` of their own when a
  column first asks near them (10-20 ms of network, plus the joins), with the world's river options
  (`EarthParams.RiverOptions`: pool layout, widths, channel rules) and every reach kept at the smallest stream's width,
  depth, bank and valley (`RiverBuildOptions.KeepAll`): one block wide and deep, a bank of 1.5 blocks and a valley of
  about three at 1:10. So the streams step, chute, fall (lip and plunge pool) and take rapids as the world's rivers do,
  and their banks are held at the water.
- **Levels fitted to the land** before a tile's networks are built (`StreamLayer.FitToLand`, against the land as drawn
  without the streams): the land sampled every 3 blocks along each piece of the course; a piece whose water line stands
  above it gets vertices every 9 blocks at the land's level (a plateau stream holds its level to the cliff's edge and
  falls there); no dams (a level at most the land's under it, never rising downstream, lowered further by the lag a
  stream's pools may have on its slope); no ridges (where the water would still lie more than 120 m under the land, the
  course crosses high ground the stream couldn't have cut, and the reach starts after it: the snapped courses are right in
  their valleys, but a few heads started in the next valley over, in the Dolomites at 46.42° N 11.87° E a head 477 m below
  the level its fit gave it, then a ridge 200 m above; 40 m cut 9 % of the reaches, ordinary V-valley incision; 120 m cuts
  0.1-1.7 %). Then the tile's network is laid out again, at most 4 times, with the vertices under any pool more than a
  block above the land brought down by that much.
- **Streams fall as soon as they lag 2 blocks** (`RiverNetwork.MaxLag`, `StreamMaxLag`; a world's own rivers keep
  `ChuteMaxDrop` 4): the pool layout's steps come no closer than 3 blocks, so on a valley falling faster than a block in 3
  a river lags up to 4 blocks above it before it falls. A one-block stream that did so stood on banks built up to 5
  blocks above the slope (the first bench, the Dolomites). Now it takes a short curtain with its lip and plunge pool:
  the cascades of a mountain brook.
- **Banks** (`TerrainModel.StreamBanksMatchLand`): a bank flush with the water is the shore strip's bare material only in a
  gully (cut 4 blocks or more into the land, or a reach falling a block in 10); elsewhere the land's own surface.
- A column takes the nearest stream of its tile's networks (its own and its visitors'), only where the world drew no
  water (rivers, lakes, the sea) and no ice; on a large river's bank or valley the stream only raises the land to its own
  bank. Each system is a basin of its own, so what a column gets doesn't depend on the order chunks come in.
- **Joins follow the water as drawn**, not a baked copy: a system ending at a river or the sea ends where its outlet reach
  first comes into that water as the world draws it without the streams (`TerrainModel.MainColumn`, sampled a block
  apart over its last kilometre: a stream crossing a river's 3-block channel between vertices 9 blocks apart hooked back
  to the old junction at Eidfjord before; a stream bound for a river ends at the sea too, where the world draws that
  river's end short of the junction: EA-0160's river ends rules 2 leave 283 reaches out past their mouths, which 17 of
  the 154,309 stream outlets on a large river join), at that water's level (fixed for the pool layout; levels below it raised to it); one
  that doesn't reach it goes on to the river's drawn course (within 1 km). So when EA-0148 moves the rivers and lakes by
  half a cell, the streams still meet them.
- `EarthParams.StreamRules` (ProtoMember 215): 1 for new worlds on full GLO-90 at 1:25 and finer whose streams are "all"
  (earth.json `StreamsLayer`, default true). `streams-glo90-1.bin` comes through the GLO-90 release files' policy
  (`earth-streams-on` / `-off`): a world that can't have it the first time goes on without the layer for good.
- The river query (Hydrology's, version 3) answers a stream's column with the stream's reach: its discharge and slope;
  the rapids, flow and span were already the column's.

**Offline proof so far** (`tests/StreamsTests probe`: a new 1:10 world's model with GLO-90, 512 or 1024 blocks square a
place, settled as the generator settles chunks):
- water standing beside lower dry land (a leak): 0 at every place (Lauterbrunnen, Grindelwald, Bolzano, Eidfjord, Odda,
  the Amata, the Brasla, and the six steepest snapped streams in Europe, falls of 200-264 m within 500 m);
- steps single blocks on gentle ground; on steep streams cascades of 2-3 block falls; the water at most 1 block above the
  land as drawn without it (one column at Odda's steepest face 3), the gentle places at or below it, cut in; banks raised
  at most 1-4 blocks; bare banks Lauterbrunnen 2 % of the shaped columns, the Amata none, the Dolomites 19 %;
- the Weisse Lütschine continuous up the Lauterbrunnen valley floor to Zweilütschinen; the Amata and the Brasla in their
  Gauja side valleys. Sigulda itself has no stream of 1 m³/s within 10 km (its ravines are smaller);
- the same columns whatever order they are asked in (two fresh layers, parallel forward against one thread backward
  after building another tile first: 0 of 65,536 columns differ at Grindelwald and at Eidfjord's confluence);
- a column about 1-2 µs dearer with the layer (2-3 µs without, per thread); a tile's first build 0.1-0.5 s.

**Every tile once** (`tests/StreamsTests probe <file> sweep=16`, before a merge: each home tile's land fit, joins and
network as the first column near it builds them, any exception failing it): 11,392 home tiles, 1,476,454 reaches, none
failed (17 November 2026, 3.8 minutes on 16 threads); 0.56 % of the reaches cut at a ridge, 166,645 given vertices to
follow the land, 5,887 tiles laid out again with vertices brought down under their pools. On courses rules 5 (18 November,
`claude/terrain-2.2`): 11,392 home tiles, 1,476,453 reaches, none failed, 0.56 % cut at a ridge, 168,002 refined, 5,884
laid out again, 3.5 minutes.

**On courses rules 5 with EA-0148 and EA-0160** (18 November 2026, `claude/terrain-2.2`, the re-baked file, a new 1:10
world's model on EA-0148's data): no leaks at any of eleven places (Lauterbrunnen, Grindelwald, Bolzano, Eidfjord, Odda,
the Amata, the Brasla, and EA-0148's Sandsfjorden, Näsijärvi, the Danube at Margaret Island and the Kiel Canal), the water
at most 1 block above the land as drawn without it (Odda's 3-block column gone), order independence (0 columns differ at
Grindelwald and Eidfjord); EA-0148's own probe at its four places gives the same water, heights and levels sample for
sample as on `claude/ea-0148-fix` (the streams don't change the world's own rivers and lakes); the 12 saved worlds
unchanged.

**Fitted to GLO-90 whichever thread asks first** (the author's 2.2.0-rc.2 world, 18 November 2026: the Border Esk stopped
6-7 km above Langholm, at 55.17° N 3.02° W, a stub left at its confluence). World Map's world chart samples the whole world
in its first minutes on threads that take the built-in heights (FineElevation.BuiltInOnly, by the thread's name); its
samples reached the layer, which built each tile there, fitted to the coarse land, and kept it for the session: the
chunks generated after it got those streams too. Fitted so, the Esk's narrow valley stood more than RidgeCutMetres above
its water: its reach into Langholm (8.8 m³/s) was cut at its vertex 50 of 56 and the one above it (7.6 m³/s) at 19 of 29,
167 ridge cuts in and around that 20 km window where GLO-90 gives none. The bench never saw it: it runs without World Map.
Now a thread on the built-in heights gets no streams and builds no tile (a chart of the whole world at 489 blocks a texel
shows none anyway), and a tile built while a read took the built-in heights where generation reads GLO-90 (a square not on
this machine, asked by a thread that may not fetch it: FineElevation.MissedReads) is never kept: that ask gets no streams
there, and threads that may not fetch don't build it again until a GLO-90 square arrives (chunk generation, which may,
always builds its own). `StreamsTests ... chart=55.175,-3.06,2048`: after the chart's thread asked first, 0 of the 8,322
stream columns dry and 0 columns differ from a fresh layer's (before: 2,141 dry, 36,781 differ); in StreamsTests' default
run at 1024 blocks. No rules version: before this the layer's tiles depended on which thread asked first, so there is no
one earlier layout to keep for worlds made with rc.1 or rc.2 (test worlds; new chunks there now get the streams as
specified, with seams against chunks made before).

**Gentle ground kept gentle, and inflows that cross** (the author's rc.2 world, 18 November 2026, EA-0165 and EA-0164:
near Skjolden a stream leaving a lake ran in a straight slot 15-25 blocks deep through flat meadow at 61.55° N 7.58° E,
and 3 km on two streams crossed in an X, one seeming to flow uphill). Found offline on GLO-90, not World Map's doing:
- *The slot*: the bake's levels fall smoothly from a lake's outlet towards the foot of the valley's step 900 m on, under
  the meadow before the step (779 m where the land is 839 m, 300 m from the lake), and the land fit only ever lowered a
  level. The stream's first vertices lie in the lake as drawn, where the fit left their baked levels alone (803 m by the
  shore against the lake's 838 m), so nothing after them could be higher. Now the fit's first pass raises each vertex to
  the land under it (in water the world draws, to that water's level) as far as the level upstream allows, never under
  the baked level; and a piece whose water line runs more than HoldBlocks (2) under the land along it gets a vertex every
  RefineStep blocks at the land, as one standing above it did. The stream leaves the lake at its level and runs level with
  the meadow (838 m against 839.6 m where the author stood) until the land falls.
- *Pools held under their levels*: the pool layout's backward bound lets a pool stand at most a block per RampLength
  blocks above the levels downstream, as a river comes down in single steps; a stream falling faster than 1 in 6 for long
  had its pools laid out from the bottom up far under its own levels (120 blocks below a lake at 28.37° N 84.78° E, a
  trench). A streams layer's network comes down in short falls as soon as it lags (MaxLag), so its bound no longer holds a
  pool under its own level except at a reach's end (its confluence's cap). On a cliff such a stream now falls as a curtain
  with its plunge pool where it used to be cut back into the cliff's top (Odda: two of 5 blocks or more, the largest 15;
  no water above the land by more than 1 there).
- *The X*: the snap lays each stream on its own valley line, and near a junction a tributary's could run over the other
  inflow's, an X and two channels side by side down to the junction. Two inflows of one junction whose courses cross before
  it now meet where they first cross: both end there, and the larger's course on from there goes to the reach below.
- Measured on every home tile (`StreamsTests ... sweep=16`: every 6 blocks along each laid-out course, the water against
  the land as drawn without the streams where that land rises less than 1 in 10 across 16 blocks; and the reaches whose
  courses cross another's away from a junction), rc.2's layer against this one. Water more than 4 blocks under gentle land: 430,051 of 112.8 million samples on 60,593
  of 1,476,453 reaches (4.1 %), the deepest 131 (the Nepal lake), against 328,601 on 36,616 reaches (2.5 %; 77,483 more
  than 8, 4,824 more than 16; 10,333 with the pools 5 or more under the reach's own levels, on 2,881 reaches), the
  deepest 44 (72.52° N 25.38° W). Reaches crossing another away from a junction: 14,676 (8,258 of them the two inflows
  of one junction) against 6,433 (118; of the rest, 4,847 a reach and the one it flows into; 253 of all lie in water the world draws, where
  no stream is drawn). The rest are small (looked at in New Zealand and Patagonia): a tributary overshooting its junction
  by a few blocks, a small knot at the confluence, and two streams of separate systems touching where the large river
  they both join begins; no X with a channel beside another as at Skjolden.
  Reaches cut at a ridge 8,199 against 6,630; no tile failed; 4.2 minutes on 16 threads.
- At Skjolden: no crossing left; the deepest water under gentle land in the 1 km round the slot 6 blocks (11 before, at the
  author's place), elsewhere in it. At the twelve probe places no leaks, the water at most 2 blocks above the land, the
  same columns whatever order they are asked in (also at Skjolden and the Nepal lake). No rules version, as above: these
  streams exist only in rc.1 and rc.2 test worlds.

**Headless server** (`tools/run_selftest.sh --style earth-10`, a new 1:10 world, 49 chunk columns at each of eight places in
the Alps, Norway and Latvia, with and without the layer at the same time on a shared machine): 68.4 s against 61.5 s
(+11 %, per place -11 % to +46 %, the most in Norway's valleys), tile builds included; generated terrain = model in
99.2-99.7 % of columns with and without; no water seams between chunks, no missing river ramps; stream step edges in the
chunks (8, 21, 2, 4 at four places); no errors in the log.

**Hydrology's headless lab** (its `river_poke` and `fall_poke` on a 1:10 world, Hydrology 0.6.2): a block put into the
stream and taken out again at Lauterbrunnen, a Dolomites cascade and the Amata leaves nothing behind; stones into a
5-block fall and out again (before the cascades): the fall as it was generated, except one source left hanging beside
Eidfjord's curtain (no longer a shape the streams make); no errors.

**First bench** (17 November 2026, 22:40-22:55, a new `preset-earth-10` world, Earth 2.1.4-streams.2, Hydrology 0.6.2,
Cylinder World 1.1.1; shots in `C:\VS mods\work\streams-layer\bench-*.png`): the Weisse Lütschine in its channel through
the Lauterbrunnen valley's terraces with steps of flowing water; a clean confluence at Zweilütschinen; Eidfjord's stream
stepping down its valley and running into the drawn river. Found: the Dolomites stream's pools on a wall of pale rock 2-5
blocks above the grass (fixed above), and gentle streams' grey banks reading as a path from above (the author's choice
above). The visit to the Amata stopped: after the teleport the server generated no chunks there (0 of 113 columns in 180 s,
no error; every server thread idle), a bench stall, as the same place generates in the headless servers.

**Second bench** (17 November 2026, 23:43-23:58, a new `preset-earth-10` world, Earth 2.1.4-streams.4 with the fixes
above; shots `bench2-*.png`): the Amata loaded at once, its stream in the side valley with grass to the water; at the
Dolomites' viewpoint of the first bench no wall, the stream in a shallow channel stepping with the slope, bare rock only
along its steeper upper gully; at Lauterbrunnen grass to the water through the valley's terraces. Dig test: the soil
block holding the Lauterbrunnen stream's side at its level broken as a player breaks it: the water filled the hole and
stopped at the soil beyond; after 30 s nothing had spread further, nothing ran down the valley. No errors in the log.

## What the author asked

The author, on 8 November 2026: "when done with large rivers and waters problems. Check possibility to add smaller
rivers. Now with detailed 1:10 map we see lot of dry rivers in mountain or hill valleys where rivers should be. This
would apply only to 1:25 - 1:10 maps."

## Why valleys are dry

- **The data stops at 20 m³/s.** `earthdata/rivers.bin` holds the HydroRIVERS reaches from 20 m³/s up: 619,723 reaches,
  about 2 million km. Its snapped copy on GLO-90 (`rivers-glo90.bin`) holds the same reaches.
- **By default a world draws even less.** `Streams` = `ends` draws the navigable rivers (from 100 m³/s) and each one's
  main branch upstream as a stream narrowing to one block at 20 m³/s. Tributaries of 20–100 m³/s off the main branch are
  left out unless a world uses `Streams` = `all`.
- **At 1:10 the land shows every valley.** On GLO-90 heights at 10 m per block, a valley with a catchment of a few km²
  is a clear landform hundreds of blocks long, but nothing in Earth's data puts water in it.

Two windows (GLO-90 relief with every HydroRIVERS reach coloured by discharge):
- **Bernese Oberland** ([map](../research/small-streams-2026-11-08/streams-oberland.png)): Earth draws only the Aare
  through the lakes of Thun and Brienz. The Lütschine (about 10 m³/s) is missing, so the Lauterbrunnen and Grindelwald
  valleys are dry. So are the streams of the hanging valleys (0.2–1 m³/s).
- **Gauja valley at Sigulda** ([map](../research/small-streams-2026-11-08/streams-sigulda.png)): Earth draws the Gauja.
  The Amata and the other side valleys carry HydroRIVERS streams of 0.2–20 m³/s that Earth leaves out. Many short
  ravines in the Gauja's walls have no HydroRIVERS line at all: their catchments are below its 10 km² minimum.

## The data on hand

HydroRIVERS v1.0 is already downloaded (`rawdata/hydrorivers`, 8,477,883 reaches; same licence and attribution as
today). Reaches added beyond today's 20 m³/s set, and an estimate of the snapped file's size at today's 12 bytes per km
(24 MB for 2 million km):

| Smallest stream | Added reaches | Added length | Estimated size |
| --- | --- | --- | --- |
| 1 m³/s | 1,764,198 | 7.0 million km | about 85 MB |
| 0.5 m³/s | 2,609,886 | 11.3 million km | about 140 MB |
| 0.2 m³/s | 4,225,924 | 18.2 million km | about 220 MB |
| 0.1 m³/s | 5,647,420 | 22.2 million km | about 270 MB |

HydroRIVERS' lines come from 15″ flow directions (about 450 m), so at 1:10 they run beside the valley floor in steps.
`tools/RiverSnap` already moves lines onto GLO-90's valley floors. It snapped all 620,000 large reaches in under a
minute, so millions of small ones should take minutes.

## Proposal

**1. A streams layer, only in worlds at 1:25 and finer** (new worlds; a rules version saved with the world):
- **Source:** the HydroRIVERS reaches below 20 m³/s down to a threshold. 0.2 m³/s is proposed: it fills mountain and
  hill valleys in wet climates, while desert wadis (long-term mean below it) stay dry.
- **Snapped** onto GLO-90 valley floors with RiverSnap, with their levels from GLO-90, as the large rivers are. HydroLAKES
  reservoirs are de-dammed as today.
- **Stored in 1° tiles** that travel with the GLO-90 heights (fetched with them, as the lake and river outline files
  are). Publishing to Earth's data release needs the author's yes.
- **Loaded by area.** The world generator reads the tiles it needs as chunks generate. The main river network stays as
  it is: building millions of reaches into it when a world starts would cost gigabytes and minutes.
- **Drawn simply:** a channel one block wide (two from about 5 m³/s at 1:10), its water at the snapped level, stepping
  down with the valley floor (Hydrology's rapids at the drops). It ends at the level of the river, lake or sea it flows
  into. No boats, no side arms or loop cutting.

**2. Optional, later: ravines.** Valleys under HydroRIVERS' 10 km² minimum would need streams traced from GLO-90 itself
(flow accumulation on the 90 m grid). Only if step 1 is not enough.

**A quick step on its own:** worlds at 1:25 and finer could default to `Streams` = `all`, which adds the 20–100 m³/s
tributaries that the data already has.

## Quick step (implemented, tall.22)

The author chose it first. What it does:
- **Setting:** earth.json's `Streams` defaults to `auto`. A new world under 37.5 m per block (1:25 and finer) saves
  `all`, every reach from 20 m³/s up; coarser worlds save `ends`. Configs with the old default `ends` move to `auto`
  (config version 5); `all` and `none` stay. Worlds keep the mode they saved.
- **Added at 1:10:** 215,249 reaches (about 730,000 km; 623,995 instead of 408,746) and 1.2 million segments (4,048,085
  instead of 2,820,862). Offline, the network alone builds in 4.4 s instead of 3.0 s. The model build of a new 1:10
  bench world took 34 s instead of 21–24 s.
- **What it fills:** side rivers of 20–100 m³/s that are not a navigable river's upper main branch: 570 more reaches in
  the Alps (45.5–48° N, 5.5–13.5° E), 358 in southern Norway, 125 in Latvia and Estonia. On the bench at 1:10:
  - the upper Adige in Val Venosta (23 m³/s, 867 m) is a stream about two blocks wide (`ends` follows the Eisack
    upstream from Bolzano);
  - the Lech below Reutte (28 m³/s, 864 m) is a stream two to three blocks wide with gravel banks;
  - the Aiviekste above Pļaviņas (82 m³/s) is a river about seven blocks wide in its valley.

  No Earth errors in the log.
- **What it does not fill:** valleys whose streams are under 20 m³/s, most of the author's dry valleys: the Gauja's
  side valleys at Sigulda get nothing, and in the Bernese Oberland only the lowest Kander (33 m³/s) is added; the
  Lütschine stays dry. These need the streams layer above.

## Questions for the author

- Threshold: 0.2 m³/s (proposed), 0.5 or 1 (smaller files, fewer streams).
- Scales: 1:25 to 1:10 as asked, or 1:50 too.
- Hosting: the stream tiles in Earth's GLO-90 data release (`earth-glo90`), downloaded with the heights.

## Risks

- **Levels on steep slopes:** a 1-block stream falling hundreds of blocks needs steps or waterfalls that hold their
  water. This is where the work is.
- **Line errors:** HydroRIVERS is wrong in places (karst, flat plains with braided streams, deltas); snapping fixes most
  in valleys and few on plains.
- **Generation cost** per chunk with many streams nearby; the per-area index keeps it local.
- **Dams and bridges:** small reservoirs below HydroLAKES' size stay flat water in GLO-90.

## History

- 2026-11-08: offline study at the author's request; written as a proposal. Maps from `rawdata/hydrorivers` and
  `rawdata/copernicus-glo90`.
- 2026-11-08 (morning): the author: "Quick step first". `Streams` = `auto` built in test build 1.0.3-tall.22
  (`aa11e30`): world options test passes up to its data-pack check (no publication zips in the worktree); offline
  network build at 1:100, 1:25 and 1:10; new 1:10 bench world (`t22a`): config moved to `auto`, streams `all`, upper
  Adige, Lech and Aiviekste with water, no errors. The streams layer below 20 m³/s is still a proposal.
- 2026-11-08 09:15: the author: "Yes, install". `earth-1.0.3-tall.22.zip` (sha256 `e2a53db8…`) installed in the player's
  Mods in place of tall.21; tall.21 and `earth.json` backed up in
  `Backups/earth-before-1.0.3-tall.22-20261108-091504`. Data packs unchanged. The player's `earth.json` (Streams
  `ends`, config version 4) moves to `auto` on the next start; existing worlds keep their rivers.

- 2026-11-18: `claude/streams-layer` merged with `claude/ea-0148-fix` into `claude/terrain-2.2` (the author: "merge into
  one branch to install and test ingame. After that we can merge to main"); the streams re-baked on courses rules 5's
  list (StreamsTests' `courses` mode), checked as above; test build 2.2.0-rc.1 installed in the author's game for the
  in-game test (by the coordinator, on the author's word). EA-0162 (decision 0094, code only) merged in the same day
  for test build 2.2.0-rc.2, with the same data files.
- 2026-11-18: the author's rc.2 world showed the Border Esk stopping above Langholm (reported through the EA-0159
  session): the layer's tiles were fitted to the built-in heights when World Map's world chart asked first; fixed on
  `claude/terrain-2.2` (the layer fitted to GLO-90 whichever thread asks first, above; EA-0163). The same world showed a
  slot below a lake and two crossing streams near Skjolden (EA-0165, EA-0164): fixed there too (gentle ground kept gentle,
  and inflows that cross, above), for test build 2.2.0-rc.3 with the same data files.
- 2026-11-18: River width (decision 0095) merged into `claude/terrain-2.2` for test build 2.2.0-rc.4. The streams'
  networks take the world's widths from the shared river options; streams under 20 m³/s are the smallest width at every
  multiplier. Checked with window digests (StreamsTests' probe, `STREAMS_SCALE`, `STREAMS_RIVERWIDTH`) at seven places:
  1:10 the same as rc.3; 1:25 at 250 % (2.1's widths) the same as rc.3; at the preset's 200 % only Amata (+1) and
  Eidfjord (+3) gain stream columns, where streams meet a large river now narrower.

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: **proposal** for the streams layer, not agreed or built. The quick step below is **implemented** at the author's
choice ("Quick step first", 8 November) in test build 1.0.3-tall.22 (`aa11e30` on release/1.0), checked offline and on
a private bench, installed in the author's game (8 November 09:15), not played yet.
