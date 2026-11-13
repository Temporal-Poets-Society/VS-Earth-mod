# 0035: Ship clearance through reviewed sea straits

Status: source implemented; offline clearance/renders checked; game acceptance pending. Date: 2026-10-30. Issue #26.

## Cause and decision

ETOPO's coarse mixed land/water cells and the coast outline can leave a strait narrow, shallow or interrupted.
0014 widens rivers; `SeaChannels` dredges exits and cuts configured canals (0020), but neither protects a complete
sea-to-sea strait. A real 700 m Bosporus narrows to roughly nine blocks at the requested scale.

`NavigableStraits` supplies one shared clearance operation for reviewed sea corridors: Bosporus, Dardanelles,
Kerch, Messina and Menai. Coarse geographic courses locate the passages; points every three columns follow
existing sea within 24 columns before the immutable spatial index is built. That avoids drawing across banks
between bends. Geometry is derived once from the world's frozen coast/height data, before rivers/channels.
Every column in the 24-block full core has at least six fluid blocks; deeper natural water stays. An eight-block
bank transition eases the cut. It is salt sea, with no river flow label. Expansion projection uses source geography.

These are gameplay clearances, not claims that the real Menai is six game blocks deep everywhere. Official
[Welsh bathymetry](https://naturalresourceswales.gov.uk/media/687997/eng-menai-strait-reg-37-report-2018.pdf) describes
substantial natural shallows; the [Turkish coastal authority](https://kiyiemniyeti.gov.tr/vessel_traffic_and_pilotage_services)
identifies the two Turkish shipping passages. The courses are approximate corridor locators, not official navigation
charts. The coast data provides alignment. No global inference turns arbitrary narrows into shipping corridors;
additional reviewed passages can use the same operation.

## Evidence

`tests/out/playtest-issues/review/strait-clearance.csv` samples both banks at intervals <=2 columns along the
resolved courses, at 512-high/log1000/512k (ring 409600), seed 386217, islands/coasts on. Width is contiguous water
at the given depth, measured normal to the course. The minimum before is **zero because each full route has at
least one interruption**; it does not mean the whole strait is dry. This distinguishes complete-route connectivity
from a favourable single cross-section.

| Strait | Before min ship width / depth | After min ship width / depth | Sections |
|---|---:|---:|---:|
| Bosporus | 0 / 0 | 25 / 6 | 373 |
| Dardanelles | 0 / 0 | 28 / 6 | 874 |
| Kerch | 0 / 0 | 28 / 6 | 473 |
| Messina | 0 / 0 | 38 / 6 | 298 |
| Menai | 0 / 0 | 26 / 6 | 648 |

`review/<Strait>-before.png` and `-after.png` are north-up offline terrain renders, visually inspected. Dark blue
has >=6 fluid blocks, light blue is shallower sea, brown is land. Natural depths outside the corridors are retained.
An early Messina locator cut unnecessarily along the Reggio bank; visual inspection caught it and its southern
course was moved back into the passage before these final metrics. Clearance is also checked on interpolated
segments, not just vertices. These renders are not game-client screenshots or physical vessel collision tests.

## Compatibility

StraitRules (85) is 1 in new worlds, 0 when absent. Existing worlds only adopt it with
`UpgradeExistingWorldWaterTerrain=true` (0034), reset after adoption. Existing chunks stay unchanged; a strait must
be generated with consistent rules along its full length to form a continuous passage. Ship mods can have larger
dimensions: six-block depth follows SeaChannels' existing ship contract, not every possible vessel.
No game client, install, push or commit. Coordinator game-assembly build and ship play-test remain required.
