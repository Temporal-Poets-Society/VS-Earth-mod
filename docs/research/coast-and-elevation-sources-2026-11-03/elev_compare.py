"""How much elevation detail the local ETOPO 2022 files hold, compared with Earth's baked 2' grid (the mean of 2x2 1'
cells, tools/bake_earth.py "downsampling to 2'"). Prints summit heights and a cross-section of the Aurlandsfjord.
usage: elev_compare.py <ETOPO 60s surface tif> <ETOPO 30s surface tif>"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))   # etopo_grid beside this script (python -I leaves it out)
from etopo_grid import Grid, mean2

g60, g30 = Grid(sys.argv[1]), Grid(sys.argv[2])

PEAKS = [("Everest", 27.9881, 86.9250, 8849), ("K2", 35.8808, 76.5155, 8611), ("Aconcagua", -32.6532, -70.0109, 6961),
         ("Denali", 63.0695, -151.0074, 6190), ("Mont Blanc", 45.8326, 6.8652, 4806), ("Matterhorn", 45.9763, 7.6586, 4478),
         ("Fuji", 35.3606, 138.7274, 3776), ("Galdhøpiggen", 61.6364, 8.3125, 2469)]

print("Highest cell within 0.05 degrees of the summit, m (real height last)")
print(f"{'':14}{'2-min mean (Earth)':>19}{'1 min':>8}{'30 s':>8}{'real':>8}")
for name, lat, lon, real in PEAKS:
    r, c = g60.rc(lat, lon)
    n = 4
    r0, c0 = (r - n) // 2 * 2, (c - n) // 2 * 2
    a60 = g60.window(r0, r0 + 2 * n, c0, c0 + 2 * n)
    r, c = g30.rc(lat, lon)
    a30 = g30.window(r - 2 * n, r + 2 * n, c - 2 * n, c + 2 * n)
    print(f"{name:14}{mean2(a60).max():>19.0f}{a60.max():>8.0f}{a30.max():>8.0f}{real:>8}")

# Aurlandsfjord, west to east at 60.926 N: the fjord surface is sea level, the walls rise to about 1,400-1,700 m
lat, lon0, lon1 = 60.926, 7.00, 7.32
print(f"\nCross-section at {lat} N, {lon0}-{lon1} E (fjord near 7.15 E): lowest and highest cell, and cells per km E-W")
for label, g, two in (("2-min mean (Earth)", g60, True), ("1 min", g60, False), ("30 s", g30, False)):
    r, c0 = g.rc(lat, lon0)
    _, c1 = g.rc(lat, lon1)
    if two:
        r0, cc0 = r // 2 * 2, c0 // 2 * 2
        row = mean2(g.window(r0, r0 + 2, cc0, c1 // 2 * 2 + 2))[0]
        per_deg = g.res / 2
    else:
        row = g.window(r, r + 1, c0, c1 + 1)[0]
        per_deg = g.res
    km = 111.32 * math.cos(math.radians(lat)) / per_deg
    print(f"  {label:20} min {row.min():>7.0f}  max {row.max():>6.0f}  cell {km*1000:>5.0f} m E-W  profile: {' '.join(f'{v:.0f}' for v in row)}")
