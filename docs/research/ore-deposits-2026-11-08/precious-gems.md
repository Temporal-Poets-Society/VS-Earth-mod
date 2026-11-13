# Precious metals, gems and industrial minerals: deposit catalogue (decision 0065)

Group `precious-gems` of [decision 0065](../../decisions/0065-ores-at-their-real-size.md). Its heat-map layer codes are
Au, diamond, corundum, emerald, olivine, garnet, lapis, topaz, tourmaline, beryl, spinel, C_graphite, F, SiO2 and U.
It was built on 8 November 2026 on branch `claude/ore-precious-gems`.

## What is built

| File | What |
|---|---|
| `assets/earth/config/ore-deposits-v1/precious-gems.json` | The catalogue, about 147 KB minified. It has 310 deposits and 7 areas (the Witwatersrand goldfields), 113 sources, grade bands for all 15 layer codes plus Cu (for by-product chalcopyrite), and `oreMetal`. |
| `tools/ore-data/precious-gems/deposits-gold.csv` | 129 lode gold rows: orogenic, Carlin, epithermal and intrusion-related, including the medieval European, Nubian and Arabian mines. |
| `tools/ore-data/precious-gems/deposits-placers.csv` | 22 placer gold blankets (`earth-placergold`). |
| `tools/ore-data/precious-gems/deposits-gems.csv` | 96 rows: diamond pipes and alluvial fields, emerald, lapis, ruby and sapphire, spinel, peridot, garnet, topaz, tourmaline and beryl. |
| `tools/ore-data/precious-gems/deposits-industrial.csv` | 63 rows: 30 uranium, 13 fluorite, 12 graphite and 8 quartz. |
| `tools/ore-data/precious-gems/areas.json` | The 7 Witwatersrand goldfields as polygons with reef beds. |
| `tools/ore-data/precious-gems/sources.json`, `sources-industrial.json` | The catalogue's `sources[]`. |
| `tools/ore-data/precious-gems/extra-evidence.json` | 94 heat-map-only district points with no catalogue body. |
| `tools/ore-data/precious-gems/build_catalogue.py` | Checks every row and writes the catalogue, `evidence.geojson` and `validation.json`. |
| `tools/ore-data/precious-gems/fetch_reference.py` | Downloads the USGS MCS 2026 gold and gemstone chapters into `rawdata\ore\precious-gems\` and writes `reference-mcs2026.json`. |
| `tools/ore-data/precious-gems/test_bake.py`, `compare_heatmaps.py` | A before/after bake of this group's codes into a scratch folder, plus the comparison images and `heatmap-comparison.json`. |
| `tools/ore-data/precious-gems/heatmap-overrides.json` | Drops nothing; the file gives the reasons. |
| `tools/ore-data/precious-gems/proposed-coordinate-corrections.csv` | Five wrong USGS positions, proposed for the shared corrections table. |
| `tools/ore-data/precious-gems/ATTRIBUTION.txt` | Sources and licences. |

To rebuild, run these from the Earth root, using `.venv\Scripts\python.exe -I`:

1. `tools/ore-data/precious-gems/fetch_reference.py`. This needs network access and is only needed for the reference check.
2. `tools/ore-data/precious-gems/build_catalogue.py`. The build stops with exit code 1 on any hard error.
3. For a test bake, `test_bake.py <scratch> before` and then `<scratch> after`.
4. Then `compare_heatmaps.py <scratch>/before/ore-heatmaps.json <scratch>/after/ore-heatmaps.json docs/research/ore-deposits-2026-11-08/precious-gems`.

## Sources and licences

[ATTRIBUTION.txt](../../../tools/ore-data/precious-gems/ATTRIBUTION.txt) lists them. Every row names its source in `src`. There are three kinds of source:

- **Public domain (USGS).**
  - Major Mineral Deposits of the World (Schulz and Briskey 2005) gives the positions and sizes of many lode deposits.
  - Mineral Commodity Summaries 2025 and 2026 (gold and gemstones) are used for validation.
  - Berger et al. 2014 covers sediment-hosted gold.
  - Professional Paper 1802 chapters H (fluorine) and J (graphite).
  - The Minerals Yearbook quartz chapter.
  - The USGS/AGS Afghanistan assessments, for Sar-e-Sang.
- **Published figures used as facts (`q` B or C).** Each is the total tonnage and grade, or the contained metal, of a named deposit. The figures come from three places:
  - national surveys, including GeoSphere Austria, the Czech and Slovak surveys, CPRM, SGC Colombia, GSI, BGS, GEUS, IGME, Bayerisches LfU, Yukon GS, Montana BMG, Arkansas GS, Wyoming SGS, NMBGMR and Ontario GS;
  - company reserve, resource and production statements;
  - peer-reviewed compilations, including Goldfarb et al. 2005, Frimmel 2014, Phillips and Hughes 1996, Wall et al. 2004, Deng et al. 2020, Klemm and Klemm 2013, Carlile and Mitchell 1994, and papers in Gems and Gemology.

  Only the numbers are used, never text or tables.
- **Natural Earth (public domain)** 1:10m countries, used for the land and country check.

Not used: Mindat, research-only GEMDATA and DIADATA rows, and any NC, ND or SA data. The few figures where I am least sure carry `q` C and name the estimated fields in `est`.

## Decisions

### Scope against the other groups

- **Gold kept here:** lode gold (orogenic, Carlin, epithermal and intrusion-related), placer gold and the Witwatersrand.
- **Gold left to base-metals:**
  - porphyry Cu-Au, such as Grasberg, Cadia, Bingham and Oyu Tolgoi;
  - VMS gold;
  - Ag-dominant epithermal veins, such as Comstock and Pachuca.

  Base-metals now gives those bodies a `gold` share, using this group's Au bands.
- **Each deposit once:** the coordinator's rule of 8 November puts every real deposit in exactly one catalogue, owned by the group of its main commodity by value. Co-products are `ores` shares inside that one body. So:
  - **Olympic Dam** is left out here. Base-metals owns it and adds the uranium share.
  - **Bayan Obo** is left out here. Iron-ferro owns it, and its fluorite (about 9 % of the REE-Fe ore, about 135 Mt CaF2) should be a share there.

  Both stay in the heat maps as district points (`pgd:olympic-dam-u` at 0.95 and `pgd:bayan-obo-f` at 0.9).
- **Gold bodies with by-product copper** carry `chalcopyrite` as a share here:

  | Deposit | Cu grade | Chalcopyrite share |
  |---|---:|---|
  | Telfer | 0.12 % | 0.1 |
  | Mount Morgan | 0.72 % | 0.1 |
  | Zijinshan | 0.45 % | 0.3, in the deeper zone (`bottom`) |

  - **Telfer and Mount Morgan:** gold is the main commodity by value.
  - **Zijinshan:** about 300 t Au is worth more than its copper, so this catalogue owns it and base-metals' Zijinshan copper body is the duplicate to remove.
  - **How the copper is tiered:** the copper takes its tier from the top-level `oreMetal` (`chalcopyrite` → `Cu`), each body's `grades` and base-metals' Cu band (0.4 / 1.2 / 3.0 %). This is the runtime's optional mechanism.
- **Uravan mineral belt:** iron-ferro gave this body to this group, because uranium is its main value. Its vanadium is a 0.6 `vanadinite` share beside 0.4 `uranium`.
  - **Why 0.6:** the carnotite-roscoelite ore holds about five times as much V2O5 (about 1.5 %) as U3O8 (0.25 %).
  - **How the vanadium is tiered:** through `oreMetal` (`vanadinite` → `V`), `grades` and iron-ferro's V band (0.3 / 0.8 / 1.4 % V2O5).
  - **If Geology Additions is not installed:** the runtime gives the share to uranium.
- **Gold bodies near base-metals' deposits:** Rosia Montana, Barza-Brad, Sacaramb and Mount Morgan sit within a few km of base-metals' porphyry bodies (Rosia Poieni, Musariu, Bolcana). These are distinct deposits, not duplicates. Base-metals' 5 km rule leaves the gold to these bodies.
- **Witwatersrand uranium:** I gave the goldfields no uranium share. An area's bed has one code, and the reefs' 0.01–0.03 % U3O8 would be a second bed at the same depth. That is optional per the coordinator, and left for later.

### Tonnage, grade and units

- **Tonnage** is pre-mining endowment: past production plus reserves plus resources, as 0065 asks. Mining before 1318 is not subtracted. That is visible in Kremnica, Jílové, the Tauern, Nubia and Bambuk, which were worked in or before the Middle Ages.
- **Grade units:**

  | Layer | Unit |
  |---|---|
  | Au | g/t |
  | U | % U3O8 |
  | C_graphite | % graphitic carbon |
  | F | % CaF2 |
  | SiO2 | % SiO2 |
  | Gems (incl. diamond) | ct/t |

  Gem grades are carats of gem-quality stone per tonne of rock, so 1 Mt at 1 ct/t holds 1 Mct.
- **Ungraded rows:** 12 rows have no grade, because the source gives none or gives it in another unit. They are emery, industrial garnet, the Åheim olivine dunite, lapis, and some quartz and fluorite. The gem blocks are "potential" variants (low, medium, high), so a null grade leaves the potential to the runtime default.

### Grade bands

Bands are in the catalogue's `gradeBands`. Below "medium" is poor.

| Layer | Unit | medium | rich | bountiful | Why |
|---|---|---|---|---|---|
| Au | g/t | 1 | 4 | 10 | Open-pit bulk ore is around 1 g/t, typical underground ore around 4–8 g/t, and bonanza veins 10 g/t and up. Placers at 0.2–1 g/t are poor, which is real. |
| U | % U3O8 | 0.1 | 0.5 | 2 | Granite and sandstone ores are 0.05–0.2 %, unconformity ores 1–20 %. |
| diamond | ct/t | 0.3 | 1 | 3 | Most pipes are 0.2–1 ct/t. Argyle and Jwaneng are above 1, and Argyle's early ore around 3. |
| emerald | ct/t | 1 | 5 | 50 | Schist-hosted belts are low; Colombian pockets are very rich. |
| corundum | ct/t | 0.1 | 1 | 10 | Marble-hosted ruby is low; gem gravels are richer. |
| spinel, olivine, topaz, tourmaline, beryl | ct/t | 0.5 | 2 | 10 | Pegmatite and xenolith gem yields. |
| garnet, lapis | ct/t | 1 / 1 | 5 / 5 | 20 / 20 | Garnet gem gravels and lapis veins are comparatively rich. |
| C_graphite | % | 5 | 10 | 20 | Flake deposits are 5–20 %. Vein graphite (Borrowdale, Sri Lanka) is over 90 %. |
| F | % CaF2 | 20 | 40 | 60 | Acid-grade feed is about 35 % or more. |
| SiO2 | % | 95 | 99 | 99.9 | Glass sand is 95 % or more; high-purity vein quartz reaches 99.9 % or more. |

### Ore codes

| Layer | Codes used |
|---|---|
| Au | `gold`, the vanilla child of `quartz`, so the blocks are `ore-*-quartz_nativegold-*`, and `earth-placergold` for placers |
| diamond | `diamond` |
| emerald | `emerald` |
| corundum | `corundumruby`, `corundumsapphire`, and vanilla `corundum` for emery |
| spinel | `spinelred` |
| olivine | `peridot` for gem peridot, `olivine` for dunite |
| garnet | the six `garnet*` codes, by the species each locality gives |
| topaz, tourmaline, beryl | the Earth gem codes (`topaz*`, `tourmaline*`, `beryl*`) |
| lapis | `lapis` |
| C_graphite, F, SiO2, U | `graphite`, `fluorite`, `quartz`, `uranium` |
| (by-product) | `chalcopyrite` in Telfer, Mount Morgan and Zijinshan, tiered by copper grade, and `vanadinite` (Geology Additions) in the Uravan belt, tiered by % V2O5. Both go through `oreMetal`. |

A pegmatite or gravel that carries more than one gem gets shares, such as aquamarine, tourmaline and topaz at Minas Gerais or the Ratnapura illam. Each carried gem also gets a heat-map point for its own layer, at 0.9 of the main peak.

### Shapes

| Shape | Count | Used for |
|---|---|---|
| `veins` | 124 | Orogenic and epithermal lode swarms, emerald and lapis veins, vein graphite, fluorite veins |
| `lens` | 87 | Carlin and shear-zone bodies, unconformity uranium, flake graphite, Ivittuut, and San Carlos peridot (xenoliths in a basalt flow, so a flat lens in bedrock rather than a gravel) |
| `stock` | 59 | Kimberlite and lamproite pipes, bulk epithermal and intrusion-related gold |
| `blanket` | 40 | Placers, alluvial diamond and gem gravels |

No `dome` is used.

A lens's `widKm` is its width in plan, not its extent down the dip, whatever the dip. This matches how the runtime reads shallow lenses such as Kuranakh, Shuiyindong and Granny Smith. For steep lenses the runtime takes the extent down the dip from `topM`..`baseM`, which is what I gave.

### Witwatersrand as areas

- **Areas:** the seven goldfields are areas:

  | Goldfield | Gold (t) |
  |---|---|
  | West Rand | 5,000 |
  | Central Rand | 12,500 |
  | East Rand | 11,000 |
  | Far West Rand | 22,000 |
  | Klerksdorp | 10,500 |
  | Free State | 17,000 |
  | Evander | 3,000 |
  | **Total** | **81,000** |

  The total is production to about 2010 plus remaining resources; Frimmel 2014 gives at least 80,000 t for the basin.
- **Outlines:** simplified outlines of the mined and subcrop ground, good to about 2–5 km.
- **Reefs:** flat reef beds at the median mined depth, 500–2,200 m.
  - The Far West Rand carries two reefs: the Ventersdorp Contact Reef at 1,500 m and the Carbon Leader at 2,200 m.
  - Klerksdorp and the Free State also carry two reefs each, at different depths.
- **Grades:** the historic average head grades, 7–16 g/t.
- **Why flat:** the real reefs dip at 20–90°. A flat bed cannot follow them, and the median depth keeps the gold at the depth where most of it was mined.
- **Cover:** `cover` is not authored. The build derives it so that cover × area × thickness × density equals the tonnage, giving 0.15–0.66.

### Placers

- **Gold:** 22 placer gold fields are `blanket` bodies of `earth-placergold` in Cenozoic gravels, with `topM` of 30 or less, so 0060 puts them in alluvial or outwash cover.
  - **North America:** Klondike, Cariboo, Fairbanks, Nome, Sierra Nevada Tertiary gravels.
  - **Australia and New Zealand:** Victoria, Otago.
  - **Russia:** Lena, Kolyma, Aldan, Urals.
  - **Africa:** Bambuk, Bure, Lobi, Akan, Adola, Nubia.
  - **South America:** Antioquia, Minas Gerais, Tapajós, Madre de Dios.
  - **Spain:** Las Médulas.

  Placer contents are endowment, so the Sierra and Kolyma figures hold several thousand million tonnes of gravel.
- **Gems:** alluvial diamond and gem gravels are also `blanket` bodies, with the hard-rock gem codes.
  - **Diamond fields:** Tshikapa, Lunda, Cuango, Kono, Akwatia, Carnot, the Namibian and Namaqualand coasts, the Vaal, Golconda-Krishna, Diamantina and Juína.
  - **Gem gravels:** Mogok byon, Ratnapura illam, Montepuez, Chanthaburi and the Bohemian pyrope gravels.
- **Layers:** every blanket names its layers. This is the contract addition of 8 November (`claude/ore-rework` 50420af): a body's `layers` replaces 0060's per-code table.
  - Gold placers and gem gravels take `["alluvial", "outwash"]`, not the blanket default, which also includes laterite and saprolite.
  - The Sperrgebiet, Elizabeth Bay and Namaqualand beach and deflation diamonds, and Nome's beach placers, add `"cover"`, for beach and dune sand.

  So the stones sit in the gravel itself, as in the real fields. Without `layers`, 0060 would have put the hard-rock gem codes on the bedrock surface under the gravel. A separate placer gem block is no longer needed for placement; it would only change how the block looks.

### Coordinates and islands

- **Estimated positions:** positions are the mine or district centre. Those not taken from a survey or company source are flagged in `est`.
- **Zabargad:** Zabargad (St John's Island, peridot) is missing from Natural Earth's 1:10m countries. It is whitelisted in the build after a hand check (23.60 N 36.20 E, Egyptian Red Sea, about 4.5 km²). Its evidence point carries no `country`, so the bake's country check keeps it.
- **Wrong USGS positions:** five Major Deposits positions are wrong by 100–1,300 km:
  - Jiaodong is placed in Shaanxi;
  - Kelian is placed in Jakarta;
  - Veladero is placed in La Rioja;
  - Pascua is placed in the coastal ranges;
  - Berezovsk is about 105 km off.

  My rows use the right positions. The corrections for the bake's shared table are in `proposed-coordinate-corrections.csv`, for the coordinator.

### Heat-map evidence and overrides

- **Evidence points:** `evidence.geojson` has one point per deposit and per layer carried, tiered by contained amount, plus the seven goldfield polygons with a district point each, plus the 92 extra district points.

  | Tier | Peak | Radius |
  |---|---|---|
  | major | 0.95 | 75 km |
  | large | 0.85 | 60 km |
  | other | 0.75 | 45 km |

  - Major is at least 500 t Au, 100 Mct diamond, 5 Mct of another gem, 0.1 Mt U3O8, or 10 Mt graphite or CaF2.
  - The radius grows to half the deposit's length plus 25 km, up to 150 km.
- **Extra district points:** the 94 points cover districts that are real but have no tonnage I could cite. Examples: Siberian placers outside the catalogue, the Ural and Brazilian gem pegmatites, the Madagascar sapphire fields, Chinese fluorite and graphite, and the Kazakh uranium districts.
- **Overrides:** `heatmap-overrides.json` drops nothing. The comparison below shows the old maps miss places rather than paint wrong ones. MRDS "Silica" records stay for SiO2, because vanilla quartz also takes sandstone and quartzite hosts.

## Validation

### Build checks

- **Errors:** 0.
- **Hard checks, all passing:**
  - unique ids;
  - known source, code, shape, age and `q`;
  - shares summing to 1;
  - grade unit equal to the band unit;
  - body volume no more than its footprint (fill ≤ 1);
  - placers Cenozoic blankets with `topM` ≤ 30;
  - every point on land and inside its country with a 0.1° margin, using the bake's own `CountryCheck`.
- **Warnings: 5.**
  - 4 are fluorite with a Precambrian host: Ivittuut, Vergenoeg, Witkop and Okorusu. 0060 keeps fluorite in beds, so the runtime uses a similar bed host or drops the body.
  - 1 is Zabargad (island, hand-checked).
  - Three earlier coastal warnings (Gosowong, Skaland and Nome) were fixed by moving those points 3–8 km onto land.
  - The 19 earlier warnings for hard-rock gem codes on blankets went away with `layers`.
- **Ages:** by host age the deposits are 124 Precambrian, 77 Cenozoic, 48 Mesozoic, 43 Paleozoic, 8 Permian, 8 Carboniferous and 2 Devonian. All placers are Cenozoic. The runtime does not read `age` (see the runtime note); 0060's layer gate uses Earth's own column, so ages here are documentation and drive only these warnings.

### Gold against published figures

- **Catalogue gold:** 164,862 t in all, split into:
  - Witwatersrand: 81,000 t;
  - lode: 71,257 t;
  - placer: 12,605 t.
- **World coverage:** the world reference is World Gold Council above-ground stocks (216,265 t at the end of 2024) plus USGS MCS 2026 reserves (66,000 t), so 282,265 t. Coverage is 0.58.

  The rest is in three places, as 0065 expects:
  - porphyry and VMS gold, carried by base-metals;
  - thousands of small deposits, left to the background;
  - resources beyond reserves at mines not in the catalogue.

| Country | Catalogue t | Cumulative production t (rounded) | MCS 2026 reserves t | Coverage | Why |
|---|---:|---:|---:|---:|---|
| South Africa | 81,590 | 53,000 | 5,000 | 1.41 | Witwatersrand resources beyond reserves (Frimmel 2014) |
| United States | 12,351 | 24,000 | 3,000 | 0.46 | Bingham and other porphyries are base-metals'; many small districts |
| Russia | 11,905 | 18,000 | 12,000 | 0.40 | Many mid-size Siberian deposits are heat-map only |
| Australia | 9,971 | 15,000 | 13,000 | 0.36 | Cadia, Olympic Dam and Telfer copper-gold sit partly in base-metals |
| Canada | 7,088 | 12,000 | 3,200 | 0.47 | |
| Uzbekistan | 5,280 | 5,000 | 2,200 | 0.73 | Muruntau |
| China | 4,930 | 10,000 | 3,200 | 0.37 | Hundreds of small Jiaodong and Xiaoqinling bodies |
| Ghana | 4,360 | 5,000 | 1,000 | 0.73 | |
| Brazil | 3,031 | 3,500 | 2,500 | 0.51 | |
| Peru | 2,544 | 5,000 | 2,200 | 0.35 | Porphyry gold left to base-metals |
| Mexico | 969 | 3,500 | 1,400 | 0.20 | Ag-Au veins (Fresnillo, Guanajuato, Pachuca) and Peñasquito are base-metals' |
| Kazakhstan | 775 | 1,500 | 2,300 | 0.20 | Reserves are mostly in Cu-Au and VMS ores |
| Indonesia | 705 | 3,500 | 3,600 | 0.10 | Grasberg (about 3,000 t) is base-metals' porphyry |

The cumulative production figures are rounded, commonly quoted values (`q` C, about ±20 %). They are in `GOLD_CUMULATIVE_T` of the build, and `validation.json` → `reference` has the whole comparison.

The coverage ratios follow one rule: they are high where gold is in its own deposits (South Africa, Uzbekistan, Ghana) and low where most gold is a by-product of copper (Indonesia, Mexico, Kazakhstan, Peru). That is the boundary with base-metals, not missing data. The merged catalogues will be close to 1 in those countries.

### Diamonds by country

- **Catalogue diamonds:** 6,949 Mct of total endowment, gem and industrial. That is the order of all diamonds mined since 1870 plus today's reserves.
- **Production and reserves:**
  - Kimberley Process 2023: 111.5 Mct of rough a year.
  - USGS MCS 2026 gem diamond reserves: more than 2,000 Mct.
  - The catalogue figure is about 60 years of today's output plus the reserves, and diamond mining is about 150 years old.

| Country | Catalogue Mct | MCS gem production 2024 Mct | MCS gem reserves Mct |
|---|---:|---:|---:|
| Botswana | 1,521 | 12.7 | 250 |
| Russia | 1,345 | 20.9 | 750 |
| Congo (Kinshasa) | 900 | 1.96 | 150 |
| Australia | 870 | – | – |
| South Africa | 764 | 2.14 | 87 |
| Angola | 760 | 12.6 | 150 |
| Canada | 316 | 13.3 | 110 |
| Zimbabwe | 150 | 0.53 | 56 |
| Ghana | 90 | 0.33 | – |
| Namibia | 85 | 2.32 | – |
| Sierra Leone | 56 | 0.46 | – |
| Brazil, CAR, Tanzania, India, Lesotho | 26, 25, 21, 14, 5 | small | – |

The country order matches the order of cumulative production, which goes Russia and Botswana, then DR Congo, Australia, South Africa, Angola, Canada. Three countries differ for clear reasons:

- **Australia:** its MCS gem figure is blank because Argyle (870 Mct, mostly industrial brown stones) closed in 2020.
- **Canada:** its catalogue figure is low next to its output, because Ekati, Diavik and Gahcho Kué are young and small in total carats.
- **Russia:** its MCS reserves are higher than the catalogue's Russia. Its pipes are carried at the published ALROSA reserves plus past output, and I did not add the large undeveloped resources.

### Uranium

- **Catalogue uranium:** 4,250 kt U3O8 in 30 deposits whose main value is uranium. Olympic Dam's about 2,675 kt (10.7 Gt at about 0.025 % U3O8) is now base-metals' uranium share, so the merged catalogues hold about 6,900 kt.

  | Country | kt U3O8 |
  |---|---:|
  | Canada | 984 |
  | Kazakhstan | 865 |
  | Namibia | 506 |
  | Niger | 414 |
  | Australia | 332 |
  | Russia | 330 |
  | Greenland | 269 |
  | United States | 216 |

- **Published total:** the OECD-NEA/IAEA Red Book 2022 gives about 7.9 Mt U of identified resources below USD 260/kgU, about 9.3 Mt U3O8. World production to date is about 3 Mt U.
- **Coverage:** with Olympic Dam, about 0.5. That is reasonable for 31 named deposits, because Kazakh roll fronts and many small sandstone deposits are in the evidence only.

### Largest entries

| # | Gold, t | Ore, Mt (all layers) | Diamonds, Mct |
|---|---|---|---|
| 1 | Far West Rand 22,000 | Sierra Nevada gravels (placer) 4,100 | Argyle 870 |
| 2 | Free State 17,000 | Kolyma placers 2,500 | Orapa 810 |
| 3 | Central Rand 12,500 | Free State goldfield 2,125 | Mbuji-Mayi 750 |
| 4 | East Rand 11,000 | Sperrgebiet coast (diamond) 2,000 | Jwaneng 702 |
| 5 | Klerksdorp 10,500 | Åheim dunite (olivine) 2,000 | Catoca 560 |
| 6 | Muruntau 5,280 | Paracatu 1,950 | Udachnaya 400 |
| 7 | West Rand 5,000 | Far West Rand 1,833 | Aikhal-Yubileynaya 315 |
| 8 | Evander 3,000 | Muruntau 1,760 | Cullinan 260 |
| 9 | Kolyma placers 2,500 | Yanacocha 1,600 | Venetia 176 |
| 10 | Porcupine 2,160 | Boddington 1,570 | Mir 175 |
| 11 | Golden Mile 2,160 | Namaqualand coast 1,500 | Internatsionalnaya 175 |
| 12 | Obuasi 1,960 | Lena goldfields 1,500 | Tshikapa 150 |
| 13 | Sukhoi Log 1,800 | Central Rand 1,389 | Marange 150 |
| 14 | Goldstrike 1,800 | East Rand 1,375 | Lunda Norte alluvials 150 |
| 15 | Lihir 1,760 | Klondike 1,240 | Lomonosov 150 |
| 16 | Pueblo Viejo 1,560 | Klerksdorp 1,152 | Finsch 150 |
| 17 | Lena goldfields 1,500 | Balama (graphite) 1,150 | Diavik 140 |
| 18 | Yanacocha 1,440 | Kvanefjeld (U) 1,010 | Nyurba-Botuobinskaya 130 |
| 19 | Donlin 1,407 | Victorian alluvials 1,000 | Ekati 120 |
| 20 | Sanshandao (Jiaodong) 1,400 | Vaal River alluvials 1,000 | Kimberley pipes 90 |

These match the usual rankings. Muruntau, the Golden Mile, Porcupine and Obuasi are the largest single lode systems, and Sukhoi Log is the largest undeveloped one. Argyle, Orapa, Jwaneng, Catoca and Udachnaya are the largest pipes by carats.

### Heat maps before and after

I baked twice with `tools/bake_ore_heatmaps.py --out <scratch> --report <scratch>` (`test_bake.py`). The "before" bake has no group evidence at all, which is today's maps. The "after" bake uses this group's `evidence.geojson` and `heatmap-overrides.json`. Neither run touched `ore-heatmaps-*.json`, the settings or the bake script.

**Old maps:** 45 catalogue deposits sat below 0.5 observed on the old maps. These were real, well-known places the old evidence missed:

| Kind | Deposits |
|---|---|
| Diamond | Gahcho Kué, Karowe, Banganapalle, Sperrgebiet |
| Uranium | Arrow, Kvanefjeld, Jáchymov, and four US sandstone belts (Gas Hills, Uravan, Ambrosia Lake, Smith Ranch), which MRDS covers only thinly after normalisation |
| Gold | Tauern, Jílové, Sacaramb, Barza-Brad, Olimpiada, Mayskoye, Buriticá, Kelian, Gosowong, Lobi, Otago, the Nubian wadis |
| Gems | Zabargad, San Carlos, Pyaung Gaung, Sapat, Val Malenco, Outokumpu, Yogo, Elba, Nuristan, Ovalle lapis, Wah Wah red beryl, Thomas Range, Pala, Oxford County, Amelia, Zschorlau |
| Quartz and other minerals | the Aar massif, Diamantina and Ouachita quartz, Idar-Oberstein, Borrowdale, the Marico fluorspar |

**New maps:** with the group's evidence, none is below 0.5.

| Code | Observed cells before → after | Cells at 0.5+ before → after | US share of observed weight before → after |
|---|---|---|---|
| Au | 13,667 → 13,752 | 3,685 → 3,756 | 0.154 → 0.152 |
| diamond | 1,207 → 1,252 | 389 → 412 | 0.018 → 0.017 |
| corundum | 319 → 351 | 57 → 70 | 0.282 → 0.260 |
| emerald | 111 → 118 | 27 → 32 | 0.034 → 0.030 |
| olivine | 103 → 118 | 22 → 27 | 0.366 → 0.367 |
| garnet | 567 → 590 | 51 → 61 | 0.492 → 0.463 |
| lapis | 20 → 26 | 7 → 10 | 0 → 0 |
| topaz | 116 → 140 | 24 → 31 | 0.102 → 0.133 |
| tourmaline | 163 → 186 | 22 → 35 | 0.135 → 0.173 |
| beryl | 360 → 396 | 34 → 48 | 0.187 → 0.194 |
| spinel | 56 → 64 | 10 → 12 | 0.134 → 0.108 |
| C_graphite | 1,172 → 1,188 | 260 → 266 | 0.197 → 0.193 |
| F | 2,020 → 2,032 | 406 → 418 | 0.251 → 0.248 |
| SiO2 | 2,779 → 2,793 | 350 → 359 | 0.371 → 0.368 |
| U | 2,287 → 2,356 | 348 → 385 | 0.448 → 0.429 |

The images are in [precious-gems/](precious-gems/), one per code. Each shows the before map above and the after map below, as the larger of observed and potential, with the catalogue deposits as blue dots. The full numbers, including every deposit's observed value before and after, are in `tools/ore-data/precious-gems/heatmap-comparison.json`.

**What the comparison shows:**

- **Additions, not corrections:** the old maps were not wrong where they painted. Their error was missing places, mostly outside the US and in the old mining districts of Europe, the Middle East and Africa. So the fix is added evidence, not dropped sources.
- **US weighting:** two codes stay US-heavy after MRDS survey normalisation:

  | Code | US share of observed weight | US share of the world's resources |
  |---|---:|---:|
  | U | 0.43 | below 0.05 |
  | garnet | 0.46 | — |

  For garnet, the US industrial garnet records (Gore Mountain, Idaho, Adirondacks) dominate. That is a setting for the bake, such as a stronger normalisation or a per-code cap for MRDS. I sent it to the coordinator rather than dropping MRDS here.
- **Cell counts:** these move little because the catalogue deposits are mostly in cells that already had some evidence. The change is in the peaks: +23 to +72 cells at 0.5 or more per code.

## What is left

- **Placer gem block (idea for the author):** with `layers`, alluvial diamonds and gems already sit in the gravel, as hard-rock gem ore blocks of the gravel's host. A block that looks like gem stones in gravel would only change their look.
- **Placer grades:** every gold placer is poor by the Au bands, which is real. Richer placer blocks would need bands per ore code in the runtime, as base-metals' notes also say for tin placers.
- **Coverage:** Russian, Chinese, Mexican and Central Asian lode gold is mostly in the background. A further pass could add about 40 mid-size deposits (100–300 t each) from national survey compilations.
- **Coordinates still estimated:** the coordinates of Kelian, Gosowong, La Herradura and several medieval mines are estimates (`est`). Kelian's position should be checked against a survey source before the USGS correction is applied.
