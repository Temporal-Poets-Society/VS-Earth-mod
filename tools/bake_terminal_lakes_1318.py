"""
River ends rules 3 (decision 0107, EA-0124): earthdata/lakes-2.bin, decision 0005's terminal lakes (lakes.bin) with the
Aral at its level of 1318 instead of 1960's: the 2' grid's cells below 29 m joined to the western basin are lake at 29 m,
and the Aral's other patches of lakes.bin are left out, so those cells are the grid's own again (dry bed, and the North
Aral as the grid has it today). Every other lake's patches and record are lakes.bin's, byte for byte. Worlds of the works of
1318 rules 6 draw the Aral of 1318 from works-1318-4.bin's LAKE area too (GLO-90's shores, the eastern basin's lagoon);
this file gives the river ends their lake and the scales without outline coasts the same Aral on the grid.

    .venv/Scripts/python -I tools/bake_terminal_lakes_1318.py earthdata/lakes.bin earthdata/lakes-2.bin
"""
import math, struct, sys

import brotli
import numpy as np
from scipy import ndimage as ndi

ARAL = "Aral Sea"
LEVEL = 29.0                  # Krivonogov et al. 2014: the Kerderi low stand, 12th-15th centuries
SEED = (44.9, 58.4)           # the western basin
BOX = (43.0, 47.6, 57.8, 62.5)  # lat0, lat1, lon0, lon1: bake_earth.TERMINAL_LAKES' box for the Aral
SHORE_KM = 15.0               # the 1960 lake's reed shores were 40 km wide; the 1318 lake's are drawn narrower
LAND_Q, SEA_Q = 700.0, 150.0  # bake_earth's elevation code
RES = 30
FLAG_LAKE = 2


def read_varints(b, o, n):
    out = np.zeros(n, np.int64)
    for i in range(n):
        v = s = 0
        while True:
            x = b[o]; o += 1
            v |= (x & 0x7F) << s
            s += 7
            if not x & 0x80:
                break
        out[i] = v
    return out, o


def varints(v):
    out = bytearray()
    for x in np.asarray(v, np.uint64).tolist():
        while True:
            b = x & 0x7F
            x >>= 7
            out.append(b | (0x80 if x else 0))
            if not x:
                break
    return bytes(out)


def elev_m(code):
    code = code.astype(np.float64)
    return np.where(code >= 0, 50 * np.expm1(code / LAND_Q), -50 * np.expm1(-code / SEA_Q))


def main(src, dst):
    data = open(src, "rb").read()
    assert data[:4] == b"EMLK"
    version, w, n, nl = struct.unpack_from("<iiii", data, 4)
    b = brotli.decompress(data[20:])
    deltas, o = read_varints(b, 0, n)
    idx = np.cumsum(deltas)
    e = np.frombuffer(b, "<i2", n, o); o += 2 * n
    lv = np.frombuffer(b, "<i2", n, o); o += 2 * n
    fl = np.frombuffer(b, np.uint8, n, o); o += n
    lakes, recs = [], []
    while o < len(b):
        level, lat, lon, shore, kind, salt, ln = struct.unpack_from("<ffffBBB", b, o)
        name = b[o + 19:o + 19 + ln].decode("utf-8")
        recs.append(b[o:o + 19 + ln])
        lakes.append((name, level, lat, lon, shore, kind, salt))
        o += 19 + ln
    assert len(lakes) == nl
    row, col = idx // w, idx % w
    lat = 90 - (row + 0.5) / RES
    lon = (col + 0.5) / RES - 180
    aral_level = [l for l in lakes if l[0] == ARAL][0][1]
    aral = (lat >= BOX[0]) & (lat <= BOX[1]) & (lon >= BOX[2]) & (lon <= BOX[3]) & (lv == round(aral_level)) & ((fl & FLAG_LAKE) > 0)
    # the Aral's cells below the level of 1318, joined (8 ways) to the western basin
    r0, c0 = row[aral].min(), col[aral].min()
    grid = np.zeros((row[aral].max() - r0 + 1, col[aral].max() - c0 + 1), bool)
    low = aral & (elev_m(e) < LEVEL)
    grid[row[low] - r0, col[low] - c0] = True
    lab, _ = ndi.label(grid, structure=np.ones((3, 3)))
    sr, sc = int(math.floor((90 - SEED[0]) * RES)) - r0, int(math.floor((SEED[1] + 180) * RES)) - c0
    if lab[sr, sc] == 0:
        raise SystemExit("the seed is not in a cell below the level")
    keep_cell = np.zeros(len(idx), bool)
    keep_cell[low] = lab[row[low] - r0, col[low] - c0] == lab[sr, sc]
    km2 = (111.32 / RES) ** 2 * np.cos(np.radians(lat))
    print(f"{src}: {n} patches, {nl} lakes; the Aral at {aral_level} m: {int(aral.sum())} patched cells, {km2[aral].sum():,.0f} km²")
    print(f"the Aral at {LEVEL} m: {int(keep_cell.sum())} cells, {km2[keep_cell].sum():,.0f} km²; {int((aral & ~keep_cell).sum())} patches dropped")
    keep = ~aral | keep_cell
    lv2 = lv.copy()
    lv2[keep_cell] = int(round(LEVEL))
    idx2, e2, lv2, fl2 = idx[keep], e[keep], lv2[keep], fl[keep]
    out = bytearray(varints(np.diff(idx2, prepend=0)))
    out += e2.astype("<i2").tobytes() + lv2.astype("<i2").tobytes() + fl2.astype(np.uint8).tobytes()
    for (name, level, la, lo, shore, kind, salt), rec in zip(lakes, recs):
        if name == ARAL:
            nb = name.encode("utf-8")
            out += struct.pack("<ffffBBB", LEVEL, SEED[0], SEED[1], SHORE_KM, kind, salt, len(nb)) + nb
        else:
            out += rec
    body = b"EMLK" + struct.pack("<iiii", version, w, len(idx2), nl) + brotli.compress(bytes(out), quality=11, lgwin=24)
    open(dst, "wb").write(body)
    import hashlib
    print(f"{dst}: {len(idx2)} patches, {len(body):,} bytes, sha256 {hashlib.sha256(body).hexdigest()}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
