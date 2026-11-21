# 0102: Galena and its silver in crystalline rock

Status: **in Earth 2.2.0** (21 November 2026), built on `claude/galena-crystalline`, merged into `main` (19 November 2026); new Earth worlds
with regional rock (and ground columns, which hand off to it) and vanilla ores, as part of geology rules 3
([0101](0101-geology-rules-3.md)). Decided by: the author, 19 November 2026 ("Yes, add them":
new ore blocks for galena in crystalline rock, an exception to the pause on new blocks in
[0077](0077-refractory-bricks-without-bauxite.md)); how it is built by Claude as maintainer (D72). Not yet looked at in
a graphical client.

## The problem (rocks audit, 18 November 2026; EA-0174)

Vanilla's galena deposit names only sedimentary hosts (claystone, sandstone, shale, chalk, limestone, chert,
conglomerate), and its silver nests only in that galena. With vanilla ores on Earth's regional rock, galena and silver
therefore had no host at any depth on about 41 % of regional land, the classic lead and silver districts among them:
Freiberg, Kongsberg and Broken Hill in gneiss, the Harz and Cornwall's killas in slate, Linares in granite. Vanilla has no
galena block for those rocks, and neither had Earth (its cerussite takes limestone, dolostone and sandstone).

## Decision

1. **Blocks and items for every world**: granite, slate, phyllite, gneiss and schist get galena's ore blocks, its silver
   (`galena_nativesilver`), the loose-ore indicators and the ore and crystallized-ore items, copied from shale's variants
   (`patches/galena-crystalline-ores.json`, written by `tools/make_rock_ore_patches.py`; 94 variants). They reuse
   vanilla's textures (the ore overlay on the rock's own texture). Galena takes the grades vanilla gives sphalerite, its
   companion ore, in the same rock: granite poor and medium; slate, phyllite, gneiss and schist all four (gneiss and schist
   as their stand-ins slate and phyllite). The silver keeps vanilla's two grades. Blocks exist in every world so block
   lists do not depend on a world's options.
2. **Hosts only where a world's rules say so**: the five rocks are added to vanilla's own galena deposit while the game
   runs (`CrystallineGalena`), with the loose-ore indicator and the silver child's blocks, exactly as vanilla's
   `DiscDepositGenerator.Init` would have resolved them had `galena.json` named the rocks. Only in Earth worlds with regional
   rock, vanilla ores and geology rules 3 (`GeologyFacts.CrystallineGalena`), at WorldReady and after every deposit
   reload. Vanilla-rock worlds, Earth's own ore maps (which choose their hosts themselves; step 4), every world made before
   and non-Earth worlds keep vanilla's hosts.
3. **Vanilla's rules per attempt**: the same attempts, ore map, radius and depths. Attempts that landed in these rocks
   and placed nothing now place galena, so places with these rocks within reach have more lead than before (lead now
   behaves as vanilla's zinc, whose sphalerite already takes granite).
4. **The prospecting pick** reads the new hosts (its density reading uses the world's deposit), and its own simulated
   copy of the deposits gets them too, so its silver reading counts the galena the silver nests in.

## Granite as a host

Granite is a real host of galena, though a less common one than sedimentary and metamorphic rock: vein deposits in and
around granite bodies, as at Linares-La Carolina (lead-silver veins in the Linares granite and its slates) and Butte
(veins in the Butte Granite). In nature those veins lie mostly near a granite's roof and margins; vanilla's deposits
cannot follow that, so in game galena may form anywhere in granite, and where granite basement lies within reach under
sediments a place has more lead than before (1:100 Riga 330 → 550). The author kept granite on that ground (19 November
2026: "if granite is irl host then yes"). How much lead such places carry is a question for the ore maps (step 4).

## Considered and left out

- **Changing `galena.json` by a patch** (as Earth's own rocks take their stand-ins' hosts): it would put galena in granite,
  slate and phyllite in every world with Earth installed, vanilla-rock and non-Earth worlds and saved worlds included.
- **A separate Earth galena deposit for crystalline rock**: its own attempts and ore map would add lead on top of
  vanilla's, and differ from vanilla's galena where both rocks meet.
- **Malachite on the Copperbelt** (also EA-0174): not in the author's go-ahead; a later step.
- **The handbook**: vanilla writes "found in" from the hosts it resolved itself, so its galena page does not name these
  rocks.

## Consequences

Census on dedicated servers, vanilla ores, `main` against the branch
([galena-crystalline-2026-11-19](../research/galena-crystalline-2026-11-19/)): with regional rock, galena where there was
none (1:10 Kongsberg 0 → 31, Broken Hill 0 → 56, Sala 0 → 22; 1:100 Freiberg 0 → 91, Sala 0 → 127), more where
sediments lie over crystalline basement (1:100 Riga 330 → 550, Coeur d'Alene 109 → 351), over the 11 places 4 → 152 at
1:10 and 472 → 1,208 at 1:100; still none where vanilla's ore map is low. Silver in galena stays as rare as vanilla's.
Vanilla-rock worlds unchanged at every place. Worlds made before keep vanilla's hosts (their geology rules are 1 or 2);
the blocks are in every world's block list from the next release.

## Tests

`PatchVanillaTests`: the patch's rocks are `CrystallineGalena.Rocks`, each with its galena, silver, loose-ore and item
variants and sphalerite's grades; vanilla's `galena.json` names its blocks as `CrystallineGalena` builds them, none of the
five rocks, and a silver child of two grades; the prospecting pick's deposits can be read. `TerrainRulesTests`: the fact
`CrystallineGalena` from 3.
