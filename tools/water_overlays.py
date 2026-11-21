"""Water atlas data as overlays for the tall line (Earth 0053 History, tall.14).

release/1.0's water atlas (717fb18, tools/sea_referee.py and tools/snap_rivers_wbm.py) rewrote earthdata/terrain.bin,
islands.bin and coasts.bin in place, and made a snapped copy of the tall line's rivers-glo90.bin. The tall line keeps the
published data packs (earthterraindata, earthwaterdata 1.0.2, earthheightdata4 1.0.3-tall.5) for older worlds and ships
the differences in the main mod, read only by worlds that save the new versions:

  earthdata/sea-referee.bin (EMSR, EarthParams.SeaReferee): the terrain grid cells the referee changed, the island grid
      patches it changed or removed, the new island list (old islands by index, new ones in full) and the coast tiles it
      changed. Version 2 (tall.15) is made from tools/sea_ocean.py's output (the referee with the coastline the water
      mask's) and stores changed islands and coast tiles as pieces: stretches of the base outlines and new vertices.
  earthdata/rivers-glo90-snap.bin (EMRP, RiverCourses 2): the reaches the snap moved, in rivers.bin's record format.
  earthdata/rivers-glo90-arms.bin (EMRA, RiverCourses 3; tall.16): release/1.0's side arms (tools/river_arms.py, e5d1d75)
      built on the snapped rivers: where each reach is split (an arm joins there) and the arms' records. The arms come
      first in the list and each reach is followed by its later pieces, so the numbering follows from these alone.

  earthdata/rivers-glo90-snap-2.bin (EMRP version 2, RiverCourses 4; 0063): tools/snap_rivers_channel.py's reaches (every
      reach from 20 m³/s moved onto the mask's water, smoothed) with the mask's half width at each vertex (record
      version 3), the moved ones' inner vertices as steps on a 1/65536 degree grid, and the widths alone of the reaches
      it left where they were.
  earthdata/rivers-glo90-arms-2.bin (EMRA version 2, RiverCourses 4): river_arms.py on those, the arms' records version 3.

  earthdata/rivers-glo90-snap-3.bin and rivers-glo90-arms-3.bin (RiverCourses 5; decision 0092, EA-0148): the same
      layouts (EMRP version 2, EMRA version 2) from snap_rivers_channel.py and river_arms.py run on GLO-90's
      pixel-is-point grid (tools/glo90grid.py): the snapped courses lie on the mask's water where the heights have it.
Each file names its base files and its results by the first 8 bytes of their SHA-256, so the game applies it only to the
data it was made from (and knows when the data already has it). The arms' base is the snapped file.

  python -I tools/water_overlays.py --base <dir with the 1.0.2 terrain/islands/coasts and rivers-glo90.bin>
      [--referee <dir with the refereed terrain/islands/coasts (tools/sea_ocean.py --write --out)>]
      [--snapped <snapped rivers-glo90.bin> [--armed <river_arms.py's output on it>]]
      [--snapped2 <snap_rivers_channel.py's output> [--armed2 <river_arms.py's output on it>]]
      [--snapped3 <the same on the pixel-is-point grid> [--armed3 <river_arms.py's output on it>]] --out earthdata
  python -I tools/water_overlays.py ... --verify      (rebuild the targets from base + overlays and compare)
"""
import argparse, hashlib, json, math, os, struct, sys

import brotli
import numpy as np

REFEREE_VERSION = 2   # SeaRefereeData.FileVersion: 1 the referee (tall.14), 2 with the missing sea opened (tall.15)
ARMS_VERSION = 1      # RiverArmsData: rivers-glo90-arms.bin (record version 2)
ARMS2_VERSION = 2     # rivers-glo90-arms-2.bin (0063, courses rules 4: record version 3 with widths)
SNAP2_VERSION = 2     # RiverSnapData: rivers-glo90-snap-2.bin (0063: moved reaches with widths, widths of the others)
GRID = 65536          # snap-2's moved courses' inner vertices lie on a 1/GRID degree grid (1.7 m north-south; k / GRID is
                      # exact in float32 for |k| < 2^24, all longitudes): stored as whole-number steps, a third the size


def on_grid(x):
    """Degrees on the GRID (from their float32 value, so a course written and read again grids the same)."""
    return np.floor(np.asarray(x, np.float32).astype(np.float64) * GRID + 0.5) / GRID


def h8(b):
    return hashlib.sha256(b).digest()[:8]


def varint(v):
    out = bytearray()
    while True:
        x = v & 0x7F
        v >>= 7
        if v:
            out.append(x | 0x80)
        else:
            out.append(x)
            return bytes(out)


def zz(v):
    return (v << 1) ^ (v >> 63)


class Reader:
    def __init__(self, b, o=0):
        self.b, self.o = b, o

    def varint(self):
        v = shift = 0
        while True:
            x = self.b[self.o]; self.o += 1
            v |= (x & 0x7F) << shift
            if not x & 0x80:
                return v
            shift += 7

    def unzz(self):
        v = self.varint()
        return (v >> 1) ^ -(v & 1)

    def take(self, n):
        s = self.b[self.o:self.o + n]; self.o += n
        return s


# ---- grid patches (islands.bin, lakes.bin, outlets.bin): cell deltas, elevation codes, levels, flags

def read_patches(r, n):
    cells = np.empty(n, dtype=np.int64)
    c = 0
    for i in range(n):
        c += r.varint(); cells[i] = c
    elev = np.frombuffer(r.take(2 * n), dtype="<i2").copy()
    level = np.frombuffer(r.take(2 * n), dtype="<i2").copy()
    flags = np.frombuffer(r.take(n), dtype=np.uint8).copy()
    return cells, elev, level, flags


def write_patches(cells, elev, level, flags):
    out = bytearray()
    prev = 0
    for c in cells.tolist():
        out += varint(c - prev); prev = c
    out += elev.astype("<i2").tobytes() + level.astype("<i2").tobytes() + flags.astype(np.uint8).tobytes()
    return bytes(out)


# ---- terrain.bin (EMTR v2): tiles of t×t cells, 7 bytes a cell (elevation code and ice Paeth-coded)

def terrain_tiles(f):
    assert f[:4] == b"EMTR"
    ver, w, h, t, count = struct.unpack_from("<iiiii", f, 4)
    assert ver == 2
    idx = np.frombuffer(f, dtype="<i4", count=2 * count, offset=24).reshape(-1, 2)
    return (w, h, t, count), idx


def decode_tile(blob, t):
    raw = np.frombuffer(brotli.decompress(blob), dtype=np.uint8)
    n = t * t

    def unpaeth(off):
        d = raw[off:off + 2 * n].view("<i2").astype(np.int64).reshape(t, t)
        return d.cumsum(0).cumsum(1)
    return (unpaeth(0), raw[2 * n:4 * n].view("<i2").reshape(t, t).astype(np.int64),
            raw[4 * n:5 * n].reshape(t, t).astype(np.int64), unpaeth(5 * n))


def terrain_diff(fa, fb):
    la, ia = terrain_tiles(fa)
    lb, ib = terrain_tiles(fb)
    assert la == lb, (la, lb)
    w, h, t, count = la
    tx = (w + t - 1) // t
    cells, elev, level, flags = [], [], [], []
    for k in range(count):
        a = fa[ia[k, 0]:ia[k, 0] + ia[k, 1]]
        b = fb[ib[k, 0]:ib[k, 0] + ib[k, 1]]
        if a == b:
            continue
        ea, la_, ga, ica = decode_tile(a, t)
        eb, lb_, gb, icb = decode_tile(b, t)
        if (ica != icb).any():
            raise SystemExit(f"terrain tile {k}: the ice changed, which grid patches can't carry")
        diff = (ea != eb) | (la_ != lb_) | (ga != gb)
        r0, c0 = (k // tx) * t, (k % tx) * t
        for rr, cc in zip(*np.nonzero(diff)):
            cells.append((r0 + rr) * w + c0 + cc)
            elev.append(eb[rr, cc]); level.append(lb_[rr, cc]); flags.append(gb[rr, cc])
    order = np.argsort(np.array(cells, dtype=np.int64), kind="stable")
    arr = lambda xs, dt: np.array(xs, dtype=dt)[order]
    return w, arr(cells, np.int64), arr(elev, np.int64), arr(level, np.int64), arr(flags, np.int64)


# ---- islands.bin (EMIS v1)

def islands_read(f):
    assert f[:4] == b"EMIS"
    ver, w, npatch, nisl = struct.unpack_from("<iiii", f, 4)
    assert ver == 1
    r = Reader(brotli.decompress(f[20:]))
    patches = read_patches(r, npatch)
    area = np.frombuffer(r.take(4 * nisl), dtype="<f4")
    height = np.frombuffer(r.take(4 * nisl), dtype="<f4")
    width = np.frombuffer(r.take(4 * nisl), dtype="<f4")
    count = np.frombuffer(r.take(4 * nisl), dtype="<i4")
    qx = qy = 0
    isl = []
    for i in range(nisl):
        pts = []
        for _ in range(int(count[i])):
            qx += r.unzz(); qy += r.unzz()
            pts.append((qx, qy))
        isl.append((area[i].tobytes(), height[i].tobytes(), width[i].tobytes(), tuple(pts)))
    assert r.o == len(r.b), "islands.bin: trailing data"
    return w, patches, isl


# ---- coasts.bin (EMCO v1): 1° tiles, a state byte each, brotli edges for tiles with edges

def coasts_read(f):
    assert f[:4] == b"EMCO"
    ver, unit, deg, tx, ty, margin = struct.unpack_from("<iiiiii", f, 4)
    assert ver == 1
    n = tx * ty
    st = f[28:28 + n]
    o = 28 + n
    tiles = {}
    for i in range(n):
        if st[i] == 255 or not st[i] & 2:
            continue
        off, ln = struct.unpack_from("<ii", f, o); o += 8
        tiles[i] = f[off:off + ln]
    return st, tiles


def unvarints(b):
    a = np.frombuffer(b, np.uint8)
    if a.size == 0:
        return np.zeros(0, np.int64)
    end = (a & 0x80) == 0
    grp = np.concatenate([[0], np.cumsum(end)[:-1]])
    start = np.concatenate([[0], np.flatnonzero(end)[:-1] + 1])
    pos = np.arange(a.size) - start[grp]
    return np.add.reduceat((a & 0x7F).astype(np.int64) << (7 * pos), start)


def coast_runs(blob):
    """A coast tile's runs as (n, 2) int64 arrays of vertices in units from the tile's south-west corner."""
    v = unvarints(brotli.decompress(blob))
    nr = int(v[0])
    counts = v[1:1 + nr]
    z = v[1 + nr:]
    d = ((z >> 1) ^ -(z & 1)).reshape(-1, 2)
    out, s = [], 0
    for c in counts.tolist():
        out.append(np.cumsum(d[s:s + c], axis=0))
        s += c
    return out


# ---- coast tiles as copies (sea-referee.bin version 2): each run of a changed tile is a list of pieces, a stretch of
# one of the base tile's runs (forwards or backwards, MIN_COPY vertices or more) or literal vertices, so a tile changed in
# one place costs that place and not the whole tile. Run: piece count, then per piece 0, n, n vertices (zigzag deltas
# from the vertex before, the run's first from the tile's corner) or k+1, start, 2*length+backwards (base run k).

MIN_COPY = 3
ISLAND_CELL = 10000   # island units (1e-5°) per cell of the index of old islands that new ones copy from (0.1°)


def point_index(sources):
    """{vertex: (source, position)} over (id, (n, 2) array) sources, the first place a vertex occurs."""
    where = {}
    for sid, r in sources:
        for j, p in enumerate(map(tuple, r.tolist())):
            where.setdefault(p, (sid, j))
    return where


def encode_pieces(pts, src, where):
    """A vertex list as pieces: stretches (MIN_COPY or more) of the sources src[id] and literal vertices."""
    pieces, lit, i = [], [], 0
    while i < len(pts):
        best = None
        hit = where.get(pts[i])
        if hit is not None:
            sid, j = hit
            o = src[sid]
            for step in (1, -1):
                n = 0
                while (i + n < len(pts) and 0 <= j + step * n < len(o)
                       and o[j + step * n, 0] == pts[i + n][0] and o[j + step * n, 1] == pts[i + n][1]):
                    n += 1
                if best is None or n > best[3]:
                    best = (sid, j, step, n)
        if best is not None and best[3] >= MIN_COPY:
            if lit:
                pieces.append(lit); lit = []
            pieces.append(best)
            i += best[3]
        else:
            lit.append(pts[i]); i += 1
    if lit:
        pieces.append(lit)
    out = bytearray(varint(len(pieces)))
    px = py = 0
    for p in pieces:
        if isinstance(p, list):
            out += varint(0) + varint(len(p))
            for x, y in p:
                out += varint(zz(x - px)) + varint(zz(y - py)); px, py = x, y
        else:
            sid, j, step, n = p
            out += varint(sid + 1) + varint(j) + varint(2 * n + (step < 0))
            px, py = (int(v) for v in src[sid][j + step * (n - 1)])
    return bytes(out)


def decode_pieces(r, src):
    pts, px, py = [], 0, 0
    for _ in range(r.varint()):
        tag = r.varint()
        if tag == 0:
            for _ in range(r.varint()):
                px += r.unzz(); py += r.unzz(); pts.append((px, py))
        else:
            o, j, ln = src(tag - 1), r.varint(), r.varint()
            n, step = ln >> 1, -1 if ln & 1 else 1
            assert n >= 1 and 0 <= j < len(o) and 0 <= j + step * (n - 1) < len(o)
            for q in range(n):
                pts.append((int(o[j + step * q][0]), int(o[j + step * q][1])))
            px, py = pts[-1]
    return pts


def encode_coast_tile(old, new):
    src = dict(enumerate(old))
    where = point_index(src.items())
    out = bytearray(varint(len(new)))
    for r in new:
        out += encode_pieces(list(map(tuple, r.tolist())), src, where)
    return bytes(out)


def decode_coast_tile(r, old):
    return [np.array(decode_pieces(r, lambda k: old[k]), dtype=np.int64).reshape(-1, 2) for _ in range(r.varint())]


# ---- rivers.bin format (EMRV v2, or v3 with the mask's half width at each vertex): reach records

VERTEX_BYTES = {2: 11, 3: 12}


def river_version(f):
    assert f[:4] == b"EMRV"
    ver, = struct.unpack_from("<i", f, 4)
    assert ver in VERTEX_BYTES, ver
    return ver


def river_records(f):
    ver = river_version(f)
    vb = VERTEX_BYTES[ver]
    count, = struct.unpack_from("<i", f, 8)
    b = brotli.decompress(f[12:])
    o = 0
    recs = []
    for _ in range(count):
        s = o
        n, = struct.unpack_from("<i", b, o + 8)
        o += 12 + vb * n
        recs.append(b[s:o])
    assert o == len(b)
    return recs


def build_referee(base, ref):
    files = {k: (open(os.path.join(base, k), "rb").read(), open(os.path.join(ref, k), "rb").read())
             for k in ("terrain.bin", "islands.bin", "coasts.bin")}
    w, tc, te, tl, tf = terrain_diff(*files["terrain.bin"])
    wa, pa, ia = islands_read(files["islands.bin"][0])
    wb, pb, ib = islands_read(files["islands.bin"][1])
    assert wa == wb == w
    # island grid patches: removed cells, then the new or changed ones
    old = {int(c): (int(e), int(l), int(g)) for c, e, l, g in zip(*pa)}
    new = {int(c): (int(e), int(l), int(g)) for c, e, l, g in zip(*pb)}
    removed = sorted(set(old) - set(new))
    upsert = sorted(c for c in new if old.get(c) != new[c])
    # islands: each new one by its old index where it is unchanged, else in full: area, height, width and its outline as
    # pieces of the outlines of the old islands whose boxes it overlaps (old islands by index)
    by_key = {}
    for i, k in enumerate(ia):
        by_key.setdefault(k, []).append(i)
    old_pts = [np.array(k[3], dtype=np.int64).reshape(-1, 2) for k in ia]
    grid = {}
    for i, p in enumerate(old_pts):
        (x0, y0), (x1, y1) = p.min(0) // ISLAND_CELL, p.max(0) // ISLAND_CELL
        for gx in range(int(x0), int(x1) + 1):
            for gy in range(int(y0), int(y1) + 1):
                grid.setdefault((gx, gy), []).append(i)
    used = set()
    isl = bytearray(varint(len(ib)))
    prev, inline = -1, 0
    for k in ib:
        j = next((i for i in by_key.get(k, ()) if i not in used and i > prev), None)
        if j is not None:
            used.add(j)
            isl += varint(j - prev); prev = j
            continue
        inline += 1
        p = np.array(k[3], dtype=np.int64)
        (x0, y0), (x1, y1) = p.min(0) // ISLAND_CELL, p.max(0) // ISLAND_CELL
        near = {i for gx in range(int(x0), int(x1) + 1) for gy in range(int(y0), int(y1) + 1) for i in grid.get((gx, gy), ())}
        src = {i: old_pts[i] for i in near}
        isl += varint(0) + k[0] + k[1] + k[2] + encode_pieces(list(k[3]), src, point_index(src.items()))
    so, to = coasts_read(files["coasts.bin"][0])
    sn, tn = coasts_read(files["coasts.bin"][1])
    assert len(so) == len(sn)
    ctiles = [i for i in range(len(so)) if so[i] != sn[i] or to.get(i) != tn.get(i)]
    co = bytearray(varint(len(ctiles)))
    prev = 0
    for i in ctiles:
        co += varint(i - prev) + bytes([sn[i]])
        if i in tn:
            co += encode_coast_tile(coast_runs(to[i]) if i in to else [], coast_runs(tn[i]))
        prev = i
    payload = (varint(len(tc)) + write_patches(tc, te, tl, tf)
               + varint(len(removed)) + b"".join(varint(c - p) for c, p in zip(removed, [0] + removed[:-1]))
               + varint(len(upsert)) + write_patches(np.array(upsert, dtype=np.int64),
                                                     *(np.array([new[c][j] for c in upsert], dtype=np.int64) for j in range(3)))
               + bytes(isl) + bytes(co))
    head = b"EMSR" + struct.pack("<ii", REFEREE_VERSION, w)
    for k in ("terrain.bin", "islands.bin", "coasts.bin"):
        head += h8(files[k][0])
    for k in ("terrain.bin", "islands.bin", "coasts.bin"):
        head += h8(files[k][1])
    out = head + brotli.compress(payload, quality=11, lgwin=24)
    print(f"sea-referee.bin: {len(tc)} terrain cells; island patches {len(removed)} removed, {len(upsert)} new or changed; "
          f"islands {len(ia)} -> {len(ib)} ({inline} new or changed); coast tiles {len(ctiles)}; {len(out)} bytes")
    return out, files


def build_snap(base_rivers, snapped):
    fa, fb = open(base_rivers, "rb").read(), open(snapped, "rb").read()
    ra, rb = river_records(fa), river_records(fb)
    assert len(ra) == len(rb)
    ch = [i for i in range(len(ra)) if ra[i] != rb[i]]
    for i in ch:
        assert ra[i][:8] == rb[i][:8], f"reach {i}: discharge or next reach changed"
    payload = bytearray()
    prev = 0
    for i in ch:
        payload += varint(i - prev); prev = i
    for i in ch:
        payload += rb[i]
    out = b"EMRP" + struct.pack("<iii", 1, len(ra), len(ch)) + h8(fa) + h8(fb) + brotli.compress(bytes(payload), quality=11, lgwin=24)
    print(f"rivers-glo90-snap.bin: {len(ch)} of {len(ra)} reaches; {len(out)} bytes")
    return out, (fa, fb)


def split_widths(rec):
    """A record version 3 reach as its version 2 record and its widths."""
    n, = struct.unpack_from("<i", rec, 8)
    a = np.frombuffer(rec, np.uint8, 12 * n, 12).reshape(n, 12)
    return rec[:12] + a[:, :11].tobytes(), a[:, 11].tobytes()


V2 = np.dtype([("lon", "<f4"), ("lat", "<f4"), ("lev", "<i2"), ("fixed", "u1")])
V3 = np.dtype([("lon", "<f4"), ("lat", "<f4"), ("lev", "<i2"), ("fixed", "u1"), ("width", "u1")])


def varints_np(v):
    """Unsigned varints of a whole array at once."""
    v = np.asarray(v, np.uint64)
    nb = np.ones(v.size, np.int64)
    for k in range(1, 10):
        nb += v >= np.uint64(1 << (7 * k))
    pos = np.r_[0, np.cumsum(nb)[:-1]]
    out = np.zeros(int(nb.sum()), np.uint8)
    for k in range(int(nb.max()) if v.size else 0):
        m = nb > k
        out[pos[m] + k] = ((v[m] >> np.uint64(7 * k)) & np.uint64(0x7F)).astype(np.uint8) | np.where(nb[m] > k + 1, 0x80, 0).astype(np.uint8)
    return out.tobytes()


def zz_np(d):
    d = np.asarray(d, np.int64)
    return ((d << 1) ^ (d >> 63)).astype(np.uint64)


def unvarints_at(a, o, count):
    """count varints from byte o of the uint8 array a: (values as int64, the offset after them)."""
    if count == 0:
        return np.zeros(0, np.int64), o
    ends = o + np.flatnonzero(a[o:] < 0x80)[:count]
    assert len(ends) == count, "varints past the end"
    starts = np.r_[o, ends[:-1] + 1]
    ln = ends - starts + 1
    v = np.zeros(count, np.int64)
    for k in range(int(ln.max())):
        m = ln > k
        v[m] |= (a[starts[m] + k] & 0x7F).astype(np.int64) << (7 * k)
    return v, int(ends[-1]) + 1


def unzz_np(u):
    return (u >> 1) ^ -(u & 1)


def build_snap2(base_rivers, snapped):
    """rivers-glo90-snap-2.bin (0063, courses rules 4) from rivers-glo90.bin (record version 2) and
    tools/snap_rivers_channel.py's output on it (record version 3): the reaches it moved and the widths of the reaches it
    left where they have any. A moved reach keeps its discharge, next reach, first and last vertex (the first's level and
    both flags too; the last's level can be lower: carried levels never rise, and 11 base reaches rise at their end) and
    its inner vertices lie on the GRID. EMRP version 2: the version 1 header (base count, changed count, base and target
    hashes), the count of width-only reaches and GRID, then brotli: the changed and the width-only reaches' index deltas
    (varints); per changed reach its vertex count; their inner vertices' longitudes in GRID steps, then latitudes (zigzag
    varint deltas, the first from the base's first vertex floored onto the grid); the levels of every vertex after the
    first (zigzag deltas from the first's); the inner vertices' lake/sea flags (a byte each); the changed reaches' widths,
    a byte per vertex, then the width-only reaches'."""
    fa, fb = open(base_rivers, "rb").read(), open(snapped, "rb").read()
    assert river_version(fa) == 2 and river_version(fb) == 3
    ra, rb = river_records(fa), river_records(fb)
    assert len(ra) == len(rb)
    ch, wo = [], []
    for i in range(len(ra)):
        assert ra[i][:8] == rb[i][:8], f"reach {i}: discharge or next reach changed"
        geo, w = split_widths(rb[i])
        if geo != ra[i]:
            ch.append(i)
        elif any(w):
            wo.append(i)
    ns, lon, lat, lev, fix, wid = [], [], [], [], [], []
    for i in ch:
        a = np.frombuffer(ra[i], V2, offset=12)
        b = np.frombuffer(rb[i], V3, offset=12)
        assert len(b) >= 2 and a[0].tobytes() == b[0].tobytes()[:11], f"reach {i}: first vertex moved"
        assert all(a[-1][f] == b[-1][f] for f in ("lon", "lat", "fixed")), f"reach {i}: last vertex moved"
        inner = b[1:-1]
        for f, s in (("lon", lon), ("lat", lat)):
            k = inner[f].astype(np.float64) * GRID
            assert (k == np.floor(k)).all(), f"reach {i}: {f} off the grid"
            s.append(np.diff(np.r_[math.floor(float(a[0][f]) * GRID), k.astype(np.int64)]))
        lev.append(np.diff(b["lev"].astype(np.int64)))
        fix.append(inner["fixed"])
        wid.append(b["width"])
        ns.append(len(b))
    cat = lambda s, dt: np.concatenate(s).astype(dt) if s else np.zeros(0, dt)
    payload = bytearray()
    for idx in (ch, wo):
        payload += varints_np(np.diff(np.r_[0, np.array(idx, np.int64)]))
    payload += varints_np(np.array(ns, np.int64))
    for s in (lon, lat, lev):
        payload += varints_np(zz_np(cat(s, np.int64)))
    payload += cat(fix, np.uint8).tobytes() + cat(wid, np.uint8).tobytes()
    for i in wo:
        payload += split_widths(rb[i])[1]
    out = (b"EMRP" + struct.pack("<iii", SNAP2_VERSION, len(ra), len(ch)) + h8(fa) + h8(fb) + struct.pack("<ii", len(wo), GRID)
           + brotli.compress(bytes(payload), quality=11, lgwin=24))
    print(f"rivers-glo90-snap-2.bin: {len(ch)} of {len(ra)} reaches moved ({sum(ns)} vertices), {len(wo)} more with widths; "
          f"{len(out)} bytes")
    return out, (fa, fb)


def verify_snap2(blob, files):
    """Decodes the overlay as the game does (RiverSnapData.ApplyTo) and compares with the snapped file."""
    fa, fb = files
    assert blob[:4] == b"EMRP" and blob[16:24] == h8(fa) and blob[24:32] == h8(fb)
    v, count, nch = struct.unpack_from("<iii", blob, 4)
    nwo, grid = struct.unpack_from("<ii", blob, 32)
    assert v == SNAP2_VERSION and grid == GRID
    a8 = np.frombuffer(brotli.decompress(blob[40:]), np.uint8)
    base = river_records(fa)
    assert len(base) == count
    d, o = unvarints_at(a8, 0, nch)
    ch = np.cumsum(d)
    d, o = unvarints_at(a8, o, nwo)
    wo = np.cumsum(d)
    ns, o = unvarints_at(a8, o, nch)
    ni = int((ns - 2).sum())
    cols = []
    for k in range(3):
        u, o = unvarints_at(a8, o, ni if k < 2 else ni + nch)
        cols.append(unzz_np(u))
    fix = a8[o:o + ni]; o += ni
    wid = a8[o:o + int(ns.sum())]; o += int(ns.sum())
    recs = []
    for rec in base:  # no widths: zeros
        a = np.frombuffer(rec, V2, offset=12)
        b = np.zeros(len(a), V3)
        for f in V2.names:
            b[f] = a[f]
        recs.append(rec[:12] + b.tobytes())
    p = w = pl = 0
    for i, n in zip(ch.tolist(), ns.tolist()):
        a = np.frombuffer(base[i], V2, offset=12)
        b = np.zeros(n, V3)
        for f in V2.names:
            b[f][[0, -1]] = a[f][[0, -1]]
        m = n - 2
        for f, c in (("lon", cols[0]), ("lat", cols[1])):
            b[f][1:-1] = (math.floor(float(a[0][f]) * GRID) + np.cumsum(c[p:p + m])) / GRID
        b["lev"][1:] = int(a[0]["lev"]) + np.cumsum(cols[2][pl:pl + n - 1])
        b["fixed"][1:-1] = fix[p:p + m]
        b["width"] = wid[w:w + n]
        p += m; w += n; pl += n - 1
        recs[i] = base[i][:8] + struct.pack("<i", n) + b.tobytes()
    for i in wo.tolist():
        b = np.frombuffer(recs[i], V3, offset=12).copy()
        b["width"] = a8[o:o + len(b)]; o += len(b)
        recs[i] = recs[i][:12] + b.tobytes()
    assert o == len(a8) and recs == river_records(fb), "rivers differ"
    print("rivers-glo90-snap-2.bin: base + overlay = the moved rivers with widths")


def rec_parts(rec):
    """A reach record's discharge bytes, next reach, vertex count and vertex bytes (11 each)."""
    nxt, n = struct.unpack_from("<ii", rec, 4)
    return rec[:4], nxt, n, rec[12:]


def apply_arms(base, splits, arms, vb=11):
    """river_arms.py's list from the snapped one: the arms (their next reaches already in the new numbering), then each
    reach followed by its later pieces; a piece flows into the next, the last into the first piece of the old next reach.
    vb: bytes per vertex (11, or 12 with widths)."""
    first, t = [], len(arms)
    for r in range(len(base)):
        first.append(t)
        t += 1 + len(splits.get(r, ()))
    out = list(arms)
    for r, rec in enumerate(base):
        q, nxt, n, v = rec_parts(rec)
        nn = first[nxt] if nxt >= 0 else -1
        cuts = splits.get(r)
        if not cuts:
            out.append(rec[:4] + struct.pack("<i", nn) + rec[8:])
            continue
        bounds = [0] + list(cuts) + [n - 1]
        for j in range(len(bounds) - 1):
            seg = v[vb * bounds[j]:vb * (bounds[j + 1] + 1)]
            out.append(q + struct.pack("<ii", first[r] + j + 1 if j + 2 < len(bounds) else nn, len(seg) // vb) + seg)
    return out


def build_arms(snapped, armed, n_arms, name="rivers-glo90-arms.bin"):
    """The cuts each reach got (where an arm joins it) and the arms' records, from the snapped and the armed rivers.
    Version 1 on record version 2 (rivers-glo90-arms.bin), version 2 on record version 3 with widths (-arms-2.bin)."""
    fa, fb = open(snapped, "rb").read(), open(armed, "rb").read()
    ver = river_version(fa)
    assert river_version(fb) == ver, "the armed rivers' record version differs from the snapped ones'"
    vb = VERTEX_BYTES[ver]
    ra, rb = river_records(fa), river_records(fb)
    arms = rb[:n_arms]
    splits, t = {}, n_arms
    for r, rec in enumerate(ra):
        _, _, n, v = rec_parts(rec)
        pos, cuts = 0, []
        while True:
            _, _, m, w = rec_parts(rb[t])
            assert w == v[vb * pos:vb * (pos + m)], f"reach {r}: piece at vertex {pos} isn't the old course"
            t += 1
            pos += m - 1
            if pos >= n - 1 or m <= 1:
                break
            cuts.append(pos)
        if cuts:
            splits[r] = cuts
    assert t == len(rb), f"{len(rb) - t} records left over"
    payload = bytearray()
    prev = 0
    for r in sorted(splits):
        payload += varint(r - prev); prev = r
        payload += varint(len(splits[r]))
        c0 = 0
        for c in splits[r]:
            payload += varint(c - c0); c0 = c
    for rec in arms:
        payload += rec
    version = ARMS_VERSION if ver == 2 else ARMS2_VERSION
    out = (b"EMRA" + struct.pack("<iiii", version, len(ra), n_arms, len(splits)) + h8(fa) + h8(fb)
           + brotli.compress(bytes(payload), quality=11, lgwin=24))
    print(f"{name}: {n_arms} arms, {len(splits)} reaches split ({sum(map(len, splits.values()))} cuts), "
          f"{len(ra)} -> {len(rb)} reaches; {len(out)} bytes")
    return out, (fa, fb)


def verify_arms(blob, files, name="rivers-glo90-arms.bin"):
    fa, fb = files
    assert blob[:4] == b"EMRA" and blob[20:28] == h8(fa) and blob[28:36] == h8(fb)
    v, count, n_arms, n_split = struct.unpack_from("<iiii", blob, 4)
    assert v in (ARMS_VERSION, ARMS2_VERSION)
    vb = VERTEX_BYTES[2 if v == ARMS_VERSION else 3]
    r = Reader(brotli.decompress(blob[36:]))
    splits, i = {}, 0
    for _ in range(n_split):
        i += r.varint()
        c, cuts = 0, []
        for _ in range(r.varint()):
            c += r.varint(); cuts.append(c)
        splits[i] = cuts
    arms = []
    for _ in range(n_arms):
        n, = struct.unpack_from("<i", r.b, r.o + 8)
        arms.append(r.take(12 + vb * n))
    assert r.o == len(r.b)
    base = river_records(fa)
    assert len(base) == count
    got = apply_arms(base, splits, arms, vb)
    assert got == river_records(fb), "rivers differ"
    bad = sum(1 for k, rec in enumerate(got) if 0 <= struct.unpack_from("<i", rec, 4)[0] <= k)
    assert bad == 0, f"{bad} reaches flow into an earlier one"
    print(f"{name}: snapped + overlay = the armed rivers ({len(got)} reaches, flow order kept)")


def verify_referee(blob, files):
    """Rebuild the refereed terrain cells, islands and coast tiles from base + overlay and compare with the targets."""
    assert blob[:4] == b"EMSR"
    v, w = struct.unpack_from("<ii", blob, 4)
    assert v == REFEREE_VERSION, v
    for j, k in enumerate(("terrain.bin", "islands.bin", "coasts.bin")):
        assert blob[12 + 8 * j:20 + 8 * j] == h8(files[k][0]) and blob[36 + 8 * j:44 + 8 * j] == h8(files[k][1]), k
    r = Reader(brotli.decompress(blob[60:]))
    tc, te, tl, tf = read_patches(r, r.varint())
    # terrain: every tile of the base with the patches applied equals the target's
    fa, fb = files["terrain.bin"]
    (gw, gh, t, count), ia_ = terrain_tiles(fa)
    _, ib_ = terrain_tiles(fb)
    tx = (gw + t - 1) // t
    tile_of = (tc // gw // t) * tx + (tc % gw) // t
    for k in range(count):
        a = fa[ia_[k, 0]:ia_[k, 0] + ia_[k, 1]]
        b = fb[ib_[k, 0]:ib_[k, 0] + ib_[k, 1]]
        sel = np.nonzero(tile_of == k)[0]
        if a == b:
            assert len(sel) == 0, f"terrain tile {k}: patched but unchanged"
            continue
        ea, la_, ga, _ = decode_tile(a, t)
        eb, lb_, gb, _ = decode_tile(b, t)
        rr, cc = (tc[sel] // gw) % t, (tc[sel] % gw) % t
        ea[rr, cc], la_[rr, cc], ga[rr, cc] = te[sel], tl[sel], tf[sel]
        assert (ea == eb).all() and (la_ == lb_).all() and (ga == gb).all(), f"terrain tile {k} differs"
    _, pa, ia = islands_read(files["islands.bin"][0])
    _, pb, ib = islands_read(files["islands.bin"][1])
    patches = {int(c): (int(e), int(l), int(g)) for c, e, l, g in zip(*pa)}
    c = 0
    for _ in range(r.varint()):
        c += r.varint(); del patches[c]
    for c2, e, l, g in zip(*read_patches(r, r.varint())):
        patches[int(c2)] = (int(e), int(l), int(g))
    want = {int(c3): (int(e), int(l), int(g)) for c3, e, l, g in zip(*pb)}
    assert patches == want, "island patches differ"
    n = r.varint()
    got = []
    prev = -1
    for _ in range(n):
        tag = r.varint()
        if tag:
            prev += tag; got.append(ia[prev]); continue
        a, hgt, wd = r.take(4), r.take(4), r.take(4)
        got.append((a, hgt, wd, tuple(decode_pieces(r, lambda k: ia[k][3]))))
    assert got == ib, "islands differ"
    so, to = coasts_read(files["coasts.bin"][0])
    sn, tn = coasts_read(files["coasts.bin"][1])
    st = bytearray(so)
    i, seen = 0, set()
    for _ in range(r.varint()):
        i += r.varint()
        st[i] = r.take(1)[0]
        seen.add(i)
        if st[i] != 255 and st[i] & 2:
            got_runs = decode_coast_tile(r, coast_runs(to[i]) if i in to else [])
            want_runs = coast_runs(tn[i])
            assert len(got_runs) == len(want_runs) and all(np.array_equal(a, b) for a, b in zip(got_runs, want_runs)), f"coast tile {i}"
        else:
            assert i not in tn, f"coast tile {i}"
    assert bytes(st) == bytes(sn), "coast states differ"
    assert all(to.get(k) == tn.get(k) for k in set(to) | set(tn) if k not in seen), "unlisted coast tiles differ"
    assert r.o == len(r.b)
    print("sea-referee.bin: base + overlay = the refereed data")


def verify_snap(blob, files):
    fa, fb = files
    assert blob[:4] == b"EMRP" and blob[16:24] == h8(fa) and blob[24:32] == h8(fb)
    _, count, nch = struct.unpack_from("<iii", blob, 4)
    r = Reader(brotli.decompress(blob[32:]))
    recs = river_records(fa)
    assert len(recs) == count
    idx = []
    i = 0
    for _ in range(nch):
        i += r.varint(); idx.append(i)
    for i in idx:
        s = r.o
        n, = struct.unpack_from("<i", r.b, s + 8)
        recs[i] = r.take(12 + 11 * n)
    assert r.o == len(r.b) and recs == river_records(fb), "rivers differ"
    print("rivers-glo90-snap.bin: base + overlay = the snapped rivers")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", required=True)
    ap.add_argument("--referee")
    ap.add_argument("--snapped")
    ap.add_argument("--armed", help="river_arms.py's output on the snapped rivers (its .json beside it gives the arms' count)")
    ap.add_argument("--snapped2", help="tools/snap_rivers_channel.py's output on rivers-glo90.bin (courses rules 4)")
    ap.add_argument("--armed2", help="river_arms.py's output on --snapped2")
    ap.add_argument("--snapped3", help="tools/snap_rivers_channel.py's output on rivers-glo90.bin on the pixel-is-point grid (courses rules 5)")
    ap.add_argument("--armed3", help="river_arms.py's output on --snapped3")
    ap.add_argument("--out", required=True)
    ap.add_argument("--verify", action="store_true")
    a = ap.parse_args()
    assert not a.armed or a.snapped, "--armed needs --snapped (the arms' base)"
    assert not a.armed2 or a.snapped2, "--armed2 needs --snapped2 (the arms' base)"
    assert not a.armed3 or a.snapped3, "--armed3 needs --snapped3 (the arms' base)"
    blobs = []
    if a.referee:
        ref, rfiles = build_referee(a.base, a.referee)
        if a.verify:
            verify_referee(ref, rfiles)
        blobs.append(("sea-referee.bin", ref))
    if a.snapped:
        snap, sfiles = build_snap(os.path.join(a.base, "rivers-glo90.bin"), a.snapped)
        if a.verify:
            verify_snap(snap, sfiles)
        blobs.append(("rivers-glo90-snap.bin", snap))
    if a.armed:
        with open(a.armed + ".json", encoding="utf8") as f:
            n_arms = json.load(f)["arms"]
        arms, afiles = build_arms(a.snapped, a.armed, n_arms)
        if a.verify:
            verify_arms(arms, afiles)
        blobs.append(("rivers-glo90-arms.bin", arms))
    if a.snapped2:
        snap, sfiles = build_snap2(os.path.join(a.base, "rivers-glo90.bin"), a.snapped2)
        if a.verify:
            verify_snap2(snap, sfiles)
        blobs.append(("rivers-glo90-snap-2.bin", snap))
    if a.armed2:
        with open(a.armed2 + ".json", encoding="utf8") as f:
            n_arms = json.load(f)["arms"]
        arms, afiles = build_arms(a.snapped2, a.armed2, n_arms, "rivers-glo90-arms-2.bin")
        if a.verify:
            verify_arms(arms, afiles, "rivers-glo90-arms-2.bin")
        blobs.append(("rivers-glo90-arms-2.bin", arms))
    if a.snapped3:
        snap, sfiles = build_snap2(os.path.join(a.base, "rivers-glo90.bin"), a.snapped3)
        if a.verify:
            verify_snap2(snap, sfiles)
        blobs.append(("rivers-glo90-snap-3.bin", snap))
    if a.armed3:
        with open(a.armed3 + ".json", encoding="utf8") as f:
            n_arms = json.load(f)["arms"]
        arms, afiles = build_arms(a.snapped3, a.armed3, n_arms, "rivers-glo90-arms-3.bin")
        if a.verify:
            verify_arms(arms, afiles, "rivers-glo90-arms-3.bin")
        blobs.append(("rivers-glo90-arms-3.bin", arms))
    for name, blob in blobs:
        path = os.path.join(a.out, name)
        with open(path, "wb") as f:
            f.write(blob)
        print("wrote", path)


if __name__ == "__main__":
    main()
