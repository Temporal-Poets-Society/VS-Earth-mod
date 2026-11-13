# 0023: An Earth tab on the world creation screen; Cylinder World required

Status: Agreed (the user, 2026-10-22); implemented 0.16.0, server self-tests passed, tab not yet seen in game | Date: 2026-10-22 | Decided by: the user

## Context

Every Earth setting lived in `ModConfig/earthmap.json`, which a player only finds by editing files. The settings a
player picks per world (where to start, the canals) belong on the game's own world creation screen. Earth's
proportions also need a matching world size: with Cylinder World the ring is 2 × the world length and the width left
over becomes hidden seam ocean, so a square world or a too-narrow one gives the wrong shape, and vanilla's length list
has no 204 800 for a 512 000 wide world.

Earth also kept a second layout for worlds without Cylinder World (the full width holds 360°, latitudes stretched up
to 1.25× to fill the length, polar filler or cropped poles), which nobody plays.

The user (2026-10-22), on a proposal by Claude: world size "agree" (width chosen, length automatic); spawn by
random place at the starting climate, presets (capitals) or coordinates, "A is not needed as it overlaps with C"
(the fixed place per starting climate); canals "yes"; "Earth on/off - ... it goes by unclicking the mod in mod
manager so not needed"; the other options "my artistic choice and not needed"; world edge "irrelevant as Cylinder
world intention was make N S unreachable and EW cannot be reached at all"; world height "we don't need separate slider
but if can default to 384 then would be good"; "Cylinder World - is Earth dependency and would not allow to use
without it even if technically i could be. Wmap is soft dependency - as can work but lot of critical mechanics are
there."

## Decision

**An "Earth" tab** on the world creation screen (`worldconfig.json` at the mod's root, category `earth`, labels in
`assets/game/lang/worldconfig-en.json`, which the main menu reads before any mod code runs). Its options are stored in
the world's configuration and read when a new world is created; all are set at creation only.

- **World size** (`earthWorldSize`): the world's width, 128 000 / **256 000 (default)** / 512 000 / 1 024 000 /
  2 048 000 / 4 096 000 blocks; the length is set to 0.4 × the width, so Cylinder World's Earth proportions give a
  ring of 0.8 × the width (204 800 for 256 000) with a tenth of the width as hidden seam ocean at each side. Earth
  writes both into the new world's `worldWidth` / `worldLength` and the save's map size on the server, before the map
  is built (the game reads `worldWidth` / `worldLength` before mods start and builds the map after their start phase).
  Option "World generation tab": keep vanilla's width and length as chosen there.
- **Spawn** (`earthSpawn`):
  - **Random at the starting climate (default)**: dry, gentle lowland anywhere on Earth whose real yearly mean
    surface temperature lies in vanilla's band for the chosen starting climate (hot 28–32 °C, warm 19–23, temperate
    6–14, cool -5 to 1, icy -15 to -10), with land around it (not a speck of an island), picked from the world seed:
    the same seed gives the same place. If none is found, the former fixed place for that climate (the Upper Rhine,
    Andalusia, the Sahel, the White Sea, Taymyr).
  - **A capital** from a list of 36 around the world: the nearest dry, gentle land to it, at any height.
  - **Coordinates** (`earthSpawnCoordinates`, "lat, lon" in degrees): the nearest dry, gentle land, at any height.
  - The fixed place per starting climate (the old `auto`) is gone as a choice: the presets cover it.
- **Suez Canal** (`earthSuezCanal`) and **Panama Canal** (`earthPanamaCanal`), off by default, as decided in
  [0020](0020-canal-gaps.md). `UpgradeExistingWorldCanals` now applies the world's own canal options, which an
  existing world changes with `/worldconfig earthSuezCanal true` before the restart.
- `SpawnPlace`, `SuezCanal` and `PanamaCanal` leave `earthmap.json`; all other settings stay there.

**Not on the tab:** Earth on/off (the mod manager does it), the artistic settings (relief curves, rivers, regional
content, weather, ...), the world edge (Cylinder World's walls and seam make the edges unreachable).

**World height** stays the game's own slider. A mod can't change its default: mod code doesn't run in the main menu,
and playstyles can't set the height. The README recommends 384, and the server log says so when a new Earth world is
created lower.

**Cylinder World is required** (`modinfo.json` dependency, 0.11.11 or later, the first with `SetSpawn`). Earth no
longer lays the Earth out on a world without it: the flat-world layout (`EdgeSea`, `MaxStretch`, the stretched
latitudes) is removed. A world where Cylinder World is not active, or lacks what Earth needs (layout v3 with polar
walls and polar climate), gets no Earth and a clear error, as failed steps do in [0008](0008-failed-activation.md).

**Wmap is recommended**, not required: Earth works without it, but its charts and far terrain carry much of the play.
The README says so and the server log notes when it is missing.

## Alternatives

- **Length chosen, width automatic**: players and vanilla name worlds by width.
- **Earth playstyles** ("Earth 256k", ...) setting vanilla's width and length: proven, but one entry per size in the
  playstyle list and still no 204 800 length in vanilla's own dropdown; kept as the fallback had the map size not been
  settable from mod code.
- **A fixed place per starting climate** (the old `auto`): the capital presets cover it (the user).
- **Forcing height 384** when a new world comes with the default 256: it would overrule a player who chose 256.
- **Keeping the flat-world layout**: the user rules out Earth without Cylinder World.

## Consequences

- Vanilla's width and length dropdowns stay on the World generation tab but are overruled by Earth's size unless it is
  set to "World generation tab". Dedicated servers set `earthWorldSize` in `serverconfig.json`'s world configuration
  (the self-test tooling does).
- Existing worlds keep their saved spawn and canals; their configuration lacks the new keys and reads as the defaults,
  which only matters to `UpgradeExistingWorldCanals`.
- Old `earthmap.json` files lose `SpawnPlace`, `SuezCanal` and `PanamaCanal` when rewritten (a server that had
  `SpawnPlace` set uses the tab's default for its next new world).
- Saves made without Cylinder World no longer load as Earth worlds. Nothing is public yet (test worlds only).

## Implementation

0.16.0: `worldconfig.json`, `assets/game/lang/worldconfig-en.json`, `src/EarthWorldOptions.cs` (reading the options,
the map size by reflection on the save's `MapSizeX` / `MapSizeZ` in `StartServerSide`), `src/Core/SpawnSearch.cs`
(random place at the starting climate, dry land near a place, size and coordinate values), `src/EarthMapSystem.cs`
(spawn, canals, height and Wmap notes), `src/EarthMapConfig.cs`. Cylinder World required: `modinfo.json`,
`src/Core/EarthLayout.cs` (only Cylinder World's ring and latitudes), `src/EarthMapSystem.cs` (`CylinderWorldMissing`,
refusal of saves without it; `SaveParams` only writes active worlds), `src/EarthWorldGen.cs`, `src/CylinderCompat.cs`,
tests (no flat layouts), `tools/run_selftest.sh` (no `--no-cylinder`; `--earth "key=value;..."`; `--size` sets the Earth
size to `vanilla`); `docs/selftest-report-nocyl-1024k-h384.txt` deleted.

Tests: `tests/EarthMapTests` `CheckSpawnSearch`: sizes, coordinates, every starting climate finds a place in its band
for seeds 12345, 777 and 2026 (3–241 ms; the same place for the same seed), dry land near seven capitals (Mexico City
and Addis Ababa at their height). The other test projects pass; EarthMapTests' Lake Geneva step check fails as on
0.15.6 (not related). Server self-tests (game 1.22.7, Cylinder World 0.11.11): Earth size 512000 with a 256000 × 102400
server config gave a 512000 × 204800 world (Cylinder World: ring 409 600), start Riga, the Suez cut at Lake Timsah;
defaults with seed 777 and height 256: 256000 × 102400, a random temperate start at 44.5°N 121.5°E, the height note,
no failed checks; coordinates -33.9, 151.2 and the Panama cut; Cylinder World disabled: the error and vanilla terrain.

## History

- 2026-10-22: proposed by Claude, agreed by the user with the changes quoted above.
- 2026-10-22: implemented in 0.16.0; offline tests and server self-tests passed; the tab on the game's own screen and
  play not checked.
- 2026-10-22: 0.16.0 crashed the game's Singleplayer world list (it reads each mod's `playStyles` without a null check,
  `GuiScreenSingleplayer.getHoverText`); 0.16.1 declares an empty `playStyles` list. Server self-test passed; the
  screens not yet rechecked in game.
