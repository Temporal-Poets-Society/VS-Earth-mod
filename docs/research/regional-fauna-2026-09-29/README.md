# Fauna evidence and verification snapshot, 2026-09-29 (updated 2026-10-02 for rules version 2)

The implementation and limitations are explained in [regional-fauna-implementation.md](../../regional-fauna-implementation.md). These research artifacts are not packaged as game assets.

| File | Purpose |
| --- | --- |
| `catalogue-summary.json` | Provider versions, enabled/held counts, input SHA-256 hashes, and the URL, licence (CC0) and SHA-256 of every PHYLACINE raster used (present-natural and current). |
| `held-species.csv` | All 11 held taxa and the recorded reason. |
| `traits.csv`, `spatial-metadata.csv`, `phylacine-tree.json` | Pinned PHYLACINE 1.2.1 traits, per-species range provenance and repository tree. |
| `phylacine-release-metadata.json` | Primary Zenodo release metadata; license field is `other-open`. The pinned [v1.2.1 README](https://github.com/MegaPast2Future/PHYLACINE_1.2/blob/v1.2.1/README.md) explicitly states CC0; packaged attribution retains both release citations and underlying-source credits. |
| `ranges/*.tif` | Cached reference rasters. A cached extinct raster does not imply it is enabled or copied into runtime data. Only selected living rasters are used. Gitignored. |
| `ranges-current/*.tif` | Cached PHYLACINE 1.2.1 current-range rasters (142 files used by 157 rules) for the "current" profile, fetched 2026-10-02 from the pinned tag. Gitignored; hashes in `catalogue-summary.json`. |
| `provider-climate.json` | Representative resolved adult climate constraints from the reviewed provider entity JSON inventory; not a full live resolved-asset dump. |
| `coverage.csv` | 76 scenario points: range candidates and the provider temperature/rain/height screen at the local ground climate, extinction on/off. |
| `native-climate-scan.csv` | Global 2° grid (79°S to 81°N) plus 0.25° island grids on real terrain, including coast classification. Zero samples means unassessed, not proven missing; the feasibility test covers every enabled rule instead. |
| `coverage-before-climate-fix.csv`, `native-climate-scan-before-fix.csv` | Baseline using the actual game's fixed-reference map-temperature/rain behavior; preserved for comparison. |
| `felinae-source.json` | Local diagnostic copy of the exact malformed Felinae 0.2.14 entity definition; not a redistributed game asset. |
| `il/` | Local installed-game inspection outputs used to verify spawn/climate signatures. Diagnostic material, not Earth runtime/source implementation. |

Rebuild data with `tools/build-fauna-catalog.py --range-cache <ranges> --current-cache <ranges-current>` (offline by default; `--fetch` downloads missing pinned files). The build script requires numpy and tifffile/imagecodecs (e.g. the repository `.venv`); the game runtime has no Python dependency. The script uses the pinned PHYLACINE tag and local provider audit. Manual range decisions live in `tools/fauna-taxa.json` and `tools/fauna-birds.json`.

Run pure tests and regenerate the coverage outputs with:

```powershell
dotnet run --project tests/RegionalFaunaTests/RegionalFaunaTests.csproj -c Release -- 'C:\VS mods\Earth' --coverage
```

Current result: 950 assertions pass, including real-terrain feasibility for every enabled rule (current profile: all but the Malabar civet, whose current range is empty). Terrain tests use the Cylinder World 512000 x 204800 layout (409600-block ring), height 384, sea level 165, rivers on. Grid screening retains provider representative climate/relative-height limits but does not simulate local forest, collision, group completion or populations. Actual generated-world diagnostics remain necessary.

The local Felinae source SHA-256 is `1267FAEEBC8321705046885863C2DCDFD8A74171BE70F7269CDE96418EFAECF3`. Its `idleSoundRangeByType` has `*-adult-* = 36`, `*-baby-* = 12`; several matching lynx sound dictionaries omit `idle`. Installed VS `EntityType.CreateAndPopulate(ICoreServerAPI, ...)` directly indexes that missing sound. Earth's guarded in-memory patch narrows the ranges to adult/baby caracal keys; it does not add a sound or modify animation dictionaries.

The original Vulpes mapping bug was reproduced by the full server: matching token membership ignored that `Vulpes vulpes` requires two adjacent occurrences, and therefore overlapped other foxes. Exact contiguous token matching now fixes it. Tests cover all audited variant cross-products, as well as adult, baby and natural-carcass synthetic suffixes; a species cannot silently match two catalogue rules.

Inspecting installed VS code also showed that worldgen and runtime give each entity group/state its own attempts; no global weighted species pool needs renormalization. Provider constraints remain authoritative. Headless runs with no player cannot establish runtime population density. The detached patched-helper diagnostic validates runtime climate plumbing separately and reports its own probe counters.
