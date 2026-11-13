"""Fetch OpenStreetMap's beaches (© OpenStreetMap contributors, ODbL) into rawdata/osm-beaches/beaches.tsv (Earth 0070;
the author chose "OSM plus the rule", 12 November 2026).

Every natural=beach and natural=shingle node, way and relation with its surface and fixme tags and its geometry as WKT,
in one query to QLever's OSM planet endpoint (University of Freiburg, https://qlever.dev/osm-planet). The Overpass API
was tried first and answered "too busy" on every instance on 12 November. tools/bake_shore_material.py keeps those at a
shore and reads their surface (sand, gravel, pebblestone, shingle, rock ...).
usage: python tools/fetch_osm_beaches.py [OUTDIR]"""
import os, sys, time, urllib.parse, urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.environ.get("EARTH_RAW", os.path.join(ROOT, "rawdata")), "osm-beaches")
API = "https://qlever.dev/api/osm-planet"
QUERY = """PREFIX osmkey: <https://www.openstreetmap.org/wiki/Key:>
PREFIX geo: <http://www.opengis.net/ont/geosparql#>
SELECT ?osm ?v ?surface ?fixme ?wkt WHERE {
  ?osm osmkey:natural ?v . FILTER(?v IN ("beach", "shingle"))
  OPTIONAL { ?osm osmkey:surface ?surface }
  OPTIONAL { ?osm osmkey:fixme ?fixme }
  ?osm geo:hasGeometry/geo:asWKT ?wkt
}"""


def main():
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, "beaches.tsv")
    data = urllib.parse.urlencode({"query": QUERY}).encode()
    req = urllib.request.Request(API, data=data, headers={"Accept": "text/tab-separated-values", "User-Agent": "earth-mod-bake/1.0 (beach outlines)"})
    t0, n = time.time(), 0
    with urllib.request.urlopen(req, timeout=3600) as resp, open(path + ".part", "wb") as f:
        while True:
            b = resp.read(1 << 20)
            if not b:
                break
            f.write(b)
            n += len(b)
            if n % (50 << 20) < (1 << 20):
                print(f"{n / 1e6:.0f} MB, {time.time() - t0:.0f} s", flush=True)
    os.replace(path + ".part", path)
    print(f"done: {n / 1e6:.1f} MB in {time.time() - t0:.0f} s -> {path}", flush=True)


if __name__ == "__main__":
    main()
