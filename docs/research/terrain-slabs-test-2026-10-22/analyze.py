# analyze.py <runs dir>: compares slabs vs control1 (and control1 vs control2 = noise) per site; writes summary.json
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
summary = {"world": W["slabs"].w, "sites": {}}


def name(i): return names[i]


def hist(mask, arr, top=12):
    vals, cnt = np.unique(arr[mask], return_counts=True)
    order = np.argsort(-cnt)[:top]
    return {names[int(vals[k])]: int(cnt[k]) for k in order}


for s in sites:
    site = s["Name"]
    data = {k: w.load(site) for k, w in W.items()}
    if any(v is None for v in data.values()):
        summary["sites"][site] = {"missing": [k for k, v in data.items() if v is None]}; continue
    g = {k: (to_global(W[k], d["solid"]), to_global(W[k], d["fluid"])) for k, d in data.items()}
    names = {v: k for k, v in space.items()}
    nn = len(space)
    is_slab = np.zeros(nn, bool); base = np.arange(nn)
    for code, i in list(space.items()):
        b = base_of(code)
        if b: is_slab[i] = True; base[i] = gid(b)
    names = {v: k for k, v in space.items()}
    nn = len(space)
    is_slab = np.concatenate([is_slab, np.zeros(nn - len(is_slab), bool)]); base = np.concatenate([base, np.arange(len(base), nn)])
    flowing = np.array([(":water-" in c or ":saltwater-" in c) and "-still-" not in c for c in (names[i] for i in range(nn))])
    air = gid("game:air")

    Ss, Sf = g["slabs"]; C1s, C1f = g["control1"]; C2s, C2f = g["control2"]
    H = data["slabs"]["H"]
    top = data["control1"]["top"]
    yy = np.arange(H)[:, None, None]
    above_top = yy > top[None]
    r = {"kind": s["Kind"], "centre": data["slabs"]["centre"], "cells": int(Ss.size)}
    # noise between two identical control worlds
    r["noise_solid"] = int((C1s != C2s).sum()); r["noise_fluid"] = int((C1f != C2f).sum())
    # slab replacements: same block, now its slab
    slab = is_slab[Ss]
    same_base = base[Ss] == C1s
    expected = slab & same_base
    r["slabs_total"] = int(slab.sum())
    r["slabs_replacing_same_block"] = int(expected.sum())
    depth = top[None] - yy
    r["slabs_at_surface"] = int((expected & (depth <= 1) & (depth >= -1)).sum())
    r["slabs_below_surface_caves"] = int((expected & (depth > 1)).sum())
    r["slabs_by_base"] = hist(expected, C1s, 15)
    # other solid changes (not noise): slab of a different block, or a non-slab change
    other = (Ss != C1s) & ~expected & (C1s == C2s)
    r["other_solid_changes"] = int(other.sum())
    r["other_solid_from"] = hist(other, C1s); r["other_solid_to"] = hist(other, Ss)
    # fluid changes (not noise)
    fch = (Sf != C1f) & (C1f == C2f)
    r["fluid_changes"] = int(fch.sum())
    r["fluid_from"] = hist(fch, C1f); r["fluid_to"] = hist(fch, Sf)
    # slabs under or in water
    f_above = np.zeros_like(slab); f_above[:-1] = Sf[1:] != air
    r["slabs_with_fluid_above"] = int((slab & f_above).sum())
    r["slabs_with_fluid_above_by_fluid"] = hist(slab[:-1] & f_above[:-1], Sf[1:])
    r["slabs_with_fluid_in_cell"] = int((slab & (Sf != air)).sum())
    # fluid beside a slab at the same height (the slab's empty upper half is open to it)
    side = np.zeros_like(slab)
    for ax, sh in ((1, 1), (1, -1), (2, 1), (2, -1)):
        side |= np.roll(Sf != air, sh, axis=ax)
    side[:, [0, -1], :] = False; side[:, :, [0, -1]] = False
    r["slabs_with_fluid_beside"] = int((slab & side).sum())
    # near flowing water (Earth's falls and ramps): within 2 blocks horizontally and 2 vertically
    fl = flowing[Sf]
    near = fl.copy()
    for ax in (0, 1, 2):
        for sh in (1, 2, -1, -2):
            near |= np.roll(fl, sh, axis=ax)
    r["flowing_water_cells"] = int(fl.sum())
    r["slabs_near_flowing_water"] = int((slab & near).sum())
    r["flowing_water_cells_control"] = int(flowing[C1f].sum())
    # what sits on slabs (offset by Place On Slabs on the client)
    on = np.zeros_like(slab); on[:-1] = slab[:-1]
    r["on_top_of_slabs"] = hist(on[:-1] & (Ss[1:] != air), Ss[1:], 15)
    # exposed edge blocks left as full blocks: open sky/air above, air on one side, top of terrain
    open_above = np.zeros_like(slab); open_above[:-1] = (Ss[1:] == air) & (Sf[1:] == air)
    side_air = np.zeros_like(slab)
    for ax, sh in ((1, 1), (1, -1), (2, 1), (2, -1)):
        side_air |= np.roll((Ss == air) & (Sf == air), sh, axis=ax)
    side_air[:, [0, -1], :] = False; side_air[:, :, [0, -1]] = False
    left = ~slab & (Ss != air) & open_above & side_air & (np.abs(depth) <= 1)
    r["exposed_edges_left_full"] = hist(left, Ss, 15)
    r["exposed_edges_slabbed"] = int((slab & open_above & side_air & (np.abs(depth) <= 1)).sum())
    # bog peat
    bog = gid("earthmap:bogpeat")
    r["bogpeat_control"] = int((C1s == bog).sum()); r["bogpeat_slabs"] = int((Ss == bog).sum())
    r["bogpeat_exposed_edge_full"] = int((left & (Ss == bog)).sum())
    peat = np.array(["peat" in names[i] for i in range(nn)])
    r["peat_slabs"] = int((slab & peat[Ss]).sum())
    summary["sites"][site] = r
    print(site, json.dumps({k: v for k, v in r.items() if not isinstance(v, dict)}))

json.dump(summary, open(os.path.join(runs, "summary.json"), "w"), indent=1)
