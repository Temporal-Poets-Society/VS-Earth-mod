# 0058: Crafted stone of Earth's rocks from vanilla's templates, recoloured in the game

Status: implemented in source (`claude/ground-columns`, commit ea95f13); offline tests pass; seen in game on the
AgentBridge bench on 7 November (Earth as a folder mod at 1:200: the client logged 55 recoloured textures for each of
the five rocks, gneiss and schist from granite, quartzite from limestone, gypsum and dolostone from chalk; placed
cobble, ashlar and polished walls look like vanilla's beside granite, limestone and chalk, no seams; no errors); merged into release/1.0 at 35eea55 with the author's "Yes, merge" (7 November).
Date: 2026-11-07. (0057 is the tall line's geothermal sites.)

## What the author asked

"for block textures - look how vanilla is done and try to match implementation, especially on cobble and ashlar
blocks". Earlier the same day: "Look at textures in with several blocks next to each other as some i saw was not nice
due to visible repeating patterns."

## How vanilla does it (1.22.7 assets)

- Cobblestone (with its slab, stairs, fanned and skull forms), brick (ashlar), aged and cracked brick, drystone and its
  fence, polished rock and its slab, the old polished rock sets and wavy sand are one greyscale template per block type,
  recoloured for each rock. Granite's and basalt's cobblestone correlate 1.00; a per-luminance colour lookup rebuilds
  each rock's file within 0.6-2.3 of 255 against a spread of 17-38.
- Rock, gravel, plain sand and termite mounds are drawn per rock.

## Decision

- Earth's own rocks (gneiss, schist, quartzite, gypsum, dolostone) take vanilla's template for each of those block
  types, moved into the Earth rock's palette. The client does it when assets load (`EarthStoneTextures`, before block
  textures are collected): vanilla's files are read from the player's game, so Earth ships no vanilla pixels.
- The template comes from the vanilla rock nearest in colour (mean luma and chroma of rock 1-4, chroma weighted 1.5):
  gneiss and schist from granite, quartzite from limestone, gypsum and dolostone from chalk in the prototype.
- Recolour (`Core/StoneRecolour`): luma shifted to the Earth rock's mean with the spread scaled by the rocks' spread
  ratio (0.85-1.2); chroma rotated to the Earth rock's hue and scaled by the saturation ratio (0.4-2.5), or shifted
  when the reference is nearly grey. Wavy sand uses the sand palettes.
- Only textures that are Earth's own are replaced (origin inside Earth's folder or zip), so a rock supplied by
  Geology Additions or Advanced Geology keeps that mod's textures. Earth's hand-drawn textures stay in the archive as
  the fallback when a template is missing or the step fails (logged as a warning).
- Rock, gravel and sand stay Earth's own drawings, redrawn the same day for tiling (fine grain at low contrast, the
  same mean and contrast in every alternate, small features inside the tile; commit 070c3ae).

## Not done

- Checked only with Earth as a folder mod; the zip install uses the same origin check (zip path against the mod's
  source path).
