"""EA-0147: do a lake outlines file's pieces meet along the 1° tiles' edges? For every pair of tiles sharing a side, the
stretches of that side the two tiles' lakes run along (cut edges: both ends on the side), as unions; where they differ,
a piece ends on the edge that its neighbour doesn't continue (a straight step on World Map's chart).

Reported apart for the sides where GLO-90 changes its column spacing (the parallels 50, 60, 70, 80 and 85 north and south),
where each tile resamples the other's cells and small differences remain. Lengths in vertex units (2400 a degree: 2 a 3"
row; a column is 3 units at 50-60 N, 4 at 60-70 N).

Usage: python -I tools/check_lake_edges.py FILE [FILE ...]
"""
import struct, sys

import brotli

BANDS = (50, 60, 70, 80, 85)


def varint(s, i):
    v = sh = 0
    while True:
        c = s[i]; i += 1
        v |= (c & 0x7F) << sh; sh += 7
        if c < 0x80:
            return v, i


def unzz(v):
    return (v >> 1) ^ -(v & 1)


def union(iv):
    iv = sorted(i for i in iv if i[1] > i[0])
    res = []
    for a, c in iv:
        if res and a <= res[-1][1]:
            res[-1][1] = max(res[-1][1], c)
        else:
            res.append([a, c])
    return res


def length(u):
    return sum(c - a for a, c in u)


def inter(u, v):
    i = j = tot = 0
    while i < len(u) and j < len(v):
        a, c = max(u[i][0], v[j][0]), min(u[i][1], v[j][1])
        if c > a:
            tot += c - a
        if u[i][1] < v[j][1]:
            i += 1
        else:
            j += 1
    return tot


def check(path):
    b = open(path, "rb").read()
    assert b[:4] in (b"EMLO", b"EMRO")   # (EA-0148: river outlines have the same layout)
    version, unit, tile_deg, tx, ty = struct.unpack_from("<5i", b, 4)
    states = b[24:24 + tx * ty]
    off = 24 + tx * ty
    index = {}
    for t in range(tx * ty):
        if states[t] == 1:
            o, n = struct.unpack_from("<2i", b, off); off += 8
            index[t] = (o, n)
    side = unit * tile_deg
    cache = {}

    def edges(t):
        if t in cache:
            return cache[t]
        out = {"S": [], "N": [], "W": [], "E": []}
        if t in index:
            o, n = index[t]
            s = brotli.decompress(b[o:o + n]); i = 0
            lakes, i = varint(s, i)
            for _ in range(lakes):
                _, i = varint(s, i); _, i = varint(s, i)
                rings, i = varint(s, i)
                x = y = 0
                for _ in range(rings):
                    cnt, i = varint(s, i)
                    pts = []
                    for _ in range(cnt):
                        dx, i = varint(s, i); dy, i = varint(s, i)
                        x += unzz(dx); y += unzz(dy); pts.append((x, y))
                    for k in range(cnt):
                        (x0, y0), (x1, y1) = pts[k], pts[(k + 1) % cnt]
                        if y0 == y1 == 0: out["S"].append((min(x0, x1), max(x0, x1)))
                        elif y0 == y1 == side: out["N"].append((min(x0, x1), max(x0, x1)))
                        elif x0 == x1 == 0: out["W"].append((min(y0, y1), max(y0, y1)))
                        elif x0 == x1 == side: out["E"].append((min(y0, y1), max(y0, y1)))
        out = {k: union(v) for k, v in out.items()}
        if len(cache) > 4000:
            cache.clear()
        cache[t] = out
        return out

    stats = {"same band": [0, 0, 0, 0], "band change": [0, 0, 0, 0]}   # sides with lakes, sides that differ, units differing, units
    for t in sorted(index):
        r, c = divmod(t, tx)
        lat = r * tile_deg - 90
        here = edges(t)
        for nb, mine, theirs, kind in (((r, (c + 1) % tx), "E", "W", "same band"),
                                       ((r + 1, c), "N", "S", "band change" if (lat + 1) in BANDS or -(lat + 1) in BANDS else "same band")):
            if nb[0] >= ty:
                continue
            u = nb[0] * tx + nb[1]
            if states[u] == 255:
                continue
            a, o = here[mine], edges(u)[theirs]
            if not a and not o:
                continue
            s = stats[kind]
            s[0] += 1
            d = length(a) + length(o) - 2 * inter(a, o)
            if d:
                s[1] += 1
            s[2] += d
            s[3] += length(a) + length(o)
    print(f"{path}: version {version}")
    for kind, (n, nd, ud, ut) in stats.items():
        print(f"  {kind:11s}: {n:6d} shared sides with lake cuts, {nd:6d} where the two tiles' cuts differ; "
              f"{ud:8d} of {ut:9d} units of cut on one side only ({100.0 * ud / max(ut, 1):.2f} %)")


if __name__ == "__main__":
    for p in sys.argv[1:]:
        check(p)
