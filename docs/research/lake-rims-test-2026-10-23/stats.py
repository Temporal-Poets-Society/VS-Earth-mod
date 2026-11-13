# stats.py <npz from view.py>: lake (main level) water walls, curtain edge, fall columns, lower river width
import sys
import numpy as np
z = np.load(sys.argv[1]); th, tw, fc = z["th"], z["tw"], z["fallc"]
wet = tw > th; N = th.shape[0]
L = np.bincount(tw[wet]).argmax()
up = wet & (tw == L)


def nb(m):
    r = np.zeros_like(m); r[1:] |= m[:-1]; r[:-1] |= m[1:]; r[:, 1:] |= m[:, :-1]; r[:, :-1] |= m[:, 1:]; return r


dry_low = ~wet & (th <= L - 2)
wall = up & nb(dry_low)
low = wet & (tw <= L - 5)
heights = []
for i, j in zip(*np.nonzero(wall)):
    m = min(th[a, b] for a, b in ((i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1)) if 0 <= a < N and 0 <= b < N and not wet[a, b])
    heights.append(int(L - m))
print(f"upper level {L}: water beside dry land 2+ lower {wall.sum()} (heights {sorted(set(heights))}); "
      f"upper water beside lower water (curtain edge) {(up & nb(low)).sum()}; fall columns {fc.sum()}")
# dry cliffs: dry land 5+ above a dry neighbour, near the falls (within 40 of a fall column)
fi, fj = np.nonzero(fc)
near = np.zeros_like(fc)
if len(fi):
    ii, jj = np.meshgrid(np.arange(N), np.arange(N), indexing="ij")
    for a, b in zip(fi[::3], fj[::3]):
        near |= (np.abs(ii - a) <= 40) & (np.abs(jj - b) <= 40)
cl = np.zeros_like(fc)
for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1)):
    nbt = np.roll(np.roll(th, a, 0), b, 1); nbw = np.roll(np.roll(wet, a, 0), b, 1)
    cl |= ~wet & ~nbw & (th - nbt >= 5)
print(f"dry cliffs of 5+ within 40 blocks of the falls: {(cl & near).sum()} columns")
