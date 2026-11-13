# 0033: Coral worldgen requires warm marine water

Status: source implemented; offline policy checked; game-assembly hook validation pending. Date: 2026-10-30.
Issue #24, Hammar Lake and freshwater habitats worldwide.

## Cause and decision

Vanilla [BlockCoral source](https://raw.githubusercontent.com/anegostudios/vssurvivalmod/master/Block/BlockCoral.cs)
owns reef placement. `TryPlaceBlockForWorldGenUnderwater` grows a multi-column shape, tests whether a block is
liquid without checking salinity, and can replace aquatic plants with saltwater before `PlaceCoral` runs.
Earth's warm lake water therefore qualifies. This is not fixed by changing a patch's seed-position test alone.

`EarthCorals` guards the initial attempt and each actual target read before those replacements; disallowed targets
look dry to the coral search. Placement and sea-plant hooks also veto invalid targets, with a one-column marine
halo for lateral decorations. Existing lakes/rivers are not converted to saltwater. A per-attempt thread-local
cache avoids recomputing terrain throughout each vertical search. Hook failures log an explicit warning.

`MarineEcology` requires Earth's **WaterSea**, excludes rivers/gutters, and requires mean climate >=18 C and
warmest month >=20 C. This is a warm-climate proxy, not sea-surface-temperature or reef-distribution data;
vanilla patch depth/substrate/temperature rules still run. Salt lakes are excluded irrespective of salinity.
The guard is independent of the regional-tree/plant-pack switches and is worldgen-only.

## Evidence

`tests/out/playtest-issues/review/coral-habitat.csv` checks real terrain and a water-kind matrix:

| Habitat | Old liquid test | New gate |
|---|---|---|
| Hammar basin, 30.75 N 47.1 E | eligible | rejected |
| Warm fresh lake/river water (kind 2) | eligible | rejected |
| Warm salt lake (kind 3) | eligible | rejected |
| Red Sea, 26.8 N 34.5 E | eligible | retained |
| Gulf, 26.5 N 51.5 E | eligible | retained |
| Great Barrier Reef, 18.325 S 147.35 E | eligible | retained |
| Norwegian sea, 61.5 N 3 E | liquid | rejected as cold |

The approximate reported Hammar coordinate is dry in this offline seed, so the table also samples inside the
basin. Natural Earth labels Hammar an alkaline lake: the existing bake consequently uses kind 3 there. That
classification is not evidence of marine habitat; this guard rejects both freshwater and inland saltwater.
Historical wetland salinity and exact seasonal shorelines are not reconstructed by this change.

The screenshot was read; no game was launched. These are eligibility results, not measured coral counts in generated
chunks. The coordinator must validate Harmony binding against installed game assemblies, including target reads,
cross-shore scatter and aquatic-plant replacement. Existing generated coral is not removed; 0002 species-rule fixes
apply to future worldgen only. Files: `src/EarthCorals.cs`, `src/Core/MarineEcology.cs`, `src/EarthWorldGen.cs`.
