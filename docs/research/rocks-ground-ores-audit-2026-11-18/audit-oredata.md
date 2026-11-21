# Earth ore data audit: heat maps and deposit catalogue

Earth `main` at 1ac0f2d (v2.1.4), 18 November 2026. Read-only audit. Scripts, CSVs and console output are in
`scripts\oredata\`.
Country and region lookups use Natural Earth 1:10m admin-0 (`C:\VS mods\data\earth-raw\ore\coal-evaporites\ne_10m_admin_0_countries.zip`);
land and ice come from `preview/grids.npz`, as in the bake. Real-world shares are **approximate figures from memory**
(USGS Mineral Commodity Summaries 2024/2025, OECD-NEA/IAEA Red Book 2022, BGR energy study). Treat them as about ±5
percentage points, and check them before quoting.

Region scheme (compact): USA+Canada (Greenland included), Latin America (Mexico, Central America, the Caribbean and
South America), Europe (without Russia, with Ukraine and Belarus), Russia, Central Asia+Mongolia (with Afghanistan),
China, Other Asia (East Asia without China, South Asia, Southeast Asia including Indonesian Papua, the Middle East with
Iran, Turkey and the Caucasus), Africa, Oceania (Australia, New Zealand, PNG and the Pacific). Share of ice-free land:
USA+Canada 14.7 %, Latin America 15.2 %, Europe 4.4 %, Russia 12.6 %, Central Asia 4.6 %, China 7.0 %, Other Asia 11.5 %,
Africa 22.5 %, Oceania 6.3 %.

---

## 0. Top findings, ranked

1. **The catalogue and heat maps are not pinned per world.** `OreDeposits.Folder` is fixed at `config/ore-deposits-v1/`. The
   world saves only an informational fingerprint (`EarthOres.UpdateFingerprint`, which logs a warning and goes on). On the
   planned 1:10 no-wipe server, any later catalogue fix (a missing mine added, a coordinate moved, a coal basin's cover
   raised) applies to new chunks only. A body that straddles explored and new ground gets cut, and a moved body leaves a
   duplicate behind. **Every catalogue fix below has to land before launch, or the catalogue needs a per-world version
   (a `v2` folder plus a saved field), as the heat maps already have.**
2. **The heat still affects generation for every graded ore in v5, not only the five boosted resources.**
   `EarthOreGenerator.CompleteStage2` and the child path call `RegionalOreCatalog.GradeQuantile(u, heat, bias)` with the
   raw heat, so background quality is `u^(2.8 − 2·heat)`. In a cold cell (heat 0) background ore is about 61 % poor and
   10 % bountiful; in a hot cell (heat 1) it is about 18 % poor and 30 % bountiful. The heat is 76–97 % MRDS for Au, Ag,
   Pb, Zn, Fe, Mn, U and V, with 76–94 % of the points in the US. So **a US/Europe survey bias goes straight into
   background ore quality** (decision 0065 §3 keeps "the heat-driven quality of 0006"). This conflicts with the author's
   rule that a missing record is not a missing ore. The bog-iron and quartz-vein boosts carry the same bias (finding 6).
3. **Coal tonnage is on mixed bases, and the beds follow the tonnage.** Russia is 38 % of catalogue coal (5.3 Tt; the
   Tunguska and Lena prognostic resources are 2.0 and 1.6 Tt), against roughly 14 % of the world's resources plus
   reserves. China is 12 % against about 25 %, Australia 2 % against about 9 % (Australia 0.12× and China 0.29× of BGR in
   the group's own table). The bed `cover` is derived from `mt`, so in-game coal per basin follows these ratios. The
   group left this open ("decision 12, tonnage basis").
4. **Missing major deposits (confirmed by name and distance; 36 of 368 checks missing).** In order of size or fame:
   Antamina (Cu-Zn, Peru), the Mount Isa copper orebody (only its Pb-Zn-Ag body is there), Palabora (Cu), Kipushi
   (Zn-Cu), Peñasquito (Zn-Pb-Ag-Au), San Cristóbal (Bolivia), Pine Point, Zinkgruvan, Garpenberg, Laisvall, Kabwe,
   Kansai/Altyn-Topkan, Imiter (Ag), Greenbushes (Sn-Ta), Kavalerovo and Iultin (Russian Sn), the **Atacama nitrates (the
   N layer has no catalogue entry at all)**, Khibiny and Kovdor apatite (P), Ronneburg (U), Inder (B), Inner Mongolian
   fluorite, the Bureya, Sakhalin, Collie, Leigh Creek, Waikato and Sakoa coalfields, Itakpe (Fe) and Uyuni (K brine).
5. **Regional bias in the catalogue (contained metal).** Latin America is right for copper (40 % against about 38 %).
   Russia is thin for Cu (3 % against about 8 %: Norilsk's and Sudbury's copper are not counted, see 7), Au (7 % against
   about 11 % of endowment), Ag (7 % against 15 %) and Zn (11 % against 10 %, fine). China is thin for Zn (11 % against
   20 %), Pb (8 % against 13 %), Fe (3 % against 8 %) and coal. Oceania is thin for Fe (17 % against 32 %), Pb (24 %
   against 40 %), Sn (3 % against 13 %) and coal. USA+Canada are heavy in Zn and Pb (23 %/22 % against about 5 %) and in
   Ag (19 % against 5 %): historic districts are well catalogued and today's big producers less so. Africa is right or
   heavy for Au (Witwatersrand), Mn, Cr, Pt, diamonds and bauxite, and thin for phosphate (51 % against about 78 %).
6. **The bog-iron and quartz-vein boosts use biased proxies.** Bog iron (`BogPeat` ×(1+2·Fe heat), and `earth-bogiron` by
   the Fe layer) is driven by hard-rock iron heat, which is 91 % MRDS and 81 % US, and is zero over Latvia. Quartz veins
   follow SiO2 heat, which is 94 % MRDS and 79 % US (USA+Canada hold 40 % of the observed SiO2 heat; Russia and China
   0.5 % each). Both are "uncatalogued" resources where boosts are allowed, but their evidence does not describe them.
7. **Co-products of the iron-ferro bodies are not graded.** iron-ferro has no `grades` or `oreMetal`. Talnakh's
   chalcopyrite (60 % of its blocks) takes the 1.0 % Ni grade on the Ni bands, and Norilsk's copper (about 40 Mt
   endowment) and PGE count in no Cu or Pt total. The same holds for Sudbury, Jinchuan and Duluth Cu, Panzhihua's
   magnetite (graded by 11 % TiO2, so "rich"; Fe is about 30 %) and Kachkanar.
8. **Position precision.** 64 % of point bodies have 3 or more decimals (USGS databases, about 100 m). 22 % have 2
   decimals (about 1 km). 14 % have 1 decimal or fewer (5 km or more; at 1:10 that is ±550 blocks or more). By ore
   tonnage, the 1-decimal share is Al 82 %, Sn 64 %, diamond 60 %, Ni 52 %, Au 50 % and Fe 46 %. Every Fe, Sn, Au, U,
   diamond, Al, Mn, Ti, V, Pt, W, Hg, Sb, Bi, NaCl, B, S, alum and gem body is hand-entered from the literature (q C for
   709 bodies), and 207 of 337 area outlines are hand-drawn (about 5–10 km, so 500–1,000 blocks at 1:10). El Teniente's
   USGS coordinate (−70.4625) lies about 10 km west of the Braden pipe (about −70.35); check it.
9. **Heat-map codes that are mostly prior or guesswork:** clay (90 % of lit cells prior-only), graphite 87 %, fluorite
   81 %, gypsum 74 %, Mo 72 %, Al 62 %, U 62 %, K 59 %, W 48 %, Sn 47 %. The gem codes lapis, spinel, topaz, emerald and
   tourmaline get 25–60 % of their records from the authored best-guess layer or the catalogue, and cover at most 0.3 % of
   land. N covers 0.2 %. All of them are display-only in v5 except through the grade tilt.
10. **Empty blocks.** No heat in any plane and no catalogue body within 100 km covers 87 % of Africa's ice-free land for
    copper (USA+Canada 55 %, Europe 52 %, China 30 %). For iron it is 92 % of Africa and 90 % of Russia, against Europe
    63 %. The biggest empty blocks with permissive rock are the Central African shield (Cameroon, CAR, South Sudan,
    northern DRC), the West African craton (Côte d'Ivoire, Guinea, Mali, Ghana, Burkina Faso), the
    Mozambique-Tanzania-Zambia belts, the Guiana shield and Amazonia. In v5 they matter for the overlays and the grade
    tilt only, because the background floor is everywhere.
11. **Mindat (OpenMindat) is not worth adopting** for the heat maps or the catalogue (section 6). Better open sources
    exist for the real gaps: FODD (Fennoscandia), Geoscience Australia OZMIN/AIMR deposit tables, the USGS regional
    mineral-industry GIS compilations and the USGS World Coal Quality Inventory.

---

## 1. Heat maps: what they are and what they still do

### 1.1 Build

`tools/bake_ore_heatmaps.py` with `tools/ore-heatmap-settings.json` (version 2, dataset `earth-resource-evidence-2026-11-08-v4`)
writes a 720×360 grid of 0.5° cells, 44 layers, three uint8 planes (observed, potential, deep) and a uint16 deep-minimum
depth. The planes combine sources by maximum.

- **Point evidence** gets a tier peak and a linear kernel with a 25 % floor: USGS Major Deposits 0.95/75 km; deposit_large
  0.85/70; deposit 0.75/60; prospect 0.55/45; occurrence 0.40/35. MRDS points get status × MRDS "score" (0.25–0.75,
  25–45 km). They are then normalised by survey density: an enrichment and effort multiplier over a 2.5° window (floor
  0.35), and kernels widened to 75 km where records are sparse. Placeholder stacks (5 or more names on one coordinate)
  are dropped, and the Natural Earth country check repairs or drops points outside their reported country.
- **Polygons** add weight × (0.3 + 0.7 × covered fraction): formations 0.6, surveyed areas 0.8, permissive tracts 0.35
  (potential plane), context polygons 0.25 (potential).
- **Priors** (potential plane only, from the dominant 0.5° GLiM class): evaporite → NaCl/gypsum 0.25 and K 0.15;
  carbonate → Pb, Zn, F 0.15; basic plutonic → Cr, Ni 0.2 and Pt, Ti, V 0.15; basic volcanic → Cu 0.15; arc volcanics
  and plutonics → Cu 0.2 and Au, Ag, Mo 0.15; granite → U, F 0.15 and Mo 0.1; metamorphic → Au 0.12 (0.15 next to granite
  or mafic rock) and graphite 0.1; a humid-tropical bauxite climate → Al 0.2; a peat climate and relief prior → peat 0.2;
  GUM fine sediments → clay 0.59. Sn and W left the granite and aureole priors on 8 November; their potential is now 28
  authored Sn-W(-Mo-Bi) belt polygons at 0.3.
- **The authored best-guess layer** (`best-guess-districts.csv`) has 480 districts in 94 countries, at 0.45–0.85 with
  40–150 km kernels, observed plane only.
- **Group evidence** (decision 0065): each group's `evidence.geojson` stamps its catalogue bodies (size-tiered,
  0.6–0.95), and its `heatmap-overrides.json` drops sources. MRDS is dropped for Ni, Cr, Pt and S. For Sn, W, Mo, Bi, Hg
  and Sb it is re-stamped per cell at 0.25–0.45 ("mrds capped"), so MRDS alone never makes a district.
- **K is mirrored into NaCl.**

### 1.2 What v5 (ore rules 4 and 5) uses the heat for

Checked in code:

| Use | Codes | Where |
|---|---|---|
| Background **attempts** raised (class floor up to the hotspot) | SiO2 (quartz veins, `OreClass.Veins`), clay, peat, Fe (through `earth-bogiron` and the bog peat ×(1+2·Fe heat)), NaCl (through `earth-saltflat`) | `OreBackground.Evidence`/`Uncatalogued` (`src/Core/OreDeposits.cs` 1225–1228); `OreSession.BogPeat` (`src/OreSession.cs` 218–225) |
| Background **grade tilt** (poor → bountiful), every graded ore | all metal codes with graded blocks (Cu, Sn, Fe, Au, Ag, Pb, Zn, Ni, Cr, Ti, Mn, W, Bi, and Hg, U, Pt, V, Mo, Sb where the provider's block is graded) | `EarthOreGenerator.cs` 367 and 578 → `RegionalOreCatalog.GradeQuantile` (exponent 2.8 − 2·heat) |
| Children (gold and silver in quartz, silver in galena) | Evidence 0 for catalogued codes, so no boost; normalised by the parent's ratio | `OreRules.ChildRatio` |
| Display | all 44 (`/earth ores`, World Map overlays by reflection) | `OreSession.Describe`, `docs/REFLECTION-SURFACE.md` |
| Rules v3 worlds (before 0065) | everything, through `ore-heatmaps-v1.json` | out of scope here |

Lens and "fill any rock" are off in ore-layer worlds (`OreBinder.cs` 208), and v4 and v5 need ore layers, so the heat
no longer decides placement anywhere else. The prospecting pick reads catalogue bodies exactly and the background at
its floor.

### 1.3 The 44 codes

Key: "pot-only" is the share of lit land cells lit only by the potential plane (prior or tract). "US pts" is the share of
point records in the USA. "v5" lists D = display, G = grade tilt of background deposits, A = attempts raised. Counts are
records accepted in the v2 bake (`docs/research/ore-heatmaps-v2/bake-manifest.json`). The obs-heat share is area-weighted
observed or deep heat by region (`heat-stats-v2.csv`).

| Code | Main sources (records) | Potential plane | Lit land % / pot-only % | US pts | v5 | Flag |
|---|---|---|---|---|---|---|
| Fe | MRDS 23,033 (91 %); USGS Fe specialist 1,674; GA AIMR 206; minfac 148; catalogue 116 + 11 areas; best guess 34 | none | 16.1 / 0 | 81 % | D G **A** (bog iron) | MRDS-dominated, US-heavy (USA+Canada 23 % of obs heat); drives the bog-iron boost |
| Au | MRDS 81,612 (93 %); specialist 4,485; GA 1,386; minfac 312; catalogue 165 + 24 districts; best guess 69 | metamorphic 0.12–0.15, arcs 0.15 | 33.1 / 36 | 85 % | D G | MRDS-dominated; observed in Africa 10.7 % against 22.5 % of land |
| Ag | MRDS 48,357; specialist 2,715; base-metals catalogue 413; GA 277 | arcs 0.15 | 16.4 / 13 | 82 % | D G | US-heavy (31.5 % of obs heat) |
| Pb | MRDS 37,598; specialist 1,744; catalogue 396; GA 192 | carbonate 0.15 | 20.9 / 31 | 81 % | D G | US-heavy; Africa 6.5 % |
| Zn | MRDS 27,595; specialist 2,204; catalogue 728 | carbonate 0.15 | 21.1 / 31 | 76 % | D G | as Pb |
| Bi | MRDS capped 408; specialist 167; catalogue 8; Sn-W belts | belts 0.3 | 1.7 / 16 | 50 % | D G | USA+Canada 44 % of obs heat, Africa 0.6 % |
| Ni | NiCrPGE primary 1,403; GA 184; catalogue 142; Major Deposits 101 (MRDS dropped) | basic plutonic 0.2 | 4.0 / 17 | 7 % | D G | good |
| Cr | NiCrPGE 935; catalogue 876 + 2 areas; Major Deposits 108 | basic plutonic 0.2 | 2.3 / 33 | 50 % | D G | the catalogue counts many tiny US pods (603 of 878 bodies) |
| Ti | MRDS 2,272; specialist 320; GA 176; PP1802 121; catalogue 22 | basic plutonic 0.15 | 5.7 / 12 | 60 % | D G | MRDS-heavy |
| Mn | MRDS 9,916 (96 %); specialist 221; catalogue 17 + 3 areas | none | 6.8 / 0 | 80 % | D G | MRDS-dominated |
| Hg | MRDS capped 682; specialist 148; catalogue 17 | none | 1.5 / 0 | 62 % | D (G) | USA+Canada 52 % of obs heat |
| W | MRDS capped 1,161; specialist 202; catalogue 32; belts 22 | belts 0.3 | 6.0 / 48 | 51 % | D G | half authored belts |
| U | MRDS 14,867 (97 %); specialist 277; GA 95; catalogue 30 + 10; best guess 30 | granite 0.15 | 9.9 / **62** | **94 %** | D (G) | mostly prior; observed heat 47 % in USA+Canada |
| Pt | NiCrPGE 315; Major Deposits 157; PP1802 153; catalogue 9 + 3 | basic plutonic 0.15 | 2.1 / 33 | 4 % | D (G) | good |
| V | MRDS 4,651 (93 %); specialist 207; PP1802 81; catalogue 7 + 1 | basic plutonic 0.15 | 3.8 / 18 | 89 % | D (G) | MRDS, US-heavy |
| Mo | MRDS capped 1,245; specialist 307; catalogue 118 | arcs 0.15, granite 0.1, belts | 13.0 / **72** | 52 % | D (G) | mostly prior |
| Sb | MRDS capped 1,040; specialist 448; PP1802 38; catalogue 8 | none | 4.7 / 0 | 44 % | D (G) | China 25 % of obs heat (good) |
| B | MRDS 611; specialist 43; catalogue 12; best guess 11 | none | 1.1 / 0 | 44 % | D | thin |
| P | MRDS 2,784; USGS phosphate 1,076; catalogue 27 areas | none | 6.3 / 0 | 50 % | D | reasonable |
| N | MRDS 110; specialist 12; best guess 2; **no catalogue** | none | 0.2 / 0 | 24 % | D | tiny; the Atacama only from MRDS and best guess |
| K | PotashDeps 979; MRDS 355; potash tracts 84 (0.35, with depth); catalogue 25 | tracts; evaporite 0.15 | 9.3 / **59** | 23 % | D | mostly tracts (reasonable for potash) |
| NaCl | MRDS 912; catalogue 99; specialist 98; tracts; K mirror | evaporite 0.25 | 10.6 / 15 | 50 % | D **A** (salt flats) | salt flats boosted by deep-basin salt heat (the playa gate limits the effect) |
| S | native-sulfur MRDS 100; specialist 35; PGI 20; catalogue 11; best guess 10 | none | 0.5 / 0 | 8 % | D | thin |
| C_graphite | MRDS 757; PP1802 110; specialist 60; catalogue 12 + 8 | metamorphic 0.1 | 15.5 / **87** | 51 % | D | mostly prior |
| C_coal | coal polygons 3,918; context polygons 729 (0.25 potential); supplements 509; FSU 446; minfac 355; MRDS 239; catalogue 119 basins; best guess 100 | context polygons | 15.1 / 5 | 12 % | D | good spatially; Africa 2.1 % of obs heat (low but realistic) |
| F | MRDS 4,059 (93 %); specialist 113; PP1802 106; catalogue 13 + 9 | carbonate and granite 0.15 | 16.8 / **81** | 78 % | D | mostly prior, US-heavy |
| SiO2 | MRDS 3,594 (94 %); PGI 94; specialist 86; catalogue 8 | none | 4.7 / 0 | 79 % | D **A** (quartz veins) | US 40 % of obs heat, Russia and China 0.5 % each: a biased vein boost |
| Al | MRDS 2,891; specialist 129; minfac 69; catalogue 43; GA 40; best guess 20 | bauxite climate 0.2 | 14.4 / **62** | 60 % | D (G) | mostly prior |
| gypsum | permissive formations 27,659 polygons; MRDS 1,938; reviewed 525; catalogue 19 | tract prior 0.2 | 10.2 / **74** | 58 % | D | mostly formations and priors (Europe 24 % of obs heat) |
| diamond | MRDS 480; specialist 88; minfac 65; catalogue 41 + 9; best guess 15 | none | 2.4 / 0 | 7 % | D | fine (Africa and Latin America) |
| corundum | MRDS 229; best guess 19; specialist 17; catalogue 13 + 10 | none | 0.6 / 0 | 71 % | D | thin, US-heavy |
| emerald | MRDS 15; catalogue 11; specialist 9; best guess 9 | none | 0.2 / 0 | 10 % | D | guess-heavy (36 %) |
| olivine | MRDS 45; specialist 23; best guess 5; catalogue 5 | none | 0.2 / 0 | 72 % | D | thin |
| garnet | MRDS 563 (91 %); specialist 17; best guess 10; catalogue 7 | none | 0.9 / 0 | **84 %** | D | MRDS, US 54 % of obs heat |
| lapis | best guess 3; catalogue 3; MRDS 2 (12 records) | none | 0.0 / 0 | 0 % | D | guesswork (58 % authored) |
| alum | MRDS 149; catalogue 12; best guess 6 | none | 0.3 / 0 | 82 % | D | thin, US-heavy |
| topaz | MRDS 34; best guess 17; catalogue 10 | none | 0.3 / 0 | 47 % | D | guess-heavy (44 %) |
| tourmaline | MRDS 68; additional 27; best guess 15; catalogue 10 | none | 0.3 / 0 | 40 % | D | guess-heavy |
| beryl | MRDS 582; additional 36; best guess 20; catalogue 14 | none | 0.7 / 0 | 71 % | D | MRDS, US-heavy |
| spinel | MRDS 16; best guess 8; catalogue 5 + 3 | none | 0.1 / 0 | 36 % | D | guess-heavy (48 %) |
| Cu | MRDS (Cu/Sn export) 43,424; specialist 7,069; global Cu assessment 3,420; catalogue 1,616; porphyry and sed-Cu tracts 240 | tracts 0.35; volcanic and arc priors | 36.3 / 40 | 63 % | D G | the best-supported layer, still 24 % of obs heat in USA+Canada |
| Sn | MRDS capped 823; specialist 212; PP1802 113; catalogue 61; belts 23 | belts 0.3 | 5.3 / 47 | 29 % | D G | half authored belts (by design) |
| peat | PEATMAP fraction (12,672 cells); GUM organic 451; LVGMC 111 | climate and relief prior 0.2 | 20.1 / 12 | 0 % | D **A** | good |
| clay | GUM fine-sediment classes (5,084 cells, potential 0.59); minfac 85; specialist 62 | GUM | 8.4 / **90** | 8 % | D **A** | almost entirely a sediment-class prior |

US share of observed heat compared with land share (USA+Canada are 14.7 % of land): Hg 52 %, garnet 54 %, olivine
50 %, U 47 %, Bi 44 %, Mo 42 %, SiO2 40 %, W 36 %, V 36 %, Ag 32 %. Africa (22.5 % of land): Bi 0.6 %, B 0.8 %, N 0 %,
alum 0 %, Sb 2.5 %, coal 2.1 %, Mo 3.9 %, Hg 4.0 %, gypsum 5.8 %, Pb 6.5 %, Zn 7.0 %.

---

## 2. Catalogue coverage

### 2.1 Contents

3,551 point bodies and 337 areas. Base metals: 1,933 + 94; coal and evaporites: 76 + 216; iron and ferroalloys: 1,232 +
20; precious metals and gems: 310 + 7. By source: 2,726 point bodies come from USGS databases (porphyry Cu, sediment-hosted
Cu, VMS, SEDEX/MVT, laterite, podiform chromite), 779 are hand-entered from the literature and company reports
(`earth-named-2026`, `published-endowment`, `lit*`, `co-*`) and 46 from other agencies. Every Fe, Mn, Ti, V, Pt, Al, Sn,
W, Hg, Sb, Bi, Au, U, diamond, gem, NaCl, B, S and alum body is hand-entered. 207 areas carry literature outlines and 130
USGS ones. Coal has no point bodies, only 119 basins.

Per layer, number of bodies and areas by region (compact):

| Layer | n | USA+Can | Lat. Am. | Europe | Russia | C. Asia | China | Oth. Asia | Africa | Oceania |
|---|---|---|---|---|---|---|---|---|---|---|
| Cu | 1,338 | 386 | 158 | 177 | 62 | 62 | 67 | 240 | 107 | 79 |
| Cr | 878 | **603** | 23 | 10 | 1 | 1 | 0 | 213 | 3 | 24 |
| Zn | 451 | 150 | 20 | 82 | 24 | 14 | 38 | 65 | 18 | 40 |
| Au | 158 | 29 | 26 | 8 | 15 | 4 | 6 | 11 | 34 | 25 |
| Ni | 142 | 11 | 29 | 11 | 11 | 3 | 1 | 39 | 12 | 25 |
| Fe | 127 | 17 | 20 | 26 | 9 | 3 | 6 | 11 | 17 | 18 |
| C_coal | 119 | 16 | 5 | 41 | 8 | 5 | 14 | 17 | 7 | 6 |
| NaCl | 69 | 11 | 1 | 36 | 3 | 1 | 2 | 9 | 5 | 1 |
| Ag / Sn / Pb | 56 / 56 / 48 | 19/1/10 | 14/10/6 | 15/16/12 | 1/4/1 | 1/0/1 | 0/6/3 | 2/12/6 | 0/5/4 | 4/2/5 |
| Al / diamond / U | 43 / 41 / 30 | 1/3/11 | 10/3/0 | 5/0/4 | 2/6/1 | 1/0/3 | 2/0/0 | 8/3/1 | 10/25/7 | 4/1/3 |
| P / W / K / Mo | 27/26/24/23 | 3/6/5/11 | 3/1/4/0 | 1/4/6/3 | 0/2/1/1 | 1/0/1/1 | 3/7/2/5 | 6/3/2/1 | 9/0/3/0 | 1/3/0/1 |
| Ti, Mn, gypsum, Hg, F, B, alum, Pt, graphite, S, emerald, Sb, V, SiO2, tourmaline, garnet, topaz, olivine, Bi, lapis, beryl, spinel | 22 … 1 | | | | | | | | | |
| **N** | **0** | | | | | | | | | |

### 2.2 Contained metal or tonnage: catalogue share against the real world (approximate)

The catalogue counts pre-mining endowment (production plus reserves plus resources). The "real" column is mostly MCS
reserves, so regions with a long mining history (Europe, the USA) should sit above it. Where an endowment figure is
better known it is given instead.

| Commodity (catalogue total) | USA+Can | Lat. Am. | Europe | Russia | C. Asia | China | Oth. Asia | Africa | Oceania |
|---|---|---|---|---|---|---|---|---|---|
| **Cu** 2,498 Mt Cu: catalogue | 19 % | **40 %** | 6 % | **3 %** | 6 % | 3 % | 8 % | 9 % | **6 %** |
| real (reserves about 980 Mt) | ~6 | ~38 | ~4 | ~8 | ~4 | ~4 | ~5 | ~11 | ~11 |
| **Au** 222 kt (157 kt in Au bodies): catalogue | 16 % | 11 % | 2 % | **7 %** | 6 % | 3 % | 7 % | **39 %** | 10 % |
| real (production + reserves, about 280 kt) | ~15 | ~11 | ~1 | ~11 | ~4 | ~5 | ~4 | ~27 | ~11 |
| **Fe** 193 Gt Fe (449 Gt ore): catalogue | 13 % | 27 % | 9 % | 15 % | 2 % | **3 %** | 7 % | 9 % | **17 %** |
| real (reserves about 85 Gt Fe) | ~4 | ~20 | ~4 | ~16 | ~1 | ~8 | ~6 | ~3 | ~32 |
| **Coal** 13.9 Tt (basin tonnage): catalogue | 36 % | 0.3 % | 3 % | **38 %** | 1 % | **12 %** | 5 % | 3 % | **2 %** |
| real (BGR resources + reserves, about 22 Tt) | ~37 | ~0.3 | ~3 | ~14 | ~1.5 | ~25 | ~3.5 | ~2 | ~9 |
| **Zn** 800 Mt: catalogue | **23 %** | **6 %** | 14 % | 11 % | 4 % | **11 %** | 12 % | 3 % | 17 % |
| real (reserves about 220 Mt) | ~5 | ~16 | ~3 | ~10 | ~5 | ~20 | ~5 | ~2 | ~29 |
| **Pb** 345 Mt: catalogue | **22 %** | 6 % | 15 % | 9 % | 4 % | 8 % | 8 % | 5 % | **24 %** |
| real (reserves about 90 Mt) | ~5 | ~13 | ~3 | ~7 | ~3 | ~13 | ~4 | ~2 | ~40 |
| **Ag** 1.93 Mt: catalogue | **19 %** | 33 % | 18 % | **7 %** | 3 % | **4 %** | 6 % | 1 % | 10 % |
| real (reserves about 610 kt) | ~5 | ~32 | ~10 | ~15 | ~3 | ~12 | ~3 | ~1 | ~15 |
| **Sn** 22.6 Mt: catalogue | 1 % | 19 % | 8 % | 3 % | 0 % | 25 % | 36 % | 6 % | **3 %** |
| real (reserves about 4.2 Mt; SE Asia about half of historic output) | ~0 | ~22 | ~1 | ~5 | ~0 | ~24 | ~38 | ~4 | ~13 |
| **Ni** 345 Mt: catalogue | 11 % | 12 % | 2 % | 9 % | 1 % | 2 % | 45 % | 5 % | 15 % |
| real (reserves about 130 Mt) | ~2 | ~13 | ~1 | ~6 | ~1 | ~3 | ~46 | ~2 | ~23 |
| **Cr** 6.8 Gt Cr2O3: catalogue | 1 % | 0 % | 1 % | 0 % | 6 % | 0 % | 2 % | **90 %** | 0 % |
| real (resources: about 95 % Kazakhstan and southern Africa) | ~0 | ~1 | ~1 | ~0 | ~10–15 | ~0 | ~3 | ~80 | ~0 |
| **Mn** 7.7 Gt Mn: catalogue | 0 % | 6 % | 14 % | 0 % | 1 % | 3 % | 3 % | **70 %** | 3 % |
| real (identified resources: South Africa about 70 %) | ~0 | ~5 | ~5 | ~0 | ~1 | ~5 | ~3 | ~75 | ~6 |
| **PGE** 104 kt: catalogue | 4 % | 0 % | 0 % | **2 %** | 0 % | 0 % | 0 % | 94 % | 0 % |
| real (reserves: South Africa about 90 %, Russia about 6 %, Zimbabwe, USA) | ~2 | 0 | 0 | ~6 | 0 | 0 | 0 | ~92 | 0 |
| **Bauxite** 33.8 Gt: catalogue | 0 % | 22 % | 1 % | 2 % | 1 % | 2 % | 24 % | 27 % | 21 % |
| real (MCS resources: Africa 32, Oceania 23, S. Am. + Caribbean 21, Asia 18, other 6) | ~0 | ~21 | ~1 | ~1 | ~1 | ~3 | ~15 | ~32 | ~23 |
| **U** 6.4 Mt U incl. Olympic Dam's co-product: catalogue | **23 %** | **0 %** | 3 % | 5 % | 14 % | **0 %** | 0 % | 15 % | 39 % |
| real (Red Book identified resources) | ~11 | ~5 | ~3 | ~8 | ~17 | ~4 | ~1 | ~19 | ~28 |
| **Diamonds** 6.9 Gct: catalogue | 5 % | 0.4 % | 0 % | 19 % | 0 % | 0 % | 0.2 % | 63 % | 13 % |
| real (cumulative production) | ~4 | ~1 | 0 | ~20–25 | 0 | 0 | ~0.5 | ~60 | ~10 |
| **Potash** 290 Gt (`mt`, mixed basis): catalogue | **55 %** | 2 % | 14 % | **7 %** | 1 % | 1 % | 18 % | 3 % | 0 % |
| real (reserves as K2O) | ~35 | ~3 | ~27 | ~13 | ~0 | ~5 | ~16 | ~1 | ~0 |
| **Phosphate rock** 112 Gt: catalogue | **19 %** | 5 % | 3 % | **0 %** | 4 % | 6 % | 8 % | **51 %** | 4 % |
| real (reserves about 74 Gt; Morocco about 68 %) | ~1.5 | ~2 | ~1.5 | ~3 | ~0.5 | ~5 | ~4 | ~78 | ~1.5 |

Reading (bold cells): the catalogue matches the world for bauxite, Ni, Mn, Cr, diamonds and the Latin American copper.
It is **US/Europe-heavy for Zn, Pb and Ag** (historic districts complete, today's big producers in Latin America and
China thin) and **Russia-heavy for coal**. It is **thin in Oceania** (Fe, Pb, Sn, coal; Mount Isa Cu and Greenbushes
missing), **China** (Fe, Zn, Pb, Ag, coal, U), **Russia** (Cu, Ag, Au; Norilsk's Cu and PGE not counted, see 3.3) and
**Latin America** (Zn and Pb: Antamina, Peñasquito, San Cristóbal missing; uranium: Brazil's Lagoa Real and Santa
Quitéria). Phosphate is thin in Africa and Russia (Khibiny and Kovdor missing). The group notes agree for their own
metals (base-metals: "Pb and Zn are under-represented … carbonate-replacement, skarn and vein deposits (much of Mexico,
Peru, China and Central Asia)"; iron-ferro: "Chinese iron and Mn, Australian mineral sands and Ni sulphides, Brazilian
Mn"; precious-gems: "Russian, Chinese, Mexican and Central Asian lode gold is mostly in the background").

### 2.3 Commodities thin overall

- **None at all:** N (Chilean caliche, the world's main natural nitrate) has no catalogue body.
- **Only a handful of bodies:** spinel 1, lapis 3, beryl 3, Bi 4, olivine 5, garnet 6, topaz 6, tourmaline 7, V 7 + 1
  area, Sb 8, SiO2 8, S 8 + 3 areas, C_graphite 12, alum 12, B 12, F 13, corundum 13, gypsum 4 + 14 areas, P 1 + 26 areas.
- **Lead:** only 48 Pb-layer bodies. Lead comes mainly as a co-product of the 451 Zn bodies, and the total is 344 Mt
  against about 2 Gt identified.
- **Uranium:** 30 bodies; the Kazakh roll fronts are represented by two large lenses only.
- **Coal:** basins only; small coalfields (Collie, Leigh Creek, Waikato, Sakoa, Bureya, Sakhalin, Turkish lignites beyond
  Afşin-Elbistan, South Gobi beyond Tavan Tolgoi, Maikuben and Shubarkol) are missing.
- **Iron:** 116 bodies and 11 areas; Chinese iron is about a quarter of China's published resources.
- **Count bias in Cr:** 603 of 878 chromite bodies are tiny US pods from USGS podiform chromite (many under 1 kt). That
  is harmless for tonnage, but they make chromite look US-centred in `/earth ores` lists.

---

## 3. Spot checks (≈110 named, plus 368 presence checks)

Scripts: `spotcheck.py` (named lookups with full fields) and `missing_check.py` (presence of a same-commodity body within
15–150 km or by name). "Ref" coordinates are from general knowledge, about ±2 km.

### 3.1 Well-known deposits

| Deposit | In catalogue | Position vs ref | Size, grade, depth, shape | Verdict |
|---|---|---|---|---|
| Chuquicamata | Cu stock (USGS porphyry) | 1.7 km | 21.3 Gt @ 0.59 %, top 0, **base 287 m**, 11.9 × 3.0 km | tonnage fine (merged with Radomiro Tomic by the database's 2 km rule); the stock is a pancake. The real orebody goes past 1.5 km depth, and the footprint (alteration zone) is too large |
| Escondida | Cu stock | 0.6 km | 11.2 Gt @ 0.77 %, 100–839 m | good |
| El Teniente | Cu stock | **10.4 km W** (−70.4625 against about −70.35) | 20.7 Gt @ 0.62 %, 100–1,900 m | check the coordinate (Sewell/Braden pipe) |
| Bingham Canyon | Cu stock | 1.1 km | 3.2 Gt @ 0.88 %, base 508 m | an older, low tonnage (about 6 Gt with later resources); the pit is already about 1.2 km deep |
| Grasberg | Cu-Au stock | 1.6 km | 4.0 Gt @ 0.6 % Cu, 0.64 g/t Au; 44 % of the blocks are gold (by value) | plausible; the Ertsberg district (DMLZ, Big Gossan, Kucing Liar) is not separate |
| Olympic Dam | Cu-U-Au IOCG | 0 km | 10.8 Gt @ 0.6 % Cu, 0.02 % U, 0.3 g/t Au; top 330 m | good (q B) |
| Oyu Tolgoi | Cu stock | 1.5 km | 3.75 Gt @ 0.98 %, top 100 m | fine; the deep Hugo Dummett top (about 400 m and more) is simplified |
| Katanga and Zambian Copperbelt | Cu areas (USGS sediment-hosted) | 0.5–5 km | Kamoto-KOV 416 Mt @ 4.5 %, Konkola 775 Mt @ 2.9 % | good |
| Kupferschiefer (Lubin) | Cu area | 9.6 km (area centre) | 3.6 Gt @ 2 % at 600 m, 640 km² | good |
| Udokan | Cu lens | 3.7 km | 2.7 Gt @ 1 % | good |
| Norilsk-Talnakh | **Ni** lens + Norilsk-1 Pt lens | 7 km | 2.2 Gt @ 1 % Ni at 500–1,500 m; chalcopyrite 60 % of the blocks, **Cu and PGE grades not recorded** | Cu and PGE uncounted (3.3) |
| Sudbury | 5 Ni lenses | 8–29 km (ring) | 1.65 Gt total @ 1.1–1.5 % Ni | good |
| Bushveld | Cr, Pt and V areas | area | Merensky + UG2 15 Gt; chromitites 8.6 Gt; Platreef 6 Gt | good (hand-drawn outline) |
| Great Dyke | Cr and Pt areas | 19 km | 5 Gt chromitite; 2.5 Gt MSZ | plausible |
| Kempirsai | Cr lens | 38 km (district) | 800 Mt @ 48 % | plausible |
| Kiruna | Fe lens | 0 km | 2.8 Gt @ 63 %, 4 km × 80 m, to 2 km | tonnage generous; 63 % is concentrate-like (crude ore about 45–48 %) |
| Carajás | N4-N5 lens, S11D lens, Grão Pará area | 16–28 km | 3.5 + 10 + 5 Gt @ 66 % | good (about 18 Gt) |
| Quadrilátero Ferrífero | area + Itabira | area | 25 Gt itabirite | plausible |
| Hamersley | area + 20 or more mines | area | 55 Gt area + mines | plausible |
| Mesabi | natural ores + taconite area | 4–12 km | 2.5 + 40 Gt | plausible |
| Kryvyi Rih, KMA, Lorraine | areas | 7–84 km | 20.5, 55, 6 Gt | plausible |
| Simandou | Fe lens | 5.5 km | 4.6 Gt @ 65 %, 100 × 1 km | fine |
| Witwatersrand | 7 Au areas (no `country`) | areas | 81 kt Au, reefs 0.5–1.2 m at 500–2,200 m | good |
| Muruntau | Au veins | 1.5 km | 1.76 Gt @ 3 g/t (5.3 kt) | good |
| Kalgoorlie Golden Mile | Au veins | 0.6 km | 480 Mt @ 4.5 g/t (2.2 kt) | good |
| Carlin trend | Goldstrike, Gold Quarry, Carlin | 7–15 km | 400 Mt @ 4.5, 350 @ 1.8, 40 @ 3 | fine |
| Obuasi, Sukhoi Log, Kolar | Au | 0.8 / 15 / 0.8 km | 280 Mt @ 7; 900 @ 2; 50 @ 16 | good |
| Las Médulas | Au blanket | 1 km | 180 Mt @ 0.03 g/t (5 t) | order of magnitude plausible |
| Broken Hill (NSW) | Zn lens | 1.5 km | 280 Mt @ 8.5 % Zn (+Pb, Ag) | good |
| Red Dog | Zn lens | 2 km | 165 Mt @ 16.6 % | good |
| Mount Isa | Zn-Pb-Ag lens | 0 km | 150 Mt @ 7 % Zn, 6 % Pb, 150 g/t Ag | **the Cu orebody (about 255 Mt @ 3.3 % Cu) is missing** |
| Rammelsberg | Zn lens | 1.1 km | 30 Mt @ 14 % | good |
| Falun | Cu stock | 0.4 km | 28 Mt @ 2 % | good |
| Rio Tinto | Cu lens + gossan | 0.6 km | 434 Mt incl. 100 Mt gossan | good |
| Cerro Rico de Potosí | Ag stock | 0.2 km | 300 Mt @ 200 g/t (60 kt Ag) | good |
| Bolivian tin belt | Llallagua, Huanuni, Oruro, Colquiri, Chorolque, Caracoles | 1 km | Llallagua 150 Mt @ 0.7 % (1 Mt Sn) | plausible |
| Cornwall | Camborne-Redruth veins + Wheal Jane, Carnon and others | 2.3 km | 25 Mt @ 1.2 % (0.3 Mt Sn) | probably low: historic Cornwall and Devon output is usually put at 1.5–2.5 Mt of tin |
| Bangka-Belitung | onshore and offshore placers | 2–8 km (district) | 6 + 3 + 2.5 Gt @ 0.03 % (3.5 Mt Sn) | plausible (the offshore body is in the sea) |
| Kinta Valley | Sn placer | 14.5 km | 10 Gt @ 0.02 % (2 Mt) | plausible |
| Gejiu, Erzgebirge, Panasqueira, San Rafael, Bisie | Sn and W | 0–10 km | plausible sizes | good |
| Almadén, Idrija, Huancavelica | Hg | 0.1–2.3 km | 260 kt, 143 kt, 60 kt Hg | good |
| Kimberley, Mir, Udachnaya, Jwaneng, Orapa, Catoca, Argyle | diamond stocks | 0–1 km | 14 Mct (Big Hole), 175, 400, 700, 810, 560, 870 Mct | good |
| Cigar Lake, McArthur River | U lenses | 2 km / 0.3 km | 0.9 Mt @ 15 %, 3.4 Mt @ 10 % | good |
| Inkai / Chu-Sarysu | U lenses (2) | 24 km | 500 + 700 Mt @ 0.07 % | coarse, plausible |
| Ruhr, Upper Silesia, Donbas, Kuzbass, Pechora, Karaganda, Shanxi, Ordos, Damodar, Kalimantan, Witbank, Powder River, Appalachians, Sydney | coal areas | areas | Kuzbass 640 Gt, Powder River 970 Gt, Ordos 220 Gt, Bowen 50 Gt | present; tonnage basis inconsistent (finding 3) |
| Zechstein, Stassfurt, Wieliczka, Hallstatt, Khewra, Dead Sea | K and NaCl areas and domes | 0.5–55 km | Zechstein K 20 Gt; Stassfurt saddle 28 Gt | good |
| Saskatchewan potash | K area (Elk Point) | area | 150 Gt "mt" | present; `mt` is not comparable with Upper Kama's 20 Gt |
| Chilean nitrates | — | — | — | **missing (N has no catalogue)** |
| Sicilian sulfur, Tarnobrzeg | S areas | 16–21 km | 300 Mt, 2 Gt | present |
| Khouribga, Bou Craa, Florida, Phosphoria | P areas | 5–23 km | 26 Gt @ 31 %, 1 Gt, 8 Gt, 10 Gt | present |
| Weipa, Boké-Sangarédi | Al blankets | 0–7 km | 3 Gt, 1.5 + 3 Gt @ 47–52 % | good |
| Kalahari, Nikopol, Chiatura, Moanda, Groote Eylandt | Mn | 2–9 km | 13.6 Gt, 2.2 Gt, 400 Mt, 450 Mt, 400 Mt | good |
| Mogok, Muzo, Sar-e-Sang | gems | 1–2.5 km | sizes estimated (`est`) | present |

### 3.2 Missing well-known deposits (`presence-check.csv`)

Confirmed missing (no body of that commodity within the radius and no name match within 400 km):

- **Copper:** Antamina (one of the largest Cu-Zn skarns, about 2 Gt), the Mount Isa copper orebody, Palabora
  (carbonatite Cu, about 1 Gt), Kennecott (Alaska, small but famous).
- **Lead, zinc and silver:** Kipushi, Peñasquito, San Cristóbal (Bolivia), Pine Point, Zinkgruvan, Garpenberg, Laisvall,
  Kabwe, the Kansai/Altyn-Topkan district (Tajikistan), Imiter. Huize is present as Qilinchang and Kuangshanchang.
- **Tin:** Greenbushes (now mostly lithium, historically tin), Kavalerovo, Iultin.
- **Iron:** Itakpe (small). Panzhihua and Kachkanar are present as Ti and V bodies with magnetite shares.
- **Coal:** Bureya, Sakhalin, Collie, Leigh Creek, Waikato, Sakoa. (Turkish lignites, Maikuben, Shubarkol, South Gobi,
  Gippsland beyond the Latrobe and Ombilin are already on coal-evaporites' "left to do".)
- **Industrial minerals and uranium:** the Atacama nitrates (N), Khibiny and Kovdor apatite (P), Inder (B), Inner
  Mongolian fluorite (Bayan Obo/Sumochagan area), Ronneburg (U; Schlema-Alberoda is present), Uyuni (K brine; arguably
  out of scope for solid ore).

Other gaps from the group notes and the regional shares: Brazil's uranium, Mexican Ag-Pb-Zn (Santa Eulalia and Naica
are present, Peñasquito is not), the Kazakh roll fronts as individual bodies, Chinese iron (Anshan-Benxi 10 Gt stands in
for many districts), Australian Ni sulphides and mineral sands, Stillwater's chromitite, and the Ertsberg district beside
Grasberg.

### 3.3 Size and shape issues found

- **Co-products in iron-ferro are not graded and not counted:** no `grades`/`oreMetal`. Talnakh (60 % chalcopyrite) is
  graded on Ni bands. Norilsk's Cu (about 40 Mt) and PGE (Talnakh about 4.5 g/t) and the copper of Sudbury, Jinchuan and
  Duluth count in no Cu or Pt total. Panzhihua's magnetite is graded by 11 % TiO2 (rich) and Kachkanar's by 0.13 % V2O5
  (poor). Fix: add `grades` for Cu, Pt and Fe and an `oreMetal` map to iron-ferro, as base-metals has.
- **Porphyry stock heights from Singer's ore-zone areas** make pancakes for some giants: Chuquicamata 287 m over 11.9 ×
  3 km, Morenci 428 m, Oyu Tolgoi 457 m, Bingham 508 m (17 of 88 copper stocks over 1 Gt are under 400 m tall). Real
  orebodies run 1–2 km deep. Gameplay effect: a wide thin slab instead of a deep stock. The depth curve already
  compresses depth, so this is low priority, but it moves the blocks sideways by kilometres.
- **Value-share ores** (base-metals rule 4): gold is 44 % of Grasberg's blocks, 21 % of Bingham's and 76 % of Comstock's,
  and uranium 28 % of Olympic Dam's. This is a deliberate design, and it makes porphyry copper bodies the biggest
  "gold block" sources outside the Witwatersrand. 97 % of the gold tonnage is "poor".
- **Mixed `mt` basis for areas** (coal: identified against prognostic; potash: salt against potash ore). The bed cover is
  fitted to `mt` (172 of 172 coal and evaporite areas within 0.8–1.25), so the basis carries into the game.

---

## 4. Coordinates and size at fine scale

`position_precision.py` counts decimals per point body. Trailing zeros are dropped, so "≤1 decimal" includes a few
genuine 2-decimal values such as 47.50.

| Precision | Bodies | Meaning at 1:10 (10 m per block) |
|---|---|---|
| 3 or more decimals (about 100 m or better) | 2,265 (64 %); USGS porphyry 84 %, VMS 85 %, chromite 93 %, laterite 94 % | within about 10 blocks |
| 2 decimals (about 1 km) | 793 (22 %); USGS SEDEX/MVT 77 %, coal-evaporites 67 %, precious-gems 51 % | ±55 blocks |
| 1 decimal or fewer (5 km or more) | 493 (14 %); `published-endowment` 61 %, `lit-placer-general` 69 %, most `co-*` and `lit-*` | ±550 blocks or more |

- **q:** A 1,873 (53 %), B 969 (27 %), C 709 (20 %); 45 % of q C bodies have 1 decimal. Coordinates explicitly marked
  `est`: 49 bodies (Kelian, Gosowong, La Herradura, medieval mines and others).
- **By ore tonnage, the share sitting at 1-decimal positions:** Al 82 %, Sn 64 %, diamond 60 %, Ni 52 %, Au 50 %, Fe 46 %,
  Cr 39 %, U 39 %, Zn 23 %, NaCl 15 %, Cu 12 %. **Footprints are estimated** (`est` lenKm/widKm) for 90 % of Zn tonnage,
  100 % of Sn and Al, 84 % of U, 63 % of Au and 47 % of Cu.
- **Areas:** 207 of 337 have hand-drawn outlines (q C, about 5–10 km per the iron-ferro notes): coal basins, iron
  formations, evaporite basins and the Witwatersrand. Median area 1,455 km².
- **Small bodies at coarse positions:** 668 bodies (19 %) have a footprint under 2 km and coordinates of 2 decimals or
  fewer, and 207 (6 %) have a footprint under 2 km at 1 decimal. At 1:10 such a body (under 200 blocks) can sit on the
  wrong side of a valley, under a lake or in a different rock unit from the real mine.
- **Offshore:** 36 point bodies lie off Natural Earth land, 11 by more than 2 km. Most are real (Bangka and Andaman
  offshore placers, Grand Ecaille and Cote Blanche domes, Philippine island laterites). Zabargad is 0.5° off because its
  island is missing from Natural Earth.
- **Country labels:** 24 rough mismatches, all border cases or naming (Los Pelambres, Cínovec-Zinnwald, Raibl about 2 km
  east of Cave del Predil, Kerch in Crimea); no gross error found.
- **What it means in game:** at 1:10 a district-centroid position (5 km) puts the body up to 500 blocks from the
  landmark players associate with it (the Big Hole, Kiirunavaara, Cerro Rico, the Rammelsberg). It also puts it away from
  any Earth Ruins site placed at the real mine. Hard-rock bodies do not move; rules v5 moves only cover blankets (up to
  30 km, to the nearest valley with their layer). Database bodies (Cu, Zn, Cr, Ni laterites) are well placed. The
  hand-entered layers (Fe, Au, Sn, U, diamond, bauxite, salt, gems) are the ones to re-check, and before launch (finding
  1).

---

## 5. Empty blocks (no catalogue, no heat)

`empty_blocks.py` and `empty_tiles.py`: ice-free land with zero heat in every plane and no catalogue body of that
commodity within 100 km. A Ni body counts for Cu if it has chalcopyrite.

**Share of ice-free land that is empty, by region:**

| Commodity | World | USA+Can | Lat. Am. | Europe | Russia | C. Asia | China | Oth. Asia | Africa | Oceania |
|---|---|---|---|---|---|---|---|---|---|---|
| Cu (no heat at all) | 62.5 % (83 Mkm²) | 55 | 65 | 52 | 59 | 39 | 30 | 55 | **87** | 65 |
| Cu (no observed or deep heat) | 75.8 % | 61 | 75 | 60 | 81 | 67 | 73 | 73 | **92** | 72 |
| Fe | 82.8 % (110 Mkm²) | 71 | 82 | 63 | **90** | 87 | 81 | 81 | **92** | 81 |
| Au (no heat) | 65.2 % | 53 | 60 | 66 | 73 | 71 | 74 | 70 | 66 | 65 |
| Au (no observed heat) | 77.0 % | 64 | 69 | 74 | 78 | 76 | 80 | 82 | **91** | 66 |
| Sn | 94.5 % | 95 | 95 | 94 | 91 | 92 | 94 | 95 | 97 | 91 |
| Coal | 81.2 % (108 Mkm²) | 72 | 89 | 75 | 72 | 82 | 53 | 88 | **98** | 64 |

**Largest empty 5° tiles where at least 30 % of the land is permissive rock** (GLiM: Cu = volcanic, plutonic and
metamorphic; Fe = metamorphic and mafic; Sn = granite and metamorphic; Au = crystalline plus unconsolidated (placers);
coal = siliciclastic, mixed sedimentary and unconsolidated). Area is empty permissive land.

- **Copper:** 30 tiles, 4.8 Mkm². Cameroon-Gabon-Congo 0–5° N 10–15° E (308k km²); northern DRC and South Sudan
  0–5° N 25–30° E (296k); Côte d'Ivoire, Liberia and Guinea 5–10° N 10–5° W (287k); Mali and Guinea (226k); Ghana (220k);
  Brazil, Suriname and French Guiana 0–5° N 55–50° W (213k); CAR, Sudan and South Sudan 5–10° N 20–30° E (2 × 211k; Hofrat
  en Nahas Cu, about 9.7° N 24.3° E, is in this gap); Mozambique, Tanzania and Malawi (207k); Venezuela and Colombia
  (203k); Zambia, Malawi and Mozambique (198k); Nigeria, Benin and Togo (171k).
- **Iron:** 46 tiles, 5.8 Mkm². Northern DRC (290k); Cameroon, Nigeria and Chad (254k); Burkina Faso, Mali and Ghana
  (247k); Mali and Guinea (244k); Tanzania, Zambia and DRC (229k; Liganga Fe-Ti-V at about 9.9° S 34.6° E is in this
  gap); CAR and South Sudan (2 × 211–217k); Ethiopia and Eritrea (205k); Russia 0.67 Mkm² (Siberian shield); Canada
  0.47 Mkm². Also check Cameroon-Congo iron (Mbalam-Nabeba, Zanaga, Belinga in Gabon); none was found by name.
- **Gold** (Africa 91 % without observed heat): the biggest permissive empties are Sahara and Arabia sand and Kalahari
  cover (expected), plus the interior of Australia, the Deccan, the Amazon (0–5° S 70–65° W) and DRC (0–5° S 20–25° E).
- **Tin:** 115 tiles, 14.5 Mkm²; mostly expected, as tin sits in a few belts. Missing known belts: Russia's Far East (the
  Kavalerovo and Iultin bodies; the Solnechnoye district is present) and the Kibaran belt north of Bisie and Manono
  (Rwanda's Rutongo is present).
- **Coal:** 235 tiles, 38.7 Mkm². Almost all expected: Congo and Amazon basin cover, the Sahara, the Chaco, the Kalahari
  and interior Australia. The real coal gaps are the tonnage gaps of finding 3 and the small coalfields of 3.2.

In v5 these empties change only the overlays and the background grade tilt (they get the cold-cell 61 % poor
distribution). The background floor itself is everywhere, so they do not decide where ore exists.

---

## 6. The Mindat (OpenMindat) proposal

The proposal in `docs/remaining-ore-evidence.md` (4 November) is to use Mindat localities only for cells blank in the
observed plane, at or below the occurrence tier (0.4/35 km), with the MRDS enrichment and effort damping, after a
licence check.

What I found makes it a poor fit:

- **What it could fill.** Locality points with named species, which helps exactly the display-only gem and industrial
  codes (lapis, spinel, topaz, tourmaline, beryl, garnet, corundum, emerald, olivine, alum (alunite), nitratine, native
  sulfur, borates) and European areas outside the national surveys already used (Czechia, Austria, Scandinavia, the
  Balkans). **None of these affects generation in v5**: the heat raises only quartz veins, clay, peat, bog iron and salt
  flats, and tilts background grade of graded ores, which these mostly are not. The gain would be overlay cosmetics.
- **What it cannot fill.** Mindat has no tonnage or grade, so it adds nothing to the catalogue, which is where real
  amounts come from. Its coverage follows collectors and papers, so it is dense where MRDS and the national surveys
  already are (USA, Western and Central Europe, Australia, Canada) and thin in the empty blocks of section 5 (the Central
  and West African shields, Siberia, Amazonia). Many US localities cite MRDS, so the US bias would grow, not shrink.
  Locality coordinates are often town or district level, coarser than the USGS databases.
- **Licence risk.** As far as I know, OpenMindat data are released under **CC BY-NC-SA 4.0**; this needs checking. The
  API also needs a personal account and key, which is the author's step and an outward-facing decision. Share-alike would
  pass on to the baked heat file, which already mixes CC BY, OGL, dl-de/by and public-domain inputs, and non-commercial
  may clash with any monetised server. Mindat is excluded today, which is the right call.
- **Verdict:** do not adopt it for the heat maps or the catalogue. At most, a person can use Mindat by hand to look up
  single facts (the coordinates of a named mine) while re-checking catalogue positions, citing the original source.
- **Better sources for the real gaps:**
  - FODD (Fennoscandian Ore Deposit Database: GTK, SGU, NGU and the Russian Kola surveys; tonnage and grade, CC BY 4.0 for
    GTK data). It covers Zinkgruvan, Garpenberg, Laisvall, Khibiny, Kovdor, Kostomuksha, the Bergslagen iron and Outokumpu.
  - Geoscience Australia OZMIN and AIMR deposit-level resources (CC BY 4.0). They cover the Mount Isa copper orebody,
    Greenbushes, Collie, Leigh Creek, the Bowen, Galilee and Surat in-situ coal, and Australian iron and mineral sands.
  - The four USGS mineral-industry GIS compilations for Africa, Latin America, Asia-Pacific and Eurasia (public domain;
    a manual browser download from ScienceBase, which blocked scripts on 2 October).
  - The USGS World Coal Quality Inventory (public domain) for coal points outside the US.
  - Coal: Global Energy Monitor's tracker is CC BY 4.0 but needs a sign-up form with personal data (excluded so far; the
    author's call).

---

## 7. Blindspots ranked, with fixes and rough effort

| # | Blindspot | Why it matters for the 1:10 no-wipe server | Fix | Effort |
|---|---|---|---|---|
| 1 | The catalogue and heat maps are not versioned per world | any fix after launch cuts bodies at the explored edge and duplicates moved ones | add a catalogue version to `EarthParams` (or a frozen `ore-deposits-v2` folder chosen per world, as heat maps v1/v2), and pin the server's world to it; or freeze the data before launch | 1–2 days code + `OreDepositsTests` |
| 2 | The heat tilts background grade (US/Europe survey bias in quality) | the same rock gives 18 % poor ore in Nevada and 61 % poor in the Congo | make the background grade independent of heat for catalogued resources (flat or host-rock-based, `QualityBias` only), as ore rules 6 for new worlds; keep the tilt only for the boosted uncatalogued classes, if anywhere | 0.5–1 day + a bench run |
| 3 | Coal tonnage basis (Russia prognostic against China and Australia identified) | coal per basin in game follows `mt` through the bed cover | settle the group's "decision 12": either cut Tunguska, Lena, North Slope and Alberta to identified resources, or raise China (Ordos about 2 Tt prognostic, Junggar, Shanxi) and Australia (Bowen, Galilee, Surat in-situ, from Queensland or GA) to the same basis; then recompute cover | 1 day + check |
| 4 | Missing majors (Antamina, the Mount Isa Cu orebody, Palabora, Kipushi, Peñasquito, San Cristóbal, Pine Point, the Swedish base metals, Kabwe, Kansai, Imiter, Greenbushes, Russian Sn, Atacama nitrate, Khibiny and Kovdor P, Ronneburg U, Inder B, the small coalfields) | well-known places players will visit; nitrate has no body at all | about 35 rows in the named tables (`named-additions.csv`, `authored-deposits.csv`, coal `build_catalogue.py`), with sources; an N caliche blanket in `playa` and `cover` | 1–2 days |
| 5 | iron-ferro co-products ungraded and uncounted | Norilsk and Sudbury chalcopyrite take Ni bands; Panzhihua magnetite rich by TiO2 | add `grades` (Cu, Pt, Fe) and `oreMetal` to iron-ferro; rebuild; re-run `check_catalogues.py` | 0.5 day |
| 6 | Bog-iron and quartz-vein boosts from biased hard-rock MRDS heat | bog iron (a Baltic and Scandinavian medieval staple) gets its ×3 boost in the US and none in Latvia | drive bog iron by peat/wetland (PEATMAP, GUM organic) and, if wanted, an authored bog-iron region layer; drive the quartz-vein boost by lithology (metamorphic and granite) instead of SiO2 MRDS | 1 day |
| 7 | Hand-entered positions (1 decimal) for Fe, Au, Sn, Al, diamond, U, salt and gems; 207 hand-drawn outlines | up to 500–1,000 blocks off the real mine or landmark at 1:10 | re-check the about 490 one-decimal bodies (largest tonnage first) against open sources (GA OZMIN, FODD, USGS minfac and Major Deposits, OSM `landuse=quarry` names, company reports); replace outlines with survey polygons where licences allow (GA Hamersley, CPRM Carajás and Quadrilátero); check El Teniente | 2–3 days, Europe and the likely spawn regions first |
| 8 | Pb, Zn and Ag thin outside the USGS SEDEX/VMS/MVT types (Mexico, Peru, China, Central Asia) | Latin America 6 % of Zn against about 16 %; China 11 % against 20 % | 40–60 carbonate-replacement, skarn and vein districts from the literature (Peñasquito, Antamina, Naica and Santa Eulalia sizes, Cerro de Pasco, Huize, Fankou, Kansai, Zhairem and others) | 2–3 days |
| 9 | Regional gold, iron and uranium (Russia, China, Mexico, Central Asia gold; Chinese iron; Brazilian and Kazakh U) | Russia 7 % of Au against about 11 %; China 3 % of Fe against 8 %; Latin America 0 % of U | about 40 mid-size gold deposits; Chinese BIF and skarn iron districts; Lagoa Real, Santa Quitéria and more Kazakh ISR fields | 2–3 days |
| 10 | Heat maps mostly prior for clay, graphite, F, gypsum, Mo, Al, U and K, plus guess-heavy gems | display only, but `/earth ores` and the World Map read as evidence | keep, but show observed and potential apart in the overlays (and say "prior" in `/earth ores`); no new data needed for gameplay | 0.5 day (World Map and Earth text) |
| 11 | Empty blocks in African and Amazonian shields for Cu, Fe and Sn | overlays only (the background floor is everywhere) | if the overlays matter: import the USGS Africa and Latin America mineral-industry compilations (needs the author's manual download) | 1–2 days after download |
| 12 | Stock heights flattened by Singer's ore-zone areas | wide thin slabs (Chuquicamata 287 m over 12 km) | when the area gives a height under about 600 m for a body over 1 Gt, use the type rule instead (height 200·(mt/100)^0.3) and the known pit depth | 0.5 day |
| 13 | Chromite count bias (603 tiny US pods) | `/earth ores` and lists look US-centred; no tonnage effect | optionally drop pods under 1 kt or merge them per district | 0.5 day |

Not recommended (it conflicts with the author's rule): any gate or boost of background amounts by survey density for
catalogued resources. Removing the existing heat-driven quality tilt (#2) goes the other way and removes a survey
effect.

---

## Files

All in `scripts\oredata\`:

| Script | Purpose |
|---|---|
| `common.py` | Natural Earth country and region lookup, heat-map decoder, land and ice, catalogue loader |
| `flatten_catalogue.py` | builds `catalogue-flat.csv`: every body and area with country, region and contained metal |
| `coverage_by_region.py` | builds `coverage-compact.csv`, `coverage-regions.csv` and `coverage-console.md` |
| `heat_stats.py` | builds `heat-stats-v2.csv` and `heat-console.md`: per-code lit, prior-only and regional observed heat |
| `code_sources.py` | builds `code-sources.csv`: per-code source mix from the v2 bake manifest; `v2-manifest-summary.txt` |
| `spotcheck.py` | builds `spotcheck-console.txt` and `spotcheck-raw.csv` |
| `missing_check.py` | builds `presence-check.csv`: 368 checks, 36 missing |
| `position_precision.py` | builds `precision-console.txt` |
| `empty_blocks.py` | builds `empty-summary.csv` and `empty-blocks.csv`; the connected components are too coarse to use, see the tiles |
| `empty_tiles.py` | builds `empty-tiles.csv` |

Run any of them with `C:\VS mods\Earth\.venv\Scripts\python.exe -I -X utf8 <script>`. Run `flatten_catalogue.py` and
`heat_stats.py` first, because the others read `catalogue-flat.csv` and `grids-cache.npz`.
