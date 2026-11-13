"""Ground textures: salt crust and permafrost (frozen silt, till, sand, gravel, clay).

Owned by this module: saltcrust, permafrost-silt, permafrost-till, permafrost-sand, permafrost-gravel, permafrost-clay. See "family modules" in tools/make_ground_textures.py.
Preview: python tools/make_ground_textures.py --only saltcrust,permafrost-silt,permafrost-till,permafrost-sand,permafrost-gravel,permafrost-clay --out <folder> --sheet <folder>/sheet.png
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
    noise, ramp, speckle = H.noise, H.ramp, H.speckle
    T = kit(H)

    # ------------------------------------------------------------ salt crust
    def saltcrust(rng, v):
        # white crystalline crust: fine grain, a faint fine net of blistered ridges, glints, a little pinkish and
        # grey-brown dust, the odd hairline crack kept inside the tile
        h = noise(rng, beta=1.8, lo=5, hi=12)
        f = T.grain(rng, 0.6, 0.3) + 0.45 * h
        rgb = ramp(f, [(-2.5, (198, 192, 186)), (-1, (212, 206, 199)), (0, (222, 217, 210)), (1, (230, 226, 220)),
                       (2.5, (242, 239, 234))]) * H.bevel_light(h, 0.035)[..., None]
        _, f1, f2 = H.periodic_voronoi(rng.random((24, 2)) * N)
        ridge = np.clip(1.0 - (f2 - f1 + 0.5 * noise(rng, beta=1.5, lo=4)) / 1.3, 0, 1)
        rgb = rgb + 6.0 * ridge[..., None] - 4.0 * np.roll(np.roll(ridge, 1, 0), 1, 1)[..., None] * (1 - ridge[..., None])
        rgb = rgb + noise(rng, beta=1.6, lo=4, hi=10)[..., None] * np.array([2.5, -0.5, -1.5])
        rgb = T.speck(rng, rgb, 0.04, (250, 250, 248), 3, 0.9)
        rgb = T.speck(rng, rgb, 0.015, (182, 172, 160), 6, 0.6)
        for y, x in T.scatter(rng, 2, 12.0, margin=6):
            T.dash(rgb, y, x, rng.uniform(0, 2 * np.pi), rng.uniform(3.0, 5.0), (178, 170, 162), 0.35, 0.3)
        return T.finish(rgb, (222, 216, 210), 8)

    # ------------------------------------------------------------ permafrost
    def ice_lenses(rng, rgb, n, length, colour, alpha, mind=6.0, rich=None, thick=(1.0, 1.0, 1.6)):
        """Small horizontal ice lenses, each whole inside the tile (none is cut by a block edge). rich: per-row
        weights, the same in every variant, so ice-rich layers run on across blocks."""
        acc = None if rich is None else (lambda y, x: rich[int(round(y)) % N])
        for y, x in T.scatter(rng, n, mind, margin=2, accept=acc):
            half = min(rng.uniform(*length) / 2, x - 1.0, N - 2.0 - x)
            if half < 1.2:
                continue
            col = np.asarray(colour, float) + rng.normal(0, 5)
            T.lens(rgb, float(round(y)), x, 2 * half, col, alpha * rng.uniform(0.8, 1.1), thick[rng.integers(0, len(thick))])

    def ice_layers(key):
        p = T.noise1(T.fixed_rng(key), lo=2, hi=5, beta=2.0)
        return np.clip(0.3 + 0.45 * p, 0.08, 1.0)

    SILT_LAM = T.noise1(T.fixed_rng("permafrost-silt:bands"), lo=3, hi=12, beta=1.2)
    SILT_ICE = ice_layers("permafrost-silt:ice")

    def perma_silt(rng, variant):
        # dark brown-grey arctic silt, fine layering at the same rows in every block, small milky ice lenses that
        # gather in ice-rich layers, a little hoar frost
        f = T.grain(rng, 0.6, 0.3) + 0.3 * noise(rng, beta=1.6, lo=4, hi=12, aniso=0.4)
        rgb = ramp(f, H.stops((106, 98, 88), 9))
        ice_lenses(rng, rgb, 7, (3.0, 7.0), (206, 214, 218), 0.6, 5.5, SILT_ICE)
        rgb = T.speck(rng, rgb, 0.012, (180, 190, 196), 6, 0.6)
        add = T.rows(SILT_LAM, rng, 0.5)[..., None] * np.array([3.5, 3.2, 3.0])
        return T.finish(rgb, (116, 109, 99), 11.5, add=add)

    CLAY_LAM = T.noise1(T.fixed_rng("permafrost-clay:bands"), lo=3, hi=12, beta=1.0)
    CLAY_ICE = ice_layers("permafrost-clay:ice")

    def perma_clay(rng, variant):
        # smooth cold blue-grey clay, no grit, fine laminae at the same rows in every block, thin ice sheets
        f = T.grain(rng, 0.45, 0.3) + 0.35 * noise(rng, beta=1.6, lo=4, hi=12, aniso=0.3)
        rgb = ramp(f, H.stops((118, 130, 140), 8))
        ice_lenses(rng, rgb, 5, (4.0, 9.0), (192, 204, 212), 0.42, 6.5, CLAY_ICE, thick=(1.0,))
        rgb = T.speck(rng, rgb, 0.008, (184, 196, 204), 5, 0.5)
        add = T.rows(CLAY_LAM, rng, 0.5)[..., None] * np.array([3.0, 3.2, 3.4])
        return T.finish(rgb, (126, 137, 146), 10, add=add)

    TILL_PEBBLES = [(128, 128, 126), (146, 144, 140), (148, 120, 108), (94, 94, 98), (170, 166, 158), (112, 106, 100)]

    def perma_till(rng, variant):
        # frozen grey till: sandy matrix with small pebbles and a few medium ones; the ice-wedge net survives only as
        # a few short, faint ice veinlets and lenses inside the tile
        rgb = ramp(T.grain(rng, 0.6, 0.4), H.stops((110, 116, 122), 9))
        rgb = T.speck(rng, rgb, 0.04, (142, 146, 148), 6, 0.6)
        for y, x in T.scatter(rng, 26, 3.6):
            T.blob(rgb, y, x, rng.uniform(0.6, 1.1), TILL_PEBBLES[rng.integers(0, 6)], alpha=0.8, lit=0.15, shadow=0.2)
        for y, x in T.scatter(rng, 3, 10.0, margin=3):
            T.blob(rgb, y, x, rng.uniform(1.4, 1.9), TILL_PEBBLES[rng.integers(0, 6)], alpha=0.9, lit=0.18, rim=0.1,
                   shadow=0.25)
        for y, x in T.scatter(rng, 3, 9.0, margin=6):
            T.dash(rgb, y, x, rng.uniform(0, 2 * np.pi), rng.uniform(3.0, 5.0), (196, 208, 214), 0.45, 0.4)
        ice_lenses(rng, rgb, 2, (3.0, 5.0), (200, 210, 216), 0.5)
        rgb = T.speck(rng, rgb, 0.012, (190, 202, 208), 6, 0.6)
        return T.finish(rgb, (117, 123, 128), 13)

    GRAVEL = [(140, 136, 128), (104, 106, 108), (156, 148, 136), (120, 114, 108), (92, 94, 98), (146, 140, 144),
              (128, 118, 104), (176, 172, 164), (162, 160, 156)]

    def perma_gravel(rng, variant):
        # rounded pebbles packed in milky ice: dense and even, like vanilla gravel, with ice showing between
        rgb = ramp(T.grain(rng, 0.5, 0.5), [(-2.5, (140, 150, 156)), (0, (164, 174, 180)), (2.5, (190, 198, 202))])
        for y, x in T.scatter(rng, 30, 3.0):
            T.blob(rgb, y, x, rng.uniform(0.7, 1.0), GRAVEL[rng.integers(0, 9)], alpha=0.9, lit=0.2, shadow=0.15)
        for y, x in T.scatter(rng, 120, 3.1):
            r = rng.uniform(1.2, 1.9)
            T.blob(rgb, y, x, r, np.array(GRAVEL[rng.integers(0, 9)]) + rng.normal(0, 6), lit=0.2, rim=0.08, shadow=0.2,
                   ry=r * rng.uniform(0.75, 1.0))
        rgb = T.speck(rng, rgb, 0.012, (210, 220, 226), 5, 0.6)
        return T.finish(rgb, (142, 146, 146), 17)

    # permafrost-sand was fine as it was; its ice helpers stay as they were
    def ice_mask(rng, lens, veins, thick=0.5, warp=0.9):
        """Float 0..1 ice cover: horizontal lenses plus a reticulate vein network."""
        m = np.zeros((N, N))
        if lens:
            f = noise(rng, beta=1.8, lo=1, hi=9, aniso=1 / 7.0) + 0.25 * noise(rng, beta=1.0, lo=2, hi=6)
            cut = np.quantile(f, 1 - lens)
            m = np.maximum(m, np.clip((f - cut) * 2.2, 0, 1))
        if veins:
            pts = rng.random((veins, 2)) * N
            _, f1, f2 = H.periodic_voronoi(pts, sy=1.0, sx=0.8)
            d = (f2 - f1) + warp * noise(rng, beta=2.0, lo=2, hi=8)
            m = np.maximum(m, np.clip(1.0 - d / (1.3 * thick + 0.3), 0, 1) * 0.9)
        return m

    def apply_ice(rng, rgb, m, strength=1.0):
        """Milky cream-white ice (as in real ice wedges) with grey sediment streaks."""
        depth = np.clip(m, 0, 1)
        inner = H.edge_distance(depth < 0.35, 3) / 3.0
        tone = 0.3 + 0.7 * inner + 0.3 * noise(rng, beta=1.2, lo=2, hi=10)
        ice = ramp(tone, [(0, (176, 178, 176)), (0.6, (222, 228, 228)), (1.2, (246, 249, 249))])
        streak = noise(rng, beta=1.5, lo=1, hi=14, aniso=6.0)          # vertical bubble / dirt trains
        ice = ice - np.clip(streak - 0.4, 0, 2)[..., None] * np.array([16.0, 17, 16])
        k = np.clip(depth * 1.4 - 0.1, 0, 1)[..., None] * strength
        ice = ice * 0.9 + rgb * 0.1
        out = rgb * (1 - k) + ice * k
        rim = (depth > 0.1) & (depth < 0.45)
        out[rim] = out[rim] * 0.8
        return out

    def frost(rng, rgb, amount):
        rgb = speckle(rng, rgb, amount * 0.5, (214, 224, 230), 6)
        sheen = np.clip(noise(rng, beta=2.2, lo=1, hi=5) - 0.6, 0, 2)[..., None]
        return rgb + sheen * np.array([5.0, 7, 8])

    def perma_sand(rng, variant):
        # pale warm sand cemented by ice: bright and glittery, only small lenses
        base = H.sand_rgb(rng, base=(196, 186, 162))
        rgb = apply_ice(rng, base, ice_mask(rng, [0.05, 0.07, 0.09][variant], 0), 0.7)
        rgb = speckle(rng, rgb, 0.018, (226, 232, 232), 4)
        rgb = speckle(rng, rgb, 0.012, (160, 146, 122), 4)
        return frost(rng, rgb, 0.02)

    return {"soft": {
        "saltcrust": (4, saltcrust),
        "permafrost-silt": (4, perma_silt),
        "permafrost-till": (4, perma_till),
        "permafrost-sand": (3, perma_sand),
        "permafrost-gravel": (4, perma_gravel),
        "permafrost-clay": (4, perma_clay),
    }}
