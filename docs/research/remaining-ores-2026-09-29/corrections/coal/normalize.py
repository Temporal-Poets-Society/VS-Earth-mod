"""Normalize primary coal geology without turning full basins or workings buffers into ore."""
from pathlib import Path
from zipfile import ZipFile
from collections import Counter
import csv,json,math,sys,tempfile,datetime
import pyogrio
from pyogrio.raw import read
from shapely import from_wkb
from shapely.geometry import mapping,shape
from shapely.ops import transform
from pyproj import Transformer

ROOT=Path(__file__).resolve().parent;RAW=ROOT/'raw';OUT=ROOT/'normalized';OUT.mkdir(exist_ok=True)
POLYGONS=[];CONTEXT=[];POINTS=[];HALITE=[];LAYERS=[]
DATE='2026-09-29'

SOURCES={
 'bgs_coal_2004':dict(title='BGS Coal Resources for New Technologies',url='https://www.bgs.ac.uk/datasets/coal-resources-for-new-technologies/',year=2004,license='Open Government Licence',license_url='https://www.nationalarchives.gov.uk/doc/open-government-licence/version/3/',attribution='Contains British Geological Survey materials © UKRI; BGS/Coal Authority 1999 geological base. Retrieved 29 September 2026.',limitation='1:1,000,000 legacy compilation. Contains both exposed and concealed coal-bearing strata, not continuous coal seams. Modern economic assumptions do not define geological absence.'),
 'pgi_midas':dict(title='PIG-PIB MIDAS documented and archived mineral-deposit boundaries',url='https://geologia.pgi.gov.pl/srv/rest/services/midas/MapServer',year=None,license='PIG-PIB public-sector information reuse conditions; attribution/date/processing disclosure and original database terms',license_url='https://www.pgi.gov.pl/rejestry-i-bazy-danych-geologicznych/zasady-korzystania-z-isp.html',attribution='PIG-PIB, MIDAS documented deposits layer0 and selected archived deposits layer3, retrieved 29 September 2026. Coal-only subset; API reprojection to WGS84 and schema normalization by Earth research.',limitation='Deposit documentation boundaries can be reconstructed from mining-area boundaries; original caveats retained. Current registry does not mean currently operating. Archived register is explicitly incomplete. Full source reuse conditions remain applicable.'),
 'saxony_ruk400':dict(title='LfULG Saxony RÜK400 fossil fuels',url='https://luis.sachsen.de/geologie/rohstoffgeologische-karten.html',year=None,license='Datenlizenz Deutschland – Namensnennung 2.0',license_url='https://www.govdata.de/dl-de/by-2-0',attribution='Geodaten Sachsen, LfULG, RÜK400Bre, retrieved 29 September 2026; selected coal-seam polygons reprojected by Earth research.',limitation='1:400,000 coal-seam distributions. Individual seam layers overlap vertically; do not sum feature counts or overlap to infer abundance. Aggregate duplicate seam layer and pit boundaries excluded.'),
 'saxony_lignite50':dict(title='LfULG Saxony lignite-field inventory',url='https://luis.sachsen.de/geologie/rohstoffgeologische-karten.html',year=None,license='Datenlizenz Deutschland – Namensnennung 2.0',license_url='https://www.govdata.de/dl-de/by-2-0',attribution='Geodaten Sachsen, LfULG, Lagerstättenkataster Braunkohle 1:50,000, retrieved 29 September 2026; reprojected by Earth research.',limitation='Named inventory fields overlap seam distributions. Source IDs retained; names are not guessed from identifiers. Publication page supplies scale; individual mining/depletion/depth status absent.'),
 'nrw_rk500':dict(title='Geologischer Dienst NRW IS RK500',url='https://www.gd.nrw.de/pr_kd_rohstoffkarte-500000.php',year=2005,license='Datenlizenz Deutschland – Namensnennung 2.0',license_url='https://www.govdata.de/dl-de/by-2-0',attribution='IS RK500 (https://www.opengeodata.nrw.de/produkte/geologie/geologie/RK/ISRK500/), Geologischer Dienst NRW, Abrufdatum 29.09.2026. Nutzung gemäß Datenlizenz Deutschland – Namensnennung 2.0. Coal and rock-salt subsets reprojected by Earth research.',limitation='1:500,000 generalized resource areas. Actual layer and attribute say hard coal to 2000 m; landing page says 200 m. Preserve source 2000 m and flag this conflict; no shallow-access inference.'),
 'ga_basins_2013':dict(title='Geoscience Australia Coal Basin Outlines',url='https://data.gov.au/data/dataset/9d5a8d74-a201-42bd-9d49-3c392244be16',year=2013,license='Creative Commons Attribution 3.0 Australia',license_url='https://creativecommons.org/licenses/by/3.0/au/',attribution='© Commonwealth of Australia (Geoscience Australia), Australian Coal Basins 2013, Bioregional Assessment Source Dataset, retrieved 29 September 2026; reprojected by Earth research.',limitation='Full province extents including covered regions; best use1:2M–1:5M; original metadata says pre-release not fully QA checked. RANK field is geological province rank, not coal rank. No assumption that entire basin contains coal.'),
 'nsw_coalfields':dict(title='NSW Mining Coalfields',url='https://data.nsw.gov.au/data/dataset/nsw-mining-coalfieds',year=2016,license='Creative Commons Attribution (catalogue)',license_url='https://data.nsw.gov.au/data/dataset/nsw-mining-coalfieds',attribution='NSW Department of Industry 2016 / Geological Survey of NSW, Standing Committee 1976 coalfield definitions; retrieved 29 September 2026. API reprojection and normalization by Earth research.',limitation='Five named coalfield areas derived from structural basins, not seam-by-seam ore extents. Source original CRS GDA94. Existing restricted BA-derived contour dataset not used; independently released open source used.'),
 'victoria_coalfields':dict(title='Victorian Coal Fields 2006 inventory',url='https://discover.data.vic.gov.au/en_AU/dataset/victorian-coal-fields',year=2006,license='Creative Commons Attribution 4.0 International',license_url='https://creativecommons.org/licenses/by/4.0/',attribution='Victorian Government DEECA / Geological Survey of Victoria, COALINVFLDS, retrieved 29 September 2026; WFS output and schema normalization by Earth research.',limitation='46 fields may contain multiple seams. Source age, overburden, development, resources and accuracy-code fields retained without converting reserve tonnage into grade. Linked seam lookup tables not imported.'),
 'ga_aimr_2025':dict(title="Australia's Identified Mineral Resources 2025, coal resource sites",url='https://services.ga.gov.au/gis/rest/services/AustraliasIdentifiedMineralResources/MapServer',year=2025,license='Creative Commons Attribution 4.0 International',license_url='https://creativecommons.org/licenses/by/4.0/',attribution='© Commonwealth of Australia (Geoscience Australia), AIMR 2025 coal layers 32/33, retrieved 29 September 2026.',limitation='Supplementary resource-site points, not basin footprints; includes supplied site status without operating-mine filtering. Size class is resource tonnage, not grade or universal accessibility.'),
 'usgs_india_2011':dict(title='USGS GIS representation of coal-bearing areas in India and Bangladesh',url='https://pubs.usgs.gov/of/2011/1296/',year=2011,license='Use constraints:none; acknowledge USGS and cited original sources',license_url='https://pubs.usgs.gov/of/2011/1296/Metadata/IndiaCoalfields%20Metadata.htm',attribution='USGS Trippi/Tewalt et al., OFR2011-1296, underlying Geological Survey of India Bulletin45 and listed sources; retrieved 29 September 2026; datum transformed by Earth research.',limitation='Legacy georeferenced coal-bearing formation outlines; some contacts are USGS interpretations under alluvium. Formation identities not included. Indian 1975 (EPSG:4240) datum converted for display; no rank inferred from location.'),
 'usgs_china_2014':dict(title='USGS Chinese coal-mining areas compilation',url='https://pubs.usgs.gov/of/2014/1219/',year=2014,license='USGS release; original metadata and source references retained',license_url='https://pubs.usgs.gov/of/2014/1219/',attribution='USGS Trippi, Belkin, Dai, Tewalt and Chou, OFR2014-1219, source coal-mining areas as of 2001; retrieved 29 September 2026; Beijing1954 datum transformed by Earth research.',limitation='Mapped coal-mining areas from historical atlas, not current mine operations or exhaustive coalfield geometry. Source reported coal rank retained. Does not prove continuous coal or surface access.'),
 'usgs_americas_2008':dict(title='USGS coal-bearing areas in North, Central and South America',url='https://pubs.usgs.gov/of/2008/1257/',year=2008,license='USGS public release; attribution and original source references retained',license_url='https://pubs.usgs.gov/of/2008/1257/',attribution='USGS Tewalt, Kinney and Merrill, OFR2008-1257; cited national geology/coal sources retained per feature. Retrieved 29 September 2026.',limitation='Small-scale approximations using appropriate-age surficial geology constrained by coal literature. Not adjusted for structure and excludes coal at depth; not suitable for resource-volume calculation. Do not reinterpret entire sedimentary units as continuous seams.'),
 'usgs_us_2012':dict(title='Coal Fields of Conterminous US, NCRA updated version',url='https://pubs.usgs.gov/of/2012/1205/',year=2012,license='USGS public release; attribution and metadata retained',license_url='https://pubs.usgs.gov/of/2012/1205/Downloads/Metadata/Coal_Fields.html',attribution='USGS OFR2012-1205, updated Coal_Fields polygon layer, retrieved 29 September 2026; NAD83 to WGS84 transformation by Earth research.',limitation='National generalized coalfields, includes inherited legacy boundaries. Detailed assessed thickness/overburden layers remain raw and are not mixed into resource extent counts. Overlaps the broader2008 Americas layer; not independent evidence.'),
}

def clean(v):
 if hasattr(v,'item'):v=v.item()
 if isinstance(v,(datetime.date,datetime.datetime)):return v.isoformat()
 if isinstance(v,float) and not math.isfinite(v):return None
 if isinstance(v,str) and v.strip() in ('','None','Not Reported','Not reported','Unknown'):return None
 return v

def prop(source,fid,layer,attrs,crs,kind):
 s=SOURCES[source]
 return dict(source_id=source,source_feature_id=str(fid),source_layer=layer,source_url=s['url'],source_attributes=attrs,
  snapshot_date=DATE,source_crs=crs,output_crs='EPSG:4326',feature_kind=kind,name=None,country=None,
  coal_rank_source=None,coal_age_source=None,depth_min_m=None,depth_max_m=None,depth_source_text=None,
  exposure=None,source_status=None,economic_status=None,historical_status=None,early_accessibility=None,
  grade_value=None,thickness_m=None,spatial_precision=None,source_reference=None,eligibility_limit=s['limitation'],
  license=s['license'],attribution=s['attribution'],game_generation_ready=False)

def add(source,fid,layer,attrs,geometry,crs,kind,changes=None,target=None,transformation=None):
 p=prop(source,fid,layer,attrs,crs,kind);p.update(changes or {})
 p['coordinate_transform']=transformation or 'Provider GeoJSON output requested in WGS84; original source CRS retained in service metadata'
 p['geometry_valid']=bool(geometry.is_valid)
 p['geometry_empty']=bool(geometry.is_empty)
 f=dict(type='Feature',id=source+':'+layer+':'+str(fid),geometry=mapping(geometry),properties=p)
 (target if target is not None else POLYGONS).append(f)

def vector(zipname,member,layer,source,kind,changes,target=None,crs_override=None):
 path='/vsizip/'+(RAW/zipname).as_posix()+'/'+member
 meta,fids,wkb,arrays=read(path,layer=layer,return_fids=True)
 crs=meta['crs'] or crs_override
 if not crs:raise ValueError('Unverified CRS: '+member)
 transformer=Transformer.from_crs(crs,'EPSG:4326',always_xy=True)
 for i,fid in enumerate(fids):
  a={str(k):clean(v[i]) for k,v in zip(meta['fields'],arrays)}
  original=from_wkb(wkb[i]);g=transform(transformer.transform,original)
  try:operation=transformer.get_last_used_operation().description
  except Exception:operation=transformer.description
  add(source,fid,member+'::'+layer if layer else member,a,g,crs,kind,changes(a),target,operation)
 LAYERS.append(dict(source=source,archive=zipname,member=member,layer=layer,source_crs=crs,records=len(fids),feature_kind=kind))

def geojson(file,source,kind,changes,target=None,source_crs=None):
 data=json.loads((RAW/file).read_text(encoding='utf-8'))
 assert 'error' not in data and not data.get('exceededTransferLimit')
 for i,f in enumerate(data['features']):
  a=f['properties'];fid=a.get('OBJECTID',a.get('objectid',f.get('id',i)))
  add(source,fid,file,a,shape(f['geometry']),source_crs,kind,changes(a),target)
 LAYERS.append(dict(source=source,file=file,records=len(data['features']),feature_kind=kind))

def main():
 for area in ['GB','NI']:
  member=f'Coal Resources For New Technologies/Coal_Resources_For_New_Technologies_{area}.gpkg'
  def bgs(a):
   t=a['FEATURE'];return dict(name=t,country='United Kingdom',coal_rank_source='Lignite' if 'Lignite' in t else None,
    depth_min_m=1200 if '> 1200m' in t else None,depth_max_m=1200 if '< 1200m' in t else None,
    depth_source_text=t,exposure='outcropping coal-bearing strata' if 'at surface' in t else 'concealed',
    spatial_precision='1:1,000,000; boundary not a seam trace',source_reference=a.get('SHEET'))
  vector('bgs-coal-gpkg.zip',member,'Coal-bearing strata','bgs_coal_2004','mapped_coal_bearing_strata',bgs)
  opencast='Area with technical potential for opencast workings (source Coal Resource Map of Great Britain BGS/Coal Authority 1999)'
  vector('bgs-coal-gpkg.zip',member,opencast,'bgs_coal_2004','technical_surface_working_potential',lambda a:dict(name=a['FEATURE'],country='United Kingdom',depth_source_text=None,spatial_precision='1:1,000,000; technical potential, not proven ancient access'),CONTEXT)
  if area=='GB':
   vector('bgs-coal-gpkg.zip',member,'Extent of underground workings with 500m buffer zone (based on Coal Authority data, May 2002)','bgs_coal_2004','historical_workings_with_source_buffer',lambda a:dict(name=a['FEATURE'],country='United Kingdom',historical_status='Underground workings extent as of May 2002 with ORIGINAL source 500 m buffer',eligibility_limit='Source includes 500 m buffer. Historical context only: not an ore-body boundary or evidence of remaining coal.'),CONTEXT)
 for mode in ['current','archive']:
  def pgi(a):return dict(name=a['NAZWA_ZL'],country='Poland',coal_rank_source=a['COMMODITY'],
   source_status='documented in current MIDAS registry; operational status unknown' if mode=='current' else 'removed from resource balance; selected archival record',
   historical_status=a.get('USUNIECIE'),source_reference=a.get('NR_DOK'),spatial_precision='Documentation-dependent; source caveats in UWAGI/LOKACJA/UKL_WSP',
   boundary_source_caveat=a.get('UWAGI'),deposit_registry_id=a['NR_ZLOZA'])
  geojson(f'pgi-{mode}-coal.geojson','pgi_midas','documented_deposit_boundary' if mode=='current' else 'archived_deposit_boundary',pgi,source_crs='EPSG:2180')
 with ZipFile(RAW/'saxony-coal400.zip') as z:
  for t in z.namelist():
   if t.endswith('.shp') and t not in ['Floeze_gesamt.shp','Tagebaue.shp']:
    vector('saxony-coal400.zip',t,None,'saxony_ruk400','mapped_coal_seam_distribution',lambda a,t=t:dict(name=Path(t).stem,country='Germany',coal_rank_source='Brown coal / lignite',spatial_precision='1:400,000',coal_age_source='Miocene to Eocene seam units; exact unit retained in source layer'))
 with ZipFile(RAW/'saxony-lignite50.zip') as z:
  for t in z.namelist():
   if t.endswith('.shp'):vector('saxony-lignite50.zip',t,None,'saxony_lignite50','mapped_lignite_field',lambda a,t=t:dict(name=Path(t).stem+' / '+str(a.get('IDENT')),country='Germany',coal_rank_source='Brown coal / lignite',spatial_precision='1:50,000'))
 for layer in ['RK500_Steinkohle_2000m','RK500_Braunkohle']:
  vector('nrw500-gpkg.zip','IS_RK500.gpkg',layer,'nrw_rk500','generalized_resource_area',lambda a:dict(name=a['ROHSTOFFART'],country='Germany',coal_rank_source='Hard coal' if 'Steinkohle' in a['ROHSTOFFART'] else 'Brown coal / lignite',depth_max_m=2000 if '2000' in a['ROHSTOFFART'] else None,depth_source_text=a['ROHSTOFFART'],spatial_precision='1:500,000',depth_metadata_conflict='Landing page says 200 m; actual layer and attribute say 2000 m' if '2000' in a['ROHSTOFFART'] else None))
 vector('nrw500-gpkg.zip','IS_RK500.gpkg','RK500_Steinsalz','nrw_rk500','generalized_rock_salt_resource_area',lambda a:dict(name=a['ROHSTOFFART'],country='Germany',commodity='NaCl',mineral='halite',spatial_precision='1:500,000',eligibility_limit='Explicit rock-salt resource areas; generalized boundaries. Depth/exposure/early accessibility absent; do not assume shallow halite.'),HALITE)
 vector('ga-coal-basins.zip','Coal_Basin_Outlines_v01_20130729/Coal_Basin_Outlines.gdb','Coal_Basin_Outlines','ga_basins_2013','broad_coal_basin_context',lambda a:dict(name=a['NAME'],country='Australia',coal_rank_source=a['Coal_Type'],source_status=a['STATUS']+' geological province; not mine status',spatial_precision='Approx1:1M capture; recommended display1:2M–1:5M',geological_province_rank=a['RANK']),CONTEXT)
 geojson('nsw-coalfields.geojson','nsw_coalfields','mapped_coalfield',lambda a:dict(name=a['Name'],country='Australia',source_reference=a['GS_Report'],spatial_precision='Generalized coalfield definitions1976; not individual seams'),source_crs='EPSG:4283')
 geojson('victoria-coalfields.geojson','victoria_coalfields','mapped_coalfield',lambda a:dict(name=a['name'],country='Australia',coal_rank_source=a['coal_type'],coal_age_source=a['coal_age'],source_reference=a['polygonsou'],spatial_precision='Source accu_loctn code '+str(a['accu_loctn'])+'; units/code meaning not inferred',source_status=a['devt'],overburden_geology=a['geol_over'],resource_description_source=a['res_class'],resource_date_source=a['qty_date']),source_crs='Provider outputEPSG:4326')
 for layer in [32,33]:
  geojson(f'ga-aimr-coal{layer}.geojson','ga_aimr_2025','resource_site',lambda a:dict(name=a['entity_id'],country='Australia',coal_rank_source=a['commodity'],source_status=a['currentstatus'],source_reference=a['source'],spatial_precision='Resource-site point; no footprint'),POINTS,source_crs='Provider service coordinates')
 for country in ['India','Bangladesh']:
  vector('india-bangladesh-coal.zip',f'{country}Coalfields/{country}Coalfields.shp',None,'usgs_india_2011','mapped_coal_bearing_area',lambda a,country=country:dict(name=a['Coalfield'],country=country,source_reference=a['DataSource'],spatial_precision='Georeferenced source maps of varying scales; some interpreted contacts'))
 vector('china-coal-areas.zip','AllChinaCoalMiningAreas.shp',None,'usgs_china_2014','mapped_coal_mining_area_2001',lambda a:dict(name=a['Province']+' coal area',country='China',coal_rank_source=a['Rank'],historical_status='Atlas coal-mining area as of 2001; no current status inferred',spatial_precision='Historical atlas georeferencing; no local mine-boundary accuracy claim'))
 vector('americas-coal.zip','Americas_coal/spatial/shape/Americas_coalply.shp',None,'usgs_americas_2008','generalized_coal_bearing_area',lambda a:dict(country=a['country'],coal_rank_source=a['rank'],coal_age_source=a['age'],source_reference=a['source'],spatial_precision='Small-scale approximate coal-bearing surficial geology'))
 vector('americas-coal.zip','Americas_coal/spatial/shape/Central_American_coalpt.shp',None,'usgs_americas_2008','coal_occurrence_point',lambda a:dict(country=a['country'],coal_rank_source=a['rank'],coal_age_source=a['age'],source_reference=a['source'],spatial_precision='Source description-derived occurrence point'),POINTS)
 vector('us-coal2012.zip','GIS/Updated Coal Fields/Coal_Fields.shp',None,'usgs_us_2012','mapped_coalfield',lambda a:dict(name=a['NAME'],country='United States',coal_rank_source=a['RANK'],coal_age_source=a['AGE'],spatial_precision='National generalized coalfield map'))
 # The2000 China archive lacks a declared CRS in the shapefile: preserve raw only.
 # The2014 published successor provides an explicit datum and has been imported above.
 for name,features in [('coal-polygons.geojson',POLYGONS),('coal-context-polygons.geojson',CONTEXT),('coal-supplement-points.geojson',POINTS),('nrw-halite.geojson',HALITE)]:
  assert len({f['id'] for f in features})==len(features)
  data=dict(type='FeatureCollection',features=features,metadata=dict(snapshot_date=DATE,meaning='Geological evidence, not continuous mineral, abundance or spawn probability',source_geometry_policy='No buffers added; no repairs or simplification; native source archives retained; reprojection only',invalid_geometry_policy='Retain and flag; map consumers must withhold invalid geometries'))
  (OUT/name).write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
  print(name,len(features),'invalid',sum(not f['properties']['geometry_valid'] for f in features),flush=True)
 (ROOT/'layer-inventory.json').write_text(json.dumps(LAYERS,indent=2,ensure_ascii=False),encoding='utf-8')
 (ROOT/'source-inventory.json').write_text(json.dumps(SOURCES,indent=2,ensure_ascii=False),encoding='utf-8')
 (ROOT/'normalization-summary.json').write_text(json.dumps(dict(polygons=len(POLYGONS),context_polygons=len(CONTEXT),points=len(POINTS),by_source=dict(Counter(f['properties']['source_id'] for f in POLYGONS)),by_kind=dict(Counter(f['properties']['feature_kind'] for f in POLYGONS)),unassessed_global_regions_remain=True,do_not_sum_overlapping_layers=True,geological_deposit_count=None),indent=2),encoding='utf-8')

if __name__=='__main__':main()
