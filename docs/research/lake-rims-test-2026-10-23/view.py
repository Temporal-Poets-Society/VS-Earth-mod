import importlib.util, json, os, sys
import numpy as np
from PIL import Image
A = "C:/VS mods/Earth/docs/research/river-drops-test-2026-10-22/analyze.py"
src = open(A, encoding="utf-8").read().split("\ndef analyze")[0]
ns = {"__file__": A}; exec(compile(src.replace("out = sys.argv[1]", "out = '.'"), "an", "exec"), ns)
run, name, out = sys.argv[1], sys.argv[2], sys.argv[3]
d = os.path.join(run, "slabaudit")
codes = {int(k): v.split(":")[-1] for k, v in json.load(open(os.path.join(d, "blocks.json"))).items()}
ids = lambda p: np.array([i for i, c in codes.items() if p(c)] or [-1])
x0, z0, bx, bz, solid, fluid = ns["load"](d, name)
N, _, H = solid.shape
soft = ids(lambda c: c.startswith(ns["SOFT"]) or "leaves" in c)
hard = (solid > 0) & ~np.isin(solid, soft)
water = np.isin(fluid, ids(lambda c: c.startswith(("water-", "rapidwater-"))))
fall = np.isin(fluid, ids(lambda c: "-d-" in c))
rapid = np.isin(fluid, ids(lambda c: c.startswith("rapidwater-")))
topv = lambda m: np.where(m.any(2), H - 1 - np.argmax(m[:, :, ::-1], 2), -1)
th, tw = topv(hard), topv(water)
wet = tw > th
fallc = fall.any(2)
np.savez_compressed(os.path.join(out, name + ".npz"), th=th, tw=tw, fallc=fallc, rapidc=rapid.any(2) & wet, x0=x0, z0=z0)
print("origin", x0, z0, "N", N, "bx bz", bx, bz)
print("water levels", np.unique(tw[wet], return_counts=True))
img = np.zeros((N, N, 3), np.uint8)
lo, hi = np.percentile(th, 2), np.percentile(th, 98)
g = np.clip((th - lo) / max(1, hi - lo) * 200 + 30, 0, 255).astype(np.uint8)
img[...] = g[..., None]
lv = tw[wet]; t = (tw - lv.min()) / max(1, lv.max() - lv.min())
img[wet, 0] = 20; img[wet, 1] = (50 + 150 * t[wet]).astype(np.uint8); img[wet, 2] = (120 + 135 * t[wet]).astype(np.uint8)
img[wet & rapid.any(2)] = (0, 220, 120)
img[fallc] = (255, 255, 255)
# cliff: neighbour ground drop 5+ without water at the bottom
cl = np.zeros((N, N), bool)
for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1)):
    nb = np.roll(np.roll(th, a, 0), b, 1); nbw = np.roll(np.roll(wet, a, 0), b, 1)
    cl |= (nb - th >= 5) & ~wet
img[cl & ~fallc] = (230, 40, 40)
Image.fromarray(img.transpose(1, 0, 2)).resize((N * 3, N * 3), Image.NEAREST).save(os.path.join(out, name + ".png"))
