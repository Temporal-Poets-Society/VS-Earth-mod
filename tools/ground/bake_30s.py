"""Bake the 30" (1/120 degree, about 0.9 km) ground tile sets, in 10-degree tiles the game unpacks on demand.

Usage: python -P tools/ground/bake_30s.py <data dir> [set names...]
Writes <data dir>/baked-30s/global-<set>.egts; copy them to VintagestoryData/ModData/earth/ground. They are too big for
the repository and for Earth's archives (40 MB each), so they are not in assets; the rules list them as optional tiles
over the 2' sets, which remain the fallback. bake_global.py's 2' soil groups come from the same 30" classes once
they are fetched, so the fallback has true classes too.

<data dir> as for bake_global.py, plus:
  soilgrids-30s/wrb/<w>_<s>.npy     tools/ground/fetch_soilgrids_30s.py
  permafrost/UiO_PEX_PERPROB_5.0_20181128_2000_2016_NH.tif (+ .tfw), from UiO_PEX_PERPROB_NH.zip
"""
import os, sys
import numpy as np, tifffile
sys.path.insert(0, os.path.dirname(__file__))
import tiles
from bake_baltic import WRB
import bake_global as g

STEP30, TILE30 = 1 / 120, 1200
NX30 = 360 * 120
LOG_STEPS = 40          # depth to bedrock: v = round(40 ln(1 + m)), m = exp(v / 40) - 1; about 2.5 % steps, 254 = 570 m


def tile30(name, source, licence, west, north, fields):
    if all((f["data"] == f["nodata"]).all() for f in fields):
        return None
    return tiles.pack(f"{name} {west:+d} {north:+d}", source, licence, west, north, STEP30, fields)


def npy_tiles(data):
    d = os.path.join(data, "soilgrids-30s", "wrb")
    for fn in sorted(os.listdir(d)):
        if fn.endswith(".npy") and ".part" not in fn:
            w, s = (int(v) for v in fn[:-4].split("_"))
            yield w, s, np.load(os.path.join(d, fn))


def soilgroup(data):
    """WRB group at 30" (the most common full-resolution class). Gaps of up to 12 cells (about 11 km: towns, lakes,
    rivers) take the nearest mapped class; the sea and ice sheets stay unmapped."""
    from scipy.ndimage import distance_transform_edt
    blobs = []
    for w, s, a in npy_tiles(data):
        nodata = a >= len(WRB)
        if nodata.all():
            continue
        dist, (iy, ix) = distance_transform_edt(nodata, return_indices=True)
        near = nodata & (dist <= 12)
        a = a.copy(); a[near] = a[iy[near], ix[near]]
        b = tile30("global-soilgroup-30s", "SoilGrids 2.0 (ISRIC, Poggio et al. 2021): WRB most probable group, the most common of 16 full-resolution cells",
                   "CC BY 4.0", w, s + 10, [tiles.field("soil_group", a, legend=WRB)])
        if b:
            blobs.append(b)
    return blobs


def soilgroup_2min(data):
    """World-size 2' soil groups: the most common 30" class in each 2' cell (at least 4 of 16 mapped)."""
    T = g.TILE
    wrb = np.full((g.NY, g.NX), 255, np.uint8)
    for w, s, a in npy_tiles(data):
        b = a.reshape(T, 4, T, 4).transpose(0, 2, 1, 3).reshape(T, T, 16)
        counts = np.zeros((T, T, len(WRB)), np.uint8)
        for k in range(16):
            v = b[:, :, k]; ok = v < len(WRB); yy, xx = np.nonzero(ok)
            np.add.at(counts, (yy, xx, v[ok]), 1)
        out = counts.argmax(2).astype(np.uint8); out[counts.sum(2) < 4] = 255
        r0, c0 = int(round((90 - (s + 10)) * 30)), int(round((w + 180) * 30))
        wrb[r0:r0 + T, c0:c0 + T] = out
    return wrb


def shangguan(data):
    """Depth to bedrock at its own 30" grid, log-coded in one byte."""
    tif = tifffile.TiffFile(os.path.join(data, "BDTICM_M_1km_ll.tif"))
    page = tif.pages[0]
    h, w = page.shape
    top = int(round((90 - page.tags[33922].value[4]) * 120))
    assert w == NX30
    fh = tif.filehandle
    band = np.full((TILE30, NX30), 255, np.uint8)
    blobs = []

    def flush(by):
        for tx in range(NX30 // TILE30):
            f = tiles.field("quaternary_m", band[:, tx * TILE30:(tx + 1) * TILE30].copy(), scale=1 / LOG_STEPS)
            f["curve"] = "log1p"
            b = tile30("global-shangguan-30s", "Shangguan et al. 2017, depth to bedrock (BDTICM, SoilGrids 2017), 30\" log-coded", "CC BY 4.0",
                       -180 + tx * 10, 90 - by * 10, [f])
            if b:
                blobs.append(b)
        band[:] = 255

    row = top
    for off, n in zip(page.dataoffsets, page.databytecounts):  # one row per strip
        fh.seek(off)
        a = page.decode(fh.read(n), 0)[0].reshape(-1)[:w].astype(float)
        band[row % TILE30] = np.where(a >= 0, np.clip(np.round(LOG_STEPS * np.log1p(a / 100)), 0, 254), 255).astype(np.uint8)
        row += 1
        if row % TILE30 == 0:
            flush(row // TILE30 - 1)
    if row % TILE30:
        flush(row // TILE30)
    return blobs


def permafrost(data):
    """Permafrost probability (Obu et al. 2019) at its full 1 km grid, sampled at 30" cell centres north of 25 N."""
    d = os.path.join(data, "permafrost")
    name = [f for f in os.listdir(d) if f.endswith("_NH.tif")][0]
    tfw = [float(v) for v in open(os.path.join(d, name.replace(".tif", ".tfw"))).read().split()]
    a = tifffile.TiffFile(os.path.join(d, name)).pages[0].asarray()
    h, w = a.shape
    px = tfw[0]
    ox, oy = tfw[4] - px / 2, tfw[5] + px / 2
    blobs = []
    for ty in range(7):                                    # 90..20 N
        north = 90 - ty * 10
        lat = north - (np.arange(TILE30) + 0.5) * STEP30
        for tx in range(36):
            west = -180 + tx * 10
            lon = west + (np.arange(TILE30) + 0.5) * STEP30
            LA, LO = np.meshgrid(lat, lon, indexing="ij")
            x, y = g.stereo_north(LA, LO)
            ix = np.floor((x - ox) / px).astype(int); iy = np.floor((oy - y) / px).astype(int)
            ok = (ix >= 0) & (ix < w) & (iy >= 0) & (iy < h) & (LA >= 25)
            v = np.full(LA.shape, np.nan, np.float32)
            v[ok] = a[iy[ok], ix[ok]]
            pct = np.where(np.isnan(v) | (v < 0.01) | (v > 1), 255, np.clip(np.round(v * 100), 1, 100)).astype(np.uint8)
            b = tile30("global-permafrost-30s", "Obu et al. 2019, permafrost probability 2000-2016 (ESA GlobPermafrost), 1 km at 30\" centres",
                       "CC BY 3.0", west, north, [tiles.field("permafrost_pct", pct)])
            if b:
                blobs.append(b)
    return blobs


SETS = {"soilgroup-30s": soilgroup, "shangguan-30s": shangguan, "permafrost-30s": permafrost}


def main(data, only):
    out = os.path.join(data, "baked-30s")
    os.makedirs(out, exist_ok=True)
    for name, fn in SETS.items():
        if only and name not in only:
            continue
        blobs = fn(data)
        n = tiles.write_set(os.path.join(out, f"global-{name}.egts"), blobs)
        print(f"  global-{name:14s} {len(blobs):4d} tiles {n:>13,} bytes", flush=True)


if __name__ == "__main__":
    main(sys.argv[1], set(sys.argv[2:]))
