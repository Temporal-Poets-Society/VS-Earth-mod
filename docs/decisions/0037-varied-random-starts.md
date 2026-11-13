# 0037: Varied random starts at the chosen climate (ROADMAP #38)

Status: Implemented in 0.17.0-pre.12; real-data offline distribution checks passed, in-game acceptance pending | Date: 2026-10-31

## Problem and measurement

The user mostly saw temperate starts around Ukraine, Kazakhstan and the Caspian, with no western European or
southern starts in their sample. The old selector samples equal areas from 60 S to 80 N and accepts the first point
passing temperature, dry/gentle lowland and an eight-of-eight land ring 256 blocks out. This gives a place's large
flat interior many more chances than a narrow coast or smaller region.

Measure before changing that policy: 500 arbitrary signed seeds and seeds 0..499 per starting climate, using real
earthdata in a controlled 512k terrain fixture. The frozen pre.11 algorithm remains in the test. All 5,000 baseline
searches succeeded; temperate arbitrary seeds gave 481 north / 19 south. Western Europe was reachable, so "never"
is not an absolute property of the old code. Cool and icy starts were entirely northern in that sample.

The full before/after region, hemisphere and latitude tables, sequential-seed comparison, 200,000-point cumulative
filter survey, model settings and limitations are in [the measurement report](../random-start-survey-0037.md).
The reference fixture holds terrain noise fixed to isolate selection and is not a replacement for server acceptance.

## Fair means regions have a chance

Choose each **qualifying major region with equal probability**, then choose an area-weighted qualifying search cell
inside it and randomize the actual point locally. This deliberately does not give every qualifying square kilometre
the same global probability: doing that recreates the continental-interior bias. It is not a population-weighted
model and does not claim equal hemispheres or countries. Both hemispheres must be reachable wherever suitable land
exists. Rare, small qualifying regions keep their chance rather than losing it through a failed rejection loop.

The disjoint, exhaustive bins are `SpawnSearch.StartRegion`: western/eastern North America (split at 100 W), South
America (the Americas below 10 N), western Europe/Mediterranean and eastern Europe (split at 15 E), central/north
Asia, East Asia, north Africa/west Asia, sub-Saharan Africa, south/southeast Asia, Australia/New Zealand and
Antarctica (below 60 S). These names describe broad sampling bins; boundaries are not country or continent maps.
Western Europe and eastern North America have separate chances, as do East Asia and southern temperate regions.
The code's ordered coordinate rules are authoritative for boundaries/overlaps.

Build a deterministic half-degree catalogue of qualifying centres from 85 S to 85 N for the world's terrain model.
If a climate has no qualifying centre in one hemisphere, refine that hemisphere at 0.1 degrees before considering
it unavailable. This finds narrow ice-free Antarctic cool coasts missed by the coarse catalogue. Cache the catalogue
weakly per terrain model; it is not a global cache across worlds or a baked set of named spawn places.

Within a chosen region, cell weights are cos(latitude) times cell size squared. The second term prevents refined
cells gaining extra weight simply because they are smaller. Try up to 48 local jittered points with all filters
checked again, including region membership. If jitter fails, use the fully validated centre. Do not retry a different
region, which would silently favour larger/easier regions. Empty catalogues retain the existing anchor fallback.

Use a full signed-seed SplitMix64 stream with a fixed domain constant instead of System.Random(seed*31+7919).
The same model, data, options and seed give the same place. The measured old first-draw correlation was small
(0.056567 lag-one for sequential seeds); random and sequential starts both showed the regional skew. Seed mixing is
a reproducibility/quality improvement, not a claim that the old RNG caused the main bias. No baseline search hit
the 20,000-attempt fallback limit, so fallback/order did not explain this measured concentration.

## Climate and safe coasts

Keep the exact bands from survival 1.22.7's own startingClimate attribute in its embedded assembly metadata:
hot 28-32 C, warm 19-23 C, temperate 6-14 C, cool -5 to 1 C, icy -15 to -10 C. They already matched the code.
Compare real annual mean surface temperature; do not broaden bands to force a desired country. In particular,
"temperate" in everyday geography does not guarantee the game's 6-14 C annual mean: some southern lowlands are
warmer, while their cooler highlands fail the lowland budget.

Retain the 60-block-above-sea maximum, ice-free/dry centre, map margin and gentle dry safety cross 12 blocks out.
Replace the all-land outer ring with at least three of eight land probes at 128 blocks and at least one at 256.
This permits substantial coastlines while rejecting isolated little patches. Also reject the explicit small-island
outline catalogue (islands under 1000 km2), even if mainland across a strait would satisfy the rings. Large islands,
including New Zealand and Tasmania, remain eligible. The existing outline and terrain data determine the result;
no client-side heuristic or country-specific spawn exception is introduced.

## Save compatibility and validation

`EarthMapSystem.ChooseSpawn` runs only on fresh parameter creation. EarthParams already saves SpawnLat/SpawnLon
and SpawnX/SpawnZ, so existing worlds keep their exact spawn and never rebuild this catalogue. No spawn-version
member or one-shot upgrade is needed. Capitals/coordinates and the existing FindDryLand search remain unchanged.

The real-data regression runs 500 arbitrary and 500 sequential seeds for **every** climate, with exact climate,
safety and determinism checks. It requires both hemispheres (at least 20 of 500 each), several regions without
collapse, all temperate target regions and explicit western-Europe/eastern-North-America geographic subchecks.
All 5,000 final searches succeed and these checks pass. Full result tables and reproduction commands are linked
above; raw CSVs/logs are in ignored tests/out/earth-playstyle.

Coordinator acceptance: actual Earth-style new worlds at multiple seeds/heights, coastal spawn surroundings,
first-world selection time, southern cool/icy starts and unchanged saved-world spawns. Those polar starts are
intentionally harsh when the player chooses a cold climate; only dry, ice-free safe terrain is promised.

## Selection on demand (5 November 2026, Earth test build 1.0.3-tall.9, installed in the author's game 21:05)

The catalogue ran the terrain check (`RandomSpawnable`, 21 column samples) on every half-degree land cell of the globe
before drawing. On Earth's full GLO-90 heights (0051) that decoded tiles everywhere: the author's new 1:10 world took
4 min 12 s to choose its start. The author: "does it need to?"

It doesn't. The catalogue now holds the land cells at each climate by region from the climate and land data only. The
terrain check runs on a cell when it is drawn, on the world's own heights, and is remembered for the model:
- A drawn cell that fails is put aside for this search, and another is drawn by area from the rest. Valid cells keep
  their relative weights, so the cell chosen is area-weighted among the valid cells, as before.
- A region whose cells all fail is put aside for this search and another region is drawn. The region chosen is uniform
  among regions with a valid cell, as before.
- A climate's hemisphere is refined at 0.1 degrees exactly when none of its half-degree cells passes, as before; the
  check is made the first time that climate is searched.
- The random stream depends only on the cells and their results, never on what was checked earlier, so the same seed
  gives the same place.

Checked offline (512,000-wide world, built-in heights): the old and new searches on 20,000 different seeds each per
climate give the same region shares, region by 5-degree latitude bands and 2-degree squares (chi-square p 0.18-0.92 over
15 tests). `tests/SpawnDistributionTests after` passes, as do `tests/EarthMapTests`' random-start checks. A seed's
place changes for new worlds; saved spawns don't. On full GLO-90 (the author's store, 4,096,000 wide), the first
search per climate took 0.0-3.2 s and read 3-170 tiles, against 235 s and 15,615 tiles.
