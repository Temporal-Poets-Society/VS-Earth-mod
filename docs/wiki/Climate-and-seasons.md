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

**Earth:** each place swings between its own real coldest and warmest month (config `RealSeasons`, on by default). So
continental, maritime and Gulf Stream climates are right:

| Place | January | July |
| --- | --- | --- |
| London | about 4 °C | about 20 °C |
| Southern Italy | about 9 °C | about 29 °C |
| Finland | about −10 °C | about 17 °C |
| Yakutsk | about −38 °C | about 21 °C |

(These are game readings. The figures are the month's mean, before the day's weather and the time of day.)

What it means for you:
- A coastal start such as Western Europe has mild winters, so growing seasons are long and you rarely need to fear
  frost. An inland start at the same latitude, such as central Russia, has hard winters.
- Monsoon and wet-season places get their rain in their real months (see [Weather](Weather)).

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
  time it rains, and on how many days it thunders. It also shows the weather now. See [Weather](Weather).

## Settings

The climate itself has no choice on the world-creation screen: an Earth world always has the real climate. These
settings in `VintagestoryData/ModConfig/earth.json` change it:

| Setting | Default | What it does |
| --- | --- | --- |
| `RealSeasons` | `true` | Each place's own seasons; `false` gives vanilla's swing by latitude. Read at every start, so it can be changed on an existing world. |
| `ExtendTemperatureRange` | `true` | Live temperatures below −20 °C and above 40 °C where the real climate has them. Saved with the world when it is made. |

## Without Earth's terrain

Worlds made with another play style (not an Earth play style) keep vanilla's climate even with Earth installed.
