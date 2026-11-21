"""
Earth decision 0069 rules 1 and 4: rivers-flow-glo90-1.bin, the flow of the water mask's river water (the water river
outlines 3 draw, tools/bake_river_outlines.py masks_v3), solved once per 1-degree tile from the river courses (courses
rules 4, with their discharges): which water carries the river's flow, in which direction and how fast.

Per tile, each connected body of river water (8-connected) gets
  inflows  where a course enters it: from land or a lake, across the tile's edge, or a headwater starting in it, each
           with the course's discharge (a tile edge's inflow spread over the body's pixels along that edge);
  outflows where the water meets the sea (every body pixel next to the mask's ocean), where a course leaves it for land or
           a lake, or across the tile's edge (that edge's run of body pixels), all at potential 0.
The flow through the body is the potential flow between them, more easily where the water is deeper (conductance
depth^(5/3), the depth growing with the distance from the shore up to DEEP_PX pixels): div(T grad phi) = sources,
no flow through the banks. A body no course enters carries none (side arms, backwaters, canals: still water); a body
with inflows but no outflow drains at its course's last pixel in it.

Each pixel's flux q (m3/s per metre of width) becomes a speed v = q / d with the body's depth by its largest inflow
Q (hydraulic geometry d = DEPTH_C * Q^DEPTH_F m), and a direction. A course that leaves its body for the sea across
land while the body reaches the sea by its own water (the Lielupe at 23.85 E, the Daugava at Vecaki) is not an outflow
there: tools/bake_river_mouths.py reroutes it through the body's water.

File (little-endian): "EMFL", int32 version 1, int32 1 (tile degrees), int32 tiles across (360) and down (180), the
course key (uint64) of the courses it was solved on, int32 the reroutes' offset, a state byte per tile (255 no data, 0 no river water, 1 flow), then
per tile with flow (int32 offset, int32 length), then each such tile's brotli stream: int32 height, int32 width, then
two byte rasters of height x width: the speed (cm/s, 0 for no flow or no river water, 1-255) and the direction (0-255,
a 256th of a turn, 0 east, counter-clockwise: 64 north). At the reroutes' offset: int32 count, then per rerouted reach
(by index): int32 reach, int32 the vertex after which its new end starts, int32 points, float32 latitude and longitude
per point (the body's water to its own mouth).

Version 2 (decision 0092, EA-0148, rivers-flow-glo90-2.bin): the same solve on GLO-90's pixel-is-point grid
(tools/glo90grid.py; EARTH_GLO90_GRID=cell gives version 1's placement): pixel (i, j) of a tile is the mask's cell
centred on (lat0 + 1 - i/hp, lon0 + j/wp), the courses (courses rules 5) are put on pixels by that, and the reroutes'
points lie on those centres. The layout is version 1's; RiverFlowData reads a version 2 file's pixels on those centres
(a version 1 file's half a pixel south-east of them, as it was solved).

    python -I tools/bake_river_flow.py OUT COURSES.bin --reservoirs DAMS/reservoirs-dams.npz --corridors DAMS/corridors.npz
           [--canals rawdata/osm-canals] [--workers 16] [--tiles N56E023,N57E024]
"""
import glob, hashlib, math, os, struct, sys, time
from multiprocessing import Pool

import brotli
import numpy as np
from scipy import ndimage as ndi
from scipy.sparse import coo_matrix
from scipy.sparse.linalg import spsolve

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import bake_river_outlines as bro          # noqa: E402  masks_v3, init_worker, the WBM folder
import glo90grid as gg                     # noqa: E402
from bake_dam_removal import read_courses  # noqa: E402

VERSION = 2 if gg.POINT else 1
DEEP_PX = 4            # the depth grows from the shore over this many pixels (about 370 m)
DEPTH_C, DEPTH_F = 0.27, 0.39   # mean depth d = c Q^f (m), Leopold & Maddock's at-a-station form
SPEED_CAP = 255        # cm/s
TOUCH_PX = 3           # a course within this many pixels of a body touches it
MOUTH_PX = 4           # a mouth reach that meets the sea within this many pixels of leaving its water ends there
WINDOW_DEG = 0.25      # each tile is solved with this much of its neighbours around it (a river crossing the tile's
                       # edge, a mouth just beyond it), and only the tile is kept
T0 = time.time()
G = {}


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


def tile_name(lat0, lon0):
    return f"Copernicus_DSM_COG_30_{'N' if lat0 >= 0 else 'S'}{abs(lat0):02d}_00_{'E' if lon0 >= 0 else 'W'}{abs(lon0):03d}_00_WBM.tif"


def course_arrays(courses_path, out, key=None):
    """The courses' stations as arrays in OUT/courses.npz (read once; workers map it): reach, lat, lon, each reach's
    first station, discharge and next reach, whether it has an upstream reach, and the stations sorted by 1-degree tile."""
    path = os.path.join(out, "courses.npz")
    if os.path.exists(path):
        return path
    if key is not None:
        import bake_dam_removal as bdr
        real = bdr.course_key
        bdr.course_key = lambda *a: key          # the key is given: no byte-by-byte hash in Python
        C = read_courses(courses_path)
        bdr.course_key = real
    else:
        C = read_courses(courses_path)
    t = ((np.floor(C["lat"]).astype(np.int64) + 90) * 360 + (np.floor(C["lon"]).astype(np.int64) + 180))
    order = np.argsort(t, kind="stable")
    up = np.zeros(len(C["q"]), bool)
    nx = C["next"]
    up[nx[nx >= 0]] = True
    # the side arms RiverArmsData puts first (their flow is a share of their river's, which already counts it): from
    # river_arms.py's report beside the list (EA-0148: courses rules 5's list has its own), else rivers-glo90-arms-2.bin's
    import json
    report = courses_path + ".json"
    if os.path.exists(report) and "arms" in json.load(open(report, encoding="utf8")):
        arms = int(json.load(open(report, encoding="utf8"))["arms"])
    else:
        arms_file = os.path.join(os.path.dirname(TOOLS), "earthdata", "rivers-glo90-arms-2.bin")
        arms = struct.unpack_from("<i", open(arms_file, "rb").read(16), 12)[0] if os.path.exists(arms_file) else 0
    np.savez(path, reach=C["reach"], lat=C["lat"], lon=C["lon"], start=C["start"], q=C["q"], next=nx, up=up,
             order=order, tsorted=t[order], key=np.array([C["key"]], np.uint64), arms=np.array([arms]))
    return path


def tile_stations(key):
    """The stations in a tile and the ones next to them along their reach."""
    lo, hi = np.searchsorted(G["tsorted"], [key, key + 1])
    if lo == hi:
        return None
    st = G["order"][lo:hi]
    reach = G["reach"]
    prev, nxt = st - 1, st + 1
    prev = prev[(prev >= 0) & (reach[np.maximum(prev, 0)] == reach[st])]
    nxt = nxt[nxt < len(reach)]
    nxt = nxt[reach[nxt] == reach[nxt - 1]]
    return np.unique(np.concatenate([st, prev, nxt]))


def init(res, cor, courses_npz, canal_dir, cache):
    # the river water as the outlines of this version draw it (4: with the short bulges trimmed, 0069 rule 5)
    os.environ["EARTH_COURSES_NPZ"] = courses_npz
    bro.init_worker(res, cor, int(os.environ.get("EARTH_OUTLINE_VERSION", "4")))
    G["cache"] = cache
    z = np.load(courses_npz)
    for k in ("reach", "lat", "lon", "start", "q", "next", "up", "order", "tsorted"):
        G[k] = z[k]
    G["arms"] = int(z["arms"][0]) if "arms" in z.files else 0
    G["canals"] = None
    if canal_dir:
        import bake_works_1318 as bw
        import json, shapely
        idx = json.load(open(os.path.join(canal_dir, "index.json"), encoding="utf-8"))
        corr = []
        for c in idx:
            p = os.path.join(canal_dir, c["key"] + ".json")
            if os.path.exists(p):
                lines, polys = bw.osm_canal(p)
                if lines or polys:
                    g = bw.canal_corridor(lines, polys)
                    shapely.prepare(g)
                    corr.append(g)
        G["canals"] = corr


def course_runs(lat0, lon0, hp, wp, H, W, top, left):
    """The courses through the window as pixel paths: per reach, its pixels in order (row, column; -1 outside the
    window), sampled every quarter pixel along its stations. hp, wp: pixels per degree; top, left: the window's corner."""
    sts = []
    for dla in (-1, 0, 1):
        for dlo in (-1, 0, 1):
            la_, lo_ = lat0 + dla, (lon0 + dlo + 180) % 360 - 180
            if -90 <= la_ < 90:
                t = tile_stations((la_ + 90) * 360 + (lo_ + 180))
                if t is not None:
                    sts.append(t)
    if not sts:
        return {}
    st = np.unique(np.concatenate(sts))
    reach = G["reach"][st]
    out = {}
    for r in np.unique(reach):
        idx = st[reach == r]
        # consecutive runs of this reach's stations
        breaks = np.flatnonzero(np.diff(idx) != 1) + 1
        for run in np.split(idx, breaks):
            la, lo = G["lat"][run], G["lon"][run]
            pr = (top - la) * hp
            pc = ((lo - left + 180) % 360 - 180) * wp
            pts, sts = [], []
            for k in range(len(run) - 1):
                n = max(1, int(math.ceil(4 * max(abs(pr[k + 1] - pr[k]), abs(pc[k + 1] - pc[k])))))
                f = np.arange(n) / n
                pts.append(np.column_stack([pr[k] + (pr[k + 1] - pr[k]) * f, pc[k] + (pc[k + 1] - pc[k]) * f]))
                sts.append(np.full(n, run[k]))
            pts.append(np.array([[pr[-1], pc[-1]]]))
            sts.append(np.array([run[-1]]))
            p = np.floor(np.vstack(pts)).astype(np.int64)
            sid = np.concatenate(sts)
            keep = np.ones(len(p), bool)
            keep[1:] = np.any(np.diff(p, axis=0) != 0, axis=1)
            p, sid = p[keep], sid[keep]
            inside = (p[:, 0] >= 0) & (p[:, 0] < H) & (p[:, 1] >= 0) & (p[:, 1] < W)
            p[~inside] = -1
            out.setdefault(int(r), []).append((run, p, sid))
    return out


def edge_run(lab, body, r, c, hp, wp):
    """The body's pixels along the tile edge nearest (r, c): the connected run of them on that edge."""
    sides = []
    if r <= 0: sides.append(("top", 0, slice(None)))
    if r >= hp - 1: sides.append(("bottom", hp - 1, slice(None)))
    if c <= 0: sides.append(("left", slice(None), 0))
    if c >= wp - 1: sides.append(("right", slice(None), wp - 1))
    out = []
    for name, rs, cs in sides:
        line = lab[rs, cs] == body
        pos = c if name in ("top", "bottom") else r
        pos = min(max(pos, 0), len(line) - 1)
        if not line[pos]:
            near = np.flatnonzero(line)
            if len(near) == 0:
                continue
            pos = near[np.argmin(abs(near - pos))]
        l = pos
        while l > 0 and line[l - 1]:
            l -= 1
        h = pos
        while h < len(line) - 1 and line[h + 1]:
            h += 1
        for k in range(l, h + 1):
            if name == "top":
                out.append((0, k))
            elif name == "bottom":
                out.append((hp - 1, k))
            elif name == "left":
                out.append((k, 0))
            else:
                out.append((k, wp - 1))
    return out


def to_sea(lab, b, near_sea, start):
    """The shortest 8-connected path through body b from start to a pixel of it next to the sea (pixels), every third
    pixel and the last; None when there is none."""
    from collections import deque
    hp, wp = lab.shape
    prev = {start: None}
    dq = deque([start])
    while dq:
        i, j = dq.popleft()
        if near_sea[i, j]:
            path, cur = [], (i, j)
            while cur is not None:
                path.append(cur)
                cur = prev[cur]
            path = path[::-1]
            return path[::3] + ([path[-1]] if (len(path) - 1) % 3 else [])
        for di in (-1, 0, 1):
            for dj in (-1, 0, 1):
                ni, nj = i + di, j + dj
                if (di or dj) and 0 <= ni < hp and 0 <= nj < wp and lab[ni, nj] == b and (ni, nj) not in prev:
                    prev[(ni, nj)] = (i, j)
                    dq.append((ni, nj))
    return None


def prep(path):
    """Pass 1: a tile's river water as the outlines take it (masks_v3), less the canals, into the cache; None without."""
    name = os.path.basename(path)
    lat0, lon0 = bro.tile_origin(name)
    m = bro.masks_v3(path)
    if m is None:
        return None
    water = m["river"].copy()
    hp, wp = water.shape
    if G["canals"]:
        import shapely
        rr, cc = np.nonzero(water)
        if len(rr):
            # the cells' centres (glo90grid: on the pixel-is-point grid a cell's area runs from half a cell north-west)
            la = gg.top(lat0 + 1, hp) - (rr + 0.5) / hp
            lo = gg.left(lon0, wp) + (cc + 0.5) / wp
            dig = np.zeros(len(rr), bool)
            for g in G["canals"]:
                x0, y0, x1, y1 = g.bounds
                if x1 < lon0 or x0 > lon0 + 1 or y1 < lat0 or y0 > lat0 + 1:
                    continue
                dig |= shapely.contains_xy(g, lo, la)
            water[rr[dig], cc[dig]] = False
    wbm = m["wbm"]
    np.savez(os.path.join(G["cache"], name.replace("_WBM.tif", ".npz")), shape=np.array([hp, wp]), water=np.packbits(water),
             sea=np.packbits(wbm == 1), lake=np.packbits(wbm == 2))
    return name if water.any() else None


def tile_masks(lat0, lon0):
    """A tile's river water, sea and lake (from the cache, else its water mask: no river water there), or None where
    GLO-90 has no tile (open sea: all sea)."""
    if not -90 <= lat0 < 90:
        return None
    lon0 = (lon0 + 180) % 360 - 180
    name = bro.tile_name(lat0, lon0) if hasattr(bro, "tile_name") else tile_name(lat0, lon0)
    c = os.path.join(G["cache"], name.replace("_WBM.tif", ".npz"))
    if os.path.exists(c):
        z = np.load(c)
        hp, wp = z["shape"]
        n = int(hp) * int(wp)
        return tuple(np.unpackbits(z[k])[:n].reshape(hp, wp).astype(bool) for k in ("water", "sea", "lake"))
    tp = os.path.join(bro.WBM, name)
    if not os.path.exists(tp):
        return "sea"
    import tifffile
    w = tifffile.imread(tp)
    return np.zeros(w.shape, bool), w == 1, w == 2


def window(lat0, lon0):
    """The tile with WINDOW_DEG of its neighbours around it, at the tile's own pixels per degree: river water, sea, lake;
    the tile's size and margins."""
    centre = tile_masks(lat0, lon0)
    hp, wp = centre[0].shape
    mr, mc = int(round(WINDOW_DEG * hp)), int(round(WINDOW_DEG * wp))
    H, W = hp + 2 * mr, wp + 2 * mc
    out = [np.zeros((H, W), bool), np.zeros((H, W), bool), np.zeros((H, W), bool)]
    for dla in (-1, 0, 1):
        for dlo in (-1, 0, 1):
            # the window's rows and columns this neighbour covers
            r0, r1 = (mr - hp, mr) if dla == 1 else (mr, mr + hp) if dla == 0 else (mr + hp, mr + 2 * hp)
            c0, c1 = (mc - wp, mc) if dlo == -1 else (mc, mc + wp) if dlo == 0 else (mc + wp, mc + 2 * wp)
            r0c, r1c, c0c, c1c = max(r0, 0), min(r1, H), max(c0, 0), min(c1, W)
            if r0c >= r1c or c0c >= c1c:
                continue
            m = centre if dla == 0 and dlo == 0 else tile_masks(lat0 + dla, lon0 + dlo)
            if m is None:
                continue
            if isinstance(m, str):
                out[1][r0c:r1c, c0c:c1c] = True
                continue
            nh, nw = m[0].shape
            rows = ((np.arange(r0c, r1c) - r0) * nh) // hp
            if gg.POINT:
                # across a parallel where GLO-90's column count changes: the nearest cell
                cols = np.minimum(np.floor((np.arange(c0c, c1c) - c0) * nw / wp + 0.5).astype(np.int64), nw - 1)
            else:
                cols = ((np.arange(c0c, c1c) - c0) * nw) // wp
            for k in range(3):
                out[k][r0c:r1c, c0c:c1c] = m[k][np.ix_(rows, cols)]
    return out[0], out[1], out[2], hp, wp, mr, mc


def tile(name):
    lat0, lon0 = bro.tile_origin(name)
    water, sea, lake, hp_t, wp_t, mr, mc = window(lat0, lon0)
    H, W = water.shape
    # the north-west corner of the window's pixel (0, 0)'s area (glo90grid: half a pixel north-west on the
    # pixel-is-point grid)
    top, left = gg.top(lat0 + 1 + mr / hp_t, hp_t), gg.left(lon0 - mc / wp_t, wp_t)
    hp, wp = H, W         # (the solve below works on the window)
    lab, nb = ndi.label(water)          # 4-connected: the solve couples 4 neighbours
    if nb == 0:
        return {"name": name, "lat0": lat0, "lon0": lon0, "blob": None, "stats": {}}
    # pixel size (m): north-south and east-west
    dy = 111320.0 / hp_t
    lat_mid = lat0 + 0.5
    dx = 111320.0 * math.cos(math.radians(lat_mid)) / wp_t
    # conductance from the depth: distance from the shore, up to DEEP_PX
    dist = ndi.distance_transform_edt(water)
    depth = np.clip(dist / DEEP_PX, 0.25, 1.0)
    T = np.where(water, depth ** (5.0 / 3.0), 0.0)
    src = np.zeros((hp, wp))
    fixed = np.zeros((hp, wp), bool)
    # every body pixel next to the sea drains to it
    near_sea = ndi.binary_dilation(sea, structure=np.array([[0, 1, 0], [1, 1, 1], [0, 1, 0]], bool)) & water
    fixed |= near_sea
    body_q = np.zeros(nb + 1)
    stats = {"inflows": 0, "outflows": 0, "headwaters": 0, "wrong_way": 0}
    reroutes = []
    # every pixel within TOUCH_PX of the water: the body it touches and the water pixel it touches (a course is not
    # exactly in the mask's water: it runs on the bank, steps off and back)
    dist_w, (tr_, tc_) = ndi.distance_transform_edt(~water, return_indices=True)
    touch_body = np.where(dist_w <= TOUCH_PX, lab[tr_, tc_], 0)
    has_sea = np.bincount(lab.ravel(), weights=near_sea.ravel(), minlength=nb + 1) > 0
    # each reach's path through the tile (its runs in station order; outside the tile -1), and per body it touches the
    # first and last touch along it
    paths = {}
    for r, runs in course_runs(lat0, lon0, hp_t, wp_t, H, W, top, left).items():
        runs = sorted(runs, key=lambda rp: rp[0][0])
        p = np.vstack([rp[1] for rp in runs])
        sid = np.concatenate([rp[2] for rp in runs])
        first_station, last_station = runs[0][0][0], runs[-1][0][-1]
        inside = p[:, 0] >= 0
        B = np.zeros(len(p), np.int64)
        B[inside] = touch_body[p[inside, 0], p[inside, 1]]
        touches = {}
        for b in np.unique(B[B > 0]):
            k = np.flatnonzero(B == b)
            touches[int(b)] = (int(k[0]), int(k[-1]))
            body_q[b] = max(body_q[b], float(G["q"][r]))
        paths[r] = dict(p=p, sid=sid, inside=inside, B=B, touches=touches,
                        starts=first_station == G["start"][r] and inside[0],
                        ends=last_station == G["start"][r + 1] - 1 and inside[-1])
    upstream = {}
    for r in paths:
        nx = int(G["next"][r])
        if nx in paths:
            upstream.setdefault(nx, []).append(r)

    def at(p, k):
        """The water pixel a path pixel touches."""
        i, j = p[k]
        return int(tr_[i, j]), int(tc_[i, j])

    def spread(b, k, p, q):
        px = edge_run(lab, b, p[k, 0], p[k, 1], hp, wp)
        tw = sum(T[i, j] for i, j in px) or 1.0
        for i, j in px:
            src[i, j] += q * T[i, j] / tw

    def touches_down(r, b):
        """Whether a reach further down the chain (in the window) touches body b again."""
        nx, hops = int(G["next"][r]), 0
        while nx >= 0 and nx in paths and hops < 10000:
            if b in paths[nx]["touches"]:
                return True
            nx, hops = int(G["next"][nx]), hops + 1
        return False

    memo_up = {}

    def touches_up(r, b):
        """Whether a reach upstream of r (in the window) touches body b: its flow is in the body already."""
        key = (r, b)
        if key in memo_up:
            return memo_up[key]
        memo_up[key] = False
        stack, seen, hit = list(upstream.get(r, [])), set(), False
        while stack and not hit:
            u = stack.pop()
            if u in seen:
                continue
            seen.add(u)
            hit = b in paths[u]["touches"]
            stack += upstream.get(u, [])
        memo_up[key] = hit
        return hit

    for r, P in paths.items():
        q = float(G["q"][r])
        p, inside, B = P["p"], P["inside"], P["B"]
        n = len(p)
        nx = int(G["next"][r])
        for b, (f, l) in P["touches"].items():
            # inflow at the first touch, unless the flow came into the body along an upstream reach (a course that steps
            # onto land at a junction and comes back stays in the body)
            if r < G["arms"]:
                pass                                        # a side arm: its share is in its river's flow already
            elif touches_up(r, b):
                # the flow its upstream reaches brought into this body is in it; a tributary that joined on land adds
                # the rest
                brought = sum(float(G["q"][u]) for u in upstream.get(r, []) if b in paths[u]["touches"] or touches_up(u, b))
                if q - brought > 0.5:
                    src[at(p, f)] += q - brought
                    stats["inflows"] += 1
            elif f == 0 and P["starts"]:
                src[at(p, f)] += q                          # a headwater, or a confluence on land beside the body
                stats["headwaters" if not G["up"][r] else "inflows"] += 1
            elif f == 0 or not inside[f - 1]:
                spread(b, f, p, q)                          # from beyond the window
                stats["inflows"] += 1
            else:
                src[at(p, f)] += q
                stats["inflows"] += 1
            # outflow at the last touch, unless a reach further down touches the body again
            if touches_down(r, b):
                continue
            if l == n - 1 and P["ends"]:
                if nx < 0 and has_sea[b]:
                    continue                                # a mouth: the sea drains it
                fixed[at(p, l)] = True
                stats["outflows"] += 1
            elif l == n - 1 or not inside[l + 1]:
                for i, j in edge_run(lab, b, p[l, 0], p[l, 1], hp, wp):
                    fixed[i, j] = True                      # beyond the window
                stats["outflows"] += 1
            else:
                # the course leaves the body across land for good
                ahead = p[l + 1:][inside[l + 1:]]
                at_sea = np.flatnonzero(sea[ahead[:, 0], ahead[:, 1]]) if len(ahead) else np.array([], np.int64)
                if nx < 0 and len(at_sea) and at_sea[0] <= MOUTH_PX:
                    continue                                # its mouth: it meets the sea right after its water
                # a mouth reach that goes on to the sea across land while the body reaches the sea by its own water goes
                # the wrong way (the Lielupe at 23.85 E, the Daugava at Vecaki): no outflow here, and the course is
                # rerouted through the body to its own mouth
                if nx < 0 and has_sea[b]:
                    path = to_sea(lab, b, near_sea, at(p, l))
                    if path is not None:
                        stats["wrong_way"] += 1
                        ci, cj = at(p, l)
                        if mr <= ci < mr + hp_t and mc <= cj < mc + wp_t:   # (the tile's own: each once)
                            reroutes.append((int(r), int(P["sid"][l]), [(top - (i + 0.5) / hp_t, left + (j + 0.5) / wp_t) for i, j in path]))
                        continue
                fixed[at(p, l)] = True
                stats["outflows"] += 1
    # bodies with inflow but no outflow drain at their most downstream course pixel (the largest potential would be
    # undefined): their pixel with the smallest inflow-weighted distance is not known, so the farthest from the inflows
    has_src = np.bincount(lab.ravel(), weights=(src > 0).ravel(), minlength=nb + 1) > 0
    has_fix = np.bincount(lab.ravel(), weights=fixed.ravel(), minlength=nb + 1) > 0
    for bdy in np.flatnonzero(has_src & ~has_fix):
        sel = lab == bdy
        sd = ndi.distance_transform_edt(~(src > 0) | ~sel)
        sd[~sel] = -1
        i, j = np.unravel_index(np.argmax(sd), sd.shape)
        fixed[i, j] = True
    if os.environ.get("FLOW_DEBUG_POINTS"):
        big = np.argsort(-np.bincount(lab.ravel(), minlength=nb + 1))[:3]
        for bdy in big:
            if bdy == 0:
                continue
            ii, jj = np.nonzero((src > 0.5) & (lab == bdy))
            print(f"  {name[22:39]} body {bdy} inflows: " + "; ".join(f"{top - (i + .5) / hp_t:.4f},{left + (j + .5) / wp_t:.4f} {src[i, j]:.1f}" for i, j in zip(ii, jj)), flush=True)
            ii, jj = np.nonzero(fixed & (lab == bdy) & ~near_sea)
            print(f"  {name[22:39]} body {bdy} outflows (not the sea): " + "; ".join(f"{top - (i + .5) / hp_t:.4f},{left + (j + .5) / wp_t:.4f}" for i, j in zip(ii, jj)), flush=True)
            print(f"  {name[22:39]} body {bdy}: {int((near_sea & (lab == bdy)).sum())} pixels by the sea", flush=True)
    if os.environ.get("FLOW_DEBUG"):
        npx_b = np.bincount(lab.ravel(), minlength=nb + 1)
        src_b = np.bincount(lab.ravel(), weights=src.ravel(), minlength=nb + 1)
        fix_b = np.bincount(lab.ravel(), weights=fixed.ravel(), minlength=nb + 1)
        sea_b = np.bincount(lab.ravel(), weights=near_sea.ravel(), minlength=nb + 1)
        for bdy in np.argsort(-npx_b)[:8]:
            if bdy == 0:
                continue
            rr, cc = np.nonzero(lab == bdy)
            print(f"  {name[22:39]} body {bdy}: {npx_b[bdy]} px around {lat0 + 1 - rr.mean() / hp:.3f},{lon0 + cc.mean() / wp:.3f}; "
                  f"inflow {src_b[bdy]:.1f} m3/s, fixed {int(fix_b[bdy])} px ({int(sea_b[bdy])} by the sea), largest course Q {body_q[bdy]:.1f}", flush=True)
    active = has_src[lab] & water
    speed = np.zeros((hp, wp), np.uint8)
    dirb = np.zeros((hp, wp), np.uint8)
    n_active = int(active.sum())
    if n_active:
        idx = -np.ones((hp, wp), np.int64)
        free = active & ~fixed
        idx[free] = np.arange(int(free.sum()))
        n = int(free.sum())
        rows, cols, vals = [], [], []
        diag = np.zeros(n)
        rhs = src[free].copy()
        # 4-neighbour conductances: harmonic mean of the two pixels, scaled by the edge's length over the step
        for (di, dj, scale) in ((0, 1, dy / dx), (1, 0, dx / dy)):
            a = active[:hp - di, :wp - dj] & active[di:, dj:]
            ta, tb = T[:hp - di, :wp - dj], T[di:, dj:]
            tc = np.where(a, 2 * ta * tb / np.maximum(ta + tb, 1e-12), 0) * scale
            ii, jj = np.nonzero(a)
            i2, j2 = ii + di, jj + dj
            c = tc[ii, jj]
            u, v = idx[ii, jj], idx[i2, j2]
            for (p_, q_) in ((u, v), (v, u)):
                m_ = p_ >= 0
                np.add.at(diag, p_[m_], c[m_])
                m2 = m_ & (q_ >= 0)
                rows.append(p_[m2]); cols.append(q_[m2]); vals.append(-c[m2])
        if n:
            A = coo_matrix((np.concatenate(vals + [diag]), (np.concatenate(rows + [np.arange(n)]), np.concatenate(cols + [np.arange(n)]))),
                           shape=(n, n)).tocsr()
            phi_free = spsolve(A, rhs)
        phi = np.zeros((hp, wp))
        if n:
            phi[free] = phi_free
        # flux per metre of width: the potential's fall times the conductance, over the pixel's edge length
        gx = np.zeros((hp, wp)); gy = np.zeros((hp, wp))
        a = active[:, :-1] & active[:, 1:]
        f = np.where(a, (phi[:, :-1] - phi[:, 1:]) * 2 * T[:, :-1] * T[:, 1:] / np.maximum(T[:, :-1] + T[:, 1:], 1e-12) * (dy / dx), 0)
        gx[:, :-1] += f / 2; gx[:, 1:] += f / 2
        a = active[:-1, :] & active[1:, :]
        f = np.where(a, (phi[:-1, :] - phi[1:, :]) * 2 * T[:-1, :] * T[1:, :] / np.maximum(T[:-1, :] + T[1:, :], 1e-12) * (dx / dy), 0)
        gy[:-1, :] += f / 2; gy[1:, :] += f / 2
        # gx: flow east (m3/s across a north-south pixel edge), gy: flow south (across an east-west edge)
        qx, qy = gx / dy, -gy / dx                 # per metre of width; qy north
        qm = np.hypot(qx, qy)
        bq = body_q[lab]
        d_m = np.where(bq > 0, DEPTH_C * np.maximum(bq, 1.0) ** DEPTH_F, 1.0)
        v = np.where(active, qm / d_m, 0) * 100     # cm/s
        speed[active] = np.clip(np.round(v[active]), 0, SPEED_CAP).astype(np.uint8)
        if os.environ.get("FLOW_DEBUG"):
            for bdy in np.argsort(-np.bincount(lab.ravel(), minlength=nb + 1))[:4]:
                if bdy == 0:
                    continue
                sel = lab == bdy
                vv = v[sel]
                print(f"  {name[22:39]} body {bdy}: speed cm/s p10 {np.percentile(vv, 10):.1f} p50 {np.percentile(vv, 50):.1f} "
                      f"p90 {np.percentile(vv, 90):.1f} max {vv.max():.1f}; phi max {phi[sel].max():.3g}; free {int((sel & free).sum())}", flush=True)
        ang = np.arctan2(qy, qx)
        dirb[active] = (np.round(ang[active] / (2 * math.pi) * 256) % 256).astype(np.uint8)
        stats["active_px"] = n_active
        stats["still_px"] = int((water & ~active).sum())
    if os.environ.get("FLOW_DEBUG_DIR"):
        np.savez_compressed(os.path.join(os.environ["FLOW_DEBUG_DIR"], name.replace("_WBM.tif", ".npz")),
                            water=water[mr:mr + hp_t, mc:mc + wp_t], src=src[mr:mr + hp_t, mc:mc + wp_t],
                            fixed=fixed[mr:mr + hp_t, mc:mc + wp_t], speed=speed[mr:mr + hp_t, mc:mc + wp_t], lab=lab[mr:mr + hp_t, mc:mc + wp_t])
    speed, dirb = speed[mr:mr + hp_t, mc:mc + wp_t], dirb[mr:mr + hp_t, mc:mc + wp_t]
    n_active = int((speed > 0).sum())
    blob = brotli.compress(struct.pack("<ii", hp_t, wp_t) + speed.tobytes() + dirb.tobytes(), quality=9, lgwin=22) if n_active else None
    return {"name": name, "lat0": lat0, "lon0": lon0, "blob": blob, "stats": stats, "reroutes": reroutes}


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    pos, opt = [], {}
    args = sys.argv[1:]
    while args:
        a = args.pop(0)
        if a.startswith("--"):
            opt[a[2:]] = args.pop(0)
        else:
            pos.append(a)
    out, courses = pos[0], pos[1]
    os.makedirs(out, exist_ok=True)
    workers = int(opt.get("workers", 16))
    filt = opt.get("tiles", "")
    tiles = sorted(p for p in glob.glob(os.path.join(bro.WBM, "*_WBM.tif"))
                   if not filt or any(f"_{f[0]}{f[1:3]}_00_{f[3]}{f[4:]}_00_" in os.path.basename(p) for f in filt.split(",")))
    key = int(opt["key"], 16) if "key" in opt else None
    cnpz = course_arrays(courses, out, key)
    key = int(np.load(cnpz)["key"][0])
    log(f"{len(tiles)} tiles, {workers} workers; courses key {key:016x}")
    TX, TY = 360, 180
    states = np.full(TX * TY, 255, np.uint8)
    blobs, tot, reroutes = {}, {}, []
    cache = os.path.join(out, "masks")
    os.makedirs(cache, exist_ok=True)
    with Pool(workers, initializer=init, initargs=(opt["reservoirs"], opt["corridors"], cnpz, opt.get("canals"), cache)) as pool:
        names = []
        for i, n_ in enumerate(pool.imap_unordered(prep, tiles, chunksize=4), 1):
            if i % 2000 == 0:
                log(f"pass 1: {i}/{len(tiles)} tiles")
            if n_:
                names.append(n_)
        log(f"pass 1: {len(names)} tiles with river water")
        if filt:
            # a test: only the named tiles' flow (their neighbours' masks are in the cache when they were named too)
            pass
        for i, r in enumerate(pool.imap_unordered(tile, sorted(names), chunksize=2), 1):
            if i % 1000 == 0:
                log(f"{i}/{len(tiles)} tiles")
            if r is None:
                continue
            t = (r["lat0"] + 90) * TX + (r["lon0"] + 180)
            states[t] = 1 if r["blob"] else 0
            if r["blob"]:
                blobs[t] = r["blob"]
            for k, v in r["stats"].items():
                tot[k] = tot.get(k, 0) + v
            reroutes += r.get("reroutes", [])
    hsize = struct.calcsize("<4siiiiQi")
    index = bytearray()
    o = hsize + TX * TY + 8 * len(blobs)
    body = bytearray()
    for t in sorted(blobs):
        index += struct.pack("<ii", o + len(body), len(blobs[t]))
        body += blobs[t]
    # the reroutes: per reach (sorted), its vertex the new end starts after and the new end's points
    start = np.load(cnpz)["start"]
    trailer = bytearray(struct.pack("<i", len(reroutes)))
    for r, sid, pts in sorted(reroutes):
        trailer += struct.pack("<iii", r, sid - int(start[r]), len(pts))
        trailer += np.asarray(pts, "<f4").tobytes()
    trailer_at = o + len(body)
    head = struct.pack("<4siiiiQi", b"EMFL", VERSION, 1, TX, TY, key, trailer_at)
    data = head + states.tobytes() + bytes(index) + bytes(body) + bytes(trailer)
    for r, sid, pts in sorted(reroutes):
        log(f"reroute reach {r} after vertex {sid - int(start[r])}: {len(pts)} points to {pts[-1][0]:.4f},{pts[-1][1]:.4f}")
    fname = f"rivers-flow-glo90-{VERSION}.bin"
    with open(os.path.join(out, fname), "wb") as f:
        f.write(data)
    log(f"{fname}: {len(data):,} bytes, sha256 {hashlib.sha256(data).hexdigest()}, {len(blobs)} tiles with flow; {tot}")


if __name__ == "__main__":
    main()
