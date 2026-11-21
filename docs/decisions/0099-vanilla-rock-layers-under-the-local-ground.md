# 0099: Vanilla's rock layers under the local ground (vanilla strata rules 1)

Status: **in Earth 2.2.0** (21 November 2026), built on `claude/vanilla-strata`, merged into `main` (18 November 2026); new Earth worlds
with vanilla rocks. Decided by: the author ("Yes, do EA-0181", 18 November 2026), how it is built by Claude as
maintainer (D72). Looked at in game on the AgentBridge bench the same day (EA-0181: a cut at the
Grand Canyon rim, columns at Addis Ababa, Lhasa and Vienna, the Matterhorn; shots in the hub's `backlog/media`).

## What the author asked

After deposit depth rules 1 ([0097](0097-vanilla-deposits-below-the-local-ground.md)) put vanilla's deposits within reach
under plateaus, iron was still missing at most highland places because its host rocks were not there; the author asked
"why all granite in highlands?" and to fix it (EA-0181).

## The problem

Vanilla fills a column with its default rock (granite, `global.json`), then lays layers into it (`rockstrata.json`):
basalt (volcanic provinces), the sedimentary rocks (claystone, sandstone, shale, conglomerate, limestone, chert, chalk),
bauxite and slate from the top down; kimberlite, phyllite, andesite, peridotite, basalt and granite from y 1 up. Each
layer's thickness comes from a noise map and the province's maximum, both times world height / 256. And each sedimentary
layer is thinned by half the column's height above sea level, so that vanilla's mountains (50-100 blocks above sea)
show their hard core. On Earth's terrain:

- under a plateau 150-400 blocks above sea level (1:10) the sediments are thinned away and the bottom-up layers lie
  250-480 blocks down, so the top 120 blocks are granite (La Paz, Zermatt, Tehran 100 %; Mexico City 70 % with 30 %
  bauxite);
- in a tall world every layer is 4 times as thick (1024 high), so one layer fills the reach even in lowlands (Vienna
  100 % sandstone, Riga 96 % shale at 1:10).

Vanilla's ores each take a few rocks (limonite basalt, shale or chert; magnetite andesite, claystone, chalk, conglomerate
or slate; coal, sulfur and salt sedimentary rock), so where their hosts were missing they were missing too.

## Decision

New Earth worlds with vanilla rocks save `EarthParams.VanillaStrataRules` = 1 (field 219). In them (`EarthVanillaStrata`):

1. **Each column's layers are the standard world's for a column standing at sea level plus its relief**, laid with their
   top at the real ground. The relief is the ground's height above the regional surface: Earth's smoothed terrain over
   about 0.6 of a half-degree cell, as regional geology's rules v2 use for their beds (`GeologyReference`, without its
   undulation). So:
   - a plateau (little relief) keeps its sediments, and its bottom-up layers lie about a sea level's depth down, as under
     a lowland;
   - a ridge standing above its surroundings is thinned as a vanilla mountain of that height is;
   - below the scratch column's bottom the default rock stays.
2. **The standard world's thicknesses**: each new region's layer maps and the provinces' maximum thicknesses are scaled by
   standard height / world height (the standard world is 256 high at sea level 110, or the vanilla world with this world's
   sea level), so a tall world's layers are as thick as the standard world's.
3. **Vanilla's own code lays them.** Earth wraps vanilla's rock layer handler in the terrain pass (through the game's
   public handler list), runs vanilla's column code on a scratch column whose height is the column's scratch top, and
   copies the result under the real ground, changing only blocks that are still the default rock, as vanilla does.
4. **The prospecting pick reads the same column** (built the same way with a strata generator of its own), through
   Earth's existing hook on the pick's reading.
5. **Saved per world** (decision 0002): worlds made before keep vanilla's layers; worlds with Earth's regional rock lay
   their own (field 0). The patches are a `PatchVanilla` table (`VanillaStrataTable`), checked against the game by
   `PatchVanillaTests`; a world with the rules refuses to generate without them (decision 0083). A pass that throws fails
   the chunk as Earth's other passes do (`EarthWorldGen.FailedPass`).

## Alternatives considered

- **Thin sediments by nothing** (every column as if at sea level): plateaus and mountains alike keep all sediments;
  vanilla's mountain look (a hard core in high ground) is lost.
- **Thin by height above sea level, as vanilla**: what Earth had; plateaus become granite.
- **Earth's own column code instead of vanilla's**: would copy vanilla's rules into Earth and drift from vanilla's
  layer list and province data on a game update or with rock mods; running vanilla's own code keeps them.
- **Only the thickness change** (no relief): fixes tall lowlands, leaves plateaus granite.

## Consequences

- Census on dedicated servers ([vanilla-strata-2026-11-18](../research/vanilla-strata-2026-11-18/)): more layers within reach
  at most places (Vienna and Riga at 1:10 from one rock type to four; Lhasa, Mexico City and Johannesburg gain sediments);
  coal, sulfur and salt on plateaus where they had none; lowland sediment ores at vanilla's standard amount (coal on the
  Pannonian plain at 1:10 from 859 to 131 a chunk column, its sedimentary layers now a quarter as thick, as in the
  standard world).
- Iron stays as patchy as vanilla's own design makes it (each ore's own map and hosts): it appeared at some places and
  left others.
- Rock layers look thinner and more varied in tall worlds, and plateaus show sediments. On the bench (1:10): the Grand
  Canyon rim has claystone 0-18 blocks down, granite to 120, andesite at 124-136, flat and continuous across a chunk border;
  Addis Ababa chert, slate, peridotite and andesite above granite at 108; the Matterhorn's peak shows its core.

## Not covered

- Glacier ice: the ground is the terrain height map's, as vanilla's own pass reads it.
- The relief scale (about 30 km) is regional geology's; it was not tuned for this.

## Tests

`PatchVanillaTests`: the table binds on the installed game (1.22.7) without a line, replaces vanilla's handler, calls
through, and gives vanilla's handler back when released; the scratch top's arithmetic. `EarthWorldOptionsTests`: vanilla
rocks save rules 1 (with either ore choice), regional rock 0; the field is in the pinned list; the 295 pinned new worlds
differ only by it (checked by forcing it to 0). The self-test reports "vanilla rock layers: PASS; rules 1, active".
