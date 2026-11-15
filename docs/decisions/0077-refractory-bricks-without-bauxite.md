# 0077: refractory bricks without bauxite; kyanite, sillimanite and andalusite; volcanic-ash mortar

Status: **built** (EA-0068, EA-0069; the author, 13 November 2026, via the "Industrial Story mod dependency" session:
implement the missing minerals and the no-bauxite refractory route before the Earth release). New worlds only (0002).
Not seen in game by the author yet.
Date: 2026-11-13.

## Why

Vanilla's refractory bricks need crushed bauxite at every tier (`recipes/grid/rawrefractorybrick.json`: fire clay ×2,
crushed quartz, crushed bauxite; tier 2 adds crushed olivine, tier 3 crushed ilmenite). Since ground rules 2 (0076) and
under Earth's ore maps, bauxite lies only where it is real: tropical laterite and warm karst terra rossa. Most of the map
therefore cannot build a cementation furnace or coke oven. The suite rule is no lockout by spawn: real sources and real
substitutes, not place-anywhere fallbacks. Historically, kilns and furnaces were lined with fireclay brick (kaolinitic
clay tempered with grog, crushed fired brick) long before bauxite was known, and kyanite, sillimanite and andalusite
fire to mullite, the mineral of high-alumina brick, in the metamorphic belts where they are common.

The author wants no new blocks for some time after the 2.0 release, and rules are frozen per world (0002), so the new
ore blocks ship now together with their deposits.

## Crafting rules 1 (`EarthParams.CraftingRules`, field 203)

- A crafting rules version is saved per world. New worlds get 1 where Earth decides where bauxite lies: ground rules 2
  and later, or Earth's ore maps (`EarthCrafting.RulesFor`). Vanilla rock with vanilla ores, regional rock with vanilla
  ores, every world made before and non-Earth worlds get 0 and keep vanilla's recipes only.
- The recipes are an ordinary asset, `earth:recipes/grid/refractory.json`. In a world whose rules are 0, `EarthCrafting`
  takes them out of the server's grid recipes at WorldReady, right after Earth reads the world's params. The server
  builds the asset packet (recipes included) only after WorldReady, so clients and their handbook never see them there
  (checked in the decompiled 1.22.7 `ServerMain`: `StartBuildServerAssetsPacket` runs after the WorldReady phase). The
  crafting grid finds recipes through the world's index by ingredient (`FastSearchRecipesByIngredient`), built from the
  same recipe objects when they load, so the removed recipes are also disabled and taken out of that index; without
  that the server still crafted them in a vanilla-rock world although its clients showed nothing (found on the bench, 14
  November).
- They only add options. Vanilla's recipes, bauxite's included, stay in every world. The outputs are vanilla's
  `refractorybrick-raw-tierN`, which vanilla's multiblocks check.

| Recipe | Ingredients | Output |
| --- | --- | --- |
| Grog | hammer + any fired brick (`burnedbrick-*`) | 2 `earth:grog` |
| Crushing | hammer + kyanite, sillimanite or andalusite | 1 `earth:crushed-{mineral}` |
| Fire clay | 8 blue or red clay + 1 grog | 8 fire clay |
| Fire clay | 8 blue or red clay + 1 crushed quartz | 8 fire clay |
| Fireclay brick | 3 fire clay + crushed quartz + grog | 1 `refractorybrick-raw-tier1` |
| Tiers 1-3 | vanilla's recipes with crushed kyanite, sillimanite or andalusite in bauxite's place | as vanilla |
| Tiers 1-3 | vanilla's recipes with vanilla's corundum (`ore-corundum`) in bauxite's place | as vanilla |

- The plain fireclay brick takes a third more fire clay than vanilla's and grog costs a firing, so bauxite and the
  aluminosilicates stay the better choice where they occur. Tiers 2 and 3 still need bauxite, an aluminosilicate or
  corundum: the later tiers may stay regional (EA-0069's reach plan).
- Nothing for a tier needs steel. Crushed quartz needs vanilla's pulverizer and a bronze pickaxe as before; the new ores
  need a copper pickaxe (mining tier 2); grog and the crushed minerals need only a hammer. The new ore items also crush in
  the pulverizer.
- Vanilla's own fire clay (clay + powdered flint) and Earth's kaolin (`game:rawclay-fire-none` in saprolite, laterite and
  under coal, 0060) are unchanged.

## Volcanic-ash mortar (no switch)

`earth:recipes/barrel/mortar-tephra.json`: 1 L slaked lime and 1 `earth:tephra` give 6 mortar instead of vanilla's 4,
the pozzolana of Roman mortar and concrete. Tephra exists only in ground-column worlds, so the recipe needs no switch.

## Ore layers 4: kyanite, sillimanite and andalusite

- New blocks, following Earth's ungraded ores (`blocktypes/stone/ore-ungraded.json`): `earth:ore-kyanite-{schist,
  gneiss,quartzite}`, `earth:ore-sillimanite-{gneiss,schist}`, `earth:ore-andalusite-{slate,phyllite,schist}` (8), each
  dropping `earth:ore-{mineral}` (1.25). Loose surface markers `earth:looseores-{mineral}-{rock}-{free,snow}` (16) in
  vanilla's loose-ore form. New items: the three ores, `earth:crushed-{kyanite,sillimanite,andalusite}`, `earth:grog`.
- Deposits in `earth:worldgen/deposits/earth-aluminosilicates.json` (`earth-kyanite`, `earth-sillimanite`,
  `earth-andalusite`): discs of about 9 blocks radius and 3 thick, 4-36 blocks below the surface, 0.6, 0.4 and 0.5
  attempts per chunk, a loose marker over 8 % of a marked deposit's columns. Their host rocks carry the geography: regional
  geology lays schist, gneiss, quartzite, slate and phyllite only in mapped metamorphic belts (Scottish Highlands,
  Lepontine Alps, Scandinavian Caledonides, Appalachians, the shields).
- They are layer deposits: only worlds with ore layers 4 keep them (`EarthOreLayers.PruneDeposits`, by the world's
  ore-layer version), and they start only where ground columns own the column. `ore-layers-v4.json` is v3 with one rule,
  "aluminosilicates", in bed and basement rock. Worlds with ore layers 1-3 generate exactly as before.
- Under the ore maps they keep their native attempts (no heat map for them).
- Textures: the block overlays and ore items are Earth's own procedural drawings, like its other ores
  (`tools/make_ore_overlays.py`, `art/ores-gems/ores/gen_textures.py`). The crushed items and grog are drawn the way 0058
  draws crafted stone: the client takes vanilla's crushed template nearest in colour and moves it into the palette of
  Earth's own drawing (`EarthStoneTextures`, `tools/make_crushed_textures.py`), which stays as the fallback. They are
  recognised as Earth's by their `earth:` domain, not by the crafted stone's origin test, which never matches from a
  release zip (code review CR-O10); so the recolour also runs for zip installs (checked on the bench). Nothing comes
  from Advanced Geology, which has no licence.

## Not done

- Magnesite (a basic brick tier; Advanced Geology's codes, guarded) waits.
- No reach measurement yet (EA-0069 step 3): the nearest tier-1 source from a few thousand land spawns.
- No district rules for the aluminosilicates beyond their host rocks.

## Checks

- `OreLayersTests`: v1 to v4 load, v4 is v3 plus the aluminosilicate rule, each deposit's hosts, ore blocks and loose
  markers exist (8 ore blocks, 16 markers).
- `EarthWorldOptionsTests`: crafting rules 1 with ground columns or the ore maps, 0 with vanilla rocks and ores or
  regional rocks with vanilla ores.
- In game on the AgentBridge bench (14 November), through the player's own crafting grid (`tools/craftprobe` lays the
  stacks and takes the output through the grid's output slot, as a click does): every recipe above crafted in a
  ground-rules-2 world; the fireclay brick fired to `refractorybrick-fired-tier1` in a pit kiln built and lit by hand; a
  barrel of 1 L slaked lime and 1 tephra held 6 mortar. In a vanilla-rock world none of Earth's grids gave an output,
  vanilla's bauxite brick and flint fire clay did, and the tephra mortar did (no switch, by design).
- The server self-test logs the crafting rules, Earth's grid recipes the crafting grid's index still finds, Earth's grid recipes in the world and how many resolved, the tephra
  mortar, the aluminosilicate deposit generators, and a census of the new ores and markers per place.
