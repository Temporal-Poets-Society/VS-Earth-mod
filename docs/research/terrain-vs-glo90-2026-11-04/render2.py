"""Built-in and GLO-90 game terrain (the player's world settings) against Copernicus GLO-90 at many places."""
import json, math, os, sys
import numpy as np
import tifffile
from scipy import ndimage
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.colors import LightSource

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out2")
FIG = os.path.join(HERE, "fig2")
GLO = r"C:\VS mods\Earth\rawdata\copernicus-glo90"
os.makedirs(FIG, exist_ok=True)

TITLES = {
    "everest": "Everest, Khumbu (Nepal/Tibet)", "dhaulagiri": "Dhaulagiri and the Kali Gandaki (your Wmap spot)",
    "matterhorn": "Matterhorn, Zermatt (Alps)", "naeroyfjord": "Nærøyfjord (Norway)", "grandcanyon": "Grand Canyon (Arizona)",
    "yosemite": "Yosemite Valley (California)", "fuji": "Mount Fuji (Japan)", "kilimanjaro": "Kilimanjaro (Tanzania)",
    "aconcagua": "Aconcagua (Andes)", "glencoe": "Glen Coe (Scottish Highlands)", "gauja": "Gauja valley, Sigulda (Latvia)",
    "dolomites": "Dolomites (Italy)", "rhine": "Middle Rhine gorge (Germany)",
}


def tile_name(la, lo):
    ns = "N" if la >= 0 else "S"
    ew = "E" if lo >= 0 else "W"
    return f"Copernicus_DSM_COG_30_{ns}{abs(la):02d}_00_{ew}{abs(lo):03d}_00_DEM.tif"


def glo90(lat, lon):
    la0, la1 = int(math.floor(lat.min())), int(math.floor(lat.max()))
    lo0, lo1 = int(math.floor(lon.min())), int(math.floor(lon.max()))
    rows = []
    for la in range(la1, la0 - 1, -1):
        row = []
        for lo in range(lo0, lo1 + 1):
            f = os.path.join(GLO, tile_name(la, lo))
            row.append(tifffile.imread(f).astype(np.float64) if os.path.exists(f) else np.zeros((1200, 1200)))
        rows.append(np.hstack(row))
    m = np.vstack(rows)
    ncol = m.shape[1] // (lo1 - lo0 + 1)
    r = (la1 + 1 - lat)[:, None] * 1200 * np.ones((1, lon.size))
    c = np.ones((lat.size, 1)) * ((lon - lo0) * ncol)[None, :]
    return ndimage.map_coordinates(m, [r, c], order=1, mode="nearest")


def load(name):
    meta = json.load(open(os.path.join(OUT, name + ".json")))
    nx, nz = meta["nx"], meta["nz"]
    def rd(v, what, dt):
        return np.fromfile(os.path.join(OUT, f"{name}-{v}-{what}.bin"), dtype=dt).reshape(nz, nx)
    d = {}
    for v in ["builtin", "glo90"]:
        d[v] = {"top": rd(v, "top", np.int16).astype(float), "elev": rd(v, "elev", np.float32).astype(float),
                "mask": rd(v, "mask", np.uint8), "wtop": rd(v, "wtop", np.int16).astype(float)}
    lat = meta["latN"] - np.arange(nz) * meta["step"] / meta["bpdZ"]
    lon = meta["lonW"] + np.arange(nx) * meta["step"] / meta["bpdX"]
    return meta, d, lat, lon


def peaks_count(h, px_m, valid, radius_m=600, drop_m=150):
    r = max(1, int(round(radius_m / px_m)))
    y, x = np.ogrid[-r:r + 1, -r:r + 1]
    disc = x * x + y * y <= r * r
    mx = ndimage.maximum_filter(h, footprint=disc)
    mn = ndimage.minimum_filter(h, footprint=disc)
    core = (h >= mx) & (h - mn >= drop_m) & valid
    core[:r, :] = core[-r:, :] = False
    core[:, :r] = core[:, -r:] = False
    return ndimage.label(core)[1]


def analyse(name):
    meta, d, lat, lon = load(name)
    real = glo90(lat, lon)
    cos = math.cos(math.radians(lat.mean()))
    dz = meta["step"] / meta["bpdZ"] * 110_950
    dx = meta["step"] / meta["bpdX"] * 111_320 * cos
    px = (dx + dz) / 2
    sea, k = meta["sea"], (meta["y8849"] - meta["sea"]) / 8849.0  # blocks per metre (linear curve)
    res = {"name": name, "title": TITLES.get(name, name), "m_per_block_h": round(meta["bpdZ"] and 110_950 / meta["bpdZ"], 1),
           "m_per_block_v": round(1 / k, 1), "real_max": float(real.max()), "real_tops": None}
    for v in ["builtin", "glo90"]:
        g = d[v]
        water = g["mask"] & 7
        river = (g["mask"] & 4) > 0
        near = (g["mask"] & 8) > 0
        dry = (water == 0) & ~river & ~near
        e = g["elev"]
        # what stands in the world: the column's top block back in metres
        top_m = (g["top"] + 1 - sea) / k
        diff = e - real
        dtop = top_m - real
        r = {
            "dry_share": float(dry.mean()),
            "mae": float(np.mean(np.abs(diff[dry]))), "rms": float(np.sqrt(np.mean(diff[dry] ** 2))), "bias": float(np.mean(diff[dry])),
            "p99": float(np.percentile(np.abs(diff[dry]), 99)) if dry.any() else 0.0,
            "top_mae": float(np.mean(np.abs(dtop[dry]))),
            "max": float(e.max()), "top_max": int(g["top"].max()),
            "tops": peaks_count(e, px, dry | near),
            "river_share": float(river.mean()), "near_share": float(near.mean()), "water_share": float((water > 0).mean()),
        }
        if river.any():
            wm = (g["wtop"] + 1 - sea) / k
            rr = wm[river] - real[river]
            r["river_above_real_median"] = float(np.median(rr))
            r["river_above_real_p90"] = float(np.percentile(rr, 90))
            r["river_above_real_p10"] = float(np.percentile(rr, 10))
        if near.any():
            nn = dtop[near]
            r["near_mae"] = float(np.mean(np.abs(nn))); r["near_bias"] = float(np.mean(nn))
        land = (water == 0) | river
        r["cut_30"] = float(((dtop < -30) & land).mean())    # ground more than 30 m below the real (about 3 blocks)
        r["raised_30"] = float(((dtop > 30) & land).mean())  # more than 30 m above
        r["near_share_area"] = float(near.mean())
        # land in the game where GLO-90 has water level ground (fjords, coasts), and water where GLO-90 has land
        r["dry_on_real_sea"] = float(((water == 0) & ~river & (real <= 1.0)).mean())
        r["water_on_real_land"] = float(((water > 0) & (real > 30)).mean())
        res[v] = r
    res["real_tops"] = peaks_count(real, px, (d["glo90"]["mask"] & 7) == 0)

    # figure: real | built-in | GLO-90 ; difference GLO-90 - real ; profile
    ext = [lon[0], lon[-1], lat[-1], lat[0]]
    asp = 1 / cos
    ls = LightSource(azdeg=315, altdeg=40)
    vmin = min(np.percentile(real, 1), 0) if real.min() < 50 else np.percentile(real, 1)
    vmax = max(real.max(), d["glo90"]["elev"].max())
    fig = plt.figure(figsize=(18, 11.5))
    gs = fig.add_gridspec(2, 3, height_ratios=[1, 0.78])
    panels = [("Real: Copernicus GLO-90", real, None),
              ("Game now: built-in heights (your world)", d["builtin"]["elev"], d["builtin"]["mask"]),
              ("Game on GLO-90 heights (1.0.3-tall.4)", d["glo90"]["elev"], d["glo90"]["mask"])]
    for i, (t, h, mask) in enumerate(panels):
        ax = fig.add_subplot(gs[0, i])
        rgb = ls.shade(h, cmap=plt.cm.terrain, vert_exag=1.0, dx=dx, dy=dz, blend_mode="soft", vmin=vmin, vmax=vmax)
        if mask is not None:
            w = (mask & 7) > 0
            rgb[w, :3] = rgb[w, :3] * 0.35 + np.array([0.15, 0.35, 0.75]) * 0.65
        ax.imshow(rgb, extent=ext, aspect=asp, interpolation="bilinear")
        key = "glo90" if i == 2 else "builtin" if i == 1 else None
        sub = f"max {h.max():,.0f} m, tops {res[key]['tops'] if key else res['real_tops']}"
        if key: sub += f", dry land |game − real| {res[key]['mae']:.0f} m"
        ax.set_title(f"{t}\n{sub}", fontsize=10.5); ax.tick_params(labelsize=7)
    ax = fig.add_subplot(gs[1, 0])
    diffmap = d["glo90"]["elev"] - real
    im = ax.imshow(diffmap, extent=ext, aspect=asp, cmap="RdBu_r", vmin=-60, vmax=60)
    fig.colorbar(im, ax=ax, shrink=0.85, label="m")
    ax.set_title("GLO-90 game − real (blue: game lower)", fontsize=10.5); ax.tick_params(labelsize=7)
    ax = fig.add_subplot(gs[1, 1:])
    j = int(np.argmax(np.ptp(real, axis=1)))  # the row with the most relief
    km = (lon - lon[0]) * 111.32 * cos
    ax.plot(km, real[j], "k", lw=1.8, label="Real (GLO-90)")
    ax.plot(km, d["builtin"]["elev"][j], color="tab:red", lw=0.9, label="Game now (built-in)")
    ax.plot(km, (d["glo90"]["top"][j] + 1 - sea) / k, color="tab:blue", lw=0.9, label="Game on GLO-90 (column tops)")
    riv = (d["glo90"]["mask"][j] & 4) > 0
    if riv.any():
        wm = (d["glo90"]["wtop"][j] + 1 - sea) / k
        ax.plot(km[riv], wm[riv], ".", color="tab:cyan", ms=3, label="River water (GLO-90 game)")
    ax.set_xlabel("km from the west edge"); ax.set_ylabel("m"); ax.grid(alpha=0.3); ax.legend(fontsize=9)
    ax.set_title(f"West-east profile at {lat[j]:.3f}°", fontsize=10.5)
    fig.suptitle(f"{res['title']}: {lat[-1]:.2f}–{lat[0]:.2f}°, {lon[0]:.2f}–{lon[-1]:.2f}° · {px:.0f} m per pixel · "
                 f"your world: {res['m_per_block_h']} m per block across, {res['m_per_block_v']} m per block up", fontsize=12)
    fig.tight_layout()
    fig.savefig(os.path.join(FIG, f"{name}.png"), dpi=72)
    plt.close(fig)
    return res


if __name__ == "__main__":
    names = sys.argv[1:]
    allres = [analyse(n) for n in names]
    json.dump(allres, open(os.path.join(FIG, "results.json"), "w"), indent=1)
    print(f"{'place':12s} {'real tops':>9s} | {'built-in: MAE  RMS  bias tops max':>34s} | {'GLO-90: MAE RMS bias p99 topMAE tops max':>44s} | river-real med/p90 | near MAE bias | near% cut>30 raised>30 | dry-on-real-sea")
    for r in allres:
        b, g = r["builtin"], r["glo90"]
        print(f"{r['name']:12s} {r['real_tops']:9d} | {b['mae']:6.0f} {b['rms']:5.0f} {b['bias']:+5.0f} {b['tops']:4d} {b['max']:6.0f} |"
              f" {g['mae']:5.1f} {g['rms']:5.1f} {g['bias']:+5.1f} {g['p99']:5.0f} {g['top_mae']:5.1f} {g['tops']:4d} {g['max']:6.0f} |"
              f" {g.get('river_above_real_median', float('nan')):+6.0f} {g.get('river_above_real_p90', float('nan')):+6.0f} |"
              f" {g.get('near_mae', float('nan')):5.0f} {g.get('near_bias', float('nan')):+5.0f} | {g['near_share_area']*100:4.1f}% {g['cut_30']*100:4.1f}% {g['raised_30']*100:4.1f}% | {g['dry_on_real_sea']*100:4.1f}% real max {r['real_max']:.0f}")
