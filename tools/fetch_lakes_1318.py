"""
Lakes of 1318 (decision 0064 step 3, decision 0107): fetches the outlines tools/lakes-1318.json takes from
OpenHistoricalMap (CC0) and OpenStreetMap (ODbL) and writes each as GeoJSON in tools/lakes-1318/<key>.geojson, which
tools/bake_lakes_1318.py reads. Run once; the outlines are kept in the repository so the bake doesn't need the network.

    .venv/Scripts/python -I tools/fetch_lakes_1318.py [key ...]
"""
import json, os, sys, time, urllib.parse, urllib.request

import shapely
from shapely.geometry import LineString, Polygon, mapping
from shapely.ops import polygonize, unary_union

TOOLS = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(TOOLS, "lakes-1318")
SERVERS = {"ohm": "https://overpass-api.openhistoricalmap.org/api/interpreter", "osm": "https://overpass-api.de/api/interpreter"}
AGENT = "earth-mod-bake/1.0"


def query(server, q):
    data = urllib.parse.urlencode({"data": q}).encode()
    req = urllib.request.Request(SERVERS[server], data=data, headers={"User-Agent": AGENT})
    with urllib.request.urlopen(req, timeout=180) as r:
        return json.load(r)


def element_polygon(e):
    """A way's or a multipolygon relation's area (outer rings less inner ones)."""
    if e["type"] == "way":
        pts = [(g["lon"], g["lat"]) for g in e["geometry"]]
        return Polygon(pts).buffer(0)
    outer, inner = [], []
    for m in e["members"]:
        if m["type"] != "way" or "geometry" not in m:
            continue
        line = LineString([(g["lon"], g["lat"]) for g in m["geometry"]])
        (inner if m.get("role") == "inner" else outer).append(line)
    po = unary_union(list(polygonize(unary_union(outer))))
    pi = unary_union(list(polygonize(unary_union(inner)))) if inner else None
    return po.difference(pi).buffer(0) if pi is not None else po.buffer(0)


def main():
    lakes = json.load(open(os.path.join(TOOLS, "lakes-1318.json"), encoding="utf-8"))["areas"]
    want = set(sys.argv[1:])
    os.makedirs(OUT, exist_ok=True)
    for a in lakes:
        src = a["outline"]
        if "ohm" not in src and "osm" not in src or (want and a["key"] not in want):
            continue
        server = "ohm" if "ohm" in src else "osm"
        kind, ident = src[server].split("/")
        res = query(server, f"[out:json][timeout:120];{kind}({ident});out geom;")
        e = res["elements"][0]
        poly = element_polygon(e)
        feature = {"type": "Feature", "properties": {"key": a["key"], "source": f"{server} {kind}/{ident}", "tags": e.get("tags", {}),
                                                     "copyright": res.get("osm3s", {}).get("copyright", "")},
                   "geometry": mapping(poly)}
        json.dump({"type": "FeatureCollection", "features": [feature]}, open(os.path.join(OUT, a["key"] + ".geojson"), "w", encoding="utf-8"),
                  ensure_ascii=False)
        print(f"{a['key']}: {server} {kind}/{ident}, {poly.geom_type}, {len(getattr(poly, 'geoms', [poly]))} parts, "
              f"{sum(len(p.interiors) for p in getattr(poly, 'geoms', [poly]))} holes", flush=True)
        time.sleep(2)


if __name__ == "__main__":
    main()
