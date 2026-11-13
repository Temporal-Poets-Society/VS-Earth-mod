# River outlines from the GLO-90 water mask

Date: 7 November 2026 (night). The author, on the world map of a 1:10 test world at Riga in tall.17: "test of game and
river looks to be wrong. straight lines and not in the correct path"; then "Yes, build them", of drawing the rivers
from the water mask's outlines as tall.17 draws its lakes. Proposed as [0053](../decisions/0053-scales-included-glo90-and-glo90-rivers.md)
item 18; built as test build tall.18. Not agreed or released.

## Why the rivers were straight

Earth draws a river along its course: HydroRIVERS' line moved onto GLO-90 (`rivers-glo90.bin` with its snap and arms
files), a line of straight pieces about 500 m long, and a channel of one width along it. At 1:10 that is a 50-block
straight line, and the Daugava at Riga a channel about 20 blocks wide where the river is 600–700 m (60–70 blocks). The
Copernicus GLO-90 water body mask has the river's own water as pixels (class 3), and its shores.

At Riga (1:10, a 1,600-block window at 57.0° N 24.12° E) tall.17 drew 45,626 river columns and covered 43,260 of the
253,883 columns under the mask's river (17 %); 198,086 of them were land.

## What was built

- **Data:** `tools/bake_river_outlines.py` writes `rivers-outline-glo90-1.bin`: every river pixel of the mask north of
  60° S as outlines in the layout of `lakes-glo90-1.bin` (magic `EMRO`), each piece of a tile's rivers a "lake" whose
  level is the median GLO-90 height of its river pixels (GLO-90 is the water's surface there, in steps of half a metre).
  A piece reaches one pixel into the lakes and the sea it touches: the two files' outlines are rounded off apart and
  had left a sliver of land where Ķīšezers' channel meets the Daugava. Reservoirs are left out (below).
- **Mod:** `RiverOutlineData` reads it with `LakeOutlineData`'s reader; its layer measures shores out to about 160 m
  (4–16 blocks) and indexes cells of at least 32 blocks. Where a world reads GLO-90 for a square (not a fallback), dry
  land that is not ice inside an outline is river water (`TerrainModel.ApplyOutlineRiver`):
  - the water top is the nearest river course's at that place (its ramp and its meeting with lakes as the course's own
    channel has them), found within the course's reach or, failing that, within about 2 km (`RiverNetwork.NearestCourse`);
    with no course, the piece's own level;
  - the bed falls half a block per block from a block below the water at the shore to the course's channel depth (3
    blocks with no course), deeper in the course's own channel where it runs inside;
  - land within 1.5 blocks outside is held at the bank top, as the lakes' shores.
  A course whose centre line lies inside an outline draws no channel, bank or valley of its own; just outside one its
  channel's half width is at most 2 blocks more than its distance to the outline, so it grows out of the outline's
  shore instead of ending square across it.
- **Saved:** `EarthParams.RiverOutlines` (103) is 1 in new worlds on full GLO-90, 0 otherwise. Like the lakes, a new
  world that can't get the file the first time stores that (`earth-river-outlines-off`) and keeps the courses' channels
  for good.

## Reservoirs

The mask draws many reservoirs as rivers: above the Riga dam (1974) a "river" 4 km wide. As the lakes leave
reservoirs out (no modern dams in 1318), the bake finds each HydroLAKES reservoir (type 2) from its dam (its pour
point), without new data: the highest flat water the mask has by the dam (the water below a dam is lower) that is a
tenth of the reservoir's area or 1 km², then up to 3 m higher in half-metre steps until it has the reservoir's area.
HydroLAKES' own elevations are often from before the dam filled (Three Gorges 62 m; its water is at 145–175 m), so
they are not used. Its river pixels are left out, and the courses draw the river there as before.

| Reservoir | HydroLAKES km² | Level found | Found km² | River pixels left out |
|---|---|---|---|---|
| Riga HPP | 36.3 | 14.5 m | 35.0 | 4,961 |
| Ķegums | 15.3 | 31.0 m | 17.9 | 2,530 |
| Pļaviņas | 25.4 | 68.0 m | 29.6 | 2,943 |
| Rybinsk | 4,042 | 99.5 m | 4,042 | 4,206 (the rest is lake) |
| Kakhovka | 2,092 | 14.5 m | 2,127 | 0 (all lake) |
| Three Gorges | 853 | 155.0 m | 391 | 48,706 (its upper reaches rise more than 3 m) |

Over the whole mask (the bake of 7 November, 11 minutes on 16 workers, 8 of them for the reservoirs): of 6,687 HydroLAKES reservoirs north of 60° S,
5,919 were found and 2,067,317 river pixels left out; 351 were not found (no flat water by the dam of the size
needed: the dam's point off the water, or the reservoir not in the mask); 417 were left as they are because the flat
water by the dam is more than twice their area. The first bake, without that limit, took 373 km² of the Sacramento
delta's channels at sea level for a 9 km² forebay. Most of the 417 are real reservoirs whose water runs into a
neighbour's (Lake Marion and Lake Moultrie, joined by a canal) or into a natural lake they raised (the Sheksna
reservoir and Lake Beloye), 3,368 km² in all, much of it lake in the mask. Found reservoirs have 0.53–1.67 times
their HydroLAKES area (5th–95th percentile; median 1.08).

The file: 30,314,258 bytes, sha256 `52a0c79ebc7925ac086a271a1817b144b472b08f34aa28bd8547b1d549f3807b`; 4,033,710
pieces, 20,123,332 vertices, 58,230,348 river pixels in 12,200 tiles (19,433 tiles of the mask covered).

## Probes

Scratch harness (`look17`, the mod's terrain model on the whole GLO-90 store), 1:10, map height 1024, linear heights,
tall.17 against tall.18:

- **Riga:** river columns 45,626 → 236,498; columns under the mask's river that are river 43,260 → 223,696 of 253,883
  (17 % → 88 %); river on the mask's land 2,291 → 12,373 (the pixel reached into Ķīšezers and the courses' channels
  outside the outlines). Every river top is 109, the sea's; 99 one-block steps where the river meets Ķīšezers (the
  lake's level rounds a block lower; the course's step ramp smooths it). The Daugava, its north channel, Sarkandaugava
  and Ķīšezers are one water body. No spills. Cost unchanged (0.4 µs per column).
- **The Riga dam:** below the dam the Daugava is the mask's (river columns 30,271 → 54,490); above it, where the
  reservoir is left out, the course's channel as in tall.17. One face of river water beside lower dry ground.
- **Ķegums** (the dam and its reservoir): 23,641 → 23,624 river columns, no steps.
- **Three Gorges** (Badong, 110.5° E): 0 → 6,756 river columns, the reservoir's water more than 3 m above its level
  by the dam.
- **The Sacramento delta:** 7,435 → 373,305 river columns, the delta's channels at sea level; 4 steps of two blocks.
- **Earlier, on the first bake:** the Middle Rhine's gorge and Manaus drawn at their width; the Rhône window (the
  Camargue) has spills that tall.17 has too.

In game (AgentBridge bench, tall.18 with the published packs and tall.17's Water Fix and Ground Data packs, the river
file placed in the bench's GLO-90 folder; world t18r10, play style earth-10, 1:10, height 1024): the log says "rivers'
water from the water mask's outlines" and "Earth rivers: … shores measured 16 blocks out", no Earth errors. From
400 blocks above Riga the Daugava has its own shores, side basins and harbours; at the Riga dam the mask's river ends at
the dam and the course's channel goes on above it.

## Open

- Straight pieces the mask has itself (dredged channels, its pixel staircases simplified by half a pixel).
- Pieces of one or two pixels make small ponds.
- Reservoirs without a HydroLAKES dam, the 417 whose water runs into more than their own, and the parts of a
  reservoir more than 3 m above its level by the dam stay river. Where a reservoir is left out, GLO-90's land is its
  flat surface: the courses' channel runs through a plain at the reservoir's level, as in tall.17.
- Where the mask's sea ends in the Daugava, sea and river water meet in a straight line across it (the mask's own).
- The file comes with the GLO-90 heights, as the lakes'; publishing it on `earth-glo90` needs the author's yes.
