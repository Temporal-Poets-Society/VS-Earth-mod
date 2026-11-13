"""
Bakes the large rivers from HydroRIVERS (Lehner & Grill 2013, www.hydrosheds.org; free for scientific, educational
and commercial use with attribution) into earthdata/rivers.bin.

Reaches with a long-term average discharge of at least MIN_Q m3/s (default 20) are kept. The mod shows the navigable
rivers from 100 m3/s (Daugava, Rhine, Danube, Seine, ...) and, by default, only their upstream ends below that, as
streams narrowing to a block; worlds can choose other limits. Each reach keeps its discharge, its downstream reach and a simplified centre line; every
vertex gets a water level (m) from ETOPO (lowest ground near the line), or the lake level where the river crosses a
lake, and a flag for lake/sea vertices (the river must meet those exactly).

Dams: ETOPO shows reservoirs as long flat water surfaces that end in a drop at the dam (Three Gorges, Lake Nasser,
Pļaviņas on the Daugava). Using the reservoirs from HydroLAKES (Lake_type 2, the pour point is the dam), each
reservoir stretch of the main stem is replaced by a straight slope from the reservoir's tail to the river below the
dam, as the river was before it was dammed.

Then the levels are smoothed along the whole network and made non-increasing downstream.

Needs tools/bake_earth.py to have run first (preview/grids.npz: lake and sea cells, without reservoir lakes).
rivers.bin v2: "EMRV", int32 version, int32 reach count, brotli( per reach: float32 Q, int32 next reach index (-1),
int32 n, n x (float32 lon, float32 lat, int16 level, uint8 fixed) ), reaches in topological order (sources first).
"""
import os, struct, sys, time
from collections import defaultdict, deque

import brotli
import numpy as np
import scipy.sparse as sp
import shapefile
import tifffile
from scipy.spatial import cKDTree

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RAW = os.path.join(ROOT, "rawdata")
OUT = os.path.join(ROOT, "earthdata")
MIN_Q = float(sys.argv[1]) if len(sys.argv) > 1 else 20.0
TOL = 0.002   # Douglas-Peucker tolerance in degrees (~220 m, about one block at 198 m/block)
T0 = time.time()


def log(m):
    print(f"[{time.time() - T0:6.1f}s] {m}", flush=True)


def simplify(pts, tol):
    """Douglas-Peucker on (lon, lat) points."""
    if len(pts) < 3:
        return pts
    pts = np.asarray(pts)
    keep = np.zeros(len(pts), bool)
    keep[0] = keep[-1] = True
    stack = [(0, len(pts) - 1)]
    while stack:
        a, b = stack.pop()
        if b <= a + 1:
            continue
        p, q = pts[a], pts[b]
        seg = q - p
        L2 = seg @ seg
        mid = pts[a + 1:b]
        if L2 == 0:
            d = np.hypot(*(mid - p).T)
        else:
            t = np.clip(((mid - p) @ seg) / L2, 0, 1)
            proj = p + t[:, None] * seg
            d = np.hypot(*(mid - proj).T)
        i = int(np.argmax(d))
        if d[i] > tol:
            k = a + 1 + i
            keep[k] = True
            stack.append((a, k))
            stack.append((k, b))
    return pts[keep]


def reservoirs():
    """HydroLAKES reservoirs (Lake_type 2) of at least 1 km2: (lon, lat, surface elevation m)."""
    shp = os.path.join(RAW, "hydrolakes", "HydroLAKES_points_v10_shp", "HydroLAKES_points_v10.shp")
    r = shapefile.Reader(shp, encoding="latin1", encodingErrors="replace")
    out = []
    for rec in r.iterRecords(fields=["Lake_type", "Lake_area", "Elevation", "Pour_long", "Pour_lat"]):
        d = rec.as_dict()
        if d["Lake_type"] == 2 and d["Lake_area"] >= 1.0:
            out.append((d["Pour_long"], d["Pour_lat"], float(d["Elevation"])))
    return out


def main():
    shp = os.path.join(RAW, "hydrorivers", "HydroRIVERS_v10_shp", "HydroRIVERS_v10.shp")
    r = shapefile.Reader(shp)
    names = [f[0] for f in r.fields[1:]]
    iid, inext, iq = names.index("HYRIV_ID"), names.index("NEXT_DOWN"), names.index("DIS_AV_CMS")
    sel = []
    for i, rec in enumerate(r.iterRecords()):
        if rec[iq] >= MIN_Q:
            sel.append((i, rec[iid], rec[inext], rec[iq]))
    log(f"{len(sel)} reaches with Q >= {MIN_Q}")

    g = np.load(os.path.join(ROOT, "preview", "grids.npz"))
    flags, lvl = g["flags"], g["level"]
    RES = flags.shape[1] // 360
    S = tifffile.imread(os.path.join(RAW, "ETOPO_2022_v1_60s_N90W180_surface.tif")).astype(np.float32)
    H1, W1 = S.shape

    reaches = {}
    for n, (i, rid, nxt, q) in enumerate(sel):
        pts = simplify(np.array(r.shape(i).points, np.float64), TOL)
        lev = np.empty(len(pts), np.float32)
        fixed = np.zeros(len(pts), bool)
        for k, (lon, lat) in enumerate(pts):
            rr = min(max(int((90 - lat) * RES), 0), flags.shape[0] - 1)
            cc = int((lon + 180) * RES) % flags.shape[1]
            f = flags[rr, cc]
            if f & 1:
                lev[k] = 0.0
                fixed[k] = True
            elif f & 2 and np.isfinite(lvl[rr, cc]):
                lev[k] = lvl[rr, cc]
                fixed[k] = True
            else:
                # valley floor: lowest ground in a 5x5 arc-minute window
                r1 = min(max(int((90 - lat) * 60), 2), H1 - 3)
                c1 = int((lon + 180) * 60) % W1
                cols = [(c1 + d) % W1 for d in (-2, -1, 0, 1, 2)]
                lev[k] = max(S[r1 - 2:r1 + 3][:, cols].min(), 0.0)
        reaches[rid] = {"next": nxt, "q": q, "pts": pts, "lev": lev, "fixed": fixed}
        if n % 50000 == 0:
            log(f"  {n} reaches read")

    # river mouths: HydroRIVERS ends each river at the HydroSHEDS coastline, but ETOPO has no water depth in many
    # estuaries (the lower Yenisei, the James estuary, Suisun Bay, the Gambia: flat land at 0-3 m), so the terrain's
    # sea can begin tens of km further out. A river ending near sea level on land is continued along the lowest ground
    # to the terrain's open sea (ocean cells below -1 m surrounded by ocean), up to ~250 km, and one cell into it.
    elev2 = g["elev"]
    ocean = (flags & 1) != 0
    H2, W2 = flags.shape
    # deep enough that the terrain's detail noise (scaled by the local relief) can't raise it above sea level
    open_sea = ocean & (elev2 < -(2 + 0.3 * g["rough"]))
    open_sea[1:-1] &= ocean[:-2] & ocean[2:]
    open_sea &= np.roll(ocean, 1, axis=1) & np.roll(ocean, -1, axis=1)
    from scipy.sparse.csgraph import dijkstra
    # valley floors: the lowest 1' ground in each 2' cell (the 2' average puts steep valleys on the mountainside)
    floor2 = S[:2 * H2, :2 * W2].reshape(H2, 2, W2, 2).min(axis=(1, 3))
    R = 75  # cells (2' each): 2.5 degrees
    extended, far = 0, 0
    for rid, re in reaches.items():
        if re["next"] in reaches or re["lev"][-1] > 10:
            continue
        # big rivers search up to 2.5 degrees for open sea, streams nearby only
        R = 75 if re["q"] >= 100 else 20
        lon, lat = re["pts"][-1]
        rr = min(max(int((90 - lat) * RES), 0), H2 - 1)
        cc = int((lon + 180) * RES) % W2
        if open_sea[rr, cc] or (flags[rr, cc] & 2):
            continue  # already in open sea, or ends in a lake
        r0, r1 = max(rr - R, 0), min(rr + R + 1, H2)
        cols = np.arange(cc - R, cc + R + 1)
        win_e = floor2[r0:r1][:, cols % W2]
        win_t = open_sea[r0:r1][:, cols % W2]
        h, w = win_e.shape
        coslat = max(0.05, np.cos(np.radians(lat)))
        # any cell, but the cost rises steeply with height: the path follows valley floors and water
        passable = np.ones_like(win_t)
        sr, sc = rr - r0, R
        idx = np.arange(h * w).reshape(h, w)
        rows, cols_, costs = [], [], []
        for dr, dc in ((0, 1), (1, 0), (1, 1), (1, -1)):
            ia = idx[max(0, -dr):h - max(0, dr), max(0, -dc):w - max(0, dc)]
            ib = idx[max(0, dr):h - max(0, -dr), max(0, dc):w - max(0, -dc)]
            ea = win_e[max(0, -dr):h - max(0, dr), max(0, -dc):w - max(0, dc)]
            eb = win_e[max(0, dr):h - max(0, -dr), max(0, dc):w - max(0, -dc)]
            ok = passable[max(0, -dr):h - max(0, dr), max(0, -dc):w - max(0, dc)] & passable[max(0, dr):h - max(0, -dr), max(0, dc):w - max(0, -dc)]
            step = np.hypot(dr, dc * coslat)
            # following low ground: cost grows with the height above sea level
            c = step * (1 + 0.5 * (np.maximum(ea, 0) + np.maximum(eb, 0)) + 20 * ((ea > 8) | (eb > 8)))
            rows.append(ia[ok]); cols_.append(ib[ok]); costs.append(c[ok])
        rows, cols_, costs = np.concatenate(rows), np.concatenate(cols_), np.concatenate(costs)
        graph = sp.csr_matrix((costs, (rows, cols_)), shape=(h * w, h * w))
        dist, pred = dijkstra(graph, directed=False, indices=idx[sr, sc], return_predecessors=True)
        dist = dist.reshape(h, w)
        cand = np.where(win_t & np.isfinite(dist))
        if len(cand[0]) == 0:
            far += 1
            continue
        k = int(np.argmin(dist[cand]))
        tr, tc = cand[0][k], cand[1][k]
        # the path back to the river end
        path = []
        node = idx[tr, tc]
        while node >= 0 and node != idx[sr, sc]:
            path.append(node)
            node = pred[node]
        path = path[::-1]
        # not through mountains: the highest cell on the way (after the first few, which are the river's own valley)
        heights = win_e.ravel()[path[3:]] if len(path) > 3 else np.zeros(1)
        if len(heights) and heights.max() > 120:
            far += 1
            continue
        pts = np.array([[(p % w + cc - R + 0.5) / RES - 180, 90 - (p // w + r0 + 0.5) / RES] for p in path])
        if len(pts) == 0:
            continue
        # one cell further on, in the direction of the last few cells
        back = pts[max(0, len(pts) - 4)] if len(pts) > 1 else np.array([lon, lat])
        d = pts[-1] - back
        n = np.hypot(d[0] * coslat, d[1])
        if n > 0:
            pts = np.vstack([pts, pts[-1] + d / n / RES])
        pts = simplify(np.vstack([[lon, lat], pts]), TOL)[1:]
        re["pts"] = np.vstack([re["pts"], pts])
        re["lev"] = np.append(re["lev"], np.zeros(len(pts))).astype(np.float32)
        re["fixed"] = np.append(re["fixed"], np.ones(len(pts), bool))
        extended += 1
    log(f"continued {extended} river mouths to the open sea ({far} found no way)")

    # topological order (sources first) and one vertex graph over the whole network
    indeg = defaultdict(int)
    for re in reaches.values():
        if re["next"] in reaches:
            indeg[re["next"]] += 1
    order = []
    queue = deque(rid for rid in reaches if indeg[rid] == 0)
    while queue:
        rid = queue.popleft()
        order.append(rid)
        nx = reaches[rid]["next"]
        if nx in reaches:
            indeg[nx] -= 1
            if indeg[nx] == 0:
                queue.append(nx)
    start, total = {}, 0
    for rid in order:
        start[rid] = total
        total += len(reaches[rid]["lev"])
    lev = np.empty(total, np.float64)
    fixed = np.zeros(total, bool)
    nxt = np.full(total, -1, np.int64)
    q_v = np.empty(total, np.float64)
    lon_v = np.empty(total)
    lat_v = np.empty(total)
    for rid in order:
        re = reaches[rid]
        a, n = start[rid], len(re["lev"])
        lev[a:a + n] = re["lev"]
        fixed[a:a + n] = re["fixed"]
        q_v[a:a + n] = re["q"]
        lon_v[a:a + n], lat_v[a:a + n] = re["pts"][:, 0], re["pts"][:, 1]
        nxt[a:a + n - 1] = np.arange(a + 1, a + n)
        if re["next"] in start:
            nxt[a + n - 1] = start[re["next"]]
    # main upstream neighbour of each vertex (the inflow with the most water)
    prev_main = np.full(total, -1, np.int64)
    best_q = np.zeros(total)
    for v in range(total):
        w = nxt[v]
        if w >= 0 and q_v[v] > best_q[w]:
            best_q[w] = q_v[v]
            prev_main[w] = v

    # --- dams: straight natural slope through each reservoir stretch of the main stem
    res = reservoirs()
    tree = cKDTree(np.c_[lon_v * np.cos(np.radians(lat_v)), lat_v])
    dedammed = 0
    touched = np.zeros(total, bool)
    for lon, lat, elev in res:
        cands = tree.query_ball_point([lon * np.cos(np.radians(lat)), lat], r=0.03)
        cands = [c for c in cands if lev[c] <= elev + 5 and not touched[c]]
        if not cands:
            continue
        v = min(cands, key=lambda c: (lon_v[c] - lon) ** 2 + (lat_v[c] - lat) ** 2)
        # below the dam: first vertex downstream clearly lower than the reservoir surface
        w, steps = v, 0
        while w >= 0 and lev[w] > elev - 3 and steps < 60:
            w = nxt[w]
            steps += 1
        if w < 0 or lev[w] > elev - 3:
            continue
        path = []
        u = prev_main[w]
        while u >= 0 and lev[u] <= elev + 2 and len(path) < 3000:
            path.append(u)
            u = prev_main[u]
        if u < 0 or len(path) < 2:
            continue
        seq = [u] + path[::-1] + [w]
        if fixed[seq[1:-1]].any():
            continue  # through a lake that stays (a natural lake only regulated by the dam)
        xs = lon_v[seq] * np.cos(np.radians(lat_v[seq]))
        d = np.r_[0, np.cumsum(np.hypot(np.diff(xs), np.diff(lat_v[seq])))]
        if d[-1] <= 0:
            continue
        l0, l1 = lev[u], lev[w]
        for k in range(1, len(seq) - 1):
            lev[seq[k]] = l0 + (l1 - l0) * d[k] / d[-1]
            fixed[seq[k]] = False
            touched[seq[k]] = True
        dedammed += 1
    log(f"de-dammed {dedammed} reservoir stretches (of {len(res)} reservoirs >= 1 km2)")

    # Vertices are numbered in flow order (a vertex's downstream neighbour always has a higher number).
    # Lake and sea vertices keep their exact level. Everything upstream of one is at least that high: a river point
    # next to a lake can pick up the lake bottom from ETOPO (the Great Lakes have bathymetry), which put the Detroit
    # and Niagara rivers 10 blocks below Lake Erie.
    def raise_to_downstream_levels():
        low = np.full(total, -1e9)
        conflicts = 0
        for v in range(total - 1, -1, -1):
            w = nxt[v]
            b = low[w] if w >= 0 else -1e9
            if fixed[v]:
                if lev[v] < b - 0.5:
                    conflicts += 1  # a lake or sea higher than the one upstream of it (data mismatch)
                b = max(b, lev[v]) if not fixed[v] else lev[v]
            elif lev[v] < b:
                lev[v] = b
            low[v] = b
        return conflicts

    def monotone():
        # never rising downstream; lake and sea vertices stay at their level
        for v in range(total):
            w = nxt[v]
            if w >= 0 and not fixed[w] and lev[w] > lev[v]:
                lev[w] = lev[v]

    raise_to_downstream_levels()
    monotone()
    # spread the remaining drops out: average with downstream and (mean) upstream neighbours, lake/sea vertices fixed
    has_next = nxt >= 0
    up = sp.csr_matrix((np.ones(int(has_next.sum())), (nxt[has_next], np.nonzero(has_next)[0])), shape=(total, total))
    up_count = np.asarray(up.sum(axis=1)).ravel()
    fixed_lev = lev[fixed].copy()
    for it in range(60):
        up_mean = np.where(up_count > 0, (up @ lev) / np.maximum(up_count, 1), lev)
        down = np.where(has_next, lev[np.maximum(nxt, 0)], lev)
        lev = (lev + up_mean + down) / 3
        lev[fixed] = fixed_lev
    conflicts = raise_to_downstream_levels()
    monotone()
    log(f"levels smoothed and made monotonic along {len(order)} reaches ({total} vertices); "
        f"{conflicts} lake/sea vertices lower than one downstream")

    index = {rid: i for i, rid in enumerate(order)}
    buf = bytearray()
    for rid in order:
        re = reaches[rid]
        a, n = start[rid], len(re["lev"])
        buf += struct.pack("<fii", float(re["q"]), index.get(re["next"], -1), n)
        arr = np.zeros(n, dtype=[("lon", "<f4"), ("lat", "<f4"), ("lev", "<i2"), ("fixed", "u1")])
        arr["lon"], arr["lat"] = re["pts"][:, 0], re["pts"][:, 1]
        arr["lev"] = np.clip(np.round(lev[a:a + n]), -1000, 9000)
        arr["fixed"] = fixed[a:a + n]
        buf += arr.tobytes()
    path = os.path.join(OUT, "rivers.bin")
    with open(path, "wb") as f:
        f.write(b"EMRV")
        f.write(struct.pack("<ii", 2, len(order)))
        f.write(brotli.compress(bytes(buf), quality=11, lgwin=24))
    log(f"rivers.bin v2: {len(order)} reaches, {total} vertices, {os.path.getsize(path) / 1e6:.1f} MB")


if __name__ == "__main__":
    main()
