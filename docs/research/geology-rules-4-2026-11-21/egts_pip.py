"""Random cells of a baked tile against point-in-polygon at the exact cell centre."""
import os, sys, numpy as np
os.environ.setdefault("OGR_ORGANIZE_POLYGONS", "ONLY_CCW")
sys.path.insert(0, sys.argv[3]); import egts_read as E
import pyogrio.raw as pr, shapely
from pyproj import Transformer
t = E.read_set(sys.argv[1]); gdb = sys.argv[2]
fwd = Transformer.from_crs("EPSG:4326", "ESRI:54012", always_xy=True)
rng = np.random.default_rng(1)
for h, p in t:
    j = rng.integers(0, h["ny"], 1500); i = rng.integers(0, h["nx"], 1500)
    lat = h["north"] - (j + .5) * h["step"]; lon = h["west"] + (i + .5) * h["step"]
    x, y = fwd.transform(lon, lat)
    _, _, wkb, (lith,) = pr.read(gdb, layer="GLiM_export", columns=["Litho"], bbox=(x.min(), y.min(), x.max(), y.max()))
    g = shapely.from_wkb(wkb); tree = shapely.STRtree(g)
    pts = shapely.points(x, y); pi, gi = tree.query(pts, predicate="within")
    truth = np.array([None] * len(pts), object); truth[pi] = lith[gi]
    leg = h["fields"][0]["legend"]
    got = np.array([None if v == 65535 else leg[v] for v in p["litho"][j, i]], object)
    ok = (got == truth).sum(); print(h["name"], f"agree {ok}/{len(pts)}", [(a, b) for a, b in zip(truth, got) if a != b][:6])
