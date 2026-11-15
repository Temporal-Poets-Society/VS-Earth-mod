# 0071: River mouths without dredging; one bed where a river meets the sea

Status: **built**, on `main` (from `claude/river-mouths`, merged as `2d36d58`) for 2.0.0 (EA-0038, EA-0039; the author
asleep, "take our own decisions", 12-13 November 2026); in Earth 2.0.0. New worlds only: sea channel rules 5 (`EarthParams.RiverChannels`) and channel rules 3
(`EarthParams.ChannelRules`); older worlds keep the rules they saved (0002). Date: 2026-11-13.

## What the author reported

At the Daugava's mouth in a 1:10 world (57.0550° N 24.0565° E, 12 November, night): a straight-sided, flat-bottomed
channel cut through the river's bed and on into the sea, fanning out in a wedge, "different mechanics at play and still
dredging is happening for 1:10 map"; and where the river meets the sea the bed is patchy, stripes and blotches of
shallow and deep. "These diagnostics are for all rivers."

## What writes the bed at a mouth (rc.7, measured)

A depth map from the world's own model (the bench's rc.7 1:10 world `rc7-fish`, its saved parameters, the bench's
GLO-90 store; each pixel attributed by rebuilding it with the sea channels, the straits and the rivers taken out in
turn). At the Daugava (57.02-57.09° N, 23.98-24.12° E, 2 blocks a pixel):

1. **The sea channel (`SeaChannels`, rules 4)**: the red wedge. The river's exit to open water deepened to the channel
   depth (7 blocks: the river's own depth at its end, at least `ShipDepth` 6) over the river's width, widening 30° to
   each side up to `MaxHalfWidth` (about 1,300 blocks at 1:10): 34,434 pixels, every one exactly 7 deep, against the
   natural 6 around, 1.3 km out to sea. The channels were made for the 2' grid, whose cells (38 blocks at 1:100) could not
   draw an estuary's way out; with outline coasts (1:100 and finer) GSHHG's coast and the water mask draw it.
2. **The course's channel through the sea**: the yellow groove. Where a river course at the sea's level ran through sea
   water, `ApplyChannel` cut its channel "through the shallows": 17 blocks wide, 9 deep, walls for sides, along the
   course rerouted through the estuary (0069 rule 2), which hugs the north bank.
3. **GSHHG's sea in the river**: the patches. GSHHG has the lower 2.5 km of the Daugava as sea; the water mask has it
   as river. Sea columns take the coast profile (1 block at the coast, `MinSeaDepth` a reach out) over the 2' grid's
   -1.5 to -4.7 m: 1-5 blocks in bands along the banks. The mask's river water upstream is 7-9. Where GSHHG's line
   crosses the river, depth jumped by 3-8 blocks along it.
4. Inside the river water itself (0069 rule 3), two kinds of straight steps: the nearest course piece's channel depth
   (two pieces of the Daugava 12 and 8.5 blocks wide, so 7 and 9 deep, with the bisector between them as the step), and
   the 3-block still-water depth below 2 cm/s against the full depth above it.
5. The straits (`NavigableStraits`) and the shore strip (0070) write nothing here (the strip sets material only).

## Decision

1. **Sea channel rules 5: no river exits where the world has outline coasts** (`SeaChannels.Version` 5; 1:100 and
   finer, the worlds `EarthMapSystem.CoastsFor` gives coasts). The real water is wide and deep enough there at the
   world's scale; nothing is dredged. The Suez and Panama options are canals, not river exits, and stay. Worlds without
   outline coasts (1:200) keep rules 4's channels: their grid cannot draw an estuary.
2. **Channel rules 3: one bed where a river meets the sea** (`TerrainModel.ApplyMouth`, `MouthChannelRules`). A sea
   column where a river at the sea's level reaches it is never shallower than the sea's own bed, and deeper only where
   the river is:
   - **in the water mask's river water** (GSHHG's sea up an estuary): the depth that water has as river (below),
     reached from the sea's own depth at half a block per block in from the mask's edge. At GSHHG's line across the
     river the sea's own depth is a block, as the river's shelf is; at the mask's edge towards the open sea it is the
     sea's own: no step on either side;
   - **along the courses and their mouths' prolongations near**, where a course's own line is at sea, the deepest of
     their troughs (the nearest course alone made a step between a delta's branches): the course's channel depth over
     its water, widened by the distance from the shore (at most half the course's reach and the coast profile's reach),
     shelving to the sea's bed and to the shore at half a block per block. Along the prolongation, and as the course's
     line lies further out to sea, the trough's depth eases to the sea's over at least three blocks per block it loses
     (HydroRIVERS runs the Rhine's and the Loire's last reaches 5-10 km out over water that was sea in 1318);
   - **on the land side, a mouth bar**: a river at the sea's level within the coast profile's reach of the coast is no
     deeper than a block plus half a block per block from the coast, so the river and the sea meet at a block or two at
     the coastline. In the mask's river water the distance from the mask's edge counts too, so where the mask's water
     goes on into GSHHG's sea (the Daugava) there is no bar across the river. GSHHG's distance is known up to the coast
     profile's reach only (8 blocks at 1:100), so the shelf steepens to reach the river's depth there (a block per block
     for a 9-block river at 1:100);
   - rounded through the rivers' depth noise, so steps of a block wander.
   The course's own groove "through the shallows" is not cut in the sea any more (lakes keep it).
3. **Channel rules 3 also smooth the mask's river water** where the flow is solved (`MaskWaterDepth`): every course near,
   weighted as 0063 weighs them for the depth cap (their channels' depths, deepened where the mask narrows them, easing
   out over their reach), over the nearest course's depth for its flow (one value along a reach) beyond them; and from
   the 3-block still-water depth to that as the solved speed rises to 0.1 m/s (`CarryFull`). The same function gives the
   estuary's sea its depth, so river and sea are one surface.

No guard: a ship that draws more than the real water at a bar grounds there (the author: realism over safety guards).

## Checks

**Depth maps** (an offline probe that rebuilds a world's model from its saved parameters, as 0069's did; the bench's
rc.7 worlds `rc7-fish`, 1:10, and `review1111`, 1:100; the bench's GLO-90 store; before = trunk `ed90062` with the
world's saved rules, after = this build with sea channel rules 5 and channel rules 3). "Steps" counts neighbouring
water columns at one water level whose depths differ by 3 blocks or more; "sea" pairs touch a sea column, "river"
pairs are river water only.

| Mouth (box) | 1:10 sea steps | 1:10 river steps | 1:100 sea steps | 1:100 river steps |
| --- | --- | --- | --- | --- |
| Daugava (57.02-57.09 N, 23.98-24.12 E) | 776 → 6 | 193 → 166 | | |
| Vistula, Przekop | 258 → 8 | 25 → 26 | 0 → 29 | 0 → 8 |
| Neva | 1,163 → 186 | 276 → 211 | 14 → 0 | 136 → 32 |
| Elbe, Cuxhaven | 0 → 0 (flat 7 → mean 5.8) | 0 → 0 | 0 → 0 (flat 7 → mean 5.7) | 0 → 0 |
| Rhine, Hoek van Holland | 1,846 → 114 | 146 → 23 | 82 → 220 | 0 → 1 |
| Thames | 221 → 20 | 0 → 0 | 19 → 0 | 0 → 0 |
| Loire | 1,231 → 150 | 160 → 3 | 243 → 0 | 4 → 0 |
| Tagus | 0 → 0 (mean 7.4 → 6.1) | 0 → 0 | 0 → 0 (mean 7.4 → 5.9) | 0 → 0 |
| Danube, Sulina | 137 → 4 | 0 → 0 | 419 → 7 | 14 → 5 |
| Nile, Rosetta | 288 → 21 | 563 → 569 | 5 → 7 | 135 → 108 |
| Mississippi, Southwest Pass | 0 → 0 (2-block steps 416 → 1) | 0 → 0 | 0 → 0 | 0 → 0 |
| Gauja (a small river) | 21 → 0 | 121 → 77 | 0 → 0 | 1 → 0 |

- The Elbe's whole 1:10 box (866,026 water columns, 18 × 9 km) and most of the Tagus's were the sea channel's flat 7-block
  fan; now the real bed. At the Daugava the wedge (34,434 columns at exactly 7) and the 9-deep groove are gone; the
  estuary is one bed, 9 along the thalweg by the north bank and 6-7 across, shelving to both shores.
- Where the 1:100 counts rose (the Vistula, the Rhine), the dredged disc had covered the place. At Rotterdam the
  remaining steps are today's harbour moles shaping the coast profile inside the sea of 1318 (EA-0042), not the river.
- The river steps that remain at the Nile and the Neva are inside the river water away from the sea (channel profile
  edges of side arms), not touched here.

**Bench** (AgentBridge, a new 1:10 world per build, Earth `2.0.0-rc.7-mouths.4` with Cylinder World 1.1.0-rc.3 and
Hydrology 0.6.2-rc.5): the author's own view at the Daugava (57.0550° N 24.0565° E, y 232, looking SSW) shows no groove
and no wedge; the mouth seen from above shows the channel fading into the sea floor; the Elbe at Cuxhaven and the
Vistula's mouth look natural. Screenshots in the suite's `backlog/media/EA-0038-after-*`.

**Tests**: RiverWaterTests, ModelCacheTests, RiverIndexTests, EarthWorldOptionsTests pass; EarthMapTests fails only its
Lake Geneva check, as on the trunk (EA-0040).

## History

- 2026-11-13: built for EA-0038 and EA-0039 (the author's report of 12 November at the Daugava).
