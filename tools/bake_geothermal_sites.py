"""Bake Earth's geothermal sites (Core.Geothermal; decision 0057; the author: "Hot springs should be only in places with
hot springs") into two generated C# files:

- src/Core/GeothermalSites.cs: the Smithsonian Global Volcanism Program's Holocene volcanoes, and GeoNames' hot springs
  (SPNT) and geysers (GYSR) that OpenStreetMap doesn't have within 1 km.
- src/Core/GeothermalSitesOsm.cs: OpenStreetMap's natural=hot_spring and natural=geyser. It is kept apart because it
  is ODbL data (a derivative database: offered under ODbL 1.0, (c) OpenStreetMap contributors).

Sources (each downloaded with the author's permission, 7 November 2026):
- GVP, Volcanoes of the World, Holocene volcano list: the GVP web feature service
  (GVP-VOTW:Smithsonian_VOTW_Holocene_Volcanoes, CSV), saved as rawdata/gvp-holocene/Smithsonian_VOTW_Holocene_Volcanoes.csv.
- GVP, Holocene eruption list, from the same service (GVP-VOTW:Smithsonian_VOTW_Holocene_Eruptions, CSV), saved as
  rawdata/gvp-eruptions/Smithsonian_VOTW_Holocene_Eruptions.csv ("download what is needed", 0057 part 2).
- OpenStreetMap through the Overpass API (rawdata/osm-geothermal/query.overpassql), saved as
  rawdata/osm-geothermal/osm-hot-springs-geysers.json.
- GeoNames allCountries (CC BY 4.0), rawdata/geonames/allCountries.zip of 30 October 2026, as for peaks.json.

Volcano classes (Core.Geothermal.SiteKind), from the GVP's evidence and last known eruption:
- Active: an eruption dated in the Common Era (year 0 or later). Its system was alive in 1318: an eruption after
  1318 needs one then. Only vents born after 1318 (Paricutin 1943) are wrong; the GVP lists them inside older fields.
- Holocene: dated before the Common Era, or credible Holocene evidence without a date.
- Uncertain: "Evidence Uncertain" and "Unrest / Holocene".
GeoNames' own volcanoes (VLC) are not used: they carry no ages and include long-dead ones (Hohentwiel, about 7 million
years), and the GVP list covers the Holocene ones.

For volcano bodies (geothermal rules 2, 0057 part 2) each volcano also gets its GVP number, landform (Core.Geothermal.
Landform), summit elevation and whether it was active in the game's time. The game happens an unknown time after 1318,
the author's guess 100-200 years: active then is a confirmed eruption starting 1300-1700, or one of the volcanoes erupting
at least FREQUENT times since (the record before 1700 is thin away from Europe, Japan and Indonesia; a volcano that busy
now was busy then too).

Springs: a spring or geyser tagged with a temperature below 25 C, or named as a cold-water geyser (Andernach's,
driven by gas), is left out. Duplicates within 100 m (a spring mapped as a point and an area) are merged.

Usage: python -I tools/bake_geothermal_sites.py [--gvp CSV] [--eruptions CSV] [--osm JSON] [--geonames ZIP]
"""
import argparse, csv, io, json, math, os, re, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RAW = os.environ.get("EARTH_RAW", os.path.join(ROOT, "rawdata"))

# Core.Geothermal.SiteKind, in its order
KIND = {"Active": 0, "Holocene": 1, "Uncertain": 2, "HotSpring": 3, "Geyser": 4}
# Core.Geothermal.Landform, in its order (the GVP's Volcanic_Landform; its three "Minor" ones are one)
FORM = {"Composite": 0, "Shield": 1, "Caldera": 2, "Cluster": 3, "Minor": 4}
ERA = (1300, 1700)
FREQUENT = 5


def form(landform):
    return FORM["Minor" if landform.startswith("Minor") else landform] if landform else FORM["Minor"]


def read_era(path):
    """GVP volcano number -> active in the game's time (see the module notes)."""
    during, after = set(), {}
    with open(path, encoding="utf8", newline="") as f:
        for r in csv.DictReader(f):
            y = r["StartDateYear"].strip()
            if r["Activity_Type"].strip() != "Confirmed Eruption" or not y:
                continue
            y, n = int(float(y)), r["Volcano_Number"].strip()
            if ERA[0] <= y <= ERA[1]:
                during.add(n)
            elif y > ERA[1]:
                after[n] = after.get(n, 0) + 1
    return during | {n for n, k in after.items() if k >= FREQUENT}, len(during)


def cs(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def km(a, b):
    """Distance in km between (lat, lon) pairs (equirectangular; fine at these distances)."""
    dlat = (a[0] - b[0]) * 111.32
    dlon = (a[1] - b[1] + 540) % 360 - 180
    dlon *= 111.32 * math.cos(math.radians((a[0] + b[0]) / 2))
    return math.hypot(dlat, dlon)


class Grid:
    """Points in 0.1-degree cells, for near-duplicate checks."""

    def __init__(self):
        self.cells = {}

    def key(self, p):
        return (math.floor(p[0] * 10), math.floor(p[1] * 10))

    def add(self, p, kind):
        self.cells.setdefault(self.key(p), []).append((p, kind))

    def near(self, p, limit, kinds=None):
        k = self.key(p)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                for q, kind in self.cells.get((k[0] + dy, k[1] + dx), ()):  # no wrap at 180 degrees: no dedupe across it
                    if (kinds is None or kind in kinds) and km(p, q) <= limit:
                        return True
        return False


def read_gvp(path):
    sites = []
    with open(path, encoding="utf8", newline="") as f:
        for r in csv.DictReader(f):
            lat, lon = float(r["Latitude"]), float(r["Longitude"])
            ev = r["Evidence_Category"].strip()
            year = r["Last_Eruption_Year"].strip()
            if ev in ("Evidence Uncertain", "Unrest / Holocene"):
                kind = "Uncertain"
            elif year and int(float(year)) >= 0:
                kind = "Active"
            else:
                kind = "Holocene"
            elevation = int(round(float(r["Elevation"]))) if r["Elevation"].strip() else 0
            sites.append((lat, lon, kind, r["Volcano_Name"].strip(), r["Volcano_Number"].strip(),
                          form(r["Volcanic_Landform"].strip()), elevation))
    return sites


def temperature(tags):
    t = tags.get("temperature", "")
    m = re.match(r"\s*(-?\d+(?:[.,]\d+)?)", t)
    if not m:
        return None
    v = float(m.group(1).replace(",", "."))
    return (v - 32) * 5 / 9 if "F" in t.upper() and "C" not in t.upper() else v


def read_osm(path):
    d = json.load(open(path, encoding="utf8"))
    out, cold = [], 0
    for e in d["elements"]:
        tags = e.get("tags", {})
        kind = {"hot_spring": "HotSpring", "geyser": "Geyser"}.get(tags.get("natural"))
        if kind is None:
            continue
        if "lat" in e:
            lat, lon = e["lat"], e["lon"]
        elif "center" in e:
            lat, lon = e["center"]["lat"], e["center"]["lon"]
        else:
            continue
        t = temperature(tags)
        name = (tags.get("name", "") + " " + tags.get("name:en", "")).lower()
        if (t is not None and t < 25) or "kaltwasser" in name or "cold" in name:
            cold += 1
            continue
        out.append((lat, lon, kind))
    return out, cold, d.get("osm3s", {}).get("timestamp_osm_base", "?")


def read_geonames(path):
    out = []
    with zipfile.ZipFile(path) as z, z.open("allCountries.txt") as f:
        for line in io.TextIOWrapper(f, encoding="utf8"):
            p = line.rstrip("\n").split("\t")
            if len(p) > 7 and p[7] in ("SPNT", "GYSR"):
                out.append((float(p[4]), float(p[5]), "HotSpring" if p[7] == "SPNT" else "Geyser"))
    return out


def dedupe(points, limit_km):
    g, kept = Grid(), []
    for p in sorted(points):
        if g.near(p[:2], limit_km):
            continue
        g.add(p[:2], p[2])
        kept.append(p)
    return kept


def write(path, header, arrays):
    lines = [f"// {h}" if h else "//" for h in header]
    lines += ["namespace EarthMap.Core", "{", "    public static partial class Geothermal", "    {"]
    for name, typ, values in arrays:
        lines.append(f"        static readonly {typ}[] {name} = {{")
        per = 8 if typ != "string" else 4
        for i in range(0, len(values), per):
            lines.append("            " + " ".join(v + "," for v in values[i:i + per]))
        lines.append("        };")
    lines += ["    }", "}", ""]
    with open(path, "w", encoding="utf8", newline="\n") as f:
        f.write("\n".join(lines))


def latlon(sites):
    return [f"{v:.5f}f" for s in sites for v in (s[0], s[1])]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gvp", default=os.path.join(RAW, "gvp-holocene", "Smithsonian_VOTW_Holocene_Volcanoes.csv"))
    ap.add_argument("--eruptions", default=os.path.join(RAW, "gvp-eruptions", "Smithsonian_VOTW_Holocene_Eruptions.csv"))
    ap.add_argument("--osm", default=os.path.join(RAW, "osm-geothermal", "osm-hot-springs-geysers.json"))
    ap.add_argument("--geonames", default=os.path.join(RAW, "geonames", "allCountries.zip"))
    ap.add_argument("--out", default=os.path.join(ROOT, "src", "Core"))
    a = ap.parse_args()

    volcanoes = sorted(read_gvp(a.gvp))
    era, during = read_era(a.eruptions)
    era_count = sum(1 for v in volcanoes if v[4] in era and v[2] != "Uncertain")
    osm, cold, stamp = read_osm(a.osm)
    osm = dedupe(osm, 0.1)
    osm_grid = Grid()
    for p in osm:
        osm_grid.add(p[:2], p[2])
    geonames_all = read_geonames(a.geonames)
    geonames = [p for p in dedupe(geonames_all, 0.1) if not osm_grid.near(p[:2], 1.0)]

    vc = {k: sum(1 for v in volcanoes if v[2] == k) for k in ("Active", "Holocene", "Uncertain")}
    oc = {k: sum(1 for v in osm if v[2] == k) for k in ("HotSpring", "Geyser")}
    gc = {k: sum(1 for v in geonames if v[2] == k) for k in ("HotSpring", "Geyser")}

    write(os.path.join(a.out, "GeothermalSites.cs"), [
        "Generated by tools/bake_geothermal_sites.py. Do not edit.",
        "Holocene volcanoes: Global Volcanism Program, Smithsonian Institution, Volcanoes of the World, Holocene volcano",
        "list (web feature service GVP-VOTW:Smithsonian_VOTW_Holocene_Volcanoes, retrieved 7 November 2026): "
        + ", ".join(f"{n} {k}" for k, n in vc.items()) + ".",
        f"Active in the game's time (GVP Holocene eruption list, same service and date): {era_count} volcanoes, a confirmed",
        f"eruption starting {ERA[0]}-{ERA[1]} or at least {FREQUENT} since.",
        "Hot springs and geysers: GeoNames allCountries (CC BY 4.0, https://download.geonames.org/export/dump/),",
        "feature codes SPNT and GYSR, those without an OpenStreetMap one within 1 km: "
        + ", ".join(f"{n} {k}" for k, n in gc.items()) + ".",
    ], [
        ("GvpLatLon", "float", latlon(volcanoes)),
        ("GvpKind", "byte", [str(KIND[v[2]]) for v in volcanoes]),
        ("GvpName", "string", [cs(v[3]) for v in volcanoes]),
        ("GvpNumber", "int", [v[4] for v in volcanoes]),
        ("GvpForm", "byte", [str(v[5]) for v in volcanoes]),
        ("GvpElevation", "short", [str(max(-11000, min(9000, v[6]))) for v in volcanoes]),
        ("GvpEra", "bool", ["true" if v[4] in era else "false" for v in volcanoes]),
        ("GeoNamesSpringLatLon", "float", latlon(geonames)),
        ("GeoNamesSpringKind", "byte", [str(KIND[v[2]]) for v in geonames]),
    ])
    write(os.path.join(a.out, "GeothermalSitesOsm.cs"), [
        "Generated by tools/bake_geothermal_sites.py. Do not edit.",
        "",
        "Hot springs and geysers from OpenStreetMap (natural=hot_spring, natural=geyser; Overpass API, data of " + stamp + ").",
        "(c) OpenStreetMap contributors. This list is a derivative database of OpenStreetMap and is available under the",
        "Open Database License 1.0 (https://opendatacommons.org/licenses/odbl/1-0/); see https://www.openstreetmap.org/copyright.",
        "Changed: positions (areas by their centre) and kind only; springs tagged below 25 C and cold-water geysers left out ("
        + str(cold) + "); duplicates within 100 m merged. " + ", ".join(f"{n} {k}" for k, n in oc.items()) + ".",
    ], [
        ("OsmSpringLatLon", "float", latlon(osm)),
        ("OsmSpringKind", "byte", [str(KIND[v[2]]) for v in osm]),
    ])
    print("volcanoes", vc, "| active in the game's time", era_count, f"({during} with an eruption {ERA[0]}-{ERA[1]})",
          "| OSM", oc, "cold left out", cold, "| GeoNames kept", gc, "of", len(geonames_all))


if __name__ == "__main__":
    main()
