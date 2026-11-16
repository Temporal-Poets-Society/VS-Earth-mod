# Earth

Earth turns a new Vintage Story world into the real Earth: its land and sea floor, coasts, islands, rivers and lakes,
the Greenland and Antarctic ice sheets, the real climate and weather of every place, and, if you choose, real rocks
and ground. Trees, plants and animals live where they lived around the year 1318. You can sail from Riga to Lisbon,
climb the Alps, cross the Sahara or winter in Siberia, and find each place where it is on a real map.

The world wraps east to west: walk far enough west and you come back from the east. The seam runs through the Bering
Strait, and the north and south poles are walls of ice. That ring comes from Cylinder World, which Earth needs.

These pages are about **how the game plays differently from vanilla**. Each topic says what vanilla does, what Earth
does instead, and what that means for you.

## What Earth needs

- **Vintage Story 1.22** (1.22.0 or newer).
- **Cylinder World 1.1.0 or newer.** The ring, the seam, the ice walls and
  the latitude of every block come from it.
- **Hydrology 0.6.2 or newer.** It makes Earth's rivers behave: calm water,
  rapids where rivers fall steeply, water that stays put instead of flooding the map.
- All three go on the server **and** on every client.

Recommended or supported, not required: **Sea** (waves and ships follow Earth's wind), **World Map** (a map of the real
Earth as you explore it), the **Floral Zones** packs, **Wildcraft Trees** with Herbarium, **Fauna of the Stone Age**
and **Geology Additions**. See [Compatibility](Compatibility).

## Install

1. Put the Earth zip, Cylinder World and Hydrology in the `Mods` folder of the server and of every player.
2. Start the game or the server. The first time, Earth downloads its data from GitHub: about 290 MB, 58 MB more of monthly temperatures for a new world, then all the real land
   heights (8.6 GB by default; the download starts when your first world opens, and you can play meanwhile). Only the server or the single-player game downloads; a
   client joining a server downloads nothing. Details, disk space and memory are on
   [Data and performance](Data-and-performance).

## Quick start

1. **Create a world** and choose one of the **Earth** play styles. There is one for each scale: **Earth · 1:200**,
   **Earth · 1:100** (a good first world), **Earth · 1:50**, **Earth · 1:25** and **Earth · 1:10**. A smaller number is a
   bigger world: at 1:100 a block is about 100 m, at 1:10 about 10 m. See [The world](The-world).
2. Open **Customize** to see the **Earth** tab (rocks, ores, plants and animals, rivers). Where you begin (a random
   place, a capital city, or coordinates pasted from Google Maps) is on the **Player spawn and death** tab, below
   Starting climate. The defaults are a good
   start. Every choice is explained on [Settings](Settings).
3. Play. Type `/earth` to see where you are: latitude, longitude, the real height and the climate. More on
   [Commands](Commands).

Other play styles keep vanilla's terrain; Cylinder World still wraps those worlds and Hydrology still changes their
water.

## The pages

- [The world](The-world): the scales, the world's size and height, sea level, the seam and the ice walls.
- [Terrain](Terrain): mountains, coasts and islands, rivers and lakes as of 1318, ice sheets, volcanoes and hot springs.
- [Water](Water): calm rivers and rapids, springs, ponds, the water table, and Hydrate or Diedrate.
- [Climate and seasons](Climate-and-seasons): the real climate of every place and month.
- [Weather](Weather): Earth's rain, storms, thunder, hail and wind.
- [Rocks, soil and ores](Rocks-soil-and-ores): vanilla's rocks or Earth's, soil, ores and gems, prospecting.
- [Plants and animals](Plants-and-animals): what grows and lives where, around 1318.
- [Progression](Progression): what's easier or harder than in vanilla, and where to find what you need.
- [Spawn and story locations](Spawn-and-story-locations): where you start and where the story sites end up.
- [Settings](Settings), [Commands](Commands), [Compatibility](Compatibility).
- [Data and performance](Data-and-performance), [Saves](Saves), [Troubleshooting](Troubleshooting).

## Version and problems

This wiki describes **Earth 2.1.1**, with Cylinder World 1.1.1, Hydrology 0.6.2, Sea 0.6.1 and World Map 0.5.4. Earth 2.0
makes new worlds only: worlds made with an Earth older than 2.0.0 aren't supported (see [Saves](Saves)).

Report problems on Earth's Mod DB page. Say which Earth version you run, the world's scale and settings, where in the
world it happened (`/earth` gives the latitude and longitude), and attach `server-main.log`.

Earth uses free public data: Copernicus GLO-90 heights, ETOPO, GSHHG coastlines, HydroRIVERS, Natural Earth,
OpenStreetMap and many more. The full list with every licence is in the mod's `earthdata/ATTRIBUTION.txt`.
