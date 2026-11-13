# Outline coasts for the big land (0.14.5)

Status: implemented in 0.14.5 ([decision 0012](decisions/0012-coast-outlines.md)), behind `EarthParams.Coasts`.
It's on for new worlds where a 2' grid cell is at least `CoastMinCellBlocks` (32) blocks wide, and existing saves can
opt in with `UpgradeExistingWorldCoasts`. `terrain.bin` and `islands.bin` don't change. The outlines are a new file,
`earthdata/coasts.bin`. Built as a prototype on 2026-10-14 and ported onto 0.14.4 on 2026-10-18; not played yet.

## The problem

Islands under 1000 km² already come from GSHHG full-resolution outlines (`IslandLayer`, `islands.bin`). Everything
bigger, meaning continents and big islands, still gets its coastline from the 2' ETOPO grid. A 2' cell is about
3.7 km, so a coast can't be shaped more finely than the bicubic interpolation of 3.7 km cells plus detail noise. In
world blocks that is:

| ring (blocks) | m per block | 2' cell (blocks) |
|---|---|---|
| 204 800 | 196 | 19 |
| 512 000 | 78 | 47 |
| 4 000 000 | 10 | 370 |

At 4M a coast is a smooth curve with a bend every 370 blocks, and the only finer detail comes from noise. Headlands,
coves, skerries, spits and narrow fjords are missing or the wrong shape. At 512k the error is about a cell (47 blocks).
At 204 800 the grid is about as fine as the outline matters, so nothing changes there.

## What the grid and the outline disagree on

`tools/bake_earth.py` was compared offline with the 2' grid (with the islands overlay) against the big-land outline
rasterised at 2' (north of 60°S):

- Outline land where the grid has sea: 128 000 cells. 77% of them are within one cell of the outline, 91% within
  three. The deep cases are mostly places where the outline is right and the grid is wrong:
  - polders the grid flooded because they're below sea level: Haarlemmermeer, Noordoostpolder, Flevoland, the Elbe
    marshes, the Dollart;
  - the Sebkha Tawurgha;
  - the Po and Mississippi delta marshes;
  - northern Greenland and Ellesmere, where the ice shelves are land in GSHHG and sea in the grid.
- Outline sea where the grid has land: 28 000 cells. Most are within a cell. The deep ones are nearly all Greenland
  glaciers filling fjords that GSHHG has as water, plus a few lagoons the grid has as land (the Curonian Lagoon,
  Hotham Inlet).
- The IJsselmeer, Markermeer and some coastal lagoons are GSHHG lakes (level 2) inside the land polygon but sea in the
  grid. If the outline were trusted alone, it would fill them with land.

So the outline should decide land and sea near any coast, except on ice and in water GSHHG calls a lake.

## Design

### Data: `coasts.bin` (bake_coasts)

- **Contents**: the GSHHG L1 polygons of at least 1000 km² north of 60°S (295 polygons, 5.1 M vertices). Also the
  130 GSHHG L2 lakes inside them whose 2' cells are mostly grid ocean (the IJsselmeer and the like), included as
  holes. Antarctica isn't in L1 (GSHHG has it as L5/L6), so tiles south of 60°S are marked "no data" and the grid stays
  as it is there.
- **Tiles**: 1° tiles. Each holds every edge whose bounding box comes within 0.05° of it, including edges across 180°
  shifted by 360°. The edges are stored as runs of consecutive vertices: zigzag varint deltas in 1/20000° units
  (5.6 m, half a block at the 4M ring), compressed per tile with brotli. The cut edges along 180°, where GSHHG
  splits Eurasia and the Chukchi peninsula, are left out.
- **Tile state**: one byte per tile says whether the tile centre is land, whether it has edges, or that it has no data.
  The baker works out each centre by counting crossings along a ray from 180°W, using all edges including the cuts.
  So at runtime a tile with no edges is entirely land or entirely sea, and needs no decoding.
- **Bake command**: `python tools/bake_earth.py --coasts-only` reads the flags from the existing `terrain.bin`
  (about 25 s). The full bake runs it last.

### Runtime: `CoastLayer` (src/Core/Coasts.cs)

The layer works in the world's block metric (x = longitude × BlocksPerDegreeX, z = latitude × BlocksPerDegreeZ), like
`IslandLayer`.

- **Signed distance**: `SignedDistance(lat, lon)` returns the distance to the coast in blocks, positive on land and
  capped at ±`Reach`. It returns NaN where there's no data.
- **Reach**: about 640 m in blocks, clamped to 8–64. That is 8 at 512k and 64 at 4M.
- **Tile decoding**: a tile is decoded the first time a column comes into it, and up to 96 tiles stay in a cache (LRU,
  lock, as for `EarthData`'s tiles). Decoding builds:
  - the vertices in blocks;
  - a grid of square cells of max(Reach, 16) blocks, each with the edges whose bounding box touches it (CSR arrays);
  - whether each cell's centre is land. This is found by counting crossings from the tile centre: along the vertical
    through the centre to the row, then along the row. The file gives the tile centre's state.
- **Inside test**: start from the point's cell centre and flip once for every edge crossed on an L-shaped path: along
  the centre's row to the point's x, then up or down to the point. Both legs use half-open crossing rules consistent
  with how the cell centres were computed. Only that cell's edges can be crossed.
- **Distance**: the point-to-segment minimum over the cells within `Reach`.

### Terrain: `TerrainModel.CoastColumn`

This is called from `NaturalColumn` after the grid's own sample. It is skipped:

- for grid lake water;
- for lake/sea barrier strips;
- where the outline's weight is zero.

Because it runs inside `NaturalColumn`, the gutter's edge columns and Antarctic bridge see it too. Islands, rivers and
river exit channels are applied on top as before.

1. **Weight `w`** (0..1): how much the outline decides. It is
   `max(smoothstep(SeaNear / 0.25), 1 − smoothstep((d − Reach/2) / (Reach/2))) × (1 − smoothstep(Ice / 30 m))`:
   - `SeaNear` is the bilinear share of the 2×2 cells whose water level is 0. The grid marks every cell within 3 cells
     of the sea that way.
   - `d` is the outline distance.
   - So the outline decides within about 11 km of the grid's sea, and also within half a Reach of its own coasts and
     across its own sea. That second part covers fjords and lagoons the grid has nowhere near any sea: the inner
     Fjærlandsfjord is 15 km from the grid's sea, and a first version that used `SeaNear` alone cut it off there.
   - It doesn't decide on the ice sheets.
   - Inland, the outline tiles there either have no edges (no decoding at all) or nothing within Reach, so `w` is 0 and
     the grid's column is returned as it was.
2. **Outline side**: `fo = d − ChannelBlocks/2 × Ocean`, then sea is damped by `(1 − smoothstep(Lake / 0.25))`, then
   the result is clamped to ±Reach.
   - Where the grid has sea, the outline's land is pulled back by up to 2 blocks (`ChannelBlocks` = `MinIslandBlocks`,
     4). A fjord or sound the bake kept open therefore stays at least about 4 blocks wide even where GSHHG makes it
     narrower than a block or two (the Nærøyfjord is 250 m: 3 blocks at 512k).
   - The outline never puts sea right beside one of the grid's lakes, so fresh water and salt water don't touch at
     different levels.
3. **Land or sea**: land when `w·fo + (1 − w)·fg ≥ 0`, where `fg` is the grid's own sign: the elevation clamped to
   ±Reach where the 2×2 has sea, and +Reach otherwise. Both values are capped, so where outline and grid disagree the
   coast moves smoothly from one to the other as `w` fades. There is no cut along a band edge.
   A point's tile and its offset in it come from the same sums. Before that was the case, a point a rounding error
   west of a whole-degree meridian landed 360° away and drew thin sea lines along the meridians (now a check).
4. **Elevation**: `w·ho + (1 − w)·h`, where `ho` is a coastal profile on the outline's side, from the coast line to
   `Reach` in:
   - land: `CoastLand (1 m) + (LandElev + detail − 1 m) · g`;
   - sea: `CoastSea (−1.5 m) + (min(SeaElev + detail, SeaElev/2) + 1.5 m) · g`.

   Here:
   - `g = 1 − (1 − t)^p` with `t = dist/Reach`, and `p = 1.5 + 3·smoothstep((relief − 150)/1000)`. On rugged land the
     profile rises fast (cliffs). On flat land it rises slowly (beaches).
   - `LandElev` and `SeaElev` (in `EarthData.SampleTerrain(..., coastRefs: true)`) are the grid's bicubic with the
     other kind's cells among the 4×4 replaced by this kind's tent-weighted mean (at least 2 m for land, at most −5 m
     for sea). Where the outline adds land over grid sea, it rises like the land around it: a polder at a few metres,
     a fjord wall towards the walls' height. Where the outline adds sea over grid land, the floor drops like the sea
     beside it. Neither inherits the grid coast's slope.
5. **Columns**:
   - Sea is one block deep at the coast, going to the world's `MinSeaDepth` (6 in new worlds) one Reach out.
   - Land never goes below the sea or a nearby lake's water (with eased lake shores, the level the lake holds it at).
   - Land is beach where it is lower than `BeachHeight × (1 − smoothstep(d/Reach))`. Lake beaches work as before.
   - Ice works as before.

`CoastLandAt` gives the ocean map (`EarthOceanLayer`) the outline's land or sea wherever `w ≥ 0.5`.

### Save policy

- `EarthParams.Coasts` (ProtoMember 61) holds the version of the coast data (0 = none). New worlds get it when all of
  these hold:
  - `Coasts` is on;
  - `Islands` is on, because the coasts leave the small islands out and only the islands' outlines bring them back;
  - `CoastLayer.CellBlocks(geo) ≥ CoastMinCellBlocks` (32: rings from about 345 600 blocks).
- Old saves have 0 and are unchanged. `terrain.bin` doesn't change: the bake reads it and writes only `coasts.bin`.
- `UpgradeExistingWorldCoasts` is one-shot, the same as the other upgrade flags. Chunks that are already generated
  keep their coasts and meet the new ones with a step where the two differ.

### Smoothing GSHHG's raster traces

At the 4M ring the first renders showed that much of GSHHG "full" is traced from a raster. Western Norway is straight
runs of about 550 m joined by 45° chamfers, and 18% of all big-land edges are exactly horizontal or vertical (in parts
of Indonesia it's about half). At 10 m per block that's a 50-block staircase. Real detail below about 200 m isn't in
the data, so the outline is rounded off: two passes of Chaikin corner cutting, each corner cut by at most 0.0015°
(about 170 m), with points on 180° kept.

Doing this in the bake would have made the file 22 MB (16 M points), so `coasts.bin` keeps the raw outline and
`CoastLayer.Smooth` runs when a tile is decoded. The baker works out each tile centre's land/sea on the same smoothed
outline (`smooth_ring`, the same arithmetic), so the parity stays consistent. A run's two ends are not cut the way the
whole ring's corners would be, but they lie beyond the tile's 0.05° margin, where it makes no difference.

The raw vertices sit on a 3″ grid (about 93 m north–south), and GSHHG is also often 60–170 m from OpenStreetMap's
coastline, so no smoothing recovers real coasts finer than that. Measurements and images:
[research/coast-and-elevation-sources-2026-11-03](research/coast-and-elevation-sources-2026-11-03.md).

## Interactions

- **Small islands**: the coast outlines don't include islands under 1000 km², so the coast layer makes them sea
  (including the grid's blobs near the big land, which the islands overlay keeps). `ApplyIslands` runs afterwards and
  brings back each one wide enough for the world. That's why coasts need the islands. Brač and Hvar off Split
  stay.
- **Rivers and mouths**: `ApplyRiver` runs on top. Mouth prolongations cut only low ground, and new outline land at a
  mouth is low because it starts at `CoastLand`. Wide estuaries and delta distributaries that GSHHG has as sea come in
  as sea channels at sea level (the Mekong's branches). If a river ends short of an outline that reaches further out
  than the grid did, `SeaChannels` (worlds with river exit channels) cuts its way out through the low land along the
  channel it found on the grid. High outline land (a headland) on such a channel is not cut, as for the grid's own
  headlands. Check: the Bassac at Cần Thơ reaches open water at 512k and 4M.
- **Lakes near coasts**: grid lake water and lake/sea barrier strips are left to the grid. The outline makes no sea
  where the 2×2 cells hold a lake (`Lake` damps the sea side), so fresh water and salt water at different levels never
  touch. Lakes that GSHHG has as lakes but the grid has as sea (the IJsselmeer, Markermeer and 128 others) are holes in
  the outline land, so they stay as the grid has them. Lake beaches work as before.
- **Fjords (narrow channels kept in the bake)**:
  - Where the grid has sea, the outline's land is pulled back by up to `ChannelBlocks/2` (2 blocks). A channel GSHHG
    draws narrower than about 4 blocks therefore stays open where the bake kept it.
  - Fjords that GSHHG itself leaves out stay land: they are narrower than its trace (the inner Nærøyfjord, 250 m, ends
    8 km short of Gudvangen). Fjords that GSHHG has and the 2' grid doesn't now appear: the Aurlandsfjord to Flåm and
    the Fjærlandsfjord at 512k, which the grid had closed.
  - The fjord walls rise from the outline towards the walls' height (`LandElev`), and the floor drops towards the
    fjord's depth (`SeaElev`). Neither is the grid's 3.7 km mush.
- **TerrainExpansion**: the coast is sampled at the projected source position like everything else in
  `ColumnForExpansion`, so an expansion magnifies the outline coast with the rest.
- **The seam gutter**: `GutterColumn` takes its edge columns from `NaturalColumn`, so the gutter blends from the outline
  coast. The Antarctic bridges are south of 60°S, where there is no outline. Checks: the gutter's water is as without
  coasts, and the coasts add no differences one ring apart. A few columns at the gutter edges already differed one ring
  apart, because the natural terrain's noise there uses the raw x.
- **Antarctic**: there's no L1 south of 60°S (GSHHG has Antarctica as L5/L6, ice front and grounding line), so those
  tiles are "no data" and the grid decides. Check: columns along the Antarctic Peninsula and the Ross Sea coast are
  unchanged.
- **Ice**: the weight falls to 0 under ice thicker than about 30 m. Greenland's glaciers and fjords, and the ice shelves
  of north Greenland and Ellesmere, keep the grid's coasts.
- **Ocean map** (`EarthOceanLayer`): it follows the outline where it decides (weight at least 0.5).
- **Climate, trees, geology**: these still read the grid's ocean fraction (`EarthTrees` shore and sea-chunk tests,
  `RegionalGeologyCompatibility`, `TerrainModel.Surface`). On outline land over grid sea (polders, new headlands) that
  can mean a few trees fewer than inland, and climate is as the grid's cell says. This is a follow-up (see below).

## Measurements (2026-10-14)

`dotnet run -c Release -- <root> 384 coasts` in tests/EarthMapTests. The renders go to tests/out/coasts-*.png. The
"before" maps have the outline drawn in red.

**File**: `coasts.bin` is 9.5 MB and bakes in about 25 s with `--coasts-only`.

- It holds 5.1 M vertices.
- There are 5436 tiles with edges, 12 791 all-land tiles, 35 773 all-sea tiles, and 10 800 no-data tiles (south of 60°S).
- With 1e-5° units the file would be 12.3 MB. Smoothed in the bake it would be 22.3 MB.
- The pack grows from about 56 MB of earthdata to about 65 MB.

**Land and sea against the outline**: points of a 160×160 grid over each site, at least 3 blocks from the line, small
islands and rivers left out.

| site | 512k before | 512k after | 4M before | 4M after |
|---|---|---|---|---|
| Sognefjord (61°N 6.5°E) | 93.5% | 99.9% | 91.0% | 99.7% |
| Mekong delta | 97.5% | 100.0% | 96.0% | 100.0% |
| Dalmatia (Split) | 98.7% | 100.0% | 97.5% | 100.0% |

What's left over in Norway is the narrow-channel rule: up to 2 blocks of water kept where the grid has sea.

**Other checks**:

- 204 800 ring: off (a 2' cell is 19 blocks).
- Inland, open sea, and the Antarctic Peninsula and Ross Sea coasts: unchanged column for column.
- The Sognefjord is open sea from its mouth to Flåm and to Fjærland. Flåm was closed before at both sizes, and
  Fjærland was closed before at 4M.
- The Bassac at Cần Thơ reaches open water.
- The Noordoostpolder, Haarlemmermeer and Flevoland are land; the grid had them as sea.
- The IJsselmeer and the Split Channel stay water, and Brač stays land.
- There are no seams along whole degrees.
- The seam gutter's water is unchanged, with no extra differences one ring apart.
- Deltas get beaches (100% of the coast's land). The Dalmatian coast rises about 11 blocks within 3–6 blocks at 512k.

**Cost per column**: the same 256×256 block area, after warming the caches, on a laptop (µs).

| place | 512k before | 512k after | 4M before | 4M after |
|---|---|---|---|---|
| Norwegian coast (61.05°N 5.2°E) | 0.78 | 0.95 | 0.86 | 0.99 |
| Mekong delta | 0.96 | 1.10 | 4.72 | 4.86 |
| Alps (inland) | 0.75 | 0.78 | 0.74 | 0.76 |

So a coastal column costs about +0.15 µs and an inland one nothing measurable. The Mekong delta at 4M is slow already
without coasts: that's the river network there. Decoding a coastal tile (brotli, smoothing, cell grid, cell centres)
takes a few milliseconds and happens once per tile while it stays among the 96 cached.

## Known limits and follow-ups

- **The data's limits**:
  - The outline is no better than GSHHG "full": a raster trace with little detail below about 200 m.
  - Fjords narrower than its trace are missing (the inner Nærøyfjord) and stay land.
  - At 4M a 1–2 km fjord is 100–200 blocks wide and looks right, but its floor is the grid's: deep fjords have the
    bake's −300 m floor smoothing, which shows as straight-edged deep patches (also before).
- **Small islands' staircases**: fixed separately by island rules v2 (0.14.4), which round off `IslandLayer`'s rings
  the same way.
- **Tree, geology and climate placement** still read the grid's ocean fraction near coasts (`EarthTrees` shore and
  sea-chunk tests). With coasts they should ask the column or `CoastLandAt`.
- **River exit channels** are found on the grid's cells and only cut low land. A channel whose grid path crosses a
  high outline headland stays blocked there, as with the grid's own headlands.
- **Rivers that end in GSHHG sea above sea level**: a river whose pools stand above 0 m where GSHHG already has sea
  (a long estuary the grid has as land) meets the sea with a step. None was seen at the test sites; the Amazon at
  Manaus is not sea in GSHHG.
- **Grid lakes the outline calls sea**: lakes the grid has but GSHHG has as part of the sea (Lake Melville, the
  Curonian Lagoon) keep their water and level, so the outline's sea stops at the grid lake.
- **Old saves** opting in with `UpgradeExistingWorldCoasts` get a step where generated chunks meet new ones along
  every coast. That's worse than the islands upgrade, which only affects reefs.
- **Better data** (OpenStreetMap coastline, a 1′ terrain grid) is deferred: see decision 0012.
- **Test cost**: the default suite now also runs these checks without renders. The 4M river network takes about 90 s
  to build, as it would in a world that size.
