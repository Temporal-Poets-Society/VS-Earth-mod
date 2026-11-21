# 0094: A chunk without Earth's terrain: name the mod, and leave other mods' own chunks alone

Status: **in Earth 2.2.0** (21 November 2026), built on `claude/ea-0162-refused` (EA-0162), merged into `claude/terrain-2.2` for the author's in-game test
(test build 2.2.0-rc.2). Date: 2026-11-18. Decided by the maintainer (D72) on
a player's report.

## Context

A player's new world stopped at once: "Earth cannot generate this world: vanilla's terrain pass made chunk 6765,3125
without Earth's terrain (a mod replaced Earth's terrain handler)". 6765,3125 is the first chunk of vanilla's 7 × 7 spawn
area in a 1:100 world (433,152 × 200,192 blocks: the map's middle chunk 6768,3128 less 3), so Earth's terrain had run for
no chunk at all. The player had no mod they knew of as world generation.

Earth's surface pass (`EarthWorldGen.BareRock`, after vanilla's block layers in the terrain pass) refuses a chunk whose
terrain writer left nothing in the handoff. With the game alone that can't happen: vanilla runs a chunk column's terrain
pass once, on one thread, from its first handler to its last (Lib `ServerSystemSupplyChunks.PopulateChunk`/`runGenerators`;
with more worldgen threads each takes one pass; a peek runs with them paused; a column resumed from the save starts after a
whole pass, so never between the writer and the surface pass; a handler that throws in the terrain pass ends it before the
surface pass). Headless worlds with Earth 2.1.4 and only its required mods made their spawn areas at 1:200, 1:100, 1:50,
1:25 and 1:10 without it. A test mod (`tests/TerrainHandlerProbe`) that replaces vanilla's terrain handler before Earth
starts gives the player's message at the same chunk, a second after vanilla's first loading line, as the player's log
shows; so does one that replaces Earth's handler after Earth starts.

So the guard was right, but its message named no mod and the player could not act on it. And its handoff was keyed by
the chunk's coordinates: a mod running the terrain pass's handlers on chunks of its own at the same coordinates (as a
far-terrain preview does) could take or overwrite the entry of the chunk the game was making, which then refused a healthy
world; a preview that skipped Earth's terrain refused it too, though nothing of a preview is saved.

## Decision

1. **Name the mod.** The refusal reads the terrain pass's handlers before the surface pass and names, by mod, type and
   method, whatever is not the game's or Earth's (`TerrainPassReport`): the handler in the terrain generator's place
   (vanilla's replaced before Earth started, Earth's replaced after), the others beside it, a wrapper with what it wraps,
   the chunk's source when it is not the game's own generation, and other mods' Harmony patches on vanilla's terrain
   generator or on Earth's code. The whole pass is listed on the line before. Where Earth's terrain runs inside vanilla's
   generator (vanilla's handler gone from the list when Earth started), the start-up warning now says so, with the list,
   as it did for a handler replaced after Earth.
2. **The handoff belongs to the chunk column**, not its coordinates: kept under the column's bottom chunk object (a weak
   table, so a cut-short pass's entry goes with its chunks), with its coordinates checked. Another generation at the same
   coordinates can no longer touch it.
3. **Another mod's own chunks are left alone.** A chunk that reaches the surface pass without Earth's terrain, from a
   request that is not the game's and with a map chunk that is not the world's (nothing of it is saved), gets no surface
   pass and no refusal; the first is logged with the mod's name and the pass's handlers. The game's own chunks, a peek's
   included, are refused as before.

## Consequences

- Saved worlds generate the same: the handoff carries the same flags and ramps (`SavedWorldDigestTests`).
- A player who meets the refusal reads which mod to remove. What to ask a player who still reports it: their
  `server-main.log` (its mod list and game version, Earth's start-up warning about its terrain, and the two lines of the
  refusal).
- What a far-terrain mod shows of Earth's world, where it skips Earth's terrain, is not Earth's; Earth does not try to
  make it so.
