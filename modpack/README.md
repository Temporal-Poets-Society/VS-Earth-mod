# Earth Regional Trees pack

For Vintage Story **1.22.7**, Windows. Earth Map and Cylinder World (both included in
this archive), eight Floral Zones packs, Wildcraft: Trees and Shrubs, and Herbarium.
The Earth and Cylinder World versions are recorded in `pack-lock.json`.

1. Extract this pack into its own folder.
2. Run **Setup-Earth.cmd**. It installs the two included mods and downloads the ten
   external mods from the official Mod DB CDN, checks their exact versions and SHA-256
   hashes, and creates `EarthProfile`.
3. Run **Start-Earth.cmd** and create a **new world**. World height 384 is recommended.
   Use `/earth trees` to inspect the botanical region and native candidates.

The normal Vintage Story profile is separate and is never modified. Saves/configurations
in this Earth profile survive repeated setup runs. No automatic mod updates are performed.
Keep the game installation's own `Mods` directory for the built-in game mods.

Setup can be run again at any time. Every archive is verified before use and moved into
place under its final name only after verification, so an interrupted setup leaves no
half-copied mod. A damaged download in `Downloads` is deleted and downloaded again; a
damaged or truncated archive inside the profile is replaced. If a download keeps failing,
the message names the mod; nothing unverified is installed and the profile is not marked
ready.

If the game is installed somewhere else:

```powershell
.\Start-Earth.ps1 -GamePath 'D:\Games\Vintagestory'
```

For a custom profile, supply the same `-ProfilePath` to both PowerShell scripts.
The installer also accepts `-CachePath` and `-Offline` for pre-downloaded archives;
filenames must match `pack-lock.json`.

Trees and tree-generator shrubs use native botanical-country ranges plus climate,
elevation and water checks; where none of the installed species is native, designated
regional stand-ins ("analogues") for the biome are used, and only as a last resort the
earlier climate-only pick. Ground plants are handled by a separate Earth ground-plant
integration; block-patch bushes, crops, decorative schematics and fauna follow their own
rules. Botanical countries are a first approximation, not precise vegetation boundaries
inside a state. Generic wood types sometimes stand in for several regional species.
Player planting and existing chunks are unchanged. Autumn colouring remains supplied by
VS/the mods.

Compatibility on VS 1.22.7: Wildcraft 1.3.4 logs **50 unresolved wood-trellis recipes**
(an upstream compatibility gap). A full client visual/crafting playtest of this release
has not been completed. See the Earth repository's `docs/regional-trees-implementation.md`.

Earth has no hard dependency on the ten content mods: they are optional providers.
The pack is a versioned installation recipe, so a later pack can replace a provider
without rewriting Earth's geographic engine. Removing a provider from an existing save
still removes its blocks/items; provider changes belong in a planned pack update.

For multiplayer, the server operator installs the same set. Vintage Story downloads
required server mods for joining players into a server-specific folder. The server
must make the matching Earth and Cylinder World archives available too.

External mods retain their authors' licenses. This archive contains Earth's own release,
Cylinder World and setup scripts; it does not redistribute Floral Zones/Wildcraft/Herbarium.
Download sources, versions, attribution links and hashes are in `pack-lock.json`.

## For maintainers

`pack-lock.json` must be regenerated for every Earth release: build Earth in Release mode
(`release/earthmap-<version>.zip`), then run `tools/build-regional-tree-pack.ps1`
(optionally `-CylinderWorld <zip or folder>`). It records the bundled archives' versions and
hashes in the lock and writes `release/Earth-Regional-Trees-<version>.zip`. The checked-in
lock carries a placeholder hash for Earth and is refused by the installer until then.
Commit the regenerated lock with the release. A profile created from one lock refuses a
different lock; players start a new profile folder for a new pack release.
