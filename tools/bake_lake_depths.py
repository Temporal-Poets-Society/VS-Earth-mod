"""EA-0184, decision 0100: lake-depths-1.bin, each lake's real depth for lakes-glo90-5.bin's lakes.

HydroLAKES (Messager et al. 2016, CC BY 4.0) gives every lake from 10 ha its mean depth (Depth_avg: from measured volumes
for the largest, from a model of the land around for the rest) at its outlet (the pour point). This bake joins
lakes-glo90-5.bin's pieces into whole lakes (pieces in neighbouring 1-degree tiles that meet along the shared side at the
same level), finds for every HydroLAKES lake from MIN_AREA km2 the whole lake its outlet lies on (within MATCH_M metres,
the closest in area), and writes for every piece of a matched lake:

- its floor depth (dm): the depth far from the shore that gives the lake its mean depth (the matched HydroLAKES lakes'
  volume over their area) over its own distances to the shore (pieces from EDT_KM2 rasterised at 15", a distance
  transform), with the bed curve the game draws (TerrainModel.LakeDepthAt); smaller lakes as a lake as wide as its size;
- its size (10 m units): the whole lake's area over its shore's length (tile cuts left out), which the game turns into
  how fast the bed falls away from the shore (TerrainModel.LakeDepthAt);
- flag 1: ETOPO 2022 holds the lake's bed (its surface grid is below the lake's level at most of the lake's inside
  points), so the game keeps the 2' grid's bathymetry there (Baikal, the Great Lakes).

Smaller and unmatched lakes are left out: the game gives them a depth by their area (the medians printed at the end).

File: "EMLD", int32 version 1, int32 tiles across and down (360, 180), int64 length and 32-byte SHA-256 of the
lakes-glo90-5.bin it was made for, then a brotli block: varint tile count; per tile a varint step from the last tile
index, a varint count, and per entry a varint step from the last lake index, varint depth (dm), varint size (10 m), a
flags byte.

    .venv\\Scripts\\python.exe -I tools\\bake_lake_depths.py <lakes-glo90-5.bin> <HydroLAKES_points_v10.shp> <ETOPO surface tif> <out.bin>
"""
import hashlib, math, os, struct, sys, time
from collections import defaultdict
from multiprocessing import Pool

import brotli
import numpy as np
import shapefile
import shapely
import shapely.affinity

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lake_outline_reader import LakeOutlines  # noqa: E402

MIN_AREA = 1.0        # km2: HydroLAKES lakes looked for
MATCH_M = 600.0       # m: an outlet this near a piece's shore lies on it (more for large lakes: match_metres)
MATCH_MAX_M = 30000.0
AREA_FACTOR = 8.0     # a whole lake this many times smaller than the HydroLAKES lake is not it (a larger one may hold many)
MIN_COVER = 0.1       # the matched HydroLAKES lakes must cover this share of a whole lake for their depth to be its
BATHY_KM2 = 100.0     # whole lakes from this size are tested for ETOPO's bathymetry, matched or not
LEVEL_TOL = 0.05      # m: pieces meeting across a tile side at levels this close are one lake
SAMPLES = 6           # inside points per piece from 1 km2 for the ETOPO test
EDT_KM2 = 5.0         # pieces from this size get their distances to the shore measured (15" cells)
EDT_CELL = 4800 // 240   # units: 15"
HIST_M = 50.0         # m: the distance histogram's bin
SHAPE_MEAN = 0.5677   # the bed curve's mean across a lake as wide as its size (TerrainModel.LakeShapeMean), without a histogram
M_PER_DEG = 111320.0
VERSION = 1

L = None


def log(msg):
    print(time.strftime("%H:%M:%S"), msg, flush=True)


def init(path):
    global L
    L = LakeOutlines(path)


def ring_stats(pts, side, lat0):
    """Shoelace area (units2, signed) and the shore length (m) of a ring, leaving out steps along the tile's sides."""
    a = np.asarray(pts, dtype=np.float64)
    x, y = a[:, 0], a[:, 1]
    xn, yn = np.roll(x, -1), np.roll(y, -1)
    area = 0.5 * np.sum(x * yn - xn * y)
    on_side = ((x == 0) & (xn == 0)) | ((x == side) & (xn == side)) | ((y == 0) & (yn == 0)) | ((y == side) & (yn == side))
    lat = lat0 + (y + yn) / 2 / L.unit
    dx = (xn - x) / L.unit * M_PER_DEG * np.cos(np.radians(lat))
    dy = (yn - y) / L.unit * M_PER_DEG
    shore = float(np.sum(np.hypot(dx, dy)[~on_side]))
    return area, shore


def tile_pass(t):
    """Per piece: level, area km2, shore m, bbox (units), the stretches of each tile side it runs along, and for pieces
    from 1 km2 a few inside points (lat, lon)."""
    lat0, lon0 = L.tile_origin(t)
    side = L.unit * L.tile_deg
    out = []
    for level, flags, rings in L.lakes(t):
        area_u = 0.0
        shore = 0.0
        edges = defaultdict(list)
        xs, ys = [], []
        for pts in rings:
            a, s = ring_stats(pts, side, lat0)
            area_u += a
            shore += s
            n = len(pts)
            for k in range(n):
                (x0, y0), (x1, y1) = pts[k], pts[(k + 1) % n]
                if x0 == x1 == 0: edges["W"].append((min(y0, y1), max(y0, y1)))
                elif x0 == x1 == side: edges["E"].append((min(y0, y1), max(y0, y1)))
                elif y0 == y1 == 0: edges["S"].append((min(x0, x1), max(x0, x1)))
                elif y0 == y1 == side: edges["N"].append((min(x0, x1), max(x0, x1)))
            xs.append(min(p[0] for p in pts)); xs.append(max(p[0] for p in pts))
            ys.append(min(p[1] for p in pts)); ys.append(max(p[1] for p in pts))
        cy = lat0 + (min(ys) + max(ys)) / 2 / L.unit
        km2 = abs(area_u) / L.unit ** 2 * (M_PER_DEG / 1000) ** 2 * math.cos(math.radians(cy))
        inside = []
        if km2 >= MIN_AREA:
            poly = even_odd(rings)
            if poly is not None and not poly.is_empty:
                bx0, by0, bx1, by1 = poly.bounds
                rng = np.random.default_rng(t * 7919 + len(out))
                px = rng.uniform(bx0, bx1, 200); py = rng.uniform(by0, by1, 200)
                ok = shapely.contains_xy(poly, px, py)
                for X, Y in list(zip(px[ok], py[ok]))[:SAMPLES]:
                    inside.append((lat0 + Y / L.unit, lon0 + X / L.unit))
        hist = distance_histogram(rings, lat0, side) if km2 >= EDT_KM2 else None
        out.append((level, km2, shore, hist, dict(edges) or None, inside or None))
    return t, out


def distance_histogram(rings, lat0, side):
    """The piece's distances to its shore (15" cells, metres, in HIST_M bins): the piece rasterised in its tile, the shore
    where it meets land inside the tile; across a tile's side the lake goes on (no shore there), as the world measures it."""
    from scipy import ndimage
    poly = even_odd(rings)
    if poly is None or poly.is_empty:
        return None
    x0, y0, x1, y1 = poly.bounds
    c0, r0 = int(x0 // EDT_CELL), int(y0 // EDT_CELL)
    c1, r1 = int(math.ceil(x1 / EDT_CELL)), int(math.ceil(y1 / EDT_CELL))
    xs = (np.arange(c0, c1) + 0.5) * EDT_CELL
    ys = (np.arange(r0, r1) + 0.5) * EDT_CELL
    gx, gy = np.meshgrid(xs, ys)
    water = shapely.contains_xy(poly, gx, gy)
    if not water.any():
        return None
    # a margin of land round it, except along the tile's sides (there the lake goes on: water)
    pad = np.zeros((water.shape[0] + 2, water.shape[1] + 2), bool)
    pad[1:-1, 1:-1] = water
    if c0 <= 0: pad[:, 0] = pad[:, 1]
    if c1 * EDT_CELL >= side: pad[:, -1] = pad[:, -2]
    if r0 <= 0: pad[0, :] = pad[1, :]
    if r1 * EDT_CELL >= side: pad[-1, :] = pad[-2, :]
    lat = lat0 + (y0 + y1) / 2 / L.unit
    dy = EDT_CELL / L.unit * M_PER_DEG
    dx = dy * math.cos(math.radians(lat))
    d = ndimage.distance_transform_edt(pad, sampling=(dy, dx))[1:-1, 1:-1][water]
    # (a cell's centre is half a cell in: the shore lies between it and the land beyond)
    d = np.maximum(d - 0.5 * min(dx, dy), 0)
    return np.bincount((d / HIST_M).astype(np.int64)).astype(np.int64)


def even_odd(rings):
    poly = None
    for pts in rings:
        if len(pts) < 3:
            continue
        p = shapely.Polygon(pts)
        if not p.is_valid:
            p = shapely.make_valid(p)
        poly = p if poly is None else poly.symmetric_difference(p)
    return poly


def match_metres(km2):
    """How far from a whole lake's shore an outlet may lie: MATCH_M, more for large lakes, 250 m times the root of the area
    in km2 (their outlet river is often the mask's river for kilometres: the St. Clair below Lake Huron, the Svir below
    Onega, 21 km), at most MATCH_MAX_M. The area test keeps a large lake off a small one nearby."""
    return max(MATCH_M, min(MATCH_MAX_M, 250.0 * math.sqrt(km2)))


def tile_segments(tt, sx, sy):
    """A tile's shore segments (tile cuts left out) shifted by (sx, sy) units, with each one's lake index."""
    side = L.unit * L.tile_deg
    parts = []
    for i, (level, flags, rings) in enumerate(L.lakes(tt)):
        for pts in rings:
            if len(pts) < 2:
                continue
            a = np.asarray(pts, dtype=np.float64)
            b = np.roll(a, -1, axis=0)
            on_side = ((a[:, 0] == 0) & (b[:, 0] == 0)) | ((a[:, 0] == side) & (b[:, 0] == side)) \
                | ((a[:, 1] == 0) & (b[:, 1] == 0)) | ((a[:, 1] == side) & (b[:, 1] == side))
            k = ~on_side
            if k.any():
                parts.append(np.column_stack([a[k, 0] + sx, a[k, 1] + sy, b[k, 0] + sx, b[k, 1] + sy, np.full(k.sum(), i, np.float64)]))
    return np.concatenate(parts) if parts else np.zeros((0, 5))


BUCKET = 64.0   # units (a 3" cell is 4): at least the match distance at every latitude this bake meets lakes at


def match_pass(job):
    """HydroLAKES outlets in one tile: the pieces (this tile's and its neighbours') whose shore lies within MATCH_M."""
    t, points = job
    lat0, lon0 = L.tile_origin(t)
    r, c = divmod(t, L.tx)
    segs, tiles_of = [], []
    for dr in (-1, 0, 1):
        for dc in (-1, 0, 1):
            rr, cc = r + dr, (c + dc) % L.tx
            if not (0 <= rr < L.ty):
                continue
            tt = rr * L.tx + cc
            if tt not in L.index:
                continue
            sg = tile_segments(tt, dc * L.unit * L.tile_deg, dr * L.unit * L.tile_deg)
            if len(sg):
                segs.append(sg)
                tiles_of.append(np.full(len(sg), tt, np.int64))
    found = []
    if not segs:
        return [(hl, []) for hl, _, _, _ in points]
    S = np.concatenate(segs)
    T = np.concatenate(tiles_of)
    mx, my = (S[:, 0] + S[:, 2]) / 2, (S[:, 1] + S[:, 3]) / 2
    longs = np.flatnonzero(np.maximum(np.abs(S[:, 2] - S[:, 0]), np.abs(S[:, 3] - S[:, 1])) > BUCKET)
    bx, by = np.floor(mx / BUCKET).astype(np.int64), np.floor(my / BUCKET).astype(np.int64)
    key = bx * 1_000_003 + by
    order = np.argsort(key, kind="stable")
    ks = key[order]
    uniq, first = np.unique(ks, return_index=True)
    last = np.append(first[1:], len(ks))
    where = dict(zip(uniq.tolist(), zip(first.tolist(), last.tolist())))
    for hl, lat, lon, radius in points:
        X, Y = (lon - lon0) * L.unit, (lat - lat0) * L.unit
        cosl = max(0.05, math.cos(math.radians(lat)))
        reach_u = radius / M_PER_DEG * L.unit
        span, span_y = int(math.ceil(reach_u / cosl / BUCKET)), int(math.ceil(reach_u / BUCKET))
        px, py = int(math.floor(X / BUCKET)), int(math.floor(Y / BUCKET))
        idx = [order[a:b] for kx in range(px - span, px + span + 1) for ky in range(py - span_y, py + span_y + 1)
               for a, b in [where.get(kx * 1_000_003 + ky, (0, 0))] if b > a]
        if len(longs):
            idx.append(longs)
        if not idx:
            found.append((hl, []))
            continue
        ii = np.unique(np.concatenate(idx))
        ax, ay, bx2, by2 = S[ii, 0] * cosl, S[ii, 1], S[ii, 2] * cosl, S[ii, 3]
        qx, qy = X * cosl, Y
        dx, dy = bx2 - ax, by2 - ay
        ll = dx * dx + dy * dy
        tt_ = np.clip(((qx - ax) * dx + (qy - ay) * dy) / np.where(ll > 0, ll, 1), 0, 1)
        d = np.hypot(ax + tt_ * dx - qx, ay + tt_ * dy - qy) / L.unit * M_PER_DEG
        ok = d <= radius
        best = {}
        for tile, lake, dist in zip(T[ii][ok].tolist(), S[ii, 4][ok].astype(np.int64).tolist(), d[ok].tolist()):
            if (tile, lake) not in best or dist < best[(tile, lake)]:
                best[(tile, lake)] = dist
        found.append((hl, [(tile, lake, dist) for (tile, lake), dist in best.items()]))
    return found


class Union:
    def __init__(self, n):
        self.p = np.arange(n, dtype=np.int64)

    def find(self, a):
        p = self.p
        root = a
        while p[root] != root:
            root = p[root]
        while p[a] != root:
            p[a], a = root, p[a]
        return root

    def join(self, a, b):
        ra, rb = self.find(a), self.find(b)
        if ra != rb:
            self.p[max(ra, rb)] = min(ra, rb)


def overlap(u, v):
    return any(min(a1, b1) - max(a0, b0) > 0 for a0, a1 in u for b0, b1 in v)


def varint(v):
    out = bytearray()
    while True:
        b = v & 0x7F; v >>= 7
        if v:
            out.append(b | 0x80)
        else:
            out.append(b)
            return bytes(out)


def main():
    lakes_path, hl_path, etopo_path, out_path = sys.argv[1:5]
    workers = min(24, os.cpu_count() or 4)
    base = LakeOutlines(lakes_path)
    tiles = sorted(base.index)
    log(f"{len(tiles)} tiles in {lakes_path}")

    # 1. every piece's area, shore, sides and (from 1 km2) inside points
    with Pool(workers, initializer=init, initargs=(lakes_path,)) as pool:
        per_tile = dict(pool.imap_unordered(tile_pass, tiles, chunksize=8))
    offset, n = {}, 0
    for t in tiles:
        offset[t] = n
        n += len(per_tile[t])
    log(f"{n} pieces")
    km2 = np.zeros(n); shore = np.zeros(n); level = np.zeros(n)
    for t in tiles:
        for i, (lv, a, s, _, _, _) in enumerate(per_tile[t]):
            km2[offset[t] + i] = a; shore[offset[t] + i] = s; level[offset[t] + i] = lv

    # 2. whole lakes: pieces meeting along a shared side at one level
    uf = Union(n)
    joins = 0
    for t in tiles:
        r, c = divmod(t, base.tx)
        for side, nb, other in (("E", r * base.tx + (c + 1) % base.tx, "W"), ("N", (r + 1) * base.tx + c, "S")):
            if nb not in per_tile:
                continue
            mine = [(i, p[4][side]) for i, p in enumerate(per_tile[t]) if p[4] and p[4].get(side)]
            if not mine:
                continue
            theirs = [(j, p[4][other]) for j, p in enumerate(per_tile[nb]) if p[4] and p[4].get(other)]
            for i, u in mine:
                for j, v in theirs:
                    if abs(per_tile[t][i][0] - per_tile[nb][j][0]) <= LEVEL_TOL and overlap(u, v):
                        uf.join(offset[t] + i, offset[nb] + j)
                        joins += 1
    root = np.array([uf.find(k) for k in range(n)], dtype=np.int64)
    g_hist = {}
    for t in tiles:
        for i, p in enumerate(per_tile[t]):
            h = p[3]
            if h is None:
                continue
            g = root[offset[t] + i]
            if g in g_hist:
                a, b = g_hist[g], h
                if len(a) < len(b): a, b = b, a
                a = a.copy(); a[:len(b)] += b
                g_hist[g] = a
            else:
                g_hist[g] = h
    g_km2 = np.bincount(root, weights=km2, minlength=n)
    g_shore = np.bincount(root, weights=shore, minlength=n)
    log(f"{joins} joins across tile sides; {len(np.unique(root))} whole lakes")

    # 3. HydroLAKES outlets from MIN_AREA, tile by tile
    r = shapefile.Reader(hl_path, encoding="latin-1")
    F = [f[0] for f in r.fields[1:]]
    I = {k: F.index(k) for k in ("Hylak_id", "Lake_area", "Depth_avg", "Vol_total", "Pour_lat", "Pour_long")}
    hl = []
    for rec in r.iterRecords():
        if rec[I["Lake_area"]] >= MIN_AREA:
            hl.append((rec[I["Hylak_id"]], rec[I["Lake_area"]], rec[I["Depth_avg"]], rec[I["Vol_total"]], rec[I["Pour_lat"]], rec[I["Pour_long"]]))
    log(f"{len(hl)} HydroLAKES lakes from {MIN_AREA} km2")
    jobs = defaultdict(list)
    for k, (_, a, _, _, la, lo) in enumerate(hl):
        t = (int(math.floor(la)) + 90) * base.tx + (int(math.floor(lo)) + 180) % base.tx
        jobs[t].append((k, la, lo, match_metres(a)))
    with Pool(workers, initializer=init, initargs=(lakes_path,)) as pool:
        found = [x for part in pool.imap_unordered(match_pass, list(jobs.items()), chunksize=4) for x in part]
    log("outlets looked up")

    # 4. each HydroLAKES lake on the whole lake nearest in area
    g_vol = defaultdict(float); g_area = defaultdict(float); g_lakes = defaultdict(int)
    matched = 0
    matched_set = set()
    unmatched_big = []
    for k, cands in found:
        _, a, d, vol, la, lo = hl[k]
        best = None
        radius = match_metres(a)
        for tt, i, dist in cands:
            g = root[offset[tt] + i]
            ratio = g_km2[g] / a
            if ratio < 1 / AREA_FACTOR:
                continue
            # (a whole lake may hold many HydroLAKES lakes: a larger one is less likely, a smaller one far less)
            score = dist / radius + (math.log(1 / ratio) if ratio < 1 else 0.25 * math.log(ratio))
            if best is None or score < best[0]:
                best = (score, g)
        if best is None:
            if a >= 100:
                unmatched_big.append((a, la, lo, hl[k][0]))
            continue
        g = best[1]
        g_vol[g] += a * d          # (mean depth over area: the volume as Depth_avg x area, km2 m)
        g_area[g] += a
        g_lakes[g] += 1
        matched += 1
        matched_set.add(k)
    thin = [g for g in g_vol if g_area[g] < MIN_COVER * g_km2[g]]
    log(f"{matched} of {len(hl)} HydroLAKES lakes matched to {len(g_vol)} whole lakes; {len(thin)} of those covered under {MIN_COVER:.0%} by them")
    for size_lo, size_hi in ((1, 10), (10, 100), (100, 1000), (1000, 1e9)):
        sel = [k for k in range(len(hl)) if size_lo <= hl[k][1] < size_hi]
        got = sum(1 for k, cands in found if size_lo <= hl[k][1] < size_hi and k in matched_set)
        log(f"  HydroLAKES lakes {size_lo}-{size_hi:g} km2: {got} of {len(sel)} matched")
    for a, la, lo, hid in sorted(unmatched_big, reverse=True)[:25]:
        log(f"  unmatched: Hylak {hid} {a:.0f} km2 at {la:.3f},{lo:.3f}")

    # 5. ETOPO's bathymetry under the deeper or larger matched lakes
    import tifffile
    log("reading ETOPO surface")
    S = np.asarray(tifffile.imread(etopo_path), dtype=np.float32)
    H, W = S.shape
    bathy = set()
    etopo_mean = {}
    pts_by_group = defaultdict(list)
    for t in tiles:
        for i, p in enumerate(per_tile[t]):
            g = root[offset[t] + i]
            if p[5] and (g in g_vol or g_km2[g] >= BATHY_KM2):
                pts_by_group[g].extend((la, lo, p[0]) for la, lo in p[5])
    for g, pts in pts_by_group.items():
        mean = g_vol[g] / g_area[g] if g in g_vol else 0.0
        if g in g_vol and mean < 10 and g_km2[g] < BATHY_KM2 or len(pts) < 3:
            continue
        depths = []
        for la, lo, lv in pts:
            rr = min(H - 1, max(0, int((90 - la) * H / 180)))
            cc = min(W - 1, max(0, int((lo + 180) * W / 360)))
            depths.append(lv - float(S[rr, cc]))
        depths = np.array(depths)
        if np.mean(depths >= 3) >= 0.6:
            bathy.add(g)
            etopo_mean[g] = float(np.mean(np.maximum(depths, 0)))
    del S
    log(f"{len(bathy)} whole lakes with ETOPO's bathymetry ({sum(1 for g in bathy if g not in g_vol)} of them not matched in HydroLAKES)")
    # the depth each whole lake gets: HydroLAKES' where its lakes cover enough of it, else ETOPO's where it has the bed
    final = {}
    for g in set(g_vol) | bathy:
        if g in g_vol and g_area[g] >= MIN_COVER * g_km2[g]:
            final[g] = g_vol[g] / g_area[g]
        elif g in etopo_mean:
            final[g] = etopo_mean[g]
    log(f"{len(final)} whole lakes get a depth")
    # the floor depth (Dmax) that gives the lake its mean: over its own distances to the shore where measured, else as a
    # lake as wide as its size
    floor_depth = {}
    measured = 0
    for g, mean in final.items():
        size = g_km2[g] * 1e6 / max(g_shore[g], 1.0)
        l = max(size / 2, 10.0)
        h = g_hist.get(g)
        if h is not None and h.sum() > 0:
            centres = (np.arange(len(h)) + 0.5) * HIST_M
            factor = float(np.sum(h * (1 - np.exp(-centres / l))) / h.sum())
            measured += 1
        else:
            factor = SHAPE_MEAN
        floor_depth[g] = mean / max(factor, 0.05)
    log(f"{measured} of {len(final)} lakes' floors from their own distances to the shore")
    for g in sorted(final, key=lambda g: -g_km2[g])[:20]:
        log(f"  {g_km2[g]:9.0f} km2: mean {final[g]:6.1f} m, floor {floor_depth[g]:6.1f} m ({'HydroLAKES' if g in g_vol and g_area[g] >= MIN_COVER * g_km2[g] else 'ETOPO'}"
            f"{', ETOPO bed' if g in bathy else ''}), size {g_km2[g] * 1e6 / max(g_shore[g], 1) / 1000:.2f} km")

    # 6. the file
    body = bytearray()
    entries = []
    for t in tiles:
        row = []
        for i in range(len(per_tile[t])):
            g = root[offset[t] + i]
            if g not in final:
                continue
            depth_dm = int(round(floor_depth[g] * 10))
            size10 = int(round(g_km2[g] * 1e6 / max(g_shore[g], 1.0) / 10))
            row.append((i, min(depth_dm, 65535), min(max(size10, 1), 65535), 1 if g in bathy else 0))
        if row:
            entries.append((t, row))
    body += varint(len(entries))
    last_t = 0
    count = 0
    for t, row in entries:
        body += varint(t - last_t); last_t = t
        body += varint(len(row))
        last_i = 0
        for i, d, s, f in row:
            body += varint(i - last_i); last_i = i
            body += varint(d) + varint(s) + bytes([f])
            count += 1
    raw = open(lakes_path, "rb").read()
    head = b"EMLD" + struct.pack("<iiiq", VERSION, base.tx, base.ty, len(raw)) + hashlib.sha256(raw).digest()
    data = head + brotli.compress(bytes(body), quality=11)
    open(out_path, "wb").write(data)
    log(f"wrote {out_path}: {len(data)} bytes, {count} pieces in {len(entries)} tiles ({len(final)} lakes)")

    # 7. the depths the game gives the rest, by area (HydroLAKES' medians of the lakes below MIN_AREA and above)
    areas = np.array([x[1] for x in hl]); depths = np.array([x[2] for x in hl])
    for lo_a, hi_a in ((1, 10), (10, 100), (100, 1e9)):
        m = (areas >= lo_a) & (areas < hi_a)
        log(f"HydroLAKES median depth {lo_a}-{hi_a:g} km2: {np.median(depths[m]):.1f} m ({m.sum()} lakes)")


if __name__ == "__main__":
    main()
