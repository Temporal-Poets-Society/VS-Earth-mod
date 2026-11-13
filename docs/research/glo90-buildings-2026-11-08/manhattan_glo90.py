"""Read Earth's GLO-90 tiles (EGLO) under Manhattan and compare relief in built-up and open areas."""
import os, struct, sys
import brotli
import numpy as np

STORE = os.path.join(os.environ["APPDATA"], "VintagestoryData", "ModData", "earth", "glo90")
MAXROW = 480  # rows from the north edge to decode (41.0 -> 40.6 N)


def decode(name, maxrow=MAXROW):
    b = open(os.path.join(STORE, name + ".egl"), "rb").read()
    assert b[:4] == b"EGLO" and b[4] == 1
    o = 5
    src, conv, latS, lonW, cols, rows, mn, mx = struct.unpack_from("<BHhhHHhh", b, o); o += struct.calcsize("<BHhhHHhh")
    o += 8  # source bytes
    el = b[o]; o += 1 + el
    o += 8  # checksum
    ln, = struct.unpack_from("<i", b, o); o += 4
    planes = brotli.decompress(b[o:o + ln])
    n = cols * rows
    z = np.frombuffer(planes[:n], np.uint8).astype(np.int32) | (np.frombuffer(planes[n:2 * n], np.uint8).astype(np.int32) << 8)
    d = (z >> 1) ^ -(z & 1)
    d = d.reshape(rows, cols)
    h = np.zeros((maxrow, cols), np.int32)
    for r in range(maxrow):
        prev = h[r - 1] if r > 0 else np.zeros(cols, np.int32)
        row = h[r]
        for c in range(cols):
            w = row[c - 1] if c > 0 else 0
            nn = prev[c] if r > 0 else 0
            nw = prev[c - 1] if (r > 0 and c > 0) else 0
            lo, hi = (w, nn) if w < nn else (nn, w)
            p = lo if nw >= hi else hi if nw <= lo else w + nn - nw
            v = p + d[r, c]
            v = ((v + 32768) & 0xFFFF) - 32768
            row[c] = v
    return latS, lonW, cols, h


tiles = {}
for nm in ("N40W074", "N40W075"):
    tiles[nm] = decode(nm)
    print(nm, "decoded", file=sys.stderr)


def at(lat, lon):
    lonW = int(np.floor(lon))
    latS, lw, cols, h = tiles[("N40W%03d" % -lonW)]
    r = int(round((latS + 1 - lat) * 1200)); c = int(round((lon - lw) * cols))
    return h[r, c]


def box(lat0, lat1, lon0, lon1):
    vals = []
    for lat in np.arange(lat0, lat1, 1 / 1200):
        row = [at(lat, lon) for lon in np.arange(lon0, lon1, 1 / 1200)]
        vals.append(row)
    return np.array(vals, float)


pts = [
    ("Chrysler / Grand Central (player)", 40.7516, -73.9755),
    ("Empire State Building", 40.7484, -73.9857),
    ("One World Trade Center", 40.7130, -74.0132),
    ("Central Park, Great Lawn", 40.7813, -73.9665),
    ("Central Park, Sheep Meadow", 40.7718, -73.9750),
    ("Washington Square (low-rise)", 40.7308, -73.9973),
    ("Park Slope, Brooklyn (low-rise)", 40.6720, -73.9780),
]
print("Points (GLO-90 height, m EGM2008):")
for nm, la, lo in pts:
    print(f"  {nm:36s} {at(la, lo):5d}")

boxes = [
    ("Midtown 40.745-40.765, 73.995-73.970W", 40.745, 40.765, -73.995, -73.970),
    ("Financial District 40.702-40.712, 74.015-74.004W", 40.702, 40.712, -74.015, -74.004),
    ("Central Park interior 40.775-40.790, 73.970-73.960W", 40.775, 40.790, -73.970, -73.960),
    ("Greenwich Village 40.728-40.738, 74.005-73.995W", 40.728, 40.738, -74.005, -73.995),
    ("Flatbush, Brooklyn 40.640-40.660, 73.965-73.945W", 40.640, 40.660, -73.965, -73.945),
]
print("\nBoxes: min / p10 / median / p90 / max, and mean |step| between neighbouring 90 m pixels")
for nm, a, b_, c, d_ in boxes:
    v = box(a, b_, c, d_)
    steps = np.concatenate([np.abs(np.diff(v, axis=0)).ravel(), np.abs(np.diff(v, axis=1)).ravel()])
    q = np.percentile(v, [0, 10, 50, 90, 100])
    print(f"  {nm:58s} {q[0]:5.0f} {q[1]:5.0f} {q[2]:5.0f} {q[3]:5.0f} {q[4]:5.0f}   step {steps.mean():4.1f} m  (n={v.size})")
