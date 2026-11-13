import sys, os, numpy as np
sys.argv = sys.argv[:1] + []  # keep render's loop idle
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render
from render import load, glo90, metres_per_px
from scipy import ndimage
import matplotlib.pyplot as plt
def orient(h, lo_m, hi_m, px):
    b = ndimage.gaussian_filter(h, lo_m / px / 2.5) - ndimage.gaussian_filter(h, hi_m / px / 2.5)
    gz, gx = np.gradient(b)
    line = (np.degrees(np.arctan2(-gz, gx)) + 90) % 180
    w = gx**2 + gz**2; tot = w.sum(); out = {}
    for k, c in {"E-W": 0, "NE-SW": 45, "N-S": 90, "NW-SE": 135}.items():
        d = np.abs((line - c + 90) % 180 - 90); out[k] = round(float(w[d < 22.5].sum() / tot * 100), 1)
    return out
fig, axes = plt.subplots(1, 3, figsize=(15, 5))
for name in ["everest", "dhaulagiri"]:
    meta, d, lat, lon = load(name)
    real = glo90(lat, lon)
    px = 2 * 12.0  # blocks are the noise's frame: 2 blocks per pixel
    noise = d["game-elev"] - d["nodetail-elev"]
    for band in [(0, 300), (300, 1200), (1200, 4000)]:
        lo = max(band[0], 1)
        print(name, f"{band[0]}-{band[1]} m", "real", orient(real, lo, band[1], px), "noise", orient(noise, lo, band[1], px))
    if name == "dhaulagiri":
        # power spectrum of the noise and of the block surface
        for ax, (t, h) in zip(axes, [("noise only", noise), ("game surface (tops)", d["game-top"]), ("real GLO-90", real)]):
            h = h - ndimage.gaussian_filter(h, 40)
            P = np.abs(np.fft.fftshift(np.fft.fft2(h * np.hanning(h.shape[0])[:, None] * np.hanning(h.shape[1])[None, :]))) ** 2
            cy, cx = np.array(P.shape) // 2; r = 120
            ax.imshow(np.log10(P[cy - r:cy + r, cx - r:cx + r] + 1e-9), cmap="magma"); ax.set_title(f"{t}: spectrum (centre crop)"); ax.axis("off")
fig.tight_layout(); fig.savefig(os.path.join(os.path.dirname(os.path.abspath(__file__)), "spectrum.png"), dpi=80)
