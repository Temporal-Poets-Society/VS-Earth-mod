# 0104: Ground columns and Earth's ore maps off the world creation screen for 2.2; experimental switches in earth.json

Status: **in Earth 2.2.0** (21 November 2026), built on `claude/terrain-2.2` (EA-0197) for the author's in-game test (test build 2.2.0-rc.11, 20 November
2026; Ores back with one value after it, in test build 2.2.0-rc.12). Date: 2026-11-20. Decided by the
author: "for RC 10 we need to disable Ground columns and real ore spawns from the GUI. They need work to do"; "Keep
regional"; on rc.11's Earth tab: "when leaving vanilla ore spawns - i thought leave the config but having just one
choice". How it is built by Claude as maintainer (D72).

## Context

The Earth tab offered, up to 2.2.0-rc.10 ([0056](0056-rocks-and-ground-apart-from-ores.md),
[0084](0084-world-screen-2-0-1.md)):
- **Terrain · Rocks and ground** (`earthRocks`): vanilla (the default), ground columns, or regional rock regions only;
- **Terrain · Ground depth** (`earthGroundDepth`), used only by ground columns;
- **Terrain · Ores** (`earthOreDeposits`): vanilla deposits (the default) or "Earth ore maps (experimental)".

Ground columns and the ore maps need more work before players should choose them: ground columns 3 (the plan agreed on
Earth main) and the next ore model (EA-0177).

Two paths could also switch them on without the screen:
- earth.json's `RegionalOres` is true in every earth.json Earth has written. A world config without the Ores key took
  the ore maps from it, and so did `UpgradeExistingWorldOreRules`.
- `GroundColumnsPrototype` gave ground columns to a world without a rock key.

## Decision

1. **On the screen:** Rocks and ground offers vanilla (the default) and regional rock regions only. Ground depth is gone
   (`worldconfig.json`, its English texts). Ores stays with one value, `vanilla`: the game's deposits below the real
   ground ([0097](0097-vanilla-deposits-below-the-local-ground.md)) plus Earth's gems and added ores, with its old label
   and the description of that value; the play styles set it as before. Every new world gets them, as the default did.
   (rc.11 had taken Ores off the screen; the author wanted the choice kept with that one value.)
2. **No other way in:** a world config that still says `groundcolumns` or `maps` gives its rock choice only. That
   includes an older template, `/worldconfig`, a dedicated server's world configuration, and the earlier single key
   `earthOres`. So `groundcolumns` gives regional rock without ground columns, and the Ores key is not read at all: a
   world gets the game's deposits whatever it says (`EarthWorldOptions.ReadTab`). Ground depth is removed rather than
   hidden (`onCustomizeScreen` false), like the ground columns choice; Ores' one value is in every new world's config,
   since the game writes every attribute's default into a new world, which is why the key can't tell a choice.
3. **Experimental switches for development**, in `ModConfig/earth.json`, server side, off by default:
   - `ExperimentalGroundColumns`: a new world whose rock is regional (the tab's "Regional rock regions only", or
     `groundcolumns` in a world config) gets ground columns, with the world config's `earthGroundDepth` or the default
     curve.
   - `ExperimentalOreMaps`: every new world gets Earth's ore maps (its Ores value, always `vanilla`, isn't read: turn the
     switch off for a world with the game's deposits).
   - When either is on, the server log says so in a warning for each new world, with what that world gets.
   - They replace the old flags. `GroundColumnsPrototype` is no longer read (a true value is noted in the log).
     `RegionalOres` only decides what `UpgradeExistingWorldOreRules` does with an existing world's ore maps (keep or turn
     off); turning them on in a world without them takes `ExperimentalOreMaps`, and without it the upgrade says so.
     A stale value in a player's earth.json therefore never turns either on.
4. **Saves:** worlds that saved ground columns or ore maps in `earth-params` keep them exactly; the 12 saved worlds are
   unchanged.
5. **The code stays.** Ground columns, their ground data download, the ore maps and their crafting rules are untouched,
   for the ore session's branches and for worlds that have them.

## Consequences

- The new-world pins change only where a world config asks for ground columns or the ore maps, or has no Ores key (30
  of 320 cases: `RegionalOres`, `OreRulesVersion`, `CraftingRules` and `DepositDepthRules`, and in the 10 ground-column
  cases `GroundColumns`, `GroundDepth`, `OreLayers` and `OreRules`). Every play style's own world is as in rc.10.
- In singleplayer the switches can't pick ground columns apart from regional rock: with `ExperimentalGroundColumns` on,
  "Regional rock regions only" gives ground columns. That is acceptable for a development switch, and the log says it.
- The ground data (`egts-v1`, about 172 MB) is fetched only for worlds with ground columns, so a 2.2 player doesn't fetch it.

## History

- 2026-11-20: built for 2.2.0-rc.11 (EA-0197).
- 2026-11-20: Ores back on the screen with one value, vanilla (the author); `ExperimentalOreMaps` alone gives the ore
  maps. On `claude/terrain-2.2`, in test build 2.2.0-rc.12.
