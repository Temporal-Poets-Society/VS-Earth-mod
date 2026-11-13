# River index start-up speed (2 November 2026)

Branch `claude/river-index-speed` from `release/1.0` (`34c6c54`, Earth 1.0.2). Committed on the branch; not merged, packaged or installed.

## Why

The AstraTerra load-time review of the user's 2 November session found Astra under a second, and Earth's model build the
largest single mod cost of server start: `Earth Map: 1136764 river segments ... indexed in 7955 ms` (the time covers
`EarthParams.BuildModel`, not only the river index), on every start of the 409,600-ring world.

## Where the time went

An offline harness built the same model from `earthdata/` with that world's layout (W 512,000, ring 409,600 from
51,200, walls at z 48 / 204,752, height 512, sea level 110) and reproduced the same 1,136,764 segments in about 7.1 s:

| Stage | Before | After |
|---|---|---|
| `RiverNetwork` constructor | 4.8–4.9 s | 2.2–2.6 s |
| of it: `RiverStraighten.Apply` | 2.8 s | 0.8 s |
| of it: pool layout rounds | 1.0 s | 0.4 s |
| `SeaChannels.Build` | 0.64 s | 0.30 s |
| `AridHydrology.WetSourceFlow` | 0.38–0.44 s | 0.15–0.20 s |
| `NavigableStraits`, `RiverEnds.Prepare`, `WadiFans`, the rest | unchanged | unchanged |
| **Model build, 3 runs each** | **7.2–7.9 s** | **4.3–4.8 s** |

The totals are the same with the harness held to 8 logical processors (4 cores of the Ryzen 9 7950X3D): 7.1–7.9 s
before, 4.3–4.6 s after.

## What changed

All four changes give the same result as before; only the work is split differently.

- `RiverStraighten.Apply`: the chains (a river between confluences) run in parallel. Each reach belongs to one chain, and
  a chain reads and replaces only its own reaches' lines. Each worker reuses its buffers from chain to chain (1.9 GB of
  temporary lists and arrays became 0.45 GB). The loop cut-off's neighbour search uses one sorted key array per chain
  instead of a list per 8-block cell; it keeps the farthest qualifying point, whatever the order it finds them in.
- `RiverNetwork` pool rounds: the river systems (reaches joined through `Next`) run in parallel. Each keeps its reaches
  in the old order, and all take the same number of rounds. Inflows, the reach downstream and the valley ahead (`Ahead`)
  never leave a basin. The diagnostics counters `GorgeSections`/`GorgeSteps` are added atomically.
- `SeaChannels.Build`: the river mouths' searches for deep water run in parallel, at most 4 workers (each `SearchBuffers`
  is about 30 MB for a ship channel). The channels are then made in the rivers' order.
- `AridHydrology.WetSourceFlow`: the climate cells are sampled in parallel. The flow routing, whose sums depend on order,
  stays serial.

Tried and left out: the five strait courses in parallel (one course dominates: 0.64 → 0.56–0.62 s).
`RiverEnds.Prepare` interleaves its searches with edits to shared river levels and was not touched.

## Verification

- A deep fingerprint (every reachable field, floats bitwise, dictionaries by key) of the river network, straits, sea
  channels, wet-source flow, dry fans, river ends and deltas: identical to `release/1.0` in every run (network
  `FF7DB455FA4C97FB`).
- `tests/RiverIndexTests`: PASS (1,563,683 Nearest, 372,456 distance and 21,924 candidate-order checks).
- `tests/EarthMapTests` 384: 283 ok and 2 FAIL on both this branch and a clean `release/1.0` checkout. The same check
  lines appear in both, and only the timings differ. The two failures already fail on `release/1.0`: the known
  `Lake Geneva: no steeper steps than before (6)` and `icy, seed 2026: -66.07,101.25 at -10.1 °C`.
- `EarthMap.csproj` Release: 0 warnings, 0 errors.
- Not run: a dedicated server or the game. The in-game log line should drop from about 8 s to about 5 s on this world.

Coordination: the calm-rivers session (`claude/calm-rivers`) edits the constants near `StepLead`, the constructor's
signature and the rapids loop after the pool rounds. It leaves the pool rounds to this branch; whichever merges second
rebases.
