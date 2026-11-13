"""Download the Copernicus DEM GLO-90 water body masks (WBM): for every height tile in rawdata/copernicus-glo90, its
companion <tile>_WBM.tif from the tile's AUXFILES folder in the public AWS bucket copernicus-dem-90m (no account). The
mask is on the height tile's own 3" grid, one byte per pixel: 0 land, 1 ocean, 2 lake, 3 river. Copernicus edited the
heights over water with it, so it agrees with GLO-90's heights; tools/water_atlas.py uses it as the reference for where
water is. Resumable: files already there with the right size are skipped. Each download goes to <name>.part, is checked
against the bucket's ETag (the MD5 of single-part uploads) and renamed. Progress goes to <out>/fetch.log.

Copernicus DEM: (c) DLR e.V. 2010-2014 and (c) Airbus Defence and Space GmbH 2014-2018 provided under COPERNICUS by
the European Union and ESA; all rights reserved. Free licence: rawdata/copernicus-glo90-eula.

usage: fetch_glo90_wbm.py [dem folder] [out folder] [--workers 8]
Standard library only."""
import argparse, hashlib, os, sys, threading, time, urllib.error, urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUCKET = "https://copernicus-dem-90m.s3.amazonaws.com/"
UA = "EarthVintageStoryMod-data-fetch/1.0"


def head(url):
    req = urllib.request.Request(url, method="HEAD", headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=60) as r:
        return int(r.headers["Content-Length"]), r.headers.get("ETag", "").strip('"')


def fetch(dem, out):
    wbm = dem[:-len("_DEM")] + "_WBM"
    url = f"{BUCKET}{dem}/AUXFILES/{wbm}.tif"
    dst = os.path.join(out, wbm + ".tif")
    for attempt in range(1, 6):
        try:
            if os.path.exists(dst):
                return wbm, "here", 0  # renamed only after its MD5 matched
            size, etag = head(url)
            part = dst + ".part"
            md5 = hashlib.md5()
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=120) as r, open(part, "wb") as f:
                for b in iter(lambda: r.read(1 << 16), b""):
                    f.write(b)
                    md5.update(b)
            if os.path.getsize(part) != size or ("-" not in etag and md5.hexdigest() != etag):
                raise IOError("size or MD5 mismatch")
            os.replace(part, dst)
            return wbm, "got", size
        except urllib.error.HTTPError as e:
            if e.code in (403, 404):
                return wbm, f"missing ({e.code})", 0
            err = e
        except Exception as e:  # network hiccups: retry
            err = e
        time.sleep(2 * attempt)
    return wbm, f"failed: {err}", 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dem", nargs="?", default=os.path.join(ROOT, "rawdata", "copernicus-glo90"))
    ap.add_argument("out", nargs="?", default=os.path.join(ROOT, "rawdata", "copernicus-glo90-wbm"))
    ap.add_argument("--workers", type=int, default=8)
    a = ap.parse_args()
    names = sorted(f[:-4] for f in os.listdir(a.dem) if f.endswith("_DEM.tif"))
    os.makedirs(a.out, exist_ok=True)
    lock = threading.Lock()
    done, total, bad = 0, 0, []
    t0 = time.time()
    with open(os.path.join(a.out, "fetch.log"), "a", encoding="utf8") as log, ThreadPoolExecutor(a.workers) as ex:
        log.write(f"--- {time.strftime('%Y-%m-%d %H:%M:%S')}: {len(names)} tiles\n")
        for fut in as_completed([ex.submit(fetch, n, a.out) for n in names]):
            name, how, size = fut.result()
            with lock:
                done += 1
                total += size
                if how not in ("got", "here"):
                    bad.append((name, how))
                    log.write(f"{name}: {how}\n")
                if done % 500 == 0 or done == len(names):
                    msg = f"{done}/{len(names)} tiles, {total / 1e6:.0f} MB this run, {len(bad)} not got, {time.time() - t0:.0f}s"
                    print(msg, flush=True)
                    log.write(msg + "\n")
                    log.flush()
    if bad:
        print(f"{len(bad)} tiles not got (see fetch.log)", file=sys.stderr)


if __name__ == "__main__":
    main()
