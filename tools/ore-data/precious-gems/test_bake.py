"""Test bakes of the ore heat maps for the precious-gems group (never touches the shipped file).

  python -I tools/ore-data/precious-gems/test_bake.py <scratch folder> before|after

before: the bake with no group evidence and no group overrides (what the maps are without this group's work).
after:  the bake with this group's evidence.geojson and heatmap-overrides.json only (other groups' folders ignored, so
        the comparison isolates this group's changes).
Writes <scratch>/<mode>/ore-heatmaps.json and <scratch>/<mode>/report/.
"""
from pathlib import Path
import importlib.util, shutil, sys, tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def main():
    scratch, mode = Path(sys.argv[1]).resolve(), sys.argv[2]
    out = scratch / mode
    out.mkdir(parents=True, exist_ok=True)
    groups = Path(tempfile.mkdtemp(prefix='ore-groups-'))
    if mode == 'after':
        (groups / HERE.name).mkdir()
        for f in ['evidence.geojson', 'heatmap-overrides.json']:
            if (HERE / f).exists(): shutil.copy(HERE / f, groups / HERE.name / f)
    sys.argv = [str(ROOT / 'tools/bake_ore_heatmaps.py'), '--out', str(out / 'ore-heatmaps.json'), '--report', str(out / 'report')]
    spec = importlib.util.spec_from_file_location('bake', ROOT / 'tools/bake_ore_heatmaps.py')
    bake = importlib.util.module_from_spec(spec); spec.loader.exec_module(bake)
    bake.GROUPS = groups
    # track() records inputs relative to ROOT; group files copied to a temp folder are not under ROOT.
    orig_track = bake.Baker.track
    def track(self, path):
        try: orig_track(self, path)
        except ValueError: self.inputs[str(path)] = {'file': str(path), 'sha256': bake.sha_file(path)}
    bake.Baker.track = track
    bake.main()


if __name__ == '__main__':
    main()
