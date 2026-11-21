"""Bake lakes-glo90-3.bin (EA-0147): lakes-glo90-2.bin's lakes, levels and flags, outlined again across the 1° tiles'
edges, so that a lake's pieces in neighbouring tiles meet where its shore crosses the edge.

Why: tools/bake_lake_outlines.py contours each tile's mask on its own, padded with land. A lake that crosses a tile edge
then has, on each side, a shore that runs along the edge from the centre of its first water cell there to the centre of
its last, and leaves it at 45°. Where the two facing columns (or rows) differ by a cell, as they do wherever the shore
crosses the edge at a slant, the joined shore steps by a whole cell straight along the edge (a 3" row is 9 blocks at 1:10,
a 4.5"-6" column 14-19 blocks on the ring at 50-62°N); where they agree, both pieces are notched by half a cell. Inside a
tile the same shore crosses a cell boundary diagonally. Seen on 27°E at Tamula (57.84°N) and near Mikkeli (61.75°N): on the
meridian the water changed between two block columns in 255 of 1,226 rows at 1:10, against 11-19 at any cell boundary
inside a tile.

How: each tile's drawn lakes are its mask's lake cells (class 2, 8-connected labels as the bake) whose labels lakes-glo90-2's
rings cover; the old bake run on that mask gives back version 2's tile exactly (checked per tile, counted in the report).
The tile is then contoured with one cell of its eight neighbours' drawn lakes around it (the neighbour's facing row or
column; across the parallels where GLO-90 changes its column spacing, resampled to this tile's columns by cell centre)
and the polygons are clipped to the tile's square. The cells either side of an edge are contoured alike in both tiles,
so the two pieces' cuts end at the same points. Each piece keeps its label's lake (level and flags) from version 2;
the rest is version 2's simplification (half a cell, vertices on the tile's edge kept) and file layout, header version 3.

EA-0148 (decision 0092): on GLO-90's pixel-is-point grid (tools/glo90grid.py; EARTH_GLO90_GRID=cell for the old placement)
cell (r, c) is centred on (lat0 + 1 - r/1200, lon0 + c/cols), not half a cell south-east of it: the contour's padded
coordinate x maps to (x - 1) units of a column and the tile's square is x, y in 1 .. wp + 1, hp + 1 (the south
neighbour's first row and the east neighbour's first column give the strip between this tile's last row or column and
its edge; what reaches over the north and west edges is clipped off: those neighbours draw it). Every tile is outlined
anew and clipped, at OUT_UNIT (4,800) units a degree, where half a cell is a whole number at every column count. Version
2's tiles are still recovered and checked in their own placement. The file is the intermediate lakes-glo90-3p.bin
(header version 3), which tools/relevel_lake_outlines.py turns into lakes-glo90-5.bin.

Usage: python -I tools/recontour_lake_outlines.py OLD_FILE OUTDIR [workers] [tile filters, e.g. N57_00_E02,N61_00_E02]
Needs EARTH_RAW (or rawdata/) with copernicus-glo90-wbm. Writes OUTDIR/lakes-glo90-3.bin (3p.bin), its .json, report.
"""
import glob, hashlib, json, os, struct, sys, time
from multiprocessing import Pool

import brotli
import contourpy
import numpy as np
import shapely
import tifffile
from scipy import ndimage as ndi

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bake_lake_outlines as bl  # noqa: E402  (simplify_ring, lake_bytes, varints, constants)
import glo90grid as gg  # noqa: E402

# the outlines written: on GLO-90's pixel-is-point grid (EA-0148) the tile's sides lie at padded 1 and wp + 1, and units
# are 4,800 a degree; the old placement: 0.5 and wp + 0.5, 2,400 (bake_lake_outlines.UNIT)
EDGE = 1.0 if gg.POINT else 0.5
OUT_UNIT = 4800 if gg.POINT else bl.UNIT

VERSION = 3
T0 = time.time()
W = {}


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


def wbm_path(lat, lon):
    return os.path.join(bl.WBM, f"Copernicus_DSM_COG_30_{'N' if lat >= 0 else 'S'}{abs(lat):02d}_00_"
                                 f"{'E' if lon >= 0 else 'W'}{abs(lon):03d}_00_WBM.tif")


# ------------------------------------------------------------------------------------------- the old file

def read_index(path):
    b = open(path, "rb").read()
    assert b[:4] == b"EMLO"
    version, unit, tile_deg, tx, ty = struct.unpack_from("<5i", b, 4)
    assert unit in (bl.UNIT, OUT_UNIT) and tile_deg == 1 and (tx, ty) == (360, 180), (unit, tile_deg, tx, ty)
    states = np.frombuffer(b, np.uint8, tx * ty, 24).copy()
    idx = {}
    o = 24 + tx * ty
    for t in np.nonzero(states == 1)[0]:
        idx[int(t)] = struct.unpack_from("<2i", b, o)
        o += 8
    return b, version, states, idx


def varint(s, i):
    v = sh = 0
    while True:
        c = s[i]; i += 1
        v |= (c & 0x7F) << sh; sh += 7
        if c < 0x80:
            return v, i


def unzz(v):
    return (v >> 1) ^ -(v & 1)


def decode(blob):
    """A tile's lakes: (level_dm, flags, rings as int arrays of (X, Y) units)."""
    s = brotli.decompress(blob)
    i = 0
    n, i = varint(s, i)
    out = []
    for _ in range(n):
        lv, i = varint(s, i); fl, i = varint(s, i); nr, i = varint(s, i)
        x = y = 0
        rings = []
        for _ in range(nr):
            k, i = varint(s, i)
            pts = np.empty((k, 2), np.int64)
            for j in range(k):
                dx, i = varint(s, i); dy, i = varint(s, i)
                x += unzz(dx); y += unzz(dy)
                pts[j] = (x, y)
            rings.append(pts)
        out.append((unzz(lv), fl, rings))
    return out


def tile_key(lat, lon):
    return (lat + 90) * 360 + (lon + 180)


# ------------------------------------------------------------------------------------------- a tile's drawn lakes

def labels(lat, lon):
    """The tile's mask lakes (class 2) as the bake labels them (8-connected)."""
    wbm = tifffile.imread(wbm_path(lat, lon))
    return ndi.label(wbm == 2, structure=np.ones((3, 3), bool))


def drawn(lat, lon):
    """The tile's drawn lake cells as version 2 has them: (keep mask, labels, per label (level_dm, flags) or None, version
    2's lakes), or None where version 2 has no lakes in this tile."""
    t = tile_key(lat, lon)
    if t not in W["idx"]:
        return None
    o, n = W["idx"][t]
    lakes = decode(W["old"][o:o + n])
    lab, k = labels(lat, lon)
    hp, wp = lab.shape
    rr, cc = np.nonzero(lab)
    labs = lab[rr, cc]
    # in cells: x east from the tile's west edge, y north from its south edge, a cell's centre at (c + 0.5, hp - r - 0.5)
    X = cc + 0.5
    Y = hp - rr - 0.5
    owner = [None] * (k + 1)
    sx, sy = wp / bl.UNIT, hp / bl.UNIT
    for li, (lv, fl, rings) in enumerate(lakes):
        # A label is drawn where a version 2 ring comes within 0.75 cells of one of its cells' centres. Every vertex of
        # its rings is a contour point within half a cell of a centre of its own label (the simplification keeps a subset
        # of them), and other labels' centres are two cells or more from those (8-connected labels): 1.5 cells or more
        # from the vertices and, with the simplification's half cell, a cell or more from the edges. Centres inside the
        # ring are not enough: a lake whose cells meet only at corners comes out of the contour in pieces that the
        # simplification may leave as slivers between centres, or as rectangles through them.
        poly = shapely.Polygon(rings[0] * (sx, sy), [r * (sx, sy) for r in rings[1:]])
        if not poly.is_valid:
            poly = shapely.make_valid(poly)
        x0, y0, x1, y1 = poly.bounds
        s = np.nonzero((X >= x0 - 1) & (X <= x1 + 1) & (Y >= y0 - 1) & (Y <= y1 + 1))[0]
        if not len(s):
            continue
        near = s[shapely.distance(poly, shapely.points(X[s], Y[s])) <= 0.75]
        for l in np.unique(labs[near]):
            if owner[l] is None:
                owner[l] = (lv, fl)
            elif owner[l] != (lv, fl):
                W.setdefault("conflicts", 0)
                W["conflicts"] += 1
    draw = np.array([o is not None for o in owner])
    draw[0] = False
    return draw[lab], lab, owner, lakes


# ------------------------------------------------------------------------------------------- outlines

def outline(keep, lab, owner, margin, old=False, codes=None):
    """The tile's lakes as lake_bytes records: keep contoured with the given one-cell margin around it (None: land, as
    version 2), clipped to the tile's square. Also counts (pieces without a cell, pieces over two labels). old: in
    version 2's placement and units, as bake_lake_outlines.py drew it (pass A's check). codes: the margin's lakes as
    relevel_lake_outlines.code gives them (its layout): on the pixel-is-point grid the south neighbour's first row and the
    east neighbour's first column lie on the tile's edges, and their water reaches half a cell into the tile; a piece
    with no cell of the tile's own is that lake's."""
    hp, wp = keep.shape
    edge, unit = (0.5, bl.UNIT) if old else (EDGE, OUT_UNIT)
    if margin is None and edge != 0.5:
        # on the pixel-is-point grid a tile's cells reach half a cell over its north and west edges: always clipped
        margin = {"top": np.zeros(wp + 2), "bottom": np.zeros(wp + 2), "left": np.zeros(hp + 2), "right": np.zeros(hp + 2),
                  "corners": (0.0, 0.0, 0.0, 0.0)}
    z = np.zeros((hp + 2, wp + 2), np.float32)
    z[1:-1, 1:-1] = keep
    if margin is not None:
        z[0, :], z[-1, :], z[:, 0], z[:, -1] = margin["top"], margin["bottom"], margin["left"], margin["right"]
        z[0, 0], z[0, -1], z[-1, 0], z[-1, -1] = margin["corners"]
    polys, offs = contourpy.contour_generator(z=z, name="serial", fill_type="OuterOffset", corner_mask=False).filled(0.5, 1.5)
    box = shapely.box(edge, edge, wp + edge, hp + edge)
    recs, empty, mixed = [], 0, 0
    kr, kc = np.nonzero(keep)
    for pts, off in zip(polys, offs):
        rings = [pts[off[i]:off[i + 1]] for i in range(len(off) - 1)]
        if margin is None:
            parts = [(rings, None)]
        else:
            # whole polygons inside the square stay exactly as contoured; the others are clipped to it (on the
            # pixel-is-point grid also those that reach the square's side: the margin's cells lie on it, and a piece of
            # the neighbours' alone ends there)
            inside = (pts[:, 0].min() >= edge and pts[:, 0].max() <= wp + edge and pts[:, 1].min() >= edge and pts[:, 1].max() <= hp + edge)
            if edge != 0.5:
                inside = (pts[:, 0].min() > edge and pts[:, 0].max() < wp + edge and pts[:, 1].min() > edge and pts[:, 1].max() < hp + edge)
            if inside:
                parts = [(rings, None)]
            else:
                g = shapely.Polygon(rings[0], rings[1:])
                if not g.is_valid:
                    g = shapely.make_valid(g)
                g = shapely.intersection(g, box)
                parts = []
                for p in shapely.get_parts(g):
                    if p.geom_type == "Polygon" and p.area > 1e-9:
                        parts.append(([snap(np.asarray(p.exterior.coords), wp, hp, edge)] + [snap(np.asarray(h.coords), wp, hp, edge) for h in p.interiors], p))
                    elif p.geom_type == "MultiPolygon":
                        for q in p.geoms:
                            if q.area > 1e-9:
                                parts.append(([snap(np.asarray(q.exterior.coords), wp, hp, edge)] + [snap(np.asarray(h.coords), wp, hp, edge) for h in q.interiors], q))
        for prings, geom in parts:
            lvfl = None
            if geom is None:
                own = first_owner(prings[0], keep, lab, hp, wp)
            else:
                own = area_owner(geom, kr, kc, lab, on_edge=not old)
                if own == -2:
                    mixed += 1
                    own = area_owner(geom, kr, kc, lab, majority=True, on_edge=not old)
                if (own is None or own <= 0 or owner[own] is None) and codes is not None and not old:
                    lvfl = margin_owner(geom, codes, hp, wp)
            if lvfl is None and (own is None or own <= 0 or owner[own] is None):
                if geom is not None:
                    empty += 1
                continue
            if geom is not None:
                prings = orient(prings)
            out = []
            for i, ring in enumerate(prings):
                ring = bl.simplify_ring(ring, wp, hp, edge=edge)
                if ring is None:
                    if i == 0:
                        break
                    continue
                X = np.round((ring[:, 0] - edge) * unit / wp).astype(np.int64)
                Y = np.round((hp - (ring[:, 1] - edge)) * unit / hp).astype(np.int64)
                out.append(np.column_stack([X, Y]))
            if out:
                lv, fl = lvfl if lvfl is not None else owner[own]
                recs.append((int(lv), int(fl), out))
    return recs, empty, mixed


def margin_owner(geom, codes, hp, wp):
    """The lake (level, flags) of the south and east neighbours' cells a clipped piece holds (on its south or east side,
    where they lie on the pixel-is-point grid), the most of them; None where it holds none."""
    bottom, right = np.asarray(codes["bottom"]), np.asarray(codes["right"])
    xs = np.r_[np.arange(1, wp + 1), np.full(hp, wp + 1), wp + 1].astype(np.float64)
    ys = np.r_[np.full(wp, hp + 1), np.arange(1, hp + 1), hp + 1].astype(np.float64)
    cs = np.r_[bottom[1:wp + 1], right[1:hp + 1], codes["corners"][3]].astype(np.int64)
    m = cs != 0
    if not m.any():
        return None
    x0, y0, x1, y1 = geom.bounds
    m &= (xs >= x0) & (xs <= x1) & (ys >= y0) & (ys <= y1)
    if not m.any():
        return None
    s = np.nonzero(m)[0]
    s = s[shapely.intersects_xy(geom, xs[s], ys[s])]
    if not len(s):
        return None
    u, n = np.unique(cs[s], return_counts=True)
    c = int(u[np.argmax(n)])
    return c >> 8, (c & 0xFF) - 1


def snap(c, wp, hp, edge=0.5):
    """Ring coordinates on the square's sides set exactly on them (the clip's arithmetic leaves the last bits)."""
    c = c.copy()
    for col, lo, hi in ((0, edge, wp + edge), (1, edge, hp + edge)):
        c[np.abs(c[:, col] - lo) < 1e-6, col] = lo
        c[np.abs(c[:, col] - hi) < 1e-6, col] = hi
    return c


def orient(rings):
    """Outer ring and holes wound as contourpy winds them (the outer anticlockwise in the array's x right, y down; holes the
    other way), so a lake's signed area keeps its sign in the game."""
    out = []
    for i, r in enumerate(rings):
        a = 0.5 * np.sum(r[:-1, 0] * r[1:, 1] - r[1:, 0] * r[:-1, 1])
        want = W["outer_sign"] if i == 0 else -W["outer_sign"]
        out.append(r if np.sign(a) == want else r[::-1].copy())
    return out


def first_owner(ring, keep, lab, hp, wp):
    """Version 2's choice: the label of a drawn cell next to the ring's first vertex."""
    x, y = ring[0]
    if abs(x - round(x)) < 1e-6:
        cands = [(int(round(x)), int(np.floor(y))), (int(round(x)), int(np.ceil(y)))]
    else:
        cands = [(int(np.floor(x)), int(round(y))), (int(np.ceil(x)), int(round(y)))]
    for cx_, cy_ in cands:
        if 1 <= cy_ <= hp and 1 <= cx_ <= wp and keep[cy_ - 1, cx_ - 1]:
            return lab[cy_ - 1, cx_ - 1]
    return 0


def area_owner(geom, kr, kc, lab, majority=False, on_edge=False):
    """The label of the drawn cells whose centres a clipped piece holds; -2 where they belong to several (majority: the
    most), None where it holds none. on_edge: centres on the piece's boundary count (on the pixel-is-point grid the
    first row's and column's centres lie on the tile's edge; inside a tile no centre lies on a contour)."""
    x0, y0, x1, y1 = geom.bounds
    s = np.nonzero((kc + 1 >= x0) & (kc + 1 <= x1) & (kr + 1 >= y0) & (kr + 1 <= y1))[0]
    if not len(s):
        return None
    test = shapely.intersects_xy if on_edge else shapely.contains_xy
    inside = s[test(geom, kc[s] + 1.0, kr[s] + 1.0)]
    if not len(inside):
        return None
    ls = lab[kr[inside], kc[inside]]
    u, n = np.unique(ls, return_counts=True)
    if len(u) > 1 and not majority:
        return -2
    return int(u[np.argmax(n)])


def raw_bytes(recs):
    return bl.varints([len(recs)]) + b"".join(bl.lake_bytes(lv, fl, rings) for lv, fl, rings in recs)


def tile_bytes(recs):
    raw = raw_bytes(recs)
    return brotli.compress(raw, quality=11, lgwin=22), len(raw)


def canon(recs):
    return sorted((lv, fl, tuple(tuple(map(tuple, r.tolist())) for r in rings)) for lv, fl, rings in recs)


# ------------------------------------------------------------------------------------------- passes

def init(old_path, work, borders=None):
    b, version, states, idx = read_index(old_path)
    W["old"], W["idx"], W["work"] = b, idx, work
    W["borders"] = borders
    W["outer_sign"] = outer_sign()


def owner_path(t):
    return os.path.join(W["work"], f"{t}.npy")


def outer_sign():
    """contourpy's winding of an outer ring in the array (x right, y down): a lone cell's."""
    z = np.zeros((5, 5), np.float32)
    z[2, 2] = 1
    polys, offs = contourpy.contour_generator(z=z, name="serial", fill_type="OuterOffset", corner_mask=False).filled(0.5, 1.5)
    r = polys[0][offs[0][0]:offs[0][1]]
    return np.sign(0.5 * np.sum(r[:-1, 0] * r[1:, 1] - r[1:, 0] * r[:-1, 1]))


def pass_a(t):
    """A tile's drawn lakes' border cells, and whether version 2's method gives version 2's tile back from them (its raw
    bytes). Each label's lake goes to the work folder for pass B."""
    lat, lon = t // 360 - 90, t % 360 - 180
    d = drawn(lat, lon)
    if d is None:
        return t, None, None, 0
    keep, lab, owner, lakes = d
    recs, _, _ = outline(keep, lab, owner, None, old=True)
    o, n = W["idx"][t]
    same = raw_bytes(recs) == brotli.decompress(W["old"][o:o + n]) or canon(recs) == canon(lakes)
    np.save(owner_path(t), np.array([(0, 0, 0) if v is None else (1, v[0], v[1]) for v in owner], np.int64))
    hp, wp = keep.shape
    br = {"top": np.packbits(keep[0]), "bottom": np.packbits(keep[-1]), "left": np.packbits(keep[:, 0]),
          "right": np.packbits(keep[:, -1]), "w": wp, "h": hp}
    return t, br, same, int(keep.sum())


def border(t, side, n):
    """A neighbour's border cells, resampled to n cells (by cell centre); none where the neighbour has no drawn lakes."""
    br = W["borders"].get(t)
    if br is None:
        return np.zeros(n, np.float32)
    m = br["w"] if side in ("top", "bottom") else br["h"]
    v = np.unpackbits(br[side])[:m].astype(np.float32)
    if m == n:
        return v
    return v[resample_index(n, m)]


def resample_index(n, m):
    """For n cells along a side, the neighbour's cell (of m) each takes across a parallel where GLO-90's column count
    changes: the nearest on the pixel-is-point grid (cell i lies on i/n of the degree), else the one holding the cell's
    centre."""
    if gg.POINT:
        return np.minimum(np.floor(np.arange(n) * m / n + 0.5).astype(np.int64), m - 1)
    return np.minimum(((np.arange(n) + 0.5) * m / n).astype(np.int64), m - 1)


def corner(t, which):
    br = W["borders"].get(t)
    if br is None:
        return 0.0
    row = np.unpackbits(br["top" if which[0] == "t" else "bottom"])[:br["w"]]
    return float(row[0] if which[1] == "l" else row[-1])


def margin(t):
    """One cell of the eight neighbours' drawn lakes around the tile (z's outer ring), or None where all of it is land."""
    lat, lon = t // 360 - 90, t % 360 - 180
    own = W["borders"][t]
    hp, wp = own["h"], own["w"]
    wl, el = (lon - 1 + 180) % 360 - 180, (lon + 1 + 180) % 360 - 180
    k = lambda la, lo: tile_key(la, lo) if -90 <= la <= 89 else None
    m = {
        # the row above this tile's first row is the north tile's last; below its last, the south tile's first
        "top": np.concatenate([[0], border(k(lat + 1, lon), "bottom", wp), [0]]),
        "bottom": np.concatenate([[0], border(k(lat - 1, lon), "top", wp), [0]]),
        "left": np.concatenate([[0], border(tile_key(lat, wl), "right", hp), [0]]),
        "right": np.concatenate([[0], border(tile_key(lat, el), "left", hp), [0]]),
        "corners": (corner(k(lat + 1, wl), "br"), corner(k(lat + 1, el), "bl"), corner(k(lat - 1, wl), "tr"), corner(k(lat - 1, el), "tl")),
    }
    if not (m["top"].any() or m["bottom"].any() or m["left"].any() or m["right"].any() or any(m["corners"])):
        return None
    return m


def pass_b(t):
    """The tile outlined with its margin: (tile, lakes, brotli bytes or None for version 2's own, vertices, raw bytes,
    pieces without a cell, pieces over two labels, changed)."""
    m = margin(t)
    if m is None and not gg.POINT:
        return t, -1, None, 0, 0, 0, 0, False
    lat, lon = t // 360 - 90, t % 360 - 180
    lab, k = labels(lat, lon)
    own = np.load(owner_path(t))
    owner = [(int(v[1]), int(v[2])) if v[0] else None for v in own]
    draw = np.array([o is not None for o in owner])
    draw[0] = False
    keep = draw[lab]
    recs, empty, mixed = outline(keep, lab, owner, m)
    raw = raw_bytes(recs)
    o, n = W["idx"][t]
    changed = gg.POINT or raw != brotli.decompress(W["old"][o:o + n])
    if not changed:
        return t, -1, None, 0, 0, empty, mixed, False
    nv = sum(len(r) for _, _, rings in recs for r in rings)
    return t, len(recs), brotli.compress(raw, quality=11, lgwin=22) if recs else b"", nv, len(raw), empty, mixed, True


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    old, out = sys.argv[1], sys.argv[2]
    workers = int(sys.argv[3]) if len(sys.argv) > 3 else 8
    filt = sys.argv[4] if len(sys.argv) > 4 else ""
    os.makedirs(out, exist_ok=True)
    work = os.path.join(out, "owners")
    os.makedirs(work, exist_ok=True)
    b, version, states, idx = read_index(old)
    log(f"{old}: version {version}, {len(idx)} tiles with lakes; writing on {gg.describe()}")
    tiles = sorted(t for t in idx if any(f in os.path.basename(wbm_path(t // 360 - 90, t % 360 - 180)) for f in filt.split(",")))
    # pass A: every tile with lakes (the neighbours of a filtered set too)
    need = set(tiles)
    if filt:
        for t in tiles:
            lat, lon = t // 360 - 90, t % 360 - 180
            for dr in (-1, 0, 1):
                for dc in (-1, 0, 1):
                    if -90 <= lat + dr <= 89:
                        need.add(tile_key(lat + dr, (lon + dc + 180) % 360 - 180))
    need = sorted(t for t in need if t in idx)
    borders, back, notback, cells = {}, 0, [], 0
    with Pool(workers, initializer=init, initargs=(old, work)) as pool:
        for i, (t, br, same, n) in enumerate(pool.imap_unordered(pass_a, need, chunksize=4), 1):
            if br is not None:
                borders[t] = br
                cells += n
                if same:
                    back += 1
                else:
                    notback.append(t)
            if i % 1000 == 0:
                log(f"pass A: {i}/{len(need)} tiles")
    log(f"pass A: {len(borders)} tiles, {cells} drawn lake cells; version 2's method gives version 2's tile back in {back} "
        f"(not in {len(notback)}: {sorted(notback)[:20]})")
    if notback:
        raise SystemExit("version 2's tiles not recovered exactly: stopped")
    new_states = states.copy()
    blobs, nl, nv, nraw, empty, mixed, changed = {}, 0, 0, 0, 0, 0, 0
    with Pool(workers, initializer=init, initargs=(old, work, borders)) as pool:
        for i, (t, n, blob, v, raw, e, mx, ch) in enumerate(pool.imap_unordered(pass_b, tiles, chunksize=4), 1):
            empty += e; mixed += mx
            if ch:
                changed += 1
                if n:
                    blobs[t] = blob; nl += n; nv += v; nraw += raw
                else:
                    new_states[t] = 0
            if i % 1000 == 0:
                log(f"pass B: {i}/{len(tiles)} tiles, {changed} changed")
    # unchanged tiles keep version 2's bytes
    for t in idx:
        if t not in blobs and new_states[t] == 1:
            o, n = idx[t]
            blobs[t] = b[o:o + n]
    head = struct.pack("<4siiiii", b"EMLO", VERSION, OUT_UNIT, 1, 360, 180)
    index = bytearray()
    o = len(head) + len(new_states) + 8 * len(blobs)
    body = bytearray()
    for t in sorted(blobs):
        index += struct.pack("<ii", o + len(body), len(blobs[t]))
        body += blobs[t]
    data = head + new_states.tobytes() + bytes(index) + bytes(body)
    name = "lakes-glo90-3p.bin" if gg.POINT else "lakes-glo90-3.bin"
    with open(os.path.join(out, name), "wb") as f:
        f.write(data)
    info = {"file": name, "from": os.path.basename(old), "from_sha256": hashlib.sha256(b).hexdigest(), "bytes": len(data),
            "sha256": hashlib.sha256(data).hexdigest(), "tiles_checked": len(tiles), "tiles_changed": changed,
            "tiles_with_lakes": len(blobs), "lakes_in_changed_tiles": nl, "vertices_in_changed_tiles": nv,
            "pieces_without_a_cell": empty, "pieces_over_two_labels": mixed, "pass_a_back": back, "pass_a_not_back": len(notback)}
    with open(os.path.join(out, name.replace(".bin", ".json")), "w", encoding="utf8") as f:
        json.dump(info, f, indent=1)
    log(json.dumps(info))


if __name__ == "__main__":
    main()
