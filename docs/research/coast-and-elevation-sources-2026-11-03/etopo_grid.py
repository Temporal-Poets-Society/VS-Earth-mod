"""Windowed reading of the global ETOPO 2022 GeoTIFFs (tiled, 256x256), shared by elev_compare.py and fjord_profile.py.
Only the tiles a window needs are decoded, so the 1.5 GB 30s file is never loaded whole."""
import math
import numpy as np
import tifffile


class Grid:
    def __init__(self, path):
        self.tif = tifffile.TiffFile(path)
        self.page = self.tif.pages[0]
        self.h, self.w = self.page.shape
        self.res = self.w // 360                      # cells per degree
        self.tw, self.th = self.page.tilewidth, self.page.tilelength
        self.across = math.ceil(self.w / self.tw)
        self.fh = self.tif.filehandle
        self.cache = {}

    def tile(self, ty, tx):
        k = (ty, tx)
        if k not in self.cache:
            i = ty * self.across + tx
            self.fh.seek(self.page.dataoffsets[i])
            data = self.fh.read(self.page.databytecounts[i])
            a = self.page.decode(data, i)[0]
            self.cache[k] = np.asarray(a).reshape(self.th, self.tw)
        return self.cache[k]

    def window(self, r0, r1, c0, c1):
        out = np.empty((r1 - r0, c1 - c0), np.float32)
        for ty in range(r0 // self.th, (r1 - 1) // self.th + 1):
            for tx in range(c0 // self.tw, (c1 - 1) // self.tw + 1):
                t = self.tile(ty, tx)
                ra, rb = max(r0, ty * self.th), min(r1, (ty + 1) * self.th)
                ca, cb = max(c0, tx * self.tw), min(c1, (tx + 1) * self.tw)
                out[ra - r0:rb - r0, ca - c0:cb - c0] = t[ra - ty * self.th:rb - ty * self.th, ca - tx * self.tw:cb - tx * self.tw]
        return out

    def rc(self, lat, lon):
        return int((90 - lat) * self.res), int((lon + 180) * self.res)


def mean2(a):
    """1' to Earth's 2': the mean of aligned 2x2 blocks (a starts on an even row and column)."""
    h, w = a.shape[0] // 2 * 2, a.shape[1] // 2 * 2
    return a[:h, :w].reshape(h // 2, 2, w // 2, 2).mean(axis=(1, 3))
