# Lake outlines from the GLO-90 water mask: measuring pass

Date: 7 November 2026. The author asked what lakes need ("What is needed for lakes?"), then for a measuring pass first
("Yes, measure first"). The question: how much data would block-resolution outlines of the water mask's lakes take,
which lakes would they add, and do they remove the "lake Earth lacks" patches in the Patagonia window. Measured offline
only. Nothing in `src/` or `earthdata/` changed, and nothing is decided.

## Method

`tools/measure_lake_outlines.py` reads the Copernicus GLO-90 Water Body Mask, class 2 (lake), north of 60°S:
- **Lakes.** Each 1° tile's lake pixels (3″) are labelled 8-connected. Pass 1 joins each lake's pieces across tile edges
  (east–west and north–south; corners not), so lakes are whole when tagged and cut by size.
- **Missing.** A lake is missing when Earth's sea and lakes cover under half of it. Earth's lakes here are the baked
  Natural Earth lakes, the terrain grid's lake flags and the terminal lakes of `lakes.bin` (the water atlas's caches).
- **Tags,** as the water atlas has them, on whole lakes:
  - sea: Earth's sea covers half or more;
  - polar: the atlas's box, north of 75°N or north of 59°N between 75°W and 10°W (Greenland, also Iceland and northern
    Quebec);
  - reservoir: a Natural Earth reservoir the bake dropped, or a HydroLAKES reservoir (type 2) whose pour point lies at
    the lake with a lake at least 0.3 times its size;
  - raised: the same with a natural lake raised by a dam (type 3). Raised lakes are kept and drawn.
  - salt flat: under 250 mm a year and flat (height spread under 1 m);
  - ice: half or more under RGI glaciers.

  One change from the atlas: the dam's pour point must lie at the lake itself, within its box plus 3 pixels. The atlas's
  margin of √(dam lake area) was meant for sites that are only a part of a reservoir. On whole lakes it tagged every
  pond near a big regulated lake.
- **Level.** The median GLO-90 height of the lake's pixels. GLO-90's water is flattened, so this is its surface.
- **Outline.** Marching squares at the pixel edges (contourpy), simplified to 0.5 or 1 pixel. Vertices are in 1/2400°.
  Each tile is coded as `coasts.bin` codes its edges (varints, brotli quality 11): per lake its level, flags and rings,
  then zigzag deltas.

A run takes 14 minutes with 24 workers. Outputs were not kept in git (`summary.txt`, `lakes-joined.txt`, `sizes.json`,
`big.json`).

## The lakes Earth lacks

All lakes, not only open water: the mask has 4.56 million lake pieces in 14,985 tiles, joined into 4.52 million lakes.

| Missing real lakes | Lakes | Area |
|---|---:|---:|
| under 0.05 km² | 2,986,236 | 51,242 km² |
| 0.05–0.1 km² | 461,702 | 32,461 km² |
| 0.1–0.5 km² | 517,314 | 110,054 km² |
| 0.5–1 km² | 85,024 | 59,166 km² |
| 1–10 km² | 82,496 | 213,268 km² |
| 10–100 km² | 7,352 | 164,885 km² |
| 100 km² and more | 328 | 103,213 km² |
| **all** | **4,140,452** | **734,289 km²** |

Natural lakes raised by a dam add 56 missing lakes and 13,365 km², among them Southern Indian Lake, Manicouagan, Lac Seul
and Lac Saint-Jean. The mask has them at their raised, modern extent.

The tags leave out these missing lakes:
- reservoirs: 8,010 lakes, 218,808 km² (Volta, Kariba, Kuybyshev, Nasser, Rybinsk, Guri, Tucuruí, Sobradinho, Bratsk);
- salt flats: 255,356 lakes, 130,958 km²;
- polar: 39,314 lakes, 9,310 km²;
- glacier: 596 lakes, 114 km².

The lakes Earth already has come to 51,610 lakes and 1,316,095 km², with the Caspian (Earth's grid lake) at 402,439 km².

The atlas's recount had 238,768 km² of real gaps in 37,233 places. It counted 30″ cells at least 80 % lake. This pass
counts every pixel and every lake, so it finds far more small lakes.

The largest missing real lakes:
- Rann of Kutch (11,653 km²);
- Smallwood Reservoir (5,668 km²);
- Lake Rukwa (5,638 km², 39 % in Earth);
- Zeya Reservoir (2,236 km²);
- Williston Lake (1,391 km²);
- the Amazon floodplain lakes (Lago Grande do Curuai 1,225 km²);
- Mweru (34 % in Earth), Chilwa, Alexandrina, Abitibi, Oulujärvi, Songkhla and Chilika.

### Where the tags are wrong

- **Salt flats.** GLO-90 flattens all water, so every lake in a dry climate is "flat". The rule catches the real pans:
  Uyuni, Gairdner, Torrens, MacLeod, Carnegie and Sevier. It also takes real lakes in cold dry places: Zaysan (4,299 km²),
  Ulungur, Pangong Tso, Karakul and the Mackenzie delta lakes.

  HydroLAKES can't separate them: it lists Uyuni, Eyre, Torrens and Gairdner as ordinary lakes (type 1). Natural Earth's
  playa layer (`ne_10m_playas`, not in `rawdata/`) or a short list kept by hand could.
- **Rann of Kutch.** The atlas names it as a seasonal marsh the mask counts as lake. At 450 mm a year the salt rule
  doesn't catch it.
- **Reservoirs the dam test misses:** Smallwood, Zeya and Williston. Their dam is away from the mask's lake (narrow arms
  are river in the mask), or HydroLAKES' record of the lake is smaller.
- **Wetland clusters.** The missing lakes of 10 km² and more are 7,716 lakes but 65,807 separate outlines. Many are
  ponds touching at corners (fens, floodplains, thermokarst), not one open water.

## Data size

Bytes with brotli, coded as above. Simplified to 1 pixel, with 0.5 pixel in brackets:

| Smallest lake kept | Missing lakes only | All lakes (Earth's redrawn too) |
|---|---:|---:|
| all | 41.9 MB (48.5) | 44.3 MB (51.7) |
| 0.05 km² | 22.9 MB (28.4) | 25.1 MB (31.4) |
| 0.1 km² | 17.8 MB (22.6) | 19.9 MB (25.5) |
| 0.5 km² | 9.5 MB (12.6) | 11.6 MB (15.4) |
| 1 km² | 7.2 MB (9.6) | 9.2 MB (12.4) |
| 10 km² | 2.5 MB (3.4) | 4.5 MB (6.1) |

- **Main zip.** None of this fits. The zip has about 371 KB left under 40 MB (tall.16: 39,628,689 bytes); even the
  missing lakes of 10 km² or more are 2.5 MB.
- **Scale.** The mask's tiles are 548 MB and the GLO-90 heights 8 GB. The missing lakes of 1 km² or more are 4.2
  million vertices (1 pixel).

## The Patagonia window

The window is the bench spot's 640 × 640 blocks at -51.6, -72.9: tall.15's model columns, whose lakes are tall.16's. In
it, land where the mask has a lake drops from 49.7 km² to 4.0 km² once the outlines are drawn as lake. What is left is
pixel-sized fringe around the smallest ponds: here one block is about one mask pixel, and the mask is read at the
nearest pixel.

Earth has no lakes in this window. All 244 mask lakes there are missing, so "missing only" and "all lakes" give the same
picture. This shows only lake or land: no levels, beds or shores.

## What a build would need (proposal, not decided)

1. **Where the data goes.** The outlines only make sense with GLO-90 heights, since the levels are GLO-90's. So they
   belong with the GLO-90 download, either:
   - an extra file on the `earth-glo90` release, fetched by "Download all" (publishing needs the author's yes), or
   - an extra data pack.

   The size depends on the cut: 7–12 MB for lakes of 1 km² or more, 42–52 MB for all.
2. **Tag fixes:**
   - salt flats: a playa layer or a list;
   - the Rann of Kutch;
   - the reservoirs the dam test misses;
   - whether wetland clusters and raised lakes count.
3. **The mod:**
   - a lake layer like `CoastData`/`CoastLayer`: inside or outside, and distance to the shore;
   - water at the lake's level;
   - a bed shape (GLO-90 gives only the surface, so a depth rule is needed);
   - shores where the land around sits below the level;
   - how the outlines meet Earth's own lakes and the terminal lakes it fills on purpose (`lakes.bin`, decision 0005);
   - the rivers in and out (0055).

   As with the rivers, only new worlds would get it, through a saved rules field.
4. **Checks:** the bench at the Patagonia spot and in a lake district (Finland, Canadian Shield).

## The build: all lakes (tall.17, evening)

The author: "Take all lakes. We will increase size anyway and can put large data into same repo as height map in git."
Built as [0053](../decisions/0053-scales-included-glo90-and-glo90-rivers.md) item 17 (proposed).

**Bake** (`tools/bake_lake_outlines.py`, 15 min on 24 workers): the measuring pass's lakes, joined across tiles, with
the tags changed:
- **Left out:** lakes Earth's sea has (9,695 lakes, 13,454 km²); reservoirs, as before plus any lake half or more
  inside a Natural Earth reservoir or a lake the bake dropped as one (18,539 lakes, 267,567 km²: Smallwood, Volta,
  Kariba, Kuybyshev, Nasser and Zeya now among them); lakes on glaciers (630, 120 km²).
- **Left to the grid:** lakes 30 % or more on Earth's salt lakes and the terminal lakes it fills on purpose
  (decision 0005; 19,772 lakes, 517,293 km², the Caspian among them). The grid keeps those at their 1318 extent.
- **Polar lakes are drawn,** Greenland's and the high Arctic's included.
- **Salt:** under 250 mm a year, flat, and arid by de Martonne's index (rain / (T + 10) under 15). `climate.bin`'s mean
  temperature is reduced to sea level (Uyuni read 32.7 °C), so T is taken down 6.5 °C per km of the lake's level. Rain
  alone made Great Bear Lake and the Mackenzie delta salt; the index at sea-level temperatures left Uyuni fresh. Now
  126,824 lakes and 84,871 km²: Uyuni, Eyre, Torrens, Gairdner, the Salton Sea, Khyargas and Pangong salt; Zaysan,
  Great Bear Lake and the Arctic fresh. Ulungur (slightly brackish) counts as salt.
- **Drawn:** 4,472,466 lakes, 1,783,625 km², every size; 4,416,906 of them (847,223 km²) missing in Earth's grid.
  Raised natural lakes (68, 168,638 km²) are drawn at their modern, raised extent.

**File** `lakes-glo90-1.bin`: 58,106,553 bytes, sha256 `8f0075ac…79236384`; 5.56 million lake pieces in 14,880 tiles
(tiles cut lakes at their edges), 34.2 million vertices, 106.9 MB before brotli. Format in the tool's docstring and
`src/Core/LakeOutlines.cs`.

**The mod** (`LakeOutlineData`, `LakeOutlineLayer`, `TerrainModel`): wherever a world reads GLO-90 for a square (not the
included heights, not a fallback square) and the file covers it, the outlines decide:
- the grid's fresh lakes and their rings are taken out there; salt and terminal grid lakes stay;
- inside an outline: water at the lake's level; the bed falls 5 % from the shore to 40 m, or to the grid's own depth of
  a lake taken out there where that is deeper (Baikal, Tanganyika), with some noise; at least 1 block of water at the
  shore and 3 a few blocks out;
- outside, within Reach (800 m, 6–64 blocks): rivers meet the nearest lake at its level; dry land beside a lake is held
  at its water top for 1.5 blocks, then sinks a block per block; low land within a block of the water is beach (lakes
  of 2 blocks² or more); sea or a lower lake right beside a higher one becomes a strip of land at its top;
- the river valleys keep that rim, so no lake spills into a valley beside it.

**Probe** (1:100, 500,736 wide, height 384, 640 × 640 blocks, tall.16 against tall.17; scratch harness `look17`):

| Window | Mask lake | tall.16 drawn | tall.17 drawn | Off by more than 2 blocks |
|---|---:|---:|---:|---:|
| Patagonia | 49.8 km² | 0 | 46.0 km² | 0 |
| Saimaa | 539.4 km² | 463.5 km² | 522.8 km² | 0 |
| Kuopio | 533.8 km² | 462.2 km² | 518.9 km² | 0 |
| Lake Geneva | 479.8 km² | 415.9 km² | 478.9 km² | 0 |
| Uyuni (salt) | 3,741.1 km² | 0 | 3,738.5 km² | 0 |

No lake or sea water stands beside lower dry ground or lower still water in any window (tall.16: none either). Cost 0.2–
0.4 µs per column on all threads, warm (tall.16: 0.1–0.3).

**Open:**
- two outline lakes within a block of each other at different levels (inside columns don't look at other lakes);
- the in-game map and `/earth info` still read the grid's lakes;
- raised lakes are drawn at their modern extent;
- the Rann of Kutch and wetland clusters as above; reservoirs the wider reservoir test still misses were not checked
  one by one (Williston not looked up).
