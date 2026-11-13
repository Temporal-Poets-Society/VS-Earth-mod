"""Preview images of the baked data (reads preview/grids.npz written by bake_earth.py)."""
import os
import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
P = os.path.join(ROOT, "preview")
g = np.load(os.path.join(P, "grids.npz"))
elev, flags, ice, seam = g["elev"], g["flags"], g["ice"], g["seam"]
RES = elev.shape[1] // 360


def colorize(e, fl, ic):
    """Hypsometric tint + hillshade, water blue, lakes teal, ice white."""
    h, w = e.shape
    img = np.zeros((h, w, 3), np.float32)
    land_stops = [(0, (70, 120, 60)), (300, (120, 150, 80)), (1000, (170, 160, 100)), (2500, (150, 120, 90)), (4500, (200, 200, 200)), (8000, (255, 255, 255))]
    sea_stops = [(0, (120, 170, 220)), (-200, (70, 130, 200)), (-2000, (30, 70, 150)), (-6000, (10, 30, 90)), (-11000, (5, 10, 50))]

    def ramp(v, stops):
        xs = [s[0] for s in stops]
        out = np.zeros(v.shape + (3,), np.float32)
        order = np.argsort(xs)
        xs = np.array(xs)[order]
        cols = np.array([stops[i][1] for i in order], np.float32)
        for k in range(3):
            out[..., k] = np.interp(v, xs, cols[:, k])
        return out

    land = ramp(np.maximum(e, 0), land_stops)
    sea = ramp(np.minimum(e, 0), sea_stops)
    ocean = (fl & 1) > 0
    lake = (fl & 2) > 0
    img = np.where(ocean[..., None], sea, land)
    img = np.where(lake[..., None], np.array([60, 170, 190], np.float32), img)
    img = np.where((ic > 30)[..., None] & ~ocean[..., None], np.array([235, 240, 250], np.float32), img)
    # hillshade on land
    gy, gx = np.gradient(e.astype(np.float32))
    shade = np.clip(1 + (gx - gy) * 0.004, 0.6, 1.3)
    img = np.where(ocean[..., None] | lake[..., None], img, img * shade[..., None])
    return np.clip(img, 0, 255).astype(np.uint8)


def global_map(step=3):
    e = elev[::step, ::step]
    img = Image.fromarray(colorize(e, flags[::step, ::step], ice[::step, ::step]))
    d = ImageDraw.Draw(img)
    h, w = e.shape
    lat = np.linspace(90, -90, len(seam))
    pts = [(((lon + 180) % 360) * w / 360, (90 - la) * h / 180) for la, lon in zip(lat, seam)]
    for a, b in zip(pts, pts[1:]):
        if abs(a[0] - b[0]) < w / 2:
            d.line([a, b], fill=(255, 0, 0), width=1)
    img.save(os.path.join(P, "global.png"))


def region(name, lat0, lat1, lon0, lon1, scale=2):
    r0, r1 = int((90 - lat1) * RES), int((90 - lat0) * RES)
    cols = (np.arange(int(lon0 * RES), int(lon1 * RES)) + 180 * RES) % (360 * RES)
    e = elev[r0:r1][:, cols]
    img = Image.fromarray(colorize(e, flags[r0:r1][:, cols], ice[r0:r1][:, cols]))
    img = img.resize((img.width * scale, img.height * scale), Image.NEAREST)
    d = ImageDraw.Draw(img)
    lat = np.linspace(90, -90, len(seam))
    pts = []
    for la, lon in zip(lat, seam):
        if lat0 <= la <= lat1:
            lo = lon if lon >= lon0 else lon + 360
            pts.append(((lo - lon0) * RES * scale, (lat1 - la) * RES * scale))
    if len(pts) > 1:
        d.line(pts, fill=(255, 0, 0), width=max(1, scale))
    img.save(os.path.join(P, f"region-{name}.png"))


def climate():
    t, rain = g["t_mean"], g["rain"]
    e4 = elev.reshape(180 * 4, RES // 4, 360 * 4, RES // 4).mean(axis=(1, 3)) if RES % 4 == 0 else None
    ts = t - 0.0065 * np.maximum(e4, 0) if e4 is not None else t
    def cmap(v, lo, hi, c0, c1, c2):
        x = np.clip((v - lo) / (hi - lo), 0, 1)[..., None]
        c0, c1, c2 = (np.array(c, np.float32) for c in (c0, c1, c2))
        return np.where(x < 0.5, c0 + (c1 - c0) * x * 2, c1 + (c2 - c1) * (x - 0.5) * 2).astype(np.uint8)
    Image.fromarray(cmap(ts, -40, 32, (40, 40, 200), (240, 240, 240), (200, 30, 20))).save(os.path.join(P, "climate-temp.png"))
    Image.fromarray(cmap(rain, 0, 255, (200, 170, 90), (120, 200, 90), (20, 90, 60))).save(os.path.join(P, "climate-rain.png"))


if __name__ == "__main__":
    global_map()
    region("bering", 50, 72, 160, 215, 3)
    region("europe", 34, 62, -12, 42, 1)
    region("alps", 43, 49, 4, 17, 4)
    region("caspian", 35, 48, 45, 57, 3)
    region("bering-strait", 62, 68, 185, 197, 6)
    region("aleutians", 50, 58, 185, 200, 4)
    region("seam-south", -80, 30, 180, 205, 1)
    climate()
    print("previews written to", P)
