# Random start survey: ROADMAP #38

Measured offline on 2026-10-31. Each algorithm ran 500 arbitrary signed seeds and seeds 0..499 for each of the five starting climates: 5,000 searches before and 5,000 after. All searches succeeded. The arbitrary seed list is reproducible (`new Random(380036)` in the test). Tables below use the 500 arbitrary seeds per climate unless marked sequential.

The reference fixture uses real repository earthdata at width 512000, length 204800, ring 409600, height 512, sea 110, log1000, fixed terrain noise seed 12345, current fine climate/rain, summit/glacier and island/lake overlays, and rivers. Holding the terrain model fixed isolates the spawn selector. Its river width/straightening/smoothing values are 1 / 0.65 / 0.5; it does not build story pads, sea exit channels, large-coast refinement or companion terrain expansions. These are controlled offline distribution measurements, not a full server-world comparison. The coordinator should check representative actual new worlds.

The pre.11 selector is preserved in `tests/SpawnDistributionTests/LegacySpawnSearch.cs`; only observation counters were added. Final major-region labels are applied to both sets of coordinates for comparison. Regions are explicit geographic sampling bins, not exact political borders or a population dataset.

## Hemisphere

| Climate | Before north | Before south | After north | After south |
|---|---:|---:|---:|---:|
| hot | 485 | 15 | 399 | 101 |
| warm | 232 | 268 | 384 | 116 |
| temperate | 481 | 19 | 401 | 99 |
| cool | 500 | 0 | 442 | 58 |
| icy | 500 | 0 | 444 | 56 |

## Before: major regions

| Region | Hot | Warm | Temperate | Cool | Icy |
|---|---:|---:|---:|---:|---:|
| Antarctica | 0 | 0 | 0 | 0 | 0 |
| Australia / New Zealand | 8 | 194 | 5 | 0 | 0 |
| Central / North Asia | 0 | 4 | 109 | 262 | 67 |
| East Asia | 0 | 2 | 29 | 63 | 262 |
| Eastern Europe | 0 | 19 | 165 | 26 | 0 |
| Eastern North America | 3 | 33 | 111 | 80 | 87 |
| North Africa / West Asia | 435 | 151 | 0 | 0 | 0 |
| South / Southeast Asia | 27 | 17 | 0 | 0 | 0 |
| South America | 5 | 69 | 14 | 0 | 0 |
| Sub-Saharan Africa | 22 | 5 | 0 | 0 | 0 |
| Western Europe / Mediterranean | 0 | 0 | 67 | 0 | 2 |
| Western North America | 0 | 6 | 0 | 69 | 82 |

## After: major regions

| Region | Hot | Warm | Temperate | Cool | Icy |
|---|---:|---:|---:|---:|---:|
| Antarctica | 0 | 0 | 0 | 58 | 56 |
| Australia / New Zealand | 56 | 38 | 48 | 0 | 0 |
| Central / North Asia | 0 | 32 | 58 | 83 | 84 |
| East Asia | 0 | 51 | 65 | 56 | 63 |
| Eastern Europe | 0 | 49 | 76 | 84 | 58 |
| Eastern North America | 73 | 56 | 70 | 63 | 83 |
| North Africa / West Asia | 83 | 39 | 0 | 0 | 0 |
| South / Southeast Asia | 84 | 62 | 0 | 0 | 0 |
| South America | 63 | 44 | 51 | 0 | 0 |
| Sub-Saharan Africa | 83 | 34 | 0 | 0 | 0 |
| Western Europe / Mediterranean | 0 | 45 | 52 | 73 | 73 |
| Western North America | 58 | 50 | 80 | 83 | 83 |

## Before: latitude bands

| Latitude | Hot | Warm | Temperate | Cool | Icy |
|---|---:|---:|---:|---:|---:|
| -90 to -75 | 0 | 0 | 0 | 0 | 0 |
| -75 to -60 | 0 | 0 | 0 | 0 | 0 |
| -60 to -45 | 0 | 0 | 9 | 0 | 0 |
| -45 to -30 | 0 | 39 | 10 | 0 | 0 |
| -30 to -15 | 8 | 226 | 0 | 0 | 0 |
| -15 to 0 | 7 | 3 | 0 | 0 | 0 |
| 0 to 15 | 243 | 0 | 0 | 0 | 0 |
| 15 to 30 | 242 | 123 | 0 | 0 | 0 |
| 30 to 45 | 0 | 109 | 178 | 0 | 0 |
| 45 to 60 | 0 | 0 | 303 | 252 | 0 |
| 60 to 75 | 0 | 0 | 0 | 248 | 494 |
| 75 to 90 | 0 | 0 | 0 | 0 | 6 |

## After: latitude bands

| Latitude | Hot | Warm | Temperate | Cool | Icy |
|---|---:|---:|---:|---:|---:|
| -90 to -75 | 0 | 0 | 0 | 0 | 0 |
| -75 to -60 | 0 | 0 | 0 | 58 | 56 |
| -60 to -45 | 0 | 0 | 35 | 0 | 0 |
| -45 to -30 | 0 | 12 | 64 | 0 | 0 |
| -30 to -15 | 56 | 103 | 0 | 0 | 0 |
| -15 to 0 | 45 | 1 | 0 | 0 | 0 |
| 0 to 15 | 267 | 0 | 0 | 0 | 0 |
| 15 to 30 | 132 | 190 | 0 | 0 | 0 |
| 30 to 45 | 0 | 194 | 198 | 0 | 0 |
| 45 to 60 | 0 | 0 | 203 | 174 | 0 |
| 60 to 75 | 0 | 0 | 0 | 195 | 324 |
| 75 to 90 | 0 | 0 | 0 | 73 | 120 |

## Sequential seeds 0..499

| Climate | Before north | Before south | After north | After south |
|---|---:|---:|---:|---:|
| hot | 475 | 25 | 397 | 103 |
| warm | 236 | 264 | 384 | 116 |
| temperate | 471 | 29 | 396 | 104 |
| cool | 500 | 0 | 422 | 78 |
| icy | 500 | 0 | 442 | 58 |

## Temperate targets

| Geographic check | Before arbitrary | After arbitrary | Before sequential | After sequential |
|---|---:|---:|---:|---:|
| Western Europe | 57 | 49 | 54 | 59 |
| Eastern North America | 95 | 61 | 95 | 59 |
| Southern hemisphere | 19 | 99 | 29 | 104 |
| East Asia | 29 | 65 | 40 | 62 |

## Which filters matter?

A separate 200,000-point equal-area survey follows the old 60 S to 80 N domain and applies each filter cumulatively. `Climate` includes ocean candidates, exactly as the original loop does; `Lowland` additionally requires dry terrain within 60 blocks above sea. `Gentle` also requires ice-free ground and the dry 12-block safety cross. `Old interior` requires all eight probes at 256 blocks to be land. `New coast` applies the final coast/island rule. Counts are candidate-pool sizes, not starts.

| Temperate region | Area draws | Climate | Lowland | Gentle | Old interior | New coast |
|---|---:|---:|---:|---:|---:|---:|
| Australia / New Zealand | 13817 | 3297 | 91 | 49 | 35 | 49 |
| Central / North Asia | 6765 | 1539 | 574 | 554 | 488 | 554 |
| East Asia | 13514 | 2302 | 264 | 178 | 142 | 178 |
| Eastern Europe | 3677 | 1543 | 906 | 826 | 712 | 823 |
| Eastern North America | 17087 | 1953 | 674 | 615 | 475 | 614 |
| North Africa / West Asia | 12191 | 96 | 0 | 0 | 0 | 0 |
| South / Southeast Asia | 34598 | 2087 | 0 | 0 | 0 | 0 |
| South America | 46526 | 7828 | 147 | 96 | 70 | 96 |
| Sub-Saharan Africa | 26838 | 2558 | 0 | 0 | 0 | 0 |
| Western Europe / Mediterranean | 5533 | 2575 | 515 | 402 | 296 | 399 |
| Western North America | 19454 | 4069 | 91 | 28 | 16 | 28 |

The skew comes from weighting by the area of land that survives all filters: large flat continental interiors dominate, and the all-land outer ring removes coastal sites. The 6-14 C band is not a complete explanation: qualifying western European, South American and Australian/New Zealand candidates remain. The lowland/slope filters remove many more southern candidates; relaxed coasts alone cannot give small remaining regions a fair share. South African coastal annual means generally do not meet the game temperate band; the cooler highlands fail the lowland requirement in this fixture. The bands therefore stay unchanged.

The old filter survey leaves 2,234 temperate candidates: 1,200 (53.7%) in eastern Europe/central-north Asia and 105 (4.7%) in South America/Australia-New Zealand. The old 500-seed starts closely follow that pool: 274 (54.8%) in those northern interiors and 19 (3.8%) south. Merely replacing the coast rule leaves the northern pair with 1,377 of 2,741 candidates (50.2%) and the southern pair with 145 (5.3%). That small improvement is why regional weighting is needed as well. The final sampler gives the northern pair 134 starts (26.8%) and the southern pair 99 (19.8%) in this seed sample.

The game definitions were checked in Survival Mode 1.22.7 assembly metadata: hot 28-32 C, warm 19-23 C, temperate 6-14 C, cool -5 to 1 C, icy -15 to -10 C. Real annual mean temperature is used. No broader band is substituted to manufacture a desired place.

Sequential seeds show the same northern temperate skew. The old first latitude RNG draws have lag-one correlation 0.056567 over seeds 0..499, so this measurement does not support seed correlation as the main cause. There were no 20,000-attempt failures or anchor fallbacks in 5,000 old searches. First-success rejection sampling weights the surviving geographic pool; changing filter order does not make small pools competitive. SplitMix64 is nevertheless used for a stable, thoroughly mixed seed stream.

A separate southern cool scan at 0.1 degrees found 1,546 climate-matching land points, 152 lowland points, 43 ice-free points, and four gentle points. All four failed the initial three-of-eight outer-ring rule. Two mainland/coastal sites pass the final two-ring land checks; the coarse catalogue needed refinement to find them. The final selector can reach both hemispheres for every climate, including ice-free Antarctic cool/icy sites.

## Reproduce and checks

Run `powershell -File tools/run-earth-playstyle-tests.ps1 -MeasureBaseline -FilterSurvey`. The ordinary run omits the lengthy baseline/survey and checks options plus the final 5,000-search distribution. `probe` mode in the spawn executable reproduces the southern cool scan. All output is under `tests/out/earth-playstyle/`.

The regression requires both hemispheres for all five climates and both seed lists, multiple reasonably represented regions, all temperate target regions, western Europe/eastern North America geographic subchecks, exact climate bounds, safe selected sites and deterministic repeated seeds. It uses broad distribution bounds rather than fragile exact-coordinate goldens. Existing saves never call this selector again. See decision 0037.

Raw outputs: `spawn-before.csv`, `spawn-after.csv`, their summary/filter CSVs, `spawn-filter-survey.csv`, `southern-cool-probe.csv` and runner logs. No client or server was launched for this measurement.
