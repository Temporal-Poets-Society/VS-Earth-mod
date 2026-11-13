"""Procedural 32x32 ore textures (original pixel art; inspired by mineral habit/colour only).
Layout: A (0,0) matrix with ore, B (16,0) pure mineral surface, C (0,16) matrix variant, D (16,16) mineral variant.
Tetrahedrite, sperrylite, bog iron and bauxite are painted by their own functions (PAINT) in vanilla's soft-noise item
style; their shapes (gen_shapes.py: massive, studded, porous, earthy) sample the fixed spots noted there.

  python -I gen_textures.py          writes ./textures
  python -I gen_textures.py --live   also copies the textures of the live ore types to assets/earth/textures/item/ore
                                     and the nuggets (NUGGETS) to assets/earth/textures/item/nugget"""
import random, math, zlib, sys, json, shutil
from pathlib import Path
from PIL import Image, ImageDraw
OUT = Path(__file__).parent / "textures"
LIVE = Path(__file__).parents[3] / "assets" / "earth"


def mix(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))
def shade(c, f): return tuple(max(0, min(255, int(v * f))) for v in c)


def matrix_tile(rng, base, dark, light):
    im = Image.new("RGB", (16, 16)); px = im.load()
    for y in range(16):
        for x in range(16):
            t = rng.random()
            px[x, y] = mix(dark, base, t * 1.3) if t < .75 else mix(base, light, (t - .75) * 3)
    d = ImageDraw.Draw(im)
    for _ in range(4):
        x, y = rng.randrange(16), rng.randrange(16)
        d.line([x, y, x + rng.randint(-3, 3), y + rng.randint(-2, 2)], fill=shade(dark, .85))
    return im


def f_cubes(d, rng, pal, n, lo, hi):
    main, hi_c, lo_c, edge = pal
    for _ in range(n):
        s = rng.randint(lo, hi); x, y = rng.randint(-1, 16 - s + 1), rng.randint(-1, 16 - s + 1)
        d.rectangle([x, y, x + s - 1, y + s - 1], fill=main)
        for i in range(1, s, 2): d.line([x, y + i, x + s - 1, y + i], fill=shade(main, .88))
        d.line([x, y, x + s - 1, y], fill=hi_c); d.line([x, y, x, y + s - 1], fill=hi_c)
        d.line([x + s - 1, y + 1, x + s - 1, y + s - 1], fill=lo_c); d.line([x, y + s - 1, x + s - 1, y + s - 1], fill=lo_c)
        if s > 2: d.point((x + 1, y + 1), fill=edge)


def f_prisms(d, rng, pal, n, lo, hi):
    main, hi_c, lo_c, edge = pal
    for _ in range(n):
        w = rng.choice([3, 3, 4, 5]); h = rng.randint(lo, hi); x = rng.randint(-1, 15 - w + 1); y = rng.randint(0, 16 - h)
        d.rectangle([x, y, x + w - 1, y + h - 1], fill=main)
        d.line([x, y, x, y + h - 1], fill=hi_c); d.line([x + w - 1, y, x + w - 1, y + h - 1], fill=lo_c)
        if w > 3: d.line([x + 1, y, x + 1, y + h - 2], fill=mix(main, hi_c, .5))
        d.line([x, y, x + w - 1, y], fill=edge)


def f_plates(d, rng, pal, n, lo, hi):
    main, hi_c, lo_c, edge = pal
    for _ in range(n):
        s = rng.randint(lo, hi); x, y = rng.randint(0, 16 - s), rng.randint(0, 16 - s)
        if rng.random() < .4:
            c = (x + s // 2, y + s // 2); r = s // 2 + 1
            d.polygon([(c[0], c[1] - r), (c[0] + r, c[1]), (c[0], c[1] + r), (c[0] - r, c[1])], fill=main, outline=hi_c)
            d.point((c[0], c[1] - r + 1), fill=edge)
        else:
            d.rectangle([x, y, x + s - 1, y + s - 1], fill=main, outline=hi_c)
            d.line([x + s - 1, y + 1, x + s - 1, y + s - 1], fill=lo_c); d.line([x + 1, y + s - 1, x + s - 1, y + s - 1], fill=lo_c)
            d.point((x + 1, y + 1), fill=edge)


def f_needles(d, rng, pal, n, lo, hi):
    main, hi_c, lo_c, edge = pal
    for _ in range(n):
        x, y = rng.randint(0, 15), rng.randint(0, 15); a = rng.uniform(0, math.pi); l = rng.randint(lo, hi)
        x2, y2 = x + int(math.cos(a) * l), y + int(math.sin(a) * l)
        d.line([x + 1, y, x2 + 1, y2], fill=lo_c); d.line([x, y, x2, y2], fill=main); d.point((x, y), fill=edge)


def f_botryoids(d, rng, pal, n, lo, hi):
    main, hi_c, lo_c, edge = pal
    for _ in range(n):
        r = rng.randint(lo, hi); cx, cy = rng.randint(0, 15), rng.randint(0, 15)
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=lo_c)
        d.ellipse([cx - r, cy - r, cx + r - 1, cy + r - 1], fill=main)
        d.ellipse([cx - r + 1, cy - r + 1, cx, cy], fill=hi_c)
        d.point((cx - r + 1, cy - r + 1), fill=edge)


def f_tetra(d, rng, pal, n, lo, hi):
    main, hi_c, lo_c, edge = pal
    for _ in range(n):
        s = rng.randint(lo, hi); x, y = rng.randint(0, 16 - s), rng.randint(0, 16 - s)
        d.polygon([(x, y), (x + s, y), (x, y + s)], fill=hi_c); d.polygon([(x + s, y), (x + s, y + s), (x, y + s)], fill=lo_c)
        d.polygon([(x + 1, y + 1), (x + s - 1, y + 1), (x + 1, y + s - 1)], fill=main); d.point((x + 1, y + 1), fill=edge)


def f_grains(d, rng, pal, n, lo, hi):
    main, hi_c, lo_c, edge = pal
    for _ in range(n):
        x, y = rng.randint(0, 15), rng.randint(0, 15); s = rng.randint(lo, hi)
        d.rectangle([x, y, x + s - 1, y + s - 1], fill=main); d.point((x, y), fill=edge)
        if s > 1: d.point((x + s - 1, y + s - 1), fill=lo_c)


def f_pisolites(d, rng, pal, n, lo, hi):
    main, hi_c, lo_c, edge = pal
    for _ in range(n):
        r = rng.randint(lo, hi); cx, cy = rng.randint(0, 15), rng.randint(0, 15)
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=lo_c, outline=shade(lo_c, .7))
        d.ellipse([cx - r + 1, cy - r + 1, cx + r - 1, cy + r - 1], fill=main)
        d.point((cx - 1, cy - 1), fill=hi_c)


def f_blades(d, rng, pal, n, lo, hi):
    main, hi_c, lo_c, edge = pal
    for _ in range(n):
        x, y = rng.randint(-2, 12), rng.randint(0, 15); l = rng.randint(lo, hi); w = rng.choice([2, 3])
        for k in range(w):
            d.line([x + k, y, x + k + l // 2, y + l], fill=hi_c if k == 0 else (lo_c if k == w - 1 else main))
        d.point((x, y), fill=edge)


FEAT = dict(cubes=f_cubes, prisms=f_prisms, plates=f_plates, needles=f_needles, botryoids=f_botryoids,
            tetra=f_tetra, grains=f_grains, pisolites=f_pisolites, blades=f_blades)
# m = matrix (base, dark, light); p = mineral (main, highlight, shadow, glint)
ORES = {
 "pyrite":       dict(m=((150,128,96),(112,94,68),(184,164,128)), p=((212,182,78),(244,222,128),(136,108,38),(255,246,186)), f="cubes", sz=(3,5), n=7, fill=(4,7)),
 "chalcopyrite": dict(m=((92,88,80),(66,62,56),(124,118,108)), p=((190,150,48),(226,196,92),(110,72,40),(120,150,190)), f="tetra", sz=(3,5), n=5, fill=(2,4)),
 "cerussite":    dict(m=((74,104,88),(48,74,62),(104,134,112)), p=((232,230,218),(252,252,246),(168,168,160),(255,255,255)), f="prisms", sz=(3,6), n=8, fill=(4,7)),
 "smithsonite":  dict(m=((140,128,112),(104,94,82),(170,158,140)), p=((132,200,190),(190,238,228),(76,138,132),(236,255,250)), f="botryoids", sz=(2,3), n=6, fill=(5,9)),
 "hemimorphite": dict(m=((158,146,128),(120,108,92),(184,174,156)), p=((176,214,226),(224,246,252),(104,150,170),(255,255,255)), f="blades", sz=(7,11), n=6, fill=(4,8)),
 "vanadinite":   dict(m=((156,150,138),(116,110,100),(190,184,172)), p=((176,40,26),(228,96,60),(98,20,16),(255,170,130)), f="prisms", sz=(4,8), n=6, fill=(4,6)),
 "wulfenite":    dict(m=((178,164,142),(136,124,104),(204,192,170)), p=((236,142,30),(255,198,78),(170,86,18),(255,238,150)), f="plates", sz=(4,6), n=5, fill=(3,6)),
 "tetrahedrite": dict(m=((88,88,90),(58,58,62),(122,122,126)), p=((74,76,84),(126,130,142),(34,34,40),(196,200,210)), f="tetra", sz=(4,6), n=5, fill=(3,5)),
 "sperrylite":   dict(m=((68,66,64),(44,42,42),(98,96,94)), p=((216,220,224),(250,252,255),(140,146,154),(255,255,255)), f="grains", sz=(1,2), n=14, fill=(8,14)),
 "azurite":      dict(m=((110,98,84),(80,70,60),(140,128,112)), p=((34,70,176),(88,128,226),(16,30,100),(170,196,255)), f="botryoids", sz=(1,3), n=8, fill=(5,9)),
 "bogiron":      dict(m=((88,60,34),(58,40,24),(120,86,52)), p=((134,76,34),(176,112,60),(78,44,22),(214,150,90)), f="pisolites", sz=(2,3), n=10, fill=(6,10)),
 "bauxite":      dict(m=((150,92,64),(110,66,46),(184,126,92)), p=((176,98,62),(212,142,100),(118,62,42),(236,184,148)), f="pisolites", sz=(1,2), n=14, fill=(8,14)),
 "gypsum":       dict(m=((200,194,184),(160,154,144),(226,222,214)), p=((236,236,230),(255,255,255),(180,184,190),(255,255,255)), f="blades", sz=(8,12), n=5, fill=(3,6)),
}


# ------------------------------------------------------------------ soft-noise painters (vanilla item style)
def noise(rng, n=16, octaves=((3, .5), (6, .32), (16, .18))):
    """Smooth value noise 0..1 on an n x n tile (bicubic-upsampled random grids)."""
    acc = [[0.0] * n for _ in range(n)]
    for cells, amp in octaves:
        g = Image.new("L", (cells, cells)); g.putdata([rng.randrange(256) for _ in range(cells * cells)])
        g = g.resize((n, n), Image.BICUBIC) if cells < n else g
        px = g.load()
        for y in range(n):
            for x in range(n):
                acc[y][x] += amp * px[x, y] / 255
    lo = min(map(min, acc)); hi = max(map(max, acc))
    return [[(v - lo) / (hi - lo + 1e-9) for v in row] for row in acc]


def ramp(t, stops):
    for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
        if t <= p1:
            return mix(c0, c1, max(0.0, (t - p0) / (p1 - p0 + 1e-9)))
    return stops[-1][1]


def paint(rng, stops, tint=None, n=16):
    """Tile from a colour ramp over noise; tint=(rgb, strength) blends a second colour in where another noise is high."""
    a = noise(rng, n); b = noise(rng, n) if tint else None
    im = Image.new("RGB", (n, n)); px = im.load()
    for y in range(n):
        for x in range(n):
            c = ramp(min(1, max(0, a[y][x] + rng.uniform(-.06, .06))), stops)
            if tint and b[y][x] > .55:
                c = mix(c, tint[0], tint[1] * (b[y][x] - .55) / .45)
            px[x, y] = c
    return im


def tetrahedrite(rng):
    """Steel-grey massive tetrahedrite with a bronze sheen; triangular facets on the crystal quadrants (B/D);
    A/C carry some milky quartz gangue."""
    grey = [(0, (32, 33, 38)), (.35, (64, 66, 74)), (.7, (94, 95, 102)), (1, (128, 126, 124))]
    bronze = ((122, 98, 66), .5)
    sheet = Image.new("RGB", (32, 32))
    for q, (ox, oy) in enumerate([(0, 0), (16, 0), (0, 16), (16, 16)]):
        t = paint(rng, grey, bronze); px = t.load(); d = ImageDraw.Draw(t)
        if q in (0, 2):  # quartz gangue patches
            g = noise(rng)
            for y in range(16):
                for x in range(16):
                    if g[y][x] > .88:
                        px[x, y] = ramp(rng.random(), [(0, (164, 160, 150)), (1, (204, 200, 190))])
        else:            # tetrahedral facets: brighter triangle, lit left edge, dark base
            for _ in range(3):
                s = rng.randint(5, 8); x0, y0 = rng.randint(0, 16 - s), rng.randint(0, 16 - s)
                for y in range(y0, y0 + s):
                    half = (y - y0) / max(1, s - 1) * (s - 1) / 2
                    for x in range(x0, x0 + s):
                        if abs(x - (x0 + (s - 1) / 2)) <= half + .01:
                            px[x, y] = shade(px[x, y], 1.18)
                apex = (x0 + (s - 1) // 2, y0)
                d.line([(x0, y0 + s - 1), apex], fill=(176, 170, 160)); d.line([(x0, y0 + s - 1), (x0 + s - 1, y0 + s - 1)], fill=(40, 40, 46))
        for _ in range(4):  # metallic glints
            px[rng.randrange(16), rng.randrange(16)] = (198, 192, 180)
        sheet.paste(t, (ox, oy))
    return sheet


def sperrylite(rng):
    """Dark norite matrix with sparse tin-white grains (A/C); bright tin-white cube faces (B/D, sampled 2x2)."""
    dark = [(0, (42, 44, 40)), (.5, (72, 75, 68)), (1, (108, 110, 100))]
    white = [(0, (172, 176, 184)), (.5, (212, 216, 222)), (1, (240, 242, 246))]
    sheet = Image.new("RGB", (32, 32))
    for q, (ox, oy) in enumerate([(0, 0), (16, 0), (0, 16), (16, 16)]):
        if q in (0, 2):
            t = paint(rng, dark); px = t.load()
            for _ in range(rng.randint(13, 16)):         # single sparkling grains
                px[rng.randrange(16), rng.randrange(16)] = rng.choice([(236, 238, 242), (250, 250, 252)])
            for _ in range(2):                           # tiny cubes: lit corner, faces, shaded corner
                x, y = rng.randrange(15), rng.randrange(15)
                px[x, y] = (252, 252, 254); px[x + 1, y] = (210, 214, 220); px[x, y + 1] = (200, 204, 210); px[x + 1, y + 1] = (140, 144, 152)
            for _ in range(3):
                px[rng.randrange(16), rng.randrange(16)] = (104, 100, 90)   # dull sulphide grains
        else:
            t = paint(rng, white); px = t.load()
            for _ in range(10):
                px[rng.randrange(16), rng.randrange(16)] = (255, 255, 255)
            for _ in range(5):
                px[rng.randrange(16), rng.randrange(16)] = (150, 156, 166)
        sheet.paste(t, (ox, oy))
    return sheet


def bogiron(rng):
    """Porous rusty brown-black limonite: pits with a lit lower rim and root-channel grooves (A/C/D); tube wall
    streaks in B (rows 0-10, the porous form's root casts) and the open channel of a root cast at D (26-30, 26-30)."""
    rust = [(0, (26, 18, 13)), (.4, (54, 34, 21)), (.68, (92, 50, 25)), (.86, (138, 72, 32)), (1, (178, 104, 46))]
    sheet = Image.new("RGB", (32, 32))
    for q, (ox, oy) in enumerate([(0, 0), (16, 0), (0, 16), (16, 16)]):
        t = paint(rng, rust, ((172, 86, 34), .6)); px = t.load()
        if q != 1:
            for _ in range(rng.randint(16, 20)):          # pits: black hole, rust-lit lower rim
                x, y = rng.randrange(15), rng.randrange(15)
                px[x, y] = (16, 10, 8)
                if rng.random() < .3: px[x + 1, y] = (28, 17, 11)
                px[x, y + 1] = (176, 98, 42)
            for _ in range(2 if q != 3 else 0):           # root-channel grooves
                x, y = rng.randrange(1, 11), rng.randrange(1, 13); dy = rng.choice((1, -1, 0))
                for k in range(rng.randint(3, 5)):
                    xx = min(15, x + k); yy = min(14, max(0, y + (k * dy) // 2))
                    px[xx, yy] = (26, 16, 11); px[xx, yy + 1] = (150, 88, 40)
        else:                                             # tube wall: ochre-rust crust, streaks along u
            for y in range(16):
                f = (.8, 1.0, 1.12, .9)[y % 4]
                for x in range(16):
                    px[x, y] = shade(mix(px[x, y], (164, 96, 42), .55), f * rng.uniform(.94, 1.06))
        sheet.paste(t, (ox, oy))
    px = sheet.load()
    for y in range(26, 30):                               # open root channel: rust ring, black core
        for x in range(26, 30):
            px[x, y] = (146, 84, 38) if (x in (26, 29) or y in (26, 29)) else (18, 11, 8)
    px[26, 26] = (176, 112, 56); px[28, 28] = (54, 32, 18)
    return sheet


def bauxite(rng):
    """Earthy reddish-brown bauxite with round pisoliths and pale gibbsite/kaolin spots (A/C/D); B holds four 3x3
    pisolith ball faces at x 26-28, y 2-13 for the earthy form's pisolith elements."""
    earth = [(0, (116, 52, 34)), (.4, (150, 74, 46)), (.75, (174, 98, 64)), (1, (198, 130, 92))]
    sheet = Image.new("RGB", (32, 32))
    for q, (ox, oy) in enumerate([(0, 0), (16, 0), (0, 16), (16, 16)]):
        t = paint(rng, earth); px = t.load()
        for _ in range(rng.randint(5, 7) if q != 1 else 4):   # pisoliths: darker disc, lit upper-left rim
            r = rng.choice((1.6, 2.0, 2.4)); cx, cy = rng.uniform(1, 15), rng.uniform(1, 15)
            if q == 1 and cx > 8: cx -= 7
            for y in range(16):
                for x in range(16):
                    dd = math.hypot(x + .5 - cx, y + .5 - cy)
                    if dd <= r:
                        edge = dd > r - 1
                        lit = (x + .5 - cx) + (y + .5 - cy) < -.5
                        px[x, y] = (214, 150, 110) if edge and lit else (176, 104, 70) if edge else (104, 42, 28)
            if rng.random() < .5: px[int(cx), int(cy)] = (206, 170, 140)
        for _ in range(rng.randint(6, 9)):                    # pale spots
            x, y = rng.randrange(15), rng.randrange(15)
            if q == 1 and x > 8: x -= 8
            px[x, y] = (230, 212, 190)
            if rng.random() < .6: px[x + 1, y] = (212, 186, 160)
            if rng.random() < .4: px[x, y + 1] = (206, 178, 152)
        sheet.paste(t, (ox, oy))
    px = sheet.load()
    sw = [[(176, 102, 68), (150, 70, 46), (132, 58, 38)],
          [(150, 70, 46), (124, 54, 36), (106, 44, 30)],
          [(132, 58, 38), (106, 44, 30), (88, 36, 24)]]
    for i in range(4):                                        # pisolith ball faces
        for dy in range(3):
            for dx in range(3):
                px[26 + dx, 2 + 3 * i + dy] = shade(sw[dy][dx], 1 + .06 * (i - 1.5))
    return sheet


PAINT = dict(tetrahedrite=tetrahedrite, sperrylite=sperrylite, bogiron=bogiron, bauxite=bauxite)


# ------------------------------------------------------------------ nuggets (16x16, vanilla's item/nugget model)
def nativeplatinum(rng):
    """Placer platinum grains: steel-grey, a little darker and bluer than silver, worn smooth with soft highlights and
    a few black specks of the chromite that comes with them."""
    steel = [(0, (70, 72, 78)), (.35, (118, 121, 128)), (.7, (166, 169, 175)), (1, (212, 214, 218))]
    t = paint(rng, steel, ((150, 156, 170), .35)); px = t.load()
    for _ in range(7):                                   # worn, rounded highlights
        x, y = rng.randrange(15), rng.randrange(15)
        px[x, y] = (236, 237, 240); px[x + 1, y] = shade(px[x + 1, y], 1.15)
    for _ in range(4):                                   # chromite specks
        px[rng.randrange(16), rng.randrange(16)] = (34, 32, 34)
    return t.convert("RGBA")


NUGGETS = dict(nativeplatinum=nativeplatinum)
# Earth ore types drawn with vanilla's own art: vanilla 1.22.7 ships wolframite's ore overlays, chunk models and nugget
# without any block or deposit that uses them (Earth's item type points at game:item/ore/{grade}/wolframite).
VANILLA_ART = {"wolframite"}


def make(name, cfg):
    rng = random.Random(zlib.crc32(name.encode()))
    if name in PAINT:
        return PAINT[name](rng).convert("RGBA")
    base, dark, light = cfg["m"]; pal = cfg["p"]; fn = FEAT[cfg["f"]]
    sheet = Image.new("RGBA", (32, 32))
    for q, (ox, oy) in enumerate([(0, 0), (16, 0), (0, 16), (16, 16)]):
        if q in (0, 2):
            tile = matrix_tile(rng, base, dark, light); d = ImageDraw.Draw(tile)
            fn(d, rng, pal, cfg["n"] if cfg["f"] == "grains" else rng.randint(*cfg["fill"]), *cfg["sz"])
        else:
            tile = Image.new("RGB", (16, 16), shade(pal[0], .85 if q == 1 else .75)); px = tile.load()
            for y in range(16):
                for x in range(16):
                    if rng.random() < .25: px[x, y] = shade(pal[0], rng.uniform(.8, 1.1))
            fn(ImageDraw.Draw(tile), rng, pal, cfg["n"] + 6, *cfg["sz"])
        sheet.paste(tile.convert("RGBA"), (ox, oy))
    return sheet


def install():
    """Copy the textures of the ore types the live item types declare into assets/earth/textures/item/ore."""
    names = set()
    for n in ("ore-graded", "ore-ungraded"):
        it = json.loads((LIVE / "itemtypes" / "resource" / f"{n}.json").read_text(encoding="utf8"))
        names.update(next(v["states"] for v in it["variantgroups"] if v["code"] == "type"))
    names -= VANILLA_ART
    for n in sorted(names):
        shutil.copyfile(OUT / f"{n}.png", LIVE / "textures" / "item" / "ore" / f"{n}.png")
    print("live textures:", ", ".join(sorted(names)))


if __name__ == "__main__":
    OUT.mkdir(exist_ok=True)
    for n, c in ORES.items(): make(n, c).save(OUT / f"{n}.png")
    for n, f in NUGGETS.items(): f(random.Random(zlib.crc32(("nugget-" + n).encode()))).save(OUT / f"nugget-{n}.png")
    print("textures:", ", ".join(ORES), "| nuggets:", ", ".join(NUGGETS))
    if "--live" in sys.argv:
        install()
        for n in NUGGETS:
            shutil.copyfile(OUT / f"nugget-{n}.png", LIVE / "textures" / "item" / "nugget" / f"{n}.png")
