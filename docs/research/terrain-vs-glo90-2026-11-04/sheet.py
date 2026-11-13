import os, sys, json, math, numpy as np
sys.argv = sys.argv[:1]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render2 as R
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.colors import LightSource
res = {r["name"]: r for r in json.load(open(os.path.join(R.FIG, "results.json")))}
names = ["everest", "dhaulagiri", "matterhorn", "dolomites", "aconcagua", "kilimanjaro", "fuji", "yosemite", "grandcanyon", "naeroyfjord", "glencoe", "rhine", "gauja"]
fig, axes = plt.subplots(len(names), 3, figsize=(13.5, 3.3 * len(names)))
ls = LightSource(azdeg=315, altdeg=40)
for row, name in zip(axes, names):
    meta, d, lat, lon = R.load(name)
    real = R.glo90(lat, lon)
    cos = math.cos(math.radians(lat.mean()))
    dz = meta["step"] / meta["bpdZ"] * 110950; dx = meta["step"] / meta["bpdX"] * 111320 * cos
    vmin = min(np.percentile(real, 1), 0) if real.min() < 50 else np.percentile(real, 1); vmax = real.max()
    r = res[name]
    for ax, (t, h, m) in zip(row, [("Real (GLO-90)", real, None), ("Your world (built-in)", d["builtin"]["elev"], d["builtin"]["mask"]),
                                    ("tall.4 on GLO-90", d["glo90"]["elev"], d["glo90"]["mask"])]):
        rgb = ls.shade(h, cmap=plt.cm.terrain, vert_exag=1.0, dx=dx, dy=dz, blend_mode="soft", vmin=vmin, vmax=vmax)
        if m is not None:
            w = (m & 7) > 0
            rgb[w, :3] = rgb[w, :3] * 0.35 + np.array([0.15, 0.35, 0.75]) * 0.65
        ax.imshow(rgb, extent=[lon[0], lon[-1], lat[-1], lat[0]], aspect=1 / cos, interpolation="bilinear"); ax.set_xticks([]); ax.set_yticks([])
        if t.startswith("Real"): sub = f"{r['title']}\nreal: {r['real_tops']} tops"
        elif "built-in" in t: sub = f"{t}\n|game − real| {r['builtin']['mae']:.0f} m, {r['builtin']['tops']} tops"
        else: sub = f"{t}\n|game − real| {r['glo90']['mae']:.1f} m, {r['glo90']['tops']} tops"
        ax.set_title(sub, fontsize=9)
fig.tight_layout(); fig.savefig(os.path.join(R.FIG, "all-places.png"), dpi=60)
