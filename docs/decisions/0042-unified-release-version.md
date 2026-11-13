# 0042: One release version for all three Earth parts

Date: 31 October 2026. Status: agreed; 1.0.0 built and verified. Not published or installed by this version-update task.

## Author decision

The author asks to bump Earth pre.16 to **1.0.0**, matching the two data packs, because the three downloads form one mod and should continue using the same version. This selects the publication version; it does not change the ongoing development roadmap.

## Implementation

- Main `modinfo.json`: `0.17.0-pre.16` → `1.0.0`. Its required `earthterraindata` and `earthwaterdata` versions already equal 1.0.0.
- The release packager refuses any build whose main version differs from either required data-pack version. Future releases update the main version and both data dependency versions together, even when the numeric data itself is unchanged.
- The existing Terrain/Water 1.0.0 archives are reused byte-for-byte. The 1.0.0 main archive differs from pre.16 only in `EarthMap.dll`, `EarthMap.pdb` and `modinfo.json`; the sole manifest difference is its version. No runtime source, terrain, ecology, climate or world settings change. Assembly binding identity remains 1.0.0.0.
- Earth’s Markdown and HTML description now explain the single mod's three required downloads and link both data-pack pages.
- Earlier pre.16/pre.15 archives are preserved. The author's currently installed and accepted pre.16 package is unchanged; this task prepares the matching publication labels.

## Verification

Release build: zero warnings/errors. All three ZIP manifests declare **1.0.0** and each archive is below 40,000,000 bytes:

| Archive | Bytes |
| --- | ---: |
| `earthmap-1.0.0.zip` | 28,490,075 (after the manifest-only upload correction in 0043) |
| `earthterraindata-1.0.0.zip` | 36,424,452 |
| `earthwaterdata-1.0.0.zip` | 31,583,561 |

`EarthWorldOptionsTests`: **507 checks passed** against the versioned packages, including data hashes, deferred loading and representative terrain/climate results. `RequiredModsTests`: **35 checks passed**. A failure-path check with main version 1.0.1 and stale 1.0.0 data dependencies is rejected before any archives are written. Archive CRCs and complete payload comparisons pass. Gameplay acceptance comes from the author's installed pre.16 test; no new 1.0.0 client or native-server run is claimed for this metadata-only bump.

[Package verification](../../../../../docs/publication-packages-1.0.0.json). Source: `modinfo.json`, `tools/package-publication.py`, `README.md`; current publication text: `Earth/docs/moddb-page.md` and `Earth/docs/moddb-description.html` in the main checkout.

Upload follow-up: [0043](0043-moddb-manifest-compatibility.md) removes the empty `incompatibilities` field rejected by Mod DB. Compared with pre.16, the current 1.0.0 manifest therefore changes its version and removes that unused property. Both data parts retain the same version and bytes.
