# Settings

Earth has settings in three places:

- **The world creation screen:** the **Earth** play styles, the **Earth** tab under **Customize**, and
  Earth's two start options on the game's **Player spawn and death** tab. This is where almost every player chooses.
- **A few hidden world options** that admins can set in a world's configuration, but that aren't on the screen.
- **The server's `ModConfig/earth.json`:** downloads, start-up and tuning, for server owners.

**Earth saves almost every setting into a world when it is made, and the world keeps it.** A few are read at every
start and are marked so below. Apart from those, changing an Earth tab option or `earth.json` later doesn't change a
world that already exists; only the one-shot upgrade switches at the end of this page do, and then only for land
generated from then on.

## The Earth play styles

On **Create New World**, choose one of the Earth play styles. Each sets the Earth's scale with the world height and
mountain heights that suit it:

| Play style | One block across | World size (blocks) | World height | Mountain heights |
| --- | --- | --- | --- | --- |
| Earth · 1:200 | about 200 m | 233 472 × 100 352 | 384 | Balanced relief |
| **Earth · 1:100** | about 100 m | 433 152 × 200 192 | 384 | Balanced relief |
| Earth · 1:50 | about 50 m | 834 560 × 400 896 | 384 | Balanced relief |
| Earth · 1:25 | about 25 m | 1 635 328 × 801 280 | 512 | Linear (real proportions) |
| Earth · 1:10 | about 10 m | 4 040 704 × 2 003 968 | 1024 | Linear (real proportions) |

They also set some vanilla options: survival, realistic climate, harsh winters, 9 days per month, temporal storms
"sometimes", no grace timer, and lightning fires on. You can change those on their own tabs as usual. The exception is
the Polar-Equator distance: Earth sets it to the world's real distance at its scale, whatever the tab says.

The play styles' codes, for a server's world configuration: `earth` (1:100), `earth-200`, `earth-50`, `earth-25` and
`earth-10`, all with the world type `standard`.

## The Earth tab

Defaults are in **bold**. Every option here applies when the world is made and can't be changed afterwards, except
**Terrain · GLO-90 land heights**, which is read each time the world opens (see [Hidden world options](#hidden-world-options)).
The key of each option, for a server without the screen, is in [The keys](#the-keys) below.

### Map

| Option | Values | What it does |
| --- | --- | --- |
| **Map · Earth size** | 1:200, **1:100**, 1:50, 1:25, 1:10, 1:5 (extreme), or the World generation tab's width and length | The Earth's scale. The world is the Equator at that scale east–west (it wraps around), pole to pole north–south. 1:5 is the most detail and the most to store; its server needs about 7 GB of memory while it starts. If you use the World generation tab's size instead, make the world at least twice as wide as long, or the Earth is stretched. |
| **Map · World height** | 256 to 1024 in steps of 64 (**384**; the play style sets it), or World Height at the top (0) | Replaces World Height at the top of the screen, which stops at 512. 0 uses that slider instead. |
| **Map · Mountain heights** | **Balanced relief (log 1000 m)**, Strongest lowland relief (log 250 m), Strong lowland relief (log 500 m), More room for high mountains (log 2000 m), Linear (real proportions) | How real elevations fit between sea level and Everest. The logarithmic choices give lowlands more height and squeeze the highest mountains; linear keeps real proportions, which needs a tall world (in a 384-high world it leaves the lowlands flat). |
| **Map · Sea level** | **110**, 128, 160 (tall worlds) | Blocks below sea level. More blocks below the sea leave less room for mountains above it. |
| **Map · Suez and Panama canals** | **off**, on | On: wide cuts at sea level for sailing from the Mediterranean to the Red Sea (with Lake Timsah and the Bitter Lakes) and from the Caribbean to the Pacific (with Gatun and Miraflores lakes, no locks). Off: the land as before the canals. |

### Terrain

| Option | Values | What it does |
| --- | --- | --- |
| **Terrain · GLO-90 land heights** | **Download all: Earth's data release (GitHub, 8.6 GB)**, Download all before the world starts (GitHub, 8.6 GB), Download as the world reaches them, Download all: Copernicus originals (71 GB, converted here) | Where the real land heights come from and when they are downloaded. They are kept once per machine and shared by every world. **Download all:** the whole set starts downloading when the world opens, with a progress window; you can play meanwhile. **Before the world starts:** a new single-player world waits on its loading screen until the download is done (Cancel goes back to the menu; opening the world again plays while the rest downloads); a dedicated server downloads while it runs. **As the world reaches them:** each 1° square (about 0.3 MB) when the world first needs it. See [Data and performance](Data-and-performance). |
| **Terrain · Summit height correction** | Off, 1,000 m to 8,000 m and higher (**3,000 m and higher**) | Restores the full height of listed peaks at or above this real elevation, which the height data tends to cut short. |
| **Terrain · Rock floor** | **Automatic**, None, 96, 128, 160, 192 or 256 blocks down | How far below the lowest ground in each 32 × 32 area the ground turns to plain rock, with no caves, ores or lava. It keeps tall worlds' saves small. Automatic: 160 blocks in worlds taller than 512, none otherwise. |
| **Terrain · Rocks and ground** | **Vanilla rock layers**, Ground columns: real soil, sediments and rock, Regional rock regions only | Vanilla: the game's own rock layers. Ground columns: real rock regions, and the ground built from real data (soil, the sediments below it in metres, sea-floor sediment, beach sand; in the Baltic also the real bedrock layers). The first such world on a machine downloads about 172 MB of ground data. Regional: real rock regions and surface materials, without ground columns. See [Rocks, soil and ores](Rocks-soil-and-ores). |
| **Terrain · Ground depth** | **Log, strong: 2,000 m about 30 blocks above the bottom**, Linear at the terrain's scale, Log, mild: 2,000 m at the bottom, Linear, 2,000 m at the bottom | Only with Ground columns: how real depth below the surface becomes blocks. With the other rock choices it does nothing. |
| **Terrain · Ores** | **Vanilla ore deposits, plus Earth's gems and ores**, Earth ore maps (experimental) | Vanilla: the game's own ore deposits, plus Earth's gems (beryl, corundum, garnet, spinel, topaz, tourmaline) and ores the game doesn't place, by the game's own deposit rules. Earth ore maps: ore districts from real geology and mining records; experimental and being reworked. |
| **Terrain · River rapids** | **At the drops of steep rivers**, Off | Where white water runs: at the drops of rivers whose real fall is strong for their size; a gentle current elsewhere. Off: no white water; waterfalls stay. |
| **Terrain · Mountain springs and streams** | **Cave springs only**, Real stream heads, Vanilla | Vanilla's springs in cliff faces and mountain-side streams come out at random spots on real terrain. Cave springs only: springs in cave walls only. Real stream heads: also a spring where each real all-year stream starts, and small springs in wet climates. |
| **Terrain · Surface ponds** | **Off**, Wetlands only, Vanilla | Vanilla digs small round ponds into hollows everywhere. Wetlands only: only in mapped bogs, marshes and swamps. Earth's own lakes and rivers aren't affected. |

### Nature

| Option | Values | What it does |
| --- | --- | --- |
| **Nature · Plants and animals** | **As around 1318**, Vanilla, by climate | As around 1318: plants and animals where they lived then, with the crops people had spread by then. Vanilla: by climate alone. See [Plants and animals](Plants-and-animals). |
| **Nature · Biomes** | **Earth (by real climate)**, Vanilla | Earth: forest and shrub density from the real vegetation zones, open prairie and steppe, treeless bogs, alpine turf and scree, dry grassland that greens after rain. Vanilla: the game's own forest and shrub maps. |
| **Nature · Include extinct animals** | **off**, on | With As around 1318: also animals that died out before 1318 (mammoths, cave lions, woolly rhinos) from supported animal mods. |
| **Nature · Chickens everywhere** | **on**, off | On: wild chickens wherever the climate suits them, so every region has feathers. Off: only where people kept them around 1318. |

### Weather

| Option | Values | What it does |
| --- | --- | --- |
| **Weather · Source** | **Earth's real climate**, Vanilla weather | Earth: rain, storms and winds from the real climate of each place and month. See [Weather](Weather). |

Two other mods put an option on the Earth tab: Hydrology's **Water flooding** (always there, since Hydrology is
required) and World Map's **Wmap: explore by sight**. Unlike Earth's own options, both can be changed after the world
is made. Their own wikis explain them.

## Start options (Player spawn and death tab)

The last two options of the game's **Player spawn and death** tab, below **Starting climate**:

| Option | Values | What it does |
| --- | --- | --- |
| **Earth · Start** | **Random place at the starting climate**, 36 capitals (London to Santiago), Coordinates below | Random: dry lowland anywhere whose real yearly mean temperature matches the **Starting climate** above it, the same place for the same seed. A capital or coordinates: the nearest dry, gentle land there. |
| **Earth · Start coordinates** | text | Used when Start is **Coordinates below**. See below. |

**Start coordinates** take what you copy from Google Maps:
- the numbers it shows when you click a place: `56.949600, 24.105200` (south and west negative);
- degrees, minutes and seconds: `56°56'58.6"N 24°06'18.7"E`;
- the map's link, with `@latitude,longitude` in it.

You can also type `latitude, longitude` in degrees. If the coordinates can't be read, the world starts at a random place
at the starting climate, and the host (or a server's admins) are told why in chat. A short share link
(`maps.app.goo.gl/...`) has no coordinates in it: open it and copy the numbers instead.

These two options are not on the Earth tab because the game's text field doesn't draw correctly in a list that
scrolls, and the Earth tab does. On a window too small for the Player spawn and death tab (1024 × 600), coordinates
scrolled out of view can draw below the dialog.

**Morning start:** a new world starts in the morning at its spawn. With **Astra Terra for Earth**, whose sun follows the
real longitudes, Earth moves the new world's clock forward once (by less than a day) so that the spawn's local time is the
game's start hour. Without it, the sky keeps one clock for the whole world and the start is morning everywhere anyway.

## Hidden world options

These are not on the screen. A server owner can set them in the world's configuration **before the world is made**:
on a dedicated server, in `serverconfig.json` under `WorldConfig` → `WorldConfiguration`, before the first start. Text
values go in quotes, also numbers: `"earthRainFrequency": "2"`; a bare number is dropped by the game.

| Key | Values | What it does |
| --- | --- | --- |
| `earthRainFrequency` | `"0"` to `"3"`; empty by default | Multiplies how often it rains (Earth weather only): `"1"` is the real climate, `"0"` no rain. Empty: `RainFrequency` from `earth.json` (1). |
| `earthStormFrequency` | `"0"` to `"5"`; empty by default | Multiplies how often storms form: `"1"` is the real climate, `"0"` no storms. Empty: `StormFrequency` from `earth.json` (1). |
| `earthElevation` | `auto` (default), `glo90`, `glo90coarse`, `builtin` | Where dry land's heights come from. `auto` and `glo90`: Copernicus GLO-90; `glo90coarse`: the coarser GLO-90 grid that comes with Earth's data download, no per-square downloads; `builtin`: the older built-in heights. |
| `earthSuezCanal`, `earthPanamaCanal` | `true` / `false` (default false) | The two canals on their own, from before 2.0.1. Read only when **Suez and Panama canals** is off. |

`/worldconfig <key> <value>` (alias `/wc`, admins) also changes these and the Earth tab's options in a running world,
but Earth only reads them when a world is made, so a change does nothing by itself. There are two exceptions:
- `earthGlo90Download` is read each time the world opens, until its download has finished or a player stopped it. So
  `/worldconfig earthGlo90Download needed` stops the automatic download from the next start, and `release` or
  `copernicus` starts one.
- `UpgradeExistingWorldCanals` below reads the world's canal options again.

## The keys

On a dedicated server there is no world creation screen. To choose Earth's options there, set their keys in the world
configuration (`serverconfig.json`, `WorldConfig` → `WorldConfiguration`) before the world's first start, with the play
style's code as above. Values are strings in quotes, except `true`/`false` and the world height. A key that is left out
gets its default.

| Key | Values (default first) | Option |
| --- | --- | --- |
| `earthWorldSize` | `"100"`, `"200"`, `"50"`, `"25"`, `"10"`, `"5"`, `"vanilla"` | Map · Earth size |
| `earthWorldHeight` | `384`, or 256 to 1024 in steps of 64; `0` uses World Height | Map · World height |
| `earthHeightCurve` | `"log1000"`, `"log250"`, `"log500"`, `"log2000"`, `"linear"` | Map · Mountain heights |
| `earthVerticalBudget` | `"110"`, `"128"`, `"160"` | Map · Sea level |
| `earthCanals` | `false`, `true` | Map · Suez and Panama canals |
| `earthGlo90Download` | `"release"`, `"first"`, `"needed"`, `"copernicus"` | Terrain · GLO-90 land heights (download all; before the world starts; as the world reaches them; Copernicus originals) |
| `earthSummitCorrection` | `"3000"`, `"off"`, `"1000"` to `"8000"` | Terrain · Summit height correction |
| `earthRockFloor` | `"auto"`, `"off"`, `"96"`, `"128"`, `"160"`, `"192"`, `"256"` | Terrain · Rock floor |
| `earthRocks` | `"vanilla"`, `"groundcolumns"`, `"regional"` | Terrain · Rocks and ground |
| `earthGroundDepth` | `"log4000"`, `"terrain"`, `"log2000"`, `"linear2000"` | Terrain · Ground depth |
| `earthOreDeposits` | `"vanilla"`, `"maps"` | Terrain · Ores |
| `earthRiverRapids` | `"steep"`, `"off"` | Terrain · River rapids |
| `earthMountainSprings` | `"caves"`, `"streams"`, `"vanilla"` | Terrain · Mountain springs and streams |
| `earthSurfacePonds` | `"off"`, `"wetlands"`, `"vanilla"` | Terrain · Surface ponds |
| `earthNature` | `"1318"`, `"vanilla"` | Nature · Plants and animals |
| `earthBiomes` | `"earth"`, `"vanilla"` | Nature · Biomes |
| `earthIncludeExtinctAnimals` | `false`, `true` | Nature · Include extinct animals |
| `earthChickensEverywhere` | `true`, `false` | Nature · Chickens everywhere |
| `earthWeather` | `"earth"`, `"vanilla"` | Weather · Source |
| `earthSpawn` | `"random"`, a capital (`"london"`, `"riga"`, `"buenosaires"`, ...: the name in lower case without spaces), `"custom"` | Earth · Start |
| `earthSpawnCoordinates` | `""`, or coordinates as on the screen | Earth · Start coordinates |

## `earth.json` (server owners)

`VintagestoryData/ModConfig/earth.json` is read at every start and written back with any new settings. If it can't be
read (a missing comma, say), Earth keeps your file, saves a copy beside it and doesn't start an Earth world until it's
fixed or deleted. A value outside its range is reported in the log and replaced by its default (`ElevationDownloads`
and `StorySearchRadius` are brought to the nearest limit instead).

Many settings only affect **new worlds**: they are saved into a world when it's made.

`RegionalGeology`, `GroundColumnsPrototype`, `RegionalOres`, `RegionalFauna`, `IncludeExtinctAnimals` and `Weather`
**don't decide for new worlds**: the world's own options do (Rocks and ground, Ores, Plants and animals, Include
extinct animals, Weather · Source), and the game fills in their defaults when a server's world configuration leaves
them out. To choose them on a dedicated server, set the option's key (see [The keys](#the-keys)). `RegionalGeology`,
`RegionalOres` and `RegionalFauna` are what the one-shot upgrades below apply to an existing world.

### Data and downloads (every start)

| Setting | Default | What it does |
| --- | --- | --- |
| `DataRepo` | Earth's `earth-data` release on GitHub | Where Earth's map data (about 289 MB) comes from the first time. A mirror or a local folder works; empty: never download. |
| `GroundRepo` | Earth's `earth-ground` release | Where the ground data for Ground columns worlds (about 172 MB) comes from. Empty: never download. |
| `ElevationRepo` | Earth's `earth-glo90` release | The first source of the GLO-90 land heights (already converted). Empty: none. |
| `ElevationSource` | Copernicus's own server | The originals, used for squares the release lacks and for "Copernicus originals". Empty: none. |
| `ElevationDownloads` | 3 | Height squares downloaded at the same time (1 to 8). |
| `ElevationPrefetchBlocks` | 4096 | Heights are fetched this far around players ahead of time. 0: off. |
| `ModelCache` | true | Keeps slow start-up results in `ModData/earth/cache`, so later starts are faster. The world is the same either way. |

### The world and its terrain (new worlds)

| Setting | Default | What it does |
| --- | --- | --- |
| `Enabled` | true | Lets new worlds of the Earth play styles become Earth. false: they stay vanilla. Existing Earth worlds keep working. |
| `SeaCurve` | 20 | How sea depth fits into the blocks below sea level; smaller makes shallow seas deeper. |
| `DetailScale` | 1.0 | Strength of the small terrain detail added on top of the real heights (0 to 10). |
| `SeamGutter` | false | true: a strip of open sea along the seam where the world wraps. false: the real Earth runs across it. |
| `GutterMargin` | 512 | Extra open sea on each side of the seam when there is a gutter. |
| `Islands` | true | Small islands from their real outlines. |
| `MinIslandBlocks` | 4 | Islands narrower than this are left out (1 to 64). |
| `Coasts` | true | Mainland coasts from their real outlines, in worlds big enough for it. |
| `CoastMinCellBlocks` | 32 | Outline coasts are used where one 2′ data cell is at least this many blocks wide (8 to 1000; 32: at 1:100 and finer, not at 1:200). |
| `SpawnSearchRadius` | 3000 | How far around the chosen start Earth looks for dry land. |
| `MoveStoryLocations` | true | Moves the story locations onto suitable real land near where vanilla puts them. |
| `StorySearchRadius` | 3000 | How far that search goes (64 to 50 000; it is tried at the radius, then at twice and four times it). |

### Rivers (new worlds)

| Setting | Default | What it does |
| --- | --- | --- |
| `Rivers` | true | Real rivers, with their waterfalls, channels, ends and deltas. |
| `RiverMinDischarge` | 100 | The smallest river in m³/s; raise it for fewer rivers (0 to 1,000,000). |
| `RiverWidthScale` | 1.5 | Multiplies river widths (0 to 20). |
| `Streams` | auto | Small streams: `ends` (rivers narrow to a stream upstream), `all`, `none`, or `auto` (`all` at 1:25 and finer, `ends` coarser). |
| `StreamMinDischarge` | 20 | The smallest stream in m³/s (0 to 1,000,000). |
| `RiverStraighten` | 0.5 | Cuts off tight loops where a widened river would touch itself (0 to 1). |
| `RiverSmoothWidths` | 2 | Straightens wiggles smaller than this many river widths (0: the real course; up to 50). |
| `RiverRelief` | 1.0 | How much of the land's relief a river's surface follows; lower gives deeper valleys (0 to 10). |

### Rocks, ores, plants and animals

| Setting | Default | What it does |
| --- | --- | --- |
| `RegionalGeology` | true | Regional rocks. No effect on new worlds (Rocks and ground decides); used by `UpgradeExistingWorldGeologyRules`. |
| `GroundColumnsPrototype` | false | No effect on new worlds (Rocks and ground decides). |
| `GeologyTransitionBlocks` | 96 | Half-width of the blend between rock regions, 0 to 512; 0 gives sharp contacts (new worlds). |
| `RegionalOres` | true | Earth's ore maps. No effect on new worlds (Ores decides); used by `UpgradeExistingWorldOreRules`. |
| `Ores` | (a group of settings) | Tuning for Earth's ore maps: how often, how big and how rich deposits are, coal basins, iron formations, rich districts, bog iron. For ore-map worlds; new worlds. |
| `RegionalTrees` | true | Trees and shrubs only in their native ranges (new worlds; off with Plants and animals: Vanilla). |
| `RegionalPlants` | true | Ground plants from plant packs (Floral Zones) only in their native ranges, in worlds made with Plants and animals: As around 1318; every start, new land only. |
| `RegionalFauna` | true | Animals only in their native ranges. No effect on new worlds (Plants and animals decides); used by `UpgradeExistingWorldFaunaRules`. |
| `IncludeExtinctAnimals` | false | No effect on new worlds (Include extinct animals decides). |
| `FaunaRangeProfile` | natural | Animal ranges: `natural`, `current` (present-day) or `1318`. Worlds As around 1318 use `1318` unless this is `current`. New worlds. |

### Weather and climate

| Setting | Default | What it does |
| --- | --- | --- |
| `Weather` | true | No effect on new worlds (Weather · Source decides). |
| `WeatherPace` | 1 | Weather hours per game hour (0.25 to 8): higher gives more weather systems per season, changing faster. New worlds. |
| `RainFrequency` | 1 | How often it rains (0 to 3), for every new world whose `earthRainFrequency` is empty. |
| `StormFrequency` | 1 | How often storms form (0 to 5), likewise. |
| `ExtendTemperatureRange` | true | Temperatures below −20 °C and above 40 °C where the real climate has them. New worlds. |
| `RealSeasons` | true | Each place's real seasonal swing instead of vanilla's latitude-only one. Every start. |

### When something is missing

| Setting | Default | What it does |
| --- | --- | --- |
| `AcceptMissingFeatures` | false | If part of a world's generation isn't available at a start (a game update changed something Earth relies on, a mod the world's blocks came from was removed, a data file is missing), the server stops before it makes new land and says what is missing. true: generate anyway, with vanilla's blocks in those places; new land may not match the old. See [Troubleshooting](Troubleshooting). |

### One-shot upgrades for existing worlds

Set one of these to `true` and start the server: the next existing Earth world that starts applies it to land
generated from then on, and the switch turns itself off again once the world is saved. Land already generated keeps
what it has, so a step or seam can show where new land meets old. A world made in that same start ignores them, and
they are switched off when it is saved: set one, then start the existing world, not a new one.

| Setting | What it applies |
| --- | --- |
| `UpgradeExistingWorldCanals` | The world's own canal options (change them with `/worldconfig` first: `earthCanals`, or `earthSuezCanal` / `earthPanamaCanal`). |
| `UpgradeExistingWorldIslands` | `Islands` and `MinIslandBlocks`. |
| `UpgradeExistingWorldCoasts` | `Coasts`. |
| `UpgradeExistingWorldGeologyRules` | `RegionalGeology` and `GeologyTransitionBlocks`: turns regional rocks on (or off) for new land. It doesn't add ground columns. |
| `UpgradeExistingWorldOreRules` | `RegionalOres` and the `Ores` tuning: turns Earth's ore maps on (or off) for new land. |
| `UpgradeExistingWorldTreeRules` | `RegionalTrees`. |
| `UpgradeExistingWorldFaunaRules` | `RegionalFauna`, for animals that spawn from then on. |
| `UpgradeExistingWorldMountainTerrain` | The current summit heights and alpine surfaces. |
| `UpgradeExistingWorldClimateData` | Newer climate data, when a release brings it. |

The other `UpgradeExistingWorld…` switches in the file bring worlds from Earth's early versions up to date and do
nothing in worlds made with Earth 2.0.

`SelfTest` is a test tool for developers; leave it off. `ConfigVersion` is the file's own version; don't change it.
`LandCurve`, `TopMargin` and `MaxSeaDepth` are only read by an old height curve the Earth tab no longer offers.
