"""Bake full-resolution GLiM (Hartmann & Moosdorf 2012, the 1.2 million polygon geodatabase) to a 30" rock tile set.

Usage: python -I tools/ground/bake_rock_30s.py <GLiM .gdb folder> <output .egts> [--macro <units dir> --sources <json>]
       [--tiles W,S W,S ...] [--workers N]

Geology rules 4 (EA-0198) read the rock from this set instead of GLiM's 0.5-degree grid. Each 10-degree tile (1200 x 1200
cells, row 0 at the north edge) holds two category fields whose legend is GLiM's own 6-letter Litho code (437 codes; the
rules map codes to profiles, so a change of mapping needs no new bake):

  litho    the code GLiM maps at the cell's centre: bedrock, unconsolidated cover (su...), water (wb), ice (ig) or no
           data (nd); nodata (65535) off GLiM's polygons (sea, and coastal slivers where GLiM's coast lies inland of ours).
  macro    Macrostrat's unit (carto tiles at zoom 7, CC BY 4.0) in the same code vocabulary (macrostrat_codes.py), only
           where a national map at 1:1M or finer covers the cell (macrostrat_units.py): a unit whose words name a rock
           everywhere, one known only by its lithology types only where GLiM maps no bedrock; nodata elsewhere. The surface code is macro where there is one, else litho.
  source   the Macrostrat source (legend "macrostrat-<source_id>") that drew macro; nodata where macro is.
  bedrock  only where the surface code is not bedrock (cover, water, ice, no data, or off the polygons): the code of the nearest
           bedrock cell within FILL_CELLS (rules 3's coveredFill, decision 0101, at 30"); nodata elsewhere, meaning "the
           same as litho", which keeps the set small.

Cells take the polygon under their centre (no majority over sub-cells): GLiM's smallest polygons are a few km across,
coarser than the cell. Each tile is rasterized with a halo of FILL_CELLS so the bedrock fill does not stop at tile edges
(the halo is clamped at the poles and the antimeridian). Under cover the bedrock is, in this order: Macrostrat's bedrock
at the cell, GLiM's bedrock at the cell, the nearest of either within FILL_CELLS.
"""
import os, sys, json, math, time, argparse
os.environ.setdefault("OGR_ORGANIZE_POLYGONS", "ONLY_CCW")
import numpy as np
import pyogrio.raw as pr
import shapely
from pyproj import Transformer
import rasterio.features
from rasterio.transform import from_origin
from scipy.ndimage import distance_transform_edt

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tiles
import macrostrat_units as MU

STEP, TILE = 1 / 120, 1200
FILL_CELLS = 120            # 1 degree, rules 3's coveredFillRadiusCells (2 half-degree cells) rounded down
LAYER = "GLiM_export"
NOT_BEDROCK = ("su", "wb", "ig", "nd")
SOURCE = ("GLiM, Global Lithological Map (Hartmann & Moosdorf 2012, doi:10.1029/2012GC004370), full-resolution polygons "
          "(LiMW_GIS 2015), Litho code at each 30\" cell centre")
LICENCE = "GLiM (Hartmann & Moosdorf 2012); terms of the full-resolution file: see EA-0198"
MACRO_SOURCE = "Macrostrat (macrostrat.org) carto map, zoom 7, national and regional maps at 1:1M or finer; each source in ATTRIBUTION"
MACRO_LICENCE = "CC BY 4.0 (Macrostrat and each source map)"
SCALE_ORDER = {"medium": 0, "large": 1}   # finer maps drawn last, over coarser ones
NODATA = 65535


def legend(gdb):
    _, _, _, (lith,) = pr.read(gdb, layer=LAYER, read_geometry=False, columns=["Litho"])
    return sorted({l for l in lith if l})


def z7_names(w0, e0, s0, n0, z=7):
    n = 2 ** z

    def ty(lat):
        r = math.radians(max(-85.05, min(85.05, lat)))
        return int((1 - math.log(math.tan(r) + 1 / math.cos(r)) / math.pi) / 2 * n)
    x0, x1 = int((w0 + 180) / 360 * n), min(n - 1, int((e0 + 180) / 360 * n))
    return [f"{z}_{x}_{y}" for y in range(ty(n0), min(n - 1, ty(s0)) + 1) for x in range(x0, x1 + 1)]


def macro_grid(macro, w0, e0, s0, n0, nx, ny):
    """Macrostrat codes (legend indices) and sources (indices) on the window, strong units and weak ones (their words
    name no rock) apart; NODATA where none."""
    d, codes_to_legend, source_index, scale, _ = macro
    recs = [r for name in z7_names(w0, e0, s0, n0) for r in MU.read(d, name)]
    recs.sort(key=lambda r: SCALE_ORDER.get(scale.get(r[1]), 0))
    tr = from_origin(w0, n0, STEP, STEP)
    out = []
    for strong in (True, False):
        rs = [r for r in recs if r[3] == strong]
        if not rs:
            out += [np.full((ny, nx), NODATA, np.uint16)] * 2
            continue
        out.append(rasterio.features.rasterize([(g, codes_to_legend[c]) for c, s, g, _ in rs], out_shape=(ny, nx), transform=tr, fill=NODATA, dtype="uint16"))
        out.append(rasterio.features.rasterize([(g, source_index[s]) for c, s, g, _ in rs], out_shape=(ny, nx), transform=tr, fill=NODATA, dtype="uint16"))
    return out


def tile_blob(gdb, codes, west, south, macro=None):
    index = {c: i for i, c in enumerate(codes)}
    north = south + 10
    h = FILL_CELLS * STEP
    w0, e0 = max(-180.0, west - h), min(180.0, west + 10 + h)
    s0, n0 = max(-90.0, south - h), min(90.0, north + h)
    nx, ny = round((e0 - w0) / STEP), round((n0 - s0) / STEP)
    fwd = Transformer.from_crs("EPSG:4326", "ESRI:54012", always_xy=True)
    t = np.linspace(0, 1, 201)
    lon = np.concatenate([w0 + (e0 - w0) * t, np.full_like(t, e0), e0 - (e0 - w0) * t, np.full_like(t, w0)])
    lat = np.concatenate([np.full_like(t, s0), s0 + (n0 - s0) * t, np.full_like(t, n0), n0 - (n0 - s0) * t])
    x, y = fwd.transform(lon, lat)
    _, _, wkb, (lith,) = pr.read(gdb, layer=LAYER, columns=["Litho"], bbox=(x.min(), y.min(), x.max(), y.max()))
    grid = np.full((ny, nx), NODATA, np.uint16)
    if len(wkb):
        back = Transformer.from_crs("ESRI:54012", "EPSG:4326", always_xy=True)
        geoms = shapely.transform(shapely.from_wkb(wkb), lambda c: np.column_stack(back.transform(c[:, 0], c[:, 1])))
        shapes = [(g, index[l]) for g, l in zip(geoms, lith) if l and g is not None and not g.is_empty]
        if shapes:
            grid = rasterio.features.rasterize(shapes, out_shape=(ny, nx), transform=from_origin(w0, n0, STEP, STEP),
                                               fill=NODATA, dtype="uint16")
    bedrock_code = np.array([not c.startswith(NOT_BEDROCK) for c in codes] + [False] * (NODATA + 1 - len(codes)))
    if macro:
        mstrong, sstrong, mweak, sweak = macro_grid(macro, w0, e0, s0, n0, nx, ny)
        # a weak unit (only lithology types, no words) answers only where GLiM maps no bedrock
        use_weak = (mstrong == NODATA) & (mweak != NODATA) & ~bedrock_code[grid]
        mgrid = np.where(use_weak, mweak, mstrong).astype(np.uint16)
        sgrid = np.where(use_weak, sweak, sstrong).astype(np.uint16)
    else:
        mgrid, sgrid = np.full((ny, nx), NODATA, np.uint16), None
    surface = np.where(mgrid != NODATA, mgrid, grid)
    rock = np.where(bedrock_code[mgrid], mgrid, np.where(bedrock_code[grid], grid, NODATA)).astype(np.uint16)
    is_rock = rock != NODATA
    i0, j0 = round((west - w0) / STEP), round((n0 - north) / STEP)

    def cut(a):
        return a[j0:j0 + TILE, i0:i0 + TILE]
    litho = cut(grid)
    if (cut(surface) == NODATA).all() and not is_rock.any():
        return None, 0
    bed = np.full((TILE, TILE), NODATA, np.uint16)
    if is_rock.any():
        dist, (iy, ix) = distance_transform_edt(~is_rock, return_indices=True)
        filled = np.where(dist <= FILL_CELLS, rock[iy, ix], NODATA).astype(np.uint16)
        bed = cut(np.where(bedrock_code[surface], NODATA, filled).astype(np.uint16))
    if (cut(surface) == NODATA).all() and (bed == NODATA).all():
        return None, 0
    fields = [tiles.field("litho", litho, type="u16", legend=codes)]
    source, licence = SOURCE, LICENCE
    if macro:
        fields += [tiles.field("macro", cut(mgrid), type="u16", legend=codes),
                   tiles.field("source", cut(sgrid), type="u16", legend=[f"macrostrat-{s}" for s in macro[4]])]
        source += "; " + MACRO_SOURCE
        licence += "; " + MACRO_LICENCE
    fields.append(tiles.field("bedrock", bed, type="u16", legend=codes))
    blob = tiles.pack(f"global-rock-30s {west:+d} {north:+d}", source, licence, west, north, STEP, fields)
    return blob, int((cut(surface) != NODATA).sum())


def work(args):
    gdb, codes, west, south, macro = args
    t0 = time.time()
    blob, n = tile_blob(gdb, codes, west, south, macro)
    return west, south, blob, n, time.time() - t0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("gdb"); ap.add_argument("out")
    ap.add_argument("--tiles", nargs="*", help="W,S of single tiles (tests)")
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--macro", help="macrostrat_units.py output folder")
    ap.add_argument("--sources", help="Macrostrat /defs/sources?all answer (scales)")
    a = ap.parse_args()
    codes = legend(a.gdb)
    macro = None
    if a.macro:
        mc = json.load(open(os.path.join(a.macro, "codes.json"), encoding="utf-8"))
        codes = sorted(set(codes) | set(mc["codes"]))
        li = {c: i for i, c in enumerate(codes)}
        scale = {s["source_id"]: s["scale"] for s in json.load(open(a.sources, encoding="utf-8"))["success"]["data"]}
        macro = (a.macro, [li[c] for c in mc["codes"]], {s: i for i, s in enumerate(mc["sources"])}, scale, mc["sources"])
    assert len(codes) < NODATA
    if a.tiles:
        todo = [tuple(int(v) for v in s.split(",")) for s in a.tiles]
    else:
        todo = [(w, s) for s in range(80, -90, -10) for w in range(-180, 180, 10)]
    jobs = [(a.gdb, codes, w, s, macro) for w, s in todo]
    done = {}
    if a.workers > 1:
        from multiprocessing import Pool
        with Pool(a.workers) as pool:
            for w, s, blob, n, dt in pool.imap_unordered(work, jobs):
                done[(w, s)] = blob
                print(f"  {w:+4d} {s:+3d}: {n:>9,} mapped cells, {len(blob or b''):>9,} bytes, {dt:5.1f} s", flush=True)
    else:
        for j in jobs:
            w, s, blob, n, dt = work(j)
            done[(w, s)] = blob
            print(f"  {w:+4d} {s:+3d}: {n:>9,} mapped cells, {len(blob or b''):>9,} bytes, {dt:5.1f} s", flush=True)
    blobs = [done[k] for k in todo if done[k]]
    n = tiles.write_set(a.out, blobs)
    with open(a.out + ".legend.json", "w", encoding="utf-8") as f:
        json.dump(codes, f)
    print(f"{len(blobs)} tiles, {n:,} bytes -> {a.out}")


if __name__ == "__main__":
    main()
