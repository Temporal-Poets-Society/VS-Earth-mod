# Source terrain and activation follow-up

## Dependency activation guard fixed in pre 2

A disposable 512-high world with the experimental 128-block budget and Cylinder World disabled reproduced an
introduced startup bug. Earth correctly refused to activate, but the early bootstrap had already changed the world,
terrain-generation and climate sea levels to 128. Its fallback therefore no longer had vanilla's sea level of 220.

The bootstrap now checks Cylinder World's public configuration before choosing a custom sea level for a new world:
Enabled, PolarWalls and PolarClimate must all be true. Full geometry validation still occurs in the existing Earth
initialization; this preflight does not replace it. Existing Earth saves continue restoring their saved sea level.

The same disabled-dependency scenario after the change retained 220 in all three APIs. A supported 384-high combined
profile still activated at 128, agreed with Cylinder World, and passed the climate checks. The change and an inactive
self-test diagnostic are packaged as **0.17.0-pre.2**; pre.1 and its evidence remain preserved.

```text
Before: Earth inactive; world sea=128, generation sea=128, climate sea=128.
After:  Earth inactive; world sea=220, generation sea=220, climate sea=220.
Valid:  Earth active; sea agreement PASS at 128; climate helper checks PASS.
```

## A nearby model maximum is more useful than the target column alone

[The source survey](source-peaks.csv) checks a neighbourhood of plus or minus 0.15 degrees around each target at
one-arcminute sampling intervals. It disables procedural detail, enables the current small-island grid patches and
reconstructs small islands through IslandLayer. It does not apply river carving, site flattening or Cylinder World's
polar passes. Its nearby maximum is a diagnostic sample, not identification of a surveyed summit.

Fuji's sampled neighbourhood remains around 2840 m at its maximum, while the
[Geospatial Information Authority of Japan reports a 3776 m summit](https://web1.gsi.go.jp/WNEW/PRESS-RELEASE/keikaku61003.html).
The Everest neighbourhood rises from about 7309 m at the target to 7437 m nearby; Denali rises from about 5245 to
5606 m. Changing the height curve cannot recreate missing summit shape or recover the correct source elevations.
The sampling window is not a replacement for a higher-resolution DEM comparison.

Small islands require particular care. The current island patches deliberately remove their coarse terrain blobs
from the grid, and IslandLayer reconstructs their land from outlines and stored island height. Reading the patched
grid alone makes St Helena appear to be shallow sea. Conversely, reading the old unpatched grid can give irrelevant
peaks that the current generator no longer uses. The revised diagnostic separates patched-grid and reconstructed
model elevation; the profile CSV uses the outline model for small islands too.

## St Helena needs an island climate and shape review

The outline model without detail reaches about 936 m at the St Helena target and 1125 m in the sampled neighbourhood.
This deserves investigation alongside the island's stored source height and profile. The government's
[Peaks National Park management plan](https://www.sainthelena.gov.sh/documents/st-helena-the-peaks-national-park-conservation-management-plan-2019-2024-3/)
lists its protected central ridge up to 823 m. This comparison flags a mismatch; it does not establish the error in
every generated column.

The same plan describes about 175 mm of annual rain in dry outer areas and about 1050 mm in the cloudier Peaks, with
mist providing an important additional moisture source. That variation cannot be represented by treating the whole
island as uniformly dry. The current target's climate suitability of 12.79/255 and sandy result should therefore be
checked against local rainfall, mist and elevation evidence. This is a source-data/habitat question in addition to
the slope rule. No global climate data or island height has been changed in this experiment.
