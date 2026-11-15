# 0056: Rocks and ground apart from Ores; the ground pack required

Status: built, in Earth 2.0.0 (two choices, ground columns by default), merged with the author's "Yes, merge" (7 November); the required ground data pack is replaced by 0066's download. New worlds only.
Date: 2026-11-07.

## What the author asked

"For rocks, ground and soil we need UI setting for player to select. and new ground rock would be default. Then ore
spawns are separate UI - vanilla (default for now) and our heatmaps (still needs rework)." Asked whether that meant
ground columns as the default with the ground pack required, the author answered "Yes, split it".

## Decision

The Earth tab's single "Rocks and ores" (`earthOres`: regional, groundcolumns, vanilla; vanilla by default since
6 November) becomes two choices:

| Choice | Key | Values | Default |
|---|---|---|---|
| Terrain · Rocks and ground | `earthRocks` | `groundcolumns` (regional rock regions plus ground columns), `regional` (rock regions only), `vanilla` | `groundcolumns` |
| Terrain · Ores | `earthOreDeposits` | `vanilla` (the game's deposits), `maps` (Earth's ore heat maps, experimental, being reworked) | `vanilla` |

- `earthRocks` sets EarthParams.RegionalGeology (not vanilla) and EarthParams.GroundColumns (ground columns);
  Ground depth still applies only with ground columns.
- `earthOreDeposits` alone sets EarthParams.RegionalOres. Every combination is allowed, including vanilla rocks with
  the ore maps; the two systems were already independent flags in earth.json.
- All five play styles take ground columns with vanilla ores.
- A world configuration that still carries the old `earthOres` key (worlds made by scripts or old server configs)
  keeps its meaning where the new keys are missing: vanilla is vanilla rocks and ores; regional and groundcolumns are
  that rock choice with the ore maps. The new keys win where both are present. Saved worlds are not affected: the
  choices are frozen into EarthParams at creation.
- Earth requires `earthgrounddata` (modinfo dependency, minimum 1.0.1). The pack becomes Universal and required on
  client and server like the other data packs, because the game's loader resolves dependencies on the client too
  (`ModLoader.CheckAndSortDependencies`, 1.22.7): a server-only pack would leave Earth unresolved on joining clients.
  `tools/package-publication.py` refuses to pack when modinfo does not require it. Without its tiles (development
  setups) a world still logs the missing pack and generates with regional geology.

Earth's own ore and gem deposits (`assets/earth/worldgen/deposits/earth-ores.json`, `earth-gems.json`) are ordinary
deposit assets, so they spawn under either ore choice; with the ore maps they are bound to the heat maps like any
deposit the catalogue knows.

Rates (the author, 7 November: "can we fix it and make vanilla type chances for vanilla ore setting"): the first
wiring gave Earth's deposits 0.1-0.4 tries per chunk, so they placed 9-65x fewer ore blocks per chunk than vanilla's
own ores and the gems 50-200x fewer. Their tries now give the ore blocks per chunk of a vanilla counterpart, computed
with vanilla's disc generator (radius, thickness, edge thinning; `tools/earth_deposit_rates.py`): chalcopyrite as
malachite, cerussite as galena, smithsonite and hemimorphite as sphalerite, sperrylite as chromite, pyrite as sulfur;
azurite, tetrahedrite, vanadinite and wulfenite keep their authored rarity against the family's leader; each rough gem
(topaz, tourmaline, beryl, spinel, garnet, ruby, sapphire) places as much as emerald, its colour deposits sharing it.
Earth keeps its own host rocks and depth bands, so the shallow lead and zinc minerals sit in a 4-30 block band rather
than vanilla's whole column. The ore maps scale these tries like any vanilla deposit's. On the bench (default 1:200
world, four 128 x 128 boxes to 110 blocks deep): chalcopyrite 607 blocks beside native copper 727, pyrite 2772 beside
sulfur 1567, smithsonite 231 beside sphalerite 1344, garnet 86 and spinel 40 beside emerald 110.

## Tests

`tests/EarthWorldOptionsTests`: both rock choices with both ore choices, vanilla rocks with the ore maps, the old key's
three values, the new keys winning over the old one, invalid values falling back to earth.json, and the menu defaults
(ground columns, vanilla ores, no `earthOres` entry left).

## Later: the whole ground column (GenFixRules 4, 14 November 2026)

`EarthGeology.RegionalRock`, which the ore plans' host search, prospecting and the water table read, knew only the top
512 blocks of a ground column (`GroundIds` was 512 long), so below that it answered regional geology's bed or nothing
where the ground pass had placed the ground column's own blocks (a top above y 512: 1:10 and 1:5 mountains). New worlds
(`GenFixRules` 4, code review ER-20) read the column to its bottom; older worlds keep the 512 blocks.

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: implemented in source (`claude/ground-columns`, commit 9718956), offline tests pass; loaded on the
AgentBridge bench on 7 November (Earth with `earthgrounddata` 1.0.1 as a separate zip; the default 1:200 world got
ground columns and vanilla ores, no Earth errors); merged into release/1.0 at a3381cd with the author's "Yes, merge" (7 November). New worlds only.
