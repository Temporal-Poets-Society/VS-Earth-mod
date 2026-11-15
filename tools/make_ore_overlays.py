#!/usr/bin/env python
"""Draw transparent 32x32 ore/gem OVERLAY textures for the Earth mod.

Vanilla ore blocks draw block/stone/rock/<rock>1 and, on top of it, a transparent
block/stone/ore/<type>1..3 overlay.  This script paints those overlays for Earth's
own ores and gems, procedurally and deterministically (seeded per type and variant);
no vanilla or photographic pixels are used.

    python tools/make_ore_overlays.py                  # write assets/earth/textures/block/stone/ore/*
    python tools/make_ore_overlays.py --preview p.png  # also a 4x sheet on granite/limestone/basalt/gneiss
    python tools/make_ore_overlays.py --only vanadinite --no-write --preview p.png

Style follows vanilla: grains of 1-4 px with soft partial-alpha edges, a faint dark
drop shadow on the lower right, a lit upper-left, 12-35 % of the tile covered, and
the rest fully transparent.  Colours follow the item art in art/ores-gems/.
"""
import argparse
import os
import zlib

import numpy as np
from PIL import Image, ImageDraw

N = 32
ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(ROOT, "assets", "earth", "textures", "block", "stone", "ore")
VANILLA = os.path.join(os.environ.get("APPDATA", ""), "Vintagestory", "assets", "survival",
                       "textures", "block", "stone")
EARTH_ROCK = os.path.join(ROOT, "assets", "game", "textures", "block", "stone", "rock")

LIGHT = np.array([-0.62, -0.78])  # light comes from the upper left (screen y down)


# ---------------------------------------------------------------- palettes
def P(dark, mid, lit):
    return [np.array(c, float) for c in (dark, mid, lit)]


def ramp(pal, t):
    """t in [0,1] -> rgb, quantised to 6 steps for a pixel-art feel."""
    t = np.round(np.clip(t, 0, 1) * 5) / 5
    d, m, l = pal
    c = d + (m - d) * (t / 0.5) if t < 0.5 else m + (l - m) * ((t - 0.5) / 0.5)
    gray = c @ np.array([0.3, 0.59, 0.11])
    return gray + (c - gray) * SAT


SAT = 0.74        # overall saturation (vanilla overlays are muted)
AMAX = [0.9]      # peak alpha of a grain; set per recipe so a little rock shows through


# ---------------------------------------------------------------- shape helpers
def rot(p, a):
    c, s = np.cos(a), np.sin(a)
    return np.array([c * p[0] + s * p[1], -s * p[0] + c * p[1]])


def poly_pts(n, r, a0=0.0, sx=1.0, sy=1.0):
    ang = a0 + np.arange(n) * 2 * np.pi / n
    return np.stack([np.cos(ang) * r * sx, np.sin(ang) * r * sy], 1)


def poly_edges(pts):
    """Return outward normals and offsets of a convex polygon (ccw or cw, auto)."""
    n = len(pts)
    c = pts.mean(0)
    nor, off = [], []
    for i in range(n):
        a, b = pts[i], pts[(i + 1) % n]
        e = b - a
        nn = np.array([e[1], -e[0]])
        nn /= np.linalg.norm(nn) + 1e-9
        if np.dot(nn, a - c) < 0:
            nn = -nn
        nor.append(nn)
        off.append(np.dot(nn, a))
    return np.array(nor), np.array(off)


class Grain:
    """A single drawn feature.  kind: poly | disc | fan | blade.  shade: sectors | ramp."""

    def __init__(self, cx, cy, kind, size, angle=0.0, pal=None, shade="ramp", pts=None,
                 noise=0.0, spec=0.0, contrast=1.0, bands=None, pal2=None, split=0.5):
        self.cx, self.cy, self.kind, self.size, self.angle = cx, cy, kind, size, angle
        self.pal, self.pal2, self.shade, self.noise, self.spec = pal, pal2, shade, noise, spec
        self.contrast, self.bands, self.split = contrast, bands, split
        self.pts = pts
        if kind == "poly":
            self.nor, self.off = poly_edges(pts)
            self.R = np.abs(pts).max() + 1.5
        else:
            self.R = size + 1.5

    # signed distance (negative inside) and shading in [0,1]; p is in local coords
    def sample(self, p, rng):
        k = self.kind
        if k == "disc":
            r = self.size
            d = np.hypot(p[0], p[1])
            sd = d - r
            lp = p / max(r, 0.5)
            t = 0.62 + 0.5 * np.dot(lp, LIGHT) - 0.3 * (d / r) ** 2
            return sd, t, 0
        if k == "poly":
            ds = self.nor @ p - self.off
            i = int(np.argmax(ds))
            sd = ds[i]
            if self.shade == "sectors":
                # facet: normal lit-ness; centre gets a gentle highlight
                nl = np.dot(self.nor[i], rot(LIGHT, self.angle))
                t = 0.5 + 0.34 * nl * self.contrast + 0.08 * (1 - min(1.0, np.hypot(p[0], p[1]) / max(self.size, 1)))
                return sd, t, i
            if self.shade == "bevel":
                # flat face with lit top-left edge, dark bottom-right edge (cubes)
                nl = np.dot(self.nor[i], rot(LIGHT, self.angle))
                depth = -sd
                if depth < 1.1:
                    t = 0.5 + 0.55 * np.sign(nl) * min(1.0, abs(nl) + 0.35)
                else:
                    t = 0.5 + 0.08 * np.dot(p / max(self.size, 1), -LIGHT)
                return sd, t, i
            if self.shade == "bands":
                # prism seen from the side: bands across its width
                w = self.size
                u = p[1] / max(w, 0.5)
                ll = rot(LIGHT, self.angle)[1]
                t = 0.58 + 0.38 * ll * np.clip(u, -1, 1) * 1.3 + 0.18 * (1 - abs(u)) ** 2
                return sd, t, i
            return sd, 0.55, i
        if k == "blade":
            # slender tapered blade from (0,0) to (L,0); size = length, bands[0] = half width
            L, w = self.size, self.bands[0]
            x = p[0] / L
            hw = w * (1.0 - 0.55 * x) if 0 <= x <= 1 else w
            sd = max(abs(p[1]) - hw, -p[0] - 0.0, p[0] - L)
            ll = rot(LIGHT, self.angle)[1]
            t = 0.45 + 0.3 * x + 0.3 * ll * np.clip(p[1] / max(w, 0.3), -1, 1)
            return sd, t, 0
        raise ValueError(k)


def render_grain(g, rng):
    """Return (alpha, rgb, bbox) arrays covering the grain's bounding box (unwrapped)."""
    x0, x1 = int(np.floor(g.cx - g.R)), int(np.ceil(g.cx + g.R)) + 1
    y0, y1 = int(np.floor(g.cy - g.R)), int(np.ceil(g.cy + g.R)) + 1
    w, h = x1 - x0, y1 - y0
    A = np.zeros((h, w))
    C = np.zeros((h, w, 3))
    best = (9, None)
    for j in range(h):
        for i in range(w):
            p = rot(np.array([x0 + i + 0.5 - g.cx, y0 + j + 0.5 - g.cy]), g.angle)
            sd, t, f = g.sample(p, rng)
            a = np.clip(0.55 - sd * 1.05, 0, 1) * AMAX[0] * (1 - 0.1 * rng.random())
            if a <= 0:
                continue
            t += rng.normal(0, g.noise) if g.noise else 0
            pal = g.pal
            if g.pal2 is not None:
                # two-tone crystals (watermelon tourmaline, tarnish): split along local x
                if (p[0] / max(g.R, 1)) > (g.split * 2 - 1):
                    pal = g.pal2
            col = ramp(pal, t)
            A[j, i] = a
            C[j, i] = col
            dd = np.hypot(x0 + i + 0.5 - (g.cx - 0.25 * g.size), y0 + j + 0.5 - (g.cy - 0.3 * g.size))
            if sd < -0.5 and dd < best[0]:
                best = (dd, (j, i, pal))
    if g.spec > 0 and best[1] is not None:
        j, i, pal = best[1]
        C[j, i] = C[j, i] + (np.array([255, 255, 255.0]) - C[j, i]) * g.spec
        A[j, i] = 1.0
    return A, C, (x0, y0)


def over(canvas_a, canvas_c, A, C, x0, y0, shadow=0.0):
    h, w = A.shape
    for j in range(h):
        for i in range(w):
            a = A[j, i]
            if a <= 0:
                continue
            x, y = (x0 + i) % N, (y0 + j) % N
            ba = canvas_a[y, x]
            na = a + ba * (1 - a)
            if na > 0:
                canvas_c[y, x] = (C[j, i] * a + canvas_c[y, x] * ba * (1 - a)) / na
            canvas_a[y, x] = na


def shadow_layer(A, amount):
    """Offset the grain's alpha by (+1,+1) and darken: a faint drop shadow on the lower right."""
    h, w = A.shape
    S = np.zeros((h, w))
    S[1:, 1:] = A[:-1, :-1]
    return S * amount


def value_noise(rng, cells=4):
    g = rng.random((cells, cells))
    xs = np.linspace(0, cells, N, endpoint=False)
    out = np.zeros((N, N))
    for y in range(N):
        for x in range(N):
            x0, y0 = int(xs[x]) % cells, int(xs[y]) % cells
            x1, y1 = (x0 + 1) % cells, (y0 + 1) % cells
            fx, fy = xs[x] - int(xs[x]), xs[y] - int(xs[y])
            fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
            out[y, x] = (g[y0, x0] * (1 - fx) + g[y0, x1] * fx) * (1 - fy) + \
                        (g[y1, x0] * (1 - fx) + g[y1, x1] * fx) * fy
    return out


# ---------------------------------------------------------------- ore recipes
# Each recipe(rng) yields grain factories (callables taking cx, cy) as "clusters": a list of
# Grain objects around a seed point.  `cover` is the target fraction of tile with alpha>0.4.
def U(rng, a, b):
    return rng.uniform(a, b)


def blob_pts(rng, r, n=7, jitter=0.28, sx=1.0, sy=1.0):
    ang = np.sort(rng.uniform(0, 2 * np.pi, n))
    ang = np.arange(n) * 2 * np.pi / n + rng.normal(0, 0.15, n)
    rr = r * (1 + rng.uniform(-jitter, jitter, n))
    # keep convex-ish by averaging with neighbours
    rr = 0.5 * rr + 0.25 * (np.roll(rr, 1) + np.roll(rr, -1))
    return np.stack([np.cos(ang) * rr * sx, np.sin(ang) * rr * sy], 1)


PAL = {
    "brass": P((104, 74, 18), (176, 140, 40), (232, 204, 96)),
    "tarnish": P((52, 40, 104), (104, 84, 168), (80, 170, 190)),
    "steel_dark": P((20, 20, 26), (62, 64, 70), (128, 132, 146)),
    "tinwhite": P((92, 94, 98), (178, 182, 186), (240, 243, 246)),
    "azure": P((10, 22, 96), (24, 62, 170), (92, 150, 232)),
    "cerussite": P((98, 104, 98), (186, 186, 176), (252, 252, 246)),
    "vanadinite": P((104, 22, 16), (190, 48, 26), (240, 120, 66)),
    "wulfenite": P((170, 92, 16), (236, 146, 28), (255, 214, 96)),
    "smithsonite": P((50, 120, 120), (118, 196, 186), (216, 244, 234)),
    "smithpale": P((120, 160, 150), (190, 222, 212), (240, 252, 246)),
    "hemimorphite": P((96, 140, 170), (170, 214, 236), (240, 250, 255)),
    "pyrite": P((128, 104, 34), (214, 188, 84), (252, 242, 176)),
    # aluminosilicates of metamorphic rock (EA-0068): kyanite's blue blades, sillimanite's white fibres, andalusite's
    # pink-brown square prisms (chiastolite shows a dark cross in section)
    "kyanite": P((30, 62, 128), (74, 122, 196), (170, 204, 240)),
    "kyan_pale": P((96, 120, 150), (150, 178, 210), (220, 232, 246)),
    "sillimanite": P((126, 120, 108), (204, 198, 184), (246, 244, 236)),
    "andalusite": P((92, 52, 46), (164, 106, 92), (222, 176, 160)),
    "chiastolite": P((24, 20, 20), (52, 44, 42), (84, 74, 70)),
    "topaz": P((150, 82, 24), (224, 148, 48), (250, 222, 150)),
    "tourmaline": P((16, 70, 44), (46, 138, 82), (140, 206, 160)),
    "tourm_pink": P((150, 40, 100), (222, 96, 150), (250, 178, 206)),
    "beryl": P((38, 120, 150), (96, 190, 206), (196, 250, 252)),
    "spinel": P((150, 20, 80), (214, 52, 118), (254, 150, 192)),
    "garnet": P((70, 26, 30), (118, 44, 46), (168, 92, 86)),
    "ruby": P((130, 20, 56), (204, 50, 90), (250, 150, 172)),
    "sapphire": P((18, 42, 140), (46, 88, 204), (130, 180, 255)),
}


def g_blob(rng, cx, cy, r, pal, noise=0.05, spec=0.0, shade="ramp", pal2=None, split=0.5):
    pts = blob_pts(rng, r, 6, 0.42)
    return Grain(cx, cy, "poly", r, rng.uniform(0, 6.3), pal, shade, pts, noise, spec,
                 pal2=pal2, split=split)


def cluster(rng, kind_fn, k_range, spread):
    def f(cx, cy):
        out = []
        for _ in range(rng.integers(k_range[0], k_range[1] + 1)):
            a, d = rng.uniform(0, 6.283), abs(rng.normal(0, spread))
            out.append(kind_fn(cx + np.cos(a) * d, cy + np.sin(a) * d))
        return out
    return f


def recipe_chalcopyrite(rng):
    def one(cx, cy):
        r = U(rng, 1.1, 2.3)
        tar = rng.random() < 0.15
        g = g_blob(rng, cx, cy, r, PAL["tarnish"] if tar else PAL["brass"], 0.09, 0.0)
        if not tar and rng.random() < 0.5:
            g.pal2, g.split = PAL["tarnish"], U(rng, 0.55, 0.8)   # tarnish creeping in on one rim
        return g
    return dict(make=cluster(rng, one, (1, 3), 1.7), cover=0.20, seeds=1.0, mind=3.0)


def recipe_tetrahedrite(rng):
    def one(cx, cy):
        r = U(rng, 1.3, 2.6)
        n = int(rng.choice([3, 4, 5]))       # angular, tetrahedral-ish (few facets)
        pts = poly_pts(n, r, rng.uniform(0, 6.3), U(rng, 0.85, 1.2), U(rng, 0.85, 1.1))
        return Grain(cx, cy, "poly", r, rng.uniform(0, 6.3), PAL["steel_dark"], "sectors", pts,
                     noise=0.05, spec=0.0 if r < 2.2 else 0.55, contrast=1.15)
    return dict(make=cluster(rng, one, (1, 2), 1.4), cover=0.24, seeds=1.0, mind=3.2)


def recipe_sperrylite(rng):
    def one(cx, cy):
        s = U(rng, 0.55, 0.95)           # 1-2 px bright cubes, never lines
        pts = poly_pts(4, s * 1.41, np.pi / 4)
        return Grain(cx, cy, "poly", s, rng.uniform(-0.3, 0.3), PAL["tinwhite"], "ramp", pts,
                     noise=0.03, spec=0.8)
    return dict(make=cluster(rng, one, (1, 3), 1.1), cover=0.09, seeds=0.8, mind=4.6)


def recipe_azurite(rng):
    def f(cx, cy):
        out = []
        n = int(rng.integers(5, 8))
        a0 = rng.uniform(0, 6.28)
        for i in range(n):
            a = a0 + i * 6.283 / n + rng.normal(0, 0.14)
            L = U(rng, 2.2, 4.2)
            out.append(Grain(cx, cy, "blade", L, a, PAL["azure"], "ramp", None, noise=0.04,
                             bands=[U(rng, 0.6, 0.95)]))
        out.append(Grain(cx, cy, "disc", U(rng, 0.9, 1.5), 0, PAL["azure"], "ramp", noise=0.04,
                         spec=0.6))
        return out
    return dict(make=f, cover=0.17, seeds=1.0, mind=5.5)


def recipe_cerussite(rng):
    def f(cx, cy):
        out = []
        a0 = rng.uniform(0, 3.14)
        for i in range(int(rng.integers(2, 4))):    # reticulated / twinned stubby prisms
            L, w = U(rng, 1.6, 2.6), U(rng, 0.6, 0.85)
            pts = np.array([[-L, -w], [L, -w], [L + 0.7, 0], [L, w], [-L, w], [-L - 0.7, 0]])
            out.append(Grain(cx, cy, "poly", w, a0 + i * U(rng, 0.9, 1.3), PAL["cerussite"],
                             "bands", pts, noise=0.04))
        return out
    return dict(make=f, cover=0.17, seeds=1.0, mind=5.0)


def recipe_vanadinite(rng):
    def one(cx, cy):
        r = U(rng, 0.9, 1.6)
        pts = poly_pts(6, r, rng.uniform(0, 1.0), 1.0, 1.0)
        return Grain(cx, cy, "poly", r, rng.uniform(0, 1.0), PAL["vanadinite"], "sectors", pts,
                     noise=0.04, spec=0.45, contrast=1.0)
    return dict(make=cluster(rng, one, (2, 5), 1.7), cover=0.18, seeds=1.0, mind=3.6)


def recipe_wulfenite(rng):
    def one(cx, cy):
        w, h = U(rng, 1.4, 2.2), U(rng, 1.0, 1.4)    # thin square plates seen obliquely
        pts = np.array([[-w, -h], [w, -h], [w, h], [-w, h]])
        return Grain(cx, cy, "poly", h, rng.uniform(0, 3.14), PAL["wulfenite"], "bevel", pts,
                     noise=0.05, spec=0.35)
    return dict(make=cluster(rng, one, (1, 3), 2.0), cover=0.17, seeds=1.0, mind=4.0)


def recipe_smithsonite(rng):
    def f(cx, cy):
        out = []
        for _ in range(int(rng.integers(3, 6))):    # botryoidal: lumpy overlapping spheres
            a, d = rng.uniform(0, 6.28), U(rng, 0, 2.0)
            out.append(Grain(cx + np.cos(a) * d, cy + np.sin(a) * d, "disc", U(rng, 1.0, 1.8), 0,
                             PAL["smithsonite"] if rng.random() < 0.7 else PAL["smithpale"], "ramp", noise=0.04, spec=0.4))
        return out
    return dict(make=f, cover=0.20, seeds=1.0, mind=5.0)


def recipe_hemimorphite(rng):
    def f(cx, cy):
        out = []
        base = rng.uniform(0, 6.28)
        n = int(rng.integers(4, 7))
        for i in range(n):    # fan of slender blades spreading over ~100 degrees
            a = base + (i - (n - 1) / 2) * 0.2 + rng.normal(0, 0.02)
            out.append(Grain(cx, cy, "blade", U(rng, 3.2, 5.0), a, PAL["hemimorphite"], "ramp",
                             None, noise=0.04, bands=[U(rng, 0.75, 1.05)]))
        return out
    return dict(make=f, cover=0.17, seeds=1.0, mind=6.0)


def recipe_pyrite(rng):
    def one(cx, cy):
        s = U(rng, 1.0, 1.9)
        pts = np.array([[-s, -s], [s, -s], [s, s], [-s, s]])
        return Grain(cx, cy, "poly", s, rng.uniform(-0.5, 0.5), PAL["pyrite"], "bevel", pts,
                     noise=0.03, spec=0.0)
    return dict(make=cluster(rng, one, (1, 4), 2.0), cover=0.18, seeds=1.0, mind=3.4)


def recipe_kyanite(rng):
    def f(cx, cy):
        out = []
        a = rng.uniform(0, 6.28)
        for i in range(int(rng.integers(1, 3))):    # long flat blades, one or two side by side, blue to grey-blue
            out.append(Grain(cx + i * 1.2 * np.sin(a), cy + i * 1.2 * np.cos(a), "blade", U(rng, 3.6, 5.6), a + rng.normal(0, 0.06),
                             PAL["kyanite"] if rng.random() < 0.75 else PAL["kyan_pale"], "ramp", None, noise=0.05,
                             bands=[U(rng, 0.8, 1.15)]))
        return out
    return dict(make=f, cover=0.17, seeds=1.0, mind=5.0)


def recipe_sillimanite(rng):
    def f(cx, cy):
        out = []
        base = rng.uniform(0, 6.28)
        for i in range(int(rng.integers(3, 6))):    # fibrous bundle (fibrolite): thin parallel needles
            off = (i - 2) * 0.7
            out.append(Grain(cx + off * np.sin(base), cy + off * np.cos(base), "blade", U(rng, 3.0, 4.6), base + rng.normal(0, 0.05),
                             PAL["sillimanite"], "ramp", None, noise=0.06, bands=[U(rng, 0.42, 0.6)]))
        return out
    return dict(make=f, cover=0.16, seeds=1.0, mind=5.5)


def recipe_andalusite(rng):
    def f(cx, cy):
        w, h = U(rng, 1.1, 1.6), U(rng, 2.0, 3.0)   # stout square prism, often cut across with chiastolite's dark cross
        a = rng.uniform(0, 3.14)
        pts = np.array([[-w, -h], [w, -h], [w, h], [-w, h]])
        out = [Grain(cx, cy, "poly", h, a, PAL["andalusite"], "bands", pts, noise=0.05, spec=0.1)]
        if rng.random() < 0.45:
            s = min(w, h) * 0.45
            out.append(Grain(cx, cy, "poly", s, a + 0.785, PAL["chiastolite"], "bevel",
                             np.array([[-s, -0.35], [s, -0.35], [s, 0.35], [-s, 0.35]]), noise=0.03))
        return out
    return dict(make=f, cover=0.17, seeds=1.0, mind=4.6)


# ---- gems: small glassy crystals, mostly 1-3 px with the odd 4 px one
def gr(rng, big=0.18):
    return U(rng, 1.9, 2.3) if rng.random() < big else U(rng, 0.8, 1.55)


def gem_prism(rng, cx, cy, pal, w, elong=(1.5, 2.6), pal2=None, split=0.5):
    L = w * U(rng, *elong) + 0.4
    pts = np.array([[-L, -w], [L, -w], [L + w * 0.9, 0], [L, w], [-L, w], [-L - w * 0.9, 0]])
    return Grain(cx, cy, "poly", w, rng.uniform(0, 3.14), pal, "bands", pts, noise=0.02,
                 spec=0.6, pal2=pal2, split=split)


def gem(**kw):
    d = dict(seeds=1.0, mind=4.2, count=(6, 10), gem=True, cover=0.13)
    d.update(kw)
    return d


def recipe_topaz(rng):
    def f(cx, cy):
        return [gem_prism(rng, cx, cy, PAL["topaz"], gr(rng) * 0.7)]
    return gem(make=f, count=(6, 10))


def recipe_tourmaline(rng):
    def f(cx, cy):
        w = gr(rng) * 0.65
        if rng.random() < 0.35:    # watermelon: pink core end, green rim end
            return [gem_prism(rng, cx, cy, PAL["tourmaline"], w, (1.8, 3.0), PAL["tourm_pink"],
                              U(rng, 0.35, 0.6))]
        return [gem_prism(rng, cx, cy, PAL["tourmaline"], w, (1.8, 3.0))]
    return gem(make=f, count=(6, 10))


def recipe_beryl(rng):
    def f(cx, cy):
        r = gr(rng)
        pts = poly_pts(6, r, np.pi / 6)
        return [Grain(cx, cy, "poly", r, rng.uniform(0, 1.0), PAL["beryl"], "sectors", pts,
                      noise=0.02, spec=0.7, contrast=1.3)]
    return gem(make=f, count=(6, 10))


def recipe_spinel(rng):
    def f(cx, cy):
        r = gr(rng) * 1.15
        pts = poly_pts(4, r * 1.2, 0.0)     # octahedron: diamond outline
        return [Grain(cx, cy, "poly", r, rng.uniform(-0.35, 0.35), PAL["spinel"], "sectors", pts,
                      noise=0.02, spec=0.7, contrast=1.3)]
    return gem(make=f, count=(6, 10))


def recipe_garnet(rng):
    # almandine: dull brownish wine-red, rounded 12-sided grains, many small ones scattered
    def f(cx, cy):
        r = U(rng, 0.8, 1.35) if rng.random() < 0.85 else U(rng, 1.8, 2.2)
        pts = poly_pts(10, r, rng.uniform(0, 1.0))
        return [Grain(cx, cy, "poly", r, rng.uniform(0, 1.0), PAL["garnet"], "sectors", pts,
                      noise=0.04, spec=0.0, contrast=0.7)]
    return gem(make=f, count=(9, 15), mind=2.9, cover=0.13)


def recipe_ruby(rng):
    # flat hexagonal plates with a glassy lit edge; few and rare
    def f(cx, cy):
        r = U(rng, 1.3, 2.0) if rng.random() < 0.75 else U(rng, 2.1, 2.4)
        pts = poly_pts(6, r, np.pi / 6)
        return [Grain(cx, cy, "poly", r, rng.uniform(0, 1.0), PAL["ruby"], "bevel", pts,
                      noise=0.02, spec=0.8, contrast=1.0)]
    return gem(make=f, count=(3, 5), mind=7.0, cover=0.10)


def recipe_sapphire(rng):
    def f(cx, cy):
        w = gr(rng) * 0.8
        L = w * U(rng, 0.8, 1.5) + 0.3
        pts = np.array([[-L, -w * 0.8], [-L * 0.3, -w], [L * 0.3, -w], [L, -w * 0.8],
                        [L + 0.7, 0], [L, w * 0.8], [L * 0.3, w], [-L * 0.3, w], [-L, w * 0.8],
                        [-L - 0.7, 0]])
        return [Grain(cx, cy, "poly", w, rng.uniform(0, 3.14), PAL["sapphire"], "sectors", pts,
                      noise=0.02, spec=0.7, contrast=1.3)]
    return gem(make=f, count=(6, 10))


RECIPES = {
    "chalcopyrite": recipe_chalcopyrite, "tetrahedrite": recipe_tetrahedrite,
    "sperrylite": recipe_sperrylite, "azurite": recipe_azurite, "cerussite": recipe_cerussite,
    "vanadinite": recipe_vanadinite, "wulfenite": recipe_wulfenite,
    "smithsonite": recipe_smithsonite, "hemimorphite": recipe_hemimorphite,
    "pyrite": recipe_pyrite,
    "kyanite": recipe_kyanite, "sillimanite": recipe_sillimanite, "andalusite": recipe_andalusite,
    "topaz": recipe_topaz, "tourmaline": recipe_tourmaline, "beryl": recipe_beryl,
    "spinel": recipe_spinel, "garnet": recipe_garnet, "ruby": recipe_ruby,
    "sapphire": recipe_sapphire,
}


# ---------------------------------------------------------------- tile composer
def draw_tile(name, variant):
    rng = np.random.default_rng(zlib.crc32(f"{name}/{variant}".encode()) & 0xFFFFFFFF)
    rec = RECIPES[name](rng)
    AMAX[0] = 0.92 if rec.get('gem') else 0.86
    dens = value_noise(rng, 3)
    alpha = np.zeros((N, N))
    col = np.zeros((N, N, 3))
    centres = []
    target = rec["cover"] * (0.92 + 0.16 * rng.random())
    fixed = rec.get("count")
    want = int(rng.integers(fixed[0], fixed[1] + 1)) if fixed else 999
    placed, tries = 0, 0
    while tries < 600 and placed < want and (alpha > 0.15).mean() < target:
        tries += 1
        cx, cy = rng.uniform(0, N), rng.uniform(0, N)
        if not rec.get("gem") and dens[int(cy) % N, int(cx) % N] < rng.uniform(0.15, 0.75):
            continue       # ore bunches up in richer patches, like vanilla's overlays
        if any(min(abs(cx - x), N - abs(cx - x)) ** 2 + min(abs(cy - y), N - abs(cy - y)) ** 2
               < rec["mind"] ** 2 for x, y in centres):
            continue
        centres.append((cx, cy))
        placed += 1
        grains = rec["make"](cx, cy)
        for g in grains:
            A, C, (x0, y0) = render_grain(g, rng)
            S = shadow_layer(A, 0.42 if rec.get("gem") else 0.32)
            over(alpha, col, S, np.zeros_like(C) + np.array([6, 6, 10.0]), x0, y0)
            over(alpha, col, A, C, x0, y0)
    img = np.zeros((N, N, 4), np.uint8)
    img[..., :3] = np.clip(np.round(col), 0, 255)
    img[..., 3] = np.clip(np.round(alpha * 255), 0, 255)
    img[img[..., 3] < 12] = 0           # drop invisible fringe
    return Image.fromarray(img, "RGBA")


# ---------------------------------------------------------------- preview
def load_rocks():
    rocks = []
    for label, folder, fn in (("granite", VANILLA, "rock/granite1.png"),
                              ("limestone", VANILLA, "rock/limestone1.png"),
                              ("basalt", VANILLA, "rock/basalt1.png"),
                              ("gneiss", None, "gneiss1.png")):
        path = os.path.join(EARTH_ROCK, fn) if folder is None else os.path.join(folder, fn)
        if os.path.isfile(path):
            rocks.append((label, Image.open(path).convert("RGBA").resize((N, N))))
    return rocks


def preview(path, types, existing=("galena", "emerald")):
    rocks = load_rocks()
    S = 4
    tile = N * S
    lab = 92
    cols = len(rocks) * 3
    rows = len(types) + len(existing)
    sheet = Image.new("RGBA", (lab + cols * tile + len(rocks) * 6, 20 + rows * tile), (24, 24, 28, 255))
    d = ImageDraw.Draw(sheet)
    for ri, (rn, _) in enumerate(rocks):
        d.text((lab + ri * (3 * tile + 6) + 4, 4), rn, fill=(220, 220, 220, 255))
    items = [(t, "earth") for t in types] + [(t, "vanilla") for t in existing]
    for row, (t, src) in enumerate(items):
        y = 20 + row * tile
        d.text((4, y + tile // 2 - 6), t + ("" if src == "earth" else " (vanilla)"),
               fill=(230, 230, 230, 255))
        for ri, (_, rock) in enumerate(rocks):
            for v in (1, 2, 3):
                if src == "earth":
                    ov = draw_tile(t, v)
                else:
                    ov = Image.open(os.path.join(VANILLA, "ore", f"{t}{v}.png")).convert("RGBA")
                tl = rock.copy()
                tl.alpha_composite(ov)
                x = lab + ri * (3 * tile + 6) + (v - 1) * tile
                sheet.alpha_composite(tl.resize((tile, tile), Image.NEAREST), (x, y))
    sheet.convert("RGB").save(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", help="write a 4x comparison sheet to this png")
    ap.add_argument("--only", nargs="*", help="limit to these types")
    ap.add_argument("--no-write", action="store_true", help="do not write the overlay pngs")
    ap.add_argument("--out", default=OUT)
    a = ap.parse_args()
    types = a.only or list(RECIPES)
    if not a.no_write:
        os.makedirs(a.out, exist_ok=True)
        for t in types:
            for v in (1, 2, 3):
                draw_tile(t, v).save(os.path.join(a.out, f"{t}{v}.png"))
        print(f"wrote {len(types) * 3} overlays to {a.out}")
    if a.preview:
        preview(a.preview, types)
        print("preview", a.preview)


if __name__ == "__main__":
    main()
