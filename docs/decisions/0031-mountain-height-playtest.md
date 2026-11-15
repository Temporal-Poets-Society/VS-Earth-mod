# 0031: Mountain height and alpine vegetation playtest

Status: Agreed (the user, 26-27 October); the experiment became the defaults in 0.17.0-pre.4 to pre.8 (sea 110, curve presets, climate rules 3, real-altitude ecology), in Earth 1.0.0; in Earth 2.0.0. Default sizes and heights since replaced by 0053's scales | Date: 2026-10-26, integration 2026-10-27 | Decided by: the user

## Context

The [reported Fuji and Everest issue](../open-issues-mountain-height-and-vegetation.md) combines compressed high
mountains, enlarged foothills, lush high slopes and bare lower islands, including St Helena. The user authorized a
branch and tests of the alternatives, with Claude to review and merge only after a good playtest. They regard 384
as the minimum desirable height and want roughly the same coastal underground depth regardless of total height.

## Decision

Use `codex/mountain-height-playtest` for independent new-world comparisons. Preserve the default layout and all saved
worlds. Expose creation-only test settings for sea level, land curve, procedural detail, local alpine vegetation and
physical slope for rock exposure. These deliberately override decision 0023's exclusion of artistic settings from
the tab for this experiment; they are not a decision to keep every test control in the final product.

At height 512 and sea 128, 384 blocks lie above sea level; the existing 32-block peak reserve leaves 352 for the nominal
Everest reference. At height 384, the same sea level leaves 256 above sea and a 24-block reserve leaves 232 for relief.
Sea 110 provides the default 256-world coastal depth as an alternative. Fixed-budget test profiles keep the nominal
deepest water at 47 blocks rather than enlarging ocean depth with total height.

The comparison curves are the current logarithm (200 m), gentler logarithms (2000 and 6000 m), linear, and an
experimental height-dependent curve: 1000/2000/4000/6000 m at heights <=256/384/512/greater. These values are candidates,
not settled defaults. The adaptive candidate asks whether larger worlds can spend less of their budget on lowlands.

Any opt-in experiment uses climate rules 3 (numbered 2 on Codex's branch, renumbered at integration because the
installed playtest line already gives every new world decision 0009's rules 2): the climate map stores the sea-level
temperature, which vanilla's patched helpers cool by the real lapse in metres (decision 0009's `ClimateLapse`), and the
real rainfall, which the patched `GetRainFall` leaves unadjusted. This avoids filling the climate byte with large
block-height compensation and avoids increasing humidity purely because a mountain has more blocks (vanilla's altitude
rain compensation also clips at 0 on dry high ground and reads back wet). Default new worlds keep rules 2, older worlds
their own. Local ecology and physical rock remain separate toggles.

## Alternatives

Lowering sea level alone enlarges both hills and mountains without changing their ratio. Increasing total world
height alone preserves the same distorted proportions. A linear curve preserves height ratios but may make lowland
relief and river gradients too small. Keeping a fixed curve for all heights is included in the experiment rather
than assumed inferior to the adaptive candidate.

## Consequences

New parameters are saved and synchronized; old saves do not adopt them. There is no in-place sea-level upgrade.
Custom sea level is applied at WorldReady before Cylinder World captures its parameters, after vanilla's
GenTerra.AssetsFinalize establishes its usual level. Client climate helpers use the synchronized vertical scale.
Geology, ore depth, cave clearance and navigation must be playtested before promoting a profile.

More air might reduce solid-block filling, compressed save size and some downstream work. A 512-high column still
contains more vertical chunk slots than a 384-high one. Startup data preparation and river indexing do not disappear.
Compare fresh generation and reload separately; do not treat "mostly air" as a proven loading-time optimization.

## Implementation

Experimental source version: 0.17.0-pre.3 (pre.2 on Codex's branch). `VerticalTerrainOptions`, `VerticalScale`, `EarthVerticalBootstrap`,
`ClimateLapse` (rules 3 rain), `SeasonTintAltitude`, `EarthParams`, terrain climate encoding, local tree selection, rock exposure and creation UI.
The pre.1 continental matrix passed all 22 worlds. Pre.2 adds configuration preflight for the early sea-level change;
its disabled-dependency control retains vanilla sea level and its valid-profile control still passes. Repeated
performance, reload, detail-off and installed-provider checks are complete. No loading speedup is demonstrated.
The copied installed-package runs reproduce an existing Hydrology packaging error; the original-build control
confirms it predates this branch. [Morning report](../mountain-height-morning-report.md). [Verification and review handoff](../mountain-height-review-handoff.md).
Detailed comparisons, commands and review conditions: [playtest plan](../mountain-height-playtest-plan.md).

## Review and integration (27 October)

Claude reviewed `codex/mountain-height-playtest` (`e51a2ea`) and merged it onto `playtest/2026-10-26` (`8a38b78`, the
installed 0.16.14-rivers.2 line) as `claude/mountain-height-integration`, 0.17.0-pre.3. Findings and changes:

- Both lines had a "climate rules 2" with different encodings and separate Harmony patches of the same five vanilla
  helpers. The integration keeps decision 0009's `ClimateLapse` (patched at mod start, per-side claims, client cut,
  CHELSA temperatures, `ClimatePixelForVanillaLapse` for Wmap) and drops Codex's `EarthAltitudeClimate`, whose
  `Dispose` unpatched the shared helpers while the other side of a single-player game could still be generating.
  The test profiles get rules 3 (above). The rain patch is in `ClimateLapse`; the seasonal tint shader line is in
  `SeasonTintAltitude` (client, rules 3 only, never unpatched; reloads the shaders when the loaded line doesn't match).
- A lowered sea level was only re-applied from a saved, active EarthParams. When Earth refused a new world after the
  early change (Cylinder World turning itself off on a narrow map), the vanilla world was made at 128 and reopened at
  vanilla's level. `EarthVerticalBootstrap` now also saves the lowered level on its own (`earthmap-sealevel`).
- Codex's edit had re-encoded three non-ASCII comment characters in `EarthParams.cs`; restored.
- `Climate.Sealevel` is now set on the client from the world's EarthParams: vanilla only sets it in the server's
  `GenTerra`, so a multiplayer client read rain with sea level 110.

Saved-world compatibility: the new fields (74-77) are absent in older saves, which keep their vertical layout, rules
and vanilla shader line; `EarthParams.CurrentVersion` is unchanged. Not changed by the review: the curve values, the
local tree line, physical slope (too much soil at 512, see the morning report) and the test controls themselves.

## User direction after the first client look (27 October)

In world 3 (F, 512, sea 128) Everest's summit stands at about y 445: the averaged ETOPO grid has 7,437 m there, so no curve
alone reaches the top. The user's direction: default sea level 110, default width 512000 and height 512; Everest's rock
up to about 510 (players build to 511); a linear 110-510 baseline (22.1 m per block) with a knee that lifts low ground,
keeping player settings for curve and sea level but setting defaults; mountains need visible base-to-top height and must
not be overgrown; climate and vegetation controlled by real altitude. Reported in the user's world (Andris' Hermit
Tales): Fuji and Kilimanjaro grassed and wooded to the top, hyenas on Fuji, Jostedalsbreen as lush 2-3 block soil
grassland instead of rock and glacier, St Helena sand and cacti. Earth limits vanilla land animals only south of 60°S, so
vanilla hyenas spawn wherever vanilla's climate allows.

Proposed plan (not agreed in detail): (1) every ecological rule keyed on the real elevation a block stands for (inverse
curve) and CHELSA temperature: altitude zones forest / shrub / alpine meadow / scree and rock / snow and ice by the
warmest month there, soil thinning with altitude, vanilla land animals within regional ranges and zones; (2) mountain
glaciers from the Randolph Glacier Inventory; (3) CHELSA precipitation for orographic and island rain (St Helena's Peaks);
(4) a control-point knee curve over the 110-510 linear baseline, defaults chosen from side-by-side worlds; (5) summit
restoration from finer ETOPO 2022 (15 or 30 arc-seconds) in high relief and named peak heights; (6) new defaults (sea 110,
Everest at height - 2, width 512000; height 512 only recommended in the mod description, user 27 October).
Curves (user, 27 October): several selectable presets rather than one knee; the default is the one the user likes most.
Earth's 60°S rule for vanilla land animals is deliberate (no goats or polar bears in Antarctica); ranges and altitude zones
for vanilla animals elsewhere are a separate addition. (2), (3) and (5) need data downloads.

## Defaults and presets, 0.17.0-pre.4 (27 October)

User's choice after comparing curves: default sea level 110 at every world height, Everest's 8849 m at the second-highest
block (510 at 512; the terrain is cut there anyway), a logarithmic curve with a 1000 m parameter as default; presets
linear, log 250, log 500, log 2000 and legacy (the pre-0.17 layout). Default Earth size 512000; world height 512 is a
recommendation in the description. A knee curve may be added later. The test presets gentle, balanced and adaptive are
gone from the creation tab (worlds made with them keep their saved scale). Every non-legacy world gets climate rules 3.
The user's five comparison worlds ("Earth curve 1-5") were made with pre.4 by dedicated servers.

## Real-altitude ecology, 0.17.0-pre.5 (27 October)

The user's direction "climate and vegetation maybe we need to control by real altitude": new worlds default to the
"Mountain ecology: by real altitude" setting (`earthAlpineEcology` local; legacy keeps the pre-0.17 rules). Everything
uses the warmest month at the real elevation a block stands for (the inverse of the height curve, CHELSA temperatures), so
the curve choice doesn't move tree lines or snow:

- Trees: Codex's local tree line (planting column's own elevation; trees from a warmest month of 8 °C, all by 13 °C).
- Surface zones (`EarthWorldGen.AlpineColumn`): nival below the snow line (0.5 °C, rising to 3 °C where it is wet):
  snow on gentle ground, rock on steeper; fell-field below 3 °C: scree (the rock's gravel) with a few soil pockets, rock
  on slopes; alpine meadow below 6 °C: one block of soil, rock from moderate slopes; tree-line ecotone below 10 °C: two
  blocks of soil, rock on steep slopes; lower: vanilla's soil, rock on cliffs.
- Vanilla land animals: goats anywhere; sheep, hares, foxes, wolves, bears and butterflies up to the snow line; the
  others (hyena, gazelle, pig, deer, moose, raccoon, chicken) only below the tree line (6 °C). Their geographic ranges
  (no hyenas in Japan) need PHYLACINE rasters for vanilla taxa: a download, not done. *Since 2026-11-07 (ranges
  proposal, step 2) these limits are data: each entry of `fauna-vanilla.json` carries "minWarmMonth" (6 or 0.5 °C,
  none for goats), checked by the same prefix as the ranges; values unchanged.*

Measured with pre.5 (dedicated server, seed 386217, log 1000 m, 224 x 224-block self-test areas; top blocks, legacy ->
real altitude): Everest snow 47 -> 79 %, rock 35 -> 13 %; Mont Blanc rock 35 -> 48 %, soil 44 -> 32 %; Kilimanjaro
soil 57 -> 42 %, rock 27 -> 39 %; Jostedalsbreen soil 51 -> 36 %, rock 33 -> 50 % (heath and shrubs remain:
`research/mountain-height-2026-10-26/jostedalsbreen-legacy-vs-real-altitude-pre5.png`). Fuji's area barely changes
(leaves 27 -> 29 %): its highest model column is y 331 = 2,534 m, below the real tree line, because the averaged grid
loses about 1,240 m of the cone; summit restoration is what fixes Fuji. St Helena unchanged (rain data).

Then open (approved and done in pre.6, below): mountain glaciers (Randolph Glacier Inventory;
Jostedalsbreen sits at about 1,500 m in the averaged grid with a 7.8 °C warmest month, so the zones give heath and rock
there, not ice), CHELSA precipitation (St Helena), summit restoration (finer ETOPO), vanilla animal ranges (PHYLACINE).

## Data: summits, glaciers, rain, animal ranges, 0.17.0-pre.6 to pre.8 (27 October)

The user approved the four downloads ("Go ahead and download all four"). Raw files in the shared cache
`C:/VS mods/Earth/rawdata` (gitignored): ETOPO 2022 30" surface (1.6 GB), CHELSA V2.1 bio12/bio18 (1.1 GB), RGI 6.2
from OGGM's public mirror (0.4 GB; RGI 7 at NSIDC needs a login, not used), PHYLACINE v1.2.1 rasters (small). New
worlds record each as a version in EarthParams (fields 78-81) only when the file is in the mod; older worlds keep
theirs. All non-legacy new worlds get them.

- `summits.bin` v2 (tools/bake_summits.py, 40 MB): the excess of ETOPO's 30" dry ground over a bilinear surface of its
  own 2' means, u8 in 8 m steps, tiles reaching 500 m. `TerrainModel` adds it to rugged dry land (not to valleys, water,
  shores). Reconstructed maxima: Everest 8,358 m (2' grid 7,308; real 8,849), Fuji 3,666 (2,644; 3,776), Mont Blanc 4,502
  (3,689; 4,808), Galdhøpiggen 2,282, Ben Nevis 1,168. At 30" summits are still 100-500 m below the real heights.
- `glaciers.bin` (tools/bake_glaciers.py, 1.2 MB): RGI 6.2 outlines as 30" coverage, 0.6 % from RGI's area. Columns at
  least half covered get 2-5 blocks of glacier ice.
- `rain.bin` (tools/bake_rain.py, 11.9 MB): the climate grid's rainfall formula from CHELSA bio12/bio18 and monthly
  temperatures per 30" pixel on the 2' grid; St Helena's Peaks 91 instead of 13. CHELSA overstates rain on high tropical
  peaks (Kilimanjaro's summit reads very wet; it mainly raises the snow line there).
- `fauna-vanilla.json` (tools/build-vanilla-fauna.py): vanilla land animal types mapped to the species they depict and
  their PHYLACINE present-natural ranges (chickens: a red junglefowl box). No hyenas in Japan; the natural profile keeps
  Ice Age ranges (spotted hyenas in Europe).
- pre.7/pre.8: below the tree line soil is stripped only where the real rise over run is at least 0.6 as well: the
  curve stretches low ground (about 6 m per block near the sea against 98 m across), which bared St Helena's flanks.

Measured with pre.8 (same seed and self-test areas as pre.5): St Helena soil 24 % of the area (island land) and no bare
rock (pre.5: 23 % rock, sand and cacti); Mont Blanc rock 29 %, glacier ice 23 %, soil 31 %; Jostedalsbreen glacier ice
53-67 %; Everest's area tops out at y 506 with 26 % glacier ice; Fuji's cone rises to y 377 (about 3,590 m); Kilimanjaro
a snow cap over a rock ring. Maps: `research/mountain-height-2026-10-26/pre8-sthelena-montblanc-kilimanjaro-fuji.png`.
The package grows from 90 to 143 MB.

## History

- 2026-10-26: user authorized the experimental branch, height curves and visual comparisons. No default profile chosen.
- 2026-10-26 overnight: initial continental matrix 22/22 passes; original renders and site data preserved. Pre.2 fixes
  the reproduced custom sea-level leak when Cylinder World is disabled. Visual acceptance and Claude review pending.

- 2026-10-26, completed for the morning of 27 October: clean timing/reload, detail-off, provider and installed-package
  controls finished. Preserve the frozen pre.2 artifact; human client playtest and Claude integration review remain.
- 2026-10-27: Claude review; merged onto the installed playtest line as `claude/mountain-height-integration`,
  0.17.0-pre.3 (climate rules 3, saved sea level, client sea level, comment encoding). Visual playtest pending.
- 2026-10-27: first client look (worlds 1-4); user direction above (sea 110, Everest ~510, 512000, real-altitude
  ecology); next plan proposed.
- 2026-10-27: pre.4: defaults sea 110, Everest at height - 2, log 1000 m, Earth size 512000; presets linear, log 250,
  500, 2000; comparison worlds created.
- 2026-10-27: pre.5: real-altitude ecology by default (zones, local tree line, vanilla animal altitude limits).
- 2026-10-27: downloads approved; pre.6-pre.8: summits, glaciers, CHELSA rain, vanilla animal ranges, real slope below
  the tree line. pre.8 installed in place of pre.5.

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: Agreed experimental branch; reviewed and integrated onto the installed playtest line, visual playtest pending
