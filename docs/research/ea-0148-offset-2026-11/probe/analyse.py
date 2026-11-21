"""EA-0148: compare the probe's water (Program.cs) with GLO-90's own water on its pixel-is-point grid.

For each window, scale and variant:
- truth M: the water mask's grid point nearest each sample is water (any class): where a correct outline puts the shore
  (half way between a water and a land grid point);
- truth F: GLO-90's surface (as the world reads it) within 0.5 m of the level of the nearest mask water point: the flat
  water of the heights. It sits inside M by a fraction of a cell on every side (the surface rises between the last water
  and the first land point), the same on every side;
- the world's water W (a water block above the column's top) and the outlines alone O (inside a lake or river outline).
Per pair: the displacement that best lays W on the truth (cross-correlation; + south, + east), and per shore side the
median distance from the truth's shore to W's along the columns and rows (north and west: + = a dry strip; south and
east: + = water up the bank). One-to-one runs only (a truth run meeting exactly one W run and the other way round).
All in samples of the 1:10 world's blocks (a tenth of a block at 1:100), reported in blocks of each world.

Usage: python -I analyse.py <probe out dir> <report dir> [image places, comma separated]
"""
import csv, glob, json, math, os, sys
import numpy as np
from scipy import ndimage as ndi
from scipy import signal

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import glo90  # noqa: E402

TOL_M = 0.5


def load(path):
    with open(path, "rb") as f:
        h = json.loads(f.readline())
        nz, nx = h["nz"], h["nx"]
        n = nz * nx

        def arr(dt, size, shape):
            return np.frombuffer(f.read(size * int(np.prod(shape))), dt).reshape(shape)

        d = {"lat": arr("<f8", 8, (nz,)), "lon": arr("<f8", 8, (nx,))}
        d["flags"] = arr("<u2", 2, (nz, nx))
        d["top"] = arr("<i2", 2, (nz, nx))
        d["wtop"] = arr("<i2", 2, (nz, nx))
        d["fine"] = arr("<f4", 4, (nz, nx))
        d["elev"] = arr("<f4", 4, (nz, nx))
        d["level"] = arr("<f4", 4, (nz, nx))
    return h, d


def runs(v):
    """Start and end (exclusive) of the True runs of a 1-D bool array."""
    dv = np.diff(np.concatenate([[0], v.astype(np.int8), [0]]))
    return np.flatnonzero(dv == 1), np.flatnonzero(dv == -1)


def side_diffs(T, W):
    """Along each column (axis 0): for one-to-one runs not touching the window's ends, (W start - T start, W end - T end)."""
    out_a, out_b = [], []
    n = T.shape[0]
    for j in range(T.shape[1]):
        ta, tb = runs(T[:, j])
        wa, wb = runs(W[:, j])
        if len(ta) == 0 or len(wa) == 0:
            continue
        # overlaps: run i of T with run k of W
        ov = (ta[:, None] < wb[None, :]) & (tb[:, None] > wa[None, :])
        one_t = ov.sum(1) == 1
        one_w = ov.sum(0) == 1
        for i in np.flatnonzero(one_t):
            k = int(np.flatnonzero(ov[i])[0])
            if not one_w[k]:
                continue
            if ta[i] == 0 or tb[i] == n or wa[k] == 0 or wb[k] == n:
                continue
            out_a.append(wa[k] - ta[i])
            out_b.append(wb[k] - tb[i])
    return np.array(out_a, float), np.array(out_b, float)


def best_shift(T, W, rz, rx):
    """The displacement (dz, dx) in samples of W against T that makes the most samples agree (water on water, land on
    land) inside the window less a margin (+ south, + east), with a parabola through the peak's neighbours for a fraction
    of a sample. The margin keeps the window's own edges out (a plain overlap favours no shift where water is cut off by
    them), and counting land too keeps a W wider than T from matching anywhere within its extra width."""
    t = 2 * T[rz:T.shape[0] - rz, rx:T.shape[1] - rx].astype(np.float32) - 1
    w = 2 * W.astype(np.float32) - 1
    sub = signal.correlate(w, t, mode="valid", method="fft")
    iz, ix = np.unravel_index(int(np.argmax(sub)), sub.shape)

    def frac(a, b, cc):
        den = a - 2 * b + cc
        return 0.0 if den == 0 else 0.5 * (a - cc) / den

    fz = frac(sub[iz - 1, ix], sub[iz, ix], sub[iz + 1, ix]) if 0 < iz < sub.shape[0] - 1 else 0.0
    fx = frac(sub[iz, ix - 1], sub[iz, ix], sub[iz, ix + 1]) if 0 < ix < sub.shape[1] - 1 else 0.0
    edge = iz in (0, sub.shape[0] - 1) or ix in (0, sub.shape[1] - 1)
    return iz - rz + fz, ix - rx + fx, edge


def iou(T, W):
    u = (T | W).sum()
    return float((T & W).sum() / u) if u else float("nan")


def med(a):
    return float(np.median(a)) if len(a) else float("nan")


def tmean(a, lim):
    """The mean of the differences within lim samples (a run paired with the wrong one is dropped): unbiased on slanting
    shores, where a run's end steps by whole cells along the mask's staircase."""
    a = a[np.abs(a) <= lim]
    return float(a.mean()) if len(a) else float("nan")


def analyse(path, truth_cache):
    h, d = load(path)
    lat, lon = d["lat"], d["lon"]
    key = (h["place"])
    if key not in truth_cache:
        M = glo90.nearest(lat[:, None], lon[None, :], "WBM").astype(np.int16)
        DEM = glo90.nearest(lat[:, None], lon[None, :], "DEM")
        truth_cache[key] = (M, DEM)
    M, DEM = truth_cache[key]
    T = M > 0
    # cell size in samples (the samples are the 1:10 world's blocks)
    lat_s = math.ceil(float(np.mean(lat))) - 1
    cols = glo90.cols_for(lat_s)
    bpd10 = abs(lat[0] - lat[-1]) and (len(lat) - 1) / abs(lat[0] - lat[-1])
    bpdx10 = (len(lon) - 1) / abs(lon[-1] - lon[0])
    cell_z, cell_x = bpd10 / 1200.0, bpdx10 / cols
    # the flat water of the heights: the surface within TOL_M of the nearest mask water point's height, within 2 cells
    dist, (iz, ix) = ndi.distance_transform_edt(~T, sampling=(1 / cell_z, 1 / cell_x), return_indices=True)
    level = DEM[iz, ix]
    fine = d["fine"].astype(np.float64)
    F = (dist <= 2) & np.isfinite(fine) & (fine <= level + TOL_M)
    W = (d["flags"] & 1) > 0
    O = (d["flags"] & (4096 | 8192)) > 0
    FLOW = (d["flags"] & 512) > 0
    blocks = 10.0 / h["scale"]   # blocks of this world per sample
    rz, rx = int(round(2.5 * cell_z)), int(round(2.5 * cell_x))
    res = {"place": h["place"], "scale": h["scale"], "variant": h["variant"], "cell_z": cell_z, "cell_x": cell_x,
           "cols": cols, "blocks": blocks, "water_share": float(T.mean()), "w_count": int(W.sum()), "o_count": int(O.sum()),
           "half_s": 0.5 * cell_z * blocks, "half_e": 0.5 * cell_x * blocks}
    for tn, TT in (("M", T), ("F", F)):
        for wn, WW in (("W", W), ("O", O)):
            if not WW.any() or not TT.any():
                continue
            dz, dx, edge = best_shift(TT, WW, rz, rx)
            na, sb = side_diffs(TT, WW)
            wa, eb = side_diffs(TT.T, WW.T)
            res[f"{wn}{tn}"] = {
                "shift_s": dz * blocks, "shift_e": dx * blocks, "shift_s_cells": dz / cell_z, "shift_e_cells": dx / cell_x,
                "peak_at_edge": edge, "iou": iou(TT, WW),
                "north": tmean(na, 2 * cell_z) * blocks, "south": tmean(sb, 2 * cell_z) * blocks,
                "west": tmean(wa, 2 * cell_x) * blocks, "east": tmean(eb, 2 * cell_x) * blocks,
                "north_med": med(na) * blocks, "south_med": med(sb) * blocks, "west_med": med(wa) * blocks, "east_med": med(eb) * blocks,
                "n_ns": len(na), "n_ew": len(wa)}
    # the flow file's flowing water against the mask's rivers and against the world's river outlines
    ORIV = (d["flags"] & 8192) > 0
    for name, RR in (("flowM", T & (M == 3)), ("flowO", ORIV)):
        if FLOW.any() and RR.any():
            dz, dx, edge = best_shift(RR, FLOW, rz, rx)
            res[name] = {"shift_s": dz * blocks, "shift_e": dx * blocks, "shift_s_cells": dz / cell_z, "shift_e_cells": dx / cell_x,
                         "peak_at_edge": edge}
    # the GLO-90 path: the world's heights at the grid points beside the centre against the raw DEM's cells (whole metres
    # in the world's tiles, so up to 0.5 m apart)
    gp = h.get("gridPoints", [])
    lon_w = math.floor(round(float(np.mean(lon)), 3))   # the place's own square (places have 3 decimals), as Program.cs takes it
    tile = glo90.tile(h["latS"], lon_w, "DEM")
    diffs = [abs(v - float(tile[r, c])) for r, c, v in gp if 0 <= r < tile.shape[0] and 0 <= c < tile.shape[1]]
    res["grid_check"] = max(diffs) if diffs else float("nan")
    res["grid_points"] = len(diffs)
    return res, (h, d, T, F, W, O)


def course_phase(csv_path, lat_rng, lon_rng):
    """Course vertices in the window: their fractional position between GLO-90's rows (and between 1/1200° columns, the
    snap tools' mosaic): 0 on a grid point (pixel-is-point), 0.5 half way (a cell's centre if cells were areas)."""
    fr, fc = [], []
    with open(csv_path) as f:
        for row in csv.DictReader(f):
            la, lo = float(row["lat"]), float(row["lon"])
            if not (lat_rng[0] <= la <= lat_rng[1] and lon_rng[0] <= lo <= lon_rng[1]):
                continue
            fr.append(((90 - la) * 1200) % 1.0)
            fc.append(((lo + 180) * 1200) % 1.0)
    return np.array(fr), np.array(fc)


def picture(T, W, z0, x0, n):
    """Mask water (pixel-is-point, nearest grid point) against the world's: blue both, yellow mask water the world leaves
    dry, red world water on the mask's land, beige land in both."""
    t = T[z0:z0 + n[0], x0:x0 + n[1]]
    w = W[z0:z0 + n[0], x0:x0 + n[1]]
    rgb = np.zeros(t.shape + (3,), np.uint8)
    rgb[:] = (236, 230, 214)
    rgb[t & w] = (74, 128, 196)
    rgb[t & ~w] = (238, 190, 40)
    rgb[~t & w] = (200, 50, 50)
    return rgb


def main():
    src, out = sys.argv[1], sys.argv[2]
    imgs = sys.argv[3].split(",") if len(sys.argv) > 3 else []
    os.makedirs(out, exist_ok=True)
    files = sorted(glob.glob(os.path.join(src, "*_*_*.bin")))
    cache, results = {}, []
    held, pics = {}, {}
    for p in files:
        r, arrays = analyse(p, cache)
        results.append(r)
        held[(r["place"], r["scale"], r["variant"])] = arrays
        if len(held) > 6:
            held.pop(next(iter(held)))
        if r["place"] in imgs and r["scale"] == 10:
            h, d, T, F, W, O = arrays
            pics.setdefault(r["place"], []).append((r["variant"], T, W))
        print(f"{r['place']:22s} 1:{r['scale']:<3d} {r['variant']:8s} " + "  ".join(
            f"{k}: S{r[k]['shift_s']:+.1f} E{r[k]['shift_e']:+.1f} n{r[k]['north']:+.1f} s{r[k]['south']:+.1f} w{r[k]['west']:+.1f} e{r[k]['east']:+.1f}"
            for k in ("WM", "WF", "OM") if k in r)
            + "".join(f"  {k} S{r[k]['shift_s']:+.1f} E{r[k]['shift_e']:+.1f}" for k in ("flowM", "flowO") if k in r)
            + f"  W {r['w_count']}  grid {r['grid_check']:.3g}/{r['grid_points']}", flush=True)
    for place, views in pics.items():
        order = {"base": 0, "shift": 1, "shiftall": 2, "shiftflow": 3, "shiftcourses": 4}
        views = sorted(views, key=lambda v: order.get(v[0], 9))
        if place != "kiel-canal":
            views = [v for v in views if v[0] != "shiftall"]
        # the 120 x 160 crop with the most mask shore in it (the same for every variant)
        T0 = views[0][1]
        shore = np.zeros(T0.shape, np.float32)
        shore[1:, :] += T0[1:, :] != T0[:-1, :]
        shore[:, 1:] += T0[:, 1:] != T0[:, :-1]
        dens = ndi.uniform_filter(shore, size=(120, 160), mode="constant")
        nz, nx = T0.shape
        dens[:60, :] = dens[nz - 60:, :] = -1
        dens[:, :80] = dens[:, nx - 80:] = -1
        cz, cx = np.unravel_index(int(np.argmax(dens)), dens.shape)
        z0, x0 = cz - 60, cx - 80
        parts = [picture(T, W, z0, x0, (120, 160)) for _, T, W in views]
        gap = np.full((parts[0].shape[0], 6, 3), 255, np.uint8)
        row = np.concatenate([p for q in parts for p in (q, gap)][:-1], axis=1)
        from PIL import Image
        im = Image.fromarray(row)
        im = im.resize((im.width * 3, im.height * 3), Image.NEAREST)
        im.save(os.path.join(out, f"{place}.png"), optimize=True)
    # courses
    phases = {}
    for p in sorted(glob.glob(os.path.join(src, "*_10_courses.csv"))):
        place = os.path.basename(p)[:-len("_10_courses.csv")]
        key = (place, 10, "base")
        arr = None
        for f in files:
            if os.path.basename(f) == f"{place}_10_base.bin":
                h, d = load(f)
                arr = (d["lat"], d["lon"])
        if arr is None:
            continue
        la, lo = arr
        fr, fc = course_phase(p, (la.min(), la.max()), (lo.min(), lo.max()))
        phases[place] = {"n": len(fr), "rows_hist": np.histogram(fr, bins=10, range=(0, 1))[0].tolist(),
                         "cols_hist": np.histogram(fc, bins=10, range=(0, 1))[0].tolist(),
                         "rows_near_half": float(np.mean(np.abs(fr - 0.5) < 0.1)) if len(fr) else float("nan"),
                         "rows_near_point": float(np.mean(np.minimum(fr, 1 - fr) < 0.1)) if len(fr) else float("nan"),
                         "cols_near_half": float(np.mean(np.abs(fc - 0.5) < 0.1)) if len(fc) else float("nan"),
                         "cols_near_point": float(np.mean(np.minimum(fc, 1 - fc) < 0.1)) if len(fc) else float("nan")}
        print(place, "courses", phases[place])
    with open(os.path.join(out, "results.json"), "w") as f:
        json.dump({"windows": results, "courses": phases}, f, indent=1, default=float)
    tables(results, os.path.join(out, "tables.md"))


def tables(results, path):
    """The note's tables: the best-fit displacement of the world's water against the mask, and the shore strips against
    GLO-90's flats, per place, scale and variant (blocks of that world)."""
    by = {(r["place"], r["scale"], r["variant"]): r for r in results}
    places = list(dict.fromkeys(r["place"] for r in results))

    def sh(r, k):
        x = r.get(k)
        if not x or x["peak_at_edge"]:
            return "—"
        return f"{x['shift_s']:+.1f} / {x['shift_e']:+.1f}"

    def sides(r, k):
        x = r.get(k)
        if not x:
            return "—"
        return " / ".join("—" if not np.isfinite(x[s]) else f"{x[s]:+.0f}" if r["scale"] == 10 else f"{x[s]:+.1f}"
                          for s in ("north", "south", "west", "east"))

    lines = ["| place | cols | half cell S / E (1:10) | 1:10 shipped | 1:10 shifted | 1:100 shipped | 1:100 shifted |",
             "|---|---|---|---|---|---|---|"]
    for p in places:
        b10, s10, b100, s100 = (by.get((p, sc, v), {}) for sc, v in ((10, "base"), (10, "shift"), (100, "base"), (100, "shift")))
        if not b10:
            continue
        lines.append(f"| {p} | {b10['cols']} | {b10['half_s']:.1f} / {b10['half_e']:.1f} | {sh(b10, 'WM')} | {sh(s10, 'WM')} | "
                     f"{sh(b100, 'WM')} | {sh(s100, 'WM')} |")
    lines += ["", "| place | 1:10 shipped N / S / W / E | 1:10 shifted | 1:100 shipped | 1:100 shifted |", "|---|---|---|---|---|"]
    for p in places:
        b10, s10, b100, s100 = (by.get((p, sc, v), {}) for sc, v in ((10, "base"), (10, "shift"), (100, "base"), (100, "shift")))
        if not b10:
            continue
        lines.append(f"| {p} | {sides(b10, 'WF')} | {sides(s10, 'WF')} | {sides(b100, 'WF')} | {sides(s100, 'WF')} |")
    lines += ["", "| place | 1:10 outlines alone, shipped | shifted | flow vs mask rivers | flow vs river outlines, shipped | shifted |",
              "|---|---|---|---|---|---|"]
    for p in places:
        b10, s10 = by.get((p, 10, "base"), {}), by.get((p, 10, "shift"), {})
        if not b10:
            continue
        lines.append(f"| {p} | {sh(b10, 'OM')} | {sh(s10, 'OM')} | {sh(b10, 'flowM')} | {sh(b10, 'flowO')} | {sh(s10, 'flowO')} |")
    lines += ["", "| place | world water blocks 1:10: shipped | shifted | shifted with the canal areas |", "|---|---|---|---|"]
    for p in places:
        c = [by.get((p, 10, v), {}).get("w_count") for v in ("base", "shift", "shiftall")]
        if all(v is not None for v in c) and (c[0] != c[1] or c[1] != c[2]):
            lines.append(f"| {p} | {c[0]} | {c[1]} | {c[2]} |")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
