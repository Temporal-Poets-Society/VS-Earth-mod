"""EA-0131: how many HydroRIVERS reaches the streams layer would add (1 <= Q < 20 m3/s), and how GIRES v1.0 classes them.
usage: python -I tools/streams/count_streams.py <hydrorivers-attrs.npz> <gires-reaches.npz> [qmin]"""
import sys
import numpy as np

h = np.load(sys.argv[1])
g = np.load(sys.argv[2])
qmin = float(sys.argv[3]) if len(sys.argv) > 3 else 1.0
q, ln, up = h["q"], h["length"], h["upland"]
sel = (q >= qmin) & (q < 20)
print(f"reaches {len(q)}; {qmin} <= Q < 20: {sel.sum()} reaches, {ln[sel].sum() / 1e6:.2f} million km")
# GIRES by HYRIV_ID
order = np.argsort(g["id"])
gid = g["id"][order]
pos = np.searchsorted(gid, h["id"])
pos = np.clip(pos, 0, len(gid) - 1)
found = gid[pos] == h["id"]
c1 = np.where(found, g["c1"][order][pos], -1)
c30 = np.where(found, g["c30"][order][pos], -1)
p30 = np.where(found, g["p30"][order][pos], np.nan)
s = sel
print(f"  found in GIRES: {(found & s).sum()}")
print(f"  c1 (>=1 zero-flow day): 0 {((c1 == 0) & s).sum()}, 1 {((c1 == 1) & s).sum()}, missing {((c1 < 0) & s).sum()}")
print(f"  c30 (>=30 days): 0 {((c30 == 0) & s).sum()} ({ln[(c30 == 0) & s].sum() / 1e6:.2f} M km), 1 {((c30 == 1) & s).sum()} ({ln[(c30 == 1) & s].sum() / 1e6:.2f} M km)")
runoff = q / np.maximum(up, 1e-3) * 31.5576e6 / 1e6 * 1e3 / 1e3  # mm/yr: m3/s per km2 * 3.15576e7 s / 1e6 m2 * 1e3 mm
runoff = q * 31.5576e6 / (np.maximum(up, 1e-3) * 1e6) * 1e3
for lim in (50, 100, 200):
    k = s & (runoff < lim)
    print(f"  runoff < {lim} mm/yr: {k.sum()} reaches; of them c30=1: {(k & (c30 == 1)).sum()}")
for lo, hi in ((1, 2), (2, 5), (5, 10), (10, 20)):
    k = (q >= lo) & (q < hi)
    print(f"  {lo}-{hi} m3/s: {k.sum()} reaches, {ln[k].sum() / 1e6:.2f} M km, often dry {(k & (c30 == 1)).sum()}")
