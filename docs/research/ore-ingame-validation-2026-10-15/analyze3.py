"""In-game ore validation (merged runs; comma-separated result files): Earth rules v2 (E) vs vanilla ores on the same Earth rocks (B) vs pure vanilla (V).
analyze2.py <E.jsonl> <B.jsonl> <V.jsonl> <out.json>
Volume ratios use pooled volume-per-attempt of the baseline times the baseline's mean attempts, which removes
vanilla's sparse ore-map noise (most chunks have factor 0) while keeping host-rock effects."""
import json, sys, math
from collections import defaultdict

def load(ps):
    head, sites = None, {}
    for p in ps.split(","):
        rows = [json.loads(l) for l in open(p, encoding="utf-8")]
        head = head or rows[0]
        for r in rows[1:]: sites[r["site"]] = r
    return head, sites

eh, E = load(sys.argv[1]); bh, B = load(sys.argv[2]); vh, V = load(sys.argv[3])
hs = eh["world"]["y"] / 256
base_mean, res_of, cls_of = {}, {}, {}
for b in eh["bindings"]:
    k = (b["code"], b["tries"])
    base_mean.setdefault(k, b["baseMean"]); res_of.setdefault(b["code"], b["resource"]); cls_of.setdefault(k, b["cls"])

def fam(v):
    f = v["family"]
    if f is None and v["placeblock"]:
        p = v["placeblock"].split(":")[-1]
        f = "saltpeter" if p.startswith("saltpeter") else "bauxite" if p == "rock-bauxite" else None
    return f

def tops(h): return [(f"{i}:{v['code']}", v) for i, v in enumerate([v for v in h["variants"] if v["parent"] is None])]
fam_res, fam_cls = {}, {}
for _, v in tops(eh):
    f = fam(v)
    if f and v["code"] in res_of:
        fam_res.setdefault(f, res_of[v["code"]]); fam_cls.setdefault(f, set()).add(cls_of.get((v["code"], v["tries"])))
for v in eh["variants"]:
    if v["parent"] and fam(v) and v["code"] in res_of: fam_res.setdefault(fam(v), res_of[v["code"]]); fam_cls.setdefault(fam(v), set()).add("Child")

def chunks(s): return (2 * s["r"] + 1) ** 2
def attempts(s, h, f, vanilla_world):
    """(actual attempts, attempts at vanilla's mean factor) summed over the site's chunks for the family's top-level variants."""
    A = Am = 0.0
    for key, v in tops(h):
        if fam(v) != f or key not in s["factors"]: continue
        sumA, _, n = s["factors"][key]
        A += sumA
        if vanilla_world: bm = base_mean.get((v["code"], v["tries"]), 0.0705) if v["withOreMap"] else 1.0
        else: bm = base_mean.get((v["code"], v["tries"]), 1.0)
        Am += v["tries"] * bm * (hs if v["scaleWithHeight"] else 1) * n
    return A, Am
def n(s, f): return s["families"].get(f, {}).get("n", 0)
def heat(s, res):
    e = s.get("evidence", {}).get(res)
    return max(e["obs"], e["pot"], e["deep"]) if e else 0.0
land = lambda s: s["landCols"] / max(1, s["columns"])

fams = sorted(f for f in fam_res)
vland = [s for s in V.values() if land(s) >= 0.5]

def vanilla_ref(f):
    nn = sum(n(s, f) for s in vland); ch = sum(chunks(s) for s in vland)
    A = Am = 0.0
    for s in vland:
        a, am = attempts(s, vh, f, True); A += a; Am += am
    est = nn / A * Am / ch if A > 0 else nn / ch
    per = [n(s, f) / chunks(s) for s in vland]; m = sum(per) / len(per)
    se = math.sqrt(sum((x - m) ** 2 for x in per) / (len(per) - 1) / len(per))
    return dict(perChunk=est, raw=nn / ch, rawSE=se, blocks=nn, depth=sum(s["families"].get(f, {}).get("meanDepth", 0) * n(s, f) for s in vland) / nn if nn else None)

def group(names, f):
    """Pooled Earth volume vs (a) vanilla ores on the same rocks and (b) pure vanilla land."""
    ch = sum(chunks(E[x]) for x in names)
    nE = sum(n(E[x], f) for x in names)
    AE = AEm = nB = AB = ABm = 0.0
    for x in names:
        a, am = attempts(E[x], eh, f, False); AE += a; AEm += am
        if x in B:
            nB += n(B[x], f); a, am = attempts(B[x], bh, f, True); AB += a; ABm += am
    sameRocks = (nE / ch) / (nB / AB * ABm / ch) if AB > 0 and nB > 0 else None
    vr = VREF[f]["perChunk"]
    return dict(sites=len(names), chunks=ch, blocksE=nE, blocksB=nB, attemptsB=AB,
                earthAttempts=AE / AEm if AEm else None, xSameRocks=sameRocks,
                xVanilla=(nE / ch) / vr if vr > 0 else None,
                volPerAttempt=(nE / AE) / (nB / AB) if AE > 0 and AB > 0 and nB > 0 else None)

VREF = {f: vanilla_ref(f) for f in fams}
named = [x for x, s in E.items() if not s["kind"].startswith("random")]
randland = [x for x, s in E.items() if s["kind"] == "random-land" and land(s) >= 0.5]
out = dict(vanilla=VREF, famResource=fam_res, famClass={k: sorted(c for c in v if c) for k, v in fam_cls.items()},
           counts=dict(E=len(E), B=len(B), V=len(V), vanillaLand=len(vland), randomLand=len(randland)), groups={}, named={}, checks={})
for f in fams:
    res = fam_res[f]
    floor = [x for x in randland + [y for y in named if E[y]["kind"].startswith("background")] if heat(E[x], res) == 0]
    hot = [x for x in randland + named if heat(E[x], res) >= 0.8 and land(E[x]) >= 0.5]
    out["groups"][f] = dict(resource=res, landAverage=group(randland, f), noEvidence=group(floor, f) if floor else None,
                            hotspot=group(hot, f) if hot else None, hotSites=hot)
for x in named:
    s = E[x]
    out["named"][x] = dict(kind=s["kind"], lat=s["lat"], lon=s["lon"], land=land(s), evidence={k: round(max(v["obs"], v["pot"], v["deep"]), 2) for k, v in s["evidence"].items()},
                           fam={f: group([x], f) for f in fams if n(s, f) or (x in B and n(B[x], f))})
# placement checks and prospecting
above = defaultdict(int); infl = defaultdict(int); sea_blocks = defaultdict(int)
pp = []
for x, s in E.items():
    for f, d in s["families"].items():
        if f.startswith("loose:"): continue
        above[f] += d["above"]; infl[f] += d["inFluid"]; sea_blocks[f] += d["sea"]
    code_fam = {"lapis": "lapislazuli", "gold": "quartz_nativegold", "peridot": "olivine_peridot", "earth-bauxite": "bauxite"}
    rd = defaultdict(list)
    for p in s["propick"]:
        for code, (ppt, tf) in p["readings"].items(): rd[code].append(ppt)
    for code, vals in rd.items():
        nn = n(s, "galena_nativesilver") + n(s, "quartz_nativesilver") if code == "silver" else n(s, code_fam.get(code, code))
        pp.append(dict(site=x, code=code, reading=sum(vals) / len(vals), measured=nn / s["sumTop"] * 1000, blocks=nn))
out["checks"] = dict(aboveSurface=dict(above), inFluid=dict(infl), underSeaBlocks=dict(sea_blocks), propick=pp)
RES_FAMS = {"Sn": ["cassiterite"], "Cu": ["nativecopper", "malachite"], "Fe": ["limonite", "hematite", "magnetite"], "C_coal": ["lignite", "bituminouscoal", "anthracite"],
    "diamond": ["diamond"], "Au": ["quartz_nativegold"], "NaCl": ["halite"], "K": ["sylvite"], "Pb": ["galena"], "Ni": ["pentlandite"], "Cr": ["chromite"], "lapis": ["lapislazuli"],
    "S": ["sulfur"], "P": ["phosphorite"], "Hg": ["cinnabar"], "B": ["borax", "kernite"], "emerald": ["emerald"]}
def resource_group(names, res):
    fs = RES_FAMS[res]; ch = sum(chunks(E[x]) for x in names)
    nE = sum(n(E[x], f) for x in names for f in fs)
    expB = 0.0; okB = True; nBt = 0
    for f in fs:
        nB = sum(n(B[x], f) for x in names if x in B); AB = ABm = 0.0
        for x in names:
            if x in B: a, am = attempts(B[x], bh, f, True); AB += a; ABm += am
        nBt += nB
        if AB > 0 and nB > 0: expB += nB / AB * ABm
        elif ABm > 0 and nB == 0 and AB > 0: pass
        else: okB = False
    vr = sum(VREF[f]["perChunk"] for f in fs)
    AE = AEm = 0.0
    for x in names:
        for f in fs: a, am = attempts(E[x], eh, f, False); AE += a; AEm += am
    return dict(sites=len(names), chunks=ch, blocksE=nE, blocksB=nBt, perChunkE=nE / ch, vanillaPerChunk=vr, xVanilla=(nE / ch) / vr if vr else None,
                xSameRocks=(nE / ch) / (expB / ch) if expB > 0 else None, earthAttempts=AE / AEm if AEm else None,
                meanHeat=sum(heat(E[x], res) for x in names) / len(names))
hot = defaultdict(list)
for x, s in E.items():
    if "#" in x: hot[(x.split("#")[0], s["kind"].split(":")[1])].append(x)
out["hotspots"] = {name: dict(resource=res, total=resource_group(xs, res), fam={f: group(xs, f) for f in RES_FAMS[res]}) for (name, res), xs in hot.items()}
floor_res = {}
for res in RES_FAMS:
    fl = [x for x in randland + [y for y in named if E[y]["kind"].startswith("background")] if heat(E[x], res) == 0]
    floor_res[res] = dict(noEvidence=resource_group(fl, res) if fl else None, landAverage=resource_group(randland, res))
out["resources"] = floor_res
json.dump(out, open(sys.argv[4], "w"), indent=1)
print(json.dumps(out["counts"]))
