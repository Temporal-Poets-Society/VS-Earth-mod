# 0097: Vanilla's deposits at their depth below the local ground (deposit depth rules 1)

Status: **in Earth 2.2.0** (21 November 2026), built on `claude/vanilla-ore-depths`, merged into `main` (18 November 2026); new Earth worlds
with vanilla ores. Decided by: Claude as maintainer (D72), as step 1 of the author's order for rocks, ground
and ores (EA-0170). Not seen in a graphical client.

## What the author asked

On the audit of 18 November ([rocks, ground and ores](../research/rocks-ground-ores-audit-2026-11-18.md)): "even if
vanilla ore spawn is selected and vanilla rocks then highlands end without ores? 1st we need to fix vanilla settings to
work correct" ([proposal](../proposals/2026-11-18-ores-next-model.md), section 7, step 1).

## The problem

Vanilla places each deposit by a height rule made for its own terrain, where land stands a little above sea level:

| Generator | Height | Deposits |
|---|---|---|
| `disc-followsealevel` | a share of the sea level | all iron (limonite, hematite, magnetite), bituminous coal, anthracite, sulfur, borax, saltpeter, chromite, ilmenite, cinnabar, rhodochrosite, emerald |
| `disc-followsurface` | a share of the column's ground height | deep native copper, malachite, cassiterite, galena, sphalerite, lignite, lapis, pentlandite, diamond, salt domes, the surface starters |
| `disc-anywhere` | a share of the world height | quartz (with gold and silver), olivine |
| `disc-followsurfacebelow` | a depth below the ground | clay, peat, salt flats, all of Earth's own deposits |

Earth keeps the sea level at 110 at every world height (decisions 0031, 0045) and has real plateaus. On ground above
about 1,450 m (10 % of ice-free land; 20 % lies above 1,000 m) the sea-level deposits lay 140-640 blocks down at 384 and
512 high, and at 1:10 the rock floor removed them (the 3 November measurement: none left in the Alps, Ethiopia, Tibet or
the Atacama); the surface-following ones went deeper the higher the ground (copper 130-215 blocks under the plateaus).
And vanilla multiplies a deposit's attempts by world height / 256 because its bands grow with the world; Earth's
sea-level band does not, so lowland ore thickened with height (1.3-1.6 times per column from 512 to 1024 high, the Dead
Sea's salt 8.6 times). Both hold under every rock choice.

## Decision

New Earth worlds with vanilla ores save `EarthParams.DepositDepthRules` = 1 (field 218). In them
(`EarthDepositDepths`, `DepositDepth`):

1. **Every vanilla disc deposit lies as deep below its column's ground as below a column at sea level in vanilla's
   standard world** (256 high at sea level 110; for another sea level, the vanilla world with that sea level):
   - sea-level: vanilla's height lifted by (ground − sea level);
   - surface-following: its share taken of a standard column (the sea level) instead of the local ground height, then
     lifted;
   - anywhere: its share of the standard world's height instead of this world's, then lifted;
   - surface-below: unchanged (already a depth below the ground);
   - vanilla's distortion is kept, and a height below the standard world's bottom stays there, so vanilla skips it here
     as there (saltpeter's band reaches below y 0).
   A column at sea level keeps vanilla's heights exactly; a plateau gets the ore of a lowland within the same reach.
2. **Attempts without the world-height factor.** Each height-scaled variant gets its standard world's attempts (times
   sea level / 110 where that is not 110: its bands are that much thicker); per-area variants (the surface starters,
   Earth's own) keep theirs. Applied at world start, after vanilla's world-generator init and after `/wgen regen`.
3. **The prospecting pick reads the same.** The generators' height ranges (`GetYMinMax`, which the pick's host count and
   child deposits use) are moved the same way; a reading's amount follows the attempts.
4. **Saved per world** (decision 0002): worlds made before keep vanilla's heights and attempts; Earth's ore maps place
   their own (field 0). The patches are a `PatchVanilla` table (`DepositDepthsTable`), checked against the game by
   `PatchVanillaTests`; a world with the rules refuses to generate without them (decision 0083).

## Alternatives considered

- **Lift by a smoothed regional surface** (as regional geology's reference surface). More geological (deposits deeper
  under ridges than under valleys), but in mountains it leaves ore far under the ridges again, and the point of this step
  is vanilla's depths within reach everywhere.
- **Only keep deposits above the rock floor.** Keeps them unreachable at 140-640 blocks under plateaus at 384 and 512 high.
- **Rewrite vanilla's height factor in `GenDeposits.GeneratePartial` with a transpiler.** Also reaches the pick's private
  generator, but a transpiler breaks more easily on a game update than a field set on each variant.
- **Attempts by each chunk's own rock thickness** (the audit's first suggestion). With the bands now the standard world's,
  the standard world's attempts are what keeps ore per block of rock; a thickness factor would add ore under mountains
  that the bands never reach.

## Consequences

- Plateau ore within reach: in the census (dedicated servers, 14 places, [deposit-depths-2026-11-18](../research/deposit-depths-2026-11-18/))
  copper, tin and lead under the plateaus moved from 80-250 to 25-90 blocks down; sulfur, borax, chromite, coal and salt
  domes appear on plateaus where 2.1.4 had none.
- Lowland ore at 1:10 is about a quarter of 2.1.4's, at 1:100 about two thirds: vanilla's standard world's ore per
  column. A server that wants more raises the game's own deposit rate (`globalDepositSpawnRate`), which multiplies
  everything as in vanilla.
- Iron still needs its host rock within reach. With vanilla rocks on Earth's terrain, the top 120 blocks often hold one
  or two rock types (vanilla's layers grow with world height), so at 1:10 iron appeared at 2 of 9 highland places. That
  is EA-0181 (vanilla's rock layers at the standard world's thickness), a separate step because it changes how rock
  layers look.

## Not covered

- The ground is the terrain height map's: under glacier ice the depth counts from the ice's top.
- The pick's own simulated deposit pass (`ProPickWorkSpace`'s private `GenDeposits`) keeps vanilla's attempts; that only
  changes how many of its simulated column's blocks are ore rather than host rock.
- Worlds with ground columns or regional rock and vanilla ores were not in the census (the rules apply there too).

## Tests

`PatchVanillaTests`: the table binds on the installed game (1.22.7) without a line; the arithmetic (a sea-level column
keeps vanilla's heights; plateaus lift; the distortion is kept; heights below the bottom stay out; the standard world of
another sea level) and the attempts (height-scaled variants lose the factor once, per-area ones keep theirs, a second
pass changes nothing, sea level 160). `EarthWorldOptionsTests`: each rock choice with vanilla ores saves rules 1, with the
ore maps 0; the field is in the pinned list; the 295 pinned new worlds differ only by it (checked by forcing it to 0).
The self-test reports "deposit depths: PASS; rules 1, active, 0 deposit(s) still height-scaled".
