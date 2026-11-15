"""Whole-file sizes of the best lossless re-encodings (every tile), with a round-trip check. Read-only on copies."""
import struct, sys, time
from concurrent.futures import ProcessPoolExecutor
import numpy as np, brotli

Z = sys.argv[1]


def grid(data):
    version, w, h, tile, count = struct.unpack_from('<iiiii', data, 4)
    return version, w, h, tile, [struct.unpack_from('<ii', data, 24 + 8 * i) for i in range(count)]


def nb(a):
    a = a.astype(np.int32)
    w = np.zeros_like(a); w[:, 1:] = a[:, :-1]
    n = np.zeros_like(a); n[1:] = a[:-1]
    nw = np.zeros_like(a); nw[1:, 1:] = a[:-1, :-1]
    return a, w, n, nw


def med(a):
    a, w, n, nw = nb(a)
    lo, hi = np.minimum(w, n), np.maximum(w, n)
    return a - np.where(nw >= hi, lo, np.where(nw <= lo, hi, w + n - nw))


def left(a):
    a, w, n, nw = nb(a)
    w[1:, 0] = a[:-1, 0]
    return a - w


def avg(a):
    a, w, n, nw = nb(a)
    p = (w + n) >> 1
    p[0, :] = w[0, :]
    p[:, 0] = n[:, 0]
    return a - p


def unleft(r):
    """Inverse of left() for u8 residuals (mod 256): the check that the decoder can undo it."""
    r = r.astype(np.int64)
    first = np.cumsum(r[:, 0]) & 255
    out = r.copy(); out[:, 0] = first
    return (np.cumsum(out, axis=1) & 255).astype(np.uint8)


def b11(x):
    return len(brotli.compress(x, quality=11, lgwin=24))


def split16(rs):
    z = [((r << 1) ^ (r >> 31)).astype(np.uint16) for r in rs]
    return b''.join((x & 255).astype(np.uint8).tobytes() for x in z) + b''.join((x >> 8).astype(np.uint8).tobytes() for x in z)


def u8_job(job):
    blob, tile, planes = job
    raw = brotli.decompress(blob)
    n = tile * tile
    ps = [np.frombuffer(raw[k * n:(k + 1) * n], np.uint8).reshape(tile, tile) for k in range(planes)]
    out = {'orig': len(blob)}
    for name, f in (('left', left), ('med', med), ('avg', avg)):
        out[name] = b11(b''.join((f(p) & 255).astype(np.uint8).tobytes() for p in ps))
    out['best of three + 1 byte'] = 1 + min(out['left'], out['med'], out['avg'])
    out['best incl. raw + 1 byte'] = 1 + min(out['left'], out['med'], out['avg'], out['orig'])
    for p in ps:
        assert np.array_equal(unleft((left(p) & 255).astype(np.uint8)), p)
    # lossy: values halved in precision (for the record only)
    out['left, even values only (lossy)'] = b11(b''.join((left((p >> 1)) & 255).astype(np.uint8).tobytes() for p in ps))
    return out


def t_job(job):
    blob, tile = job
    raw = brotli.decompress(blob)
    n = tile * tile
    res = [np.frombuffer(raw[k * 2 * n:(k + 1) * 2 * n], '<i2').reshape(tile, tile) for k in range(3)]
    mask = raw[6 * n:]
    val = [r.astype(np.int64).cumsum(0).cumsum(1).astype(np.int16) for r in res]
    q = [(v.astype(np.int32) // 5) for v in val]
    assert all(np.array_equal(x * 5, v) for x, v in zip(q, val))
    out = {'orig': len(blob)}
    out['MED, byte planes'] = b11(split16([med(v) for v in val]) + mask)
    out['in 0.5 C steps, MED, byte planes'] = b11(split16([med(x) for x in q]) + mask)
    out['in 0.5 C steps, left, byte planes'] = b11(split16([left(x) for x in q]) + mask)
    out['in 0.5 C steps, avg, byte planes'] = b11(split16([avg(x) for x in q]) + mask)
    r = [med(x) for x in q]
    fits = all(np.abs(x).max() < 128 for x in r)
    out['in 0.5 C steps, MED, one byte where every residual fits (+1)'] = 1 + (b11(b''.join((x & 255).astype(np.uint8).tobytes() for x in r) + mask) if fits else out['in 0.5 C steps, MED, byte planes'])
    return out


def run(name, planes):
    data = open(f'{Z}/earthdata/{name}', 'rb').read()
    version, w, h, tile, entries = grid(data)
    full = [(o, n) for o, n in entries if n]
    head = len(data) - sum(n for o, n in full)
    t = time.time()
    with ProcessPoolExecutor(8) as pool:
        if planes:
            rs = list(pool.map(u8_job, [(data[o:o + n], tile, planes) for o, n in full], chunksize=8))
        else:
            rs = list(pool.map(t_job, [(data[o:o + n], tile) for o, n in full], chunksize=8))
    print(f'{name}: {len(data):,} bytes ({head:,} of header and index), {len(full)} tiles, {time.time() - t:.0f} s')
    for k in rs[0]:
        v = head + sum(r[k] for r in rs)
        print(f'  {k:62s} {v:12,d}  {v - len(data):+12,d}')
    sys.stdout.flush()


if __name__ == '__main__':
    for name, planes in (('aridity.bin', 1), ('rain.bin', 3), ('temperature.bin', 0)):
        run(name, planes)
