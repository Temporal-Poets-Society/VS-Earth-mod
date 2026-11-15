# 0085: Start and Start coordinates on the game's Player spawn and death tab

Status: built, in Earth 2.0.3 (EA-0143). Replaces item 2 of [0084](0084-world-screen-2-0-1.md). Saved worlds are
unaffected: the keys, values and defaults are the same, only where the screen shows them changed.
Date: 2026-11-15.

## What the author asked

After Earth 2.0.2 (15 November 2026, through the coordinating session): "why still having separate GUI tab for Earth
start? told to move it under one tab of Earth". Shown both ways on screen (A: at the top of the Earth tab; B: on the
game's Player spawn and death tab), the author chose B.

## What the screen does (vanilla 1.22.7, decompiled)

- `GuiScreenWorldCustomize` puts each tab's options in one list: core mods first, then mods by their names, each mod's
  in the order it declares them. So Hydrology's Water flooding and World Map's explore by sight always come after
  Earth's options on the Earth tab.
- The list (`GuiElementContainer`) draws every option of the tab every frame inside the list's clip.
- The text field (`GuiElementTextInput`) clips to its own box, draws its text, then turns clipping off. Everything drawn
  after it then runs outside the list until something turns clipping on again.
- Of the controls the screen makes, only a slider does that: its resting text pushes and pops a clip, which restores the
  list's. It draws its bar and knob before that. A closed dropdown and a switch only draw; the descriptions' hover texts
  turn clipping off and on again with whatever box is current.
- Seen on a main-menu bench (Earth 2.0.2 with Cylinder World 1.1.1, Hydrology 0.6.2, Sea 0.6.1 and World Map 0.5.4, at
  1920×1080 and 1280×800): with Start, Start coordinates and World height at the top of the Earth tab (the smallest leak
  the Earth tab allows), the typed coordinates and World height's bar and knob floated above the list whenever the tab
  was scrolled; at 1080p, at the top edge of the screen. Everything after World height clipped right.
- No mod code runs on that screen, so Earth can't correct it.

## Decision

1. Start (`earthSpawn`) and Start coordinates (`earthSpawnCoordinates`) are in category `spawnndeath`, the game's Player
   spawn and death tab. Mods' options come after the game's there, so they are the tab's last two, below Starting
   climate, which the random start uses. No suite mod adds options to that tab.
2. That tab doesn't scroll at 1280×800 or 1920×1080, so nothing draws outside it. The Earth tab has no text field any
   more and scrolls cleanly. On a window too small for the tab (1024×600 shows six of its nine rows), typed coordinates
   whose field is scrolled out of view draw below the dialog. Nothing else does, and the field is last, so no other
   option follows it.
3. They are named **Earth · Start** and **Earth · Start coordinates** (before: Map · …), since the Earth tab's section
   names mean nothing on a game tab. Their descriptions and the five play styles' texts say where the start is. Start's
   description named the tab "Spawn & Death"; it now points to Starting climate above it. The "Earth start" tab and its
   name are gone.
4. The keys, values, defaults and how Earth reads them are unchanged. A server's world configuration names the same
   keys, and a world made before keeps its settings.
5. EarthWorldOptionsTests:
   - a text field is still the last of Earth's options on a tab of at most four;
   - Start, then Start coordinates, are Earth's only options on `spawnndeath`;
   - there is no Earth start tab;
   - Start's description and the play styles' texts name the tabs as the game does.
6. Option A (the top of the Earth tab, its order on a branch) was not merged; the branch was deleted.

The underlying fault is vanilla's: the text field turns clipping off instead of restoring the list's. Reporting it to
the game's developers is outward-facing and left to the author.
