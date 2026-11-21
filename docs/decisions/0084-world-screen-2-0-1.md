# 0084: The world screen in 2.0.1: vanilla rocks by default, the start options' own tab kept, rain and storm frequency off the screen, one canals switch, coordinates from Google Maps

Status: built, in Earth 2.0.1 (EA-0134; items 4 and 5 EA-0136). New worlds only: saved worlds keep the choices their params froze.
Item 2 is replaced by [0085](0085-start-on-the-spawn-tab.md) (2.0.3): Start and Start coordinates are on the game's
Player spawn and death tab.
Item 1's dropdown loses ground columns in 2.2, and Ground depth and Ores leave the screen
([0104](0104-ground-columns-and-ore-maps-off-the-screen.md)).
Date: 2026-11-15.

## What the author asked

After looking at the world creation screen with Earth 2.0.0 (15 November 2026), through the coordinating session:

1. "Rocks and ground": vanilla becomes the default.
2. Move the Earth start tab's options (Start, Start coordinates) back onto the Earth tab, unless the game forces a tab of
   their own; if it does, record why.
3. Take "Weather · Rain frequency" and "Weather · Storm frequency" off the screen, keeping them for admins.

How each is built was left to the maintainer (D72).

## Decision

1. **Vanilla rocks by default.** `earthRocks`' default and all five play styles are `vanilla` (the dropdown lists it
   first, like Ores). The other choices are unchanged. What follows from it, checked:
   - Ground depth applies only with ground columns (the builder stores none otherwise); its text already says so.
   - The rock floor doesn't depend on the rock choice (the terrain pass lays it under any rock).
   - Ores stay "Vanilla ore deposits" by default.
   - Hydrate or Diedrate keeps its own defaults with vanilla rock ([0059](0059-water-table-and-hod-aquifers.md)), so a
     default new world no longer gets Earth's aquifers; a world with ground columns or regional rocks still does.
   - New-world params: no rules or params change. The rock choice is already its own saved field (RegionalGeology,
     GroundColumns, GroundDepth), so a new world simply makes another choice. The 280 pinned new worlds of
     EarthWorldOptionsTests equal 2.0.0's code with only the play styles' rock choice switched to vanilla (235 of them
     change; those that choose their rocks themselves don't). A world config without the key still takes earth.json's
     `RegionalGeology`, as before (no world made from the menu lacks it).
   - What a default world's params lose with it, as any vanilla-rock world always has: the geology rules, ground columns
     and their depth, Earth's ore layers and Earth's crafting rules for its own rocks (grog, fire clay, refractory bricks
     without bauxite). A real 2.0.1 world of the 1:100 play style agrees with the builder's params in every field the
     builder writes (118; the 13 others are the seed's and those later systems fill in). 2.0.1's pins
     (SavedWorldDigestTests `2.0.1`) equal 2.0.0's in all 77 hashes: the digest covers the terrain model's columns, which
     the rock choice doesn't touch, so they keep the release's record but say nothing about the rocks; a default world's
     rocks were seen in the 2.0.1 self test and bench start (EA-0134).
2. **The start options keep their own tab** (replaced by [0085](0085-start-on-the-spawn-tab.md) in 2.0.3). The game forces it. Its text field (`GuiElementTextInput`, decompiled
   1.22.7) sets a scissor to its own box, draws its text and caret, then turns scissoring off, and the option list
   (`GuiElementContainer`) draws every option of the tab each frame in order. On the scrolling Earth tab (twenty
   options), everything after the field would draw over the window's edges (what the author saw before 2.0.0-rc.6),
   and even as the tab's last option the field would draw its own text and caret outside the list whenever it is
   scrolled out of view. No mod code runs on that screen to correct it. Putting Start alone on the Earth tab would part
   its "Coordinates below" choice from the field it names. So Start and Start coordinates stay together on the short
   Earth start tab, which never scrolls; EarthWorldOptionsTests keeps any text field last on a tab of at most four.
3. **Rain and storm frequency for admins only.** Both attributes have `onCustomizeScreen: false`, like `earthElevation`.
   The game writes every attribute's default into a new world's config (`WorldConfig.allDefaultValues`), so a hidden
   "1" would always reach the world and earth.json could never apply. They are therefore strings with an empty default,
   and no play style sets them: an empty value takes earth.json's `RainFrequency` and `StormFrequency` (1 by default),
   and a world config's own value (`0` to `3`, `0` to `5`, set in a dedicated server's world configuration before the
   world is made, as a string: the game drops a bare JSON number for a string attribute) wins. The dropdowns' value
   names are gone from the language file. A world made with 2.0.0 now lists the two frequencies in the world list's
   hover text (the game shows what differs from the play style); cosmetic, left as it is.

4. **One canals switch** (the author: "panama and Suez can be one setting"). `earthCanals` (bool, off, on the Earth tab)
   replaces `earthSuezCanal` and `earthPanamaCanal` on the screen and in the play styles, stored in EarthParams.Canals as
   before. On: both canals. Off: the two old keys, each on its own (`EarthWorldOptions.CanalCodes`). The old keys stay
   declared, off the screen and off by default (Fable's re-review of `ee0110f`, F1): the game copies only declared
   options from a server's world configuration into a new world (`SaveGame.SetNewWorldConfig`), so undeclared they would
   be dropped without a word; and its Modify world screen writes a missing option's default into an older world, so a
   presence test on the new key would hide an older world's own keys. An older world keeps exactly its two keys (the game
   adds no option to a saved world's configuration on load), which `UpgradeExistingWorldCanals` reads as before. Saved
   params are untouched; EarthWorldOptionsTests' 280 pinned new worlds are unchanged ("both canals" now by the new key,
   the same params), with 15 new cases: both, Suez only and Panama only by the old keys with the new switch off.
5. **Start coordinates from Google Maps** (the author: "make custom starting coordinates format work with copy paste from
   Google maps coordinates"). `Core.Coordinates` reads the decimal pair Google shows for a clicked place (and the plain
   form from before), degrees, minutes and seconds with hemispheres before or after and the quote characters Google,
   browsers and word processors use, degrees with decimal minutes, and a Maps link: the place's pin (`!3d…!4d…`) where the
   link has one, since that is the place picked, else the map's centre (`@lat,lon`), else `q=`, `query=`, `ll=`,
   `center=`. Latitude within ±90, longitude within ±180 (before, any longitude was taken and wrapped). A short share link
   holds no coordinates and is refused with that reason. What it can't read still gives a random place at the starting
   climate, but no longer silently: the reason and the formats that work go to the server log and, in chat, to the
   host or the server's admins (`controlserver`) who join in the session that makes the world (no mod code runs on the
   creation screen to refuse it there). After Fable's re-review: a directions link is refused (its `@` is the map's
   centre, neither end), seconds written as two typographic single quotes are read, and the two coordinates may touch
   after a hemisphere letter or a seconds mark.
   EarthWorldOptionsTests (CoordinateChecks) reads real samples in both hemispheres and signs, at 0 and ±180, and checks
   the refusals' reasons. The field stays on the Earth start tab (item 2).

## Not changed

Cylinder World's Seam mist and Sea's Storm wave height, which the author also wants hidden, are those mods' own options.
