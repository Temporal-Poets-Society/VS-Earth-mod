"""Package Earth as one archive within the Mod DB's 40 MB (decision 0066).

The build output (bin/Release/mod) holds the code, the assets and the data a client reads; every other data file is in the
server's data store (src/Core/EarthDataStore.cs, release ed-v1 of Temporal-Poets-Society/earth-data) and must not be in
the zip. Writes release/earth-<version>.zip and release/earth-<version>-package.json. A published archive is never replaced:
bump "version" in modinfo.json, or pass --overwrite-main to replace an unpublished one (the old one is backed up).
"""
import argparse, hashlib, io, json, pathlib, re, zipfile

LIMIT = 40_000_000
STORE_LIST = pathlib.Path(__file__).resolve().parent.parent / 'src' / 'Core' / 'EarthDataStore.cs'
CLIENT_DATA = ['earth.json', 'climate.bin', 'climatefix.bin', 'climatefix2.bin', 'climatefix3.bin', 'temperature.bin', 'rain.bin', 'weather.bin', 'aridity.bin']
DATA_PACKS = ['earthterraindata', 'earthwaterdata', 'earthwaterfixdata', 'earthheightdata1', 'earthheightdata2',
              'earthheightdata3', 'earthheightdata4', 'earthgrounddata']


def store_names():
    names = re.findall(r'new\("([^"]+)", [\d_]+, "[0-9a-f]{64}"\)', STORE_LIST.read_text(encoding='utf-8'))
    if len(names) < 30:
        raise SystemExit(f'Could not read the data store list from {STORE_LIST} ({len(names)} names)')
    return set(names)


def archive(files):
    memory = io.BytesIO()
    with zipfile.ZipFile(memory, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name, data in sorted(files.items()):
            z.writestr(name, data)
    return memory.getvalue()


def matches(path, files):
    with zipfile.ZipFile(path) as z:
        return set(z.namelist()) == set(files) and all(z.read(name) == data for name, data in files.items())


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--input', type=pathlib.Path, required=True, help='the build output (bin/Release/mod)')
    ap.add_argument('--output', type=pathlib.Path, required=True, help='the release folder')
    ap.add_argument('--overwrite-main', action='store_true', help='back up and replace an unpublished archive of this version')
    args = ap.parse_args()
    source, output = args.input.resolve(), args.output.resolve()
    files = {p.relative_to(source).as_posix(): p.read_bytes() for p in source.rglob('*') if p.is_file()}
    manifest = json.loads(files['modinfo.json'])
    version, modid = manifest['version'], manifest['modid']
    if any(key.lower() == 'incompatibilities' for key in manifest):
        raise SystemExit('Mod DB rejects the modinfo.json property incompatibilities. Remove it before packaging.')
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-(?:dev|pre|rc)\.\d+)?', version):
        raise SystemExit(f'Mod DB accepts versions x.y.z or x.y.z-dev|pre|rc.N only, not {version}')
    packs = [d for d in manifest.get('dependencies', {}) if d in DATA_PACKS]
    if packs:
        raise SystemExit('Earth 2 depends on no data-pack mod (decision 0066); remove from modinfo.json: ' + ', '.join(packs))
    leaked = sorted(name for name in files if name.rsplit('/', 1)[-1] in store_names())
    if leaked:
        raise SystemExit('Data store files belong in the earth-data release, not in the zip: ' + ', '.join(leaked[:8]))
    missing = [n for n in CLIENT_DATA if 'earthdata/' + n not in files]
    if missing:
        raise SystemExit('The client data must be in the zip: ' + ', '.join(missing))
    data = archive(files)
    if len(data) > LIMIT:
        raise SystemExit(f'{modid}-{version}.zip is {len(data):,} bytes; the Mod DB takes at most {LIMIT:,}')
    output.mkdir(parents=True, exist_ok=True)
    target = output / f'{modid}-{version}.zip'
    if target.exists() and not matches(target, files):
        if not args.overwrite_main:
            raise SystemExit(f'{target} already exists with different contents. Bump the version; existing releases are preserved.')
        previous = target.read_bytes()
        backup = output / '.package-backups' / (target.stem + '-' + hashlib.sha256(previous).hexdigest()[:12] + '.zip')
        backup.parent.mkdir(exist_ok=True)
        if not backup.exists():
            backup.write_bytes(previous)
        target.write_bytes(data)
    elif not target.exists():
        with target.open('xb') as stream:
            stream.write(data)
    else:
        data = target.read_bytes()
    report = {'limit_bytes': LIMIT, 'file': str(target), 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
              'headroom_bytes': LIMIT - len(data)}
    (output / f'{modid}-{version}-package.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
