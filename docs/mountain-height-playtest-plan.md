# Mountain height and vegetation playtest plan

Branch: `claude/mountain-height-integration` (from Codex's `codex/mountain-height-playtest`) | Experimental build: 0.17.0-pre.3 | Date: 2026-10-26, integrated 2026-10-27

**Since 27 October (pre.3):** the branch sits on the installed playtest line (0.16.14-rivers.2: real seam, pool layouts
9-10, climate rules 2). It replaces only the Earth package; Cylinder World 0.11.15-playtest.3, Hydrology 0.1.11, Sea,
Wmap and AstraTerra stay as installed. Existing worlds keep their saved layout and rules, but back up the Earth package
you replace and use **fresh worlds** for every case. Codex's pre.1/pre.2 test worlds can't be continued with pre.3
(their climate rules number changed). [Review findings](mountain-height-claude-review.md).

The purpose is to choose mountain proportions and vegetation by comparing the same places in fresh worlds. Claude
should review the branch and merge only after the user finds the playtest good. The branch keeps current defaults;
select test settings explicitly in the Earth creation tab. Existing worlds keep their saved terrain settings.
The automated phase is complete: [morning report](mountain-height-morning-report.md). Use fresh worlds in a separate
test setup; this main-based branch does not include the newer installed river/seam playtest changes.

## Quick start

Build `dotnet build -c Release`, or use the branch's `release/earthmap-0.17.0-pre.3.zip`. Replace the Earth package
and keep Cylinder World. Create a fresh world, choose height 512 in the game's normal
height control and size 512000 on the Earth tab. Start with case C: budget 128, gentler curve 2000 m, full detail,
current vegetation and current rock exposure. Repeat with the other curves, then change the ecological controls.

For automated disposable server runs, use `tools/run-mountain-playtest.ps1 -Case K -Height 512 -CylinderZip PATH`.
`-Smoke` omits rivers/story relocation for a fast integration check; it is not the full gameplay setup. `-Play`
keeps the local server running for a client to join at the printed address. Stop that server normally after testing.
`-Resume -RunName NAME` opens only the named test save and retains its server configuration. No existing player save
or installed mod is changed by the runner. The runner uses vanilla trees; repeat the chosen profile with the user's
Floral Zones/Wildcraft and other installed providers before accepting its vegetation.

## Comparisons

Use world width 512000, the same seed (386217), the same mod set, season, weather, view direction and render distance.
Start with height 512, then repeat the preferred candidates at 384. Keep height 256 as a compatibility/control case.
Leave canals off. Never regenerate the player's existing world to compare profiles.

| Case | Underground budget | Curve | Detail | Vegetation | Rock exposure | Purpose |
| --- | --- | --- | --- | --- | --- | --- |
| A | Current | Current | Full | Current | Current | Baseline |
| B | 128 | Current | Full | Current | Current | Sea budget alone |
| C | 128 | 2000 m | Full | Current | Current | Gentler curve |
| D | 128 | 6000 m | Full | Current | Current | Near linear candidate |
| E | 128 | Linear | Full | Current | Current | Linear reference |
| F | 128 | Adaptive | Full | Current | Current | Height-specific curve |
| G | 110 | Preferred | Full | Current | Current | Vanilla-256 coastal depth |
| H | 128 | Preferred | Half, then off | Current | Current | Does detail obscure the silhouette? |
| I | 128 | Preferred | Preferred | Local | Current | Actual-surface climate and treeline |
| J | 128 | Preferred | Preferred | Current | Physical | Soil stripping independent of exaggeration |
| K | Preferred | Preferred | Preferred | Local | Physical | Combined candidate |

In the automated matrix, G uses curve 6000 m; H-K use curve 6000 m and half detail (H was also tested off).
These concrete baselines implement the plan's "Preferred" placeholders for comparison; no preference was chosen.

Cases B-K use climate rules 3 (real rainfall; A has the default rules 2); compare B to A with that difference in mind.
For curve-only comparisons, use B as the shared rules-3 control. Do not assign all changes in grass or colour to the
height curve.

## Locations and screenshots

Use `/earth tp LAT LON` and `/earth trees` to inspect the local result. Coordinates below are comparison targets, not
proof of a peak's exact position in the averaged elevation grid.

| Area | Latitude and longitude | What to compare |
| --- | --- | --- |
| Fuji | 35.36, 138.73 | Main cone, wooded foothills, alpine transition; also return to the supplied view at 34.8904, 138.5124 |
| Everest | 27.99, 86.93 | Summit/valley hierarchy, snow, amount of descent before trees return |
| Mont Blanc | 45.83, 6.86 | Recognizable alpine relief and forest transition |
| Kilimanjaro, Africa | -3.067, 37.355 | Tropical mountain against lowland and alpine vegetation |
| Denali, North America | 63.069, -151.006 | Cold high relief and long lower slopes |
| Aconcagua, South America | -32.653, -70.011 | Dry alpine rock and summit prominence |
| Kosciuszko, Australia | -36.456, 148.264 | Lower alpine summit and herbfield transition |
| Puncak Jaya, Oceania | -4.078, 137.158 | Equatorial alpine conditions |
| Vinson, Antarctica | -78.525, -85.617 | Ice, rock exposure and absence of trees |
| Elbrus, Europe | 43.355, 42.439 | Additional high European massif |
| St Helena | -15.97, -5.71 | Interior versus coast: bare rock, soil, shrubs and grass separately |
| Mediterranean control | 36.40, 25.40 | Santorini as a repeatable initial island target; add the user's exact reported island later |
| Tropical highland | 9.03, 38.74 | Addis Ababa: avoid falsely freezing warm highlands |
| Lowlands | 56.95, 24.10 | Riga area: low hills, sea shallows, soil, forest and navigation |

Photograph each site from a fixed geographic camera position and direction, plus one summit-to-valley transect. Use
enough render distance or Wmap's far terrain to include the intended peak. Keep a bare terrain view and a normal
vegetated view where possible. Score silhouette, foothill competition, walkability, rock/soil balance and vegetation
transition separately on a 1-5 scale; record a short reason. Absolute Y is not the visual height above local ground.

## Automated verification

1. Build and run the focused vertical-profile tests: monotonicity/inverse mapping, defaults, height-specific values,
   coastal/seabed room, physical slope independence, and climate decoding across 256/384/512/768.
2. Run the existing terrain, tree and terrain-expansion regressions. Compare any failure to the unchanged baseline.
3. Generate disposable server worlds for A, C and K at 384 and 512. Verify sea level agrees in the world API,
   TerraGenConfig, Climate, EarthParams and Cylinder World's saved parameters. Reopen K, change creation settings in
   its config, and verify its frozen settings stay unchanged. Check old-world reload and old/new world transitions.
4. Inspect server renders for the locations above; check rivers/lakes/coasts, seam and polar ice, story placement,
   caves and starter/deep ores. Server renders support the test but do not replace a client playtest.

## Overnight continental matrix

The user requested overnight work and mountains on every continent on 26 October. The shared fixture is
`tests/fixtures/mountain-sites.json`: fourteen targets, including all seven continents and the original island and
lowland controls. Coordinates are approximate test targets; inspect the nearby source maximum before assuming the
target column is a surveyed summit. Polar massif names can identify a broader area than a peak; compare the
[Antarctic gazetteer](https://data.aad.gov.au/aadc/gaz/display_name.cfm?gaz_id=133282) and
[USGS Vinson survey](https://pubs.usgs.gov/publication/ofr20071047SRP069). Kosciuszko's alpine terrain is described by
[NSW National Parks](https://www.nationalparks.nsw.gov.au/things-to-do/lookouts/kosciuszko-lookout).

`tools/run-mountain-matrix.ps1 -CylinderZip PATH` runs A-K at both heights sequentially with every fixture target,
full rivers and separate disposable saves. Each row records the tested assembly hash, errors, site summaries,
elapsed time and save bytes. It writes progress and results under `testruns/`. Use `-Cases A,B -Repeats 3` for the
initial budget/performance comparison with alternating case order. Cold-start totals alone do not isolate generation
or reload cost; use per-site times and a separate reopen experiment. For a fast dry run, `-Smoke` omits rivers/story
relocation and must remain labelled as such.

## Performance experiment

For each height, compare A versus B first (same curve/detail/ecology). Then compare preferred profiles. Use identical
seed, locations, requested chunk radius, mods, view distance and server GC settings. Run at least three repeats,
alternating order. Separate fresh generation time, reload time, allocated/resident memory, saved bytes and long ticks.
Use no connected player for every sample, or the same connected-player conditions for every sample: vanilla's forced
GC while unloading chunks without a player is a known confounder; see [the GC investigation](research/server-gc-stalls-2026-10-23.md).

Record generated columns and solid/water/air volume or empty subchunks. Sea level 128 does not make every column
mostly air: high mountains still contain much rock. Terrain filling can become cheaper while startup or reload stays
unchanged. Report measured results without extrapolating a percentage from block counts alone.

## Review and merge conditions

The user accepts the selected profile visually at both 384 and 512. Claude reviews sea-level startup/reload ordering,
saved-field compatibility, climate helper patches and client colouring, local tree rules, soil stripping, river
gradients and geological/underground gameplay. New defaults and whether to retain the test controls are a separate
decision. Merge to main only after the playtest and Claude review; this branch does not merge itself.

Initial results: [continental findings](research/mountain-height-2026-10-26/continental-findings.md),
[offline visual comparisons](research/mountain-height-2026-10-26/continental-matrix/comparisons.html),
and [source terrain and activation checks](research/mountain-height-2026-10-26/source-and-activation-findings.md).
Completed follow-ups separately measure fresh generation and reload, test detail off, and copy vegetation providers
and installed packages into disposable worlds. See the [morning report](mountain-height-morning-report.md) for the
qualified installed-package result, source limits and remaining human checks.

## Claude integration checks

27 October, `claude/mountain-height-integration` 0.17.0-pre.3 (merge `3fa4a08`), Release build 0 warnings,
`release/earthmap-0.17.0-pre.3.zip` (90,257,690 bytes, SHA-256 `a59d2e636693558248170b65dee398268d58dc48bc0d70653518bb20fdf6e006`).
Dedicated servers only, with the installed Cylinder World 0.11.15-playtest.3; worlds under `testruns/` (ignored).

| Check | Result |
| --- | --- |
| VerticalProfileTests, with real data and new rules-3 rain checks | 142,141 assertions, 0 failures |
| EarthMapTests 384 | Only the known `Lake Geneva: no steeper steps than before (6)` fails, as on the playtest line |
| K 512 smoke (128 / 6000 m / half / local / physical) | Sea 128 agrees in Earth, Cylinder, API, TerraGenConfig, Climate; rules 3 helpers and rain PASS; real lapse PASS |
| C 384 smoke (128 / 2000 m) | Sea 128 agrees; rules 3 PASS |
| A 512 smoke (defaults) | Sea 220, rules 2, real lapse PASS: defaults unchanged |
| Upgrade: world created with installed rivers.2, reopened with pre.3 | Sea 165, rules 2, profile legacy kept; all PASS |
| K 512 full (rivers, story relocation, eight places) | All PASS, no errors; 1,134,897 river segments; model agreement 99.3-100 % at all mountain/island sites |
| A 512 full with installed rivers.2 (baseline) | Same pattern: 0 % model agreement only at the jumpline and the polar wall in both builds (Cylinder World's band and walls) |

Installed in the game on 27 October in place of 0.16.14-rivers.2 (backup
`VintagestoryData/Backups/earthmap-before-0.17.0-pre.3-20261027-154850`), with the user's approval.

Not checked: the client (creation tab, seasonal tint shader, silhouettes), Hydrology/Sea/Wmap/AstraTerra in the same
run, the refusal path that saves the lowered sea level (code review only), multiplayer.
