"""Does packing GLO-90 into bigger compression tiles, or into coarser cells, compress better? 1 m steps, MED + xz.
usage: tile_size.py <tile.tif>"""
import lzma, sys
import numpy as np
import tifffile

h = tifffile.imread(sys.argv[1]).astype(np.float64)


def med_xz(a):
    x = np.rint(a).astype(np.int32)
    w = np.zeros_like(x); w[:, 1:] = x[:, :-1]
    n = np.zeros_like(x); n[1:, :] = x[:-1, :]
    nw = np.zeros_like(x); nw[1:, 1:] = x[:-1, :-1]
    pred = np.where(nw >= np.maximum(w, n), np.minimum(w, n), np.where(nw <= np.minimum(w, n), np.maximum(w, n), w + n - nw))
    r = x - pred
    zig = ((r << 1) ^ (r >> 31)).astype(np.uint16)
    return len(lzma.compress(zig.tobytes(), preset=9 | lzma.PRESET_EXTREME))


print("Same 90 m cells, split into compression tiles of different sizes:")
H, W = h.shape
for parts in (16, 8, 4, 2, 1):
    th, tw = H // parts, W // parts
    total = sum(med_xz(h[i:i + th, j:j + tw]) for i in range(0, H, th) for j in range(0, W, tw))
    print(f"  {parts * parts:4} tiles of {tw:4} x {th:4} cells ({1/parts:.4g} deg): {total/1e3:7.0f} kB  {8*total/h.size:5.2f} bit/cell")

print("Coarser cells (mean of k x k), whole tile:")
for k in (1, 2, 3, 4, 6):
    c = h[:H // k * k, :W // k * k].reshape(H // k, k, W // k, k).mean(axis=(1, 3))
    b = med_xz(c)
    print(f"  {k}x: {90*k:4} m cells, {c.size:7} cells: {b/1e3:6.0f} kB  {8*b/c.size:5.2f} bit/cell  ({b/med_xz(h):.0%} of 90 m)")
