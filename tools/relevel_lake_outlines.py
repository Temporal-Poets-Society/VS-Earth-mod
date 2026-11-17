"""Bake lakes-glo90-4.bin (EA-0150, lake outlines rules 4): lakes-glo90-3.bin's lakes cut into their water bodies, each
at its own level, and the reservoirs among them left out.

Why: tools/bake_lake_outlines.py labels the water mask's lake cells 8-connected and joins the labels across the 1° tiles'
edges, so water at different levels that the mask happens to connect (a lake, the river below it where the mask has it
as lake, a reservoir further down) is one lake: drawn at the level of its largest piece, and judged as one. The Pielisjoki's
Kuurna reservoir (HydroLAKES 12260, 25 km², 82.5 m in GLO-90) is joined through the river to Pielinen (976 km² in all):
too small a share of that to be left out as a reservoir, it was drawn at Pielinen's 92.5 m, 10 m above its own water, and
the game held it there with rims, the river running below in a walled trench (EA-0150, seen at 1:50, 1024 high). Worldwide
about 2,200 pieces (1,100 km²) were drawn 3 m or more above their own water and 11,600 (1,700 km²) 3 m or more below it:
Cross Lake on the Nelson (206 m) at Lake Winnipeg's 215.5 m, among them.

How: GLO-90 flattens each water body to one height (half metres). Each tile's drawn lake labels (version 2's, recovered from
its rings as tools/recontour_lake_outlines.py does) are cut into bodies where a label holds two or more flats (heights of
FLAT_MIN cells or more) SPLIT_M or more apart: every cell of the label goes to the flat nearest it, and each flat's
8-connected parts are bodies. An uncut label is one body. The bodies are joined across the tiles' edges as the bake joins
its labels, except between two flat bodies (one height on FLAT_SHARE of the cells or more) SPLIT_M or more apart: an
unflattened lake's pieces either side of a whole degree, whose medians differ, stay one lake (no new seam). A lake whose
bodies were not cut and whose links all stand is version 2's lake, with its level and flags. Any other is judged again as
the bake judges a lake, on what the bake's inputs left here: its level the median GLO-90 height of its largest body; left
out as a reservoir where a HydroLAKES or Global Dam Watch reservoir's dam is at one of its bodies (the bake's box) and that
reservoir is at least RES_SHARE of the lake's area, or RES_SHARE or more of it lies in a Natural Earth reservoir; raised where
such a lake control is; salt and missing as its largest body's lake was. (The bake's other tests, Earth's sea, its kept
salt and terminal lakes, glaciers and the Natural Earth lakes its grid dropped as reservoirs, decided the whole lake that
was drawn; its parts are not judged by them again.)

Tiles whose lakes and neighbours' facing lakes are as before keep version 3's bytes. The others are outlined as version 3
outlines a tile (one cell of the neighbours' lakes around it, clipped to the square), one level at a time: a body's margin
is the neighbours' facing cells of the same lake level and flags, so two bodies at different levels meet where their cells
do and each keeps its own shore there.

Usage: python -I tools/relevel_lake_outlines.py V2_FILE V3_FILE OUTDIR [workers] [tile filters, e.g. N62_00_E029,N54_00_W098]
Needs EARTH_RAW (or rawdata/) with copernicus-glo90, copernicus-glo90-wbm, hydrolakes, global-dam-watch and ne_lakes.
Writes OUTDIR/lakes-glo90-4.bin, lakes-glo90-4.json and report.txt; OUTDIR/work holds scratch (delete it after).
"""
import hashlib, json, os, struct, sys, time
from multiprocessing import Pool

import brotli
import numpy as np
import tifffile
from scipy import ndimage as ndi
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bake_lake_outlines as bl  # noqa: E402
import recontour_lake_outlines as rc  # noqa: E402

VERSION = 4
FLAT_MIN = 30          # cells: a height a label holds this many cells of is one of its flats (about 0.1-0.3 km²)
SPLIT_M = 2.0          # m: flats this far apart are separate water bodies (a label is cut; no link across an edge)
FLAT_SHARE = 0.5       # a body is flat (GLO-90 flattened it) when one height covers this share of its cells
RES_SHARE = 0.3        # the bake's: a reservoir at least this share of a lake's area makes it one (a lake control: raised)
NERES_SHARE = 0.3      # the bake's: this share of a lake in Natural Earth reservoirs makes it one
T0 = time.time()
A = {}


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


def dem_path(lat, lon):
    return rc.wbm_path(lat, lon).replace(bl.WBM, bl.DEM).replace("_WBM.tif", "_DEM.tif")


# ------------------------------------------------------------------------------------------- inputs

def prepare(out):
    """The dams (HydroLAKES reservoirs and lake controls, Global Dam Watch's reservoirs it lacks: lon, lat, km², type) and
    Natural Earth's reservoirs at 1', as the bake makes them."""
    work = os.path.join(out, "work")
    p = os.path.join(work, "dams.npy")
    if not os.path.exists(p):
        os.environ["EARTH_GDW"] = "1"
        from reservoir_list import reservoirs
        np.save(p, reservoirs(bl.RAW, (2, 3), -90.0, log)[:, [1, 2, 3, 5]])
    p = os.path.join(work, "neres1m.npy")
    if not os.path.exists(p):
        import shapefile
        from PIL import Image, ImageDraw
        img = Image.new("1", (360 * 60, 180 * 60), 0)
        dr = ImageDraw.Draw(img)
        n = 0
        for f in ("ne_10m_lakes", "ne_10m_lakes_europe", "ne_10m_lakes_north_america"):
            r = shapefile.Reader(os.path.join(bl.RAW, "ne_lakes", f), encoding="utf-8")
            for sr in r.iterShapeRecords():
                if (sr.record.as_dict().get("featurecla") or "") != "Reservoir" or not sr.shape.points:
                    continue
                parts = list(sr.shape.parts) + [len(sr.shape.points)]
                for i in range(len(parts) - 1):
                    pts = sr.shape.points[parts[i]:parts[i + 1]]
                    if len(pts) >= 3:
                        dr.polygon([((x + 180) * 60, (90 - y) * 60) for x, y in pts], fill=1)
                n += 1
        np.save(p, np.array(img, bool))
        log(f"Natural Earth reservoirs: {n}")


def init(v2_path, v3_path, out):
    rc.init(v2_path, os.path.join(out, "work"))
    b3, _, states3, idx3 = rc.read_index(v3_path)
    A["v3"], A["idx3"], A["out"] = b3, idx3, out
    A["neres"] = np.load(os.path.join(out, "work", "neres1m.npy"), mmap_mode="r")


def work_path(t, what):
    return os.path.join(A["out"], "work", f"{t}-{what}.npy")


# ------------------------------------------------------------------------------------------- pass A: bodies

BODY_FIELDS = ("label", "level", "flat", "npx", "km2", "latS", "lonW", "latN", "lonE", "neres", "lv2", "fl2", "split")


def body_stats(body, k, dem, lat, lon, neres_px):
    """Per body id 0..k: the stats in BODY_FIELDS' order (label, lv2, fl2 and split are filled by the caller)."""
    hp, wp = body.shape
    m = body > 0
    B = body[m]
    H = dem[m]
    rr, cc = np.nonzero(m)
    npx = np.bincount(B, minlength=k + 1)
    rows_lat = lat + 1 - (rr + 0.5) / hp
    km2 = np.bincount(B, weights=(111.32 / hp) * (111.32 / wp) * np.cos(np.radians(rows_lat)), minlength=k + 1)
    # the bake's level: the median of the GLO-90 heights (the element at npx // 2 of each body's sorted heights)
    order = np.lexsort((H, B))
    Hs, Bs = H[order], B[order]
    starts = np.searchsorted(Bs, np.arange(k + 1))
    level = np.zeros(k + 1)
    has = npx > 0
    level[has] = Hs[starts[has] + npx[has] // 2]
    # flat: one height (0.1 m) on FLAT_SHARE of the cells or more
    key = Bs.astype(np.int64) * (1 << 32) + (np.round(Hs * 10).astype(np.int64) + (1 << 30))
    u, c = np.unique(key, return_counts=True)
    mode = np.zeros(k + 1, np.int64)
    np.maximum.at(mode, (u >> 32).astype(np.int64), c)
    flat = mode >= FLAT_SHARE * np.maximum(npx, 1)
    lat_s = np.full(k + 1, np.inf); lat_n = np.full(k + 1, -np.inf); lon_w = np.full(k + 1, np.inf); lon_e = np.full(k + 1, -np.inf)
    np.minimum.at(lat_s, B, lat + 1 - (rr + 1) / hp)
    np.maximum.at(lat_n, B, lat + 1 - rr / hp)
    np.minimum.at(lon_w, B, lon + cc / wp)
    np.maximum.at(lon_e, B, lon + (cc + 1) / wp)
    ner = np.bincount(B, weights=neres_px[m].astype(np.float64), minlength=k + 1) / np.maximum(npx, 1)
    z = np.zeros(k + 1)
    return [z.copy(), level, flat.astype(np.float64), npx.astype(np.float64), km2, lat_s, lon_w, lat_n, lon_e, ner, z.copy(), z.copy(), z.copy()]


def split_label(lab, l, dem, flats, next_id, body):
    """Cuts label l into bodies, one per 8-connected part of the cells nearest each flat (heights in decimetres, largest
    first); the largest part of the largest flat keeps id l. Returns the next free id."""
    sl = ndi.find_objects((lab == l).astype(np.int8))[0]
    sub = lab[sl] == l
    hd = np.round(dem[sl] * 10).astype(np.int64)
    seed = np.zeros(sub.shape, np.int32)
    for i, f in enumerate(flats, 1):
        seed[sub & (hd == f)] = i
    _, ind = ndi.distance_transform_edt(seed == 0, return_indices=True)
    near = seed[ind[0], ind[1]]
    near[~sub] = 0
    parts = []
    for i in range(1, len(flats) + 1):
        pl, n = ndi.label(near == i, structure=np.ones((3, 3), bool))
        if n:
            sizes = np.bincount(pl.ravel(), minlength=n + 1)
            for j in range(1, n + 1):
                parts.append((i, -int(sizes[j]), pl == j))
    parts.sort(key=lambda p: (p[0], p[1]))
    out = body[sl]
    for idx, (_, _, mask) in enumerate(parts):
        if idx == 0:
            out[mask] = l
        else:
            out[mask] = next_id
            next_id += 1
    body[sl] = out
    return next_id


def pass_a(t):
    """A tile's bodies: (tile, stats as an array (fields x ids), borders, labels cut), or (tile, None, ...) without lakes."""
    lat, lon = t // 360 - 90, t % 360 - 180
    d = rc.drawn(lat, lon)
    if d is None:
        return t, None, None, 0
    keep, lab, owner, _ = d
    hp, wp = lab.shape
    dem = tifffile.imread(dem_path(lat, lon)).astype(np.float64)
    k = len(owner) - 1
    body = np.where(keep, lab, 0).astype(np.int32)
    # the labels' flats
    m = keep
    L = lab[m].astype(np.int64)
    hd = np.round(dem[m] * 10).astype(np.int64)
    u, c = np.unique(L * (1 << 32) + (hd + (1 << 30)), return_counts=True)
    ul, uh = u >> 32, (u & ((1 << 32) - 1)) - (1 << 30)
    big = c >= FLAT_MIN
    cut = []
    next_id = k + 1
    for l in np.unique(ul[big]):
        s = big & (ul == l)
        if s.sum() < 2 or (uh[s].max() - uh[s].min()) < SPLIT_M * 10:
            continue
        flats = uh[s][np.argsort(-c[s], kind="stable")]
        next_id = split_label(lab, int(l), dem, flats.tolist(), next_id, body)
        cut.append(int(l))
    nb = next_id - 1
    neres_px = bl.sample(A["neres"], 60, lat, lon, hp, wp)
    st = body_stats(body, nb, dem, lat, lon, neres_px)
    # each body's label and version 2's lake (level, flags) for it
    labels = np.zeros(nb + 1, np.int64)
    np.maximum.at(labels, body[keep], lab[keep])
    st[0] = labels.astype(np.float64)
    for b in range(1, nb + 1):
        o = owner[labels[b]] if labels[b] > 0 else None
        if o is not None:
            st[10][b], st[11][b] = o
    for l in cut:
        st[12][labels == l] = 1
    st = np.array(st)
    if cut:
        np.save(work_path(t, "body"), body)
    br = {"top": body[0].copy(), "bottom": body[-1].copy(), "left": body[:, 0].copy(), "right": body[:, -1].copy(), "w": wp, "h": hp}
    return t, st, br, len(cut)


# ------------------------------------------------------------------------------------------- the join

def join(stats, borders):
    """Groups of bodies across the tiles' edges: (global offsets, group per global id, links cut)."""
    tiles = sorted(stats)
    offs, o = {}, 0
    for t in tiles:
        offs[t] = o
        o += stats[t].shape[1]
    N = o
    level = np.concatenate([stats[t][1] for t in tiles])
    flat = np.concatenate([stats[t][2] for t in tiles]) > 0
    a_, b_ = [], []

    def link(ta, la, tb, lb):
        mk = (la > 0) & (lb > 0)
        a_.append(offs[ta] + la[mk].astype(np.int64)); b_.append(offs[tb] + lb[mk].astype(np.int64))
    for t in tiles:
        lat, lon = t // 360 - 90, t % 360 - 180
        e = borders[t]
        east = rc.tile_key(lat, (lon + 1 + 180) % 360 - 180)
        if east in borders:
            r, lft = e["right"], borders[east]["left"]
            ri = np.nonzero(r)[0]
            for dd in (-1, 0, 1):
                link(t, r[ri], east, lft[np.clip(ri + dd, 0, len(lft) - 1)])
        north = rc.tile_key(lat + 1, lon) if lat < 89 else None
        if north in borders:
            top, bot = e["top"], borders[north]["bottom"]
            wt, wn = len(top), len(bot)
            ti, bi = np.nonzero(top)[0], np.nonzero(bot)[0]
            for dd in (-1, 0, 1):
                link(t, top[ti], north, bot[np.clip(((ti + 0.5) * wn / wt).astype(int) + dd, 0, wn - 1)])
                link(t, top[np.clip(((bi + 0.5) * wt / wn).astype(int) + dd, 0, wt - 1)], north, bot[bi])
    a = np.concatenate(a_) if a_ else np.zeros(0, np.int64)
    b = np.concatenate(b_) if b_ else np.zeros(0, np.int64)
    if len(a):
        ab = np.unique(np.column_stack([np.minimum(a, b), np.maximum(a, b)]), axis=0)
        a, b = ab[:, 0], ab[:, 1]
    cutl = flat[a] & flat[b] & (np.abs(level[a] - level[b]) >= SPLIT_M)
    ka, kb = a[~cutl], b[~cutl]
    _, comp = connected_components(coo_matrix((np.ones(len(ka), np.int8), (ka, kb)), shape=(N, N)), directed=False)
    return offs, comp, np.column_stack([a[cutl], b[cutl]])


def judge(stats, offs, comp, cuts, dams):
    """Per global body id: the new (level dm, flags) or None (left out), and the changed lakes' report rows."""
    tiles = sorted(stats)
    S = np.concatenate([stats[t] for t in tiles], axis=1)
    F = {f: S[i] for i, f in enumerate(BODY_FIELDS)}
    N = S.shape[1]
    tile_of = np.concatenate([np.full(stats[t].shape[1], t) for t in tiles])
    real = F["npx"] > 0
    nc = comp.max() + 1
    changed = np.zeros(nc, bool)
    changed[comp[(F["split"] > 0) & real]] = True
    if len(cuts):
        changed[comp[cuts[:, 0]]] = True
        changed[comp[cuts[:, 1]]] = True
    km2 = np.bincount(comp, weights=np.where(real, F["km2"], 0), minlength=nc)
    # the largest body of each lake
    order = np.lexsort((-np.where(real, F["km2"], -1), comp))
    first = np.ones(N, bool)
    first[1:] = comp[order][1:] != comp[order][:-1]
    largest = np.full(nc, -1, np.int64)
    largest[comp[order][first]] = order[first]
    # the dams at each body (the bake's box: its cells' box plus 3 cells and a twentieth of its own size)
    d2, d3 = np.zeros(N), np.zeros(N)
    cand = np.nonzero(real & changed[comp])[0]
    if len(cand):
        hp = 1200.0
        mid = 0.5 * (F["latS"][cand] + F["latN"][cand])
        coslat = np.maximum(np.cos(np.radians(np.floor(mid) + 0.5)), 0.05)
        sq = np.sqrt(np.maximum(F["km2"][cand], 0))
        for j, i in enumerate(cand):
            t = int(tile_of[i])
            w = A["widths"].get(t, 1200)
            mm = 3.0 / hp + 0.05 * sq[j] / 111.32
            mk = 3.0 / w + 0.05 * sq[j] / 111.32 / coslat[j]
            near = (dams[:, 1] >= F["latS"][i] - mm) & (dams[:, 1] <= F["latN"][i] + mm) & (dams[:, 0] >= F["lonW"][i] - mk) & (dams[:, 0] <= F["lonE"][i] + mk)
            for dx, dy, da, dt in dams[near]:
                if dt == 2:
                    d2[i] = max(d2[i], da)
                else:
                    d3[i] = max(d3[i], da)
    D2 = np.zeros(nc); D3 = np.zeros(nc)
    np.maximum.at(D2, comp, d2)
    np.maximum.at(D3, comp, d3)
    ner = np.bincount(comp, weights=np.where(real, F["neres"] * F["km2"], 0), minlength=nc) / np.maximum(km2, 1e-12)
    lv = np.round(F["level"][np.maximum(largest, 0)] * 10).astype(np.int64)
    fl_big = F["fl2"][np.maximum(largest, 0)].astype(np.int64)
    flags = (fl_big & (bl.FLAG_SALT | bl.FLAG_MISSING)) | np.where(D3 >= RES_SHARE * km2, bl.FLAG_RAISED, 0)
    out_res = (D2 >= RES_SHARE * km2) | (ner >= NERES_SHARE)
    new = {}
    for i in np.nonzero(real)[0]:
        c = comp[i]
        if not changed[c]:
            new[i] = (int(F["lv2"][i]), int(F["fl2"][i]))
        elif out_res[c]:
            new[i] = None
        else:
            new[i] = (int(lv[c]), int(flags[c]))
    rows = []
    chg = np.nonzero(changed)[0]
    sel = np.nonzero(real & changed[comp])[0]
    srt = sel[np.argsort(comp[sel], kind="stable")]
    lo_ = np.searchsorted(comp[srt], chg, "left")
    hi_ = np.searchsorted(comp[srt], chg, "right")
    for c, a0, a1 in zip(chg, lo_, hi_):
        mem = srt[a0:a1]
        i = largest[c]
        old = sorted(set((int(F["lv2"][j]), int(F["fl2"][j])) for j in mem))
        rows.append({"km2": float(km2[c]), "lat": float(0.5 * (F["latS"][i] + F["latN"][i])), "lon": float(0.5 * (F["lonW"][i] + F["lonE"][i])),
                     "bodies": int(len(mem)), "old": old, "new": None if out_res[c] else [int(lv[c]), int(flags[c])],
                     "reservoir_km2": float(D2[c]), "control_km2": float(D3[c]), "neres": float(ner[c]),
                     "levels": sorted(set(round(float(F["level"][j]), 1) for j in mem))[:8]})
    return new, rows, F, tile_of


# ------------------------------------------------------------------------------------------- pass B: outlines

def code(o):
    return 0 if o is None else (o[0] << 8) | (o[1] + 1)


def pass_b(job):
    """A tile outlined again: its lakes as version 3 outlines them (one contour, one cell of the neighbours' lakes around
    it), except the levels in `special` (a cut label's bodies, a lake meeting another level across the tile's edge), each
    outlined on its own with the neighbours' cells of its own level and flags: (tile, lakes, bytes or None for version 3's
    own, vertices, raw bytes, changed)."""
    t, owners, margins, special = job
    lat, lon = t // 360 - 90, t % 360 - 180
    p = work_path(t, "body")
    if os.path.exists(p):
        body = np.load(p)
    else:
        d = rc.drawn(lat, lon)
        keep, lab = d[0], d[1]
        body = np.where(keep, lab, 0).astype(np.int32)
    own = [None] * (len(owners))
    for i, o in enumerate(owners):
        own[i] = None if o == 0 else (o >> 8, (o & 0xFF) - 1)
    codes = np.array(owners, np.int64)
    bc = codes[body]
    sp = np.array(sorted(special), np.int64)
    keep_n = (bc > 0) & ~np.isin(bc, sp)
    mg = {s: (margins[s] > 0).astype(np.float32) for s in ("top", "bottom", "left", "right")}
    mg["corners"] = tuple(float(c > 0) for c in margins["corners"])
    mn = mg if (any(mg[s].any() for s in ("top", "bottom", "left", "right")) or any(mg["corners"])) else None
    ow = [o if (o is not None and code(o) not in special) else None for o in own]
    recs, _, _ = rc.outline(keep_n, body, ow, mn)
    for v in sp:
        keep_v = bc == v
        mg = {s: (margins[s] == v).astype(np.float32) for s in ("top", "bottom", "left", "right")}
        mg["corners"] = tuple(float(c == v) for c in margins["corners"])
        mv = mg if (any(mg[s].any() for s in ("top", "bottom", "left", "right")) or any(mg["corners"])) else None
        ow = [o if (o is not None and code(o) == v) else None for o in own]
        r, _, _ = rc.outline(keep_v, body, ow, mv)
        recs += r
    if t in A["idx3"]:
        o3, n3 = A["idx3"][t]
        old = rc.decode(A["v3"][o3:o3 + n3])
        if rc.canon(recs) == rc.canon(old):
            return t, -1, None, 0, 0, False
    raw = rc.raw_bytes(recs)
    nv = sum(len(r) for _, _, rings in recs for r in rings)
    return t, len(recs), brotli.compress(raw, quality=11, lgwin=22) if recs else b"", nv, len(raw), True


def init_b(v2_path, v3_path, out):
    init(v2_path, v3_path, out)
    rc.W["outer_sign"] = rc.outer_sign()


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    v2, v3, out = sys.argv[1], sys.argv[2], sys.argv[3]
    workers = int(sys.argv[4]) if len(sys.argv) > 4 else 8
    filt = sys.argv[5] if len(sys.argv) > 5 else ""
    os.makedirs(os.path.join(out, "work"), exist_ok=True)
    prepare(out)
    dams = np.load(os.path.join(out, "work", "dams.npy"))
    b2, _, states2, idx2 = rc.read_index(v2)
    b3, ver3, states3, idx3 = rc.read_index(v3)
    assert ver3 == 3 and set(idx2) == set(idx3), "version 3 must be version 2's lakes outlined again"
    log(f"{v2} and {v3}: {len(idx3)} tiles with lakes")
    tiles = sorted(idx2)
    if filt:
        want = sorted(t for t in idx2 if any(f in os.path.basename(rc.wbm_path(t // 360 - 90, t % 360 - 180)) for f in filt.split(",")))
        ring = set()
        for t in want:
            lat, lon = t // 360 - 90, t % 360 - 180
            for dr in (-2, -1, 0, 1, 2):
                for dc in (-2, -1, 0, 1, 2):
                    if -90 <= lat + dr <= 89:
                        ring.add(rc.tile_key(lat + dr, (lon + dc + 180) % 360 - 180))
        tiles = sorted(t for t in ring if t in idx2)
    stats, borders, cut_labels = {}, {}, 0
    with Pool(workers, initializer=init, initargs=(v2, v3, out)) as pool:
        for i, (t, st, br, nc) in enumerate(pool.imap_unordered(pass_a, tiles, chunksize=4), 1):
            if st is not None:
                stats[t], borders[t] = st, br
                cut_labels += nc
            if i % 1000 == 0:
                log(f"pass A: {i}/{len(tiles)} tiles, {cut_labels} labels cut")
    log(f"pass A: {len(stats)} tiles, {cut_labels} labels cut into water bodies")
    A["widths"] = {t: borders[t]["w"] for t in borders}
    offs, comp, cuts = join(stats, borders)
    log(f"join: {comp.max() + 1} lakes, {len(cuts)} links between flat bodies {SPLIT_M} m or more apart not made")
    new, rows, F, tile_of = judge(stats, offs, comp, cuts, dams)
    # per tile: owner codes per body id, and whether any differs from version 2's
    owners, changed_t = {}, set()
    for t in stats:
        n = stats[t].shape[1]
        oc = [0] * n
        for b in range(1, n):
            g = offs[t] + b
            if g in new:
                oc[b] = code(new[g])
                if new[g] != (int(stats[t][10][b]), int(stats[t][11][b])) or stats[t][12][b] > 0:
                    changed_t.add(t)
        owners[t] = oc
    redo = set()
    for t in changed_t:
        lat, lon = t // 360 - 90, t % 360 - 180
        for dr in (-1, 0, 1):
            for dc in (-1, 0, 1):
                if -90 <= lat + dr <= 89:
                    n = rc.tile_key(lat + dr, (lon + dc + 180) % 360 - 180)
                    if n in stats:
                        redo.add(n)

    def side(t, s, n):
        if t not in borders:
            return np.zeros(n, np.int64)
        v = np.array(owners[t], np.int64)[borders[t][s]]
        if len(v) == n:
            return v
        return v[np.minimum(((np.arange(n) + 0.5) * len(v) / n).astype(np.int64), len(v) - 1)]

    def corner(t, which):
        if t not in borders:
            return 0
        row = borders[t]["top" if which[0] == "t" else "bottom"]
        return int(np.array(owners[t], np.int64)[row[0] if which[1] == "l" else row[-1]])

    def specials(t, mg):
        """The tile's lake levels outlined on their own: a cut label's, and any that meets another drawn level across a
        side (within a cell, as the links) or a corner."""
        oc = np.array(owners[t], np.int64)
        sp = set(int(c) for c in np.unique(oc[(stats[t][12] > 0) & (oc > 0)]))
        for s in ("top", "bottom", "left", "right"):
            mine, theirs = oc[borders[t][s]], mg[s][1:-1]
            for dd in (-1, 0, 1):
                th = theirs[np.clip(np.arange(len(mine)) + dd, 0, len(theirs) - 1)]
                sp |= set(int(c) for c in np.unique(mine[(mine > 0) & (th > 0) & (th != mine)]))
        ends = (oc[borders[t]["top"][0]], oc[borders[t]["top"][-1]], oc[borders[t]["bottom"][0]], oc[borders[t]["bottom"][-1]])
        for mine, th in zip(ends, mg["corners"]):
            if mine > 0 and th > 0 and th != mine:
                sp.add(int(mine))
        return sp

    jobs = []
    for t in sorted(redo):
        lat, lon = t // 360 - 90, t % 360 - 180
        hp, wp = borders[t]["h"], borders[t]["w"]
        wl, el = (lon - 1 + 180) % 360 - 180, (lon + 1 + 180) % 360 - 180
        k = lambda la, lo: rc.tile_key(la, lo) if -90 <= la <= 89 else None
        mg = {"top": np.concatenate([[0], side(k(lat + 1, lon), "bottom", wp), [0]]),
              "bottom": np.concatenate([[0], side(k(lat - 1, lon), "top", wp), [0]]),
              "left": np.concatenate([[0], side(rc.tile_key(lat, wl), "right", hp), [0]]),
              "right": np.concatenate([[0], side(rc.tile_key(lat, el), "left", hp), [0]]),
              "corners": (corner(k(lat + 1, wl), "br"), corner(k(lat + 1, el), "bl"), corner(k(lat - 1, wl), "tr"), corner(k(lat - 1, el), "tl"))}
        jobs.append((t, owners[t], mg, specials(t, mg)))
    log(f"pass B: {len(changed_t)} tiles with a changed lake, {len(jobs)} to outline again with their neighbours")
    blobs, new_states = {}, states3.copy()
    nl = nv = nraw = changed = 0
    with Pool(workers, initializer=init_b, initargs=(v2, v3, out)) as pool:
        for i, (t, n, blob, v, raw, ch) in enumerate(pool.imap_unordered(pass_b, jobs, chunksize=2), 1):
            if ch:
                changed += 1
                if n:
                    blobs[t] = blob; nl += n; nv += v; nraw += raw
                else:
                    new_states[t] = 0
            if i % 500 == 0:
                log(f"pass B: {i}/{len(jobs)} tiles, {changed} changed")
    for t in idx3:
        if t not in blobs and new_states[t] == 1:
            o, n = idx3[t]
            blobs[t] = b3[o:o + n]
    head = struct.pack("<4siiiii", b"EMLO", VERSION, bl.UNIT, 1, 360, 180)
    index, body = bytearray(), bytearray()
    o = len(head) + len(new_states) + 8 * len(blobs)
    for t in sorted(blobs):
        index += struct.pack("<ii", o + len(body), len(blobs[t]))
        body += blobs[t]
    data = head + new_states.tobytes() + bytes(index) + bytes(body)
    name = f"lakes-glo90-{VERSION}.bin"
    with open(os.path.join(out, name), "wb") as f:
        f.write(data)
    dropped = [r for r in rows if r["new"] is None]
    relevel = [r for r in rows if r["new"] is not None]
    info = {"file": name, "from": [os.path.basename(v2), os.path.basename(v3)], "from_sha256": hashlib.sha256(b3).hexdigest(),
            "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(), "filter": filt, "tiles": len(stats),
            "labels_cut": cut_labels, "links_not_made": int(len(cuts)), "lakes_changed": len(rows),
            "lakes_left_out_as_reservoirs": len(dropped), "km2_left_out": round(sum(r["km2"] for r in dropped), 1),
            "tiles_changed": changed, "lakes_in_changed_tiles": nl, "vertices_in_changed_tiles": nv,
            "params": {"FLAT_MIN": FLAT_MIN, "SPLIT_M": SPLIT_M, "FLAT_SHARE": FLAT_SHARE, "RES_SHARE": RES_SHARE, "NERES_SHARE": NERES_SHARE}}
    with open(os.path.join(out, f"lakes-glo90-{VERSION}.json"), "w", encoding="utf8") as f:
        json.dump(info, f, indent=1)
    with open(os.path.join(out, "report.txt"), "w", encoding="utf8") as f:
        f.write(json.dumps(info, indent=1) + "\n\nleft out as reservoirs (largest first):\n")
        for r in sorted(dropped, key=lambda r: -r["km2"]):
            f.write(f"  {r['lat']:8.3f} {r['lon']:9.3f} {r['km2']:9.1f} km²  bodies {r['bodies']}  levels {r['levels']}  was {r['old']}  "
                    f"reservoir {r['reservoir_km2']:.1f} km², NE {r['neres']:.2f}\n")
        f.write("\nlevelled again (largest first):\n")
        for r in sorted(relevel, key=lambda r: -r["km2"]):
            f.write(f"  {r['lat']:8.3f} {r['lon']:9.3f} {r['km2']:9.1f} km²  bodies {r['bodies']}  level {r['new'][0] / 10:.1f} m flags {r['new'][1]}  "
                    f"was {r['old']}  levels {r['levels']}\n")
    log(json.dumps(info))


if __name__ == "__main__":
    main()
