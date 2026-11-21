# Commands

Earth adds two chat commands on the server, `/earth` and `/earthweather` (short: `/ew`), and one on the client,
`.earthelevation`. Most of them work only in an Earth world; elsewhere they say that Earth isn't active there.

Who may use what:
- **Everyone** (the `chat` privilege): the information commands and `/ew`.
- **Teleport** (`tp`): `/earth tp`.
- **Time** (`time`): `/earth date`.
- **Admins** (`controlserver`): the downloads, the story locations, the world map and the test tools.

## Where am I? (everyone)

Each prints what Earth knows about the place you stand on.

| Command | What it tells you |
| --- | --- |
| `/earth` or `/earth info` | Latitude and longitude, the real elevation in metres, land, sea or lake, and the climate: the yearly mean temperature at the surface and at your height, the coldest and warmest month, and the yearly rain. It says so when the heights here are still approximate because the land heights for this square haven't downloaded yet. |
| `/earth where` | Which botanical region the place belongs to, which sets the trees, plants and animals. |
| `/earth trees` | The native tree region and the installed trees that can grow here (the climate still decides). Only in worlds with plants as around 1318. |
| `/earth plants` | The native range of ground plants from plant packs (Floral Zones) here. |
| `/earth placement` | Plants as around 1318 here: the region, the climate, and the crops and plants that belong. |
| `/earth fauna` | The animals that can spawn here and their ranges. |
| `/earth geology` | The regional rocks and surface materials here. Only in worlds with Earth's rocks (regional rock, or ground columns). |
| `/earth ores` | The ore evidence here. Only in worlds with Earth's ore maps, which are experimental and not on the world creation screen. Evidence doesn't promise ore: you still have to prospect. |
| `/earth water` | The water table: how deep the ground water is here, and the aquifers Hydrate or Diedrate gets. |
| `/earth site` | What the place is like for plants: warmth, light by slope, water, soil, wetness. |
| `/earth volcano` | The nearest volcano: its name, distance, summit and crater, lava lake and how far its ash reaches. |

Example: walk somewhere new and type `/earth info`.

## Travel

### `/earth tp <latitude> <longitude> [water]` (teleport privilege)

Teleports you to a real place, by latitude and longitude in degrees (south and west negative). You land just above the
ground.

- `/earth tp 51.5 -0.12`: London.
- `/earth tp -33.86 151.21`: Sydney.
- `/earth tp 59.9 10.7 water`: the nearest open water to Oslo, within 3000 blocks, on the surface. In a boat or on a
  ship, Earth looks for water by itself.

If the land there isn't loaded yet, Earth asks the game for it through its ordinary loading queue, the way your view
does, and teleports you as soon as it is ready. Teleports take their turn one at a time, so that a busy server isn't
stalled by many at once; the reply says how many are ahead of yours, for example "Making the land at ... ready; you are
teleported when it is (1 other teleport waiting)." Only the teleporting player waits.

If the land heights for that 1° square aren't on the server yet, Earth downloads them first and says so, then teleports
you when they are here. It waits at most **2 minutes**: if they haven't arrived by then, you are **not** teleported (you
would land where the ground isn't made yet), the download goes on, and you can try again in a while. A newer
`/earth tp` replaces one that is still waiting.

## Time

### `/earth date [day] [month] [year]` (time privilege)

Without anything, shows today's real date in this world, for example "It is 15–18 July (game day 5 of 9), year 0,
08:00". Each game month is the real month, its game days spread over the month's real days.

With a date, moves time **forward** to the game day that covers it, keeping the time of day. Time never goes back: a
date already past this year means next year's. The month can be `jan` to `dec`, a full name, or `1` to `12`.

- `/earth date 15 jul`
- `/earth date 1 december`

In creative mode, the creative settings panel also has Month and Day choices that do the same.

## Weather (everyone)

### `/earthweather` or `/ew`

Shows the weather where you are: this month's normal rain and how often it rains, the weather now (rain or snow by the
local temperature, clouds, air pressure, wind), thunder, and more. Only with Earth's weather (Weather · Source: Earth's
real climate).

| Command | What it shows |
| --- | --- |
| `/ew` | The weather here now and this month's normals. |
| `/ew forecast` | The next 24 hours, every 3 hours. |
| `/ew storms` | How many storms there are on Earth now, and the eight nearest with distance, direction and wind. |
| `/ew thunder` | Thunder here now, the strongest thunderstorm within 3000 blocks, and the next thunder here within 4 days. |
| `/ew fog` | The next fog here within 30 days. |
| `/ew hail` | Hail here over the next year, and the nearest hail within 3000 blocks. |
| `/ew map` | A small map of rain and clouds around you, north up: `#` rain, `+` overcast, `-` broken cloud, `.` fair, `@` you. |

See [Weather](Weather).

## Land heights (GLO-90)

### `/earth elevation`

Shows where this world's land heights come from, how many of the GLO-90 squares are on the server, and any download
that is running, with what's left and how long it may take.

Admins can also:
- `/earth elevation download all`: download every missing square from Earth's data release (about 8.6 GB, already
  converted). Squares already here are skipped.
- `/earth elevation download all copernicus`: the same from Copernicus's originals (about 71 GB, converted on the
  server; this takes hours).
- `/earth elevation cancel`: stop the download. Squares being fetched finish.

A download is refused when the world doesn't use GLO-90, there isn't enough disk space, no download source is set, or
one is already running.

### `.earthelevation` (client)

Opens the **Earth: land heights** window: the same status, refreshed every two seconds. Admins get the **Download
all...** and **Stop download** buttons and can choose the source; other players can only watch. The window opens by
itself for admins when the Earth tab's download all starts in a new world, or can't.

See [Data and performance](Data-and-performance).

## For admins

| Command | What it does |
| --- | --- |
| `/earth story` | Lists the story locations with their real latitude and longitude. Admins only, since it gives them away. |
| `/earth map [blocks per pixel]` | Draws the whole Earth as this world generates it into `VintagestoryData/EarthMap/earth-<seed>.png`, in the background, and tells you when it's done (default 128 blocks per pixel, 16 to 4096). Red is the spawn, yellow the story locations. |
| `/earth checkrivers` | Walks every river (about a minute) and writes any broken water it finds (blocked, running uphill, sudden falls) to `VintagestoryData/EarthMap/rivercheck-<seed>.txt`. |

Examples: `/earth story`, `/earth map 64`.

### Test tools

These are for testing Earth and comparing settings. Most players and owners won't need them, and some change the
world.

| Command | What it does |
| --- | --- |
| `/earth propick [radius] [old]` | For ores that lie inside another ore (silver in galena, gold in quartz, sylvite in halite, peridot in olivine): each one's ore map value and its parent ore in your column, or around it. Works after a prospecting pick has been used once since the server started. |
| `/earth orelayers [radius]` | Counts ore blocks by ground layer in the loaded chunks around you (worlds with ground columns, which are experimental and not on the world creation screen). |
| `/earth woods [radius \| trace]` | Counts trees by wood around you, with their mean height; `trace` counts the trees generation picks from then on (run it again to report). |
| `/earth census [radius]` | Counts plants, crops, berry bushes, fruit trees and animals in the loaded chunks around you. |
| `/earth siterules [rules] [anyway]` | Shows or changes the plant site rules for chunks generated from now on, until the server stops. Outside creative a change needs `anyway`, because chunks generated meanwhile keep it. |
| `/earth cost [on \| off]` | Measures what Earth's climate costs the game while it runs. |
| `/earth snowlight [on \| off]` | Turns relighting where snow melts on (the default) or off, for comparison. |
| `/earth relight [radius]` | Relights the loaded chunks around you, for rock left dark where snow melted. |
| `/earth boattest` | **Changes the world:** builds four 100-block test canals beside you (ramps, steps, a cascade, rapids) to try boats on. |
