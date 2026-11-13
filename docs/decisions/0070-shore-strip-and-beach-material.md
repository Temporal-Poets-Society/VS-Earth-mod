# 0070: A shore strip at any height, of the coast's own material

Status: **built** in 2.0.0-rc.7 (local, not pushed). The author, 12 November 2026: "For beaches. I suppose almost all
beaches has either, sand, gravel or rocks and combination. Either we make it up or get data then"; then "my mind problem
is not so much height as not having soil till sea and but about correct material"; chose "OSM plus the rule".
`shores-osm-1.bin` was uploaded to the data release ed-v1 on the author's yes ("Yes, upload it", 12 November); the
download matches the store's size and SHA-256. Date: 2026-11-12.

## The problem

- A shore column was a beach only where its height was under `TerrainModel.BeachHeight` (3 m), fading to 0 a short way
  inland. Wherever GLO-90 reads the shore higher, the column was ordinary land: topsoil and grass ran into the sea.
  Jūrmala reads 9 m: a 90 m cell blends the 50-70 m of sand with the dune and pines behind it.
- Every beach was the same sand (`ground-rules-v1.json` `beachMetres` of `beach-sand`), apart from the older surface pass
  that put gravel on cold coasts.

## What was built

1. **The strip** (`TerrainModel.ApplyShoreStrip`, after islands, rivers, channels and straits): dry land within the
   strip's width of the sea (the coast outlines' distance, on their land side, where they decide), of a small island's
   coast, or of a lake of 50 km² or more (the lake outlines; half the width) is a beach at whatever height it stands
   (`Column.Beach`, `Column.Shore`). The height is not changed. Widths are real metres: sand 40, OSM beach without a
   surface 35, mixed 30, mud 30, no OSM beach 30, gravel 20, rock 8; ±35% along the coast (noise of about 400 m); at least
   one block, so at 1:50 and coarser the shore column itself. Glacier ice, river water and dry river beds stay as they
   are. Every beach of the older rules (low ground at the sea, a lake or a river) takes the class too.
2. **OpenStreetMap's beaches** (`shores-osm-1.bin`, `ShoreMaterialData`, a data-store file): natural=beach and
   natural=shingle (337,478, fetched from QLever's OSM planet endpoint on 12 November because every Overpass instance
   answered "too busy"), less the 56,279 CanVec "could be dunes or raised_beach" imports, on a 6″ grid per 1° square:
   6,908 squares, 5.4 million cells, 2.2 MB. Classes from the surface tag (`ShoreMaterial.OfSurface`): sand 47,822; gravel
   98,640 (gravel, pebblestone, shingle, natural=shingle untagged); sand and gravel 85; rock 764; mud 317; no surface
   133,570. Each beach is grown by one cell so the terrain's own shore line finds it.
3. **The material** (`GroundColumns.ShoreSubstrate`, ground columns cover the whole world): OSM's surface where it gives
   one; otherwise, and for OSM beaches without one, the coast's ground:
   - a cliff (slope over 1) is bare rock, unless OSM says beach;
   - the sea floor's mapped class beside it (EMODnet, where mapped): sand, gravel, mixed, mud, rock;
   - the cover: dunes, sand sheets, coastal, marine, deltaic, alluvial and outwash deposits give sand; carbonate shelves
     and karst carbonate sand; tephra black sand; till, colluvium and fans gravel and sand in patches; peat, lake clay,
     playas and marsh clays mud (sand on an OSM beach); thin cover and weathered rock bare rock (gravel on gentle ground
     or an OSM beach);
   - otherwise sand.
   Bare rock takes the loose cover away down to the bedrock. Sand 3 m (the rules' beachMetres), gravel 1.5 m, mud 2 m.
4. **No plants on it:** vanilla's tall grass (`BlockLayerAdjustments.TallGrassCoverPrefix`), land block patches
   (`EarthPlants.GeneratePrefix`: OnSurface, OnSurfacePlusUnderTrees, UnderTrees, ReplaceSurface) and, since a patch
   scatters round a centre the check sees alone, a PreDone pass (`EarthWorldGen.ClearShorePlants`) that removes any plant
   standing on a strip column; reeds (`tallplant-*`) stay. Trees already stayed off beaches (0069 rule 7).

New worlds record `EarthParams.ShoreRules` = 1 (ProtoMember 199). Older saves keep the 3 m beach of beach sand.

## Checked

- Ground column tests (21 new checks), river water tests (shore file: 9 new, the real file at Jūrmala and Chesil Beach),
  world options (758), model cache, column store, map and river index tests pass.
- Probe at 1:10: Jūrmala's shore, patchy before, is one strip 3-5 blocks wide; Chesil Beach is OSM gravel; the Tallinn
  coast is mostly the rule's. Buļļu sala (GSHHG's sea, the island layer's land) first came out all beach: the sea
  distance is now read only on the outline's land side. Render time unchanged.
- Bench, a new 1:10 world (`preset-earth-10`): Jūrmala has soil, then 4 blocks of sand to the water, the forest behind;
  after the PreDone pass 1 tall grass tuft in 160 strip columns (7 before). Tallinn: bare gravel and stones. Bergen: sand
  and grey stone in patches (till); that coast is mostly bare rock in reality, so till on steep rocky coasts may want
  rock: open.

## Open

- Credited on the author's yes ("Yes, add the credit"): earth-data's README, manifest.json, LICENSE.md and
  ATTRIBUTION.txt list `shores-osm-1.bin` and credit OpenStreetMap (commit 78dde14), and ed-v1's release text says so;
  the manifest and attribution attached to the release predate the file (release assets are never replaced).
- Till on steep rocky coasts (Bergen) gives sand and gravel patches where bare rock is usual: done in shore rules 3
  (below).
- The strip is one block above the water where the ground is (no slope down into the sea): the author said height is
  not the problem; not changed.
- The OSM cells are 6″ (about 185 m by 100 m at 57° N): where OSM's class and the rule's differ, the change follows the
  cells' edges.
- Seabed next to Jūrmala reads gravel (the rules' glaciated-sea default where EMODnet has no class there).
- GLO-30 for coastal cells only was discussed (about 100-150 MB estimated for one 90 m cell's nine 30 m cells along all
  coasts); not built.

## Shore rules 2 (12 November 2026, after the author's play on rc.7)

The author, in a new 1:10 world with Terrain Slabs: "Beach is not just 3-4 blocks of sand on top of coast. Several
blocks on same level as water and half block into water transition"; "Granite gravel on coast - no idea from where";
of the bench's flush beach with slabs, "this looks about correct"; of a Finnish island, gneiss under 5 blocks of cover
"and covered island by silt"; of Lahti, "beach and lake bed is pure granite or rock. No silt there"; of Normandy, "trees
are still too close to beach or coast"; of a till coast, "here sand is patchy".

Built (`ShoreMaterial.RulesVersion` 2, `ShoreZones.RulesVersion` 2; new worlds):

1. **The beach at the water's level** (`TerrainModel.ApplyShoreStrip`): the strip's width lies flush with the water where
   the shore is low (no more than `FlatShoreMetres` 25 m above the slope there) and the land not rugged (grid relief up to
   `FlatShoreRelief` 300 m), and not OSM's rock. Behind it a foredune of sand (`ShoreDuneShare` 0.5 of the width), and the
   ground rises at most a block per block for `ShoreRampBlocks` 4 more: a slope, not a wall. Fjords and rocky coasts keep
   their heights. Jūrmala at 56.9675° N 23.743° E: 4 blocks of sand at the water, then 110, 111 into the dune forest.
   Terrain Slabs adds the half-block edges.
2. **The shallows** take the shore's material (`Column.Shore` on water within `NearshoreShare` of the width): sand in front
   of a sandy beach, stones or bare rock off a shield's shore, the mapped sea floor where EMODnet has it; not the open
   floor's gravel or the lakes' default mud.
3. **No sea-floor mud on land:** a beach on land no longer reads the mapped sea floor (EMODnet's basin "Mud" laid silt over
   the Gulf of Finland's low islands). Till on a shield's shore (`Province` "shield") is gravel and stones.
4. **Older beaches only at the water:** low land the older rules made beach (whole low islands, Buļļu sala's inside) is land
   again beyond the strip, where the sea's, an island's or a big lake's distance is known; river banks and lake beaches stay.
5. **Big lakes from 1 km²** (was 50): the lake outlines give a lake's part in one tile (Vesijärvi at Lahti: 5.3 km²).
6. **Sand or gravel in stretches:** a mixed shore picks sand or gravel over about a kilometre, not 40 m blotches.
7. **Trees off every exposed sea coast** (shore zones 2): the open belt and the treeless scrub belt apply on high coasts too
   (the chalk of the Pays de Caux), except where the land is rugged (fjords).

Checked: ground tests (25 shore checks), river water, world options; probe at Jūrmala and Lahti; bench with Terrain Slabs at
Jūrmala (flush sand, sand shallows, low steps), Finland (59.93° N 23.66° E: gravel shores and shallows, no silt), Lahti
(gravel shore) and Normandy (sand, 10 blocks of open grass and shrubs, then forest).

Not changed, open: the cover under the Finnish island (gneiss under 5 blocks of till, sand and clay): Shangguan's depth to
bedrock and the till recipe's layers, each at least a block on the "Log, strong" depth curve; the author's call.

## Shore rules 3 (13 November 2026, EA-0056)

The author, 13 November: fix the open issues that change new worlds' terrain before the release. Bergen's coast came out
sand and grey stone in patches (till) where it is bare rock in reality. GLO-90's 90 m cells read these shores at a slope
of about 0.05, so the cliff test never fires there, and the ground columns give the fjord coasts thin till (GUM's
glaciated cover) or knob till (the ice-scoured crests).

Built (`ShoreMaterial.RulesVersion` 3, `GroundColumns.Sample.ShoreRules` from `EarthParams.ShoreRules`; new worlds): a
shore of thin or knob till where OpenStreetMap maps no beach is bare rock to the water, its shallows too
(`GroundColumns.ShoreSubstrate`, "rock (scoured till)"). OSM's pocket beaches between the rocks stay gravel, stones or
sand; thick till, outwash and every other cover are unchanged.

Checked: ground tests (shore rules 3: thin and knob till bare rock, its shallows rock, an OSM beach on thin till gravel
or sand, rules 2 unchanged, thick till unchanged); an offline probe of the shore columns: with rules 2 Bergen, Lofoten,
Connemara and Maine about half gravel and half sand, Bohuslän and Stockholm gravel; with rules 3 all bare rock, while
Finland's archipelago (till) stays gravel and Tallinn (outwash and till) is unchanged. Bench, a new 1:10 world at Bergen
(60.4035 N 5.2205 E): a strip of bare grey rock a few blocks wide at the water, grass and forest above, no sand or gravel.
Not seen by the author.
