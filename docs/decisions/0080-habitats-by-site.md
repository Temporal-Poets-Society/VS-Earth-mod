# 0080: Habitats by site: rules from site drivers, ground blocks in Earth, the land use of 1318 as abandoned land

Status: **agreed** (the author, 14 November 2026: "Yes" to the recommendations of
[the habitat steps proposal](../proposals/2026-11-14-habitat-steps-left.md), EA-0054). Steps 1 and 2 built, in Earth 2.0.0 (site rules 1, new worlds; see below). Step 5 (the
land use of 1318) is prepared on branch `claude/landuse-1318` and waits for the HYDE files, after the 2.0 release set
(the author, 14 November: "this can wait"); steps 3-4 and 6 also after 2.0. Date: 2026-11-14.

## Decided

- **A. Calibration: rules from the site drivers.** Habitats come from rules on each column's site drivers (wetness,
  richness, light, heat, water supply, disturbance). Potential-vegetation maps (Hengl et al. 2018, RESOLVE 2017, Bohn's
  European map) are only offline test oracles; none is baked in or read by the game.
- **B. Ground blocks in Earth, plants from providers.** Earth adds a small set of ground blocks: sphagnum carpet with
  peat under it, moss carpet, needle litter, leaf litter, fen turf and black muck (Earth owns rocks, ground and soil).
  Plants (cotton grass, cloudberry, Labrador tea, sedges and the like) come from content providers through the 1318
  catalogue ([0049](0049-flora-ranges-as-of-1318.md)).
- **C. The land use of 1318: Earth draws it as abandoned land.** People vanished around 1318 and the game is later, so the
  fields, meadows and pastures of 1318 show as land going back to scrub and forest: from a 1300 cropland and pasture grid,
  lower forest density, meadow grass and pioneer trees (birch, pine, aspen) by how long the land has lain fallow. The
  fallow time is one world setting on the Earth tab, with a default justified where it is built, for the author to change.
  Field walls, terraces and other built traces stay Earth Ruins' (later).
- **D. Data.** Fetch EIVE 2023 (European ecological indicator values, for step 2) and HYDE 3.x (cropland and pasture
  around 1300, for step 5) from their official sources, licence-checked first, each into its own new folder as data, and
  credited in `ATTRIBUTION`. The rest (KK10, PEATMAP, Fan et al. 2013, a canopy-height map, the PNV maps) only as
  oracles when a step needs them, each with the author's yes.
- **E. Order.** 1 site drivers, 2 species by site, 5 land use of 1318, 3 and 4 wetland surfaces and the ground layer
  together, 6 fauna by habitat. Each step that changes generation goes behind its own rules version for new worlds
  ([0002](0002-save-compatibility.md)), with an in-game look.

## Step 1: site drivers (built 14 November 2026)

`src/Core/SiteDrivers.cs` computes, per column and without game types: heat (growing degree days above 5 °C from the
climate's mean, coldest and warmest month, lapse applied), clear-sky light on the slope and aspect (FAO-56, a 5 × 5
least-squares plane of real heights about 100 m apart), water supply (an aridity index read back from the rain value),
soil texture, richness A-D and permeability (the habitat prototype's Baltic rules, widened to every WRB group of the
ground columns by base status), wetness 0-7 from the water table's depth and the height above its drainage base
([0059](0059-water-table-and-hod-aquifers.md)), and disturbance (distance to the sea, height above the nearest river, a fire index). The
wetness and richness classes name a site type (the forest-site grid of the Baltic and Fennoscandian classifications:
lichen heath to rich swamp; open land or forest is still the climate zone's). `/earth site` prints them;
`tests/SiteDriversTests` checks the light against FAO-56, the heat sums, the soil and wetness rules and the site types.
Nothing in world generation reads them yet: no rules version.

Limits (review of 14 November): the climate caps wetness only where the table lies 2 m and deeper, so a desert
basin whose modelled table is shallow (an oasis, a playa's edge) reads moist to wet, as the water table puts its water
there (it lowers the table in dry climates); a Gleysol or Histosol keeps its floor in any climate. Humid ground stays
"fresh" however deep the table (a karst plateau with the table 50 m down), the prototype's rule: the roots live on
rain there. Aridity is the climate's own (`ClimateSample.Aridity`, as soil by aridity, 0048, reads it); the light table
(`SolarTable`) is only used by `/earth site` so far.

The first slice was written on 12 November before the water table and the ground columns' WRB names existed (kept in
`archive/backup/wip-2026-11-12-habitat-overlay`); its Latvia habitat map is not carried over.

## Step 2: species by site (built 14 November 2026, site rules 1)

New worlds save `SiteRules` 1 (EarthParams field 204; 0 in every world made before and with vanilla biomes). Trees and
shrubs then read each column's own site instead of the 2′ soil grid's shares:

- `EarthWaterTable.SiteGrid` gives a chunk a 4 × 4 grid of sites (8 blocks apart) from one drainage-base field: the
  water table's depth, the height above its drainage base, the ground column's soil and the climate's aridity, turned
  into wetness and richness by step 1's rules. The tree pick takes the cell its column lies in.
- `SiteNiche` puts the site on EIVE's scales, calibrated on EIVE's own values: moisture by wetness class (very dry 2.5,
  dry 3.5, fresh 4.6, moist 5.6, wet 6.5, waterlogged 7.2, flooded 8.0; beech and oak stand at 4.6-4.8, downy birch 6.3,
  black alder 7.2, the bog plants 6.9-7.1), shifted by the climate where the table is out of reach (2 units per e-fold
  of the aridity index, -2.5 to +1); nutrients by richness (A 2.0, B 3.5, C 5.0, D 6.5; heather 1.3, pine 2.7, beech 4.9,
  ash 7.0); reaction from the ground column's pH as before. `SoilNiche.Fit` uses these single values (one place, no
  shares). The fit sorts the species and keeps the forest's density, as before; it applies where EIVE describes the
  flora (Europe).
- Wet and waterlogged columns (the table within 1 m) count as wet ground for the floodplain trees (alder, willows), so
  alder carr stands in mires away from rivers.
- A tree's size is multiplied by the site's productivity, everywhere: full on fresh to moist fertile ground, 0.88 on
  dry and 0.75 on very dry ground, 0.9 on wet, 0.65 on waterlogged and 0.55 on flooded ground, times 0.8 on poor (A) and
  0.9 on moderately poor (B) soil: stunted pines on a raised bog, small pines on dry heath. Trees keep at least half their size
  (smaller sizes risk stumps in some generators); shrubs keep theirs (review of 14 November).

Checked (14 November): `tests/RegionalTreesTests` on four column sites in central Germany (raised bog: Scots pine
51 %, birches 19 %, broadleaves 3 %; alder carr: black alder 23 %, none on fresh or bog ground; dry heath: pine 34 %,
birch 31 %; fresh rich ground: maple, beech, oak and lime), and every site keeps the forest's density. In game (1:100,
GLO-90, vanilla trees only), the same chunks regenerated with site rules 1 and 0 (`/earth siterules`, `/wgen regenk`;
regeneration is repeatable), picks counted with `/earth woods trace`: as many trees either way (Thuringia 518 and 513,
Kemeri 177 and 175), the mix moved by a few to 15 per cent (Kemeri birch 45 against 39, maple 12 against 18; Thuringia
maple 221 against 190, oak 211 against 231), Kemeri's pines 15 % shorter on waterlogged ground. Kemeri reads as fen on
lake clay rather than raised bog because the ground columns give Luvisols on lake clay there; its peat waits for
step 3's bog data.

Cost (review of 14 November, measured with `/earth woods trace` on the bench): a chunk's site grid takes 0.65-0.78 ms
on the vegetation thread (Kemeri, Thuringia; 34 and 95 grids). `/earth siterules` is a comparison tool: chunks made
while it differs from the save keep those rules for good, so it logs a warning and says so; the saved rules return when
the world is reopened.

Not in this step: ground plants (berries, herbs) by site come with the ground layer (step 4); the bog surfaces and
their thinning of trees with step 3.
