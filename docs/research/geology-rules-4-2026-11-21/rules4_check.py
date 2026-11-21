"""Rules 3 (0.5-degree GLiM classes) against rules 4 (30" full GLiM codes): carbonate-hosted deposits and named districts.
Usage: python rules4_check.py <worktree> <rock .egts> <deposit-check.json> <out.json>"""
import sys, json, base64, collections
import numpy as np
sys.path.insert(0, __import__("os").path.dirname(__file__))
import egts_read as E

root, egts, deps_path, out = sys.argv[1:5]
cfg = root + "/assets/earth/config/"
r3 = json.load(open(cfg + "geology-rules-v3.json", encoding="utf-8"))
r4 = json.load(open(cfg + "geology-rules-v4.json", encoding="utf-8"))
g = json.load(open(cfg + "geology-grids-v1.json", encoding="utf-8"))
coarse = np.frombuffer(base64.b64decode(next(v for k, v in g.items() if k.lower() == "lithology")), dtype=np.uint8).reshape(360, 720)
P3 = {p["id"]: p for p in r3["profiles"]}; P4 = {p["id"]: p for p in r4["profiles"]}
CARB = {"limestone", "dolostone", "marble", "chalk"}
tiles = E.read_set(egts)
L = r4["litho"]


def prof4_id(code):
    for p, v in L["patterns"]:
        if all(a == "?" or a == b for a, b in zip(p, code)):
            return v
    return L["classes"].get(code[:2], "unknown")


def at4(lat, lon):
    lit = E.lookup(tiles, lat, lon, "litho"); bed = E.lookup(tiles, lat, lon, "bedrock")
    try:
        mac = E.lookup(tiles, lat, lon, "macro")
    except KeyError:
        mac = None
    if mac:
        lit = lit + ">" + mac if lit else ">" + mac
    code = bed or mac or (lit.split(">")[0] if lit else None)
    return lit, code, (prof4_id(code) if code else "unknown")


def at3(lat, lon):
    j = min(359, int((90 - lat) / .5)); i = min(719, int((lon + 180) / .5))
    c = int(coarse[j, i])
    return c, r3["classes"].get(str(c), "unknown")


def carb(p):
    beds = [b["material"] for b in p["beds"]]
    return bool(set(beds) & CARB), bool(beds) and beds[0] in CARB


def region(lat, lon):
    if lon < -30: return "Americas"
    if lat < -10 and lon > 110: return "Australia"
    if lon < 60 and lat > 35: return "Europe"
    if lon < 55 and lat <= 35: return "Africa"
    return "Asia"


deps = json.load(open(deps_path, encoding="utf-8"))
tot = collections.Counter(); per = collections.defaultdict(collections.Counter); rows = []
for d in deps:
    lat, lon = d["lat"], d["lon"]
    c3, p3 = at3(lat, lon); lit, code, p4 = at4(lat, lon)
    a3, t3 = carb(P3[p3]); a4, t4 = carb(P4[p4])
    reg = region(lat, lon)
    for k, v in (("n", 1), ("r3_any", a3), ("r3_top", t3), ("r4_any", a4), ("r4_top", t4)):
        tot[k] += v; per[reg][k] += v
    rows.append({"name": d["name"], "lat": lat, "lon": lon, "host": d["host"], "r3": p3, "litho": lit, "code": code, "r4": p4})
pc = lambda c, k: f"{100 * c[k] / max(1, c['n']):4.0f} %"
print(f"{tot['n']} carbonate-hosted places: carbonate beds in the profile, rules 3 {pc(tot, 'r3_any')} -> rules 4 {pc(tot, 'r4_any')}; "
      f"carbonate top bed {pc(tot, 'r3_top')} -> {pc(tot, 'r4_top')}")
for reg, c in sorted(per.items()):
    print(f"  {reg:10s} n={c['n']:4d}  any {pc(c, 'r3_any')} -> {pc(c, 'r4_any')}   top {pc(c, 'r3_top')} -> {pc(c, 'r4_top')}")

DISTRICTS = [
    ("Mednorudyansk, Urals (skarn copper in limestone/marble)", 57.93, 60.03), ("Tsumeb, Namibia (dolomite)", -19.24, 17.71),
    ("Burra, South Australia (dolomite)", -33.68, 138.93), ("Bisbee, Arizona (limestone)", 31.44, -109.91),
    ("Kitwe, Zambian Copperbelt (dolomitic shale)", -12.80, 28.21), ("Kolwezi, Katanga (dolomite)", -10.72, 25.47),
    ("Chuquicamata, Chile (porphyry)", -22.29, -68.90), ("Carrara, Italy (marble)", 44.08, 10.13),
    ("Mount Isa, Queensland (dolomitic shale)", -20.73, 139.49), ("Broken Hill, NSW (gneiss)", -31.96, 141.47),
    ("Kiruna, Sweden (volcanics, iron ore)", 67.85, 20.22), ("Hamersley, Pilbara (banded iron)", -22.6, 118.0),
    ("Witwatersrand, Johannesburg (conglomerate, quartzite)", -26.2, 28.0), ("Kalgoorlie (greenstone)", -30.75, 121.47),
    ("Dartmoor (granite)", 50.57, -3.92), ("Peak District, Derbyshire (limestone)", 53.25, -1.75),
    ("Pine Point, NWT (dolomite, MVT)", 60.85, -114.45), ("Viburnum Trend, Missouri (dolomite, MVT)", 37.6, -91.1),
    ("Silesia, Olkusz (dolomite, MVT)", 50.28, 19.56), ("Ruhr (coal measures)", 51.5, 7.2),
    ("Donbas (coal measures)", 48.0, 38.0), ("Stassfurt (salt)", 51.85, 11.58), ("Sudbury (layered intrusion)", 46.6, -81.2),
    ("Kyiv", 50.45, 30.52), ("Riga", 56.95, 24.1),
]
print("\nDistricts: rules 3 profile -> rules 4 code (bedrock under cover) profile")
drows = []
for name, lat, lon in DISTRICTS:
    c3, p3 = at3(lat, lon); lit, code, p4 = at4(lat, lon)
    print(f"  {name:55s} {p3:22s} -> {lit or '-'}{'' if code == lit else ' / ' + str(code)} {p4}")
    drows.append({"name": name, "lat": lat, "lon": lon, "r3": p3, "litho": lit, "code": code, "r4": p4})
json.dump({"deposits": rows, "districts": drows, "totals": tot, "regions": per}, open(out, "w", encoding="utf-8"), indent=1)
