"""
Earth decision 0070: shores-osm-1.bin, OpenStreetMap's beaches by material on a 6" grid, for the shore strip's material
(src/Core/ShoreMaterial.cs).

Input: tools/fetch_osm_beaches.py's beaches.tsv (natural=beach and natural=shingle, with surface, fixme and WKT; ODbL).
Each beach's class (ShoreMaterial.OfSurface, the same table as the game's):

  1 sand, 2 gravel (gravel, fine_gravel, pebblestone, pebbles, shingle, shells, stones; natural=shingle without a surface),
  3 sand and gravel or stones together, 4 rock (rock, stone, bare_rock, boulders), 5 mud (mud, clay, dirt, silt),
  6 a beach without a surface the table knows.

The Canadian CanVec import's "beaches" tagged fixme=could be dunes or raised_beach (56,000, mostly inland) are left out.
Each 1-degree square that has a beach gets a CPD x CPD byte raster (rows from the north): polygons filled, lines one cell
wide, points one cell; untagged beaches drawn first, tagged ones over them; then every empty cell beside a beach takes its
class (one cell grown), so the terrain's own shore line, which may lie a cell off OSM's, finds it.

  "EMSH", int32 version 1, int32 CPD, int32 squares; per square int16 south, int16 west, int32 offset, int32 length; then
  each square's brotli raster. Little-endian.

    python -I tools/bake_shore_material.py rawdata/osm-beaches/beaches.tsv earthdata/shores-osm-1.bin
"""
import collections, math, struct, sys
import brotli
import numpy as np
from PIL import Image, ImageDraw
import shapely.wkt
from shapely.geometry import Point, LineString, Polygon, MultiPolygon, MultiLineString, GeometryCollection, MultiPoint

CPD = 600
VERSION = 1
SAND, GRAVEL, MIXED, ROCK, MUD, BEACH = 1, 2, 3, 4, 5, 6
SKIP_FIXME = "could be dunes or raised_beach"


def of_surface(surface, shingle):
    if not surface:
        return GRAVEL if shingle else BEACH
    sand = gravel = rock = mud = False
    for raw in surface.split(";"):
        t = raw.strip()
        if t in ("sand", "fine_sand", "coarse_sand"):
            sand = True
        elif t in ("gravel", "fine_gravel", "pebblestone", "pebbles", "shingle", "shells", "stones"):
            gravel = True
        elif t in ("rock", "stone", "bare_rock", "boulders", "rocks"):
            rock = True
        elif t in ("mud", "clay", "dirt", "silt"):
            mud = True
    if sand and (gravel or rock):
        return MIXED
    if sand:
        return SAND
    if gravel:
        return MIXED if rock else GRAVEL
    if rock:
        return ROCK
    if mud:
        return MUD
    return GRAVEL if shingle else BEACH


def unq(v):
    v = v.strip()
    if v.startswith('"'):
        v = v[1:v.rindex('"')] if v.count('"') >= 2 else v[1:]
    return v.lower()


def parts(g):
    if isinstance(g, (MultiPolygon, MultiLineString, MultiPoint, GeometryCollection)):
        for p in g.geoms:
            yield from parts(p)
    else:
        yield g


class Squares:
    def __init__(self):
        self.img = {}

    def draw(self, key):
        if key not in self.img:
            self.img[key] = Image.new("L", (CPD, CPD), 0)
        return ImageDraw.Draw(self.img[key])


def px(lat_s, lon_w, x, y):
    return ((x - lon_w) * CPD, (lat_s + 1 - y) * CPD)


def put(sq, g, cls):
    minx, miny, maxx, maxy = g.bounds
    for lat_s in range(math.floor(miny), math.floor(maxy) + 1):
        for lon_w in range(math.floor(minx), math.floor(maxx) + 1):
            if lat_s < -90 or lat_s > 89 or lon_w < -180 or lon_w > 179:
                continue
            d = sq.draw((lat_s, lon_w))
            if isinstance(g, Polygon):
                d.polygon([px(lat_s, lon_w, x, y) for x, y in g.exterior.coords], fill=cls, outline=cls)
                for hole in g.interiors:
                    pass  # holes (islands in a beach) are rare and smaller than a cell or two; kept filled
            elif isinstance(g, LineString):
                d.line([px(lat_s, lon_w, x, y) for x, y in g.coords], fill=cls, width=1)
            elif isinstance(g, Point):
                x, y = px(lat_s, lon_w, g.x, g.y)
                d.point((x, y), fill=cls)


def grow(a):
    out = a.copy()
    h, w = a.shape
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dy == 0 and dx == 0:
                continue
            sh = np.zeros_like(a)
            ys, yd = (slice(max(0, dy), h + min(0, dy)), slice(max(0, -dy), h + min(0, -dy)))
            xs, xd = (slice(max(0, dx), w + min(0, dx)), slice(max(0, -dx), w + min(0, -dx)))
            sh[yd, xd] = a[ys, xs]
            fill = (out == 0) & (sh != 0)
            out[fill] = sh[fill]
    return out


def main():
    src, dst = sys.argv[1], sys.argv[2]
    rows = []
    counts = collections.Counter()
    skipped = bad = 0
    with open(src, encoding="utf-8") as f:
        f.readline()
        for line in f:
            c = line.rstrip("\n").split("\t")
            if len(c) < 5:
                bad += 1
                continue
            natural, surface, fixme, wkt = unq(c[1]), unq(c[2]), unq(c[3]), c[4].strip()
            if fixme == SKIP_FIXME:
                skipped += 1
                continue
            if wkt.startswith('"'):
                wkt = wkt[1:wkt.index('"', 1)]
            cls = of_surface(surface, natural == "shingle")
            rows.append((cls, wkt))
            counts[cls] += 1
    print(f"{len(rows)} beaches ({skipped} CanVec raised beaches left out, {bad} bad rows); by class {dict(sorted(counts.items()))}", flush=True)
    sq = Squares()
    # untagged first, tagged over them
    for pass_untagged in (True, False):
        for cls, wkt in rows:
            if (cls == BEACH) != pass_untagged:
                continue
            try:
                g = shapely.wkt.loads(wkt)
            except Exception:
                bad += 1
                continue
            for p in parts(g):
                if not p.is_empty:
                    put(sq, p, cls)
    keys = sorted(sq.img)
    blobs = []
    cells = 0
    for k in keys:
        a = grow(np.asarray(sq.img[k], dtype=np.uint8))
        cells += int((a != 0).sum())
        blobs.append(brotli.compress(a.tobytes(), quality=11, lgwin=22))
    head = 16 + 12 * len(keys)
    out = bytearray(b"EMSH" + struct.pack("<iii", VERSION, CPD, len(keys)))
    off = head
    for (lat_s, lon_w), b in zip(keys, blobs):
        out += struct.pack("<hhii", lat_s, lon_w, off, len(b))
        off += len(b)
    for b in blobs:
        out += b
    with open(dst, "wb") as f:
        f.write(out)
    print(f"{len(keys)} squares, {cells} cells with a beach; {len(out)} bytes -> {dst}")


if __name__ == "__main__":
    main()
