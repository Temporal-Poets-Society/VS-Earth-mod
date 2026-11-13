"""Ground textures: dry-land crusts and wind dust: calcrete, gypcrete, loess, tephra (volcanic ash).

Owned by this module: calcrete, gypcrete, loess, tephra. See "family modules" in tools/make_ground_textures.py.
Preview: python tools/make_ground_textures.py --only calcrete,gypcrete,loess,tephra --out <folder> --sheet <folder>/sheet.png
"""
import types
import zlib

import numpy as np


# ---------------------------------------------------------------- tiling kit (the same in each soft-ground module)
# The game shows one whole 32 px variant per block face, picked at random and never rotated, so a tile edge meets
# another variant's edge. These helpers keep a material quiet at block scale, as vanilla soil and sand are: fine
# low-contrast grain, no structure at the scale of the tile, the same mean colour and contrast in every variant,
# bands at the same rows in every variant, and distinctive features small, spread out and kept inside the tile.

def kit(H):
    N = H.N
    yy, xx = np.mgrid[0:N, 0:N].astype(float)
    K = np.hypot(np.fft.fftfreq(N)[:, None] * N, np.fft.fftfreq(N)[None, :] * N)
    LUM = np.array([0.299, 0.587, 0.114])

    def fixed_rng(key):
        """One generator per material and purpose, the same for every variant (for shared bands)."""
        return np.random.default_rng(zlib.crc32(key.encode("utf-8")) ^ 1318)

    def offs(cy, cx):
        return (yy - cy + N / 2) % N - N / 2, (xx - cx + N / 2) % N - N / 2

    def lowcut(rgb, k=3.0):
        """Remove structure at the scale of the whole tile (below k cycles per tile; the mean is kept)."""
        f = np.fft.fft2(rgb, axes=(0, 1))
        f[(K > 0) & (K < k)] = 0
        return np.real(np.fft.ifft2(f, axes=(0, 1)))

    def finish(rgb, mean, sd, k=3.0, add=None):
        """Same mean colour and luminance contrast in every variant, so blocks never read as patches.
        add: a shared term (bands), laid on after the normalisation."""
        if k:
            rgb = lowcut(rgb, k)
        rgb = rgb - rgb.mean(axis=(0, 1))
        rgb = np.array(mean, float) + rgb * (sd / max(float((rgb @ LUM).std()), 1e-6))
        if add is not None:
            add = add if add.ndim == 3 else add[..., None]
            rgb = rgb + (add - add.mean(axis=(0, 1)))
        return rgb

    def noise1(rng, lo=1, hi=None, beta=1.5):
        """1-D periodic noise of length N, unit std (a band profile down the tile)."""
        k = np.abs(np.fft.fftfreq(N) * N)
        amp = np.where(k > 0, np.maximum(k, 1) ** (-beta / 2), 0.0)
        amp[k < lo] = 0
        if hi is not None:
            amp[k > hi] = 0
        out = np.real(np.fft.ifft(np.fft.fft(rng.standard_normal(N)) * amp))
        return out / (out.std() or 1.0)

    def rows(profile, rng, wobble=0.6):
        """A row profile laid out as horizontal bands, gently wavy inside the tile and exactly level at its left and
        right edges, so a band runs on into the next block whatever variant that shows."""
        w = noise1(rng, lo=1, hi=3) * wobble * np.sin(np.pi * (xx[0] + 0.5) / N)
        return np.interp((yy + w[None, :]) % N, np.arange(N + 1), np.append(profile, profile[0]))

    def scatter(rng, n, mind, margin=0, accept=None, tries=4000):
        """Up to n points at least mind apart (blue noise: features spread out, never clump). margin > 0 keeps them
        that far inside the tile so no feature is cut by a block edge; margin 0 wraps."""
        pts = []
        for _ in range(tries):
            if len(pts) >= n:
                break
            p = rng.uniform(margin, N - margin, 2) if margin else rng.uniform(0, N, 2)
            if accept is not None and rng.random() > accept(p[0], p[1]):
                continue
            ok = True
            for q in pts:
                d = np.abs(p - q)
                if not margin:
                    d = np.minimum(d, N - d)
                if d @ d < mind * mind:
                    ok = False
                    break
            if ok:
                pts.append(p)
        return pts

    def put(rgb, cov, colour):
        cov = np.clip(cov, 0, 1)[..., None]
        rgb[:] = rgb * (1 - cov) + np.asarray(colour, float) * cov

    def blob(rgb, cy, cx, r, colour, alpha=1.0, lit=0.2, rim=0.0, shadow=0.0, ry=None):
        """Soft round grain or pebble (ry: vertical radius), lit from the top left, optional contact shadow."""
        ry = ry or r
        dy, dx = offs(cy, cx)
        d = np.sqrt((dy / ry) ** 2 + (dx / r) ** 2)
        cov = np.clip((1 - d) * min(r, ry) + 0.5, 0, 1) * alpha
        if shadow:
            ds = np.sqrt(((dy - 0.8) / ry) ** 2 + ((dx - 0.8) / r) ** 2)
            sh = np.clip((1 - ds) * min(r, ry) + 0.5, 0, 1) * (1 - cov) * shadow
            rgb *= (1 - sh)[..., None]
        shade = 1 + lit * (-(dx / r + dy / ry) / 1.4) - rim * np.clip(d, 0, 1) ** 3
        rgb[:] = rgb * (1 - cov[..., None]) + np.asarray(colour, float) * shade[..., None] * cov[..., None]

    def lens(rgb, cy, cx, length, colour, alpha=1.0, thick=1.0):
        """Thin horizontal lens tapering to both tips."""
        dy, dx = offs(cy, cx)
        t = np.clip(1 - (2 * dx / length) ** 2, 0, 1)
        cov = np.clip(thick * 0.5 * np.sqrt(t) + 0.5 - np.abs(dy), 0, 1) * (0.35 + 0.65 * t) * (t > 0)
        put(rgb, cov * alpha, colour)

    def dash(rgb, y0, x0, ang, length, colour, alpha=1.0, width=0.5):
        """Straight thin stroke from (y0, x0) at angle ang (0 = to the right, pi/2 = down)."""
        ey, ex = np.sin(ang), np.cos(ang)
        dy, dx = offs(y0, x0)
        t = np.clip(dy * ey + dx * ex, 0, length)
        d = np.hypot(dy - t * ey, dx - t * ex)
        put(rgb, np.clip(width + 0.5 - d, 0, 1) * alpha, colour)

    def speck(rng, rgb, prob, colour, jitter=5.0, alpha=1.0):
        """Single-pixel specks blended over the matrix."""
        m = rng.random(rgb.shape[:2]) < prob
        col = np.asarray(colour, float)[None, :] + rng.normal(0, jitter, (int(m.sum()), 1))
        rgb[m] = rgb[m] * (1 - alpha) + col * alpha
        return rgb

    def blend(rgb, colour, k):
        k = np.clip(k, 0, 1)[..., None]
        return rgb * (1 - k) + np.asarray(colour, float) * k

    def grain(rng, fine=0.6, clump=0.4):
        """Vanilla-like ground grain: pixel noise plus 2-4 px clumps, nothing larger."""
        return fine * H.noise(rng, beta=0.4, lo=8) + clump * H.noise(rng, beta=1.4, lo=4, hi=12)

    fns = dict(locals())
    return types.SimpleNamespace(**{k: v for k, v in fns.items() if callable(v)})


def register(H):
    N = H.N
    noise, ramp = H.noise, H.ramp
    T = kit(H)

    # ------------------------------------------------------------ calcrete
    def calcrete(rng, v):
        # powdery cream carbonate: fine grain over a soft popcorn of small nodules with sandier seams between them,
        # scattered pale nodules, quartz sand grains and the odd root pore
        lump = noise(rng, beta=2.0, lo=4, hi=11)
        f = T.grain(rng, 0.75, 0.35) + 0.5 * lump
        rgb = ramp(f, [(-2.6, (194, 172, 136)), (-1, (212, 194, 158)), (0, (224, 208, 174)), (1, (232, 219, 188)),
                       (2.6, (242, 234, 210))]) * H.bevel_light(lump, 0.035)[..., None]
        for y, x in T.scatter(rng, 9, 7.0, margin=2):
            T.blob(rgb, y, x, rng.uniform(0.9, 1.5), (242, 233, 210), alpha=0.75, lit=0.15, shadow=0.15)
        rgb = T.speck(rng, rgb, 0.035, (180, 160, 128), 6, 0.7)   # quartz sand
        rgb = T.speck(rng, rgb, 0.02, (246, 240, 226), 4, 0.8)    # carbonate flecks
        for y, x in T.scatter(rng, 3, 9.0, margin=2):            # root-cast pores: dark pit, lit lower lip
            y, x = int(y), int(x)
            rgb[y, x] *= 0.82
            rgb[y + 1, x] = rgb[y + 1, x] * 1.04
        return T.finish(rgb, (222, 205, 170), 9.5)

    # ------------------------------------------------------------ gypcrete
    GYP_BANDS = T.noise1(T.fixed_rng("gypcrete:bands"), lo=2, hi=9, beta=1.6)

    def gypcrete(rng, v):
        # grey-white powdery crust, faint layering at the same rows in every block, selenite glints, a little dust
        f = T.grain(rng, 0.6, 0.3) + 0.3 * noise(rng, beta=1.6, lo=4, hi=12, aniso=0.5)
        rgb = ramp(f, H.stops((208, 207, 199), 9))
        rgb = T.speck(rng, rgb, 0.025, (238, 240, 238), 3, 0.9)
        rgb = T.speck(rng, rgb, 0.012, (180, 168, 148), 6, 0.6)
        band = T.rows(GYP_BANDS, rng, 0.6)
        return T.finish(rgb, (208, 207, 199), 6.5, add=band[..., None] * np.array([3.2, 3.2, 2.8]))

    # ------------------------------------------------------------ loess
    def loess(rng, v):
        # warm uniform yellow-buff silt with a faint vertical fabric and a few short rhizolith root channels
        f = T.grain(rng, 0.6, 0.3) + 0.4 * noise(rng, beta=1.0, lo=6, aniso=3.0)
        rgb = ramp(f, H.stops((212, 192, 148), 8))
        rgb = T.speck(rng, rgb, 0.025, (228, 212, 178), 4, 0.8)
        rgb = T.speck(rng, rgb, 0.012, (160, 134, 96), 6, 0.6)
        for y, x in T.scatter(rng, 3, 9.0, margin=5):
            ln = rng.uniform(3.0, 5.5)
            a = np.pi / 2 + rng.normal(0, 0.2)
            T.dash(rgb, y - ln / 2, x, a, ln, (172, 148, 106), 0.55, 0.35)
            T.dash(rgb, y - ln / 2, x + 1, a, ln, (234, 220, 188), 0.4, 0.3)
        return T.finish(rgb, (211, 191, 147), 8.5)

    # ------------------------------------------------------------ tephra
    TEPH_TONE = T.noise1(T.fixed_rng("tephra:tone"), lo=2, hi=6, beta=1.6)
    TEPH_COARSE = T.noise1(T.fixed_rng("tephra:coarse"), lo=2, hi=5, beta=1.8)
    TEPH_RED = T.noise1(T.fixed_rng("tephra:red"), lo=1, hi=3, beta=2.0)

    def tephra(rng, v):
        # dark ash in sorted layers that run on across blocks: fine ash, gritty lapilli layers, a faint oxidised layer,
        # a few small pale pumice fragments
        ck = np.clip((T.rows(TEPH_COARSE, rng, 0.5) - 0.1) * 1.4, 0, 1)   # 0 fine ash .. 1 lapilli layer
        f = T.grain(rng, 0.5, 0.3) + 0.55 * ck * noise(rng, beta=0.8, lo=6)
        rgb = ramp(f, [(-2.8, (38, 35, 36)), (-1, (58, 54, 53)), (0, (76, 70, 68)), (1, (96, 88, 84)),
                       (2.8, (124, 114, 106))])
        scoria = [(42, 39, 40), (54, 49, 48), (80, 56, 48), (66, 60, 58), (46, 42, 42)]
        for y, x in T.scatter(rng, 44, 2.6):
            if rng.random() < 0.2 + 0.7 * ck[int(y), int(x)]:
                T.blob(rgb, y, x, rng.uniform(0.6, 1.1), scoria[rng.integers(0, 5)], alpha=0.8, lit=0.3, shadow=0.15)
        for y, x in T.scatter(rng, 4, 9.0, margin=2):
            T.blob(rgb, y, x, rng.uniform(0.5, 0.9), np.array((150, 142, 130)) + rng.normal(0, 10), alpha=0.85, lit=0.25)
        tone = T.rows(TEPH_TONE, rng, 0.5)
        red = np.clip(T.rows(TEPH_RED, rng, 0.5) - 0.8, 0, 2)
        add = tone[..., None] * np.array([4.0, 3.6, 3.4]) + red[..., None] * np.array([9.0, 1.0, -3.0])
        return T.finish(rgb, (76, 70, 68), 10.5, add=add)

    return {"soft": {"calcrete": (4, calcrete), "gypcrete": (4, gypcrete), "loess": (4, loess), "tephra": (4, tephra)}}
