"""Draw Earth's ground-column block textures procedurally.

Writes 32x32 tileable PNGs:
- assets/earth/textures/block/ground/: till (reddish, grey), silt, marl (devonian, lake), and placeholder
  looks for the world-wide ground types (SOFT: saprolite, laterite, calcrete, loess, tephra, salt crust,
  gypcrete, permafrost, oozes, colluvium, regolith, terra rossa)
- assets/game/textures/block/stone/**/<rock>*, for dolostone, gneiss, schist, quartzite and gypsum: every texture the vanilla rock-derived
  blocks and items look up for a rock code (rock, gravel, sand, wavy sand, cobble, fanned
  cobble, cobble skull, ashlar, aged/cracked ashlar, dry stone, dry stone fence, polished,
  aged polished, termite mound). Paths mirror what vanilla 1.22.7 ships for limestone.

Everything is drawn from noise and geometry here; no game texture is read or copied.
Deterministic: the same script gives the same files.

    python tools/make_ground_textures.py [--sheet preview.png]
"""
import argparse
import os
import sys
import zlib

import numpy as np
from PIL import Image

SEED = 1318
N = 32
ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
EARTH_DIR = os.path.join(ROOT, "assets", "earth", "textures", "block", "ground")
STONE_DIR = os.path.join(ROOT, "assets", "game", "textures", "block", "stone")


def rng_for(name):
    return np.random.default_rng((zlib.crc32(name.encode("utf-8")) ^ SEED) & 0xFFFFFFFF)


# ---------------------------------------------------------------- periodic fields

def noise(rng, size=N, beta=2.0, lo=1.0, hi=None, aniso=1.0):
    """Tileable 1/f^beta noise via FFT, normalised to zero mean, unit std.
    aniso > 1 stretches features horizontally (bedding)."""
    w = rng.standard_normal((size, size))
    f = np.fft.fft2(w)
    ky = np.fft.fftfreq(size)[:, None] * size
    kx = np.fft.fftfreq(size)[None, :] * size
    k = np.sqrt((kx / aniso) ** 2 + (ky * 1.0) ** 2)
    k[0, 0] = 1.0
    amp = 1.0 / k ** (beta / 2.0)
    amp[k < lo] = 0.0
    if hi is not None:
        amp[k > hi] = 0.0
    amp[0, 0] = 0.0
    out = np.real(np.fft.ifft2(f * amp))
    s = out.std()
    return out / s if s > 0 else out


def roll(a, dy, dx):
    return np.roll(np.roll(a, dy, axis=0), dx, axis=1)


def periodic_voronoi(points, size=N, sy=1.0, sx=1.0):
    """Labels, F1 and F2 distances on a torus. sy/sx scale the metric (sy>1 flattens cells)."""
    yy, xx = np.mgrid[0:size, 0:size].astype(float) + 0.5
    d = np.empty((len(points), size, size))
    for i, (py, px) in enumerate(points):
        dy = np.abs(yy - py)
        dy = np.minimum(dy, size - dy) * sy
        dx = np.abs(xx - px)
        dx = np.minimum(dx, size - dx) * sx
        d[i] = np.sqrt(dx * dx + dy * dy)
    order = np.argsort(d, axis=0)
    lab = order[0]
    f = np.sort(d, axis=0)
    return lab, f[0], f[1]


def boundary(lab, both=True):
    """Pixels on a label boundary (4-neighbour, wrapped). both=False gives 1 px lines."""
    b = (lab != roll(lab, 0, -1)) | (lab != roll(lab, -1, 0))
    if both:
        b |= (lab != roll(lab, 0, 1)) | (lab != roll(lab, 1, 0))
    return b


def edge_distance(mask, cap=8):
    """Periodic chamfer distance (in px) from the True pixels of mask."""
    d = np.where(mask, 0.0, float(cap))
    for _ in range(cap):
        nd = np.minimum.reduce([d, roll(d, 0, 1) + 1, roll(d, 0, -1) + 1, roll(d, 1, 0) + 1, roll(d, -1, 0) + 1,
                                roll(d, 1, 1) + 1.4, roll(d, 1, -1) + 1.4, roll(d, -1, 1) + 1.4, roll(d, -1, -1) + 1.4])
        if np.array_equal(nd, d):
            break
        d = nd
    return d


def bevel_light(h, strength):
    """Lambert-ish relief shading of height field h, light from the top left."""
    dx = (roll(h, 0, 1) - roll(h, 0, -1)) * -0.5  # dh/dx with wrap: h[x+1]-h[x-1]
    dy = (roll(h, 1, 0) - roll(h, -1, 0)) * -0.5
    return 1.0 + strength * (dx + dy)


# ---------------------------------------------------------------- colour helpers

def ramp(field, stops):
    """Map field (any range, used as is) through colour stops [(t, (r,g,b)), ...]."""
    ts = np.array([s[0] for s in stops], float)
    cs = np.array([s[1] for s in stops], float)
    out = np.empty(field.shape + (3,))
    for c in range(3):
        out[..., c] = np.interp(field, ts, cs[:, c])
    return out


def to_img(rgb):
    a = np.clip(np.rint(rgb), 0, 255).astype(np.uint8)
    alpha = np.full(a.shape[:2] + (1,), 255, np.uint8)
    return Image.fromarray(np.concatenate([a, alpha], axis=2), "RGBA")


def speckle(rng, rgb, prob, colour, jitter=10):
    m = rng.random(rgb.shape[:2]) < prob
    col = np.array(colour, float)[None, :] + rng.normal(0, jitter, (m.sum(), 1))
    rgb[m] = col
    return rgb


# ---------------------------------------------------------------- dolostone rock
# The game shows one whole variant per block face, at random and never rotated, so rock and ground variants are
# fine grain at low contrast: four variants of a blotch read as a grid, and a blotch cut by the tile edge shows the
# seam. Bedding is one set of rows shared by every variant of a material (seeded by name), so it runs on from block
# to block; the variants differ only in the grain and specks.

def fine_grain(rng, size=N, coarse=0.35):
    """Crystalline / sandy grain: mostly 1-2 px, a little 3-5 px clustering. Zero mean, unit std."""
    g = rng.standard_normal((size, size)) + coarse * noise(rng, size, beta=1.2, lo=5)
    return (g - g.mean()) / g.std()


def shared_rows(name, size=N, lo=2, hi=12, beta=1.0, y=None):
    """Bedding profile shared by every variant seeded by name: 1-D noise of whole cycles lo..hi per tile (so it
    tiles), soft-clipped so that no single band stands out. Zero mean and unit std over the pixel rows; y gives
    other (fractional, any shape) rows to evaluate at, e.g. bent ones."""
    r = rng_for(name)
    ks = np.arange(lo, hi + 1)
    amp = ks ** (-beta / 2.0) * np.sqrt(r.exponential(1.0, len(ks)))
    ph = r.uniform(0, 2 * np.pi, len(ks))

    def raw(yy):
        return sum(a * np.sin(2 * np.pi * k * yy / size + p) for k, a, p in zip(ks, amp, ph))

    rows = np.arange(size) + 0.5
    sd = raw(rows).std()
    clip = lambda yy: np.tanh(raw(yy) / (1.5 * sd))
    m, s = clip(rows).mean(), clip(rows).std()
    return (clip(rows if y is None else np.asarray(y, float)) - m) / s


def dolostone_rgb(rng, size=N, tone=1.0, vugs=True):
    """Buff sugary dolostone: even crystalline grain, faint bedding, rhomb sparkle and small pores."""
    bed = shared_rows("dolostone:rows", size, lo=3, hi=12)[:, None, None]
    g = fine_grain(rng, size, coarse=0.45)
    drift = noise(rng, size, beta=2.0, lo=2, hi=8)
    rgb = np.array((197.0, 189.0, 172.0)) + g[..., None] * np.array((7.6, 7.4, 7.0)) + bed * np.array((1.6, 1.6, 1.5))
    # creamier and greyer drift (dolomite vs. relic calcite), weak and small
    rgb += drift[..., None] * np.array((1.8, 0.8, -1.2))
    # rhomb faces catching the light, and darker grains
    rgb = speckle(rng, rgb, 0.03, (220, 214, 200), 4)
    rgb = speckle(rng, rgb, 0.02, (174, 166, 150), 5)
    if vugs:
        # small dissolution pores, typical of dolostone: a darker pixel with a lit lower rim
        for _ in range(int(rng.integers(2, 4))):
            y, x = rng.integers(0, size, 2)
            rgb[y, x] = (150, 142, 128)
            rgb[(y + 1) % size, x] = (214, 208, 194)
    return rgb * tone


# ---------------------------------------------------------------- other full rocks

def foliated_rgb(rng, stops, size=N, bands=0.9, wave=1.2, fine=0.5, aniso=7.0, sparkle=None, tone=1.0):
    """Metamorphic foliation: wavy layering (aniso stretches it sideways) over mottling and grain."""
    yy = np.mgrid[0:size, 0:size][0].astype(float)
    warp = noise(rng, size, beta=2.8, lo=1, hi=4) * wave
    lay = noise(rng, size, beta=2.2, lo=1, hi=10, aniso=1.0 / aniso)  # noise() aniso > 1 stretches vertically
    f = bands * lay + 0.35 * noise(rng, size, beta=2.4, lo=1, hi=5) + fine * noise(rng, size, beta=0.2, lo=6)
    f = f + 0.25 * np.sin(2 * np.pi * (yy + 3 * warp) / (size / 4))
    rgb = ramp(f, stops)
    if sparkle:
        rgb = speckle(rng, rgb, sparkle[0], sparkle[1], 6)
    return rgb * tone


def gneiss_rgb(rng, size=N, tone=1.0, vugs=True):
    # light quartz-feldspar bands with pinkish feldspar, dark biotite-hornblende bands
    stops = [(-2.4, (58, 56, 58)), (-1.1, (92, 88, 88)), (-0.2, (150, 140, 134)), (0.6, (186, 170, 160)),
             (1.4, (204, 184, 172)), (2.6, (222, 208, 198))]
    rgb = foliated_rgb(rng, stops, size, bands=1.3, wave=1.6, fine=0.45, aniso=5.0, sparkle=(0.03, (36, 34, 36)))
    return speckle(rng, rgb, 0.02, (226, 196, 180), 6) * tone


def schist_rgb(rng, size=N, tone=1.0, vugs=True):
    # silvery green-grey mica schist, fine crenulated foliation, mica glints
    stops = [(-2.4, (84, 92, 86)), (-1, (108, 116, 108)), (0, (126, 134, 126)), (1, (146, 152, 144)),
             (2.4, (170, 176, 168))]
    rgb = foliated_rgb(rng, stops, size, bands=1.0, wave=2.2, fine=0.4, aniso=9.0, sparkle=(0.05, (206, 210, 200)))
    return speckle(rng, rgb, 0.006, (150, 70, 64), 8) * tone  # the odd garnet


def quartzite_rgb(rng, size=N, tone=1.0, vugs=True):
    # sugary, very even, pale pinkish white; faint bedding ghosts
    f = 0.35 * noise(rng, size, beta=2.4, lo=1, hi=5) + 0.25 * noise(rng, size, beta=2.0, lo=1, hi=8, aniso=1 / 6.0) \
        + 0.5 * noise(rng, size, beta=0.1, lo=8)
    stops = [(-2.5, (178, 164, 158)), (-1, (200, 188, 182)), (0, (214, 204, 198)), (1, (226, 218, 212)),
             (2.5, (240, 234, 230))]
    rgb = speckle(rng, ramp(f, stops), 0.04, (246, 244, 240), 4)
    return speckle(rng, rgb, 0.01, (168, 140, 128), 6) * tone


def gypsum_rgb(rng, size=N, tone=1.0, vugs=True):
    # alabaster-white, faint grey bedding and clay partings, selenite glints
    f = 0.45 * noise(rng, size, beta=2.6, lo=1, hi=5) + 0.4 * noise(rng, size, beta=2.0, lo=1, hi=10, aniso=1 / 8.0) \
        + 0.3 * noise(rng, size, beta=0.3, lo=8)
    stops = [(-2.5, (176, 174, 168)), (-1, (204, 202, 196)), (0, (220, 218, 212)), (1, (230, 229, 224)),
             (2.5, (242, 241, 238))]
    rgb = ramp(f, stops)
    part = noise(rng, size, beta=2.0, lo=1, hi=12, aniso=1 / 12.0) > 1.6
    rgb[part] = rgb[part] * np.array([0.82, 0.8, 0.78])
    return speckle(rng, rgb, 0.03, (250, 250, 246), 3) * tone


ROCKS = {
    # code: rock pattern, gravel pieces, sand, termite mound earth
    "dolostone": (dolostone_rgb, None, (206, 197, 176), (204, 178, 138)),
    "gneiss": (gneiss_rgb, [(168, 156, 150), (110, 104, 102), (196, 180, 170), (84, 80, 80), (150, 140, 136)],
               (182, 170, 158), (190, 160, 128)),
    "schist": (schist_rgb, [(128, 136, 128), (106, 114, 108), (150, 156, 148), (92, 98, 94), (140, 144, 136)],
               (160, 162, 150), (178, 156, 124)),
    "quartzite": (quartzite_rgb, [(220, 210, 204), (204, 192, 186), (232, 226, 222), (188, 176, 170), (212, 200, 196)],
                  (222, 212, 202), (210, 186, 150)),
    "gypsum": (gypsum_rgb, [(226, 224, 218), (210, 208, 202), (236, 235, 230), (196, 194, 188), (220, 218, 212)],
               (226, 222, 212), (214, 196, 160)),
}
STYLE = {"rgb": dolostone_rgb}


def rock_rgb(rng, size=N, tone=1.0, vugs=True):
    return STYLE["rgb"](rng, size, tone=tone, vugs=vugs)


# ---------------------------------------------------------------- stone patterns

def stones_render(rng, lab, gap, rock, relief=6.0, bevel=3.0, tint=10.0, gap_rgb=(70, 64, 56),
                  gap_noise=8.0, darken=1.0, chip=0.0):
    """Paint stones given a label map. gap is the boolean joint mask."""
    size = lab.shape[0]
    d = edge_distance(gap, cap=8)
    h = np.sqrt(np.clip(d / bevel, 0, 1))
    shade = bevel_light(h, relief * 0.12)
    out = np.empty((size, size, 3))
    ids = np.unique(lab)
    for i in ids:
        m = lab == i
        dy, dx = rng.integers(0, size, 2)
        src = roll(rock, dy, dx)
        # mostly a lightness change per stone, with a slight warm/cool shift
        t = rng.normal(0, tint) + rng.normal(0, tint * 0.3) * np.array([1.0, 0.3, -0.8])
        out[m] = src[m] + t
    # edge darkening toward joints (ambient occlusion)
    ao = 0.82 + 0.18 * np.clip(d / 2.5, 0, 1)
    out = out * (shade * ao * darken)[..., None]
    if chip > 0:
        ch = (rng.random(lab.shape) < chip) & (d <= 1.5) & ~gap
        out[ch] = out[ch] * 0.72
    g = np.array(gap_rgb, float)[None, None, :] + rng.normal(0, gap_noise, (size, size, 1))
    out[gap] = g[gap]
    return out


def voronoi_stones(rng, count, sy=1.0, sx=1.0, size=N, jitter_min=0.0):
    pts = []
    tries = 0
    while len(pts) < count and tries < 5000:
        tries += 1
        p = rng.random(2) * size
        ok = True
        for q in pts:
            dy = abs(p[0] - q[0]); dy = min(dy, size - dy) * sy
            dx = abs(p[1] - q[1]); dx = min(dx, size - dx) * sx
            if (dy * dy + dx * dx) ** 0.5 < jitter_min:
                ok = False
                break
        if ok:
            pts.append(p)
    lab, f1, f2 = periodic_voronoi(pts, size, sy, sx)
    return lab, f1, f2


def cobble(rng, name_rock, count=14, sy=1.0, sx=1.0, gapw=1.1, relief=5.0, darken=1.0,
           gap_rgb=(88, 81, 70)):
    rock = rock_rgb(rng)
    lab, f1, f2 = voronoi_stones(rng, count, sy, sx, jitter_min=4.0)
    # irregular joints: wobble the bisector width with noise
    wob = noise(rng, beta=1.5, lo=2) * 0.35
    gap = (f2 - f1) < (gapw + wob)
    return stones_render(rng, lab, gap, rock, relief=relief, bevel=3.0, tint=9.0,
                         gap_rgb=gap_rgb, darken=darken)


def course_labels(rng, heights, min_w, max_w, size=N, offset_rows=True):
    """Rectangular stones in horizontal courses (rows of given heights)."""
    lab = np.zeros((size, size), int)
    y0 = 0
    nid = 0
    for r, hgt in enumerate(heights):
        widths = []
        while sum(widths) < size:
            widths.append(int(rng.integers(min_w, max_w + 1)))
        over = sum(widths) - size
        widths[-1] -= over
        if widths[-1] < min_w // 2 and len(widths) > 1:
            widths[-2] += widths[-1]
            widths.pop()
        off = int(rng.integers(0, size)) if offset_rows else 0
        x = off
        for w in widths:
            for k in range(w):
                lab[y0:y0 + hgt, (x + k) % size] = nid
            x += w
            nid += 1
        y0 += hgt
    return lab


def ashlar_labels(size=N, row=16, block=32, shift=16, rows_offset=0):
    lab = np.zeros((size, size), int)
    for y in range(size):
        r = ((y + rows_offset) // row)
        for x in range(size):
            xs = (x + (r % 2) * shift) % size
            lab[y, x] = r * 100 + xs // block
    return lab


def ashlar(rng, row=16, block=32, shift=16, rock_tone=1.0, tint=6.0, relief=4.0, darken=1.0,
           gap_rgb=(112, 104, 92), chip=0.0, cracks=0, stain=0.0, lab=None):
    rock = rock_rgb(rng, tone=rock_tone, vugs=False)
    # tooled face: faint vertical tooling marks
    tool = noise(rng, beta=1.0, lo=4, aniso=0.15)
    rock = rock + 3.0 * tool[..., None]
    if lab is None:
        lab = ashlar_labels(row=row, block=block, shift=shift)
    gap = boundary(lab, both=False)
    out = stones_render(rng, lab, gap, rock, relief=relief, bevel=2.0, tint=tint, gap_rgb=gap_rgb,
                        gap_noise=5.0, darken=darken, chip=chip)
    if stain > 0:
        s = noise(rng, beta=2.6, lo=1, hi=6)
        out *= (1.0 - stain * np.clip(s, 0, 3) / 3.0)[..., None]
    for _ in range(cracks):
        crack(rng, out)
    return out


def crack(rng, rgb, length=None):
    size = rgb.shape[0]
    y, x = rng.integers(0, size, 2).astype(float)
    ang = rng.random() * np.pi
    length = length or int(rng.integers(8, 18))
    for _ in range(length):
        yi, xi = int(y) % size, int(x) % size
        rgb[yi, xi] = rgb[yi, xi] * 0.55
        rgb[(yi + 1) % size, xi] = rgb[(yi + 1) % size, xi] * 1.06
        ang += rng.normal(0, 0.45)
        y += np.sin(ang)
        x += np.cos(ang)


def drystone(rng, variant):
    """Dry-laid wall: flat stones in irregular courses, deep dark joints, no mortar."""
    rock = rock_rgb(rng)
    hs = {0: [6, 9, 7, 10], 1: [8, 5, 11, 8], 2: [7, 7, 9, 9], 3: [10, 6, 8, 8],
          4: [5, 8, 6, 7, 6], 5: [9, 9, 6, 8], 6: [6, 10, 6, 10], 7: [8, 8, 8, 8]}[variant]
    lab = course_labels(rng, hs, 6, 15)
    # irregular stone outlines: shift labels along noisy boundaries
    wy = np.rint(noise(rng, beta=2.5, lo=1, hi=5) * 1.0).astype(int)
    wx = np.rint(noise(rng, beta=2.5, lo=1, hi=5) * 1.4).astype(int)
    yy, xx = np.mgrid[0:N, 0:N]
    lab = lab[(yy + wy) % N, (xx + wx) % N]
    gap = boundary(lab, both=True)
    gap &= ~((rng.random(gap.shape) < 0.18) & ~boundary(lab, both=False))
    if variant >= 5:
        # a few small pinning stones (chinks) wedged in the joints
        for _ in range(3):
            y, x = rng.integers(0, N, 2)
            gap[y, x] = False
            gap[y, (x + 1) % N] = False
            lab[y, x] = lab[y, (x + 1) % N] = 999 + _
    return stones_render(rng, lab, gap, rock, relief=6.0, bevel=2.5, tint=10.0,
                         gap_rgb=(58, 53, 45), gap_noise=6.0)


def fanned(rng, variant):
    """Segmental (fan) paving: rings of small setts around a centre below the tile."""
    rock = rock_rgb(rng)
    yy, xx = np.mgrid[0:N, 0:N].astype(float) + 0.5
    cy, cx = [(N + 6, N / 2), (N + 2, N / 2 + 4), (N + 8, N / 2 - 3), (N + 4, N / 2)][variant]
    # tile repeats: measure from the nearest copy of the centre horizontally
    best_r = np.full((N, N), 1e9)
    best_a = np.zeros((N, N))
    for ox in (-N, 0, N):
        for oy in (-N, 0):
            r = np.hypot(yy - (cy + oy), xx - (cx + ox))
            a = np.arctan2(xx - (cx + ox), -(yy - (cy + oy)))
            m = r < best_r
            best_r[m] = r[m]
            best_a[m] = a[m]
    ring_w = 4.6
    ring = np.floor(best_r / ring_w).astype(int)
    seg_len = 4.8
    seg = np.floor(best_a * (ring + 0.5) * ring_w / seg_len + (ring % 2) * 0.5).astype(int)
    lab = ring * 1000 + seg
    gap = boundary(lab, both=False)
    return stones_render(rng, lab, gap, rock, relief=6.0, bevel=2.0, tint=8.0,
                         gap_rgb=(76, 70, 60), gap_noise=6.0)


# ---------------------------------------------------------------- loose material

def grains(rng, base_stops, count, rmin, rmax, palette, size=N, gap_rgb=None, relief=6.0):
    """Gravel / pebbles: voronoi pieces on a dark matrix."""
    lab, f1, f2 = voronoi_stones(rng, count, jitter_min=rmin)
    wob = noise(rng, beta=1.4, lo=3) * 0.4
    gap = (f2 - f1) < (1.3 + wob)
    cols = palette
    rock = ramp(noise(rng, beta=0.8, lo=3) * 0.8, base_stops)
    out = np.empty((size, size, 3))
    for i in np.unique(lab):
        m = lab == i
        c = np.array(cols[rng.integers(0, len(cols))], float) + rng.normal(0, 6)
        out[m] = c + (rock[m] - rock.mean(axis=(0, 1)))
    d = edge_distance(gap, cap=6)
    h = np.sqrt(np.clip(d / 2.0, 0, 1))
    out *= (bevel_light(h, relief * 0.12) * (0.85 + 0.15 * np.clip(d / 2, 0, 1)))[..., None]
    g = np.array(gap_rgb, float) + rng.normal(0, 8, (size, size, 1))
    out[gap] = g[gap]
    return out


DOLO_PIECES = [(198, 191, 175), (184, 177, 162), (212, 206, 192), (170, 164, 150), (192, 186, 172)]


def sand_rgb(rng, size=N, base=(206, 197, 176)):
    f = noise(rng, size, beta=0.3, lo=8)
    m = noise(rng, size, beta=2.4, lo=1, hi=5)
    rgb = np.array(base, float)[None, None, :] + (9 * f + 4 * m)[..., None]
    rgb = speckle(rng, rgb, 0.05, (232, 226, 212), 5)
    rgb = speckle(rng, rgb, 0.03, (150, 142, 126), 8)
    rgb = speckle(rng, rgb, 0.01, (120, 110, 100), 8)
    return rgb


def wavy_sand(rng, size=4 * N, base=(206, 197, 176)):
    """128x128 ripple field cut into a 4x4 tile set (tile 1 top-left, row by row)."""
    yy, xx = np.mgrid[0:size, 0:size].astype(float)
    warp = noise(rng, size, beta=2.6, lo=1, hi=6) * 0.9
    # integer wave vector keeps the field periodic
    ph = 2 * np.pi * (xx * 3 + yy * 11) / size + warp
    s = np.sin(ph)
    # asymmetric ripple: gentle stoss slope, steep shaded lee
    a = np.abs(s)
    prof = np.where(s > 0, a ** 0.6, -(a ** 1.6))
    lit = np.cos(ph)
    f = noise(rng, size, beta=0.3, lo=24)
    rgb = np.array(base, float)[None, None, :] + (8 * prof + 11 * lit + 7 * f)[..., None]
    rgb = speckle(rng, rgb, 0.04, (232, 226, 212), 5)
    rgb = speckle(rng, rgb, 0.025, (150, 142, 126), 8)
    tiles = []
    for ty in range(4):
        for tx in range(4):
            tiles.append(rgb[ty * N:(ty + 1) * N, tx * N:(tx + 1) * N].copy())
    return tiles


# ---------------------------------------------------------------- earth ground blocks

PEBBLES = {
    "reddish": [(132, 128, 124), (156, 150, 144), (176, 128, 112), (96, 92, 92), (206, 200, 190),
                (120, 108, 96), (168, 160, 150), (70, 68, 70)],
    "grey": [(128, 128, 126), (150, 148, 144), (164, 124, 112), (88, 88, 90), (198, 194, 186),
             (110, 104, 98), (160, 156, 150), (62, 62, 66)],
}
TILL_MATRIX = {
    # reddish-brown Weichselian till; dense blue-grey Saalian till
    "reddish": [(-2.5, (112, 70, 50)), (-1, (134, 88, 64)), (0, (148, 100, 74)), (1, (162, 114, 86)), (2.5, (176, 130, 100))],
    "grey": [(-2.5, (84, 90, 96)), (-1, (100, 107, 112)), (0, (112, 119, 124)), (1, (124, 130, 134)), (2.5, (138, 144, 146))],
}


def pebble(rgb, rng, cy, cx, r, colour, size=N):
    """Rounded clast with light from the top left and a contact shadow, wrapped."""
    col = np.array(colour, float)
    rr = int(np.ceil(r)) + 1
    for dy in range(-rr, rr + 1):
        for dx in range(-rr, rr + 1):
            q = ((dy / (r * rng.uniform(0.95, 1.05))) ** 2 + (dx / (r * 1.15)) ** 2)
            y, x = (cy + dy) % size, (cx + dx) % size
            if q <= 1.0:
                lightv = 1.0 + 0.22 * (-(dx + dy) / max(r, 1)) * (1 - 0.3 * q)
                rgb[y, x] = col * lightv + rng.normal(0, 4, 3)
            elif q <= 1.6 and dy + dx > 0:
                rgb[y, x] = rgb[y, x] * 0.78


TILL_CLASTS = {
    # Scandinavian erratics in the Baltic tills: grey gneiss and granite, pink granite, dark diabase, pale quartzite
    "reddish": [(150, 140, 132), (128, 122, 118), (168, 134, 120), (106, 100, 98), (172, 166, 156), (136, 116, 102)],
    "grey": [(146, 144, 140), (160, 156, 148), (154, 126, 116), (86, 86, 90), (170, 166, 158), (132, 122, 112)],
}


def clast(rgb, rng, cy, cx, r, colour, size=N):
    """Sub-angular clast of irregular outline, lit from the top left, with a faint contact shadow; wrapped."""
    col = np.array(colour, float)
    ang, asp = rng.uniform(0, np.pi), rng.uniform(0.6, 1.0)
    lobes = [(k, rng.normal(0, 0.14), rng.uniform(0, 2 * np.pi)) for k in (2, 3, 5)]
    rr = int(np.ceil(r * 1.4)) + 1
    for dy in range(-rr, rr + 1):
        for dx in range(-rr, rr + 1):
            u = dx * np.cos(ang) + dy * np.sin(ang)
            v = (-dx * np.sin(ang) + dy * np.cos(ang)) / asp
            rho, th = np.hypot(u, v), np.arctan2(v, u)
            rad = r * (1.0 + sum(a * np.cos(k * th + p) for k, a, p in lobes))
            y, x = (cy + dy) % size, (cx + dx) % size
            if rho <= rad:
                rgb[y, x] = col * (1.0 + 0.09 * (-(dx + dy)) / max(r, 1.0)) + rng.normal(0, 3, 3)
            elif rho <= rad + 0.9 and dx + dy > 0:
                rgb[y, x] = rgb[y, x] * 0.9


def till(rng, kind, variant):
    """Unsorted glacial till: an even sandy-silty matrix with sparse small clasts of mixed rock."""
    base = np.array(TILL_MATRIX[kind][2][1], float)
    k = 1.0 if kind == "reddish" else 0.85  # the grey Saalian till is denser and more uniform
    g = fine_grain(rng, coarse=0.5)
    drift = noise(rng, beta=2.0, lo=2, hi=8)
    rgb = base + (g[..., None] * 7.5 + drift[..., None] * 1.5) * k
    # sand and silt grains in the matrix, grit of the clast rocks
    sand = (170, 128, 100) if kind == "reddish" else (134, 140, 142)
    rgb = speckle(rng, rgb, 0.05, sand, 5)
    rgb = speckle(rng, rgb, 0.04, (122, 82, 62) if kind == "reddish" else (92, 98, 104), 5)
    pal = TILL_CLASTS[kind]
    rgb = speckle(rng, rgb, 0.012, pal[4], 6)
    rgb = speckle(rng, rgb, 0.01, pal[3], 6)
    # clasts: the same small number in every variant, kept apart so none cluster into a memorable group
    placed = []
    for i in range(9):
        r = rng.uniform(1.5, 2.0) if i < 2 else rng.uniform(0.7, 1.3)
        for _ in range(40):
            y, x = rng.integers(0, N, 2)
            if all(min(abs(y - py), N - abs(y - py)) ** 2 + min(abs(x - px), N - abs(x - px)) ** 2 >= 30
                   for py, px in placed):
                break
        placed.append((y, x))
        clast(rgb, rng, y, x, r, np.array(pal[rng.integers(0, len(pal))]) + rng.normal(0, 4))
    return rgb


def silt(rng, variant):
    # fine, even and pale: grain no coarser than a few pixels, faint horizontal lamination at the same rows in every
    # variant (so it runs on across blocks), a couple of root/void pin-holes kept inside the tile, and the same mean
    # colour and contrast in every variant (no patchwork)
    stops = [(-2.5, (160, 150, 128)), (-1, (176, 166, 144)), (0, (186, 177, 156)), (1, (196, 188, 168)),
             (2.5, (210, 203, 184))]
    f = 0.6 * noise(rng, beta=0.4, lo=8) + 0.35 * noise(rng, beta=1.4, lo=4, hi=12) \
        + 0.25 * noise(rng, beta=1.6, lo=4, hi=12, aniso=0.4)
    rgb = ramp(f, stops)
    m = rng.random((N, N)) < 0.025
    rgb[m] = rgb[m] * 0.2 + (np.array((210, 204, 188)) + rng.normal(0, 4, (int(m.sum()), 1))) * 0.8
    holes = []
    while len(holes) < 2:
        p = rng.integers(2, N - 3, 2)
        if all(abs(p - q).max() > 8 for q in holes):
            holes.append(p)
    for y, x in holes:
        rgb[y, x] = rgb[y, x] * 0.3 + np.array((132, 122, 104)) * 0.7
        rgb[y + 1, x] = rgb[y + 1, x] * 1.03
    rgb = np.array((186.5, 177.6, 156.8)) + (rgb - rgb.mean(axis=(0, 1))) * (
        7.5 / (rgb @ np.array([0.299, 0.587, 0.114])).std())
    # shared lamination: one row profile for all variants, gently wavy inside the tile and level at its edges
    k = np.abs(np.fft.fftfreq(N) * N)
    prof = np.real(np.fft.ifft(np.fft.fft(rng_for("silt:laminae").standard_normal(N))
                               * np.where((k >= 3) & (k <= 12), np.maximum(k, 1) ** -0.7, 0.0)))
    prof = prof / prof.std()
    yy, xx = np.mgrid[0:N, 0:N].astype(float)
    wob = 0.6 * noise(rng, beta=2.0, lo=1, hi=3)[0] * np.sin(np.pi * (xx[0] + 0.5) / N)
    lam = np.interp((yy + wob[None, :]) % N, np.arange(N + 1), np.append(prof, prof[0]))
    return rgb + (lam - lam.mean())[..., None] * np.array([3.4, 3.4, 3.0])


MARL = {
    # base colour, grain, lamination strength
    "devonian": ((138, 146, 128), (5.4, 5.6, 5.0), (2.4, 2.4, 2.2)),
    "lake": ((220, 213, 190), (4.8, 4.8, 5.2), (2.0, 2.2, 2.8)),
}


def marl(rng, kind, variant):
    """Calcareous mudstone: fine even grain, thin laminae on the same rows in every variant."""
    base, grain_amp, lam_amp = (np.array(c, float) for c in MARL[kind])
    lam = shared_rows(f"marl-{kind}:rows", N, lo=3, hi=14, beta=0.8)[:, None]
    # each lamina a little stronger or fainter along its length, so the variants differ in detail only
    lam = lam * (1.0 + 0.3 * noise(rng, beta=2.0, lo=1, hi=5, aniso=0.3))
    g = fine_grain(rng, coarse=0.5)
    drift = noise(rng, beta=2.0, lo=2, hi=8)
    rgb = base + g[..., None] * grain_amp + lam[..., None] * lam_amp + drift[..., None] * 1.2
    if kind == "devonian":
        # variegated: small reddish-brown and darker grey-green mottles, as in the Baltic Devonian marls
        mot = noise(rng, beta=1.6, lo=4, hi=10)
        red, green = np.clip(mot - 0.8, 0, 1.2)[..., None], np.clip(-mot - 1.2, 0, 1)[..., None]
        rgb = rgb + red * np.array((22.0, -5.0, -11.0)) + green * np.array((-4.0, -1.5, -4.0))
        rgb = speckle(rng, rgb, 0.012, (112, 120, 106), 5)
    else:
        # freshwater lime: tiny white shell fragments and a few dark plant traces lying in the laminae
        rgb = speckle(rng, rgb, 0.025, (238, 234, 222), 3)
        for _ in range(int(rng.integers(2, 4))):
            y, x = rng.integers(0, N, 2)
            for d in range(int(rng.integers(1, 3))):
                rgb[y, (x + d) % N] = (176, 166, 140)
    return rgb


# ---------------------------------------------------------------- placeholder ground blocks (7 November)
# Generic looks for the world-wide ground types; good enough to tell them apart in a shaft, to be redrawn later.

def soft(rng, matrix, smooth=0.6, grain=0.45, lam=0.0, specks=(), clasts=None, mottle=None, nodules=None, cracks=0):
    """Earthy matrix (colour stops) with optional lamination, speckles, clasts, mottles, nodules and cracks."""
    f = smooth * noise(rng, beta=2.3, lo=1, hi=7) + grain * noise(rng, beta=0.3, lo=7)
    if lam:
        f = f + lam * noise(rng, beta=2.0, lo=1, hi=10, aniso=1 / 8.0)
    rgb = ramp(f, matrix)
    if mottle:
        colour, share, scale = mottle
        m = noise(rng, beta=2.6, lo=1, hi=scale)
        cut = np.quantile(m, 1 - share)
        k = np.clip((m - cut) * 1.5, 0, 1)[..., None]
        rgb = rgb * (1 - k) + (np.array(colour, float) + 6 * noise(rng, beta=0.5, lo=6)[..., None]) * k
    for prob, colour in specks:
        rgb = speckle(rng, rgb, prob, colour, 7)
    if clasts:
        palette, count, rmin, rmax = clasts
        for _ in range(count):
            y, x = rng.integers(0, N, 2)
            pebble(rgb, rng, y, x, rng.uniform(rmin, rmax), palette[rng.integers(0, len(palette))])
    if nodules:
        colour, count = nodules
        for _ in range(count):
            y, x = rng.integers(0, N, 2)
            pebble(rgb, rng, y, x, rng.uniform(0.8, 1.8), colour)
    for _ in range(cracks):
        crack(rng, rgb)
    return rgb


def stops(c, spread=16):
    c = np.array(c, float)
    return [(-2.5, tuple(c - 2.2 * spread)), (-1, tuple(c - spread)), (0, tuple(c)), (1, tuple(c + spread * 0.8)),
            (2.5, tuple(c + 1.8 * spread))]


SOFT = {
    # name: (variants, keyword arguments for soft())
    "saprolite-felsic": (3, dict(matrix=stops((176, 150, 120), 14), lam=0.35, specks=[(0.05, (226, 220, 206)), (0.02, (120, 96, 72))],
                                 mottle=((196, 120, 84), 0.18, 5))),
    "saprolite-mafic": (3, dict(matrix=stops((128, 84, 62), 13), specks=[(0.03, (92, 96, 70)), (0.02, (170, 120, 90))],
                                mottle=((96, 104, 80), 0.15, 5))),
    "laterite-mottled": (3, dict(matrix=stops((176, 92, 60), 14), mottle=((214, 196, 170), 0.3, 6), specks=[(0.02, (110, 50, 34))])),
    "laterite-hard": (3, dict(matrix=stops((122, 52, 34), 12), grain=0.6, nodules=((150, 66, 40), 14),
                              specks=[(0.04, (70, 34, 26)), (0.02, (176, 110, 70))])),
    "calcrete": (3, dict(matrix=stops((214, 204, 182), 10), nodules=((232, 226, 210), 10), specks=[(0.03, (170, 150, 120))], cracks=2)),
    "loess": (4, dict(matrix=stops((196, 172, 130), 9), grain=0.35, specks=[(0.02, (216, 200, 170)), (0.01, (130, 110, 84))])),
    "tephra": (3, dict(matrix=stops((88, 84, 82), 14), grain=0.8, specks=[(0.08, (150, 140, 128)), (0.05, (40, 38, 40))],
                       clasts=([(70, 66, 66), (120, 108, 100), (150, 90, 70)], 10, 0.7, 1.6))),
    "saltcrust": (3, dict(matrix=stops((232, 230, 224), 8), specks=[(0.06, (250, 250, 248)), (0.02, (190, 182, 170))], cracks=4)),
    "gypcrete": (3, dict(matrix=stops((218, 208, 188), 10), lam=0.3, specks=[(0.05, (244, 242, 236)), (0.02, (184, 168, 140))])),
    "permafrost-silt": (3, dict(matrix=stops((150, 146, 136), 10), lam=0.4, specks=[(0.08, (218, 230, 236)), (0.02, (110, 106, 98))],
                                mottle=((200, 214, 222), 0.12, 8))),
    "permafrost-till": (3, dict(matrix=stops((112, 116, 120), 12), specks=[(0.06, (214, 226, 232))],
                                clasts=(PEBBLES["grey"], 10, 0.6, 1.6), mottle=((196, 210, 218), 0.1, 8))),
    "permafrost-sand": (3, dict(matrix=stops((170, 158, 132), 10), grain=0.8, specks=[(0.08, (220, 232, 238)), (0.03, (130, 120, 100))],
                                mottle=((204, 216, 222), 0.1, 8))),
    "permafrost-gravel": (3, dict(matrix=stops((124, 124, 122), 12), clasts=([(150, 146, 140), (110, 108, 104), (176, 170, 160)], 18, 1.0, 2.6),
                                  specks=[(0.07, (216, 228, 236))], mottle=((196, 210, 218), 0.1, 8))),
    "permafrost-clay": (3, dict(matrix=stops((116, 126, 134), 9), smooth=0.4, lam=0.5, specks=[(0.05, (214, 226, 234))],
                                mottle=((190, 206, 216), 0.14, 8))),
    "ooze-calcareous": (3, dict(matrix=stops((216, 210, 194), 8), smooth=0.4, lam=0.4, specks=[(0.04, (240, 238, 228))])),
    "ooze-siliceous": (3, dict(matrix=stops((170, 160, 132), 9), smooth=0.4, lam=0.4, specks=[(0.03, (200, 196, 180)), (0.02, (120, 112, 92))])),
    "colluvium": (3, dict(matrix=stops((128, 104, 80), 14), clasts=([(140, 132, 120), (110, 100, 92), (170, 160, 146), (96, 88, 80)], 14, 0.8, 2.6))),
    "regolith": (3, dict(matrix=stops((150, 136, 118), 14), clasts=([(156, 150, 140), (130, 124, 116), (180, 172, 160)], 20, 1.2, 3.2),
                         specks=[(0.04, (190, 176, 150))])),
    "terrarossa": (3, dict(matrix=stops((150, 64, 44), 12), specks=[(0.02, (200, 190, 170)), (0.02, (100, 40, 30))])),
}


# ---------------------------------------------------------------- family modules (tools/ground_textures/*.py)
# Each family module owns some materials and may redraw them without touching this file. A module defines
# register(H), where H is this module (helpers: noise, ramp, speckle, pebble, crack, voronoi_stones, grains, soft,
# stops, N ...), and returns a dict with any of:
#   "soft":  {name: (variants, fn(rng, variant) -> float RGB array N x N x 3)}   replaces the SOFT placeholder
#   "rocks": {code: dict(rgb=fn(rng, size=N, tone=1.0, vugs=True), pieces=[5 RGB], sand=RGB, termite=RGB)}
#            replaces the ROCKS entry (any key left out keeps the current value)

FAMILIES = ["foliated", "quartzite_gypsum", "tropical", "dryland", "cold_salt", "ooze_slope"]


def load_families():
    import importlib.util
    soft_fns, rocks = {}, {code: dict(rgb=v[0], pieces=v[1], sand=v[2], termite=v[3]) for code, v in ROCKS.items()}
    here = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ground_textures")
    for fam in FAMILIES:
        path = os.path.join(here, fam + ".py")
        if not os.path.exists(path):
            continue
        spec = importlib.util.spec_from_file_location("ground_textures_" + fam, path)
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        got = mod.register(sys.modules[__name__]) or {}
        for name, entry in got.get("soft", {}).items():
            assert name in SOFT, f"{fam}: {name} is not a ground block"
            soft_fns[name] = entry
        for code, entry in got.get("rocks", {}).items():
            assert code in rocks, f"{fam}: {code} is not a rock"
            rocks[code].update(entry)
    return soft_fns, rocks


# ---------------------------------------------------------------- output

def save(img_rgb, path, written):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    to_img(img_rgb).save(path, optimize=True)
    written.append(path)


def family(code, rgb_fn, pieces, sand_base, termite_base, g, written):
    """Every texture vanilla's rock-derived blocks look up for one rock code. Dolostone keeps its seeds unprefixed."""
    STYLE["rgb"] = rgb_fn
    R = (lambda n: rng_for(n)) if code == "dolostone" else (lambda n: rng_for(code + ":" + n))
    # --- raw rock and loose material
    for v in range(4):
        save(rock_rgb(R(f"rock{v}")), g("rock", f"{code}{v + 1}.png"), written)
    pieces_stops = [(-2, (-14, -14, -14)), (2, (14, 14, 14))]
    r = R("gravel")
    gr = grains(r, pieces_stops, 48, 2.2, 4.0, pieces, gap_rgb=(110, 104, 94), relief=4.0)
    save(gr, g("gravel", f"{code}.png"), written)
    save(sand_rgb(R("sand"), base=sand_base), g("sand", f"{code}.png"), written)
    for i, t in enumerate(wavy_sand(R("sandwavy"), base=sand_base)):
        save(t, g("sand", "wavy", f"{code}{i + 1}.png"), written)

    # --- cobble
    for v in range(5):
        save(cobble(R(f"cobble{v}"), code, count=13 + v % 3), g("cobblestone", f"{code}{v + 1}.png"), written)
    # coursed cobble for slab and stair faces: flatter stones in rows
    save(cobble(R("cobbleslab"), code, count=16, sy=1.7), g("cobblestoneslab", f"{code}.png"), written)
    save(cobble(R("cobblestairs"), code, count=16, sy=1.7), g("cobblestonestairs", f"{code}.png"), written)
    # cobble skull: cobble with a few bone fragments set into darker joints
    r = R("cobbleskull")
    cs = cobble(r, code, count=15, darken=0.82, gap_rgb=(40, 36, 30))
    for _ in range(4):
        y, x = r.integers(0, N, 2)
        pebble(cs, r, y, x, r.uniform(1.4, 2.2), (214, 206, 182))
        cs[(y + 1) % N, x] = (60, 54, 44)
    save(cs, g("cobbleskull", f"{code}.png"), written)
    for v in range(4):
        save(fanned(R(f"fanned{v}"), v), g("cobblestone", "fanned", f"{code}{v + 1}.png"), written)

    # --- ashlar
    for v in range(3):
        r = R(f"brick{v}")
        save(ashlar(r, lab=course_labels(r, [16, 16], 13, 22)), g("brick", f"{code}{v + 1}.png"), written)
    for v in range(4):
        r = R(f"agedbrick{v}")
        lab = course_labels(r, [[8, 8, 8, 8], [10, 6, 10, 6], [8, 8, 16], [6, 10, 8, 8]][v], 8, 16)
        save(ashlar(r, lab=lab, darken=0.9, tint=10.0, chip=0.35, stain=0.18, gap_rgb=(86, 78, 66)),
             g("agedbrick", f"{code}{v + 1}.png"), written)
    for v in range(2):
        save(ashlar(R(f"crackedbrick{v}"), darken=0.8, tint=7.0, chip=0.25, cracks=4, stain=0.25,
                    gap_rgb=(70, 64, 56)), g("crackedbrick", f"{code}{v + 1}.png"), written)

    # --- dry stone
    for v in range(8):
        save(drystone(R(f"drystone{v}"), v), g("drystone", f"{code}{v + 1}.png"), written)
    save(drystone(R("drystonefence"), 2), g("drystonefence", f"{code}.png"), written)
    r = R("drystonefence-edge")
    lab = course_labels(r, [4, 4, 4, 4, 4, 4, 4, 4], 10, 22)
    save(stones_render(r, lab, boundary(lab, both=False), rock_rgb(r), relief=6.0, bevel=1.5,
                       tint=9.0, gap_rgb=(48, 44, 38)), g("drystonefence", f"{code}-edge.png"), written)

    # --- polished
    def polished(r, tone=1.0, seam=None, stain=0.0, tiles=False, chip=0.0):
        rgb = rock_rgb(r, tone=tone, vugs=True)
        rgb = rgb * 0.6 + rgb.mean(axis=(0, 1)) * 0.4  # polishing evens the face out
        rgb = rgb + 4.0 * noise(r, beta=2.0, lo=1, hi=4)[..., None]
        if stain > 0:
            s = noise(r, beta=2.4, lo=1, hi=6)
            rgb *= (1.0 - stain * np.clip(s + 0.5, 0, 3) / 3.0)[..., None]
        if seam is not None:
            rgb[seam] = rgb[seam] * 0.72
            rgb[(seam + 1) % N] = rgb[(seam + 1) % N] * 1.05
        if tiles:
            for k in (0, 16):
                rgb[k, :] *= 0.62
                rgb[:, k] *= 0.62
                rgb[(k + 1) % N, :] *= 1.06
                rgb[:, (k + 1) % N] *= 1.06
        if chip > 0:
            yy, xx = np.mgrid[0:N, 0:N]
            near = ((yy % 16) <= 1) | ((xx % 16) <= 1) | ((yy % 16) >= 15) | ((xx % 16) >= 15)
            m = near & (r.random((N, N)) < chip)
            rgb[m] *= 0.7
        return rgb

    save(polished(R("polished")), g("polishedrock", f"{code}.png"), written)
    save(polished(R("polished-inside"), tone=0.94), g("polishedrock", f"{code}-inside.png"), written)
    save(polished(R("polishedslab"), seam=15), g("polishedrockslab", f"{code}.png"), written)
    for v in range(2):
        save(polished(R(f"pold-full{v}"), tone=0.72, stain=0.35, tiles=True),
             g("polishedrock-old", "full", f"{code}{v + 1}.png"), written)
        save(polished(R(f"pold-cobbled{v}"), tone=0.66, stain=0.4, tiles=True, chip=0.45),
             g("polishedrock-old", "cobbled", f"{code}{v + 1}.png"), written)
    save(polished(R("pold-inside"), tone=0.7, stain=0.3), g("polishedrock-old", "inside", f"{code}.png"), written)

    # --- termite mound: cemented sandy earth with grit of the rock
    r = R("termitemound")
    tm = sand_rgb(r, base=termite_base)
    tm = speckle(r, tm, 0.08, (214, 206, 188), 8)
    tm = speckle(r, tm, 0.04, (150, 112, 76), 8)
    tm *= (1.0 + 0.05 * noise(r, beta=2.0, lo=1, hi=6))[..., None]
    save(tm, g("termitemound", f"{code}.png"), written)
    STYLE["rgb"] = dolostone_rgb


def build(only=None, out=None):
    """Writes the textures (only: names or rock codes to limit it to; out: a preview folder instead of the assets)."""
    written = []
    earth_dir, stone_dir = (os.path.join(out, "earth"), os.path.join(out, "stone")) if out else (EARTH_DIR, STONE_DIR)
    g = lambda *p: os.path.join(stone_dir, *p)
    e = lambda name: os.path.join(earth_dir, name)
    want = lambda name: only is None or name in only
    soft_fns, rocks = load_families()

    # --- Earth blocks
    for kind in ("reddish", "grey"):
        if want(f"till-{kind}"):
            for v in range(4):
                save(till(rng_for(f"till-{kind}{v}"), kind, v), e(f"till-{kind}{v + 1}.png"), written)
    if want("silt"):
        for v in range(4):
            save(silt(rng_for(f"silt{v}"), v), e(f"silt{v + 1}.png"), written)
    for kind in ("devonian", "lake"):
        if want(f"marl-{kind}"):
            for v in range(3):
                save(marl(rng_for(f"marl-{kind}{v}"), kind, v), e(f"marl-{kind}{v + 1}.png"), written)
    for name, (count, kw) in SOFT.items():
        if not want(name):
            continue
        if name in soft_fns:
            count, fn = soft_fns[name]
            for v in range(count):
                save(fn(rng_for(f"{name}{v}"), v), e(f"{name}{v + 1}.png"), written)
        else:
            for v in range(count):
                save(soft(rng_for(f"{name}{v}"), **kw), e(f"{name}{v + 1}.png"), written)

    for code, r in rocks.items():
        if want(code):
            family(code, r["rgb"], r["pieces"] or DOLO_PIECES, r["sand"], r["termite"], g, written)
    return written


def sheet(paths, out, scale=4):
    cols = 8
    cell = N * scale + 4
    rows = (len(paths) + cols - 1) // cols
    im = Image.new("RGBA", (cols * cell, rows * cell), (255, 0, 255, 255))
    for i, p in enumerate(paths):
        t = Image.open(p).resize((N * scale, N * scale), Image.NEAREST)
        im.paste(t, ((i % cols) * cell, (i // cols) * cell))
    im.save(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--sheet", help="also write a contact sheet PNG of every texture")
    ap.add_argument("--only", help="comma-separated block names or rock codes (e.g. saprolite-felsic,gneiss)")
    ap.add_argument("--out", help="write into this preview folder instead of the mod's assets")
    a = ap.parse_args()
    paths = build(set(a.only.split(",")) if a.only else None, a.out)
    for p in paths:
        print(os.path.relpath(p, ROOT).replace("\\", "/"))
    print(f"{len(paths)} textures")
    if a.sheet:
        sheet(paths, a.sheet)


if __name__ == "__main__":
    main()
