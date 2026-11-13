# Realistic Water + Realistic Rapids with Earth — server-side test (2026-10-22)

This test checks how blondie's [Realistic Water](https://mods.vintagestory.at/show/mod/47649) 0.4.0 and
[Realistic Rapids](https://mods.vintagestory.at/realisticrapids) 0.1.3 behave in an Earth 0.15.6 world (`3be1209`,
Debug build) on Vintage Story 1.22.7.

The questions were what happens when a player:
- digs a 2-wide trench from a river or from the sea;
- dams an Earth river;
- disturbs one of Earth's river steps (the flowing-water ramps);
- builds a vanilla water wheel on Earth's rapids.

Nothing in Earth was changed, no save was opened, and nothing was installed in the game folder. All worlds are new and
live in the git-ignored `testruns/water-20261022b`, `-c` and `-probe` folders (about 1 GB in total, safe to delete).

## Verdict

| Question | Vanilla water (control) | Realistic Water + Rapids |
|---|---|---|
| 2-wide trench from a river, 40 long | 6 blocks of flowing water, then it stops | **Whole trench fills with source water** (navigable). This happens whether the trench is opened last or dug outward from the river. |
| 1-wide trench from a river | 6 blocks | Whole trench fills with flowing water, but no sources (so it dries up if cut off) |
| 2-wide trench from the sea, **dug outward from the water** | 6 blocks | **Whole trench fills with salt source water**, following the digging (about 1 row per second) |
| 2-wide trench from the sea, dug dry and **opened last** | 6 blocks | Only 6 rows (sources); the rest stays dry |
| Dam across an Earth river (Daugava, 19 wide) | Nothing changes outside the dam itself | The same: no rise upstream, no drying downstream |
| Player pokes a river step (places and removes a block) | 48–153 pool cells within 5–10 blocks turn from source to flowing water. In 14 columns the surface drops a little. | 9–19 cells change within 3–7 blocks; part of the ramp disappears. No spreading along the river. |
| Vanilla 3 m water wheel on Earth's rapids (Rhine Falls) | **Turns** (flowRate 7.24, 3 rapid cells) | **Stops after about 2 s.** Placing the wheel turns 81 of Earth's rapid-water cells within 21 blocks into nothing. |
| Mill: 20-block channel → 15-deep pit → tunnel, vanilla wheel | Water never reaches the pit | The pit's bottom 5 cells become rapid falling water. A wheel **in the fall turns** (flowRate 7.68). Beside the fall or 8 blocks down the tunnel it gets 0. |
| Server tick | mean 46–47 ms | mean 46–47 ms, with no extra spikes |

In short:
- Realistic Water does what the user wanted for river canals and sea-level cuts, provided the cut is dug from the water
  outward.
- A mill with Realistic Rapids works when the wheel sits in a fall at least 10 blocks high.
- Damming does nothing, because Earth's rivers are infinite source water in both versions.
- The one conflict: **Realistic Rapids removes Earth's worldgen rapids on first touch**, so vanilla wheels on Earth's
  rapids stop working when that mod is installed.

## What the mods do (read from the 0.4.0 / 0.1.3 code)

Both DLLs were decompiled to read them; the decompiled code was not kept in the repository.
- **Realistic Water** replaces the spreading behaviour of `water` and `saltwater` with `RealisticSpreadingLiquid`.
  - Fresh flowing water becomes `realisticwater-{flow}-{level}-{sublevel}`, with 20 sublevels per level. That is why it
    travels about 100 blocks.
  - Salt water keeps vanilla's 6-level flowing blocks, so it reaches only 6 blocks per spread.
  - "Lake extension" turns a flowing cell into a source when it rests on solid ground or a source and has two adjacent
    sources (or one adjacent and one diagonal).
  - The converted cell is set without a block update, so it does not spread again by itself. That is why a salt-water
    trench opened all at once stops at 6 rows. Digging outward wakes each new cell, so the source front follows the
    player.
  - Sources never drain. A dam therefore changes nothing up- or downstream, and the same is true in vanilla.
- **Realistic Rapids** replaces the spreading behaviour of `rapidwater`.
  - Rapid water at level 1–4, or with no higher neighbour, is lowered on its next update and turns into normal water or
    air. A rapid source becomes `water-still-7`.
  - New rapids appear only at the foot of a fall: when water spreads down under 10 cells of non-source water, the cell
    becomes `rapidwater-d-6`.
- **Vanilla wheel** (`BEBehaviorMPWaterWheel`):
  - checks its rim cells once a second;
  - needs non-still flowing water with `flowSpeed` > 1.5, which only rapid water has (2). Fresh water and Realistic
    Water's direction-less `realisticwater-still-*` give nothing.

## Method

| World | Mods | Purpose |
|---|---|---|
| **rw** | Earth 0.15.6 + Realistic Water 0.4.0 + Realistic Rapids 0.1.3 | under test |
| **control** | Earth 0.15.6 | vanilla water |

Both worlds were 256000 × 256 × 102400 with seed 386217, an otherwise default Earth config, and story-location moves
turned off. Realistic Water ran with its defaults (lake extension and rivulets on, search distance 20).

The test-only helper mod [wateraudit](realistic-water-test-2026-10-22/wateraudit) works in five steps:
1. It loads 7 × 7 chunk columns around each site and keeps them loaded.
2. It finds the feature for each scenario in [scenarios2.json](realistic-water-test-2026-10-22/scenarios2.json): still
   water next to a long strip of land, a pool to dam, a ramp on a pool, or a rapid ramp.
3. It does what a player would. It breaks blocks with `BreakBlock`, which gives the same neighbour updates as mining.
   It places cobblestone and clears the fluid. For water wheels it places the block and marks its construction complete.
4. It dumps solid and fluid ids in a 113 × 34 × 113 box at 0, 15, 30, 60, 120, 300 and 600 s.
5. It samples each wheel's `flowRate` and power-source count every 4 s, and the server tick time every 20 ms.

[analyze.py](realistic-water-test-2026-10-22/analyze.py) compares the dumps with the t0 dump of the same world. The
results are in [summary.json](realistic-water-test-2026-10-22/summary.json) and
[analysis.txt](realistic-water-test-2026-10-22/analysis.txt).

Sites:
- river trenches on the Daugava at Jēkabpils;
- sea trenches on the Baltic coast at Ventspils, plus one at Cascais near Lisbon;
- mills at the Rhine near Speyer;
- the dam on the Daugava at Daugavpils;
- steps on the Elbe at Dresden and the Daugava at Koknese;
- rapids just below the Rhine Falls.

A "row" is one block of trench length.

To reproduce:
1. Build `wateraudit` with `dotnet build -c Release`.
2. Put the ModDB zips into `realistic-water-test-2026-10-22/dl/` (zips are git-ignored):
   - `realisticwater_0.4.0.zip`, sha256 `2b6620db…a125`;
   - `realisticrapids_0.1.3.zip`, sha256 `701d4d5d…dc2c`.
3. Run `mkrun.py <absolute run dir> <rw|control> <port> <absolute scenarios file>` once per world.
4. Run `analyze.py <runs dir>`.

The run dir must be absolute: the server runs with the game folder as working directory.

## Findings

- **River canals work** with Realistic Water. Both 2-wide trenches cut into the Daugava's bank filled all 40 rows with
  source water:
  - the one opened last within 15 s;
  - the one dug outward within a second of each row being dug.

  The 1-wide trench filled with flowing water only, as the mod page says. Vanilla stops at 6 in every case.
  Earth's river banks are mostly flush with the water (land top at the water level), so a 1-deep cut is enough.
- **Sea-level cuts work if dug from the sea**. Two cases differ:
  - Dug outward one row per second, the 2-wide trench filled with salt source water over its whole length.
  - Dug dry and opened last, it filled 6 rows (Ventspils and Cascais alike).

  So a Panama-style cut has to be dug from the water inward, or extended a block at a time after opening. The cut runs
  at sea level and cannot cross ground above it: neither mod has locks or pumping.
- **Dams do nothing**. In both worlds the only changed cells were the 231 that the dam itself replaced. Earth's rivers
  are source water in every pool, so nothing is "fed" from upstream. A reservoir that fills, and a river that dries below
  a dam, need a different mod (the reservoir-basin idea from the earlier discussion).
- **River steps survive** in both worlds:
  - With Realistic Water, poking a ramp changed only a few cells within 7 blocks, and the river settled within 15 s:
    - Dresden: 10 of 125 ramp cells removed.
    - Koknese: 1 removed and 5 turned into sources.
  - Vanilla changed more. 153 and 48 pool source cells turned into level-6 flowing water within 10 and 5 blocks.
  - Neither world spread anything along the river.
- **Earth's rapids and Realistic Rapids conflict**. Earth places rapid ramps, `rapidwater-{dir}-{6..1}`, on the lower
  pool of fast steps. Under Realistic Rapids every rapid cell at level 1–4, or without a higher neighbour, is lowered as
  soon as anything touches it:
  - Placing the wheel removed 81 rapid cells within 21 blocks.
  - The wheel turned for one sample (flowRate 4.24 at 2 s), then had no power source.
  - In vanilla the same wheel keeps turning (7.24).

  New rapids only appear below falls of 10 or more blocks of non-source water. Earth's waterfall curtains were not
  tested.
- **Mill pit**. A 20-block channel from the Rhine, a 15-deep 2 × 2 pit and a 4-high tunnel at its foot gave:
  - source water in the channel;
  - falling Realistic Water down the pit, with the bottom 5 cells as `rapidwater-d-6`;
  - direction-less `realisticwater-still-5-*` in the tunnel.

  A wheel with its axle in the falling column (`under`) turned at flowRate 7.68, comparable to the wheel on Earth's
  rapids in vanilla. A wheel beside the fall (`side`) or 8 blocks down the tunnel got nothing. That matches the mod
  page's "wheel directly below the waterfall".
- **Server cost**. Mean tick time was the same in both worlds (46–47 ms; the target is 50 ms). The only spikes, about
  1.1 s once per window, are wateraudit's own dumps and appear in both worlds. The test had no players and only these
  small changes; large wetlands forming or draining were not measured.

## Side finding: trenches undone in the first run

In the first run (`testruns/water-20261022b`), both Cascais sea trenches were back to the untouched terrain at 300 s.
Their solid and fluid ids were identical to t0, in **both** worlds, so this was not Realistic Water. The second run dug
the same trench at the same place, and it stayed through 600 s. The first run did not log chunk unloads. The second, which did, logged none for the
test sites. The cause was not found.

## Not verified

- Client side: rendering of `realisticwater-*` blocks, boats on the new canals, and the wheel animation.
- Multiplayer with players. Chunk unload and reload of Realistic Water blocks over a longer time.
- Earth's waterfall curtains (`water-d-*`) once disturbed under Realistic Water and Realistic Rapids.
- The rivulets Realistic Water generates in mountains (enabled by default) next to Earth's frozen-spring patch,
  decision 0021.
- Hydrate or Diedrate and True Archimedes Screw together with these mods.
