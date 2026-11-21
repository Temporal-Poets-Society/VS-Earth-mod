# Vanilla strata rules 1: census of 18 November 2026 (EA-0181, decision 0099)

Which rocks lie within reach (the top 120 blocks of land columns) with vanilla rocks and vanilla ores, before and after
vanilla's rock layers are laid under the local ground ([decision 0099](../../decisions/0099-vanilla-rock-layers-under-the-local-ground.md)).
"Before" is the deposit depth rules' census ([deposit-depths-2026-11-18](../deposit-depths-2026-11-18/), runs `rock-10`
and `rock-100`: decision 0097 alone); "after" is the same harness with both rules (`census.csv` here, runs `strata-10` and
`strata-100`).

## Method

As in the deposit depths census: dedicated servers one after another, the play styles `earth-10` (1:10, 1024 high) and
`earth` (1:100, 384 high), seed 12345, spawn Vienna, vanilla rocks and ores, the branch build relabelled
`2.1.4-ea0181.1`, the ore audit mod with the rock census patch, 5 × 5 chunk columns at 14 places. `compare_strata.py`
writes `compare.txt`. The logs say "Earth vanilla rock layers (rules 1): ... thickness x 0.25" at 1:10 and "x 0.667" at
1:100, and the self-test "vanilla rock layers: PASS; rules 1, active". No errors.

## Results

Rock types with at least 3 % of the rock within 120 blocks of the ground, before → after (full lists in `compare.txt`):

| Place | 1:10 | 1:100 |
|---|---|---|
| Vienna | sandstone → andesite, sandstone, granite, bauxite | peridotite, slate → + granite |
| Riga | shale → andesite, granite, shale, phyllite | granite, phyllite → same |
| Pannonian plain | chalk, peridotite, andesite → peridotite, granite, chalk | 4 → 4 |
| Zermatt | granite → granite, andesite, peridotite | 3 → 2 |
| Addis Ababa | granite, sandstone, peridotite, shale, chalk → sandstone, peridotite, andesite, granite | granite, andesite → andesite, granite |
| Mexico City | granite, bauxite → granite, bauxite | peridotite → peridotite, granite, limestone |
| Lhasa | granite, slate → granite, chert, slate | granite → granite, conglomerate, phyllite |
| Tehran | granite → granite, peridotite | 3 → andesite, granite, claystone, slate |
| Johannesburg | peridotite, granite → same | granite, bauxite → granite, shale, sandstone, bauxite |
| Atacama | claystone, sandstone, chalk, conglomerate → granite, claystone, peridotite, slate | granite, peridotite → granite, peridotite, andesite |
| La Paz, Dead Sea, Nairobi (1:100) | granite → granite | granite → granite |

- **More layers within reach** at most places: a tall world's layers are the standard world's thickness (one layer no
  longer fills the reach: Vienna, Riga at 1:10), and plateaus keep sediments and get the bottom-up layers about a sea
  level's depth down (Lhasa's chert and conglomerate, Mexico City's limestone, Johannesburg's shale and sandstone).
- **Ores that need those hosts appear on plateaus**: coal at Lhasa (242 a chunk column at 1:10), Mexico City and Brasília
  (1:100), sulfur at Johannesburg (61) and Mexico City (32), salt domes at Lhasa (2,644 at 1:10, 840 at 1:100), iron at
  Addis Ababa (57, 1:100) and Brasília (19 → 53, 1:10).
- **Lowland sediment ores are vanilla's standard amount**: with standard thicknesses, coal on the Pannonian plain at 1:10
  falls from 859 to 131 a chunk column, at Riga from 669 to 356.
- **Iron is still patchy, as vanilla's own design makes it.** Each iron ore takes a few rocks and follows its own ore map
  (hotspots about 200 blocks across, none on about half of a map), so a 5 × 5 chunk window can lack it. Some places lost
  iron where their layers moved (Riga at 1:10 105 → 0, Tehran at 1:100 79 → 0) and others gained it. Pure granite places
  (La Paz, the Dead Sea) are granite in vanilla's province there; granite is vanilla's default rock and hosts hematite.
- **Speed**: 3.7 s on average for 25 chunk columns at a place (4-7 s in the earlier runs; other sessions' servers and a
  game client ran meanwhile, so this only shows no large cost).

## Not covered

Ground columns and regional rock (the
rule applies only to vanilla rocks), and the prospecting pick's readings in game (its column is built the same way;
checked only by the shared code).

## Looked at in game (18 November 2026)

On the AgentBridge bench (its own data folder and port), a new 1:10 world with the branch build. Columns read block by
block, depth below the ground: the Grand Canyon's south rim (ground y 326) claystone 0-18, granite 18-120, andesite
124-136, granite below, in a cut 48 × 80 blocks wide and 145 deep whose bands run flat and continuous across a chunk
border; Addis Ababa (ground 361) chert 3-15 with sulfur, slate, peridotite, andesite 45-105, granite from 108; Lhasa, a
valley floor below its mountains, peridotite 3-45 over granite; Vienna slate 6-9 over granite. The Matterhorn's peak is
grey core rock, its lower ledges another rock, with no seams. Shots: the hub's `backlog/media/EA-0181-*.webp`.
