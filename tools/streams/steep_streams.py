"""EA-0131: the steepest snapped streams (largest fall over a short distance), to probe the drawing where it is hardest.
usage: python -I tools/streams/steep_streams.py <input.bin> <snapped.bin> [window m] [top] [lat0,lat1,lon0,lon1]"""
import json, math, struct, sys
import brotli
import numpy as np

inp, snp = sys.argv[1], sys.argv[2]
win = float(sys.argv[3]) if len(sys.argv) > 3 else 500
top = int(sys.argv[4]) if len(sys.argv) > 4 else 30
box = [float(v) for v in sys.argv[5].split(",")] if len(sys.argv) > 5 else None
N = json.load(open(inp + ".json"))["frozenfrom"]
raw = open(snp, "rb").read()
b = brotli.decompress(raw[12:])
dt = np.dtype([("lon", "<f4"), ("lat", "<f4"), ("lev", "<i2"), ("fixed", "u1")])
o = 0
res = []
for i in range(N):
    q, nx, nv = struct.unpack_from("<fii", b, o)
    o += 12
    a = np.frombuffer(b, dtype=dt, count=nv, offset=o)
    o += 11 * nv
    if nv < 2:
        continue
    lat, lon, lev = a["lat"].astype(float), a["lon"].astype(float), a["lev"].astype(float)
    if box and not ((lat >= box[0]) & (lat <= box[1]) & (lon >= box[2]) & (lon <= box[3])).any():
        continue
    cl = math.cos(math.radians(float(lat.mean())))
    s = np.concatenate([[0], np.cumsum(np.hypot(np.diff(lon) * cl, np.diff(lat)) * 111195.0)])
    # the largest fall within win metres along the reach
    best, at = 0.0, 0
    j = 0
    for k in range(nv):
        while s[k] - s[j] > win:
            j += 1
        f = lev[j] - lev[k]
        if f > best:
            best, at = f, k
    res.append((best, i, q, float(lat[at]), float(lon[at]), s[-1], lev[0] - lev[-1]))
res.sort(reverse=True)
falls = np.array([r[0] for r in res])
print(f"{len(res)} reaches; largest fall within {win:.0f} m: p50 {np.percentile(falls, 50):.0f} m, p90 {np.percentile(falls, 90):.0f}, "
      f"p99 {np.percentile(falls, 99):.0f}, p99.9 {np.percentile(falls, 99.9):.0f}, max {falls.max():.0f}")
for r in res[:top]:
    print(f"  fall {r[0]:5.0f} m  reach {r[1]:7}  Q {r[2]:5.2f}  at {r[3]:.4f},{r[4]:.4f}  length {r[5] / 1000:.1f} km, whole fall {r[6]:.0f} m")
