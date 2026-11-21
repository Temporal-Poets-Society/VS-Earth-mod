# 0101: Geology rules 3: envelopes, rock under the cover, continental basement, evaporites

Status: **in Earth 2.2.0** (21 November 2026), built on `claude/geology-rules-3`, merged into `main` (19 November 2026); new Earth worlds
with regional rock (and ground columns, which hand off to it). Decided by: the author's order ("2nd to fix is regional
rocks with vanilla ores", 18 November 2026; EA-0173), how it is built by Claude as maintainer (D72). Not yet looked at in
a graphical client.

## The problems (rocks audit, 18 November 2026)

- No ophiolite or ultramafic rock anywhere at the surface, and the great layered intrusions mapped as sediment or granite
  (R3): Troodos sandstone, Oman conglomerate or granite, the Bushveld granite.
- Mantle peridotite under every continental flood basalt (R2), about 46 blocks down (Deccan, Siberian Traps, Paraná,
  Ethiopia), with its olivine.
- The 0.5° grid lost granite districts and islands (R4): Cornwall 0 % granite, Bangka in a sea cell.
- GLiM's unconsolidated cover (class 1, 28 % of ice-free land) kept vanilla's random strata in "regional" mode (R8):
  Vienna's basin peridotite, Amazonia andesite, with seams at the cover's edges.
- The evaporitic profile had no evaporite (R15).
- The clay and peat slope rule looked 12 blocks to each side at every scale, 120 m at 1:10 (R10).
- Rules 2 could change silently (R7).

## Decision

New worlds save `GeologyRulesVersion` 3 (`geology-rules-v3.json`; worlds made with 1 or 2 keep theirs):

1. **Envelopes** (author-drawn after published geological maps, approximate outlines like the chalk envelopes):
   - ophiolites, with a profile of pillow basalt, chert, sheeted dykes and gabbro over mantle peridotite: Troodos, Semail,
     Vourinos, Bay of Islands, Zambales, the Papuan belt, New Caledonia, Kempirsai, the Urals platinum belt;
   - layered intrusions, gabbro with peridotite layers over granite: the Bushveld's three limbs, the Great Dyke, Stillwater,
     Sudbury, Duluth;
   - oceanic volcanic islands, basalt and gabbro over peridotite: Iceland, Hawaii, Réunion, the Canaries, the Azores,
     Galápagos;
   - granite districts: the five Cornish plutons, Eibenstock, Bangka, Belitung, the Malaysian Main Range, the Jos Plateau.
2. **Continental flood basalt on continental crust**: the mafic-volcanic profile's basement is granite (oceanic islands
   keep peridotite through their envelopes).
3. **Rock under the cover**: a cover cell takes the bedrock class of the nearest mapped bedrock cell within 2 cells
   (`coveredFillRadiusCells`), keeping its own sediment class; farther from bedrock a new basin-fill profile (claystone,
   sandstone, shale, conglomerate over granite). No cell is left to vanilla's strata (17,790 cover cells: 14,265 take
   bedrock, 3,525 basin fill).
4. **Evaporites**: the evaporitic profile carries two gypsum beds (Earth's own `rock-gypsum`) and two rock-salt beds
   (vanilla's `rock-halite`).
5. **Clay and peat relief over 1.2 km at every scale** (12 blocks at 1:100 as before, 120 at 1:10).
6. **Rules 2 pinned**: a golden of its cell lookup, fingerprint and strata (`golden-v2.json`).

## Considered and left out

- **Beds in metres through the depth curve** (the audit's R1). Built and measured on the play styles' layouts: the share
  of land showing a lower bed at its surface became 5.3 / 19.7 / 10.9 % at 1:100, 1:25 and 1:10 against rules 2's 12.0 /
  13.1 / 18.5 %, more uneven and far lower at the default. The audit's unevenness came from test layouts finer near sea
  level than any play style. Rules 3 keeps rules 2's 66-block beds.
- **Basement by a crust map** (CRUST1.0) for every profile, and **sequence thickness from sediment thickness** (R9): later,
  with data checks.
- **A finer lithology raster** (GLiM polygons, IGME 5000; R4, R13): a separate step with a licence check.
- **Earth's rocks in vanilla's sand and gravel layers** (R6): Earth's own rocks (gneiss, schist, dolostone, quartzite,
  gypsum, added to vanilla's rock family by Earth's patches) and tuff are not in vanilla's rock strata list, so vanilla's
  sand, gravel and beach layers are skipped where they are the top rock; a later step (it needs a patch of vanilla's
  block layers).
- **Lead in crystalline rock** under vanilla ores (EA-0174): vanilla and Earth had lead ore blocks only for sedimentary
  rocks. The author allowed new blocks (19 November 2026): galena and its silver in granite, slate, phyllite, gneiss and
  schist are part of rules 3, [0102](0102-galena-in-crystalline-rock.md).

## Consequences

Census on dedicated servers, regional rock with vanilla ores, rules 2 against 3
([geology-rules-3-2026-11-19](../research/geology-rules-3-2026-11-19/)): ophiolites and intrusions show their peridotite and
gabbro (Troodos chromite 21 → 47 a chunk column at 1:100, Oman and Kempirsai olivine about 600-800, Sudbury chromite);
flood basalts sit on granite (their olivine gone); evaporite basins carry gypsum and rock salt (not at the census's places); Vienna, Amazonia and West Siberia have Earth's rock under the cover
(West Siberia's coal doubles); the Baltic envelope is unchanged.

## Tests

`RegionalGeologyTests`: the v2 golden (cells, fingerprint, strata in three layouts); v3 at named places (eleven envelopes,
the Deccan's granite basement, gypsum and rock salt, no profile starting with tuff or halite, no cover left to vanilla's strata,
West Siberia and Amazonia with bedrock, the relief footprint at 1:200, 1:100, 1:10 and 1:5). `TerrainRulesTests`: the
facts `CoveredFill` and `ReliefInMetres` from 3. `EarthWorldOptionsTests`: the 295 pinned new worlds differ only by the
geology rules version (checked by forcing it back to 2).
