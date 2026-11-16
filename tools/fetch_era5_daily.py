"""
Fetches ERA5 post-processed daily statistics (Copernicus CDS, derived-era5-single-levels-daily-statistics): the daily
mean of 2 m temperature, whole globe at 0.25 deg, one NetCDF per year, for the day-to-day spread of the monthly
temperature layer (decision 0086, tools/bake_seasons.py spread). Needs the cdsapi package and a CDS key in
%USERPROFILE%\\.cdsapirc (the account holder's; this script never reads or prints it).

    .venv/Scripts/python tools/fetch_era5_daily.py <first year> [last year] [folder] [--workers N]
Files: <folder>/era5-t2m-daily-mean-<year>.nc. A year already there is skipped. With --workers N, N requests run at once
(the CDS queue serves a user's requests side by side up to its own limit): each worker claims a year with a lock file
(<file>.lock, created exclusively), so no year is fetched twice, also by a second run started beside this one. A lock
left by a stopped run is cleared by deleting it.
"""
import os, sys, threading, time

import cdsapi

REQUEST = {
    "product_type": "reanalysis",
    "variable": ["2m_temperature"],
    "month": [f"{m:02d}" for m in range(1, 13)],
    "day": [f"{d:02d}" for d in range(1, 32)],
    "daily_statistic": "daily_mean",
    "time_zone": "utc+00:00",
    "frequency": "1_hourly",
}


def claim(target):
    try:
        fd = os.open(target + ".lock", os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        os.write(fd, str(os.getpid()).encode())
        os.close(fd)
        return True
    except FileExistsError:
        return False


def fetch(years, folder, lock):
    c = cdsapi.Client(quiet=True, progress=False)
    while True:
        with lock:
            year = target = None
            while years:
                y = years.pop(0)
                t = os.path.join(folder, f"era5-t2m-daily-mean-{y}.nc")
                if os.path.exists(t) and os.path.getsize(t) > 0:
                    print(f"{y}: already there ({os.path.getsize(t) / 1e9:.2f} GB)", flush=True)
                    continue
                if not claim(t):
                    print(f"{y}: being fetched by another worker or run (lock file)", flush=True)
                    continue
                year, target = y, t
                break
        if year is None:
            return
        t0 = time.time()
        try:
            c.retrieve("derived-era5-single-levels-daily-statistics", dict(REQUEST, year=str(year)), target + ".part")
            os.replace(target + ".part", target)
            print(f"{year}: {os.path.getsize(target) / 1e9:.2f} GB in {(time.time() - t0) / 60:.1f} min", flush=True)
        except Exception as e:
            print(f"{year}: FAILED after {(time.time() - t0) / 60:.1f} min: {type(e).__name__}: {str(e)[:200]}", flush=True)
        finally:
            try:
                os.remove(target + ".lock")
            except OSError:
                pass


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    workers = 1
    if "--workers" in sys.argv:
        workers = int(sys.argv[sys.argv.index("--workers") + 1])
        args = [a for a in args if a != str(workers)]
    first = int(args[0])
    last = int(args[1]) if len(args) > 1 else first
    folder = args[2] if len(args) > 2 else r"C:\VS mods\data\earth-raw\era5-daily-t2m"
    os.makedirs(folder, exist_ok=True)
    years = list(range(first, last + 1))
    lock = threading.Lock()
    threads = [threading.Thread(target=fetch, args=(years, folder, lock)) for _ in range(workers)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()


if __name__ == "__main__":
    main()
