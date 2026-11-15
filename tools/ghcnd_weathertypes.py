"""Thunder, fog and hail days at real weather stations, to fit and check Earth's thunderstorms and fog (weather rules 7).

GHCN-Daily (NOAA NCEI; Menne et al. 2012, doi:10.1175/JTECH-D-11-00103.1, dataset doi:10.7289/V5D21VHZ) US first-order
stations report weather types per day: WT03 thunder, WT01 fog (ice fog, freezing fog), WT02 heavy fog (visibility a
quarter mile or less), WT05 hail. For each USW station file among the GHCN-Daily records tools/ghcnd_dryspells.py fetched,
per calendar month over 1991-2020: the share of days with each type, counting only the years in which the station reported
thunder on at least three days (a year without weather-type reports is not a year without weather).

Only these monthly shares are used (tools/fit_thunder.py --dry, WeatherTests fog <table>); the records are not shipped.

Usage: python -I tools/ghcnd_weathertypes.py [--data rawdata/ghcnd] [--out tests/out/weather/ghcnd-weathertypes.tsv]
"""
import argparse
import collections
import csv
import glob
import os

Y0, Y1 = 1991, 2020
TYPES = ("WT03", "WT01", "WT02", "WT05")
MONTH_DAYS = (31, 28.25, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31)


def flag(row, key):
    return (row.get(key) or "").strip() == "1"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="rawdata/ghcnd")
    ap.add_argument("--out", default="tests/out/weather/ghcnd-weathertypes.tsv")
    a = ap.parse_args()
    os.makedirs(os.path.dirname(a.out) or ".", exist_ok=True)
    with open(a.out, "w", newline="") as out:
        out.write("id\tname\tlat\tlon\telev\tmonth\tdays\tthunder\tfog\theavyfog\thail\n")
        for path in sorted(glob.glob(os.path.join(a.data, "access", "USW*.csv"))):
            with open(path, newline="") as f:
                rows = list(csv.DictReader(f))
            if not rows or "WT03" not in rows[0]:
                continue
            years = collections.Counter(int(r["DATE"][:4]) for r in rows if Y0 <= int(r["DATE"][:4]) <= Y1 and flag(r, "WT03"))
            good = {y for y, n in years.items() if n >= 3}
            if not good:
                continue
            counts = collections.defaultdict(lambda: [0] * (1 + len(TYPES)))
            for r in rows:
                if int(r["DATE"][:4]) not in good:
                    continue
                c = counts[int(r["DATE"][5:7])]
                c[0] += 1
                for i, k in enumerate(TYPES):
                    c[i + 1] += flag(r, k)
            r0 = rows[0]
            for m in range(1, 13):
                c = counts[m]
                if c[0]:
                    out.write("\t".join([r0["STATION"], r0["NAME"], r0["LATITUDE"], r0["LONGITUDE"], r0["ELEVATION"], str(m), str(c[0])]
                                        + ["%.4f" % (v / c[0]) for v in c[1:]]) + "\n")
            yearly = [sum(counts[m][i] / counts[m][0] * MONTH_DAYS[m - 1] for m in range(1, 13) if counts[m][0]) for i in range(1, 1 + len(TYPES))]
            print("%-45s %2d years: thunder %3.0f, fog %3.0f, heavy fog %3.0f, hail %4.1f days a year" % (r0["NAME"][:45], len(good), *yearly))
    print("wrote", a.out)


if __name__ == "__main__":
    main()
