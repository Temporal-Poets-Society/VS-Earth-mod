"""
Bakes earthdata/aridity.bin: how much Earth's aridity index (precipitation / potential evapotranspiration) changes where
the evapotranspiration is CHELSA's Penman-Monteith instead of the Holdridge estimate rain.bin and climate.bin use.

rain.bin (tools/bake_rain.py) and climate.bin (tools/bake_earth.py) both estimate the potential evapotranspiration from
the temperature alone: pet = max(58.93 biotemp, 450 + 25 biotemp), biotemp the yearly mean of the monthly means clipped
to 0..30 °C. That runs low wherever sun, wind and dry air drive the evaporation more than warmth: the Pontic and Kazakh
steppes, the Great Plains, the Pampas, the Tibetan Plateau, the East African highlands. Their index reads 15 to 45 %
high (Lincoln, Nebraska 1.03 against 0.72; Kherson 0.64 against 0.52). The Holdridge floor also makes the cold
lands look too dry (Yakutsk 0.47 against 0.53).

Earth keeps its rainfall (the value vanilla and Earth's catalogues read) and corrects only the index: worlds with
EarthParams.AridityRules 1 multiply the index decoded from the rainfall (SoilFertility.Aridity) by this grid's ratio
k = PET_Holdridge / PET_Penman. The ratio, not the index itself, so it applies to every rainfall the game reads: the
2' grid's, the 0.25° grid's and the rivers' raised green-belt rainfall.

Per 30" CHELSA pixel: PET_Holdridge from CHELSA's monthly mean temperatures as bake_rain.py; PET_Penman = 12 x CHELSA
BIOCLIM+ pet_penman_mean (the mean monthly Penman-Monteith evapotranspiration, kg m-2 = mm). A 2' cell's k is the
geometric mean of its pixels' ratios (the index's curve is logarithmic).

Inputs (see tools/download_data.sh):
  - rawdata/chelsa-v2.1/CHELSA_tas_MM_1981-2010_V.2.1.tif, MM = 01..12
  - rawdata/chelsa-bio-v2.1/CHELSA_pet_penman_mean_1981-2010_V.2.1.tif (uint16, scale 0.01, nodata 65535)
Karger, D.N. et al. (2017): Climatologies at high resolution for the earth's land surface areas. Scientific Data 4,
170122; Karger, D.N. et al. (2021), Version 2.1, EnviDat, https://doi.org/10.16904/envidat.228.v2.1. CC BY 4.0.

aridity.bin (little-endian): header EMAI, version 1 (with predicted planes, tools/planecoding.py: 1 | 1 << 16), width
10800, height 5400, tile 180, tile count 1800, then (offset, length) per tile (row-major from the north-west) and the
tiles: one tile x tile u8 plane as predicted planes (EA-0077; plain brotli before 2.0.0-rc.14), 0 where there is no
data (a cell needs at least 4 of its 16 pixels with both values), else clip(round(128 + 64 log2 k), 1, 255): k from
0.25 to 4 in steps of 1.1 %. Cell (r, c) is centred at lat 90 - (r + 0.5) / 30, lon -180 + (c + 0.5) / 30, as rain.bin.

    .venv/Scripts/python -I tools/bake_aridity.py [tas folder] [bio folder] [--check]
"""
import os, struct, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
import planecoding as pc
from bake_rain import Raster, log, ROOT, OUT, TAS, BIO, RES, TILE, SUB, MIN_PIXELS, STRIP, BLOCK

PET = "CHELSA_pet_penman_mean_1981-2010_V.2.1.tif"


def ratios(tas, tas_r, pet, pet_r):
    """log(PET_Holdridge / PET_Penman) and validity per 30" pixel."""
    valid = (tas != tas_r.nodata).all(axis=0) if tas_r.nodata is not None else np.ones(tas.shape[1:], bool)
    valid &= pet != 65535
    kelvin = 273.15 if tas_r.unit == "K" else 0.0
    t = tas.astype(np.float32) * np.float32(tas_r.scale) + np.float32(tas_r.offset - kelvin)
    biotemp = np.clip(t, 0, 30, out=t).mean(axis=0)
    del t
    pet_h = np.maximum(58.93 * biotemp, 450 + 25 * biotemp)
    pet_p = 12 * (pet.astype(np.float32) * np.float32(pet_r.scale) + np.float32(pet_r.offset))
    valid &= pet_p > 1
    lr = np.log(pet_h / np.maximum(pet_p, 1))
    return lr, valid


def bake(tas_dir=TAS, bio_dir=BIO):
    tas_r = [Raster(os.path.join(tas_dir, f"CHELSA_tas_{m:02d}_1981-2010_V.2.1.tif")) for m in range(1, 13)]
    pet_r = Raster(os.path.join(bio_dir, PET))
    for r in (tas_r[0], pet_r):
        log(r.describe())
    ref = tas_r[0]
    for r in tas_r + [pet_r]:
        if (r.h, r.w) != (ref.h, ref.w) or abs(r.tie[3] - ref.tie[3]) > 1e-6 or abs(r.tie[4] - ref.tie[4]) > 1e-6 or abs(r.px[0] - 1 / 120) > 1e-8:
            raise SystemExit(f"{r.path}: not CHELSA's 30\" grid")
    if ref.w != 360 * 120 or abs(ref.tie[3] + 180) > 0.01:
        raise SystemExit("CHELSA: not global")
    if abs(pet_r.scale - 0.01) > 1e-9 or pet_r.offset != 0:
        raise SystemExit(f"{PET}: expected scale 0.01 and offset 0, found {pet_r.scale} and {pet_r.offset}")
    H, W = 180 * RES, 360 * RES
    r_top = int(round((90 - ref.tie[4]) * RES))
    code = np.zeros((H, W), np.uint8)
    for s0 in range(0, ref.h, STRIP):
        s1 = min(s0 + STRIP, ref.h)
        tas = np.stack([r.rows(s0, s1) for r in tas_r])
        pet = pet_r.rows(s0, s1)
        for k0 in range(0, s1 - s0, BLOCK):
            k1 = min(k0 + BLOCK, s1 - s0)
            lr, valid = ratios(tas[:, k0:k1], tas_r[0], pet[k0:k1], pet_r)
            hh = (k1 - k0) // SUB
            n = valid.reshape(hh, SUB, W, SUB).sum(axis=(1, 3))
            s = np.where(valid, lr, 0).reshape(hh, SUB, W, SUB).sum(axis=(1, 3), dtype=np.float64)
            k = np.exp(s / np.maximum(n, 1))
            c = np.clip(np.round(128 + 64 * np.log2(k)), 1, 255)
            r = r_top + (s0 + k0) // SUB
            code[r:r + hh] = np.where(n >= MIN_PIXELS, c, 0)
        del tas, pet
        if (s0 // STRIP) % 8 == 0:
            log(f"rows {s0}..{s1} of {ref.h} (lat {ref.tie[4] - s0 / 120:.1f})")
    log(f"cells with data {(code > 0).mean() * 100:.1f}%")
    return code


def write(code):
    H, W = code.shape
    tiles = []
    for ty in range(H // TILE):
        for tx in range(W // TILE):
            t = code[ty * TILE:(ty + 1) * TILE, tx * TILE:(tx + 1) * TILE]
            tiles.append([t] if t.any() else None)
    path = os.path.join(OUT, "aridity.bin")
    pc.write_tiled(path, "EMAI", 1, W, H, TILE, tiles, [pc.U8])
    log(f"aridity.bin {os.path.getsize(path) / 1e6:.2f} MB, {len(tiles)} tiles, {sum(1 for t in tiles if t is None)} empty")


PLACES = [
    ("Kherson", 46.64, 32.6), ("Odesa", 46.48, 30.73), ("Kyiv", 50.45, 30.52), ("Paris", 48.85, 2.35),
    ("Lincoln", 40.81, -96.7), ("Lhasa", 29.65, 91.1), ("Yakutsk", 62.03, 129.73), ("Manaus", -3.1, -60.0),
]


def report(code):
    for name, lat, lon in PLACES:
        c = code[int((90 - lat) * RES), int((lon + 180) * RES)]
        print(f"  {name}: " + (f"k {2 ** ((int(c) - 128) / 64):.2f}" if c else "no data"))


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    code = bake(*args[:2])
    report(code)
    if "--check" not in sys.argv:
        write(code)
