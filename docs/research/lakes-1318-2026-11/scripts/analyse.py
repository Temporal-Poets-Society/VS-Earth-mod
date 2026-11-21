"""EA-0124: read the probe's windows (Ea0124Probe), count what a player sees at the revived terminal lakes, draw maps.

Usage: python -I analyse.py <dir> <place> [<level m> [<seed lat> <seed lon>]]
Writes <dir>/<place>_<scale>_<variant>.png maps and prints the counts.
"""
import json, sys, os
import numpy as np
from PIL import Image

D = sys.argv[1]
PLACE = sys.argv[2]
LEVEL = float(sys.argv[3]) if len(sys.argv) > 3 else None
SEED = (float(sys.argv[4]), float(sys.argv[5])) if len(sys.argv) > 5 else None


def load(path):
    with open(path, 'rb') as f:
        b = f.read()
    nl = b.index(b'\n')
    h = json.loads(b[:nl])
    o = nl + 1
    nz, nx = h['nz'], h['nx']
    n = nz * nx

    def arr(dt, count):
        nonlocal o
        a = np.frombuffer(b, dtype=dt, count=count, offset=o)
        o += a.nbytes
        return a
    lat = arr('<f8', nz); lon = arr('<f8', nx)
    flags = arr('<u2', n).reshape(nz, nx)
    top = arr('<i2', n).reshape(nz, nx).astype(np.int32)
    wtop = arr('<i2', n).reshape(nz, nx).astype(np.int32)
    fine = arr('<f4', n).reshape(nz, nx)
    elev = arr('<f4', n).reshape(nz, nx)
    lakelv = arr('<f4', n).reshape(nz, nx)
    gridlv = arr('<f4', n).reshape(nz, nx)
    gridshare = arr('<f4', n).reshape(nz, nx)
    gridelev = arr('<f4', n).reshape(nz, nx)
    return h, lat, lon, flags, top, wtop, fine, elev, lakelv, gridlv, gridshare, gridelev


def cell_km2(lat, lon):
    dlat = abs(lat[1] - lat[0]) if len(lat) > 1 else 0
    dlon = abs(lon[1] - lon[0]) if len(lon) > 1 else 0
    return (dlat * 111.32) * (dlon * 111.32 * np.cos(np.radians(lat)))[:, None]


def metres_of_y(h, y):
    ys = np.array(h['yOfMetre'])
    # Y of metre m = ys[m + 100]; invert by interpolation (monotone)
    return np.interp(y, ys, np.arange(len(ys)) + h['yOfMetre0'])


def label(mask):
    from scipy import ndimage as ndi
    return ndi.label(mask)


def analyse(scale, variant):
    path = os.path.join(D, f'{PLACE}_{scale}_{variant}.bin')
    if not os.path.exists(path):
        return None
    h, lat, lon, flags, top, wtop, fine, elev, lakelv, gridlv, gridshare, gridelev = load(path)
    km2 = cell_km2(lat, lon)
    water = (flags & 1) != 0
    river = (flags & 2) != 0
    outline_col = (flags & 4) != 0
    lakew = (flags & 8) != 0
    sea = (flags & 16) != 0
    beach = (flags & 32) != 0
    in_mask_lake = (flags & 4096) != 0
    in_river_outline = (flags & 8192) != 0
    term_cell = (flags & 16384) != 0
    salt = (flags & 32768) != 0
    grid_lake_water = lakew & ~outline_col & ~river
    out = {}
    out['window'] = f"{h['nz']} x {h['nx']} samples, {lat[0]:.3f}..{lat[-1]:.3f} N, {lon[0]:.3f}..{lon[-1]:.3f} E, {abs(lat[1]-lat[0])*111.32*1000:.0f} m apart"
    out['area_lake_water_km2'] = float(km2.repeat(h['nx'], 1)[lakew].sum())
    out['area_grid_lake_water_km2'] = float(km2.repeat(h['nx'], 1)[grid_lake_water].sum())
    out['area_outline_lake_water_km2'] = float(km2.repeat(h['nx'], 1)[lakew & outline_col].sum())
    out['area_river_water_km2'] = float(km2.repeat(h['nx'], 1)[water & river].sum())
    out['area_terminal_cells_km2'] = float(km2.repeat(h['nx'], 1)[term_cell].sum())
    # water surface Y levels of lake water: histogram
    wl = wtop[lakew]
    vals, counts = np.unique(wl, return_counts=True)
    order = np.argsort(-counts)[:8]
    out['lake_water_tops'] = {int(vals[i]): int(counts[i]) for i in order}
    gl = gridlv[grid_lake_water]
    gv, gc = np.unique(np.round(gl, 1), return_counts=True)
    out['grid_lake_levels_m'] = {float(gv[i]): int(gc[i]) for i in np.argsort(-gc)[:6]}
    ol = lakelv[lakew & outline_col]
    ov, oc = np.unique(np.round(ol, 1), return_counts=True)
    out['outline_lake_levels_m'] = {float(ov[i]): int(oc[i]) for i in np.argsort(-oc)[:8]}
    # steps: 4-neighbour pairs both lake water with different water tops
    steps = 0; step_cells = np.zeros_like(water)
    for dz, dx in ((0, 1), (1, 0)):
        a = lakew[:h['nz'] - dz, :h['nx'] - dx]; b = lakew[dz:, dx:]
        ta = wtop[:h['nz'] - dz, :h['nx'] - dx]; tb = wtop[dz:, dx:]
        m = a & b & (ta != tb)
        steps += int(m.sum())
        step_cells[:h['nz'] - dz, :h['nx'] - dx] |= m
        step_cells[dz:, dx:] |= m
    out['water_surface_steps_pairs'] = steps
    # water standing above land: lake water next to a dry column whose top is below the water top
    above = np.zeros_like(water)
    for dz, dx in ((0, 1), (1, 0), (0, -1), (-1, 0)):
        sl_a = (slice(max(0, -dz), h['nz'] - max(0, dz)), slice(max(0, -dx), h['nx'] - max(0, dx)))
        sl_b = (slice(max(0, dz), h['nz'] - max(0, -dz) if dz < 0 else h['nz']), slice(max(0, dx), h['nx'] - max(0, -dx) if dx < 0 else h['nx']))
        sl_b = (slice(max(0, dz), h['nz'] + min(0, dz)), slice(max(0, dx), h['nx'] + min(0, dx)))
        a_w = lakew[sl_a]; b_dry = ~water[sl_b]
        m = a_w & b_dry & (top[sl_b] < wtop[sl_a])
        above[sl_a] |= m
    out['lake_water_above_adjacent_land'] = int(above.sum())
    # dry ground inside the terminal lake's cells below its level (GLO-90 below the level, column dry)
    if LEVEL is not None:
        dry_below = term_cell & ~water & (fine < LEVEL)
        out['dry_in_terminal_cells_glo90_below_level_km2'] = float(km2.repeat(h['nx'], 1)[dry_below].sum())
        # GLO-90 below the level, connected to the seed: the lake a fill of today's GLO-90 surface would give
        if SEED is not None:
            lab, n = label(np.nan_to_num(fine, nan=1e9) < LEVEL)
            j = int(np.argmin(np.abs(lat - SEED[0]))); i = int(np.argmin(np.abs(lon - SEED[1])))
            if lab[j, i] > 0:
                filled = lab == lab[j, i]
                out['glo90_fill_at_level_km2'] = float(km2.repeat(h['nx'], 1)[filled].sum())
                out['glo90_fill_touches_window_edge'] = bool(filled[0].any() or filled[-1].any() or filled[:, 0].any() or filled[:, -1].any())
                out['lake_water_outside_glo90_fill_km2'] = float(km2.repeat(h['nx'], 1)[lakew & ~filled].sum())
                out['glo90_fill_dry_km2'] = float(km2.repeat(h['nx'], 1)[filled & ~water].sum())
    out['salt_water_km2'] = float(km2.repeat(h['nx'], 1)[salt].sum())
    # the map: colours
    img = np.zeros((h['nz'], h['nx'], 3), np.uint8)
    # dry land: grey by GLO-90 height (relative), beach sandy
    f = np.nan_to_num(fine, nan=0)
    lo_, hi_ = np.percentile(f, 2), np.percentile(f, 98)
    g = np.clip((f - lo_) / max(1, hi_ - lo_), 0, 1)
    shade = (90 + 120 * g).astype(np.uint8)
    img[...] = np.stack([shade, shade, shade - 10], -1)
    img[beach & ~water] = (220, 200, 140)
    img[sea] = (20, 40, 120)
    img[grid_lake_water & ~river] = (40, 110, 210)          # the grid's lake (0005's terminal lake): blue
    img[lakew & outline_col] = (0, 200, 200)       # the water mask's outline lakes: cyan
    img[water & river] = (150, 50, 220)            # river water (river columns): purple
    # the world's river courses (the prepared list) as thin yellow lines
    cpath = os.path.join(D, f'{PLACE}_{scale}_courses.csv')
    if os.path.exists(cpath):
        import csv
        rows = list(csv.DictReader(open(cpath)))
        by = {}
        for r in rows:
            by.setdefault(r['reach'], []).append((float(r['lat']), float(r['lon']), float(r['q'])))
        dl = (lat[-1] - lat[0]) / (len(lat) - 1); dn = (lon[-1] - lon[0]) / (len(lon) - 1)
        for pts in by.values():
            if max(p[2] for p in pts) < float(os.environ.get('EA0124_MINQ', '20')):
                continue
            for (a0, o0, _), (a1, o1, _) in zip(pts, pts[1:]):
                m = int(max(abs((a1 - a0) / dl), abs((o1 - o0) / dn)) * 2) + 1
                for t in np.linspace(0, 1, m):
                    jj = int(round((a0 + (a1 - a0) * t - lat[0]) / dl)); ii = int(round((o0 + (o1 - o0) * t - lon[0]) / dn))
                    if 0 <= jj < len(lat) and 0 <= ii < len(lon) and not water[jj, ii]:
                        img[jj, ii] = (250, 230, 0)
    if LEVEL is not None:
        img[term_cell & ~water & ~beach & (fine < LEVEL)] = (230, 120, 30)  # dry ground (not beach) below the level in the lake's cells: orange
    img[step_cells] = (255, 0, 0)                  # water surface steps: red
    img[above & ~step_cells] = (255, 0, 255)       # water above adjacent land: magenta
    im = Image.fromarray(img)
    png = os.path.join(D, f'{PLACE}_{scale}_{variant}.png')
    im.save(png)
    # also a water-level map: water top colour-coded
    return out


for scale in (10, 100):
    for variant in ('base', 'noends'):
        r = analyse(scale, variant)
        if r is None:
            continue
        print(f'== {PLACE} 1:{scale} {variant}')
        for k, v in r.items():
            print(f'  {k}: {v}')
