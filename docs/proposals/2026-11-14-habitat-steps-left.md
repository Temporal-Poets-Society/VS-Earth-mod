# Habitat steps left: options for the author (14 November 2026)

Status: **proposal only** (EA-0054). Nothing here is built or decided. It gathers the open steps of the habitat proposal
([habitats README](../research/habitats-2026-11-06/README.md), [site drivers](../research/habitats-2026-11-06/site-drivers.md))
and the decisions they wait for, with a recommendation each, now that more of the ground under them is on `main`.

## What is on `main` already

- Climate zones set forest and shrub density, and the tree mix follows the zone (habitats steps 1-2, `ZoneRules`).
- Ground columns are the default: WRB soil group, texture, pH, cover recipe (till, loess, peat, alluvium) and its depth
  per column (0056). Richness and permeability can be read from them everywhere, not only in the Latvia pilot.
- A water table for every land column (0059), raised by the mapped wetlands; GLWD v2 wetlands (`wetlands.bin`) and
  ponds only in them (0062); open wetlands and ground moisture in the Biomes choice (0075).
- Animals already follow forest and open land through vanilla's density gates, which read the zones' densities.

So of the six decisions the 6 November proposal asked for, two are in effect settled by what was built: groundwater is a
query (0059; the seep rule in Hydrology is still open), and the ground-column split of section 3a is how the code works.

## The steps left

| Step | Needs | Size |
| --- | --- | --- |
| 1. Site drivers per column: wetness × richness (and light from slope and aspect); `/earth site` | nothing new: water table + ground columns; the first slice (`SiteDrivers.cs`, `SiteDriversTests`) is in tag `archive/backup/wip-2026-11-12-habitat-overlay`, written before both existed | small; no world change |
| 2. Species by site: soft wetness and richness weights per tree and plant; tree size from productivity | step 1; catalogue values (EIVE for Europe) | medium; new rules version |
| 3. Wetland surfaces: bog, fen and swamp-forest top blocks, hummocks and pools | step 1; decision B (blocks); Hydrology's seep rule for digging | medium-large |
| 4. Ground layer: moss, litter and bog-plant cover; herbs by habitat | decision B; plants from providers | medium |
| 5. Land use of 1318: fields, meadows, pasture and heath around settlements | decision C; a 1300 land-use grid | medium |
| 6. Fauna by habitat beyond forest and open (wetland, rocky, riverine) | steps 3-4 | small, after them |

## Decisions for the author

**A. Calibration.** (1) Rules from the drivers, with potential-vegetation maps (Hengl et al. 2018, RESOLVE 2017, Bohn's
European map) only as offline test oracles; or (2) a PNV map baked in and read directly.
*Recommended: (1).* The world stays independent of one map's errors and licence; nothing new ships in the zip (it is
76 kB under the 40 MB limit), and the maps catch formula mistakes such as the steppe one.

**B. New ground blocks.** (1) In Earth: a small set of ground blocks (sphagnum carpet with peat under it, moss carpet,
needle litter, leaf litter, fen turf, black muck), since Earth owns rocks, ground and soil and already ships its own
blocks (kyanite, sillimanite, andalusite ores, 0077); plants (cotton grass, cloudberry, Labrador tea, sedges) from content
providers through the 1318 catalogue. (2) Everything in a separate content mod that Earth reads through the catalogue.
(3) Wait for Floral Zones to supply them (ask its author for the Eurasian ground layer and a sphagnum block).
*Recommended: (1) for the ground blocks, providers for the plants.* Ground is Earth's by the ownership rule; plant
content stays with providers, as the catalogue decision (0049) has it. Small textures fit the zip's headroom, but it is
tight: check the size per block added.

**C. Land use of 1318.** People vanished around 1318 and the game is later, at an unknown time (the ruins timeline). So
the fields, meadows and pastures of 1318 would show as abandoned land going back to scrub and forest. (1) Earth draws
it as land cover: from a 1300 cropland and pasture grid (HYDE 3.x or KK10), lower forest density, meadow grass and
pioneer trees (birch, pine, aspen) by how long the land has lain fallow; Earth Ruins later adds field walls, terraces
and other built traces. (2) All of it waits for Earth Ruins and its villages (paused; the author plans to remake ruins).
*Recommended: (1)*, with the fallow time as one number the author picks (decades: open meadow with young birch;
a century or more: mostly young forest). It is vegetation by place, which is Earth's, and it does not wait on the ruins
redesign.

**D. Data to fetch and licence-check** (none fetched): EIVE 2023 (catalogue values, step 2), HYDE 3.x or KK10 (step 5),
PEATMAP (bogs where GLWD has none, step 3), Fan et al. 2013 water-table depth (an oracle for 0059's table), a canopy-height
map such as Lang et al. 2023 (tree size, step 2), and the PNV maps of A. Each is fetched only with the author's yes, and
anything baked into the zip or a data release is credited in `ATTRIBUTION`.
*Recommended: EIVE and HYDE first* (the two that unblock steps 2 and 5); the rest as oracles when a step needs them.

**E. Order.** *Recommended:* step 1 now (diagnostic only, no world change), then 2, then 5 (decision C), then 3 and 4
together (decision B), then 6. Each step that changes generation goes behind its own rules version for new worlds (0002).

## Not changed by this proposal

Caves by rock type (decided 6 November) and the zones (built). The seep rule for digging below the water table stays
Hydrology's to build once wetland surfaces exist.
