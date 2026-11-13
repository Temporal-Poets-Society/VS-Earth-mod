# 0053: Exact scales, included GLO-90 heights, rivers on GLO-90 and a smaller detail noise

Date: 5 November 2026. Status: **implemented, on `main`** (in every Earth 2.0.0 release candidate since rc.1,
8 November 2026; not released). The direction is the author's (below); the details were proposals, built in test builds
1.0.3-tall.5 to tall.21 on `claude/tall-glo90` (committed 7 November as `5207611` and later; release/1.0 merged into it
the same day at the author's request, "Merge both to get right land, water and rivers", `3fc1d7d`) and carried into 2.0
rather than agreed as a whole. Since then: the scales are play styles 1:200 to 1:10 (1:100 the default) with 1:5 on the
Earth tab, and their sizes are [0068](0068-world-size-by-scale.md)'s, not item 1's; full GLO-90 heights are the default
at every scale ([0051](0051-glo90-elevation-on-demand.md)); the dam removal (item 19) is on in new worlds on full
GLO-90, its files on `egl-v1`. History below.

## Author's direction

4 November 2026, after tall.4 (decisions [0050](0050-tall-worlds.md), [0051](0051-glo90-elevation-on-demand.md)):

- "why we cannot place rivers naturally especially for 1:10 worlds where rivers should reach their true size?"
- "Scale 1:400, 1:200, 1:100, 1:50, 1:25 1:10 (why keep 1:12? …) For scales upon selection we can preselect recommended
  rest of settings - linear height for 1024 and 1:10 scale world, etc."
- "add GLO-90 option"
- "noise is fine but it should be small enough not to swap real terrain and have same transition between cells (separate
  smoothing?)"
- "could we add in Earth terrain data extra lets says 2 parts to have 120mb data and pack smaller scale GLO 90 data inside
  for default offline option"
- Then: "Over night build full version to test in game. But use VM machine yourself for in game testing. Targets as
  listed above are have GLO-90 and rivers converging, better default data we can expand to several Earth height map mods -
  up to maybe 160MB in 4 packs."

5 November 2026, late evening, after the data release plan ([0051](0051-glo90-elevation-on-demand.md)):

- "we need to clean Earth UI tab, group geography, weather, fauna etc."
- "Current Mountain height and Land heights are confusing also world height automatic is confusing. My mind for world
  height we show what is selected and change by scale selected."
- "We need to decide what to do with old height or use only GLO 90 data. Ideas? Also we need to test the data on
  smaller worlds"
- The author's answers to the choices put to them: land heights "GLO-90 only", with "but what about sea floor? we still
  need it" (sea floors stay on Earth's own data); the tab grouped by "Headings in one tab"; World height a "Number that
  follows size"; Mountain heights "Remove; automatic".

## Context

The 4 November comparison ([terrain against GLO-90](../research/terrain-vs-glo90-2026-11-04.md)) found:

- On the built-in 2′ heights the detail noise made 2–24 times the real number of tops in mountains, with a 45° grain.
- On full GLO-90 (0051) dry land matched within 0.3–0.4 m, but rivers did not: they keep HydroRIVERS' courses
  (15″, simplified to 0.002°, straightened for widths that grow with the world) and ETOPO 1′ levels, and their banks and
  valleys scale with the world (about 3.4 km wide at 1:12). In the Grand Canyon, the Middle Rhine and the Kali Gandaki
  8–23 % of the land ended more than 30 m below GLO-90.
- The menu's sizes were powers of two in thousands (1:391 … 1:12); nothing in the game needs that. A width must be a
  multiple of 512 (a map region) and at most 8,192,000 (Earth's own limit).
- Vanilla's tiled dungeons keep the map size they see at start-up; Earth sets the size later in a new world.

## Decision (proposed)

1. **Exact scales.** Earth size offers 1:400, 1:200, 1:100 (default), 1:50, 1:25 and 1:10: widths 125,440, 250,880,
   500,736, 1,002,496, 2,002,944 and 5,009,920 blocks (the ring, twice the length, is the Equator at that scale within
   0.2 %). Worlds made with the old sizes keep them; any width can still be typed into a world's config. Since tall.12
   the tab no longer offers 1:400 (item 11).
2. **Automatic settings for the scale.** The 1.22 main menu runs no mod code, so a choice can't change other fields on
   the screen. World height, Mountain heights and Land heights instead get "Automatic (for the Earth size)", the new
   default, resolved when the world is created and logged. Mountain heights on Automatic follow the world's height, not
   the scale: linear in worlds 512 and more blocks high, balanced relief below (author, 5 November: "linear only above
   512 or more"; tall.6), so a height chosen by hand counts too:

   | Scale | World height | Mountain heights | Land heights |
   |---|---|---|---|
   | 1:400, 1:200, 1:100, 1:50 | 384 | balanced (log 1000 m) | included GLO-90 |
   | 1:25 | 512 | linear | full GLO-90 (downloads) |
   | 1:10 | 1024 | linear | full GLO-90 (downloads) |

   Since tall.10 Land heights on Automatic is full GLO-90 at every size (item 8), and Mountain heights and Land heights
   are off the tab (item 10). Since tall.12 a play style per scale sets World height and Mountain heights instead, and
   the tab offers no Automatic for them (item 11); the Automatic values stay for older configs.

3. **Included GLO-90 heights** (the new default land heights): GLO-90 averaged to a coarser grid in four height data
   packs (`earthheightdata1`–`4`, required like the terrain and water packs), with a detail noise sized by GLO-90's own
   spread inside each cell, and the surveyed peaks restored as on the built-in heights. With full GLO-90 they replace the
   built-in heights where a square can't be downloaded: a 24″ grid (about 741 m) of land-only means, slightly
   sharpened, in steps of 1–8 m, with a 72″ layer of GLO-90's spread around it (tools/Glo90Coarse). Details under
   Verification.
4. **Rivers on GLO-90** with either GLO-90 choice: courses snapped to GLO-90's valley floors and levels from GLO-90
   (`rivers-glo90.bin`, same reaches as `rivers.bin`, in height data pack 4; tools/RiverSnap); loops cut only where
   the water would overlap itself (one water width), no smoothing, and chains without a cut keep their vertices;
   banks and valleys in metres from the land grid's own cell instead of the world's size. Water widths stay real
   (8·√Q m, width rules 1).
5. **Detail noise rules 1** in new worlds: wavelengths in metres below the land data's cell, octaves turned against each
   other (no shared lattice direction), sized smoothly between cells (smooth relief weights, Catmull-Rom 30″ summits); on
   the built-in heights about half the old spread.
6. **Tiled dungeons** get the live map size at WorldReady.
7. **Full GLO-90 without noise** (GLO-90 rules 3, tall.7; author, 5 November evening: "fix full res GLO 90 data that we
   have at 8 GB to not have dumb noise on it"): new worlds on full GLO-90 join its 3″ points by a smooth curve
   (Catmull-Rom along and across the rows, `FineElevation.TrySmoothHeight`) instead of straight lines, and add no
   undulation noise; the summit lift of rules 2 stays. Rules 1 and 2 worlds keep theirs
   ([glo90-smooth-2026-11-05](../research/glo90-smooth-2026-11-05.md)).

8. **GLO-90 only** (tall.10): Land heights on Automatic is full GLO-90 at every Earth size, downloaded per square as the
   world reaches it (0051's data release), with the included GLO-90 where a square can't be had. A world config
   without the setting gets the same. Sea floors, lake beds, coasts and rivers stay on Earth's own data, as before.
   "Built-in" and "included" stay as values for older saves and configs typed by hand. Why: at 1:400 to 1:50 full
   GLO-90 is 2–20 times closer to each block's real height than the included grid, and on full GLO-90 small worlds
   aren't too rough (Verification, Smaller worlds).
9. **The block's mean at small sizes** (GLO-90 rules 4, tall.10): at 75 m and more per block a column's land height is
   the mean of 2 × 2 smooth GLO-90 points spread over the block (3 × 3 from 150 m per block; `TerrainModel.FineFootprint`,
   `FineMean`), instead of the one point at its centre. So 1:100 and 1:200 take 2 × 2, 1:400 3 × 3, 1:50 and larger
   one point. A block whose points aren't all there takes the point at its centre. Rules 3 worlds keep one point.
10. **The Earth tab cleaned up** (tall.10):
    - Rows in four groups, each label starting with its group: "Map ·" (Earth size, World height, Sea level, Start,
      Start coordinates, Suez Canal, Panama Canal), "Terrain ·" (GLO-90 land heights, Summit height correction, Rock
      floor, Rocks and ores, River rapids), "Nature ·" (Plants and animals, Include extinct animals) and "Weather ·"
      (Source, Rain frequency, Storm frequency). GLO-90 land heights chooses how the GLO-90 squares come: as the world
      reaches them, or all of them from Earth's data release or from Copernicus when the world opens
      ([0051](0051-glo90-elevation-on-demand.md), Data release).
    - Mountain heights and Land heights are off the screen (`onCustomizeScreen: false`) and stay Automatic; a world
      config can still set them. (tall.12: Mountain heights is back on the tab, item 11.)
    - Each Earth size names the height that goes with it ("1:100 · 384 blocks high (default)", "1:25 · 512 blocks
      high", "1:10 · 1024 blocks high (test)"). World height's default is "Follows Earth size"; "World Height at the
      top" uses vanilla's slider, and a number typed into a world config sets it. (tall.12: each size names its width,
      and World height is a number set by the play style, item 11.)
    - Why labels and not headings: the 1.22 world creation screen is vanilla's. Each category in `worldconfig.json`
      becomes a tab of rows (a label, a control and a hover text), and the main menu runs no mod code (it reads only
      each mod's `modinfo`), so a mod can't add headings, a number that changes with another row, or a button there.
11. **A play style per scale** (tall.12; author, 5 November late evening: "Why did you remove linear and log height
    options?", then "Not automatic, presets for each world scale. Also world scale map size need side info on world
    width" and "also 1:400 option we can remove"). A play style is the only preset the world creation screen has: it
    sets every row's value, and the rows stay visible and can be changed. Five Earth play styles, the same in everything
    else:

    | Play style | Code | Earth size | World height | Mountain heights |
    |---|---|---|---|---|
    | Earth · 1:200 | `earth-200` | 250,880 × 100,352 | 384 | balanced (log 1000 m) |
    | Earth · 1:100 | `earth` | 500,736 × 200,192 | 384 | balanced (log 1000 m) |
    | Earth · 1:50 | `earth-50` | 1,002,496 × 400,896 | 384 | balanced (log 1000 m) |
    | Earth · 1:25 | `earth-25` | 2,002,944 × 801,280 | 512 | linear |
    | Earth · 1:10 (test) | `earth-10` | 5,009,920 × 2,003,968 | 1024 | linear |

    - The 1:100 one keeps the code `earth`, so the bench's `preset-earth`, older notes and other tools still mean 1:100.
      `EarthWorldOptions.IsEarthStyle` accepts the five codes (`PlayStyleCodes`).
    - Mountain heights ("Map · Mountain heights") is back on the Earth tab with its five curves and no Automatic; World
      height is a number (0 keeps "World Height at the top"; 64–192 are skipped, so no "Follows Earth size"). The values
      are those Automatic gave (by size and height), now written into each play style.
    - Earth size offers 1:200 to 1:10 and names each one's width ("1:100 · 500 736 blocks wide (default)"); its hover
      text lists width × length for each. 1:400 is gone from the tab; a 125,440 width typed into a config still works.
    - Changing Earth size on the tab leaves World height and Mountain heights as the play style set them: the screen
      can't change one row from another (item 10).
12. **Download all by default** (tall.12; author: "this should be default option", of "Download all: Earth's data
    release (GitHub, 8.6 GB)"). Terrain · GLO-90 land heights defaults to Earth's data release in every Earth play style.
    A world without the setting (made before the row existed) still downloads only as it needs, so opening an older
    world never starts the 8.6 GB download.
13. **River exit channels judged on the built-in heights** (channel rules 3, tall.12; author: "Yes, fix it here", of
    0052's proposal). A channel cuts low land on its way (bars, spits, beaches: 16 m or lower and at most 4 blocks above
    the sea). On GLO-90 that test read GLO-90's heights, which include forest canopy, dunes and dikes, so lanes kept
    land in them (an islet in the Rhine–Maas cut near Rotterdam; the Amazon's banks 24 m on GLO-90, 6 m on the built-in
    heights) and the cut broke 0051's rule that GLO-90 doesn't change which columns are sea. From rules 3 dry land whose
    height is GLO-90's is judged on its built-in height (`TerrainModel.Column.GridElev`, `SeaChannels.LowLand`); the
    channels' routes already ran on the grid. GLO-90 still gives the land its height everywhere else. Channel rules 3
    here is not 0052's rules 3 (sloping sides and more, on `walls-glo90-test`, not agreed): if 0052 is merged, its rules
    become 5 and include this (4 is the water atlas's dredging in place, item 14).
14. **The water atlas's fixes, for new worlds only** (tall.14, 7 November; author: "Yes, as tall.14", of bringing
    release/1.0's water atlas onto the tall line with older worlds left on the files they have). Release/1.0's water
    atlas (`717fb18`, its research note `docs/research/water-atlas-2026-11-06` there; author: "Yes do all the fixes and
    test again") checked Earth's water against Copernicus GLO-90's water body mask. Three of its fixes come over, each a
    version the world saves:
    - **Channel rules 4, dredging in place** (`SeaChannels`, from the atlas, with rules 3's judgement of GLO-90 land on
      the built-in heights kept): the way out is laid through the natural water on a grid 16 times finer, by the coast
      outlines and islands; land is cut only to join the river's own end to the water beside it (12 fine cells at most),
      and near the mouth low banks are worn down to widen it (author: "Could we get rid of Dredging or do it only in real
      river bed or erode it if we need it larger"). The routes are refined in the same parallel workers as the grid
      routes. Every new world gets rules 4, on either heights and with either courses.
      - **Open: lower worlds.** A low bank is one at most 2 blocks above the sea, counted in blocks, so in a world lower
        than 384 blocks more of a delta's land counts as low. At height 256 the widening takes about 60 km² of the coast
        beside the Bassac's way out (9.53–9.57° N, 106.38–106.51° E), land the GSHHG outline keeps: the Mekong outline
        check gives 99.4 % (13,427 of 13,513 at 1:78 m, 14,191 of 14,281 at 1:10 m) instead of 100 %. It passes at 320
        (1:78 m) and 384, and at 256 with rules 3. The tall line's automatic height is 384 or more, so only a height
        chosen by hand meets it. Release/1.0 has the same rules; its suite ran at 384.
        **Closed in tall.15:** the erosion runs only in worlds without outline coasts, the small ones where a mouth comes
        out too narrow; with outline coasts the shore stays the outline's (author: "turn off for big worlds", release/1.0
        `72af35f`, taken as it is).
    - **River courses rules 2** (`RiverNetwork.Glo90SnapCourses`): `rivers-glo90.bin` with the reaches the atlas moved
      into the mask's river and lake water (from 100 m³/s; ends, joins and discharges unchanged): 86,778 of 619,723
      differ from the published file (the atlas counts 87,227 moved).
      - **Open: the Tagus's islands.** The moved Tagus runs in the real channel between the Mouchão islands, and its game
        width reaches 13 of 915 island samples there ("rules v3: the Tagus's islands stay whole" fails; island rules 3
        protect island land only over the grid's sea). Release/1.0's moved `rivers.bin` fails it the same way.
    - **Sea referee 1** (`EarthParams.SeaReferee`, 101): where GSHHG draws sea over what the mask has as land at least
      15 m high or under a glacier, that is land (Patagonia's glacier fjords, the Rakaia, Vanuatu's Maewo; 22,031 km²),
      and outlines GSHHG has shifted by 1–4 km nearby are moved. It is part of the grid, the islands and the coasts the
      world starts from, before every other overlay.
    - **Data as differences.** Release/1.0 rewrote `terrain.bin`, `islands.bin` and `coasts.bin` in place, so older
      worlds there see them in new chunks. The tall line keeps the published packs (earthterraindata and earthwaterdata
      1.0.2, earthheightdata4 1.0.3-tall.5) for older worlds and ships only the differences in the main mod
      (`tools/water_overlays.py`): `earthdata/sea-referee.bin` (3.7 MB: 5,649 grid cells, 93 island patches, the island
      list with 5,634 islands new or changed, 179,541 → 177,815, and 1,218 coast tiles) and
      `earthdata/rivers-glo90-snap.bin` (4.5 MB). Each names the files it was made from and its results by their
      SHA-256: it is applied only to those files, data that already has it needs nothing, and otherwise a new world
      doesn't get it and an older one says so in the log. No data pack changes.
    - **Not taken:** the river side arms (`e5d1d75`, release/1.0's `rivers.bin`) were built for `rivers.bin`, not
      `rivers-glo90.bin`; worlds on the built-in heights keep the published `rivers.bin`. (Item 16 builds them on the
      rivers on GLO-90.)
15. **The water mask's coastline, sea referee 2** (tall.15, 7 November; author: "Do whatever that creates real terrain",
    of the land tall.14 still had where the mask has sea). GLO-90's height is exactly 0 m on every pixel the water body
    mask has as ocean and never 0 on its land, and the world's land heights are GLO-90's, so GSHHG land on the mask's
    ocean stood as flat land at sea level with no water. From sea referee 2 the coastline is the mask's wherever the two
    disagree by more than a shoreline offset (`tools/sea_ocean.py`, on top of item 14's referee, from the published data):
    - **Opened:** GSHHG land (the big land and the small islands) on mask ocean at 0 m, not an Earth lake cell, opened
      with a 3×3 square, in areas of 0.05 km² or more that touch Earth's sea: lagoons (Ébrié at Abidjan, Laguna Superior
      in Oaxaca, the Chatham Islands' Te Whanga), the Limfjord, marsh the Mississippi delta has lost, channels and fjord
      heads GSHHG draws narrow or off (Patagonia), and tidal flats the mask has as sea (the Indus delta's creeks, Khor
      Musa, the old Yellow River delta), which GLO-90 has flattened to the water surface. 135,471 km², 50,201 of them on
      small islands.
    - **Added:** GSHHG sea on mask land, opened 3×3, with the mask's lakes and rivers it encloses, in areas of 0.05 km²
      or more: new land in the Meghna estuary and at Chongming, the Guiana mudbanks, the Laguna Madre's Land Cut,
      reclaimed land. 88,298 km². With both, islands GSHHG draws 1–2 km off their place (Tanna, Roatán, Taveuni,
      Akimiski) move to where the mask has them instead of being cut away.
    - **Kept: GSHHG land the mask has no counterpart for.** The mask lacks some real islands outright: Donousa in the
      Cyclades (14 km², 383 m), Silhouette in the Seychelles, Visokoi in the South Sandwich Islands are open sea at 0 m
      there with no land for 10 km and more. GSHHG also draws some reefs as islands (Minto Reef, 46 km², under water).
      One data set alone can't tell those apart, so GSHHG land is opened only where the mask has land within 4 km of at
      least half of that piece of land, amounting to at least a tenth of its area; 5,530 km² of candidates stay as GSHHG
      has them. A first cut without this lost 40,616 small islands (7,115 km²), Donousa and Silhouette among them.
    - Small islands 179,541 → 167,555 (tall.14: 177,815), many merged or moved; 4,450 2' grid cells land → sea and
      1,045 sea → land, the referee's included. Greenland, the high Arctic and south of 60° S are left alone, as the
      referee leaves them.
    - **Data:** `earthdata/sea-referee.bin` version 2 (6.26 MB; tall.14's 3.73 MB): changed coast tiles and islands are
      stored as stretches of the published outlines and new vertices. Still one file applied only to the published
      files it was made from; no data pack changes.
    - **Open, not part of this:** land where the mask has a lake or river (lakes GSHHG and HydroLAKES don't have; about
      50 km² in the Patagonia window).
16. **River side arms on GLO-90** (tall.16, 7 November; author: "Merge both to get right land, water and rivers", of
    release/1.0 and the tall line). HydroRIVERS has one line per river, so where a river splits round an island only one
    arm was there and the island was part of the bank (Doles sala on the Daugava). Release/1.0 builds the other arms from
    the water mask (`e5d1d75`, `tools/river_arms.py`; author: "Yes, install them"), but on its moved `rivers.bin`; new
    worlds here read the rivers on GLO-90, so the same tool, unchanged, ran on the moved `rivers-glo90.bin` (courses
    rules 2's list):
    - 2,408 arms, 19,669 km (release's `rivers.bin`: 2,598, 22,257 km), from 16,419 mask islands of 0.5–400 km² beside
      rivers of 200 m³/s or more; left out most often for no way round through water (3,098), the ends outside the
      tile (2,038) and the arm too close to the course (1,230). The largest: the Volga at Astrakhan (a 369 km² island,
      72 km arm), the Amazon by Macapá, the Solimões, the Kolyma.
    - **River courses rules 3** (`RiverNetwork.Glo90ArmsCourses`, EarthParams 97): rules 2's list with the arms. Each
      arm is a reach of its own from a vertex of the main course to where it rejoins, and the reach it joins is split
      there; the arms come first and each reach is followed by its later pieces, so reach numbers change (the game's
      flow order: every reach flows into a later one). Lake outlets find their reaches by place, not by number.
    - **Data:** `earthdata/rivers-glo90-arms.bin` (186 KB, `tools/water_overlays.py --armed`): the arms' records and
      the 1,864 vertices where 1,786 reaches are split, applied after `rivers-glo90-snap.bin` and only to the moved
      rivers it was made from. No data pack changes.
    - **Open:** an arm can step up one block where it leaves or rejoins its river (release's audit: 88 of 163 places
      where water runs uphill were on arms). Worlds on the built-in heights (older worlds and hand-typed configs) keep
      the published `rivers.bin`: release/1.0's moved and armed one would need a new Earth Water Data pack, as a
      difference it is too big for the main zip.
17. **Lakes from the water mask's outlines** (tall.17, 7 November; author: "Take all lakes. We will increase size anyway
    and can put large data into same repo as height map in git", after the [measuring pass](../research/lake-outlines-2026-11-07.md)).
    The mask has 4.1 million lakes Earth lacks (734,289 km²), and Earth's own lakes sit on a 2′ grid:
    - **Data:** `lakes-glo90-1.bin` (`tools/bake_lake_outlines.py`, 58.1 MB, sha256 `8f0075ac…79236384`): every lake
      of the mask north of 60° S as outlines with its level (the median GLO-90 height of its largest piece) and flags
      (salt, raised, missing in Earth), in 1° tiles. Left out: lakes Earth's sea has, reservoirs (no modern dams in
      1318) and lakes on glaciers. Lakes on Earth's salt and terminal lakes (0005) stay the grid's, at their 1318
      extent. 4,472,466 lakes drawn, 1,783,625 km²; 126,824 salt (dry, flat and arid at the lake's own temperature).
    - **Where it comes from:** too large for the main zip, so it travels with the GLO-90 heights: read from the GLO-90
      folder, or fetched once from Earth's data release beside the `.egl` squares, checked by size and SHA-256.
      On `earth-glo90`'s release `egl-v1` since 8 November 2026 (the author: "Yes, upload it").
    - **Rules** (`EarthParams.LakeOutlines`, 102): where a world reads GLO-90 for a square, not a fallback, the
      outlines replace the grid's fresh lakes: water at the lake's level, a bed sloping from the shore (5 % to 40 m, or
      the grid's own deeper bed), dry shores held at the water top, beaches within a block, no water beside lower water
      or lower ground. Built-in heights and fallback squares keep the grid's lakes.
    - **Probe** at 1:100 (Patagonia, Saimaa, Kuopio, Lake Geneva, Uyuni): drawn lake 0–96 % → 92–100 % of the mask's;
      no mismatch more than 2 blocks from a shore; no spills.
    - **Open:** two outline lakes within a block at different levels; the map and `/earth info` read the grid's lakes;
      raised lakes at their modern extent; the Rann of Kutch and wetland clusters count as lakes.
    - **Combined build and packs** (author: "Yes, combine", then "split and coordinate with 'Ore maps' session on
      that"): tall.17 also carries release/1.0 up to `7edb1c7` (the biome work, creative date, goats, zones on CHELSA,
      savanna, the December rain fix and ground columns). As one archive Earth was 71.1 MB, 42.5 MB without the ground
      tiles. Split (`tools/package-tall.py`, `tools/package-ground-pack.py`):
      - `earth-1.0.3-tall.17.zip` 31,558,976 bytes;
      - `earthwaterfixdata-1.0.3-tall.17.zip` 10,966,512 bytes, required: the tall line's water difference files
        (`sea-referee.bin`, `rivers-glo90-snap.bin`, `rivers-glo90-arms.bin`), routed and hash-checked through
        `pack-index.json` like the height packs;
      - `earthgrounddata-1.0.3-tall.17.zip` 28,563,026 bytes, optional (the ground-column tiles; the ore session's
        pack, the author's choice of a separate ground pack). Without it a ground-columns world logs that the pack is
        missing and uses regional geology.
      The published packs (terrain and water 1.0.2, heights tall.5) stay as they are.
    - **Bench** (AgentBridge, the three zips with the published packs, the lake file placed in the bench's GLO-90
      folder): world t17b (play style earth, 1:100) logs "rivers on GLO-90, moved into the water mask's rivers, with
      their side arms; sea refereed by the water mask; lakes from the water mask's outlines" and "Earth lakes: … shores
      measured 8 blocks out", the zone, ground cover and moisture rules, no errors; from above, Saimaa's islands and
      channels stand at block resolution with beaches. `/earth where` at Kassel and the Serengeti gives the biome
      session's fixed plant lists. World t17gc200 (1:200, ground columns from a staged play style, no 30″ sets) logs
      "Earth ground columns v1 … from 2057 tiles" with the 30″ sets listed as not installed, and no pack error. Lake
      Geneva fills its basin there. Bench Mods, ModConfig and settings restored by hash.
18. **Rivers from the water mask's outlines** (tall.18, 7 November night; the author, on tall.17's world map at Riga in
    a 1:10 world: "river looks to be wrong. straight lines and not in the correct path", then "Yes, build them";
    [note](../research/river-outlines-2026-11-07.md)). A course is a line of straight pieces about 500 m long with a
    channel of one width along it: at 1:10 the Daugava at Riga was a 20-block channel in 50-block straight lines where
    the river is 60–70 blocks wide.
    - **Data:** `rivers-outline-glo90-1.bin` (`tools/bake_river_outlines.py`, 30.3 MB, sha256 `52a0c79e…49f3807b`):
      every river pixel of the mask north of 60° S as outlines in the lakes file's layout (`EMRO`), each piece at its
      median GLO-90 height, reaching a pixel into the lakes and sea it touches; 4.03 million pieces, 58.2 million
      pixels. Reservoirs are left out (no modern dams in 1318): from each HydroLAKES reservoir's dam, the highest flat
      water there and up to 3 m above it, to the reservoir's area and never over twice it. 5,919 of 6,687 found
      (2.07 million river pixels left out); 417 whose flat water is more than twice theirs (a forebay in the
      Sacramento delta; reservoirs joined to a neighbour or to a lake they raised) and 351 not found stay. It travels
      with the GLO-90 heights as the lakes' file does; publishing it on `earth-glo90` needs the author's yes.
    - **Rules** (`EarthParams.RiverOutlines`, 103): where a world reads GLO-90 for a square, dry land (not ice) inside
      the outlines is river water at the nearest course's level (within the course's reach, else within about 2 km;
      with none, the piece's own), its bed falling half a block per block from the shore to the course's channel depth,
      and land 1.5 blocks beside it held at the bank. A course whose centre line lies in an outline draws no channel,
      bank or valley there; just outside one its half width is at most 2 blocks more than its distance to it.
    - **Probe** (1:10, height 1024, linear): at Riga river columns 45,626 → 236,498 and the mask's river covered 17 % →
      88 %, every river top at the sea's 109, the Daugava, its north channel, Sarkandaugava and Ķīšezers one water
      body; 99 one-block steps where the river meets Ķīšezers; no spills; cost unchanged. At the Riga dam the mask's
      river below it, the course's channel above it (the reservoir left out); Ķegums unchanged; the Sacramento
      delta's channels drawn (7,435 → 373,305 river columns).
    - **Bench** (AgentBridge; tall.18 with the published packs and tall.17's Water Fix and Ground Data packs, the river
      file in the bench's GLO-90 folder; world t18r10, play style earth-10): the log names the river outlines
      ("shores measured 16 blocks out"), no Earth errors; from above, the Daugava at Riga has its own shores, side
      basins and harbours, and at the dam the mask's river gives way to the course's channel. Bench Mods, ModConfig
      and settings restored by hash.
    - **Packs:** only Earth changes (`earth-1.0.3-tall.18.zip`, 31,562,507 bytes, sha256 `07fd4542…68cdd72a`; the DLL
      and modinfo differ from tall.17). Its Water Fix Data files are unchanged, so it keeps requiring
      `earthwaterfixdata` 1.0.3-tall.17 (`tools/package-tall.py` now keeps the base's pack when its files match);
      `earthgrounddata` tall.17 as before.
    - **Open:** straight pieces the mask has itself; one- and two-pixel pieces as ponds; reservoirs without a HydroLAKES
      dam, those whose water runs into more than their own, and reservoir water more than 3 m above its level by the
      dam (Three Gorges' upper reaches) stay river; a left-out reservoir's land is GLO-90's flat surface of it.
19. **Dams taken out of GLO-90** (proposed; prototype, 7 November night: the tall line's session asked for a
    proposal and a prototype to finish items 17 and 18's "no modern dams in 1318", then the author for the global
    bake; [note](../research/dam-removal-2026-11-07.md)). With the reservoirs' water left out, GLO-90 still has the dams'
    walls (Three Gorges' crest at 185 m over the river at 62 m), their water's flat surface as land, and courses that run
    flat through a reservoir and step down at its dam (at 1:10 the Yangtze 9 blocks at Three Gorges, 2 at Gezhouba).
    - **Data:** `dams-glo90-1.bin` (`tools/bake_dam_removal.py`, `EMDM`): per GLO-90 square the heights that change,
      and the lowered course levels keyed to the course list they were made for (`DamRemovalData.KeyOf`, river courses
      3). Baked first for two test areas (Riga; the Yangtze from Chengdu to Yichang), then for the whole world:
      4,245,225 bytes, 2,604 squares, 13.2 million GLO-90 points and 69,387 course levels, in about 12 minutes on 16
      workers (reservoirs in groups that keep a cascade together). Rebaked on 8 November with the rules below:
      4,829,546 bytes (sha256 `6e50b73c…be1af66`), 2,703 squares, 14.6 million points, 75,925 course levels, and river
      corridors in 1,214 squares (1.66 million pixels). Not published.
    - **Reservoirs** from each HydroLAKES pour point as in item 18, and on up to 30 m above the flat water by the dam
      while under 0.8 times their area (Three Gorges: 11 m, 687 of 853 km²): 5,919 of 6,687 found, 1,756 of them
      risen past 3 m; the same 417 wide and 351 not found as item 18. The river outlines are rebaked from the same
      regions (`reservoirs-dams.npz`), so the water left out and the floor rebuilt agree: 30,282,208 bytes, 2,307,573
      reservoir river pixels left out (tall.18's file: 2,067,317). Version 2 (`rivers-outline-glo90-2.bin`, 8 November,
      with the corridors and bridges): 30,630,118 bytes (sha256 `6e10438f…b820aaf1`), 2,321,155 reservoir river pixels
      left out, 21,639 bridge pixels filled in 8,972 pieces.
    - **Rules** (`DamRules` 1, EarthParams 105; new worlds on full GLO-90 with river courses 3 get it from the 8 November
      build, with `RiverOutlines` 2):
      - Dam walls: ground within 2 km of the pour point, within 300 m of both the reservoir and the lower water or ground
        below the dam, and at most 30 m above the reservoir's level. (Adding the dam's downstream face to the wall was
        tried and is off: it left 3,792 walls higher.)
      - River corridors: along each course run through a zone that reaches a dam or has at least half the flow of its
        reservoir's largest run (Jebel Aulia's White Nile, where no wall is found). Tributaries keep their channels: a
        steep one's pools stepped two blocks across its corridor at Kariba. A corridor is as wide as the mask's river
        within 3 km where it comes in or goes out (else 3 m times the root of the flow), at most half the old water's
        width, and lies on the old water and the dams only. The floor holds them at the course's level, and the river outlines (version 2) draw
        them as river, so the river keeps its width through the old reservoir and across the dam.
      - Courses through the reservoirs and walls fall evenly by length from where the river comes in to the foot of
        each dam (the lowest level within 2 km below it, but not below the ground just below the dam, where the river
        falls on steeply: Guatapé's 333 m trench became 111 m) and to a tributary's main river. Levels are never raised
        and never rise downstream. A cascade falls dam to dam: Pļaviņas 73 → 33 m; Ķegums 26 → 17 m; Riga 1 m; the Yangtze
        166 → 64 m at Three Gorges (512 km) → 40 m at Gezhouba.
      - Walls come down to the valley's cross-section 300–600 m below them. (Filling them with the reservoirs' floors
        instead, as the second pass first did, doubled the walls left more than 100 m above the river below: 61 → 122.)
      - The ground under the reservoirs is a smooth surface between the courses (half a metre above their water), the
        walls and the shores. Reservoirs without a course fall 10 % from their shores to the foot. Nothing is raised
        above GLO-90.
      - The mod reads the dam file before it builds a world's model (`EarthElevationSystem.LoadDams`): from the GLO-90
        folder, else the data release, by size and SHA-256. It applies the course levels in `EarthParams.BuildModel`
        when the key matches, and a square's heights as `FineElevation` loads it. A new world that can't get the file,
        or whose courses don't match, the first time it needs it stores `earth-dams-off`. It then keeps the dams for
        good and reads the version 1 river outlines. A world that had the dams and loses the file logs an error.
    - **Bridges** (in the version 2 river outlines, at the parent session's request after the author saw Riga's
      Southern Bridge as a land dam at 1:10): land a 3 × 3 closing of the water fills, with the river on one side and
      any water on the other, is river, with all of these:
      - a line at most two pixels thick, no shorter across the river than along it;
      - its ends within 3 pixels of land;
      - open water across it on both sides for as long as it is (4–8 pixels);
      - the water beside it no wider than its reach (a channel it spans);
      - it touches the river.

      Its pixels don't count towards their piece's level. Most decks are water in the mask already (Riga's Stone and
      railway bridges; Budapest, Cologne, Prague, London). Zaķusala, the Danube Island, Margaret Island, quays and the
      Amazon's levees stay land.
    - **Probe** (1:10, height 1024, linear; tall.18 against the dam file with the rebaked outlines): one-block steps
      where a river meets lower water 1,232 → 131 at Three Gorges (the river's own fall below it), 104 → 0 at Pļaviņas,
      26 → 0 at Ķegums; two-block steps 44 → 0 at Gezhouba; no new spills; cost unchanged. The suite at 256 and 384
      (with courses 3) has its known failures only. The global files give the same counts there, and at new dams
      601 → 0 (and 57 → 0 two-block) at Zhiguli, 807 → 96 at Volgograd, 886 → 39 at Sayano-Shushenskaya, 374 → 27 at
      Hoover, 309 → 40 at Kariba, 466 → 248 at Aswan, 644 → 378 at Nurek.
    - **Probe, 8 November** (the second pass's global files, with the version 2 outlines; the note has the table):
      - Dam steps: still 0 at Pļaviņas, Ķegums and Zhiguli. Two-block steps: 0 at Gezhouba and Kariba.
      - The river now keeps its width through the old reservoirs: 102,630 → 146,524 river columns at
        Sayano-Shushenskaya, 63,727 → 180,056 at Jebel Aulia.
      - The one-block steps left are the river's own pool steps 3.4–9.4 km from the dams, over its full width now:
        886 → 99 at Sayano-Shushenskaya, 807 → 249 at Volgograd, 374 → 47 at Hoover, 309 → 74 at Kariba; the Vakhsh's
        gorge by Nurek 644 → 631.
      - Walls more than 100 m above the river below: 62, as in the first pass.
      - The suite at 256 and 384 (with and without COURSES=3): every line as before.
    - **Dam rules 2** ([0063](0063-rivers-follow-the-real-channel.md) rule 3, `dams-glo90-2.bin`, 8 November; the
      note has the rules and numbers). These fix the necks the author saw at Rīgas HES and Kakhovka's wide, high
      corridor:
      - **Width:** the corridor keeps the river's width within 1 km of a wall, the tailrace's banks included, and is
        pinned there.
        - Its width is the smaller of the river's coming in and going out, measured past the tailwater basin, at most
          10√Q or 186 m.
      - **Levels:** never above GLO-90's ground beside them less 1 m.
      - **Cascades:** a cascade's foot comes from the next river outside the zones.
      - **Global bake on courses rules 3's list** (12 minutes on 4 workers): 5,119,779 bytes, 104,019 course levels.
        - Necked dam runs went 624 → 0 by the first count; the drawn widths are in the note.
        - Kakhovka's corridor went from 0.8–1.1 km to 0.5–0.7 km, its levels from 26 → 0 m to 13 → 0 m against GLO-90's
          14 m.
      - **Probe:** one-block steps still 0 at Pļaviņas, Ķegums and Zhiguli; two-block steps still 0 at Gezhouba and
        Kariba.
      - **Not given to any world yet.** The mod reads `dams-glo90-2.bin` for `DamRules` 2. The final file is baked on
        courses rules 4's list (`DamRemovalData.CourseRules2`), and its size and hash are filled in then.
    - **Open:**
      - Reservoirs with no course through them: their floor stays near the old water, so a wall that comes down can
        be a dip.
      - Bank towers and abutments; deck bumps over rivers too narrow for the mask (not checked).
      - Lakes drawn from the lake outlines inside a zone; long embankments more than 2 km from the pour point.
      - The 768 reservoirs that stay; canals and harbours (5).
      - Publishing the dam file and the version 2 outlines on the data release needs the author's yes.
20. **No beach sand below the sea far from the coast** (GLO-90 rules 5, 8 November; the author, on squares and crosses
    of sand and soil 30 km from the sea near Suez in their 1:10 tall.22 world: "Fix."). Two faults made it, both
    global:
    - **The beach rule's 0 m.** `TerrainModel.CoastColumn` makes land a beach below a height that fades from 3 m at the
      outline's coast to 0 m a Reach (640 m) inland. Wherever the grid has sea within a few cells it runs at any
      distance, so all land below 0 m became beach sand, flush with the sea. GLO-90 stores whole metres, and fields
      and fish ponds a metre below the sea are single 90 m pixels: 9-block squares of sand at 1:10. Where the grid
      itself has sea over the land (polders), the side's narrowed channel moved the coast inland, so the height never
      reached 0 m (Haarlemmermeer, Flevoland, the Po delta: nearly every column sand). Rules 5: beyond the Reach
      nothing is a beach, measured from the outline itself where that is further than the narrowed channel.
    - **The smooth curve's swings.** Catmull-Rom (rules 3) swings past the two points it runs between by up to 7 % of
      a neighbour's step. Round a pixel 1 m lower than flat land at 0 m its four diagonal neighbours dipped to about
      −0.005 m, a block lower: the X-shaped crosses. Rules 5 hold each piece of the curve, along the rows and across,
      between its two points (`FineElevation.CatmullRomBounded`); on a steady slope nothing changes.
    - **Probe** (the author's world rebuilt offline from the installed packs, 2 km boxes, every 4th block; beach
      columns before → after): Suez spot 351 → 0 (3,940 → 7 on the probe's map around the screenshot's spot), Nile
      delta north 288 → 0, Haarlemmermeer 2,554 → 0, Flevoland 2,561 → 0, Po delta 1,470 → 0. Real beaches stay with
      rules 5: 9 columns on the Dutch dune coast, all 78 land columns of Venice's Lido. `ElevationTests`: the bounded curve never leaves the four points around it
      at 4,000 places in N60E006 (the plain curve 170 times); the three checks that already failed before (exact
      equality at a grid point and the last row, a fallback square) fail as before.
    - **Not changed:** the land stays at sea level − 1 (y 109 at sea level 110) as before, now with soil; GLO-90's
      modern polders and the Suez Canal (a lake from the water mask's outlines) are today's land, not 1318's. Worlds
      made with rules 4 and earlier keep their beaches.

Existing saves keep everything: each rule is a saved version (EarthParams 95 FineElevation, 96 CoarseElevation, 97
RiverCourses, 98 DetailRules; 94 stays reserved for 0052) and is 0 in older saves. Channel rules 3 is saved in
`RiverChannels` (52); worlds made with tall.11 and earlier keep 2. From tall.14 (item 14) new worlds save channel
rules 4 and `SeaReferee` 1 (101), and on GLO-90 river courses 2; worlds made with tall.13 and earlier keep 3, 0 and 1,
and read the published terrain, islands, coasts and GLO-90 rivers. From tall.15 (item 15) new worlds save `SeaReferee`
2; tall.14 was never installed, and its test worlds (1) read 2's data and say so in the log, so new chunks there may
meet older ones with a step where the coastline moved. From tall.16 (item 16) new worlds on GLO-90 save river courses
3; worlds of tall.14 and tall.15 keep 2, without the side arms. From tall.17 (item 17) new worlds on full GLO-90 save
`LakeOutlines` 1 (102); older worlds have 0 and keep the grid's lakes. A new world that can't get the file the first
time it needs it stores that (`earth-lake-outlines-off`) and keeps the grid's lakes for good, so chunks never mix the
two; one that has used it and later loses the file logs an error and keeps the grid's lakes for new chunks. From
tall.18 (item 18) new worlds on full GLO-90 also save `RiverOutlines` 1 (103), with the same rules for its file
(`earth-river-outlines-off`); older worlds have 0 and keep the courses' channels. From the 8 November build (item
19) new worlds on full GLO-90 with river courses 3 save `DamRules` 1 (105) and `RiverOutlines` 2. A world that can't
get the dam file the first time it needs it stores `earth-dams-off`, keeps the dams and reads the version 1 river
outlines for good. Older worlds keep 0 and 1.

## Consequences

- Four more required downloads (about 160 MB with the GLO-90 rivers; see Verification), on top of the terrain and water
  packs.
- The rivers on GLO-90 depend on GLO-90's own errors: it is a surface model (trees, bridges), and its water bodies are
  flattened.
- Automatic full GLO-90 downloads from Earth's data release (0051), since tall.10 at every Earth size: about 0.3 MB
  per square as a world reaches it; a 1:400 start in the Alps took 114 squares (0.1 GB) in game. Offline, a new
  world's squares that can't be had keep the included heights for good (0051 Proposal 4).
- The Earth tab's grouping is only in the labels: vanilla's screen sorts nothing and draws no headings.
- From sea referee 2 (item 15) the coastline follows GLO-90's water mask where it has a counterpart for GSHHG's land,
  so the mask's own errors come with it there: a real island the mask lacks within 4 km of land it has is opened, and
  intertidal flats the mask has as sea are sea.

## Verification

Test build 1.0.3-tall.5, 5 November (night), on `claude/tall-glo90`, uncommitted. Release zips:
`release/earth-1.0.3-tall.5.zip` (28.6 MB) and `release/heightpacks/earthheightdata1..4-1.0.3-tall.5.zip` (39.78,
39.80, 39.79 and 33.96 MB: 153.3 MB, each under the 40,000,000-byte limit of 0040). Installed in the player's game on 5 November (History).
Figures and screenshots: [tall5-checks](../research/tall5-checks-2026-11-05/).

**Included GLO-90** ([build note](../research/glo90-coarse-packs-2026-11-05.md)): 24″ rows, round(150·cos lat) columns
per degree. Over the whole dataset the grid without its noise is 17.2 m RMS from GLO-90; per window 2.2 m
(Netherlands), 8 m (Gauja), 21 m (Middle Rhine), 57–68 m (Matterhorn, Everest, Grand Canyon). Lookup 100–340 ns; about
230 MB in memory; opens in 78–93 ms. At 24″ the heights take 120 MB of the four packs and leave room for the rivers;
18″ would not have fitted in 160 MB with them.

At the 13 places of the [4 November comparison](../research/terrain-vs-glo90-2026-11-04.md) (the author's world
settings, 4,096,000 wide, height 1024, linear), dry land against GLO-90, mean error and number of tops:

| Place | Real tops | Built-in, tall.4 | Built-in, noise rules 1 | Included GLO-90 | Full GLO-90 |
|---|---:|---|---|---|---|
| Everest | 120 | 180 m / 331 | 170 m / 163 | 75 m / 85 | 0 m / 118 |
| Dhaulagiri | 74 | 241 m / 409 | 203 m / 188 | 69 m / 66 | 0 m / 74 |
| Aconcagua | 91 | 159 m / 239 | 146 m / 118 | 60 m / 80 | 0 m / 91 |
| Matterhorn | 47 | 177 m / 161 | 168 m / 80 | 63 m / 50 | 0 m / 46 |
| Dolomites | 64 | 171 m / 131 | 166 m / 18 | 63 m / 53 | 0 m / 64 |
| Kilimanjaro | 16 | 139 m / 382 | 61 m / 214 | 24 m / 11 | 0 m / 16 |
| Fuji | 8 | 82 m / 183 | 40 m / 45 | 13 m / 8 | 0 m / 8 |
| Yosemite | 34 | 144 m / 142 | 132 m / 24 | 41 m / 37 | 0 m / 35 |
| Glen Coe | 75 | 134 m / 31 | 132 m / 7 | 46 m / 61 | 0 m / 75 |
| Nærøyfjord | 83 | 168 m / 154 | 161 m / 25 | 56 m / 95 | 0 m / 85 |
| Grand Canyon | 102 | 161 m / 314 | 148 m / 88 | 65 m / 152 | 0 m / 104 |
| Middle Rhine | 11 | 31 m / 0 | 30 m / 0 | 17 m / 40 | 0 m / 12 |
| Gauja | 0 | 7 m / 0 | 7 m / 0 | 6 m / 0 | 0 m / 0 |

Noise rules 0 (older worlds) gave output byte-identical to tall.4 at three places. Open: on the included heights the
noise makes too many small tops on plateaus and low hills (Grand Canyon rim, Middle Rhine).

**Rivers on GLO-90** ([build note](../research/river-snap-2026-11-05.md)): 619,723 reaches, 4.19 M vertices, 23.98 MB;
no joints apart, no rise within reaches or at joints except 17 lake/sea points already in `rivers.bin`; courses moved
p50 108 m, p90 324 m. Same 13 places: the share of land more than 30 m below GLO-90, and the river water's median
height against GLO-90 under it:

| Place | Full GLO-90 + rivers.bin | Full GLO-90 + rivers on GLO-90 | Included + rivers.bin | Included + rivers on GLO-90 |
|---|---|---|---|---|
| Grand Canyon | 23.2 %, −210 m | 2.1 %, −28 m | 42.2 % | 33.2 % |
| Middle Rhine | 22.1 %, −47 m | 2.4 %, −5 m | 26.8 % | 13.6 % |
| Dhaulagiri (Kali Gandaki) | 9.7 %, −24 m | 0.2 %, −11 m | 37.9 % | 32.6 % |
| Gauja | 8.3 %, −2 m | 0.8 %, −8 m | 8.8 % | 1.6 % |

On the included heights most of the remaining share is the grid's own error in deep valleys, not the rivers. With
loop cut-offs at one water width and no smoothing the laid-out courses stay on the snapped lines: at 1:10 the river
agent's bench measured p90 0 m and p99 2.4 m from the input, against 510 m and 1.3 km for `rivers.bin` today.

**Headless servers** (dedicated 1.22.7, self-test, Earth tab on Automatic):
- 1:100 (500,736), no packs: height 384, log 1000, built-in heights, noise rules 1; Everest y 382; terrain = model in
  99.4–99.8 %; no errors.
- 1:100 with the included heights and rivers on GLO-90 (dev build): "Copernicus GLO-90, included; rivers on GLO-90";
  included heights on every dry-land column sampled at the Gauja, Everest, the Middle Rhine and the Grand Canyon;
  2.04 M river segments indexed in 5.3 s; Everest y 382; terrain = model in 99.4–99.8 %; no errors.
- 1:10 (5,009,920) from the release zips with the four packs: height 1024, linear, full GLO-90 from the local tile
  folder with the included heights behind it, rivers on GLO-90; 2.29 M segments in 14.1 s; columns within 0.2–0.6 m
  of GLO-90 at the Middle Rhine, the Grand Canyon, the Kali Gandaki and Everest (y 1022); no errors.
- Tiled dungeons: "map size 500736 × 384 × 200192 instead of the 256000 × 384 × 102400 seen at start-up"; 0
  out-of-bounds warnings in every run (192,500 in the first session of a tall.4 world).

**In game** on the AgentBridge bench (its own data folder; there is no virtual machine on this computer), Vintage
Story 1.22.7 client with the release zip and the four height packs, new worlds through the Earth play style:
- 1:100 at Everest: the packs were found through `pack-index.json`; "Copernicus GLO-90, included; rivers on GLO-90".
- 1:10 at the Middle Rhine: full GLO-90 (local tiles), height 1024; the Rhine at its real width (about 20 blocks) in
  its gorge; 60 fps. No Earth errors in either world.
- Not checked in game: the create-world screen's new labels and Automatic entries (the bench makes worlds from the
  play style's defaults), multiplayer, Wmap's charts on the new heights.

Open:
- Still sized with the world for rivers on GLO-90: fade lengths, the rapids window (about ±19.5 km at 1:10), the
  waterfall search and the river index span.
- Where GLO-90 has no gorge (the Dadu at 29.3°N 102.95°E, the Usumacinta at 17.27°N 91.43°W) the snapped levels keep
  the river's fall and the runtime carves a slot up to about 1.6 km deep.
- A 97,500 m³/s Amazon branch near Manaus crosses land (the snap's width filter hides the real narrow channel).
- Detail on the included heights (author, 5 November: noise hides the grid's real detail): rules 1 is about a third
  further from GLO-90 than no noise at 13 places; a prototype of gullies down the grid's own slope (rules 2, not in
  any build) keeps most of that accuracy and looks more like terrain. Experiment, not agreed:
  [detail-gullies-2026-11-05](../research/detail-gullies-2026-11-05.md). Evening sweep of gully strength (0–1) and
  noise on top (0.1–0.5) in the same note; the author found Gullies B (1.5) "quite strong".
- Packaging (author, 5 November evening: "water data my mind was separate file (aka mod)"; "river file moves into its
  own mod … due to size limit - not technical"). These were first recorded as a direction to move `rivers-glo90.bin`
  (24.0 MB) out of height pack 4 into a mod of its own. The author corrected that the same evening: "GLO-90 river file
  should move into a mod of its own - no. i meant that earthwaterdata - has river data and now is it duplicate or
  what?" No move was asked for; the question was whether the water pack's `rivers.bin` and `rivers-glo90.bin`
  duplicate each other. They hold the same 619,723 reaches; `rivers-glo90.bin` moves their courses onto GLO-90's valley
  floors and takes its levels from GLO-90. Worlds with `RiverCourses` 1 or later use only `rivers-glo90.bin` (`rivers.bin` is
  still read at start-up and dropped unused); worlds with 0 (before tall.5, released 1.0.x, no height packs) need
  `rivers.bin`, and the water pack's coasts, islands, lakes and shores serve every world. Where `rivers-glo90.bin`
  lives is not decided; it can't join `earthwaterdata` (31.7 MB) under the 40,000,000-byte limit of 0040. The author
  would accept 200 MB of heights ("be can also make it 200mb"). Not done. What the room
  buys, from scratch builds of the whole dataset (heights + roughness, RMS from GLO-90 over all land points;
  [table](../research/glo90-coarse-packs-2026-11-05.md#finer-grids-with-the-rivers-in-a-mod-of-their-own-evening-5-november)):
  24″ (now) 129 MB, 17.2 m; 20″ with coarser steps in the mountains 158 MB in four packs, 14.5 m; 18″ likewise 188 MB in
  five packs, 13.0 m. Not chosen.
- Rebuilt GLO-90 instead of a grid (author, 5 November evening: about 200 MB, rebuilt on the player's PC and saved to
  disk once, "to avoid large download"; not full GLO-90 but "much better res" than a grid of that size). First
  measurement: JPEG 2000 with the bytes shared by need is 7.8 m RMS at 200 MB against 13.0 m for the 18″ grid, and
  about 3 times closer in high mountains; a learned codec is written, not trained. Paused for CPU. Experiment, not
  agreed: [glo90-rebuild-2026-11-05](../research/glo90-rebuild-2026-11-05.md).

**Smaller worlds on GLO-90** (tall.10; scratch harness `scalecheck` with the mod's own terrain model and the whole
store). Eight 40 km windows: the Alps (Bernina), Everest, the Sognefjord, the Netherlands, the Grand Canyon, Aconcagua,
the Scottish Highlands and the Loess Plateau; dry land away from rivers. The truth is GLO-90's mean over each block's
footprint; RMS error in metres, and the roughness ratio (how much neighbouring blocks differ, against the truth's):

| Earth size | Included GLO-90 | Full GLO-90, one point (rules 3) | Full GLO-90, block mean (rules 4) |
|---|---|---|---|
| 1:50 | 4.8–96.9 m, 2.2–2.8 | 0.5–3.8 m, 1.02–1.21 | (one point, as rules 3) |
| 1:100 | 4.7–94.5 m, 1.8–2.3 | 0.6–5.0 m, 1.03–1.30 | 0.4–3.6 m, 1.02–1.18 |
| 1:200 | 2.7–69.2 m, 0.38–0.88 | 1.2–9.5 m, 1.04–1.42 | 0.3–3.6 m, 1.01–1.11 |
| 1:400 | 2.1–58.5 m, 0.58–0.94 | 2.1–21.9 m, 1.06–1.53 | 0.2–3.0 m, 1.01–1.06 |

- The low ends are the Netherlands; the included grid's high ends are Everest.
- The included grid is too rough at 1:50 and 1:100 (its noise) and too smooth at 1:200 and 1:400.
- One point per block at 1:200 and 1:400 is one of 5–20 GLO-90 points under the block, so neighbouring blocks differ by
  chance: flat land up to 1.5 times as rough as it is. The block mean removes that.
- In blocks, rules 4 is 0.18–0.77 blocks RMS from the truth at every size.
- Cost: 0.5–2.4 ms per 1,000 columns on full GLO-90 with rules 4, against 0.2–2.0 ms with one point.
- In game on the AgentBridge bench (tall.10, 1:400, height 384, an Alps start): valley floors and rivers look right. On
  steep slopes the columns stand as needles: at 1:400 a block is about 400 m across but about 25 m up, so a mountain
  side rises several blocks per block. That is the world's vertical stretch (World height, Mountain heights), not the
  data. Open; the author's call.
- Not checked: a server's chunk times at 1:400 with a large view distance (the 24-tile cache may thrash), the creation
  screen's new labels in game (the bench makes worlds from the play style's defaults and can't reach the main menu).

## History

- 5 November 2026: proposed and implemented in 1.0.3-tall.5 overnight at the author's request; checked on headless
  servers and in game on the AgentBridge bench (above). Not committed, agreed or released.
- 5 November 2026, 07:34 Europe/Riga: installed at the author's request ("Yes, install tall.5").
  `earth-1.0.3-tall.5.zip` (sha256 `628ffd55…2aadfd0b`) replaces tall.4 in `VintagestoryData/Mods`, with the four
  new packs `earthheightdata1`–`4-1.0.3-tall.5.zip` (`36992775…90032126`, `08529f30…b05cb5a7`, `1272f36b…df659afa`,
  `49600fc4…89ee9870`); each installed file's hash equals the release file the bench ran. tall.4 (`3020ca8d…`) moved
  to `VintagestoryData/Backups/earth-before-1.0.3-tall.5-20261105-073421`. `earthterraindata` and `earthwaterdata`
  1.0.2, companions, settings and saves unchanged; the game was not running. Existing worlds keep their old rules
  (the new EarthParams fields are 0 in their saves).
- 5 November 2026: the author: "i think linear only above 512 or more". 1.0.3-tall.6: Mountain heights on Automatic is
  linear only in worlds 512 and more blocks high (EarthWorldOptions.AutoCurveFor by the map's height), so 1:50 on
  Automatic gets balanced relief; the world-option checks for it pass. Only the Earth zip changes
  (`release/earth-1.0.3-tall.6.zip`, sha256 `002c02e9…048b5ca5fe`, packed like tall.5); the height packs stay tall.5.
  Not installed, not run on a server.
- 5 November 2026, evening: the author: "Fix the full res first". 1.0.3-tall.7 = tall.6 + GLO-90 rules 3 (decision
  item 7): `release/earth-1.0.3-tall.7.zip` (28,603,071 bytes, sha256 `cd266fa3…29000ea18`, packed like tall.6; height
  packs stay tall.5). It also carries the gully prototype's code, which no world can reach (DetailRulesVersion stays
  1). `tests/ElevationTests` pass with the new smooth checks; on the AgentBridge bench new 1:10 worlds at the
  Nærøyfjord (tall.5 against tall.7) and at Everest (tall.7) load on full GLO-90 (screenshots in the note). Found on the
  way: the author's world "vintage adventure lands" (last on tall.3) is on the built-in heights, not GLO-90, and keeps
  them. Not run on a server.
- 5 November 2026, 20:07 Europe/Riga: tall.7 installed at the author's request ("Yes, install tall.7"):
  `earth-1.0.3-tall.7.zip` (`cd266fa3…29000ea18`, equal to the release file) replaces tall.5 in
  `VintagestoryData/Mods`; tall.5 (`628ffd55…`) moved to
  `VintagestoryData/Backups/earth-before-1.0.3-tall.7-20261105-200755`. The four height packs stay tall.5, as
  tall.7's dependencies ask; other mods, settings and saves unchanged; the game was not running.
- 5 November 2026, 20:36: 1.0.3-tall.8 = tall.7 + the random start's catalogue on the included heights in worlds on
  full GLO-90 (0051 Verification: 10.7 s instead of 235 s offline; the author's new 1:10 world on tall.7 took about
  6 minutes to load). `release/earth-1.0.3-tall.8.zip` (28,603,585 bytes, sha256 `de6e6e96…adcd39e71030`, packed
  like tall.7; height packs tall.5). Built, not installed.
- 5 November 2026, 20:52: 1.0.3-tall.9 = tall.7 + the random start checking terrain only on the cells it draws
  (0037, Selection on demand; the author: "does it need to?"), replacing tall.8's workaround: 0.0–3.2 s on the whole
  GLO-90 store. `release/earth-1.0.3-tall.9.zip` (28,603,412 bytes, sha256 `9d01bbd1…c5f8a539ed`, packed like tall.7;
  height packs tall.5). tall.8 (the catalogue on the included heights) was built and never installed.
- 5 November 2026, 21:05:11 Europe/Riga: tall.9 installed at the author's request ("Yes, install tall.9"), once the game
  had closed: `earth-1.0.3-tall.9.zip` (`9d01bbd1…c5f8a539ed`, equal to the release file) replaces tall.7 in
  `VintagestoryData/Mods`; tall.7 (`cd266fa3…29000ea18`) moved to
  `VintagestoryData/Backups/earth-before-1.0.3-tall.9-20261105-210511`. The four height packs stay tall.5; other mods,
  settings and saves unchanged.
- 5 November 2026, late evening: 1.0.3-tall.10 = tall.9 + 0051's data release as the default source with the
  Copernicus fallback and the dialog's source dropdown, and items 8–10 (GLO-90 only, rules 4, the Earth tab cleaned
  up), the Earth tab's GLO-90 land heights (0051) and the story-location searches on the included heights (0051
  Verification). `tests/ElevationTests` pass; `tests/EarthWorldOptionsTests` pass up to their data pack checks, which
  fail because `release/earthterraindata-1.0.2.zip` is not in this worktree (as before tall.10).
  `release/earth-1.0.3-tall.10.zip` (28,617,933 bytes, sha256 `8011eda1…a9dda90`, packed like tall.9; height packs
  stay tall.5). Built, not installed.
- 5 November 2026, late evening: 1.0.3-tall.11 = tall.10 + decision 0054's animals as around 1318 with "Chickens
  everywhere" (`claude/fauna-1318` `a4e2382`, merged file by file into the working tree; the author: "With the tall
  release"). The Earth tab's Nature group gains "Nature · Chickens everywhere". `release/earth-1.0.3-tall.11.zip`
  (28,647,850 bytes, sha256 `0e621572…62fa1941d8`, packed like tall.10). Built, not installed.
- 5 November 2026, 22:47:48 Europe/Riga: tall.11 installed at the author's request ("Yes, install tall.11"); the game
  was not running. `earth-1.0.3-tall.11.zip` (`0e621572…62fa1941d8`, equal to the release file) replaces tall.9 in
  `VintagestoryData/Mods`; tall.9 (`9d01bbd1…c5f8a539ed`) moved to
  `VintagestoryData/Backups/earth-before-1.0.3-tall.11-20261105-224748`. Its dependencies are installed: the four
  height packs and the terrain and water packs at the versions it asks for, Cylinder World 1.0.4 and Hydrology
  0.6.2-rivers.7 (it asks for 1.0.3 and 0.6.1 or later). Other mods, settings and saves unchanged. tall.10 was never
  installed. Existing worlds keep their saved rules; new worlds get GLO-90 rules 4, the Earth tab's new rows and the
  1318 animals.
- 5 November 2026, late evening: 1.0.3-tall.12 = tall.11 + items 11–13: a play style per scale (1:200 to 1:10, 1:400
  off the tab), Mountain heights back on the Earth tab without Automatic, each size's width in its label, Download all
  from Earth's data release by default, and channel rules 3. Lanes of every exit channel at 1:100 on the tall line's
  code (scratch probe after 0052's `lanes`; same model, rivers on GLO-90 and routes for both height sources): rules 2
  leave 16,345 lane columns in 116 channels sea on the built-in heights and not on GLO-90 (15,559 of them dry land; 427
  on the centre line in 50 channels) and turn 53,054 columns in 94 channels to sea; rules 3 leave 786 in 25 channels,
  none of them dry land (river or lake water instead of sea; 11 on the centre line in 3 channels), and 592 in 42. The
  world option tests pass (with the play style checks) up to DataPackChecks, which still stops at the 1.0.2 data packs
  missing from `release/`; EarthMapTests: one failure, "4M dalmatia: the rocky coast rises steeply", as in the 20:52
  run before these changes. `release/earth-1.0.3-tall.12.zip` (28,648,314 bytes, sha256 `1cf0b057…aedd8872`, packed like
  tall.11). Built, not installed. On the AgentBridge bench (its own data folder, a complete GLO-90 store) a new world
  from `preset-earth-25` was an Earth world: 2,002,944 × 801,280 blocks, world height 512 from the Earth tab, Mountain
  heights `linear`, rivers on GLO-90, spawn 42.67° N 92.73° W, and its Download all found all 26,475 squares there and
  marked itself done. Vanilla's `/worldconfig earthWorldHeight` printed 0 while the log shows 512 applied. The creation
  screen itself was not looked at.
- 5 November 2026, late evening: 1.0.3-tall.13 = tall.12 + [0055](0055-lake-outlet-tongues.md)'s lake outlets, taken
  from `28f92a1` (`claude/lake-outlets`, release/1.0) by a three-way merge of each file (base `bbd00c0`); in
  `EarthParams` both new fields stay (99 ChickensByRange, 100 LakeOutlets). The river release works on either river
  file, so new worlds with rivers on GLO-90 get it too. Niagara on the tall line's new-world settings (scratch probe:
  GLO-90 heights, rivers on GLO-90), outlets off → on: at 1:100 Chippawa and the Grand Island west channel go from Lake
  Erie to river and the Tonawanda channel to land; lake columns within 500 m of the river from Erie's outlet to Lake
  Ontario 3,355 → 740. At 1:10 the gorge, Chippawa and Tonawanda points are land and Grand Island west river, 9,273 →
  2,014 lake samples (every 6 blocks). Lake Erie stays a lake at both. EarthMapTests with outlets: 0055's checks pass
  (Erie a lake, the four Niagara points not Erie, no lake water above the river below 14 falls); the one failure is
  still Dalmatia. `release/earth-1.0.3-tall.13.zip` (28,653,357 bytes, sha256 `508f53a8…8f541009`, with
  `earthdata/outlets.bin`). On the bench a new world from `preset-earth` loaded the data from the tall.13 zip with no
  errors; `/earth info` gives land at Tonawanda (lake without the outlets) and river water at Chippawa and Grand Island
  west (the command names river water "lake", as the tests' water 2). Not played there; built, not installed.
- 6 November 2026, 07:34:17 Europe/Riga: tall.13 installed at the author's request ("Yes, install tall.13"); the game
  was not running. `earth-1.0.3-tall.13.zip` (`508f53a8…8f541009`, equal to the release file) replaces tall.11 in
  `VintagestoryData/Mods`; tall.11 (`0e621572…62fa1941d8`) moved to
  `VintagestoryData/Backups/earth-before-1.0.3-tall.13-20261106-073417`. Its dependencies are the same as tall.11's and
  were already installed. Other mods, settings and saves unchanged. tall.12 was never installed. Existing worlds keep
  their saved rules; new worlds get the play styles per scale, Download all by default, channel rules 3 and lake
  outlets.

- 7 November (late morning): tall.14 built (`claude/tall-glo90`, uncommitted): tall.13 plus item 14, the water atlas's
  fixes from release/1.0's `717fb18` (the commits after it there are the atlas's own notes and tools, and the side arms
  of `e5d1d75`, not taken). `SeaChannels` merged from the atlas by hand around rules 3's `LowLand`; the routes' fine
  refinement runs in the parallel workers. Overlays made by `tools/water_overlays.py` from the published 1.0.2 data and
  the atlas's files and checked to give them byte for byte (in Python, and in C# through `EarthData`: grid tiles,
  islands, coasts and all 619,723 reaches the same): `sea-referee.bin` 3,734,898 bytes, `rivers-glo90-snap.bin`
  4,510,852 bytes. A world on the atlas's own files needs neither, and data that fits neither gets neither (logged).
  - A first cut gave rules 4 only to worlds with moved courses, after the full suite failed the Mekong outline with
    `rivers.bin` and rules 4; that suite ran at its default height 256, and a coasts-only check at 384 had passed.
    Measured again: rules 4 fails the Mekong at 256 with either courses (99.4 %) and passes at 320 and 384 with either,
    so the cut was dropped and every new world gets rules 4 (item 14, open point on lower worlds).
  - Suite at height 384 (`. 384`): with rivers on GLO-90 moved (COURSES=2) 3 of 310 checks fail: Lake Geneva's steps
    (old; tall.13's settings fail it too) and the two that release/1.0's atlas has open, the Tagus islands (13 of 915
    samples) and one water wall at San Carlos Bay (26.4503 N, 82.0661 W: top 161 beside sea at 164, by a 160 m³/s
    river's channel). With `rivers.bin` (built-in heights) and with tall.13's settings (courses 1, rules 3, no referee)
    only Lake Geneva. At the default height 256, rules 4 also fails the Mekong outline (item 14), beside 4M Dalmatia's
    old failure there.
  - Model at 1:100 (500,736, height 384, GLO-90, rivers on GLO-90, lake outlets): built in 5.2 s with tall.13's
    settings, 11.2 s with tall.14's (alone on the machine; at 1:10, height 1024: 13.4 → 17.7 s). 975 exit channels:
    rules 3 may cut low land anywhere on their 68,291 km; with rules 4, 962 take the fine way and 43 cross land over 33
    km. Niagara unchanged (Erie lake, Chippawa and Grand Island west river, Tonawanda land). Sea → land at Patagonia A
    and B (-51.61, -72.87 and -51.51, -73.24), the Rakaia mouth and Maewo; sea in a 5 km square around each 80 / 87 / 39
    / 86 % → 4 / 29 / 0 / 19 %. The probe points in the old Rhine–Maas, Ems and Scheldt cuts are land again (y 109–110).
  - Zip `release/earth-1.0.3-tall.14.zip` 36,910,780 bytes, sha256
    `a1ebcf47f34ebb4ee09fe8bf192c41155fba1b3db5c265bb288e4aab3e65ecdf` (the two overlays added to tall.13's entries;
    data packs unchanged). Loaded through the published packs (earthterraindata and earthwaterdata 1.0.2, height packs
    tall.5), it reads as the worktree's folder: grid, islands and coasts with the referee off and on, and the reaches of
    courses 0, 1 and 2 the same (the options suite's pack check stops earlier, as for every tall zip, on `earth.json`
    without `dataPacks`). Bench (AgentBridge, world t14s100, play style earth): the log says "rivers on GLO-90, moved
    into the water mask's rivers; sea refereed by the water mask", no errors; at 51.61° S 72.87° W `/earth info` gives
    land at 1,248 m (top y 206), mountains with snow and ice from above, where tall.13 had sea. Not installed.

- 7 November (afternoon): tall.15 built (`claude/tall-glo90`, uncommitted): tall.14 plus item 15 and release/1.0's
  `72af35f` (rules 4's erosion only without outline coasts). The author, of the land tall.14 still had where the mask has
  sea in the Patagonia window: "Do whatever that creates real terrain".
  - `tools/sea_ocean.py` (new; `sea_referee.py` and `water_atlas.py` copied from release/1.0 unchanged) scanned the
    6,414 GLO-90 tiles with Earth sea and wrote terrain.bin, islands.bin and coasts.bin with the referee's results in
    one pass (17 min on 11 workers). A one-way first cut (opening only) cut away islands GSHHG draws off their place;
    the added side moves them. A second cut without counterparts lost 40,616 small islands (Donousa, Silhouette,
    Visokoi among them, real islands the mask lacks), so the counterpart rule was added before anything was built on
    it. Checked by pictures of single tiles: Roatán, Tanna, Taveuni moved; Donousa, Silhouette and North Island, Minto
    Reef kept; the Indus delta, Khor Musa and the old Yellow River delta show flats opened.
  - `tools/water_overlays.py` writes `sea-referee.bin` version 2: 6,262,394 bytes (budget 6,824,118 for the main zip
    under 40,000,000), 18,251 grid cells, 326 island patches, 54,745 islands new or changed, 4,416 coast tiles; its own
    check and the C# loader (`EarthData`, through `SeaRefereeData`) give the written files exactly: every grid cell,
    all 167,555 islands, the 152,639 patches and the 64,800 coast tiles; with the referee off the published grid.
    `rivers-glo90-snap.bin` unchanged.
  - Patagonia window at 1:100, height 384 (the bench's view, 640 × 640 blocks): land where the mask has sea 130.1 km² on
    tall.14 → 6.3 km²; sea where the mask has land 36.3 → 43.0 km²; none of either more than 2 blocks from the shore.
    Land where the mask has a lake or river unchanged (49.8 km²).
  - Suite (`. 384`): with rivers on GLO-90 moved (COURSES=2) the 3 of 310 known failures (Lake Geneva's steps, the
    Tagus islands, the water wall at San Carlos Bay); with `rivers.bin` only Lake Geneva. At 256 only 4M Dalmatia's
    steep rise (old; fails the same on `bbd00c0`): the Mekong outline now holds (100 % at 512k and 4M). Two of the three
    parallel runs stopped after their checks, writing the same render in `tests/out`.
  - Zip `release/earth-1.0.3-tall.15.zip` 39,441,144 bytes, sha256
    `487f0c90b5ee5e4f1f9129a01c95974f2ed2704cd5725312dbe746164e65fd8f` (tall.14's entries; data packs unchanged).
    Loaded through the published packs it reads as the worktree's folder (sea referee 2 available, grid, islands and
    coasts with the referee off and on, reaches of courses 0, 1 and 2). Bench (AgentBridge, world t15s100, play style
    earth): log as tall.14's, no errors; `/earth info` gives land at Patagonia A (1,248 m), Donousa (187 m) and
    Silhouette (321 m); sea in Laguna Superior, the Ébrié lagoon and the Limfjord's Nissum Bredning; at Tanna and Roatán
    sea on GSHHG's side and land on the mask's. Bench Mods and ModConfig restored byte for byte.
  - Installed 13:38 at the author's request ("commit , install"), with the game not running:
    `earth-1.0.3-tall.15.zip` (`487f0c90…4e65fd8f`, equal to the release file) replaces tall.13 in
    `VintagestoryData/Mods`; tall.13 (`508f53a8…8f541009`) moved to
    `VintagestoryData/Backups/earth-before-1.0.3-tall.15-20261107-133820`. Dependencies as tall.13's, already
    installed. tall.14 was never installed. Existing worlds keep their saved rules (tall.13's: courses 1, channel rules
    3, no referee); new worlds get item 14's fixes and item 15's coastline. Committed with the tall line on
    `claude/tall-glo90`.

- 7 November (late afternoon): release/1.0 merged into the tall line and tall.16 built. The author: "Merge both to get
  right land, water and rivers" (and, relayed from the water atlas session: "we merge on release").
  - Merge `3fc1d7d` (release/1.0 at `72af35f` into `claude/tall-glo90` at `5207611`): release/1.0's code (0055,
    the water atlas, `72af35f`) was already on the tall line by hand, so all eight conflicts resolve to the tall side and
    `src` is tall.15's. From release/1.0 it brings the water atlas's tools and notes. Its rebuilt `terrain.bin`,
    `islands.bin`, `coasts.bin` and `rivers.bin` are not taken: the published 1.0.2 files stay, the difference files'
    hashes name them as their base, and release/1.0's own notes name the risk of unversioned data to existing saves.
  - Item 16: `tools/river_arms.py` (release/1.0, unchanged) on the moved `rivers-glo90.bin` (the water atlas's
    `rivers-glo90-wbm.bin`, equal to `rivers-glo90.bin` with `rivers-glo90-snap.bin`, by hash) in 7 min on 12
    workers: 2,408 arms, 19,669 km, 623,995 reaches. `tools/water_overlays.py --armed` writes `rivers-glo90-arms.bin`
    (185,836 bytes; the rebuilt snap file equals the committed one) and rebuilds the armed list from the moved one
    exactly, in flow order.
  - Zip `release/earth-1.0.3-tall.16.zip` 39,628,689 bytes, sha256
    `bf95cfabb09a04aedc84027b4690e0ea1e02689fc94510bc49357063763ba721` (tall.15's entries and the arms file; data
    packs unchanged). Read through the published packs it gives new worlds courses 3 and equals the worktree's folder
    for courses 0–3 and the grid, islands and coasts; courses 3 equals `river_arms.py`'s file in all 623,995 reaches,
    none flowing into an earlier one.
  - River audit (harness `audit`, 1:10, height 384): rules 2 and 3 both 99.91 % of the centre line in water and 422
    blocked; running uphill 190 → 265 and falls of 2+ blocks 258 → 263, the arms' one-block steps where they leave or
    rejoin their river (release/1.0's own audit on `rivers.bin`: 88 of its 163).
  - Suite (`. 384`): with COURSES=3 and with COURSES=2 the same 3 of 310 known failures as tall.15 (Lake Geneva's
    steps, the Tagus islands, the water wall at San Carlos Bay). A first run with rules 3 failed the 15 named river
    ends besides: those checks looked reaches up by number in `rivers.bin`, which rules 3's list no longer shares; they
    now read the list the model was built from.
  - Bench (AgentBridge, world t16s100, play style earth, 1:100): the log says "rivers on GLO-90, moved into the water
    mask's rivers, with their side arms; sea refereed by the water mask", no errors; seen from above, the Daugava divides
    round Doles sala and joins again below it; at the arm's middle vertex the player stood in river water. Bench Mods,
    ModConfig and settings restored byte for byte.
  - Committed as `0266b74` on `claude/tall-glo90`; release/1.0 fast-forwarded to it, so both branches are the same.
  - Installed 14:20 at the author's request ("Yes, install tall.16"), with the game not running:
    `earth-1.0.3-tall.16.zip` (`bf95cfab…763ba721`, equal to the release file) replaces tall.15 in
    `VintagestoryData/Mods`; tall.15 (`487f0c90…4e65fd8f`) moved to
    `VintagestoryData/Backups/earth-before-1.0.3-tall.16-20261107-142001`. Dependencies as tall.15's, already
    installed. Existing worlds keep their saved rules (tall.15's: river courses 2, without the arms); new worlds on GLO-90
    get the side arms.

- 7 November (evening): tall.17 (item 17), combined with release/1.0 up to `7edb1c7` and split into three zips (item
  17, Combined build and packs). The author: "Take all lakes …", "Yes, combine", "split and coordinate with 'Ore maps'
  session on that". Lakes `9b38ba8` and `1e64b42`, merges `0ac769e`, `c0ce27f` and `857e094`, split `3fdd848` on
  `claude/tall-glo90`; not pushed, release/1.0 not moved.
  - Bench as in item 17. Suite (`. 384`, COURSES=3): the same 3 of 310 known failures as tall.16.
  - Installed 18:35 at the author's request ("Yes, install tall.17"), with the game not running:
    `earth-1.0.3-tall.17.zip` (`241c9d43…d8a7f819`), `earthwaterfixdata-1.0.3-tall.17.zip` (`5cb33584…68443127`) and
    `earthgrounddata-1.0.3-tall.17.zip` (`a75958ef…e484abca`), each equal to the release file, in `VintagestoryData/Mods`;
    tall.16 (`bf95cfab…763ba721`) moved to `VintagestoryData/Backups/earth-before-1.0.3-tall.17-20261107-183553`.
    `lakes-glo90-1.bin` (`8f0075ac…79236384`) copied into `VintagestoryData/ModData/earth/glo90`, so new GLO-90 worlds
    read it there; it is not yet on the `earth-glo90` release. Other mods, settings and saves unchanged. Existing worlds
    keep their saved rules (the grid's lakes); new worlds on full GLO-90 get the lake outlines and the biome rules.

- 7 November (night): tall.18 (item 18), rivers from the water mask's outlines. The author, on tall.17's world map at
  Riga in a 1:10 world: "test of game and river looks to be wrong. straight lines and not in the correct path", then
  "Yes, build them". Committed as `eabf485` on `claude/tall-glo90`; not pushed.
  - Suite (`. 384`, COURSES=3): the same 3 of 310 known failures as tall.16 and tall.17, and the 4 new checks of
    `NearestCourse` and the river file pass; without COURSES the suite equals that of the commit before
    (`77516a3`) but for the new checks.
  - Bench and packs as in item 18.
  - Installed 20:08 at the author's request ("Yes, install tall.18"), with the game not running:
    `earth-1.0.3-tall.18.zip` (`07fd4542…68cdd72a`, equal to the release file) in `VintagestoryData/Mods`; tall.17
    (`241c9d43…d8a7f819`) moved to `VintagestoryData/Backups/earth-before-1.0.3-tall.18-20261107-200837`.
    `rivers-outline-glo90-1.bin` (`52a0c79e…49f3807b`) copied into `VintagestoryData/ModData/earth/glo90` beside the
    lake file, so new GLO-90 worlds read it there; it is not yet on the `earth-glo90` release. Earth Water Fix Data and
    Ground Data stay at tall.17 (equal to their release files). Other mods, settings and saves unchanged. Existing
    worlds keep their saved rules (the courses' channels); new worlds on full GLO-90 get the river outlines.

- 7 November (night): item 19 proposed with a prototype (dams taken out of GLO-90): `tools/bake_dam_removal.py`,
  `src/Core/DamRemoval.cs`, `EarthParams.DamRules` (105, given to no world) and a baked file for Riga and the
  Yangtze, probed offline ([note](../research/dam-removal-2026-11-07.md)). Suite at 256 and 384 (with and without
  COURSES=3): the known failures only. Uncommitted; not built into a test build, not installed.
- 7 November (night): item 19's global bake, at the author's word: `bake_dam_removal.py --global` (every reservoir,
  in groups that keep a cascade together), a foot no lower than the ground just below the dam, and the river outlines
  rebaked from its regions. Probed offline at the prototype's six places (same counts) and eight new dams. Files in a
  scratch folder only: not published, not installed, nothing committed.
- 8 November (after midnight): item 19's second pass. The author asked to close the first pass's open points ("can you
  close them?") and to wire it ("Yes, wire it"); the tall line's session asked for the bridges in the same file.
  - River corridors through the old reservoirs and across their dams, in the version 2 river outlines with the
    bridges the mask has as land.
  - Jebel Aulia's region whole, on mosaics up to 15 × 15 squares.
  - The first pass's wall rule kept: the downstream face and a floor fill were tried, and both left walls higher.
  - New worlds on full GLO-90 with river courses 3 get `DamRules` 1 and `RiverOutlines` 2.

  Rebaked globally, probed offline at 16 places. The suite at 256 and 384 (with and without COURSES=3) has its known
  failures only. Committed on `claude/tall-glo90` at the author's word ("Commit work and merge as main manager",
  relayed by the tall line's session). The two files are in a scratch folder: not published, not installed.
- 8 November (night): item 19 in combined test build 1.0.3-tall.21 (`03dddab`; release/1.0 fast-forwarded to it, not
  pushed), with volcano bodies (0057), ores in their layers (0060) and the water table (0059). Suite at 256: all checks
  pass. On a private bench (a new 1:10 world) the log reports the dams out in 2,703 squares (75,925 course stations
  lowered) and the rivers from `rivers-outline-glo90-2.bin`. Riga's Southern Bridge (56.9119 N 24.1603 E) is open
  water, its bed at y 102 under water at 109, and the Daugava at Pļaviņas runs as a river in its valley. Installed in
  the author's game at their word ("we will install when it is merged"): Earth's zip, and `dams-glo90-1.bin` and
  `rivers-outline-glo90-2.bin` copied into the player's `ModData/earth/glo90` beside the version 1 files; hashes
  checked. Still not published on `earth-glo90`.
- 8 November: dam rules 2 ([0063](0063-rivers-follow-the-real-channel.md) rule 3) in `tools/bake_dam_removal.py`
  version 2 and `DamRemovalData`, which reads `dams-glo90-2.bin` for `DamRules` 2. Rules 1 worlds still read
  `dams-glo90-1.bin`.
  - Baked globally on courses rules 3's list for testing; probed offline at eight places.
  - Final bake on courses rules 4's list (5,142,018 bytes); new worlds on full GLO-90 get dam rules 2 with courses 4,
    river outlines 3 and channel rules 1 (test build tall.24).
  - Nothing published or installed.
- 8 November 2026: item 20, GLO-90 rules 5 (new worlds), after the author's report of sand squares near Suez ("Fix.").
  On `claude/biomes-1.0`, offline only (probe and tests); not in a test build, not installed.
