# Global resource access: copper, tin and peat-associated bog iron

Date: 29 September 2026. Status: design requirements; not implemented or playtested.

**0.12.0 update:** the first [regional ore generator and finite peat-associated bog iron](regional-ores-implementation.md) are implemented and server-tested. The travel/access assessment below remains outstanding; no guaranteed copper/tin travel radius has been established.

## Scope

Copper and tin access must be evaluated **from any starting region worldwide**. Latvia is one regional geology example, not the centre of the global progression design. Keep credible ore locations and compatible source/host rocks; accept that some starting regions require substantial travel.

The first deliverable should be a resource-access assessment alongside the geological distribution map. It should answer where usable copper and tin are, how far the player must travel at the selected Earth scale, and whether that journey and extraction are possible before obtaining the resource in question.

Credible sources include **low-chance unrecorded deposits where regional geology and local host rocks are suitable**, as explicitly requested. Observed deposits strengthen hotspots; absence from a mine catalogue does not disable reviewed geological potential. See the [heatmap design](copper-tin-heatmap-design.md) for evidence layers and uncertainty. A low chance alone must not be counted as a guaranteed sufficient starter supply.

The [initial global copper/tin catalogue](copper-tin-global-data.md) is available for source review. It has not yet been deduplicated into generation districts or turned into validated early-access routes.

## Historical supply includes more than mapped mines

Modern mine locations alone are too restrictive for the source catalogue. Distinguish the original mineral-bearing rocks from places where people could collect material after weathering and transport, and from supplies moved by people.

| Supply | Historical evidence | Proposed Earth representation |
|---|---|---|
| Native copper, including transported pieces | Lake Superior sources include both bedrock copper and loose copper in glacial deposits, river bottoms and lakeshores. Native metal could be hammered and annealed without ore smelting. [NPS Minong study](https://www.nps.gov/subjects/nationalhistoriclandmarks/upload/Minong-Copper-Mining_NHL_WSecFinal_2021-05-19_Redacted.pdf). | Finite surface pieces within reviewed source and transport areas. A loose find need not indicate a vein directly underneath. Historical handworking is context, not a proposed change to the game's anvil rules. |
| Accessible copper ores | Prehistoric miners worked copper ores including malachite, using stone and antler tools and firesetting. [Historic England archaeometallurgy guide](https://historicengland.org.uk/images-books/publications/archaeometallurgy-guidelines-best-practice/heag003-archaeometallurgy-guidelines/). | Review exposed and weathered mineralization separately from deep deposits. Any new extraction mechanic requires a separate gameplay decision; do not count inaccessible rock as starter supply. |
| Stream tin and residual tin-bearing material | In the Penzance district, early tin extraction worked cassiterite in alluvial sand and gravel, before later shallow and underground workings. [BGS district memoir](https://webapps.bgs.ac.uk/memoirs/docs/B01737.html). Cassiterite is an ore requiring smelting, not metallic tin. [Historic England guide](https://historicengland.org.uk/images-books/publications/archaeometallurgy-guidelines-best-practice/heag003-archaeometallurgy-guidelines/). | Concentrate tin-bearing grains in suitable sediments linked to reviewed source areas and drainage. Review residual deposits independently. Do not grant tin to every river or every granite region. |
| Imported metal and recycled bronze | The Cape Gelidonya wreck carried copper and tin ingots and scrap bronze tools intended for recycling. [Institute of Nautical Archaeology excavation](https://nauticalarch.org/projects/cape-gelidonya-late-bronze-age-shipwreck-excavation/). | Assess actual trader, salvage and recycling availability separately from natural geology. Recycling bronze supplies an alloy; it must not imply a simple recipe for separating its copper and tin. |

The source data therefore needs primary mineralization, secondary collection areas and their transport provenance. Use drainage and documented glacial transport where applicable, rather than circular buffers around modern mine coordinates. A region with unsuitable local bedrock may still contain credible transported material; absence of local bedrock ore is not sufficient evidence to exclude every natural supply. Conversely, a possible trace occurrence does not establish enough recoverable material for progression.

Assess usable secondary deposits as potential destinations in their own right. They can shorten a player's journey to first copper or tin, but neither their existence nor their yield should be guaranteed in every region. Modern industrial workings and their waste also need to be distinguished from deposits available before industrial mining.

## What distance must mean

| Measure | Purpose |
|---|---|
| Nearest sufficient copper supply reachable before metal tools | Finds a viable start to the technology chain. A deep vein requiring a pick is not a valid first supply. |
| Sufficient copper for the first anvil, directly or in bronze | Goes beyond a few starter nuggets. Account for the metal needed for tools and the anvil, and any additional constituents and equipment required by the selected recipe. |
| Nearest sufficient tin supply reachable with copper-stage equipment | Tests access to tin bronze after copper tools. |
| Viable combined copper-to-tin route | Prevents choosing a nearby copper district that leads to an impractical tin journey when another copper district gives a better overall route. Include collection time and, separately, return travel where the player intends to keep a home base. |
| Distance to bedrock sources versus loose/placer supplies | Distinguishes first-tool quantities from a sustainable mining district. |
| Natural supply versus imported supply | Reports trade/loot as a distinct path, only when its availability has been verified. A hypothetical trader is not a guaranteed route. |

For every result, report the source district, route distance in **game blocks**, transport requirements, approximate travel time and enough information about yield to show that progression is possible. Geographic kilometres are useful for checking the real-world source, but do not directly express player effort.

Earth does not have a single universal conversion from geographic distance to route length. `EarthLayout` derives horizontal scale from world size or Cylinder World's ring; north/south scale can differ, and `EarthGeometry` includes seam warping and an ocean gutter. Use those transforms and the actual world's wrapping rules. A straight geographic line across sea or mountains is only a lower bound, not a walkable route. [Layout](../src/Core/EarthLayout.cs), [geometry](../src/Core/EarthGeometry.cs).

## Assessment method

1. Assemble reviewed source districts and eligible near-surface forms of copper and tin, including secondary collection areas. Record source geology, transport/deposition, depth, mineral form, extraction requirements, evidence and confidence. Preserve zero occurrence where both local and transported supplies have been reviewed and ruled out; label unassessed regions as unknown.
2. Build a coarse world-scale transport graph with land and water separated. A pre-metal walking profile cannot cross an ocean just because a water route is geometrically short. Water travel is a separate profile using actual available craft and their recipe requirements.
3. Query candidate resource districts from representative starts in each region. Find several viable copper/tin sequences instead of relying only on the closest point of each mineral independently. Filter by equipment and required yield before ranking routes.
4. Project into the selected game-world scale. Estimate route cost with stated movement assumptions; include elevation/barriers, necessary crossings and collection time. Report measured playtest times separately from modelled travel estimates.
5. Produce copper-access and tin-access heatmaps plus a region table: minimum/typical/longest sampled route, required transport, confidence and missing data. A region's capital or centre point is not a sufficient sample of the whole region.
6. Verify representative easy, remote and island starts on actual generated worlds and multiple seeds. Coarse maps screen the design; they cannot establish that one specific seed contains enough accessible ore.

Suggested authoring report columns:

`start region | copper source/form | pre-metal route blocks | tin source/form | onward route blocks | transport/gear | usable quantity | estimated time | evidence/coverage`

No exact global distances have been calculated in this pass: the reviewed deposit catalogue, world layout and valid transport profiles must exist first. Do not publish invented distances or promise a fixed search radius.

## How to make travel demanding and understandable

Tune visibility, local grade, deposit size and frequency inside credible source areas. Do not move copper or tin into unsuitable regions to meet a global maximum-distance target. The access report should expose an isolated or demanding start rather than secretly changing its geology.

Players need practical clues: outcrops, source-linked loose material, appropriate prospecting and regional information. An author/debug map can show exact predicted districts; the normal player interface need not reveal every coordinate. A world setup can describe starting-region resource access once supported by actual data. These are proposed interfaces, not implemented features.

Check the full sequence: first usable copper, processing vessels/moulds and fuel, copper tools, a reachable tin source, a useful bronze batch and the required anvil. **Bog iron does not bypass copper: the first anvil still needs copper, directly or in bronze.** Preserve the game's anvil and metalworking requirements; collecting bog ore early does not make it usable iron equipment. Alternative bronze alloys retain their own credible constituent distributions and should be checked for deliberate rather than accidental progression paths.

The [Latvia study](geology-latvia-pilot.md) includes potential reference districts in Cornwall, Erzgebirge and Falun; these are research examples, not a complete global resource network or calibrated travel routes.

## Bog iron: the requested peat collection model

**Bog ore is a limited deposit collected while digging natural peat, not a hard-rock mining resource.** This is a required behavior of the proposed Earth integration, not merely a possible later option.

- Select suitable peatland patches using local hydrology and an iron-supply suitability mask. Give them finite, sparse nodules or thin accumulations. Peat presence alone must not guarantee iron.
- Use a peat-digging action/tool available before bronze to recover bog-ore pieces. Keep harvesting requirements separate from the later furnace, fuel and bloom-working requirements.
- Reuse Geology Additions' bog-ore item and appropriate processing definitions. Its current ore block requires mining tier 3 and its ordinary generators target muddy gravel. Replace that natural placement/harvest path in the Earth profile; lowering the tier on the existing rock-like block alone would not satisfy the requested peat behavior.
- Persist deterministic deposit locations/depletion. Avoid a global random extra drop on every peat break: replacing blocks must not create infinite ore. Player-placed peat must not acquire natural bog-ore reserves. No rapid automatic regeneration is assumed.
- Keep a narrow compatibility adapter responsible for this behavior. The Earth core supplies peatland/hydrology context and regional rules; it need not duplicate the provider's ore artwork or smelting content.

**Peat eligibility (2 October 2026 revision, all worlds):** natural peat attempts now use a frequency multiplier instead of the earlier hard 600 mm / −5 °C / 2-block relief gates, which had excluded about two thirds of GUM's mapped peat (northern Russia, Hudson Bay Lowlands, the Baltics). Mapped peat bypasses the climate wetness rule, inferred peat uses a precipitation/evaporation index, permafrost peat is allowed, and relief is measured in metres; see [regional geology](regional-geology-implementation.md#clay-peat-and-salt-all-worlds). Peat-associated bog iron therefore has more candidate patches in boreal lowlands; its own evidence weighting and finiteness are unchanged.

The ecological/geochemical distinction remains relevant: peat does not turn into iron. Bog ore forms from iron carried in water and deposited under suitable conditions. A peat-associated collection model is a deliberate, useful game representation; real bog iron also occurs in other wetland sediments. [USGS field study](https://pubs.usgs.gov/of/2003/of03-346/), [USGS descriptions of peat-associated nodules](https://pubs.usgs.gov/pp/0113/report.pdf).

Implementation should compare a persistent ore-bearing peat variant with a deposit mask/depletion record attached to natural peat. Choose the smallest approach that correctly handles chunk unloading, block replacement and multiplayer without creating a new simulation. Any new block variant would itself become saved content; this is part of the adapter design decision.

## Acceptance gate

The first geology release needs a credible, playable copper-to-bronze route from each advertised supported starting region, with difficult or inaccessible starts explicitly identified. It does not need every region to provide its own copper and tin. Bog iron should visibly follow the peat collection rule and remain limited. Exact route lengths and supported regions are outputs of data and gameplay validation, not assumptions of this report.
