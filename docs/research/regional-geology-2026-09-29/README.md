# Regional geology source cache

The original GLiM gridded ZIP and extracted GUM gridded ASCII plus class names are openly licensed CC BY 3.0. Full author citations and transformation notes are shipped in [runtime attribution](../../../assets/earthmap/config/geology-attribution.txt). Original URLs and hashes are in [bake-manifest.json](bake-manifest.json).

- `glim-download.zip`: published 0.5-degree grid and class names, fetched via https://hdl.handle.net/10013/epic.39939.d001.
- `gum_v1.0_0point5deg.txt.asc` and `classnames.txt`: selected nested archive members of https://hs.pangaea.de/Maps/Boerker-etal_2018/Boerker_et_al_GUM_v1.0.zip.
- `gum-archive-inventory.json`: original nested archive member sizes; no original GIS shapes were downloaded or silently substituted.
- `bake-manifest.json`: reproducible source/runtime hashes and grid class counts.
- `worldgen-hook-audit.json`: installed assembly hash, observed generation order and child-grade compatibility behavior.
- `ga-child-grade-normalization.log`: the 33 actual host mappings repaired in the successful optional-provider startup; no unsupported host was removed in this run.

Rebuild the runtime grid with `tools/bake_geology.py`. `tools/fetch_geology_sources.py` retrieves GUM's small grid without the large shapefile. Runtime generation reads only the checked-in config assets, not this research folder.

Worldgen assembly inspection scratch is under `obj/geology-inspect` and is deliberately not shipped. Implementation and source-resolution caveats are in [the geology report](../../regional-geology-implementation.md).
