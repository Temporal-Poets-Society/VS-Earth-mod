"""Distinct Macrostrat units in a folder of carto tiles and the code each translates to (eyeball test).
Usage: python -I macro_translate_test.py <tools/ground> <tile dir> <sources.json> <out.tsv> [extra tile dir]"""
import sys, os, json, glob, collections
sys.path.insert(0, sys.argv[1])
import mvt, macrostrat_codes as M
scale = {s["source_id"]: s["scale"] for s in json.load(open(sys.argv[3], encoding="utf-8"))["success"]["data"]}
seen = collections.Counter(); ex = {}
dirs = [sys.argv[2]] + sys.argv[5:]
for d in dirs:
    for f in glob.glob(os.path.join(d, "*.mvt")):
        L = mvt.decode(open(f, "rb").read())
        for u in L.get("units", {"features": []})["features"]:
            p = u["props"]
            sc = scale.get(p.get("source_id"), "?")
            types = [p.get(f"lith_type{i}") for i in range(1, 5)]
            k = (sc, p.get("lith") or "", tuple(t for t in types if t))
            seen[k] += 1
with open(sys.argv[4], "w", encoding="utf-8") as out:
    codes = collections.Counter()
    for (sc, lith, types), n in seen.most_common():
        c = M.code(lith, types)
        codes[(sc in ("medium", "large"), c)] += n
        out.write(f"{sc}\t{c}\t{lith}\t{','.join(types)}\n")
print(len(seen), "distinct units")
for (fine, c), n in codes.most_common(40):
    print(f"  {'1:1M+' if fine else 'coarse'} {c} {n}")
