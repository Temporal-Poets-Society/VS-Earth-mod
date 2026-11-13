"""Compress each sampled GLO-90 tile (1 m steps) and estimate the size of all GLO-90 tiles in each format.
usage: glo90_measure.py <sample dir>
The global estimate scales the sample's compressed/published ratio to the bucket's total of DEM files, 71.1 GB in 26,475
tiles (bucket_size.py, 2026-11-03), with a bootstrap 95% interval over tiles."""
import lzma, os, sys
from concurrent.futures import ProcessPoolExecutor
import numpy as np

ALL_TILES, ALL_BYTES = 26475, 71.1e9


def measure(path):
    import brotli, imagecodecs, tifffile
    h = tifffile.imread(path).astype(np.float64)
    x = np.rint(h).astype(np.int32)
    w = np.zeros_like(x); w[:, 1:] = x[:, :-1]
    n = np.zeros_like(x); n[1:, :] = x[:-1, :]
    nw = np.zeros_like(x); nw[1:, 1:] = x[:-1, :-1]
    pred = np.where(nw >= np.maximum(w, n), np.minimum(w, n), np.where(nw <= np.minimum(w, n), np.maximum(w, n), w + n - nw))
    r = x - pred
    raw = ((r << 1) ^ (r >> 31)).astype(np.uint16).tobytes()
    xz = len(lzma.compress(raw, preset=9 | lzma.PRESET_EXTREME))
    br = len(brotli.compress(raw, quality=11, lgwin=24))
    jxl = len(imagecodecs.jpegxl_encode(np.clip(x + 500, 0, 65535).astype(np.uint16), lossless=True, effort=7))
    lat = os.path.basename(path).split("_")[3]
    return dict(name=os.path.basename(path), lat=lat, cells=h.size, orig=os.path.getsize(path), xz=xz, br=br, jxl=jxl,
                sea=float((h == 0).mean()), miss=float(np.abs(r[1:, 1:]).mean()), top=float(h.max()))


def main():
    d = sys.argv[1]
    sys.stdout.reconfigure(encoding="utf-8")
    names = open(os.path.join(d, "sample.txt")).read().split()
    with ProcessPoolExecutor(max_workers=6) as ex:
        rows = list(ex.map(measure, [os.path.join(d, t + ".tif") for t in names]))
    rows.sort(key=lambda r: 8 * r["xz"] / r["cells"])
    print(f"{'tile':48}{'cells':>9}{'sea':>6}{'top m':>7}{'mean miss':>10}{'pub':>7}{'xz':>7}{'brotli':>7}{'JXL':>7}  bits/cell")
    for r in rows:
        b = lambda k: 8 * r[k] / r["cells"]
        print(f"{r['name'][:47]:48}{r['cells']:>9}{r['sea']:>6.0%}{r['top']:>7.0f}{r['miss']:>9.1f}m{b('orig'):>7.2f}{b('xz'):>7.2f}{b('br'):>7.2f}{b('jxl'):>7.2f}")

    orig = np.array([r["orig"] for r in rows], float)
    cells = np.array([r["cells"] for r in rows], float)
    rng = np.random.default_rng(0)
    idx = rng.integers(0, len(rows), (5000, len(rows)))
    print(f"\n{len(rows)} tiles, {cells.sum()/1e6:.0f} M cells, {orig.sum()/1e6:.0f} MB published; "
          f"all GLO-90 about {cells.mean() * ALL_TILES / 1e9:.1f} billion cells")
    for k, label in (("orig", "published COG"), ("xz", "1 m steps, MED + xz"), ("br", "1 m steps, MED + Brotli"), ("jxl", "1 m steps, JPEG XL lossless")):
        v = np.array([r[k] for r in rows], float)
        est = ALL_BYTES * v.sum() / orig.sum()
        boot = ALL_BYTES * v[idx].sum(1) / orig[idx].sum(1)
        lo, hi = np.percentile(boot, [2.5, 97.5])
        print(f"  {label:30} {8 * v.sum() / cells.sum():5.2f} bit/cell   all GLO-90 ≈ {est/1e9:5.1f} GB  (95%: {lo/1e9:.1f}–{hi/1e9:.1f} GB)")
    for lo_m, hi_m, label in ((0, 1, "flat (mean miss < 1 m)"), (1, 5, "hilly (1–5 m)"), (5, 1e9, "mountains (5 m and more)")):
        sel = [r for r in rows if lo_m <= r["miss"] < hi_m]
        if sel:
            c = sum(r["cells"] for r in sel)
            print(f"  {label:30} {len(sel):3} tiles  MED + xz {8 * sum(r['xz'] for r in sel) / c:5.2f} bit/cell")


if __name__ == "__main__":
    main()
