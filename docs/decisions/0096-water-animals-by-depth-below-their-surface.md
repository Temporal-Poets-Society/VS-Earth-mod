# 0096: Fish and other water animals spawn by their depth below their water's surface

Status: **in Earth 2.2.0** (21 November 2026), built on branch `claude/ea-0178-fish` (EA-0178), from `claude/terrain-2.2` (a66257c2, 2.2.0-rc.3); also
cherry-picked onto v2.1.4 as `claude/ea-0178-fish-2.1` for a possible 2.1.5; merged into `claude/terrain-2.2` for the
author's in-game test (test build 2.2.0-rc.5, 18 November 2026). Measured on headless servers at 1:100 and 1:10; not
seen in game. All Earth worlds, saved ones too (no rules version). Date: 2026-11-18. Decided by the maintainer
(D72): how a vanilla test is translated to Earth's water is technical; what players see is the fish the game already
promises.

## Context

Players reported fish spawning broken in Earth worlds, without details (EA-0178).

**How vanilla places a fish** (1.22.7: `GenCreatures`, `ServerSystemEntitySpawner`, the three fish entity files):

- **Where:** at the terrain heightmap + 1, so on the bed of the water (`tryOnlySurface`).
- **What it must be inside:** freshwater fish in `water-still-7` (Earth's patch `fish-rivers.json` adds `water-*-7` and
  `rapidwater-*-7`), saltwater fish in `saltwater-still-7`, reef fish in `coral-*`.
- **Light:** at least 3 there.
- **Climate:** each species' temperature range, and a random species map.
- **Height band:** `minY`..`maxY` in vanilla's relative height: y / sea level at or below the sea, 1 + share of the sky above
  it. Freshwater and saltwater fish have 0.8..1.0 (catfish, sheatfish and walleye 0.7..0.9; crappie, perch and trout
  0.9..1.0; coelacanths 0.3..0.6). The fish must be at or below sea level.
- **At runtime also:** water two blocks to every side at the spawn height (`HabitatSize` 2; reef fish 0).

Every vanilla lake and sea has its surface at sea level, so vanilla's band is a depth below the surface. A vanilla
world's ocean surface measured at exactly the sea level (110), its few ponds above it get no fish either. Earth's lakes
and rivers lie at their real heights, nearly all above sea level, where the band leaves no water at all.

**Measured** with `tools/fishprobe` (fish entities after generation in 81 chunks around each place; the runtime test
replayed on 48 random columns per chunk for every fish type, the spawner's own `CanSpawnAt_offthread` included). Earth
2.1.4, Cylinder World 1.1.1, Hydrology 0.6.2; world height 384, sea level 110 at 1:100; 1024 and 110 at 1:10:

| Place (1:100) | Water above sea | Fish after generation | Runtime: water columns a fish type passes |
|---|---|---|---|
| Daugava at Riga (sea level) | 0% | 11 freshwater | 141 |
| Lakes Ladoga, Peipus (5-30 m) | 0% | 35, 22 | 2,330, 1,415 |
| Daugava at Daugavpils (~90 m) | 98% | **0** | **0** |
| Danube at Vienna (~160 m) | 100% | **0** | **0** |
| Lakes Pielinen, Geneva, Great Bear, Baikal | 78-100% | **0** in each | **0** in each |

At 1:10 the same: no fish in the Daugava at Daugavpils, the Danube at Vienna, Lakes Pielinen, Geneva or Baikal, every
test failing on height. Sea 0.6.2, Sea Monsters 0.2.4 and Ships 0.1.2 change nothing: Sea and Hydrology use vanilla's water
blocks, the kraken has its own population count and no spawn conditions. Earth's range guards and fauna catalogue have no
fish. The same rule shuts out other mods' water animals the same way: Fauna of the Stone Age's river dolphins
(Iniidae, `maxY` 0.99) and sirenians (`maxY` 1).

## Decision

**In Earth worlds a creature that spawns in water has its height band measured from its water's own surface**
(`src/Core/WaterDepthBand.cs`). The spawn height is moved by the water's height above or below the sea and then tested
the way vanilla tests it:

- Water at sea level (the sea, coastal lakes, the lower Daugava) gets exactly vanilla's test.
- A lake 20 blocks above the sea gets the sea's depth bands: freshwater fish in its top 21 blocks, catfish from about
  11 blocks down.

**"Spawns in water"** means every block its `InsideBlockCodes` name is a liquid: vanilla's 26 freshwater and 19 saltwater
fish types, and other mods' water animals. Reef fish spawn inside coral and keep vanilla's test; Earth's corals grow
only in the sea, where both tests are the same.

**How** (`src/WaterSpawnDepth.cs`): vanilla tests the band inside `GenCreatures.CanSpawnAtConditions` and inline in the
runtime spawner before `CanSpawnAt_offthread`, and no patch reaches either.

- While Earth is active, each such type's band is widened to the whole world (0..2).
- The depth is tested in two postfixes, after vanilla's own test of the position: `WorldgenWaterDepth` on
  `GenCreatures.CanSpawnAtPosition` and `RuntimeWaterDepth` on `CanSpawnAt_offthread` (PatchVanilla features).
- A side is widened only while its postfix is bound. A game update that takes a patch away leaves vanilla's test, never
  no test.
- The bands are put back when Earth goes inactive.
- The log names the number of types at start and the counts kept and turned away at the end of the session.

**No rules version.** Fish are not terrain: they move and die, and saved worlds had the same missing fish. Old chunks
get fish by spawning, new chunks also at generation. The saved-world digests are unchanged.

**Left as vanilla:**

- `HabitatSize` 2: rivers narrower than five blocks get fish at world generation only. Runtime spawning is rare anyway:
  chance 0.0001 a round, at most 4 of a kind on the server.
- The light test: the floor of deep lakes (Baikal at 1:10, 29 blocks) is dark, so fish keep to its shallows, as in
  vanilla's deep seas.
- Vanilla's random species maps: each fish type's map is seeded from .NET's per-process string hash, so which species
  may live where changes at every server start, and counts at one place vary a lot from run to run.

## Result

Measured on the same terrain, Earth 2.1.4 against 2.1.4 with this change (`2.1.4-fish.1`):

| | Fish after generation, inland water above the sea | Runtime water columns passing |
|---|---|---|
| 1:100: Daugavpils, Vienna, Pielinen, Geneva, Great Bear, Baikal | 0 → 61 | 0 → 4,692 |
| 1:10: Daugavpils, Vienna, Pielinen, Geneva, Baikal | 0 → 67 | 0 → 7,087 |

Freshwater fish across all places measured: 85 → 147 at 1:100, 54 → 101 at 1:10. The sea and water at sea level are
unchanged (no depth refusal in the North Sea; the differences there come from vanilla's water plants and species maps,
which differ run to run).

Checks: RegionalFaunaTests (sea-level water gets vanilla's test for seven bands; a lake above the sea the sea's bands,
block for block); PatchVanillaTests (the two features bind on 1.22.7); the quick suite; SavedWorldDigestTests.

## Not chosen

- **A JSON patch raising freshwater fish's `maxY` to 2:** fish at any depth and none of vanilla's depth bands (catfish in
  puddles), the sea fish untouched, other mods' water animals still shut out, and it would apply in vanilla worlds too
  (Earth's JSON patches load in every world).
- **A transpiler on the inline runtime test:** brittle against game updates. The postfixes sit where Earth already
  patches (its range guards).
- **A rules version:** it would leave the reported worlds without fish in their lakes forever.
