"""
Review cards for tools/water_atlas.py: for the top sites of each class, the river and channel courses that leave the real
water most, and named places, two pictures side by side:
  - Sentinel-2 cloudless 2016 (EOX, https://s2maps.eu; CC BY 4.0, contains modified Copernicus Sentinel data 2016),
  - a disagreement map on GLO-90's 3" grid: light blue where Earth and the WBM both have water, red where Earth has water
    on WBM land, dark blue where the WBM has water Earth has as land, beige land; the world's river courses (white) and
    river exit channels (orange) drawn on.
Writes preview/water-atlas/cards/<n>-s2.jpg, <n>-map.png and cards.json (what each card shows).

    .venv/Scripts/python -I tools/water_atlas_cards.py <game-rivers.bin> <channels.json> [--per-class 10]
"""
import argparse, json, math, os, sys, time, urllib.request

import numpy as np
import tifffile
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import water_atlas as wa

CARDS = os.path.join(wa.OUTDIR, "cards")
UA = "EarthVintageStoryMod-atlas/1.0"
WMS = ("https://tiles.maps.eox.at/wms?service=WMS&request=GetMap&version=1.1.1&layers=s2cloudless&styles=&srs=EPSG:4326"
       "&bbox={lo0},{la0},{lo1},{la1}&width={w}&height={h}&format=image/jpeg")
PX = 720                  # card width (pixels)
PLACES = [  # places the author reported (5-6 November 2026)
    ("Patagonia fjords by the Balmaceda glacier (author's screenshot)", -51.45, -73.40, 0.30),
    ("New York harbour: Hudson along Manhattan (author's chart)", 40.70, -74.03, 0.22),
    ("New Orleans: Mississippi and the canals (author's chart)", 29.85, -90.00, 0.70),
    ("Ganges delta west of the Meghna (author's chart)", 22.70, 89.90, 1.00),
    ("Niagara River (decision 0055)", 43.00, -79.00, 0.35),
]


def mosaic_wbm(la0, lo0, la1, lo1):
    """WBM classes over a box on a uniform 3" grid (narrow high-latitude tiles repeated across), 255 where no tile (sea)."""
    r = 1200
    H_ = int(round((la1 - la0) * r))
    W_ = int(round((lo1 - lo0) * r))
    out = np.full((H_, W_), 1, np.uint8)
    for tl in range(math.floor(la0), math.ceil(la1)):
        for tc in range(math.floor(lo0), math.ceil(lo1)):
            name = f"Copernicus_DSM_COG_30_{'N' if tl >= 0 else 'S'}{abs(tl):02d}_00_{'E' if tc >= 0 else 'W'}{abs(tc):03d}_00_WBM.tif"
            p = os.path.join(wa.WBM, name)
            if not os.path.exists(p):
                continue
            t = tifffile.imread(p)
            t = t[:, (np.arange(r) * t.shape[1]) // r]
            # rows/cols of this tile in the box
            y0 = int(round((la1 - (tl + 1)) * r)); x0 = int(round((tc - lo0) * r))
            ys, xs = max(y0, 0), max(x0, 0)
            ye, xe = min(y0 + r, H_), min(x0 + r, W_)
            if ye > ys and xe > xs:
                out[ys:ye, xs:xe] = t[ys - y0:ye - y0, xs - x0:xe - x0]
    return out


def earth_water(la0, lo0, la1, lo1, shape):
    """Earth's sea and lakes on the box's 3" grid (from the atlas cache)."""
    sea = np.load(os.path.join(wa.CACHE, "sea.npy"), mmap_mode="r")
    lakes = np.load(os.path.join(wa.CACHE, "lakes1m.npy"), mmap_mode="r")
    grid = np.load(os.path.join(wa.CACHE, "gridlakes2m.npy"), mmap_mode="r")
    h, w = shape
    lat = la1 - (np.arange(h) + 0.5) / 1200
    lon = lo0 + (np.arange(w) + 0.5) / 1200
    r30 = np.clip(((90 - lat) * 120).astype(int), 0, 180 * 120 - 1)
    c30 = ((lon + 180) * 120).astype(int) % (360 * 120)
    s = np.asarray(sea[np.ix_(r30, c30)]) > 0
    l1 = np.asarray(lakes[np.ix_(r30 // 2, c30 // 2)]) > 0
    g2 = np.asarray(grid[np.ix_(r30 // 4, c30 // 4)])
    return s | l1 | g2


def draw_map(la0, lo0, la1, lo1, reaches, rbox, channels):
    wbm = mosaic_wbm(la0, lo0, la1, lo1)
    ew = earth_water(la0, lo0, la1, lo1, wbm.shape)
    real = wbm > 0
    img = np.zeros(wbm.shape + (3,), np.uint8)
    img[:] = (236, 226, 200)
    img[real & ew] = (160, 200, 235)
    img[ew & ~real] = (220, 50, 40)
    img[real & ~ew] = (30, 70, 170)
    im = Image.fromarray(img)
    h, w = wbm.shape
    scale = PX / w
    im = im.resize((PX, max(1, int(round(h * scale)))), Image.NEAREST)
    d = ImageDraw.Draw(im)

    def xy(lat, lon):
        return ((lon - lo0) * 1200 * scale, (la1 - lat) * 1200 * scale)

    x0b, x1b, y0b, y1b = rbox
    sel = np.nonzero((x1b >= lo0) & (x0b <= lo1) & (y1b >= la0) & (y0b <= la1))[0]
    for i in sel:
        q, _, lo, la = reaches[i]
        if q < 100:
            continue
        d.line([xy(a, b) for a, b in zip(la, lo)], fill=(255, 255, 255), width=3 if q >= 1000 else 2)
        d.line([xy(a, b) for a, b in zip(la, lo)], fill=(20, 20, 20), width=1)
    for ch in channels:
        c = ch["course"]
        if any(la0 <= a <= la1 and lo0 <= b <= lo1 for a, b in c):
            d.line([xy(a, b) for a, b in c], fill=(255, 140, 0), width=4)
    return im


def fetch_s2(la0, lo0, la1, lo1, h, path):
    if os.path.exists(path):
        return True
    url = WMS.format(lo0=lo0, la0=la0, lo1=lo1, la1=la1, w=PX, h=h)
    for attempt in range(3):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": UA}), timeout=60) as r:
                b = r.read()
            if b[:2] == b"\xff\xd8":
                open(path, "wb").write(b)
                return True
        except Exception:
            time.sleep(2 + attempt * 3)
    return False


def box_around(lat, lon, half_deg, box=None):
    if box:
        la0, lo0, la1, lo1 = box
        cy, cx = (la0 + la1) / 2, (lo0 + lo1) / 2
        half = max((la1 - la0) / 2 * 1.4, (lo1 - lo0) / 2 * 1.4 * math.cos(math.radians(cy)), 0.06)
        lat, lon, half_deg = cy, cx, min(half, 1.2)
    kx = max(math.cos(math.radians(lat)), 0.2)
    return (round(lat - half_deg * 0.75, 4), round(lon - half_deg / kx, 4), round(lat + half_deg * 0.75, 4), round(lon + half_deg / kx, 4))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("rivers")
    ap.add_argument("channels")
    ap.add_argument("--per-class", type=int, default=10)
    ap.add_argument("--bands", help="course length shares by discharge (scratch course_bands.py output) for the page")
    ap.add_argument("--page-only", action="store_true", help="rebuild index.html from cards.json without new cards")
    a = ap.parse_args()
    if a.page_only:
        page(json.load(open(os.path.join(wa.OUTDIR, "cards.json"), encoding="utf8")), a.bands)
        return
    os.makedirs(CARDS, exist_ok=True)
    sites = json.load(open(os.path.join(wa.OUTDIR, "sites.json"), encoding="utf8"))
    islands = json.load(open(os.path.join(wa.OUTDIR, "islands.json"), encoding="utf8"))["lost"]
    courses = json.load(open(os.path.join(wa.OUTDIR, "courses.json"), encoding="utf8"))
    chans = json.load(open(a.channels, encoding="utf8"))["channels"]
    reaches = wa.read_reaches(a.rivers)
    rbox = (np.array([lo.min() for _, _, lo, _ in reaches]), np.array([lo.max() for _, _, lo, _ in reaches]),
            np.array([la.min() for _, _, _, la in reaches]), np.array([la.max() for _, _, _, la in reaches]))
    picks = []
    for name, lat, lon, half in PLACES:
        picks.append(dict(group="reported", title=name, box=box_around(lat, lon, half)))
    for c in wa.CLASSES:
        xs = [s for s in sites if s["class"] == c and not s["polar"] and not s["source"].startswith("reservoir") and s["core_km2"] > 0]
        for s in xs[:a.per_class]:
            note = f" ({s['mask_note']})" if s.get("mask_note") else ""
            picks.append(dict(group=c, title=f"{s['source']}: {s['km2']:.0f} km², land {s['height_m']:.0f} m, rough {s['rough_m']:.0f} m{note}",
                              site=s, box=box_around(s["lat"], s["lon"], 0, s["box"])))
    for i in [i for i in islands if i["in"] == "river"][:a.per_class]:
        picks.append(dict(group="island_lost", title=f"river island {i['km2']:.1f} km², {int(i['earth_water'] * 100)}% under Earth water",
                          site=i, box=box_around(i["lat"], i["lon"], max(0.08, math.sqrt(i["km2"]) / 111 * 2.5))))
    for k in ("channels", "rivers"):
        for r in courses.get(k, [])[:a.per_class + 4]:
            picks.append(dict(group="course_" + k, title=f"{'channel' if k == 'channels' else 'river'} Q {r['q']:.0f} m³/s: bed off real water "
                              f"{r['dry_km']:.1f} km (longest {r['longest_dry_km']:.1f}), centre on land {r['centre_land_km']:.1f} km",
                              site=r, box=box_around(r["at"][0], r["at"][1], max(0.1, min(0.6, r["longest_dry_km"] / 111 * 1.2)))))
    out = []
    for n, p in enumerate(picks):
        la0, lo0, la1, lo1 = p["box"]
        try:
            im = draw_map(la0, lo0, la1, lo1, reaches, rbox, chans)
        except Exception as e:
            print("map failed", n, p["title"], e)
            continue
        mp = os.path.join(CARDS, f"{n:03d}-map.png")
        im.save(mp, optimize=True)
        ok = fetch_s2(la0, lo0, la1, lo1, im.size[1], os.path.join(CARDS, f"{n:03d}-s2.jpg"))
        p.update(n=n, map=f"cards/{n:03d}-map.png", s2=f"cards/{n:03d}-s2.jpg" if ok else None)
        out.append(p)
        if n % 10 == 0:
            wa.log(f"{n + 1}/{len(picks)} cards")
    for p in out:
        la0, lo0, la1, lo1 = p["box"]
        p["id"] = f"{p['group']}_{(la0 + la1) / 2:.3f}_{(lo0 + lo1) / 2:.3f}"
        p.pop("site", None)
    json.dump(out, open(os.path.join(wa.OUTDIR, "cards.json"), "w", encoding="utf8"), ensure_ascii=False, indent=1)
    wa.log(f"{len(out)} cards in {CARDS}")
    page(out, a.bands)


def page(cards, bands_path):
    """preview/water-atlas/index.html: the review page (tools/water_atlas_page.html with the cards and counts filled in)."""
    import re
    counts = []
    for line in open(os.path.join(wa.OUTDIR, "summary.txt"), encoding="utf8"):
        m = re.match(r"(\w+): (\d+) sites, (\d+) km²", line)
        if m:
            counts.append(dict(key=m[1], sites=int(m[2]), km2=int(m[3])))
        m = re.match(r"island_lost: (\d+) of (\d+)", line)
        if m:
            counts.append(dict(key="island_lost", sites=int(m[1]), km2=None, note=f"of {int(m[2]):,} islands of 0.25 km² or more"))
    head = open(os.path.join(wa.OUTDIR, "courses.txt"), encoding="utf8").readline()
    m = re.match(r"channels: (\d+) whose bed", head)
    if m:
        counts.append(dict(key="course_channels", sites=int(m[1]), km2=None, note="channels whose bed leaves real water for 1 km or more"))
    bands = []
    if bands_path:
        for line in open(bands_path, encoding="utf8"):
            if line.strip() and not line.startswith(" "):
                if bands:
                    break  # first file only (the game's courses)
                continue
            m = re.match(r"\s+Q\s+(\d+)-\s*(\w+): +(\d+) km, bed off water +([\d.]+)%, centre on land +([\d.]+)%", line)
            if m:
                lab = f"{int(m[1]):,} m³/s and up" if m[2] == "max" else f"{int(m[1]):,} – {int(m[2]):,} m³/s"
                bands.append(dict(label=lab, km=int(m[3]), off=float(m[4]), centre=float(m[5])))
    data = dict(cards=[{k: c[k] for k in ("id", "group", "title", "box", "map", "s2")} for c in cards], counts=counts, bands=bands)
    fx = os.path.join(wa.OUTDIR, "fixes", "fixes.json")  # written by hand after the fixes: lead, results rows, shots
    if os.path.exists(fx):
        data["fixes"] = json.load(open(fx, encoding="utf8"))
    tpl = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "water_atlas_page.html"), encoding="utf8").read()
    open(os.path.join(wa.OUTDIR, "index.html"), "w", encoding="utf8").write(
        tpl.replace("/*DATA*/null", json.dumps(data, ensure_ascii=False).replace("</", "<\\/")))
    wa.log("index.html written")


if __name__ == "__main__":
    main()
