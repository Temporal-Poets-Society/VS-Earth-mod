"""
Works after 1318 (Earth decision 0064): the areas where a new world's coasts and waters go back to 1318, in the layout of
the lake outlines (lakes-glo90-1.bin, tools/bake_lake_outlines.py) under its own magic, so the game reads them with
LakeOutlineData/LakeOutlineLayer.

Each "lake" record is one area: its level (decimetres, for lakes) and its kind in the flags byte, cut at the 1° tiles:

  16 SEA   sea in 1318, land today (reclaimed): the coast's land gives way to it
  32 LAKE  a lake in 1318 at its level, land today (drained)
  64 LAND  land in 1318, water today (dug, flooded or cut after 1318): today's lake and river water there is not drawn
 128 FEN   fen in 1318 at about sea level, land below the sea today (sunk after drainage)

Step 1, the Netherlands (decision 0064): the RCE paleogeographic maps (Vos et al., Atlas van Nederland in het Holoceen,
CC BY 4.0) of 1250, 1500 and 2000 AD, compared class by class. 1250 is the map nearest 1318; 1500 adds the natural change
after it (the storm floods of 1362 and 1421, the Zuiderzee and the lakes eating into the peat), which stays, as do dunes,
salt marsh and tidal flats in 2000:

  SEA  = open water or tidal flats in 1500, or in 1250 and not land by 1500  ∩  drained, embanked, built-over or inland
         water in 2000 (the Zuiderzee and the IJ, the Lauwerszee, the Delta's arms). Sea of 1250 that is land in 1500
         (734 km² embanked, 2 drained: the Middelzee, Zeeland's 14th-century polders) was mostly diked or salt marsh by
         1318, and stays land.
  LAKE = inland water in 1250 or 1500  ∩  drained, embanked or built-over in 2000, outside SEA
         (Haarlemmermeer, Beemster, Schermer, Purmer, the Grote Waard drowned in 1421)
  LAND = inland water in 2000  ∩  land in 1250 and 1500, apart from pieces touching SEA (the Zuiderzee ate into the
         peat after 1500 too: that is sea)                                       (peat-digging lakes: Loosdrecht)

Works rules 4 (file version 3, works-1318-2.bin; EA-0053, EA-0057): land made in the sea that the RCE map of 2000 does not
show as made land is SEA too (--post2000): Maasvlakte 2 (2008-2013, after the map, which has open sea there) and the
dunes heaped on Maasvlakte 1 (which the map has as natural high dune). Within a named work's box, the water mask's land
(GLO-90 WBM class 0: the game draws it, tools/sea_ocean.py adds it to GSHHG's sea) on the sea of 1318 as above (sea in
1500, or in 1250 and not land by 1500), closed so its basins and lagoons are in. Only named works: the other places where the mask's land and RCE's sea of 2000 differ are
sandbanks and Wadden islands that moved by themselves, which is natural change and stays. Every area these rules add
carries LATE (4), which worlds of works rules 1-3 skip, and comes after all older pieces of its tile, so the file keeps
every area of version 2 as it was.

Big rivers are inland water in all three maps, so they stay. Holland's sunken peat polders need nothing: in 1318 they
were drained peat land at about sea level, and the game holds land below the sea at the sea's level.

    .venv/Scripts/python -I tools/bake_works_1318.py --rce DIR --out FILE [--report FILE.json]

DIR holds paleogeografie1250nc.geojson, paleogeografie1500nc.geojson and paleogeografie2000nc.geojson as RCE's WFS serves them (EPSG:4326).
"""
import argparse, json, math, os, struct, sys, time

import brotli
import numpy as np
import shapely
from shapely.geometry import shape

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)

UNIT = 2400                 # vertex units per degree, as the lake outlines
VERSION = 1                 # 2 with the canals (--canals): the same areas, and the canals' with the CANAL flag;
                            # 3 with --post2000: and works rules 4's areas, with the LATE flag
MAGIC = b"EMWK"
SEA, LAKE, LAND, FEN = 16, 32, 64, 128
CANAL = 8                   # decision 0069 rule 6: a canal dug after 1318 (with LAND); worlds of works rules 1 skip these
LATE = 4                    # works rules 4 (EA-0053, EA-0057): an area added in file version 3; worlds of rules 1-3 skip these
CANAL_HALF_M = 60           # a canal's half width where OpenStreetMap gives none
CANAL_PAD_M = 90            # and a GLO-90 pixel beyond it, so the mask's water along it is all inside
MIN_KM2 = 0.05              # smaller pieces are left out
SIMPLIFY_DEG = 1.0 / 6000   # ~18 m north-south: the maps' vertices are at 1e-4°
T0 = time.time()

RCE_SEA = {"buitenwater", "getijdenzone", "geul"}
RCE_MADE_2000 = {"droogmakerij", "ingedijkt", "kwelderrug-binnendijks", "stadcontour", "stad", "binnenwater"}
RCE_INLAND_WATER = {"binnenwater"}

# Works rules 4 (EA-0053): land made in the sea that the RCE map of 2000 does not show as made: (key, built, box lon0,
# lat0, lon1, lat1); everything in a box was sea in 1318
POST2000 = [
    ("maasvlakte", "1965-2013", (3.93, 51.92, 4.07, 52.01)),   # Rotterdam's port in the North Sea: Maasvlakte 2 and 1's dunes
]
POST2000_CLOSE_M = 600      # closed by this much: the basins and the dams' lagoons the mask has as water are in
POST2000_GROW_M = 150       # then grown by this much: the area's edge lies clear of the mask's land, not on it
POST2000_MIN_KM2 = 0.005    # the mask's smallest pieces of land (a few pixels of a dam) are the game's land too


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


def km2(g, lat):
    return shapely.area(g) * 111.32 * 111.32 * math.cos(math.radians(lat))


def load_classes(path):
    fc = json.load(open(path, encoding="utf8"))
    by = {}
    for f in fc["features"]:
        by.setdefault(f["properties"]["legenda"], []).append(shapely.make_valid(shape(f["geometry"])))
    return by


def union_of(by, names):
    parts = [g for n in names for g in by.get(n, [])]
    return shapely.union_all(parts) if parts else shapely.Polygon()


def pieces(g, lat, min_km2=MIN_KM2):
    """The polygons of g, simplified, without slivers and pieces under min_km2."""
    g = shapely.make_valid(shapely.simplify(g, SIMPLIFY_DEG, preserve_topology=True))
    # drop slivers: open by about 20 m and close again
    g = shapely.buffer(shapely.buffer(g, -SIMPLIFY_DEG, join_style="mitre"), SIMPLIFY_DEG, join_style="mitre")
    out = []
    for p in getattr(g, "geoms", [g]):
        if p.geom_type == "Polygon" and not p.is_empty and km2(p, lat) >= min_km2:
            out.append(p)
    return out


def rce_areas(d, report=None):
    log("reading the RCE maps")
    m = {y: load_classes(os.path.join(d, f"paleogeografie{y}nc.geojson")) for y in (1250, 1500, 2000)}
    sea = {y: union_of(m[y], RCE_SEA) for y in m}
    water = {y: union_of(m[y], RCE_INLAND_WATER) for y in m}
    land = {y: union_of(m[y], [k for k in m[y] if k not in RCE_SEA | RCE_INLAND_WATER | {"buitenland"}]) for y in m}
    made2000 = union_of(m[2000], RCE_MADE_2000)
    made_land2000 = union_of(m[2000], RCE_MADE_2000 - RCE_INLAND_WATER)
    log("intersecting")
    sea_g = shapely.intersection(shapely.union(sea[1500], shapely.difference(sea[1250], land[1500])), made2000)
    lake_g = shapely.difference(shapely.intersection(shapely.union(water[1250], water[1500]), made_land2000), sea_g)
    land_g = shapely.intersection(water[2000], shapely.intersection(land[1250], land[1500]))
    # dug-lake pieces touching the sea are the sea's own erosion of the peat (natural): sea
    shapely.prepare(sea_g)
    eroded = [p for p in getattr(land_g, "geoms", [land_g]) if shapely.dwithin(sea_g, p, 0.0005)]
    if eroded:
        e = shapely.union_all(eroded)
        sea_g, land_g = shapely.union(sea_g, e), shapely.difference(land_g, e)
        if report is not None:
            report["land_touching_sea_km2"] = round(km2(e, 52.3), 1)
    if report is not None:
        # how much of SEA is sea in 1250 but land by 1500 (embanked or silted in between: before or after 1318?)
        split = shapely.intersection(shapely.intersection(sea[1250], made2000), land[1500])
        late = shapely.difference(shapely.intersection(sea[1500], made2000), sea[1250])
        report["sea_1250_land_1500_km2"] = round(km2(split, 52.3), 1)
        report["sea_1500_only_km2"] = round(km2(late, 52.3), 1)
    out = []
    for kind, g in ((SEA, sea_g), (LAKE, lake_g), (LAND, land_g)):
        ps = pieces(g, 52.3)
        out += [(kind, 0, p) for p in ps]
        log(f"kind {kind}: {len(ps)} areas, {sum(km2(p, 52.3) for p in ps):.0f} km²")
    return out


def ring_units(coords, lon0, lat0):
    c = np.asarray(coords)[:-1]
    x = np.round((c[:, 0] - lon0) * UNIT).astype(np.int64)
    y = np.round((c[:, 1] - lat0) * UNIT).astype(np.int64)
    r = np.column_stack([x, y])
    keep = np.ones(len(r), bool)
    keep[1:] = np.any(np.diff(r, axis=0) != 0, axis=1)
    r = r[keep]
    if len(r) > 1 and (r[0] == r[-1]).all():
        r = r[:-1]
    return r if len(r) >= 3 else None


def zz(a):
    a = np.asarray(a, np.int64)
    return ((a << 1) ^ (a >> 63)).astype(np.uint64)


def varints(v):
    out = bytearray()
    for x in np.asarray(v, np.uint64).tolist():
        while True:
            b = x & 0x7F
            x >>= 7
            out.append(b | (0x80 if x else 0))
            if not x:
                break
    return bytes(out)


def area_bytes(level_dm, flags, rings):
    vals = [zz([level_dm]), np.array([flags, len(rings)], np.uint64)]
    px = py = 0
    for r in rings:
        vals.append(np.array([len(r)], np.uint64))
        d = np.diff(np.vstack([[px, py], r]), axis=0)
        px, py = int(r[-1, 0]), int(r[-1, 1])
        vals.append(zz(d.ravel()))
    return varints(np.concatenate(vals))


def write(areas, path):
    TX, TY = 360, 180
    tiles = {}
    for kind, level_dm, poly in areas:
        x0, y0, x1, y1 = poly.bounds
        for r in range(int(math.floor(y0)) + 90, int(math.floor(y1)) + 91):
            for c in range(int(math.floor(x0)) + 180, int(math.floor(x1)) + 181):
                lon0, lat0 = c - 180, r - 90
                part = shapely.clip_by_rect(poly, lon0, lat0, lon0 + 1, lat0 + 1)
                for p in getattr(part, "geoms", [part]):
                    if p.geom_type != "Polygon" or p.is_empty:
                        continue
                    p = shapely.geometry.polygon.orient(p)
                    rings = [ring_units(p.exterior.coords, lon0, lat0)]
                    if rings[0] is None:
                        continue
                    rings += [x for x in (ring_units(h.coords, lon0, lat0) for h in p.interiors) if x is not None]
                    tiles.setdefault(r * TX + c, []).append(area_bytes(level_dm, kind, rings))
    states = bytearray(TX * TY)
    for t in tiles:
        states[t] = 1
    blobs = {t: brotli.compress(varints([len(v)]) + b"".join(v), quality=11, lgwin=22) for t, v in sorted(tiles.items())}
    head = struct.pack("<4siiiii", MAGIC, VERSION, UNIT, 1, TX, TY) + bytes(states)
    off = len(head) + 8 * len(blobs)
    index, body = bytearray(), bytearray()
    for t, b in blobs.items():
        index += struct.pack("<ii", off + len(body), len(b))
        body += b
    data = head + bytes(index) + bytes(body)
    open(path, "wb").write(data)
    log(f"{path}: {len(tiles)} tiles, {sum(len(v) for v in tiles.values())} area pieces, {len(data):,} bytes")
    return data


def canal_width_m(tags):
    try:
        return float(str(tags.get("width", "")).split()[0].replace(",", "."))
    except (ValueError, IndexError):
        return None


def osm_canal(path):
    """A canal's lines (with OSM's width where it has one) and areas, from tools/fetch_osm_canals.py's JSON."""
    lines, polys = [], []
    for e in json.load(open(path, encoding="utf-8")).get("elements", []):
        tags = e.get("tags", {})
        if e["type"] == "way" and "geometry" in e:
            geoms = [e["geometry"]]
        elif e["type"] == "relation":
            geoms = [m["geometry"] for m in e.get("members", []) if "geometry" in m]
        else:
            geoms = []
        for g in geoms:
            c = [(q["lon"], q["lat"]) for q in g]
            if len(c) < 2:
                continue
            closed = len(c) >= 4 and c[0] == c[-1]
            if closed and (tags.get("water") == "canal" or tags.get("natural") == "water" or e["type"] == "relation"):
                polys.append(shapely.Polygon(c))
            else:
                lines.append((shapely.LineString(c), canal_width_m(tags)))
    return lines, polys


def canal_corridor(lines, polys):
    """The canal's lines buffered by their half width plus a pixel, and its areas by a pixel, in degrees (buffered in
    metres on a plane scaled at the canal's middle latitude)."""
    allg = [g for g, _ in lines] + polys
    lat = shapely.union_all(allg).centroid.y
    k = math.cos(math.radians(lat))

    def to_m(g):
        return shapely.transform(g, lambda xy: np.column_stack([xy[:, 0] * 111320 * k, xy[:, 1] * 111320]))

    parts = [to_m(g).buffer((w / 2 if w else CANAL_HALF_M) + CANAL_PAD_M) for g, w in lines]
    parts += [to_m(q.buffer(0)).buffer(CANAL_PAD_M) for q in polys]
    m = shapely.union_all(parts)
    return shapely.transform(m, lambda xy: np.column_stack([xy[:, 0] / (111320 * k), xy[:, 1] / 111320]))


def wbm_name(lat0, lon0):
    return f"Copernicus_DSM_COG_30_{'N' if lat0 >= 0 else 'S'}{abs(lat0):02d}_00_{'E' if lon0 >= 0 else 'W'}{abs(lon0):03d}_00_WBM.tif"


def canal_areas(canals_dir, wbm_dir, report=None):
    """Decision 0069 rule 6: LAND|CANAL areas where the water mask has river water (class 3) inside a canal's corridor.
    Lake water (class 2) along it stays: the natural lakes a canal passes through (the Kiel Canal's), which the mask
    tells from the canal's own water. Only river-class pixels become land."""
    import tifffile
    index = json.load(open(os.path.join(canals_dir, "index.json"), encoding="utf-8"))
    areas = []
    for c in index:
        path = os.path.join(canals_dir, c["key"] + ".json")
        if not os.path.exists(path):
            continue
        if c.get("undo") is False:
            log(f"canal {c['key']}: kept ({c['note']}; the fetch list says why)")
            continue
        lines, polys = osm_canal(path)
        if not lines and not polys:
            log(f"canal {c['key']}: nothing in OpenStreetMap")
            continue
        corr = canal_corridor(lines, polys)
        shapely.prepare(corr)
        x0, y0, x1, y1 = corr.bounds
        npx = 0
        for lat0 in range(int(math.floor(y0)), int(math.floor(y1)) + 1):
            for lon0 in range(int(math.floor(x0)), int(math.floor(x1)) + 1):
                tp = os.path.join(wbm_dir, wbm_name(lat0, lon0))
                if not os.path.exists(tp):
                    continue
                w = tifffile.imread(tp)
                hp, wp = w.shape
                r0 = max(0, int((lat0 + 1 - y1) * hp) - 1)
                r1 = min(hp, int((lat0 + 1 - y0) * hp) + 2)
                c0 = max(0, int((x0 - lon0) * wp) - 1)
                c1 = min(wp, int((x1 - lon0) * wp) + 2)
                if r0 >= r1 or c0 >= c1:
                    continue
                rr, cc = np.mgrid[r0:r1, c0:c1]
                lat = lat0 + 1 - (rr + 0.5) / hp
                lon = lon0 + (cc + 0.5) / wp
                inside = shapely.contains_xy(corr, lon, lat) & (w[r0:r1, c0:c1] == 3)
                if not inside.any():
                    continue
                ii, jj = np.nonzero(inside)
                boxes = [shapely.box(lon0 + (c0 + j) / wp, lat0 + 1 - (r0 + i + 1) / hp, lon0 + (c0 + j + 1) / wp, lat0 + 1 - (r0 + i) / hp)
                         for i, j in zip(ii, jj)]
                # (three quarters of a pixel more all round: the river outlines are contoured smooth from the mask, so a
                # LAND area cut to the pixels left slivers of their water along its edges)
                u = shapely.union_all(boxes).buffer(0.75 / hp, join_style="mitre").simplify(0.5 / hp)
                for g in getattr(u, "geoms", [u]):
                    if g.geom_type == "Polygon" and not g.is_empty:
                        areas.append((LAND | CANAL | (LATE if c.get("rules", 2) >= 4 else 0), 0, g))
                npx += len(ii)
        log(f"canal {c['key']} ({c['built']}): {npx} river pixels of the mask become land")
        if report is not None:
            report.setdefault("canals", {})[c["key"]] = {"built": c["built"], "pixels": npx}
    return areas


def post2000_areas(rce_dir, wbm_dir, report=None):
    """Works rules 4 (EA-0053): SEA|LATE areas where a named work made land in the sea of 1318 that the RCE map of 2000 does
    not show as made: the water mask's land there (and the water it encloses), cut to the sea of 1318."""
    import tifffile
    m = {y: load_classes(os.path.join(rce_dir, f"paleogeografie{y}nc.geojson")) for y in (1250, 1500)}
    sea = {y: union_of(m[y], RCE_SEA) for y in m}
    land1500 = union_of(m[1500], [k for k in m[1500] if k not in RCE_SEA | RCE_INLAND_WATER | {"buitenland"}])
    sea1318 = shapely.union(sea[1500], shapely.difference(sea[1250], land1500))
    areas = []
    for key, built, (x0, y0, x1, y1) in POST2000:
        box = shapely.box(x0, y0, x1, y1)
        o = shapely.intersection(sea1318, box)
        shapely.prepare(o)
        boxes = []
        for lat0 in range(int(math.floor(y0)), int(math.floor(y1)) + 1):
            for lon0 in range(int(math.floor(x0)), int(math.floor(x1)) + 1):
                tp = os.path.join(wbm_dir, wbm_name(lat0, lon0))
                if not os.path.exists(tp):
                    continue
                w = tifffile.imread(tp)
                hp, wp = w.shape
                rr, cc = np.mgrid[0:hp, 0:wp]
                ins = (w == 0) & shapely.contains_xy(o, lon0 + (cc + 0.5) / wp, lat0 + 1 - (rr + 0.5) / hp)
                ii, jj = np.nonzero(ins)
                boxes += [shapely.box(lon0 + j / wp, lat0 + 1 - (i + 1) / hp, lon0 + (j + 1) / wp, lat0 + 1 - i / hp)
                          for i, j in zip(ii, jj)]
        if not boxes:
            log(f"post-2000 {key}: no mask land on the sea of 1318")
            continue
        kx = 111320 * math.cos(math.radians((y0 + y1) / 2))

        def to_m(q):
            return shapely.transform(q, lambda xy: np.column_stack([xy[:, 0] * kx, xy[:, 1] * 111320]))

        def to_deg(q):
            return shapely.transform(q, lambda xy: np.column_stack([xy[:, 0] / kx, xy[:, 1] / 111320]))

        gm = to_m(shapely.union_all(boxes))
        gm = gm.buffer(POST2000_CLOSE_M, join_style="mitre").buffer(-POST2000_CLOSE_M, join_style="mitre")
        gm = gm.buffer(POST2000_GROW_M, join_style="mitre")
        g = shapely.make_valid(shapely.intersection(to_deg(gm), o))
        ps = pieces(g, (y0 + y1) / 2, POST2000_MIN_KM2)
        areas += [(SEA | LATE, 0, p) for p in ps]
        k = sum(km2(p, p.centroid.y) for p in ps)
        log(f"post-2000 {key} ({built}): {len(ps)} SEA areas, {k:.1f} km²")
        if report is not None:
            report.setdefault("post2000", {})[key] = {"built": built, "areas": len(ps), "km2": round(k, 2)}
    return areas


def main():
    global VERSION
    ap = argparse.ArgumentParser()
    ap.add_argument("--rce", required=True, help="folder with the RCE 1250 and 2000 AD layers (GeoJSON, EPSG:4326)")
    ap.add_argument("--out", required=True)
    ap.add_argument("--report")
    ap.add_argument("--canals", help="tools/fetch_osm_canals.py's folder: version 2 with the canals (decision 0069 rule 6)")
    ap.add_argument("--wbm", help="the GLO-90 water mask tiles (rawdata/copernicus-glo90-wbm)")
    ap.add_argument("--post2000", action="store_true", help="version 3: and works rules 4's areas (land made after 2000, "
                    "the canals the fetch list marks rules 4), flagged LATE")
    a = ap.parse_args()
    rep = {}
    areas = rce_areas(a.rce, rep)
    if a.canals:
        VERSION = 2
        areas += canal_areas(a.canals, a.wbm, rep)
    if a.post2000:
        VERSION = 3
        areas += post2000_areas(a.rce, a.wbm, rep)
    # works rules 4's areas after all older ones: each tile keeps version 2's pieces first and in their order
    areas = [x for x in areas if not x[0] & LATE] + [x for x in areas if x[0] & LATE]
    data = write(areas, a.out)
    if a.report:
        import hashlib
        rep.update({"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(), "kinds": {}})
        for kind, name in ((SEA, "sea"), (LAKE, "lake"), (LAND, "land"), (FEN, "fen")):
            ps = [p for k, _, p in areas if k & ~(CANAL | LATE) == kind]
            rep["kinds"][name] = {"areas": len(ps), "km2": round(sum(km2(p, p.centroid.y) for p in ps), 1),
                                  "largest": [{"lat": round(p.centroid.y, 4), "lon": round(p.centroid.x, 4),
                                               "km2": round(km2(p, p.centroid.y), 1)}
                                              for p in sorted(ps, key=lambda p: -p.area)[:12]]}
        json.dump(rep, open(a.report, "w"), indent=1)
        log(json.dumps({k: (v["areas"], v["km2"]) for k, v in rep["kinds"].items()}))
        log(f"sea in 1250 but land by 1500: {rep['sea_1250_land_1500_km2']} km²; sea only in 1500: {rep['sea_1500_only_km2']} km²; "
            f"today's water on 1500 land touching the sea: {rep.get('land_touching_sea_km2', 0)} km²")


if __name__ == "__main__":
    main()
