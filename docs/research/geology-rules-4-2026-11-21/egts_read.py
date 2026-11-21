"""Read an EGTS rock set (bake_rock_30s.py) and look up cells."""
import struct, json, zlib, numpy as np
def read_set(path):
    b = open(path, "rb").read(); assert b[:4] == b"EGTS"
    n = struct.unpack_from("<I", b, 4)[0]; o = 8; out = []
    for _ in range(n):
        L = struct.unpack_from("<I", b, o)[0]; o += 4; t = b[o:o + L]; o += L
        hl = struct.unpack_from("<I", t, 4)[0]; h = json.loads(t[8:8 + hl])
        body = zlib.decompress(t[8 + hl:]); planes = {}; off = 0
        for f in h["fields"]:
            a = np.frombuffer(body, "<u2", h["nx"] * h["ny"], off).reshape(h["ny"], h["nx"]); off += a.nbytes
            planes[f["name"]] = a
        out.append((h, planes))
    return out
def lookup(tiles, lat, lon, field):
    for h, p in tiles:
        i = int((lon - h["west"]) / h["step"]); j = int((h["north"] - lat) / h["step"])
        if 0 <= i < h["nx"] and 0 <= j < h["ny"]:
            v = p[field][j, i]; leg = h["fields"][0]["legend"]
            return None if v == 65535 else leg[v]
    return None
