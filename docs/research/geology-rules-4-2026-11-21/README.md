# Geology rules 4: full GLiM and Macrostrat at 30″ (21 November 2026)

For EA-0198, on `claude/geology-rules-4`. Rules 4 read the rock from full-resolution GLiM instead of GLiM's 0.5° grid.
The first sections are step 1 (GLiM alone); the last one is the final set with Macrostrat, which the game reads
(decision 0106).

## The bake

`tools/ground/bake_rock_30s.py <LiMW_GIS 2015.gdb> <out.egts>` rasterizes GLiM's 1,235,259 polygons (Eckert IV) to
30″ cells, in 10° EGT1 tiles.

Each cell has two category fields, with GLiM's 6-letter code as the legend (437 codes, `glim-codes.tsv` with each one's
share of land):
- `litho`: the code under the cell's centre.
- `bedrock`: only where `litho` is cover, water, ice or no data. It holds the nearest bedrock code within 1°, as rules
  3's cover fill does (decision 0101). Elsewhere it is nodata, which means "the same as litho".

Results:
- **Size:** 388 tiles, 31,193,620 bytes, SHA-256 `57ba0655…49bd0`. The run takes about 3 minutes with 4 workers.
- **Correct cells:** 3000 random cells in two tiles all match exact point-in-polygon at their centres (`egts_pip.py`).
- **Needs Python packages:** pyogrio, shapely, pyproj, scipy and rasterio. rasterio was added to Earth's `.venv` for
  this.

## The code table (`geology-rules-v4.json`, `litho`)

Written by `make_rules_v4.py` from rules 3.

- **Matching:** the first pattern that matches a code wins. Other codes take their class's profile, as in rules 3.
- **Second level** (from the GLiM paper, table 1): it is a sub-type and sets the profile:
  - `pu` carbonate: pure carbonate; marble under `mt`;
  - `sh` fine-grained and `ss` coarse-grained sediments;
  - `py` pyroclastics: volcaniclastic sediments;
  - `am` mafic metamorphics and `gr` greenstone.
- **Third level:** it names rocks the source unit *mentions* besides its class, not a property of the whole unit.
  Pine Point's unmetamorphosed dolomite is `smmxmt`. So `mt`, `vr`, `pr` and `sr` change nothing. Only these do:
  - `ev` evaporites: gypsum beds;
  - `fe` iron formation;
  - `cl` coal measures.
- **New materials:** marble (vanilla `rock-whitemarble`, which vanilla's malachite, lapis and graphite already use as a
  host) and phyllite (vanilla). No new blocks.
- **Profiles:** 16 new ones. Metacarbonate and metasedimentary are left for drawn provinces.

Shares of land by profile, before the cover fill: `profile-shares.txt`.

## Checks (`rules4_check.py`, `rules4-check.txt`)

**809 carbonate-hosted deposits** (USGS sediment-hosted Zn-Pb and Cu sets, recorded host rock carbonate):

| | Rules 3 (0.5°) | Rules 4 (30″) |
|---|---|---|
| Profile has carbonate beds | 43 % | 59 % |
| Carbonate top bed | 19 % | 29 % |
| Australia, carbonate beds | 18 % | 76 % |
| Americas | 46 % | 62 % |
| Asia | 45 % | 60 % |
| Europe | 52 % | 63 % |
| Africa | 23 % | 27 % |

**Districts.**
- Fixed:
  - Mednorudyansk: carbonate;
  - Dartmoor: granite;
  - Chuquicamata: plutonic;
  - Hamersley: iron formation;
  - Donbas: coal measures;
  - Viburnum Trend: pure carbonate;
  - Mount Isa and Burra: mixed sediments;
  - Kalgoorlie: mafic;
  - Silesia: evaporite-bearing mixed sediments.
- Still wrong, because GLiM maps them otherwise:
  - Tsumeb, Kitwe and Kolwezi: GLiM has metamorphic;
  - Carrara: carbonate-sandy, not marble;
  - Bisbee: siliciclastic;
  - Sudbury: siliciclastic. Its province, as in rules 3, sets the layered intrusion.

  These are for Macrostrat (step 2) and drawn provinces (step 3).

## Final set: GLiM with Macrostrat (`rules4-check-final.txt`)

`global-rock-30s-1.egts`:
- **Size and hash:** 388 tiles, 45,399,312 bytes, SHA-256 `725e75a7…1dcc98`.
- **Macrostrat input:** the carto tiles at zoom 7 over all land (8,309 tiles, 114 MB, in
  `data\earth-raw\macrostrat\carto-z7`). From them, 943,740 units of the 83 sources at 1:1M or finer, credited one by
  one in `assets/earth/config/geology-macrostrat-sources.txt`.

Translator fixes after the first bake (`flips.py` lists where Macrostrat changes carbonate):
- **Types count as mentions.** A carbonate or evaporite lithology type counts as mentioned even when the words leave it
  out. Missouri's Ozark units are "chert, sandstone" in words.
- **"limes" is limestone.** Ireland's maps use the abbreviation.
- **Weak units.** A unit whose words name no rock (only types) answers only where GLiM maps no bedrock.

| Carbonate-hosted deposits (809) | Rules 3 | GLiM alone | GLiM + Macrostrat |
|---|---|---|---|
| Profile has carbonate beds | 43 % | 59 % | 61 % |
| Carbonate top bed | 19 % | 29 % | 30 % |
| Americas | 46 % | 62 % | 66 % |
| Asia | 45 % | 60 % | 64 % |
| Australia | 18 % | 76 % | 75 % |
| Europe | 52 % | 63 % | 59 % |
| Africa | 23 % | 27 % | 26 % |

- **Where Macrostrat changes carbonate:** it adds it at 49 places and removes it at 17. Most removals are real
  disagreements: a volcanic cover over buried limestone in Mexico, Italian phyllite over Alpine carbonate.
- **Districts:** with the drawn outlines, all 18 checked districts are right (`RegionalGeologyTests`). Bisbee, Burra and
  Pine Point come right from Macrostrat.

## Bench look (21 November 2026, the author: "Yes, bench look")

A private AgentBridge bench ran test build `2.2.0-rc.13-rock4.1`, a new 1:10 world with regional rock. The rock set was
fetched from a local source through the real download and check.

- **Server log:** rules v4 with 41 envelopes, no errors.
- **Pits cut at four places:**
  - Carrara: white marble with darker schist bands, from the Apuan Alps outline.
  - Peak District: grey limestone with tan sandstone bands (carbonate-sandy, from Macrostrat source 23).
  - Dartmoor: granite.
  - Bisbee: schist at the teleport point. Paleozoic limestone begins about 50 blocks west.
- **The Bisbee contact:** a trench 140 blocks long shows the contact bending at the scale of a cell.
- **Fixed after the look:** `/earth geology` and the start log printed the saved 96-block contact width. They now print
  the width used, at most one 30″ cell (about 92 blocks at 1:10).

Shots: `C:\VS mods\work\regional-rock-fix\bench-shots`.
