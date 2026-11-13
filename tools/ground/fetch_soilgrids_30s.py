"""Fetch SoilGrids 2.0 WRB most probable group at full resolution and reduce it to 30" (1/120 degree) by the most common
class, in 10-degree tiles.

Usage: python -P tools/ground/fetch_soilgrids_30s.py <data dir> [workers]
Writes <data dir>/soilgrids-30s/wrb/<west>_<south>.npy (1200x1200 uint8, row 0 at the north edge; 255 = no soil mapped).

Why not the WCS's own scaling (fetch_soilgrids_2min.py): the server answers reduced requests from overviews built by
averaging, so class numbers are averaged (Podzols 23 and Cambisols 6 come back as Histosols 14; lake 0 and Cambisols 6 as
Calcisols 5), whatever INTERPOLATION asks for. Full-resolution requests (7.5", 4800x4800 per tile, about 3 MB compressed)
are the classes themselves.

The WCS writes 0 both for Acrisols and for no data (sea, lakes, towns, ice). A 0 counts as Acrisols only where the
Acrisols probability layer (averaged to 30", which is fine for a probability) is at least ACRISOL_PCT.
A 30" cell takes the most common class among its 16 full-resolution pixels when at least 4 of them are soil.
SoilGrids 2.0 (Poggio et al. 2021, ISRIC), CC BY 4.0. Be gentle with the server: a few workers at most.
"""
import io, os, sys, time, urllib.request
from concurrent.futures import ThreadPoolExecutor
import numpy as np, tifffile
sys.path.insert(0, os.path.dirname(__file__))
from fetch_soilgrids_2min import land_tiles

URL = ("https://maps.isric.org/mapserv?map=/map/wrb.map&SERVICE=WCS&VERSION=2.0.1&REQUEST=GetCoverage&COVERAGEID={cov}"
       "&FORMAT=image/tiff&SUBSET=long({w},{e})&SUBSET=lat({s},{n})"
       "&SUBSETTINGCRS=http://www.opengis.net/def/crs/EPSG/0/4326&OUTPUTCRS=http://www.opengis.net/def/crs/EPSG/0/4326{size}")
N30 = 1200                 # 30" cells per 10-degree side
ACRISOL_PCT = 5


def get(cov, w, s, size=""):
    url = URL.format(cov=cov, w=w, e=w + 10, s=s, n=s + 10, size=size)
    err = None
    for attempt in range(5):
        try:
            with urllib.request.urlopen(url, timeout=300) as r:
                data = r.read()
            if data[:2] in (b"II", b"MM"):
                return tifffile.imread(io.BytesIO(data))
            err = data[:300]
        except Exception as e:  # network or server error: wait and retry
            err = repr(e)
        time.sleep(15 * (attempt + 1))
    raise RuntimeError(f"{cov} {w},{s}: {err}")


def mode16(a, valid):
    """Most common valid class in each 4x4 block; 255 where fewer than 4 are valid."""
    ny, nx = a.shape[0] // 4, a.shape[1] // 4
    b = np.where(valid, a, 255).astype(np.uint8)[:ny * 4, :nx * 4].reshape(ny, 4, nx, 4).transpose(0, 2, 1, 3).reshape(ny, nx, 16)
    counts = np.zeros((ny, nx, 30), np.uint8)
    for k in range(16):
        v = b[:, :, k]
        ok = v < 30
        yy, xx = np.nonzero(ok)
        np.add.at(counts, (yy, xx, v[ok]), 1)
    total = counts.sum(2)
    out = counts.argmax(2).astype(np.uint8)
    out[total < 4] = 255
    return out


def job(args):
    w, s, path = args
    a = get("MostProbable", w, s)
    edge = s < -50 or s >= 80             # SoilGrids covers 56 S to 84 N: the rest of these tiles is unmapped
    if edge and a.shape[1] == 4800 and a.shape[0] < 4800:
        pad = np.zeros((4800 - a.shape[0], 4800), a.dtype)
        a = np.vstack([a, pad]) if s < 0 else np.vstack([pad, a])
    if a.shape != (4800, 4800):
        raise RuntimeError(f"{w},{s}: shape {a.shape}")
    if edge:                              # no Acrisols beyond 50 degrees
        acr_full = np.zeros(a.shape, bool)
    else:
        acr = get("Acrisols", w, s, f"&SCALESIZE=long({N30}),lat({N30})").astype(np.int32)
        acr_full = np.repeat(np.repeat(acr >= ACRISOL_PCT, 4, 0), 4, 1)
    valid = (a > 0) & (a < 30) | ((a == 0) & acr_full)
    out = mode16(a, valid)
    np.save(path + ".part.npy", out)
    os.replace(path + ".part.npy", path)
    return w, s, int((out < 30).sum())


def main(data, workers=3):
    d = os.path.join(data, "soilgrids-30s", "wrb")
    os.makedirs(d, exist_ok=True)
    jobs = [(w, s, os.path.join(d, f"{w}_{s}.npy")) for w, s in land_tiles()]
    jobs = [j for j in jobs if not os.path.exists(j[2])]
    print(f"{len(jobs)} tiles to fetch", flush=True)
    done = 0
    with ThreadPoolExecutor(workers) as ex:
        futures = [ex.submit(job, j) for j in jobs]
        for f in futures:
            done += 1
            try:
                w, s, n = f.result()
                if done % 20 == 0:
                    print(f"{done}/{len(jobs)}", flush=True)
            except Exception as e:
                print("FAILED", e, flush=True)
    print("finished", flush=True)


if __name__ == "__main__":
    main(sys.argv[1], int(sys.argv[2]) if len(sys.argv) > 2 else 3)
