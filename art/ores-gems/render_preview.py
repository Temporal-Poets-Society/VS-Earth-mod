#!/usr/bin/env python
"""Isometric preview of Vintage Story shape JSONs (boxes with from/to/rotationOrigin/rotationX/Y/Z, face UVs, children).
Usage: python render_preview.py <shapes_dir> <textures_dir> <out_png>
Textures are resolved by the last path part of each shape's first texture id (<textures_dir>/<name>.png).
Draws two views per shape on a dark tile; nearest-neighbour texture sampling, simple face shading.
Euler order assumed Rz(Ry(Rx v)) about rotationOrigin; verify against the game if combining several axes."""
import json, math, os, sys, re
import numpy as np
from PIL import Image, ImageDraw


def rot(v, rx, ry, rz):
    x, y, z = v
    c, s = math.cos(rx), math.sin(rx); y, z = y * c - z * s, y * s + z * c
    c, s = math.cos(ry), math.sin(ry); x, z = x * c + z * s, -x * s + z * c
    c, s = math.cos(rz), math.sin(rz); x, y = x * c - y * s, x * s + y * c
    return (x, y, z)


def load_json(p):
    return json.loads(re.sub(r',(\s*[}\]])', r'\1', open(p, encoding='utf8').read()))


FACES = {
    'north': ([(1, 0, 0), (0, 0, 0), (0, 1, 0), (1, 1, 0)], (0, 0, -1)),
    'south': ([(0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)], (0, 0, 1)),
    'east': ([(1, 0, 1), (1, 0, 0), (1, 1, 0), (1, 1, 1)], (1, 0, 0)),
    'west': ([(0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0)], (-1, 0, 0)),
    'up': ([(0, 1, 1), (1, 1, 1), (1, 1, 0), (0, 1, 0)], (0, 1, 0)),
    'down': ([(0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)], (0, -1, 0))}


def collect(els, off=(0, 0, 0), out=None):
    out = [] if out is None else out
    for e in els:
        f = [e['from'][i] + off[i] for i in range(3)]
        t = [e['to'][i] + off[i] for i in range(3)]
        ro = e.get('rotationOrigin', [0, 0, 0])
        ro = [ro[i] + off[i] for i in range(3)]
        r = [math.radians(e.get(k, 0)) for k in ('rotationX', 'rotationY', 'rotationZ')]
        for fn, fd in e['faces'].items():
            if fd.get('enabled') is False or f == t:
                continue
            cs, n = FACES[fn]
            pts = []
            for c in cs:
                p = [f[i] if c[i] == 0 else t[i] for i in range(3)]
                q = rot([p[i] - ro[i] for i in range(3)], *r)
                pts.append([q[i] + ro[i] for i in range(3)])
            out.append((pts, rot(n, *r), fd['uv']))
        collect(e.get('children', []), f, out)
    return out


def render(shape, tex, yaw, pitch, S=100, W=720):
    zb = np.full((W, W), -1e9)
    img = np.zeros((W, W, 4), np.uint8)
    cy, sy, cp, sp = math.cos(yaw), math.sin(yaw), math.cos(pitch), math.sin(pitch)

    def tr(x, y, z):
        x, z = x * cy + z * sy, -x * sy + z * cy
        y, z = y * cp - z * sp, y * sp + z * cp
        return x, y, z

    def proj(p):
        x, y, z = tr(p[0] - 8, p[1] - 1.5, p[2] - 8)
        return (W / 2 + x * S, W * 0.62 - y * S, z)

    TH, TW = tex.shape[0], tex.shape[1]
    L = np.array([-0.4, 0.8, -0.45]); L /= np.linalg.norm(L)
    for pts, n, uv in collect(shape['elements']):
        pr = [proj(p) for p in pts]
        if tr(*n)[2] < 0.0:
            continue
        light = 0.55 + 0.45 * max(0, n[0] * L[0] + n[1] * L[1] + n[2] * L[2])
        u0, v0, u1, v1 = uv
        uvs = [(u0, v1), (u1, v1), (u1, v0), (u0, v0)]
        for tri in ((0, 1, 2), (0, 2, 3)):
            P = [pr[i] for i in tri]; Q = [uvs[i] for i in tri]
            xs = [p[0] for p in P]; ys = [p[1] for p in P]
            x0, x1 = int(max(0, min(xs))), int(min(W - 1, max(xs) + 1))
            y0, y1 = int(max(0, min(ys))), int(min(W - 1, max(ys) + 1))
            if x1 < x0 or y1 < y0:
                continue
            gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + .5, np.arange(y0, y1 + 1) + .5)
            d = (P[1][1] - P[2][1]) * (P[0][0] - P[2][0]) + (P[2][0] - P[1][0]) * (P[0][1] - P[2][1])
            if abs(d) < 1e-9:
                continue
            a = ((P[1][1] - P[2][1]) * (gx - P[2][0]) + (P[2][0] - P[1][0]) * (gy - P[2][1])) / d
            b = ((P[2][1] - P[0][1]) * (gx - P[2][0]) + (P[0][0] - P[2][0]) * (gy - P[2][1])) / d
            c = 1 - a - b
            m = (a >= -1e-6) & (b >= -1e-6) & (c >= -1e-6)
            z = a * P[0][2] + b * P[1][2] + c * P[2][2]
            u = a * Q[0][0] + b * Q[1][0] + c * Q[2][0]
            v = a * Q[0][1] + b * Q[1][1] + c * Q[2][1]
            tx = np.clip((u / 16 * TW).astype(int), 0, TW - 1)
            ty = np.clip((v / 16 * TH).astype(int), 0, TH - 1)
            col = tex[ty, tx].astype(float); col[..., :3] *= light
            sub = zb[y0:y1 + 1, x0:x1 + 1]
            upd = m & (z > sub) & (col[..., 3] > 8)
            sub[upd] = z[upd]
            img[y0:y1 + 1, x0:x1 + 1][upd] = np.clip(col[upd], 0, 255).astype(np.uint8)
    return Image.fromarray(img, 'RGBA')


def main():
    sd, td, out = sys.argv[1:4]
    names = sorted(f[:-5] for f in os.listdir(sd) if f.endswith('.json'))
    tiles = []
    for n in names:
        sh = load_json(os.path.join(sd, n + '.json'))
        tid = list(sh['textures'].values())[0].split('/')[-1]
        tex = np.array(Image.open(os.path.join(td, tid + '.png')).convert('RGBA'))
        for yaw, pitch in ((math.radians(35), math.radians(28)), (math.radians(-60), math.radians(12))):
            tile = Image.new('RGBA', (360, 360), (44, 44, 50, 255))
            im = render(sh, tex, yaw, pitch)
            bb = im.getbbox()
            if bb:
                im = im.crop(bb)
                k = min(300 / im.width, 300 / im.height)
                im = im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))), Image.NEAREST)
                tile.alpha_composite(im, ((360 - im.width) // 2, (360 - im.height) // 2 + 10))
            ImageDraw.Draw(tile).text((6, 6), n, fill=(230, 230, 230, 255))
            tiles.append(tile)
    cols = 4
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new('RGBA', (360 * cols, 360 * rows), (44, 44, 50, 255))
    for i, t in enumerate(tiles):
        sheet.paste(t, ((i % cols) * 360, (i // cols) * 360))
    sheet.convert('RGB').save(out)
    print('wrote', out)


main()
