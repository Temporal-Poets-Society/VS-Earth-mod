"""Deposits where Macrostrat turns GLiM's carbonate into non-carbonate (and back), with the Macrostrat unit's own words."""
import sys, os, json, collections
sys.path.insert(0, os.path.dirname(__file__))
sys.path.insert(0, sys.argv[3])
import mvt, macrostrat_codes as M
rows = json.load(open(sys.argv[1], encoding="utf-8"))["deposits"]
r4 = json.load(open(sys.argv[2], encoding="utf-8"))
CARB = {"limestone", "dolostone", "marble", "chalk"}
P = {p["id"]: p for p in r4["profiles"]}
L = r4["litho"]
def prof(c):
    for p, v in L["patterns"]:
        if all(a == "?" or a == b for a, b in zip(p, c)): return v
    return L["classes"].get(c[:2], "unknown")
carb = lambda c: c is not None and bool(CARB & {b["material"] for b in P[prof(c)]["beds"]})
lost, won = [], []
for d in rows:
    lit = d["litho"] or ""
    if ">" not in lit: continue
    g = lit.split(">")[0] or None
    if carb(g) and not carb(d["code"]): lost.append(d)
    elif not carb(g) and carb(d["code"]): won.append(d)
print(f"Macrostrat changed carbonate: lost at {len(lost)}, won at {len(won)}")
tiles = sys.argv[4]
def words(lat, lon):
    x, y = mvt.tile_xy(lat, lon, 7)
    p = os.path.join(tiles, f"7_{x}_{y}.mvt")
    if not os.path.exists(p): return "?"
    import shapely
    from shapely.geometry import Point
    L = mvt.decode(open(p, "rb").read())["units"]
    n = 2 ** 7; ext = L["extent"]
    import math
    px = ((lon + 180) / 360 * n - x) * ext
    py = ((1 - math.log(math.tan(math.radians(lat)) + 1 / math.cos(math.radians(lat))) / math.pi) / 2 * n - y) * ext
    import macrostrat_units as U
    for f in L["features"]:
        if f["type"] != 3 or not f["rings"]: continue
        for poly in U.polygons(f["rings"]):
            if poly.is_valid and poly.contains(Point(px, py)):
                pr = f["props"]
                return f"[{pr.get('source_id')}] {pr.get('lith')!r} types={[pr.get(f'lith_type{i}') for i in range(1,5)]} -> {M.code(pr.get('lith'), [pr.get(f'lith_type{i}') for i in range(1,5)])}"
    return "(no unit)"
for d in lost[:40]:
    print(f"LOST {d['name'][:28]:28s} {d['lat']:.2f},{d['lon']:.2f} host={d['host'][:50]!r} glim={d['litho']} -> {d['code']}\n      {words(d['lat'], d['lon'])}")
