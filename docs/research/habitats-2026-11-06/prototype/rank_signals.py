"""Which terrain signal ranks wetland best? AUC and equal-area overlap against OSM wetlands (Latvia, land only).

usage: python -I rank_signals.py <arrays.npz from habitat_proto DUMP>
"""
import sys, numpy as np
from scipy import ndimage
from scipy.stats import rankdata

a = np.load(sys.argv[1])
land, osm = a['land'], a['osm']
hand, mound1, twi, slope, fill, perm = a['hand'], a['mound'], a['twi'], a['slope'], a['fill'], a['perm']
xdown, xup, acc = a['xdown'], a['xup'], a['acc']
y = (osm > 0) & land
# flatness of the surroundings: mean slope within ~1 km (bogs need wide flats)
slope_1km = ndimage.uniform_filter(np.minimum(slope, 0.05), size=7)
rng = np.random.default_rng(1)
idx = np.flatnonzero(land)
samp = rng.choice(idx, size=min(400000, idx.size), replace=False)
share = y.ravel()[idx].mean()

def evaluate(name, score):
    s = score.ravel()[samp]; t = y.ravel()[samp]
    r = rankdata(s)
    n1 = float(t.sum()); n0 = float(t.size) - n1
    auc = (r[t].sum() - n1 * (n1 + 1) / 2) / (n1 * n0)
    # equal area: the top cells covering the OSM share of the land
    thr = np.quantile(s, 1 - share)
    pred = s >= thr
    prec = (pred & t).sum() / max(pred.sum(), 1)
    # tolerant overlap (~500 m) on the full grid
    full = (score >= thr) & land
    near_osm = ndimage.binary_dilation(y, iterations=3)
    near_pred = ndimage.binary_dilation(full, iterations=3)
    p500 = (full & near_osm).sum() / max(full.sum(), 1)
    r500 = (y & near_pred).sum() / max(y.sum(), 1)
    print(f'{name:44s} AUC {auc:.3f}   equal-area precision=recall {prec:.3f}   within 500 m: P {p500:.3f} R {r500:.3f}')
    return auc

print(f'OSM wetland share of Latvian land: {share * 100:.1f} %, sample {samp.size}')
evaluate('height above drainage (low = wet)', -hand)
evaluate('topographic wetness index', twi)
evaluate('flat surroundings (1 km mean slope)', -slope_1km)
evaluate('closed-hollow depth', fill)
for kmul in (1, 10, 100, 1000):
    m = mound1 / np.sqrt(kmul)
    evaluate(f'mound - HAND, K x{kmul}', m - hand)
# mound normalised by height: fraction of the hillslope the mound fills
evaluate('mound / HAND', mound1 / np.maximum(hand, 0.5))
evaluate('distance from drainage x / hillslope L', xdown / np.maximum(xdown + xup, 1))
evaluate('mound - HAND, K x100, + flatness', (mound1 / 10 - hand) - 200 * slope_1km)
evaluate('-HAND + flatness', -hand - 300 * slope_1km)
