# readings.py <run>=<results.jsonl> ...: prospecting readings (ppt, summed over the site's five probe points) per site and ore
import json, sys
runs = {}
for a in sys.argv[1:]:
    name, path = a.split("=", 1)
    rows = {}
    for line in open(path, encoding="utf-8"):
        r = json.loads(line)
        if r.get("header"): continue
        rows[r["site"]] = r
    runs[name] = rows
names = list(runs)
sites = list(next(iter(runs.values())).keys())
for s in sites:
    ores = set()
    tot = {}
    for n in names:
        r = runs[n].get(s)
        if not r: continue
        t = {}
        for p in r["propick"]:
            for k, v in p["readings"].items(): t[k] = t.get(k, 0) + v[0]
        tot[n] = t; ores |= set(t)
    print(f"== {s}  (ppt summed over 5 probe points)  " + " / ".join(names))
    for k in sorted(ores):
        vals = [tot.get(n, {}).get(k, 0) for n in names]
        if max(vals) < 0.05: continue
        flag = "" if max(vals) - min(vals) < 0.01 else "   <"
        print(f"   {k:16} " + " / ".join(f"{v:6.2f}" for v in vals) + flag)
