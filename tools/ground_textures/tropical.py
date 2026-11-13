"""Ground textures: deep tropical weathering: saprolite on felsic and mafic rock, laterite (mottled zone and ironstone crust).

Owned by this module: saprolite-felsic, saprolite-mafic, laterite-mottled, laterite-hard. See "family modules" in tools/make_ground_textures.py.
Preview: python tools/make_ground_textures.py --only saprolite-felsic,saprolite-mafic,laterite-mottled,laterite-hard --out <folder> --sheet <folder>/sheet.png
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

    # ------------------------------------------------------------ saprolite
    FEL_BANDS = T.noise1(T.fixed_rng("saprolite-felsic:bands"), lo=3, hi=10, beta=1.4)

    def felsic(rng, v):
        # pinkish-buff grus: fine grain, small pink and ochre stains, many tiny white kaolinised feldspars and quartz
        # grains, a ghost of the parent rock's foliation at the same rows in every block
        rgb = ramp(T.grain(rng, 0.65, 0.4), H.stops((204, 162, 128), 11))
        rgb = T.blend(rgb, (214, 144, 118), (noise(rng, beta=2.0, lo=4, hi=9) - 1.0) * 0.5)
        rgb = T.blend(rgb, (214, 174, 98), (noise(rng, beta=2.0, lo=4, hi=9) - 1.2) * 0.45)
        for y, x in T.scatter(rng, 16, 4.5):
            y, x = int(y), int(x)
            a = rng.uniform(0.55, 0.8)
            col = np.array((236, 228, 212)) + rng.normal(0, 4)
            for dx in range(int(rng.integers(1, 3))):
                rgb[y, (x + dx) % N] = rgb[y, (x + dx) % N] * (1 - a) + col * a
        rgb = T.speck(rng, rgb, 0.02, (226, 220, 206), 5, 0.6)   # quartz
        rgb = T.speck(rng, rgb, 0.008, (124, 92, 72), 6, 0.6)    # biotite ghosts
        add = T.rows(FEL_BANDS, rng, 0.8)[..., None] * np.array([3.5, 3.0, 2.5])
        return T.finish(rgb, (204, 162, 128), 10.5, add=add)

    def mafic(rng, v):
        # brown-red clay with small olive-grey relics of the parent rock, ochre stains and manganese specks
        rgb = ramp(T.grain(rng, 0.65, 0.45), H.stops((138, 90, 62), 10))
        g = noise(rng, beta=2.2, lo=4, hi=10)
        k = np.clip((g - np.quantile(g, 0.88)) * 2.0, 0, 1) * 0.6
        grey = ramp(noise(rng, beta=0.4, lo=8), [(-2.5, (88, 92, 72)), (0, (106, 108, 86)), (2.5, (126, 126, 100))])
        rgb = rgb * (1 - k[..., None]) + grey * k[..., None]
        rgb = T.blend(rgb, (176, 124, 70), (noise(rng, beta=2.0, lo=4, hi=9) - 1.1) * 0.45)
        rgb = T.speck(rng, rgb, 0.012, (70, 48, 38), 5, 0.7)
        rgb = T.speck(rng, rgb, 0.01, (190, 144, 96), 6, 0.6)
        return T.finish(rgb, (136, 90, 63), 10)

    # ------------------------------------------------------------ laterite
    def mottled(rng, v):
        # brick-red clay with small pale (kaolinite) mottles, slightly tubular, with yellowish-red goethite halos
        rgb = ramp(T.grain(rng, 0.65, 0.4), H.stops((170, 90, 64), 10))
        m = noise(rng, beta=3.0, lo=3, hi=7, aniso=1.5)
        cut = np.quantile(m, 0.88)
        a = np.clip((m - cut) * 1.6, 0, 1) * 0.5
        halo = np.clip((m - cut + 0.5) * 1.2, 0, 1) * 0.3
        rgb = T.blend(rgb, (188, 118, 74), halo)
        cream = ramp(noise(rng, beta=0.5, lo=6), [(-2.5, (192, 166, 140)), (0, (206, 184, 160)), (2.5, (222, 204, 182))])
        rgb = rgb * (1 - a[..., None]) + cream * a[..., None]
        rgb = T.speck(rng, rgb, 0.01, (120, 52, 40), 6, 0.7)
        return T.finish(rgb, (174, 104, 78), 13)

    def hard(rng, v):
        # dark ironstone packed with small round pisoliths (lit top left, shadow below right) and a few vesicles
        rgb = ramp(T.grain(rng, 0.6, 0.4), [(-2.5, (52, 28, 26)), (-1, (66, 35, 31)), (0, (80, 42, 36)), (1, (94, 50, 40)),
                                             (2.5, (112, 60, 46))])
        pal = [(126, 62, 44), (112, 54, 40), (138, 74, 50), (102, 50, 42), (132, 80, 56)]
        for y, x in T.scatter(rng, 70, 3.0):
            T.blob(rgb, y, x, rng.uniform(0.9, 1.4), pal[rng.integers(0, len(pal))], alpha=0.9, lit=0.3, rim=0.2,
                   shadow=0.3)
        for y, x in T.scatter(rng, 5, 8.0, margin=1):
            y, x = int(y), int(x)
            rgb[y, x] = rgb[y, x] * 0.55
            rgb[(y + 1) % N, x] = rgb[(y + 1) % N, x] * 1.12
        return T.finish(rgb, (88, 46, 37), 12)

    return {"soft": {"saprolite-felsic": (4, felsic), "saprolite-mafic": (4, mafic),
                     "laterite-mottled": (4, mottled), "laterite-hard": (4, hard)}}
