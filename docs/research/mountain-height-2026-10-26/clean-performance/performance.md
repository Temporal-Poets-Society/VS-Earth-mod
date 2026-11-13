# Generation and reload timing results

These are separate dedicated-server cohorts with three repeats per case and height. Each world uses seed 386217,
width 512000, fourteen target areas, vanilla vegetation providers and Cylinder World 0.11.14. Case A retains current
defaults; B uses sea 128 and the current height curve with climate rules 2. These compare complete profiles, not air
volume alone.

## Fresh world process timing

The clean pre.2 cohort ran sequentially after the debugging/provider servers finished. Totals include test setup,
mod/data loading, spawn and target-area generation, analysis and shutdown. They are not client loading-screen or FPS
measurements. Filesystem caches were not flushed.

| Height | Case | Median seconds | Range in seconds | Mean save MiB |
| --- | --- | ---: | ---: | ---: |
| 384 | A | 77.01 | 76.62 to 77.88 | 91.27 |
| 384 | B | 78.83 | 78.17 to 79.15 | 91.53 |
| 512 | A | 82.70 | 82.11 to 82.85 | 96.23 |
| 512 | B | 84.52 | 84.36 to 84.61 | 95.51 |

The server also times the fourteen requested target regions separately. These sums include their generation,
region analysis and surface rendering; they exclude most process startup and are not pure terrain-fill timings.

| Height | Case | Median target-region seconds |
| --- | --- | ---: |
| 384 | A | 33.50 |
| 384 | B | 34.60 |
| 512 | A | 39.10 |
| 512 | B | 39.10 |

No end-to-end speedup appears in this sample. Case B is about two percent slower, while saved size changes by less
than one percent. Climate encoding and terrain differences prevent attributing those small changes solely to the
underground budget. Observed peak working sets varied substantially between repeats; they do not establish a
predictable memory saving.

## Existing save reload timing

The separate pre.1 cohort reopened each already-generated save using the same original build and targets. Its fresh
reports and server logs were archived before reopening. This cohort is not combined with pre.2 absolute timings.

| Height | Case | Median seconds | Range in seconds |
| --- | --- | ---: | ---: |
| 384 | A | 37.41 | 37.15 to 37.46 |
| 384 | B | 38.02 | 37.79 to 38.52 |
| 512 | A | 37.18 | 36.51 to 37.37 |
| 512 | B | 38.09 | 38.05 to 38.59 |

Reloading B is also slightly slower in this sample. The practical reason to choose the new layout is its terrain
proportions and available height; a faster general load time has not been demonstrated. A 512-high column still has
16 vertical chunk slots, versus 12 at height 384.

[Exact run values](timing-runs.csv) and [grouped values with DLL hashes](timing-summary.json) preserve the measurements.
The earlier development timing series overlapped activation probes and is excluded from these fresh-world figures.
