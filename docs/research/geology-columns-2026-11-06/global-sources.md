# Global sources for ground columns (checked 6 November 2026)

A research agent checked these links with HTTP HEAD or range requests on 6 November 2026. Anything marked UNVERIFIED was not checked. Nothing here has been downloaded into Earth or decided yet.

Bytes per cell were measured by baking three 10°×10° tiles at 2′ (300×300) from the SoilGrids WCS for 20–30°E, 50–60°N, then zlib as uint8:
- WRB group: 0.33 B/cell
- clay: 0.39 B/cell
- pH: 0.44 B/cell

Land at 2′ without Antarctica is about 15 M cells.

| Field | Primary | Backup / notes | Native res | Licence | Bake at 2′ / shipped estimate |
|---|---|---|---|---|---|
| soil_group | SoilGrids 2.0 WRB `MostProbable` (Poggio et al. 2021), via the WCS at `maps.isric.org/mapserv?map=/map/wrb.map` | SoilGrids 2017 `TAXNWRB` (CC BY 4.0). HWSD v2 is CC BY-NC-SA 3.0 IGO, so it is for tests only and must not ship. SoilGrids 2.0 has no aggregated WRB layer. | 250 m | CC BY 4.0 | WCS sweep in 10° tiles (about 200 land tiles); ~5 MB |
| clay_pct, ph | SoilGrids 2.0 `clay`, `phh2o`, 15–30 cm, through the WCS | The aggregated 1 km and 5 km files are in Homolosine (ESRI:54052), so use the WCS, which returns EPSG:4326. Better to request 30″ and take the mean (the WCS resampling method is UNVERIFIED). | 250 m | CC BY 4.0 | ~6 + 6.5 MB |
| quaternary_m | Shangguan et al. 2017, doi:10.1002/2016MS000686, `BDTICM_M` (depth to bedrock, cm); `files.isric.org/soilgrids/former/2017-03-10/aggregated/5km/BDTICM_M_5km_ll.tif`, 13.4 MB (10 km is 3.5 MB) | Pelletier et al. 2016, ORNL DAAC 1304 (needs an Earthdata login, caps at 50 m) | 250 m; 1/5/10 km aggregates | CC BY 4.0 | Log-coded uint8; ~4–6 MB (estimate) |
| cover_unit | GUM v1.0 (Börker et al. 2018), PANGAEA 884822, `hs.pangaea.de/Maps/Boerker-etal_2018/Boerker_et_al_GUM_v1.0.zip`, 1.44 GB | GLiM `su` class | polygons | CC BY 3.0 | Rasterised once at cell centres; ~1–2 MB (estimate) |
| subcrop age | Chorlton 2007, GSC Open File 5529, doi:10.4095/223767, about 9,100 polygons with age and rock type; `of_5529.zip`, 577 MB | Macrostrat "tiny" scale is the same map (source_id 154); its larger scales add regional maps. USGS `mrdata.usgs.gov/services/wfs/worldgeol` (GSC OF 2915; licence UNVERIFIED) | 1:35M | Open Government Licence – Canada | Age code at 2′; lithology stays from GLiM; ~1–2 MB (estimate) |
| basement_m | CRUST1.0 (already in Earth) | — | 1° | — | ~50 KB |
| seabed | Dutkiewicz et al. 2015, *Geology* 43:795, 13 classes; `earthbyte.org/webdav/ftp/papers/Dutkiewicz_etal_seafloor_lithology/seabed_lithology_v1.nc`, 13 MB | Diesing 2020, PANGAEA 911692, 5 classes, deeper than 500 m only (CC BY 4.0) | 0.1° | **unclear**: the EarthByte page states none; ask before shipping | ~0.5 MB |
| province | Hasterok et al. 2022, ESR 231:104069; Zenodo 5093930 (CC BY 4.0); shapefile `global_gprv.shp` in GitHub `dhasterok/global_tectonics` (the repo says GPL-3.0, so cite Zenodo) | USGS World Geologic Provinces (UNVERIFIED) | 914 polygons | CC BY 4.0 | Fields `prov_type` (15 types), `lastorogen` (28), `crust_type`; under 0.5 MB |

Total shipped estimate: **about 25–30 MB**. The three soil fields make up about 17 MB of that. Taking the mode or mean from 30″ and quantising pH to 0.2 and clay to 2 % should bring them under about 12 MB (not measured).

Region downloads at 30″ would need:
- soil (SoilGrids WCS)
- quaternary_m (Shangguan 1 km, or Pelletier with a login)
- cover_unit (GUM)
- subcrop (Macrostrat larger scales, national maps)

## Gaps and fallback rules (proposed)

**Shelf and Arctic seabed.** Dutkiewicz's shelves are interpolated and the high Arctic appears empty; Diesing starts at 500 m. Proposed rule:
- glaciated shelf (till ashore) → gravelly sand or till
- river mouth → mud
- tropical shelf → carbonate sand
- otherwise sand above about 50 m, mud below

NOAA deck41 and dbSEABED are later options; their terms are UNVERIFIED.

**Formations between subcrop and basement.** There is no open global source. Use templates chosen by `prov_type` × subcrop age, thickened to the CRUST1.0 depth.

**basement_rock.** There is no open global map. Proposed rule:
- shield or craton → granite-gneiss
- orogen or accretionary complex → schist or granite
- arc → diorite or andesite
- oceanic crust or ophiolite → basalt or gabbro

`lastorogen` gives the age.

**Subcrop under mapped cover.** Where a source maps Cenozoic sediment instead of bedrock, take the nearest older domain or the template.

**Quaternary thickness.** Shangguan is weak beyond a few tens of metres and Pelletier caps at 50 m. Keep the GUM unit × relief rule and the 3 m last resort.

## Corrections to global-design.md

- Macrostrat's API reports CC BY 4.0, but its global layer comes from GSC. Cite Chorlton/GSC under OGL-Canada as well.
- "SoilGrids at 1 km for the base": that 1 km product is in Homolosine and has no WRB layer. Use the WCS in EPSG:4326 instead.

## Suggested download order

1. SoilGrids 2.0 WCS sweep at 2′ for WRB, clay and pH: about 600 requests, about 40 MB of traffic.
2. Shangguan `BDTICM_M_5km_ll.tif` (13.4 MB).
3. Hasterok `global_gprv.shp` and `.dbf` (about 3 MB).
4. Chorlton OF 5529 (577 MB, for baking only), or Macrostrat tiles at z4–5 instead.
5. Dutkiewicz seabed (13 MB) once its licence is confirmed.
