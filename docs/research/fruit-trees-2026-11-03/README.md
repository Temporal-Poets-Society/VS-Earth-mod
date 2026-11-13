# Wild fruit trees on Earth 1.0.2: where vanilla's rule lets them grow

Date: 3 November 2026. Evidence for [decision 0049](../../decisions/0049-flora-ranges-as-of-1318.md) and
[decision 0047](../../decisions/0047-placement-catalogue-and-audit.md).

## Report

A Mod DB comment (UncleTony, 3 November) found no olive trees around Athens and hardly any fruit trees.

## How vanilla places them (VS 1.22.7, decompiled `VSSurvivalMod`)

- `survival/worldgen/blockpatches/other.json`, "Rare fruit tree": `fruittree-branch`, chance 0.04, quantity 1 ± 1,
  forest 0.3–0.8, rain 0–1, minTemp −20.
- `BlockFruitTreeBranch.TryPlaceBlockForWorldGen`: needs fertility > 20 below, reads
  `GetClimateAt(pos, WorldGenValues)` and tries the `worldgen` conditions of `fruittreebranch.json` in random order.
  The first type whose temperature and rain bounds contain the site's values is placed.

| Type | Temperature (°C) | Rain (0–1) |
| --- | --- | --- |
| pinkapple | −5 … 18 | 0.5 … 1 |
| redapple | −9 … 18 | 0.5 … 0.9 |
| yellowapple | 0 … 22 | 0.6 … 1 |
| cherry | 0 … 24 | 0.6 … 1 |
| peach | 10 … 17 | 0.55 … 1 |
| pear | −10 … 19 | 0.5 … 1 |
| olive | 22 … 50 | 0.3 … 0.5 |
| pomegranate | 22 … 50 | 0.3 … 0.7 |
| orange | 25 … 30 | 0.45 … 1 |
| lychee | 22 … 50 | 0.6 … 1 |
| mango, breadfruit | 28 … 50 | 0.6 … 1 |

Earth does not touch this path. `EarthPlants.BuildGates` skips vanilla patches (`GateKind.Vanilla`), and no Earth
source mentions fruit trees. The climate map stores Earth's real yearly mean (`TerrainModel.Pixel`, climate rules v2:
sea-level `TMean`, cooled by the real lapse at the surface) and real rainfall (0–255 from aridity, `bake_rain.py`). So
vanilla's thresholds, tuned to vanilla's climate scale, decide on real values.

## Measured

`FruitCheck.cs` loads `EarthMap.dll` from `earth-1.0.2.zip` by reflection on the .NET 10 runtime and reads
`EarthData.SampleClimate` / `SampleTerrain` with the climate fix and fine rain on, as in a new world. Surface temperature
is `TMean − 6.5 °C/km × elevation`. Run: `dotnet build -c Release`, set the runtimeconfig framework to 10.0.0, then
`dotnet fruitcheck.dll <earth zip> <earthterraindata zip> <earthwaterdata zip>`. (The site table below came from an
earlier version of the same reader with fixed site elevations; the sweep is the current file.)

| Site | Yearly mean °C | Rain | Fruit types allowed |
| --- | ---: | ---: | --- |
| Athens | 17.1 | 0.25 | none |
| Mesogeia (Attica) | 16.6 | 0.25 | none |
| Corinth | 17.7 | 0.25 | none |
| Kalamata | 18.3 | 0.37 | none |
| Heraklion, Crete | 16.7 | 0.30 | none |
| Chania, Crete | 18.3 | 0.32 | none |
| Corfu | 18.2 | 0.47 | none |
| Thessaloniki | 16.2 | 0.33 | none |
| Larissa | 15.8 | 0.27 | none |
| Rhodes | 17.8 | 0.33 | none |
| Seville | 19.3 | 0.28 | none |
| Jaén | 17.7 | 0.31 | none |
| Tunis | 19.4 | 0.27 | none |
| Sfax | 19.6 | 0.19 | none |
| Jerusalem | 17.3 | 0.31 | none |
| Rome | 15.6 | 0.39 | none |
| Bari | 16.1 | 0.32 | none |
| Nicosia | 19.2 | 0.24 | none |
| Marrakesh | 19.9 | 0.21 | none |
| Izmir | 18.3 | 0.32 | none |

Land-masked sweeps (ocean share ≤ 0.5):

| Area | Land cells | Any fruit type | Olive | Pomegranate |
| --- | ---: | ---: | ---: | ---: |
| Greece, 34.8–41.8° N 19.3–28.3° E, 0.05° | 10,452 | 2,454 (apples, pear, cherry, peach) | 0 | 0 |
| Mediterranean basin, 30–46° N 10° W–40° E, 0.1° | 45,217 | 10,442 | 1 | 1 |
| World 50° S–55° N, 0.25° | 170,677 | 81,835 | 11,771 | 20,506 |

Olive-eligible land by 10° × 20° box: 20–10° S 20–40° E (Zambia, Angola, Mozambique) 1,003; 20–10° S 120–140° E
(northern Australia) 989; 10° S–0 20–40° E 825; 0–10° N 20–40° E 806; 20–30° N 60–80° E (Thar, Indus) 716;
0–10° N 0–20° E 573; 20–10° S 60–40° W (Brazil) 525; 10–20° N 60–80° E (Deccan) 478; the Sahel 459 + 436.

Not measured: the patch's forest requirement (0.3–0.8) and fertility, which remove more of dry Attica.

## Other vanilla paths with the same exposure

Vanilla worldgen patches Earth does not gate (file: patches with `blockCodes`): aquatic-coral 5, aquatic 23,
berrybush 10, crop 16, fern 7, flower 37, grass 10, lorecontent 2, mushroom 45, other 23, stonesshells 10. Some are
not organisms (stones, shells, meteorite, lore). The vanilla American desert plants (saguaro, barrel and silver torch
cactus, agave/yucca) are the one exception: decision 0032 limits them to the Americas.

Vanilla wild crops: amaranth, carrot, cassava, fennel, flax, licorice, onion, parsnip, peanut, pineapple, rice, rye,
soybean, spelt, sunflower, turnip. Vanilla berry bushes: black, red and white currant, blueberry, cranberry,
strawberry, beautyberry, cloudberry, blackberry, raspberry.

## Follow-up

Fixed by the first slice of [decision 0047](../../decisions/0047-placement-catalogue-and-audit.md#implementation-first-slice):
the fruit-tree type, vanilla wild crops and berry bushes now follow their 1318 range and Earth climate envelopes in new
worlds created with **Plants and animals: As around 1318**. The server results are under its Verification heading.
The sand that still kept fruit trees off the ground around Athens is fixed worldwide by
[decision 0048](../../decisions/0048-soil-on-semi-arid-land.md): the next server run grew olive, pomegranate and peach
trees there.
