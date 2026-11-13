"""Check the shipped ore artifact, its provenance and the offline preview without opening a browser.

Run: .venv\\Scripts\\python.exe tests/RegionalOresTests/check_artifacts.py [node] [--release]

Always checked: runtime hash recorded in the bake manifest, 44-layer v1 file contract, every recorded input hash,
static maps, viewer payload and JavaScript syntax, plus the content regressions in test_ore_regression.py and the
geometry/depth unit checks. With --release (after the C# project and release archive are rebuilt) the release
zip must also contain exactly this runtime file and the built DLL.
"""
from pathlib import Path
import base64, gzip, hashlib, json, os, re, subprocess, sys, tempfile, zipfile

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'docs/research/ore-heatmaps-v1'
PY = sys.executable
RESEARCH = Path(os.environ.get('EARTH_RESEARCH') or ROOT.parent / 'data' / 'earth-research')
V1_BAKE_COMMIT = '5f147b8'   # the commit that wrote ore-heatmaps-v1/bake-manifest.json (0.13.1)
changed_since = []
args = [a for a in sys.argv[1:] if not a.startswith('--')]
release = '--release' in sys.argv


def sha(b): return hashlib.sha256(b).hexdigest()


runtime = ROOT / 'assets/earth/config/ore-heatmaps-v1.json'
raw = runtime.read_bytes(); data = json.loads(raw)
manifest = json.loads((OUT / 'bake-manifest.json').read_text(encoding='utf8'))
assert sha(raw) == manifest['runtimeSha256'], 'runtime differs from bake manifest; re-run tools/bake_ore_heatmaps.py'
assert data['version'] == 1 and data['width'] == 720 and data['height'] == 360 and len(data['layers']) == 44
assert data['datasetId'] == manifest['settings']['datasetId']
for layer in data['layers']:
    decoded = gzip.decompress(base64.b64decode(layer['data']))
    assert len(decoded) == 720 * 360 * 5
    assert (OUT / 'maps' / f"{layer['code']}.png").exists()
for item in manifest['inputs']:
    # the manifest predates the asset domain's rename from earthmap to earth (decision 0044) and the move of the
    # untracked research downloads out of the repo (the suite's data\earth-research, or EARTH_RESEARCH)
    path = ROOT / item['file'].replace('assets/earthmap/', 'assets/earth/')
    if not path.exists() and item['file'].startswith('docs/research/'):
        path = RESEARCH / item['file'][len('docs/research/'):]
    if sha(path.read_bytes()) == item['sha256']:
        continue
    # the v1 grid is frozen for the worlds made with it; a tracked input changed since for heat maps v2 (decision 0065,
    # 8 November) must still be the one recorded as it was at the commit that wrote this manifest
    old = subprocess.run(['git', '-C', str(ROOT), 'show', f'{V1_BAKE_COMMIT}:{item["file"]}'], capture_output=True)
    assert old.returncode == 0 and sha(old.stdout) == item['sha256'], item['file']
    changed_since.append(item['file'])
sources = (ROOT / 'assets/earth/config/ore-heatmaps-SOURCES.txt').read_text(encoding='utf8')
assert data['datasetId'] in sources, 'ore-heatmaps-SOURCES.txt does not name the shipped datasetId'
html = (OUT / 'index.html').read_text(encoding='utf8')
payload = json.loads(re.search(r'<script id="data" type="application/json">(.*?)</script>', html, re.S).group(1))
assert payload['runtime'] == data and payload['sha256'] == sha(raw)
assert len(gzip.decompress(base64.b64decode(payload['ocean']))) == 720 * 360
# the viewer is v1's (0.13.1); the config gained tuning keys since (decision 0006, 0.14.1), which the viewer predates
ore_config = json.loads((ROOT / 'docs/regional-ores-config.json').read_text())['Ores']
assert all(ore_config.get(k) == v for k, v in payload['defaults'].items()), 'viewer defaults differ from docs/regional-ores-config.json'
script = re.findall(r'<script>(.*?)</script>', html, re.S)[0]
script_path = Path(tempfile.gettempdir()) / 'earth-ore-viewer-syntax-check.js'; script_path.write_text(script, encoding='utf8')
node = args[0] if args else 'node'
try:
    subprocess.run([node, '--check', str(script_path)], check=True)
    js = 'JavaScript syntax passed'
except FileNotFoundError:
    js = 'node not found; JavaScript syntax not checked'
for test in ['test_bake_depths.py', 'test_bake_geometry.py', 'test_ore_regression.py']:
    subprocess.run([PY, str(HERE / test)], check=True)
result = {'status': 'PASS', 'datasetId': data['datasetId'], 'layers': 44, 'inputHashesChecked': len(manifest['inputs']),
          'inputsChangedSinceV1Bake': changed_since,
          'runtimeSha256': sha(raw), 'offlinePreview': f'44 grids, defaults, source hashes; {js}; live browser UI not tested.',
          'contentRegression': 'test_ore_regression.py, test_bake_geometry.py and test_bake_depths.py passed'}
if release:
    version = json.loads((ROOT / 'modinfo.json').read_text())['version']
    archive = ROOT / 'release' / f'earth-{version}.zip'
    with zipfile.ZipFile(archive) as z:
        assert z.testzip() is None
        entries = {n.replace('\\', '/'): n for n in z.namelist()}
        for filename in ['modinfo.json', 'EarthMap.dll', 'assets/earth/config/ore-heatmaps-v1.json', 'assets/earth/config/ore-heatmaps-SOURCES.txt', 'assets/earth/blocktypes/bogpeat.json', 'assets/earth/lang/en.json']:
            assert filename in entries, filename
        assert json.loads(z.read(entries['modinfo.json']))['version'] == version
        assert z.read(entries['assets/earth/config/ore-heatmaps-v1.json']) == raw, 'release zip carries a different ore grid; rebuild the release'
        assert z.read(entries['EarthMap.dll']) == (ROOT / 'bin/Release/mod/EarthMap.dll').read_bytes()
    result.update(version=version, archiveSha256=sha(archive.read_bytes()), releaseDllSha256=sha((ROOT / 'bin/Release/mod/EarthMap.dll').read_bytes()))
else:
    result['release'] = 'not checked (run with --release after rebuilding the release archive)'
(OUT / 'artifact-validation.json').write_text(json.dumps(result, indent=2), encoding='utf8')
print(json.dumps(result, indent=2))
