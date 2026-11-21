"""Reads Earth's lake outline files (EMLO: lakes-glo90-*.bin; EMRO, the river outlines, has the same layout).

Layout: "EMLO", then int32 version, unit (vertex units per degree), tile size (degrees), tiles across, tiles down; a byte
per tile (1: it has lakes); for each such tile an int32 offset and length of its brotli block. A block holds a varint
count of lakes, and per lake: its level (zigzag varint, decimetres), its flags (varint), its rings (varint), and per ring
a varint count of points and zigzag varint steps from the last point (the first from the tile's south-west corner),
x east and y north in units. Tile t is row t // across (from 90 S), column t % across (from 180 W).
"""
import struct

import brotli


def varint(s, i):
    v = sh = 0
    while True:
        c = s[i]; i += 1
        v |= (c & 0x7F) << sh; sh += 7
        if c < 0x80:
            return v, i


def unzz(v):
    return (v >> 1) ^ -(v & 1)


class LakeOutlines:
    def __init__(self, path):
        self.b = open(path, "rb").read()
        b = self.b
        assert b[:4] in (b"EMLO", b"EMRO"), path
        self.version, self.unit, self.tile_deg, self.tx, self.ty = struct.unpack_from("<5i", b, 4)
        states = b[24:24 + self.tx * self.ty]
        off = 24 + self.tx * self.ty
        self.index = {}
        for t in range(self.tx * self.ty):
            if states[t] == 1:
                o, n = struct.unpack_from("<2i", b, off); off += 8
                self.index[t] = (o, n)

    def tile_origin(self, t):
        r, c = divmod(t, self.tx)
        return r * self.tile_deg - 90, c * self.tile_deg - 180

    def lakes(self, t):
        """The tile's lakes in file order: (level m, flags, [rings as lists of (x, y) units])."""
        if t not in self.index:
            return []
        o, n = self.index[t]
        s = brotli.decompress(self.b[o:o + n]); i = 0
        count, i = varint(s, i)
        out = []
        for _ in range(count):
            lv, i = varint(s, i); fl, i = varint(s, i)
            rings_n, i = varint(s, i)
            rings = []
            x = y = 0
            for _ in range(rings_n):
                cnt, i = varint(s, i)
                pts = []
                for _ in range(cnt):
                    dx, i = varint(s, i); dy, i = varint(s, i)
                    x += unzz(dx); y += unzz(dy); pts.append((x, y))
                rings.append(pts)
            out.append((unzz(lv) / 10.0, fl, rings))
        return out
