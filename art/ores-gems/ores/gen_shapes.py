"""Generates Earth ore-chunk shapes: 9 forms x 4 grades, each a cluster of boxes.

Textures are 32x32 sheets (see gen_textures.py): body regions A/C use matrix+ore, crystal regions B/D pure mineral.
Shape textureWidth/Height = 32; faces use ~1.6-2 texels per model unit.
Every shape is normalised against vanilla's chunks (item/ore/medium/*: 4.5 x 3.5 x 4.5 units resting on y = 0): it is
scaled until its hotbar silhouette (seen through the guiTransform) matches vanilla's area, a little less for poor and
more for bountiful, and moved on x/z so that silhouette sits in the middle of the slot.
Element rotations follow the game's order (vertex rotated about Z, then Y, then X; see ShapeTesselator).

Forms: blocky (pyrite), rounded (chalcopyrite), crystal (cerussite, vanadinite), plate (wulfenite, hemimorphite),
nodule (azurite, smithsonite), massive (tetrahedrite: steel-grey lump with tetrahedral points),
studded (sperrylite: dark matrix with tiny tin-white cubes), porous (bog iron: pitted lump with root casts),
earthy (bauxite: earthy lump with pisoliths).

  python -I gen_shapes.py          writes ./shapes
  python -I gen_shapes.py --live   also copies the forms the item types use to assets/earth/shapes/item/ore
"""
import json, math, random, sys, shutil
from pathlib import Path
HERE = Path(__file__).parent
OUT = HERE / "shapes"
LIVE = HERE.parents[2] / "assets" / "earth"
BODY = [(0, 0, 16, 16), (0, 16, 16, 32)]
CRYS = [(16, 0, 32, 16), (16, 16, 32, 32)]
GRADES = ["poor", "medium", "rich", "bountiful"]
CX = CZ = 8.0
AREA = [16.5, 18.5, 20.0, 21.5]   # hotbar silhouette per grade in square units; vanilla's chunks show 19.8
MAX_W, MAX_H = 6.2, 4.4           # caps on footprint and height (vanilla chunk: 4.5 x 3.5 x 4.5)
GUI_ROT, GUI_ORIGIN = (150, -38), (8, 1.6, 8)   # ore-graded guiTransform rotation and origin (x16)


def faces(rng, w, h, d, regs):
    dims = dict(north=(w, h), south=(w, h), east=(d, h), west=(d, h), up=(w, d), down=(w, d))
    out = {}
    for f, (a, b) in dims.items():
        reg = rng.choice(regs.get(f, regs['default']) if isinstance(regs, dict) else regs); uw = max(3, min(15, round(a * 1.6))); vh = max(3, min(15, round(b * 1.6)))
        u = rng.uniform(reg[0], reg[2] - uw); v = rng.uniform(reg[1], reg[3] - vh)
        out[f] = {"texture": "#ore", "uv": [round(u * 2) / 2, round(v * 2) / 2, round(u * 2) / 2 + uw, round(v * 2) / 2 + vh]}
    return out


def box(rng, name, x, y, z, w, h, d, regs, rx=0, ry=0, rz=0, origin=None, uv=None, fixed=None):
    """uv: optional explicit {face: [u0, v0, u1, v1]} (with 'default'), used instead of random windows. Explicit faces
    keep their window ('fixed', default all of them); the others are resized to the texel density after scaling."""
    if uv is not None:
        fc = {f: {"texture": "#ore", "uv": list(uv.get(f, uv["default"]))} for f in ("north", "east", "south", "west", "up", "down")}
    else:
        fc = faces(rng, w, h, d, regs)
    e = {"name": name, "from": [round(x, 3), round(y, 3), round(z, 3)], "to": [round(x + w, 3), round(y + h, 3), round(z + d, 3)],
         "rotationOrigin": [round(v, 3) for v in (origin or (x + w / 2, y, z + d / 2))], "faces": fc}
    if uv is not None:
        e["_fixed"] = list(fixed if fixed is not None else fc)
    if rx: e["rotationX"] = rx
    if ry: e["rotationY"] = ry
    if rz: e["rotationZ"] = rz
    return e


def window(rng, reg, n):
    """n x n texel window inside a texture region, on whole texels."""
    u = rng.randint(reg[0], reg[2] - n); v = rng.randint(reg[1], reg[3] - n)
    return [u, v, u + n, v + n]


def _mat(axis, deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return {"x": [[1, 0, 0], [0, c, -s], [0, s, c]], "y": [[c, 0, s], [0, 1, 0], [-s, 0, c]], "z": [[c, -s, 0], [s, c, 0], [0, 0, 1]]}[axis]


def _mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def orient(bearing, tilt, spin):
    """Element angles (rotationX, Y, Z) for: spin about the element's own y axis, then tip it 'tilt' degrees towards
    the compass 'bearing'. The game applies Rx * Ry * Rz, so the wanted matrix is decomposed into those Euler angles."""
    m = _mul(_mul(_mat("y", bearing), _mat("x", tilt)), _mat("y", spin - bearing))
    b = math.asin(max(-1.0, min(1.0, m[0][2])))
    a = math.atan2(-m[1][2], m[2][2]); c = math.atan2(-m[0][1], m[0][0])
    return tuple(round(math.degrees(v), 2) for v in (a, b, c))


# ------------------------------------------------------------------ existing forms
def blocky(rng, g):
    n = [4, 6, 8, 10][g]; r = [1.2, 1.6, 2.0, 2.3][g]; els = []
    for i in range(n):
        w, d = rng.uniform(1.6, 3.2), rng.uniform(1.6, 3.2); h = rng.uniform(1.0, 2.2)
        x = CX + rng.uniform(-r, r) - w / 2; z = CZ + rng.uniform(-r, r) - d / 2
        y = 0 if i < n // 2 else rng.uniform(0.8, 1.8)
        els.append(box(rng, f"b{i}", x, y, z, w, h, d, {"default": BODY, "up": CRYS, "east": BODY + CRYS}, ry=rng.choice([0, 0, 22.5, 45, -22.5]),
                       rx=rng.choice([0, 0, 0, 22.5, -22.5]) if i else 0))
    return els


def rounded(rng, g):
    """Rounded lumps: each a box plus its 45-degree twin (octagonal), lightly tilted."""
    n = [2, 3, 4, 5][g]; els = []
    for i in range(n):
        s = rng.uniform(2.2, 3.4) * (0.75 if i > 2 else 1); h = s * rng.uniform(.75, 1.0)
        x = CX + rng.uniform(-1.3, 1.3) - s / 2; z = CZ + rng.uniform(-1.3, 1.3) - s / 2
        y = 0 if i < 3 else rng.uniform(1.0, 1.6)
        tilt = rng.choice([0, 12, -12])
        els.append(box(rng, f"l{i}a", x, y, z, s, h, s, BODY, rx=tilt))
        els.append(box(rng, f"l{i}b", x, y + .02, z, s, h * .96, s, BODY, rx=tilt, ry=45))
    return els


def crystal(rng, g):
    """Prism spray on a matrix base; each prism is a box plus its 45-degree twin (octagonal) and a narrower cap,
    all three sharing one axis (orient() turns 'tilt towards a bearing, spin about the axis' into game angles)."""
    els = [box(rng, "base0", CX - 2.2, 0, CZ - 1.8, 3.6, 1.0, 3.2, BODY, ry=rng.choice([0, 22.5])),
           box(rng, "base1", CX - 0.4, 0, CZ - 0.6, 2.8, 1.3, 2.6, BODY, ry=45)]
    n = [3, 4, 5, 6][g]
    for i in range(n):
        w = rng.uniform(.8, 1.35) * (1 + g * .06); h = rng.uniform(1.8, 3.4) + g * .15
        x = CX + rng.uniform(-2.2, 1.8); z = CZ + rng.uniform(-1.8, 1.6); y = rng.uniform(.5, 1.0)
        bearing = rng.uniform(0, 360); tilt = rng.choice([0, 15, 25, 35, 45]); spin = rng.uniform(0, 45)
        o = (x + w / 2, y, z + w / 2)
        for k, (sp, part) in enumerate(((spin, "a"), (spin + 45, "b"))):
            rx, ry, rz = orient(bearing, tilt, sp)
            els.append(box(rng, f"c{i}{part}", x, y, z, w, h, w, CRYS, rx, ry, rz, o))
        t = w * .62                                                                               # tapered termination
        rx, ry, rz = orient(bearing, tilt, spin + 22.5)
        els.append(box(rng, f"c{i}t", x + (w - t) / 2, y + h, z + (w - t) / 2, t, w * .7, t, CRYS, rx, ry, rz, o))
    return els


def plate(rng, g):
    els = [box(rng, "base", CX - 2.4, 0, CZ - 2.0, 4.6, 1.0, 4.0, BODY, ry=rng.choice([0, 22.5, 45]))]
    n = [3, 5, 7, 9][g]
    for i in range(n):
        w, d = rng.uniform(2.0, 3.4), rng.uniform(1.8, 3.0); t = rng.uniform(.35, .6)
        x = CX + rng.uniform(-2.0, 1.4) - w / 2; z = CZ + rng.uniform(-1.6, 1.6) - d / 2; y = rng.uniform(.6, 1.6)
        tilt = rng.choice([22.5, -22.5, 45, -45, 67.5]); axis = rng.choice(["rx", "rz"])
        kw = {axis: tilt}
        els.append(box(rng, f"p{i}", x, y, z, w, t, d, CRYS, ry=rng.choice([0, 22.5, 45, -22.5]), origin=(x + w / 2, y, z + d / 2), **kw))
    return els


def nodule(rng, g):
    """Botryoidal knobs: each a box plus a twin turned 45 degrees and tipped, which rounds the corners."""
    n = [4, 5, 6, 7][g]; els = []
    for i in range(n):
        s = rng.uniform(1.5, 2.6); x = CX + rng.uniform(-1.9, 1.9) - s / 2; z = CZ + rng.uniform(-1.9, 1.9) - s / 2
        y = max(0, rng.gauss(.6, .7) * (0.7 + g * .2)) if i >= 3 else 0
        reg = BODY if i % 2 else CRYS
        els.append(box(rng, f"n{i}a", x, y, z, s, s * .9, s, reg))
        els.append(box(rng, f"n{i}b", x, y, z, s, s * .9, s, reg, ry=45, rx=22.5))
    return els


# ------------------------------------------------------------------ new forms
def lump(rng, n, r, regs, smin=2.0, smax=3.1, hmin=1.2, hmax=2.1, rot=(0, 0, 8, -8, 15, -15), name="m", tip=True):
    """Compact vanilla-like cluster of n boxes within radius r; the first half rests on y=0. tip=False keeps every box
    upright (turned about y only), so other parts can be placed on its faces."""
    els = []
    for i in range(n):
        w, d = rng.uniform(smin, smax), rng.uniform(smin, smax); h = rng.uniform(hmin, hmax)
        x = CX + rng.uniform(-r, r) - w / 2; z = CZ + rng.uniform(-r, r) - d / 2
        y = 0 if i < max(2, (n + 1) // 2) else rng.uniform(0.7, 1.3)
        els.append(box(rng, f"{name}{i}", x, y, z, w, h, d, regs, ry=rng.choice(rot), rx=rng.choice((0, 0, 6, -6)) if i and tip else 0))
    return els


def top_of(els):
    return max(e["to"][1] for e in els)


def massive(rng, g):
    """Tetrahedrite: massive steel-grey lump with tetrahedral points (a cube stood on its corner shows three
    triangular faces meeting at an apex), sunk only a little so the points stand clear."""
    els = lump(rng, [3, 4, 4, 5][g], [0.6, 0.8, 1.0, 1.2][g], {"default": CRYS + BODY, "up": CRYS}, hmin=1.1, hmax=1.8)
    ytop = top_of(els)
    for i in range([1, 2, 3, 3][g]):
        s = rng.uniform(1.45, 1.75) * (1 if i == 0 else .78)
        a = rng.uniform(0, 2 * math.pi); r = 0 if i == 0 else rng.uniform(.8, 1.2)
        cx = CX + r * math.cos(a); cz = CZ + r * math.sin(a); cy = ytop - s * (.2 if i == 0 else .45)
        o = (cx, cy, cz)
        els.append(box(rng, f"t{i}", cx - s / 2, cy - s / 2, cz - s / 2, s, s, s, CRYS, rx=-35.26 + rng.uniform(-6, 6),
                       ry=rng.uniform(-15, 15), rz=45, origin=o))
    return els


def _turn(px, pz, ox, oz, ry):
    """Position (px, pz) turned ry degrees about (ox, oz) the way the game turns an element (Ry)."""
    c, s_ = math.cos(math.radians(ry)), math.sin(math.radians(ry)); dx, dz = px - ox, pz - oz
    return ox + dx * c + dz * s_, oz - dx * s_ + dz * c


def studded(rng, g):
    """Sperrylite: dark rock matrix with tiny tin-white cubes half sunk into its upper and outer faces."""
    els = lump(rng, [3, 4, 4, 5][g], [0.7, 0.9, 1.1, 1.3][g], BODY, rot=(0, 0, 10, -10, 20), tip=False)
    hosts = sorted(els, key=lambda e: -e["to"][1])
    n = [4, 6, 8, 9][g]
    for i in range(n):
        hst = hosts[i % min(len(hosts), 3)]
        (x0, y0, z0), (x1, y1, z1) = hst["from"], hst["to"]; hry = hst.get("rotationY", 0)
        ox, oz = hst["rotationOrigin"][0], hst["rotationOrigin"][2]
        s = rng.uniform(.4, .62)
        if i % 3 == 2:   # on an outer side face, upper half
            side = max(((1, 0), (-1, 0), (0, 1), (0, -1)),
                       key=lambda v: v[0] * ((x0 + x1) / 2 - CX) + v[1] * ((z0 + z1) / 2 - CZ) + rng.random())
            px = (x0 + x1) / 2 + side[0] * ((x1 - x0) / 2 + s * .1) + (0 if side[0] else rng.uniform(-.3, .3) * (x1 - x0))
            pz = (z0 + z1) / 2 + side[1] * ((z1 - z0) / 2 + s * .1) + (0 if side[1] else rng.uniform(-.3, .3) * (z1 - z0))
            cy = y0 + (y1 - y0) * rng.uniform(.55, .85)
        else:            # on the top face
            rad = max(0.0, min(x1 - x0, z1 - z0) / 2 - s * .7)
            a = rng.uniform(0, 2 * math.pi); r = rad * math.sqrt(rng.random())
            px = (x0 + x1) / 2 + r * math.cos(a); pz = (z0 + z1) / 2 + r * math.sin(a); cy = y1 + s * .1
        cx, cz = _turn(px, pz, ox, oz, hry)
        o = (cx, cy, cz)
        # tin-white texel windows; a 2-texel face keeps the cube crisp
        uv = {"default": window(rng, (16, 0, 32, 16), 2), "up": window(rng, (16, 0, 32, 8), 2)}
        els.append(box(rng, f"c{i}", cx - s / 2, cy - s / 2, cz - s / 2, s, s, s, None, ry=hry + rng.choice((0, 10, -10, 25)),
                       rx=rng.choice((0, 0, 8, -8)), origin=o, uv=uv))
    return els


def porous(rng, g):
    """Bog iron: irregular pitted lumps with one or two tubular root casts (hollow channel on the end faces)."""
    els = []
    n = [2, 3, 4, 5][g]
    for i in range(n):
        s = rng.uniform(2.0, 3.0) * (0.8 if i > 2 else 1); h = s * rng.uniform(.6, .85)
        x = CX + rng.uniform(-1.3, 1.3) - s / 2; z = CZ + rng.uniform(-1.3, 1.3) - s / 2
        y = 0 if i < 3 else rng.uniform(0.8, 1.2)
        ry = rng.uniform(-30, 30); tilt = rng.choice((0, 10, -10))
        els.append(box(rng, f"l{i}a", x, y, z, s, h, s * rng.uniform(.8, 1), BODY, ry=ry, rx=tilt))
        if i < 2:   # irregular outline: a lower, turned twin
            els.append(box(rng, f"l{i}b", x + .2, y, z + .2, s - .4, h * .8, s - .4, BODY, ry=ry + 45, rz=rng.choice((0, 8, -8))))
    for i in range([1, 1, 2, 2][g]):
        t = rng.uniform(.8, .95); L = rng.uniform(2.4, 3.0)
        cx = CX + rng.uniform(-.6, .6); cz = CZ + rng.uniform(-.6, .6); cy = top_of(els) - t * .35
        o = (cx, cy, cz)
        uv = {"default": [16, 1 + 4 * i, 16 + round(L * 1.6), 1 + 4 * i + 2],        # streaked tube wall (along the tube)
              "east": [26, 26, 30, 30], "west": [26, 26, 30, 30]}                   # channel opening
        els.append(box(rng, f"r{i}", cx - L / 2, cy - t / 2, cz - t / 2, L, t, t, None, ry=rng.uniform(0, 180),
                       rz=rng.uniform(-14, 14), origin=o, uv=uv, fixed=("east", "west")))
    return els


def earthy(rng, g):
    """Bauxite: earthy rounded lump (box + 45-degree twin) carrying a few pea-sized pisoliths."""
    els = []
    n = [2, 2, 3, 4][g]
    for i in range(n):
        s = rng.uniform(2.3, 3.2) * (0.8 if i > 1 else 1); h = s * rng.uniform(.65, .8)
        x = CX + rng.uniform(-1.2, 1.2) - s / 2; z = CZ + rng.uniform(-1.2, 1.2) - s / 2
        y = 0 if i < 2 else rng.uniform(0.6, 1.0)
        tilt = rng.choice((0, 8, -8))
        els.append(box(rng, f"l{i}a", x, y, z, s, h, s, BODY, rx=tilt))
        els.append(box(rng, f"l{i}b", x + .15, y + .02, z + .15, s - .3, h, s - .3, BODY, rx=tilt, ry=45))
    hosts = sorted(els, key=lambda e: -e["to"][1])
    for i in range([2, 3, 4, 5][g]):
        hst = hosts[i % min(len(hosts), 4)]
        (x0, y0, z0), (x1, y1, z1) = hst["from"], hst["to"]
        s = rng.uniform(.7, .9)
        rad = max(0.0, min(x1 - x0, z1 - z0) / 2 - s * .8)
        a = rng.uniform(0, 2 * math.pi); r = rad * math.sqrt(rng.random())
        cx = (x0 + x1) / 2 + r * math.cos(a); cz = (z0 + z1) / 2 + r * math.sin(a); cy = y1 - s * .15
        o = (cx, cy, cz)
        uv = {"default": [26, 2 + 3 * (i % 4), 29, 5 + 3 * (i % 4)]}   # one pisolith disc per face (D column, see textures)
        els.append(box(rng, f"p{i}a", cx - s / 2, cy - s / 2, cz - s / 2, s, s, s, None, origin=o, uv=uv))
        els.append(box(rng, f"p{i}b", cx - s / 2, cy - s / 2 + .01, cz - s / 2, s, s * .98, s, None, ry=45, origin=o, uv=uv))
    return els


FORMS = dict(blocky=blocky, rounded=rounded, crystal=crystal, plate=plate, nodule=nodule,
             massive=massive, studded=studded, porous=porous, earthy=earthy)


# ------------------------------------------------------------------ normalisation
def _rot(v, rx, ry, rz):
    """Game order: vertex rotated about Z first, then Y, then X (stackMatrix X * Y * Z)."""
    x, y, z = v
    c, s = math.cos(math.radians(rz)), math.sin(math.radians(rz)); x, y = x * c - y * s, x * s + y * c
    c, s = math.cos(math.radians(ry)), math.sin(math.radians(ry)); x, z = x * c + z * s, -x * s + z * c
    c, s = math.cos(math.radians(rx)), math.sin(math.radians(rx)); y, z = y * c - z * s, y * s + z * c
    return x, y, z


def corners(e):
    f, t, o = e["from"], e["to"], e["rotationOrigin"]
    r = (e.get("rotationX", 0), e.get("rotationY", 0), e.get("rotationZ", 0))
    for i in range(8):
        p = [t[k] if i >> k & 1 else f[k] for k in range(3)]
        q = _rot([p[k] - o[k] for k in range(3)], *r)
        yield [q[k] + o[k] for k in range(3)]



def gui_silhouette(boxes, rot, origin, px=16):
    """Hotbar silhouette of element corner sets as the guiTransform rotation shows them (vertex turned about Y, then X;
    screen y down): returns (area in square units, centre offset from the projected transform origin)."""
    from PIL import Image, ImageDraw
    def proj(v):
        x, y, z = (v[k] - origin[k] for k in range(3))
        c, s = math.cos(math.radians(rot[1])), math.sin(math.radians(rot[1])); x, z = x * c + z * s, -x * s + z * c
        c, s = math.cos(math.radians(rot[0])), math.sin(math.radians(rot[0])); y, z = y * c - z * s, y * s + z * c
        return x, y
    def hull(p):
        p = sorted(set(p))
        def half(q):
            h = []
            for a in q:
                while len(h) > 1 and (h[-1][0] - h[-2][0]) * (a[1] - h[-2][1]) - (h[-1][1] - h[-2][1]) * (a[0] - h[-2][0]) <= 0:
                    h.pop()
                h.append(a)
            return h
        return half(p)[:-1] + half(p[::-1])[:-1]
    polys = [hull([proj(v) for v in b]) for b in boxes]
    xs = [p[0] for q in polys for p in q]; ys = [p[1] for q in polys for p in q]
    x0, y0 = min(xs) - 1, min(ys) - 1
    im = Image.new("L", (int((max(xs) - x0 + 2) * px), int((max(ys) - y0 + 2) * px))); d = ImageDraw.Draw(im)
    for q in polys:
        if len(q) > 2:
            d.polygon([((a - x0) * px, (b - y0) * px) for a, b in q], fill=255)
    bb = im.getbbox(); area = im.histogram()[255] / px / px
    return area, ((bb[0] + bb[2]) / 2 / px + x0, (bb[1] + bb[3]) / 2 / px + y0)


def gui_shift(off, rot):
    """World x/z shift that moves the hotbar silhouette centre by -off (keeps y, so the item still rests on y = 0)."""
    def proj(x, z):
        c, s = math.cos(math.radians(rot[1])), math.sin(math.radians(rot[1])); x, z = x * c + z * s, -x * s + z * c
        c, s = math.cos(math.radians(rot[0])), math.sin(math.radians(rot[0]))
        return x, -z * s
    (a, c), (b, d) = proj(1, 0), proj(0, 1)
    det = a * d - b * c
    return ((-off[0]) * d - b * (-off[1])) / det, (a * (-off[1]) - c * (-off[0])) / det


def normalise(els, area):
    """Rest on y = 0, scale so the hotbar silhouette has 'area' (within the caps), centre it on the slot like vanilla."""
    def apply(f):
        for e in els:
            for key in ("from", "to", "rotationOrigin"):
                e[key] = [round(v, 4) for v in f(e[key])]
    pts = [p for e in els for p in corners(e)]
    lo = [min(p[k] for p in pts) for k in range(3)]; hi = [max(p[k] for p in pts) for k in range(3)]
    c = ((lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2)
    apply(lambda v: [8 + v[0] - c[0], v[1] - c[1], 8 + v[2] - c[2]])
    a, _ = gui_silhouette([list(corners(e)) for e in els], GUI_ROT, GUI_ORIGIN)
    k = min(math.sqrt(area / a), MAX_W / max(hi[0] - lo[0], hi[2] - lo[2]), MAX_H / (hi[1] - lo[1]))
    apply(lambda v: [8 + (v[0] - 8) * k, v[1] * k, 8 + (v[2] - 8) * k])
    _, off = gui_silhouette([list(corners(e)) for e in els], GUI_ROT, GUI_ORIGIN)
    dx, dz = gui_shift(off, GUI_ROT)
    apply(lambda v: [round(v[0] + dx, 3), round(v[1], 3), round(v[2] + dz, 3)])
    texel_density(els)
    return els


TEXELS = 2.0   # texels per model unit, as vanilla's chunks (16-unit UVs on 32 px textures, about 1 UV unit per unit)


def texel_density(els):
    """Resize every non-fixed face window to TEXELS per unit of the final (scaled) element, inside its 16 px quadrant."""
    for e in els:
        w, h, d = (e["to"][k] - e["from"][k] for k in range(3))
        dims = dict(north=(w, h), south=(w, h), east=(d, h), west=(d, h), up=(w, d), down=(w, d))
        fixed = set(e.pop("_fixed", ()))
        for f, fd in e["faces"].items():
            if f in fixed:
                continue
            u0, v0 = fd["uv"][0], fd["uv"][1]
            qu, qv = (min(u0, 31.9) // 16) * 16, (min(v0, 31.9) // 16) * 16
            uw = max(2, min(16, round(dims[f][0] * TEXELS))); vh = max(2, min(16, round(dims[f][1] * TEXELS)))
            u0 = min(max(u0, qu), qu + 16 - uw); v0 = min(max(v0, qv), qv + 16 - vh)
            fd["uv"] = [u0, v0, u0 + uw, v0 + vh]


def build(form, grade):
    rng = random.Random(sum(map(ord, form)) * 131 + grade * 17)
    return {"editor": {"allAngles": True, "singleTexture": True}, "textureWidth": 32, "textureHeight": 32,
            "textures": {"ore": "earth:item/ore/pyrite"}, "elements": normalise(FORMS[form](rng, grade), AREA[grade])}


def install():
    """Copy the forms the live item types reference (and refresh any other form already there) into
    assets/earth/shapes/item/ore."""
    used = set()
    for n in ("ore-graded", "ore-ungraded"):
        it = json.loads((LIVE / "itemtypes" / "resource" / f"{n}.json").read_text(encoding="utf8"))
        bases = [v["base"] for v in it.get("shapeByType", {}).values()] + ([it["shape"]["base"]] if "shape" in it else [])
        for b in bases:
            for g in GRADES:
                used.add(b.split(":")[-1].replace("{grade}", g).split("/")[-1])
    dst = LIVE / "shapes" / "item" / "ore"
    used |= {f.stem for f in dst.glob("*.json") if (OUT / f.name).exists()}   # refresh older copies too
    for name in sorted(used):
        shutil.copyfile(OUT / f"{name}.json", dst / f"{name}.json")
    print("live shapes:", ", ".join(sorted(used)))


if __name__ == "__main__":
    OUT.mkdir(exist_ok=True)
    for f in FORMS:
        for g, name in enumerate(GRADES):
            (OUT / f"{f}-{name}.json").write_text(json.dumps(build(f, g), indent=1))
    print("shapes:", len(FORMS) * 4)
    if "--live" in sys.argv:
        install()
