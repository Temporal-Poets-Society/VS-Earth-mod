"""Shared helpers for the ore-data audit (read-only on the Earth repo)."""
import base64, gzip, json, math, os, sys
from pathlib import Path
import numpy as np
import shapefile  # pyshp
import shapely
from shapely.geometry import shape, Point

EARTH = Path(r'C:\VS mods\Earth')
CAT = EARTH / 'assets/earth/config/ore-deposits-v1'
NE_ZIP = Path(r'C:\VS mods\data\earth-raw\ore\coal-evaporites\ne_10m_admin_0_countries.zip')
GROUPS = ['base-metals', 'coal-evaporites', 'iron-ferro', 'precious-gems']
W, H = 720, 360

# ------------------------------------------------------------------ regions
SOUTHERN_AFRICA = {'ZAF', 'BWA', 'NAM', 'LSO', 'SWZ', 'ZWE', 'ZMB', 'MOZ', 'MWI', 'MDG'}
CENTRAL_ASIA = {'KAZ', 'UZB', 'KGZ', 'TJK', 'TKM', 'MNG', 'AFG'}
EAST_ASIA = {'JPN', 'KOR', 'PRK', 'TWN'}
OCEANIA = {'AUS', 'NZL'}

REGIONS = ['USA', 'Canada+Greenland', 'Mexico+C.Am+Carib', 'South America', 'Europe', 'Russia W of 60E',
           'Russia E of 60E', 'C.Asia+Mongolia+Afg', 'China', 'East Asia other', 'South Asia', 'SE Asia',
           'Middle East+Caucasus', 'North Africa', 'West Africa', 'Central+East Africa', 'Southern Africa',
           'Australia+NZ', 'Melanesia+Pacific', 'Antarctica', 'Ocean/none']
COMPACT = {'USA': 'USA+Canada', 'Canada+Greenland': 'USA+Canada', 'Mexico+C.Am+Carib': 'Latin America',
           'South America': 'Latin America', 'Europe': 'Europe', 'Russia W of 60E': 'Russia', 'Russia E of 60E': 'Russia',
           'C.Asia+Mongolia+Afg': 'Central Asia+Mongolia', 'China': 'China', 'East Asia other': 'Other Asia',
           'South Asia': 'Other Asia', 'SE Asia': 'Other Asia', 'Middle East+Caucasus': 'Other Asia',
           'North Africa': 'Africa', 'West Africa': 'Africa', 'Central+East Africa': 'Africa', 'Southern Africa': 'Africa',
           'Australia+NZ': 'Oceania', 'Melanesia+Pacific': 'Oceania', 'Antarctica': 'Antarctica', 'Ocean/none': 'Ocean/none'}
COMPACT_ORDER = ['USA+Canada', 'Latin America', 'Europe', 'Russia', 'Central Asia+Mongolia', 'China', 'Other Asia',
                 'Africa', 'Oceania', 'Antarctica', 'Ocean/none']


def region_of(a3, continent, subregion, lon):
    if a3 == 'USA': return 'USA'
    if a3 in ('CAN', 'GRL'): return 'Canada+Greenland'
    if a3 == 'RUS': return 'Russia W of 60E' if lon < 60 else 'Russia E of 60E'
    if a3 == 'CHN' or a3 in ('HKG', 'MAC'): return 'China'
    if a3 in CENTRAL_ASIA: return 'C.Asia+Mongolia+Afg'
    if a3 in EAST_ASIA: return 'East Asia other'
    if a3 in OCEANIA: return 'Australia+NZ'
    if a3 == 'IRN': return 'Middle East+Caucasus'
    if a3 in SOUTHERN_AFRICA: return 'Southern Africa'
    if continent == 'Antarctica' or a3 == 'ATA': return 'Antarctica'
    if a3 == 'MEX' or subregion in ('Central America', 'Caribbean'): return 'Mexico+C.Am+Carib'
    if continent == 'South America': return 'South America'
    if continent == 'Europe': return 'Europe'
    if subregion == 'Western Asia': return 'Middle East+Caucasus'
    if subregion == 'Southern Asia': return 'South Asia'
    if subregion == 'South-Eastern Asia': return 'SE Asia'
    if subregion == 'Northern Africa': return 'North Africa'
    if subregion == 'Western Africa': return 'West Africa'
    if continent == 'Africa': return 'Central+East Africa'
    if continent == 'Oceania': return 'Melanesia+Pacific'
    if continent == 'Asia': return 'East Asia other'
    if continent == 'North America': return 'Mexico+C.Am+Carib'
    return 'Ocean/none'


class Countries:
    def __init__(self):
        r = shapefile.Reader(str(NE_ZIP))
        names = [f[0] for f in r.fields[1:]]
        self.rows = []; geoms = []
        for sr in r.iterShapeRecords():
            rec = dict(zip(names, sr.record))
            g = shape(sr.shape.__geo_interface__)
            if not g.is_valid: g = shapely.make_valid(g)
            a3 = rec['ADM0_A3']
            self.rows.append({'admin': rec['ADMIN'], 'sov': rec['SOVEREIGNT'], 'a3': a3, 'continent': rec['CONTINENT'],
                              'subregion': rec['SUBREGION']})
            geoms.append(g)
        self.geoms = np.array(geoms, dtype=object)
        self.tree = shapely.STRtree(self.geoms)

    def lookup(self, lat, lon, max_deg=1.0):
        """(country row, distance in degrees) of the containing or nearest country within max_deg."""
        p = Point(lon, lat)
        hits = self.tree.query(p, predicate='intersects')
        if len(hits): return self.rows[int(hits[0])], 0.0
        i = self.tree.nearest(p)
        if i is None: return None, None
        d = self.geoms[int(i)].distance(p)
        if d <= max_deg: return self.rows[int(i)], d
        return None, d

    def region(self, lat, lon):
        row, d = self.lookup(lat, lon)
        if row is None: return 'Ocean/none', None, d
        return region_of(row['a3'], row['continent'], row['subregion'], lon), row, d

    def lookup_many(self, lats, lons):
        pts = shapely.points(np.asarray(lons), np.asarray(lats))
        idx_pt, idx_g = self.tree.query(pts, predicate='intersects')
        out = np.full(len(pts), -1, int); out[idx_pt] = idx_g
        return out


# ------------------------------------------------------------------ heat maps
def load_heat(version=2):
    d = json.loads((EARTH / f'assets/earth/config/ore-heatmaps-v{version}.json').read_text())
    out = {}
    N = W * H
    for l in d['layers']:
        raw = gzip.decompress(base64.b64decode(l['data']))
        planes = np.frombuffer(raw[:3 * N], np.uint8).reshape(3, H, W).astype(np.float32) / 255
        depth = np.frombuffer(raw[3 * N:], '<u2').reshape(H, W)
        out[l['code']] = {'obs': planes[0], 'pot': planes[1], 'deep': planes[2], 'depth': depth, 'label': l['label'],
                          'deposits': l['deposits']}
    return out, d


def land_ice():
    """Land and ice fraction per 0.5 degree cell, from preview/grids.npz flags as the bake does."""
    with np.load(EARTH / 'preview/grids.npz') as p:
        flags = p['flags']
    def block(a, f):
        h, w = a.shape
        return a.reshape(h // f, f, w // f, f).mean(axis=(1, 3))
    land = block(((flags & 1) == 0).astype(np.float32), 15)
    ice = block(((flags & 8) > 0).astype(np.float32), 15)
    return land, ice


def cell_area_km2():
    lat = 89.75 - np.arange(H) / 2.0
    a = (111.195 * .5) ** 2 * np.cos(np.radians(lat))
    return np.repeat(a[:, None], W, axis=1)


# ------------------------------------------------------------------ catalogue
def load_catalogue():
    cats = {}
    for g in GROUPS:
        cats[g] = json.loads((CAT / f'{g}.json').read_text(encoding='utf8'))
    return cats


def metal_tonnes(mt, grade, unit):
    """Contained metal in tonnes (for % and g/t); carats for ct/t; None otherwise."""
    if grade is None or mt is None: return None
    if unit == '%': return mt * 1e6 * grade / 100.0
    if unit == 'g/t': return mt * grade  # Mt x g/t = t
    if unit == 'kg/t': return mt * 1e3 * grade
    if unit == 'ct/t': return mt * 1e6 * grade  # carats
    return None
