# Vintage Story bug: coral reefs leave salt water in the solid block layer

Status: **already reported upstream**, open: https://github.com/anegostudios/VintageStory-Issues/issues/8246
("Missing water blocks generating in coral reefs", LadyWYT, 18 Feb 2026, game 1.22.0-pre2; confirmed in 1.22.0-pre.4 by
taArden on 5 Mar 2026). Labels `department: code`, `status: new`. Neither report names the cause.

Found by us on 2026-10-21 on the Great Bahama Bank in an Earth world (game 1.22.7). Not critical. Earth does not work
around it (decision: no fix in Earth for now, and no repair of chunks already generated). Plan: add the comment below to
#8246 (still present in 1.22.7, plus the cause). **Not posted yet.**

If it ever needs a workaround in Earth: a transpiler on `BlockCoral.TryPlaceBlockForWorldGenUnderwater` that turns its
`SetBlock(saltwater.BlockId, position)` into "clear the solid layer, put salt water in the fluid layer". Only new
chunks would change.

---

## Comment for #8246

```markdown
Still happens in 1.22.7 (Stable). Screenshots attached: the pockets are 1 block wide, as tall as the kelp/seagrass
stalks around them, with sloped water-surface tops.

I think I found the cause in the decompiled code:

`BlockCoral.TryPlaceBlockForWorldGenUnderwater` clears existing water plants (`CoralReplaceOtherPatches`, 0.65 in
`worldgen/blockpatches/aquatic-coral.json`) with

    blockAccessor.SetBlock(saltwater.BlockId, position);

During the vegetation pass, `GenVegetationAndPatches` gets its accessor with
`chunkProvider.GetBlockAccessor(updateHeightmap: true)`, i.e. `BlockAccessorWorldGenUpdateHeightmap`. Its
`SetBlock(int, BlockPos, ItemStack)` override calls `SetSolidBlock` directly and doesn't check `block.ForFluidsLayer`
(the base `BlockAccessorWorldGen.SetBlock` does, and sends liquids to `SetFluidBlock`). So `saltwater-still-7` ends up
in the solid layer where the kelp/seagrass stalk was. That's the "glitched water": rendered as a separate liquid block
and not real fluid-layer water, which would explain no oxygen.

Possible fixes:
- `BlockAccessorWorldGenUpdateHeightmap.SetBlock`: route `ForFluidsLayer` blocks to `SetFluidBlock` like the base class
  (this would also cover any other worldgen code that places liquids during the vegetation pass), or
- in `BlockCoral`: `SetBlock(0, position, BlockLayersAccess.Solid)` plus
  `SetBlock(saltwater.BlockId, position, BlockLayersAccess.Fluid)`.

Minor, same loop: the inner `do ... while` that walks down the stalk doesn't advance `i`, so the depth passed to
`PlaceCoral` doesn't count the stalk.
```
