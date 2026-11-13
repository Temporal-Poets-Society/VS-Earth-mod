# Reads the slabaudit dumps of river-drops runs: analyze.py <out dir> <label>=<run dir> [<label>=<run dir> ...]
# Per site and run: the river's own surface water (the connected water body at the site), the drops between
# neighbouring river columns (height, what fills the lower column up to the upper level), the banks beside drops of 2+
# blocks, and river water standing next to open air (water walls), near 2+ drops or elsewhere. Writes a top-view PNG
# per site and run, and summary.json.
import gzip, json, os, sys
from collections import Counter, deque
import numpy as np
from PIL import Image

out = sys.argv[1]
runs = [a.split("=", 1) for a in sys.argv[2:]]
here = os.path.dirname(os.path.abspath(__file__))
sites = json.load(open(os.path.join(here, "sites.json")))
# solid blocks water may share a column with or stand beside without being held by them
SOFT = ("tallgrass", "flower", "fern", "reeds", "papyrus", "tallplant", "seaweed", "kelp", "coral", "waterlily", "mushroom",
        "leaves", "smallberrybush", "bigberrybush", "sapling", "aquatic", "watergrass", "brownsedge", "cattail", "tule",
        "horsetail", "lilypad", "loosestones", "looseflints", "looseboulders", "deadcrop", "wildvine", "hangingvine", "snowlayer")


def load(d, name):
    with gzip.open(os.path.join(d, name + ".bin.gz")) as f:
        b = f.read()
    cx0, cz0, n, H, bx, bz = (int(v) for v in np.frombuffer(b[:24], dtype=np.int32))
    N = n * 32
    solid = np.zeros((N, N, H), dtype=np.int32)
    fluid = np.zeros((N, N, H), dtype=np.int32)
    col = 4096 + H * 1024 * 8
    for i in range(n):
        for j in range(n):
            base = 24 + (i * n + j) * col
            arr = np.frombuffer(b[base + 4096: base + col], dtype=np.int32).reshape(H, 2, 32, 32)  # y, layer, lz, lx
            solid[i * 32:(i + 1) * 32, j * 32:(j + 1) * 32] = arr[:, 0].transpose(2, 1, 0)
            fluid[i * 32:(i + 1) * 32, j * 32:(j + 1) * 32] = arr[:, 1].transpose(2, 1, 0)
    return cx0 * 32, cz0 * 32, bx, bz, solid, fluid


def analyze(d, label, s):
    codes = {int(k): v.split(":")[-1] for k, v in json.load(open(os.path.join(d, "blocks.json"))).items()}
    x0, z0, bx, bz, solid, fluid = load(d, s["Name"])
    N, _, H = solid.shape

    def fkind(i):
        c = codes.get(int(i), "") if i > 0 else ""
        p = c.split("-")
        if p[0] not in ("water", "rapidwater"):
            return 0
        if p[1] == "d":
            return 3  # falling
        if p[1] == "still" and p[-1] == "7":
            return 1  # source
        return 2  # flowing

    fk = np.vectorize(fkind, otypes=[np.int8])(fluid)
    rapid_ids = np.array([i for i, c in codes.items() if c.startswith("rapidwater")])
    rapid = np.isin(fluid, rapid_ids)
    soft_ids = np.array([i for i, c in codes.items() if c.startswith(SOFT)])
    hard = (solid > 0) & ~np.isin(solid, soft_ids)
    water = fk > 0
    # surface water: the column's highest water cell lies above its highest hard block
    top_hard = np.where(hard.any(2), H - 1 - np.argmax(hard[:, :, ::-1], 2), -1)
    top_water = np.where(water.any(2), H - 1 - np.argmax(water[:, :, ::-1], 2), -1)
    surf = (top_water > top_hard)
    # still level: the highest source cell of the column
    src = fk == 1
    level = np.where(src.any(2), H - 1 - np.argmax(src[:, :, ::-1], 2), -1)
    # the river: the connected surface water body (4-neighbours, levels within 8 blocks) that holds most columns near the centre
    ci, cj = bx - x0, bz - z0
    comp = np.zeros((N, N), dtype=bool)
    best = None
    for oi in range(-12, 13, 3):
        for oj in range(-12, 13, 3):
            i, j = ci + oi, cj + oj
            if 0 <= i < N and 0 <= j < N and surf[i, j] and level[i, j] >= 0 and not comp[i, j]:
                seen = np.zeros((N, N), dtype=bool)
                q = deque([(i, j)]); seen[i, j] = True
                while q:
                    a, b = q.popleft()
                    for da, db in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        u, v = a + da, b + db
                        if 0 <= u < N and 0 <= v < N and not seen[u, v] and surf[u, v] and level[u, v] >= 0 and abs(int(level[u, v]) - int(level[a, b])) <= 8:
                            seen[u, v] = True; q.append((u, v))
                if best is None or seen.sum() > best.sum():
                    best = seen
                comp |= seen
    river = best if best is not None else np.zeros((N, N), dtype=bool)
    r = {"run": label, "kind": s["Kind"], "centre": [bx, bz], "riverColumns": int(river.sum())}
    # drops between river neighbours
    drops = Counter()
    fill = {}
    big = []
    for di, dj in ((1, 0), (0, 1)):
        for i, j in zip(*np.nonzero(river[:N - di, :N - dj] & river[di:, dj:])):
            a, b = level[i, j], level[i + di, j + dj]
            if a == b:
                continue
            hi, lo = ((i, j), (i + di, j + dj)) if a > b else ((i + di, j + dj), (i, j))
            h = abs(int(a) - int(b))
            drops[h] += 1
            if h >= 2:
                big.append((hi, lo, h))
            # what the lower column holds from just above its still level up to the upper level
            names = []
            for y in range(level[lo] + 1, level[hi] + 1):
                k = int(fk[lo[0], lo[1], y])
                nm = ["air/other", "source", "flowing", "falling"][k] if k else ("air" if solid[lo[0], lo[1], y] == 0 else "solid")
                if k and rapid[lo[0], lo[1], y]:
                    nm = "rapid " + nm
                names.append(nm)
            key = f"{h}: " + " / ".join(sorted(set(names)))
            fill[key] = fill.get(key, 0) + 1
    r["drops"] = {str(k): v for k, v in sorted(drops.items())}
    r["lowerColumnAtDrops"] = dict(sorted(fill.items(), key=lambda kv: (int(kv[0].split(":")[0]), -kv[1])))
    near_big = np.zeros((N, N), dtype=bool)
    for hi, lo, h in big:
        for (a, b) in (hi, lo):
            near_big[max(0, a - 4):a + 5, max(0, b - 4):b + 5] = True
    # banks: dry columns next to river water; their hard top against the highest water beside them
    banks = {"near 2+ drops": Counter(), "elsewhere": Counter()}
    for i in range(1, N - 1):
        for j in range(1, N - 1):
            if river[i, j] or surf[i, j]:
                continue
            nb = [top_water[i + a, j + b] for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1)) if river[i + a, j + b]]
            if not nb:
                continue
            dlt = int(top_hard[i, j]) - int(max(nb))
            banks["near 2+ drops" if near_big[i, j] else "elsewhere"][max(-3, min(dlt, 3))] += 1
    r["bankTopMinusWaterBeside"] = {k: {str(a): b for a, b in sorted(v.items())} for k, v in banks.items()}
    # water walls: river water cells (and the falling water of its drops) with open air beside them
    open_air = (solid == 0) & (fluid == 0)
    walls = {"near 2+ drops": Counter(), "elsewhere": Counter()}
    wall_cols = np.zeros((N, N), dtype=bool)
    zone = river.copy()
    zone[1:] |= river[:-1]; zone[:-1] |= river[1:]; zone[:, 1:] |= river[:, :-1]; zone[:, :-1] |= river[:, 1:]
    for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nb_air = np.zeros_like(open_air)
        sl_dst = (slice(max(0, -di), N - max(0, di)), slice(max(0, -dj), N - max(0, dj)))
        sl_src = (slice(max(0, di), N - max(0, -di)), slice(max(0, dj), N - max(0, -dj)))
        nb_air[sl_dst] = open_air[sl_src]
        m = water & nb_air & zone[:, :, None]
        m[0], m[-1], m[:, 0], m[:, -1] = False, False, False, False
        for (i, j, y) in zip(*np.nonzero(m)):
            nm = ["", "source", "flowing", "falling"][fk[i, j, y]]
            if rapid[i, j, y]:
                nm = "rapid " + nm
            walls["near 2+ drops" if near_big[i, j] else "elsewhere"][nm] += 1
            wall_cols[i, j] = True
    r["waterNextToOpenAir"] = {k: dict(v.most_common()) for k, v in walls.items()}
    # top view
    img = np.zeros((N, N, 3), dtype=np.uint8)
    g = np.clip((top_hard - 90) * 2, 30, 230).astype(np.uint8)
    img[..., 0] = img[..., 1] = img[..., 2] = g
    if river.any():
        lv = level[river]
        t = (level - lv.min()) / max(1, lv.max() - lv.min())
        img[river, 0] = 20
        img[river, 1] = (60 + 140 * t[river]).astype(np.uint8)
        img[river, 2] = (120 + 135 * t[river]).astype(np.uint8)
    img[river & (fk == 3).any(2)] = (255, 255, 255)
    img[river & (rapid & (fk == 2)).any(2) & ~(fk == 3).any(2)] = (0, 220, 120)
    for hi, lo, h in big:
        img[lo] = (230, 30, 30)
    img[wall_cols] = (255, 200, 0)
    Image.fromarray(img.transpose(1, 0, 2)).resize((N * 3, N * 3), Image.NEAREST).save(os.path.join(out, f"drops-{s['Name']}-{label}.png"))
    return r


report = {}
for s in sites:
    for label, d in runs:
        r = analyze(os.path.join(d, "slabaudit"), label, s)
        report[f"{s['Name']} [{label}]"] = r
        print(f"== {s['Name']} [{label}] ({s['Kind']}), {r['riverColumns']} river columns")
        for k in ("drops", "lowerColumnAtDrops", "bankTopMinusWaterBeside", "waterNextToOpenAir"):
            print(f"   {k}: {r[k]}")
json.dump(report, open(os.path.join(out, "summary.json"), "w"), indent=1)
