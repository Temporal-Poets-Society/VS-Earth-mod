# Habitat prototype: Latvia (6 November 2026)

Status: **offline research prototype.** It is not game code and nothing in Earth changed. It tests section 3a of the [habitat proposal](../README.md): water table → soil moisture → wetlands and forest site types, on a Latvia box (55.65–58.10 N, 20.95–28.25 E).

## Method

| Step | How |
|---|---|
| Heights | GLO-90 tiles from `Earth/rawdata`, averaged to 6″ × 9″ cells (~185 × 155 m), 1470 × 2920 cells; sea, lakes and rivers from the GLO-90 water-body masks |
| Drainage | priority flood with D8 routing over the filled surface; streams from 3 km² upslope area plus all mapped water |
| Height above drainage (HAND) | height above the stream cell each cell drains to |
| Hillslope | flow distance down to the stream `x` and up to the divide; `L = x + up` |
| Water table, v1 | depth = 0.25 m + k × HAND (k by subsoil: clay .3, loam .55, sand .9) |
| Water table, v2 (kept) | steady groundwater mound between streams (Dupuit): `h² = R/K (2Lx − x²)`, depth = HAND − h. Recharge R from Earth's climate rain (0.15 m/yr in Latvia); K by subsoil class; mapped peat gets a very low K, per the author's decision that peat takes part in the table |
| Moisture | 0 very dry … 6 flooded, from table depth and topsoil sand (section 3a table) |
| Richness | SoilGrids 2.0 topsoil sand, clay and pH (clips from the ground-column pilot) |
| Habitats | moisture × richness → Latvian forest site types; wetland types; floodplain meadows near large rivers |
| Reference | OpenStreetMap wetlands inside Latvia (Geofabrik extract of 6 November 2026, `natural=wetland` with its `wetland=*` kind): 4.9 % of the land |

Scripts: [habitat_proto.py](habitat_proto.py) and [rank_signals.py](rank_signals.py). Input data stays outside the repository.

## Results

### 1. Terrain alone finds wetlands only modestly

Each signal ranks every land cell from wettest to driest; the top 4.9 % (OSM's share) is compared with OSM. Chance would give 0.049 precision and AUC 0.5.

| Signal | AUC | Equal-area precision (= recall) | Within 500 m: precision, recall |
|---|---|---|---|
| Height above drainage (low = wet) | 0.705 | 0.094 | 0.18, 0.70 |
| Topographic wetness index | 0.651 | 0.074 | 0.15, 0.49 |
| Flat surroundings (1 km mean slope) | 0.688 | 0.201 | 0.27, 0.37 |
| Closed-hollow depth | 0.629 | 0.118 | 0.21, 0.30 |
| Groundwater mound − HAND (best K) | 0.737 | 0.178 | 0.27, 0.44 |
| −HAND + flatness | 0.740 | 0.201 | 0.28, 0.43 |

The mound model ranks best of the physical models and finds the real large bogs (Ķemeri, Cena, the bogs south of the Lielupe, Latgale). But at 90 m heights and ~1 km soil data no signal reaches more than about four times chance. Removing raised patches narrower than ~1 km from the surface (a crude canopy filter) did not help (best AUC 0.726). Bog edges sit only ~1 m below their surroundings in the data, so canopy is not a large artifact at this resolution. Raised-bog domes are not resolved: centres are 0.6 m *below* their edges.

Without mapped wetlands, thresholds that reach most OSM wetlands also flag 20–80 % of the land:

| Model | Wetland predicted | Recall | Precision |
|---|---|---|---|
| v1 HAND rule | 20.8 % | 0.44 | 0.10 |
| v2 mound, K ×100 | 39.9 % | 0.73 | 0.09 |

Over-prediction comes from flat clay plains (Zemgale) and narrow valley floors.

**Consequence for the design** (as agreed in 3a): mapped wetlands must set mire extent. The water table supplies the wetness gradient around and between them, and picks only small kettle and valley mires.

### 2. The full chain with mapped wetlands

Configuration:
- Mound model with K ×1000. This is an effective value tied to 150 m cells and 3 km² streams, which miss the real dense ditch-and-brook network.
- OSM wetlands stand in for the mapped layer (GLWD v2 / PEATMAP / national peat maps later); their kinds set the type.
- Unmapped cells: waterlogged becomes a small mire only on flat ground (1 km mean slope < 0.25 %) away from streams. Waterlogged valley strips become "wet", not mire.

| Habitat | % of land |
|---|---|
| Lichen pine (sils) | 6.3 |
| Pine, cowberry/heather (mētrājs) | 14.9 |
| Pine-spruce, blueberry (lāns) | 19.3 |
| Spruce, oxalis (vēris, damaksnis) | 14.1 |
| Oak-ash-lime-hazel (gārša) | 14.2 |
| Wet pine-birch, sphagnum | 10.6 |
| Wet birch-spruce-alder | 13.0 |
| Raised bog | 3.7 |
| Fen, alder swamp, birch-pine swamp, marsh | 1.5 |
| Floodplain meadow | 2.3 |
| Small unmapped mires (included above) | 0.3 |

Moisture: 21 % dry, 42 % fresh, 5 % moist, 26 % wet, 5 % waterlogged or flooded. The wet share is high for today but plausible for 1318: much of Latvia's wet forest and mire edge was drained in the 20th century, and OSM maps today's wetlands.

Maps ([out/](out/)):
- [05-habitats.png](out/05-habitats.png): all of Latvia
- [07-habitats-kemeri-cena.png](out/07-habitats-kemeri-cena.png), [09-habitats-teici.png](out/09-habitats-teici.png): zooms
- [03-water-table-depth.png](out/03-water-table-depth.png), [04-moisture.png](out/04-moisture.png)
- [02-hand.png](out/02-hand.png), [01-elevation.png](out/01-elevation.png)
- [06](out/06-wetlands-vs-osm.png), [08](out/08-wetlands-vs-osm-kemeri-cena.png), [10](out/10-wetlands-vs-osm-teici.png): model vs OSM. With mapped input these overlaps are circular by design; only the unmapped cells are the model's own.

What looks right:
- The Zemgale clay plain is rich broadleaf (gārša).
- The sands around Riga and along the coast are pine (mētrājs, sils).
- Latgale and the Ķemeri–Cena area carry most of the bogs.
- Floodplain meadows line the Lielupe, Daugava and Gauja.
- Wet forests surround the bogs.

### 3. In-game test overlay (dev build only)

The prototype's habitat map was baked into `earthdata/habitat-latvia.bin` (2920 × 1470 cells, one byte per cell, 521 KB). A dev build in the Earth worktree `habitat-overlay` (branch `claude/habitat-overlay`, version `1.0.3-tall.13-habitat.1`, not committed, not for release) reads it, only inside Latvia:
- The forest map is raised in each habitat, then trees are thinned by habitat: bog 12 %, fen 7 %, meadow 3 %, marsh none.
- Species weights come from the habitat. On the bench, vanilla stand-ins are used: fir for spruce, river birch for alder, Norway maple for ash and lime.
- Tree size follows the habitat: bog pines ×0.35, rich sites ×1.15.
- More moss grows on trunks in wet types.
- Top block: peat in bogs, fens, marshes and part of the wet forests, with bare peat hollows in patches; soil fertility follows site richness.
- Open pools in clumps: bog 12 %, carr 12 %, marsh 30 %.
- Standing water lies one block down in mires and two blocks down in wet forests and meadows, only where the surface is level, so digging there opens water.

Bench world `habitat2` (1:25, creative, spawn on the Ķemeri bog) and screenshots in [../ingame/](../ingame/):
- **Looks right:** bogs read as open mires with stunted pines and pools. The Zemgale broadleaf, Riga sand pines, Vidzeme spruce and wet birch-spruce forests are visibly different from each other.
- **Not yet:** the ground layer is still vanilla's grass and shrubs everywhere. Lichen pine has no lichen, and bogs have no heather, sphagnum or cotton grass beyond vanilla's. Bog colours are vanilla grass tint. These are the flora step (proposal 3), not the overlay.

### 4. With the ground columns, at 1:10 (6 November, evening)

After the author's review of section 3:
- **Soil types from the ground columns.** `GROUND=<ground-baltic-v1.bin>` replaces the SoilGrids stand-ins with the ground grid's WRB soil group, GUM cover class, clay and pH.
  - Permeability: sandy soils and outwash, dune or marine cover drain fast; Stagnosols, Planosols, lake clay and clay ≥ 30 % hold water.
  - Richness: Podzols and Arenosols A; Regosols, Planosols, Stagnosols, Umbrisols and the lighter 40 % of Albeluvisols B; other Albeluvisols, Luvisols and Cambisols C; Phaeozems, Chernozems, Fluvisols, and base-rich or clayey Luvisols, Cambisols and Gleysols D.
  - Water: Gleysols are at least moist (wet within 1.5 m of the table). Histosols and peat cover count as mire.
  - Map: [11-habitats-ground-columns.png](out/11-habitats-ground-columns.png). Shares: sils 2.2, mētrājs 6.1, lāns 14.1, vēris/damaksnis 33.2, gārša 12.3, wet poor 4.9, wet rich 16.1, bog 4.0, carr 3.0, marsh 1.0, meadow 2.3, coastal dune pine 0.6, beach/foredune 0.2 (%).
  - The 1 km soil map's polygon edges show as straight lines in places.
- **Coast:** beach and foredune within ~250 m of the sea (sand, no trees); coastal dune pine within ~2 km on sandy or poor soil (sand top in patches, sparse lower pines).
- **Taller trees, like Lakul's Enhanced Vanilla Trees:**
  - Vanilla's default size range (0.2–1.0) averages a 12-block pine. Each species now has a mature size range (pine and spruce 0.85–1.35, oak 0.75–1.2, birch 0.6–1.0, alder 0.55–0.95), times the habitat factor.
  - Lakul's mod does this by patching vanilla's tree JSON for every world; here it is per species and habitat, and only inside the overlay.
- **No hidden water** under solid blocks any more (it showed as a layer below the soil). Pools stay. A water table you can dig into belongs to Hydrology (Earth gives the level, D06/D25).
- **Lookup:** a small domain warp, so the 185 m cells don't show as squares at 1:10. Land columns in water cells take the nearest land habitat, so shore trees follow the coast.

The ground-columns prototype was copied unchanged from the `ground-columns` worktree into the dev build. Its tests pass there. Its save field moved from ProtoMember 93 (taken on the tall line) to 190 in `claude/ground-columns` as well, so both branches and the `habitat10b` world read it the same way.

Bench world `habitat10b` (1:10, creative, spawn on the Carnikava dunes). Screenshots in [../ingame-1to10/](../ingame-1to10/):
- **Looks right:** the coast reads as beach, then foredune, then open dune pine. Bogs, Zemgale mixed broadleaf and spruce, heather pine, Vidzeme spruce and Latgale pine-spruce differ from each other, and the trees are tall.
- **Still wrong:**
  - Poor pine types (lichen and heather pine) are closed, tall stands. They should be open and lower; density and size need tuning.
  - Ground flora is still vanilla grass everywhere: lichen, heather, sphagnum, cotton grass, blueberry and cowberry are missing.
  - Beaches are a one-block bank onto gravel in places (Earth's beach terrain, not the overlay).

## Limits

- **Soil richness and permeability are stand-ins.** SoilGrids is smooth in Latvia: topsoil sand 41–58 % between the 10th and 90th percentile, clay 13–23 %. The classes are relative (the sandiest quarter = poor), and its ~1 km cells show as blocks in the zooms. The ground column's Quaternary map (LVĢMC 1:200,000, CC0: outwash sand, till, glaciolacustrine clay, peat) should replace both, with per-layer permeability.
- **Lichen pine is too common** (6 % against a rare type in reality) because "very dry" rests on the same relative sand classes.
- **K is calibrated, not physical.** A real drainage density, denser on clay than on sand, would let K be physical.
- **No mapped-wetland dataset yet.** OSM stands in. GLWD v2 and PEATMAP would make it worldwide; licences still to check.
- One climate cell type (hemiboreal) only; zones are not tested here.
- Real forest site-type shares (Latvian State Forest Register) have not been compared yet.

## Next

1. Replace the SoilGrids stand-ins with the ground column's Quaternary cover and permeability (LVĢMC map) when that session has it.
2. Fetch GLWD v2 and PEATMAP, and rerun with them as the mapped layer instead of OSM.
3. Compare site-type shares with the forest register.
4. Then move the model into `src/Core` as `HabitatModel` and `WaterTable`, with `/earth site`.

## Running

```
python -I habitat_proto.py <Earth/rawdata> <soilgrids clips> <Earth/earthdata/climate.bin> <reference> <out dir>
```
`<reference>` is a comma-separated list: the Geofabrik `gis_osm_water_a_free_1.shp` (needs `PYSHP_DIR` pointing at pyshp), the Geofabrik `latvia.poly`, and/or Overpass JSON. Environment variables:
- `WT_MODEL=hand|mound`
- `K_MUL` (1000 for the run above)
- `MAPPED=1`: use the reference as mapped wetlands
- `DRAIN_CACHE`: cache the drainage step (~1 minute)
- `DUMP`: write the arrays that `rank_signals.py` reads
- `GROUND_OPEN=n`: crude canopy filter
