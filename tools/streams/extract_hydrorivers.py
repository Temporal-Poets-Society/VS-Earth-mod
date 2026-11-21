"""EA-0131 (decision 0061): HydroRIVERS v1.0's attributes for every reach, as arrays (one pass over the 873 MB dbf).
usage: python -I tools/streams/extract_hydrorivers.py <HydroRIVERS_v10.dbf> <out.npz>
Keeps HYRIV_ID, NEXT_DOWN, MAIN_RIV, LENGTH_KM, DIST_DN_KM, UPLAND_SKM, DIS_AV_CMS, ORD_STRA, ORD_CLAS, ORD_FLOW, ENDORHEIC
and the record number (the shapes' order)."""
import sys, time
import numpy as np
import shapefile

KEYS = ["HYRIV_ID", "NEXT_DOWN", "MAIN_RIV", "LENGTH_KM", "DIST_DN_KM", "UPLAND_SKM", "DIS_AV_CMS", "ORD_STRA", "ORD_CLAS",
        "ORD_FLOW", "ENDORHEIC"]
r = shapefile.Reader(dbf=open(sys.argv[1], "rb"))
n = len(r)
cols = {k: [] for k in KEYS}
t0 = time.time()
for i, rec in enumerate(r.iterRecords(fields=KEYS)):
    d = rec.as_dict()   # (pyshp gives the fields in the file's order, not KEYS')
    for k in KEYS:
        cols[k].append(d[k])
    if i % 1000000 == 0:
        print(f"{i} of {n} ({time.time() - t0:.0f} s)", flush=True)
np.savez(sys.argv[2],
         id=np.array(cols["HYRIV_ID"], np.int64), next=np.array(cols["NEXT_DOWN"], np.int64),
         main=np.array(cols["MAIN_RIV"], np.int64), length=np.array(cols["LENGTH_KM"], np.float32),
         distdn=np.array(cols["DIST_DN_KM"], np.float32), upland=np.array(cols["UPLAND_SKM"], np.float32),
         q=np.array(cols["DIS_AV_CMS"], np.float32), strahler=np.array(cols["ORD_STRA"], np.int32),
         clas=np.array(cols["ORD_CLAS"], np.int32), flow=np.array(cols["ORD_FLOW"], np.int32),
         endo=np.array(cols["ENDORHEIC"], np.int32))
print(f"done: {n} reaches, {time.time() - t0:.0f} s", flush=True)
