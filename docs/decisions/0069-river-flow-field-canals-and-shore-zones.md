# 0069: River water by a baked flow field, canals after 1318, and shore zones for plants

Status: **agreed and built** (proposed: the author, 12 November 2026: "Yes write up", of the baked flow field and the Kiel Canal's removal
as rc.7's water design; "Merit Hydro my mind is not useful as would address only large rivers"; and, of trees on the
sand and at the water's edge, "My mind some model should fix that. Coastal zones and river banks"); then "Yes, build it".
**Built in 2.0.0-rc.7; in Earth 2.0.0**, for new worlds on full GLO-90: rules 1–4 (the baked flow field, `rivers-flow-glo90-1.bin`), 6
(canals after 1318, works rules 2), 7 (shore zones), 8 (wet or dry by GIRES, `rivers-nonperennial-2.bin` in the mod) and
9 (newer reservoirs, dam rules 3), with channel rules 2; the GLO-90 files on `egl-v1`. **Rule 5 (trimming the ponds) is
not built here** ([Build](#build-rc7)); channel rules 5 does it in the game instead, for new worlds after rc.10
([0074](0074-one-width-along-rivers-no-mask-ponds.md)). Date: 2026-11-12.

## What the author reported

The author's new 1:10 world on 2.0.0-rc.6 (11–12 November, Riga and Jūrmala; full GLO-90, courses 4, lake outlines,
river outlines 3, dam rules 2, channel rules 1, Streams "all"):

1. A straight channel from the Lielupe to the sea at Jūrmala that "does not exist irl".
2. "Large ponds or lakes in river path" along the Lielupe.
3. A river dead end where "still water flows like from invisible source" (56.9915° N 24.1096° E).
4. The Daugava at the old Rīga HPP reservoir (56.8458° N 24.2973° E) "patchy with different colors and flow directions".
5. The Daugava has two mouths: the old one to the right "is also active while it should not be according our geo data"
   (Earth has not restored the 14th-century mouth). The course's channel "cuts into old river bed and mouth", the mouth's
   bed "has weird formations", and the wide mouth is deep only along the HydroRIVERS line, where "most of river should
   have depth for end and where it widens. Flow speed probably slows down as river widens."
6. The Kiel Canal "should not exist" (1318), "and it also have wrong flow direction as indicator for wrong flow data".
7. Sandy beaches gone, Latvia "full of trees"; the sand there is "full of trees", and trees reach the edge of rivers
   too.
8. "In Iran some rivers are being interrupted" (a 1:50 world, 33.3067° N 47.1935° E): two pools in a gorge with a dry
   bed of shale sand between them.

"For river flow and dead ends - should we have stream data to avoid dead end flow and other weird things?"

## What the probe found

A 1:10 test world with the same rules as the author's, rebuilt offline from its saved parameters (`earth-params` read
from the save; the rc.6 source and zip and the bench's GLO-90 folder; outlines, dams and GLO-90 attached as
`EarthElevationSystem.AttachServer` does). The author's own save was not read while their game ran.

1. **The Lielupe's straight channel is its own course, not a cut.** HydroRIVERS' last reach of the Lielupe (293749,
   127.7 m³/s, no next reach) runs straight north from 56.9688° N 23.8521° E to 57.0090° N 23.8503° E, through the dune
   strip; the real mouth is about 6 km east, where the mask's river outlines draw it. Courses rules 4 move a reach onto
   the mask's water only where that water lies near, so this reach stays. The river exit channel there (496) lies in
   the sea only (57.050 → 57.101° N, no land pieces): tall.15's agreement holds (mouth erosion only in worlds without
   outline coasts).
2. **The Daugava's second mouth is the same.** Its last reach (553238, 749.3 m³/s) runs from 57.0271° N 24.0854° E north
   through the old bed, which the mask has as water (a closed backwater today), to the coast at about 57.08° N 24.09° E,
   and its channel is drawn through the dunes at Vecāķi. The modern mouth is the mask's water and the sea. There is no
   river exit channel anywhere in 57.00–57.10° N, 23.98–24.18° E.
3. **The trench in the mouth.** Across the river at 57.0416° N: the mask's water from 24.0605 to 24.0760° E (about 170
   blocks) 3 blocks deep (`OutlineRiverLoneDepth`), the course's channel from 24.0715 to 24.0745° E (about 35 blocks)
   6–7 deep. Channel rules 1 cap outline water's depth by the courses near and shelve it to 3 blocks beyond their banks
   and valleys: one deep groove along the HydroRIVERS line, with steps of a block on each side.
4. **The ponds are the mask's river pieces.** Every column in them is river outline water. The mask marks the Lielupe
   unevenly: 511 m across at 23.74° E, 265 m at 23.77° E, where the course's measured widths are 45–160 m.
5. **The dead end** is a side arm the mask has as river water, joined to the Daugava at one end, 3 blocks deep. Outline
   water takes its flow from the courses near (`RiverNetwork.FlowNear`, `BlendNear`), so the arm carries the Daugava's
   current.
6. **Rīgas HES:** 0063 already found straight-edged patches of another depth and flow label there and made their edges
   wander (noise ±0.6 blocks on the depth, up to 0.3 radians on the flow). The water still carries eight flow labels
   blended from courses that run on through hundreds of blocks of wide water, so patches remain.
7. **The Kiel Canal** is in the mask as river water (0063 item 9: "most of the Kiel Canal" stays like side arms); 0064
   step 3 (canals after 1318) is not built. Its flow labels come from the nearest courses, which is why they point the
   wrong way.
8. **Beaches.** On 20 sandy coasts worldwide (Jūrmala, Liepāja, Ventspils, Palanga, Nida, Hel, Pärnu, Saulkrasti, Kolka,
   Sylt, Scheveningen, Biscarrosse, Praia de Faro, Rimini, Copacabana, Daytona, the Outer Banks, the Gold Coast,
   Skagen; ±150 blocks each), 74 % of the land columns within 2 blocks of the sea are beach on GLO-90 heights, 42 % on
   the built-in heights. Beaches are missing where GLO-90, a surface model, reads the shore high: Jūrmala 5 % (median
   shore 9.0 m: the dune pines), Sylt 0 % (13.9 m), Scheveningen 29 %, Daytona 39 %. Where they exist they are 2–5 blocks
   wide; a beach of 30–100 m is 3–10 blocks at 1:10. In game the sand is laid where the model has a beach (the author's
   screenshot at the Daugava mouth).
9. **Trees on the sand and at the water.** Vanilla's tree and shrub generation (`GenVegetationAndPatches`) only refuses
   a block of fertility 0; sand has 10 (`survival/blocktypes/stone/sand.json`). Earth's tree picker (`EarthTrees`)
   chooses coastal and riparian species where they are native but leaves no column bare by its distance from water, and
   the forest density (`EarthMapLayers.ForestAndShrubs`) does not know the shore. So the forest runs onto beaches and to
   the edge of every river and lake.
10. **The interrupted river in Iran is the Seymareh at its dam.** The Seimare Dam (about 33.29° N 47.20° E, a 180 m
    arch, normal level about 720 m, a reservoir of about 40 km, filled from 2011–2013) is not on the reservoir list
    Earth takes reservoirs out by (HydroLAKES, built on dam data from before it). So the mask's reservoir water is drawn
    as a lake (lake outlines), and the dam's gorge between its pieces is land. The Seymareh's course through the gorge
    (reaches 441940–445420, 191–193 m³/s) is a dry bed by the arid rule: the place counts as hot desert (vanilla
    rainfall 64.6), and the reaches' wet-source flow is 0.3 m³/s, because the wet share starts at rainfall 100 and the
    Zagros catchment reads 70–85. This is 0063's open item (the Karkheh at Al-Qurna). The probe ran at 1:10. The causes
    don't depend on the scale.
11. **The forest's pattern moved in rc.5.** The density multiplies Earth's forest potential by vanilla's forest noise at
    absolute block coordinates; rc.5's sizes moved every place, so where forest falls changed (the Jūrmala test place
    from 10 % leaves to 37 %, Amsterdam down). No rule changed.

## Proposal

New worlds only, each a version the world saves; older worlds keep what they saved.

1. **A flow field, baked offline** (`tools/bake_river_flow.py`, a new GLO-90 folder file `rivers-flow-glo90-1.bin`
   beside the river outlines, on the data release after the author's yes). We make the stream data ourselves, from what
   Earth already has: the mask's river water as river outlines 3 draws it (reservoirs out, dam corridors in), the sea and
   lakes it touches, and courses rules 4 with their discharges, down to Streams' 20 m³/s. MERIT Hydro is not used.
   - Each connected body of river water gets its **inflows** where courses enter it (each with its discharge, a
     tributary adding its own) and its **outflows** where the water meets the sea or a lake it drains to, or where the
     downstream course leaves it.
   - The flow through the body is solved once over its 90 m pixels: water moves from the inflows to the outflows, more
     easily where it is deeper (the depth of rule 3), never through the banks. Each pixel stores its direction and
     speed; the speed is the discharge over the cross-section.
   - What it gives: no current in dead-end arms and backwaters; slower water where a river widens; a direction that
     follows the banks, not the HydroRIVERS line; and which of a body's contacts with the sea is a mouth.
   - Where a river has no mask water (the many narrow ones the courses draw alone), nothing changes.
2. **The water's own way out.** A course's stretch that leaves a body of river water over land the mask calls land and
   reaches the sea or a lake, while that body reaches it by its own water, is not drawn: the Lielupe at 23.85° E and
   the Daugava at Vecāķi go. A gap the mask has inside one river (a bridge, a dam corridor, a narrow neck) is still
   crossed: it is the only way between two pieces of water.
3. **Depth across the whole width.** Water that carries flow is as deep as the course's channel for its discharge
   (channel rules 1's depth, with its deepening where the river is narrow), across the whole width, shelving to the
   banks at half a block per block as now. No more 3-block plain with a groove along the course. Water that carries
   none (dead arms, backwaters) keeps 3 blocks. The depth noise stays, so the steps that are left still wander.
4. **Still water where it is slow.** Vintage Story's water has no speed, only still or flowing in eight directions.
   Water slower than a threshold (0.15 m/s proposed, set on the bench) is still; faster water takes its label from the
   field's direction, which changes along smooth lines, not in patches. The 0.3-radian turn goes.
5. **One width along a river (the ponds).** River water further from the field's main flow line than 1.5 times the
   river's running median half width (over 2 km of river) becomes land, unless the widening is longer than three widths
   (a real widening, or a lake the lake outlines have). At the Lielupe the 511 m pieces come back towards the 265 m
   around them. Values proposed; tuned on the probe.
6. **Canals after 1318** (0064 step 3, for canals). Modern canals become land through `works-1318.bin` (kind 64,
   land): the Kiel Canal (1887–1895), the North Sea Canal (1865–1876), the Kakhovka irrigation canals and the other
   canals the mask has as rivers. The source is a dated list of canals (OpenStreetMap's canals, ODbL, credited; the
   download needs the author's yes); canals built before 1318 stay. Natural rivers and lakes along a canal stay (the
   Eider, the lakes the Kiel Canal passes through). The flow field helps find candidates: a body of river water that
   joins two seas or two rivers and that no course feeds carries no flow.
7. **Shore zones for plants** (the author: "Coastal zones and river banks"). Every dry land column gets its distance to
   the water (sea, lake or river, in metres), its height above that water, and the kind of shore, and the plants follow
   zones:
   - **Sea coast:** the beach (bare sand), then the foredune (grass, no trees), then dune scrub, then forest.
   - **Lake shore:** a reed belt in the shallows, then a sedge meadow, then forest.
   - **River bank:** the low bank up to the river's flood height (bare sand, gravel or mud, sedge and reeds, no trees),
     then a riparian belt (only the region's riparian species, willow and alder, as shrubs and trees), then the forest
     of the place.
   - Widths in metres, scaled by the world: a beach 30–100 m, a foredune 20–50 m, a riparian belt growing with the
     river's width. Real widths come from the literature, not chosen by look; values to be researched and proposed
     before building.
   - Beach columns get no trees, shrubs or tall grass. The tree and shrub picker (`EarthTrees`), the forest density and
     Earth's ground cover read the zones, so the forest thins towards the water instead of stopping at a line.
   - Species stay the regional catalogue's (its coastal and riparian lists); the zones decide only where they may grow.

8. **Wet or dry by a published per-reach map, not the rainfall proxy.** The global map of non-perennial rivers
   (Messager et al., 2021, Nature) classes every HydroRIVERS reach, the courses Earth draws, as flowing all year or
   not; licence and size to check, and the download needs the author's yes. A reach it has as perennial is never dried
   by the arid rule (the Seymareh, the Karkheh, the Tigris and Euphrates below the Zagros); the rule decides only where
   that map is missing.
9. **A newer reservoir list.** Reservoirs built after the dam data HydroLAKES was made from (the Seimare Dam, 2013)
   are taken out like the rest: a dam list with newer dams (for example Global Dam Watch, 2024; licence to check, the
   download needs the author's yes) joins HydroLAKES in the dam bake (0053 item 19, `tools/bake_dam_removal.py`) and the
   lake outlines' reservoir exclusion. The river then runs through its gorge as before the dam.

## Open, not in this proposal

- **Beach width on sandy coasts.** Where GLO-90 reads the shore high (dune forest, buildings), no beach is drawn. A
  sandy-coast source (for example the global sandy-beach classification of Luijendijk et al., 2018, or EMODnet's coastal
  types in Europe; licences to check) could give the beach its real width there. Rule 7's zones would then start from
  it.
- **The forest's pattern tied to the place**, so that it doesn't change with the world's size (finding 11).
- **The 1318 mouth of the Daugava.** Not restored; the flow field draws today's mouth.

## Verification planned

- **Probe at 1:10**, at every place above:
  - the Lielupe: the cut is gone, the ponds come back towards 265 m;
  - the Daugava mouth: one mouth, the depth across the width;
  - the dead end: still water;
  - Rīgas HES: flow labels along smooth lines;
  - the Kiel Canal: land;
  - the Seymareh at the Seimare Dam: one wet river through the gorge, no lake (and at 1:50, the author's scale there).
- **0063's places** (the Tigris, the Irtysh, the Tiber, Rotterdam, the Danube delta), checked again.
- **The beach and shore counts** above, for rule 7.
- **The bench at 1:10** (Riga, Jūrmala, Kiel).

## Costs

- The bake runs offline; its file size will be measured when it is made (the river outlines file it follows is 36 MB).
- In game the field is read like the outlines: per tile, when a chunk first needs it.

## Build (rc.7)

The author, 12 November: "Yes, build it". Downloads approved the same day: GIRES v1.0 (`GIRES_v10_shp.zip`,
1,658,289,777 bytes, figshare 10.6084/m9.figshare.14633022, with its 627,439-byte technical README) into
`rawdata/gires`, and OpenStreetMap canal outlines through the Overpass API into `rawdata/osm-canals`
(`tools/fetch_osm_canals.py`). Global Dam Watch v1.0 (CC BY 4.0) was already in `rawdata/global-dam-watch`.

Order and where each rule lands:

1. **Rule 8, wet or dry** (`tools/extract_gires.py`, `tools/bake_nonperennial.py` → `earthdata/rivers-nonperennial-1.bin`
   in the mod; `NonPerennialData`; `EarthParams.PerennialRules` 196). GIRES' licence: its metadata says CC BY 4.0, its
   technical documentation CC BY-SA 4.0; the derived file is under CC BY-SA 4.0, credited in `earthdata/ATTRIBUTION.txt`.
   The file holds GIRES' non-perennial reaches (15 m³/s and more) by position, class and discharge. When a model is
   built, each reach is matched to them (within 8 km of its middle vertex, discharge within 2 %); side arms take their
   downstream reach's class. A reach dry 30 days a year or more has no wet-source flow; every other reach has all of
   its flow, so the arid rule (still only in hot deserts) never dries a year-round river. GIRES (8.1 million reaches)
   has 619,723 of 20 m³/s and more, Earth's network without its side arms; a first match placed 620,973 of the model's
   624,024 reaches (533,986 year-round, 15,780 dry 1–29 days, 71,207 dry 30 days or more). The Seymareh and the Karkheh
   are year-round, the Draa dry for 30 days or more.
2. **Rule 6, canals** (built): `works-1318.bin` version 2 (146,283 bytes; works rules 2, `Works1318Data.Canal` = 8)
   keeps every RCE area of version 1 byte for byte (a bake without `--canals` reproduces version 1) and adds LAND|CANAL
   areas: OpenStreetMap's lines buffered by half their width (60 m where OSM has none) plus a 90 m pixel, and its
   water=canal areas by a pixel, cut to the water mask's river-class pixels (lake-class water along a canal stays: the
   natural lakes the Kiel Canal passes). Rules 1 worlds skip the canal areas (`LakeOutlineLayer` SkipFlags). 26 of 30
   canals found (`docs/research/works-1318-canals-report.json`): the Kiel Canal 2,406 pixels, the Karakum 10,187, the
   Grand Canal d'Alsace 4,427, ...; the Nieuwe Waterweg, the Corinth Canal, the White Sea–Baltic Canal and the Ismailia
   Canal have other names in OSM. Works rules 4 (0064, EA-0057, 13 November) adds the last three (29 of 30, in
   `works-1318-2.bin`); the Nieuwe Waterweg is kept, since the RCE maps have its course as the Maas mouth's open water.
3. **Rule 9, reservoirs** (baked): `tools/reservoir_list.py` adds Global Dam Watch's reservoirs that HydroLAKES lacks
   (7,233: 6,218 not in HydroLAKES, 1,015 real dams on what HydroLAKES has as natural lakes, built 1900 or later and
   15 m or higher; lake controls, dams by 1318 and reservoirs under 0.1 km² left as they are). The Seimare Dam is GDW 6937
   (built 2013, 180 m), on HydroLAKES lake 1427797, which HydroLAKES has as natural.
   - `dams-glo90-3.bin` (dam rules 3): 6,207,627 bytes, sha256 `dcc99785…f7299`, 3,488 squares, 149,953 course levels.
   - `lakes-glo90-2.bin`: 57,734,593 bytes, sha256 `51e2a4b6…f53d6`, 5,546,561 lakes; reservoirs left out 24,459 lakes
     (283,086 km²) against version 1's 18,539 (267,567 km²). Without Global Dam Watch the same bake reproduces version 1
     byte for byte (the water atlas caches rebuilt from the raw data).
   - `rivers-outline-glo90-4.bin`: version 3's rules on dam rules 3's reservoirs and corridors.
4. **Rules 1–4, river water** (built): `tools/bake_river_flow.py` → `rivers-flow-glo90-1.bin`. As built:
   - each tile is solved inside a window of a quarter degree of its neighbours (a river crossing the tile's edge, a
     mouth just beyond it: the Lielupe meets the sea just north of 57° N), and only the tile is kept;
   - a course touches a body of water when it comes within 3 pixels of it (courses run on the bank, step off and
     back); along a chain of reaches a body gets one inflow where the chain first touches it and one outflow where it
     last does, so a course stepping onto land at a reach junction no longer short-circuits the flow (the first try
     left the lower Lielupe at a median of 0.1 cm/s);
   - a mouth reach that leaves its body across land for the sea while the body reaches the sea itself is rerouted
     along the shortest path through the body to its own mouth: the Lielupe (reach 293749) after its vertex 2, 31 points
     to 57.0071° N 23.9331° E; the Daugava (553238) after its vertex 5, 13 points to 57.0629° N 24.0306° E;
   - speeds at Riga: the Daugava 31–118 cm/s across its width (a section mean near 0.45 m/s for 750 m³/s), the lower
     Lielupe 10–17 cm/s. Still water below 0.05 m/s (`TerrainModel.StillBelow`), not 0.15: the lower Lielupe is a slow
     lowland river and should keep its current; dead arms and backwaters fall well below;
   - in game: outline water takes the solved direction (no 0.3-radian turn), `Column.Still` where slower (no current
     labels, `RiverFlowAt` reports none), and where it carries flow the deeper of the courses' blended cap and the
     nearest course's channel depth across its width; the flow file is loaded before the model (its reroutes change the
     courses) and is part of the model cache's key (layout 4).
   - **Rule 5 (the ponds) is not built.** Trimming the mask's water further than 1.25 (or 1.0) half widths from the
     course in pieces shorter than 3 (or 8) widths, at the mask's 90 m, either barely touched the Lielupe's ponds (556 →
     463 m at 23.74° E, 6 % of the tile's river pixels) or notched the far bank where the course swings. The code is in
     the outline bake for study (`EARTH_TRIM_BULGES=1`); the files are baked without it. With rules 1–4 the ponds are
     calm water as deep as the river and continuous with it. Channel rules 5 ([0074](0074-one-width-along-rivers-no-mask-ponds.md),
     13 November) trims them in the game by a width profile along each reach instead.
5. **Rule 7, shore zones** (built, `ShoreZones`, rules 1): the widths from the research note — a beach bare; a low sea
   coast (shore under 12 m) bare of trees and shrubs within 40 m, of trees within 100 m, the forest thinning in by 200 m;
   a river's low bank bare for a quarter of its real width (8√Q m) up to 30 m, thinning in over as much again; a lake's
   sedge shore 15 m; no tall grass on beach sand. Decided per attempt by a position hash.
6. **Review and checks.** A Codex review of the build found six real faults, all fixed before the final bakes:
   GIRES matched only against its non-perennial reaches (the file now has all 711,038 reaches of 15 m³/s and more:
   600,576 year-round, 19,141 dry 1–29 days, 91,321 dry 30 days or more; 9,243,506 bytes, in the data store); side arms
   added their flow twice; a tributary joining on land lost its flow; bodies were 8-connected while the solve couples 4;
   still water was stored as 1 cm/s (now 0, `CarryMin` 0.02 m/s); river and lake zones reached cliffs above the water
   (now only ground up to 3 m above it).
   - `rivers-flow-glo90-1.bin`: 93,803,882 bytes, sha256 `d6cb96dd…f1954`, 9,400 tiles with flow, 448 mouth reaches
     rerouted (34 minutes on 20 workers).
   - Probe at 1:10 with rc.7's rules: the Lielupe's straight cut gone, the river to its own mouth; the Daugava one
     mouth, the old bed a still backwater short of the coast; across the Daugava at 57.0416° N 7–9 blocks deep over its
     whole width (was 3, with a groove of 7), the current turning smoothly 94–129°; the dead end at 56.9915° N 24.1096° E
     still; Rīgas HES's labels in broad bands, no specks; the Seimare reservoir gone, the Seymareh one river through its
     valley; the Kiel Canal gone (its slivers too, with the canal areas widened by three quarters of a pixel), the
     Rhine beside the Grand Canal d'Alsace kept.
   - Bench (AgentBridge, a new 1:10 world at Jūrmala, rc.7 with Cylinder World 1.1.0-rc.3 and Hydrology 0.6.2-rc.5): every
     file attached (the flow with 448 reroutes, dam rules 3, lake outlines 2, river outlines 4); the dead end
     `water-still-7`; the Lielupe's pond at Bulduri still, its channel `water-ne-7`; the Daugava's mouth `water-nw-7`,
     9 blocks deep; the Lielupe's banks sand without trees at the water; no channel north from 23.85° E. Jūrmala's sea
     shore is grass to the water with the trees set back: no beach sand, as the model flags none there (GLO-90 reads the
     shore at 9 m; open, below).
   - Tests: `RiverWaterTests` 42 checks, EarthWorldOptionsTests 758, ModelCacheTests 133, RiverIndexTests, ColumnStoreTests,
     EarthMapTests pass. WetlandTests, ZoneTests, SoilTests and FloodplainTests need the old data packs' folder layout and
     were not run (their readers are unchanged).

7. **Data release.** The author, 12 November: the four GLO-90 files uploaded to `egl-v1` ("All needed"); the GIRES file
   not to `ed-v1` ("Not yet"; of its 9 MB: "can be separate file then"). Rule 8's file became version 2,
   `earthdata/rivers-nonperennial-2.bin` in the mod (20,575 bytes): one class per reach of courses rules 4's list (key
   9693b65a3a265a62, checked before use), matched at bake time against every GIRES reach of 15 m³/s and more (621,120 of
   624,024 matched; 536,972 year-round, 15,799 dry 1–29 days, 71,253 dry 30 days or more; the side arms take their
   river's class). Version 1 (9.2 MB, every GIRES reach by place, matched when a world started) was never released.
8. **The ponds, from the mask** (the author asked to see it): the GLO-90 mask has the Lielupe two pixels (about
   150–180 m) across almost everywhere and three at bends and diagonals. Where three, the outlines drew the mask's water
   (about 270 m, rounded off); where two, the narrow pieces are left to the course, whose channel was its flow's usual
   width (90 m for 128 m³/s): thin and wide by turns. **Channel rules 2** (`RiverNetwork.MaskWidthHalfWidth`): the
   channel follows the mask's measured width where it is wider than the flow's too (up to `MaxWiden` 3 times, never
   under half), depth as the flow's; loop cutting keeps the narrowing rule, so courses and the baked flow are
   unchanged. On the probe the Lielupe at Jūrmala is one band now; one rounded piece stays where the mask bulges at a
   bend (the author: "then do it").

## History

- 2026-11-12: proposed, after the author's reports on rc.6 (11–12 November) and the offline probe of a 1:10 world.
- 2026-11-13: rule 5 done another way by channel rules 5 ([0074](0074-one-width-along-rivers-no-mask-ponds.md), EA-0055).
