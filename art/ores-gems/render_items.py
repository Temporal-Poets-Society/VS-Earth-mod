#!/usr/bin/env python
"""Item preview the way Vintage Story 1.22.7 draws it: Earth's live ore chunks and rough gems beside vanilla's.

Usage:  python -I render_items.py [out_png]        (default: previews/items-sheet.png next to this script)

Per item: the hotbar slot at GUI scale 1 (48 px, guiTransform, gui shader shading) shown 1:1 and enlarged x3, and a
dropped-item view (groundTransform x 0.2 like EntityItemRenderer, seen from about a block away, looking down 35 degrees).
Geometry follows ShapeTesselator: element matrix = T(origin) Rx Ry Rz T(from - origin), children nested in the parent
frame; face corners and UVs follow CubeMeshUtil (face 'rotation' shifts the UV corners). Textures resolve as the game
does: 'earth:' from assets/earth, vanilla from %APPDATA%/Vintagestory/assets/{survival,game}; vanilla graded chunks get
granite1 under the ore overlay. Drafts that are not live yet (bog iron, bauxite) render from art/ores-gems.
"""
import json, math, os, re, sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
EARTH = os.path.join(REPO, "assets", "earth")
VAN = os.path.join(os.environ.get("APPDATA", ""), "Vintagestory", "assets")

# ------------------------------------------------------------------ math
def T(x, y, z):
    m = np.eye(4); m[:3, 3] = (x, y, z); return m


def R(axis, deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a); m = np.eye(4)
    i, j = {"x": (1, 2), "y": (2, 0), "z": (0, 1)}[axis]
    m[i, i] = c; m[i, j] = -s; m[j, i] = s; m[j, j] = c
    return m


def S(k):
    m = np.eye(4); m[0, 0] = m[1, 1] = m[2, 2] = k; return m


# CubeMeshUtil: corner signs per face (N, E, S, W, U, D) and the UV corner table shared by all faces
CORNERS = {
    "north": [(-1, -1, -1), (-1, 1, -1), (1, 1, -1), (1, -1, -1)],
    "east": [(1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1)],
    "south": [(1, -1, 1), (1, 1, 1), (-1, 1, 1), (-1, -1, 1)],
    "west": [(-1, -1, 1), (-1, 1, 1), (-1, 1, -1), (-1, -1, -1)],
    "up": [(1, 1, 1), (1, 1, -1), (-1, 1, -1), (-1, 1, 1)],
    "down": [(-1, -1, 1), (-1, -1, -1), (1, -1, -1), (1, -1, 1)]}
NORMALS = {"north": (0, 0, -1), "east": (1, 0, 0), "south": (0, 0, 1), "west": (-1, 0, 0), "up": (0, 1, 0), "down": (0, -1, 0)}
UVC = [(1, 0), (1, 1), (0, 1), (0, 0)]


def load_json(p):
    txt = open(p, encoding="utf8").read()
    try:
        return json.loads(txt)
    except ValueError:
        txt = re.sub(r"//[^\n]*", "", txt)
        txt = re.sub(r",(\s*[}\]])", r"\1", txt)
        return json.loads(txt)


def tex_path(code):
    dom, _, path = code.rpartition(":")
    if dom == "earth":
        return os.path.join(EARTH, "textures", path + ".png")
    for d in ("survival", "game", "creative"):
        p = os.path.join(VAN, d, "textures", path + ".png")
        if os.path.exists(p):
            return p
    raise FileNotFoundError(code)


def shape_path(code):
    dom, _, path = code.rpartition(":")
    if dom == "earth":
        return os.path.join(EARTH, "shapes", path + ".json")
    return os.path.join(VAN, "survival", "shapes", path + ".json")


def load_tex(spec):
    """spec: texture code, absolute png path, or (base, overlay) tuple."""
    if isinstance(spec, tuple):
        base = load_tex(spec[0]).copy(); ov = load_tex(spec[1])
        if ov.size != base.size:
            ov = ov.resize(base.size, Image.NEAREST)
        base.alpha_composite(ov); return base
    p = spec if os.path.isabs(spec) else tex_path(spec)
    return Image.open(p).convert("RGBA")


def tessellate(shape, textures):
    """-> list of (corners 4x3 block units, uv 4x2 normalised, texture array, normal 3)."""
    tw, th = shape.get("textureWidth", 16), shape.get("textureHeight", 16)
    out = []

    def walk(els, M):
        for e in els:
            f = np.array(e["from"], float); t = np.array(e["to"], float)
            ro = np.array(e.get("rotationOrigin", [0, 0, 0]), float)
            m = M @ T(*(ro / 16))
            for ax in "xyz":
                a = e.get("rotation" + ax.upper(), 0)
                if a: m = m @ R(ax, a)
            m = m @ T(*((f - ro) / 16))
            size = (t - f) / 16
            if np.any(size != 0):
                for fn, fd in e.get("faces", {}).items():
                    if fd is None or fd.get("enabled") is False or fn not in CORNERS:
                        continue
                    key = fd.get("texture", "#")[1:]
                    tex = textures.get(key)
                    if tex is None:
                        continue
                    u0, v0, u1, v1 = fd.get("uv", [0, 0, 0, 0])
                    rot = int(fd.get("rotation", 0) / 90)
                    pts, uvs = [], []
                    for j, c in enumerate(CORNERS[fn]):
                        p = size / 2 + size * np.array(c) / 2
                        pts.append((m @ np.append(p, 1))[:3])
                        cu, cv = UVC[(rot + j) % 4]
                        uvs.append(((u0 + (u1 - u0) * cu) / tw, (v1 + (v0 - v1) * cv) / th))
                    n = m[:3, :3] @ np.array(NORMALS[fn], float)
                    out.append((np.array(pts), np.array(uvs), tex, n / np.linalg.norm(n)))
            walk(e.get("children") or [], m)
    walk(shape["elements"], np.eye(4))
    return out


def raster(faces, view, W, H, shade, ss=1):
    """view: 4x4 to screen pixels (x right, y down, z toward viewer). Returns RGBA image W x H."""
    W2, H2 = W * ss, H * ss
    zb = np.full((H2, W2), -1e18); img = np.zeros((H2, W2, 4), float)
    V = S(1); V[:] = view; V[:2] *= ss
    for pts, uvs, tex, n in faces:
        ns = view[:3, :3] @ n; ns /= np.linalg.norm(ns)
        if ns[2] <= 1e-4:
            continue
        b = shade(ns)
        P = (V @ np.c_[pts, np.ones(4)].T).T[:, :3]
        TH, TW = tex.shape[:2]
        for tri in ((0, 1, 2), (0, 2, 3)):
            A = P[list(tri)]; Q = uvs[list(tri)]
            x0, x1 = int(max(0, math.floor(A[:, 0].min()))), int(min(W2 - 1, math.ceil(A[:, 0].max())))
            y0, y1 = int(max(0, math.floor(A[:, 1].min()))), int(min(H2 - 1, math.ceil(A[:, 1].max())))
            if x1 < x0 or y1 < y0:
                continue
            gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + .5, np.arange(y0, y1 + 1) + .5)
            d = (A[1, 1] - A[2, 1]) * (A[0, 0] - A[2, 0]) + (A[2, 0] - A[1, 0]) * (A[0, 1] - A[2, 1])
            if abs(d) < 1e-12:
                continue
            l0 = ((A[1, 1] - A[2, 1]) * (gx - A[2, 0]) + (A[2, 0] - A[1, 0]) * (gy - A[2, 1])) / d
            l1 = ((A[2, 1] - A[0, 1]) * (gx - A[2, 0]) + (A[0, 0] - A[2, 0]) * (gy - A[2, 1])) / d
            l2 = 1 - l0 - l1
            m = (l0 >= -1e-6) & (l1 >= -1e-6) & (l2 >= -1e-6)
            z = l0 * A[0, 2] + l1 * A[1, 2] + l2 * A[2, 2]
            u = l0 * Q[0, 0] + l1 * Q[1, 0] + l2 * Q[2, 0]
            v = l0 * Q[0, 1] + l1 * Q[1, 1] + l2 * Q[2, 1]
            tx = np.clip(np.floor(u * TW).astype(int), 0, TW - 1); ty = np.clip(np.floor(v * TH).astype(int), 0, TH - 1)
            col = tex[ty, tx].astype(float)
            sub = zb[y0:y1 + 1, x0:x1 + 1]
            upd = m & (z > sub) & (col[..., 3] > 25)
            sub[upd] = z[upd]
            c = col[upd]; c[:, :3] = np.clip(c[:, :3] * b, 0, 255); c[:, 3] = 255
            img[y0:y1 + 1, x0:x1 + 1][upd] = c
    im = Image.fromarray(img.astype(np.uint8), "RGBA")
    return im.resize((W, H), Image.BOX) if ss > 1 else im


def gui_shade(n):
    # gui.fsh + normalshading.fsh, lightPosition (0.7071, -0.7071, 0) in screen space (y down)
    nb = max(0.45, 0.5 + 0.5 * (n[0] * 0.7071 - n[1] * 0.7071))
    nb = max(nb, n[1] * 0.95)
    nb += max(0.0, -n[2]) * 0.2
    return nb * 1.2


def world_shade(n_world):
    L = np.array([0.35, 0.85, 0.4]); L /= np.linalg.norm(L)
    return 0.62 + 0.45 * max(0.0, float(np.dot(n_world, L)))


def tf_matrix(tf):
    o = tf.get("origin", {}); r = tf.get("rotation", {}); s = tf.get("scale", 1)
    ox, oy, oz = o.get("x", 0), o.get("y", 0), o.get("z", 0)
    return R("x", r.get("x", 0)) @ R("y", r.get("y", 0)) @ R("z", r.get("z", 0)) @ T(-ox, -oy, -oz), s, (ox, oy, oz)


def render_gui(faces, tf, slot=48, ss=4):
    rot, s, (ox, oy, oz) = tf_matrix(tf)
    tr = tf.get("translation", {})
    size = 25.6
    view = T(slot / 2 - 3 + ox + tr.get("x", 0), slot / 2 - 1 + oy + tr.get("y", 0), 0) @ S(size * s) @ rot
    return raster(faces, view, slot, slot, gui_shade, ss)


def render_ground(faces, tf, W=200, px_per_block=520, yaw=30, pitch=35, ss=2):
    rot, s, _ = tf_matrix(tf)
    model = S(0.2 * s) @ rot                                   # EntityItemRenderer: 0.2 x groundTransform scale
    cam = R("x", pitch) @ R("y", yaw)                           # camera: look down 'pitch' degrees
    flip = np.diag([1.0, -1.0, 1.0, 1.0])                       # screen y down, z toward viewer
    world = cam @ model
    view = T(W / 2, W * 0.66, 0) @ S(px_per_block) @ flip @ world
    # shading uses world-space normals; raster gives screen normals, so wrap the shade function
    inv = np.linalg.inv((flip @ cam)[:3, :3])
    shade = lambda ns: world_shade(inv @ ns)
    return raster(faces, view, W, W, shade, ss)


# ------------------------------------------------------------------ item list
def earth_types():
    d = os.path.join(EARTH, "itemtypes", "resource")
    return {n: load_json(os.path.join(d, n + ".json")) for n in ("ore-graded", "ore-ungraded", "gem-rough")}


VAN_ORE_TF = {"guiTransform": {"translation": {"x": 3, "y": 2, "z": 0}, "rotation": {"x": 150, "y": -38, "z": 0},
                               "origin": {"x": 0.5, "y": 0.1, "z": 0.5}, "scale": 3.8},
              "groundTransform": {"rotation": {}, "origin": {"x": 0.5, "y": 0, "z": 0.5}, "scale": 4.5}}
VAN_GEM_TF = {"guiTransform": {"translation": {"x": 0, "y": 0, "z": 0}, "rotation": {"x": 151, "y": -3, "z": 0},
                               "origin": {"x": 0.487, "y": 0.08, "z": 0.5}, "scale": 6},
              "groundTransform": {"rotation": {}, "origin": {"x": 0.5, "y": 0, "z": 0.5}, "scale": 4.5}}


def item(label, shape_file, tex_over, tfs):
    sh = load_json(shape_file)
    codes = dict(sh.get("textures", {})); codes.update(tex_over)
    textures = {k: np.array(load_tex(v)) for k, v in codes.items()}
    return dict(label=label, faces=tessellate(sh, textures), tf=tfs, n=sum(1 for _ in iter_elems(sh["elements"])))


def iter_elems(els):
    for e in els:
        if any(f.get("enabled", True) for f in e.get("faces", {}).values()):
            yield e
        yield from iter_elems(e.get("children") or [])


def sections():
    it = earth_types()
    out = []
    # rough gems
    g = it["gem-rough"]; states = next(v["states"] for v in g["variantgroups"] if v["code"] == "ore")
    gems = [item(f"gem {o}", shape_path(g["shape"]["base"].replace("{ore}", o)), {}, g) for o in states]
    van = [item(f"vanilla {o}", shape_path(f"item/gem/rough/normal/{o}"), {}, VAN_GEM_TF)
           for o in ("emerald", "diamond", "olivine_peridot", "quartz", "fluorite")]
    van.append(item("vanilla corundum (flawed)", shape_path("item/gem/rough/flawed/corundum"), {}, VAN_GEM_TF))
    out.append(("Rough gems: Earth", gems)); out.append(("Rough gems: vanilla", van))
    # graded chunks
    o = it["ore-graded"]; types = next(v["states"] for v in o["variantgroups"] if v["code"] == "type")
    for grade in ("poor", "medium", "rich", "bountiful"):
        row = []
        for t in types:
            base = next(v["base"] for k, v in o["shapeByType"].items() if k == "*-" + t)
            row.append(item(f"{t} {grade}", shape_path(base.replace("{grade}", grade)),
                            {"ore": o["textures"]["ore"]["base"].replace("{type}", t)}, o))
        out.append((f"Earth ore chunks, {grade}", row))
    u = it["ore-ungraded"]
    extra = [item("pyrite", shape_path(u["shape"]["base"]), {"ore": u["textures"]["ore"]["base"].replace("{type}", "pyrite")}, u)]
    drafts = os.path.join(HERE, "ores")
    for name, form in (("bogiron", "porous"), ("bauxite", "earthy")):
        for grade in ("poor", "medium", "bountiful"):
            sp = os.path.join(drafts, "shapes", f"{form}-{grade}.json")
            extra.append(item(f"{name} {grade} (draft)", sp, {"ore": os.path.join(drafts, "textures", name + ".png")}, o))
    out.append(("Earth ungraded and drafts", extra))
    vg = []
    for ore in ("hematite", "galena", "nativecopper", "malachite", "cassiterite", "sphalerite", "limonite", "pentlandite", "platinum"):
        sh = load_json(shape_path(f"item/ore/medium/{ore}"))
        vg.append(item(f"vanilla {ore}", shape_path(f"item/ore/medium/{ore}"),
                       {"ore": ("block/stone/rock/granite1", sh["textures"]["ore"])}, VAN_ORE_TF))
    out.append(("Vanilla graded chunks (medium, granite)", vg))
    vh = []
    for grade in ("poor", "rich", "bountiful"):
        sh = load_json(shape_path(f"item/ore/{grade}/hematite"))
        vh.append(item(f"vanilla hematite {grade}", shape_path(f"item/ore/{grade}/hematite"),
                       {"ore": ("block/stone/rock/granite1", sh["textures"]["ore"])}, VAN_ORE_TF))
    for ore in ("sulfur", "quartz", "lapislazuli", "cinnabar", "bituminouscoal", "fluorite"):
        vh.append(item(f"vanilla {ore}", shape_path(f"item/ore/ungraded/{ore}"), {}, VAN_ORE_TF))
    out.append(("Vanilla grades and ungraded", vh))
    return out


# ------------------------------------------------------------------ sheet
SLOT_BG = (58, 52, 46, 255); SLOT_EDGE = (34, 30, 27, 255); BG = (86, 80, 72, 255); GROUND = (92, 104, 70, 255)


def slot_tile(im):
    t = Image.new("RGBA", (48, 48), SLOT_BG); d = ImageDraw.Draw(t)
    d.rectangle([0, 0, 47, 47], outline=SLOT_EDGE); d.rectangle([1, 1, 46, 46], outline=(70, 64, 58, 255))
    t.alpha_composite(im); return t


def main(out):
    secs = sections()
    CW, CH = 400, 214; cols = 5
    rows = sum((len(r) + cols - 1) // cols for _, r in secs)
    sheet = Image.new("RGBA", (CW * cols, rows * CH + 30 * len(secs)), BG)
    d = ImageDraw.Draw(sheet); y = 0
    for title, items in secs:
        d.text((8, y + 9), title, fill=(255, 240, 200, 255)); y += 30
        for i, it in enumerate(items):
            x = (i % cols) * CW; yy = y + (i // cols) * CH
            gui = slot_tile(render_gui(it["faces"], it["tf"]["guiTransform"]))
            sheet.alpha_composite(gui, (x + 6, yy + 22))
            sheet.alpha_composite(gui.resize((144, 144), Image.NEAREST), (x + 60, yy + 22))
            gr = Image.new("RGBA", (190, 190), GROUND)
            gr.alpha_composite(render_ground(it["faces"], it["tf"]["groundTransform"], W=190, px_per_block=430))
            sheet.alpha_composite(gr, (x + 208, yy + 22))
            d.text((x + 6, yy + 6), f"{it['label']}  [{it['n']} el]", fill=(240, 240, 240, 255))
        y += ((len(items) + cols - 1) // cols) * CH
    os.makedirs(os.path.dirname(out), exist_ok=True)
    sheet.convert("RGB").save(out)
    print("wrote", out)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "previews", "items-sheet.png"))
