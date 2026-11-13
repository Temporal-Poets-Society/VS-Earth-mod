# Mountain playtest: morning of 27 October 2026

The experimental branch and **Earth 0.17.0-pre.2** package are ready for the user's visual playtest and Claude's code
review. Automated comparisons cover 384 and 512 heights and mountains on every continent. No profile has been chosen
as a production default. Main, installed mods and player saves were preserved; nothing was merged or installed.

Branch: `codex/mountain-height-playtest`, based on main `59c17fc`. Production code is frozen at `852584b`; later commits
record diagnostics, reproducible tools and evidence. All overnight workers completed on 26 October, by 21:46 UTC.
The experiment is stable; further work depends on client playtesting and review.

## Start here

- [Built playtest package](../release/earthmap-0.17.0-pre.2.zip), 70,492,903 bytes.
- SHA-256: `601dc4dcc3228f6547b7b8de851ea571ebc9e49a15512d9cff1cf09f51ae8a88`.
- [Offline mountain comparison](research/mountain-height-2026-10-26/continental-matrix/comparisons.html): all 308
  original surface tiles, with height, location and case controls.
- [Creation settings and teleport targets](mountain-height-playtest-plan.md).
- [Claude review handoff](mountain-height-review-handoff.md).

Use a **separate test mod setup and fresh worlds**. This branch starts from main 0.16.13 and does not include the
newer river/seam changes in the installed 0.16.14-rivers.2 build. Do not replace that setup or open its worlds with
this experimental package before integration review.

Begin at width 512000, seed 386217, height 512, coastal budget 128, curve 2000 m, full detail, current vegetation and
current rock exposure: case C. Compare adaptive F at 512, then repeat at 384. At 384, F is the same 2000 m curve as C;
at 512 it uses 4000 m. Keep the current-default A world as a reference. After choosing geometry, vary detail,
vegetation and rock independently. The combined K profile is an experiment, not the recommended winner.

## What the experiment establishes

Lowering the sea-level datum works in the tested fresh worlds. World, Earth, Cylinder World and climate helpers
agree, and reloading preserves the saved settings. Moving the datum of an existing world is a different migration
problem: generated terrain, water, structures and player positions would all need handling. This branch does not
attempt that migration.

| Total height | Sea level | Space above sea | Existing summit reserve | Nominal reference relief |
| --- | ---: | ---: | ---: | ---: |
| 384 | 128 | 256 | 24 | 232 |
| 512 | 128 | 384 | 32 | 352 |

The 128 budget is the distance from the world bottom to coastal sea level. Mountains still contain more solid rock
under their surfaces. Fixed profiles retain a nominal deepest ocean of 47 blocks; caves, ore access and the remaining
bottom clearance still require gameplay checks. Sea 110 was included as the default-256 coastal-depth comparison.

Changing the curve matters more to mountain-versus-foothill proportions than moving sea level alone. The current
200 m logarithm spends much of its height on low elevations. The 2000 m curve gives more of the available range to
high mountains. In the generated Fuji footprint at height 512, A to C increases local ground relief from 63 to 101
blocks, reduces canopy from 22% to 14%, and increases exposed rock from 10% to 34%. These are measurements over a
96 by 96 block area, including lower slopes; they do not establish the first-person skyline or an exact treeline.

The rock rule is a separate problem. Physical real-world rise/run retains much more soil on some slopes than the
current block-slope rule: K gives Fuji about 59% soil and almost no exposed rock at 512. St Helena becomes largely
sand rather than forest. That option needs tuning or rejection after visual review; it is not an automatic cure for
the user's green-mountain/bare-island contrast.

## Mountains on every continent

These compare A to C at height 512. Percentages cover the whole sampled footprint. The linked visual report also
contains both heights and every tested case.

| Mountain | Continent | Local ground relief, blocks | Main observation |
| --- | --- | ---: | --- |
| Everest | Asia | 18 to 44 | Remains treeless and overwhelmingly snowy. |
| Mont Blanc | Europe | 57 to 106 | Exposed rock rises from 46% to 71%; check steepness in game. |
| Kilimanjaro | Africa | 30 to 64 | Exposed rock rises from 43% to 66%; lower forest transition needs travel. |
| Denali | North America | 37 to 81 | Snow remains dominant. |
| Aconcagua | South America | 26 to 59 | Exposed rock rises from 22% to 40%. |
| Kosciuszko | Australia | 27 to 42 | Canopy rises from 10% to 19%; the curve alone does not reduce every forest. |
| Vinson | Antarctica | 23 to 50 | Snow/ice retained, no trees. |

Fuji, Elbrus, Puncak Jaya, St Helena, Santorini, Addis Ababa and Riga complete the fourteen targets. The user's exact
Mediterranean island remains unspecified; Santorini is the repeatable control. Its target footprint includes about
75% water, so it cannot stand for every island. [Full continental findings](research/mountain-height-2026-10-26/continental-findings.md).

## Source limits and vegetation providers

Fuji reaches only about 2840 m in the model's sampled neighbourhood. A height curve cannot recover its missing
summit shape. Everest's nearby sampled maximum is about 7437 m and Denali's about 5606 m. Higher-resolution source
terrain is a separate improvement. Small islands must be inspected through IslandLayer, which rebuilds them after
their coarse grid blobs are removed.

St Helena needs both a source-height/profile and local-moisture investigation: its reconstructed neighbourhood
reaches about 1125 m, while its target rainfall suitability is only 12.79/255. Published government evidence
distinguishes the dry outer island from the misty central Peaks. Changing the height curve or turning rock into sand
does not resolve that habitat problem. [Source evidence and activation fix](research/mountain-height-2026-10-26/source-and-activation-findings.md).

The copied Floral Zones/Wildcraft/Herbarium provider setup generated all targets successfully at both heights.
Larger crowns change measured canopy: C at 512 gives Fuji about 20% and Kosciuszko 42% canopy with these providers.
The local ecology option still needs a client view and an altitude transect with those providers.
[Provider visual comparison](research/mountain-height-2026-10-26/provider-matrix/comparisons.html).

## Verification completed

| Check | Result |
| --- | --- |
| Release build, pre.2 | Zero warnings/errors. |
| Focused vertical/profile checks | 141,861 assertions pass, including 256/384/512/768 and legacy mapping. |
| Regional-tree quick / terrain-expansion suites | 169 / 2,730,759 checks pass. |
| Existing full EarthMapTests, 384 | One documented pre-existing Lake Geneva failure; no other failing check. |
| A-K continental matrix, pre.1 | 22/22 worlds pass, 308 regions, 2,838,528 analyzed columns. |
| Detail off, pre.2 | 2/2 worlds pass, 28 regions. |
| Vegetation providers, C/K, pre.2 | 4/4 worlds pass, 56 regions. |
| Clean fresh timing, pre.2 | 12/12 worlds pass, 168 regions. |
| Separate existing-save reload timing, pre.1 | 12/12 reloads pass. |
| Full copied installed package set, K, pre.2 | 28 regions generated; strict results 0/2 because of the external error below. |

The full installed package set logs `Failed to find language folder: ...hydrology-0.1.11.zip...assets\game\lang`
once per run. The same frozen packages with the original installed Earth 0.16.14-rivers.2 reproduce that error in a
separate 384 control. There are no other recorded errors in these three runs. Strict failures remain recorded;
this is qualified dedicated-server compatibility, not a complete client or installed-configuration validation.
[Compatibility evidence](research/mountain-height-2026-10-26/installed-mod-matrix/compatibility.md).

Pre.2 fixes the one introduced activation bug found overnight: when Cylinder World was disabled, pre.1 changed
vanilla sea level before Earth refused activation. The refusal control now keeps 220 at height 512; a valid
384 control still uses 128. Later geometry validation remains separate from the early configuration preflight.

## Loading hypothesis: no measured speedup

Three matched repeats per profile and height distinguish fresh generation from existing-save reload. Fresh totals
include process setup, loading, generation, analysis and shutdown; reloads are a separate pre.1 cohort. Filesystem
caches were not flushed, and A/B also changes climate encoding. These are not client loading-screen measurements.

| Height | Fresh A / B median seconds | Reload A / B median seconds |
| --- | ---: | ---: |
| 384 | 77.01 / 78.83 | 37.41 / 38.02 |
| 512 | 82.70 / 84.52 | 37.18 / 38.09 |

Budget 128 is slightly slower in these samples. Save-size differences are below 1%; observed working sets vary too
much to establish a memory benefit. A 512-high column still has sixteen vertical chunk slots versus twelve at 384.
Choose the extra height for terrain geometry; a general loading optimization has not been demonstrated.
[Timings, target-region measurements and methodology](research/mountain-height-2026-10-26/clean-performance/performance.md).

## Before any merge

The user needs to judge silhouettes from fixed camera views, summit-to-valley travel, 384 versus 512, vegetation,
seasonal colour, rivers/coasts, caves and ores. Dedicated-server surface tiles do not replace that client playtest;
the new seasonal shader adjustment and creation UI have not been exercised in a client. Claude should review the
saved schema, bootstrap ordering, global climate helpers, direct climate-byte consumers, local ecology and rock
rules, then integrate the separate newer river/seam work. Merge waits for both visual acceptance and code review.
