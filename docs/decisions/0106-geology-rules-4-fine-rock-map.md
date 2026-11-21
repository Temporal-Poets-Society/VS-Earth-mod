# 0106: Geology rules 4, a 30″ rock map from full-resolution GLiM and Macrostrat

Status: **in Earth 2.2.0** (21 November 2026), built on `claude/geology-rules-4` (EA-0198, from `claude/terrain-2.2` at 2.2.0-rc.13, first in
test build 2.2.0-rc.14). Seen on the
bench (21 November 2026). The rock set is uploaded to `egts-v1`. Date: 2026-11-21.

The author asked for the rebuild: "we need to finish regional rock rebuild", and for the loose sediments (silt, marl,
clay) of ground columns to come after it. On Macrostrat: "Yes, together", one release with GLiM. How it is built is
Claude's, as maintainer (D72).

## Context

Rules 1–3 read the rock everywhere from GLiM's 0.5° grid (about 50 km cells) through 16 classes. Narrow carbonate belts
and contacts vanish, and so do the hosts vanilla's ores need. EA-0189 is an example: Bisbee's limestone.

Two finer maps exist:
- **Full-resolution GLiM** (Hartmann & Moosdorf 2012): 1.2 million polygons, each with a 6-letter code on three levels.
- **Macrostrat's carto map** (CC BY 4.0): national and regional maps at 1:1M or finer, where countries have them.

Measured before the rebuild: of land, 31 % changes rock family between the 0.5° grid and full GLiM.

## Decision

1. **A 30″ rock set.** `tools/ground/bake_rock_30s.py` bakes `global-rock-30s-1.egts`: 10° EGT1 tiles of 30″ cells,
   read lazily like the ground sets. Its fields:
   - `litho`: GLiM's code at the cell centre.
   - `macro`: Macrostrat's unit, where a map at 1:1M or finer covers the cell and its words name a rock. Its words are
     translated into GLiM's code vocabulary (`macrostrat_codes.py`).
   - `source`: which Macrostrat map drew the cell, for credits and the probe.
   - `bedrock`: under cover, water or ice, the bedrock beneath. It is taken in this order: Macrostrat's bedrock at the
     cell, GLiM's at the cell, else the nearest within 1° (rules 3's fill, decision 0101, at 30″).

   The set holds codes, not profiles, so the rules can change without a new bake. GLiM's cover codes (loess, dune sand,
   alluvium, laterite, organic sediment) stay in it for ground columns' loose sediments.
2. **The litho table** (`geology-rules-v4.json`, `litho`).
   - **Matching:** the first pattern that matches a code chooses the profile. Other codes take their class's profile, as
     in rules 3.
   - **Second level:** sets the sub-type:
     - carbonate (pure, shaly, sandy);
     - fine or coarse siliciclastics;
     - pyroclastic sediments;
     - marble (`mtpu`);
     - mafic metamorphics;
     - greenstone.
   - **Third level:** names rocks the source unit mentions besides its class. Pine Point's unmetamorphosed dolomite is
     `smmxmt`. So it changes the profile only for evaporites (gypsum beds), iron formation and coal measures.
   - **Profiles:** 18 new ones. New materials are vanilla's white marble (which vanilla's malachite, lapis and graphite
     already take as a host) and phyllite. No new blocks.
3. **Outlines.**
   - Rules 3's drawn ophiolite, layered-intrusion and granite outlines apply only over fitting rock of the finer map.
     Before, the Ural platinum belt's outline covered Mednorudyansk's carbonate.
   - New outlines where neither map knows the rock:
     - the Central African Copperbelt (the Roan Group's dolomites and ore shale);
     - the Otavi Mountainland (dolomite);
     - the Apuan Alps (marble).
4. **Contacts.** Contacts bend at the 30″ cell's scale. The wiggle is at most one cell (about 92 blocks at 1:10, 9 at
   1:100), so the saved contact width (96 blocks) no longer smears the finer map.
5. **The cover classes** for clay, peat and gravel lenses stay GUM's 0.5° classes. Ground columns replace them later.
6. **Per world.**
   - New worlds with regional rock save rules 4. EarthParams 223 `RockSet` records the set, decided at the first start
     before any chunk, as `GroundData` is.
   - The set comes from the ground release folder (`GroundDownloader.RockSets`), apart from the ground columns' sets.
   - A new world whose source lacks the set is saved with rules 3 for good, so its rock never mixes the two maps. Any
     other failure stops the start, with nothing saved.
   - A rules 4 world that lost its set fetches it again, or stops.
   - Worlds made before keep their rules.

## Checks

Carbonate-hosted deposits (USGS sediment-hosted Zn-Pb and Cu): of 809 places, the share on a profile with carbonate
beds is 43 % with rules 3, 59 % with GLiM alone and 61 % with GLiM and Macrostrat. Details are in
`docs/research/geology-rules-4-2026-11-21/`.

Districts:
- Mednorudyansk, Dartmoor, Viburnum, Hamersley, Donbas and the Peak District: right from GLiM.
- Bisbee: right from Macrostrat.
- The Copperbelt, Tsumeb and Carrara: right from the outlines.

All are in `RegionalGeologyTests`.

## Left for the author

- **The GLiM licence:** the full-resolution file has no stated licence (the 0.5° grid is CC BY 3.0). Ask its authors
  before a public release.
- ~~The data upload~~: done on the author's word ("yes upload", 21 November 2026). `global-rock-30s-1.egts` and
  `ATTRIBUTION-rock-30s-1.txt` are in `egts-v1`, the public download's SHA-256 matches, and `DataReleaseCheck` finds the
  set.
- **A look in game,** and when it ships relative to the public server (a no-wipe world keeps the rules it was made with).
