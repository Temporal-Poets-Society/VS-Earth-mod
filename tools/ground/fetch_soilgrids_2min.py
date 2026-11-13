"""Fetch SoilGrids 2.0 WRB group, clay and pH (15-30 cm) at 2' (1/30 degree) for the whole world, in 10-degree tiles.

Usage: python tools/ground/fetch_soilgrids_2min.py <out dir> [workers]
Writes <out dir>/soilgrids-2min/<field>/<west>_<south>.tif (300x300, EPSG:4326, the WCS's own resampling to 2').
Only tiles with land in GLiM's 0.5-degree grid are requested; tiles already on disk are skipped, so it can be rerun.
SoilGrids 2.0 (Poggio et al. 2021, ISRIC), CC BY 4.0. Be gentle with the server: a few workers at most.
"""
import os, sys, time, zipfile, urllib.request
from concurrent.futures import ThreadPoolExecutor
import numpy as np

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FIELDS = {
    "wrb": ("wrb", "MostProbable"),
    "clay": ("clay", "clay_15-30cm_mean"),
    "ph": ("phh2o", "phh2o_15-30cm_mean"),
}
URL = ("https://maps.isric.org/mapserv?map=/map/{map}.map&SERVICE=WCS&VERSION=2.0.1&REQUEST=GetCoverage&COVERAGEID={cov}"
       "&FORMAT=image/tiff&SUBSET=long({w},{e})&SUBSET=lat({s},{n})"
       "&SUBSETTINGCRS=http://www.opengis.net/def/crs/EPSG/0/4326&OUTPUTCRS=http://www.opengis.net/def/crs/EPSG/0/4326"
       "&SCALESIZE=long(300),lat(300)")


def land_tiles():
    src = os.path.join(REPO, "docs/research/regional-geology-2026-09-29/glim-download.zip")
    with zipfile.ZipFile(src) as z:
        glim = np.loadtxt(z.open("glim_wgs84_0point5deg.txt.asc"), skiprows=6).astype(int)
    out = []
    for s in range(-60, 90, 10):              # SoilGrids covers about 62 S to 87 N
        for w in range(-180, 180, 10):
            r0 = int((90 - (s + 10)) / 0.5); c0 = int((w + 180) / 0.5)
            block = glim[r0:r0 + 20, c0:c0 + 20]
            if ((block > 0) & (block != 16)).any():   # any land that is not ice
                out.append((w, s))
    return out


def fetch(job):
    field, w, s, path = job
    m, cov = FIELDS[field]
    url = URL.format(map=m, cov=cov, w=w, e=w + 10, s=s, n=s + 10)
    for attempt in range(4):
        try:
            with urllib.request.urlopen(url, timeout=180) as r:
                data = r.read()
            if data[:2] in (b"II", b"MM"):
                tmp = path + ".part"
                open(tmp, "wb").write(data)
                os.replace(tmp, path)
                return field, w, s, len(data), None
            err = data[:200]
        except Exception as e:  # network or server error: wait and retry
            err = repr(e)
        time.sleep(10 * (attempt + 1))
    return field, w, s, 0, err


def main(out, workers=3):
    tiles = land_tiles()
    jobs = []
    for field in FIELDS:
        d = os.path.join(out, "soilgrids-2min", field)
        os.makedirs(d, exist_ok=True)
        for w, s in tiles:
            p = os.path.join(d, f"{w}_{s}.tif")
            if not os.path.exists(p):
                jobs.append((field, w, s, p))
    print(f"{len(tiles)} land tiles, {len(jobs)} requests to make", flush=True)
    done = 0
    with ThreadPoolExecutor(workers) as ex:
        for field, w, s, n, err in ex.map(fetch, jobs):
            done += 1
            if err:
                print(f"FAILED {field} {w},{s}: {err}", flush=True)
            elif done % 50 == 0:
                print(f"{done}/{len(jobs)}", flush=True)
    print("finished", flush=True)


if __name__ == "__main__":
    main(sys.argv[1], int(sys.argv[2]) if len(sys.argv) > 2 else 3)
