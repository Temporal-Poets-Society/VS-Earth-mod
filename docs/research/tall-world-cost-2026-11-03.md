# Tall worlds and a deep floor — server-side cost test (2026-11-03)

This test measures what a 1024-high Earth world costs compared with 512. It also tests the proposal to make
everything deeper than a fixed depth below the surface a single block type ("deep floor"). This is a measurement and
a prototype, not an agreed design. The prototype is a pair of test-only switches. The client was not tested.

**Result.**
- Raising the height from 512 to 1024 without a floor costs about **+65% memory per chunk column** in use and
  **+60% compressed** (saved and sent).
- With mantle from 160 blocks below the surface down, a 1024 world costs about the same as a 512 world: **+9% in use,
  −26% compressed**.
- The floor puts 6–7 mixed chunks under every column, from coast to Tibet. Without it there are 15 on the coast and
  30 under Everest.

## Setup

- Earth 0.16.13 working tree with the two switches below, Cylinder World 1.0.4, Vintage Story 1.22.7, a dedicated
  server per world, seed 12345.
- World 4 096 000 × 1 638 400 (ring 3 276 800, about 12 m per block).
- The game set the sea level: 220 at height 512 and 441 at height 1024. Earth's default log curve was used, which
  puts Everest at y≈480 and y≈960. This is **not** the proposed 1:10 profile with sea level near 100.
- Seven places, each 9 × 9 chunk columns, generated with the full worldgen pipeline: Amsterdam, the Amazon at
  Manaus, Mont Blanc, Everest, the Tibetan plateau (33°N 88°E), the Ethiopian highlands (9°N 38.7°E) and the
  mid-Atlantic (30°N 40°W).
- Two other Vintage Story servers from other work were running on the machine, so generation times are noisy.

| World | Height | `DeepFloorDepth` |
| --- | ---: | ---: |
| a512 | 512 | 0 (off) |
| b1024 | 1024 | 0 (off) |
| c1024floor | 1024 | 160 |

### The switches (`ModConfig/earthmap.json`, `SelfTest` section)

- **`DeepFloorDepth`:** above 0, each chunk column is mantle from this many blocks below its lowest rock top down.
  - Earth's terrain pass lays the mantle instead of rock ([EarthWorldGen.cs](../../src/EarthWorldGen.cs)
    `GenerateTerrain`). Vanilla rock strata only replace the default rock and deposits only replace `rock-*`, so
    both leave it alone.
  - Vanilla caves carve through anything, mantle included. A refill in the Vegetation pass (`RefillDeepFloor`)
    therefore makes everything up to the floor mantle again.
  - Chunks wholly below the floor are rebuilt from empty, so their block list holds only the mantle.
  - The floor is per chunk column, so it steps at chunk borders. It is not saved with the world.
- **`MeasureStorage`:** after each place, logs how its chunks are stored and the server's memory (`SelfTest.MeasureStorage`).

## How chunks are stored (engine, decompiled)

- A chunk's blocks are a list of the block types it contains plus 1 to 15 bit planes of 4 KB each (`ChunkDataLayer`).
- The list always holds air, so a chunk of one block type still takes **one bit plane (4 KB)**. A chunk of only air
  has no block data.
- Fluids and light are separate layers built the same way.
- The server compresses chunks that have been idle for about 8 s (`WorldChunk.Pack`). An idle chunk therefore
  takes about its compressed size on the server. Compressed is also roughly what is saved and sent to clients.

## Results

Per chunk column (9 × 9 per place). The "mixed" column counts chunks with more than one block type.

| Place | Surface y (512 / 1024) | Mixed chunks a512 / b1024 / c1024floor | Unpacked KB a / b / c | Compressed KB a / b / c |
| --- | --- | --- | --- | --- |
| Amsterdam | 220 / 442 | 7.9 / 14.9 / 6.9 | 199 / 316 / 241 | 15.4 / 21.4 / 14.5 |
| Amazon | 233 / 468 | 8.3 / 15.9 / 6.9 | 187 / 313 / 235 | 14.9 / 20.5 / 13.1 |
| Mont Blanc | 419 / 840 | 13.6 / 25.9 / 6.3 | 185 / 317 / 193 | 11.2 / 22.8 / 9.0 |
| Everest | 465 / 931 | 15.0 / 29.6 / 6.5 | 198 / 332 / 179 | 11.0 / 19.6 / 6.2 |
| Tibet | 443 / 886 | 13.6 / 27.0 / 6.0 | 189 / 321 / 183 | 10.7 / 18.1 / 5.8 |
| Ethiopia | 393 / 787 | 13.0 / 25.0 / 6.0 | 198 / 314 / 175 | 11.5 / 15.2 / 4.5 |
| Mid-Atlantic (sea floor) | 141 / 285 | 5.0 / 9.0 / 6.0 | 139 / 231 / 209 | 5.6 / 9.8 / 6.4 |
| **Mean** | | **10.9 / 21.0 / 6.4** | **185 / 306 / 202** | **11.5 / 18.2 / 8.5** |

Whole run, including the neighbour columns that generation needs (2 248 columns):
- **Save, chunk table:** a512 28.9 MB, b1024 38.4 MB, c1024floor 20.7 MB. Map chunks and regions were the same
  (about 54 MB).
- **Generation time** for the seven places: 68 s, 84 s and 81 s.
  - The prototype still runs caves and ores through the full depth, then refills. It does not show what skipping
    the floor would save.
  - Two other servers were running, so the times are noisy.
- **Server managed memory:** 1.8–2.0 GB in all three, mostly Earth's own model at this world size. The chunks of a
  place were 14–26 MB of that.

## What this does and does not show

- The extra cost of a taller world sits in the rock between the floor and the bottom. Air above the surface and
  ocean water are nearly free.
- With a deep floor, the number of mixed chunks follows the floor depth (160 blocks ≈ 5 chunks, plus local relief
  and chunk alignment), not the world height or the elevation.
- One-block chunks are not free when unpacked: 4 KB each. A 1:10 profile with sea level near 100 would have fewer of
  them under the lowlands and as many under the mountains.
- c1024floor has only 160 blocks of mining depth below each chunk's lowest surface. The a512 and b1024 worlds have
  rock, caves and ores all the way down. The lower cost is partly less underground content.
- Not measured:
  - the client: meshing time for one-block chunks, client memory, GPU memory, chunk sending time;
  - generation that skips the floor;
  - the 1:10 vertical profile;
  - floor steps at chunk borders;
  - gameplay below the floor;
  - companions that use depth (Underground Cities, Hydrology, Earth's own ore depths).

## Reproduce

The scripts and the three `server-main.log` files are in `testruns/tall-world-20261103/` (git-ignored).
- `run.sh name:height:depth ...` builds a fresh dedicated-server data folder per case next to it, starts the server
  with the Earth build and Cylinder World 1.0.4, and prints the save's table sizes (`dbsize.py`).
- The Earth build came from
  `dotnet build -c Release -p:OutputPath=<dir>/mod/ -p:IntermediateOutputPath=<dir>/obj/ -p:ReleaseZip=<dir>/earthmap-tall.zip`.
  This leaves `release/` and `bin/` alone.
