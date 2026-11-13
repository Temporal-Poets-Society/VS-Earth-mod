# Surface soil on Earth's real climate (3 November 2026)

Triggered by the olive follow-up of [the fruit-tree research](../fruit-trees-2026-11-03/README.md): around Athens, 32
of 34 fruit-tree attempts fell on sand (fertility 10), and vanilla's fruit-tree rule needs ground of fertility above
20. The author asked to treat this as a worldwide soil problem, not a Greek one. Decision:
[0048](../../decisions/0048-soil-on-semi-arid-land.md).

## Where the sand comes from

Vanilla's block layers (`assets/survival/worldgen/blocklayers.json`, `GenBlockLayers.LoadBlockLayers`, VS 1.22.7) take
each column's fertility from the climate map: `Climate.GetFertility(rain, temperature, height)`, about
`rain / 2 + rain x (temperature + 20) x 4.25 / 512` on the 0–255 scale, less above half the height between sea level
and the world top. Below 0.25 the top layer is **L1 Infertile Soil**: sand from 8 °C, gravel down to −15 °C. Grassed
soil needs 0.25 or more.

Earth stores the real rainfall: a log curve of the aridity index (precipitation / potential evapotranspiration,
`tools/bake_rain.py`: index 0.05 → 15, 0.2 → 50, 0.5 → 95, 0.75 → 115), cut by up to a quarter where summers are dry.
Vanilla's formula was tuned to vanilla's own climate maps, not to this scale: at 18 °C it needs rain 78 (an index of
about 0.45 with summer rain, more with dry summers) for soil. Athens (index 0.38, rain 64, 17 °C) gets fertility 0.20,
so sand.

## Survey

`SoilCheck.cs` (run: `dotnet run -c Release -- <worktree> 0.25`) classifies every land cell between 56° S and 72° N on
a 0.25° grid (area-weighted) by a simplified Köppen class and reports the top block vanilla's layers pick from Earth's
climate (temperature lapsed to the cell's elevation, default 110/log1000 vertical scale, 512-high world). It does not
see Earth's later cover (dunes, beaches, dry rivers, mountain surfaces).

| Climate class | land share | vanilla: soil | sand | gravel | with 0048: soil | sand | gravel |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| A tropical | 23.0% | 97% | 3% | 0% | 100% | 0% | 0% |
| BSh hot steppe | 5.9% | 47% | 53% | 0% | 95% | 5% | 0% |
| BSk cold steppe | 4.2% | 26% | 49% | 24% | 95% | 2% | 3% |
| BWh hot desert | 14.9% | 0% | 100% | 0% | 13% | 87% | 0% |
| BWk cold desert | 3.8% | 2% | 79% | 19% | 35% | 52% | 13% |
| C temperate | 15.2% | 99% | 1% | 0% | 100% | 0% | 0% |
| Cs mediterranean | 1.9% | 58% | 42% | 0% | 100% | 0% | 0% |
| D continental | 25.5% | 92% | 0% | 7% | 97% | 0% | 2% |
| E polar/tundra | 5.7% | 76% | 0% | 9% | 83% | 0% | 8% |

| Precipitation (mm) | land share | vanilla: soil | sand | gravel | with 0048: soil | sand | gravel |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| below 100 | 9.6% | 2% | 90% | 6% | 6% | 90% | 4% |
| 100–250 | 14.8% | 17% | 60% | 19% | 52% | 38% | 8% |
| 250–400 | 15.1% | 62% | 32% | 4% | 92% | 7% | 1% |
| 400–600 | 17.7% | 90% | 9% | 0% | 99% | 1% | 0% |
| 600–1000 | 16.5% | 95% | 5% | 0% | 99% | 0% | 0% |
| 1000+ | 26.4% | 100% | 0% | 0% | 100% | 0% | 0% |

Remaining shares are snow, ice and other layers.

- Vanilla's rule lays sand on **24.7%** of this land, more than the **18.7%** that is desert (BW) at all, and sand or
  gravel on 14% of land outside the deserts: 42% of Mediterranean land, half the hot steppes, three quarters of the
  cold steppes, and the dry savannas.
- With the floor, sand covers 15.3% of the land, nearly all of it desert; soil covers 97% of the land outside the
  deserts.
- The desert cells that change sit at the desert margin (index 0.2–0.3): Western Australia's mulga country, Utah,
  Mangyshlak, the Kopet Dag foothills, inland Syria. Köppen's arid line and UNEP's index of 0.2 differ there; real soils
  are red earths and calcisols, and mapped dunes keep their sand through Earth's geology (GUM aeolian sediment).

Real reference points (World Reference Base, FAO Harmonized World Soil Database): Arenosols, the sandy soils, cover
about 7% of the land and lie mostly in the Sahara, Kalahari, Arabia and Australia's dune fields. Mediterranean land
has Luvisols, Cambisols, Calcisols and terra rossa; the steppes have Chernozems and Kastanozems, among the most fertile
soils on Earth; dry savannas have Lixisols, Luvisols and, in the Sahel, Arenosols on old dunes.

## Places (tests/SoilTests)

Aridity index from Earth's rainfall, vanilla fertility at sea level, surface temperature:

| Place | index | vanilla fertility | °C | | Place | index | vanilla fertility | °C |
| --- | ---: | ---: | ---: | --- | --- | ---: | ---: | ---: |
| Athens | 0.38 | 0.20 | 17.2 | | Kazakh steppe | 0.40 | 0.23 | 3.7 |
| Seville | 0.49 | 0.23 | 19.3 | | Ulaanbaatar | 0.39 | 0.22 | 0.5 |
| Tunis | 0.44 | 0.22 | 19.3 | | Mendoza | 0.25 | 0.19 | 17.9 |
| Madrid | 0.47 | 0.22 | 15.3 | | Murcia | 0.25 | 0.18 | 18.0 |
| Los Angeles | 0.41 | 0.21 | 19.6 | | Bamako | 0.56 | 0.27 | 28.1 |
| Simpson Desert | 0.15 | 0.14 | 23.3 | | Western Desert | 0.00 | 0.00 | 22.5 |

## Native server

VS 1.22.7, Earth branch `claude/placement-catalogue` with soil rules v1, Cylinder World 1.0.4, Hydrology 0.6.2, world
256,000 x 512 x 102,400, self-test radius 5 chunks (census within 192 blocks), 3 November 2026:

| Place | Surface census (columns) | Fruit trees |
| --- | --- | --- |
| Athens | soil-low 87,517; muddy gravel 35,159 (sea floor); sand 858; gravel 67 | peach 4, olive 3, pomegranate 3, yellow apple 2, cherry 1 |
| Murcia | soil-low 123,036; gravel 534; sand 101 | pomegranate 4, cherry 2, olive 2, peach 2, pink apple 2 |
| Kazakh steppe (50.3 N 68 E) | soil-low 119,359; gravel 3,614 | peach 7, pink apple 7, yellow apple 2, red apple 1 |
| Amarillo | soil-low 96,196; forest floor 13,536 | none (no catalogue fruit native) |
| Bamako | soil-low 99,120; soil-medium 11,418; sand 2,230 | none |
| Adelaide | soil-low 54,855; muddy gravel 44,848 (sea floor) | none |
| Western Desert (25 N 28 E) | sand 123,865 (99%) | none |
| Los Angeles | soil-low 105,704; gravel 1,029; sand 416 | none (no vanilla fruit native in 1318) |

The earlier run without the floor (decision 0047) counted around Athens about 107,000 sand columns, 722 soil and no
fruit tree. The server log reports "Earth soil rules v1" and no errors.

Not checked: tall grass density and grass cover on the new soil in a client, crops on tilled semi-arid soil, and
worlds made before soil rules (they keep vanilla's sand; see 0048).
