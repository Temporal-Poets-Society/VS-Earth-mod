"""Macrostrat carto tiles -> per-tile polygons in degrees with a GLiM-style code (macrostrat_codes.py) and their source.

Usage: python -I tools/ground/macrostrat_units.py <carto tile dir> <sources.json> <out dir> [--workers N]
<carto tile dir> holds z_x_y.mvt from tiles.macrostrat.org/carto (CC BY 4.0); <sources.json> is the API's
/defs/sources?all answer (each source's scale). Keeps units of sources at 1:1M or finer ("medium", "large") whose words name
a rock; writes <out dir>/z_x_y.bin per tile: records of uint16 code index (bit 15: weak, the unit's words name no rock), uint32 source id, uint32 WKB length, WKB
(polygons in lon/lat, clipped to the tile), and <out dir>/codes.json with the code list.
"""
import os, sys, json, glob, math, struct, argparse
import shapely
from shapely.geometry import Polygon, box
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mvt, macrostrat_codes as M

FINE = ("medium", "large")
WEAK = 0x8000   # code index flag: the unit's words name no rock (only its lithology types do)


def _area(r):
    return sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(r, r[1:] + r[:1])) / 2


def polygons(rings):
    """MVT rings (tile pixels, y down): positive area opens a polygon, negative ones are its holes."""
    out, shell, holes = [], None, []
    for r in rings:
        if len(r) < 3:
            continue
        a = _area(r)
        if a > 0:
            if shell:
                out.append((shell, holes))
            shell, holes = r, []
        elif a < 0 and shell:
            holes.append(r)
    if shell:
        out.append((shell, holes))
    return [Polygon(s, h) for s, h in out]


def convert(args):
    path, scale, out_dir = args
    name = os.path.basename(path)[:-4]
    z, x, y = (int(v) for v in name.split("_"))
    n = 2 ** z
    L = mvt.decode(open(path, "rb").read())
    lay = L.get("units")
    recs = []
    if lay:
        ext = lay["extent"]
        clip = box(0, 0, ext, ext)

        def to_deg(c):
            lon = (x + c[:, 0] / ext) / n * 360 - 180
            lat = [math.degrees(math.atan(math.sinh(math.pi * (1 - 2 * (y + v / ext) / n)))) for v in c[:, 1]]
            import numpy as np
            return np.column_stack([lon, np.array(lat)])

        for f in lay["features"]:
            p = f["props"]
            if f["type"] != 3 or scale.get(p.get("source_id")) not in FINE:
                continue
            c = M.code(p.get("lith"), [p.get(f"lith_type{i}") for i in range(1, 5)])
            if not c:
                continue
            g = shapely.make_valid(shapely.MultiPolygon(polygons(f["rings"]))) if f["rings"] else None
            if g is None or g.is_empty:
                continue
            g = g.intersection(clip)
            if g.is_empty or g.area <= 0:
                continue
            recs.append((c, int(p["source_id"]), shapely.to_wkb(shapely.transform(g, to_deg)), M.named(p.get("lith"))))
    return name, recs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("tiles"); ap.add_argument("sources"); ap.add_argument("out")
    ap.add_argument("--workers", type=int, default=4)
    a = ap.parse_args()
    scale = {s["source_id"]: s["scale"] for s in json.load(open(a.sources, encoding="utf-8"))["success"]["data"]}
    os.makedirs(a.out, exist_ok=True)
    jobs = [(p, scale, a.out) for p in sorted(glob.glob(os.path.join(a.tiles, "*.mvt")))]
    results = {}
    from multiprocessing import Pool
    with Pool(a.workers) as pool:
        for i, (name, recs) in enumerate(pool.imap_unordered(convert, jobs, chunksize=8)):
            results[name] = recs
            if (i + 1) % 1000 == 0:
                print(f"  {i + 1} tiles", flush=True)
    codes = sorted({c for recs in results.values() for c, _, _, _ in recs})
    idx = {c: i for i, c in enumerate(codes)}
    units = 0
    for name, recs in results.items():
        with open(os.path.join(a.out, name + ".bin"), "wb") as f:
            for c, s, w, strong in recs:
                f.write(struct.pack("<HII", idx[c] | (0 if strong else WEAK), s, len(w))); f.write(w)
        units += len(recs)
    sources = sorted({s for recs in results.values() for _, s, _, _ in recs})
    json.dump({"codes": codes, "sources": sources}, open(os.path.join(a.out, "codes.json"), "w", encoding="utf-8"))
    print(f"{len(results)} tiles, {units} units, {len(codes)} codes, {len(sources)} sources")


def read(out_dir, name):
    """Records of one tile: (code index, source id, shapely geometry, strong): strong when the unit's words name a rock."""
    p = os.path.join(out_dir, name + ".bin")
    if not os.path.exists(p):
        return []
    b = open(p, "rb").read(); o = 0; out = []
    while o < len(b):
        c, s, n = struct.unpack_from("<HII", b, o); o += 10
        out.append((c & ~WEAK, s, shapely.from_wkb(b[o:o + n]), not c & WEAK)); o += n
    return out


if __name__ == "__main__":
    main()
