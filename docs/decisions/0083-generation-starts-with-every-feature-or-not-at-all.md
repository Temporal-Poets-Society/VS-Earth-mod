# 0083. A world's generation starts with every feature its rules use, or not at all

Status: implemented 14 November 2026 (EA-0098, the Fable pass of the 14 November code review, FP-02); decided by the
author 14 November 2026: the parts that count stay as listed, and `AcceptMissingFeatures` stays one switch in earth.json
for the whole installation.

## Context

Before this, only Earth's terrain refused a world (CR-G2, ER-08 for a failing pass). Every other part of generation
turned itself off for the session with one log line when it could not start: an unsupported rules version, a hook on
vanilla's code that a game update changed, a competing mod, a shipped data file that is missing or unreadable, a block a
removed provider mod had supplied. The chunks made in that session kept vanilla's blocks where the world's saved rules
put Earth's, for good, and met the chunks of other sessions with a seam.

## Decision

When a world's generation first makes a chunk (and again when the game starts running), Earth asks each part of
generation whether it is on. When a part the world's saved rules use is off, the server stops before that chunk is made,
with one error naming every such part and why. Nothing wrong is kept: before the game runs nothing is saved, and a chunk
asked for after it runs is noted and made again at the next start (ER-08's list). The world opens as it was once the cause
is gone (update or remove the mod, update Earth for the game version, reinstall). An owner who accepts the seams sets
`AcceptMissingFeatures` in earth.json: the world then generates without those parts, with the list as a warning. It is
one switch for every world of the installation (the author, 14 November 2026); an acceptance saved with a world, or one
naming the parts accepted (Fable's low 6 on `ad56a74`), was considered and not taken.

The parts that count, each where the world's saved rules use it:

- regional geology (and its hooks on vanilla's soil and deposit passes, and its deposit layers in rules 2), a rock block
  a provider mod supplied, ground columns, ore layers;
- Earth's ore layer (regional ores) and, in ore rules 4, its deposit catalogue;
- the springs at stream heads (springs rules 2), the water table, and the water table's aquifer data for Hydrate or
  Diedrate (water table rules 1, with Hydrate or Diedrate installed);
- plants as around 1318 (placement rules 1) and the regional plant gates;
- volcano bodies (geothermal rules 2 and later);
- Earth's climate and ocean maps;
- the rule data the world's rules read (zones, soils, wetlands, floodplains, the ice sheets' ground, ground cover, the
  people of 1318, the fauna ranges);
- Earth's guards on vanilla's generation that change blocks: the arid basins, soil by aridity (soil rules 1), the tall
  grass share (ground cover rules 1), the tree species pick (regional trees), coral salinity, the structure and village
  guards, ponds (pond rules 1), springs out of cave walls only (springs rules 1), and the story locations' placement.

Not counted: the animal range guards (living animals move and die; nothing stays in the ground), prospecting readings,
the companions' reserved-site guard (each companion decides with Earth's TerrainReservationPatchGuardReady), and a log
report. A new world whose story locations can't be placed stops instead of keeping vanilla's sites (its StoryPlaced is
not saved, so the next start tries again). A volcano whose plan throws is a failed pass of its chunk (ER-08), no longer
left out for the session.

## Consequences

- A game update that changes one of the hooks stops Earth worlds until Earth is updated (or the owner accepts the
  seams), where before they went on with vanilla's blocks in new chunks.
- Old worlds are held to the rules they were saved with; a part their rules don't use never stops them.
- Still open: a failure of a single pick (one tree, one plant patch) uses vanilla's for that pick with one line, and
  the shipped data files carry no compiled hash (the zip is trusted as built).
