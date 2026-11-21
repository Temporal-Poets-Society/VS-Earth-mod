import sys, numpy as np
from scipy.ndimage import distance_transform_edt
d = sys.argv[1]
nx, ny = 3600, 1400
f = np.fromfile(d + r'\flags.u16', dtype='<u2').reshape(ny, nx)
land = (f & 1) != 0
lat = 84 - (np.arange(ny) + 0.5) * 0.1
w = np.cos(np.radians(lat))[:, None] * np.ones((1, nx))
bits = {'raw clay top 4 blk (interfluve)': 2, 'raw clay any depth (interfluve)': 1, 'alluvial/outwash (interfluve)': None,
        'valley alluvial/outwash (if a river)': 12, 'valley raw clay (if a river)': 13, 'laterite (bauxite)': 6, 'saprolite (kaolin)': 7,
        'peat (bog iron)': 5, 'sand/gravel top 3 blk (panning flint)': 10, 'playa (salt flats)': 8, 'gravel top 3 blk': 9}
glac = (f & (1 << 14)) != 0
regions = {'world': np.ones_like(land), 'glaciated': glac, 'not glaciated': ~glac,
           'Europe (35-72N,-10-40E)': None, 'N America (25-72N,-170..-50)': None, 'Africa': None, 'S America': None, 'Asia (E of 40E, N of 5N)': None, 'Australia': None}
LON = -180 + (np.arange(nx) + 0.5) * 0.1
LA, LO = np.meshgrid(lat, LON, indexing='ij')
regions['Europe (35-72N,-10-40E)'] = (LA >= 35) & (LA < 72) & (LO >= -10) & (LO < 40)
regions['N America (25-72N,-170..-50)'] = (LA >= 25) & (LA < 72) & (LO >= -170) & (LO < -50)
regions['Africa'] = (LA >= -35) & (LA < 35) & (LO >= -20) & (LO < 52)
regions['S America'] = (LA >= -56) & (LA < 13) & (LO >= -82) & (LO < -34)
regions['Asia (E of 40E, N of 5N)'] = (LA >= 5) & (LA < 78) & (LO >= 40)
regions['Australia'] = (LA >= -44) & (LA < -10) & (LO >= 112) & (LO < 155)
# distance in km: EDT per 10-degree latitude band with anisotropic sampling (approximate)
def dist_km(mask):
    out = np.full(mask.shape, np.inf)
    for b in range(0, ny, 50):
        lo, hi = max(0, b - 150), min(ny, b + 200)   # include neighbours so distances crossing the band edge count
        sub = mask[lo:hi]
        if not sub.any():
            continue
        latm = lat[b + 25 if b + 25 < ny else ny - 1]
        dd = distance_transform_edt(~sub, sampling=(11.12, max(0.5, 11.12 * np.cos(np.radians(latm)))))
        out[b:b + 50] = dd[b - lo:b - lo + 50]
    return out
print('land cells', land.sum(), 'glaciated share', (w * (land & glac)).sum() / (w * land).sum())
for name, bit in bits.items():
    have = ((f & ((1 << 3) | (1 << 4))) != 0) if bit is None else ((f & (1 << bit)) != 0)
    have &= land
    dk = dist_km(have)
    line = [f'{name}:']
    for rn, rm in regions.items():
        sel = land & rm
        ws = (w * sel).sum()
        if ws == 0: continue
        share = (w * (sel & have)).sum() / ws
        far100 = (w * (sel & (dk > 100))).sum() / ws
        far200 = (w * (sel & (dk > 200))).sum() / ws
        far1000 = (w * (sel & (dk > 1000))).sum() / ws
        line.append(f'  {rn}: present {100*share:.1f}%, >100 km {100*far100:.1f}%, >200 km {100*far200:.1f}%, >1000 km {100*far1000:.2f}%')
    print('\n'.join(line))
