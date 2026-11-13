# River and water mods compared, 2026-10-25

**Prompt.** The user played Earth 0.16.11 (pool layout 7) with Hydrology 0.1.7:

> now rapids looks patch from top with rapids and still water between. Flooding does not happen when remove blocks.
> But now rapids have 1 block thick walls that does not look right.

The user asked to think more broadly, starting from Algernon's Watersheds' average slope, Realistic Water and two
river mods.

**Sources:**
- Watersheds: our decompiled study ([watersheds-streams-study-2026-10-22](watersheds-streams-study-2026-10-22.md)).
- Realistic Water: our server test ([realistic-water-test-2026-10-22](realistic-water-test-2026-10-22.md)) and its mod page.
- Rivers (sneezerstein): its mod page and the open source at github.com/glutzer/Rivers-Mod, config file only.
- Rivers Extended (Chilimunyaka): its mod page.

Nothing new was downloaded.

## How each one makes a river

| | Water surface | Rapids | Current, boats, wheels | Cost |
|---|---|---|---|---|
| **Rivers** (sneezerstein, mod id `rivers`) | Valleys carved down to sea level ("heightBoost" above it). Every river is flat still water at sea level. | None as blocks. | A stored flow vector per column ("flowVectors") draws flowing water in the shader and pushes entities; "riverSpeed" also drives water wheels. | No liquid simulation; about 2 GB of RAM reported. |
| **Rivers Extended** | Terrain shaping around Rivers' channels (smoothed shores, lowered banks). | — | — | — |
| **Watersheds** | One whole-block level per cross-section from the banks, never rising downstream. Steps of any height wherever that level falls. | Rapid-water blocks placed beside the stream and on lips, where the average slope over about 20 points is 0.3 blocks per point or more. | A push vector per water block in chunk data. Boats drift with it, and a wheel patch reads it, so wheels turn on still water. | Planned on worker threads; vanilla liquid updates act after generation. |
| **Realistic Water** | Replaces liquid behaviour. Flowing water spreads about 100 blocks and thins, with a path search for a way down. Canals 2+ wide become sources ("lake extension"). | Its Realistic Rapids add-on ties rapids and wheels to drop height. It erased Earth's rapids in our test. | Vanilla wheels. | Off the graphics thread; heavy during floods. |
| **Earth + Hydrology** | Geography-true pools. Steps placed where the real valley falls, settled into shapes Hydrology keeps. Sills under steps stop canal fill from flooding them. | Rapid-water steps 5 apart, in sections (layout 7). | Vanilla wheels on rapid water. | Settled at generation; rules run only on updates. |

## What this says about the user's two problems

- **Patchy rapids.** A block-stepped river can only show white water where it steps down. On the Congo at 256k the
  stretch falls about 1 block per 35–46 columns, and each block of fall gives at most 5–6 columns of white water. No
  step layout makes that continuous.
  - Watersheds paints rapids where the *average slope* is steep, and Rivers paints flow on *still* water.
  - Both separate how a river looks and pushes from where its water blocks step.
- **Walls.** The 1-block walls are Earth's sills. They exist only because Hydrology's communicating vessels can't tell
  a broken sill from the front of a dug canal (rule study 2026-10-24). Every local rule that tells them apart also stops
  real canals filling.
  - The rule could tell them apart if it knew which water is a natural river. That is what Earth's planned river flow
    query is for ([decision 0022](../decisions/0022-river-damming-and-diversion.md) part A, and 0024 section 4: "which
    water is a natural river comes from Earth's read-only river flow query, not per-block flags").

## A broader direction (proposal, not decided)

Earth answers a read-only **river flow query** per column, from data it already has: whether it is natural river
water, its flow direction, its discharge, and the average slope of its reach. Hydrology uses it:
1. **Canal fill:** no communicating-vessels fill in natural river water. Dug canals and ports still fill. Earth can
   then drop the sills, so the walls go away.
2. **Rapids by average slope (Watersheds' idea):** in a river column whose reach is steep enough, the surface sources
   are rapid water. H4 keeps them because the query says rapids, not because a step is near.
   - White water then covers whole steep stretches, however few blocks they fall.
   - Steps stay where the valley falls, with ramps as now.
   - The study found that rapid sources over calm pools revert under today's rules (180–240 cells). With the query
     they would not.
3. **Wheels (Watersheds' idea):** power from the query's slope × discharge, so a wheel turns in any flowing river, not
   only on rapid blocks.
4. **Later (Rivers' idea):** a flow shader and current on boats from the same query.
   - This is client work. Sea or Hansa may already push boats.

Earth's layout could then return to a simple one: steps where the valley falls. Layouts 5–7's spacing and sills were
there to keep water stable without the query.

**Costs and risks:**
- **Interface:** a cross-mod interface (Earth → Hydrology), cached per chunk. 0022 already proposes it.
- **Hydrology:** H4 and the vessels rule need the query; Hydrology on vanilla worlds keeps today's rules.
- **Saves:** new pool layout, new worlds only.
- **Unknowns:** how rapid sources on a pool behave next to ordinary water when a player digs. The rule study's
  simulator would check that first.
