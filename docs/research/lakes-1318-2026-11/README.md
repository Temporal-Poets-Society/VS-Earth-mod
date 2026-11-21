# The lakes of 1318 (EA-0124, decision 0107; 21 November 2026)

Decision [0107](../../decisions/0107-lakes-of-1318.md) builds step 3 of decision 0064: works rules 6 (`works-1318-4.bin`)
and river ends rules 3 (`lakes-2.bin`), new worlds only.

- [`ea-0124-check-rc13.md`](ea-0124-check-rc13.md): the check of decision 0005's revived terminal lakes in a 2.2.0-rc.13
  world on GLO-90 (the Aral whole at 53 m, one level, rivers in, the 2′ grid's shores), what new worlds had where the
  drained lakes were, the Aral's hypsometry, and the sizing that led to 0107.
- [`research-drained-lakes.md`](research-drained-lakes.md): the sourced research (a helper agent's, read and checked
  against the probe): drained lakes worldwide with their state about 1318, outline sources and licences, subsidence,
  rivers in and out, the Aral and the Sarykamysh about 1318, the Bitter Lakes before 1869, and the outline datasets.
- `probe/` (`Lakes1318Probe`): builds a new world's terrain model as a server does (WorldParamsBuilder's params for the
  `earth-10`, `earth-25` and `earth` play styles, seed 386217) on the full GLO-90 store and samples `GetColumn` on the
  1:10 world's block grid (`EA0124_STRIDE`: every Nth block); variants `base` (this build's new world), `w5` (works rules
  5 and river ends rules 2, as rc.14 makes it), `noends` (no terminal lakes). It writes each window's flags (bit 65536
  inside a LAKE area of 1318, 131072 inside a LAND area), tops, water tops, GLO-90 heights and the courses near it with
  their levels.
- `scripts/`: `check.py` (per window: the lakes' water, water tops, water beside lower land, steps, river contacts, maps),
  `pairs.py` (what the steps are between), `cmpdig.py` (the digest places with and without the new rules), `analyse.py`
  (the rc.13 check's counts and maps).
- `images/`: the rc.13 Aral without and with 0005's lake, its shores at Muynak and under the Ustyurt cliff; the Aral, the
  Valley of Mexico and Fucino, Copais, Karla and Amik at 1:10 as rc.14 and as this build make them (blue the lakes of
  1318, cyan today's lakes, purple rivers).

Usage: build `probe` (as SavedWorldDigestTests, with `-p:VintageStoryPath` or `VINTAGE_STORY`), then
`EARTH_STORE=<data store> EA0124_STRIDE=<n> dotnet probe/bin/Release/net8.0/Lakes1318Probe.dll <repo root> <out dir>
<places file> 10,100 base,w5`; a places file has `name; lat; lon; half lat; half lon` per line. The run of 21 November
(`C:\VS mods\work\ea-0124\l1318`, disposable) used strides 40 (the Aral), 4, 10 and 2, and 1 at the places with steps.

## Results (21 November 2026)

New world of this build against rc.14's rules, 1:10 and 1:100:

| Lake | Water (km²) | Water tops (1:10 / 1:100) | Notes |
|---|---|---|---|
| Aral (29 m) | 8,024 | 111 / 112 | Amu Darya into the eastern lagoon (216 / 1,006 contacts, no step); the Syr Darya into today's North Aral |
| Valley of Mexico (5) | 869 | 339 / 248 | one top; no step block by block where they meet or at Zumpango's lagoon |
| Fucino (660 m) | 131 | 177 / 169 | clean |
| Copais (97 m) | 234 | 118 / 120 | one-block drops where small rivers meet it at 1:100 (8 sampled contacts) |
| Karla (48 m) | 131 | 113 / 114 | clean |
| Amik (79 m) | 148 | 117 / 118 | one-block drops at small rivers at 1:100 (27 contacts) |
| Hachirōgata (0 m) | 145 | 109 / 109 | two-block drops at the remnant channels at 1:100 (39), as in the rc.14 world (41) |
| Antela, Hula, Whittlesey, Stretham | 26, 13.5, 4.1, 0.3 | one each | clean (Hula's fishponds give way to its shore) |
| Ogura-ike (12 m) | 7.7 | 110 / 110 | one-block drops at small rivers (13 / about 50 contacts) |
| Salton Sea (LAND) | 0 of 1,080 | – | dry |

- Water beside lower dry land: none at 1:100; one or two sampled columns at 1:10 (Amik, Hachirōgata, Ogura-ike).
- New worlds at 1:10 and 1:25, 128 × 128 blocks at each of SavedWorldDigestTests' 35 places: identical with rules 6 and
  rules 5 at all but the Aral.
- SavedWorldDigestTests: the 12 saved worlds' pinned hashes all equal.
