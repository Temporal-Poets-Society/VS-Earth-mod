# Detail on the included GLO-90 heights: noise, none, or gullies (5 November 2026)

Status: experiment, not agreed. Prompted by the author on the 4 November Everest sheet: "we can do better on smoothing
and not just applying noise", "at least by images with just noise we loose real details", "can we apply other algorithm
to smoothen and then small noise?".

Method: the 0053 sampler (scratch `sampler4`) at the author's world settings (4,096,000 wide, height 1024, linear;
about 8 m per block), dry land against Copernicus GLO-90, as in [the 4 November comparison](terrain-vs-glo90-2026-11-04.md).
The included heights are the 24″ grid (about 741 m) interpolated bicubically (Catmull-Rom); the variants differ only in
what is added below the grid's cell.

| Variant | What is added |
|---|---|
| Noise rules 1 (tall.5) | turned fBm and ridges, spread = GLO-90's own spread inside the cell |
| No noise | nothing |
| A third of the noise | rules 1 at a third |
| Gullies A (prototype rules 2) | gullies and spurs running down the grid's own slope (`src/Core/Gullies.cs`), wavelengths 741 m down to 90 m or 4 blocks, each finer octave 0.6 as steep, plus rules 1 noise at a tenth |
| Gullies B | as A, 1.5 times as steep, finer octaves 0.5, down to 120 m |

Mean error of dry land (m) and number of tops (real tops in brackets):

| Place | Rules 1 | No noise | A third | Gullies A | Gullies B |
|---|---|---|---|---|---|
| Everest (120) | 75 / 85 | 51 / 79 | 54 / 82 | 57 / 82 | 64 / 86 |
| Grand Canyon (102) | 71 / 152 | 48 / 91 | 51 / 108 | 51 / 100 | 54 / 113 |
| Middle Rhine (11) | 20 / 40 | 13 / 2 | 14 / 4 | 14 / 3 | 14 / 5 |
| Nærøyfjord (83) | 56 / 95 | 37 / 44 | 40 / 64 | 41 / 58 | 44 / 63 |
| Fuji (8) | 13 / 8 | 8 / 1 | 9 / 3 | 11 / 2 | 14 / 3 |

All 13 places for rules 1, no noise and a third: [results-13-places.json](detail-gullies-2026-11-05/results-13-places.json).
No noise is about a third closer to GLO-90 than rules 1 everywhere: noise as spread as the real detail but not where
the real detail is doubles its error. Close-ups:
[Everest](detail-gullies-2026-11-05/everest-zoom.jpg), [Nærøyfjord](detail-gullies-2026-11-05/naeroyfjord-zoom.jpg).

Reading the close-ups:
- Rules 1 covers everything in an even stipple, glacier floors and fjord water's edge included; it hides the grid's real
  ridges and valleys.
- No noise keeps them but is soft: ridges and fjord walls are rounded over a cell.
- Gullies A run down the slopes and branch, so fjord walls get notches like the real ones and valley floors and flat
  land stay smooth; plateau tops still get a faint fuzz. It costs 1–6 m of mean error over no noise.

What the included grid can't give: anything real below its 741 m cell. A different interpolation of the same means
changes little (the error of no noise is mostly the grid's own); the detail added below it can only look like terrain,
not match it.

At the default 1:100 (100 m per block) a cell is about 7 blocks, so rules 1 has one octave and gullies one or none;
the difference matters at 1:50 (37 blocks a cell) and where the included heights stand in for full GLO-90 at 1:25 and
1:10.

## Why "no noise" differs from GLO-90

[everest-why.jpg](detail-gullies-2026-11-05/everest-why.jpg) draws the grid's 24″ cells on the real heights (about 32
pixels each in the close-up) and maps the difference. The grid holds one mean height per cell; the smooth curve through
the means can't hold a valley or ridge narrower than about two cells (1.5 km). Narrow valleys come out too high (35 %
of the close-up more than 30 m high), crests and their steep faces are blurred into each other (25 % more than 30 m
low), mean 62 m in the close-up. The three red spots at Everest, Lhotse and Nuptse are the surveyed summits put back
on the smooth surface, wider than the real peaks. It is the grid's own error (65 m RMS at Everest in the
[build note](glo90-coarse-packs-2026-11-05.md)), not noise or a shift.

The included heights are not GLO-90 compressed without loss: that is 8.6 GB (decision 0051's store). They are GLO-90
averaged to one height per cell, about 64 GLO-90 points each, and only those means are coded without loss.
[grandcanyon-grids.jpg](detail-gullies-2026-11-05/grandcanyon-grids.jpg) shows the same close-up at 24″, 20″, 18″ and
9″: the side canyons, 200–800 m wide, come back only at 9″.

## Strength and noise sweep (evening, 5 November)

The author asked to vary the gully strength and the plain noise on top, and compare them with the real terrain
(Gullies B "quite strong"). Gullies A's other settings (longest 1 cell, finer octaves 0.6 as steep, down to 90 m or
4 blocks); strength 0, 0.5, 0.75 and 1; noise 0.1, 0.25 and 0.5 of rules 1 (of GLO-90's spread in the cell).
Close-ups: [Everest](detail-gullies-2026-11-05/everest-sweep.jpg),
[Nærøyfjord](detail-gullies-2026-11-05/naeroyfjord-sweep.jpg),
[Grand Canyon](detail-gullies-2026-11-05/grandcanyon-sweep.jpg); all numbers
[results-sweep.json](detail-gullies-2026-11-05/results-sweep.json).

Mean error of dry land (m) / tops, rows gully strength, columns noise:

| Place (real tops) | No noise | Rules 1 | Strength | Noise 0.1 | 0.25 | 0.5 |
|---|---|---|---|---|---|---|
| Everest (120) | 51 / 79 | 75 / 85 | 0 | 51 / 81 | 53 / 82 | 58 / 82 |
| | | | 0.5 | 53 / 80 | 54 / 78 | 60 / 80 |
| | | | 0.75 | 55 / 81 | 56 / 79 | 61 / 81 |
| | | | 1 | 57 / 82 | 59 / 82 | 64 / 84 |
| Grand Canyon (102) | 48 / 91 | 71 / 152 | 0 | 48 / 91 | 50 / 100 | 55 / 119 |
| | | | 0.5 | 49 / 92 | 50 / 100 | 55 / 118 |
| | | | 0.75 | 50 / 93 | 51 / 101 | 56 / 119 |
| | | | 1 | 51 / 100 | 52 / 110 | 57 / 122 |
| Middle Rhine (11) | 13 / 2 | 20 / 40 | 0 | 13 / 3 | 14 / 4 | 15 / 7 |
| | | | 0.5 | 13 / 3 | 14 / 4 | 15 / 8 |
| | | | 0.75 | 14 / 3 | 14 / 4 | 15 / 7 |
| | | | 1 | 14 / 3 | 14 / 4 | 16 / 9 |
| Nærøyfjord (83) | 37 / 44 | 56 / 95 | 0 | 38 / 54 | 39 / 60 | 43 / 70 |
| | | | 0.5 | 39 / 55 | 40 / 59 | 44 / 73 |
| | | | 0.75 | 40 / 57 | 41 / 61 | 45 / 73 |
| | | | 1 | 41 / 58 | 42 / 63 | 46 / 76 |
| Fuji (8) | 8 / 1 | 13 / 8 | 0 | 8 / 3 | 8 / 3 | 9 / 5 |
| | | | 0.5 | 9 / 3 | 9 / 3 | 10 / 5 |
| | | | 0.75 | 10 / 3 | 10 / 2 | 11 / 5 |
| | | | 1 | 11 / 2 | 11 / 3 | 12 / 5 |

Reading it:
- Every added detail costs accuracy; noise costs more than gullies for the same look of roughness (noise 0.5 alone: +4
  to +7 m; gullies 0.5: +0 to +2 m).
- Noise of 0.25 and more brings back the even stipple of rules 1 on plateaus and valley floors; 0.1 is about 5 m and
  only shows in the hillshade.
- Gullies 1 put close teeth along the fjord walls, closer than the real notches; 0.5–0.75 look nearer the real walls.
- None of them finds the real narrow valleys (Grand Canyon's side canyons, Everest's glacier valleys): those are below
  the grid's cell.

Code state: `Gullies.cs` and the rules-2 branch in `TerrainModel.CoarseGround` are in the worktree with prototype knobs
(`GullyStrength` etc., not saved); `DetailRulesVersion` stays 1, so no build makes rules-2 worlds yet. tall.6 was packed
before this code.
