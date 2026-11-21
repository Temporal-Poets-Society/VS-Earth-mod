# Troubleshooting

Most problems with Earth come from its downloads, from disk space or from memory. Earth's rule is to stop and say why
rather than make a world from partial data, so the first thing to read is the message it leaves. For what is
downloaded and how big it is, see [Data and performance](Data-and-performance).

## Where to look

- **The server log:** `VintagestoryData/Logs/server-main.log` (in single player too; on a dedicated server, the `Logs`
  folder of its data folder). Earth's lines start with `[earth]`. The ones about data say `Earth data:` (the data
  files), `Earth elevation:` (the GLO-90 land heights), `Earth lakes:`, `Earth rivers:`, `Earth river flow:`,
  `Earth streams:` and `Earth dams:` (the files beside the heights), `Earth soil:` and `Earth geology:` (the soil and rock
  maps). Errors are marked `[Error]`.
- **The game's own log:** `VintagestoryData/Logs/client-main.log`, for problems on a player's side.
- **The loading screen** of a single-player world shows Earth's data downloads and anything that stops the start.
- **In game:** `/earth elevation` (or the `.earthelevation` window) shows how many GLO-90 squares are on the server,
  the free disk space and the last download.

When you report a problem, attach `server-main.log` and name the Earth, Cylinder World and Hydrology versions.

## Downloads

### The server stops at its first start: "Earth's data files ... are missing ... and could not be had"

The data files (296 MB) are fetched before any world is built, and the server doesn't build a world without them. The
message names the missing files and why: the network, a full disk, or no source set.
- **No network, or GitHub blocked:** let the server reach GitHub and start again, or download the files elsewhere and
  put them in place (see "Without internet" on [Data and performance](Data-and-performance)).
- **A full disk:** the message says so; free space on that disk and start again.
- **`DataRepo` empty or wrong** in `ModConfig/earth.json`: set it back to the default address, a working mirror or a
  local folder holding the files.

### The server stops while the world starts: "... could not be fetched while the world starts"

The world's first chunks, or the lake, river, stream and dam files beside the heights, couldn't be downloaded while the
world was starting. The server stops on purpose, with nothing saved, so that a short failure doesn't leave the world
with coarser heights or without its lakes and rivers for good. Start again once the network works.

### A new world stops: "... this world needs ..., and it can't be had"

A new world that needs a data file it can't have stops before it generates anything: the release doesn't have the
file, the copy on the computer is an older one than this Earth reads, or no source is set in `earth.json`. In single
player the message is on the world's loading screen; on a server, in the log. It names the file and why, the folder to
put it in and the release it comes from, and how to make the world without it where there is a way (for the streams:
set `StreamsLayer` to `false` in `earth.json`).
- Nothing is saved. The world stays in the world list; start it again once the file is there, and it is made as new,
  with the same settings.
- A world an earlier Earth version had already marked as going without a file keeps working without it, so that its
  chunks match, and says so in chat when it opens.
- The soil map and the rock map are the exception: if the release doesn't have them, a new world uses Earth's coarser
  soil or rock map for good and the log says so (`Earth soil:`, `Earth geology:`).

### The download is slow, or stopped

- The window (`.earthelevation`) shows the progress, the time left and any error. **Download all...** there starts it
  again; squares already on the computer are skipped.
- A download that stops on an error says why in the window and in the log. Squares that couldn't be had are fetched
  again when the world reaches them, and the default choice starts the download again at the world's next opening.
- `ElevationDownloads` in `earth.json` sets how many squares download at once (1 to 8, 3 by default).
- If GitHub is slow or blocked but Amazon S3 isn't, the window can download all from **Copernicus originals** instead:
  much more to download (about 71 GB) and hours of converting, the same heights in the end.

### Two worlds or servers on one computer are slow to start

They share Earth's data folder and take turns downloading into it: one waits for the other (up to 20 minutes).

## Playing offline

A world made with full land heights needs each GLO-90 square of land the first time a chunk there is made.
- **Download them all first:** with all squares on the computer, the world never needs the network again. The Earth
  tab's default downloads them all while you play; **Download all before the world starts** does it before the first
  chunk; on an existing world, `.earthelevation` or `/earth elevation download all`.
- **The data files** (296 MB) are needed at every start, but only downloaded once.
- **Offline at the world's first start:** the server stops (see above) instead of making the spawn area without its
  heights.

### Holes in the ground, and "can't be made yet" in chat

If a square of land can't be downloaded while you play (no network, the server can't reach GitHub), Earth doesn't make
that land with made-up heights. The chunks there are **left empty**, and every player gets a chat line:

> Earth: the land of N48E011 (48° to 49° latitude, 11° to 12° longitude) can't be made yet: its height data could not
> be downloaded. Chunks there are left as empty holes with nothing to stand on: keep away from them and don't build
> there. They are made when the world is next started with the data here, and anything built in them is deleted then.

What this means:
- The holes are real: nothing to stand on, open to the void. Keep away from them.
- **Don't build in them.** When the world next starts with the square downloaded, those chunks are made properly, and
  anything built in them is deleted.
- Earth tries the square again 5 minutes after a failed try; the chat line comes at most every 10 minutes for a square.
- To fill them: get the square (let the server reach the network, or download all), then restart the world.
- Chunks made before the network went away are not touched.
- A known limit: the game's coarse regional maps (climate, forest cover) made for that area while the square was
  missing keep Earth's built-in heights for good, so trees and climate there can differ a little from the land around.

### "/earth tp" says the heights couldn't be had

`/earth tp` needs the `tp` privilege. To a place whose square isn't on the computer it downloads the square first and
waits up to 2 minutes. If it can't
be had, you are not teleported (you would land over a hole), and the reply says why. Try again once the network works.

## Disk space

- **"Not started: only ... free on the disk holding ...":** a download all would leave less than 1 GB free on that
  disk, so it didn't start. Free some space, or choose **Download as the world reaches them** for new worlds (squares of
  about 0.3 MB each, as needed). The full heights need 8.6 GB.
- **A download that fills the disk** stops and says so in the window and the log; free space and start it again.
- The game itself also stops a server whose disk is nearly full, to protect the save (vanilla's `DieBelowDiskSpaceMb`
  in `serverconfig.json`).
- Earth's data lives in `VintagestoryData/ModData/earth`. Deleting the `glo90` folder frees up to 8.9 GB, but every
  world with full heights then downloads its squares and its lake, river, stream and dam files again; deleting `data`
  makes the next start download 296 MB again.

## Memory and speed

- **The server runs out of memory or crashes at the start of a big world:** a 1:10 server peaks near 5 GB while it
  starts and a 1:5 server near 7.2 GB; in single player the game needs its own memory on top. Choose a coarser scale
  (1:100 is the default) or a computer with more memory. See the memory table on
  [Data and performance](Data-and-performance).
- **The first start takes a long time:** the data download (shown on the loading screen) and the world's model build
  come before the spawn area. Later starts reuse cached parts of the model (`ModelCache` in `earth.json`).
- **New land appears slowly:** Earth generates chunks about five times more slowly than vanilla. Flying fast over new
  land outruns the generator sooner.

## The server stops: "parts of generation this world's rules use are off this session"

Earth checks at every start that everything the world was made with still works: its rock regions, ore layers,
springs, water table, plants as around 1318, volcanoes, and Earth's changes to vanilla generation. If one is off (a game
update changed something Earth hooks into, another mod keeps it out, a data file is missing or damaged), the server
stops before making any chunk, and the log names each part with its reason. Nothing of the world is changed.
- Update or remove the mod that keeps a part out, use an Earth version made for your game version, reinstall Earth,
  or let it fetch a damaged data file again.
- An owner who accepts that new chunks get vanilla's blocks there, possibly with a visible seam against older chunks,
  can set `AcceptMissingFeatures` to `true` in `earth.json`. It applies to every Earth world on that server or
  computer, so set it back to `false` once the cause is fixed.

## The server stops: "Earth cannot generate this world ... without Earth's terrain"

The game made a chunk without Earth's land, because another mod replaced or wrapped the step of world generation that
Earth's terrain runs in. With the game, Earth and its required mods alone this doesn't happen. Earth stops rather than
make a world of broken land, and the message names the mod and what it put in that step (the line before it in the log
lists the whole step). Remove that mod, or keep it out of Earth worlds. A mod that only previews far terrain on chunks of
its own doesn't stop an Earth world: Earth leaves those chunks alone and says so once in the log.

## Worlds from older Earth versions

Worlds made with Earth before 2.0.0 (0.x, 1.x and the 2.0.0 test builds) are not supported by Earth 2.0: start a new
world. Worlds made with 2.0.0 or later keep working in later versions.

The other way round, an older Earth refuses a world a newer one made: "this world was saved by a newer Earth", naming
the version, and the world is left unchanged. A world made with Earth 2.2.0 needs 2.2.0 or newer. See [Saves](Saves).
