# Climate and seasons

In an Earth world the climate is the real Earth's. Every place has its own temperature and rainfall for every month of
the year. They come from climate records, not from the game's own climate noise. This page covers what that changes for
the player. The day-to-day weather on top of it (rain, storms, wind) is on the [Weather](Weather) page.

## Where the climate comes from

**Vanilla:** the game paints its climate map with noise. Temperature falls toward the poles of the world and with
height; rainfall is noise too. Deserts, forests and tundra land wherever the noise puts them.

**Earth:** the climate map holds each place's real yearly mean temperature and a rainfall value worked out from the real
dryness of that place (rainfall measured against how much water the air can take up). The rainfall scale is set so that
the game's trees and plants land where they really grow: temperate Europe gets oak, birch, maple and Scots pine; the wet
tropics get kapok, tree ferns and bamboo; savannas get acacia; deserts stay bare.

What it means for you:
- The Sahara is a desert, the Amazon is rainforest, Siberia is taiga and tundra, and the Mediterranean is dry in summer.
  You can plan a journey with a real atlas.
- Places with dry summers (southern Italy, Greece, Spain, California, central Chile) count as drier than their yearly
  rain alone would make them, so their vegetation is sparser.
- Soil follows the dryness too. Land that isn't truly arid gets at least poor soil instead of sand or gravel. The
  Mediterranean, the steppes and the dry savannas have soil, while the real deserts keep their sand.
- Forests grow where the real climate allows trees. That means the warmest month is above about 10 °C (below the polar
  and alpine tree line) and the place is moist enough. Steppe, desert and tundra stay open.

## Latitude

**Vanilla:** a world has a north-south temperature gradient. How far it is from the equator to a pole is a world
setting (Polar-Equator distance).

**Earth:** an Earth world always runs on Cylinder World's planet layout. Cylinder World gives every block row a real
latitude, from the north ice wall to the south one, and Earth places the real Earth on it. The equator, the tropics and
the polar circles are where they really are, so latitude does all the things it does in the game:
- **Sun and day length:** the sun's height and the length of the day follow the latitude of the place you stand. Summer
  days are long in the north and short in the south, polar summers have midnight sun, and polar winters have polar
  night.
- **Two hemispheres:** the seasons are reversed in the southern hemisphere. July is winter in Patagonia, and January is
  summer in Australia.
- The game's clock is one clock for the whole world. Noon comes at the same moment everywhere, unless you use a sky mod
  that follows longitude (see "Day and night by longitude" in Earth's README).

## Altitude

**Vanilla:** it gets colder by 1 °C for every 6.375 blocks above sea level. The temperature is kept between −20 °C and
40 °C.

**Earth:** high ground is as cold as it really is. Earth stores each place's climate so that the game's own height rule
gives back the real temperature at the ground. Where that rule can't reach (very tall worlds, the coldest ice sheets),
Earth adds the difference itself. Addis Ababa, Mexico City and Quito are mild highland cities, not frozen ones, and the
Himalaya and the Andes are cold and snowy above their tree lines. Summits where even the warmest month is near freezing
lose their soil.

## How hot and how cold

**Vanilla:** live temperatures stay between −20 °C and 40 °C.

**Earth:** where the real climate goes further, so does the game (config `ExtendTemperatureRange`, on by default). The
Antarctic plateau reaches about −60 °C in its winter, and the hottest deserts go past 40 °C in summer. Dress
and shelter for it: body temperature, crops and food spoilage all follow these live temperatures.

## Seasons

**Vanilla:** every place swings between winter and summer by the same rule, about 65 °C times its latitude share. Two
places at the same latitude have the same seasons.

**Earth:** each place has its own real seasons (config `RealSeasons`, on by default). So continental, maritime and Gulf
Stream climates are right:

| Place | January | July |
| --- | --- | --- |
| London | about 4 °C | about 20 °C |
| Southern Italy | about 9 °C | about 29 °C |
| Finland | about −10 °C | about 17 °C |
| Yakutsk | about −38 °C | about 21 °C |

(These are game readings in a world made with Earth 2.0. The figures are the month's mean, before the day's weather and
the time of day. A world made with 2.1.0 is about a degree colder in Europe: see "The climate of 1318" below.)

**Each month its own temperature** (worlds made with Earth 2.1.0): every place has its real mean temperature for each
of the twelve months, and the temperature runs smoothly from one month to the next. Places whose year doesn't have the
usual shape get it right: Mumbai is hottest in May, before the monsoon rains cool it, Delhi in June, Tokyo, Lisbon and
Miami in August, San Francisco in September, and in the tropics the months differ by only a degree or two.

What it means for you:
- A coastal start such as Western Europe has mild winters, so growing seasons are long and you rarely need to fear
  frost. An inland start at the same latitude, such as central Russia, has hard winters.
- Monsoon and wet-season places get their rain in their real months (see [Weather](Weather)).

## Day and night

**Vanilla:** every day swings by the same rule, about 5 to 18 °C depending on rainfall, coldest at 04:00 and warmest at
16:00, whatever the sun does.

**Earth** (worlds made with 2.1.0): each place has its real day-night range for the month. A desert swings by 15 °C and
more, a cloudy coast or a northern winter by only a few degrees. The day follows the sun: it is coldest just after
sunrise and warmest about two hours after noon, so a long summer day warms longer, a short winter day hardly warms at
all, and in the polar day and night the temperature only swings gently.

## Warm and cold spells

**Vanilla:** two random swings move the temperature of the whole world at once, up to about 3 °C over weeks and 1 °C
from day to day.

**Earth:** warm and cold spells come with the weather and move with it, so one region can have a cold snap while another
is mild. In worlds made with 2.1.0 each place's spells are as large as its real day-to-day swings at that time of year:
small in the tropics, several degrees in a continental winter. The world-wide swings are gone.

## The climate of 1318

An Earth world is the world as it was around 1318. Worlds made with Earth 2.1.0 have the climate of that time too:
the early 1300s, before the modern warming and early in the Little Ice Age. Compared with today's climate:

| Place | The year | Its coldest month |
| --- | --- | --- |
| The globe | about 0.5 °C colder | |
| Western Europe (Paris) | about 1 °C colder | about 1.5 °C colder |
| The Alps | about 1 °C colder | |
| Moscow | about 1.4 °C colder | about 2 °C colder |
| The Arctic | about 2 °C colder | |
| Greenland's Norse settlements | about the same | |

Everything the temperature decides when the world is made follows it: snow, crops, vegetation, the tree lines and
forests. The figures come from a reconstruction of past climate from tree rings, ice cores and corals, with climate
models for how it differs month by month.

## Worlds made with Earth 2.0

A world made with Earth 2.0 keeps its temperatures: each place's coldest and warmest month, the game's day-night rule
and the world-wide swings. To give it the months, days and spells above, set `UpgradeExistingWorldSeasons` to `true` in
`earth.json` once and start that world. Nothing generated changes: its land and its climate map stay as they are (today's
climate), and from then on the temperature you feel follows each place's months, days and spells. The shape of its year
is that of 1318, so its winters are a little colder against its yearly mean (about a degree in Moscow, more in the
Arctic).

## The monthly temperature file

The months, days and spells come from one data file (about 58 MB). A server, or a single-player game, downloads it once
at the first start of a world that uses it; a player's game downloads nothing, because the server sends each player
the land around them. Without the file (offline), the world plays with Earth 2.0's temperatures that session and
fetches the file at a later start.

### The calendar

An Earth world keeps the game's calendar. A year has 12 months and each month has the world's days per month (9 game
days by default), so a real season passes in a few game days. Earth reads the game's month as the real month: game
January is real January.

To try a season, an admin with the `time` privilege can show the date with `/earth date` and jump forward to a real
date: `/earth date 15 jul`, or with the month as a number, `/earth date 15 7` (day first; a year may follow). Time only
runs forward; a date that has already passed this year is next year's.

## Seeing the climate where you stand

- `/earth` shows the latitude, longitude, real elevation and climate at your position.
- `/earthweather` (or `/ew`) shows this month's climate here: how much rain normally falls in a day, how much of the
  time it rains, and on how many days it thunders. It also shows the weather now. See [Weather](Weather). In a world
  made with 2.1.0 it also shows how the temperature here is made up: the month's curve, the day-night range and the
  hour's place in it, sunrise and sunset, and the spell now.

## Settings

The climate itself has no choice on the world-creation screen: an Earth world always has the real climate. These
settings in `VintagestoryData/ModConfig/earth.json` change it:

| Setting | Default | What it does |
| --- | --- | --- |
| `RealSeasons` | `true` | Each place's own seasons; `false` gives vanilla's seasons and day-night swing by latitude, in every world. Read at every start, so it can be changed on an existing world. |
| `UpgradeExistingWorldSeasons` | `false` | Once: the next world made with Earth 2.0 that starts takes the monthly temperatures (see above); then it turns itself off. |
| `ExtendTemperatureRange` | `true` | Live temperatures below −20 °C and above 40 °C where the real climate has them. Saved with the world when it is made. |

## Without Earth's terrain

Worlds made with another play style (not an Earth play style) keep vanilla's climate even with Earth installed.
