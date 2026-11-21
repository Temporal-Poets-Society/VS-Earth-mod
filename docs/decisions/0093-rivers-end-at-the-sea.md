# 0093: A river ends where it reaches the sea

Status: **in Earth 2.2.0** (21 November 2026), built on branch `claude/ea-0148-fix` (EA-0160), for new worlds; seen on the bench at 1:10 (18 November 2026). Date:
2026-11-17. Decided by the maintainer (D72): where a river course ends is technical; what players see is the trench a
player reported, which the coordinator asked to be fixed with EA-0148's release if the fix was small (it is: one rules
version, no data file).

## Context

A player at 1:25 found a dead-straight walled trench running inland from Sandsfjorden in Norway (EA-0160, 59.49°N
6.15°E): dozens of blocks deep through a forested slope, with a broken line of water on its flat floor.

Probed offline at 1:25 and 1:10 on 2.1.4 and on EA-0148's courses rules 5 (`docs/research/ea-0148-offset-2026-11/`,
section EA-0160): the trench is reach 41302 (41303 under courses rules 5), Q 26 m³/s. `rivers-glo90.bin` leads that river
from the head of Hylsfjorden down Hylsfjorden and Sandsfjorden to the open sea on the 2′ grid, which fills the narrow
fjords: at sea level for 40 km in straight 2′ steps, over the headlands and shores between the fjords. Its channel cuts down to that level wherever it crosses land: a straight walled trench up to 367 m deep in
GLO-90's terms, with the river's water along its floor. Courses rules 5 does not change it: the snap moves reaches onto
the water mask's rivers and leaves those that run in the sea or over land where the mask has none.

Worldwide on courses rules 5's list, 352 reaches run on over land after they reached the ocean, 1,560 km of trench in all,
most in the fjord coasts: British Columbia, Norway, Greenland, Labrador, Patagonia, Alaska, New Zealand's Fiordland.

## Decision

**River ends rules 2** (`Core.RiverEnds.Version` 2, `EarthParams.RiverEnds`, new worlds): after rules 1's steps, each
reach is followed along its course where it runs at sea level (20 m or lower), every 40 m, against this world's coast
(`TerrainModel.CoastSign`: the coast outline where it decides, else the 2′ grid; at least 60 m and 2 blocks from the
coast, else undecided).

- A reach that comes to the sea and then onto land that reaches **5 m** or more (the 2′ grid's land alone near the sea,
  `TerrainSample.LandElev`, so a fjord's wall rises like the land beside it) before the sea comes again is **cut where that
  stretch in the sea began**: that point is its new end, fixed, at sea level.
- A reach whose first point begins such a stretch is **left out** (no flow, nothing drawn), and the reaches running into it
  end in the sea there.
- Land lower than 5 m all the way only ends the stretch in the sea, without a cut. The Grand Rhône's estuary is sea in
  patches in GLO-90's mask, with the river's own low banks between them; cut at its first patch, the river stopped about
  10 km above its mouth at 1:100 in a trial. Deltas, spits and beaches keep rules 1's courses.
- A cut river gets **no mouth prolongation** (`RiverSegmentBuilder.Mouth`): its end is in the sea already, and the
  prolongation along its last direction could run on over the land beyond (in the same trial, 5 km across the Rhône delta
  at 1:100).

The course list and its key are unchanged (the files keyed to it stay valid); the cut is made where the world's rivers
are laid out (`EarthParams.BuildRivers`), so it follows each world's own coast at its own scale.

Considered and not done:

- **Folding it into courses rules 5's bake**: the bake would need the world's coast at every scale, and it would change
  the course list the dams, flow and GIRES files are keyed to.
- **Cutting at the first point in the sea**: it also ended rivers whose estuaries the mask has as sea in patches (the
  Grand Rhône).
- **Using GLO-90's own heights for the land test**: they are not there when a new world lays its rivers out (they come
  tile by tile as chunks generate).

## Consequences

Checked offline on courses rules 5's list and the EA-0148 data (`docs/research/ea-0148-offset-2026-11/`, section EA-0160):

- **Sandsfjorden** (columns cut 8 blocks or more below GLO-90 on the mask's land, in a 0.06° × 0.24° window): at 1:25
  16,523 shipped, 15,956 with courses rules 5, **0** with river ends 2; at 1:10 16,344, 15,859 and **0** where a course
  draws (2,246 columns remain on a small island in the fjord, not a river's). The river now ends at the head of
  Hylsfjorden.
- **Worldwide** (the 1:10 world's coast): 1,038 reaches are cut at the sea and 283 left out past a mouth, of 624,037.
  Of the 352 reaches that ran on over land after reaching the ocean, 291 are cut or left out: 1,438 of the 1,560 km of
  trench. Most of the rest is on Greenland's ice coasts, where the coast can't be told 60 m out; 38 reaches pass land
  less than 50 m above them.
- **Mouths and deltas unchanged**: the Daugava, Rotterdam, Damietta, the Grand Rhône (also a 0.16° × 0.2° window), the
  Amazon, the Mississippi, the Ganges, the Mekong and Venice come out the same to the block at 1:10 and 1:100. At
  British Columbia, Chile and Labrador fjords the courses that crossed the windows' land at 1:10 are gone. The Grand
  Rhône keeps rules 1's mouth, led on to the sea across its low east bank (49 columns cut 8 blocks or more at 1:10).
- **Cost**: a few seconds at world start at 1:10 (the probe's model builds: 16 s against 10 s, the first of them also
  warming up); the land test only runs on courses at 20 m or lower after they reached the sea.
- New worlds only (`WorldParamsBuilder` gives `Core.RiverEnds.Version`); saved worlds keep rules 1, and their pinned
  terrain is unchanged (SavedWorldDigestTests).
- **Seen in game** on the AgentBridge bench (18 November 2026, a new 1:10 world, test build `2.1.4-ea0148.1`): at the
  reported place the slope is forest down to the fjord's beaches, with no trench along the old course either way. The
  player's world was 1:25; the offline probe covers that scale.
