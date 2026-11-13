# Earth weather open issues (moved to the suite backlog)

Since 12 November 2026 the issues that were listed here are tracked as one file each in the suite backlog,
`C:\VS mods\backlog\items\`, with their current status and the evidence for it. Each was rechecked against
`main` that day. The index of everything open is [backlog/BACKLOG.md](../../backlog/BACKLOG.md); for this mod's
open items run `tools\Update-Backlog.ps1 -Mod earth` from `C:\VS mods`.

| Item | Issue |
| --- | --- |
| [EA-0016](../../backlog/items/EA-0016-climate-grid-leftover-stripes.md) | Climate grid: rain stripes along coasts (227 spans with fix v2, 67 with v3) |
| [EA-0017](../../backlog/items/EA-0017-warm-spot-at-75e-48n.md) | Warm spot at 75° E 48.88° N |
| [EA-0018](../../backlog/items/EA-0018-weather-bin-rain-outliers.md) | 240 land cells of monthly rain stand out |
| [EA-0019](../../backlog/items/EA-0019-hail-is-vanilla-random.md) | Hail is still vanilla's random event |
| [EA-0020](../../backlog/items/EA-0020-wet-days-by-place-off.md) | Rain falls on the wrong number of days in some places |
| [EA-0021](../../backlog/items/EA-0021-rain-ignores-ground-height.md) | Rain rate ignores ground height |
| [EA-0022](../../backlog/items/EA-0022-thunder-low-in-arid-west-and-africa.md) | Too little thunder in the arid West and Africa |
| [EA-0023](../../backlog/items/EA-0023-missing-low-cloud-off-peru-namibia.md) | Coasts of Peru and Namibia too clear |
| [EA-0024](../../backlog/items/EA-0024-lows-have-no-fronts.md) | Lows have no fronts |
| [EA-0025](../../backlog/items/EA-0025-weather-does-not-change-temperature.md) | Weather doesn't change the temperature |
| [EA-0026](../../backlog/items/EA-0026-fog-and-mist-not-tied-to-earth.md) | Fog and mist not tied to Earth's humidity or coasts |
| [EA-0027](../../backlog/items/EA-0027-cloud-edges-ignore-rain-areas.md) | Cloud edges don't follow Earth's rain areas |
| [EA-0028](../../backlog/items/EA-0028-earth-weather-not-played.md) | Earth's rain, clouds and storms never looked at in game |
| [EA-0029](../../backlog/items/EA-0029-weather-determinism-across-machines.md) | Weather determinism across machines unchecked |
| [SE-0030](../../backlog/items/SE-0030-gpu-shader-check-two-failures.md) | Two failures in Sea's GPU shader check not confirmed gone |
| [WM-0006](../../backlog/items/WM-0006-barometer-ignores-earth-pressure.md) | Barometer doesn't read Earth's air pressure |

The text this page held before is in this repository's history: `git show 7145eb2:docs/open-issues-weather.md`.
Add new issues to the backlog, not here.
