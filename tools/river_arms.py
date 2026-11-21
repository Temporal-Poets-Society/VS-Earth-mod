"""
River side arms from the GLO-90 water mask (water atlas, 6 November 2026).

HydroRIVERS is a tree with one line per river, so where a river splits around an island and joins again, only one arm is
in the data and the island is part of the bank: Doles sala on the Daugava below Riga, whose southern arm (the Sausā
Daugava) Earth did not have. The Copernicus GLO-90 water body mask (WBM, 3") shows both arms.

For every river of MIN_Q m³/s or more this looks for land the mask encloses in river or lake water right beside the course
(an island of MIN_ISLAND_KM2 to MAX_ISLAND_KM2), and for the other way round it: the cheapest way through the mask's
water from the course above the island to the course below it with the course itself between those points blocked, so
the way has to go round the other side. The way is kept as a side arm when it is a real arm and not something else:
 - it runs at least MIN_OFFSET_M from the course somewhere (closer, the game's river already covers it),
 - most of it is river water in the mask (class 3), not a reservoir's or lake's (class 2), and none of it is sea,
 - it is not much longer than the course it leaves (no detours through a lake system),
 - it crosses at most MAX_BRIDGED_M of land in all (gaps of up to ~2·GAP_PX pixels: dams and embankments built since),
 - no other reach of the data already runs along it (HydroRIVERS sometimes has an arm as a small stream),
 - it does not overlap an arm already found.
A side arm becomes a new reach: it starts at a vertex of the main course (like a delta branch's head, RiverDeltas), its
level follows the main river's own profile between its ends, entered ARM_INSET_M below the split and left ARM_INSET_M
above the join (each reach gets its own stepped surface in the game, and a block too high at either end shows as water
running uphill), and it carries a share of the
discharge by its width against the main channel's (SHARE_MIN to SHARE_MAX). The main reach is split where the arm joins,
so the arm can flow into the lower piece; all existing reaches keep their Q (the main stem is not
reduced, so the total is not conserved: the arms are for the shape of the river, not its water balance).

    .venv/Scripts/python -I tools/river_arms.py <in rivers.bin> <out .bin> [--workers 12] [--only lat0,lon0,lat1,lon1]

On record version 3 rivers (tools/snap_rivers_channel.py, courses rules 4) the split pieces keep their vertices' widths and
each arm gets the mask's half width at its vertices measured the same way (snap_rivers_channel.widths_for; EARTH_RAWDATA
names the rawdata folder when it isn't the checkout's).

Writes <out> (EMRV v2, or v3 from v3) and <out>.json (every arm with its place, length, share and island). The game reads reaches in
flow order, every reach flowing into a later one, so the arms come first (nothing flows into them), then each old reach
followed by its split pieces: reach numbers change.
"""
import argparse, json, math, os, sys, time
from multiprocessing import Pool

import numpy as np
import scipy.ndimage as ndi
import scipy.sparse as sp
from scipy.sparse.csgraph import dijkstra

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from water_atlas_cards import mosaic_wbm
import glo90grid as gg
from snap_rivers_wbm import read_full, write_full, densify, length_m, rdp

MIN_Q = 200.0              # m³/s: rivers whose arms show in the game at all
MIN_ISLAND_KM2, MAX_ISLAND_KM2 = 0.5, 400.0
NEAR_M = 400.0             # the course runs within this of the island
MIN_OFFSET_M = 500.0       # the arm runs at least this far from the course somewhere
MIN_RIVER_SHARE = 0.5      # share of the arm on mask river water (class 3)
MAX_LONGER = 3.0           # arm length against the course it leaves (plus 2 km)
SHARE_MIN, SHARE_MAX = 0.15, 0.5
MARGIN = 0.15              # degrees of window around each 1° tile
GAP_PX = 3                 # land gaps up to about twice this (3" pixels) between water count as water: the dams and
                           # embankments that cut an arm off its river today (the Sausā Daugava at Rīgas HES)
GAP_COST = 8.0             # a bridged gap costs this much more than water
MAX_BRIDGED_M = 600.0      # an arm bridges at most this much land in all (Doles sala: 557 m at the HES embankment)
ARM_INSET_M = 3000.0       # an arm enters the main river's level profile this far below the split and leaves it this far above the join
PX_M = 111320.0 / 1200
T0 = time.time()
G = {}


def log(m):
    print(f"[{time.time() - T0:8.1f}s] {m}", flush=True)


def init_worker(path):
    R = read_full(path)
    G["R"] = R
    n = len(R)
    G["box"] = np.array([[a["lat"].min(), a["lon"].min(), a["lat"].max(), a["lon"].max()] for _, _, a in R], np.float32)
    G["q"] = np.array([q for q, _, _ in R], np.float32)
    # distance to the mouth along the network at each reach's last vertex, for the order along a river
    seg = [length_m(a["lat"].astype(np.float64), a["lon"].astype(np.float64)) if len(a) > 1 else 0.0 for _, _, a in R]
    end = np.full(n, -1.0)
    for r0 in range(n):
        stack, r = [], r0
        while r >= 0 and end[r] < 0 and len(stack) <= n:
            stack.append(r)
            r = R[r][1]
        base = 0.0 if r < 0 else end[r] + seg[r]
        for s in reversed(stack):
            end[s] = base
            base += seg[s]
    G["dtm_end"] = end


def vertex_dtm(r):
    """Distance to the mouth (m) at each vertex of reach r."""
    a = G["R"][r][2]
    lat, lon = a["lat"].astype(np.float64), a["lon"].astype(np.float64)
    if len(a) < 2:
        return np.array([G["dtm_end"][r]])
    kx = np.cos(np.radians(lat[:-1]))
    d = np.hypot(np.diff(lat) * 111320, np.diff(lon) * 111320 * kx)
    rest = np.concatenate([np.cumsum(d[::-1])[::-1], [0.0]])
    return G["dtm_end"][r] + rest


def graph(cost, ok, dx):
    idx = -np.ones(ok.shape, np.int64)
    n = int(ok.sum())
    idx[ok] = np.arange(n)
    rows, cols, wts = [], [], []
    for dr, dc, L in ((0, 1, dx), (1, 0, PX_M), (1, 1, math.hypot(dx, PX_M)), (1, -1, math.hypot(dx, PX_M))):
        if dc < 0:
            A, B = idx[:idx.shape[0] - dr, -dc:], idx[dr:, :idx.shape[1] + dc]
            CA, CB = cost[:cost.shape[0] - dr, -dc:], cost[dr:, :cost.shape[1] + dc]
        else:
            A, B = idx[:idx.shape[0] - dr, :idx.shape[1] - dc], idx[dr:, dc:]
            CA, CB = cost[:cost.shape[0] - dr, :cost.shape[1] - dc], cost[dr:, dc:]
        m = (A >= 0) & (B >= 0)
        rows.append(A[m]); cols.append(B[m]); wts.append(0.5 * (CA[m] + CB[m]) * L)
    return idx, sp.csr_matrix((np.concatenate(wts), (np.concatenate(rows), np.concatenate(cols))), shape=(n, n))


def work(tile):
    tl, tc = tile
    la0, lo0, la1, lo1 = tl - MARGIN, tc - MARGIN, tl + 1 + MARGIN, tc + 1 + MARGIN
    B, Q, R = G["box"], G["q"], G["R"]
    inwin = np.flatnonzero((B[:, 2] >= la0) & (B[:, 0] <= la1) & (B[:, 3] >= lo0) & (B[:, 1] <= lo1))
    big = inwin[Q[inwin] >= MIN_Q]
    if not len(big):
        return [], {}
    W = mosaic_wbm(la0, lo0, la1, lo1)
    H_, W_ = W.shape
    # pixel (r, c)'s area runs from (oy - r/1200, ox + c/1200) (glo90grid: half a pixel north-west of la1, lo0 on the
    # pixel-is-point grid)
    oy, ox = gg.top(la1), gg.left(lo0, 1200)
    if not ((W == 3).any()):
        return [], {"no river water": 1}
    dx = PX_M * math.cos(math.radians(tl + 0.5))
    samp = (PX_M, dx)
    water = (W == 2) | (W == 3)
    ocean = (W == 1) | (W == 255)
    # narrow land between water (a dam, an embankment) bridged: closing joins water across gaps up to ~2·GAP_PX
    wet = ndi.binary_closing(water, structure=np.ones((3, 3), bool), iterations=GAP_PX) | water
    gap = wet & ~water

    def to_rc(lat, lon):
        return np.floor((oy - lat) * 1200).astype(int), np.floor((lon - ox) * 1200).astype(int)

    # the big courses, densified: point -> (reach, vertex position, distance to mouth)
    P_r, P_k, P_d, P_lat, P_lon, P_f = [], [], [], [], [], []
    for r in big:
        a = R[r][2]
        if len(a) < 2:
            continue
        lat, lon = a["lat"].astype(np.float64), a["lon"].astype(np.float64)
        dl, do = densify(lat, lon, 30.0)
        # vertex position of each dense point (fractional) and its distance to the mouth
        kx = np.cos(np.radians(lat[:-1]))
        seg = np.hypot(np.diff(lat) * 111320, np.diff(lon) * 111320 * kx)
        cum = np.concatenate([[0], np.cumsum(seg)])
        dk = np.concatenate([[0], np.cumsum(np.hypot(np.diff(dl) * 111320, np.diff(do) * 111320 * np.cos(np.radians(dl[:-1]))))])
        kpos = np.interp(dk, cum, np.arange(len(lat)))
        vd = vertex_dtm(r)
        P_r.append(np.full(len(dl), r)); P_k.append(kpos); P_d.append(np.interp(kpos, np.arange(len(lat)), vd))
        P_lat.append(dl); P_lon.append(do)
        P_f.append(a["fixed"][np.round(kpos).astype(int)] > 0)
    if not P_r:
        return [], {}
    P_r, P_k, P_d, P_lat, P_lon, P_f = map(np.concatenate, (P_r, P_k, P_d, P_lat, P_lon, P_f))
    pr, pc = to_rc(P_lat, P_lon)
    inside = (pr >= 0) & (pr < H_) & (pc >= 0) & (pc < W_)
    P_r, P_k, P_d, P_lat, P_lon, P_f, pr, pc = (x[inside] for x in (P_r, P_k, P_d, P_lat, P_lon, P_f, pr, pc))
    if not len(pr):
        return [], {}
    pid = -np.ones((H_, W_), np.int64)
    pid[pr, pc] = np.arange(len(pr))
    D, (ir, ic) = ndi.distance_transform_edt(pid < 0, sampling=samp, return_indices=True)
    near_pt = pid[ir, ic]
    # every other reach (any size) for the "already in the data" test
    other = np.zeros((H_, W_), bool)
    for r in inwin:
        a = R[r][2]
        if len(a) < 2:
            continue
        dl, do = densify(a["lat"].astype(np.float64), a["lon"].astype(np.float64), 40.0)
        rr, cc = to_rc(dl, do)
        m = (rr >= 0) & (rr < H_) & (cc >= 0) & (cc < W_)
        other[rr[m], cc[m]] = True

    land = (W == 0) & ~wet
    lab, n = ndi.label(land)
    if n == 0:
        return [], {}
    px_km2 = PX_M * dx / 1e6
    area = np.bincount(lab.ravel(), minlength=n + 1) * px_km2
    border = np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))
    touch_sea = np.unique(lab[ndi.binary_dilation(ocean, iterations=2) & land])
    # nearest course distance per island
    mind = ndi.minimum(D, lab, np.arange(n + 1))
    border_s, sea_s = set(border.tolist()), set(touch_sea.tolist())
    cand = [i for i in range(1, n + 1)
            if MIN_ISLAND_KM2 <= area[i] <= MAX_ISLAND_KM2 and mind[i] <= NEAR_M and i not in border_s and i not in sea_s]
    stats = {"islands": len(cand)}
    if not cand:
        return [], stats
    inner = ndi.distance_transform_edt(water, sampling=samp)
    used = np.zeros((H_, W_), bool)
    slices = ndi.find_objects(lab)
    out = []
    for i in sorted(cand, key=lambda i: -area[i]):
        sl = slices[i - 1]
        # the island's centre must be in this tile (each island once)
        cy, cx = (sl[0].start + sl[0].stop) / 2, (sl[1].start + sl[1].stop) / 2
        clat, clon = oy - cy / 1200, ox + cx / 1200
        if not (tl <= clat < tl + 1 and tc <= clon < tc + 1):
            continue
        isl = lab == i
        # course points beside the island, of the biggest river there
        ring = ndi.binary_dilation(isl[max(sl[0].start - 12, 0):sl[0].stop + 12, max(sl[1].start - 12, 0):sl[1].stop + 12], iterations=8)
        r0, c0 = max(sl[0].start - 12, 0), max(sl[1].start - 12, 0)
        rr_, cc_ = np.nonzero(ring)
        pts = np.unique(near_pt[rr_ + r0, cc_ + c0][D[rr_ + r0, cc_ + c0] <= NEAR_M + 200])
        pts = pts[pts >= 0]
        if not len(pts):
            stats["no course"] = stats.get("no course", 0) + 1
            continue
        qmax = Q[P_r[pts]].max()
        pts = pts[Q[P_r[pts]] >= 0.5 * qmax]
        # one river: the points of the chain the biggest reach belongs to, ordered by distance to the mouth
        d_up, d_dn = P_d[pts].max() + 400, P_d[pts].min() - 400
        main_r = P_r[pts[np.argmax(Q[P_r[pts]])]]
        chain = set()
        r = main_r
        while r >= 0 and r not in chain and len(chain) < 64:
            chain.add(r)
            r = R[r][1]
        # upstream reaches of the same river (biggest inflow each time)
        on = np.isin(P_r, list(chain)) | (Q[P_r] >= 0.5 * qmax)
        sel = np.flatnonzero(on & (P_d <= d_up) & (P_d >= d_dn))
        if len(sel) < 3:
            stats["short course"] = stats.get("short course", 0) + 1
            continue
        s_pt = sel[np.argmax(P_d[sel])]
        e_pt = sel[np.argmin(P_d[sel])]
        # the river there is in a lake or the sea in the game (fixed to its level): the game's water covers it already
        if P_f[sel].any():
            stats["in lake or sea"] = stats.get("in lake or sea", 0) + 1
            continue
        # block the course between the ends (keep 150 m at each end open)
        blk = np.zeros((H_, W_), bool)
        mid = sel[(P_d[sel] < P_d[s_pt] - 150) & (P_d[sel] > P_d[e_pt] + 150)]
        blk[pr[mid], pc[mid]] = True
        blk = ndi.binary_dilation(blk, iterations=2) & ~isl
        # local window around the island
        pad = int(max(30, 0.5 * max(sl[0].stop - sl[0].start, sl[1].stop - sl[1].start)))
        a0, a1 = max(sl[0].start - pad, 0), min(sl[0].stop + pad, H_)
        b0, b1 = max(sl[1].start - pad, 0), min(sl[1].stop + pad, W_)
        sr, sc, er, ec = pr[s_pt], pc[s_pt], pr[e_pt], pc[e_pt]
        if not (a0 <= sr < a1 and b0 <= sc < b1 and a0 <= er < a1 and b0 <= ec < b1):
            stats["ends outside"] = stats.get("ends outside", 0) + 1
            continue
        wl = wet[a0:a1, b0:b1] & ~blk[a0:a1, b0:b1] & ~used[a0:a1, b0:b1]
        wl[sr - a0, sc - b0] = wl[er - a0, ec - b0] = True
        cost = 1.0 + 2.0 / (1.0 + inner[a0:a1, b0:b1] / PX_M) + GAP_COST * gap[a0:a1, b0:b1]
        idx, Gm = graph(cost, wl, dx)
        s, e = idx[sr - a0, sc - b0], idx[er - a0, ec - b0]
        dist, pred = dijkstra(Gm, directed=False, indices=int(s), return_predecessors=True)
        if not np.isfinite(dist[e]):
            stats["no way round"] = stats.get("no way round", 0) + 1
            stats.setdefault("failed", []).append(("no way round", round(clat, 3), round(clon, 3), round(float(area[i]), 1)))
            continue
        path = [int(e)]
        while path[-1] != s:
            path.append(int(pred[path[-1]]))
        path.reverse()
        qr, qc = np.nonzero(wl)
        qr, qc = qr[path] + a0, qc[path] + b0
        plat, plon = oy - (qr + 0.5) / 1200, ox + (qc + 0.5) / 1200
        L_arm = length_m(plat, plon)
        L_main = P_d[s_pt] - P_d[e_pt]
        off = float(D[qr, qc].max())
        cls = W[qr, qc]
        bridged = float(gap[qr, qc].sum() * PX_M)
        why = None
        if off < MIN_OFFSET_M:
            why = "close to the course"
        elif (cls == 3).mean() < MIN_RIVER_SHARE:
            why = "not river water"
        elif bridged > MAX_BRIDGED_M:
            why = "bridges too much land"
        elif L_arm > MAX_LONGER * L_main + 2000:
            why = "long way round"
        elif (ndi.binary_dilation(other, iterations=2)[qr, qc] & (D[qr, qc] > 200)).sum() > 0.5 * (D[qr, qc] > 200).sum():
            why = "already in the data"
        elif used[qr, qc].mean() > 0.3:
            why = "overlaps an arm"
        if why:
            stats[why] = stats.get(why, 0) + 1
            stats.setdefault("failed", []).append((why, round(clat, 3), round(clon, 3), round(float(area[i]), 1)))
            continue
        # mark the arm and a little around it as used
        u = np.zeros((H_, W_), bool)
        u[qr, qc] = True
        used |= ndi.binary_dilation(u, iterations=3)
        hw_arm = float(np.median(inner[qr, qc][D[qr, qc] > 200])) if (D[qr, qc] > 200).any() else PX_M
        mm = sel[(P_d[sel] <= P_d[s_pt]) & (P_d[sel] >= P_d[e_pt])]
        hw_main = float(np.median(inner[pr[mm], pc[mm]])) if len(mm) else hw_arm
        share = float(np.clip(hw_arm / max(hw_arm + hw_main, 1.0), SHARE_MIN, SHARE_MAX))
        xy = np.c_[(plon - plon[0]) * 111320 * math.cos(math.radians(clat)), (plat - plat[0]) * 111320]
        k = rdp(xy, 0.5 * PX_M)
        out.append(dict(s_reach=int(P_r[s_pt]), s_k=float(P_k[s_pt]), e_reach=int(P_r[e_pt]), e_k=float(P_k[e_pt]),
                        lat=plat[k].tolist(), lon=plon[k].tolist(), share=round(share, 3), q_main=float(Q[P_r[s_pt]]),
                        island_km2=round(float(area[i]), 2), island=[round(clat, 4), round(clon, 4)],
                        length_km=round(L_arm / 1000, 2), offset_m=round(off), river_share=round(float((cls == 3).mean()), 2),
                        bridged_m=round(bridged)))
        stats["arms"] = stats.get("arms", 0) + 1
    return out, stats


def main_profile(R, rs, ks, re_, ke, limit=64):
    """The main course from (rs, ks) down to (re_, ke): distance along it (m) and level per vertex, or (None, None)."""
    d, lv, r, k, prev = [0.0], [float(R[rs][2][ks]["lev"])], rs, ks, R[rs][2][ks]
    for _ in range(limit):
        arr = R[r][2]
        stop = ke if r == re_ else len(arr) - 1
        if r == re_ and k > ke:
            return None, None
        for j in range(k + 1, stop + 1):
            p = arr[j]
            d.append(d[-1] + math.hypot((float(p["lat"]) - float(prev["lat"])) * 111320,
                                        (float(p["lon"]) - float(prev["lon"])) * 111320 * math.cos(math.radians(float(p["lat"])))))
            lv.append(float(p["lev"]))
            prev = p
        if r == re_:
            return (np.array(d), np.minimum.accumulate(np.array(lv))) if d[-1] > 0 else (None, None)
        r, k = R[r][1], 0
        if r < 0:
            return None, None
    return None, None


def build(R, arms):
    """New reach list: the old reaches (split where arms join), then the arms."""
    # the arm's ends on real vertices: the start at the nearest vertex, the join at the nearest vertex below it
    splits = {}
    plan = []
    for a in arms:
        rs, ks = a["s_reach"], int(round(a["s_k"]))
        re_, ke = a["e_reach"], int(round(a["e_k"]))
        ns, ne = len(R[rs][2]), len(R[re_][2])
        ks, ke = min(max(ks, 0), ns - 1), min(max(ke, 0), ne - 1)
        if rs == re_ and ke <= ks:
            continue
        # a join on a reach's last vertex is its next reach's first
        if ke == ne - 1:
            if R[re_][1] < 0:
                continue  # joins at the river's very end: the mouth is the sea's or a lake's, not an arm's
            re_, ke = R[re_][1], 0
        if ke > 0:
            splits.setdefault(re_, set()).add(ke)
        plan.append((a, rs, ks, re_, ke))
    out = [list(x) for x in R]
    piece_at = {}  # (reach, vertex) -> index of the reach that starts there
    extra = {}  # reach -> its later pieces, in order
    for r, ks_ in splits.items():
        q, nxt, arr = R[r]
        cuts = sorted(ks_)
        bounds = [0] + cuts + [len(arr) - 1]
        idxs = [r] + [len(out) + i for i in range(len(cuts))]
        extra[r] = idxs[1:]
        for i in range(len(cuts)):
            out.append(None)
        for j in range(len(bounds) - 1):
            seg = arr[bounds[j]:bounds[j + 1] + 1].copy()
            nn = idxs[j + 1] if j + 1 < len(idxs) else nxt
            out[idxs[j]] = [q, nn, seg]
            piece_at[(r, bounds[j])] = idxs[j]
    dt = R[0][2].dtype
    added = []
    for a, rs, ks, re_, ke in plan:
        sv = R[rs][2][ks]
        ev = R[re_][2][ke]
        lat = np.r_[sv["lat"], np.array(a["lat"][1:-1], np.float32), ev["lat"]].astype(np.float32)
        lon = np.r_[sv["lon"], np.array(a["lon"][1:-1], np.float32), ev["lon"]].astype(np.float32)
        L = np.concatenate([[0], np.cumsum(np.hypot(np.diff(lat.astype(np.float64)) * 111320,
                                                     np.diff(lon.astype(np.float64)) * 111320 * np.cos(np.radians(lat[:-1].astype(np.float64)))))])
        # Levels: the main river's own profile between the ends, entered a little below the split and left a little
        # above the join. Each reach gets its own stepped surface in the game, so water then only steps down where the
        # arm leaves the main river and where it rejoins it, never up.
        md, ml = main_profile(R, rs, ks, re_, ke)
        if md is None:
            continue
        # (the audit, 2,598 arms: 88 one-block steps up on them with 3 km, 103 with none, 157 holding the split's level)
        inset = min(ARM_INSET_M, 0.25 * md[-1])
        t = inset + (md[-1] - 2 * inset) * L / max(L[-1], 1)
        lev = np.minimum.accumulate(np.round(np.interp(t, md, ml))).astype(np.int16)
        arr = np.zeros(len(lat), dt)
        arr["lat"], arr["lon"], arr["lev"] = lat, lon, lev
        arr["fixed"] = 0
        nxt = piece_at.get((re_, ke), re_ if ke == 0 else None)
        if nxt is None:
            continue
        out.append([float(a["share"] * a["q_main"]), int(nxt), arr])
        a["reach"] = len(out) - 1
        added.append(a)
    # The game reads the network in flow order: every reach's Next comes after it ("downstream reaches come later",
    # RiverNetwork). So: the arms first (nothing flows into them), then each old reach followed by its split pieces.
    order = [a["reach"] for a in added]
    for r in range(len(R)):
        order.append(r)
        order += extra.get(r, [])
    assert len(order) == len(out) and len(set(order)) == len(out)
    new_of = {old: i for i, old in enumerate(order)}
    res = []
    for old in order:
        q, nx, arr = out[old]
        res.append((q, new_of[nx] if nx >= 0 else -1, arr))
    for a in added:
        a["reach"] = new_of[a["reach"]]
    bad = sum(1 for i, (q, nx, arr) in enumerate(res) if 0 <= nx <= i)
    assert bad == 0, f"{bad} reaches flow into an earlier one"
    return res, added


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("out")
    ap.add_argument("--workers", type=int, default=12)
    ap.add_argument("--only")
    o = ap.parse_args()
    R = read_full(o.src)
    log(f"{len(R)} reaches")
    tiles = set()
    for q, _, a in R:
        if q >= MIN_Q and len(a) >= 2:
            for la, lo in zip(a["lat"][::4], a["lon"][::4]):
                tiles.add((math.floor(la), math.floor(lo)))
    if o.only:
        b = [float(v) for v in o.only.split(",")]
        tiles = {t for t in tiles if b[0] - 1 < t[0] <= b[2] and b[1] - 1 < t[1] <= b[3]}
    tiles = sorted(tiles)
    log(f"{len(tiles)} tiles with rivers of {MIN_Q:.0f} m³/s or more")
    arms, stats, done = [], {}, 0
    with Pool(o.workers, initializer=init_worker, initargs=(o.src,)) as pool:
        for res, st in pool.imap_unordered(work, tiles, chunksize=4):
            arms += res
            for k, v in st.items():
                stats[k] = stats.get(k, 0) + v if k != "failed" else stats.get(k, []) + v
            done += 1
            if done % 500 == 0:
                log(f"{done}/{len(tiles)} tiles, {len(arms)} arms")
    # in a fixed order (the tiles finish in any), so the same rivers give the same list and reach numbers
    arms.sort(key=lambda a: (a["s_reach"], a["s_k"], a["e_reach"], a["e_k"], a["island"]))
    out, added = build(R, arms)
    if "width" in R[0][2].dtype.names:
        # record version 3 (0063, courses rules 4): split reaches keep their vertices' widths; the arms get theirs
        # measured as the moved courses' were (tools/snap_rivers_channel.py)
        import snap_rivers_channel as channel
        ws = channel.widths_for([(out[a["reach"]][2]["lat"], out[a["reach"]][2]["lon"]) for a in added], o.workers)
        for a, w in zip(added, ws):
            out[a["reach"]][2]["width"] = w
            a["width_m"] = [int(v) * 5 for v in (w.min(), np.median(w), w.max())]
    write_full(o.out, out)
    json.dump(dict(source=os.path.basename(o.src), stats=stats, arms=len(added), reaches=len(out),
                   km=round(sum(a["length_km"] for a in added)), list=sorted(added, key=lambda a: -a["island_km2"])),
              open(o.out + ".json", "w", encoding="utf8"), indent=0)
    log(f"{len(added)} side arms ({sum(a['length_km'] for a in added):.0f} km), {len(out)} reaches; {stats}; wrote {o.out}")


if __name__ == "__main__":
    main()
