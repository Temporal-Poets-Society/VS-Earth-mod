"""Expected ore blocks per try of vanilla's disc deposit generator (DiscDepositGenerator, 1.22.7), Monte Carlo.
Prints Earth's deposits beside their vanilla counterparts and the tries that give vanilla-like ore per chunk.
  python -I tools/earth_deposit_rates.py . [--write]   (from the Earth checkout)"""
import json, math, random, sys

rng = random.Random(7)


def nat(dist, avg, var):
    r = rng.random
    if dist == "uniform": return avg + (r() - .5) * 2 * var
    if dist == "gaussian": return avg + ((r() + r() + r()) / 3 - .5) * 2 * var
    if dist == "triangle": return avg + ((r() + r()) / 2 - .5) * 2 * var
    if dist == "invexp": return avg + r() * r() * var
    if dist == "stronginvexp": return avg + r() * r() * r() * var
    raise ValueError(dist)


def per_try(rad, th, n=40000):
    tot = 0.0
    for _ in range(n):
        R = min(64, int(nat(*rad)))
        if R <= 0: continue
        k = max(-.25, min(.25, rng.random() - .5))
        rx, rz = R - int(R * k), R + int(R * k)
        t = nat(*th); dt = int(t) + (1 if rng.random() < t - int(t) else 0)
        if dt <= 0: continue
        for dx in range(-rx, rx):
            for dz in range(-rz, rz):
                d = 1 - (dx * dx / (rx * rx) + dz * dz / (rz * rz))
                if d < 0: continue
                tot += dt * max(0.0, min(1.0, d * 2 - .2))
    return tot / n


VANILLA = {  # survival/worldgen/deposits, 1.22.7
    "malachite": (10, ("uniform", 7, 3), ("stronginvexp", 1, 2)),
    "galena": (40, ("gaussian", 4, 4), ("uniform", 1, 0)),
    "sphalerite": (12.5, ("gaussian", 4, 3), ("uniform", 1, 0)),
    "chromite": (3, ("invexp", 5, 10), ("uniform", 1, 0)),
    "sulfur": (15, ("gaussian", 8, 2), ("uniform", 1, 0)),
    "emerald": (64, ("gaussian", 1, .5), ("uniform", 1, 0)),
}
# Counterparts added later. They and their Earth deposits are sampled after all the others, so the earlier rows keep
# their numbers (one random stream).
LATE = {
    "cassiterite": (7, ("gaussian", 5, 3), ("uniform", 1, 0)),  # the common deposit, not the rare massive one
}
# Earth deposit -> (vanilla counterpart, family). Ore families: the family's most common member gets the counterpart's
# ore per chunk, the rest keep their authored rarity against it. Gem families are one rough gem item: their colour
# deposits share the counterpart's ore per chunk.
ORES = {
    "chalcopyrite": ("malachite", "copper"), "azurite": ("malachite", "copper"), "tetrahedrite": ("malachite", "copper"),
    "cerussite": ("galena", "lead"), "vanadinite": ("galena", "lead"), "wulfenite": ("galena", "lead"),
    "smithsonite": ("sphalerite", "zinc"), "hemimorphite": ("sphalerite", "zinc"),
    "sperrylite": ("chromite", "platinum"), "pyrite": ("sulfur", "pyrite"),
    "wolframite": ("cassiterite", "tungsten"),
}
# A family's ore per chunk as a share of its counterpart's: wolframite, the tin miners' companion mineral, at about a
# quarter of tin (world mine output about 80 kt W against 300 kt Sn a year; USGS Mineral Commodity Summaries).
SHARE = {"tungsten": .25}
GEMS = {"topaz": "emerald", "tourmaline": "emerald", "beryl": "emerald", "spinel": "emerald", "garnet": "emerald",
        "corundumruby": "emerald", "corundumsapphire": "emerald"}

root = sys.argv[1]
vrate = {k: t * per_try(r, th) for k, (t, r, th) in VANILLA.items()}
rows = []
for late in (False, True):
    if late: vrate.update({k: t * per_try(r, th) for k, (t, r, th) in LATE.items()})
    for name in ("earth-ores", "earth-gems"):
        path = f"{root}/assets/earth/worldgen/deposits/{name}.json"
        deps = json.load(open(path, encoding="utf-8"))
        for d in deps:
            code = d["code"]
            if name == "earth-ores":
                ref, fam = ORES[code]; gem = False
            else:
                fam = next(g for g in GEMS if code.startswith(g)); ref = GEMS[fam]; gem = True
            if (ref in LATE) != late: continue
            a = d["attributes"]
            pt = per_try((a["radius"]["dist"], a["radius"]["avg"], a["radius"]["var"]),
                         (a["thickness"]["dist"], a["thickness"]["avg"], a["thickness"]["var"]))
            rows.append(dict(file=name, dep=d, code=code, fam=fam, ref=ref, gem=gem, pt=pt, cur=d["triesPerChunk"] * pt))
for r in rows:
    fam = [x for x in rows if x["fam"] == r["fam"]]
    share = r["cur"] / sum(x["cur"] for x in fam) if r["gem"] else r["cur"] / max(x["cur"] for x in fam)
    r["new"] = round(vrate[r["ref"]] * share * SHARE.get(r["fam"], 1) / r["pt"], 2)
print(f"{'deposit':22s} {'per try':>8s} {'tries now':>9s} {'new':>7s}  x    counterpart (ore blocks/chunk before ore map)")
for r in rows:
    print(f"{r['code']:22s} {r['pt']:8.1f} {r['dep']['triesPerChunk']:9.2f} {r['new']:7.2f} {r['new'] / r['dep']['triesPerChunk']:5.0f}  "
          f"{r['ref']} {vrate[r['ref']]:.0f}; Earth now {r['cur']:.0f}, new {r['new'] * r['pt']:.0f}")
if "--write" in sys.argv:
    for name in ("earth-ores", "earth-gems"):
        path = f"{root}/assets/earth/worldgen/deposits/{name}.json"
        deps = json.load(open(path, encoding="utf-8"))
        new = {r["code"]: r["new"] for r in rows if r["file"] == name}
        txt = open(path, encoding="utf-8", newline="").read()
        for d in deps:
            old = f'"code": "{d["code"]}",\n  "triesPerChunk": {json.dumps(d["triesPerChunk"])},'
            assert txt.count(old) == 1, old
            txt = txt.replace(old, f'"code": "{d["code"]}",\n  "triesPerChunk": {new[d["code"]]},')
        open(path, "w", encoding="utf-8", newline="").write(txt)
        print("written", path)
