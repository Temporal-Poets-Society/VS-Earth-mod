"""Earth 0069 rule 8: the GIRES v1.0 non-perennial river map (Messager & Lehner 2021, CC BY-SA 4.0) reduced to what
Earth reads: per RiverATLAS reach (the HydroRIVERS reaches) its id, midpoint, long-term discharge and predicted flow
intermittence (at least 1 and at least 30 zero-flow days a year: probability and class).
usage: python -I tools/extract_gires.py GIRES_v10_shp.zip OUT.npz"""
import site, sys, time, zipfile
sys.path.append(site.getusersitepackages())
import numpy as np
import shapefile

KEYS = ["HYRIV_ID", "MID_X", "MID_Y", "dis_m3_pyr", "predprob1", "predcat1", "predprob30", "predcat30"]
z = zipfile.ZipFile(sys.argv[1])
cols = {k: [] for k in KEYS}
t0 = time.time()
for name in sorted(n for n in z.namelist() if n.endswith(".dbf") and "_rivers_" in n):
    r = shapefile.Reader(dbf=z.open(name), encoding="utf-8")
    n0 = len(cols["HYRIV_ID"])
    for rec in r.iterRecords(fields=KEYS):
        for k, v in zip(KEYS, rec):
            cols[k].append(v)
    print(f"{name}: {len(cols['HYRIV_ID']) - n0} reaches ({time.time() - t0:.0f} s)", flush=True)
np.savez_compressed(sys.argv[2],
                    id=np.array(cols["HYRIV_ID"], np.int64), lon=np.array(cols["MID_X"], np.float64),
                    lat=np.array(cols["MID_Y"], np.float64), q=np.array(cols["dis_m3_pyr"], np.float32),
                    p1=np.array(cols["predprob1"], np.float32), c1=np.array(cols["predcat1"], np.int8),
                    p30=np.array(cols["predprob30"], np.float32), c30=np.array(cols["predcat30"], np.int8))
print("done", flush=True)
