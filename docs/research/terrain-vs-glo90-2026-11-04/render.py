"""Game terrain (Earth's model with the player's saved settings) against Copernicus GLO-90, on the same grid."""
import json, math, os, sys
import numpy as np
import tifffile
from scipy import ndimage
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.colors import LightSource

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
GLO = r"C:\VS mods\Earth\rawdata\copernicus-glo90"

PEAKS = {
    "everest": [("Everest", 27.9881, 86.9250), ("Lhotse", 27.9617, 86.9333), ("Makalu", 27.8897, 87.0889),
                ("Cho Oyu", 28.0942, 86.6608), ("Nuptse", 27.9675, 86.8867), ("Ama Dablam", 27.8617, 86.8614)],
    "dhaulagiri": [("Dhaulagiri I", 28.6967, 83.4875), ("Tukuche", 28.7225, 83.5578), ("Annapurna I", 28.5961, 83.8203),
                   ("you (Wmap)", 28.6957, 83.4908)],
}


def load(name):
    meta = json.load(open(os.path.join(OUT, name + ".json")))
    nx, nz = meta["nx"], meta["nz"]
    def rd(suffix, dtype):
        return np.fromfile(os.path.join(OUT, f"{name}-{suffix}.bin"), dtype=dtype).reshape(nz, nx).astype(np.float64)
    d = {k: rd(k, np.float32) for k in ["game-elev", "nodetail-elev", "raw-elev", "relief"]}
    d["game-top"] = rd("game-top", np.int16)
    d["nodetail-top"] = rd("nodetail-top", np.int16)
    lat = meta["latN"] - np.arange(nz) * meta["step"] / meta["bpdZ"]
    lon = meta["lonW"] + np.arange(nx) * meta["step"] / meta["bpdX"]
    return meta, d, lat, lon


def glo90(lat, lon):
    """Bilinear GLO-90 heights on the game grid (lat descending, lon ascending)."""
    la0, la1 = int(math.floor(lat.min())), int(math.floor(lat.max()))
    lo0, lo1 = int(math.floor(lon.min())), int(math.floor(lon.max()))
    rows = []
    for la in range(la1, la0 - 1, -1):
        row = []
        for lo in range(lo0, lo1 + 1):
            f = os.path.join(GLO, f"Copernicus_DSM_COG_30_N{la:02d}_00_E{lo:03d}_00_DEM.tif")
            a = tifffile.imread(f).astype(np.float64)
            row.append(a)
        rows.append(np.hstack(row))
    m = np.vstack(rows)
    n = rows[0][0].shape[0] if False else 1200  # 3" rows per degree
    ncol = m.shape[1] // (lo1 - lo0 + 1)
    top = la1 + 1
    r = (top - lat)[:, None] * n * np.ones((1, lon.size))
    c = np.ones((lat.size, 1)) * ((lon - lo0) * ncol)[None, :]
    return ndimage.map_coordinates(m, [r, c], order=1, mode="nearest")


def metres_per_px(meta, lat):
    dz = meta["step"] / meta["bpdZ"] * 110_950
    dx = meta["step"] / meta["bpdX"] * 111_320 * math.cos(math.radians(lat.mean()))
    return dx, dz


def peaks_count(h, px_m, radius_m=600, drop_m=150):
    """Local tops: highest within radius_m and at least drop_m above that disc's lowest point."""
    r = max(1, int(round(radius_m / px_m)))
    y, x = np.ogrid[-r:r + 1, -r:r + 1]
    disc = x * x + y * y <= r * r
    mx = ndimage.maximum_filter(h, footprint=disc)
    mn = ndimage.minimum_filter(h, footprint=disc)
    core = (h >= mx) & (h - mn >= drop_m)
    core[:r, :] = core[-r:, :] = False
    core[:, :r] = core[:, -r:] = False
    lab, n = ndimage.label(core)
    return n


def highpass_std(h, px_m, cutoff_m=3000):
    sigma = cutoff_m / px_m / 2.5
    return float(np.std(h - ndimage.gaussian_filter(h, sigma)))


def render(name):
    meta, d, lat, lon = load(name)
    real = glo90(lat, lon)
    dx, dz = metres_per_px(meta, lat)
    px = (dx + dz) / 2
    ext = [lon[0], lon[-1], lat[-1], lat[0]]
    ls = LightSource(azdeg=315, altdeg=40)
    panels = [("Real (Copernicus GLO-90, 90 m)", real),
              ("Game: your world (detail noise on)", d["game-elev"]),
              ("Game without detail noise", d["nodetail-elev"]),
              ("2' grid alone (bicubic, before summits)", d["raw-elev"])]
    vmin, vmax = np.percentile(real, 1), max(real.max(), d["game-elev"].max())
    fig, axes = plt.subplots(2, 2, figsize=(16, 16 * (lat.size / lon.size) * (dz / dx) + 1.2))
    for ax, (title, h) in zip(axes.flat, panels):
        rgb = ls.shade(h, cmap=plt.cm.terrain, vert_exag=1.0, dx=dx, dy=dz, blend_mode="soft", vmin=vmin, vmax=vmax)
        ax.imshow(rgb, extent=ext, aspect=(1 / math.cos(math.radians(lat.mean()))), interpolation="bilinear")
        ax.set_title(f"{title}\nmax {h.max():,.0f} m, tops {peaks_count(h, px)}, detail < 3 km ±{highpass_std(h, px):.0f} m", fontsize=11)
        for label, pla, plo in PEAKS[name]:
            if lat[-1] <= pla <= lat[0] and lon[0] <= plo <= lon[-1]:
                ax.plot(plo, pla, "k^" if "you" not in label else "rx", ms=6)
                ax.annotate(label, (plo, pla), xytext=(4, 4), textcoords="offset points", fontsize=9,
                            bbox=dict(boxstyle="round,pad=0.15", fc="white", alpha=0.7, lw=0))
        ax.tick_params(labelsize=8)
    fig.suptitle(f"{name}: game terrain vs real, same grid ({meta['step']} blocks = {px:.0f} m per pixel), light from NW", fontsize=13)
    fig.tight_layout()
    fig.savefig(os.path.join(HERE, f"{name}-compare.png"), dpi=80)
    plt.close(fig)

    # profile through the first peak, west to east
    _, pla, plo = PEAKS[name][0]
    j = int(np.argmin(abs(lat - pla)))
    km = (lon - lon[0]) * 111.32 * math.cos(math.radians(pla))
    fig, ax = plt.subplots(figsize=(16, 5))
    ax.plot(km, real[j], "k", lw=1.6, label="Real (GLO-90)")
    ax.plot(km, d["game-elev"][j], "tab:red", lw=1.0, label="Game (your world)")
    ax.plot(km, d["nodetail-elev"][j], "tab:blue", lw=1.0, label="Game without detail noise")
    ax.plot(km, d["raw-elev"][j], "tab:green", lw=1.0, ls="--", label="2' grid alone")
    ax.axvline((plo - lon[0]) * 111.32 * math.cos(math.radians(pla)), color="grey", lw=0.6)
    ax.set_xlabel("km from the west edge"); ax.set_ylabel("m"); ax.grid(alpha=0.3); ax.legend()
    ax.set_title(f"West-east profile at {pla:.4f}°N through {PEAKS[name][0][0]}")
    fig.tight_layout(); fig.savefig(os.path.join(HERE, f"{name}-profile.png"), dpi=80); plt.close(fig)

    # game blocks: the noise alone, and the surface as blocks (what Wmap draws)
    noise = d["game-elev"] - d["nodetail-elev"]
    fig, axes = plt.subplots(1, 2, figsize=(16, 8 * (lat.size / lon.size) * (dz / dx) + 1))
    im = axes[0].imshow(noise, extent=ext, cmap="RdBu_r", vmin=-800, vmax=800, aspect=(1 / math.cos(math.radians(lat.mean()))))
    fig.colorbar(im, ax=axes[0], shrink=0.8, label="m")
    axes[0].set_title(f"Detail noise added (game − without): ±{np.std(noise):.0f} m (std), range {noise.min():.0f} to {noise.max():.0f} m")
    ls2 = LightSource(azdeg=315, altdeg=40)
    sh = ls2.hillshade(d["game-top"], vert_exag=1.0, dx=meta["step"], dy=meta["step"])
    axes[1].imshow(sh, extent=ext, cmap="gray", aspect=(1 / math.cos(math.radians(lat.mean()))))
    axes[1].set_title("Game surface in blocks (column tops), hillshade")
    fig.tight_layout(); fig.savefig(os.path.join(HERE, f"{name}-noise.png"), dpi=80); plt.close(fig)

    stats = {
        "px_m": px,
        "max": {k: float(v.max()) for k, v in [("real", real), ("game", d["game-elev"]), ("nodetail", d["nodetail-elev"]), ("raw", d["raw-elev"])]},
        "top_max": float(d["game-top"].max()),
        "tops": {k: peaks_count(v, px) for k, v in [("real", real), ("game", d["game-elev"]), ("nodetail", d["nodetail-elev"]), ("raw", d["raw-elev"])]},
        "hp3km": {k: highpass_std(v, px) for k, v in [("real", real), ("game", d["game-elev"]), ("nodetail", d["nodetail-elev"]), ("raw", d["raw-elev"])]},
        "rms_vs_real": {k: float(np.sqrt(np.mean((v - real) ** 2))) for k, v in [("game", d["game-elev"]), ("nodetail", d["nodetail-elev"]), ("raw", d["raw-elev"])]},
        "noise_std": float(np.std(noise)), "noise_p1_p99": [float(np.percentile(noise, 1)), float(np.percentile(noise, 99))],
        "relief_median": float(np.median(d["relief"])),
    }
    json.dump(stats, open(os.path.join(HERE, f"{name}-stats.json"), "w"), indent=1)
    print(name, json.dumps(stats, indent=1))


for n in sys.argv[1:]:
    render(n)
