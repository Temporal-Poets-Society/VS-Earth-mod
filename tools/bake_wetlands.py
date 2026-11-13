"""Bake assets/earth/config/wetlands.bin: the share of each grid cell covered by seven groups of wetland vegetation.

Source: Global Lakes and Wetlands Database version 2 (GLWD v2), "combined classes" GeoTIFFs (15 arc-seconds, 86400 x
33600 pixels, 180 W..180 E, 84 N..56 S, 128 x 128 LZW tiles, nodata 255 = sea):
  Lehner, B., Anand, M., Fluet-Chouinard, E., Tan, F., Aires, F., Allen, G. H., Bousquet, P., Canadell, J. G.,
  Davidson, N., Ding, M., Finlayson, C. M., Gumbricht, T., Hilarides, L., Hugelius, G., Jackson, R. B., Korver, M. C.,
  Liu, L., McIntyre, P. B., Nagy, S., Olefeldt, D., Pavelsky, T. M., Pekel, J.-F., Poulter, B., Prigent, C., Wang, J.,
  Worthington, T. A., Yamazaki, D., Zhang, X., and Thieme, M. (2025): Mapping the world's inland surface waters: an
  upgrade to the Global Lakes and Wetlands Database (GLWD v2). Earth Syst. Sci. Data 17, 2277-2329,
  https://doi.org/10.5194/essd-17-2277-2025
  Data: https://www.hydrosheds.org/products/glwd, https://doi.org/10.6084/m9.figshare.28519994
  Attribution shipped with the grid: assets/earth/config/wetland-attribution.txt
  Licence: Creative Commons Attribution 4.0 (https://creativecommons.org/licenses/by/4.0/). The authors ask that the
  original data are not redistributed whole in their original format; Earth ships only this aggregate.

The combined-classes zip holds, per pixel, GLWD_v2_0_main_class (the dominant of the 33 classes, ties to the lower
class; 0 dryland) and GLWD_v2_0_area_pct (the percentage of the pixel covered by all 33 classes together). It has no
per-class fractions, so each pixel's whole wetland percentage is given to its dominant class: a pixel 60 % bog and 30 %
lake counts as 90 % bog. Minor classes inside mixed pixels are lost, which at 15" (about 500 m) matters little at the
grid step. Sea pixels (nodata) count as area without wetland, so a coastal cell's share is of the whole cell.

Groups (layer order of the file; GLWD class IDs as in GLWD_Legend_v2_0.csv):
  0 bog          open peatland: 23 arctic/boreal, 25 temperate, 27 tropical peatland, non-forested
  1 peatforest   wooded peatland: 22, 24, 26
  2 swampforest  wooded swamp and floodplain, flooded: 8 lacustrine, 10/12 riverine, 16 palustrine (forested)
  3 marsh        open marsh, flooded: 9, 11, 13, 17 (non-forested), 30 large river delta, 31 other coastal wetland
  4 mangrove     28
  5 saltmarsh    29
  6 damp         seasonally saturated or ephemeral ground, forested or not: 14, 15, 18, 19, 20, 21. GLWD maps these from
                 maximum-inundation models and calls them its most uncertain classes (overestimated); their wooded or
                 open state is mostly today's farming (much of Picardy, the Beauce and the Po plain), so in a world of 1318
                 they are wet ground, not open marsh.
Left out: 1-7 lakes, reservoirs, rivers and streams (Earth's own water), 32 salt pans (not wetland vegetation), 33 rice
paddies (modern farmland).

File (little-endian): "EWL1", west, north, step (doubles), columns, rows, layer count (int32), each layer's name (a byte
length, then ASCII), then every layer's cells (layer-major, rows from the north, columns from the west) as bytes
round(250 x share), all in one raw deflate stream (System.IO.Compression.DeflateStream). Read by src/Core/WetlandGrid.cs.
Pixel sums are integers and zlib's level 9 is deterministic, so the same zip always gives the same file.

    .venv/Scripts/python -I tools/bake_wetlands.py [--zip combined_classes_tif.zip] [--step 0.05] [--out wetlands.bin]
"""
import argparse, math, os, shutil, struct, sys, tempfile, time, zipfile, zlib
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import numpy as np
import tifffile

ROOT = Path(__file__).resolve().parents[1]
RAW = ROOT.parents[2]/'rawdata' if ROOT.parent.name == 'worktrees' else ROOT/'rawdata'
MAIN, PCT = 'GLWD_v2_0_main_class.tif', 'GLWD_v2_0_area_pct.tif'
PIXELS_PER_DEGREE = 240          # 15 arc-seconds
NODATA = 255
NORTH = 84.0                     # GLWD's north edge and the grid's
GROUPS = [
    ('bog', (23, 25, 27)),
    ('peatforest', (22, 24, 26)),
    ('swampforest', (8, 10, 12, 16)),
    ('marsh', (9, 11, 13, 17, 30, 31)),
    ('mangrove', (28,)),
    ('saltmarsh', (29,)),
    ('damp', (14, 15, 18, 19, 20, 21)),
]

p = argparse.ArgumentParser(description=__doc__.split('\n')[0])
p.add_argument('--zip', type=Path, default=RAW/'glwd-v2'/'combined_classes_tif.zip',
               help='GLWD v2 combined classes GeoTIFF zip, or a folder holding its two rasters already extracted')
p.add_argument('--step', type=float, default=0.1, help='grid step in degrees (a whole number of 15" pixels)')
p.add_argument('--south', type=float, default=-60.0, help='south edge of the grid (its north edge is GLWD\'s, 84 N)')
p.add_argument('--out', type=Path, default=ROOT/'assets/earth/config/wetlands.bin')
args = p.parse_args()

T0 = time.time()
POOL = ThreadPoolExecutor(max(1, min(16, os.cpu_count() or 1)))


def log(msg):
    print(f'[{time.time() - T0:7.1f}s] {msg}', flush=True)


class Raster:
    """One tiled single-band uint8 GeoTIFF, read by rows of tiles."""

    def __init__(self, path):
        self.tif = tifffile.TiffFile(path)
        pg = self.page = self.tif.pages[0]
        if not pg.is_tiled or pg.dtype != np.uint8 or len(pg.shape) != 2:
            raise ValueError(f'{path}: expected a tiled single-band uint8 raster, got {pg.shape} {pg.dtype}')
        self.h, self.w = pg.shape
        self.th, self.tw = pg.tilelength, pg.tilewidth
        self.per_row = -(-self.w // self.tw)
        tie, px = pg.tags['ModelTiepointTag'].value, pg.tags['ModelPixelScaleTag'].value
        nodata = int(float(str(pg.tags['GDAL_NODATA'].value).strip('\x00 '))) if 'GDAL_NODATA' in pg.tags else None
        if abs(tie[3] + 180) > 1e-6 or abs(tie[4] - 84) > 1e-6 or abs(px[0] - 1 / PIXELS_PER_DEGREE) > 1e-9 \
                or abs(px[1] - 1 / PIXELS_PER_DEGREE) > 1e-9 or self.w != 360 * PIXELS_PER_DEGREE or nodata != NODATA:
            raise ValueError(f'{path}: unexpected georeference (tie {tie[3:5]}, pixel {px[:2]}, width {self.w}, nodata {nodata})')
        self.south = 84 - self.h / PIXELS_PER_DEGREE

    def rows(self, r0, r1):
        pg, fh = self.page, self.tif.filehandle
        out = np.full((r1 - r0, self.w), NODATA, np.uint8)
        segs = [s * self.per_row + k for s in range(r0 // self.th, (r1 - 1) // self.th + 1) for k in range(self.per_row)]
        data = []
        for i in segs:
            if pg.databytecounts[i]:
                fh.seek(pg.dataoffsets[i])
                data.append(fh.read(pg.databytecounts[i]))
            else:
                data.append(None)

        def decode(item):
            i, d = item
            y, x = (i // self.per_row) * self.th, (i % self.per_row) * self.tw
            ya, yb, xb = max(y, r0), min(y + self.th, r1, self.h), min(x + self.tw, self.w)
            if d is None:
                return
            seg = pg.decode(d, i)[0]
            seg = np.asarray(seg).reshape(seg.shape[-3], seg.shape[-2])
            out[ya - r0:yb - r0, x:xb] = seg[ya - y:yb - y, :xb - x]
        list(POOL.map(decode, zip(segs, data)))
        return out

    def close(self):
        self.tif.close()


def bake(folder):
    sub = round(args.step * PIXELS_PER_DEGREE)
    if sub < 1 or abs(sub - args.step * PIXELS_PER_DEGREE) > 1e-6:
        raise ValueError(f'--step {args.step} is not a whole number of 15" pixels')
    cols, rows = round(360 / args.step), round((NORTH - args.south) / args.step)
    if abs(rows * args.step - (NORTH - args.south)) > 1e-6:
        raise ValueError(f'--south {args.south} is not a whole number of steps from 84 N')
    main, pct = Raster(folder/MAIN), Raster(folder/PCT)
    if (main.h, main.w) != (pct.h, pct.w):
        raise ValueError('main class and area rasters differ in size')
    if main.h % sub:
        raise ValueError(f'{main.h} source rows are not a whole number of {sub}-pixel cells')
    log(f'GLWD v2 {main.w} x {main.h} pixels, 84 N to {main.south:g} S; grid {cols} x {rows} at {args.step:g} deg ({sub} x {sub} pixels per cell)')
    lut = np.full(256, len(GROUPS), np.uint8)              # group of each class; len(GROUPS) = no layer
    for g, (_, classes) in enumerate(GROUPS):
        lut[list(classes)] = g
    cells = np.zeros((len(GROUPS), rows, cols), np.uint8)
    full = 100 * sub * sub                                  # a cell's pixel percentages when all of it is wetland
    class_sums = np.zeros(256, np.int64)
    band = sub * main.th // math.gcd(sub, main.th)
    while band < 384:
        band *= 2
    src_rows = min(main.h, rows * sub)
    for r0 in range(0, src_rows, band):
        r1 = min(r0 + band, src_rows)
        cls, area = main.rows(r0, r1), pct.rows(r0, r1)
        sea = cls == NODATA
        if np.any(sea != (area == NODATA)):
            raise ValueError(f'rows {r0}..{r1}: the two rasters disagree on nodata')
        if np.any(area[~sea] > 100) or np.any(cls[~sea] > 33):
            raise ValueError(f'rows {r0}..{r1}: values outside 0..100 % or classes 0..33')
        area = np.where(sea, 0, area).astype(np.uint16)
        class_sums += np.bincount(cls.ravel(), weights=area.ravel(), minlength=256).astype(np.int64)
        grp = lut[cls]
        n = (r1 - r0) // sub
        c0 = r0 // sub
        for g in range(len(GROUPS)):
            s = np.where(grp == g, area, 0).reshape(n, sub, cols, sub).sum(axis=(1, 3), dtype=np.int64)
            cells[g, c0:c0 + n] = (s * 500 + full) // (2 * full)    # round(250 x share), half up
        if (r0 // band) % 10 == 0:
            log(f'rows {r0}..{r1} of {src_rows}')
    main.close(); pct.close()
    log('wetland area attributed to each dominant class (pixel-percent sums / 100 = 15" pixels):')
    for c in range(1, 34):
        print(f'  {c:2d} {class_sums[c] / 100:14,.0f}  -> {GROUPS[lut[c]][0] if lut[c] < len(GROUPS) else "-"}')
    return cols, rows, cells


def write(cols, rows, cells):
    head = b'EWL1' + struct.pack('<dddiii', -180.0, NORTH, args.step, cols, rows, len(GROUPS))
    for name, _ in GROUPS:
        head += bytes([len(name)]) + name.encode('ascii')
    z = zlib.compressobj(level=9, method=zlib.DEFLATED, wbits=-15)
    body = z.compress(cells.tobytes(order='C')) + z.flush()
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_bytes(head + body)
    log(f'wrote {args.out} ({len(head) + len(body):,} bytes)')
    lat = NORTH - (np.arange(rows) + 0.5) * args.step
    cell_km2 = (111.32 * args.step) ** 2 * np.cos(np.radians(lat))
    for g, (name, _) in enumerate(GROUPS):
        layer = cells[g]
        km2 = float((layer.sum(axis=1, dtype=np.int64) / 250.0 * cell_km2).sum())
        print(f'  {g} {name:12s} cells > 0: {np.count_nonzero(layer):9,d}  area {km2 / 1e6:6.3f} million km2')


if args.zip.is_dir():
    cols, rows, cells = bake(args.zip)
else:
    with tempfile.TemporaryDirectory(prefix='glwd-') as tmp, zipfile.ZipFile(args.zip) as z:
        names = {Path(n).name: n for n in z.namelist()}
        for want in (MAIN, PCT):
            if want not in names:
                sys.exit(f'{args.zip} has no {want}; expected the GLWD v2 combined classes GeoTIFF zip')
            log(f'extracting {names[want]}')
            with z.open(names[want]) as src, open(Path(tmp)/want, 'wb') as dst:
                shutil.copyfileobj(src, dst, 1 << 22)
        cols, rows, cells = bake(Path(tmp))
write(cols, rows, cells)
