"""EA-0131: the streams of a streams-glo90 file near a place (decodes the tiles round it).
usage: python -I tools/streams/list_tile.py <streams.bin> <lat> <lon> [radius km]"""
import math, struct, sys
import brotli

f = open(sys.argv[1], "rb").read()
lat, lon = float(sys.argv[2]), float(sys.argv[3])
rad = float(sys.argv[4]) if len(sys.argv) > 4 else 10
unit, deg, tx, ty = struct.unpack_from("<iiii", f, 8)
states = f[24:24 + tx * ty]
index, o = {}, 24 + tx * ty
for t in range(tx * ty):
    if states[t]:
        index[t] = struct.unpack_from("<ii", f, o); o += 8

def decode(t):
    off, ln = index[t]
    b = brotli.decompress(f[off:off + ln]); o = [0]
    def var():
        v = s = 0
        while True:
            x = b[o[0]]; o[0] += 1; v |= (x & 0x7f) << s; s += 7
            if x < 0x80: return v
    def zig():
        v = var(); return (v >> 1) ^ -(v & 1)
    lat0, lon0 = t // tx - 90, t % tx - 180
    systems = []
    for _ in range(var()):
        kind, nr = var(), var()
        rs = []
        for r in range(nr):
            q, nx, nv = var() / 100, var() - 1, var()
            x = y = 0; pts = []
            for k in range(nv):
                x += zig(); y += zig(); pts.append((lat0 + y / unit, lon0 + x / unit))
            lv = 0; levs = []
            for k in range(nv):
                lv += zig(); levs.append(lv)
            rs.append((q, nx, pts, levs))
        systems.append((kind, rs))
    vis = [(zig(), zig()) for _ in range(var())]
    return systems, vis

t = (int(math.floor(lat)) + 90) * tx + (int(math.floor(lon)) + 180)
print("tile", t, "has data" if t in index else "no data")
if t in index:
    systems, vis = decode(t)
    homes = [t] + [((t // tx + dy) * tx + (t % tx + dx) % tx) for dx, dy in vis]
    print("visitors", vis)
    for h in homes:
        if h not in index: continue
        ss, _ = decode(h)
        for si, (kind, rs) in enumerate(ss):
            for ri, (q, nx, pts, levs) in enumerate(rs):
                d = min(math.hypot((p[0] - lat) * 111.2, (p[1] - lon) * 111.2 * math.cos(math.radians(lat))) for p in pts)
                if d <= rad:
                    print(f"home {h} system {si} kind {kind} reach {ri} Q {q} next {nx} {len(pts)} vertices, nearest {d:.1f} km, from {pts[0][0]:.4f},{pts[0][1]:.4f} to {pts[-1][0]:.4f},{pts[-1][1]:.4f}, levels {levs[0]}..{levs[-1]}")
