#!/usr/bin/env python
"""Generates the rough-gem shape JSONs (shapes/*.json) and 32x32 textures (textures/*.png).

  python -I build_gems.py          writes ./shapes and ./textures
  python -I build_gems.py --live   also copies them to assets/earth/shapes|textures/item/gem/rough (ids as in gem-rough.json)
  python -I ../render_items.py     preview beside vanilla (hotbar slot and dropped item)

Shapes follow vanilla's rough-gem layout: one invisible 'origin' element holding the crystals, textureWidth/Height 16
with a 32px texture, and the same item transforms as vanilla's gem-rough. Each crystal is a faceless group element
that carries the crystal's tilt; its boxes inside only turn about the crystal's own axis. (The game rotates a vertex
about Z, then Y, then X, so a tilt and an axial turn on the same box send the boxes of one prism in different
directions; the group keeps them on one axis.) Every gem rests on y = 0 and is scaled until its hotbar silhouette
(through the guiTransform) is a little larger than vanilla's rough gems, then moved on x/z so it sits in the middle of the
slot. Geometry and textures are original.

Habits: beryl hexagonal prism with flat end; tourmaline slender striated prism with a pink end; topaz stout prism with
a wedge tip; garnet rhombic dodecahedra; spinel octahedra; ruby flat hexagonal tabular plates, stacked and tilted;
sapphire steep hexagonal barrel/bipyramid tapering at both ends, lying across the slot.
"""
import json, math, os, random, shutil, sys
import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
LIVE = os.path.abspath(os.path.join(HERE, "..", "..", "..", "assets", "earth"))
os.makedirs(os.path.join(HERE, "shapes"), exist_ok=True)
os.makedirs(os.path.join(HERE, "textures"), exist_ok=True)
FACES = ("north", "east", "south", "west", "up", "down")


# ---------------------------------------------------------------- shape helpers
def orient(bearing, tilt, spin=0.0):
    """Game angles (rotationX, Y, Z) for: turn 'spin' about the own y axis, then tip 'tilt' degrees towards the
    compass 'bearing' (0 = +z, 90 = +x). The game applies Rx * Ry * Rz, so the matrix is decomposed into those."""
    def m(axis, deg):
        a = math.radians(deg); c, s = math.cos(a), math.sin(a)
        return np.array({"x": [[1, 0, 0], [0, c, -s], [0, s, c]], "y": [[c, 0, s], [0, 1, 0], [-s, 0, c]],
                         "z": [[c, -s, 0], [s, c, 0], [0, 0, 1]]}[axis])
    M = m("y", bearing) @ m("x", tilt) @ m("y", spin - bearing)
    b = math.asin(max(-1.0, min(1.0, M[0, 2]))); a = math.atan2(-M[1, 2], M[2, 2]); c = math.atan2(-M[0, 1], M[0, 0])
    return tuple(round(math.degrees(v), 2) for v in (a, b, c))


def rot_keys(e, rx=0.0, ry=0.0, rz=0.0):
    for k, v in (("rotationX", rx), ("rotationY", ry), ("rotationZ", rz)):
        if abs(v) > 1e-6:
            e[k] = round(v, 2)
    return e


class UV:
    """Texture zones in uv units (16 = whole 32px texture). side: (u0, v0, u1, v1) strip whose v runs along the crystal
    axis (v0 = tip); top/bottom: windows for the end faces; k = uv units per model unit."""
    def __init__(self, side=(0, 0, 8, 16), top=(8, 0, 16, 8), bottom=(8, 8, 16, 16), k=2.5):
        self.side, self.top, self.bottom, self.k = side, top, bottom, k

    def faces(self, w, d, f0, f1, top=True, bottom=True):
        """f0..f1: the box's span along its crystal (0 = base, 1 = tip)."""
        s = self.side; v_tip = s[1] + (s[3] - s[1]) * (1 - f1); v_base = s[1] + (s[3] - s[1]) * (1 - f0)
        def strip(n): return [s[0], round(v_tip, 3), round(s[0] + min(s[2] - s[0], n * self.k), 3), round(v_base, 3)]
        def win(z, a, b):
            cx, cy = (z[0] + z[2]) / 2, (z[1] + z[3]) / 2; hw = min((z[2] - z[0]) / 2, a * self.k / 2); hh = min((z[3] - z[1]) / 2, b * self.k / 2)
            return [round(cx - hw, 3), round(cy - hh, 3), round(cx + hw, 3), round(cy + hh, 3)]
        f = {"north": strip(w), "south": strip(w), "east": strip(d), "west": strip(d),
             "up": win(self.top, w, d), "down": win(self.bottom, w, d)}
        out = {k: {"texture": "#gem", "uv": v} for k, v in f.items()}
        if not top: out["up"]["enabled"] = False
        if not bottom: out["down"]["enabled"] = False
        return out


def box(name, w, d, y0, y1, uv, L, ry=0.0, cx=0.0, cz=0.0, top=True, bottom=True):
    """Box of footprint w x d centred on (cx, cz) of its crystal, spanning y0..y1 along the crystal axis (crystal length
    L), turned ry about that axis."""
    e = {"name": name, "from": [round(cx - w / 2, 3), round(y0, 3), round(cz - d / 2, 3)],
         "to": [round(cx + w / 2, 3), round(y1, 3), round(cz + d / 2, 3)],
         "rotationOrigin": [round(cx, 3), round(y0, 3), round(cz, 3)],
         "faces": uv.faces(w, d, max(0, y0 / L), min(1, y1 / L), top, bottom)}
    return rot_keys(e, ry=ry)


def hexlayer(name, R, y0, y1, uv, L, spin=0.0, **kw):
    """Hexagonal prism of circumradius R from three rectangles R x sqrt(3)R turned 0/60/120 degrees (their union is the
    hexagon); tiny height offsets stop coplanar end faces fighting."""
    return [box(f"{name}{i}", R, 1.732 * R, y0 + .004 * i, y1 - .004 * i, uv, L, ry=spin + a, **kw) for i, a in enumerate((0, 60, 120))]


def crystal(name, pos, bearing, tilt, children):
    """Faceless group at pos carrying the crystal's tilt; children use the crystal's local frame (y = axis)."""
    g = {"name": name, "from": [round(v, 3) for v in pos], "to": [round(v, 3) for v in pos],
         "rotationOrigin": [round(v, 3) for v in pos],
         "faces": {k: {"texture": "#gem", "uv": [0, 0, 1, 1], "enabled": False} for k in FACES}, "children": children}
    return rot_keys(g, *orient(bearing, tilt))


# ------------------------------------------------ normalise (evaluate the hierarchy like ShapeTesselator)
def _M(e):
    def T(v):
        m = np.eye(4); m[:3, 3] = v; return m
    def R(axis, deg):
        a = math.radians(deg); c, s = math.cos(a), math.sin(a); m = np.eye(4)
        i, j = {"x": (1, 2), "y": (2, 0), "z": (0, 1)}[axis]; m[i, i] = c; m[i, j] = -s; m[j, i] = s; m[j, j] = c
        return m
    o = np.array(e.get("rotationOrigin", [0, 0, 0]), float); m = T(o)
    for ax in "xyz":
        if e.get("rotation" + ax.upper()):
            m = m @ R(ax, e["rotation" + ax.upper()])
    return m @ T(np.array(e["from"], float) - o)


def _points(els, M=np.eye(4)):
    for e in els:
        m = M @ _M(e); size = np.array(e["to"], float) - np.array(e["from"], float)
        if np.any(size) and any(f.get("enabled", True) for f in e["faces"].values()):
            for i in range(8):
                yield (m @ np.append([size[k] if i >> k & 1 else 0 for k in range(3)], 1))[:3]
        yield from _points(e.get("children") or [], m)


def _scale(els, k):
    for e in els:
        for key in ("from", "to", "rotationOrigin"):
            e[key] = [round(v * k, 3) for v in e[key]]
        _scale(e.get("children") or [], k)



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


def _boxes(els, M=np.eye(4)):
    for e in els:
        m = M @ _M(e); size = np.array(e["to"], float) - np.array(e["from"], float)
        if np.any(size) and any(f.get("enabled", True) for f in e["faces"].values()):
            yield [tuple((m @ np.append([size[k] if i >> k & 1 else 0 for k in range(3)], 1))[:3]) for i in range(8)]
        yield from _boxes(e.get("children") or [], m)


GUI_ROT, GUI_ORIGIN = (151, -3), (7.79, 1.28, 8.0)   # gem-rough guiTransform rotation and origin (x16), as vanilla
GUI_AIM = (0.31, 0.1)   # units right/down of the transform origin: the item's -3/-1 px slot offset puts the origin left of centre


def wrap(children, texture_id, area, max_size=3.6):
    """Vanilla-style origin element; scales the crystals until the hotbar silhouette has 'area' square units (vanilla's
    rough gems show about 4.2), the largest extent at most max_size, rests them on y = 0 and centres the silhouette."""
    P = np.array(list(_points(children))); lo, hi = P.min(0), P.max(0)
    a, _ = gui_silhouette(list(_boxes(children)), GUI_ROT, (0, 0, 0))
    k = min(math.sqrt(area / a), max_size / max(hi - lo)); _scale(children, k)
    c = (lo + hi) / 2 * k; org = [8 - c[0], -lo[1] * k, 8 - c[2]]
    shifted = [[(v[0] + org[0], v[1] + org[1], v[2] + org[2]) for v in b] for b in _boxes(children)]
    _, off = gui_silhouette(shifted, GUI_ROT, GUI_ORIGIN)
    dx, dz = gui_shift((off[0] - GUI_AIM[0], off[1] - GUI_AIM[1]), GUI_ROT)
    org = [round(org[0] + dx, 3), round(org[1], 3), round(org[2] + dz, 3)]
    return {
        "editor": {"allAngles": True, "singleTexture": False},
        "textureWidth": 16, "textureHeight": 16,
        "textures": {"gem": f"earth:item/gem/rough/{texture_id}"},
        "elements": [{
            "name": "origin", "from": org, "to": org, "rotationOrigin": [8.0, 0.0, 8.0],
            "faces": {k: {"texture": "#gem", "uv": [0, 0, 1, 1], "enabled": False} for k in FACES},
            "children": children}]}


# ---------------------------------------------------------------- crystal habits
STD = UV()


def octlayer(name, R, y0, y1, uv, L, spin=0.0, **kw):
    """Octagonal prism (box + 45-degree twin) with the same across-flats width as a hexagon of radius R; for caps and
    thin crystals where two boxes read the same as three."""
    return [box(f"{name}{j}", 1.7 * R, 1.7 * R, y0 + .004 * j, y1 - .004 * j, uv, L, ry=spin + 45 * j, **kw) for j in (0, 1)]


def prism(name, pos, bearing, tilt, R, L, spin=0.0, caps=(), uv=STD, hexagonal=True):
    """Prism of length L (hexagonal, or octagonal for thin ones) with stepped octagonal caps [(radius factor, height)]."""
    total = L + sum(h for _, h in caps)
    ch = (hexlayer if hexagonal else octlayer)(name, R, 0, L, uv, total, spin)
    y = L
    for i, (f, h) in enumerate(caps):
        ch += octlayer(f"{name}Cap{i}", R * f, y, y + h, uv, total, spin + 15, bottom=False)
        y += h
    return crystal(name, pos, bearing, tilt, ch)


def beryl():
    return [prism("Main", (1.3, 0.55, 1.3), 90, 9, 0.95, 2.7, spin=8, caps=[(0.82, 0.14)]),
            prism("Side", (2.55, 0.25, 0.75), 270, 16, 0.55, 1.7, spin=-20),
            prism("Shard", (0.35, 0.2, 1.9), 200, 12, 0.4, 1.2, spin=35, hexagonal=False)]


def tourmaline():
    # long, slender, striated prisms with a low stepped cap; the texture puts the pink 'watermelon' end at the tip
    return [prism("Main", (1.3, 0.5, 1.2), 270, 11, 0.72, 3.1, spin=-12, caps=[(0.7, 0.14)]),
            prism("Side", (2.3, 0.3, 1.9), 60, 19, 0.45, 2.1, spin=25, caps=[(0.6, 0.1)], hexagonal=False),
            prism("Twin", (0.4, 0.3, 1.4), 140, 12, 0.4, 1.5, spin=5, hexagonal=False)]


def topaz():
    # orthorhombic: stout rectangular prism with a steep stepped wedge, a smaller crystal beside it
    def xtal(name, pos, bearing, tilt, w, d, L, steps, ry):
        total = L + sum(h for _, _, h in steps)
        ch = [box(f"{name}Body", w, d, 0, L, STD, total, ry=ry)]
        y = L
        for i, (fw, fd, h) in enumerate(steps):
            ch.append(box(f"{name}Tip{i}", w * fw, d * fd, y, y + h, STD, total, ry=ry, bottom=False)); y += h
        return crystal(name, pos, bearing, tilt, ch)
    return [xtal("Main", (1.4, 0.55, 1.3), 90, 8, 1.35, 1.1, 2.1, [(0.78, 0.8, .22), (0.5, 0.55, .22), (0.22, 0.3, .22)], 15),
            xtal("Side", (2.65, 0.25, 0.7), 270, 15, 0.7, 0.55, 1.5, [(0.7, 0.7, .15), (0.3, 0.35, .15)], -30)]


def garnet():
    # rhombic dodecahedron: three cubes turned through 45 degrees approximate the 12 rhomb faces; a small cluster
    def drop(n, pos, s, spin):
        u = UV(k=2.5)
        ch = [rot_keys(box(n + "A", s, s, -s / 2, s / 2, u, s), ry=45),
              rot_keys(box(n + "B", s, s, -s / 2, s / 2, u, s), rx=45),
              rot_keys(box(n + "C", s, s, -s / 2, s / 2, u, s), rz=45)]
        for e in ch:
            e["rotationOrigin"] = [0, 0, 0]
        return crystal(n, pos, 0, 0, [rot_keys({"name": n + "Spin", "from": [0, 0, 0], "to": [0, 0, 0], "rotationOrigin": [0, 0, 0],
                                                  "faces": {k: {"texture": "#gem", "uv": [0, 0, 1, 1], "enabled": False} for k in FACES},
                                                  "children": ch}, ry=spin)])
    return [drop("Big", (1.4, 0.5 + 0.68, 1.4), 1.35, 8), drop("Mid", (2.7, 0.25 + 0.43, 0.8), 0.85, 20),
            drop("Small", (0.5, 0.2 + 0.3, 2.5), 0.6, -10)]


SPINEL = UV(top=(1, 5, 7, 11))   # step tops take the body colour, so the stack reads as one octahedron, not stripes


def spinel():
    # octahedra: stacked squares (7, the twin 5) turned 45 degrees in true octahedral proportion (apex to apex = 1.41 x girdle),
    # tipped sideways so they show the double-pyramid outline rather than an upright stack; a smaller twin against it
    def octa(n, pos, s, spin, bearing, tilt, levels=7):
        L = {7: [(0.2, .202), (0.46, .202), (0.73, .202), (1.0, .202), (0.73, .202), (0.46, .202), (0.2, .202)],
             5: [(0.26, .283), (0.62, .283), (1.0, .283), (0.62, .283), (0.26, .283)]}[levels]
        total = sum(h for _, h in L) * s; ch, y = [], 0.0
        for i, (k, hh) in enumerate(L):
            ch.append(box(f"{n}{i}", s * k, s * k, y - total / 2, y + hh * s - total / 2, SPINEL, total, ry=spin + 45,
                          top=(i >= levels // 2), bottom=(i <= levels // 2)))
            y += hh * s
        for e in ch:     # v along the whole octahedron
            for fn in ("north", "south", "east", "west"):
                u = e["faces"][fn]["uv"]; y0, y1 = e["from"][1] / total + .5, e["to"][1] / total + .5
                e["faces"][fn]["uv"] = [u[0], round(16 * (1 - y1), 3), u[2], round(16 * (1 - y0), 3)]
        return crystal(n, pos, bearing, tilt, ch)
    return [octa("Oct", (0.0, 1.2, 0.0), 2.0, 10, 100, 40), octa("Tw", (-0.95, 0.75, -0.45), 1.2, -10, 250, 30, levels=5)]


# ruby texture zones: deep carmine face, glassy light edge, dark underside, tilted-face variant
RUBY_FLAT = UV(side=(8, 0, 16, 1.5), top=(0, 0, 8, 8), bottom=(0, 8, 8, 16), k=2.6)
RUBY_TILT = UV(side=(8, 0, 16, 1.5), top=(8, 8, 16, 16), bottom=(0, 8, 8, 16), k=2.6)   # second plate: lighter face


def ruby():
    """Flat hexagonal tabular plates: a broad plate tipped towards the viewer carrying a smaller plate and a thin
    terrace stacked off-centre (each layer a little turned), and a second plate standing across it at an angle."""
    def plates(name, pos, bearing, tilt, layers):
        ch = []
        for i, (dx, dz, R, y0, y1, spin, uv) in enumerate(layers):
            ch += hexlayer(f"{name}{i}_", R, y0, y1, uv, y1 - y0 + 1e-9, spin, cx=dx, cz=dz, bottom=(i == 0))
        for e in ch:     # the edge strip is thin: every layer samples the same few light rows
            for fn in ("north", "south", "east", "west"):
                u = e["faces"][fn]["uv"]; e["faces"][fn]["uv"] = [u[0], 0, u[2], 1.5]
        return crystal(name, pos, bearing, tilt, ch)
    return [plates("Stack", (0.0, 0.5, 0.0), 180, 24, [(0, 0, 1.3, 0, .42, 10, RUBY_FLAT), (.32, -.3, .82, .42, .82, 32, RUBY_TILT),
                                                       (.24, -.4, .42, .82, .94, 18, RUBY_FLAT)]),
            plates("Cross", (-0.75, 0.7, 0.65), 200, 70, [(0, 0, 0.95, 0, .32, -6, RUBY_TILT)])]


def sapphire():
    """Steep hexagonal barrel tapering to both ends (stepped bipyramid), lying across the slot, with a smaller one
    crossing in front. Hexagonal body (3 boxes), octagonal shoulders (box + 45-degree twin), single-box tips."""
    def spindle(name, pos, bearing, tilt, R, L, spin, prof):
        ch = []
        n = len(prof)
        for i, (a, b, f, kind) in enumerate(prof):
            y0, y1 = (a - .5) * L, (b - .5) * L
            # an end face shows only where the next level towards the tip is narrower (or there is none)
            up = i == n - 1 or prof[i + 1][2] < f
            dn = i == 0 or prof[i - 1][2] < f
            if kind == 1:
                ch.append(box(f"{name}T{i}", R * f * 1.4, R * f * 1.4, y0, y1, STD, 1, ry=spin + 15, top=up, bottom=dn))
            elif kind == 2:
                ch += [box(f"{name}S{i}{j}", R * f * 1.6, R * f * 1.6, y0 + .004 * j, y1 - .004 * j, STD, 1, ry=spin + 45 * j, top=up, bottom=dn)
                       for j in (0, 1)]
            else:
                ch += hexlayer(f"{name}L{i}", R * f, y0, y1, STD, 1, spin, top=up, bottom=dn)
        for e in ch:     # side v along the whole crystal (tip of the texture strip = upper end)
            for fn in ("north", "south", "east", "west"):
                u = e["faces"][fn]["uv"]; y0, y1 = e["from"][1] / L + .5, e["to"][1] / L + .5
                e["faces"][fn]["uv"] = [u[0], round(16 * (1 - y1), 3), u[2], round(16 * (1 - y0), 3)]
        return crystal(name, pos, bearing, tilt, ch)
    main = [(0, .15, .3, 1), (.15, .33, .64, 2), (.33, .67, 1.0, 3), (.67, .85, .64, 2), (.85, 1, .3, 1)]
    small = [(0, .18, .45, 1), (.18, .82, 1.0, 2), (.82, 1, .45, 1)]
    return [spindle("Main", (0.0, 1.0, 0.0), 108, 62, 0.66, 3.4, 0, main),
            spindle("Small", (-0.2, 0.55, -0.75), 128, 72, 0.42, 2.0, 20, small)]


GEMS = {  # id: (builder, palette or painter, hotbar silhouette in square units; vanilla's rough gems show about 4.2)
    "beryl": (beryl, "aquamarine", 4.7), "tourmaline": (tourmaline, "tourmaline", 4.7), "sapphire": (sapphire, "sapphire", 4.6),
    "ruby": (ruby, "ruby", 4.6), "topaz": (topaz, "topaz", 4.5), "garnet": (garnet, "garnet", 4.4), "spinel": (spinel, "spinel", 4.5),
}


# ---------------------------------------------------------------- textures
# palettes: (deep, body, light, glint) in RGB
PAL = {
    "aquamarine": ((38, 120, 150), (110, 200, 215), (190, 240, 240), (245, 255, 255)),
    "tourmaline": ((30, 92, 60), (70, 160, 100), (220, 110, 150), (200, 240, 200)),  # green body, pink 'watermelon' end
    "sapphire": ((25, 50, 150), (55, 105, 215), (130, 175, 245), (225, 238, 255)),
    "ruby": ((110, 10, 35), (190, 25, 55), (240, 100, 120), (255, 215, 220)),
    "topaz": ((150, 85, 30), (225, 150, 55), (250, 205, 120), (255, 245, 210)),
    "garnet": ((95, 12, 30), (160, 28, 50), (225, 85, 95), (255, 190, 185)),
    "spinel": ((150, 18, 85), (230, 60, 125), (255, 140, 185), (255, 230, 240)),
}


def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def make_texture(name, pal_name, seed=1):
    rng = random.Random(seed)
    deep, body, light, glint = PAL[pal_name]
    S = 32
    img = np.zeros((S, S, 3), float)
    for y in range(S):
        for x in range(S):
            if x < 16:  # side zone, gradient along the crystal axis (top of image = crystal tip)
                t = y / 31.0
                col = lerp(light if pal_name != "tourmaline" else light, body, min(1, t * 1.5)) if t < .33 else lerp(body, deep, (t - .33) / .67)
                if pal_name == "tourmaline":
                    col = lerp((235, 120, 160), (90, 175, 110), min(1, t * 2.2)) if t < .45 else lerp((70, 160, 100), (28, 80, 55), (t - .45) / .55)
                # vertical facet bands: alternate faces slightly brighter/darker, bright edge on the left of each facet
                f = (x % 8) / 8.0
                shade = 0.82 + 0.28 * math.sin(f * math.pi)
                col = tuple(c * shade for c in col)
                if pal_name == "tourmaline" and x % 2 == 0:  # striations along the c-axis
                    col = tuple(c * 0.86 for c in col)
                if pal_name in ("sapphire", "ruby") and y % 6 < 1:  # growth banding on the barrel
                    col = tuple(c * 0.9 for c in col)
                if pal_name == "aquamarine" and rng.random() < 0.06:  # hollow growth tubes / inclusions
                    col = tuple(min(255, c * 1.12) for c in col)
            elif y < 16:  # top zone: radial facets towards the apex
                dx, dy = x - 23.5, y - 7.5
                a = math.atan2(dy, dx)
                r = math.hypot(dx, dy) / 8
                k = (math.floor((a + math.pi) / (math.pi / 3.0)) % 2)
                col = lerp(light, body, min(1, r * 0.9)) if k else lerp(glint, light, min(1, r))
                col = lerp(col, light, 0.0)
            else:  # bottom zone: dark
                col = lerp(body, deep, 0.7)
            img[y, x] = col
    # a few glints and soft speckle for translucency
    for _ in range(6):
        gx, gy = rng.randrange(0, 15), rng.randrange(1, 28)
        for dy in range(rng.choice((2, 3, 4))):
            if gy + dy < 32:
                img[gy + dy, gx] = lerp(tuple(img[gy + dy, gx]), glint, 0.6)
    noise = np.array([[rng.uniform(-6, 6) for _ in range(S)] for _ in range(S)])
    img += noise[:, :, None]
    im = Image.fromarray(np.clip(img, 0, 255).astype("uint8"), "RGB").convert("RGBA")
    # slight transparency like vanilla gems: keep opaque for rough items, alpha 235 in top zone
    px = im.load()
    for y in range(S):
        for x in range(S):
            if x >= 16 and y < 16:
                r, g, b, _ = px[x, y]
                px[x, y] = (r, g, b, 235)
    im.save(os.path.join(HERE, "textures", name + ".png"))


# ---------------------------------------------------------------- corundum textures (soft noise like vanilla's gem textures)
def _noise(rng, w, h, cells=((3, .5), (6, .32), (16, .18))):
    acc = np.zeros((h, w))
    for c, amp in cells:
        g = Image.fromarray((np.array([[rng.random() for _ in range(c)] for _ in range(c)]) * 255).astype("uint8"))
        acc += amp * np.asarray(g.resize((w, h), Image.BICUBIC), float) / 255
    return (acc - acc.min()) / (acc.max() - acc.min() + 1e-9)


def _ramp(t, stops):
    t = np.clip(t, 0, 1); out = np.zeros(t.shape + (3,))
    for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
        m = (t >= p0) & (t <= p1); f = ((t - p0) / (p1 - p0))[m][:, None]
        out[m] = np.array(c0) + (np.array(c1) - np.array(c0)) * f
    return out


def ruby_texture(rng):
    """Deep pinkish carmine plate faces (0-15, 0-15), glassy light edge rows (16-31, 0-2), dark underside
    (0-15, 16-31) and a tilted-face variant with a sheen (16-31, 16-31)."""
    img = np.zeros((32, 32, 3))
    face = [(0, (92, 8, 38)), (.4, (146, 16, 60)), (.75, (182, 34, 82)), (1, (212, 68, 112))]
    img[0:16, 0:16] = _ramp(_noise(rng, 16, 16) + np.array([[rng.uniform(-.05, .05) for _ in range(16)] for _ in range(16)]), face)
    edge = _ramp(_noise(rng, 16, 16) * .5 + .5, [(0, (200, 70, 112)), (.6, (236, 128, 162)), (1, (252, 196, 214))])
    edge[0] = [(255, 214, 226) if rng.random() < .35 else (244, 160, 186) for _ in range(16)]
    edge[1] = [(236, 132, 166) if rng.random() < .8 else (250, 190, 208) for _ in range(16)]
    edge[2] = [(222, 104, 142) for _ in range(16)]
    img[0:16, 16:32] = edge
    img[16:32, 0:16] = _ramp(_noise(rng, 16, 16), [(0, (64, 4, 24)), (1, (120, 14, 48))])
    tilt = _ramp(_noise(rng, 16, 16), [(0, (124, 14, 54)), (.5, (170, 28, 76)), (1, (210, 64, 110))])
    img[16:32, 16:32] = tilt
    for _ in range(6):                                   # glints
        y, x = rng.randrange(16), rng.randrange(16)
        img[y + rng.choice((0, 16)), x] = (255, 214, 226)
    return img


def sapphire_texture(rng):
    """Side strip (0-15, 0-31) runs tip to tip: deep blue core, lighter zoning towards both ends and in thin bands
    across the axis, faint facet columns; tip faces light (16-31, 0-15); lower faces mid blue (16-31, 16-31)."""
    img = np.zeros((32, 32, 3))
    n = _noise(rng, 16, 32)
    for y in range(32):
        t = abs(y - 15.5) / 15.5
        z = .12 + .45 * max(0, (t - .45) / .55) ** 1.4 + .25 * n[y].mean()
        if y in (5, 10, 21, 26): z += .28                  # growth zones
        if y in (13, 18): z -= .1
        for x in range(16):
            img[y, x] = _ramp(np.array([z + .18 * (n[y, x] - .5)]), [(0, (22, 42, 132)), (.35, (40, 76, 186)), (.7, (92, 134, 224)), (1, (176, 204, 250))])[0]
            img[y, x] *= (0.9, 1.0, 1.06, 1.0, 0.95)[x % 5]
            if x in (2, 3) and rng.random() < .8:          # glassy sheen along the prism
                img[y, x] = img[y, x] * .7 + np.array((190, 214, 255)) * .3
    img[0:16, 16:32] = _ramp(_noise(rng, 16, 16), [(0, (60, 100, 204)), (.7, (104, 146, 230)), (1, (170, 200, 248))])
    img[16:32, 16:32] = _ramp(_noise(rng, 16, 16), [(0, (34, 62, 160)), (1, (78, 118, 214))])
    for _ in range(5):
        img[rng.randrange(32), rng.randrange(16)] = (226, 238, 255)
    return img


PAINTERS = {"ruby": ruby_texture, "sapphire": sapphire_texture}


def write_texture(gid, pal):
    if gid in PAINTERS:
        img = PAINTERS[gid](random.Random(sum(map(ord, gid))))
        Image.fromarray(np.clip(img, 0, 255).astype("uint8"), "RGB").convert("RGBA").save(os.path.join(HERE, "textures", gid + ".png"))
    else:
        make_texture(gid, pal, seed=sum(map(ord, gid)))


def install():
    it = json.load(open(os.path.join(LIVE, "itemtypes", "resource", "gem-rough.json"), encoding="utf8"))
    ids = next(v["states"] for v in it["variantgroups"] if v["code"] == "ore")
    for gid in ids:
        shutil.copyfile(os.path.join(HERE, "shapes", gid + ".json"), os.path.join(LIVE, "shapes", "item", "gem", "rough", gid + ".json"))
        shutil.copyfile(os.path.join(HERE, "textures", gid + ".png"), os.path.join(LIVE, "textures", "item", "gem", "rough", gid + ".png"))
    print("live gems:", ", ".join(ids))


if __name__ == "__main__":
    for gid, (fn, pal, size) in GEMS.items():
        shape = wrap(fn(), gid, size)
        with open(os.path.join(HERE, "shapes", gid + ".json"), "w") as fh:
            json.dump(shape, fh, indent=1)
        write_texture(gid, pal)
        print("wrote", gid, sum(1 for _ in _points(shape["elements"])) // 8, "boxes")
    if "--live" in sys.argv:
        install()
