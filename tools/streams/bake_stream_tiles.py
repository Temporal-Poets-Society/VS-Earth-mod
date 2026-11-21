"""EA-0131 (decision 0061): streams-glo90-<n>.bin, the streams layer's tiles (src/Core/StreamData.cs has the layout).

From tools/RiverSnap's output on build_stream_input.py's file (the streams first, then the frozen large rivers): the
streams grouped into systems (each tree down to its outlet), each stored in the 1° tile of its outlet; each tile lists the
home tiles whose systems come within MARGIN of it. An outlet's kind: a large river (its downstream reach is one), the sea
or a sink (HydroRIVERS' NEXT_DOWN 0), else the end of the data (its downstream reach was left out).

usage: python -I tools/streams/bake_stream_tiles.py <input.bin> <snapped.bin> <hydrorivers-attrs.npz> <out.bin>
"""
import json, os, struct, sys, time
from collections import defaultdict

import brotli
import numpy as np

UNIT = 100000          # vertex units per degree (1.1 m north-south)
MARGIN_M = 1500.0      # a system reaches into a tile when within this of it (its banks and valley, with room)
t0 = time.time()


def log(m):
    print(f"[{time.time() - t0:7.1f}s] {m}", flush=True)


def read(path, count=None):
    raw = open(path, "rb").read()
    assert raw[:4] == b"EMRV" and struct.unpack_from("<i", raw, 4)[0] == 2, path
    n = struct.unpack_from("<i", raw, 8)[0] if count is None else count
    b = brotli.decompress(raw[12:])
    out, o = [], 0
    dt = np.dtype([("lon", "<f4"), ("lat", "<f4"), ("lev", "<i2"), ("fixed", "u1")])
    for i in range(n):
        q, nx, nv = struct.unpack_from("<fii", b, o)
        o += 12
        out.append((q, nx, np.frombuffer(b, dtype=dt, count=nv, offset=o)))
        o += 11 * nv
    return out


def var(buf, v):
    assert v >= 0
    while v >= 0x80:
        buf.append((v & 0x7f) | 0x80)
        v >>= 7
    buf.append(v)


def zig(buf, v):
    var(buf, (v << 1) if v >= 0 else ((-v) << 1) - 1)


inp_path, snap_path, attrs_path, out = sys.argv[1:5]
if os.path.exists(out):
    sys.exit(out + " exists; not replaced")
meta = json.load(open(inp_path + ".json"))
N = meta["frozenfrom"]
ids = np.load(inp_path + ".ids.npy")
assert len(ids) == N
inp = read(inp_path, N)
snp = read(snap_path, N)
log(f"{N} streams; {sum(len(r[2]) for r in snp)} snapped vertices (input {sum(len(r[2]) for r in inp)})")
h = np.load(attrs_path)
order = np.argsort(h["id"])
nd = h["next"][order][np.searchsorted(h["id"][order], ids)]

# systems: each reach's outlet (the reaches are in flow order: walk down from the end)
nxt = np.array([r[1] for r in inp], np.int64)
outlet = np.empty(N, np.int64)
for i in range(N - 1, -1, -1):
    outlet[i] = outlet[nxt[i]] if 0 <= nxt[i] < N else i
members = defaultdict(list)
for i in range(N):
    members[int(outlet[i])].append(i)
log(f"{len(members)} systems")


def tile_of(lat, lon):
    r = int(np.floor(lat + 90)); c = int(np.floor(((lon + 180) % 360)))
    return min(max(r, 0), 179) * 360 + min(max(c, 0), 359)


home = defaultdict(list)       # tile -> outlets
visitors = defaultdict(set)    # tile -> home tiles
kinds = defaultdict(int)
for o_, mem in members.items():
    last = snp[o_][2]
    ht = tile_of(float(last["lat"][-1]), float(last["lon"][-1]))
    home[ht].append(o_)
    lat = np.concatenate([snp[i][2]["lat"] for i in mem]).astype(np.float64)
    lon = np.concatenate([snp[i][2]["lon"] for i in mem]).astype(np.float64)
    # (longitudes unwrapped about the outlet: a system across the antimeridian)
    lon = float(last["lon"][-1]) + ((lon - float(last["lon"][-1]) + 180) % 360 - 180)
    mlat = MARGIN_M / 111195.0
    mlon = mlat / max(0.05, np.cos(np.radians(min(89.0, float(np.abs(lat).max())))))
    r0, r1 = int(np.floor(lat.min() - mlat + 90)), int(np.floor(lat.max() + mlat + 90))
    c0, c1 = int(np.floor(lon.min() - mlon + 180)), int(np.floor(lon.max() + mlon + 180))
    hr, hc = ht // 360, ht % 360
    for r in range(max(0, r0), min(179, r1) + 1):
        for c in range(c0, c1 + 1):
            t = r * 360 + c % 360
            if t != ht:
                visitors[t].add(ht)

tiles = sorted(set(home) | set(visitors))
bodies = {}
stats = {"systems": len(members), "reaches": N, "vertices": 0, "tiles": len(tiles), "end_kinds": {}}
for t in tiles:
    buf = bytearray()
    row, col = t // 360, t % 360
    lat0, lon0 = row - 90, col - 180
    outs = sorted(home.get(t, []))
    var(buf, len(outs))
    for o_ in outs:
        mem = members[o_]
        local = {g: k for k, g in enumerate(mem)}
        nx = int(nxt[o_])
        kind = 1 if nx >= N else 2 if nd[o_] == 0 else 0
        kinds[kind] += 1
        var(buf, kind)
        var(buf, len(mem))
        for g in mem:
            q, _, a = snp[g]
            var(buf, int(round(q * 100)))
            ng = int(nxt[g])
            var(buf, local[ng] + 1 if 0 <= ng < N and g != o_ else 0)
            var(buf, len(a))
            stats["vertices"] += len(a)
            px, py = 0, 0
            for k in range(len(a)):
                lon = float(a["lon"][k]); lat = float(a["lat"][k])
                lon = lon0 + ((lon - lon0 + 180) % 360 - 180) if abs(lon - lon0) > 180 else lon
                x = int(round((lon - lon0) * UNIT)); y = int(round((lat - lat0) * UNIT))
                zig(buf, x - px); zig(buf, y - py)
                px, py = x, y
            pl = 0
            for k in range(len(a)):
                lv = int(a["lev"][k])
                zig(buf, lv - pl)
                pl = lv
    vs = sorted(visitors.get(t, []))
    var(buf, len(vs))
    for ht in vs:
        dy = ht // 360 - row
        dx = (ht % 360 - col + 180) % 360 - 180
        zig(buf, dx); zig(buf, dy)
    bodies[t] = brotli.compress(bytes(buf), quality=11, lgwin=22)
stats["end_kinds"] = {"free": kinds[0], "river": kinds[1], "sea_or_sink": kinds[2]}
log(f"{len(tiles)} tiles encoded")

states = bytearray(360 * 180)
for t in tiles:
    states[t] = 1
head = b"EMST" + struct.pack("<iiiii", 1, UNIT, 1, 360, 180)
index = bytearray()
o = len(head) + len(states) + 8 * len(tiles)
for t in tiles:
    index += struct.pack("<ii", o, len(bodies[t]))
    o += len(bodies[t])
with open(out, "xb") as f:
    f.write(head); f.write(states); f.write(index)
    for t in tiles:
        f.write(bodies[t])
stats["bytes"] = os.path.getsize(out)
json.dump(stats, open(out + ".json", "w"), indent=1)
log(f"{out}: {stats}")
