"""Small reviewed cross-source joins and nonspatial facts. Never infer a deposit from proximity."""
from pathlib import Path
import json

B=Path(__file__).resolve().parent
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def write(p,data):p.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
pp=read(B/'normalized/pp1802-global.geojson')['features']
vms=read(B.parents[1]/'normalized/Ag-specialist.geojson')['features']
nui=next(f for f in pp if f['properties']['source_feature_id']=='points.588')
bol=next(f for f in vms if f['properties']['record_id']=='vms:640')
assert nui['properties']['name']=='Nui Phao' and nui['properties']['country']=='Vietnam'
assert bol['properties']['name']=='Boliden' and bol['properties']['country']=='Sweden'
enriched=[]
for old,code,name,commodities,mineral_forms,urls,proof,join in [
 (nui,'nui-phao','Nui Phao',['Bi','W','Cu','Au','F'],['fluorite'],
  ['https://pubs.usgs.gov/publication/pp1802G'],
  'USGS PP1802 chapter G explicitly identifies the named northern Vietnam deposit as tungsten-fluorspar-bismuth-copper-gold. The GIS record lists fluorite only.',
  'Exact distinctive deposit name Nui Phao and Vietnam match. Coordinate copied from PP1802 points.588, not from the province probe centre.'),
 (bol,'boliden','Boliden',['Bi'],['native bismuth','tellurobismuthite','tetradymite'],
  ['https://resource.sgu.se/produkter/c/c438-rapport.pdf','https://resource.sgu.se/dokument/publikation/c/c651rapport/c651-rapport.pdf'],
  'SGU C438 Table42 records bismuth among Boliden ore minerals. SGU C651 section7.1.10 records Bi-bearing tellurides and hand-picked tellurium-bismuth material, including material from the240m level. These facts do not establish an independent economic bismuth orebody.',
  'Exact Boliden deposit name, Sweden and Skellefte geological district match the USGS VMS record and SGU mine study. Use vms:640, not ambiguous MRDS Boliden Operations or regional phosphorus point.')]:
 p=old['properties'];enriched.append({'type':'Feature','id':'reviewed_secondary:'+code,'geometry':old['geometry'],'properties':{
  'record_id':'reviewed_secondary:'+code,'source_id':'reviewed_secondary_commodity_20260929','name':name,'country':p['country'],
  'commodities':commodities,'mineral_forms':mineral_forms,'evidence_kind':'explicit_named_secondary_commodity_cross_source_join',
  'coordinate_source_record_id':p['record_id'],'coordinate_source_url':p['source_url'],'source_url':urls[0],'evidence_source_urls':urls,
  'identity_match_basis':join,'reviewed_fact':proof,'coordinate_precision':'Original USGS regional point, no surveyed orebody footprint implied',
  'economic_status':None,'depth_m':None,'early_accessibility':None,'grade':None,'spawn_probability':None,
  'depth_note':'SGU sampling at240m is not the deposit top or full vertical extent.' if code=='boliden' else None,
  'duplicate_of_coordinate_record':p['record_id'],'count_as_new_deposit':False,
  'rights':'USGS coordinate data with attribution; geological facts paraphrased from cited primary publications; no source artwork or substantial source text reproduced.',
  'runtime_use':'hold_for_mineral_form_host_depth_and_accessibility_review'}})
write(B/'normalized/reviewed-secondary-metals.geojson',{'type':'FeatureCollection','features':enriched})

thesis='https://dspace.lu.lv/items/f9bf4048-f4d6-48fa-a19f-fe0c605bbfbe'
recent='https://konference84.lu.lv/fileadmin/user_upload/lu_portal/84konference/LU-84_konf-tezes_2026_.pdf#page=595'
depth='https://priede.bf.lu.lv/LatvijasDaba/ainava/'
records=[]
for name,reported_depth,assays in [('Staicele',688,[{'analyte':'iron oxide (source does not specify oxidation formula)','unit':'mass percent as reported','range':[7,59]},{'analyte':'manganese oxide (formula not specified)','unit':'mass percent as reported','range':[1,19]}]),('Garsene (Subate)',945,[{'analyte':'iron oxide (source does not specify oxidation formula)','unit':'mass percent as reported','range':[21,58]},{'analyte':'cobalt oxide (formula not specified)','unit':'mass percent as reported','range':[0.03,0.08]}])]:
 records.append({'type':'Feature','id':'latvia-basement:'+name.split()[0].lower(),'geometry':None,'properties':{
  'name':name,'country':'Latvia','commodities':['Fe'],'associated_elements':['Mn','Ti','Zn'] if name=='Staicele' else ['Co','Ti','Zn'],
  'evidence_kind':'drill_core_confirmed_deep_banded_iron_formation','deposit_system':'Superior-type banded iron formation, metamorphosed crystalline basement',
  'spatial_status':'Named locality only; verified borehole coordinates/orebody extent not acquired. DO NOT geocode town centroid as ore footprint.',
  'depth_m':None,'reported_depth_m':reported_depth,'reported_depth_basis':'Older University of Latvia hosted physical geography account; not a surveyed deposit-top model or claim of constant depth across deposit',
  'reported_depth_source_url':depth,'source_status':'Historical drilling; primary core research resumed2024 and ongoing2026',
  'economic_status':None,'economic_status_note':'No contemporary mineability study or certified reserve acquired. Older teaching account describes depth as uneconomic; do not convert that to absent ore.',
  'early_accessibility':'Deep buried source; no surface availability established','mineral_form':['magnetite'] if name=='Staicele' else [],
  'assays':assays,'assay_source_url':recent,'assay_scope':'Ranges from limited borehole core samples, not average body grade or reserves. Oxide fractions must not be relabelled elemental metal percentages.',
  'subeconomic_association_note':'2026 primary abstract places associated Mn at Staicele and Co at Garsene slightly below industrial concentration thresholds using sparse cores; not a separate proven mine.',
  'source_urls':[thesis,recent,depth],'source_access':'Thesis abstract and conference abstract reviewed; full thesis file requires permission and was not obtained.',
  'heatmap_eligible':False,'runtime_use':'nonspatial_evidence_hold_until_borehole_geometry_and_vertical_mapping_review','spawn_probability':None}})
write(B/'latvia-deep-evidence.geojson',{'type':'FeatureCollection','features':records})
print('Wrote2 reviewed spatial commodity joins and2 nonspatial deep-basement records.')
