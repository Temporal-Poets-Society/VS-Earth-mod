"""Consolidated background table: per material, vanilla vs Earth v4 blocks per chunk (hosts assumed), with vanilla's 1 %
attempt rounding and the world-height factor, deposit size and spacing of the main variant."""
import json, math, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
out = json.load(open(os.path.join(HERE, 'scaling.json')))
import quantize  # noqa: prints its own table; we reuse effective() and textures
eff, clustered, soft = quantize.effective, quantize.clustered, quantize.soft

MAT = {
    'copper': ['nativecopper', 'malachite', 'surfacecopper', 'chalcopyrite', 'azurite'],
    'tin': ['cassiterite', 'surfacecassiterite'],
    'iron': ['limonite', 'hematite', 'magnetite'],
    'coal': ['lignite', 'bituminouscoal', 'anthracite'],
    'lead/zinc': ['galena', 'sphalerite', 'cerussite'],
    'sulfur': ['sulfur', 'pyrite'],
    'saltpeter': ['saltpeter'],
    'borax/kernite': ['borax', 'kernite'],
    'halite (domes+crust)': ['halite'],
    'quartz (gold/silver host)': ['quartz'],
    'flint': ['flint'],
}
COMBOS = [('1:200', 384), ('1:100', 384), ('1:10', 1024), ('1:5', 1024), ('1:10', 384), ('1:100', 256)]
print(f"{'material':<26}{'vanilla H384':>13}{'vanilla H1024':>14}" + ''.join(f"{s+'/'+str(H):>15}" for s, H in COMBOS))
for mat, codes in MAT.items():
    recs = [r for r in out if r['code'] in codes]
    van = {H: sum(r['tries'] * r['mean'] * (H / 256 if r['swh'] else 1) * r['van_vol'] for r in recs) for H in (256, 384, 1024)}
    cells = []
    for s, H in COMBOS:
        tot = 0
        for r in recs:
            hs = H / 256 if r['swh'] else 1
            rate = r['tries'] * r['mean'] * r['ratio'] * r[s]['att_scale'] * hs
            e = eff(rate, clustered if r['mean'] < 1 else soft)
            tot += e * r[s]['vol']
        cells.append(tot)
    print(f"{mat:<26}{van[384]:>13.1f}{van[1024]:>14.1f}" + ''.join(f"{c:>15.1f}" for c in cells))

print('\nMain variant: radius (blocks) / blocks per deposit / spacing between deposits in blocks (with rounding) / same in real km')
MAIN = {'copper': 'nativecopper', 'surface copper': 'surfacecopper', 'tin': 'cassiterite', 'iron': 'limonite', 'coal': 'bituminouscoal',
        'lead': 'galena', 'sulfur': 'sulfur', 'borax': 'borax', 'quartz': 'quartz', 'saltpeter': 'saltpeter'}
H_OF = {'1:200': 384, '1:100': 384, '1:10': 1024, '1:5': 1024}
MPB = {'1:200': 200.4, '1:100': 100.2, '1:10': 10.02, '1:5': 5.01}
for name, code in MAIN.items():
    r = [x for x in out if x['code'] == code][0]
    line = f" {name:<15}"
    for s in ('1:200', '1:100', '1:10', '1:5'):
        H = H_OF[s]; hs = H / 256 if r['swh'] else 1
        rate = r['tries'] * r['mean'] * r['ratio'] * r[s]['att_scale'] * hs
        e = eff(rate, clustered if r['mean'] < 1 else soft)
        sp = 32 / math.sqrt(e) if e > 0 else float('inf')
        line += f" | {s}: R {r[s]['r']:.0f}, {r[s]['vol']:.0f} blk/dep, {sp:.0f} blk ({sp*MPB[s]/1000:.1f} km)"
    vh = 32 / math.sqrt(r['tries'] * r['mean'] * (384 / 256 if r['swh'] else 1))
    print(line + f" | vanilla H384: R {r['van_r']:.0f}, {r['van_vol']:.0f} blk/dep, {vh:.0f} blk")
