# Ground columns: what real data exists, field by field (7 November 2026)

The author asked to find out what data is reasonable before choosing the resolution ("Most of it probably anyways will be made up … inside the cell we anyways have some variation"; a 3–5 km grid is fine for the first step). This file merges two research passes:
- [global-sources.md](global-sources.md): SoilGrids, Shangguan, GUM, Chorlton, CRUST1.0, Dutkiewicz, Hasterok and others, link-checked 6 November.
- [data-inventory-sources.md](data-inventory-sources.md): ice-age ice, permafrost, loess, floodplains, karst, volcanic, salt, weathering and national maps, 7 November. Many official sites blocked the agent, so its UNVERIFIED marks matter.

Coverage shares are rough shares of ice-free land.

| Field | Real data | Covers | Data resolution | Licence | What fills the rest |
|---|---|---|---|---|---|
| Soil group (and so fertility) | SoilGrids 2.0 WRB | ~100 % (modelled from profiles everywhere) | 250 m | CC BY 4.0 | — |
| Cover thickness | Shangguan 2017 depth to bedrock | ~100 % (modelled; weak beyond a few tens of metres) | 1 km | CC BY 4.0 | rule by unit and relief, for deep cover |
| Surface sediment type | GUM | ~100 % at 0.5° (polygons finer), with gaps at coasts | 0.5° grid / polygons | CC BY 3.0 | gaps from the nearest land unit; refined by place (below) |
| — finer, national | Canada GSC 1:5M, USGS DS 425 1:5M, Australia GA 1:1M, Norway NGU, UK BGS 625k, Latvia | ~17 % (US, Canada, Australia), a few % more in Europe | 1–10 km | OGL-Canada, CC BY 4.0 (GA), others to check | — |
| Ice-age (LGM) ice extent | Batchelor et al. 2019 | ~100 % of the former ice sheets | margins finer than 3 km | CC BY 4.0 (the shapefile is still to be found in the archive) | — |
| Permafrost | ESA CCI v5 / Obu 2019 | ~100 % of the northern hemisphere | 1 km | citation / CC BY 3.0 | rule by temperature elsewhere (mountains of the south) |
| Ground ice | Brown et al. 1997 | Arctic | 12.5–25 km | cite only | rule |
| Floodplains | GFPLAIN250m (to 60° N) | most rivers | 250 m | CC BY 4.0 | **better taken from Earth's own rivers**: the world's rivers are Earth's, not the real DEM's |
| Coast, beaches, dunes, lagoons | none global | — | — | — | Earth's own coastline, waves and climate |
| Loess | GUM `El`; Lehmkuhl 2021 for Europe only | Europe in detail, elsewhere GUM | 1:2.5M | CC BY-NC 4.0 (blocks a release) | GUM + rule (downwind of ice-age outwash and deserts) |
| Laterite, duricrust, deep weathering | **none open** | — | — | — | rule: rainfall, dry season, surface age, unglaciated shields |
| Calcrete, gypsum crust | none open (SoilGrids Calcisols and Gypsisols are a strong hint) | via soil groups | 250 m | — | rule from soil group + aridity |
| Salt flats, playas | Natural Earth (major ones only) | a sample | 1:50M | public domain | rule: closed basins in drylands (Earth's water data knows terminal lakes) |
| Volcanic ash, lava fields | **none open** (GVP volcano points are non-commercial) | — | — | — | rule from GLiM volcanic classes and province type (arcs, hotspots) |
| Karst | WOKAM (built on GLiM) | — | — | — | GLiM carbonate classes already give it |
| First bedrock: rock type | GLiM (in Earth) | ~100 % | 1:3.75M (~2–5 km) | CC BY 3.0 | — |
| First bedrock: age | Chorlton 2007 (= Macrostrat's world layer); IGME for Europe | ~100 % at 1:35M | 10–50 km | OGL-Canada | nearest mapped unit |
| Layers down to the basement | regional templates (Baltic) | <1 % | — | — | **guesswork everywhere else**: generic stacks by province type × age |
| Depth to basement / sediment | CRUST1.0 | 100 % | 1° | free | — |
| Basement rock | **none open** | — | — | — | rule by province (Hasterok 2022, CC BY 4.0) |
| Seabed | EMODnet (Europe), Dutkiewicz 2015 (deep sea, licence unclear), Diesing 2020 (>500 m) | European seas; deep ocean | 0.1° / 1:250k | CC BY 4.0 / unclear | rule by depth, latitude, distance to shore |

## What this says

- **Well covered:** the top and the bottom of the column. Soil, cover thickness and surface sediment type exist everywhere, as do ice-age ice, permafrost, the first bedrock's rock type and age, and the depth to basement. Most of this is modelled or generalised, not surveyed, but it is real data.
- **Guesswork:**
  - the special surface types: laterite, calcrete, salt, volcanic ash, coastal sediments
  - everything between the first bedrock and the basement
  - the basement rock itself

  These are rules over climate, terrain and tectonic province, all of which Earth has everywhere. Floodplains and coasts should follow Earth's own rivers and coastline anyway, not real-world masks.
- **Resolution the real data supports:** 250 m–1 km for soil, thickness, permafrost and floodplains. About 2–5 km for rock type and the best national maps. 10–50 km for ages, ground ice and deep structure. 50 km for GUM's grid.

## Proposed grid

**2′ (1/30°, about 3.7 km) for everything in the first step, on Earth's own terrain grid.** Coarse sources (GUM, CRUST1, ages, province) stay at their native grids. Finer sources are averaged down: mode for categories, mean for quantities. Shipped size is about 25–35 MB. The generator varies things inside each cell (wavy boundaries, cover thinning on slopes, thickening in valleys).

Going finer later only pays for soil, cover thickness and permafrost, where the data is 250 m–1 km. That would be a separate 30″ download (about 150–200 MB, section 8.1 of [global-design.md](global-design.md)), and only if the 2′ world looks too coarse in game.

## Licence notes before shipping

- Dutkiewicz seabed: no licence stated. Ask the authors or EarthByte, or use rules plus Diesing.
- Batchelor 2019 ice-sheet shapefiles (OSF 7jen3): no licence stated on OSF (checked 7 November); the article is CC BY 4.0. Baked into `global-glaciation.egts` on the branch; confirm before a release.
- Lehmkuhl loess: CC BY-NC. Use for tests only.
- GVP volcanoes: non-commercial. Use rules instead.
- The national maps other than Canada, Australia and Latvia have their licences still to read.
