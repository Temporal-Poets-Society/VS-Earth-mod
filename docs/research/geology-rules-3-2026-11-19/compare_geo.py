# EA-0173: regional rock with vanilla ores, geology rules 2 (main) against rules 3: rocks within 120 blocks and ores
import json, sys
OUT = sys.argv[1]
G = {"iron": ["limonite", "hematite", "magnetite"], "chromite": ["chromite"], "nickel": ["pentlandite"], "copper": ["nativecopper", "malachite", "chalcopyrite"],
     "tin": ["cassiterite"], "lead/zinc": ["galena", "sphalerite", "cerussite", "smithsonite"], "coal": ["lignite", "bituminouscoal", "anthracite"], "halite": ["halite"],
     "olivine": ["olivine"]}
def load(r):
    out = {}
    for line in open(f"{OUT}/{r}/oreaudit/results.jsonl", encoding="utf-8"):
        d = json.loads(line)
        if not d.get("header"): out[d["site"]] = d
    return out
def rocks(d):
    r = d.get("rocks", {}); t = sum(r.values()) or 1
    return ", ".join(f"{k} {100*v/t:.0f}%" for k, v in sorted(r.items(), key=lambda x: -x[1]) if 100 * v / t >= 4)
for sc in ("10", "100"):
    a, b = load(f"main-{sc}"), load(f"geo3-{sc}")
    print(f"\n=== 1:{sc} regional rock, vanilla ores: rules 2 -> rules 3")
    for s in b:
        x, y = a.get(s, {}), b[s]; cols = y["columns"] / 1024
        ores = []
        for g, fs in G.items():
            p = sum(x.get("families", {}).get(f, {}).get("n", 0) for f in fs) / cols; q = sum(y["families"].get(f, {}).get("n", 0) for f in fs) / cols
            if p or q: ores.append(f"{g} {p:.0f}>{q:.0f}")
        print(f"{s:12s} before: {rocks(x)}")
        print(f"{'':12s} after:  {rocks(y)}")
        print(f"{'':12s} ores:   {'; '.join(ores)}")
