"""EA-0131 (decision 0061): the streams layer's input for tools/RiverSnap.

Selects the HydroRIVERS v1.0 reaches (Lehner & Grill 2013) from MIN_Q up to the large rivers' 20 m3/s, leaves out those
GIRES v1.0 (Messager, Lehner et al. 2021) has stopping 30 days a year or more for want of water (kept where the coldest
month is below FROST_C: there it is frost that stops them, and the game freezes them itself), and writes one rivers.bin
v2 file: the streams in flow order, then the large rivers as the game's new worlds lay them on GLO-90 (rivers-glo90.bin,
same reaches as rivers.bin), which RiverSnap holds frozen (frozenfrom=<stream count>) so the streams end on their snapped
courses. A stream whose downstream reach is neither a stream kept nor a large river ends there (Next -1).

usage: python -I tools/streams/build_stream_input.py <HydroRIVERS_v10.shp> <hydrorivers-attrs.npz> <gires-reaches.npz>
           <CHELSA tas folder> <course list> <out.bin> [--min-q 1] [--keep-dry] [--frost -10] [--box la0,la1,lo0,lo1] [--base rivers.bin]
The course list: the newest courses rules' snapped list as the game reads it, written by
`dotnet run -c Release --project tests/StreamsTests -- <repo root> courses <out.bin>` (courses rules 5: rivers-glo90.bin
with rivers-glo90-snap-3.bin applied by RiverSnapData; record version 3, in rivers-glo90.bin's order, reach ends
unchanged, without the side arms), with --base the store's data/rivers.bin; or rivers-glo90.bin itself (rules 1).
Prints the stream count (the frozenfrom value) and writes <out.bin>.json with the counts.
"""
import argparse, json, os, struct, sys, time

import brotli
import numpy as np
import shapefile
import tifffile

t0 = time.time()


def log(m):
    print(f"[{time.time() - t0:7.1f}s] {m}", flush=True)


ap = argparse.ArgumentParser()
ap.add_argument("shp"); ap.add_argument("attrs"); ap.add_argument("gires"); ap.add_argument("chelsa"); ap.add_argument("rivers")
ap.add_argument("out")
ap.add_argument("--min-q", type=float, default=1.0)
ap.add_argument("--keep-dry", action="store_true", help="keep the reaches GIRES has dry 30+ days a year for want of water")
ap.add_argument("--frost", type=float, default=-10.0, help="coldest month (degC) below which GIRES' dry days are frost's")
ap.add_argument("--box", action="append", default=[], help="la0,la1,lo0,lo1: only streams with a vertex inside (tuning)")
ap.add_argument("--base", help="rivers.bin (default: beside the course list), whose first vertices give the junctions")
a = ap.parse_args()
if os.path.exists(a.out):
    sys.exit(a.out + " exists; not replaced")

h = np.load(a.attrs)
ids, nxt, q = h["id"], h["next"], h["q"]
n_all = len(ids)
sel = (q >= a.min_q) & (q < 20)
log(f"{n_all} reaches; {sel.sum()} with {a.min_q} <= Q < 20 m3/s")

# GIRES by HYRIV_ID
g = np.load(a.gires)
o = np.argsort(g["id"])
gid = g["id"][o]
pos = np.clip(np.searchsorted(gid, ids), 0, len(gid) - 1)
found = gid[pos] == ids
c30 = np.where(found, g["c30"][o][pos], 0)
glat, glon = g["lat"][o][pos], g["lon"][o][pos]
dry = sel & (c30 == 1)
counts = {"selected": int(sel.sum()), "gires_often_dry": int(dry.sum())}
if not a.keep_dry:
    # the coldest month of CHELSA v2.1's 1981-2010 means (January or July), at each dry reach's midpoint
    idx = np.nonzero(dry)[0]
    coldest = np.full(len(idx), np.inf)
    for month in ("01", "07"):
        path = os.path.join(a.chelsa, f"CHELSA_tas_{month}_1981-2010_V.2.1.tif")
        with tifffile.TiffFile(path) as tf:
            page = tf.pages[0]
            sx, sy = page.tags[33550].value[:2]
            tie = page.tags[33922].value
            x0, y0 = tie[3], tie[4]
            img = page.asarray()
        r = np.clip(((y0 - glat[idx]) / sy).astype(np.int64), 0, img.shape[0] - 1)
        c = np.clip(((glon[idx] - x0) / sx).astype(np.int64), 0, img.shape[1] - 1)
        v = img[r, c].astype(np.float64)
        t = np.where(v == 65535, np.nan, v * 0.1 - 273.15)
        coldest = np.fmin(coldest, np.where(np.isnan(t), np.inf, t))
        del img
        log(f"CHELSA tas {month} sampled")
    frost = coldest < a.frost
    drop = idx[~frost]
    sel[drop] = False
    counts["dry_kept_for_frost"] = int(frost.sum())
    counts["dry_left_out"] = int(len(drop))
    log(f"GIRES: {len(idx)} streams dry 30+ days a year; {frost.sum()} kept (coldest month below {a.frost} degC), {len(drop)} left out")

# large rivers as laid out on GLO-90 (rivers.bin's reaches, in its order)
# (record version 2, or 3 with a width byte per vertex: EA-0148's courses rules 5 list snapped-5.bin; the widths are dropped)
raw = open(a.rivers, "rb").read()
rv = struct.unpack_from("<i", raw, 4)[0]
assert raw[:4] == b"EMRV" and rv in (2, 3), a.rivers + ": not a rivers file of record version 2 or 3"
nmain = struct.unpack_from("<i", raw, 8)[0]
mb = brotli.decompress(raw[12:])
main = []
off = 0
vdt = np.dtype([("lon", "<f4"), ("lat", "<f4"), ("lev", "<i2"), ("fixed", "u1")] + ([("width", "u1")] if rv == 3 else []))
for i in range(nmain):
    qq, nx, nv = struct.unpack_from("<fii", mb, off)
    off += 12
    arr = np.frombuffer(mb, dtype=vdt, count=nv, offset=off)
    off += vdt.itemsize * nv
    if rv == 3:
        arr = arr[["lon", "lat", "lev", "fixed"]].astype([("lon", "<f4"), ("lat", "<f4"), ("lev", "<i2"), ("fixed", "u1")])
    main.append((qq, nx, arr))
log(f"{a.rivers}: {nmain} reaches")

# shapes of the streams (the shapefile's record order is the dbf's)
shp = shapefile.Reader(a.shp)
sidx = np.nonzero(sel)[0]
lines = {}
boxes = [tuple(float(x) for x in b.split(",")) for b in a.box]
if boxes:
    near = np.zeros(len(sidx), bool)
    for b in boxes:
        near |= (glat[sidx] >= b[0] - 0.3) & (glat[sidx] <= b[1] + 0.3) & (glon[sidx] >= b[2] - 0.3) & (glon[sidx] <= b[3] + 0.3)
    sidx = sidx[near]
for k, i in enumerate(sidx):
    pts = np.asarray(shp.shape(int(i)).points, dtype=np.float64)
    if boxes and not any(((pts[:, 1] >= b[0]) & (pts[:, 1] <= b[1]) & (pts[:, 0] >= b[2]) & (pts[:, 0] <= b[3])).any() for b in boxes):
        continue
    lines[int(i)] = pts
    if k % 200000 == 0:
        log(f"  shapes {k} of {len(sidx)}")
if boxes:
    # with boxes: every stream downstream of one inside, so the systems stay whole down to their ends
    rec_of_id = {int(v): r for r, v in enumerate(ids)}
    todo = list(lines.keys())
    while todo:
        r = todo.pop()
        nr = rec_of_id.get(int(nxt[r]))
        if nr is not None and sel[nr] and nr not in lines:
            lines[nr] = np.asarray(shp.shape(int(nr)).points, dtype=np.float64)
            todo.append(nr)
log(f"{len(lines)} stream shapes read")

# the large river each stream outlet joins: its downstream HydroRIVERS reach's first vertex is the stream's last, and is
# the first vertex of a rivers.bin reach (the bake kept lines' ends; float32 exact)
first = {}
rivers_bin = a.base or os.path.join(os.path.dirname(a.rivers), "rivers.bin")
orig = open(rivers_bin, "rb").read() if os.path.exists(rivers_bin) else None
# (rivers-glo90.bin's first vertices moved with the snap: match on rivers.bin's, which are HydroRIVERS' own)
src = main
if orig is not None:
    ob = brotli.decompress(orig[12:])
    off = 0
    src = []
    for i in range(struct.unpack_from("<i", orig, 8)[0]):
        qq, nx, nv = struct.unpack_from("<fii", ob, off)
        off += 12
        arr = np.frombuffer(ob, dtype=[("lon", "<f4"), ("lat", "<f4"), ("lev", "<i2"), ("fixed", "u1")], count=nv, offset=off)
        off += 11 * nv
        src.append((qq, nx, arr))
    # the course list may hold more reaches than rivers.bin: courses rules 5 (EA-0148) put its side arms first, then
    # rivers.bin's reaches in their order; rivers.bin's reach i is then the list's reach offset + i (checked on the discharges)
    offset = nmain - len(src)
    if offset < 0:
        sys.exit("the course list has fewer reaches than rivers.bin")
    bad = sum(1 for i in range(len(src)) if abs(main[offset + i][0] - src[i][0]) > 1e-3 * max(1.0, src[i][0]))
    if bad:
        sys.exit(f"the course list's reaches from {offset} on are not rivers.bin's in its order ({bad} discharges differ)")
    if offset:
        log(f"the course list starts with {offset} reaches of its own (side arms); rivers.bin's follow")
else:
    sys.exit("rivers.bin not found beside " + a.rivers)
for i, (_, _, arr) in enumerate(src):
    first[(float(arr["lon"][0]), float(arr["lat"][0]))] = i
fx = np.array([k[0] for k in first.keys()]); fy = np.array([k[1] for k in first.keys()]); fi = np.array(list(first.values()))

rec_of_id = {int(v): r for r, v in enumerate(ids)}
recs = sorted(lines.keys())
# flow order: a reach's downstream one is nearer the outlet (DIST_DN_KM falls by its length), then checked
dist = h["distdn"]
order = sorted(recs, key=lambda r: (-float(dist[r]), r))
pos_of = {r: k for k, r in enumerate(order)}
nstreams = len(order)
joins = {"stream": 0, "river": 0, "river_near": 0, "end": 0, "sea_or_sink": 0}
next_index = []
for r in order:
    d = int(nxt[r])
    if d == 0:
        next_index.append(-1); joins["sea_or_sink"] += 1; continue
    dr = rec_of_id.get(d)
    if dr is not None and dr in pos_of:
        if pos_of[dr] <= pos_of[r]:
            sys.exit(f"reach {ids[r]} flows into {d}, not later in the order")
        next_index.append(pos_of[dr]); joins["stream"] += 1; continue
    if dr is not None and q[dr] >= 20:
        p = lines[r][-1]
        key = (float(np.float32(p[0])), float(np.float32(p[1])))
        m = first.get(key)
        if m is None:
            dd = (fx - key[0]) ** 2 + (fy - key[1]) ** 2
            k = int(np.argmin(dd))
            if dd[k] < (2e-3) ** 2:
                m = int(fi[k]); joins["river_near"] += 1
        if m is not None:
            next_index.append(nstreams + offset + m); joins["river"] += 1; continue
    next_index.append(-1); joins["end"] += 1
log(f"{nstreams} streams; joins {joins}")

buf = bytearray()
nv_streams = 0
for k, r in enumerate(order):
    pts = lines[r]
    n = len(pts)
    nv_streams += n
    buf += struct.pack("<fii", float(q[r]), next_index[k], n)
    arr = np.zeros(n, dtype=[("lon", "<f4"), ("lat", "<f4"), ("lev", "<i2"), ("fixed", "u1")])
    arr["lon"], arr["lat"] = pts[:, 0], pts[:, 1]
    buf += arr.tobytes()
for qq, nx, arr in main:
    buf += struct.pack("<fii", qq, nx + nstreams if nx >= 0 else -1, len(arr))
    buf += arr.tobytes()
with open(a.out, "xb") as f:
    f.write(b"EMRV")
    f.write(struct.pack("<ii", 2, nstreams + nmain))
    f.write(brotli.compress(bytes(buf), quality=4, lgwin=24))
counts.update({"streams": nstreams, "stream_vertices": nv_streams, "main": nmain, "joins": joins, "frozenfrom": nstreams,
               "hyriv_ids": [int(ids[r]) for r in order] if len(order) <= 50000 else None})
json.dump(counts, open(a.out + ".json", "w"), indent=1)
# the HYRIV_IDs in stream order, for the tiler
np.save(a.out + ".ids.npy", np.array([int(ids[r]) for r in order], np.int64))
log(f"{a.out}: {nstreams} streams ({nv_streams} vertices) + {nmain} large rivers, {os.path.getsize(a.out) / 1e6:.1f} MB; frozenfrom={nstreams}")
