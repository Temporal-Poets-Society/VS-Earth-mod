"""Size experiments on copies of climate.bin, the climate fixes, weather.bin, the JSON assets and the PNGs. Read-only."""
import io, json, os, struct, sys, zlib, pathlib
import numpy as np, brotli

Z = pathlib.Path(sys.argv[1])


def defl(b):
    co = zlib.compressobj(9, zlib.DEFLATED, -15)
    return len(co.compress(b) + co.flush())


def b11(x):
    return len(brotli.compress(bytes(x), quality=11, lgwin=24))


def med_res(a):
    a = a.astype(np.int32)
    w = np.zeros_like(a); w[:, 1:] = a[:, :-1]
    n = np.zeros_like(a); n[1:] = a[:-1]
    nw = np.zeros_like(a); nw[1:, 1:] = a[:-1, :-1]
    lo, hi = np.minimum(w, n), np.maximum(w, n)
    p = np.where(nw >= hi, lo, np.where(nw <= lo, hi, w + n - nw))
    return a - p


def split16(rs):
    z = [((r << 1) ^ (r >> 31)).astype(np.uint16) for r in rs]
    return b''.join((x & 255).astype(np.uint8).tobytes() for x in z) + b''.join((x >> 8).astype(np.uint8).tobytes() for x in z)


# ---------------------------------------------------------------- climate.bin
f = (Z / 'earthdata/climate.bin').read_bytes()
ver, w, h = struct.unpack_from('<iii', f, 4)
n = w * h
raw = brotli.decompress(f[16:])
print(f'climate.bin: {len(f):,} bytes, version {ver}, {w}x{h}, raw {len(raw):,}')
P = [np.frombuffer(raw[2 * n * k:2 * n * (k + 1)], '<i2').reshape(h, w) for k in range(3)]
P.append(np.frombuffer(raw[6 * n:8 * n], '<u2').reshape(h, w))
P.append(np.frombuffer(raw[8 * n:9 * n], np.uint8).reshape(h, w))
if ver >= 3:
    P.append(np.frombuffer(raw[9 * n:10 * n], np.uint8).reshape(h, w))
print('  re-brotli raw q11 lgwin24:', f'{b11(raw):,}')
m16 = [med_res(p) for p in P[:4]]
m8 = [(med_res(p) & 255).astype(np.uint8).tobytes() for p in P[4:]]
print('  MED residual, byte planes:', f'{b11(split16(m16) + b"".join(m8)):,}')
d = [P[0], P[1].astype(np.int32) - P[0], P[2].astype(np.int32) - P[0], P[3]]
print('  MED, cold/warm as differences from the mean:', f'{b11(split16([med_res(x) for x in d]) + b"".join(m8)):,}')
for k, p in enumerate(P):
    print(f'    plane {k}: distinct {len(np.unique(p))}, gcd of values {np.gcd.reduce(np.abs(p.astype(np.int64)).ravel())}')

# ---------------------------------------------------------------- climate fixes
planes = [p.astype(np.float64) for p in P]
cur = {0: [p.copy() for p in P]}
for v, name in ((1, 'climatefix.bin'), (2, 'climatefix2.bin'), (3, 'climatefix3.bin'), (4, 'climatefix4.bin')):
    f = (Z / 'earthdata' / name).read_bytes()
    layered = v >= 3
    head = 24 if layered else 20
    ver, fw, fh, count = struct.unpack_from('<iiii', f, 4)
    mask = struct.unpack_from('<i', f, 20)[0] if layered else 0x3F
    raw = brotli.decompress(f[head:])
    idx = np.frombuffer(raw[:4 * count], '<i4').astype(np.int64)
    if layered:
        idx = idx.cumsum()
    base = cur[v - 1] if layered else cur[0]
    new = [p.copy() for p in base]
    o = 4 * count
    same = []
    payload_diff = []
    for p in range(6):
        if not mask >> p & 1:
            same.append('-')
            continue
        if p < 4:
            vals = np.frombuffer(raw[o:o + 2 * count], '<i2' if p < 3 else '<u2'); o += 2 * count
        else:
            vals = np.frombuffer(raw[o:o + count], np.uint8); o += count
        old = base[p].ravel()[idx]
        changed = int((old != vals).sum())
        same.append(f'{changed}')
        flat = new[p].reshape(-1).copy(); flat[idx] = vals; new[p] = flat.reshape(h, w)
        payload_diff.append((p, vals.astype(np.int32) - old.astype(np.int32)))
    cur[v] = new
    print(f'{name}: {len(f):,} bytes, {count:,} cells of {n:,}, mask {mask:#x}; cells that differ from the base per plane: {same}')
    if not layered:
        # alternative A: only the planes that change, the cells as a bitmap, values as differences from the base
        bitmap = np.zeros(n, np.uint8); bitmap[idx] = 1
        bm = np.packbits(bitmap).tobytes()
        parts = []
        for p, dif in payload_diff:
            if not dif.any():
                continue
            parts.append(split16([dif]) if p < 4 else (dif & 255).astype(np.uint8).tobytes())
        a = b11(bm + b''.join(parts))
        # alternative B: the whole changed planes as grids (difference from the base, MED) - no index at all
        full = []
        for p, dif in payload_diff:
            if not dif.any():
                continue
            g = np.zeros(n, np.int32); g[idx] = dif
            full.append(split16([g.reshape(h, w)]) if p < 4 else (g & 255).astype(np.uint8).tobytes())
        b = b11(b''.join(full))
        g2 = []
        for p, dif in payload_diff:
            if not dif.any():
                continue
            r = med_res(new[p].astype(np.int32))
            g2.append(split16([r]) if p < 4 else (r & 255).astype(np.uint8).tobytes())
        c = b11(b''.join(g2))
        print(f'  A bitmap + changed planes as differences: {a:,};  B changed planes as difference grids: {b:,};  C changed planes whole (MED): {c:,}')

# ---------------------------------------------------------------- weather.bin
f = (Z / 'earthdata/weather.bin').read_bytes()
ver, w, h, months = struct.unpack_from('<iiii', f, 4)
n = w * h
raw = brotli.decompress(f[20:])
planes_n = len(raw) // n
print(f'weather.bin: {len(f):,} bytes, version {ver}, {w}x{h}, {planes_n} planes, raw {len(raw):,}')
W = np.frombuffer(raw, np.uint8).reshape(planes_n, h, w)
print('  re-brotli raw:', f'{b11(raw):,}')
print('  MED per plane:', f'{b11(b"".join((med_res(p) & 255).astype(np.uint8).tobytes() for p in W)):,}')
# month deltas within each group of 12, the first plane of a group MED alone
groups = [(0, 12, False), (12, 24, False), (24, 25, False)] + ([(25, 37, True), (37, 49, True), (49, 61, False)] if planes_n >= 61 else [])
out = []
for a, b, signed in groups:
    g = W[a:b].astype(np.int8 if signed else np.uint8).astype(np.int32)
    out.append((med_res(g[0]) & 255).astype(np.uint8).tobytes())
    for m in range(1, b - a):
        out.append((med_res(g[m] - g[m - 1]) & 255).astype(np.uint8).tobytes())
print('  month differences, MED:', f'{b11(b"".join(out)):,}')
out = []
for a, b, signed in groups:
    g = W[a:b].astype(np.int8 if signed else np.uint8).astype(np.int32)
    mean = np.round(g.mean(0)).astype(np.int32)
    out.append((med_res(mean) & 255).astype(np.uint8).tobytes())
    if b - a > 1:
        for m in range(b - a):
            out.append((med_res(g[m] - mean) & 255).astype(np.uint8).tobytes())
print('  yearly mean + month anomalies, MED (one extra plane a group):', f'{b11(b"".join(out)):,}')
for a, b, signed in groups:
    g = W[a:b]
    print(f'    planes {a}..{b - 1}: brotli alone {b11(g.tobytes()):,}, MED {b11(b"".join((med_res(p.astype(np.int8 if signed else np.uint8)) & 255).astype(np.uint8).tobytes() for p in g)):,}, distinct values {len(np.unique(g))}')

# ---------------------------------------------------------------- JSON
print('JSON assets (deflate 9 as in the zip):')
tot0 = tot1 = totb = 0
for p in sorted(Z.rglob('*.json')):
    b = p.read_bytes()
    if len(b) < 20000:
        continue
    try:
        obj = json.loads(b.decode('utf-8-sig'))
    except Exception as e:
        print(f'  {p.name}: not strict JSON ({e})')
        continue
    mini = json.dumps(obj, ensure_ascii=False, separators=(',', ':')).encode('utf-8')
    d0, d1, br = defl(b), defl(mini), b11(mini)
    tot0 += d0; tot1 += d1; totb += br
    print(f'  {p.relative_to(Z).as_posix():60s} {len(b):9,d} -> deflate {d0:8,d}; minified {len(mini):9,d} -> deflate {d1:8,d} ({d1 - d0:+,d}); brotli 11 {br:8,d} ({br - d0:+,d})')
print(f'  total deflate {tot0:,} -> minified {tot1:,} ({tot1 - tot0:+,d}); minified + brotli {totb:,} ({totb - tot0:+,d})')

# ---------------------------------------------------------------- PNG
try:
    from PIL import Image
    pngs = sorted(Z.rglob('*.png'))
    t0 = t1 = tpal = 0
    sizes = {}
    modes = {}
    for p in pngs:
        b = p.read_bytes()
        im = Image.open(io.BytesIO(b)); im.load()
        sizes[im.size] = sizes.get(im.size, 0) + 1
        modes[im.mode] = modes.get(im.mode, 0) + 1
        o = io.BytesIO(); im.save(o, 'PNG', optimize=True)
        best = min(len(b), len(o.getvalue()))
        # lossless palette where the image has at most 256 colours
        cols = im.convert('RGBA').getcolors(257)
        if cols is not None and im.mode != 'P':
            q = im.convert('RGBA').quantize(256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE) if im.mode == 'RGBA' else im.convert('P', palette=Image.Palette.ADAPTIVE, colors=256)
            if list(q.convert('RGBA').getdata()) == list(im.convert('RGBA').getdata()):
                o2 = io.BytesIO(); q.save(o2, 'PNG', optimize=True)
                tpal += 1
                best = min(best, len(o2.getvalue()))
        t0 += len(b); t1 += best
    print(f'PNG: {len(pngs)} files, {t0:,} bytes; Pillow optimize / lossless palette: {t1:,} ({t1 - t0:+,d}); {tpal} could be paletted; sizes {sizes}; modes {modes}')
    names = sum(len(p.relative_to(Z).as_posix()) for p in pngs)
    print(f'  zip overhead of the PNG entries: {len(pngs) * 76 + 2 * names:,} bytes (30 + 46 bytes and the name twice, each)')
except ImportError as e:
    print('no Pillow:', e)

allf = [p for p in Z.rglob('*') if p.is_file()]
print(f'zip entries {len(allf)}, overhead {len(allf) * 76 + 2 * sum(len(p.relative_to(Z).as_posix()) for p in allf) + 22:,} bytes')
# entries deflate makes bigger
grow = 0
for p in allf:
    b = p.read_bytes()
    d = defl(b)
    if d > len(b):
        grow += d - len(b)
print(f'bytes lost by deflating entries that do not compress (store them instead): {grow:,}')
