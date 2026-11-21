"""EA-0131: how far each snapped stream reach lies from its HydroRIVERS line (largest vertex distance, m), worst first.
usage: python -I tools/streams/compare_snap.py <input.bin> <snapped.bin> <stream count> [top]"""
import struct, sys
import brotli
import numpy as np

def read(path, n):
    raw = open(path, "rb").read()
    b = brotli.decompress(raw[12:])
    out, o = [], 0
    for i in range(n):
        q, nx, nv = struct.unpack_from("<fii", b, o); o += 12
        a = np.frombuffer(b, dtype=[("lon", "<f4"), ("lat", "<f4"), ("lev", "<i2"), ("fixed", "u1")], count=nv, offset=o); o += 11 * nv
        out.append((q, nx, a))
    return out

n = int(sys.argv[3]); top = int(sys.argv[4]) if len(sys.argv) > 4 else 20
A, B = read(sys.argv[1], n), read(sys.argv[2], n)
M = 111195.0
res = []
for i in range(n):
    a, b = A[i][2], B[i][2]
    cl = np.cos(np.radians(float(a["lat"].mean())))
    ax, ay = a["lon"].astype(float) * cl * M, a["lat"].astype(float) * M
    bx, by = b["lon"].astype(float) * cl * M, b["lat"].astype(float) * M
    # distance from each snapped vertex to the original polyline
    worst = 0.0
    for x, y in zip(bx, by):
        px, py = ax[:-1], ay[:-1]; dx, dy = ax[1:] - px, ay[1:] - py
        l2 = np.maximum(dx * dx + dy * dy, 1e-9)
        t = np.clip(((x - px) * dx + (y - py) * dy) / l2, 0, 1)
        d = np.sqrt((px + t * dx - x) ** 2 + (py + t * dy - y) ** 2).min() if len(px) else 0
        worst = max(worst, d)
    lev = b["lev"]
    res.append((worst, i, A[i][0], float(b["lat"][0]), float(b["lon"][0]), int(lev[0]), int(lev[-1])))
res.sort(reverse=True)
w = np.array([r[0] for r in res])
print(f"{n} reaches: worst offset p50 {np.percentile(w, 50):.0f} m, p90 {np.percentile(w, 90):.0f}, p99 {np.percentile(w, 99):.0f}, max {w.max():.0f}")
for r in res[:top]:
    print(f"  reach {r[1]:6} Q {r[2]:5.1f} offset {r[0]:6.0f} m at start {r[3]:.4f},{r[4]:.4f} levels {r[5]}..{r[6]}")
