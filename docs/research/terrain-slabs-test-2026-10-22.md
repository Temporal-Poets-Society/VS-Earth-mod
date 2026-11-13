# Terrain Slabs with Earth — server-side compatibility test (2026-10-22)

This run checks whether BeloMaximka's [Terrain Slabs](https://mods.vintagestory.at/terrainslabs) 1.0.17 and its required
[Place On Slabs](https://mods.vintagestory.at/show/mod/38090) 1.2.0 work with Earth 0.15.6 (`3be1209`, Debug build) on
Vintage Story 1.22.7. Nothing in Earth was changed, no save was opened, and nothing was installed. All test worlds are new
and live in the git-ignored `testruns/slabs-20261022` folder (about 660 MB, safe to delete).

**Verdict.** On the server, nothing in Earth breaks.
- Both mods load on 1.22.7, although ModDB lists them only for 1.22.2–1.22.3. All JSON patches applied, there are no new
  errors or warnings, and generation takes the same time (94 s vs 94 s for 14 sites).
- The terrain shape is unchanged: `WorldGenTerrainHeightMap` is identical in all 25 600 columns of every site.
- Earth's falls, lakes, coasts, bare-rock mountains and bog peat all come out as without the mods.
- The one visible change at Earth features is Terrain Slabs' "beach" step: a half-slab of the bed material goes into the
  first water cell along every shore (details below).

The client side was **not** tested:
- how Place On Slabs renders and offsets plants, snow and placed items;
- how water behaves at the shore half-slabs once block updates reach them, for example when a player breaks or places
  blocks there.

## What the mods do

Terrain Slabs appends one handler to the `standard` world type's `PreDone` pass. That pass runs after Earth's own terrain
work (`EarthWorldGen.BareRock` in the `Terrain` pass) and after vegetation. The handler replaces an edge block with its
half-height slab when two things are true:
- the block above it is air, a plant or another block that Place On Slabs can offset;
- one horizontal side is open (not side-solid and without fluid).

It builds the full block → slab map from its own `terrainslabs:<type>-<domain>-…` blocks. Blocks without a slab variant,
such as `earthmap:bogpeat`, stay full. Generated structures are skipped. The default "Column" mode works all the way down
the column, so cave floors get slabs too.

The "beach" step works on a water cell next to such an edge when that cell holds no solid block, or an offsettable one
such as a water plant. It moves that block up by one and puts a slab of the bed block into the water cell.

Neither GitHub repository has a license, so their code can't be copied into Earth. The integration is just "install both
mods". Earth needs no code for it, because Terrain Slabs hooks the same `standard` world type that Earth uses.

## Method

| World | Mods | Purpose |
|---|---|---|
| **slabs** | Earth 0.15.6 + Geology Additions 1.4.8 + Terrain Slabs 1.0.17 + Place On Slabs 1.2.0 | under test |
| **control1** | Earth 0.15.6 + Geology Additions 1.4.8 | baseline |
| **control2** | identical to control1 | run-to-run noise |

All three worlds were 256000 × 256 × 102400 with seed 386217, an otherwise default Earth config, and story-location moves
turned off. Geology Additions is included because Earth only places bog peat when it is present.

The test-only helper mod [slabaudit](terrain-slabs-test-2026-10-22/slabaudit) loaded 5×5 chunk columns around each of the
14 sites in [sites.json](terrain-slabs-test-2026-10-22/sites.json). It dumped every cell's solid id and fluid id, plus the
terrain and rain height maps, together with each world's id → code table.

Three scripts compare the dumps cell by cell:
- [analyze.py](terrain-slabs-test-2026-10-22/analyze.py) → [summary.json](terrain-slabs-test-2026-10-22/summary.json);
- [analyze_details.py](terrain-slabs-test-2026-10-22/analyze_details.py) → [summary-details.json](terrain-slabs-test-2026-10-22/summary-details.json);
- [analyze_plants.py](terrain-slabs-test-2026-10-22/analyze_plants.py).

A cell counts as changed by the slabs only if the two controls agree on it.

To reproduce:
1. Build `slabaudit` with `dotnet build -c Release`.
2. Put the ModDB zips into `terrain-slabs-test-2026-10-22/dl/` (zips are git-ignored):
   - `terrainslabs_1.0.17.zip`, sha256 `b3ab0250…7adc`;
   - `placeonslabs_1.2.0.zip`, sha256 `f65313ad…d889`.
3. Run `mkrun.py <run dir> <slabs|control> <port>` once per world.
4. Run `analyze.py <runs dir>`.

## Results per site

"Shore half-slabs" are slabs that share their cell with water. "Edges left full" counts terrain blocks at the surface
with air above and air beside them that did not become slabs.

| Site | Kind | Slabs | at surface | in caves | shore half-slabs in water | fluid changes (noise) | fall cells control → slabs | bog peat | edges left full |
|---|---|--:|--:|--:|--:|--:|--:|--:|--:|
| rhine falls | falls | 9,834 | 4,357 | 4,787 | 679 | 3 (0) | 47 -> 46 | 13 | 0 of 1,914 |
| niagara falls | falls | 7,976 | 1,814 | 4,953 | 1,216 | 29 (2) | 430 -> 433 | 3 | 36 of 885 |
| victoria falls | falls | 9,909 | 4,150 | 5,003 | 756 | 0 (0) | 269 -> 269 | 0 | 0 of 355 |
| lake bracciano | lake | 14,736 | 6,106 | 8,012 | 629 | 51 (8) | 43 -> 41 | 0 | 21 of 2,881 |
| lake geneva north | lake | 9,133 | 5,432 | 2,804 | 895 | 12 (0) | 6 -> 8 | 0 | 9 of 2,436 |
| lisbon coast | coast | 12,706 | 5,860 | 5,135 | 1,711 | 0 (0) | 3 -> 3 | 0 | 0 of 2,983 |
| dutch coast | coast | 5,372 | 617 | 3,112 | 1,643 | 0 (0) | 0 -> 0 | 17 | 5 of 320 |
| dover cliffs | coast | 8,073 | 2,311 | 4,884 | 878 | 0 (0) | 7 -> 7 | 1 | 5 of 1,140 |
| mont blanc | mountain | 17,020 | 10,193 | 6,610 | 222 | 24 (1) | 31 -> 33 | 0 | 1 of 4,842 |
| aconcagua | mountain | 8,874 | 4,547 | 4,138 | 191 | 2 (3) | 6 -> 8 | 0 | 9 of 2,576 |
| bog of allen | bog | 5,989 | 1,608 | 4,237 | 144 | 0 (0) | 4 -> 4 | 0 | 7 of 815 |
| teici bog | bog | 6,492 | 1,903 | 4,359 | 225 | 0 (3) | 10 -> 9 | 20 | 2 of 810 |
| finland bogs | bog | 7,485 | 2,697 | 4,368 | 421 | 4 (0) | 10 -> 10 | 34 | 2 of 1,289 |
| daugava riga | river | 5,960 | 935 | 3,770 | 1,256 | 4 (0) | 10 -> 10 | 20 | 10 of 391 |

## Findings

- **Terrain shape.** Unchanged. Only the top block of an exposed edge becomes a half slab, so a 1-block step becomes two
  half steps that can be walked without jumping. Heights, slopes and the height map stay as Earth generated them. On a
  cliff of 2 or more blocks, only the top block is halved.
- **Falls.** Intact. Fall cells (`water-d-*`) stay within ±3 per site. At most 30 flowing-water cells per site change
  direction, and they are all next to a new slab.
- **Shores (lakes, coasts, rivers).** The first water cell along every shore gets a waterlogged half-slab of the bed:
  - basalt or claystone sand on coasts;
  - rhyolite gravel at Bracciano;
  - muddy gravel in Earth's river beds.

  That is 140–1,700 per 160 × 160 area. The water surface is unchanged (at most 51 fluid cells per site differ). The shore
  rim becomes half a block shallower. Water plants moved up by this step all stayed in water (0 out of water).
- **Bare-rock mountains.** Behave as intended: Earth's stripped rock gets rock slabs (Mont Blanc 16,800, mostly granite
  and limestone, plus 377 snow-block slabs; Aconcagua 8,900). There are 6 snow layers on slabs at Mont Blanc; the reported
  "floating snow" would be a client rendering issue.
- **Bog peat.** `earthmap:bogpeat` has no slab variant and stays full. The counts are identical (1–34 blocks per site),
  and none sat on an exposed edge in the five sites that had any, so nothing shows. Normal surface peat gets slabbed.
- **Caves.** About half of all slabs are below the surface, because Column mode slabs cave floors. Surface mode
  (`/ts g s Surface`) would avoid that.
- **Edges missed.** At most 36 terrain edge blocks per site stay full, against 320–4,840 that become slabs.

## Side finding: Earth worldgen is not repeatable

The two controls are identical runs, yet they differ by 6 to about 9,100 solid cells per site, with no fluid
differences larger than 8. Freshwater plant species and placement change completely between runs. For example, Lake
Geneva has hornwort and muskgrass in control1 but spiked/northern milfoil and milfoil feather in control2. Whether that
comes from vanilla or from Earth was not investigated. Diffs between worlds therefore need a second control, as used here.

## Not verified

- Client rendering of Place On Slabs offsets (plants, snow, chests on slabs) and the visual seam of waterlogged slabs.
- Water behaviour after block updates next to shore half-slabs and near Earth's falls.
- Story locations and Earth Ruins sites; Terrain Slabs says it skips generated structures.
- Memory use: commenters report about double the RAM with some worldgen mods. Not measured.
