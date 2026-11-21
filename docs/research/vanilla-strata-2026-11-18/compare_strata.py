# EA-0181: rock variety within 120 blocks of the ground and ore per chunk column, deposit depth rules only (rock-*) against
# deposit depth rules + vanilla strata rules (strata-*)
import csv, json, sys
old_csv, new_dir = sys.argv[1], sys.argv[2]
GROUPS = {"iron": ["limonite", "hematite", "magnetite"], "coal": ["lignite", "bituminouscoal", "anthracite"],
          "copper": ["nativecopper", "malachite"], "tin": ["cassiterite"], "lead/zinc": ["galena", "sphalerite"],
          "sulfur": ["sulfur"], "gold+silver": ["quartz_nativegold", "quartz_nativesilver"], "halite": ["halite"]}
old = {}
for r in csv.DictReader(open(old_csv, encoding="utf-8")):
    if not r["run"].startswith("rock-"): continue
    d = old.setdefault((r["run"][5:], r["site"]), {"rocks": r["rocksWithin120"], "fam": {}})
    d["fam"][r["family"]] = float(r["perChunkColumn"])
def rocks_text(rs):
    tot = sum(rs.values()) or 1
    return ", ".join(f"{k} {100*v/tot:.0f}%" for k, v in sorted(rs.items(), key=lambda x: -x[1]) if 100 * v / tot >= 3)
def parse_old(s):
    out = {}
    for part in filter(None, s.split("; ")):
        k, v = part.rsplit(" ", 1); out[k] = int(v)
    return out
for sc in ("10", "100"):
    print(f"\n=== 1:{sc}: rock types >= 3% within 120 blocks, depth rules only -> + vanilla strata rules; ore per chunk column")
    for line in open(f"{new_dir}/strata-{sc}/oreaudit/results.jsonl", encoding="utf-8"):
        d = json.loads(line)
        if d.get("header"): continue
        o = old.get((sc, d["site"]), {"rocks": "", "fam": {}})
        ro, rn = parse_old(o["rocks"]), d.get("rocks", {})
        cols = d["columns"] / 1024
        ores = []
        for g, fs in GROUPS.items():
            a = sum(o["fam"].get(f, 0) for f in fs); b = sum(d["families"].get(f, {}).get("n", 0) for f in fs) / cols
            if a or b: ores.append(f"{g} {a:.0f}>{b:.0f}")
        print(f"{d['site']:13s} ground {d['minTop']}-{d['maxTop']}")
        print(f"   before: {rocks_text(ro)}")
        print(f"   after:  {rocks_text(rn)}")
        print(f"   ores:   {'; '.join(ores)}")
