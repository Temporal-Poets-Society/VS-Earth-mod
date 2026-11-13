"""
Sea referee: where GSHHG has sea that the Copernicus GLO-90 water body mask (WBM) and heights say is clearly land, Earth's
sea data is made land.

GSHHG's full-resolution shorelines (coasts.bin, islands.bin) have sea in places the WBM and GLO-90 heights have as land,
often hundreds of metres up and under RGI glaciers: the fjords at the Grey and Balmaceda glaciers and around the Southern
Patagonian Icefield, parts of Vanuatu and Kamchatka. The game then cuts a sea fjord through mountains and glaciers. The
referee takes the WBM's side there:

  correction = Earth sea (outside coasts.bin's big land and GSHHG's small islands)
             ∩ WBM not ocean (land, or a lake or river pixel)
             ∩ (GLO-90 height ≥ HEIGHT_M  or  RGI glacier over half the 30" cell, earthdata/glaciers.bin)
  opened with a 3x3 square (strips one or two 3" pixels wide along a coast are a shoreline offset, not a fjord), with the
  low WBM land it encloses filled in, and kept per connected area of MIN_KM2 or more. Low flats and lagoons (heights near
  0) stay as GSHHG has them. Greenland and the high Arctic (water_atlas.polar: floating ice, an unreliable mask) and south
  of 60°S are left alone.

The areas are polygonised (pixel runs as rectangles, unioned per 1° tile, simplified by about a pixel, grown by one
pixel so they close against the GSHHG outline they border), unioned into the outlines coasts.bin is baked from
(bake_earth.coast_rings, with the GSHHG small islands they touch) and coasts.bin is written by bake_earth.write_coasts.
The 2' grid (terrain.bin) cells flagged ocean whose sea the correction mostly removes (at least half the cell's GSHHG sea,
leaving under half the cell sea) become land: FlagOcean cleared, elevation the GLO-90 mean height of the cell's WBM land
(at least a metre above the water level), and the water level of the land around recomputed as the bake does (nearest
water within 3 cells). Every other cell, and every untouched 6° tile's bytes, stay as they are.

    .venv/Scripts/python -I tools/sea_referee.py [--workers 16] [--only lat0,lon0,lat1,lon1] [--write] [--pictures]

Without --write it finds the areas and writes the report only (preview/water-atlas/sea-referee/). --write first copies
earthdata/coasts.bin and terrain.bin to preview/water-atlas/orig/ (once: an existing copy there is kept as the original),
then rewrites them from those originals. --pictures renders before/after pictures of PICTURES from orig/ and earthdata/.
"""
import argparse, json, math, os, pickle, shutil, struct, sys, time
from multiprocessing import Pool

import brotli
import numpy as np
import scipy.ndimage as ndi
import shapefile
import shapely
import tifffile
from PIL import Image, ImageDraw

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import water_atlas as wa  # noqa: E402

ROOT = wa.ROOT
EARTH = os.path.join(ROOT, "earthdata")
OUTDIR = os.path.join(wa.OUTDIR, "sea-referee")
ORIG = os.path.join(wa.OUTDIR, "orig")
REPORT = os.path.join(ROOT, "docs", "research", "water-atlas-2026-11-06", "sea-referee.txt")

HEIGHT_M = 15.0           # GLO-90 height a WBM land pixel needs to count as clearly land (without a glacier)
GLACIER = 128             # glaciers.bin cover (0..255) of the 30" cell: over half
TOP_M = 60.0              # ... and its highest pixel at least this high (not a forest canopy on a low coast)
MIN_KM2 = 0.2             # smaller areas are left as they are
MIN_PX = 6
REOPEN_KM = 2.5           # WBM sea that GSHHG has as land this close to an area made land is opened (0: never)
REOPEN_MIN_KM2 = 0.05
REOPEN_WHOLE_KM2 = 30.0   # ... and the whole of a piece of GSHHG land on WBM sea up to this size that reaches that close
GRID = 30                 # terrain.bin cells per degree
CELL_FLIP_LEFT = 0.5      # a grid ocean cell becomes land when less than this share of it is sea after the correction
CELL_FLIP_TAKEN = 0.5     # ... and the correction took at least this share of its GSHHG sea
PICTURES = [("grey", "Grey glacier", -51.0, -73.3), ("balmaceda", "Balmaceda glacier", -51.45, -73.40),
            ("vanuatu", "Maewo, Vanuatu", -15.13, 168.11), ("kamchatka", "Kamchatka", None, None)]
REGIONS = [  # (name, lat0, lat1, lon0, lon1): the first box holding an area's centre
    ("Patagonia and Tierra del Fuego", -57, -40, -80, -60),
    ("Southern Andes coast (Chile, 40°S-18°S)", -40, -18, -80, -65),
    ("New Zealand", -48, -33, 165, 179.99),
    ("Melanesia and the Pacific islands", -25, 0, 140, 180),
    ("New Guinea north, Indonesia, Philippines", -11, 22, 92, 140),
    ("Kamchatka, Kurils, Russian Far East", 40, 75, 130, 180),
    ("Chukotka and Bering Sea", 55, 75, -180, -160),
    ("Alaska and British Columbia", 47, 72, -160, -120),
    ("Arctic Canada and Labrador", 50, 75, -100, -75),
    ("Norway, Svalbard, northern Europe", 55, 75, -10, 45),
    ("Mediterranean and Black Sea", 30, 47, -10, 45),
    ("Japan and Korea", 30, 46, 124, 146),
]

T0 = time.time()


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


def region_of(lat, lon):
    for name, a0, a1, o0, o1 in REGIONS:
        if a0 <= lat < a1 and o0 <= lon < o1:
            return name
    return "elsewhere"


# ------------------------------------------------------------------------------------------- coasts.bin, read back

def unvarint(b):
    """All unsigned LEB128 varints in a byte string, as int64."""
    a = np.frombuffer(b, np.uint8)
    if a.size == 0:
        return np.zeros(0, np.int64)
    end = (a & 0x80) == 0
    grp = np.concatenate([[0], np.cumsum(end)[:-1]])
    start = np.concatenate([[0], np.flatnonzero(end)[:-1] + 1])
    pos = np.arange(a.size) - start[grp]
    vals = (a & 0x7F).astype(np.int64) << (7 * pos)
    return np.add.reduceat(vals, start)


def smooth_run(p, cut=0.0015, passes=2):
    """A run of a coasts.bin tile rounded off as the game does when it decodes one (CoastLayer.Smooth: corner cutting,
    ends kept, nothing cut on 180°): the tile states are worked out on the smoothed outline, so the land/sea read
    back from the tile needs it too."""
    for _ in range(passes):
        keep = np.r_[(np.diff(p, axis=0) != 0).any(axis=1), True]
        p = p[keep]
        n = len(p)
        if n < 2:
            return p
        a, b = p[:-1], p[1:]
        k = np.cos(np.radians(a[:, 1]))
        L = np.hypot((b[:, 0] - a[:, 0]) * k, b[:, 1] - a[:, 1])
        f = np.where(L > 0, np.minimum(0.25, cut / np.maximum(L, 1e-300)), 0.25)
        on = lambda x: (np.abs(np.abs(x) - 180) < 1e-9) | (np.abs(np.abs(x) - 540) < 1e-9)
        f[on(a[:, 0]) | on(b[:, 0])] = 0
        u = a + f[:, None] * (b - a)
        v = b - f[:, None] * (b - a)
        mid = np.stack([u, v], axis=1).reshape(-1, 2)
        p = np.vstack([p[:1], mid, p[-1:]])
    return p


class CoastFile:
    """coasts.bin as written by bake_earth.write_coasts: tile states and each tile's edges (degrees, unsmoothed)."""

    def __init__(self, path):
        with open(path, "rb") as f:
            self.b = f.read()
        assert self.b[:4] == b"EMCO"
        _, self.unit, self.tdeg, self.tx, self.ty, self.margin = struct.unpack_from("<iiiiii", self.b, 4)
        n = self.tx * self.ty
        self.states = np.frombuffer(self.b, np.uint8, n, 28)
        self.off = {}
        o = 28 + n
        for t in np.flatnonzero((self.states != 255) & ((self.states & 2) != 0)):
            self.off[int(t)] = struct.unpack_from("<ii", self.b, o)
            o += 8

    def tile(self, lat0, lon0):
        return (lat0 + 90) // self.tdeg * self.tx + (lon0 + 180) // self.tdeg

    def blob(self, t):
        if t not in self.off:
            return b""
        o, ln = self.off[t]
        return self.b[o:o + ln]

    def edges(self, t):
        """(x0, y0, x1, y1) arrays in degrees of the tile's edges."""
        if t not in self.off:
            return np.zeros((0, 4))
        v = unvarint(brotli.decompress(self.blob(t)))
        nr = int(v[0])
        counts = v[1:1 + nr]
        z = v[1 + nr:]
        z = (z >> 1) ^ -(z & 1)
        d = z.reshape(-1, 2)
        ox = (t % self.tx) * self.tdeg - 180
        oy = (t // self.tx) * self.tdeg - 90
        out = []
        s = 0
        for c in counts:
            c = int(c)
            p = smooth_run(np.cumsum(d[s:s + c], axis=0) / self.unit + [ox, oy])
            out.append(np.column_stack([p[:-1], p[1:]]))
            s += c
        return np.concatenate(out) if out else np.zeros((0, 4))

    def land(self, lat0, lon0, rows, cols):
        """Land (bool rows x cols) at the pixel centres of the 1° tile at (lat0, lon0), row 0 at its north edge, by the
        game's rule: from the tile's centre (state bit 0) up or down its middle to the row, then along the row."""
        t = self.tile(lat0, lon0)
        st = int(self.states[t])
        if st == 255:
            return np.zeros((rows, cols), bool)
        centre = bool(st & 1)
        if not st & 2:
            return np.full((rows, cols), centre)
        e = self.edges(t)
        x0, y0, x1, y1 = e[:, 0], e[:, 1], e[:, 2], e[:, 3]
        cx, cy = lon0 + 0.5, lat0 + 0.5
        ys = lat0 + 1 - (np.arange(rows) + 0.5) / rows
        xs = lon0 + (np.arange(cols) + 0.5) / cols
        m = (x0 > cx) != (x1 > cx)
        up = np.sort(y0[m] + (cx - x0[m]) / (x1[m] - x0[m]) * (y1[m] - y0[m]))
        row_land = centre ^ (np.abs(np.searchsorted(up, ys) - np.searchsorted(up, cy)) % 2 == 1)
        out = np.empty((rows, cols), bool)
        lo, hi = np.minimum(y0, y1), np.maximum(y0, y1)
        order = np.argsort(lo)
        lo_s = lo[order]
        for j, y in enumerate(ys):
            k = order[:np.searchsorted(lo_s, y, side="right")]
            k = k[hi[k] > y]
            k = k[(y0[k] > y) != (y1[k] > y)]
            xc = np.sort(x0[k] + (y - y0[k]) / (y1[k] - y0[k]) * (x1[k] - x0[k]))
            n = np.abs(np.searchsorted(xc, xs) - np.searchsorted(xc, cx))
            out[j] = row_land[j] ^ (n % 2 == 1)
        return out


# ------------------------------------------------------------------------------------------- terrain.bin, read back

class TerrainFile:
    """terrain.bin (bake_earth.write_terrain): 6° tiles of elevation code, level, flags, ice, decoded on demand."""

    def __init__(self, path):
        with open(path, "rb") as f:
            self.b = f.read()
        assert self.b[:4] == b"EMTR"
        self.version, self.w, self.h, self.t, self.count = struct.unpack_from("<iiiii", self.b, 4)
        self.tx = self.w // self.t
        self.idx = [struct.unpack_from("<ii", self.b, 24 + 8 * k) for k in range(self.count)]
        self.cache = {}

    def raw(self, k):
        o, ln = self.idx[k]
        return self.b[o:o + ln]

    def planes(self, k):
        if k not in self.cache:
            t = self.t
            r = brotli.decompress(self.raw(k))
            n2 = 2 * t * t
            res = np.frombuffer(r[:n2], "<i2").reshape(t, t).astype(np.int64)
            elev = np.cumsum(np.cumsum(res, axis=0), axis=1).astype(np.int32)
            lvl = np.frombuffer(r[n2:2 * n2], "<i2").reshape(t, t).copy()
            flags = np.frombuffer(r[2 * n2:2 * n2 + t * t], np.uint8).reshape(t, t).copy()
            ires = np.frombuffer(r[2 * n2 + t * t:], "<i2").reshape(t, t).astype(np.int64)
            ice = np.cumsum(np.cumsum(ires, axis=0), axis=1).astype(np.int32)
            self.cache[k] = {"elev": elev, "lvl": lvl, "flags": flags, "ice": ice}
        return self.cache[k]

    @staticmethod
    def encode(p):
        import bake_earth as be
        return (be.paeth_residual(p["elev"]).astype("<i2").tobytes() + p["lvl"].astype("<i2").tobytes()
                + p["flags"].astype(np.uint8).tobytes() + be.paeth_residual(p["ice"]).astype("<i2").tobytes())

    def write(self, path, changed):
        """The file with the tiles in `changed` re-encoded from their (edited) planes; the others' bytes as they are."""
        blobs = []
        for k in range(self.count):
            if k in changed:
                blobs.append(brotli.compress(self.encode(self.cache[k]), quality=11, lgwin=24))
            else:
                blobs.append(self.raw(k))
        with open(path, "wb") as f:
            f.write(b"EMTR")
            f.write(struct.pack("<iiiii", self.version, self.w, self.h, self.t, len(blobs)))
            off = 4 + 20 + 8 * len(blobs)
            for b in blobs:
                f.write(struct.pack("<ii", off, len(b)))
                off += len(b)
            for b in blobs:
                f.write(b)


# ------------------------------------------------------------------------------------------- per tile

G = {}


def verify_tile(args):
    """Earth sea on WBM land up high (or under a glacier), and Earth land on WBM ocean, in km², by the coasts and
    islands files given: the before/after check of one tile."""
    name, coasts_path, islands_path = args
    t = read_tile(name)
    if t is None:
        return None
    lat0, lon0, wbm, dem, glac, *_ = t
    hp, wp = wbm.shape
    if coasts_path not in G:
        G[coasts_path] = CoastFile(coasts_path)
    if islands_path not in G:
        isl = IslandFile(islands_path)
        start = np.concatenate([[0], np.cumsum([len(p) for p in isl.pts])]).astype(np.int64)
        box = np.array([[p[:, 0].min(), p[:, 1].min(), p[:, 0].max(), p[:, 1].max()] for p in isl.pts])
        G[islands_path] = (np.concatenate(isl.pts), start, box)
    G["isl_pts"], G["isl_start"], G["isl_box"] = G[islands_path]
    land = G[coasts_path].land(lat0, lon0, hp, wp) | small_island_land(lat0, lon0, hp, wp)
    lat_r = lat0 + 1 - (np.arange(hp) + 0.5) / hp
    px = ((111.32 / hp) * (111.32 / wp) * np.cos(np.radians(lat_r)))[:, None]
    high = ~land & (wbm != 1) & ((dem >= HEIGHT_M) | glac)
    return name, float((high * px).sum()), float(((land & (wbm == 1)) * px).sum()), float(((~land & (wbm != 1)) * px).sum())


def small_islands_path():
    return os.path.join(OUTDIR, "cache", "small-islands.npz")


def init_worker(coasts_path):
    G["sea"] = np.load(os.path.join(wa.CACHE, "sea.npy"), mmap_mode="r")
    G["glaciers"] = np.load(os.path.join(wa.CACHE, "glaciers.npy"), mmap_mode="r")
    G["coasts"] = CoastFile(coasts_path)
    z = np.load(small_islands_path())
    G["isl_pts"], G["isl_start"], G["isl_box"] = z["pts"], z["start"], z["box"]


def read_tile(name):
    lat0, lon0 = wa.tile_origin(name)
    p = os.path.join(wa.WBM, name[:-4] + "_WBM.tif")
    if not os.path.exists(p):
        return None
    wbm = tifffile.imread(p)
    if not (wbm != 1).any():
        return None
    dem = tifffile.imread(os.path.join(wa.DEM, name + ".tif")).astype(np.float32)
    dem[dem < -1000] = 0
    hp, wp = wbm.shape
    R = wa.RES
    r0, c0 = int(round((90 - (lat0 + 1)) * R)), int(round((lon0 + 180) * R)) % wa.W
    ri, ci = np.arange(hp) * R // hp, np.arange(wp) * R // wp
    glac = (np.asarray(G["glaciers"][r0:r0 + R, c0:c0 + R]) >= GLACIER)[ri[:, None], ci[None, :]]
    return lat0, lon0, wbm, dem, glac, r0, c0, ri, ci


def scan(name):
    """Phase 1, cheap: could this tile hold a correction? (the 30" Earth sea, grown by 3 cells, over WBM land up high)"""
    lat0, lon0 = wa.tile_origin(name)
    if lat0 < wa.SOUTH or wa.polar(lat0 + 0.5, lon0 + 0.5):
        return None
    t = read_tile(name)
    if t is None:
        return None
    lat0, lon0, wbm, dem, glac, r0, c0, ri, ci = t
    R = wa.RES
    rr = np.arange(r0 - 3, r0 + R + 3).clip(0, wa.H - 1)
    cc = np.arange(c0 - 3, c0 + R + 3) % wa.W
    sea = np.asarray(G["sea"][rr[:, None], cc[None, :]], bool)
    sea = ndi.binary_dilation(sea, iterations=3)[3:-3, 3:-3]
    q = sea[ri[:, None], ci[None, :]] & (wbm != 1) & ((dem >= HEIGHT_M) | glac)
    px_km2 = (111.32 / wbm.shape[0]) * (111.32 / wbm.shape[1]) * math.cos(math.radians(lat0 + 0.5))
    return name if q.sum() * px_km2 >= MIN_KM2 else None


def small_island_land(lat0, lon0, hp, wp):
    b = G["isl_box"]
    sel = np.flatnonzero((b[:, 0] <= lon0 + 1) & (b[:, 2] >= lon0) & (b[:, 1] <= lat0 + 1) & (b[:, 3] >= lat0))
    img = Image.new("1", (wp, hp), 0)
    if len(sel):
        dr = ImageDraw.Draw(img)
        for i in sel:
            p = G["isl_pts"][G["isl_start"][i]:G["isl_start"][i + 1]]
            dr.polygon([((x - lon0) * wp, (lat0 + 1 - y) * hp) for x, y in p], fill=1, outline=1)
    return np.array(img, bool)


def runs_polygons(mask, lat0, lon0):
    """The mask (row 0 north) as shapely polygons in degrees: horizontal runs as rectangles, unioned."""
    hp, wp = mask.shape
    p = np.pad(mask, ((0, 0), (1, 1))).astype(np.int8)
    d = np.diff(p, axis=1)
    rs, cs = np.nonzero(d == 1)
    re, ce = np.nonzero(d == -1)
    assert (rs == re).all()
    boxes = shapely.box(lon0 + cs / wp, lat0 + 1 - (rs + 1) / hp, lon0 + ce / wp, lat0 + 1 - rs / hp)
    u = shapely.union_all(boxes)
    u = shapely.simplify(u, 0.75 / hp, preserve_topology=True)
    return [g for g in getattr(u, "geoms", [u]) if g.geom_type == "Polygon" and not g.is_empty]


def refer(name):
    """Phase 2: the tile's correction areas, their polygons, and what they do to the 2' grid cells."""
    t = read_tile(name)
    if t is None:
        return None
    lat0, lon0, wbm, dem, glac, r0, c0, ri, ci = t
    hp, wp = wbm.shape
    isl = small_island_land(lat0, lon0, hp, wp)
    land = G["coasts"].land(lat0, lon0, hp, wp) | isl
    sea = ~land
    notocean = wbm != 1
    high = dem >= HEIGHT_M
    cand = sea & notocean & (high | glac)
    raw_km2_px = cand
    kept = ndi.binary_opening(cand, structure=np.ones((3, 3), bool))
    # low WBM land and inland water the kept area encloses (valley floors, a river through it) goes with it
    kept |= ndi.binary_fill_holes(kept) & sea & notocean
    lat_r = lat0 + 1 - (np.arange(hp) + 0.5) / hp
    px_km2 = (111.32 / hp) * (111.32 / wp) * np.cos(np.radians(lat_r))
    lab, n = ndi.label(kept, structure=np.ones((3, 3), bool))
    areas = []
    if n:
        idx = np.arange(1, n + 1)
        npx = ndi.sum(np.ones_like(lab), lab, idx)
        km2 = ndi.sum(np.broadcast_to(px_km2[:, None], lab.shape), lab, idx)
        hsum = ndi.sum(dem, lab, idx)
        hmax = ndi.maximum(dem, lab, idx)
        gl = ndi.sum(glac, lab, idx)
        yy = ndi.mean(np.broadcast_to(np.arange(hp)[:, None], lab.shape), lab, idx)
        xx = ndi.mean(np.broadcast_to(np.arange(wp)[None, :], lab.shape), lab, idx)
        # clearly land: under a glacier, or high on average and somewhere well above a forest canopy (GLO-90 is a surface
        # model: mangroves and swamp forest on the Amazon, Guiana and New Guinea coasts stand 15-40 m on WBM land at sea level)
        good = (km2 >= MIN_KM2) & (npx >= MIN_PX) & (((hsum / npx >= HEIGHT_M) & (hmax >= TOP_M)) | (gl >= 0.5 * npx))
        keep_ids = idx[good]
        kept = np.isin(lab, keep_ids)
        for i in np.flatnonzero(good):
            areas.append({"lat": float(lat0 + 1 - (yy[i] + 0.5) / hp), "lon": float(lon0 + (xx[i] + 0.5) / wp),
                          "km2": float(km2[i]), "px": int(npx[i]), "hsum": float(hsum[i]), "hmax": float(hmax[i]),
                          "glac_px": int(gl[i])})
    else:
        kept = np.zeros_like(kept)
    polys = [shapely.to_wkb(p) for p in runs_polygons(kept, lat0, lon0)] if kept.any() else []
    # reopen: next to the areas made land, the WBM's own sea that GSHHG has as land becomes sea. Where GSHHG's channels
    # lie beside the WBM's (shifted by up to a few km in the Patagonian archipelago) filling only GSHHG's channel would
    # close the strait; with the WBM's channel opened it moves instead.
    reopen = np.zeros_like(kept)
    if REOPEN_KM > 0 and kept.any():
        ky, kx = 111.32 / hp, 111.32 / wp * math.cos(math.radians(lat0 + 0.5))
        near = ndi.distance_transform_edt(~kept, sampling=(ky, kx)) <= REOPEN_KM
        full = ndi.binary_opening(land & (wbm == 1), structure=np.ones((3, 3), bool))
        # a piece of GSHHG land on WBM sea that reaches into the near zone opens whole when it is small (the rest of an
        # island GSHHG has a few km off, Maewo, Medny), else only within REOPEN_KM
        labf, nf = ndi.label(full, structure=np.ones((3, 3), bool))
        if nf:
            idxf = np.arange(1, nf + 1)
            kf = ndi.sum(np.broadcast_to(px_km2[:, None], labf.shape), labf, idxf)
            reach = np.zeros(nf + 1, bool)
            reach[np.unique(labf[full & near])] = True
            reach[0] = False
            # (any size on islands.bin's small islands: the island moves to where the WBM has it)
            on_isl = ndi.sum(isl, labf, idxf) >= 0.5 * ndi.sum(np.ones_like(labf), labf, idxf)
            whole = reach & np.r_[False, (kf <= REOPEN_WHOLE_KM2) | on_isl]
            rc = (full & near) | whole[labf]
        else:
            rc = full & near
        lab2, n2 = ndi.label(rc, structure=np.ones((3, 3), bool))
        if n2:
            touch = ndi.binary_dilation(sea & ~kept, structure=np.ones((3, 3), bool)) & rc
            idx2 = np.arange(1, n2 + 1)
            k2 = ndi.sum(np.broadcast_to(px_km2[:, None], lab2.shape), lab2, idx2)
            ok = np.zeros(n2 + 1, bool)
            ok[np.unique(lab2[touch])] = True
            ok[1:] &= k2 >= REOPEN_MIN_KM2
            ok[0] = False
            reopen = ok[lab2]
    rpolys = [shapely.to_wkb(p) for p in runs_polygons(reopen, lat0, lon0)] if reopen.any() else []
    # the 2' cells (40 rows of 3" pixels; columns repeated up to a multiple of 30)
    f = math.lcm(wp, GRID) // wp
    ry, rx = hp // GRID, wp * f // GRID
    rep = (lambda a: np.repeat(a, f, axis=1)) if f > 1 else (lambda a: a)
    n_cell = ry * rx
    cells = {
        "sea": wa.block_sum(rep(sea.astype(np.int32)), ry, rx) / n_cell,
        "corr": wa.block_sum(rep(kept.astype(np.int32)), ry, rx) / n_cell,
        "reopen": wa.block_sum(rep(reopen.astype(np.int32)), ry, rx) / n_cell,
        "glac": wa.block_sum(rep(glac.astype(np.int32)), ry, rx) / n_cell,
    }
    lm = notocean
    nl = wa.block_sum(rep(lm.astype(np.int32)), ry, rx)
    cells["hland"] = np.where(nl > 0, wa.block_sum(rep(np.where(lm, dem, 0).astype(np.float64)), ry, rx) / np.maximum(nl, 1), np.nan)
    # what stays: Earth sea on high WBM land that was not taken (specks, strips, small areas)
    left = cand & ~kept
    stats = {"cand_km2": float((raw_km2_px * px_km2[:, None]).sum()), "kept_km2": float((kept * px_km2[:, None]).sum()),
             "left_km2": float((left * px_km2[:, None]).sum()),
             "reopen_km2": float((reopen * px_km2[:, None]).sum()),
             "reopen_isl_km2": float(((reopen & isl) * px_km2[:, None]).sum()),
             "kept_isl_touch_km2": 0.0}
    return {"name": name, "lat0": lat0, "lon0": lon0, "areas": areas, "polys": polys, "rpolys": rpolys, "cells": cells,
            "stats": stats}


# ------------------------------------------------------------------------------------------- outlines

class IslandFile:
    """islands.bin (bake_earth.write_islands): the grid patches (kept as bytes) and the small islands' records."""

    def __init__(self, path):
        with open(path, "rb") as f:
            b = f.read()
        assert b[:4] == b"EMIS"
        self.version, self.w, self.npatch, n = struct.unpack_from("<iiii", b, 4)
        buf = brotli.decompress(b[20:])
        a = np.frombuffer(buf, np.uint8)
        end = np.flatnonzero((a & 0x80) == 0)
        o = int(end[self.npatch - 1]) + 1 if self.npatch else 0
        self.patch_idx = np.cumsum(unvarint(buf[:o])) if self.npatch else np.zeros(0, np.int64)
        o += 5 * self.npatch
        self.patches = buf[:o]
        self.area = np.frombuffer(buf, "<f4", n, o).copy()
        self.height = np.frombuffer(buf, "<f4", n, o + 4 * n).copy()
        self.width = np.frombuffer(buf, "<f4", n, o + 8 * n).copy()
        counts = np.frombuffer(buf, "<i4", n, o + 12 * n)
        z = unvarint(buf[o + 16 * n:])
        z = (z >> 1) ^ -(z & 1)
        q = np.cumsum(z.reshape(-1, 2), axis=0)
        assert len(q) == counts.sum()
        start = np.concatenate([[0], np.cumsum(counts)])
        self.pts = [q[start[i]:start[i + 1]] / 1e5 for i in range(n)]

    def write(self, path):
        import bake_earth as be
        buf = bytearray(self.patches)
        buf += np.asarray(self.area, "<f4").tobytes()
        buf += np.asarray(self.height, "<f4").tobytes()
        buf += np.asarray(self.width, "<f4").tobytes()
        buf += np.array([len(p) for p in self.pts], "<i4").tobytes()
        q = np.round(np.concatenate(self.pts) * be.ISLAND_UNIT).astype(np.int64)
        d = np.diff(q, axis=0, prepend=np.zeros((1, 2), np.int64))
        buf += be.varint(be.zigzag(d.ravel()))
        with open(path, "wb") as f:
            f.write(b"EMIS")
            f.write(struct.pack("<iiii", self.version, self.w, self.npatch, len(self.pts)))
            f.write(brotli.compress(bytes(buf), quality=11, lgwin=24))


def load_small_islands(path=None):
    """islands.bin's small islands as (pts, start, box) arrays for the workers' rasters."""
    cache = small_islands_path()
    if path is None and os.path.exists(cache):
        return np.load(cache)
    src = path or (os.path.join(ORIG, "islands.bin") if os.path.exists(os.path.join(ORIG, "islands.bin"))
                   else os.path.join(EARTH, "islands.bin"))
    isl = IslandFile(src)
    start = np.concatenate([[0], np.cumsum([len(p) for p in isl.pts])]).astype(np.int64)
    box = np.array([[p[:, 0].min(), p[:, 1].min(), p[:, 0].max(), p[:, 1].max()] for p in isl.pts])
    os.makedirs(os.path.dirname(cache), exist_ok=True)
    np.savez(cache, pts=np.concatenate(isl.pts), start=start, box=box)
    log(f"small islands: {len(isl.pts)} from {src}")
    return np.load(cache)


def km2_of(g):
    c = g.centroid
    return g.area * 111.32 * 110.57 * math.cos(math.radians(c.y))


def island_width(pts):
    """Twice the distance (km) from the coast to the innermost point (as bake_earth.island_records)."""
    lon0, lon1 = pts[:, 0].min(), pts[:, 0].max()
    lat0, lat1 = pts[:, 1].min(), pts[:, 1].max()
    kx = 111.32 * math.cos(math.radians((lat0 + lat1) / 2))
    wkm, hkm = max((lon1 - lon0) * kx, 1e-3), max((lat1 - lat0) * 111.32, 1e-3)
    px = max(wkm, hkm) / 96
    nx, ny = int(wkm / px) + 5, int(hkm / px) + 5
    img = Image.new("L", (nx, ny), 0)
    ImageDraw.Draw(img).polygon([(2 + (x - lon0) * kx / px, 2 + (lat1 - y) * 111.32 / px) for x, y in pts], fill=1)
    m = np.array(img, dtype=bool)
    return 2 * float(ndi.distance_transform_edt(m).max()) * px if m.any() else 0.5 * min(wkm, hkm)


def polygons_of(g):
    return [p for p in getattr(g, "geoms", [g]) if p.geom_type == "Polygon" and not p.is_empty]


def merge_outlines(rings, adds, removes, isl, add_height):
    """The coasts.bin rings and islands.bin islands with the areas made land (`adds`) and the WBM sea opened (`removes`),
    both lists of disjoint shapely polygons. Areas, islands and the rings they touch are joined into groups:
      - a group with big land (coasts.bin rings: land by odd crossings, as bake_earth.coast_rings gives them): its rings
        become (the rings' land ∪ its small islands ∪ adds) − removes; its small islands stay in islands.bin, less the
        removes (the island layer would build them back over the opened sea);
      - a group without: its islands become (islands ∪ adds) − removes, each piece an island (height: the highest of the
        islands in it, or the GLO-90 top of a new one, add_height(piece)).
    Returns the new rings and a report; `isl` (IslandFile) is changed in place."""
    from shapely.strtree import STRtree
    from shapely.geometry import Polygon
    from shapely.validation import make_valid
    rbox = np.array([[p[:, 0].min(), p[:, 1].min(), p[:, 0].max(), p[:, 1].max()] for p in rings])
    rtree = STRtree(shapely.box(rbox[:, 0], rbox[:, 1], rbox[:, 2], rbox[:, 3]))
    ibox = np.array([[p[:, 0].min(), p[:, 1].min(), p[:, 0].max(), p[:, 1].max()] for p in isl.pts])
    itree = STRtree(shapely.box(ibox[:, 0], ibox[:, 1], ibox[:, 2], ibox[:, 3]))
    prepared, ipoly = {}, {}

    def ring_poly(i):
        if i not in prepared:
            g = Polygon(rings[i])
            shapely.prepare(g)
            prepared[i] = g
        return prepared[i]

    def island_poly(i):
        if i not in ipoly:
            g = Polygon(isl.pts[i])
            ipoly[i] = g if g.is_valid else make_valid(g)
        return ipoly[i]

    shapes = [("a", g) for g in adds] + [("r", g) for g in removes]
    parent = list(range(len(shapes)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    ring_of, isl_of = {}, {}
    # adds and removes touching each other (a channel moved) are one group
    stree = STRtree([g for _, g in shapes])
    for k, (_, g) in enumerate(shapes):
        for j in stree.query(g, predicate="intersects"):
            parent[find(int(j))] = find(k)
    for k, (_, c) in enumerate(shapes):
        for i in rtree.query(c):
            i = int(i)
            try:
                hit = ring_poly(i).intersects(c)
            except shapely.errors.GEOSException:
                hit = make_valid(Polygon(rings[i])).intersects(c)
            if hit:
                if i in ring_of:
                    parent[find(k)] = find(ring_of[i])
                else:
                    ring_of[i] = k
        for i in itree.query(c):
            i = int(i)
            if island_poly(i).intersects(c):
                if i in isl_of:
                    parent[find(k)] = find(isl_of[i])
                else:
                    isl_of[i] = k
    groups = {}
    for k in range(len(shapes)):
        groups.setdefault(find(k), {"a": [], "r": [], "rings": [], "isl": []})[shapes[k][0]].append(shapes[k][1])
    for i, k in ring_of.items():
        groups[find(k)]["rings"].append(i)
    for i, k in isl_of.items():
        groups[find(k)]["isl"].append(i)
    info = {"groups": len(groups), "big_land_groups": 0, "island_groups": 0, "new_islands": 0, "islands_changed": 0,
            "islands_removed": 0, "slivers": 0, "rings_replaced": 0, "rings_added": 0, "removes_unused_km2": 0.0}
    replaced, out = set(), []
    new_isl = {}   # island index -> list of (polygon, old islands in it or None) replacing it (empty: removed)
    appended = []
    for g in groups.values():
        rem = shapely.union_all(g["r"]) if g["r"] else None
        add = shapely.union_all(g["a"]) if g["a"] else None
        if g["rings"]:
            info["big_land_groups"] += 1
            region = None
            for i in sorted(g["rings"]):
                p = make_valid(Polygon(rings[i]))
                region = p if region is None else shapely.symmetric_difference(region, p)
            parts = [region] + [island_poly(i) for i in g["isl"]] + ([add] if add is not None else [])
            new = shapely.union_all(parts)
            if rem is not None:
                new = shapely.difference(new, rem)
            replaced.update(g["rings"])
            for poly in polygons_of(new):
                out.append(np.asarray(poly.exterior.coords, np.float64))
                for h in poly.interiors:
                    # slivers the joins leave between the outlines (a few pixels) are not water
                    if Polygon(h).area < 1e-6 and not any(len(rings[i]) == len(h.coords) for i in g["rings"]):
                        info["slivers"] += 1
                        continue
                    out.append(np.asarray(h.coords, np.float64))
            # its islands, less the opened sea
            if rem is not None:
                for i in g["isl"]:
                    ip = island_poly(i)
                    if ip.intersects(rem):
                        new_isl[i] = [(p, None) for p in polygons_of(shapely.difference(ip, rem))]
        else:
            info["island_groups"] += 1
            parts = [island_poly(i) for i in g["isl"]] + ([add] if add is not None else [])
            if not parts:
                info["removes_unused_km2"] += km2_of(rem)
                continue
            new = shapely.union_all(parts)
            if rem is not None:
                new = shapely.difference(new, rem)
            pieces = polygons_of(new)
            if not g["isl"]:
                info["new_islands"] += len(pieces)
                appended += [(p, []) for p in pieces]
                continue
            first = min(g["isl"])
            for i in g["isl"]:
                new_isl[i] = []
            new_isl[first] = [(p, [i for i in g["isl"] if island_poly(i).intersects(p)]) for p in pieces]

    def record(poly, olds):
        # (islands have no holes: a lake inside a merged island is land, as GSHHG's L1 islands have it)
        pts = np.asarray(poly.exterior.coords, np.float64)[:-1]
        area = km2_of(Polygon(pts))
        h = max([float(isl.height[j]) for j in olds]) if olds else add_height(poly)
        h = float(np.clip(h, 2.0, 3 + 100 * math.sqrt(area)))
        return pts, area, h, island_width(pts)

    pts, area, height, width = [], [], [], []
    for i in range(len(isl.pts)):
        if i not in new_isl:
            pts.append(isl.pts[i]); area.append(isl.area[i]); height.append(isl.height[i]); width.append(isl.width[i])
            continue
        if not new_isl[i]:
            info["islands_removed"] += 1
        else:
            info["islands_changed"] += 1
        for poly, olds in new_isl[i]:
            if km2_of(poly) < 0.01:
                continue
            r = record(poly, [i] if olds is None else olds)
            pts.append(r[0]); area.append(r[1]); height.append(r[2]); width.append(r[3])
    for poly, _ in appended:
        if km2_of(poly) < 0.01:
            continue
        r = record(poly, [])
        pts.append(r[0]); area.append(r[1]); height.append(r[2]); width.append(r[3])
    info["islands_before"] = len(isl.pts)
    isl.pts = pts
    isl.area, isl.height, isl.width = (np.array(area, np.float32), np.array(height, np.float32), np.array(width, np.float32))
    info["islands_after"] = len(isl.pts)
    new_rings = [p for i, p in enumerate(rings) if i not in replaced] + out
    info["rings_replaced"] = len(replaced)
    info["rings_added"] = len(out)
    info["removes_unused_km2"] = round(info["removes_unused_km2"], 2)
    return new_rings, info


# ------------------------------------------------------------------------------------------- the 2' grid

def update_terrain(tf, results):
    """The 2' grid under the corrections. A cell flagged ocean whose GSHHG sea the correction mostly takes (at least
    CELL_FLIP_TAKEN of it, leaving under CELL_FLIP_LEFT of the cell sea) becomes land at the GLO-90 mean height of its WBM
    land; a land cell (no ocean or lake flag) whose land the reopening mostly takes, leaving the cell sea by majority,
    becomes ocean (a few metres deep, water level 0). The land within 3 cells of a changed cell takes the level of the
    nearest water within 3 cells (or none), as the bake sets it, and stands at least a metre above it. Returns the
    changed cells and the 6° tiles that need re-encoding."""
    import bake_earth as be
    T, W, H = tf.t, tf.w, tf.h
    changed_tiles = set()

    def get(row, col, plane):
        col %= W
        return tf.planes((row // T) * tf.tx + col // T)[plane][row % T, col % T]

    def put(row, col, plane, v):
        col %= W
        k = (row // T) * tf.tx + col // T
        tf.planes(k)[plane][row % T, col % T] = v
        changed_tiles.add(k)

    to_land, to_sea = [], []
    for r in results:
        c = r["cells"]
        touched = (c["corr"] > 0) | (c["reopen"] > 0)
        if not touched.any():
            continue
        row0 = (90 - r["lat0"] - 1) * GRID
        col0 = (r["lon0"] + 180) * GRID
        for i, j in zip(*np.nonzero(touched)):
            sea, corr, reo = c["sea"][i, j], c["corr"][i, j], c["reopen"][i, j]
            after = sea - corr + reo
            f = get(row0 + i, col0 + j, "flags")
            if f & 1 and corr > 0 and after < CELL_FLIP_LEFT and corr >= CELL_FLIP_TAKEN * sea:
                to_land.append((row0 + i, col0 + j, c["hland"][i, j], c["glac"][i, j]))
            elif not f & 3 and reo > 0 and after >= CELL_FLIP_LEFT and reo >= CELL_FLIP_TAKEN * (1 - sea):
                to_sea.append((row0 + i, col0 + j))
    out = []
    for row, col, hland, glac in to_land:
        put(row, col, "flags", get(row, col, "flags") & ~np.uint8(1))
    for row, col in to_sea:
        put(row, col, "flags", get(row, col, "flags") | np.uint8(1))
        old = int(get(row, col, "elev"))
        put(row, col, "lvl", 0)
        put(row, col, "elev", int(be.encode_elev(np.array([-5.0]))[0]))
        out.append({"to": "sea", "row": row, "col": col, "lat": round(90 - (row + 0.5) / GRID, 4),
                    "lon": round((col + 0.5) / GRID - 180, 4), "elev_m": -5.0, "old_code": old})
    changed = {(r_, c_) for r_, c_, *_ in to_land} | {(r_, c_) for r_, c_ in to_sea}
    near = set()
    for row, col in changed:
        for dr in range(-3, 4):
            for dc in range(-3, 4):
                if dr * dr + dc * dc <= 9 and 0 <= row + dr < H:
                    near.add((row + dr, (col + dc) % W))

    def nearest_level(row, col):
        best, lv = None, -32768
        for dr in range(-3, 4):
            for dc in range(-3, 4):
                d2 = dr * dr + dc * dc
                if d2 > 9 or not 0 <= row + dr < H or d2 == 0:
                    continue
                if get(row + dr, col + dc, "flags") & 3 and (best is None or d2 < best):
                    l = int(get(row + dr, col + dc, "lvl"))
                    if l != -32768:
                        best, lv = d2, l
        return lv

    hl = {(r_, c_): (h, g) for r_, c_, h, g in to_land}
    lvl_changed = 0
    for row, col in sorted(near):
        if get(row, col, "flags") & 3:
            continue   # water
        nl = nearest_level(row, col)
        if (row, col) in hl:
            hland, glac = hl[(row, col)]
            old = int(get(row, col, "elev"))
            h = max(float(hland) if np.isfinite(hland) else 0.0, 1.0)
            if nl != -32768:
                h = max(h, nl + 1.0)
            put(row, col, "lvl", nl)
            put(row, col, "elev", int(be.encode_elev(np.array([h]))[0]))
            out.append({"to": "land", "row": row, "col": col, "lat": round(90 - (row + 0.5) / GRID, 4),
                        "lon": round((col + 0.5) / GRID - 180, 4), "elev_m": round(h, 1), "old_code": old, "level": nl,
                        "glacier": round(float(glac), 2)})
            continue
        if nl != int(get(row, col, "lvl")):
            put(row, col, "lvl", nl)
            lvl_changed += 1
            if nl != -32768:
                e = int(get(row, col, "elev"))
                floor = int(be.encode_elev(np.array([nl + 1.0]))[0])
                if e < floor:
                    put(row, col, "elev", floor)
    log(f"terrain: {len(to_land)} cells from sea to land, {len(to_sea)} from land to sea, {lvl_changed} land cells "
        f"around them with a new water level, {len(changed_tiles)} tiles re-encoded")
    return out, changed_tiles


def sync_island_patches(isl, tf, flips):
    """islands.bin's grid patches hold whole cells (elevation, level, flags) that replace terrain.bin's in worlds with
    the small islands: a cell the referee turned from sea to land or back whose patch still has the other kind takes
    the new grid's values there too. Returns how many."""
    n = isl.npatch
    o = len(isl.patches) - 5 * n
    buf = bytearray(isl.patches)
    pe = np.frombuffer(buf, "<i2", n, o)
    pl = np.frombuffer(buf, "<i2", n, o + 2 * n)
    pf = np.frombuffer(buf, np.uint8, n, o + 4 * n)
    pos = {int(c): i for i, c in enumerate(isl.patch_idx)}
    T, done = tf.t, 0
    for f in flips:
        row, col = f["row"], f["col"]
        i = pos.get(row * tf.w + col)
        if i is None:
            continue
        p = tf.planes((row // T) * tf.tx + col // T)
        nf = int(p["flags"][row % T, col % T])
        if (int(pf[i]) & 1) == (nf & 1):
            continue
        pe[i], pl[i], pf[i] = int(p["elev"][row % T, col % T]), int(p["lvl"][row % T, col % T]), nf
        done += 1
    isl.patches = bytes(buf)
    log(f"islands.bin: {done} grid patches take the referee's land or sea")
    return done


def patch_cells(path, magic):
    """Cell indices of a patch file (islands.bin, lakes.bin, shores.bin, outlets.bin: brotli after the header)."""
    if not os.path.exists(path):
        return np.zeros(0, np.int64)
    with open(path, "rb") as f:
        b = f.read()
    if b[:4] != magic:
        return np.zeros(0, np.int64)
    hdr = 16 if magic == b"EMOT" else 20
    n = struct.unpack_from("<i", b, 12)[0]
    raw = brotli.decompress(b[hdr:])
    a = np.frombuffer(raw, np.uint8)
    end = np.flatnonzero((a & 0x80) == 0)
    cut = end[n - 1] + 1
    return np.cumsum(unvarint(raw[:cut]))


# ------------------------------------------------------------------------------------------- pictures

def picture(key, title, lat, lon, half=0.25):
    """Old and new Earth sea over the WBM around (lat, lon): preview/water-atlas/sea-referee/<key>.png."""
    old_c, new_c = CoastFile(os.path.join(ORIG, "coasts.bin")), CoastFile(os.path.join(EARTH, "coasts.bin"))
    old_t, new_t = TerrainFile(os.path.join(ORIG, "terrain.bin")), TerrainFile(os.path.join(EARTH, "terrain.bin"))
    G["glaciers"] = np.load(os.path.join(wa.CACHE, "glaciers.npy"), mmap_mode="r")

    def islands_of(path):
        isl = IslandFile(path)
        start = np.concatenate([[0], np.cumsum([len(p) for p in isl.pts])]).astype(np.int64)
        box = np.array([[p[:, 0].min(), p[:, 1].min(), p[:, 0].max(), p[:, 1].max()] for p in isl.pts])
        return np.concatenate(isl.pts), start, box

    old_i, new_i = islands_of(os.path.join(ORIG, "islands.bin")), islands_of(os.path.join(EARTH, "islands.bin"))
    halfx = half / max(math.cos(math.radians(lat)), 0.2)
    la0, la1, lo0, lo1 = lat - half, lat + half, lon - halfx, lon + halfx
    hp = 1200
    wp = None
    tiles = []
    for tl in range(math.floor(la0), math.floor(la1 - 1e-9) + 1):
        for tn in range(math.floor(lo0), math.floor(lo1 - 1e-9) + 1):
            tiles.append((tl, tn))
    # a common pixel size: the finest column count among the tiles
    names = {}
    for tl, tn in tiles:
        nm = f"Copernicus_DSM_COG_30_{'N' if tl >= 0 else 'S'}{abs(tl):02d}_00_{'E' if tn >= 0 else 'W'}{abs(tn):03d}_00_DEM"
        names[(tl, tn)] = nm
    rows = int(round((la1 - la0) * hp))
    wps = []
    for k, nm in names.items():
        p = os.path.join(wa.WBM, nm[:-4] + "_WBM.tif")
        if os.path.exists(p):
            wps.append(tifffile.TiffFile(p).pages[0].shape[1])
    wp = max(wps) if wps else 1200
    cols = int(round((lo1 - lo0) * wp))
    ys = la1 - (np.arange(rows) + 0.5) / hp
    xs = lo0 + (np.arange(cols) + 0.5) / wp
    wbm = np.ones((rows, cols), np.uint8)
    dem = np.zeros((rows, cols), np.float32)
    glac = np.zeros((rows, cols), bool)
    sea_old = np.ones((rows, cols), bool)
    sea_new = np.ones((rows, cols), bool)
    for (tl, tn), nm in names.items():
        rsel = np.flatnonzero((ys >= tl) & (ys < tl + 1))
        csel = np.flatnonzero((xs >= tn) & (xs < tn + 1))
        if not len(rsel) or not len(csel):
            continue
        p = os.path.join(wa.WBM, nm[:-4] + "_WBM.tif")
        if os.path.exists(p):
            w = tifffile.imread(p)
            d = tifffile.imread(os.path.join(wa.DEM, nm + ".tif"))
            th, tw = w.shape
            ri = ((tl + 1 - ys[rsel]) * th).astype(int).clip(0, th - 1)
            ci = ((xs[csel] - tn) * tw).astype(int).clip(0, tw - 1)
            wbm[np.ix_(rsel, csel)] = w[np.ix_(ri, ci)]
            dem[np.ix_(rsel, csel)] = d[np.ix_(ri, ci)]
        R = wa.RES
        gr = ((90 - ys[rsel]) * R).astype(int)
        gc = ((xs[csel] + 180) * R).astype(int) % wa.W
        glac[np.ix_(rsel, csel)] = np.asarray(G["glaciers"][gr[:, None], gc[None, :]]) >= GLACIER
        ri2 = ((tl + 1 - ys[rsel]) * 1200).astype(int).clip(0, 1199)
        ci2 = ((xs[csel] - tn) * wp).astype(int).clip(0, wp - 1)
        for cf, ii, sea in ((old_c, old_i, sea_old), (new_c, new_i, sea_new)):
            G["isl_pts"], G["isl_start"], G["isl_box"] = ii
            land = cf.land(tl, tn, 1200, wp) | small_island_land(tl, tn, 1200, wp)
            sea[np.ix_(rsel, csel)] = ~land[np.ix_(ri2, ci2)]

    def grid_ocean(tf):
        gr = ((90 - ys) * GRID).astype(int)
        gc = ((xs + 180) * GRID).astype(int)
        out = np.zeros((rows, cols), bool)
        for r_ in np.unique(gr):
            for c_ in np.unique(gc):
                k = (r_ // tf.t) * tf.tx + c_ // tf.t
                f = tf.planes(k)["flags"][r_ % tf.t, c_ % tf.t]
                if f & 1:
                    out[np.ix_(gr == r_, gc == c_)] = True
        return out

    def panel(sea, ocean2, label):
        shade = np.clip(90 + dem / 1500 * 150, 90, 240).astype(np.uint8)
        img = np.stack([shade, shade, shade], -1).astype(np.float32)
        img[glac & (wbm != 1)] = img[glac & (wbm != 1)] * 0.5 + np.array([235, 245, 255]) * 0.5
        img[wbm == 1] = [25, 55, 110]
        img[wbm == 2] = [60, 120, 200]
        img[wbm == 3] = [80, 160, 230]
        bad = sea & (wbm != 1)
        img[bad] = img[bad] * 0.35 + np.array([230, 30, 30]) * 0.65
        edge = sea & ~ndi.binary_erosion(sea, border_value=1)
        img[edge] = [255, 230, 0]
        # the 2' grid's ocean cells: a magenta dot at each cell centre
        gr = ((90 - ys) * GRID).astype(int)
        gc = ((xs + 180) * GRID).astype(int)
        mid_r = np.flatnonzero(np.abs(((90 - ys) * GRID) % 1 - 0.5) < 0.5 / (hp / GRID) + 1e-9)
        mid_c = np.flatnonzero(np.abs(((xs + 180) * GRID) % 1 - 0.5) < 0.5 / (wp / GRID) + 1e-9)
        for r_ in mid_r:
            for c_ in mid_c:
                if ocean2[r_, c_]:
                    img[max(r_ - 1, 0):r_ + 2, max(c_ - 1, 0):c_ + 2] = [255, 0, 255]
        im = Image.fromarray(img.clip(0, 255).astype(np.uint8))
        d = ImageDraw.Draw(im)
        d.rectangle([0, 0, im.width, 14], fill=(0, 0, 0))
        d.text((4, 2), label, fill=(255, 255, 255))
        return im

    a = panel(sea_old, grid_ocean(old_t), f"{title} {lat:.2f},{lon:.2f}: before (GSHHG)")
    b = panel(sea_new, grid_ocean(new_t), "after the sea referee")
    out = Image.new("RGB", (a.width * 2 + 6, a.height + 30), (255, 255, 255))
    out.paste(a, (0, 0))
    out.paste(b, (a.width + 6, 0))
    d = ImageDraw.Draw(out)
    d.text((4, a.height + 4), "grey: WBM land by GLO-90 height; white tint: RGI glacier; blues: WBM ocean / lake / river; "
                              "red: Earth sea on WBM land; yellow: Earth sea edge; magenta dots: 2' grid ocean cells",
           fill=(0, 0, 0))
    os.makedirs(OUTDIR, exist_ok=True)
    path = os.path.join(OUTDIR, f"{key}.png")
    out.save(path)
    km2 = lambda m: float(m.sum()) * (111.32 / hp) * (111.32 / wp) * math.cos(math.radians(lat))
    stats = {"before_sea_on_wbm_land_km2": round(km2(sea_old & (wbm != 1)), 1),
             "after_sea_on_wbm_land_km2": round(km2(sea_new & (wbm != 1)), 1),
             "before_high": round(km2(sea_old & (wbm != 1) & ((dem >= HEIGHT_M) | glac)), 1),
             "after_high": round(km2(sea_new & (wbm != 1) & ((dem >= HEIGHT_M) | glac)), 1)}
    log(f"picture {path}: {stats}")
    return path, stats


# ------------------------------------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--workers", type=int, default=16)
    ap.add_argument("--only", help="lat0,lon0,lat1,lon1: only tiles in this box")
    ap.add_argument("--write", action="store_true", help="rewrite earthdata/coasts.bin and terrain.bin")
    ap.add_argument("--pictures", action="store_true", help="only render the before/after pictures")
    ap.add_argument("--rescan", action="store_true", help="ignore the cached areas")
    ap.add_argument("--verify", action="store_true", help="before/after totals over the tiles with areas")
    ap.add_argument("--out", help="with --write: write the three files here instead of earthdata/ (a trial)")
    a = ap.parse_args()
    os.makedirs(os.path.join(OUTDIR, "cache"), exist_ok=True)
    if a.verify:
        res = pickle.load(open(os.path.join(OUTDIR, "cache", f"results-{int(HEIGHT_M)}.pkl"), "rb"))
        names = sorted(r["name"] for r in res)
        out = {}
        for label, d in (("before", ORIG), ("after", EARTH)):
            with Pool(a.workers, initializer=init_worker, initargs=(os.path.join(ORIG, "coasts.bin"),)) as pool:
                rr = [x for x in pool.imap_unordered(verify_tile, [(n, os.path.join(d, "coasts.bin"), os.path.join(d, "islands.bin")) for n in names], chunksize=4) if x]
            out[label] = {"sea_on_high_wbm_land_km2": round(sum(x[1] for x in rr), 1),
                          "land_on_wbm_ocean_km2": round(sum(x[2] for x in rr), 1),
                          "sea_on_any_wbm_land_km2": round(sum(x[3] for x in rr), 1)}
            log(f"{label}: {out[label]}")
        out["tiles"] = len(names)
        json.dump(out, open(os.path.join(OUTDIR, "verify.json"), "w"), indent=1)
        return
    if a.pictures:
        rep = json.load(open(os.path.join(OUTDIR, "areas.json"), encoding="utf8"))
        pics = {}
        for key, title, lat, lon in PICTURES:
            if lat is None:
                # the biggest area in the region named
                cand = [x for x in rep["areas"] if x["region"].startswith(title)]
                if not cand:
                    continue
                lat, lon = cand[0]["lat"], cand[0]["lon"]
            pics[key] = picture(key, title, lat, lon)[1]
        json.dump(pics, open(os.path.join(OUTDIR, "pictures.json"), "w"), indent=1)
        return
    src = ORIG if os.path.exists(os.path.join(ORIG, "coasts.bin")) else EARTH
    coasts_path, terrain_path = os.path.join(src, "coasts.bin"), os.path.join(src, "terrain.bin")
    log(f"originals from {src}")
    wa.build_glaciers()
    load_small_islands()
    tag = "" if not a.only else "-" + a.only.replace(",", "_")
    cache = os.path.join(OUTDIR, "cache", f"results{tag}-{int(HEIGHT_M)}.pkl")
    if os.path.exists(cache) and not a.rescan:
        results = pickle.load(open(cache, "rb"))
        log(f"{len(results)} tiles with areas from {cache}")
    else:
        tiles = sorted(f[:-4] for f in os.listdir(wa.DEM) if f.endswith("_DEM.tif"))
        if a.only:
            la0, lo0, la1, lo1 = map(float, a.only.split(","))
            tiles = [t for t in tiles if la0 - 1 < wa.tile_origin(t)[0] <= la1 and lo0 - 1 < wa.tile_origin(t)[1] <= lo1]
        sea = np.load(os.path.join(wa.CACHE, "sea.npy"), mmap_mode="r")
        any_sea = np.zeros((180, 360), bool)
        for r in range(180):
            any_sea[r] = np.asarray(sea[r * wa.RES:(r + 1) * wa.RES]).reshape(wa.RES, 360, wa.RES).max(axis=(0, 2)) > 0
        tiles = [t for t in tiles if any_sea[89 - wa.tile_origin(t)[0], wa.tile_origin(t)[1] + 180]]
        log(f"{len(tiles)} tiles with Earth sea")
        with Pool(a.workers, initializer=init_worker, initargs=(coasts_path,)) as pool:
            cand = [t for t in pool.imap_unordered(scan, tiles, chunksize=8) if t]
            log(f"{len(cand)} tiles may hold a correction")
            results = [r for r in pool.imap_unordered(refer, sorted(cand), chunksize=2) if r and r["areas"]]
        pickle.dump(results, open(cache, "wb"))
        log(f"{len(results)} tiles with areas")

    # ---- areas: the tiles' polygons joined across tile edges, grown by a pixel (closes them against GSHHG's outline)
    polys = [shapely.from_wkb(p) for r in results for p in r["polys"]]
    grown = shapely.buffer(np.array(polys, dtype=object), 1 / 1200, join_style="mitre", mitre_limit=2.0)
    u = shapely.union_all(grown)
    u = shapely.clip_by_rect(u, -180, -90, 180, 90)
    corrections = [g for g in getattr(u, "geoms", [u]) if g.geom_type == "Polygon" and not g.is_empty]
    log(f"{len(polys)} tile polygons -> {len(corrections)} areas")
    # area statistics: the tiles' pieces summed into the area holding them
    from shapely.strtree import STRtree
    tree = STRtree(corrections)
    rpolys = [shapely.from_wkb(p) for r in results for p in r.get("rpolys", [])]
    removes = []
    if rpolys:
        u = shapely.union_all(shapely.buffer(np.array(rpolys, dtype=object), 1 / 1200, join_style="mitre", mitre_limit=2.0))
        u = shapely.clip_by_rect(u, -180, -90, 180, 90)
        removes = [g for g in getattr(u, "geoms", [u]) if g.geom_type == "Polygon" and not g.is_empty]
    log(f"{len(rpolys)} tile polygons -> {len(removes)} areas of WBM sea opened")
    agg = [{"km2": 0.0, "px": 0, "hsum": 0.0, "hmax": -1e9, "glac_px": 0, "wlat": 0.0, "wlon": 0.0} for _ in corrections]
    for r in results:
        for x in r["areas"]:
            pt = shapely.Point(x["lon"], x["lat"])
            hit = tree.query(pt, predicate="intersects")
            if not len(hit):
                hit = [tree.nearest(pt)]
            s = agg[int(hit[0])]
            s["km2"] += x["km2"]; s["px"] += x["px"]; s["hsum"] += x["hsum"]; s["hmax"] = max(s["hmax"], x["hmax"])
            s["glac_px"] += x["glac_px"]; s["wlat"] += x["lat"] * x["km2"]; s["wlon"] += x["lon"] * x["km2"]
    areas = []
    for s in agg:
        if s["km2"] <= 0:
            continue
        lat, lon = s["wlat"] / s["km2"], s["wlon"] / s["km2"]
        areas.append({"lat": round(lat, 4), "lon": round(lon, 4), "km2": round(s["km2"], 2),
                      "height_m": round(s["hsum"] / s["px"], 1), "max_m": round(s["hmax"], 0),
                      "glacier": round(s["glac_px"] / s["px"], 2), "region": region_of(lat, lon)})
    areas.sort(key=lambda x: -x["km2"])
    stats = {k: sum(r["stats"].get(k, 0) for r in results) for k in ("cand_km2", "kept_km2", "left_km2", "reopen_km2", "reopen_isl_km2")}
    regions = {}
    for x in areas:
        g = regions.setdefault(x["region"], {"areas": 0, "km2": 0.0})
        g["areas"] += 1
        g["km2"] += x["km2"]
    report = {"height_m": HEIGHT_M, "glacier_cover": GLACIER / 255, "min_km2": MIN_KM2, "only": a.only,
              "areas_n": len(areas), "km2": round(sum(x["km2"] for x in areas), 1),
              "candidate_km2": round(stats["cand_km2"], 1), "left_km2": round(stats["left_km2"], 1),
              "reopen_km2": round(stats["reopen_km2"], 1), "reopen_in_small_islands_km2": round(stats["reopen_isl_km2"], 1),
              "regions": {k: {"areas": v["areas"], "km2": round(v["km2"], 1)} for k, v in sorted(regions.items(), key=lambda kv: -kv[1]["km2"])},
              "areas": areas}

    # ---- rivers whose line runs over the new land (HydroRIVERS, 100 m³/s and more)
    rv = np.load(os.path.join(wa.CACHE, "rivers.npy"))
    ends = shapely.points(rv[:, 2], rv[:, 3])
    on = tree.query(ends, predicate="intersects")
    report["river_segments_on_new_land"] = int(len(np.unique(on[0]))) if len(on) else 0
    flips, changed_tiles = [], set()
    if a.write:
        import bake_earth as be
        os.makedirs(ORIG, exist_ok=True)
        for f in ("coasts.bin", "terrain.bin", "islands.bin"):
            if not os.path.exists(os.path.join(ORIG, f)) and not a.out:
                shutil.copy2(os.path.join(EARTH, f), os.path.join(ORIG, f))
                log(f"backed up {f} to {ORIG}")
        src = ORIG if os.path.exists(os.path.join(ORIG, "terrain.bin")) else EARTH
        tf = TerrainFile(os.path.join(src, "terrain.bin"))
        flags = np.zeros((tf.h, tf.w), np.uint8)
        for k in range(tf.count):
            r_, c_ = divmod(k, tf.tx)
            flags[r_ * tf.t:(r_ + 1) * tf.t, c_ * tf.t:(c_ + 1) * tf.t] = tf.planes(k)["flags"]
        tf.cache.clear()
        rings = be.coast_rings(flags)
        del flags
        isl = IslandFile(os.path.join(src, "islands.bin"))
        hpts = [(x["lon"], x["lat"], x["hmax"]) for r in results for x in r["areas"]]
        htree = STRtree(shapely.points([h[0] for h in hpts], [h[1] for h in hpts]))

        def add_height(poly):
            hit = htree.query(poly, predicate="intersects")
            return max([hpts[int(i)][2] for i in hit], default=10.0)

        new_rings, info = merge_outlines(rings, corrections, removes, isl, add_height)
        report["outlines"] = info
        log(f"outlines: {info}")
        # rings that fall to fewer than 4 distinct vertices at coasts.bin's unit (specks of a few m²) are left out
        keep = []
        for p in new_rings:
            q = np.round(p * be.COAST_UNIT).astype(np.int64)
            if len(np.unique(q, axis=0)) >= 4:
                keep.append(p)
        info["rings_dropped_degenerate"] = len(new_rings) - len(keep)
        new_rings = keep
        dest = a.out or EARTH
        os.makedirs(dest, exist_ok=True)
        be.OUT = dest
        be.write_coasts(new_rings)
        flips, changed_tiles = update_terrain(tf, results)
        tf.write(os.path.join(dest, "terrain.bin"), changed_tiles)
        report["island_patches_synced"] = sync_island_patches(isl, tf, flips)
        isl.write(os.path.join(dest, "islands.bin"))
        report["terrain_cells_to_land"] = sum(1 for f_ in flips if f_["to"] == "land")
        report["terrain_cells_to_sea"] = sum(1 for f_ in flips if f_["to"] == "sea")
        report["terrain_tiles_rewritten"] = len(changed_tiles)
        # patch files whose cells overlap the changed cells
        W = tf.w
        flipped = np.array([f_["row"] * W + f_["col"] for f_ in flips], np.int64)
        over = {}
        for fn, magic in (("islands.bin", b"EMIS"), ("lakes.bin", b"EMLK"), ("shores.bin", b"EMSH"), ("outlets.bin", b"EMOT")):
            idx = IslandFile(os.path.join(dest, fn)).patch_idx if fn == "islands.bin" else patch_cells(os.path.join(EARTH, fn), magic)
            over[fn] = int(np.isin(flipped, idx).sum())
        report["patch_overlap"] = over
        log(f"patch files holding changed cells: {over}")
        report["terrain_cells"] = [{k: (v.item() if hasattr(v, "item") else v) for k, v in f_.items()} for f_ in flips[:200]]
    json.dump(report, open(os.path.join(OUTDIR, f"areas{tag}.json"), "w", encoding="utf8"), ensure_ascii=False, indent=1)
    write_report(report, os.path.join(OUTDIR, f"sea-referee{tag}.txt") if a.only or not a.write or a.out else REPORT)
    log("done")


def write_report(rep, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf8") as f:
        f.write("Sea referee (tools/sea_referee.py): Earth sea (GSHHG) that the GLO-90 water body mask and heights say is "
                "land, made land\n")
        f.write(f"rule: Earth sea ∩ WBM not ocean ∩ (GLO-90 ≥ {rep['height_m']:.0f} m or RGI glacier over half the 30\" cell), "
                f"opened 3x3, enclosed low land filled, areas ≥ {rep['min_km2']} km²; not Greenland/high Arctic, not south of 60°S\n")
        if rep.get("only"):
            f.write(f"box only: {rep['only']}\n")
        f.write(f"areas: {rep['areas_n']}, {rep['km2']:.1f} km² made land "
                f"(candidate pixels {rep['candidate_km2']:.1f} km², left as sea {rep['left_km2']:.1f} km²: strips, specks)\n")
        f.write(f"WBM sea opened beside them (GSHHG land, WBM ocean, within {REOPEN_KM} km): {rep['reopen_km2']:.1f} km², "
                f"{rep['reopen_in_small_islands_km2']:.1f} km² of it on islands.bin's small islands\n")
        if "terrain_cells_to_land" in rep:
            f.write(f"terrain.bin: {rep['terrain_cells_to_land']} 2' cells from ocean to land, {rep['terrain_cells_to_sea']} from "
                    f"land to ocean, in {rep['terrain_tiles_rewritten']} 6° tiles\noutlines: {rep['outlines']}\n")
            f.write(f"patch files holding changed cells (their values win where the option is on): {rep['patch_overlap']}\n")
        f.write(f"HydroRIVERS segments (100 m³/s+) ending on the new land: {rep['river_segments_on_new_land']}\n\n")
        f.write("per region:\n")
        for k, v in rep["regions"].items():
            f.write(f"  {v['km2']:9.1f} km²  {v['areas']:5d} areas  {k}\n")
        f.write("\ntop 30 areas:\n")
        for x in rep["areas"][:30]:
            f.write(f"  {x['lat']:9.4f},{x['lon']:10.4f}  {x['km2']:8.2f} km²  mean {x['height_m']:6.1f} m, max {x['max_m']:5.0f} m, "
                    f"glacier {int(x['glacier'] * 100):3d}%  {x['region']}\n")
    log(f"report {path}")


if __name__ == "__main__":
    main()
