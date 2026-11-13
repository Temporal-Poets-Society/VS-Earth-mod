"""Bake the runtime ore evidence grids (assets/earth/config/ore-heatmaps-v1.json).

File format is unchanged (v1): 720x360 north-up 0.5-degree cells, x=floor((lon+180)*2), y=floor((90-lat)*2);
per layer gzip(base64) of three uint8 planes observed|potential|deep followed by a little-endian uint16 plane
holding the minimum depth in metres of the deep evidence. Layer codes, order, labels and deposit bindings are
the same as v1. The evidence MODEL is v2 (settings tools/ore-heatmap-settings.json, version 2):

* every record gets a peak weight from its evidence tier (major-deposit compilation > specialist deposit >
  prospect > occurrence; MRDS by development status x MRDS information score) and a tier-dependent kernel;
* MRDS weights are normalised by local survey density (commodity enrichment relative to all MRDS records in a
  2.5-degree window, plus effort damping) so densely surveyed regions do not saturate;
* polygons are repaired (make_valid), split into parts, unwrapped across the antimeridian and rasterised with
  covered-area fractions (weight x (min + (1-min) x fraction)) instead of "any touch lights the whole cell";
* low-weight host-rock (GLiM) and climate priors go only into the potential plane;
* records whose coordinates fall outside their reported country (with margin) are dropped and logged, known bad
  coordinates are corrected from a reviewed table, and stacks of many differently named MRDS records at one
  identical coordinate (country/county placeholders) are dropped;
* sylvite is a child of halite in VS: every K stamp is mirrored into NaCl at the same weight;
* native sulfur no longer counts pyrite-only mentions;
* registry entries with tierRule 'authored' (the best-guess district table authored for this project from general
  knowledge where no survey layer is on disk) take their peak from settings authoredBestGuess (size x confidence,
  capped at 0.85) and their kernel radius from each entry.

Ore rework groups (decision 0065): every `tools/ore-data/<group>/evidence.geojson` adds features in the
group evidence schema (see read_groups), and every `tools/ore-data/<group>/heatmap-overrides.json` may drop
existing sources for its own layer codes ({"drop": {"C_coal": ["<source name>", ...]}}; a source name is what the
bake manifest lists under acceptedSourceAssociations, or "mrds" for every MRDS record of that code).

Options: --out <file> and --report <folder> write a test bake elsewhere (the runtime file and report are untouched).

No network access. Requires tools/requirements-ore.txt in the project venv.
"""
from pathlib import Path
from collections import Counter, defaultdict
import base64, csv, gzip, hashlib, json, math, os, re, sys

import numpy as np
import shapely
from shapely.geometry import shape, box, Polygon, MultiPolygon
from scipy.ndimage import uniform_filter, maximum_filter

ROOT = Path(__file__).resolve().parents[1]
ATLAS = ROOT / 'docs/research/remaining-ores-2026-09-29'
CUTIN = ROOT / 'docs/research/copper-tin-global-2026-09-29/normalized'
sys.path.insert(0, str(ATLAS))
from build_mrds import COMMODITIES  # noqa: E402
from correction_layers import LABELS  # noqa: E402

SETTINGS = ROOT / 'tools/ore-heatmap-settings.json'
OUT = ROOT / 'assets/earth/config/ore-heatmaps-v1.json'
REPORT = ROOT / 'docs/research/ore-heatmaps-v1'
GROUPS = ROOT / 'tools/ore-data'
for _i, _a in enumerate(sys.argv[1:-1], 1):
    if _a == '--out': OUT = Path(sys.argv[_i + 1]).resolve()
    if _a == '--report': REPORT = Path(sys.argv[_i + 1]).resolve()
GEOLOGY = ROOT / 'assets/earth/config/geology-grids-v1.json'
ENV = ROOT / 'preview/grids.npz'
W, H = 720, 360
N = W * H
CODES = list(COMMODITIES) + ['Cu', 'Sn', 'peat', 'clay']
EXTRA_LABELS = {'Cu': 'Copper', 'Sn': 'Tin', 'peat': 'Peat', 'clay': 'Clay'}
METALS = ['Cu', 'Sn', 'Fe', 'Au', 'Ag', 'Pb', 'Zn', 'Ni', 'Cr', 'Mn', 'W', 'U', 'Hg', 'Mo', 'Sb', 'Bi', 'Ti', 'Pt', 'V']
KM_PER_DEG = 111.195


# ----------------------------------------------------------------------------------------------- helpers
def combine_deep(old_weight, old_depth, new_weight, new_depth):
    """Keep a depth paired with its evidence strength; never give a deep strong record a shallow weak depth.

    The compact v1 format retains only the strongest record (shallower wins ties).
    A weaker shallower record may be omitted, so this is conservative about accessibility.
    """
    replace = (new_weight > old_weight) | ((new_weight == old_weight) & ((old_depth == 0) | (new_depth < old_depth)))
    return np.maximum(old_weight, new_weight), np.where(replace, new_depth, old_depth)


def features(path):
    """Stream full-resolution FeatureCollections, including the large gypsum file."""
    opener = gzip.open if str(path).endswith('.gz') else open
    with opener(path, 'rt', encoding='utf-8-sig') as f:
        buf = ''; decoder = json.JSONDecoder(); started = False
        while True:
            chunk = f.read(262144); buf += chunk
            if not started:
                key = buf.find('"features"')
                if key < 0:
                    if not chunk: raise ValueError('No features: ' + str(path))
                    continue
                bracket = buf.find('[', key)
                if bracket < 0: continue
                buf = buf[bracket + 1:]; started = True
            while True:
                buf = buf.lstrip(' \r\n\t,')
                if buf.startswith(']'): return
                try: item, end = decoder.raw_decode(buf)
                except json.JSONDecodeError: break
                yield item; buf = buf[end:]
            if not chunk:
                raise ValueError('Truncated feature collection: ' + str(path))


def cell_x(lon):
    """Column index; math.floor semantics (v1 used int() which truncated toward zero west of -180+0.5)."""
    return np.mod(np.floor((np.asarray(lon, dtype=np.float64) + 180.0) * 2).astype(np.int64), W)


def cell_y(lat):
    return np.clip(np.floor((90.0 - np.asarray(lat, dtype=np.float64)) * 2).astype(np.int64), 0, H - 1)


def kernel_offsets(lat, lon, radius):
    """Cells within `radius` km of each point: returns (point_index, ys, xs, closeness 0..1) flattened.

    Closeness is 1 - distance/radius (distance from the point to cell centres); the containing cell always gets 1.
    """
    lat = np.asarray(lat, dtype=np.float64); lon = np.asarray(lon, dtype=np.float64)
    if not len(lat): return [np.zeros(0, np.int64)] * 3 + [np.zeros(0)]
    cy = cell_y(lat); cx = cell_x(lon)
    ky = int(math.ceil(radius / (KM_PER_DEG * .5))) + 1
    coslat = np.maximum(np.cos(np.radians(lat)), 0.05)
    out = [[], [], [], []]
    # Group by needed x reach so high-latitude points do not inflate low-latitude work.
    kx_all = np.minimum(W // 2, np.ceil(radius / (KM_PER_DEG * .5 * coslat)).astype(int) + 1)
    for kx in np.unique(kx_all):
        sel = np.nonzero(kx_all == kx)[0]
        dy, dx = np.meshgrid(np.arange(-ky, ky + 1), np.arange(-kx, kx + 1), indexing='ij')
        dy = dy.ravel(); dx = dx.ravel()
        ys = cy[sel, None] + dy[None, :]
        xs = np.mod(cx[sel, None] + dx[None, :], W)
        clat = 89.75 - ys / 2.0; clon = xs / 2.0 - 179.75
        dl = np.mod(clon - lon[sel, None] + 180.0, 360.0) - 180.0
        dist = np.hypot(dl * KM_PER_DEG * coslat[sel, None], (clat - lat[sel, None]) * KM_PER_DEG)
        close = 1 - dist / radius
        home = (dy[None, :] == 0) & (dx[None, :] == 0)
        close = np.where(home, 1.0, close)
        ok = (close >= 0) & (ys >= 0) & (ys < H)
        pi = np.broadcast_to(sel[:, None], ys.shape)
        out[0].append(pi[ok]); out[1].append(ys[ok]); out[2].append(xs[ok]); out[3].append(close[ok])
    return [np.concatenate(o) for o in out]


def polygon_parts(geom):
    """Repair, split and unwrap a polygonal geometry. Yields (part, repaired, unwrapped)."""
    repaired = False
    if not geom.is_valid:
        geom = shapely.make_valid(geom); repaired = True
    parts = []
    def collect(g):
        if isinstance(g, Polygon):
            if not g.is_empty: parts.append(g)
        elif hasattr(g, 'geoms'):
            for sub in g.geoms: collect(sub)
    collect(geom)
    for p in parts:
        west, south, east, north = p.bounds
        unwrapped = False
        if east - west > 180:
            # Ring drawn across the antimeridian: move western-hemisphere vertices to +180..+360.
            p = shapely.transform(p, lambda c: np.where(c[:, :1] < 0, c + [360.0, 0.0], c))
            if not p.is_valid: p = shapely.make_valid(p)
            unwrapped = True
            if not isinstance(p, Polygon):
                for q in getattr(p, 'geoms', []):
                    if isinstance(q, Polygon) and not q.is_empty: yield q, repaired, True
                continue
        yield p, repaired, unwrapped


def polygon_fractions(part, samples=8):
    """Covered fraction of every 0.5-degree cell touched by a polygon part (lon may run to +360)."""
    west, south, east, north = part.bounds
    x0 = int(math.floor((west + 180) * 2)); x1 = int(math.floor((east + 180) * 2))
    y0 = max(0, int(math.floor((90 - north) * 2))); y1 = min(H - 1, int(math.floor((90 - south) * 2)))
    if y1 < y0 or x1 < x0: return np.zeros(0, int), np.zeros(0, int), np.zeros(0)
    if x0 == x1 and y0 == y1:  # fast path: part lies inside one cell
        return np.array([y0]), np.array([x0 % W]), np.array([min(1.0, part.area / .25)])
    gx, gy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
    gx = gx.ravel(); gy = gy.ravel()
    if len(gx) <= 16:
        boxes = shapely.box(gx / 2 - 180, 89.5 - gy / 2, gx / 2 - 179.5, 90 - gy / 2)
        frac = shapely.area(shapely.intersection(part, boxes)) / .25
    else:
        shapely.prepare(part)
        off = (np.arange(samples) + .5) / samples / 2
        sx = (gx[:, None, None] / 2 - 180 + off[None, None, :])
        sy = (90 - gy[:, None, None] / 2 - off[None, :, None])
        sx, sy = np.broadcast_arrays(sx, sy)
        hits = shapely.contains_xy(part, sx.ravel(), sy.ravel()).reshape(len(gx), samples * samples)
        frac = hits.mean(axis=1)
        if not frac.any():  # sliver thinner than the sampling pitch
            boxes = shapely.box(gx / 2 - 180, 89.5 - gy / 2, gx / 2 - 179.5, 90 - gy / 2)
            frac = shapely.area(shapely.intersection(part, boxes)) / .25
    keep = frac > 0
    return gy[keep], np.mod(gx[keep], W), np.minimum(1.0, frac[keep])


def parse_depth(v):
    """First (shallowest) number of a depth attribute such as '350-620; 370-700' or '>1200'."""
    if v is None: return None
    if isinstance(v, (int, float)): return float(v) if math.isfinite(v) and v > 0 else None
    m = re.search(r'\d+(?:\.\d+)?', str(v))
    return float(m.group()) if m else None


def sha_file(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for b in iter(lambda: f.read(1 << 20), b''): h.update(b)
    return h.hexdigest()


# ----------------------------------------------------------------------------------------------- country check
class CountryCheck:
    """Country sanity check for point records.

    Primary test: the point lies within `tolerance` degrees of the Natural Earth 1:10m admin-0 polygons of the
    reported country's sovereign state (overseas territories included; a few historical/disputed labels are unions).
    Labels Natural Earth cannot resolve fall back to the coarse boxes in country-bboxes.csv. Coordinates that fail
    are repaired only when exactly one sign-flip/lat-lon-swap candidate falls inside the country (no tolerance)."""
    CANDIDATES = [('flip_lat', lambda la, lo: (-la, lo)), ('flip_lon', lambda la, lo: (la, -lo)),
                  ('flip_both', lambda la, lo: (-la, -lo)), ('swap', lambda la, lo: (lo, la)),
                  ('swap_flip_lat', lambda la, lo: (-lo, la)), ('swap_flip_lon', lambda la, lo: (lo, -la)),
                  ('swap_flip_both', lambda la, lo: (-lo, -la))]
    NE_NAMES = {'Burma': 'Myanmar', 'Congo (Brazzaville)': 'Republic of the Congo', 'Congo (Kinshasa)': 'Democratic Republic of the Congo',
                "Cote D'Ivoire": 'Ivory Coast', 'French Guiana': 'France', 'Korea, North': 'North Korea', 'Korea, South': 'South Korea',
                'Macedonia': 'North Macedonia', 'Swaziland': 'eSwatini'}
    # Source labels that predate or ignore later borders: accept the union of these sovereign states.
    NE_UNIONS = {'Morocco': ['Morocco', 'Western Sahara'], 'Sudan': ['Sudan', 'South Sudan'], 'Serbia': ['Republic of Serbia', 'Kosovo', 'Montenegro'],
                 'Somalia': ['Somalia', 'Somaliland'], 'Cyprus': ['Cyprus', 'Northern Cyprus'], 'Ethiopia': ['Ethiopia', 'Eritrea'],
                 'Western Sahara': ['Western Sahara', 'Morocco'], 'Israel': ['Israel', 'Palestine']}
    # Natural Earth 5.x draws de-facto control; source labels follow de-jure borders here (Crimea under Ukraine).
    EXTRA_BOXES = {'Ukraine': [(32.4, 44.3, 36.7, 46.3)]}
    REPAIR_TOLERANCE = .1

    def __init__(self, table, margin, ne_path=None, tolerance=.5):
        self.margin = margin; self.tolerance = tolerance; self.boxes = defaultdict(list); self.names = {}; self.geoms = {}; self.cache = {}
        with open(table, encoding='utf-8') as f:
            for r in csv.DictReader(f):
                b = tuple(float(r[k]) for k in ['west', 'south', 'east', 'north'])
                for name in [r['country']] + [a for a in r['aliases'].split('|') if a]:
                    key = name.strip().casefold(); self.boxes[key].append(b); self.names[key] = r['country']
        if ne_path:
            from pyogrio.raw import read
            fields = ['SOVEREIGNT', 'ADMIN', 'NAME', 'NAME_LONG', 'FORMAL_EN', 'NAME_EN']
            _, _, wkb, cols = read('/vsizip/' + Path(ne_path).as_posix() + '/ne_10m_admin_0_countries.shp', columns=fields)
            geoms = shapely.from_wkb(wkb); sov = defaultdict(list); lookup = {}
            for i, g in enumerate(geoms):
                sv = str(cols[0][i]); sov[sv].append(g)
                for c in cols: lookup.setdefault(str(c[i]).casefold(), str(cols[0][i]))
            sov = {k: shapely.union_all(v) for k, v in sov.items()}
            for canon in set(self.names.values()):
                members = self.NE_UNIONS.get(canon) or [lookup.get(self.NE_NAMES.get(canon, canon).casefold())]
                parts = [sov[lookup.get(m.casefold(), m)] for m in members if m and lookup.get(m.casefold(), m) in sov]
                if parts:
                    g = shapely.union_all(parts); shapely.prepare(g); self.geoms[canon] = g

    def canonical(self, label):
        return self.names.get((label or '').strip().casefold())

    def inside(self, label, lat, lon, margin=None):
        """True/False, or None when the label is unknown or names several countries."""
        key = (label or '').strip().casefold()
        boxes = self.boxes.get(key)
        if not boxes: return None
        g = self.geoms.get(self.names[key])
        if g is not None:
            tol = self.tolerance if margin is None else margin
            for w, s, e, n in self.EXTRA_BOXES.get(self.names[key], []):
                if s <= lat <= n and w <= lon <= e: return True
            ck = (self.names[key], round(lat, 4), round(lon, 4), tol)
            if ck not in self.cache:
                pt = shapely.Point(lon, lat)
                self.cache[ck] = bool(shapely.dwithin(g, pt, tol)) if tol > 0 else bool(shapely.intersects(g, pt))
            return self.cache[ck]
        m = self.margin if margin is None else margin
        for w, s, e, n in boxes:
            if not (s - m <= lat <= n + m): continue
            if (w <= e and w - m <= lon <= e + m) or (w > e and (lon >= w - m or lon <= e + m)): return True
        return False

    def repair(self, label, lat, lon):
        hits = [(name, f(lat, lon)) for name, f in self.CANDIDATES
                if abs(f(lat, lon)[0]) <= 90 and self.inside(label, *f(lat, lon), margin=self.REPAIR_TOLERANCE if self.geoms else 0)]
        return hits[0] if len(hits) == 1 else (None, None)


# ----------------------------------------------------------------------------------------------- the bake
class Baker:
    def __init__(self, cfg):
        self.cfg = cfg
        self.obs = {c: np.zeros((H, W), np.float32) for c in CODES}
        self.pot = {c: np.zeros((H, W), np.float32) for c in CODES}
        self.deepw = {c: np.zeros((H, W), np.float32) for c in CODES}
        self.deepd = {c: np.zeros((H, W), np.uint16) for c in CODES}
        self.points = defaultdict(lambda: ([], [], []))  # (code, plane, radius) -> lat, lon, peak
        self.deep_points = []  # (code, lat, lon, peak, radius, depth)
        self.frac = {}  # (code, plane, weight, group) -> float32 grid of summed covered fractions
        self.counts = defaultdict(Counter); self.excluded = Counter(); self.inputs = {}
        self.geometry_log = Counter(); self.coord_log = []; self.record_country = defaultdict(Counter)
        cc = cfg['coordinateCheck']
        self.country = CountryCheck(ROOT / cc['table'], cc['marginDegrees'], ROOT / cc['naturalEarth'], cc['polygonToleranceDegrees'])
        self.track(ROOT / cc['naturalEarth'])
        self.corrections = {}
        with open(ROOT / cc['corrections'], encoding='utf-8') as f:
            for r in csv.DictReader(f): self.corrections[(r['source_id'], str(r['source_feature_id']))] = r
        self.track(ROOT / cc['table']); self.track(ROOT / cc['corrections']); self.track(SETTINGS)
        self.applied_corrections = []
        self.drop = defaultdict(set)  # code -> source names dropped by a group's heatmap-overrides.json
        for f in sorted(GROUPS.glob('*/heatmap-overrides.json')):
            self.track(f)
            for code, names in (json.loads(f.read_text(encoding='utf-8')).get('drop') or {}).items():
                self.drop[code].update(names)

    def dropped(self, code, source):
        names = self.drop.get(code)
        if not names: return False
        is_mrds = source.endswith('-occurrences.csv.gz') or source == 'mrds-copper-tin-occurrences.csv'  # read_mrds passes file names
        if source in names or is_mrds and 'mrds' in names:
            self.excluded['group_override_drop ' + code + ' ' + source] += 1; return True
        return False

    def track(self, path):
        path = Path(os.path.abspath(path))  # not resolve(): worktrees link the raw inputs as junctions (tools/ore-data/link-raw.ps1)
        if str(path) not in self.inputs:
            self.inputs[str(path)] = {'file': str(path.relative_to(ROOT)).replace('\\', '/'), 'sha256': sha_file(path)}

    # ---- record admission
    def admit_point(self, source, sid, fid, country, lat, lon, name=None):
        """Apply reviewed corrections and the country envelope check. Returns (lat, lon) or None."""
        key = (sid or '', str(fid))
        if key in self.corrections:
            r = self.corrections[key]
            self.applied_corrections.append({'source_id': sid, 'source_feature_id': fid, 'name': name, 'action': r['action']})
            if r['action'] == 'drop':
                self.excluded['reviewed_coordinate_drop'] += 1; return None
            lat, lon = float(r['latitude']), float(r['longitude'])
        if not (-90 <= lat <= 90 and -180 <= lon <= 180) or not (math.isfinite(lat) and math.isfinite(lon)):
            self.excluded['bad_coordinates'] += 1; return None
        ok = self.country.inside(country, lat, lon)
        if ok is False:
            how, fixed = self.country.repair(country, lat, lon)
            row = {'source': source, 'source_id': sid, 'source_feature_id': fid, 'name': name, 'country': country,
                   'latitude': lat, 'longitude': lon, 'action': 'repaired_' + how if how else 'dropped',
                   'repaired_latitude': fixed[0] if how else '', 'repaired_longitude': fixed[1] if how else ''}
            self.coord_log.append(row)
            if how:
                self.excluded['coordinate_repaired_' + how + ' (kept)'] += 1
                return fixed
            self.excluded['outside_reported_country'] += 1
            return None
        return lat, lon

    def add_point(self, code, lat, lon, peak, radius, source, depth=None, plane='obs', country=None):
        if code not in self.obs or peak <= 0: return
        if self.dropped(code, source): return
        self.record_country[code][self.country.canonical(country) or ('unlabelled' if not country else 'other label')] += 1
        if depth:
            self.deep_points.append((code, lat, lon, peak, radius, min(65535, max(1, round(depth)))))
        else:
            lst = self.points[(code, plane, radius)]
            lst[0].append(lat); lst[1].append(lon); lst[2].append(peak)
        self.counts[code][source] += 1

    def add_polygon(self, code, geom_json, weight, plane, source, group, depth=None, site_tier=None, site_radius=None,
                    lat_lon_check=None):
        if code not in self.obs: return
        if self.dropped(code, source): return
        try: geom = shape(geom_json)
        except Exception:
            self.excluded['unreadable_geometry'] += 1; return
        stamped = False
        for part, repaired, unwrapped in polygon_parts(geom):
            if repaired: self.geometry_log['repaired_parts'] += 1
            if unwrapped: self.geometry_log['antimeridian_parts'] += 1
            if site_tier is not None and part.area <= self.cfg['polygons']['siteMaxAreaDeg2']:
                c = part.representative_point()
                lon = ((c.x + 180) % 360) - 180
                self.add_point(code, c.y, lon, site_tier, site_radius, source, depth)
                self.counts[code][source] -= 1  # counted once per feature below
                stamped = True; continue
            ys, xs, fr = polygon_fractions(part, self.cfg['polygons']['samplesPerCellSide'])
            if not len(ys): continue
            if depth:
                fmin = self.cfg['polygons']['minimumFractionWeight']
                vals = (weight * (fmin + (1 - fmin) * fr)).astype(np.float32)
                ow, od = self.deepw[code][ys, xs], self.deepd[code][ys, xs]
                d = min(65535, max(1, round(depth)))
                nw, nd = combine_deep(ow, od, vals, np.uint16(d))
                self.deepw[code][ys, xs] = nw; self.deepd[code][ys, xs] = nd
            else:
                k = (code, plane, weight, group)
                if k not in self.frac: self.frac[k] = np.zeros((H, W), np.float32)
                np.add.at(self.frac[k], (ys, xs), fr.astype(np.float32))
            stamped = True
        if stamped: self.counts[code][source] += 1
        else: self.excluded['empty_polygon'] += 1

    # ---- source readers
    def mrds_peak(self, status, score):
        table = self.cfg['mrds']['peak']
        row = table.get(status) or table['Unknown']
        return row.get(score or '', row['']), self.cfg['mrds']['radiusKm'].get(status, self.cfg['mrds']['radiusKm']['Unknown'])

    def read_mrds(self):
        """All MRDS-derived records (remaining-ores per-commodity exports + copper-tin export)."""
        cfg = self.cfg; mcfg = cfg['mrds']
        recs = []  # code, dep_id, lat, lon, status, score, mineral_only, country, name, source file
        base = gzip.open(ATLAS / 'normalized/mrds-records.csv.gz', 'rt', encoding='utf-8')
        self.track(ATLAS / 'normalized/mrds-records.csv.gz')
        all_ids = {}; names_at = defaultdict(set)
        for r in csv.DictReader(base):
            k = (r['latitude'], r['longitude']); all_ids[r['source_feature_id']] = (float(r['latitude']), float(r['longitude']))
            names_at[k].add((r['name'] or '').strip().casefold())
        for c in COMMODITIES:
            path = ATLAS / 'normalized' / (c + '-occurrences.csv.gz'); self.track(path)
            with gzip.open(path, 'rt', encoding='utf-8') as f:
                for r in csv.DictReader(f):
                    labels = set(filter(None, (r['matching_source_commodity_labels'] or '').split(';')))
                    if c == 'S' and 'Sulfur' not in labels:
                        self.excluded['sulfur_pyrite_only_or_pyrite_feed'] += 1; continue
                    recs.append((c, r['source_feature_id'], r['latitude'], r['longitude'], r['source_status'] or 'Unknown',
                                 r['source_quality'], r['evidence_basis'] == 'explicit_ore_mineral_only', r['country'], r['name'], path.name))
        path = CUTIN / 'mrds-copper-tin-occurrences.csv'; self.track(path)
        with path.open(encoding='utf-8-sig') as f:
            for r in csv.DictReader(f):
                if r['heatmap_eligible'] != 'True': self.excluded['cu_sn_not_heatmap_eligible'] += 1; continue
                dep = r['source_feature_id']
                if dep not in all_ids:
                    all_ids[dep] = (float(r['latitude']), float(r['longitude']))
                    names_at[(r['latitude'], r['longitude'])].add((r['name'] or '').strip().casefold())
                recs.append((r['commodity'], dep, r['latitude'], r['longitude'], r['source_status'] or 'Unknown',
                             r['source_quality'], False, r['country'], r['name'], path.name))
        stacks = {k for k, v in names_at.items() if len(v) >= mcfg['placeholderStackMinNames']}
        # Survey effort: every unique MRDS record, by containing cell; commodity counts likewise.
        lat_all = np.array([v[0] for v in all_ids.values()]); lon_all = np.array([v[1] for v in all_ids.values()])
        effort = np.zeros((H, W), np.float64); np.add.at(effort, (cell_y(lat_all), cell_x(lon_all)), 1)
        per = defaultdict(lambda: np.zeros((H, W), np.float64)); seen = set()
        for c, dep, la, lo, *_ in recs:
            if (c, dep) in seen: continue
            seen.add((c, dep)); per[c][cell_y(float(la)), cell_x(float(lo))] += 1
        dcfg = cfg['surveyDensity']; size = dcfg['windowCells']; alpha = dcfg['pseudoCount']
        win = lambda a: uniform_filter(a, size=size, mode=['constant', 'wrap']) * size * size
        effort_w = win(effort); total = len(all_ids)
        mult = {}
        for c, grid in per.items():
            sg = grid.sum() / total
            local = (win(grid) + alpha * sg) / (effort_w + alpha)
            enrich = np.clip(local / sg, dcfg['enrichmentFloor'], 1.0)
            damp = np.clip((dcfg['effortReference'] / np.maximum(effort_w, 1)) ** dcfg['effortExponent'], dcfg['effortFloor'], 1.0)
            mult[c] = (enrich * damp).astype(np.float32)
        self.mrds_density = {'records': total, 'effort_window': effort_w, 'multipliers': mult}
        done = set()
        for c, dep, la, lo, status, score, mineral_only, country, name, src in recs:
            if (c, dep) in done: continue
            done.add((c, dep))
            if (la, lo) in stacks: self.excluded['mrds_placeholder_coordinate_stack'] += 1; continue
            pt = self.admit_point(src, 'usgs_mrds_digest', dep, country, float(la), float(lo), name)
            if pt is None: continue
            peak, radius = self.mrds_peak(status, score)
            if mineral_only: peak = min(peak, mcfg['mineralMentionOnlyCap'])
            cy, cx = cell_y(pt[0]), cell_x(pt[1])
            peak *= float(mult[c][cy, cx])
            # Sparse surveys: a lone record stands for a larger area, so widen its kernel (up to maxRadiusKm).
            widen = max(1.0, (dcfg['effortReference'] / max(1.0, effort_w[cy, cx])) ** dcfg['sparseRadiusExponent'])
            radius = min(dcfg['maxRadiusKm'], 5 * round(radius * widen / 5))
            self.add_point(c, pt[0], pt[1], round(peak, 4), radius, src, country=country)

    def tier(self, name):
        t = self.cfg['tiers'][name]; return t['peak'], t['radiusKm']

    def specialist_tier(self, p, code):
        sid = p.get('source_id'); st = (p.get('source_status') or '').casefold(); size = p.get('ore_tonnage_mt')
        if sid == 'usgs_major-deposits': return 'major'
        if sid in ('usgs_sedexmvt', 'usgs_laterite'): return 'deposit_large'
        if sid == 'usgs_vms': return 'deposit_large' if (size or 0) >= 1 else 'deposit'
        if sid == 'usgs_podchrome': return 'deposit' if (size or 0) >= .1 else 'prospect'
        if sid == 'usgs_phosphate':
            prod = (p.get('production_status') or '').casefold()
            return 'deposit_large' if re.search(r'produc|active|mine', prod) and 'not' not in prod else 'deposit'
        if sid == 'usgs_nicrpge':
            if re.search(r'produc|mine|worked|exploit', st) and 'not exploited' not in st and 'never' not in st: return 'deposit'
            if re.search(r'deposit|dep=sito|tonnage', st): return 'deposit'
            if re.search(r'prospect|exploration', st): return 'prospect'
            return 'occurrence'
        return 'deposit'

    def read_specialist(self):
        for c in CODES:
            path = ATLAS / 'normalized' / (c + '-specialist.geojson')
            if not path.exists(): continue
            self.track(path)
            for f in features(path):
                p = f['properties']; g = f.get('geometry')
                if not g: continue
                if c == 'S':
                    labels = set((p.get('source_commodity_labels') or '').split(';'))
                    if not (p.get('source_id') == 'usgs_major-deposits' and 'S' in labels):
                        self.excluded['sulfur_specialist_sulfide_or_pyrite'] += 1; continue
                lon, lat = g['coordinates'][:2]
                pt = self.admit_point(path.name, p.get('source_id'), p.get('source_feature_id'), p.get('country'), lat, lon, p.get('name'))
                if pt is None: continue
                peak, radius = self.tier(self.specialist_tier(p, c))
                self.add_point(c, pt[0], pt[1], peak, radius, path.name, country=p.get('country'))

    def read_copper_assessment(self):
        path = CUTIN / 'global-copper-assessment-occurrences.csv'; self.track(path)
        with path.open(encoding='utf-8') as f:
            for r in csv.DictReader(f):
                st = r['source_status'].casefold()
                tier = ('deposit_large' if st == 'deposit' else 'deposit' if ('historic mine' in st or 'estimated' in st)
                        else 'prospect' if st.startswith('prospect') else 'occurrence')
                pt = self.admit_point(path.name, r['source_id'], r['source_feature_id'], r['country'], float(r['latitude']), float(r['longitude']), r['name'])
                if pt is None: continue
                peak, radius = self.tier(tier)
                self.add_point('Cu', pt[0], pt[1], peak, radius, path.name, country=r['country'])

    def overlay_tier(self, entry, p, g):
        rule = entry.get('tierRule'); tier = entry.get('tier', 'deposit')
        st = str(p.get('source_status') or '').casefold()
        if rule == 'ga_aimr': tier = 'deposit_large' if 'producing' in st else 'deposit'
        elif rule == 'additional_evidence':
            if (p.get('source_id') or '').startswith('ga_aimr'): tier = 'deposit_large' if 'producing' in st else 'deposit'
            elif re.search(r'mine|produc', st): tier = 'deposit'
            elif re.search(r'deposit', st): tier = 'deposit'
            else: tier = 'occurrence'
        elif rule == 'coal_supplement': tier = 'deposit_large' if (p.get('source_id') or '').startswith('ga_aimr') else 'prospect'
        elif rule == 'fsu_coal': tier = 'deposit' if p.get('record_type') == 'coal_deposit_location' else 'prospect'
        elif rule == 'potash_deps':
            t = str((p.get('source_attributes') or {}).get('TYPE_REC') or '').casefold()
            tier = 'deposit_large' if re.search(r'mine|deposit|lease|grouped', t) else 'prospect'
        return tier

    def read_registry(self):
        reg_path = ATLAS / 'corrections/overlay-registry.json'; self.track(reg_path)
        self.registry = json.loads(reg_path.read_text())
        pc = self.cfg['polygons']
        for entry in self.registry:
            if str(entry.get('status', 'active')).startswith('hold'):
                self.excluded['registry_hold_' + entry['file'].split('/')[-1]] += 1; continue
            path = Path(os.path.abspath(ATLAS / entry.get('bakeFile', entry['file']))); self.track(path)
            if entry.get('sourceTable'): self.track(ATLAS / entry['sourceTable'])  # versioned table the bake file is built from
            role = entry.get('role', 'points'); src = path.name
            if role == 'fractionGrid':
                # Pre-rasterised 0.5-degree covered-area fraction (e.g. PEATMAP peatland share per cell).
                with np.load(path) as z: fr = z['fraction'].astype(np.float32)
                fmin = pc['minimumFractionWeight']; m = fr >= entry.get('minimumFraction', 0)
                m &= fr > 0
                for c in entry['commodities']:
                    vals = np.where(m, np.float32(entry['weight']) * (fmin + (1 - fmin) * np.minimum(1, fr)), 0).astype(np.float32)
                    target = self.obs[c] if entry.get('plane', 'obs') == 'obs' else self.pot[c]
                    np.maximum(target, vals, out=target)
                    self.counts[c][src + ' (cells)'] += int(m.sum())
                continue
            for f in features(path):
                p = dict(f.get('properties') or {}); g = f.get('geometry')
                if not g: self.excluded['no_geometry'] += 1; continue
                if str(p.get('runtime_use', '')).startswith('hold') or p.get('source_id') in ['gemdata_2005', 'diadata_2006', 'diadata_1a']:
                    self.excluded['research_only_rights_or_precision'] += 1; continue
                if p.get('gypsum_spawn_evidence_withheld') or p.get('mineral_identity') == 'anhydrite' or p.get('overlay_class') == 'context_only':
                    self.excluded['context_only_withheld'] += 1; continue
                cs = entry.get('commodities') or p.get('commodities') or [LABELS.get(s.strip().casefold()) for s in str(p.get('commodity_label') or '').split(';')]
                if isinstance(cs, str): cs = [cs]
                cs = [c for c in set(cs) if c in self.obs]
                if not cs: self.excluded['no_runtime_layer'] += 1; continue
                depth = parse_depth(p.get('depth_min_m'))
                if depth is None: depth = parse_depth(p.get('depth_m'))
                if depth is None and entry.get('tierRule') == 'potash_deps':
                    depth = parse_depth((p.get('source_attributes') or {}).get('K_DEPTH_M'))
                if g['type'] in ('Point', 'MultiPoint'):
                    coords = [g['coordinates']] if g['type'] == 'Point' else g['coordinates']
                    if entry.get('tierRule') == 'authored':
                        # Authored best-guess districts: peak from size x confidence, per-entry kernel radius.
                        ab = self.cfg['authoredBestGuess']
                        peak = min(ab['peak'][p['size']][p['confidence']], ab['maxPeak'])
                        radius = min(float(p['radius_km']), ab['maxRadiusKm'])
                    else:
                        tier = self.overlay_tier(entry, p, g); peak, radius = self.tier(tier)
                        radius = entry.get('radiusKm', radius)
                    for lon, lat in (c[:2] for c in coords):
                        pt = self.admit_point(src, p.get('source_id'), p.get('source_feature_id') or p.get('record_id'), p.get('country'), lat, lon, p.get('name'))
                        if pt is None: continue
                        for c in cs: self.add_point(c, pt[0], pt[1], peak, radius, src, depth, country=p.get('country'))
                elif g['type'] in ('Polygon', 'MultiPolygon'):
                    prole = entry.get('polygonRole', role)
                    group = (src, p.get(entry['sumFractionsBy'])) if entry.get('sumFractionsBy') else (src, p.get('source_id'))
                    for c in cs:
                        if prole == 'site':
                            peak, radius = self.tier(entry.get('tier', 'deposit')); radius = entry.get('radiusKm', radius)
                            self.add_polygon(c, g, pc['surveyedAreaWeight'], 'obs', src, group, depth, site_tier=peak, site_radius=radius)
                        elif prole == 'formation':
                            self.add_polygon(c, g, pc[entry.get('weightKey', 'formationWeight')], 'obs', src, group, depth)
                        elif prole == 'tract':
                            self.add_polygon(c, g, pc['tractWeight'], 'pot', src, group)
                            for pcode, w in (entry.get('priorStamp') or {}).items():
                                self.add_polygon(pcode, g, w, 'pot', src + ' (prior)', group)
                        elif prole == 'context':
                            self.add_polygon(c, g, pc['contextWeight'], 'pot', src, group)
                else:
                    self.excluded['unsupported_geometry'] += 1

    def read_groups(self):
        """Group evidence (decision 0065): tools/ore-data/<group>/evidence.geojson. Feature properties:
        code (layer code), plane ('obs' default, or 'pot'), tier (a settings tier name) or peak (0..1) with radiusKm
        (points), weight (0..1, polygons; default the settings' formationWeight), depthM (optional, metres to the top of
        buried evidence), source (short source name, required), id (record id), country (optional, enables the
        country envelope check), name (optional)."""
        pc = self.cfg['polygons']
        for path in sorted(GROUPS.glob('*/evidence.geojson')):
            self.track(path)
            for f in features(path):
                p = f.get('properties') or {}; g = f.get('geometry')
                code = p.get('code')
                if not g or code not in self.obs: self.excluded['group_evidence_unusable'] += 1; continue
                src = str(p.get('source') or path.parent.name)
                plane = p.get('plane', 'obs'); depth = parse_depth(p.get('depthM'))
                if g['type'] in ('Point', 'MultiPoint'):
                    if p.get('tier'): peak, radius = self.tier(p['tier'])
                    else: peak, radius = float(p['peak']), float(p['radiusKm'])
                    radius = float(p.get('radiusKm', radius))
                    assert 0 < peak <= 1 and 0 < radius <= 250, (path, p)
                    coords = [g['coordinates']] if g['type'] == 'Point' else g['coordinates']
                    for lon, lat in (c[:2] for c in coords):
                        pt = self.admit_point(src, src, p.get('id'), p.get('country'), lat, lon, p.get('name')) if p.get('country') else (lat, lon)
                        if pt is None: continue
                        self.add_point(code, pt[0], pt[1], peak, radius, src, depth, plane=plane, country=p.get('country'))
                elif g['type'] in ('Polygon', 'MultiPolygon'):
                    w = float(p.get('weight', pc['formationWeight']))
                    assert 0 < w <= 1, (path, p)
                    self.add_polygon(code, g, w, plane, src, (src, p.get('group')), depth)
                else:
                    self.excluded['unsupported_geometry'] += 1

    # ---- grids and priors
    def flush(self):
        """Stamp accumulated points and polygon fractions into the planes (maximum combination)."""
        floor = self.cfg['kernelFloor']
        for (code, plane, radius), (lat, lon, peak) in self.points.items():
            pi, ys, xs, close = kernel_offsets(np.array(lat), np.array(lon), radius)
            vals = (np.array(peak, np.float32)[pi] * (floor + (1 - floor) * close)).astype(np.float32)
            keep = vals >= self.cfg['minimumStampValue']  # very weak kernel tails are not evidence
            np.maximum.at(self.obs[code] if plane == 'obs' else self.pot[code], (ys[keep], xs[keep]), vals[keep])
        for code, lat, lon, peak, radius, d in self.deep_points:
            pi, ys, xs, close = kernel_offsets(np.array([lat]), np.array([lon]), radius)
            vals = (peak * (floor + (1 - floor) * close)).astype(np.float32)
            nw, nd = combine_deep(self.deepw[code][ys, xs], self.deepd[code][ys, xs], vals, np.uint16(d))
            # duplicates inside one kernel cannot occur (unique cells), so plain assignment is safe
            self.deepw[code][ys, xs] = nw; self.deepd[code][ys, xs] = nd
        fmin = self.cfg['polygons']['minimumFractionWeight']
        for (code, plane, weight, group), fr in self.frac.items():
            m = fr > 0
            vals = np.zeros((H, W), np.float32); vals[m] = weight * (fmin + (1 - fmin) * np.minimum(1, fr[m]))
            target = self.obs[code] if plane == 'obs' else self.pot[code]
            np.maximum(target, vals, out=target)

    def environment(self):
        geo = json.loads(GEOLOGY.read_text()); self.track(GEOLOGY)
        lith = np.frombuffer(base64.b64decode(geo['lithology']), dtype=np.uint8).reshape(H, W)
        sed = np.frombuffer(base64.b64decode(geo['sediment']), dtype=np.uint8).reshape(H, W)
        self.track(ENV)
        with np.load(ENV) as p:
            flags = p['flags']
            def block(a, f, func=np.mean):
                h, w = a.shape
                return func(a.reshape(h // f, f, w // f, f).transpose(0, 2, 1, 3).reshape(h // f, w // f, f * f), axis=2)
            land = block(((flags & 1) == 0).astype(np.float32), 15)
            ice = block(((flags & 8) > 0).astype(np.float32), 15)
            elev = block(np.where((flags & 1) == 0, p['elev'], 0).astype(np.float32), 15) / np.maximum(land, 1e-6)
            rough = block(p['rough'].astype(np.float32), 15, np.median)
            t = block(p['t_mean'], 2); rain = block(p['rain'], 2)
        return lith, sed, land, ice, elev, rough, t, rain

    def priors(self, env):
        lith, sed, land, ice, elev, rough, t, rain = env
        pr = self.cfg['priors']; land_ok = (land >= .5) & (ice < .5)
        self.prior_cells = Counter()
        def put(code, mask, w):
            mask = mask & land_ok
            self.prior_cells[code] += int(mask.sum())
            np.maximum(self.pot[code], np.where(mask, np.float32(w), np.float32(0)), out=self.pot[code])
        for rule in pr['lithology']:
            m = np.isin(lith, rule['glim'])
            for code, w in rule['codes'].items(): put(code, m, w)
        def near(codes):
            return maximum_filter(np.isin(lith, codes).astype(np.uint8), size=3, mode=['nearest', 'wrap']) > 0
        rule = pr['metamorphic']
        for code, w in rule['codes'].items(): put(code, np.isin(lith, rule['glim']), w)
        for key in ['graniteAureole', 'crystallineGold']:
            rule = pr[key]; m = np.isin(lith, rule['glimHost']) & near(rule['nearGlim'])
            for code, w in rule['codes'].items(): put(code, m, w)
        b = pr['bauxite']
        put(b['code'], np.isin(lith, b['glim']) & (t >= b['minTempC']) & (rain >= b['minRain']), b['weight'])
        pe = pr['peat']
        def ramp(v, lo, hi): x = np.clip((v - lo) / (hi - lo), 0, 1); return x * x * (3 - 2 * x)
        bo = pe['boreal']; tr = pe['tropical']
        # The VS rainfall field shows row artefacts of the coarse reanalysis grid; smooth over 2.5 degrees first.
        rain = uniform_filter(rain.astype(np.float64), size=pe['rainSmoothingCells'], mode=['nearest', 'wrap'])
        boreal = ((t >= bo['minTempC']) & (t <= bo['maxTempC']) & (elev <= bo['maxElevM'])) * ramp(rain, bo['rainLow'], bo['rainHigh']) * (1 - ramp(rough, bo['roughLow'], bo['roughHigh']))
        tropic = ((t >= tr['minTempC']) & (elev <= tr['maxElevM'])) * ramp(rain, tr['rainLow'], tr['rainHigh']) * (1 - ramp(rough, tr['roughLow'], tr['roughHigh']))
        peat = (pe['weight'] * np.maximum(boreal, tropic)).astype(np.float32)
        peat[peat < .02] = 0
        peat[~land_ok] = 0; peat[np.isin(lith, [11, 16])] = 0
        self.prior_cells['peat'] += int((peat > 0).sum())
        np.maximum(self.pot['peat'], peat, out=self.pot['peat'])
        ob = self.cfg['observedLayers']
        gum_peat = np.isin(sed, ob['gumPeatClasses'])
        np.maximum(self.obs['peat'], np.where(gum_peat, np.float32(ob['gumPeatWeight']), 0), out=self.obs['peat'])
        self.counts['peat']['GUM organic sediment classes (cells)'] += int(gum_peat.sum())
        gum_clay = np.isin(sed, ob['gumClayClasses'])
        np.maximum(self.pot['clay'], np.where(gum_clay, np.float32(ob['gumClayWeight']), 0), out=self.pot['clay'])
        self.counts['clay']['GUM fine-sediment classes (cells, potential plane)'] += int(gum_clay.sum())

    def halite_from_potash(self):
        """Sylvite is a child deposit of halite in VS: mirror every K weight into NaCl (never lowers NaCl)."""
        np.maximum(self.obs['NaCl'], self.obs['K'], out=self.obs['NaCl'])
        np.maximum(self.pot['NaCl'], self.pot['K'], out=self.pot['NaCl'])
        nw, nd = combine_deep(self.deepw['NaCl'], self.deepd['NaCl'], self.deepw['K'], self.deepd['K'])
        self.deepw['NaCl'], self.deepd['NaCl'] = nw.astype(np.float32), nd.astype(np.uint16)
        self.counts['NaCl']['mirrored from K layer'] += 1


def quantize(a):
    return np.clip(np.rint(a * 255), 0, 255).astype(np.uint8)


def main():
    cfg = json.loads(SETTINGS.read_text())
    assert cfg['version'] == 2 and cfg['gridDegrees'] == .5
    for t in cfg['tiers'].values(): assert 0 < t['peak'] <= 1 and 0 < t['radiusKm'] <= 250
    b = Baker(cfg)
    b.read_mrds(); print('mrds read', flush=True)
    b.read_specialist(); print('specialist read', flush=True)
    b.read_copper_assessment()
    b.read_registry(); print('registry read', flush=True)
    b.read_groups(); print('group evidence read', flush=True)
    b.flush(); print('stamped', flush=True)
    env = b.environment(); b.priors(env); b.halite_from_potash()
    binding = defaultdict(set)
    with (ATLAS / 'game-mineral-scope.csv').open(encoding='utf8') as f:
        for r in csv.DictReader(f):
            c = r['commodity_layer']; d = r['deposit_code']
            if c in b.obs and d not in ['bogironlarge', 'bogironsmall']: binding[c].add(d)
    binding['Al'].add('earth-bauxite'); binding['gypsum'].add('earth-gypsum'); binding['peat'].add('peat'); binding['clay'].update(['clay', 'redclay'])
    b.track(ATLAS / 'game-mineral-scope.csv')
    layers = []; summaries = []
    for c in CODES:
        planes = np.stack([quantize(b.obs[c]), quantize(b.pot[c]), quantize(b.deepw[c])])
        depth = np.where(planes[2] > 0, b.deepd[c], 0).astype('<u2')
        payload = planes.tobytes() + depth.tobytes()
        assert len(payload) == N * 5
        layers.append({'code': c, 'label': COMMODITIES[c][0] if c in COMMODITIES else EXTRA_LABELS[c],
                       'deposits': sorted(binding[c]), 'data': base64.b64encode(gzip.compress(payload, mtime=0)).decode()})
        summaries.append({'code': c, 'nonzeroObservedCells': int(np.count_nonzero(planes[0])), 'potentialCells': int(np.count_nonzero(planes[1])),
                          'deepCells': int(np.count_nonzero(planes[2])), 'priorCells': b.prior_cells.get(c, 0),
                          'pointRecords': sum(b.record_country[c].values()),
                          'pointRecordUsShare': round(b.record_country[c]['United States'] / max(1, sum(b.record_country[c].values())), 3),
                          'acceptedSourceAssociations': {k: v for k, v in b.counts[c].items() if v},
                          'generation_model': 'Authored suitability; not measured abundance/probability'})
    out = {'version': 1, 'datasetId': cfg['datasetId'], 'width': W, 'height': H,
           'model': 'evidence-tiers-v2', 'planes': ['observed', 'potential', 'deep', 'deepMinimumMetres:uint16le'],
           'provenance': os.path.relpath(REPORT / 'bake-manifest.json', ROOT).replace('\\', '/'), 'layers': layers}
    OUT.parent.mkdir(parents=True, exist_ok=True); REPORT.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(out, separators=(',', ':')) + '\n', encoding='utf8')
    b.track(Path(__file__))
    with (REPORT / 'coordinate-check-log.csv').open('w', encoding='utf-8', newline='') as f:
        wr = csv.DictWriter(f, fieldnames=['source', 'source_id', 'source_feature_id', 'name', 'country', 'latitude', 'longitude',
                                           'action', 'repaired_latitude', 'repaired_longitude'])
        wr.writeheader(); wr.writerows(b.coord_log)
    manifest = {'settings': cfg, 'runtimeSha256': hashlib.sha256(OUT.read_bytes()).hexdigest(),
                'inputs': sorted(b.inputs.values(), key=lambda x: x['file']), 'excluded': dict(b.excluded),
                'geometryRepairs': dict(b.geometry_log), 'coordinateCorrectionsApplied': b.applied_corrections,
                'outsideCountryDropped': sum(r['action'] == 'dropped' for r in b.coord_log),
                'outsideCountryRepaired': sum(r['action'] != 'dropped' for r in b.coord_log), 'outsideCountryLog': os.path.relpath(REPORT / 'coordinate-check-log.csv', ROOT).replace('\\', '/'),
                'mrdsSurveyRecords': b.mrds_density['records'],
                'registry': b.registry, 'resources': summaries,
                'caveats': ['0.5 degree cells and tiered point kernels (25-75 km) are gameplay inference, not deposit footprints.',
                            'Tiers, density normalisation and priors reduce but cannot remove geographic survey-coverage bias; the main point source (USGS MRDS) remains US-heavy.',
                            'Potential-plane priors from 0.5-degree dominant GLiM lithology and climate are low-weight geological permissiveness, not observed evidence.',
                            'Coal and peat remain regional where no global inventory is on disk (see docs/remaining-ore-evidence.md for the recommended datasets).',
                            'Deep evidence boosts only at/under its reported depth; depths are used only when a source states them (coal polygons, potash sites with K_DEPTH_M, reviewed gypsum). Assessment-tract depth ranges are not deposit tops and are not used.',
                            'V1 file format keeps the strongest deep weight paired with its own burial depth (shallower wins ties); weaker shallow records can be omitted, never used to expose stronger deep evidence early.']}
    (REPORT / 'bake-manifest.json').write_text(json.dumps(manifest, indent=2, ensure_ascii=False), encoding='utf8')
    print(json.dumps({'layers': len(layers), 'runtimeBytes': OUT.stat().st_size, 'exclusions': dict(b.excluded),
                      'geometry': dict(b.geometry_log), 'outsideCountry': len(b.coord_log), 'sources': len(b.inputs)}), flush=True)


if __name__ == '__main__':
    main()
