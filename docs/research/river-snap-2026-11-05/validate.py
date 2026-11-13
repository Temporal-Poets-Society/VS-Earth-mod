"""
Rivers before (earthdata/rivers.bin) and after (a RiverSnap output) against Copernicus GLO-90 at eight windows.

  python validate.py <before rivers.bin> <after rivers.bin> <out folder> [label] [window,window,...]

Per window and for all rivers in it (Q >= 20 m3/s, as in the file) and for its main river (reaches with at least half the
window's largest discharge), on the courses resampled every 30 m inside the window:
  - floor distance: across the course (perpendicular to it, +-(500 + 4*sqrt(Q)) m, every 15 m), the distance from the
    course to the nearest point within 2 m of that profile's lowest GLO-90 point;
  - level - GLO-90: the river's level (linear between vertices, as the runtime reads it) minus GLO-90 at the course;
  - "cut > 30 m": share of course points whose level lies more than 30 m below GLO-90 there;
  - rises: vertex pairs in the window whose level rises downstream (within a reach, and from a reach's last vertex to
    the next reach's first), and how many of them are at lake/sea vertices.
GLO-90 is read from the published tiles (rawdata/copernicus-glo90), bilinear on the 3" points.
Writes <out>/<window>.png (hillshade, before left, after right) and <out>/results-<label>.json.
"""
import json, math, os, struct, sys

import brotli
import numpy as np
import tifffile
from scipy import ndimage
import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.colors import LightSource
from matplotlib.collections import LineCollection

GLO = r"C:\VS mods\Earth\rawdata\copernicus-glo90"
M = 111195.0
WINDOWS = [
    ("grandcanyon", "Grand Canyon (Colorado)", 36.0, 36.3, -112.3, -111.9),
    ("rhine", "Middle Rhine gorge", 50.0, 50.3, 7.55, 7.85),
    ("kaligandaki", "Kali Gandaki", 28.55, 28.85, 83.4, 83.8),
    ("gauja", "Gauja at Sigulda", 57.1, 57.25, 24.75, 25.0),
    ("irongates", "Danube at the Iron Gates", 44.55, 44.75, 22.0, 22.6),
    ("sion", "Rhone at Sion", 46.15, 46.3, 7.2, 7.5),
    ("manaus", "Amazon near Manaus", -3.2, -3.0, -60.2, -59.8),
    ("stlouis", "Mississippi near St. Louis", 38.5, 38.8, -90.3, -90.0),
]


def read_rivers(path):
    b = open(path, "rb").read()
    assert b[:4] == b"EMRV"
    ver, cnt = struct.unpack("<ii", b[4:12])
    assert ver == 2
    raw = brotli.decompress(b[12:])
    dt = np.dtype([("lon", "<f4"), ("lat", "<f4"), ("lev", "<i2"), ("fixed", "u1")])
    out, o = [], 0
    for _ in range(cnt):
        q, nx, n = struct.unpack_from("<fii", raw, o)
        o += 12
        out.append((q, nx, np.frombuffer(raw, dt, n, o)))
        o += n * 11
    return out


def bbox_index(rivers):
    bb = np.empty((len(rivers), 4))
    for i, (_, _, a) in enumerate(rivers):
        bb[i] = a["lat"].min(), a["lat"].max(), a["lon"].min(), a["lon"].max()
    return bb


def tile_name(la, lo):
    return f"Copernicus_DSM_COG_30_{'N' if la >= 0 else 'S'}{abs(la):02d}_00_{'E' if lo >= 0 else 'W'}{abs(lo):03d}_00_DEM.tif"


class Glo:
    """GLO-90 mosaic over a box, bilinear on the 3" points. Tiles with fewer columns (north of 50 degrees) are brought
    to the widest one's spacing linearly along their rows, which leaves bilinear values within a tile unchanged."""

    def __init__(self, la0, la1, lo0, lo1):
        self.la_top = math.floor(la1) + 1
        self.lo_w = math.floor(lo0)
        tiles = {}
        for la in range(math.floor(la1), math.floor(la0) - 1, -1):
            for lo in range(math.floor(lo0), math.floor(lo1) + 1):
                f = os.path.join(GLO, tile_name(la, lo))
                tiles[la, lo] = tifffile.imread(f).astype(np.float64) if os.path.exists(f) else np.zeros((1200, 1200))
        self.cols = max(t.shape[1] for t in tiles.values())
        rows = []
        for la in range(math.floor(la1), math.floor(la0) - 1, -1):
            row = []
            for lo in range(math.floor(lo0), math.floor(lo1) + 1):
                t = tiles[la, lo]
                if t.shape[1] != self.cols:
                    x = np.arange(self.cols) * (t.shape[1] / self.cols)
                    t = np.stack([np.interp(x, np.arange(t.shape[1]), r) for r in t])
                row.append(t)
            rows.append(np.hstack(row))
        self.m = np.vstack(rows)

    def __call__(self, lat, lon):
        r = (self.la_top - np.asarray(lat)) * 1200
        c = (np.asarray(lon) - self.lo_w) * self.cols
        return ndimage.map_coordinates(self.m, [r, c], order=1, mode="nearest")


def course_points(rivers, idx, step=30.0):
    """Resampled course points: lat, lon, level, unit direction (east, north), Q, reach."""
    out = {k: [] for k in ("lat", "lon", "lev", "tx", "ty", "q", "r")}
    for r in idx:
        q, _, a = rivers[r]
        lat, lon, lev = a["lat"].astype(float), a["lon"].astype(float), a["lev"].astype(float)
        for k in range(len(a) - 1):
            cl = math.cos(math.radians(0.5 * (lat[k] + lat[k + 1])))
            dx, dy = (lon[k + 1] - lon[k]) * cl * M, (lat[k + 1] - lat[k]) * M
            L = math.hypot(dx, dy)
            if L <= 0:
                continue
            n = max(1, int(math.ceil(L / step)))
            f = (np.arange(n) + 0.5) / n
            out["lat"].append(lat[k] + f * (lat[k + 1] - lat[k]))
            out["lon"].append(lon[k] + f * (lon[k + 1] - lon[k]))
            out["lev"].append(lev[k] + f * (lev[k + 1] - lev[k]))
            out["tx"].append(np.full(n, dx / L)); out["ty"].append(np.full(n, dy / L))
            out["q"].append(np.full(n, q)); out["r"].append(np.full(n, r))
    return {k: (np.concatenate(v) if v else np.zeros(0)) for k, v in out.items()}


def floor_distance(glo, p):
    """Distance from each course point to the nearest point within 2 m of its cross profile's lowest point."""
    if len(p["lat"]) == 0:
        return np.zeros(0)
    L = np.minimum(500 + 4 * np.sqrt(p["q"]), 3000)
    offs = np.arange(-3000, 3001, 15.0)
    nx, ny = -p["ty"], p["tx"]
    cl = np.cos(np.radians(p["lat"]))
    lat = p["lat"][:, None] + offs[None, :] * ny[:, None] / M
    lon = p["lon"][:, None] + offs[None, :] * nx[:, None] / (M * cl[:, None])
    h = glo(lat.ravel(), lon.ravel()).reshape(lat.shape)
    h[np.abs(offs)[None, :] > L[:, None]] = np.inf
    fl = h.min(axis=1)
    band = h <= fl[:, None] + 2
    d = np.where(band, np.abs(offs)[None, :], np.inf).min(axis=1)
    return d


def rises(rivers, idx, la0, la1, lo0, lo1):
    inside = lambda a, k: la0 <= a["lat"][k] <= la1 and lo0 <= a["lon"][k] <= lo1
    total = fixed = 0
    for r in idx:
        q, nx, a = rivers[r]
        lev = a["lev"].astype(int)
        for k in range(len(a) - 1):
            if lev[k + 1] > lev[k] and (inside(a, k) or inside(a, k + 1)):
                total += 1
                fixed += bool(a["fixed"][k] or a["fixed"][k + 1])
        if nx >= 0:
            b = rivers[nx][2]
            if int(b["lev"][0]) > lev[-1] and inside(a, len(a) - 1):
                total += 1
                fixed += bool(a["fixed"][-1] or b["fixed"][0])
    return total, fixed


def stats(glo, p, la0, la1, lo0, lo1):
    ins = (p["lat"] >= la0) & (p["lat"] <= la1) & (p["lon"] >= lo0) & (p["lon"] <= lo1)
    p = {k: v[ins] for k, v in p.items()}
    if len(p["lat"]) == 0:
        return None
    g = glo(p["lat"], p["lon"])
    dl = p["lev"] - g
    d = floor_distance(glo, p)
    pc = lambda x, q: float(np.percentile(x, q))
    return {"points": int(len(dl)), "km": round(len(dl) * 0.03, 1),
            "floor_dist_median": pc(d, 50), "floor_dist_p90": pc(d, 90),
            "level_minus_glo_median": pc(dl, 50), "level_minus_glo_p10": pc(dl, 10), "level_minus_glo_p90": pc(dl, 90),
            "cut_over_30m_pct": float(100 * np.mean(dl < -30))}


def main():
    before_path, after_path, out = sys.argv[1], sys.argv[2], sys.argv[3]
    label = sys.argv[4] if len(sys.argv) > 4 else "run"
    only = set(sys.argv[5].split(",")) if len(sys.argv) > 5 else None
    os.makedirs(out, exist_ok=True)
    rv = {"before": read_rivers(before_path), "after": read_rivers(after_path)}
    bb = {k: bbox_index(v) for k, v in rv.items()}
    results = {}
    for key, title, la0, la1, lo0, lo1 in WINDOWS:
        if only and key not in only:
            continue
        pad = 0.04
        glo = Glo(la0 - pad, la1 + pad, lo0 - pad, lo1 + pad)
        res = {}
        lines = {}
        for which in ("before", "after"):
            b = bb[which]
            idx = np.nonzero((b[:, 1] >= la0 - pad) & (b[:, 0] <= la1 + pad) & (b[:, 3] >= lo0 - pad) & (b[:, 2] <= lo1 + pad))[0]
            p = course_points(rv[which], idx)
            qmax = max((rv[which][r][0] for r in idx if ((p["r"] == r) & (p["lat"] >= la0) & (p["lat"] <= la1) & (p["lon"] >= lo0) & (p["lon"] <= lo1)).any()), default=0)
            main_sel = p["q"] >= 0.5 * qmax
            res[which] = {"all": stats(glo, p, la0, la1, lo0, lo1),
                          "main": stats(glo, {k: v[main_sel] for k, v in p.items()}, la0, la1, lo0, lo1),
                          "main_q": qmax,
                          "rises": rises(rv[which], idx, la0, la1, lo0, lo1),
                          "vertices": int(sum(((rv[which][r][2]["lat"] >= la0) & (rv[which][r][2]["lat"] <= la1) &
                                               (rv[which][r][2]["lon"] >= lo0) & (rv[which][r][2]["lon"] <= lo1)).sum() for r in idx))}
            lines[which] = [(rv[which][r][2]["lon"], rv[which][r][2]["lat"], rv[which][r][0]) for r in idx]
        results[key] = res
        render(key, title, glo, la0, la1, lo0, lo1, lines, res)
        a, b2 = res["before"]["main"], res["after"]["main"]
        print(f"{key:12s} main Q {res['after']['main_q']:8.0f}: floor dist {a['floor_dist_median']:6.0f}/{a['floor_dist_p90']:6.0f} -> "
              f"{b2['floor_dist_median']:5.0f}/{b2['floor_dist_p90']:5.0f} m; level-GLO {a['level_minus_glo_median']:7.1f} "
              f"({a['level_minus_glo_p10']:7.1f}..{a['level_minus_glo_p90']:6.1f}) -> {b2['level_minus_glo_median']:6.1f} "
              f"({b2['level_minus_glo_p10']:6.1f}..{b2['level_minus_glo_p90']:5.1f}); cut>30 {a['cut_over_30m_pct']:4.1f}% -> {b2['cut_over_30m_pct']:4.1f}%; "
              f"rises {res['before']['rises']} -> {res['after']['rises']}", flush=True)
    with open(os.path.join(out, f"results-{label}.json"), "w") as f:
        json.dump(results, f, indent=1)


def render(key, title, glo, la0, la1, lo0, lo1, lines, res):
    ny = int(round((la1 - la0) * 1200))
    cl = math.cos(math.radians(0.5 * (la0 + la1)))
    nxp = int(round((lo1 - lo0) * glo.cols))
    lat = np.linspace(la1, la0, ny)
    lon = np.linspace(lo0, lo1, nxp)
    LA, LO = np.meshgrid(lat, lon, indexing="ij")
    h = glo(LA.ravel(), LO.ravel()).reshape(LA.shape)
    dx = (lon[1] - lon[0]) * cl * M
    dy = (lat[0] - lat[1]) * M
    ls = LightSource(azdeg=315, altdeg=40)
    rgb = ls.shade(h, cmap=plt.cm.gist_earth, vert_exag=1.5, dx=dx, dy=dy, blend_mode="soft",
                   vmin=np.percentile(h, 1), vmax=np.percentile(h, 99.5))
    aspect = 1 / cl
    w_in = 11
    h_in = w_in / 2 * (la1 - la0) / ((lo1 - lo0) * cl) + 0.9
    fig, axes = plt.subplots(1, 2, figsize=(w_in, min(h_in, 8)), dpi=100)
    for ax, which, col in ((axes[0], "before", "#d62728"), (axes[1], "after", "#1f3fff")):
        ax.imshow(rgb, extent=(lo0, lo1, la0, la1), aspect=aspect, interpolation="bilinear")
        segs, widths = [], []
        for x, y, q in lines[which]:
            segs.append(np.c_[x, y])
            widths.append(0.6 + 0.45 * math.log10(max(q, 20) / 20 + 1) * 2.2)
        ax.add_collection(LineCollection(segs, colors=col, linewidths=widths, alpha=0.9))
        ax.set_xlim(lo0, lo1); ax.set_ylim(la0, la1)
        s = res[which]["main"]
        lab = "rivers.bin (HydroRIVERS, ETOPO levels)" if which == "before" else "rivers-glo90.bin (snapped to GLO-90)"
        ax.set_title(f"{lab}\nmain river: floor dist. median {s['floor_dist_median']:.0f} m, level-GLO median {s['level_minus_glo_median']:.0f} m",
                     fontsize=8)
        ax.tick_params(labelsize=7)
    fig.suptitle(title, fontsize=10)
    fig.tight_layout()
    fig.savefig(os.path.join(sys.argv[3], f"{key}.png"))
    plt.close(fig)


if __name__ == "__main__":
    main()
