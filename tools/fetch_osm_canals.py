"""Fetch canals built after 1318 from OpenStreetMap (Overpass API, ODbL) into rawdata/osm-canals, one JSON per canal
(Earth 0069 rule 6; the author: "Yes, fetch them", 12 November 2026).

Each canal: its waterway=canal lines and water=canal areas whose name matches, with geometry. The bake
(tools/bake_works_1318.py, canals) turns the water mask's river water along them into land, keeping natural rivers
and lakes. Years are the canal's opening (construction) per its Wikipedia article; every one is after 1318.

Works rules 4 (EA-0057, 13 November 2026): four canals found nothing under the first names. OpenStreetMap has the Corinth
Canal as "Διώρυγα Κορίνθου" (no "της"), the White Sea-Baltic Canal's dug sections as "Беломорканал", the Ismailia Canal
as "قناة الإسماعيلية" and the Nieuwe Waterweg as a river (waterway=river). Those entries carry rules 4 in the index: the
bake flags their areas LATE, which worlds of works rules 1-3 skip. A canal whose file holds no elements is asked again.
The Overpass API answered 502 and 504 on every instance on 13 November; --qlever asks QLever's OSM planet endpoint
(University of Freiburg, https://qlever.dev/osm-planet) instead and writes the same JSON (ways and relations with their
geometry), as tools/fetch_osm_beaches.py does.
usage: python tools/fetch_osm_canals.py [OUTDIR] [--qlever]"""
import json, os, sys, time, urllib.parse, urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ARGS = [a for a in sys.argv[1:] if not a.startswith("--")]
QLEVER = "--qlever" in sys.argv
OUT = ARGS[0] if ARGS else os.path.join(os.environ.get("EARTH_RAW", os.path.join(ROOT, "rawdata")), "osm-canals")
API = "https://overpass-api.de/api/interpreter"
QLEVER_API = "https://qlever.dev/api/osm-planet"

# (file key, OSM name regex, built, note[, options]): options "rules" (the works rules that undo it; 2 when not given),
# "waterway" (the waterway values its lines may have; canal when not given) and "undo" (False: fetched but left as it is)
CANALS = [
    ("kiel", "^Nord-Ostsee-Kanal$", "1887-1895", "Kiel Canal"),
    ("noordzeekanaal", "^Noordzeekanaal$", "1865-1876", "North Sea Canal"),
    ("amsterdam-rijnkanaal", "^Amsterdam-Rijnkanaal$", "1931-1952", "Amsterdam-Rhine Canal"),
    # (EA-0057: kept. Its course is open water, the Maas mouth, in RCE's maps of 1250 and 1500 as in 2000; the land dug
    # through in 1866-1872 had grown there after 1500 and lies in decision 0064's SEA areas, so 1318 has sea there already)
    ("nieuwe-waterweg", "^Nieuwe Waterweg$", "1866-1872", "Rotterdam's dug mouth", {"rules": 4, "waterway": ("canal", "river"), "undo": False}),
    ("manchester-ship-canal", "^Manchester Ship Canal$", "1887-1894", ""),
    ("mittellandkanal", "^Mittellandkanal$", "1906-1938", ""),
    ("dortmund-ems-kanal", "^Dortmund-Ems-Kanal$", "1892-1899", ""),
    ("main-donau-kanal", "^Main-Donau-Kanal$", "1960-1992", ""),
    ("albertkanaal", "^Albertkanaal$", "1930-1939", ""),
    ("gent-terneuzen", "^(Kanaal Gent-Terneuzen|Kanaal van Gent naar Terneuzen)$", "1823-1827", ""),
    ("boudewijnkanaal", "^Boudewijnkanaal$", "1896-1907", "Bruges-Zeebrugge"),
    ("grand-canal-alsace", "^Grand Canal d'Alsace$", "1928-1959", ""),
    ("donzere-mondragon", "^Canal de Donzère-Mondragon$", "1947-1952", ""),
    ("corinth", "^Διώρυγα (της )?Κορίνθου$", "1881-1893", "Corinth Canal", {"rules": 4}),
    ("dunare-marea-neagra", "^Canalul Dunăre( |-|–)Marea Neagră$", "1976-1984", "Danube-Black Sea Canal"),
    ("dtd", "^Kanal Dunav( |-|–)Tisa( |-|–)Dunav$|^Канал Дунав( |-|–)Тиса( |-|–)Дунав$", "1957-1977", "Danube-Tisa-Danube"),
    ("volga-don", "Волго-Донской", "1948-1952", "Volga-Don Canal"),
    ("moscow-canal", "^Канал имени Москвы$", "1932-1937", "Moscow Canal"),
    ("white-sea-baltic", "Беломорско-Балтийский канал|^Беломорканал$", "1931-1933", "White Sea-Baltic Canal", {"rules": 4}),
    ("kakhovka-main", "Каховський магістральний канал|Каховский магистральный канал", "1979-", "Kakhovka main canal"),
    ("north-crimean", "Північнокримський канал|Северо-Крымский канал", "1961-1971", "North Crimean Canal"),
    ("karakum", "Garagum|Каракумский канал", "1954-1988", "Karakum Canal"),
    ("irtysh-karaganda", "Иртыш.*Караганда|Ертіс.*Қарағанды|Канал имени Сатпаева|Сәтбаев", "1962-1974", "Irtysh-Karaganda Canal"),
    ("indira-gandhi", "Indira Gandhi (Nahar|Canal)", "1958-", "Indira Gandhi Canal"),
    ("mahmoudiyah", "المحمودية", "1817-1820", "Mahmoudiyah Canal"),
    ("ismailia", "قناة الإسماعيلية|ترعة الإسماعيلية|^Ismailia Canal$", "1861-1863", "Ismailia (Sweet Water) Canal", {"rules": 4}),
    ("welland", "^Welland (Ship )?Canal$", "1829-1932", ""),
    ("chicago-sanitary", "^Chicago Sanitary and Ship Canal$", "1892-1900", ""),
    ("cape-cod", "^Cape Cod Canal$", "1909-1914", ""),
    ("chesapeake-delaware", "^Chesapeake (and|&) Delaware Canal$", "1829", ""),
]


def query(regex, waterways=("canal",)):
    r = regex.replace("\\", "\\\\").replace('"', '\\"')
    ww = "|".join(waterways)
    return (f'[out:json][timeout:300];('
            f'way["waterway"~"^({ww})$"]["name"~"{r}"];'
            f'way["water"="canal"]["name"~"{r}"];'
            f'relation["water"="canal"]["name"~"{r}"];'
            f'relation["waterway"~"^({ww})$"]["name"~"{r}"];'
            f');out geom;')


def qlever(regex, waterways=("canal",)):
    """The same selection from QLever's OSM planet (SPARQL), as Overpass JSON: each way or relation with its tags and its
    geometry from the WKT (a line or an area's outer rings as the members of a relation)."""
    import shapely.wkt
    r = regex.replace("\\", "\\\\").replace('"', '\\"')
    ww = ", ".join(f'"{w}"' for w in waterways)
    q = f"""PREFIX osmkey: <https://www.openstreetmap.org/wiki/Key:>
PREFIX geo: <http://www.opengis.net/ont/geosparql#>
SELECT ?osm ?name ?ww ?water ?natural ?width ?wkt WHERE {{
  {{ ?osm osmkey:waterway ?ww . FILTER(?ww IN ({ww})) }} UNION {{ ?osm osmkey:water ?water . FILTER(?water = "canal") }}
  ?osm osmkey:name ?name . FILTER(REGEX(?name, "{r}"))
  OPTIONAL {{ ?osm osmkey:natural ?natural }} OPTIONAL {{ ?osm osmkey:width ?width }}
  ?osm geo:hasGeometry/geo:asWKT ?wkt
}}"""
    req = urllib.request.Request(QLEVER_API, data=urllib.parse.urlencode({"query": q}).encode(),
                                 headers={"Accept": "application/sparql-results+json", "User-Agent": "earth-mod-bake/1.0 (canal outlines)"})
    with urllib.request.urlopen(req, timeout=900) as resp:
        rows = json.load(resp)["results"]["bindings"]
    els = {}
    for b in rows:
        iri = b["osm"]["value"]
        kind, oid = iri.rstrip("/").split("/")[-2:]
        if kind not in ("way", "relation"):
            continue
        tags = {k: b[v]["value"] for k, v in (("name", "name"), ("waterway", "ww"), ("water", "water"), ("natural", "natural"), ("width", "width")) if v in b}
        g = shapely.wkt.loads(b["wkt"]["value"])
        pts = lambda cs: [{"lat": round(y, 7), "lon": round(x, 7)} for x, y in cs]
        if g.geom_type == "LineString":
            geo = [pts(g.coords)]
        elif g.geom_type in ("Polygon", "MultiPolygon"):
            geo = [pts(p.exterior.coords) for p in getattr(g, "geoms", [g])]
        elif g.geom_type == "MultiLineString":
            geo = [pts(l.coords) for l in g.geoms]
        else:
            continue
        e = els.setdefault((kind, oid), {"type": kind, "id": int(oid), "tags": {}})
        e["tags"].update(tags)
        if kind == "way" and len(geo) == 1:
            e["geometry"] = geo[0]
        else:
            e["type"] = "relation"
            e["members"] = [{"type": "way", "role": "outer", "geometry": x} for x in geo]
    return json.dumps({"version": 0.6, "generator": "QLever osm-planet (fetch_osm_canals.py --qlever)",
                       "osm3s": {"copyright": "The data included in this document is from www.openstreetmap.org. The data is made available under ODbL."},
                       "elements": list(els.values())}, ensure_ascii=False, indent=1).encode("utf-8")


def main():
    os.makedirs(OUT, exist_ok=True)
    index = []
    for key, regex, built, note, *opt in CANALS:
        opt = opt[0] if opt else {}
        waterways = opt.get("waterway", ("canal",))
        path = os.path.join(OUT, key + ".json")
        if os.path.exists(path) and not json.load(open(path, encoding="utf-8")).get("elements"):
            os.remove(path)                                  # found nothing before: ask again
        if not os.path.exists(path) and QLEVER:
            body = qlever(regex, waterways)
            with open(path, "wb") as f:
                f.write(body)
        if not os.path.exists(path):
            data = urllib.parse.urlencode({"data": query(regex, waterways)}).encode()
            req = urllib.request.Request(API, data=data, headers={"User-Agent": "earth-mod-bake/1.0 (canal outlines)"})
            for attempt in range(4):
                try:
                    with urllib.request.urlopen(req, timeout=400) as resp:
                        body = resp.read()
                    break
                except Exception as e:
                    print(f"{key}: {e}; retrying", flush=True)
                    time.sleep(30 * (attempt + 1))
            else:
                print(f"{key}: failed", flush=True)
                continue
            with open(path, "wb") as f:
                f.write(body)
            time.sleep(5)
        els = json.load(open(path, encoding="utf-8")).get("elements", [])
        print(f"{key}: {len(els)} elements ({os.path.getsize(path)} bytes)", flush=True)
        index.append({"key": key, "name": regex, "built": built, "note": note, "elements": len(els), "rules": opt.get("rules", 2),
                      "undo": opt.get("undo", True)})
    json.dump(index, open(os.path.join(OUT, "index.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
