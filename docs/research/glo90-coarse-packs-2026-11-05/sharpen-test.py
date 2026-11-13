"""Does a sharpening prefilter on 24" box means lower the Catmull-Rom error against GLO-90? Interior of single tiles."""
import numpy as np, tifffile, zlib, sys
S = r"C:\VS mods\Earth\rawdata\copernicus-glo90\Copernicus_DSM_COG_30_%s_DEM.tif"
tiles = ['N27_00_E086_00', 'N45_00_E007_00', 'N36_00_W113_00', 'N50_00_E007_00', 'N57_00_E024_00', 'N60_00_E006_00', 'N47_00_E010_00', 'N35_00_E138_00']
def cr(t):
    t2, t3 = t*t, t*t*t
    return np.stack([0.5*(-t3+2*t2-t), 0.5*(3*t3-5*t2+2), 0.5*(-3*t3+4*t2+t), 0.5*(t3-t2)])
for name in tiles:
    a = tifffile.imread(S % name).astype(np.float64)
    R, C = a.shape
    k = 8 if C == 1200 else None
    if C != 1200: continue  # keep to the 3" band for a square check
    m = a.reshape(R//8, 8, C//8, 8).mean(axis=(1,3))  # GLO bands (k x k) just for this test
    # native point (i, j) lies at coarse coordinate ((i + 0.5)/8 - 0.5)
    ii = np.arange(40, R-40); jj = np.arange(40, C-40)
    y = (ii + 0.5)/8 - 0.5; x = (jj + 0.5)/8 - 0.5
    y0 = np.floor(y).astype(int); x0 = np.floor(x).astype(int)
    wy = cr(y - y0); wx = cr(x - x0)
    res = {}
    for alpha in [0.0, 0.1, 0.2, 0.3, 0.4]:
        lap = np.zeros_like(m)
        lap[1:-1,1:-1] = m[1:-1,1:-1] - 0.25*(m[:-2,1:-1]+m[2:,1:-1]+m[1:-1,:-2]+m[1:-1,2:])
        v = m + alpha*lap
        # interpolate rows then columns
        rows = sum(wy[p][:,None] * v[y0-1+p][:, :] for p in range(4))  # (len(ii), C/8)
        est = sum(wx[q][None,:] * rows[:, x0-1+q] for q in range(4))
        e = est - a[np.ix_(ii, jj)]
        codes = np.round(v/4).astype(np.int32)
        d = (codes[1:,1:] - codes[1:,:-1] - codes[:-1,1:] + codes[:-1,:-1]).astype(np.int16).tobytes()
        res[alpha] = (np.sqrt((e**2).mean()), len(zlib.compress(d, 9)))
    base = res[0.0]
    print(name, ' '.join('a%.1f rms %.1f (%.1f%%) size %+.1f%%' % (al, r, 100*(r/base[0]-1), 100*(s/base[1]-1)) for al, (r, s) in res.items()))
