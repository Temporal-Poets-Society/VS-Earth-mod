# Data inventory for ground layers: extra sources (checked 7 November 2026)

Complements `global-sources.md` and `global-design.md`. Those already cover SoilGrids, Shangguan, Pelletier, GUM, GLiM, Chorlton, Macrostrat, Hasterok, CRUST1.0, Dutkiewicz, Diesing, EMODnet, IGME 5000 and the Latvian grid; nothing from them is repeated here. Nothing was downloaded.

## How to read "verified"

- **Y** = I opened the official record or page and read the licence/format there (WebFetch, curl, or the DataCite API record of the DOI).
- **S** = confirmed only through a search-result snippet of the official record; the page itself would not open for me (403, bot wall, or redirect loop).
- **N** = UNVERIFIED. Treat the cell as a lead, not a fact.
- Coverage share is of total world land (about 149 M km², Antarctica 14.2 M km² and Greenland 2.2 M km² included). Country areas are standard figures from memory and are rounded; they are not from the sources.
- "UNVERIFIED" in a cell means I could not check it.

## Table

| Field | Source | Licence | Resolution | Coverage share | Size | Login | URL | Verified | Note |
|---|---|---|---|---|---|---|---|---|---|
| Ice at LGM (N Hemisphere, 17 time slices) | Batchelor et al. 2019, Nat. Commun. 10:3713 | CC BY 4.0 (Apollo record, DataCite). Correction, 7 November: the Apollo record is the article; the shapefiles on OSF 7jen3 have no licence | Polygons, ice margins; scale UNVERIFIED | N Hemisphere ice sheets only: about 10-12% of world land lay under LGM ice (my estimate, UNVERIFIED) | Apollo lists 4 files (0.25-4.3 MB). I could not tell shapefiles from article PDFs. | No | https://doi.org/10.17863/CAM.56359 (redirects to https://www.repository.cam.ac.uk/handle/1810/309263) | Y for licence; N for shapefile files | Not on PANGAEA. Best hypothesis-based global-NH set; the LGM slice is one of 17. Check the zip contents before relying on it. |
| Ice at LGM (North America) | Dalton et al. 2020, QSR 234:106223 | UNVERIFIED (paper is accepted-manuscript open access) | Polygons, 36 isochrones (shapefile and PDF) | Laurentide and Cordilleran: about 7% of world land | Search snippet says about 13 MB `Dalton_2020_NA_IceSheets.zip` | UNVERIFIED | Paper: https://ore.exeter.ac.uk/repository/handle/10871/122545 ; GIS: https://theghub.org/resources/4878/supportingdocs (returned 404 for me) | N | GIS link did not work. Radiocarbon-based, best for NA deglaciation. |
| Ice at LGM (Eurasia) | DATED-1, Hughes et al. 2016, Boreas, doi:10.1111/bor.12142 | UNVERIFIED | Polygons, time slices | Eurasian ice: max 5.5 M km² at about 21 ka, about 3.7% of world land (from the paper's summary in a search snippet) | UNVERIFIED | UNVERIFIED | https://www.sciencedirect.com/science/article/pii/S0012825216303348 (403) | N | No data download link found. Supplementary to the paper, probably on Wiley. |
| Ice at LGM (global, maximum limits) | Ehlers, Gibbard and Hughes 2011/2018 (INQUA). Mirror: CRC 806 "LGM Glaciation Extends" | Not stated on the CRC page; Elsevier terms UNVERIFIED | Shapefile; scale not stated | Global LGM and per-region limits | Not stated | No (CRC page), but it says "currently offline" | https://crc806db.uni-koeln.de/maps/show/6 ; Elsevier booksite is decommissioned | Y for page; N for data | The shapefiles are claimed, no working link seen. Do not ship without a licence. |
| Ice at LGM (global thickness/extent grids) | PaleoMIST 1.0, Gowan et al. 2021, Nat. Commun. | Paper CC BY 4.0. Zenodo plot record CC BY 4.0 | Grids; resolution UNVERIFIED | Global, 80 ka | Plot archive 53 MB (do not download); the ice-grid files are separate and not located | No (Zenodo) | https://zenodo.org/records/4559963 ; https://doi.org/10.1038/s41467-021-21469-w | Y for the plot record; N for the ice grids | Global, includes Antarctica and Greenland. Gridded thickness would suit 3-5 km cells best, but I did not find the file. |
| Permafrost extent (fraction) | ESA Permafrost_cci v5.0 NH, CEDA | ESA CCI Permafrost terms; "Permitted use: Any", open access, citation required | 1 km, annual 1997-2023 | N Hemisphere 25-85 N: about 15% of NH exposed land is permafrost per Obu 2019 | 28 files, 318 MB total (per-year NetCDF, so one file is much smaller than the total) | No | https://catalogue.ceda.ac.uk/uuid/d235665772ec4b558e9a89ac85595e71/ | Y | Fraction and uncertainty only, no ground-ice field. Current climate, not 1318; a cold-period proxy only. |
| Permafrost (Antarctica) | Permafrost_cci Antarctica v5.0, CEDA | Same ESA CCI terms | 1 km | Antarctica ice-free parts | 27 files, 12.9 MB total (DataCite) | No | https://doi.org/10.5285/25447c253cbe435b937347756fe47146 | S | Small. |
| Permafrost ground temperature, zonation | Obu et al. 2019, PANGAEA 888600 | CC BY 3.0 | 1 km, GeoTIFF; zonation as shapefile; EPSG:3995 | NH land to 25 N; permafrost about 15% of it | Not stated; 40 data points listed | No | https://doi.org/10.1594/PANGAEA.888600 | Y | The same as CCI, older version, simpler to use. |
| Ground ice and permafrost zones | Brown et al. 1997, IPA Circum-Arctic Map v2 (NSIDC GGD318) | No licence named; "must cite" only | Shapefile from 1:10 M paper map; grids at 12.5 km, 25 km, 0.5 degree | N Hemisphere 20-90 N: permafrost about 22-25% of NH land (from memory, UNVERIFIED) | Not stated | Not mentioned; FTP | https://nsidc.org/data/ggd318/versions/2 | Y | Only open source of ground-ice abundance classes. Coarse, old, and covers the upper 20 m. Fine for 3-5 km. |
| Loess and aeolian deposits (Europe) | Lehmkuhl et al. 2021, ESR 215:103496, GFZ Data Services | CC BY-NC 4.0 (DataCite record). The CRC page shows "CC BY-NC". Non-commercial. | Shapefile compiled from national maps of 27 countries; 6 domains, 17 subdomains | Europe only, about 1.5-2% of world land (my estimate) | Not stated; `Loess_domains.7z` | No | https://doi.org/10.5880/SFB806.58 ; mirror https://crc806db.uni-koeln.de/dataset/show/geodata-of-european-loess-domains1608047013 (said "currently offline") | Y for licence; N for file size | Non-commercial licence conflicts with a possible Mod DB release, check whether a free mod counts. LGM time slice. |
| Alluvial fill and fluvial deposits (Europe, LGM) | Lehmkuhl et al., CRC 806, doi:10.5880/SFB806.57 | UNVERIFIED (DataCite lists none) | Shapefile | Europe | UNVERIFIED | No | https://crc806db.uni-koeln.de/dataset/show/geodata-of-alluvial-fill-and-fluvial-deposits-in-europe1608046618 | N | Only a search snippet and a DataCite stub. |
| Loess (global) | Li et al. 2020, ESR 201, "Loess genesis and worldwide distribution" | UNVERIFIED | Maps in the paper; no downloadable dataset found | About 5-10% of world land is loess-covered per the literature (from memory, UNVERIFIED) | - | - | https://tuprints.ulb.tu-darmstadt.de/id/eprint/17375 (bot wall, not opened) | N | No open vector file found. GUM and GLiM remain the only global loess polygons. |
| Floodplains | GFPLAIN250m, Nardi et al. 2019, Sci. Data 6:180309 | CC BY 4.0 (figshare record via DataCite) | 250 m, GeoTIFF and ESRI ASCII, WGS84 | Global, SRTM-derived (about 60 N to 56 S, so no high north) | 81.7 MB for the whole record; zipped per continent | No | https://doi.org/10.6084/m9.figshare.6665165 | Y for licence and size; the figshare page itself returned 403 | Geomorphic floodplain, not mapped alluvium; flags every river valley, so it overstates. Floodplain share of land UNVERIFIED. |
| Karst and carbonate | WOKAM, BGR/IAH/KIT/UNESCO 2017, DOI 10.25928/b2.21_sfkq-r406 | BGR terms with attribution "WHYMAP WOKAM, (C) BGR Berlin, IAH Reading, KIT Karlsruhe, UNESCO Paris 2017" (GDI-DE record snippet); no standard open licence | Shapefile, 1:25 M, EPSG:4326 | Global. Carbonate outcrops about 15% of ice-free land (from memory, UNVERIFIED) | UNVERIFIED | No | https://gdk.gdi-de.org/geonetwork/srv/api/records/473d851c-4694-4050-a37f-ee421170eca8 ; map service https://services.bgr.de/agswa/rest/services/grundwasser/whymap_wokam/MapServer | S (map service answered 200; the record page timed out) | Built on GLiM, so it adds almost nothing beyond GLiM carbonate classes. The only new thing is the aquifer-type split. |
| Volcanic deposits | GLiM volcanic classes (already checked) plus Smithsonian GVP | GVP: non-commercial use, commercial use needs written permission (search summary of GVP terms; terms page returned 403 to me) | Points (about 1,200 Holocene volcanoes) | Points only | Small | No | https://volcano.si.edu/gvp_termsofuse.cfm | S | No open global polygon map of Quaternary volcanic fields found. Use GLiM `vb`/`va`/`vi`/`py` for fields and GVP points only as tests. |
| Playas and salt pans | Natural Earth World Playas, via Stanford PURL kj615fy0064 | Public domain, no restrictions | 1:50 M polygons (a 1:10 M version exists) | Global but "a sampling of major" playas; extent in the record ends at about 118 W to 140 E | Not stated; Natural Earth 10 m set is a few MB | No | https://purl.stanford.edu/kj615fy0064 | Y | Only a global open salt-flat layer; incomplete. Real playas are small relative to a 3-5 km cell. |
| Playas (western US, detailed) | Safaee and Wang, PANGAEA 913152 | CC BY 4.0 (search snippet) | Landsat-8 derived polygons | Western US only | About 73 MB (do not download) | No | https://doi.pangaea.de/10.1594/PANGAEA.913152 | S | A model for later, no global product. |
| Evaporites, salt domes | USGS global potash and evaporite database (SIR 2010-5090 chapter 2) | US Government work (licence not read, UNVERIFIED) | Points: more than 900 deposit records | Points | UNVERIFIED | No | https://pubs.usgs.gov/publication/sir20105090S | S | Points only. No global salt-dome layer found. |
| Laterite, duricrust, weathering depth | None global beyond Shangguan and Pelletier | - | - | - | - | - | - | - | No open global laterite or duricrust map found. Regional only: Victoria duricrust lines, CC BY 4.0 (https://discover.data.vic.gov.au/dataset/regolith-regional-duricrust-lines-1-100000), covers about 0.15% of world land. |
| Surface geology, USA | USGS DS 425 surficial materials (1:5 M, 2009) and SGMC | DS 425: USGS publication, licence not read (UNVERIFIED); SGMC: UNVERIFIED | 1:5 M polygons; SGMC about 1:250 K to 1:1 M | Conterminous US, about 5.4% | Zip with geodatabase and shapefile, size UNVERIFIED | No | https://pubs.usgs.gov/ds/425/ ; https://mrdata.usgs.gov/geology/state/ (both returned 403 to my fetch) | S | The only one with a true surficial-materials theme at this scale. |
| Surficial geology, Canada | GSC Surficial geology map of Canada 1:5 M (doi:10.4095/295462) | Open Government Licence - Canada | 1:5 M vector; GDB, REST, WMS | Canada, about 6.7% | Not stated | No | https://open.canada.ca/data/en/dataset/317ebf06-e053-b10f-6092-bfe0c1af8703 | Y | Follows the GSC Surficial Data Model, so units map cleanly to till, glaciofluvial, glaciolacustrine, etc. |
| Surface geology, Australia | GA Surface Geology of Australia 1:1 M, 2012 | CC BY 4.0 (data.gov.au and GA eCat snippets); one old mirror says CC BY 3.0 | 1:1 M polygons, shp/gdb | Australia, about 5.2% | UNVERIFIED | No | https://ecat.ga.gov.au/geonetwork/eng/api/records/b804b59b-3ceb-3a7e-e044-00144fdd4fa6 | S | Regolith units are in GA's regolith products, not checked. |
| Geology, Great Britain | BGS Geology 625k (DiGMapGB-625), bedrock v5 and superficial v4 | Open Government Licence per data.gov.uk text; BGS metadata also has a "no reproduction" line, so terms are contradictory | 1:625 K shapefiles | GB, about 0.15% | UNVERIFIED | No | https://www.data.gov.uk/dataset/ef9d9b58-a0bd-4925-a65e-8a838de8bde0/digital-geological-map-data-of-great-britain-625k-digmapgb-625-bedrock-version-5 | S | The BGS download page URL I tried returned 404. Check terms. |
| Quaternary, Norway | NGU Løsmasser (N1000 simplified, regional, detailed) | NLOD (Norwegian licence for public data), attribution text required | 1:1 M, regional, detailed | Mainland Norway, about 0.2% | UNVERIFIED | No | https://data.norge.no/en/datasets/5ecb7eb2-aa47-3b03-a759-34188af6d957/losmasser-forenklet-kart-n1000 | S | Download at nedlasting.ngu.no (not opened). |
| Quaternary, Finland | GTK Superficial deposits 1:1 M | No licence found | 1:1 M vector | Finland, about 0.23% | UNVERIFIED | UNVERIFIED | https://egdi.geology.cz/record/basic/4bd00557-2c34-4c89-86b8-06c40a01080d | S | Metadata only, tagged "Free". |
| Soil map, Germany | BGR BÜK250 database v1.0 (and BÜK200, GÜK250 not confirmed) | Federal geodata terms (GeoNutzV) with attribution "BÜK250-Datenbank V1.0, © BGR 2024" | 1:250 K | Germany, about 0.24% | MS Access database, size UNVERIFIED | No | https://gdk.gdi-de.org/geonetwork/srv/api/records/6178e5a0-45d1-410b-9bd9-e5104d865899 | S | GÜK250 and BÜK200 licences not confirmed. |
| Quaternary, Europe | IQUAME 2500 (BGR/INQUA), 14 sheets | UNVERIFIED (BGR copyright; no reuse terms read) | 1:2.5 M; raster service, vector in progress | Europe, about 6.5% (Europe incl. western Russia, my estimate) | - | No for the raster map service | https://services.bgr.de/arcgis/rest/services/geologie/iqe2500_raster/MapServer | S | Revision is still under way. |
| Not checked | Sweden SGU, France BRGM, Netherlands TNO/PDOK, Poland PGI, Estonia, Lithuania, Russia, China, Brazil, India | UNVERIFIED | - | Russia about 11.5%, China 6.4%, Brazil 5.7%, India 2.2%, France 0.37%, Sweden 0.30%, Poland 0.21%, NL 0.02%, Estonia 0.03%, Lithuania 0.04% | - | - | Landing pages answered 200 or 403 only (infoterre.brgm.fr, www.sgu.se, www.pgi.gov.pl, geoportaal.maaamet.ee, www.dinoloket.nl, www.pdok.nl; geoportal.vsegei.ru, geosgb.sgb.gov.br, bhukosh.gsi.gov.in did not answer) | N | No licences read. I did not look for China, Russia, Brazil or India data in any depth. |

## Which fields are well covered by real data

Rough counts, not measured.

- **Ice at the LGM.** Well covered. Three regional reconstructions (Batchelor for the NH, Dalton for North America, Hughes DATED-1 for Eurasia) plus PaleoMIST for the globe. Margins are good to a few tens of km, which is finer than a 3-5 km cell needs. Thickness is a model result. Licences are open for Batchelor and PaleoMIST (correction, 7 November: Batchelor's article only; its shapefiles on OSF have no licence). The file locations for Dalton, DATED-1 and PaleoMIST ice grids still need confirming.
- **Permafrost extent.** Well covered for the north at 1 km, from satellite and reanalysis (CCI, Obu). This is the present climate, not 1318 and not LGM. Southern Hemisphere has Antarctica only and no mountain permafrost outside it. Ground ice has one old coarse source (Brown 1997, 12.5-25 km), enough for classes only.
- **Floodplains.** Covered globally at 250 m, but as terrain-derived flat valley floor, so it is a mask, not mapped alluvium. It is usable at 3-5 km as a fraction per cell. It stops at about 60 N.
- **Loess.** Good only for Europe (Lehmkuhl 2021, but non-commercial). Elsewhere GUM and GLiM carry the loess class. No new global polygon set was found.
- **Karst and carbonate.** Covered globally, but WOKAM derives from GLiM, so it is not independent evidence.
- **National surficial maps.** The US, Canada, Australia and parts of Europe cover about 18% of world land between them, at 1:1 M to 1:5 M. Russia, China, Brazil and India (about 26% of land) are unchecked.

## Which fields are mostly guesswork

- **Volcanic deposits and tephra.** Points (GVP) and GLiM polygons only. Tephra blankets (Andisols) come from soil rules, not from data.
- **Evaporites and salt.** A sampled playa layer and potash points. Place by aridity and endorheic basin rules.
- **Laterite, duricrust, deep weathering.** No global map. Rules from climate (warm and wet, old stable shields) plus Shangguan and Pelletier depth.
- **Alluvium thickness, loess thickness outside Europe, glacial till thickness.** Extent exists in places, thickness does not. Pelletier and Shangguan are the only global thickness numbers.

## Resolution the real data supports

| Field | Supported resolution |
|---|---|
| LGM ice margin | Polygons, finer than 3 km |
| Permafrost fraction | 1 km |
| Ground ice | 12.5-25 km |
| Floodplain mask | 250 m (a fraction per cell) |
| Loess, Europe | Polygons at about 1:2.5 M, so 5 km is the honest floor |
| Carbonate / karst | 1:25 M (WOKAM) or GLiM's roughly 1:3.75 M, so 5 km is generous |
| National surficial maps | 1:1 M gives about 1-2 km; 1:5 M gives about 5-10 km |
| Playas | 1:50 M, no better than 25 km as a source |

A 3-5 km grid is at or finer than most of the real data except permafrost fraction and floodplains. Choosing a finer grid than 3 km adds only the generator's own variation.

## Licence flags for shipping

- Non-commercial: Lehmkuhl loess (CC BY-NC 4.0), GVP (non-commercial).
- Unclear or contradictory: WOKAM (BGR terms), BGS 625k (OGL plus a "no reproduction" line), Ehlers/INQUA shapefiles, Brown 1997 (cite only), GTK, IQUAME.
- Clean open: Batchelor (correction, 7 November: the article only; the shapefiles on OSF have no licence), PaleoMIST (CC BY 4.0), Obu (CC BY 3.0), ESA CCI Permafrost ("Any" use with citation), GFPLAIN250m (CC BY 4.0), Natural Earth playas (public domain), GSC surficial (OGL-Canada), GA surface geology (CC BY 4.0, from snippets).

## Not done

- No file contents were opened, because nothing was downloaded. File lists, sizes marked UNVERIFIED and shapefile contents still need a check before baking.
- Pages that blocked me (403, bot wall or redirect loops): volcano.si.edu, mrdata.usgs.gov, pubs.usgs.gov, pmc.ncbi.nlm.nih.gov, nature.com, figshare.com, sciencedirect.com, tuprints.ulb.tu-darmstadt.de, bgr.bund.de, whymap.org.
