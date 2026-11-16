"""
Fetches the sources of the 1318 temperature correction (decision 0087) into earth-raw/paleo-1318:
  - LMR v2.1 (Tardif et al. 2019) gridded 2 m air temperature, ensemble mean and spread, NOAA NCEI (public domain);
  - GISTEMP v4, 1200 km smoothing, NASA GISS (public domain);
  - CMIP6 PMIP4 past1000 and CMIP historical, tas Amon, five models, only the files covering 1300-1330 and 1981-2010,
    from ESGF (CC BY 4.0); MRI-ESM2-0's past1000 (one 850-1849 file) as its 1300-1330 years through OPeNDAP, else whole.
Every file is fetched once (an existing one is skipped) and listed with its size in fetched.tsv.

    .venv/Scripts/python tools/fetch_paleo_1318.py [folder]
"""
import json, os, sys, time, urllib.parse, urllib.request

FOLDER = sys.argv[1] if len(sys.argv) > 1 else r"C:\VS mods\data\earth-raw\paleo-1318"
LMR = "https://www.ncei.noaa.gov/pub/data/paleo/reconstructions/tardif2019lmr/v2_1/"
GISTEMP = "https://data.giss.nasa.gov/pub/gistemp/gistemp1200_GHCNv4_ERSSTv5.nc.gz"
MODELS = [("ACCESS-ESM1-5", "r1i1p1f1"), ("MIROC-ES2L", "r1i1p1f2"), ("MRI-ESM2-0", "r1i1p1f1"), ("INM-CM4-8", "r1i1p1f1"),
          ("EC-Earth3-Veg-LR", "r1i1p1f1")]
ESGF = "https://esgf.ceda.ac.uk/esg-search/search?"


def log(msg):
    print(msg, flush=True)


def get(url, target):
    if os.path.exists(target) and os.path.getsize(target) > 0:
        log(f"there: {os.path.basename(target)}")
        return
    t0 = time.time()
    req = urllib.request.Request(url, headers={"User-Agent": "earth-mod-data-fetch"})
    with urllib.request.urlopen(req, timeout=300) as r, open(target + ".part", "wb") as f:
        while True:
            b = r.read(1 << 20)
            if not b:
                break
            f.write(b)
    os.replace(target + ".part", target)
    log(f"got {os.path.basename(target)}: {os.path.getsize(target) / 1e6:.1f} MB in {time.time() - t0:.0f} s")


def search(**kw):
    q = dict(type="File", project="CMIP6", format="application/solr+json", limit="500", distrib="true",
             fields="title,size,url,source_id,member_id,data_node", variable_id="tas", table_id="Amon")
    q.update(kw)
    return json.load(urllib.request.urlopen(ESGF + urllib.parse.urlencode(q), timeout=120))["response"]["docs"]


def covers(title, y0, y1):
    r = title.rsplit("_", 1)[1].split(".")[0]
    return int(r[:4]) <= y1 and int(r[7:11]) >= y0


def files_for(model, member, exp, y0, y1):
    out = {}
    docs = search(experiment_id=exp, source_id=model, member_id=member)
    if not any(covers(d["title"], y0, y1) for d in docs):
        # replicas can fill the answer's 500 places before the originals (EC-Earth3-Veg-LR's yearly historical files)
        docs = search(experiment_id=exp, source_id=model, member_id=member, replica="false")
    for d in docs:
        if not covers(d["title"], y0, y1) or d["title"] in out:
            continue
        http = [u.split("|")[0] for u in d["url"] if u.endswith("HTTPServer")]
        dap = [u.split("|")[0] for u in d["url"] if u.endswith("OPENDAP")]
        if http:
            out[d["title"]] = (d["size"], http[0], dap[0] if dap else None)
    return out


def subset_opendap(dap, target, first_year, y0, y1):
    """The months y0..y1 of a file whose first month is January of first_year, through OPeNDAP, as a small NetCDF."""
    import netCDF4
    import numpy as np
    url = dap[:-5] if dap.endswith(".html") else dap
    with netCDF4.Dataset(url) as src:
        i0, i1 = (y0 - first_year) * 12, (y1 - first_year + 1) * 12
        tas = src.variables["tas"][i0:i1]
        lat, lon = src.variables["lat"][:], src.variables["lon"][:]
        t = src.variables["time"]
        times, units, cal = t[i0:i1], t.units, getattr(t, "calendar", "standard")
    with netCDF4.Dataset(target + ".part", "w") as dst:
        dst.createDimension("time", None); dst.createDimension("lat", len(lat)); dst.createDimension("lon", len(lon))
        v = dst.createVariable("time", "f8", ("time",)); v.units = units; v.calendar = cal; v[:] = times
        dst.createVariable("lat", "f8", ("lat",))[:] = lat
        dst.createVariable("lon", "f8", ("lon",))[:] = lon
        v = dst.createVariable("tas", "f4", ("time", "lat", "lon"), zlib=True); v.units = "K"; v[:] = tas
        dst.source = f"subset {y0}-{y1} of {url}"
    os.replace(target + ".part", target)
    log(f"got {os.path.basename(target)} by OPeNDAP: {os.path.getsize(target) / 1e6:.1f} MB")


def main():
    os.makedirs(FOLDER, exist_ok=True)
    for name in ("air_MCruns_ensemble_mean_LMRv2.1.nc", "air_MCruns_ensemble_spread_LMRv2.1.nc"):
        get(LMR + name, os.path.join(FOLDER, name))
    get(GISTEMP, os.path.join(FOLDER, os.path.basename(GISTEMP)))
    cmip = os.path.join(FOLDER, "cmip6")
    os.makedirs(cmip, exist_ok=True)
    for model, member in MODELS:
        for exp, y0, y1 in (("past1000", 1300, 1330), ("historical", 1981, 2010)):
            fs = files_for(model, member, exp, y0, y1)
            if not fs:
                log(f"FAILED {model} {exp}: no file on ESGF covers {y0}-{y1}")
                continue
            for title, (size, http, dap) in sorted(fs.items()):
                target = os.path.join(cmip, title)
                span = title.rsplit("_", 1)[1].split(".")[0]
                whole = int(span[7:11]) - int(span[:4]) > 200
                if whole and dap:
                    sub = os.path.join(cmip, title.replace(span, f"{y0}01-{y1}12").replace(".nc", "_subset.nc"))
                    if os.path.exists(sub):
                        log(f"there: {os.path.basename(sub)}")
                        continue
                    try:
                        subset_opendap(dap, sub, int(span[:4]), y0, y1)
                        continue
                    except Exception as e:
                        log(f"OPeNDAP subset of {title} failed ({type(e).__name__}: {str(e)[:150]}); the whole file instead")
                try:
                    get(http, target)
                except Exception as e:
                    log(f"FAILED {title}: {type(e).__name__}: {str(e)[:200]}")
    with open(os.path.join(FOLDER, "fetched.tsv"), "w", encoding="utf8") as f:
        for root, _, names in os.walk(FOLDER):
            for n in sorted(names):
                if n.endswith((".nc", ".gz")):
                    p = os.path.join(root, n)
                    f.write(f"{os.path.relpath(p, FOLDER)}\t{os.path.getsize(p)}\n")
    log("DONE")


if __name__ == "__main__":
    main()
