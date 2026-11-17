# 0054: Animals live where they lived around 1318

Date: 5 November 2026. Status: **Agreed**; **built** 5 November 2026; in Earth 2.0.0; the packs of 17 November 2026
(Moschidae, the steppe bison, the domestic dog) in Earth 2.1.4. Offline tests pass; not checked in game.

## Report

The author met a leopard in the Peloponnese (37.41° N, 22.09° E) in a world placed as around 1318. Leopards had been
gone from Greece since prehistory. Regional fauna ([implementation](../regional-fauna-implementation.md)) gates the
fauna mods' animals, and since [0031](0031-mountain-height-playtest.md) vanilla's land animals, by PHYLACINE 1.2.1
*present-natural* ranges: where a species would live with no human impact at all. Those ranges are counterfactual by
design. They include places people had emptied long before 1318 (lions in Greece, moose in Britain), and places the
species never reached after the Ice Age: spotted hyenas across Europe, India and China (PHYLACINE counts Pleistocene
cave hyenas as *Crocuta crocuta*), muskox across Siberia and Scandinavia, dholes in North America, tigers in Japan.
[0049](0049-flora-ranges-as-of-1318.md) and [0047](0047-placement-catalogue-and-audit.md) had already extended the
1318 rule to animals; only extinct animals still alive in 1318 had been implemented.

## Decision

The author, 5 November 2026, asked for a 1318 animal profile ("Yes, build it") and set the rule for domestic animals:
**"I assume domesticated animals become wild when humans disappeared."**

- **Wild animals:** the 1318 range is the present-natural range minus the regions where the species no longer lived
  around 1318 (roughly 1250–1400): lost to people before then, or never reached in the Holocene. Losses after 1318 stay
  (most wolf, bear and lynx losses in Europe, all colonial-era losses in the Americas, Africa and Australia).
- **Today's native range is never removed.** Where a species lives natively today (PHYLACINE *current*), it lived in
  1318. This also repairs coarse boxes at range edges.
- **Domestic animals went feral where people kept them in 1318:** chickens across the Old World, Madagascar and the
  settled Pacific (not New Zealand, Australia or the Americas); sheep (vanilla's mouflon) wherever sheep were herded;
  the four domestic goat breeds each around a home area (Valais: Europe; Angora: Anatolia, the Caucasus, Iran, Central
  and East Asia; Nubian: Africa and Arabia; Sirohi: South and Southeast Asia); pigs also on the Mediterranean and
  Atlantic islands, island Southeast Asia, New Guinea, Melanesia and Polynesia; fallow deer where they lived wild or in
  parks (Anatolia, the Mediterranean, France, Britain, Ireland).
- **Stop-gap: chickens everywhere** (author, 5 November 2026: "ship updated version with knob in UI tab for chicken
  everywhere (on by default) till we solve the issue"). Arrows need feathers, and no animal gives the Americas or
  Australia a feather bird yet. The Earth tab's "Chickens everywhere" (`earthChickensEverywhere`, default on) lets wild
  chickens spawn wherever the climate suits them (every region but Antarctica, `everywhereExcludedRegions`, since
  2026-11-07, when the 60°S guard that kept them out was replaced by ranges); off keeps them to their 1318 range above.
  It applies with plants and animals as around 1318 only: a world with vanilla nature has no animal ranges at all
  (`SpawnRangeGuards`; README: "placement by climate alone"), so its chickens spawn by climate either way. (Until
  2.0.0-rc.2 this line and the option's description said "or the red junglefowl's with vanilla nature", which no build
  did; the description was corrected on 9 November 2026, the behaviour kept so no saved world changes.) Saved per
  world as `EarthParams.ChickensByRange` (proto 99; missing = everywhere, so older worlds get chickens everywhere too).
  To be revisited once a feather bird covers every region.
- **The game's tur ("goat-turdag") is the East Caucasian tur**, *Capra cylindricornis*, of the Caucasus. It was mapped
  to the wild goat's range in Anatolia and Iran.

Implementation choices the author can override: the goat breeds' home areas, and fallow deer kept out of Germany, the
Low Countries and Scandinavia (their parks date from the 16th–17th centuries).

## Implementation

- **Data:** `tools/fauna-1318.json` holds, per binomial, the entity keys, the regions (TDWG level 3) and boxes lost by
  1318, evidence, sources and confidence (79 taxa reviewed, 39 with losses), plus the domestic lists. The research and
  its reconciliation are in [fauna-1318-2026-11-05](../research/fauna-1318-2026-11-05/README.md).
- **Fauna mods** (`RegionalFaunaCatalog`, rules version 3): a third profile, `"1318"`, beside `"natural"` and
  `"current"`. Rules carry `lost1318Regions`, `lost1318Envelopes` and `evidence1318` (31 rules with losses); only the
  1318 profile reads them, and never inside the rule's current raster. Rule evaluation still allocates nothing.
- **Vanilla animals** (`VanillaFaunaRanges`, version 2): entries carry `lost1318Regions`/`lost1318Envelopes` with their
  current raster, `range1318Regions` (replaces the natural range: chickens, goat breeds, fallow deer) or
  `added1318Regions` (extends it: sheep, pigs). The 1318 ranges apply when a region index is given.
- **Which worlds:** worlds placed as around 1318 (`PlacementRules` ≥ 1) use the 1318 profile for future natural spawns,
  including worlds created before it existed (they had saved the old default "natural"); `FaunaRangeProfile`
  `"current"` in earth.json still wins. New 1318 worlds save `"1318"`. Worlds with vanilla nature, and older worlds
  without placement rules, are unchanged. Animals already in a world are never touched; the fauna rules version
  2 → 3 is logged like earlier rule updates ([0002](0002-save-compatibility.md)).

## Verification

`tests/RegionalFaunaTests` (971 assertions, real terrain included) adds: the Peloponnese leopard is gone and Kenya's
stays; no lions in Greece, tigers in Japan, dholes in North America or Tasmanian devils on the mainland; wolves stay in
Germany and on the Great Plains; spotted hyenas only in Africa; muskox in Arctic America, not Norway; feral chickens in
Poland, Kenya and Hawaii, not Mexico, Australia or New Zealand; Valais goats in the Alps, not Iran; pigs on Hawaii;
the tur in Dagestan; losses never apply outside the 1318 profile; every enabled taxon keeps an eligible column on real
terrain in the 1318 profile (267/267). `tests/EarthWorldOptionsTests` checks that 1318 worlds save the profile and
that earth.json's "current" wins. Chickens everywhere: on by default and in older saves, off is saved, and with it on
chickens spawn in Mexico, Australia and Canada while every other animal keeps its 1318 range (972 fauna assertions).

Not done: an in-game census in a 1318 world; the low-confidence calls listed in the research README.

## Open

- **Feather birds in the Americas.** No installed mod or vanilla animal gives North, Central or most of South America
  a feather bird (only South American penguins), and arrows need feathers. Until then "Chickens everywhere" (on by
  default) covers it. More Animals (wild turkey, pheasants,
  capercaillie; 1.22.7) could be mapped as a provider: turkeys in the Americas, pheasants and capercaillie in Eurasia.
  Not decided.
- Traders and loot, flowers and mushrooms, and adoption in older worlds remain open as in 0047.

## History

- 5 November 2026: the author reports the Peloponnese leopard, sets the feral rule and asks for the profile;
  implemented the same day. The author asks for the "Chickens everywhere" option (on by default) as a stop-gap.
- 5 November 2026, late evening: the author chose to ship it with the tall release ("With the tall release").
  Brought into `claude/tall-glo90`'s working tree (a file-by-file three-way merge of `a4e2382` onto the tall line, not
  committed) as test build 1.0.3-tall.11; "Chickens everywhere" is the Earth tab's "Nature · Chickens everywhere";
  `ChickensByRange` keeps proto 99. `tests/RegionalFaunaTests` (972), `tests/PlacementTests` (413) and the option tests
  pass; a new 1:100 world on the AgentBridge bench loaded without errors. Animal spawns not yet looked at in game.
- 5 November 2026, 22:47:48 Europe/Riga: tall.11 installed in the author's game at their request (0053 History).
- 17 November 2026 (EA-0156, the author: "can we update Fauna pack and ranges for the release?"): the catalogue
  reviewed against the packs' newest 1.22 releases ([fauna-packs-2026-11-17](../research/fauna-packs-2026-11-17/README.md)).
  Moschidae 1.0.1 is a supported provider (five musk deer on their PHYLACINE ranges, whose present-natural and current
  ranges are the same, so no 1318 losses). Bovinae 1.0.3's steppe bison is extinct, so it needs *Include extinct
  animals*. Caninae's domestic dog follows this record's domestic rule: it went feral where people kept dogs around
  1318 (a region list: the Old World and settled Pacific of the chicken study without islands where dogs are not
  attested, plus Siberia, most of the Americas and New Zealand; not Australia, where the dingo is its own rule). Its
  pack gives the dog no natural spawn chance, so no wild dogs appear yet; the generic canid carcass now also appears
  where only dogs lived. FotSA's domestic cattle, water buffalo and reindeer stay held (no natural spawning in their
  packs either). The fauna rules version stays 4: new mappings for new entities, no rule changes; saved worlds load
  them for future natural spawns. 285 taxa, 274 enabled; Earth 2.1.4.
