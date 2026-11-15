# 0067: A faster start: the slow steps on several cores, and a per-world model cache

Status: **agreed** (the author, 9 November 2026: "Yes, parallel and cache", after the start-up breakdown of their own
1:10 world; and "my pc has powerful CPU and other players might have much longer startup, so parallel could be
important"). Built in 2.0.0-rc.4; in Earth 2.0.0. Date: 2026-11-09.

## What the author saw

A new 1:10 world took a little over two minutes from Create to playing. The logs of that start (Earth 2.0.0-rc.3 with
52 mods, 20:11:37 to about 20:13:40):

| Step | Time | Whose |
|---|---|---|
| The server loads the game's and the mods' assets | 37 s | vanilla 12 s with Earth, Cylinder World and Hydrology alone; the other mods about 25 s |
| Earth's world set-up (the model 21 s, the spawn 3 s, the ground sets 2 s, the rest) | 29 s | Earth |
| Vanilla's world generation set-up (tiled dungeons, structures, deposits) | 14 s | vanilla |
| Earth's world generation set-up (story locations, volcanoes) | 3.3 s | Earth |
| Underground Cities plans 144 cities | 4.4 s | Underground Cities |
| The 49 chunk columns around the spawn | 15 s | every generator, Earth's terrain the largest part |
| Joining (connect, the player's column, map cache, sky) | about 20 s | mostly vanilla |

The client's own loading (texture atlases, 7 682 shapes) runs while the server works and adds little.

## The model build, step by step (1:10, offline-equivalent headless server, rc.3)

| Step | Time | Before rc.4 |
|---|---|---|
| Straits: each course moved onto the sea (5 straits, a 49 × 49 window every 3 blocks) | 4.2 s | one core |
| Sea channels: each of 3 744 river mouths' way out (grid route, then the fine way) | 4.5 s | at most 4 cores |
| Sea channels: the cell index (11 053 pieces in 2.3 million 128-block cells, 11.5 million entries) | 2.7 s | one core |
| River courses straightened (6.1 million vertices in, 10.1 million out) | 2.4 s | parallel already |
| River pools | 1.5 s | parallel by basin already |
| The courses' world positions | 0.6 s | one core |
| The river index | 0.8 s | half on one core |
| Everything else (data overlays, dams, wadis, deltas, pieces) | about 4 s | |

## Decided

1. **The slow steps run on several cores, with the same result.** Each was a loop of independent pieces whose result
   was put together in a fixed order; the pieces now run in parallel and are put together in that same order:
   - the straits' sea search (every point's nearest sea column is its own search; each worker remembers the columns
     it read);
   - the sea channels' ways out: up to 8 workers (`ProcessorCount / 2`), each with its own search buffers (about 20 MB);
   - the cell indexes of the sea channels and the river network (`Core/CellIndex`): every cell belongs to one worker
     by its key's hash, every worker walks all items in order, so each cell lists its items in ascending order as the
     serial build did, with any number of workers;
   - the river courses' world positions;
   - the story locations' site search (`StoryPlacer`): each candidate on a worker that reads the heights the caller
     reads (`FineElevation.BuiltInOnly` is per thread, so the caller's switch is set on each worker and the worker's
     own put back), the best then picked in grid order.

   **Tried and left out:** the random start's catalogue of half-degree land cells (`SpawnSearch`) on several cores. Its
   cells sample CHELSA's climate tiles all over the world at once, and a 1:10 server's memory rose from 5 to 11.9 GB for a
   minute; the spawn step took as long as before (about 4 s), so its time is elsewhere. Memory is checked for every step
   here: a 1:10 start peaks at 5.2 GB as in rc.3.
2. **A per-world model cache** (`Core/ModelCache`, `EarthMapSystem.OpenModelCache`): the straits' courses and the sea
   channels' ways (the slowest steps, and small: a few MB) are kept in `ModData/earth/cache/model-<key>.bin` and read
   back at later starts. The other steps are not kept: the laid-out courses alone would be about 250 MB a world.
   - **The key** is a hash of what the cached steps read: this Earth DLL's module id (any other build, even of the same
     version, builds afresh, so results of older code are never used; it also pins the data, whose files the store
     checks against the sizes and SHA-256 compiled into it), whether the dam file is there, and the world's parameters,
     without those set after the build at a start or during play (the spawn, the story placement and its flat zones,
     which `EarthParams.BuildModel` adds last, after the straits and the sea channels; the ore, geology and terrain-plan
     signatures, the weather handshake, the ground and deposit sets). Any other change (a config edit, an upgrade) only
     makes the next start build afresh. The first key also held the names of the data files opened and the zones: the
     in-game test showed both change between a new world's first and second start (a new world opens some files before
     the build; the story zones are added after it), and the log line named them, so they went.
   - **Safety:** the file checks itself (SHA-256 of its content); a damaged, unreadable or foreign file is ignored and
     replaced; a part that doesn't fit the exits of this start (the sea channels keep each exit's search start) is
     built; writing goes to a temporary file renamed over the old one; the folder keeps the 8 most recently used
     files. `ModelCache: false` in `earth.json` turns it off; the world is the same either way.
   - **The log** says each start what the cache gave, or what was built and, when a world's last key changed, what
     changed (the Earth build, the data, or the settings by name).

## Reviews (9 November)

- **Codex:** a damaged older cache file could make the "what changed" explanation throw outside the guarded build and
  stop the start. Fixed: every length in a file is checked against what is left of it, a file's inputs are read back
  only after its whole hash checks out, and the cache report can never stop a start (one warning line).
- **Adversarial review:**
  - the mod's own data files (works-1318.bin, outlets.bin, reservoirs-v1.json have no hash compiled in, and the mod's
    copy wins over the store's) could change with the same Earth DLL in a development folder or a rebuilt zip: the key
    now holds each of the mod's data files' name, size and CRC-32 (a folder's: size and time of change);
  - an older layout of the inputs could make the explanation throw and so skip the save: it catches everything itself,
    and the file format went to 2;
  - Parallel.For with thread-local state starts each worker again every time slice, so the sea channels' 20–40 MB search
    buffers were made many times over, and the uncapped loops used every pool thread: all parallel steps now run on
    `ParallelWork`, a few long-lived workers (half the logical processors, at most 8) taking items from a shared counter;
  - smaller: the runtime and platform are in the key (the searches use Math.Pow, Sin, Cos); a failed rename leaves no
    temporary file; a world without straits or sea channels logs "nothing to keep".
  - Left as they are: runtime settings applied every start (weather pace, rain and storm frequency, real seasons, the
    temperature extension while not frozen) stay in the key, so editing them in `earth.json` makes the next start build
    afresh once (speed only).

## Checked

- The same worlds: a new 1:10 world (seed 386217, 20 places, Earth rc.4 with Cylinder World 1.0.4 and Hydrology
  0.6.2-rc.4) against rc.3's with the same mods, once building the cache and once reading it: every block of the terrain
  pass and every one of Earth's generation records identical, the spawn and the six story locations identical, the later
  passes differing only in the known columns where vanilla's own generation differs between two runs.
- In game (the AgentBridge bench, Earth with Cylinder World 1.0.4 and Hydrology 0.6.2-rc.5, a new 1:10 world, server
  start to the player joining): rc.3 74 s, rc.4 55 s; the same world started again: rc.3 38 s, rc.4 29 s (the cache
  read). The model: 20.7 s in rc.3, 14.3 to 14.6 s in rc.4, 11.8 s from the cache. Headless: story placement 14.3 s to
  1.1 s.
- Memory: a 1:10 server sampled every second for six minutes: rc.3 peaks at 5.36 GB, rc.4 at 5.51 GB, both then 4.5 to
  4.8 GB (peaks vary by about 0.7 GB between runs of one build with the garbage collector's timing).
- `ModelCacheTests`: the cell index against a plain serial build with 1 to 13 workers; the cache file's round trip bit
  for bit, other inputs, damaged and truncated files, parts turned down, pruning.
- The offline suites that build models (river index, playtest review and issues, world options, terrain expansion, the
  map tests).
