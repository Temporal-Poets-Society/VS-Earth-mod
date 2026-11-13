# Regional-world research snapshot — 29 September 2026

Research for [ADR-001](../../regional-world-architecture.md). These archives were downloaded for read-only inspection; they were not installed and their DLLs were not executed. Do not distribute the archives or copied source inside an Earth release. Earth's project explicitly packages its build output and earthdata, not this folder.

## Contents

- `*-api.json`: public Vintage Story Mod DB API snapshots, including exact download URLs and advertised game versions.
- Seven `*.zip` files: MNG 2.0.10, Geology Additions 1.4.8, Advanced Geology 2.0.1, Rock Strata Variety 0.0.4, Interesting Ore Gen 2.3.8, Deciduous Trees 1.8.2, Wildcraft Trees 1.3.4.
- `archive-audit.json`: manifests, SHA-256 hashes and relevant asset paths for all seven.
- Selected `*-assets_*.json`: strata/config/leaf definitions extracted for inspection, without modifying providers.
- `advancedgeology-source-tree.json` and `ag-*.cs`: selected public source from `funions123/vs-AdvancedGeology`, commit `346df7cf3d439759d159109844aca91152af9d7c`. Source is evidence for hook assessment, not proof of binary/source equivalence.
- `vanilla-tree-blocks.json`: exact paths, hashes and block-code excerpts from five installed vanilla tree generators. Sugar, Norway and Japanese maple share the same leaf blocks; Crimson King differs.
- Follow-up Geology Additions extracts: bog-iron block, item smelting, deposit placement, mudstone crushing and mudstone/fireclay patches. These support the [Latvia and early progression pilot](../../geology-latvia-pilot.md). Installed vanilla `itemtypes/tool/pickaxe.json` was also checked read-only: copper mining tier 2, bronze tier 3.

Mod page slugs are not reliable manifest identifiers: `materialneedsgeology` resolves to `mngeology`. Game compatibility tags are declarations by publishers, not Earth runtime verification. No combined gameplay test was performed in this report-only pass.

Geographic datasets (GLiM, GUM, Macrostrat and mineral occurrence sources) were reviewed as candidates, not downloaded or incorporated into Earth. Primary-source links and limitations are recorded next to the relevant claims in ADR-001.
