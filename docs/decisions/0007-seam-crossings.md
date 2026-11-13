# 0007: River seam crossings found by the ring's geometry

Status: Implemented (0.14.1-pre, not committed) | Date: 2026-10-16 | Decided by: Claude, fixing a review finding the user asked to fix; follows [0002](0002-save-compatibility.md)

## Context

A river line that crosses the seam (the Pacific cut where the ring's ends meet) has one piece jumping from one end of
the ring to the other. Rivers, straightening, exit channels and the river audit found such pieces with a fixed test:
"a piece longer than 4000 blocks crosses the seam". The river data's real pieces are up to 1.62° long, so the test also
caught real pieces in large worlds. Measured on rivers.bin:

| Ring (blocks) | Longest real piece | Real pieces over 4000 |
|---|---|---|
| 204 800 | 922 | 0 |
| 512 000 | 2 305 | 0 |
| 1 024 000 | 4 610 | 3 |
| 2 048 000 | 9 221 | 18 |
| 4 000 000 | 18 009 | 111 |

Those pieces were dropped from the river index (a gap in the river), counted as 1 block long in pool planning (its
descent squeezed), and stopped straightening for the whole chain.

## Decision

- A piece crosses the seam when its x span is more than half the Earth's width in the ring
  (`EarthGeometry.AcrossSeam`). No real piece comes close, at any world size.
- Worlds with river end rules ([0005](0005-river-ends.md), `EarthParams.RiverEnds >= 1`) use the geometry test. Older
  worlds keep the 4000-block test, so their new chunks match their old ones.
- Exit channels (SeaChannels) use the geometry test in every world. Their pieces join neighbouring 2′ cells, so the
  result is the same for any ring under about 40 million blocks.
- The river audit asks the network (`RiverNetwork.AcrossSeam`), so it checks what the world really has.

## Alternatives

- *Raise the threshold.* Still wrong for some world size, and it would change older worlds.
- *Split or unwrap pieces at the seam using longitude.* Equivalent here, but it needs the geographic points at every
  call site. The x span is available everywhere.
- *A separate `RiverSeams` version.* River end rules v1 isn't released yet and already changes rivers in new worlds, so
  tying the fix to it costs no extra save field. At 512 000 blocks and below the two tests agree, which covers every
  world the user has.

## Consequences

- Large worlds (rings from about 1 million blocks) made with river ends get those rivers whole.
- Existing large worlds without river ends keep the gaps until they upgrade with `UpgradeExistingWorldRiverEnds`.

## Implementation

`src/Core/EarthGeometry.cs` (`AcrossSeam`), `src/Core/RiverNetwork.cs` (`geometrySeams`), `src/Core/RiverStraighten.cs`,
`src/Core/SeaChannels.cs`, `src/Core/RiverAudit.cs`, `src/EarthParams.cs`. Test: `tests/EarthMapTests`
`CheckSeamPieces` (4 million block ring: every real piece indexed, none across the seam, the old test still drops the
long pieces).

## History

- 2026-10-16: found by the adversarial review; fixed.
