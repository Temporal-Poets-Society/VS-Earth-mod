"""A window's lake bed as depth below the water (blocks), two variants side by side: deeper is darker; land grey; where a
course draws, a dot. python bed_picture.py <dir>/<place>_<scale> <variant a> <variant b> <out.png>"""
import os, sys
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "ea-0148-offset-2026-11", "probe"))
from analyse import load

base, va, vb, out = sys.argv[1:5]
imgs = []
for v in (va, vb):
    h, d = load(f"{base}_{v}.bin")
    f = d["flags"].astype(np.int64)
    depth = (d["wtop"].astype(np.int64) - d["top"].astype(np.int64))
    water = (f & 1) != 0
    rgb = np.full(depth.shape + (3,), 150, np.uint8)
    t = np.clip(depth / float(os.environ.get('BED_SCALE', '8')), 0, 1)
    rgb[water] = np.stack([60 + 120 * (1 - t), 120 + 110 * (1 - t), 170 + 70 * (1 - t)], -1)[water].astype(np.uint8)
    course = (f & 64) != 0
    rgb[course & ((np.indices(depth.shape).sum(0) % 3) == 0)] = (200, 40, 40)
    imgs.append(rgb)
scale = max(1, 600 // max(imgs[0].shape))
gap = np.full((imgs[0].shape[0], 6, 3), 255, np.uint8)
im = Image.fromarray(np.concatenate([imgs[0], gap, imgs[1]], 1))
im = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
im.save(out)
print(out, im.size)
