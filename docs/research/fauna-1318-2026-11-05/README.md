# Animal ranges as around 1318: research of 5 November 2026

Input and provenance of [decision 0054](../../decisions/0054-animals-as-around-1318.md). The shipped result is
`tools/fauna-1318.json`; the build tools turn it into the 1318 fields of `assets/earth/config/fauna-species.json`
and `fauna-vanilla.json`.

## Method

1. `lost.py` lists, for every living taxon with a PHYLACINE 1.2.1 raster (fauna mods and vanilla), the TDWG level-3
   regions where its present-natural range has cells and its current range has none (`lost.csv`). That is the
   candidate set: places the counterfactual range adds. Most of it was lost after 1318 (colonial era).
2. `split.py` splits the candidates into three areas by region centre (`area-A.txt`: Europe, North Africa with the
   Sahara and Sahel, the Middle East, Central Asia, western Siberia; `area-B.txt`: South, East and Southeast Asia and
   Siberia east of about 75°E; `area-C.txt`: the Americas, Africa south of about 12°N, Australia and the Pacific).
3. Three research passes (one per area) decided per taxon and region whether the species lived there around 1318
   (1250–1400), with web sources; a fourth worked out where people kept chickens, sheep, goats and pigs and had
   fallow deer in 1318, and identified the game's "turdag" goat. Raw results: `raw/result-A.json` … `result-D.json`.
   No IUCN Red List pages are cited.
4. `merge.py` validated every key and region code and merged the areas; `assemble.py` reconciled overlaps
   (see below) and wrote `tools/fauna-1318.json`.
5. `current-check.py` counted the current-range cells each exclusion would remove. Because coarse boxes clipped
   places where species live natively today (reindeer in the Altai and Mongolia, hyenas in Sudan, Caucasus
   leopards, lynx in Iran), the profile never removes a taxon's current range.

## Reconciliation

- Domestic goat breeds and fallow deer take their 1318 lists from the domestic study; the wild-range exclusions for
  *Capra aegagrus* and *Dama dama* were dropped.
- Wild boar stays in Britain, Ireland and Finland: wild boar were (nearly) gone there, but domestic pigs kept in 1318
  went feral (the author's rule).
- The Bornean elephant's rule covers Borneo only; "exclude BOR" became boxes that keep north-eastern Sabah.

## Calls to review (low confidence)

- Brown bear's eastern North American edge (the Minnesota box, Iowa, Missouri, the Québec cut at 54°N).
- Moose kept in Colorado and Utah; mule deer edges (Northwest Territories, Nunavut, Missouri, Ontario, Wisconsin).
- The red fox's pre-contact southern limit in the eastern United States, and the two California boxes.
- Elephants and rhinos in China (box edges at about 25.5°N; Elvin's maps and Wen Huanran's papers not read directly);
  elephants in north-west India (Babur's 1520s limit projected back).
- Giant panda and wild water buffalo in southern China; Sunda clouded leopard on Java.
- Tiger in the Altai, Tuva, Mongolia and western Chita; tiger and lion edges in Iraq, the North Caucasus, Afghanistan,
  Sinai and Yemen.
- Eurasian lynx in Denmark, the Low Countries and Crimea; wild boar in Britain (last secure records 1251/1260).
- Black rhino kept in Burkina Faso, Togo and Benin on thin evidence.
- Chickens on Micronesian and small Polynesian islands; where chickens stop in Siberia; sheep and goats in Lesotho and
  the Free State; sheep in Korea, Java and Sumatra; goats on Ryukyu; pigs on Iceland; fallow deer in Sicily, Spain,
  Portugal and France.
