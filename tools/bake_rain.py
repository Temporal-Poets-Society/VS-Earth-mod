"""
Bakes CHELSA v2.1 precipitation (1981-2010, 30 arc-seconds) and monthly temperatures into earthdata/rain.bin: Vintage
Story rainfall (0..255) and summer dryness (0..1) on Earth's 2' terrain grid.

The formula is climate.bin's (build_climate in tools/bake_earth.py), applied per 30" CHELSA pixel and then averaged over
the 4 x 4 pixels of each 2' cell:
  - t_surf: CHELSA's monthly mean air temperature at the pixel (CHELSA tas already follows the terrain, so there is no
    lapse-rate step); biotemp = mean over the 12 months of clip(t_surf, 0, 30)
  - pet = max(58.93 biotemp, 450 + 25 biotemp); ai = p_year / pet with p_year = CHELSA bio12 (annual precipitation)
  - rain = the same log-aridity curve, + 60 for the humid tropics
  - summer drought: pet3 = the three warmest months' pet_m = max(58.93 tm, 450 + 25 tm) / 12, tm = clip(t_surf, 0, 30);
    p3 = CHELSA bio18, the precipitation of the warmest quarter. bake_earth sums the precipitation of the three warmest
    months; bio18 is CHELSA's closest ready-made equivalent: its quarter is the warmest three CONSECUTIVE months, which
    are the three warmest months almost everywhere. ai_summer = p3 / max(pet3, 1);
    dry = smooth((0.45 - ai_summer) / 0.35) smooth((ai - 0.15) / 0.3) smooth((biotemp - 8) / 4); rain *= 1 - 0.25 dry
Unlike UDel's 0.5° gauges (climate.bin), CHELSA resolves orographic rain, e.g. the wet peaks of small islands.

Inputs (see tools/download_data.sh):
  - rawdata/chelsa-v2.1/CHELSA_tas_MM_1981-2010_V.2.1.tif, MM = 01..12 (K x 10: scale 0.1, offset -273.15; nodata 65535)
  - rawdata/chelsa-bio-v2.1/CHELSA_bio12_1981-2010_V.2.1.tif, CHELSA_bio18_1981-2010_V.2.1.tif (kg m-2 = mm, uint16
    with scale 0.1; 65535 is the largest value the format holds, 6553.5 mm, reached in a few very wet places, and is
    taken as that; validity comes from tas)
All 43200 x 20880 pixels, rows from 84 N to 90 S. The planes are read in strips of 512 rows, never whole.
Karger, D.N. et al. (2017): Climatologies at high resolution for the earth's land surface areas. Scientific Data 4,
170122; Karger, D.N. et al. (2021), Version 2.1, EnviDat, https://doi.org/10.16904/envidat.228.v2.1. CC BY 4.0.

rain.bin (little-endian): header EMRN, version 1, width 10800, height 5400, tile 180, tile count 1800, then (offset,
length) per tile (row-major from the north-west) and the tiles: brotli of three tile x tile u8 planes, rain
(clip(round(rain), 0, 255)), dryness (clip(round(dry x 255), 0, 255)) and a data mask (1 = data). A cell needs at least 4
of its 16 pixels with data; a tile without any data has length 0. Cell (r, c) is centred at lat 90 - (r + 0.5) / 30,
lon -180 + (c + 0.5) / 30, as temperature.bin. CHELSA covers the sea too, so the mask is 0 only north of 84 N.

    .venv/Scripts/python -I tools/bake_rain.py [tas folder] [bio folder] [--check]
"""
import os, re, struct, sys, time
from concurrent.futures import ThreadPoolExecutor

import brotli
import numpy as np
import tifffile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "earthdata")
TAS = os.path.join(ROOT, "rawdata", "chelsa-v2.1")
BIO = os.path.join(ROOT, "rawdata", "chelsa-bio-v2.1")
RES = 30              # Earth's terrain cells per degree (2')
TILE = 180            # cells per tile side (6°), as terrain.bin and temperature.bin
SUB = 4               # CHELSA pixels per cell side (30" -> 2')
MIN_PIXELS = 4        # a cell needs a quarter of its pixels with data
STRIP = 512           # source rows read at once (a multiple of SUB and of the tas tile height)
BLOCK = 128           # source rows computed at once
T0 = time.time()
POOL = ThreadPoolExecutor(max(1, min(16, os.cpu_count() or 1)))


def log(msg):
    print(f"[{time.time() - T0:7.1f}s] {msg}", flush=True)


class Raster:
    """A single-band GeoTIFF read by rows, segment (tile or strip) by segment."""

    def __init__(self, path):
        self.path = path
        self.tif = tifffile.TiffFile(path)
        p = self.page = self.tif.pages[0]
        self.h, self.w = p.shape
        self.dtype = p.dtype
        if p.is_tiled:
            self.seg_h, self.per_row = p.tilelength, -(-self.w // p.tilewidth)
        else:
            self.seg_h, self.per_row = p.rowsperstrip, 1
        md = str(p.tags["GDAL_METADATA"].value) if "GDAL_METADATA" in p.tags else ""
        def item(role, default):
            m = re.search(r'role="%s">([^<]+)<' % role, md)
            return float(m.group(1)) if m else default
        self.scale, self.offset = item("scale", 1.0), item("offset", 0.0)
        self.unit = (re.search(r'name="variable_unit">([^<]*)<', md) or [None, None])[1]
        self.nodata = None
        if "GDAL_NODATA" in p.tags:
            try:
                v = int(float(str(p.tags["GDAL_NODATA"].value).strip("\x00 ")))
                info = np.iinfo(self.dtype)
                if info.min <= v <= info.max:
                    self.nodata = v
            except ValueError:
                pass
        self.tie = p.tags["ModelTiepointTag"].value
        self.px = p.tags["ModelPixelScaleTag"].value

    def describe(self):
        return (f"{os.path.basename(self.path)}: {self.w} x {self.h} {self.dtype}, west {self.tie[3]:.5f}, top {self.tie[4]:.5f}, "
                f"pixel {self.px[0]:.10f}, scale {self.scale}, offset {self.offset}, unit {self.unit}, nodata {self.nodata}")

    def rows(self, r0, r1):
        p, fh = self.page, self.tif.filehandle
        out = np.empty((r1 - r0, self.w), self.dtype)
        segs = [s * self.per_row + k for s in range(r0 // self.seg_h, (r1 - 1) // self.seg_h + 1) for k in range(self.per_row)]
        data = []
        for i in segs:
            if p.databytecounts[i]:
                fh.seek(p.dataoffsets[i])
                data.append(fh.read(p.databytecounts[i]))
            else:
                data.append(None)
        def decode(args):
            i, d = args
            seg, idx, shape = p.decode(d, i)
            y, x = idx[-3], idx[-2]
            hh, ww = shape[-3], shape[-2]
            ya, yb = max(y, r0), min(y + hh, r1, self.h)
            xb = min(x + ww, self.w)
            if seg is None:
                out[ya - r0:yb - r0, x:xb] = self.nodata if self.nodata is not None else 0
            else:
                seg = np.asarray(seg).reshape(seg.shape[-3], seg.shape[-2])
                out[ya - r0:yb - r0, x:xb] = seg[ya - y:yb - y, :xb - x]
        list(POOL.map(decode, zip(segs, data)))
        return out


def smooth(x):
    x = np.clip(x, 0, 1)
    return x * x * (3 - 2 * x)


XS = np.log([0.01, 0.05, 0.2, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 5.0]).astype(np.float32)
YS = np.array([0, 15, 50, 95, 115, 130, 150, 170, 200, 235], np.float32)


def pixels(tas, tas_r, b12, b12_r, b18, b18_r):
    """rain, dry and validity per 30" pixel (build_climate's formula)."""
    valid = (tas != tas_r.nodata).all(axis=0) if tas_r.nodata is not None else np.ones(tas.shape[1:], bool)
    # CHELSA's tags give scale 0.1 and offset 0 for Kelvin; to Celsius as tools/bake_chelsa.py
    kelvin = 273.15 if tas_r.unit == "K" else 0.0
    t = tas.astype(np.float32) * np.float32(tas_r.scale) + np.float32(tas_r.offset - kelvin)
    tm = np.clip(t, 0, 30, out=t)
    del t
    biotemp = tm.mean(axis=0)
    pet = np.maximum(58.93 * biotemp, 450 + 25 * biotemp)
    p_year = b12.astype(np.float32) * np.float32(b12_r.scale) + np.float32(b12_r.offset)
    ai = p_year / pet
    rain = np.interp(np.log(np.maximum(ai, 1e-4)), XS, YS).astype(np.float32)
    rain += 60 * smooth((biotemp - 20) / 5) * smooth((ai - 0.6) / 0.5)
    pet_m = np.maximum(58.93 * tm, 450 + 25 * tm)
    pet_m /= 12
    # pet_m rises with the temperature, so the three warmest months' pet_m are the three largest
    pet3 = np.partition(pet_m, 9, axis=0)[9:].sum(axis=0)
    del pet_m, tm
    p3 = b18.astype(np.float32) * np.float32(b18_r.scale) + np.float32(b18_r.offset)
    ai_summer = p3 / np.maximum(pet3, 1)
    dry = smooth((0.45 - ai_summer) / 0.35) * smooth((ai - 0.15) / 0.3) * smooth((biotemp - 8) / 4)
    rain *= 1 - 0.25 * dry
    return rain, dry, valid


def bake(tas_dir=TAS, bio_dir=BIO):
    tas_r = [Raster(os.path.join(tas_dir, f"CHELSA_tas_{m:02d}_1981-2010_V.2.1.tif")) for m in range(1, 13)]
    b12_r = Raster(os.path.join(bio_dir, "CHELSA_bio12_1981-2010_V.2.1.tif"))
    b18_r = Raster(os.path.join(bio_dir, "CHELSA_bio18_1981-2010_V.2.1.tif"))
    for r in (tas_r[0], b12_r, b18_r):
        log(r.describe())
    ref = tas_r[0]
    for r in tas_r + [b12_r, b18_r]:
        if (r.h, r.w) != (ref.h, ref.w) or abs(r.tie[3] - ref.tie[3]) > 1e-6 or abs(r.tie[4] - ref.tie[4]) > 1e-6 or abs(r.px[0] - 1 / 120) > 1e-8:
            raise SystemExit(f"{r.path}: not CHELSA's 30\" grid")
    if ref.w != 360 * 120 or abs(ref.tie[3] + 180) > 0.01:
        raise SystemExit("CHELSA: not global")
    H, W = 180 * RES, 360 * RES
    r_top = int(round((90 - ref.tie[4]) * RES))       # Earth's row of CHELSA's first cell row (84 N -> 180)
    rain_c = np.zeros((H, W), np.uint8)
    dry_c = np.zeros((H, W), np.uint8)
    mask = np.zeros((H, W), np.uint8)
    sat12 = sat18 = nvalid = 0
    hist = np.zeros(256, np.int64)
    for s0 in range(0, ref.h, STRIP):
        s1 = min(s0 + STRIP, ref.h)
        tas = np.stack([r.rows(s0, s1) for r in tas_r])
        b12, b18 = b12_r.rows(s0, s1), b18_r.rows(s0, s1)
        for k0 in range(0, s1 - s0, BLOCK):
            k1 = min(k0 + BLOCK, s1 - s0)
            rain, dry, valid = pixels(tas[:, k0:k1], tas_r[0], b12[k0:k1], b12_r, b18[k0:k1], b18_r)
            nvalid += int(valid.sum())
            sat12 += int((valid & (b12[k0:k1] == 65535)).sum())
            sat18 += int((valid & (b18[k0:k1] == 65535)).sum())
            hh = (k1 - k0) // SUB
            def cells(a):
                return np.where(valid, a, 0).reshape(hh, SUB, W, SUB).sum(axis=(1, 3), dtype=np.float64)
            n = valid.reshape(hh, SUB, W, SUB).sum(axis=(1, 3))
            ok = n >= MIN_PIXELS
            rc = cells(rain) / np.maximum(n, 1)
            dc = cells(dry) / np.maximum(n, 1)
            r = r_top + (s0 + k0) // SUB
            rain_c[r:r + hh] = np.where(ok, np.clip(np.round(rc), 0, 255), 0)
            dry_c[r:r + hh] = np.where(ok, np.clip(np.round(dc * 255), 0, 255), 0)
            mask[r:r + hh] = ok
        del tas, b12, b18
        if (s0 // STRIP) % 8 == 0:
            log(f"rows {s0}..{s1} of {ref.h} (lat {ref.tie[4] - s0 / 120:.1f})")
    log(f"cells with data {mask.mean() * 100:.1f}%; valid pixels at the bio12 / bio18 maximum (6553.5 mm): {sat12} / {sat18} of {nvalid}")
    return rain_c, dry_c, mask


def write(rain_c, dry_c, mask):
    H, W = mask.shape
    blobs = []
    for ty in range(H // TILE):
        for tx in range(W // TILE):
            sl = (slice(ty * TILE, (ty + 1) * TILE), slice(tx * TILE, (tx + 1) * TILE))
            if not mask[sl].any():
                blobs.append(b"")
                continue
            raw = rain_c[sl].tobytes() + dry_c[sl].tobytes() + mask[sl].tobytes()
            blobs.append(brotli.compress(raw, quality=11, lgwin=24))
    path = os.path.join(OUT, "rain.bin")
    with open(path, "wb") as f:
        f.write(b"EMRN")
        f.write(struct.pack("<iiiii", 1, W, H, TILE, len(blobs)))
        off = 4 + 20 + 8 * len(blobs)
        for b in blobs:
            f.write(struct.pack("<ii", off, len(b)))
            off += len(b)
        for b in blobs:
            f.write(b)
    log(f"rain.bin {os.path.getsize(path) / 1e6:.2f} MB, {len(blobs)} tiles, {sum(1 for b in blobs if not b)} empty")


PLACES = [
    ("St Helena peaks", -15.96, -5.70), ("St Helena coast", -15.92, -5.72), ("Sahara", 23, 12), ("Amazon", -3, -60),
    ("Paris", 48.85, 2.35), ("Riga", 56.95, 24.1), ("Jostedalsbreen", 61.7, 6.9), ("Kilimanjaro summit", -3.07, 37.36),
    ("Kilimanjaro slopes", -3.2, 37.4), ("Andalusia", 37.4, -5.9),
]


def report(rain_c, dry_c, mask):
    for name, lat, lon in PLACES:
        r, c = int((90 - lat) * RES), int((lon + 180) * RES)
        print(f"  {name}: " + (f"rain {rain_c[r, c]}, dry {dry_c[r, c] / 255:.2f}" if mask[r, c] else "no data"))


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    rain_c, dry_c, mask = bake(*args[:2])
    report(rain_c, dry_c, mask)
    if "--check" not in sys.argv:
        write(rain_c, dry_c, mask)
