# 0061: Small streams in 1:25 to 1:10 worlds (proposal)

Status: **proposal** for the streams layer, not agreed or built. The quick step below (every stream from 20 m³/s at 1:25 and finer) is **built** at the author's choice ("Quick step first", 8 November), in Earth 2.0.0; not played. Date: 2026-11-08.

## What the author asked

The author, on 8 November 2026: "when done with large rivers and waters problems. Check possibility to add smaller
rivers. Now with detailed 1:10 map we see lot of dry rivers in mountain or hill valleys where rivers should be. This
would apply only to 1:25 - 1:10 maps."

## Why valleys are dry

- **The data stops at 20 m³/s.** `earthdata/rivers.bin` holds the HydroRIVERS reaches from 20 m³/s up: 619,723 reaches,
  about 2 million km. Its snapped copy on GLO-90 (`rivers-glo90.bin`) holds the same reaches.
- **By default a world draws even less.** `Streams` = `ends` draws the navigable rivers (from 100 m³/s) and each one's
  main branch upstream as a stream narrowing to one block at 20 m³/s. Tributaries of 20–100 m³/s off the main branch are
  left out unless a world uses `Streams` = `all`.
- **At 1:10 the land shows every valley.** On GLO-90 heights at 10 m per block, a valley with a catchment of a few km²
  is a clear landform hundreds of blocks long, but nothing in Earth's data puts water in it.

Two windows (GLO-90 relief with every HydroRIVERS reach coloured by discharge):
- **Bernese Oberland** ([map](../research/small-streams-2026-11-08/streams-oberland.png)): Earth draws only the Aare
  through the lakes of Thun and Brienz. The Lütschine (about 10 m³/s) is missing, so the Lauterbrunnen and Grindelwald
  valleys are dry. So are the streams of the hanging valleys (0.2–1 m³/s).
- **Gauja valley at Sigulda** ([map](../research/small-streams-2026-11-08/streams-sigulda.png)): Earth draws the Gauja.
  The Amata and the other side valleys carry HydroRIVERS streams of 0.2–20 m³/s that Earth leaves out. Many short
  ravines in the Gauja's walls have no HydroRIVERS line at all: their catchments are below its 10 km² minimum.

## The data on hand

HydroRIVERS v1.0 is already downloaded (`rawdata/hydrorivers`, 8,477,883 reaches; same licence and attribution as
today). Reaches added beyond today's 20 m³/s set, and an estimate of the snapped file's size at today's 12 bytes per km
(24 MB for 2 million km):

| Smallest stream | Added reaches | Added length | Estimated size |
| --- | --- | --- | --- |
| 1 m³/s | 1,764,198 | 7.0 million km | about 85 MB |
| 0.5 m³/s | 2,609,886 | 11.3 million km | about 140 MB |
| 0.2 m³/s | 4,225,924 | 18.2 million km | about 220 MB |
| 0.1 m³/s | 5,647,420 | 22.2 million km | about 270 MB |

HydroRIVERS' lines come from 15″ flow directions (about 450 m), so at 1:10 they run beside the valley floor in steps.
`tools/RiverSnap` already moves lines onto GLO-90's valley floors. It snapped all 620,000 large reaches in under a
minute, so millions of small ones should take minutes.

## Proposal

**1. A streams layer, only in worlds at 1:25 and finer** (new worlds; a rules version saved with the world):
- **Source:** the HydroRIVERS reaches below 20 m³/s down to a threshold. 0.2 m³/s is proposed: it fills mountain and
  hill valleys in wet climates, while desert wadis (long-term mean below it) stay dry.
- **Snapped** onto GLO-90 valley floors with RiverSnap, with their levels from GLO-90, as the large rivers are. HydroLAKES
  reservoirs are de-dammed as today.
- **Stored in 1° tiles** that travel with the GLO-90 heights (fetched with them, as the lake and river outline files
  are). Publishing to Earth's data release needs the author's yes.
- **Loaded by area.** The world generator reads the tiles it needs as chunks generate. The main river network stays as
  it is: building millions of reaches into it when a world starts would cost gigabytes and minutes.
- **Drawn simply:** a channel one block wide (two from about 5 m³/s at 1:10), its water at the snapped level, stepping
  down with the valley floor (Hydrology's rapids at the drops). It ends at the level of the river, lake or sea it flows
  into. No boats, no side arms or loop cutting.

**2. Optional, later: ravines.** Valleys under HydroRIVERS' 10 km² minimum would need streams traced from GLO-90 itself
(flow accumulation on the 90 m grid). Only if step 1 is not enough.

**A quick step on its own:** worlds at 1:25 and finer could default to `Streams` = `all`, which adds the 20–100 m³/s
tributaries that the data already has.

## Quick step (implemented, tall.22)

The author chose it first. What it does:
- **Setting:** earth.json's `Streams` defaults to `auto`. A new world under 37.5 m per block (1:25 and finer) saves
  `all`, every reach from 20 m³/s up; coarser worlds save `ends`. Configs with the old default `ends` move to `auto`
  (config version 5); `all` and `none` stay. Worlds keep the mode they saved.
- **Added at 1:10:** 215,249 reaches (about 730,000 km; 623,995 instead of 408,746) and 1.2 million segments (4,048,085
  instead of 2,820,862). Offline, the network alone builds in 4.4 s instead of 3.0 s. The model build of a new 1:10
  bench world took 34 s instead of 21–24 s.
- **What it fills:** side rivers of 20–100 m³/s that are not a navigable river's upper main branch: 570 more reaches in
  the Alps (45.5–48° N, 5.5–13.5° E), 358 in southern Norway, 125 in Latvia and Estonia. On the bench at 1:10:
  - the upper Adige in Val Venosta (23 m³/s, 867 m) is a stream about two blocks wide (`ends` follows the Eisack
    upstream from Bolzano);
  - the Lech below Reutte (28 m³/s, 864 m) is a stream two to three blocks wide with gravel banks;
  - the Aiviekste above Pļaviņas (82 m³/s) is a river about seven blocks wide in its valley.

  No Earth errors in the log.
- **What it does not fill:** valleys whose streams are under 20 m³/s, most of the author's dry valleys: the Gauja's
  side valleys at Sigulda get nothing, and in the Bernese Oberland only the lowest Kander (33 m³/s) is added; the
  Lütschine stays dry. These need the streams layer above.

## Questions for the author

- Threshold: 0.2 m³/s (proposed), 0.5 or 1 (smaller files, fewer streams).
- Scales: 1:25 to 1:10 as asked, or 1:50 too.
- Hosting: the stream tiles in Earth's GLO-90 data release (`earth-glo90`), downloaded with the heights.

## Risks

- **Levels on steep slopes:** a 1-block stream falling hundreds of blocks needs steps or waterfalls that hold their
  water. This is where the work is.
- **Line errors:** HydroRIVERS is wrong in places (karst, flat plains with braided streams, deltas); snapping fixes most
  in valleys and few on plains.
- **Generation cost** per chunk with many streams nearby; the per-area index keeps it local.
- **Dams and bridges:** small reservoirs below HydroLAKES' size stay flat water in GLO-90.

## History

- 2026-11-08: offline study at the author's request; written as a proposal. Maps from `rawdata/hydrorivers` and
  `rawdata/copernicus-glo90`.
- 2026-11-08 (morning): the author: "Quick step first". `Streams` = `auto` built in test build 1.0.3-tall.22
  (`aa11e30`): world options test passes up to its data-pack check (no publication zips in the worktree); offline
  network build at 1:100, 1:25 and 1:10; new 1:10 bench world (`t22a`): config moved to `auto`, streams `all`, upper
  Adige, Lech and Aiviekste with water, no errors. The streams layer below 20 m³/s is still a proposal.
- 2026-11-08 09:15: the author: "Yes, install". `earth-1.0.3-tall.22.zip` (sha256 `e2a53db8…`) installed in the player's
  Mods in place of tall.21; tall.21 and `earth.json` backed up in
  `Backups/earth-before-1.0.3-tall.22-20261108-091504`. Data packs unchanged. The player's `earth.json` (Streams
  `ends`, config version 4) moves to `auto` on the next start; existing worlds keep their rivers.

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: **proposal** for the streams layer, not agreed or built. The quick step below is **implemented** at the author's
choice ("Quick step first", 8 November) in test build 1.0.3-tall.22 (`aa11e30` on release/1.0), checked offline and on
a private bench, installed in the author's game (8 November 09:15), not played yet.
