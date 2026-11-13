"""Research overlay adapter. Never imports these layers into game generation."""
from pathlib import Path
from collections import defaultdict
import json,re
ROOT=Path(__file__).resolve().parent
LABELS={'Iron':'Fe','Gold':'Au','Silver':'Ag','Lead':'Pb','Zinc':'Zn','Bismuth':'Bi','Nickel':'Ni','Chromium':'Cr','Titanium':'Ti','Manganese':'Mn','Mercury':'Hg','Tungsten':'W','Uranium':'U','Platinum':'Pt','Platinum-group elements':'Pt','Platinum group elements':'Pt','Vanadium':'V','Molybdenum':'Mo','Antimony':'Sb','Boron':'B','Phosphate':'P','Phosphorus':'P','Potash':'K','Salt':'NaCl','Sulfur':'S','Graphite':'C_graphite','Fluorite':'F','Fluorspar':'F','Bauxite':'Al','Aluminum':'Al','Copper':'Cu','Tin':'Sn'}
LABELS={k.casefold():v for k,v in LABELS.items()}

def load_corrections():
    by_code=defaultdict(list);source_counts={};unmapped=defaultdict(int)
    registry=json.loads((ROOT/'corrections/overlay-registry.json').read_text())
    for item in registry:
        # Tracts/large bake-only inputs and licence holds are not research display overlays.
        if item.get('bakeOnly') or str(item.get('status','active')).startswith('hold'):continue
        path=ROOT/item['file']
        data=json.loads(path.read_text(encoding='utf-8-sig'))
        source_counts[item['file']]=len(data['features'])
        for i,f in enumerate(data['features']):
            p=f['properties'];g=f.get('geometry')
            if not g or p.get('geometry_valid') is False:continue
            codes=item.get('commodities') or p.get('commodities') or []
            if isinstance(codes,str):codes=[codes]
            if not codes:
                for label in re.split(r';\s*',p.get('commodity_label','')):
                    if label.casefold() in LABELS:codes.append(LABELS[label.casefold()])
                    elif label:unmapped[label]+=1
            for code in set(codes):
                category=item.get('evidence_class') or p.get('evidence_class') or p.get('evidence_kind') or 'reported_evidence'
                if p.get('gypsum_spawn_evidence_withheld') or p.get('mineral_identity')=='anhydrite':category='context_only'
                by_code[code].append({'type':'Feature','geometry':g,'properties':dict(p,overlay_file=item['file'],overlay_class=category),'id':p.get('record_id',f.get('id',str(i)))})
    return dict(by_code),{'source_features':source_counts,'unmapped_out_of_scope_labels':dict(unmapped)}
