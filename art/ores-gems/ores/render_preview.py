"""Isometric preview renderer for Vintage Story shape JSON (boxes with from/to/rotationOrigin/rotationX/Y/Z and face UVs).
Usage: python render_preview.py            -> previews/*.png and previews/contact-sheet.png
Vanilla chunks are rendered for comparison from %APPDATA%\\Vintagestory\\assets (read only)."""
import json, math, os, re, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).parent
VS = Path(os.environ.get("APPDATA", "")) / "Vintagestory" / "assets" / "survival"
# face -> (normal, right tangent, down tangent) as seen from outside; y is up
FACES = {"north": ((0, 0, -1), (-1, 0, 0), (0, -1, 0)), "south": ((0, 0, 1), (1, 0, 0), (0, -1, 0)),
         "east": ((1, 0, 0), (0, 0, -1), (0, -1, 0)), "west": ((-1, 0, 0), (0, 0, 1), (0, -1, 0)),
         "up": ((0, 1, 0), (1, 0, 0), (0, 0, 1)), "down": ((0, -1, 0), (1, 0, 0), (0, 0, -1))}
LIGHT = np.array([-.35, .85, .4]); LIGHT /= np.linalg.norm(LIGHT)


def rot(rx, ry, rz):
    def m(a, ax):
        c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
        return np.array([[1, 0, 0], [0, c, -s], [0, s, c]]) if ax == 0 else np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]]) if ax == 1 else np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])
    return m(rz, 2) @ m(ry, 1) @ m(rx, 0)


def load_json(p):
    return json.loads(re.sub(r",(\s*[}\]])", r"\1", Path(p).read_text()))


def flatten(elements, off=(0, 0, 0), out=None):
    out = [] if out is None else out
    for e in elements:
        e2 = dict(e); e2["from"] = [e["from"][i] + off[i] for i in range(3)]; e2["to"] = [e["to"][i] + off[i] for i in range(3)]
        e2["rotationOrigin"] = [e.get("rotationOrigin", [e["from"][i] for i in range(3)])[i] + off[i] for i in range(3)]
        out.append(e2); flatten(e.get("children", []), tuple(e["from"][i] + off[i] for i in range(3)), out)
    return out


def render(shape, tex, size=192, ppu=30, yaw=-45, pitch=30, ss=3):
    """shape: dict, tex: {name: RGBA image}. Returns RGBA preview."""
    els = flatten(shape["elements"]); tw, th = shape.get("textureWidth", 16), shape.get("textureHeight", 16)
    S = size * ss; img = np.zeros((S, S, 4), np.float32); zb = np.full((S, S), -1e9, np.float32)
    cy_, sy_ = math.cos(math.radians(yaw)), math.sin(math.radians(yaw)); cp, sp = math.cos(math.radians(pitch)), math.sin(math.radians(pitch))
    Ry = np.array([[cy_, 0, sy_], [0, 1, 0], [-sy_, 0, cy_]]); Rx = np.array([[1, 0, 0], [0, cp, -sp], [0, sp, cp]]); V = Rx @ Ry
    pts = np.array([[e["from"][i] for i in range(3)] for e in els] + [[e["to"][i] for i in range(3)] for e in els])
    ctr = np.array([8.0, pts[:, 1].min() + 1.8, 8.0]); k = ppu * ss
    for e in els:
        f0, t0, org = np.array(e["from"], float), np.array(e["to"], float), np.array(e["rotationOrigin"], float)
        R = rot(e.get("rotationX", 0), e.get("rotationY", 0), e.get("rotationZ", 0)); sz = t0 - f0
        for fname, fd in e["faces"].items():
            if fd.get("enabled") is False or fd["texture"].lstrip("#") not in tex: continue
            n, r, dn = (np.array(v, float) for v in FACES[fname])
            ctr_f = (f0 + t0) / 2 + n * sz / 2                       # face centre in model space
            half_r, half_d = np.abs(r * sz).sum() / 2, np.abs(dn * sz).sum() / 2
            tl = ctr_f - r * half_r - dn * half_d                    # top-left corner (before rotation)
            P0 = R @ (tl - org) + org; Ru = R @ (r * half_r * 2); Rv = R @ (dn * half_d * 2); N = R @ n
            if (V @ N)[2] <= 0: continue                             # back face (camera looks down -z in view space)
            sP = lambda p: (np.array([(V @ (p - ctr))[0] * k + S / 2, -(V @ (p - ctr))[1] * k + S * .6]), (V @ (p - ctr))[2])
            a, za = sP(P0); b, zb_ = sP(P0 + Ru); c, zc = sP(P0 + Rv)
            M = np.array([[b[0] - a[0], c[0] - a[0]], [b[1] - a[1], c[1] - a[1]]])
            if abs(np.linalg.det(M)) < 1e-6: continue
            Mi = np.linalg.inv(M)
            x0, x1 = int(max(0, min(a[0], b[0], c[0], b[0] + c[0] - a[0]) - 1)), int(min(S - 1, max(a[0], b[0], c[0], b[0] + c[0] - a[0]) + 1))
            y0, y1 = int(max(0, min(a[1], b[1], c[1], b[1] + c[1] - a[1]) - 1)), int(min(S - 1, max(a[1], b[1], c[1], b[1] + c[1] - a[1]) + 1))
            if x1 <= x0 or y1 <= y0: continue
            X, Y = np.meshgrid(np.arange(x0, x1 + 1) + .5, np.arange(y0, y1 + 1) + .5)
            st = np.einsum("ij,jhw->ihw", Mi, np.stack([X - a[0], Y - a[1]]))
            s, t = st[0], st[1]; m = (s >= 0) & (s < 1) & (t >= 0) & (t < 1)
            depth = za + s * (zb_ - za) + t * (zc - za)
            u1, v1, u2, v2 = fd["uv"]; tx = tex[fd["texture"].lstrip("#")]; W, H = tx.size
            uu = ((u1 + s * (u2 - u1)) / tw * W).astype(int).clip(0, W - 1); vv = ((v1 + t * (v2 - v1)) / th * H).astype(int).clip(0, H - 1)
            col = np.asarray(tx)[vv, uu].astype(np.float32)
            lit = .55 + .45 * max(0.0, float(N @ (Ry.T @ Rx.T @ np.array([0, 0, 0]) if False else LIGHT)))
            col[..., :3] *= lit
            sub = zb[y0:y1 + 1, x0:x1 + 1]; ok = m & (depth > sub) & (col[..., 3] > 8)
            sub[ok] = depth[ok]; img[y0:y1 + 1, x0:x1 + 1][ok] = col[ok]
    out = Image.fromarray(img.clip(0, 255).astype(np.uint8), "RGBA")
    return out.resize((size, size), Image.BOX)


def vanilla_chunk(ore, grade="medium", rock="granite"):
    """Vanilla chunk = host rock texture with ore overlay, on vanilla's shape (reference only)."""
    sh = load_json(VS / "shapes/item/ore" / grade / f"{ore}.json")
    base = Image.open(VS / f"textures/block/stone/rock/{rock}1.png").convert("RGBA")
    ov = Image.open(VS / sh["textures"]["ore"].replace("block/", "textures/block/").replace("item/", "textures/item/") if False else VS / ("textures/" + sh["textures"]["ore"] + ".png")).convert("RGBA")
    base = base.resize(ov.size); base.alpha_composite(ov)
    return sh, {"ore": base}


def label(im, text):
    c = Image.new("RGBA", (im.width, im.height + 14), (30, 30, 34, 255)); c.paste(im, (0, 0), im)
    ImageDraw.Draw(c).text((4, im.height), text, fill=(220, 220, 220)); return c


def main():
    prev = HERE / "previews"; prev.mkdir(exist_ok=True)
    tex_dir = HERE / "textures"; shapes = sorted((HERE / "shapes").glob("*.json"))
    tiles = []
    # vanilla reference row
    for ore in ["galena", "malachite", "cassiterite", "sphalerite"]:
        sh, tex = vanilla_chunk(ore, "rich")
        tiles.append(label(render(sh, tex), f"vanilla {ore} rich"))
    forms = ["blocky", "rounded", "crystal", "plate", "nodule"]
    # one row per ore with its natural form, all four grades
    plan = [("pyrite", "blocky"), ("chalcopyrite", "rounded"), ("cerussite", "crystal"), ("vanadinite", "crystal"),
            ("wulfenite", "plate"), ("smithsonite", "nodule"), ("hemimorphite", "plate"), ("tetrahedrite", "rounded"),
            ("sperrylite", "rounded"), ("azurite", "nodule"), ("bogiron", "nodule"), ("bauxite", "rounded"), ("gypsum", "crystal")]
    cols = 4
    rows = [tiles]
    for ore, form in plan:
        tex = {"ore": Image.open(tex_dir / f"{ore}.png").convert("RGBA")}
        row = []
        for g in ["poor", "medium", "rich", "bountiful"]:
            sh = load_json(HERE / "shapes" / f"{form}-{g}.json"); im = render(sh, tex)
            im.save(prev / f"{ore}-{form}-{g}.png"); row.append(label(im, f"{ore} {form} {g}"))
        rows.append(row)
    # forms overview with a neutral test texture would hide ore look; use pyrite/vanadinite/wulfenite instead
    w, h = rows[0][0].size
    sheet = Image.new("RGBA", (w * cols, h * len(rows)), (30, 30, 34, 255))
    for r, row in enumerate(rows):
        for c, im in enumerate(row): sheet.paste(im, (c * w, r * h))
    sheet.save(prev / "contact-sheet.png"); print("contact sheet", sheet.size)


if __name__ == "__main__":
    main()
