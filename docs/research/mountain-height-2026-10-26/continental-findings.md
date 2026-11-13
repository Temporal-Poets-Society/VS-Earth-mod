# Initial continental mountain findings

The first complete Release matrix finished on 26 October 2026: **22 of 22 worlds passed the recorded integration
checks**, covering fourteen locations per world and 2,838,528 analyzed columns. Both heights 384 and 512 used the
same seed, world width and vanilla vegetation providers, with Cylinder World 0.11.14. The exact DLL hash is in
[the machine-readable summary](continental-matrix/summary.json). Repeated performance, reload and optional-provider
tests are a separate follow-up; these initial single runs do not establish a loading-time improvement.

The [offline visual comparison](continental-matrix/comparisons.html) contains every original surface tile, with
height, location and case controls. Its overhead maps cannot establish whether the first-person mountain silhouette
looks right. [All site measurements](continental-matrix/site-comparisons.csv) retain the location and case labels.

## Continental coverage

The table compares current defaults A against the gentler 2000 m curve C at height 512. Terrain range is highest minus
lowest generated ground in the fixed 96 by 96 block footprint. The percentages are reported top-block coverage over
that whole footprint, rounded by the server self-test; canopy can cover lower slopes as well as the central target.

| Mountain | Continent | Ground range A to C in blocks | Canopy A to C | Exposed rock A to C | Interpretation for playtesting |
| --- | --- | ---: | ---: | ---: | --- |
| Everest | Asia | 18 to 44 | 0% to 0% | 2% to 4% | More local relief; snow and ice still dominate. |
| Mont Blanc | Europe | 57 to 106 | 4% to 2% | 46% to 71% | More relief and exposed rock; check whether the slopes feel too abrupt. |
| Kilimanjaro | Africa | 30 to 64 | 0% to 0% | 43% to 66% | More summit relief and less soil; inspect the lower forest transition separately. |
| Denali | North America | 37 to 81 | 0% to 0% | 0% to 1% | Snow still dominates; larger ground relief does not mean the summit is higher in the source grid. |
| Aconcagua | South America | 26 to 59 | 0% to 0% | 22% to 40% | More exposed rock and ground relief. |
| Kosciuszko | Australia | 27 to 42 | 10% to 19% | 1% to 2% | Vegetation needs care: the gentler curve alone does not make every mountain less wooded. |
| Vinson | Antarctica | 23 to 50 | 0% to 0% | 0% to 0% | Persistent snow/ice retained; inspect skyline and ice geometry in a client. |

Fuji changes from 63 to 101 blocks of local ground relief, canopy from 22% to 14%, and rock from 10% to 34%. Puncak
Jaya changes from 28 to 56 blocks, keeps about 3% canopy over the footprint and gains exposed rock from 0% to 13%.
Elbrus also remains treeless in these sampled areas. The raw data includes all three additional mountain controls.

## The combined candidate needs a separate judgment

Case K is not the automatic winner. At height 512 its physical-slope rock rule retains much more soil than C on
several mountains: Fuji reports 59% soil and approximately 0% exposed rock; Kilimanjaro reports 72% soil and 7% rock.
Case I, with the current rock rule and local ecology, exposes much more rock on those slopes than K. These are
different hypotheses about soil retention. Prefer C and the adaptive curve F as initial geometry comparisons, then
compare ecology I and rock J independently before accepting a combination.

St Helena shows the same distinction: current A reports 65% rock and 3% sand, C about 63% rock and 16% sand, and K
about 0% rock and 78% sand. The water footprint remains about 21%. K does not resolve the dry-looking island by making
it green. The sampled climate rainfall suitability is only 12.79/255, so an island-climate investigation is still
needed. The Santorini target includes about 75% water; inspect the surrounding island and record the user's exact
Mediterranean example before generalizing from it.

## What passed and what remains

All matrix rows reported sea-level agreement between Earth, Cylinder World and the climate helpers, and no recorded
errors, failed checks, missing ramps or water seams were found in these reports. The four dedicated climate probes
per world pass; the fourteen generated regions also record climate and vegetation observations. This is not a
complete validation of every direct climate-byte consumer or a first-person client/shader playtest.

The height curve and soil-exposure rule remain coupled where slopes are measured in blocks. A gentler curve improves
the balance between large and small relief in these measurements, but source-grid summit averaging, vegetation,
seasonal appearance, cave/ore access and the user's visual preference still determine which profile should ship.
