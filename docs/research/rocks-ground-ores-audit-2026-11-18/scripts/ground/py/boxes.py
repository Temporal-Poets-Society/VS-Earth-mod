import sys, numpy as np
d = sys.argv[1]
nx, ny = 3600, 1400
r = np.fromfile(d + r'\recipe.u8', dtype=np.uint8).reshape(ny, nx)
names = open(d + r'\recipes.txt').read().split('\n')
lat = 84 - (np.arange(ny) + 0.5) * 0.1
lon = -180 + (np.arange(nx) + 0.5) * 0.1
LA, LO = np.meshgrid(lat, lon, indexing='ij')
w = np.cos(np.radians(LA))
boxes = {'Sahel 8-18N 20W-40E': (8, 18, -20, 40), 'Sahara 18-32N 15W-35E': (18, 32, -15, 35), 'China Loess Plateau 34-40N 100-114E': (34, 40, 100, 114),
         'Ukraine-S Russia 45-52N 25-45E': (45, 52, 25, 45), 'US Great Plains 35-48N 105-95W': (35, 48, -105, -95), 'Pampas 38-30S 65-58W': (-38, -30, -65, -58),
         'Amazon 10S-2N 70-50W': (-10, 2, -70, -50), 'Congo 5S-5N 15-30E': (-5, 5, 15, 30), 'India Deccan 15-23N 73-80E': (15, 23, 73, 80),
         'SE Asia 10-25N 95-110E': (10, 25, 95, 110), 'Australia arid 30-20S 120-140E': (-30, -20, 120, 140), 'Fennoscandia 60-70N 10-30E': (60, 70, 10, 30),
         'Canadian Shield 50-60N 100-75W': (50, 60, -100, -75), 'Western Europe 45-55N 5W-15E': (45, 55, -5, 15), 'Mediterranean 36-44N 10W-30E': (36, 44, -10, 30)}
for bn, (s, n, wl, el) in boxes.items():
    sel = (LA >= s) & (LA < n) & (LO >= wl) & (LO < el) & (r > 0)
    tot = (w * sel).sum()
    if tot == 0: continue
    shares = []
    for i, nm in enumerate(names):
        if i == 0: continue
        v = (w * (sel & (r == i))).sum() / tot
        if v >= 0.02: shares.append((v, nm))
    shares.sort(reverse=True)
    print(f"{bn}: " + ', '.join(f"{nm} {100*v:.0f}%" for v, nm in shares))
