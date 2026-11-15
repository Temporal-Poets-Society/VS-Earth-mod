"""Size experiments on copies of the tiled grids (rain.bin, aridity.bin, temperature.bin). Read-only."""
import struct, sys, time, lzma
from concurrent.futures import ProcessPoolExecutor
import numpy as np, brotli

Z = sys.argv[1]
STEP = int(sys.argv[2]) if len(sys.argv) > 2 else 6


def grid(data):
    version, w, h, tile, count = struct.unpack_from('<iiiii', data, 4)
    return version, w, h, tile, [struct.unpack_from('<ii', data, 24 + 8 * i) for i in range(count)]


def med_res(a):
    """MED (LOCO-I) residual of an int array, 0 outside."""
    a = a.astype(np.int32)
    w = np.zeros_like(a); w[:, 1:] = a[:, :-1]
    n = np.zeros_like(a); n[1:] = a[:-1]
    nw = np.zeros_like(a); nw[1:, 1:] = a[:-1, :-1]
    lo, hi = np.minimum(w, n), np.maximum(w, n)
    p = np.where(nw >= hi, lo, np.where(nw <= lo, hi, w + n - nw))
    return a - p, (w, n, nw)


def planar_res(a):
    a = a.astype(np.int32)
    w = np.zeros_like(a); w[:, 1:] = a[:, :-1]
    n = np.zeros_like(a); n[1:] = a[:-1]
    nw = np.zeros_like(a); nw[1:, 1:] = a[:-1, :-1]
    return a - (w + n - nw)


def left_res(a):
    a = a.astype(np.int32)
    w = np.zeros_like(a); w[:, 1:] = a[:, :-1]
    w[1:, 0] = a[:-1, 0]
    return a - w


def zig16(r):
    return ((r << 1) ^ (r >> 31)).astype(np.uint16)


def b11(x):
    return len(brotli.compress(x, quality=11, lgwin=24))


def u8_job(job):
    blob, tile, planes = job
    raw = brotli.decompress(blob)
    n = tile * tile
    out = {'orig': len(blob)}
    ps = [np.frombuffer(raw[k * n:(k + 1) * n], np.uint8).reshape(tile, tile) for k in range(planes)]
    for name, f in (('med', lambda a: med_res(a)[0]), ('planar', planar_res), ('left', left_res)):
        out[name] = b11(b''.join((f(p) & 255).astype(np.uint8).tobytes() for p in ps))
    out['lzma_raw'] = len(lzma.compress(raw, preset=9 | lzma.PRESET_EXTREME))
    out['lzma_med'] = len(lzma.compress(b''.join((med_res(p)[0] & 255).astype(np.uint8).tobytes() for p in ps), preset=9 | lzma.PRESET_EXTREME))
    # context statistics for an adaptive range coder: residual (mod 256) by quantised gradient context
    hist = np.zeros((planes, 27, 256), np.int64)
    for k, p in enumerate(ps):
        r, (w, nn, nw) = med_res(p)
        ne = np.zeros_like(nn); ne[1:, :-1] = p[:-1, 1:].astype(np.int32)
        def q(d):
            return np.where(d < -3, 0, np.where(d > 3, 2, 1)) if False else np.digitize(d, [-2, 3])
        ctx = q(nn - nw) * 9 + q(w - nw) * 3 + q(ne - nn)
        np.add.at(hist[k], (ctx.ravel(), (r & 255).ravel()), 1)
    return out, hist


def t_job(job):
    blob, tile = job
    raw = brotli.decompress(blob)
    n = tile * tile
    out = {'orig': len(blob)}
    res = [np.frombuffer(raw[k * 2 * n:(k + 1) * 2 * n], '<i2').reshape(tile, tile) for k in range(3)]
    mask = raw[6 * n:]
    val = [r.astype(np.int64).cumsum(0).cumsum(1).astype(np.int16) for r in res]
    # the values are multiples of 5 (0.5 C steps of 0.1 C units)? measure
    out['step5'] = int(all((v % 5 == 0).all() for v in val))
    def planes16(rs):
        z = [zig16(r.astype(np.int32)) for r in rs]
        return b''.join((x & 255).astype(np.uint8).tobytes() for x in z) + b''.join((x >> 8).astype(np.uint8).tobytes() for x in z)
    out['planar_split'] = b11(planes16(res) + mask)
    medr = [med_res(v)[0] for v in val]
    out['med_split'] = b11(planes16(medr) + mask)
    # in steps of 5, as bytes where they fit; cold and warm as differences from the mean
    if out['step5']:
        q = [(v.astype(np.int32) // 5) for v in val]
        d = [q[0], q[1] - q[0], q[2] - q[0]]
        m = [med_res(x)[0] for x in d]
        out['q5_med_split'] = b11(planes16(m) + mask)
        m2 = [med_res(x)[0] for x in q]
        out['q5_med_nodiff'] = b11(planes16(m2) + mask)
        hist = np.zeros((3, 27, 512), np.int64)
        for k, x in enumerate(d):
            r, (w, nn, nw) = med_res(x)
            ne = np.zeros_like(nn); ne[1:, :-1] = x[:-1, 1:]
            q3 = lambda dd: np.digitize(dd, [-2, 3])
            ctx = q3(nn - nw) * 9 + q3(w - nw) * 3 + q3(ne - nn)
            np.add.at(hist[k], (ctx.ravel(), np.clip(r + 256, 0, 511).ravel()), 1)
        return out, hist
    return out, None


def entropy_bytes(hist):
    tot = 0.0
    for h in hist.reshape(-1, hist.shape[-1]):
        s = h.sum()
        if s:
            p = h[h > 0] / s
            tot += -(h[h > 0] * np.log2(p)).sum()
    return tot / 8


def run(name, planes):
    data = open(f'{Z}/earthdata/{name}', 'rb').read()
    version, w, h, tile, entries = grid(data)
    full = [(o, n) for o, n in entries if n]
    sample = full[::STEP]
    share = sum(n for o, n in sample) / sum(n for o, n in full)
    print(f'{name}: {len(data):,} bytes, {w}x{h} tile {tile}, {len(full)} tiles with data of {len(entries)}; sample {len(sample)} tiles = {share:.3f} of the bytes', flush=True)
    t = time.time()
    with ProcessPoolExecutor(10) as pool:
        if planes:
            rs = list(pool.map(u8_job, [(data[o:o + n], tile, planes) for o, n in sample], chunksize=4))
        else:
            rs = list(pool.map(t_job, [(data[o:o + n], tile) for o, n in sample], chunksize=4))
    keys = rs[0][0].keys()
    tot = {k: sum(r[0].get(k, 0) for r in rs) for k in keys}
    hist = None
    for r in rs:
        if r[1] is not None:
            hist = r[1] if hist is None else hist + r[1]
    for k, v in tot.items():
        print(f'  {k:16s} {v:12,d}  x{v / tot["orig"]:.3f}  -> whole file about {v / share / 1e6:.2f} MB')
    if hist is not None:
        e = entropy_bytes(hist)
        print(f'  ctx-entropy      {e:12,.0f}  x{e / tot["orig"]:.3f}  -> whole file about {e / share / 1e6:.2f} MB (27 gradient contexts per plane, static)')
        for k in range(hist.shape[0]):
            print(f'    plane {k}: {entropy_bytes(hist[k]) / share / 1e6:.2f} MB')
    print(f'  {time.time() - t:.0f} s', flush=True)


if __name__ == '__main__':
    for name, planes in (('aridity.bin', 1), ('rain.bin', 3), ('temperature.bin', 0)):
        run(name, planes)
