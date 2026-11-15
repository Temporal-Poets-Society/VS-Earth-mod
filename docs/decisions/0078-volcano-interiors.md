# 0078: Volcano interiors in real proportions (geothermal rules 3)

Status: **built** (EA-0067). New worlds only (0002): they save `GeothermalRules` 3; worlds of rules 2 keep their bodies
as 0057 part 2 made them. Checked offline at every scale and on the bench at 1:10; not seen in game by the author yet.
Date: 2026-11-14.

## Why

The author looked into Etna from below in noclip (13 November 2026, EA-0067) and called the volcano "built as slop
work": no visible magma chamber, a thin lava pipe up to the crater, black blobs and loose blocks around it.

Causes, found on the bench (a new 1:10 world, rules 2):
1. **Squeezed shapes.** Rules 2 sized every part in real metres on each axis. A world's underground is squeezed: 10 km
   between sea level and the bottom are some 94 blocks at every scale, while across a block is 5-200 m. At 1:10 Etna's
   chamber, 1.6 km wide and 600 m high, became a lava sheet 160 blocks wide and 6 high (y 91-97), and the 20 m conduit
   a 2-block pipe 340 blocks long. From inside the rock neither reads as a chamber or a conduit.
2. **Loose blocks.** The basalt sleeve and shell were written into whatever was there, so where vanilla's caves crossed
   them, single basalt blocks and short runs were left hanging in the caves.
3. **Black blobs.** In noclip, unlit caves and sealed lava both render black. Most of the "blobs" in the screenshot are
   vanilla's caves under the mountain; rules 3 removes the caves next to the volcano's parts but keeps caves in the rest
   of the cone.

## Decision

Rules 3 take the real sizes across and give each part its real proportions in blocks (`Core.VolcanoInterior`, planned
with the body in `VolcanoPlan`, so every chunk agrees). Rock by the volcano's magma, from the GVP's major rock type
(`tools/bake_geothermal_sites.py`, `GvpMagma`): basaltic (basalt, picro-basalt, trachybasalt, tephrite, foidite),
intermediate (andesite, basaltic andesite, trachyandesite, phonolite, trachyte) or silicic (dacite, rhyolite); unknown is
basaltic for shields, intermediate otherwise.

| Part | Rules 3 |
| --- | --- |
| Cone | Composite cones and shields on land: above the cone's base (the mean land height on rings around the vent, out to 20 km, shields 40 km, where it stops falling), plain rock becomes the volcano's own, in layers that follow the cone's smoothed profile: lava flows 2-4 blocks with ash beds 1-2 between (shields: flows 1-3, an ash bed every fourth layer). Ores in that rock stay, in the new rock. Valleys cut through the layers |
| Chamber | A lens at rules 2's depth, real across (half-width 800 m active, 500 m dormant, calderas x1.5; at least 5 blocks), its roof 0.4 of its half-width high (shields and calderas 0.3), its floor 0.6 of the roof; its outline 12 % from round. Lava at least 1.5 blocks inside a rind of crystallised magma; a basaltic chamber's floor holds settled crystals (peridotite). Dormant: a lava core 0.6 of the lens |
| Conduit | 40 m radius (shields 50 m), at least 1.5 blocks; a cupola over the chamber's roof, a funnel under the crater (a quarter of its radius), bowing gently (never by more than 0.6 of its radius); a 1.5-block rind of chilled rock (silicic: obsidian). Active: lava up to 3 blocks of crust under the vent (or into the lava lake); dormant: a plug of solid rock up to 4 blocks under the surface |
| Dykes and sill | At 1:50 and finer, where the conduit is at least 30 blocks long: blades of solid magma rock from the conduit towards the flanks, one block thick (two at 1:5), drawn as digital planes so every block touches the next by a face, ending 6 blocks or more under the surface. Composite cones 3 (dormant 2), shields 2 opposite each other (a rift), calderas 1 and a ring dyke over the chamber. A sill along the cone's base |
| Hot zone | Within 3 blocks of any part, a cave, water or non-solid block becomes the rock its column holds next to it, so caves end in their own rock and no lone block of the interior's rock is left in one |

Materials: basaltic flows basalt, ash beds scoria, rind basalt, floor peridotite; intermediate andesite and tuff;
silicic andesite flows, tuff, a granite rind and an obsidian sleeve. A missing block falls back to basalt or scoria.

Lava is everywhere at least 1.5 blocks inside solid rock: mining in lets it out, by design (0057), but nothing leaks on
its own. Craters, lava lakes and fresh ash are rules 2's.

**Not in rules 3:** eruptions; lava flows on the surface; glow or heat at dormant volcanoes; pumice (no block); volcanic
fields and minor vents (still no body).

## Verification

- `tests/VolcanoTests` (new): plans all 825 bodies at 1:200, 1:100, 1:50, 1:25, 1:10 and 1:5 with each preset's
  vertical scale and checks invariants (chamber under the conduit, room above and below, every part inside the map,
  conduit reaching the vent or lake); for twelve named volcanoes (Etna, Vesuvius, Fujisan, Hekla, Kīlauea, Mauna Loa,
  Campi Flegrei, Shasta, Nyiragongo, Stromboli, Erebus, Santorini) it checks that no lava block touches anything but lava
  or solid rock and that every dyke and sill block is face-connected to the conduit (1:10 to 1:200), and draws section
  images. All pass. At 1:10: 627 layered cones, 1,544 dykes, 579 sills, 42 ring dykes. Etna at 1:10: chamber y 92 (73-123),
  160 wide; conduit radius 4, cupola 14, funnel 5.
- Bench, a new 1:10 world (`preset-earth-10`), log "geothermal rules v3". Cutaways (WorldEdit slab south of each vent):
  Etna (basaltic), Vesuvius (intermediate) and Fujisan (basaltic) each show a domed lava lens with a flatter floor, a
  continuous conduit up to its crust under the crater, and the cone's layers following its slopes. Shots in the suite's
  `backlog/media/EA-0067-*`.
- A world made before (rules 2) with the new build: log "geothermal rules v2", and `/earth volcano` at Etna reports
  rules 2's body unchanged (chamber y 94, lava 80 x 3, conduit radius 2).

Files: `src/Core/VolcanoInterior.cs`, `src/Core/Volcanoes.cs` (magma family, `VolcanoPlan(v, model, rules)`,
`VolcanoPlan.Interior`), `src/EarthVolcanoes.cs` (`Interior`, `Fill`, the materials), `src/Core/Geothermal.cs`
(`RulesVersion` 3), `tools/bake_geothermal_sites.py` and `data/geothermal-sites.bin` (`GvpMagma`, read by `src/Core/GeothermalSiteFiles.cs`).

## History

- 2026-11-13: the author's report (EA-0067).
- 2026-11-14: built as geothermal rules 3; checked offline and on the bench.
- 2026-11-14 (code review ER-19): new worlds' `GenFixRules` 4 cap a crater-less vent's conduit at each column's own top:
  nothing of it above the top, lava only 2 blocks or more under it (`VolcanoInterior.capToTop`). Such a vent is wet (a
  crater lake, the sea, a shore), and where its column fell below the vent's top the conduit's lava stood in the water or
  the air. Vents with a crater, lava lakes and every older world are unchanged (`tests/VolcanoTests`, 1:50 to 1:5).
