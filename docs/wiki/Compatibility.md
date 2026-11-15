# Compatibility

What Earth needs, what it works with, what doesn't mix, and which saves keep working.

## Required

Earth needs these on the server **and** on every client. Without them the game doesn't load Earth (on a dedicated
server, check the log: the server still starts, without Earth). If Cylinder World is installed but switched off in its
config, a new world comes out vanilla with an error in the log, and a saved Earth world isn't opened.

| Mod | Version | Why |
| --- | --- | --- |
| Vintage Story | 1.22.0 or newer | |
| **Cylinder World** | 1.1.0 or newer | The world wraps east–west on its ring; the seam, the ice walls at the poles and the latitude of every block come from it. |
| **Hydrology** | 0.6.2 or newer | Makes Earth's rivers behave: calm water, rapids at real drops, water that stays where it is instead of flooding the map. Earth checks Hydrology's river rules at start and refuses an older one. |

## The other mods of the suite (optional)

| Mod | What it adds with Earth |
| --- | --- |
| **Sea** | Waves on the sea that follow Earth's wind (with Weather · Source: Earth's real climate); ships and vanilla boats ride them. Earth gives Sea its wind. Recommended. |
| **World Map** | An atlas and minimap of the real Earth, drawn from Earth's terrain (in survival, the land you have seen), with real rivers, latitude and longitude, local time and the barometer. |
| **Astra Terra for Earth** | A sky that follows the real longitudes: the sun rises in the east of the world first, local time differs from place to place, and the weather's afternoon showers follow the local sun. A new world then starts in the morning at its spawn (see [Settings](Settings)). Its `LongitudeAwareSun` setting must be on. |

Without Astra Terra the sky keeps vanilla's single clock: the sun is at the same height everywhere at the same moment.

## Other mods Earth supports

| Mod | What happens with Earth |
| --- | --- |
| **Hydrate or Diedrate** | With Earth's own rocks and ground (Rocks and ground: Ground columns or Regional, and Biomes: Earth), Earth gives it the aquifers of its real water table. With vanilla rocks (the default) or vanilla biomes, it makes its own as without Earth. Only land made while it is installed gets Earth's aquifers, and only with a Hydrate or Diedrate that uses aquifer data version 2 (2.5.8 does); otherwise it makes its own, with a warning in the log. See [Water](Water). |
| **Floral Zones** packs | Their ground plants grow only in their native ranges, and their trees too, with Plants and animals: As around 1318. See [Plants and animals](Plants-and-animals). |
| **Wildcraft Trees** (with Herbarium) | Its trees grow only in their native ranges, with Plants and animals: As around 1318. |
| **Fauna of the Stone Age** | With Plants and animals: As around 1318, its animals spawn only within their ranges and habitats. With Include extinct animals, the ones that died out before 1318 too. |
| **Geology Additions** | With Rocks and ground: Ground columns or Regional (not the default), its rocks and materials are used where the real ground has them. Earth's bog iron needs it. |

Vanilla's own plants and animals work without any of these. Plant and animal mods that Earth doesn't know keep their
own spawning rules.

## Mods that don't mix

- **Algernon's Watersheds** replaces the same terrain generation as Earth (and vanilla's maps and block layers). Earth
  writes an error to the log when both are installed: use one or the other. Earth makes its own real rivers.
- **Biomes:** its own geographic rules can stop animals that Earth's ranges allow. Earth warns in the log; leave it out
  of an Earth world.
- **Other world generators** that replace the standard world's terrain (continents, heights, rock layers) fight Earth
  for the same land. Earth's terrain only works in its own play styles.

Mods for other things (crafting, food, tools, buildings, decoration) work as they do anywhere. This list is what has
been checked; a mod not named here may still work.

## Saves

- **Worlds made with Earth 2.0.0 or newer** keep working with later Earth versions. A world remembers every choice it
  was made with, so updating Earth doesn't change its terrain and rocks in land still to be generated; fixes to tree,
  plant, animal and ore rules do reach new land.
- **Worlds from older Earth versions** (0.x, 1.x and the 2.0.0 test builds) are **not supported**: start a new world.
- If a part of a world's generation is missing at a start (a game update changed something Earth relies on, a mod the
  world's blocks came from was removed, a data file is missing), the server stops before it makes new land and names
  what is missing. Fix the cause, or set `AcceptMissingFeatures` to `true` in `earth.json` to go on with vanilla's
  blocks in those places, which may not match the land around them. See [Troubleshooting](Troubleshooting).
- Adding or removing mods in an existing world: see [Saves](Saves).
