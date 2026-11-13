# How Algernon's Watersheds sets stream water heights (study, 2026-10-22)

Watersheds 7.0.5 (`watersheds_7.0.5.zip`, sha256 `627fa977…e8a42b`) was downloaded from ModDB with the user's approval.
It was decompiled in a scratch folder, read for its height-aware stream method, and the decompiled source was deleted.
The mod has no stated license, so no code is kept here. This note describes the method in our own words, as input for
[decision 0024](../decisions/0024-hydrology-water-mod.md). Points marked *(inferred)* are readings, not something the code
states directly.

## Pipeline

- **Grid.** Streams are planned on a grid of cells 32, 64 or 128 blocks across, not per block. Each cell samples the
  terrain *noise function* (no real chunks) for its median height, downhill direction, slope and roughness. Rough cells
  are excluded, and ocean cells are drains.
- **Flow.** Cells near a drain have their downhill direction turned toward it. Flow then accumulates over a few
  passes: a cell gains from neighbours that point at it. There is no rainfall map.
- **Pathing.** A stream grows *upstream from its drain*, one cell per step, choosing the neighbour with the most
  accumulated flow that points the right way. A cell more than 1 block lower than the current one is refused, so a
  stream never climbs.
  - Tributaries grow from a bigger stream's cells. Their bed is floored at the parent's bed where they join.
  - Meanders and braids are applied to a smooth curve through the cell centres, with about one curve point per block
    *(inferred)*.
- **Storage.** Results go to SQLite in the save, with memory caches. The settings that shape the result are locked into
  the save so they cannot change later.

## Water height along a stream

- **Bed.** The bed is lowered in proportion to the slope (averaged over about 20 points). It is then forced to only go
  down toward the mouth, by a running minimum from source to mouth.
  - A short "trench" pre-lowers the bed up to 3 blocks over the 10 blocks above a sudden drop, which softens a
    knickpoint into an incised ramp.
  - Bend pools and coast fades adjust it further.
- **Water.** Each cross-section has one whole-block level:
  - the lowest of the two nearest bank heights and of every bank height upstream, minus a 1-block freeboard;
  - plus up to 2 blocks of backfill in pits, so they become pools.

  The running minimum from source to mouth keeps the level from ever rising downstream. The banks are read 3 points
  upstream, which avoids a dry lip.
- **Drops.** There is no explicit drop logic: no minimum pool length, no maximum drop and no waterfall builder. A step
  appears wherever that whole-block level falls, and it can be any height.
- **Rapids.** They come from an *averaged slope*, not a drop height. Where the smoothed fall within ±10 points reaches
  0.3 blocks per point, rapid-water blocks go into the air cells beside the stream water: its edges and the lip over a
  drop.
- **Ramps.** After the bed is laid, a flood fill puts flowing water (levels falling by 1 per block) only into air
  directly above full water. This gives a thin ramp over the lower pool at each step, as Earth does.
- **Liquid updates.** Every placed water block and rapid gets a scheduled block update, so vanilla's liquid rules act
  on them right after generation. Water reaching a chunk edge is queued for the neighbour chunk as flowing water. The
  queue lives in memory only, so it is probably lost on a restart *(inferred)*.

## Water wheels and push

- **Push vectors.** For every stream water block, Watersheds stores a push vector in chunk moddata: flow direction ×
  strength, weaker toward the banks. Boats and entities drift with it (fish excepted).
- **Wheels.** A Harmony postfix on vanilla `BEBehaviorMPWaterWheel.CheckWater` acts when vanilla found no rapid water.
  It adds up the stored push along the wheel's rim and sets the wheel's flow rate from that. An option makes every push
  count as rapids strength.
  - So wheels turn on ordinary still river water, from stored flow data rather than flowing blocks.

## Performance

- The planning runs on its own worker threads, with prefetching when chunks are requested, memory caps, and pauses
  coordinated with vanilla world generation.
- Water placement runs per chunk, in parallel over its columns.
- Cell and stream data persist in SQLite.

## What this means for Earth and Hydrology

- **Monotonic levels.** Earth already has these: `RiverReach.Level` is the valley floor, de-dammed and smoothed, never
  rising downstream. What Watersheds adds is a freeboard below the banks and a backfill in pits. Earth's pools come
  from the real valley, so they need neither.
- **Drop heights.** Watersheds never chooses a drop height. Earth knows its valley floor along every reach, so it can
  give each drop its exact height: the fall since the pool began, with a minimum pool length and a cap. Rapids can then
  follow the user's rule, a drop of 2+ blocks, rather than an averaged slope.
  - An averaged-slope test is still useful for choosing the cap on steep reaches.
- **Softening knickpoints.** A short incised approach above a drop, like their trench, could stop a 2–4 block drop from
  cutting the bank as a hard notch. That is a terrain-carving detail for Earth.
- **Wheels on any river.** Stored flow data instead of flowing blocks is a strong idea for Hydrology's later "power
  from drop × flow" target (0024 section 4).
  - Earth's planned river flow query (0022 part A) already knows direction and discharge for every river column. A
    wheel could take its power from that, not from blocks, with no extra save data.
  - Watersheds patches the vanilla wheel this way, so it is known to work.
- **Opposite choices.** Watersheds hands its generated water to vanilla's liquid rules at once (scheduled updates) and
  queues cross-chunk spreads in memory. Earth draws settled shapes instead and needs no queues. Hydrology's drop
  function (0024 section 3) keeps that: the generated shape is already what the rules would settle into, so updates
  change nothing.
