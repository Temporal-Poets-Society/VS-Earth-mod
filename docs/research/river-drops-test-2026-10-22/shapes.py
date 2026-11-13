# Pool layout 5 (decision 0024 step 3) in a slabaudit run: shapes.py <run dir> <sites file>
# Per site: the water blocks at drops (edge sources, curtains, spills by block code) and the sills under drops
# (an upper pool column beside a lower pool: its bed should be at least at the lower pool's surface; at a 1-block step it then
# holds water only in its top block).
import importlib.util, json, os, sys
from collections import Counter
import numpy as np

A = os.path.join(os.path.dirname(os.path.abspath(__file__)), "analyze.py")
src = open(A, encoding="utf-8").read().split("\ndef analyze")[0]
ns = {"__file__": A}
exec(compile(src.replace("out = sys.argv[1]", "out = '.'"), "an", "exec"), ns)
run, sites_file = sys.argv[1], sys.argv[2]
d = os.path.join(run, "slabaudit")
codes = {int(k): v.split(":")[-1] for k, v in json.load(open(os.path.join(d, "blocks.json"))).items()}


def kind(code):
    # water-still-7 -> still7, water-n-7 -> dir7, rapidwater-d-6 -> rapid d6, ...
    parts = code.split("-")
    base = "rapid " if parts[0] == "rapidwater" else ""
    flow, h = parts[1], parts[2]
    f = "still" if flow == "still" else "d" if flow == "d" else "dir"
    return base + f + h


for s in json.load(open(sites_file)):
    x0, z0, bx, bz, solid, fluid = ns["load"](d, s["Name"])
    N, _, H = solid.shape
    soft = np.array([i for i, c in codes.items() if c.startswith(ns["SOFT"]) or "leaves" in c] or [-1])
    hard = (solid > 0) & ~np.isin(solid, soft)
    wids = np.array([i for i, c in codes.items() if c.startswith(("water-", "rapidwater-"))])
    water = np.isin(fluid, wids)
    srcs = np.isin(fluid, np.array([i for i, c in codes.items() if c.startswith(("water-", "rapidwater-")) and c.endswith("-7")]))
    top_hard = np.where(hard.any(2), H - 1 - np.argmax(hard[:, :, ::-1], 2), -1)
    level = np.where(srcs.any(2), H - 1 - np.argmax(srcs[:, :, ::-1], 2), -1)
    pool = (level >= 0) & (level > top_hard)
    # the block kinds of all water above the pools' beds
    kinds = Counter()
    for i, j, y in zip(*np.nonzero(water)):
        c = codes.get(int(fluid[i, j, y]), "")
        k = kind(c)
        if k not in ("still7",) and not k.startswith("dir") or k == "dir7":
            kinds[k] += 1
    # sills: an upper pool column beside a lower pool
    sill_ok = sill_bad = 0
    bad_at = []
    for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        for i in range(1, N - 1):
            for j in range(1, N - 1):
                a, b = (i, j), (i + di, j + dj)
                if not (pool[a] and pool[b]) or level[a] <= level[b]:
                    continue
                if top_hard[a] >= level[b]:
                    sill_ok += 1
                else:
                    sill_bad += 1
                    if len(bad_at) < 5: bad_at.append((x0 + i, z0 + j, int(level[a]), int(top_hard[a])))
    # ramps: a flowing (not falling) cell stays as vanilla leaves it only beside a neighbour of its liquid exactly one higher
    # (a source counts as 7), or with water above it; else its first update rebuilds it ("stretched" ramps)
    hgt = np.zeros(max(codes) + 1, dtype=np.int8)
    flowing = np.zeros(max(codes) + 1, dtype=bool)
    for i, c in codes.items():
        if c.startswith(("water-", "rapidwater-")):
            parts = c.split("-")
            hgt[i] = int(parts[2])
            flowing[i] = parts[1] not in ("d", "still") and parts[2] != "7"
    fh = hgt[fluid]
    fl = flowing[fluid]
    ok_ramp = bad_ramp = 0
    bad_ramp_at = []
    for i, j, y in zip(*np.nonzero(fl)):
        if i in (0, N - 1) or j in (0, N - 1) or y >= H - 1:
            continue
        if water[i, j, y + 1] or any(fh[a, b, y] == fh[i, j, y] + 1 for a, b in ((i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1))):
            ok_ramp += 1
        else:
            bad_ramp += 1
            if len(bad_ramp_at) < 4: bad_ramp_at.append((x0 + i, int(y), z0 + j, codes.get(int(fluid[i, j, y]))))
    # water next to open air, by block kind (walls)
    air = (solid == 0) & (fluid == 0)
    walls = Counter()
    for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nb = np.zeros_like(air)
        sd = (slice(max(0, -di), N - max(0, di)), slice(max(0, -dj), N - max(0, dj)))
        ss = (slice(max(0, di), N - max(0, -di)), slice(max(0, dj), N - max(0, -dj)))
        nb[sd] = air[ss]
        m = water & nb
        m[0], m[-1], m[:, 0], m[:, -1] = False, False, False, False
        for i, j, y in zip(*np.nonzero(m)):
            walls[kind(codes.get(int(fluid[i, j, y]), ""))] += 1
    print(f"== {s['Name']}")
    print(f"   drop blocks: {dict(sorted(kinds.items()))}")
    print(f"   drops (upper pool beside a lower one): sill {sill_ok}, no sill {sill_bad} {bad_at}")
    print(f"   flowing cells beside one a height higher (or under water): {ok_ramp}, not: {bad_ramp} {bad_ramp_at}")
    print(f"   water beside open air: {dict(walls.most_common())}")
