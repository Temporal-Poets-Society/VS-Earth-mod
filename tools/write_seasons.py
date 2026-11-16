"""
Writes the monthly temperature layer (EA-0144, the weather session's tools/bake_seasons.py bakes the values) into its
data-release file, seasons-<version>.bin: a TiledGrid (src/Core/TiledGrid.cs) of predicted planes (PlaneCoding,
tools/planecoding.py) on Earth's 2' terrain grid, read by src/Core/SeasonData.cs.

Planes of a cell, in this order (37):
  - mean[0..11]: int16, the month's mean air temperature in 0.1 C units, reduced to sea level (multiples of 5: 0.5 C);
  - range[0..11]: uint8, the month's mean daily maximum minus minimum, in 0.2 C units;
  - spread[0..11]: uint8, the standard deviation of the daily mean within the month, in 0.1 C units;
  - provenance: uint8 bits, 1 = mean measured, 2 = range measured, 4 = spread measured (0: filled by the bake).
Each month's plane may be coded as the difference from the previous month's plane of its kind; per tile the coder
takes that or none, and the spatial predictor, whichever compresses the plane smallest. The coding is exact: the reader
gets the very integers of the bake.

Input: the bake's .npz with mean int16 [12, 5400, 10800], range uint8 [12, ...], spread uint8 [12, ...] and provenance
uint8 [5400, 10800] (or measured bool for bit 0 only). With --means-only (the first bake, before the range and spread
sources) only the means and the provenance are written, to measure size and decode time.

    .venv/Scripts/python -I tools/write_seasons.py <bake.npz> <out.bin> [--tile 90] [--means-only] [--sample N]
    .venv/Scripts/python -I tools/write_seasons.py --synthetic tests/fixtures/seasons-synthetic.bin

90-cell tiles (3 degrees): a decoded tile is 397 kB, against 1.6 MB at 180 cells, for about 10 % more file (measured on
the means of 16 November 2026: 22.9 against 20.8 MB). --synthetic writes the test fixture of tests/SeasonDataTests: four
tiles (a 2 x 2 block) of values from SYNTHETIC below, every other tile empty.
"""
import argparse, os, sys, time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import planecoding as pc

MAGIC = 'EMSN'
VERSION = 1
WIDTH, HEIGHT = 10800, 5400
T0 = time.time()


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


def load(path, means_only):
    z = np.load(path)
    mean = z['mean'] if 'mean' in z.files else z['means']
    if mean.shape != (12, HEIGHT, WIDTH) or mean.dtype != np.int16:
        raise SystemExit(f'mean: {mean.dtype} {mean.shape}, expected int16 (12, {HEIGHT}, {WIDTH})')
    if 'provenance' in z.files:
        prov = z['provenance'].astype(np.uint8)
    elif 'measured' in z.files:
        prov = z['measured'].astype(np.uint8)
    else:
        raise SystemExit('no provenance (or measured) plane')
    planes = {'mean': mean, 'provenance': prov}
    if not means_only:
        for k in ('range', 'spread'):
            a = z[k]
            if a.shape != (12, HEIGHT, WIDTH) or a.dtype != np.uint8:
                raise SystemExit(f'{k}: {a.dtype} {a.shape}, expected uint8 (12, {HEIGHT}, {WIDTH})')
            planes[k] = a
    return planes


def layout(means_only):
    """The planes' types and the reference candidates of each (indices of earlier planes; None for none)."""
    types, refs = [], []
    kinds = [('mean', pc.I16)] + ([] if means_only else [('range', pc.U8), ('spread', pc.U8)])
    for _, t in kinds:
        base = len(types)
        for m in range(12):
            types.append(t)
            refs.append([None] if m == 0 else [None, base + m - 1])
    types.append(pc.U8)
    refs.append([None])
    return kinds, types, refs


def tile_planes(planes, kinds, ty, tx, tile):
    sl = (slice(ty * tile, (ty + 1) * tile), slice(tx * tile, (tx + 1) * tile))
    out = []
    for k, _ in kinds:
        out += [np.ascontiguousarray(planes[k][m][sl]) for m in range(12)]
    out.append(np.ascontiguousarray(planes['provenance'][sl]))
    return out


SYNTHETIC_TILE = 90
SYNTHETIC_TILES = [(10, 20), (10, 21), (11, 20), (11, 21)]   # (tile row, tile column)


def synthetic(kind, m, r, c):
    """The fixture's value of a cell (r, c are grid rows and columns); tests/SeasonDataTests computes the same."""
    if kind == 'mean':
        return 5 * ((r * 7 + c * 3 + m * 11) % 400 - 200)
    if kind == 'range':
        return (r + c + 3 * m) % 200
    if kind == 'spread':
        return (2 * r + c + m) % 100
    return (r + c) % 8


def write_synthetic(out):
    t = SYNTHETIC_TILE
    kinds, types, refs = layout(False)
    tiles = [None] * ((HEIGHT // t) * (WIDTH // t))
    for ty, tx in SYNTHETIC_TILES:
        r = np.arange(ty * t, (ty + 1) * t)[:, None]
        c = np.arange(tx * t, (tx + 1) * t)[None, :]
        planes = []
        for k, dtype in (('mean', np.int16), ('range', np.uint8), ('spread', np.uint8)):
            planes += [np.broadcast_to(synthetic(k, m, r, c), (t, t)).astype(dtype) for m in range(12)]
        planes.append(np.broadcast_to(synthetic('provenance', 0, r, c), (t, t)).astype(np.uint8))
        tiles[ty * (WIDTH // t) + tx] = planes
    steps = ['auto'] * (len(types) - 1) + [1]
    size = pc.write_tiled(out, MAGIC, VERSION, WIDTH, HEIGHT, t, tiles, types, refs=refs, steps=steps)
    log(f'{out}: {size:,} bytes, the synthetic fixture')


def main():
    if len(sys.argv) == 3 and sys.argv[1] == '--synthetic':
        write_synthetic(sys.argv[2])
        return
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('bake')
    ap.add_argument('out')
    ap.add_argument('--tile', type=int, default=90)
    ap.add_argument('--means-only', action='store_true')
    ap.add_argument('--sample', type=int, default=0, help='encode only every Nth tile (a size estimate; the file is not usable)')
    args = ap.parse_args()
    if WIDTH % args.tile or HEIGHT % args.tile:
        raise SystemExit(f'tile {args.tile} does not divide the grid')
    planes = load(args.bake, args.means_only)
    kinds, types, refs = layout(args.means_only)
    tiles = []
    n = 0
    for ty in range(HEIGHT // args.tile):
        for tx in range(WIDTH // args.tile):
            n += 1
            if args.sample and (n - 1) % args.sample:
                tiles.append(None)
                continue
            tiles.append(tile_planes(planes, kinds, ty, tx, args.tile))
    log(f'{sum(t is not None for t in tiles)} of {len(tiles)} tiles of {args.tile} cells, {len(types)} planes')
    steps = ['auto'] * (len(types) - 1) + [1]
    size = write_batched(args.out, args.tile, tiles, types, refs, steps)
    coded = sum(t is not None for t in tiles)
    log(f'{args.out}: {size / 1e6:.2f} MB' + (f' for {coded} tiles; the whole grid about {size * len(tiles) / coded / 1e6:.1f} MB' if args.sample else ''))


def _encode(job):
    """One tile in a worker: its planes (views in their own types) widened there, coded, and checked to decode to them."""
    planes, types, refs, steps = job
    if planes is None:
        return b''
    return pc._encode_tile(([np.asarray(p).astype(np.int64) for p in planes], types, refs, steps))


def write_batched(path, tile, tiles, types, refs, steps, batch=256):
    """planecoding.write_tiled's file, the tiles coded a batch at a time: write_tiled widens every tile's planes to int64
    before it starts, which for the 37 planes of 7,200 tiles is about 17 GB."""
    from concurrent.futures import ProcessPoolExecutor
    import struct
    blobs = []
    workers = max(1, min(16, (os.cpu_count() or 2) - 2))
    with ProcessPoolExecutor(workers) as pool:
        for at in range(0, len(tiles), batch):
            jobs = [(t, types, refs, steps) for t in tiles[at:at + batch]]
            blobs += list(pool.map(_encode, jobs, chunksize=4))
            log(f'{min(at + batch, len(tiles))} of {len(tiles)} tiles coded, {sum(map(len, blobs)) / 1e6:.1f} MB so far')
    with open(path, 'wb') as f:
        f.write(MAGIC.encode('ascii'))
        f.write(struct.pack('<iiiii', pc.header_version(VERSION, pc.PREDICTED), WIDTH, HEIGHT, tile, len(blobs)))
        off = 4 + 20 + 8 * len(blobs)
        for b in blobs:
            f.write(struct.pack('<ii', off, len(b)))
            off += len(b)
        for b in blobs:
            f.write(b)
    return os.path.getsize(path)


if __name__ == '__main__':
    main()
