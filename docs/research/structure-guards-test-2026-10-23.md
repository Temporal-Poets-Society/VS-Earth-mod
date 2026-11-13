# Cheaper structure guards: matched-world test (2026-10-23)

This checks Earth 0.16.7's deferred underground structure guard
([decision 0026](../decisions/0026-cheaper-structure-guards.md)) against 0.16.6 on dedicated servers. Proposal 9 of the
[server GC stalls report](server-gc-stalls-2026-10-23.md) started it.

Nothing was installed in the game. The worlds are new and disposable: `C:\VS mods\Devkit\testruns\guards-*`.

**Verdict.**
- **Same placement.** Every matched world placed a byte-identical list of structures.
- **About 11× cheaper.** Earth's structure pass costs 0.75 ms per chunk column instead of 8.5 ms.
- **Faster generation.** 289 columns generate in 17.4 s instead of 22.4 s.

## Method

- **Software:** Vintage Story 1.22.7 dedicated server, Devkit 0.1.1, Cylinder World 0.11.14, Hydrology 0.1.3.
- **Builds:**
  - 0.16.6: HEAD `71b7c9f`, Debug build.
  - 0.16.7: built from a clean `git archive` of HEAD plus only this change. Another session's unfinished river work was
    in the same working tree at the time, so the build was isolated from it.
- **Worlds:** 256000 × 384 × 102400; `/chunk unload false`, then `/devkit gen r`.
- **Runner:** a scratch copy of `Devkit/tests/server/run.py` that adds `SEED`, and console commands before and after
  generation.
- **Test-only mod `strucdump`:**
  - `/strucdump <file>` writes every `GeneratedStructure` (code, group, box) in the loaded map regions, sorted.
  - `/guardinfo` times Earth's guard functions and vanilla's generators with Harmony (inclusive, all threads), and
    prints Earth's deferral counter and the patch owners.
- Other servers (other sessions' tests) were running on the machine at the same time. Timings are therefore compared
  only between interleaved runs.

## Where the time went (`/guardinfo`, seed 386217, 289 columns, 0.16.6)

| Part | Calls | Inclusive time |
|---|---|---|
| `WorldGenStructure.TryGenerate` | 144 964 | 5 658 ms |
| `TryGenerateUnderground` (guard included) | 142 949 | 4 884 ms |
| Earth's underground guard | 142 949 | **4 575 ms** (81 %) |
| `TryGenerateAtSurface` + `RuinAtSurface` (guard included) | 589 | 460 ms |
| Earth's surface guard | 589 | 95 ms |
| `TryGenerateInShallowWater` (guard included) | 359 | 200 ms |
| Earth's river guard | 359 | 34 ms |
| Earth's latitude guard | 144 964 | 20 ms |

Almost every structure attempt is an underground one (buried chests, vugs, cave lakes). The underground guard's
"cheap away from water" path costs about 32 µs: a river-distance query and nine terrain samples. The surface guard
that proposal 9 named was only about 2 %.

In 0.16.7 vanilla's own checks (host block, check positions, distance) turn away all but about 0.2 % of underground
attempts before Earth's guard runs.

| Part (0.16.7) | Calls | Inclusive time |
|---|---|---|
| `TryGenerate` | 144 964 | 1 093 ms |
| `TryGenerateUnderground` | 142 949 | 292 ms |
| Earth's underground guard | 303 | 12.6 ms |
| Surface and river guards (unchanged) | 589 / 359 | 82 / 29 ms |
| Generation of 289 columns | | 17.7 s (23.7 s in 0.16.6) |

## Same placement

Each list is all structures in the loaded regions: the generated area plus the regions loaded at start-up.

| World | Seed, radius | Structures | 0.16.6 vs 0.16.7 |
|---|---|---|---|
| q | 386217, 8 | 872 | identical |
| e1 | 1234567, 12 | 919 | identical |
| e2 | 9876543, 12 | 957 | identical |

Two runs of 0.16.6 with the same seed were also identical, so generation is deterministic and a difference would have
shown. The deferred guard still turns structures away.

An earlier step also deferred the surface and shallow-water guards. It placed identical structures in eight matched
worlds, but the Codex review found a case those worlds did not hit: `FindClearEntranceRotation` reads rotation 1 of a
schematic before unpacking it. Letting more surface attempts reach vanilla's unpacking could change a later entrance
rotation. Those guards went back up front; they were about 2 % of the cost.

## Devkit per chunk column (interleaved, without the test mod)

| Run | `GenStructures` incl. `TryGenerate` | `TryGenerate` per call | `GenTerra` | Generation |
|---|---|---|---|---|
| 0.16.6 | 8.50 ms | 35.1 µs | 3.30 ms | 22.4 s |
| 0.16.7 | 0.75 ms | 6.3 µs | 3.16 ms | 17.4 s |

`GenTerra`, which this change does not touch, stayed level. In the user's play profile the structure pass was ≈ 17 ms
per column (44.8 s in 455 s) under heavier load. The same ratio would bring it to ≈ 1.5 ms.

## Reviews

- **Claude:** every underground placing path ends with `WouldOverlapAt`. The early `LastPlacedSchematic*` writes are
  only read after a placement. The structure random is reseeded per attempt, and each schematic is unpacked before it
  is read.
- **Codex:** confirmed the placing paths, the captured coordinates, the parameter names, the finalizer and thread-local
  state, and no conflict with Cylinder World's guard on `TryGenerate`. It found the surface rotation case above (fixed).
- **Open (Codex, unverified):** a nested generator call before the outer overlap check would overwrite the noted
  attempt. Vanilla has no such call.

## Not covered

- The in-game self-test was not run; its guard report line now also prints the deferral counter.
- Villages and the village building guard are unchanged.
- The offline suites compile only `src/Core`, which this change does not touch.
