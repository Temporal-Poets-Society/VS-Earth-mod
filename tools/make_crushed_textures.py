#!/usr/bin/env python
"""Fallback textures for Earth's crushed minerals and grog (EA-0068), 32x32, procedural (no vanilla pixels).

    python -I tools/make_crushed_textures.py      # writes assets/earth/textures/item/resource/crushed/*.png

In the game the client redraws them from vanilla's crushed template nearest in colour, moved into the palette these files
carry (EarthStoneTextures, decision 0058's recolour); these drawings stay as the fallback. Only their mean colour and
spread matter there, so they are plain grain noise in the mineral's colours.
"""
import os, zlib
import numpy as np
from PIL import Image

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(ROOT, "assets", "earth", "textures", "item", "resource", "crushed")
# mean colour, luma spread, a second grain colour and its share
MINERALS = {
    "kyanite": ((150, 172, 206), 20, (92, 124, 186), 0.25),       # pale blue-grey with blue blades
    "sillimanite": ((208, 202, 188), 18, (236, 234, 226), 0.3),   # white to buff fibres
    "andalusite": ((170, 128, 116), 20, (118, 80, 74), 0.25),     # pink-brown
    "grog": ((166, 92, 66), 22, (204, 150, 112), 0.2),            # crushed fired clay: brick red with paler bits
}


def draw(name, mean, spread, second, share):
    rng = np.random.default_rng(zlib.crc32(name.encode()))
    n = 32
    # grains of 1-2 px: coarse noise upsampled, plus fine noise
    coarse = rng.normal(0, 1, (16, 16)).repeat(2, 0).repeat(2, 1)
    fine = rng.normal(0, 1, (n, n))
    t = 0.65 * coarse + 0.35 * fine
    t = (t - t.mean()) / t.std()
    base = np.array(mean, float)[None, None, :] + t[..., None] * spread
    pick = rng.random((16, 16)).repeat(2, 0).repeat(2, 1) < share
    alt = np.array(second, float)[None, None, :] + t[..., None] * spread * 0.6
    rgb = np.where(pick[..., None], alt, base)
    img = np.zeros((n, n, 4), np.uint8)
    img[..., :3] = np.clip(np.round(rgb), 0, 255)
    img[..., 3] = 255
    return Image.fromarray(img, "RGBA")


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for k, v in MINERALS.items():
        draw(k, *v).save(os.path.join(OUT, k + ".png"))
    print("wrote", ", ".join(MINERALS), "to", OUT)
