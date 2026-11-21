"""GLO-90's water mask and heights around a place, on their own grid: pixel-is-point (GTRasterTypeGeoKey 2, tie point on
the tile's north-west corner), so cell (r, c) of the tile with south-west corner (lat0, lon0) is centred at
(lat0 + 1 - r/1200, lon0 + c/cols). Reads the raw Copernicus tiles under EARTH_RAW (default C:\\VS mods\\data\\earth-raw)."""
import math, os
import numpy as np
import tifffile

RAW = os.environ.get("EARTH_RAW", r"C:\VS mods\data\earth-raw")
ROWS = 1200


def cols_for(lat_s):
    a = lat_s if lat_s >= 0 else -lat_s - 1
    return 1200 if a < 50 else 800 if a < 60 else 600 if a < 70 else 400 if a < 80 else 240 if a < 85 else 120


def tile_name(lat_s, lon_w, kind):
    ns = ("N%02d" % lat_s) if lat_s >= 0 else ("S%02d" % -lat_s)
    ew = ("E%03d" % lon_w) if lon_w >= 0 else ("W%03d" % -lon_w)
    folder = "copernicus-glo90-wbm" if kind == "WBM" else "copernicus-glo90"
    return os.path.join(RAW, folder, "Copernicus_DSM_COG_30_%s_00_%s_00_%s.tif" % (ns, ew, kind))


_cache = {}


def tile(lat_s, lon_w, kind):
    key = (lat_s, lon_w, kind)
    if key not in _cache:
        p = tile_name(lat_s, lon_w, kind)
        if os.path.exists(p):
            a = tifffile.imread(p)
        else:  # no tile: open sea (mask 1, height 0)
            a = np.full((ROWS, cols_for(lat_s)), 1 if kind == "WBM" else 0, np.uint8 if kind == "WBM" else np.float32)
        if len(_cache) > 24:
            _cache.clear()
        _cache[key] = a
    return _cache[key]


def nearest(lat, lon, kind):
    """The value of the grid point nearest each (lat, lon) (arrays): pixel-is-point, so round, not floor."""
    lat = np.asarray(lat, float)
    lon = np.asarray(lon, float)
    out = np.zeros(np.broadcast(lat, lon).shape, np.float64)
    lat, lon = np.broadcast_arrays(lat, lon)
    # global row from the north pole and the square that holds it: row r of square lat_s is at lat_s + 1 - r/1200
    g = np.rint((90 - lat) * ROWS).astype(np.int64)
    lat_s = 89 - g // ROWS
    r = g % ROWS
    for ls in np.unique(lat_s):
        m = lat_s == ls
        c_all = cols_for(int(ls))
        gc = np.rint((lon[m] + 180) * c_all).astype(np.int64)
        lon_w = gc // c_all - 180
        cc = gc % c_all
        rr = r[m]
        vals = np.zeros(m.sum())
        for lw in np.unique(lon_w):
            k = lon_w == lw
            t = tile(int(ls), int((lw + 180) % 360 - 180), kind)
            vals[k] = t[rr[k], cc[k]]
        out[m] = vals
    return out
