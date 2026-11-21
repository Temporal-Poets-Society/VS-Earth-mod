# EA-0170 census: per site and ore group, blocks per chunk column and mean depth, base (2.1.4) against new (rules 1)
import json, sys, os
OUT = sys.argv[1]; scales = sys.argv[2:] or ["10", "100"]
GROUPS = {
  "iron": ["limonite", "hematite", "magnetite"], "coal": ["lignite", "bituminouscoal", "anthracite"],
  "copper": ["nativecopper", "malachite"], "tin": ["cassiterite"], "lead/zinc": ["galena", "sphalerite"],
  "sulfur": ["sulfur"], "borax": ["borax"], "saltpeter": ["saltpeter"],
  "gold+silver": ["quartz_nativegold", "quartz_nativesilver", "galena_nativesilver"], "quartz": ["quartz"],
  "halite": ["halite"], "chromite": ["chromite"],
}
def load(path):
    sites = {}
    if not os.path.exists(path): return sites
    for line in open(path, encoding="utf-8"):
        d = json.loads(line)
        if d.get("header"): continue
        sites[d["site"]] = d
    return sites
for sc in scales:
    base, new = load(f"{OUT}/base-{sc}/oreaudit/results.jsonl"), load(f"{OUT}/" + (__import__("os").environ.get("NEW","new")) + f"-{sc}/oreaudit/results.jsonl")
    print(f"\n=== 1:{sc}  blocks per chunk column (mean depth below ground) base -> new")
    names = [s for s in new if s in base] or list(new) or list(base)
    print("site".ljust(13) + "ground".rjust(9) + "".join(g.rjust(22) for g in GROUPS))
    for s in names:
        b, n = base.get(s, {}), new.get(s, {})
        cols = (n or b).get("columns", 25600) / 1024
        ground = f"{(n or b).get('minTop')}-{(n or b).get('maxTop')}"
        row = s.ljust(13) + ground.rjust(9)
        for g, fams in GROUPS.items():
            def tot(d):
                fs = d.get("families", {}); cnt = sum(fs.get(f, {}).get("n", 0) for f in fams)
                dep = sum(fs.get(f, {}).get("n", 0) * fs.get(f, {}).get("meanDepth", 0) for f in fams if fs.get(f, {}).get("n", 0) > 0)
                return cnt / cols, (dep / cnt if cnt else float("nan"))
            (bc, bd), (nc, nd) = tot(b), tot(n)
            fmt = lambda c, d: f"{c:.0f}" + (f"({d:.0f})" if c > 0 else "")
            row += (fmt(bc, bd) + ">" + fmt(nc, nd)).rjust(22)
        print(row)
