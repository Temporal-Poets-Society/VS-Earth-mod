"""
The mean-preserving monthly curve of the temperature layer (decision 0086): a periodic cubic spline through knots at the
months' middles whose knot values are chosen so that the curve's average over each month is that month's mean exactly.

With twelve equal months (the game's calendar) the map from the twelve means to the knot values is one fixed 12 x 12
matrix, and the curve at any point of the year is a fixed set of twelve weights on the means: T(t) = sum_j K_j(t) m_j.
The game keeps K as a table (TABLE_STEPS points per month, linearly interpolated); this module computes it and checks
the curve (month averages exact, overshoot) on the layer's cells and the stations.

    .venv/Scripts/python tools/season_curve.py table     prints the C# table
    .venv/Scripts/python tools/season_curve.py check     month averages and overshoot over every 37th cell of the layer
"""
import os, sys
import numpy as np
from scipy.interpolate import CubicSpline

MONTHS = 12
TABLE_STEPS = 32               # table points per month
FINE = 3600                    # integration points per month


def spline_weights(t):
    """The periodic cubic spline's weights on the 12 knot values (knots at j + 0.5) at the times t (months, 0..12)."""
    knots = np.arange(MONTHS + 1) + 0.5
    w = np.zeros((len(t), MONTHS))
    for k in range(MONTHS):
        v = np.zeros(MONTHS + 1)
        v[k] = 1
        v[MONTHS] = v[0]
        s = CubicSpline(knots, v, bc_type="periodic")
        w[:, k] = s(((np.asarray(t) - 0.5) % MONTHS) + 0.5)
    return w


def mean_matrix():
    """A[i, k]: the average over month i of the spline with knot k at 1 and the others at 0."""
    t = (np.arange(MONTHS * FINE) + 0.5) / FINE
    w = spline_weights(t)
    return w.reshape(MONTHS, FINE, MONTHS).mean(axis=1)


def kernel(steps=TABLE_STEPS):
    """K[p, j]: the curve's weight on month j's mean at the table point p (time p / steps months from the year's start)."""
    m = np.linalg.inv(mean_matrix())
    t = np.arange(MONTHS * steps) / steps
    return spline_weights(t) @ m


def curve(means, t, k=None):
    """The curve through one cell's twelve means at times t (months from the year's start), from the table as the game reads it."""
    k = kernel() if k is None else k
    pos = (np.asarray(t) % MONTHS) * TABLE_STEPS
    i0 = np.floor(pos).astype(int) % (MONTHS * TABLE_STEPS)
    f = pos - np.floor(pos)
    i1 = (i0 + 1) % (MONTHS * TABLE_STEPS)
    return ((1 - f)[:, None] * k[i0] + f[:, None] * k[i1]) @ means


def main():
    what = sys.argv[1] if len(sys.argv) > 1 else "check"
    k = kernel()
    if what == "table":
        print(f"// {MONTHS * TABLE_STEPS} x {MONTHS}: tools/season_curve.py table")
        for row in k:
            print("    " + ", ".join(f"{v:.9f}f" for v in row) + ",")
        return
    # the table's month averages (the linear table between points is what the game reads)
    t = (np.arange(MONTHS * FINE) + 0.5) / FINE
    pos = t * TABLE_STEPS
    i0 = np.floor(pos).astype(int) % (MONTHS * TABLE_STEPS)
    f = pos - np.floor(pos)
    w = (1 - f)[:, None] * k[i0] + f[:, None] * k[(i0 + 1) % (MONTHS * TABLE_STEPS)]
    a = w.reshape(MONTHS, FINE, MONTHS).mean(axis=1)
    print(f"table's month averages against the identity: max |error| {np.abs(a - np.eye(MONTHS)).max():.2e} per degree of mean")
    path = os.path.join(r"C:\VS mods\work\seasons", "seasons-means.npz")
    if not os.path.exists(path):
        return
    means = np.load(path)["means"][:, ::37, ::37].reshape(MONTHS, -1).T.astype(float) / 10
    vals = means @ w.T                                   # cells x time
    over = vals.max(axis=1) - means.max(axis=1)
    under = means.min(axis=1) - vals.min(axis=1)
    print(f"{len(means)} cells: the curve's peak above the warmest month's mean: median {np.median(over):.2f}, 99% {np.percentile(over, 99):.2f}, max {over.max():.2f} C")
    print(f"  its trough below the coldest month's mean: median {np.median(under):.2f}, 99% {np.percentile(under, 99):.2f}, max {under.min() if False else under.max():.2f} C")


if __name__ == "__main__":
    main()
