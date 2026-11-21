"""Minimal Mapbox Vector Tile reader: layers, feature properties and polygon rings in tile pixels (no dependencies)."""
import math, struct, urllib.request, gzip

def _varint(b, i):
    r = s = 0
    while True:
        c = b[i]; i += 1
        r |= (c & 0x7F) << s; s += 7
        if c < 0x80: return r, i

def _fields(b):
    i = 0
    while i < len(b):
        key, i = _varint(b, i); f, t = key >> 3, key & 7
        if t == 0: v, i = _varint(b, i)
        elif t == 1: v = b[i:i+8]; i += 8
        elif t == 2:
            n, i = _varint(b, i); v = b[i:i+n]; i += n
        elif t == 5: v = b[i:i+4]; i += 4
        else: raise ValueError(t)
        yield f, t, v

def _value(b):
    for f, t, v in _fields(b):
        if f == 1: return v.decode("utf-8", "replace")
        if f == 2: return struct.unpack("<f", v)[0]
        if f == 3: return struct.unpack("<d", v)[0]
        if f in (4, 5): return v
        if f == 6: return (v >> 1) ^ -(v & 1)
        if f == 7: return bool(v)

def _packed(b):
    out, i = [], 0
    while i < len(b):
        v, i = _varint(b, i); out.append(v)
    return out

def _rings(cmds):
    rings, cur, x, y, i = [], [], 0, 0, 0
    while i < len(cmds):
        c = cmds[i]; i += 1; op, n = c & 7, c >> 3
        if op in (1, 2):
            for _ in range(n):
                dx, dy = cmds[i], cmds[i+1]; i += 2
                x += (dx >> 1) ^ -(dx & 1); y += (dy >> 1) ^ -(dy & 1)
                if op == 1:
                    if cur: rings.append(cur)
                    cur = [(x, y)]
                else: cur.append((x, y))
        elif op == 7:
            if cur: rings.append(cur); cur = []
    if cur: rings.append(cur)
    return rings

def decode(data):
    if data[:2] == b"\x1f\x8b": data = gzip.decompress(data)
    layers = {}
    for f, t, lb in _fields(data):
        if f != 3: continue
        name, keys, vals, feats, extent = None, [], [], [], 4096
        raw = []
        for g, u, v in _fields(lb):
            if g == 1: name = v.decode()
            elif g == 2: raw.append(v)
            elif g == 3: keys.append(v.decode())
            elif g == 4: vals.append(_value(v))
            elif g == 5: extent = v
        for fb in raw:
            tags, gtype, geom = [], 0, []
            for g, u, v in _fields(fb):
                if g == 2: tags = _packed(v)
                elif g == 3: gtype = v
                elif g == 4: geom = _packed(v)
            props = {keys[tags[k]]: vals[tags[k+1]] for k in range(0, len(tags), 2)}
            feats.append({"props": props, "type": gtype, "rings": _rings(geom) if gtype == 3 else None})
        layers[name] = {"extent": extent, "features": feats}
    return layers

def tile_xy(lat, lon, z):
    n = 2 ** z
    x = int((lon + 180) / 360 * n)
    y = int((1 - math.log(math.tan(math.radians(lat)) + 1 / math.cos(math.radians(lat))) / math.pi) / 2 * n)
    return x, y

def fetch(layer, z, x, y):
    req = urllib.request.Request(f"https://tiles.macrostrat.org/{layer}/{z}/{x}/{y}.mvt", headers={"User-Agent": "earth-geology-check/1.0", "Accept-Encoding": "gzip"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()
