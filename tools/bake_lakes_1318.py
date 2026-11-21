"""
Lakes of 1318 (decision 0064 step 3, decision 0107, EA-0124): earthdata/works-1318-4.bin, file version 5 of the works
of 1318 (tools/bake_works_1318.py): works-1318-3.bin's areas byte for byte and in their order in every tile, then the
areas of tools/lakes-1318.json after them:

  LAKE (32, with 1 for salt water) a lake of 1318 at its level, drained since (the Valley of Mexico, Fucino, Copais, the
       Aral at its 14th-century low, ...)
  LAND (64) dry land in 1318 under water today (the Salton Sea, made in 1905-1907)

An area's outline is an OpenHistoricalMap (CC0) or OpenStreetMap (ODbL) element kept in tools/lakes-1318/ by
tools/fetch_lakes_1318.py, or a fill of GLO-90: the Copernicus DEM tiles (rawdata/copernicus-glo90, pixel-is-point as
tools/glo90grid.py has them) below a height, connected to a seed within a box, contoured at that height (so the shore
lies where GLO-90's ground meets the level, between its cells), its islands of 0.05 km² and more kept. Works rules 6 worlds
read this file; works rules 5 worlds keep works-1318-3.bin, which this file leaves as it was.

    .venv/Scripts/python -I tools/bake_lakes_1318.py --works earthdata/works-1318-3.bin --out earthdata/works-1318-4.bin
        [--glo90 rawdata/copernicus-glo90] [--report FILE.json] [--geojson FILE]
"""
import argparse, hashlib, json, math, os, struct, sys

import brotli
import contourpy
import numpy as np
import shapely
from scipy import ndimage as ndi
from shapely.geometry import Polygon, MultiPolygon, shape, mapping

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)

import bake_works_1318 as bw  # noqa: E402
import glo90grid as gg  # noqa: E402

VERSION = 5
LAKE, LAND, SALT = bw.LAKE, bw.LAND, 1
MIN_KM2 = 0.05
SIMPLIFY_DEG = 1.0 / 9600     # about 12 m: half the file's unit
RAW = os.environ.get("EARTH_RAW", os.path.join(TOOLS, "..", "rawdata"))


def km2(g, lat):
    return g.area * 111.32 ** 2 * math.cos(math.radians(lat))


# ------------------------------------------------------------------ GLO-90 heights


class Glo90:
    """The Copernicus DEM's 1° tiles, on GLO-90's pixel-is-point grid (1,200 rows a degree; 1,200 columns below 50°)."""

    def __init__(self, folder):
        self.folder = folder
        self.cache = {}

    def tile(self, lat0, lon0):
        k = (lat0, lon0)
        if k not in self.cache:
            name = f"Copernicus_DSM_COG_30_{'N' if lat0 >= 0 else 'S'}{abs(lat0):02d}_00_{'E' if lon0 >= 0 else 'W'}{abs(lon0):03d}_00_DEM.tif"
            path = os.path.join(self.folder, name)
            if os.path.exists(path):
                import tifffile
                t = tifffile.imread(path).astype(np.float32)
            else:
                t = None   # open sea
            self.cache[k] = t
        return self.cache[k]

    def mosaic(self, lon0, lat0, lon1, lat1):
        """Heights over whole degrees covering the box: the array (north row first), and the latitude of row 0 and the
        longitude of column 0 (cell centres: lat = top - r/1200, lon = left + c/1200)."""
        la0, la1 = math.floor(lat0), math.ceil(lat1)
        lo0, lo1 = math.floor(lon0), math.ceil(lon1)
        rows, cols = (la1 - la0) * 1200, (lo1 - lo0) * 1200
        h = np.full((rows, cols), np.nan, np.float32)
        for la in range(la0, la1):
            for lo in range(lo0, lo1):
                t = self.tile(la, lo)
                if t is None:
                    continue
                if t.shape != (1200, 1200):
                    raise SystemExit(f"tile {la} {lo}: {t.shape}, expected 1200 x 1200 (a fill above 50 degrees needs its columns resampled)")
                r = (la1 - 1 - la) * 1200
                c = (lo - lo0) * 1200
                h[r:r + 1200, c:c + 1200] = t
        return h, float(la1), float(lo0)

    def at(self, lat, lon):
        la0, lo0 = math.floor(lat), math.floor(lon)
        t = self.tile(la0, lo0)
        if t is None:
            return float("nan")
        r = min(1199, max(0, int(round((la0 + 1 - lat) * 1200))))
        c = min(t.shape[1] - 1, max(0, int(round((lon - lo0) * t.shape[1]))))
        return float(t[r, c])


def fill_outline(g, spec, log):
    lon0, lat0, lon1, lat1 = spec["box"]
    below = spec["below"]
    h, top, left = g.mosaic(lon0, lat0, lon1, lat1)
    lat = top - np.arange(h.shape[0]) / 1200.0
    lon = left + np.arange(h.shape[1]) / 1200.0
    inbox = ((lat >= lat0) & (lat <= lat1))[:, None] & ((lon >= lon0) & (lon <= lon1))[None, :]
    low = np.nan_to_num(h, nan=1e9) < below
    lab, _ = ndi.label(low & inbox)
    sr = int(round((top - spec["seed"][0]) * 1200)); sc = int(round((spec["seed"][1] - left) * 1200))
    if lab[sr, sc] == 0:
        win = np.where(lab[sr - 5:sr + 6, sc - 5:sc + 6] > 0)
        if len(win[0]) == 0:
            raise SystemExit(f"fill: the seed {spec['seed']} is not below {below} m")
        sr, sc = sr - 5 + win[0][0], sc - 5 + win[1][0]
    comp = lab == lab[sr, sc]
    rr, cc = np.where(comp)
    edge = bool(rr.min() == 0 or cc.min() == 0 or rr.max() == h.shape[0] - 1 or cc.max() == h.shape[1] - 1
                or not inbox[rr.min(), cc.min()] or lat[rr.min()] >= lat1 - 1 / 1200 or lat[rr.max()] <= lat0 + 1 / 1200
                or lon[cc.min()] <= lon0 + 1 / 1200 or lon[cc.max()] >= lon1 - 1 / 1200)
    if edge:
        log(f"  warning: the fill reaches its box's edge")
    # contour the component's own heights at the level: every other cell stands above it
    z = np.where(comp, np.nan_to_num(h, nan=below + 100.0), below + 100.0).astype(np.float64)
    r0, r1 = max(rr.min() - 2, 0), min(rr.max() + 3, h.shape[0])
    c0, c1 = max(cc.min() - 2, 0), min(cc.max() + 3, h.shape[1])
    gen = contourpy.contour_generator(lon[c0:c1], lat[r0:r1], z[r0:r1, c0:c1], fill_type="OuterCode")
    pts, codes = gen.filled(-1e12, below)
    polys = []
    for p, cd in zip(pts, codes):
        starts = list(np.where(cd == 1)[0]) + [len(cd)]
        rings = [p[starts[i]:starts[i + 1]] for i in range(len(starts) - 1)]
        rings = [r for r in rings if len(r) >= 4]
        if not rings:
            continue
        outer = Polygon(rings[0])
        holes = [r for r in rings[1:] if km2(Polygon(r), outer.centroid.y) >= MIN_KM2]
        polys.append(Polygon(rings[0], holes).buffer(0))
    geom = shapely.unary_union([p for p in polys if km2(p, p.centroid.y) >= MIN_KM2])
    if spec.get("grow_m"):
        geom = grow(geom, spec["grow_m"])
    return geom


def grow(geom, metres):
    c = math.cos(math.radians(geom.centroid.y))
    local = shapely.affinity.scale(geom, xfact=c, yfact=1.0, origin=(0, 0))
    local = local.buffer(metres / 111320.0, join_style="round")
    return shapely.affinity.scale(local, xfact=1.0 / c, yfact=1.0, origin=(0, 0))


def clean(geom):
    """Pieces and holes under MIN_KM2 left out, simplified to half the file's unit."""
    out = []
    for p in getattr(geom, "geoms", [geom]):
        if p.geom_type != "Polygon" or km2(p, p.centroid.y) < MIN_KM2:
            continue
        holes = [h for h in p.interiors if km2(Polygon(h), p.centroid.y) >= MIN_KM2]
        out.append(Polygon(p.exterior, holes))
    g = shapely.unary_union(out).simplify(SIMPLIFY_DEG, preserve_topology=True)
    return g


def edge_heights(g, geom, step_deg=1.0 / 1200):
    """GLO-90's heights along the outline (every cell's width) and inside it (a 1/600° lattice)."""
    shore = []
    for p in getattr(geom, "geoms", [geom]):
        for ring in [p.exterior] + list(p.interiors):
            n = max(8, int(ring.length / step_deg))
            for t in np.linspace(0, 1, n, endpoint=False):
                q = ring.interpolate(t, normalized=True)
                shore.append(g.at(q.y, q.x))
    x0, y0, x1, y1 = geom.bounds
    ins = []
    shapely.prepare(geom)
    for la in np.arange(y0, y1, 1.0 / 600):
        for lo in np.arange(x0, x1, 1.0 / 600):
            if geom.contains(shapely.Point(lo, la)):
                ins.append(g.at(la, lo))
    shore, ins = np.array(shore), np.array(ins)
    pct = lambda a, q: float(np.nanpercentile(a, q)) if len(a) and np.isfinite(a).any() else float("nan")
    return {"shore_p10": pct(shore, 10), "shore_median": pct(shore, 50), "shore_p90": pct(shore, 90),
            "inside_p10": pct(ins, 10), "inside_median": pct(ins, 50), "inside_p90": pct(ins, 90)}


# ------------------------------------------------------------------ the works file


def read_works(path):
    data = open(path, "rb").read()
    magic, version, unit, deg, tx, ty = struct.unpack_from("<4siiiii", data, 0)
    if magic != bw.MAGIC or deg != 1:
        raise SystemExit(f"{path}: not a works file")
    states = data[24:24 + tx * ty]
    o = 24 + tx * ty
    tiles = {}
    for t in range(tx * ty):
        if states[t] != 1:
            continue
        off, ln = struct.unpack_from("<ii", data, o)
        o += 8
        raw = brotli.decompress(data[off:off + ln])
        count, k = 0, 0
        shift = 0
        while True:
            b = raw[k]; k += 1
            count |= (b & 0x7F) << shift
            shift += 7
            if not b & 0x80:
                break
        tiles[t] = (count, raw[k:], data[off:off + ln])
    return version, unit, tx, ty, tiles


def pieces(kind, level_dm, geom):
    """The areas' records by tile, cut at the 1° tiles as bake_works_1318.write cuts them."""
    out = {}
    for poly in getattr(geom, "geoms", [geom]):
        x0, y0, x1, y1 = poly.bounds
        for r in range(int(math.floor(y0)) + 90, int(math.floor(y1)) + 91):
            for c in range(int(math.floor(x0)) + 180, int(math.floor(x1)) + 181):
                lon0, lat0 = c - 180, r - 90
                part = shapely.clip_by_rect(poly, lon0, lat0, lon0 + 1, lat0 + 1)
                for p in getattr(part, "geoms", [part]):
                    if p.geom_type != "Polygon" or p.is_empty:
                        continue
                    p = shapely.geometry.polygon.orient(p)
                    rings = [bw.ring_units(p.exterior.coords, lon0, lat0)]
                    if rings[0] is None:
                        continue
                    rings += [x for x in (bw.ring_units(h.coords, lon0, lat0) for h in p.interiors) if x is not None]
                    out.setdefault(r * 360 + c, []).append(bw.area_bytes(level_dm, kind, rings))
    return out


def write(version_in, unit, tx, ty, tiles, new, path):
    if unit != bw.UNIT:
        raise SystemExit(f"the works file has {unit} units a degree, this bake {bw.UNIT} (EARTH_GLO90_GRID?)")
    states = bytearray(tx * ty)
    blobs = {}
    for t in sorted(set(tiles) | set(new)):
        states[t] = 1
        if t not in new:
            blobs[t] = tiles[t][2]      # the tile as it was, its compressed bytes too
            continue
        count, rest = tiles[t][:2] if t in tiles else (0, b"")
        blobs[t] = brotli.compress(bw.varints([count + len(new[t])]) + rest + b"".join(new[t]), quality=11, lgwin=22)
    head = struct.pack("<4siiiii", bw.MAGIC, VERSION, unit, 1, tx, ty) + bytes(states)
    off = len(head) + 8 * len(blobs)
    index, body = bytearray(), bytearray()
    for t, b in blobs.items():
        index += struct.pack("<ii", off + len(body), len(b))
        body += b
    data = head + bytes(index) + bytes(body)
    open(path, "wb").write(data)
    return data


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--works", required=True, help="works-1318-3.bin (file version 4)")
    ap.add_argument("--out", required=True)
    ap.add_argument("--glo90", default=os.path.join(RAW, "copernicus-glo90"))
    ap.add_argument("--list", default=os.path.join(TOOLS, "lakes-1318.json"))
    ap.add_argument("--report")
    ap.add_argument("--geojson", help="write the areas as GeoJSON too (for maps and checks)")
    a = ap.parse_args()
    log = lambda m: print(m, flush=True)
    version, unit, tx, ty, tiles = read_works(a.works)
    if version != 4:
        raise SystemExit(f"{a.works}: file version {version}, expected 4 (works-1318-3.bin)")
    log(f"{a.works}: version {version}, {len(tiles)} tiles, {sum(v[0] for v in tiles.values())} areas")
    g = Glo90(a.glo90)
    spec = json.load(open(a.list, encoding="utf-8"))["areas"]
    new, report, features = {}, [], []
    for s in spec:
        src = s["outline"]
        if "fill" in src:
            geom = fill_outline(g, src["fill"], log)
        else:
            gj = json.load(open(os.path.join(TOOLS, "lakes-1318", s["key"] + ".geojson"), encoding="utf-8"))
            geom = shapely.unary_union([shape(f["geometry"]) for f in gj["features"]]).buffer(0)
            west = src.get("drop_holes_west_of")
            if west is not None:
                # islands of a later age (Tenochtitlan's, from 1325) are lake in 1318: their holes go
                geom = shapely.unary_union([Polygon(p.exterior, [h for h in p.interiors if Polygon(h).centroid.x >= west])
                                            for p in getattr(geom, "geoms", [geom])])
        geom = clean(geom)
        kind = LAKE | (SALT if s.get("salt") else 0) if s["kind"] == "lake" else LAND
        level = s.get("level", 0.0) if s["kind"] == "lake" else 0.0
        for t, recs in pieces(kind, int(round(level * 10)), geom).items():
            new.setdefault(t, []).extend(recs)
        parts = list(getattr(geom, "geoms", [geom]))
        stats = edge_heights(g, geom)
        rec = {"key": s["key"], "kind": s["kind"], "level": level, "salt": bool(s.get("salt")),
               "km2": round(sum(km2(p, p.centroid.y) for p in parts), 1), "parts": len(parts),
               "holes": sum(len(p.interiors) for p in parts), "lat": round(geom.centroid.y, 4), "lon": round(geom.centroid.x, 4),
               **{k: round(v, 1) for k, v in stats.items()}}
        report.append(rec)
        features.append({"type": "Feature", "properties": rec, "geometry": mapping(geom)})
        log(f"{s['key']}: {rec['kind']} {level} m, {rec['km2']} km², {rec['parts']} parts, {rec['holes']} holes; GLO-90 shore median "
            f"{rec['shore_median']} (p10 {rec['shore_p10']}, p90 {rec['shore_p90']}), inside median {rec['inside_median']}")
    data = write(version, unit, tx, ty, tiles, new, a.out)
    sha = hashlib.sha256(data).hexdigest()
    log(f"{a.out}: version {VERSION}, {len(set(tiles) | set(new))} tiles, {sum(len(v) for v in new.values())} new area pieces, {len(data):,} bytes, sha256 {sha}")
    if a.report:
        json.dump({"bytes": len(data), "sha256": sha, "areas": report}, open(a.report, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    if a.geojson:
        json.dump({"type": "FeatureCollection", "features": features}, open(a.geojson, "w", encoding="utf-8"), ensure_ascii=False)


if __name__ == "__main__":
    main()
