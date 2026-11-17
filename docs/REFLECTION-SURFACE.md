# What other mods read from Earth by reflection

World Map, Hydrology and Cylinder World use Earth without a build reference: they find its mod systems by type name and
read the members below by name. Renaming one, changing its type or kind (field or property), adding an overload to a
method bound by its bare name, or giving a method an optional parameter where a consumer asks for the exact parameter
types breaks those mods in their worlds, not at Earth's build, and often without a word in the log.
`tests/ReflectionContractTests` (in `tests/Run-All.ps1`) loads the built `EarthMap.dll` by path and checks each member
by the lookup its consumer uses, naming the consumer when one fails (EA-0132). Change this list and the check together.

The versions checked are the released ones Earth 2.0.0 runs with: World Map 0.5.3, Hydrology 0.6.2, Cylinder World 1.1.0.
Sea reads nothing from Earth by reflection: it takes Earth's weather through the open sea contracts' `weather` role
(`src/Core/WeatherContract.cs`, Earth decision 0017), which `tests/WeatherContractTests` checks.

## Mod systems

| Member | Kind | Read by |
| --- | --- | --- |
| `EarthMap.EarthMapSystem` | mod system, found by its full name | World Map, Hydrology (`GetModSystem(name)`), Cylinder World |
| `EarthMapSystem.ExecuteOrder()` | 0.05 | Cylinder World's `EarthPolarSystem` (0.09) binds Earth's model after Earth's start |
| `EarthMap.EarthWeather` | mod system | World Map (the barometer) |
| `EarthMap.EarthGeology` | mod system | World Map (the ore heat maps) |
| `EarthMap.EarthOreGenerator` | deposit generator type, matched by its full name | World Map (which ores Earth places) |

## `EarthMapSystem`

| Member | Kind | Read by |
| --- | --- | --- |
| `Params` (`EarthParams`), `Model` (`TerrainModel`) | public properties | World Map (field or property), Cylinder World (`AccessTools.Property`) |
| `LongitudeAt(double)` | public instance method returning `double?` | World Map (the HUD clock) |
| `RiverQueryVersion` | public `int` property | Hydrology |
| `RiverAt(int, int, out bool, out double, out double)` | public instance method returning `bool` | Hydrology |
| `RiverFlowAt(int, int)` | public instance method returning `int` | Hydrology (query version 2 and later) |
| `RiverSpanAt(int, int, out int, out int)` | public instance method returning `bool` | Hydrology (optional) |
| an exception named `RiverQueryNotReadyException` | matched by its type name, on the exception or an inner one | Hydrology |
| save key `earth-creating` (`CreatingKey`) | Earth's mark while it creates a world | Cylinder World |
| save key `cylinderworld-seam` (`CylinderRecordKey`) | Cylinder World's record, which Earth reads | Earth (pinned on Cylinder World's side by its CW-0012) |
| world-config `hydrologyFillVessels` (`HydrologyVesselsKey`), `hydrologyFillVesselsWithRiverQuery` (`HydrologyVesselsWithQueryKey`) | `bool` attributes Earth sets in a world's configuration (river steps with sills; with the river query) | Hydrology (`H2Filling.WorldVesselsKey`, `WorldVesselsWithQueryKey`) |

## `EarthParams`

| Member | Kind | Read by |
| --- | --- | --- |
| `Enabled` | public `bool` **field** | World Map (field or property), Cylinder World (`AccessTools.Field`) |

World Map also serializes the whole `EarthParams` (JSON) into its chart cache key: any saved field changes the key.

## `TerrainModel`

| Member | Kind | Read by |
| --- | --- | --- |
| the class itself | World Map snapshots it with `MemberwiseClone` | World Map |
| `Geometry`, `Data` | public **fields** | World Map (field or property), Cylinder World (`GetField`) |
| `FlatZones` | public **field**, an `IList` with a parameterless constructor | World Map (copies the list into its snapshot) |
| `GetColumn(double, double)` | public instance method returning a struct | World Map, Cylinder World |
| `GetSurfaceColumn(double, double)` | the same, returning `GetColumn`'s type | World Map (the surface as generated, craters included) |
| the column's `Top`, `WaterTop`, `IceBottom` (`int`), `Water` (`byte`), `River`, `Beach` (`bool`), `Lat`, `Lon` (`double`) | public fields | World Map (`Expression.Field`), Cylinder World (`Lat`, `Lon`, `Top`, `IceBottom`, `Water`; `WaterTop` optional) |
| `TerrainGeoAt(double, double)` | public method; its result's `Lat`, `Lon` (`double`), `InGutter`, `BeyondPole` (`bool`) fields | World Map |
| **`ClimateAt`** | bound **by its bare name** (`GetMethod(name)`): it must have **no overload**; `void (double, double, out double, out double, out double)` | World Map |
| `Vertical`'s `SeaLevel`, `MapHeight` | numbers | World Map |
| `ForestPotential(double, double)` | returning `double` | World Map |
| `ClimatePixelForVanillaLapse(double, double, int, float, float)` | returning `int` (else `ClimatePixel`, the same) | World Map |
| `GrassSeasonAt(double, double, float[])` | returning `int`, the index into `GrassPhenology.Codes`; fills a curve of `GrassSeason.Steps` (24) | World Map |
| `GrassPhenology.Codes` | the order evergreen, season80, season66, season52, season38, summerdry, steppe, and the assets `earth:textures/environment/seasons/earthgrass-<code>.png` (kept for World Map: from 2.1.3 the game's colour maps hold the seven as bands of one map, `earthGrassSeasons`, decision 0091) | World Map (its share table and far terrain's maps, by index and name) |
| `Rivers`: the private field `segs` (an array of a struct with `X1`, `Z1`, `X2`, `Z2`, `Q`) and `HalfWidth(double)` | any visibility for the fields; public method | World Map |
| `GutterSeabed` | public `int` **field** | Cylinder World |
| `RealSeam` | public `bool` property | Cylinder World |

## `Geometry` and `Data`

| Member | Kind | Read by |
| --- | --- | --- |
| `Geometry.RingStart`, `RingLength` | numbers | World Map |
| `Geometry.EquatorZ`, `BlocksPerDegreeZ` | public `double` fields | World Map, Cylinder World |
| `Geometry.Wraps` | `bool` | World Map |
| `Geometry.ToGeo(double, double)` | public method; its result's `Lat`, `Lon`, `InGutter`, `BeyondPole` | World Map |
| `Data.GlacierCoverage(double, double)` | returning `double` | World Map |
| `Data.HasGlaciers` | `bool` | World Map |
| `Data.HasTerrain` | `bool` property | Cylinder World |
| `Data.SampleClimate(double, double)` | returning a struct with `TMean`, `TCold`, `TWarm` (`double` fields) | Cylinder World |

## Elsewhere

| Member | Kind | Read by |
| --- | --- | --- |
| `EarthMap.Core.FineElevation.BuiltInOnly` | public static `bool` property with a public setter | World Map (`GetProperty`, `Public \| Static`), Cylinder World (`AccessTools`) |
| `EarthWeather.Model` | public instance property | World Map |
| its `Sample(double, double, double)` | public method; the result's `Pressure` (`double`, hPa) | World Map (the barometer; the weather role's `pressure` gives the same from Earth 2.0.0) |
| `EarthGeology.RegionalRock(double, double, int, int)` | public instance method returning `int` | World Map |
| `EarthWorldGen.RockFloorTop(int, int)` | static, any visibility, **by its exact parameter types**, returning `int` | World Map. Keep the two-argument overload: an optional third parameter isn't found by that lookup (EA-0132) |
| `RockFloorPass.RockFloorReading(DiscDepositGenerator, BlockPos, ref int[])` | static, any visibility, returning `void` | World Map |

## Read without reflection

Pinned elsewhere, listed so a change here is made knowingly:

- The mod id `earth`: Cylinder World asks `IsModEnabled("earth")`, then `"earthmap"` (Earth before 2.0), to tell an
  Earth that failed to start from no Earth; World Map shows `GetMod("earth")`'s version in its messages.
- The weather role (`seacontracts:weather`, `WeatherContractTests`): Cylinder World 1.1.0's seam rain reads `owner`,
  `writesVanilla` (`bool`), `revision` (`Func<long>`) and `rain`, and steps aside only while the owner also has a Harmony
  patch on vanilla's `WeatherSystemBase.GetRainCloudness` (any owner but Cylinder World's own); Earth's is the "rain and
  rain clouds" patch in `EarthWeather.Patches.cs`.

## What the values mean

The checks pin shapes; the consumers also hard-code what some values mean, which a change could break while every check
passes. As they read them today:

| Value | Meaning the consumers rely on | Read by |
| --- | --- | --- |
| the column's `Water` | 0 none, 1 sea, 2 lake, 3 salt lake: World Map takes any non-zero as water and 2 for a lake that can freeze; Cylinder World takes 1 for the sea | World Map, Cylinder World |
| the column's `IceBottom` | the lowest glacier-ice block, `int.MaxValue` for none: `IceBottom <= Top` is a column topped with ice | World Map, Cylinder World |
| the column's `WaterTop` | the top water block, -1 for none | World Map, Cylinder World |
| `TerrainModel.GutterSeabed` | 1: the gutter's seabed is the real one; 2: the seam meridian's land is carried across as well; 3 (`RealSeamVersion`): no gutter (`RealSeam`) | Cylinder World |
| `GrassSeasonAt`'s result | the index into `GrassPhenology.Codes`, below 0 for no map of Earth's | World Map |
| `ClimatePixelForVanillaLapse`'s result | the stored climate byte as vanilla lapses it: temperature in bits 16 to 23, rain in bits 8 to 15 | World Map |
| save key `earth-creating` | non-empty while Earth is still creating the world, cleared once its params are stored | Cylinder World |
| `RiverQueryVersion` | 0 off; 1, 2, 3 the river query's versions (`RiverFlowAt` from 2) | Hydrology |

## How each one looks them up

- **World Map** (`Wmap/src/Integrations/WorldSource.cs`, `EarthHeights.cs`, `EarthPressure.cs`, `LocalClock.cs`,
  `Server/OreHeatServer.Pick.cs`): values by `GetField(name)`, else `GetProperty(name)`; methods mostly by name and
  parameter types (`GetMethod(name, types)`), then `CreateDelegate` or compiled expressions (`Expression.Field`,
  `Expression.Unbox`), which need the exact types above. `ClimateAt` alone is found by its bare name. A missing terrain
  member makes the atlas chart recorded terrain only, with an error naming Earth; a missing ore member leaves the ore
  heat maps on vanilla's strata or columns with a warning; `RockFloorTop` was missed without a word.
- **Hydrology** (`Hydrology/src/EarthRiverQuery.cs`): `GetModSystem("EarthMap.EarthMapSystem")`, then `GetProperty` and
  `GetMethod(name, Public | Instance, types)` with the return types above; the not-ready exception by its type name.
  Without the query, Hydrology's river rules are off (Earth refuses an Earth world without Hydrology's query version 3).
- **Cylinder World** (`Cworld/src/EarthPolarSystem.cs`, `EarthPolarModel.cs`, `CylinderCommands.cs`,
  `CylinderWorldCore.cs`): Harmony's `AccessTools.Property`/`Field`/`TypeByName` for the mod system's members; plain
  `GetField`/`GetProperty`/`GetMethod(name, types)` with exact types for the model. A missing member refuses the world's
  Earth polar layout with one message naming everything missing.
