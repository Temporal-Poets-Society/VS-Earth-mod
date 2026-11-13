# 0026: Cheaper structure guards: the underground water check only after vanilla's own checks

Status: Implemented 0.16.7; matched server worlds identical, reviewed (Claude, Codex), not played | Date: 2026-10-23 | Decided by: the user (2026-10-23)

## Context

The user's 455 s play profile (Devkit) showed `WorldGenStructure.TryGenerate` as the largest worldgen cost: 44.8 s of
self time on the worldgen threads, ≈ 17 ms per chunk column in play. On a test server Earth's structure pass cost
3.6–4.3 ms per column against vanilla's 0.68 ms
([server GC stalls report](../research/server-gc-stalls-2026-10-23.md), proposal 9).

The user's words (2026-10-23, relayed by the Devkit session): "Yes, ask the Earth investigation session to implement
proposal 9: cheaper structure guards with the same rules, measured before and after with Devkit."

**What the cost really is.** A timing probe (test-only Harmony timers, seed 386217, 289 columns, Earth 0.16.6) split
`TryGenerate`'s 5.66 s:

| Part | Calls | Inclusive time |
|---|---|---|
| `UndergroundStructureGuard` (Earth) | 142 949 | **4 575 ms** (≈ 81 %) |
| `TryGenerateUnderground` (vanilla, guard included) | 142 949 | 4 884 ms |
| `TryGenerateAtSurface` + `RuinAtSurface` (vanilla, guard included) | 589 | 460 ms |
| `SurfaceStructureGuard` (Earth) | 589 | 95 ms |
| `TryGenerateInShallowWater` (vanilla, guard included) | 359 | 200 ms |
| `RiverStructureGuard` (Earth) | 359 | 34 ms |
| `StructureLatitudeGuard` (Earth) | 144 964 | 20 ms |

So the surface guard the proposal named is ≈ 2 %. The underground guard is the cost: ≈ 32 µs per attempt, nearly every
attempt being an underground one. Its "cheap away from water" path (`ReachesWater`: a river-distance query and nine
`GetColumn` samples) is not cheap at this call rate.

## Decision

The underground guard keeps its rule exactly. It runs at the last check vanilla makes before placing an underground
structure: a prefix on `WorldGenStructure.WouldOverlapAt`.
- `TryGenerateUnderground`'s prefix only notes the attempt (structure, x, y, z) in thread-static fields.
- `WouldOverlapAt`'s prefix runs the guard and answers "overlaps" when it blocks.
- A finalizer on the generator clears the note.

Both underground placing paths, direct and cave-attached, end with `WouldOverlapAt`.

**Why the outcome cannot change:**
- The checks in between are read-only: the host block, check positions, distance, overlap. Vanilla's
  `LastPlacedSchematic*` fields are written early but only read after a placement.
- Each attempt reseeds the structure's own random generator from its position (`TryGenerate` → `InitPositionSeed`).
- This generator reads every schematic only after unpacking it in the same attempt. Vanilla's lazy `Unpack` rotates
  cached schematics in place, but no read here depends on what earlier attempts unpacked.

**The surface, ruin and shallow-water guards stay up front, unchanged.** Their generators call
`FindClearEntranceRotation`. It reads rotation 1's `EntranceRotation` and size before that attempt unpacks it. Letting
more attempts reach vanilla's unpacking could therefore change a later structure's rotation. The Codex review found
this; an earlier step of this change deferred those guards too. They were only ≈ 2 % of the cost.

**Fallback:** if `WouldOverlapAt` is missing, the underground guard runs up front as before.

## Alternatives

- **A coarser footprint sample before the full scan.** It needs the full scan anyway before accepting, and it does not
  skip attempts vanilla rejects.
- **Repeating vanilla's level-corners test in Earth's prefix.** It depends on vanilla's random schematic pick; fragile.

## Implementation

0.16.7, `src/EarthWorldGen.cs`:
- `UndergroundBlocked`;
- `DeferUndergroundStructureGuard`, `DeferredUndergroundGuard`, `ClearDeferredUndergroundGuard`.

The old prefix `UndergroundStructureGuard` remains as the fallback. Offline suites don't compile this file; the check is matched server worlds.

## History

- 2026-10-23: agreed by the user.
- Step 1 (surface and shallow-water guards deferred), tested with matched worlds against 0.16.6:
  - identical structure lists in 5 of 5 pairs (seed 386217 three times at radius 8, seeds 1234567 and 9876543 at
    radius 12; 4 492 structures);
  - surface scans 589 → 12, about 130 ms saved per 289 columns: no measurable change per column.
- Step 2 (the underground guard deferred the same way), tested with matched worlds against 0.16.6:
  - identical structure lists (seed 386217 at radius 8; seeds 1234567 and 9876543 at radius 12; 2 748 structures);
  - `TryGenerate` 5.66 s → 0.78 s;
  - Earth's structure pass 8.0 → 0.7 ms per column;
  - 289 columns generate in 16.7–17.2 s instead of 20.6 s.
  - [Test report](../research/structure-guards-test-2026-10-23.md).
- Review (Claude and Codex): the deferred surface and shallow-water guards could change later entrance rotations
  (`FindClearEntranceRotation` reads rotation 1 before unpacking it). They went back up front.
- The final build (underground guard deferred only) was retested against 0.16.6:
  - identical structure lists (seeds 386217, 1234567, 9876543);
  - `TryGenerate` 5.66 s → 1.09 s;
  - structure pass 8.5 → 0.75 ms per column;
  - 289 columns in 17.4 s instead of 22.4 s.
