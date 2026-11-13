"""Natural ranges for vanilla Vintage Story land animals (decision 0031): assets/earth/config/fauna-vanilla.json.

Each vanilla animal type (entity code family + type, e.g. bear-polar, deer-chital, hyena-spotted) maps to the wild species it
depicts; its range is that species' PHYLACINE v1.2.1 present-natural raster (CC0, the same source and packing as
fauna-species.json, tools/build-fauna-catalog.py). Domestic goat breeds take the wild goat's (Capra aegagrus), the mouflon
Ovis orientalis'. Chickens are red junglefowl, which PHYLACINE (mammals only) lacks: a box over their native range.
Butterflies are not mapped. Earth then keeps natural spawns of these animals inside their ranges in worlds with real-altitude
ecology (EarthWorldGen's vanilla spawn guard).

Version 2 (decision 0054) adds each entry's 1318 range from tools/fauna-1318.json, used in worlds placed as around 1318: regions
the species had lost by then ("taxa" keyed game:family-type), and for domestic animals, which went feral when people
disappeared, the regions where people kept them ("vanilla": range1318Regions replaces the natural range, added1318Regions
extends it). Entries with losses also carry their PHYLACINE current raster ("currentMask"): the losses never remove today's
range. The tur ("goat-turdag") is the East Caucasian tur, Capra cylindricornis, not the wild goat.

Offline by default; --fetch downloads missing rasters from the pinned v1.2.1 tag into the range cache.

    .venv/Scripts/python -I tools/build-vanilla-fauna.py [--range-cache DIR] [--fetch]
"""
import argparse, base64, hashlib, json, urllib.request
from pathlib import Path

import numpy as np
import tifffile

ROOT = Path(__file__).resolve().parents[1]
ap = argparse.ArgumentParser()
ap.add_argument('--range-cache', type=Path, default=ROOT / 'docs/research/regional-fauna-2026-09-29/ranges')
ap.add_argument('--current-cache', type=Path, default=ROOT / 'docs/research/regional-fauna-2026-09-29/ranges-current')
ap.add_argument('--fetch', action='store_true')
args = ap.parse_args()
BASE = 'https://raw.githubusercontent.com/MegaPast2Future/PHYLACINE_1.2/v1.2.1/'

# family -> type -> PHYLACINE binomial (file name)
TAXA = {
    'bear': {'black': 'Ursus_americanus', 'brown': 'Ursus_arctos', 'sun': 'Helarctos_malayanus', 'panda': 'Ailuropoda_melanoleuca',
             'polar': 'Ursus_maritimus'},
    'fox': {'red': 'Vulpes_vulpes', 'arctic': 'Vulpes_lagopus'},
    'hare': {'arctic': 'Lepus_arcticus', 'cape': 'Lepus_capensis', 'european': 'Lepus_europaeus', 'indian': 'Lepus_nigricollis',
             'jackblack': 'Lepus_californicus', 'scrub': 'Lepus_saxatilis'},
    'deer': {'whitetail': 'Odocoileus_virginianus', 'redbrocket': 'Mazama_americana', 'marsh': 'Blastocerus_dichotomus',
             'caribou': 'Rangifer_tarandus', 'water': 'Hydropotes_inermis', 'pudu': 'Pudu_puda', 'elk': 'Cervus_canadensis',
             'taruca': 'Hippocamelus_antisensis', 'chital': 'Axis_axis', 'guemal': 'Hippocamelus_bisulcus',
             'pampas': 'Ozotoceros_bezoarticus', 'fallow': 'Dama_dama', 'moose': 'Alces_alces'},
    'gazelle': {'thomson': 'Eudorcas_thomsonii'},
    'goat': {'ibexalp': 'Capra_ibex', 'ibexnub': 'Capra_nubiana', 'markhor': 'Capra_falconeri', 'mountain': 'Oreamnos_americanus',
             'muskox': 'Ovibos_moschatus', 'takingold': 'Budorcas_taxicolor', 'angora': 'Capra_aegagrus', 'nubian': 'Capra_aegagrus',
             'sirohi': 'Capra_aegagrus', 'turdag': 'Capra_cylindricornis', 'valais': 'Capra_aegagrus'},
    'pig': {'eurasian': 'Sus_scrofa', 'redriver': 'Potamochoerus_porcus', 'warthog': 'Phacochoerus_africanus'},
    'sheep': {'bighorn': 'Ovis_canadensis', 'mouflon': 'Ovis_orientalis'},
    'hyena': {'spotted': 'Crocuta_crocuta'},
    'raccoon': {'common': 'Procyon_lotor'},
    'wolf': {'eurasian': 'Canis_lupus'},
}
# red junglefowl: India and the Himalayan foothills to southern China and Indonesia (west, south, east, north)
CHICKEN = [[68.0, -11.0, 125.0, 31.0]]


def fetch(name):
    target = args.range_cache / (name + '.tif')
    if not target.exists():
        if not args.fetch: raise SystemExit(f'missing {target}; rerun with --fetch')
        target.parent.mkdir(parents=True, exist_ok=True)
        with urllib.request.urlopen(BASE + 'Data/Ranges/Present_natural/' + name + '.tif', timeout=90) as r: target.write_bytes(r.read())
    return target


def packed(path):
    a = tifffile.imread(path)
    assert a.shape == (142, 360), path
    return base64.b64encode(np.packbits((a == 1).ravel(), bitorder='little')).decode(), int((a == 1).sum())


f1318 = json.loads((ROOT / 'tools/fauna-1318.json').read_text(encoding='utf-8'))
lost = {k[5:]: t for t in f1318['taxa'].values() for k in t['keys'] if k.startswith('game:')}
domestic = f1318['vanilla']
FIELDS = ('range1318Regions', 'added1318Regions', 'lost1318Regions', 'lost1318Envelopes')


def add1318(entry, key):
    t = lost.get(key)
    if t:
        if t.get('lost1318Regions'): entry['lost1318Regions'] = sorted(t['lost1318Regions'])
        if t.get('lost1318Envelopes'): entry['lost1318Envelopes'] = t['lost1318Envelopes']
        if ('lost1318Regions' in entry or 'lost1318Envelopes' in entry) and 'mask' in entry:
            name = entry['taxon'].replace(' ', '_')
            cur = args.current_cache / (name + '.tif')
            if not cur.exists():
                if not args.fetch: raise SystemExit(f'missing {cur}; rerun with --fetch')
                cur.parent.mkdir(parents=True, exist_ok=True)
                with urllib.request.urlopen(BASE + 'Data/Ranges/Current/' + name + '.tif', timeout=90) as r: cur.write_bytes(r.read())
            entry['currentMask'] = packed(cur)[0]
            shas['current/' + name + '.tif'] = hashlib.sha256(cur.read_bytes()).hexdigest()
    d = domestic.get(key)
    if d:
        for f in FIELDS:
            if d.get(f): entry[f] = sorted(set(entry.get(f, []) + d[f])) if f.endswith('Regions') else d[f]
    return entry


entries, shas = [], {}
for family, types in TAXA.items():
    for t, name in types.items():
        path = fetch(name)
        mask, cells = packed(path)
        if cells == 0: raise SystemExit(f'{name}: empty range')
        shas[name + '.tif'] = hashlib.sha256(path.read_bytes()).hexdigest()
        entries.append(add1318({'family': family, 'type': t, 'taxon': name.replace('_', ' '), 'mask': mask}, family + '-' + t))
        print(f'{family}-{t}: {name} ({cells} cells)')
# "Chickens everywhere" (decision 0054): every region people could keep them in, so not Antarctica.
entries.append(add1318({'family': 'chicken', 'taxon': 'Gallus gallus (red junglefowl; box, not in PHYLACINE)', 'envelopes': CHICKEN, 'everywhereExcludedRegions': ['ANT']}, 'chicken'))
# Ranges proposal, step 2: butterflies have a range (every region but Antarctica, through Earth's region index), and every
# entry carries its altitude limit as data: the warmest month at the spawn's real elevation must reach "minWarmMonth" (°C):
# goats (wild goats, ibex, tur, muskox and the breeds gone feral) on any ground with a thawing summer (0: not on ice caps or
# the highest summits), animals of alpine meadows and fell-fields up to the snow line (0.5), the rest up to the tree line (6).
entries.append({'family': 'butterfly', 'taxon': 'Lepidoptera (butterflies and moths occur on every continent but Antarctica)', 'excludedRegions': ['ANT']})
ALPINE = {'sheep', 'hare', 'fox', 'wolf', 'bear', 'butterfly'}
for e in entries:
    e['minWarmMonth'] = 0 if e['family'] == 'goat' else 0.5 if e['family'] in ALPINE else 6
known = {e['family'] + ('-' + e['type'] if 'type' in e else '') for e in entries}
missing = [k for k in list(lost) + list(domestic) if k not in known]
if missing: raise SystemExit('fauna-1318.json: no vanilla entry for ' + ', '.join(missing))
out = {'version': 3, 'source': 'PHYLACINE v1.2.1 present-natural ranges (CC0 1.0): ' + BASE + 'Data/Ranges/Present_natural/{name}.tif',
       'sha256': shas, 'entries': entries}
target = ROOT / 'assets/earth/config/fauna-vanilla.json'
target.write_text(json.dumps(out, separators=(',', ':'), ensure_ascii=False) + '\n', encoding='utf-8')
print(f'{target}: {len(entries)} entries, {target.stat().st_size} bytes')
