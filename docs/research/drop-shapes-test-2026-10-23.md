# Drop shapes that stay as generated (pool layout 5), 2026-10-23

Pool layout 5 ([decision 0024](../decisions/0024-hydrology-water-mod.md) step 3) draws the river drops in the shape
that the 2026-10-23 rule study found stays as generated under Hydrology's rules. The study is in Hydrology
`docs/water-rules-study-2026-10-23.md` (`watersim verify-shape`, "What Earth should generate").

Background: the user saw ordinary sources standing at Niagara's lip without rapids, and rapids forming only once a
player touched something. Hydrology 0.1.4 also has a communicating-vessels fill (`FillVessels`) for deep canals, which
floods Earth's older steps.

## What new worlds get

- **Sills.** The upper water's edge column beside any lower pool has its bed at least at the lower pool's surface. At a
  1-block step it then holds water only in its top block. Without this, its lower layer touches the lower pool, and the
  vessels rule fills the ramp up to the upper pool. For a drop of 2 or more, deep upper water would fill the curtain's
  foot from the side.
- **Edge sources.**
  - At a 1-block step, the upper pool's top source beside the ramp is `water-<dir>-7` (a label only).
  - At a chute's step it is `rapidwater-<dir>-7`.
  - At a curtain's lip, every upper source beside the curtain is `rapidwater-<dir>-7`, from the top down as far as the
    curtain reaches (both layers of the 2-deep lip).
- **Curtains.** The foot on the plunge pool is `rapidwater-<dir>-6`. Every cell above it, up to the upper water's level,
  is `rapidwater-d-6`. Before, it was `water-d-6` with a flowing top cell.
- **Spills.** Where a chute's ramp (3 or more high) ends beside the next step down, `rapidwater-d-<h-1>` sits above that
  step's first ramp cell. With Earth's 6, 5, 4 chute ramps that is mostly `d-3`.
- **The world asks Hydrology for vessels.** Earth sets the world-config attribute `hydrologyFillVessels` = true, so
  Hydrology fills deep canals layer by layer in these worlds only.
- **Lake chunks.** A chunk where lake water meets lake or river water at another level, inside it or right across its
  border, also settles. Otherwise the lake side's sill, edge sources and ramps would only be computed in a neighbouring
  river chunk's margin, which is thrown away.
- **Salt lakes.** Salt lakes beside each other at different levels get sills too. The vessels rule fills salt water as
  well, never across liquids.

## Server world

Seed 386217, 256k, height 384: Congo (Livingstone), Victoria, Niagara and the Rhine gorge, against the pool layout 3
world of 2026-10-22. Measured with [shapes.py](river-drops-test-2026-10-22/shapes.py)
([layout 3](drop-shapes-test-2026-10-23/shapes-layout3.txt), [layout 5](drop-shapes-test-2026-10-23/shapes-layout5.txt)).

| Upper pool column beside a lower pool, bed at least at the lower surface | Layout 3 | Layout 5 |
|---|--:|--:|
| Congo | 233 of 1211 | **1211 of 1211** |
| Victoria | 31 of 31 | 31 of 31 |
| Niagara | 90 of 332 | **318 of 318** |
| Rhine gorge | 28 of 359 | **359 of 359** |

Block counts in layout 5:
- **Curtains:** 438 (Victoria), 550 (Niagara) and 461 (Congo) cells of `rapidwater-d-6`. Only 1–9 cells of plain
  `water-d-6` remain per site (vanilla's or the game's own).
- **Lip and chute edges:** 1008 `rapidwater-<dir>-7` on the Congo and 145 on the Rhine.
- **Spills:** 342 `d-3`, 37 `d-4` and 38 `d-5` on the Congo; 36 `d-3` and 9 `d-4` on the Rhine.
- **Ramp cells:** the same as in layout 3. The first build lost the last ramp cell above each chute edge: the edge pass
  relabelled the source under it, and the ramp pass then took it for not open water. Fixed before these numbers.

**The release build** (`earthmap-0.16.8.zip`, the 0.16.7 structure guards included) gives the same counts at the
four sites ([256k](drop-shapes-test-2026-10-23/shapes-0.16.8-256k.txt)). Niagara in a 1M world
([1M](drop-shapes-test-2026-10-23/shapes-0.16.8-1m.txt)):
- **Lake rims hold:** no lake water above dry land; the curtain edge is 61 columns, as in 0.16.6.
- **Sills:** 95 of 98 pool pairs have one. The other 3 are a vanilla cave spring in the gorge wall at y 188, whose
  source the checker counts as a pool. It trickles down onto the river as falling water. It is not a drop of Earth's,
  but it is the kind of cave water at a river the user reported earlier.

**Offline** (256k): the river checks change only in the depth histograms, where sills make edge columns 1 deep. The
suite's one failure is Lake Geneva, as before.

## Ramps one column per height (0.16.9)

The rule study tested 0.16.8 with Hydrology 0.1.5 on a server, in Hydrology `docs/server-test-0.1.5/earth-0.16.8-shape.json`.
- **Stretched ramps:** the only water that reshaped. At Congo steps and chutes, and at Niagara, ramps repeated each
  height over 2–3 columns (`11223344556`). Vanilla keeps a flowing cell only beside a neighbour exactly one higher, so
  the first update rebuilt them: 229, 218, 336 and 39 cells.
- **Where they came from:** the river model's own ramp heights (along the river), which won over the settling's one
  column per height.
- **The fix:** in pool layout 5, settling drops the model's ramp heights before it lays the ramps out from the steps.

Measured with the ramp check in [shapes.py](river-drops-test-2026-10-22/shapes.py): a flowing cell stays as it is only
beside a neighbour one height higher, or with water above it. Data: [0.16.8](drop-shapes-test-2026-10-23/shapes-0.16.8-256k.txt),
[0.16.9](drop-shapes-test-2026-10-23/shapes-0.16.9-256k.txt).

| Flowing cells without a neighbour one higher | 0.16.8 | 0.16.9 |
|---|--:|--:|
| Congo | 2369 of 6013 | **0** of 4847 |
| Victoria | 12 of 164 | **0** of 164 |
| Niagara | 146 of 1519 | **0** of 1387 |
| Rhine gorge | 324 of 1643 | **0** of 1348 |

Sills are unchanged. Spills are now `d-2` to `d-5`: they follow the settled ramp beside them.

**With Hydrology 0.1.5.** The study's shape test (Hydrology `tests/server/scenarios-earth-shape.json`, `WA_WAKEALL=20`)
was rerun on `earthmap-0.16.9.zip`. Each site is recorded as generated, after every water cell is woken once, and after
its poke. Data: [hydrology-shape-0.16.9.json](drop-shapes-test-2026-10-23/hydrology-shape-0.16.9.json).

| Site | 0.16.8: first update, water / labels | 0.16.9 | After the poke (both) |
|---|--:|--:|--:|
| Ramp (Daugava) | 0 / 119 | 0 / 119 | 0 |
| Congo step | 229 / 11 | **0** / 7 | 0 |
| Congo rapids | 218 / 16 | **0** / 20 | 0 |
| Congo chute | 336 / 30 | **116** / 27 | 1 |
| Rhine chute | 80 / 136 | **2** / 155 | 0 |
| Victoria fall | 0 / 253 | 0 / 180 | 10 |
| Niagara fall | 95 / 205 | **0** / 258 | 8 |

- **Labels** are vanilla relabelling flow directions from neighbouring heights. Only the look changes.
- **The falls after the poke:** the harness breaks Earth's rock under the lip on its way out, and the lip sources
  above lose their floor. That is a harness artefact.
- **Still open: rapid marking at the Congo chute.** Most of its remaining changes are at one level of the chute: rapid
  ramp cells (6 to 1) and a few rapid edge sources turn into ordinary water of the same height, and 5 cells of rapid
  water spread. The Rhine's 2 are a rapid edge source turning ordinary. Earth marks the whole chute rapid, and
  Hydrology's rule (the water falls 2 or more within its window) doesn't see rapids there. The shape stays; only the
  rapid look changes on the first touch. Matching Earth's rapid marking to Hydrology's window is for the rule study's
  simulator.

## Rapids step by step (0.16.10, pool layout 6)

After 0.16.9, rapid water at chute ends turned ordinary on the first touch: 116 cells at the Congo chute and 2 at the
Rhine. The rule study worked out which steps Hydrology's rapids window (H4) keeps rapid (Hydrology
`tools/watersim/src/RapidRule.cs`, 729 of 729 edges in its simulator chutes). Earth 0.16.10 ports it into settling:
- **Edges:** an upper edge of a 1-block step is rapid when the river model has rapids there and the water falls 2 or more
  within H4's window on the settled columns. The window alone (the first build) also marked a long single Congo step
  rapid. H4 keeps either there, but rows with a vanilla water plant on the edge went back to ordinary water (174 cells),
  so the rule only takes rapids away. An upper edge beside several lower pools is rapid when any of them makes it rapid.
- **Ramps:** a ramp is rapid when its step's edge is; a ramp below a curtain is always rapid.
- **Spills:** a spill sits above the next step's ramp where a rapid ramp cell beside it is 2 or more high (was 3).
- **After the banks:** rapids are decided after the banks reach their final heights.
- **Wider margin:** pool layout 6 settles a chunk with a 20-column margin (8 before). The window reads settled ramps up
  to 6 columns away, and those depend on steps further out. Settled chunks read about 3 ms more columns.

Hydrology's shape test, first-update water changes ([data](drop-shapes-test-2026-10-23/hydrology-shape-0.16.10.json)):

| Site | 0.16.9 | 0.16.10 |
|---|--:|--:|
| Daugava ramp | 0 | 0 |
| Congo rapids | 0 | 0 |
| Congo chute | 116 | **2** |
| Rhine chute | 2 | **0** |
| Victoria fall | 0 | 0 |
| Niagara fall | 0 | 0 |
| Congo step | 0 (another step) | 87 |

- **The Congo step rows aren't comparable.** The test picks its step from the generated ramps, and 0.16.10 picked a
  different, rapid one. Its 87 cells are rows whose edge is a water plant. 0.16.10 only removes rapids compared with
  0.16.9 (and ramps below curtains were rapid in both), so it cannot add rapid-to-ordinary flips at a given step.
- **After the pokes:** the same as 0.16.9.
- **With Hydrology 0.1.6 (2026-10-24):** the water-plant rows are fixed on Hydrology's side. Plants under the ramps had
  hid the drop from H4's window. With 0.1.6 the Congo step changes 0 cells (was 87) and the Congo chute 1. See Hydrology
  `docs/server-test-0.1.6.md`.
- **Earth's own check** ([0.16.10](drop-shapes-test-2026-10-23/shapes-0.16.10-256k.txt)): every drop has its sill, and
  every flowing cell has a neighbour one height higher.

## White-water gorge sections (0.16.11, pool layout 7)

The user's Congo screenshot showed thin rapid stripes across a calm river. The user wanted rapids to look like
continuous white water.
- **What the study found** (Hydrology `docs/whitewater-2026-10-24.md`): 1-block steps 2–5 apart stay as generated, all
  rapid. Rapid water drawn over calm pools doesn't stay.
- **Why it can't be continuous:** at 256k the Congo's stretch falls only about 1 block per 35–46 columns. So continuous
  white water isn't possible; the fall can only be grouped.
- **The user's choice: gorge sections.**

Pool layout 7, new worlds only:
- **Chutes:** steps 5 apart. A chute (2–4 blocks, not forced by a lake or the river ahead) is laid out step by step,
  on across reach ends. HydroRIVERS' reaches on the Congo are only 3–40 blocks long.
- **Gorge sections:** where a chute begins and the valley ahead keeps falling (4 blocks or more within 256 at 256k),
  the river takes up to 6 steps, 5 apart, before a listed fall's hold or a lake. It cuts into a gorge and runs calm
  until the valley catches up. Gentle rivers stay as in layout 6: the Seine, Danube, Daugava, Rhine delta and Nile come
  out identical.
- **Rapids runs:** after a chute, the river keeps stepping every 5 blocks while its valley falls that fast.
- **Section depth:** a white-water section's pools are 3 deep across the full width. With 1-deep pools a broken sill
  flooded the pool up to the next step (198 and 109 cells at the Congo), because Hydrology's vessels rule can't tell
  that hole from a canal's front. The user's choice (2026-10-24): 3 deep, though a caravel can then pass the rapids.
- **Rapid marks:** an edge is rapid by Hydrology's window alone (0.1.6 fixed the water plants). A ramp is rapid when
  any rapid cell one higher feeds it. Spills also sit above curtains, where two steps meet on neighbouring rows of a
  diagonal river.

Results:
- **Congo:** about 27–32% more rapid ramp water (offline), grouped into sections
  ([picture](drop-shapes-test-2026-10-23/congo-layout6-vs-layout7.png); rapid surface 17.9% → 21.0% around the user's
  screenshot spot).
- **Worldwide audit:** unintended falls on centre lines go from 125 to 91, uphill spots from 92 to 78.
- **Hydrology 0.1.7 shape test** ([data](drop-shapes-test-2026-10-23/hydrology-shape-0.16.11.json)): 0 first-update
  water changes at all 7 sites.
- **Sill-break test** ([data](drop-shapes-test-2026-10-23/hydrology-sill-break-0.16.11.json)): every break changes at
  most 7 cells (the hole filling), with 0 first-update changes.
- **Earth's check** ([0.16.11](drop-shapes-test-2026-10-23/shapes-0.16.11-256k.txt)): every drop has its sill, and every
  flowing cell has a neighbour one higher.
- **Reviews:** two independent reviews (Codex's tool wasn't installed this round). Fixed:
  - steps landing past a lake or a fall's hold;
  - the look-ahead reaching a lake only when far enough ahead;
  - chutes laid out step by step not starting a rapids run;
  - a tributary's look-ahead taking the main river's valley;
  - pending steps not cancelled at lakes and forced drops;
  - an infinity-to-int overflow that kept gorge sections from starting where nothing lay ahead.

## Reviews

Two Codex reviews of pool layouts 4 and 5. Every problem they found is fixed and re-measured above:
- **The sill covered only 1-block steps,** so deep upper water above a drop of 2+ still touched the lower pool's level.
- **A lake-only chunk never settled,** so its sills at a river in the next chunk were lost. The first fix covered only
  a lower river outside the chunk. The second pass found the missing cases: lake-to-lake drops, a river above a lake
  chunk, and two levels inside one lake chunk.
- **Salt lakes at two levels** got no sills.

Codex found nothing in the layout 4 rims, the bit packing, the indexing or thread safety. It also found no change for
older pool layouts: all 24 096 valid packings of the older fields come out the same.

For 0.16.10, two more Codex reviews. Fixed:
- a settling margin too small for the rule (rapids at chunk borders depended on the chunk);
- rapids decided before the banks were final;
- steps beside a curtain skipped;
- an edge with several lower pools labelled by whichever came last.

## Not verified

- Not played in the client.
- Hydrology 0.1.5's tests of these shapes on an Earth world are done in the Hydrology repository (see its server test
  report).
