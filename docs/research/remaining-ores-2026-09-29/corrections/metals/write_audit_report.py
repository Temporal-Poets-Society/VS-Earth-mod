from pathlib import Path
import json,csv,collections
B=Path(__file__).resolve().parent
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
audit=read(B/'metal-audit.json');probes=read(B/'province-checks.json')
sources=[
 ('usgs_pp1802_2017','USGS Critical mineral resources of the United States: spatial data','https://mrdata.usgs.gov/pp1802/','https://doi.org/10.5066/F7GH9GQR','Acquired: full global KML and WFS; 2125 features, all commodities retained','USGS public data; preserve attribution and regional-scale use constraints in metadata.','2017; extracted2026-09-29'),
 ('pp1802_metadata','USGS PP1802 data metadata','https://mrdata.usgs.gov/pp1802/metadata/pp1802.xml','','Acquired; warns against spatial analyses below regional scale','Source metadata retained in raw/','2017'),
 ('ga_aimr','Geoscience Australia identified mineral resources service','https://services.ga.gov.au/gis/rest/services/AustraliasIdentifiedMineralResources/MapServer','https://www.ga.gov.au/aimr2025/australias-identified-mineral-resources','Acquired:16 metal layers;3205 points including deposits and producing mines','Creative Commons Attribution4.0; Commonwealth of Australia (Geoscience Australia)2026. Changes: selected layers, converted to WGS84 GeoJSON, normalized fields; originals retained.','Service copyright2026; record source AIMR2025'),
 ('mcs2026','USGS Mineral Commodity Summaries2026','https://pubs.usgs.gov/periodicals/mcs2026/mcs2026.pdf','https://pubs.usgs.gov/publication/mcs2026','Acquired PDF/text; country production/reserve alarms only, not spatial orebody evidence','USGS public publication; country facts paraphrased; do not treat a refinery as a local mine.','2026 tables largely describing2025 estimates'),
 ('mcs2026_data','USGS MCS2026 data release','https://www.usgs.gov/data/mineral-commodity-summaries-2026-data-release','https://doi.org/10.5066/P1WKQ63T','Reviewed source/rights; spatial footprints not supplied','Official release CC0','2026'),
 ('usgs_existing_global','USGS major mineral deposits of the world','https://mrdata.usgs.gov/major-deposits/','','Existing package imported; sampled named provinces compared to point witnesses','Original citations and metadata in existing package; no new global-completeness claim','Existing source snapshot'),
 ('usgs_vms','USGS volcanogenic massive-sulfide deposits','https://mrdata.usgs.gov/vms/','','Existing package; exact Boliden record640 supplies coordinate for reviewed Bi join','USGS coordinate retained unchanged; no new orebody footprint','Existing source snapshot'),
 ('nui_phao_usgs','USGS PP1802 chapterG, Fluorine','https://pubs.usgs.gov/publication/pp1802G','','Reviewed named W-F-Bi-Cu-Au occurrence; coordinates come separately from exact PP1802 point588','Paraphrased geological fact; underlying coordinate record cited','2017'),
 ('sgu_c438','SGU C438: Boliden ore mineralogy','https://resource.sgu.se/produkter/c/c438-rapport.pdf','','Primary mine study; Table42 names bismuth minerals','Facts only; no source figure/text redistribution','Historical SGU study'),
 ('sgu_c651','SGU C651: The Boliden sulphide deposit','https://resource.sgu.se/dokument/publikation/c/c651rapport/c651-rapport.pdf','','Primary mine study section7.1.10 describes Bi tellurides and collected mineral material','Facts only; no source figure/text redistribution','Historical SGU study'),
 ('bgs_bismuth','BGS geological availability of bismuth research','https://www.bgs.ac.uk/news/geological-supply-of-bismuth-is-greater-than-previously-understood/','','Primary researcher summary: geological Bi is broader than primary commodity mining statistics','Research context, not acquired spatial inventory','2022'),
 ('iaea_udepo','IAEA World Distribution of Uranium and Thorium Deposits','https://infcis.iaea.org/UTHDEPO/Deposits','https://infcis.iaea.org/UTHDEPO','Reviewed; priority global complement NOT acquired spatially','IAEA rights require review before bulk redistribution; resource estimates can be historical or low-cutoff, not always modern reserves','Live inventory reviewed2026-09-29'),
 ('iaea_types','IAEA global uranium deposit classification/inventory','https://www-pub.iaea.org/MTCD/publications/PDF/TE1843_web.pdf','','Reviewed primary global inventory: Kirovograd/Lagoa Real metasomatites, Chu-Sarysu roll-front, unconventional sources','Paraphrased facts only; no spatial redistribution','Inventory describes end2015 database'),
 ('iaea_kazakhstan','IAEA TECDOC1425: Kazakhstan uranium technical contribution','https://www-pub.iaea.org/MTCD/Publications/PDF/te_1425_web.pdf','','Primary map and deposit account documents Chu-Sarysu/Syr-Darya; pp96ff','Facts only; need original georeferenced records for map repair','Historical technical publication'),
 ('iaea_lagoa','UERJ primary Lagoa Real/Cachoeira research at IAEA URAM2014','https://conferences.iaea.org/event/44/contributions/7944/','','Primary geological abstract confirms albitite-hosted uranium; point geometry not acquired','Facts paraphrased','2014'),
 ('iaea_world','IAEA World Uranium','https://www-pub.iaea.org/MTCD/publications/PDF/WOR_URA_web.pdf','','Country exploration history includes Kizylkum sandstone and Boztau black-shale deposits','Facts only; no inferred polygon','Historical global review'),
 ('iaea_yuzhnoye','IAEA historical Yuzhnoye uranium deposit record','https://www-pub.iaea.org/MTCD/publications/PDF/TE_1629_CD/Deposits/652.html','','Ukraine dormant deposit, zero production; example of real resources outside mine filters','Facts only; historical status not asserted current','Historical2003-2007 database record'),
 ('gsp_metallogeny','Geological Survey of Pakistan, Metallogeny and Mineral Deposits of Pakistan','https://gsp.gov.pk/wp-content/uploads/2026/02/Matellogeny-and-Mineral-Deposits-of-Pakistan-Kazmi-and-Abbas.pdf','','Metallic Minerals chapter documents Krinj/Partsan/Qila Abdullah Sb showings, quartz-stibnite veins and intermittent mining','Primary national-survey facts only; occurrence coordinates not acquired','Historical book; current hosting is not study date'),
 ('pulganbar','Clarence Valley Council, Copmanhurst Shire thematic history','https://www.clarence.nsw.gov.au/files/assets/public/community/files/heritage/thematic_history_copmanhurst.pdf','','Commissioned2004 study pp45-46 describes local mercury extraction ending1916','Paraphrased facts; smelter point would not establish orebody location','2004'),
 ('lu2024','Dmitrijs Vorobjovs, Staicele and Garsene crystalline-basement iron ore thesis','https://dspace.lu.lv/items/f9bf4048-f4d6-48fa-a19f-fe0c605bbfbe','','Primary analytical study of LVGMC drill cores; public abstract only. Full thesis restricted and not obtained.','Facts paraphrased; no restricted file requested or copied','2024'),
 ('lu2026','Stinkulis and others: metal potential of deeply buried Latvian basement','https://konference84.lu.lv/fileadmin/user_upload/lu_portal/84konference/LU-84_konf-tezes_2026_.pdf#page=595','','Primary core/geophysical research abstract, printed and physical page595; ongoing research and limited assays','Facts paraphrased; oxide assay basis retained, no reserve conversion','2026'),
 ('lu_depth','Olgerts Nikodemus, Latvian physical geography','https://priede.bf.lu.lv/LatvijasDaba/ainava/','','Academic teaching account: Staicele688m, Garsene945m; secondary depth source, not original borehole log','Facts paraphrased; old economic assessment is not current feasibility','Undated historical teaching page'),
 ('bgs_cornwall','BGS Cornwall geological memoir','https://webapps.bgs.ac.uk/memoirs/docs/B01737.html','','Historical Cu/Sn systems, workings and geological source context; existing Cu/Sn package','Primary geological-survey context, not new spatial export','Historical memoir'),
 ('gsc_tin_w','GSC World Tin and Tungsten deposits, OF7688','https://doi.org/10.4095/295581','','Priority global historical/occurrence complement; NOT acquired. Existing package records download failure.','Crown copyright/redistribution terms need verification','2014 publication; much underlying information earlier'),
 ('ga_potential','Geoscience Australia mineral potential mapping','https://www.eftf.ga.gov.au/mineral-potential-mapping','','Priority prospective-geology complement; not imported here and not a universal commodity layer','Review individual map metadata/licenses before use','Current source discovery'),
 ('bgs_historical_stats','BGS world mineral statistics archive','https://www.bgs.ac.uk/mineralsuk/statistics/world-mineral-statistics/world-mineral-statistics-archive/','','Historical national alarms; production tables do not locate ore and miss earlier periods','Reference only; not acquired spatial data','Historical time series'),
 ('root_afghanistan','USGS Afghanistan2002 mineral inventory (root correction)','https://pubs.usgs.gov/of/2002/0110/','','Root additional-evidence.geojson includes722 accepted non-starred records; metallic subsets included in this audit','Root preserves original publisher-restricted starred exclusions','2002')]
rows=[dict(zip(['id','title','url','related_url','application','rights','source_date'],r)) for r in sources]
(B/'source-inventory.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
with (B/'source-inventory.csv').open('w',newline='',encoding='utf-8') as f:w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)

text='''# Metallic resource evidence audit — 29 September 2026

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
'''
for r in audit:
 status='Missing spatial witnesses: '+', '.join(r['empty_province_windows']) if r['empty_province_windows'] else 'Sampled provinces have witnesses; incomplete globally'
 text+=f"| {r['commodity']} — {r['label']} | {r['named_province_checks']} | {r['new_complement_records']} | {status} | {r['mineral_form_and_depth_caveat']} |\n"
text+='''
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
'''
(B/'README.md').write_text(text,encoding='utf-8')
print(f'Wrote source inventory ({len(rows)} sources) and per-resource narrative ({len(audit)} metals).')
