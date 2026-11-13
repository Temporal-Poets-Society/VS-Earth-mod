# analyze.py <run>...: per site terrain, sea-level ores, saltpeter, and totals per run; with "floor" and "nofloor" also what the floor removes
import json, sys, os
here = os.path.dirname(os.path.abspath(__file__))
SEALEVEL = {"hematite","limonite","magnetite","bituminouscoal","anthracite","graphite","chromite","ilmenite","rhodochrosite","borax","cinnabar",
            "fluorite","kernite","phosphorite","saltpeter","sulfur","emerald","peridot","obsidian"}
def load(run):
    rows = {}
    p = os.path.join(here, "results", run + ".jsonl")
    if not os.path.exists(p): return None, None
    hdr = None
    for line in open(p, encoding="utf-8"):
        r = json.loads(line)
        if r.get("header"): hdr = r; continue
        rows[r["site"]] = r
    return hdr, rows
runs = sys.argv[1:]
data = {r: load(r) for r in runs}
for r, (h, rows) in data.items():
    if h: print(r, "world", h["world"])
sites = next(rows for h, rows in data.values() if rows).keys()
for s in sites:
    print("\n==", s)
    for r, (h, rows) in data.items():
        if not rows or s not in rows: continue
        x = rows[s]; f = x["families"]; cols = x["columns"] or 1
        sea = {k: v for k, v in f.items() if k in SEALEVEL}
        tot = sum(v["n"] for k, v in f.items() if not k.startswith("loose:"))
        seatot = sum(v["n"] for v in sea.values())
        sp = f.get("saltpeter", {})
        print(f"  {r:8} top {x['minTop']}-{x['maxTop']} (land {x['landCols']}/{cols}) | deposit blocks/col {tot/cols:7.1f}, sea-level {seatot/cols:6.1f}"
              f" | saltpeter n {sp.get('n',0)} above {sp.get('above',0)} exposed {sp.get('exposed',0)} maxY {sp.get('maxY',-1)}"
              f" | sea-level maxY {max([v['maxY'] for v in sea.values()] or [-1])}")
    if "floor" in data and "nofloor" in data and data["floor"][1] and data["nofloor"][1] and s in data["floor"][1] and s in data["nofloor"][1]:
        a, b = data["floor"][1][s]["families"], data["nofloor"][1][s]["families"]
        lost = []
        for k in sorted(b):
            if k.startswith("loose:"): continue
            nb, na = b[k]["n"], a.get(k, {}).get("n", 0)
            if nb and (nb - na) / nb > .02: lost.append(f"{k} {nb}->{na} ({100*(nb-na)/nb:.0f}% lost, minY {b[k]['minY']})")
        print("   floor removes:", "; ".join(lost) or "nothing over 2 %")
