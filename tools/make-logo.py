"""Render Earth's real map on the cylinder used by the suite's Mod DB logos.

Usage: python tools/make-logo.py preview/global.png docs/moddb-logo.png
The output is exactly 480x320. --scale 3 writes a 1440x960 master instead.
Adapted from Cworld/tools/make-logo.py; uses the input raster, without generated geography.
"""

import argparse
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


def render(map_path, output_path, scale=1, seam=144, badge=None):
    aa = 3 * scale
    width, height = 480 * aa, 320 * aa
    with Image.open(map_path) as source:
        texture = np.asarray(source.convert("RGB"), dtype=np.float32)[2:-2, 2:-2] / 255
    texture = np.roll(texture, -seam, axis=1)
    source_h, source_w, _ = texture.shape

    y = np.linspace(0, 1, height)[:, None]
    background = (1 - y) * np.array([0.10, 0.16, 0.26]) + y * np.array([0.03, 0.06, 0.11])
    pixels = np.repeat(background[:, None, :], width, axis=1)

    cx, radius, ellipse = 140 * aa, 84 * aa, 22 * aa
    top, side_height = 38 * aa, 248 * aa
    ys, xs = np.mgrid[0:height, 0:width].astype(np.float32)
    dx = (xs + 0.5 - cx) / radius
    curve = np.sqrt(np.clip(1 - dx * dx, 0, 1))
    side_y = ys + 0.5 - top - ellipse * curve
    side = (np.abs(dx) < 1) & (side_y >= 0) & (side_y <= side_height)
    longitude = (np.arcsin(np.clip(dx, -1, 1)) / (2 * math.pi) + 0.33) % 1
    latitude = np.clip(side_y / side_height, 0, 1)
    surface = texture[
        np.clip((latitude * (source_h - 1)).astype(int), 0, source_h - 1),
        np.clip((longitude * (source_w - 1)).astype(int), 0, source_w - 1),
    ]
    lighting = (0.45 + 0.6 * np.clip(curve * 0.8 - dx * 0.35 + 0.2, 0, 1))[..., None]
    pixels[side] = np.clip(surface * lighting, 0, 1)[side]

    # The cap and its rim retain Cylinder World's simple polar-wall treatment.
    r2 = ((xs + 0.5 - cx) / radius) ** 2 + ((ys + 0.5 - top) / ellipse) ** 2
    cap = r2 <= 1
    ice = np.array([0.93, 0.96, 1.0]) - 0.12 * np.clip(r2, 0, 1)[..., None] * np.array([1, 0.8, 0.5])
    pixels[cap] = ice[cap]
    pixels[cap & (r2 > 0.80)] = np.array([0.80, 0.87, 0.95])

    result = Image.fromarray((pixels * 255).astype(np.uint8))
    draw = ImageDraw.Draw(result)
    draw.arc([cx-radius, top+side_height-ellipse, cx+radius, top+side_height+ellipse],
             0, 180, fill=(220, 232, 245), width=3 * aa)
    arrow_y = top + side_height * 0.5
    draw.arc([cx-radius-18*aa, arrow_y-ellipse-14*aa,
              cx+radius+18*aa, arrow_y+ellipse+14*aa],
             25, 160, fill=(255, 214, 120), width=3 * aa)
    arrow_x = cx + (radius + 18*aa) * math.cos(math.radians(20))
    arrow_tip_y = arrow_y + (ellipse + 14*aa) * math.sin(math.radians(20))
    draw.polygon([(arrow_x+2*aa, arrow_tip_y-12*aa),
                  (arrow_x+12*aa, arrow_tip_y+4*aa),
                  (arrow_x-8*aa, arrow_tip_y+2*aa)], fill=(255, 214, 120))

    def font(names, size):
        for name in names:
            try:
                return ImageFont.truetype(str(Path("C:/Windows/Fonts") / name), size * aa)
            except OSError:
                pass
        return ImageFont.load_default(size=size * aa)

    heading = font(["georgiab.ttf", "timesbd.ttf"], 50)
    body = font(["georgia.ttf", "times.ttf"], 16)
    draw.text((267 * aa, 57 * aa), "Earth", font=heading, fill=(240, 244, 250))
    if badge:
        # A version mark after the title, in the arrow's gold, its baseline on the title's.
        mark = font(["georgiab.ttf", "timesbd.ttf"], 42)
        title_box = draw.textbbox((267 * aa, 57 * aa), "Earth", font=heading)
        mark_box = draw.textbbox((0, 0), badge, font=mark)
        draw.text((title_box[2] + 8 * aa - mark_box[0], title_box[3] - mark_box[3]), badge, font=mark, fill=(255, 214, 120))
    for line, y_pos in zip(("Real-world terrain", "Climate and weather", "Regional flora and fauna"), (137, 162, 187)):
        draw.text((269 * aa, y_pos * aa), line, font=body, fill=(190, 205, 225))

    result = result.resize((480 * scale, 320 * scale), Image.Resampling.LANCZOS)
    result.save(output_path)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("map", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--scale", type=int, choices=(1, 2, 3), default=1)
    parser.add_argument("--seam", type=int, default=144)
    parser.add_argument("--badge", help="a version mark after the title, e.g. v2")
    args = parser.parse_args()
    render(args.map, args.output, args.scale, args.seam, args.badge)
