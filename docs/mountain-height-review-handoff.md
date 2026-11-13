# Mountain height branch review and completed overnight handoff

Branch: `codex/mountain-height-playtest` | Base: main `59c17fc` | Version: 0.17.0-pre.2 | Date: 2026-10-26

The user authorized implementation and overnight testing, including notable mountains on every continent. Keep the
branch separate. The user must like the playtest and Claude must review the code before a merge to main. Nothing has
been installed in the player's mod folder and no player save was opened. The original main checkout's unfinished
README and unrelated untracked work were preserved.

## Reviewable output

- [Morning report](mountain-height-morning-report.md): completed results and the recommended first comparisons.

- [Playtest plan](mountain-height-playtest-plan.md): A-K comparison matrix, UI settings, locations and acceptance.
- [Decision 0031](decisions/0031-mountain-height-playtest.md): experimental scope and saved-world compatibility.
- [Initial issue](open-issues-mountain-height-and-vegetation.md) and preserved screenshot.
- [Initial integration evidence](research/mountain-height-2026-10-26/initial-server-checks.md).
- [Real-data profile samples](research/mountain-height-2026-10-26/vertical-profiles.csv), including all continental targets.
- Frozen pre.1 package: `release/earthmap-0.17.0-pre.1.zip` (70,492,617 bytes), SHA-256 `773026cd0dffcc9b7e0f04d76a386f18782a1c4a291c2f993d71e119eaf3d8d3`.
- Frozen pre.1 DLL SHA-256: `00754fc478da9fa8fd851538b0a002879c9e06099eed0b07abd8e46839720cfc`.

- Current playtest package: `release/earthmap-0.17.0-pre.2.zip` (70,492,903 bytes), SHA-256 `601dc4dcc3228f6547b7b8de851ea571ebc9e49a15512d9cff1cf09f51ae8a88`.
- Current Release DLL SHA-256: `2ba62fec81b1226e856070c34ae7b118f4c8df0fd57dd3fc3f4c5a824c4059bf`.
- [Continental findings](research/mountain-height-2026-10-26/continental-findings.md) and [offline visual comparison](research/mountain-height-2026-10-26/continental-matrix/comparisons.html).
- [Source terrain and activation guard findings](research/mountain-height-2026-10-26/source-and-activation-findings.md).

The Release build completed with zero warnings/errors. Focused vertical-profile tests: 141,861 assertions pass.
Existing regional-tree quick suite: 169 checks pass. Terrain-expansion suite: 2,730,759 checks pass. The full existing
EarthMapTests at height 384 finishes with its documented pre-existing failure, `Lake Geneva: no steeper steps than
before (6)`; no other check fails. The baseline curve path is bit-identical at sampled negative and positive
elevations across heights 256/384/512/768. The old default map and ecology paths remain selected in legacy worlds.

## Overnight work completed

The original continental matrix completed **22/22 worlds**, fourteen targets per world, 308 regions and 2,838,528
analyzed columns. Its frozen pre.1 results and original overhead maps remain in
`docs/research/mountain-height-2026-10-26/continental-matrix/`.

Pre.2 follow-ups completed: detail off **2/2 worlds**, Floral Zones/Wildcraft/Herbarium **4/4 worlds**, and clean fresh
performance **12/12 worlds**. The separate frozen pre.1 existing-save reload cohort completed **12/12 reloads**.
[Timing report](research/mountain-height-2026-10-26/clean-performance/performance.md),
[provider comparisons](research/mountain-height-2026-10-26/provider-matrix/comparisons.html), and
[detail-off maps](research/mountain-height-2026-10-26/detail-off/comparisons.html) preserve these results.

The full copied installed-package set generated all 28 regions at 384/512, with sea agreement and climate checks
passing, but both worlds retain strict failure status because Hydrology 0.1.11 logs one missing-language-folder
error. A separate original-Earth 0.16.14-rivers.2 control reproduces the same error with no other errors. See the
[qualified compatibility result](research/mountain-height-2026-10-26/installed-mod-matrix/compatibility.md).
Do not describe these two worlds as error-free or as a client compatibility pass.

Clean fresh timings ran after the debugging and provider workers finished. The earlier overlapping development
series is retained but excluded from the reported timing result. All workers finished by 21:46 UTC on 26 October;
no further automated batches are needed. Raw saves, logs and progress remain under ignored `testruns/`; the committed
summaries, package hashes and offline image reports are the portable evidence. No player files were modified.

## Remaining human playtest and Claude review

1. Use a fresh world and separate mod setup. Compare C and F at 512; at 384 they resolve to the same 2000 m curve.
   Choose geometry first, then compare detail, local ecology and rock independently. In the automated matrix,
   G uses curve 6000 m and H-K use curve 6000 m/half detail as their shared experimental baseline. The plan's
   "Preferred" placeholders were not a user preference or a selection of K.
2. Judge first-person silhouettes, summit-to-valley transitions, actual movement, seasonal colour, river/coast
   gradients, underground clearance and ores. The creation UI and seasonal shader have not been exercised in a
   client. Overhead server maps do not establish that the user's visual issue is solved.
3. Review bootstrap/reload ordering, climate helper patch consumers and cleanup, saved fields, local tree selection
   and rock slope. Tags 74-77 do not collide with the checked `playtest/2026-10-26` and
   `claude/river-rapids-issues-3-4` EarthParams sources (both stop at 73); that check alone is not integration approval.
4. Integrate the separate newer river/seam playtest work only after review. This package is based on main 0.16.13;
   it must not replace the installed 0.16.14-rivers.2 setup or open its saves as an assumed upgrade.
5. Merge only after the user accepts the visuals and Claude approves the code. No production default is chosen.
   Keep the built package and report stable while awaiting that feedback.

## Change after the first matrix

Pre.2 fixes a reproduced early-sea-level leak when Cylinder World is disabled: Earth refused activation but pre.1
had already lowered vanilla's sea level. Configuration preflight now checks Enabled, PolarWalls and PolarClimate
before choosing a new-world datum. The disabled-dependency control stays at 220 after the fix; a valid 384-high
combined control still activates at 128. Release builds with zero warnings/errors. The focused 141,861 assertions
still pass. This guard does not replace later geometry validation.

The source diagnostic now reconstructs small islands rather than mistaking their intentionally flattened patched
grid for actual island terrain. Its bounded nearby-maximum survey and raw-versus-model heights are recorded in
`research/mountain-height-2026-10-26/source-peaks.csv`. No global terrain or climate dataset was changed.

## Findings and limits that must survive the handoff

- A 512-high world at sea 128 has 384 blocks above sea, but the existing 32-block summit reserve leaves 352 for the
  nominal Everest reference. At 384, those figures are 256 and 232. Fixed 110 is the vanilla-256 coastal-depth control.
- Current height 512 still allocates 16 vertical chunk slots per column versus 12 at height 384, as seen in server
  startup. The clean A/B timing and separate reload cohorts show no general speedup; B is slightly slower and save-size
  differences are below one percent.
- The sample at Fuji is about 2840 m in the baked grid and the Everest target about 7309 m. This is an averaged-data
  limitation at those points, not a surveyed-height error caused solely by the height curve. Inspect nearby maxima.
- St Helena's sample has rainfall suitability about 12.79/255. Changing slope-based stripping changes much exposed
  rock to sand in the test, not to forest. Confirm climate-data representativeness before trying to force greenery.
- The test island coordinate for Santorini can fall in water in the coarse source grid. Inspect the surrounding
  island area and add the user's exact reported Mediterranean location when available.
- Server checks confirm sea-level agreement between Earth, Cylinder, the world API and climate helpers. Reload after
  changing creation controls retained the saved sea level, curve and ecology flags. Gameplay below sea level is bounded
  to a real-elevation floor of -500 m for climate, as in the existing model.
- The local ecology path uses actual planting elevation, an all-tier thermal treeline, local factors and reference
  height bands; dedicated-server Floral Zones/Wildcraft checks pass, while their client appearance remains untested. The physical rock option is deliberately
  separate and uses real rise/run. Both are experiments, not chosen production defaults.
- Client UI, seasonal shader compilation/appearance and first-person silhouettes have not been playtested. Review
  `EarthAltitudeClimate` especially: global helper patches, thread-safe scale publication, cleanup between worlds,
  decode limits and the seasonal shader replacement. Review any consumer that reads climate bytes directly.
- This branch is based on main, not the newer combined river/seam playtest branches. The separate seam and pool-layout
  fixes shown in the suite register are not silently included. The copied installed-package server check and original-build control are complete; combining the newer source
  work and validating the client and player configuration remain part of Claude's integration review.
