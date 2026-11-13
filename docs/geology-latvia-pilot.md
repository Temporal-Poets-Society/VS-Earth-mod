# Credible regional geology: Latvia pilot and copper/tin progression

Date: 29 September 2026. Status: proposed integration requirements; no gameplay changes or combined playtest.

The user's follow-up clarifies that copper/tin travel distance is a **global, any-starting-region requirement**. This file retains Latvia as a geology example. The controlling access and peat-ore requirements are in [Global resource access](geology-resource-access-plan.md).

## Direction agreed with the user

Resources should occur in credible real-world regions, in appropriate host rocks and geological settings. Some regions should lack particular accessible ores, creating a reason for long-distance travel. Copper and tin are the first priority because they determine early progression. Geology Additions is the preferred first optional content provider, including its existing bog iron.

This refines [ADR-001](regional-world-architecture.md): the default is credible distribution, not locally guaranteed progression resources. Quantity can be adjusted inside a credible deposit area; geography and mineral identity should not be changed to supply every region. Modern country borders are convenient labels for this pilot, not ore-generation boundaries.

## Latvia: evidence and proposed treatment

The table distinguishes researched evidence from proposed game rules. Exact deposit polygons, depth models and gameplay quantities remain to be compiled.

| Resource | Evidence | Proposed Earth treatment |
|---|---|---|
| Bog iron | LU's geology collection account documents local iron production from bog ore and describes deposits as small and variable in quality. [LU Museum, October 2017 entry](https://www.muzejs.lu.lv/petnieciba/menesa-prieksmets/2017-gads/) | Limited nodules/patches recovered by digging natural peat in suitable iron-bearing wetland settings. Do not place it in every peat bog or as hard-rock ore. Reuse Geology Additions material with an Earth-specific harvest/placement adapter. |
| Deep iron | LU's active research identifies Staicele, Strenči, Inčukalns and Gārsene as basement-ore investigation areas. This establishes deep geological potential, not early mining access. [Research project](https://www.lu.lv/zinatne/projekti/nacionalas-programmas-un-projekti/flpp-2023-gada-konkurss/metalus-saturoso-kristaliska-pamatklintaja-iezu-rudu-potencials-viduslatvija-un-austrumlatvija/) | Keep deep basement iron separate from bog iron. Preserve the practical depth barrier; do not lift deposits into shallow sedimentary cover. The user's approximately 700 m Staicele example agrees with the [National Encyclopaedia's locality entry](https://enciklopedija.lv/skirklis/245358); obtain primary borehole depth/extent data before fixing its geometry. |
| Copper and tin | This review established no historically accessible Latvian source deposit to map. Archaeological research documents the lower Daugava's participation in long-distance bronze exchange and local processing. That does not identify one universal source mine. [Vasks, 2021](https://e-journals.ku.lt/journal/AB/article/2570/info) | Leave local natural starter deposits absent in the reviewed pilot unless positive deposit evidence changes the decision. Travel or explicitly supplied imported metal is the intended progression path. This is a conservative generation choice, not a claim that no copper or tin atoms/mineral traces exist beneath Latvia. |
| Peat | A major local resource formed under wet, oxygen-poor conditions. [LVĢMC 2020–2023 report, p. 13](https://videscentrs.lvgmc.lv/files/Zemes_dziles/Kopsavilkuma%20p%C4%81rskats%202023%202.pdf) | Common within mapped peatlands and suitable persistent wetland basins, with variable thickness. Reuse vanilla peat initially; later distinguish peat types only if provider assets or gameplay need it. |
| Clay | LVĢMC describes Devonian/Quaternary building clays dominated by illite, with kaolinite/chlorite admixtures; Triassic smectite-rich clay is another resource. [Same report, p. 10](https://videscentrs.lvgmc.lv/files/Zemes_dziles/Kopsavilkuma%20p%C4%81rskats%202023%202.pdf) | Make ordinary pottery/building clay locally abundant in mapped sedimentary settings. Classify by composition and use; ordinary clay is not automatically high-grade fireclay, kaolin or bauxite. Reserve high-temperature applications for evidenced material properties. |
| Dolostone and limestone | LVĢMC places important Devonian dolostone in central/eastern Latvia and useful Permian limestone in the southwest, including Auce, Saldus and Nīgrande surroundings. [Same report, pp. 8–9](https://videscentrs.lvgmc.lv/files/Zemes_dziles/Kopsavilkuma%20p%C4%81rskats%202023%202.pdf) | Map their respective formations and depth/exposure. Geology Additions supplies dolostone. A single currently prominent quarry is not the full historical rock distribution. |
| Chalk | The reviewed Kūmas source describes limestone and lists other historically worked deposits. It does not establish that Latvia has exactly one chalk occurrence. [LU limestone collection leaflet](https://www.lu.lv/fileadmin/user_upload/LU.LV/Apaksvietnes/Izstrade/Muzejs/Bukleti/Zemes_dzilu_bagatibas_Latvija_Kalkakmens.pdf) | Keep chalk and other limestone lithologies distinct in the catalogue. Do not label Kūmas chalk merely to match a vanilla block name. A chalk-specific local mapping remains unverified. |
| Gypsum | LU documents major deposits in central/southern Latvia, including Saurieši, Salaspils, Skaistkalne and Zvejnieki. [LU collection study](https://www.muzejs.lu.lv/par-mums/zinas/zina/t/59038/) | Regional sedimentary/evaporite layers and lenses in the relevant formations, not a nationwide chance in arbitrary rocks. |
| Bauxite | No documented local bauxite deposit was established in this review. | No natural bauxite generation in this pilot without positive evidence. Do not infer its existence from clay abundance or a mod's substitute recipe. |

Modern extraction lists do not describe an untouched historical world. Restore documented original formations, not modern excavated holes, and do not remove an originally present resource because its real mine is now depleted. Ancient ore formation also should not be inferred solely from present-day temperature and rainfall.

## What Geology Additions already supplies—and what it does not

Read-only inspection of version 1.4.8 found the following. These are asset definitions, not measurements of actual game behavior. The previously recorded advertised game support reaches 1.22.3; Earth testing on 1.22.7 is still required.

| Asset | Finding | Integration consequence |
|---|---|---|
| `assets/game/blocktypes/bogiron.json` | `game:bogiron`, mining tier 3, drops `game:stone-bogiron` | Installed vanilla pick definitions give copper tier 2 and bronze tier 3. Unmodified bog iron is not a pre-copper collection resource. |
| `assets/game/patches/ores/stone-bogiron.json` | Eight bog-iron items per `ironbloom` via the smelting definition | Reuse the item/bloom chain; verify furnace and bloom-working requirements during gameplay testing. It does not eliminate bronze/tool dependencies. |
| `assets/game/worldgen/deposits/other/bogiron.json` | Two surface-following generators targeting `muddygravel`, with rain/temperature conditions and relative-height parameters | There is no Earth-region or iron-bearing groundwater criterion in these definitions. Earth must supply eligibility and calibrate height/host placement against its terrain. Avoid retaining a second unrestricted placement path. |
| `assets/game/patches/itemtypes/rockitemuses.json` | Crushing mudstone yields `crushed-bauxite` | Geographic absence of bauxite rock alone will not preserve material scarcity. A geology profile needs an explicit decision on this shortcut; generic mudstone is not evidence of bauxite. |
| `assets/game/patches/worldgen/mudstonefireclaypatch.json` | Extends fireclay generation into mudstone hosts | Host identity alone must not imply that every mapped mudstone formation supplies refractory clay. Review together with clay composition and processing. |

The five inspected definitions are saved under [the research folder](research/regions-2026-09-29/README.md), using filenames beginning `geoaddons-assets_game_`. The core mod need not duplicate bog-iron artwork or items.

The requested integration must recover small bog-ore quantities through peat digging with a pre-bronze tool. Replace the provider's normal natural placement/mining path within the Earth profile, and keep deposits finite and tied to natural peat. Smelting and working the resulting iron still require a coherent technology path. No such code or recipe change has been implemented in this report. The first progression milestone remains global copper and tin access.

For the strict geology profile, document and narrowly override material shortcuts where they defeat regional identity. Keep scientifically defensible alternatives with their own requirements; do not require travelling for a named item when an appropriate local substitute should actually perform the same function. Any recipe change belongs in the explicit compatibility profile, not a hidden global core patch.

## First milestone: copper to tin bronze

Test a complete dependency chain:

`pre-metal tools + food/transport + clay vessels/moulds + fuel → first copper supply → copper tools → accessible tin → tin bronze → iron-working equipment`

The diagram describes validation order, not a requirement to remove other vanilla bronze alloys. Bismuth/black-bronze alternatives should retain their own credible constituent distributions and be audited for unintended shortcuts; copper remains essential.

### Copper

Map credible mineralized districts, actual host rocks and the form available near the surface. Where geology supports it, provide a small but usable first supply through loose copper pieces, appropriate weathered material or suitable placer/panning deposits. Link those pieces to a source area. A hard-rock ore body that requires a copper pick cannot be the only route to the first copper pick.

Do not globally scatter starter nuggets across copper-poor regions. For Latvia, progression must have a reachable source district or an explicit imported-metal route. Validate movement with equipment available at that stage: a boat or other transport that itself needs bronze would create a circular requirement. Do not assume an unimplemented Magelan feature or guaranteed trader stock solves it.

### Tin

Use cassiterite-bearing districts with credible source rocks, weathering and downstream transport. Host-rock compatibility is necessary but insufficient: not every granite contains a tin deposit, and not every river should yield tin from panning.

Prioritize shallow, exposed or alluvial occurrences suitable for the intended stage. Tin can require a further expedition after copper tools are obtained. Quantities should support a useful bronze batch, without turning an entire sedimentary plain into a tin source.

### Source districts to investigate first

These are credible reference locations, not yet implemented routes from Latvia or claims about the precise origin of ancient Latvian metal:

- **West Cornwall:** the British Geological Survey describes early tin extraction from cassiterite-bearing alluvium and probable copper collection from cliff-exposed lodes. This is a useful pilot for accessible tin and copper tied to primary mineralization and erosion. [BGS Penzance memoir, economic geology](https://webapps.bgs.ac.uk/memoirs/docs/B01737.html).
- **Erzgebirge/Krušnohoří:** documented tin placer landscapes provide another tin-region candidate, requiring district/catchment mapping. [UNESCO Eibenstock mining landscape record](https://whc.unesco.org/en/documents/166867).
- **Falun, Sweden:** a real historical copper district worth investigating for a Baltic-region route. Its museum dates the mine's discovery to the Viking Age; do not call it a proven Bronze Age supplier to Latvia. Surface mineral accessibility and processing still need their own geological review. [Falu Gruva museum](https://www.falugruva.se/en/see-and-do/the-mine-museum/).

## Generation rules and acceptance criteria

Every natural ore pathway must use the same eligibility model: major deposits, small deposits, loose nuggets and panning. Natural mineral indicators and prospecting must describe the generated resources. Imported objects, trader stock and ruins are separate human/gameplay sources, not proof of local ore; identify their role explicitly if included in progression.

Keep separate fields for region/province, host lithology, process, source catchment, depth, grade/quantity and confidence. A Latvia-wide boolean is too coarse for bog iron, gypsum or carbonate formations. Peat, clay and bog iron need near-surface hydrology/cover rules; deep basement iron needs structural/depth rules. Missing data must be visible in diagnostics.

Before calling the first profile playable, demonstrate on multiple seeds/scales:

1. Credible copper and tin regions contain harvestable supplies with matching rocks and indicators; reviewed unsuitable regions do not receive generic fallbacks.
2. For starting regions worldwide, assess distance to sufficient first copper, copper-stage tin and viable combined routes. A resource-poor start must reach an identified source or verified import route before requiring the missing metal if it is advertised as a playable start. Record travel distance/time at the selected Earth scale; identify demanding/inaccessible starts explicitly.
3. Available material is sufficient for the actual tool/mould/alloy chain, including losses and required mining tiers. Mere trace presence does not pass.
4. Bog iron is recovered from limited deposits in natural peat using a digging tool, and remains distinct from deep iron. Peat abundance does not automatically imply bog ore everywhere; replacing peat cannot renew depleted deposits.
5. Ordinary clay, fireclay/kaolin, bauxite, limestone/dolostone and gypsum maintain distinct identities and uses. Review provider recipes and all natural generation paths together.
6. Deep deposits remain deep or outside the accessible model; vertical compression does not manufacture an easy mine.

The open work is geographic data acquisition and gameplay validation, not modelling new rocks or animals. No map layers, deposits, recipes, trade inventories or saved chunks were changed during this research pass.
