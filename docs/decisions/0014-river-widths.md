# 0014: River widths — the standard world's widths as a floor in blocks, real widths where wider

Status: Agreed, implemented (0.14.7), offline checks only, not played yet | Date: 2026-10-20 | Decided by: the user ("go on
with the proposed rule"; on narrower rivers in mid-sized worlds: "not a real trade-off, it would be more correct scale")

## Context

Rivers are widened for gameplay: `RiverNetwork.HalfWidth` gives a navigable river (from 100 m³/s) clamp(2·Q^0.3, 5, 48)
× `RiverWidthScale` 1.5 blocks of water, so a 100 m³/s river is 12 blocks and the Amazon 72 (the cap, from about
40 000 m³/s). The exponent 0.3 keeps small rivers from looking like trickles on the standard 256k world (ring 204 800,
~196 m per block), where the user tested and found the sizes good, the Amazon included.

The width was also multiplied by the world's size (`SizeScale` = ring / 204 800), so it stayed the same in kilometres
on every map: a 100 m³/s river 2.3 km wide (233 blocks) and the Amazon 14 km (1406 blocks) at a 4 million block ring,
where a block is 10 m. Rivers never reached their true size, and the Amazon was only 3.4 times the Daugava on any map
(really ~18 times).

## Decision

For new worlds (`EarthParams.RiverWidths` = `RiverNetwork.WidthRulesVersion` 1), a river's water width in blocks is the
larger of:

- **the gameplay floor**: the standard world's width curve, in blocks, on any map at least that big (scaled down as
  before on smaller ones, so worlds up to the 204 800 ring are unchanged), the cap included;
- **its real width**: about 8·√Q metres (hydraulic geometry: Rhine at Lobith ~380 m, Mississippi ~1 km, Amazon ~3.6
  km), divided by the world's metres per block, with no cap.

| water width (blocks) | 100 m³/s | Daugava 650 | 2300 | Mekong branch 7500 | 40 000 | Amazon 200 000 |
|---|---|---|---|---|---|---|
| ring 204 800 (196 m/block) | 12 | 21 | 31 | 44 | 72 | 72 |
| 512 000 (78 m), was | 12 (30) | 21 (52) | 31 (76) | 44 (109) | 72 (180) | 72 (180) |
| 1 000 000 (40 m), was | 12 (58) | 21 (102) | 31 (149) | 44 (213) | 72 (352) | 89 (352) |
| 4 000 000 (10 m), was | 12 (233) | 21 (409) | 38 (598) | 69 (852) | 160 (1406) | 357 (1406) |

The biggest rivers reach their real width first (the Amazon from about an 800 000 ring); a river with more water is
never narrower than one with less. River exit channels' least width for ships (12 blocks) is a floor in blocks the same
way. Banks, valleys, pool lengths and fades keep growing with the world: they follow the terrain's scale, not the water.
The courses are straightened and smoothed with the old (world-sized) widths, so the centre lines are the same with
either rules: smoothing with the narrower water left the river data's 15" staircase (about 46 blocks a step at 4
million) in thin rivers. Depths are unchanged.

Existing worlds keep their widths; they opt in with `UpgradeExistingWorldRiverWidths` (in worlds bigger than the
standard ring rivers then become narrower, and may meet generated chunks with a step; [0002](0002-save-compatibility.md)).

## Alternatives

- *Keep widths growing with the world.* Rivers the same in km on every map, never their true size.
- *Let the floor grow slowly with the world (e.g. with √SizeScale).* Softer change for mid-sized worlds; the user
  rejected it: narrower rivers on bigger maps are the more correct scale.
- *Lift the 72-block cap on the standard world.* The Amazon would be ~117 blocks there; the user found 72 good.

## Implementation

`src/Core/RiverNetwork.cs` (`WidthRulesVersion`, `RealWidthMetres`, `WidthFloorScale`, `MetresPerBlock`, `HalfWidth`),
`src/Core/SeaChannels.cs` (ship channel floor), `src/EarthParams.cs` (ProtoMember 63), `src/EarthMapSystem.cs`,
`src/EarthMapConfig.cs`. Tests: `EarthMapTests` mode `widths` (also in the full suite), renders with `ringrender lat lon
ring w h step [name]` (`WIDTHS=0` for the old rules).
