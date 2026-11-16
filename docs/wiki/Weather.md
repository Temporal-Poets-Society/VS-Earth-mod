# Weather

Earth makes the weather of Earth worlds from the real climate: rain, clouds, storms, thunder, hail, fog and wind. The
game still draws and plays it with its own rain, snow, clouds, lightning and sounds, but what falls where and when is
Earth's. This page describes the weather of Earth 2.0 (weather rules 9, which every new world gets). For the climate
underneath it (temperatures, seasons, latitude) see [Climate and seasons](Climate-and-seasons).

## How it works, in short

**Vanilla:** the world is split into weather regions about 512 blocks across. Each region picks random weather
patterns (clear, cloudy, overcast, rain), random wind and random weather events (thunderstorms, hail) by chances that
barely depend on where you are. A desert can get a thunderstorm and a rainforest can stay dry for weeks.

**Earth:** the weather follows the real climate of the place and the month. Rain areas, fronts, showers and storms form
where and when they do on the real Earth. They move across the map with the winds and pass over you; they don't switch
on and off in place.

- The server and every player work out the same weather from the world's seed and the game's time. Nothing extra is
  saved or sent, a player who joins late sees the same sky, and the weather doesn't change when you reload.
- The weather runs at real speeds over the map's real distances. At 1:100 (the default play style), a block is about
  100 m of the real Earth, so a rainy spell lasts hours and a front takes about half a day to pass. Showers and gusts
  are sized for the player.
- The game's year is short (108 days by default), so a season brings fewer fronts than a real one. The config setting
  `WeatherPace` trades that off (see [Settings](#settings) below).

## Rain and snow

**Vanilla:** rain comes from the region's random pattern; how wet a place is barely matters.

**Earth:** it rains as often as it really does at that place in that month, from the real monthly climate records.
- Mumbai rains about 43 % of the time in July and almost never in January. Rome's summer is dry. The Sahara hardly
  ever sees rain, and London has rain in every season.
- Rain comes as wide rain areas and fronts hundreds of kilometres across, and as showers a few hundred blocks wide.
  Rain clouds gather before the rain starts. Over land, showers are most common in the afternoon.
- Rain comes in showers of a few hours spread through the day, the way weather stations report it. In warm months the
  rain falls as a few downpours and a lot of light rain, so rainy days come as often as they really do.
- High ground gets rain and snow on as many days as its weather stations report.
- The cold desert coasts (the Atacama, the Namib) get no rain. The desert Southwest of the United States has its summer
  thunderstorms.
- Whether it falls as rain or snow is the game's own rule: snow where it is cold enough.

## Clouds

**Vanilla:** each region's cloud pattern is random.

**Earth:** the clouds are as thick as Earth's cloud cover at that place, so their edges follow the rain areas. Desert
skies are clear, and fronts and wet seasons are overcast. Low cloud decks lie over the cold seas off Peru and Namibia.

## Wind

**Vanilla:** the wind is random noise per region, with random gusts.

**Earth:** the wind starts from each month's real prevailing winds: the trade winds, the westerlies and the "roaring
forties", the monsoons (from the south-west over India and West Africa in summer, cold northerlies over East Asia in
winter), and the winds off the ice sheets. Day-to-day changes and gusts come on top, and storms bring their own winds.
- Trees, the slant of falling rain, cloud drift and the wind's sound follow it.
- With **Sea** installed, the waves and boats follow the same wind (see Sea's wiki).

## Storms

**Vanilla:** no storm systems; strong wind and rain come as random events.

**Earth:** about 85 tropical cyclones and 1,000 mid-latitude lows form per 365 days of weather, as on the real Earth,
where and when they really do (a default game year of 108 days sees about a third of them).
Atlantic hurricanes come in summer and autumn, there are none in the South Atlantic, and lows cross the westerly belts.
They move along real storm tracks. Their winds turn around the centre and are strongest near the eye, with spiral rain
bands and low pressure. `/earthweather storms` lists the nearest ones.

## Thunder and lightning

**Vanilla:** thunderstorms are random weather events in a region, desert or not.

**Earth:** lightning strikes where Earth's weather has a thunderstorm, as often as the storm is strong, and nowhere
else. Thunderstorms follow Earth's storms and the warm season's showers. Each place thunders on about as many days as
its weather stations report (West and Central Africa, for example, have many thunder days).
- The bolts are the game's own: the flash, the damage, and lightning rods work as usual.
- You hear distant thunder from storms up to about 1,200 blocks away, even under a clear sky.
- **Lightning fires:** with the world option "Fire from lightning" on, lightning can set fire to what burns, but only
  where it hasn't rained in the last ten days. Earth's rain decides that. `/earthweather` tells you whether lightning
  could start a fire where you stand.

## Hail

**Vanilla:** hail comes as a random weather event.

**Earth:** hail falls only from the strongest thunderstorms. It is drawn and sounds as hail, and it hurts players caught
outside (the game's own hail damage). `/earthweather hail` searches the coming game year for when hail next falls where you stand. The search runs
in the background and the answer arrives in chat when it is done.

## Fog

**Vanilla:** fog comes with the region's random weather pattern.

**Earth:** fog comes from the weather. Sea fog lies on the cold coasts (Lima has it all year round). Valley fog forms
on clear, calm nights after rain. `/earthweather fog` lists when fog comes here in the next 30 days.

## The weather's own temperature

**Vanilla:** the temperature follows the season and the time of day; the weather doesn't change it.

**Earth:** the weather is part of the temperature. Under clouds and rain a warm month's afternoon is cooler, and a
winter's snowy night is milder, by about 3 °C at most places. Warm and cold spells come on top, small in the tropics
and up to about 6 °C in a continental winter; in worlds made with 2.1.0 each place's spells are as large as its real
day-to-day swings at that time of year (see [Climate and seasons](Climate-and-seasons)). `/earthweather` says by how
much the weather is warming or cooling it here right now. Your body temperature, crops and food follow the
temperature with the weather included.

## Commands

All players can use these (no special privilege):

| Command | What it shows |
| --- | --- |
| `/earthweather` (or `/ew`) | This month's climate here (normal daily rain, how much of the time it rains, thunder days), the weather now (rain, clouds, pressure, wind and the month's prevailing wind, thunder, hail, fog, how much warmer or colder the weather makes it), and whether lightning could start a fire here |
| `/ew forecast` | The next 24 hours here, every three hours: rain or snow, clouds, wind, pressure |
| `/ew storms` | How many storms are on the Earth now, and the nearest ones with distance, direction and wind |
| `/ew thunder` | Thunder here now, the strongest thunderstorm within 3,000 blocks, and when it next thunders here (up to four days ahead) |
| `/ew fog` | When fog comes here in the next 30 days |
| `/ew hail` | When hail next falls here in the coming year, and the nearest hail now within 3,000 blocks |
| `/ew map` | A text map of the rain and clouds around you |

Of the game's own `/weather` commands, `/weather setprecip <level>` sets the rain by hand until `/weather setprecipa`
hands it back to Earth's weather. `/weather stoprain` changes nothing in an Earth world (Earth's rain can't be moved
on), and `/whenwillitstopraining` reads Earth's rain.

## Settings

### On the world-creation screen (Earth tab)

| Option | Values | Default | Changeable later |
| --- | --- | --- | --- |
| Weather · Source | "Earth's real climate (default)" or "Vanilla weather" | Earth's real climate | No, it is chosen when the world is made |

### Hidden from the screen (admins)

Two settings are not on the world-creation screen. A server owner sets them in the dedicated server's world
configuration **before the world is made**. Write them as text in quotes: the game drops a bare number for these.

| Key | Values | Empty (the default) means |
| --- | --- | --- |
| `earthRainFrequency` | `"0"` to `"3"`: times the real chance of rain (`"2"` rains twice as often, `"0.5"` half as often, `"0"` never) | `RainFrequency` from `earth.json` (1) |
| `earthStormFrequency` | `"0"` to `"5"`: times the real number of storms (`"0"` makes no storms) | `StormFrequency` from `earth.json` (1) |

Example, in the server's world configuration: `"earthRainFrequency": "2"`.

They only act with Earth's weather, and they are saved with the world when it is made.

### Config file (`VintagestoryData/ModConfig/earth.json`)

| Setting | Default | What it does |
| --- | --- | --- |
| `Weather` | `true` | Earth's weather in worlds made without the Weather · Source option (`false` = vanilla weather). New worlds take Weather · Source from the creation screen. |
| `WeatherPace` | `1` | Weather hours per game hour, from 0.25 to 8. At 1 fronts and storms move at real speeds; higher gives more weather per game season and faster changes. Saved with the world when it is made. |
| `RainFrequency` | `1` | How often it rains, 0 to 3 times the real chance; used when the world has no `earthRainFrequency`. Saved with the world when it is made. |
| `StormFrequency` | `1` | How many storms form, 0 to 5 times the real number; used when the world has no `earthStormFrequency`. Saved with the world when it is made. |

## Good to know

- **Multiplayer:** every player must have the same Earth files. A player whose Earth data differs from the server's
  gets vanilla weather instead of different weather, and the game log says why.
- Weather isn't saved, so stopping and restarting a world doesn't change it.
- Worlds made with Weather · Source on "Vanilla weather" behave like vanilla: the game's random patterns, events and
  lightning.
- A front brings its rain and cloud, but not yet the sharp wind shift and temperature drop of a real cold or warm
  front; those are not in Earth 2.0.
