"""EA-0199: top block of land columns per audit site and run (SoilAudit results), and the digest comparison."""
import json, sys, pathlib, collections
OUT = pathlib.Path(sys.argv[1])
runs = ["audit-100-realistic", "audit-100-vanilla", "audit-10-realistic", "audit-10-vanilla"]
KINDS = ["soil-high", "soil-compost", "soil-medium", "soil-low", "soil-verylow", "forestfloor", "sand", "gravel", "rock", "snow/ice"]
SHORT = {"soil-high": "black earth", "soil-compost": "high", "soil-medium": "medium", "soil-low": "low", "soil-verylow": "barren",
         "forestfloor": "forest floor", "sand": "sand", "gravel": "gravel", "rock": "rock", "snow/ice": "snow/ice"}
data = {}
for r in runs:
    f = OUT / r / "soilaudit" / "results.jsonl"
    if not f.exists():
        continue
    for line in f.read_text(encoding="utf-8").splitlines():
        d = json.loads(line)
        data[(r, d["site"])] = d
sites = []
for (r, s) in data:
    if s not in sites:
        sites.append(s)
print("Top block of land columns (share of land columns; other kinds such as crops, loose stones and plants left out):\n")
for s in sites:
    print(f"{s}:")
    for r in runs:
        d = data.get((r, s))
        if not d:
            continue
        land = max(1, d["land"])
        top = d["top"]
        parts = [f"{SHORT[k]} {100 * top.get(k, 0) / land:.0f}%" for k in KINDS if 100 * top.get(k, 0) / land >= 1]
        print(f"  {r:22s} {', '.join(parts)}")
        uf, fd = d.get("underForest") or {}, d.get("forestDrops") or {}
        if uf:
            n = sum(uf.values())
            print(f"  {'':22s} under the forest floor: {', '.join(f'{k} {100 * v / n:.0f}%' for k, v in sorted(uf.items(), key=lambda x: -x[1])[:4])}")
        if fd:
            print(f"  {'':22s} forest floor drops (first {sum(fd.values())}): {', '.join(f'{k} x{v}' for k, v in sorted(fd.items(), key=lambda x: -x[1])[:4])}")
    print()
# digests
for scale in ("10", "25"):
    a, b = OUT / f"digest-{scale}-rc11" / "EarthMap", OUT / f"digest-{scale}-branch" / "EarthMap"
    if not a.exists() or not b.exists():
        print(f"digest 1:{scale}: missing ({a.exists()}, {b.exists()})")
        continue
    same = differ = 0
    for fa in sorted(a.glob("digest-*.Terrain*.txt")):
        if fa.name.endswith(".chunks.txt"):
            continue
        fb = b / fa.name
        if not fb.exists():
            print(f"  1:{scale} {fa.name}: missing on the branch"); differ += 1; continue
        la, lb = fa.read_text().splitlines(), fb.read_text().splitlines()
        if la == lb:
            same += 1
        else:
            differ += 1
            n = sum(1 for x, y in zip(la, lb) if x != y)
            print(f"  1:{scale} {fa.name}: {n} of {len(la)} lines differ")
    print(f"digest 1:{scale} climate only, rc.11 against the branch: {same} files identical, {differ} differ")
