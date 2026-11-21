# EA-0174, decision 0102: compare_galena.py <census folder>. Vanilla ores, Earth main against galena in crystalline rock:
# rocks within 120 blocks of the ground, galena, its silver and their loose ores per chunk column
import json, sys
OUT = sys.argv[1]
F = {"galena": ["galena"], "silver": ["galena_nativesilver"], "loose galena": ["loose:galena"], "zinc": ["sphalerite"]}
def load(r):
    out = {}
    for line in open(f"{OUT}/{r}/oreaudit/results.jsonl", encoding="utf-8"):
        d = json.loads(line)
        if not d.get("header"): out[d["site"]] = d
    return out
def rocks(d):
    r = d.get("rocks", {}); t = sum(r.values()) or 1
    return ", ".join(f"{k} {100*v/t:.0f}%" for k, v in sorted(r.items(), key=lambda x: -x[1]) if 100 * v / t >= 4)
for a_name, b_name, title in (("main-10", "gal-10", "1:10 regional rock"), ("main-100", "gal-100", "1:100 regional rock"), ("vr-main-100", "vr-gal-100", "1:100 vanilla rock (must not change)")):
    try: a, b = load(a_name), load(b_name)
    except FileNotFoundError as e: print("missing", e); continue
    print(f"\n=== {title}, vanilla ores: main -> galena in crystalline rock")
    for s in b:
        x, y = a.get(s, {}), b[s]; cols = y["columns"] / 1024
        vals = []
        for g, fs in F.items():
            p = sum(x.get("families", {}).get(f, {}).get("n", 0) for f in fs) / cols; q = sum(y["families"].get(f, {}).get("n", 0) for f in fs) / cols
            vals.append(f"{g} {p:.1f} > {q:.1f}")
        print(f"{s:12s} {rocks(y)}")
        print(f"{'':12s} {'; '.join(vals)}")
