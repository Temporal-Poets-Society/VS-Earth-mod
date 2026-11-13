"""
Earth decision 0069 rule 8: earthdata/rivers-nonperennial-2.bin, which of Earth's river reaches GIRES v1.0 has as
non-perennial, for the arid rule (AridHydrology) to dry only those.

GIRES v1.0 (Messager, M.L., Lehner, B., et al. 2021: Global prevalence of non-perennial rivers and streams. Nature 594,
391-397; data doi:10.6084/m9.figshare.14633022; CC BY-SA 4.0 per its technical documentation) predicts for every
RiverATLAS reach (the HydroRIVERS reaches Earth's network is made of) whether its flow stops at least 1 and at least 30
days a year. tools/extract_gires.py reduces it to an .npz (id, midpoint, discharge, classes).

This bake matches each reach of Earth's course list (courses rules 4: rivers-glo90.bin with the snap-2 and arms-2 files,
course key 9693b65a3a265a62) to the GIRES reach nearest in discharge (within MATCH_SHARE) and place (within MATCH_KM of
its middle vertex), among all GIRES reaches of at least MIN_Q m3/s whatever their class (matching only the non-perennial
ones took a year-round reach for an often-dry neighbour of the same flow 4.9 km away). The side arms (the list's first
reaches, rivers-glo90-arms-2.bin's count) take the class of the first reach downstream that is not an arm. The file:

  magic "EMNP", int32 version 2, uint64 the course list's key (DamRemovalData.KeyOf), int32 reaches, then brotli of one
  byte per reach: 0 flows all year (or no match), 1 its flow stops 1-29 days a year, 2 30 days or more. Little-endian.

Version 1 (all GIRES reaches by position, matched when a world starts) was 9.2 MB; this one is about 20 kB, in the mod.

    python -I tools/bake_nonperennial.py rawdata/gires/extract/gires-reaches.npz COURSES.npz earthdata/rivers-nonperennial-2.bin
COURSES.npz: tools/bake_river_flow.py's courses.npz (the course list's stations, reaches, discharges, key and arms).
"""
import site, struct, sys
sys.path.append(site.getusersitepackages())
import brotli
import numpy as np
from scipy.spatial import cKDTree

MIN_Q = 15.0
MATCH_SHARE, MATCH_KM = 0.02, 8.0
VERSION = 2


def xyz(lat, lon):
    la, lo = np.radians(lat), np.radians(lon)
    return np.c_[np.cos(la) * np.cos(lo), np.cos(la) * np.sin(lo), np.sin(la)]


def main():
    g = np.load(sys.argv[1])
    c = np.load(sys.argv[2])
    keep = g["q"] >= MIN_Q
    glat, glon, gq = g["lat"][keep], g["lon"][keep], g["q"][keep].astype(np.float64)
    gcls = np.where(g["c30"][keep] == 1, 2, np.where(g["c1"][keep] == 1, 1, 0)).astype(np.uint8)
    tree = cKDTree(xyz(glat, glon))
    start, q, nxt = c["start"], c["q"].astype(np.float64), c["next"]
    n = len(q)
    arms = int(c["arms"][0])
    mid = start[:-1] + (start[1:] - start[:-1]) // 2
    pts = xyz(c["lat"][mid], c["lon"][mid])
    dist, idx = tree.query(pts, k=24, distance_upper_bound=MATCH_KM / 6371.0)
    cls = np.zeros(n, np.uint8)
    matched = 0
    for r in range(arms, n):
        best, score = -1, 1e9
        for d, k in zip(dist[r], idx[r]):
            if not np.isfinite(d):
                break
            share = abs(gq[k] - q[r]) / max(q[r], 1e-6)
            if share > MATCH_SHARE:
                continue
            s = share + d * 6371.0 / 50.0
            if s < score:
                score, best = s, k
        if best >= 0:
            cls[r] = gcls[best]
            matched += 1
    for r in range(min(arms, n)):
        x, hops = int(nxt[r]), 0
        while 0 <= x < arms and hops < arms:
            x, hops = int(nxt[x]), hops + 1
        cls[r] = cls[x] if arms <= x < n else 0
    key = int(c["key"][0])
    body = brotli.compress(cls.tobytes(), quality=11, lgwin=22)
    with open(sys.argv[3], "wb") as f:
        f.write(b"EMNP" + struct.pack("<iQi", VERSION, key, n) + body)
    print(f"{n} reaches ({arms} side arms), {matched} matched to GIRES; year-round {int((cls == 0).sum())}, "
          f"dry 1-29 days {int((cls == 1).sum())}, dry 30 days or more {int((cls == 2).sum())}; key {key:016x}; "
          f"{20 + len(body)} bytes")


if __name__ == "__main__":
    main()
