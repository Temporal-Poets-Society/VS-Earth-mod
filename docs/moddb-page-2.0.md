# Earth 2.0 Mod DB page: draft notes

Draft for the author (EA-0043, 13 November 2026). Nothing here is published; the upload, the page edits and the wording
are the author's. It describes Earth 2.0.0. **The texts for the 2.0.2 upload are final in [moddb-description.html](moddb-description.html) and [moddb-2.0/](moddb-2.0/) (changelog since 1.0.2, the upload form's facts, the notices for the two old data pages; 15 November 2026); this file stays as the notes they came from.** The 1.0 page draft is
[moddb-page.md](moddb-page.md); its description, logo and screenshot notes still apply where this file does not replace
them.

## What changes on the Earth page

| Field | 1.0 | 2.0 |
| --- | --- | --- |
| Files | Earth, Earth Terrain Data, Earth Water Data (three downloads) | one download, `earth-2.0.0.zip` (under 40 MB) |
| Required dependencies | Earth Terrain Data, Earth Water Data, Cylinder World, Hydrology | [Cylinder World](https://mods.vintagestory.at/cylinderworld) and [Hydrology](https://mods.vintagestory.at/hydrology), at the minimums in `modinfo.json` when it is built (Cylinder World 1.1.0, EA-0096; Hydrology 0.6.2) |
| Game version | 1.22 | 1.22 (unchanged) |
| Side | client and server | client and server (unchanged); clients download no data |

Remove Earth Terrain Data and Earth Water Data from Earth 2.0's dependencies. Leave them on the 1.0.x files.

## Text for the release (changelog box and the top of the description)

> **Earth 2.0 is one mod.** The Earth Terrain Data and Earth Water Data mods are no longer needed. The first time a
> server (or a single-player game) starts Earth 2.0, it downloads Earth's data once per computer from Earth's data
> releases on GitHub (Temporal-Poets-Society: `earth-data`, `earth-ground`, `earth-glo90`) into
> `VintagestoryData/ModData/earth`, and checks every file: about 290 MB of map data, about 170 MB of ground data for
> worlds with ground columns (a Rocks and ground choice), and the GLO-90 land heights (8.6 GB, by default all of them when a new world
> first opens, in the background with a progress window; or square by square as the world reaches them). A game client
> joining a server downloads nothing. Without internet, the files can be put into that folder by hand; a server that
> cannot get them stops and says which are missing.
>
> **For new worlds.** Earth 2.0 makes new worlds. Worlds from older Earth versions (0.x, 1.x and the 2.0.0 release
> candidates) are not supported: start a new world. From 2.0.0 on, saves are kept working.
>
> **Story locations on real land.** The game's story locations (the Resonance Archive and the other story places) keep
> vanilla's distances from your spawn, but Earth moves each one onto suitable real land nearby:
> flat, dry, ice-free lowland for the surface ones, dry land a little above the sea for the Resonance Archive. They are
> not in the sea, on a glacier or on a mountain side. `/earth story` lists where they went, with their real
> coordinates.
>
> **New in 2.0:** one play style per scale (Earth · 1:200 to 1:10, 1:100 the default; 1:5 on the Earth tab), each with
> the world size, height and mountains that suit it; Copernicus GLO-90 land heights at every scale; rivers on their
> real channels, with a current, still backwaters and seasonal rivers; coasts, lakes and rivers as they were around
> 1318 (reservoirs and canals dug since then undone, the Zuiderzee back); beaches of the coast's own material; soil by
> real aridity, and rocks and ground from real geology and soil maps to choose on the Earth tab (the game's rocks by
> default); plants and animals where they lived around 1318; hot springs at
> known sites and lava under Holocene volcanoes; Earth's own weather, shared with other mods through the open sea
> contracts. Earth's ore maps (real deposits at their real size) are an experimental choice on the Earth tab; the
> default ores are vanilla's.

Check the "New in 2.0" list against the 2.0.0 entry in [CHANGELOG.md](../CHANGELOG.md) at release.

## The old data-pack pages

Earth Terrain Data (`earthterraindata`) and Earth Water Data (`earthwaterdata`) stay on the Mod DB for Earth 1.0.x
worlds. Suggested notice at the top of each page, and in the summary line:

> **For Earth 1.0 only.** Earth 2.0 no longer uses this mod: it downloads its data itself the first time a server
> starts. Keep this mod only for a world made with Earth 1.0.x, together with Earth 1.0.x. If it is still installed
> with Earth 2.0, Earth copies the files it needs from it instead of downloading them, so it does no harm.

Summary line: "Data for Earth 1.0.x (not needed by Earth 2.0)". Do not mark their files as compatible with Earth 2.0.

## Before the upload (the author's decisions)

- The Mod DB's rules on mods that download data at run time (decision 0066): read them, and say on the page what is
  downloaded, from where and how much (the text above does).
- Licences and credits on the page: Copernicus GLO-90 (its required notice, also on the Earth tab), OpenStreetMap
  (ODbL), GIRES (CC BY-SA), TDWG and PHYLACINE/IUCN ranges; the full list is `earthdata/ATTRIBUTION.txt` and the data
  repos' READMEs.
- Screenshots from a 2.0 world (the 1.0 shot list in [moddb-page.md](moddb-page.md) still fits).
