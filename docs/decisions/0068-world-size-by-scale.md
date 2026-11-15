# 0068: The world's size by its scale: width = 2 × length + a fixed seam margin

Status: **agreed** (the author in the Cylinder World session, 9 November 2026, relayed to Earth: "vanilla does not have
any preset ratio. Can choose whatever as width and length is set separately and default is 1M and 1M. Current width is
confusing. and i dont see any reason for it", "i was considering as extreme 1:5 scale", and "ok can you pass this to main
session once it is done with current work to do it"). Built in 2.0.0-rc.5; in Earth 2.0.0. Date: 2026-11-09.

## Before

Earth's size option was a world width; the length was 0.4 × the width (`SpawnSearch.LengthPerWidth`), and Cylinder
World's Earth proportions made the ring 2 × the length. So a tenth of the width at each side, beyond the jump lines, was
hidden seam ocean: 500 992 blocks per side at 1:10. Nothing generates or loads there further than about a view distance
past a jump line, so it cost no memory, but it had no purpose and made the numbers confusing.

## Decided

1. **The option is the scale** (`earthWorldSize`: `200`, `100` (default), `50`, `25`, `10`, and the new `5`; `1:10` typed
   by hand is read too). The world's **length** is the Equator (40 075 017 m) over twice the scale, in whole 512-block map
   regions; the same lengths as before for 1:200 to 1:10. Its **width** is twice the length (Cylinder World's ring) plus
   **16 384 blocks of hidden seam ocean beyond each end** (`SpawnSearch.SeamMargin`, 32 map regions).
2. **Why 16 384:** Cylinder World's Earth proportions need at least the view radius + 32, rounded up to 256 (512 at 12
   chunks, 1 280 at 32), or its rule falls back and the ring is no longer 2 × the length; 32 regions also cover a server
   that raises its view distance later, and far-view or LOD mods.
3. **Sizes:**

   | Scale | Width × length (blocks) | Before |
   |---|---|---|
   | 1:200 | 233 472 × 100 352 | 250 880 × 100 352 |
   | 1:100 | 433 152 × 200 192 | 500 736 × 200 192 |
   | 1:50 | 834 560 × 400 896 | 1 002 496 × 400 896 |
   | 1:25 | 1 635 328 × 801 280 | 2 002 944 × 801 280 |
   | 1:10 | 4 040 704 × 2 003 968 | 5 009 920 × 2 003 968 |
   | 1:5 | 8 047 616 × 4 007 424 | (not offered) |

   1:5 is the finest scale the option takes: its width fits Earth's cap of 8 192 000 (vanilla's own limit is 67 108 864).
4. **New worlds only.** A world keeps the size it was made with. A world configuration from before (a width in blocks,
   64 000 to 8 192 000) is still read: its length stays 0.4 × that width, its width follows the new rule. Absolute
   coordinates of the same place differ from a world made before (the ring now starts at x = 16 384, not a tenth of the
   width in), and with them the terrain's fine detail noise, as for any new world.
5. Cylinder World needs no change: it takes the width it gets.

## Checked

- `EarthMapTests`: every scale's size (the Equator over the ring within 0.2 %, whole map regions, under the cap), the old
  widths reading as the same lengths, values below 1:5 and outside the old widths refused; `EarthWorldOptionsTests`: the
  play styles' scales, the menu (1:5 offered, 1:100 the default, no 1:400), each label naming its scale and size, a scale
  and its old width giving the same metres per block. The Earth tab's texts and values checked against vanilla's menu
  code (24 options, 5 play styles, no missing text).
- Headless new worlds at 1:10 (from an old width value) and 1:5, 20 test places each, no error: Cylinder World takes both
  as Earth proportions (ring twice the length, jump lines at x = 16 384 and 16 384 + ring); the spawn search finds the
  same place as before; 1:5 builds its model in 28.8 s (15.5 million river pieces) and peaks at 7.2 GB (the tab says so).
  The story locations land elsewhere than in an rc.4 world (their site search reads the terrain at the new coordinates).
- Codex: a hand-written ratio such as `1:8192000` was read as an old width and made a huge world; a value written as a
  ratio is now a scale only (refused beyond 1:5 to 1:1000).
