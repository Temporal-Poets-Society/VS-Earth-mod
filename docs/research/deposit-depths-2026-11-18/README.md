# Deposit depth rules 1: census of 18 November 2026 (EA-0170, decision 0097)

What vanilla's deposits give within reach under Earth's lowlands and plateaus, in Earth 2.1.4 and with deposit depth
rules 1 ([decision 0097](../../decisions/0097-vanilla-deposits-below-the-local-ground.md)), with vanilla rocks and
vanilla ores (the world screen's defaults).

## Method

- Dedicated servers on this PC, one after another (`run.sh`), each a fresh world of the play style (`earth-10`: 1:10, 1024
  high, linear; `earth`: 1:100, 384 high, log1000), seed 12345, spawn Vienna, sea level 110, the player's GLO-90 store
  linked in. Packages: the released `earth-2.1.4.zip` against the branch build relabelled `2.1.4-ea0170.2`, with
  Cylinder World 1.1.1 and Hydrology 0.6.2.
- The test-only ore audit mod of the 15 October validation
  (`../ore-ingame-validation-2026-10-15/oreaudit`), with the tall-world patch of 3 November and
  `oreaudit-census.patch` (a count of the rock types within 120 blocks of the ground). It generates 5 × 5 chunk columns at
  each of 14 places (`sites.json`) and counts every deposit block by family, depth below the ground and ore-map factor.
- `analyze.py` gives `compare.txt` (per place and ore group: blocks per chunk column, with the mean depth below the
  ground); `census.csv` has every family's count per place and run, and the rock census.
- Other sessions' game client and servers ran at the same time; nothing here is a timing.

## Results

Blocks per chunk column, mean depth below the ground in brackets, 2.1.4 → rules 1 (the full table is `compare.txt`).
Highland places: Addis Ababa, Nairobi, Mexico City, La Paz, Lhasa, Tehran, Johannesburg, Brasília, the Atacama.

| | 1:10, 2.1.4 | 1:10, rules 1 | 1:100, 2.1.4 | 1:100, rules 1 |
|---|---|---|---|---|
| Copper on the plateaus, depth | 79-146 | 49-77 | 132-216 | 72-87 |
| Tin on the plateaus, depth | 87-143 | 37-63 | 81-144 | 41-53 |
| Lead and zinc on the plateaus, depth | 81-118 | 25-68 | 92-168 | 34-68 |
| Iron at highland places | none at 9 of 9 | La Paz 75 (53), Brasília 19 (24) | 6 of 9, 164-253 deep | 5 of 9, 34-91 deep |
| Sea-level ores on the plateaus | none | sulfur, borax, chromite, coal, salt domes 24-171 deep | deep | as at 1:10 |
| Lowland ore (Riga, the Pannonian plain) | iron 490 and 191, coal 2,711 and 2,878 | 105 and 31, 669 and 859 | | |

- **Depth.** Everything that lay 80-250 blocks under a plateau now lies where it lies under a lowland: 25-90 blocks down.
  Sulfur (Addis 12 a column, 26 down), borax (Nairobi 80, 32 down), chromite (Mexico City 27, 84 down), coal (Addis 156,
  27 down) and salt domes (Addis 1,180, Nairobi 2,802) appear on plateaus where 2.1.4 had none.
- **Saltpeter** keeps vanilla's depth: the first build of the rules lifted the part of its band below y 0 too (140-200
  blocks down on plateaus); vanilla skips that part, and so do the rules now (`DepositDepth.Lift`).
- **Amount.** Lowland ore at 1:10 is about a quarter of 2.1.4's (vanilla's world-height factor, 4 at 1024 high, is gone):
  the ore per column of vanilla's standard world. At 1:100 (factor 1.5) about two thirds.
- **Iron at 1:10 is still missing at 7 of 9 highland places, and the cause is the rock, not the depth.** Vanilla's iron
  ores each take a few rocks (limonite basalt, shale or chert; hematite granite, peridotite, limestone or phyllite;
  magnetite andesite, claystone, chalk, conglomerate or slate). Within 120 blocks of the ground the rock census finds one
  or two rock types at most places: Zermatt, La Paz, Tehran and the Dead Sea 100 % granite, Mexico City 70 % granite and
  30 % bauxite, Vienna 100 % sandstone (1:10). Where iron's ore map was on (Mexico City 13 limonite attempts, the Atacama
  8.5) its host rock was not there. Vanilla's rock layers grow with world height (4 times as thick at 1024 high), so
  fewer of them lie within reach in a tall world; that is EA-0181, not this rule.

## Not covered

Deposit counts in a 5 × 5 chunk window follow vanilla's ore maps (hotspots about 200 blocks across, none on about half of
a map), so a single place's zero says little; the depths and the lowland ratios are the robust part. Ground columns and
regional rock were not run (the rules apply there too; their hosts are Earth's). No graphical client.
