# Sources for restoring 1318 coasts and waters

Survey of 8 Nov 2026, read only. No data files were downloaded. "Unverified" means not confirmed on a primary page.

## 1. Netherlands and other North Sea coasts

| Source | Publisher | Licence | Format, scale | Period | Use for us |
|---|---|---|---|---|---|
| Paleogeografische kaarten, Atlas van Nederland in het Holoceen, service v2.1 ([data.overheid.nl 62601](https://data.overheid.nl/en/dataset/62601-paleogeografische-kaarten---atlas-van-nederland-in-het-holoceen); WFS `https://services.rce.geovoorziening.nl/paleogeografischekaarten/wfs`) | RCE (Vos et al., Deltares/TNO) | **CC BY 4.0**. WMS lists access constraints and fees as "none". | WFS/WMS vector polygons with fields `legenda` and `omschrijving`, plus a separate `waterlopen` (watercourses) layer for each slice. Scale not stated. | 13 slices, including **1250 AD** (`paleogeografie1250nc`), 800, 1500, 1850 and 2000 AD | Best source. Use 1250 as the 1318 base layer. It covers the Netherlands only and is generalised. |
| Vos & Knol 2015, *Netherl. J. Geosci.* 94(2):157–183, [doi 10.1017/njg.2015.4](https://www.cambridge.org/core/product/0330DE1B4003CC38676132DBD54524E3) | Deltares, Groninger Museum | Not stated in the text I read (unverified) | Maps as figures only. No GIS found. | 500 BC, 100, 800, **1500**, 2000 AD | The only cross-border map from the Marsdiep to the Weser (Niedersachsen). Digitise the 800 and 1500 maps for the Ems, Dollard and Jade. |
| Belgium/Flanders | – | – | No GIS found | Holocene maps (Baeteman & Declercq 2002) stop at 6 ka; Ervynck et al. 1999 is a schematic map for the 7th–8th c. | Gap. The Zwin was an arm of the sea by the late 13th c. |
| Nordfriesland, Danish Wadden | DFG Rungholt project | – | Site surveys only; no regional shoreline found | c. 1200–1362 | Gap. The area before 1362 was about 50% larger than today's Uthlande (state portal). Mejer's 1649 map is thought to put the coast too far west. |
| UK Fens | East Anglian Archaeology reports on ADS (e.g. [EAA 52](https://archaeologydataservice.ac.uk/catalogue/adsdata/arch-2304-1/dissemination/pdf/eaa/vol_052/EAA_REPORT_52.pdf)) | ADS terms (unverified) | Paper maps | Medieval | Digitise these. In 1318 the fens were undrained (the large-scale drainage was 17th c., from general knowledge); Whittlesey Mere (756–1,214 ha) was drained 1851–53. |

Timing checks: the Zuiderzee formed c. 1170–1196 and reached its full extent c. 1400. In 1531 the Haarlemmermeer was 26 km², with three smaller lakes nearby totalling about 31 km²; it had grown to about 150 km² by 1647 and was drained 1848–52. So the 1250 slice is close for both. Land drowned after 1318 should stay land: the 1421 St Elizabeth flood area, Reimerswaal (1530), Saeftinghe and the later Dollard. These dates are from general knowledge and were not checked here.

## 2. Global modern reclamation and water change

| Source | Licence | Format, resolution | Period | Use for us |
|---|---|---|---|---|
| JRC Global Surface Water v1.4, transition band ([GEE](https://developers.google.com/earth-engine/datasets/catalog/JRC_GSW1_4_GlobalSurfaceWater)) | Copernicus: "free of charge, without restriction of use". Cite it, with "Source: EC JRC/Google" on maps. | 30 m raster. Classes: 2 new permanent, 3 lost permanent, 5 new seasonal, 6 lost seasonal, among others. | 1984–2021 | Good for flagging new reservoirs and new land. It cannot see anything before 1984. |
| Murray et al. 2019, Global Intertidal Change, tidal flats ([GEE UQ/murray/Intertidal/v1_1](https://developers.google.com/earth-engine/datasets/catalog/UQ_murray_Intertidal_v1_1_global_intertidal)) | **CC BY 4.0** | 30 m, 11 three-year periods, 60°N–60°S | 1984–2016 | Shows tidal flats later reclaimed, e.g. in the Yellow Sea and at Saemangeum. |
| Zhang Q., "Global dynamics of coastal reclamation…" ([figshare 25556901 v4](https://doi.org/10.6084/m9.figshare.25556901.v4)) | **CC BY 4.0** | 30 m rasters (`changeyear` 42 MB, `landuse` 37 MB, RAR). Companion paper unverified. | 1985–2020 | The best global reclamation layer, with the year of change. |
| Sengupta et al. 2023, *Earth's Future* ([Zenodo 7528236](https://zenodo.org/records/7528236)) | CC BY 4.0 | Excel totals for 135 cities. No polygons ("GIS version … will be launched soon"). | 2000–2020 | Not usable spatially. |

Also: Murray et al. 2022 (*Science*, tidal wetlands 1999–2019, licence unverified) adds little. Martín-Antón et al. 2016 (*J. Coastal Res.* SI75) is a review with no data. Gap: no global dataset covers reclamation before 1984 (most of the Netherlands, early Tokyo Bay, the Wash), so those areas need regional sources or hand-made polygons.

## 3. Drained wetlands and lakes

- **Fluet-Chouinard et al. 2023**, *Nature* 614:281, [Zenodo 10.5281/zenodo.7616651](https://zenodo.org/records/7616651) v1.01. Licence **CC BY 4.0**. NetCDF in a 307 MB zip. It has 33 decadal slices from 1700 to 2020, seven drainage land uses, and covers 56°S–84°N. Grid cells are capped by the HYDE 3.2 land grid, so the resolution is probably 5′ (about 9 km), but this is unverified. Use: a coarse map of the fraction of each cell drained since 1700. It gives no outlines.
- **Lost lakes:** I found no global inventory. Wikidata (CC0) has 281 items classed as "former lake" (Q47486890), 244 of them with coordinates, and 14 classed as "drained lake" (Q110775381). These are points only. The other inventories are regional, e.g. drained lakes in northern Alaska 1955–2017 (AWI). In practice we need a hand-made list.

## 4. Canals with opening dates

- **OSM** (ODbL; taginfo, 7 Nov 2026): 812,572 objects tagged `waterway=canal`, of which only **1,352 (0.17%)** have `start_date`. There are 71,281 `water=canal` areas, and taginfo lists no `start_date` combination for them. Not usable as a date filter.
- **Wikidata** (CC0): 48,527 items are "canal" (Q12284), but only **348** have both a date (P571, P1619 or P729) and coordinates. The large canals are dated.
- **OpenHistoricalMap** (CC0) has some dated canals; coverage unknown. The Wikipedia page "List of canals by length" returned 404.

| Canal | Date | Source |
|---|---|---|
| Suez | opened 17 Nov 1869 | Wikidata P1619 |
| Panama | opened 15 Aug 1914 | Wikidata P1619 |
| Kiel | in service 21 Jun 1895 | Wikidata P729 |
| Corinth | in service 6 Aug 1893 | Wikidata P729 |
| Volga–Don | in service 27 Jul 1952 | Wikidata P729 |
| White Sea–Baltic | built 1931–33, opened 2 Aug 1933 | en.wikipedia |
| Karakum | begun 1954. The article says "completed 1958" but is categorised under "opened 1988". The staged dates up to 1988 are **unverified**. | en.wikipedia |
| Manchester Ship Canal | official opening 21 May 1894 | Wikidata P1619 |
| Nieuwe Waterweg | begun 31 Oct 1863, completed 1872 | en.wikipedia |
| Noordzeekanaal | built 1865–76, opened 1 Nov 1876. The rest of the IJ was then turned into polders. | en.wikipedia |
| Grand Canal (Yuan) | Kublai's works ran 1281–1293. The Jizhou canal dates to 1283; en.wikipedia says "1238", which is probably a typo (**unverified**). The Huitong canal was built in 1289. The Tonghui opened in 1293 and completed the Beijing–Hangzhou route. | en.wikipedia, Beijing sources |

So the whole Yuan Grand Canal existed in 1318 and must be kept.

## 5. Reservoirs that existed before 1318

- **GRanD**: v1.3 has 7,320 records and v1.4 has 7,424 dams. It has a construction or commissioning year field (`YEAR`, plus `ALT_YEAR`), and v1.3 added the year of dam removal. The GlobalDamWatch page states no licence for v1.3 (unverified). The SEDAC documentation could not be reached. **The number of dams dated before 1318 is unverified**, because counting them means downloading the table.
- **Global Dam Watch v1.0**, which supersedes GRanD ([*Sci. Data* 2024](https://pmc.ncbi.nlm.nih.gov/articles/PMC11461629/), figshare 10.6084/m9.figshare.25988293). Licence **CC BY 4.0**. Shapefile and geodatabase, with 41,145 barriers and 35,295 reservoir polygons. Its `Year_dam` field means the year of building, commissioning *or refurbishment*, and is filled for only 37% of records; `Pre_year` gives an earlier bound. So an ancient tank may carry its year of rehabilitation.

| Reservoir | Built | Water in 1318? |
|---|---|---|
| Parakrama Samudra | c. 386 AD; joined into one system 1153–86 | Probably, but it "gradually fell into disrepair" after Polonnaruwa declined (no date given). Rehabilitated 1937–44. |
| Kalawewa | Dhatusena, c. 455–478 | Uncertain. Restored 1885–87. |
| Minneriya | Mahasena, 276–303 | Probably. One source says the British found the dam unbreached. |
| Veeranam | Rajaditya Chola, 907–955 | Yes. Today it supplies Chennai and is sometimes dry. |
| Bhojtal (Upper Lake, Bhopal) | 11th c., attributed to Raja Bhoj | Yes. 31 km². |
| Sayama-ike | 616 (tree-ring dating) | Yes. Still in use. |
| **Mannō-ike** | 701–704; rebuilt by Kūkai 821 | **No.** The dam collapsed in 1184 and was abandoned until 1628, and the basin was farmed. |
| West Baray | 11th c.; 7.8 × 2.1 km | Yes. Its western end holds water year-round. |
| Proserpina, Cornalvo | 1st–2nd c. AD | Yes. Both still in use. |

Use: a rule of "remove reservoirs with year ≥ 1318" needs an allowlist of ancient reservoirs and a default for the 63% with no year. HydroLAKES reservoirs link to GRanD through `Lake_type` and `Grand_id`; this comes from its documentation and was not re-read here.

## 6. Historic extents

| Water | Water in 1318? | Best outline source |
|---|---|---|
| Lake Texcoco and the Valley of Mexico lakes | Yes. The 1519 extent is the usual proxy. | CDMX/CentroGeo GeoNode layer `lago1519`, digitised from a Wikimedia map of the basin in 1519 (host unreachable, licence unverified). Gorenflo 2015 has the survey GIS of the Basin of Mexico for checking. |
| Zuiderzee, Haarlemmermeer | Yes; the Haarlemmermeer was then smaller lakes | RCE 1250 slice (section 1) |
| Po delta before 1604 | Yes, but the coast and mouths were different (Porto Viro cut 1604) | Univ. Bologna field guide figure with a "14th century AD coastline" ([amsacta 2781](https://amsacta.unibo.it/id/eprint/2781/1/escursionedeltadelpo.pdf)). Cremonini & Samonati 2009 (GFDQ) on the maps before 1604. No GIS. |
| Yellow River 1128–1855 and the Jiangsu coast | In 1318 the river reached the Yellow Sea through Jiangsu. The mouth advanced 88 km between 1128 and 1855, 73 km of it after 1578, so the 1318 coast lay near the Song Fan Gong seawall. | No GIS found. Use the Fan Gong dike line as a proxy (Bao et al. 2020, *Climatic Change*). The progradation figures come from a search excerpt only. |
| Mesopotamian marshes | Yes, but in a medieval layout: the Great Swamp (Baṭāʾiḥ) was fed by the Tigris via Wasit, and the Tigris returned to its eastern bed in the 16th c. | UNEP/Partow 2001, Map 7 (land cover 1973–76); NASA gives 15,000–20,000 km². REACH 2020 has a "marsh extent 1973" layer. No GIS download found. |
| Lake Fucino | Yes. It had refilled to about 140 km² by the 5th–6th c., and a 13th-c. attempt to restore the Roman drain failed. Drained 1862–76, declared dry 1878. | Maps from before 1876. No GIS. |
| Lake Copais | Probably a lake and marsh; its medieval state is undocumented. Drained 1880s–1931, freeing 241 km². | 19th-c. maps. No GIS. |
| Hachirōgata | Yes. A lagoon of about 220 km², reclaimed 1957–77; 48 km² remain. | Japanese GSI topographic maps from before 1957. No GIS. |
| Saemangeum | Yes, estuary and tidal flats. The 33 km seawall was finished Apr 2006 and enclosed about 401 km². | JRC GSW and Murray tidal flats for 1984–86. |
| Tulare Lake | Yes, up to about 700 sq mi (about 1,800 km²). Drained by diversions from the late 1800s. | ESRP (CSU Stanislaus) "Historical lakes of the San Joaquin Valley", vector from Griggs et al. 1992. Use constraints "None"; acknowledge ESRP. Flagged as "not suitable for large scale mapping". |

## Main gaps

I found no open GIS for c. 1300 for Flanders, Nordfriesland, the Danish Wadden, the Fens, the Po, Jiangsu or the medieval Mesopotamian marshes, so these need digitising from published figures or old maps. I found no global record of reclamation before 1984. The pre-1318 count in GRanD has not been checked.
