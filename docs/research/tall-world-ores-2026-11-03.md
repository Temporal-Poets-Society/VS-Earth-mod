# Ores in tall worlds (1:10, height 1024)

Date: 3 November 2026. Question (the author): "does new heights work correct with ores? before we had problem with
saltpeter spawing on ground due to sea level offset" (decision [0045](../decisions/0045-sealevel-deposits.md)).

## Method

Four isolated dedicated servers (VS 1.22.7), one after another, Earth 1.0.3-tall.2 as installed, data packs 1.0.2,
Cylinder World 1.0.4, Hydrology 0.6.2-rivers.7 and the test-only ore audit
([ore-ingame-validation-2026-10-15](ore-ingame-validation-2026-10-15/oreaudit), with deeper depth bins and the ground
range per site: `tall-world-ores-2026-11-03/oreaudit-tall.patch`). World 5,120,000 × 2,048,000, seed 12345, spawn Vienna,
World generation tab 256, set by the Earth tab:

| Run | Earth height | Sea level, curve | Rock floor | Ores |
| --- | ---: | --- | --- | --- |
| `floor` | 1024 | 160, linear | Automatic (160) | regional |
| `nofloor` | 1024 | 160, linear | None | regional |
| `vanilla` | 1024 | 160, linear | Automatic (160) | vanilla |
| `std512` | 512 | 110, log1000 (the play style's defaults) | Automatic (none at 512) | regional |

The audit counted every deposit block in 5 × 5 chunk columns around eight sites: Vienna (48.17° N 16.9° E), the
Pannonian plain (47.2° N 19.8° E), the Alps (46.45° N 8.1° E), Zermatt (46.02° N 7.75° E), the Ethiopian highlands
(9° N 38.7° E), Tibet (33° N 88° E), the Atacama (23° S 69.5° W) and the Dead Sea's west shore (31.45° N 35.4° E). The
same seed and positions give the same deposits in `floor` and `nofloor` above the floor. Scripts and raw results are in
[tall-world-ores-2026-11-03](tall-world-ores-2026-11-03) (`setup.py`, `run.sh`, `sites.json`, `analyze.py`,
`results/*.jsonl`); `run.sh` expects the patched audit, built, in `oreaudit/` beside it.

Every server logged the sea-level rescale (1024: vanilla's 441 → 160 for 24–26 generators; 512: 220 → 110) and
`deposit sea agreement: PASS; 38 sea-level deposit generator(s), 0 not scaled`. No errors in any server log.

## Saltpeter and the sea-level offset: correct

No saltpeter above the ground in any run or site, including vanilla ores (the mode of the 0045 report). Saltpeter blocks
(count, above ground, highest y):

| Site | Ground y 512 / 1024 | 512 | 1024 no floor | 1024 floor | 1024 vanilla ores |
| --- | --- | --- | --- | --- | --- |
| Vienna | 127–134 / 171–174 | 0 | 23, 0, 116 | 23, 0, 116 | 170, 0, 113 |
| Pannonian plain | 122–126 / 165–169 | 0 | 0 | 0 | 0 |
| Alps | 298–335 / 348–417 | 0 | 4, 0, 121 | 0 | 0 |
| Zermatt | 257–313 / 321–373 | 272, 0, 66 | 1028, 0, 102 | 0 | 0 |
| Ethiopia | 317–327 / 379–399 | 0 | 0 | 0 | 0 |
| Tibet | 420–429 / 641–668 | 374, 0, 84 | 9, 0, 85 | 0 | 0 |
| Atacama | 262–274 / 300–311 | 1748, 0, 78 | 832, 0, 125 | 0 | 0 |
| Dead Sea | 83–89 / 132–139 | 93, 0, 64 | 3, 0, 68 | 3, 0, 68 | 79, 0, 77 |

Sea-level deposits reach at most y 160 (borax on the Pannonian plain with vanilla ores, ground 165–169). Without 0045's
rescale they would reach about 0.8 × 441 = 353, above most land. The Dead Sea's ground (y 132–139 at 1024) lies below sea
level; saltpeter stays at y ≤ 77 there.

## What the rock floor removes

All deposit blocks per chunk column, and sea-level ore blocks (iron, coal, chromite, ilmenite, cinnabar, saltpeter,
sulfur, borax, gems and the rest of `disc-followsealevel`) in the 25 columns:

| Site | All deposits per column: 512 / 1024 no floor / 1024 floor | Sea-level ore blocks: 512 / no floor / floor | Depth of sea-level ores at 512 |
| --- | --- | --- | --- |
| Vienna | 1.3 / 1.7 / 1.7 | 4,286 / 3,023 / 2,822 | 80–160 |
| Pannonian plain | 2.5 / 4.1 / 4.1 | 8,044 / 14,190 / 14,161 | 10–160 |
| Alps | 2.9 / 2.9 / 1.0 | 377 / 2,189 / **0** | 160–640 |
| Zermatt | 3.1 / 4.7 / 1.3 | 6,786 / 22,697 / **0** | 160–320 |
| Ethiopia | 3.1 / 4.6 / 2.0 | 1,470 / 5,816 / **0** | 160–320 |
| Tibet | 4.7 / 5.9 / 1.1 | 674 / 1,301 / **0** | 320–640 |
| Atacama | 4.9 / 6.2 / 3.7 | 3,806 / 7,758 / **0** | 160–320 |
| Dead Sea | 3.3 / 14.9 / 14.9 | 1,488 / 1,136 / 1,136 | 20–80 |

- **Lowlands**: the floor is at y 11 (Vienna) and 5 (Pannonia), and absent at the Dead Sea (ground below 160). It
  removes only what lies below: Pannonia a few per cent, Vienna cassiterite 1,224 → 677 blocks and chromite 913 → 712,
  all at y 2–11.
- **Highlands** (ground above about y 300, roughly 1,450 m): `disc-followsealevel` deposits sit at absolute y 0–145
  (vanilla's position × sea level 160), more than 160 blocks down, so the floor removes every one: hematite, magnetite,
  chromite, ilmenite, cinnabar, saltpeter, emerald. It also removes the deep part of the surface-following and anywhere
  deposits (native copper 87–99 %, quartz 49–86 %, pentlandite 100 %). In the Alps, Zermatt, Ethiopia and Tibet the floor
  leaves 19–43 % of the deposit blocks; Atacama 60 %.
- This is not new with tall worlds. In the 512 world with Earth's default sea level 110 the same highland sea-level ores
  are already 160–640 blocks below the ground (table). The floor turns "out of reach" into "absent". It is consistent with
  the rock floor's purpose (nothing deeper than 160 blocks, 1.6 km at 1:10), but highland regions get no iron, coal,
  chromite and the other sea-level ores at any depth.

Ore within 160 blocks of the ground is similar in the 1024 floor world and the 512 world (for example native copper,
Atacama 46,813 / 45,388 blocks; Zermatt 2,122 / 2,427; quartz Vienna 30,244 / 23,535). Vanilla scales attempts per chunk by
height / 256 (4× at 1024, 2× at 512) for every deposit. Where the floor cuts nothing, 1024 has 1.3–1.6× the 512 world's
deposit blocks per column (Vienna, Pannonia). The exception is the Dead Sea's halite: 335,718 blocks up to y 135 (ground
132–139) against 38,861 at 512. Sylvite (6,833) appears only at 1024.

## Prospecting pick: did not know the floor (tall.2)

The audit's prospecting readings (site centres) are **identical** in `floor` and `nofloor` for every site and ore. With
the floor the pick still reports, for example, hematite 1.33 ‰ at Zermatt and magnetite 0.95 ‰ in the Atacama, where none
exists. Earth's reading (`EarthOreGenerator.Reading`) works from a simulated rock column (`OreSession.EarthColumn`) and the
native depth distribution, not from placed blocks; vanilla's reading (vanilla ores) does the same with vanilla's column.
Neither knows the floor, and `FillRockFloor` removes the floor's map-chunk record once it has filled the chunks.

### Fixed in 1.0.3-tall.3 (4 November)

At the author's request ("Yes, fix the pick"; "only consider that there might be prospecting mods"), readings leave out
the floor (decision 0050, Decision 2): the floor's record stays in the map chunk; Earth's ore layer empties its rock
column and coal beds below it; vanilla's disc deposits get the pick's column with that part emptied. Same servers, sites
and seed, with the worktree's Release build (`run-tall3.sh`, `readings.py`; results `floor3`, `vanfloor3`,
`vannofloor3`, `fallback`). Prospecting ‰, summed over each site's five probe points:

| Site, ore | Regional: no floor / tall.2 floor / tall.3 floor | Vanilla ores: no floor / tall.3 floor |
| --- | --- | --- |
| Zermatt, hematite | 6.60 / 6.60 / **0.00** | — |
| Zermatt, native copper | 14.47 / 14.47 / 0.90 | 2.12 / 0.90 |
| Alps, magnetite | — | 0.58 / **0.00** |
| Alps, native copper | 6.97 / 6.97 / 0.44 | 18.23 / 7.21 |
| Atacama, magnetite | 4.71 / 4.71 / **0.00** | 0.32 / **0.00** |
| Ethiopia, hematite | 1.29 / 1.29 / **0.00** | — |
| Vienna, native copper | 4.59 / 4.59 / 4.06 | — |
| Pannonian plain, bituminous coal | 10.99 / 10.99 / 10.99 | 42.03 / 42.03 |
| Dead Sea, halite | 296.25 / 296.25 / 296.25 | — |

- Readings are expectations over each deposit's depth range, not counts of the sampled blocks; they now cover only the
  part above the floor. Ores whose whole range lies below it read 0: every sea-level ore at the highland sites, and
  pentlandite. Ores reaching above it keep that share: in the Alps native copper keeps 315 of 9,426 sampled blocks and
  reads 0.44 instead of 6.97; Ethiopia's cassiterite reads 0.16 instead of 0.32 although none of its 703 sampled blocks
  lies above the floor. At the Dead Sea (no floor) and on the Pannonian plain readings are unchanged, apart from ores
  reaching below y 5.
- The deposits placed are the same as tall.2's (`floor3` against `floor`: the same counts and Y ranges of every deposit
  family and the same rock count at all sites). The one exception is a loose native-copper block at Ethiopia (11 against
  10); the tall.2 runs differ among themselves by such single blocks.
- **Chunks from tall.2** (the `floor` world reopened with tall.3: no record, the floor from the chunk's lowest terrain
  height): readings equal fresh tall.3's at six sites. On the Pannonian plain and in the Atacama native copper reads up to
  2 % higher (11.16 / 11.02, 24.31 / 23.87).
- The prefix bound (no warning) and no errors in any server log; `deposit sea agreement: PASS` in each.

Not checked: other sites, child deposits individually, Earth's coal seams under highlands (none reached the floor here),
a prospecting mod in the game, and a graphical client.
