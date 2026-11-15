# Earth 2.0.3 — Vintage Story 1.22

**2.0.3 (15 November 2026).** Earth 2.0 is one mod and makes new worlds. Worlds from older Earth versions (0.x, 1.x and
the 2.0.0 release candidates) are not supported: start a new world. From 2.0.0 on, saves are kept working. 2.0.1 makes
vanilla rocks the Earth tab's default, takes rain and storm frequency off the world screen, joins the two canal switches,
reads start coordinates pasted from Google Maps, adds downloading the GLO-90 heights before a world starts, and lets
the prospecting pick read Earth's rock blocks; 2.0.2 fixes a new world that couldn't start again after its first start
stopped; 2.0.3 moves Start and Start coordinates to the game's Player spawn and death tab. What it brings over 1.0.x,
and every earlier version, is in
[CHANGELOG.md](CHANGELOG.md).

Earth generates the real Earth in new worlds of its play styles: land and sea floor from real elevation (Copernicus
GLO-90 on land), real rivers, lakes and coasts as they were around 1318, the Greenland and Antarctic ice sheets, the
real climate and Earth's own weather, soil by real aridity, real rocks and ground to choose on the Earth tab (the
game's rocks by default), and trees, plants and animals where they lived around 1318. The world wraps east–west on Cylinder World's ring (the seam runs through the Bering Strait, the poles are
its walls), at a scale from 1:200 to 1:5.

- **Needs:** Vintage Story 1.22, **Cylinder World 1.1.0** or newer (with 1.0.4 a new world whose first start stopped
  was taken for an existing one on the next) and **Hydrology 0.6.2** or newer (its river query version 3; older Hydrology could let a water
  source spilled into the sea make the sea climb and flood the map), on the server and every client. Sea is
  recommended.
- **Data:** the first start of a server (or a single-player game) downloads Earth's data from GitHub once per computer:
  about 290 MB, 170 MB more of ground data for worlds with ground columns, and the GLO-90 land heights with about
  190 MB of lake, river and dam files beside them (see [Quick start](#quick-start)). A game client downloads nothing.
- **New worlds only:** worlds from Earth versions before 2.0.0 (0.x, 1.x, the release candidates) are not supported:
  start a new world (the author, 15 November 2026). Worlds made with 2.0.0 and later are kept working.
- **Memory and start:** a 1:10 server peaks at about 5.5 GB while it starts, then holds 4.5 to 4.8 GB; 1:5 peaks at
  about 7.2 GB. A world's slowest map-model results are cached in `VintagestoryData/ModData/earth/cache`, so its later
  starts are faster (`ModelCache` in the config).

On top of the terrain, the world is regional:

- **Trees and shrubs** follow native botanical regions (WCVP × TDWG) as well as climate, with regional stand-ins where
  no installed tree is native, so no forest is left bare. Vanilla works alone; optional adapters cover all eight
  Floral Zones packs and Wildcraft Trees/Herbarium — [details](docs/regional-trees-implementation.md),
  [pack instructions](modpack/README.md).
- **Ground plants** from the Floral Zones packs grow only in their native ranges —
  [details](docs/regional-plants-implementation.md). Vanilla ground plants keep their climate rules.
- **Rocks** follow global lithology and surface-sediment maps (GLiM, GUM), with optional Geology Additions materials;
  clay, peat and salt follow the ground they form in — [details](docs/regional-geology-implementation.md).
- **Ores** follow evidence maps of real deposits and districts, with a background chance on suitable host rocks
  everywhere, fewer and larger deposits, and rate classes so clay, starter copper/tin, coal, iron and tin stay
  available — [details and settings](docs/regional-ores-implementation.md),
  [heat-map preview](docs/research/ore-heatmaps-v1/index.html). Optional Geology Additions bog iron comes from finite
  natural peat.
- **Weather** (0.15.0) follows the real climate of each place and month: monsoon rains and dry seasons, dry
  Mediterranean summers, desert skies, the trade winds, westerlies and monsoon winds, fronts and showers that move
  with the winds, tropical cyclones and mid-latitude storms in their seasons — [below](#weather).
- **Animals** from Fauna of the Stone Age (optional) spawn only within their ranges and habitats, either natural
  ranges without human impact or present-day ones; extinct animals can be included or excluded —
  [details](docs/regional-fauna-implementation.md).

Existing worlds keep loading across updates: rule fixes that cause no visible seams reach them automatically, rock
layers stay as they were unless you opt in once (see `UpgradeExistingWorld*` below).

Everything comes from free public data (see `earthdata/ATTRIBUTION.txt` and the data releases' READMEs): ETOPO 2022
(NOAA, public domain), University of Delaware / NCEP / GPCP climate normals (NOAA PSL), Natural Earth lakes (public
domain), GSHHG shorelines (LGPL), HydroRIVERS (www.hydrosheds.org, free with attribution), and among others
Copernicus GLO-90 land heights (its required notice is also on the Earth tab), OpenStreetMap beaches and canal outlines
(ODbL), GIRES river intermittence (Messager et al. 2021, CC BY-SA) and Global Dam Watch reservoirs.

## Quick start

1. **Install** on the server and on every client: `earth-<version>.zip` (one mod since 2.0, [decision 0066](docs/decisions/0066-one-mod-data-from-github.md)),
   Cylinder World (`cylinderworld` 1.1.0 or later, required) and Hydrology (`hydrology` 0.6.2 or later, required:
   its river query version 3). Sea is recommended. The 1.0.x data-pack mods (Earth Terrain Data, Earth Water Data and the
   test builds' height, water-fix and ground packs) are no longer needed; if they are still installed, Earth copies its
   files from them instead of downloading them. To build the zip yourself: `dotnet build EarthMap.csproj -c Release`
   → `release/earth-<version>.zip`, with the version from `modinfo.json` (an existing zip of that version stops the
   build: bump the version, or pass `-p:OverwriteRelease=true` for an unpublished one).
2. **Create a new world** with one of the Earth play styles: **Earth · 1:100** (the default, 433 152 × 200 192 blocks,
   384 high), 1:200, 1:50, 1:25 or 1:10 (4 040 704 × 2 003 968 blocks, 1024 high); 1:5 is on the Earth tab. Each style
   sets the world size, height and mountain curve for its scale (decision
   [0068](docs/decisions/0068-world-size-by-scale.md)); the start is on the game's Player spawn and death tab, the
   other choices on the Earth tab. Other play styles keep vanilla generation. Earth 2.0 is for new worlds: worlds from Earth versions
   before 2.0.0 are not supported.
3. **First start downloads Earth's data** (the server, or the single-player game; a game client downloads nothing), once
   per computer into `VintagestoryData/ModData/earth`, shared by every world and every later Earth version:
   - about 290 MB of data files from the release `ed-v1` of
     [earth-data](https://github.com/Temporal-Poets-Society/earth-data) into `ModData/earth/data`, before the first
     chunk, each checked against its size and SHA-256;
   - about 170 MB of 30″ ground sets from the release `egts-v1` of
     [earth-ground](https://github.com/Temporal-Poets-Society/earth-ground) for worlds with ground columns;
   - the GLO-90 land heights from the release `egl-v1` of
     [earth-glo90](https://github.com/Temporal-Poets-Society/earth-glo90) into `ModData/earth/glo90`: all of it (8.6 GB)
     by default, starting when the world opens, with its progress in a window (`.earthelevation`); or, on the Earth tab,
     all of it before a new single-player world starts (on its loading screen), each square as the world reaches it, or
     Copernicus's originals (about 71 GB, converted on the machine). Beside the
     heights a new world fetches the release's lake and river outlines, river flow and dam files (about 190 MB:
     `lakes-glo90-2.bin`, `rivers-outline-glo90-4.bin`, `rivers-flow-glo90-1.bin`, `dams-glo90-3.bin`).

   Without internet, put the releases' files into those folders by hand (or set `DataRepo` in `earth.json` to a mirror
   or a local folder). A server that can't get the files a world needs stops and says which are missing, rather than
   generate a world with parts of its data missing. A GLO-90 square that can't be downloaded while the game runs leaves
   its chunks empty, with a chat line; they are made at the next start, once the square is there.
4. Check `server-main.log` for Earth's lines: the data it loaded, the layout, the spawn and where each story location went.
5. In game: `/earth` shows latitude, longitude, the real elevation and climate where you stand.
   `/earth tp 27.99 86.93` takes you to Everest (aboard a boat or ship, or with `water` at the end, you land on the
   nearest open water). `/earth map` renders the whole Earth as this world generates it to
   `VintagestoryData/EarthMap/earth-<seed>.png`, with spawn and the story locations marked. `/earth story` lists the
   story locations with their real coordinates. `/earth boattest` builds four 100-block test canals next to you
   (ramps, bare steps, a cascade, rapids) to try boats upstream and downstream. `/earth checkrivers` walks every river
   of the world and writes where the water is broken (with map coordinates) to `VintagestoryData/EarthMap/`.
   `/earth trees`, `/earth plants`, `/earth placement`, `/earth geology`, `/earth ores` and `/earth fauna` explain the regional data where
   you stand (botanical region and candidates, rocks and resource eligibility, ore evidence, animal ranges). `/earth census [radius]`
   counts the catalogue plants, other crops and berry bushes, fruit trees and animals generated in loaded chunks around you.
6. Testing a season: `/earth date 15 jul` (or `/earth date 3 1`, an optional year at the end) jumps forward to that real
   date; `/earth date` alone shows the date. Each month has the world's `daysPerMonth` game days (9 by default), so a
   real date lands on the game day covering it (15 July → July game day 5, real days 15–18). Time only runs forward,
   through vanilla's `/time add`, like `/time setmonth`: a date that has passed this year means next year's, and the
   time of day is kept. Going back is left out, since crops, animals, food and weather keep last-update times that
   would then lie in the future. Needs the `time` privilege. The creative panel (WorldEdit's settings, `~` in creative)
   has the same as Month and Day dropdowns under the time slider.

## Scale

The map is equirectangular: x is longitude, z is latitude, both linear (exactly Cylinder World's own latitude
layout, so the sun, day length and seasons match the real place). East–west distances are stretched toward the
poles (2× at 60°), north–south distances are true everywhere.

| World | Blocks per degree | Metres per block | London → Paris | Alps (Geneva → Vienna) |
|---|---|---|---|---|
| 256000 × 102400 + CW | 563 | ≈198 | ≈2 050 blocks | ≈5 900 blocks |
| 512000 × 204800 + CW | 1 132 | ≈98 | ≈4 100 blocks | ≈11 800 blocks |

Vertically, heights are mapped with two strongly logarithmic curves: lowlands and shallow seas are exaggerated
(the terrain gets more relief, the Gulf of Riga is a real sea for boats), high mountains are compressed (the Alps
reach ~85 % of Everest's height, the deep ocean and the Mariana Trench differ little):

| Real | 256-high world (sea 110) | 384-high world (sea 165) | 512-high world (sea 220) |
|---|---|---|---|
| −4 000 m (ocean floor) | 69 | 104 | 139 |
| −200 m (shelf edge) | 91 | 137 | 182 |
| −26 m (Gulf of Riga) | 102 | 154 | 206 |
| 0 m | 110 | 165 | 220 |
| 100 m | 123 | 185 | 247 |
| 500 m | 152 | 229 | 305 |
| 1 000 m | 171 | 256 | 342 |
| 2 000 m | 191 | 287 | 383 |
| 4 000 m (Alps) | 213 | 320 | 427 |
| 8 849 m (Everest) | 240 | 360 | 480 |

## What gets generated

- **Terrain**: vanilla's terrain pass (`GenTerra`) is replaced by one that builds each column from the data
  (bicubic from a 2′ grid) plus detail noise scaled by the local relief (ridged noise in rugged terrain).
  Regional rock strata are applied before soil and deposits. Caves, deposits, vegetation, structures
  and Cylinder World's seam and walls continue through their normal generation passes; regional ores adapts supported deposit generators.
- **Geology**: GLiM and GUM categories at 0.5° select broad rock types and surface-sediment context. Reviewed local
  profiles can refine them. Layer thicknesses are synthesized game-scale approximations, not mapped boreholes.
  Unknown underlying bedrock retains vanilla strata. Clay and peat are weighted (not vetoed) by substrate, water,
  wetness and relief in metres; halite and other resources use evidence heat plus a host-gated background; optional
  bog iron occurs in natural peat. Refractory-clay chemistry is not modelled. Rock contacts are displaced coherently
  within a configurable transition zone, with no chunk-coordinate reset. See
  [regional geology](docs/regional-geology-implementation.md) for the rules versions (new worlds get the current rock
  layers; older regional worlds keep theirs unless upgraded).
- **Ores**: each deposit type has a rate class. Soil resources (clay, peat) and quartz keep vanilla's rate where the
  ground suits them; starter surface copper/tin at least half of vanilla anywhere; progression ores (copper, tin,
  iron, coal) a no-evidence floor of about a third of vanilla's volume and 3–5× at evidence hotspots; other ores a
  similar floor and hotspots. Deposits search the generated column for a host rock (tin under thick sediments), and
  the prospecting pick reads Earth's rocks. Numbers and settings: [regional ores](docs/regional-ores-implementation.md).
- **Fauna**: supported optional animals are checked against range evidence and local land/freshwater/coastal
  habitat, both during generation and later natural spawning. Provider climate and population conditions still
  apply. Worlds placed as around 1318 use each animal's range of about 1318: the natural range without what it had
  lost by then or never reached after the Ice Age, never removing today's native range (no leopards or lions in
  Greece, no spotted hyenas outside Africa); domestic animals went feral where people kept them (chickens across the
  Old World and Polynesia, not the Americas). Vanilla land animals follow the same rule
  ([0054](docs/decisions/0054-animals-as-around-1318.md)). `FaunaRangeProfile` can instead pick natural ranges without
  human impact (PHYLACINE present-natural: lions in Greece, leopards in Latvia) or present-day ranges. Taxa without
  usable evidence are held from natural spawning, and coverage gaps are documented. Unrelated fauna keeps its rules.
- **Ground plants**: Floral Zones' ground-plant patches are limited to their species' native botanical regions
  (or the pack's floristic region where the species can't be identified); wild crops stay climate-only.
- **Sea**: salt water down to the real depth; the continental shelves are shallow, the abyss 50–70 blocks deep.
- **Islands**: islands under 1000 km² come from their real outlines (GSHHG full resolution), at their true size, and
  only where they are at least `MinIslandBlocks` (4) blocks wide in this world: with a 4 million block ring a 40 m
  cay is there, with 512 000 islands from about 300 m wide, with 204 800 from about 800 m. They rise from a beach to
  their real height, and the sea floor comes up to their shores. Where GSHHG traced an outline from a raster (straight
  runs of ~550 m with 45° cuts, 50-block staircases with a 4 million block ring) the corners are rounded off, as for
  the big land's coasts; worlds made with 0.14.0's island rules keep their staircases until `UpgradeExistingWorldIslands`.
  In the elevation grid they were ETOPO's 2' blobs (a
  tiny cay as a 15 km², 50 m hill), and ETOPO has false islands on reef rims (the Great Bahama Bank's southern rim and
  the Tongue of the Ocean, Chagos, the Spratlys): those are shallow reef now.
- **Coasts** (new worlds whose 2' grid cell is at least `CoastMinCellBlocks` (32) blocks wide: rings from about
  350 000): the big land's coasts follow its GSHHG outline instead of the grid's 3.7 km cells — headlands, coves, fjords
  and spits at their true size, cliffs on rugged coasts and beaches on flat ones, polders as land. Fjords the grid keeps
  open stay at least 4 blocks wide; ice coasts, lakes and the Antarctic keep the grid's. See docs/coast-outlines.md and
  [decision 0012](docs/decisions/0012-coast-outlines.md).
- **Lakes**: Natural Earth lakes at their real levels (Superior 180 m, Baikal 454 m, Titicaca 3 808 m, Geneva
  368 m, the Dead Sea −427 m); lake beds from ETOPO where it has them (Great Lakes, Baikal, Caspian), otherwise a
  depth profile. The Caspian is a salt lake 27 m below sea level; the Dead Sea, Great Salt Lake, Aral, Urmia and Van
  are salt too. Dry depressions (Qattara, Death Valley) stay dry below sea level. Land reclaimed from the sea is
  not: without their dikes, the Dutch polders and the IJsselmeer are the Zuiderzee again. Lakes within 3 m of sea level sit exactly at sea level, and lake basins that touch share one level.
  Coastal lakes whose dune strip is narrower than the data grid (Łebsko, Engure, Babīte, …) get a strip of land with a
  low dune between them and the sea, so fresh and salt water never touch.
- **Beaches**: land up to 3 m above the sea or a lake is flush with the water surface (as vanilla beaches are) and
  covered with sand of the local rock, or gravel on cold coasts (yearly mean below 0 °C).
- **Rivers**: the large rivers from HydroRIVERS (long-term discharge ≥ 150 m³/s: in Latvia only the Daugava; the
  Rhine, Seine, Loire, Elbe, Oder, Vistula, Danube, Dnieper, Volga, Nile, …), carved at their real courses with an
  exaggerated width so they are navigable at ~200 m per block: Daugava ~14 blocks, Rhine ~20, Danube ~28 (capped at
  48 for the Amazon), with a flat bed 4–9 blocks deep shelving to 2 blocks at the edges (the same all along the river),
  a bank strip flush with the water (sand or gravel at the water's edge) and a valley easing into the land. Water
  levels come from the valley floor in ETOPO, smoothed along the whole network and never rising downstream. The surface
  is a staircase of flat still-water pools, each at least 32 blocks long. **Drops follow the real slope** (new worlds,
  [decision 0024](docs/decisions/0024-hydrology-water-mod.md)): a drop is as high as the valley floor has fallen since
  its pool began, so a gentle river steps down 1 block at a time and a steep one by 2 blocks or more, with no cap.
  Below a 1-block step a **ramp of flowing water** (heights 6 → 1 over 6 blocks, VS's own spreading pattern) slopes
  down onto the lower pool, with the banks one block higher beside it. A drop of 2 blocks or more is **rapids**: a
  curtain of falling water with a ramp of **rapid water** below it, the banks beside the curtain as high as the water
  above it — the natural barriers (on the 400 biggest rivers
  mostly 2–3 blocks, up to 10 and more, e.g. the Congo's Livingstone rapids). Older worlds keep their layout: 1-block
  steps only, following every 6 blocks as a cascade of rapid water where the river falls fast. From 0.16.4 (new
  worlds, [decision 0024](docs/decisions/0024-hydrology-water-mod.md) 1b) a drop of 2–4 blocks is **rapids**: a chute
  of 1-block steps 3 blocks apart, all rapid water, its bed 1–2 blocks deep (too shallow for a caravel). A drop of 5
  or more and every listed waterfall is a curtain, with a shallow lip (2 blocks deep over the last 8 blocks) above it and
  a plunge pool below it, deeper by half the drop (up to 8). From 0.16.5 the bed eases into and out of chutes, lips and
  plunge pools over 8 blocks (0.16.4 had rock breaking the chutes' surface and steep underwater steps). From 0.16.6 a
  lake above a river beside it keeps a rim at its level and meets the river only at the river's drop (Niagara's lake data
  reaches past the falls, which made a curtain along the whole gorge in a 1M world), and no lake water stands above
  lower dry land. In new worlds with lake outlets ([decision 0055](docs/decisions/0055-lake-outlet-tongues.md)) a lake
  ends at its outlet: where Natural Earth's lake runs on down the river (Lake Erie over the upper Niagara to 3 km below
  the falls, the Pitt and Harrison rivers, 31 places) that stretch is land and the river runs through it. From 0.16.8 every drop has a sill (the upper water's edge column has its bed at the lower pool's
  surface) and its water blocks are drawn in the shape that stays as generated under Hydrology's rules: rapid sources
  at chute steps and down a curtain's lip, curtains of falling rapid water, a spill where a chute's ramp ends, ramps one
  column per height (0.16.9), rapids at 1-block steps only where Hydrology's rapids rule keeps them (0.16.10), and white water grouped into
  gorge sections of up to 6 steps 5 apart, 3 deep (0.16.11; rapids can then be passed by a caravel). From 0.16.13, when
  the installed Hydrology reads Earth's river flow query (0.1.8 or later), new worlds have steps without sills and whole
  rapids reaches of white water: reaches whose real valley falls 0.5 m/km or more, averaged over 100 blocks each way
  (decision 0027). Hydrology then fills no deep canals inside natural rivers, and water wheels in rivers turn faster
  on steeper, bigger rivers. These worlds also turn on Hydrology's deep-canal fill (`hydrologyFillVessels`; from 0.16.13
  `hydrologyFillVesselsWithRiverQuery`). From 0.16.14-rivers.1 (pool layout 9, with Hydrology 0.1.10) the steps come
  one block at a time where the valley falls, at least 7 blocks apart (closer only where the valley falls faster), so
  each ramp runs out at the next step: no vertical water faces and no chutes of steps 3 apart; a rapids reach's
  sources are labelled with the river's flow (they look like and push as running water), and a river beside a lake
  takes the lake's level by its centre line. From 1.0.3 (pool layout 11, with Hydrology 0.6.2) a reach is rapids only
  where the river has the power: valley fall × √discharge high enough (at least 0.3 m/km), so middle-sized rivers on
  gentle valleys run calm and large rivers in gorges keep their rapids. Even there, white water shows only at the
  drops: each step's edge and the ramp below it, with calm pools between. Every other river surface flows as ordinary
  water (slow, quiet), and the Earth tab's **River rapids** option (at the drops of steep rivers / off) can turn rapids
  off for a new world ([decision 0046](docs/decisions/0046-calm-rivers.md)). Vanilla's own **springs and streams**
  (single water sources in rock faces, and short streams, almost all of them rapids at Earth's sea level, breaking out
  of mountain sides at random) are set by the tab's **Mountain springs and streams** option: new worlds from 1.0.3-tall.24
  keep only the springs in cave walls (no open cliff-face springs, no mountain-side streams); "Real stream heads" adds a
  spring where each real stream that runs all year starts (the heads of HydroRIVERS' streams, without the intermittent
  ones of dry country), moved onto the valley floor nearby, rapid water where the ground down-valley is steep for its
  flow, and small seeps where the water table meets a slope in wet climates, none in deserts; "vanilla" keeps vanilla's
  ([decision 0063](docs/decisions/0063-rivers-follow-the-real-channel.md) rule 5). The water runs by vanilla's rules:
  at 1:10 most springs spread out about 60 m on the valley floor, at 1:100 mountain springs run a kilometre or more.
  **Rivers on the real channel** (new worlds on full GLO-90 from 1.0.3-tall.24, [decision
  0063](docs/decisions/0063-rivers-follow-the-real-channel.md)): every river from 20 m³/s runs along the river water of
  the satellite water mask, smoothed off its pixels; the mask's rivers are drawn as one smooth water with the course's
  channel (no pools on necks, no land strips, no rings of silt in the middle); the channel is never cut by the mask, is
  narrower and deeper where the real river is narrower than usual for its flow (down to half, keeping its
  cross-section), takes its depth and flow from every river near it at confluences, and is never dried by the arid rules
  where the mask has water. Removed dams leave the river's full width across the wall. Canals stay out: no water along
  the Suez and Panama canals without their options (the Bitter Lakes are dry), and narrow canals that only leave a river
  (the irrigation canals below Kakhovka) are not drawn at fine scales. From 1.0.3-tall.25 the rivers' courses are
  smoothed into bends at scales finer than about 1:50, without leaving the real river's water, so no straight legs
  with 45° corners.
  Vanilla's **surface ponds** (round pools it digs into any hollow, hillsides and river beds too) are set by the tab's
  **Surface ponds** option: off by default in new worlds from 1.0.3-tall.25, or only in the mapped wetlands, or
  vanilla's; ponds in caves stay ([decision 0062](docs/decisions/0062-ponds-in-wetlands.md)).
  **Waterfalls** (new worlds from 0.13.3):
  Victoria, Niagara, Iguazu, Kaieteur, the Rhine Falls, Khone Phapheng, Murchison, Dettifoss, Gullfoss, Shoshone,
  Ruacana, Augrabies, Hukou and Kalandula drop their river at once, as a curtain of falling water as tall as a cliff of
  that height by the sea (384-high world: Victoria 22 blocks, Niagara 11, Kaieteur 38), and the river below runs in
  a gorge until its valley comes down to it. A fall never takes its river below a lake, the sea or a bigger river
  close below it (Murchison above Lake Albert, Iguazu above the Paraná are lower for that). **River exits to the sea**
  (new worlds from 0.13.3): each river that ends at the sea gets a channel from its end through the natural water
  (estuary, bay, strait) out to open water at least as deep, found on the terrain grid preferring the deeper water.
  It is dredged to 6 blocks for the navigable rivers (a carrack draws 4.5; streams get their own depth), over the
  river's width and then widening 30° to each side out to sea, so the way in is a broad opening; deeper natural water
  stays. Low bars and spits on the way are cut over the river's width only; river shores, headlands and cliffs stay.
  A narrow natural entrance (the Tagus narrows at Lisbon) is dredged over its whole water, but not widened. From 0.14.1
  (channel rules 2) a channel is as deep as its river at its end (at least 6 blocks; the Tagus 7) and goes on to open
  water, past the bars outside an estuary's deep narrows (the bar off Lisbon was 1–5 blocks deep). From 2.0.0 (sea
  channel rules 5, decision 0071) worlds with outline coasts (1:100 and finer) get no exit channels: the coast
  outlines and the water mask draw the real way out, and the river's bed meets the sea's as one bed (channel rules 3:
  the estuary at the river's depth, the river's trough easing into the sea floor at half a block per block). Rivers arrive
  at the exact level of the lakes and seas they flow into, and tributaries at the level of the main river.
  **River ends** (new worlds from 0.14.1): a river continues downstream to its real end. One that loses water in dry
  land (the Darling, the Rio Grande) narrows instead of stopping where its discharge drops below the navigable size, and
  where it grows again (the upper Niger, the Zambezi) it is one river, without a gap. Rivers without an outlet end in
  their terminal lake (`lakes.bin`). The lakes that irrigation and dams dried up in the 20th century are back at their
  level before, since the rivers carry HydroRIVERS' natural discharge (without the withdrawals): the Aral Sea at 53 m
  (65 600 km²), Lop Nur, the Hamun lakes of Sistan, Tashk and Bakhtegan, Gavkhouni, Manas Lake, Lake Chad as in 1963,
  Owens and Tulare lakes, the Dead Sea of the 1960s. Like the rivers' green belts, their shores in dry land are green
  (reed marsh, meadow, the deltas' woods) and ease out through steppe into the desert: 40 km around the Aral, 25 km
  around Lake Chad, less around the smaller ones, none around brines and salt pans. Salt lakes that fill in wet years
  are filled: Kati Thanda–Lake Eyre, Paraku (Lake Gregory), Salar de Coipasa, Makgadikgadi, Namak Lake, Salinas
  Grandes, Etosha Pan. A river that
  stops short of water (HydroRIVERS ends at the head of a fjord or an estuary the grid has as land: the Namsen, the
  Ångermanälven, the Derwent, the Hawkesbury) is led on to it, and wadis that end in the sand (Wadi ar-Rummah)
  narrow to a trickle over their last stretch instead of ending at full width.
  **No modern dams**: reservoirs are left out (Natural Earth "lakes" that Wikidata lists as reservoirs, or with a
  HydroLAKES dam at their outlet, e.g. Pļaviņas on the Daugava, the US Army Corps lakes) and the river levels through
  them are replaced by the natural slope (Three Gorges, Lake Nasser, Kariba, the Volga and Dnieper cascades); natural
  lakes that are only regulated by a dam (Mjøsa, Inari, Cayuga Lake) stay. In dry climates a green belt follows the
  river (the Nile valley has soil and grass, scattered acacias, and steppe at its edge, then the desert).
- **Ice sheets**: Greenland and Antarctica get glacier ice from the bedrock up to the real ice surface
  (ETOPO surface − bed), e.g. ~70 blocks thick on central Greenland in a 256-high world.
- **Climate**: the climate map gets the real yearly mean temperature and a rainfall value from the real aridity
  (precipitation / potential evaporation), stored so that vanilla's own altitude rules give back the real values at
  the surface. The rainfall scale is calibrated to VS's tree ranges: temperate Europe ≈115–140 (Scots pine, oak,
  birch, maple), the humid tropics ≈190–210 (kapok, fern trees, bamboo), savannas ≈70–100 (acacia), deserts below
  30. Vanilla derives fertility from them; in new worlds land that isn't arid by the real aridity index (0.2 or more)
  gets at least low soil instead of vanilla's sand or gravel (decision 0048), so the Mediterranean, the steppes and the
  dry savannas have soil and deserts keep their sand. Shrubs follow the same rainfall (none on desert sand). Live temperatures are also extended beyond vanilla's −20..40 °C where the
  real climate is that extreme (−50 °C on the Antarctic plateau, with reversed seasons in the south via Cylinder
  World's latitude).
- **Seasons**: vanilla swings temperatures by 65 °C × latitude between winter and summer everywhere. Earth
  replaces that with each place's real swing between its coldest and warmest month, so continental, maritime and
  Gulf Stream climates come straight from the data: London about 4 °C in January / 20 °C in July, southern Italy
  9 / 29 °C, Finland −10 / 17 °C, Yakutsk −38 / 21 °C (measured in game).
- **Mediterranean**: climates with dry summers (from the monthly precipitation) get a lower rainfall value, so they
  are sparser and drier: southern Italy, Greece, Spain, California, central Chile.
- **Forests** follow the real climate: vanilla's forest noise is scaled by a forest potential (warmest month
  above ~10 °C = below the polar and alpine tree line, and moist enough), so the Amazon and the taiga are wooded
  and the steppe, deserts and tundra are open.
- **Forest types**: the species mix follows the real forest type (from the coldest and warmest month, summer
  drought and elevation) instead of vanilla's climate ranges alone: boreal (Finland: fir and birch, some pine),
  hemiboreal (Baltics: pine, fir and birch), temperate broadleaf (central and western Europe: oak and maple — VS has
  no beech — walnut, some birch and conifers), Mediterranean (mostly oak), montane (fir, mountain pine, larch).
  Tropical and desert species are picked as in vanilla. Swamp trees don't grow in the sea.
- **Ruins**: vanilla's surface ruins skip places where the Earth terrain is water (vanilla would put them on shallow
  sea floors down to 20 blocks below sea level). Underground structures are kept away from the beds of
  water bodies; from 0.16.7 that check runs only for attempts that pass vanilla's own checks (the same structures are
  placed, at a fraction of the cost).
- **Bare rock**: steep slopes, moderately steep slopes above the tree line, and everything where even the warmest
  month is near freezing lose their soil (vanilla would grow grass on the summits).
- **Spawn**: by the world's starting climate (temperate: Upper Rhine valley 48.6°N 7.8°E; warm: Andalusia;
  hot: the Sahel on the Niger; cool: the White Sea coast; icy: Taymyr), or any `lat,lon` in the config;
  the nearest gentle dry land is used.
- **Story locations**: vanilla picks them at random offsets from spawn. Earth keeps those distances, anchors
  the chain at the new spawn, tries the four mirror images of each offset and moves each location to the best
  suitable real land nearby, never closer than 75 % of vanilla's distance to spawn or to its parent location: flat, dry, ice-free lowland for the surface ones (the site is flattened, since vanilla's
  forced "veryflat" landform doesn't apply to real terrain), dry land a few blocks above sea level for the
  underground Resonance Archive. With the default seed at 256000 × 102400 they end up in southern Norway, Hungary,
  near Kyiv, Karelia, by the Sea of Azov and in Lorraine. On bigger worlds (smaller blocks) the same distances keep
  them within France and Germany. Lower the world's "story structures distance scaling" for a tighter chain.

## With Cylinder World

- The Earth is placed on Cylinder World's ring (between the jump lines) with Cylinder World's latitudes.
- **Seam**: the Earth is cut along the straight meridian 169.25°W ([decision 0019](docs/decisions/0019-straight-seam.md)),
  so every meridian is a straight north-south line in the world. It runs through the Bering Strait between Big Diomede
  and Cape Dezhnev, east of the Pribilofs and the Aleutians' Islands of Four Mountains, through open Pacific to the
  Ross Sea; the only land it cuts is St. Lawrence Island and the Antarctic plateau. A strip of open sea (the "gutter",
  2 × (Cylinder World's seam band + 512) = 2 240 blocks by default) is inserted around the jump line, so crossing the
  Bering Strait is the gutter plus about half a degree of sea, with the jump line in the middle of Cylinder World's fog.
  Worlds made before 0.15.3 are not kept compatible: their new chunks near the seam no longer meet the old ones.
  In new worlds from 0.14.7 (decision 0015) the gutter's floor is the
  seam's real seabed carried across it, and Cylinder World 0.11.6+ keeps it in its seam band; older worlds fade to a flat
  floor that meets Cylinder World's seabed.
- **Antarctica across the gutter** (new worlds from 0.13.2): south of 60°S, where the Earth is land or ice on both sides
  of the gutter, the gutter carries it across instead of cutting a strait through it. The seam crosses the Ross Ice
  Shelf and the polar plateau, so the bridge runs from the shelf front (about 78.5°S) to the south Wall. Both gutter
  edges are the same Earth point, so the bridge blends the two edge columns, with detail of its own in the middle,
  and depends only on the position across the gutter: the two sides of the jump line match block for block at the
  surface. Cylinder World (0.11.3+) keeps it instead of flattening its seam band there. Older worlds keep the strait
  unless `UpgradeExistingWorldGutterBridges` is set once.
- Earth wraps Cylinder World's climate layer (temperature and rainfall come from the Earth, geologic activity
  stays vanilla's) and turns off Cylinder World's own temperature extension at runtime (it would add its latitude
  curve on top of the real climate). Cylinder World's winter rule on the ice sheet still works with the real
  temperatures (the Antarctic plateau is −46 °C yearly, −60 °C in the southern winter).
- Spawn and the story locations are placed after Cylinder World moved them.
- Cylinder World 0.11.11 or newer is a required dependency (decision 0023);
  its geometry is still read by reflection, so Earth doesn't compile against it.

Known interplay issues, best fixed in Cylinder World (suggestions for its next version):
1. **Ice sheet height**: Cylinder World's ice sheet from 85° is a fixed plateau (70 blocks above sea). The real
   Antarctic plateau at 85°S maps higher (≈80 blocks in a 256 world, ≈120 in a 384 world), so it's planed down
   there, leaving a drop of up to ~110 blocks at the sheet's edge in a 384-high world. North of 85°N is the Arctic
   Ocean, where Cylinder World builds its ice sheet on a raised sea floor.
   Suggestion: let another mod provide the ice sheet's surface height (or skip the sheet where the Earth has sea ice).
2. **Hooks instead of reflection**: an interface in Cylinder World (e.g. `ISurfaceProvider` with base temperature,
   rainfall and "true" temperature at a position, plus an event before spawn and story locations are moved) would let
   Earth plug in cleanly and let Cylinder World's `/cyl map` and temperature code use the Earth's values.
3. `/cyl map` shows vanilla's ocean map, not the Earth; use `/earth map`.

## Without Cylinder World

Not supported ([decision 0023](docs/decisions/0023-world-creation-tab-and-cylinder-world.md)). A new world needs
Cylinder World active with its planet layout: `"Enabled"`, `"PolarWalls"` and `"PolarClimate"` true in
`ModConfig/cylinderworld.json` (the defaults), and a map wide and long enough that it keeps its seam and walls.
Otherwise the log has an error saying what is missing, and the world keeps vanilla terrain (create a new one after
fixing the config). A world made by an older Earth (then Earth Map) without Cylinder World, or one whose Cylinder World has been
switched off, loads with vanilla generation and an error in the log; its save is left unchanged.

## With Terrain Slabs

[Terrain Slabs](https://mods.vintagestory.at/terrainslabs) (with its required
[Place On Slabs](https://mods.vintagestory.at/show/mod/38090)) works on Earth worlds without any Earth code:
- **What it does:** it turns the top block of exposed terrain edges into half slabs, so 1-block steps can be walked
  without jumping. Earth's heights and slopes stay the same.
- **Shores:** its "beach" step also puts a half slab into the first water cell along every shore, which makes the rim of
  rivers, lakes and coasts half a block shallower.
- **Bog peat:** Earth's bog peat has no slab version and stays a full block.

It is an optional companion, not a dependency. Removing it from a world needs its own revert command first. The
server-side test (Earth 0.15.6, Terrain Slabs 1.0.17, game 1.22.7) found no problems; client rendering and water
behaviour at the shore slabs are not checked yet. See
[docs/research/terrain-slabs-test-2026-10-22.md](docs/research/terrain-slabs-test-2026-10-22.md).

## Day and night by longitude (with AstraTerra)

Latitude is Earth's (Cylinder World's latitudes), so the sun's height, day length, the seasons in both hemispheres and polar
day and night are already right. Vanilla has one clock for the whole world, though: the game's time is best read as
UTC, and on its own the sun stands at the same hour everywhere.

From 0.14.2, Earth offers the longitude of every X to other mods (`EarthMapSystem.LongitudeAt(x)`, both sides, null
when Earth isn't active) and hands it to **AstraTerra** when that is installed and can take it
(`SetLongitudeProvider`, by reflection; Earth doesn't reference AstraTerra). With such an AstraTerra and its
`LongitudeAwareSun: true` (its default), AstraTerra's sun, daylight, local moon, stars and clock follow the real
meridians: noon in Lisbon comes about 3 game hours after noon in Moscow. For a UTC or zone clock, set
AstraTerra's `DisplayedClockTime` to `universal` or `zones`.

- The sky uses the meridian of each X column, which is the map's longitude at every latitude (the seam is straight
  from 0.15.3). The seam gutter has the seam's meridian, so the sun doesn't jump at the jump line.
- **Needs an AstraTerra with `SetLongitudeProvider`.** The released AstraTerra 0.15.0 doesn't have it: Earth then logs
  "has no SetLongitudeProvider" once and AstraTerra keeps its own longitude (prime meridian at the map's centre).
  Without AstraTerra, Earth logs that once and nothing changes.
- Nothing is saved; worlds of any Earth version get it. Not yet checked in game.
- **Known limitations, deferred** ([decision 0010](docs/decisions/0010-real-day-and-night.md)): without such an
  AstraTerra there is no longitude sun, so everyone sees sunrise at the same moment; and even with it, animal AI hours
  and NPC schedules follow the global clock. Making it work without AstraTerra needs a fallback sun/moon and patched
  creature schedules (phases 3 and 5 there).

## Weather

From 0.15.0 Earth makes the weather of Earth worlds ([decision 0017](docs/decisions/0017-weather.md)); vanilla's rain,
clouds and winds follow it. It is a pure function of the world's seed, layout and game time, so the server and every
client compute the same weather without anything being saved or sent, a late joiner sees the same sky, and switching it
off (`Weather: false`) gives vanilla's weather back.

- **Rain** happens as often as it really does at that place in that month (the month's precipitation from the
  climate normals): Mumbai rains about 43 % of July and almost never in January, Rome's summer is dry, the Sahara
  hardly ever sees rain, London gets rain in every season. Fronts and rain areas hundreds of kilometres wide and
  showers a few hundred blocks wide drift with the steering winds; rain clouds gather before the rain starts; showers
  over land peak in the afternoon. Snow where it is cold enough (vanilla's rule).
- **Clouds**: each weather region's cloud pattern is picked by vanilla's chances weighted by Earth's cloud cover there
  (clear desert skies, overcast at fronts and in the wet season).
- **Winds**: the month's prevailing winds from the NCEP/NCAR reanalysis' monthly means: trade winds, the monsoons
  (India and West Africa from the south-west in summer, East Asia's cold northerlies in winter), the westerlies and
  roaring forties, the winds off the ice sheets; day-to-day drift and gusts on top. Trees, rain slant (streaks
  drawn along the drops' paths), cloud drift and wind sound follow them. **Sea** (0.4 and later) takes Earth's wind as its wind provider, so waves and ships
  follow the same weather; an older Sea keeps its own wind and Earth then does rain and clouds only.
- **Storms**: about 85 tropical cyclones and 1000 mid-latitude lows a year form where and when they do on the Earth
  (no hurricanes in the South Atlantic, Atlantic hurricanes in summer and autumn) and move along real tracks; their
  winds turn around the centre, strongest at the eyewall, with spiral rain bands and low pressure.
- **Scale**: fronts and storms move at their real speed over the map's real kilometres, so at the standard 198 m per
  block a rainy spell lasts hours and a front takes about half a day to pass; showers and gusts are sized for the
  player. The game's year is short (108 days), so a season has fewer fronts than a real one; `WeatherPace` trades that
  against how fast the weather at one place changes.
- `/earthweather` (`/ew`): the month's climate and the weather here; `/ew forecast` the next 24 hours; `/ew storms` the
  nearest storms.
- **For other mods** (the open sea contracts' `weather` role, [decision 0017](docs/decisions/0017-weather.md#open-sea-contracts-the-weather-role-7-november-2026)):
  on an Earth world `api.ObjectCache["seacontracts:weather"]` holds Earth's wind and prevailing wind (m/s, toward, +X east,
  +Z south), storms (presence 0-1, their wind, `hurricane` for a tropical cyclone, `gale` for a low) and rain (vanilla's
  0-1 cloud overlay and rainfall) as plain .NET delegates, at any place and game hour, the same on every side. Vanilla's
  wind lookups give the wind as a vector (X east, Z south, the length its strength).
- **Thunder, hail and cloud decks** (weather rules 6 in new worlds, [decision 0017](docs/decisions/0017-weather.md)):
  thunderstorms follow Earth's storms, hail falls from the strongest of them, low cloud decks lie over the cold seas
  off Peru and Namibia, and the weather is part of the temperature. The weather is the same on every operating system.
  `/earthweather hail` says when hail next falls where you stand.
- **Ground, fog and clouds** (weather rules 7 in new worlds, [decision 0017](docs/decisions/0017-weather.md)): high
  ground rains and snows on as many days as its stations (the rain rate reads the ground's temperature); the desert
  coasts get no rain (climate fix 4); the desert Southwest has its summer thunderstorms; fog and mist come from Earth's
  weather (sea fog on the cold coasts, valley fog on clear calm nights after rain) and the clouds are drawn as thick as
  Earth's cloud cover, so their edges follow its rain areas. `/earthweather` says the fog.
- **Showers and downpours** (weather rules 8 in new worlds, [decision 0017](docs/decisions/0017-weather.md)): rain comes
  in showers of a few hours through the whole day, as the airports report it, and warm months' rain as a few downpours
  and much light rain, so rainy days come as often as at real stations; the month's rain is as before. Hail hurts.
- **Africa's thunder** (weather rules 9 in new worlds, [decision 0017](docs/decisions/0017-weather.md)): West and Central
  Africa thunder on as many days as their airports report, a third more than before, without more rain.

## The Earth tab — world creation screen

Choose an **Earth** play style, then Customize → **Earth** ([decision 0036](docs/decisions/0036-earth-play-style-and-world-options.md)).
There is one per scale: Earth · 1:200 (`earth-200`), Earth · 1:100 (`earth`), Earth · 1:50 (`earth-50`), Earth · 1:25
(`earth-25`) and Earth · 1:10 (`earth-10`). Each is Standard survival's exact 1.22.7 preset plus Earth's
defaults, with the Earth size, world height and mountain heights for its scale (384 blocks and balanced relief at 1:200
to 1:50, 512 and linear at 1:25, 1024 and linear at 1:10). The list places them right after Standard.

New worlds made with any other play style use vanilla generation with Earth installed. Earth leaves their size,
sea level, terrain, climate, weather, trees, plants, animals, ores, story locations and spawn alone. Other installed
mods still apply their own rules. A world without saved Earth parameters is classified from `SaveGame.IsNew`,
`WorldType` and `PlayStyle` at its first start, never from the presence of Earth tab keys (the game supplies those to
all styles). Earth writes a disabled record for an inactive world so a later restart cannot convert it.
**Existing saves with enabled EarthParams remain Earth worlds whatever their play style.** Existing vanilla saves
remain vanilla, even if their style is changed to Earth.

The game offers no declarative style-dependent disabling of a tab or option. Earth's descriptions therefore say
**“Used by the Earth play style.”** Other suite mods' entries in the same tab keep their own meanings.
All Earth choices below are creation-only and saved in `earth-params`; subsequent config edits do not change them.
Dedicated servers set `WorldConfig.PlayStyle` to one of the codes above and put options that differ from it under
`WorldConfig.WorldConfiguration`.

| Option | Key | Default | Meaning |
|---|---|---|---|
| Earth size | `earthWorldSize` | 100 (the play style's) | the scale: `200` (1:200), `100` (1:100), `50` (1:50), `25` (1:25), `10` (1:10) or `5` (1:5). The length is the Equator over twice the scale (100 352 to 4 007 424 blocks), the width twice the length (Cylinder World's ring) plus 16 384 blocks of hidden seam ocean beyond each end (decision 0068; a width given in blocks, as an older world configuration has it, is still read: the same length, this width). `vanilla` keeps World generation tab dimensions (at least twice as wide as long, such as 1 024 000 × 512 000, or the Earth is stretched north-south) |
| World height | `earthWorldHeight` | 384 (the play style's) | 256 to 1024 blocks in steps of 64 (a server config may give any multiple of 32); replaces World Height at the top of the screen. `0` keeps that slider's height |
| Start | `earthSpawn` | random | **Earth · Start**, on the game's **Player spawn and death** tab with Start coordinates, its last two options, below Starting climate (the game's text field turns clipping off for everything drawn after it in a list, so on the scrolling Earth tab the typed coordinates and the next options would draw outside the list; that tab doesn't scroll; from 2.0.3, [decision 0085](docs/decisions/0085-start-on-the-spawn-tab.md)). Regional random dry, gentle lowland at the chosen starting climate; 36 capitals; or `custom`. Random gives each qualifying major region a fair chance, permits coasts and avoids tiny islands ([decision 0037](docs/decisions/0037-varied-random-starts.md)). The same seed and settings give the same place. Capitals and coordinates find nearby dry land at any height |
| Start coordinates | `earthSpawnCoordinates` | (empty) | for `custom`: pasted from Google Maps (the decimal pair a clicked place shows, `56.949600, 24.105200`; degrees, minutes and seconds, `56°56'58.6"N 24°06'18.7"E`; or a Maps link, whose place pin or `@latitude,longitude` is read), or `latitude, longitude` in degrees, south and west negative. Unreadable: a random place, and the host or the server's admins are told why in chat when they join while the world is made (from 2.0.1, [decision 0084](docs/decisions/0084-world-screen-2-0-1.md)) |
| Suez and Panama canals | `earthCanals` | off | both navigable sea-level cuts, including their lakes; Panama has no locks. Off: `earthSuezCanal` and `earthPanamaCanal`, the switches before 2.0.1 (off the screen, off by default), each on its own: a server's world configuration can still name them, and a world made before keeps its own |
| Sea level | `earthVerticalBudget` | 110 | `110`, `128` or `160` blocks below sea (at most half the world height), independent of world height |
| GLO-90 land heights | `earthGlo90Download` | release | `release` or `copernicus`: every GLO-90 square is downloaded when the new world first opens (Earth's data release, 8.6 GB, or Copernicus's originals, about 71 GB converted here), squares already on the machine skipped; `needed`: each square as the world reaches it. A world without the key downloads as needed |
| Mountain heights | `earthHeightCurve` | log1000 | `log1000`, `log250`, `log500`, `log2000` or `linear`. Smaller log values exaggerate lowlands and compress high mountains; linear preserves proportions |
| Summit height correction | `earthSummitCorrection` | 3000 | `off` or minimum surveyed elevations `1000` through `8000` in 1,000 m steps. Corrects listed peaks at or above that real elevation; excluded peaks do not narrow qualifying peaks' correction footprints. The finer terrain-detail layer stays active. Saved at creation; older saves keep all previously eligible peaks |
| Land heights | `earthElevation` | auto | not on the tab ([decisions 0051, 0053](docs/decisions/0051-glo90-elevation-on-demand.md)): `auto` (as `glo90`) takes dry land's heights from Copernicus GLO-90 (3″, about 90 m), per 1° square as chunks need them, kept in `VintagestoryData/ModData/earth/glo90` for every world on the machine, with the included coarser GLO-90 grid where a square can't be had; rivers, lakes and dams follow GLO-90 too. `glo90coarse`: the included grid; `builtin` (and any other value): the older 2′ heights. Listed peaks at or above the summit cutoff get a narrow lift at GLO-90's own top to their surveyed height (never lowered). `.earthelevation` opens the download dialog |
| Rock floor | `earthRockFloor` | auto | how far below the lowest ground of each 32 × 32 column the ground turns to plain rock (no caves, ores or lava; mantle only in the bottom layer): `off`, `96`, `128`, `160`, `192` or `256` blocks; `auto` is 160 in worlds taller than 512, none otherwise |
| Rocks and ground | `earthRocks` | vanilla | `vanilla` (the default from 2.0.1, [decision 0084](docs/decisions/0084-world-screen-2-0-1.md)): the game's rock layers, and Hydrate or Diedrate keeps its own aquifers ([decision 0059](docs/decisions/0059-water-table-and-hod-aquifers.md)); `groundcolumns`: regional rock regions and ground built from real data (soil, sediments in metres, sea-floor sediment; bedrock layers in the Baltic); the first such world on a machine fetches the 30″ ground data (about 172 MB, [earth-ground](https://github.com/Temporal-Poets-Society/earth-ground)); `regional`: rock regions only (`RegionalGeology`) |
| Ground depth | `earthGroundDepth` | log4000 | ground columns only: how real depth becomes blocks (`log4000`, `log2000`, `terrain`, `linear2000`) |
| Ores | `earthOreDeposits` | vanilla | `vanilla`: the game's ore deposits, plus Earth's gems (beryl, corundum, garnet, spinel, topaz, tourmaline) and added ores (`earth-gems.json`, `earth-ores.json`, by the game's deposit rules; the author, 15 November 2026: keep them); `maps`: Earth's ore maps (`RegionalOres`, experimental, being reworked). Worlds set up with the earlier single key `earthOres` keep its meaning: `vanilla` is vanilla rocks and ores, `regional` and `groundcolumns` are that rock choice with the ore maps |
| Plants and animals | `earthNature` | 1318 | `1318`: plants and animals where they lived around 1318 ([decisions 0049, 0047](docs/decisions/0047-placement-catalogue-and-audit.md)): fruit trees, wild crops and berry bushes by their 1318 range and Earth climate, regional trees, real animal ranges. `vanilla`: placement by climate alone (no regional trees, plant gates or animal ranges). Worlds made before this option read the old `earthAnimals` key; older saves keep vanilla fruit, crop and berry placement |
| Biomes | `earthBiomes` | earth | `earth`: Earth's biome rules (vegetation zones from the real climate, wetlands, fire openings, ground cover, ground moisture, winter grass, alpine surfaces); `vanilla`: the game's own forest and shrub maps (fed by Earth's climate) and tall grass ([decision 0075](docs/decisions/0075-biomes-back-to-vanilla.md)). Terrain, rivers, coasts, rocks and ores stay Earth's; works with either Plants and animals choice |
| River rapids | `earthRiverRapids` | steep | `steep`: white water at the drops of rivers whose real fall is strong for their size ([decision 0046](docs/decisions/0046-calm-rivers.md)); `off`: no rapids reaches, waterfalls stay |
| Mountain springs and streams | `earthMountainSprings` | caves | `caves`: vanilla's springs in cave walls only, none in open cliff faces and no mountain-side streams; `streams`: as `caves`, plus a spring at the head of each perennial stream (HydroRIVERS) and seeps where the ground water meets a slope in wet climates; `vanilla`: all of them as the game makes them |
| Surface ponds | `earthSurfacePonds` | off | `off`: none of vanilla's surface ponds; `wetlands`: only in the mapped bogs, marshes and swamps (GLWD v2), by their share of the ground; `vanilla`: wherever the game digs them. Ponds in caves are the game's in every case |
| Include extinct animals | `earthIncludeExtinctAnimals` | off | used with 1318; also spawns species that died out before 1318 (mammoths, cave lions) from supported animal mods. Animals still alive in 1318 (aurochs, moa) spawn without it |
| Chickens everywhere | `earthChickensEverywhere` | on | used with 1318 ([decision 0054](docs/decisions/0054-animals-as-around-1318.md)): on lets wild chickens spawn wherever the climate suits them (every region but Antarctica); off keeps them where people kept them around 1318. With `vanilla` nature every animal spawns by climate alone, so this changes nothing there |
| Weather | `earthWeather` | earth | real-climate rain, storms, clouds and wind, or vanilla weather |
| Rain frequency | `earthRainFrequency` | (empty) | not on the world screen from 2.0.1 (an admin's setting, [decision 0084](docs/decisions/0084-world-screen-2-0-1.md)): Earth weather only, 0 to 3 times the real monthly rain chance, as a string in a world configuration (`"earthRainFrequency": "2"`; a bare number is dropped by the game); empty takes earth.json's `RainFrequency` (1) |
| Storm frequency | `earthStormFrequency` | (empty) | not on the world screen from 2.0.1: Earth weather only, 0 to 5 times the real storm frequency, as a string (`"0.5"`); empty takes earth.json's `StormFrequency` (1) |

Full terrain detail and real-altitude mountain ecology are the defaults. Legacy sea/height curves and test detail,
ecology and rock-exposure switches have left the menu; old saves retain their stored values and behaviour. Default
alpine cover already uses physical slopes; the removed `physical` test bypassed those alpine rules.

The Earth tab's World height replaces the game's own slider (which stops at 512); each Earth play style sets the height
for its scale.
Older saves without the new creation-options marker keep their former config-on-restart weather and extinct-animal
behaviour. Terrain versions and explicit one-shot upgrades still follow [decision 0002](docs/decisions/0002-save-compatibility.md).

Other mods own these entries in category `earth`; Earth does not declare or read their new options:

| Owner | Entries / keys |
|---|---|
| Sea | Storm wave height (`seaStormHeight`), wakes (`seaWakes`) |
| Cylinder World | Seam mist (`cylinderSeamMist`) |
| Hydrology | Water flooding rules (`hydrologyFlooding`) |
| Wmap | Shared chart (`wmapSharedChart`), exploring by sight (`wmapExploreBySight`) |

Earth's older Hydrology compatibility handshake (`hydrologyFillVessels*`, decisions 0024/0027) remains confined to
active Earth worlds; it is separate from Hydrology's new user-facing flooding option.

Offline checks: `powershell -File tools/run-earth-playstyle-tests.ps1` (requires the supplied `.local/vs` references and real `earthdata`). Add `-MeasureBaseline -FilterSurvey` to reproduce the full before/after measurement and filter survey. CSVs and logs go to `tests/out/earth-playstyle/`.

## Config — `VintagestoryData/ModConfig/earth.json`

Everything that shapes terrain is frozen into the savegame at world creation (`earth-params`). Config files from
0.1/0.2 are migrated to the 0.3 height curves automatically (`ConfigVersion`).

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | true | allow new worlds in the Earth play style to generate Earth; existing saved Earth worlds stay active |
| `RegionalTrees` | true | native botanical regions for new worlds; frozen in the save |
| `RegionalPlants` | true | native ranges for regional ground plants (Floral Zones) in worlds with regional trees; applied at every start |
| `RegionalGeology` | true | regional rocks and surface-resource eligibility for new worlds; frozen in the save |
| `GeologyTransitionBlocks` | 96 | half-width of the rock-contact transition zone (0..512); frozen in the save |
| `RegionalOres` | true | Earth-like ores (rules v3: deposits where the evidence is, coal measures under the coal basins, copper near every start); false: vanilla ore generation on Earth's rocks (clay, peat and salt still follow `RegionalGeology`). Both follow the world's ore settings. Frozen in the save |
| `Ores` | see [ores](docs/regional-ores-implementation.md) | ore tuning (background/hotspot chance, frequency, size, depth, the weights of the background and real-data maps, ore-body ramps and richness, per-resource overrides); frozen in the save; invalid values fall back to the defaults with an error in the log |
| `RegionalFauna` | true | range checks for supported optional fauna in new worlds; frozen in the save |
| `FaunaRangeProfile` | natural | `natural` (ranges without human impact) or `current` (present-day ranges where known); frozen in each world from 0.17.0-pre.12 on, applied on restart in older saves |
| `IncludeExtinctAnimals` | false | creation fallback; worlds from 0.17.0-pre.12 on save their choice, older saves apply this on restart; existing animals remain |
| `Islands` | true | small islands from their outlines instead of the grid's blobs, without ETOPO's false reef islands, in new worlds; frozen in the save |
| `MinIslandBlocks` | 4 | islands narrower than this many blocks are left out (1..64), so how many show follows the world's size; frozen in the save |
| `UpgradeExistingWorldIslands` | false | one-shot: the next Earth world started takes `Islands`, the current island rules (rounded corners, estuary islands not cut by rivers) and `MinIslandBlocks` for chunks generated from then on; chunks already generated keep their coasts and may meet the new ones with a step near small islands and reefs. The flag then resets itself |
| `Coasts` | true | the big land's coasts from their outlines in new worlds whose 2' grid cells are at least `CoastMinCellBlocks` wide (needs `Islands`); frozen in the save |
| `CoastMinCellBlocks` | 32 | outline coasts only where a 2' grid cell is at least this many blocks wide (8..1000; 32: rings from about 345 600) |
| `UpgradeExistingWorldRiverDeltas` | false | one-shot: the next Earth world started gets the delta branches of 0.14.7 (the Mekong's Tiền and Bassac share its water) in chunks generated from then on, if it has the river ends; chunks already generated keep their rivers and may meet the new widths with a step. The flag then resets itself |
| `UpgradeExistingWorldRiverWidths` | false | one-shot: the next Earth world started gets the river widths of 0.14.7 (the standard world's widths in blocks, real widths where wider) in chunks generated from then on; worlds up to the standard 204 800 ring are unchanged, in bigger ones rivers become narrower and may meet chunks already generated with a step. The flag then resets itself |
| `UpgradeExistingWorldCoasts` | false | one-shot: the next Earth world started takes `Coasts` for chunks generated from then on (if it has the islands and its cells are wide enough); chunks already generated keep their coasts and may meet the new ones with a step. The flag then resets itself |
| `UpgradeExistingWorldCanals` | false | one-shot: the next Earth world started takes its own canal options (`earthSuezCanal`, `earthPanamaCanal` from the Earth tab; change them first with `/worldconfig earthSuezCanal true`) for chunks generated from then on; chunks already generated on a canal's course keep their land. The flag then resets itself |
| `UpgradeExistingWorldRiverChannels` | false | one-shot: the next Earth world started gets the river exit channels (or those of 0.14.1: as deep as the river, past the bars) in chunks generated from then on; chunks already generated keep their shallower water. The flag then resets itself |
| `UpgradeExistingWorldRiverEnds` | false | one-shot: the next Earth world started gets the river ends of 0.14.1 (rivers continued to their end, the revived and filled terminal lakes, rivers led on to water or faded out) in chunks generated from then on; chunks already generated keep their land and rivers and may meet the new lakes and courses with a step. The flag then resets itself |
| `UpgradeExistingWorldSeaDepth` | false | one-shot: the next Earth world started gets the sea floor of 0.14.1 (at least 6 blocks of water away from the shore, was 5) in chunks generated from then on; chunks already generated keep their sea floor and may meet the new one with a one-block step. The flag then resets itself |
| `UpgradeExistingWorldLakeShores` | false | one-shot: the next Earth world started gets the lake shores of 0.14.3 (land around a lake that is lower than the lake slopes down to the real ground instead of standing flat at the lake's level with a cliff where that ends, as south of Lake Bolsena and Lake Bracciano) in chunks generated from then on; chunks already generated keep their land and may meet the new slopes with a step. The flag then resets itself |
| `UpgradeExistingWorldRiverFalls` | false | one-shot: the next Earth world started gets the waterfalls on its rivers in chunks generated from then on; chunks already generated near a fall keep the old river and may meet the new one with a step. The flag then resets itself |
| `UpgradeExistingWorldGutterBridges` | false | one-shot: the next Earth world started (with Cylinder World) gets the Antarctic bridge across the seam gutter in chunks generated from then on; chunks already generated there keep the strait and may meet it with a step. The flag then resets itself |
| `UpgradeExistingWorldTreeRules` / `…GeologyRules` / `…OreRules` / `…FaunaRules` | false | one-shot: the next Earth world started applies the matching `Regional…` setting (on or off) and, for geology and ores, the current rock layers / ore tuning to its new chunks; the flag then resets itself in the config. Not needed for rule fixes, which reach existing regional saves automatically |
| `SpawnSearchRadius` | 3000 | blocks to search for gentle dry land around the Earth tab's start |
| `TopMargin` | 0.0625 | share of the world height above Everest |
| `LandCurve` / `SeaCurve` | 200 / 20 | curve constants (m); smaller = more exaggeration near sea level, more compression higher up |
| `MaxSeaDepth` | 0.43 | deepest floor as a share of the sea level's height (71 blocks in a 384 world) |
| `RealSeasons` | true | each place's real seasonal swing instead of vanilla's latitude-only one |
| `DetailScale` | 1 | strength of the detail noise |
| `GutterMargin` | 512 | open sea beyond Cylinder World's seam band on each side |
| `ExtendTemperatureRange` | true | live temperatures below −20 °C / above 40 °C where the real climate is |
| `MoveStoryLocations` / `StorySearchRadius` | true / 3000 | move story locations onto suitable land |
| `Rivers` | true | carve the large rivers |
| `RiverMinDischarge` | 100 | smallest navigable river (m³/s), full width and depth from here; raise it for fewer rivers (300: no Seine above Paris, 500: only the biggest) |
| `RiverWidthScale` | 1.5 | multiplier on river widths (1.5: Daugava ~21 blocks at its mouth, Rhine ~31, Danube ~42, Amazon ~72 in a 256k world; on bigger maps these are the floor in blocks and rivers are as wide as they really are where wider, decision 0014 — worlds before 0.14.7 grow them with the world instead) |
| `Streams` | auto | `ends`: each river continues upstream as a stream narrowing to one block; `all`: every stream down to `StreamMinDischarge` (for much larger worlds); `none`; `auto`: `all` in worlds at 1:25 and finer, `ends` in coarser ones (decision 0061) |
| `StreamMinDischarge` | 20 | smallest stream (m³/s), one block wide (the data goes down to 20) |
| `RiverStraighten` | 0.5 | 0..1: cut off loops where a widened river would touch itself (slivers, steps between its arms); higher cuts earlier, 0 keeps every loop |
| `RiverSmoothWidths` | 2 | smooth out bends shorter than this many river widths (Gaussian along the river); 0 keeps the real courses |
| `RiverRelief` | 1 | share of the land's relief the river surface follows; below 1 = deeper valleys and fewer steps, but a step where a river leaves a lake |
| `Weather` | true | Earth's weather (rain, clouds, winds, storms from the real climate); false = vanilla weather. Creation fallback in worlds from 0.17.0-pre.12 on; config on restart in older Earth saves |
| `WeatherPace` | 1 | weather hours per game hour (0.25..8): 1 = real speeds; higher = more weather per season, faster changes |
| `UpgradeExistingWorldClimateData` | false | one-shot: the newest climate fix ([decision 0018](docs/decisions/0018-climate-stripes.md): v2, the complete UDel normals, without one-row stripes and with December's precipitation) for this world's new chunks |
| `RainFrequency` / `StormFrequency` | 1 / 1 | multiply how often it rains (0..3) / storms form (0..5, 0 = none) |
| `ModelCache` | true | keep the slowest results of a world's map model (the straits' courses, the sea channels' ways; about 600 KB a world) in `ModData/earth/cache` and read them back at its later starts, only for exactly the same Earth build, data, runtime and world settings; false builds them at every start. The world is the same either way |
| `SelfTest` | off | automated in-game test (see below) |

## Data pipeline

```
tools/download_data.sh                  # the bakes' sources into rawdata/ (about 87 GB today)
python -m venv .venv
.venv/Scripts/python -m pip install numpy scipy netCDF4 tifffile imagecodecs pyshp pillow brotli
.venv/Scripts/python tools/bake_earth.py # ~5 min → earthdata/terrain.bin, climate.bin, earth.json
.venv/Scripts/python tools/bake_earth.py --climate-only   # ~20 s, when only the climate mapping changed
.venv/Scripts/python tools/bake_earth.py --climate-fix-only [3] # ~30 s, climatefix.bin (v1), climatefix2.bin (v2, complete UDel) and climatefix3.bin (v3, coast band; against v2); climate.bin stays
.venv/Scripts/python tools/bake_earth.py --weather-only   # ~20 s, weather.bin (complete UDel; stops on a cut-short download or a dry month)
.venv/Scripts/python tools/bake_earth.py --lakes-only     # ~5 min, when only the terminal lakes (TERMINAL_LAKES) changed
.venv/Scripts/python tools/bake_earth.py --shores-only    # ~2 min, when only the lake shores changed (shores.bin)
.venv/Scripts/python tools/bake_earth.py --outlets-only   # ~6 min, when only the outlet tongues changed (outlets.bin)
.venv/Scripts/python tools/bake_earth.py --outlets-report # ~1 min, lists the tongues (preview/outlets-report.json), writes no data
.venv/Scripts/python tools/bake_earth.py --coasts-only    # ~25 s, when only the coast outlines changed (coasts.bin)
.venv/Scripts/python tools/fetch_lake_types.py # once: which Natural Earth lakes are reservoirs (Wikidata) → rawdata/lake_types.json
.venv/Scripts/python tools/bake_rivers.py 20   # ~1 min → earthdata/rivers.bin (needs rawdata/hydrorivers and hydrolakes, and bake_earth.py first)
.venv/Scripts/python tools/preview.py    # preview/ images of the baked data and the seam path
```

- `terrain.bin` (30 MB): 2′ grid (10 800 × 5 400) in 6° tiles, Brotli per tile: elevation (log-quantised,
  ≈0.1 m steps near sea level, 4 m at 3 000 m), water level of the nearest sea/lake, flags (sea, lake, salt, ice),
  ice thickness. Decoded on demand, ~320 tiles cached.
- `climate.bin` (1.8 MB): 0.25° grid: sea-level-equivalent yearly mean, coldest and warmest month, yearly
  precipitation, VS rainfall.
- The client climate files (`climate.bin` and its fixes 1 and 2, `temperature.bin`, `rain.bin`, `aridity.bin`,
  `weather.bin`) are predicted planes ([decision 0082](docs/decisions/0082-predicted-climate-files.md),
  `tools/planecoding.py`): a spatial predictor in front of brotli, the same values in about 10 MB less of the zip. The
  bakes write that form; `tools/repack-client-data.py` turns a plain file into it.
- `rivers.bin` (12 MB, v2): 620 000 HydroRIVERS reaches with discharge ≥ 20 m³/s in network order (each with its
  downstream reach), simplified to ~1 block, with a de-dammed, smoothed water level (m) per vertex and a flag for
  vertices in a lake or the sea. The pools and ramps are laid out from it when a world loads (~0.6 s).
- `lakes.bin` (17 kB): the terminal lakes (`TERMINAL_LAKES` in `bake_earth.py`: name, level, seed, box), each filled
  below its level from its seed in the 1′ ETOPO, and the 2′ grid cells that differ from `terrain.bin` with them
  (16 000 patches). Applied in worlds with river ends.
- `shores.bin` (27 kB): the 2′ grid cells around lakes whose land is eased below the lake's level instead of raised
  to it (only the cells touching a lake keep the raise; out to the third cell it fades to the dry land's own height).
  Applied in worlds with eased lake shores ([decision 0011](docs/decisions/0011-lake-shores.md)).
- `outlets.bin` (4.5 kB): the 2′ grid cells that change when the lakes' tongues past their outlet are land: Natural Earth
  lakes that run on down their outflow river past the HydroLAKES pour point (Lake Erie's upper Niagara, past the falls),
  river-shaped (3.5 km wide or less, a quarter or less HydroLAKES lakes). Applied in new worlds (`LakeOutlets`), where
  river points there are no longer held at the lake's level ([decision 0055](docs/decisions/0055-lake-outlet-tongues.md)).
  Ships in the main mod, not a data pack.
- `coasts.bin` (9.5 MB): the GSHHG outlines of the big land (1000 km² and more, north of 60°S) and of the lakes in it
  that the grid has as sea, in 1° tiles of delta-coded vertices. Applied in large worlds with outline coasts
  ([decision 0012](docs/decisions/0012-coast-outlines.md)).
- `earth.json`: grid description, the baked seam path (longitude for every 0.1° of latitude; previews only since
  0.15.3, the mod cuts along a straight meridian), attribution.
- All geographic, so any world size works; larger worlds just get more noise detail per data cell.

## Tests

- Regional systems, offline on the real data (`dotnet run -c Release --project tests/<Name> -- "<repo root>"`):
  `RegionalTreesTests` (treeless share, regional mixes, borders, pick speed), `RegionalPlantsTests`,
  `RegionalGeologyTests` (v1 golden output, clay/peat rates at several world heights, v2 exposure and anti-grid,
  ~4 min), `RegionalOresTests` (rate classes against vanilla on the real evidence grid), `RegionalFaunaTests`
  (feasibility of every enabled rule on real terrain, habitats, profiles), `RiverIndexTests` (no arguments; legacy
  equivalence), `TerrainExpansionTests`, `SkyLongitudeTests` (the sky longitude on the live world's layout, and the
  AstraTerra binding against fakes, the local AstraTerra build with `SetLongitudeProvider` from
  `../AstraTerra/src/AstraTerra/bin/Release/net10.0/` and the installed 0.15.0 package; a missing one is a SKIP).
  Python: `tests/RegionalOresTests/*.py` (famous-deposit and coverage checks
  on the baked heat maps).
- `tests/EarthMapTests`: offline checks of the terrain model (no game needed): geometry round trips, vertical
  scale, known places, and renders to `tests/out/` (whole ring, Europe, Alps, Mont Blanc at 1 block/px, the Bering
  seam, …), and river checks at the Daugava, Rhine, Danube, Seine, Nile, Yangtze, Niagara and Victoria Falls (steps
  with and without a ramp, water walls, depths). `cd tests/EarthMapTests && dotnet run -c Release -- "<root>" 384`.
  Extra modes after the height: `audit` (walks the centre line of all 217 000 river reaches, ~10 s, report in
  `tests/out/river-audit.txt`: blocked water, water running uphill, falls of 2+ blocks, and the intended rapids and
  listed waterfalls), `mouths` (does every river
  end reach the sea or a lake), `probe lat,lon,half ...` (1 block/px renders and river ends), `col x z`,
  `reach id ...`, `chain id n` (pools along a river), `lakes` (the terminal lakes, river ends and the Lisbon channel,
  with renders before/after to `tests/out/ends-*.png`). `shores` (the lake shores of old worlds against eased ones around seven lakes: steepest
  steps, spills), `shoredebug lat lon` (the columns around a point in both), `coasts` (outline coasts at 512k and 4M with before/after
  renders to `tests/out/coasts-*.png`), `coastprobe lat lon ring`, `coastrender lat lon ring w h step`. The test world has river ends as a new world does; `NOENDS=1`
  checks it as an older world, and `POOLS=0` to `3` with an older pool layout (1-block steps; curtains; 0.16.4's chutes;
  0.16.5 without lake rims). `WORLD=1024000` builds the test world at another Earth tab size (add `BANDINNER=672` to
  match a server world, and `SIZESCALE=auto` for the river widths and windows of that size, as a server world has them),
  `area x z half` prints the settled water of a square (levels, ramps, rapids reaches), `profile x z length` the water
  along a river's centre line downstream (level changes, gaps between steps, rises), and `lakerims` lists where a lake stands more than 3 blocks above a river beside it
  (`RIMSPOTS="lat,lon;..."` runs the river check around each). The in-game self-test runs the same river audit at the end.
- `tests/WeatherTests` (`dotnet run -c Release -- "<root>" [maps]`): the weather offline on the standard layout: the
  ITCZ's seasons, determinism, the measured rain share against each month's climate at 12 places, the monsoon and
  Mediterranean seasons, prevailing winds, rain spells (hours, no flicker), storm numbers, basins, seasons and tracks,
  storm winds, the ring's seam, wind strengths and speed; `maps` writes climate, prevailing-wind and weather-moment maps
  to `tests/out/weather/`.
- `tools/run_selftest.sh [--size WxL] [--height N] [--keep] [--places "a;b"]`: creates a fresh world
  on a local dedicated server (`testserver/`), generates a list of places with the full worldgen pipeline, compares
  the generated terrain with the model, probes the game's climate there, renders top-down images and the world map
  to `testserver/EarthMap/`, and stops. Results of the runs so far: terrain = model in 99–100 % of columns
  everywhere except where structures or Cylinder World's polar zone rebuild it; the game's temperatures match the
  real yearly means within ~1 °C (Greenland −29 °C, Antarctica −46 °C, Dead Sea 23 °C, Persian Gulf 28 °C, spawn
  9–11 °C).

## Code map

| File | Side | What it does |
|---|---|---|
| `src/Core/EarthData.cs` | both | the baked data as one object: forwards to the files as read and to the world's current data view |
| `src/Core/EarthDataset.cs`, `RiverSourceRepository.cs` | both | the baked files as read (grids, climate fixes, overlay files, optional grids on first use); rivers.bin and the rivers on GLO-90 with their overlays |
| `src/Core/EarthDataView.cs`, `TerrainOverlays.cs` | both | what a world reads of the data (climate fix, fine grids, terrain overlays), frozen; a new view when a choice changes |
| `src/Core/TerrainTileCache.cs`, `TerrainSampler.cs`, `ClimateSampler.cs` | both | a view's terrain tiles with its overlays; bicubic/bilinear terrain sampling; climate sampling |
| `src/Core/TiledGrid.cs`, `FineTemperature.cs`, `EarthBinary.cs`, `TileCache.cs` | both | the tiled grid container (rain, aridity, summits, glaciers, CHELSA temperatures); shared binary reading; the tile cache |
| `src/Core/EarthGeometry.cs` | both | world x/z ↔ latitude/longitude, gutter, straight seam |
| `src/Core/EarthLayout.cs` | both | places the Earth on Cylinder World's ring and latitudes |
| `src/Core/VerticalScale.cs` | both | elevation ↔ block height for a world height |
| `src/Core/TerrainModel.cs` | both | one column's terrain (sea, lakes, land, ice, flattened sites); climate pixels; forest potential; `GetSurfaceColumn` with what later steps carve, for maps (0079) |
| `src/Core/ColumnStore.cs` | server | the terrain columns of recently generated chunks, built once by the terrain pass and read by the later passes and the neighbours' river margins (audit B03) |
| `src/Core/Noise.cs`, `Png.cs` | both | simplex noise, PNG writer and map colours |
| `src/EarthMapSystem.cs` | both | config, per-world params, spawn, network sync, temperature extension, latitude, `/earth` |
| `src/EarthWorldGen.cs` | server | world generation's wiring: which Earth handler runs in which pass and order, and its set-up (rule data, block tables, story locations, the companions' plan) |
| `src/PatchVanilla.cs` | server, both sides for the lapse, thunder and the client's patches | every Harmony patch and non-public game member of Earth's world generation and of the climate and spawning it reads (passes and guards, story locations, trees, corals, regional geology, ores, regional plants, 1318 placement, sea-level deposits, the real lapse, regional fauna, tiled dungeons), and the running game's and the client's (tall grass after the snow, dry land after rain, the grass colour, the season tint, thunder), in tables by feature; a missing or changed target turns only its feature off, whole, logged once; the creative date's patches are its `CreativeDateTable`; the weather's patches stay outside, as its doc says |
| `src/TerrainChunkWriter.cs`, `ChunkSettler.cs`, `TerrainHandoff.cs` | server | the terrain pass (in place of vanilla's GenTerra); the river water settled between columns, shared with the river query; what the terrain pass hands the surface pass |
| `src/RiverBlockPass.cs`, `AlpineSurfacePass.cs`, `RockFloorPass.cs` | server | the surface pass's river ramps and its surface columns (bare rock, snow, glaciers, beaches); the rock floor and prospecting |
| `src/StructurePlacementGuards.cs`, `SpringsAndPonds.cs`, `SpawnRangeGuards.cs`, `BlockLayerAdjustments.cs`, `EarthMapLayers.cs` | server | the guards on vanilla's structures, ponds, springs and animals; the block layers' arid fill, soil fertility and tall grass; climate, ocean, forest and shrub maps |
| `src/WorldGenerationContext.cs`, `WorldGenRules.cs`, `WorldGenBlocks.cs` | server | what the patches read about the world being generated; the rule data and block tables loaded once per start |
| `src/Core/SoilFertility.cs` | server | soil on land that isn't arid by the real aridity index (decision 0048) |
| `src/Core/FloodplainGrid.cs` | server | the natural flood of dry-season rivers through arid land (`floodplains.bin`, `tools/FloodplainBake`): arid ground in it keeps topsoil in ground-column worlds (decision 0048, soil rules 3) |
| `src/StoryPlacer.cs` | server | moves the story locations onto suitable real land |
| `src/EarthTrees.cs` | server | the forest-type species mix (replaces vanilla's pick in Earth worlds) |
| `src/Core/RegionalTreeCatalog.cs` | server | botanical regions, tree rules and the pure native → analogue → climate tree pick |
| `src/EarthPlants.cs`, `src/Core/RegionalPlantCatalog.cs` | server | native-range gating of regional ground-plant patches |
| `src/EarthPlacement.cs`, `src/Core/PlacementCatalogue.cs`, `assets/earth/config/placement.json` | server | the 1318 placement catalogue (decision 0047): fruit-tree type choice, crop and berry ranges, placement inventory, `/earth placement` and `/earth census` |
| `src/EarthGeology.cs`, `src/Core/RegionalGeologyCatalog.cs`, `src/Core/GeologyTransitions.cs` | server | regional rock profiles, surface-sediment context, peat/clay/halite eligibility, rock contacts and optional material mappings |
| `src/EarthOres.cs`, `src/EarthOreGenerator.cs`, `src/Core/RegionalOreCatalog.cs` | server | ore evidence maps, rate classes, host search and the deposit-generator wrapper |
| `src/EarthFauna.cs`, `src/Core/RegionalFaunaCatalog.cs` | server | supported natural-spawn range/habitat checks, range profiles and extinct-animal policy |
| `src/Core/TerrainReservations.cs`, `src/Core/TerrainExpansion.cs` | server | site pads and local terrain magnification published by companion mods before worldgen |
| `src/Core/RiverDeltas.cs` | server | delta branches: a river's water shared among the branches of a curated list of splits |
| `src/Core/RiverNetwork.cs` | server | the rivers in world coordinates with a grid index; pools, steps and ramps; widths, depths, banks |
| `src/CylinderCompat.cs` | both | reads Cylinder World's geometry by reflection |
| `src/Core/SkyLongitude.cs`, `src/Core/LongitudeProviderBinding.cs`, `src/AstraTerraLongitude.cs` | both | the sky longitude service (`LongitudeAt`) and handing it to AstraTerra by reflection |
| `src/Core/WeatherData.cs`, `WeatherNoise.cs`, `WeatherTypes.cs`, `WeatherModel*.cs` (one partial per subject), `WeatherStorms.cs` | both | the weather: monthly normals and ITCZ, noise, winds, rain, clouds, storms |
| `src/EarthWeather.cs` | both | the weather in the game: vanilla rain/cloud/wind patches, Sea's wind provider, `/earthweather` |
| `src/SelfTest.cs` | server | the automated in-game test |

## Versions

Every version's changes are in [CHANGELOG.md](CHANGELOG.md).

## Known gaps / next steps

- Weather and climate: the open items are in the suite backlog (`tools\Update-Backlog.ps1 -Mod earth` from `C:\VS mods`).

- **Ore balance** (rules v3, 0.14.1; measured in game on 2026-10-16/17, `docs/regional-ores-implementation.md`,
  "Validation status"): the inflation of host-limited ores and the empty districts of rules v2 are fixed, and the
  great districts are rich ore bodies. Still open: silver in galena about 10× vanilla, emerald 0.15× (gem pockets are
  host-limited), halite and sylvite outside the salt districts below vanilla, the Gulf Coast salt domes under the
  district threshold, blue clay missing (geology), and Pilbara and Mesabi weaker in the iron evidence than their size.
  Kimberley has about 380 diamond blocks per chunk (`Ores.RichDistricts` tunes all districts, `Ores.Resources.<code>`
  each one's ramp and richness). See
  [decision 0006](docs/decisions/0006-ore-balance.md).
- **Rivers**: the pools are still water, so there is no current except on the ramps (a push on boats in river pools
  would need a physics patch and the river data on the client). Whether a boat gets up a ramp depends on the boat:
  test with `/earth boattest`. River mouths meet the sea with fresh water next to salt water; deltas have only the
  branches HydroRIVERS lists. Steps the layout leaves without a ramp (inner edges of tight bends, meander loops that
  touch, tributary mouths): since 0.5.1 the world generator settles those too, the way VS's liquid spreading would.
  Straits and isthmuses narrower than the data grid (the Bosporus is carved by hand) could get the same minimum-width
  treatment as rivers.
- **Mountains** at 198 m per block are broad: the 2′ grid averages away individual summits. Options: bake a 1′
  "HD" terrain file for bigger worlds, or boost the relief detail (`DetailScale`).
- **Mountain glaciers** outside Greenland and Antarctica (Alps, Himalaya, Alaska) aren't in ETOPO's ice data; the
  snow line from the climate covers them with snow, but there are no glacier tongues reaching down the valleys.
- **Regional data is coarse**: rocks come from 0.5° cells (~280 blocks at 198 m per block) with a reviewed profile only
  for the Baltic interior and a few chalk areas; plant ranges are whole botanical countries (a US state, a Chinese
  province); animal ranges are ~96 km cells. Ore evidence is still densest where national surveys are open (USA,
  Europe, Australia); elsewhere an authored best-guess layer of 480 well-known districts (positions from general
  knowledge, ±10–30 km), the host-rock background and geological priors carry the supply. The USGS mineral-industry
  GIS compilations for Africa, Asia-Pacific, Eurasia and Latin America (public domain) would replace those guesses
  with survey data, but their download site blocks scripted downloads: fetch them in a browser and add them through
  the overlay registry (`docs/research/remaining-ores-2026-09-29/`).
- **Ground plants**: only the Floral Zones packs' plants follow native ranges; vanilla's ground plants keep their
  climate rules.
- **Not exercised in the test setup**: Fauna of the Stone Age and Geology Additions (their integrations are tested
  offline against their code and data only).
- **Licensing questions before publishing**: the TDWG boundary files carry no explicit licence (TDWG's site content is
  CC BY 4.0); PHYLACINE's current ranges derive from IUCN maps. Ore sources and their licences are listed in
  `assets/earthmap/config/ore-heatmaps-SOURCES.txt` (Polish PIG-PIB MIDAS: CC BY 4.0, Saxony LfULG: dl-de/by-2-0,
  verified 2026-10-02).
- The **client** side (latitude and extended temperatures for players) has no automated test; the dedicated server
  self-test covers the rest. It loads only `climate.bin` from the mod zip.
- Vanilla occasionally leaves a rain height map value of 65535 in a column (1–2 per 50 000 columns, also skipped
  by vanilla's own code); harmless as far as seen.
