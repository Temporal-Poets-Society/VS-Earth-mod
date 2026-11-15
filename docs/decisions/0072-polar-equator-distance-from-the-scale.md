# 0072: polarEquatorDistance is the world's own equator-to-pole distance

Status: **built** for 2.0.0-rc.9, in Earth 2.0.0 (EA-0041; the author, 13 November 2026: "then fix this also. if need Cylinder world
changes then do it"). New worlds only (0002): a world made before keeps the value it saved. Cylinder World unchanged.
Date: 2026-11-13.

## The problem

Vanilla's world setting `polarEquatorDistance` is the distance in blocks from the equator to a pole. All five Earth
play styles (1:200 to 1:10) set it to 100,000, right only for 1:100, and the Earth tab's size (0068) never touched it.
Rough Seas takes its metres per block from it (Sea's `docs/open-sea-contracts.md`, section 11), so on a 1:10 Earth its
waves would be built for blocks ten times too big.

## Who reads it (vanilla 1.22.7, the suite, 13 November 2026)

| Reader | What it does with it | On an Earth world |
| --- | --- | --- |
| Vanilla `GenMaps` latitude (`getLatitude`, server and, by the `latitudedata` packet, client) | A triangle wave over z with period 4 × the distance | Replaced: Cylinder World sets `Calendar.OnGetLatitude` to its own `(EquatorZ − z) / PoleDistance` on both sides after vanilla. Seasons, daylight, the night sky, `ModTemperature`, music and Earth's climate hooks read that one |
| Vanilla `NoiseClimateRealistic` (the climate noise) | The temperature bands' width and the spawn offset | Its temperature and rain are replaced by Earth's climate layer (`EarthClimateLayer` keeps only the geologic-activity byte, which comes from a position-seeded random number, not from this distance). The same noise picks vanilla's landform map, which Earth's terrain does not use; vanilla's tiled dungeons that need a `veryflat` landform read it, so where such a dungeon is tried in a new world can differ (they were already placed by noise, not by Earth's terrain) |
| Vanilla `/wgen` debug maps | Draw the noise | Debug only |
| `SurvivalConfig.PolarEquatorDistance` | Not read by anything | — |
| Astra Terra (`WorldClimateScale`, `LatitudeMapper.MapWorldLongitude`) | Longitude fallback: the ring is 4 × the distance | Earth registers its longitude provider, so the fallback is not used; were it used, the right value now gives Earth's ring to 0.3% |
| Rough Seas (not in this folder; Sea's spec section 11) | Metres per block | Right after this decision |
| Cylinder World | Not read (only a comment); its latitude, poles, walls and seasonal ice come from its own `SeamParams` | Unchanged |
| Earth, Sea, Ships, World Map, Sea Monsters, Hydrology, Hydrate or Diedrate | Not read | — |

## Decision

1. A new Earth world writes its own equator-to-pole distance into its world configuration when Earth creates it (in
   `ResolveParams`, right after `EarthWorldOptions.Freeze`, at `WorldReady`): `round(90 × BlocksPerDegreeZ)`, which is
   Cylinder World's pole distance (half the world's length less its walls), the same distance the calendar's latitude
   uses. That is 10,000 km over the scale to within 0.3% (world lengths are whole 512-block map regions): 50,128 blocks
   at 1:200, 100,048 at 1:100, 1,001,936 at 1:10, 2,003,664 at 1:5, with Cylinder World's 48-block walls. It replaces
   the play style's or the World generation tab's value (the log line names the old one), because on an Earth world
   any other value contradicts the map; the Earth tab's size, the World generation tab's size and Cylinder World's wall
   depth all come through `BlocksPerDegreeZ`.
2. The world generators start after `WorldReady` (`ServerMain`: `WorldReady`, then `TriggerWorldgenStartup`), so vanilla's
   `GenMaps` reads the new value; players join later and get it with the world configuration.
3. The play styles show their scale's nominal value (10,000 km over the scale): 50,000, 100,000, 200,000, 400,000 and
   1,000,000. The game's customize screen adds a value its list lacks (1,000,000) to the dropdown.
4. Existing worlds keep their saved value (0002): changing it in a running world would change vanilla's noise for new
   chunks, and nothing on an Earth world needs it except Rough Seas.

## Verified (13 November 2026)

- `tests/EarthWorldOptionsTests` (774 checks): the play styles' values, the distance at 1:200 to 1:5 against 10,000 km
  over the scale, the writes (replacing, unset, unusable geometry left alone).
- AgentBridge bench, fresh worlds with Earth, Cylinder World 1.1.0-rc.3 and Hydrology 0.6.2-rc.6, rc.8 against rc.9 at
  1:100 and 1:10, with a probe mod reading the world configuration, `OnGetLatitude`, the sun's height at noon on four
  dates and the world-gen climate on server and client: rc.8 saved 100,000 at both scales; rc.9 saved 100,048 at 1:100
  and 1,001,936 at 1:10 (log: "was 100000" / "was 1000000"), the same on the client and in vanilla's `/worldconfig`, and
  still after reopening the world (not written again). At London, Cairo (1:10), Nairobi, Buenos Aires, Svalbard and
  McMurdo the latitude, the four sun heights, the world-gen temperature and rain, the ground height, Cylinder World's
  walls, latitude, base temperature and Earth's sea-ice target and ice-sheet lines were identical between rc.8 and rc.9.
  Only vegetation and snow depth differed, as between any two runs.

## Not done

- Plain Cylinder World worlds (without Earth) keep the World generation tab's value, which need not match Cylinder
  World's latitude either; that is Cylinder World's to decide (CW backlog).
