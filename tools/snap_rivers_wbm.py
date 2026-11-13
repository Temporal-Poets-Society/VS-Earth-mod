"""
River courses moved into the real water (water atlas, 6 November 2026).

HydroRIVERS' courses (and the tall line's GLO-90-snapped ones) follow a 15" grid, so in wide rivers, estuaries and deltas a
course runs straight across banks, shores and islands, and the game carves its own bed along it: the straight cuts the
author saw in New York, on the Mississippi and in the Ganges delta. The Copernicus GLO-90 water body mask (WBM: ocean, lake,
river on the heights' 3" grid) shows where the water really is. For every reach of MIN_Q m³/s or more whose course leaves
the mask's water, this finds the cheapest way from its first to its last vertex on the 3" grid within a corridor around the
old course: water is cheap (cheapest away from its banks, so the course keeps to the middle), land dear, and both a little
dearer the further from the old course. The new course is kept when it is clearly wetter and not much longer.
Reach ends (confluences, mouths) stay where they are, so the network is unchanged; levels and lake/sea flags are carried
over by the share of the way along. Reaches below SMALL_Q (narrower than about 1.5 mask pixels) move only where a good
share of them is on the mask's water already (delta channels: the straight Sundarbans distributaries).
The game's own small islands (islands.bin) count as land even where the mask has water (tidal marsh in the Tagus estuary),
so no course is drawn over an island the game builds.

    .venv/Scripts/python -I tools/snap_rivers_wbm.py <in rivers.bin> <out .bin> [--workers 12] [--only lat0,lon0,lat1,lon1]
        [--islands earthdata/islands.bin]

Writes the same EMRV v2 file (same reaches, order and links) and <out>.json (counts, and per reach changed: old and new
wet shares).
"""
import argparse, json, math, os, struct, sys, time
from multiprocessing import Pool

import brotli
import numpy as np
import scipy.ndimage as ndi
import scipy.sparse as sp
from scipy.sparse.csgraph import dijkstra
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import water_atlas as wa
from water_atlas_cards import mosaic_wbm

MIN_Q = 100.0             # the game's smallest rivers (RiverMinDischarge)
SMALL_Q, SMALL_WET = 300.0, 0.3  # below SMALL_Q (8·√300 = 139 m) a course moves only where this share of it is on mask water
                          # already, so a narrow tributary beside a big river isn't pulled into the big river's water
PX_M = 111320.0 / 1200    # a 3" pixel north-south (m)
LAND_COST, OFF_COST, WATER_OFF_COST = 8.0, 4.0, 0.5
MIN_GAIN = 0.05           # the new course must be this much wetter (share of its length)
MAX_LONGER = 1.5          # and at most this much longer (plus 1 km)
T0 = time.time()
ISL = {}


def islands_npz(path):
    """islands.bin's small islands as flat arrays (pts, start, box), cached beside the atlas by the file's size and time."""
    import sea_referee as sr
    st = os.stat(path)
    cache = os.path.join(wa.OUTDIR, "cache", f"snap-islands-{st.st_size}-{int(st.st_mtime)}.npz")
    if not os.path.exists(cache):
        isl = sr.IslandFile(path)
        start = np.concatenate([[0], np.cumsum([len(p) for p in isl.pts])]).astype(np.int64)
        box = np.array([[p[:, 0].min(), p[:, 1].min(), p[:, 0].max(), p[:, 1].max()] for p in isl.pts])
        os.makedirs(os.path.dirname(cache), exist_ok=True)
        np.savez(cache, pts=np.concatenate(isl.pts), start=start, box=box)
        log(f"{len(isl.pts)} islands from {path}")
    return cache


def init_worker(npz):
    if npz:
        z = np.load(npz)
        ISL.update(pts=z["pts"], start=z["start"], box=z["box"])


def island_land(W, la1, lo0):
    """The game's small islands over the window W (3", NW corner la1/lo0) as a bool raster."""
    h, w = W.shape
    img = Image.new("1", (w, h), 0)
    if ISL:
        lo1, la0 = lo0 + w / 1200, la1 - h / 1200
        b = ISL["box"]
        sel = np.flatnonzero((b[:, 0] <= lo1) & (b[:, 2] >= lo0) & (b[:, 1] <= la1) & (b[:, 3] >= la0))
        dr = ImageDraw.Draw(img)
        for i in sel:
            p = ISL["pts"][ISL["start"][i]:ISL["start"][i + 1]]
            dr.polygon([((x - lo0) * 1200, (la1 - y) * 1200) for x, y in p], fill=1, outline=1)
    return np.array(img, bool)


def log(m):
    print(f"[{time.time() - T0:8.1f}s] {m}", flush=True)


VERTEX = {2: np.dtype([("lon", "<f4"), ("lat", "<f4"), ("lev", "<i2"), ("fixed", "u1")]),
          # record version 3 (0063, courses rules 4): and the mask's half width in RiverReach.WidthUnit (5 m)
          3: np.dtype([("lon", "<f4"), ("lat", "<f4"), ("lev", "<i2"), ("fixed", "u1"), ("width", "u1")])}


def read_full(path):
    """An EMRV file (record version 2, or 3 with widths) as [(q, next, vertices)]."""
    with open(path, "rb") as f:
        b = f.read()
    assert b[:4] == b"EMRV"
    version, count = struct.unpack_from("<ii", b, 4)
    assert version in VERTEX, version
    raw = brotli.decompress(b[12:])
    out, o = [], 0
    dt = VERTEX[version]
    for _ in range(count):
        q, nxt, n = struct.unpack_from("<fii", raw, o)
        o += 12
        a = np.frombuffer(raw, dt, n, o).copy()
        o += n * dt.itemsize
        out.append((q, nxt, a))
    return out


def write_full(path, reaches):
    """Writes record version 3 when the vertices carry widths, else 2."""
    version = 3 if reaches and "width" in reaches[0][2].dtype.names else 2
    buf = bytearray()
    for q, nxt, a in reaches:
        assert a.dtype == VERTEX[version]
        buf += struct.pack("<fii", q, nxt, len(a))
        buf += a.tobytes()
    with open(path, "wb") as f:
        f.write(b"EMRV")
        f.write(struct.pack("<ii", version, len(reaches)))
        f.write(brotli.compress(bytes(buf), quality=11, lgwin=24))


def radius_m(q):
    return min(500.0 + 4 * 8 * math.sqrt(q), 5000.0)


def densify(lat, lon, step_m=40.0):
    kx = np.cos(np.radians(lat))
    d = np.hypot(np.diff(lat) * 111320, np.diff(lon) * 111320 * kx[:-1])
    la, lo = [lat[:1]], [lon[:1]]
    for i, L in enumerate(d):
        k = max(int(math.ceil(L / step_m)), 1)
        t = np.arange(1, k + 1) / k
        la.append(lat[i] + t * (lat[i + 1] - lat[i]))
        lo.append(lon[i] + t * (lon[i + 1] - lon[i]))
    return np.concatenate(la), np.concatenate(lo)


def length_m(lat, lon):
    kx = np.cos(np.radians(lat[:-1]))
    return float(np.hypot(np.diff(lat) * 111320, np.diff(lon) * 111320 * kx).sum())


def rdp(pts, eps):
    """Ramer–Douglas–Peucker on (n, 2) metres; indices kept."""
    keep = np.zeros(len(pts), bool)
    keep[0] = keep[-1] = True
    stack = [(0, len(pts) - 1)]
    while stack:
        a, b = stack.pop()
        if b <= a + 1:
            continue
        p, q = pts[a], pts[b]
        v = q - p
        L = np.hypot(*v)
        seg = pts[a + 1:b] - p
        d = np.abs(seg[:, 0] * v[1] - seg[:, 1] * v[0]) / L if L > 0 else np.hypot(seg[:, 0], seg[:, 1])
        i = int(np.argmax(d))
        if d[i] > eps:
            m = a + 1 + i
            keep[m] = True
            stack += [(a, m), (m, b)]
    return np.nonzero(keep)[0]


def snap_one(q, a, W, la1, lo0):
    """New (lat, lon) course for one reach in the window W (WBM classes, 3", NW corner la1/lo0), or None."""
    lat, lon = a["lat"].astype(np.float64), a["lon"].astype(np.float64)
    R = radius_m(q)
    rlat = R / 111320
    rlon = R / (111320 * max(math.cos(math.radians(float(lat.mean()))), 0.05))
    r0 = max(int((la1 - (lat.max() + rlat)) * 1200), 0)
    r1 = min(int(math.ceil((la1 - (lat.min() - rlat)) * 1200)), W.shape[0])
    c0 = max(int((lon.min() - rlon - lo0) * 1200), 0)
    c1 = min(int(math.ceil((lon.max() + rlon - lo0) * 1200)), W.shape[1])
    if r1 - r0 < 3 or c1 - c0 < 3:
        return None, "edge"
    water = W[r0:r1, c0:c1] > 0
    if not water.any():
        return None, "no water"
    ylat = la1 - r0 / 1200
    xlon = lo0 + c0 / 1200
    dx = PX_M * math.cos(math.radians(float(lat.mean())))
    samp = (PX_M, dx)
    dl, do = densify(lat, lon)
    rr = np.clip(((ylat - dl) * 1200).astype(int), 0, water.shape[0] - 1)
    cc = np.clip(((do - xlon) * 1200).astype(int), 0, water.shape[1] - 1)
    to_water = ndi.distance_transform_edt(~water, sampling=samp)
    old_wet = float((to_water[rr, cc] <= 90).mean())
    if old_wet >= 0.97:
        return None, "wet"
    if q < SMALL_Q and old_wet < SMALL_WET:
        return None, "small, dry"
    course = np.zeros(water.shape, bool)
    course[rr, cc] = True
    dist_course = ndi.distance_transform_edt(~course, sampling=samp)
    corr = dist_course <= R
    if not (water & corr).any():
        return None, "no water near"
    inner = ndi.distance_transform_edt(water, sampling=samp) / PX_M  # pixels to the bank, inside water
    off = dist_course / R
    cost = np.where(water, 1.0 + 1.0 / (1.0 + inner) + WATER_OFF_COST * off, LAND_COST + OFF_COST * off)
    idx = -np.ones(water.shape, np.int64)
    n = int(corr.sum())
    idx[corr] = np.arange(n)
    s, e = idx[rr[0], cc[0]], idx[rr[-1], cc[-1]]
    if s < 0 or e < 0:
        return None, "ends"
    rows, cols, wts = [], [], []
    for dr, dc, L in ((0, 1, dx), (1, 0, PX_M), (1, 1, math.hypot(dx, PX_M)), (1, -1, math.hypot(dx, PX_M))):
        if dc < 0:
            A = idx[:idx.shape[0] - dr, -dc:]
            B = idx[dr:, :idx.shape[1] + dc]
            CA, CB = cost[:cost.shape[0] - dr, -dc:], cost[dr:, :cost.shape[1] + dc]
        else:
            CA = cost[:cost.shape[0] - dr, :cost.shape[1] - dc]
            CB = cost[dr:, dc:]
            A = idx[:idx.shape[0] - dr, :idx.shape[1] - dc]
            B = idx[dr:, dc:]
        m = (A >= 0) & (B >= 0)
        rows.append(A[m]); cols.append(B[m]); wts.append(0.5 * (CA[m] + CB[m]) * L)
    G = sp.csr_matrix((np.concatenate(wts), (np.concatenate(rows), np.concatenate(cols))), shape=(n, n))
    dist, pred = dijkstra(G, directed=False, indices=int(s), return_predecessors=True)
    if not np.isfinite(dist[e]):
        return None, "no path"
    path = [int(e)]
    while path[-1] != s:
        path.append(int(pred[path[-1]]))
    path.reverse()
    pr, pc = np.nonzero(corr)
    pr, pc = pr[path], pc[path]
    plat = ylat - (pr + 0.5) / 1200
    plon = xlon + (pc + 0.5) / 1200
    plat[0], plon[0], plat[-1], plon[-1] = lat[0], lon[0], lat[-1], lon[-1]
    new_wet = float((to_water[pr, pc] <= 90).mean())
    if new_wet < old_wet + MIN_GAIN:
        return None, "no gain"
    L_old, L_new = length_m(lat, lon), length_m(plat, plon)
    if L_new > MAX_LONGER * L_old + 1000:
        return None, "longer"
    xy = np.c_[(plon - plon[0]) * 111320 * math.cos(math.radians(float(lat.mean()))), (plat - plat[0]) * 111320]
    k = rdp(xy, 0.5 * PX_M)
    return (plat[k], plon[k], old_wet, new_wet, L_old, L_new), "snapped"


def carry(a, nlat, nlon):
    """Levels and fixed flags for a new course by the share of the way along the old one."""
    lat, lon = a["lat"].astype(np.float64), a["lon"].astype(np.float64)
    kx = math.cos(math.radians(float(lat.mean())))
    so = np.r_[0, np.cumsum(np.hypot(np.diff(lat), np.diff(lon) * kx))]
    sn = np.r_[0, np.cumsum(np.hypot(np.diff(nlat), np.diff(nlon) * kx))]
    fo = so / so[-1] if so[-1] > 0 else np.linspace(0, 1, len(so))
    fn = sn / sn[-1] if sn[-1] > 0 else np.linspace(0, 1, len(sn))
    lev = np.interp(fn, fo, a["lev"].astype(np.float64))
    lev = np.minimum.accumulate(np.round(lev))
    lev[0], lev[-1] = a["lev"][0], a["lev"][-1]
    lev = np.minimum.accumulate(lev)
    near = np.clip(np.searchsorted(fo, fn), 0, len(fo) - 1)
    out = np.zeros(len(nlat), a.dtype)
    out["lat"], out["lon"] = nlat, nlon
    out["lev"] = np.clip(lev, -1000, 9000).astype(np.int16)
    out["fixed"] = a["fixed"][near]
    out["fixed"][0], out["fixed"][-1] = a["fixed"][0], a["fixed"][-1]
    return out


def work(job):
    (tl, tc), items = job
    rmax = max(radius_m(q) for _, q, _ in items)
    la0 = min(float(a["lat"].min()) for _, _, a in items) - rmax / 111320 - 0.01
    la1 = max(float(a["lat"].max()) for _, _, a in items) + rmax / 111320 + 0.01
    kx = max(math.cos(math.radians(tl + 0.5)), 0.05)
    lo0 = min(float(a["lon"].min()) for _, _, a in items) - rmax / (111320 * kx) - 0.01
    lo1 = max(float(a["lon"].max()) for _, _, a in items) + rmax / (111320 * kx) + 0.01
    la0, la1 = math.floor(la0 * 1200) / 1200, math.ceil(la1 * 1200) / 1200
    lo0, lo1 = math.floor(lo0 * 1200) / 1200, math.ceil(lo1 * 1200) / 1200
    if (la1 - la0) * (lo1 - lo0) > 30:
        return [], {"too big": len(items)}
    W = mosaic_wbm(la0, lo0, la1, lo1)
    W[island_land(W, la1, lo0)] = 0
    res, stats = [], {}
    for i, q, a in items:
        if len(a) < 2:
            continue
        try:
            r, why = snap_one(q, a, W, la1, lo0)
        except Exception as ex:  # report, keep the old course
            r, why = None, "error " + type(ex).__name__
        stats[why] = stats.get(why, 0) + 1
        if r:
            nlat, nlon, ow, nw, lo_, ln = r
            res.append((i, carry(a, nlat, nlon), ow, nw, lo_, ln))
    return res, stats


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("out")
    ap.add_argument("--workers", type=int, default=12)
    ap.add_argument("--only")
    ap.add_argument("--min-q", type=float, default=MIN_Q)
    ap.add_argument("--islands", default=os.path.join(wa.ROOT, "earthdata", "islands.bin"), help="the game's islands ('' for none)")
    o = ap.parse_args()
    npz = islands_npz(o.islands) if o.islands else None
    R = read_full(o.src)
    log(f"{len(R)} reaches")
    box = [float(v) for v in o.only.split(",")] if o.only else None
    groups = {}
    for i, (q, nxt, a) in enumerate(R):
        if q < o.min_q or len(a) < 2:
            continue
        cl, co = float(a["lat"].mean()), float(a["lon"].mean())
        if box and not (box[0] <= cl <= box[2] and box[1] <= co <= box[3]):
            continue
        groups.setdefault((math.floor(cl), math.floor(co)), []).append((i, q, a))
    jobs = sorted(groups.items(), key=lambda kv: -len(kv[1]))
    log(f"{sum(len(v) for _, v in jobs)} reaches of {o.min_q:.0f} m³/s or more in {len(jobs)} tiles")
    changed, stats, done = {}, {}, 0
    with Pool(o.workers, initializer=init_worker, initargs=(npz,)) as pool:
        for res, st in pool.imap_unordered(work, jobs):
            for k, v in st.items():
                stats[k] = stats.get(k, 0) + v
            for i, arr, ow, nw, lo_, ln in res:
                changed[i] = (arr, ow, nw, lo_, ln)
            done += 1
            if done % 500 == 0:
                log(f"{done}/{len(jobs)} tiles, {len(changed)} courses moved")
    out = [(q, nxt, changed[i][0] if i in changed else a) for i, (q, nxt, a) in enumerate(R)]
    write_full(o.out, out)
    rep = dict(source=os.path.basename(o.src), min_q=o.min_q, reaches=len(R), outcome=stats, moved=len(changed),
               moved_km=round(sum(v[4] for v in changed.values()) / 1000),
               changes=[dict(reach=i, q=round(R[i][0], 1), lat=round(float(R[i][2]["lat"].mean()), 4), lon=round(float(R[i][2]["lon"].mean()), 4),
                             wet_before=round(v[1], 3), wet_after=round(v[2], 3), km_before=round(v[3] / 1000, 2), km_after=round(v[4] / 1000, 2))
                        for i, v in sorted(changed.items(), key=lambda kv: -R[kv[0]][0])])
    json.dump(rep, open(o.out + ".json", "w", encoding="utf8"), indent=0)
    log(f"{len(changed)} courses moved ({rep['moved_km']} km); {stats}; wrote {o.out}")


if __name__ == "__main__":
    main()
