"""EA-0160: a window under river ends rules 2 and 1 side by side (sea blue, river water cyan, course black, land by height)."""
import os, sys
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from analyse import load

base, out = sys.argv[1], sys.argv[2]
imgs = []
for v in ("base", "base-ends1"):
    h, d = load(f"{base}_{v}.bin")
    f = d["flags"]
    top = d["top"].astype(float)
    g = np.clip((top - h["y0"]) * 6 + 120, 60, 240).astype(np.uint8)
    rgb = np.stack([g, g, g], -1)
    rgb[(f & 1) > 0] = (120, 200, 230)
    rgb[(f & 16) > 0] = (60, 110, 180)
    rgb[(f & 64) > 0] = (90, 70, 50)
    rgb[(f & 2) > 0] = (0, 220, 220)
    imgs.append(rgb)
    print(v, h["lat0"], h["lat1"], h["lon0"], h["lon1"], "water", int(((f & 1) > 0).sum()), "river", int(((f & 2) > 0).sum()),
          "course", int(((f & 64) > 0).sum()), "sea", int(((f & 16) > 0).sum()), "top range", int(top.min()), int(top.max()))
gap = np.full((imgs[0].shape[0], 8, 3), 255, np.uint8)
Image.fromarray(np.concatenate([imgs[0], gap, imgs[1]], 1)).save(out)
