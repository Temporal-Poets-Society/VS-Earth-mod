import re,subprocess,sys
repo=sys.argv[1]
def consts(rev):
    out={}
    files=subprocess.run(['git','-C',repo,'ls-tree','-r','--name-only',rev,'src'],capture_output=True).stdout.decode().split('\n')
    for f in files:
        if not f.endswith('.cs'): continue
        src=subprocess.run(['git','-C',repo,'show',rev+':'+f],capture_output=True).stdout.decode('utf-8','replace')
        for m in re.finditer(r'public const int ([^;]+);',src):
            for part in m.group(1).split(','):
                mm=re.match(r'\s*(\w+)\s*=\s*(-?\d+)\s*$',part)
                if mm and re.search(r'Version|Rules|Newest|Courses|Layout',mm.group(1)): out[(f.split('/')[-1],mm.group(1))]=int(mm.group(2))
    return out
head=consts('fef6166')
for rev in sys.argv[2:]:
    c=consts(rev)
    down=[f'{k[0]}:{k[1]} {v}->{head.get(k)}' for k,v in sorted(c.items()) if k in head and head[k]<v]
    gone=[f'{k[0]}:{k[1]}={v}' for k,v in sorted(c.items()) if k not in head]
    print(rev,'constants',len(c),'| lower at HEAD:',down or 'none','| not at HEAD:',gone or 'none')
