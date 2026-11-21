"""Catalogue (0065) at several scales: targets, sub-block bodies, compact blankets, spacing and nearest deposits from the
spawn cities. Re-implements OreDepositCatalog defaults and OreDepositPlan.Plan's target (analytic for non-lens shapes)."""
import json, math, glob, os, collections, sys
HERE = os.path.dirname(os.path.abspath(__file__))
CAT = r"C:\VS mods\Earth\assets\earth\config\ore-deposits-v1"

class Curve:
    def __init__(s, top, d0): s.top, s.d0 = top, d0
    def blocks(s, m):
        m = max(0, m)
        return m / s.top if math.isinf(s.d0) else s.d0 / s.top * math.log(1 + m / s.d0)
    def metres(s, b):
        return b * s.top if math.isinf(s.d0) else s.d0 * (math.exp(b * s.top / s.d0) - 1)
def fit(top, floor_m, blocks):
    if not (floor_m > top * blocks): return Curve(top, math.inf)
    lo, hi = 1e-3, 1e12
    for _ in range(200):
        mid = math.sqrt(lo * hi)
        b = mid / top * math.log(1 + floor_m / mid)
        if b > blocks: hi = mid
        else: lo = mid
    return Curve(top, math.sqrt(lo * hi))
def terrain_top(height, curve):          # VerticalScale.Slope(0): metres per block at sea level, sea 110, Everest at height-2
    top = height - 2
    if curve == 'linear': return 8849 / (top - 110)
    s = (top - 110) / math.log(1 + 8849 / 1000)
    return 1000 / s
CURVES = {}
for H, c in [(256, 'log1000'), (384, 'log1000'), (512, 'linear'), (1024, 'linear'), (1024, 'log1000')]:
    t = terrain_top(H, c)
    CURVES[(H, c)] = fit(t, 4000, 110 + 5 - 1)   # DepthCurve.For(log4000, t, 110)

def default_density(shape, code):
    D = {"lignite": 1.2, "bituminouscoal": 1.35, "anthracite": 1.55, "peat": 1.1, "halite": 2.15, "sylvite": 2.15, "earth-saltflat": 2.15,
         "earth-saltflat-sylvite": 2.15, "earth-gypsum": 2.3, "gypsum": 2.3, "earth-bauxite": 2.2, "earth-placergold": 2.0, "earth-placertin": 2.0,
         "earth-placerplatinum": 2.0, "earth-bogiron": 1.6, "earth-kaolin": 2.1, "hematite": 3.5, "magnetite": 3.5, "limonite": 3.0,
         "chromite": 3.8, "ilmenite": 3.6, "borax": 1.8, "kernite": 1.8, "phosphorite": 2.6, "sulfur": 2.1}
    return D.get(code, {'lens': 3.2, 'blanket': 2.0, 'dome': 2.15}.get(shape, 2.7))

deps, areas = [], []
for f in sorted(glob.glob(os.path.join(CAT, '*.json'))):
    d = json.load(open(f, encoding='utf-8'))
    for x in d['deposits']:
        if not (isinstance(x.get('mt'), (int, float)) and x['mt'] > 0): continue
        x['_file'] = os.path.basename(f)
        sh = x['shape']; ores = x['ores']
        dens = x.get('density')
        if not (isinstance(dens, (int, float)) and .8 <= dens <= 8): dens = default_density(sh, ores[0]['code'])
        top = x.get('topM'); base = x.get('baseM')
        if not (isinstance(top, (int, float)) and 0 <= top < 20000): top = 0 if sh in ('blanket', 'stock') else 50
        if not (isinstance(base, (int, float)) and base > top and base < 20000):
            base = top + {'blanket': 10, 'veins': 500, 'stock': 800, 'dome': 3000}.get(sh, 300)
        vol = x['mt'] * 1e6 / dens
        L, W = x.get('lenKm'), x.get('widKm')
        if not (isinstance(L, (int, float)) and L > 0 and isinstance(W, (int, float)) and W > 0):
            area = vol / (.5 * (base - top))
            if isinstance(L, (int, float)) and L > 0: W = min(L, area / (math.pi / 4 * L * 1e3) / 1e3)
            elif isinstance(W, (int, float)) and W > 0: L = max(W, area / (math.pi / 4 * W * 1e3) / 1e3)
            else: W = math.sqrt(area / (math.pi / 4 * 2)) / 1e3; L = 2 * W
        L, W = max(L, W), min(L, W)
        dip = x.get('dipDeg')
        if not (isinstance(dip, (int, float)) and 0 <= dip <= 90): dip = {'lens': 45, 'blanket': 0, 'veins': 80}.get(sh, 90)
        x.update(_vol=vol, _top=top, _base=base, _L=L, _W=W, _dip=dip, _dens=dens)
        deps.append(x)
    for a in d.get('areas', []):
        a['_file'] = os.path.basename(f); areas.append(a)
print(len(deps), 'deposits,', len(areas), 'areas')

def target(x, hz, curve, rate=1.0):
    """Expected blocks (OreDepositPlan.Plan), and the compact depth. Lens: vertical extent from the ellipsoid (approx.)."""
    lat = x['lat']; hx = 111320 * max(.02, math.cos(math.radians(lat))) / (111320 / hz)
    top, base = x['_top'], x['_base']
    vol = x['_vol']
    if x['shape'] == 'lens':
        A = x['_L'] * 500; B = x['_W'] * 500; dip = math.radians(x['_dip']); depth = base - top
        sin, cos = math.sin(dip), math.cos(dip)
        c = depth / 2 / sin if sin > .7 else max(B / max(.2, cos), depth / 2 / max(.05, sin) * (1 if sin > .05 else 0))
        c = max(c, 1)
        E = 1.5 * vol / (math.pi * A * c) / 2
        if E > c: k = math.sqrt(E / c); A *= k; c *= k; E = c
        hv = math.sqrt((c * sin) ** 2 + (E * cos) ** 2)
        mid = max((top + base) / 2, top + hv)
        t, b = max(0, mid - hv), mid + hv
        # average blocks per metre over the lens's vertical span, weighted ~ uniformly (approximation of the quadrature)
        per_m = (curve.blocks(b) - curve.blocks(t)) / max(1e-9, b - t)
        tgt = vol / (hx * hz) * per_m * rate
        cdepth = math.floor(curve.blocks(mid))
    else:
        per_m = (curve.blocks(base) - curve.blocks(top)) / (base - top)
        tgt = vol / (hx * hz) * per_m * rate
        cdepth = math.floor(curve.blocks((top + base) / 2))
    return tgt, cdepth, hx

SC = [('1:200', 200.4, (384, 'log1000')), ('1:100', 100.2, (384, 'log1000')), ('1:10', 10.02, (1024, 'linear')), ('1:5', 5.01, (1024, 'linear')),
      ('1:25', 25.05, (512, 'linear')), ('1:100/256', 100.2, (256, 'log1000'))]
for name, hz, ck in SC:
    cv = CURVES[ck]
    print(f"curve {name} {ck}: top {cv.top:.2f} m/blk, d0 {cv.d0:.0f} m, 100 m -> {cv.blocks(100):.1f} blk, 1000 m -> {cv.blocks(1000):.1f} blk, 2000 m -> {cv.blocks(2000):.1f}, floor 160 blk = {cv.metres(160):.0f} m")

print('\nSub-block and compact bodies (target < 1 -> compact cluster; blankets with compact depth 0 lose their centre layer)')
for name, hz, ck in SC[:4]:
    cv = CURVES[ck]
    sub = collections.Counter(); subb = collections.Counter(); tot = collections.Counter(); lostb = 0; lostb_exp = 0
    for x in deps:
        t, cd, hx = target(x, hz, cv)
        tot[x['shape']] += 1
        if t < 1:
            sub[x['shape']] += 1
            if x['shape'] == 'blanket' and cd == 0:
                subb[x['layer']] += 1; lostb_exp += t
    print(f" {name}: sub-block {dict(sub)} of {dict(tot)}; sub-block blankets with compact depth 0 by layer {dict(subb)} (their expected blocks {lostb_exp:.1f})")

print('\nPer layer: deposits, total target blocks at each scale, median target, and catalogue blocks per land chunk on average')
LAND_KM2 = 1.49e8
bylayer = collections.defaultdict(list)
for x in deps: bylayer[x['layer']].append(x)
for layer in sorted(bylayer, key=lambda k: -len(bylayer[k])):
    xs = bylayer[layer]
    line = f" {layer:<11}{len(xs):>5} dep, Mt {sum(x['mt'] for x in xs):>10.0f}"
    for name, hz, ck in SC[:4]:
        ts = sorted(target(x, hz, CURVES[ck])[0] for x in xs)
        line += f" | {name} sum {sum(ts):>11.0f} med {ts[len(ts)//2]:>9.1f}"
    print(line)

# ---------------------------------------------------------------- spacing and nearest from spawns
SPAWNS = {'london': (51.507, -0.128), 'paris': (48.857, 2.352), 'madrid': (40.417, -3.704), 'rome': (41.903, 12.496), 'berlin': (52.52, 13.405),
          'vienna': (48.208, 16.373), 'warsaw': (52.23, 21.012), 'athens': (37.984, 23.728), 'stockholm': (59.329, 18.069), 'riga': (56.949, 24.105),
          'kyiv': (50.45, 30.523), 'moscow': (55.756, 37.617), 'rabat': (34.02, -6.84), 'cairo': (30.044, 31.236), 'abuja': (9.076, 7.399),
          'niamey': (13.512, 2.112), 'addisababa': (9.03, 38.74), 'nairobi': (-1.286, 36.817), 'pretoria': (-25.747, 28.229),
          'baghdad': (33.315, 44.366), 'tehran': (35.689, 51.389), 'newdelhi': (28.614, 77.209), 'bangkok': (13.756, 100.502),
          'beijing': (39.904, 116.407), 'seoul': (37.566, 126.978), 'tokyo': (35.676, 139.65), 'jakarta': (-6.208, 106.846),
          'canberra': (-35.281, 149.13), 'wellington': (-41.286, 174.776), 'ottawa': (45.421, -75.697), 'washington': (38.907, -77.037),
          'mexicocity': (19.433, -99.133), 'lima': (-12.046, -77.043), 'brasilia': (-15.794, -47.882), 'buenosaires': (-34.604, -58.382),
          'santiago': (-33.449, -70.669)}
GROUPS = {'copper (Cu)': lambda x: x['layer'] == 'Cu', 'tin (Sn)': lambda x: x['layer'] == 'Sn' or any(o['code'] in ('cassiterite', 'earth-placertin') for o in x['ores']),
          'iron (Fe)': lambda x: x['layer'] == 'Fe', 'gold (Au)': lambda x: any(o['code'] in ('gold', 'earth-placergold') for o in x['ores']),
          'silver (Ag)': lambda x: any(o['code'] == 'silver' for o in x['ores']), 'lead/zinc': lambda x: x['layer'] in ('Pb', 'Zn'),
          'sulfur (S)': lambda x: any(o['code'] == 'sulfur' for o in x['ores']), 'borax (B)': lambda x: x['layer'] == 'B',
          'bauxite (Al)': lambda x: x['layer'] == 'Al', 'salt (NaCl)': lambda x: x['layer'] == 'NaCl', 'coal (deposits)': lambda x: x['layer'] == 'C_coal'}
def dist_km(a, b):
    la1, lo1, la2, lo2 = map(math.radians, (a[0], a[1], b[0], b[1]))
    h = math.sin((la2 - la1) / 2) ** 2 + math.cos(la1) * math.cos(la2) * math.sin((lo2 - lo1) / 2) ** 2
    return 2 * 6371 * math.asin(math.sqrt(h))
def dist_blocks(a, b, bpd):     # world blocks on the cylinder (x: degrees of longitude at bpd, z: degrees of latitude)
    dlon = abs(a[1] - b[1]); dlon = min(dlon, 360 - dlon)
    return math.hypot(dlon * bpd, (a[0] - b[0]) * bpd)
# areas: use their ring vertices' centroid and inside test approx by the nearest vertex
def area_pts(a):
    g = a['geometry']; pts = []
    polys = [g['coordinates']] if g['type'] == 'Polygon' else g['coordinates']
    for poly in polys:
        for p in poly[0]: pts.append((p[1], p[0]))
    return pts
area_groups = {'coal (basins)': lambda a: a['layer'] == 'C_coal', 'salt/potash (basins)': lambda a: a['layer'] in ('NaCl', 'K'),
               'copper (areas)': lambda a: a['layer'] == 'Cu', 'iron (areas)': lambda a: a['layer'] == 'Fe', 'gypsum (areas)': lambda a: a['layer'] == 'gypsum',
               'phosphorite (areas)': lambda a: a['layer'] == 'P', 'sulfur (areas)': lambda a: a['layer'] == 'S'}
print('\nCount, mean spacing sqrt(land/N), and nearest catalogue entry from the 36 spawn cities (km; blocks at 1:100 / 1:10 on the cylinder): median, 75th pct, max')
bpd100, bpd10 = 111080 / 100.2 * 100.2 / 100.0 * 1.0, 0   # placeholder
BPD = {'1:100': 1110.8, '1:10': 11108}
res = {}
for gname, pred in GROUPS.items():
    pts = [(x['lat'], x['lon']) for x in deps if pred(x)]
    if not pts: continue
    near = sorted(min(dist_km(s, p) for p in pts) for s in SPAWNS.values())
    nb100 = sorted(min(dist_blocks(s, p, BPD['1:100']) for p in pts) for s in SPAWNS.values())
    nb10 = sorted(min(dist_blocks(s, p, BPD['1:10']) for p in pts) for s in SPAWNS.values())
    sp = math.sqrt(LAND_KM2 / len(pts))
    q = lambda v, f: v[min(len(v) - 1, int(f * len(v)))]
    print(f" {gname:<22} N={len(pts):>5} spacing {sp:>6.0f} km | nearest km med {q(near,.5):>5.0f} p75 {q(near,.75):>5.0f} max {near[-1]:>5.0f} | 1:100 blk med {q(nb100,.5):>7.0f} max {nb100[-1]:>8.0f} | 1:10 blk med {q(nb10,.5):>8.0f} max {nb10[-1]:>9.0f}")
    res[gname] = {k: min(dist_km(s, p) for p in pts) for k, s in SPAWNS.items()}
for gname, pred in area_groups.items():
    ar = [a for a in areas if pred(a)]
    if not ar: continue
    pts = [p for a in ar for p in area_pts(a)]
    near = sorted(min(dist_km(s, p) for p in pts) for s in SPAWNS.values())
    q = lambda v, f: v[min(len(v) - 1, int(f * len(v)))]
    print(f" {gname:<22} N={len(ar):>5} areas | nearest edge vertex km med {q(near,.5):>5.0f} p75 {q(near,.75):>5.0f} max {near[-1]:>5.0f}")
    res[gname] = {k: min(dist_km(s, p) for p in pts) for k, s in SPAWNS.items()}
print('\nPer spawn: nearest catalogue entry (km) for copper, tin, iron, coal basin, salt basin, gold')
for k in SPAWNS:
    print(f" {k:<12}" + ' '.join(f"{g.split(' ')[0]}={res[g][k]:.0f}" for g in ['copper (Cu)', 'tin (Sn)', 'iron (Fe)', 'coal (basins)', 'salt/potash (basins)', 'gold (Au)', 'sulfur (S)', 'borax (B)', 'bauxite (Al)']))
json.dump({'spawn_nearest_km': res}, open(os.path.join(HERE, 'catalogue.json'), 'w'), indent=1)

# totals by commodity of ore tonnage for the global average check
print('\nGlobal endowment -> average blocks per land chunk (catalogue spread over land), v at the top of each curve')
for code_grp, pred, dens in [('Cu ore', lambda x: x['layer'] == 'Cu', 2.7), ('Fe ore', lambda x: x['layer'] == 'Fe', 3.5), ('Au ore', lambda x: x['layer'] == 'Au', 2.7),
                             ('Sn ore', lambda x: x['layer'] == 'Sn', 2.5), ('Zn+Pb ore', lambda x: x['layer'] in ('Zn', 'Pb'), 3.0)]:
    mt = sum(x['mt'] for x in deps if pred(x))
    thick = mt * 1e6 / dens / (LAND_KM2 * 1e6)
    print(f" {code_grp:<10} {mt/1000:>8.1f} Gt in deposits (areas excluded) -> mean {thick*1000:.3f} mm over land -> " +
          ', '.join(f"{n}: {thick*1024/CURVES[ck].top:.3f} blk/chunk" for n, hz, ck in SC[:4]))
amt = collections.defaultdict(float)
for a in areas:
    if isinstance(a.get('mt'), (int, float)): amt[a['layer']] += a['mt']
print(' area tonnage by layer (Mt, from the files\' mt):', {k: round(v) for k, v in amt.items()})
