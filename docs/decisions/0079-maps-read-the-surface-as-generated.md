# 0079: Maps read the surface as generated (`GetSurfaceColumn`)

Status: **built** (EA-0071, for World Map's WM-0023). No change to generation, saves or rules versions: every world,
old or new, generates as before. Checked offline and in a headless 1:10 world; not seen in game by the author yet.
Date: 2026-11-14.

## Why

The author: World Map does not show volcano craters (WM-0023, 13 November 2026). World Map predicts the world from
`TerrainModel.GetColumn` alone (its decision WM-D03: predictions never generate chunks). Earth carves its summit craters
and their lava lakes in a later Terrain step (`EarthVolcanoes.Crater`, order 0.71, 0057 part 2 and 0078) that writes only
the chunk's blocks and height maps, so every predicted view showed the uncarved cone. The crater cannot go into
`GetColumn` itself: the crater's plan is built from `GetColumn` (its vent, rim and floor), and `GetColumn` also answers
Hydrology's river query, the water table and story placement, which must not change.

## Decision

- `TerrainModel.GetSurfaceColumn(double x, double z)` returns `GetColumn` with the top lowered to what later steps carve:
  a crater's floor and walls, or a lava lake's top (lava is not water; `Water` stays as it is). Elsewhere it is
  `GetColumn`. For maps only; generation never reads it.
- The later steps register themselves on the model: `TerrainModel.Carved` (an object made with the model, so the copy
  World Map takes when the game starts sees what is set later) holds `Craters`, a function from a block column to the
  surface y a crater or lava lake leaves there. `EarthVolcanoes` sets it once it has indexed the volcano bodies (rules 2
  and 3) and clears it when the world closes. `VolcanoPlan.SeenSurface` is the one shape both use: the lava's top in a
  lake (the rain height `Crater` writes), else `CraterSurface`.
- **Plans are never made from built-in heights for a map.** A map thread uses the plan generation made, or makes the
  plan itself only from the full heights, as `/earth volcano` does (CR-D1): on a whole-world sweep's thread
  (`FineElevation.BuiltInOnly`: World Map's world chart, its climate raster) it answers no crater, and a plan whose planning
  read a built-in height (a tile not on this machine) is neither used nor kept, and is tried again after a minute. Such a
  plan would be wrong (rules 3 sample the cone's land out to tens of kilometres), and kept it would make generation carve
  a wrong crater and cone. The world chart (about 49 blocks a texel at 1:100) could not show a crater anyway; World Map's
  detail tiles and near band can.
- **The grass as the client colours it** (part 2, 14 November 2026; World Map's WM-0022): `TerrainModel.GrassSeasonAt(double x,
  double z, float[] rainCurve)` returns the column's grass season map (`GrassPhenology.Codes` index, -1 without weather)
  and fills `rainCurve` with its wetness curve (`GrassSeason.Curve`, 24 half-month steps), the two things
  `EarthGrassSeason` gives grass on the client besides vanilla's tints: World Map's far view drew grass from vanilla's yearly
  climate tint alone and came out olive to yellow where the game draws it green (the season map carries about nine tenths
  of the colour in temperate lands). The map is chosen as the client chooses it: the weather's monthly shape at the
  column around its yearly mean at the ground. No time of year: the reader applies its own (the season map's column, the
  curve's value). For maps only; nothing in generation or on the client changes.

## Consequences

- World Map 0.5.3-rc.4 and later sample `GetSurfaceColumn` when it exists (its WM-D54), else `GetColumn`. Its caches are
  keyed by Earth's version, so a world's chart and tiles are made again with the craters after this build.
- Other carving steps can join `Carved` later: the known hot springs' pools (vanilla's generator digs about 16 blocks
  across), the mountain springs' one-block dips, river water settled between columns. Not done.
- Earth's interface for other mods gains one member; the names World Map, Hydrology and Cylinder World read stay as they
  were.

## Checked

- Offline: EarthMapTests' volcano plans: `GetSurfaceColumn` is `GetColumn` without a layer; with one it gives Etna's
  crater floor at the vent, Nyiragongo's lava top over its lake, and `GetColumn` beyond the crater.
- Server self-test (`tools/run_selftest.sh --style earth-10 --places "37.751,14.994;-1.52,29.25"`, 14 November 2026):
  every carved column around Etna and Nyiragongo against the generated chunk's rain height. Etna: 1,262 carved columns,
  all exactly at the rain height; Nyiragongo: 10,953, all exactly at it (PASS).
