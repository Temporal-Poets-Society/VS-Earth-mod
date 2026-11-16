# 0089: World options the Mod DB reads

Status: **built, in Earth 2.1.2**. Date: 2026-11-16. Decided by the coordinator as maintainer (D72), after the Mod DB
refused the 2.1.1 file on the author's upload.

## Context

The Mod DB reads a mod's `modinfo.json` and `worldconfig.json` with Anego's modpeek (`LibModPeek/ParseJSON.cs` and
`Validate.cs`) and refuses the file on any error. The game reads more than modpeek allows. Earth 2.1.1's World height
attribute (`earthWorldHeight`) carried two keys the game uses, `skipValues` (`"64...192"`) and `alarm` (512), and the
upload failed twice with "Unexpected property 'worldConfigAttributes' on WorldConfigAttributes[1]" (modpeek's message
names the parent property, not the key).

modpeek's rules: an attribute may hold only `dataType` (a name of EnumDataType), `category`, `code` (both non-empty),
`min`, `max`, `step` (numbers), `onCustomizeScreen`, `onlyDuringWorldCreate` (booleans), `default` (a string), `values` and
`names` (string arrays of the same length); the file's root only `$schema`, `playStyles` and `worldConfigAttributes`; a
play style only `code`, `playListCode`, `langCode`, `listOrder`, `mods`, `worldType`, `worldConfig` (the four names
non-empty, valid mod ids); `modinfo.json` only its listed keys, its version and dependency versions matching
`^\d{1,5}\.\d{1,4}\.\d{1,4}(?:-(?:rc|pre|dev)\.\d{1,4})?$` (or empty or `*` for a dependency). Keys are compared
without case.

## Decision

1. **World height** is an intRange of 256 to 1024 in steps of 64, 384 by default, set only when the world is made, with no
   `skipValues`, `alarm`, `values` or `names`. Without skipValues the slider would offer 128 and 192, which
   `EarthWorldOptions.Height` refuses (the world falls back to the World generation tab's height, logged only), so the
   slider starts at 256. Its old 0 ("World Height at the top") goes: the vanilla slider stops at 512 and below 256 Earth
   refuses anyway. The code is unchanged: `Height` still takes 0 and the old Automatic (64) from a config or
   `/worldconfig`; saved worlds keep their stored height. The description says the 512 warning the `alarm` showed
   ("Taller than 512: generation is slower and saves are larger. Keep Rock floor on Automatic."); the unused
   `worldconfig-earthWorldHeight-0` and `worldattribute-earthWorldHeight-warning` lines go.
2. **A check that it can't come back:** `tests/EarthWorldOptionsTests/ModDbChecks.cs` applies modpeek's rules to the
   repository's `modinfo.json` and `worldconfig.json` and, when one is built, to the release zip's, with two negative
   controls (2.1.1's two keys, a version outside the pattern).

## Consequences

- New worlds' terrain is 2.1.1's (`SavedWorldDigestTests` 2.1.2 pins the same hashes); only the world screen changed.
- The slider no longer shows the warning line above 512; the description carries it.
