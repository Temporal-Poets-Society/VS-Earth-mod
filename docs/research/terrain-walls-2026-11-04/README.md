# Terrain walls probe, 4 November 2026

This is the offline probe behind [decision 0052](../../decisions/0052-river-valley-and-channel-walls.md). It builds the
terrain model the way a new world from the Earth tab's defaults has it: 512,000 blocks wide, height 512, sea level 110,
coast outlines, islands, straits, rivers with pool layout 10, dry fans and exit channels. It needs no game.

## Build

The probe compiles Earth's `src/Core` directly. Point `EarthSrc` at the source you want to measure:

- **Before (`release/1.0`):** `dotnet build -c Release -p:EarthSrc="<release-1.0>\src"`
- **After (this branch):** `dotnet build -c Release -p:EarthSrc="<terrain-walls>\src" -p:DefineConstants=WALLS`
- **With GLO-90 land heights (0051; `walls-glo90-test`, where both are merged):**
  `dotnet build -c Release -p:EarthSrc="<walls-glo90-test>\src" "-p:DefineConstants=WALLS%3BFINE"`

Build each in its own folder, because the two share `obj`.

## Run

Run `dotnet bin/Release/net8.0/WallsProbe.dll <mode> [name lat lon]...`. With no sites given, it probes Sion and
Zeeland.

**Environment variables:**

| Variable | Default | What it sets |
| --- | --- | --- |
| `EARTHROOT` | | The folder holding `earthdata` |
| `SEED` | 386217 | The noise seed |
| `CHANNELS` | the code's current version | Channel rules |
| `VALLEY` | the code's current version (2) | River valley rules (WALLS build only) |
| `RADIUS` | | How far around each site the probe looks |
| `ABLATE` | | Switches parts off: `channels`, `straits`, `rivers`, `coasts`, `islands` or `fans` |
| `OUT` | | Where renders go |
| `GLO90` | | FINE build only: a folder of converted GLO-90 tiles (`VintagestoryData/ModData/earth/glo90`). Dry land takes their heights, attached after the model is built as the server does. Nothing is downloaded, so a missing tile leaves that square on the built-in heights. |
| `FINERULES` | the code's current version (2) | GLO-90 rules (2: with the summit lift) |

**Modes:**

| Mode | What it reports |
| --- | --- |
| `walls` | Land steps of `MINSTEP` (4) or more beside rivers that the river shaping made: at least `MADE` (3) more than the natural ground's step there. `EXAMPLES` and `SHOWFROM` list cases, with the river that governs each side. Also the natural ground's own steps of `MINSTEP` or more at the same pairs. |
| `levels` | Dry columns in a river's valley: how many have natural ground below the nearest river's water (what the valley rules raise) and how far, and the spread of natural ground minus water. |
| `finediff` | FINE build with `GLO90`: columns whose water (sea, lake, river or none) differs between the built-in heights and GLO-90. |
| `lanes` | FINE build with `GLO90`: every exit channel's lane worldwide (the course, the river's half width plus the channel's depth each side). It counts columns that are sea on the built-in heights but not on GLO-90, those on the course's centre line, and the reverse. |
| `seabed` | Steps of `MINSTEP` (3) or more from a shore to the sea bed, and on the bed. |
| `spills` | River or lake water beside dry ground lower than the water (it would run out), and the time to make a column (one thread). |
| `channels` | Each exit channel's course near a site, with the grid, outline and column at every point. |
| `crossings` | All exit channels worldwide: points on the outlines' land, and rivers with no way out. |
| `hash` | A fingerprint of every column within `RADIUS`. Use it to compare old rules between builds. |
| `sites` | A render (`<name>.png`), a grid of tops (`GRID`, `STEP`) and transects (`TRANSECT`). |

## Renders

`zeeland-before-after.png` and `zeeland-site-zoom.png` show 1,401 and 321 blocks around the Westerschelde site, with
the old rules on the left and the new on the right. In the old version, a diagonal canal crosses Zuid-Beveland and the
fan's hard dredged edges line the shores. In the new version, the channel leaves along the Westerschelde and the bed
shelves from each shore (the pale fringe). The cyan capsule south of the site is the Scheldt's fresh-water last reach,
which this decision does not change.
