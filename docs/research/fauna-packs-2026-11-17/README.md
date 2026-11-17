# Fauna and flora packs reviewed on 17 November 2026 (EA-0156)

The provider releases Earth's fauna catalogue (`assets/earth/config/fauna-species.json`) and plant-pack ranges
(`assets/earth/config/regionalplants.json`) are reviewed against since Earth 2.1.4. It replaces
[ecology-2026-09-28](../ecology-2026-09-28/README.md) as the input of `tools/build-fauna-catalog.py` and
`tests/RegionalFaunaTests`; that folder stays as the first review's record.

A player's mod set (`work\crashcheck`, EA-0154) logged 34 fauna and 8 plant "newer than reviewed" warnings with Earth
2.1.3, and six entities kept out of natural spawning: the steppe bison (`bovinae:…-bospriscus`) and the domestic dog
(`caninae:…-canis-familiaris`). The player also ran Moschidae, which Earth did not list.

| File | What |
| --- | --- |
| `release-inventory.json` | Each pack's newest Mod DB release for game 1.22 (`https://mods.vintagestory.at/api/mod/<modid>`, 17 November 2026): version, game, date, archive URL, changelog |
| `archive-audit.json` | Manifest, entity and biomes files, DLL names and SHA-256 of each release zip |
| `entity-spawn-audit.json` | Code, variant groups and spawn conditions of every entity JSON (161 files), as in the 2026-09-28 format |
| `changes.json` | Per pack against the 2026-09-28 review: entity files, codes and variant states, worldgen block-patch codes and tree generators added or removed |

All are written by `tools/inventory-fauna-packs.py` (zips read as data; nothing run or installed). Its `audit()`
reproduces the 2026-09-28 `entity-spawn-audit.json` and `archive-audit.json` exactly from that review's zips (151 entity
files, no difference). The zips are not kept in Git: `C:\VS mods\work\fauna-packs\current` (28 from the player's two
reports, Floral Zones Neozeylandic 1.0.27 downloaded from the Mod DB; each matched the Mod DB's file name and its
modinfo id and version); the reviewed ones are in
`C:\VS mods\data\earth-research\ecology-2026-09-28`.

## Versions

| Pack | Reviewed before | Now | Released |
| --- | --- | --- | --- |
| Bovinae | 0.3.6 | 1.0.3 | 2026-11-12 |
| Caninae | 1.1.9 | 1.1.10 | 2026-10-01 |
| Capreolinae | 2.0.18 | 2.0.21 | 2026-11-06 |
| Casuariidae Plus | 1.1.13 | 1.1.14 | 2026-10-14 |
| Cervinae | 0.1.14 | 0.1.19 | 2026-11-09 |
| Chelonioidea | 1.0.8 | 1.0.10 | 2026-10-14 |
| Dinornithiformes Plus | 1.0.25 | 1.0.27 | 2026-11-06 |
| Elephantidae | 1.0.20 | 1.0.23 | 2026-11-06 |
| Felinae | 0.2.14 | 0.2.16 | 2026-10-14 |
| Iniidae Plus | 0.1.7 | 0.1.8 | 2026-10-08 |
| Machairodontinae | 1.1.13 | 1.1.13 | 2026-09-23 |
| Manidae | 1.0.23 | 1.0.24 | 2026-10-14 |
| Meiolaniidae | 0.1.11 | 0.1.13 | 2026-10-14 |
| **Moschidae** | not supported | 1.0.1 | 2026-11-06 |
| Pantherinae | 1.2.18 | 1.2.18 | 2026-09-11 |
| Rhinocerotidae | 1.0.28 | 1.0.29 | 2026-10-14 |
| Sirenia | 1.0.32 | 1.0.33 | 2026-10-01 |
| Spheniscidae | 1.0.18 | 1.0.20 | 2026-11-06 |
| Thylacinidae Plus | 0.1.7 | 0.1.9 | 2026-11-06 |
| Viverridae Plus | 1.0.10 | 1.0.11 | 2026-10-14 |
| Vombatidae Plus | 0.4.6 | 0.4.8 | 2026-10-13 |
| Floral Zones: Cape | 1.0.37 | 1.0.39 | 2026-11-09 |
| Floral Zones: Caribbean | 1.0.29 | 1.0.31 | 2026-11-09 |
| Floral Zones: Central Australian | 1.0.27 | 1.0.29 | 2026-11-09 |
| Floral Zones: Circumboreal | 1.0.2 | 1.0.5 | 2026-11-09 |
| Floral Zones: Cosmopolitan | 1.0.13 | 1.0.15 | 2026-11-09 |
| Floral Zones: East Asiatic | 1.0.24 | 1.0.26 | 2026-11-09 |
| Floral Zones: Mediterranean | 1.0.24 | 1.0.26 | 2026-11-09 |
| Floral Zones: Neozeylandic | 1.0.24 | 1.0.27 | 2026-11-09 |

## What changed in the packs

- **Bovinae 1.0.3:** a new species, the steppe bison (`bospriscus`, adults and calves); tame aurochs entities.
- **Cervinae 0.1.19:** its one entity file split into adult, baby, semi-tame and tame files, and a giant-deer carcass
  (`cervidae-cervini-carcass`).
- **Vombatidae 0.4.8:** carcasses for its diprotodontids and *Thylacoleo* (`diprotodontidae-carcass`,
  `thylacoleonidae-carcass`).
- **Chelonioidea, Thylacinidae, Iniidae, Meiolaniidae, Felinae:** entity files moved to new folders; the same codes.
  Felinae 0.2.16 loaded without the 0.2.14 lynx sound error in the 31 October check
  ([installed-ecology-audit](../../installed-ecology-audit-2026-10-31.md)); Earth's repair stays, for 0.2.14 only.
- **Caninae 1.1.10:** no entity change. Its domestic dog (pups and semi-tame adults) was already in 1.1.9 and unmapped;
  its natural spawn chance is 0 (puppies come from traders).
- **Moschidae 1.0.1:** five musk deer: *Moschus anhuiensis*, *M. berezovskii*, *M. cupreus*, *M. fuscus*,
  *M. moschiferus* (its language file also names *M. chrysogaster* and *M. leucogaster*, which have no entities yet).
- **Floral Zones (all eight):** no worldgen file changed: the same 275 block patches with the same codes, and no tree
  generator added or removed. Their changes are block types (berry bushes, logs, saplings), recipes and trader patches.
- The other packs: no entity code added or removed.

## Mapping (decision 0054 for animals, 0049 for plants)

- **Steppe bison** (*Bison priscus*): extinct, so it spawns only with *Include extinct animals*. Range as the other
  mammoth-steppe animals: a Eurasian box (10°W–180°, 36–76°N) and Alaska with Yukon (170–128°W, 55–72°N), with the
  usual island barriers; the North American bison that descended from it further south are other species. Period: Late
  Pleistocene; gone from western Europe c. 12–10 ka, from Siberia in the early to mid Holocene, latest secure record
  c. 5.5 ka (Yukon).
- **Domestic dog** (*Canis familiaris*): gone feral where people kept dogs around 1318 (the author's rule in 0054), the
  same range in every profile: the 1318 chicken regions without the small oceanic islands where dogs are not attested
  or were gone at contact, plus Siberia, North America with the Arctic and Greenland, Mexico, Central America, the
  Caribbean, the Andes, the Pampas, Paraguay, southern Brazil and New Zealand (279 regions). Left out: Australia and
  Tasmania (the dingo is its own rule), Amazonia and the Guianas, northern and central Brazil, Patagonia and southern
  Chile, the Aleutians, the Andamans, Easter Island, Samoa, Tonga, New Caledonia and islands uninhabited in 1318. The
  rule enters through `fauna-taxa.json`'s explicit `tokens`, because the pack's adult dog file no longer lists the dog.
  Since the pack gives it no natural spawn chance, players see no wild dogs yet; the generic canid carcass now also
  appears where only the dog lives (Hawaii, New Zealand, the Pacific and Caribbean islands, Madagascar).
- **Musk deer**: PHYLACINE 1.2.1 rasters, as every living mammal. For all five the present-natural and current ranges
  are the same cells, so the 1318 range is the PHYLACINE range (reviewed in `tools/fauna-1318.json`, no losses).
- **Floral Zones:** nothing new to map; the reviewed versions and hashes in `tools/plant-taxa.json` were raised and
  `regionalplants.json` re-baked with `bake_plant_regions.py --ranges-from` (the ranges of the reviewed WCVP, so every
  plant keeps its range; only the provider lines and the mapping hash changed).
- **Still held** (unchanged): the 11 taxa of `regional-fauna-2026-09-29/held-species.csv`, among them domestic cattle,
  water buffalo and reindeer. None of these three has natural spawning in its pack, so holding them changes nothing in
  play; mapping them as gone feral (like the dog) is a follow-up, not done here.

## Check

Every registered variant of the 21 packs (allowed and skipped variants applied), resolved as `EarthFaunaSystem` does:
with 2.1.3's catalogue, exactly the six bench entities are unmapped with spawn conditions; with this one, none: 1,382
codes map to a rule, 50 to a held rule, 18 generic carcasses to a group, none ambiguous.
