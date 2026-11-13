# Shared model contract — draft 1

Applies to all builders, including future modelling agents. Proposed profile `earth-ruins-half-v1`:
**0.5 blocks per real metre**, equal X/Y/Z scale. Cutoff **1500 CE**, confirmed by the user.
Do not interpret larger world dimensions as permission to build a larger version of just one landmark.

## Build dimensions and orientation

- Convert measured real dimensions with `floor(metres * 0.5 + 0.5)`; keep a dimension ledger with source and rounding.
- Separate measured dimensions, rough reference envelopes and invented gameplay geometry. A reference envelope
  is adequate for a labelled blockout, not an approved replica.
- Doors need at least 2 blocks clear height (3 preferred), normal corridors 2 blocks wide and 3 high. Main stairs
  need at least 2 blocks width. Use slabs/stairs for comfortable rises in a finished model.
- Human figures, furniture, mechanisms and circulation use a shared **gameplay scale**. A terracotta warrior must
  remain readable next to the player; record this intentional exception instead of resizing its entire building.
- X = east/right, Y = up, Z = south. North = -Z. Unrotated entrance direction, intended geographic bearing and
  schematic origin are explicit metadata. Preserve accurate local bearings when supported; do not random-rotate landmarks.
- Dimensions always use **X × Y × Z** in metadata. Human-facing tables may use width × depth × height only if labelled.
- Use a consistent ground datum. Foundations and cellars are below it, above-ground architecture above it. Report
  `groundY`, lowest carve level, top, water exclusions and support requirements.
- Keep raw schematic axes below the format's 1024-cell coordinate limit; split larger structures into modules.
- Use vanilla blocks for the baseline palette. Optional decorative/custom blocks need a declared fallback. Store
  asset codes, not assumed numeric game IDs. Schematic numeric IDs are local palette keys only.

## Complexes and underground additions

The geographic anchor belongs to the whole historical complex. Child models use a local layout and do not each
project to near-identical world coordinates. Include an exterior bounding/reservation polygon plus carve/fill volumes.
Do not let decorative scatter expand outside the reservation without an explicit placement check.

Each final model has a clear entry-to-interest route, an exit, and optional secrets. Shared underground modules may
reuse construction techniques but should not repeat the same dungeon behind every monument. Historical passages
and fictional additions are labelled separately in the brief. Do not rely on chiselling through arbitrary solid rock
to reach the main content. A secret may deliberately require tools, but that choice must be visible in its brief.

Build intact massing first, then apply designed ruin variants. Weathering follows material and environment: sand
burial, eroded earth, frost damage, timber loss, vegetation, collapsed roofs and stone reuse. It is not uniform random
block deletion. Keep identifiable features, path clearance and structural support after weathering.

## Required delivery from a builder

1. Editable source or generator and native Vintage Story block schematic(s).
2. Manifest: stable site/model ID; profile/version; dimensions and ground datum; coordinates/complex ID; historical
   phase; local bearing; palette/fallbacks; dependencies; carve/fill/air mask; loot and discovery sockets.
3. Dimension ledger and reference list, with inferred/fictional details called out.
4. Exterior views from four sides, top plan, underground cutaway, scale comparison with player/corridor dimensions.
5. Navigable route and secret-location notes for review; spoilery authoring data stays out of normal player charts.
6. Validation report: size and palette validity, rotation bounds, support, connected circulation, no misplaced water,
   chunk-edge placement, and in-game inspection. Explicitly identify checks not yet run.

Use stages: `candidate → researched → blockout → playable → art-reviewed → worldgen-tested → released`.
Blockouts are disposable scale and silhouette studies, not finished historical models.

## Initial assignments, ready to give to individual agents later

| Model | First pass | Supporting kit | Special check |
|---|---|---|---|
| Giza / Khufu | Pyramid proportions and recognizable face; readable entrance/passage | Mastaba, small court, quay and quarry | One Giza reservation; distinguish real passage plan from invented branches |
| Giza / Sphinx | Separate sculpture blockout, east-facing in complex layout | Enclosure and nearby temple remains | Same 0.5 blocks/m ledger as pyramid; planned secret is explicitly fictional |
| Colosseum | Elliptical outer arcades, seating and arena; ruin silhouette | Villa fragment, bath/service room, road station | Hypogeum access, stair route and exits; no medieval/modern features mixed accidentally |
| Tikal / selected temple | Stepped limestone mass, stair and roof-comb silhouette | Plaza house, stela, causeway and reservoir edge | Research selected temple dimensions/orientation; stair remains usable after scale reduction |
| Timbuktu / selected compound | Dated courtyard and earthen architecture | Manuscript room, market, granary, well | Choose exact phase; no generic Egyptian palette or invented stone catacombs |
| Great Zimbabwe | Great Enclosure wall and conical tower, then hill fragment | Daga-house footing and dry-stone court | Keep dry-stone construction and local proportions; avoid arbitrary dungeon |
| Petra | One documented facade plus approach/cistern | Caravan court and water-channel module | Terrain cutting bounded and compatible with Earth cliff relief |
| Angkor / selected temple | One recognizable temple unit and causeway | Reservoir edge and regional house platforms | Khmer phase/palette; vegetation respects stairs and entrances |
| China / Qin pit | A representative buried pit with readable warrior groups | Kiln/work yard and enclosure | Figures at declared gameplay scale; do not silently shrink architecture |
| China / wall kit | Earthwork, stone section, ramp, gate and beacon | Road/post station | Dated route eligibility; no later Ming towers smuggled into 1500 profile |
| China / Forbidden City | Early Ming gate and court module before whole complex | Regional court, granary, workshop | Current surviving roof/decor forms require phase review |
| Paris / Notre-Dame | Medieval facade, nave, buttresses and crypt concept | Townhouse, quay, bridge/market fragment | Exclude 19th-century spire; check island and Seine clearance |

When delegated, each builder owns one model directory. One integrator owns the common profile, palettes and shared
validation; builders must not change the global scale or the worldgen planner independently. Do one reviewed pilot
before parallel production. No modelling agents have been launched as part of this draft.

## Starter blockouts in this folder

The generator creates three provisional silhouettes at the shared scale: a Khufu-sized pyramid with an invented
test passage, a Colosseum-sized amphitheatre massing study, and a Tikal-inspired temple study. The latter two use
rough planning envelopes pending dimension research. They establish size and export format only; they are not
accurate replicas, complete ruin art, complete dungeons, or a worldgen release.

Schematic files deliberately include explicit air only where the generator carves a passage/arena; omitted cells
are not a whole-box terrain clearing instruction. The prototype manifest's `placement=manual-empty-test-area`
requires an already clear test area. Production placement still needs a reviewed carve/fill mask and terrain fitting.
