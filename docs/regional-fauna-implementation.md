# Regional fauna, rules version 3

Earth can restrict the *natural* spawning of animals from supported fauna mods (Fauna of the Stone Age and related packs) to each species' range on the real Earth, and it gives those animals' climate checks the local ground climate. The content mods keep their animals, artwork, AI, drops, breeding and population settings. Vanilla animals and unrelated mods are not geographically controlled.

The catalogue maps **285 taxa from 21 optional providers: 274 enabled (214 living, 60 extinct) and 11 held**. It is regional coverage for the animals these packs provide, not a complete fauna of Earth. The [held list](research/regional-fauna-2026-09-29/held-species.csv) and the [coverage audit](research/regional-fauna-2026-09-29/coverage.csv) show what is missing. The provider versions it was reviewed against, and what changed in them, are in [fauna-packs-2026-11-17](research/fauna-packs-2026-11-17/README.md) (EA-0156; the first review was [ecology-2026-09-28](research/ecology-2026-09-28/README.md)).

## When it is active (and when it is not)

The integration fails safe. In every case below, natural spawning stays exactly as the game and the installed mods define it, and the server keeps running:

| Situation | Behaviour |
| --- | --- |
| Not an Earth world, or `RegionalFauna` off in the save | Nothing is loaded. |
| No supported provider installed | Logged once. No catalogue is built and nothing is patched. |
| Saved fauna rules version differs from the current one | Warning; the current rules apply to future natural spawns only. Existing animals are never touched. |
| A game hook is missing or has a different signature | Warning; regional fauna is off for the session. Every hook is checked before anything is patched. |
| Shipped data missing, corrupt or from another Earth version | Error; regional fauna is off for the session. |
| A single bad rule (unknown region, bad raster, no range) | Warning; that taxon is held, the rest work. |
| An entity code matches two taxa | Warning; treated as unknown (no natural spawning) rather than guessed. |
| A provider version differs from the reviewed one | Warning; unmapped new species of a mapped provider fail closed and are logged. |

`IncludeExtinctAnimals` (default `true`) admits the extinct taxa; changing it affects only future natural spawns. `/earth fauna` reports the state, including why the integration is inactive.

## What the ranges mean

**Living mammals** use [PHYLACINE 1.2.1](https://github.com/MegaPast2Future/PHYLACINE_1.2/tree/v1.2.1) *present-natural* ranges on its ~96 km Behrmann grid: the estimated **natural range without human impact**. These ranges are counterfactual by design. They include areas the species has lost to people, so leopards are eligible in Scotland and Latvia, tigers in Japan and dholes in Florida. They are not today's distribution and not dated historical maps. `/earth fauna` and the packaged attribution say "natural range without human impact (PHYLACINE present-natural)", not "native".

PHYLACINE also publishes *current* ranges (largely derived from IUCN Red List range maps, redistributed by PHYLACINE under CC0). The catalogue carries a current raster for every one of the 162 raster-based rules (157 before Moschidae's five musk deer, whose two rasters are the same cells; the figures below are of those 157), and the `"current"` profile uses them; rules without a PHYLACINE raster (birds, reptiles, sirenians, extinct taxa, region-only splits) are identical in both profiles. Across those 157 rules the current ranges cover 72.7% of the present-natural cells. 25 taxa keep less than half of their natural range (lion, tiger, leopard, dhole, African wild dog, Asian elephant, Sumatran and Javan rhinos, red wolf, Iberian lynx, Tasmanian devil and others), and the Malabar civet has no current range at all. The coyote is the one taxon whose current range is larger than its natural one (2,113 vs 301 cells), reflecting its human-assisted spread. The profile is selected with `EarthFaunaSystem.RangeProfile` before the world is ready; unknown values fall back to `"natural"` with a warning.

**Taxonomic splits** absent from PHYLACINE use the parent species' raster, restricted to the split taxon's documented regions: African golden wolf (*Canis aureus* raster, African regions only), forest elephant and forest buffalo (West/Central African rainforest belt), *Tragelaphus sylvaticus* (East and southern Africa), eastern wolf (southern Ontario and Quebec), *Mazama rufa* (Atlantic Forest, Cerrado and southern Amazonia) and *Pudella carlae* (Peruvian yungas south-east of the Huancabamba Depression, 1,800–3,300 m; the northern pudu is limited to north of it). *Mazama tienhoveni*, now usually treated as a synonym of the Amazonian brown brocket, uses that species' raster limited to the lower Rio Aripuanã basin where it was described. Split and parent rules overlap where the literature leaves the boundary open.

**Birds, reptiles and sirenians** use TDWG regions or boxes from the cited accounts (New Zealand Birds Online, DOC, the Australian Antarctic Program, NOAA, Australian Museum, Wikipedia species articles). They are coarser than breeding or migration maps. The **dingo** is included for mainland Australia (not Tasmania). It arrived with people about 4,000 years ago (earliest confirmed remains about 3,250 years old) and has lived wild since. No IUCN Red List pages or spatial data are used directly.

### Ranges as around 1318 (rules version 3)

[Decision 0054](decisions/0054-animals-as-around-1318.md): worlds placed as around 1318 use the `"1318"` profile. A living
raster rule's 1318 range is its present-natural range minus `lost1318Regions` (TDWG level 3) and `lost1318Envelopes`
(boxes): where the taxon no longer lived around 1318 because people had removed it, or never lived in the Holocene
(PHYLACINE's counterfactual cells). Losses after 1318 stay in. The exclusions never remove the rule's current raster:
where a species lives natively today, it lived then. 31 rules carry losses (leopard and lion out of Europe, dhole out of
Europe and the Americas, tiger out of Japan, Borneo, Sakhalin and most of Siberia, Tasmanian devil out of mainland
Australia, moose and reindeer out of western and central Europe, elephants and rhinos out of northern China, and
more); each has `evidence1318` with sources. Source: `tools/fauna-1318.json`; research and reconciliation in
[fauna-1318-2026-11-05](research/fauna-1318-2026-11-05/README.md). Worlds placed as around 1318 that saved the old
default `"natural"` switch to `"1318"`; earth.json's `"current"` still wins.

Vanilla land animals (`fauna-vanilla.json` version 2) follow the same rule, and domestic animals went feral where
people kept them in 1318 (`range1318Regions` replaces, `added1318Regions` extends the natural range).

Caninae's **domestic dog** (EA-0156) is a region rule of where people kept dogs around 1318: the 1318 chicken regions
without the small oceanic islands where dogs are not attested, plus Siberia, most of the Americas and New Zealand, not
Australia (the dingo's) or Tasmania. It has no raster, so it is the same in every profile. Its pack gives it no natural
spawn chance (puppies come from traders), so it only matters if a later version lets dogs spawn; meanwhile it puts the
generic canid carcass where only dogs live too. Its rule comes from an explicit `tokens` entry in `tools/fauna-taxa.json`,
because the pack lists the dog only in its pup and semi-tame entities. Domestic cattle, water buffalo and reindeer are
still held (their packs give them no natural spawning either).

### Extinct taxa

Policy: an extinct taxon's range is **its last natural range before human-driven range loss or extinction**, from late Pleistocene to Holocene records. Post-contact relicts are not used as the whole range. Every enabled extinct rule states its `period` in `tools/fauna-taxa.json` (or `tools/fauna-birds.json`), and the tests enforce this. PHYLACINE's counterfactual rasters are never used for extinct taxa.

| Taxa | Period represented |
| --- | --- |
| Woolly mammoth, woolly rhino, cave lion, Columbian mammoth, dire wolf, sabre-tooths, American lion, stag-moose, *Genyornis*, *Thylacoleo*, *Phascolonus*, *Zygomaturus*, giant devil, Hackett's wombat, *Antifer* | Late Pleistocene (about 50–12 ka), coarse continental boxes or regions |
| Pygmy mammoth, Cyprus dwarf elephant, Cyprus genet, *Cynotherium*, Cebu and Sulawesi dwarf buffalo | Late Pleistocene to early Holocene (Cebu: age uncertain), single islands |
| Giant deer | Late Pleistocene to early Holocene, Europe to Siberia |
| Steppe bison | Late Pleistocene mammoth steppe, western Europe to Alaska and Yukon (gone from Siberia in the early to mid Holocene; latest secure record c. 5.5 ka, Yukon) |
| European aurochs | Late Pleistocene to Holocene Europe and western Asia (Ireland excluded: no postglacial record; last individual 1627) |
| Steller's sea cow | Late Pleistocene to Holocene North Pacific rim, Japan to California. The 1741 Commander Islands herd was a relict and is no longer used as the whole range. |
| *Dusicyon avus*, *Bubalus mephistopheles* | Late Pleistocene to Holocene, until about 400 BP (southern Patagonia) and about 1200 BC (China) |
| Moa and other New Zealand, Chatham and Lord Howe birds; Tasmanian emu; Falkland wolf | Holocene range before human arrival or European contact |
| Baiji | 20th-century middle/lower Yangtze (functionally extinct since 2006) |

Continental boxes are an interpretation of the fossil record, not a claim that every enclosed locality has a find. For extinct land taxa defined only by a continental box (≥ 25 square degrees), a fixed list of island regions the animal never reached is excluded, so woolly rhinos are not placed in Japan. Island-scale boxes are drawn around their island and get no such exclusions. In rules version 1 that list was also applied to the Lord Howe swamphen's own island.

## Spawn gate

`EarthFaunaSystem` is a server ModSystem with its own Harmony id. At WorldReady it resolves every loaded entity code of an installed provider to a taxon (an exact token sequence, so *Vulpes vulpes* needs two adjacent tokens). It then patches the VS 1.22 natural-spawn paths:

| Path | Change |
| --- | --- |
| `GenCreatures.CanSpawnAtPosition` | Range and habitat check for each worldgen candidate, including herd companions. |
| `GenCreatures.CanSpawnAtConditions` | Local Earth ground temperature and rain replace the engine's reference-height values. The game's other checks (climate bounds, vegetation, altitude, light, collision) still apply. |
| `ServerSystemEntitySpawner.CanSpawnAt_offthread` | Range check at the runtime candidate and at the returned position; sets the thread-local climate context. |
| `ServerSystemEntitySpawner.GetSuitableClimateTemperatureRainfall` | Inside a mapped spawn only: ground climate, with live seasonal callbacks kept for non-worldgen modes. |

The geographic veto stays in these Harmony prefixes, not in `Event.OnTrySpawnEntity`. Worldgen raises that event only after a whole herd has been assembled, so a veto there would cancel spawns instead of letting the game try other positions. The climate adapter needs Harmony either way.

**Generic carcasses.** Some providers have one carcass entity per group (`caninae-canina-carcass`, `-cerdocyonina-`, `pantherinae-neofelis-`, `-panthera-`, `machairodontinae-`, `casuariidae-`). Such a carcass is allowed wherever any enabled species of its group is allowed, under the same extinct toggle. Species-specific carcasses use their species' rule. In rules version 1 these generic carcasses were blocked everywhere.

**Habitat.** Columns are classified from the Earth terrain model as sea, fresh water (rivers, lakes), saline lake or land. "Coast" means a sea/land edge within 8 or 24 blocks. "Amphibious" (manatees) means fresh water, or sea next to land. Marine depth limits are in blocks. Land and coastal sites take the nearest TDWG region when they fall outside every polygon (`BotanicalRegions.FindLand`, up to 5°), so Earth's Antarctic coast is `ANT` although the TDWG polygons miss about half of it. Until rules v4 (7 November 2026, ranges proposal step 1) every site south of 60°S was counted as `ANT` instead; open sea now keeps only the polygons' 0.08° coast tolerance. The gutter at the Cylinder World seam and the polar filler never receive natural spawns. The Snares Islands are below the terrain's resolution (no land columns), so the southern Stewart Island coast, about 100 km away, stands in for the Snares penguin's breeding islands.

**Threads and cost.** Worldgen and runtime spawning run off the main thread. All shared data is built once and published as an immutable snapshot. Per-call state is local or `[ThreadStatic]`, and counters use `Interlocked`. Entities are looked up by their `AssetLocation`, with no string building. Held, extinct-off and unknown taxa are rejected before the site is computed. The site is cached per thread and column, so the runtime post-check and other species at the same column reuse it. In a scratch benchmark on real terrain near a player (50,000 candidates, prefix plus postfix), the gate dropped from 3.7 µs and 498 bytes per candidate (v1) to 1.2 µs and 76 bytes. The remaining bytes are allocated by the shared region lookup (`BotanicalRegions.Find`). An uncached site costs about 2.5 µs, or about 15 µs when the taxon needs the coast check.

## Climate correction

Earth's climate pixels compensate for the game's height adjustment, so sampling at the actual ground recovers the local climate. VS 1.22's creature code instead samples temperature near 1.09 × sea level, and worldgen uses the stored, uncompensated rain byte. On Earth highlands this made native habitat artificially warm and dry. The adapter reads `TerrainModel.ClimateAt` and `RiparianRain` at the candidate's X/Z, respects `ExtendTemperature` and does not widen any species' limits. In the 0.12 measurement, the Ethiopian highland at 9°N 39°E read 34.0 °C at the game's reference height and 15.6 °C at the ground. The adapter is unchanged in rules version 2.

## Provider compatibility

The 21 reviewed provider versions are recorded in `fauna-species.json` (17 November 2026: Moschidae 1.0.1 added, the others raised to their newest 1.22 releases; [fauna-packs-2026-11-17](research/fauna-packs-2026-11-17/README.md)). Rebuild that snapshot with `tools/inventory-fauna-packs.py` when packs change, then the catalogue. Felinae 0.2.14 has one exact-version in-memory repair: its broad `idleSoundRangeByType` keys reach silent lynx variants that have no idle sound, and the game's entity loader then skips most of their attributes. The repair narrows those keys to caracals. It runs only when the server's save is (or is about to become) an Earth world with regional fauna. Other worlds load Felinae exactly as shipped. The Biomes mod's independent region veto can suppress valid Earth animals, and Earth logs a warning when both are installed.

## Verification

`tests/RegionalFaunaTests` (pure .NET 8, compiles `src/Core`) has 1,012 assertions (17 November 2026, with real terrain):

- **Data and inventory.** The data version matches the code, and the shipped data loads with no problems. No audited provider code (all variant cross-products) is ambiguous, every rule maps an audited entity, and the held list is exactly the reviewed 11. Every enabled extinct taxon states its period, no rule cites IUCN pages, and extinct taxa use no PHYLACINE raster.
- **The 17 November packs.** The steppe bison is extinct-only, in Europe, Yakutia, Alaska and Yukon, not the Great Plains or Japan; the domestic dog in Germany, Mexico, the Andes, Greenland, New Zealand and Hawaii, not Australia, Tasmania, Easter Island, Amazonia or Patagonia, and the pack's pups and semi-tame dogs map to it; the five musk deer in their ranges; the new giant-deer, diprotodontid and *Thylacoleo* carcasses have species groups; 21 providers.
- **Regressions.** African golden wolf (Kenya/Morocco, not Eurasia); aurochs (Britain, not Ireland); Antarctic penguins at any Antarctic longitude; Steller's sea cow from Japan to California; dingo, forest elephant, eastern wolf, Cyprus genet, Cebu buffalo and other newly mapped taxa; earlier island, continent, water and extinct-toggle cases.
- **Save safety of the catalogue.** Ambiguous codes resolve to "unknown" without throwing. Bad rules are held with a problem message. A wrong-version file is rejected as a whole. Profiles fall back correctly, and rule evaluation allocates nothing.
- **Range profiles.** Leopards in Scotland and Latvia, tigers in Japan, dholes in Florida and lions in Greece are eligible only in the natural profile; the coyote in Vermont only in the current profile. Split taxa keep their region limits in both.
- **1318 profile.** The Peloponnese leopard, lions in Greece, tigers in Japan, dholes in North America and devils on the Australian mainland are out; Kenya's leopards, German and Great Plains wolves stay. Losses apply only in this profile, an unknown region holds the rule, and every enabled rule keeps a column on real terrain (274/274). Vanilla: spotted hyenas only in Africa, muskox not in Norway, feral chickens in Poland, Kenya and Hawaii but not Mexico, Australia or New Zealand, goat breeds by home area, the tur in Dagestan, pigs on Hawaii; no allocation per check.
- **Real terrain** (the user's layout: Cylinder World 512000 × 204800, height 384, rivers on):
  - Geometry round trips, ring wrapping, the gutter and the polar filler.
  - Habitat classification: open ocean, inland Sahara, coastal land and sea at the Cape, Amazon river water, the Caspian as a saline lake, Antarctica as `ANT`.
  - Antarctic coverage: first land south of 60°S at each degree of longitude. Adélie and emperor penguins are eligible at 360 of 360 sampled coasts (170 of 360 in v1).
  - Island birds within 0.3° of their island: Lord Howe swamphen eligible on 54 columns (7 in v1). The Snares penguin's stand-in coast has 550 columns; the Snares window itself has no terrain land.
  - **Feasibility**: every enabled rule has at least one eligible column on real terrain (in the current profile all but the Malabar civet, whose current range is empty). Samples come from its boxes, its regions' polygons (fine grids for small islands), every occupied raster cell and 31 explicit island points.
  - **Island-box usability**: in every island-scale box, at least half of the habitat-suitable columns are eligible. Run against the v1 data, these checks flag the Snares penguin and the Lord Howe swamphen.

Run with `dotnet run --project tests/RegionalFaunaTests -c Release -- <repo root>`. Add `--no-terrain` for the pure tests only, or `--coverage` to regenerate `coverage.csv` and `native-climate-scan.csv`. Rebuild the data offline with `python tools/build-fauna-catalog.py --range-cache <Present_natural tifs> --current-cache <Current tifs>`; `--fetch` downloads missing pinned files, and the URLs, licence and SHA-256 of every raster used are recorded in `catalogue-summary.json`.

These checks do not simulate forests, collisions, herd completion, population caps or the player-driven runtime loop. Eligibility is not a population. In-game checks remain necessary: `/earth fauna` and the detached `VerifyRuntimeClimateAdapter` probe.

## Known gaps

- **11 held taxa.** Four are living: three domestic forms and the pangolin *Manis mysteria* (described from seized scales; native range unknown). Seven are extinct: single-locality or pre-Late-Pleistocene fossils (*Torontoceros*, *Speothos pacivorus*, *Bretzia*, *Odocoileus lucasi*, *Bubalus brevicornis*, *B. murrensis*) and *Syncerus antiquus*, whose pan-African fossil record gives no defensible range.
- **Provider climate limits.** In the coarse 2° coverage scan, 27 enabled taxa (23 living) have range samples but no sample passing the provider's representative temperature, rain and height limits. Most are small-range or freshwater taxa with 1–17 samples, so this is a triage list, not proof of absence. The Cyprus taxa fail the provider's minimum rain. Thresholds are not silently changed.
- **No content for some places.** Madagascar, Cuba and Hawaii have no mapped wild animals in these packs (only the domestic dog, which its pack does not spawn naturally). Open-ocean whales and dolphins are not provided.
- **Coarse geometry.** The baiji box admits tributaries inside it. Several fossil boxes and the split-taxon region lists are coarse interpretations of the cited sources; the rule-level evidence in `tools/fauna-taxa.json` was checked against the cited pages on 2026-10-02 (unverifiable details are marked there).
