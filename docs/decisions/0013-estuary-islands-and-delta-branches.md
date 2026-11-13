# 0013: Estuary islands stay whole; delta branches share their river's water

Status: Agreed, implemented (0.14.7), offline renders only, not played yet | Date: 2026-10-20 | Decided by: the user
("Island stays whole", "Delta split list", while play-testing the 0.14.5 world `earth 0145`)

## Context

Two things the user saw on the Wmap in a 204 800-ring world:

- **Rectangular cuts in the upper Tagus estuary** (38.9°N, 9.0°W, near Vila Franca de Xira). HydroRIVERS runs the
  Tagus's centre line down the middle of the estuary (the same at the Amazon's mouth, where the strips cut its islands
  north-south and east-west). Over the grid's sea the river layer does nothing, but the small
  GSHHG islands there (`islands.bin`, 1–10 km², a few blocks wide at this scale) are land again, and
  `TerrainModel.ApplyRiver` cut its channel through them: a strip 18 blocks wide with straight edges along the
  straight centre line, one of them exactly on 9°W (a coincidence, not a tile edge). River exit channels
  (`SeaChannels`) cut low island land on their course the same way.
- **A straight canal across the Mekong delta.** HydroRIVERS is a tree: the Mekong's main stem goes down the Tiền
  to Vàm Nao, crosses to the Hậu (Bassac) and takes all of its 15 000 m³/s to the Trần Đề mouth (54 blocks, ~10 km
  wide at 204 800). The Tiền below Vàm Nao is a separate local stream of 20–90 m³/s, and some branches are missing.
  River straightening and exit channels are not involved (renders without them are identical).

## Decision

- **Island rules v3** (`IslandLayer.RulesVersion` 3, new worlds): island land laid over the grid's sea
  (`TerrainModel.Column.IslandOverSea`) is not cut by a river or a river exit channel. The island keeps its outline;
  the water goes around it through the sea that is there anyway.
- **Delta branches** (`Core.RiverDeltas`, `EarthParams.RiverDeltas` ProtoMember 62, new worlds with river ends): a
  curated list of splits. Each names a point on the main stem and its branches by their mouths; the branch's main
  chain in the river data gets its share of the discharge at the split, its head is led to the split point (through
  optional points on the way) at the split's level, and the main stem below keeps the rest. Reach indices stay.
  First entry: the Mekong at Vàm Nao, Tiền (to the Cổ Chiên mouth) 0.5, Bassac 0.5: two 44-block branches instead
  of one 54-block one.
- Existing worlds keep their terrain; they opt in with `UpgradeExistingWorldIslands` and
  `UpgradeExistingWorldRiverDeltas` ([0002](0002-save-compatibility.md)).

## Alternatives

- *Keep cutting islands, with eased edges.* Smaller change, but the islands stay split.
- *A generic width cap near the sea instead of a list.* Covers every delta without data, but the other branches stay
  thin streams.

## Open

- More deltas for the list (the Ganges–Brahmaputra, Nile, Rhine, Danube, Niger, Irrawaddy, Volga, Lena, Mississippi,
  and the Mekong's own Hàm Luông and Mỹ Tho), each checked against the river data (`EarthMapTests ... mouths2`).
- Widths themselves: decided the same day in [0014](0014-river-widths.md).

## Implementation

`src/Core/TerrainModel.cs` (`IslandOverSea`, `ApplyIslands`, `ApplyRiver`), `src/Core/SeaChannels.cs` (`Apply`),
`src/Core/Islands.cs` (`RulesVersion`), `src/Core/RiverDeltas.cs`, `src/EarthParams.cs`, `src/EarthMapSystem.cs`,
`src/EarthMapConfig.cs`. Tests: `EarthMapTests` modes `render lat lon w h step [name]` (env `DUMP=1`, `ACROSS=lat`,
`ISLANDS=0`, `DELTAS=0`) and `mouths2 lat0 lat1 lon0 lon1 [minQ]`.
