"""Vanilla GenDeposits.GeneratePartial rounds a fractional attempt count with NextInt(100) < 100*frac: the chance of the
extra attempt is ceil(100*frac)/100 (1 % steps). Effect on Earth's rules v4 background attempts at fine scales."""
import json, math, os, random
HERE = os.path.dirname(os.path.abspath(__file__))
out = json.load(open(os.path.join(HERE, 'scaling.json')))
rnd = random.Random(7)

def smooth(t): return t * t * (3 - 2 * t)
# value-noise texture samples (OreRules.ChunkNoise: lattice of 8 chunks, smoothstep bilinear)
lat = {}
def L(i, j):
    k = (i, j)
    if k not in lat: lat[k] = rnd.random()
    return lat[k]
noise = []
for cz in range(0, 400):
    for cx in range(0, 400, 3):
        ix, iz = cx // 8, cz // 8
        tx, tz = smooth((cx - ix * 8 + .5) / 8), smooth((cz - iz * 8 + .5) / 8)
        a, b, c, d = L(ix, iz), L(ix + 1, iz), L(ix, iz + 1), L(ix + 1, iz + 1)
        noise.append((a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz)
m4 = sum(n ** 4 for n in noise) / len(noise)
clustered = [.3 + .7 * n ** 4 / m4 for n in noise]
soft = [1 + .3 * (2 * n - 1) for n in noise]
print('texture means: clustered %.3f soft %.3f' % (sum(clustered) / len(clustered), sum(soft) / len(soft)))

def effective(rate, tex):
    s = 0
    for t in tex:
        x = rate * t
        n = int(x); f = x - n
        s += n + (math.ceil(100 * f - 1e-12) / 100 if f > 0 else 0)
    return s / len(tex)

combos = [('1:200', 384), ('1:100', 384), ('1:10', 384), ('1:10', 1024), ('1:5', 1024)]
print(f"{'code':<19}{'cls':<12}" + ''.join(f"{s+'/'+str(H):>22}" for s, H in combos))
for rec in out:
    tex = clustered if rec['mean'] < 1 else soft   # natively mapped & standard/progression -> clustered
    cells = []
    for s, H in combos:
        hs = H / 256 if rec['swh'] else 1
        rate = rec['tries'] * rec['mean'] * rec['ratio'] * rec[s]['att_scale'] * hs
        eff = effective(rate, tex)
        cells.append(f"{rate:9.4f}->x{eff/rate if rate>0 else 0:5.2f}")
    print(f"{rec['code']:<19}{rec['cls']:<12}" + ''.join(f"{c:>22}" for c in cells))
