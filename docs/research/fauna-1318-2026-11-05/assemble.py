"""Reconcile merged research into tools/fauna-1318.json (decision 0054)."""
import json,sys
W=sys.argv[1];D=sys.argv[2]
taxa=json.load(open(D+'/merged-taxa.json',encoding='utf-8'))
van=json.load(open(D+'/merged-vanilla.json',encoding='utf-8'))
notes=[]
# Domestic goat breeds and fallow deer take their 1318 lists from the feral/introduction study (range1318Regions); the tur is
# no longer Capra aegagrus. Their wild-range exclusions would only fight those lists.
for b in ('Capra aegagrus','Dama dama'):
    notes.append(f'{b}: superseded by the vanilla 1318 lists ({taxa[b]["keys"]})'); del taxa[b]
# Feral rule: pigs were kept in Britain, Ireland and Finland in 1318, so the wild boar's losses there don't remove pigs.
sus=taxa['Sus scrofa']
sus['lost1318Regions']=[c for c in sus['lost1318Regions'] if c not in ('GRB','IRE','FIN')]
sus['evidence']+=' Britain, Ireland and Finland stay: domestic pigs kept there in 1318 went feral (decision 0054).'
# Bornean elephant: its rule is Borneo only; keep the north-east (Sabah, today's range), drop the rest of the island.
bor=taxa['Elephas maximus borneensis']
bor['lost1318Regions']=[]
bor['lost1318Envelopes']=[[108,-4.5,116.3,7.5],[116.3,-4.5,119.5,4.2]]
bor['evidence']+=' Applied as boxes: Borneo outside north-eastern Sabah.'
for t in taxa.values():
    t['sources']=[s for s in t['sources'] if 'iucnredlist' not in s and 'canids.org' not in s]
    if not t.get('lost1318Envelopes'): t.pop('lost1318Envelopes',None)
policy=('Decision 0054: the "1318" range of a living mammal is its PHYLACINE 1.2.1 present-natural range minus the regions '
        '(TDWG level 3) and boxes [west, south, east, north] where it no longer lived around 1318 (roughly 1250-1400): lost to '
        'people before then, or never reached in the Holocene (PHYLACINE is counterfactual). Losses after 1318 are kept. '
        'Vanilla domestic animals went feral where people kept them in 1318: range1318Regions replaces the natural range, '
        'added1318Regions extends it. Research of 5 November 2026 (web sources per entry; no IUCN Red List pages); '
        'confidence per entry.')
out={'version':1,'policy':policy,'taxa':dict(sorted(taxa.items())),'vanilla':van['vanilla'],'turdag':van['turdag']}
open(W+'/tools/fauna-1318.json','w',encoding='utf-8',newline='\n').write(json.dumps(out,indent=1,ensure_ascii=False)+'\n')
for n in notes: print(n)
print(len(taxa),'taxa;',sum(bool(t['lost1318Regions'] or t.get('lost1318Envelopes')) for t in taxa.values()),'with losses')
