"""Ground textures: quartzite and gypsum (full rock sets).

Owned by this module: quartzite, gypsum. See "family modules" in tools/make_ground_textures.py.
Preview: python tools/make_ground_textures.py --only quartzite,gypsum --out <folder> --sheet <folder>/sheet.png

Tiling: the game shows one whole variant per block face at random, never rotated, so both rocks are drawn as
fine grain at low contrast with no blotch or joint that ends at a tile edge. Their faint bedding is one shared
set of rows (seeded per rock), so it runs on from block to block; the variants differ only in the grain.
"""
import numpy as np


def register(H):
    noise, grain, shared_rows = H.noise, H.fine_grain, H.shared_rows

    def specks(rng, rgb, prob, colour, jitter):
        m = rng.random(rgb.shape[:2]) < prob
        rgb[m] = np.array(colour, float) + rng.normal(0, jitter, (m.sum(), 1))
        return m

    # ---------------------------------------------------------------- quartzite
    # Sioux-type quartzite: dense rose-mauve stone, finely flecked with white quartz grains, faint bedding ghosts
    def quartzite_rgb(rng, size=H.N, tone=1.0, vugs=True):
        n = size
        bed = shared_rows("quartzite:rows", n, lo=2, hi=10)[:, None, None]
        g = grain(rng, n, coarse=0.45)
        # very faint drift of colour, small and weak so no blotch stands out from block to block
        drift = noise(rng, n, beta=2.0, lo=2, hi=8)
        base = np.array((202.0, 176.0, 170.0))
        rgb = base + g[..., None] * np.array((8.0, 7.6, 7.4)) + bed * np.array((1.8, 2.0, 1.8))
        # cooler mauve where the drift is low, warmer rose where it is high
        rgb = rgb + drift[..., None] * np.array((2.5, 0.5, -1.5)) + np.clip(-drift, 0, 2)[..., None] * np.array((-1.5, -2.0, 1.0))
        # white quartz grains, a few glassy glints, darker mauve and rusty grains
        specks(rng, rgb, 0.05, (222, 206, 202), 4)
        specks(rng, rgb, 0.012, (236, 228, 224), 3)
        specks(rng, rgb, 0.025, (170, 138, 134), 5)
        specks(rng, rgb, 0.006, (178, 128, 106), 5)
        return rgb * tone

    # ---------------------------------------------------------------- gypsum
    # massive alabaster: milky ivory, sugary and faintly translucent, with thin pale clay laminae and satin glints
    def gypsum_rgb(rng, size=H.N, tone=1.0, vugs=True):
        n = size
        lam = np.clip(-shared_rows("gypsum:rows", n, lo=3, hi=12) - 0.8, 0, None)[:, None]
        g = grain(rng, n, coarse=0.5)
        drift = noise(rng, n, beta=2.0, lo=2, hi=8)
        base = np.array((236.0, 230.0, 214.0))
        rgb = base + g[..., None] * np.array((4.6, 4.6, 5.0)) + drift[..., None] * np.array((1.0, 1.2, 2.2))
        # laminae: a faint cooler grey line here and there, broken along its length
        lam = lam * (0.6 + 0.4 * np.clip(noise(rng, n, beta=1.5, lo=2, aniso=0.3), -1, 1))
        rgb += lam[..., None] * np.array((-5.0, -4.2, -2.6))
        # warm tan crystals and cool grey clay specks, satin-spar glints
        specks(rng, rgb, 0.03, (226, 214, 192), 4)
        specks(rng, rgb, 0.012, (210, 208, 200), 4)
        specks(rng, rgb, 0.03, (246, 243, 234), 2)
        return rgb * tone

    return {"rocks": {
        "quartzite": dict(rgb=quartzite_rgb,
                          pieces=[(214, 190, 186), (190, 162, 160), (228, 212, 208), (172, 144, 144), (204, 180, 174)],
                          sand=(222, 202, 192), termite=(206, 176, 144)),
        "gypsum": dict(rgb=gypsum_rgb,
                       pieces=[(234, 228, 214), (216, 212, 200), (242, 238, 228), (204, 200, 190), (226, 218, 204)],
                       sand=(230, 226, 214), termite=(214, 196, 160)),
    }}
