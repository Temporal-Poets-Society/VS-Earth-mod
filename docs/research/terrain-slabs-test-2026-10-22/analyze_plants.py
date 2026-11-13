# analyze_plants.py <runs dir>: water-plant counts and species in all three worlds (shows the run-to-run noise between the two controls)
import gzip, json, os, sys, collections
import numpy as np

runs = sys.argv[1]
sites = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "sites.json")))


def base_of(code):
    # terrainslabs:soil-game-low-none -> game:soil-low-none
    if ":" not in code: return None
    dom, path = code.split(":", 1)
    if dom != "terrainslabs": return None
    p = path.split("-")
    return p[1] + ":" + "-".join([p[0]] + p[2:])


class World:
    def __init__(self, name):
        self.dir = os.path.join(runs, name)
        codes = json.load(open(os.path.join(self.dir, "slabaudit", "blocks.json")))
        self.codes = {int(k): v for k, v in codes.items()}
        self.w = json.load(open(os.path.join(self.dir, "slabaudit", "world.json")))

    def load(self, site):
        f = os.path.join(self.dir, "slabaudit", site + ".bin.gz")
        if not os.path.exists(f): return None
        raw = gzip.open(f).read()
        cx0, cz0, n, H, bx, bz = np.frombuffer(raw[:24], "<i4")
        S = n * 32
        solid = np.zeros((H, S, S), np.int32); fluid = np.zeros((H, S, S), np.int32)
        top = np.zeros((S, S), np.int32)
        o = 24
        for i in range(n):
            for j in range(n):
                hm = np.frombuffer(raw[o:o + 2048], "<u2").reshape(32, 32); o += 4096
                top[j * 32:(j + 1) * 32, i * 32:(i + 1) * 32] = hm
                col = np.frombuffer(raw[o:o + H * 2048 * 4], "<i4").reshape(H, 2, 32, 32); o += H * 2048 * 4
                solid[:, j * 32:(j + 1) * 32, i * 32:(i + 1) * 32] = col[:, 0]
                fluid[:, j * 32:(j + 1) * 32, i * 32:(i + 1) * 32] = col[:, 1]
        return dict(solid=solid, fluid=fluid, top=top, H=H, n=n, origin=(int(cx0) * 32, int(cz0) * 32), centre=(int(bx), int(bz)))


# a shared code space across worlds
space = {}
def gid(code):
    if code not in space: space[code] = len(space)
    return space[code]
gid("game:air")

def to_global(world, arr):
    lut = np.zeros(max(world.codes) + 2, np.int32)
    for k, v in world.codes.items(): lut[k] = gid(v)
    lut[-1] = gid("?missing")
    return lut[np.where(arr < 0, len(lut) - 1, arr)]

W = {k: World(k) for k in ("slabs", "control1", "control2")}
for site in ("niagara_falls", "lake_geneva_north", "daugava_riga", "rhine_falls"):
    data = {k: w.load(site) for k, w in W.items()}
    g = {k: to_global(W[k], d["solid"]) for k, d in data.items()}
    names = {v: k for k, v in space.items()}; nn = len(space)
    aq = np.array([":aquatic-" in names[i] or ":aquaticplant-" in names[i] for i in range(nn)])
    tops = np.array([":aquatic-" in names[i] and names[i].endswith("-top") for i in range(nn)])
    res = {}
    for k in g:
        a = aq[g[k]]
        res[k] = [int(a.sum()), int(tops[g[k]].sum()), int(np.unique(np.argwhere(a)[:, 1:], axis=0).shape[0])]
    # species histogram per world
    sp = {}
    for k in g:
        v, c = np.unique(g[k][aq[g[k]]], return_counts=True)
        sp[k] = {names[int(a)].replace("game:", ""): int(b) for a, b in zip(v, c) if b > 50}
    print(site, "cells/tops/columns", res); print("  ", sp)
