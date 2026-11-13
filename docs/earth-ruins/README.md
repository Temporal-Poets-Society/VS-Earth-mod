# Earth Ruins — exploration expansion proposal

Design draft, 29 September 2026. **Confirmed cutoff: 1500 CE.**
Packaging is undecided. The proposed numerical building scale is provisional, not a user-approved setting.
Confirmed scale behaviour: standalone buildings stay fixed; terrain-dependent walls and roads adapt their length
and terrain fit to the chosen world. **Current priority: models, ruins and locations. Treasure design is deferred.**
This folder contains planning material and starter blockouts; it does not enable ruins in Earth world generation.

## The experience

Give every journey a destination and every region a recognizable human history. Major monuments occupy their
historical locations; ruined villages, farms, ports, camps and road stations make the journey between them interesting.
Large sites can include hidden rooms and passages, with their detailed contents decided later.

This is a fictional ruined world drawing on structures built **by 1500 CE**, not a claim that all these places
were abandoned in that year. Select an explicit historical building phase for each model. Later additions and
modern reconstructions need to be removed from reference photographs before modelling.

Four goals for the first playable release:

1. A player can identify a region from its architecture and ordinary settlement remains, without reading a label.
2. Every major ruin has at least two worthwhile discoveries, one visible and one reached through exploration.
3. Each pilot region has a landmark and a coherent set of smaller settlement or camp models.
4. The same site catalogue works on different Earth world sizes, with predictable placement and no duplicate landmarks.

## Recommended packaging

Develop an optional **Earth Ruins** companion with an Earth dependency. Keep the historical catalogue, structure
assets and loot definitions separate from the placement code, so the same content could later ship as an optional
module inside Earth. No decision on the final mod name or distribution is required to start authoring models.

Earth supplies coordinates, terrain, climate and reservations. Earth Ruins supplies historical sites, local layouts,
roads and structures. Sea, Magelan and Wmap are possible later integrations. The basic ruins should work with
vanilla blocks and Earth alone. Better Ruins may remain an optional coexistence case; its assets are not inputs to
this pack, and its generation must respect reserved landmarks before coexistence can be advertised.

## World size and building scale

There are three different measurements:

| Measurement | Rule | What a larger world changes |
|---|---|---|
| Geography | Project WGS84 latitude/longitude through the actual saved `EarthGeometry.FromGeo` | Distance between places, route length and geographic room |
| Architecture | Proposed **0.5 blocks per real metre** on all three axes | Nothing automatically; each building retains its authored size |
| Local complex layout | Expand crowded sub-sites into an authored layout around one geographic anchor | May allow more outlying buildings and separate settlements |

Never bake the 256k world's block coordinates into the catalogue. Earth currently supports different north/south
and east/west scales, a variable Pacific seam, an ocean gutter and optional Cylinder World wrapping. A simple
`longitude * fixedMultiplier` calculation would be wrong.

The README's 256000 × 102400 + Cylinder World example has roughly 563 blocks per degree east/west and 198 real
metres per block at the equator. The 512000 × 204800 example has roughly 1132 and 98. Those are examples, not presets
the expansion may assume. In particular, map width includes areas outside the cylindrical ring.

At geographic scale, Khufu's roughly 230 m base would be only about one block wide in the smaller example. At the
proposed architectural scale it is about 115 blocks wide. This deliberate enlargement is what makes exploration
possible. Doubling the map width should double Rome-to-Paris distance approximately, not double all doorways.

For a locally straight route away from the seam, use the actual geometry:

```
dx = deltaLongitudeDegrees * geometry.BlocksPerDegreeX
dz = -deltaLatitudeDegrees * geometry.BlocksPerDegreeZ
lengthBlocks = sqrt(dx*dx + dz*dz)
```

For real routes, project every sampled point, unwrap X along the shortest continuous local branch, and sum segment
lengths. Split where needed at the seam/gutter. Equirectangular distortion means a real kilometre does not have one
universal block length: east/west stretches toward the poles. Do not use `realKm / 0.198` as a general route formula.

World dimensions and content rules are frozen when a world is created. Choosing a larger **new** world is supported
by design. Resizing an existing save is a separate migration problem and must not silently reproject already placed sites.

An optional future `ArchitectureScale` is a world-wide authoring profile, not a per-site adjustment. It needs
separately validated model variants; do not resize block schematics with nearest-neighbour scaling at runtime.
Very short worlds or crowded maps may not accommodate every large model. Report that in a preflight site audit,
offer a reviewed compact layout, or omit the conflicting optional content; never silently shrink a single monument.

## Historical placement and local settlements

Use a hierarchy rather than one random ruin pool:

| Tier | What appears | Placement basis |
|---|---|---|
| Landmark | Unique named monument or recognizable ensemble | One historical geographic anchor per site ID |
| Historic settlement | Selected district of a city, village, port or monastery | Dated evidence for occupation before 1500 |
| Associated place | Quarry, farm, roadside inn, workshop, cemetery or outlying hamlet | Evidence where available; otherwise explicitly labelled thematic reconstruction |
| Camp | Hearth, shelter footing, fish camp, seasonal dwelling or trade stop | Attested occupation where known; sparse plausible infill along usable terrain/routes |

Initial content target: around **80% of settlement/ruin placements with a documented historical anchor**, with up to
20% explicitly inferred infill. This is a proposed content target, not a historical statistic. Incomplete historical
records must not make poorly documented regions appear uninhabited. Track evidence and confidence separately from
gameplay importance; balance content through authoring effort rather than importing all modern populated places.

The candidate coordinates in [site-catalog.csv](site-catalog.csv) are approximate planning anchors. They are not
surveyed entry points or approved placement coordinates. Every entry requires footprint, era and terrain review.
Some sources are research leads, explicitly distinguished from pages already consulted.

Close landmarks require a **shared complex**. Giza's pyramids and Sphinx cannot each use their raw geographic
projection plus a 100-block model: they would overlap. Anchor the Giza complex geographically, retain an approximate
historical arrangement locally at architectural scale, and reserve the entire result once. Apply the same rule to
Rome, Beijing, Angkor and dense medieval towns. Do not flatten an entire real city to make room for its ruins.

Proposed relocation policy: search within 16–96 blocks, with the actual permitted distance recorded for that site;
prefer the correct bank, island, ridge and approach direction. A move across a major river or off the correct island
fails even if the distance is small. After that, select a reviewed local terrain solution or report an unplaced site.
Keep the true anchor and final placement in the audit. River navigation channels remain open.

## Regional roster

These are starter **families**, not continent-wide interchangeable styles. The detailed catalogue stores individual
sites and an intended historical phase. Several candidates per family permit a flagship, a medium destination and
small associated places. The first release should use fewer families with complete supporting content, then expand.

| Family | Major candidates | Smaller places and recognizable details |
|---|---|---|
| Nile Egypt and Nubia | Giza pyramids + Sphinx; Karnak; Meroe | Mudbrick courtyards, mastabas, river landing, stone quarry; separate Nubian pyramid proportions |
| Roman Mediterranean | Colosseum; Pompeii district; Leptis Magna | Villa, bathhouse, roadside station, aqueduct remnants, milestone, buried drains |
| Western medieval Europe | Notre-Dame; Carcassonne; Conwy | Townhouse, mill, bridge, inn, market square, ruined monastery |
| Baltic and northern Europe | Turaida; Trakai; Visby | Timber farm, hillfort, fortified storehouse, fishing landing; local roof and masonry choices |
| Aegean and eastern Mediterranean | Athens Acropolis; Hagia Sophia; Ani | Courtyard, cistern, hill monastery, gatehouse; different Greek, Byzantine and Armenian kits |
| Sahel and West Africa | Timbuktu; Djenne-Djeno; Koumbi Saleh | Earthen compounds, market, granary, caravan well, manuscript cache; no modern Djenne mosque model |
| Eastern and southern Africa | Lalibela; Kilwa Kisiwani; Great Zimbabwe | Rock-cut court, coral-stone house, trading yard, dry-stone enclosure; three distinct traditions |
| West and Central Asia | Petra; Persepolis; Merv | Rock-cut facade, caravanserai, irrigation remains, courtyard and watch post |
| South Asia | Mohenjo-daro; Sanchi; Hampi | Brick drains, wells, stepped tanks, pillared halls and market streets; era-specific kits |
| East Asia | Early Ming Forbidden City; Qin mausoleum/terracotta pits; Great Wall | Courtyards, kiln, beacon, gate, granary, road station; wall phase checked section by section |
| Southeast Asia | Angkor; Bagan; Borobudur | Temple satellites, reservoirs, causeways and settlement platforms; Khmer, Bagan and Javanese kits |
| Mesoamerica | Tikal; Palenque; Teotihuacan | Plaza house, causeway, ballcourt, stela, agricultural terrace; avoid mixing distinct city traditions |
| Andes | Machu Picchu; Chan Chan; Tiwanaku | Waystation, terrace, storehouse, bridge anchorage and road; Inca, Chimu and Tiwanaku kits |
| North America | Cahokia; Chaco/Pueblo Bonito; Mesa Verde | Earthwork, timber settlement, great house, cliff dwelling and food store; specific regional traditions |
| North Atlantic | L'Anse aux Meadows; Brattahlid/Qassiarsuk | Turf hall, boat repair area, farm, landing; northern Indigenous camps need a separate researched kit |
| Oceania and Australia | Nan Madol; Rapa Nui; Budj Bim | Basalt enclosure, canoe landing, stone platform, fish trap and dwelling footing; distinct local traditions |

Antarctica has no pre-1500 human-settlement roster. Central/northern Asian camps, Japan/Korea, New Guinea,
Amazonian settlements and additional Indigenous North American traditions are explicit research gaps to fill,
not permission to use another region's architecture as a substitute.

## Time cutoff: the awkward cases

- **Forbidden City:** select the early Ming phase. The complex began in the early 15th century, but surviving
  buildings and decoration include later rebuilding. Modern photographs alone cannot establish the 1500 form.
- **Great Wall:** include earlier earthwork, stone and early Ming sections where supported. Much familiar surviving
  masonry belongs to later Ming construction; a single undated modern wall dataset cannot satisfy the cutoff.
- **Notre-Dame:** medieval massing and features; omit the 19th-century spire and later restoration inventions.
- **Timbuktu:** research each mosque/compound phase. Do not reproduce all present-day forms as if unchanged in 1500.
- **Kilwa:** favour medieval Swahili buildings and port remains; exclude later Portuguese additions.
- **Djenne:** Djenne-Djeno is an appropriate ancient settlement candidate. The present Great Mosque is not a
  ready-made pre-1500 reference.
- **Hagia Sophia:** select a pre-1500 Byzantine/early Ottoman state and verify each exterior addition separately.

Historical facts and fictional secrets must be separate in the authoring notes. An invented chamber under the
Sphinx, for example, is an adventure addition, not archaeological evidence.

## Hidden spaces and secret routes

Use the hidden spaces that suit each site: pyramid passages, Roman service corridors and cisterns, medieval crypts,
buried storehouses, rock-cut rooms, irrigation tunnels, temple substructures, collapsed courts and sealed archives.
Not every culture or every building gets European-style catacombs. Exposed earthworks and camps can be interesting
through traces, viewpoints, landscape puzzles and caches without becoming underground dungeons.

Each major ruin brief should specify a visible destination, an initial discovery, a deeper optional route and a lead
outward. Add readable clues (masonry changes, inscriptions, sightlines, drainage, displaced stones), a safe way back,
and at least one discovery that does not require destroying the whole monument. Preserve the site's silhouette when
weathering it. Danger and loot are designed deliberately, not multiplied by floor area.

Treasure, map fragments, chart interfaces and living villages are **deferred**, per the user's clarification.
For now, record potential discovery sockets and secret spaces in model manifests without selecting their rewards.
The only integration finding retained for later is that Sea already has seasonal prevailing winds, while Wmap's
physical charts remain a future feature. Neither integration is part of the modelling milestone.

## Roman roads and other long routes

Build actual walkable roads from vanilla path blocks. Local inspection confirms `game:stonepath-free` and
`game:stonepathfullblock`; the installed assets have a 1.30 walk-speed multiplier. Slabs/stairs need their exact
orientation variants resolved in the build adapter. Initial road width: 3 blocks, with wider authored stretches
near city approaches. Width is a gameplay/architecture choice; **length follows geography**.

Start with a small Via Appia segment and associated station, then expand the trunk route. Itiner-e offers a scholarly
Roman-road dataset with segment identifiers, downloadable geometry and a CC BY 4.0 licence. Pin the static release,
retain attribution and uncertainty fields, and inspect which records are roads rather than sea/river routes. Its
coverage/reliability varies; guessed connections should not be presented as excavated alignments.

Follow with an Andean road pilot, a caravan corridor, a Chinese road/post-station corridor and a Maya causeway.
Each requires its own geometry and date evidence. The Silk Roads form a network; they are not one continuous paved
road from Rome to China. On compressed terrain, limited local rerouting can avoid cliffs and protect river channels;
record the difference from the source route. Bridges, ferries and passes are authored crossing types.

For the Great Wall, store **dated polylines plus modules**, not one enormous schematic. Select a historically coherent
pre-1500 route set, preserve branches/gaps, project its actual vertices and derive in-game length from those paths.
Do not take the often quoted total across eras and force it into one continuous wall. Full geographic reach can be
a later milestone; it does not require all chunks to exist at world creation. Towers, gates and ramps have the same
architecture scale as other models. Wall width/height stay explorable as route length grows with the chosen map.

## Technical plan grounded in this workspace

Relevant existing code:

- `src/Core/EarthGeometry.cs`: `FromGeo`, `ToGeo`, `WrapX`, ring and gutter behaviour.
- `src/Core/EarthLayout.cs`: actual world-size-dependent geometry.
- `src/EarthMapSystem.cs`: public `Model`, `Data`, `Params`; lifecycle and world configuration.
- `src/EarthParams.cs`: save-frozen geographic parameters and terrain zones.
- `src/EarthWorldGen.cs`: vanilla generation integration and surface/water guards.
- `../Sea/src/Wind/WindModel.cs`: seasonal prevailing-wind function and optional source interface.
- `../Wmap/README.md`: explored/developer map separation and planned physical charts.

The current surface-structure guard bounds its generic footprint estimate at 96 blocks and samples every three
blocks. Large landmarks cannot rely on that alone. Use the full rotated footprint, foundation/carve volumes and
water exclusions. A new API exposing geometry, terrain probes and structure reservations is preferable to an
addon duplicating Earth's projection or reaching into private Harmony-patched internals.

Proposed pipeline:

1. **Catalogue validation:** date, source/confidence, anchor, culture, model version, size and dependencies.
2. **World planning:** project points and routes, group complexes, reserve landmarks and story locations, fit local
   layouts against terrain/water, reject conflicts, then add bounded settlement/camp candidates. Save the accepted plan.
3. **Spatial index:** only query nearby sites and route segments for an incoming chunk. Planning indexes coordinates;
   it does not eagerly generate the world or sample millions of blocks at startup.
4. **Chunk realization:** deterministic terrain/foundation edits followed by structures and decoration at a verified
   worldgen stage. Split by chunk; do not assume all neighbouring chunks are loaded. Persist plan/model versions and
   completion state so different generation orders/restarts cannot duplicate or truncate a landmark.
5. **Later discovery integration:** reserve model sockets now; define their contents after the modelling milestone.

Priority for overlap resolution: player-created/generated existing content and vanilla story reservations first,
curated landmark complexes next, roads/crossings next, optional satellites and infill last. Priority does not permit
bulldozing an existing structure. Better Ruins/other structure generators need a tested reservation adapter or a
clearly selected regional generation policy, not an unsupported promise of automatic compatibility.

Save `CatalogueVersion`, `ModelSetVersion`, `ArchitectureProfile`, route selection, accepted positions/rotations,
reserved bounds, content settings and generation progress. Follow Earth's existing opt-in upgrade pattern. Do not
install landmarks into already explored chunks automatically; an explicit migration tool is a separate deliverable.
Adding an addon to an existing save must not place half a complex across generated and ungenerated chunks.

## Delivery order and acceptance

| Step | Deliverable | Gate |
|---|---|---|
| 0 — current draft | Regional roster, shared standard, starter geometry | User can compare scale and select modelling priorities |
| 1 — authoring pilot | Giza pyramid/Sphinx; Colosseum; one Tikal temple; matching small site per kit | Recognizable silhouettes, consistent scale, source/fiction notes, traversable interiors |
| 2 — placement pilot | One historical complex per pilot region on new worlds | Stable across chunk order/restart, no river blockage, correct site anchors and reservations |
| 3 — first regional route | A Roman road segment and associated station | Path is usable and connects the intended historical places |
| 4 — regional packs | Timbuktu, Great Zimbabwe, Petra, Angkor and early Chinese content; then remaining families | Each region has a flagship plus medium and small destinations |
| 5 — long networks | Dated Great Wall route set; larger road networks | On-demand chunk realization and audited projected lengths |
| Deferred — rewards | Treasure and optional maps/villages | Separate design after the models and locations are established |

Test worldgen on at least the README's 256k and 512k cylindrical worlds and a larger non-wrapping world, plus short
height, sea-adjacent, river, steep-slope and seam cases. Audit every accepted/omitted unique site. Visit a multi-chunk
site from opposite directions in separate fresh worlds and compare blocks after restarts. Check doors, stairs,
escape routes, water sealing, lighting, story preservation and loot persistence in game.

Suggested performance gates to measure, not achieved results: no per-chunk scan of the full global catalogue;
no complete Great Wall generation at startup; preflight reports estimated touched chunks and retained plan memory;
profile p95 chunk-generation overhead against the same seed without the addon before setting release budgets.

Open choices: final packaging; approval of the 0.5 blocks/m profile; preferred first polished monuments; degree of
fictional underground space. Reward design is outside the current milestone. None prevents catalogue research or
provisional blockouts.

## Sources and evidence

The design and gameplay proposals are original recommendations. Site references establish historical context,
not model accuracy, exact footprint measurements or permission to redistribute their images/meshes.

- [Egyptian Ministry: Great Pyramid](https://egymonuments.gov.eg/monuments/the-great-pyramid/) and
  [Great Sphinx](https://egymonuments.gov.eg/monuments/the-great-sphinx/).
- UNESCO: [Great Wall](https://whc.unesco.org/en/list/438/),
  [Imperial Palaces](https://whc.unesco.org/en/list/439/),
  [Qin mausoleum](https://whc.unesco.org/en/list/441/),
  [Timbuktu](https://whc.unesco.org/en/list/119/),
  [Great Zimbabwe](https://whc.unesco.org/en/list/364/),
  [Lalibela](https://whc.unesco.org/en/list/18/), [Kilwa](https://whc.unesco.org/en/list/144/).
- UNESCO: [Petra](https://whc.unesco.org/en/list/326/), [Angkor](https://whc.unesco.org/en/list/668/),
  [Borobudur](https://whc.unesco.org/en/list/592/), [Tikal](https://whc.unesco.org/en/list/64/),
  [Teotihuacan](https://whc.unesco.org/en/list/414/), [Machu Picchu](https://whc.unesco.org/en/list/274/),
  [Chan Chan](https://whc.unesco.org/en/list/366/), [Tiwanaku](https://whc.unesco.org/en/list/567/),
  [Chaco](https://whc.unesco.org/en/list/353/), [Cahokia](https://whc.unesco.org/en/list/198/),
  [Nan Madol](https://whc.unesco.org/en/list/1503/), [Budj Bim](https://whc.unesco.org/en/list/1577/).
- [Notre-Dame: architectural chronology](https://www.notredamedeparis.fr/en/understand/architecture/).
- [Itiner-e: data, attribution and licence](https://www.itiner-e.org/about),
  [static dataset DOI](https://doi.org/10.5281/zenodo.17122148),
  [dataset research paper](https://www.nature.com/articles/s41597-025-06140-z).
- [Vintage Story BlockSchematic API](https://apidocs.vintagestory.at/api/Vintagestory.API.Common.BlockSchematic.html),
  [official implementation](https://github.com/anegostudios/vsapi/blob/master/Common/Collectible/Block/BlockSchematic.cs).
  Installed 1.22 assets were read for the export layout and vanilla path/masonry codes.

