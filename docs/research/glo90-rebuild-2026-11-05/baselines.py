"""Rate and error per GLO-90 tile for the grid baselines and JPEG 2000, on a sample of tiles.
baselines.py <out.json> [every N] [workers]"""
import json, math, os, sys, time
from multiprocessing import Pool
import numpy as np
from scipy import ndimage

GLO = r"C:\VS mods\Earth\rawdata\copernicus-glo90"
TEST = {"aconcagua": [(-33, -71), (-33, -70)], "dhaulagiri": [(28, 83)], "dolomites": [(46, 11)],
        "everest": [(27, 86), (27, 87), (28, 86), (28, 87)], "fuji": [(35, 138)], "gauja": [(57, 24), (57, 25)],
        "glencoe": [(56, -6), (56, -5)], "grandcanyon": [(36, -113), (36, -112)], "kilimanjaro": [(-4, 37), (-3, 37)],
        "matterhorn": [(45, 7), (46, 7)], "naeroyfjord": [(60, 6), (60, 7)], "rhine": [(50, 7)], "yosemite": [(37, -120)]}
LEVELS = list(range(50, 98, 2))


def tile_name(la, lo):
    return f"Copernicus_DSM_COG_30_{'N' if la >= 0 else 'S'}{abs(la):02d}_00_{'E' if lo >= 0 else 'W'}{abs(lo):03d}_00_DEM.tif"


def parse(fn):
    p = fn.split("_")
    la = int(p[4][1:]) * (1 if p[4][0] == "N" else -1)
    lo = int(p[6][1:]) * (1 if p[6][0] == "E" else -1)
    return la, lo


def all_tiles():
    return sorted((parse(f) for f in os.listdir(GLO) if f.endswith("_DEM.tif")), key=lambda t: (-t[0], t[1]))


def grid_sse(a, land, la, rows):
    nr, nc = a.shape
    cc = max(1, round(rows * math.cos(math.radians(la + 0.5))))
    ri = (np.arange(nr) * rows) // nr
    ci = (np.arange(nc) * cc) // nc
    k = (ri[:, None] * cc + ci[None, :]).ravel()
    s = np.bincount(k, (a * land).ravel(), rows * cc)
    n = np.bincount(k, land.ravel().astype(float), rows * cc)
    m = np.where(n > 0, s / np.maximum(n, 1), 0.0).reshape(rows, cc)
    nb = ndimage.convolve(m, np.array([[0, 1, 0], [1, 0, 1], [0, 1, 0]]) / 4.0, mode="nearest")
    m = m + 0.3 * (m - nb)
    r = (np.arange(nr) + 0.5) * rows / nr - 0.5
    c = (np.arange(nc) + 0.5) * cc / nc - 0.5
    R, C = np.meshgrid(r, c, indexing="ij")
    d = ndimage.map_coordinates(m, [R, C], order=3, mode="nearest") - a
    return float((d[land] ** 2).sum()), int((n > 0).sum())


def work(t):
    import tifffile, imagecodecs as ic
    la, lo = t
    a = tifffile.imread(os.path.join(GLO, tile_name(la, lo))).astype(np.float64)
    land = a != 0
    res = {"la": la, "lo": lo, "rows": a.shape[0], "cols": a.shape[1], "land": int(land.sum())}
    if res["land"] == 0:
        return res
    for rows in (150, 180, 200):
        res[f"grid{rows}"] = grid_sse(a, land, la, rows)
    u = (np.round(a) + 512).clip(0, 65535).astype(np.uint16)
    j = []
    for lvl in LEVELS:
        b = ic.jpeg2k_encode(u, level=lvl, reversible=False, numthreads=1)
        d = ic.jpeg2k_decode(b).astype(np.float64) - 512 - a
        j.append((lvl, len(b), float((d[land] ** 2).sum())))
    res["j2k"] = j
    return res


if __name__ == "__main__":
    out = sys.argv[1]
    every = int(sys.argv[2]) if len(sys.argv) > 2 else 40
    workers = int(sys.argv[3]) if len(sys.argv) > 3 else 30
    tiles = all_tiles()
    test = sorted({t for v in TEST.values() for t in v})
    sample = tiles[::every]
    todo = sorted(set(sample) | set(test))
    t0 = time.time()
    with Pool(workers) as p:
        rows = p.map(work, todo, chunksize=2)
    json.dump({"published": len(tiles), "every": every, "sample": [list(t) for t in sample], "test": TEST, "tiles": rows},
              open(out, "w"))
    print(len(tiles), "tiles;", len(sample), "sample;", len(test), "test;", f"{time.time() - t0:.0f} s")
