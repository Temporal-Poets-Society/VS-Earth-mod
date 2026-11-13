# Water depth in a slabaudit run (pool layout 2: chutes, lips, plunge pools): depths.py <run dir> <sites file>
# Per site: depth (still level minus bed) of rapid-water columns (chutes), and around waterfall curtains: the upper water
# within 8 columns of a curtain (the lip) and the lower water within 8 columns (the plunge pool), against the river overall.
import importlib.util, json, os, sys
from collections import Counter
import numpy as np

spec = importlib.util.spec_from_file_location("an", os.path.join(os.path.dirname(os.path.abspath(__file__)), "analyze.py"))
src = open(spec.origin, encoding="utf-8").read().split("\nreport = {}")[0]
ns = {"__file__": spec.origin}
exec(compile(src.replace("out = sys.argv[1]", "out = '.'").replace('runs = [a.split("=", 1) for a in sys.argv[2:]]', "runs = []"), "an", "exec"), ns)
run, sites_file = sys.argv[1], sys.argv[2]
d = os.path.join(run, "slabaudit")
codes = {int(k): v.split(":")[-1] for k, v in json.load(open(os.path.join(d, "blocks.json"))).items()}
ids = lambda pred: np.array([i for i, c in codes.items() if pred(c)])
water_ids = ids(lambda c: c.startswith(("water-", "rapidwater-")))
src_ids = ids(lambda c: c.startswith(("water-still-7", "rapidwater-still-7")))
fall_ids = ids(lambda c: c.startswith(("water-d-", "rapidwater-d-")))
rapid_ids = ids(lambda c: c.startswith("rapidwater-"))
soft_ids = ids(lambda c: c.startswith(ns["SOFT"]) or "leaves" in c)


def hist(v):
    c = Counter(int(x) for x in v)
    return {k: c[k] for k in sorted(c)}


for s in json.load(open(sites_file)):
    x0, z0, bx, bz, solid, fluid = ns["load"](d, s["Name"])
    N, _, H = solid.shape
    hard = (solid > 0) & ~np.isin(solid, soft_ids)
    water = np.isin(fluid, water_ids)
    src = np.isin(fluid, src_ids)
    top_hard = np.where(hard.any(2), H - 1 - np.argmax(hard[:, :, ::-1], 2), -1)
    top_water = np.where(water.any(2), H - 1 - np.argmax(water[:, :, ::-1], 2), -1)
    level = np.where(src.any(2), H - 1 - np.argmax(src[:, :, ::-1], 2), -1)
    surf = (top_water > top_hard) & (level >= 0) & (level >= top_hard)
    depth = np.where(surf, level - top_hard, -1)
    fall = np.isin(fluid, fall_ids).any(2) & surf
    rapid = np.isin(fluid, rapid_ids).any(2) & surf & ~fall
    rocks = (top_hard > level) & np.isin(fluid, water_ids).any(2) == False
    # underwater walls: how far the bed jumps between neighbouring river columns at the same water level
    jumps = Counter()
    for di, dj in ((1, 0), (0, 1)):
        a = surf[:N - di, :N - dj] & surf[di:, dj:] & (level[:N - di, :N - dj] == level[di:, dj:])
        dj_ = np.abs(top_hard[:N - di, :N - dj] - top_hard[di:, dj:])[a]
        jumps.update(np.minimum(dj_, 8).tolist())
    print(f"== {s['Name']} ({s['Kind']})")
    print(f"   bed jumps between neighbouring river columns (8 = 8+): {dict(sorted(jumps.items()))}; 4+ blocks: {sum(v for k, v in jumps.items() if k >= 4)}")
    print(f"   all river water, depth: {hist(depth[surf])}")
    print(f"   rapid-water columns (chutes), depth: {hist(depth[rapid])}")
    if fall.any():
        fi, fj = np.nonzero(fall)
        top_of_fall = top_water[fall].max()
        up, low = [], []
        for i in range(N):
            for j in range(N):
                if not surf[i, j] or fall[i, j]:
                    continue
                dmin = np.min(np.maximum(np.abs(fi - i), np.abs(fj - j)))
                if dmin > 8:
                    continue
                (up if level[i, j] >= top_water[fall].min() + 2 and level[i, j] > np.median(level[fall]) else low).append(depth[i, j])
        print(f"   {int(fall.sum())} curtain columns; upper water within 8 (lip), depth: {hist(up)}")
        print(f"   lower water within 8 (plunge pool), depth: {hist(low)}")
