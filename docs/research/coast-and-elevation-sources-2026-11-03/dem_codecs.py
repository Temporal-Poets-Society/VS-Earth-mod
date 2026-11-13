"""Compress one Copernicus GLO-90 tile several ways and report size and error, judged as a 1:10 game would see it.
usage: dem_codecs.py <tile.tif>"""
import lzma, os, sys
import numpy as np
import brotli
import imagecodecs
import tifffile
from scipy import fft

sys.stdout.reconfigure(encoding="utf-8")
path = sys.argv[1]
h = tifffile.imread(path).astype(np.float64)
n = h.size
BLOCK = 10.0                      # metres per block at 1:10
blocks = np.floor(h / BLOCK)
land = h > 1.0                    # Copernicus water is about 0 m


def report(name, nbytes, rec):
    e = rec - h
    changed = (np.floor(rec / BLOCK) != blocks).mean()
    flips = ((rec > 1.0) != land).sum()
    print(f"{name:44}{nbytes/1e3:9.0f} kB{8*nbytes/n:7.2f} bit/cell   max err {np.abs(e).max():6.2f} m  rms {np.sqrt((e**2).mean()):5.2f} m"
          f"   block heights changed {changed:6.2%}   land/water flips {flips}")


def med_residuals(q):
    """Quantise to q metres, then the median edge detector of LOCO-I/JPEG-LS predicts each cell from W, N, NW."""
    x = np.rint(h / q).astype(np.int32)
    a = np.zeros_like(x); a[:, 1:] = x[:, :-1]          # west
    b = np.zeros_like(x); b[1:, :] = x[:-1, :]          # north
    c = np.zeros_like(x); c[1:, 1:] = x[:-1, :-1]       # north-west
    pred = np.where(c >= np.maximum(a, b), np.minimum(a, b), np.where(c <= np.minimum(a, b), np.maximum(a, b), a + b - c))
    r = (x - pred)
    zig = ((r << 1) ^ (r >> 31)).astype(np.uint32)       # zigzag: small residuals of either sign become small numbers
    return x, zig


print(f"tile {h.shape[1]} x {h.shape[0]} = {n} cells, {h.min():.1f}..{h.max():.1f} m\n")
report("Copernicus COG as published (float32, deflate)", os.path.getsize(path), h)
report("raw int16, 1 m steps (no compression)", 2 * n, np.rint(h))

for q in (0.1, 1, 2, 5):
    x, zig = med_residuals(q)
    raw = zig.astype(np.uint16 if zig.max() < 65536 else np.uint32).tobytes()
    br = brotli.compress(raw, quality=11, lgwin=24)
    xz = lzma.compress(raw, preset=9 | lzma.PRESET_EXTREME)
    best = min(len(br), len(xz))
    report(f"quantise {q:g} m + MED predictor + {'brotli' if len(br) <= len(xz) else 'xz'}", best, x * q)

for z in (0.5, 1.0, 2.5, 5.0):
    try:
        enc = imagecodecs.lerc_encode(h.astype(np.float32), level=z)
        rec = imagecodecs.lerc_decode(enc).astype(np.float64)
        report(f"LERC, max error {z:g} m", len(enc), rec)
        enc2 = imagecodecs.lerc_encode(h.astype(np.float32), level=z, compression="zstd", compressionargs={"level": 19})
        report(f"LERC + zstd, max error {z:g} m", len(enc2), imagecodecs.lerc_decode(enc2).astype(np.float64))
    except Exception as ex:
        print("LERC failed:", ex)

off = 100   # JPEG-LS wants unsigned integers
for near in (0, 1, 2):
    try:
        u = (np.rint(h) + off).astype(np.uint16)
        enc = imagecodecs.jpegls_encode(u, level=near)
        rec = imagecodecs.jpegls_decode(enc).astype(np.float64) - off
        report(f"JPEG-LS on 1 m steps, near={near}", len(enc), rec)
    except Exception as ex:
        print("JPEG-LS failed:", ex)

try:
    u = (np.rint(h) + off).astype(np.uint16)
    enc = imagecodecs.jpegxl_encode(u, lossless=True, effort=9)
    report("JPEG XL lossless on 1 m steps", len(enc), imagecodecs.jpegxl_decode(enc).astype(np.float64) - off)
except Exception as ex:
    print("JPEG XL failed:", ex)

# transform coding: 16x16 DCT blocks (JPEG-like), coefficients quantised with step s, then brotli
H, W = h.shape[0] // 16 * 16, h.shape[1] // 16 * 16
hb = h[:H, :W].reshape(H // 16, 16, W // 16, 16).transpose(0, 2, 1, 3)
coef = fft.dctn(hb, axes=(2, 3), norm="ortho")
for s in (2, 4, 8, 16):
    qc = np.rint(coef / s).astype(np.int32)
    zig = ((qc << 1) ^ (qc >> 31)).astype(np.uint32)
    enc = brotli.compress(zig.astype(np.uint16 if zig.max() < 65536 else np.uint32).tobytes(), quality=11, lgwin=24)
    rec = h.copy()
    rec[:H, :W] = fft.idctn(qc * s, axes=(2, 3), norm="ortho").transpose(0, 2, 1, 3).reshape(H, W)
    report(f"DCT 16x16, coefficient step {s}", len(enc), rec)

# FFT of the whole tile, keeping the largest coefficients; cost counted as 6 bytes each (index + value), before entropy coding
F = fft.rfft2(h)
mag = np.abs(F).ravel()
for frac in (0.02, 0.05, 0.10):
    k = int(frac * mag.size)
    keep = np.zeros(mag.size, bool); keep[np.argpartition(mag, -k)[-k:]] = True
    rec = fft.irfft2(np.where(keep.reshape(F.shape), F, 0), s=h.shape)
    report(f"FFT of whole tile, largest {frac:.0%} kept", 6 * k, rec)
