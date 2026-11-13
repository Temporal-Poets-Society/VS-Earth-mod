#!/usr/bin/env bash
# Downloads the free source datasets into rawdata/ (about 7.9 GB: 5.1 GB CHELSA, 1.6 GB ETOPO 30", 0.4 GB RGI). Then run:
#   python -m venv .venv && .venv/Scripts/python -m pip install numpy scipy netCDF4 tifffile imagecodecs pyshp pillow brotli
#   .venv/Scripts/python tools/bake_earth.py
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$ROOT/rawdata" && cd "$ROOT/rawdata"

# into "$2.part", renamed only when curl finished: a download cut short is fetched again on the next run (UDel's
# precipitation once stayed cut short at 11 of 18.7 MB: December read as 0 mm, decision 0018)
get() { [ -s "$2" ] || { curl -fL --retry 3 -o "$2.part" "$1" && mv "$2.part" "$2"; }; }

# ETOPO 2022, 60 arc-seconds, surface (ice surface) and bed (bedrock under the ice sheets). NOAA NCEI, public domain.
E=https://www.ngdc.noaa.gov/mgg/global/relief/ETOPO2022/data/60s
get $E/60s_surface_elev_gtif/ETOPO_2022_v1_60s_N90W180_surface.tif ETOPO_2022_v1_60s_N90W180_surface.tif
get $E/60s_bed_elev_gtif/ETOPO_2022_v1_60s_N90W180_bed.tif ETOPO_2022_v1_60s_N90W180_bed.tif
# ETOPO 2022, 30 arc-seconds, surface (~1.6 GB): the mountain summits for summits.bin (tools/bake_summits.py)
mkdir -p etopo-30s
get https://www.ngdc.noaa.gov/mgg/global/relief/ETOPO2022/data/30s/30s_surface_elev_gtif/ETOPO_2022_v1_30s_N90W180_surface.tif etopo-30s/ETOPO_2022_v1_30s_N90W180_surface.tif

# Climate normals from NOAA PSL (free): UDel v5.01 over land, NCEP/NCAR 2 m air and GPCP precipitation over the oceans.
# (PSL's THREDDS server, psl.noaa.gov/thredds/fileServer, answered 429 Too Many Requests on 2026-11-07; same files)
P=https://downloads.psl.noaa.gov/Datasets
# UDel into udel-v501/ (complete: 18 669 612 and 18 669 608 bytes); tools/bake_earth.py rebuilds the cut-short copy
# climate.bin was baked from (UDEL_LEGACY_PRECIP) from the complete one when it needs it
mkdir -p udel-v501
get $P/udel.airt.precip/air.mon.v501.ltm.nc udel-v501/air.mon.v501.ltm.nc
get $P/udel.airt.precip/precip.mon.v501.ltm.nc udel-v501/precip.mon.v501.ltm.nc
get $P/ncep.reanalysis.derived/surface_gauss/air.2m.mon.ltm.1991-2020.nc ncep.reanalysis.derived_surface_gauss_air.2m.mon.ltm.1991-2020.nc
get $P/gpcp/precip.mon.ltm.1991-2020.nc gpcp_precip.mon.ltm.1991-2020.nc
# the weather's monthly 10 m winds and total cloud cover (NCEP/NCAR Reanalysis 1 long-term means, ~1 MB each)
mkdir -p ncep-reanalysis
get $P/ncep.reanalysis.derived/surface_gauss/uwnd.10m.mon.ltm.1991-2020.nc ncep-reanalysis/uwnd.10m.mon.ltm.1991-2020.nc
get $P/ncep.reanalysis.derived/surface_gauss/vwnd.10m.mon.ltm.1991-2020.nc ncep-reanalysis/vwnd.10m.mon.ltm.1991-2020.nc
get $P/ncep.reanalysis.derived/other_gauss/tcdc.eatm.mon.ltm.1991-2020.nc ncep-reanalysis/tcdc.eatm.mon.ltm.1991-2020.nc

# Natural Earth 1:10m lakes (public domain)
for n in ne_10m_lakes ne_10m_lakes_europe ne_10m_lakes_north_america; do
  get https://naciscdn.org/naturalearth/10m/physical/$n.zip $n.zip
  unzip -o -q $n.zip -d ne_lakes
done
# GSHHG 2.3.7 shorelines (Wessel & Smith, LGPL), full resolution only (~150 MB): the small islands' outlines, and where
# ETOPO's land is real (it has false islands on reef rims)
get https://www.soest.hawaii.edu/pwessel/gshhg/gshhg-shp-2.3.7.zip gshhg-shp-2.3.7.zip
unzip -o -q gshhg-shp-2.3.7.zip "GSHHS_shp/f/*" LICENSE.TXT README.TXT -d gshhg
# HydroRIVERS v1.0 (Lehner & Grill 2013, www.hydrosheds.org; free with attribution, see their license)
get https://data.hydrosheds.org/file/HydroRIVERS/HydroRIVERS_v10_shp.zip HydroRIVERS_v10_shp.zip
unzip -o -q HydroRIVERS_v10_shp.zip -d hydrorivers
# HydroLAKES v1.0 points (Messager et al. 2016, www.hydrosheds.org; same license): reservoirs, to leave dams out
get https://data.hydrosheds.org/file/hydrolakes/HydroLAKES_points_v10_shp.zip HydroLAKES_points_v10_shp.zip
unzip -o -q HydroLAKES_points_v10_shp.zip -d hydrolakes
# which Natural Earth lakes are reservoirs, from Wikidata (CC0)
"$ROOT/.venv/Scripts/python" "$ROOT/tools/fetch_lake_types.py" || echo "Wikidata lake types not fetched (optional)"
# CHELSA V2.1 (Karger et al. 2017/2021, CC BY 4.0, www.chelsa-climate.org; files on WSL's EnviCloud): monthly mean air
# temperature 1981-2010 at 30 arc-seconds and CHELSA's orography, about 3.7 GB, for temperature.bin (tools/bake_chelsa.py)
C=https://os.unil.cloud.switch.ch/chelsa02/chelsa/global
mkdir -p chelsa-v2.1
for m in 01 02 03 04 05 06 07 08 09 10 11 12; do
  get $C/climatologies/tas/1981-2010/CHELSA_tas_${m}_1981-2010_V.2.1.tif chelsa-v2.1/CHELSA_tas_${m}_1981-2010_V.2.1.tif
done
for f in dem_latlong.sgrd dem_latlong.sdat; do get $C/input/static/$f chelsa-v2.1/$f; done
# CHELSA V2.1 bioclim 1981-2010 (same license): annual precipitation (bio12) and precipitation of the warmest quarter
# (bio18), about 1.1 GB, for rain.bin (tools/bake_rain.py)
CB=https://os.zhdk.cloud.switch.ch/chelsav2/GLOBAL/climatologies/1981-2010/bio
mkdir -p chelsa-bio-v2.1
for v in 12 18; do get $CB/CHELSA_bio${v}_1981-2010_V.2.1.tif chelsa-bio-v2.1/CHELSA_bio${v}_1981-2010_V.2.1.tif; done
# CHELSA V2.1 BIOCLIM+ (same licence): the mean monthly Penman-Monteith evapotranspiration, about 330 MB, for aridity.bin
# (tools/bake_aridity.py)
get $CB/CHELSA_pet_penman_mean_1981-2010_V.2.1.tif chelsa-bio-v2.1/CHELSA_pet_penman_mean_1981-2010_V.2.1.tif
# Randolph Glacier Inventory 6.2 (RGI Consortium 2017, RGI 6.0, doi:10.7265/N5-RGI-60, CC BY 4.0), OGGM's public mirror,
# about 420 MB: the mountain glaciers' outlines for glaciers.bin (tools/bake_glaciers.py)
mkdir -p rgi62
get https://cluster.klima.uni-bremen.de/~oggm/rgi/rgi62.zip rgi62/rgi62.zip
unzip -o -q rgi62/rgi62.zip -d rgi62
for z in rgi62/[0-9][0-9]_rgi62_*.zip; do unzip -o -q "$z" -d rgi62/shp; done
echo "done: $(du -sh . | cut -f1)"
