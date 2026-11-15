# 0081: Large config JSON travels brotli-packed in the release zip

Status: **built** (14 November 2026, for EA-0054: room in the zip before habitat code, blocks or data). Date: 2026-11-14.

## Why

The rc.13 zip was 39,989,244 bytes, 11 kB under the Mod DB's 40,000,000. The habitat steps ([0080](0080-habitats-by-site.md))
add code and ground blocks; step 1 alone put the zip 564 bytes over. Measured options:

| Option | Room | Cost |
| --- | --- | --- |
| Brotli-pack the large config JSON (this decision) | ~310 kB | a build step and an unpacker; nothing lost |
| Leave `EarthMap.pdb` out | 244 kB | no line numbers in players' crash logs |
| Repack the ore heat maps' layers from gzip to brotli | ~110 kB | their bytes are pinned by the bake manifests (v1 frozen) |
| Drop old data (`climatefix.bin`, `ore-heatmaps-v1.json`) | 0.7 MB | breaks the worlds that use them: not an option |

Large data that only the server reads still belongs in the data release ([0066](0066-one-mod-data-from-github.md)).

## What

- The Release build's `PackJsonAssets` step (`EarthMap.csproj`) replaces every `assets/earth/config/**/*.json` of at
  least 40 kB in the output, except the ore heat maps, by `name.json.br` (brotli, quality 11). The repository keeps the
  plain JSON; the packed bytes decode to exactly the repository's bytes.
- `PackedAssets` (a ModSystem, ExecuteOrder 0, both sides) unpacks each `earth:*.json.br` at AssetsLoaded, before any
  other system reads assets, and adds it under its plain name (`IAssetManager.Add`). Every reader, Earth's or another
  mod's, gets `earth:config/treeregions.json` and the rest as before. A plain asset of that name wins (Debug builds).
- rc.13 with step 1: 12 files packed (treeregions, regionaltrees, the four ore deposit catalogues, fauna-species,
  fauna-vanilla, geology-grids-v1, ground-rules-v1, placement, regionalplants); zip 39,686,896 bytes, 313 kB headroom.

## Fixed after review (14 November)

- After start-up the server drops the bytes of every unpatched asset (`AssetManager.UnloadUnpatchedAssets`), and the
  mod's origin cannot load an unpacked asset again (it knows only `name.json.br`), so a later reader (`/wgen regen`,
  `/earth where`) got nothing. `PackedAssets.Unpacked` keeps the packed bytes and unpacks again whenever its data was
  dropped; EarthWorldOptionsTests checks it.
- Under Visual Studio's .NET Framework MSBuild there is no `BrotliStream`: the step leaves the files plain with a
  warning there (the zip may then pass 40 MB); build releases with `dotnet build`.

## Watch

Another mod that reads one of these assets in its own AssetsLoaded with an ExecuteOrder below 0 would not find it; none
does now. A file added to `assets/earth/config` above 40 kB is packed too.
