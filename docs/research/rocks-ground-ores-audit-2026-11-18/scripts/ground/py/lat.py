import sys, numpy as np
d = sys.argv[1]
nx, ny = 3600, 1400
r = np.fromfile(d + r'\recipe.u8', dtype=np.uint8).reshape(ny, nx)
names = open(d + r'\recipes.txt').read().split('\n')
lat = 84 - (np.arange(ny) + 0.5) * 0.1
lon = -180 + (np.arange(nx) + 0.5) * 0.1
LA, LO = np.meshgrid(lat, lon, indexing='ij')
w = np.cos(np.radians(LA))
land = r > 0
lat_rec = [names.index(n) for n in ('laterite', 'saprolite', 'saprolite-mafic', 'sand-on-saprolite') if n in names]
for band in [(35, 90), (30, 35), (23.5, 30), (0, 23.5)]:
    sel = land & (np.abs(LA) >= band[0]) & (np.abs(LA) < band[1])
    v = (w * (sel & np.isin(r, lat_rec))).sum() / (w * sel).sum()
    print(f'|lat| {band}: recipes with a laterite layer (laterite/saprolite/saprolite-mafic/sand-on-saprolite) on {100*v:.1f}% of land')
for bn, (s, n, wl, el) in {'Europe 35-60N 10W-40E': (35, 60, -10, 40), 'USA SE 30-37N 95-75W': (30, 37, -95, -75), 'China S 22-32N 100-122E': (22, 32, 100, 122), 'Japan 31-42N 129-146E': (31, 42, 129, 146)}.items():
    sel = land & (LA >= s) & (LA < n) & (LO >= wl) & (LO < el)
    v = (w * (sel & np.isin(r, lat_rec))).sum() / (w * sel).sum()
    print(f'{bn}: {100*v:.1f}%')
