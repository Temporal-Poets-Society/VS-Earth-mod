"""Build publication data from the full baked grids, without changing the input folder.

python tools/reduce-earth-data.py --source earthdata --output .local/publication-data/earthdata \
  --rawdata 'C:/VS mods/Earth/rawdata'

The 2' base terrain, rainfall, coastlines and all other geographic files are copied unchanged.
Existing worlds are outside this pre-release conversion's compatibility scope.
"""
import argparse
from concurrent.futures import ProcessPoolExecutor
import hashlib, json, pathlib, shutil, struct, time, zlib
import brotli
import numpy as np
import bake_summits as summits
from bake_chelsa import quantize_temperature, paeth_residual


def grid(data):
    version, w, h, tile, count = struct.unpack_from('<iiiii', data, 4)
    assert count == (w // tile) * (h // tile)
    return version, w, h, tile, [struct.unpack_from('<ii', data, 24 + 8 * i) for i in range(count)]


def unpaeth(a):
    return a.astype(np.int64).cumsum(0).cumsum(1).astype(a.dtype)


def summit_tile(job):
    key, blob, fine, minimum, fade = job
    old = np.frombuffer(brotli.decompress(blob), np.uint8).reshape(fine.shape)
    new = summits.filter_excess(old, fine, minimum, fade)
    assert not np.any(new[fine < minimum])
    assert np.array_equal(new[fine >= minimum + fade], old[fine >= minimum + fade])
    encoded = brotli.compress(new.tobytes(), quality=11, lgwin=24) if new.any() else b''
    if encoded:
        assert brotli.decompress(encoded) == new.tobytes()
    return key, encoded


def temperature_tile(job):
    key, blob, tile, step = job
    raw = brotli.decompress(blob)
    n = tile * tile
    planes = [unpaeth(np.frombuffer(raw[k * 2 * n:(k + 1) * 2 * n], '<i2').reshape(tile, tile)) for k in range(3)]
    q = [quantize_temperature(a, step) for a in planes]
    error = max(int(np.abs(a.astype(np.int32) - b).max()) for a, b in zip(planes, q)) / 10
    assert error <= step / 2 + 1e-8
    pred = [paeth_residual(a.astype(np.int32)).astype('<i2') for a in q]
    assert all(np.array_equal(unpaeth(p), a) for p, a in zip(pred, q))
    new = b''.join(p.tobytes() for p in pred) + raw[6 * n:]
    encoded = brotli.compress(new, quality=11, lgwin=24)
    assert brotli.decompress(encoded) == new
    assert new[6 * n:] == raw[6 * n:]  # unchanged coverage/fallback mask
    return key, encoded, error


def write_grid(original, blobs, target):
    count = struct.unpack_from('<i', original, 20)[0]
    data = bytearray(original[:24])
    offset = 24 + 8 * count
    for key in range(count):
        n = len(blobs.get(key, b''))
        data += struct.pack('<ii', offset, n)
        offset += n
    for key in range(count):
        data += blobs.get(key, b'')
    assert len(data) == offset
    target.write_bytes(data)
    co = zlib.compressobj(6, zlib.DEFLATED, -15)
    return {'bytes': len(data), 'zip_deflate_bytes': len(co.compress(data) + co.flush()),
            'sha256': hashlib.sha256(data).hexdigest(), 'stored_tiles': sum(bool(b) for b in blobs.values())}


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--source', type=pathlib.Path, default=pathlib.Path('earthdata'))
    ap.add_argument('--output', type=pathlib.Path, required=True)
    ap.add_argument('--rawdata', type=pathlib.Path, default=pathlib.Path('rawdata'))
    ap.add_argument('--minimum', type=int, default=3000)
    ap.add_argument('--fade', type=int, default=250)
    ap.add_argument('--temperature-step', type=float, default=0.5)
    ap.add_argument('--workers', type=int, default=6)
    args = ap.parse_args()
    source, output = args.source.resolve(), args.output.resolve()
    if source == output or output.exists():
        ap.error('output must be a new folder distinct from the source')
    if args.minimum < 0 or args.fade < 0 or args.workers < 1:
        ap.error('minimum/fade must be nonnegative and workers must be positive')
    quantize_temperature(np.zeros((1, 1)), args.temperature_step)
    metadata = json.loads((source / 'earth.json').read_text(encoding='utf-8'))
    if 'publicationData' in metadata:
        ap.error('input is already reduced; use the original full-data folder')
    data = (source / 'summits.bin').read_bytes()
    version, w, h, tile, entries = grid(data)
    assert data[:4] == b'EMSU' and (version, w, h, tile) == (2, 43200, 21600, 360)
    raster = summits.Raster(str(args.rawdata / summits.SRC), 120)
    output.mkdir(parents=True)
    for path in source.iterdir():
        if path.is_file() and path.name not in ('summits.bin', 'temperature.bin', 'earth.json'):
            shutil.copy2(path, output / path.name)
    report = {'summit_minimum_metres': args.minimum, 'summit_fade_metres': args.fade,
              'temperature_step_C': args.temperature_step, 'temperature_resolution_arcminutes': 2,
              'source_sha256': {name: hashlib.sha256((source / name).read_bytes()).hexdigest()
                                for name in ('summits.bin', 'temperature.bin', 'earth.json')}}
    blobs = {}
    start = time.time()
    with ProcessPoolExecutor(args.workers) as pool:
        for ty in range(h // tile):
            if not any(n for o, n in entries[ty * 120:(ty + 1) * 120]):
                continue
            fine = np.nan_to_num(raster.rows(ty * tile, (ty + 1) * tile), nan=0)
            jobs = []
            for tx in range(120):
                key = ty * 120 + tx
                off, n = entries[key]
                if n:
                    jobs.append((key, data[off:off + n], np.ascontiguousarray(fine[:, tx * tile:(tx + 1) * tile]), args.minimum, args.fade))
            for key, encoded in pool.map(summit_tile, jobs, chunksize=4):
                if encoded:
                    blobs[key] = encoded
            if ty % 10 == 9:
                print(f'Summit row {ty + 1}/60; {time.time() - start:.1f}s', flush=True)
        raster.pool.shutdown()
        raster.tif.close()
        report['summits'] = write_grid(data, blobs, output / 'summits.bin')
        data = (source / 'temperature.bin').read_bytes()
        version, w, h, tile, entries = grid(data)
        assert data[:4] == b'EMTP' and (version, w, h, tile) == (1, 10800, 5400, 180)
        blobs, max_error = {}, 0
        jobs = ((key, data[off:off + n], tile, args.temperature_step) for key, (off, n) in enumerate(entries))
        for i, (key, encoded, error) in enumerate(pool.map(temperature_tile, jobs, chunksize=4)):
            blobs[key] = encoded
            max_error = max(max_error, error)
            if i % 300 == 299:
                print(f'Temperature {i + 1}/{len(entries)}; {time.time() - start:.1f}s', flush=True)
        report['temperature'] = write_grid(data, blobs, output / 'temperature.bin')
        report['maximum_temperature_cell_change_C'] = max_error
    metadata['publicationData'] = {'summitMinimumElevationMetres': args.minimum, 'summitFadeMetres': args.fade,
                                   'temperatureStepC': args.temperature_step, 'geographicResolutionUnchanged': True}
    (output / 'earth.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    (output.parent / 'reduction-report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report), flush=True)


if __name__ == '__main__':
    main()
