"""Download every Copernicus DEM GLO-90 tile (the DEM GeoTIFFs only, about 71 GB in 26,475 files) from the public AWS
bucket copernicus-dem-90m (no account) into a folder, for decision 0051's offline work. Resumable: files already there
with the right size are skipped. Each download goes to <name>.tif.part, is checked against the bucket's ETag (the
file's MD5 for single-part uploads) and then renamed. Tiles found in --copy-from (the 100-tile sample) are copied, not
downloaded. Progress goes to <out>/fetch.log; a summary to <out>/fetch-summary.json.
usage: fetch_glo90.py <tiles.txt> <out folder> [--workers 8] [--copy-from <folder>]
Standard library only."""
import argparse, hashlib, json, os, shutil, sys, threading, time, urllib.error, urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

BUCKET = "https://copernicus-dem-90m.s3.amazonaws.com/"
UA = "EarthVintageStoryMod-data-fetch/1.0"


def head(url):
    req = urllib.request.Request(url, method="HEAD", headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=60) as r:
        return int(r.headers["Content-Length"]), r.headers.get("ETag", "").strip('"')


def fetch(name, out, copy_from, log):
    url = f"{BUCKET}{name}/{name}.tif"
    dst = os.path.join(out, name + ".tif")
    for attempt in range(1, 6):
        try:
            size, etag = head(url)
            if os.path.exists(dst) and os.path.getsize(dst) == size:
                return name, "here", 0
            src = os.path.join(copy_from, name + ".tif") if copy_from else None
            part = dst + ".part"
            md5 = hashlib.md5()
            if src and os.path.exists(src) and os.path.getsize(src) == size:
                shutil.copyfile(src, part)
                with open(part, "rb") as f:
                    for b in iter(lambda: f.read(1 << 20), b""):
                        md5.update(b)
                how, got = "copied", 0
            else:
                req = urllib.request.Request(url, headers={"User-Agent": UA})
                got = 0
                with urllib.request.urlopen(req, timeout=120) as r, open(part, "wb") as f:
                    for b in iter(lambda: r.read(1 << 20), b""):
                        f.write(b)
                        md5.update(b)
                        got += len(b)
                how = "downloaded"
            if os.path.getsize(part) != size:
                raise IOError(f"size {os.path.getsize(part)} != {size}")
            if "-" not in etag and etag and md5.hexdigest() != etag:
                raise IOError(f"md5 {md5.hexdigest()} != etag {etag}")
            os.replace(part, dst)
            return name, how, got
        except (urllib.error.URLError, IOError, OSError, TimeoutError) as e:
            if isinstance(e, urllib.error.HTTPError) and e.code == 404:
                return name, "missing", 0
            log(f"{name}: attempt {attempt} failed: {e}")
            time.sleep(min(60, 3 * attempt * attempt))
    return name, "failed", 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("tiles")
    ap.add_argument("out")
    ap.add_argument("--workers", type=int, default=8)
    ap.add_argument("--copy-from")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    names = [l.strip() for l in open(a.tiles, encoding="utf-8") if l.strip().startswith("Copernicus_DSM_COG_30_")]
    lock = threading.Lock()
    logf = open(os.path.join(a.out, "fetch.log"), "a", encoding="utf-8")

    def log(msg):
        with lock:
            logf.write(time.strftime("%Y-%m-%d %H:%M:%S ") + msg + "\n")
            logf.flush()

    log(f"start: {len(names)} tiles, {a.workers} workers, into {a.out}")
    counts = {"here": 0, "copied": 0, "downloaded": 0, "missing": 0, "failed": 0}
    failed = []
    total = 0
    t0 = time.time()
    with ThreadPoolExecutor(a.workers) as ex:
        futs = [ex.submit(fetch, n, a.out, a.copy_from, log) for n in names]
        for i, f in enumerate(as_completed(futs), 1):
            name, how, got = f.result()
            counts[how] += 1
            total += got
            if how in ("failed", "missing"):
                failed.append(name)
                log(f"{name}: {how}")
            if i % 200 == 0 or i == len(names):
                dt = time.time() - t0
                log(f"{i}/{len(names)} done {counts}; {total / 1e9:.2f} GB downloaded in {dt / 60:.1f} min ({total / 1e6 / max(dt, 1):.1f} MB/s)")
    summary = {"tiles": len(names), "counts": counts, "downloaded_bytes": total, "minutes": round((time.time() - t0) / 60, 1),
               "failed": failed, "source": BUCKET, "finished": time.strftime("%Y-%m-%d %H:%M:%S")}
    json.dump(summary, open(os.path.join(a.out, "fetch-summary.json"), "w"), indent=1)
    log("finished: " + json.dumps(counts))
    print(json.dumps(summary)[:2000])
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
