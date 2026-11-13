# 0006: Earth-like ores (ore rules v3)

Status: Agreed direction, being implemented (0.14.1-pre) | Date: 2026-10-16 | Decided by: the user (direction, "can build and fix ore parts"); Claude (the rules below)

## Context

Ore rules v2 (0.13.0) give each deposit variant a rate class against vanilla and move a deposit to the nearest host
band when Earth's strata have no host at the drawn depth ("host search", `EarthOreGenerator.HostShift`). The first
in-game run on 2026-10-15 ([report](../research/ore-ingame-validation-2026-10-15.md)) found that attempt rates match
the design, but:

1. **Host search inflates host-limited ores.** Vanilla places nothing for most of their attempts; Earth moves every
   one of them to a host, across the whole column. Volume per attempt is 8–150× vanilla's on the same rocks (lapis 154,
   phosphorite 96, limonite 85, bituminous coal 33, galena 8; silver in galena follows galena).
2. **Hotspots whose rock column has no host place nothing** (Kimberley, Broken Hill, Sar-e-Sang, limonite at Pilbara
   and Kiruna, malachite in Chile).
3. **Halite and sylvite are far below target** (thick domes appear truncated).
4. **The prospecting pick is biased.** `HostFraction` divides the host depths by the whole column height instead of
   the depth distribution placement draws from, and its host-search guess differs from placement. Most ores read 2–8×
   low; halite reads 4× high.
5. Blue clay is missing (upstream of the ore code: geology or terrain).

The adversarial review of 2026-10-16 found the same (its findings "calibrate ore placement" and "prospecting
probability").

## User direction (2026-10-16)

In the user's words:
- Two configurations: **vanilla ore generation** and **Earth-like ores**.
- Coal is too scarce in vanilla. The coal basins of the UK, Germany, Poland and elsewhere were practically unlimited
  for a single person in the 13th century. Coal deposits should be **very rich in the coal basins**: very large deposits
  concentrated in one place.
- That supports **transport**. Earth doesn't build transport networks; rich, concentrated resources make ship and rail
  mods worth using.
- **Keep locally critical resources, copper first, so progression is possible** everywhere.
- Earth's ores must **follow the game's world settings** for ore frequency and spawn scaling.

The user first deferred the fix ("leave ores for now"), then asked for it with this direction ("can build and fix ore
parts").

## Decision (rules v3)

**The two modes.** `RegionalOres = false` is vanilla ore generation: vanilla's ore deposits, rates and ore maps, on
Earth's rocks. `RegionalOres = true` is Earth-like ores, rules v3 below. Both take the world's settings. Clay, peat and
salt beds are soil and sediment, owned by regional geology: while `RegionalGeology` is on they follow Earth's wetness,
relief and substrate gates in both modes (saved with the world's geology rules); fully vanilla soil deposits need
`RegionalGeology = false` as well.

1. **A deposit stays in its own depth range.** When Earth's rocks have no host at the drawn depth, the deposit may move
   to the nearest host band within the variant's own depth window (the whole-column search is gone). A moved deposit is
   kept only as often as a deposit there would have found its host by itself: with the share of host rock in that
   window. So Earth no longer turns vanilla's misses into hits (lapis 154x, phosphorite 96x per attempt).
2. **Known districts fill any rock.** At strong evidence (heat 0.6 and above), a deposit places its ore in whatever rock
   it meets. Where that rock isn't one of the provider's hosts, it uses the ore block of the most similar host (marble,
   limestone or dolostone take chalk's, granite basement takes granite's or the nearest igneous one). Districts with no
   suitable rock in Earth's column (Kimberley, Broken Hill, Sar-e-Sang, Pilbara, the Chilean copper) get their ore, and
   salt domes in salt basins reach their full height.
3. **Coal basins are coal measures; the great districts are rich ore bodies.** Where the coal evidence is strong, up to three continuous seams run under the
   whole basin: 1–4 blocks thick, the first only a few blocks below the surface (where people dug coal in the 13th
   century), the others deeper. They follow the land, thin out and break up towards the basin's edge, and are a pure
   function of position, so every chunk agrees and a basin holds hundreds of thousands of blocks. The upper seams are
   bituminous coal, the deepest anthracite. `Ores.CoalBasins` scales them (1 = default, 0 = off). The great iron
   districts (iron evidence .55 to .85: Kiruna, Krivoy Rog, Lorraine, Carajás) get iron formations: separate ore bodies a
   few hundred blocks across, a limonite cap 2–8 blocks down, hematite about 18 and magnetite about 36 down, up to 3, 6
   and 8 blocks thick, each body a hundred or more vanilla iron deposits (`Ores.IronFormations`). Every other resource's
   main rock-hosted deposit gets a rich district from evidence .7 to .9: one bed of large lenses at its usual depth, 2–5
   blocks thick (`Ores.RichDistricts`). All are graded towards rich ore and scaled by the world's deposit rate.
4. **Copper and tin everywhere.** Surface starters (surface copper, surface tin) are at least vanilla's attempts
   everywhere and fill any rock (limestone and dolostone had none), so every start has nuggets near it. Deep copper
   gets tin's floor (0.19 instead of 0.09).
5. **World settings.** Attempts already follow vanilla's own loop: `globalDepositSpawnRate` (GenDeposits'
   `chanceMultiplier`), the surface copper and surface tin settings (`triesPerChunk`) and `ScaleWithWorldheight`. Coal
   seams and prospecting readings now use the same global rate.
6. **The prospecting pick reads what is placed.** For each variant it averages, over the depths placement draws, the
   share of the deposit that would land in host rock (moved, kept or filling any rock as above). Coal seams are added.

## Tunable maps (2026-10-17)

User direction, in their words: blend a real-data heat map and a uniform background map ("kind of vanilla but bit
limited to plausible ores in rock"), with "weights for each map in config", an "intensity factor" for the real sources,
and "game ore settings that scale quantity (and chance) per chunk for final heat map", so balancing doesn't need code
changes. The model below is that, expressed in config (`Ores` in `earthmap.json`, saved with each world):

1. **Two maps, two weights.** Attempts per chunk = vanilla's x Frequency x (BackgroundWeight x Background +
   EvidenceWeight x (Hotspot - Background) x evidence). The background map is the class floor, gated by Earth's host
   rocks; the evidence map is the real data. `BackgroundWeight` and `EvidenceWeight` (0..10, default 1) are global and
   per resource (`Ores.Resources.<code>`); a per-resource value replaces the global one, like every other override.
   An evidence weight of 0 leaves the background alone and switches the resource's ore bodies off.
2. **Intensity of the real sources: the district ramp.** Where the evidence makes a rich district is set per resource
   by `DistrictFrom` (no ore bodies) and `DistrictTo` (full ore bodies), and how rich by `Richness` (0..3, times the
   family setting `CoalBasins`, `IronFormations` or `RichDistricts`; the product is capped at 3). Built-in ramps: coal
   .45 to .75 (was .3 to .6: the 0.5° grid blurred the Ruhr's ramp over 76,000 km²), iron .55 to .85, others .7 to .9.
   A radius per source is not offered: the 0.5° grid already spreads each source, and a wider radius would only make
   districts bigger; the ramp is what makes them tighter.
3. **Depth stays 2.5D.** The data almost never gives a deposit's depth, so a 3D map would be invented. Each cell keeps
   a shallow and a deep value with its least burial (deep evidence), the strata decide where hosts are, and
   `MinDepthBlocks` / `MaxDepthBlocks` per resource limit the depths (for ores that only occur near the surface).
4. **The game's settings come last.** `globalDepositSpawnRate` scales every attempt, the ore bodies' richness and the
   prospecting readings, as before.

Changing these for an existing world needs `UpgradeExistingWorldOreRules` once and affects new chunks only
([0002](0002-save-compatibility.md)).

## Alternatives considered

- *Calibrate each ore's chance from the 2026-10-15 measurement.* Tied to one game version and one provider set; the user
  didn't follow it.
- *Accept a moved deposit with the column's host share, anywhere in the column.* Still moves surface ores to depth.
- *Synthesize host rock lenses in geology.* Geology owns rocks, and would need geology rules v3 with visible contacts in
  existing worlds; ore blocks of the nearest host give the same gameplay.
- *Coal as huge disc deposits.* Discs are capped at 64 blocks and depend on attempts; seams as a function of position
  have no size limit and no chunk-order effects.

## Consequences

- Regional-ore worlds take rules v3 in chunks generated from then on ([0002](0002-save-compatibility.md)). A coal seam
  ends where it meets an older chunk (underground only).
- Rates need the in-game harness again (`docs/research/ore-ingame-validation-2026-10-15/`), now with coal basins and the
  Baltic start.
- Blue clay (problem 5) is geology's, not part of this.

## History

- 2026-10-15: measured in game; problems and proposals recorded in the report, fixes awaiting a decision.
- 2026-10-16: adversarial review confirmed findings 1 and 4; the user deferred the fix and gave the Earth-like ore
  direction above.
- 2026-10-16: the user asked for the ore parts to be built and fixed, keeping copper reachable and following the game's
  ore settings; rules v3 designed as above and implemented. Offline tests: coal basin strength at the named basins,
  seam continuity, cover and depth, similar-host choice.
- 2026-10-16: iron formations ("very large iron deposits like Kiruna; one mine for many players") and rich districts for
  every other resource ("all very large deposits very rich compared to vanilla; the game's frequency setting tunes it")
  added as ore bodies: `OreRules.BedSet` for coal measures, iron formations and `OreRules.District`, settings
  `Ores.CoalBasins`, `Ores.IronFormations`, `Ores.RichDistricts`, all times the world's deposit rate. Children (gold and
  silver in quartz, sylvite) and gem pockets keep their deposits.
- 2026-10-17 (review round 11): ore bodies follow each resource's saved settings (concentration, depth limits, switched
  off at frequency, hotspot chance or concentration 0), in placement and prospecting; district depth up to 250. Round
  12: an explicit hotspot chance of 0 counts (`OreTuning.OreBodiesOff`), and prospecting skips layers below the world's
  floor.
- 2026-10-16/17: measured in game (three worlds; see the ores doc, "Validation status"): no-evidence inflation gone,
  copper at vanilla's reach around the Baltic start, coal measures, iron formations and every district in place.
  Kimberley has about 380 diamond blocks per chunk: very rich, as asked, and tunable.
- 2026-10-17: the user asked how much ore a basin holds in total; the Ruhr's coal measures covered 76,000 km² (6.6
  million blocks at a 204,800-block ring), because the 0.5° grid blurs the evidence. The user asked for config to tune
  it and proposed the blended map model; built as "Tunable maps" above (still rules v3: v3 was never released). The coal
  ramp moved to .45 to .75.
- 2026-10-16 (review round 4): prospecting now applies placement's per-depth acceptance and decides "fill any rock" by
  the heat at each sampled burial; a moved deposit's climb to the top of its host band stops at the window's top.
- 2026-10-16 (review round 5): prospecting moves a deposit under exactly placement's condition (no host at the drawn
  depth, even if lower layers reach one) and leaves out what MaxDepthBlocks removes. The two modes are worded
  precisely: clay, peat and salt beds belong to regional geology in both.
- 2026-10-16 (review round 6): child readings (gold and silver in quartz, sylvite in halite, ...) apply the child's own
  per-depth acceptance and MinDepth/MaxDepth at each filled block of the parent's sampled deposits (round 7: block by
  block, so a thick salt dome's deeper part counts), as GenerateDisc seeds children and GenerateChild keeps them.
  Round 8: a child's blocks per seed count the parent's thickness (one layer of a thin sheet, the whole cloud inside a
  salt dome), where v2 always assumed one layer.
