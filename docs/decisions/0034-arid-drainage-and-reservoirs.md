# 0034: Dry desert drainage and artificial-lake classification

Status: Built in 0.17.0-pre.9 (`bae8dc6`); in Earth 1.0.0 and 2.0.0. In new worlds the wet-source flow comes from GIRES (0069, PerennialRules). Not checked in game (EA-0011 closed on the author's general review). Date: 2026-10-30. Issue #25.

## Cause and decision

0005's terminal fading only narrows channels; `TerrainModel.ApplyRiver` still fills every channel with fresh water.
Moreover [HydroRIVERS](https://www.hydrosheds.org/products/hydrorivers) gives estimated long-term average discharge,
not perennial/base flow. The actual Arabian test reach has Q=292.067 m3/s: a low-Q cutoff alone misses the fault.

`AridHydrology` routes the moist-source part of Q through the whole downstream graph. Each reach adds only its
incremental discharge; CHELSA ambient aridity scales that increment from zero at rainfall 100 to full at 140.
Source climate is cached at quarter-degree centres to bound startup work. Local warm hot-desert columns
(0032's rainfall/warm-month rule) become dry where Q or accumulated moist-source flow is below 10 m3/s.
Thus imported water survives across a desert. This is a global perennial-flow **proxy**, not measured low flow;
monthly runoff, groundwater and individual springs are absent from the source data.

Dry channels retain shallow beds, receive sand/gravel in `BareRock`, have no fluid/flow labels and no riparian-rain
boost. `WadiFans` adds bounded sediment fans at dry land terminals, only over ground within four blocks of the
terminal level; sea/lake/ice is protected. Existing end-width fading remains. No permanent pools are invented.
Native riparian trees may use genuinely wet reaches; a dry drainage line no longer supplies that wet-site flag.

## Before/after flow evidence

`tests/out/playtest-issues/review/perennial-and-wadi-columns.csv` uses actual source reaches and production carving
at their centres (individual reaches isolated for the column test; provenance computed on the full source graph).
All 454 source reaches in the 19-29 N, 36-56 E regression window classify dry. Other regions use the same rule.

| Control | Q | Moist-source Q | Before water | After water |
|---|---:|---:|---|---|
| Arabian wadi, 20.4104 N 44.9854 E | 292.067 | 0 | fresh | dry, fan; riparian weight 0 |
| Nile | 2587.842 | 2346.78 | fresh | fresh |
| Euphrates | 850.717 | 230.69 | fresh | fresh |
| Tigris | 1147.157 | 219.81 | fresh | fresh |
| Indus | 5675.108 | 4860.28 | fresh | fresh |
| Colorado | 549.511 | 102.69 | fresh | fresh |
| Orange | 301.178 | 90.72 | fresh | fresh |

Full classification is `review/arabia-flow-support.csv`; raw source/climate values are `review/arid-reaches.csv`.
These controls do not prove perennial status for every small river worldwide. Groundwater-fed desert creeks remain
a known limitation of the available data; no regional exception boxes or named-river exemptions were added.

## Reservoir cause and inventory

Razzaza is NE Q2007652, HydroLAKES 146, GRanD 4474 (type 2 reservoir). Local `lake_types.json` says BOTH
`salt lake` and `artificial lake`. The bake treated salinity as proof of natural origin, vetoed the artificial tag,
then skipped further HydroLAKES evidence because Wikidata had already classified it. The fix in `bake_earth.py`
distinguishes natural-origin evidence from physical lake properties: a salt-lake tag cannot veto an explicit
artificial-lake tag. Genuine glacial/tectonic/etc. basins later regulated by dams retain their protection.
There is no Razzaza coordinate/name condition in that global classification rule.

[NASA](https://science.nasa.gov/earth/earth-observatory/iraq-lakes-bounce-back-147315/) identifies Razzaza/Milh as a
modern diversion reservoir completed in the 1970s. Current baked data is corrected through the versioned
`earthdata/reservoirs-v1.json` overlay, not by overwriting terrain.bin: 146 lake cells become dry; 383 cells including
their three-cell shore ring lose the reservoir's water level. At 32.76,43.62: salt lake -> no water, ground Y110 ->
Y113 because lake-shore holding no longer applies. Existing bed elevations are retained; ancient sediment depths
are not reconstructed. The Python full-world bake was not run in this environment.

The supplied screenshot and ALL three local NE lake shapefiles were audited, plus HydroLAKES point metadata.
`review/iraq-lake-source*.csv`, `review/iraq-hydrolakes-source.csv` and `review/iraq-water-components.csv` preserve
the evidence. Habbaniyah and Tharthar are already tagged Reservoir by NE and absent before/after; do not remove
them twice. Tharthar's modern diversion is also documented by [NASA](https://science.nasa.gov/earth/earth-observatory/iraq-reservoirs-plunge-to-low-levels/).

Every remaining water component in the screenshot window is listed below. IDs are candidate source associations
from bounds/pour points, not newly assigned historical names. HydroLAKES type 1 means natural lake, GRanD 0 means
no recorded GRanD dam; that is source evidence, not independent proof of prehistoric extent.

| Centre N,E | Cells | NE source/evidence | HydroLAKES evidence | Action |
|---|---:|---|---|---|
| 32.7596,43.6215 | 146 | global 1296, Razazah, Alkaline Lake | 146, type 2, GRanD 4474 | remove reservoir |
| 32.9222,45.4056 | 6 | Europe 761, unnamed Lake | 15072, type 1, GRanD 0 | retain |
| 32.7167,45.9000 | 18 | Europe 760, Suwayqiyah, Alkaline Lake | 1415, type 1, GRanD 0 | retain |
| 32.4250,46.7750 | 4 | Europe 757, unnamed Lake | 15114, type 1, GRanD 0 | retain |
| 32.2500,46.5333 | 8 | Europe 755, unnamed Lake | 15126, type 1, GRanD 0 | retain |
| 32.1873,45.5696 | 17 | Europe 748, Dalmaj-area unnamed Lake | 1421, type 1, GRanD 0 | retain |
| 32.0167,44.2500 | 5 | Europe 763, unnamed Lake | 15148, type 1, GRanD 0 | retain |
| 31.9130,47.2944 | 9 | Europe 756/759, unnamed lakes | 15151/15154, type 1, GRanD 0 | retain |
| 31.7000,47.7500 | 6 | Europe 758, unnamed Lake | 15170, type 1, GRanD 0 | retain |
| 31.6028,47.6250 | 12 | Europe 749, unnamed Lake | 15176, type 1, GRanD 0 | retain |
| 30.7760,47.1037 | 113 | global 1306, Hammar, Alkaline Lake | fragmented wetland lakes, type 1 | retain |
| 30.6967,46.6433 | 5 | Europe 762, unnamed Lake | 15273 nearby, type 1, GRanD 0 | retain |
| 30.5907,48.1315 | 9 | Europe 765, unnamed Lake | 15283/15293, type 1, GRanD 0 | retain |

The unnamed basins' historical extents remain unresolved; they are retained rather than deleted on appearance.
The audit establishes why the reservoir filter keeps each, and does not rename them or assert all modern shorelines
are ancient. Reservoir removal outside the audited overlay requires a future full bake of the corrected filter.

## Compatibility

New worlds store AridRiverRules=1 (84), ReservoirRules=1 (86); old saves read 0. The one-shot
`UpgradeExistingWorldWaterTerrain` adopts these and 0035 before model construction. Already generated chunks are
unchanged; seams are possible at an opt-in boundary. The overlay is packaged on client/server; its cache switch is
versioned. No save/data folders outside this worktree were touched. Game integration, complete monthly hydrology
and prehistoric shore reconstruction remain outside the verified result.

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: source implemented; offline checked; game validation pending.
