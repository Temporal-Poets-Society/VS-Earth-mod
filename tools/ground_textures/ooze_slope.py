"""Ground textures: deep-sea oozes and slope/residual ground: calcareous and siliceous ooze, colluvium, regolith, terra rossa.

Owned by this module: ooze-calcareous, ooze-siliceous, colluvium, regolith, terrarossa. See "family modules" in tools/make_ground_textures.py.
Preview: python tools/make_ground_textures.py --only ooze-calcareous,ooze-siliceous,colluvium,regolith,terrarossa --out <folder> --sheet <folder>/sheet.png
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

    def poly_clast(rgb, rng, cy, cx, r, colour, elong=1.0, shade=0.22):
        """Angular fragment: random convex polygon, lit from the top left, darker rim and contact shadow."""
        k = int(rng.integers(4, 7))
        ang = np.sort(rng.uniform(0, 2 * np.pi, k))
        rad = r * rng.uniform(0.7, 1.15, k)
        px = np.cos(ang) * rad * elong
        py = np.sin(ang) * rad
        rot = rng.uniform(0, np.pi)
        c, s = np.cos(rot), np.sin(rot)
        px, py = px * c - py * s, px * s + py * c
        col = np.array(colour, float)
        rr = int(np.ceil(r * max(elong, 1))) + 2
        for dy in range(-rr, rr + 2):
            for dx in range(-rr, rr + 2):
                inside, edge = True, 9.0
                for i in range(k):
                    j = (i + 1) % k
                    ex, ey = px[j] - px[i], py[j] - py[i]
                    ln = np.hypot(ex, ey) + 1e-9
                    d = (px[i] - dx) * ey / ln - (py[i] - dy) * ex / ln  # signed distance, >0 inside for increasing angle
                    edge = min(edge, d)
                    if d < 0:
                        inside = False
                y, x = (cy + dy) % N, (cx + dx) % N
                if inside:
                    lit = 1.0 + shade * (-(dx + dy) / (1.5 * max(r, 1)))
                    rim = 0.82 + 0.18 * min(edge / 0.9, 1.0)
                    rgb[y, x] = col * lit * rim + rng.normal(0, 4, 3)
                elif edge > -1.0 and dx + dy > 0:
                    rgb[y, x] = rgb[y, x] * 0.74

    # ------------------------------------------------------------ oozes
    def laminae(key):
        """Shared lamination for one ooze: a faint tone profile and a few thin darker laminae, the same rows in every
        variant so they run on across blocks."""
        tone = T.noise1(T.fixed_rng(key + ":tone"), lo=3, hi=10, beta=1.4)
        thin = np.clip(T.noise1(T.fixed_rng(key + ":thin"), lo=4, hi=14, beta=0.8) - 1.4, 0, 1)
        return tone, thin

    CALC_TONE, CALC_THIN = laminae("ooze-calcareous")

    def ooze_calcareous(rng, v):
        # pale cream nannofossil ooze: fine soft grain with a faint relief, faint laminae, foraminifera tests (tiny
        # bright specks, some with a buff shadow pixel) and a few buff grains
        f = T.grain(rng, 0.6, 0.3) + 0.3 * noise(rng, beta=1.6, lo=4, hi=12, aniso=0.4)
        rgb = ramp(f, H.stops((218, 211, 192), 8)) * H.bevel_light(noise(rng, beta=2.0, lo=4, hi=10), 0.025)[..., None]
        w = noise(rng, beta=2.0, lo=4, hi=10, aniso=0.3)
        rgb[..., 2] -= 3.0 * w
        rgb[..., 0] += 1.0 * w
        m = rng.random((N, N)) < 0.022
        rgb[m] = rgb[m] * 0.15 + np.array((240, 237, 226)) * 0.85
        sh = np.roll(np.roll(m, 1, 0), 1, 1) & ~m & (rng.random((N, N)) < 0.6)
        rgb[sh] = rgb[sh] * 0.94
        rgb = T.speck(rng, rgb, 0.012, (190, 180, 154), 5, 0.7)
        tone, thin = T.rows(CALC_TONE, rng, 0.6), T.rows(CALC_THIN, rng, 0.6)
        add = tone[..., None] * np.array([3.0, 3.0, 2.6]) - thin[..., None] * np.array([12.0, 12.0, 11.0])
        return T.finish(rgb, (217, 210, 191), 8.5, add=add)

    SIL_TONE, SIL_THIN = laminae("ooze-siliceous")
    SIL_TINT = T.noise1(T.fixed_rng("ooze-siliceous:tint"), lo=2, hi=8, beta=1.6)

    def ooze_siliceous(rng, v):
        # olive-buff diatom ooze: fine grain, laminae that shift between greyer-green and yellower tints and a few
        # darker organic laminae, all at shared rows; pale diatom specks and dark grains
        f = T.grain(rng, 0.6, 0.3) + 0.3 * noise(rng, beta=1.6, lo=4, hi=12, aniso=0.3)
        rgb = ramp(f, H.stops((163, 154, 116), 9))
        rgb = T.speck(rng, rgb, 0.02, (200, 196, 174), 5, 0.75)
        rgb = T.speck(rng, rgb, 0.012, (112, 108, 80), 5, 0.7)
        tone, thin, tint = T.rows(SIL_TONE, rng, 0.6), T.rows(SIL_THIN, rng, 0.6), T.rows(SIL_TINT, rng, 0.6)
        add = tone[..., None] * np.array([3.2, 3.2, 2.6]) - thin[..., None] * np.array([18.0, 17.0, 13.0]) \
            + tint[..., None] * np.array([-2.6, 1.0, 2.6])
        return T.finish(rgb, (161, 153, 115), 8.5, add=add)

    # ------------------------------------------------------------ slope ground
    COLL_CLASTS = [(144, 140, 126), (116, 112, 102), (166, 160, 144), (96, 92, 84), (150, 134, 108), (130, 128, 118)]

    def colluvium(rng, v):
        # dark loamy slope wash with small angular stones of mixed rock, evenly spread and alike in size, so no stone
        # stands out from block to block
        rgb = ramp(T.grain(rng, 0.6, 0.45), H.stops((100, 80, 60), 11))
        rgb = T.speck(rng, rgb, 0.04, (140, 114, 86), 7, 0.7)
        rgb = T.speck(rng, rgb, 0.04, (66, 50, 38), 6, 0.7)
        for y, x in T.scatter(rng, 30, 4.4):
            poly_clast(rgb, rng, int(y), int(x), rng.uniform(1.0, 1.9), COLL_CLASTS[rng.integers(0, 6)],
                       elong=rng.uniform(1.0, 1.4), shade=0.2)
        return T.finish(rgb, (104, 85, 67), 16)

    REG_CHIPS = [(150, 144, 134), (128, 122, 114), (168, 160, 146), (112, 108, 102), (160, 142, 112), (176, 152, 112)]

    def regolith(rng, v):
        # broken, weathered rock in place: small angular chips, some ochre-stained, packed in a little brown-grey fines
        rgb = ramp(T.grain(rng, 0.6, 0.45), H.stops((118, 104, 86), 11))
        rgb = T.speck(rng, rgb, 0.05, (172, 160, 140), 7, 0.7)
        for y, x in T.scatter(rng, 60, 3.3):
            poly_clast(rgb, rng, int(y), int(x), rng.uniform(0.9, 1.7), REG_CHIPS[rng.integers(0, 6)],
                       elong=rng.uniform(1.0, 1.5), shade=0.18)
        return T.finish(rgb, (134, 121, 102), 15)

    TERRA_LIME = [(194, 160, 136), (178, 142, 120), (204, 174, 150)]

    def terrarossa(rng, v):
        # deep brick-red clay with a small lumpy clod relief lit from the top left, orange and dark-red grain, and a few
        # small, dull fragments of the limestone it sits on, spread out and kept inside the tile
        clod = noise(rng, beta=2.0, lo=4, hi=10)
        f = T.grain(rng, 0.6, 0.4) + 0.35 * clod
        rgb = ramp(f, H.stops((146, 56, 38), 12)) * H.bevel_light(clod, 0.06)[..., None]
        o = noise(rng, beta=2.0, lo=4, hi=10)
        rgb[..., 1] += 4 * np.clip(o, 0, 3)
        rgb[..., 0] -= 2 * np.clip(-o, 0, 3)
        rgb = T.speck(rng, rgb, 0.015, (112, 44, 32), 5, 0.7)
        rgb = T.speck(rng, rgb, 0.012, (184, 96, 64), 6, 0.6)
        rgb = T.speck(rng, rgb, 0.01, (186, 150, 128), 6, 0.5)  # fine lime grit
        for y, x in T.scatter(rng, 3, 10.0, margin=3):
            poly_clast(rgb, rng, int(y), int(x), rng.uniform(0.7, 1.2), TERRA_LIME[rng.integers(0, 3)],
                       elong=rng.uniform(0.9, 1.3), shade=0.15)
        return T.finish(rgb, (144, 57, 37), 12)

    return {"soft": {
        "ooze-calcareous": (4, ooze_calcareous),
        "ooze-siliceous": (4, ooze_siliceous),
        "colluvium": (4, colluvium),
        "regolith": (4, regolith),
        "terrarossa": (4, terrarossa),
    }}
