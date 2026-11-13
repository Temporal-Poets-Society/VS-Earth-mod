"""
Bakes the Randolph Glacier Inventory 6.2 outlines into earthdata/glaciers.bin: the fraction of each 30" cell covered by
glacier ice, for the mountain glaciers (Jostedalsbreen, the Alps, Kilimanjaro, the Himalaya) that the ice-sheet rules
leave as soil. All 19 RGI regions are included; the game itself skips cells already inside its Greenland and Antarctic
ice sheets.

Each 30" cell is sampled at 4 x 4 points (the centres of its sub-cells, a 7.5" grid). A point is glacier when it lies
inside an RGI outline (even-odd over all the outline's rings, so nunataks and other holes stay ice-free; a point on a
shared border belongs to one neighbour only). Overlapping outlines count once. Outlines whose longitudes span more than
180 degrees are unwrapped across the antimeridian and their samples wrapped back.

Input (rawdata/rgi62/, see tools/download_data.sh, about 420 MB): OGGM's public mirror of RGI 6.2,
https://cluster.klima.uni-bremen.de/~oggm/rgi/rgi62.zip, with its per-region zips extracted into rawdata/rgi62/shp.
RGI Consortium (2017): Randolph Glacier Inventory - A Dataset of Global Glacier Outlines, Version 6.0. Technical Report,
Global Land Ice Measurements from Space, Colorado, USA. Digital Media. https://doi.org/10.7265/N5-RGI-60. CC BY 4.0.

glaciers.bin (little-endian): b"EMGL", int32 version 1, width 43200, height 21600, tile 360, tile count 7200, then
(int32 offset, int32 length) per tile, then the tiles. Tile index = tileRow * 120 + tileCol from the north-west; row r is
centred at lat 90 - (r + 0.5) / 120, column c at lon -180 + (c + 0.5) / 120. Each tile is brotli of one plane: u8
coverage = round(255 x fraction), tile x tile, row-major. Tiles without glacier have length 0.

    .venv/Scripts/python -I tools/bake_glaciers.py [rgi shp folder] [--check]
"""
import glob, math, os, struct, sys, time

import brotli
import numpy as np
import shapefile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "earthdata", "glaciers.bin")
RES = 120             # cells per degree (30")
TILE = 360            # cells per tile side (3°)
SUB = 4               # samples per cell side
W, H = 360 * RES, 180 * RES
TX, TY = W // TILE, H // TILE
SW = W * SUB          # sample columns around the globe
T0 = time.time()


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


def glacier_spans(points, parts):
    """The sample spans (row, first column, end column) inside one outline, in global sample coordinates (columns may run
    past the globe's width or below 0; wrapped later)."""
    pts = np.asarray(points, dtype=np.float64)
    x = (pts[:, 0] + 180.0) * (RES * SUB)
    y = (90.0 - pts[:, 1]) * (RES * SUB)
    if pts[:, 0].max() - pts[:, 0].min() > 180.0:          # across the antimeridian: unwrap the western longitudes
        x = np.where(pts[:, 0] < 0, x + SW, x)
    bounds = list(parts) + [len(pts)]
    x0s, y0s, x1s, y1s = [], [], [], []
    for a, b in zip(bounds[:-1], bounds[1:]):
        if b - a < 3:
            continue
        rx, ry = x[a:b], y[a:b]
        nx, ny = np.roll(rx, -1), np.roll(ry, -1)            # closes the ring (a closed ring adds one zero-length edge)
        x0s.append(rx); y0s.append(ry); x1s.append(nx); y1s.append(ny)
    if not x0s:
        return None
    x0, y0, x1, y1 = (np.concatenate(v) for v in (x0s, y0s, x1s, y1s))
    lo, hi = np.minimum(y0, y1), np.maximum(y0, y1)
    r0 = np.ceil(lo - 0.5).astype(np.int64)                 # sample rows whose centre (r + 0.5) is in [lo, hi)
    r1 = np.ceil(hi - 0.5).astype(np.int64)
    n = r1 - r0
    keep = n > 0
    if not keep.any():
        return None
    x0, y0, x1, y1, r0, n = x0[keep], y0[keep], x1[keep], y1[keep], r0[keep], n[keep]
    total = int(n.sum())
    edge = np.repeat(np.arange(len(n)), n)
    start = np.cumsum(n) - n
    rows = r0[edge] + (np.arange(total) - start[edge])
    yc = rows + 0.5
    e0x, e0y, e1x, e1y = x0[edge], y0[edge], x1[edge], y1[edge]
    xc = e0x + (yc - e0y) * (e1x - e0x) / (e1y - e0y)
    order = np.lexsort((xc, rows))
    rows, xc = rows[order], xc[order]
    if len(rows) % 2 or np.any(rows[0::2] != rows[1::2]):
        raise ValueError("unpaired crossings")
    c0 = np.ceil(xc[0::2] - 0.5).astype(np.int64)           # sample columns whose centre (c + 0.5) is in [xa, xb)
    c1 = np.ceil(xc[1::2] - 0.5).astype(np.int64)
    r = rows[0::2]
    ok = c1 > c0
    return r[ok], c0[ok], c1[ok]


def read_spans(folder):
    files = [p for p in sorted(glob.glob(os.path.join(folder, "*_rgi62_*.shp"))) if not os.path.basename(p).startswith("00_")]
    if len(files) != 19:
        raise SystemExit(f"{folder}: expected the 19 RGI region shapefiles, found {len(files)}")
    R, C0, C1 = [], [], []
    area, count, bad = 0.0, 0, 0
    for path in files:
        sf = shapefile.Reader(path)
        names = [f[0] for f in sf.fields if f[0] != "DeletionFlag"]
        ai = names.index("Area")
        for sr in sf.iterShapeRecords():
            area += sr.record[ai]
            count += 1
            s = sr.shape
            if not s.points:
                continue
            try:
                sp = glacier_spans(s.points, s.parts)
            except ValueError:
                bad += 1
                continue
            if sp is None:
                continue
            R.append(sp[0]); C0.append(sp[1]); C1.append(sp[2])
        sf.close()
        log(f"{os.path.basename(path)} read")
    r, c0, c1 = np.concatenate(R), np.concatenate(C0), np.concatenate(C1)
    # wrap the columns onto the globe, splitting spans that cross its edge
    shift = np.floor_divide(c0, SW) * SW
    c0, c1 = c0 - shift, c1 - shift
    over = c1 > SW
    r = np.concatenate([r, r[over]])
    c0 = np.concatenate([c0, np.zeros(over.sum(), np.int64)])
    c1 = np.concatenate([np.minimum(c1, SW), c1[over] - SW])
    r = np.clip(r, 0, H * SUB - 1)
    log(f"{count} outlines, RGI area {area:.0f} km2, {len(r)} spans, {bad} outlines skipped")
    return r, c0, c1, area


def bake(folder):
    r, c0, c1, rgi_area = read_spans(folder)
    order = np.argsort(r, kind="stable")
    r, c0, c1 = r[order], c0[order], c1[order]
    cell_row = r // SUB
    blobs = [b""] * (TX * TY)
    km2 = 0.0
    cell_km2 = (6371.0088 * math.pi / 180 / RES) ** 2
    band_bounds = np.searchsorted(cell_row, np.arange(0, H + 1, TILE))
    for ty in range(TY):
        a, b = band_bounds[ty], band_bounds[ty + 1]
        if a == b:
            continue
        cov = np.zeros((TILE, W), np.uint8)                 # samples inside, 0..16
        rows_here = cell_row[a:b]
        bounds = np.searchsorted(rows_here, np.arange(ty * TILE, (ty + 1) * TILE + 1))
        for i in range(TILE):
            p, q = a + bounds[i], a + bounds[i + 1]
            if p == q:
                continue
            diff = np.zeros((SUB, SW + 1), np.int32)
            sr = r[p:q] - (ty * TILE + i) * SUB
            np.add.at(diff, (sr, c0[p:q]), 1)
            np.add.at(diff, (sr, c1[p:q]), -1)
            inside = np.cumsum(diff[:, :SW], axis=1) > 0
            cov[i] = inside.reshape(SUB, W, SUB).sum(axis=(0, 2))
        u8 = np.round(cov.astype(np.float32) * (255.0 / (SUB * SUB))).astype(np.uint8)
        lat = 90.0 - (ty * TILE + np.arange(TILE) + 0.5) / RES
        km2 += float((cov.sum(axis=1) / (SUB * SUB) * np.cos(np.radians(lat))).sum() * cell_km2)
        for tx in range(TX):
            t = u8[:, tx * TILE:(tx + 1) * TILE]
            if t.any():
                blobs[ty * TX + tx] = brotli.compress(np.ascontiguousarray(t).tobytes(), quality=11, lgwin=22)
        log(f"tile row {ty}: {sum(1 for tx in range(TX) if blobs[ty * TX + tx])} tiles")
    tmp = OUT + ".tmp"
    with open(tmp, "wb") as f:
        f.write(b"EMGL")
        f.write(struct.pack("<iiiii", 1, W, H, TILE, len(blobs)))
        off = 24 + 8 * len(blobs)
        for bl in blobs:
            f.write(struct.pack("<ii", off, len(bl)))
            off += len(bl)
        for bl in blobs:
            f.write(bl)
    os.replace(tmp, OUT)
    stored = sum(1 for bl in blobs if bl)
    log(f"wrote {OUT}: {os.path.getsize(OUT) / 1e6:.2f} MB, {stored} tiles; raster area {km2:.0f} km2 vs RGI {rgi_area:.0f} km2 "
        f"({100 * (km2 / rgi_area - 1):+.2f}%)")


class Reader:
    def __init__(self, path):
        self.data = open(path, "rb").read()
        d = self.data
        if d[:4] != b"EMGL":
            raise SystemExit("bad header")
        ver, self.w, self.h, self.tile, n = struct.unpack_from("<iiiii", d, 4)
        assert (ver, self.w, self.h, self.tile, n) == (1, W, H, TILE, TX * TY)
        self.index = struct.unpack_from(f"<{2 * n}i", d, 24)
        self.cache = {}

    def tile_plane(self, k):
        if k not in self.cache:
            off, ln = self.index[2 * k], self.index[2 * k + 1]
            self.cache[k] = None if ln == 0 else np.frombuffer(brotli.decompress(self.data[off:off + ln]), np.uint8).reshape(TILE, TILE)
        return self.cache[k]

    def cell(self, row, col):
        t = self.tile_plane(row // TILE * TX + col % W // TILE)
        return 0 if t is None else int(t[row % TILE, col % W % TILE])

    def at(self, lat, lon, radius=0):
        """Coverage (0..1) of the cell at lat/lon and the mean over a (2 radius + 1)^2 neighbourhood."""
        row = int((90.0 - lat) * RES)
        col = int((lon + 180.0) * RES)
        vals = [self.cell(row + dr, col + dc) for dr in range(-radius, radius + 1) for dc in range(-radius, radius + 1)]
        return self.cell(row, col) / 255.0, sum(vals) / len(vals) / 255.0


def check():
    rd = Reader(OUT)
    stored = sum(1 for k in range(TX * TY) if rd.index[2 * k + 1])
    print(f"{OUT}: {len(rd.data) / 1e6:.2f} MB, {stored} stored tiles")
    places = [("Jostedalsbreen A", 61.70, 6.90), ("Jostedalsbreen B", 61.65, 7.05), ("Kilimanjaro summit", -3.07, 37.355),
              ("Mont Blanc / Mer de Glace", 45.88, 6.93), ("Aletsch", 46.50, 8.03), ("Everest Khumbu", 27.98, 86.86),
              ("Vatnajokull", 64.4, -16.8), ("Columbia Icefield", 52.17, -117.3), ("Paris (control)", 48.86, 2.35)]
    for name, lat, lon in places:
        c, m = rd.at(lat, lon, 2)
        print(f"  {name:28s} {lat:7.3f} {lon:8.3f}  cell {c:.3f}  5x5 mean {m:.3f}")


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if "--check" not in sys.argv:
        bake(args[0] if args else os.path.join(ROOT, "rawdata", "rgi62", "shp"))
    check()
