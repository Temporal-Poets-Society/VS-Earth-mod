# 0062: Vanilla's surface ponds only in the mapped wetlands

Status: **built** at the author's choice (test build 1.0.3-tall.23), in Earth 2.0.0; since tall.25 a choice on the Earth tab,
**Surface ponds**, off by default (the author, 8 November: "have we removed vanilla ponds? (in Earth setting UI make
default off)"); checked offline and on a private bench, not installed. Date: 2026-11-08.

## What the author asked

The author, 8 November 2026: "Also vanilla ponds are still generated?" Offered "Wetlands only" or "Nowhere", the author
chose: "Wetlands only - but that would according new biome system?" Yes: the ponds follow the biome system's wetland
map (GLWD v2, `assets/earth/config/wetlands.bin`, the grid that opens bog and marsh and raises the water table).

## What vanilla does (1.22.7, `GenPonds`)

In the TerrainFeatures pass, each chunk makes up to about 38 surface tries in wet, warm climates (fewer with less rain
or cold). A try starts a block above the rain height and fills the closed hollow there, up to about 90 blocks across;
half the time it adds a layer above and lays lake-bed blocks under each new ring. Then it makes 600 tries in caves.
Earth's guards already kept ponds off ice and permanent snow, out of rivers, off reserved sites and out of craters.

On Earth's terrain the closed hollows are mostly detail noise and bumps in the height data, not real ponds; Earth's
real lakes come from its data (water mask outlines and HydroLAKES).

## Rule (pond rules 1, saved field 126 `PondRules`, new worlds)

- **Surface tries:** a try whose start is at or above the column's top (Earth's model) is kept where the mapped wetland
  keeps it: `WetlandGrid.PondShare`, every group whole and damp ground at half (the water table's weights), against a
  frozen hash of the column (`GeologyTransitions.Hash`), so each layer of one pond gets the same answer.
- **Cave tries** (below the column's top) are left to vanilla.
- **Older worlds** (no `PondRules`) keep vanilla's ponds with the earlier guards.

## The Earth tab's choice (pond rules 2, tall.25)

`earthSurfacePonds` (`EarthWorldOptions.PondRules`), new worlds; cave tries are vanilla's in every case:
- **Off** (default, `PondRules` 2, `EarthParams.PondRulesNone`): every surface try is turned away.
- **Wetlands only** (`PondRules` 1): the rule above.
- **Vanilla** (`PondRules` 0): vanilla's ponds with Earth's other guards, as older worlds.
A world config without the key (hand-typed) or with another value gets the default. The pond rules moved from
`ComputeParams` into `EarthWorldOptions.Freeze`, as the springs rules.
- `/earth where` names the share kept; the self-test and the server's shutdown log count the tries.

Shares kept: Everglades 0.98, Danube delta 0.78, Flow Country 0.44, Picardy 0.34, Teiči bog 0.31, Riga 0.08, Val Venosta
0.02, Bernese Oberland 0.03, Sahara 0.

## Checks

- WetlandTests: 82 checks pass (pond shares by group, the known wetlands and dry places, the hash keeps 30% at a share of
  0.3 and the same each time).
- Bench, new 1:10 world (`t23a`, tall.23): pond rules v1 on at start-up. Over one session (Lech, Aiviekste, Rome) the
  rule turned away 33,560 surface tries, let 1,983 through in wetlands and left 1,292 cave tries to vanilla; no errors.
- The stepped silt rings the author found in the Tiber at Rome (41.94° N, 12.48° E, in a tall.22 world) are there in
  tall.23 too: they are not vanilla's ponds but Earth's outline lake bed (looked into separately).

## History

- 2026-11-08: asked, chosen ("Wetlands only"), built in tall.23 and checked as above.
- 2026-11-08: the author asked whether vanilla's ponds are removed and for the setting to be on the Earth tab, off by
  default: built in tall.25 (Off, Wetlands only, Vanilla). EarthWorldOptionsTests: the default, each value and a stray
  one save as expected, older saves keep 0. Bench, new 1:10 world (`t25a`): "pond rules v2: no vanilla surface
  ponds" at start-up; no errors.
