"""Decision 0107 probe check: per window and scale, the lakes of 1318 (works LAKE areas) as a new world draws them against
an rc.14 world (variant w5). Counts: water of the lakes of 1318, its water tops (one level?), lake water beside lower dry land,
water-surface steps between neighbouring water columns, river water touching the lake, dry land inside a LAND area.
Draws <place>_<scale>_<variant>.png.

Usage: python -I check.py <out dir> [places...]
"""
import json, os, sys
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

D = sys.argv[1]
sys.stdout.reconfigure(encoding="utf-8")


def load(path):
    b = open(path, "rb").read()
    nl = b.index(b"\n")
    h = json.loads(b[:nl])
    o = nl + 1
    nz, nx = h["nz"], h["nx"]
    n = nz * nx

    def arr(dt, count):
        nonlocal o
        a = np.frombuffer(b, dtype=dt, count=count, offset=o)
        o += a.nbytes
        return a
    lat = arr("<f8", nz); lon = arr("<f8", nx)
    flags = arr("<u4", n).reshape(nz, nx)
    top = arr("<i2", n).reshape(nz, nx).astype(np.int32)
    wtop = arr("<i2", n).reshape(nz, nx).astype(np.int32)
    fine = arr("<f4", n).reshape(nz, nx)
    elev = arr("<f4", n).reshape(nz, nx)
    lakelv = arr("<f4", n).reshape(nz, nx)
    gridlv = arr("<f4", n).reshape(nz, nx)
    return h, lat, lon, flags, top, wtop, fine, elev, lakelv, gridlv


def neighbours(a, b, fn):
    """fn(a_here, b_there) over the four neighbour directions; returns the 'here' mask."""
    out = np.zeros(a.shape, bool)
    nz, nx = a.shape
    for dz, dx in ((0, 1), (1, 0), (0, -1), (-1, 0)):
        sa = (slice(max(0, -dz), nz - max(0, dz)), slice(max(0, -dx), nx - max(0, dx)))
        sb = (slice(max(0, dz), nz + min(0, dz)), slice(max(0, dx), nx + min(0, dx)))
        out[sa] |= fn(sa, sb)
    return out


def analyse(place, scale, variant):
    path = os.path.join(D, f"{place}_{scale}_{variant}.bin")
    if not os.path.exists(path):
        return None
    h, lat, lon, flags, top, wtop, fine, elev, lakelv, gridlv = load(path)
    km2 = ((abs(lat[1] - lat[0]) * 111.32) * (abs(lon[1] - lon[0]) * 111.32 * np.cos(np.radians(lat))))[:, None] * np.ones((1, len(lon)))
    water = (flags & 1) != 0
    river = (flags & 2) != 0
    lakew = (flags & 8) != 0
    sea = (flags & 16) != 0
    beach = (flags & 32) != 0
    in_lake1318 = (flags & 65536) != 0
    in_land1318 = (flags & 131072) != 0
    term = (flags & 16384) != 0
    w1318 = in_lake1318 & water & ~river
    r = {"works": h.get("works"), "riverEnds": h.get("riverEnds")}
    r["lake1318_km2"] = round(float(km2[in_lake1318].sum()), 1)
    r["lake1318_water_km2"] = round(float(km2[w1318].sum()), 1)
    r["lake1318_dry_km2"] = round(float(km2[in_lake1318 & ~water].sum()), 2)
    v, c = np.unique(wtop[w1318], return_counts=True)
    r["lake1318_water_tops"] = {int(a): int(b) for a, b in zip(v, c)}
    r["all_lake_water_km2"] = round(float(km2[lakew & ~river].sum()), 1)
    r["grid_terminal_water_km2"] = round(float(km2[term & water & ~in_lake1318].sum()), 1)
    # lake water beside lower dry land (water standing above the land)
    above = neighbours(water, water, lambda sa, sb: water[sa] & ~river[sa] & ~water[sb] & (top[sb] < wtop[sa]))
    r["water_beside_lower_land"] = int(above.sum())
    r["water_beside_lower_land_1318"] = int((above & in_lake1318).sum())
    # steps: neighbouring water columns (lake or river) with different water tops
    step = neighbours(water, water, lambda sa, sb: water[sa] & water[sb] & (wtop[sa] != wtop[sb]))
    r["water_steps"] = int(step.sum())
    r["water_steps_at_1318"] = int((step & (in_lake1318 | neighbours(in_lake1318, in_lake1318, lambda sa, sb: in_lake1318[sb]))).sum())
    # rivers touching the lake of 1318
    touch = neighbours(water, water, lambda sa, sb: water[sa] & river[sa] & ~in_lake1318[sa] & w1318[sb])
    lab, nlab = ndi.label(touch, structure=np.ones((3, 3)))
    r["river_mouths_at_1318"] = int(nlab)
    r["river_water_km2"] = round(float(km2[water & river].sum()), 1)
    r["land1318_km2"] = round(float(km2[in_land1318].sum()), 1)
    r["land1318_water_km2"] = round(float(km2[in_land1318 & water & ~river].sum()), 2)
    # map
    f = np.nan_to_num(fine, nan=0)
    lo_, hi_ = np.percentile(f, 2), np.percentile(f, 98)
    s = (90 + 120 * np.clip((f - lo_) / max(1, hi_ - lo_), 0, 1)).astype(np.uint8)
    img = np.stack([s, s, s - 10], -1)
    img[beach & ~water] = (220, 200, 140)
    img[in_land1318 & ~water] = (200, 150, 120)
    img[sea] = (20, 40, 120)
    img[lakew & ~river & ~in_lake1318] = (0, 190, 200)     # today's lakes (mask, grid): cyan
    img[w1318] = (30, 90, 200)                             # the lakes of 1318: blue
    img[water & river] = (150, 50, 220)                    # rivers: purple
    img[in_lake1318 & ~water] = (230, 120, 30)             # dry inside a lake of 1318: orange
    img[step] = (255, 0, 0)
    img[above & ~step] = (255, 0, 255)
    Image.fromarray(img).save(os.path.join(D, f"{place}_{scale}_{variant}.png"))
    return r


places = sys.argv[2:] or sorted({f.split("_")[0] for f in os.listdir(D) if f.endswith(".bin")})
for place in places:
    for scale in (10, 100):
        for variant in ("base", "w5"):
            r = analyse(place, scale, variant)
            if r is not None:
                print(f"{place} 1:{scale} {variant}: " + ", ".join(f"{k} {v}" for k, v in r.items()))
