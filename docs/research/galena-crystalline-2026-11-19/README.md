# Galena in crystalline rock: census of 19 November 2026 (EA-0174, decision 0102)

Vanilla ores, Earth `main` at `3d1a4ae5` (geology rules 3) against the branch with galena in crystalline rock
([decision 0102](../../decisions/0102-galena-in-crystalline-rock.md)): regional rock at 1:10 and 1:100, and vanilla rock
at 1:100, which must not change.

## Method

As the geology rules 3 census ([geology-rules-3-2026-11-19](../geology-rules-3-2026-11-19/)): dedicated servers one after
another, the play styles `earth-10` and `earth`, seed 12345, spawn Vienna, vanilla ores, the ore audit mod with the rock
census patch, 5 × 5 chunk columns at 11 places (`sites-galena.json`: lead and silver districts and two controls).
Packages: `main` relabelled `2.1.4-main.2`, the branch `2.1.4-galena.1`. The branch's logs say "galena and its silver
also form in granite, slate, phyllite, gneiss, schist in this world (decision 0102): 5 host rocks added" and "the
prospecting pick reads galena and its silver in those rocks too"; its vanilla-rock world says neither. No new warnings.
`compare_galena.py` writes `compare.txt`; `census.csv` has galena, its silver, loose galena and sphalerite per place and run.

## Results (blocks per chunk column)

| Place | 1:10 galena | 1:100 galena |
|---|---|---|
| Kongsberg (granite, gneiss) | 0 → 31 | 0 → 10 |
| Freiberg (granite, gneiss, schist) | 0 → 0 | 0 → 91 |
| Broken Hill (granite, schist, gneiss) | 0 → 56 | 0 → 0.4 |
| Sala (gneiss, granite) | 0 → 22 | 0 → 127 |
| Harz (granite over sediments) | 0 → 0 | 28 → 49 |
| Linares (granite) | 4 → 30 | 5 → 20 |
| Coeur d'Alene (granite, slate) | 0 → 13 | 109 → 351 |
| Riga (sediments over gneiss) | 0 → 0 | 330 → 550 |
| Vienna (granite, sediments) | 0 → 0 | 0.4 → 10 |
| Příbram, Dartmoor | 0 → 0 | 0 → 0 |
| All 11 places | 4 → 152 | 472 → 1,208 |

Vanilla rock at 1:100: every place unchanged (102 in all, silver and loose galena too).

Places with crystalline rock now have galena; where vanilla's ore map is low (Příbram, Dartmoor, Freiberg at 1:10)
there is still none, as vanilla's ore maps decide. Where sediments lie over crystalline basement within reach, the
attempts that used to land in the basement now place galena too, so those places have more lead than before (Riga
+67 %, Coeur d'Alene ×3.2 at 1:100). Silver in galena stays as rare as in vanilla (0.1 to 0.4 a chunk column over all
places). The rock shares are the same in both runs (ore blocks take a few rock blocks).

## Not covered

How the blocks look in game (they reuse vanilla's textures: the galena overlay on each rock); the prospecting pick's
reading in these rocks (the log confirms its copy has the hosts); Earth's own ore maps (unchanged by design, step 4).
