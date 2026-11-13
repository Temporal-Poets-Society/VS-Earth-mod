# Metallic resource evidence audit — 29 September 2026

All **20 metallic commodity layers** were audited: Fe, Au, Ag, Pb, Zn, Bi, Ni, Cr, Ti, Mn, Hg, W, U, Pt, V, Mo, Sb, Al/bauxite, Cu and Sn. The audit makes **196 named-province queries and 230 country checks**. Every metal remains a partial inventory. Positive witnesses in sampled districts do not establish complete coverage, and a blank area is not evidence of absence.

The additions are review data only. This work changes neither Earth world generation nor the atlas renderer. No probabilities, grades or shallow ore bodies were inferred from points, country production, survey density or rock labels.

## Acquired complements and repairs

* `normalized/pp1802-global.geojson`: **2,125 USGS PP1802 features**, including 2,121 points and four generalized antimony regions. All source commodities, including nonmetals, are retained. The data are from 2017 and support regional interpretation, not surveyed deposit boundaries. [USGS data and metadata](https://mrdata.usgs.gov/pp1802/).
* `normalized/ga-aimr-metals.geojson`: **3,205 Australian records**, comprising 2,960 records labelled Deposit and 245 Producing mine, across 16 service layers. Source publication links refer to AIMR2025; the service copyright is 2026. These records improve occurrence coverage beyond operating mines but are still an identified-resource inventory. [Geoscience Australia service](https://services.ga.gov.au/gis/rest/services/AustraliasIdentifiedMineralResources/MapServer).
* `normalized/reviewed-secondary-metals.geojson`: **two explicit commodity joins**, not two new deposits. Nui Phao's original USGS point lists fluorite; [USGS chapter G](https://pubs.usgs.gov/publication/pp1802G) also identifies W, Bi, Cu and Au. Boliden gains Bi from [SGU mine mineralogy](https://resource.sgu.se/dokument/publikation/c/c651rapport/c651-rapport.pdf), using the exact named USGS VMS record's coordinate. Provenance, exact-name/country match, mineral forms and duplicate status remain attached. These facts do not automatically establish hand-mineable concentrations.
* Root's accepted Afghanistan2002 supplement is included when computing post-correction coverage; it remains a separately attributed source.

**A source-service defect would have caused a severe false global result.** PP1802 WFS without an explicit bounding box returned only 13 North Carolina points and no polygons. An explicit `BBOX=-180,-90,180,90` returned full global features. Its polygon WFS response omitted geometry, so the authoritative full KML supplied polygons, matched against WFS identifiers and attributes. The first compressed-copper audit input also required `.geojson.gz`; missing required inputs now fail loudly rather than producing empty evidence.

## Per-resource verdicts

“Witnesses” means only that the sampled provinces have matching regional point records. Search windows are approximately ±2 degrees, except Staicele ±0.45 degrees. They are queries, not deposited ore areas, permissive geology or proposed spawn radii. Nearby points do not verify the exact named mine. `province-checks.json/csv` retain source identifiers, example names and citations so those distinctions are inspectable.

| Resource | Province queries | New source associations | Result after complements | Main unresolved geological/form issue |
|---|---:|---:|---|---|
| Fe — Iron | 11 | 328 | Missing spatial witnesses: Staicele | Deep concealed and low-grade iron remains evidence; do not replace Staicele basement ore with shallow limonite. |
| Au — Gold | 11 | 1482 | Sampled provinces have witnesses; incomplete globally | Historic/exhausted placers and districts matter even when absent from modern mine lists; native gold is not guaranteed by every elemental record. |
| Ag — Silver | 7 | 288 | Sampled provinces have witnesses; incomplete globally | Byproduct silver is not necessarily native silver or freibergite; historical workings and obsolete production must remain. |
| Pb — Lead | 10 | 307 | Sampled provinces have witnesses; incomplete globally | Galena versus weathered cerussite requires mineral evidence; deep concealed districts cannot be promoted to surface resources. |
| Zn — Zinc | 8 | 295 | Sampled provinces have witnesses; incomplete globally | Elemental zinc is not proof of sphalerite, smithsonite or hemimorphite; trace zinc is not a zinc orebody. |
| Bi — Bismuth | 6 | 19 | Sampled provinces have witnesses; incomplete globally | MCS refinery countries are not local ore locations; common unreported byproduct/trace Bi makes pure commodity mapping underinclusive. |
| Ni — Nickel | 11 | 186 | Sampled provinces have witnesses; incomplete globally | Laterite cannot become pentlandite. Retain exploration showings, depleted sulfide workings and uneconomic disseminated ore. |
| Cr — Chromium | 9 | 13 | Sampled provinces have witnesses; incomplete globally | Do not mark all ultramafic rock as chromite; small pods and subeconomic disseminations differ from giant layered deposits. |
| Ti — Titanium | 10 | 299 | Sampled provinces have witnesses; incomplete globally | Titanium minerals/concentrates not titanium sponge/refinery production; mineral sands do not identify ilmenite versus rutile. |
| Mn — Manganese | 8 | 90 | Sampled provinces have witnesses; incomplete globally | Most Mn evidence is oxide ore, not rhodochrosite; Latvia Mn-oxide assays are associated mineralization, not a separate proven Mn mine. |
| Hg — Mercury | 9 | 31 | Missing spatial witnesses: Pulganbar | A modern production filter erases Almaden/Idrija and other closed historic sources. Recycled Hg, export and byproduct recovery are not new cinnabar deposits. |
| W — Tungsten | 10 | 84 | Sampled provinces have witnesses; incomplete globally | Scheelite is not wolframite. Granite fertility, contacts, veins, depth and weathered/placer derivatives must be kept separately. |
| U — Uranium | 14 | 99 | Missing spatial witnesses: Chu Sarysu, Lagoa Real, Kirovohrad, Kyzylkum | MCS nonfuel tables do not supply a full U inventory; UDEPO includes occurrences and low-grade/unconventional resources, but is not acquired here. |
| Pt — Platinum/PGE | 8 | 181 | Sampled provinces have witnesses; incomplete globally | PGE or Pd-only evidence is not native platinum/sperrylite; historical alluvials need their own source-to-placer evidence. |
| V — Vanadium | 8 | 114 | Sampled provinces have witnesses; incomplete globally | Vanadium in oil/ash/phosphate is not vanadinite. Australia has reserves despite zero production in the cited table. |
| Mo — Molybdenum | 9 | 40 | Sampled provinces have witnesses; incomplete globally | Low-grade bulk-mined porphyry presence does not prove hand-recoverable ore. Associated Mo can be missed when source commodities are primary-only. |
| Sb — Antimony | 10 | 48 | Missing spatial witnesses: Krinj | Stibnite or antimony content does not prove tetrahedrite/freibergite; old workings and unmined resources remain relevant. |
| Al — Aluminium/bauxite | 12 | 46 | Sampled provinces have witnesses; incomplete globally | Exclude refinery-only countries from bauxite alarms. Aluminium-bearing clay/anorthosite/alunite is real subeconomic Al evidence, not bauxite. |
| Cu — Copper | 14 | 652 | Sampled provinces have witnesses; incomplete globally | 2015 global permissive polygons cover porphyry and sediment-hosted Cu, not all Cu systems. Native copper, oxidized ore and deep sulfide accessibility differ. |
| Sn — Tin | 11 | 247 | Sampled provinces have witnesses; incomplete globally | Modern producers omit historic Cornwall/Erzgebirge and exhausted placers. PP1802 adds global Sn; GSC World Tin/Tungsten OF7688 remains a priority detailed source. |

Association counts can include the same physical deposit in several databases and commodities. They are not counts of new independent deposits. The Pb/Zn shared GA layer is read from its explicit commodity label; a broad mineral-sands or PGE label remains broad and cannot silently become ilmenite or native platinum. MRDS status distributions and US record shares are retained in `metal-audit.json`; neither is used as an ore probability.

## Confirmed omissions still requiring spatial evidence

* **Fe, Staicele (Latvia):** core-confirmed deep iron ore is missing from imported spatial points. This is now recorded as nonspatial evidence, described below.
* **Hg, Pulganbar (Australia):** historical local ore was processed in 1914–1916, according to the [council's primary heritage investigation](https://www.clarence.nsw.gov.au/files/assets/public/community/files/heritage/thematic_history_copmanhurst.pdf). A smelter coordinate must not substitute for an orebody coordinate. This illustrates how modern-mine lists lose small historical sources.
* **U, Chu-Sarysu (Kazakhstan), Lagoa Real (Brazil), Kirovohrad (Ukraine), Kyzylkum (Uzbekistan):** primary IAEA and geological research document these systems, but the imported point package has no witness in the sampled windows. Ukraine and Uzbekistan have no U country witness at all. [IAEA classification and named examples](https://www-pub.iaea.org/MTCD/publications/PDF/TE1843_web.pdf), [primary Lagoa Real research](https://conferences.iaea.org/event/44/contributions/7944/), [World Uranium country history](https://www-pub.iaea.org/MTCD/publications/PDF/WOR_URA_web.pdf).
* **Sb, Krinj/Partsan (Pakistan):** the national survey records quartz-stibnite veins, showings and intermittent mining; Pakistan has no Sb witness in the imported layer. [GSP Metallogeny and Mineral Deposits, Metallic Minerals chapter](https://gsp.gov.pk/wp-content/uploads/2026/02/Matellogeny-and-Mineral-Deposits-of-Pakistan-Kazmi-and-Abbas.pdf).

These seven remaining empty queries, and the two repaired Bi cases, were independently checked against named primary sources. Other positive queries are a broad reconnaissance screen against official compiled deposits, not 187 independent field reviews. Offshore and Antarctic mineralization are not comprehensively audited. More minor, concealed, exhausted and poorly surveyed occurrences certainly remain unrepresented; absence must never become a hard zero in otherwise reviewed permissive geology.

## Staicele: real deep ore, not a shallow substitute

`latvia-deep-evidence.geojson` contains **two geometry-null records** for Staicele and Garsene (Subate). A [2024 LU analytical study](https://dspace.lu.lv/items/f9bf4048-f4d6-48fa-a19f-fe0c605bbfbe) examined archived LVGMC drill cores and identifies Superior-type banded iron formations. Only its public abstract was obtained; the full thesis is restricted.

The user's “about 700 metres” for Staicele agrees with the **688 m** value in a [University-hosted academic geography account](https://priede.bf.lu.lv/LatvijasDaba/ainava/); the same account gives **945 m** at Garsene. This is a secondary depth account, not a digitized borehole survey or full deposit-top surface. Exact borehole coordinates, extent, top/base geometry and vertical conversion to Earth have not been acquired. The records therefore cannot enter point heatmaps or be moved to shallow host rock.

A [2026 LU/Tartu primary research abstract, page 595](https://konference84.lu.lv/fileadmin/user_upload/lu_portal/84konference/LU-84_konf-tezes_2026_.pdf#page=595) reports renewed study and varied oxide assays from limited cores. These remain oxide fractions, not elemental metal percentages or certified reserves. Associated Mn at Staicele and Co at Garsene are discussed as below industrial concentration thresholds with sparse sampling. This supports recording mineral presence independently of profitability; it does not establish separate exploitable Mn/Co mines. Deep crystalline iron and shallow peat/wetland bog iron require different geological rules.

## Historical, subeconomic and prospective coverage

Every metal row explicitly retains these as separate coverage questions. None is considered complete:

1. **Occurrence:** keep measured minerals and drill-core evidence even when production is zero. A commodity association, trace mineral and ore-grade body are different evidence classes. Missing grade/economy/depth stays unknown.
2. **Historical accessibility:** retain closed/exhausted workings and placer sources; a database status of Past Producer alone does not prove ancient extraction. National/archaeological inventories must establish period and accessible material. Modern refining locations are excluded from bauxite/Bi ore-location reasoning.
3. **Concealed and subeconomic resources:** preserve real host rock and depth. Dilute disseminations may be geologically real while unsuitable for hand extraction. Identified unmined deposits help, but do not enumerate all low-grade mineralization.
4. **Prospective geology:** a defensible low-chance background needs mineral-system, host, age, structural and alteration evidence. “Granite” alone is not tin-fertile granite; generic ultramafic rock is not a chromite pod or nickel sulfide body. Cu's existing porphyry/sediment-hosted assessment tracts do not cover all copper systems. No universal global potential layer was acquired for the other metals.

The next broad priorities are [IAEA UDEPO](https://infcis.iaea.org/UTHDEPO/Deposits), [GSC World Tin and Tungsten OF7688](https://doi.org/10.4095/295581), and national geological-survey occurrences and mineral-potential products. Their coordinates, dates, rights and commodity/mineral distinctions need review before import. Country production/reserve tables identify alarms; they cannot repair a spatial hole. GSC and UDEPO spatial inventories remain unacquired in this pass.

## Reproduction, validation and attribution

Run in order: `acquire_normalize.py` (cached downloads; source acquisition), `write_reviewed_evidence.py`, `audit_metals.py`, `validate_metals.py`, `write_audit_report.py`. Audit/validation use Python standard library and existing cached inputs. Required input failure is explicit. Optional empty specialist layers are allowed. Root's nonmetal supplement is a required post-audit input.

`acquisition-validation.json` records raw SHA256 hashes, WFS/KML matching and every GA service count. `validation.json` records **16,018 passing checks**: finite coordinates, closed rings, unique source IDs, preserved cross-source coordinates, required inputs and all expected unresolved alarms. General polygon self-intersection topology, grade, depth and worldgen behavior are not validated here. No runtime code changed.

`source-inventory.json/csv` records primary citations, dates, acquisition state and rights. The GA derivative is attributed to **Commonwealth of Australia (Geoscience Australia) 2026, CC BY4.0**; fields and coordinate representation were normalized, and original attributes are preserved. PP1802 attribution is **US Geological Survey, 2017, DOI10.5066/F7GH9GQR**; its regional-scale limitation remains attached. Source availability and a successful audit do not imply permission to distribute every external document inside the mod.
