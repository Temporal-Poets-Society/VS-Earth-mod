Local research evidence for the Earth ecology compatibility assessment, 28 September 2026.

The report is ../../ecology-integration-plan.md. The user requested a report and integration plan before implementation, with historical native ranges including extinct species.

- release-inventory.json: 28 species-content pack releases retrieved from the public Vintage Story Mod DB API. This is a dated snapshot, not an update lock maintained automatically.
- archive-audit.json: manifest information, Biomes metadata paths, entity paths and SHA-256 of the 28 inspected archives.
- entity-spawn-audit.json: parsed code/variant/spawn fields from all 151 entity JSON files, including non-animal entities. These are raw definitions, not the game's fully resolved variant registry.
- parse-errors.json: empty after using the JSON parser's dictionary mode to support empty property names in valid provider assets.
- dll-audit.json: checksums of the six FloralZonesBerryBushes.dll entries; all match. DLLs were not loaded or executed.
- *-api.json: public source metadata and descriptions. The failed dinornithiformes lookup is retained as retrieval history; the correct pack ID is dinornithidae, which is used by the inventory.
- biomes-commit.json: source revision used for the code review. Source files are local inspection copies, with biomes-LICENSE retained alongside them. Biomes API metadata reports 2.3.1 at the time of retrieval.
- *.zip: unmodified third-party release archives for local inspection. These were NOT installed into a game or test server, and must not be included in the Earth release or shared as an Earth content bundle.
- Other extracted JSON files are targeted evidence for the flora, mangrove, rhino and aquatic-spawning findings.

Primary endpoints: https://mods.vintagestory.at/api/mod/{modid} and https://github.com/spooq/biomes. Download URLs came from the corresponding release metadata. No executable mod content was run.

Validation performed: all 28 inventory IDs are unique and have matching inspected archive manifests; all 28 include Biomes metadata; all 151 entity files parsed; six shared DLL hashes match. No combined game startup, natural spawn measurement, AI test or performance benchmark was performed.

Superseded as the catalogue's input on 17 November 2026 by [fauna-packs-2026-11-17](../fauna-packs-2026-11-17/README.md) (EA-0156: the packs' newest 1.22 releases, and Moschidae); kept as the first review's record.
