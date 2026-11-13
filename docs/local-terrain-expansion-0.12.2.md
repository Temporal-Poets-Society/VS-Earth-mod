# Earth 0.12.2: bounded local terrain expansion

Changelog: companions can reserve a bounded local expansion of the existing Earth landscape around a fixed historical anchor. Land, coastal masks, rivers, banks, water levels, and river vegetation are sampled through the same invertible mapping. This provides room for fixed-size landmarks on smaller Earth worlds without deleting a river or moving the historical anchor. Worlds with no registered expansions and every column outside a registered domain retain their previous results exactly.

The API is opt-in. Earth 0.12.2 alone does not register any expansion. Existing generated chunks are not rebuilt. The companion owns choosing, persisting, fingerprinting and validating a site's parameters before publishing them during terrain planning; it must reject incompatible existing saves before registration. Historical model geometry remains at its fixed scale.

## API

Types are in `EarthMap.Core`:

```csharp
var expansion = new TerrainExpansion(
    "giza", centerX, centerZ, innerRadius: 439, outerRadius: 1317, magnification: 8);

if (!model.IsTerrainExpansionAllowed(expansion, out string reason, mapWidth, mapLength))
    throw new ArgumentException(reason);

// Prospective, stateless query. Does not publish or modify the model.
var prospective = model.GetColumnForExpansion(expansion, worldX, worldZ);

// After every site has passed its full geometry, shore-bank and story checks:
model.RegisterTerrainExpansions("earthruins-quality-preview", expansions, mapWidth, mapLength);
```

Registration uses the existing terrain-planning lock and closes with `SealTerrainReservations`. Each owner can publish once. The whole batch is validated before one immutable array is published; failed batches leave no partial state. Expansion domains may not overlap, cross the map/polar/seam boundaries, or overlap protected Earth story terrain. A companion must also check its game-specific structure reservations, which the pure core model cannot inspect.

`TerrainExpansion.Project` maps physical coordinates to canonical source coordinates. `Unproject` maps canonical feature positions back to physical space. `Jacobian` exposes the local derivative and `TransformFlow` maps a canonical flow tangent to physical space. Parameters are immutable. Inner radius must be positive, outer radius larger and at most 8,192 blocks, and magnification between 1 and 64. A magnification of one is exactly the identity.

`TerrainModel.GetColumnWithoutReservations` includes published expansions but excludes companion height pads. `GetColumnForExpansion` prospectively uses its supplied expansion where that expansion applies; outside it, already-published expansions retain their behavior. `ProjectTerrain` and `TerrainGeoAt` expose the registered canonical sampling point. `Column.Lat/Lon` and `Geometry.ToGeo` continue to report physical-world geographic attribution.

Raw `RiverNetwork` methods—including nearest-hit distances, reach lines, drops, and `DistanceToWater`—keep their existing canonical coordinates and units. They are not silently redefined as physical distances in a deformed region. Consumers needing physical positions can use `Unproject`; consumers needing physical shortest distances must calculate those in physical space.

## Mapping and hydrology

For physical radius `r` from the anchor, the canonical radius is:

```text
f(r) = r * alpha(r)
alpha(r) = 1 - (1 - 1/magnification) * w(r)
```

The weight is one throughout the inner radius, then falls to zero using a quintic smoothstep by the outer radius. Inside, a source feature's distance from the anchor is enlarged uniformly. In the surrounding annulus, the mapping returns smoothly to the unchanged world. Outside, code returns the original coordinates directly rather than reconstructing them arithmetically.

Because `w` is nonincreasing, `f'(r) = alpha + r*alpha'` is strictly positive. The radial map therefore has no folds; its tangential derivative is positive too. Value, first derivative and second derivative meet the constant inner/outer maps continuously. The inverse uses bounded bisection in the transition annulus.

Natural elevation, detail noise, shoreline decisions and `ApplyRiver` all sample the same canonical point. River pool/drop data and the river index remain unchanged. Flow direction is multiplied by the inverse Jacobian and normalized, so the current follows the moved channel through the transition. Story flat zones and companion pads remain in physical coordinates; registration prohibits story overlap. The existing eight-column-margin `SettleSteps` pass operates on the resulting physical neighbouring columns and derives local ramps/falls from those columns.

Climate pixels, forest/shrub moisture and the Nile-style river green belt use the same source mapping. The ocean-mask layer and Earth world's beach, pond and lake-freezing climate queries use `TerrainGeoAt`. The physical geographic grid remains available separately. Regional species/ore/geology geographic catalogs retain their existing physical attribution; the expansion does not redraw political or ecological region polygons.

An expansion does not authorize water reclamation. The companion must still check its complete geometry—including explicit underground air—against the expanded water/river columns and an adequate bank-containment rim. Dry beach material by itself is not water, but cuts must preserve adjacent water containment. A radial expansion also cannot dry an anchor that is itself wet, because the anchor is a fixed point.

## Verification

`tests/TerrainExpansionTests` uses a frozen copy of the released 0.12.1 terrain model as its oracle, with test-only type renames. The candidate's canonical river network is shared by the old/new model; its source file and index are unchanged from 0.12.1.

The suite passed **2,331,950 assertions**, including:

- 129,938 full terrain-column comparisons; doubles/floats are compared by bits where exact equivalence is required.
- 15,972 wet-column comparisons against the same canonical source terrain, covering moved channels and shores.
- Radial monotonicity, positive Jacobians, inverse mapping and exact exterior identity at magnifications 1, 2.3, 8, 32 and 64.
- 516 flow-direction comparisons against finite differences of the inverse map.
- 40,960 comparisons of native eight-column-margin water settlement against a wider surrounding region, using river windows from both pilot domains.
- Unmodified-world and exterior column identity, source-coherent climate/forest/shrubs/temperature, registration atomicity, duplicate owners, overlap/story/seam rejection, and rejection of registration after sealing.

The real-data fixture matches the user's Cylinder World layout: width 256,000, length 102,400, ring 204,800, height 384, sea 165, seed 290142474. It retains 826,332 canonical river segments. Test domains are Giza at 137553/34162 with radii 439/1317 and magnification 8, and the Colosseum at 127065/27391 with radii 237/711 and magnification 32. The companion's placement tests determine the final fit; these core tests establish the mapping and hydrology behavior.

```powershell
dotnet run --project tests/TerrainExpansionTests/TerrainExpansionTests.csproj -c Release -- 'C:\VS mods\Earth'
```

These are offline core/settlement tests. Native generation, geometry placement, save reload and client rendering are validated separately by the companion runtime workflow.

## Package provenance

Before editing, all 32 current source documents matched the portable PDB hashes in released Earth 0.12.1. This change modifies only `src/Core/TerrainModel.cs` and `src/EarthWorldGen.cs`, and adds `src/Core/TerrainExpansion.cs`. The candidate PDB matches all 33 source documents in the isolated build. Package entry hashes confirm all 0.12.1 assets/data are unchanged; only the DLL, PDB and modinfo differ.

The isolated project is `obj/terrain-expansion-0.12.2/EarthMap.csproj`, configuration `TerrainExpansion`, optimization enabled, portable PDBs. Its mod/product version is 0.12.2, file version 0.12.2.0, and public assembly identity remains `EarthMap, Version=1.0.0.0` for existing companion references. Shared `modinfo.json` remains at 0.12.0, and all previous release archives remain byte-for-byte intact. No live profile was installed or changed.

| Artifact | SHA256 |
|---|---|
| Earth 0.12.2 ZIP | `cfd1544ce3a9f5e4fa046a23780457708df7b54059880bb2b26775c6fae64a74` |
| Earth 0.12.2 DLL | `13f1b7054e1c62f0b0ab1a862ef6914d767f0ca72aa1e234264df9dd511c66c8` |
| Preserved 0.12.1 ZIP | `c2b432f86157c78b11e7103846e98a0bfcd561a795df0d1464ceb5bef36e6081` |

Reports are `tests/TerrainExpansionTests/results.json`, `source-change-audit.json`, `source-built-0.12.2.json` and `package-audit-0.12.2.json`. The pre-edit source check is `tests/RiverIndexTests/source-before-expansion.json`.
