"""Bake the heads of HydroRIVERS' smallest streams into earthdata/stream-heads-1.bin: where the springs of the Earth tab's
"Mountain springs and streams: streams" rise (springs rules 2, decision 0063 rule 5; Core.StreamHeads reads the file).

Source: HydroRIVERS v1.0 (Lehner & Grill 2013, www.hydrosheds.org; HydroSHEDS licence: free for non-commercial and
commercial use, with attribution), rawdata/hydrorivers/HydroRIVERS_v10_shp/HydroRIVERS_v10.shp, read only. Its reaches
are lines from upstream to downstream (a reach's last vertex is its downstream reach's first), at HydroSHEDS' 15"
grid. A stream starts where the area draining to it reaches 10 km2, or its mean discharge 0.1 m3/s.

A head is a reach no other reach flows into (NEXT_DOWN): 4,367,076 of them, exactly the reaches of Strahler order 1.
Its place is the 15" cell holding the middle of its first piece (the first vertex lies on a cell edge, the second at a
cell centre).

Perennial heads only. HydroRIVERS' DIS_AV_CMS is the long-term mean discharge (1971-2000) at the reach's outlet; at the
head, where 10 km2 drain to it, it is about DIS_AV_CMS * min(1, 10 / UPLAND_SKM) (Q10, the reach's runoff over the
head's area). A stream whose head carries less than FLOOR_Q (0.032 m3/s at 10 km2: 3.2 l/s per km2, 101 mm of runoff a
year) is left out as intermittent: the heads' runoff splits there between dry and wet country (median runoff of the
heads: Hoggar 28 mm, Arabia 6, central Sahara 1; Latvia 294, Bolzano 511, Bernese Oberland 1180; 90% of the Hoggar's
heads are under 52 mm, 90% of Latvia's over 217). Kept: Latvia, the Alps, southern Norway and the Amazon all; the
Spanish Meseta a third, Kansas a third, the Sahel a tenth, Murcia, Arabia, the central Sahara and the Australian
outback almost none.

stream-heads-1.bin (little-endian):
  header: char[4] "EMSH", int32 version 1, int32 cells per degree (240), int32 head count, int32 floor class (40),
          int32 classes per doubling (8)
  brotli(
    varint head count of every 1-degree tile: 180 rows from the north (row = floor(90 - lat)) of 360 tiles from
      180 W (col = floor(lon + 180)), row-major
    varint per head, by tile then cell: its 15" cell in the tile (cell = row * 240 + col, row from the tile's north edge,
      col from its west edge), the first of a tile as it is, the others as the step from the one before
    byte per head, in the same order: discharge class c, Q10 = 0.001 * 2^(c / 8) m3/s (c >= the floor class)
  )
Two heads in one cell are one (the larger class kept).

Usage: python -I tools/bake_stream_heads.py [--shp HydroRIVERS_v10.shp] [--out earthdata/stream-heads-1.bin]
"""
import argparse, os, struct, time

import brotli
import numpy as np
import shapefile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RAW = os.environ.get("EARTH_RAW", os.path.join(ROOT, "rawdata"))
CELLS = 240            # 15" cells per degree (HydroSHEDS' grid)
STEPS = 8              # discharge classes per doubling
Q0 = 0.001             # m3/s at class 0
HEAD_KM2 = 10.0        # HydroRIVERS starts a stream where 10 km2 drain to it
FLOOR_CLASS = 40       # 0.032 m3/s at 10 km2 (101 mm/yr): intermittent below
VERSION = 1
T0 = time.time()

WINDOWS = {
    "Bernese Oberland": (46.45, 46.80, 7.55, 8.25),
    "Gauja at Sigulda": (57.00, 57.30, 24.55, 25.15),
    "Alps around Bolzano": (46.25, 46.75, 11.05, 11.65),
    "southern Norway": (59.5, 61.5, 6.0, 10.0),
    "central Sahara": (20.0, 28.0, 0.0, 15.0),
    "Spanish Meseta": (39.0, 42.0, -5.0, -2.0),
}


def log(m):
    print(f"[{time.time() - T0:6.1f}s] {m}", flush=True)


def default_shp():
    for base in (RAW, os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(ROOT))), "rawdata")):
        p = os.path.join(base, "hydrorivers", "HydroRIVERS_v10_shp", "HydroRIVERS_v10.shp")
        if os.path.exists(p):
            return p
    return os.path.join(RAW, "hydrorivers", "HydroRIVERS_v10_shp", "HydroRIVERS_v10.shp")


def read_fields(shp, names):
    """The DBF columns by name, read in bulk (pyshp gives the layout; 8.5 million records record by record take long)."""
    r = shapefile.Reader(shp)
    n, shape_type = len(r), r.shapeType
    fields = r.fields[1:]
    r.close()
    if shape_type != shapefile.POLYLINE:
        raise SystemExit(f"{shp}: shape type {shape_type}, expected polylines")
    dbf = os.path.splitext(shp)[0] + ".dbf"
    with open(dbf, "rb") as f:
        hdr = f.read(32)
    count, hlen, rlen = struct.unpack("<IHH", hdr[4:12])
    if count != n:
        raise SystemExit(f"{dbf}: {count} records, the shapefile has {n}")
    layout, off = {}, 1
    for name, typ, size, dec in fields:
        layout[name] = (off, size)
        off += size
    if off != rlen:
        raise SystemExit(f"{dbf}: records of {rlen} bytes, the fields add up to {off}")
    mm = np.memmap(dbf, dtype=np.uint8, mode="r", offset=hlen, shape=(n, rlen))
    out = {}
    for name, dtype in names:
        o, size = layout[name]
        raw = np.ascontiguousarray(mm[:, o:o + size]).view(f"S{size}").ravel()
        out[name] = raw.astype(dtype)
    return n, out


def first_pieces(shp, n):
    """First and second vertex of every reach, from the .shx offsets (one part each)."""
    base = os.path.splitext(shp)[0]
    shx = np.fromfile(base + ".shx", dtype=">i4", offset=100).reshape(-1, 2)
    if len(shx) != n:
        raise SystemExit(f"{base}.shx: {len(shx)} records, the shapefile has {n}")
    start = shx[:, 0].astype(np.int64) * 2 + 8
    mm = np.memmap(base + ".shp", dtype=np.uint8, mode="r")

    def gather(offsets, size, dtype):
        return np.ascontiguousarray(mm[offsets[:, None] + np.arange(size)]).view(dtype).ravel()

    parts = gather(start + 36, 4, "<i4")
    points = gather(start + 40, 4, "<i4")
    if parts.max() != 1 or points.min() < 2:
        raise SystemExit("expected one part of at least two vertices per reach")
    p0 = start + 44 + 4
    return gather(p0, 8, "<f8"), gather(p0 + 8, 8, "<f8"), gather(p0 + 16, 8, "<f8"), gather(p0 + 24, 8, "<f8")


def check_with_pyshp(shp, x0, y0, x1, y1, sample=2000):
    r = shapefile.Reader(shp)
    rng = np.random.default_rng(1318)
    for i in rng.choice(len(x0), size=min(sample, len(x0)), replace=False):
        pts = r.shape(int(i)).points
        if pts[0] != (x0[i], y0[i]) or pts[1] != (x1[i], y1[i]):
            raise SystemExit(f"reach {i}: bulk read {x0[i], y0[i], x1[i], y1[i]}, pyshp {pts[:2]}")
    r.close()


def varints(values):
    out = bytearray()
    for v in values.tolist():
        while v >= 0x80:
            out.append((v & 0x7F) | 0x80)
            v >>= 7
        out.append(v)
    return bytes(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--shp", default=default_shp())
    ap.add_argument("--out", default=os.path.join(ROOT, "earthdata", f"stream-heads-{VERSION}.bin"))
    a = ap.parse_args()

    n, f = read_fields(a.shp, [("HYRIV_ID", np.int64), ("NEXT_DOWN", np.int64), ("DIS_AV_CMS", np.float64),
                               ("UPLAND_SKM", np.float64), ("ORD_STRA", np.int64)])
    log(f"{n} reaches read from {a.shp}")
    ids, nxt = f["HYRIV_ID"], f["NEXT_DOWN"]
    order = np.argsort(ids)
    sid = ids[order]
    fed = np.zeros(n, bool)
    m = nxt != 0
    pos = np.searchsorted(sid, nxt[m])
    ok = (pos < n) & (sid[np.minimum(pos, n - 1)] == nxt[m])
    fed[order[pos[ok]]] = True
    heads = np.nonzero(~fed)[0]
    log(f"{len(heads)} heads (no reach flows in); Strahler order 1: {(f['ORD_STRA'] == 1).sum()}")

    x0, y0, x1, y1 = first_pieces(a.shp, n)
    check_with_pyshp(a.shp, x0, y0, x1, y1)
    log("first pieces read (checked against pyshp)")

    q = f["DIS_AV_CMS"][heads]
    up = f["UPLAND_SKM"][heads]
    q10 = q * np.minimum(1.0, HEAD_KM2 / np.maximum(up, 1e-6))
    with np.errstate(divide="ignore"):
        cls = np.floor(STEPS * np.log2(np.maximum(q10, 1e-12) / Q0))
    keep = cls >= FLOOR_CLASS
    log(f"perennial heads (Q10 >= {Q0 * 2 ** (FLOOR_CLASS / STEPS):.4f} m3/s): {keep.sum()} of {len(heads)}; "
        f"the largest head reach carries {q.max():.2f} m3/s")
    heads, cls = heads[keep], np.clip(cls[keep], 0, 255).astype(np.uint8)

    lon = (x0[heads] + x1[heads]) / 2
    lat = (y0[heads] + y1[heads]) / 2
    gx = np.floor((lon + 180) * CELLS).astype(np.int64) % (360 * CELLS)
    gy = np.clip(np.floor((90 - lat) * CELLS).astype(np.int64), 0, 180 * CELLS - 1)
    tile = (gy // CELLS) * 360 + gx // CELLS
    cell = (gy % CELLS) * CELLS + gx % CELLS
    o = np.lexsort((-cls.astype(np.int64), cell, tile))
    tile, cell, cls, lat, lon = tile[o], cell[o], cls[o], lat[o], lon[o]
    first = np.r_[True, (tile[1:] != tile[:-1]) | (cell[1:] != cell[:-1])]
    log(f"{(~first).sum()} heads share a cell with a larger one and are merged into it")
    tile, cell, cls, lat, lon = tile[first], cell[first], cls[first], lat[first], lon[first]

    counts = np.bincount(tile, minlength=180 * 360)
    new_tile = np.r_[True, tile[1:] != tile[:-1]]
    delta = np.where(new_tile, cell, np.r_[0, np.diff(cell)])
    body = varints(counts) + varints(delta) + cls.tobytes()
    packed = brotli.compress(body, quality=11, lgwin=24)
    header = struct.pack("<4siiiii", b"EMSH", VERSION, CELLS, len(cell), FLOOR_CLASS, STEPS)
    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    with open(a.out, "wb") as fo:
        fo.write(header + packed)
    log(f"{a.out}: {len(cell)} heads in {np.count_nonzero(counts)} tiles, {len(header) + len(packed)} bytes "
        f"(body {len(body)} bytes before brotli)")

    for name, (la0, la1, lo0, lo1) in WINDOWS.items():
        w = (lat >= la0) & (lat < la1) & (lon >= lo0) & (lon < lo1)
        qs = Q0 * 2.0 ** (cls[w] / STEPS)
        print(f"  {name:22s} {w.sum():6d} heads" + (f", Q10 median {np.median(qs) * 1000:.0f} l/s" if w.any() else ""))


if __name__ == "__main__":
    main()
