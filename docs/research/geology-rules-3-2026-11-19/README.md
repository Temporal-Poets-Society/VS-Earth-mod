# Geology rules 3: census of 19 November 2026 (EA-0173, decision 0101)

Regional rock ("Regional rock regions only") with vanilla ores, geology rules 2 (Earth `main` at `cc3611ce`) against
rules 3 ([decision 0101](../../decisions/0101-geology-rules-3.md)), at 1:10 and 1:100.

## Method

As the deposit depths census ([deposit-depths-2026-11-18](../deposit-depths-2026-11-18/)): dedicated servers one after
another, the play styles `earth-10` and `earth`, seed 12345, spawn Vienna, `earthRocks=regional`, vanilla ores, the ore
audit mod with the rock census patch, 5 × 5 chunk columns at 14 places (`sites-geo.json`). Packages: `main` relabelled
`2.1.4-main.1`, the branch relabelled `2.1.4-geo3.1`. The logs say "Earth geology v2 ... 6 reviewed regional
envelope(s)" and "v3 ... 38". `compare_geo.py` writes `compare.txt`; `census.csv` has every family per place and run.

## Results (rock within 120 blocks of the ground; ore in blocks per chunk column)

| Place | Rules 2 | Rules 3 |
|---|---|---|
| Troodos | sandstone, bauxite, shale | basalt, peridotite (1:100 chromite 21 → 47, copper 8 → 15, olivine 4 → 111) |
| Semail, Oman | conglomerate (1:10), granite (1:100) | peridotite 90 %, basalt (olivine 11 → 611 and 4 → 803) |
| Kempirsai | granite, sandstone, shale | peridotite, basalt (olivine about 600) |
| Bushveld, Great Dyke, Sudbury | granite, sediments, gneiss | granite with gabbro (basalt) and peridotite layers (Sudbury 1:10 chromite 0 → 67) |
| Dartmoor | granite, sandstone, shale | granite and its gneiss basement (1:10 copper 8 → 251) |
| Iceland | peridotite, basalt (1:100 also granite) | peridotite, basalt |
| Deccan, Addis Ababa (flood basalt) | peridotite under basalt | granite under basalt (olivine 330-650 → about 0) |
| West Siberia (cover) | vanilla's random strata at 1:100 (granite, peridotite, andesite) | basin fill: sandstone, shale over granite (coal 331 → 647) |
| Amazonia (cover) | andesite 100 % (1:10), claystone and peridotite (1:100) | granite, shale, sandstone |
| Vienna (cover) | sandstone 100 % (1:10), peridotite 85 % (1:100) | granite, sandstone, shale, slate |
| Riga (Baltic envelope) | unchanged | unchanged |

The envelopes, the continental basement under flood basalts and the end of vanilla's random strata under cover all show;
the Baltic envelope is untouched.

The evaporitic profile's gypsum beds were added after this census (Earth's own `rock-gypsum`); none of the census's places lies
in an evaporite cell, so the tables are unchanged by it.

## Beds in metres: measured and left out

Rules 3 first hung the beds in metres through the world's depth curve (the audit's R1). On the play styles' own layouts the
share of land showing a lower bed at its surface became 5.3 % at 1:100, 19.7 % at 1:25 and 10.9 % at 1:10, against rules 2's
12.0, 13.1 and 18.5 %: more uneven between play styles and far fewer lower beds at the default 1:100. The audit's spread
(11.6 to 22.7 %) came from test layouts with a 200 m log curve, finer near sea level than any play style. Rules 3 keeps rules
2's beds (66 blocks at sea level 110).

## Not covered

How the new rock looks in game; ground columns over the new regional rock (the hand-off is the same code); envelopes
beyond these places; the lead in crystalline rock that vanilla's hosts leave out (EA-0174, needs new ore blocks).
