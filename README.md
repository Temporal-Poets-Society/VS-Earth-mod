# Earth 2.0.0-rc.12 — Vintage Story 1.22

**2.0.0-rc.12 (release candidate, 13 November 2026, not released):** rc.11 and a new Earth tab choice, **Biomes:
Earth / Vanilla** ([decision 0075](docs/decisions/0075-biomes-back-to-vanilla.md)): Vanilla turns Earth's biome rules
(vegetation zones, wetlands, fire openings, ground cover, ground moisture, winter grass, alpine surfaces) back to
vanilla's for a new world; terrain, rivers, coasts, rocks and ores stay Earth's. It works with either Nature choice.

**2.0.0-rc.11 (release candidate, 13 November 2026, not released):** rc.10 and the open issues that change new
worlds' terrain, for new worlds only.
- **Rivers ([decision 0074](docs/decisions/0074-one-width-along-rivers-no-mask-ponds.md), channel rules 5):** one width along a river,
  without the water mask's chains of ponds (the Lielupe).
- **Climate fix v3 (decision 0018):** rain blended over a 1° coast band, so the climate grid's rain stripes (Arctic
  coasts, Central America, SE Asia) are gone from soil and vegetation: 227 stretches down to 67.
- **The sea of 1318 (works rules 4):** Maasvlakte 2 is sea again; the Corinth, White Sea–Baltic and Ismailia canals are
  undone (the Nieuwe Waterweg is kept: it follows open water of 1250).
- **Coasts (shore rules 3):** steep scoured rocky coasts (Bergen) are bare rock, not till.
- **Ore maps (experimental):** fire clay no longer put back under coal (ore layers v2); the Urals' and Kondyor's
  platinum placers place ore in their valleys (ore rules v5).

**On `main` after rc.10 (new worlds only):** rivers keep one width along them
([decision 0074](docs/decisions/0074-one-width-along-rivers-no-mask-ponds.md), channel rules 5). The water mask draws a
river 2 or 3 of its 90 m pixels wide by turns, so the Lielupe and the Oder came out as chains of ponds; now a widening
shorter than five widths is land, and the channel's width and bed are even along the river (also at 1:100). Real wide
reaches, side arms, backwaters, lakes and rivers wider than about 450 m stay as the mask has them.

**2.0.0-rc.10 (release candidate, 13 November 2026, not released):** rc.9 and the author's 13 November river report, for
new worlds only.
- **Rivers ([decision 0073](docs/decisions/0073-river-water-one-bed-and-flow-reach-by-reach.md), channel rules 4):** river
  water is one bed (no pits, raised patches or straight edges where the mask, the course and lakes meet, as at the
  Daugava's Ķīšezers connection); side channels around islands stay open (the Sarkandaugava at Kundziņsala); water is
  still or flowing reach by reach, so nothing flows into a dead end and lakes don't flow out like rivers.
- **Weather rules 6:** cloud decks over cold seas (Peru, Namibia), hail from Earth's storms, the weather in the
  temperature; the same weather on every operating system.
- **Terrain fixes:** no crease where the noise's dips switch near a water level (GenFixRules 2, Lake Geneva); the sea of
  1318 over Rotterdam's port has no ridges along today's moles (works rules 3).
- **Rain along coasts (climate fix v3, [decision 0018](docs/decisions/0018-climate-stripes.md)):** the land's rain fades
  into the ocean data over 1° instead of switching within one row, so the rain index, and the soil and plants it picks,
  no longer step in straight lines along the Arctic coasts, Central America and SE Asia (227 such stretches, now 67,
  nearly all real relief).

**2.0.0-rc.9 (release candidate, 13 November 2026, not released):** rc.8 and one fix, for new worlds only. A new Earth
world saves its own equator-to-pole distance as `polarEquatorDistance` (about 10,000 km over its scale: 100,048 blocks
at 1:100, 1,001,936 at 1:10), also when the Earth tab changes the size, so Rough Seas' metres per block and anything
else that reads it match the map ([decision 0072](docs/decisions/0072-polar-equator-distance-from-the-scale.md)). It was
100,000 at every scale before. Latitude, climate, seasons and daylight do not read it and are unchanged.

**2.0.0-rc.8 (13 November 2026, not released):** new worlds only.
- **River mouths ([decision 0071](docs/decisions/0071-river-mouths-without-dredging.md)):** no dredged ship channel out
  to sea where the coasts are drawn from outlines (1:100 and finer), and a river's bed and the sea floor meet as one bed,
  without a cut edge or patches (the Daugava mouth at 1:10).
- **River water ([decision 0073](docs/decisions/0073-river-water-one-bed-and-flow-reach-by-reach.md), on `main` after
  rc.9):** one bed across wide river water (no squares, straight edges, rings or grooves where courses meet or pass), small
  pools in a river joined to it, side channels the flow passes through kept open (the Sarkandaugava round Kundziņsala),
  channel sides no steeper than 2 blocks a block, and still or flowing water reach by reach: a lake's slow outflow is still
  (the Mīlgrāvis from Ķīšezers), a river flows as a whole, dead ends are still.
- **Weather for other mods:** Earth's weather role in the open sea contracts (`seacontracts:weather`) claims itself
  again if it goes missing, as the contract asks; seen in a game with Sea 0.6.0. Rough Seas or any other mod can read
  Earth's wind, storms and rain without Earth's DLL.
- The 11 November code review's fixes (GenFixRules 1); offline tests compile and pass again (one known lake-shore check
  on the built-in heights, EA-0040).

**2.0.0-rc.7 (release candidate, 12 November 2026, not released):** rivers, canals and shores from the author's rc.6
play in a 1:10 world ([decision 0069](docs/decisions/0069-river-flow-field-canals-and-shore-zones.md)); new worlds only.
- **River flow:** the water mask's river water flows as solved on it (`rivers-flow-glo90-1.bin`, baked once from the
  courses and their discharges): dead-end arms and backwaters are still water, the current turns smoothly instead of in
  patches, wide water carries the river's depth across its whole width (no groove along the course), and a mouth reach
  that went to the sea across land while its own water reached the sea elsewhere follows its water to its own mouth (the
  Lielupe's straight cut at Jūrmala and the Daugava's second mouth at Vecāķi are gone).
- **Dry rivers:** a river is dried in a hot desert only where GIRES (Messager et al. 2021) has it stopping 30 days a year
  or more; the Seymareh and the Karkheh flow again (`rivers-nonperennial-2.bin`, one class per river reach, 20 kB, in
  the mod).
- **Reservoirs:** Global Dam Watch's reservoirs that HydroLAKES lacks are left out too (7,233 more, the Seimare Dam of 2013
  among them): dam rules 3, lake outlines 2, river outlines 4.
- **Canals after 1318:** 26 canals are land again (the Kiel Canal, the North Sea Canal, the Karakum, ...; OpenStreetMap's
  outlines; works rules 2). Natural lakes and rivers along them stay.
- **Shores:** no trees or shrubs on beaches, a low sea coast's first 40 m, a river's low bank or a lake's sedge shore;
  no trees on the next 60 m of a coast; the forest thins in beyond; no tall grass on beach sand.
- **Beaches ([decision 0070](docs/decisions/0070-shore-strip-and-beach-material.md)):** the sea's and big lakes'
  shores are a beach strip of real width at any height (sand about 40 m, gravel 20 m, rock 8 m), so soil no longer runs
  into the water where GLO-90 reads the shore high (Jūrmala: 9 m). Its material is OpenStreetMap's where a beach there
  says (sand, gravel, shingle, rock, mud; `shores-osm-1.bin`), else the coast's ground: dunes and coastal deposits sand,
  till gravel and sand in patches, bedrock and cliffs bare rock, marshes mud. No grass or land plants grow on it.
  `shores-osm-1.bin` (2.2 MB) is fetched from the data release `ed-v1` with the other server data.
- **Fish in rivers:** vanilla's freshwater fish spawned only in still water, and Earth labels a river's top water with
  its flow (`water-ne-7`), so no river had fish (a player: "I did not see any fish spawn in the rivers"). An asset patch
  (`assets/earth/patches/fish-rivers.json`) lets them spawn in full flowing water and rapids too; on the bench, 21
  freshwater fish along the Daugava, a carp in its flowing water.
- **River width (the "ponds"):** the mask has the Lielupe two or three 90 m pixels across all along; where it was two the
  course drew its flow's 90 m, where three the mask's own 270 m, so the river went thin and wide by turns. Channel rules
  2 draw the channel as wide as the mask measured it, wider than the flow's too (up to 3x): one band that widens and
  narrows gently.
- **Data:** four new files on the GLO-90 data release (`egl-v1`, uploaded 12 November on the author's yes:
  `rivers-flow-glo90-1.bin`, `rivers-outline-glo90-4.bin`, `lakes-glo90-2.bin`, `dams-glo90-3.bin`), fetched by the server
  as before.

**After rc.7: the code review of 11 November 2026 (merged from `claude/review-1111-fixes`, not released):** the
review's findings checked against rc.7 and fixed where they held; what was fixed, what wasn't and why is in
[the review's section 11](docs/research/code-review-2026-11-11.md#11-fixes-12-november-2026), the check of those fixes and
its three follow-ups in section 12.
- **An Earth world never generates without Earth:** a start whose `earth.json` can't be written back, whose Earth or
  world-generation system failed to start, or whose terrain model failed to build now stops the server (one error line)
  instead of letting vanilla's terrain into an Earth world; a chunk whose terrain throws or that vanilla's terrain made
  is noted in the save and generated again at the next start. A rules-v4 world whose ore layers are off this session
  leaves Earth's ore layer off (as without the deposit hook) instead of saving floor-only ore.
- **New worlds only (`GenFixRules` 1):** dry land below 0 m (the Caspian lowland, the Jordan valley, Death Valley,
  polders) continues from the land at 0 m without a two-block ledge on the 0 m contour; springs and seeps are planned
  from the columns generation makes (whole blocks, not half a block off, where slopes dropped many); crops and fruit
  trees in a river's green belt are judged by its rain (the Nile's onions and peaches); dry river beds' gravel is
  scattered instead of lying in diagonal stripes.
- **Servers:** `/earth` subcommands that don't need a position run from the server console; a GLO-90 square whose
  download takes more than two minutes (ten with its wait for a download slot) no longer holds chunk loading for up to
  half an hour; a failed prefetch waits before it is asked again; the download status shows the data source, its errors and the disk space to admins only.

**2.0.0-rc.6 (release candidate, 11 November 2026, not released):** rc.5 with the world creation screen's texts and a
display fix, the same worlds.
- **Earth start tab:** Start and Start coordinates are on a tab of their own. The game's text field turns clipping off
  for everything drawn after it in a scrolling list, so on a small window the Earth tab's later options drew over the
  screen's edges when scrolled (the author found it). No mod code runs on that screen; on a short tab the list never
  scrolls. A test keeps any text field the last option of a tab that never scrolls.
- **Texts:** 1:10 is no longer called a test setting ("Earth · 1:10"; the World height warning above 512 and Sea level's 160 likewise);
  the play styles' descriptions give rc.5's world sizes.
- **Install:** as rc.5 (Cylinder World 1.0.4 or later, 1.1.0-rc.3 checked; Hydrology 0.6.2-rc.4 or later).

**2.0.0-rc.5 (release candidate, 9 November 2026, not released):** rc.4 with the world's size set by its scale
(decision 0068).
- **Earth size is a scale:** 1:200, 1:100 (default), 1:50, 1:25, 1:10, and now **1:5**. The world is as long east-west as
  the Equator at that scale (Cylinder World's ring, which wraps), plus 16 384 blocks of hidden seam ocean beyond each end,
  and pole to pole north-south: 1:10 is 4 040 704 × 2 003 968 blocks (was 5 009 920 wide, a tenth of the width hidden
  at each side for nothing), 1:5 is 8 047 616 × 4 007 424. The scales from 1:200 to 1:10 keep their lengths.
- **New worlds only:** a world keeps its size. A world configuration with a width from before is still read (same length,
  the new width). The same place has other coordinates than in a world made before, and so other fine terrain detail.
- **Checked:** new worlds at 1:10 (from an old width value) and at 1:5 generate their 20 test places without an error;
  Cylinder World takes them as Earth proportions (its ring twice the length, jump lines at x = 16 384); the spawn search
  finds the same place. 1:5 builds its model in about 29 s (15.5 million river pieces) and peaks at about 7.2 GB. The
  Earth tab's texts and values checked against the game's menu code; the option and map suites updated.
- **Install:** as rc.4.

**2.0.0-rc.4 (release candidate, 9 November 2026, not released):** rc.3 with a faster start (decision 0067), the same
worlds.
- **Faster start:** the slowest steps of building a world's map model (the straits, the sea channels' ways out, the
  river and channel indexes, the river courses' positions) and the story locations' site search run on several cores
  (half the logical processors, at most 8), with the same result as one core. A new 1:10 world on the bench, server start
  to joining: 55 s instead of 74 s; the model builds in about 14.5 s instead of 20.5 s. Most of what is left of a start is
  the game itself and other mods.
- **Model cache:** the two slowest results (the straits' courses and the sea channels' ways, about 600 KB a world) are
  kept in `VintagestoryData/ModData/earth/cache` and read back at the world's later starts: the same world started again
  joins in 29 s instead of 38 s, the model in about 11.5 s. They are used only for exactly the same Earth build, data,
  runtime and world settings; anything else builds them afresh, and the log says what changed. `ModelCache: false` in
  `earth.json` turns it off; the world is the same either way.
- **Same worlds:** checked against rc.3 in a new 1:10 world (seed 386217, 20 places), once building the cache and once
  reading it: the terrain pass, Earth's generation records, the spawn and the story locations identical; the later passes
  differ only where vanilla's own generation differs between runs. Memory as in rc.3 (a 1:10 server peaks at about
  5.5 GB while it starts, then holds 4.5 to 4.8 GB).
- **Install:** as rc.3: Earth, Cylinder World 1.0.4 or later, Hydrology 0.6.2-rc.4 or later (0.6.2-rc.5 recommended).

**2.0.0-rc.3 (release candidate, 9 November 2026, not released):** rc.2 with the fixes of a third review round, again
the same worlds as rc.1.
- **Install:** Earth, Cylinder World (1.0.4 or later; 1.1.0 recommended) and Hydrology (**0.6.2-rc.4 or later**: rc.1 to
  rc.3 could let a water source spilled into the sea make the sea climb and flood the map). Worlds made with rc.1 or rc.2
  keep generating as before.
- **Reviewed:** Codex, an adversarial review of saves, multiplayer, the data store and runtime code, Hydrology's and
  Cylinder World's own second rounds, a water playtest in game (rivers, rapids, lakes and lake ice, the sea, dams,
  channels), and every option on the Earth tab tried in generated worlds (all 24 options and five play styles do what
  they say, and stay frozen in the save).
- **Fixes:** an Earth save started without Cylinder World is refused instead of getting vanilla chunks; a world
  upgrade requested in earth.json survives a start that fails; a brief network failure no longer turns a world's GLO-90
  squares, outlines or 30″ ground into a permanent fallback (only a source's "not there" is kept; otherwise the start
  stops and the world resumes next time); one lock for every download into `ModData/earth`; an offline 1.0.x world
  needs only the files its rules read; Hydrology is never handed a stand-in rapids answer it would write into the save,
  and the river query settles far fewer chunks; the climate correction no longer adds up in vanilla's catch-up loops;
  runtime and client patches (grass, ground moisture, season tint, thunder, the creative date rows) go in whole or not at
  all, through `PatchVanilla`; a closed single-player world no longer stays in memory; clearer texts on the Earth tab.
- **Same worlds:** checked against rc.1 again in the default world and both ore worlds: the terrain pass and Earth's
  other generation data identical, the rest only vanilla's own run-to-run variation. All 29 offline test suites pass.

**2.0.0-rc.2 (release candidate, 9 November 2026, not released):** the same worlds as rc.1, from restructured and
reviewed code, with a faster start and fixes.
- **Install:** Earth, Cylinder World (1.0.4 or later; 1.1.0 also finishes setting up a world whose first start stopped)
  and Hydrology (0.6.2-rc.1 or later, river query version 3). Worlds made with rc.1 keep generating as before.
- **Same worlds:** checked against rc.1 itself in a new 1:10 world (seed 386217) at 20 places, in three set-ups (default,
  ore rules v4, ore rules v3): every block of the terrain pass, the top rock, hot springs and rock floor identical; the
  later passes differ only where vanilla's own generation differs between two runs of one build (ruins' randomizer
  blocks and loot vessels, bamboo, water plants). All 29 offline test suites pass.
- **Faster:** the world's model builds in about 19 s instead of 24 s in game at 1:10 (offline: 16.5 s instead of 21.4 s,
  memory unchanged); chunks generate about 3 % faster.
- **Fixes:** a new world whose first start stopped (no internet for Earth's data) is created on the next start, not
  left as vanilla; Hydrology's river query says "rapids" exactly where Earth draws white water (at the Congo, 1:10: 139
  calm pool sources were turning rapid, now 0) and gives a river column's bed and surface (`RiverSpanAt`); `/wgen regen`
  keeps rules v4's ore catalogue and the companions' terrain plan; a rivers file out of flow order is refused at load.
- **For game updates:** every Harmony patch and non-public game member of world generation goes through one table
  (`PatchVanilla`, 45 features: 39 for world generation and what it reads, 6 for the running game and the client): if an update removes or changes one, Earth logs one line naming the game version and the
  feature and turns only that feature off. If Earth's own terrain can't hook in, Earth refuses the world (one error
  line, the server stops) instead of making half-Earth chunks. Vanilla's terrain and block-layer handlers are wrapped
  through the game's public handler list.
- **Code:** the data, river network, ore session and world generation are split into small parts with one job each
  (`EarthWorldGen` from 2,062 lines to 323), checked identical at every step; a per-chunk column store halves the model's
  column builds.

**2.0.0-rc.1 (release candidate, 8 November 2026, not released):** Earth is one mod ([decision 0066](docs/decisions/0066-one-mod-data-from-github.md)).
- **Install:** Earth, Cylinder World (1.0.4 or later) and Hydrology (0.6.2-rc.1 or later: its river query version 3 gives every new world the calm rivers). The data-pack
  mods (Earth Terrain Data, Earth Water Data and the test builds' height, water-fix and ground packs) are no longer
  needed; if they are still installed, Earth copies its files from them instead of downloading.
- **First start:** the server (or the single-player game) downloads Earth's data once per computer, about 290 MB (and 170 MB of 30″ ground sets for worlds with ground columns, the default), from
  [earth-data](https://github.com/Temporal-Poets-Society/earth-data) into `VintagestoryData/ModData/earth/data`, before
  it builds a world, and checks every file. A new world at full GLO-90 heights also fetches those heights as it needs them
  ([earth-glo90](https://github.com/Temporal-Poets-Society/earth-glo90)). Without internet, put the release's files into
  that folder by hand; a server that can't get them stops and says which are missing. A game client downloads nothing.
- **What's in it:** everything since 1.0.2: rivers on the real channel at fine scales, GLO-90 heights and outlines, coasts and
  waters as of 1318, ores at their real size in their ground layers, ground columns from real soil and geology, biomes and
  aridity, alpine turf, arid floodplains, Earth's own rain and thunder, the open sea contracts' weather role, ponds and
  springs choices on the Earth tab, and the fixes of a full code audit. Worlds made with 1.0.x keep generating as before.

**1.0.2 (source, not released):** vanilla's sea-level deposits (saltpeter, most iron and coal, sulfur, borax, gems…) follow
Earth's sea level. They were scaled for vanilla's level (220 at world height 512, 165 at 384) before Earth lowered it to
110/128, so tall worlds had saltpeter coating the ground and those ores sat in hills instead of below sea level. New
chunks only; worlds at height 256 are unchanged. [Decision 0045](docs/decisions/0045-sealevel-deposits.md).

**1.0.1 (1 November 2026):** manifest-only release of all three parts. The author field reads "Andris" (it read
"Your Name" in the mod manager), and Earth requires Hydrology **0.6.1**, which is marked required on clients: with 0.6.0
a client without Hydrology was refused on an Earth server and the game's automatic download skipped it. It also
requires Cylinder World **1.0.3**, which keeps Earth's land in its seam band (CW-D31): with 1.0.0-1.0.2 and `PolarIceMode`
"earth", islands on the jump line (St. Lawrence Island, the Diomedes) were cut by a sea channel. Code, data and terrain are
identical to 1.0.0 (only the version stamp differs).

**Shared publication version: 1.0.0.** Earth, Earth Terrain Data and Earth Water Data are three required parts of one mod and use the same release version. This labels the author-tested pre.16 implementation as 1.0.0; terrain, climate and gameplay rules are unchanged. The packager rejects mismatched versions on future builds. [Decision 0042](docs/decisions/0042-unified-release-version.md).

**Pre.16:** Sea is strongly recommended and optional. Earth generates terrain and supplies its own wind/weather
without Sea. Cylinder World, Hydrology and both Earth data packs remain required. This supersedes the Sea requirement
introduced in pre.13. [Decision and verification](docs/decisions/0041-optional-sea.md).

Pre.15 prepares three required downloads below the Mod DB's 40 MB limit: **Earth** (28.5 MB: code, ecology/resource rules, climate/weather), **Earth Terrain Data** (36.4 MB: elevation, summits, glaciers) and **Earth Water Data** (31.6 MB: rivers, coasts, islands, lake overlays). Install all three on client and server, alongside Cylinder World and Hydrology. Sea is strongly recommended. The data packs contain no executable code; Earth verifies their file hashes and routes deferred reads to their owning archive.

Additional fine mountain relief is retained above **3,000 m**, with a 250 m transition; lower terrain remains in the base map. Temperature data keeps the same 2' geographic resolution with **0.5°C storage steps**, changing stored values by at most 0.2°C. Other geographic data and the named-peak catalogue are unchanged. This is an intentional pre-release dataset change; older worlds are outside its compatibility scope. Previous archives remain preserved. Build/offline checks pass; client inspection is pending. [Decision 0040](docs/decisions/0040-publication-data-and-packaging.md).

Pre.14 adds **Summit height correction** to the Earth customization tab: Off or minimum surveyed elevations from 1,000 to 8,000 m, in 1,000 m steps; **3,000 m is the default**. Ordinary named hills receive no extra listed-peak correction at the default. Fuji remains included. The finer ETOPO terrain-detail layer remains active even with this correction off. The choice is saved per new world; older worlds retain their existing corrections. See [decision 0039](docs/decisions/0039-summit-correction-cutoff.md).

**Later author direction, 31 October:** Sea becomes a strongly recommended optional integration. Earth retains its own wind and weather without Sea; a compatible Sea consumes that wind and adds waves, reflections and vanilla-boat sailing. The earlier pre.13 requirement below is superseded for Sea.

Pre.13 made **Hydrology 0.1.10+ and Sea 0.4.0+ mandatory**, alongside Cylinder World 0.11.11+. The manifest declares them and active Earth worlds validate the river-query and wind-provider APIs before adopting world size, sea level or saved terrain settings. Incompatible runtime providers stop Earth startup with a clear error instead of choosing a fallback river layout. Client Sea validation happens before accepting enabled Earth parameters; Hydrology's API remains server-side. Existing terrain settings and the pre.12 package are preserved. See [decision 0038](docs/decisions/0038-required-water-providers.md) for the engine's excluded-mod boundary and verification.

Pre.12 adds the Earth play style, creation-only ecology/weather choices and varied regional random starts (decisions 0036 and 0037). Other new play styles keep vanilla generation with Earth installed. Offline checks pass; menu and server acceptance are pending.

**Coordinator candidate: not yet accepted in game.** This uncommitted pre.9 revision addresses #16-#18, #21 and
#24-#26 with global climate/native-range rules, a 712-entry Holocene stratovolcano catalogue, marine-only coral
generation, dry desert drainage with imported-flow protection, reservoir classification and shipping straits.
Decisions: [0032 ecology and summits](docs/decisions/0032-alpine-cover-and-surveyed-summits.md),
[0033 coral](docs/decisions/0033-marine-coral-habitat.md),
[0034 wadis/reservoirs](docs/decisions/0034-arid-drainage-and-reservoirs.md),
[0035 straits](docs/decisions/0035-navigable-straits.md).

#23's global surveyed catalogue is prepared but awaits the separate download. Run
`tools/bake_peaks.ps1 -InputFile PATH/allCountries.zip` after it arrives; the baker uses GeoNames' explicit elevation,
never its DEM field. The loader uses the baked file, or the nine fallback peaks when absent. A world freezes its
catalogue SHA256. The 15-site fixture tests validate shaping; they are not production catalogue acceptance.

Existing worlds keep their saved terrain. For **future chunks only**, opt in once with
`UpgradeExistingWorldMountainTerrain: true` (mountain surfaces/summits/catalogue) and/or
`UpgradeExistingWorldWaterTerrain: true` (dry wadis, reservoir overlay, straits) in `earth.json`.
Flags reset after adoption. Already generated chunks stay unchanged; seams can occur at an opt-in boundary.
Non-terrain species fixes follow decision 0002 and affect new generation only.

Offline evidence: `tools/run-playtest-issues.ps1` (alpine maps and real pre.8 species baseline), then
`tools/run-playtest-review.ps1` (global ecology, peak baker/loader, corals, rivers/lakes and straits).
Outputs are under `tests/out/playtest-issues/`, including `report.html`, `review-report.html`, CSVs and before/after
terrain renders. The review runner reads existing raw lake metadata in the main checkout; it downloads nothing.
The coordinator will build/test with game assemblies and commit; no game client, install or push is part of this task.

The original mountain-height experiment is documented in the [test plan](docs/mountain-height-playtest-plan.md) and
[decision 0031](docs/decisions/0031-mountain-height-playtest.md), including the pre.4–pre.8 default changes. Existing
worlds retain their saved vertical layout and climate rules. Visual acceptance of the new play-test fixes is pending.

Generates the real Earth: land and sea floor from real elevation, lakes at their real levels, the Greenland and
Antarctic ice sheets, and the real climate (temperature and rainfall, so soil, grass, crops and forests follow it).
It needs **Cylinder World** 0.11.11 or newer (the world wraps east–west, the seam runs through the Bering Strait,
the poles are Cylinder World's walls).

**Play-test build 0.16.14-playtest.1** (25 October 2026, same branch): adds Earth 0.16.13 (pool layout 8 and the river
flow query for Hydrology, [decision 0027](docs/decisions/0027-river-flow-query-for-hydrology.md)); pair it with Hydrology 0.1.9.

**Play-test build 0.16.14-playtest.2** (26 October 2026, branch `playtest/2026-10-26`): 0.16.14-realseam.1 and
0.16.14-rivers.1 below merged into one build, installed with Hydrology 0.1.10. New worlds get both the real seam
(GutterSeabed 3, with Cylinder World in Earth polar mode) and pool layout 9 (with Hydrology 0.1.10); they are separate
saved values, so existing worlds keep their seam and pool layout. Offline suite: as both branches, only the known Lake
Geneva check fails. Not seen in game.

**Test build 0.16.14-rivers.2** (26 October 2026, branch `claude/congo-gap-pulse` from `playtest/2026-10-26`, not
installed): pool layout 10 for the play-test of layout 9 at the Congo (decision 0027, "Pool layout 10"). Steps follow the
valley ahead, so steep stretches no longer bunch steps 3-5 apart. Where two steps meet on the inside of a bend the ramp
falls evenly, with no wall of falling water (spill) above it. Pair it with Hydrology 0.1.11, whose rapid water throws less
foam so the client's particles no longer pulse. Layouts 0-9 are unchanged.

**Test build 0.16.14-rivers.1** (25 October 2026, branch `claude/river-rapids-issues-3-4` from the play-test branch, not
installed): pool layout 9 for the play-test's river issues #3 and #4 (decision 0027, "Pool layout 9"): the river steps
down one block at a time, at least 7 blocks apart where the valley allows, so each ramp reaches the next step and the
surface has no vertical water faces; a rapids reach's sources carry the river's flow; beside a lake the river changes
level across its whole width. Needs Hydrology 0.1.10 (query version 2) for new worlds to get it; existing worlds keep
their layout.

**Play-test build 0.16.12-playtest.1** (24 October 2026, branch `playtest/2026-10-24`, not merged): 0.16.11 with the
review fixes of `claude/review-fixes-earth` (rounds 1 to 4 and the CHELSA temperature work: `earthdata/temperature.bin`).

**Play-test build 0.16.12-playtest.2** (25 October 2026, same branch): St. Lawrence Island continues across the seam
instead of being split by the gutter's sea channel, in new worlds ([decision 0029](docs/decisions/0029-seam-land.md));
needs Cylinder World 0.11.15-playtest.2 to keep that land in its seam band.

**Real seam build 0.16.14-realseam.1** (25 October 2026, branch `playtest/seam-island-2026-10-25`, from 0.16.14-playtest.1):
new worlds have no seam gutter. The real Earth runs across the jump line, so St. Lawrence Island keeps its real shape
instead of a straight bar across the gutter ([decision 0030](docs/decisions/0030-real-seam.md)). `SeamGutter: true` in
`earth.json` gives new worlds the old gutter of open sea.

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

Everything comes from free public data (see `earthdata/ATTRIBUTION.txt`): ETOPO 2022 (NOAA, public domain),
University of Delaware / NCEP / GPCP climate normals (NOAA PSL), Natural Earth lakes (public domain), GSHHG shorelines
(LGPL), HydroRIVERS (www.hydrosheds.org, free with attribution).

## Quick start

1. **Install** on the server and on every client: `earth-<version>.zip` (one mod since 2.0, [decision 0066](docs/decisions/0066-one-mod-data-from-github.md)),
   Cylinder World (`cylinderworld` 1.0.4 or later, required) and Hydrology (`hydrology` 0.6.2-rc.4 or later, required:
   its river query version 3). Sea is recommended. The 1.0.x data-pack mods (Earth Terrain Data, Earth Water Data and the
   test builds' height, water-fix and ground packs) are no longer needed; if they are still installed, Earth copies its
   files from them instead of downloading them. To build the zip yourself: `dotnet build EarthMap.csproj -c Release`
   → `release/earth-<version>.zip`, with the version from `modinfo.json` (an existing zip of that version stops the
   build: bump the version, or pass `-p:OverwriteRelease=true` for an unpublished one).
2. **Create a new world** with one of the Earth play styles: **Earth · 1:100** (the default, 433 152 × 200 192 blocks,
   384 high), 1:200, 1:50, 1:25 or 1:10 (4 040 704 × 2 003 968 blocks, 1024 high); 1:5 is on the Earth tab. Each style
   sets the world size, height and mountain curve for its scale (decision
   [0068](docs/decisions/0068-world-size-by-scale.md)); the start is on the Earth start tab, the other choices on the
   Earth tab. Other play styles keep vanilla generation. Earth 2.0 is for new worlds: older Earth worlds load with
   their saved rules, but are not upgraded.
3. **First start downloads Earth's data** (the server, or the single-player game; a game client downloads nothing), once
   per computer into `VintagestoryData/ModData/earth`, shared by every world and every later Earth version:
   - about 290 MB of data files from the release `ed-v1` of
     [earth-data](https://github.com/Temporal-Poets-Society/earth-data) into `ModData/earth/data`, before the first
     chunk, each checked against its size and SHA-256;
   - about 170 MB of 30″ ground sets from the release `egts-v1` of
     [earth-ground](https://github.com/Temporal-Poets-Society/earth-ground) for worlds with ground columns (the default);
   - the GLO-90 land heights from the release `egl-v1` of
     [earth-glo90](https://github.com/Temporal-Poets-Society/earth-glo90) into `ModData/earth/glo90`: all of it (8.6 GB)
     by default, starting when the world opens, with its progress in a window (`.earthelevation`); or, on the Earth tab,
     each square as the world reaches it, or Copernicus's originals (about 71 GB, converted on the machine).

   Without internet, put the releases' files into those folders by hand (or set `DataRepo` in `earth.json` to a mirror
   or a local folder). A server that can't get the files a world needs stops and says which are missing, rather than
   generate a world with parts of its data missing.
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
- **Seasons**: vanilla swings temperatures by 65 °C × latitude between winter and summer everywhere. Earth Map
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
- **Story locations**: vanilla picks them at random offsets from spawn. Earth Map keeps those distances, anchors
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
- Earth Map wraps Cylinder World's climate layer (temperature and rainfall come from the Earth, geologic activity
  stays vanilla's) and turns off Cylinder World's own temperature extension at runtime (it would add its latitude
  curve on top of the real climate). Cylinder World's winter rule on the ice sheet still works with the real
  temperatures (the Antarctic plateau is −46 °C yearly, −60 °C in the southern winter).
- Spawn and the story locations are placed after Cylinder World moved them.
- Cylinder World 0.11.11 or newer is a required dependency (decision 0023);
  its geometry is still read by reflection, so Earth Map doesn't compile against it.

Known interplay issues, best fixed in Cylinder World (suggestions for its next version):
1. **Ice sheet height**: Cylinder World's ice sheet from 85° is a fixed plateau (70 blocks above sea). The real
   Antarctic plateau at 85°S maps higher (≈80 blocks in a 256 world, ≈120 in a 384 world), so it's planed down
   there, leaving a drop of up to ~110 blocks at the sheet's edge in a 384-high world. North of 85°N is the Arctic
   Ocean, where Cylinder World builds its ice sheet on a raised sea floor.
   Suggestion: let another mod provide the ice sheet's surface height (or skip the sheet where the Earth has sea ice).
2. **Hooks instead of reflection**: an interface in Cylinder World (e.g. `ISurfaceProvider` with base temperature,
   rainfall and "true" temperature at a position, plus an event before spawn and story locations are moved) would let
   Earth Map plug in cleanly and let Cylinder World's `/cyl map` and temperature code use the Earth's values.
3. `/cyl map` shows vanilla's ocean map, not the Earth; use `/earth map`.

## Without Cylinder World

Not supported ([decision 0023](docs/decisions/0023-world-creation-tab-and-cylinder-world.md)). A new world needs
Cylinder World active with its planet layout: `"Enabled"`, `"PolarWalls"` and `"PolarClimate"` true in
`ModConfig/cylinderworld.json` (the defaults), and a map wide and long enough that it keeps its seam and walls.
Otherwise the log has an error saying what is missing, and the world keeps vanilla terrain (create a new one after
fixing the config). A world made by an older Earth Map without Cylinder World, or one whose Cylinder World has been
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
- Not played in game yet. Known gaps: thunder and hail are still vanilla's events (lightning only strikes under Earth's rain
  clouds, as vanilla requires them); the reanalysis has too little of the low cloud decks off Peru and Namibia.

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
| Earth size | `earthWorldSize` | 100 (the play style's) | the scale: `200` (1:200), `100` (1:100), `50` (1:50), `25` (1:25), `10` (1:10) or `5` (1:5). The length is the Equator over twice the scale (100 352 to 4 007 424 blocks), the width twice the length (Cylinder World's ring) plus 16 384 blocks of hidden seam ocean beyond each end (decision 0068; before 2.0.0-rc.5 the width was 2.5 × the length, and such a width in blocks is still read). `vanilla` keeps World generation tab dimensions (at least twice as wide as long, such as 1 024 000 × 512 000, or the Earth is stretched north-south) |
| World height | `earthWorldHeight` | 384 (the play style's) | 256 to 1024 blocks in steps of 64 (a server config may give any multiple of 32); replaces World Height at the top of the screen. `0` keeps that slider's height |
| Start | `earthSpawn` | random | on the **Earth start** tab with Start coordinates (from 2.0.0-rc.6: the game's text field turns clipping off for everything drawn after it in a scrolling list, so on a small window the Earth tab's options drew over the screen's edges when scrolled; on a tab of their own the list never scrolls). Regional random dry, gentle lowland at the chosen starting climate; 36 capitals; or `custom`. Random gives each qualifying major region a fair chance, permits coasts and avoids tiny islands ([decision 0037](docs/decisions/0037-varied-random-starts.md)). The same seed and settings give the same place. Capitals and coordinates find nearby dry land at any height |
| Start coordinates | `earthSpawnCoordinates` | (empty) | for `custom`: `latitude, longitude` in degrees; south/west negative, e.g. `56.95, 24.11` |
| Suez / Panama Canal | `earthSuezCanal`, `earthPanamaCanal` | off | optional navigable sea-level cuts, including their lakes; Panama has no locks |
| Sea level | `earthVerticalBudget` | 110 | `110`, `128` or `160` blocks below sea (at most half the world height), independent of world height |
| GLO-90 land heights | `earthGlo90Download` | release | `release` or `copernicus`: every GLO-90 square is downloaded when the new world first opens (Earth's data release, 8.6 GB, or Copernicus's originals, about 71 GB converted here), squares already on the machine skipped; `needed`: each square as the world reaches it. A world without the key downloads as needed |
| Mountain heights | `earthHeightCurve` | log1000 | `log1000`, `log250`, `log500`, `log2000` or `linear`. Smaller log values exaggerate lowlands and compress high mountains; linear preserves proportions |
| Summit height correction | `earthSummitCorrection` | 3000 | `off` or minimum surveyed elevations `1000` through `8000` in 1,000 m steps. Corrects listed peaks at or above that real elevation; excluded peaks do not narrow qualifying peaks' correction footprints. The finer terrain-detail layer stays active. Saved at creation; older saves keep all previously eligible peaks |
| Land heights | `earthElevation` | auto | not on the tab ([decisions 0051, 0053](docs/decisions/0051-glo90-elevation-on-demand.md)): `auto` (as `glo90`) takes dry land's heights from Copernicus GLO-90 (3″, about 90 m), per 1° square as chunks need them, kept in `VintagestoryData/ModData/earth/glo90` for every world on the machine, with the included coarser GLO-90 grid where a square can't be had; rivers, lakes and dams follow GLO-90 too. `glo90coarse`: the included grid; `builtin` (and any other value): the older 2′ heights. Listed peaks at or above the summit cutoff get a narrow lift at GLO-90's own top to their surveyed height (never lowered). `.earthelevation` opens the download dialog |
| Rock floor | `earthRockFloor` | auto | how far below the lowest ground of each 32 × 32 column the ground turns to plain rock (no caves, ores or lava; mantle only in the bottom layer): `off`, `96`, `128`, `160`, `192` or `256` blocks; `auto` is 160 in worlds taller than 512, none otherwise |
| Rocks and ground | `earthRocks` | groundcolumns | `groundcolumns`: regional rock regions and ground built from real data (soil, sediments in metres, sea-floor sediment; bedrock layers in the Baltic); the first such world on a machine fetches the 30″ ground data (about 172 MB, [earth-ground](https://github.com/Temporal-Poets-Society/earth-ground)); `regional`: rock regions only (`RegionalGeology`); `vanilla`: the game's rock layers |
| Ground depth | `earthGroundDepth` | log4000 | ground columns only: how real depth becomes blocks (`log4000`, `log2000`, `terrain`, `linear2000`) |
| Ores | `earthOreDeposits` | vanilla | `vanilla`: the game's ore deposits; `maps`: Earth's ore maps (`RegionalOres`, experimental, being reworked). Worlds set up with the earlier single key `earthOres` keep its meaning: `vanilla` is vanilla rocks and ores, `regional` and `groundcolumns` are that rock choice with the ore maps |
| Plants and animals | `earthNature` | 1318 | `1318`: plants and animals where they lived around 1318 ([decisions 0049, 0047](docs/decisions/0047-placement-catalogue-and-audit.md)): fruit trees, wild crops and berry bushes by their 1318 range and Earth climate, regional trees, real animal ranges. `vanilla`: placement by climate alone (no regional trees, plant gates or animal ranges). Worlds made before this option read the old `earthAnimals` key; older saves keep vanilla fruit, crop and berry placement |
| River rapids | `earthRiverRapids` | steep | `steep`: white water at the drops of rivers whose real fall is strong for their size ([decision 0046](docs/decisions/0046-calm-rivers.md)); `off`: no rapids reaches, waterfalls stay |
| Mountain springs and streams | `earthMountainSprings` | caves | `caves`: vanilla's springs in cave walls only, none in open cliff faces and no mountain-side streams; `streams`: as `caves`, plus a spring at the head of each perennial stream (HydroRIVERS) and seeps where the ground water meets a slope in wet climates; `vanilla`: all of them as the game makes them |
| Surface ponds | `earthSurfacePonds` | off | `off`: none of vanilla's surface ponds; `wetlands`: only in the mapped bogs, marshes and swamps (GLWD v2), by their share of the ground; `vanilla`: wherever the game digs them. Ponds in caves are the game's in every case |
| Include extinct animals | `earthIncludeExtinctAnimals` | off | used with 1318; also spawns species that died out before 1318 (mammoths, cave lions) from supported animal mods. Animals still alive in 1318 (aurochs, moa) spawn without it |
| Chickens everywhere | `earthChickensEverywhere` | on | used with 1318 ([decision 0054](docs/decisions/0054-animals-as-around-1318.md)): on lets wild chickens spawn wherever the climate suits them (every region but Antarctica); off keeps them where people kept them around 1318. With `vanilla` nature every animal spawns by climate alone, so this changes nothing there |
| Weather | `earthWeather` | earth | real-climate rain, storms, clouds and wind, or vanilla weather |
| Rain frequency | `earthRainFrequency` | 1 | Earth weather only: 0, 0.5, 1, 1.5, 2 or 3 times the real monthly rain chance |
| Storm frequency | `earthStormFrequency` | 1 | Earth weather only: 0, 0.5, 1, 1.5, 2, 3 or 5 times the real storm frequency |

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
| `FaunaRangeProfile` | natural | `natural` (ranges without human impact) or `current` (present-day ranges where known); frozen for pre.12+ worlds, applied on restart in older saves |
| `IncludeExtinctAnimals` | false | creation fallback; pre.12+ worlds save their choice, older saves apply this on restart; existing animals remain |
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
| `Weather` | true | Earth's weather (rain, clouds, winds, storms from the real climate); false = vanilla weather. Creation fallback in pre.12+; config on restart in older Earth saves |
| `WeatherPace` | 1 | weather hours per game hour (0.25..8): 1 = real speeds; higher = more weather per season, faster changes |
| `UpgradeExistingWorldClimateData` | false | one-shot: the newest climate fix ([decision 0018](docs/decisions/0018-climate-stripes.md): v2, the complete UDel normals, without one-row stripes and with December's precipitation) for this world's new chunks |
| `RainFrequency` / `StormFrequency` | 1 / 1 | multiply how often it rains (0..3) / storms form (0..5, 0 = none) |
| `SelfTest` | off | automated in-game test (see below) |

## Data pipeline

```
tools/download_data.sh                  # ~700 MB into rawdata/
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
- `climate.bin` (3 MB): 0.25° grid: sea-level-equivalent yearly mean, coldest and warmest month, yearly
  precipitation, VS rainfall.
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
| `src/Core/TerrainModel.cs` | both | one column's terrain (sea, lakes, land, ice, flattened sites); climate pixels; forest potential |
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
| `src/Core/WeatherData.cs`, `WeatherNoise.cs`, `WeatherModel.cs`, `WeatherStorms.cs` | both | the weather: monthly normals and ITCZ, noise, winds, rain, clouds, storms |
| `src/EarthWeather.cs` | both | the weather in the game: vanilla rain/cloud/wind patches, Sea's wind provider, `/earthweather` |
| `src/SelfTest.cs` | server | the automated in-game test |

## Versions

- 0.16.1 (not released yet): the game's Singleplayer world list crashed with 0.16.0 installed (`getHoverText` reads
  every mod's `playStyles` without a null check); `worldconfig.json` now has an empty `playStyles` list.

- 0.16.0 (not released yet): an **Earth tab** on the world creation screen and **Cylinder World required**
  ([decision 0023](docs/decisions/0023-world-creation-tab-and-cylinder-world.md)). The tab sets the Earth size (the
  width; the length follows at 0.4 × the width), the start (a random place at the starting climate, a capital or
  coordinates) and the Suez and Panama canals; `SpawnPlace`, `SuezCanal` and `PanamaCanal` left `earth.json`.
  Cylinder World 0.11.11 is a dependency, the flat-world layout (`EdgeSea`, `MaxStretch`) is gone, and a world without
  Cylinder World's planet layout keeps vanilla terrain with an error in the log. Offline tests and server self-tests
  pass; the tab not yet seen in the game's screen.

- 0.15.6 (not released yet): no springs or rapids out of frozen rock, no vanilla land animals in Antarctica
  ([decision 0021](docs/decisions/0021-frozen-springs-antarctic-animals.md)). Vanilla's rivulets (springs in cliffs,
  streams and rapids breaking out of mountain sides) are skipped on ice sheets and where the warmest month is below
  0 °C at their height; vanilla only asked for -15 °C at sea level. Vanilla's goats, bears, foxes, hares, wolves and
  other land animals no longer spawn south of 60°S. New chunks of Earth worlds; offline checks only, not checked in game.

- 0.15.5 (not released yet): story locations near Earth's own spawn ([decision
  0016](docs/decisions/0016-story-sites-and-ruin-latitudes.md) History). With a Lisbon spawn the village had folded
  back to 615 blocks from spawn and the devastation area was in southern England. Every story location now keeps at
  least 75 % of vanilla's distance from spawn and from its parent. With Cylinder World 0.11.11, Earth hands it its
  spawn, so vanilla's story chain (and the devastation area's vanilla spot) starts there instead of at Cylinder World's
  own spawn (10.75°E, 44°N). A devastation spot with no dry land near it searches around the mirrored offsets like the
  other sites. New worlds only. Server tests (Cylinder World 0.11.11): Lisbon spawn, 1024000², seeds 12345 / 777 /
  424242: village about 13 500 blocks from spawn near Córdoba, devastation area in La Mancha (40.2°N 3.8°W, 39.3°N
  3.2°W, 39.2°N 4.0°W; the third had open sea at vanilla's spot); default spawn at 256000 × 102400: unchanged chain
  distances, devastation area in Karelia. Every site at or above its floor; not played.

- 0.15.4 (not released yet): the land as before the Suez and Panama canals ([decision 0020](docs/decisions/0020-canal-gaps.md)).
  GSHHG splits the continents along the canals, so in big worlds with outline coasts (0.14.5) the Suez Canal was a sea
  channel a few blocks wide and Gatun Lake stood at sea level with rivers stepping into it, whether or not the canal
  option was on. `coasts.bin` now closes those gaps; with `SuezCanal` / `PanamaCanal` the mod cuts its own wide canal,
  and the Great Bitter Lake becomes part of the canal's sea. Offline checks at 512k only; not checked in game.

- 0.15.3 (not released yet): the seam is a straight meridian, 169.25°W ([decision 0019](docs/decisions/0019-straight-seam.md)):
  meridians are straight near the Bering Strait and the poles too (Wmap's grid showed them bent), the sky's longitude
  equals the map's everywhere, the world's centre meridian is 10.75°E. Not compatible with existing worlds. Offline
  tests pass except one noise-sensitive Lake Geneva shore check (see the decision); not checked in game.

- 0.15.2 (not released yet): review fixes for the weather ([decision 0017](docs/decisions/0017-weather.md) History):
  storm caches that survive the server catching up on missed hours, weather rules v2 with the data fingerprint sent to
  clients, one canonical time for rain, `RainFrequency: 0` without storm rain, local-sun showers only when AstraTerra's
  sun follows longitude (decided by the server), 15 MB instead of 60 MB of weather data per side.

- 0.15.1 (not released yet): the weather's prevailing winds and cloud cover from the NCEP/NCAR reanalysis' monthly
  means (`weather.bin` v2; `tools/download_data.sh` fetches the three files); UDel's missing precipitation months are
  filled in the weather's data, which removes one-row rain stripes across Siberia and Kazakhstan. New worlds also get
  the climate without those stripes (`earthdata/climatefix.bin`, [decision 0018](docs/decisions/0018-climate-stripes.md):
  straight lines in forests and grass across Africa, Siberia and Kazakhstan); older worlds keep theirs unless
  `UpgradeExistingWorldClimateData` is set once.

- 0.15.0 (not released yet): Earth's weather ([decision 0017](docs/decisions/0017-weather.md), [above](#weather)):
  rain, clouds, prevailing winds and storms from each place's real monthly climate, multiplayer-safe and unsaved;
  `earthdata/weather.bin`; config `Weather`, `WeatherPace`, `RainFrequency`, `StormFrequency`; `/earthweather`. Offline
  weather tests and the server self-test pass; not played.

- 0.14.8 (not released yet): story locations ([decision 0016](docs/decisions/0016-story-sites-and-ruin-latitudes.md)).
  Surface buildings get a levelled pad that follows their own footprint, at the ground's own height there, instead of a
  ~670-block disc (flattened down to sea level for the village and Tobias' cave). Tobias' cave, whose schematic is a
  hill, looks for hilly surroundings. The devastation area stays on vanilla's own spot when it is dry land (else the
  nearest dry land), with a pad under its tower only. Story heights are set from the site at every start; older worlds
  keep their sites and heights. Vanilla ruins, other worldgen structures and villages only generate between
  `RuinsNorthLimit` (70°N) and `RuinsSouthLimit` (60°S), in new chunks of every world.
- 0.14.7 (not released yet): the seam gutter's floor is the real seabed in new wrapping worlds
  ([decision 0015](docs/decisions/0015-real-gutter-seabed.md); Cylinder World 0.11.6 keeps it in its seam band).
  [Decision 0013](docs/decisions/0013-estuary-islands-and-delta-branches.md): island rules
  v3: small islands in an estuary stay whole where a river's centre line or exit channel crosses them (the Tagus cut
  straight strips through its islands near Vila Franca de Xira). Delta branches share their river's water: the
  Mekong splits at Vàm Nao into the Tiền and the Bassac, half each, instead of all of it going down the Bassac as one
  54-block canal. River widths ([decision 0014](docs/decisions/0014-river-widths.md)): the standard world's widths
  are a floor in blocks on bigger maps and rivers are as wide as they really are where that is wider, so on very big
  maps the largest rivers reach their true size (the Amazon ~357 blocks at a 4 million ring, was 1406) while small ones
  stay playable (a 100 m³/s river 12 blocks, was 233); worlds up to the standard 204 800 ring are unchanged. New
  worlds only; existing worlds opt in with `UpgradeExistingWorldIslands`, `UpgradeExistingWorldRiverDeltas` and
  `UpgradeExistingWorldRiverWidths`. (Test builds called 0.14.6 had the river changes without the gutter seabed.)
- 0.14.5 (not released yet): the big land's coasts from GSHHG outlines in new worlds whose 2' grid cells are at least
  32 blocks wide (rings from about 350 000 blocks; [decision 0012](docs/decisions/0012-coast-outlines.md)): headlands,
  coves, fjords and spits at their true size instead of the grid's 3.7 km cells; the Sognefjord open to Flåm, polders
  as land. New file `earthdata/coasts.bin` (9.5 MB). Standard 204 800 worlds are unchanged; existing large worlds opt
  in with `UpgradeExistingWorldCoasts`.
- 0.14.4 (not released yet): island rules v2 for new worlds: where GSHHG traced a small island's outline from a
  raster (straight runs of ~550 m with 45° cuts) the corners are rounded off (Čiovo off Trogir had 50-block staircases
  with a 4 million block ring; at 204 800 the steps are about 3 blocks and the rounding moves the shore by under one).
  Existing island worlds keep v1 until `UpgradeExistingWorldIslands`.
- 0.14.3 (not released yet): eased lake shores for new worlds ([decision 0011](docs/decisions/0011-lake-shores.md)).
  The grid raised all dry land within 3 cells (about 11 km) of a lake to the lake's level, so where the ground falls
  away from a lake (its outlet valley) it stood as a flat plateau at the water's level ending in a cliff up to 12
  blocks high along the cell edges (south of Lake Bolsena and Lake Bracciano near Rome). Now only the land touching a
  lake keeps the raise; further out it slopes down to the real ground (`earthdata/shores.bin`). Existing worlds keep
  their terrain unless they opt in with `UpgradeExistingWorldLakeShores`.
- 0.14.2 (not released yet): the sky longitude service, `EarthMapSystem.LongitudeAt(x)`, handed to AstraTerra by
  reflection when it has `SetLongitudeProvider` (not in AstraTerra 0.15.0), so its sun, moon, stars and clock follow
  the real meridians ([decision 0010](docs/decisions/0010-real-day-and-night.md), phases 1–2; see "Day and night by
  longitude"). No change to terrain or saves. Offline tests pass; not yet checked in game.
- 0.14.1 (not released yet): river ends and the Lisbon bar, for new worlds (existing ones opt in with
  `UpgradeExistingWorldRiverEnds` and `UpgradeExistingWorldRiverChannels`): rivers continue downstream to their end
  (18 rivers had gaps or stopped where their discharge dipped), 18 terminal lakes in `lakes.bin` (the revived Aral
  Sea, Lop Nur, Lake Chad, ... with green shores, and the natural salt lakes), rivers led on to a fjord, estuary or lake they stopped short
  of (100 in the test world), wadis fading out (218); river exit channels as deep as the river (the Tagus 7) and out
  past the bars. Worlds with river ends find the rivers' seam crossings by the ring's geometry: with rings from about
  1 million blocks the old 4000-block test also dropped real pieces of rivers (111 at 4 million blocks), leaving gaps.
  Also: a failed ore activation now really leaves vanilla deposits (the native generators are restored); `/earth map`
  needs about 3 bytes per pixel instead of ~100, runs one at a time and writes the PNG as it goes;
  `StorySearchRadius` is limited to 64–50 000 blocks (0 searched forever). New worlds have at least 6 blocks of sea
  away from the shore (was 5; existing worlds opt in with `UpgradeExistingWorldSeaDepth`), so a carrack (draws about
  4.4) has room over the Bahama banks, Florida Bay and Torres Strait; reefs still rise above that and stop her. Design decisions are recorded in
  [docs/decisions](docs/decisions/README.md).
  Ore rules v3, Earth-like ores ([decision 0006](docs/decisions/0006-ore-balance.md)), reach regional-ore worlds in new
  chunks: the coal basins (the UK, the Ruhr, Silesia, the Appalachians, ...) have up to three continuous seams of
  bituminous coal and anthracite, the first a few blocks down, about 7,400 coal blocks per chunk in a full basin
  (`Ores.CoalBasins`); deposits no longer jump to any host rock in the column (lapis, phosphorite, limonite, coal and
  galena had been 8–150× too common); known districts fill any rock (Kimberley, Broken Hill, the salt domes at full
  height); surface copper and tin at vanilla's rate everywhere, also in limestone; deep copper's floor doubled; the
  prospecting pick reads what is placed. The great iron districts (Kiruna, Krivoy Rog, Lorraine, Carajás) get iron
  formations, ore bodies a few hundred blocks across and up to 8 thick (`Ores.IronFormations`), and every other
  resource's great districts rich ore bodies (`Ores.RichDistricts`); the game's ore frequency setting scales them all.
  Balance is config, not code: each resource blends a background map and the real-data map with `BackgroundWeight`
  and `EvidenceWeight`, and its ore bodies take a district ramp (`DistrictFrom`, `DistrictTo`) and a `Richness`. The
  coal ramp moved to evidence 0.45–0.75, halving the Ruhr's coal measures to 35,000 km² of world.
  Robustness: a failed story placement keeps vanilla's positions, a newer
  geology version uses the current rules instead of vanilla strata, spawn stays on the map when it is shorter than the
  Earth, `/earth tp ... water` searches in the background.
- 0.14.0: small islands (under 1000 km²) come from their real outlines (GSHHG full resolution), at their true size,
  only where they are at least `MinIslandBlocks` (4) blocks wide in the world: about 40 m wide with a 4 million block
  ring, 300 m with 512 000, 800 m with 204 800. ETOPO's false islands on reef rims are gone: the chains along the Great
  Bahama Bank's southern rim and around the Tongue of the Ocean were 10–95 m hills where there is only reef, and the
  bicubic sample added ridges of up to +35 m at the drop-offs. New worlds get the islands; existing worlds opt in once
  with `UpgradeExistingWorldIslands`, and until then see the terrain unchanged. Also in 0.14.0: the waterfalls, river
  exits, canals (0.13.3) and the Antarctic gutter bridge (0.13.2). Checked on a fresh 512 000 × 204 800 world: terrain
  = model in 99–100 % of columns at the Bahama rims, the Exumas, Victoria Falls, Niagara, Lisbon and the Antarctic seam.
  Known issue: ore balance, see [Known gaps](#known-gaps--next-steps).
- 0.14 pre-releases: the underground shelter cities, their room generators, the Room Lab helper and their design
  document (`docs/lore-translocator-network.md`) moved to the Earth Ruins mod (`../Earth ruins`, 2026-10-14), which
  places the cities through the companion terrain API. Earth Map has no city code or `UndergroundCities` setting any
  more; worlds made with an earlier 0.14 pre-release keep their cities when Earth Ruins is installed.
- 0.13.3: waterfalls on the large rivers in new worlds. Victoria Falls was four one-block steps spread over 400
  blocks, flatter than Niagara's cascade; now 14 listed falls drop their river at once (Victoria 22 blocks, Niagara 11
  in a 384-high world). Existing worlds opt in once with `UpgradeExistingWorldRiverFalls`. `/earth checkrivers` lists
  them as waterfalls, apart from unexpected falls. River exits to the sea are dredged for ships: before, a carrack
  (4.5 deep) got stuck at the Tagus narrows 108 blocks short of Lisbon's waterfront, now it reaches it; 572 of 573
  ship channels (512k world) have at least 11 blocks of 6-deep water all the way out (the Klamath's river ends above
  sea level behind the coast in the data). Optional sea-level Suez and Panama canals with their lakes (`SuezCanal`,
  `PanamaCanal`, off by default). Existing worlds opt in with `UpgradeExistingWorldRiverChannels` /
  `UpgradeExistingWorldCanals`.
- 0.13.2: Antarctica continues across the seam gutter in new Cylinder World worlds (before, the gutter cut a strait
  about two kilometres wide through the Ross Ice Shelf and the polar plateau). Existing worlds opt in once with
  `UpgradeExistingWorldGutterBridges`. Needs Cylinder World 0.11.3 or newer, which stops flattening its seam band there.
- 0.13.1: ore evidence v3 adds an authored best-guess layer (480 mines, districts and basins from general knowledge,
  weighted below survey data) for regions the public data misses: Tavan Tolgoi, Moatize and Nord-Pas-de-Calais coal,
  Inkai uranium, Sicilian sulfur, Kibali gold, gemstone districts (Mogok, Ratnapura, Minas Gerais, …). Existing
  worlds pick it up in regions not yet generated.
- 0.13.0 (world layout remains v11; tree/fauna/ore rules v2, geology rules v2 for new worlds): a review of the
  0.9–0.12 regional features fixed:
  - **Saves never fail to load** because of a game or mod update, a data change or a missing hook: the system
    concerned logs and falls back. `UpgradeExistingWorld*` flags are one-shot and can also switch a feature off.
  - **Ores**: clay, peat, quartz and starter copper/tin had collapsed to ~1–7% of vanilla (a vanilla ore map was
    being applied on top of Earth's chance); they are back to vanilla rates, with rate classes for progression ores
    (copper, tin, iron, coal) and the 0.12 effective rate for the rest. Tin searches the column for a host rock, the
    prospecting pick reads Earth's rocks, deposits above ground are skipped, gold/silver follow their own evidence.
    The evidence maps were rebuilt with tiered record weights, repaired polygons, more global sources and geological
    priors.
  - **Geology**: clay and peat are weighted instead of vetoed (relief in metres, river beds no longer count as cliffs);
    new worlds get rock layers at absolute depths (valleys expose lower beds), chalk with flint, kept kimberlite and
    suevite, patchy gravel, filled coasts and volcanic sequences, and contacts without the 0.5° grid. Existing
    regional worlds keep their v1 rock layers bit for bit.
  - **Trees**: native → regional stand-in → climate tiers (no bare forests), Earth climate envelopes for provider
    trees, spruce in the hemiboreal/boreal mix, corrected taxa, blended botanical borders, shared alias weights, a
    ~70× faster pick. **Ground plants** from Floral Zones follow native ranges (`/earth plants`).
  - **Fauna**: no startup failures without providers, corrected ranges (golden wolf, Snares penguin, Lord Howe
    swamphen, aurochs, Antarctic coast), 33 → 11 held taxa, generic carcasses, `FaunaRangeProfile` natural/current.
  - River lookup per terrain column ~12× faster (identical results); companion terrain sites are recorded in the save.
- 0.12.0–0.12.2: regional ore deposits from evidence heat maps, peat-based bog iron, coherent rock contacts; river
  grid index for very large worlds; local terrain expansion for companion mods.
- 0.11.0: salt-evidence and deposit-gate corrections.
- 0.10.0 (world layout remains v11): independently versioned regional geology and optional fauna, with saved
  material mappings, native-range evidence, coverage diagnostics and configurable extinct animals. Metal ore
  evidence maps are research inputs; geographic metallic ore generation is deferred.
- 0.9.0: native botanical-region trees with optional Floral Zones and Wildcraft/Herbarium mappings.
- 0.8.0 (world layout v11): river widths grow with the world's size (a larger world keeps the same proportions;
  `RiverWidthScale` still multiplies on top); straighter courses for the wide rivers: loops that would touch
  themselves are cut off like meander necks (`RiverStraighten`, 0..1) and bends shorter than a few river widths are
  smoothed with a Gaussian along the river (`RiverSmoothWidths`, default 2), keeping confluences, mouths and lakes in
  place. The river audit follows the courses as laid out.
- 0.7.0 (world layout v10): rivers 1.5 times wider (Daugava ~21 blocks at its mouth, Rhine ~31, Danube ~42, Amazon
  ~72) and a block deeper; navigable from 100 m³/s (was 150: about 25 % more river); each river continues upstream
  along its main branch as a stream narrowing to one block at 20 m³/s instead of starting at full width (`Streams`:
  `ends`, or `all` for every stream down to `StreamMinDischarge`, or `none`). The data now holds rivers down to
  20 m³/s (620 000 reaches) so much larger worlds can use more of them. Existing configs get the new river defaults.
- 0.6.0 (world layout v9): permanent snow above the snow line (where no month is above freezing; bare rock on
  cliffs); seas at least 5 blocks and lakes at least 4 blocks deep away from their shores (Neva Bay, the Gulf of
  Riga, highland lakes such as Lake Victoria were 1-2 blocks); near water the terrain noise no longer dips land
  below the water level (lakes ended along data-cell edges, e.g. south of Kilimanjaro); a lake downstream on a river
  is never higher than the lake upstream (Lake Constance's Untersee was 16 m above the Obersee, so the Rhine ran
  uphill between them); fjords and sounds stay connected water (at 2' they were averaged with their walls and fell
  apart into pools); where water meets water 2+ blocks lower (a lake above a fall, rivers touching at different
  levels) a curtain of falling water joins them instead of a standing wall; no rivers on the ice sheets (HydroRIVERS
  draws flow lines across Greenland's ice); shallow-water structures stay out of rivers; dammed sea arms (the
  IJsselmeer, the Lauwerszee) are sea again, without the land strips their dikes left; a deep-sea smoothing in the
  bake no longer lifts deep fjords (Sognefjord) above sea level; reservoirs whose HydroLAKES name isn't a lake's are
  removed (Nyumba ya Mungu below Kilimanjaro), with a short list of dammed natural lakes kept (Hongze, Skagern).
- 0.5.5: `/earth tp` aboard a boat or ship (or with `water` at the end) lands on the nearest open water instead of an
  island; a client fix for a game race that crashed the client ("Index was outside the bounds of the array" in the
  network thread) when map chunks load and unload at the same moment, as when crossing Cylinder World's seam.
- 0.5.4: water in the blend zone of a flattened story site stays water.
- 0.5.3 (world layout v8): story sites keep their flattened area clear of rivers (the treasure hunter's site had cut
  the Rhine at Düsseldorf) and flattening never removes river water; lakes keep their level when the river leaving
  them falls steeply (Niagara had pulled Lake Erie and Lake St. Clair 5 blocks down); lake and sea points in the
  river data keep their exact level (the Detroit and Niagara rivers had taken the Great Lakes' bottom); beach edges
  follow the ground instead of the data grid (no more rectangles on flat deltas); `/earth checkrivers` checks every
  river of a world.
- 0.5.2 (world layout v7): every river that ends near sea level is continued along the lowest ground to the open
  sea, through estuaries that ETOPO has as flat land (the lower Yenisei, the Gambia, Chesapeake's James River, Suisun
  Bay) and down fjord valleys: 780 of 866 river ends reach the sea, 23 end in lakes, 60 in inland basins, 2 small
  fjord rivers (Namsen, Arnaud) still stop short. Under water the terrain's detail noise no longer rises above the
  surface (no false islands closing bays and fjords).
- 0.5.1 (world layout v6): river mouths open into the sea (HydroRIVERS lines that stop short of the coast are continued
  to it; the Daugava, Seine and Loire ended inland), channels keep their depth through shallow estuaries, the water
  between neighbouring columns is settled like VS's liquid spreading (no step without a ramp, no water standing above
  the bank), no bare-rock blobs at shores, no ruins in rivers (the whole footprint is checked).
- 0.5.0 (world layout v5): navigable rivers — pools with flowing-water ramps at the steps (placed after vanilla's
  block layers, so no more soil at the steps), rapids where the real river is steep, the same channel depth all along,
  no modern dams or reservoirs, rivers meeting lakes, seas and each other at the same level; the Nile belt wider with
  steppe at its edge; a hot-dry forest type (acacia, prickly moses, ebony instead of maples and oaks); shrubs follow
  the rainfall; `/earth boattest` builds test canals for boats.
- 0.4.0 (world layout v4): large rivers (HydroRIVERS) with navigable widths, flowing water at steps, river banks and
  green belts in deserts.
- 0.3.0 (world layout v3): stronger height curves tuned for world height 384, real seasonal temperature swing per
  place, dry-summer (Mediterranean) climates, forest types for the species mix, no ruins on the sea floor and no swamp
  trees in the sea. Algernon's Watersheds is incompatible (both replace the terrain generator; Earth Map logs an error).
- 0.2.0 (world layout v2): beaches flush with the water with sand/gravel, land strips between coastal lakes and the
  sea, coastal lake levels snapped to sea level, rainfall recalibrated (pines in the north, rainforest trees in the
  tropics). Worlds created with 0.1.0 log a warning: new chunks use the new coasts and may not line up with old ones.
- 0.1.0: first proof of concept.

## Known gaps / next steps

- Weather and climate: see [docs/open-issues-weather.md](docs/open-issues-weather.md) (leftover climate stripes, model gaps, what is not verified yet).

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
