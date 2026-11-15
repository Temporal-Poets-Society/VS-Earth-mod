# Saves

What happens to your world across Earth updates, and what to keep in mind when you add or remove mods.

## Which worlds Earth 2.0 keeps working

**Worlds made with Earth 2.0.0 or later are kept working.** Update Earth and your world goes on as before.

**Worlds made with older Earth versions aren't supported**: 0.x, 1.x (then called Earth Map) and the 2.0.0 release
candidates. Earth 2.0 doesn't refuse to open them, but nothing is promised about what they look like or whether they
work. Earth 2.0 is one mod and makes new worlds. Start a new world to play 2.0.

## How Earth keeps a world the same

**Vanilla:** a chunk is made once and kept. If the game's world generation changes in an update, new chunks are made
with the new rules and can meet old ones with a visible seam.

**Earth:** every world remembers the version of each of Earth's rules it was made with (coasts, rivers, lakes, rocks,
ground, soil, plants, volcanoes and the rest). After an update, your world keeps making new land with the terrain and
rock rules it started with, so new chunks always fit the old ones. Improvements to terrain reach **new worlds** only;
fixes to tree, plant, animal and ore rules do reach new land in existing worlds.

- **Settings are fixed when the world is created.** Earth's choices on the Earth tab (scale, rocks, ores, plants and
  animals, rivers, springs, ponds, weather) are saved with the world; the screen doesn't let you change them later.
  (Hydrology's Water flooding on that tab can be changed. A few one-shot switches in `earth.json` let an owner change a
  saved world's canals, rocks, ores or ranges for new land; see [Settings](Settings).)
- **A world can't always go back to an older Earth.** A world that a newer Earth saved with newer rules or settings is
  refused by an older Earth, with a message in the server log that names the version that last opened it. The world itself isn't changed.
  Keep the newer Earth, or restore a backup made before the update.

## When something is missing

Earth won't make land that is missing part of what the world was made with. If a part of its world generation can't
start (a mod the world's blocks came from was removed, a game update changed something Earth relies on, a data file is
missing), the server stops before it makes any new land and names what is missing. Fix the cause and the world opens as
it was. What to do, and the `AcceptMissingFeatures` setting for owners who would rather go on without that part, are on
[Troubleshooting](Troubleshooting).

## Adding and removing mods

- **Cylinder World and Hydrology** must stay installed. A saved Earth world whose Cylinder World is missing or switched
  off is refused: the server stops and the world is unchanged.
- **Hydrate or Diedrate added later:** only land generated from then on gets Earth's aquifers. See [Water](Water).
- **Plant and animal mods added later** (Floral Zones, Wildcraft Trees, Fauna of the Stone Age): Earth's ranges apply to
  them in land generated from then on; existing forests stay as they are.
- **Removing a mod whose blocks are in the world**, such as a rock mod that Earth's rocks used, can stop the world from
  generating new land, as above.

## Backups

Back up the world before you update Earth, Cylinder World or Hydrology. A Vintage Story world is the `.vcdbs` file in
your `Saves` folder. Earth's downloaded data (land heights and other files) is kept once per machine, outside the world,
and doesn't need to be backed up; see [Data and performance](Data-and-performance).
