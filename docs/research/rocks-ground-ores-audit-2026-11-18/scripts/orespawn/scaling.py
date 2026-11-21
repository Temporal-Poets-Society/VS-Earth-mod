"""Background (0065 s5) and catalogue (0065 s2) scaling, re-computed from Earth's code paths (read-only).

Background: EarthOreGenerator ctor (radius scale, AttemptScale), OreTuning.For (class profiles), vanilla GenDeposits
(attempts = tries * oremap mean * H/256 when scaleWithWorldheight).
"""
import json, math, os, random, sys
HERE = os.path.dirname(os.path.abspath(__file__))
rows = json.load(open(os.path.join(HERE, 'deposits.json')))

# ---------------------------------------------------------------- NatFloat (VS 1.22.7 decompiled, read only)
def nat(nf, rnd):
    if nf is None: return 0.0
    if not isinstance(nf, dict): return float(nf)
    d = nf.get('dist', 'uniform').lower(); a = float(nf.get('avg', 0)); v = float(nf.get('var', 0))
    if d == 'uniform': return a + (rnd.random() - .5) * 2 * v
    if d == 'gaussian': return a + ((rnd.random() + rnd.random() + rnd.random()) / 3 - .5) * 2 * v
    if d == 'narrowgaussian': return a + (sum(rnd.random() for _ in range(6)) / 6 - .5) * 2 * v
    if d == 'triangle': return a + ((rnd.random() + rnd.random()) / 2 - .5) * 2 * v
    if d == 'invexp': return a + rnd.random() * rnd.random() * v
    if d == 'stronginvexp': return a + rnd.random() * rnd.random() * rnd.random() * v
    if d == 'strongerinvexp': return a + rnd.random() * rnd.random() * rnd.random() * rnd.random() * v
    if d == 'dirac': return a + (rnd.random() - .5) * 2 * v
    raise ValueError(d)

# ---------------------------------------------------------------- Earth's class profiles (RegionalOreCatalog.cs)
RES = {}
for c, deps in {'Fe': ['hematite', 'limonite', 'magnetite'], 'Au': ['gold'], 'Ag': ['silver'], 'Pb': ['galena', 'cerussite'],
                'Zn': ['sphalerite', 'smithsonite', 'hemimorphite'], 'Bi': ['bismuthinite'], 'Ni': ['pentlandite'], 'Cr': ['chromite'],
                'Ti': ['ilmenite'], 'Mn': ['rhodochrosite'], 'Hg': ['cinnabar'], 'B': ['borax', 'kernite'], 'P': ['phosphorite'],
                'N': ['saltpeter'], 'K': ['sylvite'], 'NaCl': ['halite'], 'S': ['pyrite', 'sulfur'], 'C_graphite': ['graphite'],
                'C_coal': ['anthracite', 'bituminouscoal', 'lignite'], 'F': ['fluorite'], 'SiO2': ['quartz'], 'diamond': ['diamond'],
                'emerald': ['emerald'], 'olivine': ['olivine', 'peridot'], 'lapis': ['lapis'], 'alum': ['alum'],
                'Cu': ['azurite', 'chalcocite', 'chalcopyrite', 'malachite', 'nativecopper', 'surfacecopper'],
                'Sn': ['cassiterite', 'surfacecassiterite'], 'peat': ['peat'], 'clay': ['clay', 'redclay'], 'W': ['wolframite'],
                'Sb': ['tetrahedrite'], 'V': ['vanadinite'], 'Mo': ['wulfenite'], 'Pt': ['sperrylite']}.items():
    for d in deps: RES[d] = c

def profile(code, res, child=False, layerdep=False):
    """(background, frequency, radiusMul, thickMul, conc, cls) as OreTuning.For with default tuning."""
    if layerdep: cls = 'Soil'
    elif res in ('clay', 'peat'): cls = 'Soil'
    elif code.startswith('surface'): cls = 'Surface'
    elif res in ('Cu', 'Sn', 'Fe', 'C_coal'): cls = 'Progression'
    elif code == 'quartz': cls = 'Veins'
    else: cls = 'Standard'
    bg, freq, rad, th, conc = .08, .4 * (11 if child else 4), 1.6, 1.25, .85
    if not child:
        if cls == 'Progression': bg, freq = {'Cu': .19, 'Sn': .19, 'Fe': .17, 'C_coal': .17}[res], 1.33
        elif cls == 'Surface': bg, freq, rad, th, conc = .5, 2, 1, 1, 1
        elif cls == 'Soil': bg, freq, rad, th, conc = 1 / 1.75, 1.75, 1, 1, 1
        elif cls == 'Veins': bg, freq, rad, th, conc = 1 / 1.5, 1.5, 1, 1, 1
    return bg, freq, rad, th, conc, cls

def eff_radius_mul(native_avg, prof_rad):
    return 1 if native_avg < 2 else prof_rad   # OreRules.EffectiveRadiusMultiplier

def radius_scale(mpb, native_radius, cap=64):   # OreBackground.RadiusScale, rules >= 4
    r = 100 / mpb
    if abs(r - 1) < .02: return 1
    if native_radius > 0: r = min(max(r, .5 / native_radius), cap / native_radius)
    return r

def disc_volume(rv, tv, radmul, thmul, cap, rnd, n=20000):
    """Earth's expectation formula (EarthOreGenerator ctor): pi r^2 ((r>3?.9:1)-.35) th, r rounded and clamped."""
    s = 0; rs = 0; nz = 0
    for _ in range(n):
        drawn = nat(rv, rnd)
        rad = min(max(int(round(drawn * radmul)), 0), cap)
        th = max(0.0, nat(tv, rnd) * thmul)
        if rad > 0:
            s += math.pi * rad * rad * ((.9 if rad > 3 else 1) - .35) * th; rs += rad; nz += 1
    return s / n, (rs / nz if nz else 0), nz / n

SCALES = {'1:200': 200.4, '1:100': 100.2, '1:10': 10.02, '1:5': 5.01}
HEIGHTS = [256, 384, 1024]
MEAN = {None: .0705, 1.5: .0303}

out = []
rnd = random.Random(42)
WANT = ['nativecopper', 'surfacecopper', 'malachite', 'cassiterite', 'surfacecassiterite', 'limonite', 'hematite', 'magnetite',
        'lignite', 'bituminouscoal', 'anthracite', 'galena', 'sphalerite', 'quartz', 'sulfur', 'saltpeter', 'borax', 'kernite',
        'halite', 'chalcopyrite', 'azurite', 'pyrite', 'cerussite', 'tetrahedrite', 'flint', 'lapis', 'graphite', 'phosphorite']
for r in rows:
    code = r['code']
    if code not in WANT or r['parent']: continue
    if r['src'].startswith('earth:') and code in ('flint',): continue
    res = RES.get(code)
    layerdep = 'earth-layers' in r['src'] or 'aluminosilic' in r['src']
    bg, freq, radp, thp, conc, cls = profile(code, res, layerdep=layerdep)
    tries = float(r['tries'] or 0)
    mean = MEAN.get(r['mapScale'], .0705) if r['map'] else 1.0
    cap = 31 if r['climate'] else 64
    ravg = r['radius']['avg'] if isinstance(r['radius'], dict) else 10
    radmul = eff_radius_mul(ravg, radp)
    # vanilla: no radius/thickness multiplier; vanilla truncates the radius, Earth rounds: use Earth's formula for both
    van_vol, van_r, _ = disc_volume(r['radius'], r['thick'], 1, 1, 64, rnd)
    base_vol, base_r, _ = disc_volume(r['radius'], r['thick'], radmul, thp, cap, rnd)
    rec = dict(code=code, src=r['src'].split(':')[1][:22], cls=cls, tries=tries, mean=mean, swh=r['swh'], van_vol=van_vol, van_r=van_r,
               base_vol=base_vol * conc, base_r=base_r)
    ratio = bg * freq                       # Chance(0) * Frequency: v4 background has no evidence term for catalogued resources
    if cls == 'Veins' or cls == 'Soil': ratio = bg * freq   # floor; heat may raise to x1.5 / x1.75
    rec['ratio'] = ratio
    rockhosted = not layerdep and cls != 'Soil' and not (ravg < 2)
    for sname, h in SCALES.items():
        rs = radius_scale(h, ravg * radmul, cap) if rockhosted else 1
        vol, rr, valid = disc_volume(r['radius'], r['thick'], radmul * rs, thp, cap, rnd)
        att_scale = base_vol / vol if vol > 0 else 0
        rec[sname] = dict(rs=rs, r=rr, vol=vol * conc, att_scale=att_scale)
    out.append(rec)

print('Vanilla vs Earth v4 background (hosts assumed present; texture mean 1; H = world height)')
print(f"{'code':<19}{'file':<16}{'class':<12}{'tries':>6}{'mean':>7} swh {'vanR':>5}{'vanVol':>8}  {'ratio':>6}  {'EarthVol/van':>12}")
for rec in out:
    print(f"{rec['code']:<19}{rec['src']:<16}{rec['cls']:<12}{rec['tries']:>6.3g}{rec['mean']:>7.4f}  {int(rec['swh'])}  {rec['van_r']:>5.1f}{rec['van_vol']:>8.1f}  {rec['ratio']:>6.3f}  {rec['ratio']*rec['base_vol']/rec['van_vol'] if rec['van_vol'] else 0:>12.3f}")

print()
print('Per scale: mean radius (blocks), blocks per deposit, attempts per chunk (H=384), spacing between deposits (blocks; real km), blocks per chunk vanilla / Earth at H=256,384,1024')
for rec in out:
    print(f"-- {rec['code']} ({rec['src']}, {rec['cls']})")
    for H in HEIGHTS:
        hs = H / 256 if rec['swh'] else 1
        van_att = rec['tries'] * rec['mean'] * hs
        van_bpc = van_att * rec['van_vol']
        line = f"   H={H:<5} vanilla: {van_att:7.3f} att/chunk, {rec['van_vol']:7.1f} blk/dep, spacing {32/math.sqrt(van_att) if van_att>0 else float('inf'):7.0f} blk, {van_bpc:8.1f} blk/chunk | Earth v4:"
        for sname, h in SCALES.items():
            s = rec[sname]
            att = rec['tries'] * rec['mean'] * rec['ratio'] * s['att_scale'] * hs
            bpc = att * s['vol']
            sp = 32 / math.sqrt(att) if att > 0 else float('inf')
            line += f" [{sname} r={s['r']:.1f} dep={s['vol']:.0f} att={att:.4f} sp={sp:.0f}blk/{sp*h/1000:.1f}km bpc={bpc:.1f}]"
        print(line)
json.dump(out, open(os.path.join(HERE, 'scaling.json'), 'w'), indent=1)
