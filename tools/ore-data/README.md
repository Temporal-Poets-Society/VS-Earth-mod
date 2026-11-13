# Ore data groups (decision 0065)

Each group of resources has a folder here and a catalogue in `assets/earth/config/ore-deposits-v1/`. A group session
writes only its own folder and its own catalogue file. See [decision 0065](../../docs/decisions/0065-ores-at-their-real-size.md)
for what the runtime does with them.

```
tools/ore-data/<group>/
  fetch_*.py, build_*.py      download and normalise (re-runnable; downloads go to C:\VS mods\Earth\rawdata\ore\<group>\, never into git)
  evidence.geojson            optional: extra heat-map evidence for the group's layer codes (schema below)
  heatmap-overrides.json      optional: {"drop": {"<code>": ["<source name>", "mrds", ...]}} drops existing bake sources for own codes
  ATTRIBUTION.txt             one block per source used in the shipped files: title, author, URL, licence, retrieved date
assets/earth/config/ore-deposits-v1/<group>.json     the catalogue (schema below), minified, at most 3 MB
docs/research/ore-deposits-2026-11-08/<group>.md      sources, licences, decisions, validation (totals vs. published figures)
```

Groups and their heat-map layer codes (each code belongs to one group only):

| Group folder | Layer codes |
|---|---|
| `coal-evaporites` | C_coal, NaCl, K, gypsum, B, S, P, N, alum |
| `base-metals` | Cu, Sn, Pb, Zn, Ag, Bi, Hg, W, Mo, Sb |
| `iron-ferro` | Fe, Mn, Cr, Ni, Ti, V, Pt, Al |
| `precious-gems` | Au, diamond, corundum, emerald, olivine, garnet, lapis, topaz, tourmaline, beryl, spinel, C_graphite, F, SiO2, U |

Deposit codes per layer code: `docs/research/remaining-ores-2026-09-29/game-mineral-scope.csv` (`commodity_layer`,
`deposit_code`), plus Earth's `earth-bauxite` (Al), `earth-gypsum` (gypsum), the gem codes of
`assets/earth/worldgen/deposits/earth-gems.json` and the layer deposits of 0060 (placer gold, placer tin, salt flats,
bog peat, fire clay).

## Catalogue schema (version 1)

```json
{
  "version": 1,
  "group": "base-metals",
  "sources": [
    {"id": "usgs-porcu-2008", "title": "Porphyry copper deposits of the world (Singer, Berger, Moring 2008)",
     "url": "https://pubs.usgs.gov/of/2008/1155/", "licence": "public domain (USGS)", "retrieved": "2026-11-08"}
  ],
  "gradeBands": {
    "Cu": {"unit": "%", "medium": 0.6, "rich": 1.5, "bountiful": 4.0}
  },
  "deposits": [
    {"id": "usgs-porcu-2008:123", "name": "Bingham Canyon", "lat": 40.52, "lon": -112.15, "layer": "Cu",
     "ores": [{"code": "chalcopyrite", "share": 0.85}, {"code": "malachite", "share": 0.15, "zone": "top"}],
     "shape": "stock", "mt": 3230, "grade": 0.88, "unit": "%", "density": 2.7,
     "topM": 0, "baseM": 1500, "lenKm": 3.0, "widKm": 2.0, "strikeDeg": 0, "dipDeg": 90,
     "age": "Cenozoic", "src": "usgs-porcu-2008", "q": "A"}
  ],
  "areas": [
    {"id": "bgr-coal:ruhr", "name": "Ruhr coalfield", "layer": "C_coal", "age": "Carboniferous", "src": "...",
     "geometry": {"type": "Polygon", "coordinates": [[[7.0, 51.3], [7.5, 51.3], [7.5, 51.6], [7.0, 51.3]]]},
     "beds": [{"code": "bituminouscoal", "topM": 30, "thickM": 2.0, "cover": 0.8, "grade": null},
              {"code": "anthracite", "topM": 600, "thickM": 1.5, "cover": 0.6}],
     "mt": 50000}
  ]
}
```

Fields:

- `deposits[]` are single ore bodies or tight clusters (one mine district) with a point position.
  - `layer`: the heat-map code. `ores[].code`: deposit codes (above); `share` of the tonnage (sums to 1); optional
    `zone`: `top` (oxidised or supergene cap, the upper part), `bottom`, or omitted (throughout).
  - `shape`: `stock` (upright body: porphyry, pipe, breccia, skarn, kimberlite), `lens` (flattened body with strike and
    dip: massive sulphide, SEDEX, podiform chromite, magmatic Ni-Cu), `veins` (a swarm of thin steep sheets in the
    footprint: lode gold, tin and tungsten lodes, Pb-Zn-Ag and Hg veins), `blanket` (a sheet in a cover layer:
    laterite bauxite, Ni laterite, placers, salt flats, bog iron) or `dome` (salt dome: halite column with a cap).
  - `mt`: ore tonnage in million tonnes, pre-mining (past production + reserves + resources) at the source's cut-off.
    Required. `grade` with `unit` (`%`, `g/t`, `ct/t`, `kg/t`) of the deposit's main resource; null when unknown.
    `density` in t/m³, optional (the runtime defaults by shape and ore).
  - `topM`, `baseM`: metres below the surface of the top and bottom of ore. `lenKm`, `widKm`: footprint along and
    across strike, in plan view (for a lens dipping under 45° the runtime reads `widKm` as plan width; a down-dip
    extent differs from it by the cosine of the dip).
  - `grades` (optional): co-product grades by band key, e.g. `{"Cu": 2.1, "Zn": 12.0}`, used with the file's
    `oreMetal` (below) so each ore takes its tier from its own metal. `strikeDeg` (0 = north), `dipDeg` (90 = upright). Give what the source states; otherwise give a
    reasoned estimate and set `"est": ["topM", "lenKm", ...]` naming the estimated fields.
  - `layers` (optional): the 0060 layer classes this body may fill (`alluvial`, `outwash`, `peat`, `laterite`,
    `saprolite`, `playa`, `deep-cover`, `cover`, `bed`, `basement`; names from `assets/earth/config/ore-layers-v1.json`).
    For catalogue bodies it replaces the per-deposit-code table of 0060. Default by shape: `blanket` takes `alluvial`,
    `outwash`, `laterite` and `saprolite`; every other shape takes the 0060 table of its ore codes (hard rock: bed and
    basement). Examples: Ni laterite `["laterite", "saprolite"]`, heavy-mineral sands `["alluvial", "cover"]` (`cover`
    is the other loose cover, beach and dune sand among it), salt flats `["playa"]`, alluvial diamonds and gem gravels
    `["alluvial", "outwash"]`. A blanket never replaces the column's top block (soil or turf).
  - `age`: host age as 0060 uses it (`Cenozoic`, `Mesozoic`, `Paleozoic`, `Devonian`, `Carboniferous`, `Permian`,
    `Precambrian`, ...). `src`: a `sources[].id`. `q`: confidence `A` (measured resource or production record), `B`
    (compilation estimate), `C` (order of magnitude).
- `areas[]` are basins and stratiform districts too large for a point: coal basins, potash and salt basins, iron
  formations, ironstones, Kupferschiefer, phosphorite fields, laterite provinces. `geometry` is GeoJSON in degrees,
  simplified to about 1 km. `beds[]`: each bed's deposit `code`, real `topM` below the surface, real `thickM` (the
  sum of workable seams in that bed group), `cover` (share of the area where the bed is present, 0..1) and optional
  `grade` with optional `unit` (the bed's ore takes its metal's band when `oreMetal` names one, else the area
  layer's band when the units match).
- `oreMetal` (optional, top level): `{"<ore code>": "<band key>"}`, the `gradeBands` key whose grade tiers that ore,
  e.g. `{"sphalerite": "Zn", "galena": "Pb", "tetrahedrite": "Sb|Ag"}` (the first key the body has a grade for).
  Without it an ore takes the body's `grade` and the band of its layer and unit. `mt` (optional) is the area's total tonnage for validation.
- `gradeBands` are shared by all files: a file may repeat another file's band (a co-product's `oreMetal`), but the
  copies must be identical; the game keeps the first file's (files load in name order) and `check_catalogues.py`
  fails on a differing copy.
- Coordinates in WGS84 degrees, longitudes -180..180.

## Group evidence (`evidence.geojson`)

FeatureCollection; read by `tools/bake_ore_heatmaps.py` (`read_groups`). Properties: `code` (layer code), `source`
(short source name, required), `plane` (`obs` default, or `pot` for geological permissiveness), `tier` (a tier of
`tools/ore-heatmap-settings.json`) or `peak` (0..1) with `radiusKm` for points; `weight` (0..1) for polygons; optional
`depthM`, `id`, `name`, `country` (enables the country envelope check).

## Baking and testing

- `powershell -File tools/ore-data/link-raw.ps1` once in a new worktree (links the git-ignored inputs from the main
  checkout).
- Test bake without touching the shipped file:
  `C:\VS mods\Earth\.venv\Scripts\python.exe -I tools/bake_ore_heatmaps.py --out <scratch>\ore-heatmaps.json --report <scratch>\report`
- Do not commit `assets/earth/config/ore-heatmaps-*.json`; the coordinating session bakes `ore-heatmaps-v2.json` once
  from all groups.
