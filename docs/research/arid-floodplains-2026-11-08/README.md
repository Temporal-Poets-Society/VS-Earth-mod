# Arid floodplains (8 November 2026)

Decision [0048](../../decisions/0048-soil-on-semi-arid-land.md), soil rules 3. Branch `claude/arid-floodplains` (from
`claude/biomes-1.0` at `4a8a832`, merged with its tip `663205a`, the Penman-Monteith aridity of aridity rules 1). Offline only: not in a test build, not seen in game.

## Sources looked at

| Source | Result |
|---|---|
| GLWD v2 wetland grid (0.1°, already shipped) | too coarse: square edges |
| Ground cover map alluvium (GUM) | covers desert wadis and old deltas; the Suez spot is 58 % alluvium |
| SoilGrids Fluvisols | modern, irrigation shaped |
| GFPLAIN250m (Nardi et al. 2019, CC BY 4.0) | "deserts excluded by design": Baghdad 0 %, the Indus at Hyderabad 0 %, Khorezm 0 %, the Suez spot 99 % |
| MERIT Hydro HAND | drainage from 0.5 km² (every wadi); registration; CC BY-NC 4.0 or ODbL |
| **Earth's own rivers on GLO-90** | chosen by the author: no download |

## The bake (`tools/FloodplainBake`)

The terrain model is built as a new 1:10 GLO-90 world builds it (`earth-1.0.3-tall.22` with its data packs, the GLO-90
store with its dams, lake and river outlines). Seeds: every river course of 10 m³/s or more with at least 10 m³/s from
moist catchments (`AridHydrology.WetSourceFlow`), where a piece runs through land with an aridity index below 0.3
on Holdridge's evapotranspiration or on the Penman-Monteith one of aridity rules 1, whichever is lower, so one grid
serves worlds with and without those rules (13,455 reaches; Holdridge alone: 8,900). The mod zip has to hold
`earthdata/aridity.bin` (tall.22 does not: the bake used a scratch copy with the repository's file added), and the tool
sets `AridityRules` 1 because `EarthParams.BuildModel` switches the factor by it. The flood stands 1.2·Q^0.3 m above the water (1.5 to 15 m: Nile 12.6, Euphrates 8.7, 100 m³/s 4.8),
falls 0.15 m per km of the way out and covers connected land at or below it on a 7.5″ grid (ground: the lowest GLO-90
point within about 6″, under town and palm heights). Seas and lakes stop it. It never stands above the top of the river
nearest in a straight line (no flood running down a valley above the river beside it) and covers no ground more than
one flood height below that river's water (the Salton basin beside the Colorado). Output: 15″ levels, `floodplains.bin`,
2.71 MB, 37,411 blocks, levels on 1.08 million km², 6.7 minutes. Report: [bake-report.txt](bake-report.txt).

With both indices seeding, the transects below are unchanged in a world on either index (every arid sample in them is
arid on both); in a world with aridity rules 1 the Volga's floodplain at Astrakhan (46.35 N, 47.4-48.7 E) keeps topsoil
on 85 of its 90 arid samples, where the Holdridge-only grid had no level (the Volga there is not dry by Holdridge's
index: 0.32). The Ebro at Zaragoza and the Niger at Niamey are not arid on either index (Penman 0.28, 0.22).

In the game (`EarthGeology.GroundAt`, `GroundColumns.At`) an arid ground column keeps its topsoil where its own height
is at or below the bilinear level, unless its cover is wind-blown sand (dunes, sand sheets).

## Tuning

Transects (samples of arid land with topsoil / arid land; the game's rule, the checker in the session's scratchpad):

| Transect | fall 0.2 + depth floor | fall 0.15 + depth floor (shipped) |
|---|---|---|
| Nile delta 30.85 N | 123/196 | 161/196 |
| Nile delta 31.10 N | 99/225 | 203/225 |
| Nile delta 31.30 N | 84/167 | 158/167 |
| Luxor 25.70 N (0.5 km steps) | 22/100 | 22/100 |
| Asyut 27.18 N | 28/119 | 28/119 |
| Suez spot 30.96 N | 0/34 | 0/34 |
| Mesopotamia 32.5 N | 199/240 | 202/240 |
| South Iraq 31.2 N | 193/221 | 194/221 |
| Indus 26 N | 57/170 | 64/170 |
| Amu Darya 41.5 N | 122/200 | 138/200 |
| Karakum 39.2 N, 60.5-63.5 E | 61/151 | 80/151 |
| Negev, Western Desert, Gobi, Sahara, Rub' al Khali, Atacama, Namib | 0 | 0 |

Before (soil rules 2) only the river belt, about a kilometre at 1:10, kept topsoil: 98-99 % of these valley and delta samples were bare.

- Fall 0.2 left the Damietta third of the delta bare: Earth's rivers have only the Rosetta branch. Falls of 0.3 and
  0.5 shrank the delta further (45/196, 23/196 at 30.85 N) and hardly helped the Karakum (44, 35 of 151, dunes counted).
- Without the depth floor, fall 0.15 flooded part of the Suez transect (16/34), and fall 0.2 the whole Salton basin.
- The lowest-point ground window (25, 9 or 1 points) changed little (Karakum at fall 0.2: 61, 48 and 48 of 151, dunes
  counted).
- Cover exclusions from GUM (rock units, salt flats) cut rectangles into the flood and would bare half of southern Iraq
  (Solonchaks); only wind-blown sand is excluded, where the ground columns already draw GUM's edge as sand.

## Known limits

- **The Karakum beside the Amu Darya.** West of the river at 39.2 N the ground falls from 191 m at the bank to 174 m
  75 km away, about level with the river further downstream. 80 of 151 samples from 60.5 to 63.5 E get topsoil (salt
  flats and alluvium within about 60 km of the river); 140 km out (62.0 E) is bare. A height-above-drainage rule (land
  that drains to the river) would separate it but needs a world-wide flow routing.
- **Khorezm and the Aral deltas** flood widely (the Amu and Syr Darya deltas, about 100,000 km² of levels in 40-50 N,
  60-70 E); mostly alluvium, the medieval Khorezm oasis.
- **Lake Manzala's eastern shore.** From 31.0 N, 5 km north of the Suez spot, the delta's flood reaches the flats by
  the lagoon (ground 0 m, flood 2-4 m).
- **Town heights.** GLO-90 is a surface model: a column under a town stands above the flood and shows silt.
- **Damietta branch.** Not in Earth's rivers; the delta's eastern half is flooded across from the Rosetta branch.
