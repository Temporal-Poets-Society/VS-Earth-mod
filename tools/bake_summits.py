"""
Bakes ETOPO 2022's 30 arc-second surface elevation into earthdata/summits.bin (version 2): how far the mountains' ridges and
peaks rise above Earth's smooth 2' terrain (terrain.bin, from ETOPO 60"), which averages the summits away (Everest ~7,440 m,
Fuji ~2,840 m there). The game only adds height.

Input (rawdata/etopo-30s/, see tools/download_data.sh, about 1.6 GB):
  - ETOPO_2022_v1_30s_N90W180_surface.tif: 43200 x 21600 float32, cell (area) registered, row 0's centre at 90 - 1/240,
    column 0's at -180 + 1/240. NOAA NCEI, public domain, doi:10.25921/fd45-gt74.

Version 2 stores the excess over a coarse reference, one byte per 30" cell:
  - reference: the mean of each 2' cell's 4 x 4 30" cells (2' row r / column c centred at 90 - (r + 0.5) / 30,
    -180 + (c + 0.5) / 30), interpolated bilinearly at every 30" cell centre (longitude wraps, rows clamp at the poles);
    it approximates the game's smooth 2' terrain;
  - excess = max(0, fine - reference) where fine > 0 m (dry land), else 0;
  - stored byte = clip(round(excess / 8), 0, 255): 8 m steps, up to 2040 m. The ground is reference + 8 x byte.
  - publication data keeps this extra detail only above --min-elevation (default 3000 m), fading it in over
    --fade-metres (default 250 m). The base terrain remains at all elevations.

summits.bin (little-endian): b"EMSU", int32 version (2), width (43200), height (21600), tile (360), tile count (7200), then
(int32 offset, int32 length) per tile, from the north-west, index = tileRow * 120 + tileCol, then the tiles. Grid: row r's
centre at lat 90 - (r + 0.5) / 120, column c's at lon -180 + (c + 0.5) / 120. A tile is stored only where its highest 30"
cell is at least --min-max-elevation m (default 3000) and some filtered byte is > 0; otherwise its length is 0 (at the running
offset, which FineTemperature-style readers check). Each stored tile is brotli (quality 11) of tile x tile bytes,
row-major, without prediction.

The GeoTIFF is read one strip of tile rows at a time (its 256 x 256 deflate tiles), twice: the 2' means, then the excess.

    .venv/Scripts/python -I tools/bake_summits.py [30" tif] [--rawdata DIR] [--min-max-elevation M] [--check]

--check writes nothing and only reports the sizes and the summits; after a bake the summits are read back from the file.
"""
import argparse, os, struct, sys, time
from concurrent.futures import ProcessPoolExecutor, ThreadPoolExecutor

import brotli
import numpy as np
import tifffile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "earthdata", "summits.bin")
RAW = os.path.join(ROOT, "rawdata")
SRC = os.path.join("etopo-30s", "ETOPO_2022_v1_30s_N90W180_surface.tif")   # in --rawdata
SRC60 = "ETOPO_2022_v1_60s_N90W180_surface.tif"                           # in --rawdata, for comparison only
VERSION = 2
RES = 120             # cells per degree (30")
W, H = 360 * RES, 180 * RES
TILE = 360            # cells per tile side (3°)
TX, TY = W // TILE, H // TILE
SUB = 4               # 30" cells per 2' reference cell side
CW, CH = W // SUB, H // SUB
STEP = 8              # metres per stored unit
SIZE_THRESHOLDS = (500, 1000)          # sizes reported for these
DEFAULT_MIN_MAX = 3000
DEFAULT_MIN_ELEVATION = 3000
DEFAULT_FADE = 250
T0 = time.time()

SUMMITS = [
    ("Everest", 27.988, 86.925, 8849), ("K2", 35.88, 76.51, 8611), ("Fuji", 35.361, 138.727, 3776),
    ("Mont Blanc", 45.833, 6.865, 4808), ("Kilimanjaro", -3.067, 37.355, 5895), ("Denali", 63.069, -151.007, 6190),
    ("Aconcagua", -32.653, -70.011, 6961), ("Elbrus", 43.355, 42.439, 5642), ("Galdhøpiggen", 61.636, 8.312, 2469),
    ("Ben Nevis", 56.797, -5.004, 1345),
]


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


def window(lat, lon, res, radius=0.1):
    """Rows r0..r1 and columns c0..c1 (end exclusive) of the cells centred within `radius` degrees of (lat, lon)."""
    r0 = max(int(np.ceil((90 - lat - radius) * res - 0.5)), 0)
    r1 = min(int(np.floor((90 - lat + radius) * res - 0.5)) + 1, 180 * res)
    c0 = int(np.ceil((lon + 180 - radius) * res - 0.5))
    c1 = int(np.floor((lon + 180 + radius) * res - 0.5)) + 1
    return r0, r1, c0, c1


class Raster:
    """A global, cell-registered, tiled float GeoTIFF read by rows of its tiles (untrusted data: only pixel values are used)."""

    def __init__(self, path, res):
        self.tif = tifffile.TiffFile(path)
        p = self.page = self.tif.pages[0]
        tags = {t.name: t.value for t in p.tags.values()}
        geo = self.tif.geotiff_metadata or {}
        self.res = res
        w, h = 360 * res, 180 * res
        if p.shape != (h, w) or not p.is_tiled or p.samplesperpixel != 1:
            raise SystemExit(f"{path}: shape {p.shape}, tiled {p.is_tiled}; expected {(h, w)}")
        scale = tags.get("ModelPixelScaleTag")
        tie = tags.get("ModelTiepointTag")
        if scale is None or tie is None or abs(scale[0] - 1 / res) > 1e-9 or abs(scale[1] - 1 / res) > 1e-9:
            raise SystemExit(f"{path}: pixel scale {scale}")
        # the centre of row 0 / column 0: PixelIsArea ties the cell's corner, PixelIsPoint its centre
        is_area = int(geo.get("GTRasterTypeGeoKey", 1)) == 1
        half = 0.5 / res if is_area else 0.0
        top = tie[4] + tie[1] / res - half
        west = tie[3] - tie[0] / res + half
        node_offset = "node_offset\">1" in str(tags.get("GDAL_METADATA", ""))
        self.registration = (f"raster type {'PixelIsArea' if is_area else 'PixelIsPoint'}, tiepoint {tuple(tie)}, "
                             f"GDAL node_offset=1: {node_offset}; row 0 centre {top:.6f}, column 0 centre {west:.6f}")
        if abs(top - (90 - 0.5 / res)) > 1e-9 or abs(west - (-180 + 0.5 / res)) > 1e-9:
            raise SystemExit(f"{path}: not cell registered as expected ({self.registration})")
        nd = tags.get("GDAL_NODATA")
        self.nodata = float(nd) if nd is not None else None
        self.tl, self.tw = p.tilelength, p.tilewidth
        self.ncols = -(-w // self.tw)
        self.w, self.h = w, h
        self.bands = {}
        self.pool = ThreadPoolExecutor(8)

    def _band(self, b):
        """Rows b * tilelength .. of the image (float32, NaN for no data)."""
        if b in self.bands:
            return self.bands[b]
        p, fh = self.page, self.tif.filehandle
        datas = []
        for i in range(b * self.ncols, (b + 1) * self.ncols):
            fh.seek(p.dataoffsets[i])
            datas.append((fh.read(p.databytecounts[i]), i))
        out = np.empty((self.tl, self.ncols * self.tw), np.float32)
        def dec(arg):
            data, i = arg
            a = p.decode(data, i)[0]
            c = i - b * self.ncols
            out[:, c * self.tw:(c + 1) * self.tw] = a.reshape(a.shape[-3], a.shape[-2])[:self.tl, :self.tw]
        list(self.pool.map(dec, datas))
        out = out[:, :self.w]
        if self.nodata is not None:
            out[out == self.nodata] = np.nan
        if len(self.bands) >= 2:
            self.bands.pop(min(self.bands))
        self.bands[b] = out
        return out

    def rows(self, r0, r1):
        r1 = min(r1, self.h)
        bands = [self._band(b) for b in range(r0 // self.tl, (r1 - 1) // self.tl + 1)]
        a = np.concatenate(bands, axis=0)
        off = (r0 // self.tl) * self.tl
        return a[r0 - off:r1 - off]

    def window_max(self, lat, lon, radius=0.1):
        """The highest cell whose centre is within `radius` degrees in latitude and longitude, and where it is."""
        res = self.res
        r0, r1, c0, c1 = window(lat, lon, res, radius)
        a = self.rows(r0, r1)[:, c0:c1]
        k = np.nanargmax(a)
        r, c = divmod(k, a.shape[1])
        return float(a[r, c]), 90 - (r0 + r + 0.5) / res, -180 + (c0 + c + 0.5) / res


def fine_rows(ras, r0, r1):
    return np.nan_to_num(ras.rows(r0, r1), nan=0.0)


def coarse_means(ras):
    """The 2' grid: the mean of each cell's 4 x 4 30" cells (float32, CH x CW)."""
    m = np.empty((CH, CW), np.float32)
    k = TILE // SUB
    for ty in range(TY):
        strip = fine_rows(ras, ty * TILE, (ty + 1) * TILE)
        m[ty * k:(ty + 1) * k] = strip.reshape(k, SUB, CW, SUB).mean(axis=(1, 3), dtype=np.float64)
    log(f"2' means {float(m.min()):.0f}..{float(m.max()):.0f} m")
    return m


def axis(n_fine, n_coarse, wrap):
    """For each fine index: the two coarse indices around its centre and the weight of the second (bilinear)."""
    y = (np.arange(n_fine) + 0.5) / SUB - 0.5
    i0 = np.floor(y).astype(np.int64)
    f = (y - i0).astype(np.float32)
    i1 = i0 + 1
    if wrap:
        i0 %= n_coarse
        i1 %= n_coarse
    else:
        i0 = np.clip(i0, 0, n_coarse - 1)
        i1 = np.clip(i1, 0, n_coarse - 1)
    return i0, i1, f


ROW_AXIS = axis(H, CH, False)
COL_AXIS = axis(W, CW, True)


def reference(m, r0, r1, c0=0, c1=W):
    """The 2' means interpolated bilinearly at the 30" cell centres of rows r0..r1, columns c0..c1 (wrapping)."""
    i0, i1, fy = (a[r0:r1] for a in ROW_AXIS)
    cols = np.arange(c0, c1) % W
    j0, j1, fx = (a[cols] for a in COL_AXIS)
    a, b = m[i0], m[i1]
    top = a[:, j0] * (1 - fx) + a[:, j1] * fx
    bot = b[:, j0] * (1 - fx) + b[:, j1] * fx
    return top * (1 - fy)[:, None] + bot * fy[:, None]


def excess_bytes(fine, ref):
    e = np.where(fine > 0, np.maximum(fine - ref, 0), 0)
    return np.clip(np.round(e / STEP), 0, 255).astype(np.uint8)


def filter_excess(q, fine, minimum=DEFAULT_MIN_ELEVATION, fade=DEFAULT_FADE):
    """Remove only the added fine relief below the cutoff; preserve the ordinary terrain grid."""
    if minimum <= 0:
        return q
    weight = np.clip((fine - minimum) / fade, 0, 1) if fade > 0 else (fine >= minimum)
    return np.rint(q * weight).astype(np.uint8)


def compress(raw, quality):
    return brotli.compress(raw, quality=quality, lgwin=24)


def bake(ras, m, quality, keep_from, minimum=DEFAULT_MIN_ELEVATION, fade=DEFAULT_FADE):
    """Brotli blobs of every tile whose highest 30" cell is at least keep_from m and has a byte > 0: {index: (max, blob)}."""
    tiles = {}
    capped = 0
    with ProcessPoolExecutor(max(1, (os.cpu_count() or 2) - 1)) as pool:
        for ty in range(TY):
            r0, r1 = ty * TILE, (ty + 1) * TILE
            fine = fine_rows(ras, r0, r1)
            q = filter_excess(excess_bytes(fine, reference(m, r0, r1)), fine, minimum, fade)
            capped += int((q == 255).sum())
            todo = []
            for tx in range(TX):
                sl = (slice(None), slice(tx * TILE, (tx + 1) * TILE))
                top = float(fine[sl].max())
                t = q[sl]
                if top >= keep_from and t.any():
                    todo.append((ty * TX + tx, top, np.ascontiguousarray(t).tobytes()))
            blobs = pool.map(compress, [x[2] for x in todo], [quality] * len(todo))
            for (i, top, _), blob in zip(todo, blobs):
                tiles[i] = (top, blob)
            if ty % 10 == 9 or ty == TY - 1:
                log(f"tile row {ty + 1}/{TY}: {len(tiles)} tiles, {sum(len(b) for _, b in tiles.values()) / 1e6:.1f} MB")
    log(f"{capped} cells at the 255 cap ({255 * STEP} m)")
    return tiles


def write(tiles, min_max, path=OUT):
    keep = {i: b for i, (top, b) in tiles.items() if top >= min_max}
    count = TX * TY
    with open(path, "wb") as f:
        f.write(b"EMSU")
        f.write(struct.pack("<iiiii", VERSION, W, H, TILE, count))
        off = 4 + 20 + 8 * count
        for i in range(count):
            n = len(keep[i]) if i in keep else 0
            f.write(struct.pack("<ii", off, n))   # unstored: length 0 at the running offset (FineTemperature checks offsets)
            off += n
        for i in range(count):
            if i in keep:
                f.write(keep[i])
    log(f"{path}: {os.path.getsize(path) / 1e6:.2f} MB ({os.path.getsize(path)} bytes), {len(keep)} of {count} tiles stored (max >= {min_max} m)")


class SummitsFile:
    """Reads summits.bin version 2 as the game would: per tile, brotli of tile x tile bytes (8 m units)."""

    def __init__(self, path=OUT):
        with open(path, "rb") as f:
            self.file = f.read()
        if self.file[:4] != b"EMSU":
            raise SystemExit("summits.bin: bad header")
        version, self.w, self.h, self.tile, count = struct.unpack_from("<iiiii", self.file, 4)
        if version != VERSION or count != (self.w // self.tile) * (self.h // self.tile):
            raise SystemExit("summits.bin: layout mismatch")
        self.index = [struct.unpack_from("<ii", self.file, 24 + 8 * i) for i in range(count)]
        header = 24 + 8 * count
        if not all(o >= header and o + n <= len(self.file) for o, n in self.index) \
                or header + sum(n for _, n in self.index) != len(self.file):
            raise SystemExit("summits.bin: bad tile index")
        self.cache = {}

    def tile_at(self, key):
        if key not in self.cache:
            off, n = self.index[key]
            t = self.tile
            self.cache[key] = np.zeros((t, t), np.uint8) if n == 0 else \
                np.frombuffer(brotli.decompress(self.file[off:off + n]), np.uint8).reshape(t, t)
        return self.cache[key]

    def stored(self, key):
        return self.index[key][1] > 0

    def window(self, r0, r1, c0, c1):
        out = np.empty((r1 - r0, c1 - c0), np.uint8)
        t, tx = self.tile, self.w // self.tile
        for r in range(r0, r1):
            for c in range(c0, c1):
                cc = c % self.w
                out[r - r0, c - c0] = self.tile_at(r // t * tx + cc // t)[r % t, cc % t]
        return out


def report(ras, m, src60, written, minimum=DEFAULT_MIN_ELEVATION, fade=DEFAULT_FADE):
    ras60 = Raster(src60, 60) if os.path.exists(src60) else None
    sf = SummitsFile() if written else None
    print("summit           real  30s max  60s max  2' ref max  ref+excess  tile stored")
    for name, lat, lon, real in SUMMITS:
        v = ras.window_max(lat, lon)[0]
        v60 = ras60.window_max(lat, lon)[0] if ras60 else float("nan")
        r0, r1, c0, c1 = window(lat, lon, RES)
        ref = reference(m, r0, r1, c0, c1)
        if sf:
            rec = ref + STEP * sf.window(r0, r1, c0, c1).astype(np.float32)
            keys = {r // TILE * TX + (c % W) // TILE for r in (r0, r1 - 1) for c in (c0, c1 - 1)}
            st = "yes" if all(sf.stored(k) for k in keys) else ("partly" if any(sf.stored(k) for k in keys) else "no")
        else:
            fine = fine_rows(ras, r0, r1)[:, c0:c1]
            rec = ref + STEP * filter_excess(excess_bytes(fine, ref), fine, minimum, fade).astype(np.float32)
            st = "-"
        print(f"{name:14s} {real:6d} {v:8.0f} {v60:8.0f} {float(ref.max()):11.0f} {float(rec.max()):11.0f}  {st}")
    if sf:
        # round trip: the Everest tile from the file against the bytes computed from the source
        r, c = int((90 - 27.988) * RES), int((86.925 + 180) * RES)
        ty, tx = r // TILE, c // TILE
        r0, r1 = ty * TILE, (ty + 1) * TILE
        fine = fine_rows(ras, r0, r1)
        q = filter_excess(excess_bytes(fine, reference(m, r0, r1)), fine, minimum, fade)[:, tx * TILE:(tx + 1) * TILE]
        dec = sf.tile_at(ty * TX + tx)
        print(f"round trip tile {ty * TX + tx} (row {ty}, col {tx}): {'identical' if np.array_equal(dec, q) else 'MISMATCH'}, "
              f"max byte {int(dec.max())} ({int(dec.max()) * STEP} m)")


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src", nargs="?", help="the 30\" GeoTIFF (default: <rawdata>/" + SRC.replace(os.sep, "/") + ")")
    ap.add_argument("--rawdata", default=RAW, help="the raw data folder (default: rawdata/ in the repository)")
    ap.add_argument("--min-max-elevation", type=int, default=DEFAULT_MIN_MAX,
                    help=f"store a tile only if its highest 30\" cell is at least this many metres (default {DEFAULT_MIN_MAX})")
    ap.add_argument("--quality", type=int, default=11, help="brotli quality (9..11, default 11)")
    ap.add_argument("--min-elevation", type=int, default=DEFAULT_MIN_ELEVATION, help="minimum cell elevation for added relief (0 disables cell filtering; also use --min-max-elevation 500 for legacy coverage)")
    ap.add_argument("--fade-metres", type=int, default=DEFAULT_FADE, help="elevation interval over which the added relief fades in")
    ap.add_argument("--check", action="store_true", help="report sizes and summits without writing summits.bin")
    args = ap.parse_args()
    if args.min_elevation < 0 or args.fade_metres < 0:
        ap.error("elevation and fade must be non-negative")
    sys.stdout.reconfigure(encoding="utf-8")
    args.src = args.src or os.path.join(args.rawdata, SRC)
    ras = Raster(args.src, RES)
    log(f"{args.src}: {ras.registration}, nodata {ras.nodata}")
    m = coarse_means(ras)
    tiles = bake(ras, m, args.quality, min(args.min_max_elevation, *SIZE_THRESHOLDS), args.min_elevation, args.fade_metres)
    header = 24 + 8 * TX * TY
    for th in sorted(set(SIZE_THRESHOLDS + (args.min_max_elevation,))):
        sel = [len(b) for top, b in tiles.values() if top >= th]
        print(f"  max >= {th} m: {len(sel)} tiles, {(header + sum(sel)) / 1e6:.2f} MB")
    if not args.check:
        write(tiles, args.min_max_elevation)
    report(ras, m, os.path.join(args.rawdata, SRC60), not args.check, args.min_elevation, args.fade_metres)
