# analyze_details.py <runs dir>: second pass over the same dumps (water plants out of water, fluid changes near falls, flowing water beside slabs, snow on slabs, terrain edges left full); writes summary-details.json
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
out = {}
TERR = ("soil-", "rock-", "sand-", "sandwavy-", "gravel-", "muddygravel", "forestfloor-", "peat-", "rawclay-", "crackedrock-", "snowblock", "glacierice", "packedglacierice", "devastatedsoil", "cob-", "bogpeat", "clay")
for s in sites:
    site = s["Name"]
    data = {k: w.load(site) for k, w in W.items()}
    g = {k: (to_global(W[k], d["solid"]), to_global(W[k], d["fluid"])) for k, d in data.items()}
    for c in list(space): b = base_of(c); b and gid(b)
    names = {v: k for k, v in space.items()}; nn = len(space)
    is_slab = np.array([base_of(names[i]) is not None for i in range(nn)])
    aquatic = np.array([any(t in names[i] for t in (":aquatic-", ":aquaticplant-", ":seaweed", ":coral")) for i in range(nn)])
    terr = np.array([names[i].startswith("game:") and names[i][5:].startswith(TERR) or names[i] == "earthmap:bogpeat" for i in range(nn)])
    snowl = np.array(["snowlayer" in names[i] for i in range(nn)])
    falling = np.array([":water-d-" in names[i] or ":rapidwater-d-" in names[i] for i in range(nn)])
    flowing = np.array([(":water-" in c or ":saltwater-" in c or ":rapidwater-" in c) and "-still-" not in c for c in (names[i] for i in range(nn))])
    air = gid("game:air")
    Ss, Sf = g["slabs"]; C1s, C1f = g["control1"]; C2s, C2f = g["control2"]
    slab = is_slab[Ss]
    r = {}
    # aquatic plants now without water (moved up out of the water by the beach step)
    dry_aq_s = aquatic[Ss] & (Sf == air); dry_aq_c = aquatic[C1s] & (C1f == air)
    r["aquatic_plants_out_of_water"] = [int(dry_aq_c.sum()), int(dry_aq_s.sum())]
    r["aquatic_plants_total"] = [int(aquatic[C1s].sum()), int(aquatic[Ss].sum())]
    # fluid changes vs. nearest Earth fall cell (water-d-*) in the control
    fch = (Sf != C1f) & (C1f == C2f)
    pts = np.argwhere(fch)
    fall_pts = np.argwhere(falling[C1f])
    if len(pts) and len(fall_pts):
        d = np.abs(pts[:, None, :] - fall_pts[None, :, :]).max(axis=2).min(axis=1)
        r["fluid_change_cheb_dist_to_fall"] = np.bincount(np.minimum(d, 10)).tolist()
    r["fluid_changes"] = int(len(pts))
    # slab next to a new slab? fluid changes within 2 of a changed solid
    sch = (Ss != C1s) & (C1s == C2s)
    near = sch.copy()
    for ax in (0, 1, 2):
        for sh in (1, -1, 2, -2): near |= np.roll(sch, sh, axis=ax)
    r["fluid_changes_near_solid_change"] = int((fch & near).sum())
    r["fall_cells"] = [int(falling[C1f].sum()), int(falling[Sf].sum())]
    # flowing water at same height beside a slab (the slab's empty upper half is open to it)
    fl = flowing[Sf]; side = np.zeros_like(fl)
    for ax, sh in ((1, 1), (1, -1), (2, 1), (2, -1)): side |= np.roll(fl, sh, axis=ax)
    r["slabs_beside_flowing_water"] = int((slab & side).sum())
    r["slabs_under_flowing_water"] = int((slab[:-1] & fl[1:]).sum())
    # snow layers on slabs
    r["snowlayer_on_slab"] = int((slab[:-1] & snowl[Ss[1:]]).sum())
    r["snowlayer_on_full"] = int((~slab[:-1] & snowl[Ss[1:]]).sum())
    # terrain edge blocks left full: terrain block, open air above, air beside, at the surface
    top = data["control1"]["top"]; depth = top[None] - np.arange(data["slabs"]["H"])[:, None, None]
    openab = np.zeros_like(slab); openab[:-1] = (Ss[1:] == air) & (Sf[1:] == air)
    sair = np.zeros_like(slab)
    for ax, sh in ((1, 1), (1, -1), (2, 1), (2, -1)): sair |= np.roll((Ss == air) & (Sf == air), sh, axis=ax)
    sair[:, [0, -1], :] = False; sair[:, :, [0, -1]] = False
    edge = openab & sair & (np.abs(depth) <= 1)
    left = edge & terr[Ss] & ~slab
    vals, cnt = np.unique(Ss[left], return_counts=True)
    r["terrain_edges_left_full"] = {names[int(v)]: int(c) for v, c in sorted(zip(vals, cnt), key=lambda p: -p[1])[:10]}
    r["terrain_edges_slabbed"] = int((edge & slab).sum())
    out[site] = r
    print(site, json.dumps(r))
json.dump(out, open(os.path.join(runs, "summary-details.json"), "w"), indent=1)
