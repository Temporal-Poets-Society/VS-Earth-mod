"""Where a cell of Copernicus GLO-90's water mask and heights lies (EA-0148, decision 0092).

GLO-90's tiles are pixel-is-point (GTRasterTypeGeoKey RasterPixelIsPoint, the tie point on the tile's north-west
corner): cell (r, c) of the tile with south-west corner (lat0, lon0) is centred at (lat0 + 1 - r/rows, lon0 + c/cols),
and its area is half a cell either way of that. FineElevation reads the heights so. The water bakes before EA-0148 took
cell (r, c) as the area from (lat0 + 1 - r/rows, lon0 + c/cols) down and east to the next row and column, half a cell
south-east of the heights: every lake and river outline, the snapped courses, the works of 1318's canals and the flow
file's reader (RiverFlowData) carried it (docs/research/ea-0148-offset-2026-11).

The bake tools keep their area logic (a cell spans r..r+1, c..c+1 from an origin; floor from a position to a cell, + 0.5
back) and take the origin from here: `top(lat0 + 1, rows)` and `left(lon0, cols)` are the north-west corner of cell (0, 0)'s
area. On the pixel-is-point grid (the default, EARTH_GLO90_GRID=point) that is half a cell north and west of the tile's
corner; EARTH_GLO90_GRID=cell gives the old placement back, to rebake the files older worlds read.

Narrow high-latitude tiles (fewer than 1,200 columns) are put on a uniform 1/1200° grid by `columns`: each column takes
the cell whose centre is nearest (pixel-is-point), or the cell whose area holds its west edge (the old placement).
"""
import os

import numpy as np

POINT = os.environ.get("EARTH_GLO90_GRID", "point") != "cell"
ROWS = 1200


def half(n):
    """Half a cell of a grid with n cells a degree, in degrees: 0 on the old placement."""
    return 0.5 / n if POINT else 0.0


def top(lat_edge, rows=ROWS):
    """The latitude of the north side of row 0's area, for a grid whose row 0 lies on lat_edge."""
    return lat_edge + half(rows)


def left(lon_edge, cols):
    """The longitude of the west side of column 0's area, for a grid whose column 0 lies on lon_edge."""
    return lon_edge - half(cols)


def column_index(n, cols):
    """For n columns a degree on a tile of cols: the tile's column each takes (cols where it is the next tile's first)."""
    k = np.arange(n)
    if POINT:
        return np.floor(k * cols / n + 0.5).astype(np.int64)
    return (k * cols) // n


def columns(t, n, east=None):
    """Tile array t (rows x cols) on n columns a degree (see column_index). Where a column takes the next tile's first
    column, east (that tile's array, or None for open sea or no data) gives it; without it the tile's last stands in."""
    idx = column_index(n, t.shape[1])
    over = idx >= t.shape[1]
    out = t[:, np.minimum(idx, t.shape[1] - 1)]
    if over.any() and east is not None:
        e = east[:, 0] if east.shape[0] == t.shape[0] else east[:, 0][np.minimum(np.arange(t.shape[0]), east.shape[0] - 1)]
        out = out.copy()
        out[:, over] = e[:, None]
    return out


def describe():
    return "pixel-is-point (EA-0148)" if POINT else "cell areas from the tile's corner (before EA-0148)"
