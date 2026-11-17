# 0091: Earth's grass calendars in one colour map, and it gives way to the game's limit

Status: **built, in Earth 2.1.3** (EA-0154). Date: 2026-11-17. Decided by the coordinator
as maintainer (D72): "Earth must never be what pushes the game past 40, and must not crash it"; then, after the author's
"We have to address color maps asap", the seven calendars packed into one map with the same look. How it is built is this
session's.

## Context

Two players' clients crashed while joining Earth 2.1.2 worlds: IndexOutOfRangeException in vanilla's
`ClientWorldMap.PopulateColorMaps`. Vanilla's `LoadColorMaps` (a thread the client starts after every mod's AssetsLoaded)
numbers the colour maps that go into the block atlas, and `PopulateColorMaps` writes four floats per map into a fixed
array of 160 (`GlobalConstants.MaxColorMaps`, 40). Vanilla has 26, Earth had 7 (its grass calendars since 2.0:
evergreen, four growing-season lengths, summer-dry, steppe), Wildcraft Trees 4: 37. The players also had Wildcraft Fruit,
newer Floral Zones packs and other plant mods.

## Decision

1. **One map.** `earthgrass-seasons.png` (128 × 168, `tools/make_grass_seasons.py`) holds the seven calendars as bands
   of 24 rows: 4 rows repeating the calendar's first row, its 16 rows (each band pixel for pixel the 128 × 16 map it was,
   checked against the 2.1.2 files), 4 repeating its last. A grass block's colour data carries Earth's map's place and the
   calendar's band + 1 in bits 13-15, which vanilla reads only for leaves (their per-tree offset); Earth writes them only
   on the cube faces of `seasonalGrass` blocks (ChunkTesselator.TesselateBlock's colour data and getColorMapData; tall
   grass's JSON meshes keep vanilla's map ids), which no chunk shader draws as leaves.
2. **The chunk shaders take the band.** Vanilla's colormap.vsh takes a season map's row from noise clamped to a 16-row
   map's first and last row centres. `Core.GrassSeasonShader.Patch` rewrites that one expression, in the four chunk
   passes, as the client's shader loader reads them (a new PatchVanilla feature, GrassSeasonShader, on
   `ShaderRegistry.LoadShaderProgram`, as the season tint's): where the season map is Earth's (its place, written into the
   code) and bits 13-15 are set, the same row moved into the band; otherwise vanilla's. The place is known once vanilla
   has numbered the maps, so EarthGrassSeason loads the shaders again with it and uses the map only when all four passes
   carry it. The u, the noise and the in-band row are vanilla's: the same look. The repeated rows keep a band from
   blending with its neighbours at coarser mip levels (vanilla's own 16-row maps blend with whatever the atlas puts next to
   them). Both conditions in the shader keep shaders loaded with an old place (after leaving an Earth world) from touching
   another world's map, whose colour data has no bits there.
3. **Fallback.** Where a pass lacks vanilla's row (a game update, or another mod's colormap.vsh), the loader hook is off,
   or the shaders are loaded again later without it, the packed map is not used and grass follows vanilla's seasons (the
   wet and dry seasons from Earth's rain stay); one log line. The packed map is never drawn with vanilla's rows.
4. **Give way.** A client mod system (`EarthColorMapBudget`, last in AssetsLoaded at ExecuteOrder 1e9: vanilla registers
   `config/colormaps.json` at 0.3, and mods register theirs in AssetsLoaded or before) counts the maps bound for the atlas
   (LoadIntoBlockTextureAtlas with a texture, as vanilla counts them). Past 40, Earth's map leaves the atlas (vanilla still
   reads it as a picture but gives it no place) and grass follows vanilla's seasons, quietly
   (`EarthGrassSeason.PackedMapPlace`). One warning names the count and the other mods' maps by mod (the domain of each
   map's texture, most first: "game 26, wildcrafttree 4, ..."), so a player can see the rest fits. Where the other mods'
   maps alone pass 40, Earth can't prevent the crash; it still takes its own out and one error says the crash comes from
   the other mods, naming their count and the mods that add the most (eight, the rest summed). While everything fits, one
   information line gives the count.
5. **The seven strips stay as assets** (`earthgrass-<calendar>.png`), no longer colour maps: World Map's far terrain reads
   them by `GrassPhenology.Codes`' names (docs/REFLECTION-SURFACE.md).
6. **No fewer calendars** (EA-0155): the shader mixes a season map into the grass tint by a weight from the temperature
   (0.71 in the taiga, 0.91-0.93 from the Baltic to Ireland and in the steppe, 0.77 on the Mediterranean, 0.20 in the
   tropics), so dropping any changes how grass looks; packing them keeps all seven in one place.

## Tests

- `tests/PatchVanillaTests/ColorMapChecks.cs`, with the game's own ColorMap objects in its OrderedDictionary: the
  author's mods are 26 + 1 + 4 = 31 and Earth's map is the shader's place 26; 41 give way (vanilla then numbers 40, every
  picture still read, the fallback quiet), 48 still over (the error names the mods), exactly 40 fits; maps without a
  texture or out of the atlas don't count; a map of the wrong height is not used; the colour data's band bits; the edit on
  the installed game's colormap.vsh (the row once, replaced alone, no apostrophe for strict drivers); the packed picture's
  bands and repeated rows. The PatchVanilla feature binds, and a renamed loader turns only it off.
- `tests/GrassShaderCheck` (new, Run-All): the four chunk shaders with and without Earth's rows compile and link on this
  machine's driver (AMD Radeon RX 6600, settings low and high), and the edited calcColorMapUvs, run on the GPU for 2,000
  world positions, takes for each band vanilla's 16-row row moved into the band with the same u (worst 0.00013 texels),
  the noise reaching both edge rows; Earth's place without the bits keeps vanilla's row.
- Not run in game (the bench takes over the author's screen).

## Consequences

- Earth takes 1 of the game's 40 colour map places (was 7). With the author's mods 31 of 40 are in use.
- A game with 41 or more colour maps no longer crashes because of Earth: its grass has vanilla's one calendar. Below that
  grass looks as in 2.1.2.
- A mod that registers colour maps after Earth's check (later than ExecuteOrder 1e9 in AssetsLoaded, or after it) is not
  counted. A mod that replaces colormap.vsh leaves Earth's grass on vanilla's seasons (said once).
