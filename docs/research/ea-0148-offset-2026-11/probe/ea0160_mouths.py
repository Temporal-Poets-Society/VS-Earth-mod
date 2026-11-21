"""EA-0160: river ends rules 2 vs 1 at 13 mouths, deltas and fjords (1:10 and 1:100)."""
import os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from analyse import load

d = r"C:\VS mods\work\ea-0148\ea0160\mouths4"
places = [l.split(";")[0].strip() for l in open(r"C:\VS mods\work\ea-0148\ea0160\mouths14.txt") if l.strip()]
print(f"{'place':18s} {'scale':>5s} {'cols':>7s} {'top differs':>11s} {'max raise':>9s} {'water r2/r1':>15s} {'river r2/r1':>15s} {'course r2/r1':>15s} {'trench r2/r1':>13s}")
for p in places:
    for s in (10, 100):
        a_path, b_path = os.path.join(d, f"{p}_{s}_base.bin"), os.path.join(d, f"{p}_{s}_base-ends1.bin")
        if not (os.path.exists(a_path) and os.path.exists(b_path)):
            continue
        ha, A = load(a_path)
        hb, B = load(b_path)
        k = (ha["y100"] - ha["y0"]) / 100.0
        n = A["top"].size
        dt = A["top"].astype(int) - B["top"].astype(int)
        def c(D, bit):
            return int(((D["flags"] & bit) > 0).sum())
        def trench(D):
            want = ha["y0"] + D["fine"].astype(np.float64) * k
            cut = want - D["top"]
            return int(((cut >= 8) & ((D["flags"] & 16) == 0) & (D["fine"] > 2)).sum())
        print(f"{p:18s} {s:5d} {n:7d} {int((dt != 0).sum()):11d} {int(dt.max()) if dt.size else 0:9d} "
              f"{c(A,1):7d}/{c(B,1):7d} {c(A,2):7d}/{c(B,2):7d} {c(A,64):7d}/{c(B,64):7d} {trench(A):6d}/{trench(B):6d}")
