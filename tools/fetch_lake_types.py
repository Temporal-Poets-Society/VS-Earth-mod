"""
Fetches what kind of water body each Natural Earth lake is from Wikidata (CC0): its "instance of" (P31) classes, so
bake_earth.py can tell dammed natural lakes (Cayuga Lake, Utah Lake: keep) from man-made reservoirs (Eufaula Lake,
Lake Cumberland: remove). Writes rawdata/lake_types.json: {"Q123": ["reservoir", "lake", ...], ...}.
Run once after downloading the Natural Earth lakes; bake_earth.py works without it (then HydroLAKES alone decides).
"""
import json, os, time, urllib.parse, urllib.request

import shapefile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RAW = os.path.join(ROOT, "rawdata")
OUT = os.path.join(RAW, "lake_types.json")


def main():
    ids = set()
    for f in ["ne_10m_lakes", "ne_10m_lakes_europe", "ne_10m_lakes_north_america"]:
        r = shapefile.Reader(os.path.join(RAW, "ne_lakes", f), encoding="utf-8")
        for rec in r.iterRecords():
            q = (rec.as_dict().get("wikidataid") or "").strip()
            if q.startswith("Q"):
                ids.add(q)
    ids = sorted(ids)
    print(len(ids), "lakes with a Wikidata id")
    out = {}
    for i in range(0, len(ids), 250):
        batch = ids[i:i + 250]
        query = ("SELECT ?item ?classLabel WHERE { VALUES ?item { %s } ?item wdt:P31 ?class . "
                 "SERVICE wikibase:label { bd:serviceParam wikibase:language \"en\". } }") % " ".join("wd:" + q for q in batch)
        url = "https://query.wikidata.org/sparql?format=json&query=" + urllib.parse.quote(query)
        req = urllib.request.Request(url, headers={"User-Agent": "EarthMapBake/0.5 (Vintage Story mod; offline data bake)"})
        with urllib.request.urlopen(req, timeout=120) as resp:
            data = json.load(resp)
        for b in data["results"]["bindings"]:
            q = b["item"]["value"].rsplit("/", 1)[1]
            out.setdefault(q, []).append(b["classLabel"]["value"])
        print(f"  {min(i + 250, len(ids))}/{len(ids)}")
        time.sleep(1)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=0, ensure_ascii=False, sort_keys=True)
    print("wrote", OUT, len(out), "entries")


if __name__ == "__main__":
    main()
