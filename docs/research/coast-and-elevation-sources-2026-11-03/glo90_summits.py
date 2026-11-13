"""Highest GLO-90 point within about 1 km of each surveyed summit, against its surveyed height and the 2'/1'/30" ETOPO
values in the note. usage: glo90_summits.py <folder with Copernicus_DSM_COG_30_*.tif>"""
import math, os, sys
import numpy as np, tifffile

sys.stdout.reconfigure(encoding="utf-8")
SUMMITS = [("Everest", 27.9881, 86.9250, 8849), ("K2", 35.8825, 76.5133, 8611), ("Aconcagua", -32.6532, -70.0109, 6961),
           ("Denali", 63.0692, -151.0070, 6190), ("Mont Blanc", 45.8326, 6.8652, 4806), ("Matterhorn", 45.9763, 7.6586, 4478),
           ("Fuji", 35.3606, 138.7274, 3776), ("Galdhøpiggen", 61.6364, 8.3125, 2469)]


def tile(lat, lon):
    la, lo = math.ceil(lat) - 1, math.floor(lon)
    return f"Copernicus_DSM_COG_30_{'N' if la >= 0 else 'S'}{abs(la):02d}_00_{'E' if lo >= 0 else 'W'}{abs(lo):03d}_00_DEM.tif", la, lo


for name, lat, lon, real in SUMMITS:
    f, la, lo = tile(lat, lon)
    p = os.path.join(sys.argv[1], f)
    if not os.path.exists(p):
        print(f"{name:13} {real:5} m  tile {f} not here yet")
        continue
    h = tifffile.imread(p)
    rows, cols = h.shape
    r = (la + 1 - lat) * rows
    c = (lon - lo) * cols
    dr = int(round(0.009 * rows)) + 1                       # about 1 km north-south
    dc = int(round(0.009 / math.cos(math.radians(lat)) * cols)) + 1
    win = h[max(0, int(r) - dr): int(r) + dr + 1, max(0, int(c) - dc): int(c) + dc + 1]
    top = float(win.max())
    print(f"{name:13} {real:5} m  GLO-90 {top:7.0f} m  ({top - real:+5.0f} m)")
