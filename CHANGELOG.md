# Earth changelog

What each version changed. What the current version needs and does is in [README.md](README.md).

## 2.1.0 (16 November 2026)

New worlds get each place's real temperatures month by month and hour by hour, and the climate as it was around 1318
([decisions 0086](docs/decisions/0086-monthly-temperature-layer.md) and
[0087](docs/decisions/0087-climate-as-of-1318.md)). Worlds made with 2.0 keep their temperatures unless you opt in.

- **Each place's own year.** A new world reads every place's real mean temperature for each month (climate records on
  a grid of about 3.7 km) and runs a smooth curve through the twelve months. The seasons no longer follow one shape
  everywhere: Mumbai is hottest in May, before the monsoon, Delhi in June, Tokyo and Lisbon in August, San Francisco in
  September, and the tropics swing only a little. At 47 weather stations the monthly temperatures are now off by
  0.8 °C on average (2.0: 2.0 °C), and the warmest month is right at 40 of them (2.0: 30).
- **Day and night.** Each place has its real day-night range for the month: 15 °C and more in a desert, a few degrees
  on a cloudy coast or in a northern winter, instead of the game's one rule. The day follows the sun: coldest just after
  sunrise, warmest about two hours after noon, so long summer days and short winter days have their real shape, and
  the polar day and night swing gently.
- **Warm and cold spells** are as large as each place's real day-to-day swings at that time of year: small in the
  tropics, several degrees in a continental winter. The game's two world-wide random temperature swings (the same
  everywhere at once) are gone from these worlds.
- **The climate as around 1318.** A new world's temperatures, and everything they decide when the world is made (snow,
  crops, vegetation, tree lines, forests), are those of the early 1300s, before the modern warming and early in the
  Little Ice Age: about 0.5 °C colder over the globe, 1 °C in western Europe and the Alps, 1.4 °C in Moscow (2 °C in
  its winter), almost 2 °C in the Arctic; Greenland's Norse settlements keep their summers.
- **Data:** a server (or a single-player game) downloads the monthly temperatures once, about 58 MB, at the first
  start of a world that uses them. A game client downloads nothing: the server sends each player the land around them.
  Without the file (offline) the world plays with 2.0's temperatures that session and fetches it at a later start.
- **Worlds made with 2.0** keep 2.0's temperatures. To give one the new months, days and spells, set
  `UpgradeExistingWorldSeasons` to `true` in `earth.json` once and start that world. Nothing generated changes: its
  land and climate map stay 2.0's (today's climate), while the shape of its year is that of 1318 (its winter about a
  degree colder against its yearly mean in Moscow, more in the Arctic).
- `RealSeasons: false` in `earth.json` now gives vanilla's seasons and day-night swing in every world.
- `/earthweather` shows how the temperature here is made up: the month's curve, the day-night range and the hour's
  place in it, sunrise and sunset, the day-to-day spread and the spell now.

## 2.0.3 (15 November 2026)

- **Start and Start coordinates are on the game's Player spawn and death tab**, below Starting climate, which a random
  start uses; the separate Earth start tab is gone ([decision 0085](docs/decisions/0085-start-on-the-spawn-tab.md)).
  They are named Earth · Start and Earth · Start coordinates. The game's text field draws outside a list that scrolls,
  so it stays off the long Earth tab, which now scrolls cleanly. The keys and values are the same, so world
  configurations and saved worlds are unchanged.
- Start's description points to Starting climate instead of a "Spawn & Death tab"; the play styles' texts say where the
  start is.

## 2.0.2 (15 November 2026)

- **A new world whose first start stopped opens again** with Astra Terra for Earth. 2.0.1's morning start moved a new
  world's clock back for a spawn east of longitude 0, and the game's rift weather then failed the start of a world whose
  first start had stopped before it ran (Cancel while downloading before the world starts, a missing download, a
  refused start): every later start failed the same way. The clock now only moves forward, by less than a day, and the
  spawn still starts at 08:00 local. Worlds that started fine are unchanged.

## 2.0.1 (15 November 2026)

The world creation screen as the author asked after 2.0.0 ([decision 0084](docs/decisions/0084-world-screen-2-0-1.md)),
a choice to download the GLO-90 heights before a world starts, and a fix from the 2.0.1 playtest. Saved worlds are
unchanged.

- **Rocks and ground: vanilla by default**, in every Earth play style. Ground columns and regional rocks are still
  there to choose. With vanilla rocks, Hydrate or Diedrate keeps its own aquifers.
- **Rain frequency and storm frequency are off the screen.** Admins still set them: in earth.json (`RainFrequency`,
  `StormFrequency`, for every new world) or in a world's own configuration before it is made, as a string
  (`"earthRainFrequency": "2"`).
- **One switch for the Suez and Panama canals** instead of two. Worlds made before keep theirs; a server's world
  configuration that still names one of the two old keys (`earthSuezCanal`, `earthPanamaCanal`) gets that canal.
- **Start coordinates can be pasted from Google Maps**: the numbers it shows for a clicked place, degrees, minutes and
  seconds, or the map's link. Coordinates that can't be read give a random start and, for the host or the server's
  admins, a chat line saying why and what works, instead of a line in the server log only.
- Start and Start coordinates stay on their own Earth start tab: the game's text field draws outside a list that
  scrolls, which the Earth tab does.
- **GLO-90 land heights: "Download all before the world starts (GitHub, 8.6 GB)"**, a fourth choice
  ([decision 0051](docs/decisions/0051-glo90-elevation-on-demand.md), EA-0133). A new single-player world waits on its
  loading screen while every square comes from Earth's data release, with the progress there; Cancel goes back to the
  menu, and the world then plays while the rest downloads. "Download all" (play while it downloads) stays the default.
  A dedicated server downloads while it runs.
- **Ores: "Vanilla ore deposits, plus Earth's gems and ores"**: the option's name and text now say what it places
  (EA-0138). Earth's gems (beryl, corundum, garnet, spinel, topaz, tourmaline) and the ores the game doesn't place
  (such as chalcopyrite, tetrahedrite, wolframite and wulfenite) were always added by the game's own deposit rules;
  nothing generates differently.
- **A new world starts in the morning at its spawn** (the author, from the playtest). With Astra Terra for Earth, whose
  sun follows the meridians, a start far from longitude 0 began at night: the game's start hour is the same clock
  everywhere. The new world's clock now moves once, at most half a day, so the spawn's local time is the game's start
  hour (08:00), in single player and on a dedicated server. Without Astra Terra the sky keeps one clock and nothing
  changes; saved worlds keep their time.
- **The prospecting pick reads Earth's rock blocks** (from the playtest): marl, calcrete, gypcrete, saltcrust and hard
  laterite are propickable like vanilla's rock, so the pick gives readings on them (before, none at all where they make the ground,
  as on Latvia's Devonian marl with ground columns). Already generated worlds read them too.

## 2.0.0 (15 November 2026)

Earth 2.0 is one mod and makes new worlds. It sums up everything since 1.0.2.

### Install and compatibility

- **One mod** ([decision 0066](docs/decisions/0066-one-mod-data-from-github.md)): Earth Terrain Data, Earth Water Data
  and the test builds' height, water-fix and ground packs are no longer needed. If they are still installed, Earth
  copies its files from them instead of downloading them.
- The zip carries Earth's large config JSON brotli-packed and unpacks it when the game loads assets, which leaves room
  for the habitat steps ([decision 0081](docs/decisions/0081-packed-config-assets.md)).
- The client climate files (`climate.bin` and its fixes 1 and 2, `temperature.bin`, `rain.bin`, `aridity.bin`,
  `weather.bin`) put a spatial predictor in front of their compression
  ([decision 0082](docs/decisions/0082-predicted-climate-files.md), EA-0077): the same values to the bit, about 10 MB
  less of the zip. Worlds generate and their weather runs as before; each world's model cache is rebuilt once.
- **Needs** Vintage Story 1.22, Cylinder World 1.1.0 or newer (with 1.0.4 a new world whose first start stopped was
  taken for an existing one on the next; EA-0096) and Hydrology 0.6.2 or newer: its river query version 3 gives new worlds the calm rivers, and older
  Hydrology could let a water source spilled into the sea make the sea climb and flood the map.
- **Hydrate or Diedrate** ([decision 0059](docs/decisions/0059-water-table-and-hod-aquifers.md)): with HoD 2.5.8
  installed, a new world with Earth's own rock and ground gets HoD's aquifers from Earth's water table. With vanilla rock
  and ground, or vanilla biomes, Earth leaves HoD alone and HoD makes its own aquifers with its own defaults (the author,
  15 November 2026).
- **Data from GitHub:** the first start of a server (or a single-player game) downloads Earth's data once per computer
  into `VintagestoryData/ModData/earth` and checks every file: about 290 MB from
  [earth-data](https://github.com/Temporal-Poets-Society/earth-data) (`ed-v1`), about 170 MB of 30″ ground sets from
  [earth-ground](https://github.com/Temporal-Poets-Society/earth-ground) (`egts-v1`) for worlds with ground columns,
  and the GLO-90 land heights from [earth-glo90](https://github.com/Temporal-Poets-Society/earth-glo90) (`egl-v1`). A
  game client downloads nothing. Without internet the files can be put there by hand; a server that can't get them
  stops and says which are missing.
- **New worlds:** worlds from Earth versions before 2.0.0 (0.x, 1.x and the release candidates) are not supported:
  start a new world. From 2.0.0 on, saves are kept working.
- **Worlds from a newer Earth:** from this version a world that a newer Earth saved (a rules number above this build's,
  or settings this build doesn't know) is refused with one line, unchanged. Earlier versions (1.0.x, and the 2.0 test
  builds before this check) don't look: don't open a world in an older Earth than the one that last saved it, or its
  new chunks get the older rules for good (S-L3 of the code review of 14 November 2026).
- **For game updates:** every Harmony patch and non-public game member Earth uses goes through one table
  (`PatchVanilla`, 46 features). If a game update removes or changes one, Earth logs one line naming the game version
  and the feature. A world whose saved rules use a part of generation that is off then (that hook, a competing mod, a
  missing file) stops before its first chunk and names it ([decision 0083](docs/decisions/0083-generation-starts-with-every-feature-or-not-at-all.md));
  `AcceptMissingFeatures` in earth.json lets an owner generate without it. If Earth's own terrain can't hook in, Earth
  refuses the world instead of making half-Earth chunks.
- **Maps see the craters** ([decision 0079](docs/decisions/0079-maps-read-the-surface-as-generated.md), EA-0071): maps can
  read `TerrainModel.GetSurfaceColumn`, the terrain column with the summit craters and lava lakes that a later step
  carves, so World Map (0.5.3-rc.4 and later) draws them; before, every predicted view showed the cones uncarved. A map
  never plans a volcano from built-in heights. Maps can also read `TerrainModel.GrassSeasonAt`, a place's grass season
  map and its wet and dry seasons, so World Map's far view (0.5.3-rc.5 and later) colours grass as the client does.
  Generation is unchanged.
- **What other mods read from Earth is pinned** (EA-0132): [docs/REFLECTION-SURFACE.md](docs/REFLECTION-SURFACE.md) lists
  every member World Map, Hydrology and Cylinder World read by reflection, and a test checks each the way its mod looks
  it up, so a renamed or changed member fails Earth's tests instead of those mods in a world.

### World size and heights

- **One play style per scale:** Earth · 1:200, 1:100 (the default), 1:50, 1:25 and 1:10, and 1:5 on the Earth tab,
  each with the world size, height and mountain curve for its scale
  ([decision 0068](docs/decisions/0068-world-size-by-scale.md)). The world is as long east-west as the Equator at that
  scale (Cylinder World's ring) plus 16 384 blocks of hidden seam ocean beyond each end, and pole to pole north-south:
  1:10 is 4 040 704 × 2 003 968 blocks, 1:5 8 047 616 × 4 007 424.
- A world saves its own equator-to-pole distance as `polarEquatorDistance` (about 10,000 km over its scale), so Rough
  Seas' metres per block and anything else that reads it match the map
  ([decision 0072](docs/decisions/0072-polar-equator-distance-from-the-scale.md)).
- **Copernicus GLO-90 land heights** at every scale, with coast, lake and river outlines drawn on them.

### Rivers, lakes and coasts as of 1318

- **Rivers on the real channel** ([decision 0063](docs/decisions/0063-rivers-follow-the-real-channel.md)): rivers follow
  the satellite water mask's river water.
- **Flow** ([decision 0069](docs/decisions/0069-river-flow-field-canals-and-shore-zones.md)): the mask's river water
  flows as solved on it (`rivers-flow-glo90-1.bin`). Dead-end arms and backwaters are still water, the current turns
  smoothly, and a mouth reach follows its own water to its own mouth.
- **One bed** ([decision 0073](docs/decisions/0073-river-water-one-bed-and-flow-reach-by-reach.md), channel rules 4):
  wide river water is one bed, without pits, raised patches, straight edges, rings or grooves where the mask, the
  course and lakes meet; small pools in a river join it; side channels around islands stay open; channel sides are no
  steeper than 2 blocks a block; water is still or flowing reach by reach, so nothing flows into a dead end and lakes
  don't flow out like rivers.
- **One width** ([decision 0074](docs/decisions/0074-one-width-along-rivers-no-mask-ponds.md), channel rules 5): a
  river keeps one width along it instead of the mask's chains of ponds (the Lielupe, the Oder). Real wide reaches, side
  arms, backwaters, lakes and rivers wider than about 450 m stay as the mask has them.
- **River mouths** ([decision 0071](docs/decisions/0071-river-mouths-without-dredging.md)): no dredged ship channel
  out to sea where the coasts come from outlines (1:100 and finer); a river's bed and the sea floor meet as one bed.
- **Dry rivers:** a river is dry in a hot desert only where GIRES (Messager et al. 2021) has it stopping 30 days a year
  or more (`rivers-nonperennial-2.bin`, in the mod).
- **Fish in rivers:** an asset patch (`assets/earth/patches/fish-rivers.json`) lets vanilla's freshwater fish spawn in
  flowing water and rapids too; before, rivers had none.
- **Reservoirs:** Global Dam Watch's reservoirs that HydroLAKES lacks are left out too (7,233 more; dam rules 3).
- **Works after 1318 undone:** 26 canals are land again (the Kiel Canal, the North Sea Canal, the Karakum and others,
  from OpenStreetMap's outlines; works rules 2), and Maasvlakte 2 is sea again; the Corinth, White Sea–Baltic and
  Ismailia canals are undone, the Nieuwe Waterweg is kept (it follows open water of 1250); the sea over Rotterdam's
  port has no ridges along today's moles (works rules 3 and 4). Natural lakes and rivers along the canals stay.
- **Beaches** ([decision 0070](docs/decisions/0070-shore-strip-and-beach-material.md)): the sea's and big lakes'
  shores are a beach strip of real width at any height (sand about 40 m, gravel 20 m, rock 8 m). Its material is
  OpenStreetMap's where it has a beach there (`shores-osm-1.bin`), else the coast's own ground. No grass or land plants
  grow on it.
- **Shores:** no trees or shrubs on beaches, on a low coast's first 40 m, a river's low bank or a lake's sedge shore; no
  trees on the next 60 m of a coast, and the forest thins in beyond; steep scoured rocky coasts (Bergen) are bare rock
  (shore rules 3).

### Ground, climate and nature

- **Trees by site** ([decision 0080](docs/decisions/0080-habitats-by-site.md), site rules 1, new worlds): in Europe the
  tree and shrub mix follows each place's own wetness (from the water table) and soil richness and pH (from the ground
  columns): pine and birch on raised bogs and dry heath, alder in wet hollows, beech, oak, ash and lime on fresh rich
  ground. Everywhere, trees grow smaller on poor, dry or waterlogged ground.
- Ground columns from real soil and geology (the default), a rock floor, soil by aridity, biomes, alpine turf and arid
  floodplains.
- Earth's ore maps put ores at their real size in their ground layers (experimental, on the Earth tab; the default ores
  are vanilla's). Fire clay is no longer put back under coal (ore layers v2), and the Urals' and Kondyor's platinum
  placers place ore in their valleys (ore rules v5).
- **Terra rossa only on warm karst** ([decision 0076](docs/decisions/0076-warm-karst-terra-rossa-and-surface-flint.md),
  ground rules 2): karst cooler than a yearly mean of 12 °C has rendzina on limestone rubble, and vanilla's bauxite
  layers become the region's own rock under either ore choice, so there is no orange clay or bauxite high in the Alps. **Loose flint in the glaciated north and chert on karst** (ore layers v3): flint
  lies on till, outwash, river beds and gravel shores of the old ice sheets, chert on limestone.
- **Refractory bricks without bauxite** ([decision 0077](docs/decisions/0077-refractory-bricks-without-bauxite.md),
  crafting rules 1, in worlds where Earth decides where bauxite lies): grog from fired bricks, fire clay from clay with
  grog or crushed quartz, a plain fireclay brick (fire clay, quartz, grog) for tier 1, and crushed kyanite, sillimanite,
  andalusite or corundum in bauxite's place at every tier. Vanilla's recipes stay. **Kyanite, sillimanite and
  andalusite** (ore layers v4): new ores with loose markers in the schist, gneiss, quartzite, slate and phyllite of
  metamorphic belts. **Volcanic-ash mortar:** slaked lime and tephra give 6 mortar in a barrel.
- **Volcanoes inside** ([decision 0078](docs/decisions/0078-volcano-interiors.md), geothermal rules 3): the GVP's
  volcanoes have interiors in real proportions: a cone of the volcano's own layered rock, a lens-shaped magma chamber,
  a conduit with a cupola and a funnel up to the crater, dykes and a sill at 1:50 and finer, and no caves right beside
  them, in basalt, andesite or rhyolitic rock by the volcano's magma.
- Plants and animals where they lived around 1318.
- **Biomes: Earth / Vanilla** on the Earth tab ([decision 0075](docs/decisions/0075-biomes-back-to-vanilla.md)):
  Vanilla turns Earth's biome rules (vegetation zones, wetlands, fire openings, ground cover, ground moisture, winter
  grass, alpine surfaces) back to vanilla's for a new world. Terrain, rivers, coasts, rocks and ores stay Earth's. It
  works with either Nature choice.
- **Climate fix v3** ([decision 0018](docs/decisions/0018-climate-stripes.md)): the land's rain fades into the ocean
  data over a 1° coast band, so soil and plants no longer change in straight lines along the Arctic coasts, Central
  America and SE Asia (227 such stretches, now 67, nearly all real relief).
- **Climate fix v4** ([decision 0018](docs/decisions/0018-climate-stripes.md), EA-0060): the desert coasts of Peru, Chile
  and Namibia are as dry as CHELSA has them (Lima 434 → 60 mm a year); the coarse ocean data had put the Andes' rain on
  them.

### Weather

- Earth's own rain and thunder; weather rules 6 add cloud decks over cold seas (Peru, Namibia), hail from Earth's
  storms and the weather in the temperature, and give the same weather on every operating system.
- Weather rules 7 for new worlds ([decision 0017](docs/decisions/0017-weather.md)): high ground rains and snows on as
  many days as its real stations (the rain rate reads the ground's temperature: Denver, the Great Basin, Iran); no rain
  on the desert coasts; the desert Southwest's summer thunderstorms (Phoenix about a fifth of July's days); fog and mist
  from Earth's weather instead of the patterns' (sea fog on the cold coasts, valley fog on clear calm nights after rain);
  and the clouds drawn as thick as Earth's cloud cover, so their edges follow its rain areas.
- Weather rules 8 for new worlds ([decision 0017](docs/decisions/0017-weather.md), EA-0020, EA-0073): rain comes in
  showers of a few hours as at the airports (a cool month's rain had lasted 5-12 hours at a time), through the whole day
  rather than mostly in the afternoon, and in warm months as a few downpours and much light rain (fewer rainy days reach
  1 mm: the Great Plains, Florida, Tokyo); Perth's, Adelaide's and Cape Town's winters rain on more days. The month's
  rain hours and millimetres are as before. The weather's warmth and chill is worked out once per 32 blocks and ten game
  minutes, a twenty-fifth of its cost. `/earthweather hail` says when hail next falls here. Hail hurts as vanilla's
  large hail does (it almost never did).
- Weather rules 9 for new worlds (EA-0074): West and Central Africa get the thunder their airports report (Douala about
  210 thunder days a year, Abuja 145, Bamako 115; a third more than before), without more rain.
- A client whose `weatherground.bin` or climate fixes differ from the server's keeps vanilla weather instead of making
  other weather than the server (EA-0118). `/earthweather hail` searches the year off the main thread and answers when
  done. The drawn clouds, wind, hail, fog and thunder use the rain's own hour; the clouds' drift cancels vanilla's own push.
- Earth's hail is drawn and heard in the game (EA-0101): it hurt but showed as rain, because vanilla's weather tick
  overwrote it with its own event.
- The weather costs less (the code review of 14 November): the client works out the fog, cloud tiles, region winds and
  hail once instead of every frame or per asker, vanilla's wind lookup is no longer run and thrown away, and storms out of
  reach cost nothing; older worlds' weather is unchanged bit for bit.
- Earth's weather role in the open sea contracts (`seacontracts:weather`) lets Rough Seas or any other mod read Earth's
  wind, storms and rain without Earth's DLL; it claims itself again if it goes missing. It also gives the sea-level
  air pressure (`pressure`), the barometer World Map reads.

### Earth tab and commands

- The Earth tab has choices for surface ponds, mountain springs and biomes. Start and Start coordinates are on a tab
  of their own (**Earth start**), since the game's text field turns clipping off for everything drawn after it in a
  scrolling list.
- `/earth relight` (above) and `/earth cost`, which measures what Earth's climate hook costs in the running game.
- `/earth site` shows the site drivers where you stand ([decision 0080](docs/decisions/0080-habitats-by-site.md), step 1):
  heat, light on the slope, water supply, soil richness, wetness from the water table, disturbance, and the site type
  they make (dry heath to alder carr), and what plants read from it.
- `/earth woods` counts the trees around you by wood, or with `trace` the tree and shrub picks of new chunks;
  `/earth siterules 0|1` switches the site rules for this session only, to compare with `/wgen regenk`.
- `/earth` subcommands that don't need a position run from the server console. The download status shows the data
  source, its errors and the disk space to admins only.
- `/earth tp` into a square whose GLO-90 heights aren't on this computer yet waits for them at most 2 minutes
  (EA-0121). If they can't be had, it says why (the release unreachable, say) and doesn't teleport you over land that
  can't be made yet; a square no source has still takes the built-in heights, as before.

### Faster start

- The slowest steps of building a world's map model and the story locations' site search run on several cores (half
  the logical processors, at most 8), with the same result as one core
  ([decision 0067](docs/decisions/0067-faster-start-parallel-build-and-model-cache.md)). A new 1:10 world on the test bench joins in about 55 s.
- **Model cache:** the straits' courses and the sea channels' ways (about 600 KB a world) are kept in
  `VintagestoryData/ModData/earth/cache` and read back at the world's later starts (the same world joins in about 29 s).
  They are used only for exactly the same Earth build, data, runtime and world settings; `ModelCache: false` in
  `earth.json` turns the cache off. The world is the same either way.
- A 1:10 server peaks at about 5.5 GB while it starts, then holds 4.5 to 4.8 GB; 1:5 builds its model in about 29 s and
  peaks at about 7.2 GB.
- **Less memory** (EA-0087): the climate is kept as the data files store it and only the climate fix a world uses, on
  servers and clients (a new world's climate 104 → 44 MB), and terrain tiles keep their heights and ice as 16-bit codes
  (114 → 73 MB at the 320 tiles a server keeps). A new world's server holds about 100 MB less after spawn (1:10 and
  1:5), clients about 60 MB less. Every value is the same. At a server's start (EA-0108) `rivers.bin` is read only by
  worlds on the old river courses (135 MB less until the river network is built, every new world), and once the sea
  referee's islands and coasts are built the ones as read are let go (56 MB less for the session). The surveyed peaks
  (`peaks.json`) take 18.5 MB instead of 50.7 MB (EA-0116 D-R4), with the same summits.
- **Faster around the sea of 1318** (EA-0095): columns where works rules 3 undo the Dutch delta's land (Rotterdam's port,
  the Rhine-Maas mouths, Flevoland, the Wadden Sea) search the coast of 1318 with far fewer lookups: Rotterdam about
  270 → 50 µs a column at 1:100 and 390 → 145 at 1:10, the delta about 5× faster. The terrain is the same block for block.
- **Faster on large islands and wide rivers** (EA-0113): a column inside a large island searches the coast near it
  instead of every edge within half the island's width, and asks for its island once: at 1:10 Åland about 49 → 2 µs a
  column, the islands off Marajó 16 → 6, Maine's 8 → 2. The mask's river water no longer allocates a closure per column
  and skips pieces out of reach before measuring them; the rock strata below every column's top are written a layer at a
  time (the pass 3.1 -> 2.0 ms a chunk at 1:10, 1024 high). The terrain is the same block for block.

### Fixes

- **No straight seams along whole degrees in new worlds** (GenFixRules 5, island rules 4; Fable pass): wide rivers
  read their flow across the 1° tiles' edges (the Amazon on 3° S had a bed step of up to 4 blocks along it); lake shore
  strips follow the whole lake, not its piece in one tile; islands crossing 180° (Taveuni, Rabi) have no notch down to
  the sea along the meridian. Worlds made before keep their terrain (pinned by `SavedWorldDigestTests`).
- **The prospecting pick finds child ores again** (EA-0075): on Earth's own rock it read silver, gold, sylvite and
  peridot as nothing, because Earth's rock replaced the parent ores (quartz, halite, olivine) in the pick's simulated
  column. Those ores now stay, within the layers the deposit may use; `/earth propick [radius]` shows the question
  per column for admins.
- **An Earth world never generates without Earth:** a start whose `earth.json` can't be written back, whose Earth or
  world-generation system failed to start, or whose terrain model failed to build stops the server (one error line). A
  chunk whose terrain failed, or that vanilla's terrain made, is noted in the save and generated again at the next
  start. An Earth save started without Cylinder World is refused. A new world whose first start stopped (no internet
  for Earth's data) is created on the next start.
- **Downloads:** a brief network failure no longer turns a world's GLO-90 squares, outlines or ground data into a
  permanent fallback; one lock for every download into `ModData/earth`; a GLO-90 square that takes too long no longer
  holds chunk loading; a failed prefetch waits before it is asked again; an offline 1.0.x world needs only the files its
  rules read. The GLO-90 download leaves 1 GB free on its disk (EA-0100): "download all" doesn't start without room for
  what is missing and stops at the first square that can't be written, a square fetched as needed is not fetched without
  room, and a square the store can't take is no longer fetched again from Copernicus (on a full disk the download used
  to read four times its size and start again at every opening). A square for chunk generation leaves only 100 MB free,
  and a write refused for a moment (a virus scanner) is tried again before the store counts as failed. Every GLO-90 tile
  has egl-v1's heights (EA-0106), judged by a table of the release's heights checksums in the mod (212 kB), not by file
  bytes, which differ between runtimes: a square converted from Copernicus is kept only with the release's heights, else
  the release's tile is fetched; a tile already on a computer is checked at its first read in a session, and one with
  other heights stays in use while the release's is fetched beside it to take its place at the next start (the old kept
  as `.differs`), so two computers make a world on the same heights. No download or GeoTIFF is taken larger than it can be (a mirror's
  manifest 16 MB, a tile 8 MB, a GeoTIFF 64 MB), and a GeoTIFF of other dimensions than GLO-90's is not kept. Earth's data
  store (289 MB) is checked against its SHA-256 in the read that loads each file instead of a pass of its own at every
  start (EA-0092 A6); a damaged file is still set aside and fetched again before it is used. A local source folder that
  isn't there (a drive not mounted, a typo in the address) is an error at once, naming the setting and the folder, never
  a source that lacks the files (which kept a new world on the 2′ ground sets for good). A square from Copernicus no
  longer waits for the release's manifest first. The Earth tab's "download all", when an opening can't start it (no
  room, no source set), is told to players who control the server, in chat and in the elevation dialog, not only in the
  log.
- **No built-in heights in a GLO-90 world while it runs** (EA-0099): a chunk whose GLO-90 square can't be downloaded
  while the game runs (the network, the release down, a full disk) is no longer made on the built-in heights, which met
  the chunks made later from GLO-90 with a step for good. It is left empty, noted in the save and made at the next start
  with the square's heights; players get a chat line saying the chunks are holes to keep away from and not to build in,
  since anything built there is deleted when they are made (at most every 10 minutes a square), and the square is tried
  again 5 minutes after a fetch failed. A volcano's planning that reaches the
  square leaves the chunk for the next start too. Map regions read there still take the built-in heights for that read.
- **Terrain** (`GenFixRules` 1 to 3, new worlds): dry land below 0 m (the Caspian lowland, the Jordan valley, Death
  Valley, polders) continues from the land at 0 m without a ledge; springs and seeps sit on whole blocks; crops and
  fruit trees in a river's green belt are judged by its rain (the Nile's onions and peaches); dry river beds' gravel is
  scattered, not striped; no crease where the noise's dips switch near a water level (Lake Geneva); a grid lake on flat
  low ground ends along an irregular shore, not a straight one between the 2′ cells (Lake Geneva at the Rhône's mouth
  on the built-in heights).
- **No black rock where snow melts:** vanilla melts snow blocks without relighting, so rock along the snow line lay
  pitch-black in daylight; Earth now relights each melted cell (every Earth world). `/earth relight [radius]` lights
  patches dark from before.
- **Hydrology:** its river query says "rapids" exactly where Earth draws white water and gives a river column's bed and
  surface (`RiverSpanAt`); Hydrology is never handed a stand-in rapids answer, and the query settles far fewer chunks.
- `/wgen regen` keeps rules v4's ore catalogue and the companions' terrain plan; a world upgrade requested in
  `earth.json` survives a start that fails; the climate correction no longer adds up in vanilla's catch-up loops;
  runtime and client patches (grass, ground moisture, season tint, thunder, the creative date) go in whole or not at
  all; a closed single-player world no longer stays in memory; a rivers file out of flow order is refused at load.
- The code was restructured into small parts with one job each and reviewed (Codex, an adversarial review, the code
  review of 11 November 2026: [section 11](docs/research/code-review-2026-11-11.md#11-fixes-12-november-2026)); all
  offline test suites pass.

### New data files

- On the GLO-90 release `egl-v1`: `rivers-flow-glo90-1.bin`, `rivers-outline-glo90-4.bin`, `lakes-glo90-2.bin`,
  `dams-glo90-3.bin`. On `ed-v1`: `shores-osm-1.bin` (2.2 MB). In the mod: `rivers-nonperennial-2.bin` (20 kB),
  `climatefix4.bin` (13 kB) and `weatherground.bin` (40 kB). The
  server fetches them with the rest of Earth's data.

## 1.0.2 back to the 0.16 play-test builds

The notes as they were written for each version.

**1.0.2 (published on Mod DB 2 November 2026):** vanilla's sea-level deposits (saltpeter, most iron and coal, sulfur, borax, gems…) follow
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

## 0.1.0 to 0.16.1

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

- 0.15.0 (not released yet): Earth's weather ([decision 0017](docs/decisions/0017-weather.md), [README](README.md#weather)):
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
  Known issue: ore balance, see [Known gaps](README.md#known-gaps--next-steps).
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
