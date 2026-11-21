"""
River courses rules 4 (decision 0063 rule 1, 8 November 2026): every reach onto the real channel, smoothed off the pixel
centres, with the water mask's half width at each vertex.

Courses rules 2 (tools/snap_rivers_wbm.py) moved only reaches from 100 m³/s, and below 300 m³/s only where a good share
of them was on the mask's water already, so the Gauja (90 m³/s), the Tiber at Rome (195-220) and the Hawizeh marsh's
outflow at Al-Qurna (244) kept courses that cut across meander necks or run beside the real channel; and the courses it
moved sat on the mask's pixel centres, turning by 0°, 45° or 90° only (45° staircase banks at 1:10).

Here every reach of rivers-glo90.bin (all are 20 m³/s or more, Streams' floor) is moved onto the Copernicus GLO-90 water
body mask's water (WBM, 3") where that lies near:
 - the cheapest 8-connected way (a diagonal step costs its true length) from the reach's first to its last vertex within
   a corridor of CORRIDOR_MIN..CORRIDOR_MAX m round the old course. Mask water (river and lake; the sea for rivers from
   OCEAN_Q, smaller ones only right beside their course) is cheap, cheapest in mid-channel; land is dear, dearer the
   higher GLO-90's ground stands above the course's level there, so a gap in the mask is crossed on the lowest ground;
   both a little dearer away from the old course;
 - another river's water is not this reach's: water nearer the course of a reach not joined to this one and not much
   smaller (by more than FOREIGN_RATIO), water a river FOREIGN_RATIO times bigger was already moved onto (bigger rivers
   go first), and below SMALL_Q mask river wider than such a river can be (WIDTH_CAP);
 - the new course is kept when clearly wetter (MIN_GAIN more of its length within half a pixel of its water) and at most
   MAX_LONGER as long plus 1 km (a meander loop round a neck the old course cut across);
 - it is smoothed off the pixel centres (a Gaussian of SMOOTH_PX pixels along the way, ends pinned), held within HOLD_PX
   of the water it was on, and simplified to SIMPLIFY_PX; its inner vertices are put on water_overlays.GRID (1.7 m), which
   rivers-glo90-snap-2.bin stores as whole steps.
Reach ends (confluences, mouths) stay where they are, so the network is unchanged; levels and lake/sea flags are carried
over by the share of the way along (snap_rivers_wbm.carry). The game's small islands (islands.bin) count as land.

Every reach also gets the mask's half width at each vertex (record version 3: RiverReach.Width in WIDTH_UNIT m, 1..255;
0 where no mask river water is near): the largest half width of the mask's river water (class 3; its distance transform
less half a pixel, in metres with the pixel's true size) over the course's pixels within WIDTH_WINDOW_M of the vertex
along the course; gaps up to WIDTH_GAP_M between measured vertices interpolated, unmeasured ends up to WIDTH_END_M taking
the nearest measured value; in a confined gorge the valley floor's half width where that is smaller (GORGE_RISE below).
A width is never made where the mask has none.

    set EARTH_RAWDATA=<rawdata with copernicus-glo90 and copernicus-glo90-wbm>
    .venv/Scripts/python -I tools/snap_rivers_channel.py <rivers-glo90.bin> <out .bin> [--workers 4]
        [--only lat0,lon0,lat1,lon1] [--islands earthdata/islands.bin]

Writes EMRV record version 3 (same reaches, order and links; every vertex with its width), <out>.json (counts) and
<out>.moved.npz (per moved reach: index, discharge, wet shares and lengths before and after). tools/river_arms.py then
builds the side arms on it (their widths measured the same way, widths_for), and tools/water_overlays.py
--snapped2/--armed2 makes rivers-glo90-snap-2.bin and rivers-glo90-arms-2.bin.
"""
import argparse, json, math, os, sys, time
from collections import OrderedDict
from multiprocessing import Pool

import numpy as np
import scipy.ndimage as ndi
import scipy.sparse as sp
from scipy.sparse.csgraph import dijkstra
import tifffile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import water_atlas as wa
import snap_rivers_wbm as v1
import water_overlays as wo
import glo90grid as gg

MIN_Q = 20.0                # m³/s: Streams' floor; rivers-glo90.bin has nothing smaller
PX_M = 111320.0 / 1200      # a 3" pixel north-south (m)
CORRIDOR_MIN, CORRIDOR_MAX = 2500.0, 5000.0   # m round the old course (Al-Qurna: 1-2 km off; the Tiber's loop 1.3 km)
LAND_COST, WATER_OFF_COST = 8.0, 0.5
LAND_OFF_M = 1000.0         # land costs LAND_COST more for each (distance from the old course / this)²: across a gap in
                            # the mask the new course keeps to the old one (the Tigris at 31.42 N: the mask's few pixels
                            # lie along the old course's bend, the straight way across it is wrong)
HEIGHT_COST, HEIGHT_CAP = 0.5, 30.0           # land: more per metre of ground above the course's level (up to the cap)
OCEAN_Q, OCEAN_NEAR_PX = 100.0, 2             # below OCEAN_Q the sea is a river's only within this of its course
SMALL_Q = 300.0                               # below this, mask river wider than WIDTH_CAP(q) is a bigger river's
FOREIGN_RATIO = 3.0
NEAR_SLACK_M = 150.0        # water is another course's when that is nearer by more than this
MIN_GAIN = 0.05             # share of the length within HOLD_PX of the reach's water: the new course this much more
WET = 0.97                  # reaches with this share already stay
MAX_LONGER = 2.0            # and at most this much longer (plus 1 km)
SMOOTH_PX, HOLD_PX, SIMPLIFY_PX = 1.0, 0.5, 0.15
WIDTH_UNIT = 5.0            # RiverReach.WidthUnit
WIDTH_WINDOW_M, WIDTH_GAP_M, WIDTH_END_M = 250.0, 5000.0, 2000.0
# Gorges (the author, 8 November: "where river narrows irl and goes deeper we should try similar, but need to keep also
# some width"): where the valley is confined, its floor's half width when narrower than the mask's (a river narrower than
# a mask pixel shows as one pixel). GLO-90 is a surface model (trees, buildings 10-30 m), so a valley counts as confined
# only where the ground on both sides rises GORGE_RISE above the water within GORGE_REACH_M, at every point within
# WIDTH_WINDOW_M along the course (forested lowland valleys, the Gauja's 60-85 m deep one among them, fail it); the floor
# reaches to where the ground first rises FLOOR_RISE above the water (GLO-90 flattens the mask's water; its land noise is
# a few metres) and stays up for FLOOR_HOLD_M. The water: GLO-90's lowest ground within LEVEL_M across the course (the
# courses' levels are de-dammed, below a reservoir's surface).
GORGE_RISE, GORGE_REACH_M = 50.0, 500.0
FLOOR_RISE, FLOOR_HOLD_M, FLOOR_MAX_M = 5.0, 45.0, 600.0
LEVEL_M, GORGE_STEP_M, GORGE_SAMPLE_M = 150.0, 50.0, 15.0
MAX_WINDOW_DEG2 = 6.0       # a group's window larger than this is split (rivers-glo90.bin's largest: 3.2)
VT = v1.VERTEX[3]
T0 = time.time()
G = {}


def log(m):
    print(f"[{time.time() - T0:8.1f}s] {m}", flush=True)


def radius_m(q):
    return min(max(CORRIDOR_MIN, 500.0 + 32 * math.sqrt(q)), CORRIDOR_MAX)


def width_cap(q):
    """The widest half width (m) of mask river a river of q m³/s can be: four times its real one (8·√q wide), 150 m at least."""
    return max(150.0, 16 * math.sqrt(q))


# ---- the mask and the heights on the 3" grid (tiles cached per worker)

TILES = OrderedDict()


def tile(kind, tl, tc):
    key = (kind, tl, tc)
    if key in TILES:
        TILES.move_to_end(key)
        return TILES[key]
    t = raw_tile(kind, tl, tc)
    if t is not None:
        # narrow high-latitude tiles on the uniform 3" grid (glo90grid.columns; on the pixel-is-point grid a tile's last
        # column or two take the next tile's first)
        east = None
        if (gg.column_index(1200, t.shape[1]) >= t.shape[1]).any():
            east = raw_tile(kind, tl, ((tc + 1 + 180) % 360) - 180)
            if east is None:
                east = np.full((t.shape[0], 1), 1 if kind == "WBM" else 0, t.dtype)
        t = gg.columns(t, 1200, east)
        t = np.ascontiguousarray(t if kind == "WBM" else t.astype(np.float32))
    TILES[key] = t
    while len(TILES) > 24:
        TILES.popitem(last=False)
    return t


def raw_tile(kind, tl, tc):
    """A GLO-90 tile as stored, or None where there is none (open sea)."""
    name = f"Copernicus_DSM_COG_30_{'N' if tl >= 0 else 'S'}{abs(tl):02d}_00_{'E' if tc >= 0 else 'W'}{abs(tc):03d}_00_{kind}.tif"
    p = os.path.join(wa.WBM if kind == "WBM" else wa.DEM, name)
    return tifffile.imread(p) if os.path.exists(p) else None


def mosaic(kind, la0, lo0, la1, lo1):
    """WBM classes (1, the sea, where no tile) or GLO-90 heights (0 where no tile) over a box on a uniform 3" grid."""
    r = 1200
    H_, W_ = int(round((la1 - la0) * r)), int(round((lo1 - lo0) * r))
    out = np.full((H_, W_), 1, np.uint8) if kind == "WBM" else np.zeros((H_, W_), np.float32)
    for tl in range(math.floor(la0), math.ceil(la1)):
        for tc in range(math.floor(lo0), math.ceil(lo1)):
            t = tile(kind, tl, ((tc + 180) % 360) - 180)
            if t is None:
                continue
            y0, x0 = int(round((la1 - (tl + 1)) * r)), int(round((tc - lo0) * r))
            ys, xs, ye, xe = max(y0, 0), max(x0, 0), min(y0 + r, H_), min(x0 + r, W_)
            if ye > ys and xe > xs:
                out[ys:ye, xs:xe] = t[ys - y0:ye - y0, xs - x0:xe - x0]
    return out


def half_widths(river, dy, dx):
    """Half width (m) of the mask's river at each of its pixels: the distance to the nearest pixel that isn't river less
    half the pixel's extent that way (anisotropic pixels); 0 off the river."""
    out = np.zeros(river.shape, np.float32)
    if not river.any() or river.all():
        return out
    d, (ir, ic) = ndi.distance_transform_edt(river, sampling=(dy, dx), return_indices=True)
    rr, cc = np.nonzero(river)
    vy, vx = (ir[rr, cc] - rr) * dy, (ic[rr, cc] - cc) * dx
    L = d[rr, cc]
    out[rr, cc] = L - 0.5 * (np.abs(vy) * dy + np.abs(vx) * dx) / np.maximum(L, 1e-6)
    return out


class Window:
    """The rasters of a box on the 3" grid (its first row on la1, its first column on lo0). A pixel's area is (oy - r/1200
    .. oy - (r + 1)/1200, ox + c/1200 .. ox + (c + 1)/1200): oy, ox from glo90grid (half a pixel north and west of
    la1, lo0 on the pixel-is-point grid)."""

    def __init__(self, la0, lo0, la1, lo1, full=True):
        self.la0, self.lo0, self.la1, self.lo1 = la0, lo0, la1, lo1
        self.oy, self.ox = gg.top(la1), gg.left(lo0, 1200)
        self.dx = PX_M * max(math.cos(math.radians(0.5 * (la0 + la1))), 0.02)
        self.samp = (PX_M, self.dx)
        W = mosaic("WBM", la0, lo0, la1, lo1)
        W[v1.island_land(W, self.oy, self.ox)] = 0
        self.shape = W.shape
        self.water = W > 0
        self.river = W == 3
        self.hw = half_widths(self.river, PX_M, self.dx)
        self.dem = mosaic("DEM", la0, lo0, la1, lo1)
        self._relief = None
        if not full:
            return
        self.ocean = (W == 1) | (W == 255)
        self.inner = (ndi.distance_transform_edt(self.water, sampling=self.samp) / PX_M).astype(np.float32)

    def frac(self, lat, lon):
        """Fractional row and column (pixel (r, c) spans r..r+1, c..c+1)."""
        return (self.oy - lat) * 1200, (lon - self.ox) * 1200

    @property
    def relief(self):
        """GLO-90's highest ground within about GORGE_REACH_M less its lowest within a pixel: where this is under
        GORGE_RISE no valley side can rise that far (most rivers: no gorge to measure)."""
        if self._relief is None:
            k = (2 * int(math.ceil(GORGE_REACH_M / PX_M)) + 1, 2 * int(math.ceil(GORGE_REACH_M / self.dx)) + 1)
            self._relief = ndi.maximum_filter(self.dem, size=k) - ndi.minimum_filter(self.dem, size=3)
        return self._relief


def near_mask(fr, fc, mask, tol=HOLD_PX):
    """Whether each point (fractional row, column) lies within tol pixels of a pixel of mask."""
    H, W = mask.shape
    r0, c0 = np.floor(fr).astype(np.int64), np.floor(fc).astype(np.int64)
    ok = np.zeros(len(fr), bool)
    for dr in (0, -1, 1):
        for dc in (0, -1, 1):
            r, c = r0 + dr, c0 + dc
            ins =(r >= 0) & (r < H) & (c >= 0) & (c < W)
            m = np.zeros(len(fr), bool)
            m[ins] = mask[r[ins], c[ins]]
            ey = np.maximum(np.abs(fr - (r + 0.5)) - 0.5, 0)
            ex = np.maximum(np.abs(fc - (c + 0.5)) - 0.5, 0)
            ok |= m & (np.hypot(ey, ex) <= tol)
    return ok


def resample(P, h):
    """A polyline (n, 2) resampled every h along its length, ends kept."""
    s = np.r_[0, np.cumsum(np.hypot(*np.diff(P, axis=0).T))]
    m = max(int(math.ceil(s[-1] / h)), 1)
    t = np.linspace(0, s[-1], m + 1)
    return np.c_[np.interp(t, s, P[:, 0]), np.interp(t, s, P[:, 1])]


def smooth_hold(P, ok, dx):
    """The pixel path P (metres: y = row·PX_M, x = column·dx, local) smoothed by a Gaussian along its length with its
    ends pinned, held within HOLD_PX of ok where the path itself is."""
    h = 0.25 * min(PX_M, dx)
    Q = resample(P, h)
    m = len(Q) - 1
    if m < 4:
        return Q
    sig = SMOOTH_PX * math.sqrt(PX_M * dx) / h
    k = min(int(math.ceil(3 * sig)) + 1, m)
    pad = np.r_[2 * Q[0] - Q[k:0:-1], Q, 2 * Q[-1] - Q[-2:-k - 2:-1]]   # odd reflection: the ends stay put
    S = ndi.gaussian_filter1d(pad, sig, axis=0, mode="nearest")[k:k + m + 1]
    S[0], S[-1] = Q[0], Q[-1]
    to_rc = lambda X: (X[:, 0] / PX_M, X[:, 1] / dx)
    raw_ok = near_mask(*to_rc(Q), ok)
    alpha = np.ones(m + 1)
    X = S
    for _ in range(8):
        X = Q + alpha[:, None] * (S - Q)
        bad = raw_ok & ~near_mask(*to_rc(X), ok)
        if not bad.any():
            return X
        alpha[bad] *= 0.5
        alpha = ndi.minimum_filter1d(alpha, 5)
    X = Q + alpha[:, None] * (S - Q)
    bad = raw_ok & ~near_mask(*to_rc(X), ok)
    X[bad] = Q[bad]
    return X


def graph_path(cost, ok, dx, s_rc, e_rc):
    """Cheapest 8-connected way through ok from s_rc to e_rc (each step costs its length times the mean of its ends'
    costs), as pixel rows and columns, or None."""
    idx = -np.ones(ok.shape, np.int64)
    n = int(ok.sum())
    idx[ok] = np.arange(n)
    s, e = idx[s_rc], idx[e_rc]
    if s < 0 or e < 0:
        return None
    rows, cols, wts = [], [], []
    diag = math.hypot(dx, PX_M)
    for dr, dc, L in ((0, 1, dx), (1, 0, PX_M), (1, 1, diag), (1, -1, diag)):
        if dc < 0:
            A, B = idx[:idx.shape[0] - dr, -dc:], idx[dr:, :idx.shape[1] + dc]
            CA, CB = cost[:cost.shape[0] - dr, -dc:], cost[dr:, :cost.shape[1] + dc]
        else:
            A, B = idx[:idx.shape[0] - dr, :idx.shape[1] - dc], idx[dr:, dc:]
            CA, CB = cost[:cost.shape[0] - dr, :cost.shape[1] - dc], cost[dr:, dc:]
        m = (A >= 0) & (B >= 0)
        rows.append(A[m]); cols.append(B[m]); wts.append(0.5 * (CA[m] + CB[m]) * L)
    Gm = sp.csr_matrix((np.concatenate(wts), (np.concatenate(rows), np.concatenate(cols))), shape=(n, n))
    dist, pred = dijkstra(Gm, directed=False, indices=int(s), return_predecessors=True)
    if not np.isfinite(dist[e]):
        return None
    path = [int(e)]
    while path[-1] != s:
        path.append(int(pred[path[-1]]))
    path.reverse()
    pr, pc = np.nonzero(ok)
    return pr[path], pc[path]


# ---- widths

def width_bytes(m):
    return np.where(m > 0, np.clip(np.round(m / WIDTH_UNIT), 1, 255), 0).astype(np.uint8)


def bilinear(a, fr, fc):
    """a sampled at fractional rows and columns (pixel centres at r + 0.5), clamped to the raster."""
    H, W = a.shape
    y = np.clip(fr - 0.5, 0, H - 1.001)
    x = np.clip(fc - 0.5, 0, W - 1.001)
    r, c = y.astype(np.int64), x.astype(np.int64)
    wy, wx = y - r, x - c
    return (a[r, c] * (1 - wy) * (1 - wx) + a[r, c + 1] * (1 - wy) * wx
            + a[r + 1, c] * wy * (1 - wx) + a[r + 1, c + 1] * wy * wx)


def gorge_widths(lat, lon, S, win):
    """Per vertex: the confined valley floor's half width (m) round the course, inf where the valley isn't confined at
    every point within WIDTH_WINDOW_M along it (the largest floor half width there)."""
    n = len(lat)
    out = np.full(n, np.inf)
    if n < 2 or S[-1] <= 0:
        return out
    t = np.r_[np.arange(0, S[-1], GORGE_STEP_M), S[-1]]
    dl, do = np.interp(t, S, lat), np.interp(t, S, lon)
    fr, fc = win.frac(dl, do)
    H, W = win.shape
    ins = (fr >= 0) & (fr < H) & (fc >= 0) & (fc < W)
    hw = np.full(len(t), np.inf)
    cand = np.flatnonzero(ins)
    cand = cand[win.relief[fr[cand].astype(np.int64), fc[cand].astype(np.int64)] >= GORGE_RISE]
    if len(cand):
        k = np.clip(np.searchsorted(S, t[cand], "right") - 1, 0, n - 2)
        vy = (lat[k + 1] - lat[k]) * 111320
        vx = (lon[k + 1] - lon[k]) * 111320 * np.cos(np.radians(lat[k]))
        L = np.hypot(vy, vx)
        ok = L > 0
        cand, vy, vx, L = cand[ok], vy[ok], vx[ok], L[ok]
        ny, nx = -vx / L, vy / L        # to the left of the flow (north, east metres)
        d = np.arange(0, FLOOR_MAX_M + GORGE_SAMPLE_M / 2, GORGE_SAMPLE_M)
        Z = []
        for side in (1, -1):
            sr = fr[cand][:, None] - side * d[None, :] * ny[:, None] / PX_M
            sc = fc[cand][:, None] + side * d[None, :] * nx[:, None] / win.dx
            Z.append(bilinear(win.dem, sr, sc))
        near = d <= LEVEL_M
        level = np.minimum(Z[0][:, near].min(1), Z[1][:, near].min(1))
        reach = d <= GORGE_REACH_M
        confined = (Z[0][:, reach].max(1) >= level + GORGE_RISE) & (Z[1][:, reach].max(1) >= level + GORGE_RISE)
        hold = int(round(FLOOR_HOLD_M / GORGE_SAMPLE_M)) + 1
        jj = np.arange(len(d))
        end = np.minimum(jj + hold, len(d))
        edges = []
        for Zs in Z:
            up = Zs >= (level + FLOOR_RISE)[:, None]
            cs = np.concatenate([np.zeros((len(up), 1), np.int32), np.cumsum(up, axis=1, dtype=np.int32)], axis=1)
            stay = (cs[:, end] - cs[:, jj]) == (end - jj)[None, :]   # up from here for FLOOR_HOLD_M (or to the end)
            has = stay.any(1)
            j = np.argmax(stay, axis=1)
            zj, zp = Zs[np.arange(len(j)), j], Zs[np.arange(len(j)), np.maximum(j - 1, 0)]
            thr = level + FLOOR_RISE
            frac_ = np.where(j > 0, np.clip((thr - zp) / np.maximum(zj - zp, 1e-6), 0, 1), 0)
            e = np.where(j > 0, d[np.maximum(j - 1, 0)] + frac_ * GORGE_SAMPLE_M, 0.0)
            edges.append(np.where(has, e, np.inf))
        g = 0.5 * (edges[0] + edges[1])
        hw[cand] = np.where(confined, g, np.inf)
    lo = np.searchsorted(t, S - WIDTH_WINDOW_M, "left")
    hi = np.searchsorted(t, S + WIDTH_WINDOW_M, "right")
    for v, (a, b) in enumerate(zip(lo, hi)):
        if b > a:
            out[v] = hw[a:b].max()
    return out


def vertex_widths(lat, lon, win):
    """The half width at each vertex of a course, in metres: the mask's (the largest of its river's half width over the
    course within WIDTH_WINDOW_M along it, gaps filled; 0 where none) and the one written (the mask's or a confined
    gorge's floor, the smaller, at least one WIDTH_UNIT). A gorge only narrows a mask width, never makes one: channel
    rules 1 read a width as the mask's river water being there (the arid rules never dry it)."""
    m = mask_widths(lat, lon, win)
    S = arc_m(np.asarray(lat, np.float64), np.asarray(lon, np.float64))
    g = gorge_widths(np.asarray(lat, np.float64), np.asarray(lon, np.float64), S, win)
    return m, np.where(m > 0, np.maximum(np.minimum(m, g), WIDTH_UNIT), 0.0)


def arc_m(lat, lon):
    if len(lat) < 2:
        return np.zeros(len(lat))
    return np.r_[0, np.cumsum(np.hypot(np.diff(lat) * 111320, np.diff(lon) * 111320 * np.cos(np.radians(lat[:-1]))))]


def mask_widths(lat, lon, win):
    """The mask's half width (m) at each vertex of a course from the window's half-width raster: the largest over the
    course within WIDTH_WINDOW_M along it, gaps filled."""
    hw = win.hw
    lat, lon = np.asarray(lat, np.float64), np.asarray(lon, np.float64)
    n = len(lat)
    if n == 1:
        S = np.zeros(1)
        t, dl, do = S, lat, lon
    else:
        kx = np.cos(np.radians(lat[:-1]))
        S = np.r_[0, np.cumsum(np.hypot(np.diff(lat) * 111320, np.diff(lon) * 111320 * kx))]
        step = 0.2 * min(PX_M, win.dx)
        t = np.r_[np.arange(0, S[-1], step), S[-1]]
        dl, do = np.interp(t, S, lat), np.interp(t, S, lon)
    fr, fc = win.frac(dl, do)
    rr, cc = np.floor(fr).astype(np.int64), np.floor(fc).astype(np.int64)
    H, W = hw.shape
    ins = (rr >= 0) & (rr < H) & (cc >= 0) & (cc < W)
    v = np.zeros(len(t), np.float32)
    v[ins] = hw[rr[ins], cc[ins]]
    lo = np.searchsorted(t, S - WIDTH_WINDOW_M, "left")
    hi = np.searchsorted(t, S + WIDTH_WINDOW_M, "right")
    m = np.array([v[a:b].max() if b > a else 0.0 for a, b in zip(lo, hi)], np.float64)
    meas = np.flatnonzero(m > 0)
    if len(meas) and len(meas) < n:
        out = m.copy()
        for j in range(len(meas) - 1):
            a, b = meas[j], meas[j + 1]
            if b > a + 1 and S[b] - S[a] <= WIDTH_GAP_M:
                out[a + 1:b] = np.interp(S[a + 1:b], [S[a], S[b]], [m[a], m[b]])
        a, b = meas[0], meas[-1]
        lead = np.arange(a)
        out[lead[S[a] - S[lead] <= WIDTH_END_M]] = m[a]
        trail = np.arange(b + 1, n)
        out[trail[S[trail] - S[b] <= WIDTH_END_M]] = m[b]
        m = out
    return m


# ---- workers

def init_worker(src, npz):
    v1.init_worker(npz)
    if src:
        R = v1.read_full(src)
        G["R"] = R
        G["box"] = np.array([[a["lat"].min(), a["lon"].min(), a["lat"].max(), a["lon"].max()] for _, _, a in R], np.float32)
        G["q"] = np.array([q for q, _, _ in R], np.float32)
        nxt = np.array([n for _, n, _ in R], np.int64)
        G["next"] = nxt
        order = np.argsort(nxt, kind="stable")
        G["inflow"] = (order, nxt[order])


def inflows(r):
    order, srt = G["inflow"]
    return order[np.searchsorted(srt, r, "left"):np.searchsorted(srt, r, "right")]


def family(r):
    """The reach, the one it flows into, and the ones flowing into either: all one river at their joins."""
    n = int(G["next"][r])
    f = [np.array([r]), inflows(r)]
    if n >= 0:
        f += [np.array([n]), inflows(n)]
    return np.unique(np.concatenate(f))


def dense(lat, lon, step):
    """Points every step (m) along a course, with each point's share of the way along."""
    if len(lat) == 1:
        return lat.copy(), lon.copy(), np.zeros(1)
    kx = np.cos(np.radians(lat[:-1]))
    S = np.r_[0, np.cumsum(np.hypot(np.diff(lat) * 111320, np.diff(lon) * 111320 * kx))]
    t = np.r_[np.arange(0, S[-1], step), S[-1]]
    return np.interp(t, S, lat), np.interp(t, S, lon), (t / S[-1] if S[-1] > 0 else np.zeros(len(t)))


def nearest_courses(win, items_box):
    """Over the window: the reach whose course is nearest each pixel (the bigger where courses share a pixel) and how far."""
    la0, lo0, la1, lo1 = win.la0, win.lo0, win.la1, win.lo1
    B = G["box"]
    sel = np.flatnonzero((B[:, 2] >= la0) & (B[:, 0] <= la1) & (B[:, 3] >= lo0) & (B[:, 1] <= lo1))
    sel = sel[np.argsort(G["q"][sel], kind="stable")]
    H, W = win.shape
    cid = np.full((H, W), -1, np.int32)
    for r in sel:
        a = G["R"][r][2]
        dl, do, _ = dense(a["lat"].astype(np.float64), a["lon"].astype(np.float64), 30.0)
        fr, fc = win.frac(dl, do)
        rr, cc = np.floor(fr).astype(np.int64), np.floor(fc).astype(np.int64)
        m = (rr >= 0) & (rr < H) & (cc >= 0) & (cc < W)
        cid[rr[m], cc[m]] = r
    if (cid < 0).all():
        return np.full((H, W), -1, np.int32), np.full((H, W), np.inf, np.float32)
    d, (ir, ic) = ndi.distance_transform_edt(cid < 0, sampling=win.samp, return_indices=True)
    return cid[ir, ic], d.astype(np.float32)


def snap_one(i, win, near, Dn, claim_q, claim_id):
    """A new course for reach i ((lat, lon, old share, new share, old length, new length), why) or (None, why)."""
    q, _, a = G["R"][i]
    lat, lon, lev = a["lat"].astype(np.float64), a["lon"].astype(np.float64), a["lev"].astype(np.float64)
    if len(a) < 2:
        return None, "one vertex"
    R_ = radius_m(q)
    dx = win.dx
    rlat, rlon = R_ / 111320, R_ / (111320 * max(math.cos(math.radians(float(lat.mean()))), 0.05))
    H, W = win.shape
    r0 = max(int((win.oy - (lat.max() + rlat)) * 1200), 0)
    r1 = min(int(math.ceil((win.oy - (lat.min() - rlat)) * 1200)), H)
    c0 = max(int((lon.min() - rlon - win.ox) * 1200), 0)
    c1 = min(int(math.ceil((lon.max() + rlon - win.ox) * 1200)), W)
    if r1 - r0 < 3 or c1 - c0 < 3:
        return None, "edge"
    sl = (slice(r0, r1), slice(c0, c1))
    water = win.water[sl]
    if not water.any():
        return None, "no water"
    h = 0.25 * min(PX_M, dx)
    dl, do, share = dense(lat, lon, h)
    so = np.r_[0, np.cumsum(np.hypot(np.diff(lat) * 111320, np.diff(lon) * 111320 * np.cos(np.radians(lat[:-1]))))]
    dlev = np.interp(share, so / so[-1] if so[-1] > 0 else np.linspace(0, 1, len(so)), lev)
    fr, fc = win.frac(dl, do)
    fr, fc = fr - r0, fc - c0
    shp = water.shape
    rr = np.clip(np.floor(fr).astype(np.int64), 0, shp[0] - 1)
    cc = np.clip(np.floor(fc).astype(np.int64), 0, shp[1] - 1)
    course = np.zeros(shp, bool)
    course[rr, cc] = True
    levr = np.zeros(shp, np.float32)
    levr[rr, cc] = dlev
    dist_r, (ir, ic) = ndi.distance_transform_edt(~course, sampling=win.samp, return_indices=True)
    lev_near = levr[ir, ic]
    corr = dist_r <= R_
    # another river's water
    fam = family(i)
    nr = near[sl]
    qn = np.where(nr >= 0, G["q"][np.maximum(nr, 0)], 0)
    foreign = water & ~np.isin(nr, fam) & (qn >= q / FOREIGN_RATIO) & (dist_r > Dn[sl] + NEAR_SLACK_M)
    foreign |= water & (claim_q[sl] >= FOREIGN_RATIO * q) & ~np.isin(claim_id[sl], fam)
    if q < OCEAN_Q:
        foreign |= win.ocean[sl] & (dist_r > OCEAN_NEAR_PX * PX_M)
    if q < SMALL_Q:
        foreign |= win.river[sl] & (win.hw[sl] > width_cap(q))
    own = water & ~foreign
    old_share = float(near_mask(fr, fc, own).mean())
    if old_share >= WET:
        return None, "wet"
    if not (own & corr).any():
        return None, "no water near"
    above = np.clip(win.dem[sl] - lev_near, 0, HEIGHT_CAP)
    cost = np.where(own, 1.0 + 1.0 / (1.0 + win.inner[sl]) + WATER_OFF_COST * dist_r / R_,
                    LAND_COST * (1.0 + (dist_r / LAND_OFF_M) ** 2) + HEIGHT_COST * above)
    got = graph_path(cost, corr, dx, (rr[0], cc[0]), (rr[-1], cc[-1]))
    if got is None:
        return None, "no path"
    pr, pc = got
    if len(pr) < 3:
        return None, "short"
    P = np.c_[(pr + 0.5) * PX_M, (pc + 0.5) * dx]
    P[0] = (fr[0] * PX_M, fc[0] * dx)
    P[-1] = (fr[-1] * PX_M, fc[-1] * dx)
    X = smooth_hold(P, own, dx)
    Y = resample(X, h)
    yr, yc = Y[:, 0] / PX_M, Y[:, 1] / dx
    new_share = float(near_mask(yr, yc, own).mean())
    if new_share < old_share + MIN_GAIN:
        return None, "no gain"
    # and nearer its water on the whole (not wetter in a few places and further off elsewhere)
    d_own = ndi.distance_transform_edt(~own, sampling=win.samp)
    yr_ = np.clip(np.floor(yr).astype(np.int64), 0, shp[0] - 1)
    yc_ = np.clip(np.floor(yc).astype(np.int64), 0, shp[1] - 1)
    if d_own[yr_, yc_].mean() > d_own[rr, cc].mean():
        return None, "further off"
    L_old = float(so[-1])
    L_new = float(np.hypot(*np.diff(X, axis=0).T).sum())
    if L_new > MAX_LONGER * L_old + 1000:
        return None, "longer"
    k = v1.rdp(X, SIMPLIFY_PX * math.sqrt(PX_M * dx))
    X = X[k]
    nlat = wo.on_grid(win.oy - (X[:, 0] / PX_M + r0) / 1200)
    nlon = wo.on_grid(win.ox + (X[:, 1] / dx + c0) / 1200)
    nlat[0], nlon[0], nlat[-1], nlon[-1] = lat[0], lon[0], lat[-1], lon[-1]
    return (nlat, nlon, old_share, new_share, L_old, L_new), "moved"


def claim(i, q, lat, lon, hw_max, win, claim_q, claim_id):
    """Marks the mask water along reach i's course (out to its half width and a pixel) as its own for smaller rivers."""
    reach = hw_max + PX_M
    dl, do, _ = dense(np.asarray(lat, np.float64), np.asarray(lon, np.float64), 0.25 * min(PX_M, win.dx))
    fr, fc = win.frac(dl, do)
    H, W = win.shape
    pad_r, pad_c = int(reach / PX_M) + 2, int(reach / win.dx) + 2
    r0, r1 = max(int(fr.min()) - pad_r, 0), min(int(fr.max()) + pad_r + 1, H)
    c0, c1 = max(int(fc.min()) - pad_c, 0), min(int(fc.max()) + pad_c + 1, W)
    if r1 <= r0 or c1 <= c0:
        return
    sl = (slice(r0, r1), slice(c0, c1))
    path = np.zeros((r1 - r0, c1 - c0), bool)
    rr, cc = np.floor(fr).astype(np.int64) - r0, np.floor(fc).astype(np.int64) - c0
    m = (rr >= 0) & (rr < r1 - r0) & (cc >= 0) & (cc < c1 - c0)
    if not m.any():
        return
    path[rr[m], cc[m]] = True
    z = win.water[sl] & (ndi.distance_transform_edt(~path, sampling=win.samp) <= reach)
    cq, ci = claim_q[sl], claim_id[sl]
    upd = z & (q > cq)
    cq[upd] = q
    ci[upd] = i


def window_box(courses, margin_m):
    la0 = min(float(np.min(la)) for la, _ in courses) - margin_m / 111320 - 0.01
    la1 = max(float(np.max(la)) for la, _ in courses) + margin_m / 111320 + 0.01
    kx = max(math.cos(math.radians(max(abs(la0), abs(la1)))), 0.05)
    lo0 = min(float(np.min(lo)) for _, lo in courses) - margin_m / (111320 * kx) - 0.01
    lo1 = max(float(np.max(lo)) for _, lo in courses) + margin_m / (111320 * kx) + 0.01
    return (math.floor(la0 * 1200) / 1200, math.floor(lo0 * 1200) / 1200,
            math.ceil(la1 * 1200) / 1200, math.ceil(lo1 * 1200) / 1200)


def split_items(items, courses):
    """Two halves of a group along its longer side (by the courses' centres)."""
    c = np.array([[float(np.mean(la)), float(np.mean(lo))] for la, lo in courses])
    ax = int(np.ptp(c[:, 1]) > np.ptp(c[:, 0]))
    order = np.argsort(c[:, ax], kind="stable")
    h = len(items) // 2
    return [items[j] for j in order[:h]], [items[j] for j in order[h:]]


def work(job):
    _, items = job
    out, stats = [], {}
    process(items, out, stats)
    return out, stats


def process(items, out, stats):
    R = G["R"]
    courses = [(R[i][2]["lat"], R[i][2]["lon"]) for i in items]
    box = window_box(courses, max(radius_m(R[i][0]) for i in items))
    if (box[2] - box[0]) * (box[3] - box[1]) > MAX_WINDOW_DEG2 or box[3] - box[1] > 180:
        if len(items) > 1:
            for part in split_items(items, courses):
                process(part, out, stats)
            return
        stats["too big"] = stats.get("too big", 0) + 1
        out.append((items[0], None, np.zeros(len(R[items[0]][2]), np.uint8), None))
        return
    win = Window(*box)
    near, Dn = nearest_courses(win, box)
    claim_q = np.zeros(win.shape, np.float32)
    claim_id = np.full(win.shape, -1, np.int32)
    for i in sorted(items, key=lambda i: -R[i][0]):
        q, _, a = R[i]
        try:
            res, why = snap_one(i, win, near, Dn, claim_q, claim_id)
        except Exception as ex:  # report, keep the old course
            res, why = None, "error " + type(ex).__name__
        stats[why] = stats.get(why, 0) + 1
        lat, lon = (res[0], res[1]) if res else (a["lat"], a["lon"])
        wb, mask_max = measure(lat, lon, win, stats)
        if q >= FOREIGN_RATIO * MIN_Q and mask_max > 0:
            claim(i, q, lat, lon, mask_max, win, claim_q, claim_id)
        out.append((i, res, wb, None))


def measure(lat, lon, win, stats):
    """Width bytes of a course and the mask's largest half width along it (m), counting in stats what the gorge rule
    changed."""
    try:
        wm, wf = vertex_widths(lat, lon, win)
    except Exception as ex:
        stats["width error " + type(ex).__name__] = stats.get("width error " + type(ex).__name__, 0) + 1
        wm = wf = np.zeros(len(lat))
    bm, bf = width_bytes(wm), width_bytes(wf)
    assert ((bf > 0) == (bm > 0)).all()
    for k, v in (("vertices", len(bf)), ("vertices with a mask width", int((bm > 0).sum())),
                 ("vertices narrowed by a gorge", int((bf < bm).sum()))):
        stats[k] = stats.get(k, 0) + v
    return bf, (float(wm.max()) if len(wm) else 0.0)


GORGE_MARGIN_M = FLOOR_MAX_M + GORGE_REACH_M + 2 * PX_M


def widths_job(job):
    _, courses = job
    win = Window(*window_box([(la, lo) for _, la, lo in courses], GORGE_MARGIN_M), full=False)
    st = {}
    res = [(k, measure(la, lo, win, st)[0]) for k, la, lo in courses]
    return res, st


def widths_for(courses, workers=4, islands=None):
    """The half width bytes (mask and gorges) at each vertex of each (lat, lon) course (side arms: tools/river_arms.py)."""
    npz = v1.islands_npz(islands or os.path.join(wa.ROOT, "earthdata", "islands.bin"))
    groups = {}
    for k, (la, lo) in enumerate(courses):
        groups.setdefault((math.floor(float(np.mean(la))), math.floor(float(np.mean(lo)))), []).append((k, la, lo))
    out, stats = [None] * len(courses), {}
    with Pool(workers, initializer=init_worker, initargs=(None, npz)) as pool:
        for res, st in pool.imap_unordered(widths_job, sorted(groups.items())):
            for k, w in res:
                out[k] = w
            for k, v in st.items():
                stats[k] = stats.get(k, 0) + v
    log(f"widths of {len(courses)} courses: {stats}")
    return out


def widths_only(o, npz):
    """--widths-only: the widths (mask and gorges) measured anew on src's courses, which are kept."""
    R = v1.read_full(o.src)
    log(f"{len(R)} reaches; measuring widths only")
    groups = {}
    for i, (q, nxt, a) in enumerate(R):
        groups.setdefault((math.floor(float(a["lat"].mean())), math.floor(float(a["lon"].mean()))), []).append(
            (i, a["lat"].astype(np.float64), a["lon"].astype(np.float64)))
    widths, stats, done = {}, {}, 0
    with Pool(o.workers, initializer=init_worker, initargs=(None, npz)) as pool:
        for res, st in pool.imap_unordered(widths_job, sorted(groups.items()), chunksize=4):
            for i, w in res:
                widths[i] = w
            for k, v in st.items():
                stats[k] = stats.get(k, 0) + v
            done += 1
            if done % 1000 == 0:
                log(f"{done}/{len(groups)} tiles")
    out = []
    for i, (q, nxt, a) in enumerate(R):
        c = np.zeros(len(a), VT)
        for f in ("lon", "lat", "lev", "fixed"):
            c[f] = a[f]
        c["width"] = widths[i]
        out.append((q, nxt, c))
    v1.write_full(o.out, out)
    rep = dict(source=os.path.basename(o.src), widths_only=True, reaches=len(R), widths=stats,
               with_width=sum(1 for w in widths.values() if w.any()), seconds=round(time.time() - T0))
    json.dump(rep, open(o.out + ".json", "w", encoding="utf8"), indent=1)
    log(f"widths: {stats}; wrote {o.out}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("out")
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--only")
    ap.add_argument("--islands", default=os.path.join(wa.ROOT, "earthdata", "islands.bin"), help="the game's islands ('' for none)")
    ap.add_argument("--widths-only", action="store_true", help="keep src's courses, measure their widths anew")
    o = ap.parse_args()
    log(f"mask {wa.WBM}, heights {wa.DEM}")
    npz = v1.islands_npz(o.islands) if o.islands else None
    if o.widths_only:
        return widths_only(o, npz)
    R = v1.read_full(o.src)
    log(f"{len(R)} reaches")
    box = [float(v) for v in o.only.split(",")] if o.only else None
    groups = {}
    for i, (q, nxt, a) in enumerate(R):
        if q < MIN_Q:
            continue
        cl, co = float(a["lat"].mean()), float(a["lon"].mean())
        if box and not (box[0] <= cl <= box[2] and box[1] <= co <= box[3]):
            continue
        groups.setdefault((math.floor(cl), math.floor(co)), []).append(i)
    jobs = sorted(groups.items())
    log(f"{sum(len(v) for _, v in jobs)} reaches of {MIN_Q:.0f} m³/s or more in {len(jobs)} tiles")
    moved, widths, stats, done = {}, {}, {}, 0
    with Pool(o.workers, initializer=init_worker, initargs=(o.src, npz)) as pool:
        for res, st in pool.imap_unordered(work, jobs, chunksize=4):
            for k, v in st.items():
                stats[k] = stats.get(k, 0) + v
            for i, r, wb, _ in res:
                widths[i] = wb
                if r:
                    moved[i] = r
            done += 1
            if done % 200 == 0:
                log(f"{done}/{len(jobs)} tiles, {len(moved)} courses moved")
    log(f"all tiles done: {len(moved)} courses moved; writing")
    out = []
    for i, (q, nxt, a) in enumerate(R):
        if i in moved:
            nlat, nlon = moved[i][:2]
            b = v1.carry(a, nlat, nlon)
        else:
            b = a
        c = np.zeros(len(b), VT)
        for f in ("lon", "lat", "lev", "fixed"):
            c[f] = b[f]
        if i in widths:
            c["width"] = widths[i]
        out.append((q, nxt, c))
    v1.write_full(o.out, out)
    ids = np.array(sorted(moved), np.int64)
    np.savez_compressed(o.out + ".moved.npz", reach=ids, q=np.array([R[i][0] for i in ids], np.float32),
                        wet_before=np.array([moved[i][2] for i in ids], np.float32), wet_after=np.array([moved[i][3] for i in ids], np.float32),
                        m_before=np.array([moved[i][4] for i in ids], np.float32), m_after=np.array([moved[i][5] for i in ids], np.float32))
    nw = sum(1 for w in widths.values() if w.any())
    rep = dict(source=os.path.basename(o.src), min_q=MIN_Q, reaches=len(R), considered=len(widths), outcome=stats,
               moved=len(moved), moved_km=round(sum(v[5] for v in moved.values()) / 1000),
               with_width=nw, vertices=int(sum(len(c) for _, _, c in out)), seconds=round(time.time() - T0))
    json.dump(rep, open(o.out + ".json", "w", encoding="utf8"), indent=1)
    log(f"{len(moved)} courses moved ({rep['moved_km']} km), {nw} reaches with a width; {stats}; wrote {o.out}")


if __name__ == "__main__":
    main()
