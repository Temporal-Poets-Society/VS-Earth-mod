# 0075: the Earth tab's "Biomes" choice turns Earth's biome rules back to vanilla

Status: **built** (EA-0061; the author, 13 November 2026: "for new biomes - we should add option to config back to vanilla
in Earth GUI settings"). New worlds only (0002). Not seen in game by the author yet.
Date: 2026-11-13.

## The choice

The Earth tab's Nature group gets **Biomes** (`earthBiomes`, creation only, next to `earthNature`):

- `earth` (default): Earth's biome rules, as every new world had them before.
- `vanilla`: none of them. The world saves `EarthParams.VanillaBiomes = 1` (proto 202) and the biome rules below as 0.

It is independent of **Nature** (0047): Nature decides which plants and animals grow and spawn where (the 1318 catalogue,
regional trees, animal ranges); Biomes decides how dense the vegetation is and what the ground surface looks like. All
four pairs are valid. With Nature 1318 and vanilla biomes, the 1318 catalogue still picks species and crops (every place
counts as arable without the zones), the tree picker uses its forest-type factors from the climate instead of the zones',
and the catalogue's ephemeral annuals (EarthGroundMoisture's blooms after rain) do not bloom, because there is no thinned
dry-land grass for the ground moisture to act on.

## What "biome" covers (rules frozen for new worlds, 13 November 2026)

| Rule (EarthParams) | What it does | Biome? | With vanilla biomes |
| --- | --- | --- | --- |
| `ZoneRules` (habitats step 1) | 22 vegetation zones from climate set forest and shrub density; rules 2 also drop vanilla's floor of a tree every 7 to 8 chunks | yes | 0 |
| `FireRules` | prairie, parkland, Pampas, forest-steppe kept open | yes (needs zones) | 0 |
| `WetlandRules` | open bog, marsh, saltmarsh without forest | yes (needs zones) | 0 |
| `GroundCoverRules` | dry land keeps a share of vanilla's tall grass by aridity | yes | 0 |
| `MoistureRules` | grass greens after rain, withers after; 1318 ephemeral blooms | yes (needs ground cover) | 0 |
| `GrassWinterRules` | tall grass lies flat after the snow and grows back | yes | 0 |
| `AlpineSurfaceRules` + the surface pass's altitude zones | alpine turf, fell-field, scree towards the snow line | yes | 0, and no altitude zones |
| Forest and shrub maps scaled by Earth's forest potential (`RegionMaps.ForestAndShrubs`, since 1.0; no rules number) | vanilla's noise × Earth's potential | yes | skipped: vanilla's own maps |
| `SoilRules` (0048, soil by aridity) | where vanilla's block layers give soil or sand | no, see below | kept |
| `AridityRules`, climate rules, weather | the climate the game and every rule reads | no (climate) | kept |
| `PondRules`, `SpringRules` | surface ponds, springs | no: their own Earth-tab choices | kept |
| `ShoreZoneRules`, `ShoreRules` (0069, 0070) | no trees or grass on the beach strip | no (coasts) | kept |
| Rock exposure on steep and cold ground, nival snow, glaciers, beaches, dry river beds (surface pass) | terrain | no | kept |
| `LocalAlpineEcology` (real altitude for the tree climate and fauna altitudes) | corrects vanilla's altitude climate to the real elevation | no (climate at height) | kept |
| Rocks, ores, ground columns, rivers, lakes, coasts, geothermal, water table | terrain and geology | no | kept |

**Soil by aridity stays Earth's.** It is ground material, not vegetation: it decides whether vanilla's own block layers
put soil or sand where Earth's real rainfall (lower than vanilla's noise in semi-arid land) would otherwise bare the
steppe, and the ground columns of "Rocks and ground" read the same rules (soil rules 2 and 3: real deserts without topsoil,
floodplains). Switching it with Biomes would mix a geology choice into a vegetation one; vanilla soil already follows
"Rocks and ground: vanilla" plus the climate.

## Why 0 is vanilla for each, and what needed more than 0

Read in the code (13 November 2026) and against vanilla 1.22.7 (`devtools\vs-ref\decompiled\1.22.7`):

- Ground cover 0: `TallGrassCoverPrefix` returns true (vanilla's tall grass); the shore strip rule (0070) is a coast rule
  and stays. Moisture needs ground cover ≥ 1 (`EarthGroundMoisture.Watch`), so it is off. Grass in winter 0: its watch
  never starts.
- Zones 0: no zones, so no tree floor change (`NoTreeFloor` false: vanilla's floor), no fire (`model.Fire` needs zones),
  no open wetlands (`OpenWetlands` needs zones). But zone rules 0 alone fall back to Earth 1.0's forest potential, a rain
  curve that still scales vanilla's forest and shrub maps. Vanilla's `MapLayerWobbledForest` already thins its noise by
  the climate map's rain × temperature, which in an Earth world is Earth's real climate. So `VanillaBiomes` also skips
  `RegionMaps.ForestAndShrubs` (`EarthWorldGen.OnMapRegionGen`): the maps stay vanilla's.
- Alpine surface 0: with real-altitude ecology the surface pass still drew the older altitude zones (snow, fell-field,
  scree, thin soil), so `VanillaBiomes` turns the pass's zones off (`AlpineSurfacePass.SurfaceChunk.Zones`). What is left
  is the terrain's own rock exposure on steep and cold ground and snow in the nival zone, as in a world without
  real-altitude ecology. The opt-in mountain upgrade (`UpgradeExistingWorldMountainTerrain`) leaves such a world's
  alpine surface rules at 0.

## Where it shows

- Log at world load: `Earth biomes: vanilla (world setting, decision 0075): ...` and none of the zone, fire, wetland,
  ground cover, ground moisture, grass-in-winter or alpine surface lines.
- `/earth where`: `Biomes in this world: vanilla (world setting) ...` or `Biomes in this world: Earth's: zones v2, fire v2, ...`.

## Checks

- `tools\run-earth-playstyle-tests.ps1`: Freeze with every Nature × Biomes pair, round trip, older saves keep Earth's
  biomes, the menu entry and its strings.
- Offline suite on the branch: all projects pass.
- AgentBridge bench, two fresh 1:100 worlds from staged play-style copies (`earthBiomes` set in the play style; Nature
  1318 in both), 13 November 2026. Vanilla: the log has the `Earth biomes: vanilla` line and no zone, fire, wetland,
  ground cover, moisture, grass-in-winter or alpine lines; `/earth where` says vanilla biomes. Earth: all seven rule
  lines, `/earth where` lists zones v2, fire v2, wetlands v1, ground cover v1, moisture v1, grass in winter v1, alpine
  surfaces v2. Same two places from 175 m up (`backlog/media/EA-0061-*.png`): the Pripyat marshes (51.85 N 26.0 E:
  Earth's mixed hemiboreal forest with 18 % open bog against vanilla's thinner, even scatter of trees) and the forest-steppe
  near Poltava (49.6 N 35.0 E: Earth's open steppe against vanilla's closed forest).
