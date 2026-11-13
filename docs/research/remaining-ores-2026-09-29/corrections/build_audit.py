"""Assemble an explicit row for every atlas resource, plus copper and tin."""
from pathlib import Path
from collections import Counter
import csv,json,sys
ROOT=Path(__file__).resolve().parent;ATLAS=ROOT.parent
sys.path.insert(0,str(ATLAS))
from build_mrds import COMMODITIES
from correction_layers import load_corrections

def main():
    metals=json.loads((ROOT/'metals/metal-audit.json').read_text(encoding='utf8'))
    nonmetals=json.loads((ROOT/'nonmetals/resource-audit.json').read_text(encoding='utf8'))['resources']
    overlays,metadata=load_corrections();rows=[]
    for m in metals:
        rows.append({'commodity':m['commodity'],'label':m['label'],'audit_verdict':m['verdict'],'generation_ready':False,'country_controls':m['country_checks'],'named_region_controls':m['named_province_checks'],'known_gaps':'; '.join(m['missing_country_witnesses']+m['empty_province_windows']),'interpretation_problem':m['mineral_form_and_depth_caveat'],'geological_model':m['mineral_systems'],'next_data':m['next_source'],'sources':m['source_urls'],'detail_report':'metals/README.md'})
    for n in nonmetals:
        rows.append({'commodity':n['commodity'],'label':COMMODITIES[n['commodity']][0],'audit_verdict':n['verdict'],'generation_ready':False,'country_controls':len(n['known_country_controls']),'named_region_controls':None,'known_gaps':'; '.join(c['country_control'] for c in n['known_country_controls'] if c['result'].startswith('NO_')),'interpretation_problem':n['problem'],'geological_model':n['regions'],'next_data':n['needed'],'sources':n['sources'],'detail_report':'nonmetals/README.md'})
    rows.extend([
      {'commodity':'C_coal','label':'Coal, all ranks','audit_verdict':'CORRECTED MAJOR BASINS; GLOBAL COVERAGE STILL INCOMPLETE','generation_ready':False,'country_controls':None,'named_region_controls':None,'known_gaps':'See coal/known-region-audit.json; national polygons do not cover all global coal systems.','interpretation_problem':'Coal-bearing strata, mining areas, concealed coal, coalfield boundaries, mining-workings buffers and entire sedimentary basins have different meanings.','geological_model':'Coal-bearing formations with rank, age, cover/depth where available.','next_data':'Complete remaining national coal-bearing strata and rank/depth; preserve unworked and historical resources.','sources':['https://www.bgs.ac.uk/datasets/bgs-maps-portal/','https://www.pgi.gov.pl/en/mineral-resources/maps','https://services.ga.gov.au/gis/rest/services/AustraliasIdentifiedMineralResources/MapServer'],'detail_report':'coal/README.md'},
      {'commodity':'gypsum','label':'Gypsum / anhydrite','audit_verdict':'CORRECTED REGIONAL DEPOSITS AND FORMATIONS; GLOBAL COVERAGE STILL INCOMPLETE','generation_ready':False,'country_controls':None,'named_region_controls':None,'known_gaps':'See gypsum/known-region-audit.csv; covered strata and many non-European provinces remain incomplete.','interpretation_problem':'Gypsum, anhydrite, mixed evaporitic formations and commercial quarry footprints are not interchangeable.','geological_model':'Sulfate-bearing evaporite stratigraphy, secondary hydration and cover/depth.','next_data':'Global gypsum/anhydrite-bearing units, original stratigraphy, depth and historical workings.','sources':['https://www.pgi.gov.pl/en/mineral-resources/maps','https://www.igme.es/','https://videscentrs.lvgmc.lv/'],'detail_report':'gypsum/README.md'}])
    codes={r['commodity'] for r in rows};assert len(rows)==42 and codes==set(COMMODITIES)|{'Cu','Sn'}
    order=list(COMMODITIES)+['Cu','Sn'];rows.sort(key=lambda r:order.index(r['commodity']))
    for r in rows:
        fs=overlays.get(r['commodity'],[]);r['additional_feature_links']=len(fs);r['additional_points']=sum(f['geometry']['type']=='Point' for f in fs);r['additional_areas']=len(fs)-r['additional_points']
        r['inclusion_policy']='Include known historical, closed, unworked, deep and subeconomic evidence; no current-production or reserve cutoff.'
        r['absence_policy']='Unmapped is unknown; never hard-zero, never poor grade solely because of missing evidence.'
    (ROOT/'resource-coverage-audit.json').write_text(json.dumps({'resources':rows,'resource_count':42,'all_layers_audited':True,'all_layers_generation_ready':False,'source_metadata':metadata},ensure_ascii=False,indent=2),encoding='utf8')
    with (ROOT/'resource-coverage-audit.csv').open('w',encoding='utf8',newline='') as f:
        w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader()
        for r in rows:w.writerow({k:json.dumps(v,ensure_ascii=False) if isinstance(v,list) else v for k,v in r.items()})
    print(json.dumps({'audited_resources':len(rows),'additional_feature_links':sum(r['additional_feature_links'] for r in rows),'country_controls':sum(r['country_controls'] or 0 for r in rows),'named_metal_province_controls':sum(r['named_region_controls'] or 0 for r in rows)}))
if __name__=='__main__':main()
