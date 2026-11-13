"""
The water mask's coastline (Earth 0053 item 15, tall.15): where GSHHG's shorelines and the Copernicus GLO-90 water body
mask (WBM) disagree, the sea follows the mask, on top of the sea referee (tools/sea_referee.py).

GLO-90's height is exactly 0 m on every WBM ocean pixel (Copernicus flattened the sea) and never on WBM land, and the
world's heights are GLO-90's. Where GSHHG (coasts.bin, islands.bin) has land on WBM ocean the world stands at sea level
with no water: lagoons (Ébrié at Abidjan, Laguna Superior in Oaxaca), the Limfjord, marsh the Mississippi delta has
lost, the far side of channels GSHHG draws a few hundred metres to a few km off (Patagonia's fjords). Where GSHHG has sea
on WBM land, the land the heights hold is cut away: islands GSHHG draws off their place, spits and bars, reclaimed land.
The referee already takes the mask's side where its land is high (15 m) or under a glacier, and opens its sea within
2.5 km of that; this takes it everywhere else:

  opened = GSHHG land (coasts.bin's big land and islands.bin's small islands, the published originals)
           ∩ WBM ocean ∩ GLO-90 height 0 ∩ not an Earth lake cell (terrain.bin flag bit 1),
           opened with a 3x3 square, kept per connected area of MIN_KM2 or more that touches Earth sea;
  added  = GSHHG sea ∩ WBM land, opened with a 3x3 square, with the WBM lakes and rivers it encloses, kept per
           connected area of MIN_KM2 or more.
Strips one or two 3" pixels wide along a coast are a shoreline offset and stay. With both, an island GSHHG has shifted
moves to where the mask has it instead of being cut away. Only GSHHG land the mask has a counterpart for is opened
(counterparts: mask land within COUNTER_KM of most of the piece): the mask lacks some real islands outright (Donousa in
the Cyclades, Silhouette in the Seychelles, Visokoi in the South Sandwich Islands are open sea at 0 m there), and GSHHG
draws some reefs as islands (Minto Reef); one data set alone can't tell those apart, so they stay as GSHHG has them. Greenland, the high Arctic (water_atlas.polar) and south of
60°S are left alone, as the referee leaves them.

The areas are polygonised as the referee's and written with them in one pass from the originals: the referee's areas
come from its cached results (preview/water-atlas/sea-referee/cache/results-15.pkl), these join them (added areas to
its land, opened ones to its reopened sea), and coasts.bin, islands.bin and terrain.bin are written by the referee's own
merge_outlines, update_terrain and sync_island_patches (a 2' cell whose sea or land the change mostly takes flips).
With --none the output is the referee's, byte for byte.

    .venv/Scripts/python -I tools/sea_ocean.py [--workers 12] [--only lat0,lon0,lat1,lon1] [--min-km2 0.05]
        [--write --out DIR]

Needs the referee's inputs (preview/water-atlas/cache, orig/ with the published coasts.bin, islands.bin and terrain.bin,
sea-referee/cache) and rawdata/copernicus-glo90 and copernicus-glo90-wbm. Writes preview/water-atlas/sea-ocean/
(coast-<min>.json, coast-<min>.txt; cache/coast-<min>.pkl).
"""
import argparse, json, math, os, pickle, sys, time
from multiprocessing import Pool

import numpy as np
import scipy.ndimage as ndi
import shapely

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import water_atlas as wa  # noqa: E402
import sea_referee as sr  # noqa: E402

OUTDIR = os.path.join(wa.OUTDIR, "sea-ocean")
MIN_KM2 = 0.05            # smaller areas are left as they are (the referee's REOPEN_MIN_KM2)
COUNTER_KM = 4.0          # a piece of GSHHG land has a counterpart in the mask when the mask has land within this
COUNTER_NEAR = 0.5        # ... of at least this share of the piece
COUNTER_AREA = 0.1        # ... and that land is at least this share of the piece's area
SQ = np.ones((3, 3), bool)
T0 = time.time()


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


def init_worker(coasts_path, terrain_path, min_km2):
    sr.init_worker(coasts_path)
    sr.G["terrain"] = sr.TerrainFile(terrain_path)
    sr.G["min_km2"] = min_km2


def lake_cells(lat0, lon0):
    """terrain.bin's lake flag (bit 1) on the 1° tile's 30x30 2' cells, row 0 north."""
    tf = sr.G["terrain"]
    T = tf.t
    row0, col0 = (90 - lat0 - 1) * sr.GRID, (lon0 + 180) * sr.GRID
    out = np.zeros((sr.GRID, sr.GRID), bool)
    for i in range(sr.GRID):
        row = row0 + i
        for j0 in range(0, sr.GRID, T):
            col = col0 + j0
            p = tf.planes((row // T) * tf.tx + col // T)["flags"]
            n = min(T - col % T, sr.GRID - j0)
            out[i, j0:j0 + n] = (p[row % T, col % T:col % T + n] & 2) > 0
    if len(tf.cache) > 64:
        tf.cache.clear()
    return out


def keep_areas(mask, px_km2, extra=None):
    """mask's 8-connected areas of min_km2 or more (and, with extra, that extra touches); their labels and sizes."""
    lab, n = ndi.label(mask, structure=SQ)
    if not n:
        return np.zeros_like(mask), lab, np.zeros(0, int), np.zeros(0)
    idx = np.arange(1, n + 1)
    km2 = ndi.sum(np.broadcast_to(px_km2[:, None], lab.shape), lab, idx)
    ok = np.r_[False, km2 >= sr.G["min_km2"]]
    if extra is not None:
        t = np.zeros(n + 1, bool)
        t[np.unique(lab[extra & mask])] = True
        ok &= t
    return ok[lab], lab, np.flatnonzero(ok[1:]) + 1, km2


def counterparts(land, wl, lat0, hp, wp):
    """Which pieces (8-connected areas) of GSHHG land the mask has too: mask land (wl) within COUNTER_KM of at least
    COUNTER_NEAR of the piece, and at least COUNTER_AREA of the piece's area of mask land within COUNTER_KM of it. The mask
    lacks some real islands outright (Donousa, Silhouette in the Seychelles, Visokoi: open sea at 0 m with no land for
    10 km and more), and GSHHG draws some reefs as islands (Minto Reef); with one data set alone neither can be told from
    the other, so such a piece stays as GSHHG has it. A piece GSHHG has shifted, a lagoon behind its barrier or a flat
    along a shore has the mask's land beside it. Returns a bool per pixel (True: the piece has a counterpart) and the
    pieces' labels."""
    lab, n = ndi.label(land, structure=SQ)
    if not n:
        return np.zeros_like(land), lab
    lat_c = math.radians(lat0 + 0.5)
    samp = (111.32 / hp, 111.32 * math.cos(lat_c) / wp)
    near = ndi.distance_transform_edt(~wl, sampling=samp) <= COUNTER_KM
    size = np.bincount(lab.ravel(), minlength=n + 1)
    share = np.bincount(lab[land & near], minlength=n + 1) / np.maximum(size, 1)
    d, (iy, ix) = ndi.distance_transform_edt(~land, sampling=samp, return_indices=True)
    owner = lab[iy, ix]
    m = wl & (d <= COUNTER_KM)
    mask_land = np.bincount(owner[m], minlength=n + 1)
    ok = (share >= COUNTER_NEAR) & (mask_land >= COUNTER_AREA * size)
    ok[0] = False
    return ok[lab], lab


def describe(lab, ids, km2, lat0, lon0, hp, wp, dem, isl):
    if not len(ids):
        return []
    yy = ndi.mean(np.broadcast_to(np.arange(hp)[:, None], lab.shape), lab, ids)
    xx = ndi.mean(np.broadcast_to(np.arange(wp)[None, :], lab.shape), lab, ids)
    hmax = ndi.maximum(dem, lab, ids)
    on_isl = ndi.mean(isl, lab, ids)
    return [{"lat": float(lat0 + 1 - (y + 0.5) / hp), "lon": float(lon0 + (x + 0.5) / wp), "km2": float(km2[i - 1]),
             "hmax": float(h), "island_share": float(s)} for i, y, x, h, s in zip(ids, yy, xx, hmax, on_isl)]


def coast(name):
    """The tile's opened and added areas, their polygons and what they do to the 2' grid cells."""
    lat0, lon0 = wa.tile_origin(name)
    if lat0 < wa.SOUTH or wa.polar(lat0 + 0.5, lon0 + 0.5):
        return None
    t = sr.read_tile(name)
    if t is None:
        return None
    lat0, lon0, wbm, dem, glac, r0, c0, ri, ci = t
    hp, wp = wbm.shape
    isl = sr.small_island_land(lat0, lon0, hp, wp)
    land = sr.G["coasts"].land(lat0, lon0, hp, wp) | isl
    sea = ~land
    if not sea.any() or not (wbm == 1).any():
        return None
    lat_r = lat0 + 1 - (np.arange(hp) + 0.5) / hp
    px_km2 = (111.32 / hp) * (111.32 / wp) * np.cos(np.radians(lat_r))
    # opened: GSHHG land on WBM ocean (0 m), touching Earth sea
    lake = lake_cells(lat0, lon0)[(np.arange(hp) * sr.GRID // hp)[:, None], (np.arange(wp) * sr.GRID // wp)[None, :]]
    cand_all = land & (wbm == 1) & (dem == 0) & ~lake
    has_counter, lab_g = counterparts(land, wbm != 1, lat0, hp, wp)
    cand_o = cand_all & has_counter
    lone = cand_all & ~has_counter
    full_o = ndi.binary_opening(cand_o, structure=SQ)
    opened, lab_o, ids_o, km2_o = keep_areas(full_o, px_km2, ndi.binary_dilation(sea, structure=SQ))
    # added: GSHHG sea on WBM land, with the WBM lakes and rivers it encloses
    cand_a = sea & (wbm == 0)
    full_a = ndi.binary_opening(cand_a, structure=SQ)
    full_a |= ndi.binary_fill_holes(full_a) & sea & (wbm != 1)
    added, lab_a, ids_a, km2_a = keep_areas(full_a, px_km2)
    # pieces kept for want of a counterpart, by their whole area (reported)
    lone_ids = np.unique(lab_g[lone])
    lone_ids = lone_ids[lone_ids > 0]
    km2_g = np.zeros(0)
    if len(lone_ids):
        km2_g = ndi.sum(np.broadcast_to(px_km2[:, None], lab_g.shape), lab_g, np.arange(1, lab_g.max() + 1))
        lone_ids = lone_ids[km2_g[lone_ids - 1] >= sr.G["min_km2"]]
    stats = {"lone_km2": float((lone * px_km2[:, None]).sum()),
             "open_cand_km2": float((cand_o * px_km2[:, None]).sum()), "opened_km2": float((opened * px_km2[:, None]).sum()),
             "opened_isl_km2": float(((opened & isl) * px_km2[:, None]).sum()),
             "add_cand_km2": float((cand_a * px_km2[:, None]).sum()), "added_km2": float((added * px_km2[:, None]).sum())}
    res = {"name": name, "lat0": lat0, "lon0": lon0, "stats": stats,
           "opened": describe(lab_o, ids_o, km2_o, lat0, lon0, hp, wp, dem, isl),
           "added": describe(lab_a, ids_a, km2_a, lat0, lon0, hp, wp, dem, isl),
           "lone": describe(lab_g, lone_ids, km2_g, lat0, lon0, hp, wp, dem, isl),
           "opolys": [shapely.to_wkb(p) for p in sr.runs_polygons(opened, lat0, lon0)] if opened.any() else [],
           "apolys": [shapely.to_wkb(p) for p in sr.runs_polygons(added, lat0, lon0)] if added.any() else [],
           "cells": None}
    if not opened.any() and not added.any():
        return res
    f = math.lcm(wp, sr.GRID) // wp
    ry, rx = hp // sr.GRID, wp * f // sr.GRID
    rep = (lambda a: np.repeat(a, f, axis=1)) if f > 1 else (lambda a: a)
    n_cell = ry * rx
    lm = wbm != 1
    nl = wa.block_sum(rep(lm.astype(np.int32)), ry, rx)
    res["cells"] = {
        "sea": wa.block_sum(rep(sea.astype(np.int32)), ry, rx) / n_cell,
        "corr": wa.block_sum(rep(added.astype(np.int32)), ry, rx) / n_cell,
        "reopen": wa.block_sum(rep(opened.astype(np.int32)), ry, rx) / n_cell,
        "glac": wa.block_sum(rep(glac.astype(np.int32)), ry, rx) / n_cell,
        "hland": np.where(nl > 0, wa.block_sum(rep(np.where(lm, dem, 0).astype(np.float64)), ry, rx) / np.maximum(nl, 1), np.nan),
    }
    return res


def union_polys(wkbs):
    if not wkbs:
        return []
    polys = [shapely.from_wkb(p) for p in wkbs]
    u = shapely.union_all(shapely.buffer(np.array(polys, dtype=object), 1 / 1200, join_style="mitre", mitre_limit=2.0))
    u = shapely.clip_by_rect(u, -180, -90, 180, 90)
    return [g for g in getattr(u, "geoms", [u]) if g.geom_type == "Polygon" and not g.is_empty]


def summary(items):
    sizes = np.array([x["km2"] for x in items]) if items else np.zeros(0)
    by_size = {f">={lo:g} km2": {"n": int(((sizes >= lo) & (sizes < hi)).sum()),
                                 "km2": round(float(sizes[(sizes >= lo) & (sizes < hi)].sum()), 1)}
               for lo, hi in ((0, 0.2), (0.2, 1), (1, 10), (10, 100), (100, 1e9))}
    regions = {}
    for x in items:
        g = regions.setdefault(sr.region_of(x["lat"], x["lon"]), {"areas": 0, "km2": 0.0})
        g["areas"] += 1
        g["km2"] += x["km2"]
    return by_size, {k: {"areas": v["areas"], "km2": round(v["km2"], 1)} for k, v in sorted(regions.items(), key=lambda kv: -kv[1]["km2"])}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--workers", type=int, default=12)
    ap.add_argument("--only", help="lat0,lon0,lat1,lon1: only tiles in this box")
    ap.add_argument("--min-km2", type=float, default=MIN_KM2)
    ap.add_argument("--rescan", action="store_true")
    ap.add_argument("--none", action="store_true", help="write the referee's data alone (checks the pass reproduces it)")
    ap.add_argument("--no-add", action="store_true", help="open the mask's sea only (the first cut: shifted islands vanish)")
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--out", help="with --write: the directory for coasts.bin, islands.bin and terrain.bin")
    a = ap.parse_args()
    os.makedirs(os.path.join(OUTDIR, "cache"), exist_ok=True)
    coasts_path = os.path.join(sr.ORIG, "coasts.bin")
    terrain_path = os.path.join(sr.ORIG, "terrain.bin")
    sr.load_small_islands()
    tag = ("" if not a.only else "-" + a.only.replace(",", "_")) + f"-{a.min_km2:g}"
    cache = os.path.join(OUTDIR, "cache", f"coast{tag}.pkl")
    if a.none:
        oc = []
    elif os.path.exists(cache) and not a.rescan:
        oc = pickle.load(open(cache, "rb"))
        log(f"{len(oc)} tiles from {cache}")
    else:
        tiles = sorted(f[:-4] for f in os.listdir(wa.DEM) if f.endswith("_DEM.tif"))
        if a.only:
            la0, lo0, la1, lo1 = map(float, a.only.split(","))
            tiles = [t for t in tiles if la0 - 1 < wa.tile_origin(t)[0] <= la1 and lo0 - 1 < wa.tile_origin(t)[1] <= lo1]
        sea = np.load(os.path.join(wa.CACHE, "sea.npy"), mmap_mode="r")
        any_sea = np.zeros((180, 360), bool)
        for r in range(180):
            any_sea[r] = np.asarray(sea[r * wa.RES:(r + 1) * wa.RES]).reshape(wa.RES, 360, wa.RES).max(axis=(0, 2)) > 0
        tiles = [t for t in tiles if any_sea[89 - wa.tile_origin(t)[0], wa.tile_origin(t)[1] + 180]]
        log(f"{len(tiles)} tiles with Earth sea")
        with Pool(a.workers, initializer=init_worker, initargs=(coasts_path, terrain_path, a.min_km2)) as pool:
            oc = [r for r in pool.imap_unordered(coast, tiles, chunksize=4) if r]
        pickle.dump(oc, open(cache, "wb"))
        log(f"{len(oc)} tiles with candidates, {sum(1 for r in oc if r['cells'] is not None)} with areas")
    if a.no_add:
        for r in oc:
            r["added"], r["apolys"] = [], []
            if r["cells"] is not None:
                r["cells"] = dict(r["cells"], corr=np.zeros_like(r["cells"]["corr"]))
    keys = ("lone_km2", "open_cand_km2", "opened_km2", "opened_isl_km2", "add_cand_km2", "added_km2")
    stats = {k: round(sum(r["stats"][k] for r in oc), 1) for k in keys}
    if a.no_add:
        stats["added_km2"] = 0.0
    report = {"min_km2": a.min_km2, "only": a.only, "no_add": a.no_add, **stats}
    for kind in ("opened", "added", "lone"):
        items = sorted(({"lat": round(x["lat"], 4), "lon": round(x["lon"], 4), "km2": round(x["km2"], 3),
                         "hmax": round(x["hmax"], 1), "island_share": round(x["island_share"], 2),
                         "region": sr.region_of(x["lat"], x["lon"])} for r in oc for x in r.get(kind, [])), key=lambda x: -x["km2"])
        by_size, regions = summary(items)
        report[kind] = {"areas_n": len(items), "by_size": by_size, "regions": regions, "areas": items}
    log("coastline: " + json.dumps({**stats, "opened_n": report["opened"]["areas_n"], "added_n": report["added"]["areas_n"]}))
    if a.write:
        import bake_earth as be
        from shapely.strtree import STRtree
        results = pickle.load(open(os.path.join(sr.OUTDIR, "cache", f"results-{int(sr.HEIGHT_M)}.pkl"), "rb"))
        log(f"referee: {len(results)} tiles with areas")
        corrections = union_polys([p for r in results for p in r["polys"]] + [p for r in oc for p in r["apolys"]])
        removes = union_polys([p for r in results for p in r.get("rpolys", [])] + [p for r in oc for p in r["opolys"]])
        log(f"{len(corrections)} areas made land, {len(removes)} areas of WBM sea opened")
        # the grid: the referee's cells with these shares added
        by = {r["name"]: i for i, r in enumerate(results)}
        merged = list(results)
        for o in oc:
            if o["cells"] is None:
                continue
            i = by.get(o["name"])
            if i is None:
                merged.append({"name": o["name"], "lat0": o["lat0"], "lon0": o["lon0"], "cells": o["cells"], "areas": []})
            else:
                c = dict(results[i]["cells"])
                c["corr"] = np.maximum(c["corr"], o["cells"]["corr"])
                c["reopen"] = np.maximum(c["reopen"], o["cells"]["reopen"])
                merged[i] = dict(results[i], cells=c)
        tf = sr.TerrainFile(terrain_path)
        flags = np.zeros((tf.h, tf.w), np.uint8)
        for k in range(tf.count):
            r_, c_ = divmod(k, tf.tx)
            flags[r_ * tf.t:(r_ + 1) * tf.t, c_ * tf.t:(c_ + 1) * tf.t] = tf.planes(k)["flags"]
        tf.cache.clear()
        rings = be.coast_rings(flags)
        del flags
        isl = sr.IslandFile(os.path.join(sr.ORIG, "islands.bin"))
        hpts = [(x["lon"], x["lat"], x["hmax"]) for r in results for x in r["areas"]] + \
               [(x["lon"], x["lat"], x["hmax"]) for r in oc for x in r["added"]]
        htree = STRtree(shapely.points([h[0] for h in hpts], [h[1] for h in hpts]))

        def add_height(poly):
            hit = htree.query(poly, predicate="intersects")
            return max([hpts[int(i)][2] for i in hit], default=10.0)

        new_rings, info = sr.merge_outlines(rings, corrections, removes, isl, add_height)
        keep = []
        for p in new_rings:
            q = np.round(p * be.COAST_UNIT).astype(np.int64)
            if len(np.unique(q, axis=0)) >= 4:
                keep.append(p)
        info["rings_dropped_degenerate"] = len(new_rings) - len(keep)
        report["outlines"] = info
        log(f"outlines: {info}")
        os.makedirs(a.out, exist_ok=True)
        be.OUT = a.out
        be.write_coasts(keep)
        flips, changed_tiles = sr.update_terrain(tf, merged)
        tf.write(os.path.join(a.out, "terrain.bin"), changed_tiles)
        report["island_patches_synced"] = sr.sync_island_patches(isl, tf, flips)
        isl.write(os.path.join(a.out, "islands.bin"))
        report["terrain_cells_to_land"] = sum(1 for f_ in flips if f_["to"] == "land")
        report["terrain_cells_to_sea"] = sum(1 for f_ in flips if f_["to"] == "sea")
        report["terrain_tiles_rewritten"] = len(changed_tiles)
    out = f"coast{tag}{'-noadd' if a.no_add else ''}"
    json.dump(report, open(os.path.join(OUTDIR, out + ".json"), "w", encoding="utf8"), ensure_ascii=False, indent=1)
    with open(os.path.join(OUTDIR, out + ".txt"), "w", encoding="utf8") as f:
        f.write("The water mask's coastline (tools/sea_ocean.py): where GSHHG and the GLO-90 water body mask disagree, the sea "
                "follows the mask\n")
        f.write(f"opened: GSHHG land ∩ WBM ocean ∩ GLO-90 0 m ∩ not an Earth lake cell, opened 3x3, areas ≥ {a.min_km2:g} km² "
                f"touching Earth sea; added: GSHHG sea ∩ WBM land, opened 3x3, enclosed WBM water filled, areas ≥ {a.min_km2:g} "
                f"km²; not Greenland/high Arctic, not south of 60°S; GSHHG land without a counterpart in the mask (mask land "
                f"within {COUNTER_KM:g} km of under {COUNTER_NEAR:g} of the piece, or under {COUNTER_AREA:g} of its area) "
                f"stays\n")
        f.write(f"kept for want of a counterpart: {stats['lone_km2']:.1f} km² of candidate pixels on "
                f"{report['lone']['areas_n']} pieces of GSHHG land of {a.min_km2:g} km² or more\n")
        f.write(f"opened {stats['opened_km2']:.1f} km² of {stats['open_cand_km2']:.1f} km² candidate pixels in "
                f"{report['opened']['areas_n']} areas ({stats['opened_isl_km2']:.1f} km² on islands.bin's small islands); "
                f"added {stats['added_km2']:.1f} km² of {stats['add_cand_km2']:.1f} km² in {report['added']['areas_n']} areas\n")
        for kind in ("opened", "added", "lone"):
            f.write(f"{kind}:\n")
            for k, v in report[kind]["by_size"].items():
                f.write(f"  areas {k}: {v['n']}, {v['km2']} km²\n")
            for k, v in report[kind]["regions"].items():
                f.write(f"  {k}: {v['areas']} areas, {v['km2']} km²\n")
            f.write("  largest:\n")
            for x in report[kind]["areas"][:30]:
                f.write(f"    {x['km2']:9.2f} km²  {x['lat']:9.4f},{x['lon']:10.4f}  top {x['hmax']:6.0f} m  {x['region']}"
                        f"{'  (small island)' if x['island_share'] >= 0.5 else ''}\n")
        for k in ("terrain_cells_to_sea", "terrain_cells_to_land", "terrain_tiles_rewritten", "island_patches_synced"):
            if k in report:
                f.write(f"{k}: {report[k]}\n")
        if "outlines" in report:
            f.write(f"outlines: {report['outlines']}\n")
    log("done")


if __name__ == "__main__":
    main()
