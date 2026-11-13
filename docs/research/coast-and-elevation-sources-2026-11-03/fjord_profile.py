"""A west-to-east profile across the Nærøyfjord in four elevation grids: Earth's 2' (mean of 2x2 ETOPO 1' cells), ETOPO
1' and 30", and one Copernicus DEM GLO-90 tile. Marks the water from OSM's coastline, prints figures, writes a PNG.
usage: fjord_profile.py <ETOPO 60s tif> <ETOPO 30s tif> <Copernicus GLO-90 tile tif> <OSM coastline json> <lat> <lon0> <lon1> <out png>
Each grid is sampled by nearest cell every ~10 m, so the steps in the chart are the grid's own cells."""
import json, math, os, sys
import numpy as np
import tifffile
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))   # etopo_grid beside this script (python -I leaves it out)
from etopo_grid import Grid

sys.stdout.reconfigure(encoding="utf-8")
p60, p30, pcop, posm = sys.argv[1:5]
lat, lon0, lon1 = map(float, sys.argv[5:8])
out = sys.argv[8]
lons = np.arange(lon0, lon1, 0.0002)
km = (lons - lon0) * 111.32 * math.cos(math.radians(lat))


def etopo_row(g, two):
    """Nearest-cell values along the profile; with two, Earth's 2' cells (2x2 means of 1')."""
    r = int((90 - lat) * g.res)
    c = ((lons + 180) * g.res).astype(int)
    if two:
        r0, ca, cb = r // 2 * 2, c.min() // 2 * 2, c.max() // 2 * 2 + 2
        win = g.window(r0, r0 + 2, ca, cb)
        cells = win.reshape(2, -1, 2).mean(axis=(0, 2))
        return cells[(c - ca) // 2]
    ca = c.min()
    return g.window(r, r + 1, ca, c.max() + 1)[0][c - ca]


def cop_row():
    """Copernicus tile: pixel-is-point, tiepoint and pixel scale from its GeoTIFF tags."""
    t = tifffile.TiffFile(pcop)
    a = t.pages[0].asarray()
    geo = t.geotiff_metadata
    sx, sy = geo["ModelPixelScale"][:2]
    x0, y0 = geo["ModelTiepoint"][3:5]
    r = int(round((y0 - lat) / sy))
    c = np.rint((lons - x0) / sx).astype(int)
    return a[r, c], 1 / sx, 1 / sy


def osm_water():
    """Longitudes where the profile crosses OSM's coastline, paired west to east (sea lies between each pair)."""
    xs = []
    for e in json.load(open(posm, encoding="utf-8"))["elements"]:
        g = e.get("geometry")
        if not g:
            continue
        for a, b in zip(g[:-1], g[1:]):
            if (a["lat"] - lat) * (b["lat"] - lat) < 0:
                x = a["lon"] + (lat - a["lat"]) / (b["lat"] - a["lat"]) * (b["lon"] - a["lon"])
                if lon0 <= x <= lon1:
                    xs.append(x)
    xs.sort()
    return list(zip(xs[::2], xs[1::2]))


g60, g30 = Grid(p60), Grid(p30)
cop, cop_px_lon, cop_px_lat = cop_row()
rows = [("Earth's 2′ grid", etopo_row(g60, True), "#2a78d6"),
        ("Copernicus GLO-90", cop, "#eb6834"),
        ("ETOPO 1′", etopo_row(g60, False), "#1baf7a"),
        ("ETOPO 30″", etopo_row(g30, False), "#eda100")]
water = osm_water()
kx = 111.32 * math.cos(math.radians(lat))
step_km = 0.0002 * kx

print(f"Profile at {lat} N, {lon0}-{lon1} E ({km[-1]:.1f} km)")
for w0, w1 in water:
    print(f"  OSM water {w0:.4f}-{w1:.4f} E: {(w1 - w0) * kx * 1000:.0f} m wide")
for name, v, _ in rows:
    low = v < (2 if name.startswith("Copernicus") else 0)
    print(f"  {name:20} min {v.min():7.0f}  max {v.max():6.0f}  at or below sea level over {low.sum() * step_km * 1000:5.0f} m")
print(f"  Copernicus cell here: {1000 * kx / cop_px_lon:.0f} m E-W x {1000 * 110.574 / cop_px_lat:.0f} m N-S")

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

SURFACE, INK, INK2, GRID = "#fcfcfb", "#0b0b0b", "#52514e", "#e6e5e0"
fig, ax = plt.subplots(figsize=(10, 5.2), dpi=120)
fig.patch.set_facecolor(SURFACE)
ax.set_facecolor(SURFACE)
for w0, w1 in water:
    ax.axvspan((w0 - lon0) * kx, (w1 - lon0) * kx, color="#dcdbd5", lw=0, zorder=0)
ax.axhline(0, color=INK2, lw=0.8, zorder=1)
for name, v, col in rows:
    ax.plot(km, v, color=col, lw=1.6, label=name, zorder=3, solid_joinstyle="round")
# direct labels at the east end, kept in the lines' order and spread at least 80 m apart, each with its line's swatch
ends = sorted(((v[-1], name, col) for name, v, col in rows), reverse=True)
ys = [ends[0][0]]
for y, _, _ in ends[1:]:
    ys.append(min(y, ys[-1] - 80))
for y, (_, name, col) in zip(ys, ends):
    ax.annotate("", (km[-1] + 0.25, y), xytext=(km[-1] + 0.05, y), annotation_clip=False,
                arrowprops=dict(arrowstyle="-", color=col, lw=2))
    ax.annotate(name, (km[-1] + 0.3, y), va="center", fontsize=9, color=INK, annotation_clip=False)
# the one value that tells the story: where Earth's grid puts the fjord's floor
earth = rows[0][1]
i = int(np.argmin(earth))
j = len(earth) - 1 - int(np.argmin(earth[::-1]))   # east end of that lowest cell
ax.annotate(f"Earth's grid: lowest {earth[i]:.0f} m, no sea", (km[j] - 0.8, earth[i] - 120), ha="left", va="center",
            fontsize=9, color=INK)
if water:
    w0, w1 = water[0]
    ax.annotate("water (OSM)", (((w0 + w1) / 2 - lon0) * kx, ax.get_ylim()[1]), xytext=(0, -12), textcoords="offset points",
                ha="center", fontsize=9, color=INK2)
ax.set_xlim(0, km[-1])
ax.set_xlabel(f"km east of {lon0}°E (at 1:10, 1 km is 100 blocks)", color=INK2)
ax.set_ylabel("elevation, m", color=INK2)
ax.grid(axis="y", color=GRID, lw=0.8)
for s in ("top", "right"):
    ax.spines[s].set_visible(False)
for s in ("left", "bottom"):
    ax.spines[s].set_color(GRID)
ax.tick_params(colors=INK2)
ax.legend(loc="lower left", frameon=False, fontsize=9, labelcolor=INK)
fig.text(0.08, 0.955, f"Across the Nærøyfjord at {lat}°N: what each elevation grid holds", color=INK, fontsize=12)
fig.text(0.08, 0.915, "Nearest cell every 10 m. Copernicus shows the water surface, not the fjord's depth.",
         color=INK2, fontsize=9)
fig.subplots_adjust(left=0.08, right=0.8, top=0.87)
fig.savefig(out, facecolor=SURFACE)
print("wrote", out)
