"""
The modern reservoirs Earth's water bakes leave out (no dams in 1318): HydroLAKES' reservoirs, and from decision 0069
rule 9 the reservoirs of Global Dam Watch v1.0 HydroLAKES lacks (dams built after its dam data, like the Seimare Dam of
2013, which HydroLAKES has as a natural lake). Read by tools/bake_dam_removal.py, bake_lake_outlines.py and
bake_river_outlines.py, each with EARTH_GDW=1 for its new file version (dams-glo90-3, lakes-glo90-2,
rivers-outline-glo90-4).

Global Dam Watch v1.0: Lehner, B., Beames, P., Mulligan, M., Zarfl, C., De Felice, L., van Soesbergen, A., Thieme, M.,
Garcia de Leaniz, C., Anand, M., Belletti, B., et al. (2024): The Global Dam Watch database of river barrier and reservoir
information for large-scale applications. Scientific Data 11, 1069. https://doi.org/10.1038/s41597-024-03752-9.
CC BY 4.0. Its barriers (GDW_barriers_v1_0) are taken as reservoirs when:
  - built after 1318 or of unknown year (decision 0064 keeps reservoirs older than 1318);
  - their reservoir is at least MIN_KM2 (AREA_SKM);
  - not a natural lake's control structure (DAM_TYPE "Lake Control Dam", LAKE_CTRL Yes/Maybe/Enlarged, or a HydroLAKES
    lake of type 3, lake control);
  - not a HydroLAKES reservoir already (type 2: it is in the list from HydroLAKES);
  - on a lake HydroLAKES has as natural (type 1) only when the dam is a real one: built 1900 or later and at least
    TYPE1_MIN_HEIGHT_M high (a weir at a natural lake's outlet doesn't make the lake a reservoir).
Each GDW reservoir's pour point is its dam on the river (LONG_RIV, LAT_RIV, else LONG_DAM, LAT_DAM); its id is
GDW_ID_BASE + GDW_ID, above HydroLAKES' ids.
"""
import io, os, site, sys, zipfile
sys.path.append(site.getusersitepackages())
import numpy as np

MIN_KM2 = 0.1
TYPE1_MIN_HEIGHT_M = 15
GDW_ID_BASE = 10_000_000
GDW_ZIP = ("global-dam-watch", "GDW_v1_0_shp.zip")
HL_SHP = ("hydrolakes", "HydroLAKES_points_v10_shp", "HydroLAKES_points_v10.shp")


def gdw_enabled():
    return os.environ.get("EARTH_GDW", "0") == "1"


def _num(v):
    try:
        f = float(v)
        return f if np.isfinite(f) and f != -99 else None
    except (TypeError, ValueError):
        return None


def hydrolakes(raw, types=(2,)):
    """HydroLAKES pour points of the given lake types: rows (id, lon, lat, area km2, elevation m, type)."""
    import shapefile
    hl = shapefile.Reader(os.path.join(raw, *HL_SHP), encoding="latin1", encodingErrors="replace")
    return [(r["Hylak_id"], r["Pour_long"], r["Pour_lat"], r["Lake_area"], r["Elevation"], r["Lake_type"]) for r in
            hl.iterRecords(fields=["Hylak_id", "Lake_type", "Lake_area", "Elevation", "Pour_long", "Pour_lat"])
            if r["Lake_type"] in types]


def gdw_additions(raw, hl_types=None):
    """Global Dam Watch's reservoirs HydroLAKES lacks, by the rules above: rows (id, lon, lat, area km2, elevation m or
    NaN, 2) and a dict of how many barriers each rule took or left."""
    import shapefile
    if hl_types is None:
        hl = shapefile.Reader(os.path.join(raw, *HL_SHP), encoding="latin1", encodingErrors="replace")
        hl_types = {r["Hylak_id"]: r["Lake_type"] for r in hl.iterRecords(fields=["Hylak_id", "Lake_type"])}
    z = zipfile.ZipFile(os.path.join(raw, *GDW_ZIP))
    base = "GDW_v1_0_shp/GDW_barriers_v1_0"
    b = shapefile.Reader(shp=io.BytesIO(z.read(base + ".shp")), dbf=io.BytesIO(z.read(base + ".dbf")),
                         shx=io.BytesIO(z.read(base + ".shx")), encoding="utf-8", encodingErrors="replace")
    fields = ["GDW_ID", "DAM_TYPE", "LAKE_CTRL", "YEAR_DAM", "DAM_HGT_M", "AREA_SKM", "ELEV_MASL", "LONG_RIV", "LAT_RIV",
              "LONG_DAM", "LAT_DAM", "HYLAK_ID"]
    rows, why = [], {}

    def count(k):
        why[k] = why.get(k, 0) + 1

    for rec in b.iterRecords(fields=fields):
        r = {k: rec[k] for k in fields}
        year, height, area, hylak = _num(r["YEAR_DAM"]), _num(r["DAM_HGT_M"]), _num(r["AREA_SKM"]), _num(r["HYLAK_ID"])
        t = hl_types.get(int(hylak)) if hylak and hylak > 0 else None
        if year is not None and 0 < year <= 1318:
            count("built by 1318: kept"); continue
        if area is None or area < MIN_KM2:
            count(f"reservoir under {MIN_KM2} km2 or none"); continue
        if r["DAM_TYPE"] == "Lake Control Dam" or (r["LAKE_CTRL"] or "") in ("Yes", "Maybe", "Enlarged") or t == 3:
            count("a natural lake's control: kept"); continue
        if t == 2:
            count("a HydroLAKES reservoir already"); continue
        if t == 1 and not (year is not None and year >= 1900 and height is not None and height >= TYPE1_MIN_HEIGHT_M):
            count("on a HydroLAKES natural lake, no real dam: kept"); continue
        lon, lat = _num(r["LONG_RIV"]), _num(r["LAT_RIV"])
        if lon is None or lat is None or (lon == 0 and lat == 0):
            lon, lat = _num(r["LONG_DAM"]), _num(r["LAT_DAM"])
        if lon is None or lat is None or (lon == 0 and lat == 0):
            count("no position"); continue
        elev = _num(r["ELEV_MASL"])
        rows.append((GDW_ID_BASE + int(r["GDW_ID"]), lon, lat, area, np.nan if elev is None else elev, 2))
        count("added: on a HydroLAKES natural lake, a real dam" if t == 1 else "added: not in HydroLAKES")
    return rows, why


def reservoirs(raw, types=(2,), south=-90.0, log=print):
    """HydroLAKES' pour points of the given types, and with EARTH_GDW=1 Global Dam Watch's additions (type 2): rows
    (id, lon, lat, area km2, elevation m, type) north of `south`, as a float64 array."""
    rows = [r for r in hydrolakes(raw, types) if r[2] >= south]
    log(f"{len(rows)} HydroLAKES pour points of types {types}")
    if gdw_enabled() and 2 in types:
        add, why = gdw_additions(raw)
        add = [r for r in add if r[2] >= south]
        log(f"Global Dam Watch: {len(add)} reservoirs added; " + "; ".join(f"{k} {v}" for k, v in sorted(why.items(), key=lambda kv: -kv[1])))
        rows += add
    return np.array(rows, np.float64)


if __name__ == "__main__":
    os.environ["EARTH_GDW"] = "1"
    a = reservoirs(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "rawdata"))
    print(len(a), "rows; Seimare:", a[a[:, 0] == GDW_ID_BASE + 6937].tolist())
