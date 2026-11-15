"""Turn the plain client climate files into predicted planes (EA-0077, code review 14 November 2026 ER-02).

rain.bin, aridity.bin, temperature.bin, climate.bin, climatefix.bin, climatefix2.bin and weather.bin travel in the mod zip
with the DLL that reads them. Their plain form is brotli of the raw planes; tools/planecoding.py codes the same values
with a spatial predictor in front of brotli. This reads each plain file, writes it predicted, reads the new file back
and checks that every value is the plain file's, then replaces the file (or writes to --output). Files already predicted
are left as they are. weather.bin keeps the plain file's signature (worlds froze it, EarthParams.WeatherDataMade).
climatefix3.bin and climatefix4.bin (layered, small) stay plain.

    .venv/Scripts/python.exe -I tools/repack-client-data.py [--data earthdata] [--output folder] [--only rain,weather]
"""
import argparse, hashlib, json, os, pathlib, shutil, struct, sys, time

import brotli
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import planecoding as pc

TILED = {'rain.bin': ('EMRN', [pc.U8] * 3, None), 'aridity.bin': ('EMAI', [pc.U8], None),
         'temperature.bin': ('EMTP', [pc.I16, pc.I16, pc.I16, pc.U8], ['auto', 'auto', 'auto', 1])}


def coding_of(path):
    return pc.split_version(struct.unpack_from('<i', open(path, 'rb').read(8), 4)[0])[1]


def temperature_values(planes):
    """A plain temperature.bin tile's planes (Paeth residuals) as the values FineTemperature.UnPaeth makes of them."""
    return [pc.wrap(np.cumsum(np.cumsum(p, axis=0), axis=1), pc.I16) for p in planes[:3]] + [planes[3]]


def tiled(src, dst, name):
    magic, types, steps = TILED[name]
    old = pc.TiledFile(open(src, 'rb').read(), types)
    assert old.magic == magic and old.coding == pc.PLAIN, (name, old.magic, old.coding)
    tiles = []
    for key in range(len(old.entries)):
        t = old.planes(key)
        if t is not None and name == 'temperature.bin':
            t = temperature_values(t)
        tiles.append(t)
    pc.write_tiled(dst, magic, old.version, old.width, old.height, old.tile, tiles, types, steps=steps)
    new = pc.TiledFile(open(dst, 'rb').read(), types)
    assert (new.magic, new.version, new.coding, new.width, new.height, new.tile) == (magic, old.version, pc.PREDICTED, old.width, old.height, old.tile)
    for key, t in enumerate(tiles):
        b = new.planes(key)
        assert (t is None) == (b is None), (name, key)
        if t is not None:
            assert all(np.array_equal(x, y) for x, y in zip(t, b)), (name, key)
    return {'tiles': len(tiles), 'tiles with data': sum(t is not None for t in tiles)}


def climate(src, dst):
    planes, version = pc.read_climate(src)
    assert coding_of(src) == pc.PLAIN
    pc.write_climate(dst, planes, version)
    back, v2 = pc.read_climate(dst)
    assert v2 == version and all(np.array_equal(a, b) for a, b in zip(planes, back))
    return {'version': version}


def fix(src, dst, climate_planes):
    data = open(src, 'rb').read()
    hv, w, h, count = struct.unpack_from('<iiii', data, 4)
    version, idx, mask, vals = pc.read_climate_fix(src)
    assert version in (1, 2) and mask == 0x3F and coding_of(src) == pc.PLAIN
    base = [np.asarray(p).ravel()[idx] for p in climate_planes]
    pc.write_climate_fix(dst, version, w, h, idx, vals, base, climate=climate_planes)
    v2, idx2, mask2, vals2 = pc.read_climate_fix(dst, climate_planes)
    assert v2 == version and np.array_equal(idx, idx2) and all(np.array_equal(a, b) for a, b in zip(vals, vals2))
    changed = [int((np.asarray(v) != b).sum()) for v, b in zip(vals, base)]
    return {'version': version, 'cells': int(count), 'cells changed per plane': changed}


def weather(src, dst):
    planes, signature = pc.read_weather(src)
    assert coding_of(src) == pc.PLAIN
    version = pc.split_version(struct.unpack_from('<i', open(src, 'rb').read(8), 4)[0])[0]
    pc.write_weather(dst, planes, version, signature)
    back, sig2 = pc.read_weather(dst)
    assert sig2 == signature and all(np.array_equal(a, b) for a, b in zip(planes, back))
    return {'signature': f'{signature:016x}'}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--data', type=pathlib.Path, default=pathlib.Path(__file__).resolve().parent.parent / 'earthdata')
    ap.add_argument('--output', type=pathlib.Path, help='write here instead of replacing the files')
    ap.add_argument('--only', help='comma-separated file stems (rain, aridity, temperature, climate, climatefix, climatefix2, weather)')
    args = ap.parse_args()
    data = args.data.resolve()
    out = (args.output or data).resolve()
    out.mkdir(parents=True, exist_ok=True)
    names = ['rain.bin', 'aridity.bin', 'temperature.bin', 'climate.bin', 'climatefix.bin', 'climatefix2.bin', 'weather.bin']
    if args.only:
        names = [n for n in names if n[:-4] in args.only.split(',')]
    report = {}
    climate_planes = None
    for name in names:
        src = data / name
        if coding_of(src) != pc.PLAIN:
            print(f'{name}: already predicted, left as it is', flush=True)
            continue
        start = time.time()
        tmp = out / (name + '.repack')
        if name in TILED:
            info = tiled(src, tmp, name)
        elif name == 'climate.bin':
            info = climate(src, tmp)
        elif name.startswith('climatefix'):
            if climate_planes is None:
                climate_planes, _ = pc.read_climate(data / 'climate.bin')
            info = fix(src, tmp, climate_planes)
        else:
            info = weather(src, tmp)
        before, after = src.stat().st_size, tmp.stat().st_size
        info.update({'plain bytes': before, 'predicted bytes': after, 'saved': before - after,
                     'plain sha256': hashlib.sha256(src.read_bytes()).hexdigest(),
                     'predicted sha256': hashlib.sha256(tmp.read_bytes()).hexdigest(), 'seconds': round(time.time() - start)})
        os.replace(tmp, out / name)
        report[name] = info
        print(f'{name}: {before:,} -> {after:,} bytes ({before - after:+,}), every value checked, {info["seconds"]} s', flush=True)
    print(json.dumps(report, indent=1))


if __name__ == '__main__':
    main()
