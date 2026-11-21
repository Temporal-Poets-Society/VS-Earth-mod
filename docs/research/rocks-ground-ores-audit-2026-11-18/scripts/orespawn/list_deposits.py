"""List vanilla and Earth deposit variants with the numbers the background scaling needs."""
import os, sys, glob, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import vsjson

VAN = os.path.join(os.environ['APPDATA'], 'Vintagestory', 'assets', 'survival', 'worldgen', 'deposits')
EARTH = r"C:\VS mods\Earth\assets\earth\worldgen\deposits"

def variants(path):
    d = vsjson.load(path)
    if isinstance(d, dict): d = [d]
    for v in d:
        yield v, None
        for c in v.get('childDeposits') or []:
            yield c, v

rows = []
for root, tag in ((VAN, 'game'), (EARTH, 'earth')):
    for f in sorted(glob.glob(os.path.join(root, '**', '*.json'), recursive=True)):
        rel = os.path.relpath(f, root)
        for v, parent in variants(f):
            a = v.get('attributes', {})
            inb = a.get('inblock', {})
            hosts = inb.get('allowedVariants') or [inb.get('code')]
            rows.append(dict(src=tag + ':' + rel, code=v.get('code'), parent=parent.get('code') if parent else None,
                             gen=v.get('generator'), tries=v.get('triesPerChunk'), map=v.get('withOreMap', False),
                             mapScale=v.get('oreMapScale'), swh=v.get('scaleWithWorldheight', True),
                             radius=a.get('radius'), thick=a.get('thickness'), ypos=a.get('yPosRel'), depth=a.get('depth'),
                             tries_child=a.get('randomTries'), hosts=hosts, climate=bool(v.get('climate') or a.get('climate')),
                             place=(a.get('placeblock') or {}).get('code')))
json.dump(rows, open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deposits.json'), 'w'), indent=1)
for r in rows:
    print(f"{r['src']:<32} {str(r['code']):<22} par={str(r['parent']):<12} {str(r['gen']):<24} tries={r['tries']} map={r['map']}/{r['mapScale']} swh={r['swh']} R={r['radius']} T={r['thick']} y={r['ypos'] or r['depth']} hosts={r['hosts']}")
