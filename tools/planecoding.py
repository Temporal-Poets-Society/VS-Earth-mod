"""Predicted planes (EA-0077, code review 14 November 2026 ER-02): how Earth's client climate files are coded from
2.0.0-rc.14 on, as src/Core/PlaneCoding.cs reads them. The bakes (bake_rain.py, bake_aridity.py, bake_chelsa.py,
bake_earth.py) write their files through the writers here, and tools/repack-client-data.py turns the plain files into
these, checking every value.

A header's version int carries the coding in its high 16 bits (0 plain brotli, 1 predicted planes) and the content
version in its low 16. A block (before brotli) is, per plane, three bytes (predictor, step, reference plane + 1 or 0),
then each plane's residuals row by row: one byte a cell for 8-bit planes; for 16-bit planes the low bytes of all cells,
then the high bytes, zigzag-coded. A cell's value v (in its type's range) becomes d = wrap(v - ref), q = d / step; the
predictor guesses q from the west, north and north-west cells (the first row from the west, the first column from the
north, the first cell 0); the residual is q - guess modulo 2^bits. Every step is exact integer arithmetic.

Pure numpy and brotli (the Earth venv): .venv/Scripts/python.exe -I.
"""
import hashlib, math, os, struct
from concurrent.futures import ProcessPoolExecutor

import brotli
import numpy as np

PLAIN, PREDICTED = 0, 1
U8, I8, I16, U16 = 0, 1, 2, 3
NONE, LEFT, UP, MED, AVERAGE, PLANAR = range(6)
PREDICTORS = (NONE, LEFT, UP, MED, AVERAGE, PLANAR)
BITS = {U8: 8, I8: 8, I16: 16, U16: 16}
SIGNED = {U8: False, I8: True, I16: True, U16: False}
DTYPE = {U8: np.dtype(np.uint8), I8: np.dtype(np.int8), I16: np.dtype('<i2'), U16: np.dtype('<u2')}
QUALITY, LGWIN = 11, 24


def sha8(data):
    """The first 8 bytes of SHA-256 as a little-endian number, as EarthBinary.Sha8."""
    return struct.unpack('<Q', hashlib.sha256(data).digest()[:8])[0]


def header_version(content, coding):
    return content | coding << 16


def split_version(v):
    return v & 0xFFFF, (v & 0xFFFFFFFF) >> 16


def wrap(v, t):
    """Values wrapped into the type's range (modulo 2^bits), as int64."""
    bits = BITS[t]
    v = np.asarray(v, np.int64) & ((1 << bits) - 1)
    if SIGNED[t]:
        v = np.where(v >= 1 << (bits - 1), v - (1 << bits), v)
    return v


def guess(q, pred):
    """The predictor's guess for every cell of q (2-D int64), from the cells' true neighbours (the encoder's view)."""
    g = np.zeros_like(q)
    if pred == NONE:
        return g
    W = np.zeros_like(q); W[:, 1:] = q[:, :-1]
    N = np.zeros_like(q); N[1:] = q[:-1]
    NW = np.zeros_like(q); NW[1:, 1:] = q[:-1, :-1]
    if pred == LEFT:
        g = W
    elif pred == UP:
        g = N
    elif pred == MED:
        lo, hi = np.minimum(W, N), np.maximum(W, N)
        g = np.where(NW >= hi, lo, np.where(NW <= lo, hi, W + N - NW))
    elif pred == AVERAGE:
        g = (W + N) >> 1
    elif pred == PLANAR:
        g = W + N - NW
    else:
        raise ValueError(pred)
    g = g.copy()
    g[0, :] = W[0, :]
    g[1:, 0] = N[1:, 0]
    g[0, 0] = 0
    return g


def residual_bytes(q, t, pred):
    """A plane's residual bytes for predictor pred (q: its values after reference and step, 2-D, in the type's range)."""
    r = (q - guess(q, pred)) & ((1 << BITS[t]) - 1)
    if BITS[t] == 8:
        return r.astype(np.uint8).tobytes()
    s = np.where(r >= 1 << 15, r - (1 << 16), r)
    z = ((s << 1) ^ (s >> 63)) & 0xFFFF
    return (z & 255).astype(np.uint8).tobytes() + (z >> 8).astype(np.uint8).tobytes()


def _compressed(b):
    return len(brotli.compress(b, quality=QUALITY, lgwin=LGWIN))


def auto_step(d):
    """The largest step (1..255) every value of d is a multiple of."""
    g = int(np.gcd.reduce(np.abs(d).ravel())) if d.size else 0
    return g if 1 < g <= 255 else 1


def encode_block(planes, types, refs=None, steps=None, preds=None):
    """Brotli of a block of predicted planes. planes: 2-D arrays of equal shape, each in the range of its type in types;
    refs: per plane None, the index of an earlier plane of the same type to subtract first, or a list of such choices
    (None among them for none) to take the one that compresses the plane smallest; steps: per plane an int, or 'auto' for
    the largest common step; preds: per plane a predictor, or None to take the one that compresses the plane smallest on
    its own. Returns (bytes, the chosen (pred, step, ref) per plane)."""
    k = len(planes)
    refs = refs or [None] * k
    steps = steps or [1] * k
    preds = preds or [None] * k
    head, body, chosen = bytearray(), [], []
    for p, (v, t) in enumerate(zip(planes, types)):
        v = np.asarray(v).astype(np.int64)
        if not np.array_equal(wrap(v, t), v):
            raise ValueError(f'plane {p} holds values outside its type')
        best = None
        for ref in (refs[p] if isinstance(refs[p], (list, tuple)) else [refs[p]]):
            if ref is not None:
                if not 0 <= ref < p or types[ref] != t:
                    raise ValueError(f'plane {p}: reference plane {ref} must be an earlier plane of the same type')
                d = wrap(v - np.asarray(planes[ref]).astype(np.int64), t)
            else:
                d = v
            step = auto_step(d) if steps[p] == 'auto' else int(steps[p])
            if not 1 <= step <= 255 or (d % step).any():
                raise ValueError(f'plane {p}: step {step} does not divide every value')
            q = d // step
            for pred in (PREDICTORS if preds[p] is None else [preds[p]]):
                res = residual_bytes(q, t, pred)
                size = _compressed(res) if preds[p] is None or isinstance(refs[p], (list, tuple)) else 0
                if best is None or size < best[0]:
                    best = (size, pred, step, ref, res)
        _, pred, step, ref, res = best
        head += bytes((pred, step, 0 if ref is None else ref + 1))
        body.append(res)
        chosen.append((pred, step, ref))
    return brotli.compress(bytes(head) + b''.join(body), quality=QUALITY, lgwin=LGWIN), chosen


def _unpredict(delta, pred, t):
    """The values q of one plane from its residuals (delta: 2-D int64, the residuals as signed or byte numbers)."""
    rows, cols = delta.shape
    if pred == NONE:
        return wrap(delta, t)
    if pred == LEFT:
        q = delta.copy()
        q[:, 0] = np.cumsum(delta[:, 0])          # the first column from the north (the first cell from 0)
        return wrap(np.cumsum(q, axis=1), t)
    if pred == UP:
        q = delta.copy()
        q[0, :] = np.cumsum(delta[0, :])          # the first row from the west
        return wrap(np.cumsum(q, axis=0), t)
    if pred == PLANAR:                            # the 2-D prefix sum, boundaries included
        return wrap(np.cumsum(np.cumsum(delta, axis=0), axis=1), t)
    if pred not in (MED, AVERAGE):
        raise ValueError(f'unknown predictor {pred}')
    # MED and the average depend on the west cell just decoded: one anti-diagonal at a time (its cells only need the
    # diagonals before it)
    q = np.zeros((rows, cols), np.int64)
    bits = BITS[t]
    for k in range(rows + cols - 1):
        r0, r1 = max(0, k - cols + 1), min(rows - 1, k)
        r = np.arange(r0, r1 + 1)
        c = k - r
        g = np.zeros(len(r), np.int64)
        inner = (r > 0) & (c > 0)
        top = (r == 0) & (c > 0)
        left = (c == 0) & (r > 0)
        if top.any():
            g[top] = q[r[top], c[top] - 1]
        if left.any():
            g[left] = q[r[left] - 1, c[left]]
        if inner.any():
            ri, ci = r[inner], c[inner]
            w, n = q[ri, ci - 1], q[ri - 1, ci]
            if pred == AVERAGE:
                g[inner] = (w + n) >> 1
            else:
                nw = q[ri - 1, ci - 1]
                lo, hi = np.minimum(w, n), np.maximum(w, n)
                g[inner] = np.where(nw >= hi, lo, np.where(nw <= lo, hi, w + n - nw))
        q[r, c] = wrap(g + delta[r, c], t)
    return q


def decode_block(blob, rows, cols, types):
    """A block back to its planes (int64 arrays in each type's range), as PlaneCoding.Decode reads it."""
    raw = brotli.decompress(blob)
    n, k = rows * cols, len(types)
    size = 3 * k + sum(n * BITS[t] // 8 for t in types)
    if len(raw) != size:
        raise ValueError(f'predicted planes of {len(raw)} bytes, expected {size}')
    at, out = 3 * k, []
    for p, t in enumerate(types):
        pred, step, ref = raw[3 * p], raw[3 * p + 1], raw[3 * p + 2] - 1
        if pred > PLANAR or step == 0 or ref >= p or (ref >= 0 and types[ref] != t):
            raise ValueError(f'plane {p} has an unknown coding')
        if BITS[t] == 8:
            delta = np.frombuffer(raw, np.uint8, n, at).astype(np.int64)
            at += n
        else:
            z = np.frombuffer(raw, np.uint8, n, at).astype(np.int64) | np.frombuffer(raw, np.uint8, n, at + n).astype(np.int64) << 8
            delta = (z >> 1) ^ -(z & 1)
            at += 2 * n
        q = _unpredict(delta.reshape(rows, cols), pred, t)
        v = q * step
        if ref >= 0:
            v = v + out[ref]
        out.append(wrap(v, t))
    return out


def raw_bytes(planes, types):
    """The plain files' layout of these planes: each little-endian at its type's width, one after the other."""
    return b''.join(np.asarray(v).astype(DTYPE[t]).tobytes() for v, t in zip(planes, types))


def planes_of(raw, rows, cols, types):
    """A plain file's planes (int64) from its decompressed bytes."""
    n, at, out = rows * cols, 0, []
    for t in types:
        size = n * DTYPE[t].itemsize
        out.append(np.frombuffer(raw, DTYPE[t], n, at).astype(np.int64).reshape(rows, cols))
        at += size
    if at != len(raw):
        raise ValueError(f'{len(raw)} bytes for planes of {at}')
    return out


# ------------------------------------------------------------------------------------------------ tiled grids

def _encode_tile(job):
    planes, types, refs, steps = job
    if planes is None:
        return b''
    blob, _ = encode_block(planes, types, refs, steps)
    if not all(np.array_equal(a, b) for a, b in zip(decode_block(blob, *planes[0].shape, types), planes)):
        raise AssertionError('a predicted tile did not decode to its values')
    return blob


def write_tiled(path, magic, version, width, height, tile, tiles, types, refs=None, steps=None, coding=PREDICTED, workers=None):
    """A TiledGrid file (src/Core/TiledGrid.cs): header magic, version (coding << 16 | content version), width, height,
    tile, tile count, then (offset, length) per tile from the north-west and the tiles. tiles: per tile (row-major)
    None for a tile without data, or its planes (tile x tile arrays). Plain coding is brotli of the raw planes; predicted
    coding, a block of predicted planes (each tile checked to decode to its values). Returns the file's size."""
    if coding == PLAIN:
        blobs = [b'' if t is None else brotli.compress(raw_bytes(t, types), quality=QUALITY, lgwin=LGWIN) for t in tiles]
    else:
        jobs = [(None if t is None else [np.asarray(p).astype(np.int64) for p in t], types, refs, steps) for t in tiles]
        with ProcessPoolExecutor(workers or max(1, min(16, (os.cpu_count() or 2) - 2))) as pool:
            blobs = list(pool.map(_encode_tile, jobs, chunksize=4))
    with open(path, 'wb') as f:
        f.write(magic.encode('ascii'))
        f.write(struct.pack('<iiiii', header_version(version, coding), width, height, tile, len(blobs)))
        off = 4 + 20 + 8 * len(blobs)
        for b in blobs:
            f.write(struct.pack('<ii', off, len(b)))
            off += len(b)
        for b in blobs:
            f.write(b)
    return os.path.getsize(path)


class TiledFile:
    """A TiledGrid file read back, either coding: planes(key) gives a tile's planes (int64) or None for a tile without
    data."""

    def __init__(self, data, types):
        self.data, self.types = data, types
        self.magic = data[:4].decode('ascii')
        hv, self.width, self.height, self.tile, count = struct.unpack_from('<iiiii', data, 4)
        self.version, self.coding = split_version(hv)
        self.entries = [struct.unpack_from('<ii', data, 24 + 8 * i) for i in range(count)]

    def tile_blob(self, key):
        off, n = self.entries[key]
        return self.data[off:off + n]

    def planes(self, key):
        off, n = self.entries[key]
        if n == 0:
            return None
        if self.coding == PLAIN:
            return planes_of(brotli.decompress(self.data[off:off + n]), self.tile, self.tile, self.types)
        if self.coding == PREDICTED:
            return decode_block(self.data[off:off + n], self.tile, self.tile, self.types)
        raise ValueError(f'{self.magic}: unknown coding {self.coding}')


# ------------------------------------------------------------------------------------------------ whole grids

CLIMATE_TYPES = [I16, I16, I16, U16, U8, U8]


def write_climate(path, planes, version=3, coding=PREDICTED):
    """climate.bin: header EMCL, version, width, height, then brotli of the planes (yearly mean, coldest and warmest month
    in °C x 100 as i16, precipitation mm as u16, rainfall 0..255 and summer dryness x 255 as u8). Predicted: the coldest
    and warmest month as differences from the mean where that is smaller."""
    types = CLIMATE_TYPES[:len(planes)]
    H, W = planes[0].shape
    if coding == PLAIN:
        blob = brotli.compress(raw_bytes(planes, types), quality=QUALITY, lgwin=LGWIN)
    else:
        blob, _ = encode_block([np.asarray(p).astype(np.int64) for p in planes], types, refs=[None, [None, 0], [None, 0], None, None, None][:len(planes)])
        back = decode_block(blob, H, W, types)
        assert all(np.array_equal(a, b) for a, b in zip(back, planes)), 'climate.bin did not decode to its values'
    with open(path, 'wb') as f:
        f.write(b'EMCL')
        f.write(struct.pack('<iii', header_version(version, coding), W, H))
        f.write(blob)
    return os.path.getsize(path)


def read_climate(path):
    """climate.bin's planes as stored (int64 arrays), either coding, and its content version."""
    data = open(path, 'rb').read()
    assert data[:4] == b'EMCL', path
    hv, W, H = struct.unpack_from('<iii', data, 4)
    version, coding = split_version(hv)
    types = CLIMATE_TYPES if version >= 3 else CLIMATE_TYPES[:5]
    if coding == PLAIN:
        return planes_of(brotli.decompress(data[16:]), H, W, types), version
    if coding == PREDICTED:
        return decode_block(data[16:], H, W, types), version
    raise ValueError(f'climate.bin: unknown coding {coding}')


WEATHER_MONTHS = 12
WEATHER_TYPES = [U8] * 12 + [U8] * 12 + [U8] + [I8] * 12 + [I8] * 12 + [U8] * 12


def fnv1a(data):
    """FNV-1a of the bytes, as WeatherNoise.Fnv1a: weather.bin's signature."""
    h = 0xCBF29CE484222325
    for b in data:
        h = ((h ^ b) * 0x100000001B3) & 0xFFFFFFFFFFFFFFFF
    return h


def write_weather(path, planes, version=2, signature=None, coding=PREDICTED):
    """weather.bin: header EMWX, version, width, height, months; plain: brotli of the planes (decision 0017, bake_earth
    write_weather). Predicted: then the u64 signature (FNV-1a of the plain file these planes were made into, which worlds
    froze when they were made, EarthParams.WeatherDataMade), the u64 sha8 of the planes in the plain layout (the reader
    checks the decoded planes against it, so the signature stays tied to the values) and the block. A new bake writes the
    plain file first and passes its signature."""
    types = WEATHER_TYPES[:len(planes)]
    H, W = planes[0].shape
    with open(path, 'wb') as f:
        f.write(b'EMWX')
        f.write(struct.pack('<iiii', header_version(version, coding), W, H, WEATHER_MONTHS))
        if coding == PLAIN:
            f.write(brotli.compress(raw_bytes(planes, types), quality=QUALITY, lgwin=LGWIN))
        else:
            if signature is None:
                raise ValueError('a predicted weather.bin needs the plain file\'s signature')
            # each month's plane may go against the month before (it pays for the temperatures)
            firsts = {0, 12, 24, 25, 37, 49}
            refs = [None if i in firsts else [None, i - 1] for i in range(len(planes))]
            blob, _ = encode_block([np.asarray(p).astype(np.int64) for p in planes], types, refs=refs)
            back = decode_block(blob, H, W, types)
            assert all(np.array_equal(a, b) for a, b in zip(back, planes)), 'weather.bin did not decode to its values'
            f.write(struct.pack('<QQ', signature, sha8(raw_bytes(planes, types))))
            f.write(blob)
    return os.path.getsize(path)


def read_weather(path):
    """weather.bin's planes (int64) and its signature (int), either coding."""
    data = open(path, 'rb').read()
    assert data[:4] == b'EMWX', path
    hv, W, H, months = struct.unpack_from('<iiii', data, 4)
    version, coding = split_version(hv)
    types = WEATHER_TYPES if version >= 2 else WEATHER_TYPES[:25]
    if coding == PLAIN:
        return planes_of(brotli.decompress(data[20:]), H, W, types), fnv1a(data)
    if coding == PREDICTED:
        sig, content = struct.unpack_from('<QQ', data, 20)
        planes = decode_block(data[36:], H, W, types)
        if sha8(raw_bytes(planes, types)) != content:
            raise ValueError(f'{path}: its planes are not the ones its signature was made of')
        return planes, sig
    raise ValueError(f'weather.bin: unknown coding {coding}')


# ------------------------------------------------------------------------------------------------ climate fixes

def _diff_bytes(d, t):
    """Differences (int64, wrapped to the type) as stored: 8-bit as bytes, 16-bit zigzag, low bytes then high bytes."""
    if BITS[t] == 8:
        return (d & 255).astype(np.uint8).tobytes()
    r = np.asarray(d, np.int64) & 0xFFFF
    s = np.where(r >= 1 << 15, r - (1 << 16), r)          # as a signed 16-bit number, whatever the plane's type
    z = ((s << 1) ^ (s >> 63)) & 0xFFFF
    return (z & 255).astype(np.uint8).tobytes() + (z >> 8).astype(np.uint8).tobytes()


def climate_base(climate):
    """The sha8 of climate.bin's planes in the plain layout (EarthDataset's climateBase): which climate.bin a predicted
    fix is against."""
    return sha8(raw_bytes(climate, CLIMATE_TYPES[:len(climate)]))


def write_climate_fix(path, version, width, height, idx, new, base, mask=0x3F, coding=None, climate=None):
    """climatefix*.bin (bake_earth write_climate_fix). idx: the changed cells (ascending); new: per plane in the mask the
    new values at idx; base: per plane the values the fix changes at idx (climate.bin's for versions 1 and 2); climate:
    climate.bin's whole planes, for a predicted fix (its header names them by climate_base).
    Versions 1 and 2, plain: header EMCF, version, width, height, count, then brotli of the cell indices (i32) and the
    six planes' new values. Predicted (EA-0077, the default for versions 1 and 2): the same header with the coding in the
    version and then the u64 climate_base of the climate.bin it is against (the reader refuses another), then brotli of a
    bitmap of the cells (one bit a cell from the north-west, the high bit first) and each plane's
    new values as differences from climate.bin's at those cells, wrapped to the plane's type (16-bit planes zigzag, low
    bytes then high bytes). Versions 3 and later (layered, against the version before) stay plain: header plus the plane
    mask, the indices as steps from the one before and the masked planes' values."""
    idx = np.asarray(idx, np.int64)
    if len(idx) and ((np.diff(idx) <= 0).any() or idx[0] < 0 or idx[-1] >= width * height):
        raise ValueError('climate fix cells must be ascending and on the grid')
    layered = version >= 3
    if coding is None:
        coding = PLAIN if layered else PREDICTED
    types = CLIMATE_TYPES
    with open(path, 'wb') as f:
        f.write(b'EMCF')
        if layered:
            if coding != PLAIN:
                raise ValueError('layered climate fixes are plain')
            steps = np.diff(np.concatenate([[0], idx])).astype('<i4')
            raw = steps.tobytes() + b''.join(np.asarray(new[p]).astype(DTYPE[types[p]]).tobytes() for p in range(6) if mask >> p & 1)
            f.write(struct.pack('<iiiii', version, width, height, len(idx), mask))
        elif coding == PLAIN:
            raw = idx.astype('<i4').tobytes() + b''.join(np.asarray(new[p]).astype(DTYPE[types[p]]).tobytes() for p in range(6))
            f.write(struct.pack('<iiii', version, width, height, len(idx)))
        else:
            bitmap = np.zeros(width * height, np.uint8)
            bitmap[idx] = 1
            raw = np.packbits(bitmap).tobytes() + b''.join(
                _diff_bytes(wrap(np.asarray(new[p], np.int64) - np.asarray(base[p], np.int64), types[p]), types[p]) for p in range(6))
            if climate is None:
                raise ValueError('a predicted climate fix needs climate.bin\'s planes')
            f.write(struct.pack('<iiiiQ', header_version(version, PREDICTED), width, height, len(idx), climate_base(climate)))
        f.write(brotli.compress(raw, quality=QUALITY, lgwin=LGWIN))
    return os.path.getsize(path)


def read_climate_fix(path, climate=None):
    """A climate fix as (version, idx, mask, values per plane or None). A predicted fix needs climate.bin's planes
    (read_climate) as its base."""
    data = open(path, 'rb').read()
    assert data[:4] == b'EMCF', path
    hv, w, h, count = struct.unpack_from('<iiii', data, 4)
    version, coding = split_version(hv)
    layered = version >= 3
    types = CLIMATE_TYPES
    if layered:
        mask = struct.unpack_from('<i', data, 20)[0]
        raw = brotli.decompress(data[24:])
        idx = np.frombuffer(raw, '<i4', count).astype(np.int64).cumsum()
    elif coding == PLAIN:
        mask = 0x3F
        raw = brotli.decompress(data[20:])
        idx = np.frombuffer(raw, '<i4', count).astype(np.int64)
    else:
        if climate is None:
            raise ValueError('a predicted climate fix needs climate.bin')
        mask = 0x3F
        if struct.unpack_from('<Q', data, 20)[0] != climate_base(climate):
            raise ValueError(f'{path}: differences from another climate.bin')
        raw = brotli.decompress(data[28:])
        nb = (w * h + 7) // 8
        idx = np.flatnonzero(np.unpackbits(np.frombuffer(raw, np.uint8, nb))[:w * h]).astype(np.int64)
        assert len(idx) == count, f'{path}: {len(idx)} cells in the bitmap, {count} in the header'
        vals, at = [], nb
        for p, t in enumerate(types):
            if BITS[t] == 8:
                d = np.frombuffer(raw, np.uint8, count, at).astype(np.int64)
                at += count
            else:
                z = np.frombuffer(raw, np.uint8, count, at).astype(np.int64) | np.frombuffer(raw, np.uint8, count, at + count).astype(np.int64) << 8
                d = (z >> 1) ^ -(z & 1)
                at += 2 * count
            vals.append(wrap(np.asarray(climate[p]).ravel()[idx] + d, t))
        assert at == len(raw)
        return version, idx, mask, vals
    at, vals = 4 * count, []
    for p, t in enumerate(types):
        if not mask >> p & 1:
            vals.append(None)
            continue
        vals.append(np.frombuffer(raw, DTYPE[t], count, at).astype(np.int64))
        at += count * DTYPE[t].itemsize
    assert at == len(raw)
    return version, idx, mask, vals
