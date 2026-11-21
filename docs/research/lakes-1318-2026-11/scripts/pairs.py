import sys
OUTDIR = __import__('os').environ.get('OUTDIR', 'out')
sys.argv = ['x', 'out']
src = open('check.py', encoding='utf-8').read().split('places = sys.argv')[0]
exec(src)
import numpy as np
for spec in sys.stdin.read().split():
    place, scale = spec.split(':')
    h, lat, lon, flags, top, wtop, fine, elev, lakelv, gridlv = load(f'{OUTDIR}/{place}_{scale}_base.bin')
    water = (flags & 1) != 0; river = (flags & 2) != 0; inl = (flags & 65536) != 0; outl = (flags & 4) != 0
    pairs = {}; where = []
    nz, nx = water.shape
    for dz, dx in ((0, 1), (1, 0)):
        a = (slice(0, nz - dz), slice(0, nx - dx)); b = (slice(dz, nz), slice(dx, nx))
        m = water[a] & water[b] & (wtop[a] != wtop[b]) & (inl[a] | inl[b])
        ka = np.where(inl[a], 'L', np.where(river[a], 'r', np.where(outl[a], 'm', 'g')))[m]
        kb = np.where(inl[b], 'L', np.where(river[b], 'r', np.where(outl[b], 'm', 'g')))[m]
        for x1, x2, t1, t2 in zip(ka, kb, wtop[a][m], wtop[b][m]):
            k = tuple(sorted([(str(x1), int(t1)), (str(x2), int(t2))]))
            pairs[k] = pairs.get(k, 0) + 1
        jj, ii = np.where(m)
        where += [(round(float(lat[j]), 4), round(float(lon[i]), 4)) for j, i in zip(jj[:3], ii[:3])]
    print(place, scale, pairs, where[:4])
