"""Earth's grass season colour maps (assets/earth/textures/environment/seasons/earthgrass-*.png).

Vanilla tints grass by one calendar for the whole world ("seasonalGrass": brown in January to March, olive in April and
May, green to September, straw after). Earth gives each place one of these maps instead, picked from its own monthly
temperature and rain (Core.GrassPhenology): grass is dead and brown while the month is below about 5 C, turns a bright
fresh green over the weeks after it warms, settles to summer green, and goes to straw after the autumn frosts. Two more
maps carry the dry seasons: Mediterranean (green from the autumn rains to May, straw in summer) and steppe (brown winter,
green spring, straw from July).

Each map is 128 x 16 like vanilla's: u is the time of year in the northern calendar (vanilla's shader adds half a year in
the southern hemisphere), v a noise row for small variation. The colours are multipliers on the grey grass texture, so
they are bright and nearly without blue, as vanilla's are. All colours are Earth's own. Run:

    .venv/Scripts/python tools/make_grass_seasons.py
"""
import math
import os

from PIL import Image

OUT = os.path.join(os.path.dirname(__file__), '..', 'assets', 'earth', 'textures', 'environment', 'seasons')
W, H = 128, 16

# tint colours (r, g, b), multipliers on the grey grass texture
MATTED = (122, 92, 46)      # last year's grass, flattened under the snow
DORMANT = (150, 120, 70)    # dead, standing, weathered
STRAW = (204, 170, 92)      # just died back: after frost, or dried out in summer
GOLDEN = (214, 182, 88)     # summer-dry grass in the sun
SHOOTS = (150, 152, 34)     # the first green through the dead thatch
FRESH = (128, 196, 20)      # the spring flush: bright yellow-green
SUMMER = (98, 152, 10)      # full summer green
LATE = (122, 140, 14)       # late summer, duller
WINTER_GREEN = (104, 138, 22)  # mild winter: green, a little dull

# season lengths (share of the year above about 5 C) of the temperate maps; GrassPhenology.Lengths must match
LENGTHS = [0.80, 0.66, 0.52, 0.38]
CENTRE = 0.545  # middle of the growing season, about 18 July (northern calendar)


def lerp(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def path(keys, u):
    """Piecewise-linear colour through (time, colour) keys, around the year."""
    keys = sorted(((k % 1.0), c) for k, c in keys)
    for i, (k, c) in enumerate(keys):
        k2, c2 = keys[(i + 1) % len(keys)]
        span = (k2 - k) % 1.0 or 1.0
        d = (u - k) % 1.0
        if d <= span:
            return lerp(c, c2, d / span)
    return keys[0][1]


def temperate(length):
    g, s = CENTRE - length / 2, CENTRE + length / 2
    k = max(0.5, min(1.2, length / 0.6))
    keys = [
        (g, MATTED),
        (g + 0.03 * k, SHOOTS),
        (g + 0.08 * k, FRESH),
        (g + 0.20 * k, SUMMER),
        (s - 0.08 * k, SUMMER),
        (s - 0.02 * k, LATE),
        (s + 0.03 * k, STRAW),
        (s + 0.12 * k, DORMANT),
    ]
    return lambda u: path(keys, u)


def evergreen(u):
    # mild winters: green all year, freshest in spring, a little dull in winter
    return path([(0.05, WINTER_GREEN), (0.30, FRESH), (0.45, SUMMER), (0.75, LATE), (0.90, WINTER_GREEN)], u)


def summerdry(u):
    # Mediterranean: the autumn rains green it, spring is fresh, it dries to straw by late June
    return path([(0.79, STRAW), (0.86, SHOOTS), (0.95, WINTER_GREEN), (0.22, FRESH), (0.36, LATE), (0.44, STRAW), (0.55, GOLDEN), (0.72, GOLDEN)], u)


def steppe(u):
    # cold winter and dry summer: brown, a short green spring, straw from early July
    return path([(0.24, MATTED), (0.28, SHOOTS), (0.33, FRESH), (0.42, SUMMER), (0.48, LATE), (0.53, STRAW), (0.62, GOLDEN), (0.80, STRAW), (0.92, DORMANT)], u)


MAPS = [('evergreen', evergreen)] + [(f'season{int(round(l * 100))}', temperate(l)) for l in LENGTHS] + [('summerdry', summerdry), ('steppe', steppe)]


def row_shift(v):
    """Small, smooth variation between the noise rows: lightness and a touch of hue."""
    a = math.sin(v * 2.1 + 0.4)
    b = math.sin(v * 3.7 + 1.3)
    return 1 + 0.06 * a, 0.04 * b


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, f in MAPS:
        im = Image.new('RGB', (W, H))
        for y in range(H):
            light, hue = row_shift(y)
            for x in range(W):
                r, g, b = f((x + 0.5) / W)
                r, g = r * (1 + hue), g * (1 - hue / 2)
                im.putpixel((x, y), tuple(int(max(0, min(255, round(c * light)))) for c in (r, g, b)))
        p = os.path.join(OUT, f'earthgrass-{name}.png')
        im.save(p)
        print(p)


if __name__ == '__main__':
    main()
