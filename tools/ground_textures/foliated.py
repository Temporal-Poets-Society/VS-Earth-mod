"""Ground textures: gneiss and schist (full rock sets: rock, cobble, bricks, polished, gravel, sand ...).

Owned by this module: gneiss, schist. See "family modules" in tools/make_ground_textures.py.
Preview: python tools/make_ground_textures.py --only gneiss,schist --out <folder> --sheet <folder>/sheet.png

Tiling: the game shows one whole variant per block face, picked at random and never rotated. So the layering is
one shared set of rows (seeded per rock, not per variant) and every variant only varies the detail: a gentle
bend that fades to nothing at the tile edges, swelling and thinning of each band, and the mineral grain. Bands
therefore run on from block to block like vanilla slate. Large blotches are avoided: four variants of a blotch
read as a grid.
"""
import numpy as np


def register(H):
    noise, shared_rows, grain = H.noise, H.shared_rows, H.fine_grain

    def edge_window(n):
        """sin^2 window per axis: 0 at the tile edges, 1 in the middle."""
        t = np.sin(np.pi * (np.arange(n) + 0.5) / n) ** 2
        return t[:, None], t[None, :]

    def bent_rows(rng, n, bend, jitter, lo=1, hi=4):
        """Row coordinate with a per-variant bend (zero at every tile edge) and sub-pixel edge jitter."""
        yy = np.mgrid[0:n, 0:n][0].astype(float) + 0.5
        wy, wx = edge_window(n)
        d = noise(rng, n, beta=2.6, lo=lo, hi=hi) * bend * wx * wy
        return yy + d + jitter * noise(rng, n, beta=0.4, lo=6)

    def swell(rng, n, amount):
        """Pinch and swell: each band a little stronger or fainter along its length, unchanged at the tile edges."""
        wy, wx = edge_window(n)
        return 1.0 + amount * noise(rng, n, beta=2.2, lo=1, hi=4, aniso=0.4) * wx * wy

    def specks(rng, rgb, prob, colour, jitter, where=None):
        m = rng.random(rgb.shape[:2]) < prob
        if where is not None:
            m &= where
        rgb[m] = np.array(colour, float) + rng.normal(0, jitter, (m.sum(), 1))
        return m

    # ---------------------------------------------------------------- gneiss
    # banded grey-pink orthogneiss: pale pink-grey quartz-feldspar layers and grey biotite layers of uneven width

    def gneiss_rgb(rng, size=H.N, tone=1.0, vugs=True):
        n = size
        band = shared_rows("gneiss:rows", n, lo=2, hi=10, beta=1.4, y=bent_rows(rng, n, bend=1.6, jitter=0.35))
        band = band * swell(rng, n, 0.3)
        g = grain(rng, n)
        rgb = (np.array((161.0, 151.0, 147.0)) + band[..., None] * np.array((7.5, 7.0, 6.6))
               + g[..., None] * np.array((6.4, 6.0, 5.8)))
        # the biotite layers darken and cool a little more, the felsic layers turn pink
        rgb -= np.clip(-band - 0.6, 0, 1.5)[..., None] * np.array((6.0, 5.5, 4.5))
        rgb += np.clip(band - 0.2, 0, 1.6)[..., None] * np.array((6.0, -0.5, -2.5))
        # minerals: biotite flecks mostly in the dark layers, pink feldspar and glassy quartz in the light ones
        light = band > 0.2
        specks(rng, rgb, 0.10, (116, 110, 112), 5, where=band < -0.5)
        specks(rng, rgb, 0.02, (124, 118, 120), 5)
        specks(rng, rgb, 0.05, (188, 162, 154), 4, where=light)
        specks(rng, rgb, 0.025, (192, 188, 184), 4, where=light)
        return rgb * tone

    # ---------------------------------------------------------------- schist
    # silvery green-grey mica schist: thin mica films in a fine crenulated foliation, chlorite green, the odd garnet

    def schist_rgb(rng, size=H.N, tone=1.0, vugs=True):
        n = size
        band = shared_rows("schist:rows", n, lo=4, hi=13, beta=0.6, y=bent_rows(rng, n, bend=1.0, jitter=0.3, lo=2, hi=6))
        band = band * swell(rng, n, 0.25)
        g = grain(rng, n, coarse=0.25)
        # short sideways streaks: the mica grain is stretched along the foliation
        st = noise(rng, n, beta=0.6, lo=3, aniso=0.25)
        rgb = (np.array((131.0, 140.0, 131.0)) + band[..., None] * np.array((6.6, 6.6, 6.2))
               + g[..., None] * np.array((5.0, 5.2, 5.0)) + st[..., None] * 3.0)
        # greener in the dark films (chlorite), cooler silver in the light ones
        rgb += np.clip(-band, 0, 2)[..., None] * np.array((-2.0, 1.5, -1.0))
        rgb += np.clip(band, 0, 2)[..., None] * np.array((1.0, 0.5, 2.0))
        # mica glints: short bright flakes along the foliation, a few darker biotite flakes
        for _ in range(int(n * n * 0.012)):
            y, x = int(rng.integers(0, n)), int(rng.integers(0, n))
            c = np.array((172, 178, 170)) + rng.normal(0, 4, 3)
            for d in range(int(rng.integers(1, 3))):
                rgb[y, (x + d) % n] = c
        specks(rng, rgb, 0.02, (106, 114, 108), 5)
        # rare small garnets
        for _ in range(int(rng.integers(0, 2))):
            y, x = int(rng.integers(0, n)), int(rng.integers(0, n))
            rgb[y, x] = (128, 76, 74)
        return rgb * tone

    return {"rocks": {
        "gneiss": dict(rgb=gneiss_rgb,
                       pieces=[(172, 162, 156), (122, 116, 116), (190, 178, 170), (100, 96, 98), (152, 144, 140)],
                       sand=(178, 168, 160), termite=(190, 160, 128)),
        "schist": dict(rgb=schist_rgb,
                       pieces=[(136, 144, 136), (110, 120, 112), (158, 164, 156), (96, 104, 98), (146, 150, 142)],
                       sand=(156, 160, 150), termite=(178, 156, 124)),
    }}
