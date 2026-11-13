# Ore and gem model inventory (vanilla 1.22.7 vs Earth)

Sources: `assets/earth/config/ore-heatmaps-v1.json` (44 layers), `docs/remaining-ore-evidence.md`, `src/EarthOres.cs`,
`assets/earth/patches/ground-rocks-*.json`, vanilla `assets/survival` shapes/textures/blocktypes. Only block/item
variants Earth actually places are real; layers marked "map only" are evidence maps with no block or item behind them yet.

## Commodities in the heat maps
Metals: Fe Au Ag Pb Zn Bi Ni Cr Ti Mn Hg W U Pt V Mo Sb Cu Sn. Non-metals: B P N K NaCl S C_graphite C_coal F SiO2 Al
gypsum alum peat clay. Gems: diamond corundum emerald olivine garnet lapis topaz tourmaline beryl spinel.

## What vanilla already has
- Ore blocks (rock + `block/stone/ore/<type>1-3` overlay; 3 grades poor/medium/rich/bountiful): bismuthinite, cassiterite,
  chromite, galena, galena_nativesilver, hematite, ilmenite, limonite, magnetite, malachite, nativecopper, pentlandite,
  platinum, quartz_nativegold, quartz_nativesilver, rhodochrosite, sphalerite, uranium, wolframite. Ungraded/low-med-high
  blocks: alum, anthracite, bituminous/lignite, borax, cinnabar, corundum, diamond, emerald, fluorite, graphite, halite,
  kernite, lapislazuli, olivine, olivine_peridot, phosphorite, quartz, sulfur, sylvite, saltpeter.
- Ore chunk shapes (`item/ore/{poor,medium,rich,bountiful}`): the 19 graded metals above. `item/ore/ungraded`: alum,
  anthracite, bituminouscoal, borax, charcoal, cinnabar, clearquartz, coke, corundum, fluorite, graphite, halite, kernite,
  lapislazuli, lignite, olivine, phosphorite, quartz, sulfur, sylvite.
- Nuggets (`item/nugget` + textures): 19 (the metals, plus cinnabar, graphite, native gold/silver/copper).
- Rough gems (normal grade): diamond, emerald, fluorite, malachite, olivine_peridot, quartz, uranium; chipped/flawed grades
  add corundum and lapislazuli. Cut gems: corundum, diamond, emerald, fluorite, lapislazuli, malachite, olivine, quartz, uranium.
  The `ore-gem-rough` list (what drops from gem ore blocks) holds only diamond, emerald, olivine_peridot; corundum has chips/flaws/cut
  shapes but no gem-ore block.
- Orphan vanilla gem textures exist for amethyst, aquamarine, citrine, ruby, sapphire, topaz, onyx, turquoise
  (`textures/block/stone/gem`) with no item or shape behind them (not used by the drafts here, which carry their own).

## Missing for what Earth maps
| Commodity | Ore block overlay | Chunk shape | Nugget | Rough gem | Cut gem |
|---|---|---|---|---|---|
| topaz, tourmaline, beryl/aquamarine, spinel, garnet (map only) | none | n/a | n/a | none | none |
| corundum (ruby / sapphire) | overlay yes, one grey-blue; no ruby/sapphire split | ungraded yes | n/a | chipped/flawed only, no normal | one cut shape |
| Zn, Fe, Pb ... graded metals | yes | yes | Zn/Pb/Fe covered via sphalerite/galena/hematite | n/a | n/a |
| Ni (pentlandite), Cr, Ti, Mn, Bi, W, U, Pt, Sn, Cu, Hg (cinnabar) | yes | yes | yes except Pt, Mn partial | n/a | n/a |
| V, Mo, Sb, Au/Ag as own minerals | no (vanadinite, molybdenite, stibnite absent) | none | none | n/a | n/a |
| garnet, spinel, beryl, tourmaline, topaz in host rock | would need 5 new overlay textures (or reuse emerald/corundum overlay tinted) | none | n/a | none | none |
| B, P, N, K, NaCl, S, graphite, coal, F, SiO2, alum | yes | yes (ungraded) | graphite | n/a | n/a |
| gypsum / anhydrite | no ore block (it is a rock and plaster, not an ore) | none | n/a | n/a | n/a |
| peat, clay | not ore blocks | n/a | n/a | n/a | n/a |

Note from `docs/remaining-ore-evidence.md`: uranium, wolframite and corundum have ore variants but no standalone vanilla deposit;
Geology Additions-style garnet/beryl/corundum/spinel/topaz/tourmaline variants need species/colour evidence beyond a generic gem point.
Earth's own `src` does not place any block for topaz, tourmaline, beryl, spinel or garnet; they are heat maps only.

## New host rocks (gneiss, schist, quartzite, gypsum, dolostone)
`ground-rocks-ores.json` adds vanilla ore variants into gneiss, schist and quartzite (bismuthinite, cassiterite, emerald, galena,
hematite, ilmenite, magnetite, native copper, quartz+gold, quartz+silver, rhodochrosite, sphalerite, uranium) and
dolostone/others into vanilla deposits. Vanilla ore blocks are `base: block/stone/rock/{rock}1` plus `overlays: block/stone/ore/{type}1-3`,
so the ore texture is a transparent overlay drawn on top of whichever rock texture is the base. **No new ore textures are needed
for the new hosts; only the host rock base textures (`rock/gneiss1`, `schist1` ...) must exist**, which the ground-textures tool handles.
A host-specific overlay (e.g. brighter ore on dark gneiss) would be optional polish only.

## Proposal (priority order)
1. Rough gems for topaz (honey), tourmaline (watermelon), beryl/aquamarine, spinel, ruby/sapphire corundum, garnet: drafts done in
   `gems/` (habit-true shapes, own textures). Cut shapes could reuse vanilla's gem-cut shape format with new textures afterwards.
2. Ore overlay textures for those gems on rock (5 new overlays), drawn as small glassy crystals rather than flecks.
3. Chunk models for metals Earth adds beyond vanilla graded list: only vanadinite, molybdenite, stibnite, native platinum nugget
   (vanilla has `platinum` chunk but no nugget); graded variants sharing existing shapes with new colour textures are cheapest.
4. Block-scale additions vanilla lacks: crystal druse / geode half (amethyst, quartz, calcite lined cavity) as a cave decor block;
   nodule block (flint-like hematite, siderite or manganese nodules in limestone/clay); rock-salt and gypsum rose; a bare
   crystal-cluster block per gem to mark gem pockets. Vanilla only has `crystal-small`, `crystal-largecluster` and `crystal-layer`
   (quartz-like), which are not coloured per mineral.
5. Native copper/silver wire-like specimen items and ore chunks with visible crystals (pyrite cubes, galena cubes) improve readability.
