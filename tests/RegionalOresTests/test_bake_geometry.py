"""Unit checks for the v2 bake geometry helpers (no data files needed except the country table).

Run: .venv\\Scripts\\python.exe tests/RegionalOresTests/test_bake_geometry.py
"""
from pathlib import Path
import sys
import numpy as np
from shapely.geometry import Polygon, MultiPolygon, mapping

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
import bake_ore_heatmaps as B  # noqa: E402

# floor, not int(): a point at -179.9 W must land in column 0 and -0.1 in column 359 (v1 truncated toward zero).
assert int(B.cell_x(-179.9)) == 0 and int(B.cell_x(-0.1)) == 359 and int(B.cell_x(0.0)) == 360
assert int(B.cell_x(179.99)) == 719 and int(B.cell_x(180.0)) == 0
assert int(B.cell_y(89.99)) == 0 and int(B.cell_y(-89.99)) == 359 and int(B.cell_y(0.1)) == 179 and int(B.cell_y(-0.1)) == 180

# Kernel: the containing cell always gets closeness 1; a kernel near the antimeridian wraps.
pi, ys, xs, close = B.kernel_offsets(np.array([10.1]), np.array([179.9]), 60)
home = (ys == B.cell_y(10.1)) & (xs == B.cell_x(179.9))
assert close[home].max() == 1 and (xs == 0).any(), 'kernel must wrap across 180 degrees'

# Invalid bow-tie polygon is repaired, not dropped.
bowtie = Polygon([(10, 10), (12, 12), (12, 10), (10, 12), (10, 10)])
assert not bowtie.is_valid
parts = list(B.polygon_parts(bowtie))
assert parts and all(p.is_valid for p, _, _ in parts) and parts[0][1] is True
assert abs(sum(p.area for p, _, _ in parts) - 2.0) < 1e-9

# Antimeridian ring drawn the long way (vertices at +179 and -179) is unwrapped to a 2-degree-wide part.
ring = Polygon([(179, 50), (-179, 50), (-179, 52), (179, 52), (179, 50)])
(part, repaired, unwrapped), = list(B.polygon_parts(ring))
assert unwrapped and abs(part.bounds[2] - part.bounds[0] - 2) < 1e-9
ys, xs, fr = B.polygon_fractions(part)
assert set(xs.tolist()) == {718, 719, 0, 1}, sorted(set(xs.tolist()))
assert np.allclose(fr, 1.0)

# A MultiPolygon split at the dateline keeps both halves.
mp = MultiPolygon([Polygon([(178, 0), (180, 0), (180, 1), (178, 1)]), Polygon([(-180, 0), (-178, 0), (-178, 1), (-180, 1)])])
assert len(list(B.polygon_parts(mp))) == 2

# Area fractions: a polygon covering the western half of one cell -> 0.5 there; exact and sampled paths agree.
half = Polygon([(10.0, 10.0), (10.25, 10.0), (10.25, 10.5), (10.0, 10.5)])
ys, xs, fr = B.polygon_fractions(half)
assert len(fr) == 1 and abs(fr[0] - .5) < 1e-9
big = Polygon([(0, 0), (5.25, 0), (5.25, 5), (0, 5)])
ys, xs, fr = B.polygon_fractions(big)
assert abs(fr.sum() - 5.25 * 5 / .25) < 1e-6 and abs(fr.min() - .5) < 1e-9

# Depth strings: shallowest reported number.
assert B.parse_depth('350-620; 370-700') == 350 and B.parse_depth('>1200') == 1200 and B.parse_depth(None) is None

# Country envelopes: overseas boxes, antimeridian boxes, repairs of sign-dropped/swapped coordinates.
cc = B.CountryCheck(ROOT / 'docs/research/ore-heatmaps-v1/country-bboxes.csv', 1.5)
assert cc.inside('France', 4.6, -52.2) and cc.inside('Denmark', 65.2, -49.2)         # French Guiana, Greenland
assert cc.inside('Russia', 66.0, -172.0) and cc.inside('United States', 52.0, 178.0)   # Chukotka, Aleutians
assert cc.inside('Congo (Kinshasa)', -26.0, 27.5) is False                            # Kivu record in South Africa
assert cc.inside('Atlantis', 0, 0) is None
assert cc.repair('Peru', 9.55564, 77.1087) == ('flip_both', (-9.55564, -77.1087))     # Antamina, signs dropped
assert cc.repair('Ukraine', 30.56, 48.16) == ('swap', (48.16, 30.56))                 # lat/lon swapped
assert cc.repair('Tunisia', 26.58, 10.46) == (None, None)                             # no unique repair -> drop
# Natural Earth polygon mode (used by the bake): de-facto/de-jure label unions and overseas territories.
ne = ROOT / 'docs/research/remaining-ores-2026-09-29/raw/naturalearth/ne_10m_admin_0_countries.zip'
if ne.exists():
    pc = B.CountryCheck(ROOT / 'docs/research/ore-heatmaps-v1/country-bboxes.csv', 1.5, ne, .5)
    assert pc.inside('Western Sahara', 26.35, -12.85) and pc.inside('Ukraine', 45.33, 36.45)   # Bou Craa, Kerch
    assert pc.inside('France', 4.6, -52.2) and pc.inside('United States', 18.2, -66.5)        # French Guiana, Puerto Rico
    assert pc.inside('Italy', 63.0, 10.0) is False                                            # Norwegian mine labelled Italy
    assert pc.inside('Congo', -4.55, 12.09) is None                                           # ambiguous label: unchecked
    assert pc.repair('France', 43.4167, -6.7667) == ('flip_lon', (43.4167, 6.7667))           # Fontsante fluorite, Var
else:
    print('note: Natural Earth zip absent; polygon mode not tested')
print('PASS: floor indexing, kernel wrap, polygon repair/split/unwrap, area fractions, depth parsing, country checks')
