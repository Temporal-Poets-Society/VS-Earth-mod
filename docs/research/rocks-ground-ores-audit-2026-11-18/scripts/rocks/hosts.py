import json,sys,re,collections
p=json.load(open(sys.argv[1],encoding='utf-8'))
m=collections.defaultdict(set)
for e in p:
    f=e['file'];path=e['path']
    if path.endswith('allowedVariants/-'):
        m[f.split('/')[-1].replace('.json','')+path.split('/attributes')[0]].add(e['value'])
for k,v in sorted(m.items()):print(k,sorted(v))
