# Data and performance

Earth is one mod, but the real Earth doesn't fit in a mod download. The server fetches Earth's data from GitHub the
first time it needs it, keeps it on that computer, and shares it with every world and every later Earth version. This
page says what is downloaded and when, how much disk space and memory Earth needs, how long a start takes, and the
server settings for where the data comes from. When something goes wrong, see [Troubleshooting](Troubleshooting).

**Vanilla:** a world needs nothing beyond the game itself, and a server's memory is mostly the chunks players have
loaded.

**Earth:** the server needs about 9 GB of downloaded data for a world with full land heights, keeps a model of the whole
Earth in memory (1.3 to 1.8 GB more than vanilla), and generates new land about five times more slowly than vanilla.

## Installing

Install three mods on the server and on every player's game: Earth 2.1.4, Cylinder World (1.1.0 or newer) and
Hydrology (0.6.2 or newer). Sea is recommended. Then make a new world with one of the Earth play styles (Earth · 1:100
is the default). Earth 2.0 is one mod: the data-pack mods of Earth 1.0 (Earth Terrain Data, Earth Water Data) are no
longer needed. If they are still installed, Earth copies its files from them instead of downloading them again.

Only the server downloads. In a single-player game your own computer is the server. A player who joins a server
downloads nothing beyond the mods: the climate the players' games need is inside the mod, and the monthly temperatures
of the land around each player come from the server as they play.

## What is downloaded, and when

Everything goes into `VintagestoryData/ModData/earth`, once per computer. All of it comes from Earth's data releases on
GitHub (the Temporal-Poets-Society organisation), and every file is checked against its size and checksum before it is
used.

| What | Size | When | Folder |
|---|---|---|---|
| Earth's data files (terrain, water, ground, ecology) | 289 MB | the first time the server starts any Earth world, before the first chunk | `data` |
| Ground sets (soil types, depth to bedrock, permafrost) | 172 MB | only for a world with **Rocks and ground: Ground columns**, before its first chunk | `ground` |
| Monthly temperatures (`seasons-1.bin`: each place's months, day-night range and day-to-day spread) | 58.2 MB | the first start of a world made with Earth 2.1.0 or later (or a 2.0 world opted in with `UpgradeExistingWorldSeasons`); without it that session plays with 2.0's temperatures and fetches it at a later start | `data` |
| Lake outlines, river outlines, river flow, dams | 193 MB | when a new world on GLO-90 heights first starts | `glo90` |
| GLO-90 land heights | 8.6 GB in all (26,475 squares of 1° × 1°, about 0.3 MB each) | by the world's choice below | `glo90` |
| The world's model cache | under 1 MB a world | at the world's first start | `cache` |

While the data files download at the first start, their progress shows on the game's loading screen (the "Earth data:"
lines). The data is shared: a second world, or a later Earth version, uses what is already there. A world made with an
older version keeps finding the files it was made from, since a changed file always gets a new name.

## The GLO-90 land heights: four choices

Earth's land heights come from Copernicus GLO-90, a height every 90 m on all the world's land. The whole set is 8.6 GB.
Sea floors, lakes and coasts are Earth's own data and don't need it.

The Earth tab of the world creation screen has **Terrain · GLO-90 land heights**, with four choices. It is set when the
world is created and can't be changed afterwards. Squares already on the computer are never downloaded again, so the
big download is needed once per computer, whichever world asks for it.

- **Download all: Earth's data release (GitHub, 8.6 GB)** (the default). The download of every square starts when the
  world opens, and you can play meanwhile. Players who may run the server (the `controlserver` privilege; in single
  player, you) get a window with its progress, where they can stop it and start it again. New land the world reaches
  meanwhile fetches its own squares alongside the download. The download starts again at each opening of the world until every
  square is there or a player stops it. On a fast connection the whole release takes a few minutes; a 10 MB/s connection
  takes about 15 minutes.
- **Download all before the world starts (GitHub, 8.6 GB)**. The same download, but a new single-player world waits for
  it on its loading screen before any of it is made, with progress lines there (how far, how much is left, the time
  left). Cancel on the loading screen goes back to the main menu; opening the world again then plays while the rest
  downloads, as with the default. A refused or failed download says why on the loading screen, and the world starts
  anyway. A dedicated server doesn't wait (its players couldn't join meanwhile): it downloads while it runs, as with the
  default.
- **Download as the world reaches them**. Each square, about 0.3 MB, is fetched when the world first needs it, and
  squares around online players are fetched a little ahead. New land waits for its square: right after the world opens
  you may notice the first chunks of a new area come in a little later.
- **Download all: Copernicus originals (71 GB, converted here)**. Every square from Copernicus's own files on Amazon S3
  instead of Earth's release, converted on your computer: about 71 GB to download, 8.6 GB kept, and hours of work. For
  when GitHub can't be reached; otherwise the release is the same heights, already converted.

A square that neither source has keeps Earth's built-in, coarser heights for good, so the world's chunks there match
each other. A square that can't be had for another reason (no network, a timeout) is never given up that way: see
[Troubleshooting](Troubleshooting).

### The download window and commands

- `.earthelevation` opens the window: how many squares are on the server, the free disk space, the running download
  and its time left, where to download all from (Earth's data release or Copernicus) and a **Download all...** button
  behind a confirmation that names the sizes. Only players who may run the server can start or stop it.
- `/earth elevation` says the same in chat.
- `/earth elevation download all` downloads every missing square from Earth's data release; `/earth elevation download
  all copernicus` from Copernicus's originals. `/earth elevation cancel` stops it; the squares being fetched finish, and
  the next download skips everything already there. Both need the `controlserver` privilege and work from the server
  console too.

## Disk space

| What | Space |
|---|---|
| Data files | 289 MB |
| Ground sets (worlds with ground columns only) | 172 MB |
| Monthly temperatures (worlds made with 2.1.0 or later) | 58.2 MB |
| GLO-90 land heights, all of them | 8.6 GB |
| Lake, river and dam files beside them | 193 MB |
| Free space a download all leaves on that disk | 1 GB (a single square fetched for new land needs 100 MB left free) |

So a computer with full heights needs about **9.2 GB** for Earth's data, 9.4 GB with ground columns, plus the worlds'
own saves, which grow as players explore (more at finer scales). The Copernicus choice converts square by square and
keeps only the result: it needs the same 8.6 GB, not 71 GB.

A download all that wouldn't leave 1 GB free on the disk doesn't start, and says so in chat and in the window. A
download that runs out of room stops and says so. If the data files can't be stored at the first
start, the server stops and says how much space it needs.

## Memory

Earth keeps a model of the whole Earth in the server's memory: rivers, coasts, islands, lakes, climate and the
heights it has read. Measured on a dedicated server with no player (Vintage Story 1.22.7 on .NET 10), right after the
start and after generating a 17 × 17-chunk area (Earth 2.0.0 with its default settings, which had ground columns; a
2.0.1 world with vanilla rocks was not measured):

| | Vanilla | Earth 1:100 | Earth 1:10 |
|---|---|---|---|
| Memory in use after the start | 0.7 GB | 2.0 GB | 2.5 GB |
| ... after generating the area | 0.8 GB | 2.1 GB | 2.6 GB |
| The server process after the start | 1.1 GB | 2.8 GB | 3.7 GB |
| The server process's peak while starting | 1.4 GB | 3.4 GB | 4.9 GB |

- A 1:5 world peaks at about 7.2 GB while it starts.
- An earlier build's 1:10 server held 4.5 to 4.8 GB after its start.
- Memory grows with the chunks players keep loaded, as in vanilla.
- A world with the monthly temperatures keeps the decoded parts around its players in memory: about 5 MB, a little
  more for each player (at most about 100 MB). Each player's game holds only the land around it.
- In single player your computer runs the server and the game together: count the game's own memory on top. As a
  rough guide (not measured at every scale), 16 GB of RAM is comfortable at 1:100; take 1:10 and 1:5 only with RAM to
  spare.
- Earth's garbage-collection pauses are no longer than vanilla's (a full collection took 0.26 to 0.31 s on Earth, 0.2
  to 0.44 s on vanilla, in the same test). Earth's longest stalls in that test came while the server started.

## Start-up time

- **The first start on a computer** first downloads the 289 MB of data files (and the ground sets, for ground columns)
  before the world is built: a few seconds to a few minutes, by connection.
- **Building the world's model** of rivers, coasts and straits: well under a minute (29 s at 1:5). The slowest
  parts are cached in `ModData/earth/cache` and read back at the world's later starts. They are used only by exactly
  the same Earth version, data and world settings; the world is the same either way.
- **The spawn area:** the game makes its spawn chunks as in vanilla. Their GLO-90 squares are fetched first, whatever
  the download choice.
- On a test PC with all data already downloaded, a 1:100 world was ready about 45 s after its server started, new or
  opened again.
- **New land while playing:** Earth generates chunks more slowly than vanilla. On the dedicated test server (Earth 2.0.0,
  ground columns) the 289 columns of a 17 × 17-chunk area took 27 s at 1:100 and 28 s at 1:10, against 5 s for vanilla. Flying fast over new
  land shows the edge of the generated world sooner than in vanilla.

## Server settings

These settings are in `VintagestoryData/ModConfig/earth.json` (on a dedicated server, in its own data folder). The game
writes the file with its defaults at the first start; edit it while the server is stopped.

| Setting | Default | What it does |
|---|---|---|
| `DataRepo` | `https://github.com/Temporal-Poets-Society/earth-data/releases/download/ed-v1/` | where the data files come from. A mirror's address or a local folder holding the release's files also works. Empty: never fetched (files already there are still used) |
| `GroundRepo` | `https://github.com/Temporal-Poets-Society/earth-ground/releases/download/egts-v1/` | where the ground sets come from (for worlds with ground columns). A local folder holding the three `.egts` files also works; empty: never fetched |
| `ElevationRepo` | `https://github.com/Temporal-Poets-Society/earth-glo90/releases/download/egl-v1/` | where the GLO-90 squares come from first: Earth's release, already converted. A local folder holding the release's manifest and parts also works; empty: none |
| `ElevationSource` | `https://copernicus-dem-90m.s3.amazonaws.com/` | Copernicus's originals: for squares the release can't supply, and for the Copernicus download all. A local folder holding the original `.tif` files also works; empty: none |
| `ElevationDownloads` | `3` | GLO-90 squares fetched at the same time (1 to 8) |
| `ElevationPrefetchBlocks` | `4096` | how far around online players (in blocks) squares are fetched ahead of the world; 0: none |
| `ModelCache` | `true` | keep the slow parts of the world's model between starts; false builds them at every start |

A malformed address or path is reported once in the server log, naming the setting.

### Without internet, or behind a firewall

Download the release files on another computer and either put them in place or point the settings at them:
- **Data files:** all the files of the `ed-v1` release of `earth-data` into `ModData/earth/data`, or into any folder
  that `DataRepo` names.
- **Ground sets** (only for ground columns): the three `.egts` files of `egts-v1` of `earth-ground` into
  `ModData/earth/ground`, or a folder that `GroundRepo` names.
- **GLO-90 heights, lake, river and dam files:** the whole `egl-v1` release of `earth-glo90` (its manifest and its
  parts) into a folder, with `ElevationRepo` set to that folder. Earth then reads the squares from it as if it were
  GitHub.

Every file is checked against its size and checksum in the same way as a download.

### Two servers on one computer

A single-player game and a dedicated server on the same computer share `VintagestoryData`, and with it Earth's data.
They take turns: one waits while the other fetches into the same folder.
