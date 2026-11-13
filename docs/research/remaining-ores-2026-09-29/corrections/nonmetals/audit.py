"""Known-region controls and source coverage audit; no abundance inference."""
from pathlib import Path
from collections import Counter,defaultdict
import csv,gzip,json,sys,re,unicodedata
ROOT=Path(__file__).resolve().parent;ATLAS=ROOT.parents[1]
sys.path.insert(0,str(ATLAS))
from correction_layers import load_corrections

def country(s):
    s=unicodedata.normalize('NFKD',s or '').encode('ascii','ignore').decode().lower().strip()
    aliases={'united states of america':'united states','usa':'united states','us':'united states','uk':'united kingdom','england':'united kingdom','russian federation':'russia','burma':'myanmar','turkiye':'turkey','turkiye (turkey)':'turkey','czech republic':'czechia','republic of south africa':'south africa'}
    return aliases.get(s,s)
def main():
    spec=json.loads((ROOT/'audit-spec.json').read_text());extra,meta=load_corrections()
    base={r['commodity']:r for r in csv.DictReader((ATLAS/'normalized/commodity-coverage.csv').open())}
    counts=defaultdict(Counter)
    for file,field in [('country-coverage.csv','source_records'),('specialist-country-coverage.csv','specialist_source_records')]:
        for r in csv.DictReader((ATLAS/'normalized'/file).open()):counts[r['commodity']][country(r['country'])]+=int(r[field])
    audit=[];countrychecks=[]
    for s in spec['resources']:
        code=s['commodity'];extra_country=Counter(country(f['properties'].get('country')) for f in extra.get(code,[]));allcountries=sorted(set(counts[code])|set(extra_country))
        checks=[]
        for control in s['countries']:
            b=counts[code][country(control)];n=extra_country[country(control)]
            checks.append({'commodity':code,'country_control':control,'baseline_source_links':b,'additional_source_links':n,'result':'NO_IMPORTED_COUNTRY_LABEL_MATCH' if b+n==0 else 'RECORD_PRESENT_NOT_COMPLETENESS_PASS'})
        countrychecks+=checks
        # Independent supplemental source is also a positive control. Report
        # countries it establishes that are absent from baseline, including
        # countries not on the hand-selected check list.
        holes=[c for c in extra_country if c and extra_country[c] and not counts[code][c]]
        row=dict(s,verdict='NOT_READY_FOR_GLOBAL_GENERATION',baseline_mrds_records=int(base[code]['source_records']),baseline_mrds_us_share=float(base[code]['us_record_share']),additional_source_links=len(extra.get(code,[])),newly_evidenced_country_labels=holes,known_country_controls=checks,global_potential_polygons_complete=False,commercial_status_not_an_exclusion=True,deep_resources_not_excluded=True)
        audit.append(row)
    (ROOT/'resource-audit.json').write_text(json.dumps({'method':spec['method'],'resources':audit},indent=2),encoding='utf8')
    with (ROOT/'known-country-controls.csv').open('w',newline='',encoding='utf8') as f:
        w=csv.DictWriter(f,fieldnames=list(countrychecks[0]));w.writeheader();w.writerows(countrychecks)
    lines=['# Nonmetal and gemstone coverage audit','','All 20 layers were checked. None is ready to define global absence or spawn probability.','',spec['method'],'','| Resource | Baseline MRDS records | Additional source links | Previously absent country labels now evidenced | Main deficiency |','|---|---:|---:|---|---|']
    for r in audit:lines.append(f"| {r['commodity']} | {r['baseline_mrds_records']:,} | {r['additional_source_links']:,} | {', '.join(r['newly_evidenced_country_labels']) or 'None identified by these supplements'} | {r['problem']} |")
    lines+=['','## Per-resource review','']
    for r in audit:
        lines += [f"### {r['commodity']}",'',f"**Check regions:** {r['regions']}. {r['problem']}",'',f"**Still needed:** {r['needed']}",'','Sources: '+', '.join(f'[source {i+1}]({u})' for i,u in enumerate(r['sources'])), '']
    lines+=['## Data interpretation','','The supplement imports 780 in-scope GEMDATA records and 1,174 DIADATA records, without requiring current mining or profitable extraction. The ambiguous MOGANITE field is withheld rather than silently classified as beryl. One DIADATA record has no resource identity and is withheld. Its duplicated original key TZ011DIA is retained as two distinct source features, with feature IDs appended. These legacy compilations have mixed gazetteer/locality precision and unreviewed runtime redistribution terms; they are research clues, not deposit masks.','', 'The Afghanistan 2002 supplement keeps unambiguous, non-starred occurrence/deposit/field rows, including small and intermittent producers. Multiple-coordinate rows remain withheld. Publisher-restricted starred rows are excluded from derived geometry. Raw primary files remain local research inputs.','', 'Geoscience Australia supplements retain both deposits and operating mines, source deposit styles and resource-size fields. Australian potash salt lakes are not sylvite/halite bedrock. Fluorine records without a mineral form are not automatically fluorite.','', 'Depth, grade, early accessibility and economic status remain unknown unless reported. The revised atlas must not turn missing records or poor survey coverage into low geological grade. No game files are changed by this audit.']
    (ROOT/'README.md').write_text('\n'.join(lines)+'\n',encoding='utf8')
    print(json.dumps({'resources':len(audit),'country_controls':len(countrychecks),'controls_without_imported_label':sum(x['result'].startswith('NO_') for x in countrychecks),'new_country_label_controls':sum(len(x['newly_evidenced_country_labels']) for x in audit)}))
if __name__=='__main__':main()
